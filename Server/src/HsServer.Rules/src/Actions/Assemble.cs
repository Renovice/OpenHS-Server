// HsServer addition (not upstream SabberStone): Assemble (BE expansion) — one mechanism for every Assemble card.
using System;
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks;
using SabberStoneCore.Tasks.SimpleTasks;
using SabberStoneCore.Triggers;

namespace SabberStoneCore.Actions
{
	/// <summary>
	/// HsServer (2026-11-16): Assemble — "Get a random minion. Change half its text." (wiki Assemble; RESEARCH/be-expansion.md).
	/// <list type="bullet">
	/// <item>The pool: the client's tag 4533 (CRAFTABLE_OUTPUT_POWER, 321 collectible minions). The base ("middle card") is a random
	/// pool minion matching the Assemble card's own filter (Fleshshape: Deathrattle; Steward of the Vale: Beast; F3TCH3R: 4-Cost …),
	/// of the player's class or neutral unless the card says otherwise (HSReplay's per-card "Middle Card Pool").</item>
	/// <item>Two side minions from the pool, costing the base's Cost to 2 more (wiki), class or neutral; each offered twice, once
	/// per half. Live (session-20261021-013733 28457-28620, Shadow Sculpt): EntityChoices of 4 = [side1 4573, side1 4572, side2 4573,
	/// side2 4572]; an option marked 4572 (CRAFTABLE_FIRST_HALF_DBID 1) hands over its SECOND half, one marked 4573 its FIRST half
	/// (live: TIME_100's 4572 option -> the result's 4573 = TIME_100's dbf: "At the start of your turn, give all minions in your hand
	/// +1/+1" on a Doomsayer).</item>
	/// <item>The halves: the client text's '&amp;' splits a pool card's text into its first half (when: "Battlecry:", "Deathrattle:",
	/// "At the end of your turn,", "After you play a minion,") and its second (what). In the engine: a card's one Trigger,
	/// Deathrattle or Battlecry is the when, its task the what (AssembleHalves). A card with several parts can't be split
	/// automatically and stays out of the pools until its script says how (Power.AssembleWhen / AssembleWhat).</item>
	/// <item>The result keeps the base's name, Cost, stats and card tags; live marks it ASSEMBLED_MINION 4957, CRAFT 4556, 4973 = the
	/// picked option, the swapped half's dbf in 4572 / 4573, DISPLAYED_CREATOR, and an enchantment (BE_044e, NUM_1 = the option).</item>
	/// </list>
	/// </summary>
	public static partial class Generic
	{
		public const GameTag ASSEMBLE_POOL = (GameTag)4533, CRAFT = (GameTag)4556, CRAFTABLE_FIRST_HALF_DBID = (GameTag)4572,
			CRAFTABLE_SECOND_HALF_DBID = (GameTag)4573, ASSEMBLED_MINION = (GameTag)4957, ASSEMBLED_FROM = (GameTag)4973;

