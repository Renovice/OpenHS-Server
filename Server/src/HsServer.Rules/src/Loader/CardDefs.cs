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
using SabberStoneCore.CardSets;
using SabberStoneCore.CardSets.Adventure;
using SabberStoneCore.CardSets.Standard;

//using SabberStoneCore.CardSets.TavernBrawl;
//using SabberStoneCore.CardSets.Adventure;

namespace SabberStoneCore.src.Loader
{
	///// <summary>
	///// Interface for instances which support delayed removal.
	///// This interface is used to remove <see cref="OldEnchant"/>s after various
	///// effects were calculated.
	///// </summary>
	///// <seealso cref="Game.LazyRemoves"/>
	//public interface ILazyRemove
	//{
	//	/// <summary>Initiates removal of the implemented type.
	//	/// The instance will remove itself from the game.
	//	/// </summary>
	//	void Remove();
	//}

	internal class CardDefs
	{
		private static CardDefs _instance;

		/// <summary>
		/// Contains all defined powers.
		/// The index is the CARDID of the card which powers are defined.
		/// </summary>
		private readonly Dictionary<string, CardDef> _cardDefsDic;

		/// <summary>HsServer: a definition that does something (same test as CardContainer's Implemented).</summary>
		private static bool HasEffect(CardDef d) => d.Power != null && (d.Power.PowerTask != null || d.Power.DeathrattleTask != null ||
			d.Power.ComboTask != null || d.Power.TopdeckTask != null || d.Power.OverkillTask != null || d.Power.Aura != null ||
			d.Power.Trigger != null || d.Power.Enchant != null);

		/// <summary>HsServer (WONDERS): the ids HsServer's own card sets define (CardSets/HsServer): a script written for the current
		/// text. A stale 2021 definition is shared with a "counts as a copy" reprint only when it is one of these (CardContainer).</summary>
		internal static readonly HashSet<string> HsServerIds = new HashSet<string>();

		private static Dictionary<string, CardDef> HsServerCards()
		{
			var defs = new Dictionary<string, CardDef>();
			CardSets.HsServer.CurrentCoreCardsGen.AddAll(defs);
			return defs;
		}

