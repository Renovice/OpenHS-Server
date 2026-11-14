// HsServer addition (not upstream SabberStoneCore): Path of Arthas (set 1869, RLK_ — the Death Knight starter cards; Harth
// Stonebrew's Death Knight hands use them by name). Shared pieces: EffectAttack (forced attacks), Power.HandTarget (client
// CAN_TARGET_CARDS_IN_HAND: the player picks the hand card), NextCardEffect-style player enchantments, Frost spells by school.
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
		private static void PathOfArthas(IDictionary<string, CardDef> cards)
		{
			// [RLK_018] Plague Strike (2) — Deal $3 damage to a minion. If it dies, summon a 2/2 Zombie with Rush.
			cards.Add("RLK_018", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Generic.DamageCharFunc((IPlayable)s, m, V("RLK_018", "damage", 3), true);
					if (m.IsDead || m.ToBeDestroyed) Summon(g, c, Cards.FromId("RLK_018t"), s);
				})
			}));
			// [RLK_038] Icy Touch (1) — Deal $2 damage to an enemy and Freeze it.
			cards.Add("RLK_038", new CardDef(TargetEnemy, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("RLK_038", "damage", 2), EntityType.TARGET, true), ComplexTask.Freeze(EntityType.TARGET))
			}));
			// [RLK_056] Unholy Frenzy (2) — Choose an enemy minion. Your minions attack it. Resummon any that die. (Each in board order
			// while it lives, as Devouring Swarm; the dead come back as new copies after the deaths.)
			cards.Add("RLK_056", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion prey)) return;
					var died = new List<Card>();
					foreach (Minion m in c.BoardZone.Where(x => !x.Untouchable).ToList())
					{
						if (prey.Zone?.Type != Zone.PLAY || prey.ToBeDestroyed) break;
						EffectAttack(g, m, prey);
						if (m.ToBeDestroyed || m.Health <= 0 || m.Zone?.Type == Zone.GRAVEYARD) died.Add(m.Card);
					}
					g.DeathProcessingAndAuraUpdate();
					foreach (Card card in died) Summon(g, c, card, s);
				})
			}));
			// [RLK_110] Ymirjar Frostbreaker (1) — Battlecry: Gain +1 Attack for each Frost spell in your hand.
			cards.Add("RLK_110", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int frost = c.HandZone.Count(IsFrostSpell);
					for (int i = 0; i < frost; i++) Give(g, s, (IPlayable)s, "RLK_110e");
				})
			}));
			cards.Add("RLK_110e", FromText("RLK_110e"));
			// [RLK_512] Glacial Advance (3) — Deal $4 damage. Your next spell this turn costs (2) less. (Its enchantments RLK_025o / e.)
			cards.Add("RLK_512", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("RLK_512", "damage", 4), EntityType.TARGET, true), new AddEnchantmentTask("RLK_025o", EntityType.CONTROLLER))
			}));
			cards.Add("RLK_025o", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("RLK_512", "discount", 2)))
				{
					Condition = SelfCondition.IsSpell,
					ShowAs = "RLK_025e",
					RemoveTrigger = (TriggerType.CAST_SPELL, null)
				},
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = RemoveEnchantmentTask.Task } // "this turn"
			}));
			// [RLK_516] Bone Breaker (1) — After your hero attacks a minion, deal 2 damage to the enemy hero.
			cards.Add("RLK_516", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventTarget is Minion)
							Generic.DamageCharFunc((IPlayable)s, c.Opponent.Hero, V("RLK_516", "damage", 2), false);
					})
				}
			}));
			// [RLK_711] Vicious Bloodworm (2) — Battlecry: Give a minion in your hand Attack equal to this minion's Attack. (The player
			// picks: client CAN_TARGET_CARDS_IN_HAND.)
			cards.Add("RLK_711", new CardDef(new Power
			{
				HandTarget = SelfCondition.IsMinion,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m && m.Zone?.Type == Zone.HAND && m.Controller == c && s is Minion me && me.AttackDamage > 0)
						Give(g, s, m, "RLK_711e", me.AttackDamage);
				})
			}));
			cards.Add("RLK_711e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0)) { UseScriptTag = true } }));
			// [RLK_713] Lady Deathwhisper (4) — Deathrattle: Copy all Frost spells in your hand.
			cards.Add("RLK_713", new CardDef(new Power
			{
				DeathrattleTask = ComplexTask.Create(
					new IncludeTask(EntityType.HAND),
					new FilterStackTask(new SelfCondition(IsFrostSpell)),
					new CopyTask(EntityType.STACK, Zone.HAND))
			}));
			// [RLK_730] Blood Boil (5) — Lifesteal. Infect all enemy minions. At the end of your turns, they take 2 damage. (RLK_730e on
			// each: "At the end of your opponent's turns, take 2 damage"; the client data gives the enchantment the LIFESTEAL tag, so it
			// deals the damage itself and heals its owner.)
			cards.Add("RLK_730", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.Opponent.BoardZone.Where(m => !m.IsDead), "RLK_730e"))
			}));
			cards.Add("RLK_730e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					EitherTurn = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Enchantment e && e.Target is Minion infected && g.CurrentPlayer == infected.Controller.Opponent)
							Generic.DamageCharFunc(e, infected, V("RLK_730", "damage", 2), false);
					})
				}
			}));
		}
	}
}
