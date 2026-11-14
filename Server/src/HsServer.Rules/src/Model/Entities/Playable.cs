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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SabberStoneCore.Auras;
using SabberStoneCore.Enums;
using SabberStoneCore.Enchants;
using SabberStoneCore.Tasks;
using SabberStoneCore.Triggers;

namespace SabberStoneCore.Model.Entities
{
	/// <summary>
	/// Interface for an entity which can be played from <see cref="Zone.HAND"/> into
	/// <see cref="Zone.PLAY"/>, in other words: the entity can be 'played'.
	/// </summary>
	/// <seealso cref="IEntity" />
	public partial interface IPlayable : IEntity
	{
		/// <summary>Gets a value indicating whether this entity is playable. Some entities require specific
		/// requirements before they can be played. This method will process the requirements and produce
		/// a result for the current state of the game.
		/// </summary>
		/// <value><c>true</c> if this entity is playable; otherwise, <c>false</c>.</value>
		bool IsPlayable { get; }

		/// <summary>Gets a value indicating whether this entity is playable by player. Dynamic requirements
		/// are checked, eg: If a spell costs health instead of mana, this method will return <c>false</c>
		/// if the health cost would be higher than the available health.
		/// </summary>
		/// <value><c>true</c> if this entity is playable by player; otherwise, <c>false</c>.</value>
		bool IsPlayableByPlayer { get; }

		/// <summary>Gets a value indicating whether this entity is playable by card requirements.
		/// Static requirements are checked.</summary>
		/// <value><c>true</c> if this entity is playable by card requirements; otherwise, <c>false</c>.</value>
		bool IsPlayableByCardReq { get; }

		/// <summary>Gets or sets a value indicating whether this entity is ignoring damage.
		/// This is usefull when damage related tags have to be edited without causing
		///	side-effects on this entities health and destruction status.
		/// </summary>
		/// <value><c>true</c> if this instance is ignoring damage; otherwise, <c>false</c>.</value>
		bool IsIgnoreDamage { get; set; }

		/// <summary>Gets a value indicating whether this <see cref="IPlayable"/> has a combo effect.</summary>
		/// <value><c>true</c> if combo; otherwise, <c>false</c>.</value>
		bool Combo { get; }

		/// <summary>Gets or sets the amount of resources this entity costs to play.</summary>
		/// <value>The cost.</value>
		int Cost { get; set; }

		///// <summary>Gets or sets the number of turns this entity is in <see cref="Zone.PLAY"/>.</summary>
		///// <value>The number of turns in play.</value>
		//int NumTurnsInPlay { get; set; }

		/// <summary>Mark this entity for destruction.</summary>
		/// <returns>Returns itself.</returns>
		void Destroy();

		/// <summary>
		/// Gets a value indicating whether this entity will be destroyed during the next cleanup
		/// phase.
		/// </summary>
		/// <value><c>true</c> if pending destruction; otherwise, <c>false</c>.</value>
		bool ToBeDestroyed { get; set; }

		/// <summary>
		/// Activates <see cref="SabberStoneCore.Enchants.Power"/> and queues up tasks.
		/// </summary>
		/// <param name="activation"></param>
		/// <param name="target"></param>
		/// <param name="chooseOne"></param>
		/// <param name="source"></param>
		void ActivateTask(in PowerActivation activation, in IPlayable target = null, in int chooseOne = 0, in IPlayable source = null);

		/// <summary>Gets or sets the entity ID target.</summary>
		/// <value><see cref="IEntity.Id"/></value>
		int CardTarget { get; set; }

		/// <summary>
		/// Playable zoneposition.
		/// </summary>
		int ZonePosition { get; set; }

		/// <summary>
		/// Playable is exhausted. <c>true</c> indicates that the entity cannot
		/// perform it's effect anymore.
		/// eg: <see cref="Minion"/>s cannot attack when exhausted.
		/// eg: <see cref="HeroPower"/>s cannot be triggered when exhausted.
		/// </summary>
		bool IsExhausted { get; set; }

		/// <summary>
		/// Playable is overloading mana.
		/// </summary>
		int Overload { get; }

		/// <summary>
		/// Playable has deathrattle.
		/// </summary>
		bool HasDeathrattle { get; set; }

		/// <summary>
		/// Playable has lifesteal.
		/// </summary>
		bool HasLifeSteal { get; set; }

		/// <summary>
		/// Playable has Echo ability.
		/// </summary>
		bool IsEcho { get; }

