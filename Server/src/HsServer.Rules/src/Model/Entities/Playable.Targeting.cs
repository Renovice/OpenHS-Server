using System;
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Enums;
using SabberStoneCore.Loader;

namespace SabberStoneCore.Model.Entities
{
	public partial interface IPlayable
	{
		/// <summary>Gets all characters which can be targetted when playing this entity.</summary>
		/// <value><see cref="ICharacter"/></value>
		IEnumerable<ICharacter> ValidPlayTargets { get; }

		/// <summary>
		/// Determines whether the specified character is a valid target.
		/// </summary>
		/// <param name="target">The proposed target</param>
		/// <returns>
		///   <c>true</c> if the specified target is valid; otherwise, <c>false</c>.
		/// </returns>
		bool IsValidPlayTarget(ICharacter target);

		bool HasAnyValidPlayTargets { get; }

		/// <summary>HsServer: the cards in its owner's hand this card's play may target (Power.HandTarget), or null when
		/// it doesn't target the hand.</summary>
		List<IPlayable> HandTargets();

		/// <summary>HsServer: a valid target for playing this card (a character, a card in hand, or none).</summary>
		bool IsValidTarget(IPlayable target);
	}

	public partial class Playable
	{
		/// <summary>Client tag: the card's play may target a card in hand (the client aims at the hand only with it).</summary>
		public const GameTag CAN_TARGET_CARDS_IN_HAND = (GameTag)1508;

		/// <summary>
		/// HsServer: an optional target with a condition (the "condition met" glow, POWERED_UP): a target filter
		/// (<see cref="Card.TargetChecks"/>: a minion type, Taunt, Attack N+, damaged, Deathrattle…) or an "if" the target
		/// needs (holding a Dragon, N friendly minions, a Secret, the hero attacked). Live POWERED_UP: Skeletal Sidekick,
		/// Amalgam of the Deep; the user: The Black Knight and the tech cards. A plain optional target doesn't glow (live:
		/// Darkshire Alchemist, Stormpike Commando), nor Combo (the client's own glow) or a card that must have a target.
		/// </summary>
		public static bool HasConditionalTarget(Card card) =>
			!card.MustHaveTargetToPlay && card.TargetingType != TargetingType.None &&
			(card.TargetChecks.Count > 0 || TargetConditions(card).Count > 0);

		/// <summary>HsServer: <see cref="HasConditionalTarget"/>'s card glows: its "if" holds and a target passes its filters.</summary>
		public static bool ConditionalTargetReady(Playable p)
		{
			foreach (AvailabilityPredicate condition in TargetConditions(p.Card))
				if (!condition(p.Controller, p.Card)) return false;
			return p.HasAnyValidPlayTargets;
		}

		// the card's "take a target if …" conditions, without Combo (the client glows it) and "an Elemental last turn"
		// (its own ELEMENTAL_POWERED_UP)
		private static List<AvailabilityPredicate> TargetConditions(Card card)
		{
			var list = new List<AvailabilityPredicate>();
			if (card.TargetingAvailabilityPredicate == null) return list;
			foreach (Delegate d in card.TargetingAvailabilityPredicate.GetInvocationList())
				if (d is AvailabilityPredicate condition && condition != TargetingPredicates.ReqTargetForCombo &&
				    condition != TargetingPredicates.ElementalPlayedLastTurn)
					list.Add(condition);
			return list;
		}

		public List<IPlayable> HandTargets()
		{
			Conditions.SelfCondition condition = Card.Power?.HandTarget;
			if (condition == null)
				return null;
			var targets = new List<IPlayable>();
			foreach (IPlayable p in Controller.HandZone)
				if (p != this && condition.Eval(p))
					targets.Add(p);
			return targets;
		}

