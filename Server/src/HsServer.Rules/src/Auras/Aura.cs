using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using SabberStoneCore.Conditions;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Model.Zones;

namespace SabberStoneCore.Auras
{
	/// <summary>
	/// Auras can affect entities and change the applied entities' ATK, COST, etc. 
	/// Aura must be activated first to affect entities. 
	/// The effect of an aura is applied or removed during <see cref="SabberStoneCore.Model.Game.AuraUpdate"/>.
	/// </summary>
	public class Aura : IAura
	{
		private protected enum Instruction
		{
			Invalid, RemoveAll, AddAll, Add, Remove, /*CheckAdjacency*/
		}
		private protected readonly struct AuraUpdateInstruction : IEquatable<AuraUpdateInstruction>
		{
			public readonly IPlayable Src;
			public readonly Instruction Instruction;

			public AuraUpdateInstruction(IPlayable src, Instruction instruction)
			{
				Src = src;
				Instruction = instruction;
			}

			public AuraUpdateInstruction(Instruction instruction)
			{
				Src = null;
				Instruction = instruction;
			}

			public bool Equals(AuraUpdateInstruction other)
			{
				return Equals(Src, other.Src) && Instruction == other.Instruction;
			}

			public override string ToString()
			{
				if (Src != null)
					return $"{Instruction} - {Src}";
				else
					return Instruction.ToString();
			}

			public override bool Equals(object obj) => obj != null && (obj is AuraUpdateInstruction instruction && Equals(instruction));

			public override int GetHashCode()
			{
				unchecked
				{
					return ((Src != null ? Src.GetHashCode() : 0) * 397) ^ (int) Instruction;
				}
			}
		}

		private protected readonly Util.PriorityQueue<AuraUpdateInstruction> AuraUpdateInstructionsQueue;
		private protected readonly Util.SmallFastCollection AppliedEntityIdCollection;

		private readonly TriggerManager.TriggerHandler _removeHandler;
		private readonly int _ownerId;

		private IPlayable _owner;

		protected bool On = true;
		protected IEffect[] Effects;

		/// <summary>HsServer: the aura's effects (read by Game.PublishDerived for client hints).</summary>
		internal IEffect[] EffectList => Effects;

		public readonly Game Game;
		public readonly AuraType Type;
		public readonly Card EnchantmentCard;

		/// <summary>
		/// HsServer: the client's enchantment card this aura shows on everything it affects, when its effects are its own
		/// (live: Sorcerer's Apprentice puts EX1_608e on each spell in hand, Preparation EX1_145e, Auchenai Soulpriest EX1_591e
		/// on the player; each leaves the game when the card stops being affected). <see cref="EnchantmentCard"/> does the
		/// same for an aura whose effects come from its enchantment card.
		/// </summary>
		/// (A display card the loaded card data lacks — the 2021 data without the client export — shows nothing.)
		public string ShowAs { init => _shown = value == null ? null : Cards.TryFromId(value); }
		private Card _shown;
		protected Card Shown => EnchantmentCard ?? _shown;
		/// <summary>HsServer: the shown enchantment does something itself (a trigger, a granted Honorable Kill: Wing Commander
		/// Mulverick), so it exists even in games without history.</summary>
		private bool ShownActs => Shown?.Power?.Trigger != null || Shown?.Power?.HonorableKillTask != null;

		/// <summary>
		/// True if this aura should check the condition of the applied entities every time.
		/// </summary>
		public bool Restless;
		/// <summary>
		/// Only entities satisfying this condtion will be affected by this aura.
		/// </summary>
		public SelfCondition Condition;
		/// <summary>
		/// This aura will be removed when the given type of trigger is handled.
		/// Condition checks the trigger sender.
		/// </summary>
		public (TriggerType Type, SelfCondition Condition) RemoveTrigger;

		public IPlayable Owner => _owner ?? (_owner = Game.IdEntityDic[_ownerId]);

		public Aura(AuraType type, params IEffect[] effects)
		{
			Type = type;
			Effects = effects;
		}

		public Aura(AuraType type, string enchantmentId)
		{
			Type = type;
			EnchantmentCard = Cards.FromId(enchantmentId);
		}

