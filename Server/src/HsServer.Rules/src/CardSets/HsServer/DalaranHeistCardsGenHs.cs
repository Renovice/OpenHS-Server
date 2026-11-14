// HsServer addition (not upstream SabberStone): The Dalaran Heist (2019 solo adventure) — its own cards: the bosses' Hero Powers
// (Normal "p" and Heroic "px"), the boss-only deck cards, the treasures and passives, the player heroes' Hero Powers, the chapter twists
// and Bob's Tavern. One hook per chunk file (DalaranHeistCardsGenHs1..5.cs); a chunk not written yet is simply absent (C# partial method).
// The run itself (save data, decks, bosses) is the server's: Server/src/HsServer/Lobby/HeistRuns.cs.
using System.Collections.Generic;
using SabberStoneCore.Model;
using SabberStoneCore.src.Loader;

namespace SabberStoneCore.CardSets.HsServer
{
	public static partial class CurrentCoreCardsGen
	{
		private static void DalaranHeist(IDictionary<string, CardDef> cards)
		{
			HeistBossPowers1(cards); // DalaranHeistCardsGenHs1.cs: bosses DALA_BOSS_01..25 (Hero Powers + their boss-only cards)
			HeistBossPowers2(cards); // DalaranHeistCardsGenHs2.cs: bosses DALA_BOSS_26..50
			HeistBossPowers3(cards); // DalaranHeistCardsGenHs3.cs: bosses DALA_BOSS_51..75 + boss-only deck cards of other sets
			HeistTreasures(cards);   // DalaranHeistCardsGenHs4.cs: treasures, passives, player Hero Powers, chapter twists
			HeistTavern(cards);      // DalaranHeistCardsGenHs5.cs: Bob's Tavern (DALA_BOSS_99h / 98h, DALA_901..914)
			HeistAnomalies(cards);   // DalaranHeistCardsGenHs6.cs: Anomaly Mode's anomalies (DALA_850..867) and their enchantments
		}

		static partial void HeistBossPowers1(IDictionary<string, CardDef> cards);
		static partial void HeistBossPowers2(IDictionary<string, CardDef> cards);
		static partial void HeistBossPowers3(IDictionary<string, CardDef> cards);
		static partial void HeistTreasures(IDictionary<string, CardDef> cards);
		static partial void HeistTavern(IDictionary<string, CardDef> cards);
		static partial void HeistAnomalies(IDictionary<string, CardDef> cards);
	}
}