		/// <summary>HsServer: is <paramref name="target"/> a valid target for playing this card: a card in hand by
		/// <see cref="HandTargets"/>, a character by the card's targeting, or none (only when no hand card is valid).</summary>
		public bool IsValidTarget(IPlayable target)
		{
			List<IPlayable> hand = HandTargets();
			if (target == null)
				return (hand == null || hand.Count == 0) && IsValidPlayTarget(null);
			if (target.Zone?.Type == Enums.Zone.HAND)
				return hand != null && hand.Contains(target);
			return target is ICharacter character && IsValidPlayTarget(character);
		}
		/// <summary>HsServer (livecheck 2026-10-10): the card whose play requirements (targeting) this entity's play uses: its own, or —
		/// when its own takes no target — the first card named by its client tags ADDITIONAL_PLAY_REQS_1 / _2 (515 / 516) that does.
		/// The devs' way for a card that casts another card when played: live session-20261005-140614 35272, Bashana's Treant
		/// (MEND_046t "Battlecry: Cast {0}.") carries ADDITIONAL_PLAY_REQS_1 = its spell's dbf id and is played with the spell's target;
		/// wiki Swampqueen Hagatha: "Targeted spells are not targeted randomly; instead, you choose the target when you play the
		/// minion, just like any other targeted battlecry"; wiki School Teacher: "Nagaling can target if the spell can target".</summary>
		public Card TargetingCard
		{
			get
			{
				if (Card.TargetingType != TargetingType.None) return Card;
				int first = this[GameTag.ADDITIONAL_PLAY_REQS_1], second = this[GameTag.ADDITIONAL_PLAY_REQS_2];
				if (first == 0 && second == 0) return Card;
				foreach (int dbf in new[] { first, second })
					if (dbf > 0 && Cards.FromAssetId(dbf) is Card extra && extra.TargetingType != TargetingType.None)
						return extra;
				return Card;
			}
		}

		/// <summary>HsServer: its play needs a target. A minion that carries a spell's requirements (TargetingCard) takes the target "just
		/// like any other targeted battlecry" (wiki Swampqueen Hagatha): when one is available.</summary>
		public bool MustHaveTarget => TargetingCard is Card t && t.MustHaveTargetToPlay && (t == Card || !(this is Minion));

		/// <summary>
		/// Gets a value indicating whether the entity requires a target list to be calculated before being played.
		/// </summary>
		/// <value><c>true</c> if a target list must be calculated; otherwise, <c>false</c>.</value>
		protected internal virtual bool NeedsTargetList =>
			Card.RequiresTarget
			|| Card.RequiresTargetForCombo
			|| Card.RequiresTargetIfAvailable
			|| Card.RequiresTargetIfAvailableAndDragonInHand // && Controller.DragonInHand 
			|| Card.RequiresTargetIfAvailableAndElementalPlayedLastTurn // && Controller.NumElementalsPlayedLastTurn > 0
			|| Card.RequiresTargetIfAvailableAndMinimumFriendlyMinions // && Controller.Board.Count >= 4
			|| Card.RequiresTargetIfAvailableAndMinimumFriendlySecrets // && Controller.Secrets.Count > 0;
			|| Card.RequiresTargetIfAvailableAndNo3CostCardInDeck;

		/// <summary>Gets all characters which can be targetted when playing this entity.</summary>
		/// <value>
		/// <see cref="T:SabberStoneCore.Model.Entities.ICharacter" />
		/// </value>
		/// <autogeneratedoc />
		public IEnumerable<ICharacter> ValidPlayTargets => GetValidPlayTargets();

