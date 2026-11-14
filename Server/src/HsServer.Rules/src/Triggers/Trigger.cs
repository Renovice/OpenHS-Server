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
using System.Linq;
using System.Collections.Generic;
using SabberStoneCore.Conditions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks;

namespace SabberStoneCore.Triggers
{
    public class Trigger
	{ 
		private readonly TriggerManager.TriggerHandler _processHandler;
		private readonly int _sourceId;
		private readonly TriggerType _triggerType;
		private readonly SequenceType _sequenceType;
		private readonly bool _isSecret;
		private bool _removed;

		protected readonly IPlayable _owner;

	    internal bool IsAncillaryTrigger;

		public readonly Game Game;
		/// <summary>
		/// Indicates the Zone at which the effect is triggered.
		/// </summary>
		public TriggerActivation TriggerActivation;
	    /// <summary>
	    /// Indicates which entities can trigger the effect.
	    /// </summary>
		public TriggerSource TriggerSource;
		/// <summary>
		/// Task to do when this effect is triggered.
		/// </summary>
		public ISimpleTask SingleTask;

	    /// <summary> Additional condition for trigger sources </summary>
	    public SelfCondition Condition;

		/// <summary> 
		/// This option is only meaningful when this the type of this trigger is <see cref="TriggerType.TURN_END"/> or <see cref="TriggerType.TURN_START"/>.
		/// </summary>
		/// <value>	true means the effect can be triggered at both player's turn.</value>
		public bool EitherTurn;

		/// <summary>HsServer: a "… while holding this" counter (the card's TAG_SCRIPT_DATA_NUM_1): it starts over each time the
		/// card enters the hand (user 2026-10-06: Commander Sivara replayed and back in hand counts again).</summary>
		public bool CountsWhileHeld;

		/// <summary>HsServer: the effect index of this trigger's TRIGGER block: which of the card's client trigger effects
		/// (CardDef m_TriggerEffectDefs) the client plays. Live numbers a card's triggers 0, 1, 2... (wire-conventions §2);
		/// 0 unless the card has several (Gnomelia: 0 = her cleave burst).</summary>
		public int EffectIndex;

		/// <summary>HsServer: the TriggerKeyword its TRIGGER block carries when the card's own keyword tags don't give it
		/// (live Echoing Ooze: ECHOING_OOZE_SPELL, the tag that also shows its slime).</summary>
		public GameTag? KeywordOverride;
		/// <summary>HsServer (2026-10-08): its TRIGGER block shows in the history without a keyword (live Amitus' damage cap: index 0,
		/// ShowInHistory, no TriggerKeyword — titans-vfx 190857).</summary>
		public bool InHistory;
		/// <summary>HsServer (2026-10-08): its TRIGGER block never shows in the history, whatever its keyword (live Resistance Aura's countdown:
		/// TRIGGER_VISUAL without ShowInHistory).</summary>
		public bool NotInHistory;
	    public bool FastExecution;
		/// <summary>HsServer: an attack trigger of the Proposed Attack Event (rule book, Combat: "triggers … that can change the defender, cause
		/// the attacker to become mortally wounded or leave play": Forgetful, Mogor, Shaku, Genzo) — with the Secrets, in order of play, before
		/// the Attack Event's triggers (Truesilver, Gorehowl, Cutpurse), which resolve once, against the final defender (Attack.cs).</summary>
		public bool ProposedAttack;
		/// <summary>HsServer: rules A8 — Priority "last within a death" (Redemption): among one death's responders this
		/// one resolves after the others, whatever the order of play. Set in the card's script.</summary>
		public bool LastInDeath;
		/// <summary>HsServer: rules A11 Rule 10b — this trigger's condition is about the moment of the Event (Chillmaw,
		/// Stargazer Luna's "right-most card in your hand") and isn't checked again when it resolves. Set in the script.</summary>
		public bool ConditionOnlyWhenQueued;
		/// <summary>HsServer: this trigger works while its minion is Dormant: it counts towards the awakening (Magtheridon's
		/// Warders). Every other trigger of a Dormant minion is off until it awakens (Imprisoned Celestial's Spellburst).</summary>
		public bool WhileDormant;
		/// <value> true means this trigger will be immediately disposed after triggered.</value>
	    public bool RemoveAfterTriggered;

		public TriggerType Type => _triggerType;

