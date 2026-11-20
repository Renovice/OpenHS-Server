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
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using System;
using System.Collections.Generic;

namespace SabberStoneCore.Config
{
	/// <summary>
	/// Holds all configuration values to create a new <see cref="Game"/> instance.
	/// </summary>
	public partial class GameConfig
	{
		/// <summary>
		/// The default value for <see cref="StartPlayer"/>.
		/// -1 means the game will randomly pick one player as starting player.
		/// </summary>
		public const int START_PLAYER_DEFAULT = -1;

		/// <summary>
		/// The default name for players. The token will be replaced
		/// by the player index.
		/// </summary>
		public const string PLAYER_NAME_DEFAULT = "Player{0}";

		/// <summary>
		/// List of <see cref="Card.Id"/>s which result in unpredictable test outcomes when included
		/// in a <see cref="Controller"/>'s deck.
		/// </summary>
		public static readonly IReadOnlyCollection<string> UnPredictableCardIDs = new[] {
			"KAR_096", // Prince Malchezaar
			"CFM_637", // Patches the Pirate

			"UNG_028", // Quests
			"UNG_067",
			"UNG_116",
			"UNG_829",
			"UNG_934",
			"UNG_920",
			"UNG_940",
			"UNG_942",
			"UNG_954",
			"ULD_131",
			"ULD_155",
			"ULD_433",
			"ULD_431",
			"ULD_724",
			"ULD_326",
			"ULD_291",
			"ULD_140",
			"ULD_711",

			"LOOT_149", // Corridor Creeper (trigger on board)
			"CFM_064", // Blubber Baron (trigger on board)
		};

		/// <summary>
		/// Gets or sets the index of the starting player.
		/// This value is 1-indexed, where 1 indicates <see cref="Player1Name"/> will start.
		/// </summary>
		/// <value>The starting player index.</value>
		public int StartPlayer { get; set; } = START_PLAYER_DEFAULT;

		/// <summary>
		/// Gets or sets the name of the player with index 1.
		/// </summary>
		/// <value>The name of the player.</value>
		public string Player1Name { get; set; } = String.Format(PLAYER_NAME_DEFAULT, 1);

		/// <summary>
		/// Gets or sets the name of the player with index 2.
		/// </summary>
		/// <value>The name of the player.</value>
		public string Player2Name { get; set; } = String.Format(PLAYER_NAME_DEFAULT, 2);

		/// <summary>
		/// Gets or sets the card class of the player with index 1.
		/// </summary>
		/// <value><see cref="CardClass"/></value>
		public CardClass Player1HeroClass { get; set; } = CardClass.HUNTER;

		/// <summary>
		/// Gets or sets the card class of the player with index 2.
		/// </summary>
		/// <value><see cref="CardClass"/></value>
		public CardClass Player2HeroClass { get; set; } = CardClass.MAGE;

		/// <summary>
		/// Gets or sets the card class of the player with index 1.
		/// </summary>
		/// <value><see cref="CardClass"/></value>
		public Card Player1HeroCard { get; set; }

		/// <summary>
		/// Gets or sets the card class of the player with index 2.
		/// </summary>
		/// <value><see cref="CardClass"/></value>
		public Card Player2HeroCard { get; set; }

		/// <summary>HsServer (2026-10-09, solo adventures — Dalaran Heist): each player's Hero Power when not its hero's own (the run's
		/// chosen one, a boss's Heroic one); null: the hero card's HERO_POWER.</summary>
		public Card Player1HeroPowerCard { get; set; }
		public Card Player2HeroPowerCard { get; set; }

		/// <summary>HsServer (2026-10-09, Dalaran Heist): each hero's Health for this game (the run's HERO_HEALTH / NEXT_BOSS_HEALTH);
		/// null: the hero card's. Set as the game starts (Game.StartGame).</summary>
		public int? Player1StartHealth { get; set; }
		public int? Player2StartHealth { get; set; }

