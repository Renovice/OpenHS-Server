// HsServer addition (not upstream SabberStone): TITANS' shared rules (set 1858, with Fall of Ulduar): Titans, Sigils in the
// hero zone, Magnetic from the client data, a drawn card cast by another entity ("draw and hold"), Blossom, removing from the
// game, pre-damage changes, Armor lost / paid, Overload unlocked, "if you play it this turn", the starting hand, anomalies,
// cost floors, excess attack damage, locked hand cards, and the small shared counters the set's cards read.
// Card scripts: CardSets/HsServer/TitansCardsGenHs*.cs. Writers' guide: scratchpad sets/TITANS/mechanics.md.
using System;
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks;
using SabberStoneCore.Tasks.PlayerTasks;
using SabberStoneCore.Tasks.SimpleTasks;
using SabberStoneCore.Triggers;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		// =========================================================================================================== Titans

		/// <summary>Client keyword TITAN (2772): on the card (its own, not a mention) and on the entity while it isn't Silenced.</summary>
		public const GameTag TITAN = (GameTag)2772;
		/// <summary>Client card tag 4786 (unnamed), on every Titan: the PowerKeyword of its abilities' option (live AllOptions).</summary>
		public const GameTag TITAN_OPTION = (GameTag)4786;
		/// <summary>Client tags TITAN_ABILITY_USED_1..3 (3140-3142): the left / centre / right pip (client Actor.UpdateTitanPips).</summary>
		public const GameTag TITAN_ABILITY_USED_1 = (GameTag)3140, TITAN_ABILITY_USED_2 = (GameTag)3141, TITAN_ABILITY_USED_3 = (GameTag)3142;
		/// <summary>Client tag 3129 (unnamed): 1 once a Titan used an ability, 0 again after it attacks (live Aman'Thul 47388).</summary>
		public const GameTag TITAN_HAS_USED_ABILITY = (GameTag)3129;

		/// <summary>(2026-11-19) Client OLD_GOD (4754): an Old God (BE) is a Titan with four abilities — the same activatable minion
		/// (ACTIVATABLE_MINION 4786; live session-20261106-001808 160292: its abilities set aside with PARENT_CARD, TITAN_ABILITY_USED_n
		/// in its RelatedCards order); the 4th pip is ACTIVATABLE_MINION_ABILITY_USED_4 (4790).</summary>
		public const GameTag OLD_GOD = (GameTag)4754, ACTIVATABLE_MINION_ABILITY_USED_4 = (GameTag)4790;

		private static readonly GameTag[] TitanUsedTags = { TITAN_ABILITY_USED_1, TITAN_ABILITY_USED_2, TITAN_ABILITY_USED_3, ACTIVATABLE_MINION_ABILITY_USED_4 };
		private static readonly GameTag[] TitanEntityTags = { HsTag.TITAN_ABILITY_ENTITY_1, HsTag.TITAN_ABILITY_ENTITY_2, HsTag.TITAN_ABILITY_ENTITY_3, HsTag.TITAN_ABILITY_ENTITY_4 };

		/// <summary>Is <paramref name="p"/> a Titan now: a Titan card's minion that isn't Silenced (wiki Titan: "Silenced Titans
		/// can't use abilities but can perform attacks like a regular minion").</summary>
		public static bool IsTitan(IPlayable p) => p is Minion m && !m.IsSilenced && (p[TITAN] > 0 || p[OLD_GOD] > 0) && p.Card.IsTitan;

		/// <summary>Is <paramref name="p"/> an Old God now (a Titan with the OLD_GOD keyword).</summary>
		public static bool IsOldGod(IPlayable p) => IsTitan(p) && p[OLD_GOD] > 0;

		/// <summary>The script-data slot where an Old God marks "got its extra action this turn" (live: TAG_SCRIPT_DATA_NUM_2; Y'Shaarj,
		/// whose NUM_1 is its 30-Mana count, NUM_4 — Power.OldGodMarkTag).</summary>
		public static GameTag OldGodMark(Card card) => card.Power?.OldGodMarkTag ?? GameTag.TAG_SCRIPT_DATA_NUM_2;

		/// <summary>An Old God's end of its owner's turn: its extra-action mark goes back to 0 in its own TRIGGER block at
		/// <paramref name="effectIndex"/> (live: C'Thun 7, N'Zoth 6, Yogg-Saron 10, Y'Shaarj 10 — only when the mark was set).</summary>
		public static Trigger OldGodTurnEnd(int effectIndex) => new Trigger(TriggerType.TURN_END)
		{
			EffectIndex = effectIndex,
			Condition = new Conditions.SelfCondition(p => p[OldGodMark(p.Card)] > 0),
			SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is IPlayable p) p[OldGodMark(p.Card)] = 0; })
		};

		/// <summary>How many abilities the Titan's card has (3).</summary>
		public static int TitanAbilityCount(IPlayable titan) => titan == null ? 0 : Math.Min(TitanUsedTags.Length, Cards.TitanAbilities(titan.Card).Length);

		/// <summary>The entity of the Titan's ability <paramref name="index"/> (0..2, ability order), or null.</summary>
		public static IPlayable TitanAbility(IPlayable titan, int index)
		{
			if (titan == null || index < 0 || index >= TitanEntityTags.Length) return null;
			int id = titan[TitanEntityTags[index]];
			return id > 0 && titan.Game.IdEntityDic.TryGetValue(id, out IPlayable ability) ? ability : null;
		}

		/// <summary>The ability index (0..2) of the ability <paramref name="card"/> on <paramref name="titan"/>'s card, or -1.</summary>
		public static int TitanAbilityIndex(IPlayable titan, Card card) =>
			titan == null || card == null ? -1 : Array.FindIndex(Cards.TitanAbilities(titan.Card), x => x.Id == card.Id);

		/// <summary>Has the Titan used its ability <paramref name="index"/> (0..2)?</summary>
		public static bool TitanAbilityUsed(IPlayable titan, int index) =>
			index >= 0 && index < TitanUsedTags.Length && titan[TitanUsedTags[index]] > 0;

		/// <summary>A Titan with an ability it hasn't used: it can't attack (wiki Titan: "After a Titan has used all 3 abilities,
		/// it will be able to attack like a regular minion").</summary>
		public static bool TitanHasAbilitiesLeft(IPlayable titan)
		{
			if (!IsTitan(titan)) return false;
			int n = TitanAbilityCount(titan);
			for (int i = 0; i < n; i++)
				if (!TitanAbilityUsed(titan, i)) return true;
			return false;
		}

		/// <summary>
		/// Can the Titan take its action now (one per attack it may make: wiki Titan: "A Titan with Windfury can use two
		/// abilities"; "A Frozen Titan skips its next action"; "Titans can't use their abilities if they are prevented from
		/// attacking"). It has hidden Charge ("Titan minions effectively have Charge without the Charge keyword"): it enters play
		/// ready (BoardZone.Add).
		/// </summary>
		public static bool TitanCanAct(Minion titan) =>
			IsTitan(titan) && titan.Zone?.Type == Zone.PLAY && titan.Controller == titan.Game.CurrentPlayer && !titan.IsFrozen &&
			!titan.CantAttack && !titan.Untouchable && titan[GameTag.DORMANT] == 0 && !titan.IsExhausted && !titan.ToBeDestroyed &&
			titan.NumAttacksThisTurn < AttacksPerTurn(titan);

		/// <summary>The characters ability <paramref name="ability"/> can target for <paramref name="titan"/> (its card's play
		/// requirements; REQ_NONSELF_TARGET means "not the Titan"), or null for an ability that takes no target.</summary>
		public static List<ICharacter> TitanAbilityTargets(Minion titan, Card ability)
		{
			if (ability.TargetingType == TargetingType.None) return null;
			List<ICharacter> targets = ability.GetValidPlayTargets(titan.Controller);
			if (ability.PlayRequirements != null && ability.PlayRequirements.ContainsKey(PlayReq.REQ_NONSELF_TARGET))
				targets.Remove(titan);
			return targets;
		}

		/// <summary>Why the Titan can't use its ability <paramref name="index"/> now (the client's PlayError for its sub-option),
		/// 0 if it can; <paramref name="targets"/>: its targets (null: none needed). Wiki Titan: "If a Titan's ability requires a
		/// target but there are no valid targets, it can't be used."</summary>
		public static PlayReq TitanAbilityError(Minion titan, int index, out List<ICharacter> targets)
		{
			targets = null;
			IPlayable ability = TitanAbility(titan, index);
			if (ability == null || TitanAbilityUsed(titan, index)) return PlayReq.REQ_LITERALLY_UNPLAYABLE;
			PlayReq failed = ability.Card.FailedPlayCheck(titan.Controller);
			if (failed != 0) return failed;
			targets = TitanAbilityTargets(titan, ability.Card);
			if (ability.Card.MustHaveTargetToPlay && (targets == null || targets.Count == 0)) return PlayReq.REQ_TARGET_TO_PLAY;
			return 0;
		}

		/// <summary>The moves the Titan offers now: each usable ability, once per target (Controller.Options).</summary>
		public static IEnumerable<PlayerTask> TitanAbilityTasks(Controller c, Minion titan)
		{
			if (!TitanCanAct(titan)) yield break;
			int n = TitanAbilityCount(titan);
			for (int i = 0; i < n; i++)
			{
				if (TitanAbilityError(titan, i, out List<ICharacter> targets) != 0) continue;
				if (targets != null && targets.Count > 0)
					foreach (ICharacter target in targets)
						yield return TitanAbilityTask.Any(c, titan, i, target);
				else
					yield return TitanAbilityTask.Any(c, titan, i);
			}
		}

		/// <summary>
		/// The Titan uses its ability <paramref name="index"/> (0..2) on <paramref name="target"/>. Live (live-20261007-vfx-decks:
		/// Eonar 159559-159590, Aman'Thul 42717-42770, Golganneth 71670-71890; live-20261006-yogg-cthun 6477): a PLAY block on the
		/// Titan (SubOption = the ability's index: the client plays its m_SubOptionEffectDefs[index]), CARD_TARGET on the Titan and
		/// the ability; a POWER block on the Titan (same SubOption, ShowInHistory) where the ability card's effect runs with the
		/// Titan as its source (wiki Titan: "not considered spells" — no Spell Damage; "affected by Lifesteal and Poisonous"; "will
		/// break Stealth if it uses an ability that deals damage"), inside the ability's own effect prefab when its card has one
		/// (SubSpells; a card script that places its effects itself sets Power.OwnFx on the ability card); then 3129 = 1,
		/// NUM_ATTACKS_THIS_TURN, the actions counter 4163, EXHAUSTED, the ability LITERALLY_UNPLAYABLE, TITAN_ABILITY_USED_n;
		/// then always the Titan's TRIGGER block with effect index = the ability's index (its "After this uses an ability" —
		/// Power.TitanAbilityTask — with ShowInHistory, or empty: the client plays that ability's trigger voice line), the
		/// options counter, the PLAY block's end and SLUSH_TIME 1000. No mana, no card played, no attack.
		/// </summary>
		public static bool UseTitanAbility(Controller c, Minion titan, int index, IPlayable target)
		{
			Game g = c.Game;
			if (titan == null || titan.Controller != c || !TitanCanAct(titan)) return false;
			if (TitanAbilityError(titan, index, out List<ICharacter> targets) != 0) return false;
			IPlayable ability = TitanAbility(titan, index);
			if (target != null && (targets == null || !targets.Contains(target as ICharacter))) return false;
			if (target == null && targets != null && targets.Count > 0 && (ability.Card.MustHaveTargetToPlay || ability.Card.RequiresTargetIfAvailable))
				return false;

			bool history = g.History;
			g.CurrentEventData = new EventMetaData(titan, target);
			if (history)
			{
				g.PowerHistory.ChosenOption = (titan.Id, index);
				g.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.PLAY, titan.Id, "", 0, target?.Id ?? 0));
			}
			if (target != null)
			{
				titan.CardTarget = target.Id;
				ability.CardTarget = target.Id;
			}

			// the ability's effect, the Titan its source
			if (history)
			{
				PowerHistoryBlockStart power = PowerHistoryBuilder.BlockStart(BlockType.POWER, titan.Id, "", 0, target?.Id ?? 0);
				// (live: most abilities show in the history; a few never do — recorded per ability, Power.NotInHistory)
				power.ShowInHistory = ability.Card.Power?.NotInHistory != true;
				// (the Titan's recorded effects of THIS ability wait in its block: Game.RecordedFxAt)
				g.TitanAbilityKey = AbilityKey(ability.Card);
				g.PowerHistory.Add(power);
				g.TitanAbilityKey = null;
			}
			ISimpleTask effect = ability.Card.Power?.PowerTask;
			string prefab = history && ability.Card.Power?.OwnFx != true ? TitanAbilityFx(titan, ability.Card, target != null) : null;
			// (live: a Titan's summon effects play from nowhere and name no target — Aman'Thul's OverrideSpawns: Source 0,
			// Targets [] even for Shape the Stars' chosen minion, live-20261007-vfx-decks 45067)
			bool spawn = prefab != null && Loader.SubSpells.RoleOf(prefab) == Loader.SubSpells.Role.Spawn;
			bool open = prefab != null && g.OpenSubSpell(titan, prefab, target != null && !spawn ? new[] { target.Id } : null,
				shownSource: spawn ? Game.NoSubSpellSource : 0,
				collectTargets: target == null && !spawn);
			Choice before = c.Choice;
			int actingBefore = g.ActingTitanId;
			g.ActingTitanId = titan.Id; // (its damage breaks its Stealth: Character.TitanBreaksStealth)
			g.TaskQueue.StartEvent();
			effect?.Process(g, c, titan, target);
			g.ProcessTasks();
			g.TaskQueue.EndEvent();
			g.ActingTitanId = actingBefore;
			if (open) g.CloseSubSpells(titan.Id);

			// HsServer (TITANS review 2026-10-07): an effect that asks the player to pick (Strike from History's two picks) keeps
			// the ability's POWER block open over its picks; the rest — the used tags, the after-ability TRIGGER block — runs once
			// the last pick resolved (Choice.ThenTask; live live-20261007-vfx-decks 42717-42770: both picks, BLOCK_END, then the
			// used tags and Aman'Thul's Discover). Values only: an AI's copy of the game runs it too.
			if (before == null && c.Choice != null && c.Choice.SourceId == titan.Id &&
			    (c.Choice.ChoiceType == ChoiceType.TARGET || c.Choice.ChoiceType == ChoiceType.GENERAL))
			{
				if (history) g.PowerHistory.ChosenOption = (0, -1);
				int titanId = titan.Id, targetId = target?.Id ?? 0, abilityIndex = index;
				Choice last = c.Choice;
				while (last.NextChoice != null && last.NextChoice.SourceId == titanId) last = last.NextChoice;
				last.ThenTask = new CustomTask((g2, c2, s2, t2, stack2) =>
				{
					if (!(g2.IdEntityDic.TryGetValue(titanId, out IPlayable p) && p is Minion me)) return;
					IPlayable chosen = targetId > 0 && g2.IdEntityDic.TryGetValue(targetId, out IPlayable tp) ? tp : null;
					g2.CurrentEventData = new EventMetaData(me, chosen);
					FinishTitanAbility(me.Controller, me, abilityIndex, chosen);
				});
				return true;
			}
			if (history)
				g.PowerHistory.ChosenOption = (0, -1);
			FinishTitanAbility(c, titan, index, target);
			return true;
		}

		/// <summary>The rest of a Titan ability once its effect is over (UseTitanAbility): the POWER block's end, the spent action
		/// (live order), the after-ability TRIGGER block, the options counter, the PLAY block's end.</summary>
		private static void FinishTitanAbility(Controller c, Minion titan, int index, IPlayable target)
		{
			Game g = c.Game;
			bool history = g.History;
			IPlayable ability = TitanAbility(titan, index);
			if (history)
				g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());

			// the action is spent (live order)
			if (titan[TITAN_HAS_USED_ABILITY] == 0) titan[TITAN_HAS_USED_ABILITY] = 1;
			titan.NumAttacksThisTurn = titan.NumAttacksThisTurn + 1;
			if (history)
			{
				titan.AttacksThisGame++;
				g.PowerHistory.Add(PowerHistoryBuilder.TagChange(titan.Id, (GameTag)4163, titan.AttacksThisGame));
			}
			if (titan.NumAttacksThisTurn >= AttacksPerTurn(titan)) titan.IsExhausted = true;
			if (ability != null) ability[HsTag.LITERALLY_UNPLAYABLE] = 1;
			titan[TitanUsedTags[index]] = 1;
			titan[HsTag.TITAN_LAST_ABILITY] = index + 1;
			titan[HsTag.TITAN_LAST_ABILITY_TARGET] = target?.Id ?? 0;
			g.DeathProcessingAndAuraUpdate();

			// (2026-11-19) an Old God's first ability of a turn gives it one more action this turn (wiki Old God: "Old Gods can naturally
			// act twice per turn while they have abilities remaining, but can only act once if all of their abilities have been used. If
			// only one ability remains, the creature can use it and then immediately attack"). Live (session-20261106-001808 160666,
			// every Old God in 14 recordings): its TRIGGER block at the card's own index (Power.OldGodExtraActionIndex: C'Thun 6,
			// N'Zoth 5, Yogg-Saron / Y'Shaarj 11) — EXTRA_ATTACKS_THIS_TURN 1, EXHAUSTED 0, TAG_SCRIPT_DATA_NUM_2 1 — before the
			// after-ability block. The client tag is the state (AttacksPerTurn reads it; Game resets it at its owner's next turn start).
			if (IsOldGod(titan) && titan[GameTag.EXTRA_ATTACKS_THIS_TURN] == 0)
			{
				if (history)
				{
					PowerHistoryBlockStart extraBlock = PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, titan.Id, "", titan.Card.Power?.OldGodExtraActionIndex ?? 0, 0);
					g.PowerHistory.Add(extraBlock);
				}
				titan[GameTag.EXTRA_ATTACKS_THIS_TURN] = 1;
				if (titan.NumAttacksThisTurn < AttacksPerTurn(titan)) titan.IsExhausted = false;
				titan[OldGodMark(titan.Card)] = 1;
				if (history) g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			}

			// "After this uses an ability" (always the block: the client plays the ability's voice line from it)
			ISimpleTask after = titan.Card.Power?.TitanAbilityTask;
			if (history)
			{
				int[] indexes = titan.Card.Power?.TitanAbilityEffectIndexes;
				PowerHistoryBlockStart trigger = PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, titan.Id, "",
					indexes != null && index < indexes.Length ? indexes[index] : index + (titan.Card.Power?.TitanAbilityEffectIndexBase ?? 0), 0);
				trigger.ShowInHistory = after != null;
				g.PowerHistory.Add(trigger);
			}
			if (after != null && IsTitan(titan) && titan.Zone?.Type == Zone.PLAY && !titan.ToBeDestroyed &&
			    !c.Hero.IsDead && !c.Opponent.Hero.IsDead)
			{
				g.TaskQueue.StartEvent();
				after.Process(g, c, titan, null);
				g.ProcessTasks();
				g.TaskQueue.EndEvent();
			}
			if (history)
				g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			g.DeathProcessingAndAuraUpdate();

			c.NumOptionsPlayedThisTurn++;
			if (history)
			{
				g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
				EndPlayerPlay(g, titan);
			}
			g.CurrentEventData = null;
		}

		/// <summary>
		/// The effect prefab around a Titan's ability step: the ability card's own (its CardDef or a recording: a missile at its
		/// target, else its one buff / area effect — Golganneth's Lord of Skies, The Primus' Runes of Frost), else the Titan's
		/// prefab named after the ability (its name's words in TitleCase between underscores: "Shape the Stars" →
		/// TTNFX_Amanthul_ShapeTheStars_OverrideSpawn, "Flourish" → TTNFX_EonarTheLifebinder_Flourish_Super, "Reign of Chaos" →
		/// TTNFX_YoggSaronUnleashed_ReignOfChaos_Super), else none (the script places it: Power.OwnFx on the ability card +
		/// Generic.NamedFx / AbilityFx). The Titan's other effects are never wrapped automatically (TaskProcessor, Summon).
		/// </summary>
		public static string TitanAbilityFx(IPlayable titan, Card ability, bool targeted)
		{
			string own = (targeted ? Loader.SubSpells.Find(ability.Id, Loader.SubSpells.Role.Missile) : null)
			             ?? Loader.SubSpells.WholeEffect(ability.Id, false);
			if (own != null) return own;
			string key = AbilityKey(ability);
			return key.Length > 0 ? Loader.SubSpells.FindNamed(titan.Card.Id, "_" + key + "_") : null;
		}

		/// <summary>An ability's name as Blizzard's effect names carry it: its words in TitleCase ("Shape the Stars" → ShapeTheStars).</summary>
		public static string AbilityKey(Card ability) =>
			string.Concat((ability?.Name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
				.Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray())).Where(w => w.Length > 0)
				.Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));

		/// <summary>
		/// HsServer (2026-10-08): does a Titan's effect belong to the ability <paramref name="key"/>? Its name carries the ability
		/// (live: TTNFX_RunesOfBlood_Destroy, TTNFX_Amanthul_StrikeFromHistory_MinionRemove), sometimes spelled another way
		/// ("Maximize Defenses" → TTNFX_MaximizeDefences_Impact): letters only, case-insensitive, or the key's first 8 letters.
		/// </summary>
		public static bool EffectOfAbility(string effectName, string key)
		{
			string Norm(string x) => new string((x ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
			string n = Norm(effectName), k = Norm(key);
			return k.Length > 0 && (n.Contains(k) || k.Length >= 8 && n.Contains(k.Substring(0, 8)));
		}

		/// <summary>
		/// A Titan's ability entities, created with it (Entity.FromCard; a transform into a Titan: ChangeEntityBlock; a bounce:
		/// ResetForBackwardMove). Live (live-20261007-vfx-decks 134451-134463, 67873-67903): right after the Titan, in SETASIDE,
		/// CREATOR = PARENT_CARD = the Titan, in ability order; shown with it. The client keeps them as the Titan's sub-cards
		/// (tooltip, history tile). Used ones are LITERALLY_UNPLAYABLE (a copy keeps the used state: SyncTitanAbilities).
		/// </summary>
		internal static void CreateTitanAbilities(Controller c, IPlayable titan)
		{
			Card[] abilities = Cards.TitanAbilities(titan.Card);
			for (int i = 0; i < abilities.Length && i < TitanEntityTags.Length; i++)
			{
				IPlayable ability = Entity.FromCard(c, abilities[i],
					new EntityData { [GameTag.CREATOR] = titan.Id, [GameTag.PARENT_CARD] = titan.Id }, c.SetasideZone);
				((Entity)titan)._data[TitanEntityTags[i]] = ability.Id;
				if (titan[TitanUsedTags[i]] > 0)
					ability[HsTag.LITERALLY_UNPLAYABLE] = 1;
			}
		}

		/// <summary>The entity ids of a Titan's ability entities (0 for none).</summary>
		internal static int[] TitanAbilityIds(IPlayable p) => TitanEntityTags.Select(t => p[t]).ToArray();

		/// <summary>Old ability entities leave (live, a Titan returned to hand 47490-47540: PARENT_CARD 0, REMOVEDFROMGAME).</summary>
		internal static void RetireTitanAbilities(Game g, int[] ids)
		{
			if (ids == null) return;
			foreach (int id in ids)
			{
				if (id <= 0 || !g.IdEntityDic.TryGetValue(id, out IPlayable ability)) continue;
				ability.Zone?.Remove(ability);
				ability[GameTag.PARENT_CARD] = 0;
				ability[GameTag.ZONE] = (int)Zone.REMOVEDFROMGAME;
			}
		}

		/// <summary>The ability entities show the Titan's used abilities (LITERALLY_UNPLAYABLE), after its state was copied.</summary>
		public static void SyncTitanAbilities(IPlayable titan)
		{
			int n = TitanAbilityCount(titan);
			for (int i = 0; i < n; i++)
				if (TitanAbility(titan, i) is IPlayable ability)
				{
					int locked = TitanAbilityUsed(titan, i) ? 1 : 0;
					if (ability[HsTag.LITERALLY_UNPLAYABLE] != locked) ability[HsTag.LITERALLY_UNPLAYABLE] = locked;
				}
		}

		/// <summary>A copy of a Titan keeps the state of its abilities (wiki Titan: "When a Titan is copied, the state of its
		/// abilities is preserved"; Generic.Copy, TransformCopyTask).</summary>
		public static void CopyTitanState(IPlayable from, IPlayable to)
		{
			if (from == null || to == null || !to.Card.IsTitan) return;
			for (int i = 0; i < TitanUsedTags.Length; i++)
				if (from[TitanUsedTags[i]] != to[TitanUsedTags[i]])
					to[TitanUsedTags[i]] = from[TitanUsedTags[i]];
			if (from[TITAN_HAS_USED_ABILITY] != to[TITAN_HAS_USED_ABILITY])
				to[TITAN_HAS_USED_ABILITY] = from[TITAN_HAS_USED_ABILITY];
			// HsServer (2026-10-07): the abilities' own numbers are part of that state (Norgannon's doubled powers, the "@" of
			// their text: TAG_SCRIPT_DATA_NUM_1 on each ability entity). Post-release deck guide (hearthpwn deck 1431799, Norgannon
			// OTK, 2023-08-04): "copying it many times with Reverberations and Cover Artist to repeatedly deal 20 damage" — 20
			// is Progenitor's Power doubled twice, so the copies keep it.
			if (from.Card.Id == to.Card.Id)
				for (int i = 0; i < TitanAbilityCount(to); i++)
					if (TitanAbility(from, i) is IPlayable was && TitanAbility(to, i) is IPlayable now &&
					    now[GameTag.TAG_SCRIPT_DATA_NUM_1] != was[GameTag.TAG_SCRIPT_DATA_NUM_1])
						now[GameTag.TAG_SCRIPT_DATA_NUM_1] = was[GameTag.TAG_SCRIPT_DATA_NUM_1];
			SyncTitanAbilities(to);
		}

		/// <summary>
		/// After a Titan's attack: live (live-20261007-vfx-decks 47386-47388, Aman'Thul after all its abilities; Golganneth
		/// 310196, 311216) sends SLUSH_TIME 3000 and an empty POWER block on the Titan (effect index 1, Target = the defender,
		/// ShowInHistory) right after the ATTACK block, then 3129 = 0.
		/// </summary>
		internal static void TitanAfterAttack(Game g, ICharacter attacker, ICharacter defender)
		{
			if (!(attacker is Minion titan) || !IsTitan(titan)) return;
			if (g.History)
			{
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 3000, Info = new List<int> { titan.Id } });
				PowerHistoryBlockStart block = PowerHistoryBuilder.BlockStart(BlockType.POWER, titan.Id, "", 1, defender?.Id ?? 0);
				block.ShowInHistory = true;
				g.PowerHistory.Add(block);
				g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			}
			if (titan[TITAN_HAS_USED_ABILITY] != 0) titan[TITAN_HAS_USED_ABILITY] = 0;
		}

		/// <summary>The Titan's ability card's own effect prefab whose name contains <paramref name="part"/>, played from the
		/// Titan around <paramref name="action"/> (ability prefabs listed under the ability's id: The Primus' Runes, Golganneth's
		/// Lord of Skies / Roaring Oceans). Prefabs listed under the Titan's id: Generic.NamedFx(titan, part, …).</summary>
		public static void AbilityFx(IPlayable titan, Card ability, string part, IEnumerable<int> targets, Action action)
		{
			string prefab = titan != null && ability != null ? Loader.SubSpells.FindNamed(ability.Id, part) : null;
			bool open = prefab != null && titan.Game.OpenSubSpell(titan, prefab, targets);
			action();
			if (open) titan.Game.CloseSubSpells(titan.Id);
		}

		// =========================================================================================================== Backward moves

		/// <summary>Tags a card keeps through every move of the same entity (a backward move resets the rest).</summary>
		private static readonly GameTag[] KeptOnBackwardMove =
		{
			HsTag.STOLEN_BY,
			// HsServer (WONDERS): permanent base gains (C'Thun's ritual: wiki "they do not disappear even if C'Thun is ... returned
			// to the hand") and whether a C'Thun follows its owner's ritual totals
			HsTag.PERMANENT_ATK_GAIN, HsTag.PERMANENT_HEALTH_GAIN, HsTag.CTHUN_FOLLOWING,
			// HsServer (2026-10-09, Dalaran Heist): its adventure-run deck slot (live 1286: kept through Bob's Tavern's moves)
			RUN_DECK_SLOT,
		};

		/// <summary>
		/// HsServer: a card moving backward (board → hand / deck, hand → deck) loses its enchantments and tags (rules C3; user rule
		/// 2026-09-30) — ReturnToHandBlock and MoveToDeck. Kept: its identity marks (HsTag.STOLEN_BY: Kologarn remembers a stolen
		/// minion played and returned again — wiki Kologarn notes). A Titan is "reverted to its base definition" (wiki Titan):
		/// its old ability entities leave the game and new ones are made (live 47490-47540).
		/// </summary>
		public static void ResetForBackwardMove(IPlayable p)
		{
			if (p == null) return;
			var kept = new List<(GameTag, int)>();
			foreach (GameTag tag in KeptOnBackwardMove)
				if (p[tag] != 0) kept.Add((tag, p[tag]));
			int[] oldAbilities = p.Card.IsTitan ? TitanAbilityIds(p) : null;
			RemoveEnchantmentsOnBackwardMove(p);
			p.Reset();
			foreach ((GameTag tag, int value) in kept)
				((Entity)p)._data[tag] = value;
			RestorePermanentStats(p);
			if (oldAbilities != null && oldAbilities.Any(id => id > 0))
			{
				RetireTitanAbilities(p.Game, oldAbilities);
				CreateTitanAbilities(p.Controller, p);
			}
		}

		/// <summary>
		/// HsServer: put <paramref name="card"/> (in play, in a hand or in a deck) into <paramref name="to"/>'s hand — the same entity
		/// (a steal is a move: user rule), a backward move from play / deck (ResetForBackwardMove). A minion in play going to a
		/// full hand dies (rules C2 Z1); any other card to a full hand — from a deck or from the other player's hand — is lost to
		/// its graveyard (rule book, Full Zone Instant Removal, Advanced_rulebook.txt:993: "the Death Event is only created if the
		/// minion was in play. If it was in your Hand or in the Graveyard, it goes to the Graveyard and nothing else happens").
		/// Returns whether it got there. (Kologarn: "put it in your hand"; "Move any in your hand to your opponent's".)
		/// </summary>
		public static bool TakeIntoHand(Controller to, IPlayable card)
		{
			if (card == null || card.Zone?.Type == Zone.GRAVEYARD) return false;
			Zone from = card.Zone?.Type ?? Zone.INVALID;
			if (to.HandZone.IsFull)
			{
				if (card is Minion m && from == Zone.PLAY) FullZoneKill(m);
				else if (card.Zone != null && !(from == Zone.HAND && card.Controller == to)) card.Controller.GraveyardZone.Add(card.Zone.Remove(card));
				return false;
			}
			if (from == Zone.PLAY) ExitCombat(card);
			GiveTo(card, to, Zone.HAND); // (WILD_WEST: with live's CONTROLLER_AND_ZONE_CHANGE when it changes owner)
			if (from == Zone.PLAY || from == Zone.DECK)
				ResetForBackwardMove(card);
			return AddHandPhase.Invoke(to, card);
		}

		// =========================================================================================================== Sigils

		/// <summary>Client keyword SIGIL (1749).</summary>
		public const GameTag SIGIL = (GameTag)1749;

		/// <summary>
		/// A Sigil ("At the start of your next turn, …"). Rule book (Advanced_rulebook.txt:910): a player's hero zone holds 5 cards,
		/// "any number of copies of any other type of hero zone card". Live (live-20261007-vfx-decks 263109, 265700, 266351-266397):
		/// played (or cast by an effect) it goes to the Secret zone (Card.IsSigil → CastSpell's hero-zone path), visible, with no
		/// POWER block; at its owner's next MAIN_START_TRIGGERS a TRIGGER block on it (effect index 0, TriggerKeyword SIGIL) runs
		/// <paramref name="effect"/> (source: the Sigil), then SLUSH_TIME 5000 and it goes to the graveyard (TaskProcessor). A full
		/// hero zone sends a cast one to the graveyard.
		/// </summary>
		public static Power SigilPower(ISimpleTask effect) => new Power
		{
			Trigger = new Trigger(TriggerType.TURN_START)
			{
				KeywordOverride = SIGIL,
				EffectIndex = 0,
				SingleTask = ComplexTask.Create(effect ?? new CustomTask((g, c, s, t, stack) => { }),
					new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Spell sigil && sigil.Zone?.Type == Zone.SECRET) sigil[HsTag.SECRET_TRIGGERED] = 1;
					}))
			}
		};

		// =========================================================================================================== Magnetic

		/// <summary>Client card tags: a Magnetic minion's magnetize enchantment (2787 = its dbf id; the enchantment's 853 names the
		/// minion back) and the minion type it can also magnetize to (MAGNETIC_TO_RACE 2859: Absorbent Parasite's Beasts).</summary>
		public const GameTag MAGNETIZE_ENCHANTMENT = (GameTag)2787, MAGNETIC_TO_RACE = (GameTag)2859;

		/// <summary>The minion right of <paramref name="m"/> it magnetizes to, or null. Wiki Magnetic: "Playing a Magnetic minion to
		/// the left of an existing Mech will automatically cause the Magnetic minion to become an enchantment"; wiki Absorbent
		/// Parasite: "the first one that can Magnetize to non-Mech minions" (MAGNETIC_TO_RACE).</summary>
		public static Minion MagnetizeHost(Minion m)
		{
			if (m?.Card.Modular != true || m.Zone?.Type != Zone.PLAY) return null;
			int pos = m.ZonePosition;
			if (pos < 0 || pos >= m.Zone.Count - 1) return null;
			Minion host = m.Controller.BoardZone[pos + 1];
			if (host == null || host.Untouchable || host.ToBeDestroyed) return null;
			int race = m.Card[MAGNETIC_TO_RACE];
			return host.IsRace(Race.MECHANICAL) || race > 0 && host.IsRace((Race)race) ? host : null;
		}

		/// <summary>HsServer (2026-10-08): the minion a Magnetic <paramref name="card"/> played at <paramref name="zonePosition"/> would join (the
		/// minion now in that slot, which ends up on its right: MagnetizeHost), or null — what a full board allows (wiki Magnetic.txt:7).</summary>
		public static Minion MagnetizeHostAt(Controller c, Card card, int zonePosition)
		{
			if (card?.Modular != true || c == null || zonePosition < 0 || zonePosition >= c.BoardZone.Count) return null;
			Minion host = c.BoardZone[zonePosition];
			if (host == null || host.Untouchable || host.ToBeDestroyed) return null;
			int race = card[MAGNETIC_TO_RACE];
			return host.IsRace(Race.MECHANICAL) || race > 0 && host.IsRace((Race)race) ? host : null;
		}

		/// <summary>HsServer (2026-10-08): a Magnetic card can be played on its owner's full board (onto a Mech it can join).</summary>
		public static bool MagneticOntoFullBoard(Controller c, Card card)
		{
			if (card?.Modular != true || c == null) return false;
			for (int i = 0; i < c.BoardZone.Count; i++)
				if (MagnetizeHostAt(c, card, i) != null) return true;
			return false;
		}

		/// <summary>A Magnetic card's magnetize enchantment: the client's 2787 (Sparkbots → TTN_719e..e7, Containment Unit →
		/// TTN_700e1, Core reprints → their originals'), else &lt;id&gt;e of the card or its original.</summary>
		public static Card MagnetizeEnchantment(Card card)
		{
			if (card == null) return null;
			if (card[MAGNETIZE_ENCHANTMENT] > 0 && Cards.FromAssetId(card[MAGNETIZE_ENCHANTMENT]) is Card fromData) return fromData;
			return Cards.TryFromId(card.Id + "e") ?? Cards.TryFromId(Loader.ReprintCards.Canonical(card.Id) + "e");
		}

		/// <summary>
		/// Live (live-20261007-vfx-decks 90235, 122090; not magnetizing: 23805, 100708, 180581, 215591): a Magnetic minion played
		/// from hand sends a TRIGGER block on itself with effect index -1 right after it enters play, naming the Mech it will join
		/// (TAG_SCRIPT_DATA_NUM_1 = the Mech) — empty when it joins none. Returns that Mech (PlayCard's PlayMinion).
		/// </summary>
		internal static Minion MagneticPlayBlock(Game g, Minion minion)
		{
			Minion host = MagnetizeHost(minion);
			if (g.History)
				g.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, minion.Id, "", -1, 0));
			if (host != null)
				minion[GameTag.TAG_SCRIPT_DATA_NUM_1] = host.Id;
			if (g.History)
				g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			return host;
		}

		/// <summary>
		/// <paramref name="m"/> magnetizes to <paramref name="host"/> (MagneticTask, in the minion's POWER block). Wiki Magnetic: it
		/// becomes an enchantment "adding its stats and text (other than Magnetic)"; "Magnetizing counts as playing a minion but
		/// not summoning a minion". Live (122090-122150): TARGET + HISTORY_TARGET_DONT_DUPLICATE_UNTIL_END the Mech, the keyword on
		/// it, the enchantment (NUM_1 / NUM_2 = the minion's Attack / Health, MODULAR, 853, CREATOR = the minion), the minion to
		/// SETASIDE. Its triggers go with it onto the Mech; then "Whenever you Magnetize" triggers (TriggerType.MAGNETIZE: source
		/// the Mech).
		/// </summary>
		public static void Magnetize(Game g, Minion m, Minion host)
		{
			if (g.History)
			{
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.TARGET, Data = 0, Info = new List<int> { host.Id } });
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.HISTORY_TARGET_DONT_DUPLICATE_UNTIL_END, Data = 0, Info = new List<int> { host.Id } });
			}
			Card enchantment = MagnetizeEnchantment(m.Card);
			if (enchantment != null)
				AddEnchantmentBlock(g, enchantment, m, host, m.AttackDamage, m.BaseHealth, 0);

			// its triggers go with it — unless its magnetize enchantment carries that effect itself (data-driven: SP-3Y3-D3R's
			// TTN_923e has "Stealth for 1 turn" with its own one-turn removal, CardContainer.MagnetizeEnchantmentsFromData; the
			// minion's own OneTurnStealth copied onto the Mech cleared the Mech's own Stealth too and played the Mech's effect 2)
			if (m.Power?.Trigger != null && enchantment?.Power?.Trigger == null)
			{
				if (host.ActivatedTrigger != null)
				{
					var aggregated = new MultiTrigger(host.ActivatedTrigger, m.Power.Trigger);
					host.ActivatedTrigger.Remove();
					host.ActivatedTrigger = null;
					aggregated.Activate(host);
				}
				else
					m.Power.Trigger.Activate(host);
			}

			m.Controller.SetasideZone.Add(m.Zone.Remove(m));

			EventMetaData outer = g.CurrentEventData;
			g.CurrentEventData = new EventMetaData(host, m);
			g.TaskQueue.StartEvent();
			g.TriggerManager.OnMagnetizeTrigger(host);
			g.ProcessTasks();
			g.TaskQueue.EndEvent();
			g.CurrentEventData = outer;
		}

		// =========================================================================================================== Draws

		/// <summary>HsServer: the card's owner drew it; a spell that is the first one they drew this turn is marked (Jotun, the
		/// Eternal: "the first spell you draw each turn" — wiki bug fix: spells drawn before Jotun was played count). Draw.cs.</summary>
		internal static void NoteDrawn(Controller c, IPlayable drawn)
		{
			if (drawn != null) c[HsTag.CARDS_DRAWN_THIS_GAME] = c[HsTag.CARDS_DRAWN_THIS_GAME] + 1; // HsServer (SPACE): The Ceaseless Expanse
			if (!(drawn is Spell) || c.Game.Turn <= 0) return;
			if (c[HsTag.SPELL_DRAWN_TURN] == c.Game.Turn) return;
			c[HsTag.SPELL_DRAWN_TURN] = c.Game.Turn;
			drawn[HsTag.FIRST_SPELL_DRAWN_ON_TURN] = c.Game.Turn;
		}

		/// <summary>Was <paramref name="drawn"/> the first spell its owner drew this turn?</summary>
		public static bool FirstSpellDrawnThisTurn(IPlayable drawn) =>
			drawn is Spell && drawn.Game.Turn > 0 && drawn[HsTag.FIRST_SPELL_DRAWN_ON_TURN] == drawn.Game.Turn;

		private static readonly System.Text.RegularExpressions.Regex CastWhenDrawnText = new System.Text.RegularExpressions.Regex(
			@"Casts? When Drawn", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

		/// <summary>Does <paramref name="p"/> cast when drawn: its own keyword (Card.ReplacedWhenDrawn) or an enchantment that
		/// gives it one (Runic Adornment's TTN_845e "Jotun's Haste: Cast When Drawn.", Surfalopod's; data: the enchantment's
		/// own text).</summary>
		public static bool CastsWhenDrawn(IPlayable p) =>
			p != null && (p.Card.ReplacedWhenDrawn ||
			              p.AppliedEnchantments?.Any(e => e.Card.ClientText is string t && CastWhenDrawnText.IsMatch(t)) == true);

		/// <summary>
		/// "Draw and hold": a drawn card cast by another entity — <paramref name="granter"/> (the enchantment whose trigger runs:
		/// Jotun's TTN_842t1, Runic Adornment's TTN_845e) casts a NEW copy of <paramref name="drawn"/>. Live (live-20261005/112456
		/// 6849-6950; live-20261007-vfx-decks 265588-265720), inside the granter's TRIGGER block: a copy of the granting card
		/// (the granter's creator's card) in REMOVEDFROMGAME for the history tile, META BEGIN_ARTIFICIAL_HISTORY_TRIGGER_TILE /
		/// OVERRIDE_HISTORY (it) / HISTORY_TARGET (the drawn card), the card's DrawAndHold effect (HOLD_DRAWN_CARD + TARGET =
		/// the drawn card), then the new copy cast like an effect's random cast (PLAY, or SECRET for a Secret / Sigil; at an
		/// enemy when <paramref name="enemiesIfPossible"/>). <paramref name="keepInHand"/>: the drawn card stays (Jotun: REVEALED);
		/// else it was "Cast When Drawn": it is set aside and its owner draws a replacement (wiki Casts When Drawn) before the
		/// tile ends (END_ARTIFICIAL_HISTORY_TILE). Returns the copy.
		/// </summary>
		public static IPlayable CastDrawnCopy(Controller c, IPlayable granter, IPlayable drawn, bool keepInHand, bool enemiesIfPossible)
		{
			if (drawn == null || drawn.Card.Type != CardType.SPELL) return null;
			Game g = c.Game;
			bool history = g.History;
			IPlayable creator = granter is Enchantment e ? e.Creator : granter;
			if (history)
			{
				ArtificialTriggerTile(c, granter, creator, drawn); // (the shared tile: Actions/DalaranHeistAnomalies.cs)
				if (keepInHand && drawn.Zone?.Type == Zone.HAND && drawn[GameTag.REVEALED] == 0)
					drawn[GameTag.REVEALED] = 1;
			}
			// the card's own DrawAndHold effect: the granting card's (the creator: live vfx-decks 265631, Runic Adornment's
			// TTNFX_RunicAdornment_DrawAndHold_Super for its TTN_845e, whose own CardDef lists Jotun's), else the granter's (Jotun
			// TTN_842 has none: its TTN_842t1's, recorded 7/7)
			string hold = history ? (creator != null ? Loader.SubSpells.FindNamed(creator.Card.Id, "DrawAndHold") : null) ?? Loader.SubSpells.FindNamed(granter.Card.Id, "DrawAndHold") : null;
			bool open = hold != null && g.OpenSubSpell(granter, hold, new[] { drawn.Id }, shownSource: Game.NoSubSpellSource);
			if (open)
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.HOLD_DRAWN_CARD, Data = 0, Info = new List<int> { drawn.Id } });
			if (open) g.CloseSubSpells(granter.Id);

			if (!keepInHand && drawn.Zone?.Type == Zone.HAND)
				c.SetasideZone.Add(c.HandZone.Remove(drawn));

			IPlayable copy = Entity.FromCard(c, drawn.Card, new EntityData { [GameTag.CREATOR] = granter.Id, [(GameTag)1715] = granter.Id });
			new CastRandomSpellTask(enemiesIfPossible: enemiesIfPossible).Process(g, c, granter, copy);

			if (!keepInHand)
			{
				// live (vfx-decks 265684-265720): ARTIFICIAL_HISTORY_INTERRUPT on the copy, SLUSH_TIME 1000 on it, the replacement
				// draw, END_ARTIFICIAL_HISTORY_TILE naming the game entity; then the granting enchantment on the set-aside card is
				// spent: it goes to the graveyard (Enchantment.RemoveToGraveyard)
				if (history)
				{
					g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.ARTIFICIAL_HISTORY_INTERRUPT, Data = 0, Info = new List<int> { copy.Id } });
					g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 1000, Info = new List<int> { copy.Id } });
				}
				Draw(c);
				if (history)
					g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.END_ARTIFICIAL_HISTORY_TILE, Data = 0, Info = new List<int> { g.Id } });
				if (granter is Enchantment spent && ReferenceEquals(spent.Target, drawn) && drawn.AppliedEnchantments?.Contains(spent) == true)
					spent.RemoveToGraveyard();
			}
			return copy;
		}

		// =========================================================================================================== Blossom

		/// <summary>"(Blossoms in @ turns.)" — the card's countdown is its TAG_SCRIPT_DATA_NUM_1 (card data: 3 / 2).</summary>
		public static bool Blossoms(Card card) => card?.ClientText?.Contains("(Blossoms in") == true;

		private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Card> _blossomed =
			new System.Collections.Concurrent.ConcurrentDictionary<string, Card>();

		/// <summary>What a Blossom card becomes: its COLLECTION_RELATED_CARD_DATABASE_ID (Frost Lotus Seedling → Blossom), else the
		/// first SPELL among its RelatedCards (Forest Seedlings → Forest Blossoms).</summary>
		public static Card BlossomVersion(Card card) => card == null ? null : _blossomed.GetOrAdd(card.Id, _ =>
		{
			int related = card[(GameTag)1452];
			if (related > 0 && Cards.FromAssetId(related) is Card bloom && bloom.Type == card.Type) return bloom;
			return Loader.ReprintCards.RelatedCardIds(card.Id)?.Select(Cards.TryFromId).FirstOrDefault(x => x != null && x.Type == CardType.SPELL);
		});

		/// <summary>
		/// At the end of each of the player's turns, their Blossom cards in hand count down (wiki Frost Lotus Seedling: "After 2
		/// turns while in the player's hand, this transforms"; patch 27.0.0: not on turn 1 when kept in the mulligan). Live
		/// (live-20261005/140614 10475, 11248): after the end-of-turn step's NEXT_STEP MAIN_CLEANUP, a TRIGGER block on the card
		/// (effect index 0, ShowInHistory), TAG_SCRIPT_DATA_NUM_1 - 1; at 0, in the same block, the upgrade effect (SUB_SPELL
		/// BARFX_RankedSpell_Upgrade_Impact_Nature_Druid, Source 0, Targets the card) around the 1069 pulse and the CHANGE_ENTITY
		/// (its cost changes kept). Game.NextStep.
		/// </summary>
		public static void BlossomAtTurnEnd(Controller c)
		{
			Game g = c.Game;
			foreach (IPlayable card in c.HandZone.GetAll().ToList())
			{
				if (!Blossoms(card.Card)) continue;
				if (g.History)
				{
					PowerHistoryBlockStart block = PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, card.Id, "", 0, 0);
					block.ShowInHistory = true;
					g.PowerHistory.Add(block);
				}
				int left = Math.Max(0, card[GameTag.TAG_SCRIPT_DATA_NUM_1] - 1);
				card[GameTag.TAG_SCRIPT_DATA_NUM_1] = left;
				if (left == 0 && BlossomVersion(card.Card) is Card bloom)
				{
					bool open = g.History && g.OpenSubSpell(card, BlossomUpgradeFx, new[] { card.Id }, shownSource: Game.NoSubSpellSource);
					ChangeEntityBlock(c, card, bloom, false); // (its 1069 pulse: ChangeEntityCore)
					if (open) g.CloseSubSpells(card.Id);
				}
				if (g.History)
					g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			}
		}

		/// <summary>Live's Blossom upgrade effect (live-20261005/140614 11250).</summary>
		private const string BlossomUpgradeFx = "BARFX_RankedSpell_Upgrade_Impact_Nature_Druid:adea29e0a6b630a49a8d20b57cd1f851";

		// =========================================================================================================== Remove from the game

		/// <summary>
		/// "Remove from the game" (Strike from History, To the Void!, Life Sentence): the minion leaves play without dying — no
		/// Death Event, no Deathrattle; its triggers and aura stop (rules C1). Live (live-20261007-vfx-decks 42726, 48019; inside the
		/// card's own effect): 1068 = 6 pulse, EXHAUSTED 0, 1037 1, ZONE_POSITION 0, ZONE SETASIDE (not REMOVEDFROMGAME), its
		/// enchantments gone, then HISTORY_TARGET it.
		/// </summary>
		public static bool RemoveFromGame(IPlayable p, IEntity source)
		{
			// HsServer (LOST_CITY): a card in hand removed from the game (the Underfel Rift — wiki: "distinct from discarding or
			// destroying it"): it leaves the hand for the set-aside zone, no discard
			if (p?.Zone?.Type == Zone.HAND)
			{
				Game hg = p.Game;
				p.Zone.Remove(p);
				if (hg.History) hg.PowerHistory.Add(PowerHistoryBuilder.TagChange(p.Id, (GameTag)1037, 1));
				p.Controller.SetasideZone.Add(p);
				return true;
			}
			if (!(p is Minion m) || m.Zone?.Type != Zone.PLAY) return false;
			Game g = m.Game;
			ExitCombat(m);
			if (g.History)
			{
				g.PowerHistory.Add(PowerHistoryBuilder.TagChange(m.Id, (GameTag)1068, 6));
				g.PowerHistory.Add(PowerHistoryBuilder.TagChange(m.Id, (GameTag)1068, 0));
			}
			if (m.IsExhausted) m.IsExhausted = false;
			m.Zone.Remove(m);
			RetireFromPlay(m);
			RemoveEnchantmentsOnBackwardMove(m);
			if (g.History)
				g.PowerHistory.Add(PowerHistoryBuilder.TagChange(m.Id, (GameTag)1037, 1));
			m.Controller.SetasideZone.Add(m);
			if (g.History)
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.HISTORY_TARGET, Data = 0, Info = new List<int> { m.Id } });
			return true;
		}

		/// <summary>
		/// (2026-11-16) A card that expires without dying — Nythendric Beetles combining, Shadow Reflection's copy at the end of the turn:
		/// it leaves (play: no Death Event, no Deathrattle; its triggers and aura stop) and ends in the graveyard. Live (session-20261003-110225
		/// 38647-38748, session-20261008-012951 6564-6572): 1068 = 6 pulse, ZONE_POSITION 0, ZONE SETASIDE, then the affected counter
		/// (4741 / LAST_AFFECTED_BY = <paramref name="source"/>), 1068 = 4 pulse, ZONE GRAVEYARD. Unlike <see cref="RemoveFromGame"/>
		/// (Strike from History: stays set aside, hidden).
		/// </summary>
		public static bool ExpireToGraveyard(IPlayable p, IEntity source)
		{
			if (p?.Zone == null || p.Zone.Type is Zone.GRAVEYARD or Zone.SETASIDE or Zone.REMOVEDFROMGAME) return false;
			Game g = p.Game;
			if (g.History)
			{
				g.PowerHistory.Add(PowerHistoryBuilder.TagChange(p.Id, (GameTag)1068, 6));
				g.PowerHistory.Add(PowerHistoryBuilder.TagChange(p.Id, (GameTag)1068, 0));
			}
			if (p is Minion m && m.Zone.Type == Zone.PLAY)
			{
				ExitCombat(m);
				if (m.IsExhausted) m.IsExhausted = false;
				m.Zone.Remove(m);
				RetireFromPlay(m);
				RemoveEnchantmentsOnBackwardMove(m);
			}
			else
				p.Zone.Remove(p);
			p.Controller.SetasideZone.Add(p);
			if (source != null) NoteAffected(p, source);
			if (g.History)
			{
				g.PowerHistory.Add(PowerHistoryBuilder.TagChange(p.Id, (GameTag)1068, 4));
				g.PowerHistory.Add(PowerHistoryBuilder.TagChange(p.Id, (GameTag)1068, 0));
			}
			p.Zone.Remove(p);
			p.Controller.GraveyardZone.Add(p);
			return true;
		}

		// =========================================================================================================== Pre-damage

		/// <summary>
		/// A pre-damage trigger's task (TriggerType.PREDAMAGE, FastExecution) that multiplies the proposed damage (Tar Slick's Oily:
		/// "Minions take double damage") — the rule book's Predamage step (DH7: "Just before a Damage Event is created, Predamage
		/// triggers will queue and resolve"). Live (live-install session-20260929-183021): the TRIGGER block holds the new
		/// PREDAMAGE on the damaged character. Its target: the damaged character.
		/// </summary>
		/// <param name="critical">HsServer (2026-10-08): live marks the doubled hit with META CRITICAL_HIT on the damaged character before the
		/// new PREDAMAGE (Talgath's GDB_472e, Quantum Destabilizer, Goldrinn — 9 of 9; Tar Slick's Oily never: per card).</param>
		public static ISimpleTask PreDamageMultiply(int factor, bool critical = false) => new CustomTask((g, c, s, t, stack) =>
		{
			if (g.CurrentEventData == null || g.CurrentEventData.EventNumber <= 0) return;
			if (critical && g.History && t != null)
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.CRITICAL_HIT, Data = 0, Info = new List<int> { t.Id } });
			SetPreDamage(g, t, g.CurrentEventData.EventNumber * factor);
		});

		/// <summary>"Your hero takes half damage, rounded up" (The Immovable Object) as a pre-damage task.</summary>
		public static ISimpleTask PreDamageHalve() => new CustomTask((g, c, s, t, stack) =>
		{
			if (g.CurrentEventData == null || g.CurrentEventData.EventNumber <= 1) return;
			SetPreDamage(g, t, (g.CurrentEventData.EventNumber + 1) / 2);
		});

		/// <summary>A pre-damage trigger's task capping the proposed damage ("can't take more than 2 damage at a time": Amitus;
		/// "can only take 1 damage at a time": Animated Armor, Solid Alibi — rule book DH7c). Its target: the damaged character.</summary>
		public static ISimpleTask PreDamageCap(int cap) => new CustomTask((g, c, s, t, stack) =>
		{
			if (g.CurrentEventData == null || g.CurrentEventData.EventNumber <= cap) return;
			SetPreDamage(g, t, cap);
		});

		/// <summary>
		/// "Can't take more than <paramref name="cap"/> damage at a time" as a pre-damage trigger (rule book DH7c) on
		/// <paramref name="source"/>: it fires only for a hit above the cap — a smaller hit is no event for it (no TRIGGER block, so
		/// no trigger effect: Amitus' effect slots 0-2 are his ability voice lines, CardDef m_TriggerEffectDefs).
		/// <paramref name="effectIndex"/>: its TRIGGER block's effect index. Amitus, Animated Armor, Solid Alibi.
		/// </summary>
		/// <paramref name="everyHit"/>: the trigger fires on every hit, lowering only those above the cap (live Amitus: 41 empty blocks
		/// for hits of 1-2, 13 that lower — titans-vfx 190857).
		public static Trigger PreDamageCapTrigger(TriggerSource source, int cap, int effectIndex = 0, bool everyHit = false) => new Trigger(TriggerType.PREDAMAGE)
		{
			TriggerSource = source,
			FastExecution = true,
			EffectIndex = effectIndex,
			Condition = everyHit ? null : new Conditions.SelfCondition(p => p.Game.CurrentEventData?.EventNumber > cap),
			InHistory = everyHit, // (live Amitus: ShowInHistory, no keyword)
			SingleTask = PreDamageCap(cap)
		};

		/// <summary>"Prevent the Damage Event" (rule book DH7d): the pending damage on the damaged character becomes 0 right here, inside the
		/// trigger's block (live Ice Block, live-20261006-yogg-cthun 31464: PREDAMAGE 0 is the first line of the Secret's block).</summary>
		public static Tasks.ISimpleTask PreventDamage => new Tasks.SimpleTasks.CustomTask((g, c, s, t, stack) =>
		{
			if (g.CurrentEventData?.EventTarget is IPlayable damaged) SetPreDamage(g, damaged, 0);
		});

		private static void SetPreDamage(Game g, IPlayable target, int amount)
		{
			g.CurrentEventData.EventNumber = amount;
			if (target is ICharacter ch && ch.PreDamage != 0 && ch.PreDamage != amount)
				ch.PreDamage = amount;
		}

		// =========================================================================================================== Armor, damage, Overload, Overheal

		/// <summary>Client player tag 3110 (unnamed): the damage the player's hero took during their own turns this game (Armor-absorbed
		/// damage too; live live-20261004-night/222041 9987, 18401-21037; never on the opponent's turn). Character.TakeDamage.</summary>
		public const GameTag DAMAGE_TAKEN_ON_OWN_TURNS = (GameTag)3110;

		/// <summary>The hero lost <paramref name="amount"/> Armor (the hero's Armor setter: every loss, Character.Armor).</summary>
		internal static void NoteArmorLost(Controller c, int amount)
		{
			if (amount <= 0) return;
			int turn = c.Game.Turn;
			if (c[HsTag.ARMOR_LOST_TURN] != turn)
			{
				c[HsTag.ARMOR_LOST_TURN] = turn;
				c[HsTag.ARMOR_LOST_THIS_TURN] = 0;
			}
			c[HsTag.ARMOR_LOST_THIS_TURN] = c[HsTag.ARMOR_LOST_THIS_TURN] + amount;
		}

		/// <summary>"If your Armor changed this turn" (Stoneskin Armorer): the hero gained or lost Armor this game turn (live: its
		/// glow lights on the opponent's turn after Armor absorbed an attack, live-20261005/184804 1634).</summary>
		public static bool ArmorChangedThisTurn(Controller c) =>
			c.Game.Turn > 0 && (c[HsTag.ARMOR_GAINED_TURN] == c.Game.Turn && c[HsTag.ARMOR_GAINED_THIS_TURN] > 0 ||
			                    c[HsTag.ARMOR_LOST_TURN] == c.Game.Turn && c[HsTag.ARMOR_LOST_THIS_TURN] > 0);

		/// <summary>"Lose N Armor to …" (Smelt, General Vezax): with at least <paramref name="n"/> Armor the hero loses it and the
		/// effect happens (true); with less, nothing (false). DOUBT (live-checks): the less-Armor case is undocumented.</summary>
		public static bool PayArmor(Controller c, IEntity source, int n)
		{
			Hero hero = c.Hero;
			if (n <= 0) return true;
			if (hero == null || hero.Armor < n) return false;
			hero.Armor -= n; // (the Armor setter notes the loss)
			return true;
		}

		/// <summary>
		/// "Unlock your Overloaded Mana Crystals" — rule book (Advanced_rulebook.txt:1071): "Unlocking Overloaded Mana Crystals …
		/// will set OVERLOAD_OWED to 0, decrease OVERLOAD_LOCKED to 0, and decrease RESOURCES_USED by the same amount" (the
		/// OverloadLocked setter moves RESOURCES_USED). Returns how many were unlocked (locked + owed: Thorim draws that many first,
		/// live-20261007-vfx-decks 84808, 295981).
		/// </summary>
		public static int UnlockOverload(Controller c)
		{
			int n = c.OverloadLocked + c.OverloadOwed;
			if (c.OverloadOwed != 0) c.OverloadOwed = 0;
			if (c.OverloadLocked != 0) c.OverloadLocked = 0;
			return n;
		}

		/// <summary>Overloaded Mana Crystals to unlock (locked + owed), without unlocking them.</summary>
		public static int OverloadToUnlock(Controller c) => c.OverloadLocked + c.OverloadOwed;

		/// <summary>The excess Health <paramref name="heal"/> Overhealed for <paramref name="c"/>'s effects (Grace of the Highfather:
		/// "Discover a card that costs the amount Overhealed"; live 56107: a full-Health target healed 8 → 8).</summary>
		public static int OverhealedDuring(Controller c, Action heal)
		{
			int before = c[HsTag.OVERHEALED_AMOUNT];
			heal();
			return c[HsTag.OVERHEALED_AMOUNT] - before;
		}

		// =========================================================================================================== "If you play it this turn"

		/// <summary>The client's "Play This Turn Enchantment" (invisible, one-turn).</summary>
		public const string PlayThisTurnEnchantment = "NEW_1308e";

		/// <summary>
		/// Mark <paramref name="card"/> "if you play it this turn" (Lightning Reflexes, Acrobatics, Flint Firearm, the Maps): live
		/// (live-20261001-questline 1942-2102, 6678-7083) NEW_1308e on the card (CREATOR = <paramref name="source"/>) and the
		/// card's TAG_ONE_TURN_EFFECT, 3908, LUNAHIGHLIGHTHINT (1054) and 4663 = 1 (its highlight); at the end of the turn the
		/// enchantment goes and the tags return to 0; played, the enchantment leaves with it. The "if played" effect is the
		/// marking card's own (its player enchantment with TAG_SCRIPT_DATA_NUM_1 = the card: <see cref="IsMarkedPlayThisTurn"/>).
		/// </summary>
		public static void MarkPlayThisTurn(IPlayable card, IEntity source) =>
			MarkPlayThisTurn(card, source, PlayThisTurnEnchantment, PlayThisTurnTags);

		/// <summary>HsServer (WILD_WEST): the same mark with the card's own <paramref name="enchantment"/> and highlight tags
		/// (Stargazing: WW_425e with LUNAHIGHLIGHTHINT 1054 and 1140 — live session-20261002-204752 65551-65690; Generic.MarkCastsTwice).
		/// At the end of the turn such a marker leaves in a TRIGGER block of its own (effect index 1, live).</summary>
		public static void MarkPlayThisTurn(IPlayable card, IEntity source, string enchantment, params GameTag[] highlight)
		{
			if (card == null || card.Zone?.Type != Zone.HAND) return;
			Game g = card.Game;
			Card marker = Cards.TryFromId(enchantment ?? PlayThisTurnEnchantment);
			if (marker != null && source is IPlayable creator)
				AddEnchantmentBlock(g, marker, creator, card, 0, 0, 0);
			if (marker != null && marker.Id != PlayThisTurnEnchantment)
			{
				card[HsTag.PLAY_THIS_TURN_MARKER] = marker.AssetId;
				_markHighlights[marker.Id] = highlight ?? new GameTag[0];
			}
			card[HsTag.PLAY_THIS_TURN_MARK] = g.Turn;
			foreach (GameTag tag in highlight ?? PlayThisTurnTags)
				if (card[tag] != 1) card[tag] = 1;
		}

		private static readonly GameTag[] PlayThisTurnTags = { GameTag.TAG_ONE_TURN_EFFECT, (GameTag)3908, (GameTag)1054, (GameTag)4663 };

		/// <summary>The highlight tags each non-default marker enchantment set (by its card id).</summary>
		private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, GameTag[]> _markHighlights =
			new System.Collections.Concurrent.ConcurrentDictionary<string, GameTag[]>();

		/// <summary>The card's "play this turn" marker enchantment card id and its highlight tags.</summary>
		private static (string Marker, GameTag[] Highlight) MarkOf(IPlayable card)
		{
			if (card[HsTag.PLAY_THIS_TURN_MARKER] > 0 && Cards.FromAssetId(card[HsTag.PLAY_THIS_TURN_MARKER]) is Card own)
				return (own.Id, _markHighlights.TryGetValue(own.Id, out GameTag[] tags) ? tags : PlayThisTurnTags);
			return (PlayThisTurnEnchantment, PlayThisTurnTags);
		}

		/// <summary>Is <paramref name="card"/> marked "if you play it this turn" for this turn?</summary>
		public static bool IsMarkedPlayThisTurn(IPlayable card) => card != null && card.Game.Turn > 0 && card[HsTag.PLAY_THIS_TURN_MARK] == card.Game.Turn;

		/// <summary>
		/// A marked card played this turn has spent its mark: live (live-20261001-questline 6977-6984, a marked Zap! played) right
		/// after the spell reaches the graveyard — TAG_ONE_TURN_EFFECT 0, NEW_1308e's 1234 = the card, HIDE_ENTITY, ZONE
		/// REMOVEDFROMGAME, LUNAHIGHLIGHTHINT (1054) 0, 4663 0 — and no end-of-turn sweep for it. CastSpell (a spell), PlayCardBlock
		/// (any other card, after it entered play: unrecorded).
		/// </summary>
		public static void SpendPlayThisTurnMark(IPlayable card)
		{
			if (card == null || card[HsTag.PLAY_THIS_TURN_MARK] == 0) return;
			Game g = card.Game;
			(string markerId, GameTag[] highlight) = MarkOf(card);
			card[HsTag.PLAY_THIS_TURN_MARK] = 0;
			if (card[HsTag.PLAY_THIS_TURN_MARKER] != 0) card[HsTag.PLAY_THIS_TURN_MARKER] = 0;
			if (card[GameTag.TAG_ONE_TURN_EFFECT] != 0) card[GameTag.TAG_ONE_TURN_EFFECT] = 0;
			foreach (Enchantment marker in card.AppliedEnchantments?.Where(e => e.Card.Id == markerId).ToList() ?? new List<Enchantment>())
			{
				if (g.History) g.PowerHistory.Add(PowerHistoryBuilder.TagChange(marker.Id, (GameTag)1234, card.Id));
				marker.Remove();
			}
			foreach (GameTag tag in highlight)
				if (card[tag] != 0) card[tag] = 0;
		}

		/// <summary>The end of the turn clears the player's marks (Game.MainCleanUp via NextStep): the marker enchantment goes, the
		/// card's highlight tags return to 0.</summary>
		internal static void ClearPlayThisTurnMarks(Controller c)
		{
			foreach (IPlayable card in c.HandZone.GetAll().ToList())
			{
				if (card[HsTag.PLAY_THIS_TURN_MARK] == 0) continue;
				(string markerId, GameTag[] highlight) = MarkOf(card);
				card[HsTag.PLAY_THIS_TURN_MARK] = 0;
				if (card[HsTag.PLAY_THIS_TURN_MARKER] != 0) card[HsTag.PLAY_THIS_TURN_MARKER] = 0;
				if (card[HsTag.CASTS_TWICE_TURN] != 0) card[HsTag.CASTS_TWICE_TURN] = 0;
				Game g = c.Game;
				foreach (Enchantment e in card.AppliedEnchantments?.Where(x => x.Card.Id == markerId).ToList() ?? new List<Enchantment>())
				{
					// HsServer (WILD_WEST): a card's own marker leaves in its TRIGGER block (effect index 1): live Stargazing's WW_425e
					// (session-20261002-204752 65551-65690) — to the graveyard, 1234 = the card
					bool own = markerId != PlayThisTurnEnchantment;
					if (own && g.History)
					{
						g.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, e.Id, "", 1, 0));
						g.PowerHistory.Add(PowerHistoryBuilder.TagChange(e.Id, (GameTag)1234, card.Id));
					}
					e.Remove();
					if (own && g.History)
						g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
				}
				foreach (GameTag tag in highlight)
					if (card[tag] != 0) card[tag] = 0;
			}
		}

		// =========================================================================================================== Starting hand

		/// <summary>Record both players' starting hands (the hand once the mulligan is over; forum
		/// us.forums.blizzard.com/…/cant-starlight-whelp-make-a-starlight-whelp/118368). Game.NextStep, once.</summary>
		internal static void RecordStartingHands(Game g)
		{
			foreach (Controller c in new[] { g.Player1, g.Player2 })
			{
				if (c[HsTag.STARTING_HAND_COUNT] != 0) continue; // recorded (-1: an empty hand)
				List<IPlayable> hand = c.HandZone.GetAll().ToList();
				int n = Math.Min(hand.Count, HsTag.STARTING_HAND_SLOTS);
				for (int i = 0; i < n; i++)
					c[HsTag.STARTING_HAND_1 + i] = hand[i].Card.AssetId;
				c[HsTag.STARTING_HAND_COUNT] = n > 0 ? n : -1;
			}
		}

		/// <summary>The cards of the player's starting hand (the Coin included for the second player: it is in hand during the
		/// mulligan — DOUBT, live-checks).</summary>
		public static List<Card> StartingHand(Controller c)
		{
			var cards = new List<Card>();
			int n = c[HsTag.STARTING_HAND_COUNT];
			for (int i = 0; i < n && i < HsTag.STARTING_HAND_SLOTS; i++)
				if (Cards.FromAssetId(c[HsTag.STARTING_HAND_1 + i]) is Card card)
					cards.Add(card);
			return cards;
		}

		// =========================================================================================================== Anomalies

		/// <summary>Client GameEntity tags ANOMALY1 / ANOMALY2 (3182 / 3183): the entity id of an active anomaly (client
		/// AnomalyMedallion, MulliganManager: the medallion and the mulligan banner).</summary>
		public const GameTag ANOMALY1 = (GameTag)3182, ANOMALY2 = (GameTag)3183;

		/// <summary>Is an anomaly active in this game (Cho'gall: never two — wiki Cho'gall notes)?</summary>
		public static bool AnomalyActive(Game g) => g[ANOMALY1] > 0;

		/// <summary>
		/// Start <paramref name="anomaly"/> (one of Cho'gall's RelatedCards TTN_002t*): its entity (owned by
		/// <paramref name="source"/>'s player, CREATOR = the source, no zone) becomes the game's ANOMALY1 (ANOMALY2 if one is
		/// already active). Its rule is its own script's: the caller applies it to both players. Returns the entity.
		/// </summary>
		public static IPlayable StartAnomaly(Game g, Card anomaly, IPlayable source)
		{
			if (anomaly == null || source == null) return null;
			IPlayable entity = Entity.FromCard(source.Controller, anomaly, new EntityData { [GameTag.CREATOR] = source.Id });
			if (g[ANOMALY1] == 0) g[ANOMALY1] = entity.Id;
			else if (g[ANOMALY2] == 0) g[ANOMALY2] = entity.Id;
			return entity;
		}

		// =========================================================================================================== Small shared queries

		/// <summary>Client player tag 2868 (unnamed): how many times the player improved their Chaotic Tendrils (live
		/// live-20261006-yogg-cthun 10384-10560: +1 at the end of each Tendril's POWER block). A Tendril's "@-Cost" is 1 + it
		/// (Power.HandCounter on YOG_514).</summary>
		public const GameTag TENDRIL_IMPROVEMENTS = (GameTag)2868;

		/// <summary>"didn't start in your deck": an entity made after the decks were (Game.FirstGeneratedEntityId) — generated,
		/// shuffled in, copied (Sharp-Eyed Seeker, Ra-den, Stowaway, Steamcleaner, Arcane Luminary).</summary>
		public static bool DidntStartInDeck(IPlayable p) => p != null && p.Id >= p.Game.FirstGeneratedEntityId;

		/// <summary>The spell schools (client SPELL_SCHOOL 1635, &gt; 0) of the spells the player played this game (Sif, Inquisitive
		/// Creation, Elemental Inspiration, Wisdom of Norgannon, Discovery of Magic; Multicaster, Razzle-Dazzler).</summary>
		public static HashSet<int> SpellSchoolsCast(Controller c)
		{
			var schools = new HashSet<int>();
			foreach (PlayHistoryEntry h in c.PlayHistory)
				if (h.SourceCard?.Type == CardType.SPELL && h.SourceCard[(GameTag)1635] > 0)
					schools.Add(h.SourceCard[(GameTag)1635]);
			return schools;
		}

		/// <summary>
		/// "Cast N random &lt;class&gt; Secrets" (Norgannon's Unlimited Potential, Reliquary Researcher): one at a time, a random
		/// Secret from the generation pool matching <paramref name="which"/> that isn't up already (the hero zone's one-copy rule),
		/// cast (CastSpell: a Secret goes to the Secret zone); stops when the zone is full or none is left (forum: Norgannon uses
		/// the normal Secret limits). CREATOR = <paramref name="source"/>. Returns how many were cast.
		/// </summary>
		public static int CastRandomSecrets(Controller c, IEntity source, Func<Card, bool> which, int n)
		{
			Game g = c.Game;
			int cast = 0;
			for (int i = 0; i < n; i++)
			{
				List<Card> pool = Cards.GenerationPool(g, c)
					.Where(x => x.IsSecret && which(x) && !c.SecretZone.Any(p => p.Card.Id == x.Id)).ToList();
				if (pool.Count == 0 || c.SecretZone.IsFull) break;
				var secret = (Spell)Entity.FromCard(c, pool[g.Random.Next(pool.Count)], new EntityData { { GameTag.CREATOR, source.Id } });
				CastSpell.Invoke(c, g, secret, null, 0);
				cast++;
			}
			return cast;
		}

		/// <summary>A random card among <paramref name="card"/>'s client RelatedCards of <paramref name="type"/> (repeats allowed:
		/// From the Scrapheap / Drone Deconstructor's Sparkbots, live vfx 89069), or null.</summary>
		public static Card RandomRelated(Game g, Card card, CardType type)
		{
			List<Card> related = Loader.ReprintCards.RelatedCardIds(card.Id)?.Select(Cards.TryFromId)
				.Where(x => x != null && x.Type == type).ToList();
			return related == null || related.Count == 0 ? null : related[g.Random.Next(related.Count)];
		}

		/// <summary>A NEW card of <paramref name="dead"/> with permanent +<paramref name="atk"/>/+<paramref name="health"/>: its base
		/// Attack / Health raised (no enchantment: live Bottomfeeder, live-20261007-vfx-decks 36598), its earlier permanent bonuses
		/// kept (its own ATK / HEALTH data). CREATOR = <paramref name="source"/>. Not placed anywhere.</summary>
		public static IPlayable CopyWithPermanentStats(Controller c, IEntity source, IPlayable dead, int atk, int health)
		{
			// (TIME_TRAVEL: a weapon keeps its earlier permanent Attack too — High King's Hammer "with +2 Attack permanently"; its
			// Durability isn't Health)
			int baseAtk = dead.Card.ATK + PermanentBonus(dead, GameTag.ATK);
			var data = new EntityData
			{
				[GameTag.CREATOR] = source?.Id ?? 0,
				[GameTag.ATK] = baseAtk + atk,
			};
			if (dead is Character) data[GameTag.HEALTH] = dead.Card.Health + PermanentBonus(dead, GameTag.HEALTH) + health;
			return Entity.FromCard(c, dead.Card, data);
		}

		/// <summary>HsServer (WHIZBANG): a card's base Attack / Health with the permanent changes it was made with (Floppy Hydra doubles it,
		/// Nesting Golem takes 1 off).</summary>
		public static int PermanentBase(IPlayable p, GameTag tag) => p.Card[tag] + PermanentBonus(p, tag);

		// a permanent bonus a card was made with (an earlier copy's raised base stats live in its own entity data)
		private static int PermanentBonus(IPlayable p, GameTag tag) =>
			((Entity)p)._data.TryGetValue(tag, out int had) && p.Id >= p.Game.FirstGeneratedEntityId ? Math.Max(0, had - p.Card[tag]) : 0;

		// =========================================================================================================== Crafted cards

		/// <summary>
		/// A card crafted from a base and two parts (Ignis' custom weapon: a Runic Shortsword / Axe / Greatmace with a keyword
		/// rune and an effect rune; live live-20261006-dredge 2351-2580): the module card of <see cref="ModularCard"/> — the parts'
		/// keywords and effects combined (their scripts' Power: a targeted Battlecry with its play requirements, after-attack
		/// triggers, a Deathrattle), MODULAR_ENTITY_PART_1 / _2 the parts (the client's "{0} {1}" text builder) — but the base's
		/// own Cost and stats (the runes have none; Zilliax's modules carry theirs, the base here does). Make the entity with
		/// Entity.FromCard.
		/// </summary>
		public static Card CraftedCard(Card look, Card part1, Card part2)
		{
			Card built = ModularCard(look, part1, part2);
			var tags = new Dictionary<GameTag, int>(built.Tags);
			foreach (GameTag own in new[] { GameTag.COST, GameTag.ATK, GameTag.HEALTH, GameTag.DURABILITY })
				if (look.Tags.TryGetValue(own, out int value)) tags[own] = value;
				else tags.Remove(own);
			var list = tags.Select(t => new Tag(t.Key, t.Value)).ToList();
			list.Add(new Tag(GameTag.CARDNAME, look.Name ?? ""));
			list.Add(new Tag(GameTag.CARDTEXT, string.Join("\n", new[] { part1.Text, part2.Text }.Where(x => !string.IsNullOrEmpty(x)))));
			var crafted = new Card(look.Id, look.AssetId, list.ToArray(), new Dictionary<PlayReq, int>(), look.Entourage,
				look.RefTags.Select(r => new Tag(r.Key, r.Value)).ToArray());
			if (built.PlayRequirements != null && built.PlayRequirements.Count > 0)
				crafted.SetPlayRequirements(new Dictionary<PlayReq, int>(built.PlayRequirements));
			crafted.Power = built.Power;
			crafted.Implemented = built.Implemented;
			return crafted;
		}

		// =========================================================================================================== Cost floor

		/// <summary>
		/// A cost floor aura on both players ("Cards can't cost less than (2)": Razorscale). Rule book (Advanced_rulebook.txt:870):
		/// "Razorscale's effect is applied after all other cost-reducing Enchantments so that no card can cost less than 2"
		/// (Playable.Cost; both players; the Coin costs 2). Hero Powers aren't cards (DOUBT, live-checks).
		/// </summary>
		public static Auras.Aura CostFloorAura(int floor) => new Auras.Aura(Auras.AuraType.CONTROLLERS,
			new Effect(HsTag.COST_FLOOR_SOURCES, EffectOperator.ADD, 1), new Effect(HsTag.COST_FLOOR_SUM, EffectOperator.ADD, floor));

		/// <summary>
		/// The cost floor shown on the cards it raises (Razorscale's TTN_924e "Razor's Edge: Costs at least (2)."): a marker aura on
		/// both hands — no effect of its own, Restless (costs change without zone moves) — showing <paramref name="shownOnCards"/>
		/// on each card whose cost before the floor is below it. Engine rule (engine-rules.md, user 2026-10-04): a hand cost aura
		/// shows its client enchantment on each card it affects (Aura.ShowAs, like Sorcerer's Apprentice).
		/// </summary>
		public static Auras.IAura[] CostFloorShownAuras(string shownOnCards)
		{
			var raised = new Conditions.SelfCondition(p => p is Playable card && !(p is HeroPower) && p.Controller != null &&
				card.CostBeforeFloor < CostFloor(p.Controller));
			return new Auras.IAura[]
			{
				new Auras.Aura(Auras.AuraType.HAND) { Condition = raised, Restless = true, ShowAs = shownOnCards },
				new Auras.Aura(Auras.AuraType.OP_HAND) { Condition = raised, Restless = true, ShowAs = shownOnCards },
			};
		}

		/// <summary>The player's cards' cost floor now (0: none). Each active floor is the same value in the data (2); with
		/// several, their average (= that value).</summary>
		public static int CostFloor(Controller c)
		{
			if (c == null) return 0;
			int sources = c.ControllerAuraEffects[HsTag.COST_FLOOR_SOURCES];
			return sources > 0 ? c.ControllerAuraEffects[HsTag.COST_FLOOR_SUM] / sources : 0;
		}

		// =========================================================================================================== Excess attack damage

		/// <summary>
		/// "Excess damage dealt by attacks hits the enemy hero" (Always a Bigger Jormungar's TTN_079e: HsTag.EXCESS_ATTACK_DAMAGE_TO_HERO
		/// on the minion): when it attacks a minion, the damage past the defender's Health goes to the enemy hero instead (forum
		/// us.forums.blizzard.com/t/bug-with-always-a-bigger-jormungar/111113: "a parcel of 5 damage for the minion, and a parcel
		/// of 1 damage for the enemy hero"; once however many copies; attacking a hero deals its full damage — wiki patch
		/// 2023-08-04). Returns the excess (Actions/Attack.cs deals it). A Divine Shield or Immune defender takes the whole hit
		/// (DOUBT, live-checks).
		/// </summary>
		internal static int ExcessAttackDamage(ICharacter attacker, ICharacter defender, int damage)
		{
			if (damage <= 0 || attacker[HsTag.EXCESS_ATTACK_DAMAGE_TO_HERO] <= 0 || !(defender is Minion m)) return 0;
			if (m.HasDivineShield || m.IsImmune) return 0;
			return Math.Max(0, damage - Math.Max(0, m.Health));
		}

		// =========================================================================================================== Locked hand cards

		/// <summary>
		/// "can't be played next turn" (Frozen Over: "Your opponent can not play them next turn"; the SW_052t4e pattern): the card
		/// can't be played until the end of its owner's next turn (game turn <paramref name="untilTurn"/>), shown as
		/// LITERALLY_UNPLAYABLE (the lock) while it lasts. <paramref name="shown"/>: the enchantment the client shows on it
		/// (Frozen Solid: TTN_744e1), removed with the lock. Game.NextStep unlocks it at the end of that turn.
		/// </summary>
		public static void LockInHand(IPlayable card, IEntity source, int untilTurn, string shown = null)
		{
			if (card == null || card.Zone?.Type != Zone.HAND) return;
			card[HsTag.LOCKED_UNTIL_TURN] = untilTurn;
			if (card[HsTag.LITERALLY_UNPLAYABLE] == 0) card[HsTag.LITERALLY_UNPLAYABLE] = 1;
			if (card[HsTag.UNPLAYABLE_VISUALS] == 0) card[HsTag.UNPLAYABLE_VISUALS] = 1; // (CATACLYSM: live Daze 140614 62238)
			if (shown != null && Cards.TryFromId(shown) is Card marker && source is IPlayable creator)
			{
				AddEnchantmentBlock(card.Game, marker, creator, card, 0, 0, 0);
				card[HsTag.LOCK_SHOWN_ENCHANTMENT] = marker.AssetId;
			}
		}

		/// <summary>The end of turn <paramref name="turn"/> unlocks the cards locked until then (Game.NextStep).</summary>
		internal static void UnlockHandCards(Game g, int turn)
		{
			foreach (Controller c in new[] { g.Player1, g.Player2 })
				foreach (IPlayable card in c.HandZone.GetAll().ToList())
				{
					int until = card[HsTag.LOCKED_UNTIL_TURN];
					if (until == 0 || until > turn) continue;
					card[HsTag.LOCKED_UNTIL_TURN] = 0;
					if (card[HsTag.LITERALLY_UNPLAYABLE] != 0) card[HsTag.LITERALLY_UNPLAYABLE] = 0;
					if (card[HsTag.UNPLAYABLE_VISUALS] != 0) card[HsTag.UNPLAYABLE_VISUALS] = 0;
					if (card[PREPARING] != 0) card[PREPARING] = 0; // (VIOLET_HOLD: the Prepare lock)
					int shown = card[HsTag.LOCK_SHOWN_ENCHANTMENT];
					if (shown == 0) continue;
					card[HsTag.LOCK_SHOWN_ENCHANTMENT] = 0;
					card.AppliedEnchantments?.Where(e => e.Card.AssetId == shown).ToList().ForEach(e => e.Remove());
				}
		}
	}
}