	    public Trigger(TriggerType type)
	    {
			_triggerType = type;

		    switch (type)
		    {
				case TriggerType.PLAY_CARD:
				case TriggerType.AFTER_PLAY_CARD:
					_sequenceType = SequenceType.PlayCard;
					return;
			    case TriggerType.PLAY_MINION:
			    case TriggerType.AFTER_PLAY_MINION:
				    _sequenceType = SequenceType.PlayMinion;
					return;
			    case TriggerType.CAST_SPELL:
			    case TriggerType.AFTER_CAST:
				    _sequenceType = SequenceType.PlaySpell;
					return;
				case TriggerType.TARGET:
					_sequenceType = SequenceType.Target;
					return;
				case TriggerType.TURN_END:
				case TriggerType.WORGEN_TRANSFORM:
					FastExecution = true;
					return;
		    }
	    }

	    protected Trigger(Trigger prototype, IEntity owner)
	    {
			Game = owner.Game;
			_sourceId = owner.Id;
		    _owner = (IPlayable)owner;
		    _triggerType = prototype._triggerType;
		    _sequenceType = prototype._sequenceType;
		    TriggerSource = prototype.TriggerSource;
			Condition = prototype.Condition;
		    SingleTask = prototype.SingleTask;
			if (SingleTask != null)
				SingleTask.IsTrigger = true;
		    EitherTurn = prototype.EitherTurn;
			FastExecution = prototype.FastExecution;
			InHistory = prototype.InHistory;
			NotInHistory = prototype.NotInHistory;
			ProposedAttack = prototype.ProposedAttack;
			LastInDeath = prototype.LastInDeath;
			ConditionOnlyWhenQueued = prototype.ConditionOnlyWhenQueued;
			WhileDormant = prototype.WhileDormant;
			RemoveAfterTriggered = prototype.RemoveAfterTriggered;
		    _isSecret = prototype.Game == null ? owner.Card.IsSecret : prototype._isSecret;
		    IsAncillaryTrigger = prototype.IsAncillaryTrigger;
			EffectIndex = prototype.EffectIndex;       // HsServer: the copy on the card keeps its client effect index
			KeywordOverride = prototype.KeywordOverride; // and its TriggerKeyword (Echoing Ooze)

			_processHandler = Process;
	    }

		public bool Validated { get; set; }