		/// <summary>
		/// Playable has Overkill ability.
		/// </summary>
		bool HasOverkill { get; }

		/// <summary>
		/// Gets or sets a value indicating whether the target must be an option from the
		/// 'Choose One' set.
		/// </summary>
		/// <value><c>true</c> if 'Choose One' must be assumed; otherwise, <c>false</c>.</value>
		bool ChooseOne { get; }

		/// <summary>Gets the set of playables necessary for the 'choose one' action
		/// invoked by this entity. This list is hardcoded into the card data of this
		/// entity.
		/// The controller must have chosen one of these playables
		/// before this entity can be moved into <see cref="Zone.PLAY"/>.
		/// </summary>
		/// <value>Playables to choose from.</value>
		IPlayable[] ChooseOnePlayables { get; set; }

		/// <summary>Gets or sets the Powers attached to this entity.
		/// These Powers are hardcoded into the card data, from which this entity
		/// is constructed.
		/// </summary>
		/// <value><see cref="Power"/></value>
		Power Power { get; }

		/// <summary>Gets the ranking order of the moment this entity was played.</summary>
		/// <value>The ranking order.</value>
		int OrderOfPlay { get; set; }

		/// <summary>
		/// Performs a deep copy of this <see cref="Controller"/> instance and returns the result.
		/// <para />
		/// Copied instance and all entities in its zones are deep copied to the target <see cref="Controller"/> instance.
		/// <para />
		/// Also, the copied <see cref="IPlayable"/> is added to the <see cref="Game.IdEntityDic"/> of the target <see cref="Controller"/>'s <see cref="Game"/>.
		/// </summary>
		/// <param name="controller">The target <see cref="Controller"/> instance.</param>
		/// <returns></returns>
		IPlayable Clone(in Controller controller);

		/// <summary>
		/// Gets or sets Ongoing effect of this entity.
		/// Ongoing effects are minion, weapon, and boss Hero Power abilities
		/// which grant special effects on an ongoing basis.
		/// Ongoing effects are often referred to as auras, particularly
		/// those which grant temporary enchantments to other targets.
		/// https://hearthstone.gamepedia.com/Ongoing_effect
		/// Here, OngoingEffect includes <see cref="OngoingEnchant"/> too.
		/// </summary>
		IAura OngoingEffect { get; set; }

		/// <summary>
		/// Gets or sets current activated <see cref="Trigger"/> of this entity.
		/// Nullifying this field does not mean deactivation of the trigger.
		/// Use <see cref="Trigger.Remove()"/> instead.
		/// </summary>
		Trigger ActivatedTrigger { get; set; }

		#region obsoleted
		// Unused
		// bool TurnStart { get; set; }

		/// <summary>Applies on of the Powers defined on this entity.</summary>
		/// <param name="activation">The activation trigger for the enchants.</param>
		/// <param name="zoneType">Type of the zone the enchant will perform on.</param>
		/// <param name="target">The target, mostly of type <see cref="ICharacter"/>.</param>
		//void ApplyPowers(PowerActivation activation, Zone zoneType, IPlayable target = null);

		/// <summary>Stores the next Order Of Play index held by the <see cref="Game"/> instance.
		///	Order of play is important because it's the order in which effects are resolved.
		/// </summary>
		/// <param name="type">The type of this entity, stringified.</param>
		//void SetOrderOfPlay(string type);

		///// <summary>
		///// Playable has just been played.
		///// </summary>
		//bool JustPlayed { get; set; }

		///// <summary>
		///// Playable has been summoned.
		///// </summary>
		//bool IsSummoned { get; set; }
		#endregion
	}

	/// <summary>
	/// Base implementation of the <see cref="IPlayable"/> interface.
	/// </summary>
	/// <seealso cref="IPlayable" />
	/// <seealso cref="Entity"/>
	public abstract partial class Playable : Entity, IPlayable
	{
		/// <summary>HsServer: the card's own rule for costing Health instead of Mana (Power.CostsHealthWhile, Death Metal Knight),
		/// or an effect that made this card cost Health (its CARD_COSTS_HEALTH: Deathbringer Saurfang's returned card).</summary>
		internal static bool CostsHealth(IPlayable p) => p.Card.Power?.CostsHealthWhile?.Invoke(p) == true || p[GameTag.CARD_COSTS_HEALTH] > 0 ||
			NextPaladinMinionCostsHealth(p) ||
			p[HsTag.CARD_ALTERNATE_COST] == 1; // HsServer (TIME_TRAVEL): the card data's TAG_CARD_ALTERNATE_COST HEALTH (Blood Draw)

