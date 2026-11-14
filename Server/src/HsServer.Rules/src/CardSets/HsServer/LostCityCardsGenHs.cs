// HsServer addition (not upstream SabberStone): The Lost City of Un'Goro (LOST_CITY, set 1952) + the Day of Rebirth mini-set —
// chunk 1 (spec: research spec_chunk1). Shared pieces: Actions/LostCity.cs (Kindred, triggering a Deathrattle), the Maps
// (DiscoverMap / MapMark below), fixed Adapt choices (ChooseToGain), Generic.CorpsesSpent (Quests that spend Corpses). Every
// number goes through CardValues (V).
using System;
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Actions;
using SabberStoneCore.Auras;
using SabberStoneCore.Conditions;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
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
		private const GameTag EVIL_GLOW = (GameTag)401;
		/// <summary>Minion (Platysaur): the card its Battlecry drew.</summary>
		private const GameTag PLATYSAUR_CARD = (GameTag)990606;

		/// <summary>
		/// A Kindred card (wiki Kindred; Generic.Kindred): <paramref name="always"/> runs in its play effect, then
		/// <paramref name="kindred"/> when Kindred holds, else <paramref name="otherwise"/> ("… instead": Scalehide Kodo). Live sends
		/// a second, empty POWER block (index 1) after every Kindred play (vfx-decks 281393, darkmoon 69614, 22690): Primalfin
		/// Challenger's "Your next Kindred triggers twice" runs the Kindred again there (unrecorded: doubt).
		/// </summary>
		private static Power KindredPower(Action<Game, Controller, IEntity, IPlayable> kindred, Action<Game, Controller, IEntity, IPlayable> always = null,
			Action<Game, Controller, IEntity, IPlayable> otherwise = null, Power power = null)
		{
			power ??= new Power();
			power.PowerTask = new CustomTask((g, c, s, t, stack) =>
			{
				always?.Invoke(g, c, s, t);
				if (!Generic.KindredPlayed(c, s)) { otherwise?.Invoke(g, c, s, t); return; }
				kindred(g, c, s, t);
				if (c[HsTag.KINDRED_TWICE] > 0)
				{
					c[HsTag.KINDRED_TWICE] = c[HsTag.KINDRED_TWICE] - 1;
					s[HsTag.KINDRED_AGAIN] = 1;
				}
			});
			power.SecondPowerTask = new CustomTask((g, c, s, t, stack) =>
			{
				if (s[HsTag.KINDRED_AGAIN] == 0) return;
				s[HsTag.KINDRED_AGAIN] = 0;
				kindred(g, c, s, t);
			});
			return power;
		}

		/// <summary>
		/// The Maps: "Discover a … If you play it this turn, also pick one of the others." Live (Crypt Map, dk-classxp 25830-26100):
		/// the pick to hand with the "play this turn" mark (Generic.MarkPlayThisTurn); the Map's enchantment ("&lt;id&gt;e") on the
		/// player (TAG_SCRIPT_DATA_NUM_1 = the pick); the two others stay set aside and are offered again when the pick is played
		/// this turn (MapMark). Rule state in player tags (MAP_*: AI copies have no enchantment instances).
		/// </summary>
		private static ISimpleTask DiscoverMap(Func<Controller, Card, bool> where) => DiscoverMapOf((g, c) =>
			DiscoverFromTask.PickDistinct(g, Cards.GenerationPool(g, c).Where(x => (x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL)) && where(c, x)), 3));

		/// <summary>A Map whose options its card builds (Mountain Map: one minion of each of three types not played yet).</summary>
		private static ISimpleTask DiscoverMapOf(Func<Game, Controller, List<Card>> options) => new CustomTask((g, c, s, t, stack) =>
		{
			List<Card> offer = options(g, c);
			if (offer.Count == 0) return;
			var offered = offer.Select(card => Entity.FromCard(c, card, new EntityData { [GameTag.CREATOR] = s.Id }, c.SetasideZone)).ToList();
			var ids = offered.Select(p => p.Id).ToList();
			string markId = ((IPlayable)s).Card.Id + "e";
			Generic.CreateChoiceEntities(c, s, offered, ChoiceAction.STACK, new CustomTask((g2, c2, s2, pick, st) =>
			{
				if (pick == null || c2.HandZone.IsFull) return;
				pick.Zone?.Remove(pick);
				pick[GameTag.DISPLAYED_CREATOR] = s2.Id;
				if (!Generic.AddHandPhase.Invoke(c2, pick)) return;
				Generic.MarkPlayThisTurn(pick, s2);
				var others = ids.Where(id => id != pick.Id).ToList();
				c2[HsTag.MAP_PICK] = pick.Id;
				c2[HsTag.MAP_OTHER_A] = others.Count > 0 ? others[0] : 0;
				c2[HsTag.MAP_OTHER_B] = others.Count > 1 ? others[1] : 0;
				c2[HsTag.MAP_TURN] = g2.Turn;
				if (Cards.TryFromId(markId) is Card mark) Generic.AddEnchantmentBlock(g2, mark, (IPlayable)s2, c2, pick.Id, 0, 0);
			}));
		});

		/// <summary>A deck Map ("Discover a card from your deck. If you play it this turn, also pick one of the others.": Cultist Map,
		/// live L1417 4272-4573): the options are linked copies of three deck cards (Generic.DiscoverFromCards' shape); the pick's deck
		/// card is drawn and marked; the two other options stay set aside for MapMark.</summary>
		private static ISimpleTask DiscoverMapFromDeck() => new CustomTask((g, c, s, t, stack) =>
		{
			List<IPlayable> deck = Generic.PickFromDeck(c, p => true);
			if (deck.Count == 0) return;
			var offered = deck.Select(card => Entity.FromCard(c, card.Card, new EntityData
			{
				[GameTag.CREATOR] = s.Id, [GameTag.LINKED_ENTITY] = card.Id, [HsTag.COPIED_FROM_ENTITY_ID] = card.Id,
			}, c.SetasideZone)).ToList();
			var ids = offered.Select(p => p.Id).ToList();
			string markId = ((IPlayable)s).Card.Id + "e";
			Generic.CreateChoiceEntities(c, s, offered, ChoiceAction.STACK, new CustomTask((g2, c2, s2, pick, st) =>
			{
				if (pick == null) return;
				Generic.NoteAffected(pick, s2.Id);
				if (pick.Zone?.Type == Zone.SETASIDE) c2.GraveyardZone.Add(pick.Zone.Remove(pick));
				if (!g2.IdEntityDic.TryGetValue(pick[GameTag.LINKED_ENTITY], out IPlayable real) || real.Zone?.Type != Zone.DECK || real.Controller != c2) return;
				if (!(Generic.Draw(c2, real) is IPlayable drawn) || drawn.Zone?.Type != Zone.HAND) return;
				Generic.MarkPlayThisTurn(drawn, s2);
				var others = ids.Where(id => id != pick.Id).ToList();
				c2[HsTag.MAP_PICK] = drawn.Id;
				c2[HsTag.MAP_OTHER_A] = others.Count > 0 ? others[0] : 0;
				c2[HsTag.MAP_OTHER_B] = others.Count > 1 ? others[1] : 0;
				c2[HsTag.MAP_TURN] = g2.Turn;
				if (Cards.TryFromId(markId) is Card mark) Generic.AddEnchantmentBlock(g2, mark, (IPlayable)s2, c2, drawn.Id, 0, 0);
			}));
		});

		/// <summary>The Map's player enchantment: the marked card played this turn → pick one of the two others (live: its TRIGGER
		/// block, effect index 1, after the card's own effect; the enchantment then leaves).</summary>
		private static CardDef MapMark() => new CardDef(new Power
		{
			Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
			{
				TriggerSource = TriggerSource.FRIENDLY,
				EffectIndex = 1,
				Condition = new SelfCondition(p => p.Controller[HsTag.MAP_PICK] == p.Id && p.Controller[HsTag.MAP_TURN] == p.Game.Turn),
				SingleTask = new CustomTask((g, c, s, t, stack) =>
				{
					int[] ids = { c[HsTag.MAP_OTHER_A], c[HsTag.MAP_OTHER_B] };
					c[HsTag.MAP_PICK] = 0;
					c[HsTag.MAP_OTHER_A] = 0;
					c[HsTag.MAP_OTHER_B] = 0;
					var left = ids.Where(id => id > 0 && g.IdEntityDic.TryGetValue(id, out IPlayable p) && p.Zone?.Type == Zone.SETASIDE)
						.Select(id => g.IdEntityDic[id]).ToList();
					if (left.Count > 0)
						Generic.CreateChoiceEntities(c, s, left, ChoiceAction.STACK, new CustomTask((g2, c2, s2, pick, st) =>
						{
							if (pick == null) return;
							// (a deck Map — Cultist Map: the option stands for a deck card, which is drawn)
							if (pick[GameTag.LINKED_ENTITY] > 0)
							{
								Generic.NoteAffected(pick, s2.Id);
								if (pick.Zone?.Type == Zone.SETASIDE) c2.GraveyardZone.Add(pick.Zone.Remove(pick));
								if (g2.IdEntityDic.TryGetValue(pick[GameTag.LINKED_ENTITY], out IPlayable real) && real.Zone?.Type == Zone.DECK && real.Controller == c2)
									Generic.Draw(c2, real);
								return;
							}
							if (c2.HandZone.IsFull) return;
							pick.Zone?.Remove(pick);
							Generic.NoteAffected(pick, s2.Id);
							Generic.AddHandPhase.Invoke(c2, pick);
						}));
					if (s is Enchantment e) e.Remove();
				})
			}
		});

		/// <summary>"Choose to gain A, B, or C" (Ancient Stegodon / Raptor / Pterrordax): the Adapt choice among the card's own
		/// related Adapt spells (its client RelatedCards), cast on itself (the ADAPT choice path).</summary>
		private static ISimpleTask ChooseToGain() => new CustomTask((g, c, s, t, stack) =>
		{
			if (!(s is Minion me) || me.Zone?.Type != Zone.PLAY) return;
			Card[] options = (ReprintCards.RelatedCardIds(me.Card.Id) ?? new List<string>()).Select(Cards.TryFromId)
				.Where(x => x != null && x.Type == CardType.SPELL).ToArray();
			if (options.Length > 0)
				Generic.CreateChoiceCards.Invoke(c, s, new List<IPlayable> { me }, ChoiceType.GENERAL, ChoiceAction.ADAPT, options, null);
		});

		/// <summary>"If you played a Quest this game" (Questing Assistant; Sky Gen'ral Kragg's check).</summary>
		private static bool PlayedQuest(Controller c) => c.PlayHistory.Any(e => e.SourceController == c.PlayerId && e.SourceCard?.IsQuest == true);

		/// <summary>A Quest step from its own card: progress by one, its reward (QUEST_REWARD_DATABASE_ID) at the end.</summary>
		private static void QuestStep(Game g, Controller c, Spell quest)
		{
			if (quest == null || !c.SecretZone.IsActiveQuest(quest) || quest.QuestProgress >= quest.QuestTotalProgress) return;
			Card reward = Cards.FromAssetId(quest.Card[GameTag.QUEST_REWARD_DATABASE_ID]);
			if (reward != null) new QuestProgressTask(reward.Id).Process(g, c, quest, null);
		}

		/// <summary>Elise the Navigator's "craft a custom location" (live 204312 37909-38023): a Location (by tier: its Cost order),
		/// then two different effects of that tier; a NEW crafted Location to the hand (Generic.CraftedCard: the Ignis pattern).
		/// Which cards: Elise's client RelatedCards (Locations; effects TLC_100t&lt;tier&gt;&lt;n&gt;).</summary>
		private static void CraftLocation(Controller c, IPlayable elise)
		{
			var related = (ReprintCards.RelatedCardIds(elise.Card.Id) ?? new List<string>()).Select(Cards.TryFromId).Where(x => x != null).ToList();
			List<Card> bases = related.Where(x => x.Type == CardType.LOCATION).OrderBy(x => x.Cost).ToList();
			if (bases.Count == 0) return;
			int eliseId = elise.Id;
			string stem = elise.Card.Id + "t";
			Generic.CreateChoiceEntities(c, elise, OfferCards(c, elise, bases.Select(x => x.Id)), ChoiceAction.STACK, new CustomTask((g2, c2, s2, basePick, st2) =>
			{
				if (basePick == null) return;
				Card look = basePick.Card;
				char tier = (char)('1' + bases.FindIndex(x => x.Id == look.Id));
				List<Card> effects = related.Where(x => x.Type != CardType.LOCATION && x.Id.Length == stem.Length + 2 && x.Id.StartsWith(stem) && x.Id[stem.Length] == tier).ToList();
				IPlayable preview = Entity.FromCard(c2, look, new EntityData { [GameTag.CREATOR] = eliseId, [GameTag.DISPLAYED_CREATOR] = eliseId }, c2.SetasideZone);
				int previewId = preview.Id;
				if (g2.History)
					g2.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.HISTORY_TARGET_DONT_DUPLICATE_UNTIL_END, Data = 0, Info = new List<int> { previewId } });
				Generic.CreateChoiceEntities(c2, s2, OfferCards(c2, s2, Pick(g2, effects, 3).Select(x => x.Id)), ChoiceAction.STACK, new CustomTask((g3, c3, s3, first, st3) =>
				{
					if (first == null) return;
					Card part1 = first.Card;
					if (g3.IdEntityDic.TryGetValue(previewId, out IPlayable shown1)) shown1[GameTag.MODULAR_ENTITY_PART_1] = part1.AssetId;
					Generic.CreateChoiceEntities(c3, s3, OfferCards(c3, s3, Pick(g3, effects.Where(x => x.Id != part1.Id), 3).Select(x => x.Id)), ChoiceAction.STACK,
						new CustomTask((g4, c4, s4, second, st4) =>
						{
							if (second == null) return;
							if (g4.IdEntityDic.TryGetValue(previewId, out IPlayable shown2)) shown2[GameTag.MODULAR_ENTITY_PART_2] = second.Card.AssetId;
							IPlayable location = Entity.FromCard(c4, Generic.CraftedCard(look, part1, second.Card), new EntityData
							{
								[GameTag.CREATOR] = eliseId, [GameTag.DISPLAYED_CREATOR] = eliseId, [HsTag.COPIED_FROM_ENTITY_ID] = previewId,
							});
							Generic.NamedFx((IPlayable)s4, "SpawnToHand", null, () => Generic.AddHandPhase.Invoke(c4, location));
						}));
				}));
			}));
		}

		private static void LostCity(IDictionary<string, CardDef> cards)
		{
			LostCityChunk1(cards);
			LostCityChunk2(cards); // LostCityCardsGenHs2.cs (after Titans: Titanographer Osk's versions reuse the TITANS abilities)
			LostCityChunk3(cards); // LostCityCardsGenHs3.cs
			LostCityChunk4(cards); // LostCityCardsGenHs4.cs
		}

		private static void LostCityChunk1(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Demon Hunter
			// [DINO_136] Horn of Feasting (4) — Summon three 2/1 Raptors with Rush. Outcast: Give them Immune while attacking this turn.
			cards.Add("DINO_136", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					bool outcast = c.OutcastPlayId == s.Id;
					var raptors = new List<IPlayable>();
					for (int i = 0; i < V("DINO_136", "raptors", 3); i++)
						if (SummonGet(g, c, Cards.FromId("DINO_136t"), s) is Minion m) raptors.Add(m);
					if (outcast) GiveAll(g, s, raptors, "DINO_136e");
				})
			}));
			cards.Add("DINO_136e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.IMMUNE_WHILE_ATTACKING, EffectOperator.SET, 1)) { IsOneTurnEffect = true } }));
			// [DINO_137] Skittish Saucier (3) — Battlecry: Reduce the Cost of adjacent cards in your hand by (1). (the slot it was played from)
			cards.Add("DINO_137", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int slot = c.PlayedFromSlot;
					if (slot < 0) return;
					foreach (int i in new[] { slot - 1, slot }.Distinct())
						if (i >= 0 && i < c.HandZone.Count) Give(g, s, c.HandZone[i], GenericCostLess(V("DINO_137", "less", 1)));
				})
			}));
			// [DINO_138] Diabolus Rex (6) — Kindred: Deal 6 damage to your opponent's left and right-most minions.
			cards.Add("DINO_138", new CardDef(KindredPower((g, c, s, t) =>
			{
				var enemies = c.Opponent.BoardZone.Where(m => !m.Untouchable && !(m is Location)).ToList();
				if (enemies.Count == 0) return;
				foreach (Minion m in new[] { enemies[0], enemies[enemies.Count - 1] }.Distinct())
					Generic.DamageCharFunc((IPlayable)s, m, V("DINO_138", "damage", 6), false);
			})));
			// [TLC_630] Gorishi Wasp (5) — Rush. Whenever this takes damage, get a 1-Cost Gorishi Stinger.
			cards.Add("TLC_630", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE) { TriggerSource = TriggerSource.SELF, SingleTask = new CustomTask((g, c, s, t, stack) => AddToHand(c, Cards.FromId("TLC_630t"))) }
			}));
			// [TLC_630t] Gorishi Stinger (1) — Deal $2 damage. Summon a 2/1 Grub with Rush. (live darkmoon 7931: the Grub even if the target died)
			cards.Add("TLC_630t", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter ch) Generic.DamageCharFunc((IPlayable)s, ch, V("TLC_630t", "damage", 2), true);
					SummonGet(g, c, Cards.FromId("TLC_903t"), s);
				})
			}));
			// [TLC_631] Unleash the Colossus (1) — Quest: Deal exactly 2 damage to an enemy on your turn, 12 times. Reward: Gorishi Colossus.
			// (wiki: "progresses from damage being dealt anywhere" — any friendly source, hand cards too)
			cards.Add("TLC_631", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEAL_DAMAGE)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentPlayer != c || !(g.CurrentEventData?.EventTarget is ICharacter hit) || hit.Controller == c ||
						    g.CurrentEventData.EventNumber != V("TLC_631", "exact", 2)) return;
						QuestStep(g, c, s as Spell);
					})
				}
			}));
			// [TLC_631t] Gorishi Colossus (5) — Battlecry: For the rest of the game, whenever you deal exactly 2 damage to an enemy, deal 2 more.
			cards.Add("TLC_631t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.AddEnchantmentBlock(g, Cards.FromId("TLC_631e"), (IPlayable)s, c, 0, 0, 0))
			}));
			cards.Add("TLC_631e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEAL_DAMAGE)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						EventMetaData ev = g.CurrentEventData;
						// (the extra 2 is itself "exactly 2": the enchantment's own damage doesn't count)
						if (ev == null || ev.EventSource?.Id == s.Id || !(ev.EventTarget is ICharacter hit) || hit.Controller == c ||
						    ev.EventNumber != V("TLC_631t", "exact", 2) || hit.IsDead || hit.ToBeDestroyed) return;
						Generic.DamageCharFunc((IPlayable)s, hit, V("TLC_631t", "more", 2), false);
					})
				}
			}));
			// [TLC_633] Bugsquasher (4) — Battlecry: Deal 6 damage to an enemy minion with a minion type.
			cards.Add("TLC_633", new CardDef(BattlecryEnemyMinion, new Power
			{
				TargetIf = (PlayReq.REQ_TARGET_IF_AVAILABLE, (src, target) => target is Minion m && Generic.MinionTypesOf(m.Card).Count > 0),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m && Generic.MinionTypesOf(m.Card).Count > 0) Generic.DamageCharFunc((IPlayable)s, m, V("TLC_633", "damage", 6), false);
				})
			}));
			// [TLC_833] Insect Claw (3) — After your hero attacks, summon a 2/1 Grub with Rush.
			cards.Add("TLC_833", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) => SummonGet(g, c, Cards.FromId("TLC_903t"), s))
				}
			}));
			// [TLC_840] Gorishi Tunneler (3) — Stealth. After this attacks, deal 2 damage to the enemy hero.
			cards.Add("TLC_840", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) => Generic.DamageCharFunc((IPlayable)s, c.Opponent.Hero, V("TLC_840", "damage", 2), false))
				}
			}));
			// [TLC_841] Entomologist Toru (8) — Battlecry: Put each minion in your hand into 0/1 Jars that cost (1). Break them to release the
			// minions! (a NEW Jar in each minion's slot; the minion waits set aside; TLC_841e on the Jar names it — NUM_1 the entity, the
			// Jar's NUM_6 its card, alternate text — and its Deathrattle summons it. Wiki: a jarred Jar releases an empty jar.)
			cards.Add("TLC_841", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable minion in c.HandZone.GetAll().Where(p => Generic.IsMinionCard(p)).ToList())
					{
						int slot = minion.ZonePosition;
						c.HandZone.Remove(minion);
						c.SetasideZone.Add(minion);
						IPlayable jar = Entity.FromCard(c, Cards.FromId("TLC_841t"), new EntityData
						{
							[GameTag.CREATOR] = s.Id, [(GameTag)2921] = minion.Card.AssetId, [GameTag.USE_ALTERNATE_CARD_TEXT] = 1,
						});
						c.HandZone.Add(jar, Math.Min(slot, c.HandZone.Count));
						Generic.AddEnchantmentBlock(g, Cards.FromId("TLC_841e"), (IPlayable)s, jar, minion.Id, 0, 0);
					}
				})
			}));
			cards.Add("TLC_841t", new CardDef(new Power { NoScript = true }));
			cards.Add("TLC_841e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.DEATHRATTLE, EffectOperator.SET, 1)),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					// (an enchantment's Deathrattle: source = the dead Jar, target = the enchantment)
					if (!(t is Enchantment e) || !g.IdEntityDic.TryGetValue(e[GameTag.TAG_SCRIPT_DATA_NUM_1], out IPlayable inside) ||
					    !(inside is Minion m) || m.Zone?.Type != Zone.SETASIDE || c.BoardZone.IsFull) return;
					m.Zone.Remove(m);
					Generic.SummonBlock.Invoke(g, m, -1, s);
				})
			}));
			// [TLC_900] Hive Map (1) — Discover a Fel spell. If you play it this turn, also pick one of the others.
			cards.Add("TLC_900", new CardDef(new Power { PowerTask = DiscoverMap((c, x) => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == FEL) }));
			cards.Add("TLC_900e", MapMark());
			// [TLC_901] Fumigate (2) — Deal $3 damage to a minion and all others of the same minion type.
			cards.Add("TLC_901", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					var types = Generic.MinionTypesOf(m.Card);
					var hit = new List<ICharacter> { m };
					if (types.Count > 0)
						hit.AddRange(c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll())
							.Where(x => x.Id != m.Id && !(x is Location) && Generic.MinionTypesOf(x.Card).Any(types.Contains)));
					DamageAll(s, hit, V("TLC_901", "damage", 3), true);
				})
			}));
			// [TLC_902] Infestation (2) — Get two 1-Cost Gorishi Stingers.
			cards.Add("TLC_902", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("TLC_902", "stingers", 2); i++) AddToHand(c, Cards.FromId("TLC_630t")); })
			}));
			// [TLC_903] Silithid Queen (5) — Rush. Kindred: Give your hero +5 Attack this turn.
			cards.Add("TLC_903", new CardDef(KindredPower((g, c, s, t) => Give(g, s, c.Hero, "TLC_903e"))));
			cards.Add("TLC_903e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TLC_903", "attack", 5))) { IsOneTurnEffect = true } }));

			// ---------------------------------------------------------------- Death Knight
			// [DINO_415] Story of Umbra (7) — Discover a Deathrattle minion that costs (5) or more. Summon it and trigger its Deathrattle.
			cards.Add("DINO_415", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x[GameTag.DEATHRATTLE] > 0 && x.Power?.DeathrattleTask != null &&
					x.Cost >= V("DINO_415", "cost", 5), afterPick: new CustomTask((g, c, s, t, stack) =>
				{
					if (t == null || t.Zone?.Type != Zone.HAND || c.BoardZone.IsFull) return;
					var m = (Minion)t;
					c.HandZone.Remove(m);
					Generic.SummonBlock.Invoke(g, m, -1, s);
					if (m.Zone?.Type == Zone.PLAY) Generic.TriggerDeathrattleOf(s, m);
				}))
			}));
			// [DINO_417] Soulrest Ceremony (1) — Give your minions +1 Attack and Rush. They die at the end of your turn.
			cards.Add("DINO_417", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.BoardZone.GetAll().Where(m => !(m is Location)).ToList(), "DINO_417e"))
			}));
			cards.Add("DINO_417e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("DINO_417", "attack", 1)), Effects.Rush),
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Enchantment e && e.Target is Minion m && !m.IsDead) Generic.Destroy(m, e.Creator); })
				}
			}));
			// [TLC_401] Bonechill Stegodon (6) — Deathrattle: Deal 6 damage to three random enemies. (different ones)
			cards.Add("TLC_401", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed).OrderBy(_ => g.Random.Next()).Take(V("TLC_401", "enemies", 3)).ToList();
					foreach (ICharacter e in enemies) Generic.DamageCharFunc((IPlayable)s, e, V("TLC_401", "damage", 6), false);
				})
			}));
			// [TLC_432] Dread Raptor (4) — Battlecry: Draw a Deathrattle minion that costs (3) or less. Kindred: It costs (0).
			cards.Add("TLC_432", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					IPlayable drawn = DrawOne(g, c, p => p is Minion && p.Card[GameTag.DEATHRATTLE] > 0 && p.Cost <= V("TLC_432", "cost", 3));
					if (drawn != null && Generic.KindredPlayed(c, s)) Give(g, s, drawn, "GBL_009e");
				}),
				SecondPowerTask = new CustomTask((g, c, s, t, stack) => { })
			}));
			// [TLC_433] Reanimate the Terror (1) — Quest: Spend 15 Corpses. Reward: Tyrax, Bone Terror. (Generic.CorpsesSpent runs the
			// active Quest's CorpseSpentTask once per Corpse)
			cards.Add("TLC_433", new CardDef(new Power { CorpseSpentTask = new CustomTask((g, c, s, t, stack) => QuestStep(g, c, s as Spell)) }));
			// [TLC_433t] Tyrax, Bone Terror (5) — Deathrattle: Open Terror's Grave. It has "Deathrattle: Resummon Tyrax."
			cards.Add("TLC_433t", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => SummonGet(g, c, Cards.FromId("TLC_433t2"), s)) }));
			// [TLC_433t2] Terror's Grave — Location: Deal 4 damage. Deathrattle: Resummon Tyrax, Bone Terror.
			cards.Add("TLC_433t2", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is ICharacter ch) Generic.DamageCharFunc((IPlayable)s, ch, V("TLC_433t2", "damage", 4), false); }),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => SummonGet(g, c, Cards.FromId("TLC_433t"), s))
			}));
			// [TLC_435] Crypt Map (1) — Discover a Frost Rune card. If you play it this turn, also pick one of the others.
			cards.Add("TLC_435", new CardDef(new Power { PowerTask = DiscoverMap((c, x) => x[COST_FROST] > 0) }));
			cards.Add("TLC_435e", MapMark());
			// [TLC_439] Wave of Tar (4) — Deal $2 damage to all enemy minions. Enemy minions cost (2) more next turn.
			cards.Add("TLC_439", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) => DamageAll(s, c.Opponent.BoardZone.GetAll().Where(m => !(m is Location)).Cast<ICharacter>().ToList(), V("TLC_439", "damage", 2), true)),
					new AddEnchantmentTask("TLC_439e2", EntityType.OP_CONTROLLER))
			}));
			cards.Add("TLC_439e2", OpponentNextTurnCost(V("TLC_439", "more", 2), SelfCondition.IsMinion, null));
			// [TLC_440] Cryosleep (4) — Deal $4 damage and draw a card. Kindred: Draw another.
			cards.Add("TLC_440", new CardDef(TargetAny, KindredPower((g, c, s, t) => Generic.Draw(c), always: (g, c, s, t) =>
			{
				if (t is ICharacter ch) Generic.DamageCharFunc((IPlayable)s, ch, V("TLC_440", "damage", 4), true);
				Generic.Draw(c);
			})));
			// [TLC_443] Reluctant Wrangler (3) — Reborn. Deathrattle: Summon a 2/2 Undead Beast with Taunt.
			cards.Add("TLC_443", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => SummonGet(g, c, Cards.FromId("TLC_443t"), s)) }));
			// [TLC_810] High Cultist Herenn (8) — Battlecry: Summon two Deathrattle minions from your deck. They fight!
			cards.Add("TLC_810", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Minion first = SummonFromDeck(g, c, s, p => p.Card[GameTag.DEATHRATTLE] > 0);
					Minion second = SummonFromDeck(g, c, s, p => p.Card[GameTag.DEATHRATTLE] > 0);
					if (first?.Zone?.Type == Zone.PLAY && second?.Zone?.Type == Zone.PLAY) EffectAttack(g, first, second);
				})
			}));

			// ---------------------------------------------------------------- Neutral
			// [DINO_410] The Egg of Khelos (3) — Deathrattle: Summon a slightly cracked Egg. (each stage summons the next of its related cards)
			Card NextEgg(string id)
			{
				var stages = (ReprintCards.RelatedCardIds("DINO_410") ?? new List<string>()).ToList();
				int i = id == "DINO_410" ? -1 : stages.IndexOf(id);
				return i + 1 < stages.Count ? Cards.TryFromId(stages[i + 1]) : null;
			}
			foreach (string egg in new[] { "DINO_410", "DINO_410t2", "DINO_410t3", "DINO_410t4", "DINO_410t5" })
			{
				string id = egg;
				cards.Add(id, new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => SummonGet(g, c, NextEgg(id), s)) }));
			}
			// [DINO_411] Holy Eggbearer (2) — Battlecry: Draw a 0-Attack minion.
			cards.Add("DINO_411", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p is Minion m && m.AttackDamage == 0)) }));
			// [DINO_419] Herbivore Assistant (3) — Battlecry: Give a friendly Beast +2/+2 and Rush.
			cards.Add("DINO_419", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_RACE, (int)Race.BEAST } },
				new Power { PowerTask = new AddEnchantmentTask("DINO_419e", EntityType.TARGET) }));
			cards.Add("DINO_419e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("DINO_419", "stats", 2)), Effects.Health_N(V("DINO_419", "stats", 2)), Effects.Rush) }));
			// [DINO_430] Beast Speaker Taka (7) — Battlecry: Discover a Legendary Beast from any class to gain its stats. Deathrattle: Summon it.
			// (DINO_430e: the stats in its script tags, the beast captured — a copy keeps it)
			cards.Add("DINO_430", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Card[] offer = DiscoverFromTask.PickDistinct(g, Cards.GenerationPool(g, c).Where(x => x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY && x.IsRace(Race.BEAST)), 3).ToArray();
					if (offer.Length == 0) return;
					Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.STACK, offer, new CustomTask((g2, c2, s2, pick, st) =>
					{
						if (pick == null || !(s2 is Minion taka)) return;
						Generic.AddEnchantmentBlock(g2, Cards.FromId("DINO_430e"), taka, taka, pick.Card.ATK, pick.Card.Health, pick.Id);
						if (pick.Zone?.Type == Zone.SETASIDE) c2.GraveyardZone.Add(pick.Zone.Remove(pick));
					}));
				}),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Enchantment e in ((IPlayable)s).AppliedEnchantments?.Where(x => x.Card.Id == "DINO_430e").ToList() ?? new List<Enchantment>())
						if (e.CapturedCard != null) SummonGet(g, c, e.CapturedCard, s);
				})
			}));
			cards.Add("DINO_430e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0), Effects.Health_N(0)) { UseScriptTag = true } }));
			// [DINO_435] Crater Experiment (5) — Kindred: Summon a copy of this.
			cards.Add("DINO_435", new CardDef(KindredPower((g, c, s, t) =>
			{
				if (s is Minion me && me.Zone?.Type == Zone.PLAY && !c.BoardZone.IsFull) Generic.Copy(c, s, me, Zone.PLAY);
			})));
			// [TLC_100] Elise the Navigator (4) — Battlecry: If your deck started with 10 cards of different Costs, craft a custom location.
			cards.Add("TLC_100", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.DeckCards.Select(x => x.Cost).Distinct().Count() >= V("TLC_100", "costs", 10),
					new CustomTask((g, c, s, t, stack) => CraftLocation(c, (IPlayable)s)))
			}));
			foreach (string loc in new[] { "TLC_100t1", "TLC_100t2", "TLC_100t3" }) cards.Add(loc, new CardDef(new Power { NoScript = true }));
			int[] geyser = { 1, 3, 5 }, moon = { 1, 2, 4 }, runic = { 1, 4, 7 }, snap = { 1, 2, 4 }, nest = { 1, 2, 4 }, lava = { 3, 6, 12 }, crystal = { 3, 3, 5 };
			for (int tier = 1; tier <= 3; tier++)
			{
				int i = tier - 1;
				string p = "TLC_100" + "t" + tier; // (the tier's effects: TLC_100t<tier><n>)
				string geyserId = p + "1", moonId = p + "2", runicId = p + "3", snapId = p + "4", nestId = p + "5", lavaId = p + "6", crystalId = p + "7";
				cards.Add(geyserId, new CardDef(new Power
				{
					DeathrattleTask = new CustomTask((g, c, s, t, stack) => DamageAll(s, Enemies(c), V(geyserId, "damage", geyser[i]), false))
				}));
				cards.Add(moonId, new CardDef(new Power { PowerTask = new AddEnchantmentTask(moonId + "e", EntityType.CONTROLLER) }));
				cards.Add(moonId + "e", new CardDef(new Power
				{
					Enchant = new Enchant(new Effect(GameTag.SPELLPOWER, EffectOperator.ADD, V(moonId, "spell damage", moon[i]))),
					Trigger = new Trigger(TriggerType.AFTER_CAST) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = RemoveEnchantmentTask.Task }
				}));
				cards.Add(runicId, new CardDef(new Power { PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL, GenericCostLess(V(runicId, "less", runic[i]))) }));
				cards.Add(snapId, new CardDef(new Power
				{
					PowerTask = new CustomTask((g, c, s, t, stack) =>
					{
						Generic.AddEnchantmentBlock(g, Cards.FromId(snapId + "e"), (IPlayable)s, c, 0, 0, 0);
						GiveAll(g, s, c.BoardZone.GetAll().Where(m => !(m is Location)).Cast<IPlayable>().Append(c.Hero).ToList(), snapId + "e2");
					})
				}));
				cards.Add(snapId + "e", new CardDef(new Power { Enchant = new Enchant { IsOneTurnEffect = true } }));
				cards.Add(snapId + "e2", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V(snapId, "attack", snap[i]))) { IsOneTurnEffect = true } }));
				cards.Add(nestId, new CardDef(new Power
				{
					PowerTask = new CustomTask((g, c, s, t, stack) => { for (int n = 0; n < V(nestId, "raptors", nest[i]); n++) SummonGet(g, c, Cards.FromId("TLC_101t"), s); })
				}));
				cards.Add(lavaId, new CardDef(new Power { PowerTask = new ArmorTask(V(lavaId, "armor", lava[i])) }));
				cards.Add(crystalId, new CardDef(LocationFriendlyMinion, new Power
				{
					PowerTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t is Minion m && !c.BoardZone.IsFull) Generic.Copy(c, s, m, Zone.PLAY, prepare: copy => Give(g, s, copy, crystalId + "e"));
					})
				}));
				cards.Add(crystalId + "e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V(crystalId, "stats", crystal[i]))) }));
			}
			// [TLC_101] Undercover Cultist (3) — Taunt, Reborn. Has +3 Attack while damaged.
			cards.Add("TLC_101", new CardDef(new Power { Aura = new EnrageEffect(AuraType.SELF, "TLC_101e") }));
			cards.Add("TLC_101e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TLC_101", "attack", 3))) }));
			// [TLC_102] Torga (4) — Battlecry: Draw a Kindred card and another card that activates it.
			cards.Add("TLC_102", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					IPlayable kindred = DrawOne(g, c, p => p.Card[Generic.KINDRED] > 0);
					if (kindred != null) DrawOne(g, c, p => Generic.SharesTypeOrSchool(kindred.Card, p.Card));
				})
			}));
			// [TLC_106] Endbringer Umbra (7) — Battlecry: Trigger the Deathrattles of 5 friendly minions that died this game. (different ones,
			// random order: live 204312 74370)
			cards.Add("TLC_106", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var dead = c.DiedThisGameCards.Where(p => p is Minion && p.Card.Power?.DeathrattleTask != null).OrderBy(_ => g.Random.Next()).Take(V("TLC_106", "deathrattles", 5)).ToList();
					foreach (IPlayable d in dead)
					{
						if (c.Hero.IsDead) break;
						Generic.TriggerDeathrattleOf(s, d);
						g.DeathProcessingAndAuraUpdate();
					}
				})
			}));
			// [TLC_107] Stormbrewer (5) — Whenever this attacks, deal 3 damage to the target first. Kindred: Gain Rush.
			cards.Add("TLC_107", new CardDef(KindredPower((g, c, s, t) => { if (s is Minion me) me.IsRush = true; }, power: new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventTarget is ICharacter target) Generic.DamageCharFunc((IPlayable)s, target, V("TLC_107", "damage", 3), false);
					})
				}
			})));
			// [TLC_109] Relic Miner (3) — Battlecry: Destroy the top card of your deck. Discover a card of the same Rarity.
			cards.Add("TLC_109", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(c.DeckZone.TopCard is IPlayable top)) return;
					Rarity rarity = top.Card.Rarity;
					Generic.DeckCardToGraveyard(top, s.Id, suppressAnimation: false);
					DiscoverFromTask.ClassOrNeutral((c2, x) => x.Rarity == rarity).Process(g, c, s, null);
				})
			}));
			// [TLC_110] City Chief Esho (6) — Battlecry: If every minion in your deck shares a minion type, give your other minions +2/+2
			// (wherever they are). (an empty deck: no — doubt)
			bool EshoHolds(Controller c)
			{
				var minions = c.DeckZone.GetAll().Where(p => Generic.IsMinionCard(p)).ToList();
				if (minions.Count == 0) return false;
				IEnumerable<Race> shared = Generic.MinionTypes;
				foreach (IPlayable m in minions) shared = shared.Intersect(Generic.MinionTypesOf(m.Card)).ToList();
				return shared.Any();
			}
			cards.Add("TLC_110", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => EshoHolds(c), new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.BoardZone.GetAll().Where(m => m.Id != s.Id && !(m is Location)).Cast<IPlayable>()
						.Concat(c.HandZone.GetAll().Where(p => Generic.IsMinionCard(p))).Concat(c.DeckZone.GetAll().Where(p => Generic.IsMinionCard(p))).ToList(), "TLC_110e")))
			}));
			cards.Add("TLC_110e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TLC_110", "stats", 2))) }));
			// [TLC_242] Ancient Stegodon / [TLC_245] Ancient Raptor / [TLC_246] Ancient Pterrordax — Battlecry: Choose to gain …
			foreach (string ancient in new[] { "TLC_242", "TLC_245", "TLC_246" }) cards.Add(ancient, new CardDef(new Power { PowerTask = ChooseToGain() }));
			// [TLC_243] Whirling Stormdrake (9) — Rush, Windfury. Kindred: Gain Immune this turn.
			cards.Add("TLC_243", new CardDef(KindredPower((g, c, s, t) => { if (s is Minion me) Give(g, s, me, "TLC_243e"); })));
			cards.Add("TLC_243e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.IMMUNE, EffectOperator.SET, 1)) { IsOneTurnEffect = true } }));
			// [TLC_244] Curious Explorer (2) — Deathrattle: Reduce the Cost of a minion in your opponent's hand by (2). (random: hidden zone)
			cards.Add("TLC_244", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.Opponent.HandZone.GetAll().Where(p => Generic.IsMinionCard(p)).ToList();
					if (minions.Count > 0) Give(g, s, minions[g.Random.Next(minions.Count)], GenericCostLess(V("TLC_244", "less", 2)));
				})
			}));
			// [TLC_247] Primal Sabretooth (4) — Stealth. After this attacks and kills a minion, get a copy of it.
			cards.Add("TLC_247", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventTarget is Minion m && (m.IsDead || m.ToBeDestroyed)) AddToHand(c, m.Card);
					})
				}
			}));
			// [TLC_249] Sizzling Cinder (1) — Deathrattle: Deal 2 damage randomly split among all enemies.
			cards.Add("TLC_249", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => SplitAmongEnemies(g, c, (IPlayable)s, V("TLC_249", "damage", 2))) }));
			// [TLC_250] Crater Gator (4) — Battlecry: Until the start of your next turn, the enemy hero can't be healed. (TLC_250e on the
			// owner, live 130515 67396; the rule tag on the enemy hero only)
			cards.Add("TLC_250", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Generic.AddEnchantmentBlock(g, Cards.FromId("TLC_250e"), (IPlayable)s, c, 0, 0, 0);
					c.Opponent.Hero[GameTag.CANT_BE_HEALED] = c.Opponent.Hero[GameTag.CANT_BE_HEALED] + 1;
				})
			}));
			cards.Add("TLC_250e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						c.Opponent.Hero[GameTag.CANT_BE_HEALED] = Math.Max(0, c.Opponent.Hero[GameTag.CANT_BE_HEALED] - 1);
						if (s is Enchantment e) e.Remove();
					})
				}
			}));
			// [TLC_251] Primalfin Challenger (3) — Battlecry: Your next Kindred triggers twice.
			cards.Add("TLC_251", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					c[HsTag.KINDRED_TWICE] = c[HsTag.KINDRED_TWICE] + 1;
					Generic.AddEnchantmentBlock(g, Cards.FromId("TLC_251e"), (IPlayable)s, c, 0, 0, 0);
				})
			}));
			cards.Add("TLC_251e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Enchantment e && c[HsTag.KINDRED_TWICE] <= 0) e.Remove(); })
				}
			}));
			// [TLC_252] Dissolving Ooze (3) — Battlecry: Destroy a friendly minion. Spit out the Bones of its Attack and Health into your
			// hand. (current stats — patch 34.0.0; live 115158 1500: the death inside the block, then the Bones)
			cards.Add("TLC_252", new CardDef(BattlecryFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m) || m.Id == s.Id) return;
					int atk = m.AttackDamage, health = m.Health;
					Generic.Destroy(m, (IPlayable)s);
					g.DeathProcessingAndAuraUpdate();
					if (c.HandZone.IsFull) return;
					Generic.AddHandPhase.Invoke(c, Entity.FromCard(c, Cards.FromId("TLC_829t"), new EntityData
					{
						[GameTag.CREATOR] = s.Id, [GameTag.TAG_SCRIPT_DATA_NUM_1] = atk, [GameTag.TAG_SCRIPT_DATA_NUM_2] = health,
					}));
				})
			}));
			// [TLC_829t] Bones (2) — Give a minion +{0}/+{1}.
			cards.Add("TLC_829t", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m) Generic.AddEnchantmentBlock(g, Cards.FromId("TLC_829te"), (IPlayable)s, m, s[GameTag.TAG_SCRIPT_DATA_NUM_1], s[GameTag.TAG_SCRIPT_DATA_NUM_2], 0);
				})
			}));
			cards.Add("TLC_829te", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0), Effects.Health_N(0)) { UseScriptTag = true } }));
			// [TLC_253] Petrified Ogre (3) — Starts Dormant. While Dormant, gain +2/+2 at the start of your turn. (50% chance to awaken
			// instead.) (TLC_253e: the counter; at each start of your turn while Dormant: awaken, or +2/+2)
			cards.Add("TLC_253", new CardDef(new Power { NoScript = true }));
			cards.Add("TLC_253e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e) || !(e.Target is Minion ogre) || ogre[GameTag.DORMANT] == 0) return;
						if (g.Random.Next(100) < V("TLC_253", "chance", 50)) Generic.Awaken(ogre);
						else Give(g, ogre, ogre, "TLC_253e2");
					})
				}
			}));
			cards.Add("TLC_253e2", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TLC_253", "stats", 2))) }));
			// [TLC_254] Tortollan Storyteller (2) — At the end of your turn, give +1/+1 to each friendly minion of a different type.
			cards.Add("TLC_254", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
						GiveAll(g, s, OfDifferentTypes(g, c.BoardZone.GetAll().Where(m => !(m is Location)), m => m.Card).Cast<IPlayable>().ToList(), "TLC_254e"))
				}
			}));
			cards.Add("TLC_254e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TLC_254", "stats", 1))) }));
			// [TLC_255] Crystal Tender (2) — Tradeable. Battlecry: Gain empty Mana Crystals until both players have the same Mana.
			cards.Add("TLC_255", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = c.Opponent.BaseMana - c.BaseMana;
					if (n > 0) new ManaCrystalEmptyTask(n).Process(g, c, s, null);
				})
			}));
			// [TLC_256] Marshland Thresher (3) — After you cast a spell, gain Divine Shield.
			cards.Add("TLC_256", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me) me.HasDivineShield = true; }) }
			}));
			// [TLC_427] Rockskipper (2) — Battlecry: Get a 1-Cost Rock that deals $3 damage.
			cards.Add("TLC_427", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => AddToHand(c, Cards.FromId("WW_001t"))) }));
			// [TLC_429] Steamfin Thief (4) — Kindred: Summon two 1/1 Murlocs with Rush.
			cards.Add("TLC_429", new CardDef(KindredPower((g, c, s, t) => { for (int i = 0; i < V("TLC_429", "murlocs", 2); i++) SummonGet(g, c, Cards.FromId("TLC_429t"), s); })));
			// [TLC_436] Reanimated Pterrordax: CorpseCardsGenHs.cs (its Corpse cost is data).
			// [TLC_454] Scalehide Kodo (6) — Battlecry: Destroy the lowest Attack enemy minion. Kindred: The highest Attack instead.
			void Kodo(Game g, Controller c, IEntity s, bool highest)
			{
				var enemies = c.Opponent.BoardZone.GetAll().Where(m => !(m is Location) && !m.IsDead && !m.ToBeDestroyed).ToList();
				if (enemies.Count == 0) return;
				int atk = highest ? enemies.Max(m => m.AttackDamage) : enemies.Min(m => m.AttackDamage);
				var pick = enemies.Where(m => m.AttackDamage == atk).ToList();
				Generic.Destroy(pick[g.Random.Next(pick.Count)], (IPlayable)s);
			}
			cards.Add("TLC_454", new CardDef(KindredPower((g, c, s, t) => Kodo(g, c, s, true), otherwise: (g, c, s, t) => Kodo(g, c, s, false))));
			// [TLC_465] Stranglevine (3) — Deathrattle: Give a random friendly minion a random Bonus Effect and this Deathrattle.
			ISimpleTask strangle = new CustomTask((g, c, s, t, stack) =>
			{
				var friends = c.BoardZone.GetAll().Where(m => !(m is Location) && !m.IsDead && !m.ToBeDestroyed).ToList();
				if (friends.Count == 0) return;
				Minion m = friends[g.Random.Next(friends.Count)];
				Generic.GainBonusEffects(g, m, 1);
				Give(g, s, m, "TLC_465e");
			});
			cards.Add("TLC_465", new CardDef(new Power { DeathrattleTask = strangle }));
			cards.Add("TLC_465e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.DEATHRATTLE, EffectOperator.SET, 1)), DeathrattleTask = strangle }));
			// [TLC_468] Blob of Tar (5) — Deathrattle: Summon a 2/2 Blob with Poisonous and a 2/2 Blob with Taunt.
			cards.Add("TLC_468", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => { SummonGet(g, c, Cards.FromId("TLC_468t1"), s); SummonGet(g, c, Cards.FromId("TLC_468t2"), s); })
			}));
			// [TLC_480] Krog, Crater King (9) — At the end of your turn, set the Attack and Health of all enemy minions to 1.
			cards.Add("TLC_480", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.Opponent.BoardZone.GetAll().Where(m => !(m is Location)).ToList(), "TLC_480e2"))
				}
			}));
			cards.Add("TLC_480e2", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("TLC_480", "stats", 1))) }));
			// [TLC_603] Platysaur (1) — Battlecry: Draw a card. Deathrattle: Discard it. (the drawn ENTITY, while it is in the owner's hand —
			// wiki: still discarded after a return to hand; live 204915 3040: TLC_603e3 names it, the card glows red)
			cards.Add("TLC_603", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(Generic.Draw(c) is IPlayable drawn) || drawn.Zone?.Type != Zone.HAND) return;
					s[PLATYSAUR_CARD] = drawn.Id;
					drawn[EVIL_GLOW] = 1;
					Generic.AddEnchantmentBlock(g, Cards.FromId("TLC_603e3"), (IPlayable)s, (IPlayable)s, drawn.Id, 0, 0);
				}),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!g.IdEntityDic.TryGetValue(s[PLATYSAUR_CARD], out IPlayable card)) return;
					if (card[EVIL_GLOW] != 0) card[EVIL_GLOW] = 0;
					if (card.Zone?.Type == Zone.HAND && card.Controller == c) Generic.DiscardBlock.Invoke(c, card);
				})
			}));
			cards.Add("TLC_603e3", new CardDef(new Power { NoScript = true }));
			// [TLC_605] Tar Tyrant (8) — Taunt, Lifesteal. Has +6 Attack during your opponent's turn.
			cards.Add("TLC_605", new CardDef(new Power
			{
				Aura = new AdaptiveEffect(GameTag.ATK, EffectOperator.ADD, p => p.Game.CurrentPlayer != p.Controller ? V("TLC_605", "attack", 6) : 0)
			}));
			// [TLC_621] Willful Watcher (4) — Taunt. Deathrattle: Destroy the top 3 cards of your deck. (live 204312 8257: no mill suppression)
			cards.Add("TLC_621", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TLC_621", "cards", 3); i++)
						if (c.DeckZone.TopCard is IPlayable top) Generic.DeckCardToGraveyard(top, s.Id, suppressAnimation: false);
				})
			}));
			// [TLC_829] Ravenous Devilsaur (7) — Battlecry: Destroy a minion. Kindred: Gain its stats. (its current stats, read first)
			cards.Add("TLC_829", new CardDef(BattlecryMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					int atk = m.AttackDamage, health = m.Health;
					Generic.Destroy(m, (IPlayable)s);
					if (Generic.KindredPlayed(c, s) && s is Minion me)
						Generic.AddEnchantmentBlock(g, Cards.FromId("TLC_829te2"), me, me, atk, health, 0);
				}),
				SecondPowerTask = new CustomTask((g, c, s, t, stack) => { })
			}));
			cards.Add("TLC_829te2", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0), Effects.Health_N(0)) { UseScriptTag = true } }));
			// [TLC_831] Pterrordax Egg (3) — Deathrattle: Summon a 3/3 Pterrordax that steals 1 Health from all other minions.
			cards.Add("TLC_831", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(SummonGet(g, c, Cards.FromId("TLC_831t"), s) is Minion dax)) return;
					var others = c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => m.Id != dax.Id && !(m is Location) && !m.IsDead).ToList();
					GiveAll(g, dax, others, "TLC_831e");
					for (int i = 0; i < others.Count; i++) Give(g, dax, dax, "TLC_831e2");
				})
			}));
			cards.Add("TLC_831e", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(-V("TLC_831", "steal", 1))) }));
			cards.Add("TLC_831e2", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("TLC_831", "steal", 1))) }));
			// [TLC_888] Cloud Serpent (3) — Battlecry: Get a copy of another Elemental or Dragon in your hand. (picked: "not random unless it
			// says so" — user rule; doubt)
			cards.Add("TLC_888", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var mine = c.HandZone.GetAll().Where(p => p.Id != s.Id && p is Minion m && (m.IsRace(Race.ELEMENTAL) || m.IsRace(Race.DRAGON))).ToList();
					var shown = Generic.PickFrom(mine, g, null);
					if (shown.Count > 0)
						Generic.DiscoverFromCards(c, s, shown, (g2, c2, s2, card) => { if (!c2.HandZone.IsFull) Generic.Copy(c2, s2, card, Zone.HAND); });
				})
			}));
			// [TLC_987] Questing Assistant (2) — Battlecry: If you played a Quest this game, deal 3 damage to an enemy minion.
			cards.Add("TLC_987", new CardDef(BattlecryEnemyMinion, new Power
			{
				TargetIf = (PlayReq.REQ_TARGET_IF_AVAILABLE, (src, target) => PlayedQuest(src.Controller)),
				GlowWhile = p => PlayedQuest(p.Controller),
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m && PlayedQuest(c)) Generic.DamageCharFunc((IPlayable)s, m, V("TLC_987", "damage", 3), false); })
			}));
		}
	}
}
