// HsServer addition (not upstream SabberStone): "Wish for the perfect card" (Zephrys the Great, Zephrys's Lamp).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>What a wished card does for the player (wiki Zephrys the Great: "Cards are broken down into categories";
		/// lethal first, then area of effect, single-target removal, healing when low, proactive cards, burst).</summary>
		public enum WishCategory { Lethal, LethalNextTurn, BoardClear, Removal, Survival, Counter, Proactive, Burst }

		private static readonly Dictionary<WishCategory, double> WishWeight = new Dictionary<WishCategory, double>
		{
			[WishCategory.Lethal] = 1, [WishCategory.LethalNextTurn] = 1, [WishCategory.BoardClear] = 1.3, [WishCategory.Removal] = 1.2, [WishCategory.Survival] = 1.2,
			[WishCategory.Counter] = 1.0, [WishCategory.Proactive] = 1.0, [WishCategory.Burst] = 0.6,
		};

		private static List<Card> _wishPool;
		private static HashSet<string> _hallOfFame;

		/// <summary>
		/// HsServer: the cards a wish looks at (wiki Zephrys the Great: "The cards Zephrys can generate from the Legacy set are
		/// based on cards that were in the Basic and Classic sets"; not "Any card that was ever in the Hall of Fame", not "Any
		/// rotated Core-exclusive or Event set-exclusive card that has become part of Legacy", not the return-to-hand cards
		/// "Shadowstep, Youthful Brewmaster, and Ancient Brewmaster", not "Uninteresting" cards "like Bloodfen Raptor"; offers
		/// from any class). Client data: a collectible card with a Basic (2) or Classic (3) CardSetTiming row; the Hall of Fame
		/// is its HoF (4) row or the recorded list (HallOfFame.tsv); a return-to-hand card says "Return a friendly minion";
		/// an uninteresting card is a minion (or hero: the starting heroes) with no text.
		/// </summary>
		public static List<Card> WishPool => _wishPool ??= Cards.All.Where(InWishPool).OrderBy(x => x.AssetId).ToList();

		private static bool InWishPool(Card card)
		{
			if (!card.Collectible || card.Type == CardType.ENCHANTMENT) return false;
			HashSet<int> sets = Loader.ReprintCards.SetsOf(card.AssetId);
			bool basicOrClassic = sets != null ? sets.Contains(2) || sets.Contains(3) : card.Set == CardSet.CORE || card.Set == CardSet.EXPERT1;
			if (!basicOrClassic || sets?.Contains(4) == true || card.Set == CardSet.HOF || HallOfFame.Contains(card.Id)) return false;
			string text = Regex.Replace(card.Text ?? "", "<[^>]+>|\\[x\\]", "").Trim();
			if ((card.Type == CardType.MINION || card.Type == CardType.HERO) && text.Length == 0) return false; // (and the starting heroes)
			return !text.Contains("Return a friendly minion");
		}

		private static HashSet<string> HallOfFame => _hallOfFame ??= IdTable("HallOfFame.tsv");

		private static HashSet<string> _notForLethal;

		/// <summary>Cards a wish never offers as lethal (wiki: "does NOT use" "Tundra Rhino, Timber Wolf, Blessing of Might,
		/// Sinister Strike"; resources/Data/WishNotForLethal.tsv).</summary>
		private static HashSet<string> NotForLethal => _notForLethal ??= IdTable("WishNotForLethal.tsv");

		private static HashSet<string> IdTable(string resource)
		{
			var set = new HashSet<string>();
			Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SabberStoneCore.Resources." + resource);
			if (stream != null)
				using (var reader = new StreamReader(stream))
				{
					string line;
					while ((line = reader.ReadLine()) != null)
						if (line.Length > 0 && line[0] != '#') set.Add(line.Split('\t')[0]);
				}
			return set;
		}

		/// <summary>The public state a wish reads (wiki: "board state, your available mana after playing Zephrys, your
		/// available mana for the next turn, both players' health totals, ... hand sizes, Secrets count, weapons"; never the
		/// cards in hand).</summary>
		private sealed class WishState
		{
			public bool MyDead, OppDead;
			public int MyLife, OppLife, Reach, MyHand, OppHand, OppSecrets, OppWeapon, OppAttack, MyWeapon, MyMana, Ramp;
			public bool LethalWithPower; // the Hero Power finishes it after the card (Basic / Upgraded powers: wiki)
			public readonly Dictionary<int, int> Mine = new Dictionary<int, int>(), Theirs = new Dictionary<int, int>();
			public readonly HashSet<int> MyTaunts = new HashSet<int>();
		}

		private static int WishValue(Minion m)
		{
			int attack = m.CantAttack ? 0 : Math.Max(0, m.AttackDamage); // ("Can't attack.": Ancient Watcher is a wall)
			return attack + Math.Max(0, m.Health) + (m.HasTaunt ? 1 : 0) + (m.HasDivineShield ? attack : 0);
		}

		private static WishState WishSnapshot(Controller me)
		{
			Controller opp = me.Opponent;
			var s = new WishState
			{
				MyDead = me.Hero.IsDead, OppDead = opp.Hero.IsDead,
				MyLife = me.Hero.Health + me.Hero.Armor,
				OppLife = opp.Hero.IsImmune ? 999 : opp.Hero.Health + opp.Hero.Armor,
				MyHand = me.HandZone.Count, OppHand = opp.HandZone.Count, OppSecrets = SecretsOf(opp).Count, MyMana = me.BaseMana,
				OppWeapon = opp.Hero.Weapon == null ? 0 : opp.Hero.Weapon.AttackDamage + opp.Hero.Weapon.Durability,
				OppAttack = opp.Hero.Weapon?.AttackDamage ?? 0,
				MyWeapon = me.Hero.Weapon == null ? 0 : me.Hero.Weapon.AttackDamage + me.Hero.Weapon.Durability,
			};
			bool taunted = false;
			foreach (Minion m in opp.BoardZone)
			{
				if (m.IsDead) continue;
				s.Theirs[m.Id] = WishValue(m);
				if (!m.IsFrozen) s.OppAttack += m.AttackDamage; // what can hit the player next turn
				taunted |= m.HasTaunt && !m.HasStealth;
			}
			foreach (Minion m in me.BoardZone)
			{
				if (m.IsDead) continue;
				s.Mine[m.Id] = WishValue(m);
				if (m.HasTaunt) s.MyTaunts.Add(m.Id);
				// what the board can still hit the enemy hero for this turn ("only spotted if lethal is already set up")
				if (!taunted && m.CanAttack && !m.CantAttackHeroes)
					s.Reach += m.AttackDamage * Math.Max(1, (m.HasWindfury ? 2 : 1) - m.NumAttacksThisTurn);
			}
			if (!taunted && me.Hero.CanAttack && !me.Hero.CantAttackHeroes)
				s.Reach += me.Hero.AttackDamage;
			return s;
		}

		/// <summary>What one play did, per category (the higher the better; ≤ 0 = nothing for that category).</summary>
		private static Dictionary<WishCategory, double> WishScore(WishState before, WishState after, bool thisTurn, bool low,
			bool alreadyLethal, bool forLethal)
		{
			var score = new Dictionary<WishCategory, double>();
			if (after.MyDead) return score;
			// lethal now: the enemy hero dies, or the board (frozen and "can't attack" minions don't count, Taunt blocks) and
			// the Hero Power now reach it (wiki: "always offer cards to allow the player to gain lethal"; not when the player
			// already has it)
			bool lethal = after.OppDead || after.Reach >= after.OppLife || after.LethalWithPower;
			if (thisTurn && forLethal && !alreadyLethal && lethal)
				score[WishCategory.Lethal] = 1000;
			// lethal next turn: a card the player can play next turn that kills by itself (wiki: Pyroblast at 10 Health or less)
			if (!thisTurn && forLethal && after.OppDead)
				score[WishCategory.LethalNextTurn] = 500;
			int hits = 0;
			double removed = 0, myLoss = 0, myGain = 0, newTaunt = 0;
			foreach (var (id, value) in before.Theirs)
			{
				int now = after.Theirs.TryGetValue(id, out int v) ? v : 0;
				if (value - now <= 0) continue;
				removed += value - now;
				if (value - now >= Math.Max(2, value * 0.4)) hits++;
			}
			removed -= after.Theirs.Where(x => !before.Theirs.ContainsKey(x.Key)).Sum(x => x.Value); // what they get back (Deathrattles)
			foreach (var (id, value) in before.Mine)
			{
				int now = after.Mine.TryGetValue(id, out int v) ? v : 0;
				if (now < value) myLoss += value - now; else myGain += now - value;
			}
			foreach (var (id, value) in after.Mine.Where(x => !before.Mine.ContainsKey(x.Key)))
			{
				myGain += value;
				if (after.MyTaunts.Contains(id)) newTaunt += value;
			}
			int lifeChange = after.MyLife - before.MyLife;
			double selfHarm = lifeChange < 0 ? -lifeChange * (low ? 2 : 0.5) : 0;
			// cards given to the opponent (King Mukla: wiki, only "mill opponent or full hand") count against it
			selfHarm += 2 * Math.Max(0, after.OppHand - before.OppHand);
			if (hits >= 2) score[WishCategory.BoardClear] = removed - myLoss - selfHarm;
			else if (hits == 1 && thisTurn) score[WishCategory.Removal] = removed - myLoss - selfHarm; // (wiki: no single-target removal for next turn)
			double counter = (before.OppWeapon - after.OppWeapon) + 4 * (before.OppSecrets - after.OppSecrets);
			if (counter > 0) score[WishCategory.Counter] = counter - selfHarm;
			int threatCut = before.OppAttack - after.OppAttack; // frozen or gone attackers
			if (low && thisTurn && (lifeChange > 0 || newTaunt > 0 || threatCut > 0)) // (next turn is too late to survive this one)
				score[WishCategory.Survival] = 1.5 * Math.Max(0, lifeChange) + newTaunt + Math.Max(0, threatCut) - selfHarm;
			int drawn = after.MyHand - before.MyHand;
			// Mana Crystals gained for good (Wild Growth): they pay every turn after, so worth most early (user 2026-10-06:
			// Zephrys coined out on turn 1 against an empty board often offers Wild Growth)
			double ramp = after.Ramp * 2 * Math.Max(1, 10 - before.MyMana);
			double proactive = myGain - myLoss + Math.Max(0, after.MyWeapon - before.MyWeapon) + 2 * drawn + ramp - selfHarm;
			if (proactive > 0) score[WishCategory.Proactive] = proactive;
			int burst = before.OppLife - after.OppLife + Math.Max(0, after.Reach - before.Reach);
			if (burst > 0 && !low) score[WishCategory.Burst] = burst - selfHarm; // (wiki: burst "only if opponent board not threatening")
			// among lethal cards, the one that also does the most (wiki: Silence "favored among lethal options")
			double extra = score.Where(x => x.Key != WishCategory.Lethal && x.Key != WishCategory.LethalNextTurn).Select(x => x.Value).DefaultIfEmpty(0).Max();
			if (score.ContainsKey(WishCategory.Lethal)) score[WishCategory.Lethal] += Math.Max(0, extra);
			if (score.ContainsKey(WishCategory.LethalNextTurn)) score[WishCategory.LethalNextTurn] += Math.Max(0, extra);
			return score;
		}

		/// <summary>A copy of the game with <paramref name="card"/> in the player's hand (and next turn's mana when asked).</summary>
		private static (Game Sim, Controller Me, IPlayable Card) WishSim(Game g, Controller c, Card card, bool nextTurn, long seed)
		{
			Game sim = g.Clone();
			sim.Random = new Util.DeepCloneableRandom(seed);
			Controller me = sim.ControllerById(c.Id);
			if (nextTurn)
			{
				me.BaseMana = Math.Min(10, c.BaseMana + 1);
				me.TemporaryMana = 0;
				me.UsedMana = Math.Min(me.BaseMana, c.OverloadOwed);
			}
			IPlayable p = Entity.FromCard(me, card, zone: me.HandZone);
			return (sim, me, p);
		}

		private static void WishResolveChoices(Game sim, Controller me)
		{
			for (int i = 0; i < 3 && me.Choice != null && me.Choice.Choices.Count > 0; i++)
				sim.Process(ChooseTask.Pick(me, me.Choice.Choices[0]));
		}

		/// <summary>
		/// HsServer: "wish for the perfect card" (wiki Zephrys the Great). Every pool card the player can play this turn or
		/// next ("Zephrys will only offer cards that the player can play on the same or following turn") is played on a copy
		/// of the game at each of its targets (and Choose One options); what it did sorts it into a category. The three best
		/// categories give one card each ("the best cards from the three best categories"); a card that gives lethal now is
		/// always one of them ("Zephrys will always offer cards to grant the player lethal if possible in the current turn").
		/// A random card is judged by its worst of three tries (wiki: pessimistic about cards like Brawl). Fewer than three
		/// good categories: the next best cards. The offer goes to the hand.
		/// </summary>
		public static void WishForThePerfectCard(Game g, Controller c, IEntity source)
		{
			if (c.HandZone.IsFull) return;
			List<Card> offer = WishOffer(g, c);
			if (offer.Count > 0)
				CreateChoiceCards.Invoke(c, source, null, ChoiceType.GENERAL, ChoiceAction.HAND, offer.ToArray(), null);
		}

		/// <summary>The three cards a wish offers now (<see cref="WishForThePerfectCard"/>).</summary>
		/// <summary>Judge the wish's cards in parallel (each on its own game copies); false: one by one (debugging).</summary>
		public static bool WishParallel = true;

		public static List<Card> WishOffer(Game g, Controller c)
		{
			WishState before = WishSnapshot(c);
			bool low = before.MyLife <= 15 || before.MyLife <= 2 * before.OppAttack;
			int nextMana = Math.Min(10, c.BaseMana + 1) - c.OverloadOwed;
			// wiki: "Zephrys is deterministic — same board state produces same three choices": fixed tries, the pool's order
			var seeds = new[] { 1, 2, 3 };
			List<Card> pool = WishPool;
			var best = new List<(Card Card, WishCategory Category, double Score)>();
			if (c != g.CurrentPlayer || g.Step != Step.MAIN_ACTION)
				return pool.Where(x => x.Cost <= nextMana).Take(3).ToList(); // not the player's turn: nothing to try the cards on
			bool alreadyLethal = before.OppDead || before.Reach >= before.OppLife || HeroPowerLethal(g.Clone(), c.Id);

			// (2026-10-09, user: "playing Zephrys is very slow" — 6 s on a full board, 370 cards × targets × tries) every card is tried on its own
			// copies of the game, so the cards are judged in parallel; the result is put back in the pool's order (deterministic: wiki
			// "same board state produces same three choices"). WishParallel = false: one by one.
			var judged = new (WishCategory Category, double Score)?[pool.Count];
			void Judge(int index)
			{
				Card card = pool[index];
				if (card.Cost > Math.Max(c.RemainingMana, nextMana) + 3) return; // (a card's Cost can change in hand; checked below)
				(WishCategory Category, double Score)? top = null;
				foreach (bool nextTurn in new[] { false, true })
				{
					List<(int Target, int ChooseOne)> options;
					try
					{
						var (sim, me, p) = WishSim(g, c, card, nextTurn, seeds[0]);
						options = me.Options().OfType<PlayCardTask>().Where(o => o.Source == p)
							.Select(o => (o.Target?.Id ?? 0, o.ChooseOne)).Distinct().ToList();
					}
					catch (Exception) { options = new List<(int, int)>(); }
					foreach (var (target, chooseOne) in options)
					{
						var scores = WishTry(g, c, card, nextTurn, target, chooseOne, seeds, before, low, alreadyLethal);
						foreach (var (category, value) in scores)
						{
							double weighted = value * WishWeight[category] * (nextTurn ? 0.8 : 1);
							if (top == null || weighted > top.Value.Score) top = (category, weighted);
						}
					}
					if (options.Count > 0) break; // playable this turn: not judged as a next-turn card
				}
				judged[index] = top;
			}
			if (WishParallel)
				System.Threading.Tasks.Parallel.For(0, pool.Count, Judge);
			else
				for (int i = 0; i < pool.Count; i++) Judge(i);
			for (int i = 0; i < pool.Count; i++)
				if (judged[i] is { } top && top.Score > 0) best.Add((pool[i], top.Category, top.Score));

			var picks = best.GroupBy(x => x.Category).Select(x => x.OrderByDescending(y => y.Score).First())
				.OrderByDescending(x => x.Score).Take(3).Select(x => x.Card).ToList(); // (lethal scores above everything)
			foreach (var next in best.OrderByDescending(x => x.Score))
				if (picks.Count < 3 && !picks.Contains(next.Card)) picks.Add(next.Card);
			foreach (Card card in pool) // nothing good enough: cards the player can play
				if (picks.Count < 3 && !picks.Contains(card) && card.Cost <= nextMana) picks.Add(card);
			return picks;
		}

		/// <summary>One play on copies of the game, worst of the tries when the outcome is random.</summary>
		private static Dictionary<WishCategory, double> WishTry(Game g, Controller c, Card card, bool nextTurn, int target, int chooseOne,
			int[] seeds, WishState before, bool low, bool alreadyLethal)
		{
			Dictionary<WishCategory, double> worst = null;
			WishState first = null;
			for (int i = 0; i < seeds.Length; i++)
			{
				WishState after;
				try
				{
					var (sim, me, p) = WishSim(g, c, card, nextTurn, seeds[i]);
					ICharacter t = target == 0 ? null : sim.IdEntityDic[target] as ICharacter;
					int startMana = me.BaseMana;
					if (!sim.Process(PlayCardTask.Any(me, p, t, -1, chooseOne))) return new Dictionary<WishCategory, double>();
					WishResolveChoices(sim, me);
					after = WishSnapshot(me);
					after.Ramp = Math.Max(0, me.BaseMana - startMana);
					if (!nextTurn && !after.OppDead && after.Reach < after.OppLife)
						after.LethalWithPower = HeroPowerLethal(sim, me.Id);
				}
				catch (Exception) { return new Dictionary<WishCategory, double>(); }
				var scores = WishScore(before, after, !nextTurn, low, alreadyLethal, !NotForLethal.Contains(card.Id));
				if (worst == null) worst = scores;
				else
					foreach (WishCategory k in worst.Keys.ToList())
						worst[k] = scores.TryGetValue(k, out double v) ? Math.Min(worst[k], v) : Math.Min(worst[k], 0);
				if (i == 0) first = after;
				else if (i == 1 && SameOutcome(first, after)) break; // no randomness: one try is enough
			}
			return worst;
		}

		/// <summary>Does the player's Hero Power (at the enemy hero, or untargeted) finish the enemy now, with the board's
		/// attacks? Tried on <paramref name="sim"/> (changed).</summary>
		private static bool HeroPowerLethal(Game sim, int controllerId)
		{
			try
			{
				Controller me = sim.ControllerById(controllerId);
				foreach (HeroPowerTask power in me.Options().OfType<HeroPowerTask>())
					if (power.Target == null || power.Target.Id == me.Opponent.Hero.Id)
					{
						sim.Process(power);
						WishState now = WishSnapshot(me);
						return now.OppDead || now.Reach >= now.OppLife;
					}
			}
			catch (Exception) { }
			return false;
		}

		private static bool SameOutcome(WishState a, WishState b) =>
			a.MyLife == b.MyLife && a.OppLife == b.OppLife && a.MyHand == b.MyHand && a.Reach == b.Reach &&
			a.Mine.Count == b.Mine.Count && a.Theirs.Count == b.Theirs.Count &&
			a.Mine.Values.Sum() == b.Mine.Values.Sum() && a.Theirs.Values.Sum() == b.Theirs.Values.Sum();
	}
}