		/// <summary>
		/// Create a new instance of <see cref="Trigger"/> object in source's Game. During activation, the instance's <see cref="Process(IEntity)"/> subscribes to the events in <see cref="TriggerManager"/>.
		/// </summary>
		public virtual Trigger Activate(IPlayable source, TriggerActivation activation = TriggerActivation.PLAY, bool cloning = false, bool asAncillary = false)
		{
			if (source.ActivatedTrigger != null && !IsAncillaryTrigger && !asAncillary)
				throw new Exceptions.EntityException($"{source} already has an activated trigger.");

			if (!cloning && activation != TriggerActivation)
			{
				// HsServer (VIOLET_HOLD): HAND_OR_DECK
				bool handOrDeck = TriggerActivation == TriggerActivation.HAND_OR_DECK &&
				                  (activation == TriggerActivation.HAND || activation == TriggerActivation.DECK);
				if (!handOrDeck)
				{
					if (TriggerActivation != TriggerActivation.HAND_OR_PLAY)
						return null;

					if (activation == TriggerActivation.DECK)
						return null;
				}
			}

			var instance = new Trigger(this, source);

			if (asAncillary)
				instance.IsAncillaryTrigger = true;
			else if (!IsAncillaryTrigger)
				source.ActivatedTrigger = instance;

			if (_sequenceType != SequenceType.None)
				source.Game.Triggers.Add(instance);

			switch (_triggerType)
			{
				case TriggerType.DEAL_DAMAGE:
					source.Game.TriggerManager.DealDamageTrigger += instance._processHandler;
					break;
				case TriggerType.TAKE_DAMAGE:
					if (TriggerSource == TriggerSource.SELF)
					{
						if (source is Minion m)
							m.TakeDamageTrigger += instance._processHandler;
						else
							throw new NotImplementedException();
						break;
					}
					if (TriggerSource == TriggerSource.HERO)
					{
						source.Controller.Hero.TakeDamageTrigger += instance._processHandler;
						break;
					}
					source.Game.TriggerManager.DamageTrigger += instance._processHandler;
					break;
				case TriggerType.HEAL:
					source.Game.TriggerManager.HealTrigger += instance._processHandler;
					break;
				case TriggerType.OVERHEAL:
					source.Game.TriggerManager.OverhealTrigger += instance._processHandler;
					break;
				case TriggerType.TURN_END:
				case TriggerType.WORGEN_TRANSFORM:
					source.Game.TriggerManager.EndTurnTrigger += instance._processHandler;
					break;
				case TriggerType.TURN_START:
					source.Game.TriggerManager.TurnStartTrigger += instance._processHandler;
					break;
				case TriggerType.SUMMON:
					source.Game.TriggerManager.SummonTrigger += instance._processHandler;
					break;
				case TriggerType.AFTER_SUMMON:
					source.Game.TriggerManager.AfterSummonTrigger += instance._processHandler;
					break;
				case TriggerType.ATTACK:
					source.Game.TriggerManager.AttackTrigger += instance._processHandler;
					break;
				case TriggerType.AFTER_ATTACK:
					switch (TriggerSource)
					{
						case TriggerSource.HERO:
							// HsServer: game-wide (Validate keeps your hero's attacks): a weapon's trigger is activated before
							// a cloned game has its hero, and a new hero card would leave a hero-bound listener behind
							source.Game.TriggerManager.AfterAttackTrigger += instance._processHandler;
							break;
						case TriggerSource.SELF:
							((Minion)source).AfterAttackTrigger += instance._processHandler;
							break;
						case TriggerSource.ENCHANTMENT_TARGET:
							((Character)((Enchantment)source).Target).AfterAttackTrigger += instance._processHandler; // HsServer: any character
							break;
						default:
							// HsServer: other sources (MINIONS, FRIENDLY, ...; e.g. Festeroot Hulk) listen game-wide; the source
							// check in Process filters the attacker. Upstream threw NotImplementedException here.
							source.Game.TriggerManager.AfterAttackTrigger += instance._processHandler;
							break;
					}
					break;
				case TriggerType.DEATH:
					source.Game.TriggerManager.DeathTrigger += instance._processHandler;
					break;
				case TriggerType.PLAY_CARD:
					source.Game.TriggerManager.PlayCardTrigger += instance._processHandler;
					break;
				case TriggerType.AFTER_PLAY_CARD:
					source.Game.TriggerManager.AfterPlayCardTrigger += instance._processHandler;
					break;
				case TriggerType.PLAY_MINION:
					source.Game.TriggerManager.PlayMinionTrigger += instance._processHandler;
					break;
				case TriggerType.AFTER_PLAY_MINION:
					source.Game.TriggerManager.AfterPlayMinionTrigger += instance._processHandler;
					break;
				case TriggerType.CAST_SPELL:
					source.Game.TriggerManager.CastSpellTrigger += instance._processHandler;
					break;
				case TriggerType.AFTER_CAST:
					source.Game.TriggerManager.AfterCastTrigger += instance._processHandler;
					break;
				case TriggerType.PREDAMAGE:
					switch (TriggerSource)
					{
						case TriggerSource.HERO:
							source.Controller.Hero.PreDamageTrigger += instance._processHandler;
							break;
						case TriggerSource.SELF:
							((Character)source).PreDamageTrigger += instance._processHandler; // HsServer: any character (a hero too)
							break;
						case TriggerSource.ENCHANTMENT_TARGET:
							((Character)((Enchantment)source).Target).PreDamageTrigger += instance._processHandler; // HsServer: any character (Solid Alibi on a hero)
							break;
						default: // HsServer: other sources watch every character (the source check in Process filters)
							source.Game.TriggerManager.PreDamageTrigger += instance._processHandler;
							break;
					}
					break;
				case TriggerType.SECRET_REVEALED:
					source.Game.TriggerManager.SecretRevealedTrigger += instance._processHandler;
					break;
				case TriggerType.ZONE:
					source.Game.TriggerManager.ZoneTrigger += instance._processHandler;
					break;
				case TriggerType.DISCARD:
					source.Game.TriggerManager.DiscardTrigger += instance._processHandler;
					break;
				case TriggerType.GAME_START:
					source.Game.TriggerManager.GameStartTrigger += instance._processHandler;
					break;
				case TriggerType.DRAW:
					source.Game.TriggerManager.DrawTrigger += instance._processHandler;
					break;
				case TriggerType.ADD_TO_HAND: // HsServer
					source.Game.TriggerManager.AddToHandTrigger += instance._processHandler;
					break;
				case TriggerType.AFTER_DISCOVER: // HsServer
					source.Game.TriggerManager.AfterDiscoverTrigger += instance._processHandler;
					break;
				case TriggerType.EXCAVATE: // HsServer
					source.Game.TriggerManager.ExcavateTrigger += instance._processHandler;
					break;
				case TriggerType.FORGE: // HsServer
					source.Game.TriggerManager.ForgeTrigger += instance._processHandler;
					break;
				case TriggerType.MAGNETIZE: // HsServer (TITANS)
					source.Game.TriggerManager.MagnetizeTrigger += instance._processHandler;
					break;
				case TriggerType.LOCATION_USED: // HsServer (ISLAND_VACATION)
					source.Game.TriggerManager.LocationUsedTrigger += instance._processHandler;
					break;
				case TriggerType.ATTACK_GAINED: // HsServer (ISLAND_VACATION)
					source.Game.TriggerManager.AttackGainedTrigger += instance._processHandler;
					break;
				case TriggerType.TARGET:
					source.Game.TriggerManager.TargetTrigger += instance._processHandler;
					break;
				case TriggerType.LOSE_DIVINE_SHIELD:
					source.Game.TriggerManager.LoseDivineShield += instance._processHandler;
					break;
				case TriggerType.INSPIRE:
					source.Game.TriggerManager.InspireTrigger += instance._processHandler;
					break;
				case TriggerType.FROZEN:
					source.Game.TriggerManager.FreezeTrigger += instance._processHandler;
					break;
				case TriggerType.ARMOR:
					source.Game.TriggerManager.ArmorTrigger += instance._processHandler;
					break;
				case TriggerType.HERO_ATTACK:
					source.Game.TriggerManager.HeroAttackTrigger += instance._processHandler;
					break;
				case TriggerType.EQUIP_WEAPON:
					source.Game.TriggerManager.EquipWeaponTrigger += instance._processHandler;
					break;
				case TriggerType.SHUFFLE_INTO_DECK:
					source.Game.TriggerManager.ShuffleIntoDeckTrigger += instance._processHandler;
					break;
				case TriggerType.OVERLOAD:
					source.Game.TriggerManager.OverloadTrigger += instance._processHandler;
					break;
			}

			return instance;
		}

