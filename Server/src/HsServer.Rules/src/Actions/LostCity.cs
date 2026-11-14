// HsServer addition (not upstream SabberStone): The Lost City of Un'Goro (LOST_CITY, set 1952) — Kindred, triggering a dead
// minion's Deathrattle. Tags: Enums/HsTag.cs 990600-990649.
using System;
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Model.Zones;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>Client INTERACTABLE_OBJECT (4089) / INTERACTABLE_OBJECT_COST (4090): a board object the player clicks and drags a
		/// hand card onto (the Underfel Rift); its charges are TAG_SCRIPT_DATA_NUM_1 (USES_CHARGES, the client's ChargesCounter).</summary>
		public const GameTag INTERACTABLE_OBJECT = (GameTag)4089, INTERACTABLE_OBJECT_COST = (GameTag)4090;

		/// <summary>The player's interactable objects' uses now: one per object with a charge left × hand card (its owner's turn, the
		/// cost in Mana — client Entity.InteractableObjectCanBeActivatedNow).</summary>
		public static IEnumerable<Tasks.PlayerTasks.PlayerTask> InteractTasks(Controller c)
		{
			if (c.Game.CurrentPlayer != c) yield break;
			foreach (Minion obj in c.BoardZone.GetSpan().ToArray())
			{
				if (obj == null || obj.Card[INTERACTABLE_OBJECT] == 0 || obj.Card.Power?.InteractTask == null) continue;
				if (obj[GameTag.TAG_SCRIPT_DATA_NUM_1] <= 0 || c.ManaToSpend < obj.Card[INTERACTABLE_OBJECT_COST]) continue;
				foreach (IPlayable card in c.HandZone.GetAll())
					yield return Tasks.PlayerTasks.InteractTask.Any(c, obj, card);
			}
		}

		/// <summary>Use <paramref name="obj"/> with <paramref name="card"/> (Power.InteractTask), spending a charge and its cost.</summary>
		public static bool UseInteractable(Controller c, Minion obj, IPlayable card)
		{
			Game g = c.Game;
			if (obj?.Card.Power?.InteractTask is not Tasks.ISimpleTask task || card?.Zone?.Type != Zone.HAND || card.Controller != c ||
			    obj[GameTag.TAG_SCRIPT_DATA_NUM_1] <= 0) return false;
			int cost = obj.Card[INTERACTABLE_OBJECT_COST];
			if (c.ManaToSpend < cost) return false;
			c.UsedMana += cost;
			obj[GameTag.TAG_SCRIPT_DATA_NUM_1] = obj[GameTag.TAG_SCRIPT_DATA_NUM_1] - 1;
			if (g.History)
			{
				var block = PowerHistoryBuilder.BlockStart(BlockType.POWER, obj.Id, "", 0, card.Id);
				block.ShowInHistory = true;
				g.PowerHistory.Add(block);
			}
			g.TaskQueue.StartEvent();
			task.Process(g, c, obj, card);
			g.ProcessTasks();
			g.TaskQueue.EndEvent();
			if (g.History) g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			g.DeathProcessingAndAuraUpdate();
			return true;
		}

		/// <summary>Is the card Temporary — or was it when it was played (Escape the Underfel, Spelunker)?</summary>
		public static bool IsTemporary(IPlayable p) => p != null && (p[HsTag.TEMPORARY] > 0 || p[HsTag.PLAYED_AS_TEMPORARY] > 0);

		/// <summary>"INFINITY" (Murozond, Unbounded; wiki Attribute: "it will set it to the maximum integer limit"; the client shows any
		/// value from 1,000,000 as infinite). Ours: a billion — infinite on the client, with room for additions without overflow.</summary>
		public const int Infinity = 1_000_000_000;

		/// <summary>
		/// The hero's resurrection (HsTag.HERO_RESURRECT_CORPSES = the cap; Husk, Eternal Reaper): a hero about to die with Corpses spends
		/// up to the cap and comes back with that much Health (its maximum at most); TIME_618e / TIME_618t leave, TIME_618e2 marks it.
		/// Wiki Husk: "will trigger through both damage as well as direct destroy effects"; 0 Corpses: it dies.
		/// </summary>
		public static void HeroResurrect(Controller p)
		{
			Hero hero = p.Hero;
			int cap = p[HsTag.HERO_RESURRECT_CORPSES];
			if (!hero.ToBeDestroyed || cap <= 0 || p.Corpses <= 0) return;
			p[HsTag.HERO_RESURRECT_CORPSES] = 0;
			int spent = Math.Min(cap, p.Corpses);
			SpendCorpses(p, hero, spent);
			hero.ToBeDestroyed = false;
			hero.MortallyWounded = false;
			p[HsTag.HERO_DEATH_LATCHED] = 0; // (its own Deathrattle brought it back: it lives on)
			hero.Damage = Math.Max(0, hero.BaseHealth - Math.Min(spent, hero.BaseHealth));
			foreach (Enchantment e in p.AppliedEnchantments?.Where(x => x.Card[GameTag.DEATHRATTLE] > 0 && x.Card[(GameTag)3051] > 0 || x.Card.Id.StartsWith("TIME_618")).ToList() ?? new List<Enchantment>())
				e.Remove();
			if (Cards.TryFromId("TIME_618e2") is Card mark) AddEnchantmentBlock(p.Game, mark, hero, hero, 0, 0, 0);
		}

		/// <summary>
		/// Damage to a minion with "excess" (Temporal Construct, Present / Future Silvermoon; live 204752 19461: a 5 into 4 Health is 4 on
		/// the minion and 1 excess — the Jormungar forum note): the minion takes up to its Health, the rest is returned. Divine Shield or
		/// Immune: no excess. Returns the excess.
		/// (WHIZBANG: the one shared "excess" hit — Workshop Mishap, Piercing Shot, Unstable Shadow Blast, Rolling Fireball too; live vfx-decks
		/// 276716: PREDAMAGE 2 / DAMAGE 2 for a 5 into 2 Health. A Divine Shield doesn't change the amount: the excess still comes —
		/// Blizzard, forum t/70422: "Divine Shield negates damage, it doesn't change the amount of incoming damage" (Generic.ExcessDamage).
		/// Immune: none (doubt).)
		/// </summary>
		public static int DamageWithExcess(IPlayable source, Minion m, int amount, bool spell)
		{
			if (m == null || m.IsDead || m.Zone?.Type != Zone.PLAY) return 0;
			int total = spell ? SpellDamageAmount(source, amount) : amount;
			bool shielded = m.IsImmune;
			int excess = shielded ? 0 : Math.Max(0, total - Math.Max(0, m.Health));
			DamageCharFunc(source, m, total - excess, false);
			return excess;
		}

		/// <summary>Client KINDRED (4028): the card's own Kindred keyword.</summary>
		public const GameTag KINDRED = (GameTag)4028;

		/// <summary>The minion types (CardRace values) a card can have.</summary>
		public static readonly Race[] MinionTypes =
		{
			Race.MURLOC, Race.DEMON, Race.MECHANICAL, Race.ELEMENTAL, Race.BEAST, Race.TOTEM, Race.PIRATE, Race.DRAGON, Race.UNDEAD,
			(Race)43, (Race)92, (Race)2,
		};

		/// <summary>A card's minion types: every type for an All-type minion, its types (dual types included: the IsRaceTag tags) else.</summary>
		public static List<Race> MinionTypesOf(Card card) => card.IsRace(Race.ALL) && card.GetRawRace() == Race.ALL
			? MinionTypes.ToList() : MinionTypes.Where(card.IsRace).ToList();

		/// <summary>Do the two cards share a minion type (minions) or a spell school (spells)?</summary>
		public static bool SharesTypeOrSchool(Card a, Card b)
		{
			if (a == null || b == null) return false;
			int school = a[(GameTag)1635];
			if (school > 0 && b[(GameTag)1635] == school) return true;
			if (a.Type != CardType.MINION || b.Type != CardType.MINION) return false;
			List<Race> mine = MinionTypesOf(a);
			return mine.Count > 0 && MinionTypesOf(b).Any(mine.Contains);
		}

		/// <summary>
		/// Kindred (wiki Kindred: "effects that trigger if the player played a card with the same minion type/spell school as it last
		/// turn"; "For dual-type minions, only one of the minion's types needs to have been played on the previous turn"): a card
		/// <paramref name="c"/> played in game turn <paramref name="turnOfPlays"/> shares a type or school with <paramref name="me"/>.
		/// Played now: the player's last turn (Turn − 2).
		/// </summary>
		public static bool KindredHolds(Controller c, Card me, int turnOfPlays)
		{
			if (c == null || me == null || turnOfPlays < 1) return false;
			foreach (PlayHistoryEntry e in c.PlayHistory)
				if (e.SourceController == c.PlayerId && e.Turn == turnOfPlays && SharesTypeOrSchool(me, e.SourceCard))
					return true;
			return false;
		}

		/// <summary>The cards the player played this game that didn't start in the deck (Mana Giant, Techysaurus).</summary>
		public static int PlayedNotFromDeck(Controller c) =>
			c.PlayHistory.Count(e => e.SourceController == c.PlayerId && e.SourceId >= c.Game.FirstGeneratedEntityId);

		/// <summary>Kindred: a card played on its owner's last turn shares a type or school with this one. HsServer (2026-10-08): "last turn" is
		/// the owner's turn before the current one on its own turn (Turn − 2), and the turn it just played during the opponent's (Turn − 1)
		/// — as the live-matched hand glow (KindredGlow); in-hand Kindred cost reductions stayed off on the opponent's turn.</summary>
		public static bool Kindred(Controller c, IEntity self) =>
			KindredHolds(c, self?.Card, c.Game.CurrentPlayer == c ? c.Game.Turn - 2 : c.Game.Turn - 1);

		/// <summary>HsServer (2026-10-08): Kindred for the effect of a card being PLAYED — wiki Kindred.txt:5 "Kindred only activates if the card is
		/// played from hand": a spell another effect casts (it isn't in play / the Secret zone while it resolves: CastSpell's castByEffect) gets
		/// no Kindred. (In-hand cost and target checks keep Generic.Kindred.)</summary>
		public static bool KindredPlayed(Controller c, IEntity self) =>
			!(self is Spell spell && spell.Zone?.Type is not (Zone.PLAY or Zone.SECRET)) && Kindred(c, self);

		/// <summary>
		/// The Kindred hand glow (live vfx-decks 280605, darkmoon 63436-64760): POWERED_UP while the card's Kindred would hold on its
		/// owner's turn of play (during the opponent's turn: the owner's turn just played), ELEMENTAL_POWERED_UP only during the
		/// owner's own turn. Only cards with their own KINDRED keyword.
		/// </summary>
		public static (bool powered, bool now) KindredGlow(IPlayable card)
		{
			if (card?.Controller == null || card.Card[KINDRED] == 0) return (false, false);
			Game g = card.Game;
			bool mine = g.CurrentPlayer == card.Controller;
			bool holds = Kindred(card.Controller, card);
			return (holds, holds && mine);
		}

		/// <summary>
		/// "Trigger its Deathrattle" (Endbringer Umbra, Story of Umbra, Call of the Grave…): only the card's OWN Deathrattle (wiki
		/// Endbringer Umbra patch 32.4.2: "Fixed a bug where Endbringer Umbra would retrigger all Deathrattles granted"). Live
		/// (204312 74419-74727): for a card not on the battlefield, SHOW_BIG_CARD of it, then a NEW set-aside copy (CREATOR = the
		/// source, REVEALED) whose TRIGGER block (DEATHRATTLE, ShowInHistory) runs the Deathrattle, SLUSH_TIME 200; a minion on the
		/// battlefield gets the same TRIGGER block itself.
		/// </summary>
		public static void TriggerDeathrattleOf(IEntity source, IPlayable card)
		{
			if (card?.Card.Power?.DeathrattleTask is not Tasks.ISimpleTask task) return;
			Game g = card.Game;
			Controller c = card.Controller;
			IPlayable runner = card;
			if (card.Zone?.Type != Zone.PLAY)
			{
				if (g.History)
					g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SHOW_BIG_CARD, Data = 0, Info = new List<int> { card.Id, 1000 } });
				runner = Entity.FromCard(c, card.Card, new EntityData { [GameTag.CREATOR] = source?.Id ?? card.Id, [GameTag.REVEALED] = 1 }, c.SetasideZone);
			}
			if (g.History)
			{
				var block = PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, runner.Id, "", 0, 0);
				block.ShowInHistory = true;
				block.TriggerKeyword = (int)GameTag.DEATHRATTLE;
				g.PowerHistory.Add(block);
			}
			g.TaskQueue.StartEvent();
			task.Process(g, c, runner, null);
			g.ProcessTasks();
			g.TaskQueue.EndEvent();
			if (g.History)
			{
				g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 200, Info = new List<int> { runner.Id } });
			}
		}
	}
}
