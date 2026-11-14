// HsServer addition (not upstream SabberStoneCore): The Great Dark Beyond — chunk 3: Paladin, Priest and Rogue (GDB_ and the
// StarCraft mini-set SC_; spec: research spec_chunk3). Shared pieces: SpaceCardsGenHs.cs, Actions/Starship.cs (Starships, the
// after-launch upgrade, the Templar merge). Every number goes through CardValues (V).
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
		/// <summary>Ultra-Capacitor's Battlecry ("Gain +1/+1 for each other friendly minion"), for the minion or, at the launch, the
		/// Starship: one SC_405e of +N/+N (live 87191, 101587).</summary>
		private static void UltraCapacitor(Game g, Minion me)
		{
			int n = me.Controller.BoardZone.GetAll().Count(m => m != me && !m.Untouchable && !(m is Location) && !m.IsDead);
			if (n > 0) Generic.AddEnchantmentBlock(g, Cards.FromId("SC_405e"), me, me, n * V("SC_405", "stats", 1), n * V("SC_405", "stats", 1), 0);
		}

		/// <summary>Celestial Aura's minion: the player's only minion (Locations and Permanent minions — Dormant, a Starship on its
		/// launchpad — don't count), else null.</summary>
		private static Minion CelestialTarget(IPlayable aura)
		{
			var mine = aura.Controller.BoardZone.GetAll().Where(m => !m.Untouchable && !(m is Location) && !m.IsDead && !m.ToBeDestroyed).ToList();
			return mine.Count == 1 ? mine[0] : null;
		}

		private static List<Enchantment> CelestialMade(IPlayable aura) =>
			aura.Controller.BoardZone.GetAll().SelectMany(m => m.AppliedEnchantments ?? new List<Enchantment>()).Where(e => e.Creator == aura).ToList();

		/// <summary>Blink's draw: a Protoss minion; with Combo it costs (2) less.</summary>
		private static CustomTask Blink(bool combo) => new CustomTask((g, c, s, t, stack) =>
		{
			IPlayable drawn = DrawOne(g, c, p => p is Minion && IsProtoss(p));
			if (combo && drawn != null) Give(g, s, drawn, "GBL_002e");
		});

		private static void SpaceChunk3(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Paladin
			// [GDB_137] Libram of Clarity (3) — Draw 2 minions. If this costs (0), give them +2/+1.
			cards.Add("GDB_137", new CardDef(new Power
			{
				GlowAtZeroCost = true,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					bool zero = PlayedForZero(s);
					for (int i = 0; i < V("GDB_137", "draw", 2); i++)
						if (DrawOne(g, c, p => p is Minion) is IPlayable drawn && zero) Give(g, s, drawn, "GDB_137e1");
				})
			}));
			cards.Add("GDB_137e1", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("GDB_137", "attack", 2)), Effects.Health_N(V("GDB_137", "health", 1))) }));
			// [GDB_138] Libram of Divinity (4) — Give a minion +3/+3. If this costs (0), return this to your hand at the end of your turn.
			// (GDB_138e2 "At the end of your turn, get a Libram of Divinity": a new card)
			cards.Add("GDB_138", new CardDef(TargetMinion, new Power
			{
				GlowAtZeroCost = true,
				PowerTask = ComplexTask.Create(
					new AddEnchantmentTask("GDB_138e", EntityType.TARGET),
					ComplexTask.If((g, c, s) => PlayedForZero(s), new AddEnchantmentTask("GDB_138e2", EntityType.CONTROLLER)))
			}));
			cards.Add("GDB_138e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("GDB_138", "stats", 3))) }));
			cards.Add("GDB_138e2", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = ComplexTask.Create(new CustomTask((g, c, s, t, stack) => AddToHand(c, Cards.FromId("GDB_138"))), RemoveEnchantmentTask.Task)
				}
			}));
			// [GDB_139] Libram of Faith (6) — Summon three 3/3 Draenei with Divine Shield. If this costs (0), give them Rush.
			cards.Add("GDB_139", new CardDef(new Power
			{
				GlowAtZeroCost = true,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					bool zero = PlayedForZero(s);
					for (int i = 0; i < V("GDB_139", "count", 3) && !c.BoardZone.IsFull; i++)
					{
						var draenei = (Minion)Entity.FromCard(c, Cards.FromId("GDB_139t"));
						Generic.SummonBlock.Invoke(g, draenei, -1, s);
						if (zero && draenei.Zone?.Type == Zone.PLAY) draenei.IsRush = true;
					}
				})
			}));
			// [GDB_140] Celestial Aura (6) — While you have exactly 1 minion in play, its Attack and Health are 10. Lasts 3 turns.
			// (wiki notes: set once, not refreshed; damage kept — 4/2 → 10/8; reset when the board goes 2+ → 1. A Paladin Aura whose
			// state check moves GDB_140e to the only minion and takes it off when there are more — Generic.GiveStatSet / EndStatSet;
			// at its end its enchantments go)
			cards.Add("GDB_140", new CardDef(PaladinAura(new Power
			{
				StateCheck = p =>
				{
					Minion only = CelestialTarget(p);
					var made = CelestialMade(p);
					return made.Any(e => e.Target != only) || only != null && !made.Any(e => e.Target == only);
				},
				StateTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is IPlayable aura)) return;
					Minion only = CelestialTarget(aura);
					var made = CelestialMade(aura);
					foreach (Enchantment e in made.Where(e => e.Target != only)) Generic.EndStatSet(e);
					if (only != null && !made.Any(e => e.Target == only)) Generic.GiveStatSet(g, aura, only, "GDB_140e");
				})
			})));
			cards.Add("GDB_140e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.SetAttack(V("GDB_140", "stats", 10)), Effects.SetMaxHealth(V("GDB_140", "stats", 10))) { KeepsDamage = true }
			}));
			// [GDB_141] Yrel, Beacon of Hope (3) — Rush. Deathrattle: Get three different Librams from an older timeline!
			// (wiki: "always generate exactly Libram of Wisdom, Libram of Justice, and Libram of Hope" — its RelatedCards, in order)
			cards.Add("GDB_141", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (string id in ReprintCards.RelatedCardIds(((IPlayable)s).Card.Id) ?? new List<string>())
						if (Cards.TryFromId(id) is Card libram && libram[HsTag.LIBRAM] > 0) AddToHand(c, libram);
				})
			}));
			// [GDB_144] Lumia (6) — Lifesteal. After a hero takes damage, they become Immune for the rest of the turn.
			cards.Add("GDB_144", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.ALL,
					Condition = new SelfCondition(p => p is Hero h && !h.IsImmune),
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (t is Hero h && !h.IsImmune) Give(g, s, h, "GDB_144e"); })
				}
			}));
			cards.Add("GDB_144e", OneTurn(Effects.Immune));
			// [GDB_462] Orbital Satellite (1) — Discover a Draenei. If you played an adjacent card this turn, Discover another.
			cards.Add("GDB_462", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.IsRace(DRAENEI)),
					ComplexTask.If((g, c, s) => s[HsTag.ADJACENT_PLAYED_TURN] == g.Turn,
						DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.IsRace(DRAENEI))))
			}));
			// [GDB_721] Interstellar Wayfarer (4) — Divine Shield. Battlecry and Deathrattle: Reduce the Cost of your Librams by (1)
			// this game. / [GDB_726] Interstellar Starslicer (3, weapon) — the same; both use GDB_726e.
			cards.Add("GDB_721", new CardDef(new Power
			{
				PowerTask = new AddEnchantmentTask("GDB_726e", EntityType.CONTROLLER),
				DeathrattleTask = new AddEnchantmentTask("GDB_726e", EntityType.CONTROLLER)
			}));
			cards.Add("GDB_726", new CardDef(new Power
			{
				PowerTask = new AddEnchantmentTask("GDB_726e", EntityType.CONTROLLER),
				DeathrattleTask = new AddEnchantmentTask("GDB_726e", EntityType.CONTROLLER)
			}));
			cards.Add("GDB_726e", LibramDiscount("GDB_726", 1));
			// [GDB_728] Interstellar Researcher (2) — Battlecry and Spellburst: Draw a Libram.
			var drawLibram = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, IsLibram));
			cards.Add("GDB_728", new CardDef(new Power { PowerTask = drawLibram, Trigger = Spellburst(drawLibram) }));
			// [SC_404] Salvage the Bunker (3) — Summon two 2/2 Marines with Taunt. Your next Starship launch costs (2) less.
			cards.Add("SC_404", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("SC_404", "marines", 2); i++) Summon(g, c, Cards.FromId("SC_403t"), s);
					Generic.DiscountStarshipLaunch(c, V("SC_404", "discount", 2));
				})
			}));
			// [SC_405] Ultra-Capacitor (2) — Starship Piece. Battlecry: Gain +1/+1 for each other friendly minion. Also triggers on launch.
			cards.Add("SC_405", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me) UltraCapacitor(g, me); }) }));
			cards.Add("SC_405e", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackHealthScriptTag }));
			cards.Add("SC_405te", new CardDef(new Power { LaunchTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion ship) UltraCapacitor(g, ship); }) }));
			// [SC_412] Hellion (4) — Your other minions have +1 Attack. (Transforms if you launched a Starship this game.)
			cards.Add("SC_412", new CardDef(new Power { Aura = new Aura(AuraType.BOARD_EXCEPT_SOURCE, "SC_412e1"), UpgradeAfterLaunch = "SC_412t" }));
			cards.Add("SC_412e1", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("SC_412", "attack", 1))) }));
			// [SC_412t] Hellbat (4) — Rush. Your other minions have +2 Attack and Rush.
			cards.Add("SC_412t", new CardDef(new Power { Aura = new Aura(AuraType.BOARD_EXCEPT_SOURCE, "SC_412e2") }));
			cards.Add("SC_412e2", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("SC_412t", "attack", 2)), Effects.Rush) }));

			// ---------------------------------------------------------------- Priest
			// [GDB_439] Orbital Halo (2) — Give a minion +1/+1 and Divine Shield. Costs (0) if you played an adjacent card this turn.
			cards.Add("GDB_439", new CardDef(TargetMinion, new Power
			{
				Aura = new AdaptiveCostEffect(p => 0, EffectOperator.SET, new SelfCondition(p => p[HsTag.ADJACENT_PLAYED_TURN] == p.Game.Turn)),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Give(g, s, m, "GDB_439e");
					m.HasDivineShield = true;
				})
			}));
			cards.Add("GDB_439e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("GDB_439", "stats", 1))) }));
			// [GDB_440] Mystified To'cha (4) — Battlecry: If the combined Health of both heroes is exactly 42, set your hero's Health
			// to 42. (Health only, not Armor; a Health-setting buff sets current Health too — Health.Apply)
			cards.Add("GDB_440", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.Hero.Health + c.Opponent.Hero.Health == V("GDB_440", "total", 42),
					new CustomTask((g, c, s, t, stack) => Give(g, s, c.Hero, "GDB_440e", V("GDB_440", "health", 42))))
			}));
			cards.Add("GDB_440e", new CardDef(new Power { Enchant = Enchants.Enchants.SetHealthScriptTag }));
			// [GDB_441] Anchorite (3) — Whenever another minion is Overhealed, give it that much extra Health.
			cards.Add("GDB_441", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.OVERHEAL)
				{
					TriggerSource = TriggerSource.ALL_MINIONS_EXCEPT_SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						int excess = g.CurrentEventData?.EventNumber ?? 0;
						if (t is Minion m && excess > 0 && m.Zone?.Type == Zone.PLAY) Give(g, s, m, "GDB_441e", excess);
					})
				}
			}));
			cards.Add("GDB_441e", new CardDef(new Power { Enchant = Enchants.Enchants.AddHealthScriptTag }));
			// [GDB_442] K'ure, the Light Beyond (3) — Spellburst: Summon a random 3-Cost minion. (Holy spells don't remove this Spellburst.)
			cards.Add("GDB_442", new CardDef(new Power
			{
				Trigger = SpellburstKeptBy(HOLY, new CustomTask((g, c, s, t, stack) => Summon(g, c, RandomMinionOfCost(g, V("GDB_442", "cost", 3)), s)))
			}));
			// [GDB_454] Overzealous Healer (1) — Deathrattle: Restore 6 Health to the enemy hero. Spellburst: Silence this minion.
			cards.Add("GDB_454", new CardDef(new Power
			{
				DeathrattleTask = new HealTask(V("GDB_454", "heal", 6), EntityType.OP_HERO),
				Trigger = Spellburst(new SilenceTask(EntityType.SOURCE))
			}));
			// [GDB_455] Askara (4) — Battlecry: The next two Draenei you play summon a copy of themselves.
			cards.Add("GDB_455", new CardDef(new Power { PowerTask = new AddEnchantmentTask("GDB_455e", EntityType.CONTROLLER) }));
			cards.Add("GDB_455e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p is Minion m && m.IsRace(DRAENEI)),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e) || !((t ?? g.CurrentEventData?.EventSource as IPlayable) is Minion played)) return;
						if (played.Zone?.Type == Zone.PLAY && !c.BoardZone.IsFull)
							Generic.NamedFx(e, "OverrideSpawn", null, () =>
								Generic.Copy(c, e, played, Zone.PLAY, position: played.ZonePosition + 1));
						int left = (e[GameTag.TAG_SCRIPT_DATA_NUM_2] > 0 ? e[GameTag.TAG_SCRIPT_DATA_NUM_2] : V("GDB_455", "count", 2)) - 1;
						e[GameTag.TAG_SCRIPT_DATA_NUM_2] = left;
						if (left <= 0) e.Remove();
					})
				}
			}));
			// [GDB_457] Lightspeed (2) — Echo. Give a minion +1/+2 and Rush.
			cards.Add("GDB_457", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Give(g, s, m, "GDB_457e1");
					m.IsRush = true;
				})
			}));
			cards.Add("GDB_457e1", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("GDB_457", "attack", 1)), Effects.Health_N(V("GDB_457", "health", 2))) }));
			// [GDB_460] Divine Star (2) — Deal 3 damage to a minion. Give a random minion in your hand +3 Health.
			cards.Add("GDB_460", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("GDB_460", "damage", 3), EntityType.TARGET, true), BuffRandomInHand(p => p is Minion, "GDB_460e2"))
			}));
			cards.Add("GDB_460e2", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("GDB_460", "health", 3))) }));
			// [GDB_464] Gravity Lapse (0) — Set EVERY minion's Attack and Health to the lower of the two. (Current Health; the Health
			// set leaves it undamaged — Health.Apply)
			cards.Add("GDB_464", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).ToList())
					{
						if (m.Untouchable || m is Location || m.IsDead || m.ToBeDestroyed) continue;
						int attack = m.AttackDamage, health = m.Health;
						if (attack > health) Give(g, s, m, "GDB_464e", health);
						else if (health > attack) Give(g, s, m, "GDB_464e2", attack);
					}
				})
			}));
			cards.Add("GDB_464e", new CardDef(new Power { Enchant = Enchants.Enchants.SetAttackScriptTag }));
			cards.Add("GDB_464e2", new CardDef(new Power { Enchant = Enchants.Enchants.SetHealthScriptTag }));
			// [SC_757] Hallucination (1) — Summon a copy of a friendly Protoss minion. It takes double damage.
			cards.Add("SC_757", new CardDef(new Dictionary<PlayReq, int>
			{
				{ PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_NUM_MINION_SLOTS, 1 }
			}, new Power
			{
				TargetIf = (PlayReq.REQ_TARGET_WITH_RACE, (src, target) => IsProtoss(target)),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m) || c.BoardZone.IsFull) return;
					if (Generic.Copy(c, s, m, Zone.PLAY, position: m.ZonePosition + 1) is Minion copy && copy.Zone?.Type == Zone.PLAY)
						Give(g, s, copy, "SC_757e");
				})
			}));
			cards.Add("SC_757e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PREDAMAGE)
				{
					TriggerSource = TriggerSource.ENCHANTMENT_TARGET,
					FastExecution = true,
					SingleTask = Generic.PreDamageMultiply(V("SC_757", "factor", 2))
				}
			}));
			// [SC_762] Mothership (12) — Taunt. Battlecry and Deathrattle: Get two random Protoss minions.
			var twoProtoss = new CustomTask((g, c, s, t, stack) =>
			{
				for (int i = 0; i < V("SC_762", "count", 2); i++)
					if (RandomCard(g, x => x.Type == CardType.MINION && IsProtossCard(x)) is Card protoss) AddToHand(c, protoss);
			});
			cards.Add("SC_762", new CardDef(new Power { PowerTask = twoProtoss, DeathrattleTask = twoProtoss }));
			// [SC_764] Sentry (2) — Lifesteal. Deathrattle: Your Protoss minions cost (1) less this game. (SC_755e: the client's
			// only enchantment with this text)
			cards.Add("SC_764", new CardDef(new Power { DeathrattleTask = new AddEnchantmentTask("SC_755e", EntityType.CONTROLLER) }));

			// ---------------------------------------------------------------- Rogue
			// [GDB_102] Starship Schematic (1) — Discover a Starship Piece from another class. It costs (1) less. (live 85604: GBL_003e)
			cards.Add("GDB_102", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.Where((c, x) => x.Type == CardType.MINION && x[Generic.STARSHIP_PIECE] > 0 &&
					!x.HasClass(c.HeroClass) && !x.HasClass(CardClass.NEUTRAL), "GBL_003e")
			}));
			// [GDB_465] Barrel Roll (3) — Deal 5 damage to an undamaged character. Costs (1) if you're building a Starship.
			cards.Add("GDB_465", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_UNDAMAGED_TARGET, 0 } }, new Power
			{
				Aura = new AdaptiveCostEffect(p => V("GDB_465", "cost", 1), EffectOperator.SET, new SelfCondition(p => Generic.IsBuildingStarship(p.Controller))),
				PowerTask = new DamageTask(V("GDB_465", "damage", 5), EntityType.TARGET, true)
			}));
			// [GDB_466] The Gravitational Displacer (5) — Starship Piece. When this is launched, summon a copy of the Starship.
			// (the copy is summoned, not launched: its launch effects don't fire again — doubt)
			cards.Add("GDB_466", new CardDef(new Power { NoScript = true }));
			cards.Add("GDB_466e", new CardDef(new Power
			{
				LaunchTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion ship) || ship.Zone?.Type != Zone.PLAY || c.BoardZone.IsFull) return;
					string fx = SubSpells.FindNamed("GDB_466", "OverrideSpawn");
					bool open = fx != null && g.OpenSubSpell(ship, fx, null);
					Generic.Copy(c, ship, ship, Zone.PLAY, position: ship.ZonePosition + 1);
					if (open) g.CloseSubSpells(ship.Id);
				})
			}));
			// [GDB_467] Quasar (8) — Shuffle your hand into your deck. Reduce the Cost of cards in your deck by (3).
			cards.Add("GDB_467", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var hand = c.HandZone.GetAll().ToList();
					Generic.NamedFx((IPlayable)s, "HandToDeck", null, () => { foreach (IPlayable p in hand) Generic.MoveToDeck(c, s, p); });
					GiveAll(g, s, c.DeckZone.ToList(), "GBL_004e");
				})
			}));
			// [GDB_472] Talgath (3) — Undamaged enemy minions take double damage. Combo: Get a Backstab.
			// (rule book DH8: damage-multiplying effects multiply the proposed damage) Live (install 204752 112932 / 113020): while Talgath is
			// in play its aura shows GDB_472e on its owner's player; the doubling is GDB_472e's pre-damage TRIGGER (index 1, no keyword) with
			// META CRITICAL_HIT on the damaged minion; it leaves with Talgath.
			cards.Add("GDB_472", new CardDef(new Power
			{
				Aura = new Aura(AuraType.CONTROLLER) { ShowAs = "GDB_472e" },
				ComboTask = new CustomTask((g, c, s, t, stack) => AddToHand(c, Cards.FromId("CORE_CS2_072")))
			}));
			cards.Add("GDB_472e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PREDAMAGE)
				{
					TriggerSource = TriggerSource.OP_MINIONS,
					FastExecution = true,
					EffectIndex = 1,
					KeywordOverride = (GameTag)0,
					Condition = new SelfCondition(p => p is Minion m && m.Damage == 0),
					SingleTask = Generic.PreDamageMultiply(V("GDB_472", "factor", 2), critical: true)
				}
			}));
			// [GDB_870] Eredar Skulker (2) — Combo and Spellburst: Gain +2 Attack and Stealth.
			var skulk = new CustomTask((g, c, s, t, stack) =>
			{
				if (!(s is Minion me) || me.Zone?.Type != Zone.PLAY) return;
				Give(g, s, me, "GDB_870e2");
				me.HasStealth = true;
			});
			cards.Add("GDB_870", new CardDef(new Power { ComboTask = skulk, Trigger = Spellburst(skulk) }));
			cards.Add("GDB_870e2", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("GDB_870", "attack", 2))) }));
			// [GDB_873] Lucky Comet (2) — Discover a Combo minion. The next one you play triggers its Combo twice.
			// (the charge: player HsTag.NEXT_COMBO_TWICE, spent in PlayCard by the next Combo minion played; GDB_873e shows it)
			cards.Add("GDB_873", new CardDef(new Power
			{
				// (the charge doesn't depend on the Discover: given first, so an empty Discover can't stop it)
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) => c[HsTag.NEXT_COMBO_TWICE] = c[HsTag.NEXT_COMBO_TWICE] + 1),
					new AddEnchantmentTask("GDB_873e", EntityType.CONTROLLER),
					DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x[GameTag.COMBO] > 0))
			}));
			cards.Add("GDB_873e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p.Card[GameTag.COMBO] > 0),
					// one enchantment per charge spent (the charges are taken in the Battlecry phase, after this)
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e)) return;
						int shown = c.AppliedEnchantments?.Count(x => x.Card == e.Card) ?? 0;
						if (shown > c[HsTag.NEXT_COMBO_TWICE] - 1) e.Remove();
					})
				}
			}));
			// [GDB_875] Spacerock Collector (1) — Battlecry: Your next Combo card costs (1) less.
			cards.Add("GDB_875", new CardDef(new Power { PowerTask = new AddEnchantmentTask("GDB_875e", EntityType.CONTROLLER) }));
			cards.Add("GDB_875e", NextCardCost(Effects.ReduceCost(V("GDB_875", "discount", 1)), new SelfCondition(p => p.Card[GameTag.COMBO] > 0), "GBL_003e"));
			// [GDB_876] Scrounging Shipwright (2) — Battlecry: Get a random Starship Piece from another class.
			cards.Add("GDB_876", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (Pick(g, OtherClassCards(g, c).Where(x => x.Type == CardType.MINION && x[Generic.STARSHIP_PIECE] > 0), 1).FirstOrDefault() is Card piece)
						AddToHand(c, piece);
				})
			}));
			// [GDB_881] Pressure Points (3) — Deal 3 damage to a minion. Reduce the Cost of Combo cards in your hand by (1).
			cards.Add("GDB_881", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("GDB_881", "damage", 3), EntityType.TARGET, true),
					new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.HandZone.GetAll().Where(p => p.Card[GameTag.COMBO] > 0).ToList(), "GBL_003e")))
			}));
			// [SC_752] Dark Templar (6) — Stealth. Battlecry: Destroy an enemy minion. Play another Templar to merge into an Archon!
			cards.Add("SC_752", new CardDef(BattlecryEnemyMinion, new Power
			{
				PowerTask = new DestroyTask(EntityType.TARGET),
				Trigger = Generic.MergeTrigger(),
				MergesInto = "SC_671t1"
			}));
			// [SC_765] High Templar (6) — Battlecry: Deal 2 damage to all enemies. Play another Templar to merge into an Archon!
			cards.Add("SC_765", new CardDef(new Power
			{
				PowerTask = new DamageTask(V("SC_765", "damage", 2), EntityType.ENEMIES),
				Trigger = Generic.MergeTrigger(),
				MergesInto = "SC_671t1"
			}));
			// [SC_671t1] Archon (8) — At the end of your turn, deal 8 damage to the enemy hero and 2 damage to their minions.
			cards.Add("SC_671t1", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = ComplexTask.Create(
						new DamageTask(V("SC_671t1", "hero", 8), EntityType.OP_HERO),
						new DamageTask(V("SC_671t1", "minions", 2), EntityType.OP_MINIONS))
				}
			}));
			// [SC_761] Blink (2) — Draw a Protoss minion. Combo: It costs (2) less.
			cards.Add("SC_761", new CardDef(new Power { PowerTask = Blink(false), ComboTask = Blink(true) }));
		}
	}
}
