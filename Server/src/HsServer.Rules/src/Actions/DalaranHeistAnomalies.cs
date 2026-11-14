// HsServer addition (not upstream SabberStone): The Dalaran Heist's Anomaly Mode (2026-10-09) — the run's anomaly (DALA_850-867,
// "Anomaly - Arcane" …) played as each boss game starts, its "… Player Enchantment" on one player (an aura over both) or on each
// player (a trigger of that player's own events), and a played spell's copy cast by an enchantment (Wild Magic). Card scripts:
// CardSets/HsServer/DalaranHeistCardsGenHs6.cs; the run side (which anomaly, the save keys): Server/src/HsServer/Lobby/HeistRuns.cs.
// Evidence: the decoded live anomaly games (live install sessions 2026-09-29 … 2026-10-08), cited per rule.
using System.Collections.Generic;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>
		/// The run's anomaly (GameConfig.Player1Anomaly: the run's player) is played once the mulligan is done, right after the passives
		/// (Game.MainBegin). Live (8 sessions, every anomaly game — e.g. live-session-20261003-185040 5565-5672 after the passive block
		/// 5508-5564; 20260929-122856 5937-6059 Gorged; 20261004-222041 1412-1456 Crying): ONE game TRIGGER block (effect index −1) creating
		/// the anomaly twice (CONTROLLER = the run's player, CREATOR = the game, ZONE SETASIDE): the first stays set aside, the second is cast
		/// like a passive (PLAY, REVEALED, SHOW_BIG_CARD, HISTORY_TARGET, SLUSH_TIME 3000, its POWER block, GRAVEYARD, SLUSH_TIME 1000), then
		/// BEGIN_ARTIFICIAL_HISTORY_TILE and OVERRIDE_HISTORY name the set-aside one — and, unlike a passive, no END_ARTIFICIAL_HISTORY_TILE.
		/// </summary>
		public static void PlayAnomaly(Game g, Controller c, Card anomaly)
		{
			if (anomaly == null || c == null) return;
			if (g.History) g.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, g.Id, "", -1, 0));
			IPlayable shown = Entity.FromCard(c, anomaly, new EntityData { [GameTag.CREATOR] = g.Id }, c.SetasideZone); // the history's copy
			CastAtGameStart(g, c, anomaly, bigCard: true);
			if (g.History)
			{
				var tile = new List<int> { shown.Id };
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.BEGIN_ARTIFICIAL_HISTORY_TILE, Data = 0, Info = tile });
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.OVERRIDE_HISTORY, Data = 0, Info = tile });
				g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			}
			g.DeathProcessingAndAuraUpdate();
		}

		/// <summary>
		/// "… Player Enchantment" for each player, the anomaly's controller first: an anomaly whose rule is a trigger of a player's own
		/// events ("At the end of each player's turn, that player draws …", "All spells cast an additional time"). Live (Refreshing:
		/// live-session-20261004-224614 91392-91443; Wild Magic: 20260929-122856 11390-11420): two enchantments in the anomaly's POWER
		/// block, ATTACHED = player 1, then player 2, each triggering for its own player (the second's trigger: DALA_856e id 76 on
		/// player 2 casts player 2's spells again). Ours belong to the player they are on (live sends CONTROLLER = the anomaly's
		/// player for both: the client shows nothing of it).
		/// </summary>
		public static void GiveEachPlayer(Game g, IPlayable creator, Card enchantment)
		{
			if (enchantment == null || creator == null) return;
			foreach (Controller p in new[] { creator.Controller, creator.Controller.Opponent })
				AddEnchantmentBlock(g, enchantment, creator, p, 0, 0, 0, owner: p);
		}

		/// <summary>
		/// "All spells cast an additional time. (targets chosen randomly)" (Anomaly - Wild Magic): after <paramref name="played"/> is
		/// played and resolved, <paramref name="granter"/> (the player's DALA_856e) casts a NEW copy of it, targets chosen randomly.
		/// Live (live-session-20260929-122856 12943-12985, 46 plays in 3 sessions, every one inside the spell's PLAY block): the
		/// enchantment's TRIGGER block — the copy (CREATOR and 1715 = the enchantment) cast like an effect's random cast (SHOW_BIG_CARD,
		/// HISTORY_TARGET, SLUSH_TIME 3000, POWER, GRAVEYARD, SLUSH_TIME 1000), then a copy of the anomaly in REMOVEDFROMGAME (CREATOR = the
		/// enchantment) and BEGIN_ARTIFICIAL_HISTORY_TRIGGER_TILE / OVERRIDE_HISTORY (it) / HISTORY_TARGET (the played spell). Only spells a
		/// player plays (live: 46 of 46 from PLAY blocks): the copy, cast by an effect, doesn't cast again.
		/// </summary>
		public static IPlayable CastPlayedCopy(Controller c, IPlayable granter, IPlayable played)
		{
			if (played == null || played.Card.Type != CardType.SPELL || granter == null) return null;
			Game g = c.Game;
			IPlayable copy = Entity.FromCard(c, played.Card, new EntityData { [GameTag.CREATOR] = granter.Id, [(GameTag)1715] = granter.Id });
			new Tasks.SimpleTasks.CastRandomSpellTask().Process(g, c, granter, copy);
			if (g.History)
				ArtificialTriggerTile(c, granter, granter is Enchantment e ? e.Creator : granter, played);
			return copy;
		}

		/// <summary>
		/// The history tile of an effect an enchantment makes on someone else's behalf: a copy of <paramref name="creator"/>'s card (the
		/// card that gave the enchantment) in REMOVEDFROMGAME (CREATOR = <paramref name="granter"/>), META BEGIN_ARTIFICIAL_HISTORY_TRIGGER_TILE
		/// and OVERRIDE_HISTORY (it), HISTORY_TARGET <paramref name="historyTarget"/>. Live: Jotun's cast (Generic.CastDrawnCopy), Wild Magic
		/// (<see cref="CastPlayedCopy"/>). History only.
		/// </summary>
		internal static void ArtificialTriggerTile(Controller c, IPlayable granter, IPlayable creator, IPlayable historyTarget)
		{
			Game g = c.Game;
			int tileId = 0;
			if (creator != null)
				tileId = Entity.FromCard(c, creator.Card, new EntityData
				{
					[GameTag.CREATOR] = granter.Id,
					[GameTag.ZONE] = (int)Zone.REMOVEDFROMGAME,
				}).Id;
			g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.BEGIN_ARTIFICIAL_HISTORY_TRIGGER_TILE, Data = 0, Info = new List<int> { tileId } });
			if (tileId > 0)
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.OVERRIDE_HISTORY, Data = 0, Info = new List<int> { tileId } });
			g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.HISTORY_TARGET, Data = 0, Info = new List<int> { historyTarget.Id } });
		}
	}
}
