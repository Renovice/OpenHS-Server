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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Auras;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model.Zones;
using SabberStoneCore.Triggers;

namespace SabberStoneCore.Model.Entities
{
	public partial class Enchantment : IPlayable
	{
		private readonly EntityData _tags;
		private int _creatorId;
		private int _targetId;
		private int _controllerId;
		private IPlayable _creator;
		private IEntity _target;
		private Controller _controller;
		private Card _capturedCard;

		private Enchantment(in Controller controller, in Card card, in EntityData tags, in int id)
		{
			_history = controller.Game.History;
			_logging = controller.Game.Logging;

			Game = controller.Game;
			Controller = controller;
			Card = card;
			_tags = tags;
			Id = id;
		}

		private Enchantment(in Controller c, in Enchantment e)
		{
			_history = c.Game.History;
			_logging = c.Game.Logging;

			Game = c.Game;
			Card = e.Card;
			Id = e.Id;
			//Target = e.Target is IPlayable ? (IEntity) Game.IdEntityDic[e.Target.Id] : c;
			_targetId = e._targetId;
			if (e.Target is Controller)
				_target = c;
			_controllerId = e._controllerId;
			_creatorId = e._creatorId;
			_capturedCard = e._capturedCard;
			//e.OngoingEffect?.Clone(this);
			e.ActivatedTrigger?.Activate(this);
			//Game.IdEntityDic.Add(Id, this);
			Game.IdEntityDic[Id] = this;
			if (e.IsOneTurnActive)
			{
				c.Game.OneTurnEffectEnchantments.Add(this);
				IsOneTurnActive = true;
			}

			//if (c.Game.History)
			//{
				Zone = c.BoardZone;
				_tags = new EntityData(entityData: e._tags);
			//}

			if (Power?.Enchant?.RemoveWhenPlayed ?? false)
			{
				Enchant.RemoveWhenPlayedTrigger.Activate(this);
			}

			// HsServer (soak 900802): the creator may be gone from the game by now (a player entity, an entity removed): a copy keeps none
			if ((e._creator ?? (e.Game.IdEntityDic.TryGetValue(e._creatorId, out IPlayable made) ? made : null)) is Enchantment eCreator)
				_creator = eCreator.Clone(in c);
		}

		public int this[GameTag t]
		{
			get => _tags.TryGetValue(t, out int value) ? value : 0;
			set
			{
				if (_history && Entity.InHistory(t))
					if (value != this[t])
						Game.PowerHistory.Add(PowerHistoryBuilder.TagChange(Id, t, value));
				_tags[t] = value;
			}
		}

		/// <summary>
		/// The entity that this enchantment is attached to.
		/// </summary>
		//public IEntity Target { get; private set; }
		public IEntity Target
		{
			get => _target ?? (_target = Game.IdEntityDic[_targetId]);
			private set
			{
				_targetId = value.Id;
				_target = value;
			}
		}

		/// <summary>
		/// <see cref="SabberStoneCore.Model.Card"/> information captured in this instance.
		/// </summary>
		public Card CapturedCard
		{
			get => _capturedCard;
			set
			{
				_capturedCard = value;
				if (value != null && Game.History && (Card.Text?.Contains("{0}") ?? false))
				{
					Card c = Card.Clone();
					c.Text = String.Format(c.Text, value.Name);
					Card = c;
				}
			}
		}

		public IPlayable Creator
		{
			get => _creator ?? (_creator = Game.IdEntityDic[_creatorId]);
			private set
			{
				_creatorId = value.Id;
				_creator = value;
			}
		}

		public Controller Controller
		{
			get => _controller ?? (_controller = Game.ControllerById(_controllerId));
			set
			{
				_controllerId = value.Id;
				_controller = value;
			}
		}

		public bool IsOneTurnActive { get; private set; }

		public int ScriptTag1 => this[GameTag.TAG_SCRIPT_DATA_NUM_1];

		public int ScriptTag2 => this[GameTag.TAG_SCRIPT_DATA_NUM_2];

