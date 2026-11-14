// HsServer addition (not upstream SabberStoneCore): the Vanilla reprints (VAN_, set 1646 and the Tavern Brawl ones) whose old
// text differs from today's card (ReprintCards.Reworded: not mapped to their original). A Vanilla wording that means the same as
// a script we have plays it (CardDef.Like: "Enrage: +5 Attack" = "+5 Attack while damaged"); the others are scripted here from
// their Vanilla text. "Destroy any minion damaged by this minion" (Vanilla Poisonous) is read from the text (Card.DestroysWhatItDamages).
using System.Collections.Generic;
using System.Linq;
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
		private static void Vanilla(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- same as a card we script (old wording only)
			// "Enrage:" = "while damaged" (the upstream scripts are the Enrage auras)
			cards.Add("VAN_EX1_009", CardDef.Like("EX1_009"));   // Angry Chicken — Enrage: +5 Attack
			cards.Add("VAN_CS2_221", CardDef.Like("CS2_221"));   // Spiteful Smith — Enrage: Your weapon has +2 Attack
			cards.Add("VAN_EX1_390", CardDef.Like("EX1_390"));   // Tauren Warrior — Taunt. Enrage: +3 Attack
			cards.Add("VAN_EX1_393", CardDef.Like("EX1_393"));   // Amani Berserker — Enrage: +3 Attack
			cards.Add("VAN_EX1_412", CardDef.Like("EX1_412"));   // Raging Worgen — Enrage: Windfury and +1 Attack
			cards.Add("VAN_EX1_414", CardDef.Like("EX1_414"));   // Grommash Hellscream — Charge. Enrage: +6 Attack
			// "all enemy characters" = "all enemies"
			cards.Add("VAN_EX1_277", CardDef.Like("EX1_277"));   // Arcane Missiles — 3 damage randomly split
			cards.Add("VAN_EX1_384", CardDef.Like("EX1_384"));   // Avenging Wrath — 8 damage randomly split
			// "As soon as your hero is attacked" = "When your hero is attacked"
			cards.Add("VAN_EX1_289", CardDef.Like("EX1_289"));   // Ice Barrier
			// "for each card played earlier this turn" = "for each other card you've played this turn"
			cards.Add("VAN_EX1_613", CardDef.Like("EX1_613"));   // Edwin VanCleef
			// "When that minion is destroyed, return it to the battlefield" = "Deathrattle: Resummon this minion"
			cards.Add("VAN_CS2_038", CardDef.Like("CS2_038"));   // Ancestral Spirit

			// ---------------------------------------------------------------- Vanilla text, its own script
			// [VAN_EX1_294] Mirror Entity (3) — Secret: When your opponent plays a minion, summon a copy of it. ("When … plays": the On Play
			// step, before its Battlecry — rule book On Play Phase; today's card says "After".)
			cards.Add("VAN_EX1_294", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PLAY_MINION)
				{
					SingleTask = ComplexTask.Create(
						new ConditionTask(EntityType.EVENT_SOURCE, SelfCondition.IsNotDead, SelfCondition.IsNotUntouchable),
						new FlagTask(true, ComplexTask.Secret(new SummonCopyTask(EntityType.EVENT_SOURCE))))
				}
			}));
			// [VAN_EX1_536] Eaglehorn Bow (3) — Whenever a Secret is revealed, gain +1 Durability. (Any Secret, either player's; today's card
			// says "a friendly Secret".)
			cards.Add("VAN_EX1_536", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.SECRET_REVEALED)
				{
					TriggerSource = TriggerSource.ALL,
					SingleTask = new AddEnchantmentTask("EX1_536e", EntityType.WEAPON)
				}
			}));
			// [VAN_CS2_233] Blade Flurry (2) — Destroy your weapon and deal its damage to all enemies. (Today: enemy minions only.)
			cards.Add("VAN_CS2_233", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_WEAPON_EQUIPPED, 0 } }, new Power
			{
				PowerTask = ComplexTask.Create(
					new GetGameTagTask(GameTag.ATK, EntityType.WEAPON),
					new DamageNumberTask(EntityType.ENEMIES, true),
					new DestroyTask(EntityType.WEAPON))
			}));
			// [VAN_EX1_145] Preparation (0) — The next spell you cast this turn costs (3) less. (Its own enchantments VAN_EX1_145o / e.)
			cards.Add("VAN_EX1_145", new CardDef(new Power { PowerTask = new AddEnchantmentTask("VAN_EX1_145o", EntityType.CONTROLLER) }));
			cards.Add("VAN_EX1_145o", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("VAN_EX1_145", "less", 3)))
				{
					Condition = SelfCondition.IsSpell,
					ShowAs = "VAN_EX1_145e",
					RemoveTrigger = (TriggerType.CAST_SPELL, null)
				},
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = RemoveEnchantmentTask.Task } // "this turn"
			}));
			// [VAN_EX1_169] Innervate (0) — Gain 2 Mana Crystals this turn only.
			cards.Add("VAN_EX1_169", new CardDef(new Power { PowerTask = new TempManaTask(V("VAN_EX1_169", "mana", 2)) }));
			// [VAN_EX1_571] Force of Nature (6) — Summon three 2/2 Treants with Charge that die at the end of the turn.
			cards.Add("VAN_EX1_571", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new SummonTask("VAN_EX1_tk9b", V("VAN_EX1_571", "treants", 3), SummonSide.SPELL)
			}));
			// [VAN_EX1_tk9b] Treant — Charge. At the end of the turn, destroy this minion.
			cards.Add("VAN_EX1_tk9b", new CardDef(new Power { Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new DestroyTask(EntityType.SOURCE) } }));
			// [VAN_NEW1_008] Ancient of Lore (7) — Choose One - Draw 2 cards; or Restore 5 Health. (Options VAN_NEW1_008a / b.)
			cards.Add("VAN_NEW1_008", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 } }, new Power()));
			cards.Add("VAN_NEW1_008a", new CardDef(new Power { PowerTask = new DrawTask(V("VAN_NEW1_008a", "cards", 2)) }));
			cards.Add("VAN_NEW1_008b", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 } }, new Power
			{
				PowerTask = new HealTask(V("VAN_NEW1_008b", "heal", 5), EntityType.TARGET)
			}));
			// [VAN_EX1_165] Druid of the Claw (5) — Choose One - Charge; or +2 Health and Taunt. (The forms VAN_EX1_165t1 / t2.)
			cards.Add("VAN_EX1_165", new CardDef(new Power()));
			cards.Add("VAN_EX1_165a", new CardDef(new Power { PowerTask = new TransformTask("VAN_EX1_165t1", EntityType.SOURCE) }));
			cards.Add("VAN_EX1_165b", new CardDef(new Power { PowerTask = new TransformTask("VAN_EX1_165t2", EntityType.SOURCE) }));
			// [VAN_EX1_103] Coldlight Seer (3) — Battlecry: Give ALL other Murlocs +2 Health. (Both sides; today only yours.)
			cards.Add("VAN_EX1_103", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new IncludeTask(EntityType.ALLMINIONS_NOSOURCE),
					new FilterStackTask(SelfCondition.IsRace(Race.MURLOC)),
					new AddEnchantmentTask("EX1_103e", EntityType.STACK))
			}));
			// [VAN_EX1_507] Murloc Warleader (3) — ALL other murlocs have +2/+1. (Both boards: AuraType.BOARDS_EXCEPT_SOURCE.)
			cards.Add("VAN_EX1_507", new CardDef(new Power
			{
				Aura = new Aura(AuraType.BOARDS_EXCEPT_SOURCE, "VAN_EX1_507e") { Condition = SelfCondition.IsRace(Race.MURLOC) }
			}));
			cards.Add("VAN_EX1_507e", FromText("VAN_EX1_507e"));
			// [VAN_EX1_508] Grimscale Oracle (1) — ALL other Murlocs have +1 Attack. (Both boards.)
			cards.Add("VAN_EX1_508", new CardDef(new Power
			{
				Aura = new Aura(AuraType.BOARDS_EXCEPT_SOURCE, "EX1_508o") { Condition = SelfCondition.IsRace(Race.MURLOC) }
			}));
			// [VAN_EX1_509] Murloc Tidecaller (1) — Whenever a Murloc is summoned, gain +1 Attack. (Either side; today only yours.)
			cards.Add("VAN_EX1_509", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.SUMMON)
				{
					TriggerSource = TriggerSource.ALL_MINIONS_EXCEPT_SELF,
					Condition = SelfCondition.IsRace(Race.MURLOC),
					SingleTask = new AddEnchantmentTask("EX1_509e", EntityType.SOURCE)
				}
			}));
			// [VAN_EX1_549] Bestial Wrath (1) — Give a Beast +2 Attack and Immune this turn. (Any Beast; today a friendly one.)
			cards.Add("VAN_EX1_549", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_TARGET_WITH_RACE, (int)Race.BEAST } }, new Power
			{
				PowerTask = new AddEnchantmentTask("EX1_549o", EntityType.TARGET)
			}));
			// [VAN_NEW1_014] Master of Disguise (4) — Battlecry: Give a friendly minion Stealth. (For good; today until your next turn.)
			cards.Add("VAN_NEW1_014", new CardDef(new Dictionary<PlayReq, int>
			{
				{ PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_NONSELF_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }
			}, new Power
			{
				PowerTask = new SetGameTagTask(GameTag.STEALTH, 1, EntityType.TARGET)
			}));
			// [VAN_EX1_625] Shadowform (3) — Your Hero Power becomes 'Deal 2 damage.' If already in Shadowform: 3 damage.
			cards.Add("VAN_EX1_625", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					string power = c.Hero.HeroPower?.Card.Id;
					bool already = power == "EX1_625t" || power == "EX1_625t2";
					new ReplaceHeroPower(Cards.FromId(already ? "EX1_625t2" : "EX1_625t")).Process(g, c, s, t, stack);
					c.Hero[GameTag.SHADOWFORM] = 1; // the shadow look (as Shadowform today)
				})
			}));
			// [VAN_EX1_323] Lord Jaraxxus (9, minion) — Battlecry: Destroy your hero and replace it with Lord Jaraxxus. (The 2014 card:
			// hero EX1_323h with 15 Health, Blood Fury, INFERNO!)
			cards.Add("VAN_EX1_323", new CardDef(new Power
			{
				PowerTask = new ReplaceHeroTask("EX1_323h", "EX1_323w", "EX1_tk33")
			}));
		}
	}
}
