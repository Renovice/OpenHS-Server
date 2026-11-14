// HsServer addition (not upstream SabberStone): The Great Dark Beyond — chunk 1: the Starship pieces and cards, the Neutral
// Spellburst and Draenei cards, Demon Hunter, Death Knight and the StarCraft faction cards (spec: research spec_chunk1; shared
// pieces: SpaceCardsGenHs.cs, Actions/Starship.cs). Every number goes through CardValues (V).
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
		/// <summary>A Starship Piece's own Spellburst / trigger: effect index 1 (index 0 is its "add to the Starship" block — live
		/// Guiding Figure, Felfused Battery).</summary>
		private static Trigger PieceTrigger(Trigger trigger)
		{
			trigger.EffectIndex = 1;
			return trigger;
		}

		/// <summary>A Starship's Spellburst from a piece (Guiding Figure's GDB_106e): once, after a spell its owner casts, with the
		/// launched Starship as "this".</summary>
		private static Trigger StarshipSpellburst(Action<Game, Controller, Minion> effect) => new Trigger(TriggerType.AFTER_CAST)
		{
			TriggerSource = TriggerSource.FRIENDLY,
			RemoveAfterTriggered = true,
			EffectIndex = 1,
			SingleTask = new CustomTask((g, c, s, t, stack) =>
			{
				if (!(Generic.StarshipOf(s) is Minion ship) || ship.Zone?.Type != Zone.PLAY) return;
				effect(g, c, ship);
				if (ship[HsTag.SPELLBURST] > 0) ship[HsTag.SPELLBURST] = 0; // (last, as live: Nether's Eye 123230)
			})
		};

		/// <summary>Guiding Figure's Spellburst: a random friendly minion's Deathrattle (its own source).</summary>
		private static void GuidingFigure(Game g, Controller c)
		{
			var withDr = c.BoardZone.GetAll().Where(m => m.HasDeathrattle && !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList();
			if (withDr.Count > 0) TriggerDeathrattleOf(g, c, withDr[g.Random.Next(withDr.Count)]);
		}

		private static void FelfusedBattery(Game g, Minion me) =>
			GiveAll(g, me, me.Controller.BoardZone.GetAll().Where(m => m != me && !m.Untouchable && !m.IsDead).ToList(), "GDB_110e2");

		private static void SoulboundSpire(Game g, Controller c, Minion me) =>
			Summon(g, c, RandomMinionOfCost(g, Math.Min(V("GDB_112", "max cost", 10), Math.Max(0, me.AttackDamage))), me, me.LastBoardPosition);

		private static void SpaceChunk1(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Starship pieces (their own effects at index 1)
			// [GDB_100] Arkonite Defense Crystal (4) — Taunt. Deathrattle: Gain 4 Armor. Starship Piece.
			cards.Add("GDB_100", new CardDef(new Power { DeathrattleTask = new ArmorTask(V("GDB_100", "armor", 4)), DeathrattleEffectIndex = 1 }));
			cards.Add("GDB_100e", new CardDef(new Power { DeathrattleTask = new ArmorTask(V("GDB_100", "armor", 4)) }));
			// [GDB_101] Dimensional Core (2) — Divine Shield. Starship Piece. / [GDB_105] Shattershard Turret (3) — Rush, Windfury.
			cards.Add("GDB_101", new CardDef(new Power { NoScript = true }));
			cards.Add("GDB_105", new CardDef(new Power { NoScript = true }));
			// [GDB_106] Guiding Figure (2) — Spellburst: Trigger a random friendly minion's Deathrattle. Starship Piece.
			cards.Add("GDB_106", new CardDef(new Power { Trigger = PieceTrigger(Spellburst(new CustomTask((g, c, s, t, stack) => GuidingFigure(g, c)))) }));
			cards.Add("GDB_106e", new CardDef(new Power { Trigger = StarshipSpellburst((g, c, ship) => GuidingFigure(g, c)) }));
			// [GDB_110] Felfused Battery (2) — After this attacks, give your other minions +1 Attack. Starship Piece.
			cards.Add("GDB_110", new CardDef(new Power
			{
				Trigger = PieceTrigger(new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me) FelfusedBattery(g, me); })
				})
			}));
			cards.Add("GDB_110e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.ENCHANTMENT_TARGET,
					EffectIndex = 1,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (Generic.StarshipOf(s) is Minion ship) FelfusedBattery(g, ship); })
				}
			}));
			cards.Add("GDB_110e2", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("GDB_110", "attack", 1))) }));
			// [GDB_112] Soulbound Spire (3) — Deathrattle: Summon a minion with Cost equal to this minion's Attack (up to 10).
			cards.Add("GDB_112", new CardDef(new Power
			{
				DeathrattleEffectIndex = 1,
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me) SoulboundSpire(g, c, me); })
			}));
			cards.Add("GDB_112e", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => { if (Generic.StarshipOf(s) is Minion ship) SoulboundSpire(g, c, ship); })
			}));
			// [SC_403] Starport (2, Location) — Summon a 2/1 Starship Piece with an effect when launched.
			cards.Add("SC_403", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId(StarportPieces[g.Random.Next(StarportPieces.Length)]), s))
			}));
			foreach (string piece in StarportPieces) cards.Add(piece, new CardDef(new Power { NoScript = true }));
			// The Starport pieces' launch effects (on the Starship, live dungar 19108-19240):
			cards.Add("SC_403ae", new CardDef(new Power { LaunchTask = new ArmorTask(V("SC_403a", "armor", 5)) }));
			cards.Add("SC_403be", new CardDef(new Power
			{
				LaunchTask = new CustomTask((g, c, s, t, stack) =>
					DamageAll(s, c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead).Cast<ICharacter>().ToList(), V("SC_403b", "damage", 2), false))
			}));
			cards.Add("SC_403ce", new CardDef(new Power
			{
				LaunchTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion ship) GainBonusEffects(g, ship, V("SC_403c", "effects", 2)); })
			}));
			cards.Add("SC_403de", new CardDef(new Power
			{
				LaunchTask = new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed).ToList();
					if (enemies.Count > 0) Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], V("SC_403d", "damage", 3), false);
				})
			}));
			cards.Add("SC_403fe", new CardDef(new Power
			{
				LaunchTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion ship)) return;
					Summon(g, c, Cards.FromId("SC_403t"), s, ship.ZonePosition + 1);
					Summon(g, c, Cards.FromId("SC_403t"), s, Math.Max(0, ship.ZonePosition));
				})
			}));
			// [SC_401] SCV (1) — Battlecry: Your next Starship launch costs (2) less.
			cards.Add("SC_401", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.DiscountStarshipLaunch(c, V("SC_401", "discount", 2)))
			}));
			// [SC_408] Ghost (4) — Stealth. Battlecry: If you're building a Starship, destroy the lowest-Cost card in your opponent's hand.
			cards.Add("SC_408", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => Generic.IsBuildingStarship(c), new CustomTask((g, c, s, t, stack) =>
				{
					var hand = c.Opponent.HandZone.GetAll().ToList();
					if (hand.Count == 0) return;
					int low = hand.Min(p => p.Cost);
					var lowest = hand.Where(p => p.Cost == low).ToList();
					Generic.Destroy(lowest[g.Random.Next(lowest.Count)], s);
				}))
			}));
			// [SC_410] Lift Off (3) — Draw 2 Terran cards. Summon a 2/1 Starship Piece with an effect when launched.
			cards.Add("SC_410", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					DrawWhere(g, c, IsTerran, V("SC_410", "cards", 2));
					Summon(g, c, Cards.FromId(StarportPieces[g.Random.Next(StarportPieces.Length)]), s);
				})
			}));
			// [GDB_120] The Exodar (7) — Battlecry: If you're building a Starship, launch it and choose a Protocol! (Free launch.)
			cards.Add("GDB_120", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => Generic.IsBuildingStarship(c), new CustomTask((g, c, s, t, stack) =>
				{
					if (!Generic.LaunchStarship(c, Generic.BuildingStarship(c), free: true)) return;
					ChooseAndCast("GDB_100a", "GDB_100b", "GDB_100c").Process(g, c, s, t);
				}))
			}));
			Minion lastLaunched(Controller c) => Generic.LaunchedStarships(c).LastOrDefault();
			cards.Add("GDB_100a", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(lastLaunched(c) is Minion ship)) return;
					for (int i = 0; i < 2; i++) c.Hero.GainArmor((IPlayable)s, Math.Max(0, ship.Health));
				})
			}));
			cards.Add("GDB_100b", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (lastLaunched(c) is Minion ship) IslandRandomHits(g, c, s, Math.Max(0, ship.AttackDamage), 1, false);
				})
			}));
			cards.Add("GDB_100c", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(lastLaunched(c) is Minion ship)) return;
					foreach (Card piece in Generic.StarshipPieceCards(ship))
						if (IslandToHand(c, s, piece) is IPlayable p) Give(g, s, p, "GBL_001e");
				})
			}));
			// [GDB_130] Crystal Welder (2) — Taunt. Battlecry: If you're building a Starship, gain +2/+2.
			cards.Add("GDB_130", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => Generic.IsBuildingStarship(c), new AddEnchantmentTask("GDB_130e", EntityType.SOURCE))
			}));
			cards.Add("GDB_130e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("GDB_130", "stats", 2))) }));
			// [GDB_340] Star Vulpera (5) — Tradeable. Battlecry: Destroy an enemy Starship or Starship Piece. (Forum t/138163: a
			// launched one — the one being built can't be targeted.)
			cards.Add("GDB_340", new CardDef(BattlecryEnemyMinion, new Power
			{
				TargetIf = (PlayReq.REQ_TARGET_WITH_RACE, (src, target) => target is Minion m && (m.Card[Generic.STARSHIP_PIECE] > 0 ||
					m.Card[Generic.STARSHIP] > 0 && m.Card[(GameTag)3471] > 0)),
				PowerTask = new DestroyTask(EntityType.TARGET)
			}));
			// [GDB_474] Warp Drive (3) — Draw 2 cards. If you're building a Starship, they cost (2) less.
			cards.Add("GDB_474", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new CustomTask((g, c, s, t, stack) => stack.Flag = Generic.IsBuildingStarship(c)),
					new FlagTask(true, new CustomTask((g, c, s, t, stack) =>
					{
						for (int i = 0; i < V("GDB_474", "cards", 2); i++)
							if (Generic.Draw(c) is IPlayable drawn && drawn.Zone?.Type == Zone.HAND) Give(g, s, drawn, GenericCostLess(V("GDB_474", "discount", 2)));
					})),
					new FlagTask(false, ComplexTask.Repeat(new DrawTask(), V("GDB_474", "cards", 2))))
			}));
			// [GDB_476] Suffocate (4) — Destroy a minion. If you're building a Starship, also destroy a random neighbor.
			cards.Add("GDB_476", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					var next = Neighbors(m).Where(x => !x.Untouchable && !x.ToBeDestroyed).ToList();
					Generic.Destroy(m, s);
					if (Generic.IsBuildingStarship(c) && next.Count > 0) Generic.Destroy(next[g.Random.Next(next.Count)], s);
				})
			}));

			// ---------------------------------------------------------------- Spellburst neutrals
			// [GDB_310] Ethereal Oracle (3) — Spell Damage +1. Spellburst: Draw 2 spells.
			cards.Add("GDB_310", new CardDef(new Power { Trigger = Spellburst(new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p is Spell, V("GDB_310", "spells", 2)))) }));
			// [GDB_311] Deep Space Curator (3) — Spellburst: Get a random minion of the spell's Cost. Set its Cost to (0).
			cards.Add("GDB_311", new CardDef(new Power
			{
				Trigger = Spellburst(new CustomTask((g, c, s, t, stack) =>
				{
					IPlayable spell = t ?? g.CurrentEventData?.EventSource as IPlayable;
					if (spell == null) return;
					var entry = c.PlayHistory.LastOrDefault(h => h.SourceId == spell.Id);
					int cost = entry.SourceCard != null ? entry.Cost : spell.Cost;
					if (IslandToHand(c, s, RandomMinionOfCost(g, cost)) is IPlayable p) Give(g, s, p, "GBL_009e");
				}))
			}));
			// [GDB_322] Lightfused Manasaber (6) — Rush. Spellburst: Gain Divine Shield.
			cards.Add("GDB_322", new CardDef(new Power { Trigger = Spellburst(new CustomTask((g, c, s, t, stack) => { if (s is Minion me) me.HasDivineShield = true; })) }));
			// [GDB_330] Ur'zul Rager (3) — Lifesteal. Spellburst: Attack a random enemy minion.
			cards.Add("GDB_330", new CardDef(new Power
			{
				Trigger = Spellburst(new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList();
					if (s is Minion me && enemies.Count > 0) EffectAttack(g, me, enemies[g.Random.Next(enemies.Count)]);
				}))
			}));
			// [GDB_860] Starscale Constellar (5) — Spellburst: Double this minion's Attack.
			cards.Add("GDB_860", new CardDef(new Power
			{
				Trigger = Spellburst(new CustomTask((g, c, s, t, stack) => { if (s is Minion me && me.AttackDamage > 0) Give(g, s, me, "GDB_860e", me.AttackDamage); }))
			}));
			cards.Add("GDB_860e", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackScriptTag }));
			// [GDB_143] Nexus-Prince Shaffar (3) — Spellburst: Give a minion in your hand +3/+3 and this Spellburst (unless it already
			// has these effects).
			ISimpleTask shaffar = new CustomTask((g, c, s, t, stack) =>
			{
				var minions = c.HandZone.GetAll().Where(p => Generic.IsMinionCard(p) && p.AppliedEnchantments?.Any(e => e.Card.Id == "GDB_143e") != true).ToList();
				if (minions.Count > 0) Give(g, s, minions[g.Random.Next(minions.Count)], "GDB_143e");
			});
			cards.Add("GDB_143", new CardDef(new Power { Trigger = Spellburst(shaffar) }));
			cards.Add("GDB_143e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("GDB_143", "stats", 3)), Effects.Health_N(V("GDB_143", "stats", 3)), new Effect(HsTag.SPELLBURST, EffectOperator.SET, 1)),
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => true),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e) || !(e.Target is Minion holder) || holder.Zone?.Type != Zone.PLAY || holder[HsTag.SPELLBURST] == 0) return;
						holder[HsTag.SPELLBURST] = 0;
						shaffar.Process(g, c, holder, t);
					})
				}
			}));
			// [GDB_463] Troubled Mechanic (2) — Divine Shield. Spellburst: Draw a Draenei.
			cards.Add("GDB_463", new CardDef(new Power { Trigger = Spellburst(new CustomTask((g, c, s, t, stack) => DrawOne(g, c, p => p is Minion m && m.IsRace(DRAENEI)))) }));

			// ---------------------------------------------------------------- Draenei
			// [GDB_131] Velen, Leader of the Exiled (7) — Taunt. Deathrattle: Trigger the Battlecries and Deathrattles of all other
			// Draenei you played this game. (Wiki: Battlecries from Velen, Deathrattles from the original minion.)
			cards.Add("GDB_131", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c[HsTag.REPEAT_DEPTH] >= 3) return;
					foreach (PlayHistoryEntry h in c.PlayHistory.Where(h => h.SourceId != s.Id && h.SourceCard?.Type == CardType.MINION && h.SourceCard.IsRace(DRAENEI)).ToList())
					{
						if (h.SourceCard.Power?.PowerTask != null) RepeatEffect(g, c, s, h.SourceCard);
						if (h.SourceCard.Power?.DeathrattleTask != null && g.IdEntityDic.TryGetValue(h.SourceId, out IPlayable original))
						{
							c[HsTag.REPEAT_DEPTH] += 1;
							try { TriggerDeathrattleOf(g, c, original); }
							finally { c[HsTag.REPEAT_DEPTH] -= 1; }
						}
					}
				})
			}));
			// [GDB_450] Ace Wayfinder (4) — Battlecry: Gain two random Bonus Effects. The next Draenei you play gains them as well.
			cards.Add("GDB_450", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me)) return;
					var gained = new List<GameTag>();
					for (int i = 0; i < V("GDB_450", "effects", 2); i++)
						if (Generic.RandomBonusEffect(g, me) is GameTag k) { Generic.GiveBonusEffect(me, k); gained.Add(k); }
					Generic.AddEnchantmentBlock(g, Cards.FromId("GDB_450e"), me, c, gained.Count > 0 ? (int)gained[0] : 0, gained.Count > 1 ? (int)gained[1] : 0, 0);
				})
			}));
			cards.Add("GDB_450e", DraeneiFutureBuff((g, e, played) =>
			{
				foreach (int k in new[] { e[GameTag.TAG_SCRIPT_DATA_NUM_1], e[GameTag.TAG_SCRIPT_DATA_NUM_2] })
					if (k > 0) Generic.GiveBonusEffect(played, (GameTag)k);
			}));
			// [GDB_461] Astral Vigilant (1) — Battlecry: Get a copy of the last Draenei you played.
			cards.Add("GDB_461", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var last = c.PlayHistory.LastOrDefault(h => h.SourceId != s.Id && h.SourceCard?.Type == CardType.MINION && h.SourceCard.IsRace(DRAENEI));
					if (last.SourceCard != null) IslandToHand(c, s, last.SourceCard);
				})
			}));
			// [GDB_720] Starlight Wanderer (1) — Battlecry: The next Draenei you play gains +2/+1.
			cards.Add("GDB_720", new CardDef(new Power { PowerTask = new AddEnchantmentTask("GDB_720e", EntityType.CONTROLLER) }));
			cards.Add("GDB_720e", DraeneiFutureBuff((g, e, played) => Give(g, e, played, "GDB_720e1")));
			cards.Add("GDB_720e1", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("GDB_720", "attack", 2)), Effects.Health_N(V("GDB_720", "health", 1))) }));
			// [GDB_722] Crimson Commander (3) — Battlecry and Deathrattle: Give all Draenei in your hand +2/+1.
			ISimpleTask redShirts = new CustomTask((g, c, s, t, stack) =>
				GiveAll(g, s, c.HandZone.GetAll().Where(p => p is Minion m && m.IsRace(DRAENEI)).ToList(), "GDB_722e"));
			cards.Add("GDB_722", new CardDef(new Power { PowerTask = redShirts, DeathrattleTask = redShirts }));
			cards.Add("GDB_722e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("GDB_722", "attack", 2)), Effects.Health_N(V("GDB_722", "health", 1))) }));
			// [GDB_723] Hologram Operator (2) — Battlecry: Get 3 random Temporary Draenei.
			cards.Add("GDB_723", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("GDB_723", "draenei", 3); i++)
						if (IslandToHand(c, s, RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(DRAENEI))) is IPlayable p) Generic.MakeTemporary(p, s);
				})
			}));
			// [GDB_861] Stranded Spaceman (2) — Battlecry: The next Draenei you play gains +2 Health and Rush.
			cards.Add("GDB_861", new CardDef(new Power { PowerTask = new AddEnchantmentTask("GDB_861e", EntityType.CONTROLLER) }));
			cards.Add("GDB_861e", DraeneiFutureBuff((g, e, played) => Give(g, e, played, "GDB_861e2")));
			cards.Add("GDB_861e2", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("GDB_861", "health", 2)), new Effect(GameTag.RUSH, EffectOperator.SET, 1)) }));
			// [GDB_862] Galactic Crusader (7) — Taunt. Deathrattle: Get two random Holy spells. They cost (3) less.
			cards.Add("GDB_862", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("GDB_862", "spells", 2); i++)
						if (IslandToHand(c, s, RandomCard(g, x => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == HOLY)) is IPlayable p)
							Give(g, s, p, GenericCostLess(V("GDB_862", "discount", 3)));
				})
			}));
			// [GDB_863] Lunar Trailblazer (5) — Battlecry: Set the Cost of a random spell in your hand to this minion's Cost.
			cards.Add("GDB_863", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var spells = c.HandZone.GetAll().Where(p => p is Spell).ToList();
					if (spells.Count > 0) Give(g, s, spells[g.Random.Next(spells.Count)], "GDB_863e", ((IPlayable)s).Cost);
				})
			}));
			cards.Add("GDB_863e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(0)) { UseScriptTag = true } }));
			// [GDB_874] Astrobiologist (2) — Battlecry: At the start of your next turn, Discover a spell.
			cards.Add("GDB_874", new CardDef(new Power { PowerTask = new AddEnchantmentTask("GDB_874e", EntityType.CONTROLLER) }));
			cards.Add("GDB_874e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = ComplexTask.Create(DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL), RemoveEnchantmentTask.Task)
				}
			}));
			// [GDB_471] Voronei Recruiter (2) — At the end of your turn, get a 4/4 Crewmate with a random Bonus Effect.
			cards.Add("GDB_471", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) => { var crew = Crewmates(); if (crew.Count > 0) AddToHand(c, crew[g.Random.Next(crew.Count)]); })
				}
			}));
			foreach (Card crewmate in Crewmates()) cards.Add(crewmate.Id, new CardDef(new Power { PowerTask = SummonAdjoiningCrewmates }));
			// [GDB_469] Auchenai Death-Speaker (1) — After another friendly minion is Reborn, summon a copy of it.
			cards.Add("GDB_469", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					Condition = new SelfCondition(p => p[HsTag.REBORN_SUMMON] > 0),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if ((t ?? g.CurrentEventData?.EventSource as IPlayable) is Minion back && back.Zone?.Type == Zone.PLAY && !c.BoardZone.IsFull)
							Generic.Copy(c, s, back, Zone.PLAY, position: back.ZonePosition + 1);
					})
				}
			}));

			// ---------------------------------------------------------------- Demon Hunter
			// [GDB_117] Dirdra, Rebel Captain (4) — Rush. Battlecry: Shuffle all 8 Crewmates into your deck. Deathrattle: Draw two
			// Crewmates.
			cards.Add("GDB_117", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Card crew in Crewmates())
						Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, crew, new EntityData { [GameTag.CREATOR] = s.Id }));
				}),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p.Card[(GameTag)3631] > 0, V("GDB_117", "crewmates", 2)))
			}));
			// [GDB_119] Emergency Meeting (1) — Get two 4/4 Crewmates. Put a random Demon that costs (3) or less between them.
			cards.Add("GDB_119", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var crew = Crewmates();
					if (crew.Count == 0) return;
					IslandToHand(c, s, crew[g.Random.Next(crew.Count)]);
					IslandToHand(c, s, RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.DEMON) && x.Cost <= V("GDB_119", "cost", 3)));
					IslandToHand(c, s, crew[g.Random.Next(crew.Count)]);
				})
			}));
			// [GDB_473] Headhunt (1) — Deal $2 damage. Get a 4/4 Crewmate with a random Bonus Effect.
			cards.Add("GDB_473", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("GDB_473", "damage", 2), EntityType.TARGET, true),
					new CustomTask((g, c, s, t, stack) => { var crew = Crewmates(); if (crew.Count > 0) AddToHand(c, crew[g.Random.Next(crew.Count)]); }))
			}));
			// [GDB_116] Eldritch Being (1) — Outcast and Spellburst: Shuffle your hand.
			cards.Add("GDB_116", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (PlayedAsOutcast.Eval((IPlayable)s)) ShuffleHand(g, c); }),
				Trigger = Spellburst(new CustomTask((g, c, s, t, stack) => ShuffleHand(g, c)))
			}));
			// [GDB_118] Xor'toth, Breaker of Stars (5) — Battlecry: Add two Stars to both sides of your hand. When they collide, deal 5
			// damage to all enemies. (Live night 13782: one far left, one far right.)
			cards.Add("GDB_118", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!c.HandZone.IsFull)
						Generic.AddHandPhase.Invoke(c, Entity.FromCard(c, Cards.FromId("GDB_118t"), new EntityData { [GameTag.CREATOR] = s.Id, [GameTag.ZONE_POSITION] = 1 }));
					IslandToHand(c, s, Cards.FromId("GDB_118t2"));
				})
			}));
			ISimpleTask starCollision = new DamageTask(V("GDB_118", "damage", 5), EntityType.ENEMIES, true);
			cards.Add("GDB_118t", new CardDef(new Power { PlayableIf = (PlayReq.REQ_CANNOT_PLAY_THIS, ctl => false), CollidesWith = "GDB_118t2", CollisionTask = starCollision }));
			cards.Add("GDB_118t2", new CardDef(new Power { PlayableIf = (PlayReq.REQ_CANNOT_PLAY_THIS, ctl => false), CollidesWith = "GDB_118t", CollisionTask = starCollision }));
			// [GDB_902] Infiltrate (3) — Choose a minion. Deal $3 damage to all other minions.
			cards.Add("GDB_902", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					DamageAll(s, c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => m != t && !m.IsDead).Cast<ICharacter>().ToList(), V("GDB_902", "damage", 3), true))
			}));
			// [SC_009] Lurker (4) — After a friendly minion attacks, deal 1 damage to a random enemy (or 2 if your minion is a Zerg).
			cards.Add("SC_009", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.MINIONS,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						bool zerg = g.CurrentEventData?.EventSource is IPlayable attacker && IsZerg(attacker);
						IslandRandomHits(g, c, s, 1, zerg ? V("SC_009", "zerg damage", 2) : V("SC_009", "damage", 1), false);
					})
				}
			}));
			// [SC_011] Creep Tumor (2, Objective) — Your Zerg minions have +1 Attack and Rush. Lasts 3 turns.
			cards.Add("SC_011", new CardDef(PaladinAura(new Power
			{
				Aura = new Aura(AuraType.BOARD, "SC_011e") { Condition = new SelfCondition(IsZerg) }
			})));
			cards.Add("SC_011e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("SC_011", "attack", 1)), new Effect(GameTag.RUSH, EffectOperator.SET, 1)) }));
			// [SC_022] Mutalisk (4) — Also damages minions next to whomever this attacks (and the enemy hero if a neighbor is missing).
			cards.Add("SC_022", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me) || !(g.CurrentEventData?.EventTarget is Minion defender)) return;
						var adjacent = defender.GetAdjacentMinions().ToList();
						foreach (Minion next in adjacent) if (!next.IsDead) next.TakeDamage(me, me.AttackDamage);
						if (adjacent.Count < 2) c.Opponent.Hero.TakeDamage(me, me.AttackDamage);
					})
				}
			}));

			// ---------------------------------------------------------------- Death Knight
			// [GDB_468] Wakener of Souls (10) — Taunt, Reborn. Deathrattle: Resurrect a different friendly Deathrattle minion.
			cards.Add("GDB_468", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
					ResurrectOne(g, c, s, p => p.Card.Deathrattle && p.Card != ((IPlayable)s).Card))
			}));
			// [GDB_475] Orbital Moon (1) — Give a minion Taunt and Lifesteal. If you played an adjacent card this turn, also give it Reborn.
			cards.Add("GDB_475", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					m[GameTag.TAUNT] = 1;
					m[GameTag.LIFESTEAL] = 1;
					if (s[HsTag.ADJACENT_PLAYED_TURN] == g.Turn) m[GameTag.REBORN] = 1;
				})
			}));
			// [GDB_477] The 8 Hands From Beyond (8) — Battlecry: Destroy both players' decks EXCEPT the 8 highest Cost cards in each.
			cards.Add("GDB_477", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Controller who in new[] { c, c.Opponent })
					{
						var keep = who.DeckZone.GetAll().OrderByDescending(p => p.Cost).ThenBy(_ => g.Random.Next()).Take(V("GDB_477", "kept", 8)).ToHashSet();
						foreach (IPlayable p in who.DeckZone.GetAll().Where(p => !keep.Contains(p)).ToList()) Generic.Destroy(p, s, many: true);
					}
				})
			}));
			// [GDB_478] Assimilating Blight (3) — Discover a 3-Cost Deathrattle minion. Summon it with Reborn.
			cards.Add("GDB_478", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.Cost == V("GDB_478", "cost", 3) && x.Deathrattle,
					afterPick: new CustomTask((g, c, s, t, stack) =>
					{
						if (t is Minion m && m.Zone?.Type == Zone.HAND && Generic.SummonFromHand(c, m, s)) m[GameTag.REBORN] = 1;
					}))
			}));
			// [SC_001] Baneling Barrage (1) — Get a 1/1 Baneling that explodes. If you control a Zerg minion, get another Baneling.
			cards.Add("SC_001", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new AddCardTo("SC_019t", EntityType.HAND),
					ComplexTask.If((g, c, s) => c.BoardZone.GetAll().Any(m => IsZerg(m)), new AddCardTo("SC_019t", EntityType.HAND)))
			}));
			// [SC_019t] Baneling — Deathrattle: Deal damage equal to this minion's Attack to all enemy minions.
			cards.Add("SC_019t", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (s is Minion me)
						DamageAll(s, c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead).Cast<ICharacter>().ToList(), Math.Max(0, me.AttackDamage), false);
				})
			}));
			// [SC_002] Infestor (3) — Deathrattle: Your Zerg minions have +1 Attack for the rest of the game.
			cards.Add("SC_002", new CardDef(new Power { DeathrattleTask = new AddEnchantmentTask("SC_002e", EntityType.CONTROLLER) }));
			cards.Add("SC_002e", new CardDef(new Power { Aura = new Aura(AuraType.BOARD, "SC_002e2") { Condition = new SelfCondition(IsZerg) } }));
			cards.Add("SC_002e2", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("SC_002", "attack", 1))) }));
			// [SC_018] Viper (4) — Battlecry: Summon a minion from your opponent's hand. Your other Zerg minions gain Reborn and attack it.
			cards.Add("SC_018", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.Opponent.HandZone.GetAll().Where(p => Generic.IsMinionCard(p)).ToList();
					if (minions.Count == 0 || c.BoardZone.IsFull) return;
					var taken = (Minion)minions[g.Random.Next(minions.Count)];
					Generic.RemoveFromHand(c.Opponent, taken);
					if (!(Generic.GiveTo(taken, c) is Minion mine)) return;
					Generic.SummonBlock.Invoke(g, mine, -1, s);
					foreach (Minion z in c.BoardZone.GetAll().Where(m => m != s && m != mine && IsZerg(m)).ToList())
					{
						z[GameTag.REBORN] = 1;
						if (mine.IsDead || mine.ToBeDestroyed || mine.Zone?.Type != Zone.PLAY) continue;
						EffectAttack(g, z, mine);
					}
				})
			}));

			// ---------------------------------------------------------------- Neutral (other)
			// [GDB_129] Doommaiden (4) — Battlecry: Draw a card from your opponent's deck. If you don't play it this turn, put it back.
			cards.Add("GDB_129", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.Opponent.DeckZone.Count == 0) return;
					IPlayable top = c.Opponent.DeckZone.TopCard;
					if (c.HandZone.IsFull) { Generic.Destroy(top, s); return; }
					if (Generic.GiveTo(top, c, Zone.HAND) is IPlayable mine)
					{
						Generic.AddHandPhase.Invoke(c, mine);
						Give(g, s, mine, "GDB_129e");
					}
				})
			}));
			cards.Add("GDB_129e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e) || !(e.Target is IPlayable card) || card.Zone?.Type != Zone.HAND) return;
						Controller owner = c.Opponent;
						Generic.RemoveFromHand(card.Controller, card);
						card.Controller = owner;
						card[GameTag.CONTROLLER] = owner.PlayerId;
						Generic.PutOnTopOfDeck(owner, card);
					})
				}
			}));
			// [GDB_132] Relentless Wrathguard (3) — Battlecry: Deal 2 damage to an enemy minion. If it dies, Discover a Demon.
			cards.Add("GDB_132", new CardDef(BattlecryEnemyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Generic.DamageCharFunc((IPlayable)s, m, V("GDB_132", "damage", 2), false);
					if (m.IsDead || m.ToBeDestroyed || m.Health <= 0)
						DiscoverFromTask.ClassOrNeutral((ctl, x) => x.Type == CardType.MINION && x.IsRace(Race.DEMON)).Process(g, c, s, null);
				})
			}));
			// [GDB_142] The Ceaseless Expanse (100) — Costs (1) less for each time a card was drawn, played, or destroyed. Battlecry:
			// Destroy all other minions.
			cards.Add("GDB_142", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => new[] { p.Controller, p.Controller.Opponent }.Sum(x =>
					x[HsTag.CARDS_DRAWN_THIS_GAME] + x.PlayHistory.Count + x.DiedThisGame.Count + x[HsTag.CARDS_DESTROYED_THIS_GAME])),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => m != s && !m.Untouchable && !m.ToBeDestroyed).ToList())
						Generic.Destroy(m, s);
				})
			}));
			// [GDB_145] Kil'jaeden (7) — Battlecry: Replace your deck with an endless portal of Demons. Each turn, they gain an additional
			// +2/+2. (Wiki: the deck removed, not destroyed; 30 random Demons, refilled; +2/+2 more at the end of each of your turns.)
			cards.Add("GDB_145", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.Hero[(GameTag)3808] > 0) return; // another Kil'jaeden keeps the portal and its buffs (wiki)
					foreach (IPlayable p in c.DeckZone.GetAll().ToList()) { c.DeckZone.Remove(p); p[GameTag.ZONE] = (int)Zone.REMOVEDFROMGAME; }
					c.Hero[(GameTag)3808] = 1;
					Generic.AddEnchantmentBlock(g, Cards.FromId("GDB_145e"), (IPlayable)s, c, 0, 0, 0);
					KiljaedenRefill(g, c, s);
				})
			}));
			cards.Add("GDB_145e", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.DRAW)
					{
						TriggerSource = TriggerSource.FRIENDLY,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (!(s is Enchantment e)) return;
							if ((t ?? g.CurrentEventData?.EventSource as IPlayable) is Minion drawn && drawn.Zone?.Type == Zone.HAND && e[GameTag.TAG_SCRIPT_DATA_NUM_2] > 0)
								Generic.AddEnchantmentBlock(g, Cards.FromId("GDB_145e2"), e.Creator ?? drawn, drawn, e[GameTag.TAG_SCRIPT_DATA_NUM_2], e[GameTag.TAG_SCRIPT_DATA_NUM_2], 0);
							KiljaedenRefill(g, c, s);
						})
					},
					new Trigger(TriggerType.TURN_END)
					{
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (s is Enchantment e) e[GameTag.TAG_SCRIPT_DATA_NUM_2] = e[GameTag.TAG_SCRIPT_DATA_NUM_2] + V("GDB_145", "growth", 2);
						})
					})
			}));
			cards.Add("GDB_145e2", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackHealthScriptTag }));
			// [GDB_320] Eredar Brute (7) — Taunt, Lifesteal. Costs (1) less for each enemy minion.
			cards.Add("GDB_320", new CardDef(new Power { Aura = new AdaptiveCostEffect(p => p.Controller.Opponent.BoardZone.GetAll().Count(m => !m.Untouchable)) }));
			// [GDB_321] Mutating Lifeform (5) — After this survives damage, gain a random Bonus Effect.
			cards.Add("GDB_321", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.SELF,
					Condition = new SelfCondition(p => p is Minion m && m.Health > 0 && !m.ToBeDestroyed),
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me) GainBonusEffects(g, me, 1); })
				}
			}));
			// [GDB_331] Splitting Spacerock (8) — Deathrattle: Summon two 4/4 Splitting Boulders. (→ two 2/2 Stones → two 1/1 Pebbles.)
			cards.Add("GDB_331", new CardDef(new Power { DeathrattleTask = new SummonTask("GDB_331t1", 2, SummonSide.DEATHRATTLE) }));
			cards.Add("GDB_331t1", new CardDef(new Power { DeathrattleTask = new SummonTask("GDB_331t2", 2, SummonSide.DEATHRATTLE) }));
			cards.Add("GDB_331t2", new CardDef(new Power { DeathrattleTask = new SummonTask("GDB_331t3", 2, SummonSide.DEATHRATTLE) }));
			// [GDB_333] Space Pirate (1) — Deathrattle: Your next weapon costs (1) less.
			cards.Add("GDB_333", new CardDef(new Power { DeathrattleTask = new AddEnchantmentTask("GDB_333e", EntityType.CONTROLLER) }));
			cards.Add("GDB_333e", NextCardCost(Effects.ReduceCost(V("GDB_333", "discount", 1)), new SelfCondition(p => p is Weapon), "GBL_003e"));
			// [GDB_341] Red Giant (8) — Costs (1) less for each adjacent card played while in hand.
			cards.Add("GDB_341", new CardDef(new Power { Aura = new AdaptiveCostEffect(p => p[HsTag.ADJACENT_PLAYED_COUNT]) }));
			// [GDB_343] Perplexing Anomaly (3) — Rush, Taunt, ...Stealth? (keywords only)
			cards.Add("GDB_343", new CardDef(new Power { NoScript = true }));
			// [GDB_435] Moonstone Mauler (2) — Battlecry: Shuffle 3 Asteroids into your deck that deal @ damage to a random enemy when drawn.
			cards.Add("GDB_435", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => ShuffleAsteroids(c, s, V("GDB_435", "asteroids", 3))), HandCounter = AsteroidDamage }));
			// (its "$@": its own number plus the player's "Your Asteroids deal N more" — Bolide Behemoth)
			cards.Add("GDB_430", new CardDef(new Power { PowerTask = AsteroidHit, TopdeckTask = AsteroidHit, HandCounter = AsteroidDamage }));
			// [GDB_877] Escape Pod (3) — Rush. Deathrattle: Give adjacent minions +1/+1 and Rush.
			cards.Add("GDB_877", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me)) return;
					foreach (int slot in new[] { me.LastBoardPosition - 1, me.LastBoardPosition })
						if (slot >= 0 && slot < c.BoardZone.Count && c.BoardZone[slot] is Minion m && !m.Untouchable) Give(g, s, m, "GDB_877e");
				})
			}));
			cards.Add("GDB_877e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("GDB_877", "stats", 1)), Effects.Health_N(V("GDB_877", "stats", 1)), new Effect(GameTag.RUSH, EffectOperator.SET, 1)) }));
			// [GDB_878] Braingill (2) — Battlecry: Give your other Murlocs "Deathrattle: Draw a card."
			cards.Add("GDB_878", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.BoardZone.GetAll().Where(m => m != s && m.IsRace(Race.MURLOC)).ToList(), "GDB_878e"))
			}));
			cards.Add("GDB_878e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.DEATHRATTLE, EffectOperator.SET, 1)), DeathrattleTask = new DrawTask() }));
			// [SC_013] Grunty (8) — Battlecry: Summon four random Murlocs, then shoot them at enemy minions. (You pick the targets!) (Wiki:
			// the player picks up to four enemy minions; the attacks left to right.)
			cards.Add("SC_013", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var murlocs = new List<int>();
					for (int i = 0; i < V("SC_013", "murlocs", 4); i++)
						if (IslandSummon(g, c, RandomBoardMinion(g, x => x.IsRace(Race.MURLOC)), s) is Minion m) murlocs.Add(m.Id);
					int me = c.PlayerId;
					s[HsTag.PICK_POINTER] = 0;
					// (ids and the pointer on Grunty: the pick may resolve on an AI's copy of the game — Generic.PickTargetsInOrder)
					Generic.PickTargetsInOrder(c, s, murlocs.Count, ch => ch is Minion m && m.Controller.PlayerId != me && !m.Untouchable, (g2, c2, s2, pick, before) =>
					{
						int shot = s2[HsTag.PICK_POINTER];
						Minion Shooter(int i) => g2.IdEntityDic.TryGetValue(murlocs[i], out IPlayable p) ? p as Minion : null;
						while (shot < murlocs.Count && (!(Shooter(shot) is Minion alive) || alive.IsDead || alive.Zone?.Type != Zone.PLAY)) shot++;
						if (shot < murlocs.Count && pick is Minion enemy && !enemy.IsDead) EffectAttack(g2, Shooter(shot), enemy);
						s2[HsTag.PICK_POINTER] = shot + 1;
					}, different: false);
				})
			}));

			// ---------------------------------------------------------------- StarCraft faction cards
			// [SC_000] Spawning Pool (1, Location) — Get a 1/1 Zergling. Deathrattle: Your Zerg minions have Rush this turn.
			cards.Add("SC_000", new CardDef(new Power
			{
				PowerTask = new AddCardTo("SC_010", EntityType.HAND),
				DeathrattleTask = new AddEnchantmentTask("SC_000te", EntityType.CONTROLLER)
			}));
			cards.Add("SC_000te", new CardDef(new Power
			{
				Aura = new Aura(AuraType.BOARD, "SC_000te2") { Condition = new SelfCondition(IsZerg) },
				Trigger = new Trigger(TriggerType.TURN_END) { EitherTurn = true, SingleTask = RemoveEnchantmentTask.Task }
			}));
			cards.Add("SC_000te2", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.RUSH, EffectOperator.SET, 1)) }));
			// [SC_003] Hive Queen (3) — At the end of your turn, get a Larva that transforms into random Zerg minions.
			cards.Add("SC_003", new CardDef(new Power { Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new AddCardTo("SC_003t", EntityType.HAND) } }));
			ISimpleTask larva = new CustomTask((g, c, s, t, stack) =>
				Generic.ShiftInHand(s, me => RandomCard(g, x => x.Type == CardType.MINION && x.Collectible && IsZergCard(x) && x.Id != "SC_003"), Cards.FromId("SC_003te")));
			cards.Add("SC_003t", new CardDef(new Power { Trigger = new Trigger(TriggerType.TURN_START) { TriggerActivation = TriggerActivation.HAND, SingleTask = larva } }));
			cards.Add("SC_003te", new CardDef(new Power
			{
				Enchant = new Enchant(GameTag.SHIFTING_MINION, EffectOperator.SET, 1) { RemoveWhenPlayed = true },
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = larva }
			}));
			// [SC_010] Zergling (1) — Battlecry: Summon a 1/1 Zergling.
			cards.Add("SC_010", new CardDef(new Power { PowerTask = new SummonTask("SC_010", SummonSide.RIGHT) }));
			// [SC_015] Nydus Worm (3) — Draw two Zerg cards. They cost (1) less.
			cards.Add("SC_015", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("SC_015", "cards", 2); i++)
						if (DrawOne(g, c, IsZerg) is IPlayable drawn && drawn.Zone?.Type == Zone.HAND) Give(g, s, drawn, GenericCostLess(V("SC_015", "discount", 1)));
				})
			}));
			// [SC_750] Chrono Boost (4) — Draw 2 Protoss cards. Summon a 3/4 Zealot with Charge.
			cards.Add("SC_750", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					DrawWhere(g, c, IsProtoss, V("SC_750", "cards", 2));
					Summon(g, c, Cards.FromId("SC_751t"), s);
				})
			}));
			// [SC_751] Warp Gate (4, Location) — Your next Protoss minion costs (3) less.
			cards.Add("SC_751", new CardDef(new Power { PowerTask = new AddEnchantmentTask("SC_751e", EntityType.CONTROLLER) }));
			cards.Add("SC_751e", NextCardCost(Effects.ReduceCost(V("SC_751", "discount", 3)), new SelfCondition(p => p is Minion && IsProtoss(p)), "GBL_004e"));
			// [SC_753] Photon Cannon (2) — Deal $3 damage. If this kills a minion, your Protoss minions cost (1) less this game.
			cards.Add("SC_753", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is ICharacter target)) return;
					Generic.DamageCharFunc((IPlayable)s, target, V("SC_753", "damage", 3), true);
					if (target is Minion m && (m.IsDead || m.ToBeDestroyed || m.Health <= 0))
						Generic.AddEnchantmentBlock(g, Cards.FromId("SC_755e"), (IPlayable)s, c, 0, 0, 0);
				})
			}));
			cards.Add("SC_755e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("SC_753", "discount", 1))) { Condition = new SelfCondition(p => p is Minion && IsProtoss(p)) }
			}));
			// [SC_783] Void Ray (3) — Rush, Divine Shield. Battlecry: If this costs (0), gain +2/+2. (The cost it was played for.)
			cards.Add("SC_783", new CardDef(new Power
			{
				GlowAtZeroCost = true,
				PowerTask = ComplexTask.If((g, c, s) => PlayedForZero(s), new AddEnchantmentTask("SC_783e", EntityType.SOURCE))
			}));
			cards.Add("SC_783e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("SC_783", "stats", 2))) }));
		}

		/// <summary>Kil'jaeden's portal: the deck back to 30 random Demons (wiki: "a self-refilling deck of 30 random demons"; "The
		/// portal cannot generate another Kil'jaeden").</summary>
		private static void KiljaedenRefill(Game g, Controller c, IEntity source)
		{
			int want = V("GDB_145", "portal", 30);
			while (c.DeckZone.Count < want && !c.DeckZone.IsFull)
			{
				Card demon = RandomCard(g, x => x.Type == CardType.MINION && x.Collectible && x.IsRace(Race.DEMON) && x.Id != "GDB_145");
				if (demon == null) break;
				IPlayable p = Entity.FromCard(c, demon, new EntityData { [GameTag.CREATOR] = source.Id });
				c.DeckZone.Add(p);
			}
		}
	}
}