		/// <summary>
		/// Creates and adds a new Enchantment entity to the given Controller's Game.
		/// </summary>
		/// <param name="controller">The controller of the enchantment.</param>
		/// <param name="creator">The entity who creates the enchantment.</param>
		/// <param name="target">The entity who is subjected to the enchantment.</param>
		/// <param name="card">The card from which the enchantment must be derived.</param>
		/// <returns>The resulting enchantment entity.</returns>
		public static Enchantment GetInstance(in Controller controller, in IPlayable creator, in IEntity target, in Card card, int num1 = 0, int num2 = 0)
		{
			int id = controller.Game.NextId;

			var tags = new EntityData(4);

			var instance = new Enchantment(in controller, in card, in tags, in id)
			{
				Creator = creator,
				Target = target,
			};

			if (target.AppliedEnchantments == null)
				target.AppliedEnchantments = new List<Enchantment> {instance};
			else
				target.AppliedEnchantments.Add(instance);

			//controller.Game.IdEntityDic.Add(instance.Id, instance);
			controller.Game.IdEntityDic[instance.Id] = instance;

			if (controller.Game.History)
			{
				tags.Add(GameTag.ENTITY_ID, id);
				tags.Add(GameTag.ZONE, (int)Enums.Zone.SETASIDE);
				tags.Add(GameTag.CONTROLLER, controller.PlayerId);

				controller.Game.PowerHistory.Add(new PowerHistoryFullEntity
				{
					Entity = new PowerHistoryEntity
					{
						Id = instance.Id,
						Name = "",
						Tags = tags.ToDictionary(k => k.Key, k => k.Value)
					}
				});

				if (!(target.Zone is DeckZone))
				{
					// HsServer: live's enchantment reveal (Frostwolf Warlord's CS2_226e, Cold Blood's CS2_073e2): controller, type,
					// its amount (TAG_SCRIPT_DATA_NUM_1/2: the client's "+3/+3" text), what it's on, zone, id, class, creator,
					// the creator's card (CREATOR_DBID) and SPAWN_TIME_COUNT; no zero tags (live sends none)
					var gameTags = new Dictionary<GameTag, int>
					{
						{GameTag.CONTROLLER, controller.PlayerId},
						{GameTag.CARDTYPE, (int) CardType.ENCHANTMENT},
					};
					if (num1 > 0) gameTags.Add(GameTag.TAG_SCRIPT_DATA_NUM_1, num1);
					if (num1 > 0 && num2 > 0) gameTags.Add(GameTag.TAG_SCRIPT_DATA_NUM_2, num2);
					gameTags.Add(GameTag.ATTACHED, target.Id);
					gameTags.Add(GameTag.ZONE, (int) Enums.Zone.SETASIDE);
					gameTags.Add(GameTag.ENTITY_ID, instance.Id);
					if ((int)card.Class > 0) gameTags.Add(GameTag.CLASS, (int)card.Class);
					if (creator != null)
					{
						gameTags.Add(GameTag.CREATOR, creator.Id);
						if (creator.Card?.AssetId > 0) gameTags.Add(GameTag.CREATOR_DBID, creator.Card.AssetId);
					}
					gameTags.Add((GameTag)1556, 1); // SPAWN_TIME_COUNT
					// its card's trigger flash (live: Blessing of Wisdom's EX1_363e reveal has TRIGGER_VISUAL, from the client data)
					if (card[GameTag.TRIGGER_VISUAL] > 0) gameTags.Add(GameTag.TRIGGER_VISUAL, card[GameTag.TRIGGER_VISUAL]);
					if (card[GameTag.TAG_ONE_TURN_EFFECT] == 1)
						gameTags.Add(GameTag.TAG_ONE_TURN_EFFECT, 1);
					controller.Game.PowerHistory.Add(new PowerHistoryShowEntity
					{
						Entity = new PowerHistoryEntity
						{
							Id = instance.Id,
							Name = instance.Card.Id,
							Tags = gameTags
						}
					});
				}
				
				instance[GameTag.ZONE] = (int)Enums.Zone.PLAY;
			}

			if (card[GameTag.TAG_ONE_TURN_EFFECT] == 1)
			{
				instance.IsOneTurnActive = true;
				controller.Game.OneTurnEffectEnchantments.Add(instance);
			}


			instance.Zone = controller.BoardZone;
			instance.OrderOfPlay = controller.Game.NextOop;
			//	323 = 1

			if (card.Power?.DeathrattleTask != null)
				((IPlayable)target).HasDeathrattle = true;

			controller.Game.Log(LogLevel.VERBOSE, BlockType.ACTION, "Enchantment",
				!controller.Game.Logging ? "" : $"Enchantment {card} created by {creator} is added to {target}.");

			if (num1 > 0)
			{
				tags.Add(GameTag.TAG_SCRIPT_DATA_NUM_1, num1);
				if (num2 > 0)
					tags.Add(GameTag.TAG_SCRIPT_DATA_NUM_2, num2);
			}

			return instance;
		}

		public Enchantment Clone(in Controller controller)
		{
			return new Enchantment(in controller, this);
		}

		public void Remove() => Remove(false);

		/// <summary>HsServer: a spent enchantment leaves for the GRAVEYARD, as live sends it at the end of its own trigger's block
		/// (live-20261007-vfx-decks 265713-265720, Runic Adornment's TTN_845e after its card was cast; live-20261004-night 38418,
		/// Gorehowl's EX1_411e): the 1068 = 4 pulse, ZONE GRAVEYARD, 1234 = the entity it was on, HIDE_ENTITY.</summary>
		public void RemoveToGraveyard() => Remove(true);

