// HsServer addition (not upstream SabberStone): The Great Dark Beyond — chunk 2: Druid, Hunter and Mage cards and their
// StarCraft cards (spec: research spec_chunk2; shared pieces: SpaceCardsGenHs.cs, Actions/Starship.cs). Every number goes through
// CardValues (V).
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

		/// <summary>
		/// Uluu, the Everdrifter: two random Choose One choices (wiki Uluu: from collectible cards legal in the format; never the
		/// self-transforming ones such as Druid of the Claw, nor Fatespinner; never two that both need a target). The options are
		/// new set-aside entities (CREATOR / PARENT_CARD = Uluu); the client's text builder reads their ids in TAG_SCRIPT_DATA_NUM_1/2.
		/// </summary>
		private static void UluuNewChoices(Game g, Controller c, IPlayable uluu)
		{
			var pool = Cards.FormatTypeCards(g.FormatType).Where(x => x.Collectible && x.ChooseOne && x.Id != uluu.Card.Id &&
					x.Text?.Contains("Choose One") == true && !(x.Text.Contains("Transform") || x.Text.Contains("transform")))
				.SelectMany(x => Cards.ChooseOneOptions(x) ?? new Card[0]).Where(x => x != null).GroupBy(x => x.Id).Select(grp => grp.First()).ToList();
			if (pool.Count < 2) return;
			static bool NeedsTarget(Card o) => o.PlayRequirements != null &&
				(o.PlayRequirements.ContainsKey(PlayReq.REQ_TARGET_TO_PLAY) || o.PlayRequirements.ContainsKey(PlayReq.REQ_TARGET_IF_AVAILABLE));
			Card a = pool[g.Random.Next(pool.Count)];
			var rest = pool.Where(x => x != a && !(NeedsTarget(a) && NeedsTarget(x))).ToList();
			if (rest.Count == 0) return;
			Card b = rest[g.Random.Next(rest.Count)];
			foreach (IPlayable old in uluu.ChooseOnePlayables ?? new IPlayable[0])
				if (old != null && old.Zone != null) { old.Zone.Remove(old); old[GameTag.PARENT_CARD] = 0; old[GameTag.ZONE] = (int)Zone.REMOVEDFROMGAME; }
			IPlayable ea = Entity.FromCard(c, a, new EntityData { [GameTag.CREATOR] = uluu.Id, [GameTag.PARENT_CARD] = uluu.Id }, c.SetasideZone);
			IPlayable eb = Entity.FromCard(c, b, new EntityData { [GameTag.CREATOR] = uluu.Id, [GameTag.PARENT_CARD] = uluu.Id }, c.SetasideZone);
			uluu.ChooseOnePlayables = new[] { ea, eb };
			uluu[GameTag.TAG_SCRIPT_DATA_NUM_1] = ea.Id;
			uluu[GameTag.TAG_SCRIPT_DATA_NUM_2] = eb.Id;
		}

		/// <summary>Exarch Hataaru's effect: Discover a spell (−1), marked "if you play it this turn"; played this turn, again.</summary>
		private static ISimpleTask HataaruDiscover => DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL, afterPick: new CustomTask((g, c, s, t, stack) =>
		{
			if (t == null || t.Zone?.Type != Zone.HAND) return;
			IPlayable hataaru = s is Enchantment e ? e.Creator ?? (IPlayable)c.Hero : (IPlayable)s;
			Give(g, hataaru, t, GenericCostLess(V("GDB_136", "discount", 1)));
			Generic.MarkPlayThisTurn(t, hataaru);
			Generic.AddEnchantmentBlock(g, Cards.FromId("GDB_136e"), hataaru, c, t.Id, 0, 0);
		}));

		/// <summary>Pocket Dimension: Discover a spell, again until a choice shows a spell already offered by this play (wiki: "until
		/// a spell appears as a choice for the second time"; live S1003 76648: the repeat is shown and picked, then it ends). The
		/// offered ids live on the spell (HsTag.POCKET_SEEN_BASE + i).</summary>
		private static void PocketDimension(Game g, Controller c, IEntity source)
		{
			var pool = Cards.FormatTypeCards(g.FormatType).Where(x => x.Type == CardType.SPELL && (x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL)) && x.Collectible).ToList();
			Card[] offer = DiscoverFromTask.PickDistinct(g, pool, 3).ToArray();
			if (offer.Length == 0) return;
			var seen = new List<int>();
			for (int i = 0; i < 30 && source[HsTag.POCKET_SEEN_BASE + i] > 0; i++) seen.Add(source[HsTag.POCKET_SEEN_BASE + i]);
			bool repeat = offer.Any(x => seen.Contains(x.AssetId));
			foreach (Card x in offer)
				if (!seen.Contains(x.AssetId) && seen.Count < 30) { source[HsTag.POCKET_SEEN_BASE + seen.Count] = x.AssetId; seen.Add(x.AssetId); }
			Generic.CreateChoiceCards.Invoke(c, source, null, ChoiceType.GENERAL, ChoiceAction.HAND, offer, new CustomTask((g2, c2, s2, pick, st) =>
			{
				if (!repeat && !c2.HandZone.IsFull) PocketDimension(g2, c2, s2);
			}));
		}

		private static void SpaceChunk2(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Starship pieces
			// [GDB_103] Sha'tari Cloakfield (2) — Elusive. Your first spell each turn costs (1) less. Starship Piece.
			Aura cloakfield() => new Aura(AuraType.HAND, Effects.ReduceCost(V("GDB_103", "discount", 1)))
			{
				Condition = new SelfCondition(p => p is Spell && !p.Controller.PlayHistory.Any(h => h.Turn == p.Game.Turn && h.SourceCard?.Type == CardType.SPELL)),
				Restless = true
			};
			cards.Add("GDB_103", new CardDef(new Power { Aura = cloakfield() }));
			cards.Add("GDB_103e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.CANT_BE_TARGETED_BY_SPELLS, EffectOperator.SET, 1), new Effect(GameTag.CANT_BE_TARGETED_BY_HERO_POWERS, EffectOperator.SET, 1)),
				Aura = cloakfield()
			}));
			// [GDB_107] Specimen Claw (3) — After your opponent plays a minion, attack it. Starship Piece. (Swamp King Dred's trigger;
			// live S1003 36608: effect index 1.)
			Trigger claw(TriggerSource source) => new Trigger(TriggerType.AFTER_PLAY_MINION)
			{
				TriggerSource = TriggerSource.ENEMY,
				EffectIndex = 1,
				SingleTask = new CustomTask((g, c, s, t, stack) =>
				{
					Minion me = Generic.StarshipOf(s);
					if (me == null || me.IsDead || me.ToBeDestroyed || me.Untouchable || me.Zone?.Type != Zone.PLAY) return;
					if ((t ?? g.CurrentEventData?.EventSource as IPlayable) is Minion played && !played.IsDead && !played.Untouchable && played.Zone?.Type == Zone.PLAY)
						EffectAttack(g, me, played);
				})
			};
			cards.Add("GDB_107", new CardDef(new Power { Trigger = claw(TriggerSource.SELF) }));
			cards.Add("GDB_107e", new CardDef(new Power { Trigger = claw(TriggerSource.ENCHANTMENT_TARGET) }));
			// [GDB_108] Starlight Reactor (3) — After you cast an Arcane spell, recast it (targets chosen randomly). Starship Piece.
			// (Live VFX 166000: effect index 2; the recast is an effect cast — no loop.)
			Trigger reactor() => new Trigger(TriggerType.AFTER_CAST)
			{
				TriggerSource = TriggerSource.FRIENDLY,
				EffectIndex = 2,
				Condition = new SelfCondition(p => p is Spell && p[SPELL_SCHOOL] == ARCANE),
				SingleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if ((t ?? g.CurrentEventData?.EventSource as IPlayable) is Spell cast)
						new CastRandomSpellTask().Process(g, c, Generic.StarshipOf(s) ?? (IEntity)s, Entity.FromCard(c, cast.Card));
				})
			};
			cards.Add("GDB_108", new CardDef(new Power { Trigger = reactor() }));
			cards.Add("GDB_108e", new CardDef(new Power { Trigger = reactor() }));
			// [GDB_111] Biopod (2) — Deathrattle: Deal damage equal to this minion's Attack to a random enemy. Starship Piece.
			ISimpleTask biopod = new CustomTask((g, c, s, t, stack) =>
			{
				Minion me = Generic.StarshipOf(s);
				var enemies = Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed).ToList();
				if (me != null && enemies.Count > 0) Generic.DamageCharFunc(me, enemies[g.Random.Next(enemies.Count)], Math.Max(0, me.AttackDamage), false);
			});
			cards.Add("GDB_111", new CardDef(new Power { DeathrattleTask = biopod, DeathrattleEffectIndex = 2 }));
			cards.Add("GDB_111e", new CardDef(new Power { DeathrattleTask = biopod, DeathrattleEffectIndex = 2 }));

			// ---------------------------------------------------------------- Mage
			// [GDB_133] Pocket Dimension (4) — Discover a spell. Repeat until you see one for the second time.
			cards.Add("GDB_133", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => PocketDimension(g, c, s)) }));
			// [GDB_134] Arkwing Pilot (6) — At the end of your turn, deal 3 damage to a random enemy. Spellburst: Summon an Arkwing Pilot.
			var pilotBurst = Spellburst(new CustomTask((g, c, s, t, stack) =>
			{
				if (s is Minion me) Summon(g, c, me.Card, s, me.Zone?.Type == Zone.PLAY ? me.ZonePosition + 1 : -1);
			}));
			pilotBurst.EffectIndex = 1;
			cards.Add("GDB_134", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.TURN_END) { SingleTask = new CustomTask((g, c, s, t, stack) => IslandRandomHits(g, c, s, 1, V("GDB_134", "damage", 3), false)) },
					pilotBurst)
			}));
			// [GDB_135] Ingenious Artificer (4) — Battlecry: The next Draenei you play refreshes Mana Crystals equal to its Attack.
			cards.Add("GDB_135", new CardDef(new Power { PowerTask = new AddEnchantmentTask("GDB_135e", EntityType.CONTROLLER) }));
			cards.Add("GDB_135e", DraeneiFutureBuff((g, e, played) => RefreshMana(played.Controller, Math.Max(0, played.AttackDamage))));
			// [GDB_136] Exarch Hataaru (5) — Battlecry: Discover a spell and reduce its Cost by (1). If you play it this turn, repeat
			// this effect. (Live S0929: the TTN_317 shape; the repeats chain.)
			cards.Add("GDB_136", new CardDef(new Power { PowerTask = HataaruDiscover }));
			cards.Add("GDB_136e", new CardDef(new Power
			{
				Enchant = new Enchant(new IEffect[0]) { IsOneTurnEffect = true },
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p.Controller.AppliedEnchantments?.Any(e => e.Card.Id == "GDB_136e" && e[GameTag.TAG_SCRIPT_DATA_NUM_1] == p.Id) == true),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable played = t ?? g.CurrentEventData?.EventSource as IPlayable;
						if (!(s is Enchantment mark) || played == null || mark[GameTag.TAG_SCRIPT_DATA_NUM_1] != played.Id) return;
						mark[GameTag.TAG_SCRIPT_DATA_NUM_1] = 0;
						HataaruDiscover.Process(g, c, mark, null);
					})
				}
			}));
			// [GDB_301] Supernova (8) — Fill your hand with random Fire spells. They cost (1).
			cards.Add("GDB_301", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					while (!c.HandZone.IsFull)
					{
						if (!(IslandToHand(c, s, RandomCard(g, x => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == FIRE)) is IPlayable p)) break;
						Give(g, s, p, "GBL_001e");
					}
				})
			}));
			// [GDB_302] Blazing Accretion (3) — Battlecry: Destroy the top 3 cards of your deck. Any Fire spells or Elementals are drawn
			// instead.
			cards.Add("GDB_302", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("GDB_302", "cards", 3) && c.DeckZone.Count > 0; i++)
					{
						IPlayable top = c.DeckZone.TopCard;
						if (top is Spell && top[SPELL_SCHOOL] == FIRE || top is Minion m && m.IsRace(Race.ELEMENTAL)) Generic.Draw(c, top);
						else Generic.Destroy(top, s);
					}
				})
			}));
			// [GDB_303] Blasteroid (3) — Battlecry: Shuffle 5 random Fire spells into your deck. They cost (2) less.
			cards.Add("GDB_303", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("GDB_303", "spells", 5); i++)
					{
						if (!(RandomCard(g, x => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == FIRE) is Card fire) || c.DeckZone.IsFull) break;
						IPlayable p = Entity.FromCard(c, fire, new EntityData { [GameTag.CREATOR] = s.Id });
						Generic.ShuffleIntoDeck.Invoke(c, s, p);
						Give(g, s, p, GenericCostLess(V("GDB_303", "discount", 2)));
					}
				})
			}));
			// [GDB_304] Saruun (6) — Battlecry: Give all Elementals in your deck Fire Spell Damage +1.
			cards.Add("GDB_304", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.DeckZone.GetAll().Where(p => p is Minion m && m.IsRace(Race.ELEMENTAL)).ToList(), "GDB_304e"))
			}));
			cards.Add("GDB_304e", new CardDef(new Power { Enchant = new Enchant(new Effect(Controller.SchoolSpellPowerTag(FIRE), EffectOperator.ADD, V("GDB_304", "spell damage", 1))) }));
			// [GDB_305] Solar Flare (5) — Deal $2 damage to all enemies. Costs (1) less for each Elemental you control.
			cards.Add("GDB_305", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.BoardZone.GetAll().Count(m => m.IsRace(Race.ELEMENTAL))),
				PowerTask = new CustomTask((g, c, s, t, stack) => DamageAll(s, Enemies(c), V("GDB_305", "damage", 2), true))
			}));
			// [GDB_456] Spontaneous Combustion (2) — Deal $4 damage to a random enemy. If you played an Elemental last turn, choose the target.
			cards.Add("GDB_456", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABE_AND_ELEMENTAL_PLAYED_LAST_TURN, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter chosen) { Generic.DamageCharFunc((IPlayable)s, chosen, V("GDB_456", "damage", 4), true); return; }
					var enemies = Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed).ToList();
					if (enemies.Count > 0) Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], V("GDB_456", "damage", 4), true);
				})
			}));
			// [SC_758] Colossus (12) — Battlecry: Deal @ damage to all enemies, twice. (Improved by Protoss spells you cast this game!)
			int colossus(IPlayable p) => p.Card[GameTag.TAG_SCRIPT_DATA_NUM_1] + p.Controller.PlayHistory.Count(h => h.SourceCard?.Type == CardType.SPELL && IsProtossCard(h.SourceCard));
			cards.Add("SC_758", new CardDef(new Power
			{
				HandCounter = colossus,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = colossus((IPlayable)s);
					for (int i = 0; i < 2; i++) DamageAll(s, Enemies(c), n, false);
				})
			}));
			// [SC_759] Shield Battery (2) — Gain 6 Armor. Your next Protoss spell costs (2) less.
			cards.Add("SC_759", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new ArmorTask(V("SC_759", "armor", 6)), new AddEnchantmentTask("SC_759e", EntityType.CONTROLLER))
			}));
			cards.Add("SC_759e", NextCardCost(Effects.ReduceCost(V("SC_759", "discount", 2)), new SelfCondition(p => p is Spell && IsProtoss(p)), null));
			// [SC_760] Resonance Coil (3) — Deal $5 damage to a minion. Get a random Protoss spell.
			cards.Add("SC_760", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("SC_760", "damage", 5), EntityType.TARGET, true),
					new CustomTask((g, c, s, t, stack) => AddToHand(c, RandomCard(g, x => x.Type == CardType.SPELL && IsProtossCard(x)))))
			}));

			// ---------------------------------------------------------------- Hunter
			// [GDB_237] Alien Encounters (5) — Summon two 2/5 Beasts with Taunt. Costs (1) less for each card you Discovered this game.
			cards.Add("GDB_237", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => V("GDB_237", "discount", 1) * p.Controller[HsTag.DISCOVERED_THIS_GAME]),
				PowerTask = new SummonTask("GDB_237t", V("GDB_237", "beasts", 2), SummonSide.SPELL)
			}));
			// [GDB_840] Extraterrestrial Egg (2) — Deathrattle: Summon a 3/5 Beast that attacks the lowest Health enemy. (Live S1003
			// 46034: Deathrattle index −1.)
			cards.Add("GDB_840", new CardDef(new Power
			{
				DeathrattleEffectIndex = -1,
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion egg) || !(IslandSummon(g, c, Cards.FromId("GDB_840t"), s, egg.LastBoardPosition) is Minion burster)) return;
					if (LowestHealthEnemy(g, c) is ICharacter low) EffectAttack(g, burster, low);
				})
			}));
			// [GDB_841] Rangari Scout (1) — After you Discover a card, get a copy of it. (Wiki: every Discover.)
			cards.Add("GDB_841", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_DISCOVER)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if ((t ?? g.CurrentEventData?.EventSource as IPlayable) is IPlayable picked && !c.HandZone.IsFull) Generic.Copy(c, s, picked, Zone.HAND);
					})
				}
			}));
			// [GDB_842] Gorm the Worldeater (3) — Dormant for 5 turns. At the end of your turn, destroy the minion to the right of this
			// to awaken 2 turns sooner. (Live NIGHT 57286: the eater GDB_842e3 attached while Dormant, removed when it awakens.)
			cards.Add("GDB_842", new CardDef(new Power
			{
				DormantTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion gorm) Generic.AddEnchantmentBlock(g, Cards.FromId("GDB_842e3"), gorm, gorm, 0, 0, 0); }),
				AwakenTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (s is Minion gorm) foreach (Enchantment e in gorm.AppliedEnchantments?.Where(x => x.Card.Id == "GDB_842e3").ToList() ?? new List<Enchantment>()) e.Remove();
				})
			}));
			cards.Add("GDB_842e3", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					WhileDormant = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e) || !(e.Target is Minion gorm) || gorm[GameTag.DORMANT] == 0 || gorm.Zone?.Type != Zone.PLAY) return;
						int right = gorm.ZonePosition + 1;
						if (right >= gorm.Controller.BoardZone.Count || !(gorm.Controller.BoardZone[right] is Minion victim) || victim.Untouchable || victim.ToBeDestroyed) return;
						Generic.Destroy(victim, gorm);
						g.DeathProcessingAndAuraUpdate();
						Generic.AwakenSooner(gorm, V("GDB_842", "sooner", 2));
					})
				}
			}));
			// [GDB_843] Parallax Cannon (3, weapon) — Has +2 Attack if you've Discovered this turn. Spellburst: Your hero is Immune this turn.
			cards.Add("GDB_843", new CardDef(new Power
			{
				Aura = new AdaptiveEffect(GameTag.ATK, EffectOperator.ADD, p => Generic.DiscoveredThisTurn(p.Controller) > 0 ? V("GDB_843", "attack", 2) : 0),
				Trigger = Spellburst(new CustomTask((g, c, s, t, stack) => Give(g, s, c.Hero, "GDB_843e2")))
			}));
			cards.Add("GDB_843e2", OneTurn(new Effect(GameTag.IMMUNE, EffectOperator.SET, 1)));
			// [GDB_844] Detailed Notes (2) — Discover a Beast that costs (5) or more. Reduce its Cost by (2).
			cards.Add("GDB_844", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.IsRace(Race.BEAST) && x.Cost >= V("GDB_844", "cost", 5),
					afterPick: new CustomTask((g, c, s, t, stack) => { if (t != null) Give(g, s, t, GenericCostLess(V("GDB_844", "discount", 2))); }))
			}));
			// [GDB_845] Laser Barrage (2) — Deal $3 damage to a minion. If you're building a Starship, also damage its neighbors.
			cards.Add("GDB_845", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					var next = Neighbors(m).Where(x => !x.Untouchable).ToList();
					bool building = Generic.IsBuildingStarship(c);
					Generic.DamageCharFunc((IPlayable)s, m, V("GDB_845", "damage", 3), true);
					if (!building) return;
					g.PowerHistory?.NewTargetRound();
					foreach (Minion n in next) Generic.DamageCharFunc((IPlayable)s, n, V("GDB_845", "damage", 3), true);
				})
			}));
			// [GDB_846] Exarch Naielle (3) — Battlecry: Replace your Hero Power with Tracking (Discover a card from your deck).
			cards.Add("GDB_846", new CardDef(new Power { PowerTask = new ReplaceHeroPower(Cards.FromId("GDB_846hp")) }));
			cards.Add("GDB_846hp", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.DiscoverFromDeck(c, s, Generic.PickFromDeck(c, null), (g2, c2, s2, card) => Generic.Draw(c2, card)))
			}));
			// [SC_008] Hydralisk (3) — Battlecry: Deal 2 damage to a random enemy. Repeat for each other Zerg minion you control.
			cards.Add("SC_008", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					IslandRandomHits(g, c, s, 1 + c.BoardZone.GetAll().Count(m => m != s && IsZerg(m)), V("SC_008", "damage", 2), false))
			}));
			// [SC_012] Roach (2) — When you draw this, get a copy of it. Battlecry: If you control another Zerg minion, gain +1/+2.
			cards.Add("SC_012", new CardDef(new Power
			{
				TopdeckTask = new CustomTask((g, c, s, t, stack) => { if (!c.HandZone.IsFull) Generic.Copy(c, s, (IPlayable)s, Zone.HAND); }),
				PowerTask = ComplexTask.If((g, c, s) => c.BoardZone.GetAll().Any(m => m != s && IsZerg(m)), new AddEnchantmentTask("SC_012e", EntityType.SOURCE))
			}));
			cards.Add("SC_012e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("SC_012", "attack", 1)), Effects.Health_N(V("SC_012", "health", 2))) }));
			// [SC_021] Evolution Chamber (2) — Give your minions +1 Attack. Give your Zerg an extra +1/+1.
			cards.Add("SC_021", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().ToList()) Give(g, s, m, IsZerg(m) ? "SC_021e" : "SC_021e2");
				})
			}));
			cards.Add("SC_021e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("SC_021", "attack", 1) + V("SC_021", "zerg extra", 1)), Effects.Health_N(V("SC_021", "zerg extra", 1))) }));
			cards.Add("SC_021e2", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("SC_021", "attack", 1))) }));

			// ---------------------------------------------------------------- Druid
			// [GDB_851] Astral Phaser (2) — Choose One - Deal $2 damage to two random enemy minions; or Make one Dormant for 2 turns.
			cards.Add("GDB_851", new CardDef(new Power { NoScript = true })); // (its options a / b below)
			cards.Add("GDB_851a", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in RandomOf(g, c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable), V("GDB_851", "minions", 2)))
					{
						g.PowerHistory?.NewTargetRound();
						Generic.DamageCharFunc((IPlayable)s, m, V("GDB_851", "damage", 2), true);
					}
				})
			}));
			cards.Add("GDB_851b", new CardDef(TargetEnemyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m) Generic.MakeDormant(m, V("GDB_851", "turns", 2), "GDB_851e", (IPlayable)s); })
			}));
			// [GDB_852] Arkonite Revelation (1) — Draw a card. If it's a spell, it costs (1) less.
			cards.Add("GDB_852", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (Generic.Draw(c) is Spell drawn && drawn.Zone?.Type == Zone.HAND) Give(g, s, drawn, GenericCostLess(V("GDB_852", "discount", 1)));
				})
			}));
			// [GDB_854] Uluu, the Everdrifter (4) — Each turn this is in your hand, gain two random Choose One choices.
			cards.Add("GDB_854", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					TriggerActivation = TriggerActivation.HAND,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is IPlayable uluu && uluu.Zone?.Type == Zone.HAND) UluuNewChoices(g, c, uluu); })
				}
			}));
			// [GDB_855] Star Grazer (8) — Elusive, Taunt. Spellburst: Give your hero +8 Attack this turn and gain 8 Armor.
			cards.Add("GDB_855", new CardDef(new Power
			{
				Trigger = Spellburst(ComplexTask.Create(new CustomTask((g, c, s, t, stack) => Give(g, s, c.Hero, "GDB_855e")), new ArmorTask(V("GDB_855", "armor", 8))))
			}));
			cards.Add("GDB_855e", OneTurn(Effects.Attack_N(V("GDB_855", "attack", 8))));
			// [GDB_856] Exarch Othaar (3) — Battlecry: If you're building a Starship, get 3 different Arcane spells and reduce their
			// Costs by (2).
			cards.Add("GDB_856", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => Generic.IsBuildingStarship(c), new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Card spell in Pick(g, Cards.FormatTypeCards(g.FormatType).Where(x => x.Collectible && !x.NeverGenerated && x.Type == CardType.SPELL && x[SPELL_SCHOOL] == ARCANE), V("GDB_856", "spells", 3)))
						if (IslandToHand(c, s, spell) is IPlayable p) Give(g, s, p, GenericCostLess(V("GDB_856", "discount", 2)));
				}))
			}));
			// [GDB_857] Final Frontier (7) — Discover a 10-Cost minion from the past. Set its Cost to (1).
			cards.Add("GDB_857", new CardDef(new Power
			{
				PowerTask = new DiscoverFromTask((g, c, s) => Cards.AllWild.Where(x => x.Type == CardType.MINION && x.Cost == V("GDB_857", "cost", 10) && Cards.FromThePast(x) &&
					(x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL))), "GBL_001e")
			}));
			// [GDB_882] Cosmic Phenomenon (5) — Summon three 2/3 Elementals with Taunt. If your board is full, give your minions +1/+1.
			cards.Add("GDB_882", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new SummonTask("GDB_882t", V("GDB_882", "elementals", 3), SummonSide.SPELL),
					new CustomTask((g, c, s, t, stack) => { if (c.BoardZone.IsFull) GiveAll(g, s, c.BoardZone.GetAll().ToList(), "GDB_882e"); }))
			}));
			cards.Add("GDB_882e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("GDB_882", "stats", 1))) }));
			// [GDB_883] Distress Signal (4) — Summon two random 2-Cost minions. Refresh 2 Mana Crystals.
			cards.Add("GDB_883", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("GDB_883", "minions", 2); i++) Summon(g, c, RandomMinionOfCost(g, V("GDB_883", "cost", 2)), s);
					RefreshMana(c, V("GDB_883", "crystals", 2));
				})
			}));
			// [SC_755] Construct Pylons (0) — Your next Protoss card this turn costs (2) less.
			cards.Add("SC_755", new CardDef(new Power { PowerTask = new AddEnchantmentTask("SC_755e2", EntityType.CONTROLLER) }));
			var pylons = NextCardCost(Effects.ReduceCost(V("SC_755", "discount", 2)), new SelfCondition(IsProtoss), null);
			pylons.Power.Enchant = new Enchant(new IEffect[0]) { IsOneTurnEffect = true };
			cards.Add("SC_755e2", pylons);
			// [SC_756] Carrier (12) — At the end of your turn, summon four 4/1 Interceptors that attack random enemies.
			cards.Add("SC_756", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) => IslandSummonThenAttack(g, c, s, Cards.FromId("SC_756t"), V("SC_756", "interceptors", 4), null))
				}
			}));
			// [SC_763] Immortal (7) — Taunt, Divine Shield. Battlecry: Spend 4 Mana to double this minion's stats.
			cards.Add("SC_763", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.RemainingMana >= V("SC_763", "mana", 4), new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me)) return;
					c.UsedMana += V("SC_763", "mana", 4);
					Generic.AddEnchantmentBlock(g, Cards.FromId("SC_763e"), me, me, me.AttackDamage, me.Health, 0);
				}))
			}));
			cards.Add("SC_763e", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackHealthScriptTag }));
		}
	}
}
