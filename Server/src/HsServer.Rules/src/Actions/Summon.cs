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
using System;
using System.Linq;
using SabberStoneCore.Model;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
	{
		public static Action<Game, Minion, int, IEntity> SummonBlock
			=> delegate (Game g, Minion minion, int zonePosition, IEntity summoner)
			{
				// HsServer: rules — a summon onto a full board doesn't happen (any script that summons without checking: no
				// throw, no summon triggers; the minion stays where it was / is never placed).
				if (minion.Controller.BoardZone.IsFull)
				{
					g.Log(LogLevel.INFO, BlockType.PLAY, "SummonBlock", !g.Logging ? "" : $"Board full: {minion} isn't summoned.");
					return;
				}

				// HsServer: a living minion's effect decides how it places its summons at its first one, before anything of this
				// summon runs (Game.SummonRuns, LayoutOf)
				Minion living = summoner is Minion lm && lm.Zone?.Type == Zone.PLAY && lm.Controller == minion.Controller ? lm : null;
				if (living != null) RunOf(g, living);
				// HsServer: no position given = Blizzard's rule for where the summoner puts it (SummonPosition). A minion
				// coming from the hand or deck (Force Play) still carries its old ZONE here.
				if (zonePosition < 0 && summoner != null)
					zonePosition = SummonPosition(g, summoner, minion.Controller,
						forcePlay: minion[GameTag.ZONE] == (int)Zone.HAND || minion[GameTag.ZONE] == (int)Zone.DECK);
				if (zonePosition > minion.Controller.BoardZone.Count)
					zonePosition = minion.Controller.BoardZone.Count;

				// HsServer: a minion an effect creates carries CREATOR = its summoner, as live. The client lays new minions
				// out next to their creator; without it the summon started on the far right and snapped over.
				if (summoner is IPlayable creator && minion[GameTag.CREATOR] == 0 && minion.Zone == null &&
				    minion[GameTag.ZONE] != (int)Zone.HAND && minion[GameTag.ZONE] != (int)Zone.DECK && minion[GameTag.ZONE] != (int)Zone.GRAVEYARD)
					minion._data[GameTag.CREATOR] = creator.Id; // in its FULL_ENTITY (no TAG_CHANGE before the entity exists)

				// HsServer: the summoner's own spawn effect replaces the blue summon (SUB_SPELL, Loader.SubSpells). A spell's
				// has no source (live: Demonic Assault's spawn Source=0, 4 of 4 plays), so it isn't anchored to the card.
				// (HsServer (TITANS): not a Titan's — its spawn effects belong to its abilities: Generic.TitanAbilityFx / NamedFx)
				bool spawnFx = g.History && summoner is IPlayable fxSource && !fxSource.Card.IsTitan &&
				               g.OpenSubSpell(fxSource, Loader.SubSpells.Find(fxSource.Card.Id, Loader.SubSpells.Role.Spawn), null,
					               shownSource: fxSource is Spell ? Game.NoSubSpellSource : 0);
				SummonPhase.Invoke(g, minion, zonePosition);
				WhenSummoned(g, minion); // HsServer (CATACLYSM): its own "When summoned, ..." right as it lands
				SummonAppendages(g, minion); // HsServer: Colossal — its appendages before its other effects (wiki Colossal)
				// HsServer: one spawn effect around all the summons of the summoner's running effect (live: Rime Sculptor's two
				// Elementals, Feral Spirit's two Wolves, JAIL_454's four in one OverrideSpawn SUB_SPELL; ours opened one per
				// minion and the client played them one after the other). Left open, the next summon of this effect plays in it
				// (OpenSubSpell keeps an open one); the effect's block closes it (TaskProcessor). A summon outside the
				// summoner's own running effect still closes it at once.
				if (spawnFx && g.PowerHistory.CurrentEffectSource != summoner.Id)
					g.CloseSubSpells(summoner.Id);
				if (summoner != null)
					g.LastSummon = (summoner.Id, minion, g.MoveNumber, g.EffectSerial);
				// HsServer: a living minion's summons in this effect so far (SummonPosition: which side the next one takes)
				if (living != null && living.Zone?.Type == Zone.PLAY && minion.Zone == living.Zone)
				{
					var run = RunOf(g, living);
					g.SummonRuns[(living.Id, g.EffectSerial)] = (run.Count + 1, run.Layout);
				}

				AfterSummonTrigger.Invoke(g, minion, summoner);
			};

		/// <summary>
		/// HsServer: how one effect of a living minion (or Location) places several summons on its side (Generic.SummonPosition).
		/// </summary>
		public enum SummonLayout
		{
			/// <summary>Both sides of it, each directly beside it: the 1st to its right, the 2nd to its left, the 3rd to its right
			/// again (pushing the 1st outward) ... (live Onyxia the Broodmother: 6 Whelps W2 W4 W6 Onyxia W5 W3 W1, N'Zoth, Lord
			/// Marrowgar, Endtime Murozond the same; wiki Onyxia: "alternating first on the right side, then on the left side").</summary>
			BothSides,
			/// <summary>Each directly to its right (the later ones closer to it: live Mother Duck, Ducklings D3 D2 D1).</summary>
			Right,
			/// <summary>To its right, each right of the previous one (a resurrect: user rule 2026-10-04 and 2026-10-10; live Kel'Thuzad).</summary>
			InOrder,
		}

		/// <summary>HsServer: how many minions a card's text summons, for the placement (Generic.SummonPosition): a fixed odd number
		/// ("three"), a fixed even one ("two", "six", "2 copies"), as many as there are or as fit ("fill", "until full", "all",
		/// "each", "for each", "up to N", "that many"), or unknown (a single summon, "them", nothing said).</summary>
		public enum SummonCount { Unknown, Odd, Even, Any }

		private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SummonCount> SummonCounts = new();
		private static readonly System.Text.RegularExpressions.Regex SummonVerb = new(
			@"\b(summon|summons|raise|raises|resurrect|resurrects|resummon|resummons|recruit|recruits)\b|\bfill (your|the|their)\b",
			System.Text.RegularExpressions.RegexOptions.Compiled);
		private static readonly System.Text.RegularExpressions.Regex SummonAny = new(
			@"\b(fill|until|all|every|up to|that many|as many|for each)\b|(?<!one of )\beach\b|\b(summon|raise|resurrect|resummon)s? (your|copies of)\b",
			System.Text.RegularExpressions.RegexOptions.Compiled);
		// (a number right after the verb — not a stat line's "5/5"; "@" / "{0}": a number the card fills in, so any)
		private static readonly System.Text.RegularExpressions.Regex SummonNumber = new(
			@"\b(?:summon|raise|resurrect|resummon|recruit)s?\s+(a|an|one|two|three|four|five|six|seven|\d+(?!/)|@|\{\d\})(?![\w/])",
			System.Text.RegularExpressions.RegexOptions.Compiled);
		private static readonly System.Text.RegularExpressions.Regex SummonMore = new(
			@"\band an? \d+/\d+|\bfrom your hand and (your )?deck\b", // "a 3/4 Rocker ... and a 4/3", "a Demon from your hand and deck"
			System.Text.RegularExpressions.RegexOptions.Compiled);
		private static readonly System.Text.RegularExpressions.Regex CopiesOf = new(
			@"\bcopies of (two|three|four|five|six|seven|\d+)\b", System.Text.RegularExpressions.RegexOptions.Compiled);
		private static readonly System.Text.RegularExpressions.Regex NumberList = new( // "a 1, 2, and 3-Attack minion"
			@"\ban? (?:random )?(\d+(?:, \d+)*,? and \d+)-", System.Text.RegularExpressions.RegexOptions.Compiled);

		/// <summary>HsServer: the summon count its text says (SummonCount), read once per card from its own text — the user's rule
		/// (2026-10-10) is the card text's: "cards that summon 2 or more even numbers OR can summon as many minions as possible" go
		/// both sides, "odd number in card text" to the right.</summary>
		public static SummonCount SummonCountOf(Card card) =>
			// (keyed by id AND text: a crafted card — Elise's Locations, Zilliax, Ignis — keeps its look's id with its parts' text)
			card == null ? SummonCount.Unknown : SummonCounts.GetOrAdd(card.Id + "\n" + card.Text, _ => ReadSummonCount(card.Text));

		internal static SummonCount ReadSummonCount(string text)
		{
			if (string.IsNullOrEmpty(text)) return SummonCount.Unknown;
			string t = System.Text.RegularExpressions.Regex.Replace(text, @"<[^>]*>|\[x\]", "").Replace('_', ' ').ToLowerInvariant();
			t = System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ");
			int most = 0;
			foreach (string sentence in System.Text.RegularExpressions.Regex.Split(t, @"(?<=[.!;@])\s*"))
			{
				if (!SummonVerb.IsMatch(sentence)) continue;
				if (SummonAny.IsMatch(sentence)) return SummonCount.Any;
				int Word(string w) => w switch
				{
					"a" or "an" or "one" => 1, "two" => 2, "three" => 3, "four" => 4, "five" => 5, "six" => 6, "seven" => 7,
					_ => int.TryParse(w, out int d) ? d : -1,
				};
				foreach (System.Text.RegularExpressions.Match m in SummonNumber.Matches(sentence))
				{
					int n = Word(m.Groups[1].Value);
					if (n < 0) return SummonCount.Any; // "summon @ copies"
					if (n == 1)
					{
						n += SummonMore.Matches(sentence).Count;
						if (NumberList.Match(sentence) is { Success: true } list) n = Math.Max(n, list.Groups[1].Value.Split(',', ' ').Count(x => int.TryParse(x, out _)));
					}
					most = Math.Max(most, n);
				}
				foreach (System.Text.RegularExpressions.Match m in CopiesOf.Matches(sentence))
					most = Math.Max(most, Word(m.Groups[1].Value));
			}
			return most < 2 ? SummonCount.Unknown : most % 2 == 0 ? SummonCount.Even : SummonCount.Odd;
		}

		/// <summary>HsServer: the summoner's effect is about to summon <paramref name="total"/> minions at once. With no
		/// <paramref name="layout"/> the shared rule places them (SummonPosition: its text's count, else this total); a script gives
		/// one only where live shows the card itself doing something else (N'Zoth: a resurrect that goes both sides).</summary>
		public static void BeginSummonGroup(Game g, IEntity summoner, int total, SummonLayout? layout = null) =>
			g.SummonGroup = (summoner?.Id ?? 0, g.MoveNumber, g.EffectSerial, total, layout);

		/// <summary>HsServer: how this effect of the living <paramref name="source"/> places its summons (decided at its first one,
		/// kept for the rest: Game.SummonRuns). Live (2026-10-10 survey of every live recording, engine-rules.md "Board placement of
		/// summons"; user rule 2026-10-10):
		/// - a layout the effect's script gives (BeginSummonGroup);
		/// - a resurrect (the effect read the dead: graveyard, "died this game") lines them up to its right (user; live Kel'Thuzad);
		/// - a text count that is even or "as many as" (two, six, fill, until full, all, each, for each, up to N): both sides, right
		///   first (live: Dr. Boom, Cenarius, Nerubian Swarmguard, Archwitch Willow, Scarlet Recruiter, Onyxia the Broodmother, Lord
		///   Marrowgar, Raid Boss Onyxia, Dragoncaller Alanna, Hedra, Endtime Murozond — 3 and 5 of a "for each"/"all" alternate too);
		/// - an odd text count ("three"): each directly to its right (live Mother Duck x6, Frostfin Chomper x3: D3 D2 D1);
		/// - no count in the text: the group's size (SummonTask's amount) — even both sides, odd right; nothing announced: both sides
		///   (a first summon goes right either way).
		/// Where the minions come from doesn't matter (live: Archwitch Willow's from the hand and deck, Scarlet Recruiter's from the
		/// deck, Doppelgangster's and Nerubian Swarmguard's copies all go both sides).</summary>
		/// <summary>HsServer: this effect's summons by the living <paramref name="source"/> so far and their layout (Game.SummonRuns;
		/// started with LayoutOf at its first summon).</summary>
		internal static (int Count, SummonLayout Layout) RunOf(Game g, Minion source)
		{
			if (g.SummonRunsMove != g.MoveNumber)
			{
				g.SummonRuns.Clear();
				g.SummonRunsMove = g.MoveNumber;
			}
			var key = (source.Id, g.EffectSerial);
			if (!g.SummonRuns.TryGetValue(key, out var run))
				g.SummonRuns[key] = run = (0, LayoutOf(g, source));
			return run;
		}

		internal static SummonLayout LayoutOf(Game g, Minion source)
		{
			var group = g.SummonGroup;
			bool inGroup = group.Source == source.Id && group.Move == g.MoveNumber && group.Effect == g.EffectSerial;
			if (inGroup && group.Layout is SummonLayout given) return given;
			if (g.DeadPoolRead == (g.MoveNumber, g.TasksThisMove)) return SummonLayout.InOrder;
			switch (SummonCountOf(source.Card))
			{
				case SummonCount.Even:
				case SummonCount.Any:
					return SummonLayout.BothSides;
				case SummonCount.Odd:
					return SummonLayout.Right;
			}
			if (inGroup && group.Total >= 2) return group.Total % 2 == 0 ? SummonLayout.BothSides : SummonLayout.Right;
			return SummonLayout.BothSides;
		}

		/// <summary>
		/// HsServer: where a summon lands, as Blizzard places it (engine-rules.md "Board placement of summons"):
		/// - a summoner on the board (minion, Location): directly to its right; several from one effect by its LayoutOf (both sides,
		///   each directly right, or a resurrect's line to its right);
		/// - one that just died (Deathrattle, a Location breaking) puts it in the slot it had, several left to right — except a
		///   minion it puts into play from the hand or deck (<paramref name="forcePlay"/>): far right (Advanced rulebook, "Where do
		///   Minions summoned by Deathrattles spawn?": "These Deathrattles also summon at the right: Meat Wagon, Giant Anaconda,
		///   Deathwing, Dragonlord");
		/// - anything else (spell, hero power, weapon, hero, a card in hand, the opponent's side) puts it on the far right (-1), in
		///   order (live: every spell, also "fill" ones — Huddle Up, Army of the Dead, Wisps of the Old Gods).
		/// </summary>
		public static int SummonPosition(Game g, IEntity summoner, Controller side, bool forcePlay = false)
		{
			// A Deathrattle granted by an enchantment (Soul of the Forest, Grave Rune) summons on the far right
			// (Advanced rulebook, "Where do Minions summoned by Deathrattles spawn?").
			if (summoner is Enchantment) return -1;
			// (HsServer 2026-10-08: such a Deathrattle runs with the dead minion as its source and its enchantment as the task's target —
			// Enchantment.Remove — so a summon from the dead minion while it runs goes far right too: rulebook :1687 "These deathrattles also
			// summon minions at the right side: … Soul of the Forest")
			if (summoner is Minion granted && granted.Zone?.Type != Zone.PLAY && g.TaskQueue.CurrentTarget is Enchantment ench &&
			    ench.Card.Power?.DeathrattleTask != null && ench.Target == granted)
				return -1;
			if (!(summoner is Minion source) || source.Controller != side) return -1;
			int position;
			if (source.Zone?.Type == Zone.PLAY)
			{
				var (k, layout) = RunOf(g, source); // k: this summon's number in the effect
				// each directly beside it: the even ones (1st, 3rd ...) to its right, the odd ones to its left; Right: all to its right
				if (layout == SummonLayout.BothSides && k % 2 == 1)
					return Math.Min(source.ZonePosition, side.BoardZone.Count);
				if (layout == SummonLayout.BothSides || layout == SummonLayout.Right)
					return Math.Min(source.ZonePosition + 1, side.BoardZone.Count);
				position = source.ZonePosition + 1; // InOrder: right of it, then right of the previous one (below)
			}
			else if (source.ToBeDestroyed && forcePlay)
				return -1; // a Deathrattle putting a minion from the hand or deck into play: far right (rulebook)
			else if (source.ToBeDestroyed && source.Zone?.Type != Zone.HAND && source.Zone?.Type != Zone.DECK)
				position = source.LastBoardPosition;
			else
				return -1;
			// The same effect's previous summon: go right of it.
			var (lastSource, lastMinion, move, effect) = g.LastSummon;
			// (Only on the same side: Sindragosa's left champion doesn't pull the right one over.)
			if (lastSource == source.Id && move == g.MoveNumber && effect == g.EffectSerial &&
			    lastMinion?.Zone == side.BoardZone && lastMinion.ZonePosition >= position)
				position = lastMinion.ZonePosition + 1;
			return Math.Min(position, side.BoardZone.Count);
		}

		private static Action<Game, Minion, int> SummonPhase
			=> delegate (Game g, Minion minion, int zonePosition)
			{
				g.Log(LogLevel.INFO, BlockType.PLAY, "SummonPhase", !g.Logging? "":$"Summon Minion {minion} to Board of {minion.Controller.Name}.");
				// HsServer: a minion pulled out of the hand or deck (Archwitch Willow, Voidcaller) moves as live (Ancestor's
				// Call, live-20261001-questline): it leaves its hand slot (ZONE_POSITION=0), then ZONE=PLAY, then its board
				// slot — the client then skips waiting for the board actor (ZoneChangeList). A slot first (still in hand)
				// plus a re-sent SHOW_ENTITY made the card snap back and forth. A deck card is revealed to the engine's
				// stream first (still hidden in the deck), so its ZONE change shows the real card, not an empty id.
				int origin = minion[GameTag.ZONE];
				bool fromHand = origin == (int)Zone.HAND, fromDeck = origin == (int)Zone.DECK;
				if (g.History && fromHand && minion[GameTag.ZONE_POSITION] != 0)
					minion[GameTag.ZONE_POSITION] = 0;
				if (g.History && fromDeck)
					g.PowerHistory.Add(PowerHistoryBuilder.ShowEntity(minion));
				int from = g.History ? g.PowerHistory.Full.Count : 0;
				minion.Controller.BoardZone.Add(minion, zonePosition);
				// HsServer: live order — the board makes room first, then the minion appears in its slot (its FULL_ENTITY
				// already in PLAY at its position). Entering at position 0 made the client start the summon on the far right
				// and snap it over.
				bool created = g.History && g.PowerHistory.PlaceEntered(minion.Id, from, g.HistoryMoveFloor(), () => PowerHistoryBuilder.FullEntity(minion),
					zoneFirst: fromHand || fromDeck);

				g.AuraUpdate();

				g.SummonedMinions.Add(minion);

				// add summon block show entity (not for a card the client already has: from the hand or the deck)
				if (g.History && !created && !fromHand && !fromDeck)
					g.PowerHistory.Add(PowerHistoryBuilder.ShowEntity(minion));
			};

		/// <summary>
		/// HsServer (TITANS review 2026-10-07): "each other &lt;X&gt; you've summoned this game" — the player's summons matching
		/// <paramref name="which"/> (Controller.SummonedThisGame), less <paramref name="p"/> itself only when it was counted there for
		/// its controller (HsTag.SUMMON_COUNTED_FOR, stamped when it was added). A minion transformed into (Shapeless Constellation,
		/// Evolve), stolen, or still in its own summon / Battlecry before its after-summon step was never counted: nothing to
		/// leave out. Wiki Astral Automaton, patch 29.2.0.198037: "Fixed a bug where Astral Automaton would appear as a 0/1 when
		/// transformed into." Astral Automaton, Pirate Admiral Hooktusk.
		/// </summary>
		public static int OthersSummonedThisGame(IPlayable p, Func<Card, bool> which)
		{
			if (p?.Controller == null) return 0;
			int n = p.Controller.SummonedThisGame.Count(which);
			bool self = p[HsTag.SUMMON_COUNTED_FOR] == p.Controller.PlayerId && which(p.Card);
			return Math.Max(0, self ? n - 1 : n);
		}

		private static Action<Game, Minion, IEntity> AfterSummonTrigger
			=> delegate (Game g, Minion minion, IEntity summoner)
			{
				//minion.IsSummoned = true;

				g.TaskQueue.StartEvent();
				EventMetaData temp = g.CurrentEventData;
				if (summoner != null)
					g.CurrentEventData = new EventMetaData(summoner as IPlayable, minion);
				g.TriggerManager.OnAfterSummonTrigger(minion);
				g.ProcessTasks();
				g.CurrentEventData = temp;
				g.TaskQueue.EndEvent();

				if (minion.IsRace(Race.TOTEM))
					minion.Controller.NumTotemSummonedThisGame++;
				minion.Controller.SummonedThisGame.Add(minion.Card); // HsServer: "summoned this game" (Kargal Battlescar)
				minion[HsTag.SUMMON_COUNTED_FOR] = minion.Controller.PlayerId; // HsServer: it is one of them (OthersSummonedThisGame)
				// HsServer: Timewarden — "Until the end of your next turn, Dragons you summon gain Taunt and Divine Shield"
				if (minion.IsRace(Race.DRAGON) && g.Turn <= minion.Controller[HsTag.DRAGONS_GAIN_TAUNT_SHIELD_UNTIL] && minion.Zone?.Type == Zone.PLAY)
					AddEnchantmentBlock(g, Cards.FromId("RLK_527e2"), minion, minion);
			};
	}
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
