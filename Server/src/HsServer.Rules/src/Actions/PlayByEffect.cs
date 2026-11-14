// HsServer addition (not upstream SabberStone): a card played by an effect ("play a card discarded by Zuramat's Prison").
using System.Collections.Generic;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.SimpleTasks;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>
		/// Plays <paramref name="card"/> for <paramref name="c"/> by an effect of <paramref name="source"/> (Zuramat, Murozond,
		/// Tess…). Only a card played from the hand is "played": a card another card brings into play is summoned, cast or
		/// equipped. A minion or Location lands on the far right, and a Battlecry never triggers (wiki Battlecry: "Battlecries do
		/// not activate if the card is brought into play by another card"; Tess Greymane: "Any minions summoned by Tess
		/// Greymane will not trigger their Battlecry effects"; wiki files Zuramat's effect under Cast spell / Equip / Summon).
		/// A spell is cast (shown big, random target); a weapon is equipped. With a full board, a minion or Location is lost.
		/// </summary>
		public static void PlayByEffect(Game g, Controller c, IEntity source, Card card)
		{
			if (card == null) return;
			PlayByEffect(g, c, source, Entity.FromCard(c, card));
		}

		/// <summary>HsServer (LOST_CITY): <see cref="PlayByEffect(Game, Controller, IEntity, Card)"/> for a prepared card (The Origin
		/// Stone plays copies of the other options with their enchantments — wiki The Forbidden Sequence: "The Origin Stone preserves
		/// enhancements on unchosen cards").</summary>
		public static void PlayByEffect(Game g, Controller c, IEntity source, IPlayable prepared)
		{
			if (prepared == null) return;
			Card card = prepared.Card;
			prepared.Zone?.Remove(prepared);
			switch (card.Type)
			{
				case CardType.MINION:
				case CardType.LOCATION:
				{
					if (c.BoardZone.IsFull) return;
					var minion = (Minion)prepared;
					// It enters like any summon by an effect: its FULL_ENTITY already in PLAY at its slot, CREATOR = the card
					// that brought it (no big card first: shown before it existed on the board, the client drew a blue
					// placeholder by Zuramat and slid it to the far right).
					if (source is IPlayable creator)
						minion._data[GameTag.CREATOR] = creator.Id;
					SummonBlock.Invoke(g, minion, -1, null); // no summoner for placement: a play lands on the far right
					// then shown big at the side and in the history tile, like a spell an effect casts (user, 2026-10-01). After
					// the summon: shown before it existed on the board, the client drew it as a blue placeholder.
					if (minion.Zone?.Type == Zone.PLAY)
						ShowPlayedByEffect(g, minion);
					break;
				}
				case CardType.SPELL:
					new CastRandomSpellTask().Process(g, c, source, prepared);
					break;
				case CardType.HERO:
					PlayHero.Invoke(c, g, (Hero)prepared, null, 0);
					break;
				case CardType.WEAPON:
				{
					var weapon = (Weapon)prepared;
					ShowPlayedByEffect(g, weapon);
					weapon.Card.Power?.Aura?.Activate(weapon);
					weapon.Card.Power?.Trigger?.Activate(weapon);
					c.Hero.AddWeapon(weapon);
					g.DeathProcessingAndAuraUpdate();
					break;
				}
			}
		}

		/// <summary>
		/// "Play the top card of your deck" (Ohn'ahra): the deck card itself is played by the effect (live s20261001-195600 8895): a
		/// spell is set aside and a NEW copy is cast (CREATOR = the source); a minion / Location goes straight from the deck to the
		/// battlefield (no Battlecry: summoned), a weapon is equipped, a hero card played. Wiki Ohn'ahra: "If the player's board is
		/// full and minions or locations are in the top 3 cards of the deck, they will get drawn to the player's hand instead."
		/// </summary>
		public static void PlayFromDeck(Controller c, IEntity source, IPlayable card)
		{
			Game g = c.Game;
			if (card == null || card.Zone?.Type != Zone.DECK) return;
			switch (card)
			{
				case Minion minion: // (a Location is a Minion here)
					if (c.BoardZone.IsFull) { Draw(c, card); return; }
					c.DeckZone.Remove(minion);
					SummonBlock.Invoke(g, minion, -1, null);
					if (minion.Zone?.Type == Zone.PLAY) ShowPlayedByEffect(g, minion);
					return;
				case Spell spell:
					if (g.History) card[GameTag.REVEALED] = 1;
					c.SetasideZone.Add(c.DeckZone.Remove(spell));
					new CastRandomSpellTask().Process(g, c, source, Entity.FromCard(c, spell.Card, new EntityData { [GameTag.CREATOR] = source.Id }));
					return;
				case Weapon weapon:
					c.DeckZone.Remove(weapon);
					ShowPlayedByEffect(g, weapon);
					weapon.Card.Power?.Aura?.Activate(weapon);
					weapon.Card.Power?.Trigger?.Activate(weapon);
					c.Hero.AddWeapon(weapon);
					g.DeathProcessingAndAuraUpdate();
					return;
				case Hero hero:
					c.DeckZone.Remove(hero);
					PlayHero.Invoke(c, g, hero, null, 0);
					return;
			}
		}

		/// <summary>A card an effect plays is shown big first (as live shows a card cast by an effect).</summary>
		private static void ShowPlayedByEffect(Game g, IPlayable card)
		{
			if (!g.History) return;
			var shown = new List<int> { card.Id };
			g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SHOW_BIG_CARD, Data = 0, Info = shown });
			g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.HISTORY_TARGET, Data = 0, Info = shown });
			g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 3000, Info = shown });
		}
	}
}