		/// <summary>
		/// Determines whether the specified character is a valid target.
		/// </summary>
		/// <param name="target">The proposed target</param>
		/// <returns>
		/// <c>true</c> if the specified target is valid; otherwise, <c>false</c>.
		/// </returns>
		/// <autogeneratedoc />
		public virtual bool IsValidPlayTarget(ICharacter target)
		{
			// HsServer: a Choose One card with both effects combined targets what its targeted options target (wiki Fandral
			// Staghelm: "Choose One cards with targeted effects will use the same target for both effects"; Boomkin's "Deal 4
			// damage" did nothing, user 2026-10-09): any option may take the target; none taken only when no option needs one
			if (RunsBothOptions)
			{
				IPlayable[] targeted = ChooseOnePlayables.Where(o => o.Card.TargetingType != TargetingType.None).ToArray();
				if (targeted.Length > 0)
					return target == null ? targeted.All(o => o.IsValidPlayTarget(null)) : targeted.Any(o => o.IsValidPlayTarget(target));
			}
			// HsServer (WILD_WEST): a bonus that replaces the target is on (client tag 2433: Heat Wave's Quickdraw, Hardcore Cultist's
			// Finale): the play takes none (Actions.Generic.DropsTarget)
			if (Actions.Generic.DropsTarget(this))
				return target == null;
			Card targeting = TargetingCard; // HsServer: its own requirements, or those it carries (ADDITIONAL_PLAY_REQS)
			if (target == null)
			{
				if (MustHaveTarget)
				{
					// target reqiuired for this card
					Game.Log(LogLevel.VERBOSE, BlockType.PLAY, "Targeting", !Game.Logging ? "" :
						$"{this} requires a target.");
					return false;
				}

				if (targeting.TargetingType == TargetingType.None)
					return true;

				if (!targeting.TargetingAvailabilityPredicate?.Invoke(Controller, targeting) ?? false)
					return true;

				if (!HasAnyValidPlayTargets)
					return true;

				Game.Log(LogLevel.VERBOSE, BlockType.PLAY, "Targeting", !Game.Logging ? "" :
					$"{this} hasn't a target and there are valid targets for this card.");
				return false;
			}

			// HsServer: Steamwheedle Sniper — a Steady Shot may target any minion or the enemy hero (same rule as Options).
			if (this is HeroPower && Controller.SteadyShotCanTarget && Card.PlayRequirements != null &&
			    Card.PlayRequirements.ContainsKey(PlayReq.REQ_STEADY_SHOT))
				return target is Minion m
					? !m.CantBeTargetedByHeroPowers && (m.Controller == Controller || !m.HasStealth && !m.IsImmune)
					: target == Controller.Opponent.Hero && !target.HasStealth && !target.IsImmune;

			switch (targeting.TargetingType)
			{
				case TargetingType.None:
					return false;
				case TargetingType.All:
					break;
				case TargetingType.FriendlyCharacters:
					if (target.Controller.PlayerId != Controller.PlayerId)
						return false;
					break;
				case TargetingType.EnemyCharacters:
					if (target.Controller.PlayerId == Controller.PlayerId)
						return false;
					break;
				case TargetingType.AllMinions:
					if (target is Hero)
						return false;
					break;
				case TargetingType.FriendlyMinions:
					if (target is Hero || target.Controller.PlayerId != Controller.PlayerId)
						return false;
					break;
				case TargetingType.EnemyMinions:
					if (target is Hero || target.Controller.PlayerId == Controller.PlayerId)
						return false;
					break;
				case TargetingType.Heroes:
					if (target is Minion)
						return false;
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}

			if (!targeting.TargetingAvailabilityPredicate?.Invoke(Controller, targeting) ?? false)
				return false;

			if (TargetingRequirements(target) && (!(Card.Power?.TargetIf is { } own) || own.Check(this, target)))
				return true;

			// got target but isn't contained in valid targets
			Game.Log(LogLevel.VERBOSE, BlockType.PLAY, "Targeting", !Game.Logging ? "" :
				$"{this} has an invalid target {target}.");

			return false;
		}

		// Default targeting for spells and hero powers

		/// <summary>Gets the valid play targets.
		/// This method defaults to targetting in the context of spells/hero powers.
		/// </summary>
		/// <returns><see cref="ICharacter"/></returns>
		private List<ICharacter> GetValidPlayTargets()
		{
			List<ICharacter> targets = TargetingCard.GetValidPlayTargets(Controller);
			// HsServer (ISLAND_VACATION): a requirement that reads the card itself (Power.TargetIf)
			if (Card.Power?.TargetIf is { } own) targets.RemoveAll(t => !own.Check(this, t));
			return targets;
		}

