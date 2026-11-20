// HsServer addition (not upstream SabberStone): the Dormant keyword, one mechanism for every card that uses it.
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Model.Zones;

namespace SabberStoneCore.Actions
{
	/// <summary>
	/// HsServer: Dormant, as live sends it (live-20261004-night: The Darkness, Slagmaw, Gorm, Soridormi;
	/// live-20261003-dk-classxp: the Dreadseeds). Rules: wiki Dormant (engine-rules.md, Keywords: Dormant).
	/// <list type="bullet">
	/// <item>Going Dormant: the minion's own TRIGGER block (Loader.DormantCards: its effect index, The Darkness's keyword 1652)
	/// attaches its counter enchantment (DORMANT_AWAKEN_CONDITION_ENCHANT, SCORE_VALUE_1 = the total), turns its keywords off
	/// (Taunt, Rush, Lifesteal, Elusive…), then DORMANT 1 and UNTOUCHABLE 1. An effect (Incarceration) does it in its own block
	/// with its own counter (MAW_026e).</item>
	/// <item>Counting: a TRIGGER block on the counter (index 0, TRIGGER_VISUAL) raises SCORE_VALUE_2 (the client shows
	/// SCORE_VALUE_1 − SCORE_VALUE_2). Turns: at the start of the owner's turn, inside the generic progress-counter effect;
	/// a condition (The Darkness's Candles): no effect.</item>
	/// <item>Awakening: UNTOUCHABLE 0, DORMANT 0, 1449 and DORMANT_AWAKENED_THIS_TURN 1, the counter removed, the keywords
	/// back, then the minion's "When this awakens" TRIGGER block (ShowInHistory). Turns: inside the last count's block;
	/// a condition: the counter's second block (index 1).</item>
	/// </list>
	/// </summary>
	public static partial class Generic
	{
		/// <summary>Client tags: the card enters play Dormant (1652; also the TriggerKeyword of The Darkness's block); the
		/// counter enchantment (1519); the minion has awakened (1449); it awakened this turn (4606, cleared at turn end).</summary>
		public const GameTag STARTS_DORMANT = (GameTag)1652, DORMANT_AWAKEN_CONDITION_ENCHANT = (GameTag)1519,
			HAS_AWAKENED = (GameTag)1449, DORMANT_AWAKENED_THIS_TURN = (GameTag)4606, ELUSIVE = (GameTag)1211;

		private const string ProgressCounterFx = "ReuseFX_Generic_ProgressCounter_Play_Super_FastActorTriggers:9511e8d4aafb51c40a0939bac572f4aa";

		/// <summary>Keywords a Dormant minion doesn't show (live: TAUNT, RUSH, LIFESTEAL, ELUSIVE and its targeting tags go
		/// to 0 when it goes Dormant and come back when it awakens; live-20261006 Imprisoned Phoenix: SPELLPOWER too, so a
		/// Dormant minion gives no Spell Damage).</summary>
		private static readonly GameTag[] SleepingKeywords =
		{
			GameTag.TAUNT, GameTag.RUSH, GameTag.CHARGE, GameTag.LIFESTEAL, GameTag.DIVINE_SHIELD, GameTag.STEALTH,
			GameTag.WINDFURY, GameTag.POISONOUS, GameTag.REBORN, ELUSIVE, GameTag.CANT_BE_TARGETED_BY_SPELLS,
			GameTag.CANT_BE_TARGETED_BY_HERO_POWERS, GameTag.SPELLPOWER,
		};
		private const int SleptKeywordBase = 1_100_000; // internal: the value a keyword had before it went to sleep (+1)

		/// <summary>HsServer: a minion entering play goes Dormant (BoardZone.Add: Card.DormantTurns / Card.StartsDormant; wiki
		/// Dormant: "Minions will always start Dormant until their condition is fulfilled, no matter how they were summoned").</summary>
		internal static void EnterDormant(Minion m)
		{
			Loader.DormantCards.Entry data = Loader.DormantCards.Of(m.Card.Id);
			GoDormant(m, m.Card.DormantTurns, data?.Counter, m, ownBlock: true, data);
		}