		protected Aura(Aura prototype, IPlayable owner)
		{
			Type = prototype.Type;
			Effects = prototype.Effects;
			Condition = prototype.Condition;
			RemoveTrigger = prototype.RemoveTrigger;
			EnchantmentCard = prototype.EnchantmentCard;
			_shown = prototype._shown;
			Restless = prototype.Restless;
			On = prototype.On;

			AppliedEntityIdCollection = prototype.AppliedEntityIdCollection != null
				? new Util.SmallFastCollection(prototype.AppliedEntityIdCollection)
				: new Util.SmallFastCollection();

			AuraUpdateInstructionsQueue = new Util.PriorityQueue<AuraUpdateInstruction>();

			Game = owner.Game;
			_owner = owner;
			_ownerId = owner.Id;

			_removeHandler = TriggeredRemove;
		}

		/// <summary>
		/// Create new Aura instance to the owner's Game.
		/// </summary>
		public virtual void Activate(IPlayable owner, bool cloning = false)
		{
			if (Effects == null)
				Effects = EnchantmentCard.Power.Enchant.Effects;

			var instance = new Aura(this, owner);

			AddToGame(owner, instance);

			if (RemoveTrigger.Type != TriggerType.NONE)
			{
				switch (RemoveTrigger.Type)
				{
					case TriggerType.PLAY_MINION:
						owner.Game.TriggerManager.PlayMinionTrigger += instance._removeHandler;
						break;
					case TriggerType.CAST_SPELL:
						owner.Game.TriggerManager.CastSpellTrigger += instance._removeHandler;
						break;
					case TriggerType.TURN_END:
						owner.Game.TriggerManager.EndTurnTrigger += instance._removeHandler;
						break;
					case TriggerType.PLAY_CARD:
						owner.Game.TriggerManager.PlayCardTrigger += instance._removeHandler;
						break;
					case TriggerType.AFTER_PLAY_CARD:
						owner.Game.TriggerManager.AfterPlayCardTrigger += instance._removeHandler;
						break;
					case TriggerType.INSPIRE:
						owner.Game.TriggerManager.InspireTrigger += instance._removeHandler;
						break;
				}
			}

			if (!cloning && !Restless)
				instance.AuraUpdateInstructionsQueue.Enqueue(new AuraUpdateInstruction(Instruction.AddAll), 1);

			#region WIP: Correct History
			//if (cloning || !owner.Game.History) return;

			//if (EnchantmentCard == null) return;

			//Controller c = owner.Controller;
			//switch (Type)
			//{
			//	case AuraType.BOARD_EXCEPT_SOURCE:
			//		foreach (Minion minion in (BoardZone)owner.Zone)
			//		{
			//			if (minion == owner) continue;
			//			if (Condition == null || Condition.Eval(minion))
			//			{
			//				Enchantment e = Enchantment.GetInstance(in c, in owner, minion, in EnchantmentCard);
			//				EnchantmentCard.Power.Trigger?.Activate(e);
			//			}
			//		}
			//		break;
			//	case AuraType.BOARD:
			//		foreach (Minion minion in (BoardZone)owner.Zone)
			//		{
			//			if (Condition == null || Condition.Eval(minion))
			//			{
			//				Enchantment e = Enchantment.GetInstance(in c, in owner, minion, in EnchantmentCard);
			//				EnchantmentCard.Power.Trigger?.Activate(e);
			//			}
			//		}
			//		break;
			//	case AuraType.HAND_AND_BOARD:
			//		foreach (Minion minion in owner.Controller.BoardZone)
			//		{
			//			if (Condition == null || Condition.Eval(minion))
			//			{
			//				Enchantment e = Enchantment.GetInstance(in c, in owner, minion, in EnchantmentCard);
			//				EnchantmentCard.Power.Trigger?.Activate(e);
			//			}
			//		}
			//		foreach (IPlayable p in owner.Controller.HandZone)
			//		{
			//			if (!(p is Minion minion)) continue;

			//			if (Condition == null || Condition.Eval(minion))
			//			{
			//				Enchantment e = Enchantment.GetInstance(in c, in owner, minion, in EnchantmentCard);
			//				EnchantmentCard.Power.Trigger?.Activate(e);
			//			}

			//		}
			//		break;
			//	case AuraType.SUMMONING_PORTAL:
			//		foreach (IPlayable p in owner.Controller.HandZone.Where(p => p.Card.Type == CardType.MINION))
			//			Enchantment.GetInstance(in c, in owner, p, in EnchantmentCard);
			//		break;
			//}
			#endregion
		}

