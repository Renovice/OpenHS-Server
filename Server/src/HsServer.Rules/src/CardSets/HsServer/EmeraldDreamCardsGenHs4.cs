// HsServer addition (not upstream SabberStone): Into the Emerald Dream — chunk 3: Paladin, Priest and Rogue (spec: research
// spec_chunk3), with the Paladin / Priest Imbue Hero Powers. Shared pieces: Actions/Imbue.cs (Imbue, Smoldering, the "Cast @ spells"
// upgrade, Toreth's shields), Generic.ReturnToHandBlock (ENTERED_HAND_FROM_PLAY), CastSpell (PLAYED_SPELLS_CAST_TWICE), PlayWeapon
// (Choose One weapons). Every number goes through CardValues (V).
using System;
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
		/// <summary>A random collectible minion of exactly <paramref name="cost"/> for the battlefield matching <paramref name="where"/>
		/// (the board pool, like RandomMinionOfCost), or null.</summary>
		private static Card RandomBoardMinion(Game g, int cost, Func<Card, bool> where)
		{
			if (!Cards.BoardCostMinionCards(g.FormatType).TryGetValue(cost, out List<Card> all)) return null;
			Card self = Cards.GeneratingCard(g);
			Controller generator = Cards.GeneratingPlayer(g);
			var pool = all.Where(card => card.Id != self?.Id && Cards.DeckAllows(generator, card) && where(card)).ToList();
			return pool.Count > 0 ? pool[g.Random.Next(pool.Count)] : null;
		}

		/// <summary>Summons <paramref name="card"/> for <paramref name="c"/> and returns it (null: no room / no card).</summary>
		private static Minion SummonGet(Game g, Controller c, Card card, IEntity source, int position = -1)
		{
			if (card == null || c.BoardZone.IsFull) return null;
			var minion = (Minion)Entity.FromCard(c, card, new EntityData { [GameTag.CREATOR] = source.Id });
			Generic.SummonBlock.Invoke(g, minion, position, source);
			return minion.Zone?.Type == Zone.PLAY ? minion : null;
		}

		/// <summary>A player enchantment of <paramref name="c"/> made from <paramref name="cardId"/>, or null.</summary>
		private static Enchantment PlayerEnchantment(Controller c, string cardId) =>
			c.AppliedEnchantments?.FirstOrDefault(e => e.Card.Id == cardId);

		private static readonly Dictionary<PlayReq, int> TargetFriendlyMinionToPlay =
			new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 } };

		private static void EmeraldChunk3(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- the Imbue Hero Powers (Paladin, Priest)
			// [EDR_445p] Blessing of the Dragon (1) — Shuffle two Emerald Portals into your deck. (Your Portals summon @-Cost Dragons.)
			// (live vfx 181996-182073: the portals carry the Hero Power's number, CREATOR = it; Generic.Imbue keeps them current)
			cards.Add("EDR_445p", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = Math.Max(1, s[GameTag.TAG_SCRIPT_DATA_NUM_1]);
					for (int i = 0; i < V("EDR_445p", "portals", 2); i++)
						Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId("EDR_445pt3"), new EntityData
						{
							[GameTag.CREATOR] = s.Id, [GameTag.DISPLAYED_CREATOR] = s.Id, [GameTag.TAG_SCRIPT_DATA_NUM_1] = n
						}));
				})
			}));
			// [EDR_445pt3] Emerald Portal — Casts When Drawn. Summon a random @-Cost Dragon. (any class: live summons of DK / Paladin Dragons)
			cards.Add("EDR_445pt3", new CardDef(new Power
			{
				TopdeckTask = new CustomTask((g, c, s, t, stack) =>
					SummonGet(g, c, RandomBoardMinion(g, Math.Max(1, s[GameTag.TAG_SCRIPT_DATA_NUM_1]), x => x.IsRace(Race.DRAGON)), s))
			}));
			// [EDR_449p] Blessing of the Moon (2) — Choose a playable Priest minion or spell to add to your hand. It costs (@) less, but is
			// Temporary. (wiki: "always one minion and one spell"; "playable": affordable after the Hero Power, with its discount)
			cards.Add("EDR_449p", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = Math.Max(1, s[GameTag.TAG_SCRIPT_DATA_NUM_1]);
					var priest = Cards.GenerationPool(g, c).Where(x => x.HasClass(CardClass.PRIEST) && x.Cost - n <= c.RemainingMana).ToList();
					var offer = Pick(g, priest.Where(x => x.Type == CardType.MINION), 1).Concat(Pick(g, priest.Where(x => x.Type == CardType.SPELL), 1)).ToArray();
					if (offer.Length == 0) return;
					Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.STACK, offer, new CustomTask((g2, c2, s2, pick, st) =>
					{
						if (pick == null || c2.HandZone.IsFull) return;
						pick.Zone?.Remove(pick);
						if (!Generic.AddHandPhase.Invoke(c2, pick)) return;
						Give(g2, s2, pick, "EDR_449e", n);
						Generic.MakeTemporary(pick, s2);
					}));
				})
			}));
			cards.Add("EDR_449e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(0)) { UseScriptTag = true } }));

			// ---------------------------------------------------------------- Paladin
			// [EDR_251] Dragonscale Armaments (1) — Draw a spell that started in your deck and one that didn't.
			cards.Add("EDR_251", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					DrawOne(g, c, p => p is Spell && !Generic.DidntStartInDeck(p));
					DrawOne(g, c, p => p is Spell && Generic.DidntStartInDeck(p));
				})
			}));
			// [EDR_252] Mark of Ursol (2) — Choose a minion. If it's an enemy, set its stats to 1/1. If it's friendly, set its stats to 3/3 instead.
			cards.Add("EDR_252", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					// (live 204312 33022: ReuseFX_Holy_Impact_Debuff_Humility_Super around the enchantment; the friendly branch's FX: doubt)
					if (t is Minion m) Generic.NamedFx((IPlayable)s, "Humility", new[] { m.Id }, () => Give(g, s, m, m.Controller == c ? "EDR_252e1" : "EDR_252e"));
				})
			}));
			cards.Add("EDR_252e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("EDR_252", "enemy stats", 1))) }));
			cards.Add("EDR_252e1", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("EDR_252", "friendly stats", 3))) }));
			// [EDR_253] Ursine Maul (4) — After your hero attacks, draw a card.
			cards.Add("EDR_253", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("EDR_253", "draw", 1); i++) Generic.Draw(c); })
				}
			}));
			// [EDR_255] Renewing Flames (7) — Lifesteal. Deal $5 damage to the lowest Health enemy, twice.
			// (live night 113357: per hit ReuseFX_BreathOfDreams_FX around the damage; the second sent empty when no enemy is left)
			cards.Add("EDR_255", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("EDR_255", "hits", 2); i++)
					{
						ICharacter target = LowestHealthEnemy(g, c);
						if (target != null && (target.IsDead || target.ToBeDestroyed)) target = null;
						Generic.NamedFx((IPlayable)s, "BreathOfDreams", target != null ? new[] { target.Id } : new int[0], () =>
						{
							if (target != null) Generic.DamageCharFunc((IPlayable)s, target, V("EDR_255", "damage", 5), true);
						});
						g.DeathProcessingAndAuraUpdate();
					}
				})
			}));
			// [EDR_256] Dreamwarden (4) — Taunt. Battlecry: If there is a card in your deck that didn't start there, draw it and gain +2/+2.
			cards.Add("EDR_256", new CardDef(new Power
			{
				GlowWhile = p => p.Controller.DeckZone.Any(Generic.DidntStartInDeck),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!c.DeckZone.Any(Generic.DidntStartInDeck)) return;
					DrawOne(g, c, Generic.DidntStartInDeck);
					if (s is Minion me) Give(g, s, me, "EDR_256e");
				})
			}));
			cards.Add("EDR_256e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("EDR_256", "stats", 2))) }));
			// [EDR_257] Lightmender (4) — Taunt. Choose One - +3 Attack and Divine Shield; or +3 Health and Lifesteal.
			// (live vfx 181571: the keyword set on the minion, the enchantment carries the stats)
			cards.Add("EDR_257", new CardDef(new Power()));
			cards.Add("EDR_257a", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me)) return;
					me.HasDivineShield = true;
					Give(g, s, me, "EDR_257ae");
				})
			}));
			cards.Add("EDR_257b", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me)) return;
					me[GameTag.LIFESTEAL] = 1;
					Give(g, s, me, "EDR_257be");
				})
			}));
			cards.Add("EDR_257ae", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("EDR_257", "attack", 3))) }));
			cards.Add("EDR_257be", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("EDR_257", "health", 3))) }));
			// [EDR_258] Toreth the Unbreaking (5) — Divine Shield, Taunt. Your Divine Shields take three hits to break.
			// (the rule: Character.TakeDamage + Generic.SyncShieldHits on HsTag.DIVINE_SHIELD_HITS)
			cards.Add("EDR_258", new CardDef(new Power
			{
				Aura = new Aura(AuraType.CONTROLLER, new Effect(HsTag.DIVINE_SHIELD_HITS, EffectOperator.ADD, V("EDR_258", "hits", 3)))
			}));
			// [EDR_259] Ursol (8) — Battlecry: Cast the highest Cost spell from your hand as an Aura that lasts @ turns.
			// (wiki: "The original spell is removed from the player's hand"; Ursol's Aura casts it at the end of each of your turns —
			// a new copy cast by an effect; TAG_SCRIPT_DATA_NUM_1 = the spell's entity, NUM_2 = the turns left)
			cards.Add("EDR_259", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var spells = c.HandZone.GetAll().Where(p => p is Spell).ToList();
					if (spells.Count == 0) return;
					int top = spells.Max(p => p.Cost);
					var best = spells.Where(p => p.Cost == top).ToList();
					IPlayable spell = best[g.Random.Next(best.Count)];
					c.HandZone.Remove(spell);
					c.SetasideZone.Add(spell);
					var aura = (Spell)Entity.FromCard(c, Cards.FromId("EDR_259e1"), new EntityData
					{
						[GameTag.CREATOR] = s.Id, [GameTag.TAG_SCRIPT_DATA_NUM_1] = spell.Id, [GameTag.TAG_SCRIPT_DATA_NUM_2] = V("EDR_259", "turns", 3)
					}, c.SetasideZone);
					aura.Zone.Remove(aura);
					new CastRandomSpellTask().Process(g, c, s, aura);
				})
			}));
			cards.Add("EDR_259e1", new CardDef(PaladinAura(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Spell aura) || aura.Zone?.Type != Zone.SECRET) return;
						if (g.IdEntityDic.TryGetValue(aura[GameTag.TAG_SCRIPT_DATA_NUM_1], out IPlayable stored) && stored.Card.Type == CardType.SPELL)
						{
							IPlayable copy = Entity.FromCard(c, stored.Card, new EntityData { [GameTag.CREATOR] = aura.Id, [(GameTag)1715] = aura.Id });
							new CastRandomSpellTask().Process(g, c, aura, copy);
						}
						// (2026-10-08: live keeps NUM_2 at its 3 and counts the turns in NUM_3 — Generic.ObjectiveTick)
					})
				}
			})));
			// [EDR_264] Aegis of Light (2) — Summon a random 2-Cost minion and give it Taunt. Imbue your Hero Power.
			cards.Add("EDR_264", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (SummonGet(g, c, RandomMinionOfCost(g, V("EDR_264", "cost", 2)), s) is Minion m) m[GameTag.TAUNT] = 1;
					Generic.Imbue(c, s);
				})
			}));
			// [EDR_451] Goldpetal Drake (3) — Battlecry and Deathrattle: Imbue your Hero Power.
			cards.Add("EDR_451", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.Imbue(c, s)),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => Generic.Imbue(c, s))
			}));
			// [FIR_914] Smoldering Strength (1) — Give a friendly minion +{0}/+{0}. (Upgrades each turn, but discards after {1}!)
			cards.Add("FIR_914", new CardDef(TargetFriendlyMinionToPlay, new Power
			{
				Trigger = Generic.SmolderingInHand(),
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m) Give(g, s, m, "FIR_914e", Math.Max(1, Num1(s))); })
			}));
			cards.Add("FIR_914e", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackHealthScriptTag }));
			// [FIR_941] Searing Reflection (7) — Draw a minion. Summon an 8/8 copy of it with Divine Shield.
			// (rule book DC4: a burned card "is not drawn" — no copy then)
			cards.Add("FIR_941", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(DrawOne(g, c, p => p is Minion) is IPlayable drawn) || c.BoardZone.IsFull) return;
					Generic.Copy(c, s, drawn, Zone.PLAY, prepare: copy =>
					{
						Give(g, s, copy, "FIR_941e1");
						if (copy is Minion m) m.HasDivineShield = true;
					});
				})
			}));
			cards.Add("FIR_941e1", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("FIR_941", "stats", 8))) }));
			// [FIR_961] Ashleaf Pixie (2) — Battlecry: If you're holding a spell that costs (5) or more, gain Divine Shield and Lifesteal.
			cards.Add("FIR_961", new CardDef(new Power
			{
				GlowWhile = p => p.Controller.HandZone.Any(x => x is Spell && x.Id != p.Id && x.Cost >= V("FIR_961", "spell cost", 5)),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me) || !c.HandZone.Any(x => x is Spell && x.Cost >= V("FIR_961", "spell cost", 5))) return;
					me.HasDivineShield = true;
					me[GameTag.LIFESTEAL] = 1;
				})
			}));

			// ---------------------------------------------------------------- Priest
			// [EDR_449] Lunarwing Messenger (2) — Lifesteal. Battlecry: Imbue your Hero Power.
			cards.Add("EDR_449", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Generic.Imbue(c, s)) }));
			// [EDR_460] Wish of the New Moon (3) — Deal $6 damage to a minion. (Cast @ spells to gain Lifesteal.)
			cards.Add("EDR_460", new CardDef(TargetMinion, new Power
			{
				Trigger = Generic.CastSpellsToUpgrade(),
				PowerTask = new DamageTask(V("EDR_460", "damage", 6), EntityType.TARGET, true)
			}));
			cards.Add("EDR_460t", new CardDef(TargetMinion, new Power { PowerTask = new DamageTask(V("EDR_460", "damage", 6), EntityType.TARGET, true) }));
			// [EDR_461] Ritual of the New Moon (5) — Summon two random 3-Cost minions. (Cast @ spells to summon 6-Cost minions instead.)
			cards.Add("EDR_461", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				Trigger = Generic.CastSpellsToUpgrade(),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("EDR_461", "minions", 2); i++) SummonGet(g, c, RandomMinionOfCost(g, V("EDR_461", "cost", 3)), s);
				})
			}));
			cards.Add("EDR_461t", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("EDR_461t", "minions", 2); i++) SummonGet(g, c, RandomMinionOfCost(g, V("EDR_461t", "cost", 6)), s);
				})
			}));
			// [EDR_462] Selenic Drake (4) — Elusive. At the end of your turn, get a random Dragon. (any class: live gave a Paladin Dragon)
			cards.Add("EDR_462", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.DRAGON)) is Card dragon) AddToHand(c, dragon);
					})
				}
			}));
			// [EDR_463] Twilight Influence (2) — Choose One - Destroy a minion with 3 or less Attack; or Summon a random 2-Cost minion.
			var influence = new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_TARGET_MAX_ATTACK, V("EDR_463", "max attack", 3) } };
			cards.Add("EDR_463", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_TARGET_MAX_ATTACK, V("EDR_463", "max attack", 3) } }, new Power()));
			cards.Add("EDR_463a", new CardDef(influence, new Power { PowerTask = new DestroyTask(EntityType.TARGET) }));
			cards.Add("EDR_463b", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => SummonGet(g, c, RandomMinionOfCost(g, V("EDR_463", "cost", 2)), s))
			}));
			// [EDR_464] Tyrande (7) — Battlecry: The next 3 spells you play cast twice. (spells cast by effects neither double nor use a
			// charge: HsTag.PLAYED_SPELLS_CAST_TWICE, CastSpell; the player enchantment shows it and leaves at 0)
			cards.Add("EDR_464", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					c[HsTag.PLAYED_SPELLS_CAST_TWICE] = c[HsTag.PLAYED_SPELLS_CAST_TWICE] + V("EDR_464", "spells", 3);
					Generic.AddEnchantmentBlock(g, Cards.FromId("EDR_464e2"), (IPlayable)s, c, 0, 0, 0);
				})
			}));
			cards.Add("EDR_464e2", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Enchantment e && c[HsTag.PLAYED_SPELLS_CAST_TWICE] <= 0) e.Remove(); })
				}
			}));
			// [EDR_472] Weaver of the Cycle (3) — Battlecry: If you're holding a spell that costs (5) or more, deal 3 damage.
			cards.Add("EDR_472", new CardDef(BattlecryAny, new Power
			{
				TargetIf = (PlayReq.REQ_TARGET_IF_AVAILABLE, (src, target) => src.Controller.HandZone.Any(p => p is Spell && p.Id != src.Id && p.Cost >= V("EDR_472", "spell cost", 5))),
				GlowWhile = p => p.Controller.HandZone.Any(x => x is Spell && x.Id != p.Id && x.Cost >= V("EDR_472", "spell cost", 5)),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter ch && c.HandZone.Any(p => p is Spell && p.Cost >= V("EDR_472", "spell cost", 5)))
						Generic.DamageCharFunc((IPlayable)s, ch, V("EDR_472", "damage", 3), false);
				})
			}));
			// [EDR_476] Moonwell (6) — Deal $4 damage to all enemy characters. Restore #4 Health to all friendly characters.
			cards.Add("EDR_476", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) => DamageAll(s, Enemies(c), V("EDR_476", "damage", 4), true)),
					new HealTask(V("EDR_476", "heal", 4), EntityType.FRIENDS))
			}));
			// [EDR_895] Aviana, Elune's Chosen (9) — Battlecry: Start a three turn lunar cycle. When the Full Moon rises, your cards cost
			// (1) this game. (wiki: "At the start of the player's next 3 turns, the countdown goes down by 1"; then "the player's cards will
			// cost 1 for the rest of the game"; "Cards can be further discounted to 0")
			cards.Add("EDR_895", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					Generic.AddEnchantmentBlock(g, Cards.FromId("EDR_895t"), (IPlayable)s, c, V("EDR_895", "turns", 3), 0, 0))
			}));
			cards.Add("EDR_895t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e)) return;
						int left = e[GameTag.TAG_SCRIPT_DATA_NUM_1] - 1;
						e[GameTag.TAG_SCRIPT_DATA_NUM_1] = Math.Max(0, left);
						if (left > 0) return;
						IPlayable creator = e.Creator;
						e.Remove();
						Generic.AddEnchantmentBlock(g, Cards.FromId("EDR_895e"), creator, c, 0, 0, 0);
					})
				}
			}));
			cards.Add("EDR_895e", new CardDef(new Power { Aura = new Aura(AuraType.HAND, Effects.SetCost(V("EDR_895", "cost", 1))) }));
			// [EDR_970] Kaldorei Priestess (3) — Battlecry: Give all enemy minions -2 Attack until your next turn. Imbue your Hero Power.
			cards.Add("EDR_970", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					GiveAll(g, s, c.Opponent.BoardZone.GetAll(), "EDR_970e");
					Generic.Imbue(c, s);
				})
			}));
			cards.Add("EDR_970e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(-V("EDR_970", "attack", 2))),
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = RemoveEnchantmentTask.Task }
			}));
			// [FIR_777] Spirit of the Kaldorei (2) — Taunt, Lifesteal. Battlecry: If you used your Hero Power this turn, gain +3/+3.
			cards.Add("FIR_777", new CardDef(new Power
			{
				GlowWhile = p => p.Controller.HeroPowerActivationsThisTurn > 0,
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (c.HeroPowerActivationsThisTurn > 0 && s is Minion me) Give(g, s, me, "FIR_777e2"); })
			}));
			cards.Add("FIR_777e2", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("FIR_777", "stats", 3))) }));
			// [FIR_916] Smoldering Ascent (2) — Deal ${0} damage to all enemy minions. (Upgrades each turn, but discards after {1}!)
			cards.Add("FIR_916", new CardDef(new Power
			{
				Trigger = Generic.SmolderingInHand(),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					DamageAll(s, c.Opponent.BoardZone.GetAll().Cast<ICharacter>().ToList(), Math.Max(1, Num1(s)), true))
			}));
			// [FIR_918] Light of the New Moon (3) — Give a minion +3/+3. (Cast @ spells to return this to your hand when played.)
			cards.Add("FIR_918", new CardDef(TargetMinion, new Power
			{
				Trigger = Generic.CastSpellsToUpgrade(),
				PowerTask = new AddEnchantmentTask("FIR_918e1", EntityType.TARGET)
			}));
			// [FIR_918t] Light of the Full Moon — Give a minion +3/+3. Add a Light of the New Moon to your hand. (a new card: a fresh count)
			cards.Add("FIR_918t", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m) Give(g, s, m, "FIR_918e1");
					var related = (ReprintCards.RelatedCardIds(((IPlayable)s).Card.Id) ?? new List<string>()).Select(Cards.TryFromId)
						.FirstOrDefault(x => x != null && x.Type == CardType.SPELL && x.Id != ((IPlayable)s).Card.Id);
					AddToHand(c, related ?? Cards.FromId("FIR_918"));
				})
			}));
			cards.Add("FIR_918e1", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("FIR_918", "stats", 3))) }));

			// ---------------------------------------------------------------- Rogue
			// [EDR_521] Tricky Satyr (3) — Battlecry: Get a copy of the lowest Cost card in your opponent's hand.
			cards.Add("EDR_521", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var theirs = c.Opponent.HandZone.GetAll().ToList();
					if (theirs.Count == 0 || c.HandZone.IsFull) return;
					int low = theirs.Min(p => p.Cost);
					var lowest = theirs.Where(p => p.Cost == low).ToList();
					Generic.Copy(c, s, lowest[g.Random.Next(lowest.Count)], Zone.HAND);
				})
			}));
			// [EDR_522] Mimicry (1) — Your opponent draws 2 cards. You get copies of them. (a burned card is not drawn: rule book DC4)
			cards.Add("EDR_522", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("EDR_522", "draws", 2); i++)
						if (Generic.Draw(c.Opponent) is IPlayable drawn && drawn.Zone?.Type == Zone.HAND && drawn.Controller == c.Opponent && !c.HandZone.IsFull)
							Generic.Copy(c, s, drawn, Zone.HAND);
				})
			}));
			// [EDR_523] Web of Deception (2) — Return a friendly minion to your hand to summon a 4/4 Spider with Stealth.
			// (live 115158 9602: the Spider takes the returned minion's place)
			cards.Add("EDR_523", new CardDef(TargetFriendlyMinionToPlay, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m) || m.Zone?.Type != Zone.PLAY) return;
					int position = m.ZonePosition;
					Generic.ReturnToHandBlock.Invoke(c, m);
					SummonGet(g, c, Cards.FromId("EDR_523t"), s, Math.Min(position, c.BoardZone.Count));
				})
			}));
			// [EDR_524] Shadowcloaked Assailant (4) — Battlecry: If you're holding one of the same cards as your opponent, shuffle theirs
			// into their deck. (wiki: "always only shuffle one card"; "A random one of eligible options is picked")
			cards.Add("EDR_524", new CardDef(new Power
			{
				GlowWhile = p => p.Controller.Opponent.HandZone.Any(x => p.Controller.HandZone.Any(y => y.Id != p.Id && y.Card.Id == x.Card.Id)),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var same = c.Opponent.HandZone.GetAll().Where(x => c.HandZone.Any(y => y.Card.Id == x.Card.Id)).ToList();
					if (same.Count == 0 || c.Opponent.DeckZone.IsFull) return;
					IPlayable theirs = same[g.Random.Next(same.Count)];
					theirs.Zone.Remove(theirs);
					Generic.ShuffleIntoDeck.Invoke(c.Opponent, s, theirs);
				})
			}));
			// [EDR_525] Barbed Thorn (3) — Choose One - Gain Poisonous this turn; or Gain "Deathrattle: Deal 2 damage to all enemies."
			// (a Choose One weapon: PlayWeapon passes the option; the weapon gains it)
			cards.Add("EDR_525", new CardDef(new Power()));
			cards.Add("EDR_525A", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => { if (s is Weapon w) Give(g, s, w, "EDR_525e1"); }) }));
			cards.Add("EDR_525B", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => { if (s is Weapon w) Give(g, s, w, "EDR_525e"); }) }));
			cards.Add("EDR_525e1", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.POISONOUS, EffectOperator.SET, 1)) { IsOneTurnEffect = true } }));
			cards.Add("EDR_525e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.DEATHRATTLE, EffectOperator.SET, 1)),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => DamageAll(s, Enemies(c), V("EDR_525", "damage", 2), false))
			}));
			// [EDR_526] Renferal, the Malignant (3) — Battlecry: Trap @ random cards in your opponent's hand for a turn. (Improved for each
			// time you've played this.) (live 185040 1963: the missile at the enemy hero, the hidden Webbed! lock until the end of their turn)
			Func<IPlayable, int> renferal = p => V("EDR_526", "cards", 1) + p.Controller.PlayHistory.Count(h => h.SourceCard?.Id == p.Card.Id && h.SourceId != p.Id);
			cards.Add("EDR_526", new CardDef(new Power
			{
				HandCounter = renferal,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = renferal((IPlayable)s);
					Generic.NamedFx((IPlayable)s, "Missile", new[] { c.Opponent.Hero.Id }, () => { });
					foreach (IPlayable card in c.Opponent.HandZone.GetAll().Where(p => p[HsTag.LOCKED_UNTIL_TURN] == 0).OrderBy(_ => g.Random.Next()).Take(n).ToList())
						Generic.LockInHand(card, s, g.Turn + 1, "EDR_526e");
				})
			}));
			cards.Add("EDR_526e", new CardDef(new Power { NoScript = true }));
			// [EDR_527] Ashamane (9) — Battlecry: Fill your hand with copies of cards from your opponent's deck. They cost (3) less.
			cards.Add("EDR_527", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var left = c.Opponent.DeckZone.GetAll().ToList();
					while (!c.HandZone.IsFull && left.Count > 0)
					{
						IPlayable from = left[g.Random.Next(left.Count)];
						left.Remove(from);
						if (Generic.Copy(c, s, from, Zone.HAND) is IPlayable copy && copy.Zone?.Type == Zone.HAND)
							Give(g, s, copy, GenericCostLess(V("EDR_527", "less", 3)));
					}
				})
			}));
			// [EDR_528] Nightmare Fuel (1) — Discover a copy of a minion in your opponent's deck. Combo: With a Dark Gift.
			cards.Add("EDR_528", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Card[] offer = Pick(g, c.Opponent.DeckZone.GetAll().Where(p => Generic.IsMinionCard(p)).Select(p => p.Card).GroupBy(x => x.Id).Select(x => x.First()), 3).ToArray();
					if (offer.Length > 0)
						Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.HAND, offer, null);
				}),
				ComboTask = new CustomTask((g, c, s, t, stack) =>
					DiscoverWithDarkGift(g, c, s, c.Opponent.DeckZone.GetAll().Where(p => Generic.IsMinionCard(p)).Select(p => p.Card).GroupBy(x => x.Id).Select(x => x.First())))
			}));
			// [EDR_540] Twisted Webweaver (2) — Whenever you play another minion you've already played, draw a card.
			// (wiki: "multiple cards with the same ID"; "Cards with the same name but different IDs don't count")
			cards.Add("EDR_540", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p.Controller.PlayHistory.Count(h => h.SourceCard?.Id == p.Card.Id) >= 2),
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (t != null && t.Id != s.Id) Generic.Draw(c); })
				}
			}));
			// [EDR_781] Harbinger of the Blighted (3) — Whenever this enters your hand from the battlefield, summon two random 2-Cost minions.
			// (Generic.ReturnToHandBlock marks the card while it enters the hand: HsTag.ENTERED_HAND_FROM_PLAY)
			cards.Add("EDR_781", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ADD_TO_HAND)
				{
					TriggerActivation = TriggerActivation.HAND,
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s[HsTag.ENTERED_HAND_FROM_PLAY] == 0) return;
						for (int i = 0; i < V("EDR_781", "minions", 2); i++) SummonGet(g, c, RandomMinionOfCost(g, V("EDR_781", "cost", 2)), s);
					})
				}
			}));
			// [FIR_919] Everburning Phoenix (4) — Costs (1) less for each card you've played this turn. Deathrattle: At end of turn, get
			// another Phoenix. (the player enchantment FIR_919e gives it at the end of the current turn, then leaves)
			cards.Add("FIR_919", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.NumCardsPlayedThisTurn),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => Generic.AddEnchantmentBlock(g, Cards.FromId("FIR_919e"), (IPlayable)s, c, 0, 0, 0))
			}));
			cards.Add("FIR_919e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					EitherTurn = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e)) return;
						AddToHand(c, e.Creator?.Card ?? Cards.FromId("FIR_919"));
						e.Remove();
					})
				}
			}));
			// [FIR_920] Smoke Bomb (2) — Discover a Combo, Battlecry, or Stealth minion with a Dark Gift. (live 185040 77257: one of each,
			// in that order, class or neutral)
			cards.Add("FIR_920", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var pool = Cards.GenerationPool(g, c).Where(x => x.Type == CardType.MINION && (x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL))).ToList();
					var options = new List<Card>();
					foreach (GameTag kind in new[] { GameTag.COMBO, GameTag.BATTLECRY, GameTag.STEALTH })
						if (Pick(g, pool.Where(x => x[kind] > 0 && options.All(o => o.Id != x.Id)), 1).FirstOrDefault() is Card one) options.Add(one);
					DiscoverWithDarkGift(g, c, s, null, options: options);
				})
			}));
			// [FIR_922] Cindersword (1) — Battlecry: If you're holding a minion with a Dark Gift, gain +3 Attack.
			cards.Add("FIR_922", new CardDef(new Power
			{
				GlowWhile = p => p.Controller.HandZone.Any(x => Generic.IsMinionCard(x) && HasDarkGift(x)),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (s is Weapon w && c.HandZone.Any(x => Generic.IsMinionCard(x) && HasDarkGift(x))) Give(g, s, w, "FIR_922e");
				})
			}));
			cards.Add("FIR_922e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("FIR_922", "attack", 3))) }));
		}
	}
}
