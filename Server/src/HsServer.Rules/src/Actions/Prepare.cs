// HsServer addition (not upstream SabberStone): the Prepare keyword (Escape from Violet Hold, set 1988). Tags: client PREPARE family;
// internal VIOLET_HOLD range Enums/HsTag.cs 990750-990799.
using System;
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>Client tags: PREPARE (4354, the keyword), IS_USING_PREPARE_OPTION (4450), PREPARE_REVEALED (4459), PREPARING (4726),
		/// PREPARED (4727).</summary>
		public const GameTag PREPARE = (GameTag)4354, IS_USING_PREPARE_OPTION = (GameTag)4450, PREPARE_REVEALED = (GameTag)4459,
			PREPARING = (GameTag)4726, PREPARED = (GameTag)4727;

		/// <summary>Client tags: DISGUISED (4690, "Can be played on either side"), IS_USING_DISGUISED_OPTION (4693).</summary>
		public const GameTag DISGUISED = (GameTag)4690, IS_USING_DISGUISED_OPTION = (GameTag)4693;

		/// <summary>The Mana Preparing this card spends: at least its deck action cost (1), at most what brings it to 0 (wiki Prepare: "If
		/// the player has more Mana than the total reduction would cost, then Preparing will only spend the amount of Mana needed for the
		/// card to reach 0"; live: spent N → discount N + 1).</summary>
		public static int PrepareSpend(Controller c, IPlayable card) =>
			Math.Max(TradeCost(card), Math.Min(c.RemainingMana, Math.Max(0, card.Cost - 1)));

		/// <summary>A Prepare card in its owner's hand that can be Prepared now (wiki: "The player must have at least 1 Mana"; "Cards can
		/// only be Prepared once"; live L1 75780: a 0-Cost card gets REQ_PREPARE).</summary>
		public static bool CanPrepare(Controller c, IPlayable card) =>
			card[PREPARE] > 0 && card.Zone?.Type == Zone.HAND && card.Controller == c && card.Cost > 0 && c.ManaToSpend >= TradeCost(card);

		/// <summary>
		/// Prepare (wiki Prepare: "Drag to your deck to spend leftover Mana. Next turn, discount this by the amount spent (plus 1 extra!)";
		/// not a play; not shuffled — it stays in the hand). Live (chunk 2 L1, friendly; dk-classxp 31924 opponent): one DECK_ACTION block —
		/// the mana, JUST_PLAYED, IS_USING_PREPARE_OPTION, PREPARE_REVEALED = the spend, SLUSH_TIME 3000, a POWER block with JAIL_907e02
		/// "Preparing" (the lock: LITERALLY_UNPLAYABLE, UNPLAYABLE_VISUALS, PREPARING; gone at the end of the turn) and JAIL_907e "Prepared"
		/// (the discount, removed when played), PREPARE 0, PREPARED = the discount; then the flags off, the options counter, SLUSH_TIME 1000.
		/// "When you Prepare while holding this" (Jailbird): Power.AfterPrepareTask of the other hand cards (HsTag.LAST_PREPARE_SPENT).
		/// </summary>
		public static bool Prepare(Controller c, IPlayable card)
		{
			if (!CanPrepare(c, card)) return false;
			Game game = c.Game;
			int spend = PrepareSpend(c, card), discount = spend + 1;
			if (game.History)
				game.PowerHistory.Add(PowerHistoryBuilder.BlockStart(DECK_ACTION, card.Id, "", 0, 0));
			c.UsedMana += spend;
			c.TotalManaSpentThisGame += spend;
			card[GameTag.JUST_PLAYED] = 1;
			card[IS_USING_PREPARE_OPTION] = 1;
			card[PREPARE_REVEALED] = spend;
			if (game.History)
			{
				game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 3000, Info = new List<int> { card.Id } });
				var block = PowerHistoryBuilder.BlockStart(BlockType.POWER, card.Id, "", 0, 0);
				block.ShowInHistory = true;
				game.PowerHistory.Add(block);
			}
			LockInHand(card, card, game.Turn, "JAIL_907e02");
			card[PREPARING] = 1;
			if (Cards.TryFromId("JAIL_907e") is Card prepared) AddEnchantmentBlock(game, prepared, card, card, discount, 0, 0);
			card[PREPARE] = 0;
			card[PREPARED] = discount;
			if (game.History)
			{
				game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.OVERRIDE_HISTORY, Data = 0, Info = new List<int> { card.Id } });
				game.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			}
			card[IS_USING_PREPARE_OPTION] = 0;
			card[GameTag.JUST_PLAYED] = 0;
			card[PREPARE_REVEALED] = 0;
			c.NumOptionsPlayedThisTurn++;
			if (game.History)
			{
				game.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
				game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 1000, Info = new List<int> { card.Id } });
			}
			c[HsTag.LAST_PREPARE_SPENT] = spend;
			foreach (IPlayable held in c.HandZone.GetAll().Where(p => p.Id != card.Id && p.Card.Power?.AfterPrepareTask != null).ToList())
			{
				game.TaskQueue.StartEvent();
				game.TaskQueue.Execute(held.Card.Power.AfterPrepareTask, c, held, card, 0, 0, 0);
				game.ProcessTasks();
				game.TaskQueue.EndEvent();
			}
			game.DeathProcessingAndAuraUpdate();
			return true;
		}
	}
}
