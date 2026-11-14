// HsServer addition (not upstream SabberStone): The Frozen Throne adventure's (ICCA) cards seen in live recordings, from shared
// tasks (livecheck pass 2026-10-10: live session-20261007-190857, Lord Marrowgar). Every number goes through CardValues (V).
using System.Collections.Generic;
using SabberStoneCore.Enchants;
using SabberStoneCore.src.Loader;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Tasks;
using SabberStoneCore.Tasks.SimpleTasks;
using SabberStoneCore.Triggers;

namespace SabberStoneCore.CardSets.HsServer
{
	public static partial class CurrentCoreCardsGen
	{
		private static void IcecrownAdventure(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Lord Marrowgar (ICCA06)
			// [ICCA06_002p] Skeletal Reconstruction (0) — Hero Power: Restore your hero to full Health. Live (19:11 1722): POWER block,
			// TARGET the hero, PREHEALING = its maximum Health (30), HEALING = the damage it had (Character.TakeFullHeal, as Lifebloom).
			cards.Add("ICCA06_002p", new CardDef(new Power { PowerTask = new HealFullTask(EntityType.HERO) }));
			// [ICCA06_004] Bone Storm (2) — Deal $1 damage to all enemies. Draw a card. (Live 6239: 1 to the enemy hero and each enemy
			// minion, then the draw.)
			cards.Add("ICCA06_004", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("ICCA06_004", "damage", 1), EntityType.ENEMIES, true), new DrawTask())
			}));
			// [ICCA06_005] Bone Spike (4, 0/8) — At the start of your turn, deal 15 damage to the enemy hero. (Live 19570: its TRIGGER
			// block at MAIN_START_TRIGGERS, TriggerKeyword TRIGGER_VISUAL — the card's tag — 15 to the enemy hero.)
			cards.Add("ICCA06_005", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = new DamageTask(V("ICCA06_005", "damage", 15), EntityType.OP_HERO) }
			}));
			// [ICCA06_003] Bryn'troll, the Bone Arbiter (6, 3/6) — Windfury: the card's keyword (data).
			cards.Add("ICCA06_003", new CardDef(new Power { NoScript = true }));

			// ---------------------------------------------------------------- (ICCA11)
			// [ICCA11_001] Skeletal Knight (1, 2/3) — Deathrattle: Add a Knights of the Frozen Throne card to your opponent's hand. (Live:
			// ICC_213, ICC_419, ICC_702, ICC_052, ICC_835, ICC_021 — any class; the pool as Scholomance's SCH_199t14.)
			cards.Add("ICCA11_001", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (RandomCard(g, x => x.Set == CardSet.ICECROWN) is Card card) AddToHand(c.Opponent, card);
				})
			}));
		}
	}
}