		private void Process(IEntity source)
		{
			// TODO: Report debug log here;

			if (_removed)
				return;

			if (_sequenceType == SequenceType.None)
				Validate(source);

			if (!Validated)
				return;

		    ProcessInternal(source);

			if (_triggerType == TriggerType.TURN_END && !_removed && (_owner.Controller.ExtraEndTurnEffect ||
			    // HsServer (CATACLYSM): Sandfury Aura — "Your minions' end of turn effects trigger twice" (wiki: doesn't stack with Drakkari)
			    _owner.Controller.ControllerAuraEffects[HsTag.MINION_EXTRA_END_TURN_EFFECT] > 0 &&
			    (_owner is Minion || _owner is Enchantment e && e.Target is Minion em && em.Controller == _owner.Controller)))
				ProcessInternal(source);
		}

	    /// <summary>HsServer: the keyword this trigger's block shows, as live: SECRET for a Secret, FREEZE for a Freeze minion's
	    /// damage trigger, TRIGGER_VISUAL for a card the client marks as triggering; otherwise none.</summary>
	    private int Keyword()
	    {
		    if (_isSecret) return (int)GameTag.SECRET;
		    if (KeywordOverride is GameTag forced) return (int)forced;
		    Card card = _owner is Enchantment ? null : _owner.Card;
		    if (card == null) return 0;
		    if (_triggerType == TriggerType.DEAL_DAMAGE && card[GameTag.FREEZE] > 0) return (int)GameTag.FREEZE;
		    // HsServer (WILD_WEST): a card with client tag 335 shows it on its own discard block (live BT_300, CATA_499, KAR_205,
		    // WW_441: TriggerKeyword=335 ShowInHistory; Actions/WildWest.cs DISCARD_KEYWORD)
		    if (_triggerType == TriggerType.DISCARD && card[Actions.Generic.DISCARD_KEYWORD] > 0) return (int)Actions.Generic.DISCARD_KEYWORD;
		    // HsServer (WONDERS): a card with several keyword triggers shows each one's own keyword (Infinite Amalgam: Inspire, Frenzy,
		    // Spellburst, Honorable Kill and Overkill — its Inspire block isn't SPELLBURST, the first keyword it has)
		    GameTag? own = _triggerType switch
		    {
			    TriggerType.INSPIRE => GameTag.INSPIRE,
			    TriggerType.TAKE_DAMAGE => HsTag.FRENZY,
			    TriggerType.AFTER_CAST => HsTag.SPELLBURST,
			    _ => (GameTag?)null,
		    };
		    if (own is GameTag ownKeyword && card[ownKeyword] > 0) return (int)ownKeyword;
		    foreach (GameTag keyword in HsTag.TriggerKeywords) // the card's own keyword (Spellburst, Frenzy...), else the trigger icon
			    if (card[keyword] > 0) return (int)keyword;
		    return 0;
	    }

