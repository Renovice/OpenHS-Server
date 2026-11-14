using System;
using System.Linq;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>
		/// HsServer: spend <paramref name="n"/> Corpses for <paramref name="source"/>'s effect, or nothing when there aren't
		/// enough. Call it where the card's text spends (engine-rules.md "Effects happen in the order of the text"). Live
		/// plays the card's own Corpse effect around the spend (Grave Strength: a SUB_SPELL
		/// ReuseFX_Necrotic_DeathKnight_AE_ConsumeCorpse_Super from the hero, around CORPSES_SPENT_THIS_GAME); what the card
		/// did before it stays in its buff / area effect, the rest goes in a new one after it (live Blood Tap: HandBuff,
		/// ConsumeCorpse, HandBuff; live-20261004-corpses). Both are recorded per card (Loader.SubSpells, roles
		/// ConsumeCorpse and the whole effect).
		/// </summary>
		public static bool SpendCorpses(Controller c, IEntity source, int n)
		{
			if (n <= 0 || c.Corpses < n)
			{
				CorpseStepDone(c, source);
				return false;
			}
			SpendCorpsesFx(c, source, n);
			return true;
		}

		/// <summary>Live's Corpse gain effect (recorded: Plagued Grain RLK_039 from its hero, Meat Grinder RLK_120 from itself).</summary>
		public const string GenericGainCorpse = "ReuseFX_Necrotic_DeathKnight_BuffImpact_GainCorpse_Super:d10fb7a6897bc214399b5c7b42f22026";

		/// <summary>
		/// HsServer: <paramref name="source"/>'s effect gains <paramref name="n"/> Corpses (not a death's Corpse: Game's graveyard
		/// step). Live plays a GainCorpse effect around the gain (the card's own recorded one, else the generic
		/// ReuseFX_Necrotic_DeathKnight_BuffImpact_GainCorpse_Super), placed like the spend's (SpendCorpsesFx): a spell / Hero
		/// Power from its hero at the hero, a minion from itself, at nothing in its triggers.
		/// </summary>
		public static void GainCorpses(Controller c, IEntity source, int n)
		{
			if (n <= 0) return;
			Game game = c.Game;
			string fx = source is IPlayable p ? Loader.SubSpells.FindNamed(p.Card.Id, "GainCorpse") ?? GenericGainCorpse : null;
			bool fromHero = source is Spell || source is HeroPower;
			Kettle.PowerHistoryBlockStart block = game.History ? game.PowerHistory.CurrentEffectBlock : null;
			bool trigger = block?.BlockType == Enums.BlockType.TRIGGER && block.TriggerKeyword != (int)Enums.GameTag.DEATHRATTLE;
			int[] at = fromHero ? new[] { c.Hero.Id } : trigger ? Array.Empty<int>() : new[] { source.Id };
			bool open = fx != null && game.OpenSubSpell((IPlayable)source, fx, at, shownSource: fromHero ? c.Hero.Id : 0);
			c.GainCorpses(n);
			if (open) game.CloseSubSpells(source.Id);
		}

		/// <summary>HsServer: spend the source card's own Corpse cost (<see cref="Enchants.Power.CorpseCost"/>).</summary>
		public static bool SpendCorpses(Controller c, IEntity source) =>
			SpendCorpses(c, source, source?.Card.Power?.CorpseCost ?? 0);

		/// <summary>HsServer: spend up to <paramref name="max"/> Corpses; returns how many were spent.</summary>
		public static int SpendCorpsesUpTo(Controller c, IEntity source, int max)
		{
			int n = Math.Min(max, c.Corpses);
			if (n > 0) SpendCorpsesFx(c, source, n);
			else CorpseStepDone(c, source);
			return n;
		}

		private static void SpendCorpsesFx(Controller c, IEntity source, int n)
		{
			Game game = c.Game;
			string consume = source is IPlayable p ? Loader.SubSpells.CorpseEffect(p.Card) : null;
			// live: a spell (or Hero Power) plays it from its caster's hero at the hero (Grave Strength); a minion, weapon or
			// location from itself, at itself in its battlecry or Deathrattle (Ymirjar Deathbringer, location-20260930) and
			// at nothing in its other triggers (Battlefield Necromancer's end of turn)
			bool fromHero = source is Spell || source is HeroPower;
			Kettle.PowerHistoryBlockStart block = game.History ? game.PowerHistory.CurrentEffectBlock : null;
			bool trigger = block?.BlockType == Enums.BlockType.TRIGGER && block.TriggerKeyword != (int)Enums.GameTag.DEATHRATTLE;
			int[] at = fromHero ? new[] { c.Hero.Id } : trigger ? Array.Empty<int>() : new[] { source.Id };
			// what the card did before the spend stays in its own effect (Blood Tap's first +1/+1); an effect nothing
			// happened in yet is taken back (Grave Strength: the spend comes first)
			if (consume != null) game.CloseSubSpells(source.Id, dropEmpty: true);
			bool open = consume != null && game.OpenSubSpell((IPlayable)source, consume, at, shownSource: fromHero ? c.Hero.Id : 0);
			c.Corpses -= n;
			if (open) game.CloseSubSpells(source.Id);
			CorpsesSpent(c, n);
			CorpseStepDone(c, source);
		}

		/// <summary>
		/// HsServer: cards that improve as their owner spends Corpses (<see cref="Enchants.Power.CorpseSpentTask"/>): every card of
		/// <paramref name="c"/>'s with that slot, in hand and in the deck, runs it once per Corpse spent (Climactic Necrotic
		/// Explosion: "(Randomly improved by Corpses you've spent)"; wiki: "any one of these numbers on the card can
		/// individually upgrade by 1"). Every spend comes through here (SpendCorpsesFx: a card's Corpse cost, "spend N", "up to N").
		/// </summary>
		public static void CorpsesSpent(Controller c, int n)
		{
			if (n <= 0) return;
			// (HsServer LOST_CITY: and the active Quest — "Quest: Spend 15 Corpses", Reanimate the Terror)
			var improving = c.HandZone.Concat(c.DeckZone).Where(p => p.Card.Power?.CorpseSpentTask != null).ToList();
			improving.AddRange(c.SecretZone.Quests.Where(quest => quest.Card.Power?.CorpseSpentTask != null));
			if (improving.Count == 0) return;
			for (int i = 0; i < n; i++)
				foreach (IPlayable p in improving)
					if (p.Controller == c && (p.Zone == c.HandZone || p.Zone == c.DeckZone || p.Zone == c.SecretZone))
						p.Card.Power.CorpseSpentTask.Process(c.Game, c, p, p);
		}

		/// <summary>After the Corpse step, a card with its own Corpse effect plays the rest of its effect inside its buff /
		/// area effect again (a spell's plays from its caster's hero).</summary>
		private static void CorpseStepDone(Controller c, IEntity source)
		{
			if (!(source is IPlayable p) || Loader.SubSpells.CorpseEffect(p.Card) == null) return;
			string whole = Loader.SubSpells.WholeEffect(p.Card.Id, false) ?? Loader.SubSpells.WholeEffect(p.Card.Id, true);
			if (whole != null)
				c.Game.OpenSubSpell(p, whole, null, shownSource: p is Spell ? c.Hero.Id : 0, collectTargets: true);
		}
	}
}