		/// <summary>HsServer: Blood Crusader — "Your next Paladin minion this turn costs Health instead of Mana".</summary>
		internal static bool NextPaladinMinionCostsHealth(IPlayable p) =>
			p is Minion && p.Zone?.Type == SabberStoneCore.Enums.Zone.HAND && p.Card.HasClass(CardClass.PALADIN) &&
			p.Game.Turn > 0 && p.Controller[HsTag.NEXT_PALADIN_MINION_COSTS_HEALTH] == p.Game.Turn;

		/// <summary>HsServer: the card costs Corpses instead of Mana (client CARD_ALTERNATE_COST = 3, TAG_CARD_ALTERNATE_COST
		/// CORPSES: Reanimated Pterrordax; set by an enchantment: Exarch Maladaar's "Costs Corpses"). Paid from the owner's
		/// Corpses; not playable with fewer (play error REQ_MINIMUM_CORPSES).</summary>
		internal static bool CostsCorpses(IPlayable p) => p[HsTag.CARD_ALTERNATE_COST] == CorpseAlternateCost ||
			p.Zone?.Type == SabberStoneCore.Enums.Zone.HAND && p.Controller[HsTag.NEXT_CARD_COSTS_CORPSES] == p.Game.Turn && p.Game.Turn > 0;

		internal const int CorpseAlternateCost = 3;

		/// <summary>HsServer: TAG_CARD_ALTERNATE_COST ARMOR (2): the card costs Armor instead of Mana (Anub'Rekhan's RLK_659e2).
		/// Paid from the hero's Armor; not playable with less.</summary>
		public const int ArmorAlternateCost = 2;

		internal static bool CostsArmor(IPlayable p) => p[HsTag.CARD_ALTERNATE_COST] == ArmorAlternateCost;

		/// <summary>HsServer: whether <paramref name="me"/> can pay <paramref name="p"/>'s cost (Mana, or what it costs instead:
		/// Health, Corpses, the opponent's Health, Armor) — 0, or the option error. The one check for both the engine
		/// (IsPlayable) and the options sent (PlayErrors): the options had no Armor branch and showed every Armor-cost card as
		/// unaffordable (Anub'Rekhan, user 2026-10-08, local-test-20261008-013810 2998: Feugen 5 with 5 Armor).</summary>
		internal static PlayReq CostError(Controller me, IPlayable p)
		{
			bool paidOtherwise = (p.AuraEffects?.CardCostHealth ?? false) || CostsHealth(p) ||
			                     p is Spell && me.ControllerAuraEffects[GameTag.SPELLS_COST_HEALTH] == 1;
			if (!paidOtherwise && CostsCorpses(p)) // costs Corpses instead of Mana
			{
				if (me.Corpses < p.Cost) return (PlayReq)133; // REQ_MINIMUM_CORPSES
				paidOtherwise = true;
			}
			if (!paidOtherwise && CostsOpponentHealth(p)) // costs the opponent's Health (Agamaggan; error code: doubt)
			{
				if (p.Cost > me[HsTag.NEXT_CARD_COSTS_OPPONENT_HEALTH]) return PlayReq.REQ_ENOUGH_MANA;
				paidOtherwise = true;
			}
			if (!paidOtherwise && CostsArmor(p)) // costs Armor instead of Mana (error code: doubt)
			{
				if (me.Hero.Armor < p.Cost) return PlayReq.REQ_ENOUGH_MANA;
				paidOtherwise = true;
			}
			return !paidOtherwise && me.ManaToSpend < p.Cost ? PlayReq.REQ_ENOUGH_MANA : 0;
		}

		/// <summary>HsServer: TAG_CARD_ALTERNATE_COST OPPONENTS_HEALTH (4): "The next card you play costs your OPPONENT'S Health
		/// instead of Mana (up to 10)" (Agamaggan; player tag NEXT_CARD_COSTS_OPPONENT_HEALTH = the cap). Wiki Agamaggan: "the
		/// opponent does not need to have a Health total greater than the final mana cost"; "If a card costs more than 10, it
		/// cannot be played while Agamaggan's effect is active".</summary>
		public const int OpponentHealthAlternateCost = 4;