		/// <summary>
		/// HsServer: an effect puts a minion in play to sleep (Incarceration). <paramref name="turns"/> &gt; 0: it awakens by
		/// itself, counted at the start of its owner's turn; 0: only its condition wakes it (<see cref="Awaken"/>,
		/// <see cref="AwakenProgress"/>). <paramref name="counter"/>: the effect's counter enchantment (MAW_026e "Doing Time").
		/// Enchantments stay ("do not reset nor effect enchantments when awakened").
		/// </summary>
		public static void MakeDormant(Minion m, int turns = 0, string counter = null, IPlayable source = null)
		{
			if (m?.Zone?.Type != Zone.PLAY) return;
			GoDormant(m, turns, counter, source ?? m, ownBlock: false, Loader.DormantCards.Of(m.Card.Id));
		}

		private static void GoDormant(Minion m, int turns, string counterId, IPlayable creator, bool ownBlock, Loader.DormantCards.Entry data)
		{
			Game g = m.Game;
			bool history = g.History && ownBlock;
			if (history)
			{
				PowerHistoryBlockStart start = PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, m.Id, "", data?.EnterIndex ?? 1, 0);
				start.TriggerKeyword = data?.EnterKeyword ?? 0;
				g.PowerHistory.Add(start);
			}
			m[HsTag.DORMANT_TIMED] = turns > 0 ? 1 : 0;
			m[HsTag.DORMANT_TURNS_LEFT] = System.Math.Max(turns, 0);
			m[HsTag.AWAKEN_PROGRESS] = 0;
			if (!string.IsNullOrEmpty(counterId) && Cards.TryFromId(counterId) is Card counterCard)
			{
				AddEnchantmentBlock(g, counterCard, creator, m);
				if (m.AppliedEnchantments?.LastOrDefault(e => e.Card == counterCard) is Enchantment counter)
				{
					int total = turns > 0 ? turns : data?.Total > 0 ? data.Total : counterCard[GameTag.SCORE_VALUE_1];
					counter[GameTag.SCORE_VALUE_1] = total;
					counter[DORMANT_AWAKEN_CONDITION_ENCHANT] = 1;
					m[HsTag.DORMANT_COUNTER] = counter.Id;
				}
			}
			if (ownBlock && m.Card.Power?.DormantTask is Tasks.ISimpleTask onDormant) // The Darkness: Candle Detect
				onDormant.Process(g, m.Controller, m, null);
			if (m.Card[STARTS_DORMANT] > 0) m[STARTS_DORMANT] = 0;
			foreach (GameTag k in SleepingKeywords)
				if (m[k] > 0)
				{
					m[(GameTag)(SleptKeywordBase + (int)k)] = m[k] + 1;
					m[k] = 0;
				}
			m[GameTag.DORMANT] = 1;
			m[GameTag.UNTOUCHABLE] = 1;
			SleepStats(m);
			if (history)
				g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			m.Controller.BoardZone.RecountUntouchables();
		}

		/// <summary>HsServer: the turns a turn-counted Dormant minion still sleeps (0: none / a condition Dormant).</summary>
		public static int DormantTurnsLeft(Minion m) => m == null ? 0 : m[HsTag.DORMANT_TURNS_LEFT];

		/// <summary>HsServer: "awaken N turns sooner" (Excavate for Slagmaw, Wild Spirits): fewer turns left, not below
		/// <paramref name="atLeast"/>; it still wakes at the start of its owner's turn.</summary>
		public static void AwakenSooner(Minion m, int turns, int atLeast = 0)
		{
			if (m == null || m[GameTag.DORMANT] == 0 || m[HsTag.DORMANT_TIMED] == 0) return;
			m[HsTag.DORMANT_TURNS_LEFT] = System.Math.Max(atLeast, m[HsTag.DORMANT_TURNS_LEFT] - turns);
		}

