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
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
using System;
using SabberStoneCore.Loader;

namespace SabberStoneCore.Model
{
	/// <summary>
	/// Object representing a known entity.
	///
	/// All properties exposed by these instances are defined by `resources/Data/CardDefs.xml`.
	/// <see cref="CardLoader"/> for extraction procedures.
	/// </summary>
	public sealed class Card
	{
		/// <summary>
		/// Constraint condition based on the state of a target <see cref="ICharacter"/>.
		/// Returns true if the target is valid for this card.
		/// The result of this predicate is independent of <see cref="TargetingType"/> of this card.
		/// null if there is no such a condtion for this card.
		/// </summary>
		public TargetingPredicate TargetingPredicate { get; private set; }
		/// <summary>
		/// Returns true if playing this card requires targeting, based on the given state of a <see cref="Controller"/>.
		/// Can be null.
		/// </summary>
		public AvailabilityPredicate TargetingAvailabilityPredicate { get; private set; }
		/// <summary>
		/// Returns false if this card cannot be played with respect to the given state of a <see cref="Controller"/>.
		/// </summary>
		public AvailabilityPredicate PlayAvailabilityPredicate { get; private set; }

		/// <summary>HsServer: each target requirement with its check, in the card's order: a target must pass all of them, and
		/// the first it fails is the error the client shows (a multicast predicate only returned its last check's result).</summary>
		public List<(PlayReq Req, TargetingPredicate Check)> TargetChecks { get; } = new List<(PlayReq, TargetingPredicate)>();
		/// <summary>HsServer: each play requirement with its check (the first failing one is the option's error).</summary>
		public List<(PlayReq Req, AvailabilityPredicate Check)> PlayChecks { get; } = new List<(PlayReq, AvailabilityPredicate)>();

		private void AddTargetCheck(PlayReq req, TargetingPredicate check)
		{
			if (check == null) return; // some requirement builders have no check (an unknown race)
			TargetChecks.Add((req, check));
			TargetingPredicate += check;
		}

		private void AddPlayCheck(PlayReq req, AvailabilityPredicate check)
		{
			if (check == null) return; // some requirement builders have no check (an unknown race)
			PlayChecks.Add((req, check));
			PlayAvailabilityPredicate += check;
		}

		/// <summary>HsServer: a script's own play check (Power.PlayableIf), added at load.</summary>
		internal void AddScriptPlayCheck(PlayReq req, AvailabilityPredicate check) => AddPlayCheck(req, check);

		/// <summary>HsServer: the first target requirement <paramref name="target"/> fails, or 0.</summary>
		public PlayReq FailedTargetCheck(ICharacter target)
		{
			foreach ((PlayReq req, TargetingPredicate check) in TargetChecks)
				if (!check(target)) return req;
			return 0;
		}

		/// <summary>HsServer: the first play requirement that fails for <paramref name="c"/>, or 0.</summary>
		public PlayReq FailedPlayCheck(Controller c)
		{
			foreach ((PlayReq req, AvailabilityPredicate check) in PlayChecks)
				if (!check(c, this)) return req;
			return 0;
		}
		/// <summary>
		/// True if playing this card requires at least one valid target.
		/// </summary>
		public bool MustHaveTargetToPlay { get; private set; }
		/// <summary>
		/// Represents the range of characters in which a card can target.
		/// </summary>
		public TargetingType TargetingType { get; private set; }

		public int ATK { get; private set; }
		public int Health { get; private set; }
		public int SpellPower { get; private set; }
		public bool Taunt { get; private set; }
		public bool Charge { get; private set; }
		public bool Stealth { get; internal set; }
		public bool Poisonous { get; private set; }
		public bool DivineShield { get; private set; }
		public bool Windfury { get; private set; }
		public bool LifeSteal { get; private set; }
		public bool Echo { get; private set; }
		/// <summary>HsServer: "Repeatable this turn" (client NON_KEYWORD_ECHO). Wiki Echo: these "function otherwise exactly the
		/// same as Echo cards, except the fact that their cost can be reduced to 0, and they don't trigger the effects of
		/// Echo-related cards".</summary>
		public bool Repeatable { get; private set; }
		public bool Rush { get; private set; }
		public bool CantBeTargetedBySpells { get; private set; }
		public bool CantBeTargetedByHeroPowers => CantBeTargetedBySpells;
		public bool CantAttack { get; private set; }
		public bool Modular { get; private set; }
		public bool ChooseOne { get; private set; }
		public bool Combo { get; private set; }
		public bool IsSecret { get; private set; }
		public bool IsQuest { get; private set; }

		/// <summary>HsServer: never randomly generated, Discovered, summoned or transformed into (the random pools leave it
		/// out: Cards.Standard / Wild / AllStandard / AllWild / CostMinionCards). Wiki Quest (ability): "Quests cannot be randomly
		/// generated"; Questline: "Questlines cannot be randomly generated"; Titan: "Titans can't be randomly summoned,
		/// transformed into, Discovered, or generated"; Rune: "cards with triple-rune requirements ... cannot be randomly
		/// generated or Discovered" (patch 26.0.4). Sidequests can (Sidequest: "Unlike Quests, Sidequests can be generated or
		/// Discovered"). Copies of a card that exists are not generation (Quest (ability): they "can still be copied").</summary>
		/// <summary>HsServer: the deck-gated families this card belongs to (bit i = HsTag.DeckGatedFamilies[i]); 0 for most.
		/// Generated / Discovered only for a player whose starting deck opens one of them (Cards.DeckAllows).</summary>
		public int DeckGatedFamilies
		{
			get
			{
				if (_deckGatedFamilies < 0)
				{
					int mask = 0;
					for (int i = 0; i < HsTag.DeckGatedFamilies.Length; i++)
						if (this[HsTag.DeckGatedFamilies[i]] > 0) mask |= 1 << i;
					_deckGatedFamilies = mask;
				}
				return _deckGatedFamilies;
			}
		}
		private int _deckGatedFamilies = -1;

		public bool NeverGenerated => IsQuest || this[(GameTag)2772] > 0 // TITAN
			|| this[(GameTag)2196] + this[(GameTag)2197] + this[(GameTag)2198] >= 3 // COST_BLOOD + COST_FROST + COST_UNHOLY
			// HsServer: the client's own generation ban, DONT_PICK_FROM_SUBSETS (676): its random subsets exclude it (SubsetRule
			// 1551 / 4025 "HAS_TAG_VALUE 676, RuleIsNot") — Quests, Titans, Zilliax, E.T.C., King of the Underbelly, C'Thun, Slice and
			// Dice, Dragon Soul, the Fabled Legendaries… (wiki Generate's "excluded from all types of card generation" list)
			|| this[(GameTag)676] > 0
			// and any card built with a sideboard (MAX_SIDEBOARD_CARDS: Commander Beatrix lacks 676) — user 2026-10-07: "they
			// literally require you to build them inside your deck"
			|| this[(GameTag)2931] > 0;

		/// <summary>HsServer: never put onto the battlefield by a random pick (randomly summoned, Discovered straight onto the
		/// battlefield, transformed into there) — the generation exclusions plus Colossal minions. Wiki Colossal: they "cannot
		/// be generated in a way that they would be summoned directly onto the battlefield ... or transformed into (e.g.
		/// Evolve)", but "can be randomly added or Discovered to your hand or deck ... as well as be transformed into in your
		/// hand and deck"; wiki Generate, "Summoning only": exactly the Colossals.</summary>
		public bool NeverRandomlyOnBoard => NeverGenerated || this[(GameTag)2247] > 0 // COLOSSAL
			// wiki Dormant: "Dormant minions with no way to awaken themselves cannot be randomly summoned or transformed into (The
			// Darkness and Magtheridon)": the ones whose own Battlecry sets up their awakening
			|| DormantUntilCondition && this[GameTag.BATTLECRY] > 0;

