// HsServer addition (not upstream SabberStone): TITANS (2023, client set 1858) with its Fall of Ulduar mini-set — the set's shared
// card pieces (Titans and their abilities, Plagues, related tokens, next-turn costs) and the registration the chunk files hang
// on. The engine rules are in Actions/Titans.cs (Titans, Sigils, Magnetic from the client data, draw-and-hold casts, Blossom,
// removal from the game, pre-damage changes, Armor paid / lost, Overload unlocked, "if you play it this turn", the starting
// hand, anomalies, cost floors, excess attack damage, locked hand cards). Every number goes through CardValues (V).
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Actions;
using SabberStoneCore.Auras;
using SabberStoneCore.Conditions;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
using SabberStoneCore.Loader;
using SabberStoneCore.src.Loader;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks;
using SabberStoneCore.Tasks.SimpleTasks;
using SabberStoneCore.Triggers;

namespace SabberStoneCore.CardSets.HsServer
{
	public static partial class CurrentCoreCardsGen
	{
		// ---------------------------------------------------------------- Titans (Actions/Titans.cs)

		/// <summary>A Titan's card: its own Power (Battlecry, aura, trigger…) with "After this uses an ability, …"
		/// (<paramref name="afterAbility"/>: source = the Titan, the ability used = <see cref="TitansLastAbility"/>). Without one the
		/// engine still sends the empty after-ability block (the client's voice line).</summary>
		private static CardDef TitansTitan(Power power = null, ISimpleTask afterAbility = null)
		{
			power ??= new Power();
			if (afterAbility != null) power.TitanAbilityTask = afterAbility;
			if (power.PowerTask == null && power.Aura == null && power.Trigger == null && power.TitanAbilityTask == null && power.DeathrattleTask == null)
				power.NoScript = true; // the abilities are the card's script (their own CardDefs)
			return new CardDef(power);
		}

		/// <summary>A Titan ability's card: its effect runs with the Titan as its source (Generic.UseTitanAbility: no Spell Damage,
		/// the Titan's Lifesteal / Poisonous apply) at the chosen target; <paramref name="requirements"/>: who it may target (the
		/// usual play requirements; REQ_NONSELF_TARGET = not the Titan, REQ_TARGET_IS_NON_TITAN = no Titan); <paramref name="ownFx"/>:
		/// the script places the ability's effect prefabs itself (Generic.AbilityFx / NamedFx) instead of the engine wrapping the
		/// whole effect in the ability card's single buff / area / missile prefab.</summary>
		/// <param name="notInHistory">its POWER block is sent without ShowInHistory (recorded per ability: Power.NotInHistory).</param>
		private static CardDef TitansAbility(ISimpleTask effect, Dictionary<PlayReq, int> requirements = null, bool ownFx = false,
			bool notInHistory = false) =>
			requirements == null
				? new CardDef(new Power { PowerTask = effect, OwnFx = ownFx, NotInHistory = notInHistory })
				: new CardDef(requirements, new Power { PowerTask = effect, OwnFx = ownFx, NotInHistory = notInHistory });

		/// <summary>The card of the ability the Titan used last ("After this uses an ability": The Primus' rune, Norgannon's other
		/// abilities), or null.</summary>
		private static Card TitansLastAbility(IEntity titan) =>
			titan is IPlayable p ? Generic.TitanAbility(p, p[HsTag.TITAN_LAST_ABILITY] - 1)?.Card : null;

		/// <summary>The index (0..2) of the ability the Titan used last, -1 for none.</summary>
		private static int TitansLastAbilityIndex(IEntity titan) => titan[HsTag.TITAN_LAST_ABILITY] - 1;

		/// <summary>The Titan's ability entities it hasn't used yet (Norgannon: "double the power of the other abilities").</summary>
		private static List<IPlayable> TitansUnusedAbilities(IPlayable titan)
		{
			var list = new List<IPlayable>();
			for (int i = 0; i < Generic.TitanAbilityCount(titan); i++)
				if (!Generic.TitanAbilityUsed(titan, i) && Generic.TitanAbility(titan, i) is IPlayable ability)
					list.Add(ability);
			return list;
		}

		/// <summary>The Titan whose ability is running (an ability's task: its source).</summary>
		private static Minion TitansTitanOf(IEntity source) => source as Minion;

		// ---------------------------------------------------------------- Plagues (client tag 2899)

		/// <summary>Client card tag 2899 (unnamed): a Plague (Blood / Unholy / Frost Plague, TTN_450t / t2 / t3).</summary>
		private const GameTag PLAGUE = (GameTag)2899;

		private static Card[] _titansPlagues;

		/// <summary>The Plagues (every card the client marks 2899).</summary>
		private static Card[] TitansPlagues => _titansPlagues ??= Cards.All.Where(x => x[PLAGUE] > 0).OrderBy(x => x.Id).ToArray();

		private static bool TitansIsPlague(IPlayable p) => p != null && p.Card[PLAGUE] > 0;