		/// <summary>
		/// Apply this aura to the target entities or remove this aura if the owner is nullified.
		/// </summary>
		public virtual void Update()
		{
			bool addAllProcessed = false;

			if (Restless)
			{
				RenewAll();
				addAllProcessed = true;
			}

			Util.PriorityQueue<AuraUpdateInstruction> queue = AuraUpdateInstructionsQueue;

			while (queue.Count != 0)
			{
				AuraUpdateInstruction inst = queue.Dequeue();
				switch (inst.Instruction)
				{
					case Instruction.RemoveAll:
						RemoveInternal();
						return;
					case Instruction.AddAll:
						addAllProcessed = true;
						UpdateInternal();
						break;
					case Instruction.Add:
						if (!addAllProcessed)
							Apply(inst.Src);
						break;
					case Instruction.Remove:
						DeApply(inst.Src);
						break;
					default:
						throw new ArgumentOutOfRangeException();
				}
			}
		}

		/// <summary>
		/// HsServer: rules A5 Rule 4 — the Aura Update (Other) right after the Death Creation Step. An aura whose source
		/// just left play is switched off now unless it only changes Attack/Health (those stay until the next Aura Update
		/// (Health/Attack), after the Death Phase). Auchenai dying with Zombie Chow: the Chow's Deathrattle heals.
		/// </summary>
		internal void UpdateOther()
		{
			if (On || IsAttackHealthOnly)
				return;
			Update();
		}

		private bool IsAttackHealthOnly
		{
			get
			{
				IEffect[] effects = Effects ?? EnchantmentCard?.Power?.Enchant?.Effects;
				if (effects == null || effects.Length == 0)
					return false;
				foreach (IEffect e in effects)
					if (e.Tag != GameTag.ATK && e.Tag != GameTag.HEALTH)
						return false;
				return true;
			}
		}

		/// <summary>
		/// Removes this aura instance from the game.
		/// </summary>
		public virtual void Remove()
		{
			On = false;
			//ToBeUpdated = true;
			AuraUpdateInstructionsQueue.Enqueue(new AuraUpdateInstruction(Instruction.RemoveAll), 0);
			Owner.OngoingEffect = null;

			switch (Type)
			{
				case AuraType.ADJACENT:
				case AuraType.BOARD:
				case AuraType.BOARD_EXCEPT_SOURCE:
					Owner.Controller.BoardZone.Auras.Remove(this);
					break;
				case AuraType.OP_BOARD: // HsServer (CATACLYSM)
					Owner.Controller.Opponent.BoardZone.Auras.Remove(this);
					break;
				case AuraType.BOARDS_EXCEPT_SOURCE: // HsServer (VANILLA)
					Owner.Controller.BoardZone.Auras.Remove(this);
					Owner.Controller.Opponent.BoardZone.Auras.Remove(this);
					break;
				case AuraType.HAND:
					Owner.Controller.HandZone.Auras.Remove(this);
					break;
				case AuraType.OP_HAND:
					Owner.Controller.Opponent.HandZone.Auras.Remove(this);
					break;
				case AuraType.HANDS:
					Owner.Controller.HandZone.Auras.Remove(this);
					Owner.Controller.Opponent.HandZone.Auras.Remove(this);
					break;
				case AuraType.HAND_AND_BOARD:
					Owner.Controller.HandZone.Auras.Remove(this);
					Owner.Controller.BoardZone.Auras.Remove(this);
					break;
			}

			switch (RemoveTrigger.Type)
			{
				case TriggerType.NONE:
					break;
				case TriggerType.CAST_SPELL:
					Game.TriggerManager.CastSpellTrigger -= _removeHandler;
					break;
				case TriggerType.TURN_END:
					Game.TriggerManager.EndTurnTrigger -= _removeHandler;
					break;
				case TriggerType.PLAY_MINION:
					Game.TriggerManager.PlayMinionTrigger -= _removeHandler;
					break;
				case TriggerType.PLAY_CARD:
					Game.TriggerManager.PlayCardTrigger -= _removeHandler;
					break;
				case TriggerType.AFTER_PLAY_CARD:
					Game.TriggerManager.AfterPlayCardTrigger -= _removeHandler;
					break;
				case TriggerType.INSPIRE:
					Game.TriggerManager.InspireTrigger -= _removeHandler;
					break;
				default:
					throw new NotImplementedException();
			}

			if (Owner is Enchantment e)
				e.Remove();
		}

