// HsServer addition (not upstream SabberStone): the Forge keyword.
using System.Collections.Generic;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>Client tag ids for Forge (the engine's 2021 GameTag enum doesn't name them).</summary>
		public const GameTag FORGE = (GameTag)2785, FORGED = (GameTag)3011, FORGES_INTO = (GameTag)3074,
			IS_USING_FORGE_OPTION = (GameTag)2869, FORGE_REVEALED = (GameTag)3070;

		/// <summary>What forging this card costs: its DECK_ACTION_COST (wiki Forge: "Forging always costs 2 mana").</summary>
		public static int ForgeCost(IPlayable card) => card[DECK_ACTION_COST] > 0 ? card[DECK_ACTION_COST] : 2;

		/// <summary>The card a Forge card turns into (client tag FORGES_INTO = its dbf id), or null.</summary>
		public static Card ForgedCard(IPlayable card) =>
			card[FORGE] > 0 && card[FORGES_INTO] > 0 ? Cards.FromAssetId(card[FORGES_INTO]) : null;

		/// <summary>A Forge card in its owner's hand that can be forged now: enough mana (wiki: forging needs no deck).</summary>
		public static bool CanForge(Controller c, IPlayable card) =>
			card.Zone?.Type == Zone.HAND && card.Controller == c && ForgedCard(card) != null && c.ManaToSpend >= ForgeCost(card);

		/// <summary>
		/// Forge (wiki Forge; live-20261002-forge-drink, Mechagnome Guide): drag the card to the deck, pay 2 mana, and it is
		/// permanently transformed into its Forged version in the same hand slot, keeping its in-hand enchantments. Not a
		/// play (no play triggers, not counted as a card played) and not a shuffle. Live: one DECK_ACTION block (effect index
		/// 0): the mana, JUST_PLAYED, IS_USING_FORGE_OPTION and FORGE_REVEALED, SLUSH_TIME 3000, then a POWER block with
		/// TRANSFORMED_FROM_CARD, META TARGET on the card and the CHANGE_ENTITY into the forged card (FORGED=1); then the
		/// flags off and the options counter; SLUSH_TIME 1000 after the block.
		/// </summary>
		public static bool Forge(Controller c, IPlayable card)
		{
			if (!CanForge(c, card)) return false;
			Game game = c.Game;
			int cost = ForgeCost(card);
			Card forged = ForgedCard(card);
			int from = card.Card.AssetId;
			if (game.History)
				game.PowerHistory.Add(PowerHistoryBuilder.BlockStart(DECK_ACTION, card.Id, "", 0, 0));
			c.UsedMana += cost;
			c.TotalManaSpentThisGame += cost;
			card[GameTag.JUST_PLAYED] = 1;
			card[IS_USING_FORGE_OPTION] = 1;
			card[FORGE_REVEALED] = 1;
			if (game.History)
			{
				game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 3000, Info = new List<int> { card.Id } });
				game.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.POWER, card.Id, "", 0, 0));
			}
			card[GameTag.TRANSFORMED_FROM_CARD] = from;
			if (game.History)
				game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.TARGET, Data = 0, Info = new List<int> { card.Id } });
			IPlayable result = ChangeEntityBlock.Invoke(c, card, forged, false); // keeps in-hand enchantments (wiki)
			result[FORGED] = 1;
			if (game.History)
				game.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			result[IS_USING_FORGE_OPTION] = 0;
			result[GameTag.JUST_PLAYED] = 0;
			result[FORGE_REVEALED] = 0;
			c.NumOptionsPlayedThisTurn++;
			if (game.History)
			{
				game.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
				game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 1000, Info = new List<int> { result.Id } });
			}
			c[HsTag.FORGES_THIS_GAME] += 1; // "If you've Forged a card this game" (Ignis)
			// "after you Forge a card" (Melted Maker)
			game.TaskQueue.StartEvent();
			game.TriggerManager.OnForgeTrigger(result);
			game.ProcessTasks();
			game.TaskQueue.EndEvent();
			game.DeathProcessingAndAuraUpdate();
			return true;
		}
	}
}