		/// <summary>A random Plague (repeats allowed between picks: "two random Plagues" — DOUBT).</summary>
		private static Card TitansRandomPlague(Game g)
		{
			Card[] all = TitansPlagues;
			return all.Length == 0 ? null : all[g.Random.Next(all.Length)];
		}

		/// <summary>
		/// "Shuffle a Plague into your opponent's deck": a NEW card of <paramref name="plague"/> (CREATOR = <paramref name="source"/>)
		/// shuffled into <paramref name="c"/>'s opponent's deck, counted for <paramref name="c"/> (HsTag.PLAGUES_SHUFFLED_INTO_ENEMY_DECK:
		/// Chained Guardian). A full deck takes none. Returns whether it got in.
		/// </summary>
		private static bool TitansShufflePlague(Controller c, IEntity source, Card plague)
		{
			if (plague == null) return false;
			Controller to = c.Opponent;
			if (to.DeckZone.IsFull) return false;
			IPlayable card = Entity.FromCard(to, plague, new EntityData { [GameTag.CREATOR] = source.Id });
			if (!Generic.ShuffleIntoDeck.Invoke(to, source, card)) return false;
			c[HsTag.PLAGUES_SHUFFLED_INTO_ENEMY_DECK] = c[HsTag.PLAGUES_SHUFFLED_INTO_ENEMY_DECK] + 1;
			return true;
		}

		/// <summary>
		/// A Plague's card: "Casts When Drawn" — the drawer casts <paramref name="effect"/> ("you" = the drawer; source = the
		/// Plague, no Spell Damage), the engine draws the replacement (Draw.OnDrawn). Helya's "Plagues they draw this game are
		/// unending" (HsTag.PLAGUES_UNENDING on the drawer; wiki Helya: "after the opponent draws one of the Plague cards, a copy is
		/// immediately shuffled back into their deck"): then a new copy goes back into the drawer's deck, the Helya player's effect.
		/// </summary>
		private static CardDef TitansPlague(ISimpleTask effect) => new CardDef(new Power
		{
			TopdeckTask = effect,
			AfterReplacementDrawTask = new CustomTask((g, c, s, t, stack) =>
			{
				if (c[HsTag.PLAGUES_UNENDING] > 0 && s is IPlayable plague)
					TitansShufflePlague(c.Opponent, s, plague.Card);
			})
		});

		/// <summary>"Destroy a Plague in your opponent's deck" (a hidden-zone pick: random); whether one was destroyed (Tomb Traitor:
		/// "to deal 3 damage" only then — forum eu.forums.blizzard.com/…/tomb-traitor-doesnt-always-work-bug/10242).</summary>
		private static bool TitansDestroyPlague(Controller c, IEntity source)
		{
			List<IPlayable> plagues = c.Opponent.DeckZone.Where(TitansIsPlague).ToList();
			if (plagues.Count == 0) return false;
			Generic.Destroy(plagues[c.Game.Random.Next(plagues.Count)], source);
			return true;
		}

		/// <summary>Does the opponent's deck hold a Plague (the Tomb Traitor glow)?</summary>
		private static bool TitansEnemyDeckHasPlague(Controller c) => c.Opponent.DeckZone.Any(TitansIsPlague);

		// ---------------------------------------------------------------- related tokens

		/// <summary>A random MINION among the source card's client RelatedCards (From the Scrapheap / Drone Deconstructor: one of the
		/// eight Sparkbots with their Bonus Effect baked in; repeats allowed — live vfx 89069).</summary>
		private static Card TitansRandomRelatedMinion(Game g, IEntity source) => Generic.RandomRelated(g, source.Card, CardType.MINION);

		// ---------------------------------------------------------------- costs

		/// <summary>
		/// "Next turn, your … cost (N) less" (Flash of Lightning) — a player enchantment that turns its HAND cost aura on at the start
		/// of its owner's next turn (cards that arrive that turn too: vicioussyndicate preview) and leaves at that turn's end; the
		/// mirror of OpponentNextTurnCost (Festival) for the caster's own next turn. <paramref name="shownOnCards"/>: what the
		/// affected cards show. DOUBT (unrecorded): which enchantment sits where.
		/// </summary>
		private static CardDef TitansNextTurnCost(IEffect cost, SelfCondition which, string shownOnCards)
		{
			var aura = new Aura(AuraType.HAND, cost) { Condition = which, ShowAs = shownOnCards };
			return new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.TURN_START)
					{
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (s is IPlayable e && e.OngoingEffect == null) aura.Activate(e);
						})
					},
					new Trigger(TriggerType.TURN_END)
					{
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (s is IPlayable e && e.OngoingEffect != null) RemoveEnchantmentTask.Task.Process(g, c, s, t);
						})
					})
			});
		}

		// ---------------------------------------------------------------- registration

		/// <summary>The set's cards (the integrator adds each chunk's registration here).</summary>
		private static void Titans(IDictionary<string, CardDef> cards)
		{
			TitansChunk1(cards); // TitansCardsGenHs2.cs
			TitansChunk2(cards); // TitansCardsGenHs3.cs
			TitansChunk3(cards); // TitansCardsGenHs4.cs
			TitansChunk4(cards); // TitansCardsGenHs5.cs
		}
	}
}