		public bool Deathrattle { get; }
		public bool Untouchable { get; private set; }

		/// <summary>HsServer: a minion that enters play Dormant for N turns: its OWN ability, a text line of its own
		/// ("<b>Dormant</b> for N turns." — Imprisoned minions, CRED_200, WW_375). The client has no tag for it: DORMANT
		/// (1518) is only ever a reference (tooltip) tag. Inside another clause it describes something else (CRED_163's
		/// Battlecry makes OTHER minions Dormant, "Deathrattle: Go Dormant"): 0 (engine audit 2026-10-02, A1).</summary>
		public int DormantTurns => _dormantTurns ?? (int)(_dormantTurns = DormantText.Match(Text ?? "") is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0);
		private int? _dormantTurns;
		private static readonly System.Text.RegularExpressions.Regex DormantText = new System.Text.RegularExpressions.Regex(
			@"^\s*(?:\[x\])?\s*<b>Dormant</b> for (\d+) turns?\.?\s*$", System.Text.RegularExpressions.RegexOptions.Multiline);
		/// <summary>HsServer: a minion that starts Dormant until a condition of its own is met ("Starts <b>Dormant</b>." — The
		/// Darkness, Stockades Prisoner; "<b>Dormant</b>." opening its text — Magtheridon). Wiki Dormant: "Minions will always
		/// start Dormant until their condition is fulfilled, no matter how they were summoned" (BoardZone.Add). Only once its
		/// script exists (Power): an unscripted one is a placeholder and would never awaken.</summary>
		public bool StartsDormant => Power != null && (DormantUntilCondition || this[(GameTag)1652] > 0 && DormantTurns == 0);
		/// <summary>HsServer: its text says it starts Dormant until a condition (scripted or not; see StartsDormant).</summary>
		public bool DormantUntilCondition => StartsDormantText.IsMatch(ClientText ?? "");
		private static readonly System.Text.RegularExpressions.Regex StartsDormantText = new System.Text.RegularExpressions.Regex(
			@"Starts (?:<b>)?Dormant(?:</b>)?|^\s*(?:\[x\])?\s*<b>Dormant</b>\.", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

		/// <summary>HsServer: its own "Casts When Drawn" / "Summoned When Drawn" keyword (text): drawn, it is cast / summoned and
		/// replaced by another draw (Generic Draw.OnDrawn). The client's CASTS_WHEN_DRAWN tag is no guide: Flame Leviathan and
		/// Sea Reaver ("When you draw this, …", the card stays in hand) carry it too.</summary>
		public bool ReplacedWhenDrawn => ClientText is string t && WhenDrawnText.IsMatch(t);
		// the keyword, or its old wording in the engine's 2021 text ("Cast this when drawn.")
		private static readonly System.Text.RegularExpressions.Regex WhenDrawnText = new System.Text.RegularExpressions.Regex(
			@"<b>(?:Casts|Summoned) When Drawn</b>|Cast this when drawn", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

		/// <summary>HsServer: the card's current text (the client's; the engine's 2021 text where the client has no such card).
		/// Keyword rules read it: upstream's Text can be the old wording ("Starts dormant.", "Cast this when drawn.").</summary>
		public string ClientText => SabberStoneCore.Loader.ReprintCards.ClientText(Id) ?? Text;

		/// <summary>HsServer: a Dormant minion that wakes sooner when its owner Excavates ("<b>Excavate</b> to awaken N turns
		/// sooner!" — Slagmaw the Slumbering): N, read from its text like <see cref="DormantTurns"/>; 0 for none.</summary>
		public int ExcavateAwakensSooner => _excavateAwakens ?? (int)(_excavateAwakens = ExcavateAwakenText.Match(Text ?? "") is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0);
		private int? _excavateAwakens;
		private static readonly System.Text.RegularExpressions.Regex ExcavateAwakenText = new System.Text.RegularExpressions.Regex(
			@"<b>Excavate</b> to awaken (\d+) turns? sooner");
		public bool HideStat { get; private set; }
		public bool ReceivesDoubleSpelldamageBonus { get; private set; }
		// HsServer (2026-10-08, user: "Frost Elemental freezes on attacks and dmg? its a battlecry"): "Freeze any character damaged by this"
		// only — of the 63 cards with their own FREEZE tag, the 26 whose text says "damaged by" (Water Elemental, Snowchugger,
		// Frostbite weapons…); the rest freeze through their effect (Frost Elemental's Battlecry, Frostbolt, Cone of Cold).
		public bool Freeze => _freezeTag && Text != null && Text.Contains("damaged by", StringComparison.Ordinal);
		private bool _freezeTag;
		/// <summary>HsServer (VANILLA): "Destroy any minion damaged by this minion" — Poisonous before the keyword (Vanilla Emperor
		/// Cobra, Patient Assassin: no POISONOUS tag, so no poison icon); read from the text (Character.TakeDamage).</summary>
		public bool DestroysWhatItDamages => _destroysDamaged ??= Text != null && Text.Contains("Destroy any minion damaged by this minion", StringComparison.Ordinal);
		private bool? _destroysDamaged;
		public bool Overkill { get; }

		public bool TwinSpell { get; }
		private Card()
		{

		}

		internal Card(string id, int assetId, Tag[] tags,
			Dictionary<PlayReq, int> playRequirements, string[] entourage, Tag[] refTags)
		{
			Id = id;
			AssetId = assetId;
			Entourage = entourage;
			PlayRequirements = playRequirements;
			var tagDict = new Dictionary<GameTag, int>();
			var refTagDict = new Dictionary<GameTag, int>();

			#region Preprocessing tags.
			foreach (Tag tag in tags)
			{
				if (tag.TagValue.HasIntValue)
				{
					tagDict.Add(tag.GameTag, tag.TagValue);
					switch (tag.GameTag)
					{
						case GameTag.COST:
							Cost = tag.TagValue;
							break;
						case GameTag.ATK:
							ATK = tag.TagValue;
							break;
						case GameTag.HEALTH:
							Health = tag.TagValue;
							break;
						case GameTag.OVERLOAD:
							HasOverload = true;
							Overload = tag.TagValue;
							break;
						case GameTag.SPELLPOWER:
							SpellPower = tag.TagValue;
							break;
						case GameTag.CHOOSE_ONE:
							ChooseOne = true;
							break;
						case GameTag.COMBO:
							Combo = true;
							break;
						case GameTag.TAUNT:
							Taunt = true;
							break;
						case GameTag.CHARGE:
							Charge = true;
							break;
						case GameTag.STEALTH:
							Stealth = true;
							break;
						case GameTag.POISONOUS:
							Poisonous = true;
							break;
						case GameTag.DIVINE_SHIELD:
							DivineShield = true;
							break;
						case GameTag.WINDFURY:
							Windfury = true;
							break;
						case GameTag.LIFESTEAL:
							LifeSteal = true;
							break;
						case GameTag.ECHO:
							Echo = true;
							break;
						case GameTag.NON_KEYWORD_ECHO:
							Repeatable = true;
							break;
						case GameTag.RUSH:
							Rush = true;
							break;
						case GameTag.OVERKILL:
							Overkill = true;
							break;
						case GameTag.TWINSPELL:
							TwinSpell = true;
							break;
						case GameTag.CANT_BE_TARGETED_BY_SPELLS:
							CantBeTargetedBySpells = true;
							break;
						case GameTag.CANT_ATTACK:
							CantAttack = true;
							break;
						case GameTag.MODULAR:
							Modular = true;
							break;
						case GameTag.SECRET:
							IsSecret = true;
							break;
						case GameTag.QUEST:
						case (GameTag)1725: // HsServer: QUESTLINE — each stage is a Quest (it takes the Quest slot)
							IsQuest = true;
							break;
						case GameTag.DEATHRATTLE:
							Deathrattle = true;
							break;
						case GameTag.UNTOUCHABLE:
							Untouchable = true;
							break;
						case GameTag.HIDE_STATS:
							HideStat = true;
							break;
						case GameTag.RECEIVES_DOUBLE_SPELLDAMAGE_BONUS:
							ReceivesDoubleSpelldamageBonus = true;
							break;
						case GameTag.FREEZE:
							_freezeTag = true;
							break;
						case GameTag.CARDRACE:
							Race = (Race)(int)tag.TagValue;
							break;
						case GameTag.CLASS:
							Class = (CardClass)(int)tag.TagValue;
							break;
						case GameTag.CARDTYPE:
							Type = (CardType)(int)tag.TagValue;
							if (Type == CardType.LOCATION) // HsServer: a Location is on the board but is not a minion
								Untouchable = true;
							break;
					}
				}
				else if
					(tag.TagValue.HasBoolValue)
				{
					tagDict.Add(tag.GameTag, tag.TagValue ? 1 : 0);
				}
				else if
					(tag.TagValue.HasStringValue)
				{
					switch (tag.GameTag)
					{
						case GameTag.CARDNAME:
							Name = tag.TagValue;
							break;
						case GameTag.CARDTEXT:
							Text = tag.TagValue;
							break;
					}
				}
			}
			foreach (Tag tag in refTags)
			{
				if (refTagDict.ContainsKey(tag.GameTag))
					continue;

				if (tag.TagValue.HasIntValue)
				{
					refTagDict.Add(tag.GameTag, tag.TagValue);
				}
				else if (tag.TagValue.HasBoolValue)
				{
					refTagDict.Add(tag.GameTag, tag.TagValue ? 1 : 0);
				}
			}
			#endregion

			#region Preprocessing requirements
			int characterType = 0;
			int friendlyCheck = 0;
			bool needsTarget = false;
			foreach (KeyValuePair<PlayReq, int> requirement in playRequirements)
			{
				switch (requirement.Key)
				{
					case PlayReq.REQ_TARGET_TO_PLAY:
						MustHaveTargetToPlay = true;
						needsTarget = true;
						break;
					case PlayReq.REQ_DRAG_TO_PLAY:  // TODO
					case PlayReq.REQ_NONSELF_TARGET:
					case PlayReq.REQ_TARGET_IF_AVAILABLE:
						needsTarget = true;
						break;
					case PlayReq.REQ_MINION_TARGET:
						characterType = 1;
						break;
					case PlayReq.REQ_FRIENDLY_TARGET:
						friendlyCheck = 1;
						break;
					case PlayReq.REQ_ENEMY_TARGET:
						friendlyCheck = -1;
						break;
					case PlayReq.REQ_HERO_TARGET:
						characterType = -1;
						break;
					case PlayReq.REQ_TARGET_WITH_RACE:
						AddTargetCheck(requirement.Key, TargetingPredicates.ReqTargetWithRace(requirement.Value));
						break;
					case PlayReq.REQ_FROZEN_TARGET:
						AddTargetCheck(requirement.Key, TargetingPredicates.ReqFrozenTarget);
						break;
					case PlayReq.REQ_DAMAGED_TARGET:
						AddTargetCheck(requirement.Key, TargetingPredicates.ReqDamagedTarget);
						break;
					case PlayReq.REQ_DAMAGED_TARGET_UNLESS_COMBO:
						AddTargetCheck(requirement.Key, TargetingPredicates.ReqDamagedTargetUnlessCombo);
						break;
					case PlayReq.REQ_UNDAMAGED_TARGET:
						AddTargetCheck(requirement.Key, TargetingPredicates.ReqUndamagedTarget);
						break;
					case PlayReq.REQ_TARGET_MAX_ATTACK:
						AddTargetCheck(requirement.Key, TargetingPredicates.ReqTargetMaxAttack(requirement.Value));
						break;
					case PlayReq.REQ_TARGET_MIN_ATTACK:
						AddTargetCheck(requirement.Key, TargetingPredicates.ReqTargetMinAttack(requirement.Value));
						break;
					case PlayReq.REQ_TARGET_EXACT_ATTACK: // HsServer (WONDERS)
						AddTargetCheck(requirement.Key, TargetingPredicates.ReqTargetExactAttack(requirement.Value));
						break;
					case PlayReq.REQ_MUST_TARGET_TAUNTER:
						AddTargetCheck(requirement.Key, TargetingPredicates.ReqMustTargetTaunter);
						break;
					case PlayReq.REQ_STEALTHED_TARGET:
						AddTargetCheck(requirement.Key, TargetingPredicates.ReqStealthedTarget);
						break;
					case PlayReq.REQ_TARGET_WITH_DEATHRATTLE:
						AddTargetCheck(requirement.Key, TargetingPredicates.ReqTargetWithDeathrattle);
						break;
					case PlayReq.REQ_TARGET_WITH_DIVINE_SHIELD:
						AddTargetCheck(requirement.Key, TargetingPredicates.ReqTargetWithDivineShield);
						break;
					case PlayReq.REQ_LEGENDARY_TARGET:
						AddTargetCheck(requirement.Key, TargetingPredicates.ReqLegendaryTarget);
						break;
					case PlayReq.REQ_TARGET_FOR_COMBO:
						needsTarget = true;
						TargetingAvailabilityPredicate += TargetingPredicates.ReqTargetForCombo;
						break;
					case PlayReq.REQ_TARGET_IF_AVAILABE_AND_ELEMENTAL_PLAYED_LAST_TURN:
						needsTarget = true;
						TargetingAvailabilityPredicate += TargetingPredicates.ElementalPlayedLastTurn;
						break;
					case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_DRAGON_IN_HAND:
						needsTarget = true;
						TargetingAvailabilityPredicate += TargetingPredicates.DragonInHand;
						break;
					case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_UNDEAD_DIED_AFTER_LAST_TURN:
						needsTarget = true;
						TargetingAvailabilityPredicate += TargetingPredicates.UndeadDiedAfterLastTurn;
						break;
					case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_MANATHIRST:
						needsTarget = true;
						TargetingAvailabilityPredicate += TargetingPredicates.ManathirstMet;
						break;
					case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_FINALE:
						needsTarget = true;
						TargetingAvailabilityPredicate += TargetingPredicates.FinaleMet;
						break;
					case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_SOLOIST:
						needsTarget = true;
						TargetingAvailabilityPredicate += TargetingPredicates.SoloistMet;
						break;
					case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_OUTCAST:
						needsTarget = true;
						TargetingAvailabilityPredicate += TargetingPredicates.OutcastMet;
						break;
					case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_FRIENDLY_RACE: // HsServer (TITANS)
						needsTarget = true;
						TargetingAvailabilityPredicate += TargetingPredicates.FriendlyRaceInPlay(requirement.Value);
						break;
					case PlayReq.REQ_TARGET_IS_TREANT: // HsServer (TITANS)
						AddTargetCheck(requirement.Key, TargetingPredicates.ReqTargetIsTreant);
						break;
					case PlayReq.REQ_TARGET_IS_NON_TITAN: // HsServer (TITANS)
						AddTargetCheck(requirement.Key, TargetingPredicates.ReqTargetIsNonTitan);
						break;
					case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_MINIMUM_FRIENDLY_MINIONS:
						needsTarget = true;
						TargetingAvailabilityPredicate += TargetingPredicates.MinimumFriendlyMinions(requirement.Value);
						break;
					case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_MINIMUM_ENEMY_MINIONS:
						needsTarget = true;
						TargetingAvailabilityPredicate += TargetingPredicates.MinimumEnemyMinions(requirement.Value);
						break;
					case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_MINIMUM_FRIENDLY_SECRETS:
						needsTarget = true;
						TargetingAvailabilityPredicate += TargetingPredicates.MinimumFriendlySecrets(requirement.Value);
						break;
					case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_NO_3_COST_CARD_IN_DECK:
						// TODO
						TargetingType = TargetingType.AllMinions;
						break;
					case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_HERO_HAS_ATTACK:
						needsTarget = true;
						TargetingAvailabilityPredicate += TargetingPredicates.ReqHeroHasAttack;
						break;
					case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_HERO_ATTACKED_THIS_TURN:
						needsTarget = true;
						TargetingAvailabilityPredicate += TargetingPredicates.ReqHeroAttackedThisTurn;
						break;
					case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_DRAWN_THIS_TURN: // HsServer (WILD_WEST)
						needsTarget = true;
						TargetingAvailabilityPredicate += TargetingPredicates.QuickdrawMet;
						break;
					case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_NOT_DRAWN_THIS_TURN: // HsServer (WILD_WEST)
						needsTarget = true;
						TargetingAvailabilityPredicate += TargetingPredicates.NotQuickdrawMet;
						break;
					case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_HOLDING_COST: // HsServer (WILD_WEST)
						needsTarget = true;
						TargetingAvailabilityPredicate += TargetingPredicates.HoldingOtherOfCost(requirement.Value);
						break;
					case PlayReq.REQ_NUM_MINION_SLOTS:
						AddPlayCheck(requirement.Key, TargetingPredicates.ReqNumMinionSlots);
						break;
					case PlayReq.REQ_MINIMUM_ENEMY_MINIONS:
						AddPlayCheck(requirement.Key, TargetingPredicates.ReqMinimumEnemyMinions(requirement.Value));
						break;
					case PlayReq.REQ_MINIMUM_TOTAL_MINIONS:
						AddPlayCheck(requirement.Key, TargetingPredicates.ReqMinimumTotalMinions(requirement.Value));
						break;
					case PlayReq.REQ_HAND_NOT_FULL:
						AddPlayCheck(requirement.Key, TargetingPredicates.ReqHandNotFull);
						break;
					case PlayReq.REQ_WEAPON_EQUIPPED:
						AddPlayCheck(requirement.Key, TargetingPredicates.ReqWeaponEquipped);
						break;
					case PlayReq.REQ_ENTIRE_ENTOURAGE_NOT_IN_PLAY:
						AddPlayCheck(requirement.Key, TargetingPredicates.ReqEntireEntourageNotInPlay(assetId));
						break;
					case PlayReq.REQ_FRIENDLY_MINION_DIED_THIS_GAME:
						AddPlayCheck(requirement.Key, TargetingPredicates.ReqFriendlyMinionDiedThisGame);
						break;
					case PlayReq.REQ_FRIENDLY_MINION_OF_RACE_DIED_THIS_TURN:
						AddPlayCheck(requirement.Key, TargetingPredicates.ReqFriendlyMinionOfRaceDiedThisTurn((Race) requirement.Value));
						break;
					case PlayReq.REQ_MUST_PLAY_OTHER_CARD_FIRST:
						AddPlayCheck(requirement.Key, (c, card) => false);
						break;
					//	REQ_STEADY_SHOT
					//	REQ_MINION_OR_ENEMY_HERO	//	Steady Shot
					//	REQ_MINION_SLOT_OR_MANA_CRYSTAL_SLOT	//	Jade Blossom
					case PlayReq.REQ_SECRET_ZONE_CAP_FOR_NON_SECRET:
						AddPlayCheck(requirement.Key, TargetingPredicates.ReqSecretZoneCapForNonSecret);
						break;
				}
			}

			if (needsTarget)
			{
				if (characterType > 0)
				{
					if (friendlyCheck == 0)
						TargetingType = TargetingType.AllMinions;
					else if (friendlyCheck == 1)
						TargetingType = TargetingType.FriendlyMinions;
					else
						TargetingType = TargetingType.EnemyMinions;
				}
				else if (characterType == 0)
				{
					if (friendlyCheck == 0)
						TargetingType = TargetingType.All;
					else if (friendlyCheck == 1)
						TargetingType = TargetingType.FriendlyCharacters;
					else
						TargetingType = TargetingType.EnemyCharacters;
				}
				else
					TargetingType = TargetingType.Heroes;
			}
			#endregion

			Tags = tagDict;
			RefTags = refTagDict;
			// spell damage information add ... 
			if (Text != null && (Text.Contains("$") || tagDict.ContainsKey(GameTag.AFFECTED_BY_SPELL_POWER)))
			{
				Text += " @spelldmg";
				IsAffectedBySpellDamage = true;
			}
		}

		internal void SetPlayRequirements(Dictionary<PlayReq, int> playReqs)
		{
			PlayRequirements = playReqs;
			bool needsTarget = false;
			TargetingType type = TargetingType.All;
			foreach (KeyValuePair<PlayReq, int> requirement in playReqs)
			{
				switch (requirement.Key)
			{
				case PlayReq.REQ_TARGET_TO_PLAY:
					MustHaveTargetToPlay = true;
					needsTarget = true;
					break;
				case PlayReq.REQ_DRAG_TO_PLAY:  // TODO
				case PlayReq.REQ_NONSELF_TARGET:
				case PlayReq.REQ_TARGET_IF_AVAILABLE:
					needsTarget = true;
					break;
				case PlayReq.REQ_MINION_TARGET:
					type &= ~TargetingType.Hero;
					break;
				case PlayReq.REQ_FRIENDLY_TARGET:
					type &= ~TargetingType.Enemy;
					break;
				case PlayReq.REQ_ENEMY_TARGET:
					type &= ~TargetingType.Friendly;
					break;
				case PlayReq.REQ_HERO_TARGET:
					type &= ~TargetingType.Minion;
					break;
				case PlayReq.REQ_TARGET_WITH_RACE:
					AddTargetCheck(requirement.Key, TargetingPredicates.ReqTargetWithRace(requirement.Value));
					break;
				case PlayReq.REQ_FROZEN_TARGET:
					AddTargetCheck(requirement.Key, TargetingPredicates.ReqFrozenTarget);
					break;
				case PlayReq.REQ_DAMAGED_TARGET:
					AddTargetCheck(requirement.Key, TargetingPredicates.ReqDamagedTarget);
					break;
				case PlayReq.REQ_DAMAGED_TARGET_UNLESS_COMBO:
					AddTargetCheck(requirement.Key, TargetingPredicates.ReqDamagedTargetUnlessCombo);
					break;
				case PlayReq.REQ_UNDAMAGED_TARGET:
					AddTargetCheck(requirement.Key, TargetingPredicates.ReqUndamagedTarget);
					break;
				case PlayReq.REQ_TARGET_MAX_ATTACK:
					AddTargetCheck(requirement.Key, TargetingPredicates.ReqTargetMaxAttack(requirement.Value));
					break;
				case PlayReq.REQ_TARGET_MIN_ATTACK:
					AddTargetCheck(requirement.Key, TargetingPredicates.ReqTargetMinAttack(requirement.Value));
					break;
				case PlayReq.REQ_TARGET_EXACT_ATTACK: // HsServer (WONDERS)
					AddTargetCheck(requirement.Key, TargetingPredicates.ReqTargetExactAttack(requirement.Value));
					break;
				case PlayReq.REQ_MUST_TARGET_TAUNTER:
					AddTargetCheck(requirement.Key, TargetingPredicates.ReqMustTargetTaunter);
					break;
				case PlayReq.REQ_STEALTHED_TARGET:
					AddTargetCheck(requirement.Key, TargetingPredicates.ReqStealthedTarget);
					break;
				case PlayReq.REQ_TARGET_WITH_DEATHRATTLE:
					AddTargetCheck(requirement.Key, TargetingPredicates.ReqTargetWithDeathrattle);
					break;
				case PlayReq.REQ_TARGET_WITH_DIVINE_SHIELD:
					AddTargetCheck(requirement.Key, TargetingPredicates.ReqTargetWithDivineShield);
					break;
				case PlayReq.REQ_LEGENDARY_TARGET:
					AddTargetCheck(requirement.Key, TargetingPredicates.ReqLegendaryTarget);
					break;
				case PlayReq.REQ_TARGET_FOR_COMBO:
					needsTarget = true;
					TargetingAvailabilityPredicate += TargetingPredicates.ReqTargetForCombo;
					break;
				case PlayReq.REQ_TARGET_IF_AVAILABE_AND_ELEMENTAL_PLAYED_LAST_TURN:
					needsTarget = true;
					TargetingAvailabilityPredicate += TargetingPredicates.ElementalPlayedLastTurn;
					break;
				case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_DRAGON_IN_HAND:
					needsTarget = true;
					TargetingAvailabilityPredicate += TargetingPredicates.DragonInHand;
					break;
				case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_UNDEAD_DIED_AFTER_LAST_TURN:
					needsTarget = true;
					TargetingAvailabilityPredicate += TargetingPredicates.UndeadDiedAfterLastTurn;
					break;
				case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_MANATHIRST:
					needsTarget = true;
					TargetingAvailabilityPredicate += TargetingPredicates.ManathirstMet;
					break;
				case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_FINALE:
					needsTarget = true;
					TargetingAvailabilityPredicate += TargetingPredicates.FinaleMet;
					break;
				case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_SOLOIST:
					needsTarget = true;
					TargetingAvailabilityPredicate += TargetingPredicates.SoloistMet;
					break;
				case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_OUTCAST:
					needsTarget = true;
					TargetingAvailabilityPredicate += TargetingPredicates.OutcastMet;
					break;
				case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_FRIENDLY_RACE: // HsServer (TITANS)
					needsTarget = true;
					TargetingAvailabilityPredicate += TargetingPredicates.FriendlyRaceInPlay(requirement.Value);
					break;
				case PlayReq.REQ_TARGET_IS_TREANT: // HsServer (TITANS)
					AddTargetCheck(requirement.Key, TargetingPredicates.ReqTargetIsTreant);
					break;
				case PlayReq.REQ_TARGET_IS_NON_TITAN: // HsServer (TITANS)
					AddTargetCheck(requirement.Key, TargetingPredicates.ReqTargetIsNonTitan);
					break;
				case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_MINIMUM_FRIENDLY_MINIONS:
					needsTarget = true;
					TargetingAvailabilityPredicate += TargetingPredicates.MinimumFriendlyMinions(requirement.Value);
					break;
				case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_MINIMUM_ENEMY_MINIONS:
					needsTarget = true;
					TargetingAvailabilityPredicate += TargetingPredicates.MinimumEnemyMinions(requirement.Value);
					break;
				case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_MINIMUM_FRIENDLY_SECRETS:
					needsTarget = true;
					TargetingAvailabilityPredicate += TargetingPredicates.MinimumFriendlySecrets(requirement.Value);
					break;
				case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_NO_3_COST_CARD_IN_DECK:
					// TODO
					TargetingType = TargetingType.AllMinions;
					break;
				case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_HERO_HAS_ATTACK:
					needsTarget = true;
					TargetingAvailabilityPredicate += TargetingPredicates.ReqHeroHasAttack;
					break;
				case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_HERO_ATTACKED_THIS_TURN:
					needsTarget = true;
					TargetingAvailabilityPredicate += TargetingPredicates.ReqHeroAttackedThisTurn;
					break;
				case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_DRAWN_THIS_TURN: // HsServer (WILD_WEST)
					needsTarget = true;
					TargetingAvailabilityPredicate += TargetingPredicates.QuickdrawMet;
					break;
				case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_NOT_DRAWN_THIS_TURN: // HsServer (WILD_WEST)
					needsTarget = true;
					TargetingAvailabilityPredicate += TargetingPredicates.NotQuickdrawMet;
					break;
				case PlayReq.REQ_TARGET_IF_AVAILABLE_AND_HOLDING_COST: // HsServer (WILD_WEST)
					needsTarget = true;
					TargetingAvailabilityPredicate += TargetingPredicates.HoldingOtherOfCost(requirement.Value);
					break;
				case PlayReq.REQ_NUM_MINION_SLOTS:
					AddPlayCheck(requirement.Key, TargetingPredicates.ReqNumMinionSlots);
					break;
				case PlayReq.REQ_MINIMUM_ENEMY_MINIONS:
					AddPlayCheck(requirement.Key, TargetingPredicates.ReqMinimumEnemyMinions(requirement.Value));
					break;
				case PlayReq.REQ_MINIMUM_TOTAL_MINIONS:
					AddPlayCheck(requirement.Key, TargetingPredicates.ReqMinimumTotalMinions(requirement.Value));
					break;
				case PlayReq.REQ_HAND_NOT_FULL:
					AddPlayCheck(requirement.Key, TargetingPredicates.ReqHandNotFull);
					break;
				case PlayReq.REQ_WEAPON_EQUIPPED:
					AddPlayCheck(requirement.Key, TargetingPredicates.ReqWeaponEquipped);
					break;
				case PlayReq.REQ_ENTIRE_ENTOURAGE_NOT_IN_PLAY:
					// HsServer: reprints / hero-skin variants use their original's asset id (the rule is keyed on it)
					AddPlayCheck(requirement.Key, TargetingPredicates.ReqEntireEntourageNotInPlay(
						Loader.ReprintCards.OriginalAssetId(Id) is int original && original > 0 ? original : AssetId));
					break;
				case PlayReq.REQ_FRIENDLY_MINION_DIED_THIS_GAME:
					AddPlayCheck(requirement.Key, TargetingPredicates.ReqFriendlyMinionDiedThisGame);
					break;
				case PlayReq.REQ_FRIENDLY_MINION_OF_RACE_DIED_THIS_TURN:
					AddPlayCheck(requirement.Key, TargetingPredicates.ReqFriendlyMinionOfRaceDiedThisTurn((Race) requirement.Value));
					break;
				case PlayReq.REQ_MUST_PLAY_OTHER_CARD_FIRST:
					AddPlayCheck(requirement.Key, (c, card) => false);
					break;
				//	REQ_STEADY_SHOT
				//	REQ_MINION_OR_ENEMY_HERO	//	Steady Shot
				//	REQ_MINION_SLOT_OR_MANA_CRYSTAL_SLOT	//	Jade Blossom
				case PlayReq.REQ_SECRET_ZONE_CAP_FOR_NON_SECRET:
					AddPlayCheck(requirement.Key, TargetingPredicates.ReqSecretZoneCapForNonSecret);
					break;
			}
			}

			if (needsTarget)
			{
				//if ((type & TargetingType.Enemy) != TargetingType.Enemy &&
				//    (type & TargetingType.Friendly) != TargetingType.Friendly)
				//{
				//	type |= TargetingType.Enemy;
				//	type |= TargetingType.Friendly;
				//}
				TargetingType = type;
			}
		}

		/// <summary>
		/// Unique asset id of that card nummeric representation.
		/// </summary>
		public int AssetId { get; }

		/// <summary>
		/// Unique card ID, as defined in
		/// </summary>
		public string Id { get; private set; }

		/// <summary>
		/// Name of the card, localized in the extracted language.
		/// </summary>
		public string Name { get; internal set; }

		/// <summary>
		/// Flavour text of the card, localized in the extracted language.
		/// </summary>
		public string Text { get; internal set; }

		/// <summary>
		/// Contains all card ID's which are generated by this specific card.
		///
		/// For example Ysera, the dragon which produces on DREAM card after your turn,
		/// has entourage: DREAM_01, DREAM_02, DREAM_03, DREAM_04, DREAM_05
		/// </summary>
		public string[] Entourage { get; internal set; }

		/// <summary>
		/// Properties set on this instance.
		/// These properties represent health, attack, # turns in play etc.
		/// <see cref="GameTag"/> for all possibilities.
		/// </summary>
		public Dictionary<GameTag, int> Tags { get; private set; }

		/// <summary>
		/// Declares all effects that are triggered by this instance.
		/// Possibilities are SpellPower, DeathRattle, Charge etc.
		/// </summary>
		public Dictionary<GameTag, int> RefTags { get; private set; }

		/// <summary>
		/// Requirements that must have been met before this card can be moved into
		/// play zone.
		/// <see cref="PlayReq"/> for all possibilities.
		/// </summary>
		public Dictionary<PlayReq, int> PlayRequirements { get; private set; }

		/// <summary>
		/// Provides easy access to the value of a specific Tag set on this instance.
		/// <seealso cref="Tags"/>
		/// </summary>
		/// <param name="t">The <see cref="GameTag"/> which value is queried</param>
		/// <returns></returns>
		public int this[GameTag t]
			=> Tags.TryGetValue(t, out int value) ? value : 0;

		/// <summary>
		/// Indicates if this card occurs in the player's collection. Only collectible
		/// cards can be put together in a deck.
		///
		/// Non-collectible cards are generated during the game, like Ysera's Dream cards.
		/// </summary>
		public bool Collectible => this[GameTag.COLLECTIBLE] == 1;

		/// <summary>
		/// A card can have NO class or AT MOST one.
		/// The cardclass is coupled with the chosen hero to represent the player.
		///
		/// Cards with a specific class can NOT be put into a deck with other classcards.
		/// <seealso cref="CardClass"/>
		/// </summary>
		public CardClass Class { get; }

		/// <summary>HsServer: whether the card belongs to <paramref name="cls"/>. A dual- or tri-class card (Scholomance on,
		/// Darkmoon Races, Gadgetzan's factions) is NEUTRAL in the client data with its classes in MULTIPLE_CLASSES (bit
		/// class - 1: Cutting Class 576 = Rogue + Warrior); it belongs to each of them and isn't neutral (user 2026-10-06:
		/// "a venn diagram of shared class cards" — Guidance can give Shaman and Shaman dual-class spells, not raw Druid).</summary>
		/// <summary>HsServer: a spell that stays in the Secret zone for some turns: a Paladin Aura (PALADIN_AURA) or an Alterac
		/// Valley Objective (OBJECTIVE: "Lasts 3 turns").</summary>
		public bool IsDurationSpell => this[Enums.HsTag.PALADIN_AURA] > 0 || this[Enums.HsTag.OBJECTIVE] > 0 || IsSigil;

		/// <summary>HsServer (TITANS): a Sigil (client SIGIL 1749: "At the start of your next turn, …"). Live (live-20261007-vfx-decks
		/// 263109, 266351; user 2026-10-07: "the same slots as secrets and/or auras … like Wildpaw Cavern"): it sits in the Secret
		/// zone, visible, until its effect at the start of its owner's next turn (Generic.SigilPower); any number of copies
		/// (Advanced rulebook: "any number of copies of any other type of hero zone card").</summary>
		public bool IsSigil => this[(GameTag)1749] > 0;

		/// <summary>HsServer (TITANS): a Titan — a minion with its own TITAN keyword (2772; the loader drops it where it is only
		/// mentioned) and its ability spells (Model.Cards.TitanAbilities). Wiki Titan: "Each Titan has 3 activated abilities."</summary>
		public bool IsTitan => Type == CardType.MINION && this[(GameTag)2772] > 0 && Cards.TitanAbilities(this).Length > 0;

		public bool HasClass(CardClass cls)
		{
			int multi = this[GameTag.MULTIPLE_CLASSES];
			if (cls == CardClass.NEUTRAL) return Class == CardClass.NEUTRAL && multi == 0;
			if (Class == cls) return true;
			int bit = (int)cls - 1;
			return multi != 0 && bit >= 0 && bit < 31 && (multi & (1 << bit)) != 0;
		}


		private Race Race;

		/// <summary>
		/// To get the raw Race defined by the card Date, typically shouldn't be use
		/// Consider using IsRace instead
		/// <see cref="Race"/>
		/// </summary>
		public Race GetRawRace()
		{
			return Race;
		}

		public bool IsRace(Race race)
		{
			if (Race == Race.ALL)
				return true;
			if (Race == race) // standard flow for all non All/Amalgadan types
				return true;
			// HsServer: a dual-type minion's second type is its "is this type" tag (CardRace.IsRaceTag: Undead 2534...). Wiki
			// Minion: dual-type minions "benefit from the effects of both types" (Murmy, a Murloc and an Undead, didn't count
			// as an Undead for Acolyte of Death).
			return RaceTag(race) is { } tag && this[tag] > 0;
		}

		private static Dictionary<Race, GameTag> raceTags;

		/// <summary>HsServer: the client's "is this race" tag for a race (CardRace.json IsRaceTag), null for none.</summary>
		public static GameTag? RaceTag(Race race)
		{
			if (raceTags == null)
			{
				var map = new Dictionary<Race, GameTag>();
				string file = ReprintCards.DbfDirectory == null ? null : System.IO.Path.Combine(ReprintCards.DbfDirectory, "CardRace.json");
				if (file != null && System.IO.File.Exists(file))
				{
					using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllBytes(file));
					foreach (System.Text.Json.JsonElement r in doc.RootElement.GetProperty("records").EnumerateArray())
						if (r.TryGetProperty("ID", out var id) && r.TryGetProperty("IsRaceTag", out var tag) && tag.ValueKind == System.Text.Json.JsonValueKind.Number)
							map[(Race)id.GetInt32()] = (GameTag)tag.GetInt32();
				}
				if (map.Count == 0) return null; // not loaded yet: try again later
				raceTags = map;
			}
			return raceTags.TryGetValue(race, out GameTag t) ? t : null;
		}

		/// <summary>
		/// <see cref="Faction"/>
		/// </summary>
		public Faction Faction => (Faction)this[GameTag.FACTION];

		/// <summary>
		/// <see cref="Rarity"/>
		/// </summary>
		public Rarity Rarity => (Rarity)this[GameTag.RARITY];

		/// <summary>
		/// The actual type of a card has limitations on it's usage.
		/// A hero card cannot be put into a deck for example.. imagine that!
		/// Knights of the Frozen Throne , is beyond imagination? With
		/// Deathknigth heros in the deck!
		/// <see cref="CardType"/>
		/// </summary>
		public CardType Type { get; }

		/// <summary>
		/// <see cref="CardSet"/>
		/// </summary>
		public CardSet Set => (CardSet)this[GameTag.CARD_SET];

		/// <summary>
		/// Original mana-cost of this card.
		/// </summary>
		public int Cost { get; private set; }

		/// <summary>
		/// True if this card will incur Overload when played.
		/// Overload is an effect that locks mana crystals.
		/// Locked mana crystals can't be spent during one turn.
		/// </summary>
		public bool HasOverload { get; internal set; }

		/// <summary>
		/// The amount of overload incurred by this card when played.
		/// </summary>
		/// <summary>The Overload amount (HsServer: the loader puts the client text's "(N)" in OVERLOAD — ReprintCards.ClientTags).</summary>
		public int Overload { get; internal set; }

		/// <summary>
		/// Returns to which multi class group this card belongs.
		/// <see cref="MultiClassGroup"/>
		/// </summary>
		public bool RequiresTarget => PlayRequirements.ContainsKey(PlayReq.REQ_TARGET_TO_PLAY);

		/// <summary>
		/// Requires a target for combo
		/// </summary>
		public bool RequiresTargetForCombo => PlayRequirements.ContainsKey(PlayReq.REQ_TARGET_FOR_COMBO);

		/// <summary>
		/// Requires a target if available
		/// </summary>
		public bool RequiresTargetIfAvailable => PlayRequirements.ContainsKey(PlayReq.REQ_TARGET_IF_AVAILABLE) ||
		                                         PlayRequirements.ContainsKey(PlayReq.REQ_DRAG_TO_PLAY);

		/// <summary>
		/// Requires a target if available and dragon in hand
		/// </summary>
		public bool RequiresTargetIfAvailableAndDragonInHand
			=> PlayRequirements.ContainsKey(PlayReq.REQ_TARGET_IF_AVAILABLE_AND_DRAGON_IN_HAND);

		/// <summary>
		/// Requires a target if available and element played last turn
		/// </summary>
		public bool RequiresTargetIfAvailableAndElementalPlayedLastTurn
			=> PlayRequirements.ContainsKey(PlayReq.REQ_TARGET_IF_AVAILABE_AND_ELEMENTAL_PLAYED_LAST_TURN);

		/// <summary>
		/// Requires a target if available and minimum friendly minions
		/// </summary>
		public bool RequiresTargetIfAvailableAndMinimumFriendlyMinions
			=> PlayRequirements.ContainsKey(PlayReq.REQ_TARGET_IF_AVAILABLE_AND_MINIMUM_FRIENDLY_MINIONS);

		/// <summary>
		/// Requires a target if available and minimum friendly secrets
		/// </summary>
		public bool RequiresTargetIfAvailableAndMinimumFriendlySecrets
			=> PlayRequirements.ContainsKey(PlayReq.REQ_TARGET_IF_AVAILABLE_AND_MINIMUM_FRIENDLY_SECRETS);

		public bool RequiresTargetIfAvailableAndNo3CostCardInDeck
			=> PlayRequirements.ContainsKey(PlayReq.REQ_TARGET_IF_AVAILABLE_AND_NO_3_COST_CARD_IN_DECK);

		/// <summary>
		/// Maximum amount of copies that are allowed in one deck.
		/// </summary>
		public int MaxAllowedInDeck => Rarity == Rarity.LEGENDARY ? 1 : 2;

		/// <summary>
		/// True if the effects of this card are implemented.
		/// </summary>
		public bool Implemented { get; set; }

		/// <summary>
		/// Holds a list of Buffs/Debuffs on this card instance.
		/// <seealso cref="Power"/>
		/// </summary>
		//public List<Power> Powers { get; set; } = new List<Power>();
		public Power Power { get; set; }

		/// <summary>
		/// True if this card increases it's owners spell damage.
		/// </summary>
		public bool IsAffectedBySpellDamage { get; set; }

		/// <summary>
		/// Multi class group.
		/// </summary>
		public int MultiClassGroup => this[GameTag.MULTI_CLASS_GROUP];

		/// <summary>
		/// Check whether this card is playable with respect to the state of a given <see cref="Controller"/>.
		/// </summary>
		public bool IsPlayableByCardReq(in Controller c)
		{
			//return PlayAvailabilityPredicate?.Invoke(c) ?? true;

			if (PlayAvailabilityPredicate != null)
			{
				//if (PlayAvailabilityPredicate.)

				bool flag = true;
				foreach (Delegate predicate in PlayAvailabilityPredicate.GetInvocationList())
					flag &= ((AvailabilityPredicate) predicate).Invoke(c, this);
				return flag;
			}
			return true;
		}

		/// <summary>Calculates if a target is valid by testing the game state for each hardcoded requirement.
		/// </summary>
		/// <param name="c">The controller of the source.</param>
		/// <param name="target">The proposed target.</param>
		/// <returns><c>true</c> if the proposed target is valid, <c>false</c> otherwise.</returns>
		public bool TargetingRequirements(in Controller c, in ICharacter target)
		{
			// HsServer (SPACE): a spell aimed at a Location (client REQ_LOCATION_TARGET 131: Consume "a friendly location") takes
			// Locations only — no character (Model/PlayErrors: its other targets show REQ_LOCATION_TARGET)
			if (PlayRequirements != null && PlayRequirements.ContainsKey(Model.PlayErrors.REQ_LOCATION_TARGET))
				return target is Location location && !location.ToBeDestroyed && FailedTargetCheck(target) == 0;

			if (target.Card.Untouchable || target is Minion { Untouchable: true }) // HsServer: Dormant
				return false;

			if ((target.HasStealth || target.IsImmune) && target.Controller != c)
				return false;

			if (FailedTargetCheck(target) != 0) // HsServer: every requirement (Invoke on the multicast only checked the last)
				return false;

			return true;
		}

		public List<ICharacter> GetValidPlayTargets(in Controller c)
		{
			var output = new List<ICharacter>(2);

			if (!TargetingAvailabilityPredicate?.Invoke(c, this) ?? false)
				return output;

			bool friendlyMinions = false;
			bool enemyMinions = false;
			bool hero = false;
			bool enemyHero = false;
			switch (TargetingType)
			{
				case TargetingType.None:
					// If this is a non-targeting card, return an empty list
					return output;
				case TargetingType.All:
					friendlyMinions = true;
					enemyMinions = true;
					hero = true;
					enemyHero = true;
					break;
				case TargetingType.FriendlyCharacters:
					friendlyMinions = true;
					hero = true;
					break;
				case TargetingType.EnemyCharacters:
					enemyMinions = true;
					enemyHero = true;
					break;
				case TargetingType.AllMinions:
					friendlyMinions = true;
					enemyMinions = true;
					break;
				case TargetingType.FriendlyMinions:
					friendlyMinions = true;
					break;
				case TargetingType.EnemyMinions:
					enemyMinions = true;
					break;
				case TargetingType.Heroes:
					hero = true;
					enemyHero = true;
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}

			if (friendlyMinions)
			{
				var span = c.BoardZone.GetSpan();
				for (int i = 0; i < span.Length; i++)
					if (TargetingRequirements(in c, span[i]))
						output.Add(span[i]);
			}

			if (enemyMinions)
			{
				var span = c.Opponent.BoardZone.GetSpan();
				for (int i = 0; i < span.Length; i++)
					if (TargetingRequirements(in c, span[i]))
						output.Add(span[i]);
			}

			if (hero)
			{
				if (TargetingRequirements(in c, c.Hero))
					output.Add(c.Hero);
			}

			if (enemyHero)
			{
				if (TargetingRequirements(in c, c.Opponent.Hero))
					output.Add(c.Opponent.Hero);
			}

			return output;
		}


		/// <summary>
		/// Returns a string representation.
		/// </summary>
		/// <returns></returns>
		public override string ToString()
		{
			return $"[{Name}]";
		}
		/// <summary>
		/// Returns a substring of the name of this instance.
		/// </summary>
		/// <param name="size">The maximum length of the substring in # characters.</param>
		/// <returns></returns>
		public string AbbreviatedName(int size)
		{
			if (Name.Length <= size)
			{
				return Name;
			}
			else if (!Name.Contains(" "))
			{
				return Name.Substring(0, size);
			}
			else
			{
				string[] strArray = Name.Split(' ');
				return String.Join("", strArray.Select(p => p.Length > 4 ? p.Substring(0, 4) : p).ToList()).Substring(0, 7);
			}

		}

		/// <summary>
		/// Returns a string containing all information about this instance.
		/// </summary>
		/// <param name="gameTag"></param>
		/// <param name="playReq"></param>
		/// <returns></returns>
		public string FullPrint(bool gameTag = false, bool playReq = false)
		{
			var builder = new StringBuilder();
			builder.Append($"[CARD: {Name} - {Id} (Col={Collectible},Set={Set})]");
			if (gameTag)
			{
				builder.Append("\n   GameTags:");
				Tags.ToList().ForEach(pair => builder.Append($"\n   - {pair.Key} -> {pair.Value}"));
			}
			if (playReq)
			{
				builder.Append("\n   PlayReq:");
				PlayRequirements.ToList().ForEach(pair => builder.Append($"\n   - {pair.Key} -> {pair.Value}"));
			}
			return builder.ToString();
		}

		internal static readonly Card CardGame = new Card()
		{
			Id = "Game",
			Name = "Game",
			Tags = new Dictionary<GameTag, int> { [GameTag.CARDTYPE] = (int)CardType.GAME },
			//PlayRequirements = new Dictionary<PlayReq, int>(),
		};

		internal static readonly Card CardPlayer = new Card()
		{
			Id = "Player",
			Name = "Player",
			Tags = new Dictionary<GameTag, int> { [GameTag.CARDTYPE] = (int)CardType.PLAYER },
			//PlayRequirements = new Dictionary<PlayReq, int>(),
		};

		public Card Clone()
		{
			var clone = (Card)MemberwiseClone();
			clone.Tags = new Dictionary<GameTag, int>(Tags);
			clone.RefTags = new Dictionary<GameTag, int>(RefTags);
			return clone;
		}

		public static Card CreateZombeastCard(in Card firstCard, in Card secondCard, bool modifyTags)
		{
			Card zombeast = firstCard.Clone();
			zombeast.ATK += secondCard.ATK;
			zombeast.Health += secondCard.Health;
			zombeast.Cost += secondCard.Cost;
			zombeast.Taunt = secondCard.Taunt;
			zombeast.Poisonous = secondCard.Poisonous;
			zombeast.Stealth = secondCard.Stealth;
			zombeast.Windfury = secondCard.Windfury;
			zombeast.Charge = secondCard.Charge;
			zombeast.LifeSteal = secondCard.LifeSteal;
			zombeast.CantBeTargetedBySpells = secondCard.CantBeTargetedBySpells;
			zombeast.Rush = secondCard.Rush;
			zombeast.Echo = secondCard.Echo;
			zombeast.Name = "Zombeast";
			zombeast.Text = secondCard.Text + "\n" + zombeast.Text;

			zombeast.Tags.Add(GameTag.MODULAR_ENTITY_PART_1, firstCard.AssetId);
			zombeast.Tags.Add(GameTag.MODULAR_ENTITY_PART_2, secondCard.AssetId);

			if (modifyTags)
			{
				// HsServer: the client export leaves 0 out (a 0-Attack Beast has no ATK tag): add to what is there, else to 0
				foreach (GameTag stat in new[] { GameTag.ATK, GameTag.HEALTH, GameTag.COST })
					zombeast.Tags[stat] = (zombeast.Tags.TryGetValue(stat, out int had) ? had : 0) + secondCard[stat];
				if (secondCard.Tags.ContainsKey(GameTag.TAUNT))
					zombeast.Tags[GameTag.TAUNT] = 1;
				if (secondCard.Tags.ContainsKey(GameTag.POISONOUS))
					zombeast.Tags[GameTag.POISONOUS] = 1;
				if (secondCard.Tags.ContainsKey(GameTag.STEALTH))
					zombeast.Tags[GameTag.STEALTH] = 1;
				if (secondCard.Tags.ContainsKey(GameTag.WINDFURY))
					zombeast.Tags[GameTag.WINDFURY] = 1;
				if (secondCard.Tags.ContainsKey(GameTag.CHARGE))
					zombeast.Tags[GameTag.CHARGE] = 1;
				if (secondCard.Tags.ContainsKey(GameTag.LIFESTEAL))
					zombeast.Tags[GameTag.LIFESTEAL] = 1;
				if (secondCard.Tags.ContainsKey(GameTag.CANT_BE_TARGETED_BY_HERO_POWERS))
					zombeast.Tags[GameTag.CANT_BE_TARGETED_BY_HERO_POWERS] = 1;
				if (secondCard.Tags.ContainsKey(GameTag.CANT_BE_TARGETED_BY_SPELLS))
					zombeast.Tags[GameTag.CANT_BE_TARGETED_BY_SPELLS] = 1;
				if (secondCard.Tags.ContainsKey(GameTag.RUSH))
					zombeast.Tags[GameTag.RUSH] = 1;
				if (secondCard.Tags.ContainsKey(GameTag.ECHO))
					zombeast.Tags[GameTag.ECHO] = 1;

				// OVERRIDECARDNAME: 46119
				// OVERRIDECARDTEXTBUILDER: 14
				// RARITY: INVALID;
			}

			return zombeast;
		}

		public static Card CreateKazakusPotion(in Card firstCard, in Card secondCard, in Card thirdCard, bool modifyTags)
		{
			// TODO: Use placeholders
			Card potion = firstCard.Clone();

			potion.Text = secondCard.Text + "\n" + thirdCard.Text;

			foreach (KeyValuePair<PlayReq, int> kvp in secondCard.PlayRequirements)
			{
				if (!potion.PlayRequirements.ContainsKey(kvp.Key))
					potion.PlayRequirements.Add(kvp.Key, kvp.Value);
			}

			potion.MustHaveTargetToPlay = secondCard.MustHaveTargetToPlay;
			potion.TargetingType = secondCard.TargetingType;
			potion.PlayAvailabilityPredicate = secondCard.PlayAvailabilityPredicate;

			return potion;
		}

		public static Card GetTigerCard(int value, bool modifyTags)
		{
			Card instance = Cards.FromId("TRL_309t");
			instance.ATK = value;
			instance.Health = value;
			if (modifyTags)
			{
				instance.Tags[GameTag.ATK] = value;
				instance.Tags[GameTag.HEALTH] = value;
			}

			return instance;
		}
	}
}