	    private void ProcessInternal(IEntity source)
	    {
		    Validated = false;

			Game.Log(LogLevel.INFO, BlockType.TRIGGER, "Trigger",
			    !Game.Logging ? "" : $"{_owner}'s {_triggerType} Trigger is triggered by {source}.");

		    if (RemoveAfterTriggered)
			    Remove();

			// Enqueue tasks
			// Source: The owner of this trigger
			// Target: The source of this trigger or
			//			if the owner is Enchantment, the target of the enchantment.
			if (FastExecution)
			    Game.TaskQueue.Execute(SingleTask, _owner.Controller, _owner,
				    source is IPlayable playable ? playable
										: _owner is Enchantment ew && ew.Target is IPlayable p ? p
										: null, 0, Keyword(), EffectIndex, InHistory, notInHistory: NotInHistory);
		    else
		    {
			    Game.TaskQueue.Enqueue(SingleTask, _owner.Controller,
				    /*_owner is Enchantment ec ? ec : */_owner,
				    source is IPlayable pSource?
					    pSource :
					    _owner is Enchantment ew && ew.Target is IPlayable p ?
						    p :
						    null,
				    Keyword(), LastInDeath ? 1 : 0, -1, ConditionOnlyWhenQueued ? null : this, source as IPlayable ?? _owner);
		    }
	    }