		void IAura.Activate(IPlayable owner)
		{
			Activate(owner);
		}

		/// <summary>
		/// Notices this aura instance that the given entity is added to the corresponding zone.
		/// </summary>
		public void EntityAdded(IPlayable playable)
		{
			if (!On)
				return;

			var instruction = new AuraUpdateInstruction(playable, Instruction.Add);

			if (!AuraUpdateInstructionsQueue.Contains(in instruction))
				AuraUpdateInstructionsQueue.Enqueue(instruction, 2);
		}

		/// <summary>
		/// Notices this aura instance that the given entity is removed from the corresponding zone.
		/// </summary>
		public void EntityRemoved(IPlayable playable)
		{
			if (!On)
				return;
			if (playable == Owner)
				return;

			AuraUpdateInstructionsQueue.Enqueue(new AuraUpdateInstruction(playable, Instruction.Remove), 1);
		}

		private void UpdateInternal()
		{
			if (!On) return;

			switch (Type)
			{
				case AuraType.BOARD:
					Owner.Controller.BoardZone.ForEach(Apply);
					break;
				case AuraType.OP_BOARD: // HsServer (CATACLYSM)
					Owner.Controller.Opponent.BoardZone.ForEach(Apply);
					break;
				case AuraType.BOARDS_EXCEPT_SOURCE: // HsServer (VANILLA)
					foreach (Minion m in Owner.Controller.BoardZone.GetAll().Concat(Owner.Controller.Opponent.BoardZone.GetAll()))
						if (m != Owner) Apply(m);
					break;
				case AuraType.BOARD_EXCEPT_SOURCE:
				{
					IPlayable owner = Owner;
					owner.Controller.BoardZone.ForEach((minion, source, apply) =>
					{
						if (minion != source)
							apply(minion);
					}, owner, new Action<IPlayable>(Apply));
					return;
				}
				case AuraType.ADJACENT:
				{
					var board = (BoardZone) Owner.Zone;
					if (board.Count == 1)
						return;
					int pos = Owner.ZonePosition;
					if (pos > 0)
						Apply(board[pos - 1]);
					if (pos < board.Count - 1)
						Apply(board[pos + 1]);
					break;
				}
				case AuraType.HAND:
					Owner.Controller.HandZone.ForEach(Apply);
					break;
				case AuraType.OP_HAND:
					Owner.Controller.Opponent.HandZone.ForEach(Apply);
					break;
				case AuraType.HANDS:
					Owner.Controller.HandZone.ForEach(Apply);
					Owner.Controller.Opponent.HandZone.ForEach(Apply);
					break;
				case AuraType.HAND_AND_BOARD:
					Owner.Controller.HandZone.ForEach(Apply);
					Owner.Controller.BoardZone.ForEach(Apply);
					break;
				case AuraType.WEAPON:
					if (Owner.Controller.Hero.Weapon == null) break;
					Apply(Owner.Controller.Hero.Weapon);
					break;
				case AuraType.CONTROLLER:
					for (int i = 0; i < Effects.Length; i++)
						((Effect)Effects[i]).ApplyTo(Owner.Controller.ControllerAuraEffects);
					// HsServer: live shows it on the player (Auchenai Soulpriest: EX1_591e); (2026-10-08) when its card acts (a trigger of its
					// own: Talgath's GDB_472e doubles the damage) it exists in every game and its trigger runs; live then marks it an aura's
					// (323 = 1, 324 = 1 after it enters play: EX1_591e, CATA_154e, GDB_472e — live-install 204752 112932)
					if (_shown != null && (Game.History || _shown.Power?.Trigger != null))
					{
						Enchantment shown = Enchantment.GetInstance(Owner.Controller, Owner, Owner.Controller, in _shown);
						_shown.Power?.Trigger?.Activate(shown);
						if (Game.History)
						{
							shown[(GameTag)323] = 1;
							shown[(GameTag)324] = 1;
						}
					}
					break;
				case AuraType.OPPONENT:
					for (int i = 0; i < Effects.Length; i++)
						((Effect)Effects[i]).ApplyTo(Owner.Controller.Opponent.ControllerAuraEffects);
					break;
				case AuraType.CONTROLLERS:
					for (int i = 0; i < Effects.Length; i++)
					{
						((Effect)Effects[i]).ApplyTo(Owner.Controller.ControllerAuraEffects);
						((Effect)Effects[i]).ApplyTo(Owner.Controller.Opponent.ControllerAuraEffects);
					}
					break;
				case AuraType.HERO:
					Apply(Owner.Controller.Hero);
					break;
				case AuraType.HEROPOWER:
					Apply(Owner.Controller.Hero.HeroPower);
					break;
				case AuraType.OP_HEROPOWER:
					Apply(Owner.Controller.Opponent.Hero.HeroPower);
					break;
				case AuraType.SELF:
					Apply(Owner);
					break;
				case AuraType.SUMMONING_PORTAL:
					break;
			}
		}

