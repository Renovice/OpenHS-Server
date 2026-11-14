// HsServer addition (not upstream SabberStone): shared rules first needed by The Dalaran Heist bosses 51-75 (DalaranHeistCardsGenHs3.cs)
// — extra casts of a spell ("Your spells cast an additional time", Kirin Tor Elemental), who wins a Brawl ("Always wins Brawls",
// Dark Iron Bouncer), swapping a hand card with one in the deck (Marei Loom's Next...), summoning a minion from the opponent's deck
// (Queen Wagtoggle's Bribery) and a card shown to both players as it enters a hidden hand (Gallywix's Greed is Good).
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>Rule tag on a spell being played: how many more times it casts ("Your spells cast an additional time": each Kirin
		/// Tor Elemental's cast trigger adds one). Read and cleared by CastSpell (<see cref="ExtraCasts"/>). Never sent.</summary>
		public const GameTag EXTRA_SPELL_CASTS = (GameTag)993301;

		/// <summary>Rule tag on a minion (an aura on itself): it always survives a Brawl (Dark Iron Bouncer "Always wins Brawls"). Never
		/// sent.</summary>
		public const GameTag ALWAYS_WINS_BRAWLS = (GameTag)993302;

		/// <summary>
		/// "Your spells cast an additional time" (Kirin Tor Elemental, DALA_504): the played spell's whole effect again, once per
		/// <see cref="EXTRA_SPELL_CASTS"/>, after its first cast. Live (live-20261002-204752 148363, Khadgar with two Elementals:
		/// GVG_005): each Elemental's empty TRIGGER block, SLUSH_TIME 3000, then three POWER blocks of the spell in a row — the 3 s
		/// pause comes before the first cast (<see cref="ExtraCastsPause"/>), none between. A target that died or left gives way
		/// to a random valid one (as the engine's other "cast twice" rules). Only a spell its player plays (not one an effect
		/// casts), like the engine's other spell doubling.
		/// </summary>
		internal static void ExtraCasts(Controller c, Game game, Spell spell, IPlayable target, int chooseOne, int times)
		{
			IPlayable again = target;
			for (int i = 0; i < times && !c.Hero.IsDead && !c.Opponent.Hero.IsDead; i++)
			{
				List<IPlayable> handTargets = spell.HandTargets();
				if (handTargets != null)
				{
					if (again != null && !handTargets.Contains(again))
						again = handTargets.Count > 0 ? handTargets[game.Random.Next(handTargets.Count)] : null;
				}
				else if (again != null && (again[GameTag.ZONE] != (int)Zone.PLAY || again.ToBeDestroyed || !(again is ICharacter ch && spell.IsValidPlayTarget(ch))))
					again = spell.GetRandomValidTarget();
				if (target != null && again == null) return;
				if (spell.Combo && c.IsComboActive)
					spell.ActivateTask(PowerActivation.COMBO, again);
				else
					spell.ActivateTask(PowerActivation.POWER, again, chooseOne);
				game.ProcessTasks();
				OverloadBlock(c, spell, game.History);
				SecondPowerBlock(c, spell, again);
			}
		}

		/// <summary>The extra casts a played spell will get (<see cref="EXTRA_SPELL_CASTS"/>), cleared from it; with any, the live
		/// SLUSH_TIME 3000 before its first cast. 0 for a spell an effect casts.</summary>
		internal static int ExtraCastsPause(Game game, Spell spell, bool castByEffect)
		{
			int n = spell[EXTRA_SPELL_CASTS];
			if (n == 0) return 0;
			spell[EXTRA_SPELL_CASTS] = 0;
			if (castByEffect) return 0;
			if (game.History)
				game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 3000, Info = new List<int> { spell.Id } });
			return n;
		}

		/// <summary>
		/// The minion left standing by a Brawl among <paramref name="all"/>: one that "always wins Brawls" (<see cref="ALWAYS_WINS_BRAWLS"/>
		/// — Dark Iron Bouncer; <paramref name="alsoWins"/>: a Brawl card's own "this always wins", Badlands Brawler) when there is
		/// one, else a random one ("Destroy all minions except one. (chosen randomly)"). Several that always win: one of them at
		/// random (no source says otherwise). Null for none.
		/// </summary>
		public static Minion BrawlSurvivor(Game g, IList<Minion> all, Minion alsoWins = null)
		{
			if (all == null || all.Count == 0) return null;
			var winners = all.Where(m => m[ALWAYS_WINS_BRAWLS] > 0 || m == alsoWins).ToList();
			IList<Minion> pool = winners.Count > 0 ? winners : all;
			if (pool.Count > 1) g.OnRandomHappened(true);
			return pool[g.Random.Next(pool.Count)];
		}

		/// <summary>
		/// "Swap the left-most card in each player's hand with one in their deck" (Marei Loom's Next..., one player at a time,
		/// the user of it first). Live (live-20260929-183021 4300, both players): the left-most hand card goes into the deck
		/// (HIDE_ENTITY, ZONE DECK), then a card of the deck is drawn (NUM_CARDS_DRAWN_THIS_TURN counts it: a draw) and the deck
		/// is shuffled — never the card just put in. No card in hand, or none in the deck to swap with: nothing for that player.
		/// </summary>
		public static void SwapLeftmostWithDeck(Controller c, IEntity source)
		{
			if (c.HandZone.IsEmpty || c.DeckZone.IsEmpty) return;
			IPlayable left = c.HandZone[0];
			var others = c.DeckZone.GetAll().ToList();
			IPlayable drawn = others[c.Game.Random.Next(others.Count)];
			c.Game.OnRandomHappened(true);
			MoveToDeck(c, source, left);
			if (drawn.Zone?.Type == Zone.DECK) Draw(c, drawn);
		}

		/// <summary>
		/// "Summon a minion from your opponent's deck" (Queen Wagtoggle's Bribery): a random minion of the opponent's deck changes
		/// side and is summoned for <paramref name="c"/> (the same entity: a Force Play from a deck, placed by Blizzard's rule
		/// for <paramref name="source"/>). No room or no minion: nothing. The summoned minion, or null.
		/// </summary>
		public static Minion SummonFromOpponentDeck(Controller c, IEntity source)
		{
			if (c.BoardZone.IsFull) return null;
			var minions = c.Opponent.DeckZone.GetAll().OfType<Minion>().Where(m => Generic.IsMinionCard(m)).ToList();
			if (minions.Count == 0) return null;
			Minion m = minions[c.Game.Random.Next(minions.Count)];
			if (minions.Count > 1) c.Game.OnRandomHappened(true);
			if (!RemoveFromZone.Invoke(c.Opponent, m)) return null;
			GiveTo(m, c, Zone.PLAY);
			SummonBlock.Invoke(c.Game, m, -1, source);
			return m.Zone?.Type == Zone.PLAY ? m : null;
		}

		/// <summary>
		/// A card that just entered <paramref name="c"/>'s hand, shown to both players for a moment (Gallywix's Greed is Good "buys a
		/// copy": live-20260929-183021 89591 — the copy's SHOW_ENTITY in the boss's hand, TARGET on it, DISPLAYED_CREATOR, … then
		/// HIDE_ENTITY and REVEALED 0 at the end of the trigger). <see cref="HideShownCard"/> hides it again. History only.
		/// </summary>
		public static void ShowCardInHand(IPlayable card)
		{
			Game g = card?.Game;
			if (g == null || !g.History || card.Zone?.Type != Zone.HAND) return;
			card.NativeTags[GameTag.REVEALED] = 1;
			g.PowerHistory.Add(PowerHistoryBuilder.ShowEntity(card));
			g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.TARGET, Data = 0, Info = new List<int> { card.Id } });
		}

		/// <summary>Hides a card <see cref="ShowCardInHand"/> showed (live: HIDE_ENTITY, then REVEALED 0).</summary>
		public static void HideShownCard(IPlayable card)
		{
			Game g = card?.Game;
			if (g == null || !g.History || card.Zone?.Type != Zone.HAND) return;
			g.PowerHistory.Add(PowerHistoryBuilder.HideEntity(card));
			card[GameTag.REVEALED] = 0;
		}
	}
}
