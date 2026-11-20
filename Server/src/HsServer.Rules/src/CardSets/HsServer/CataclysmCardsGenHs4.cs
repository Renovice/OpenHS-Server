// HsServer addition (not upstream SabberStone): CATACLYSM — chunk 4: Warrior, Warlock, Shaman, Al'Akir's hands and Deathwing, Worldbreaker
// (spec: research spec_chunk4). Shared pieces: Herald (Generic.Herald returns the Soldier), Colossal limbs, Power.AdjacentOnBoard
// (Generic.SyncAdjacentOnBoard), HsTag.TURN_DRAW_DISCOVER (Game.MainDraw: Commander Geddon), HsTag.FRIENDLY_ATTACKS_THIS_GAME (Attack),
// HsTag.ARMS_DESTROY_ENEMY_DECK, Generic.TriggerDeathrattleOf. Every number goes through CardValues (V); a limb's or Soldier's number is
// its own TAG_SCRIPT_DATA_NUM_1 (Herald-upgraded).
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
		/// <summary>"Deathrattle: Deal {0} damage to a random enemy." (Hands of Ragnaros, Soldier of Ragnaros)</summary>
		private static ISimpleTask HandOfRagnarosDeathrattle => new CustomTask((g, c, s, t, stack) =>
		{
			var enemies = Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed && x.Health > 0).ToList();
			if (enemies.Count > 0) Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], s[GameTag.TAG_SCRIPT_DATA_NUM_1], false);
		});

		/// <summary>"At the end of your turn, destroy the minion to the right to gain +{0}/+{0}." (Cho's / Gall's Arm, Soldier of Cho'gall;
		/// wiki patch: the gain before the victim dies). With Cho'gall, Mastermind: a random minion in the enemy's deck instead.</summary>
		private static ISimpleTask ChogallArm => new CustomTask((g, c, s, t, stack) =>
		{
			if (!(s is Minion me) || me.Zone?.Type != Zone.PLAY) return;
			int n = me[GameTag.TAG_SCRIPT_DATA_NUM_1];
			if (c.ControllerAuraEffects[HsTag.ARMS_DESTROY_ENEMY_DECK] > 0)
			{
				var deck = c.Opponent.DeckZone.Where(p => Generic.IsMinionCard(p)).ToList();
				if (deck.Count == 0) return;
				IPlayable victim = deck[g.Random.Next(deck.Count)];
				Generic.AddEnchantmentBlock(g, Cards.FromId("CATA_726te"), me, me, n, n, 0);
				Generic.DeckCardToGraveyard(victim, me.Id, suppressAnimation: false);
				return;
			}
			int right = me.ZonePosition + 1;
			if (right >= c.BoardZone.Count || !(c.BoardZone[right] is Minion target) || target is Location || target.Untouchable || target.IsDead) return;
			Generic.AddEnchantmentBlock(g, Cards.FromId("CATA_726te"), me, me, n, n, 0);
			Generic.Destroy(target, me);
		});

		/// <summary>Deathwing, Worldbreaker's Cataclysms (live darkmoon 14:18:30: each pick resolves at once — SHOW_BIG_CARD, its effect, the
		/// option to the graveyard — then the next choice offers the ones left).</summary>
		private static void UnleashCataclysms(Controller c, IEntity source, List<string> left, int picks)
		{
			if (picks <= 0 || left.Count == 0) return;
			Card[] offer = left.Select(Cards.TryFromId).Where(x => x != null).ToArray();
			if (offer.Length == 0) return;
			Generic.CreateChoiceCards.Invoke(c, source, null, ChoiceType.GENERAL, ChoiceAction.CAST, offer, new CustomTask((g, c2, s2, pick, st) =>
			{
				string chosen = pick?.Card.Id;
				UnleashCataclysms(c2, s2, left.Where(id => id != chosen).ToList(), picks - 1);
			}));
		}

		private static void CataclysmChunk4(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Deathwing, Worldbreaker (no class: multi-class Herald hero)
			// [CATA_190h] Deathwing, Worldbreaker (10, Hero) — Battlecry: Choose {0} Cataclysm(s) to unleash! Herald twice to upgrade. ({0} =
			// its number × the Herald upgrade: 1 / 2 / 4; the four Cataclysms are its related option cards)
			cards.Add("CATA_190h", new CardDef(new Power
			{
				HandCounter = p => p.Card[GameTag.TAG_SCRIPT_DATA_NUM_1] * Generic.HeraldScale(p.Controller),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var options = (ReprintCards.RelatedCardIds(((IPlayable)s).Card.Id) ?? new List<string>())
						.Where(id => Cards.TryFromId(id) is Card x && x.Type == CardType.SPELL).ToList();
					UnleashCataclysms(c, s, options, ((IPlayable)s).Card[GameTag.TAG_SCRIPT_DATA_NUM_1] * Generic.HeraldScale(c));
				})
			}));
			// [CATA_190p] Ruthless (2, Hero Power) — +5 Attack this turn.
			cards.Add("CATA_190p", new CardDef(new Power { PowerTask = new AddEnchantmentTask("CATA_190pe", EntityType.HERO) }));
			cards.Add("CATA_190pe", OneTurn(Effects.Attack_N(V("CATA_190p", "attack", 5))));
			// [CATA_190t10] Dragon's Reign — Summon a 12/12 Dragon.
			cards.Add("CATA_190t10", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("CATA_190t14"), s)) }));
			// [CATA_190t11] Topple — Destroy the highest Health enemy minion.
			cards.Add("CATA_190t11", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = c.Opponent.BoardZone.GetAll().Where(m => !(m is Location) && !m.Untouchable && !m.IsDead && !m.ToBeDestroyed).ToList();
					if (enemies.Count == 0) return;
					int most = enemies.Max(m => m.Health);
					var top = enemies.Where(m => m.Health == most).ToList();
					Generic.Destroy(top[g.Random.Next(top.Count)], s);
				})
			}));
			// [CATA_190t12] Raze — Deal 4 damage to all enemy minions.
			cards.Add("CATA_190t12", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					DamageAll(s, c.Opponent.BoardZone.GetAll().Where(m => !(m is Location) && !m.Untouchable).Cast<ICharacter>(), V("CATA_190t12", "damage", 4), false))
			}));
			// [CATA_190t13] Enthrall — Shuffle five random Legendary Dragons into your deck. They cost (1). (live: CATAFX_Deathwing_Deck_FallingShuffle)
			cards.Add("CATA_190t13", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("CATA_190t13", "dragons", 5); i++)
					{
						if (!(RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.DRAGON) && x.Rarity == Rarity.LEGENDARY) is Card dragon)) break;
						IPlayable p = Entity.FromCard(c, dragon, new EntityData { { GameTag.CREATOR, s.Id }, { GameTag.DISPLAYED_CREATOR, s.Id } });
						Generic.ShuffleIntoDeck.Invoke(c, s, p);
						if (p.Zone?.Type == Zone.DECK) Give(g, s, p, "GBL_001e");
					}
				})
			}));

			// ---------------------------------------------------------------- Warrior
			// [CATA_150] Ragnaros, the Great Fire (8) — Colossal +2. At the end of your turn, trigger your minions' Deathrattles. (in order of
			// play; the ones in play when it starts — doubt)
			cards.Add("CATA_150", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						foreach (Minion m in c.BoardZone.GetAll().Where(m => m.HasDeathrattle && !m.IsDead && !m.ToBeDestroyed).OrderBy(m => m.OrderOfPlay).ToList())
						{
							if (c.Hero.IsDead || c.Opponent.Hero.IsDead) break;
							if (m.Zone?.Type == Zone.PLAY) Generic.TriggerDeathrattleOf(s, m);
						}
					})
				}
			}));
			// [CATA_150t / t1] Hand of Ragnaros, [CATA_580t] Soldier of Ragnaros — Deathrattle: Deal {0} damage to a random enemy.
			foreach (string id in new[] { "CATA_150t", "CATA_150t1", "CATA_580t" })
				cards.Add(id, new CardDef(new Power { DeathrattleTask = HandOfRagnarosDeathrattle }));
			// [CATA_160] Scorching Ravager (4) — Battlecry: Herald {0}. Give the Soldier Rush. (live vfx 239043: a plain RUSH tag)
			cards.Add("CATA_160", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (Generic.Herald(c, (IPlayable)s) is Minion soldier) Generic.GiveBonusEffect(soldier, GameTag.RUSH); })
			}));
			// [CATA_580] Cataclysmic War Axe (3, weapon) — Battlecry: Herald {0}. (a weapon's summon: far right)
			cards.Add("CATA_580", new CardDef(new Power { PowerTask = HeraldTask }));
			// [CATA_581] Decimation (6) — Deal $@ damage to all minions. (Improved for each minion on the battlefield.) (live 204312 81198: 1 + 8
			// minions = 9)
			Func<IPlayable, int> decimation = p => p.Card[GameTag.TAG_SCRIPT_DATA_NUM_1] + p.Controller.BoardZone.Count(m => !(m is Location) && !m.Untouchable) +
			                                     p.Controller.Opponent.BoardZone.Count(m => !(m is Location) && !m.Untouchable);
			cards.Add("CATA_581", new CardDef(new Power
			{
				HandCounter = decimation,
				PowerTask = new CustomTask((g, c, s, t, stack) => DamageAll(s, AllOtherMinions(c, null).Cast<ICharacter>(), decimation((IPlayable)s), true))
			}));
			// [CATA_582] Searing Fissure (2) — Deal $1 damage to all minions. Give your hero +3 Attack this turn.
			cards.Add("CATA_582", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new CustomTask((g, c, s, t, stack) => DamageAll(s, AllOtherMinions(c, null).Cast<ICharacter>(), V("CATA_582", "damage", 1), true)),
					new AddEnchantmentTask("CATA_582e", EntityType.HERO))
			}));
			cards.Add("CATA_582e", OneTurn(Effects.Attack_N(V("CATA_582", "attack", 3))));
			// [CATA_584] Erupting Volcano (3, Location) — Deal 3 damage randomly split among enemies. If you've played a Fire spell this turn, deal
			// 3 more. (no $: not Spell Damage)
			cards.Add("CATA_584", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int hits = V("CATA_584", "damage", 3);
					if (c.PlayHistory.Any(h => h.Turn == g.Turn && h.SourceCard?.Type == CardType.SPELL && h.SourceCard[SPELL_SCHOOL] == FIRE_SCHOOL)) hits += V("CATA_584", "more", 3);
					for (int i = 0; i < hits; i++)
					{
						var enemies = Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed && x.Health > 0).ToList();
						if (enemies.Count == 0) break;
						Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], 1, false);
					}
				})
			}));
			// [CATA_585] Torch (1) — Deal $@ damage to a damaged minion. Return this to hand with any excess damage. (live 130515 37424: it deals
			// only what kills; a NEW Torch with the excess)
			cards.Add("CATA_585", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_DAMAGED_TARGET, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					int x = s[GameTag.TAG_SCRIPT_DATA_NUM_1] + c.CurrentSpellPower;
					if (s is Spell me) me[HsTag.SPELL_DAMAGE_COUNTED] = 1;
					int dealt = m.HasDivineShield || m.IsImmune ? 0 : Math.Min(x, Math.Max(0, m.Health));
					Generic.DamageCharFunc((IPlayable)s, m, m.HasDivineShield ? x : dealt, false);
					int excess = x - dealt;
					if (excess > 0 && GetCard(c, ((IPlayable)s).Card, s) is IPlayable again) again[GameTag.TAG_SCRIPT_DATA_NUM_1] = excess;
				})
			}));
			// [CATA_586] Destructive Blaze (5) — After this survives damage, summon a Destructive Blaze. Deathrattle: Deal 2 damage to a random enemy.
			cards.Add("CATA_586", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.SELF,
					Condition = new SelfCondition(p => p is Minion m && !m.IsDead && !m.ToBeDestroyed && m.Health > 0),
					SingleTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, ((IPlayable)s).Card, s))
				},
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed && x.Health > 0).ToList();
					if (enemies.Count > 0) Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], V("CATA_586", "damage", 2), false);
				})
			}));
			// [CATA_591] Commander Geddon (7) — Battlecry: Instead of drawing each turn, Discover a card from your deck. It costs (3) less. Destroy
			// the others. (Game.MainDraw: HsTag.TURN_DRAW_DISCOVER)
			cards.Add("CATA_591", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Generic.AddEnchantmentBlock(g, Cards.FromId("CATA_591e"), (IPlayable)s, c, 0, 0, 0);
					c[HsTag.TURN_DRAW_DISCOVER] = Cards.FromId("CATA_591e2").AssetId;
				})
			}));
			cards.Add("CATA_591e", new CardDef(new Power { Enchant = new Enchant() }));
			cards.Add("CATA_591e2", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("CATA_591", "less", 3))) }));
			// [CATA_610] Lo'Gosh's Last Stand (5) — Give a minion "Deathrattle: Summon a random minion from your hand."
			cards.Add("CATA_610", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("CATA_610e", EntityType.TARGET) }));
			cards.Add("CATA_610e", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.HandZone.OfType<Minion>().Where(m => Generic.IsMinionCard(m)).ToList();
					if (minions.Count > 0) Generic.SummonFromHand(c, minions[g.Random.Next(minions.Count)], s);
				})
			}));

			// ---------------------------------------------------------------- Warlock
			// [CATA_490] Ocular Occultist (3) — Taunt. Battlecry: Choose a card in your hand to discard.
			cards.Add("CATA_490", new CardDef(new Power
			{
				HandTarget = SelfCondition.AnyCard,
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t?.Zone?.Type == Zone.HAND && t.Controller == c) Generic.DiscardBlock.Invoke(c, t); })
			}));
			// [CATA_491] Eldritch Tentacles (6) — Deal $3 damage to all minions. Repeat this with 1 less damage. (3 then 2; the second wave skips
			// the mortally wounded — doubt)
			cards.Add("CATA_491", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int damage = V("CATA_491", "damage", 3), i = 0; i < 2 && damage > 0; i++, damage--)
						DamageAll(s, AllOtherMinions(c, null).Cast<ICharacter>(), damage, true);
				})
			}));
			// [CATA_492] Shrine of Twilight (4, Location) — Herald {0}. Draw a card.
			cards.Add("CATA_492", new CardDef(new Power { PowerTask = ComplexTask.Create(HeraldTask, new DrawTask()) }));
			// [CATA_493] Duke of Below (4) — Rush. Has +2/+2 for each card you've discarded this game.
			Func<IPlayable, int> duke = p => p.Controller.NumDiscardedThisGame * V("CATA_493", "stats", 2);
			cards.Add("CATA_493", new CardDef(new Power
			{
				Aura = new MultiAura(new AdaptiveEffect(GameTag.ATK, EffectOperator.ADD, duke), new AdaptiveEffect(GameTag.HEALTH, EffectOperator.ADD, duke))
			}));
			// [CATA_494] Maloriak (7) — After you discard a minion, summon a copy of it.
			cards.Add("CATA_494", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DISCARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if ((t ?? g.CurrentEventData?.EventSource as IPlayable) is Minion discarded && !c.BoardZone.IsFull) Summon(g, c, discarded.Card, s);
					})
				}
			}));
			// [CATA_496] Cursed Chains (2) — Take control of an enemy minion until the end of their turn. It can't attack this turn. (Shadow
			// Madness' way, back at the end of its owner's next turn)
			cards.Add("CATA_496", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 }, { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Controller owner = m.Controller;
					new ControlTask(EntityType.TARGET).Process(g, c, s, m);
					if (m.Controller == c && m.Zone?.Type == Zone.PLAY) Give(g, s, m, "CATA_496e", owner.Id);
				})
			}));
			cards.Add("CATA_496e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.CANT_ATTACK, EffectOperator.SET, 1), new Effect(GameTag.CONTROLLER_CHANGED_THIS_TURN, EffectOperator.SET, 1), Generic.NonKeywordCharge), // (live sets NON_KEYWORD_CHARGE too)
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					EitherTurn = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e) || !(e.Target is Minion m)) return;
						if (g.CurrentPlayer == m.Controller) return; // (the thief's turn: it stays; the end of its owner's turn returns it)
						RemoveEnchantmentTask.Task.Process(g, c, e, null);
						if (m.Zone?.Type == Zone.PLAY) new ControlTask(EntityType.TARGET, true).Process(g, m.Controller, m, m);
					})
				}
			}));
			// [CATA_498] Rafaams' Last Stand (3) — Deal $@ damage to two random enemy minions. (Upgrades each turn!)
			cards.Add("CATA_498", new CardDef(new Power
			{
				Trigger = TriggerLibrary.UpgradeEachTurn,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = c.Opponent.BoardZone.GetAll().Where(m => !(m is Location) && !m.Untouchable && !m.IsDead && !m.ToBeDestroyed).OrderBy(_ => g.Random.Next()).Take(V("CATA_498", "targets", 2)).ToList();
					foreach (Minion m in enemies) Generic.DamageCharFunc((IPlayable)s, m, s[GameTag.TAG_SCRIPT_DATA_NUM_1], true);
				})
			}));
			// [CATA_499] Disposable Acolytes (2) — When you play or discard this, summon two random 1-Cost minions.
			ISimpleTask acolytes = new CustomTask((g, c, s, t, stack) =>
			{
				Generic.BeginSummonGroup(g, s, V("CATA_499", "minions", 2));
				for (int i = 0; i < V("CATA_499", "minions", 2); i++) Summon(g, c, RandomMinionOfCost(g, V("CATA_499", "cost", 1)), s);
			});
			cards.Add("CATA_499", new CardDef(new Power
			{
				PowerTask = acolytes,
				Trigger = new Trigger(TriggerType.DISCARD) { TriggerActivation = TriggerActivation.HAND, TriggerSource = TriggerSource.SELF, SingleTask = acolytes }
			}));
			// [CATA_725] Shadowsworn Disciple (2) — Battlecry: Herald {0}. Deathrattle: Restore #3 Health to your hero.
			cards.Add("CATA_725", new CardDef(new Power { PowerTask = HeraldTask, DeathrattleTask = new HealTask(V("CATA_725", "heal", 3), EntityType.HERO) }));
			// [CATA_725t] Soldier of Cho'gall, [CATA_726t / t1] Cho's / Gall's Arm — At the end of your turn, destroy the minion to the right to
			// gain +{0}/+{0}.
			foreach (string id in new[] { "CATA_725t", "CATA_726t", "CATA_726t1" })
				cards.Add(id, new CardDef(new Power { Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = ChogallArm } }));
			cards.Add("CATA_726te", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(0)) { UseScriptTag = true } }));
			// [CATA_726] Cho'gall, Mastermind (9) — Colossal +2. Your Arms and Soldiers destroy minions in the enemy's deck instead.
			cards.Add("CATA_726", new CardDef(new Power { Aura = new Aura(AuraType.CONTROLLER, new Effect(HsTag.ARMS_DESTROY_ENEMY_DECK, EffectOperator.ADD, 1)) }));

			// ---------------------------------------------------------------- Shaman
			// [CATA_153t / t1] Charged Hand of Al'Akir, [CATA_565t] Soldier of Al'Akir — Adjacent minions have +{0} Attack. (CATA_153e each)
			foreach (string id in new[] { "CATA_153t", "CATA_153t1", "CATA_565t" })
				cards.Add(id, new CardDef(new Power { AdjacentOnBoard = "CATA_153e" }));
			cards.Add("CATA_153e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0)) { UseScriptTag = true } }));
			// [CATA_561] Ritual of Power (2) — Herald {0}. Get two 1/1 Elementals with Rush.
			cards.Add("CATA_561", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(HeraldTask, new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("CATA_561", "elementals", 2); i++) GetCard(c, Cards.FromId("CATA_561t"), s);
				}))
			}));
			// [CATA_563] Crackling Cloudstrider (3) — Battlecry: Choose a spell in your hand that costs (4) or less to absorb. Deathrattle: Cast it.
			// (live 130515 3783: the spell set aside, the Deathrattle on CATA_563e2 — Silence loses it)
			cards.Add("CATA_563", new CardDef(new Power
			{
				HandTarget = new SelfCondition(p => p is Spell && p.Cost <= V("CATA_563", "cost", 4)),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Spell spell) || spell.Zone?.Type != Zone.HAND || spell.Controller != c || !(s is Minion me)) return;
					Generic.RemoveFromHand(c, spell);
					me[GameTag.TAG_SCRIPT_DATA_NUM_1] = spell.Id;
					Give(g, s, me, "CATA_563e2", spell.Id);
				})
			}));
			cards.Add("CATA_563e2", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					int id = (t as Enchantment)?.ScriptTag1 ?? s[GameTag.TAG_SCRIPT_DATA_NUM_1];
					if (!g.IdEntityDic.TryGetValue(id, out IPlayable spell) || !(spell is Spell) || spell.Zone?.Type != Zone.SETASIDE) return;
					// (a new copy is cast — the absorbed one goes to the graveyard; a cast spell enters its own zones: a Secret, an Aura)
					spell.Controller.GraveyardZone.Add(spell.Zone.Remove(spell));
					new CastRandomSpellTask().Process(g, c, s, Entity.FromCard(c, spell.Card, new EntityData { [GameTag.CREATOR] = s.Id }));
				})
			}));
			// [CATA_564] Air Support (5) — Battlecry: Give a friendly minion Mega-Windfury. It can't attack heroes.
			cards.Add("CATA_564", new CardDef(BattlecryFriendlyMinion, new Power { PowerTask = new AddEnchantmentTask("CATA_564e", EntityType.TARGET) }));
			cards.Add("CATA_564e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.MEGA_WINDFURY, EffectOperator.SET, 1), new Effect(GameTag.CANNOT_ATTACK_HEROES, EffectOperator.SET, 1))
			}));
			// [CATA_565] Skywall Sentinel (2) — Taunt. Battlecry: Herald {0}.
			cards.Add("CATA_565", new CardDef(new Power { PowerTask = HeraldTask }));
			// [CATA_567] Ascendance (4) — Transform all friendly minions into ones that cost (1) more. They summon the originals when they die.
			// (live darkmoon 3705: a set-aside snapshot of each for the enchantment's {0}; the original comes back fresh)
			cards.Add("CATA_567", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().Where(m => !(m is Location) && !m.Untouchable).ToList())
					{
						if (!(CostShifted(g, m, V("CATA_567", "more", 1)) is Card into)) continue;
						IPlayable snapshot = Entity.FromCard(c, m.Card, Generic.KeepHeraldUpgrade(m, new EntityData { { GameTag.CREATOR, s.Id }, { HsTag.COPIED_FROM_ENTITY_ID, m.Id } }), c.SetasideZone);
						int slot = m.ZonePosition;
						Generic.TransformBlock.Invoke(c, into, m);
						if (slot < c.BoardZone.Count && c.BoardZone[slot] is Minion now && now.Card == into)
							Give(g, s, now, "CATA_567e", snapshot.Id);
					}
				})
			}));
			cards.Add("CATA_567e", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					int id = (t as Enchantment)?.ScriptTag1 ?? 0;
					if (g.IdEntityDic.TryGetValue(id, out IPlayable original)) // (fresh, with the upgrade it was made with: Generic.KeepHeraldUpgrade)
						if (!c.BoardZone.IsFull) Generic.SummonBlock.Invoke(g, (Minion)Entity.FromCard(c, original.Card, Generic.KeepHeraldUpgrade(original, new EntityData())), -1, s);
				})
			}));
			// [CATA_568] Muradin's Last Stand (9) — Draw 2 cards. Costs (1) less for each time a friendly character attacked this game.
			cards.Add("CATA_568", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller[HsTag.FRIENDLY_ATTACKS_THIS_GAME] * V("CATA_568", "less", 1)),
				PowerTask = ComplexTask.Create(new DrawTask(), new DrawTask())
			}));
			// [CATA_569] Ceremonial Clash (4) — Summon a random 3, 2, and 1-Cost minion. Overload: (1)
			cards.Add("CATA_569", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Generic.BeginSummonGroup(g, s, 3);
					foreach (int cost in new[] { 3, 2, 1 }) Summon(g, c, RandomMinionOfCost(g, cost), s);
				})
			}));
			// [CATA_570] Morchok (10) — Battlecry: Draw a card and reduce its Cost by (10). Repeat this with excess Cost reduction.
			cards.Add("CATA_570", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int reduce = V("CATA_570", "reduce", 10);
					for (int guard = 0; guard < 10 && reduce > 0; guard++)
					{
						if (!(Generic.Draw(c) is IPlayable drawn) || drawn.Zone?.Type != Zone.HAND) break;
						int cost = Math.Max(0, drawn.Cost);
						Give(g, s, drawn, "CATA_570e", reduce);
						reduce -= cost;
					}
				})
			}));
			cards.Add("CATA_570e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(0)) { UseScriptTag = true } }));
			// [CATA_724] Stormbinder (4) — Deathrattle: Unlock your Overloaded Mana Crystals. Overload: (3)
			cards.Add("CATA_724", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => Generic.UnlockOverload(c)) }));
		}
	}
}