		/// <summary>
		/// Private constructor as per Singleton pattern.
		/// </summary>
		private CardDefs()
		{
			_cardDefsDic = new Dictionary<string, CardDef>();

			// Standard
			CoreCardsGen.AddAll(_cardDefsDic);
			Expert1CardsGen.AddAll(_cardDefsDic);

			GilneasCardsGen.AddAll(_cardDefsDic);
			BoomsdayCardsGen.AddAll(_cardDefsDic);
			TrollCardsGen.AddAll(_cardDefsDic);
			DalaranCardsGen.AddAll(_cardDefsDic);
			UldumCardsGen.AddAll(_cardDefsDic);
			DragonsCardsGen.AddAll(_cardDefsDic);

			// Rest
			NaxxCardsGen.AddAll(_cardDefsDic);
			GvgCardsGen.AddAll(_cardDefsDic);
			BrmCardsGen.AddAll(_cardDefsDic);
			TgtCardsGen.AddAll(_cardDefsDic);
			LoeCardsGen.AddAll(_cardDefsDic);
			GangsCardsGen.AddAll(_cardDefsDic);
			KaraCardsGen.AddAll(_cardDefsDic);
			OgCardsGen.AddAll(_cardDefsDic);
			UngoroCardsGen.AddAll(_cardDefsDic);
			IcecrownCardsGen.AddAll(_cardDefsDic);
			LootapaloozaCardsGen.AddAll(_cardDefsDic);

			HofCardsGen.AddAll(_cardDefsDic);

			// HsServer: current Core cards the 2021 engine never had (adds or replaces definitions)
			if (SabberStoneCore.Loader.ReprintCards.DbfDirectory != null) // these cards exist only with the client DB
			{
				var playsAs = new List<KeyValuePair<string, CardDef>>();
				foreach (var def in HsServerCards())
				{
					if (def.Value.PlaysAs != null) { playsAs.Add(def); HsServerIds.Add(def.Key); continue; }
					// An HsServer script without its own entourage or play requirements keeps upstream's (the card data has neither:
					// Elite Tauren Chieftain's Power Chords and Gelbin's inventions vanished, "ETC does literally nothing", user
					// 2026-10-08; Rogues Do It... lost REQ_TARGET_TO_PLAY). A copy: one definition can serve several ids.
					if (_cardDefsDic.TryGetValue(def.Key, out CardDef upstream)
					    && (def.Value.Entourage == null && upstream.Entourage != null || def.Value.PlayReqs == null && upstream.PlayReqs != null))
						_cardDefsDic[def.Key] = new CardDef(def.Value.Entourage ?? upstream.Entourage, def.Value.PlayReqs ?? upstream.PlayReqs, def.Value.Power);
					else
						_cardDefsDic[def.Key] = def.Value;
					HsServerIds.Add(def.Key);
					// The original of a scripted Core reprint plays the same (TRL_345 = CORE_TRL_345): give it the script
					// when upstream left it empty.
					if (def.Key.StartsWith("CORE_") && def.Key.Length > 5)
					{
						string original = def.Key.Substring(5);
						if ((!_cardDefsDic.TryGetValue(original, out CardDef old) || !HasEffect(old)) && SabberStoneCore.Loader.ReprintCards.SameClientText(def.Key, original))
							_cardDefsDic[original] = def.Value;
					}
				}
				// HsServer: "plays like" links (CardDef.Like), once every definition is in
				foreach (var def in playsAs)
					if (_cardDefsDic.TryGetValue(def.Value.PlaysAs, out CardDef same))
						_cardDefsDic[def.Key] = same;
			}

			// HsServer: engine cards that are copies of a scripted card under another id (same text in the client).
			foreach (var (copy, original) in new[]
			         {
				         ("CORE_FB_Champs_LOOT_080", "LOOT_080"), // Lesser Emerald Spellstone (Brawl)
				         ("SW_084", "CORE_FIX_SW_084"), // Bloodbound Imp (the Core fix has the same text)
			         })
				if (!_cardDefsDic.ContainsKey(copy) && _cardDefsDic.TryGetValue(original, out CardDef originalDef))
					_cardDefsDic[copy] = originalDef;

			// HsServer: the original of any scripted Core reprint (not only ours) plays the same when the client texts match.
			if (SabberStoneCore.Loader.ReprintCards.DbfDirectory != null)
				foreach (string core in _cardDefsDic.Keys.Where(k => k.StartsWith("CORE_") && k.Length > 5).ToList())
				{
					string original = core.Substring(5);
					if ((!_cardDefsDic.TryGetValue(original, out CardDef old) || !HasEffect(old)) && HasEffect(_cardDefsDic[core])
					    && SabberStoneCore.Loader.ReprintCards.SameClientText(core, original))
						_cardDefsDic[original] = _cardDefsDic[core];
				}

			// HsServer: a Legacy reprint (LEG_RLK_071 = RLK_071, LEG_CS3_031 = CS3_031) and its original play the same when the
			// client texts match: whichever of the two is scripted gives the other its script (2026-10-04; was AddWithLegacy,
			// card by card).
			if (SabberStoneCore.Loader.ReprintCards.DbfDirectory != null)
				foreach (string id in _cardDefsDic.Keys.Where(k => HasEffect(_cardDefsDic[k])).ToList())
				{
					string other = id.StartsWith("LEG_") ? id.Substring(4) : "LEG_" + id;
					if ((!_cardDefsDic.TryGetValue(other, out CardDef old) || !HasEffect(old))
					    && SabberStoneCore.Loader.ReprintCards.SameClientText(id, other))
						_cardDefsDic[other] = _cardDefsDic[id];
				}

			// HsServer (WONDERS, 2026-10-07): a "counts as a copy" pair (client tag 858, the same current text: Caverns of Time
			// WON_x and its Wild original) plays the same. The copy borrows the original's script (ReprintCards.CanonicalOf); a
			// script written under the copy's own id serves the original too when the original's is missing or stale (its 2021
			// script was written for an older text and no HsServer script replaced it).
			if (SabberStoneCore.Loader.ReprintCards.DbfDirectory != null)
				foreach (string id in HsServerIds.Where(k => _cardDefsDic.TryGetValue(k, out CardDef d) && HasEffect(d)).ToList())
				{
					string original = SabberStoneCore.Loader.ReprintCards.CountsAsCopyOf(id);
					if (original == null || HsServerIds.Contains(original)) continue;
					if (!_cardDefsDic.TryGetValue(original, out CardDef old) || !HasEffect(old) || SabberStoneCore.Loader.ReprintCards.IsStaleScript(original))
						_cardDefsDic[original] = _cardDefsDic[id];
				}

			// Tavern Brawl
			//TbCardsGen.AddAll(_powerDic);

			// ****************************************
			// * DO NOT UNCOMMENT THEM BEFORE REMOVING
			// * REDUNDANT CARD IMPLEMENTATIONS IN THE
			// * ADV SET!!!
			// ****************************************
			// Adventure
			//BrmCardsGenAdv.AddAll(_powerDic);
			//NaxxCardsGenAdv.AddAll(_powerDic);
			//LoeCardsGenAdv.AddAll(_powerDic);
			//KaraCardsGenAdv.AddAll(_powerDic);
			//IcecrownCardsGenAdv.AddAll(_powerDic);
			//LootapaloozaCardsGenAdv.AddAll(_cardDefsDic);
		}

		/// <summary>
		/// Use the Singleton pattern to provide the same powers to each requester.
		/// </summary>
		public static CardDefs Instance => _instance ?? (_instance = new CardDefs());

		/// <summary>
		/// 
		/// </summary>
		/// <returns></returns>
		public IReadOnlyDictionary<string, CardDef> Get => _cardDefsDic;
	}
}