		internal static bool CostsOpponentHealth(IPlayable p) =>
			p.Zone?.Type == SabberStoneCore.Enums.Zone.HAND && p.Controller[HsTag.NEXT_CARD_COSTS_OPPONENT_HEALTH] > 0;

		/// <summary>HsServer: the card is the left- or right-most card in its hand (the Outcast slots).</summary>
		public static bool InOuterHandSlot(IPlayable p) =>
			p.Zone is Zones.HandZone hand && (p.ZonePosition == 0 || p.ZonePosition == hand.Count - 1);

		/// <summary>HsServer: a rule tag stops this card from being played (ONLY_OUTER_CARDS_PLAYABLE: Prosecutor Mel'tranix).</summary>
		internal static bool IsPlayRestricted(IPlayable p) =>
			p[HsTag.LITERALLY_UNPLAYABLE] > 0 || p.Controller[HsTag.ONLY_OUTER_CARDS_PLAYABLE] > 0 && !InOuterHandSlot(p) ||
			// HsServer (events): "You can only play minions / spells for the rest of the turn"
			p.Controller[HsTag.ONLY_PLAY_CARDTYPE] > 0 && (int)p.Card.Type != p.Controller[HsTag.ONLY_PLAY_CARDTYPE];

		/// <summary>Initializes a new instance of the <see cref="Playable"/> class.</summary>
		/// <param name="controller">The controller.</param>
		/// <param name="card">The card.</param>
		/// <param name="tags">The tags.</param>
		/// <autogeneratedoc />
		protected Playable(in Controller controller, in Card card, in IDictionary<GameTag, int> tags, in int id)
			: base(controller.Game, in card, in tags, in id)
		{
			Controller = controller;

			if (_history)
				tags[GameTag.CONTROLLER] = controller.PlayerId;
		}

		/// <inheritdoc />
		/// <summary>
		/// A copy constructor. This constructor is only used to the inherited copy constructors.
		/// </summary>
		/// <param name="controller">The target <see cref="T:SabberStoneCore.Model.Entities.Controller" /> instance.</param>
		/// <param name="playable">The source <see cref="T:SabberStoneCore.Model.Entities.Playable`1" /></param>
		protected Playable(in Controller controller, in Playable playable) : base(controller.Game, playable)
		{
			Controller = controller;

			controller.Game.IdEntityDic[playable.Id] = this;

			//playable.OngoingEffect?.Clone(this);
			playable.ActivatedTrigger?.Activate(this, cloning: true);

			// Cloning applied enchantments.
			{
				List<Enchantment> originalEnchantments = playable.AppliedEnchantments;
				if (originalEnchantments != null)
				{
					var enchantments = new List<Enchantment>(originalEnchantments.Count);
					foreach (Enchantment p in originalEnchantments)
					{
						enchantments.Add(p.Clone(controller));
					}
					AppliedEnchantments = enchantments;
				}
			}

			if (playable.ChooseOnePlayables != null)
			{
				ChooseOnePlayables = new IPlayable[2];
				Array.Copy(playable.ChooseOnePlayables, ChooseOnePlayables, 2);
			}

			_exhausted = playable._exhausted;
			_zonePosition = playable._zonePosition;

			_modifiedCost = playable._modifiedCost;
			_costManager = playable._costManager?.Clone();
		}

		/// <summary>
		/// Gets the set of playables necessary for the 'choose one' action
		/// invoked by this entity. This list is hardcoded into the card data of this
		/// entity.
		/// The controller must have chosen one of these playables
		/// before this entity can be moved into <see cref="F:SabberStoneCore.Enums.Zone.PLAY" />.
		/// </summary>
		/// <value>Playables to choose from.</value>
		/// <autogeneratedoc />
		public IPlayable[] ChooseOnePlayables { get; set; }

		/// <summary>
		/// Gets or sets Ongoing effect of this entity.
		/// Ongoing effects are minion, weapon, and boss Hero Power abilities
		/// which grant special effects on an ongoing basis.
		/// Ongoing effects are often referred to as auras, particularly
		/// those which grant temporary enchantments to other targets.
		/// https://hearthstone.gamepedia.com/Ongoing_effect
		/// Here, OngoingEffect includes <see cref="OngoingEnchant"/> too.
		/// </summary>
		public IAura OngoingEffect { get; set; }

		/// <summary>
		/// Gets or sets current activated <see cref="Trigger"/> of this entity.
		/// Nullifying this field does not mean deactivation of the trigger.
		/// Use <see cref="Trigger.Remove()"/> instead.
		/// </summary>
		public Trigger ActivatedTrigger { get; set; }