		/// <summary>
		/// Gets whether the current board has any valid play targets for this playable.
		/// </summary>
		public bool HasAnyValidPlayTargets
		{
			get
			{
				if (Card.Power?.TargetIf != null) return GetValidPlayTargets().Count > 0; // HsServer (ISLAND_VACATION)
				bool friendlyMinions = false;
				bool enemyMinions = false;
				bool hero = false;
				bool opHero = false;
				switch (TargetingCard.TargetingType)
				{
					case TargetingType.None:
						return false;
					case TargetingType.FriendlyCharacters:
						hero = true;
						friendlyMinions = true;
						break;
					case TargetingType.Heroes:
						hero = true;
						opHero = true;
						break;
					case TargetingType.All:
						hero = true;
						opHero = true;
						friendlyMinions = true;
						enemyMinions = true;
						break;
					case TargetingType.FriendlyMinions:
						friendlyMinions = true;
						break;
					case TargetingType.EnemyCharacters:
						opHero = true;
						enemyMinions = true;
						break;
					case TargetingType.EnemyMinions:
						enemyMinions = true;
						break;
					case TargetingType.AllMinions:
						friendlyMinions = true;
						enemyMinions = true;
						break;
					default:
						throw new ArgumentOutOfRangeException();
				}

				if (hero && TargetingRequirements(Controller.Hero)) return true;

				if (opHero && TargetingRequirements(Controller.Opponent.Hero)) return true;

				if (friendlyMinions)
				{
					var span = Controller.BoardZone.GetSpan();
					for (int i = 0; i < span.Length; i++)
						if (TargetingRequirements(span[i]))
							return true;
				}

				if (enemyMinions)
				{
					var span = Controller.Opponent.BoardZone.GetSpan();
					for (int i = 0; i < span.Length; i++)
						if (TargetingRequirements(span[i]))
							return true;
				}

				return false;
			}
		}

		/// <param name="enemiesIfPossible">HsServer: "(targets enemies if possible)" (Solarian Prime, Jace Darkweaver, The
		/// Galactic Projection Orb, Slice and Dice): a random enemy when the spell can target one, else any valid target.</param>
		public ICharacter GetRandomValidTarget(bool enemiesIfPossible = false)
		{
			List<ICharacter> validTargets = GetValidPlayTargets();
			if (validTargets.Count == 0)
				return null;
			if (enemiesIfPossible && validTargets.FindAll(t => t.Controller != Controller) is { Count: > 0 } enemies)
				validTargets = enemies;

			ICharacter randTarget = validTargets.RandomElement(Game.Random);
			CardTarget = randTarget.Id;

			if (Game.Logging)
				Game.Log(LogLevel.INFO, BlockType.POWER, "GetRandomValidTarget",
					$"{this}'s target is randomly selected to {randTarget}");

			return randTarget;
		}

		public virtual bool TargetingRequirements(ICharacter target)
		{
			return TargetingCard.TargetingRequirements(Controller, target);
		}

		/// <summary>Calculates if a target is valid by testing the game state for each hardcoded requirement.
		/// </summary>
		/// <param name="target">The proposed target.</param>
		/// <returns><c>true</c> if the proposed target is valid, <c>false</c> otherwise.</returns>
		/// <autogeneratedoc />
		//public virtual bool TargetingRequirements(ICharacter target)
		//{
		//	if (target.Card.Untouchable)
		//		return false;

		//	if ((target.HasStealth || target.IsImmune) && target.Controller != Controller)
		//		return false;

		//	if (!Card.TargetingPredicate?.Invoke(target) ?? false)
		//		return false;

		//	//foreach (KeyValuePair<PlayReq, int> item in Card.PlayRequirements)
		//	//{
		//	//	PlayReq req = item.Key;
		//	//	int param = item.Value;

		//	//	Game.Log(LogLevel.DEBUG, BlockType.PLAY, "Targeting", !Game.Logging ? "" : $"{this} check PlayReq {req} for target {target.Card.Name} ... !");

		//	//	switch (req)
		//	//	{
		//	//		//[22] REQ_TARGET_IF_AVAILABLE - If one is available, target is required. [Always:False, Param:False]