		/// <summary>A pool card's two halves: <paramref name="when"/> makes a Power that runs a task at this card's moment;
		/// <paramref name="what"/> is this card's effect. False: the card can't be split (no part, or more than one).</summary>
		public static bool AssembleHalves(Card card, out Func<ISimpleTask, Power> when, out ISimpleTask what)
		{
			when = null; what = null;
			Power p = card?.Power;
			if (p == null) return false;
			if (p.AssembleWhen != null && p.AssembleWhat != null) { when = p.AssembleWhen; what = p.AssembleWhat; return true; }
			bool trig = p.Trigger != null, dr = p.DeathrattleTask != null, bc = p.PowerTask != null;
			// ("Battlecry and Deathrattle:& X": one first half for both — the same effect at both moments)
			if (!trig && dr && bc && p.Aura == null && card[GameTag.BATTLECRY] > 0 && card[GameTag.DEATHRATTLE] > 0)
			{
				when = task => new Power { PowerTask = task, DeathrattleTask = task };
				what = p.PowerTask;
				return true;
			}
			// (a Combo:, "Whenever you spend your last Mana Crystal," — one first half each)
			if (!trig && !dr && !bc && p.ComboTask != null) { when = task => new Power { ComboTask = task }; what = p.ComboTask; return true; }
			if (!trig && !dr && !bc && p.LastManaSpentTask != null) { when = task => new Power { LastManaSpentTask = task }; what = p.LastManaSpentTask; return true; }
			// (an always-on effect before the halves — "Your hero is Immune. Battlecry:& …" — isn't part of them: it stays with its card,
			// AssembledCard keeps the base's)
			if ((trig ? 1 : 0) + (dr ? 1 : 0) + (bc ? 1 : 0) != 1 || p.Trigger is MultiTrigger) return false;
			if (trig)
			{
				Trigger t = p.Trigger;
				if (t.TriggerActivation == TriggerActivation.HAND || t.SingleTask == null) return false;
				when = task => new Power { Trigger = t.WithTask(task) };
				what = t.SingleTask;
			}
			else if (dr) { when = task => new Power { DeathrattleTask = task }; what = p.DeathrattleTask; }
			else { when = task => new Power { PowerTask = task }; what = p.PowerTask; }
			SplitCondition(card, ref when, ref what);
			return true;
		}

		/// <summary>(2026-11-20) a condition the text puts in the first half ("<b>Battlecry:</b> If you're holding a Dragon,& gain Taunt and
		/// Divine Shield.") goes with it: the second half is the effect alone. Live session-20261106-001808 196906: Chronicle Keeper with
		/// Farseer Wo's first half ("After you cast a spell,& gain Taunt and Divine Shield") gained them with no Dragon in hand. The
		/// condition is known from ComplexTask.If (ComplexTask.PartsOf).</summary>
		private static void SplitCondition(Card card, ref Func<ISimpleTask, Power> when, ref ISimpleTask what)
		{
			if (!(ComplexTask.PartsOf(what) is { } parts)) return;
			string first = System.Text.RegularExpressions.Regex.Replace(AssembleHalfText(card, true) ?? "", "<[^>]*>", "").ToLowerInvariant();
			if (!System.Text.RegularExpressions.Regex.IsMatch(first, @"\bif\b")) return;
			Func<ISimpleTask, Power> outer = when;
			when = task => outer(ComplexTask.If(parts.Condition, task));
			what = parts.Then;
		}

		/// <summary>Is <paramref name="card"/> in the Assemble pool and splittable (a base: a minion).</summary>
		public static bool Assemblable(Card card) =>
			card != null && card.Type == CardType.MINION && card[ASSEMBLE_POOL] > 0 && card.Collectible && AssembleHalves(card, out _, out _);

		/// <summary>Can <paramref name="card"/> be a side: any pool card whose halves split — weapons ("After your hero attacks,") and hero
		/// cards too (HSReplay Ranked Wild: Eyemaul, Lord Jaraxxus offered as sides); a spell only with its halves declared (its effect is
		/// no Battlecry).</summary>
		public static bool AssemblableSide(Card card) =>
			card != null && card.Collectible && (card[ASSEMBLE_POOL] > 0 || card.Type == CardType.MINION) &&
			(card.Type != CardType.SPELL || card.Power?.AssembleWhen != null) && AssembleHalves(card, out _, out _);