		/// <summary>HsServer (2026-10-09, Dalaran Heist): the chapter's twist (client WingDbf.PlotTwistCardId: DALA_829 The Coffers,
		/// DALA_832 The Prisons, DALA_833 The Carts, DALA_831 The Stench, DALA_830 The Finale) played for that player as the game starts
		/// (Generic.PlayPlotTwist; live: the run's player — its "Your opponent" is the boss). Null: no twist.</summary>
		public Card Player1PlotTwist { get; set; }
		public Card Player2PlotTwist { get; set; }

		/// <summary>HsServer (2026-10-09, Dalaran Heist Anomaly Mode): the run's anomaly (DALA_850 "Anomaly - Arcane" … DALA_867) played for
		/// that player once the mulligan is done, after the passives (Generic.PlayAnomaly; live: the run's player). Null: none.</summary>
		public Card Player1Anomaly { get; set; }
		public Card Player2Anomaly { get; set; }

		/// <summary>
		/// Gets or sets the format of the game.
		/// This value influences the game rules, see <see cref="FormatType"/>
		/// for more information.
		/// </summary>
		/// <value><see cref="FormatType"/></value>
		public FormatType FormatType { get; set; } = FormatType.FT_STANDARD;

		/// <summary>
		/// Gets or sets the deck of the player with index 1.</summary>
		/// <value><see cref="List{Card}"/></value>
		public List<Card> Player1Deck { get; set; } = null;

		/// <summary>HsServer: the game board (the client's Board.json id, from the GameSetup sent). Cards with a version per
		/// board (Transfer Student: Power.BoardVersion) start the game as that version.</summary>
		public int Board { get; set; }

		/// <summary>HsServer: the game has a turn timer (live: ranked / casual); solo games (live GT_VS_AI) have none and send
		/// no TIMEOUT unless a card gives one (Nozdormu: Generic.SyncTurnLimit).</summary>
		public bool TurnTimer { get; set; } = true;

		/// <summary>
		/// Gets or sets the deck of the player with index 2.</summary>
		/// <value><see cref="List{Card}"/></value>
		/// <autogeneratedoc />
		public List<Card> Player2Deck { get; set; } = null;

		/// <summary>HsServer: each player's deck sideboards — the cards picked while building the deck for a card with a
		/// sideboard (client SIDEBOARD_TYPE: E.T.C.'s band, Zilliax Deluxe 3000's modules, Commander Beatrix's minion, King of
		/// the Underbelly's Beasts): (owner card, sideboard card) pairs in the deck builder's order (Actions/Sideboards.cs).
		/// Null: none.</summary>
		public List<(Card Owner, Card Card)> Player1Sideboard { get; set; } = null;

		/// <summary>HsServer: see <see cref="Player1Sideboard"/>.</summary>
		public List<(Card Owner, Card Card)> Player2Sideboard { get; set; } = null;

		/// <summary>HsServer (2026-10-08): the player's coin skin (a cosmetic coin card id, DMF_COIN1…): its starting Coin and every Coin an
		/// effect gives it (live: the Darkmoon Coin both times). Null: The Coin (GAME_005).</summary>
		public string Player1CoinCard { get; set; } = null;

		/// <summary>HsServer: see <see cref="Player1CoinCard"/>.</summary>
		public string Player2CoinCard { get; set; } = null;

		/// <summary>HsServer (2026-11-01): does player 2, going second, get The Coin? Live games versus the AI (GT_VS_AI: practice,
		/// Innkeeper, Dalaran Heist): when the human goes first the AI starts with its 4 cards and no Coin — 144 of 144 such games in
		/// RESEARCH/live-recordings-backup/logs (STARTHANDSIZE 4, the AI's mulligan offers 4 cards); the human going second gets it
		/// (26 of 26). False for the AI opponent.</summary>
		public bool Player2Coin { get; set; } = true;

		/// <summary>
		/// Gets or sets a value indicating whether the game should autofill the
		/// decks of all players.
		/// This option can be used in conjunction with <see cref="Player1Deck"/> and <see cref="Player2Deck"/>
		/// since it will only add cards until the deck limit is reached.
		/// </summary>
		/// <value><c>true</c> if decks need to be filled; otherwise, <c>false</c>.</value>
		public bool FillDecks { get; set; } = false;

