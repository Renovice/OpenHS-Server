using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>
		/// HsServer: <paramref name="source"/>'s "If …" (its card text's condition) held / didn't hold in its running effect block.
		/// The conditional machinery records it by itself (FlagTask: ComplexTask.If / Conditional, ConditionTask + FlagTask);
		/// call this from a script that checks its condition in code (a CustomTask: Master's Call). Read for the block's
		/// META_DATA EFFECT_SELECTION when the card's client effect is a SelectSpell (Loader.SelectFx; engine-rules.md
		/// "A card's chosen effect (EFFECT_SELECTION)"). The first answer of a block stands unless a script says it again.
		/// </summary>
		public static void ConditionHeld(IEntity source, bool held, bool overrule = true)
		{
			if (source?.Game is not Game game || !game.History) return;
			if (game.PowerHistory.EffectBlockOf(source.Id) is { } block && (overrule || block.ConditionHeld == null))
				block.ConditionHeld = held;
		}

		/// <summary>
		/// HsServer: which variant of its client effect <paramref name="source"/>'s running block plays — the client's
		/// SelectSpell table entry (RESEARCH/vfx/tools/select_spells.py → ClientSelectFx.tsv: a direction, a deck, a form).
		/// Sent as the block's META_DATA EFFECT_SELECTION (ClientStream). Without it a variant effect plays nothing (the client
		/// can't attach it), and a conditional one follows <see cref="ConditionHeld"/>.
		/// </summary>
		public static void SelectEffect(IEntity source, int selection)
		{
			if (selection < 0 || source?.Game is not Game game || !game.History) return;
			if (game.PowerHistory.EffectBlockOf(source.Id) is { } block)
				block.EffectSelection = selection;
		}

		/// <summary>HsServer: the SelectSpell variant for an effect between a minion and the neighbor it acts on: the client's
		/// "…Right" entry when the neighbor is to its right (a higher board position), "…Left" otherwise (Loader.SelectFx;
		/// -1: the card's table has no such entry).</summary>
		public static int SideSelection(IPlayable source, int effectIndex, IPlayable from, IPlayable to, bool trigger = false)
		{
			bool right = to.ZonePosition > from.ZonePosition;
			return Loader.SelectFx.IndexNamed(source.Card.Id, trigger ? Enums.BlockType.TRIGGER : Enums.BlockType.POWER, effectIndex,
				right ? "Right" : "Left", otherwiseSpell: true);
		}
	}
}