		/// <summary>The assembled card: <paramref name="baseCard"/>'s name, Cost, stats and tags, with <paramref name="firstFrom"/>'s
		/// first half (when) and <paramref name="secondFrom"/>'s second (what); the text joined at their '&amp;'.</summary>
		public static Card AssembledCard(Card baseCard, Card firstFrom, Card secondFrom)
		{
			if (!AssembleHalves(firstFrom, out var when, out _) || !AssembleHalves(secondFrom, out _, out var what)) return baseCard;
			Card made = baseCard.Clone();
			made.Power = when(what);
			if (baseCard.Power?.Aura != null && made.Power.Aura == null) made.Power.Aura = baseCard.Power.Aura;
			// (a target the effect needs comes with the effect's card; only a Battlecry first half aims)
			// (or the first half's card, when its first half is the one that aims: Power.AssembleWhenAims)
			Card aims = firstFrom.Power?.AssembleWhenAims == true ? firstFrom : made.Power.PowerTask != null && secondFrom.Power?.AssembleWhenAims != true ? secondFrom : null;
			made.SetPlayRequirements(aims != null ? new Dictionary<PlayReq, int>(aims.PlayRequirements) : new Dictionary<PlayReq, int>());
			static string Half(Card c, bool first)
			{
				string text = c.Text ?? "";
				int i = text.IndexOf('&');
				return i < 0 ? (first ? text : "") : first ? text.Substring(0, i) : text.Substring(i + 1);
			}
			made.Text = (Half(firstFrom, true) + " " + Half(secondFrom, false)).Trim();
			return made;
		}

		/// <summary>(2026-11-17) What makes two cards "the same card" for uniqueness (different, duplicates, each kind): the card id — except an
		/// Assembled minion, a unique card of its own (wiki Assemble: "considered unique cards for effects like Eternal Servitude or Endbringer
		/// Umbra").</summary>
		public static object UniqueCardKey(IPlayable p) => p[ASSEMBLED_MINION] > 0 ? (object)p.Card : p.Card.Id;

		/// <summary>Every card any Assemble card's HSReplay lists hold, as a base or a side (AssemblePools).</summary>
		private static HashSet<int> _assembleSeen;
		private static HashSet<int> AssembleSeen => _assembleSeen ??= new HashSet<int>(AssemblePools.Values.SelectMany(v => v.Bases.Concat(v.Sides)));

		/// <summary>A pool card's first (when) or second (what) half of its client text, split at its '&amp;'. The first half compared without
		/// markup and case ("<b>Battlecry:</b>" = "Battlecry:"); the second exactly (user 2026-11-17: "Draw a card" and "draw a card" both
		/// show up together on live).</summary>
		public static string AssembleHalfText(Card card, bool first)
		{
			string text = card?.Text ?? "";
			int i = text.IndexOf('&');
			string half = i < 0 ? (first ? text : "") : first ? text.Substring(0, i) : text.Substring(i + 1);
			if (!first) return half.Trim();
			half = System.Text.RegularExpressions.Regex.Replace(half, @"<[^>]+>|\[x\]|\s+", " ");
			return half.Trim().TrimEnd(',', ':', '.').Trim().ToLowerInvariant();
		}

		/// <summary>Does <paramref name="when"/>'s first half work with <paramref name="what"/>'s second (user rule B, 2026-11-17): a
		/// Deathrattle can't act on "this" minion (its stats, keywords, attacks — it has died); an end-of-turn trigger can't Discover, Choose One
		/// or give something "this turn"; a Kindred needs the middle card's minion type (<paramref name="baseCard"/>: the card the result is).</summary>
		public static bool AssembleCompatible(Card when, Card what, Card baseCard)
		{
			string w = AssembleHalfText(when, true), e = AssembleHalfText(what, false).ToLowerInvariant();
			// (a Deathrattle that "gains" stats or keywords for itself; "Gain 3 Armor" is the hero's and fine)
			if (w.Contains("deathrattle") && System.Text.RegularExpressions.Regex.IsMatch(e,
				@"this minion|this attacks|gain \+|gain (<b>)?(rush|taunt|divine shield|windfury|lifesteal|poisonous|reborn|stealth|elusive|charge)")) return false;
			if (w.Contains("at the end of") && System.Text.RegularExpressions.Regex.IsMatch(e, "discover|choose one|this turn")) return false;
			int race = when[GameTag.CARDRACE];
			if (w.Contains("kindred") && when != baseCard && !(race > 0 && baseCard.IsRace((Race)race))) return false;
			return true;
		}

		/// <summary>Can <paramref name="side"/> be offered with the middle card <paramref name="baseCard"/>: a different first half and second
		/// half (user rule A), and both swaps compatible (rule B).</summary>
		public static bool AssembleFits(Card baseCard, Card side) =>
			AssembleHalfText(side, true) != AssembleHalfText(baseCard, true) && AssembleHalfText(side, false) != AssembleHalfText(baseCard, false) &&
			AssembleCompatible(side, baseCard, baseCard) && AssembleCompatible(baseCard, side, baseCard);