		/// <summary>
		/// Gets or sets the Powers attached to this entity.
		/// These Powers are hardcoded into the card data, from which this entity
		/// is constructed.
		/// </summary>
		/// <value>
		/// <see cref="T:SabberStoneCore.Enchants.Power" />
		/// </value>
		/// <autogeneratedoc />
		//public List<Power> Powers => Card.Powers;
		public Power Power => Card.Power;

		/// <summary>HsServer: this Choose One card (or power) runs both its options' effects — its owner's "both" (Fandral Staghelm,
		/// Cenarion Hold's cards only) or its own CHOOSE_BOTH (Raid Negotiator's Discover) — and has no combined task of its own
		/// (Druid of the Claw's combined form is scripted as its own task). Its play then targets what its options target
		/// (Controller options, IsValidPlayTarget).</summary>
		public bool ChooseOneBoth => ChooseOne && ChooseOnePlayables != null &&
		                             ((this is HeroPower ? Controller.ChooseBoth : Controller.ChooseBothCards) || this[GameTag.CHOOSE_BOTH] > 0);

		/// <summary>HsServer: <see cref="ChooseOneBoth"/> with no combined task of its own: both options run (ActivateTask).</summary>
		public bool RunsBothOptions => ChooseOneBoth && Power?.PowerTask == null;

		public void ActivateTask(in PowerActivation activation = PowerActivation.POWER, in IPlayable target = null, in int chooseOne = 0, in IPlayable source = null)
		{
			// HsServer (2026-10-07): a card with the CHOOSE_ONE tag but no options is no Choose One card (Disciple of Eonar names
			// the keyword in its text: a second Disciple spent the first one's "both effects" on itself, and "both" on it
			// would index missing options)
			if (ChooseOne && ChooseOnePlayables != null)
			{
				// HsServer: engine rule instead of a card-id list — a Choose One card that has a task of its own scripts
				// its "choose both" version as that task (Druid of the Claw's combined form, Fatespinner, Tending
				// Tauren); only cards without one run both options.
				// (also one card's own CHOOSE_BOTH: Embrace of Nature's Forged draw)
				// (HsServer, WONDERS: Cenarion Hold's "next Choose One card this turn" — cards only: Controller.ChooseBothCards)
				bool isCard = !(this is HeroPower);
				bool both = ChooseOneBoth;
				// HsServer (TITANS): "Your next Choose One card or power has both effects combined" (Disciple of Eonar) is spent by
				// it — played, cast by an effect or Cast When Drawn too (wiki Disciple of Eonar bug fixes)
				// (a flag: every Disciple's "next Choose One card" is this one — TTN_503)
				if (activation == PowerActivation.POWER && Controller[HsTag.NEXT_CHOOSE_ONE_BOTH] > 0)
					Controller[HsTag.NEXT_CHOOSE_ONE_BOTH] = 0;
				// (Cenarion Hold's is spent by the next Choose One card only)
				if (isCard && activation == PowerActivation.POWER && Controller[HsTag.CHOOSE_ONE_CARDS_BOTH_TURN] > 0)
					Controller[HsTag.CHOOSE_ONE_CARDS_BOTH_TURN] = 0;
				if (both && Power?.PowerTask == null)
				{
					// both on the one target (wiki Fandral Staghelm: "Choose One cards with targeted effects will use the same target
					// for both effects"); a targeted option the target isn't legal for is left out ("if cast on an illegal target,
					// the effects will not be combined")
					foreach (IPlayable option in ChooseOnePlayables)
						if (target == null || option.Card.TargetingType == TargetingType.None || option.IsValidPlayTarget((ICharacter)target))
							option.ActivateTask(activation, target, chooseOne, this);
					return;
				}

				if (!both && chooseOne > 0)
				{
					ChooseOnePlayables[chooseOne - 1].ActivateTask(activation, target, chooseOne, this);
					return;
				}
			}

			if (Power == null) return;

			ISimpleTask task = null;
			switch (activation)
			{
				case PowerActivation.POWER:
					task = Power.PowerTask;
					break;
				case PowerActivation.DEATHRATTLE:
					// HsServer: Deathwarden — "Deathrattles can't trigger"
					if (Controller.ControllerAuraEffects[HsTag.DEATHRATTLES_CANT_TRIGGER] > 0 ||
					    Controller.Opponent.ControllerAuraEffects[HsTag.DEATHRATTLES_CANT_TRIGGER] > 0) return;
					task = Power.DeathrattleTask;
					break;
				case PowerActivation.COMBO:
					task = Power.ComboTask;
					break;
			}
			if (task == null) return;

			// clone task here
			//ISimpleTask clone = task.Clone();
			//clone.Game = source?.Game ?? Game;
			//clone.Controller = source?.Controller ?? Controller;
			//clone.Source = source ?? this;
			//clone.Target = target;

			// HsServer: a Deathrattle fires as a TRIGGER block with TriggerKeyword=DEATHRATTLE (live).
			int keyword = activation == PowerActivation.DEATHRATTLE ? (int)GameTag.DEATHRATTLE : 0;
			if (source != null)
			{
				source.Game.TaskQueue.Enqueue(in task, source.Controller, source, target, keyword);
			}
			else
				Game.TaskQueue.Enqueue(in task, Controller, this, target, keyword);
		}

