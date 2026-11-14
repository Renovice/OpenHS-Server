// HsServer addition (not upstream SabberStone): The Lost City of Un'Goro — chunk 4: Shaman, Warlock and Warrior (spec: research
// spec_chunk4). Shared pieces: Kindred, the Maps (DiscoverMapOf), Generic.IsTemporary, the interactable object (Generic.
// UseInteractable: the Underfel Rift), HsTag.ELEMENTAL_EXTRA_DAMAGE (Character.TakeDamage), the Adapt record (Choose.cs).
// Every number goes through CardValues (V).
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
		/// <summary>Every minion type the player has played this game (a dual type marks both; an All-type minion marks none — doubt).</summary>
		private static HashSet<Race> TypesPlayedThisGame(Controller c)
		{
			var played = new HashSet<Race>();
			foreach (PlayHistoryEntry e in c.PlayHistory)
				if (e.SourceController == c.PlayerId && e.SourceCard?.Type == CardType.MINION && !(e.SourceCard.IsRace(Race.ALL) && e.SourceCard.GetRawRace() == Race.ALL))
					played.UnionWith(Generic.MinionTypesOf(e.SourceCard));
			return played;
		}

		/// <summary>The Sizzling Cinder this card summons (its COLLECTION_RELATED card).</summary>
		private static Card RelatedToken(IEntity s) =>
			s is IPlayable p ? Cards.FromAssetId(p.Card[GameTag.COLLECTION_RELATED_CARD_DATABASE_ID]) : null;

		private static void LostCityChunk4(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Shaman
			// [TLC_221] Sizzling Swarm (6) — Deal $3 damage. Summon that many 2/1 Sizzling Cinders. (that many = what the card shows: 3 +
			// Spell Damage — doubt with prevention)
			cards.Add("TLC_221", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = Generic.SpellDamageAmount((IPlayable)s, V("TLC_221", "damage", 3));
					if (t is ICharacter ch) Generic.DamageCharFunc((IPlayable)s, ch, V("TLC_221", "damage", 3), true);
					for (int i = 0; i < n; i++) SummonGet(g, c, RelatedToken(s) ?? Cards.FromId("TLC_249"), s);
				})
			}));
			// [TLC_222] Flight of the Firehawk (3) — Draw two minions of different minion types. Give them +1/+1.
			cards.Add("TLC_222", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var different = OfDifferentTypes(g, c.DeckZone.GetAll().Where(p => Generic.IsMinionCard(p)), p => p.Card).Take(V("TLC_222", "draws", 2)).ToList();
					foreach (IPlayable p in different)
						if (Generic.Draw(c, p) is IPlayable drawn && drawn.Zone?.Type == Zone.HAND) Give(g, s, drawn, "TLC_222e");
				})
			}));
			cards.Add("TLC_222e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TLC_222", "stats", 1))) }));
			// [TLC_223] Volcanic Thrasher (3) — Battlecry: Draw a Fire spell. Kindred: Give it Spell Damage +2.
			cards.Add("TLC_223", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					bool kindred = Generic.KindredPlayed(c, s);
					if (DrawOne(g, c, p => p is Spell && p.Card[SPELL_SCHOOL] == FIRE) is IPlayable drawn && kindred) Give(g, s, drawn, "TLC_223e");
				}),
				SecondPowerTask = new CustomTask((g, c, s, t, stack) => { })
			}));
			cards.Add("TLC_223e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.SPELLPOWER, EffectOperator.ADD, V("TLC_223", "spell damage", 2))) }));
			// [TLC_224] Mechanized Magma (4) — Whenever you play a Fire spell, gain stats equal to its Cost. (live 130515 105933: before the
			// spell's effect, trigger index 1)
			cards.Add("TLC_224", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					EffectIndex = 1,
					Condition = new SelfCondition(p => p is Spell && p.Card[SPELL_SCHOOL] == FIRE),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable spell = t ?? g.CurrentEventData?.EventSource as IPlayable;
						if (spell != null && s is Minion me) Give(g, s, me, "TLC_224e", PlayedCost(spell));
					})
				}
			}));
			cards.Add("TLC_224e", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackHealthScriptTag }));
			// [TLC_225] Cinderfin (2) — Deathrattle: Summon a 2/1 Sizzling Cinder.
			cards.Add("TLC_225", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => SummonGet(g, c, RelatedToken(s) ?? Cards.FromId("TLC_249"), s)) }));
			// [TLC_227] Lava Flow (3) — Deal $2 damage to the lowest Health enemy, three times. Overload: (1) (re-picked before each hit)
			cards.Add("TLC_227", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TLC_227", "hits", 3); i++)
						if (LowestHealthEnemy(g, c) is ICharacter target) Generic.DamageCharFunc((IPlayable)s, target, V("TLC_227", "damage", 2), true);
				})
			}));
			// [TLC_228] Bralma Searstone (3) — Your Elementals deal 1 extra damage.
			cards.Add("TLC_228", new CardDef(new Power
			{
				Aura = new Aura(AuraType.CONTROLLER, new Effect(HsTag.ELEMENTAL_EXTRA_DAMAGE, EffectOperator.ADD, V("TLC_228", "damage", 1)))
			}));
			// [TLC_229] Spirit of the Mountain (1) — Quest: Play 6 minions of unique types. Reward: Ashalon. (the most minions of different
			// types played since the Quest — OfDifferentTypes; NUM_1..NUM_6 = them, the client's type banner)
			cards.Add("TLC_229", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Spell quest) || !c.SecretZone.IsActiveQuest(quest)) return;
						int start = c.PlayHistory.FindIndex(h => h.SourceId == quest.Id);
						var played = c.PlayHistory.Skip(start + 1).Where(h => h.SourceController == c.PlayerId && h.SourceCard?.Type == CardType.MINION).ToList();
						var matched = OfDifferentTypes(g, played, h => h.SourceCard);
						GameTag[] nums = { GameTag.TAG_SCRIPT_DATA_NUM_1, GameTag.TAG_SCRIPT_DATA_NUM_2, (GameTag)2889, (GameTag)2919, (GameTag)2920, (GameTag)2921 };
						for (int i = 0; i < nums.Length; i++) quest[nums[i]] = i < matched.Count ? matched[i].SourceId : 0;
						int count = Math.Min(matched.Count, quest.QuestTotalProgress);
						while (quest.QuestProgress < count && c.SecretZone.IsActiveQuest(quest)) QuestStep(g, c, quest);
					})
				}
			}));
			// [TLC_229t14] Ashalon, Ridge Guardian (5) — Rush. Battlecry: Adapt twice. For the rest of the game, give minions you play those
			// Adaptations. (TLC_229t14e: the player's Adaptations from its NUM_2 on — Choose.cs records them)
			cards.Add("TLC_229t14", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Generic.AddEnchantmentBlock(g, Cards.FromId("TLC_229t14e"), (IPlayable)s, c, 0, c[HsTag.ADAPT_PICKS], 0);
					for (int i = 0; i < V("TLC_229t14", "adapts", 2); i++) new AdaptTask(EntityType.SOURCE).Process(g, c, s, null);
				})
			}));
			cards.Add("TLC_229t14e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e) || !(t is Minion m)) return;
						int from = e[GameTag.TAG_SCRIPT_DATA_NUM_2], n = V("TLC_229t14", "adapts", 2);
						for (int i = from; i < from + n && i < c[HsTag.ADAPT_PICKS]; i++)
							if (Cards.FromAssetId(c[HsTag.ADAPT_PICK_BASE + i]) is Card adapt && adapt.Power?.PowerTask is ISimpleTask task)
								task.Process(g, c, Entity.FromCard(c, adapt, null, c.SetasideZone), m);
					})
				}
			}));
			// [TLC_464] Mountain Map (1) — Discover a minion with a type you haven't played. If you play it this turn, also pick one of the
			// others. (live dk-classxp 3570: one minion of each of three such types, class or neutral)
			cards.Add("TLC_464", new CardDef(new Power
			{
				PowerTask = DiscoverMapOf((g, c) =>
				{
					var unplayed = Generic.MinionTypes.Except(TypesPlayedThisGame(c)).OrderBy(_ => g.Random.Next()).ToList();
					var pool = Cards.GenerationPool(g, c).Where(x => x.Type == CardType.MINION && (x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL))).ToList();
					var offer = new List<Card>();
					foreach (Race type in unplayed)
					{
						if (offer.Count >= 3) break;
						var ofType = pool.Where(x => x.IsRace(type) && !(x.IsRace(Race.ALL) && x.GetRawRace() == Race.ALL) && offer.All(o => o.Id != x.Id)).ToList();
						if (ofType.Count > 0) offer.Add(ofType[g.Random.Next(ofType.Count)]);
					}
					return offer;
				})
			}));
			cards.Add("TLC_464e", MapMark());
			// [TLC_482] Slagclaw (5) — Battlecry: Summon two 2/1 Sizzling Cinders. Kindred: Trigger your Sizzling Cinders' Deathrattles.
			// (wiki 36.0.0: the Kindred part once even when the Battlecry repeats)
			cards.Add("TLC_482", new CardDef(KindredPower((g, c, s, t) =>
			{
				if (s[GameTag.TAG_SCRIPT_DATA_NUM_2] > 0) return;
				s[GameTag.TAG_SCRIPT_DATA_NUM_2] = 1;
				Card cinder = RelatedToken(s);
				foreach (Minion m in c.BoardZone.GetAll().Where(m => m.Card == cinder && !m.IsDead).ToList()) Generic.TriggerDeathrattleOf(s, m);
			}, always: (g, c, s, t) => { for (int i = 0; i < V("TLC_482", "cinders", 2); i++) SummonGet(g, c, RelatedToken(s) ?? Cards.FromId("TLC_249"), s); })));
			// [DINO_406] Fire Breath (3) — Deal $4 damage. Give your Elementals +1/+1.
			cards.Add("DINO_406", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter ch) Generic.DamageCharFunc((IPlayable)s, ch, V("DINO_406", "damage", 4), true);
					GiveAll(g, s, c.BoardZone.GetAll().Where(m => !(m is Location) && m.IsRace(Race.ELEMENTAL) && !m.IsDead).ToList(), "DINO_406e");
				})
			}));
			cards.Add("DINO_406e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DINO_406", "stats", 1))) }));
			// [DINO_412] Tortotem (1) — At the end of your turn, get a random minion with multiple minion types. (All-type left out — doubt)
			cards.Add("DINO_412", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (RandomCard(g, x => x.Type == CardType.MINION && !(x.IsRace(Race.ALL) && x.GetRawRace() == Race.ALL) && Generic.MinionTypesOf(x).Count >= 2) is Card m) AddToHand(c, m);
					})
				}
			}));
			// [DINO_413] Chillspine Stegodon (4) — Battlecry: Deal 2 damage to two random enemy minions. Kindred: And Freeze them.
			void Chillspine(Game g, Controller c, IEntity s, bool freeze)
			{
				var hit = c.Opponent.BoardZone.GetAll().Where(m => !(m is Location) && !m.IsDead && !m.ToBeDestroyed).OrderBy(_ => g.Random.Next()).Take(V("DINO_413", "minions", 2)).ToList();
				foreach (Minion m in hit) Generic.DamageCharFunc((IPlayable)s, m, V("DINO_413", "damage", 2), false);
				if (freeze) foreach (Minion m in hit) m.IsFrozen = true;
			}
			cards.Add("DINO_413", new CardDef(KindredPower((g, c, s, t) => Chillspine(g, c, s, true), otherwise: (g, c, s, t) => Chillspine(g, c, s, false))));

			// ---------------------------------------------------------------- Warlock
			// [TLC_446] Escape the Underfel (1) — Quest: Play 6 Temporary cards. Reward: Underfel Rift.
			cards.Add("TLC_446", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(Generic.IsTemporary),
					SingleTask = new CustomTask((g, c, s, t, stack) => QuestStep(g, c, s as Spell))
				}
			}));
			// [TLC_446t] Underfel Rift (5) — Open a Rift to the Underfel. Throw a card in to summon 2 random Fel Beasts. (Once per turn.)
			cards.Add("TLC_446t", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => SummonGet(g, c, Cards.FromId("TLC_446t1"), s))
			}));
			// [TLC_446t1] Underfel Rift — the board object (client INTERACTABLE_OBJECT): drag a hand card onto it; the card is removed from
			// the game (wiki: "distinct from discarding or destroying it") and two random Fel Beasts (Deathrot Maw's related cards) come;
			// one charge, back at the start of its owner's turn
			cards.Add("TLC_446t1", new CardDef(new Power
			{
				InteractTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t != null) Generic.RemoveFromGame(t, s);
					for (int i = 0; i < V("TLC_446t1", "beasts", 2); i++) SummonGet(g, c, Generic.RandomRelated(g, Cards.FromId("TLC_479"), CardType.MINION), s);
				}),
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion rift) rift[GameTag.TAG_SCRIPT_DATA_NUM_1] = rift.Card[GameTag.TAG_SCRIPT_DATA_NUM_1]; })
				}
			}));
			// [TLC_447] Caustic Fumes (4) — Destroy an enemy minion. Kindred: Deal $2 damage to all minions.
			cards.Add("TLC_447", new CardDef(TargetEnemy.Concat(new[] { new KeyValuePair<PlayReq, int>(PlayReq.REQ_MINION_TARGET, 0) }).ToDictionary(x => x.Key, x => x.Value),
				KindredPower((g, c, s, t) => DamageAll(s, BoardMinions(c).Concat(BoardMinions(c.Opponent)).ToList(), V("TLC_447", "damage", 2), true),
					always: (g, c, s, t) => { if (t is Minion m) Generic.Destroy(m, (IPlayable)s); })));
			// [TLC_449] Bloodpetal Biome (1) — Location: Discover a Temporary 1-Cost minion.
			cards.Add("TLC_449", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.Cost == V("TLC_449", "cost", 1),
					afterPick: new CustomTask((g, c, s, t, stack) => { if (t?.Zone?.Type == Zone.HAND) Generic.MakeTemporary(t, s); }))
			}));
			// [TLC_450] Spelunker (2) — Battlecry: Your next Temporary card costs (2) less.
			cards.Add("TLC_450", new CardDef(new Power { PowerTask = new AddEnchantmentTask("TLC_450e", EntityType.CONTROLLER) }));
			cards.Add("TLC_450e", NextCardCost(Effects.ReduceCost(V("TLC_450", "less", 2)), new SelfCondition(Generic.IsTemporary), null));
			// [TLC_451] Cursed Catacombs (0) — Discover another card from your deck. Make it Temporary. ("another": not a copy of this — doubt)
			cards.Add("TLC_451", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					Generic.DiscoverFromDeck(c, s, Generic.PickFromDeck(c, p => p.Card.Id != ((IPlayable)s).Card.Id), (g2, c2, s2, card) =>
					{
						if (Generic.Draw(c2, card) is IPlayable drawn && drawn.Zone?.Type == Zone.HAND) Generic.MakeTemporary(drawn, s2);
					}))
			}));
			// [TLC_463] Razidir (4) — Battlecry: Discard a random card from your hand. Kindred: Your opponent's hand instead.
			cards.Add("TLC_463", new CardDef(KindredPower((g, c, s, t) => DiscardRandom(g, c.Opponent, p => true),
				otherwise: (g, c, s, t) => DiscardRandom(g, c, p => p.Id != s.Id))));
			// [TLC_466] Story of Lakkari (7) — At the end of your turn, discard a card and fill your board with 3/2 Imps. Lasts 3 turns.
			cards.Add("TLC_466", new CardDef(PaladinAura(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						DiscardRandom(g, c, p => true);
						Card imp = RelatedToken(s) ?? Cards.FromId("UNG_829t3");
						while (!c.BoardZone.IsFull && SummonGet(g, c, imp, s) != null) { }
					})
				}
			})));
			// [TLC_467] Whispering Stone (5) — Taunt. Deathrattle: Get 2 random Fel spells. They cost Health instead of Mana.
			cards.Add("TLC_467", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TLC_467", "spells", 2) && !c.HandZone.IsFull; i++)
					{
						if (!(RandomCard(g, x => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == FEL) is Card spell)) continue;
						IPlayable got = Entity.FromCard(c, spell, new EntityData { [GameTag.CREATOR] = s.Id });
						if (Generic.AddHandPhase.Invoke(c, got)) Give(g, s, got, "TLC_467e");
					}
				})
			}));
			cards.Add("TLC_467e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.CARD_COSTS_HEALTH, EffectOperator.SET, 1)) }));
			// [TLC_469] Tunnel Terror (3) — Deathrattle: Get two random Temporary 2-Cost minions. (any class: live)
			cards.Add("TLC_469", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TLC_469", "minions", 2) && !c.HandZone.IsFull; i++)
					{
						if (!(RandomCard(g, x => x.Type == CardType.MINION && x.Cost == V("TLC_469", "cost", 2)) is Card m)) continue;
						IPlayable got = Entity.FromCard(c, m, new EntityData { [GameTag.CREATOR] = s.Id });
						if (Generic.AddHandPhase.Invoke(c, got)) Generic.MakeTemporary(got, s);
					}
				})
			}));
			// [TLC_479] Deathrot Maw (6) — Taunt. Deathrattle: Summon a random Fel Beast. (its related cards)
			cards.Add("TLC_479", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => SummonGet(g, c, Generic.RandomRelated(g, ((IPlayable)s).Card, CardType.MINION), s))
			}));
			// [DINO_131] Possessed Animancer (5) — Deathrattle: Summon a random Beast from your deck. Give it Lifesteal.
			cards.Add("DINO_131", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (SummonFromDeck(g, c, s, p => p.Card.IsRace(Race.BEAST)) is Minion m) m[GameTag.LIFESTEAL] = 1;
				})
			}));
			// [DINO_132] Asphyxiodon (8) — Taunt. At the end of your turn, deal 5 damage to a random enemy minion.
			cards.Add("DINO_132", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var enemies = BoardMinions(c.Opponent).Where(m => m.Health > 0 && !m.ToBeDestroyed).ToList();
						if (enemies.Count > 0) Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], V("DINO_132", "damage", 5), false);
					})
				}
			}));
			// [DINO_402] Bat Mask (8) — Set a friendly minion's stats to 1/1. Fill your board with copies of it. (copies keep its enchantments)
			cards.Add("DINO_402", new CardDef(TargetFriendlyMinionToPlay, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Give(g, s, m, "DINO_402e");
					while (!c.BoardZone.IsFull && m.Zone?.Type == Zone.PLAY && Generic.Copy(c, s, m, Zone.PLAY) != null) { }
				})
			}));
			cards.Add("DINO_402e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("DINO_402", "stats", 1))) }));

			// ---------------------------------------------------------------- Warrior
			// [TLC_478] Axe of the Forefathers (3) — After your hero attacks, deal 1 damage to all minions. (live 204312 59248: index 1)
			cards.Add("TLC_478", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					EffectIndex = 1,
					SingleTask = new CustomTask((g, c, s, t, stack) => DamageAll(s, BoardMinions(c).Concat(BoardMinions(c.Opponent)).ToList(), V("TLC_478", "damage", 1), false))
				}
			}));
			// [TLC_600] Windpeak Wyrm (8) — Battlecry: Deal 5 damage and gain 5 Armor. Kindred: Costs (2) less.
			cards.Add("TLC_600", new CardDef(BattlecryAny, KindredPower((g, c, s, t) => { }, always: (g, c, s, t) =>
			{
				if (t is ICharacter ch) Generic.DamageCharFunc((IPlayable)s, ch, V("TLC_600", "damage", 5), false);
				new ArmorTask(V("TLC_600", "armor", 5)).Process(g, c, s, null);
			}, power: new Power { Aura = new AdaptiveCostEffect(p => Generic.Kindred(p.Controller, p) ? V("TLC_600", "less", 2) : 0) })));
			// [TLC_601] Shellnado (4) — Spend up to 5 Armor. For each spent, deal $1 damage to all minions. (a wave per Armor, deaths between)
			cards.Add("TLC_601", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TLC_601", "armor", 5); i++)
					{
						if (c.Hero.Armor <= 0 || !Generic.PayArmor(c, s, 1)) break;
						DamageAll(s, BoardMinions(c).Concat(BoardMinions(c.Opponent)).ToList(), V("TLC_601", "damage", 1), true);
						g.DeathProcessingAndAuraUpdate();
					}
				})
			}));
			// [TLC_602] Enter the Lost City (1) — Quest: Survive 10 turns. Reward: Latorvius. (wiki: "10 friendly turns"; the end of each of
			// your turns — doubt: start vs end)
			cards.Add("TLC_602", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new CustomTask((g, c, s, t, stack) => QuestStep(g, c, s as Spell)) }
			}));
			// [TLC_602t] Latorvius, Gaze of the City (5) — Battlecry: Get 2 random 'Journey to Un'Goro' Quest Rewards. Shuffle the rest into
			// your deck. (the rewards: its related cards)
			cards.Add("TLC_602t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var rewards = (ReprintCards.RelatedCardIds(((IPlayable)s).Card.Id) ?? new List<string>()).Select(Cards.TryFromId).Where(x => x != null)
						.OrderBy(_ => g.Random.Next()).ToList();
					int keep = V("TLC_602t", "rewards", 2);
					for (int i = 0; i < rewards.Count; i++)
					{
						if (i < keep) AddToHand(c, rewards[i]);
						else if (!c.DeckZone.IsFull) Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, rewards[i], new EntityData { [GameTag.CREATOR] = s.Id }));
					}
				})
			}));
			// [TLC_606] Latorvian Armorer (3) — Battlecry: Deal 2 damage to an enemy minion. If it dies, gain 5 Armor.
			cards.Add("TLC_606", new CardDef(BattlecryEnemyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Generic.DamageCharFunc((IPlayable)s, m, V("TLC_606", "damage", 2), false);
					if (m.IsDead || m.ToBeDestroyed || m.Health <= 0) new ArmorTask(V("TLC_606", "armor", 5)).Process(g, c, s, null);
				})
			}));
			// [TLC_620] Fortify (3) — Gain 3 Armor. Deal damage equal to your Armor to an enemy minion.
			cards.Add("TLC_620", new CardDef(TargetEnemy.Concat(new[] { new KeyValuePair<PlayReq, int>(PlayReq.REQ_MINION_TARGET, 0) }).ToDictionary(x => x.Key, x => x.Value), new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					new ArmorTask(V("TLC_620", "armor", 3)).Process(g, c, s, null);
					if (t is Minion m) Generic.DamageCharFunc((IPlayable)s, m, c.Hero.Armor, true);
				})
			}));
			// [TLC_622] City Defenses (4) — Summon two 0/6 Security with Taunt. They gain +1 Attack when damaged.
			cards.Add("TLC_622", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("TLC_622", "guards", 2); i++) SummonGet(g, c, Cards.FromId("TLC_622t"), s); })
			}));
			cards.Add("TLC_622t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE) { TriggerSource = TriggerSource.SELF, SingleTask = new AddEnchantmentTask("TLC_622e", EntityType.SOURCE) }
			}));
			cards.Add("TLC_622e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TLC_622t", "attack", 1))) }));
			// [TLC_623] Stonecarver (2) — At the end of your turn, give another friendly damaged minion +2/+2. (a trigger: random)
			cards.Add("TLC_623", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var damaged = c.BoardZone.GetAll().Where(m => m.Id != s.Id && !(m is Location) && m.Damage > 0 && !m.IsDead).ToList();
						if (damaged.Count > 0) Give(g, s, damaged[g.Random.Next(damaged.Count)], "TLC_623e");
					})
				}
			}));
			cards.Add("TLC_623e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TLC_623", "stats", 2))) }));
			// [TLC_624] Nablya, the Watcher (6) — Battlecry: Summon copies of your damaged minions. Give the copies Rush.
			cards.Add("TLC_624", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().Where(m => !(m is Location) && m.Damage > 0 && !m.IsDead).ToList())
					{
						if (c.BoardZone.IsFull) break;
						if (Generic.Copy(c, s, m, Zone.PLAY) is Minion copy) copy.IsRush = true;
					}
				})
			}));
			// [TLC_632] Story of Sulfuras (5) — Swap your Hero Power to "Deal 8 damage to a random enemy." After 2 uses, swap back.
			// (rule book: "Replacing your Hero Power also resets the number of times you have used your Hero Power this turn")
			cards.Add("TLC_632", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Card first = RelatedToken(s) ?? Cards.FromId("TLC_632t");
					string current = c.Hero.HeroPower?.Card.Id ?? "";
					if (!current.StartsWith(first.Id)) c[HsTag.RESTORE_HERO_POWER] = c.Hero.HeroPower?.Card.AssetId ?? 0;
					new ReplaceHeroPower(first).Process(g, c, s, null);
				})
			}));
			ISimpleTask DieInsect(Func<Controller, Card> next) => new CustomTask((g, c, s, t, stack) =>
			{
				var enemies = Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed).ToList();
				if (enemies.Count > 0) Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], V("TLC_632t", "damage", 8), true);
				if (next(c) is Card then) new ReplaceHeroPower(then).Process(g, c, s, null);
			});
			cards.Add("TLC_632t", new CardDef(new Power { PowerTask = DieInsect(c => Cards.FromId("TLC_632t2")) }));
			cards.Add("TLC_632t2", new CardDef(new Power
			{
				PowerTask = DieInsect(c =>
				{
					Card restore = Cards.FromAssetId(c[HsTag.RESTORE_HERO_POWER]);
					c[HsTag.RESTORE_HERO_POWER] = 0;
					return restore;
				})
			}));
			// [DINO_400] Barricade Basher (3) — Whenever you gain Armor, gain +2/+2 and attack a random enemy minion.
			cards.Add("DINO_400", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ARMOR)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me) || me.Zone?.Type != Zone.PLAY) return;
						Give(g, s, me, "DINO_400e");
						var enemies = c.Opponent.BoardZone.GetAll().Where(m => !(m is Location) && !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList();
						if (enemies.Count > 0 && !me.IsDead && !me.ToBeDestroyed) EffectAttack(g, me, enemies[g.Random.Next(enemies.Count)]);
					})
				}
			}));
			cards.Add("DINO_400e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DINO_400", "stats", 2))) }));
			// [DINO_401] The Great Dracorex (8) — Rush. After this attacks an enemy minion, it damages ALL other enemy minions. (Cleave
			// generalised: its Attack, the same phase)
			cards.Add("DINO_401", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me) || !(g.CurrentEventData?.EventTarget is Minion defender) || defender.Controller == c) return;
						DamageAll(s, BoardMinions(c.Opponent).Where(m => m.Id != defender.Id).ToList(), me.AttackDamage, false);
					})
				}
			}));
			// [DINO_433] Guard Duty (7) — Summon a random 6, 4, and 2-Cost Taunt minion.
			cards.Add("DINO_433", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (int cost in new[] { 6, 4, 2 })
						SummonGet(g, c, RandomBoardMinion(g, cost, x => x[GameTag.TAUNT] > 0), s);
				})
			}));
		}
	}
}