		protected virtual void RemoveInternal()
		{
			IEffect[] effects = Effects;

			// Remove effects from applied entities
			if (Type == AuraType.CONTROLLER)
			{
				// (HsServer: a Restless one only while it applies)
				if (!Restless || AppliedEntityIdCollection.Contains(Owner.Controller.Id))
					for (int i = 0; i < effects.Length; i++)
						((Effect)effects[i]).RemoveFrom(Owner.Controller.ControllerAuraEffects);
				// HsServer: the enchantment it showed on the player leaves with it
				List<Enchantment> shownOn = Owner.Controller.AppliedEnchantments;
				for (int i = (shownOn?.Count ?? 0) - 1; i >= 0; i--)
					if (_shown != null && shownOn[i].Creator == Owner && shownOn[i].Card == _shown)
						shownOn[i].Remove();
			}
			else if (Type == AuraType.OPPONENT)
			{
				for (int i = 0; i < effects.Length; i++)
					((Effect)effects[i]).RemoveFrom(Owner.Controller.Opponent.ControllerAuraEffects);
			}
			else if (Type == AuraType.CONTROLLERS)
			{
				for (int i = 0; i < effects.Length; i++)
				{
					((Effect)effects[i]).RemoveFrom(Owner.Controller.ControllerAuraEffects);
					((Effect)effects[i]).RemoveFrom(Owner.Controller.Opponent.ControllerAuraEffects);
				}
			}
			else
			{
				int outerOrder = Game.AuraOrder;
				Game.AuraOrder = Owner.OrderOfPlay;
				AppliedEntityIdCollection.ForEach(Game.IdEntityDic, effects,
					(id, idDict, effs) =>
					{
						IPlayable entity = idDict[id];
						for (int i = 0; i < effs.Length; i++)
							effs[i].RemoveAuraFrom(entity);
					});
				Game.AuraOrder = outerOrder;
			}

			// Remove this Aura from the Game
			Game.Auras.Remove(this);

			// Remove enchantments from applied entities
			if (Shown != null && (Game.History || ShownActs))
			{
				AppliedEntityIdCollection.ForEach(_ownerId, Game.IdEntityDic,
					(id, ownerId, idDict) =>
					{
						IPlayable entity = idDict[id];
						for (int i = entity.AppliedEnchantments.Count - 1; i >= 0; i--)
							if (entity.AppliedEnchantments[i].Creator.Id == ownerId)
								entity.AppliedEnchantments[i].Remove();
					});
			}

			if (Game.Logging)
				Game.Log(LogLevel.DEBUG, BlockType.TRIGGER, "Aura.RemoveInternal",
					$"{Owner}'s aura is removed from game and " +
					$"{string.Join(",", AppliedEntityIdCollection.Select(i => Game.IdEntityDic[i]))})");
		}

		private void TriggeredRemove(IEntity source)
		{
			if (RemoveTrigger.Condition != null)
			{
				if (source is Controller)
					source = Owner;
				if (!RemoveTrigger.Condition.Eval((IPlayable)source))
					return;
			}

			Remove();
		}