		/// <summary>A random base or side: a card of <paramref name="c"/>'s class weighs 2, any other 1 (user rule 2026-11-17, HSReplay's offer rates).</summary>
		public static Card PickAssembleBase(Controller c, IList<Card> bases)
		{
			int Weight(Card x) => x.HasClass(c.HeroClass) && !x.HasClass(CardClass.NEUTRAL) ? 2 : 1;
			int roll = c.Game.Random.Next(bases.Sum(Weight));
			foreach (Card x in bases) { roll -= Weight(x); if (roll < 0) return x; }
			return bases[bases.Count - 1];
		}

		/// <summary>
		/// Assemble a minion for <paramref name="c"/>: a random base matching <paramref name="basePool"/>, two side minions (Cost: the
		/// base's to 2 more; <paramref name="sideClass"/> decides their class, default class or neutral); the player picks a half;
		/// the assembled minion goes to the hand and <paramref name="after"/> runs on it ("It costs (2) less", "+2/+3" …).
		/// </summary>
		public static void Assemble(Controller c, IPlayable source, Func<Controller, Card, bool> basePool, Action<Game, Controller, IPlayable> after = null,
			Func<Controller, Card, bool> sideClass = null, string enchantment = "BE_044e", Func<Card, Card, bool> sideCost = null,
			Action<Game, Controller, IPlayable, IPlayable> place = null)
		{
			Game g = c.Game;
			sideClass ??= (cc, x) => x.HasClass(cc.HeroClass) || x.HasClass(CardClass.NEUTRAL);
			// (HSReplay's Ranked Wild pools are the truth where it has the card — user rule 2026-11-17; AssemblePools.generated.cs)
			bool listed = AssemblePools.TryGetValue(source.Card.AssetId, out var hs);
			// (2026-11-17) a player of another class (a generated Shadow Sculpt in a Priest's hand): the pools are this player's class and
			// neutral; where this card's HSReplay lists hold no card of that class (its games were the card's own class), that class's cards
			// come from every Assemble card's lists together (AssembleSeen)
			bool ClassIn(HashSet<int> list) => list.Any(id => Cards.FromAssetId(id) is Card k && k.HasClass(c.HeroClass) && !k.HasClass(CardClass.NEUTRAL));
			bool basesHaveClass = listed && ClassIn(hs.Bases), sidesHaveClass = listed && ClassIn(hs.Sides);
			bool Known(Card x, bool side)
			{
				if (!listed) return false;
				if ((side ? hs.Sides : hs.Bases).Contains(x.AssetId)) return true;
				return !(side ? sidesHaveClass : basesHaveClass) && x.HasClass(c.HeroClass) && !x.HasClass(CardClass.NEUTRAL) && AssembleSeen.Contains(x.AssetId);
			}
			var bases = Cards.FormatTypeCards(g.FormatType).Where(x => (listed ? Known(x, false) && x.Type == CardType.MINION && x.Collectible &&
				AssembleHalves(x, out _, out _) : Assemblable(x)) && basePool(c, x)).ToList();
			if (bases.Count == 0 || c.HandZone.IsFull) return;
			// (2026-11-17, user rule) the base: a card of the player's class twice as likely as any other — HSReplay's Fleshshape offer rates
			// are 7 Death Knight cards at ~6.6% and 15 neutrals at ~3.3%; the other unevenness live shows per card (Firestone reports it too)
			// is not copied: otherwise every card weighs the same. The sides: uniform.
			Card baseCard = PickAssembleBase(c, bases);
			int lo = baseCard.Cost, hi = baseCard.Cost + 2;
			if (baseCard.Cost >= 9) lo = 8; // (wiki: exceptions for 9-10 Cost bases)
			var sides = Cards.FormatTypeCards(g.FormatType)
				.Where(x => (listed ? Known(x, true) && AssemblableSide(x) : Assemblable(x)) && x.Id != baseCard.Id && (sideCost != null ? sideCost(baseCard, x) : x.Cost >= lo && x.Cost <= hi) && sideClass(c, x)).ToList();
			if (sides.Count == 0) { AddAssembled(c, source, baseCard, null, null, after, enchantment, place); return; }
			var picks = new List<Card>();
			// (2026-11-17, user rules A / B) the sides fit the middle card and each other: no two share a first half (Battlecry, Deathrattle,
			// "At the end of your turn" …) or the very same second half; and both swaps must make sense (AssembleCompatible)
			sides = sides.Where(x => AssembleFits(baseCard, x)).ToList();
			while (picks.Count < 2 && sides.Count > 0)
			{
				Card s = PickAssembleBase(c, sides); // (the sides weigh the same way: the player's class twice — user 2026-11-17)
				picks.Add(s);
				sides = sides.Where(x => x != s && AssembleHalfText(x, true) != AssembleHalfText(s, true) && AssembleHalfText(x, false) != AssembleHalfText(s, false)).ToList();
			}
			IPlayable middle = Entity.FromCard(c, baseCard, new EntityData { [GameTag.CREATOR] = source.Id }, c.SetasideZone);
			var options = new List<IPlayable>();
			foreach (Card s in picks)
			{
				options.Add(Entity.FromCard(c, s, new EntityData { [GameTag.CREATOR] = source.Id, [CRAFTABLE_SECOND_HALF_DBID] = 1 }, c.SetasideZone));
				options.Add(Entity.FromCard(c, s, new EntityData { [GameTag.CREATOR] = source.Id, [CRAFTABLE_FIRST_HALF_DBID] = 1 }, c.SetasideZone));
			}
			int middleId = middle.Id, sourceId = source.Id;
			CreateChoiceEntities(c, source, options, ChoiceAction.STACK, new CustomTask((g2, c2, s2, pick, st) =>
			{
				if (pick == null || !g2.IdEntityDic.TryGetValue(middleId, out IPlayable mid)) return;
				IPlayable src = g2.IdEntityDic.TryGetValue(sourceId, out IPlayable found) ? found : s2 as IPlayable;
				AddAssembled(c2, src, mid.Card, pick, mid, after, enchantment, place);
			}));
		}