		/// <summary>
		/// Remove this object from the Game and unsubscribe from the related event.
		/// </summary>
	    public virtual void Remove()
		{
			if (_removed) return;

			switch (_triggerType)
		    {
				case TriggerType.DEAL_DAMAGE:
					Game.TriggerManager.DealDamageTrigger -= _processHandler;
					break;
				case TriggerType.TAKE_DAMAGE:
					if (TriggerSource == TriggerSource.SELF)
					{
						if (_owner is Minion m)
							m.TakeDamageTrigger -= _processHandler;
						else
							throw new NotImplementedException();
						break;
					}
					if (TriggerSource == TriggerSource.HERO)
					{
						_owner.Controller.Hero.TakeDamageTrigger -= _processHandler;
						break;
					}
					Game.TriggerManager.DamageTrigger -= _processHandler;
					break;
			    case TriggerType.HEAL:
				    Game.TriggerManager.HealTrigger -= _processHandler;
				    break;
			    case TriggerType.OVERHEAL:
				    Game.TriggerManager.OverhealTrigger -= _processHandler;
				    break;
				case TriggerType.TURN_END:
				case TriggerType.WORGEN_TRANSFORM:
					Game.TriggerManager.EndTurnTrigger -= _processHandler;
				    break;
			    case TriggerType.TURN_START:
				    Game.TriggerManager.TurnStartTrigger -= _processHandler;
				    break;
				case TriggerType.SUMMON:
					Game.TriggerManager.SummonTrigger -= _processHandler;
					break;
				case TriggerType.AFTER_SUMMON:
				    Game.TriggerManager.AfterSummonTrigger -= _processHandler;
				    break;
				case TriggerType.ATTACK:
					Game.TriggerManager.AttackTrigger -= _processHandler;
					break;
				case TriggerType.AFTER_ATTACK:
					switch (TriggerSource)
					{
						case TriggerSource.HERO:
							Game.TriggerManager.AfterAttackTrigger -= _processHandler; // HsServer: see Activate
							break;
						case TriggerSource.SELF:
							((Minion)_owner).AfterAttackTrigger -= _processHandler;
							break;
						case TriggerSource.ENCHANTMENT_TARGET:
							((Character)((Enchantment)_owner).Target).AfterAttackTrigger -= _processHandler;
							break;
						default:
							Game.TriggerManager.AfterAttackTrigger -= _processHandler; // HsServer: see Activate
							break;
					}
					break;
				case TriggerType.DEATH:
				    Game.TriggerManager.DeathTrigger -= _processHandler;
				    break;
				case TriggerType.PLAY_CARD:
					Game.TriggerManager.PlayCardTrigger -= _processHandler;
					break;
			    case TriggerType.AFTER_PLAY_CARD:
					Game.TriggerManager.AfterPlayCardTrigger -= _processHandler;
					break;
				case TriggerType.PLAY_MINION:
				    Game.TriggerManager.PlayMinionTrigger -= _processHandler;
				    break;
				case TriggerType.AFTER_PLAY_MINION:
					Game.TriggerManager.AfterPlayMinionTrigger -= _processHandler;
					break;
			    case TriggerType.CAST_SPELL:
				    Game.TriggerManager.CastSpellTrigger -= _processHandler;
				    break;
			    case TriggerType.AFTER_CAST:
				    Game.TriggerManager.AfterCastTrigger -= _processHandler;
				    break;
				case TriggerType.PREDAMAGE:
					switch (TriggerSource)
					{
						case TriggerSource.HERO:
							_owner.Controller.Hero.PreDamageTrigger -= _processHandler;
							break;
						case TriggerSource.SELF:
							((Character)_owner).PreDamageTrigger -= _processHandler;
							break;
						case TriggerSource.ENCHANTMENT_TARGET:
							((Character)((Enchantment)_owner).Target).PreDamageTrigger -= _processHandler;
							break;
						default:
							Game.TriggerManager.PreDamageTrigger -= _processHandler; // HsServer
							break;
					}
					break;
				case TriggerType.SECRET_REVEALED:
					Game.TriggerManager.SecretRevealedTrigger -= _processHandler;
					break;
				case TriggerType.ZONE:
					Game.TriggerManager.ZoneTrigger -= _processHandler;
					break;
				case TriggerType.DISCARD:
					Game.TriggerManager.DiscardTrigger -= _processHandler;
					break;
				case TriggerType.GAME_START:
					Game.TriggerManager.GameStartTrigger -= _processHandler;
					break;
				case TriggerType.DRAW:
					Game.TriggerManager.DrawTrigger -= _processHandler;
					break;
				case TriggerType.ADD_TO_HAND: // HsServer
					Game.TriggerManager.AddToHandTrigger -= _processHandler;
					break;
				case TriggerType.AFTER_DISCOVER: // HsServer
					Game.TriggerManager.AfterDiscoverTrigger -= _processHandler;
					break;
				case TriggerType.EXCAVATE: // HsServer
					Game.TriggerManager.ExcavateTrigger -= _processHandler;
					break;
				case TriggerType.FORGE: // HsServer
					Game.TriggerManager.ForgeTrigger -= _processHandler;
					break;
				case TriggerType.MAGNETIZE: // HsServer (TITANS)
					Game.TriggerManager.MagnetizeTrigger -= _processHandler;
					break;
				case TriggerType.LOCATION_USED: // HsServer (ISLAND_VACATION)
					Game.TriggerManager.LocationUsedTrigger -= _processHandler;
					break;
				case TriggerType.ATTACK_GAINED: // HsServer (ISLAND_VACATION)
					Game.TriggerManager.AttackGainedTrigger -= _processHandler;
					break;
				case TriggerType.TARGET:
					Game.TriggerManager.TargetTrigger -= _processHandler;
					break;
				case TriggerType.LOSE_DIVINE_SHIELD:
					Game.TriggerManager.LoseDivineShield -= _processHandler;
					break;
				case TriggerType.INSPIRE:
					Game.TriggerManager.InspireTrigger -= _processHandler;
					break;
				case TriggerType.FROZEN:
					Game.TriggerManager.FreezeTrigger -= _processHandler;
					break;
			    case TriggerType.ARMOR:
				    Game.TriggerManager.ArmorTrigger -= _processHandler;
				    break;
			    case TriggerType.HERO_ATTACK:
				    Game.TriggerManager.HeroAttackTrigger -= _processHandler;
				    break;
				case TriggerType.EQUIP_WEAPON:
					Game.TriggerManager.EquipWeaponTrigger -= _processHandler;
					break;
				case TriggerType.SHUFFLE_INTO_DECK:
					Game.TriggerManager.ShuffleIntoDeckTrigger -= _processHandler;
					break;
				case TriggerType.OVERLOAD:
					Game.TriggerManager.OverloadTrigger -= _processHandler;
					break;
				default:
				    throw new ArgumentOutOfRangeException();
		    }

			if (!IsAncillaryTrigger)
				_owner.ActivatedTrigger = null;

			if (_sequenceType != SequenceType.None)
				Game.Triggers.Remove(this);

			_removed = true;

		    Game.Log(LogLevel.DEBUG, BlockType.TRIGGER, "Trigger",
			    !Game.Logging ? "" : $"{_owner}'s {_triggerType} Trigger is removed.");
	    }