		//	//		case PlayReq.REQ_MINION_TARGET: // Target must be a minion.
		//	//			if (!(target is Minion))
		//	//			{
		//	//				return false;
		//	//			}
		//	//			break;
		//	//		case PlayReq.REQ_FRIENDLY_TARGET: // Target must be friendly.
		//	//			if (target.Controller != Controller)
		//	//			{
		//	//				return false;
		//	//			}
		//	//			break;
		//	//		case PlayReq.REQ_ENEMY_TARGET: // Target must be an enemy.
		//	//			if (target.Controller == Controller)
		//	//			{
		//	//				return false;
		//	//			}
		//	//			break;
		//	//		case PlayReq.REQ_DAMAGED_TARGET: // Target must be damaged.
		//	//			if (target.Damage == 0)
		//	//			{
		//	//				return false;
		//	//			}
		//	//			break;
		//	//		case PlayReq.REQ_FROZEN_TARGET: // Target must be frozen.
		//	//			if (!target.IsFrozen)
		//	//			{
		//	//				return false;
		//	//			}
		//	//			break;
		//	//		case PlayReq.REQ_CHARGE_TARGET: // Target must have charge.
		//	//			if (minion != null && minion.HasCharge)
		//	//			{
		//	//				return false;
		//	//			}
		//	//			break;
		//	//		case PlayReq.REQ_NONSELF_TARGET: // Cannot target self.
		//	//			if (this == target)
		//	//			{
		//	//				return false;
		//	//			}
		//	//			break;
		//	//		case PlayReq.REQ_TARGET_WITH_RACE: // Target must have race: [Always:False, Param:True]
		//	//			if (target.IsRace((Race)param))
		//	//			{
		//	//				return false;
		//	//			}
		//	//			break;
		//	//		case PlayReq.REQ_HERO_TARGET: // Target must be a hero.
		//	//			if (!(target is Hero))
		//	//			{
		//	//				return false;
		//	//			}
		//	//			break;
		//	//		case PlayReq.REQ_MUST_TARGET_TAUNTER: // Must ALWAYS target taunters.
		//	//			if (minion == null || !minion.HasTaunt)
		//	//			{
		//	//				return false;
		//	//			}
		//	//			break;
		//	//		case PlayReq.REQ_UNDAMAGED_TARGET:
		//	//			if (target.Damage > 0)
		//	//			{
		//	//				return false;
		//	//			}
		//	//			break;
		//	//		case PlayReq.REQ_LEGENDARY_TARGET:
		//	//			if (target.Card.Rarity != Rarity.LEGENDARY)
		//	//			{
		//	//				return false;
		//	//			}
		//	//			break;
		//	//		case PlayReq.REQ_TARGET_WITH_DEATHRATTLE:
		//	//			if (minion == null || !minion.IsDeathrattle)
		//	//			{
		//	//				return false;
		//	//			}
		//	//			break;
		//	//		case PlayReq.REQ_TARGET_WITH_BATTLECRY:
		//	//			if (minion == null || !minion.HasBattleCry)
		//	//			{
		//	//				return false;
		//	//			}
		//	//			break;
		//	//		case PlayReq.REQ_HERO_OR_MINION_TARGET: // Target must be a hero or minion.
		//	//			if (!(target is Minion) && !(target is Hero))
		//	//			{
		//	//				return false;
		//	//			}
		//	//			break;
		//	//		case PlayReq.REQ_MINION_OR_ENEMY_HERO:
		//	//			if (!(target is Minion) && target != Controller.Opponent.Hero)
		//	//			{
		//	//				return false;
		//	//			}
		//	//			break;
		//	//		case PlayReq.REQ_TARGET_MAX_ATTACK: // Target must have a max atk of: [Always:False, Param:True]
		//	//			if (target.AttackDamage > param)
		//	//			{
		//	//				return false;
		//	//			}
		//	//			break;
		//	//		case PlayReq.REQ_TARGET_MIN_ATTACK: // Target must have a minimum atk of: [Always:False, Param:True]
		//	//			if (target.AttackDamage < param)
		//	//			{
		//	//				return false;
		//	//			}
		//	//			break;
		//	//		case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_MINIMUM_FRIENDLY_MINIONS:
		//	//			if (Controller.BoardZone.Count < param)
		//	//			{
		//	//				return false;
		//	//			}
		//	//			break;
		//	//		case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_MINIMUM_FRIENDLY_SECRETS:
		//	//			if (Controller.SecretZone.Count < param)
		//	//			{
		//	//				return false;
		//	//			}
		//	//			break;
		//	//		case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_DRAGON_IN_HAND:
		//	//			if (Card.IsRace(Race.DRAGON))
		//	//			{
		//	//				IPlayable[] dragons = Controller.HandZone.GetAll(p => p.Card.IsRace(Race.DRAGON));
		//	//				if (dragons.Length == 1 && dragons[0] == this)
		//	//					return false;
		//	//			}
		//	//			else if (!Controller.DragonInHand)
		//	//				return false;
		//	//			break;
		//	//		case PlayReq.REQ_TARGET_IF_AVAILABE_AND_ELEMENTAL_PLAYED_LAST_TURN:
		//	//			if (Controller.NumElementalsPlayedLastTurn < 1)
		//	//			{
		//	//				return false;
		//	//			}
		//	//			break;
		//	//		case PlayReq.REQ_STEALTHED_TARGET:
		//	//			if (!(target is Minion) || !((Minion)target).HasStealth)
		//	//			{
		//	//				return false;
		//	//			}
		//	//			break;
		//	//		case PlayReq.REQ_TARGET_FOR_COMBO:
		//	//			if (!Controller.IsComboActive)
		//	//			{
		//	//				return false;
		//	//			}
		//	//			break;
		//	//		case PlayReq.REQ_SECRET_ZONE_CAP_FOR_NON_SECRET:
		//	//			if (Controller.SecretZone.IsFull)
		//	//				return false;
		//	//			break;