		/// <summary>
		/// Gets or sets a value indicating whether the game should PREDICTABLY autofill the
		/// decks of all players. Set this property to true for predictable test cases!
		/// This option is only applicable when <see cref="FillDecks"/> is set to true.
		/// </summary>
		/// <value><c>true</c> if decks need to be filled PREDICTABLY; otherwise, <c>false</c>.</value>
		public bool FillDecksPredictably { get; set; } = false;

		/// <summary>
		/// Unimplemented feature, the intention was to have all possible allowed cards for a draw in
		/// a pool from which it draws.
		/// </summary>
		public bool DrawPool { get; set; } = false;

		/// <summary>
		/// Gets or sets a value indicating whether the <see cref="Game"/> should shuffle
		/// all decks before starting.</summary>
		/// <value><c>true</c> if shuffling is needed; otherwise, <c>false</c>.</value>
		public bool Shuffle { get; set; } = true;

		/// <summary>
		/// Gets or sets a value indicating whether <see cref="Game"/> must split itself
		/// when random events occur. Enabling this feature will reduce performance! At the moment
		/// this feature is only prototyped with the mad bomber tests.
		/// </summary>
		/// <value><c>true</c> if splitting is necessary; otherwise, <c>false</c>.</value>
		public bool Splitting { get; set; } = false;

		/// <summary>
		/// Gets or sets a value indicating whether <see cref="Game"/> should store log entries.</summary>
		/// <value><c>true</c> if logging is required; otherwise, <c>false</c>.</value>
		public bool Logging { get; set; } = true;

		/// <summary>
		/// Gets or sets a value indicating whether <see cref="Game"/> should store POWER history
		/// entries.
		/// </summary>
		/// <value><c>true</c> if POWER history building is required; otherwise, <c>false</c>.</value>
		public bool History { get; set; } = true;

		/// <summary>
		/// Gets or sets a value indicating whether <see cref="Game"/> should skip the Mulligan phase.
		/// </summary>
		/// <value><c>true</c> if Mulligan must be skipped; otherwise, <c>false</c>.</value>
		public bool SkipMulligan { get; set; } = true;

		/// <summary>
		/// A random seed of the random number generator used for
		/// all random interactions within <see cref="Game"/>.
		/// </summary>
		public long? RandomSeed { get; set; }

		/// <summary>HsServer (2026-11-20): the seed of a game whose config gives none — null (the real server: a fresh random game each
		/// time). The test projects set it (a module initializer), so a random-deck or random-effect test plays the same way every run:
		/// a failure is a real one, reproducible, instead of a flake.</summary>
		public static long? DefaultRandomSeed { get; set; }

		/// <summary>HsServer (2026-11-20): FillDecksPredictably fills with plain minions only (no text: nothing in the filler acts). The test
		/// projects turn it on; the tools (golden, vfxcheck, tagdiff) keep the varied filler, so "draw a spell" still finds one.</summary>
		public static bool PlainPredictableFill { get; set; }

		/// <summary>
		/// Instead of shuffling deck and drawing the top card,
		/// players draw a card using random access to the deck.
		/// This allows drawing process to be fully controlled by
		/// the random seed.
		/// Automatically disables <see cref="Shuffle"/>.
		/// </summary>
		public bool DrawWithRandom { get; set; } = false;

		/// <summary>
		/// Clones this instance.
		/// </summary>
		/// <returns></returns>
		public GameConfig Clone()
		{
			return (GameConfig)MemberwiseClone();
		}

	}

	public sealed class GameConfigBuilder
	{
		private int startPlayer = GameConfig.START_PLAYER_DEFAULT;
		private string player1Name = String.Format(GameConfig.PLAYER_NAME_DEFAULT, 1);
		private string player2Name = String.Format(GameConfig.PLAYER_NAME_DEFAULT, 2);
		private CardClass player1HeroClass = CardClass.HUNTER;
		private CardClass player2HeroClass = CardClass.MAGE;
		private Card player1HeroCard;
		private Card player2HeroCard;
		private FormatType formatType = FormatType.FT_STANDARD;
		private List<Card> player1Deck = null;
		private List<Card> player2Deck = null;
		private bool fillDecks = false;
		private bool fillDecksPredictably = false;
		private bool drawPool = false;
		private bool shuffle = true;
		private bool splitting = false;
		private bool logging = true;
		private bool history = true;
		private bool skipMulligan = true;
		private long? randomSeed = null;
		private bool drawWithRandom = false;