		internal void DeApply(IPlayable entity)
		{
			if (!AppliedEntityIdCollection.Remove(entity.Id))
				return;

			int outerOrder = Game.AuraOrder;
			Game.AuraOrder = Owner.OrderOfPlay;
			for (int i = 0; i < Effects.Length; i++)
			{
				Effects[i].RemoveAuraFrom(entity);
			}
			Game.AuraOrder = outerOrder;

			if (Game.History)
			{
				for (int i = 0; i < Effects.Length; i++)
					Game.PowerHistory.Add(
						PowerHistoryBuilder.TagChange(entity.Id, Effects[i].Tag, entity[Effects[i].Tag]));
			}

			if (Shown != null && (Game.History || ShownActs))
			{
				int cardId = Shown.AssetId;
				List<Enchantment> enchantments = entity.AppliedEnchantments;
				for (int i = (enchantments?.Count ?? 0) - 1; i >= 0; i--)
					if (enchantments[i].Creator == Owner && enchantments[i].Card.AssetId == cardId)
					{
						// HsServer: removed from the game, as live (HIDE_ENTITY, ZONE REMOVEDFROMGAME: Sorcerer's Apprentice's
						// EX1_608e when the spell is played); dropping it from the list left it in play on the client
						enchantments[i].Remove();
						break;
					}
			}

			if (Game.Logging)
				Game.Log(LogLevel.DEBUG, BlockType.TRIGGER, "Aura.DeApply",
					$"{Owner}'s aura is removed from {entity}.");
		}

		/// <summary>
		/// Apply this aura's effect(s) to the target entity.
		/// </summary>
		private void Apply(IPlayable entity)
		{
			if (entity == null)
				throw new ArgumentNullException();

			IEffect[] effects = Effects;

			// HsServer: a Location on the board is not a minion — "your minions have ..." auras skip it (in hand, card
			// auras such as cost reductions still apply).
			if (entity is Location && entity.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY)
				return;
			// HsServer (livecheck 2026-10-10): nor a Permanent or Dormant one (Advanced rulebook BE2: "Permanents and Dormant minions are
			// not considered minions and cannot be affected by any effects from either player unless otherwise specified"; live
			// session-20261007-190857: Red Herring's Stealth never reached The Twisting Nether TTN_960t)
			if (entity is Minion permanent && permanent.Untouchable && entity.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY)
				return;
			if (Type == AuraType.BOARDS_EXCEPT_SOURCE && entity == Owner) // HsServer (VANILLA): "ALL other"
				return;

			if (Condition != null)
				if (!Condition.Eval(entity))
					return;

			int outerOrder = Game.AuraOrder;
			Game.AuraOrder = Owner.OrderOfPlay;
			for (int i = 0; i < effects.Length; i++)
				effects[i].ApplyAuraTo(entity);
			Game.AuraOrder = outerOrder;

			if (Game.History)
			{
				for (int i = 0; i < effects.Length; i++)
					Game.PowerHistory.Add(
						PowerHistoryBuilder.TagChange(entity.Id, effects[i].Tag, entity[effects[i].Tag]));
			}
			

			Card shown = Shown;
			if (shown != null && ((Game.History /*&& _tempList == null*/) || ShownActs))
			{
				Enchantment instance = Enchantment.GetInstance(entity.Controller, Owner, entity, in shown);
				shown.Power?.Trigger?.Activate(instance);
			}

			AppliedEntityIdCollection.Add(entity.Id);

			if (Game.Logging)
				Game.Log(LogLevel.DEBUG, BlockType.TRIGGER, "Aura.Apply", $"{Owner}'s aura is applied to {entity}.");
		}