		private static Enchantment CounterOf(Minion m) =>
			m[HsTag.DORMANT_COUNTER] is int id && id > 0 ? m.AppliedEnchantments?.FirstOrDefault(e => e.Id == id) : null;

		/// <summary>HsServer: a turn-counted Dormant minion's turn (BoardZone.AwakenDormant, at the start of its owner's turn):
		/// one turn less, shown on its counter; at 0 it awakens in the same block.</summary>
		internal static void CountDormantTurn(Minion m)
		{
			Game g = m.Game;
			int left = m[HsTag.DORMANT_TURNS_LEFT] - 1;
			m[HsTag.DORMANT_TURNS_LEFT] = System.Math.Max(left, 0);
			Enchantment counter = CounterOf(m);
			bool history = g.History && counter != null;
			if (history)
			{
				PowerHistoryBlockStart start = PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, counter.Id, "", 0, 0);
				start.TriggerKeyword = (int)GameTag.TRIGGER_VISUAL;
				g.PowerHistory.Add(start);
				g.OpenSubSpell(counter, ProgressCounterFx, null);
			}
			if (counter != null)
				counter[GameTag.SCORE_VALUE_2] = System.Math.Max(0, counter[GameTag.SCORE_VALUE_1] - System.Math.Max(left, 0));
			if (history)
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.EFFECT_TIMING, Data = 0, Info = new List<int> { counter.Id } });
			if (left <= 0)
				AwakenCore(m, counter);
			if (history)
			{
				g.CloseSubSpells(counter.Id);
				g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 200, Info = new List<int> { counter.Id } });
			}
		}

		/// <summary>
		/// HsServer: a condition Dormant moves <paramref name="add"/> steps towards awakening (The Darkness: "Awaken when your
		/// opponent draws 3 Candles"); at <paramref name="needed"/> (default: its counter's total) it awakens. The count is
		/// the minion's own (HsTag.AWAKEN_PROGRESS) and shows on its counter (SCORE_VALUE_2; it only counts up).
		/// </summary>
		public static void AwakenProgress(Minion m, int add, int needed = 0)
		{
			if (m == null || m[GameTag.DORMANT] == 0 || m[HsTag.DORMANT_TIMED] > 0) return;
			Game g = m.Game;
			int n = m[HsTag.AWAKEN_PROGRESS] + add;
			m[HsTag.AWAKEN_PROGRESS] = n;
			Enchantment counter = CounterOf(m);
			if (needed <= 0) needed = counter?[GameTag.SCORE_VALUE_1] > 0 ? counter[GameTag.SCORE_VALUE_1] : Loader.DormantCards.Of(m.Card.Id)?.Total ?? 1;
			bool history = g.History && counter != null;
			if (history) CounterBlock(g, counter, 0);
			if (counter != null) counter[GameTag.SCORE_VALUE_2] = n;
			if (history) EndCounterBlock(g, counter);
			if (n >= needed) Awaken(m);
		}

		/// <summary>HsServer: a condition Dormant awakens (its condition is met): its counter's second block (live: The
		/// Darkness's LOOT_526e index 1). Wiki Dormant: "If a Dormant minion with an on-awaken effect is made Dormant again
		/// (e.g. by Maiev Shadowsong), its on-awaken effect will trigger again after the second awakening."</summary>
		public static void Awaken(Minion m)
		{
			if (m == null || m[GameTag.DORMANT] == 0) return;
			Game g = m.Game;
			Enchantment counter = CounterOf(m);
			bool history = g.History && counter != null;
			if (history) CounterBlock(g, counter, 1);
			AwakenCore(m, counter);
			if (history) EndCounterBlock(g, counter);
		}

		private static void CounterBlock(Game g, Enchantment counter, int index)
		{
			PowerHistoryBlockStart start = PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, counter.Id, "", index, 0);
			start.TriggerKeyword = (int)GameTag.TRIGGER_VISUAL;
			g.PowerHistory.Add(start);
		}

		private static void EndCounterBlock(Game g, Enchantment counter)
		{
			g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 200, Info = new List<int> { counter.Id } });
		}

		private static void AwakenCore(Minion m, Enchantment counter)
		{
			Game g = m.Game;
			// the card's own awaken effect, when live recorded one on its counter (Slagmaw: WWFX_SlagmawtheSlumbering_Awaken_Super)
			string fx = counter != null ? Loader.SubSpells.FindNamed(counter.Card.Id, "Awaken") : null;
			bool open = fx != null && g.OpenSubSpell(counter, fx, null);
			m[GameTag.UNTOUCHABLE] = 0;
			m[GameTag.DORMANT] = 0;
			m[HAS_AWAKENED] = 1;
			m[DORMANT_AWAKENED_THIS_TURN] = 1;
			WakeStats(m);
			counter?.Remove();
			foreach (GameTag k in SleepingKeywords)
			{
				var slept = (GameTag)(SleptKeywordBase + (int)k);
				if (m[slept] > 0)
				{
					m[k] = m[slept] - 1;
					m[slept] = 0;
				}
			}
			if (m.Card[STARTS_DORMANT] > 0) m[STARTS_DORMANT] = 1;
			m[HsTag.DORMANT_TIMED] = 0;
			m[HsTag.DORMANT_TURNS_LEFT] = 0;
			m[HsTag.AWAKEN_PROGRESS] = 0;
			m[HsTag.DORMANT_COUNTER] = 0;
			if (m.Zone?.Type == Zone.PLAY)
			{
				m.Controller.BoardZone.RecountUntouchables();
				BoardZone.InitialiseMinion(m);
			}
			if (m.Card.Power?.AwakenTask is Tasks.ISimpleTask awaken)
			{
				// "When this awakens, …": the minion's own TRIGGER block (Loader.DormantCards: Slagmaw 3, Crow Dreadseed 1)
				if (g.History)
				{
					PowerHistoryBlockStart start = PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, m.Id, "", Loader.DormantCards.Of(m.Card.Id)?.AwakenIndex ?? 1, 0);
					start.ShowInHistory = true;
					g.PowerHistory.Add(start);
					g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.EFFECT_TIMING, Data = 0, Info = new List<int> { m.Id } });
				}
				// its own event: an awakening inside a running effect (Magtheridon's last Warder dying) must not empty that
				// effect's queue (guide §4: nest processing in StartEvent / EndEvent)
				g.TaskQueue.StartEvent();
				awaken.Process(g, m.Controller, m, null);
				g.ProcessTasks();
				g.TaskQueue.EndEvent();
				if (g.History) g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			}
			if (open) g.CloseSubSpells(counter.Id);
			// the counter's own completion effect, after the minion woke, still in the counter's block (live Flightmaster
			// Dungar, live-20261006-dungar-student: the flightpath's heal follows Dungar's awaken block): source = the
			// minion, target = the counter
			if (counter?.Card.Power?.AwakenTask is Tasks.ISimpleTask done)
			{
				g.TaskQueue.StartEvent();
				done.Process(g, m.Controller, m, counter);
				g.ProcessTasks();
				g.TaskQueue.EndEvent();
			}
		}

		/// <summary>
		/// HsServer (2026-11-16): a Dormant minion shows its printed Attack and Health; its buffs stay and come back when it awakens.
		/// Live (session-20261022-020357 96500, Warden Maiev): +3/+3 makes Wailing Vapor 4/6, then right after DORMANT / UNTOUCHABLE
		/// ATK 1 / HEALTH 3; on awakening, after DORMANT 0, ATK 4 / HEALTH 6 again. Wiki Dormant: "do not reset nor effect
		/// enchantments when awakened"; patch note: "Fixed a visual bug where transforming a Dormant minion could make it appear to
		/// retain buffs while Dormant".
		/// </summary>
		private const int SleptStatBase = 1_000_000; // (stored + this: a lowered stat stays above 0)
		private static void SleepStats(Minion m)
		{
			// (only what enchantments add: a Starship's stats from its pieces stay — live session-20261006-235106 247688, a Dormant
			// Battlecruiser copy shows 6/5)
			EnchantmentStatAdds(m, out int atk, out int health);
			if (atk == 0 && health == 0) return;
			m[HsTag.DORMANT_SLEPT_ATK] = atk + SleptStatBase;
			m[HsTag.DORMANT_SLEPT_HEALTH] = health + SleptStatBase;
			m.BaseHealth = m.Card.Health;
			m.AttackDamage = m.Card.ATK;
		}

		/// <summary>The Attack and Health the minion's enchantments add (ADD effects; their script-tag numbers too).</summary>
		private static void EnchantmentStatAdds(Minion m, out int atk, out int health)
		{
			atk = 0; health = 0;
			foreach (Enchantment e in m.AppliedEnchantments ?? new List<Enchantment>())
			{
				Enchants.Enchant en = e.Card.Power?.Enchant;
				if (en?.Effects == null) continue;
				for (int i = 0; i < en.Effects.Length; i++)
				{
					Enchants.IEffect x = en.Effects[i];
					GameTag? tag; Enchants.EffectOperator op; int value;
					if (x is Enchants.Effect fx) { tag = fx.Tag; op = fx.Operator; value = fx.Value; }
					else if (x is Enchants.IStatEffect st) { tag = st.StatTag; op = st.Operator; value = st.StatValue; }
					else continue;
					if (op != Enchants.EffectOperator.ADD) continue;
					if (en.UseScriptTag) value = i == 0 || e.ScriptTag2 < 0 ? e.ScriptTag1 : e.ScriptTag2;
					if (tag == GameTag.ATK) atk += value;
					else if (tag == GameTag.HEALTH) health += value;
				}
			}
		}

		private static void WakeStats(Minion m)
		{
			if (m[HsTag.DORMANT_SLEPT_ATK] == 0 && m[HsTag.DORMANT_SLEPT_HEALTH] == 0) return;
			int atk = m[HsTag.DORMANT_SLEPT_ATK] - SleptStatBase, health = m[HsTag.DORMANT_SLEPT_HEALTH] - SleptStatBase;
			m[HsTag.DORMANT_SLEPT_ATK] = 0;
			m[HsTag.DORMANT_SLEPT_HEALTH] = 0;
			m.BaseHealth = (m._modifiedHealth ?? m.Card.Health) + health;
			m.AttackDamage = (m._modifiedATK ?? m.Card.ATK) + atk;
		}

		/// <summary>HsServer: the Dormant state a minion leaving play loses (BoardZone.Remove: played again, it sleeps again).</summary>
		internal static void ClearDormant(Minion m)
		{
			m[GameTag.DORMANT] = 0;
			m[GameTag.UNTOUCHABLE] = 0;
			WakeStats(m);
			foreach (GameTag k in SleepingKeywords)
			{
				var slept = (GameTag)(SleptKeywordBase + (int)k);
				if (m[slept] > 0)
				{
					m[k] = m[slept] - 1;
					m[slept] = 0;
				}
			}
			if (m.Card[STARTS_DORMANT] > 0) m[STARTS_DORMANT] = 1;
			m[HsTag.DORMANT_TIMED] = 0;
			m[HsTag.DORMANT_TURNS_LEFT] = 0;
			m[HsTag.AWAKEN_PROGRESS] = 0;
			m[HsTag.DORMANT_COUNTER] = 0;
		}
	}
}
