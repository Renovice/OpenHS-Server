// HsServer addition (not upstream SabberStone): Discover from any pool a card defines.
using System;
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Tasks.SimpleTasks
{
	/// <summary>
	/// "Discover a ..." from a pool the card defines (other-class Secrets, a spell, a Legendary from another class...):
	/// offers up to <c>count</c> different random cards from the pool. The pick goes where <c>action</c> says (the hand by
	/// default); then an optional enchantment is put on it and an optional task runs with the pick as its target.
	/// The generic form of the Discover code card scripts used to repeat.
	/// </summary>
	public class DiscoverFromTask : SimpleTask
	{
		private readonly Func<Game, Controller, IEntity, IEnumerable<Card>> _pool;
		private readonly int _count;
		private readonly ChoiceAction _action;
		private readonly string _enchantment;
		private readonly ISimpleTask _afterPick;

		/// <param name="pool">The cards that can be offered, for this game, player and source.</param>
		/// <param name="enchantment">An enchantment card id put on the pick (e.g. a cost reduction), or null.</param>
		/// <param name="afterPick">A task run after the pick, with the pick as its target, or null.</param>
		public DiscoverFromTask(Func<Game, Controller, IEntity, IEnumerable<Card>> pool, string enchantment = null, ISimpleTask afterPick = null,
			ChoiceAction action = ChoiceAction.HAND, int count = 3)
		{
			_pool = pool;
			_enchantment = enchantment;
			_afterPick = afterPick;
			_action = action;
			_count = count;
		}

		/// <summary>A Discover from the cards of the game's format that match <paramref name="where"/> (this player's view).</summary>
		public static DiscoverFromTask Where(Func<Controller, Card, bool> where, string enchantment = null, ISimpleTask afterPick = null) =>
			new DiscoverFromTask((g, c, s) => Cards.FormatTypeCards(g.FormatType).Where(card => where(c, card)), enchantment, afterPick);

		/// <summary>
		/// A Discover with the rule's default pool (wiki Discover: "If not stated, the pool of cards consists of all eligible
		/// neutral and class cards matching the player's current hero class"): the cards matching <paramref name="where"/>
		/// that are neutral or of the player's class. For a pool the text states ("from another class", "any"), use Where.
		/// </summary>
		public static DiscoverFromTask ClassOrNeutral(Func<Controller, Card, bool> where, string enchantment = null, ISimpleTask afterPick = null,
			ChoiceAction action = ChoiceAction.HAND) =>
			new DiscoverFromTask((g, c, s) => Cards.FormatTypeCards(g.FormatType)
				.Where(card => (card.HasClass(c.HeroClass) || card.HasClass(CardClass.NEUTRAL)) && where(c, card)), enchantment, afterPick, action);

		/// <summary>HsServer (WHIZBANG): "Discover a Secret" — rule book Discover.txt: "If a secret is discovered as a class without secrets in
		/// the format, then a random secret from the class of the card itself is discovered. Otherwise, a secret from the player's current
		/// class is discovered." (Hidden Objects)</summary>
		public static DiscoverFromTask Secrets(string enchantment = null, ISimpleTask afterPick = null) =>
			new DiscoverFromTask((g, c, s) =>
			{
				var secrets = Cards.FormatTypeCards(g.FormatType).Where(card => card.IsSecret && !card.IsSigil).ToList();
				var mine = secrets.Where(card => card.HasClass(c.HeroClass)).ToList();
				if (mine.Count > 0) return mine;
				Card self = Cards.SelfOf(s);
				return secrets.Where(card => self != null && card.Class == self.Class);
			}, enchantment, afterPick);

		/// <summary>Up to <paramref name="n"/> different random cards from <paramref name="pool"/> (by card id).</summary>
		public static List<Card> PickDistinct(Game game, IEnumerable<Card> pool, int n)
		{
			var list = pool.GroupBy(x => x.Id).Select(x => x.First()).ToList();
			var picks = new List<Card>();
			while (picks.Count < n && list.Count > 0)
			{
				Card card = list[game.Random.Next(list.Count)];
				list.Remove(card);
				picks.Add(card);
			}
			return picks;
		}

		/// <summary>HsServer: a Discover whose pick stays set aside (the after-pick makes what it needs: Recruit a Veteran's new deck
		/// card) — its options are used up like live (live-20261005 Recruit a Veteran: the others, then the pick, each touched by
		/// the source and moved SETASIDE → GRAVEYARD; the pick then back to SETASIDE). Left in SETASIDE, the client kept the
		/// picked card shown big (user, Bob's Tavern 2026-11-14). Options the after-pick moved elsewhere are left alone.</summary>
		private static void StackOptionsUsedUp(Game g, IEntity source, List<int> options, IPlayable picked)
		{
			bool pickMoved = false;
			foreach (int id in options.Where(id => id != picked.Id).Append(picked.Id))
			{
				if (!g.IdEntityDic.TryGetValue(id, out IPlayable option) || option.Zone?.Type != Zone.SETASIDE) continue;
				if (option != picked) Generic.NoteAffected(option, source);
				option.Controller.GraveyardZone.Add(option.Zone.Remove(option));
				pickMoved |= option == picked;
			}
			if (pickMoved)
				picked.Controller.SetasideZone.Add(picked.Zone.Remove(picked));
		}

		public override TaskState Process(in Game game, in Controller controller, in IEntity source, in IPlayable target,
			in TaskStack stack = null)
		{
			Card self = Cards.SelfOf(source); // HsServer: never a copy of itself
			Controller discoverer = controller; // deck-gated families only with one in the deck (Cards.DeckAllows)
			List<Card> offer = PickDistinct(game, _pool(game, controller, source).Where(c => c.Id != self?.Id && Cards.DeckAllows(discoverer, c)), _count);
			if (offer.Count == 0)
				return TaskState.STOP;
			if (_action == ChoiceAction.HAND && controller.HandZone.IsFull)
				return TaskState.STOP;

			ISimpleTask after = null;
			if (_enchantment != null || _afterPick != null || _action == ChoiceAction.STACK)
			{
				string enchantment = _enchantment;
				ISimpleTask afterPick = _afterPick;
				bool usedUp = _action == ChoiceAction.STACK;
				after = new CustomTask((g, c, s, picked, st) =>
				{
					if (picked == null) return;
					List<int> options = c.Choice?.Choices?.ToList();
					if (enchantment != null)
						Generic.AddEnchantmentBlock(g, Cards.FromId(enchantment), (IPlayable)s, picked, 0, 0, 0);
					afterPick?.Process(g, c, s, picked, st);
					if (usedUp && options != null) StackOptionsUsedUp(g, s, options, picked);
				});
			}
			Generic.CreateChoiceCards.Invoke(controller, source, null, ChoiceType.GENERAL, _action, offer.ToArray(), after);
			return TaskState.COMPLETE;
		}
	}
}