		private void RenewAll()
		{
			SelfCondition condition = Condition;
			Util.SmallFastCollection collection = AppliedEntityIdCollection;
			void Renew(IPlayable p)
			{
				if (condition.Eval(p))
				{
					if (!collection.Contains(p.Id))
						Apply(p);
				}
				else
				{
					if (collection.Contains(p.Id))
						DeApply(p);
				}
			}

			switch (Type)
			{
				case AuraType.BOARD:
					Owner.Controller.BoardZone.ForEach(Renew);
					break;
				case AuraType.OP_BOARD: // HsServer (CATACLYSM)
					Owner.Controller.Opponent.BoardZone.ForEach(Renew);
					break;
				case AuraType.BOARDS_EXCEPT_SOURCE: // HsServer (VANILLA)
					foreach (Minion m in Owner.Controller.BoardZone.GetAll().Concat(Owner.Controller.Opponent.BoardZone.GetAll()))
						if (m != Owner) Renew(m);
					break;
				case AuraType.HANDS:
					Owner.Controller.HandZone.ForEach(Renew);
					Owner.Controller.Opponent.HandZone.ForEach(Renew);
					break;
				case AuraType.HAND: // HsServer: a hand aura whose condition changes as cards are played (Kael'thas Sunstrider)
					Owner.Controller.HandZone.ForEach(Renew);
					break;
				case AuraType.OP_HAND: // HsServer: the same on the opponent's hand (Commander Ulthok: only during their turn)
					Owner.Controller.Opponent.HandZone.ForEach(Renew);
					break;
				case AuraType.WEAPON:
					if (Owner.Controller.Hero.Weapon == null) break;
					Renew(Owner.Controller.Hero.Weapon);
					break;
				case AuraType.HERO:
					Renew(Owner.Controller.Hero);
					break;
				case AuraType.SELF:
					Renew(Owner);
					break;
				case AuraType.HEROPOWER: // HsServer (TIME_TRAVEL): "Your Hero Power costs (0) while your hand has 3 or less cards" (Fletcher)
					Renew(Owner.Controller.Hero.HeroPower);
					break;
				case AuraType.CONTROLLER: // HsServer (TIME_TRAVEL): a player rule while a condition on its owner holds (Azure King Malygos)
				{
					bool holds = condition == null || condition.Eval(Owner);
					int key = Owner.Controller.Id;
					bool applied = collection.Contains(key);
					if (holds && !applied)
					{
						for (int i = 0; i < Effects.Length; i++) ((Effect)Effects[i]).ApplyTo(Owner.Controller.ControllerAuraEffects);
						collection.Add(key);
					}
					else if (!holds && applied)
					{
						for (int i = 0; i < Effects.Length; i++) ((Effect)Effects[i]).RemoveFrom(Owner.Controller.ControllerAuraEffects);
						collection.Remove(key);
					}
					break;
				}
				default:
					throw new NotImplementedException($"Restless aura of type {Type} is not implemented.");
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		protected static void AddToGame(IPlayable owner, Aura aura)
		{
			owner.Game.Auras.Add(aura);
			owner.OngoingEffect = aura;

			switch (aura.Type)
			{
				case AuraType.BOARD:
				case AuraType.BOARD_EXCEPT_SOURCE:
				case AuraType.ADJACENT:
					owner.Controller.BoardZone.Auras.Add(aura);
					break;
				case AuraType.OP_BOARD: // HsServer (CATACLYSM)
					owner.Controller.Opponent.BoardZone.Auras.Add(aura);
					break;
				case AuraType.BOARDS_EXCEPT_SOURCE: // HsServer (VANILLA)
					owner.Controller.BoardZone.Auras.Add(aura);
					owner.Controller.Opponent.BoardZone.Auras.Add(aura);
					break;
				case AuraType.HAND:
					owner.Controller.HandZone.Auras.Add(aura);
					break;
				case AuraType.OP_HAND:
					owner.Controller.Opponent.HandZone.Auras.Add(aura);
					break;
				case AuraType.HANDS:
					owner.Controller.HandZone.Auras.Add(aura);
					owner.Controller.Opponent.HandZone.Auras.Add(aura);
					break;
				case AuraType.HAND_AND_BOARD:
					owner.Controller.HandZone.Auras.Add(aura);
					owner.Controller.BoardZone.Auras.Add(aura);
					break;
			}
		}

		public virtual void Clone(IPlayable clone)
		{
			Activate(clone, true);
			//((Aura)clone.OngoingEffect).ToBeUpdated = ToBeUpdated;
		}

		public override string ToString()
		{
			var sb = new StringBuilder("[A:");
			sb.Append("[O:");
			sb.Append(Owner);
			sb.Append("]");
			sb.Append("[T:");
			sb.Append(Type);
			sb.Append("]");
			if (AppliedEntityIdCollection != null)
			{
				sb.Append("[AEs:");
				AppliedEntityIdCollection.ForEach(i => sb.Append($"{{{Game.IdEntityDic[i]}}}"));
				sb.Append("]");
			}
			sb.Append(On ? "[ON]" : "[OFF]");
			//sb.Append(ToBeUpdated ? "[U]" : "[NU]");
			return sb.ToString();
		}

		// For debugging
		public IReadOnlyList<IPlayable> AppliedEntities => AppliedEntityIdCollection.Select(i => Game.IdEntityDic[i]).ToArray();
	}
}
