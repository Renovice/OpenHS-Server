using System;
using System.Linq;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Loader
{
	public delegate bool AvailabilityPredicate(Controller controller, Card card);
	public delegate bool TargetingPredicate(ICharacter target);

	public static class TargetingPredicates
	{
		private static readonly TargetingPredicate ReqMurlocTarget
			= t => t.IsRace(Race.MURLOC);
		private static readonly TargetingPredicate ReqDemonTarget
			= t => t.IsRace(Race.DEMON);
		private static readonly TargetingPredicate ReqMechTarget
			= t => t.IsRace(Race.MECHANICAL);
		private static readonly TargetingPredicate ReqElementalTarget
			= t => t.IsRace(Race.ELEMENTAL);
		private static readonly TargetingPredicate ReqBeastTarget
			= t => t.IsRace(Race.BEAST);
		private static readonly TargetingPredicate ReqTotemTarget
			= t => t.IsRace(Race.TOTEM);
		private static readonly TargetingPredicate ReqPirateTarget
			= t => t.IsRace(Race.PIRATE);
		private static readonly TargetingPredicate ReqDragonTarget
			= t => t.IsRace(Race.DRAGON);

		public static readonly TargetingPredicate ReqFrozenTarget
			= t => t.IsFrozen;
		public static readonly TargetingPredicate ReqDamagedTarget
			= t => t.Damage > 0;
		/// <summary>HsServer: damaged, unless the player playing it (the current player) has Combo active.</summary>
		public static readonly TargetingPredicate ReqDamagedTargetUnlessCombo
			= t => t.Damage > 0 || t.Game.CurrentPlayer.IsComboActive;
		public static readonly TargetingPredicate ReqUndamagedTarget
			= t => t.Damage == 0;
		public static readonly TargetingPredicate ReqMustTargetTaunter
			= t => t.HasTaunt;
		public static readonly TargetingPredicate ReqStealthedTarget
			= t => t.HasStealth;
		public static readonly TargetingPredicate ReqTargetWithDivineShield
			= t => t[GameTag.DIVINE_SHIELD] > 0;
		public static readonly TargetingPredicate ReqTargetWithDeathrattle
			= t => t.HasDeathrattle;
		public static readonly TargetingPredicate ReqLegendaryTarget
			= t => t.Card.Rarity == Rarity.LEGENDARY;
		/// <summary>HsServer (2026-11-20): client REQ_NOT_LEGENDARY_TARGET (123) — Reincarnate, Bat Mask since the 2026-11-20 patch.</summary>
		public static readonly TargetingPredicate ReqNotLegendaryTarget
			= t => t.Card.Rarity != Rarity.LEGENDARY;

		public static TargetingPredicate ReqTargetWithRace(int race)
		{
			switch ((Race) race)
			{
				case Race.MURLOC:
					return ReqMurlocTarget;
				case Race.DEMON:
					return ReqDemonTarget;
				case Race.MECHANICAL:
					return ReqMechTarget;
				case Race.ELEMENTAL:
					return ReqElementalTarget;
				case Race.BEAST:
					return ReqBeastTarget;
				case Race.TOTEM:
					return ReqTotemTarget;
				case Race.PIRATE:
					return ReqPirateTarget;
				case Race.DRAGON:
					return ReqDragonTarget;
				case Race.EGG:
					return null;
				default:
					// HsServer: every minion type is a target requirement (Undead was ignored: Skeletal Sidekick could buff any
					// friendly minion; Naga, Quilboar, Draenei... threw). IsRace covers dual types (Undead tag 2534) and "All".
					Race wanted = (Race)race;
					return t => t.IsRace(wanted);
			}
		}

		public static TargetingPredicate ReqTargetMaxAttack(int value)
		{
			return t => t.AttackDamage <= value;
		}

		public static TargetingPredicate ReqTargetMinAttack(int value)
		{
			return t => t.AttackDamage >= value;
		}

		/// <summary>HsServer (WONDERS): REQ_TARGET_EXACT_ATTACK — exactly <paramref name="value"/> Attack.</summary>
		public static TargetingPredicate ReqTargetExactAttack(int value)
		{
			return t => t.AttackDamage == value;
		}

		private static readonly AvailabilityPredicate ReqMin1EnemyMinion
			= (c, card) => c.Opponent.BoardZone.Count >= 1;
		private static readonly AvailabilityPredicate ReqMin2EnemyMinions
			= (c, card) => c.Opponent.BoardZone.Count >= 2;
		private static readonly AvailabilityPredicate ReqMin1TotalMinion
			= (c, card) => c.BoardZone.Count + c.Opponent.BoardZone.Count > 0;

		public static readonly AvailabilityPredicate ReqTargetForCombo
			= (c, card) => c.IsComboActive;

		public static readonly AvailabilityPredicate ElementalPlayedLastTurn
			= (c, card) => c.NumElementalsPlayedLastTurn > 0;

		public static readonly AvailabilityPredicate DragonInHand
			= (c, card) => card.IsRace(Race.DRAGON)
				? c.HandZone.CountOf(p => p.Card.IsRace(Race.DRAGON)) > 1
				: c.DragonInHand;

		/// <summary>HsServer: a friendly Undead died after the player's last turn (Generic.UndeadDiedAfterYourLastTurn).</summary>
		public static readonly AvailabilityPredicate UndeadDiedAfterLastTurn
			= (c, card) => Actions.Generic.UndeadDiedAfterYourLastTurn(c);

		/// <summary>HsServer: the card's Manathirst holds (its MANATHIRST tag; permanent Mana Crystals).</summary>
		public static readonly AvailabilityPredicate ManathirstMet
			= (c, card) => card[Actions.Generic.MANATHIRST] > 0 && c.BaseMana >= card[Actions.Generic.MANATHIRST];

		/// <summary>HsServer: the card's Finale would hold if played now (a copy of it in hand costs exactly the Mana left).</summary>
		public static readonly AvailabilityPredicate FinaleMet
			= (c, card) => c.HandZone.Any(p => p.Card == card && p.Cost == c.RemainingMana);

		/// <summary>HsServer: Soloist — the player controls no minions (the card itself isn't on the board yet).</summary>
		public static readonly AvailabilityPredicate SoloistMet
			= (c, card) => c.BoardZone.Count == 0;

		/// <summary>HsServer: Outcast — a copy of the card in the left- or right-most hand slot.</summary>
		public static readonly AvailabilityPredicate OutcastMet
			= (c, card) => c.HandZone.Any(p => p.Card == card && Playable.InOuterHandSlot(p));

		/// <summary>HsServer (TITANS): "If you control another &lt;type&gt;" — a friendly minion of that type is in play (the card
		/// choosing its target is still in hand, so any of them counts; dual types count: Minion.IsRace).</summary>
		public static AvailabilityPredicate FriendlyRaceInPlay(int race)
			=> (c, card) => c.BoardZone.Any(m => m.IsRace((Race)race) && !m.Untouchable);

		/// <summary>HsServer (WILD_WEST): Quickdraw — a copy of the card in hand entered it this turn (client PlayReq 95).</summary>
		public static readonly AvailabilityPredicate QuickdrawMet
			= (c, card) => Actions.Generic.QuickdrawMet(c, card);

		/// <summary>HsServer (WILD_WEST): Quickdraw off — a copy of the card in hand didn't enter it this turn (client PlayReq 96).</summary>
		public static readonly AvailabilityPredicate NotQuickdrawMet
			= (c, card) => Actions.Generic.NotQuickdrawMet(c, card);

		/// <summary>HsServer (WILD_WEST): "If you're holding another N-Cost card" — another hand card of Cost
		/// <paramref name="cost"/> (Generic.HoldingOtherOfCostMet).</summary>
		public static AvailabilityPredicate HoldingOtherOfCost(int cost)
			=> (c, card) => Actions.Generic.HoldingOtherOfCostMet(c, card, cost);

		/// <summary>HsServer (TITANS): a Treant (client tag 2831 TreantTag on the target's card).</summary>
		public static readonly TargetingPredicate ReqTargetIsTreant
			= t => Conditions.SelfCondition.IsTreantCard(t.Card);

		/// <summary>HsServer (TITANS): not a Titan (client PlayReq 141: Shape the Stars).</summary>
		public static readonly TargetingPredicate ReqTargetIsNonTitan
			= t => !Actions.Generic.IsTitan(t);

		public static readonly AvailabilityPredicate ReqNumMinionSlots
			= (c, card) => !c.BoardZone.IsFull;

		public static readonly AvailabilityPredicate ReqHandNotFull
			= (c, card) => !c.HandZone.IsFull;

		public static readonly AvailabilityPredicate ReqWeaponEquipped
			= (c, card) => c.Hero.Weapon != null;

		// HsServer (livecheck 2026-10-10): "died" is the engine's one record, Controller.DiedThisGame (engine-rules.md "Died this game"), not
		// the Graveyard zone — which also holds cards that never died, and no longer holds a dead card brought back as itself (set aside:
		// Generic.SetAsideDead). The race check walked the Graveyard back by the died-this-turn count and ran past its start when fewer
		// marked minions were there (IndexOutOfRange).
		public static readonly AvailabilityPredicate ReqFriendlyMinionDiedThisGame
			= (c, card) => c.DiedThisGameCards.Any(p => p.Card.Type == CardType.MINION);

		public static AvailabilityPredicate ReqFriendlyMinionOfRaceDiedThisTurn(Race race)
		{
			return (c, card) => Actions.Generic.DiedThisTurn(c).Any(p => p.Card.IsRace(race));
		}

		public static readonly AvailabilityPredicate ReqSecretZoneCapForNonSecret
			= (c, card) => !c.SecretZone.IsFull;

		/// <summary>HsServer: "If your hero attacked this turn" (Glaivebound Adept).</summary>
		public static readonly AvailabilityPredicate ReqHeroAttackedThisTurn
			= (c, card) => c.Hero.NumAttacksThisTurn > 0;

		public static readonly AvailabilityPredicate ReqHeroHasAttack
			= (c, card) => c.Hero.AttackDamage > 0;

		/// <summary>HsServer: the opponent has at least <paramref name="value"/> minions (Mind Control Tech).</summary>
		public static AvailabilityPredicate MinimumEnemyMinions(int value) => (c, card) => c.Opponent.BoardZone.Count >= value;

		public static AvailabilityPredicate MinimumFriendlyMinions(int value)
		{
			if (value == 1)
				return (c, card) => c.BoardZone.Count > 0;
			if (value == 4)
				return (c, card) => c.BoardZone.Count >= 4;

			throw new NotImplementedException(
				$@"REQ_TARGET_IF_AVAILABLE_AND_MINIMUM_FRIENDLY_MINIONS = {value} is not implemented. Please Check \Loader\TargetingPredicates.cs");
		}

		public static AvailabilityPredicate MinimumFriendlySecrets(int value)
		{
			if (value == 1)
				return (c, card) => Actions.Generic.ControlsSecret(c); // HsServer: Secrets only (not a Sidequest / Aura in the zone)
			throw new NotImplementedException(
				$@"REQ_TARGET_IF_AVAILABLE_AND_MINIMUM_FRIENDLY_SECRETS = {value} is not implemented. Please Check \Loader\TargetingPredicates.cs");
		}

		public static AvailabilityPredicate ReqMinimumEnemyMinions(int value)
		{
			if (value == 1)
				return ReqMin1EnemyMinion;
			if (value == 2)
				return ReqMin2EnemyMinions;
			if (value == 0)
				return (c, card) => true;
			throw new NotImplementedException(
				$@"REQ_MINIMUM_ENEMY_MINIONS = {value} is not implemented. Please Check \Loader\TargetingPredicates.cs");
		}

		public static AvailabilityPredicate ReqMinimumTotalMinions(int value)
		{
			if (value == 1)
				return ReqMin1TotalMinion;
			return (c, card) => c.BoardZone.Count + c.Opponent.BoardZone.Count >= value;
		}

		public static AvailabilityPredicate ReqEntireEntourageNotInPlay(int assetId)
		{
			switch (assetId)
			{
				// Totemic Call
				case 687:
				case 40247:
				case 53238:
				case 55964:
				case 57754:
					return ReqTotemicCall;
				// True Love
				case 39563:
				case 40276:
					return ReqTrueLove;
				default:
					throw new NotImplementedException(
						$@"REQ_ENTIRE_ENTOURAGE_NOT_IN_PLAY of card {assetId} is not implemented. Please Check \Loader\TargetingPredicates.cs");
			}
		}

		private static unsafe bool ReqTotemicCall(Controller c, Card card)
		{
			int count = c.BoardZone.Count;

			if (count == Model.Game.MAX_MINIONS_ON_BOARD)
				return false;
			if (count < 4)
				return true;

			int* ent = stackalloc int[4];
			ent[0] = 537;
			ent[1] = 850;
			ent[2] = 458;
			ent[3] = 764;

			return CheckEntourages(c, ent, 4);
		}

		private static unsafe bool ReqTrueLove(Controller c, Card card)
		{
			int* ent = stackalloc int[1];
			ent[0] = 39561;

			return CheckEntourages(c, ent, 1);
		}

		private static unsafe bool CheckEntourages(Controller c, int* ent, int count)
		{
			int* indices = stackalloc int[count];
			ReadOnlySpan<Minion> span = c.BoardZone.GetSpan();
			for (int i = 0, j = span.Length, k = 0; i < span.Length; i++)
			{
				int index = -1;
				for (int m = 0; m < count; m++)
					if (ent[m] == span[i].Card.AssetId)
					{
						index = m;
						break;
					}

				if (index < 0)
				{
					if (--j < count)
						break;
					continue;
				}

				bool flag = false;
				for (int l = 0; l < k; l++)
				{
					if (indices[l] != index) continue;
					flag = true;
					break;
				}

				if (flag)
				{
					if (--j < count)
						break;
				}
				else
				{
					indices[k++] = index;
					if (k == count)
						return false;
				}
			}
			
			return true;
		}
	}
}