		//	//		// implemented in playable ... 
		//	//		case PlayReq.REQ_NUM_MINION_SLOTS:
		//	//		case PlayReq.REQ_FRIENDLY_MINION_DIED_THIS_GAME:
		//	//			break;

		//	//		// already implemented ... card.RequiresTarget and RequiresTargetIfAvailable
		//	//		case PlayReq.REQ_TARGET_TO_PLAY:
		//	//		case PlayReq.REQ_TARGET_IF_AVAILABLE:
		//	//			break;

		//	//		// TODO still haven't implemented all playerreq ...
		//	//		case PlayReq.REQ_NONSTEALTH_ENEMY_TARGET: // Enemy target cannot be stealthed.
		//	//		case PlayReq.REQ_MAX_SECRETS:
		//	//		case PlayReq.REQ_TARGET_ATTACKED_THIS_TURN: // Target must have already attacked this turn.
		//	//		case PlayReq.REQ_TARGET_TAUNTER: // Default attack power must target taunters
		//	//		case PlayReq.REQ_CAN_BE_ATTACKED: // Target cannot have the tag 'can't be attacked.'
		//	//		case PlayReq.REQ_TARGET_MAGNET: // Must target magnet (enemy) minion if one exists.
		//	//		case PlayReq.REQ_CAN_BE_TARGETED_BY_SPELLS: // Can be targeted by spells.
		//	//		case PlayReq.REQ_CAN_BE_TARGETED_BY_OPPONENTS:
		//	//		// Target cannot have the tag 'can't be targeted by opponents.'
		//	//		case PlayReq.REQ_CAN_BE_TARGETED_BY_HERO_POWERS:
		//	//		// Target cannot have the tag 'can't be targeted by hero powers.'
		//	//		case PlayReq.REQ_ENEMY_TARGET_NOT_IMMUNE: // Enemy target cannot be immune.
		//	//		case PlayReq.REQ_SUBCARD_IS_PLAYABLE:
		//	//		case PlayReq.REQ_CAN_BE_TARGETED_BY_BATTLECRIES:
		//	//		case PlayReq.REQ_FRIENDLY_MINION_DIED_THIS_TURN:
		//	//		case PlayReq.REQ_ENEMY_WEAPON_EQUIPPED:
		//	//		case PlayReq.REQ_SECRET_ZONE_CAP:
		//	//		case PlayReq.REQ_TARGET_EXACT_COST:
		//	//		case PlayReq.REQ_MINION_SLOT_OR_MANA_CRYSTAL_SLOT:
		//	//			Game.Log(LogLevel.ERROR, BlockType.PLAY, "Targeting", !Game.Logging ? "" : $"PlayReq {req} not implemented right now!");
		//	//			break;

		//	//		default:
		//	//			Game.Log(LogLevel.ERROR, BlockType.PLAY, "Targeting", !Game.Logging ? "" : $"PlayReq {req} not in switch needs to be added!");
		//	//			break;
		//	//	}
		//	//}

		//	return true;
		//}
	}
}