		/// <summary>
		/// Checks triggers related to the current Sequence at once before the Sequence starts.
		/// </summary>
	    public static void ValidateTriggers(Game game, IEntity source, SequenceType type)
	    {
			List<Trigger> triggers = game.Triggers;
			for (int i = 0; i < triggers.Count; i++)
				if (triggers[i]._sequenceType == type)
					triggers[i].Validate(source);
	    }

	    public static void ValidateTriggers(Game game, IEntity source, TriggerType type)
	    {
		    List<Trigger> triggers = game.Triggers;
			for (int i = 0; i < triggers.Count; i++)
			    if (triggers[i]._triggerType == type)
				    triggers[i].Validate(source);
		}

	    public static void Invalidate(Game game, SequenceType type)
	    {
		    game.Triggers.ForEach(p =>
		    {
			    if (p._sequenceType == type)
				    p.Validated = false;
		    });

			//if (game.TaskQueue.Count <= 0) return;
			//if (game.TaskQueue.CurrentEventTasks.Count > 0)
			// game.TaskQueue.CurrentEventTasks.Clear();
			//else if
			//(game.TaskQueue.TaskList.Count > 0)
			// game.TaskQueue.TaskList.Clear();

			if (game.TaskQueue.IsEmpty) return;
		    game.TaskQueue.ClearCurrentEvent();
	    }

	    public static void InvalidateAll(Game game)
	    {
			game.Triggers.ForEach(p => p.Validated = false);
			if (game.TaskQueue.IsEmpty) return;
			game.TaskQueue.ClearCurrentEvent();
	    }

