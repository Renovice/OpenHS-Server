// HsServer addition (not upstream SabberStone): Locations (card type 39). Wire format: RESEARCH/locations-protocol.md.
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Enums;

namespace SabberStoneCore.Model.Entities
{
	/// <summary>
	/// A Location: played from hand into a board slot like a minion, but not a minion. It is untouchable (can't attack, be
	/// attacked, be targeted or be hit by "minion" effects); instead its owner activates it: its card's power runs (with
	/// no target, a character, or a card in hand), it goes on cooldown until its owner's next turn and takes 1 DAMAGE.
	/// HEALTH is its durability; at DAMAGE &gt;= HEALTH it breaks (a normal DEATHS block, Deathrattle included).
	/// </summary>
	public class Location : Minion
	{
		/// <summary>1 after an activation, 0 at its owner's next turn start.</summary>
		public const GameTag LOCATION_ACTION_COOLDOWN = (GameTag)2353;
		/// <summary>Player tag: the player's Location activations this game (live 3525; counts across all their Locations).</summary>
		public const GameTag LOCATION_USES_THIS_GAME = (GameTag)3525;
		// (Card tag CAN_TARGET_CARDS_IN_HAND — the activation targets a card in its owner's hand, Zuramat's Prison — is Playable's.)

		public Location(in Controller controller, in Card card, in IDictionary<GameTag, int> tags, in int id = -1)
			: base(in controller, in card, in tags, in id) { }

		private Location(in Controller controller, in Location location) : base(in controller, location) { }

		public override IPlayable Clone(in Controller controller) => new Location(in controller, this);

		public bool OnCooldown
		{
			get => this[LOCATION_ACTION_COOLDOWN] > 0;
			set => this[LOCATION_ACTION_COOLDOWN] = value ? 1 : 0;
		}

		/// <summary>Playing it from hand never takes a target (the card's targeting is its activation's).</summary>
		protected internal override bool NeedsTargetList => false;

		public override bool IsValidPlayTarget(ICharacter target) => target == null;

		public bool TargetsCardsInHand => Card[CAN_TARGET_CARDS_IN_HAND] > 0;

		/// <summary>The activation's valid targets: cards in its owner's hand, characters by the card's targeting, or null
		/// when it takes no target.</summary>
		public List<IPlayable> ActivationTargets()
		{
			if (TargetsCardsInHand) // (HsServer CATACLYSM: filtered by its script's Power.HandTarget)
				return Controller.HandZone.Where(p => Card.Power?.HandTarget == null || Card.Power.HandTarget.Eval(p)).ToList();
			Card targeting = Absorbed ?? Card;
			if (targeting.TargetingType == TargetingType.None)
				return null;
			return targeting.GetValidPlayTargets(Controller).Cast<IPlayable>().ToList();
		}

		/// <summary>HsServer (SPACE): a Location that took on a spell's power (The Galaxy's Lens) activates with that spell's play
		/// requirements: client ADDITIONAL_PLAY_REQS_1 = the spell's dbf id (live s1002 36953, then the absorbed spell's own
		/// option errors).</summary>
		public Card Absorbed => this[GameTag.ADDITIONAL_PLAY_REQS_1] > 0 ? Cards.FromAssetId(this[GameTag.ADDITIONAL_PLAY_REQS_1]) : null;

		/// <summary>Can its owner activate it now (its turn, not on cooldown or exhausted, and a target when it needs one)?</summary>
		public bool CanActivate
		{
			get
			{
				if (Zone?.Type != Enums.Zone.PLAY || Controller != Game.CurrentPlayer || OnCooldown || IsExhausted || ToBeDestroyed)
					return false;
				// HsServer (SPACE): LITERALLY_UNPLAYABLE — nothing to do yet (The Galaxy's Lens before a spell; live options
				// REQ_LITERALLY_UNPLAYABLE)
				if (this[HsTag.LITERALLY_UNPLAYABLE] > 0)
					return false;
				List<IPlayable> targets = ActivationTargets();
				return targets == null || targets.Count > 0 || !(Absorbed ?? Card).MustHaveTargetToPlay && !TargetsCardsInHand;
			}
		}
	}
}
