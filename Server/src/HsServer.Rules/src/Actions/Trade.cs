// HsServer addition (not upstream SabberStone): the Tradeable keyword.
using System.Collections.Generic;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>Client tag ids the engine's 2021 GameTag enum doesn't name.</summary>
		public const GameTag TRADEABLE = (GameTag)1720, DECK_ACTION_COST = (GameTag)1743, IS_USING_TRADE_OPTION = (GameTag)2045;

		/// <summary>The client's history block for trading (HistoryBlock.Type.DECK_ACTION).</summary>
		public const BlockType DECK_ACTION = (BlockType)13;

		/// <summary>What trading this card costs (its DECK_ACTION_COST, else 1).</summary>
		public static int TradeCost(IPlayable card) => card[DECK_ACTION_COST] > 0 ? card[DECK_ACTION_COST] : 1;

		/// <summary>A Tradeable card in hand that can be traded now: enough mana and a deck to trade into.</summary>
		public static bool CanTrade(Controller c, IPlayable card) =>
			card[TRADEABLE] > 0 && card.Zone?.Type == Zone.HAND && card.Controller == c && !c.DeckZone.IsEmpty &&
			c.ManaToSpend >= TradeCost(card);

		/// <summary>
		/// Trade, as live (RESEARCH/captures/trade-lifesteal-20260930): one DECK_ACTION block (effect index 0) that spends the
		/// cost, reveals the card (IS_USING_TRADE_OPTION), shuffles it into the deck below the top card, waits
		/// (SLUSH_TIME 3000), then draws inside a nested POWER block; the card is hidden again at the end.
		/// </summary>
		/// <summary>HsServer (WHIZBANG): a card whose text says "(Trade to upgrade!)" (Wind-Up Musician / Sapling / Enforcer).</summary>
		public static bool TradeUpgrades(Model.Card card) => card?.Text?.Contains("to upgrade!)") == true && card.Text.Contains("Trade");

		public static bool Trade(Controller c, IPlayable card)
		{
			if (!CanTrade(c, card)) return false;
			Model.Game game = c.Game;
			int cost = TradeCost(card);
			if (game.History)
				game.PowerHistory.Add(PowerHistoryBuilder.BlockStart(DECK_ACTION, card.Id, "", 0, 0));
			c.UsedMana += cost;
			c.TotalManaSpentThisGame += cost;
			card[GameTag.JUST_PLAYED] = 1;
			card[IS_USING_TRADE_OPTION] = 1;
			card[GameTag.REVEALED] = 1;

			// Into the deck first, never on top: the draw can't bring the same card back.
			c.HandZone.Remove(card);
			card[GameTag.ZONE_POSITION] = 0; // live: out of the hand's order before it enters the deck
			c.DeckZone.Add(card, game.Random.Next(c.DeckZone.Count));
			if (game.History)
			{
				game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 3000, Info = new List<int> { card.Id } });
				game.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.POWER, card.Id, "", 0, 0));
			}
			// HsServer: Auctioneer Jaxon — "Whenever you Trade, Discover a card from your deck to draw instead"
			if (c.ControllerAuraEffects[HsTag.TRADE_DISCOVERS] > 0)
				DiscoverFromDeck(c, card, PickFromDeck(c, p => p != card));
			else
				Draw(c);
			// HsServer (WHIZBANG): "(Trade to upgrade!)" — its "@" (TAG_SCRIPT_DATA_NUM_1) +1, kept: Trade keeps the entity (Wind-Up cards)
			if (TradeUpgrades(card.Card))
				card[GameTag.TAG_SCRIPT_DATA_NUM_1] = System.Math.Max(1, card[GameTag.TAG_SCRIPT_DATA_NUM_1]) + 1;
			if (game.History)
				game.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());

			card[IS_USING_TRADE_OPTION] = 0;
			card[GameTag.REVEALED] = 0;
			if (card.Card.Power?.TradeTask is Tasks.ISimpleTask afterTrade) // "After you Trade this, ..." (Doggie Biscuit)
			{
				game.TaskQueue.Enqueue(afterTrade, c, card, null);
				game.ProcessTasks();
			}
			c.NumOptionsPlayedThisTurn++;
			if (game.History)
			{
				game.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
				game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 1000, Info = new List<int> { card.Id } });
			}
			game.DeathProcessingAndAuraUpdate();
			return true;
		}

	}
}