		/// <summary>
		/// Stores the next Order Of Play index held by the <see cref="T:SabberStoneCore.Model.Game" /> instance.
		/// Order of play is important because it's the order in which effects are resolved.
		/// </summary>
		/// <param name="type">The type of this entity, stringified.</param>
		/// <autogeneratedoc />
		//public void SetOrderOfPlay(string type)
		//{
		//	if (type.Equals("PLAY")
		//	 || type.Equals("SECRET_OR_QUEST")
		//	 || type.Equals("WEAPON"))
		//	{
		//		OrderOfPlay = Game.NextOop;
		//	}
		//}

		/// <summary>Mark this entity for destruction.</summary>
		/// <returns>Returns itself.</returns>
		/// <autogeneratedoc />
		/// <summary>HsServer: rules A5/A6 — set by a destroy effect (final). An entity only "mortally wounded" (0 Health
		/// from damage) can still be saved by healing or a Health buff until the Death Creation Step.</summary>
		internal bool DestroyedByEffect;

		/// <summary>HsServer: marked for death only because its Health reached 0 (not by a destroy effect).</summary>
		internal bool MortallyWounded;

		/// <summary>HsServer: killed by moving into a full zone (rules C2 Z1): already in the Graveyard; only its Death
		/// Event is still to be resolved in the next Death Phase.</summary>
		internal bool InstantlyRemoved;

		public virtual void Destroy()
		{
			DestroyedByEffect = true;
			ToBeDestroyed = true;
			Game.Log(LogLevel.VERBOSE, BlockType.PLAY, "Playable", !Game.Logging ? "" : $"{this} just got set to be destroyed.");
			//return this;
		}

		/// <summary>
		/// Gets a value indicating whether this entity is playable. Some entities require specific
		/// requirements before they can be played. This method will process the requirements and produce
		/// a result for the current state of the game.
		/// </summary>
		/// <value><c>true</c> if this entity is playable; otherwise, <c>false</c>.</value>
		/// <autogeneratedoc />
		public bool IsPlayable => IsPlayableByPlayer && IsPlayableByCardReq;

		/// <summary>
		/// Gets a value indicating whether this entity is playable by player. Dynamic requirements
		/// are checked, eg: If a spell costs health instead of mana, this method will return <c>false</c>
		/// if the health cost would be higher than the available health.
		/// </summary>
		/// <value><c>true</c> if this entity is playable by player; otherwise, <c>false</c>.</value>
		/// <autogeneratedoc />
		public virtual bool IsPlayableByPlayer
		{
			get
			{
				if (Zone?.Type == SabberStoneCore.Enums.Zone.HAND && IsPlayRestricted(this)) return false; // HsServer
				if (Card.HideStat)
				{
					Game.Log(LogLevel.VERBOSE, BlockType.PLAY, "Playable",
						!Game.Logging ? "" : $"{this} isn't playable, because can't play card with HIDE_STAT");
					return false;
				}

				// check if player has enough mana to play card (HsServer: or what it costs instead — CostError, shared with the options)
				if (CostError(Controller, this) != 0)
				{
					Game.Log(LogLevel.VERBOSE, BlockType.PLAY, "Playable",
						!Game.Logging ? "" : $"{this} isn't playable, because not enough mana to pay cost.");
					return false;
				}

				// check if player is on turn
				if (Controller != Game.CurrentPlayer)
				{
					Game.Log(LogLevel.VERBOSE, BlockType.PLAY, "Playable",
						!Game.Logging ? "" : $"{this} isn't playable, because player not on turn.");
					return false;
				}

				// check if entity is in hand to be played
				if (Zone != Controller.HandZone && !(this is HeroPower))
				{
					Game.Log(LogLevel.VERBOSE, BlockType.PLAY, "Playable",
						!Game.Logging ? "" : $"{this} isn't playable, because card not in hand.");
					return false;
				}

				return true;
			}
		}