		private static void AddAssembled(Controller c, IPlayable source, Card baseCard, IPlayable pick, IPlayable middle,
			Action<Game, Controller, IPlayable> after, string enchantment, Action<Game, Controller, IPlayable, IPlayable> place = null)
		{
			Game g = c.Game;
			IPlayable made = middle ?? Entity.FromCard(c, baseCard, new EntityData { [GameTag.CREATOR] = source?.Id ?? 0 }, c.SetasideZone);
			if (pick != null)
			{
				bool givesSecond = pick[CRAFTABLE_FIRST_HALF_DBID] > 0;
				made.Card = givesSecond ? AssembledCard(baseCard, baseCard, pick.Card) : AssembledCard(baseCard, pick.Card, baseCard);
				made[givesSecond ? CRAFTABLE_SECOND_HALF_DBID : CRAFTABLE_FIRST_HALF_DBID] = pick.Card.AssetId;
				made[ASSEMBLED_FROM] = pick.Id;
			}
			made[ASSEMBLED_MINION] = 1;
			if (source != null) made[GameTag.DISPLAYED_CREATOR] = source.Id;
			// (somewhere else than the hand — Rajh: a Passive Hero Power; the assembled minion stays set aside, its text shown)
			if (place != null) { place(g, c, made, source); return; }
			made.Zone?.Remove(made);
			if (!AddHandPhase.Invoke(c, made)) return;
			made[CRAFT] = 1;
			if (enchantment != null && source != null && Cards.TryFromId(enchantment) is Card ench)
				AddEnchantmentBlock(g, ench, source, made, pick?.Id ?? 0, -1);
			after?.Invoke(g, c, made);
		}
	}
}
