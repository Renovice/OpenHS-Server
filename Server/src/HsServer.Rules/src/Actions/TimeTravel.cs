// HsServer addition (not upstream SabberStone): Across the Timeways (TIME_TRAVEL, set 1957) — shared pieces: "Discover a copy of a
// card in <zone>", a Paladin Aura put into play, a Fabled card's bundle, Rafaams, "would damage this with a Nature spell … instead",
// split in halves, drawing from another player's deck. Tags: Enums/HsTag.cs 990650-990699.
using System;
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks;
using SabberStoneCore.Tasks.SimpleTasks;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>
		/// "Discover a copy of a card in your opponent's hand / from your deck" (Deja Vu, Intertwined Fate). Live Intertwined Fate
		/// (live-20261005 190813 3010-3175): each option is a copy of an existing card, set aside (CREATOR = the source, LINKED_ENTITY /
		/// COPIED_FROM_ENTITY_ID = the original; a copy from a hand or deck keeps its enchantments: wiki Copy), up to three different
		/// cards (PickFrom); the pick goes to the hand, the others stay set aside. The source's own card may be offered (live: Intertwined
		/// Fate offered a deck copy of itself). <paramref name="afterPick"/> runs after the pick (the next Discover).
		/// </summary>
		public static bool DiscoverCopyOf(Controller c, IEntity source, IEnumerable<IPlayable> cards, Action<Game, Controller, IEntity, IPlayable> afterPick = null)
		{
			List<IPlayable> originals = PickFrom(cards.ToList(), c.Game, null);
			if (originals.Count == 0) return false;
			c.Game.FlushPendingFx(source.Id);
			var offered = new List<IPlayable>();
			foreach (IPlayable original in originals)
			{
				Zone from = original.Zone?.Type ?? Zone.INVALID;
				IPlayable copy = Copy(c, source, original, Zone.SETASIDE, keepEnchantments: from == Zone.HAND || from == Zone.DECK);
				copy[GameTag.CREATOR] = source.Id;
				copy[GameTag.LINKED_ENTITY] = original.Id;
				copy[HsTag.WAS_DISCOVER_OPTION] = 1;
				offered.Add(copy);
			}
			return CreateChoiceEntities(c, source, offered, ChoiceAction.HAND,
				afterPick == null ? null : new CustomTask((g, c2, s, chosen, stack) => afterPick(g, c2, s, chosen)));
		}

		/// <summary>
		/// A Paladin Aura ("Lasts @ turns") put into the battlefield without being cast (Gelbin of Tomorrow: "Put one of each Aura from
		/// your deck into the battlefield"; wiki patch 34.0.0: "will now correctly pull Auras from the deck, even if one is already in
		/// play"): like a cast one (CastSpell) — its trigger and ongoing effect on, in the Secret zone. False when the zone is full.
		/// </summary>
		public static bool PutDurationSpellIntoPlay(Controller c, Spell spell)
		{
			if (c.SecretZone.IsFull) return false;
			spell.Zone?.Remove(spell);
			spell.Power?.Trigger?.Activate(spell);
			spell.Power?.Aura?.Activate(spell);
			c.SecretZone.Add(spell);
			spell.IsExhausted = true;
			return true;
		}

		/// <summary>Does the player have a Paladin Aura up (Manifested Timeways: "If you control an Aura"; client PALADIN_AURA)?</summary>
		public static bool ControlsAura(Controller c) => c.SecretZone.Any(p => p.Card[HsTag.PALADIN_AURA] > 0);

		/// <summary>Client tag 3928 (unnamed): a Rafaam of Timethief Rafaam's family — his nine and Baaaafam (wiki Timethief Rafaam:
		/// "other Fabled tokens will treat Baaaafam as Rafaam"; not Arch-Thief / Arch-Villain Rafaam).</summary>
		public const GameTag RAFAAM_FAMILY = (GameTag)3928;

		public static bool IsRafaam(IPlayable p) => p?.Card[RAFAAM_FAMILY] > 0;

		/// <summary>
		/// "Whenever you would damage this with a &lt;school&gt; spell, … instead" (Flux Revenant, Stormrook): a pre-damage trigger (rule
		/// book DH7 "Just before a Damage Event is created, Predamage triggers will queue and resolve"; DH2a: after Divine Shield took it to
		/// 0 there is no event) that drops the damage to 0 and runs <paramref name="instead"/> (its source: the minion). Only the owner's
		/// spells ("you"); one run per damage instance. Live Stormrook (195600 2606-2656): TRIGGER block EffectIndex 1, PREDAMAGE 0.
		/// </summary>
		public static Triggers.Trigger InsteadOfSpellDamage(int school, ISimpleTask instead, int effectIndex = 1) => new Triggers.Trigger(TriggerType.PREDAMAGE)
		{
			TriggerSource = TriggerSource.SELF,
			FastExecution = true,
			EffectIndex = effectIndex,
			Condition = new Conditions.SelfCondition(p => p.Game.CurrentEventData is EventMetaData e && e.EventNumber > 0 &&
				e.EventSource is Spell spell && spell.Controller == p.Controller && spell.Card[(GameTag)1635] == school),
			SingleTask = ComplexTask.Create(new CustomTask((g, c, s, t, stack) => SetPreDamage(g, (IPlayable)s, 0)), instead)
		};

		/// <summary>
		/// "Split a minion in your hand into two halves" (Divergence; wiki: "two copies of a random minion in hand with their cost, attack,
		/// and health halved" / odd numbers split into two different numbers at random / "Cost and attack will split into 0 and 1, but
		/// health will always split into 1 and 1"). Live (130515 98773-99140): the original goes to SETASIDE; each half is a new copy
		/// (COPIED_FROM_ENTITY_ID, a hand copy keeps its enchantments) with <paramref name="costEnchantment"/> (SET Cost NUM_1, gone when
		/// played) and <paramref name="statsEnchantment"/> (SET Attack NUM_1 / Health NUM_2), into the original's slot.
		/// </summary>
		public static void SplitInHalves(Controller c, IEntity source, IPlayable card, Card costEnchantment, Card statsEnchantment)
		{
			if (card?.Zone?.Type != Zone.HAND || !(card is Minion m)) return;
			Game g = c.Game;
			(int, int) Halves(int v, bool health)
			{
				if (health && v <= 2) return (1, 1);
				int big = (v + 1) / 2, small = v / 2;
				return g.Random.Next(2) == 0 ? (big, small) : (small, big);
			}
			var cost = Halves(Math.Max(0, m.Cost), false);
			var atk = Halves(Math.Max(0, m.AttackDamage), false);
			var health = Halves(Math.Max(1, m.Health), true);
			int slot = m.ZonePosition;
			var halves = new List<IPlayable>();
			for (int i = 0; i < 2; i++)
			{
				IPlayable half = Copy(c, source, m, Zone.SETASIDE, keepEnchantments: true);
				half[GameTag.CREATOR] = source.Id;
				halves.Add(half);
			}
			RemoveFromHand(c, m);
			for (int i = 0; i < 2; i++)
			{
				IPlayable half = halves[i];
				c.SetasideZone.Remove(half);
				if (!AddHandPhaseAt(c, half, slot + i)) continue;
				if (costEnchantment != null) AddEnchantmentBlock(g, costEnchantment, (IPlayable)source, half, i == 0 ? cost.Item1 : cost.Item2, 0, 0);
				if (statsEnchantment != null)
					AddEnchantmentBlock(g, statsEnchantment, (IPlayable)source, half, i == 0 ? atk.Item1 : atk.Item2, i == 0 ? health.Item1 : health.Item2, 0);
			}
		}

		/// <summary>
		/// <paramref name="drawer"/> draws a card of another player's deck (Chronogor: "Your opponent draws your 2 lowest Cost cards"; live
		/// 112456 30328-30431: CONTROLLER_AND_ZONE_CHANGE from your DECK to their HAND, CONTROLLER = theirs, a draw for them — their
		/// NUM_CARDS_DRAWN_THIS_TURN). Then their normal draw phases (a full hand burns it; draw triggers; Casts When Drawn as theirs).
		/// </summary>
		public static IPlayable DrawFromDeckOf(Controller drawer, IPlayable card)
		{
			if (card?.Zone?.Type != Zone.DECK) return null;
			if (card.Controller == drawer) return Draw(drawer, card);
			GiveTo(card, drawer, Zone.HAND);
			drawer.NumCardsDrawnThisTurn++;
			drawer.LastCardDrawn = card.Id;
			card[HsTag.DRAWN_ON_TURN] = drawer.Game.Turn;
			if (AddHandPhase.Invoke(drawer, card))
			{
				NoteDrawn(drawer, card);
				EnchantDrawn(drawer, card);
				Game game = drawer.Game;
				game.TaskQueue.StartEvent();
				game.TriggerManager.OnDrawTrigger(card);
				game.ProcessTasks();
				game.TaskQueue.EndEvent();
				MarkDraw(game);
				OnDrawn(drawer, card);
			}
			return card;
		}

		private static readonly Dictionary<string, List<Card>> FabledFamilies = new Dictionary<string, List<Card>>();

		/// <summary>
		/// A Fabled card's family (wiki Fabled: the card "comes with" its bundle; client RelatedCards with IS_FABLED_BUNDLE_CARD 4445):
		/// the Fabled card and its bundle cards, from any of them (Medivh / Atiesh / Karazhan name each other; Garona's King Llane).
		/// Empty for a card in no bundle.
		/// </summary>
		public static List<Card> FabledFamily(Card card)
		{
			if (card == null) return new List<Card>();
			lock (FabledFamilies)
			{
				if (FabledFamilies.TryGetValue(card.Id, out List<Card> known)) return known;
				Card main = card[(GameTag)4445] > 0
					? Cards.All.FirstOrDefault(x => x[(GameTag)4445] == 0 && Loader.ReprintCards.RelatedCardIds(x.Id)?.Contains(card.Id) == true &&
					                                x[GameTag.COLLECTIBLE] > 0)
					: card;
				var family = new List<Card>();
				if (main != null)
				{
					List<Card> bundle = (Loader.ReprintCards.RelatedCardIds(main.Id) ?? new List<string>()).Select(Cards.TryFromId)
						.Where(x => x != null && x[(GameTag)4445] > 0).ToList();
					if (bundle.Count > 0) family = bundle.Prepend(main).ToList();
				}
				FabledFamilies[card.Id] = family;
				return family;
			}
		}

		/// <summary>The card of <paramref name="card"/>'s Fabled family with this type (Medivh's Karazhan: the LOCATION), else null.</summary>
		public static Card FabledRelative(Card card, CardType type) => FabledFamily(card).FirstOrDefault(x => x.Type == type && x != card);
	}
}
