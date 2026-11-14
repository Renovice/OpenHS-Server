using System;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>
		/// HsServer: <paramref name="action"/> (a destroy) inside the source card's Destroy effect, flying from the card to
		/// <paramref name="target"/> (SubSpells role Destroy: Natalie Seline, Runes of Blood, Gorm). No effect: just the action.
		/// </summary>
		public static void DestroyFx(IPlayable source, IEntity target, Action action) =>
			WithFx(source, Loader.SubSpells.Role.Destroy, target.Id, 0, action);

		/// <summary>
		/// HsServer: <paramref name="action"/> (the card gaining what it took) inside the source card's Absorb effect, flying
		/// from <paramref name="from"/> back into the card (SubSpells role Absorb: Natalie Seline, Cho'gall, Ancient Void Hound).
		/// </summary>
		public static void AbsorbFx(IPlayable source, IEntity from, Action action) =>
			WithFx(source, Loader.SubSpells.Role.Absorb, source.Id, from.Id, action);

		/// <summary>
		/// HsServer: make <paramref name="card"/> Temporary (keyword, wiki Temporary: "Discarded at the end of your turn"): the
		/// client's GBL_999e enchantment, which marks it (TEMPORARY, VALEERASHADOW: its outline) and discards it at the end of
		/// your turn if it's still in hand.
		/// </summary>
		public static void MakeTemporary(IPlayable card, IEntity source) =>
			AddEnchantmentBlock(card.Game, Model.Cards.TryFromId("GBL_999e") ?? Model.Cards.FromId("BOT_568e"), // (2021 data alone: its equivalent)
				(IPlayable)source, card, 0, 0, 0);

		/// <summary>
		/// HsServer: <paramref name="action"/> (what the card does to <paramref name="target"/>) inside the source card's
		/// Missile effect, from the card to the target (Patchwerk's cleaver at each card it destroys). No effect: just the action.
		/// </summary>
		public static void MissileFx(IPlayable source, IEntity target, Action action) =>
			WithFx(source, Loader.SubSpells.Role.Missile, target.Id, 0, action);

		/// <summary>
		/// HsServer: <paramref name="action"/> inside the source card's buff / area effect (SubSpells.WholeEffect), its targets
		/// what it affects. For a card that places its effect itself (Power.OwnFx: Overseer Frigidara's blast only when it
		/// deals damage). No effect: just the action.
		/// </summary>
		/// <summary>HsServer (2026-10-08): the source's effect runs in a TRIGGER block now (its trigger), not its play: the effect recorded for
		/// that block is the one (Craftsman's Hammer: "whenever your hero attacks" — its armor effect only in its TRIGGER block).</summary>
		private static bool InTriggerBlock(IPlayable source) => source?.Game.PowerHistory.CurrentEffectBlock?.BlockType == Enums.BlockType.TRIGGER;

		public static void WholeFx(IPlayable source, Action action)
		{
			string prefab = source != null ? Loader.SubSpells.WholeEffect(source.Card.Id, InTriggerBlock(source)) : null; // (2026-10-08: the running block's)
			bool open = prefab != null && source.Game.OpenSubSpell(source, prefab, null,
				shownSource: source is Spell ? source.Controller.Hero.Id : 0, collectTargets: true);
			action();
			if (open) source.Game.CloseSubSpells(source.Id);
		}

		/// <summary>
		/// HsServer: the splash of a targeted effect — what it does to the characters other than its chosen target ("Deal 3
		/// damage to an enemy … Deal 1 damage to all other enemies"). Live (Howling Blast, live-20261005): the chosen target's
		/// hit is the card's own client effect (the block's TARGET names only it); the splash plays inside the card's recorded
		/// area effect, from where live plays it (a spell: its hero), with its own TARGET naming what it hits. A card with no
		/// recorded area effect (Swipe, and Explosive Shot whose client effect hits the neighbours itself): just the action.
		/// </summary>
		public static void SplashFx(IPlayable source, Action action)
		{
			string prefab = source != null ? Loader.SubSpells.WholeEffect(source.Card.Id, InTriggerBlock(source)) : null; // (2026-10-08: the running block's)
			Model.Game g = source?.Game;
			bool open = prefab != null && g.OpenSubSpell(source, prefab, null, shownSource: source is Spell ? source.Controller.Hero.Id : 0);
			if (open) g.PowerHistory.NewTargetRound();
			action();
			if (open) g.CloseSubSpells(source.Id);
		}

		/// <summary>
		/// HsServer: <paramref name="action"/> inside the source card's own effect whose name contains <paramref name="part"/>
		/// (an effect whose name gives no role: Felsoul Jailer's "FelFire", Gnomelia's "AdjacentBurst"), at
		/// <paramref name="targets"/>. Where live plays these is unrecorded: at what the action affects.
		/// </summary>
		/// <summary>
		/// HsServer: name what an effect is about, one TARGET each, first in its block — for a card whose client effect reads
		/// its TARGETs itself (live Brawl: every minion, the survivor first; the client's BrawlSpell takes the TARGET that
		/// isn't TO_BE_DESTROYED as the survivor). The engine then adds no TARGET of its own to the block.
		/// </summary>
		public static void NameTargets(SabberStoneCore.Model.Game g, System.Collections.Generic.IEnumerable<int> ids)
		{
			if (!g.History) return;
			foreach (int id in ids)
				g.PowerHistory.Add(new SabberStoneCore.Kettle.PowerHistoryMetaData
				{
					Type = SabberStoneCore.Enums.MetaDataType.TARGET, Data = 0, Info = new System.Collections.Generic.List<int> { id },
				});
		}

		public static void NamedFx(IPlayable source, string part, System.Collections.Generic.IEnumerable<int> targets, Action action)
		{
			string prefab = source != null ? Loader.SubSpells.FindNamed(source.Card.Id, part) : null;
			bool open = prefab != null && source.Game.OpenSubSpell(source, prefab, targets);
			action();
			if (open) source.Game.CloseSubSpells(source.Id);
		}

		private static void WithFx(IPlayable source, Loader.SubSpells.Role role, int target, int shownSource, Action action)
		{
			string prefab = source != null ? Loader.SubSpells.Find(source.Card.Id, role) : null;
			bool open = prefab != null && source.Game.OpenSubSpell(source, prefab, new[] { target }, shownSource: shownSource);
			action();
			if (open) source.Game.CloseSubSpells(source.Id);
		}
	}
}