		private void Remove(bool toGraveyard)
		{
			if (Game.History)
			{
				if (Zone == null)
					Zone = Controller.BoardZone;
				if (toGraveyard)
				{
					Game.PowerHistory.Add(PowerHistoryBuilder.TagChange(Id, (GameTag)1068, 4));
					Game.PowerHistory.Add(PowerHistoryBuilder.TagChange(Id, (GameTag)1068, 0));
					this[GameTag.ZONE] = (int)Enums.Zone.GRAVEYARD;
					Game.PowerHistory.Add(PowerHistoryBuilder.TagChange(Id, (GameTag)1234, Target?.Id ?? 0));
					Game.PowerHistory.Add(new PowerHistoryHideEntity { EntityID = Id, Zone = Enums.Zone.GRAVEYARD });
				}
				else
				{
					Game.PowerHistory.Add(PowerHistoryBuilder.HideEntity(this));
					this[GameTag.ZONE] = (int)Enums.Zone.REMOVEDFROMGAME;
				}
			}

			// Activate enchantment deathrattle task.
			if (Power?.DeathrattleTask != null && Target.Zone is GraveyardZone &&
			    Target.Controller.ControllerAuraEffects[HsTag.DEATHRATTLES_CANT_TRIGGER] == 0 && // Deathwarden
			    Target.Controller.Opponent.ControllerAuraEffects[HsTag.DEATHRATTLES_CANT_TRIGGER] == 0)
			{
				// (Scourge Troll: "Deathrattles given to this minion trigger twice")
				int given = Target is Minion troll && !troll.IsSilenced && troll.Card.Power?.GivenDeathrattlesTwice == true ? 2 : 1;
				for (int times = Target.Controller.DeathrattleTimes(Target as IPlayable) * given; times > 0; times--) // Rivendare
					// its own timestamp orders it among the death's responders (rules A3)
					Game.TaskQueue.Enqueue(Power.DeathrattleTask, Target.Controller, Target, this, 0, 0, OrderOfPlay);
			}

			OngoingEffect?.Remove();
			ActivatedTrigger?.Remove();

			Target.AppliedEnchantments.Remove(this);

			if (IsOneTurnActive)
				Game.OneTurnEffectEnchantments.Remove(this);

			Game.Log(LogLevel.VERBOSE, BlockType.ACTION, "Enchantment",
				!Game.Logging ? "" : $"Enchantment {this} is removed from {Target}.");
		}

		public Power Power => Card.Power;
		public Trigger ActivatedTrigger { get; set; }

		public void Reset()
		{
			_tags.Clear();
		}

		public override string ToString()
		{
			return $"'{Card.Name}[{Id}]'";
		}
	}

	public partial class Enchantment
	{
		protected readonly bool _history;
		protected readonly bool _logging;

		public int Id { get; }
		public int OrderOfPlay { get; set; }
		public Game Game { get; set; }
		public Card Card { get; set; }
		public IZone Zone { get; set; }
		public IAura OngoingEffect { get; set; }
		public IEnumerable<ICharacter> ValidPlayTargets => null;
		public bool ChooseOne { get; set; }
		public bool IsPlayable => false;
		public bool IsPlayableByPlayer => false;
		public bool IsPlayableByCardReq => false;
		public bool IsIgnoreDamage { get; set; }
		public bool Combo => false;
		public int Cost { get; set; }
		public bool ToBeDestroyed { get; set; }
		public int CardTarget { get; set; }
		public int ZonePosition { get; set; }
		public bool IsExhausted { get; set; }
		public int Overload { get; set; }
		public bool HasDeathrattle { get; set; }
		/// <summary>HsServer: the enchantment's own Lifesteal (client card tag LIFESTEAL: Blood Boil's Boiling Blood deals its damage
		/// with Lifesteal) or one set by a script.</summary>
		public bool HasLifeSteal { get => _lifeSteal || Card.LifeSteal; set => _lifeSteal = value; }
		private bool _lifeSteal;
		public bool IsEcho => false;
		public bool HasOverkill => false;
		public IPlayable[] ChooseOnePlayables { get; set; }
		public AuraEffects AuraEffects { get; set; }
		public IDictionary<GameTag, int> NativeTags => _tags;
		public List<Enchantment> AppliedEnchantments { get; set; }
		public bool HasAnyValidPlayTargets { get; }

		public string Hash(params GameTag[] ignore)
		{
			throw new NotImplementedException();
		}

		public List<IPlayable> HandTargets() => null;

		public bool IsValidTarget(IPlayable target) => throw new NotImplementedException();

		public bool IsValidPlayTarget(ICharacter target)
		{
			throw new NotImplementedException();
		}

		public void Destroy()
		{
			throw new NotImplementedException();
		}

		public void ActivateTask(in PowerActivation activation, in IPlayable target = null, in int chooseOne = 0, in IPlayable source = null)
		{
			throw new NotImplementedException();
		}

		public IEnumerator<KeyValuePair<GameTag, int>> GetEnumerator()
		{
			throw new NotImplementedException();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		IPlayable IPlayable.Clone(in Controller controller) => Clone(in controller);
	}
}