	    private void Validate(IEntity source)
	    {
		    // HsServer: Tight-Lipped Witness - while one is on either board, Secrets can't be revealed (they don't trigger).
		    if (_isSecret && _owner.Controller.ControllerAuraEffects[HsTag.SECRETS_CANT_TRIGGER] > 0)
			    return;
		    if (_isSecret && _owner.IsExhausted && _triggerType != TriggerType.TURN_START)
			    return;
		    // HsServer: a Dormant minion's effects wait until it awakens (only a trigger counting towards it works)
		    if (!WhileDormant && _owner is Minion sleeper && sleeper[GameTag.DORMANT] > 0 && sleeper.Zone?.Type == Zone.PLAY)
			    return;
		    // HsServer (SPACE): a Starship's pieces' effects wait for its launch (wiki: it "can't be attacked, damaged, targeted")
		    if ((_owner as Minion ?? (_owner as Enchantment)?.Target as Minion) is Minion pad && pad[Actions.Generic.LAUNCHPAD] > 0 && pad.Zone?.Type == Zone.PLAY)
			    return;

		    // HsServer (2026-10-09): a scenario that squelches the cards' triggers (Bob's Tavern) — Actions/DalaranHeistGroup5.cs
		    if (Actions.Generic.Squelched(_owner))
			    return;

		    switch (TriggerSource)
		    {
				case TriggerSource.ALL:
					break;
				case TriggerSource.FRIENDLY:
				    if (source.Controller != _owner.Controller) return;
				    break;
			    case TriggerSource.ENEMY when source.Controller == _owner.Controller: return;
			    case TriggerSource.SELF:
				    if (source.Id != _sourceId) return;
				    break;
			    case TriggerSource.ALL_MINIONS:
				    if (!(source is Minion)) return;
				    break;
			    case TriggerSource.MINIONS:
				    if (!(source is Minion) || source.Controller != _owner.Controller) return;
				    break;
			    case TriggerSource.MINIONS_EXCEPT_SELF:
				    if (!(source is Minion) || source.Controller != _owner.Controller || source.Id == _sourceId ||
				        source.Zone?.Type != Zone.PLAY) return; // (HsServer: a minion being played has no zone yet)
					break;
				case TriggerSource.ALL_MINIONS_EXCEPT_SELF:
					if (!(source is Minion) || source == _owner) return;
				    break;
				case TriggerSource.OP_MINIONS:
					if (!(source is Minion) || source.Controller == _owner.Controller) return;
					break;
			    case TriggerSource.HERO:
				    if (!(source is Hero) || source.Controller != _owner.Controller) return;
				    break;
			    case TriggerSource.ENCHANTMENT_TARGET:
				    if (!(_owner is Enchantment e) || e.Target.Id != source.Id) return;
				    break;
				// HsServer: "your weapon" / "your Hero Power" — the trigger owner's (upstream compared the source with itself, so
				// the opponent's weapon breaking triggered Instrument Smasher and Grave Shambler)
				// HsServer: "whenever your weapon is destroyed" means destroyed naturally (durability, an Ooze effect), not
				// replaced by equipping another (user ruling 2026-10-07: "if you equip a weapon it doesn't grant you a new one
				// even though it technically destroys the previous weapon"): a weapon's death by replacement
				// (HsTag.REPLACED_BY_WEAPON) doesn't fire DEATH triggers on "your weapon" (Instrument Smasher, Grave Shambler).
				// Its own Deathrattle still runs (Hero.RemoveWeapon).
				case TriggerSource.WEAPON:
					if (!(source is Weapon w) || w.Controller != _owner.Controller) return;
					if (_triggerType == TriggerType.DEATH && w[HsTag.REPLACED_BY_WEAPON] != 0) return;
					break;
				case TriggerSource.HERO_POWER:
					if (!(source is HeroPower hp) || hp.Controller != _owner.Controller) return;
					break;
				case TriggerSource.FRIENDLY_SPELL_CASTED_ON_THE_OWNER:
					if (!(source is Spell) || source.Controller != _owner.Controller || Game.CurrentEventData?.EventTarget != _owner) return;
					break;
				case TriggerSource.FRIENDLY_SPELL_CASTED_ON_OWN_MINIONS:
					if (!(source is Spell) || source.Controller != _owner.Controller || Game.CurrentEventData?.EventTarget?.Controller != _owner.Controller) return;
					break;
				case TriggerSource.FRIENDLY_EVENT_SOURCE:
					if (Game.CurrentEventData?.EventSource.Controller != _owner.Controller) return;
					break;
				case TriggerSource.ATTACKED_SELF: // HsServer: the attack's defender is this trigger's owner, any attacker
					if (Game.CurrentEventData?.EventTarget?.Id != _sourceId) return;
					break;
			}

		    //bool extra = false;

		    switch (_triggerType)
		    {
			    case TriggerType.PLAY_CARD when source.Id == _owner.Id:
				case TriggerType.SUMMON when source == _owner:
			    case TriggerType.AFTER_SUMMON when source.Id == _owner.Id:
			    case TriggerType.TURN_START when !EitherTurn && source != _owner.Controller:
			    case TriggerType.DEATH when _owner.ToBeDestroyed:
			    case TriggerType.INSPIRE when !EitherTurn && Game.CurrentPlayer != _owner.Controller:
				// HsServer: a shuffle that a shuffle trigger itself caused (Augmented Elekk's extra copy) doesn't trigger shuffle
				// triggers again (was: Augmented Elekk's AssetId)
				case TriggerType.SHUFFLE_INTO_DECK when Game.CurrentEventData?.EventSource is IPlayable shuffler
				                                       && shuffler.ActivatedTrigger?.Type == TriggerType.SHUFFLE_INTO_DECK:
					return;
				case TriggerType.TURN_END:
			    case TriggerType.WORGEN_TRANSFORM:
					if (!EitherTurn && source != _owner.Controller) return;
				    //if (!(SingleTask is RemoveEnchantmentTask) && Owner.Controller.ExtraEndTurnEffect)
					   // extra = true;
				    break;
		    }

		    if (Condition != null)
		    {
			    IPlayable s = source as IPlayable ?? _owner;
			    if (!Condition.Eval(s))
				    return;
		    }

		    Validated = true;
	    }

		public override string ToString()
		{
			return $"{{Owner:{_owner}}}[Type:{_triggerType}]";
		}
	}
}
