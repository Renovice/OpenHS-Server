// HsServer addition (not upstream SabberStone): The Dalaran Heist, bosses DALA_BOSS_26..50 — the shared pieces their Hero Powers
// and boss cards need: a starting Hero Power that works from the start (passive boss powers), "after a minion attacks your hero",
// a minion that takes over a dead hero (The Rat King's Underbelly Rats), healing several characters by different amounts in one
// healing step, a deck card moved to the deck's top, and the Void (Zuramat: banished minions and summoning them back).
// Card scripts: CardSets/HsServer/DalaranHeistCardsGenHs2.cs.
using System;
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>HsServer (internal, Dalaran Heist): "When your hero dies, the eldest &lt;this minion&gt; replaces your hero" — set on the
		/// minion while it has its text (an Aura SELF: Silence takes it away). Read by <see cref="MinionTakesOverDeadHero"/>.</summary>
		public const GameTag TAKES_OVER_DEAD_HERO = (GameTag)992201;
		/// <summary>HsServer (internal, Dalaran Heist): a minion banished "to the Void" (Zuramat's To The Void): its place in the Void (1, 2, …:
		/// the order they were banished in) on the set-aside entity; 0 once it came back. <see cref="BanishToVoid"/> / <see cref="SummonFromVoid"/>.</summary>
		public const GameTag IN_THE_VOID = (GameTag)992202;
		/// <summary>HsServer (internal, Dalaran Heist): on the banished minion, the player who controlled it when it was banished (it comes back
		/// on that side).</summary>
		public const GameTag VOID_OWNER = (GameTag)992203;

		// =========================================================================================================== starting Hero Power

		/// <summary>
		/// The player's starting Hero Power works from the start: its trigger and its aura are switched on when the game begins (a boss's
		/// Passive Hero Power: Cold Water, Made of Coins, Healing Hands, Kaironomaly's turn limit…), as a Hero Power that replaces another one
		/// is from the moment it's yours (ReplaceHeroPower, PlayCard's hero cards). Once only (a second call does nothing).
		/// </summary>
		public static void ActivateStartingHeroPower(Controller c)
		{
			HeroPower power = c?.Hero?.HeroPower;
			if (power == null) return;
			if (power.ActivatedTrigger == null) power.Power?.Trigger?.Activate(power);
			if (power.OngoingEffect == null) power.Power?.Aura?.Activate(power);
		}

		// =========================================================================================================== "attacks you"

		/// <summary>"After a minion attacks you": the attack that just ended (AFTER_ATTACK; its event's target is the final defender) was a
		/// minion's attack on the hero of the attacker's opponent, and the attacker is still in play and alive (rule book Rule 5: "'negative'
		/// (damaging/destroying/hindering/misdirecting) triggers ignore mortally wounded and pending destroy characters").</summary>
		public static bool AttackedEnemyHeroAndLives(IPlayable attacker) =>
			attacker is Minion m && m.Game.CurrentEventData?.EventTarget is Hero defender && defender.Controller != m.Controller &&
			m.Zone?.Type == Zone.PLAY && !m.ToBeDestroyed && !m.IsDead;

		// =========================================================================================================== dead hero taken over

		/// <summary>
		/// A dead hero taken over by a minion (Underbelly Rat: "When your hero dies, the eldest Underbelly Rat replaces your hero"): the
		/// player's eldest minion in play with <see cref="TAKES_OVER_DEAD_HERO"/> (the first one to enter play: OrderOfPlay) leaves play —
		/// not a death — and the hero lives on with the minion's current Health (wiki boss page summary: "the eldest Underbelly Rat on the
		/// board becomes the new Rat King, maintaining its current health"). Runs with the game-over check, like a hero's resurrection
		/// (HeroResurrect): the hero's death is undone, its latch too.
		/// </summary>
		public static void MinionTakesOverDeadHero(Controller p)
		{
			Hero hero = p?.Hero;
			if (hero == null || !(hero.ToBeDestroyed || p[HsTag.HERO_DEATH_LATCHED] > 0)) return;
			Minion heir = p.BoardZone.GetSpan().ToArray()
				.Where(m => m[TAKES_OVER_DEAD_HERO] > 0 && !m.ToBeDestroyed && !m.IsDead && m.Health > 0)
				.OrderBy(m => m.OrderOfPlay).FirstOrDefault();
			if (heir == null) return;
			int health = heir.Health;
			RemoveFromGame(heir, hero);
			hero.ToBeDestroyed = false;
			hero.MortallyWounded = false;
			p[HsTag.HERO_DEATH_LATCHED] = 0;
			SetRemainingHealth(hero, Math.Max(1, health));
		}

		// =========================================================================================================== healing

		/// <summary>One healing step on several characters, each by its own amount (Healing Hands' heroic "restore all friendly minions to
		/// full Health": each one's full Health, rules E2 — Character.TakeFullHeal). As HealTask: rules DH1 — all the Healing Events first,
		/// then their triggers.</summary>
		public static void HealEach(Game g, IPlayable source, IList<ICharacter> targets, Action<ICharacter> heal)
		{
			if (targets == null || targets.Count == 0) return;
			bool batch = targets.Count > 1 && g.TaskQueue.StartBatch();
			try
			{
				foreach (ICharacter t in targets) heal(t);
			}
			catch
			{
				if (batch) g.TaskQueue.EndBatch();
				throw;
			}
			if (batch)
			{
				g.TaskQueue.StartEvent();
				g.TaskQueue.EndBatch();
				g.ProcessTasks();
				g.TaskQueue.EndEvent();
			}
		}

		// =========================================================================================================== deck

		/// <summary>"… and put it on top" for a card already in its owner's deck (Dark Tidings): it moves to the deck's top slot
		/// (DeckZone.TopCard), keeping what it has (it doesn't leave the deck: no reset), no shuffle — as Dredge moves its pick.</summary>
		public static bool MoveToTopOfDeck(IPlayable deckCard)
		{
			if (deckCard?.Zone?.Type != Zone.DECK) return false;
			Controller owner = deckCard.Controller;
			if (owner.DeckZone.TopCard == deckCard) return true;
			owner.DeckZone.Remove(deckCard);
			owner.DeckZone.Add(deckCard); // the top
			return true;
		}

		// =========================================================================================================== the Void

		/// <summary>
		/// "Banish a minion to the Void" (Zuramat's To The Void). Wiki (Zuramat the Obliterator (boss), Notes): "Banishing a minion removes it
		/// from play and does not trigger its Deathrattle. Afterwards, instead of being put in list of deaths, it is put in a unique list,
		/// consisting of only banished minions." It leaves play as "removed from the game" (RemoveFromGame) and keeps its place in the Void.
		/// </summary>
		public static bool BanishToVoid(Minion m, IEntity source)
		{
			if (m == null || m.Zone?.Type != Zone.PLAY) return false;
			Game g = m.Game;
			int owner = m.Controller.PlayerId;
			if (!RemoveFromGame(m, source)) return false;
			int next = 1 + g.IdEntityDic.Values.Select(p => p[IN_THE_VOID]).DefaultIfEmpty(0).Max();
			m[IN_THE_VOID] = next;
			m[VOID_OWNER] = owner;
			return true;
		}

		/// <summary>The minions in the Void, in the order they were banished.</summary>
		public static List<IPlayable> InTheVoid(Game g) =>
			g.IdEntityDic.Values.Where(p => p is Minion && p[IN_THE_VOID] > 0 && p.Zone?.Type != Zone.PLAY).OrderBy(p => p[IN_THE_VOID]).ToList();

		/// <summary>
		/// "Summon all minions from the Void" (Void Shift): each banished minion comes back as a new minion of its card on the side it was
		/// banished from, in banish order, and leaves the Void (Blizzard forum, "Holy COW! Zuramat the Obliterator is a beast": "minions sent
		/// to the void and returned will be removed from the 'In The Void' pool"). A full board: it stays in the Void.
		/// </summary>
		public static void SummonFromVoid(Game g, IEntity source)
		{
			foreach (IPlayable banished in InTheVoid(g))
			{
				Controller owner = banished[VOID_OWNER] == g.Player2.PlayerId ? g.Player2 : g.Player1;
				if (owner.BoardZone.IsFull) continue;
				banished[IN_THE_VOID] = 0;
				var back = (Minion)Entity.FromCard(owner, banished.Card);
				SummonBlock.Invoke(g, back, -1, source);
			}
		}
	}
}
