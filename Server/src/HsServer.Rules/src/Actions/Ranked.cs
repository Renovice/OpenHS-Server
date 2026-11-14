// HsServer addition (not upstream SabberStone): ranked spells (Forged in the Barrens: "(Upgrades when you have 5 Mana.)").
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		private static readonly ConcurrentDictionary<string, (int Threshold, Card Next)> _ranks = new ConcurrentDictionary<string, (int, Card)>();

		/// <summary>HsServer: client ENTITY_TAG_THRESHOLD_VALUE (2460): the count a "({0} left!) / (Ready!)" card needs.</summary>
		public const GameTag EntityThresholdValue = (GameTag)2460;

		/// <summary>HsServer: client PLAYER_TAG_THRESHOLD_TAG_ID / _VALUE (1115 / 1116): a player tag and the count it needs
		/// (Captain Galvangar: ARMOR_GAINED_THIS_GAME 15).</summary>
		public const GameTag PlayerThresholdTag = (GameTag)1115, PlayerThresholdValue = (GameTag)1116;

		/// <summary>HsServer: does the card's data give it a "({0} left!) / (Ready!)" threshold (its own count or a player tag)?</summary>
		public static bool HasThreshold(Card card) => card != null &&
			(card[EntityThresholdValue] > 0 || card[PlayerThresholdTag] > 0 || card.Power?.ThresholdValue > 0);

		/// <summary>HsServer: a "({0} left!)" card's condition met: its own count (Power.ThresholdCount, else the TAG_SCRIPT_DATA_NUM_1
		/// it kept) reached ENTITY_TAG_THRESHOLD_VALUE (Kazakusan 4), or its owner's player tag reached PLAYER_TAG_THRESHOLD_VALUE
		/// (Galvangar 15 Armor). The client shows the count down and "(Ready!)"; the card glows then (POWERED_UP).</summary>
		public static bool ThresholdMet(IPlayable card)
		{
			if (card == null) return false;
			if (card.Card[PlayerThresholdTag] > 0)
				return card.Controller != null && card.Controller[(GameTag)card.Card[PlayerThresholdTag]] >= card.Card[PlayerThresholdValue];
			int count = card.Card.Power?.ThresholdCount?.Invoke(card) ?? card[GameTag.TAG_SCRIPT_DATA_NUM_1];
			int needed = card.Card[EntityThresholdValue] > 0 ? card.Card[EntityThresholdValue] : card.Card.Power?.ThresholdValue ?? 0;
			return needed > 0 && count >= needed;
		}

		/// <summary>HsServer: a ranked spell's next rank and the Mana Crystals it needs (its text "Upgrades when you have N
		/// Mana"; the next rank is "&lt;id&gt;t", then "&lt;id&gt;t2": Chain Lightning BAR_044 → BAR_044t → BAR_044t2). (0, null)
		/// for any other card. The client data has no tag for it.</summary>
		public static (int Threshold, Card Next) RankOf(Card card) => card == null ? (0, null) : _ranks.GetOrAdd(card.Id, _ =>
		{
			Match m = card.Text == null ? Match.Empty : Regex.Match(card.Text, @"Upgrades when you have (\d+) Mana");
			if (!m.Success) return (0, null);
			string next = Regex.IsMatch(card.Id, @"t$") ? card.Id + "2" : card.Id + "t";
			return Model.Cards.TryFromId(next) is Card nextCard ? (int.Parse(m.Groups[1].Value), nextCard) : (0, null);
		});

		/// <summary>
		/// HsServer: a ranked spell takes the ranks its owner's Mana Crystals allow (one way: losing crystals doesn't lower it).
		/// Live: in hand each rank is a TRIGGER block on the card (effect index 2), TARGET itself, the CHANGE_ENTITY (practice
		/// log 2026-09-29, Chain Lightning entering the hand at 5+ Mana); in the deck it changes silently and is drawn as the new
		/// rank (live-20261005: Fury mulliganed as Rank 1, drawn as Rank 3, nothing sent in between).
		/// </summary>
		/// (2026-10-08) It goes straight to the highest rank allowed — one block, one CHANGE_ENTITY with the 1069 pulse of the FINAL rank
		/// (live 183021 79537: Flurry Rank 1 → 3 entering the hand at 10 Mana); <paramref name="effectIndex"/> 2 entering the hand, 0 when
		/// the Mana Crystals go up (live 212735 8925: Wicked Stab at 5 Mana).
		public static void RankUp(Controller c, IPlayable card, int effectIndex = 2)
		{
			if (c == null || card == null) return;
			Card final = card.Card;
			for (int guard = 0; guard < 3; guard++)
			{
				(int threshold, Card next) = RankOf(final);
				if (next == null || c.BaseMana < threshold) break;
				final = next;
			}
			if (final == card.Card) return;
			Zone zone = card.Zone?.Type ?? Zone.INVALID;
			Game g = c.Game;
			if (zone == Zone.HAND)
			{
				if (g.History)
				{
					g.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, card.Id, "", effectIndex, 0));
					g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.TARGET, Data = 0, Info = new List<int> { card.Id } });
				}
				ChangeEntityBlock(c, card, final, false);
				if (g.History)
					g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			}
			else if (zone == Zone.DECK)
			{
				g.SilentChanges = true;
				try { ChangeEntityBlock(c, card, final, false); }
				finally { g.SilentChanges = false; }
			}
		}

		/// <summary>HsServer: every ranked spell in the player's hand and deck (their Mana Crystals went up: effect index 0).</summary>
		public static void RankUpAll(Controller c)
		{
			foreach (IPlayable card in c.HandZone.GetAll().Concat(c.DeckZone.GetAll()).Where(p => RankOf(p.Card).Next != null).ToList())
				RankUp(c, card, 0);
		}
	}
}
