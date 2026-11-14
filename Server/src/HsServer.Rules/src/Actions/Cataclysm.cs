// HsServer addition (not upstream SabberStone): CATACLYSM (set 1980) hand rules — Twisted Monstrosity's keyword swap, Genn's
// transform-in-hand condition, Sabotage!'s neighbour cost. Herald: Actions/Herald.cs; Shatter: Actions/Shatter.cs. Tags: Enums/HsTag.cs
// 990700-990749.
using System;
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.SimpleTasks;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>Client tags DYNAMIC_KEYWORD1 / 2 (4161 / 4162): the keyword tag ids the card's "{0}, {1}" text shows (Twisted
		/// Monstrosity; the client's DynamicKeywordCardTextBuilder).</summary>
		public const GameTag DYNAMIC_KEYWORD1 = (GameTag)4161, DYNAMIC_KEYWORD2 = (GameTag)4162;

		/// <summary>A Bonus Effect keyword taken away (the opposite of GiveBonusEffect).</summary>
		public static void ClearBonusEffect(Minion m, GameTag keyword)
		{
			if (keyword == HsTag.ELUSIVE)
			{
				m[HsTag.ELUSIVE] = 0;
				m[GameTag.CANT_BE_TARGETED_BY_SPELLS] = 0;
				m[GameTag.CANT_BE_TARGETED_BY_HERO_POWERS] = 0;
				return;
			}
			if (keyword == GameTag.RUSH)
			{
				m.IsRush = false;
				return;
			}
			m[keyword] = 0;
		}

		/// <summary>
		/// Twisted Monstrosity — "Each turn this is in your hand, swap between two random Bonus Effects" (wiki: "Each turn, those two
		/// keywords are swapped with two random Bonus Effect keywords"; "can roll into the same keywords multiple turns in a row"). Live
		/// (204312 36407, 37743, 38914, 42025, 43959): at the end of its owner's turn (Harmonic's point), a TRIGGER block on the card
		/// (effect index -1, ShowInHistory), ReuseFX_Shadow_HandBuff_ScaleUp_Super from the hero on it: the old keywords off, two different
		/// new ones on, DYNAMIC_KEYWORD1 / 2 = their tag ids. Data-driven: any hand card with DYNAMIC_KEYWORD1.
		/// </summary>
		public static void SwapDynamicKeywords(Controller c, Minion m)
		{
			Game g = c.Game;
			var pool = BonusEffectKeywords.ToList();
			GameTag a = pool[g.Random.Next(pool.Count)];
			pool.Remove(a);
			GameTag b = pool[g.Random.Next(pool.Count)];
			if (g.History)
			{
				var block = Kettle.PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, m.Id, "", -1, 0);
				block.ShowInHistory = true;
				g.PowerHistory.Add(block);
			}
			bool fx = g.History && g.OpenSubSpell(m, Loader.SubSpells.FindNamed(m.Card.Id, "HandBuff_ScaleUp"), new[] { m.Id });
			foreach (GameTag old in new[] { (GameTag)m[DYNAMIC_KEYWORD1], (GameTag)m[DYNAMIC_KEYWORD2] })
				if (old != 0) ClearBonusEffect(m, old);
			GiveBonusEffect(m, a);
			GiveBonusEffect(m, b);
			if (a == HsTag.ELUSIVE || b == HsTag.ELUSIVE) m[HsTag.ELUSIVE] = 1; // (the keyword shown on the card in hand)
			m[DYNAMIC_KEYWORD1] = (int)a;
			m[DYNAMIC_KEYWORD2] = (int)b;
			if (fx) g.CloseSubSpells(m.Id);
			if (g.History) g.PowerHistory.Add(Kettle.PowerHistoryBuilder.BlockEnd());
		}

		/// <summary>"While holding this, if …, transform into …" (Power.HandTransform: Genn, Cursed King) — checked after each sequence
		/// (wiki Genn patch 35.0.0: "transformation could fail after cards had their costs changed" — current Costs); one-way; the card's
		/// trigger effect 0 (its CardDef CATAFX_GennCursedKing_CardInHand_Transform_Super).</summary>
		internal static void HandTransforms(Game g)
		{
			if (g.Step == Step.BEGIN_MULLIGAN || g.State != State.RUNNING) return;
			foreach (Controller c in new[] { g.Player1, g.Player2 })
				foreach (IPlayable card in c.HandZone.GetAll().ToList())
					if (card.Card.Power?.HandTransform is { } rule && rule.When(card) && Cards.TryFromId(rule.Into) is Card into)
						ChangeInHand(c, card, into, 0);
		}

		/// <summary>"Cards adjacent to this in hand cost (1) more" (Power.AdjacentInHand: Sabotage!): each card next to such a card in its
		/// owner's hand carries its enchantment (made by it) — one per such neighbour — and loses it when they part.</summary>
		internal static void SyncAdjacentInHand(Game g)
		{
			if (g.State != State.RUNNING) return;
			foreach (Controller c in new[] { g.Player1, g.Player2 })
			{
				var hand = c.HandZone.GetAll().ToList();
				if (!hand.Any(p => p.Card.Power?.AdjacentInHand != null) && !hand.Any(p => p.AppliedEnchantments?.Any(e => e.Creator?.Card.Power?.AdjacentInHand == e.Card.Id) == true))
					continue;
				for (int i = 0; i < hand.Count; i++)
				{
					IPlayable card = hand[i];
					var wanted = new List<IPlayable>();
					foreach (int j in new[] { i - 1, i + 1 })
						if (j >= 0 && j < hand.Count && hand[j].Card.Power?.AdjacentInHand != null) wanted.Add(hand[j]);
					var have = card.AppliedEnchantments?.Where(e => e.Creator?.Card.Power?.AdjacentInHand == e.Card.Id).ToList() ?? new List<Enchantment>();
					foreach (Enchantment e in have.Where(e => !wanted.Any(w => w.Id == e.Creator.Id)).ToList())
						RemoveEnchantmentTask.Task.Process(g, c, e, null);
					foreach (IPlayable source in wanted)
						if (!have.Any(e => e.Creator.Id == source.Id) && Cards.TryFromId(source.Card.Power.AdjacentInHand) is Card ench)
							AddEnchantmentBlock(g, ench, source, card, 0, 0, 0);
				}
			}
		}
		// =========================================================================================================== mana spent

		/// <summary>Client player tag 4587: the damage the player dealt with spells this turn (Spellweaver's Brilliance, Unstable
		/// Spellcaster, Raincaller; live: every Spell source's damage, reset at the turn change).</summary>
		public const GameTag SPELL_DAMAGE_THIS_TURN = (GameTag)4587;

		/// <summary>
		/// The player spent <paramref name="amount"/> Mana (Controller.TotalManaSpentThisGame: cards, Hero Powers, Trades, Forges, "Spend
		/// all your Mana" through SpendAllMana): "If you spent N Mana while holding this" cards in hand count down (Power.ManaSpentWhileHeld;
		/// not the card being paid for — it isn't held, it is played); "Whenever you spend your last Mana Crystal" (Power.LastManaSpentTask)
		/// runs on the minions in play when no Mana is left (wiki Crystalspine Cub: "this minion cannot trigger itself when played").
		/// </summary>
		public static void ManaSpent(Controller c, int amount)
		{
			Game g = c.Game;
			if (g == null || amount <= 0) return;
			// (2026-10-08, live 012951 93875-95181 Felwood Treant: each spend is a TRIGGER block on the held card at index 1 in the play's
			// trigger phase — after the paying card's pause, before its POWER block — with NUM_1 = what's left; at 0 also NUM_2 = 1, then
			// USE_ALTERNATE_CARD_TEXT 1)
			foreach (IPlayable card in c.HandZone.GetAll())
			{
				if (card.Card.Power?.ManaSpentWhileHeld != true || card.Id == g.PayingFor || card[GameTag.TAG_SCRIPT_DATA_NUM_1] <= 0) continue;
				int spent = amount;
				g.TaskQueue.EnqueueTriggerBlock(new CustomTask((g2, c2, s2, t2, st2) =>
				{
					if (!(s2 is IPlayable held) || held.Zone?.Type != Zone.HAND || held[GameTag.TAG_SCRIPT_DATA_NUM_1] <= 0) return;
					int left = Math.Max(0, held[GameTag.TAG_SCRIPT_DATA_NUM_1] - spent);
					held[GameTag.TAG_SCRIPT_DATA_NUM_1] = left;
					if (left > 0) return;
					held[GameTag.TAG_SCRIPT_DATA_NUM_2] = 1;
					held[GameTag.USE_ALTERNATE_CARD_TEXT] = 1;
				}), c, card, 1);
			}
			if (c.ManaToSpend == 0)
				foreach (Minion m in c.BoardZone.GetAll().Where(m => !m.IsSilenced && !m.IsDead && m.Card.Power?.LastManaSpentTask != null).ToList())
					g.TaskQueue.Enqueue(m.Card.Power.LastManaSpentTask, c, m, null, (int)GameTag.TRIGGER_VISUAL);
		}

		/// <summary>"Spend all your Mana" as a real spend (Mossbinding): the Mana left is used and counts as spent. Returns how much.</summary>
		public static int SpendAllMana(Controller c)
		{
			int left = Math.Max(0, c.RemainingMana);
			if (left == 0) return 0;
			c.UsedMana += left;
			c.TotalManaSpentThisGame += left;
			return left;
		}

		/// <summary>Is a "spent N Mana while holding this" card ready (its count down to 0)?</summary>
		public static bool ManaSpentReady(IEntity card) => card != null && card[GameTag.TAG_SCRIPT_DATA_NUM_1] <= 0;

		// =========================================================================================================== Magmaw

		/// <summary>"Summon any leftover appendages when there is room." (Magmaw: Colossal +99; its TAG_SCRIPT_DATA_NUM_1 = the ones left).</summary>
		public static bool LeftoverAppendages(Card body) => body?.Text?.Contains("leftover appendages") == true;

		/// <summary>
		/// Magmaw (wiki: "keeps summoning the appendages while there is room … When another friendly minion dies, this will summon another
		/// appendage in its place … until Magmaw has summoned up to 99 appendages or until it leaves the battlefield"). Live vfx
		/// 218304-220160: a TRIGGER block on it (effect index 0, TRIGGER_VISUAL) summons them to its right in id order, cycling, its
		/// TAG_SCRIPT_DATA_NUM_1 one less each. (The freed slot of a later refill: ours to its right — doubt.)
		/// </summary>
		internal static void SummonLeftovers(Game g, Minion body)
		{
			List<Card> limbs = ColossalLimbs(body.Card);
			if (limbs.Count == 0 || body.Controller.BoardZone.IsFull || body[GameTag.TAG_SCRIPT_DATA_NUM_1] <= 0) return;
			if (g.History)
			{
				var block = Kettle.PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, body.Id, "", 0, 0);
				block.ShowInHistory = true;
				g.PowerHistory.Add(block);
			}
			int total = ColossalCount(body.Card);
			int slot = body.ZonePosition + 1;
			while (!body.Controller.BoardZone.IsFull && body[GameTag.TAG_SCRIPT_DATA_NUM_1] > 0 && body.Zone?.Type == Zone.PLAY)
			{
				int made = total - body[GameTag.TAG_SCRIPT_DATA_NUM_1];
				Minion m = HeraldScaled(body.Controller, limbs[made % limbs.Count]);
				SummonBlock.Invoke(g, m, Math.Min(slot, body.Controller.BoardZone.Count), body);
				if (m.Zone?.Type != Zone.PLAY) break;
				slot = m.ZonePosition + 1;
				body[GameTag.TAG_SCRIPT_DATA_NUM_1] = body[GameTag.TAG_SCRIPT_DATA_NUM_1] - 1;
			}
			if (g.History) g.PowerHistory.Add(Kettle.PowerHistoryBuilder.BlockEnd());
		}

		/// <summary>After each sequence: a Magmaw with room and appendages left summons them.</summary>
		internal static void SummonLeftoverAppendages(Game g)
		{
			if (g.State != State.RUNNING) return;
			foreach (Controller c in new[] { g.Player1, g.Player2 })
				foreach (Minion body in c.BoardZone.GetAll().Where(m => !m.IsSilenced && !m.IsDead && !m.ToBeDestroyed && LeftoverAppendages(m.Card)).ToList())
					SummonLeftovers(g, body);
		}

		// =========================================================================================================== Leylines

		/// <summary>Client tags: a Leyline card (4285); player: the Leylines' extra triggers (4283) and their effect increase (4288).</summary>
		public const GameTag LEYLINE = (GameTag)4285, LEYLINE_EXTRA_TRIGGERS = (GameTag)4283, LEYLINE_EFFECT_INCREASE = (GameTag)4288;

		/// <summary>
		/// The Leylines' numbers (live darkmoon 57545-59864, vfx 15481-20164): TAG_SCRIPT_DATA_NUM_1 = the card's number + the player's
		/// effect increase (4288), TAG_SCRIPT_DATA_NUM_2 = 1 + the player's extra triggers (4283), USE_ALTERNATE_CARD_TEXT 1 ("… {1} times")
		/// while there are extra triggers — on the player's Leylines in hand and deck, after each sequence.
		/// </summary>
		internal static void SyncLeylines(Game g)
		{
			if (g.State != State.RUNNING) return;
			foreach (Controller c in new[] { g.Player1, g.Player2 })
				foreach (IPlayable card in c.HandZone.GetAll().Concat(c.DeckZone.GetAll()))
				{
					if (card.Card[LEYLINE] == 0) continue;
					int n1 = card.Card[GameTag.TAG_SCRIPT_DATA_NUM_1] + c[LEYLINE_EFFECT_INCREASE], n2 = 1 + c[LEYLINE_EXTRA_TRIGGERS];
					int alt = c[LEYLINE_EXTRA_TRIGGERS] > 0 ? 1 : 0;
					if (card[GameTag.TAG_SCRIPT_DATA_NUM_1] != n1) card[GameTag.TAG_SCRIPT_DATA_NUM_1] = n1;
					if (card[GameTag.TAG_SCRIPT_DATA_NUM_2] != n2) card[GameTag.TAG_SCRIPT_DATA_NUM_2] = n2;
					if (card[GameTag.USE_ALTERNATE_CARD_TEXT] != alt) card[GameTag.USE_ALTERNATE_CARD_TEXT] = alt;
				}
		}

		/// <summary>A Leyline's runs and amount when played (its numbers as the player's state makes them now).</summary>
		public static (int Times, int Amount) LeylineNumbers(Controller c, IPlayable card) =>
			(1 + c[LEYLINE_EXTRA_TRIGGERS], card.Card[GameTag.TAG_SCRIPT_DATA_NUM_1] + c[LEYLINE_EFFECT_INCREASE]);

		// =========================================================================================================== Animal Companions

		/// <summary>The default Animal Companions (Huffer, Leokk, Misha — Animal Companion's client RelatedCards).</summary>
		private static readonly string[] DefaultCompanions = { "NEW1_034", "NEW1_033", "NEW1_032" };

		/// <summary>The player's three Animal Companions now (wiki Tame Pet: "replaces the player's three Animal Companions with three
		/// random beasts … for any card that generates them").</summary>
		public static List<Card> AnimalCompanions(Controller c)
		{
			if (c[HsTag.COMPANION_1] > 0)
				return new[] { c[HsTag.COMPANION_1], c[HsTag.COMPANION_2], c[HsTag.COMPANION_3] }.Select(Cards.FromAssetId).Where(x => x != null).ToList();
			return DefaultCompanions.Select(Cards.TryFromId).Where(x => x != null).ToList();
		}

		/// <summary>
		/// "Replace your future Animal Companions with random Beasts that cost (N) more" (Tame Pet, Migrating Elekk, Roam Free; wiki: "As the
		/// normal Animal Companions cost 3 mana, they will be upgraded from 3 mana upwards"; locked until the next upgrade). Three different
		/// random Beasts (any class) of the new Cost. Live (165219 4341): the new three set aside, HISTORY_TARGET each,
		/// CATAFX_TamePet_Reveal_Fadeout_Super from the card on the hero.
		/// </summary>
		public static void UpgradeCompanions(Controller c, IEntity source, int by)
		{
			Game g = c.Game;
			int cost = AnimalCompanions(c).Select(x => x.Cost).DefaultIfEmpty(3).Max() + by;
			var pool = Cards.GenerationPool(g, c).Where(x => x.Type == CardType.MINION && x.IsRace(Race.BEAST) && !x.NeverRandomlyOnBoard).ToList();
			while (cost > 0 && pool.Count(x => x.Cost == cost) < 3) cost--; // (none that expensive: the highest that has three)
			var picks = pool.Where(x => x.Cost == cost).OrderBy(_ => g.Random.Next()).Take(3).ToList();
			if (picks.Count < 3) return;
			bool fx = g.History && source is IPlayable card &&
			          g.OpenSubSpell(card, Loader.SubSpells.FindNamed(card.Card.Id, "TamePet_Reveal"), new[] { c.Hero.Id });
			c[HsTag.COMPANION_1] = picks[0].AssetId;
			c[HsTag.COMPANION_2] = picks[1].AssetId;
			c[HsTag.COMPANION_3] = picks[2].AssetId;
			if (g.History)
				foreach (Card pick in picks)
				{
					IPlayable shown = Entity.FromCard(c, pick, new EntityData { { GameTag.CREATOR, source.Id } }, c.SetasideZone);
					g.PowerHistory.Add(new Kettle.PowerHistoryMetaData { Type = MetaDataType.HISTORY_TARGET, Data = 0, Info = new List<int> { shown.Id } });
				}
			if (fx) g.CloseSubSpells(source.Id);
		}

		/// <summary>
		/// An effect summons Animal Companions: <paramref name="which"/> (the chosen / all three), else <paramref name="random"/> random ones;
		/// then the player's extra (Talya Earthstrider: "Your cards that summon Animal Companions summon 1 more this game"; wiki: "1 extra
		/// companion per effect, not per companion"; "The extra companion is random"). Returns the summoned minions.
		/// </summary>
		public static List<Minion> SummonCompanions(Controller c, IEntity source, int random = 1, IEnumerable<Card> which = null, int position = -1)
		{
			Game g = c.Game;
			var companions = AnimalCompanions(c);
			var summoned = new List<Minion>();
			if (companions.Count == 0) return summoned;
			// (several random ones at once are different ones: To My Side!'s two)
			var list = (which ?? companions.OrderBy(_ => g.Random.Next()).Take(random)).ToList();
			for (int i = 0; i < c[HsTag.COMPANION_EXTRA]; i++) list.Add(companions[g.Random.Next(companions.Count)]);
			foreach (Card card in list)
			{
				if (c.BoardZone.IsFull || card == null) break;
				var m = (Minion)Entity.FromCard(c, card);
				SummonBlock.Invoke(g, m, position >= 0 ? Math.Min(position + summoned.Count, c.BoardZone.Count) : -1, source);
				if (m.Zone?.Type == Zone.PLAY) summoned.Add(m);
			}
			return summoned;
		}
		// =========================================================================================================== end of turn, recruits, Health

		/// <summary>The TURN_END triggers of a card's power (one, or each of a MultiTrigger).</summary>
		private static IEnumerable<Triggers.Trigger> EndOfTurnTriggers(Power power)
		{
			if (power?.Trigger is Triggers.MultiTrigger multi) return multi.Where(t => t.Type == TriggerType.TURN_END);
			if (power?.Trigger is Triggers.Trigger one && one.Type == TriggerType.TURN_END) return new[] { one };
			return Enumerable.Empty<Triggers.Trigger>();
		}

		/// <summary>Has the minion an end of turn effect (its own, not silenced, or one an enchantment gave it)?</summary>
		public static bool HasEndOfTurnEffect(Minion m) =>
			!m.IsSilenced && EndOfTurnTriggers(m.Card.Power).Any() ||
			m.AppliedEnchantments?.Any(e => EndOfTurnTriggers(e.Power).Any()) == true;

		/// <summary>
		/// "Trigger a random friendly minion's end of turn effect" (Inspiring Maul): its end of turn effects run once, as if its owner's turn
		/// ended — on either turn (wiki patch 35.4.0: "could not trigger its end-of-turn effect during the opponent's turn"). Not an end of
		/// turn, so Drakkari / Sandfury don't double it.
		/// </summary>
		public static void TriggerEndOfTurn(Minion m)
		{
			if (m == null || m.Zone?.Type != Zone.PLAY) return;
			Game g = m.Game;
			if (!m.IsSilenced)
				foreach (Triggers.Trigger t in EndOfTurnTriggers(m.Card.Power).ToList())
					if (t.SingleTask != null) g.TaskQueue.Execute(t.SingleTask, m.Controller, m, null, 0, (int)GameTag.TRIGGER_VISUAL, 0);
			foreach (Enchantment e in m.AppliedEnchantments?.ToList() ?? new List<Enchantment>())
				foreach (Triggers.Trigger t in EndOfTurnTriggers(e.Power).ToList())
					if (t.SingleTask != null) g.TaskQueue.Execute(t.SingleTask, m.Controller, e, null, 0, 0, 0);
		}

		/// <summary>Client player tags: the Silver Hand Recruits' "this game" Attack / Health bonus (the client's SilverHandCardTextBuilder
		/// "{0}" = 1 + them / 1 + them).</summary>
		public const GameTag SILVER_HAND_RECRUIT_ATK_BUFF = (GameTag)4805, SILVER_HAND_RECRUIT_HP_BUFF = (GameTag)4806;

		/// <summary>
		/// "Give your Silver Hand Recruits +A/+H this game" (Brash Battlemaster, Resilient Savior, Emboldening Blade): the player enchantment
		/// <paramref name="playerEnchantment"/> (its hand-and-board aura gives the recruits theirs) and the client's recruit numbers (4805 /
		/// 4806). Recruits = client tag 3444 (SelfCondition.IsSilverHandRecruit).
		/// </summary>
		public static void BuffRecruitsThisGame(Controller c, IEntity source, int atk, int health, string playerEnchantment)
		{
			if (Cards.TryFromId(playerEnchantment) is Card ench) AddEnchantmentBlock(c.Game, ench, (IPlayable)source, c, 0, 0, 0);
			if (atk != 0) c[SILVER_HAND_RECRUIT_ATK_BUFF] = c[SILVER_HAND_RECRUIT_ATK_BUFF] + atk;
			if (health != 0) c[SILVER_HAND_RECRUIT_HP_BUFF] = c[SILVER_HAND_RECRUIT_HP_BUFF] + health;
		}

		/// <summary>"Set your remaining Health to N" (Alexstrasza, Guardian of Life; wiki: "If the Battlecry increases the hero's Health beyond
		/// its current maximum, it will set the hero's new maximum Health to 15" / "Bypasses any Armor" / neither healing nor damage).</summary>
		public static void SetRemainingHealth(Hero hero, int n)
		{
			if (hero == null) return;
			if (n > hero.BaseHealth)
			{
				hero.BaseHealth = n;
				hero.Damage = 0;
			}
			else hero.Damage = hero.BaseHealth - n;
		}

		/// <summary>The friendly minions that died this turn, in death order: the last NumFriendlyMinionsThatDiedThisTurn minions of
		/// Controller.DiedThisGame, the engine's one "died" record (engine-rules.md "Died this game"). HsServer (livecheck 2026-10-10): the
		/// minions counted, not the last N entries — the record also holds destroyed weapons (Charity, "If a friendly Beast died this turn"
		/// missed a minion behind a weapon); never past its start.</summary>
		public static List<IPlayable> DiedThisTurn(Controller c)
		{
			var died = new List<IPlayable>();
			for (int i = c.DiedThisGame.Count - 1; i >= 0 && died.Count < c.NumFriendlyMinionsThatDiedThisTurn; i--)
				if (c.Game.IdEntityDic.TryGetValue(c.DiedThisGame[i], out IPlayable p) && p.Card.Type == CardType.MINION)
					died.Add(p);
			died.Reverse();
			return died;
		}

		/// <summary>A "Legendary" card by its frame (ELITE) or rarity (wiki Medivh's Triumph patch 35.6.0: the Underfel Rift — ELITE, no
		/// rarity — counts).</summary>
		public static bool IsLegendary(Card card) => card != null && (card[GameTag.ELITE] > 0 || card.Rarity == Rarity.LEGENDARY);
		// =========================================================================================================== Geddon, adjacent numbers

		/// <summary>
		/// Commander Geddon's turn draw (CATA_591e: "At the start of your turns, Discover a card from your deck to play. The other two are
		/// incinerated"; wiki: it takes priority over other start-of-turn choice draws): up to three different deck cards offered, the pick
		/// drawn with the cost enchantment the player tag names (CATA_591e2 "Costs (3) less"), the others destroyed in the deck.
		/// </summary>
		public static void DiscoverInsteadOfDraw(Controller c)
		{
			var offers = PickFromDeck(c, p => true);
			if (offers.Count == 0) { Draw(c); return; }
			var ids = offers.Select(p => p.Id).ToList();
			int ench = c[HsTag.TURN_DRAW_DISCOVER];
			DiscoverFromDeck(c, c.Hero, offers, (g2, c2, s2, card) =>
			{
				if (Draw(c2, card) is IPlayable drawn && drawn.Zone?.Type == Zone.HAND && Cards.FromAssetId(ench) is Card less)
					AddEnchantmentBlock(g2, less, c2.Hero, drawn, 0, 0, 0);
				foreach (int id in ids.Where(id => id != card.Id))
					if (g2.IdEntityDic.TryGetValue(id, out IPlayable other) && other.Zone?.Type == Zone.DECK && other.Controller == c2)
						DeckCardToGraveyard(other, c2.Hero.Id, suppressAnimation: false);
			});
		}

		/// <summary>"Adjacent minions have +{0} Attack" with the source's own number (Power.AdjacentOnBoard): after each sequence, each
		/// minion next to such a minion carries its enchantment (made by it, TAG_SCRIPT_DATA_NUM_1 = its number) — one per such neighbour —
		/// and loses it when they part, the source is silenced or its number changed.</summary>
		internal static void SyncAdjacentOnBoard(Game g)
		{
			if (g.State != State.RUNNING) return;
			foreach (Controller c in new[] { g.Player1, g.Player2 })
			{
				var board = c.BoardZone.GetAll().ToList();
				bool any = board.Any(m => m.Card.Power?.AdjacentOnBoard != null) ||
				           board.Any(m => m.AppliedEnchantments?.Any(e => e.Creator?.Card.Power?.AdjacentOnBoard == e.Card.Id) == true);
				if (!any) continue;
				for (int i = 0; i < board.Count; i++)
				{
					Minion m = board[i];
					var wanted = new List<Minion>();
					foreach (int j in new[] { i - 1, i + 1 })
						if (j >= 0 && j < board.Count && board[j].Card.Power?.AdjacentOnBoard != null && !board[j].IsSilenced && !board[j].IsDead)
							wanted.Add(board[j]);
					var have = m.AppliedEnchantments?.Where(e => e.Creator?.Card.Power?.AdjacentOnBoard == e.Card.Id).ToList() ?? new List<Enchantment>();
					foreach (Enchantment e in have.Where(e => !wanted.Any(w => w.Id == e.Creator.Id && w[GameTag.TAG_SCRIPT_DATA_NUM_1] == e.ScriptTag1)).ToList())
						RemoveEnchantmentTask.Task.Process(g, c, e, null);
					have = m.AppliedEnchantments?.Where(e => e.Creator?.Card.Power?.AdjacentOnBoard == e.Card.Id).ToList() ?? new List<Enchantment>();
					foreach (Minion source in wanted)
						if (!have.Any(e => e.Creator.Id == source.Id) && Cards.TryFromId(source.Card.Power.AdjacentOnBoard) is Card ench &&
						    source[GameTag.TAG_SCRIPT_DATA_NUM_1] > 0)
							AddEnchantmentBlock(g, ench, source, m, source[GameTag.TAG_SCRIPT_DATA_NUM_1], 0, 0);
				}
			}
		}
	}
}
