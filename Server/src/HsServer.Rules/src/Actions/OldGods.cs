// HsServer addition (not upstream SabberStoneCore): Old Gods (the BE set): C'Thun, N'Zoth, Yogg-Saron and Y'Shaarj are Titans with four
// abilities (Actions/Titans.cs: Generic.IsOldGod, the extra action, the 4th pip); this file holds what other cards do with them — an
// ability triggered on its own ("Choose a C'Thun ability to trigger") and C'Thun's abilities triggered this game.
using System;
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks;
using SabberStoneCore.Tasks.SimpleTasks;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>Client tags: the player's nameplate shows another card's name (REPLACE_NAMEPLATE 4978: its dbf id), Yogg-Saron's fog over
		/// the board while he plays (YOGG_FOG 4981, on Yogg-Saron).</summary>
		public const GameTag REPLACE_NAMEPLATE = (GameTag)4978, YOGG_FOG = (GameTag)4981;

		/// <summary>Does the AI make this player's decisions now (client AI_MAKES_DECISIONS_FOR_PLAYER 1335: Mastermind Yogg-Saron's turn).
		/// The server lets its AI play that player's moves and choices; the client gets no options.</summary>
		public static bool AiControls(Controller c) => c != null && c[GameTag.AI_MAKES_DECISIONS_FOR_PLAYER] > 0;

		/// <summary>
		/// Mastermind Yogg-Saron: "Battlecry: End your turn. Yogg-saron takes an extra turn for you. (Once per game)". Live
		/// (session-20261110-153646 66806-67675, 197543; 20261110-200615 43852): in its POWER block the turn ends, NUM_TURNS_LEFT 2 with
		/// NUM_TURNS_LAST_AFFECTED_BY = Yogg-Saron, "Controlled" (BE_009e2) on the player (its NUM_1 = Yogg-Saron's id),
		/// AI_MAKES_DECISIONS_FOR_PLAYER 1, REPLACE_NAMEPLATE = Yogg-Saron's dbf id, YOGG_FOG 1 on him; the extra turn
		/// (IS_CURRENT_TURN_AN_EXTRA_TURN) is played by the AI (no options to the client); at its end BE_009e2's TRIGGER block: YOGG_FOG 0,
		/// the enchantment to the graveyard, AI_MAKES_DECISIONS_FOR_PLAYER 0, REPLACE_NAMEPLATE 0. (The turn ends right after the PLAY
		/// block here — Game.EndTurnPending — not inside it.)
		/// </summary>
		public static void YoggTakesTurn(Controller c, IPlayable yogg)
		{
			Game g = c.Game;
			if (g.CurrentPlayer != c || c[HsTag.YOGG_TURN] > 0) return;
			c[HsTag.YOGG_TURN] = g.Turn + 1;
			GrantExtraTurn(c, yogg);
			AddEnchantmentBlock(g, Cards.FromId("BE_009e2"), yogg, c, yogg.Id, -1);
			c[GameTag.AI_MAKES_DECISIONS_FOR_PLAYER] = 1;
			c[REPLACE_NAMEPLATE] = yogg.Card.AssetId;
			yogg[YOGG_FOG] = 1;
			g.EndTurnPending = true;
		}

		/// <summary>The end of Yogg-Saron's turn ("Controlled", BE_009e2: its TRIGGER block at the end of that turn).</summary>
		public static Triggers.Trigger YoggTurnEnds => new Triggers.Trigger(TriggerType.TURN_END)
		{
			Condition = new Conditions.SelfCondition(p => p.Game.Turn == p.Controller[HsTag.YOGG_TURN]),
			InHistory = true,
			SingleTask = new CustomTask((g, c, s, t, stack) =>
			{
				if (!(s is Enchantment e)) return;
				if (g.IdEntityDic.TryGetValue(e.ScriptTag1, out IPlayable yogg) && yogg[YOGG_FOG] != 0) yogg[YOGG_FOG] = 0;
				e.Remove();
				c[GameTag.AI_MAKES_DECISIONS_FOR_PLAYER] = 0;
				c[REPLACE_NAMEPLATE] = 0;
			})
		};

		/// <summary>
		/// Decipher ("(Deciphers in @ turns.)", Tome Translator): at the start of each of its owner's turns while it is in hand, its count
		/// (TAG_SCRIPT_DATA_NUM_1) goes down in its TRIGGER block (live session-20261021-194927 35518: a hand-buff effect on it, then
		/// NUM_1 1); at 0 it becomes one of its versions at random — the minions among its card's RelatedCards — keeping its enchantments
		/// (live 36044: NUM_1 0, the transform effect, CHANGE_ENTITY, its Cost discount kept).
		/// </summary>
		public static Triggers.Trigger DecipherTrigger() => new Triggers.Trigger(TriggerType.TURN_START)
		{
			TriggerActivation = TriggerActivation.HAND,
			SingleTask = new CustomTask((g, c, s, t, stack) =>
			{
				if (!(s is IPlayable card) || card.Zone?.Type != Zone.HAND) return;
				int left = Math.Max(0, card[GameTag.TAG_SCRIPT_DATA_NUM_1] - 1);
				if (left > 0)
				{
					bool buff = g.History && g.OpenSubSpell(card, DecipherTickFx, new[] { card.Id });
					card[GameTag.TAG_SCRIPT_DATA_NUM_1] = left;
					if (buff) g.CloseSubSpells(card.Id);
					return;
				}
				card[GameTag.TAG_SCRIPT_DATA_NUM_1] = 0;
				var versions = DecipherVersions(card.Card);
				if (versions.Count == 0) return;
				bool open = g.History && g.OpenSubSpell(card, DecipherTransformFx, new[] { card.Id });
				ChangeEntityBlock(c, card, versions[g.Random.Next(versions.Count)], true);
				if (open) g.CloseSubSpells(card.Id);
			})
		};

		/// <summary>(2026-11-20) "… at the end of this turn" (Assimilating Ooze's "Summon a copy of this at the end of this turn"): the task
		/// waits for this turn's end (Game.EndOfThisTurn) and runs then with <paramref name="source"/> as its source — a single task, so it
		/// also works as an Assemble half (unlike the entity-bound Trigger Echoing Ooze uses). User (2026-11-20): if the source dies before
		/// then, the effect is cancelled, same as Echoing Ooze (FP1_003, ECHOING_OOZE_SPELL) — a copy can't be made of something dead.
		/// RunEndOfThisTurn checks the source is still in Zone.PLAY.</summary>
		public static void AtEndOfThisTurn(Controller c, IEntity source, ISimpleTask task) =>
			c.Game.EndOfThisTurn.Add((source.Id, c.PlayerId, task));

		/// <summary>Runs the "at the end of this turn" effects (Game.MainEnd, after the turn's end triggers): each in a TRIGGER block of its
		/// source (shown in the history), in the order they were made; skipped if the source died or otherwise left PLAY since (user,
		/// 2026-11-20). No live recording of one yet (RESEARCH/live-checks.md).</summary>
		public static void RunEndOfThisTurn(Game g)
		{
			if (g.EndOfThisTurn.Count == 0) return;
			var due = g.EndOfThisTurn.ToList();
			g.EndOfThisTurn.Clear();
			foreach (var (sourceId, player, task) in due)
			{
				if (!g.IdEntityDic.TryGetValue(sourceId, out IPlayable source) || source.Zone?.Type != Zone.PLAY) continue;
				Controller c = g.Player1.PlayerId == player ? g.Player1 : g.Player2;
				if (c.Hero.IsDead || c.Opponent.Hero.IsDead) return;
				if (g.History)
				{
					PowerHistoryBlockStart block = PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, source.Id, "", 0, 0);
					block.ShowInHistory = true;
					g.PowerHistory.Add(block);
				}
				g.TaskQueue.StartEvent();
				task.Process(g, c, source, null);
				g.ProcessTasks();
				g.TaskQueue.EndEvent();
				if (g.History) g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
				g.DeathProcessingAndAuraUpdate();
			}
		}

		/// <summary>A Decipher card's versions: the minions among its RelatedCards.</summary>
		public static List<Card> DecipherVersions(Card card) => (Loader.ReprintCards.RelatedCardIds(card.Id) ?? new List<string>())
			.Select(Cards.TryFromId).Where(x => x != null && x.Type == CardType.MINION && x.Id != card.Id).ToList();

		/// <summary>
		/// A Decipher card's Battlecry before it deciphered ("???"): live (session-20261021-013733 10865-11010) — a random version's
		/// Battlecry runs from it, then a set-aside copy of that version (CREATOR = it) and it turns into that version where it stands
		/// (a transform effect, CHANGE_ENTITY, enchantments kept).
		/// </summary>
		public static ISimpleTask UndecipheredBattlecry => new CustomTask((g, c, s, t, stack) =>
		{
			if (!(s is IPlayable card)) return;
			var versions = DecipherVersions(card.Card);
			if (versions.Count == 0) return;
			Card version = versions[g.Random.Next(versions.Count)];
			version.Power?.PowerTask?.Process(g, c, s, t, stack);
			g.ProcessTasks();
			if (card.Zone?.Type != Zone.PLAY) return;
			Entity.FromCard(c, version, new EntityData { [GameTag.CREATOR] = card.Id }, c.SetasideZone);
			bool open = g.History && g.OpenSubSpell(card, UndecipheredTransformFx, new[] { card.Id });
			ChangeEntityBlock(c, card, version, true);
			if (open) g.CloseSubSpells(card.Id);
		});

		/// <summary>Live's Decipher effects (session-20261021-194927 35519, 36047; 20261021-013733 11003).</summary>
		private const string UndecipheredTransformFx = "ReuseFX_Generic_Impact_Transform_Generic_Super:035a11c49648b0f4fb379d2a5407d861";
		private const string DecipherTickFx = "ReuseFX_Shadow_HandBuff_ScaleUp_Super:5f7cf1c62a5adff4aab0fabde6220034",
			DecipherTransformFx = "ReuseFX_Warlock_Impact_Transform_CardInHand_Super:182bcd179430af94fa5bff3f858a231b";

		/// <summary>C'Thun, Eye of the Empire's four abilities, in its order (RelatedCards: Wrath, Domain, Glare, Servants).</summary>
		public static readonly string[] CthunAbilities = { "BE_013t1", "BE_013t2", "BE_013t3", "BE_013t4" };

		/// <summary>Player tags: C'Thun's ability N triggered this game (client CTHUN_ABILITY1..4_USED 4527 / 4760 / 4769 / 4770; live
		/// session-20261106-001808 160563: 4527 = 1 inside Wrath's POWER block; 20261021-013733 96533: 4769 = 1 in Glare's), from C'Thun
		/// or any card that triggers one.</summary>
		public static readonly GameTag[] CthunAbilityUsed = { (GameTag)4527, (GameTag)4760, (GameTag)4769, (GameTag)4770 };

		/// <summary>Note C'Thun's ability <paramref name="n"/> (1..4) as triggered by <paramref name="c"/>.</summary>
		public static void NoteCthunAbility(Controller c, int n)
		{
			if (c[CthunAbilityUsed[n - 1]] == 0) c[CthunAbilityUsed[n - 1]] = 1;
		}

		/// <summary>
		/// C'Thun, Eye of the Empire: "If you've triggered all 4 C'Thun abilities this game, this minion's abilities are quadrupled."
		/// (reworded from "tripled" in the 2026-11-03 balance patch, Server/data/dbf-37.0.0.0-20261120/changes.txt) — 4 for an ability
		/// C'Thun itself uses (its source is the C'Thun minion, or an entity it made: Glare's Lifesteal half BE_013t6) once its
		/// controller triggered all four, else 1. Another card's triggered ability isn't "this minion's".
		/// </summary>
		public static int CthunTimes(IEntity source)
		{
			IEntity from = source;
			if (!(from is Minion) && from is IPlayable p && p[GameTag.CREATOR] > 0 && p.Game.IdEntityDic.TryGetValue(p[GameTag.CREATOR], out IPlayable creator))
				from = creator;
			if (!(from is Minion m) || m.Card.Id != "BE_013" || !IsOldGod(m)) return 1;
			return CthunAbilityUsed.All(t => m.Controller[t] > 0) ? 4 : 1;
		}

		/// <summary>Once <paramref name="c"/> triggered all four, the abilities of each C'Thun they have (play, hand, deck) show their
		/// quadrupled text (USE_ALTERNATE_CARD_TEXT — live 20261106-001808: on C'Thun's BE_013t1).</summary>
		public static void CthunAltText(Controller c)
		{
			if (!CthunAbilityUsed.All(t => c[t] > 0)) return;
			foreach (IPlayable cthun in c.BoardZone.GetAll().Cast<IPlayable>().Concat(c.HandZone.GetAll()).Concat(c.DeckZone.GetAll()).Where(p => p.Card.Id == "BE_013"))
				for (int i = 0; i < TitanAbilityCount(cthun); i++)
					if (TitanAbility(cthun, i) is IPlayable ability && ability[GameTag.USE_ALTERNATE_CARD_TEXT] == 0)
						ability[GameTag.USE_ALTERNATE_CARD_TEXT] = 1;
		}

		/// <summary>
		/// An ability card triggered on its own (live 20261021-013733 96513-96560, Gargantuan Slitherer's "Choose a C'Thun ability to
		/// trigger"): a new entity of it (CREATOR = <paramref name="source"/>), its target noted (CARD_TARGET), in PLAY while it resolves;
		/// HISTORY_TARGET and SLUSH_TIME 3000 (with SHOW_BIG_CARD first when <paramref name="bigCard"/>: Glare's Lifesteal half), its
		/// POWER block (ShowInHistory) with its effect, then the graveyard. Not a spell cast: nothing counts it as played or cast.
		/// </summary>
		public static IPlayable TriggerAbility(Controller c, IEntity source, Card ability, ICharacter target, bool bigCard = false)
		{
			Game g = c.Game;
			IPlayable e = Entity.FromCard(c, ability, new EntityData { [GameTag.CREATOR] = source.Id }, c.SetasideZone);
			if (target != null) e.CardTarget = target.Id;
			e[GameTag.ZONE] = (int)Zone.PLAY;
			if (g.History)
			{
				var shown = new List<int> { e.Id };
				if (bigCard)
				{
					e[GameTag.REVEALED] = 1;
					g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SHOW_BIG_CARD, Data = 0, Info = shown });
				}
				else
					g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.HISTORY_TARGET, Data = 0, Info = shown });
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 3000, Info = shown });
				PowerHistoryBlockStart block = PowerHistoryBuilder.BlockStart(BlockType.POWER, e.Id, "", 0, target?.Id ?? 0);
				block.ShowInHistory = true;
				g.PowerHistory.Add(block);
			}
			g.TaskQueue.StartEvent();
			ability.Power?.PowerTask?.Process(g, c, e, target);
			g.ProcessTasks();
			g.TaskQueue.EndEvent();
			if (g.History) g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			g.DeathProcessingAndAuraUpdate();
			if (e.Zone != null) e.Zone.Remove(e);
			c.GraveyardZone.Add(e);
			return e;
		}

		/// <summary>
		/// "Choose a C'Thun ability to trigger" (Gargantuan Slitherer, Ritual of C'Thun, Tentacle Lash, Spawn of C'Thun, Dark Deacon;
		/// Watch Over: two different). Live (20261021-013733 7295-7454 and 96513): a choice of the four (or those not picked yet), the
		/// pick revealed; an ability that aims (Glare) asks for its target next (a TARGET choice among its valid targets; none: it
		/// isn't offered); then it is triggered (TriggerAbility). <paramref name="times"/> &gt; 1: the next choice offers the others.
		/// </summary>
		public static void ChooseOldGodAbility(Controller c, IEntity source, string[] abilities, int times = 1)
		{
			var offer = abilities.Select(Cards.FromId).Where(a => a != null && (!a.MustHaveTargetToPlay && a.TargetingType == TargetingType.None
				|| a.GetValidPlayTargets(c).Count > 0)).ToList();
			if (times <= 0 || offer.Count == 0) return;
			c.Game.FlushPendingFx(source.Id);
			var options = offer.Select(a => Entity.FromCard(c, a, new EntityData { [GameTag.CREATOR] = source.Id }, c.SetasideZone)).ToList();
			int sourceId = source.Id;
			CreateChoiceEntities(c, source, options, ChoiceAction.STACK, new CustomTask((g, c2, s, picked, stack) =>
			{
				if (!(picked is IPlayable pick)) return;
				pick[GameTag.REVEALED] = 1;
				Card ability = pick.Card;
				IEntity from = g.IdEntityDic.TryGetValue(sourceId, out IPlayable src) ? src : s;
				string[] rest = abilities.Where(id => id != ability.Id).ToArray();
				void Then(Game g2, Controller c3) { if (times > 1) ChooseOldGodAbility(c3, from, rest, times - 1); }
				if (ability.TargetingType == TargetingType.None)
				{
					TriggerAbility(c2, from, ability, null);
					Then(g, c2);
					return;
				}
				var valid = new HashSet<int>(ability.GetValidPlayTargets(c2).Select(x => x.Id));
				PickTargets(c2, from, 1, ch => valid.Contains(ch.Id), (g3, c3, s3, target) =>
				{
					TriggerAbility(c3, s3, ability, target);
					Then(g3, c3);
				});
			}));
		}
	}
}