		/// <summary>
		/// Gets a value indicating whether this entity is playable by card requirements.
		/// Static requirements are checked.
		/// </summary>
		/// <value><c>true</c> if this entity is playable by card requirements; otherwise, <c>false</c>.</value>
		/// <autogeneratedoc />
		public bool IsPlayableByCardReq
		{
			get
			{
				// check requirements on cards here 
				if (!Card.IsPlayableByCardReq(Controller))
				{
					if (_logging)
					{
						foreach (KeyValuePair<PlayReq, int> item in Card.PlayRequirements)
						{
							PlayReq req = item.Key;
							int param = item.Value;

							Game.Log(LogLevel.DEBUG, BlockType.PLAY, "Playable", !Game.Logging ? "" : $"{this} check PlayReq {req} ... !");

							switch (req)
							{
								case PlayReq.REQ_NUM_MINION_SLOTS:
									{
										if (Controller.BoardZone.IsFull)
										{
											Game.Log(LogLevel.VERBOSE, BlockType.PLAY, "Playable", !Game.Logging ? "" : $"Board is full can't summon new minion.");
											return false;
										}
										break;
									}
								case PlayReq.REQ_ENTIRE_ENTOURAGE_NOT_IN_PLAY:
									{
										var ids = Controller.BoardZone.Select(p => p.Card.Id).ToList();
										bool containsAll = true;
										for (int i = 0; i < Card.Entourage.Length; i++)
											containsAll &= ids.Contains(Card.Entourage[i]);

										if (containsAll)
										{
											Game.Log(LogLevel.VERBOSE, BlockType.PLAY, "Playable", !Game.Logging ? "" : $"All ready all entourageing cards summoned.");
											return false;
										}
										break;
									}
								case PlayReq.REQ_WEAPON_EQUIPPED:
									{
										if (Controller.Hero.Weapon == null)
										{
											Game.Log(LogLevel.VERBOSE, BlockType.PLAY, "Playable", !Game.Logging ? "" : $"Need a weapon to play this card.");
											return false;
										}
										break;
									}
								case PlayReq.REQ_MINIMUM_ENEMY_MINIONS:
									{
										if (Controller.Opponent.BoardZone.MinionCount < param) // (HsServer: minions only — rule BE2)
										{
											Game.Log(LogLevel.VERBOSE, BlockType.PLAY, "Playable", !Game.Logging ? "" : $"Need at least {param} enemy minions to play this card.");
											return false;
										}
										break;
									}
								case PlayReq.REQ_MINIMUM_TOTAL_MINIONS:
									{
										if (Controller.BoardZone.MinionCount + Controller.Opponent.BoardZone.MinionCount < param) // (HsServer: rule BE2)
										{
											Game.Log(LogLevel.VERBOSE, BlockType.PLAY, "Playable", !Game.Logging ? "" : $"Need at least {param} minions to play this card.");
											return false;
										}
										break;
									}
								case PlayReq.REQ_STEADY_SHOT:
									{
										// HsServer: the requirement sits on the Steady Shot Hero Powers (every skin and upgrade): it is met
										// when this is the current Hero Power (was: the old DS1h_292 id only)
										if (Controller.Hero.HeroPower != this)
										{
											Game.Log(LogLevel.VERBOSE, BlockType.PLAY, "Playable", !Game.Logging ? "" : $"Need steady shoot to be used.");
											return false;
										}
										break;
									}

								case PlayReq.REQ_FRIENDLY_MINION_DIED_THIS_GAME:
									{
										if (!Controller.DiedThisGameCards.Any(p => p.Card.Type == CardType.MINION)) // HsServer: the one "died" record
										{
											Game.Log(LogLevel.VERBOSE, BlockType.PLAY, "Playable", !Game.Logging ? "" : $"No friendly minions died this game.");
											return false;
										}
										break;
									}

								// implemented in Targeting
								case PlayReq.REQ_TARGET_FOR_COMBO:
								case PlayReq.REQ_FROZEN_TARGET:
								case PlayReq.REQ_MINION_OR_ENEMY_HERO:
								case PlayReq.REQ_TARGET_MAX_ATTACK:
								case PlayReq.REQ_MINION_TARGET:
								case PlayReq.REQ_FRIENDLY_TARGET:
								case PlayReq.REQ_ENEMY_TARGET:
								case PlayReq.REQ_UNDAMAGED_TARGET:
								case PlayReq.REQ_DAMAGED_TARGET:
								case PlayReq.REQ_DAMAGED_TARGET_UNLESS_COMBO:
								case PlayReq.REQ_TARGET_WITH_RACE:
								case PlayReq.REQ_MUST_TARGET_TAUNTER:
								case PlayReq.REQ_TARGET_MIN_ATTACK:
								case PlayReq.REQ_TARGET_EXACT_ATTACK: // HsServer (WONDERS)
								case PlayReq.REQ_TARGET_WITH_DEATHRATTLE:
								case PlayReq.REQ_TARGET_WITH_BATTLECRY:
								case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_DRAGON_IN_HAND:
								case PlayReq.REQ_TARGET_IF_AVAILABE_AND_ELEMENTAL_PLAYED_LAST_TURN:
								case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_MINIMUM_FRIENDLY_MINIONS:
								case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_MINIMUM_FRIENDLY_SECRETS:
								case PlayReq.REQ_NONSELF_TARGET:
									break;

								// already implemented ... card.RequiresTarget and RequiresTargetIfAvailable
								case PlayReq.REQ_TARGET_TO_PLAY:
								case PlayReq.REQ_TARGET_IF_AVAILABLE:
									break;

								default:
									Game.Log(LogLevel.ERROR, BlockType.PLAY, "Playable", !Game.Logging ? "" : $"PlayReq {req} not in switch needs to be added (Playable)!");
									break;
							}
						}
					}

					return false;
				}

				// check if we need a target and there are some
				if (MustHaveTarget && !HasAnyValidPlayTargets && !Actions.Generic.DropsTarget(this)) // HsServer (WILD_WEST); TargetingCard: ADDITIONAL_PLAY_REQS
				{
					Game.Log(LogLevel.VERBOSE, BlockType.PLAY, "Playable", !Game.Logging ? "" : $"{this} isn't playable, because need valid target and we don't have one.");
					return false;
				}

				return true;
			}
		}

