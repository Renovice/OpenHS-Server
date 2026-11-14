// HsServer: a card's turn-time limit on the wire (Nozdormu).

using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>
		/// A turn-time limit (Controller.TurnLimitSeconds: Nozdormu's aura) is both players' TIMEOUT while it lasts.
		/// - Advanced rulebook, Nozdormu and AFK: "This is implemented by changing the TIMEOUT tag between these two numbers -
		///   whenever it changes, the timer is reset" (the server's turn timer: HsServer RopeClock).
		/// - Live (live-20261008-issues, GT_VS_AI): TIMEOUT 20 on both players inside Nozdormu's summon; TIMEOUT 0 on both after
		///   the DEATHS block he left play in (a solo game has no TIMEOUT otherwise). A timed game gets its own TIMEOUT back.
		/// </summary>
		public static void SyncTurnLimit(Game g)
		{
			if (g.Player1 == null || g.Player2 == null) return;
			SyncTurnLimit(g, g.Player1);
			SyncTurnLimit(g, g.Player2);
		}

		private static void SyncTurnLimit(Game g, Controller c)
		{
			int? limit = c.TurnLimitSeconds;
			bool shown = c[HsTag.TURN_LIMIT_SHOWN] > 0;
			if (limit is int seconds)
			{
				if (!shown)
				{
					c[HsTag.TURN_SECONDS_BASE] = c.TimeOut;
					c[HsTag.TURN_LIMIT_SHOWN] = 1;
				}
				if (c.TimeOut != seconds) c.TimeOut = seconds;
			}
			else if (shown)
			{
				c[HsTag.TURN_LIMIT_SHOWN] = 0;
				c.TimeOut = g.TurnTimer ? c[HsTag.TURN_SECONDS_BASE] : 0;
				c[HsTag.TURN_SECONDS_BASE] = 0;
			}
		}
	}
}