		private GameConfigBuilder() { }

		public static GameConfigBuilder Create()
		{
			return new GameConfigBuilder()
			{
				//player1HeroClass =
			};
		}
		public GameConfigBuilder StartPlayer(int startPlayer)
		{
			this.startPlayer = startPlayer;
			return this;
		}
		public GameConfigBuilder SetPlayer1(string playerName, string deckString)
		{
			player1Name = playerName;
			Deck deck = DeckSerializer.Deserialize(deckString);
			Card heroCard = deck.GetHero();
			player1HeroCard = heroCard;
			player1HeroClass = heroCard.Class;
			player1Deck = GetCardList(deck.CardDbfIds);
			formatType = deck.Format;
			return this;
		}
		public GameConfigBuilder SetPlayer1(string playerName, Card playerHeroCard, CardClass playerHeroClass, List<Card> cards, FormatType formatType)
		{
			player1Name = playerName;
			player1HeroCard = playerHeroCard;
			player1HeroClass = playerHeroClass;
			player1Deck = cards;
			this.formatType = formatType;
			return this;
		}
		public GameConfigBuilder SetPlayer2(string playerName, string deckString)
		{
			player2Name = playerName;
			Deck deck = DeckSerializer.Deserialize(deckString);
			Card heroCard = deck.GetHero();
			player2HeroCard = heroCard;
			player2HeroClass = heroCard.Class;
			player2Deck = GetCardList(deck.CardDbfIds);
			formatType = deck.Format;

			return this;
		}
		public GameConfigBuilder SetPlayer2(string playerName, Card playerHeroCard, CardClass playerHeroClass, List<Card> cards, FormatType formatType)
		{
			player2Name = playerName;
			player2HeroCard = playerHeroCard;
			player2HeroClass = playerHeroClass;
			player2Deck = cards;
			this.formatType = formatType;
			return this;
		}
		public GameConfigBuilder FillDecks(bool fillDecks)
		{
			this.fillDecks = fillDecks;
			return this;
		}
		public GameConfigBuilder Shuffle(bool shuffle)
		{
			this.shuffle = shuffle;
			return this;
		}
		public GameConfigBuilder Logging(bool logging)
		{
			this.logging = logging;
			return this;
		}
		public GameConfigBuilder History(bool history)
		{
			this.history = history;
			return this;
		}
		public GameConfigBuilder SkipMulligan(bool skipMulligan)
		{
			this.skipMulligan = skipMulligan;
			return this;
		}
		public GameConfigBuilder RandomSeed(int randomSeed)
		{
			this.randomSeed = randomSeed;
			return this;
		}

		public GameConfigBuilder DrawWithRandom(bool drawWithRandom)
		{
			this.drawWithRandom = drawWithRandom;
			return this;
		}
		public GameConfig Build()
		{
			return new GameConfig()
			{
				StartPlayer = startPlayer,
				Player1Name = player1Name,
				Player2Name = player2Name,
				Player1HeroClass = player1HeroClass,
				Player2HeroClass = player2HeroClass,
				Player1HeroCard = player1HeroCard,
				Player2HeroCard = player2HeroCard,
				FormatType = formatType,
				Player1Deck = player1Deck,
				Player2Deck = player2Deck,
				FillDecks = fillDecks,
				FillDecksPredictably = fillDecksPredictably,
				DrawPool = drawPool,
				Shuffle = shuffle,
				Splitting = splitting,
				Logging = logging,
				History = history,
				SkipMulligan = skipMulligan,
				RandomSeed = randomSeed,
				DrawWithRandom = drawWithRandom
			};
		}
		private List<Card> GetCardList(Dictionary<int, int> cardDbfIds)
		{
			var result = new List<Card>();
			foreach (int key in cardDbfIds.Keys)
			{
				int times = cardDbfIds[key];
				Card card = Cards.FromAssetId(key);
				for(int i = 0; i < times; i++)
				{
					result.Add(card); ;
				}
			}
			return result;
		}

	}
}
