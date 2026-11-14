// HsServer addition (not upstream SabberStoneCore): The Great Dark Beyond — chunk 4: Warlock, Warrior and Shaman (GDB_ and the
// StarCraft mini-set's Zerg / Terran cards; spec: research spec_chunk4). Shared pieces: SpaceCardsGenHs.cs, Actions/Starship.cs
// (Starships, launch upgrades, extra attacks, stat sets). Every number goes through CardValues (V).
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
		/// <summary>"The next Draenei you play …" that acts once it was played (its Battlecry done: Expedition Sergeant's attack,
		/// Unyielding Vindicator's Attack): a player enchantment waiting for a played Draenei (AFTER_PLAY_MINION), then gone.</summary>
		private static CardDef DraeneiAfterPlayBuff(Action<Game, Enchantment, Minion> apply) => new CardDef(new Power
		{
			Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
			{
				TriggerSource = TriggerSource.FRIENDLY,
				Condition = new SelfCondition(p => p is Minion m && m.IsRace(DRAENEI)),
				SingleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Enchantment e) || !((t ?? g.CurrentEventData?.EventSource as IPlayable) is Minion played)) return;
					apply(g, e, played);
					e.Remove();
				})
			}
		});

		/// <summary>"Steal N Health from …" (K'ara): the victim loses N maximum Health (<paramref name="victimEnchantment"/>, can
		/// kill), the stealer gains N (<paramref name="stealerEnchantment"/>) — the client's eb / e2b pair (SCH_354's naming).
		/// Not damage: Immune doesn't stop it.</summary>
		private static void StealHealth(Game g, IPlayable stealer, ICharacter victim, string victimEnchantment, string stealerEnchantment)
		{
			if (victim == null) return;
			Give(g, stealer, victim, victimEnchantment);
			if (stealer is Minion me && me.Zone?.Type == Zone.PLAY) Give(g, stealer, me, stealerEnchantment);
		}

		/// <summary>Felfire Thrusters' Spellburst: this minion's Attack to two different random enemy minions, one damage step.</summary>
		private static void FelfireThrusters(Game g, Minion me)
		{
			int attack = me.AttackDamage;
			if (attack <= 0) return;
			var enemies = me.Controller.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed).Cast<IPlayable>().ToList();
			TitansC3DamageGroup(g, me.Controller, me, enemies.OrderBy(_ => g.Random.Next()).Take(V("GDB_104", "targets", 2)), attack, false);
		}

		/// <summary>Yamato Cannon: destroy a random enemy minion (the piece's Battlecry, or the Starship at its launch).</summary>
		private static void YamatoCannon(Game g, Controller c, IEntity source)
		{
			var enemies = c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed).ToList();
			if (enemies.Count > 0) Generic.Destroy(enemies[g.Random.Next(enemies.Count)], source);
		}

		/// <summary>Siege Tank: 10 damage to a random enemy minion; <paramref name="excessToHero"/>: what it doesn't need goes to the
		/// enemy hero (worked out before the hit: Generic.ExcessDamage).</summary>
		private static void SiegeTank(Game g, Controller c, IPlayable source, bool excessToHero)
		{
			var enemies = c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed).ToList();
			if (enemies.Count == 0) return;
			Minion victim = enemies[g.Random.Next(enemies.Count)];
			int damage = V("SC_413", "damage", 10);
			if (!excessToHero) { Generic.DamageCharFunc(source, victim, damage, false); return; }
			int excess = Generic.DamageWithExcess(source, victim, damage, false); // (WHIZBANG: the shared capped excess hit)
			if (excess > 0) Generic.DamageCharFunc(source, c.Opponent.Hero, excess, false);
		}

		/// <summary>Replicating Spore: summon every minion the player's Spores stored (in order), then a random 5-Cost one, stored
		/// for the next (wiki Spore Empress Moldara: "The summoned 5-Cost minions are 'stored' into future Replicating Spores").</summary>
		private static void ReplicatingSpore(Game g, Controller c, IEntity source)
		{
			int stored = Math.Min(c[HsTag.SPORE_COUNT], 9);
			for (int i = 0; i < stored && !c.BoardZone.IsFull; i++)
				if (Cards.FromAssetId(c[HsTag.SPORE_CARD_BASE + i]) is Card card) Summon(g, c, card, source);
			if (!(RandomMinionOfCost(g, V("GDB_234t", "cost", 5)) is Card fresh)) return;
			Summon(g, c, fresh, source);
			if (stored < 9)
			{
				c[HsTag.SPORE_CARD_BASE + stored] = fresh.AssetId;
				c[HsTag.SPORE_COUNT] = stored + 1;
			}
		}

		/// <summary>Nebula's Discover: an 8-Cost minion (class or neutral), summoned with Taunt and Elusive (no enchantment card
		/// exists: the tags go on the minion).</summary>
		private static void NebulaDiscover(Game g, Controller c, IEntity source)
		{
			var pool = Cards.FormatTypeCards(g.FormatType).Where(x => x.Collectible && x.Type == CardType.MINION && x.Cost == V("GDB_479", "cost", 8) &&
				(x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL)));
			Card[] offer = DiscoverFromTask.PickDistinct(g, pool, 3).ToArray();
			if (offer.Length == 0) return;
			Generic.CreateChoiceCards.Invoke(c, source, null, ChoiceType.GENERAL, ChoiceAction.STACK, offer, new CustomTask((g2, c2, s2, pick, st) =>
			{
				if (pick == null || c2.BoardZone.IsFull) return;
				var m = (Minion)Entity.FromCard(c2, pick.Card, new EntityData { [GameTag.CREATOR] = s2.Id });
				Generic.SummonBlock.Invoke(g2, m, -1, s2);
				if (m.Zone?.Type != Zone.PLAY) return;
				m[GameTag.TAUNT] = 1;
				m[GameTag.CANT_BE_TARGETED_BY_SPELLS] = 1;
				m[GameTag.CANT_BE_TARGETED_BY_HERO_POWERS] = 1;
			}));
		}

		private static void SpaceChunk4(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Warlock
			// [GDB_104] Felfire Thrusters (3) — Spellburst: Deal this minion's Attack damage to 2 random enemy minions. Starship Piece.
			cards.Add("GDB_104", new CardDef(new Power { Trigger = PieceTrigger(Spellburst(new CustomTask((g, c, s, t, stack) => { if (s is Minion me) FelfireThrusters(g, me); }))) }));
			cards.Add("GDB_104e", new CardDef(new Power { Trigger = StarshipSpellburst((g, c, ship) => FelfireThrusters(g, ship)) }));
			// [GDB_109] Heart of the Legion (2) — Lifesteal. Starship Piece. (the Starship copies the keyword)
			cards.Add("GDB_109", new CardDef(new Power { NoScript = true }));
			cards.Add("GDB_109e", new CardDef(new Power { NoScript = true }));
			// [GDB_121] Foreboding Flame (2) — Battlecry: Demons that didn't start in your deck cost (1) less this game.
			cards.Add("GDB_121", new CardDef(new Power { PowerTask = new AddEnchantmentTask("GDB_121e", EntityType.CONTROLLER) }));
			cards.Add("GDB_121e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("GDB_121", "discount", 1)))
				{
					Condition = new SelfCondition(p => p is Minion m && m.IsRace(Race.DEMON) && Generic.DidntStartInDeck(p))
				}
			}));
			// [GDB_122] Infernal Stratagem (3) — Give a minion +3/+3. If it's a Demon, your next one costs (2) less.
			cards.Add("GDB_122", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Give(g, s, m, "GDB_122e");
					if (m.IsRace(Race.DEMON)) Generic.AddEnchantmentBlock(g, Cards.FromId("GDB_122e2"), (IPlayable)s, c, 0, 0, 0);
				})
			}));
			cards.Add("GDB_122e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("GDB_122", "stats", 3))) }));
			cards.Add("GDB_122e2", NextCardCost(Effects.ReduceCost(V("GDB_122", "discount", 2)), new SelfCondition(p => p is Minion m && m.IsRace(Race.DEMON)), "GBL_002e"));
			// [GDB_123] Abduction Ray (1) — Get a random Demon. Reduce its Cost by (2). Repeatable this turn.
			cards.Add("GDB_123", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.DEMON)) is Card demon) || c.HandZone.IsFull) return;
					IPlayable got = Entity.FromCard(c, demon, new EntityData { [GameTag.CREATOR] = s.Id });
					if (Generic.AddHandPhase.Invoke(c, got)) Give(g, s, got, "GBL_002e");
				})
			}));
			// [GDB_124] Bad Omen (6) — In 2 turns, summon two 6/6 Demons with Taunt. If you're building a Starship, summon them now.
			// (the waiting version GDB_124t sits in the hero zone like an Objective; the card glows while building — live)
			ISimpleTask overfiends = new CustomTask((g, c, s, t, stack) =>
			{
				for (int i = 0; i < V("GDB_124", "demons", 2); i++) Summon(g, c, Cards.FromId("GDB_124t2"), s);
			});
			cards.Add("GDB_124", new CardDef(new Power
			{
				GlowWhile = p => Generic.IsBuildingStarship(p.Controller),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (Generic.IsBuildingStarship(c)) { overfiends.Process(g, c, s, t, stack); return; }
					Generic.PutSecretIntoPlay(c, (Spell)Entity.FromCard(c, Cards.FromId("GDB_124t"), new EntityData { [GameTag.CREATOR] = s.Id }));
				})
			}));
			cards.Add("GDB_124t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Spell omen) || omen.Zone?.Type != Zone.SECRET) return;
						omen.QuestProgress++;
						if (omen.QuestProgress < Math.Max(1, omen.QuestTotalProgress)) return;
						overfiends.Process(g, c, s, t, stack);
						omen.ActivatedTrigger?.Remove();
						if (omen.Zone?.Type == Zone.SECRET) c.GraveyardZone.Add(c.SecretZone.Remove(omen));
					})
				}
			}));
			// [GDB_125] Healthstone (0) — Tradeable. Restore all damage your hero has taken this turn. (@)
			cards.Add("GDB_125", new CardDef(new Power
			{
				HandCounter = p => p.Controller?.Hero?.DamageTakenThisTurn ?? 0,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int taken = c.Hero.DamageTakenThisTurn;
					if (taken > 0) c.Hero.TakeHeal((IPlayable)s, taken);
				})
			}));
			// [GDB_126] Black Hole (8) — Destroy all minions except Demons. (not Locations or Permanent minions: live, the waiting
			// Starship survived)
			cards.Add("GDB_126", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => !m.IsRace(Race.DEMON) && !m.IsDead).ToList())
						Generic.Destroy(m, s);
				})
			}));
			// [GDB_127] K'ara, the Dark Star (3) — Spellburst: Steal 2 Health from a random enemy. (Shadow spells don't remove this.)
			cards.Add("GDB_127", new CardDef(new Power
			{
				Trigger = SpellburstKeptBy(SHADOW, new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed).ToList();
					if (enemies.Count > 0) StealHealth(g, (IPlayable)s, enemies[g.Random.Next(enemies.Count)], "GDB_127eb", "GDB_127e2b");
				}))
			}));
			cards.Add("GDB_127eb", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(-V("GDB_127", "health", 2))) }));
			cards.Add("GDB_127e2b", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("GDB_127", "health", 2))) }));
			// [GDB_128] Archimonde (6) — Battlecry: Summon every Demon you played this game that didn't start in your deck.
			// (Ra-den's way: play order, repeats too, until the board is full)
			cards.Add("GDB_128", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (PlayHistoryEntry h in c.PlayHistory.ToList())
					{
						if (c.BoardZone.IsFull) break;
						if (h.SourceCard?.Type != CardType.MINION || !h.SourceCard.IsRace(Race.DEMON) || h.SourceId == s.Id ||
						    !TitansC3DidntStartInDeck(g, h.SourceId)) continue;
						Summon(g, c, h.SourceCard, s);
					}
				})
			}));
			// [SC_019] Ultralisk Cavern (3, Location) — Deal 1 damage to all enemies. Deathrattle: Summon an 8/8 Ultralisk with Rush.
			cards.Add("SC_019", new CardDef(new Power
			{
				PowerTask = new DamageTask(V("SC_019", "damage", 1), EntityType.ENEMIES),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("SC_006"), s, ((Minion)s).LastBoardPosition))
			}));
			// [SC_020] Consume (1) — Remove 1 Durability from a friendly location to restore #8 Health to your hero.
			// (client REQ_LOCATION_TARGET: Card.TargetingRequirements; a broken Location dies with its Deathrattle)
			cards.Add("SC_020", new CardDef(new Dictionary<PlayReq, int>
			{
				{ PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayErrors.REQ_LOCATION_TARGET, 0 }
			}, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Location location && location.Zone?.Type == Zone.PLAY) location.Damage += V("SC_020", "durability", 1);
					new HealTask(V("SC_020", "heal", 8), EntityType.HERO).Process(g, c, s, null);
				})
			}));
			// [SC_023] Spine Crawler (2) — Taunt. Can't attack. Has +3 Attack if you control a location.
			cards.Add("SC_023", new CardDef(new Power
			{
				Aura = new Aura(AuraType.SELF, Effects.Attack_N(V("SC_023", "attack", 3)))
				{
					Condition = new SelfCondition(p => p.Controller.BoardZone.GetSpan().ToArray().Any(m => m is Location)), Restless = true
				}
			}));

			// ---------------------------------------------------------------- Warrior
			// [GDB_226] Hostile Invader (5) — Battlecry, Spellburst, and Deathrattle: Deal 2 damage to all other minions.
			ISimpleTask invade = new DamageTask(V("GDB_226", "damage", 2), EntityType.ALLMINIONS_NOSOURCE);
			var invaderBurst = Spellburst(invade);
			invaderBurst.EffectIndex = 2; // (0 Battlecry, 1 Deathrattle — live; the Spellburst's index unrecorded)
			cards.Add("GDB_226", new CardDef(new Power { PowerTask = invade, DeathrattleTask = invade, DeathrattleEffectIndex = 1, Trigger = invaderBurst }));
			// [GDB_227] Jettison (2) — Discover a spell. Spend 2 Armor to Discover another.
			cards.Add("GDB_227", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL),
					ComplexTask.If((g, c, s) => c.Hero.Armor >= V("GDB_227", "armor", 2), ComplexTask.Create(
						new CustomTask((g, c, s, t, stack) => Generic.PayArmor(c, s, V("GDB_227", "armor", 2))),
						DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL))))
			}));
			// [GDB_228] Captain's Log (4) — Draw 2 cards. Costs (1) less for each Draenei you control.
			cards.Add("GDB_228", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.BoardZone.GetAll().Count(m => m.IsRace(DRAENEI) && !m.IsDead)),
				PowerTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("GDB_228", "draw", 2); i++) Generic.Draw(c); })
			}));
			// [GDB_229] Expedition Sergeant (3) — Battlecry: The next Draenei you play immediately attacks a random enemy.
			cards.Add("GDB_229", new CardDef(new Power { PowerTask = new AddEnchantmentTask("GDB_229e", EntityType.CONTROLLER) }));
			cards.Add("GDB_229e", DraeneiAfterPlayBuff((g, e, played) =>
			{
				if (played.Zone?.Type != Zone.PLAY || played.IsDead) return;
				var enemies = AttackableEnemies(played.Controller);
				if (enemies.Count > 0) EffectAttack(g, played, enemies[g.Random.Next(enemies.Count)]);
			}));
			// [GDB_230] Stalwart Avenger (4) — Immune while attacking. At the end of EACH turn, swap this minion's Attack and Health.
			cards.Add("GDB_230", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { EitherTurn = true, SingleTask = new SwapAttackHealthTask(EntityType.SOURCE, "GDB_230e") }
			}));
			cards.Add("GDB_230e", new CardDef(new Power { NoScript = true }));
			// [GDB_231] Crystalline Greatmace (2, weapon) — After your hero attacks, give all Draenei in your hand +2 Attack.
			cards.Add("GDB_231", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
						GiveAll(g, s, c.HandZone.GetAll().Where(p => p is Minion m && m.IsRace(DRAENEI)).ToList(), "GDB_231e"))
				}
			}));
			cards.Add("GDB_231e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("GDB_231", "attack", 2))) }));
			// [GDB_232] Unyielding Vindicator (4) — Battlecry: The next Draenei you play gives your hero its Attack for that turn.
			cards.Add("GDB_232", new CardDef(new Power { PowerTask = new AddEnchantmentTask("GDB_232e", EntityType.CONTROLLER) }));
			cards.Add("GDB_232e", DraeneiAfterPlayBuff((g, e, played) =>
			{
				if (played.AttackDamage > 0) Give(g, e, played.Controller.Hero, "GDB_232e2", played.AttackDamage);
			}));
			cards.Add("GDB_232e2", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0)) { UseScriptTag = true, IsOneTurnEffect = true } }));
			// [GDB_233] Dwarf Planet (10) — Fill your board with random 2-Cost minions that attack random enemies.
			// (Nagrand Slam's way: all summoned, then each attacks)
			cards.Add("GDB_233", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var summoned = new List<Minion>();
					while (!c.BoardZone.IsFull && RandomMinionOfCost(g, V("GDB_233", "cost", 2)) is Card card)
					{
						var m = (Minion)Entity.FromCard(c, card);
						Generic.SummonBlock.Invoke(g, m, -1, s);
						if (m.Zone?.Type != Zone.PLAY) break;
						summoned.Add(m);
					}
					foreach (Minion m in summoned)
					{
						var enemies = AttackableEnemies(c);
						if (enemies.Count > 0 && !m.IsDead && !m.ToBeDestroyed) EffectAttack(g, m, enemies[g.Random.Next(enemies.Count)]);
					}
				})
			}));
			// [GDB_234] Spore Empress Moldara (6) — Start of Game: Shuffle 7 Replicating Spores into your deck. Battlecry: Cast one.
			cards.Add("GDB_234", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.GAME_START)
				{
					TriggerActivation = TriggerActivation.DECK,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is IPlayable moldara) || !Generic.StartOfGameHolds(moldara)) return;
						for (int i = 0; i < V("GDB_234", "spores", 7); i++)
							Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId("GDB_234t"), new EntityData { [GameTag.CREATOR] = s.Id }));
					})
				},
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.PlayByEffect(g, c, s, Cards.FromId("GDB_234t")))
			}));
			// [GDB_234t] Replicating Spore (5) — Summon a random 5-Cost minion. Your future Replicating Spores summon it as well.
			cards.Add("GDB_234t", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => ReplicatingSpore(g, c, s)) }));
			// [GDB_235] Exarch Akama (5) — After this attacks, all other friendly minions can attack again (except Exarch Akama).
			cards.Add("GDB_235", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me)) return;
						var others = c.BoardZone.GetAll().Where(m => m != me && m.Card.Id != me.Card.Id && !m.IsDead).ToList();
						Generic.NamedFx(me, "BuffImpact", others.Select(m => m.Id).ToList(), () => { foreach (Minion m in others) Generic.GrantExtraAttack(m); });
					})
				}
			}));
			// [SC_406] Yamato Cannon (4) — Starship Piece. Battlecry: Destroy a random enemy minion. Also triggers on launch.
			cards.Add("SC_406", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => YamatoCannon(g, c, s)) }));
			cards.Add("SC_406e", new CardDef(new Power { LaunchTask = new CustomTask((g, c, s, t, stack) => YamatoCannon(g, c, s)) }));
			// [SC_411] Concussive Shells (1) — Deal $2 damage and gain 2 Armor. Your next Starship launch costs (2) less.
			cards.Add("SC_411", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("SC_411", "damage", 2), EntityType.TARGET, true),
					new ArmorTask(V("SC_411", "armor", 2)),
					new CustomTask((g, c, s, t, stack) => Generic.DiscountStarshipLaunch(c, V("SC_411", "discount", 2))))
			}));
			// [SC_414] Thor (8) — Battlecry: Deal 5 damage. (Transforms if you launched a Starship this game.)
			cards.Add("SC_414", new CardDef(BattlecryAny, new Power
			{
				UpgradeAfterLaunch = "SC_414t",
				PowerTask = new DamageTask(V("SC_414", "damage", 5), EntityType.TARGET)
			}));
			// [SC_414t] Thor, Explosive Payload (8) — Battlecry: Deal 5 damage. Repeat at a random enemy for each Starship you've
			// launched this game. (@) (Jim Raynor's relaunches don't count — wiki Thor)
			cards.Add("SC_414t", new CardDef(BattlecryAny, new Power
			{
				HandCounter = p => p.Controller?[HsTag.STARSHIPS_LAUNCHED] ?? 0,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int damage = V("SC_414", "damage", 5);
					if (t is ICharacter chosen) Generic.DamageCharFunc((IPlayable)s, chosen, damage, false);
					for (int i = 0; i < c[HsTag.STARSHIPS_LAUNCHED]; i++)
					{
						var enemies = Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed).ToList();
						if (enemies.Count == 0) break;
						Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], damage, false);
					}
				})
			}));

			// ---------------------------------------------------------------- Shaman
			// [GDB_434] Bolide Behemoth (4) — Battlecry: Your Asteroids deal 1 more damage this game. Spellburst: Shuffle 3 of them
			// into your deck.
			cards.Add("GDB_434", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => c[HsTag.ASTEROID_BONUS] = c[HsTag.ASTEROID_BONUS] + V("GDB_434", "bonus", 1)),
				Trigger = Spellburst(new CustomTask((g, c, s, t, stack) => ShuffleAsteroids(c, s, V("GDB_434", "asteroids", 3))))
			}));
			// [GDB_443] Cosmonaut (7) — Battlecry: Discover a spell from your deck. Reduce its Cost by (5).
			cards.Add("GDB_443", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					Generic.DiscoverFromDeck(c, s, Generic.PickFromDeck(c, p => p is Spell), (g2, c2, s2, spell) =>
					{
						if (spell.Zone?.Type == Zone.DECK) Generic.Draw(c2, spell);
						if (spell.Zone?.Type == Zone.HAND) Give(g2, s2, spell, GenericCostLess(V("GDB_443", "discount", 5)) ?? "GBL_011e");
					}))
			}));
			// [GDB_444] Planetary Navigator (2) — Battlecry: The next Draenei you play costs (2) less, but has Overload: (2).
			cards.Add("GDB_444", new CardDef(new Power { PowerTask = new AddEnchantmentTask("GDB_444e", EntityType.CONTROLLER) }));
			cards.Add("GDB_444e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("GDB_444", "discount", 2))) { Condition = new SelfCondition(p => p is Minion m && m.IsRace(DRAENEI)) },
				Trigger = new Trigger(TriggerType.PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p is Minion m && m.IsRace(DRAENEI)),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e)) return;
						int owed = Generic.AddOverload(c, V("GDB_444", "overload", 2));
						if (owed > 0 && t is IPlayable played) g.RaiseEvent(() => g.TriggerManager.OnOverloadTrigger(played, owed));
						e.Remove();
					})
				}
			}));
			// [GDB_445] Meteor Storm (6) — Deal $5 damage to all minions. Shuffle 5 Asteroids into your deck.
			cards.Add("GDB_445", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("GDB_445", "damage", 5), EntityType.ALLMINIONS, true),
					new CustomTask((g, c, s, t, stack) => ShuffleAsteroids(c, s, V("GDB_445", "asteroids", 5))))
			}));
			// [GDB_447] Farseer Nobundo (5) — Deathrattle: Open the Galaxy's Lens. It absorbs the power of the next spell you cast.
			// (live s1002 35674: the Lens in Nobundo's slot, LITERALLY_UNPLAYABLE until a spell)
			cards.Add("GDB_447", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.BoardZone.IsFull || !(s is Minion nobundo)) return;
					var lens = (Minion)Entity.FromCard(c, Cards.FromId("GDB_136t"), new EntityData { [GameTag.CREATOR] = s.Id });
					Generic.SummonBlock.Invoke(g, lens, nobundo.LastBoardPosition, s);
					if (lens.Zone?.Type == Zone.PLAY) lens[HsTag.LITERALLY_UNPLAYABLE] = 1;
				})
			}));
			// [GDB_136t] The Galaxy's Lens (5, Location) — Spellburst: Absorb the spell's power! / Cast {0}.
			// (live s1002 36953: the spell's id in TAG_SCRIPT_DATA_ENT_1 / NUM_1 (the client's "Cast {0}"), its dbf in
			// ADDITIONAL_PLAY_REQS_1 (its play requirements: Location.Absorbed), LITERALLY_UNPLAYABLE 0, SPELLBURST 0 last. Used: a new
			// copy of the spell at the chosen target — wiki Farseer Nobundo "a target can be chosen by the location")
			cards.Add("GDB_136t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					RemoveAfterTriggered = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion lens) || !((t ?? g.CurrentEventData?.EventSource as IPlayable) is Spell spell)) return;
						lens[GameTag.TAG_SCRIPT_DATA_ENT_1] = spell.Id;
						lens[GameTag.TAG_SCRIPT_DATA_NUM_1] = spell.Id;
						lens[GameTag.ADDITIONAL_PLAY_REQS_1] = spell.Card.AssetId;
						lens[HsTag.LITERALLY_UNPLAYABLE] = 0;
						if (lens[HsTag.SPELLBURST] > 0) lens[HsTag.SPELLBURST] = 0;
					})
				},
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (s is Location lens && lens.Absorbed is Card spell)
						new CastRandomSpellTask(preferTarget: true) { Spell = spell }.Process(g, c, s, t);
				})
			}));
			// [GDB_448] Murmur (9) — Your Battlecry minions cost (1), but immediately die after being played.
			cards.Add("GDB_448", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.SetCost(V("GDB_448", "cost", 1)))
				{
					Condition = new SelfCondition(p => p is Minion && p[GameTag.BATTLECRY] > 0), ShowAs = "GDB_448e"
				},
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p is Minion && p[GameTag.BATTLECRY] > 0),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if ((t ?? g.CurrentEventData?.EventSource as IPlayable) is Minion played && played.Zone?.Type == Zone.PLAY && !played.IsDead)
							Generic.Destroy(played, s);
					})
				}
			}));
			// [GDB_451] Triangulate (2) — Discover a different spell from your deck. Shuffle 3 copies of it into your deck.
			cards.Add("GDB_451", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					string self = ((IPlayable)s).Card.Id;
					Generic.DiscoverFromDeck(c, s, Generic.PickFromDeck(c, p => p is Spell && p.Card.Id != self), (g2, c2, s2, spell) =>
					{
						if (spell.Zone?.Type == Zone.DECK) Generic.Draw(c2, spell);
						for (int i = 0; i < V("GDB_451", "copies", 3); i++)
							Generic.ShuffleIntoDeck.Invoke(c2, s2, Entity.FromCard(c2, spell.Card, new EntityData { [GameTag.CREATOR] = s2.Id }));
					});
				})
			}));
			// [GDB_479] Nebula (9) — Discover two 8-Cost minions to summon with Taunt and Elusive.
			cards.Add("GDB_479", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("GDB_479", "minions", 2); i++) NebulaDiscover(g, c, s);
				})
			}));
			// [GDB_864] First Contact (1) — Summon two random 1-Cost minions. Overload: (1)
			cards.Add("GDB_864", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("GDB_864", "minions", 2); i++) Summon(g, c, RandomMinionOfCost(g, V("GDB_864", "cost", 1)), s);
				})
			}));
			// [GDB_901] Ultraviolet Breaker (3) — Battlecry: Deal 3 damage to an enemy minion. Shuffle 3 Asteroids into your deck.
			// (no target: the Battlecry doesn't happen — rule book Battlecry "If no valid target exists, the Battlecry will not take effect")
			cards.Add("GDB_901", new CardDef(BattlecryEnemyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion victim)) return;
					Generic.DamageCharFunc((IPlayable)s, victim, V("GDB_901", "damage", 3), false);
					ShuffleAsteroids(c, s, V("GDB_901", "asteroids", 3));
				})
			}));
			// [SC_407] Lock On (1) — Set a minion's Health to 1. Your next Starship launch costs (2) less.
			cards.Add("SC_407", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(
					new AddEnchantmentTask("SC_407e", EntityType.TARGET),
					new CustomTask((g, c, s, t, stack) => Generic.DiscountStarshipLaunch(c, V("SC_407", "discount", 2))))
			}));
			cards.Add("SC_407e", new CardDef(new Power { Enchant = new Enchant(Effects.SetMaxHealth(V("SC_407", "health", 1))) }));
			// [SC_409] Missile Pod (2) — Starship Piece. Battlecry: Deal 1 damage to all enemies. Also triggers on launch.
			cards.Add("SC_409", new CardDef(new Power { PowerTask = new DamageTask(V("SC_409", "damage", 1), EntityType.ENEMIES) }));
			cards.Add("SC_409e", new CardDef(new Power { LaunchTask = new DamageTask(V("SC_409", "damage", 1), EntityType.ENEMIES) }));
			// [SC_413] Siege Tank (5) — Battlecry: Deal 10 damage to a random enemy minion. (Transforms if you launched a Starship.)
			cards.Add("SC_413", new CardDef(new Power
			{
				UpgradeAfterLaunch = "SC_413t",
				PowerTask = new CustomTask((g, c, s, t, stack) => SiegeTank(g, c, (IPlayable)s, false))
			}));
			// [SC_413t] Siege Tank, Deployed (5) — Battlecry: Deal 10 damage to a random enemy minion. Excess damage hits the enemy hero.
			cards.Add("SC_413t", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => SiegeTank(g, c, (IPlayable)s, true)) }));

			// ---------------------------------------------------------------- The StarCraft Hero cards (CARDTYPE HERO, 7 Mana, 5 Armor)
			// [SC_400] Jim Raynor — Battlecry: Relaunch every Starship that you launched this game. (Generic.RelaunchStarship)
			cards.Add("SC_400", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion launched in Generic.LaunchedStarships(c).ToList())
						if (Generic.RelaunchStarship(c, launched, s) == null && c.BoardZone.IsFull) break;
				})
			}));
			// [SC_400p] Stimpack (2, hero power) — Summon a 2/2 Marine with Taunt. Give your Terran minions +2 Attack.
			cards.Add("SC_400p", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Summon(g, c, Cards.FromId("SC_403t"), s);
					GiveAll(g, s, c.BoardZone.GetAll().Where(m => IsTerran(m) && !m.IsDead).ToList(), "SC_400e");
				})
			}));
			cards.Add("SC_400e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("SC_400p", "attack", 2))) }));
			// [SC_004] Kerrigan, Queen of Blades — Battlecry: Summon two 2/5 Hive Queens. Deal 3 damage to all enemies.
			cards.Add("SC_004", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("SC_004", "queens", 2); i++) Summon(g, c, Cards.FromId("SC_003"), s); }),
					new DamageTask(V("SC_004", "damage", 3), EntityType.ENEMIES))
			}));
			// [SC_004hp] Ravage (2, hero power) — Deal $@ damage randomly split among all enemies. (Improved by Zerg minions you control!)
			// (@: its own 3 plus each Zerg minion you control)
			int ravage(IPlayable p) => p.Card[GameTag.TAG_SCRIPT_DATA_NUM_1] + (p.Controller?.BoardZone.GetAll().Count(m => IsZerg(m) && !m.IsDead) ?? 0);
			cards.Add("SC_004hp", new CardDef(new Power
			{
				HandCounter = ravage,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = ravage((IPlayable)s); i > 0; i--)
					{
						var enemies = Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed && x.Health > 0).ToList();
						if (enemies.Count == 0) break;
						Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], 1, false);
					}
				})
			}));
			// [SC_754] Artanis — Battlecry: Summon two 3/4 Zealots with Charge. Your Protoss minions cost (2) less this game.
			// (the client's Protoss discount SC_755e is "(1) less": given twice — doubt)
			cards.Add("SC_754", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("SC_754", "zealots", 2); i++) Summon(g, c, Cards.FromId("SC_751t"), s);
					for (int i = 0; i < V("SC_754", "discount", 2); i++) Generic.AddEnchantmentBlock(g, Cards.FromId("SC_755e"), (IPlayable)s, c, 0, 0, 0);
				})
			}));
			// [SC_754p] Twin Blades (2, hero power) — Give a friendly minion and your hero +1 Attack this turn and Divine Shield.
			cards.Add("SC_754p", new CardDef(TargetFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Give(g, s, m, "SC_754e", V("SC_754p", "attack", 1));
					m.HasDivineShield = true;
					Give(g, s, c.Hero, "SC_754e", V("SC_754p", "attack", 1));
					c.Hero[GameTag.DIVINE_SHIELD] = 1;
				})
			}));
			cards.Add("SC_754e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0)) { UseScriptTag = true, IsOneTurnEffect = true } }));
		}
	}
}
