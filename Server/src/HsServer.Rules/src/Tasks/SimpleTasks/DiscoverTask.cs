#region copyright
// SabberStone, Hearthstone Simulator in C# .NET Core
// Copyright (C) 2017-2019 SabberStone Team, darkfriend77 & rnilva
//
// SabberStone is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as
// published by the Free Software Foundation, either version 3 of the
// License.
// SabberStone is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
#endregion
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Tasks.SimpleTasks
{
	public enum DiscoverType
	{
		INVALID,
		BASIC_HEROPOWERS,
		DRAGON,
		OP_DECK,
		BASIC_TOTEM,
		MINION,
		DECK_MINION,
		SPELL_RANDOM,
		SPELL,
		DEATHRATTLE,
		ONE_COST,
		THREE_COST,
		SIX_COST_SUMMON,
		BEAST,
		MECHANICAL,
		ARTIFACT,
		DRUID_ROGUE_SHAMAN,
		MAGE_PRIEST_WARLOCK,
		OVERLOAD,
		TAUNT,
		HUNTER_PALADIN_WARRIOR,
		MURLOC,
		SECRET,
		ELEMENTAL,
		ALL,
		ELEMENTAL_INVOCATION,
		OWN_SPELL,
		COST_8_MORE_SUMMON,
		OP_HERO,
		DIED_THIS_GAME,
		BRANCHING_PATHS,
		LEGENDARY_MINIONS,
		BATTLECRY,
		DEMON,
		OP_DECK_MINION,
		DEATHRATTLE_MINIONS,
		SPELL_COSTS_5_OR_MORE,
		WEAPON_ANOTHERCLASS,
		_d_WEAPON_ANOTHERCLASS,
		_h_WEAPON_ANOTHERCLASS,
		_m_WEAPON_ANOTHERCLASS,
		_pa_WEAPON_ANOTHERCLASS,
		_pr_WEAPON_ANOTHERCLASS,
		_r_WEAPON_ANOTHERCLASS,
		_s_WEAPON_ANOTHERCLASS,
		_wl_WEAPON_ANOTHERCLASS,
		_wr_WEAPON_ANOTHERCLASS,
		_dh_WEAPON_ANOTHERCLASS, // HsServer
		_dk_WEAPON_ANOTHERCLASS, // HsServer
		HEISTBARON_TOGWAGGLE,
	}

	public class DiscoverTask : SimpleTask
	{
		private static readonly ConcurrentDictionary<(DiscoverType, CardClass, FormatType), (Card[][], ChoiceAction)>
			CachedDiscoverySets =
				new ConcurrentDictionary<(DiscoverType, CardClass, FormatType), (Card[][], ChoiceAction)>(); // HsServer: keyed by class and format too (the pools depend on them)

		// HsServer (livecheck 2026-10-10): keyed by the format too, and the criteria's value counts (DiscoverCriteria.Equals): a
		// "Discover a 5-Cost minion" pool was reused for "an 8-Cost minion", a Standard pool in a Wild game.
		private static readonly ConcurrentDictionary<(DiscoverCriteria, CardClass, FormatType), Card[][]>
			CachedDiscoverySetsByCriteria = new ConcurrentDictionary<(DiscoverCriteria, CardClass, FormatType), Card[][]>();

		private readonly DiscoverType _discoverType;
		//private readonly Card _enchantmentCard;
		private readonly int _numberOfChoices = 3;
		private readonly ISimpleTask _taskTodo;
		private readonly Predicate<Card[]> _keepAllCondition;

		private readonly DiscoverCriteria _discoverCriteria;
		private readonly ChoiceAction _choiceAction;
		private readonly int _repeat;

		private ref readonly DiscoverCriteria Criteria => ref _discoverCriteria;

		public DiscoverTask(CardType cardType = CardType.INVALID, CardClass cardClass = CardClass.INVALID,
			(GameTag tag, RelaSign relaSign, int value) tagValueCriteria = default, ChoiceAction choiceAction = ChoiceAction.HAND,
			ISimpleTask afterDiscoverTask = null, int repeat = 1, Predicate<Card[]> keepAllCondition = null)
		{
			_discoverCriteria =
				new DiscoverCriteria(cardType, cardClass, tagValueCriteria);
			_choiceAction = choiceAction;
			_repeat = repeat;
			_taskTodo = afterDiscoverTask;
			_keepAllCondition = keepAllCondition;
		}

		public DiscoverTask(DiscoverType discoverType, int numberOfChoices = 3)
		{
			_discoverType = discoverType;
			_numberOfChoices = numberOfChoices;
			//if (enchantmentId != null)
			//	_enchantmentCard = Cards.FromId(enchantmentId);
		}

		public DiscoverTask(DiscoverType discoverType, ISimpleTask afterDiscoverTask)
		{
			_discoverType = discoverType;
			_taskTodo = afterDiscoverTask;
		}

		/// <summary>
		/// Keep all cards if the given condition is satisfied.
		/// </summary>
		public DiscoverTask(DiscoverType discoverType, Predicate<Card[]> keepAllCondition)
		{
			_discoverType = discoverType;
			_keepAllCondition = keepAllCondition;
		}

		public override TaskState Process(in Game game, in Controller controller, in IEntity source,
			in IPlayable target,
			in TaskStack stack = null)
		{
			Card[][] cardsToDiscover;
			ChoiceAction choiceAction;

			// Sets of cards to discover.
			if (_discoverType != DiscoverType.INVALID)
				cardsToDiscover = Discover(in game, controller, in _discoverType, out choiceAction);
			else
			{
				CardClass cls = Criteria.CardClass == CardClass.OP_CLASS
					? controller.Opponent.HeroClass
					: controller.HeroClass;

				if (cls == CardClass.NEUTRAL)
				{
					if (!source.Card.HasClass(CardClass.NEUTRAL))
						cls = source.Card.Class;
					else
						cls = (CardClass) game.Random.Next(2, 11);
				}

				cardsToDiscover = Discover(game.FormatType, in _discoverCriteria, cls);
				choiceAction = _choiceAction;
			}

			// HsServer: Discovered straight onto the battlefield: no Colossals (Card.NeverRandomlyOnBoard; wiki Generate,
			// "Summoning only": excluded from being "Discovered directly into the battlefield")
			if (choiceAction == ChoiceAction.SUMMON)
				cardsToDiscover = cardsToDiscover.Select(set => set.Where(c => !c.NeverRandomlyOnBoard).ToArray()).ToArray();
			// HsServer: never a copy of itself (Cards.SelfOf)
			Card self = Cards.SelfOf(source);
			if (self != null && cardsToDiscover.Any(set => set.Contains(self)))
				cardsToDiscover = cardsToDiscover.Select(set => set.Where(c => c.Id != self.Id).ToArray()).ToArray();

			// HsServer: deck-gated families (Galakrond, Herald, Imbue) only with one in the deck (Cards.DeckAllows)
			Controller discoverer = controller;
			cardsToDiscover = cardsToDiscover.Select(set => set.Where(c => Cards.DeckAllows(discoverer, c)).ToArray()).ToArray();

			if (cardsToDiscover.Length == 1)
				cardsToDiscover[0] = cardsToDiscover[0].Distinct().ToArray();

			// Gets cards to choose from the sets. 
			Card[] result = GetChoices(cardsToDiscover, _numberOfChoices, game.Random);

			// TODO work on it ...
			//if (game.Splitting)
			//{
			//    ProcessSplit(cardsToDiscover, choiceAction);
			//}


			if (result.Length == 0)
			{
				game.Log(LogLevel.INFO, BlockType.PLAY, "DiscoverTask",
					!game.Logging ? "" : $"Found no potential cards to use for {_discoverType}");

				return TaskState.STOP;
			}

			if (_keepAllCondition?.Invoke(result) ?? false)
			{
				for (int i = 0; i < result.Length && !controller.HandZone.IsFull; i++)
				{
					IPlayable entity = Entity.FromCard(in controller, result[i]);
					entity[GameTag.DISPLAYED_CREATOR] = source.Id;
					Generic.AddHandPhase.Invoke(controller, entity);
				}

				return TaskState.COMPLETE;
			}

			Generic.CreateChoiceCards.Invoke(controller, source, null, ChoiceType.GENERAL, choiceAction,
					result, _taskTodo);

			if (_repeat > 1)
			{
				Choice current = controller.Choice;
				current.AfterChooseTask = null;
				for (int i = _repeat - 2; i >= 0; i--)
				{
					Choice choice = new Choice(controller, cardsToDiscover)
					{
						ChoiceAction = choiceAction,
						ChoiceType = ChoiceType.GENERAL,
						SourceId = source.Id,
					};
					current.NextChoice = choice;
					current = choice;
				}

				current.AfterChooseTask = _taskTodo;
			}

			return TaskState.COMPLETE;
		}

		//private void ProcessSplit(Game game, Controller controller, IEntity source, Card[][] cardsToDiscover,
		//	ChoiceAction choiceAction)
		//{
		//	int neutralCnt = cardsToDiscover[0].Length;
		//	int classCnt = 0;
		//	var uniqueList = new List<Card>(cardsToDiscover[0]);

		//	if (cardsToDiscover.Length > 1)
		//	{
		//		classCnt = cardsToDiscover[1].Length;
		//		uniqueList.AddRange(cardsToDiscover[1]);
		//	}

		//	List<IEnumerable<Card>> combinations = Util.GetDiscoverSets(uniqueList).ToList();

		//	game.Log(LogLevel.INFO, BlockType.PLAY, "DiscoverTask",
		//		!game.Logging
		//			? ""
		//			: $"... found {combinations.Count} discovery splits [class: {classCnt}, neutral: {neutralCnt}]");
		//	combinations.ForEach(p =>
		//	{
		//		Game cloneGame = game.Clone();
		//		Controller cloneController = cloneGame.ControllerById(controller.Id);
		//		bool success = Generic.CreateChoiceCards.Invoke(cloneController, source, null, ChoiceType.GENERAL,
		//			choiceAction, p.ToArray(), null, _taskTodo);
		//		cloneGame.TaskQueue.CurrentTask.State = TaskState.COMPLETE;
		//	});
		//}

		private Card[][] Discover(in Game game, Controller controller, in DiscoverType discoverType,
			out ChoiceAction choiceAction)
		{
			var cacheKey = (discoverType, controller.BaseClass, game.FormatType); // HsServer: class- and format-dependent pools
			if (!CachedDiscoverySets.TryGetValue(cacheKey, out (Card[][], ChoiceAction) result))
			{
				FormatType format = game.FormatType;

				switch (discoverType)
				{
					case DiscoverType.DRUID_ROGUE_SHAMAN:
					{
						choiceAction = ChoiceAction.HAND;
						Card[][] cardSets =
							GetTriClass(in format, CardClass.DRUID, CardClass.ROGUE, CardClass.SHAMAN);
						
						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.MAGE_PRIEST_WARLOCK:
					{
						choiceAction = ChoiceAction.HAND;
						Card[][] cardSets =
							GetTriClass(in format, CardClass.MAGE, CardClass.PRIEST, CardClass.WARLOCK);
						
						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.HUNTER_PALADIN_WARRIOR:
					{
						choiceAction = ChoiceAction.HAND;
						Card[][] cardSets = GetTriClass(in format, CardClass.HUNTER, CardClass.PALADIN,
							CardClass.WARRIOR);
						
						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.BASIC_HEROPOWERS:
					{
						choiceAction = ChoiceAction.HEROPOWER;
						// HsServer: one basic Hero Power per other class (HeroCards() now also holds every client hero skin), and
						// not cached: it depends on the discovering player's class.
						CardClass own = controller.HeroClass;
						return new[]
						{
							Cards.HeroClasses.Where(cls => cls != own)
								.Select(cls => Cards.HeroCard(cls) is { } hero && hero[GameTag.HERO_POWER] > 0 ? Cards.FromAssetId(hero[GameTag.HERO_POWER]) : null)
								.Where(p => p != null).ToArray()
						};
					}
					case DiscoverType.DRAGON:
					{
						choiceAction = ChoiceAction.HAND;
						Card[][] cardSets = GetFilter(in format, in controller,
							list => list.Where(p => p.IsRace(Race.DRAGON)));

						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.OVERLOAD:
					{
						choiceAction = ChoiceAction.HAND;
						IEnumerable<Card> cardSet = Cards.FormatTypeCards(game.FormatType);
						Card[][] cardSets = {cardSet.Where(p => p.HasOverload).ToArray()};

						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.TAUNT:
					{
						choiceAction = ChoiceAction.HAND;
						Card[][] cardSets = GetFilter(in format, in controller,
							list => list.Where(p => p[GameTag.TAUNT] == 1));
						
						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.SECRET:
					{
						choiceAction = ChoiceAction.HAND;
						CardClass classForSecret =
							controller.BaseClass == CardClass.PALADIN
							|| controller.BaseClass == CardClass.MAGE
							|| controller.BaseClass == CardClass.HUNTER
								? controller.BaseClass
								: CardClass.PALADIN;
						Card[][] cardSets = GetClassCard(in format, classForSecret,
							list => list.Where(p => p[GameTag.SECRET] == 1));
						
						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.BEAST:
					{
						choiceAction = ChoiceAction.HAND;
						Card[][] cardSets = GetFilter(in format, in controller,
							list => list.Where(p => p.IsRace(Race.BEAST)));
						
						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.MURLOC:
					{
						choiceAction = ChoiceAction.HAND;
						Card[][] cardSets = GetFilter(in format, in controller,
							list => list.Where(p => p.IsRace(Race.MURLOC)));
						
						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.ELEMENTAL:
					{
						choiceAction = ChoiceAction.HAND;
						Card[][] cardSets = GetFilter(in format, in controller,
							list => list.Where(p => p.IsRace(Race.ELEMENTAL)));
						
						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.MECHANICAL:
					{
						choiceAction = ChoiceAction.HAND;
						Card[][] cardSets = GetFilter(in format, in controller,
							list => list.Where(p => p.IsRace(Race.MECHANICAL)));
						
						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.DEMON:
					{
						choiceAction = ChoiceAction.HAND;
						Card[][] cardSets = GetFilter(in format, in controller,
							list => list.Where(p => p.IsRace(Race.DEMON)));
						
						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.ALL:
					{
						choiceAction = ChoiceAction.HAND;
						Card[][] cardSets =
							GetFilter(in format, in controller, list => list.Where(p => p.Cost >= 0));
						
						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.ARTIFACT:
					{
						choiceAction = ChoiceAction.HAND;
						Card[][] cardSets =
						{
							new []
							{
								Cards.FromId("LOEA16_3"),
								Cards.FromId("LOEA16_4"),
								Cards.FromId("LOEA16_5")
							}
						};
						
						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.ELEMENTAL_INVOCATION:
					{
						choiceAction = ChoiceAction.CAST;
						Card[][] cardSets =
						{
							new []
							{
								Cards.FromId("UNG_211a"),
								Cards.FromId("UNG_211b"),
								Cards.FromId("UNG_211c"),
								Cards.FromId("UNG_211d")
							}
						};
						
						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.MINION:
					{
						choiceAction = ChoiceAction.HAND;
						Card[][] cardSets = GetFilter(in format, in controller,
							list => list.Where(p => p.Type == CardType.MINION));
						
						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.DECK_MINION:
					{
						choiceAction = ChoiceAction.HAND;
						Card[][] cardSets =
							{controller.DeckZone.Where(p => Generic.IsMinionCard(p)).Select(p => p.Card).ToArray()};
						return cardSets;
					}
					case DiscoverType.OP_DECK_MINION:
					{
						choiceAction = ChoiceAction.HAND;
						Card[][] cardSets =
							{controller.Opponent.DeckZone.Where(p => Generic.IsMinionCard(p)).Select(p => p.Card).ToArray()};
						return cardSets;
					}
					case DiscoverType.DEATHRATTLE:
					{
						choiceAction = ChoiceAction.HAND;
						Card[][] cardSets = GetFilter(in format, in controller,
							list => list.Where(p => p[GameTag.DEATHRATTLE] == 1));
						
						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.DEATHRATTLE_MINIONS:
					{
						choiceAction = ChoiceAction.HAND;
						Card[][] cardSets = GetFilter(in format, in controller,
							list => list.Where(p => p[GameTag.DEATHRATTLE] == 1 && p.Type == CardType.MINION));
						
						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.BATTLECRY:
					{
						choiceAction = ChoiceAction.HAND;
						Card[][] cardSets = GetFilter(in format, in controller,
							list => list.Where(p => p[GameTag.BATTLECRY] == 1 && p.Type == CardType.MINION));
						
						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.ONE_COST:
					{
						choiceAction = ChoiceAction.HAND;
						Card[][] cardSets =
							GetFilter(in format, in controller, list => list.Where(p => p.Cost == 1));
						
						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.THREE_COST:
					{
						choiceAction = ChoiceAction.HAND;
						Card[][] cardSets =
							GetFilter(in format, in controller, list => list.Where(p => p.Cost == 3));
						
						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.SIX_COST_SUMMON:
					{
						choiceAction = ChoiceAction.SUMMON;
						Card[][] cardSets =
							GetFilter(in format, in controller, list => list.Where(p => p.Cost == 6 && p.Type == CardType.MINION));

						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.SPELL:
					{
						choiceAction = ChoiceAction.HAND;
						Card[][] cardSets = GetFilter(in format, in controller,
							list => list.Where(p => p.Type == CardType.SPELL));
						
						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.SPELL_COSTS_5_OR_MORE:
					{
						choiceAction = ChoiceAction.HAND;
						Card[][] cardSets = GetFilter(in format, in controller,
							list => list.Where(p => p.Type == CardType.SPELL && p.Cost >= 5));
						
						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.SPELL_RANDOM:
					{
						choiceAction = ChoiceAction.SPELL_RANDOM;
						Card[][] cardSets = GetFilter(in format, in controller,
							list => list.Where(p => p.Type == CardType.SPELL && !p.HideStat));
						
						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.OWN_SPELL:
					{
						choiceAction = ChoiceAction.HAND;
						Card[][] cardSets =
							{controller.DeckZone.Where(p => p is Spell).Select(p => p.Card).ToArray()};
						return cardSets;
					}
					case DiscoverType.BASIC_TOTEM:
					{
						choiceAction = ChoiceAction.SUMMON;
						Card[][] cardSets =
						{
							new []
							{
								Cards.FromId("AT_132_SHAMANa"),
								Cards.FromId("AT_132_SHAMANb"),
								Cards.FromId("AT_132_SHAMANc"),
								Cards.FromId("AT_132_SHAMANd")
							}
						};
						
						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.COST_8_MORE_SUMMON:
					{
						choiceAction = ChoiceAction.SUMMON;
						Card[][] cardSets = GetFilter(in format, in controller,
							list => list.Where(p => p.Cost >= 8 && p.Type == CardType.MINION));
						
						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.OP_DECK:
					{
						choiceAction = ChoiceAction.HAND;
						Card[][] cardSets = { controller.Opponent.DeckZone.Select(p => p.Card).ToArray()};
						return cardSets;
					}
					case DiscoverType.OP_HERO:
					{
						choiceAction = ChoiceAction.HAND;
						Card[][] cardSets = GetClassCard(in format, controller.Opponent.HeroClass, list => list);
						
						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.DIED_THIS_GAME:
					{
						choiceAction = ChoiceAction.SUMMON;
						Card[][] cardSets =
						{
							// HsServer: the one "died" record (a dead card brought back as itself is set aside, not in the Graveyard)
							controller.DiedThisGameCards.Where(p => p.Card.Type == CardType.MINION)
								.Select(p => p.Card).ToArray()
						};
						return cardSets;
					}
					case DiscoverType.BRANCHING_PATHS:
					{
						choiceAction = ChoiceAction.CAST;
						Card[][] cardSets =
						{
							new []
							{
								Cards.FromId("LOOT_054b"),
								Cards.FromId("LOOT_054c"),
								Cards.FromId("LOOT_054d")
							}
						};
						
						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.LEGENDARY_MINIONS:
					{
						choiceAction = ChoiceAction.SUMMON;
						Card[][] cardSets = GetFilter(in format, in controller,
							list => list.Where(p => p.Rarity == Rarity.LEGENDARY && p.Type == CardType.MINION));
						
						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					case DiscoverType.WEAPON_ANOTHERCLASS:
					{
						choiceAction = ChoiceAction.HAND;
						return GetAnotherClassWeapons(controller.HeroClass, format);
					}
					case DiscoverType.HEISTBARON_TOGWAGGLE:
					{
						choiceAction = ChoiceAction.HAND;
						Card[][] cardSets =
						{
							new []
							{
								Cards.FromId("LOOT_998h"), // Tolin's Goblet
								Cards.FromId("LOOT_998j"), // Zarog's Crown
								Cards.FromId("LOOT_998l"), // Wondrous Wand
								Cards.FromId("LOOT_998k"), // Golden Kobold
							}
						};
						CachedDiscoverySets.TryAdd(cacheKey, (cardSets, choiceAction));
						return cardSets;
					}
					default:
						throw new ArgumentOutOfRangeException(nameof(discoverType), discoverType, null);
				}
			}

			choiceAction = result.Item2;
			return result.Item1;
		}

		private Card[][] GetClassCard(in FormatType formatType, CardClass heroClass,
			in Func<IEnumerable<Card>, IEnumerable<Card>> filter)
		{
			Dictionary<CardClass, IReadOnlyList<Card>> cardSet = Cards.FormatTypeClassCards(formatType);
			IEnumerable<Card> classCards =
				filter.Invoke(cardSet[heroClass].Where(p => p.HasClass(heroClass) && !p.IsQuest));
			return new[] {classCards.ToArray()};
		}

		private Card[][] GetTriClass(in FormatType formatType, CardClass class1, CardClass class2, CardClass class3)
		{
			Dictionary<CardClass, IReadOnlyList<Card>> cardSet = Cards.FormatTypeClassCards(formatType);
			return new[]
			{
				cardSet[class1].Where(p => (p.HasClass(class1) || p.MultiClassGroup != 0) && !p.IsQuest).ToArray(),
				cardSet[class2].Where(p => (p.HasClass(class2) || p.MultiClassGroup != 0) && !p.IsQuest).ToArray(),
				cardSet[class3].Where(p => (p.HasClass(class3) || p.MultiClassGroup != 0) && !p.IsQuest).ToArray()
			};
		}

		private Card[][] GetFilter(in FormatType formatType, in Controller controller,
			in Func<IEnumerable<Card>, IEnumerable<Card>> filter)
		{
			Dictionary<CardClass, IReadOnlyList<Card>> cardSet = Cards.FormatTypeClassCards(formatType);
			CardClass heroClass = controller.BaseClass != CardClass.NEUTRAL
				? controller.BaseClass
				: Util.Choose(Cards.HeroClasses, controller.Game.Random);
			IEnumerable<Card> nonClassCards = filter.Invoke(cardSet[heroClass].Where(p => !p.HasClass(heroClass)));
			IEnumerable<Card> classCards =
				filter.Invoke(cardSet[heroClass].Where(p => p.HasClass(heroClass) && !p.IsQuest));
			return new[] {nonClassCards.ToArray(), classCards.ToArray()};
		}

		private Card[][] GetAnotherClassWeapons(CardClass myClass, FormatType format)
		{
			DiscoverType type;

			switch (myClass)
			{
				case CardClass.DRUID:
					type = DiscoverType._d_WEAPON_ANOTHERCLASS;
					break;
				case CardClass.HUNTER:
					type = DiscoverType._h_WEAPON_ANOTHERCLASS;
					break;
				case CardClass.MAGE:
					type = DiscoverType._m_WEAPON_ANOTHERCLASS;
					break;
				case CardClass.PALADIN:
					type = DiscoverType._pa_WEAPON_ANOTHERCLASS;
					break;
				case CardClass.PRIEST:
					type = DiscoverType._pr_WEAPON_ANOTHERCLASS;
					break;
				case CardClass.ROGUE:
					type = DiscoverType._r_WEAPON_ANOTHERCLASS;
					break;
				case CardClass.SHAMAN:
					type = DiscoverType._s_WEAPON_ANOTHERCLASS;
					break;
				case CardClass.WARLOCK:
					type = DiscoverType._wl_WEAPON_ANOTHERCLASS;
					break;
				case CardClass.WARRIOR:
					type = DiscoverType._wr_WEAPON_ANOTHERCLASS;
					break;
				case CardClass.DEMONHUNTER: // HsServer
					type = DiscoverType._dh_WEAPON_ANOTHERCLASS;
					break;
				case CardClass.DEATHKNIGHT: // HsServer
					type = DiscoverType._dk_WEAPON_ANOTHERCLASS;
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(myClass), myClass, null);
			}

			if (CachedDiscoverySets.TryGetValue((type, myClass, format), out (Card[][], ChoiceAction) value))
				return value.Item1;

			Card[][] cardSets = {
				Cards.FormatTypeCards(format)
					.Where(p => p.Type == CardType.WEAPON && !p.HasClass(myClass))
					.ToArray()
			};

			CachedDiscoverySets.TryAdd((type, myClass, format), (cardSets, ChoiceAction.HAND));

			return cardSets;
		}

		private Card[][] Discover(FormatType format, in DiscoverCriteria criteria, CardClass cls)
		{
			Card[][] cards;
			if (criteria.CardClass != CardClass.INVALID && criteria.CardClass != CardClass.ANOTHER_CLASS)
				cls = CardClass.INVALID;

			if (CachedDiscoverySetsByCriteria.TryGetValue((criteria, cls, format), out cards))
				return cards;

			if (criteria.CardClass == CardClass.INVALID)
			{ // Use the player's Class
				IReadOnlyList<Card> allCards = Cards.FormatTypeClassCards(format)[cls];
				List<Card> classCards = new List<Card>();
				List<Card> neutralCards = new List<Card>();
				foreach (Card card in allCards)
				{
					if (!criteria.Evaluate(card)) continue;

					if (card.HasClass(cls))
						classCards.Add(card);
					else
						neutralCards.Add(card);
				}
				//IReadOnlyList<Card> neutralCards = Cards.FormatTypeClassCards(format)[CardClass.NEUTRAL];

				cards = new[]
				{
					neutralCards.ToArray(),
					classCards.ToArray()
				};
			}
			else if
				(criteria.CardClass == CardClass.ANOTHER_CLASS)
			{
				IEnumerable<Card> allCards = Cards.FormatTypeCards(format);
				List<Card> matching = new List<Card>();
				foreach (Card card in allCards)
				{
					if (!criteria.Evaluate(card)) continue;

					if (!card.HasClass(cls))
						matching.Add(card);
				}

				cards = new[] {matching.ToArray(), new Card[0]};
			}
			else
			{	// Use the given class criterion
				IReadOnlyList<Card> allCards = Cards.FormatTypeClassCards(format)[criteria.CardClass];

				List<Card> classCards = new List<Card>();
				foreach (Card card in allCards)
				{
					if (!criteria.Evaluate(card)) continue;

					if (card.HasClass(criteria.CardClass))
						classCards.Add(card);
				}

				cards = new[] {classCards.ToArray(), new Card[0]};
				//cls = CardClass.INVALID;
			}

			CachedDiscoverySetsByCriteria.TryAdd((criteria, cls, format), cards);

			return cards;
		}

		// 2 Sets means Class cards / Neutral cards.
		// 3 Sets means Tri-Class discovers. (Gangs)
		public static Card[] GetChoices(Card[][] cardsToDiscover, int numberOfChoices, Util.DeepCloneableRandom rnd)
		{
			Card[] result;

			switch (cardsToDiscover.Length)
			{
				case 1:
					Card[] distinct = cardsToDiscover[0].Distinct().ToArray();
					if (numberOfChoices >= distinct.Length)
						result = distinct;
					else
					{
						result = new Card[numberOfChoices];
						Card pick;
						for (int i = 0; i < result.Length; i++)
						{
							do
							{
								pick = cardsToDiscover[0][rnd.Next(cardsToDiscover[0].Length)];
							} while (result.Contains(pick));

							result[i] = pick;
						}
					}
					break;
				// HsServer: bounded picks. Upstream looped forever when a pool had fewer distinct cards than choices (case 2), and
				// case 3 never reset its "contains" flag, so one duplicate pick hung the game (Grimestreet Informant).
				case 2:
				{
					var picked = new List<Card>(numberOfChoices);
					Card[] neutral = cardsToDiscover[0], classCards = cardsToDiscover[1];
					for (int attempt = 0; picked.Count < numberOfChoices && attempt < 200; attempt++)
					{
						int weight = neutral.Length + (classCards.Length << 2);
						if (weight == 0) break;
						int roll = rnd.Next(weight);
						Card pick = roll < neutral.Length ? neutral[roll] : classCards[rnd.Next(classCards.Length)];
						if (!picked.Contains(pick)) picked.Add(pick);
					}
					result = picked.ToArray();
					break;
				}
				case 3:
				{
					var picked = new List<Card>(3);
					for (int i = 0; i < 3; i++)
					{
						Card[] set = cardsToDiscover[i];
						for (int attempt = 0; set.Length > 0 && attempt < 50; attempt++)
						{
							Card pick = set[rnd.Next(set.Length)];
							if (picked.Contains(pick)) continue;
							picked.Add(pick);
							break;
						}
					}
					result = picked.ToArray();
					break;
				}
				default:
					throw new NotImplementedException();
			}

			return result;
		}

		private readonly struct DiscoverCriteria : IEquatable<DiscoverCriteria>
		{
			public readonly CardType CardType;
			public readonly CardClass CardClass;
			public readonly GameTag Tag;
			public readonly RelaSign RelaSign;
			public readonly int Value;

			public DiscoverCriteria(CardType cardType, CardClass cardClass, (GameTag tag, RelaSign relaSign, int value) tagValueCriteria)
			{
				CardType = cardType;
				CardClass = cardClass;
				(Tag, RelaSign, Value) = tagValueCriteria;
			}

			public bool Evaluate(Card card)
			{
				return
					(CardType == CardType.INVALID || card.Type == CardType) &&
					//(CardClass == CardClass.INVALID || card.HasClass(CardClass)) &&
					(Tag == default ||
					 ((RelaSign == RelaSign.EQ && card[Tag] == Value) ||
					 (RelaSign == RelaSign.LEQ && card[Tag] <= Value) ||
					  (RelaSign == RelaSign.GEQ && card[Tag] >= Value)));
			}

			#region Equality members

			public bool Equals(DiscoverCriteria other)
			{
				return CardType == other.CardType && CardClass == other.CardClass && Tag == other.Tag && RelaSign == other.RelaSign && Value == other.Value;
			}

			public override bool Equals(object obj)
			{
				return obj is DiscoverCriteria other && Equals(other);
			}

			public override int GetHashCode()
			{
				unchecked
				{
					var hashCode = (int) CardType;
					hashCode = (hashCode * 397) ^ (int) CardClass;
					hashCode = (hashCode * 397) ^ (int) Tag;
					hashCode = (hashCode * 397) ^ (int) RelaSign;
					hashCode = (hashCode * 397) ^ Value;
					return hashCode;
				}
			}

			#endregion

			public override string ToString()
			{
				var sb = new StringBuilder();
				if (CardType != CardType.INVALID)
					sb.Append($"[{CardType}]");
				if (CardClass != CardClass.INVALID)
					sb.Append($"[{CardClass}]");
				if (Tag != default)
					sb.Append($"[{Tag},{RelaSign},{Value}]");
				return sb.ToString();
			}
		}
	}
}
