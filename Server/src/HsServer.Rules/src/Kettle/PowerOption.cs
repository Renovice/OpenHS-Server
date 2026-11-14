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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Tasks.PlayerTasks;

namespace SabberStoneCore.Kettle
	#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
{
	//message AllOptions
	//{
	//    enum PacketID
	//    {
	//        ID_a74d = 14;
	//    }
	//    required int32 id = 1;
	//    repeated Option options = 2;
	//}
	public class PowerAllOptions
	{
		private static int _index = 1;
		public int Index { get; set; }

		public List<PowerOption> PowerOptionList { get; set; }

		public List<PlayerTask> PlayerTaskList { get; set; }

		public PowerAllOptions(Game game)
		{
			Index = _index++;
			game.AllOptionsMap.Add(Index, this);
			PowerOptionList = new List<PowerOption>();
		}

		public string Print()
		{
			var str = new StringBuilder();
			str.AppendLine($"PowerOption index={Index}");
			for (int i = 0; i < PowerOptionList.Count; i++)
			{
				str.Append($"option {i} {PowerOptionList[i].Print()}");
			}

			return str.ToString();
		}
	}

	public class PowerOptionsBuilder
	{
		public static PowerAllOptions AllOptions(Game game, List<PlayerTask> list)
		{
			var result = new PowerAllOptions(game)
			{
				PlayerTaskList = list
			};

			foreach (PlayerTask option in list.Where(p => p.PlayerTaskType == PlayerTaskType.END_TURN))
			{
				result.PowerOptionList.Add(new PowerOption { OptionType = OptionType.END_TURN });
			}

			var playCards = list.Where(p => p.PlayerTaskType == PlayerTaskType.PLAY_CARD && !(p is PlayCardTask { Disguised: true })).ToList();
			// HsServer (VIOLET_HOLD): a Disguised play is its own option, marked with the DISGUISED keyword (live dk-classxp 17294)
			foreach (PlayCardTask disguised in list.OfType<PlayCardTask>().Where(p => p.Disguised).GroupBy(p => p.Source.Id).Select(x => x.First()))
				result.PowerOptionList.Add(new PowerOption
				{
					OptionType = OptionType.POWER,
					MainOption = new PowerSubOption { EntityId = disguised.Source.Id, PowerKeyword = (int)Actions.Generic.DISGUISED }
				});
			foreach (int sourceId in playCards.Select(p => p.Source.Id).Distinct())
			{
				// HsServer: TargetEntity — a character or a card in hand (Power.HandTarget)
				IEnumerable<PlayerTask> targets = playCards.Where(p => p.Source.Id == sourceId && p.TargetEntity != null && p.ChooseOne <= 0 /* HsServer: "no choose one" is -1 in this engine */);
				var mainOption = new PowerOption
				{
					OptionType = OptionType.POWER,
					MainOption = new PowerSubOption
					{
						EntityId = sourceId,
						Targets = targets.Any() ? targets.Select(p => p.TargetEntity.Id).Distinct().ToList() : null
					}
				};
				result.PowerOptionList.Add(mainOption);

				for (int i = 1; i < 3; i++)
				{
					var subOptions = playCards.Where(p => p.Source.Id == sourceId && p.ChooseOne == i).ToList();
					if (subOptions.Any())
					{
						int refCardId = subOptions.First().Source.ChooseOnePlayables[i - 1].Id;
						var refCardTargets = subOptions.Where(p => p.TargetEntity != null).Select(p => p.TargetEntity).ToList();
						mainOption.SubOptions.Add(new PowerSubOption
						{
							EntityId = refCardId,
							Targets = refCardTargets.Any() ? refCardTargets.Select(p => p.Id).Distinct().ToList() : null
						});
					}
				}
			}

			var minionAttacks = list.Where(p => p.PlayerTaskType == PlayerTaskType.MINION_ATTACK).ToList();
			foreach (int sourceId in minionAttacks.Select(p => p.Source.Id).Distinct())
			{
				IEnumerable<PlayerTask> targets = minionAttacks.Where(p => p.Source.Id == sourceId);

				result.PowerOptionList.Add(
					new PowerOption
					{
						OptionType = OptionType.POWER,
						MainOption = new PowerSubOption
						{
							EntityId = sourceId,
							Targets = targets.Select(p => p.Target.Id).Distinct().ToList()
						}
					});
			}

			var heroAttacks = list.Where(p => p.PlayerTaskType == PlayerTaskType.HERO_ATTACK).ToList();
			if (heroAttacks.Any())
			{
				IEnumerable<PlayerTask> targets = heroAttacks.Where(p => p.Target != null);
				var mainOption = new PowerOption
				{
					OptionType = OptionType.POWER,
					MainOption = new PowerSubOption
					{
						EntityId = heroAttacks.First().Controller.Hero.Id,
						Targets = targets.Any() ? targets.Select(p => p.Target.Id).Distinct().ToList() : null
					}
				};
				result.PowerOptionList.Add(mainOption);
			}

			// HsServer: trading is its own option for the card, marked with the TRADEABLE keyword (the client sends it when the
			// card is dropped on the deck).
			foreach (PlayerTask trade in list.Where(p => p.PlayerTaskType == PlayerTaskType.TRADE))
				result.PowerOptionList.Add(new PowerOption
				{
					OptionType = OptionType.POWER,
					MainOption = new PowerSubOption { EntityId = trade.Source.Id, PowerKeyword = (int)Actions.Generic.TRADEABLE }
				});

			// HsServer (VIOLET_HOLD): Preparing is its own option for the card, marked with the PREPARE keyword (a deck action)
			foreach (PlayerTask prepare in list.Where(p => p.PlayerTaskType == PlayerTaskType.PREPARE))
				result.PowerOptionList.Add(new PowerOption
				{
					OptionType = OptionType.POWER,
					MainOption = new PowerSubOption { EntityId = prepare.Source.Id, PowerKeyword = (int)Actions.Generic.PREPARE }
				});

			// HsServer: forging is its own option for the card, marked with the FORGE keyword (live-20261002-forge-drink)
			foreach (PlayerTask forge in list.Where(p => p.PlayerTaskType == PlayerTaskType.FORGE))
				result.PowerOptionList.Add(new PowerOption
				{
					OptionType = OptionType.POWER,
					MainOption = new PowerSubOption { EntityId = forge.Source.Id, PowerKeyword = (int)Actions.Generic.FORGE }
				});

			// HsServer: activating a Location is a POWER option on the Location; its targets are characters or cards in hand.
			var locations = list.OfType<LocationTask>().ToList();
			foreach (int sourceId in locations.Select(p => p.Source.Id).Distinct())
			{
				List<int> targets = locations.Where(p => p.Source.Id == sourceId && p.ActivationTarget != null)
					.Select(p => p.ActivationTarget.Id).Distinct().ToList();
				result.PowerOptionList.Add(new PowerOption
				{
					OptionType = OptionType.POWER,
					MainOption = new PowerSubOption { EntityId = sourceId, Targets = targets.Count > 0 ? targets : null }
				});
			}

			// HsServer (TITANS): a Titan's abilities are ONE POWER option on the Titan, marked with its own keyword tag 4786, its
			// abilities the sub-options — the usable ones first (ability order) with their targets, then the others with why
			// (live AllOptions, live-20261007-vfx-decks 42760 "t, t3, t2(REQ_LITERALLY_UNPLAYABLE)", 71764). The client's
			// selectedSubOption indexes this list (SabberEngineGame.ToTask maps it by the ability's entity).
			var titanMoves = list.OfType<TitanAbilityTask>().ToList();
			foreach (int sourceId in titanMoves.Select(p => p.Source.Id).Distinct())
			{
				var titan = (Model.Entities.Minion)titanMoves.First(p => p.Source.Id == sourceId).Source;
				var option = new PowerOption
				{
					OptionType = OptionType.POWER,
					MainOption = new PowerSubOption { EntityId = sourceId, PowerKeyword = (int)Actions.Generic.TITAN_OPTION }
				};
				var unusable = new List<PowerSubOption>();
				int abilities = Actions.Generic.TitanAbilityCount(titan);
				for (int i = 0; i < abilities; i++)
				{
					Model.Entities.IPlayable ability = Actions.Generic.TitanAbility(titan, i);
					if (ability == null) continue;
					var moves = titanMoves.Where(p => p.Source.Id == sourceId && p.AbilityIndex == i).ToList();
					if (moves.Count > 0)
					{
						List<int> targets = moves.Where(p => p.AbilityTarget != null).Select(p => p.AbilityTarget.Id).Distinct().ToList();
						option.SubOptions.Add(new PowerSubOption { EntityId = ability.Id, Targets = targets.Count > 0 ? targets : null });
					}
					else
						unusable.Add(new PowerSubOption { EntityId = ability.Id, PlayError = (int)Actions.Generic.TitanAbilityError(titan, i, out _) });
				}
				option.SubOptions.AddRange(unusable);
				result.PowerOptionList.Add(option);
			}

			// HsServer (SPACE): a Starship's launch is a POWER option on the Starship, the launch card GDB_905 its sub-option (live
			// live-20261005/140614 15223, dungar-student 19092)
			foreach (StarshipLaunchTask launch in list.OfType<StarshipLaunchTask>())
			{
				var option = new PowerOption { OptionType = OptionType.POWER, MainOption = new PowerSubOption { EntityId = launch.Source.Id } };
				if (launch.Source[HsTag.STARSHIP_LAUNCH_ENTITY] > 0)
					option.SubOptions.Add(new PowerSubOption { EntityId = launch.Source[HsTag.STARSHIP_LAUNCH_ENTITY] });
				result.PowerOptionList.Add(option);
			}

			// HsServer (LOST_CITY): an interactable object's POWER option (keyword INTERACTABLE_OBJECT), the hand cards its targets
			// (client InputManager: OPTION_REVERSE_TARGET)
			foreach (var uses in list.OfType<InteractTask>().GroupBy(t => t.Source.Id))
				result.PowerOptionList.Add(new PowerOption
				{
					OptionType = OptionType.POWER,
					MainOption = new PowerSubOption
					{
						EntityId = uses.Key, PowerKeyword = (int)Actions.Generic.INTERACTABLE_OBJECT,
						Targets = uses.Select(t => t.TargetEntity.Id).Distinct().ToList()
					}
				});

			// HsServer (VIOLET_HOLD): a second Hero Power's uses are its own option (its entity)
			foreach (var second in list.OfType<HeroPowerTask>().Where(p => p.Power != null).GroupBy(p => p.Power.Id))
			{
				var secondTargets = second.Where(p => p.Target != null).Select(p => p.Target.Id).Distinct().ToList();
				result.PowerOptionList.Add(new PowerOption
				{
					OptionType = OptionType.POWER,
					MainOption = new PowerSubOption { EntityId = second.Key, Targets = secondTargets.Count > 0 ? secondTargets : null }
				});
			}
			var heroPowers = list.Where(p => p.PlayerTaskType == PlayerTaskType.HERO_POWER && !(p is HeroPowerTask { Power: not null })).ToList();
			if (heroPowers.Any())
			{
				IEnumerable<PlayerTask> targets = heroPowers.Where(p => p.Target != null);
				var mainOption = new PowerOption
				{
					OptionType = OptionType.POWER,
					MainOption = new PowerSubOption
					{
						EntityId = heroPowers.First().Controller.Hero.HeroPower.Id,
						Targets = targets.Any() ? targets.Select(p => p.Target.Id).Distinct().ToList() : null
					}
				};
				result.PowerOptionList.Add(mainOption);

				// HsServer: a Choose One hero power (Plague Lord, Dire Shapeshift...) offers its options like a Choose One card.
				var power = heroPowers.First().Controller.Hero.HeroPower;
				for (int i = 1; i < 3; i++)
				{
					var subOptions = heroPowers.Where(p => p.ChooseOne == i).ToList();
					if (subOptions.Count == 0 || power.ChooseOnePlayables == null || power.ChooseOnePlayables[i - 1] == null) continue;
					var subTargets = subOptions.Where(p => p.Target != null).Select(p => p.Target.Id).ToList();
					mainOption.SubOptions.Add(new PowerSubOption
					{
						EntityId = power.ChooseOnePlayables[i - 1].Id,
						Targets = subTargets.Any() ? subTargets : null
					});
				}
			}

			// HsServer: live also lists what can't be used, and why (Model/PlayErrors.cs).
			PlayErrors.Add(list.FirstOrDefault()?.Controller ?? game.CurrentPlayer, result);
			return result;
		}
	}

	//message Option
	//{
	//    enum Type
	//    {
	//        PASS = 1;
	//        END_TURN = 2;
	//        POWER = 3;
	//    }
	//    required Type type = 1;
	//    optional SubOption main_option = 2;
	//    repeated SubOption sub_options = 3;
	//}
	public class PowerOption
	{
		public OptionType OptionType { get; set; }

		public PowerSubOption MainOption { get; set; }

		public List<PowerSubOption> SubOptions { get; set; } = new List<PowerSubOption>();

		public string Print()
		{
			var str = new StringBuilder();
			str.AppendLine($"type={OptionType} mainEntity=[{MainOption?.EntityId}]");
			for (int i = 0; i < MainOption?.Targets?.Count; i++)
			{
				str.AppendLine($" target {i} entity=[{MainOption.Targets[i]}] ");
			}
			for (int i = 0; i < SubOptions.Count; i++)
			{
				PowerSubOption subOption = SubOptions[i];
				str.AppendLine($" subOption {i} entity=[{subOption.EntityId}] ");
				for (int j = 0; j < subOption.Targets?.Count; j++)
				{
					str.AppendLine($"  target {j} entity=[{subOption.Targets[j]}] ");
				}
			}
			return str.ToString();
		}
	}

	//message SubOption
	//{
	//    required int32 id = 1;
	//    repeated int32 targets = 3 [packed=true];
	//}
	public class PowerSubOption
	{
		public int EntityId { get; set; }
		public List<int> Targets { get; set; }
		/// <summary>HsServer: the keyword this option uses (TRADEABLE for a trade, BATTLECRY for a Battlecry card's play).</summary>
		public int PowerKeyword { get; set; }
		/// <summary>HsServer: why the entity can't be used now (a client PlayError code), 0 if it can.</summary>
		public int PlayError { get; set; }
		/// <summary>HsServer: the number the client's error text shows (the card's requirement value: Shadow Word: Death's
		/// "5 or more Attack", Seabreeze Chalice's "1 enemy minion"), live errorParam; 0 if none.</summary>
		public int PlayErrorParam { get; set; }
		/// <summary>HsServer: characters this option can't target, with the reason (live lists them after the valid ones).</summary>
		public List<(int Id, int Error, int Param)> TargetErrors { get; } = new List<(int, int, int)>();
	}
}
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
