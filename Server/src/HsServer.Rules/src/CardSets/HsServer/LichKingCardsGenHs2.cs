// HsServer addition (not upstream SabberStone): March of the Lich King — Demon Hunter, Death Knight, Druid, Hunter and Mage
// (see LichKingCardsGenHs.cs for the set's shared pieces).
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

		/// <summary>An Outcast card at random (the client's OUTCAST keyword).</summary>
		private static Card RandomOutcast(Game g) => RandomCard(g, x => x[OUTCAST] > 0);

		/// <summary>Deck "has no minions" (the DH / Mage "no minions" cards).</summary>
		private static bool NoMinionsInDeck(Controller c) => !c.DeckZone.Any(p => Generic.IsMinionCard(p));

		/// <summary>The lowest-Health enemy character (ties at random), or null.</summary>
		private static ICharacter LowestHealthEnemy(Game g, Controller c)
		{
			var enemies = Enemies(c).Where(x => !x.IsDead).ToList();
			if (enemies.Count == 0) return null;
			int min = enemies.Min(x => x.Health);
			var lowest = enemies.Where(x => x.Health == min).ToList();
			return lowest[g.Random.Next(lowest.Count)];
		}

		private static void LichKingClasses1(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Demon Hunter
			// [NX2_024] Shambling Chow (1) — Rush. Deathrattle: Deal 4 damage to your hero.
			cards.Add("NX2_024", new CardDef(new Power { DeathrattleTask = new DamageTask(V("NX2_024", "damage", 4), EntityType.HERO) }));
			// [NX2_025] Calamity's Grasp (1) — Deathrattle: Add a random Outcast card to your hand.
			cards.Add("NX2_025", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => { if (RandomOutcast(g) is Card o) AddToHand(c, o); }) }));
			// [NX2_026] Felscale Evoker (6) — Battlecry: If you've cast three spells while holding this, summon a different Demon from your deck.
			cards.Add("NX2_026", new CardDef(HeldSpell(p => p is Spell, new CustomTask((g, c, s, t, stack) =>
				SummonFromDeck(g, c, s, p => IsDemon(p) && p.Card.Id != ((IPlayable)s).Card.Id)))));
			// [RLK_206] Mark of Scorn (2) — Draw a card. If it's not a minion, deal 4 damage to the lowest Health enemy.
			cards.Add("RLK_206", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					IPlayable drawn = Generic.Draw(c);
					if (drawn != null && !(drawn is Minion) && LowestHealthEnemy(g, c) is ICharacter low)
						Generic.DamageCharFunc((IPlayable)s, low, V("RLK_206", "damage", 4), true);
				})
			}));
			// [RLK_207] Fierce Outsider (1) — Rush. Outcast: Your next Outcast card costs (1) less.
			cards.Add("RLK_207", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new ConditionTask(EntityType.SOURCE, PlayedAsOutcast), new FlagTask(true, new AddEnchantmentTask("RLK_207e", EntityType.CONTROLLER)))
			}));
			cards.Add("RLK_207e", NextCardCost(Effects.ReduceCost(V("RLK_207", "discount", 1)), IsOutcastCard, "GBL_003e"));
			// [RLK_208] Fel'dorei Warband (4) — Deal 4 damage. If your deck has no minions, summon four 1/1 Illidari with Rush.
			cards.Add("RLK_208", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("RLK_208", "damage", 4), EntityType.TARGET, true),
					ComplexTask.If((g, c, s) => NoMinionsInDeck(c), ComplexTask.Repeat(new SummonTask("BT_036t", SummonSide.SPELL), V("RLK_208", "illidari", 4))))
			}));
			// [RLK_209] Unleash Fel (1) — Deal 1 damage to all enemies. Manathirst (6): With Lifesteal.
			cards.Add("RLK_209", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) => { if (Generic.Manathirst((IPlayable)s)) s[GameTag.LIFESTEAL] = 1; }),
					new DamageTask(V("RLK_209", "damage", 1), EntityType.ENEMIES, true))
			}));
			// [RLK_210] Wretched Exile (2) — After you play an Outcast card, add a random Outcast card to your hand.
			cards.Add("RLK_210", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = IsOutcastCard,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (RandomOutcast(g) is Card o) AddToHand(c, o); })
				}
			}));
			// [RLK_211] Deal with a Devil (5) — Summon two 3/3 Felfiends with Lifesteal. If your deck has no minions, summon two more.
			cards.Add("RLK_211", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = V("RLK_211", "felfiends", 2) * (NoMinionsInDeck(c) ? 2 : 1);
					for (int i = 0; i < n; i++) Summon(g, c, Cards.FromId("RLK_211t"), s);
				})
			}));
			// [RLK_212] Brutal Annihilan (9) — Taunt, Rush. After this minion survives damage, deal that amount to the enemy hero.
			cards.Add("RLK_212", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						int amount = g.CurrentEventData?.EventNumber ?? 0;
						if (s is Minion me && !me.IsDead && !me.ToBeDestroyed && amount > 0)
							Generic.DamageCharFunc(me, c.Opponent.Hero, amount, false);
					})
				}
			}));
			// [RLK_213] Vengeful Walloper (7) — Rush. Costs (1) less for each Outcast card you've played this game.
			cards.Add("RLK_213", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.PlayHistory.Count(h => h.SourceCard != null && h.SourceCard[OUTCAST] > 0))
			}));
			// [RLK_214] Souleater's Scythe (4, weapon) — Start of Game: Consume 3 different minions in your deck. Leave behind
			// Souls that Discover them. (Each consumed minion's place in the deck gets a Bound Soul; a Soul Discovers one of
			// the consumed three.)
			cards.Add("RLK_214", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.GAME_START)
				{
					TriggerActivation = TriggerActivation.DECK,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is IPlayable scythe) || !Generic.StartOfGameHolds(scythe)) return;
						var kinds = c.DeckZone.Where(p => Generic.IsMinionCard(p)).GroupBy(p => p.Card.Id).Select(x => x.First()).ToList();
						var eaten = new List<IPlayable>();
						for (int i = 0; i < V("RLK_214", "consumed", 3) && kinds.Count > 0; i++)
						{
							IPlayable pick = kinds[g.Random.Next(kinds.Count)];
							kinds.Remove(pick);
							eaten.Add(pick);
						}
						for (int i = 0; i < eaten.Count; i++)
						{
							c[HsTag.SCYTHE_SOUL_1 + i] = eaten[i].Card.AssetId;
							int pos = eaten[i].ZonePosition;
							c.DeckZone.Remove(eaten[i]);
							c.SetasideZone.Add(eaten[i]);
							IPlayable soul = Entity.FromCard(c, Cards.FromId("RLK_214t"));
							c.DeckZone.Add(soul, System.Math.Min(pos, c.DeckZone.Count));
						}
					})
				}
			}));
			cards.Add("RLK_214t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var souls = Enumerable.Range(0, 3).Select(i => c[HsTag.SCYTHE_SOUL_1 + i]).Where(dbf => dbf > 0)
						.Select(dbf => Cards.FromAssetId(dbf)).Where(x => x != null).ToArray();
					if (souls.Length > 0 && !c.HandZone.IsFull)
						Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.HAND, souls, null);
				})
			}));
			// [RLK_215] Felerin, the Forgotten (4) — Battlecry: Add a random Outcast card to the left and right sides of your hand. They cost (2) less.
			cards.Add("RLK_215", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (bool left in new[] { true, false })
					{
						if (c.HandZone.IsFull || !(RandomOutcast(g) is Card o)) continue;
						IPlayable p = Entity.FromCard(c, o);
						if (Generic.AddHandPhaseAt(c, p, left ? 0 : -1)) Give(g, s, p, "GBL_002e");
					}
				})
			}));

			// ---------------------------------------------------------------- Death Knight
			// [NX2_035] Rimescale Siren (3) — Battlecry: If you've cast three spells while holding this, Freeze 3 random enemy minions.
			cards.Add("NX2_035", new CardDef(HeldSpell(p => p is Spell, new CustomTask((g, c, s, t, stack) =>
				FreezeAll(PickMinions(g, c.Opponent.BoardZone.Where(m => !m.IsDead), V("NX2_035", "frozen", 3)))))));
			// [NX2_036] Construct Quarter (3) — Destroy a friendly minion to summon a 4/5 Undead with Rush.
			cards.Add("NX2_036", new CardDef(TargetFriendlyMinion, new Power
			{
				PowerTask = ComplexTask.Create(new DestroyTask(EntityType.TARGET), new SummonTask("NX2_036t", SummonSide.SPELL))
			}));
			// [NX2_037] Frost Queen Sindragosa (7) — Colossal +2. After an enemy minion is Frozen, destroy it.
			cards.Add("NX2_037", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.FROZEN)
				{
					TriggerSource = TriggerSource.OP_MINIONS,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m && !m.IsDead) Generic.Destroy(m, s); })
				}
			}));
			// [RLK_120] Meat Grinder (3) — Battlecry: Shred a random minion in your deck to gain 4 Corpses.
			cards.Add("RLK_120", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.DeckZone.Where(p => Generic.IsMinionCard(p)).ToList();
					if (minions.Count == 0) return;
					Generic.Destroy(minions[g.Random.Next(minions.Count)], s);
					Generic.GainCorpses(c, s, V("RLK_120", "corpses", 4));
				})
			}));
			// [RLK_225] Blightfang (3) — Battlecry: Infect all enemy minions. When they die, you summon a 2/2 Zombie with Taunt.
			cards.Add("RLK_225", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.Opponent.BoardZone.Where(m => !m.IsDead), "RLK_225e"))
			}));
			cards.Add("RLK_225e", new CardDef(new Power
			{
				// "for your opponent": the infected minion's opponent (Blightfang's player)
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => Summon(g, c.Opponent, Cards.FromId("RLK_118t3"), s))
			}));

			// ---------------------------------------------------------------- Druid
			// [NX2_010] Death Beetle (6) — Taunt. Manathirst (@): Gain +4/+4 and Charge.
			cards.Add("NX2_010", new CardDef(new Power
			{
				PowerTask = ManathirstOr(null, ComplexTask.Create(new AddEnchantmentTask("NX2_010e", EntityType.SOURCE),
					new CustomTask((g, c, s, t, stack) => { if (s is Minion m) m.HasCharge = true; })))
			}));
			cards.Add("NX2_010e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("NX2_010", "stats", 4))) }));
			// [NX2_011] Life from Death (6) — Draw 3 cards. Infuse (@): This costs (1). (The infused version: NX2_011t.)
			var lifeFromDeath = new CardDef(new Power { PowerTask = ComplexTask.Repeat(new DrawTask(), V("NX2_011", "draw", 3)) });
			cards.Add("NX2_011", lifeFromDeath);
			cards.Add("NX2_011t", new CardDef(new Power { PowerTask = ComplexTask.Repeat(new DrawTask(), V("NX2_011", "draw", 3)) }));
			// [NX2_012] Rake (2) — Give your hero +2 Attack this turn. Deal damage equal to your hero's Attack to a minion.
			cards.Add("NX2_012", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("NX2_012e", EntityType.HERO),
					new CustomTask((g, c, s, t, stack) => { if (t is Minion m) Generic.DamageCharFunc((IPlayable)s, m, c.Hero.AttackDamage, true); }))
			}));
			cards.Add("NX2_012e", OneTurn(Effects.Attack_N(V("NX2_012", "attack", 2))));
			// [RLK_650] Lingering Zombie (1) — Deathrattle: Summon a 1/1 Disarmed Zombie with "Deathrattle: Summon a 1/1 Zombie."
			cards.Add("RLK_650", new CardDef(new Power { DeathrattleTask = new SummonTask("RLK_650t", SummonSide.DEATHRATTLE) }));
			cards.Add("RLK_650t", new CardDef(new Power { DeathrattleTask = new SummonTask("RLK_650t2", SummonSide.DEATHRATTLE) }));
			// [RLK_651] Crypt Keeper (8) — Taunt. Costs (1) less for each Armor you have.
			cards.Add("RLK_651", new CardDef(new Power { Aura = new AdaptiveCostEffect(p => p.Controller.Hero.Armor) }));
			// [RLK_652] Unending Swarm (6) — Resurrect all friendly minions that cost (2) or less.
			cards.Add("RLK_652", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable dead in c.DiedThisGameCards.Where(p => p is Minion && p.Card.Cost <= V("RLK_652", "cost", 2)).ToList())
						Summon(g, c, dead.Card, s);
				})
			}));
			// [RLK_654] Beetlemancy (5) — Choose One - Gain 12 Armor; or Summon two 3/3 Beetles with Taunt.
			cards.Add("RLK_654a", new CardDef(new Power { PowerTask = new ArmorTask(V("RLK_654", "armor", 12)) }));
			cards.Add("RLK_654b", new CardDef(new Power { PowerTask = new SummonTask("RLK_654t", V("RLK_654", "beetles", 2), SummonSide.SPELL) }));
			// [RLK_655] Wither (1) — Choose a minion. Each friendly Undead steals 1 Attack and Health from it.
			cards.Add("RLK_655", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion victim)) return;
					foreach (Minion undead in c.BoardZone.Where(m => !m.IsDead && m != victim && m.IsRace(Race.UNDEAD)).ToList())
					{
						if (victim.IsDead || victim.Zone?.Type != Zone.PLAY) break;
						Give(g, s, victim, "RLK_655e");
						Give(g, s, undead, "RLK_655e2");
					}
				})
			}));
			cards.Add("RLK_655e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(-V("RLK_655", "steal", 1)), Effects.Health_N(-V("RLK_655", "steal", 1))) }));
			cards.Add("RLK_655e2", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("RLK_655", "steal", 1))) }));
			// [RLK_656] Chitinous Plating (2) — Gain 4 Armor. At the start of your next turn, gain 4 more.
			cards.Add("RLK_656", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new ArmorTask(V("RLK_656", "armor", 4)), new AddEnchantmentTask("RLK_656e", EntityType.CONTROLLER))
			}));
			cards.Add("RLK_656e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = ComplexTask.Create(new ArmorTask(V("RLK_656", "armor", 4)), RemoveEnchantmentTask.Task)
				}
			}));
			// [RLK_658] Elder Nadox (5) — Battlecry: Destroy a friendly Undead. Your minions gain its Attack.
			cards.Add("RLK_658", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_RACE, (int)Race.UNDEAD } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion undead)) return;
					int attack = undead.AttackDamage;
					Generic.Destroy(undead, s);
					foreach (Minion m in c.BoardZone.Where(x => !x.IsDead && x != undead && !x.ToBeDestroyed).ToList())
						Generic.AddEnchantmentBlock(g, Cards.FromId("RLK_658e"), (IPlayable)s, m, attack, 0, 0);
				})
			}));
			cards.Add("RLK_658e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0)) { UseScriptTag = true } }));
			// [RLK_659] Anub'Rekhan (8) — Battlecry: Gain 5 Armor. Your next 3 minions this turn cost Armor instead of Mana.
			// (TAG_CARD_ALTERNATE_COST ARMOR = 2 on the minions in hand: Playable.CostsArmor.)
			cards.Add("RLK_659", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new ArmorTask(V("RLK_659", "armor", 5)),
					new CustomTask((g, c, s, t, stack) => Generic.AddEnchantmentBlock(g, Cards.FromId("RLK_659e"), (IPlayable)s, c, V("RLK_659", "minions", 3), 0, 0)))
			}));
			cards.Add("RLK_659e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, "RLK_659e2") { Condition = SelfCondition.IsMinion },
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.PLAY_MINION)
					{
						TriggerSource = TriggerSource.FRIENDLY,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (!(s is Enchantment e)) return;
							e[GameTag.TAG_SCRIPT_DATA_NUM_1] -= 1;
							if (e[GameTag.TAG_SCRIPT_DATA_NUM_1] <= 0) e.Remove();
						})
					},
					new Trigger(TriggerType.TURN_END) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = RemoveEnchantmentTask.Task })
			}));
			cards.Add("RLK_659e2", new CardDef(new Power { Enchant = new Enchant(new Effect(HsTag.CARD_ALTERNATE_COST, EffectOperator.SET, Playable.ArmorAlternateCost)) }));
			// [RLK_956] Nerubian Flyer (2) — Battlecry: If a friendly Undead died after your last turn, summon a 2/2 Nerubian.
			cards.Add("RLK_956", new CardDef(new Power { PowerTask = IfUndeadDied(new SummonTask("RLK_956t", SummonSide.RIGHT)) }));

			// ---------------------------------------------------------------- Hunter
			// [NX2_013] ZOMBEEEES!!! (2) — Secret: After your opponent plays a minion, summon four 1/1 Zombees to attack it.
			// (Improved: four 2/2 Zombees.)
			foreach (var (id, bee) in new[] { ("NX2_013", "NX2_013t"), ("NX2_013t2", "NX2_013t3") })
			{
				string token = bee;
				cards.Add(id, new CardDef(new Power
				{
					Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
					{
						TriggerSource = TriggerSource.ENEMY,
						SingleTask = ComplexTask.Secret(new CustomTask((g, c, s, t, stack) =>
						{
							Minion played = g.CurrentEventData?.EventSource as Minion ?? t as Minion;
							for (int i = 0; i < V("NX2_013", "zombees", 4) && !c.BoardZone.IsFull; i++)
							{
								var zombee = (Minion)Entity.FromCard(c, Cards.FromId(token));
								Generic.SummonBlock.Invoke(g, zombee, -1, s);
								if (played != null && !played.IsDead && played.Zone?.Type == Zone.PLAY && !zombee.IsDead)
									EffectAttack(g, zombee, played);
							}
						}))
					}
				}));
			}
			// [NX2_014] Trinket Tracker (1) — Battlecry: Draw a 1-Cost spell.
			cards.Add("NX2_014", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p is Spell && p.Cost == V("NX2_014", "cost", 1))) }));
			// [NX2_015] Faithful Companions (7) — Discover a Beast from your deck and summon it. Manathirst (@): Also summon a copy of it.
			cards.Add("NX2_015", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					bool thirsty = Generic.Manathirst((IPlayable)s);
					Generic.DiscoverFromDeck(c, s, Generic.PickFromDeck(c, p => p is Minion && IsRaceCard(p, Race.BEAST)), (g2, c2, s2, beast) =>
					{
						if (beast?.Zone?.Type == Zone.DECK && beast is Minion m && !c2.BoardZone.IsFull)
						{
							c2.DeckZone.Remove(m);
							Generic.SummonBlock.Invoke(g2, m, -1, s2);
						}
						if (thirsty && beast != null) Summon(g2, c2, beast.Card, s2);
					});
				})
			}));
			// [RLK_804] Conjured Arrow (2) — Deal 2 damage to a minion. Manathirst (6): Draw that many cards.
			cards.Add("RLK_804", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					int dealt = Generic.DamageCharFunc((IPlayable)s, m, V("RLK_804", "damage", 2), true);
					if (Generic.Manathirst((IPlayable)s)) for (int i = 0; i < dealt; i++) Generic.Draw(c);
				})
			}));
			// [RLK_817] Arcane Quiver (2) — Discover a spell from your deck. If it's Arcane, give it Spell Damage +1.
			cards.Add("RLK_817", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					Generic.DiscoverFromDeck(c, s, Generic.PickFromDeck(c, p => p is Spell), (g2, c2, s2, spell) =>
					{
						Generic.Draw(c2, spell);
						if (spell?.Zone?.Type == Zone.HAND && spell[SPELL_SCHOOL] == ARCANE) Give(g2, s2, spell, "RLK_826e");
					}))
			}));
			cards.Add("RLK_826e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.SPELLPOWER, EffectOperator.ADD, V("RLK_826", "spell damage", 1))) }));
			// [RLK_818] Ricochet Shot (1) — Deal 1 damage to three random enemies.
			cards.Add("RLK_818", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (ICharacter enemy in PickCharacters(g, Enemies(c).Where(x => !x.IsDead), V("RLK_818", "enemies", 3)))
					{
						g.PowerHistory?.NewTargetRound();
						Generic.DamageCharFunc((IPlayable)s, enemy, V("RLK_818", "damage", 1), true);
					}
				})
			}));
			// [RLK_819] Eversong Portal (4) — Summon 1 4/4 Lynx with Rush (improved by Spell Damage).
			cards.Add("RLK_819", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = V("RLK_819", "lynxes", 1) + c.CurrentSpellPower + ((IPlayable)s)[GameTag.SPELLPOWER];
					for (int i = 0; i < n; i++) Summon(g, c, Cards.FromId("RLK_819t"), s);
				})
			}));
			// [RLK_820] Halduron Brightwing (3) — Battlecry: Give all Arcane spells in your hand and deck Spell Damage +1.
			cards.Add("RLK_820", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.HandZone.Concat(c.DeckZone).Where(p => p is Spell && p[SPELL_SCHOOL] == ARCANE).ToList(), "RLK_826e"))
			}));
			// [RLK_821] Scourge Tamer (1) — Battlecry: Craft a custom Zombeast. (Deathstalker Rexxar's Build-A-Beast.)
			cards.Add("RLK_821", new CardDef(new Power { PowerTask = SpecificTask.BuildABeast }));
			// [RLK_825] Shockspitter (4) — Battlecry: Deal @ damage. (Improved by your hero attacks this game!) The @ is its
			// TAG_SCRIPT_DATA_NUM_1 (1) plus the player's hero attacks this game (HsTag.HERO_ATTACKS_THIS_GAME), kept in hand.
			cards.Add("RLK_825", new CardDef(BattlecryAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter target)
						Generic.DamageCharFunc((IPlayable)s, target, ((IPlayable)s).Card[GameTag.TAG_SCRIPT_DATA_NUM_1] + c[HsTag.HERO_ATTACKS_THIS_GAME], false);
				}),
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerActivation = TriggerActivation.HAND,
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is IPlayable me) me[GameTag.TAG_SCRIPT_DATA_NUM_1] = me.Card[GameTag.TAG_SCRIPT_DATA_NUM_1] + c[HsTag.HERO_ATTACKS_THIS_GAME];
					})
				}
			}));
			// [RLK_826] Silvermoon Farstrider (2) — Battlecry: Give all Arcane spells in your hand Spell Damage +1.
			cards.Add("RLK_826", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.HandZone.Where(p => p is Spell && p[SPELL_SCHOOL] == ARCANE).ToList(), "RLK_826e"))
			}));
			// [RLK_827] Keeneye Spotter (3) — Whenever your hero attacks a minion, set its Health to 1.
			cards.Add("RLK_827", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventTarget is Minion defender && !defender.IsDead) Give(g, s, defender, "RLK_827e");
					})
				}
			}));
			cards.Add("RLK_827e", new CardDef(new Power { Enchant = new Enchant(Effects.SetMaxHealth(V("RLK_827", "health", 1))) }));
			// [RLK_828] Hope of Quel'Thalas (6) — After your hero attacks, give your minions +1/+1 (wherever they are).
			cards.Add("RLK_828", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
						GiveAll(g, s, c.BoardZone.Where(m => !m.IsDead).Cast<IPlayable>().Concat(c.HandZone.Where(p => Generic.IsMinionCard(p))).Concat(c.DeckZone.Where(p => Generic.IsMinionCard(p))).ToList(), "RLK_828e"))
				}
			}));
			cards.Add("RLK_828e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("RLK_828", "stats", 1))) }));

			// ---------------------------------------------------------------- Mage
			// [NX2_001] Tear Reality (4) — Add 2 random Mage spells from the past to your hand. They cost (2) less.
			cards.Add("NX2_001", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("NX2_001", "spells", 2); i++)
					{
						if (c.HandZone.IsFull || !(RandomCard(g, x => x.Type == CardType.SPELL && x.HasClass(CardClass.MAGE) && Cards.FromThePast(x)) is Card spell)) break;
						IPlayable p = Entity.FromCard(c, spell);
						if (Generic.AddHandPhase.Invoke(c, p)) Give(g, s, p, "NX2_001e");
					}
				})
			}));
			cards.Add("NX2_001e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("NX2_001", "less", 2))) }));
			// [NX2_002] Spectral Trainee (3) — After you cast a spell, deal 1 damage to all enemy minions.
			cards.Add("NX2_002", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new DamageTask(V("NX2_002", "damage", 1), EntityType.OP_MINIONS)
				}
			}));
			// [NX2_003] Whirlweaver (3) — Battlecry: If you've cast a spell last turn, Discover an Elemental.
			cards.Add("NX2_003", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new ConditionTask(EntityType.SOURCE, SelfCondition.CastSpellLastTurn),
					new FlagTask(true, DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.IsRace(Race.ELEMENTAL))))
			}));
			// [RLK_541] Vexallus (5) — Your Arcane spells cast twice. (CastSpell: HsTag.ARCANE_SPELLS_CAST_TWICE)
			cards.Add("RLK_541", new CardDef(new Power { Aura = new Aura(AuraType.CONTROLLER, new Effect(HsTag.ARCANE_SPELLS_CAST_TWICE, EffectOperator.ADD, 1)) }));
			// [RLK_542] Arcsplitter (3) — Deathrattle: Add 2 Arcane Bolts to your hand.
			cards.Add("RLK_542", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("RLK_542", "bolts", 2); i++) AddToHand(c, Cards.FromId("RLK_843")); })
			}));
			// [RLK_543] Magister's Apprentice (2) — Your Arcane spells cost (1) less.
			cards.Add("RLK_543", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, "RLK_543e") { Condition = new SelfCondition(p => p is Spell && p[SPELL_SCHOOL] == ARCANE) }
			}));
			cards.Add("RLK_543e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("RLK_543", "less", 1))) }));
			// [RLK_544] Arcane Defenders (8) — Summon two 5/6 Golems with Taunt and Elusive.
			cards.Add("RLK_544", new CardDef(new Power { PowerTask = new SummonTask("RLK_544t", V("RLK_544", "golems", 2), SummonSide.SPELL) }));
			// [RLK_545] Energy Shaper (3) — Battlecry: Transform all spells in your hand into ones that cost (3) more. (They keep their original Cost.)
			cards.Add("RLK_545", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable spell in c.HandZone.Where(p => p is Spell).ToList())
					{
						int cost = spell.Cost;
						var pool = Cards.GenerationPool(g, c).Where(x => x.Type == CardType.SPELL && x.Collectible && x.Cost == spell.Card.Cost + V("RLK_545", "more", 3) &&
						                                                (x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL))).ToList();
						if (pool.Count == 0) continue;
						IPlayable now = Generic.ChangeEntityBlock.Invoke(c, spell, pool[g.Random.Next(pool.Count)], true);
						Generic.AddEnchantmentBlock(g, Cards.FromId("RLK_546e"), (IPlayable)s, now, cost, 0, 0);
					}
				})
			}));
			// [RLK_546] Vast Wisdom (2) — Discover two spells that cost (3) or less. Swap their Costs.
			cards.Add("RLK_546", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL && x.Cost <= V("RLK_546", "cost", 3)),
					DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL && x.Cost <= V("RLK_546", "cost", 3),
						afterPick: new CustomTask((g, c, s, t, stack) =>
						{
							// the two picks: the last two cards this spell put in the hand
							var picks = c.HandZone.Where(p => p[GameTag.CREATOR] == s.Id || p[GameTag.DISPLAYED_CREATOR] == s.Id).TakeLast(2).ToList();
							if (picks.Count < 2) return;
							int a = picks[0].Cost, b = picks[1].Cost;
							Generic.AddEnchantmentBlock(g, Cards.FromId("RLK_546e"), (IPlayable)s, picks[0], b, 0, 0);
							Generic.AddEnchantmentBlock(g, Cards.FromId("RLK_546e"), (IPlayable)s, picks[1], a, 0, 0);
						})))
			}));
			cards.Add("RLK_546e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(0)) { UseScriptTag = true } }));
			// [RLK_547] Prismatic Elemental (2) — Battlecry: Discover a spell from any class. It costs (1) less.
			cards.Add("RLK_547", new CardDef(new Power { PowerTask = DiscoverFromTask.Where((c, x) => x.Type == CardType.SPELL && x.Collectible, "RLK_547e") }));
			cards.Add("RLK_547e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("RLK_547", "less", 1))) }));
			// [RLK_548] Arcane Wyrm (1) — Battlecry: Add an Arcane Bolt to your hand.
			cards.Add("RLK_548", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => AddToHand(c, Cards.FromId("RLK_843"))) }));
			// [RLK_803] Grand Magister Rommath (9) — Battlecry: Recast each spell you've cast this game that didn't start in your deck. (Once per game)
			cards.Add("RLK_803", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c[HsTag.ROMMATH_USED] > 0) return;
					c[HsTag.ROMMATH_USED] = 1;
					var spells = c.PlayHistory.Where(h => h.SourceCard?.Type == CardType.SPELL && h.SourceId >= g.FirstGeneratedEntityId)
						.Select(h => h.SourceCard).ToList();
					foreach (Card spell in spells)
					{
						if (c.Hero.IsDead || c.Opponent.Hero.IsDead) break;
						CastQuietly(g, c, s, spell);
					}
				})
			}));
			// [RLK_843] Arcane Bolt (1) — Deal 2 damage. Manathirst (8): Deal 3 damage instead.
			cards.Add("RLK_843", new CardDef(TargetAny, new Power
			{
				PowerTask = ManathirstOr(new DamageTask(V("RLK_843", "damage", 2), EntityType.TARGET, true), new DamageTask(V("RLK_843", "manathirst damage", 3), EntityType.TARGET, true))
			}));
		}

		/// <summary>Up to <paramref name="n"/> different characters at random.</summary>
		private static List<ICharacter> PickCharacters(Game g, IEnumerable<ICharacter> from, int n)
		{
			var list = from.ToList();
			var picks = new List<ICharacter>();
			while (picks.Count < n && list.Count > 0)
			{
				ICharacter ch = list[g.Random.Next(list.Count)];
				list.Remove(ch);
				picks.Add(ch);
			}
			return picks;
		}
	}
}