		public abstract IPlayable Clone(in Controller controller);

		public override string Hash(params GameTag[] ignore)
		{
			if (ActivatedTrigger == null && OngoingEffect == null)
				return base.Hash(ignore);

			var str = new StringBuilder(base.Hash(ignore));
			if (ActivatedTrigger != null)
			{
				str.Append("[TR:");
				str.Append(ActivatedTrigger);
				str.Append("]");
			}

			if (OngoingEffect != null)
			{
				str.Append("[OE:");
				str.Append(OngoingEffect);
				str.Append("]");
			}
			return str.ToString();
		}
	}


	public abstract partial class Playable
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
	{
		protected bool _exhausted;
		protected int _zonePosition;

		public int ZonePosition
		{
			get => _zonePosition;
			set
			{
				_zonePosition = value;
				if (_history)
					this[GameTag.ZONE_POSITION] = value + 1;
			}
		}

		public bool Combo => Card.Combo;

		public bool ChooseOne => Card.ChooseOne;

		//public bool JustPlayed
		//{
		//	get { return this[GameTag.JUST_PLAYED] == 1; }
		//	set { this[GameTag.JUST_PLAYED] = value ? 1 : 0; }
		//}

		//public bool IsSummoned
		//{
		//	get { return this[GameTag.SUMMONED] == 1; }
		//	set { this[GameTag.SUMMONED] = value ? 1 : 0; }
		//}

		public bool IsExhausted
		{
			get => _exhausted;
			set
			{
				_exhausted = value;
				if (_history || _logging)
				{
					this[GameTag.EXHAUSTED] = value ? 1 : 0;
				}
			}
		}

		public int Overload => Card.Overload;

		public virtual bool HasDeathrattle
		{
			get => false;
			set => throw new NotImplementedException();
		}

		public virtual bool HasLifeSteal
		{
			get => Card.LifeSteal;
			set => this[GameTag.LIFESTEAL] = value ? 1 : 0;
		}

		public bool IsEcho
		{
			get
			{
				if (AuraEffects?.Echo ?? false)
					return true;
				return Card.Echo;
			}
		}

		public virtual bool HasOverkill => Card.Overkill;
	}
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
