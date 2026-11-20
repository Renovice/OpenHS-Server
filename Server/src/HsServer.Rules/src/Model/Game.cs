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
using SabberStoneCore.Conditions;
using SabberStoneCore.Actions;
using SabberStoneCore.Config;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Model.Zones;
//using SabberStoneCore.Splits;
using SabberStoneCore.Tasks;
using SabberStoneCore.Tasks.PlayerTasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SabberStoneCore.Auras;
using SabberStoneCore.Triggers;

// TODO check if event should be removed
// TODO ... spellbender phase ??? and spell text ? wtf .. did you forget them???
// TODO [DS1_188] Gladiator's Longbow, check should be on IMMUNE_WHILE_ATTACKING
// TODO cleanup the Enchant class
// TODO refactor and cleanup SelfCondition & RelaCondition class
namespace SabberStoneCore.Model
{
	/// <summary>
	/// Signature used for passing tag update information around.
	/// </summary>
	/// <param name="sender">The sender, which is probably an entity.</param>
	/// <param name="t">The gametag which changed.</param>
	/// <param name="oldValue">The old value.</param>
	/// <param name="newValue">The new value.</param>
	public delegate void EntityChangedEventHandler(object sender, GameTag t, int oldValue, int newValue);

	/// <summary>
	/// The state machine which processes the given input and generates results which can be interpreted 
	/// to create a new set of inputs.
	/// 
	/// This is THE MOST IMPORTANT type, since it provides a simple interface for handling/processing 
	/// game data.
	/// </summary>
	/// <seealso cref="Entity" />
	public partial class Game : Entity
	{
		/// <summary>
		/// The entityID of the game itself is always 1.
		/// </summary>
		public const int GAME_ENTITYID = 1;

		/// <summary>
		/// The maximum minions that are allowed on the board.
		/// </summary>
		public const int MAX_MINIONS_ON_BOARD = 7;

		private readonly GameConfig _gameConfig;

		private Controller _currentPlayer;

		/// <summary>
		/// List of activated auras.
		/// </summary>
		public readonly List<IAura> Auras;

		public readonly List<(int entityId, IEffect effect)> OneTurnEffects;

		/// <summary>
		/// Temporal container to store Enchantment entities of One_Turn_effects.
		/// </summary>
		public readonly List<Enchantment> OneTurnEffectEnchantments;

		/// <summary>
		/// List of activated triggers that should be validated in advance.
		/// </summary>
		public readonly List<Trigger> Triggers;

		/// <summary>
		/// List of Minions that ready to be destroyed and to be removed from the BoardZone.
		/// </summary>
		public readonly List<Minion> DeadMinions = new List<Minion>();

		/// <summary>
		/// List of Minions summoned in current event.
		/// </summary>
		public readonly List<Minion> SummonedMinions = new List<Minion>();

		/// <summary>
		/// List of entity ids of Minions in the state of 'AttackableByRush'.
		/// </summary>
		public readonly List<int> RushMinions = new List<int>();

		/// <summary>
		/// List of entity ids of 'Ghostly' entities created by Echo ability.
		/// </summary>
		public readonly List<int> GhostlyCards = new List<int>();
		/// <summary>HsServer (2026-11-20): "… at the end of this turn" effects waiting for this turn's end (Generic.AtEndOfThisTurn): the source's
		/// id (it may have left play since), its player and the task. Run after the turn's end triggers, then cleared.</summary>
		public readonly List<(int Source, int Player, Tasks.ISimpleTask Task)> EndOfThisTurn = new List<(int, int, Tasks.ISimpleTask)>();

		/// <summary>
		/// Gets or sets the index value for identifying the N-th clone of a game. (0-indexed)
		/// </summary>
		/// <value>The index of the clone.</value>
		public string CloneIndex { get; set; } = "[0]";

		/// <summary>
		/// Gets or sets the index of the next clone.<seealso cref="CloneIndex"/>
		/// </summary>
		/// <value>The index of the next clone.</value>
		public int NextCloneIndex { get; set; } = 1;
		internal Util.DeepCloneableRandom Random
		{
			get => _random;
			set { _random = value; if (value != null) value.Rolled = () => OnRandomHappened(true); } // HsServer: every roll is known as random
		}
		private Util.DeepCloneableRandom _random;

		public void SetRandomSeed(long seed) => Random.SetSeed(seed);

		///// <summary>
		///// Gets or sets the list of splitted (and fully resolved) games, derived from this game.
		///// The values hold the derived game instances and split meta information.
		///// </summary>
		///// <value><see cref="SplitNode"/></value>
		//public List<SplitNode> FinalSplits { get; set; }

		///// <summary>
		///// Gets or sets the list of splitted, but unresolved, game instances.
		///// These instances are derived from the current game instance but their TaskQueue
		///// still needs to process.
		///// </summary>
		///// <value><see cref="Game"/></value>
		//public List<Game> Splits { get; set; } = new List<Game>();

		/// <summary>
		/// Occurs when a TAG on an entity, which is hooked onto this game, changes.
		/// </summary>
		public event EntityChangedEventHandler EntityChangedEvent;

		/// <summary>Occurs when a random event was processed by this game.</summary>
		public event EventHandler<bool> RandomHappenedEvent;

		/// <summary>
		/// Gets the game event manager. This object delegates the state machine of
		/// the game.
		/// </summary>
		/// <value>The game event manager.</value>
		public GameEventManager GamesEventManager { get; }

		/// <summary>
		/// Holds all the controllers (== players) which are attached to this game.
		/// </summary>
		private readonly Controller[] _players = new Controller[2];

		/// <summary>
		/// Player with the first turn, alias Player 1.
		/// </summary>
		/// <value><see cref="Controller"/></value>
		public Controller Player1
		{
			get => _players[0];
			protected set => _players[0] = value;
		}

		/// <summary>
		/// Player starting at the second turn, alias Player 2.
		/// </summary>
		/// <value><see cref="Controller"/></value>
		public Controller Player2
		{
			get => _players[1];
			protected set => _players[1] = value;
		}

		/// <summary>
		/// Gets the format type of this game.
		/// </summary>
		/// <value><see cref="FormatType"/></value>
		public FormatType FormatType => _gameConfig.FormatType;

		/// <summary>HsServer: the game board (Board.json id; GameConfig.Board).</summary>
		public int Board => _gameConfig.Board;

		/// <summary>HsServer: the game has a turn timer (GameConfig.TurnTimer).</summary>
		public bool TurnTimer => _gameConfig.TurnTimer;

		///// <summary>Gets a value indicating whether this <see cref="Game"/> is intended to split.
		///// When TRUE, the game WILL SPLIT ITSELF when <see cref="Splits"/> contains games derived
		///// from this one with different random outcomes.
		///// </summary>
		///// <value><c>true</c> if splitting is intended; otherwise, <c>false</c>.</value>
		//public bool Splitting => _gameConfig.Splitting;

		/// <summary>Gets a value indicating whether this <see cref="Game"/> records debug logs.
		/// When TRUE, detailed information each process step will be saved in <see cref="Logs"/>
		/// in a form of <see cref="LogEntry"/>.
		/// </summary>
		public bool Logging => _gameConfig.Logging;

		/// <summary>Gets or sets the power history container. 
		/// This object facilitates building POWER blocks to send to the hearthstone client.
		/// </summary>
		/// <value><see cref="Kettle.PowerHistory"/></value>
		public PowerHistory PowerHistory { get; }

		/// <summary>Gets a value indicating whether this <see cref="Game"/> has Power History 
		/// building enabled.
		/// </summary>
		/// <value><c>true</c> if history building is enabled; otherwise, <c>false</c>.</value>
		public bool History => _gameConfig.History;

		/// <summary>HsServer: a change the client mustn't see (a ranked spell upgrading inside the deck): ChangeEntityBlock
		/// records no 1069 pulse while it is set. (2026-10-09) Its CHANGE_ENTITY is still recorded: the stream shows a hidden card's
		/// change to nobody, and a card revealed later must be shown as it is now — a deck card Cera'thine Fleetrunner (AV_403) or a
		/// rank-up had changed reached the client under its OLD card when it was burned / milled (soak client view: BAR_042 shown for
		/// CORE_YOP_034).</summary>
		internal bool SilentChanges;

		/// <summary>
		/// Gets the task queue.
		/// </summary>
		/// <value>The task queue.</value>
		/// <autogeneratedoc />
		public readonly TaskQueue TaskQueue;

		public readonly TriggerManager TriggerManager = new TriggerManager();

		internal EventMetaData CurrentEventData { get; set; }

		private int _idIndex = 4;
		/// <summary>Gets the next entity identifier.</summary>
		/// <value>The next entity id.</value>
		public int NextId => _idIndex++;

		/// <summary>HsServer: entities with this id or higher were created after the decks were built (Steamcleaner).</summary>
		public int FirstGeneratedEntityId { get; private set; }

		private int _oopIndex;
		/// <summary>Gets the next order of play index.</summary>
		/// <value>The next oop index.</value>
		public int NextOop => _oopIndex++;

		/// <summary>HsServer: the order-of-play counter right now (timestamps a cost enchantment when it is applied).</summary>
		internal int PeekOop => _oopIndex;

		/// <summary>HsServer: the order of play of the aura whose effects are being applied/removed (int.MaxValue: none),
		/// so a cost aura's contribution is ordered by its source (rules M6).</summary>
		internal int AuraOrder = int.MaxValue;

		/// <summary>Ovewrites the internal index (multiple) values.
		/// This method is relevant when cloning games.
		/// </summary>
		/// <param name="id">The nex entity ID.</param>
		/// <param name="oop">The next OOP index.</param>
		public void SetIndexer(int id, int oop)
		{
			_idIndex = id;
			_oopIndex = oop;
		}

		///// <summary>
		///// Gets the dictionary containing all generated entities for this game.
		///// </summary>
		///// <value><see cref="IPlayable"/></value>
		public EntityList IdEntityDic { get; private set; }

		/// <summary>
		/// Gets the dictionary containing all generated choice sets for this game.
		/// </summary>
		/// <value><see cref="PowerEntityChoices"/></value>
		public Dictionary<int, PowerEntityChoices> EntityChoicesMap { get; }

		/// <summary>
		/// Gets all the dictionary containing all generated option sets for this game.
		/// </summary>
		/// <value><see cref="PowerAllOptions"/></value>
		public Dictionary<int, PowerAllOptions> AllOptionsMap { get; }

		/// <summary>
		/// Gets or sets the collection of log entries.
		/// </summary>
		/// <value><see cref="LogEntry"/></value>
		public Queue<LogEntry> Logs { get; }

		/// <summary>Initializes a new instance of the <see cref="Game"/> class.</summary>
		/// <param name="gameConfig">The game configuration.</param>
		/// <param name="setupHeroes"></param>
		public Game(GameConfig gameConfig, bool setupHeroes = true)
			: base(null, Card.CardGame, new EntityData
			{
				[GameTag.ENTITY_ID] = GAME_ENTITYID,
				[GameTag.ZONE] = (int)Enums.Zone.PLAY,
				[GameTag.CARDTYPE] = (int)CardType.GAME
			})
		{
			// HsServer (2026-11-20): no seed given: GameConfig.DefaultRandomSeed (the test projects set one, so a test is the same every run)
			long? seed = gameConfig.RandomSeed ?? GameConfig.DefaultRandomSeed;
			Random = seed is null ?
				new Util.DeepCloneableRandom() :
				new Util.DeepCloneableRandom(seed.Value);
			IdEntityDic = new EntityList(75);
			_gameConfig = gameConfig;
			Game = this;
			Auras = new List<IAura>();
			Triggers = new List<Trigger>();
			GamesEventManager = new GameEventManager(this);

			bool history = gameConfig.History;
			if (gameConfig.Logging)
			{
				_logging = true;
				Logs = new Queue<LogEntry>();
			}

			if (history)
			{
				_history = true;
				EntityChoicesMap = new Dictionary<int, PowerEntityChoices>();
				AllOptionsMap = new Dictionary<int, PowerAllOptions>();
				PowerHistory = new PowerHistory { InPlay = InPlayNow };
				PowerHistory.BeforeAdd = BeforeHistoryAdd;
				PowerHistory.Reopened = b => RecordedFxAt(b, markers: false); // (a pick resolving in the card's block: its effects wait again)
				PowerHistory.ReopenedEnds = EndRecordedFx;
			}

			EntityData p1Dict = history
				? new EntityData(64)
				{
					//[GameTag.HERO_ENTITY] = heroId,
					[GameTag.MAXHANDSIZE] = Controller.MaxHandSize,
					[GameTag.STARTHANDSIZE] = 4,
					[GameTag.PLAYER_ID] = 1,
					[GameTag.TEAM_ID] = 1,
					[GameTag.ZONE] = (int) SabberStoneCore.Enums.Zone.PLAY,
					[GameTag.CONTROLLER] = 1,
					[GameTag.MAXRESOURCES] = Controller.MaxResources,
					[GameTag.CARDTYPE] = (int) CardType.PLAYER
				}
				: new EntityData(64)
				{
					{GameTag.MAXRESOURCES, 10},
					{GameTag.MAXHANDSIZE, 10}
				};
			EntityData p2Dict = history
				? new EntityData(64)
				{
					//[GameTag.HERO_ENTITY] = heroId,
					[GameTag.MAXHANDSIZE] = 10,
					[GameTag.STARTHANDSIZE] = 4,
					[GameTag.PLAYER_ID] = 2,
					[GameTag.TEAM_ID] = 2,
					[GameTag.ZONE] = (int) SabberStoneCore.Enums.Zone.PLAY,
					[GameTag.CONTROLLER] = 2,
					[GameTag.MAXRESOURCES] = 10,
					[GameTag.CARDTYPE] = (int) CardType.PLAYER
				}
				: new EntityData(64)
				{
					{GameTag.MAXRESOURCES, 10},
					{GameTag.MAXHANDSIZE, 10}
				};
			// HsServer: the Excavate tier cap is a player tag from the start (live CREATE_GAME: MAX_EXCAVATE_TIER)
			p1Dict[Actions.Generic.MAX_EXCAVATE_TIER] = Actions.Generic.MaxExcavateTier(gameConfig.Player1HeroClass);
			p2Dict[Actions.Generic.MAX_EXCAVATE_TIER] = Actions.Generic.MaxExcavateTier(gameConfig.Player2HeroClass);
			_players[0] = new Controller(this, gameConfig.Player1Name, 1, 2, p1Dict);
			_players[1] = new Controller(this, gameConfig.Player2Name, 2, 3, p2Dict);

			// HsServer (2026-10-09): the scenario's own game and player tags, in CREATE_GAME (Actions/DalaranHeistGroup5.cs)
			Actions.Generic.ScenarioTagsAtCreation(this);

			// add power history create game
			if (history) PowerHistory.Add(PowerHistoryBuilder.CreateGame(this, _players));

			if (setupHeroes)
			{
				// HsServer (2026-10-08): a deck with a "start the game as a different class" card (Maestra) builds its hero disguised — live
				// (vfx-decks 85210): the disguise hero and its Hero Power are the ones CREATE_GAME shows; the real class stays the deck's
				for (int i = 0; i < 2; i++)
				{
					Card real = (i == 0 ? gameConfig.Player1HeroCard : gameConfig.Player2HeroCard) ?? Cards.HeroCard(i == 0 ? gameConfig.Player1HeroClass : gameConfig.Player2HeroClass);
					Card disguise = Actions.Generic.DisguiseHero(this, i == 0 ? gameConfig.Player1Deck : gameConfig.Player2Deck, real);
					_players[i].AddHeroAndPower(disguise ?? real, i == 0 ? gameConfig.Player1HeroPowerCard : gameConfig.Player2HeroPowerCard,
						Actions.Generic.ScenarioHeroTags(this, i)); // HsServer (2026-10-09): the scenario's hero tags (Bob's Tavern)
					_players[i].BaseClass = real.Class;
					if (disguise != null) _players[i][HsTag.DISGUISED_REAL_HERO] = real.AssetId;
				}
			}

			TaskQueue = new TaskQueue(this);

			OneTurnEffects = new List<(int, IEffect)>();
			OneTurnEffectEnchantments = new List<Enchantment>();

			if (!gameConfig.Shuffle && !gameConfig.DrawWithRandom)
			{
				gameConfig.Player1Deck?.Reverse();
				gameConfig.Player2Deck?.Reverse();
			}

			// HsServer: the decks' sideboards (Actions/Sideboards.cs); a deck card built from its sideboard (Zilliax Deluxe
			// 3000's modules) is in the deck as built
			Player1.Sideboards = Generic.SideboardsOf(gameConfig.Player1Sideboard);
			Player2.Sideboards = Generic.SideboardsOf(gameConfig.Player2Sideboard);
			Player1.CoinCard = Generic.CoinSkin(gameConfig.Player1CoinCard); // HsServer (2026-10-08): the players' coin skins
			Player2.CoinCard = Generic.CoinSkin(gameConfig.Player2CoinCard);

			// setting up the decks ...
			gameConfig.Player1Deck?.ForEach(card =>
			{
				Card p = Generic.AsBuilt(Player1, card);
				// HsServer (Dalaran Heist): a Dungeon passive in the deck list starts set aside, not in the deck (Generic.StartingZoneOf)
				bool passive = Generic.IsDungeonPassive(p);
				if (!passive) Player1.DeckCards.Add(p);
				FromCard(Player1, Cards.ForBoard(p, gameConfig.Board), passive ? new EntityData { [GameTag.CREATOR] = Id } : null,
					Generic.StartingZoneOf(Player1, p)); // HsServer: its board's version
			});
			Generic.CreateBandEntities(Player1); // HsServer: live, right after the player's deck (live-20261007-zilliax-etc-remix)
			gameConfig.Player2Deck?.ForEach(card =>
			{
				Card p = Generic.AsBuilt(Player2, card);
				// HsServer (Dalaran Heist): a Dungeon passive in the deck list starts set aside, not in the deck (Generic.StartingZoneOf)
				bool passive = Generic.IsDungeonPassive(p);
				if (!passive) Player2.DeckCards.Add(p);
				FromCard(Player2, Cards.ForBoard(p, gameConfig.Board), passive ? new EntityData { [GameTag.CREATOR] = Id } : null,
					Generic.StartingZoneOf(Player2, p)); // HsServer: its board's version
			});
			Generic.CreateBandEntities(Player2);
			if (gameConfig.FillDecks)
			{
				Player1.DeckZone.Fill(_gameConfig.FillDecksPredictably ? GameConfig.UnPredictableCardIDs : null);
				Player2.DeckZone.Fill(_gameConfig.FillDecksPredictably ? GameConfig.UnPredictableCardIDs : null);
			}
			Actions.Generic.DisguiseHolders(this); // HsServer (2026-10-08): the disguise's revealer on each disguised player (Maestra)
			Actions.Generic.AssignRunDeckSlots(this); // HsServer (2026-10-09, Dalaran Heist): each run-deck card's slot (tag 1286)
			FirstGeneratedEntityId = _idIndex; // HsServer: every entity from here on did not start in a deck
			Actions.Generic.RecordStartingDecks(this); // HsServer (WILD_WEST): "If your deck started with no duplicates"

			if (gameConfig.DrawWithRandom)
			{
				gameConfig.Shuffle = false;
				_players[0].DeckZone.DrawWithRandom = true;
				_players[1].DeckZone.DrawWithRandom = true;
			}
		}

		/// <summary> A copy constructor. </summary>
		private Game(Game game, bool logging, bool resetRandomSeed, bool history) : base(null, game)
		{
			//IdEntityDic = new Dictionary<int, IPlayable>(game.IdEntityDic.Count);
			IdEntityDic = new EntityList(game.IdEntityDic.Count);
			Game = this;

			if (logging)
			{
				_logging = true;
				Logs = new Queue<LogEntry>();	// Logs are not cloned.
			}

			if (history)
			{
				_history = true;
				PowerHistory = new PowerHistory { InPlay = InPlayNow };
				PowerHistory.BeforeAdd = BeforeHistoryAdd;
				PowerHistory.Reopened = b => RecordedFxAt(b, markers: false); // (a pick resolving in the card's block: its effects wait again)
				PowerHistory.ReopenedEnds = EndRecordedFx;
				EntityChoicesMap = new Dictionary<int, PowerEntityChoices>();
				AllOptionsMap = new Dictionary<int, PowerAllOptions>();
			}

			Auras = new List<IAura>(game.Auras.Count);
			Triggers = new List<Trigger>(game.Triggers.Count);
			OneTurnEffects = new List<(int entityId, IEffect effect)>(game.OneTurnEffects);
			OneTurnEffectEnchantments = new List<Enchantment>(game.OneTurnEffectEnchantments.Count);
			RushMinions.AddRange(game.RushMinions);
			GhostlyCards.AddRange(game.GhostlyCards);
			EndOfThisTurn.AddRange(game.EndOfThisTurn);
			
			GamesEventManager = new GameEventManager(this);

			_gameConfig = game._gameConfig.Clone();
			_gameConfig.Logging = logging;
			_gameConfig.History = history;

			CloneIndex = game.CloneIndex + $"[{game.NextCloneIndex++}]";
			RunDeckEditList = new List<RunDeckEdit>(game.RunDeckEditList); // HsServer (2026-10-09): the run-deck edits so far

			Random = resetRandomSeed ? new Util.DeepCloneableRandom() : game.Random.Clone();

			Player1 = game.Player1.Clone(this);
			Player2 = game.Player2.Clone(this);
			if (game._currentPlayer != null)
				CurrentPlayer = game.CurrentPlayer.Id == 2 ? _players[0] : _players[1];

			// Clone auras lastly
			foreach (IAura aura in game.Auras)
			{
				aura.Clone(IdEntityDic[aura.Owner.Id]);
			}

			TaskQueue = new TaskQueue(this);

			SetIndexer(game._idIndex, game._oopIndex);
			FirstGeneratedEntityId = game.FirstGeneratedEntityId;
		}

		/// <summary>Method which is called when an entity wants to notify that one of it's tags changed value.</summary>
		/// <param name="entity">The entity which has changed.</param>
		/// <param name="t">The game tag which value changed.</param>
		/// <param name="oldValue">The old value.</param>
		/// <param name="newValue">The new value.</param>
		protected internal virtual void OnEntityChanged(Entity entity, GameTag t, int oldValue, int newValue)
		{
			EntityChangedEvent?.Invoke(entity, t, oldValue, newValue);
		}

		/// <summary>Call this method to invode the <see cref="RandomHappenedEvent"/>.</summary>
		/// <param name="isHappened">TODO</param>
		protected internal virtual void OnRandomHappened(bool isHappened)
		{
			RandomHappenedEvent?.Invoke(this, isHappened);
		}

		/// <summary>
		/// Gets the controller for the provided entityID.
		/// </summary>
		/// <param name="entityID">The entityID.</param>
		/// <returns></returns>
		public Controller ControllerById(int entityID)
		{
			//return _players.First(p => p.Id == entityID);
			return entityID == 2 ? _players[0] : _players[1];
		}

		/// <summary>Process the specified task.
		/// The game will execute the desired task and all effects coupled either
		/// directly or indirectly in synchronous manner.
		/// 
		/// Call <see cref="Controller.Options(Boolean)"/> on the <see cref="CurrentPlayer"/> 
		/// instance for tasks which are accepted as arguments.
		/// After this method returns, check <see cref="Controller.Options(Boolean)"/>
		/// again until only <see cref="EndTurnTask"/> remains, which will
		/// start the turn of <see cref="CurrentOpponent"/>. 
		/// </summary>
		/// <param name="gameTask">The game task to execute.</param>
		/// <summary>HsServer: tasks processed for the current move; a runaway effect (an endless trigger chain) is stopped with
		/// a TaskException naming the task instead of hanging the server.</summary>
		internal int TasksThisMove;
		/// <summary>HsServer (CATACLYSM): the card whose cost is being paid right now (PlayCard.PayPhase), else 0.</summary>
		internal int PayingFor;

		/// <summary>HsServer: counts player moves (with TasksThisMove it names "the same effect").</summary>
		internal int MoveNumber;

		/// <summary>HsServer (2026-10-10): the effect running now — a number given to each task when it starts (TaskQueue.Process /
		/// Execute) and given back when it ends, so a trigger that runs in the middle of an effect (Knife Juggler after the first of
		/// Onyxia's Whelps) has its own and the effect goes on with its own afterwards. With MoveNumber it names "the same effect"
		/// for the summon placement (Generic.SummonPosition); TasksThisMove counts every task, so it changed under such a trigger.</summary>
		internal int EffectSerial;
		internal int EffectCounter;

		/// <summary>HsServer: the last summon (summoner, minion, move, effect), so one effect's summons line up (SummonPosition).</summary>
		internal (int Source, Minion Minion, int Move, int Effect) LastSummon;
		/// <summary>HsServer: a living minion's summons in one effect (summoner, effect) so far: how many, and how the effect places
		/// them (decided at its first summon: Generic.SummonPosition). One per running effect, so a summon nested in another's
		/// (a Colossal's appendages, a "when summoned" summon) doesn't reset the outer effect's count. Emptied at each new move.</summary>
		internal readonly Dictionary<(int Source, int Effect), (int Count, Generic.SummonLayout Layout)> SummonRuns = new();
		internal int SummonRunsMove = -1;

		/// <summary>HsServer: how many minions the current effect of a summoner puts on the board at once (SummonTask's amount,
		/// Generic.BeginSummonGroup), and a layout its script asks for (null: the shared rule decides — Generic.SummonPosition).</summary>
		internal (int Source, int Move, int Effect, int Total, Generic.SummonLayout? Layout) SummonGroup;

		/// <summary>HsServer: a damage step hitting a group is running (DamageTask): no missile per character
		/// (Generic.MissilePerHit).</summary>
		internal bool AreaDamage;

		/// <summary>HsServer: the proposed attacker an effect took out of the fight (Generic.ExitCombat: SHOULDEXITCOMBAT 1);
		/// the attack's end sets it back to 0.</summary>
		internal int ExitingCombat;
		/// <summary>HsServer (VIOLET_HOLD): the attacker of the attack in its preparation (also without history: AI clones).</summary>
		internal int AttackingSource;
		/// <summary>HsServer (VIOLET_HOLD): a card with "After this gains stats" gained stats (Generic.AfterStatGains).</summary>
		internal bool StatGainsPending;
		/// <summary>HsServer (VIOLET_HOLD): "End your turn" (Slice and Dice): the current player's turn ends once this move is done.</summary>
		public bool EndTurnPending;
		/// <summary>HsServer (Rewind): the adapter can rewind this game (the live game; a clone never can): Generic.OfferRewind.</summary>
		public bool RewindEnabled;
		/// <summary>HsServer (Rewind): the card whose Rewind Timeline was picked (0: none); the adapter rewinds and replays it.</summary>
		public int PendingRewind;
		/// <summary>HsServer (Rewind): a rewound game goes on with the live game's random stream (a replay rolls anew).</summary>
		public void ContinueRandomFrom(Game other) => Random = other.Random.Clone();
		/// <summary>HsServer (Rewind): Morchie's extra run of a Rewind card ("Your Rewinds keep BOTH potential outcomes") is going on: the
		/// card's "after you Rewind" outcome (Sands of Time: your class).</summary>
		public bool RewindSecondOutcome;
		/// <summary>HsServer (2026-10-07): while another card's Deathrattle runs from a minion that captured it
		/// (ActivateCapturedDeathrattleTask: Vectus' whelps, Unearthed Raptor), that card — its Entourage is the one a
		/// RandomEntourageTask in it picks from (Arfus' Death Knight cards), not the running minion's (it has none).</summary>
		internal Card CapturedEffectCard;
		/// <summary>HsServer: the effect (move, task) that last read the dead (graveyard, "died this game"): its summons are
		/// resurrects, which line up to the right of the summoner (user rule 2026-10-04).</summary>
		internal (int Move, int Task) DeadPoolRead = (-1, -1);
		/// <summary>HsServer: the trigger whose task is running (null for plays, Battlecries...): CopyTask uses it to tell a
		/// return from the minion's own death (Redemption: its slot) from a later resurrect (Reincarnate: far right).</summary>
		internal Triggers.Trigger RunningTrigger;
		internal const int MaxTasksPerMove = 100_000;

		/// <summary>HsServer: diagnostics - set by a watchdog to make a stuck engine throw at its next task (naming it).</summary>
		public static volatile bool AbortRequested;

		/// <summary>HsServer: diagnostics - the last task any game started, and its source (a watchdog prints them on a hang).</summary>
		public static volatile object LastTask, LastTaskSource;

		public bool Process(PlayerTask gameTask)
		{
			//// start with no splits ...
			//Splits = new List<Game>();

			Log(LogLevel.INFO, BlockType.PLAY, "Game", !Logging ? "" : gameTask.FullPrint());

			// clear last power history
			PowerHistory?.Last.Clear();
			int moveStart = PowerHistory?.Full.Count ?? 0;
			_moveHistoryStart = moveStart;
			TasksThisMove = 0; // HsServer: runaway guard (TaskQueue.Process)
			TaskQueue.ResetBatches(); // HsServer: a move that threw mid-effect can't leave a batch/gathering open
			MoveNumber++;

			// make sure that we only use task for this game ...
			if (gameTask.Game != this)
			{
				gameTask.Game = this;
				gameTask.Controller = ControllerById(gameTask.Controller.Id);
				if (gameTask.HasSource)
					gameTask.Source = IdEntityDic[gameTask.Source.Id];
				if (gameTask.HasTarget)
					gameTask.Target = (ICharacter) IdEntityDic[gameTask.Target.Id];
			}
			bool result = gameTask.Process();
			// HsServer (2026-10-09): the turn step a start-of-turn pick held back (MainStartTriggers) goes on once every pick is made
			if (this[HsTag.STEP_AFTER_CHOICE] > 0 && CurrentPlayer.Choice == null && State == State.RUNNING)
			{
				var held = (Step)this[HsTag.STEP_AFTER_CHOICE];
				this[HsTag.STEP_AFTER_CHOICE] = 0;
				NextStep = held;
			}
			// HsServer (VIOLET_HOLD): "End your turn" — after the card's effect, if the game goes on (wiki Slice and Dice: none outside its turn)
			if (EndTurnPending)
			{
				EndTurnPending = false;
				if (State == State.RUNNING && Step == Step.MAIN_ACTION && CurrentPlayer.Choice == null)
				{
					Step = Step.MAIN_END;
					MainEnd();
				}
			}
			PublishDerived(); // HsServer: anything the move changed that no aura update reported yet

			CheckGameOver(); // HsServer: the end of the Sequence (Rule 9)


			// add power and buff tag changes
			//if (false)
			//{
			//	OldEnchants.ForEach(p =>
			//		p.Effects.Keys.ToList().ForEach(t =>
			//			IdEntityDic.Values.ToList().ForEach(o =>
			//				PowerHistory.Add(PowerHistoryBuilder.TagChange(o.Id, t, o[t])))));

			//	foreach (Controller controller in _players)
			//	{
			//		controller.Hero.OldEnchants.ForEach(p =>
			//			p.Effects.Keys.ToList().ForEach(t =>
			//				PowerHistory.Add(PowerHistoryBuilder.TagChange(Game.CurrentPlayer.Hero.Id, t, Game.CurrentPlayer.Hero[t]))));

			//		//CurrentPlayer.Hero.Weapon?.Enchants.ForEach(p => p.IsEnabled());
			//		//CurrentPlayer.Hero.Weapon?.Triggers.ForEach(p => p.IsEnabled());
			//		//CurrentOpponent.Hero.Weapon?.Enchants.ForEach(p => p.IsEnabled());
			//		//CurrentOpponent.Hero.Weapon?.Triggers.ForEach(p => p.IsEnabled());

			//		controller.ControlledZones.Where(z => z != null).ToList().ForEach(z =>
			//			z.Enchants.ForEach(p =>
			//				p.Effects.Keys.ToList().ForEach(t =>
			//					z.GetAll.ForEach(o =>
			//						PowerHistory.Add(PowerHistoryBuilder.TagChange(o.Id, t, o[t]))))));

			//	}

			//	Characters.ForEach(c =>
			//		c.OldEnchants.ForEach(p =>
			//			p.Effects.Keys.ToList().ForEach(t =>
			//				PowerHistory.Add(PowerHistoryBuilder.TagChange(c.Id, t, c[t])))));
			//}

			//if (Splitting)
			//{
			//	List<SplitNode> finalSplits = SplitNode.GetSolutions(this, 10, 10000);
			//	Dump("Split", $"found {finalSplits.Count} final splits of {finalSplits.Sum(p => p.SameState)}!");
			//	finalSplits.GroupBy(p => p.SameState)
			//		.Select(i => new { Word = i.Key, Count = i.Count() })
			//		.ToList().ForEach(p => Dump("Split", $" {p.Count},  with {p.Word} same states"));
			//	Dump("Split", $"Finalsplits ordered by probability:");
			//	finalSplits.OrderByDescending(p => p.Probability).ToList()
			//		.ForEach(p => Dump("Split", $"{finalSplits.IndexOf(p)}. {p.Probability.ToString("0.00%")} "));
			//	FinalSplits = finalSplits;
			//}

			HoldForChoice(moveStart);
			return result;
		}

		/// <summary>HsServer: history the engine made after a Discover was offered, shown once it is picked.</summary>
		private List<IPowerHistoryEntry> _heldForChoice = new List<IPowerHistoryEntry>();

		/// <summary>HsServer: the client is shown the game up to an open Discover; the rest follows the pick.</summary>
		public bool HoldingForChoice => _heldForChoice.Count > 0;

		/// <summary>
		/// HsServer: live resolves a pick inside the still-open blocks that offered it (the POWER and PLAY blocks end, and a
		/// spell goes to the graveyard, after the chosen card reaches the hand). The engine finishes the play first, so
		/// what it did after offering the choice is held back and shown after the pick.
		/// </summary>
		private void HoldForChoice(int moveStart)
		{
			if (!History) return;
			// (a TARGET pick, Generic.PickTargets, holds the play like a Discover)
			Choice pending = Player1.Choice?.ChoiceType is ChoiceType.GENERAL or ChoiceType.TARGET ? Player1.Choice
				: Player2.Choice?.ChoiceType is ChoiceType.GENERAL or ChoiceType.TARGET ? Player2.Choice : null;
			List<IPowerHistoryEntry> full = PowerHistory.Full, last = PowerHistory.Last;
			// Queued choices (Adapt twice...) need their options shown as they come up: nothing is held for them.
			if (pending?.NextChoice != null) pending = null;
			if (pending != null)
			{
				if (pending.HistoryMark < moveStart || pending.HistoryMark > full.Count) return; // offered earlier: still holding
				// A pick that offered the next one (Grunty's four shots): when what this move shows before the new offer touches
				// something the held part created, the held part goes first (soak 842017: Grunty's Murloc Tidecaller died in
				// the first shot; its enchantment, made after the first offer, was still held — the client got its removal
				// before its creation)
				if (_heldForChoice.Count > 0 && ReleaseHeldBefore(System.Math.Min(System.Math.Max(_holdStart, _moveHistoryStart), pending.HistoryMark), pending.HistoryMark) is int shift && shift > 0)
					foreach (Controller p in new[] { Player1, Player2 })
						for (Choice ch = p?.Choice; ch != null; ch = ch.NextChoice)
							if (ch.HistoryMark >= moveStart) ch.ShiftMark(shift);
				int count = full.Count - pending.HistoryMark;
				var tail = full.GetRange(pending.HistoryMark, count);
				full.RemoveRange(pending.HistoryMark, count);
				last.RemoveRange(System.Math.Max(0, last.Count - count), System.Math.Min(count, last.Count));
				PowerHistory.Reopen(tail); // the pick resolves inside the blocks whose ends are held (their effect, live)
				tail.AddRange(_heldForChoice); // an outer play's rest comes after this one's
				if (_heldForChoice.Count == 0) _holdStart = pending.HistoryMark;
				_heldForChoice = tail;
				return;
			}
			if (_heldForChoice.Count == 0) return;
			// If the pick touches something the held part created (a minion the play summoned after offering the choice),
			// the client must learn of it first: the held part goes before the pick, in the engine's order.
			int pickStart = System.Math.Min(System.Math.Max(_holdStart, _moveHistoryStart), full.Count);
			var createdHeld = new HashSet<int>(_heldForChoice.OfType<PowerHistoryFullEntity>().Select(f => f.Entity.Id));
			bool pickNeedsHeld = createdHeld.Count > 0 && full.Skip(pickStart).Any(e => createdHeld.Any(id => PowerHistory.Refers(e, id)));
			if (pickNeedsHeld)
			{
				full.InsertRange(pickStart, _heldForChoice);
				last.InsertRange(System.Math.Min(System.Math.Max(0, pickStart - _moveHistoryStart), last.Count), _heldForChoice);
				PowerHistory.CloseReopened();
				_heldForChoice = new List<IPowerHistoryEntry>();
				return;
			}
			// The engine did the held part first. Where the pick (shown before it) also changed a tag the held part sets, the
			// client would end on the held value: send the engine's latest (PowerHistory.Reported) after it when they differ.
			var heldLast = new Dictionary<(int, GameTag), int>();
			foreach (IPowerHistoryEntry held in _heldForChoice)
				switch (held)
				{
					case PowerHistoryTagChange t:
						heldLast[(t.EntityId, t.Tag)] = t.Value;
						break;
					// (an entity revealed in the held part carries its tags too — soak 849592: a drawn card's SHOW_ENTITY COST 5
					// shown after the pick that made it cost 4)
					case PowerHistoryShowEntity { Entity: { Tags: { } tags } } shown:
						foreach (KeyValuePair<GameTag, int> tag in tags) heldLast[(shown.Entity.Id, tag.Key)] = tag.Value;
						break;
					case PowerHistoryFullEntity { Entity: { Tags: { } tags } } full2:
						foreach (KeyValuePair<GameTag, int> tag in tags) heldLast[(full2.Entity.Id, tag.Key)] = tag.Value;
						break;
					case PowerHistoryChangeEntity { Entity: { Tags: { } tags } } changed:
						foreach (KeyValuePair<GameTag, int> tag in tags) heldLast[(changed.Entity.Id, tag.Key)] = tag.Value;
						break;
				}
			full.AddRange(_heldForChoice);
			last.AddRange(_heldForChoice);
			foreach (var ((id, tag), value) in heldLast)
				if (PowerHistory.Reported.TryGetValue(id, out var known) && known.TryGetValue(tag, out int current) && current != value)
				{
					var again = new PowerHistoryTagChange { EntityId = id, Tag = tag, Value = current };
					full.Add(again);
					last.Add(again);
				}
			PowerHistory.CloseReopened();
			_heldForChoice = new List<IPowerHistoryEntry>();
		}

		/// <summary>HsServer: when the history from <paramref name="pickStart"/> to <paramref name="pickEnd"/> (a pick, shown) touches what
		/// the held part created, the held part goes in before it; returns how many entries went in (0: kept held).</summary>
		private int ReleaseHeldBefore(int pickStart, int pickEnd)
		{
			List<IPowerHistoryEntry> full = PowerHistory.Full, last = PowerHistory.Last;
			var createdHeld = new HashSet<int>(_heldForChoice.OfType<PowerHistoryFullEntity>().Select(f => f.Entity.Id));
			if (createdHeld.Count == 0 || pickStart >= pickEnd ||
			    !full.Skip(pickStart).Take(pickEnd - pickStart).Any(e => createdHeld.Any(id => PowerHistory.Refers(e, id))))
				return 0;
			int n = _heldForChoice.Count;
			full.InsertRange(pickStart, _heldForChoice);
			last.InsertRange(System.Math.Min(System.Math.Max(0, pickStart - _moveHistoryStart), last.Count), _heldForChoice);
			PowerHistory.CloseReopened();
			_heldForChoice = new List<IPowerHistoryEntry>();
			return n;
		}

		/// <summary>HsServer: where in the history the current hold began (what came after it is shown before the held part).</summary>
		private int _holdStart;

		private int _moveHistoryStart;

		/// <summary>HsServer: history before this index may be sent already or marked by an open choice: don't reorder it.</summary>
		internal int HistoryMoveFloor()
		{
			int floor = _moveHistoryStart;
			foreach (Controller c in new[] { Player1, Player2 })
				for (Choice ch = c?.Choice; ch != null; ch = ch.NextChoice)
					floor = System.Math.Max(floor, ch.HistoryMark);
			return floor;
		}

		#region STATE_MACHINE

		/// <summary>
		/// Part of the state machine.
		/// Runs when STATE = RUNNING.
		/// First player is determined here.
		/// For true <paramref name="stopBeforeShuffling"/> the order of cards in decks
		/// and the first hands of both players are determined too.
		/// </summary>
		/// <param name="stopBeforeShuffling">true if you want to start shuffling
		/// and drawing cards later.</param>
		/// <summary>Client tag 3085 (unnamed): a Heist hero's Health for the run (live: set with the run's Health).</summary>
		private const GameTag RunHealthTag = (GameTag)3085;

		public void StartGame(bool stopBeforeShuffling = false)
		{
			Log(LogLevel.INFO, BlockType.PLAY, "Game", !Logging ? "" : "Starting new game now!");

			// set gamestats
			State = State.RUNNING;
			_players.ToList().ForEach(p => p.PlayState = PlayState.PLAYING);

			// HsServer: sideboard cards' deck-building effects (Commander Beatrix's ten copies), before the first player and
			// the opening hands (live-20261003-dk-classxp)
			Generic.SideboardEffects(this);

			// HsServer (2026-10-09, Dalaran Heist): the starting Hero Powers work from the start (a boss's Passive Hero Power: its trigger /
			// aura on, as a replacing Hero Power's is — Generic.ActivateStartingHeroPower; live: The Feast, Blessing of Elune's
			// ALL_HEALING_DOUBLE on both players from the start, live-session-20260929-183021 5251)
			foreach (Controller p in _players) Generic.ActivateStartingHeroPower(p);

			// HsServer (2026-10-09, Dalaran Heist): the run's Health for each hero — live (live-20261004-night 224614, 2nd game; client
			// Power.log 22:21:42): right after the players start, a TRIGGER block of the game where each hero's HEALTH changes twice, first
			// as a "DEF CHANGE" (the card's base Health becomes the run's: the client shows no green buff colour), then the value; tag
			// 3085 follows with the same number (live: 3085 = the hero's Health in every Heist game, the first one's FULL_ENTITY too).
			// Nothing to change when the card already has it (live's first game: the hero 15, the boss 10, no block).
			if (_gameConfig.Player1StartHealth != null || _gameConfig.Player2StartHealth != null)
			{
				// (2026-11-01, user: boss Health on screen ≠ the server's) compared with the card's printed Health — what the client
				// shows from the FULL_ENTITY — not the entity's current value, which may already hold the run's; live's order
				// (live 2026-10-04 22:20 Power.log 642-655): each hero DEF CHANGE then the value, then 3085 for both
				bool opened = false;
				var run = new List<(Hero Hero, int Health)>();
				for (int i = 0; i < 2; i++)
				{
					Hero hero = _players[i].Hero;
					if (!((i == 0 ? _gameConfig.Player1StartHealth : _gameConfig.Player2StartHealth) is int health && health > 0)) continue;
					run.Add((hero, health));
					if (hero.Card[GameTag.HEALTH] == health && hero[GameTag.HEALTH] == health) continue; // (live's first game: no block)
					if (History && !opened)
					{
						PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, Id, "", -1, 0));
						opened = true;
					}
					if (History)
					{
						PowerHistory.Add(new PowerHistoryTagChange { EntityId = hero.Id, Tag = GameTag.HEALTH, Value = health, ChangeDef = true });
						PowerHistory.Add(new PowerHistoryTagChange { EntityId = hero.Id, Tag = GameTag.HEALTH, Value = health });
					}
					hero[GameTag.HEALTH] = health; // (the engine's setter: a silent set was undone by its recalculation, 2026-11-01)
				}
				foreach (var (hero, health) in run)
				{
					if (History && (opened || hero[RunHealthTag] != health))
						PowerHistory.Add(new PowerHistoryTagChange { EntityId = hero.Id, Tag = RunHealthTag, Value = health });
					hero[RunHealthTag] = health;
				}
				if (opened)
					PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			}

			// HsServer (2026-10-09, Dalaran Heist): the chapter's twist, right after the Health block (Generic.PlayPlotTwist)
			Generic.PlayPlotTwist(this, Player1, _gameConfig.Player1PlotTwist);
			Generic.PlayPlotTwist(this, Player2, _gameConfig.Player2PlotTwist);

			// HsServer (2026-10-09, Dalaran Heist): the starting heroes' own triggers, the run deck's enchantments; a PUZZLE_MODE
			// scenario (Bob's Tavern) starts its own way — no mulligan, no opening hand (Actions/DalaranHeistGroup5.cs)
			if (Actions.Generic.ScenarioGameStart(this))
				return;

			// starting mulligan draw block
			if (History)
				PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, Id, "", -1, 0));

			// getting first player
			FirstPlayer = _gameConfig.StartPlayer < 0
				? _players[Random.Next(0, 2)]
				: _players[_gameConfig.StartPlayer - 1];
			// HsServer (VIOLET_HOLD): "You always go second" (Aya, Lotus Kingpin; wiki: "If both players are using this card in their deck, it
			// is randomly decided"; not in Solo Adventures — a set start player is kept)
			if (_gameConfig.StartPlayer < 0 && Actions.Generic.AlwaysGoesSecond(_players[0]) != Actions.Generic.AlwaysGoesSecond(_players[1]))
				FirstPlayer = Actions.Generic.AlwaysGoesSecond(_players[0]) ? _players[1] : _players[0];
			CurrentPlayer = FirstPlayer;

			Log(LogLevel.INFO, BlockType.PLAY, "Game", !Logging ? "" : $"Starting Player is {CurrentPlayer.Name}.");

			// first turn
			Turn = 1;

			// triggers Start of Game triggers (but does not process tasks here)
			TriggerManager.OnGameStartTrigger();

			if (stopBeforeShuffling)
				return;

			// set next step
			NextStep = Step.BEGIN_FIRST;
		}

		/// <summary>
		/// Part of the state machine.
		/// Runs when STATE = RUNNING &amp;&amp; NEXTSTEP = BEGIN_FIRST
		/// </summary>
		public void BeginFirst()
		{
			Log(LogLevel.VERBOSE, BlockType.PLAY, "Game", !Logging ? "" : $"Begin First.");

			// set next step
			NextStep = Step.BEGIN_SHUFFLE;
		}

		/// <summary>
		/// Part of the state machine.
		/// Runs when STATE = RUNNING &amp;&amp; NEXTSTEP = BEGIN_SHUFFLE
		/// </summary>
		public void BeginShuffle()
		{
			Log(LogLevel.VERBOSE, BlockType.PLAY, "Game", !Logging ? "" : $"Begin Shuffle.");

			if (_gameConfig.Shuffle)
			{
				Player1.DeckZone.Shuffle();
				Player2.DeckZone.Shuffle();
			}

			// set next step
			NextStep = Step.BEGIN_DRAW;
		}

		/// <summary>
		/// Part of the state machine.
		/// Runs when STATE = RUNNING &amp;&amp; NEXTSTEP = BEGIN_DRAW
		/// </summary>
		public void BeginDraw()
		{
			Log(LogLevel.VERBOSE, BlockType.PLAY, "Game", !Logging ? "" : $"Begin Draw.");

			//FirstPlayer.NumCardsToDraw = 3;
			//FirstPlayer.Opponent.NumCardsToDraw = 4;

			_players.ToList().ForEach(p =>
			{
				// HsServer (2026-10-08): every Quest goes into the opening hand, left-most — wiki x_Quest_(ability).txt:8 "Quest cards are
				// automatically included in the player's mulligan as the left-most card(s)" (Quests before Questlines); the rest drawn
				int size = p == FirstPlayer ? 3 : 4;
				var quests = p.DeckZone.Where(q => q is Spell spell && spell.IsQuest)
					.OrderBy(q => q.Card[HsTag.QUESTLINE] > 0 ? 1 : 0).Take(size).ToList();
				// HsServer (2026-10-09, Dalaran Heist): "Always starts in your hand" (DALA_905e Right Hand Man) — after the Quests
				quests.AddRange(Actions.Generic.AlwaysStartsInHand(p, quests).Take(size - quests.Count));
				foreach (IPlayable quest in quests)
					Generic.Draw(p, quest);
				for (int i = quests.Count; i < size; i++)
					Generic.Draw(p);

				// HsServer (2026-11-01): the AI opponent going second gets no Coin (GameConfig.Player2Coin, live)
				if (p != FirstPlayer && (p != Player2 || _gameConfig.Player2Coin))
				{

					IPlayable coin = FromCard(FirstPlayer.Opponent, Cards.FromId("GAME_005"), new EntityData
					{
						[GameTag.ZONE] = (int)Enums.Zone.HAND,
						[GameTag.CARDTYPE] = (int)CardType.SPELL,
						[GameTag.CREATOR] = FirstPlayer.Opponent.PlayerId
					});
					Generic.AddHandPhase(FirstPlayer.Opponent, coin);
				}

				p.NumTurnsLeft = 1;
			});
			// HsServer (WILD_WEST): the starting hand never has Quickdraw (the mulligan's replacements do: Actions/WildWest.cs)
			Actions.Generic.StartingHandNotQuickdraw(this);

			// HsServer (live): the first turn is shorter (Controller.FirstTurnSeconds); live solo games (GT_VS_AI) send no TIMEOUT
			if (TurnTimer)
			{
				Player1.TimeOut = Controller.FirstTurnSeconds;
				Player2.TimeOut = Controller.FirstTurnSeconds;
			}

			// ending mulligan draw block
			if (History)
				PowerHistory.Add(PowerHistoryBuilder.BlockEnd());

			NextStep = _gameConfig.SkipMulligan ? Step.MAIN_BEGIN : Step.BEGIN_MULLIGAN;
		}

		/// <summary>
		/// Part of the state machine.
		/// Runs when STATE = RUNNING &amp;&amp; NEXTSTEP = BEGIN_MULLIGAN
		/// </summary>
		public void BeginMulligan()
		{
			Log(LogLevel.VERBOSE, BlockType.PLAY, "Game", !Logging ? "" : $"Begin Mulligan.");
			Generic.HeraldGameStart(this); // HsServer (CATACLYSM): HERALD_COLOSSAL_CLASS for the Herald classes

			// starting mulligan draw block
			if (History)
				PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, Id, "", -1, 0));

			Player1.MulliganState = Mulligan.INPUT;
			Player2.MulliganState = Mulligan.INPUT;

			Generic.CreateChoice.Invoke(Player1, this, ChoiceType.MULLIGAN, ChoiceAction.HAND, Player1.HandZone.Select(p => p.Id).ToList());
			Generic.CreateChoice.Invoke(Player2, this, ChoiceType.MULLIGAN, ChoiceAction.HAND, Player2.HandZone.Select(p => p.Id).ToList());

			// ending mulligan draw block
			if (History)
				PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
		}

		/// <summary>
		/// Part of the state machine.
		/// Runs when STATE = RUNNING &amp;&amp; NEXTSTEP = MAIN_BEGIN
		/// </summary>
		public void MainBegin()
		{
			Log(LogLevel.VERBOSE, BlockType.PLAY, "Game", !Logging ? "" : $"Main Begin.");

			// and a coin
			//Generic.DrawCard(FirstPlayer.Opponent, Cards.FromId("GAME_005"));

			ProcessTasks();

			// HsServer (2026-10-09, Dalaran Heist): the set-aside Dungeon passives act once the mulligan is done (Generic.PlayDungeonPassives)
			Generic.PlayDungeonPassives(this);
			// HsServer (2026-10-09, Dalaran Heist Anomaly Mode): the run's anomaly right after the passives (Generic.PlayAnomaly)
			Generic.PlayAnomaly(this, Player1, _gameConfig.Player1Anomaly);
			Generic.PlayAnomaly(this, Player2, _gameConfig.Player2Anomaly);

			NextStep = Step.MAIN_READY;
		}

		/// <summary>
		/// Part of the state machine.
		/// Runs when STATE = RUNNING &amp;&amp; NEXTSTEP = MAIN_READY
		/// </summary>
		public void MainReady()
		{
			OpenStepBlock(CurrentPlayer.Id);

			// HsServer: live counts the turns everything in play has been there, first thing each turn: the game, the
			// players, heroes, hero powers, then the minions. The client shows a minion asleep (Zzz) while its count is 0.
			var inPlay = new List<IEntity> { this, Player1, Player2, Player1.Hero, Player1.Hero.HeroPower, Player2.Hero, Player2.Hero.HeroPower };
			inPlay.AddRange(Player1.BoardZone.GetAll());
			inPlay.AddRange(Player2.BoardZone.GetAll());
			foreach (IEntity e in inPlay)
				if (e != null)
					e[GameTag.NUM_TURNS_IN_PLAY] = e[GameTag.NUM_TURNS_IN_PLAY] + 1;


			for (int i = 0; i < 2; ++i)
			{
				Controller c = _players[i];

				c.BoardZone.ForEach(p =>
				{
					//p.NumTurnsInPlay++;
					p.NumAttacksThisTurn = 0;
					// (2026-11-19, live: an Old God's extra action shown last turn goes back to 0 right after its attacks count)
					if (c == CurrentPlayer && p[GameTag.EXTRA_ATTACKS_THIS_TURN] != 0) p[GameTag.EXTRA_ATTACKS_THIS_TURN] = 0;
				});

				//c.Hero.NumTurnsInPlay++;
				c.Hero.NumAttacksThisTurn = 0;

				c.NumCardsDrawnThisTurn = 0;
				// HsServer (2026-10-08): live resets the played counters only for the player whose turn starts (live-20261008-120539: at
				// MAIN_READY NUM_CARDS_PLAYED / MINIONS_PLAYED / OPTIONS_PLAYED = 0 on the new current player only; the other keeps its turn's)
				if (c == CurrentPlayer)
				{
					c.NumCardsPlayedThisTurn = 0;
					c.NumMinionsPlayedThisTurn = 0;
					c.NumOptionsPlayedThisTurn = 0;
				}
				c.NumFriendlyMinionsThatDiedThisTurn = 0;
				if (c.AmountHeroHealedThisTurn > 0)
					c.AmountHeroHealedThisTurn = 0;
				c.AmountHealedThisTurn = 0; // HsServer
			}
			// HsServer: the current player's Locations come off cooldown (live: next to NUM_OPTIONS_PLAYED_THIS_TURN=0) — and a
			// Location that was on cooldown stays exhausted this turn: wiki Location "When a location's ability is used, it cannot
			// be used in the following turn"; live (32 of 45 Location sequences, none against: JAIL_987 live-20261003-dk-classxp
			// 686 / 865 / 1416) clears COOLDOWN at the owner's next MAIN_READY and EXHAUSTED one owner turn later.
			var resting = new HashSet<int>();
			CurrentPlayer.BoardZone.ForEach(m =>
			{
				if (m is Location location && location.OnCooldown) { location.OnCooldown = false; resting.Add(location.Id); }
				if (m[HsTag.ASLEEP_UNTIL_TURN] >= Turn) resting.Add(m.Id); // HsServer (CATACLYSM): Tranquil Clearing's sleep
			});

			CurrentPlayer.Hero.IsExhausted = false;
			if (CurrentPlayer.Hero.Weapon != null)
				CurrentPlayer.Hero.Weapon.IsExhausted = false;
			CurrentPlayer.Hero.HeroPower.IsExhausted = false;
			if (Generic.SecondHeroPower(CurrentPlayer) is HeroPower second) // HsServer (VIOLET_HOLD): Thal'ena's, each once a turn
			{
				second.IsExhausted = false;
				if (second[GameTag.HEROPOWER_ACTIVATIONS_THIS_TURN] != 0) second[GameTag.HEROPOWER_ACTIVATIONS_THIS_TURN] = 0;
			}
			CurrentPlayer.BoardZone.ForEach(m => { if (!resting.Contains(m.Id)) m.IsExhausted = false; });

			// (combo: cleared for the ending player in its MAIN_CLEANUP — live)

			CurrentPlayer.SecretZone.ForEach(s => s.IsExhausted = true);

			CurrentPlayer.NumMinionsPlayerKilledThisTurn = 0;
			CurrentOpponent.NumMinionsPlayerKilledThisTurn = 0;
			CurrentPlayer.NumFriendlyMinionsThatAttackedThisTurn = 0;
			NumMinionsKilledThisTurn = 0;
			CurrentPlayer.HeroPowerActivationsThisTurn = 0;
			if (CurrentPlayer.Hero.HeroPower is { } power && power[GameTag.HEROPOWER_ACTIVATIONS_THIS_TURN] != 0)
				power[GameTag.HEROPOWER_ACTIVATIONS_THIS_TURN] = 0;

			Actions.Generic.RollElementalStreak(CurrentPlayer); // HsServer (WILD_WEST): "each turn in a row you've played an Elemental"
			CurrentPlayer.NumElementalsPlayedLastTurn = CurrentPlayer.NumElementalsPlayedThisTurn;
			CurrentPlayer.NumElementalsPlayedThisTurn = 0;

			MainRessources();

			// set next step
			NextStep = Step.MAIN_START_TRIGGERS;
		}

		/// <summary>
		/// Part of the state machine.
		/// Runs when STATE = RUNNING &amp;&amp; NEXTSTEP = MAIN_START_TRIGGERS
		/// </summary>
		public void MainStartTriggers()
		{
			CurrentPlayer.BoardZone.AwakenDormant(); // HsServer: Dormant minions count down and wake up (live: before the step block)
			OpenStepBlock(CurrentPlayer.Id);
			TriggerManager.OnTurnStartTrigger(CurrentPlayer);
			ProcessTasks();
			DeathProcessingAndAuraUpdate();

			// HsServer (2026-10-09): a pick a start-of-turn effect offered comes before the turn's draw (live Sphere of Sapience:
			// "look at your top card" — the choice, then NEXT_STEP MAIN_START and the draw); the step goes on after it (Process)
			if (CurrentPlayer.Choice?.ChoiceType is ChoiceType.GENERAL or ChoiceType.TARGET)
			{
				this[HsTag.STEP_AFTER_CHOICE] = (int)Step.MAIN_START;
				return;
			}

			// set next step
			//NextStep = Step.MAIN_RESOURCE;
			NextStep = Step.MAIN_START;
		}

		/// <summary>
		/// Part of the state machine.
		/// Runs when STATE = RUNNING &amp;&amp; NEXTSTEP = MAIN_RESOURCE
		/// </summary>
		public void MainRessources()
		{
			Controller c = CurrentPlayer;

			// adding manacrystal to next player
			Generic.ChangeManaCrystal.Invoke(c, 1, false);

			// remove temp mana
			c.TemporaryMana = 0;

			// HsServer: live turn start — RESOURCES_USED = the overload now locked, then OVERLOAD_LOCKED, then OWED = 0.
			int owed = c.OverloadOwed;
			c.UsedMana = owed;
			c.SetOverloadLockedAtTurnStart(owed);
			c.OverloadOwed = 0;

			//// set next step
			//NextStep = Step.MAIN_DRAW;
		}

		/// <summary>
		/// Part of the state machine.
		/// Runs when STATE = RUNNING &amp;&amp; NEXTSTEP = MAIN_DRAW
		/// </summary>
		public void MainDraw()
		{
			//CurrentPlayer.NumCardsToDraw = 1;
			// HsServer (TIME_TRAVEL): "You no longer draw a card at the start of your turn" (Chronochiller; live player tag 4316)
			if (CurrentPlayer[(GameTag)4316] > 0 || CurrentPlayer.Hero?.Card.Power?.SkipsTurnDraw == true) return; // (+ a deckless boss: Power.SkipsTurnDraw)
			// HsServer (2026-10-09): a trigger cancelled this draw (Bob's Tavern: Actions/DalaranHeistGroup5.cs CancelTurnDraw)
			if (Actions.Generic.TurnDrawCancelled(CurrentPlayer)) return;
			// HsServer (CATACLYSM): Commander Geddon — "Instead of drawing each turn, Discover a card from your deck"
			if (CurrentPlayer[HsTag.TURN_DRAW_DISCOVER] > 0 && CurrentPlayer.DeckZone.Count > 0)
			{
				Generic.DiscoverInsteadOfDraw(CurrentPlayer);
				return;
			}
			// HsServer (2026-11-20): Discs of Norgannon's "draw from the bottom" picked this turn
			Generic.Draw(CurrentPlayer, CurrentPlayer[HsTag.DRAW_BOTTOM_TURN] == Turn && CurrentPlayer.DeckZone.Count > 0 ? CurrentPlayer.DeckZone[0] : null);

			//// set next step
			//NextStep = Step.MAIN_START;
		}

		/// <summary>
		/// Part of the state machine.
		/// Runs when STATE = RUNNING &amp;&amp; NEXTSTEP = MAIN_START
		/// </summary>
		public void MainStart()
		{
			OpenStepBlock(CurrentPlayer.Id);
			MainDraw();

			Log(LogLevel.INFO, BlockType.PLAY, "Game", !Logging ? "" : $"[T:{Turn}/R:{Turn / 2}] with CurrentPlayer {CurrentPlayer.Name} " +
					 $"[HP:{CurrentPlayer.Hero.Health}/M:{CurrentPlayer.RemainingMana}]");

			DeathProcessingAndAuraUpdate();

			NextStep = Step.MAIN_ACTION;
		}

		/// <summary>
		/// Part of the state machine.
		/// Runs when STATE = RUNNING &amp;&amp; NEXTSTEP = MAIN_END
		/// </summary>
		/// <summary>HsServer: Freeze thaw timing — the character had an unused attack it could have made this turn (ignoring
		/// Freeze itself, its Attack value and Can't Attack): attacks left, not summoning sick, and an enemy it may hit.</summary>
		private static bool CouldStillAttack(ICharacter c)
		{
			if (c.NumAttacksThisTurn >= Generic.AttacksPerTurn(c))
				return false;
			Minion minion = c as Minion;
			// entered play this turn with Rush (ATTACKABLE_BY_RUSH is already cleared when this runs)
			bool rushOnly = minion != null && minion[GameTag.NUM_TURNS_IN_PLAY] == 0 && minion.IsRush && !minion.HasCharge;
			if (minion != null && minion.IsExhausted && !rushOnly)
				return false; // summoning sick
			Controller enemy = c.Controller.Opponent;
			bool anyMinion = enemy.BoardZone.Any(m => !m.HasStealth && !m.IsImmune && !m.Untouchable);
			bool hero = !rushOnly && !(c is Character ch && ch.CantAttackHeroes) && !enemy.Hero.HasStealth && !enemy.Hero.IsImmune;
			return anyMinion || hero;
		}

		public void MainEnd()
		{
			Log(LogLevel.INFO, BlockType.PLAY, "Game", !Logging ? "" : $"End turn proccessed by player {CurrentPlayer}");

			OpenStepBlock(CurrentPlayer.Id);

			//CurrentPlayer.TurnStart = false;

			TaskQueue.StartEvent();
			TriggerManager.OnEndTurnTrigger(CurrentPlayer);
			ProcessTasks();
			TaskQueue.EndEvent();
			DeathProcessingAndAuraUpdate();
			Actions.Generic.RunEndOfThisTurn(this); // HsServer (2026-11-20): "at the end of this turn" effects


			CurrentPlayer.CardsPlayedThisTurn.Clear();

			CurrentPlayer.Hero.DamageTakenThisTurn = 0;
			// HsServer (ISLAND_VACATION): "this turn" ends for both players (live 4453 zeroed at every turn change)
			foreach (Controller p in new[] { Player1, Player2 })
			{
				if (p[HsTag.HERO_DAMAGE_THIS_TURN] != 0) p[HsTag.HERO_DAMAGE_THIS_TURN] = 0;
				if (p[Actions.Generic.HERO_DAMAGED_TIMES_THIS_TURN] != 0) p[Actions.Generic.HERO_DAMAGED_TIMES_THIS_TURN] = 0;
			}

			if (RushMinions.Count > 0)
			{
				RushMinions.ForEach(i => IdEntityDic[i][GameTag.ATTACKABLE_BY_RUSH] = 0);
				RushMinions.Clear();
			}

			// set next step
			//NextStep = Step.MAIN_NEXT;
			NextStep = Step.MAIN_CLEANUP;
		}

		/// <summary>HsServer (live, live-20261006-echo): the turn's Echo copies leave the hand in the player's own TRIGGER block
		/// right after the end-of-turn step's NEXT_STEP MAIN_CLEANUP (ZONE_POSITION 0, then SETASIDE), before the cleanup step.</summary>
		private void RemoveGhostlyCards()
		{
			if (GhostlyCards.Count == 0) return;
			if (History) PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, CurrentPlayer.Id, "", -1, 0));
			foreach (int id in GhostlyCards)
			{
				IPlayable entity = IdEntityDic[id];
				if (entity.Zone?.Type != Enums.Zone.HAND) continue;
				IPlayable leaving = entity.Zone.Remove(entity);
				leaving[GameTag.ZONE_POSITION] = 0;
				entity.Controller.SetasideZone.Add(leaving);
			}
			GhostlyCards.Clear();
			if (History) PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
		}

		/// <summary>
		/// Part of the state machine.
		/// Runs when STATE = RUNNING &amp;&amp; NEXTSTEP = MAIN_CLEANUP
		/// </summary>
		public void MainCleanUp()
		{
			OpenStepBlock(CurrentPlayer.Id);

			// HsServer (2026-10-08): the ending player's combo ends here (live-20261008-120539: COMBO_ACTIVE 0 in its MAIN_CLEANUP block)
			CurrentPlayer.IsComboActive = false;

			// HsServer (live): the turn's played cards stop being JUST_PLAYED in this cleanup block.
			foreach (IPlayable e in IdEntityDic.Values)
			{
				if (e.Controller == CurrentPlayer && e[GameTag.JUST_PLAYED] != 0)
					e[GameTag.JUST_PLAYED] = 0;
				if (e[Actions.Generic.DORMANT_AWAKENED_THIS_TURN] != 0) // live: cleared at the end of the turn it awoke
					e[Actions.Generic.DORMANT_AWAKENED_THIS_TURN] = 0;
			}

			// Removing one-turn-effects
			if (OneTurnEffectEnchantments.Count > 0)
			{
				List<Enchantment> enchantments = OneTurnEffectEnchantments;
				for (int i = enchantments.Count - 1; i >= 0; --i)
					enchantments[i].Remove();	
			}
			if (OneTurnEffects.Count > 0)
			{
				// HsServer: a one-turn effect can sit on a player (Ruby Sanctum), which isn't in IdEntityDic.
				foreach ((int id, IEffect eff) in OneTurnEffects)
					eff.RemoveFrom(IdEntityDic.TryGetValue(id, out IPlayable playable) ? playable
						: id == Player1.Id ? Player1 : id == Player2.Id ? (IEntity)Player2 : this);
				
				OneTurnEffects.Clear();
			}

			// After a player ends their turn (just before the next player's Start of
			// Turn Phase), un-Freeze all characters they control that are Frozen, 
			// don't have summoning sickness (or do have Charge) and have not attacked
			// that turn.
			// HsServer: rules (Freeze thaw timing) — it thaws now when it still had an attack it could have made this turn:
			// Windfury with an attack left, Rush with an enemy minion to hit, Charge with a target. Summoning sick, all
			// attacks used, or nothing it may attack: it stays Frozen through its next turn. 0 Attack / Can't Attack
			// don't delay thawing.
			CurrentPlayer.BoardZone.ForEach(m =>
			{
				if (m.IsFrozen && CouldStillAttack(m) && m[HsTag.STAYS_FROZEN] == 0) // HsServer: "Frozen until …"
					m.IsFrozen = false;
			});

			if (CurrentPlayer.Hero.IsFrozen && CouldStillAttack(CurrentPlayer.Hero))
			{
				CurrentPlayer.Hero.IsFrozen = false;
			}

			// Exhausts weapon and secrets
			if (CurrentPlayer.Hero.Weapon != null)
				CurrentPlayer.Hero.Weapon.IsExhausted = true;
			CurrentPlayer.SecretZone.ForEach(p => p.IsExhausted = false);


			NextStep = Step.MAIN_NEXT;
		}

		/// <summary>
		/// Part of the state machine.
		/// Runs when STATE = RUNNING &amp;&amp; NEXTSTEP = MAIN_NEXT
		/// </summary>
		/// <summary>HsServer: damage the rules deal with the hero as its nominal source (Fatigue, a Health cost): the hero
		/// doesn't deal it, so its source effects (a Lifesteal weapon's heal, Poisonous) don't apply.</summary>
		internal bool RulesDamage;

		/// <summary>HsServer: the last turn: the game is a draw once it has ended (rules: turn 89).</summary>
		public const int MaxTurns = 89;

		public void MainNext()
		{
			OpenStepBlock(Id);

			// HsServer: rules (Advanced rulebook, Start and end of turn): the game is a draw when turn 89 has just ended.
			if (Turn >= MaxTurns)
			{
				Player1.PlayState = PlayState.TIED;
				Player2.PlayState = PlayState.TIED;
				NextStep = Step.FINAL_WRAPUP;
				return;
			}

			// HsServer (live): the player whose turn ends gets a longer turn next time (45 -> 55 -> 75 s), sent before the
			// next player becomes current.
			// While a turn-time limit shows on TIMEOUT (Nozdormu: Generic.SyncTurnLimit) the raise goes to the TIMEOUT kept for after it.
			if (TurnTimer && CurrentPlayer[HsTag.TURN_LIMIT_SHOWN] > 0)
				CurrentPlayer[HsTag.TURN_SECONDS_BASE] = Controller.RaisedTurnSeconds(CurrentPlayer[HsTag.TURN_SECONDS_BASE]);
			else if (TurnTimer && CurrentPlayer.TimeOut < Controller.TurnSeconds)
				CurrentPlayer.TimeOut = Controller.RaisedTurnSeconds(CurrentPlayer.TimeOut);

			// HsServer (live): the cards in the hand of the player whose turn ends have been in hand one more turn.
			foreach (IPlayable card in CurrentPlayer.HandZone)
				card[HsTag.NUM_TURNS_IN_HAND] += 1;
			// HsServer: where "after your last turn" starts for the player whose turn ends (Generic.UndeadDiedAfterYourLastTurn)
			CurrentPlayer[HsTag.DIED_AT_LAST_TURN_END] = CurrentPlayer.DiedThisGame.Count;

			// extra turn effect here
			if (CurrentPlayer.NumTurnsLeft > 1)
			{
				//this[GameTag.EXTRA_TURNS_TAKEN_THIS_GAME]++;
				//CurrentPlayer[GameTag.EXTRA_TURNS_TAKEN_THIS_GAME]++;
				//this[GameTag.IS_CURRENT_TURN_AN_EXTRA_TURN] = 1;
				CurrentPlayer.NumTurnsLeft--;
			}
			else if
				(CurrentOpponent.TemporusFlag)
			{
				//this[GameTag.EXTRA_TURNS_TAKEN_THIS_GAME]++;
				//CurrentOpponent[GameTag.EXTRA_TURNS_TAKEN_THIS_GAME]++;
				//this[GameTag.IS_CURRENT_TURN_AN_EXTRA_TURN] = 1;
				CurrentPlayer = CurrentOpponent;
				CurrentPlayer.NumTurnsLeft = 2;
				CurrentPlayer.TemporusFlag = false;
			}
			else
			{
				CurrentPlayer.NumTurnsLeft = 0;
				CurrentOpponent.NumTurnsLeft = 1;
				// set player for next turn ...
				CurrentPlayer = CurrentOpponent;
			}

			// HsServer (live order): the incoming player's hero damage during the turn that just ended, then the game turn,
			// then that player's own turn number.
			CurrentPlayer[HsTag.DAMAGE_DEALT_TO_HERO_LAST_TURN] = CurrentPlayer.Hero.DamageTakenThisTurn;
			foreach (Controller p in new[] { CurrentPlayer, CurrentOpponent }) // HsServer (CATACLYSM): live vfx 1327
				if (p[Actions.Generic.SPELL_DAMAGE_THIS_TURN] != 0) p[Actions.Generic.SPELL_DAMAGE_THIS_TURN] = 0;

			// count next turn
			Turn += 1;
			CurrentPlayer[GameTag.TURN] += 1;

			Log(LogLevel.INFO, BlockType.PLAY, "Game", !Logging ? "" : $"CurentPlayer {CurrentPlayer.Name}.");


			// set next step
			NextStep = Step.MAIN_READY;
		}

		/// <summary>
		/// Part of the state machine.
		/// Runs when STATE = RUNNING &amp;&amp; NEXTSTEP = FINAL_WRAPUP
		/// </summary>
		public void FinalWrapUp()
		{
			OpenStepBlock(Id);

			foreach (Controller player in _players)
			{
				// HsServer: a draw stays TIED for both players (the client has a TIED end state); upstream made both LOST.
				if (player.PlayState == PlayState.TIED)
					break;

				if (player.PlayState != PlayState.LOSING && player.PlayState != PlayState.CONCEDED) continue;

				player.PlayState = PlayState.LOST;
				player.Opponent.PlayState = PlayState.WON;
			}


			// set next step
			NextStep = Step.FINAL_GAMEOVER;
		}

		/// <summary>
		/// Part of the state machine.
		/// Runs when STATE = RUNNING &amp;&amp; NEXTSTEP = FINAL_GAMEOVER
		/// </summary>
		public void FinalGameOver()
		{
			State = State.COMPLETE;
			if (Logging)
			{
				_players.ToList().ForEach(p =>
				{
					Log(LogLevel.INFO, BlockType.PLAY, "Game", !Logging ? "" : $"{p.Name} has {p.PlayState} the Game!");
				});
			}
		}
		#endregion

		internal Action ClearWeapons;
		private static readonly Func<Minion, int> GetOrderOfPlay = m => m.OrderOfPlay;

		/// <summary>
		/// Move destroyed entities from <see cref="Zone.PLAY"/> <see cref="Zone{T}"/> into 
		/// <see cref="Zone.GRAVEYARD"/>
		/// <para></para>
		/// Death Creation Step (Death event is created but not resolved here)
		/// </summary>
		private static readonly Tasks.SimpleTasks.CustomTask RebornTask = new Tasks.SimpleTasks.CustomTask((g, c, s, t, stack) =>
		{
			var dead = (Minion)s;
			if (c.BoardZone.IsFull) return;
			Actions.Generic.NoteReborn(c, dead.Card); // HsServer (VIOLET_HOLD): Raith Van Geist's "were Reborn this game"
			// HsServer (VIOLET_HOLD): "This is Reborn with full Health and enchantments" (client tag 2872; live dk-classxp 34940): the copy made
			// before the graveyard stripped it comes back whole, without the enchantment that gave the rule (client tag 4137)
			if (dead[HsTag.REBORN_SNAPSHOT] > 0 && g.IdEntityDic.TryGetValue(dead[HsTag.REBORN_SNAPSHOT], out IPlayable kept) && kept is Minion whole &&
			    whole.Zone?.Type == Enums.Zone.SETASIDE)
			{
				whole.Zone.Remove(whole);
				Actions.Generic.ReadyFullHealthReborn(whole, dead);
				Actions.Generic.SummonBlock.Invoke(g, whole, System.Math.Min(dead.LastBoardPosition, c.BoardZone.Count), dead);
				whole[GameTag.REBORN] = 0;
				if (whole[Actions.Generic.FULL_HEALTH_REBORN] != 0) whole[Actions.Generic.FULL_HEALTH_REBORN] = 0;
				return;
			}
			var copy = (Minion)Entity.FromCard(c, dead.Card, new EntityData
			{
				[GameTag.CREATOR] = dead.Id, [(GameTag)1254] = dead.Id,
				[GameTag.START_WITH_1_HEALTH] = 1, [GameTag.HAS_BEEN_REBORN] = 1, [HsTag.REBORN_SUMMON] = 1,
			});
			copy.Damage = copy.BaseHealth - 1;
			Actions.Generic.SummonBlock.Invoke(g, copy, System.Math.Min(dead.LastBoardPosition, c.BoardZone.Count), dead);
			copy[GameTag.START_WITH_1_HEALTH] = 0;
			copy[GameTag.REBORN] = 0;
		});

		public void GraveYard()
		{
			// HsServer (2026-10-08): a weapon broken this Phase dies in the Death Phase in order of play with the minions — rulebook :618
			// "weapons with Deathrattles such as Death's Bite have their Deaths Events considered in order of play with the minion that
			// died" (:625 Death's Bite + Sludge Belcher); live: inside the DEATHS block (172 of 172 broken weapons), in order of play with
			// the minions (59 weapon first, 14 minion first, 4 in between). Was: every weapon before the minions, outside the block.
			// (Only a weapon still pending destroy: one equipped after the old one broke isn't taken with it.)
			List<Hero> brokenWeapons = null;
			if (ClearWeapons != null)
			{
				foreach (Delegate d in ClearWeapons.GetInvocationList())
					if (d.Target is Hero h && h.Weapon != null && h.Weapon.ToBeDestroyed && !(brokenWeapons?.Contains(h) ?? false))
						(brokenWeapons ??= new List<Hero>()).Add(h);
				ClearWeapons = null;
			}
			void BreakWeaponsBefore(int orderOfPlay)
			{
				if (brokenWeapons == null) return;
				foreach (Hero h in brokenWeapons.OrderBy(x => x.Weapon?.OrderOfPlay ?? 0).ToList())
				{
					if (h.Weapon != null && h.Weapon.OrderOfPlay >= orderOfPlay) continue;
					brokenWeapons.Remove(h);
					if (h.Weapon == null) continue;
					TaskQueue.StartGathering(); // (its Deathrattle and death triggers, as a minion's)
					h.RemoveWeapon();
					TaskQueue.EndGathering();
				}
			}

			// HsServer (2026-11-16): rule 4a — the Death Creation Step "Looks for all mortally wounded (0 or less Health)/pending destroy
			// Entities and kills them": a minion brought to 0 Health without damage (a Health loss: Bloated Leech's steal on a damaged
			// minion) is mortally wounded too, not only one a damage / destroy effect marked (live: the Leech's victims die)
			foreach (Controller p in _players)
				if (p != null)
					foreach (Minion m in p.BoardZone.GetAll())
						if (!m.ToBeDestroyed && m.Health <= 0 && m[GameTag.DORMANT] == 0)
							m.ToBeDestroyed = true;

			// HsServer (2026-10-08): the Death Creation Step fixes a hero's death — "If/when Hearthstone runs the Death Creation Step, your
			// current Hero is mortally wounded or pending destroy, the next time Hearthstone checks for win/loss/death you will lose the
			// game … even if your Hero is later replaced" (Advanced_rulebook.txt:713; a hero replaced in the same Phase lives: :715-717)
			foreach (Controller p in _players)
				if (p?.Hero != null && p.Hero.ToBeDestroyed && p[HsTag.HERO_DEATH_LATCHED] == 0)
					p[HsTag.HERO_DEATH_LATCHED] = 1;

			// HsServer: rules A5/A6 — "mortally wounded" is reversible until now: a minion healed or given Health back above 0
			// in the same Phase lives (Geddon + Healing Totem). A destroy effect is final.
			for (int i = DeadMinions.Count - 1; i >= 0; i--)
			{
				Minion m = DeadMinions[i];
				// an entity that already left play (moved away without resetting) can't die
				if (m.Zone == null)
				{
					DeadMinions.RemoveAt(i);
					continue;
				}
				if (m.MortallyWounded && !m.DestroyedByEffect && m.Health > 0 && m.Zone?.Type == Enums.Zone.PLAY)
				{
					DeadMinions.RemoveAt(i);
					m.ToBeDestroyed = false;
					m.MortallyWounded = false;
				}
			}

			if (DeadMinions.Count > 0 || brokenWeapons != null)
			{
				// HsServer: the DEATHS block was built but never added to the history (live sends it: game entity, index 0).
				if (History)
					PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.DEATHS, Id, "", 0, 0));

				DeadMinions.InsertionSort(GetOrderOfPlay);

				// HsServer: rules A5 — the dead minions' auras stop now (non-stat ones via the Aura Update (Other)), before
				// any of their deaths is resolved: Baron Rivendare dying with the board doesn't double their Deathrattles.
				// (but the Corpses those deaths leave are counted with the doubling the players had as they died: live 20261109-174017 17828,
				// a dying Falric still doubles them)
				foreach (Controller cp in new[] { Player1, Player2 })
					cp.CorpseDoublingInDeaths = cp.ControllerAuraEffects[HsTag.CORPSE_GAINS_DOUBLED];
				foreach (Minion dead in DeadMinions)
					dead.OngoingEffect?.Remove();
				AuraUpdateOther();

				List<Minion> reborn = null;
				foreach (Minion minion in DeadMinions)
				{
					BreakWeaponsBefore(minion.OrderOfPlay);
					Log(LogLevel.INFO, BlockType.PLAY, "Game", !Logging ? "" : $"{minion} is Dead! Graveyard say 'Hello'!");

					// HsServer: rules A8 Rule 7 — this death's responders (on-death triggers/Secrets, its Deathrattle, the
					// Deathrattles enchantments gave it) are queued in order of play; Redemption-like Priority last.
					TaskQueue.StartGathering();

					// HsServer: a Location that breaks is not a minion dying: no minion death triggers, kill counters, Corpse
					// or Infuse; its own Deathrattle still fires.
					bool isMinion = !(minion is Location);

					// Death event created
					if (isMinion)
						TriggerManager.OnDeathTrigger(minion);

					// HsServer: live order — the kill counters, then the move to the graveyard, then the friendly-died counter.
					if (isMinion)
					{
						CurrentPlayer.NumMinionsPlayerKilledThisTurn++;
						NumMinionsKilledThisTurn++;
					}
					bool instant = minion.InstantlyRemoved; // already in the Graveyard (rules C2 Z1)
					if (!instant)
					{
						minion.LastBoardPosition = minion.ZonePosition;
						minion.Zone.Remove(minion);
					}
					minion[HsTag.LAST_ATTACK_IN_PLAY] = minion.AttackDamage; // HsServer (CATACLYSM): before the graveyard strips its enchantments
					// HsServer: a friendly minion that dies leaves its owner a Corpse, before its Deathrattle - unless it was
					// "raised" (client tag 2217: "Doesn't leave a Corpse").
					if (isMinion && minion.Card[(GameTag)2217] == 0)
						minion.Controller.GainCorpses(1);
					if (isMinion && minion.IsRace(Race.UNDEAD))
						minion.Controller.LastUndeadDeathTurn = Turn;
					if (isMinion)
						InfuseStep(minion); // HsServer: Infuse counts friendly minion deaths

					// HsServer (SPACE): a Starship Piece joins its owner's Starship before its Deathrattle (live 140614 Arkonite: the
					// 3684 TRIGGER block, then the Deathrattle at effect index 1)
					if (isMinion && Actions.Generic.IsStarshipPiece(minion))
						TaskQueue.Enqueue(Actions.Generic.StarshipPieceTask, minion.Controller, minion, null, (int)Actions.Generic.STARSHIP_PIECE_KEYWORD);

					if (minion.HasDeathrattle)
						for (int times = minion.Controller.DeathrattleTimes(minion); times > 0; times--)
							minion.ActivateTask(PowerActivation.DEATHRATTLE);

					if (minion[GameTag.REBORN] > 0) // (HsServer 2026-10-08: Silence clears the tag itself; Reborn given after a Silence works — wiki Silence.txt:27)
					{
						(reborn ??= new List<Minion>()).Add(minion);
						// HsServer (VIOLET_HOLD): Sinful Steed — its copy before the graveyard strips it (live: in the DEATHS block)
						if (minion[Actions.Generic.FULL_HEALTH_REBORN] > 0 && !instant)
							minion[HsTag.REBORN_SNAPSHOT] = Actions.Generic.Copy(minion.Controller, minion, minion, Enums.Zone.SETASIDE, keepEnchantments: true).Id;
					}

					if (!instant)
						minion.Controller.GraveyardZone.Add(minion); // strips enchantments: their Deathrattles queue here
					minion.InstantlyRemoved = false;
					TaskQueue.EndGathering();
					if (isMinion)
					{
						minion.Controller.DiedThisGame.Add(minion.Id); // HsServer: the one "died this game" source
						minion.Controller.NumFriendlyMinionsThatDiedThisTurn++;
						minion.Controller.NumFriendlyMinionsThatDiedThisGame++; // HsServer: live NUM_FRIENDLY_MINIONS_THAT_DIED_THIS_GAME
					}
					// HsServer: live clears a dead minion's DAMAGE and REBORN in its DEATHS block, after it reaches the graveyard
					// (281 of 320 deaths in the live captures; 0 of ours). Returns/copies are new entities, nothing reads them.
					if (!instant && minion.Zone?.Type == Enums.Zone.GRAVEYARD)
					{
						if (minion.Damage != 0) minion.Damage = 0;
						if (minion[GameTag.REBORN] != 0) minion[GameTag.REBORN] = 0;
					}

					//minion.Damage = 0;
					//minion.IsExhausted = false;

					// should remove tags of dead cards for faster cloning
				}
				foreach (Controller cp in new[] { Player1, Player2 })
					cp.CorpseDoublingInDeaths = null;
				BreakWeaponsBefore(int.MaxValue);

				// HsServer: Reborn — after every Deathrattle of this Death Phase (rules D2, user rule 2026-09-30: Sylvanas
				// can't steal a Khartut that died with her). The first time it dies (not silenced) it returns where it was
				// as a fresh copy: 1 current Health, its maximum Health the card's. Live: FULL_ENTITY with
				// START_WITH_1_HEALTH, REBORN, HAS_BEEN_REBORN, CREATOR and 1254 = the dead minion; then
				// START_WITH_1_HEALTH and REBORN go to 0.
				if (reborn != null)
					foreach (Minion minion in reborn)
						TaskQueue.Enqueue(RebornTask, minion.Controller, minion, null);

				if (History)
					PowerHistory.Add(PowerHistoryBuilder.BlockEnd());

				DeadMinions.Clear();

				// Death Creation Step done: the dead minions' non-stat auras go before their Deathrattles resolve.
				AuraUpdateOther();
			}

			// HsServer: a dead hero doesn't end the game here. Win/loss/draw is checked when the Sequence ends
			// (CheckGameOver), so both heroes dying in one play is a draw (Advanced rulebook, Rule 9).
		}

		/// <summary>
		/// HsServer: the win/loss/draw check (Advanced rulebook, Rule 9). It runs at the end of a Sequence (a player's move,
		/// each turn step) and between combat's Preparation and Combat steps, never in the middle of a Phase: both heroes
		/// dying in the same Sequence is a draw. Returns true if the game ended.
		/// </summary>
		/// <summary>HsServer: the entity is in play at this moment (for effect blocks' TARGET, PowerHistory.InPlay).</summary>
		private bool InPlayNow(int id) => IdEntityDic.TryGetValue(id, out IPlayable e) && e[GameTag.ZONE] == (int)Enums.Zone.PLAY;

		public bool CheckGameOver()
		{
			// HsServer (2026-10-08): a player loses when its current hero is dead or its hero died at a Death Creation Step (latched: a
			// replacement after that doesn't save it — Advanced_rulebook.txt:713, :717)
			bool Dead(Controller p) => p.Hero.ToBeDestroyed || p[HsTag.HERO_DEATH_LATCHED] > 0;
			if (State == State.COMPLETE || !(Dead(Player1) || Dead(Player2)))
				return false;
			// HsServer (TIME_TRAVEL): a hero's "Deathrattle: Spend up to N Corpses to resurrect with that much Health." (Husk; wiki: through
			// damage and destroy effects; 0 Corpses: the hero dies; one use)
			foreach (Controller p in new[] { Player1, Player2 })
			{
				Actions.Generic.HeroResurrect(p);
				Actions.Generic.MinionTakesOverDeadHero(p); // HsServer (Dalaran Heist): The Rat King's Underbelly Rats
			}
			if (!(Dead(Player1) || Dead(Player2)))
				return false;
			if (Dead(Player1) && Dead(Player2))
			{
				Player1.PlayState = PlayState.TIED;
				Player2.PlayState = PlayState.TIED;
			}
			else if (Dead(Player1))
				Player1.PlayState = PlayState.LOSING;
			else
				Player2.PlayState = PlayState.LOSING;
			NextStep = Step.FINAL_WRAPUP;
			return true;
		}

		/// <summary>HsServer: the Attack of the minions that infused a card so far (internal; Sinfueled Golem).</summary>
		public const GameTag INFUSE_ATTACK_SUM = (GameTag)990005;

		/// <summary>Client tags of Infuse: the keyword, "already infused", and the infused version's DBF id.</summary>
		public const GameTag INFUSE = (GameTag)2456, INFUSED = (GameTag)2457, COLLECTION_RELATED_CARD_DATABASE_ID = (GameTag)1452;


		/// <summary>HsServer (internal tags): "This minion's Battlecries trigger twice" and "When you play this minion, summon a 2/2
		/// copy of it" (Dark Gifts Rude Awakening and Living Nightmare), read by PlayCard.</summary>
		public const GameTag DOUBLE_BATTLECRY = (GameTag)990010, LIVING_NIGHTMARE = (GameTag)990011;


		/// <summary>HsServer: the minion type an Infuse card counts, from its client tag ("Infuse (3 Beasts)"); INVALID = any.</summary>
		private static Race InfuseRace(Card card) =>
			card[HsTag.INFUSE_BEASTS] > 0 ? Race.BEAST : card[HsTag.INFUSE_TOTEMS] > 0 ? Race.TOTEM : card[HsTag.INFUSE_DEMONS] > 0 ? Race.DEMON : Race.INVALID;


		/// <summary>
		/// HsServer: Infuse — a friendly minion died: every not-yet-infused Infuse card in its owner's hand (and deck, with
		/// Afterlife Attendant) counts down (TAG_SCRIPT_DATA_NUM_1, which the client shows) and becomes its infused version
		/// (COLLECTION_RELATED_CARD_DATABASE_ID) at 0. Cards whose infused effect is scripted in place just get INFUSED.
		/// </summary>
		private void InfuseStep(Minion died)
		{
			Controller c = died.Controller;
			IEnumerable<IPlayable> cards = c.HandZone;
			if (c.ControllerAuraEffects[HsTag.INFUSE_IN_DECK] > 0) // Afterlife Attendant's aura
				cards = cards.Concat(c.DeckZone);
			foreach (IPlayable card in cards.ToList())
			{
				IPlayable p = card;
				bool endless = p.Card.Power?.EndlessInfuse == true;
				int stages = p.Card.Power?.InfuseStages ?? 1;
				if (p.Card[INFUSE] == 0 || (!endless && p[INFUSED] >= stages)) continue;
				Race race = InfuseRace(p.Card);
				if (race != Race.INVALID && !died.IsRace(race)) continue;
				p[INFUSE_ATTACK_SUM] = p[INFUSE_ATTACK_SUM] + died.AttackDamage; // Sinfueled Golem
				int left = p[GameTag.TAG_SCRIPT_DATA_NUM_1] - 1;
				if (left > 0)
				{
					p[GameTag.TAG_SCRIPT_DATA_NUM_1] = left;
					continue;
				}
				if (endless && p[INFUSED] > 0) // already infused: one more step
				{
					p[GameTag.TAG_SCRIPT_DATA_NUM_2] = p[GameTag.TAG_SCRIPT_DATA_NUM_2] + 1;
					p[GameTag.TAG_SCRIPT_DATA_NUM_1] = p.Card[GameTag.TAG_SCRIPT_DATA_NUM_1];
					continue;
				}
				if (stages == 2 && p[INFUSED] == 0) // first of two thresholds, in place
				{
					p[INFUSED] = 1;
					p[GameTag.TAG_SCRIPT_DATA_NUM_1] = System.Math.Max(1, p.Card[GameTag.TAG_SCRIPT_DATA_NUM_2] - p.Card[GameTag.TAG_SCRIPT_DATA_NUM_1]);
					continue;
				}
				int dbf = p.Card[COLLECTION_RELATED_CARD_DATABASE_ID];
				Card infused = dbf > 0 ? Cards.FromAssetId(dbf) : null;
				if (infused != null && infused.Type == p.Card.Type)
					p = Actions.Generic.ChangeEntityBlock(c, p, infused, false);
				p[GameTag.TAG_SCRIPT_DATA_NUM_1] = p.Card.Power?.EndlessInfuse == true ? p.Card[GameTag.TAG_SCRIPT_DATA_NUM_1] : 0;
				p[INFUSED] = stages;
			}
		}

		/// <summary>
		/// Update the auras on each zone (which can be influenced by entities from the
		/// <see cref="Zone.PLAY"/> zone.
		/// </summary>
		public void AuraUpdate()
		{
			List<IAura> auras = Auras;
			for (int i = auras.Count - 1; i >= 0; i--)
				auras[i].Update();
			HeroPowerUses(Player1);
			HeroPowerUses(Player2);
		}

		/// <summary>
		/// HsServer: a Hero Power used this turn is exhausted once its uses exceed 1 + HEROPOWER_ADDITIONAL_ACTIVATIONS (the
		/// player's aura: Garrison Commander 1, Coldarra Drake "any number of times"). Checked after every aura update, so
		/// one arriving after the Hero Power was used makes it usable again; it never exhausts it.
		/// </summary>
		private static void HeroPowerUses(Controller c)
		{
			if (c?.Hero?.HeroPower is not { } power) return;
			int used = power[GameTag.HEROPOWER_ACTIVATIONS_THIS_TURN];
			if (used == 0) return;
			// only ever makes it usable again (a refresh — Shadowreaper Anduin, Beardo — must not be undone here; using it
			// exhausts it in HeroPowerBlock)
			if (power.IsExhausted && used <= c[GameTag.HEROPOWER_ADDITIONAL_ACTIVATIONS])
				power.IsExhausted = false;
		}

		/// <summary>
		/// HsServer: rules A2 Rule 1a — an Event raised in the middle of something resolves its triggers at once
		/// (depth-first), then resolution resumes. Inside an area effect (see TaskQueue.StartBatch) they wait with the
		/// hit they belong to.
		/// </summary>
		internal void RaiseEvent(Action raise)
		{
			TaskQueue.StartEvent();
			raise();
			if (!TaskQueue.DeferCurrentEvent(CurrentEventData))
				ProcessTasks();
			TaskQueue.EndEvent();
		}

		// HsServer: open SUB_SPELLs (card-specific effect prefabs, Loader.SubSpells), innermost last.
		private readonly List<(int Source, string Prefab)> _openSubSpells = new List<(int, string)>();

		/// <summary>HsServer: some effect's SUB_SPELL is open (a card made now arrives inside it).</summary>
		internal bool AnySubSpellOpen => _openSubSpells.Count > 0;

		/// <summary>HsServer (2026-11-14): live's plain to-hand flight, from its source (live: Low Security Wing JAIL_987, JAIL_436, JAIL_460, CATA_556, BE_541).</summary>
		public const string GenericSpawnToHand = "ReuseFX_Generic_SpawnToHand_NoFX_Super:c61f30a3c08282d43ad7008c180d9e4c";

		/// <summary>
		/// HsServer: open a SUB_SPELL for <paramref name="source"/>'s effect prefab (no-op without history). A new role
		/// of the same source closes the one before (live: Zuramat's Prison's discard missile, then its spawn).
		/// Returns whether one was opened.
		/// </summary>
		/// <summary>HsServer: <see cref="OpenSubSpell"/>'s shownSource for "no source" (Source = 0 on the wire).</summary>
		internal const int NoSubSpellSource = -1;

		// HsServer: a card's effects held back until their action (OpenSubSpellAtFirstBuff, OpenSubSpellAt): each opens before the
		// first history entry of its kind in the card's block (2026-10-08: a list — a card's missile, then its buff, then its deck
		// effect, each where live played it: RecordedSubSpellAnchors.tsv)
		private sealed class PendingFx
		{
			public IPlayable Source;
			public string Prefab;
			public int[] Targets;
			public int Shown;
			public bool Collect;
			public Loader.SubSpells.Role Kind;
			/// <summary>From a recording (RecordedSubSpellAnchors.tsv): opens again at each new round of its action, and not at all
			/// when the action never comes.</summary>
			public bool Recorded;
			public bool Opened;
			/// <summary>The card's block it belongs to (a recorded one): it ends with it.</summary>
			public PowerHistoryBlockStart Block;
		}

		private readonly List<PendingFx> _pendingFx = new List<PendingFx>();
		// recorded empty effects played in blocks still running: engine code naming the same effect later in the block takes it back
		private readonly List<(PowerHistoryBlockStart Block, string Prefab, PowerHistorySubSpellStart Start, PowerHistorySubSpellEnd End)> _playedMarkers =
			new List<(PowerHistoryBlockStart, string, PowerHistorySubSpellStart, PowerHistorySubSpellEnd)>();
		// (opening a held-back effect itself: not engine code naming one)
		private bool _openingPending;

		/// <summary>
		/// HsServer: a card's buff effect (Loader.SubSpells Role.Buff) belongs to its buff step: it opens right before the
		/// block's first buff — an enchantment attached, a stat / Armor raised, a keyword given, a cost changed — so a step
		/// before it plays first (live Focused Will: the Silence, then ReuseFX_BuffImpact_HolyLight around the +3 Health;
		/// rule: effects in the order of the text). A card that only buffs opens it at once, as before; one that never buffs
		/// opens it (empty) at its block's end (FlushPendingFx).
		/// </summary>
		internal void OpenSubSpellAtFirstBuff(IPlayable source, string prefab, int[] targets, int shownSource, bool collectTargets) =>
			OpenSubSpellAt(Loader.SubSpells.Role.Buff, source, prefab, targets, shownSource, collectTargets);

		/// <summary>
		/// HsServer (2026-10-08): a card's effect held back until the first action of its kind in the card's block — Buff (a buff or
		/// heal: IsBuff), Heal, Damage (the first damage pulse), ToDeck (a card into a deck), Draw (a card from the deck to the hand),
		/// Transform (a CHANGE_ENTITY) — where live played it (RecordedSubSpellAnchors.tsv: what the effect wrapped). One that never
		/// meets its action opens (empty) at the block's end, as a buff effect did.
		/// </summary>
		/// <remarks>Recorded ones (<paramref name="recorded"/>, 2026-10-08): a Marker (live sent it empty) plays right before the first
		/// thing the card's block does; the others open again at each new round of their action (live: a repeated round plays the
		/// effect again — Blood Tap's hand buff before and after its Corpse spend) and are dropped when their action never comes.
		/// Engine code that names the same effect itself (OpenSubSpell: Herald, Shatter, Draw-and-hold…) wins: the recorded one goes.</remarks>
		internal void OpenSubSpellAt(Loader.SubSpells.Role kind, IPlayable source, string prefab, int[] targets, int shownSource, bool collectTargets,
			bool recorded = false)
		{
			if (!History || prefab == null) return;
			_pendingFx.Add(new PendingFx
			{
				Source = source, Prefab = prefab, Targets = targets, Shown = shownSource, Collect = collectTargets, Kind = kind, Recorded = recorded,
			});
		}

		/// <summary>HsServer: the engine's held-back effects of <paramref name="sourceId"/> at its task's end: a buff effect that never met
		/// its buff opens (empty). (Recorded ones end with their block: EndRecordedFx.)</summary>
		internal void FlushPendingFx(int sourceId)
		{
			for (int i = 0; i < _pendingFx.Count; i++)
			{
				PendingFx p = _pendingFx[i];
				if (p.Source.Id != sourceId || p.Recorded) continue;
				_pendingFx.RemoveAt(i--);
				OpenPending(p);
			}
		}

		/// <summary>
		/// HsServer (2026-10-08): a card's POWER / TRIGGER block starts — its effects live played in this kind of block
		/// (RecordedSubSpellAnchors.tsv: what each one wrapped) wait in it: an empty one ("marker") plays before anything else of
		/// the block, the others before the first action of what they wrapped (a summon, a card into the hand or deck, a draw, a
		/// destroy, damage, a heal, a buff, a transform) and again at each new round of it. Engine code placing the same effect itself
		/// (a summon's, a to-hand group's, a missile at its target, a card's script) opens it before its action and replaces the
		/// recorded one (OpenSubSpell). Not the effects placed by their own steps (Deathrattle, Corpses, Counter), not a Titan's (its
		/// abilities place theirs), not a card placing its own (OwnFx). Every path that writes a card's block gets them.
		/// </summary>
		/// <summary>HsServer (2026-10-08): the next attack is ordered by a player (MinionAttackTask / HeroAttackTask), not forced by an effect.</summary>
		internal bool AttackOrdered;

		/// <summary>HsServer (2026-10-08): client tags — a minion's last known Attack / Health in hand (live; TAG_LAST_KNOWN_COST_IN_HAND's siblings).</summary>
		public const GameTag LAST_KNOWN_ATK_IN_HAND = (GameTag)4472, LAST_KNOWN_HEALTH_IN_HAND = (GameTag)4997;

		/// <summary>HsServer (2026-10-08): Lifesteal heals held during an attack's two hits (Generic.AttackBlock pays them after both), else null.</summary>
		internal List<(IPlayable Source, int Amount)> DeferredLifesteal;
		/// <summary>HsServer (2026-10-08): in combat, the Poisonous kills wait until both hits are dealt (live: DAMAGE, DAMAGE, then the
		/// POISONOUS block — Generic.PoisonousKill).</summary>
		internal List<(IPlayable Source, Minion Target)> DeferredPoison;

		/// <summary>HsServer (2026-10-08): the Titan ability whose block is starting (Generic.UseTitanAbility), else null.</summary>
		internal string TitanAbilityKey;
		/// <summary>HsServer (2026-10-09): the Titan whose ability is resolving (0 = none) — its damage breaks its Stealth (Character).</summary>
		internal int ActingTitanId;

		private void RecordedFxAt(PowerHistoryBlockStart b, bool markers = true)
		{
			if (b.BlockType is not (BlockType.POWER or BlockType.TRIGGER) || !IdEntityDic.TryGetValue(b.Source, out IPlayable card))
				return;
			// HsServer (2026-10-08): a block running another card's effect (EffectCardId: Macaw, Shudderwock) waits for THAT card's effects
			Card fxCard = !string.IsNullOrEmpty(b.EffectCardId) && Cards.TryFromId(b.EffectCardId) is Card effectCard ? effectCard : card.Card;
			if (fxCard.Power?.OwnFx == true)
				return;
			// A Titan: live plays each ability in the Titan's own POWER block, so its recorded effects are told apart by the ability
			// their names carry (TTNFX_RunesOfBlood_…): an ability's block gets that ability's; its other blocks only the effects of
			// no ability (Eonar's summons, Aman'Thul's Discover trigger).
			string ability = fxCard.IsTitan ? TitanAbilityKey : null;
			string[] abilityKeys = fxCard.IsTitan ? Cards.TitanAbilities(fxCard).Select(Actions.Generic.AbilityKey).ToArray() : null;
			// (a deck card's trigger block without a keyword is its Start of Game reveal, not its effect: live Beatrix, nothing in it)
			if (b.BlockType == BlockType.TRIGGER && b.TriggerKeyword == 0 && card.Zone?.Type == Enums.Zone.DECK)
				return;
			string kind = b.BlockType == BlockType.POWER ? "POWER" : b.TriggerKeyword == (int)GameTag.DEATHRATTLE ? "DEATHRATTLE" : "TRIGGER";
			IPlayable target = b.Target > 0 && IdEntityDic.TryGetValue(b.Target, out IPlayable t) ? t : null;
			int shown = card is Spell && card.Controller?.Hero != null ? card.Controller.Hero.Id : 0;
			// a card nobody recorded: its own deck effect ("…SpawnToDeck…", "…DeckAE…": the client lists it, the server names it) waits
			// for its card going into a deck, in the block its name says (Trigger / EndOfTurn, Deathrattle, else its play) — live:
			// every recorded deck effect wraps the move (RecordedSubSpellAnchors.tsv "todeck")
			if (Loader.SubSpells.Anchors(fxCard.Id).Count == 0)
				foreach (string prefab in Loader.SubSpells.Prefabs(fxCard.Id))
				{
					if (Loader.SubSpells.RoleOf(prefab) != Loader.SubSpells.Role.ToDeck) continue;
					string n = prefab.Split(':')[0];
					string its = n.Contains("Deathrattle", StringComparison.OrdinalIgnoreCase) ? "DEATHRATTLE"
						: n.Contains("Trigger", StringComparison.OrdinalIgnoreCase) || n.Contains("EndOfTurn", StringComparison.OrdinalIgnoreCase) ? "TRIGGER" : "POWER";
					if (its != kind) continue;
					_pendingFx.Add(new PendingFx { Source = card, Prefab = prefab, Kind = Loader.SubSpells.Role.ToDeck, Recorded = true, Block = b, Shown = shown });
				}
			foreach (Loader.SubSpells.Anchor a in Loader.SubSpells.Anchors(fxCard.Id, kind))
			{
				if (Loader.SubSpells.RoleOf(a.Prefab) is Loader.SubSpells.Role.Deathrattle or Loader.SubSpells.Role.ConsumeCorpse
				    or Loader.SubSpells.Role.GainCorpse or Loader.SubSpells.Role.Counter)
					continue;
				if (!markers && a.Kind == "marker") continue;
				// (an effect naming no ability that waits for an action — Amitus' draw-and-hold, Aggramar's weapon buff — fits any ability
				// block: it plays only if that ability does its action; an empty one naming none stays out of them)
				if (abilityKeys != null &&
				    (ability != null
					    ? !Actions.Generic.EffectOfAbility(a.Name, ability) &&
					      (a.Kind == "marker" || abilityKeys.Any(k => Actions.Generic.EffectOfAbility(a.Name, k)))
					    : abilityKeys.Any(k => Actions.Generic.EffectOfAbility(a.Name, k))))
					continue;
				Loader.SubSpells.Role role = a.Kind == "marker" ? Loader.SubSpells.Role.Marker : Loader.SubSpells.RoleOfAnchor(a.Kind);
				bool area = role is Loader.SubSpells.Role.Buff or Loader.SubSpells.Role.Damage or Loader.SubSpells.Role.Heal or Loader.SubSpells.Role.Transform;
				_pendingFx.Add(new PendingFx
				{
					Source = card, Prefab = a.Prefab, Kind = role, Recorded = true, Block = b, Shown = shown,
					Targets = role == Loader.SubSpells.Role.Marker ? MarkerTargets(a.Targets, card, target) : target != null && area ? new[] { target.Id } : null,
					Collect = target == null && area,
				});
			}
		}

		/// <summary>HsServer (2026-10-08): a recorded marker effect's targets (RecordedSubSpellAnchors.tsv: none / self / hero / enemyhero /
		/// target / other, "+"-joined) for this play: "other" is the block's target when it has one.</summary>
		private static int[] MarkerTargets(string rel, IPlayable source, IPlayable target)
		{
			var ids = new List<int>();
			Controller controller = source.Controller;
			foreach (string r in (rel ?? "").Split('+'))
				switch (r)
				{
					case "self": ids.Add(source.Id); break;
					case "hero" when controller?.Hero != null: ids.Add(controller.Hero.Id); break;
					case "enemyhero" when controller?.Opponent?.Hero != null: ids.Add(controller.Opponent.Hero.Id); break;
					case "target" or "other" when target != null: ids.Add(target.Id); break;
				}
			return ids.Count > 0 ? ids.Distinct().ToArray() : null;
		}

		/// <summary>HsServer (2026-10-08): a block ends: its recorded markers play if nothing else of it came; its recorded effects whose
		/// action never came are dropped (live: no action, no effect).</summary>
		private void EndRecordedFx(PowerHistoryBlockStart block)
		{
			if (block == null || _pendingFx.Count == 0 && _playedMarkers.Count == 0) return;
			PlayMarkers(block);
			_pendingFx.RemoveAll(p => p.Recorded && p.Block == block);
			_playedMarkers.RemoveAll(m => m.Block == block);
		}

		/// <summary>HsServer (2026-10-08): the recorded empty effects of <paramref name="block"/> (Role.Marker), in order: each opened and
		/// closed at once, before anything else of its block.</summary>
		private void PlayMarkers(PowerHistoryBlockStart block)
		{
			if (block == null) return;
			for (int i = 0; i < _pendingFx.Count; i++)
			{
				PendingFx p = _pendingFx[i];
				if (p.Block != block || p.Kind != Loader.SubSpells.Role.Marker) continue;
				_pendingFx.RemoveAt(i--);
				if (!OpenPending(p)) continue;
				int at = _openSubSpells.FindLastIndex(x => x.Source == p.Source.Id && x.Prefab == p.Prefab);
				if (at < 0) continue;
				var start = PowerHistory.Full.LastOrDefault(x => x is PowerHistorySubSpellStart s && s.Prefab == p.Prefab) as PowerHistorySubSpellStart;
				var end = new PowerHistorySubSpellEnd();
				PowerHistory.Add(end);
				_openSubSpells.RemoveAt(at);
				if (start != null) _playedMarkers.Add((block, p.Prefab, start, end));
			}
		}

		private bool OpenPending(PendingFx p)
		{
			_openingPending = true;
			try { return OpenSubSpell(p.Source, p.Prefab, p.Targets, p.Shown, p.Collect); }
			finally { _openingPending = false; }
		}

		// HsServer (2026-10-08): players whose deck an effect shuffled cards into, waiting for its block's end (one SHUFFLE_DECK each: live)
		private readonly List<int> _pendingShuffles = new List<int>();

		/// <summary>HsServer (2026-10-08): a card was shuffled into <paramref name="playerId"/>'s deck: its SHUFFLE_DECK comes once, at the
		/// block's end (live: one per shuffle, after all its cards); outside a block, at once.</summary>
		internal void NoteShuffle(int playerId)
		{
			if (!History) return;
			if (PowerHistory.InnermostBlock == null)
			{
				PowerHistory.Add(PowerHistoryBuilder.ShuffleDeck(playerId));
				return;
			}
			if (!_pendingShuffles.Contains(playerId)) _pendingShuffles.Add(playerId);
		}

		private void BeforeHistoryAdd(IPowerHistoryEntry e)
		{
			if (e is PowerHistoryBlockEnd && _pendingShuffles.Count > 0)
			{
				var shuffled = _pendingShuffles.ToList();
				_pendingShuffles.Clear();
				foreach (int player in shuffled) PowerHistory.Add(PowerHistoryBuilder.ShuffleDeck(player));
			}
			if (e is PowerHistoryBlockEnd) EndRecordedFx(PowerHistory.InnermostBlock);
			CloseSpawnGroupBefore(e);
			OpenPendingFxBefore(e);
			if (e is PowerHistoryBlockStart started) RecordedFxAt(started);
		}

		// HsServer: the creator's to-hand effect kept open while its effect adds more cards (JoinSpawnGroup)
		private (int Source, HashSet<int> Members)? _spawnGroup;
		private bool _closingSpawnGroup;

		/// <summary>
		/// HsServer: a card an effect creates flies into the hand inside its creator's SpawnToHand SUB_SPELL; the cards one
		/// effect adds together share one (live: Thoughtsteal-like "get two / three cards" — Mend, Yogg, Cataclysm cards,
		/// Selective Breeder…: one SUB_SPELL around all their FULL_ENTITYs, they arrive at once; RESEARCH/captures scan
		/// 2026-10-06). It stays open while only these cards (and what attaches to them) come; anything else closes it — so
		/// separate instances ("casts twice": Molten Rune) get one each. False: no effect for it.
		/// </summary>
		internal bool JoinSpawnGroup(IPlayable creator, string prefab, int cardId)
		{
			if (!History || prefab == null || creator == null) return false;
			if (_spawnGroup is { } open && open.Source == creator.Id && _openSubSpells.Exists(x => x.Source == creator.Id && x.Prefab == prefab))
			{
				open.Members.Add(cardId);
				return true;
			}
			CloseSpawnGroup();
			if (!OpenSubSpell(creator, prefab, null)) return false;
			_spawnGroup = (creator.Id, new HashSet<int> { cardId });
			return true;
		}

		/// <summary>HsServer: the open to-hand effect (JoinSpawnGroup) ends.</summary>
		internal void CloseSpawnGroup()
		{
			if (_spawnGroup is not { } open) return;
			_spawnGroup = null;
			_closingSpawnGroup = true;
			CloseSubSpells(open.Source);
			_closingSpawnGroup = false;
		}

		private void CloseSpawnGroupBefore(IPowerHistoryEntry e)
		{
			if (_spawnGroup is not { } open || _closingSpawnGroup) return;
			if (e is PowerHistorySubSpellEnd)
			{
				_spawnGroup = null; // its effect is being closed by its source (the end of its task): nothing left to close
				return;
			}
			bool Mine(IDictionary<GameTag, int> tags, int id)
			{
				if (open.Members.Contains(id)) return true;
				if (tags != null && (tags.TryGetValue(GameTag.ATTACHED, out int on) && open.Members.Contains(on) ||
				                     tags.TryGetValue(GameTag.CREATOR, out int by) && by == open.Source ||
				                     !tags.TryGetValue(GameTag.ZONE, out int zone) || zone == 0)) // (a card just made, in no zone yet: the next copy)
				{
					open.Members.Add(id); // its enchantment, or the next card this effect makes
					return true;
				}
				return false;
			}
			switch (e)
			{
				case PowerHistoryFullEntity { Entity: { } f } when Mine(f.Tags, f.Id):
				case PowerHistoryShowEntity { Entity: { } s } when Mine(s.Tags, s.Id):
				case PowerHistoryTagChange t when open.Members.Contains(t.EntityId):
				case PowerHistoryMetaData { Info: { Count: > 0 } info } when info.TrueForAll(open.Members.Contains):
					return;
			}
			CloseSpawnGroup();
		}

		private void OpenPendingFxBefore(IPowerHistoryEntry e)
		{
			if (_pendingFx.Count == 0 || _openingPending || e is PowerHistorySubSpellStart || e is PowerHistorySubSpellEnd) return;
			int running = PowerHistory.CurrentEffectSource;
			PowerHistoryBlockStart block = PowerHistory.CurrentEffectBlock;
			PlayMarkers(block);
			for (int i = 0; i < _pendingFx.Count; i++)
			{
				PendingFx p = _pendingFx[i];
				if ((p.Recorded ? p.Block != block : p.Source.Id != running) || !IsKind(p.Kind, e, p.Recorded, p.Prefab)) continue;
				if (p.Recorded)
				{
					// (open already: this is the same round)
					if (_openSubSpells.Exists(x => x.Source == p.Source.Id && x.Prefab == p.Prefab)) continue;
					p.Opened = true;
				}
				else
					_pendingFx.RemoveAt(i);
				OpenPending(p);
				return;
			}
		}

		/// <summary>HsServer (2026-10-08): is the history entry the action a held-back effect of this kind waits for?</summary>
		private bool IsKind(Loader.SubSpells.Role kind, IPowerHistoryEntry e, bool recorded, string prefab = null)
		{
			int Known(int id, GameTag tag) =>
				PowerHistory.Reported.TryGetValue(id, out var tags) && tags.TryGetValue(tag, out int v) ? v : 0;
			switch (kind)
			{
				case Loader.SubSpells.Role.Buff:
					// (a recorded "buff" is what live saw change inside it: a Freeze / Immune too — Ice Lance, Frost Lance)
					return IsBuff(e) || recorded && e is PowerHistoryTagChange { Tag: GameTag.FROZEN or GameTag.IMMUNE, Value: 1 } ||
					       // (2026-10-08: an AREA effect wraps the Silences too — live Smothering Starfish's AE_Shockwave; a buff IMPACT doesn't: live
					       // Focused Will silences before its BuffImpact)
					       recorded && prefab != null && prefab.Contains("_AE_", StringComparison.Ordinal) && e is PowerHistoryTagChange { Tag: GameTag.SILENCED, Value: 1 } ||
					       // (a Silence's own impact wraps the whole Silence: its 1066 pulse, the keywords going, SILENCED — live Royal Librarian; a
					       // Debuff impact too — live Servant of Zuramat BE_228's Impact_Debuff_Humility)
					       recorded && prefab != null && (prefab.Contains("Silence", StringComparison.OrdinalIgnoreCase) || prefab.Contains("_Debuff_", StringComparison.Ordinal)) &&
					       e is PowerHistoryTagChange { Tag: GameTag.SILENCED or (GameTag)1066, Value: 1 };
				case Loader.SubSpells.Role.Heal:
					return e is PowerHistoryTagChange { Tag: GameTag.PREHEALING, Value: > 0 } || e is PowerHistoryMetaData { Type: MetaDataType.HEALING };
				case Loader.SubSpells.Role.Damage:
					// (live: the effect opens before the damage's first pulse — tag 2187 = the source, then PREDAMAGE)
					return e is PowerHistoryTagChange { Tag: (GameTag)2187, Value: > 0 } || e is PowerHistoryTagChange { Tag: GameTag.PREDAMAGE, Value: > 0 } ||
					       e is PowerHistoryMetaData { Type: MetaDataType.DAMAGE };
				case Loader.SubSpells.Role.ToDeck:
					return e is PowerHistoryTagChange { Tag: GameTag.ZONE, Value: (int)Enums.Zone.DECK } ||
					       e is PowerHistoryFullEntity { Entity: { } f } && f.Tags.TryGetValue(GameTag.ZONE, out int fz) && fz == (int)Enums.Zone.DECK ||
					       e is PowerHistoryShuffleDeck || e is PowerHistoryHideEntity { Zone: Enums.Zone.DECK };
				case Loader.SubSpells.Role.Draw:
					return e is PowerHistoryTagChange { Tag: GameTag.ZONE, Value: (int)Enums.Zone.HAND } t && Known(t.EntityId, GameTag.ZONE) == (int)Enums.Zone.DECK ||
					       // (a draw shows the card into the hand)
					       e is PowerHistoryShowEntity { Entity: { } sd } && sd.Tags.TryGetValue(GameTag.ZONE, out int sdz) && sdz == (int)Enums.Zone.HAND &&
					       Known(sd.Id, GameTag.ZONE) == (int)Enums.Zone.DECK;
				case Loader.SubSpells.Role.Transform:
					return e is PowerHistoryChangeEntity;
				case Loader.SubSpells.Role.Spawn:
					return e is PowerHistoryTagChange { Tag: GameTag.ZONE, Value: (int)Enums.Zone.PLAY } sz && Known(sz.EntityId, GameTag.ZONE) != (int)Enums.Zone.PLAY &&
					       IdEntityDic.TryGetValue(sz.EntityId, out IPlayable summoned) && summoned is Minion ||
					       e is PowerHistoryFullEntity { Entity: { } fs } && fs.Tags.TryGetValue(GameTag.ZONE, out int fsz) && fsz == (int)Enums.Zone.PLAY &&
					       IdEntityDic.TryGetValue(fs.Id, out IPlayable made) && made is Minion;
				case Loader.SubSpells.Role.SpawnToHand:
					return e is PowerHistoryTagChange { Tag: GameTag.ZONE, Value: (int)Enums.Zone.HAND } th &&
					       Known(th.EntityId, GameTag.ZONE) is not ((int)Enums.Zone.DECK or (int)Enums.Zone.HAND) ||
					       e is PowerHistoryFullEntity { Entity: { } fh } && fh.Tags.TryGetValue(GameTag.ZONE, out int fhz) && fhz == (int)Enums.Zone.HAND;
				case Loader.SubSpells.Role.Destroy:
					return e is PowerHistoryTagChange { Tag: GameTag.TO_BE_DESTROYED, Value: 1 };
			}
			return false;
		}

		private bool IsBuff(IPowerHistoryEntry e)
		{
			int Known(int id, GameTag tag) =>
				PowerHistory.Reported.TryGetValue(id, out var tags) && tags.TryGetValue(tag, out int v) ? v : 0;
			switch (e)
			{
				case PowerHistoryFullEntity { Entity: { } f } when f.Tags.ContainsKey(GameTag.ATTACHED):
				// the enchantment's own set-aside FULL_ENTITY comes inside the effect too (live: the BuffImpact SUB_SPELL opens
				// before it — live-20261001-startofgame 2523, DINO_136e)
				case PowerHistoryFullEntity { Entity: { } pre } when IdEntityDic.TryGetValue(pre.Id, out IPlayable made) && made is Enchantment:
				case PowerHistoryShowEntity { Entity: { } s } when s.Tags.ContainsKey(GameTag.ATTACHED):
					return true;
				case PowerHistoryTagChange t:
					switch (t.Tag)
					{
						case GameTag.ATK: case GameTag.HEALTH: case GameTag.ARMOR:
							return t.Value > Known(t.EntityId, t.Tag);
						case GameTag.COST:
							return true;
						case GameTag.TAUNT: case GameTag.DIVINE_SHIELD: case GameTag.LIFESTEAL: case GameTag.WINDFURY:
						case GameTag.RUSH: case GameTag.CHARGE: case GameTag.POISONOUS: case GameTag.STEALTH:
						case GameTag.REBORN: case GameTag.SPELLPOWER:
							return t.Value == 1;
						// HsServer (audit 2026-10-08): a heal is the step a heal-and-buff effect waits for (live JAIL_941
						// live-20261006-darkmoon 48396, CORE_BAR_310, CATA_302, DEEP_026, JAIL_912: the heal inside the effect)
						case GameTag.PREHEALING:
							return t.Value > 0;
					}
					return false;
				case PowerHistoryMetaData { Type: MetaDataType.HEALING }:
					return true;
				default:
					return false;
			}
		}

		internal bool OpenSubSpell(IPlayable source, string prefab, IEnumerable<int> targets, int shownSource = 0, bool collectTargets = false)
		{
			if (!History || prefab == null) return false;
			if (!_openingPending && _pendingFx.Count > 0)
			{
				// HsServer (2026-10-08): engine code naming the effect itself wins over the recorded placement of the same effect (also
				// from another entity: Runic Adornment's draw-and-hold opens from the card, live recorded it in its enchantment's block);
				// the card's other recorded empty effects still play first (live: at its block's start)
				_pendingFx.RemoveAll(p => p.Recorded && p.Prefab == prefab);
				PlayMarkers(PowerHistory.CurrentEffectBlock);
			}
			if (!_openingPending)
				for (int i = _playedMarkers.Count - 1; i >= 0; i--)
				{
					var m = _playedMarkers[i];
					if (m.Prefab != prefab) continue;
					// (played already as a recorded marker in a block still running: the engine's own placement replaces it)
					if (PowerHistory.Retract(m.End) && PowerHistory.Retract(m.Start) && m.Start.First && m.Start.Block != null)
						m.Start.Block.HasSubSpell = false;
					_playedMarkers.RemoveAt(i);
				}
			for (int i = _openSubSpells.Count - 1; i >= 0; i--)
				if (_openSubSpells[i].Source == source.Id)
				{
					if (_openSubSpells[i].Prefab == prefab) return false;
					if (Loader.SubSpells.RoleOf(_openSubSpells[i].Prefab) == Loader.SubSpells.Role.Deathrattle)
						return false; // everything the Deathrattle does plays inside its effect
					CloseSubSpells(source.Id);
					break;
				}
			// shownSource: the entity the client plays it from when it isn't the card (a spell's: its caster's hero), or
			// NoSubSpellSource for none (Source 0: the prefab's own location)
			int shown = shownSource == NoSubSpellSource ? 0 : shownSource != 0 ? shownSource : source.Id;
			// where live played this card's effect from, when recorded (a property of each card's effect: Loader.SubSpells.RecordedSource)
			switch (Loader.SubSpells.RecordedSource(source.Card.Id, prefab))
			{
				case Loader.SubSpells.Source.Hero when source.Controller?.Hero != null: shown = source.Controller.Hero.Id; break;
				case Loader.SubSpells.Source.Self: shown = source.Id; break;
				case Loader.SubSpells.Source.None: shown = 0; break;
				case Loader.SubSpells.Source.Player when source.Controller != null: shown = source.Controller.Id; break;
			}
			var start = new PowerHistorySubSpellStart { Prefab = prefab, Source = shown, CollectTargets = collectTargets };
			if (targets != null) start.Targets.AddRange(targets);
			PowerHistory.Add(start);
			_openSubSpells.Add((source.Id, prefab));
			return true;
		}

		/// <summary>HsServer: close <paramref name="sourceId"/>'s open SUB_SPELLs (innermost first). <paramref name="dropEmpty"/>:
		/// one nothing happened in yet is taken back instead (a card's effect opened before a step that comes first:
		/// Generic.SpendCorpses).</summary>
		internal void CloseSubSpells(int sourceId, bool dropEmpty = false, bool keepSpawnGroup = false)
		{
			for (int i = _openSubSpells.Count - 1; i >= 0; i--)
				if (_openSubSpells[i].Source == sourceId)
				{
					// the end of one of its tasks: cards still coming from the same effect keep their to-hand effect (JoinSpawnGroup)
					if (keepSpawnGroup && _spawnGroup?.Source == sourceId && IdEntityDic.TryGetValue(sourceId, out IPlayable grouped) &&
					    Loader.SubSpells.RoleOf(grouped.Card.Id, _openSubSpells[i].Prefab) == Loader.SubSpells.Role.SpawnToHand)
						continue;
					if (!dropEmpty || !PowerHistory.DropEmptySubSpell())
						PowerHistory.Add(new PowerHistorySubSpellEnd());
					_openSubSpells.RemoveAt(i);
				}
		}

		/// <summary>HsServer: Aura Update (Other) — see <see cref="Aura.UpdateOther"/>.</summary>
		private void AuraUpdateOther()
		{
			List<IAura> auras = Auras;
			for (int i = auras.Count - 1; i >= 0; i--)
				if (auras[i] is Aura aura)
					aura.UpdateOther();
		}

		/// <summary>
		/// Process enqueued tasks.
		/// </summary>
		internal void ProcessTasks()
		{
			TaskQueue queue = TaskQueue;
			queue.ResumePendingTasks();
			while (!queue.IsEmpty)
			{
				if (queue.Process() != TaskState.COMPLETE)
				{
					Log(LogLevel.INFO, BlockType.PLAY, "Game", !Logging ? "" : "Something really bad happend during proccessing, please analyze!");
				}
			}
		}

		/// <summary>
		/// Checks for entities which are pending to be destroyed and updated 
		/// active auras accordingly.
		/// </summary>
		/// <summary>
		/// HsServer: Quest Reward Step (rules A5, D4) — before the Summon Resolution Step, completed Quests' rewards arrive:
		/// a Hero Power reward replaces the Hero Power; a card goes to the hand, or waits while the hand is full.
		/// </summary>
		private void QuestRewardStep()
		{
			foreach (Controller c in _players)
			{
				List<(Card Reward, int QuestId)> pending = c.PendingQuestRewards;
				for (int i = 0; i < pending.Count; i++)
				{
					(Card reward, int questId) = pending[i];
					if (reward.Type == CardType.HERO_POWER)
					{
						new Tasks.SimpleTasks.ReplaceHeroPower(reward).Process(this, c, IdEntityDic[questId], null);
						HeroPower now = c.Hero.HeroPower;
						now.Power?.Aura?.Activate(now);
					}
					else
					{
						if (c.HandZone.IsFull)
							continue; // waits until there is room
						IPlayable card = Entity.FromCard(c, reward);
						card[GameTag.DISPLAYED_CREATOR] = questId;
						Actions.Generic.AddHandPhase.Invoke(c, card);
					}
					pending.RemoveAt(i--);
				}
			}
		}

		/// <summary>Summon Resolution Step: "whenever you summon" triggers for the minions summoned since the last one.</summary>
		private void SummonResolutionStep()
		{
			if (TriggerManager.HasOnSummonTrigger)
			{
				List<Minion> minions = SummonedMinions;
				TaskQueue.StartEvent();
				for (int i = 0; i < minions.Count; i++)
				{
					TriggerManager.OnSummonTrigger(minions[i]);
				}
				ProcessTasks();
				TaskQueue.EndEvent();
			}
			SummonedMinions.Clear();
		}

		/// <summary>HsServer: QUEST_CONTRIBUTOR — playing this hand card would advance its controller's active Quest: it raises
		/// the event the Quest's trigger listens to (a spell cast; a minion played and so summoned; any card played), from a
		/// side the trigger accepts, and the trigger's condition holds for it (Open the Waygate: a spell that didn't start
		/// in the deck). From the Quest's own trigger, so every Quest (and each Questline stage) gets it.</summary>
		private static bool QuestContributor(IPlayable card) =>
			card.Controller.SecretZone.Quests.Any(quest => QuestContributor(card, quest)); // (each half of a split Quest)

		private static bool QuestContributor(IPlayable card, Spell quest)
		{
			Triggers.Trigger trigger = quest?.ActivatedTrigger;
			if (trigger == null || quest.QuestTotalProgress > 0 && quest.QuestProgress >= quest.QuestTotalProgress)
				return false;
			bool raises = trigger.Type switch
			{
				TriggerType.CAST_SPELL or TriggerType.AFTER_CAST => card is Spell,
				TriggerType.PLAY_MINION or TriggerType.AFTER_PLAY_MINION or TriggerType.SUMMON or TriggerType.AFTER_SUMMON => card is Minion,
				TriggerType.PLAY_CARD or TriggerType.AFTER_PLAY_CARD => true,
				_ => false, // Discard, damage, deaths...: not something playing the card itself does
			};
			if (!raises || trigger.TriggerSource is TriggerSource.ENEMY or TriggerSource.OP_MINIONS or TriggerSource.OP_HERO or TriggerSource.SELF)
				return false;
			return trigger.Condition?.Eval(card) ?? true;
		}

		/// <summary>HsServer: rules A9 — a Forced Death Phase inside a Phase (Poison Seeds, Reincarnate, Mimiron's Head,
		/// Defile, Yogg-Saron's casts...): Aura Update → Summon Resolution → Death Creation → Death Phase → Aura Update →
		/// Summon Resolution → Aura Update. Unlike the end of an outermost Phase it runs ONE Death Phase: deaths it causes
		/// wait until the outer Phase ends (Poison Seeds: the Creeper's Spiders, killed by the Sheep, die after the
		/// Treants are summoned).</summary>
		public void ForcedDeathPhase()
		{
			AuraUpdate();
			SummonResolutionStep();
			TaskQueue.StartEvent();
			GraveYard();    // Death Creation Step
			ProcessTasks(); // Death Phase
			TaskQueue.EndEvent();
			AuraUpdate();
			if (SummonedMinions.Count > 0)
				SummonResolutionStep();
			AuraUpdate();
			PublishDerived();
		}

		/// <summary>HsServer: cards in hand that show another card on hover (Power.MouseOverCard) keep the client's
		/// DISPLAY_CARD_ON_MOUSEOVER (that card's dbf id, 0 for none) current; the client shows it in the card's tooltip (its
		/// Card / DisplayCardsInTooltip read the tag for cards in hand).</summary>
		private void UpdateMouseOverCards()
		{
			foreach (Controller p in _players)
				foreach (IPlayable card in p.HandZone)
				{
					if (card.Card.Power?.MouseOverCard is Func<IPlayable, Card> show)
					{
						int dbf = show(card)?.AssetId ?? 0;
						if (card[DisplayCardOnMouseover] != dbf) card[DisplayCardOnMouseover] = dbf;
					}
					// HsServer: a "({0} left!) / (Ready!)" card's own counter (client ENTITY_TAG_THRESHOLD_TAG_ID 2 =
					// TAG_SCRIPT_DATA_NUM_1; the client shows threshold - count, then Ready!): Power.ThresholdCount
					if (card.Card.Power?.ThresholdCount is Func<IPlayable, int> count)
					{
						int n = count(card);
						if (card[GameTag.TAG_SCRIPT_DATA_NUM_1] != n) card[GameTag.TAG_SCRIPT_DATA_NUM_1] = n;
					}
					UpdateHandCounter(card);
				}
			// HsServer (TITANS): a minion's own number stays current in play too (Sif's Spell Damage text)
			foreach (Controller p in _players)
				foreach (Minion minion in p.BoardZone)
					UpdateHandCounter(minion);
		}

		/// <summary>HsServer (TITANS): Power.HandCounter — the card's own number for its text, in TAG_SCRIPT_DATA_NUM_1.</summary>
		private static void UpdateHandCounter(IPlayable card)
		{
			if (card.Card.Power?.HandCounter is Func<IPlayable, int> counter)
			{
				int n = counter(card);
				if (card[GameTag.TAG_SCRIPT_DATA_NUM_1] != n) card[GameTag.TAG_SCRIPT_DATA_NUM_1] = n;
			}
		}
		private const GameTag DisplayCardOnMouseover = (GameTag)1078;

		public void DeathProcessingAndAuraUpdate()
		{
			//Inter-Phase steps
			
			AuraUpdate();

			if (Player1.PendingQuestRewards.Count > 0 || Player2.PendingQuestRewards.Count > 0)
				QuestRewardStep();

			SummonResolutionStep();

			TaskQueue.StartEvent();
			do
			{
				GraveYard();	// Death Creation Step

				ProcessTasks(); // Death Resolution Phase

				// HsServer: rules A5 — a Death Phase is an outermost Phase too, so what its Deathrattles and Reborn
				// summoned resolves its Summon Event right after it (Haunted Creeper's Spiders draw for Starving Buzzard
				// now, not at the next action). Its triggers can cause more deaths: the loop goes on.
				if (SummonedMinions.Count > 0)
				{
					AuraUpdate();
					SummonResolutionStep();
				}
			} while (DeadMinions.Count != 0);
			TaskQueue.EndEvent();

			AuraUpdate();	// Aura Update (Other) step(Not implemented)
			// HsServer (WILD_WEST): "whenever <state>" abilities (Howdyfin's hand below 3) once the sequence has settled
			Actions.Generic.RunStateChecks(this);
			Actions.Generic.UpdateFortunes(this); // HsServer (ISLAND_VACATION): Fortunes follow the deck's top card
			Actions.Generic.CheckHandCollisions(this); // HsServer (SPACE): Xor'toth's Stars
			Actions.Generic.ShatterHands(this); // HsServer (CATACLYSM): Shatter cards split in hand, halves next to each other merge
			Actions.Generic.HandTransforms(this); // HsServer (CATACLYSM): "While holding this, if …, transform" (Genn)
			Actions.Generic.SyncAdjacentInHand(this); // HsServer (CATACLYSM): Sabotage!'s neighbours
			Actions.Generic.SummonLeftoverAppendages(this); // HsServer (CATACLYSM): Magmaw
			Actions.Generic.SyncLeylines(this); // HsServer (CATACLYSM): the Leylines' numbers
			Actions.Generic.SyncAdjacentOnBoard(this); // HsServer (CATACLYSM): Al'Akir's hands and Soldiers
			Actions.Generic.UpgradeAfterLaunch(this); // HsServer (SPACE): Hellion, Siege Tank, Thor in hand after a launch
			Actions.Generic.SyncShieldHits(this); // HsServer (EMERALD_DREAM): Toreth's shields show their hits
			Actions.Generic.AfterStatGains(this); // HsServer (VIOLET_HOLD): Dalaran Champion's extra +1/+1
			Actions.Generic.SyncTurnLimit(this); // HsServer: Nozdormu's turn-time limit on both players' TIMEOUT
			Actions.Generic.SyncTurnPlayRestriction(this); // HsServer (events): "You can only play minions / spells" on the hand cards
			Actions.Generic.SyncTopOfDeckText(this); // HsServer (events): Sharp-Eyed Lookout names the top card of your deck
			PublishDerived();
			UpdateMouseOverCards();
		}

		// HsServer: values the client must always agree on (ZONE first: a zone change is what reveals a card).
		private static readonly GameTag[] PublishedTags =
		{
			GameTag.ZONE, GameTag.CONTROLLER, GameTag.ZONE_POSITION, GameTag.EXHAUSTED,
		};

		// HsServer: character keywords (only characters: other entities' aura data doesn't have them).
		private static readonly GameTag[] PublishedCharacterTags =
		{
			GameTag.FROZEN, GameTag.TAUNT, GameTag.DIVINE_SHIELD, GameTag.STEALTH, GameTag.WINDFURY, GameTag.CHARGE,
			GameTag.SILENCED, GameTag.POISONOUS, GameTag.LIFESTEAL, GameTag.IMMUNE, GameTag.SPELLPOWER, GameTag.ARMOR,
			GameTag.NUM_ATTACKS_THIS_TURN, GameTag.CANT_BE_TARGETED_BY_SPELLS, GameTag.CANT_BE_TARGETED_BY_HERO_POWERS,
			// (2026-10-09) a "can't attack unless" aura's state (Silithid Swarmer, Unpowered Mauler, Arcane Watcher) and an aura's Rush
			// (WW_435's Beasts): live sends them as they change (session-20261008-012951: OG_034 / BOT_098 CANT_ATTACK 1 / 0; TSC_647
			// RUSH 0); ours stayed on the server and the client kept a Rush its source's death had taken (soak client view)
			GameTag.CANT_ATTACK, GameTag.RUSH,
		};

		private static readonly GameTag[] PublishedPlayerTags =
		{
			GameTag.RESOURCES, GameTag.RESOURCES_USED, GameTag.TEMP_RESOURCES, GameTag.OVERLOAD_LOCKED, GameTag.OVERLOAD_OWED,
		};

		/// <summary>
		/// HsServer: report every value the client sees that changed without its own history entry — stats and costs
		/// from auras and enchantments, the hero's attack (its weapon counts only on its owner's turn), mana, removed
		/// enchantments. Called where live reports them: after the aura update that follows every action, at each step
		/// change and at the end of a move. The client's value is what was reported (<see cref="PowerHistory.Reported"/>).
		/// </summary>
		public void PublishDerived()
		{
			if (!History)
				return;
			Dictionary<int, Dictionary<GameTag, int>> reported = PowerHistory.Reported;
			foreach (IPlayable e in IdEntityDic.Values)
			{
				if (!reported.TryGetValue(e.Id, out Dictionary<GameTag, int> known))
					continue; // never announced: nothing to update yet
				foreach (GameTag tag in PublishedTags)
					Publish(e.Id, known, tag, e[tag]);
				// HsServer: a one-turn enchantment on it ("this turn": live TAG_ONE_TURN_EFFECT on the minion / hero / card)
				// (not on an enchantment itself: its own TAG_ONE_TURN_EFFECT is its card's, live never clears it)
				if (!(e is Enchantment))
					Publish(e.Id, known, GameTag.TAG_ONE_TURN_EFFECT, e.AppliedEnchantments?.Any(x => x.IsOneTurnActive) == true ? 1 : 0);
				// HsServer: a Divine Shield that absorbed a hit flags DIVINE_SHIELD_DAMAGE inside the action; live clears it
				// right after the action (here, at the end of the move)
				// (Toreth: a shield still standing keeps its cracks — client DIVINE_SHIELD_DAMAGE 1/2 while DIVINE_SHIELD > 1)
				if (e[HsTag.DIVINE_SHIELD_DAMAGE] != 0 && e[GameTag.DIVINE_SHIELD] <= 1)
					e[HsTag.DIVINE_SHIELD_DAMAGE] = 0;
				switch (e)
				{
					case ICharacter c:
						foreach (GameTag tag in PublishedCharacterTags)
							Publish(e.Id, known, tag, e[tag]);
						// Elusive gained by an effect: live sets ELUSIVE with both "can't be targeted" tags
						Publish(e.Id, known, HsTag.ELUSIVE,
							e.Card[HsTag.ELUSIVE] > 0 || e[GameTag.CANT_BE_TARGETED_BY_SPELLS] > 0 && e[GameTag.CANT_BE_TARGETED_BY_HERO_POWERS] > 0 ? 1 : 0);
						Publish(e.Id, known, GameTag.ATK, c.AttackDamage);
						Publish(e.Id, known, GameTag.HEALTH, c.BaseHealth);
						Publish(e.Id, known, GameTag.DAMAGE, c.Damage);
						break;
					case Weapon w:
						// The client shows DURABILITY - DAMAGE; both are stored as such.
						Publish(e.Id, known, GameTag.ATK, w.AttackDamage);
						Publish(e.Id, known, GameTag.DURABILITY, w[GameTag.DURABILITY]);
						Publish(e.Id, known, GameTag.DAMAGE, w.Damage);
						break;
				}
				// HsServer: "heals deal damage" hint (purple haze on heal cards in hand when the source is played or hovered):
				// on an in-play source of a heals-deal-damage aura (Auchenai), or on the hero while a "next heal deals
				// damage" effect waits (Ruby Sanctum). Client: Card.cs HasHealingDoesDamageHint / ZoneMgr hand bursts.
				Publish(e.Id, known, GameTag.HEALING_DOES_DAMAGE_HINT, HealingDoesDamageHint(e) ? 1 : 0);
				if (e.Zone?.Type == SabberStoneCore.Enums.Zone.HAND)
				{
					Publish(e.Id, known, GameTag.COST, e.Cost);
					// HsServer: live keeps the card's cost in hand in TAG_LAST_KNOWN_COST_IN_HAND (in its draw SHOW_ENTITY and
					// whenever it changes); the history tile of a played card shows that value (HistoryInfo.DuplicateEntity
					// copies it into COST before the play's own changes). Set only at the play, our history showed cost 0.
					if (e[GameTag.TAG_LAST_KNOWN_COST_IN_HAND] != e.Cost)
						((Entity)e)._data[GameTag.TAG_LAST_KNOWN_COST_IN_HAND] = e.Cost;
					Publish(e.Id, known, GameTag.TAG_LAST_KNOWN_COST_IN_HAND, e.Cost);
					// HsServer (2026-10-08): its Attack and Health siblings for a minion — client tags 4472 / 4997 = the card's last known
					// Attack / Health in hand (live: 1,292 of 1,550 / 1,315 of 1,559 equal the card's ATK / HEALTH, the rest a buff away;
					// in its draw / play SHOW_ENTITY next to TAG_LAST_KNOWN_COST_IN_HAND)
					if (e is Minion inHand)
					{
						if (e[LAST_KNOWN_ATK_IN_HAND] != inHand.AttackDamage) ((Entity)e)._data[LAST_KNOWN_ATK_IN_HAND] = inHand.AttackDamage;
						if (e[LAST_KNOWN_HEALTH_IN_HAND] != inHand.Health) ((Entity)e)._data[LAST_KNOWN_HEALTH_IN_HAND] = inHand.Health;
						Publish(e.Id, known, LAST_KNOWN_ATK_IN_HAND, inHand.AttackDamage);
						Publish(e.Id, known, LAST_KNOWN_HEALTH_IN_HAND, inHand.Health);
					}
					// HsServer: the "condition met" glow (PoweredUp); Outcast cards glow in the left- or right-most slot (live:
					// POWERED_UP on Spectral Sight)
					bool outcast = e.Card[HsTag.OUTCAST] > 0;
					bool quickdraw = e.Card[HsTag.QUICKDRAW] > 0; // HsServer: Quickdraw glows the turn the card entered the hand
					// HsServer: an optional target with a condition (a minion type, Taunt, Attack N+, damaged…, or "if you're holding a
					// Dragon") glows while it would take a target (live POWERED_UP on Skeletal Sidekick, Amalgam of the Deep; the
					// user: The Black Knight; Playable.HasConditionalTarget)
					bool typeTarget = Playable.HasConditionalTarget(e.Card);
					// HsServer: a card that spends Corpses on play glows while its owner has enough (live POWERED_UP: Rite of
					// Atrocity lit at 2 Corpses, Grave Strength at 5+, cleared at the play; Power.CorpseCost)
					int corpseCost = e.Card.Power?.CorpseCost ?? 0;
					// HsServer: a "({0} left!)" card glows once "(Ready!)" (user 2026-10-06: Kazakusan; its threshold in the card
					// data: Actions.Generic.ThresholdMet)
					bool threshold = Actions.Generic.HasThreshold(e.Card);
					// HsServer: a Manathirst card glows while its owner has the Mana Crystals (live POWERED_UP on Astalor
					// Bloodsworn, live-20261005 power-decoded-184804 3690; cleared at the play)
					bool manathirst = e.Card[Actions.Generic.MANATHIRST] > 0;
					// HsServer: a Finale card glows while its Cost equals the Mana left (live POWERED_UP on Infinitize the
					// Maxitude ETC_206; cleared at the play)
					bool finale = e.Card[Actions.Generic.FINALE] > 0;
					// HsServer (WILD_WEST): a card whose own cost change has a condition glows while it holds (live Aftershocks
					// POWERED_UP with its discount, live-20261005/power-decoded-184804 6858; Actions.Generic.HasCostCondition)
					bool costGlow = Actions.Generic.HasCostCondition(e.Card);
					// HsServer (SPACE): "If this costs (0)" glows while it costs 0 (Power.GlowAtZeroCost)
					bool zeroGlow = e.Card.Power?.GlowAtZeroCost == true;
					Func<IPlayable, bool> glowWhile = e.Card.Power?.GlowWhile; // (Power.GlowWhile: Bad Omen)
					// HsServer (LOST_CITY): Kindred (live: POWERED_UP while it would hold, ELEMENTAL_POWERED_UP on the owner's turn)
					(bool kindredGlow, bool kindredNow) = Actions.Generic.KindredGlow(e);
					if (e.Card[Actions.Generic.KINDRED] > 0)
					{
						Publish(e.Id, known, GameTag.POWERED_UP, kindredGlow ? 1 : 0);
						Publish(e.Id, known, GameTag.ELEMENTAL_POWERED_UP, kindredNow ? 1 : 0);
					}
					if (e.Card[Actions.Generic.KINDRED] > 0) { }
					else if (PoweredUp.Check(e.Card) is { } check || outcast || quickdraw || typeTarget || corpseCost > 0 || threshold || manathirst || finale || costGlow || zeroGlow || glowWhile != null)
						Publish(e.Id, known, GameTag.POWERED_UP,
							glowWhile != null && glowWhile(e) ||
							zeroGlow && e.Cost == 0 ||
							costGlow && Actions.Generic.CostConditionMet(e) ||
							finale && Actions.Generic.FinaleReady(e) ||
							manathirst && Actions.Generic.Manathirst(e) ||
							threshold && Actions.Generic.ThresholdMet(e) ||
							outcast && Playable.InOuterHandSlot(e) || quickdraw && Actions.Generic.IsQuickdrawNow(e) ||
							typeTarget && e is Playable conditional && Playable.ConditionalTargetReady(conditional) || corpseCost > 0 && e.Controller.Corpses >= corpseCost ||
							PoweredUp.Check(e.Card) is { } c2 && PoweredUp.Holds(this, e, c2) ? 1 : 0);
					// HsServer: "played an Elemental last turn" cards (live ELEMENTAL_POWERED_UP: Lamplighter, Spontaneous Combustion)
					// (or its play requirement: "If you played an Elemental last turn, choose the target" — SPACE Spontaneous Combustion)
					if (PoweredUp.Uses(e.Card, SelfCondition.ElementalPlayedLastTurn) || // from its script, not its text (audit A6)
					    e.Card.PlayRequirements?.ContainsKey(PlayReq.REQ_TARGET_IF_AVAILABE_AND_ELEMENTAL_PLAYED_LAST_TURN) == true)
						Publish(e.Id, known, GameTag.ELEMENTAL_POWERED_UP, e.Controller[GameTag.NUM_ELEMENTAL_PLAYED_LAST_TURN] > 0 ? 1 : 0);
					// HsServer: the cost gem shows Health (TAG_CARD_ALTERNATE_COST.HEALTH = 1) while the card costs Health
					// (Corpses = 3: Reanimated Pterrordax, Exarch Maladaar's next card)
					Publish(e.Id, known, HsTag.CARD_ALTERNATE_COST,
						(e.AuraEffects?.CardCostHealth ?? false) || Playable.CostsHealth(e) ? 1 : Playable.CostsCorpses(e) ? Playable.CorpseAlternateCost :
						Playable.CostsArmor(e) ? Playable.ArmorAlternateCost : Playable.CostsOpponentHealth(e) ? Playable.OpponentHealthAlternateCost : 0);
					// HsServer: the Quest highlight (live QUEST_CONTRIBUTOR, Open the Waygate in the Heist run)
					Publish(e.Id, known, GameTag.QUEST_CONTRIBUTOR, QuestContributor(e) ? 1 : 0);
				}
				else
				{
					// played (live: cleared as it leaves the hand, with CARD_ALTERNATE_COST)
					if (known.TryGetValue(GameTag.POWERED_UP, out int lit) && lit != 0)
						Publish(e.Id, known, GameTag.POWERED_UP, 0);
					if (known.TryGetValue(GameTag.QUEST_CONTRIBUTOR, out int quest) && quest != 0)
						Publish(e.Id, known, GameTag.QUEST_CONTRIBUTOR, 0);
				}
			}
			foreach (Controller p in _players)
				if (reported.TryGetValue(p.Id, out Dictionary<GameTag, int> known))
				{
					foreach (GameTag tag in PublishedPlayerTags)
						Publish(p.Id, known, tag, p[tag]);
					Publish(p.Id, known, GameTag.TAG_ONE_TURN_EFFECT, p.AppliedEnchantments?.Any(x => x.IsOneTurnActive) == true ? 1 : 0);
					Publish(p.Id, known, GameTag.STEADY_SHOT_CAN_TARGET, p.SteadyShotCanTarget ? 1 : 0); // HsServer: Steamwheedle Sniper
					// HsServer: the numbers the client writes on card text (Spell Damage *4*, hero power damage, doublers):
					// live keeps them as running totals on the player (CURRENT_SPELLPOWER_BASE, _FIRE..., heist/evolve logs).
					Publish(p.Id, known, GameTag.CURRENT_SPELLPOWER, p.CurrentSpellPower);
					for (int school = 1; school <= 8; school++)
						Publish(p.Id, known, Controller.CurrentSchoolSpellPowerTag(school), p.SchoolSpellPower(school));
					Publish(p.Id, known, GameTag.CURRENT_HEROPOWER_DAMAGE_BONUS, p.Hero?.HeroPowerDamage ?? 0);
					foreach (GameTag doubler in new[] { GameTag.SPELLPOWER_DOUBLE, GameTag.SPELL_HEALING_DOUBLE, GameTag.ALL_HEALING_DOUBLE })
						Publish(p.Id, known, doubler, p.ControllerAuraEffects[doubler]);
					Publish(p.Id, known, GameTag.HERO_POWER_DISABLED, p[GameTag.HERO_POWER_DISABLED]); // aura-held (Mind Spike...)
					Publish(p.Id, known, HsTag.MAIN_HAND_WEAPON_ENTITY, p.Hero?.Weapon?.Id ?? 0);
					// HsServer: the client's "your heals deal damage now" (hand hint on drawn heal cards, targeting)
					Publish(p.Id, known, GameTag.HEALING_DOES_DAMAGE, p.RestoreToDamage || NextHealDamages(p) ? 1 : 0);
					// the purple screen border of "this turn, your healing effects deal damage" (Auchenai Phantasm, Embrace the
					// Shadow): a one-turn heals-deal-damage effect on the player. Client: Player.cs toggles the hero's
					// EMBRACE_THE_SHADOW spell. Not for Ruby Sanctum's next-heal effect (live-20261001-auchenai).
					Publish(p.Id, known, GameTag.EMBRACE_THE_SHADOW,
						HealingDoesDamageEnchantments(p).Any(x => x.Card[GameTag.TAG_ONE_TURN_EFFECT] > 0) ? 1 : 0);
				}
		}

		private static bool NextHealDamages(Controller p) =>
			p.ControllerAuraEffects[HsTag.NEXT_HEAL_DAMAGES] > 0 || p[HsTag.NEXT_HEAL_DAMAGES] > 0;

		private static bool GrantsHealingDoesDamage(Card card) =>
			card.Power?.Aura is Auras.Aura aura && aura.EffectList?.Any(x => x.Tag == GameTag.HEALING_DOES_DAMAGE) == true;

		// Heals-deal-damage effects sitting on the player or its hero as enchantments (Auchenai Phantasm's "this turn").
		private static IEnumerable<Enchantment> HealingDoesDamageEnchantments(Controller p) =>
			(p.AppliedEnchantments ?? new List<Enchantment>()).Concat(p.Hero?.AppliedEnchantments ?? new List<Enchantment>())
			.Where(x => GrantsHealingDoesDamage(x.Card));

		// Live (live-20261001-auchenai): the hint is on the source minion (Auchenai Soulpriest) or, for an effect on the
		// player (Ruby Sanctum's next heal, Auchenai Phantasm's this turn), on the hero.
		private static bool HealingDoesDamageHint(IPlayable e)
		{
			if (e is Enchantment)
				return false;
			if (e is Hero hero)
				return hero.Controller.Hero == hero && (NextHealDamages(hero.Controller) || HealingDoesDamageEnchantments(hero.Controller).Any());
			return e.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY && e[GameTag.SILENCED] == 0 && GrantsHealingDoesDamage(e.Card);
		}

		private void Publish(int id, Dictionary<GameTag, int> known, GameTag tag, int value)
		{
			known.TryGetValue(tag, out int current);
			if (current != value)
				PowerHistory.Add(PowerHistoryBuilder.TagChange(id, tag, value));
		}


		/// <summary>
		/// Performs a deep copy of this game instance and returns the result.
		/// </summary>
		/// <returns></returns>
		public Game Clone(bool logging = false, bool resetRandomSeed = true, bool history = false)
		{
			return new Game(this, logging, resetRandomSeed, history);
		}

		/// <summary>Builds and stores a logentry, from the specified log message.</summary>
		/// <param name="level"><see cref="LogLevel"/></param>
		/// <param name="block"><see cref="BlockType"/></param>
		/// <param name="location">Rough string respresentation of where in the code the message came from.</param>
		/// <param name="text">The message itself.</param>
		public void Log(LogLevel level, BlockType block, string location, string text)
		{
			if (!_gameConfig.Logging)
				return;

			Logs.Enqueue(new LogEntry()
			{
				TimeStamp = DateTime.Now,
				Level = level,
				Location = location,
				BlockType = block,
				Text = text
			});
		}

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

		public void Dump(string location, string text)
		{
			Logs.Enqueue(new LogEntry()
			{
				TimeStamp = DateTime.Now,
				Level = LogLevel.DUMP,
				Location = location,
				BlockType = BlockType.SCRIPT,
				Text = text
			});
		}

		public override string Hash(params GameTag[] ignore)
		{
			var str = new StringBuilder();
			str.Append(base.Hash(ignore));
			str.Append(Player1.Hash(ignore));
			str.Append(Player2.Hash(ignore));
			return str.ToString();
		}

		public string FullPrint()
		{
			var str = new StringBuilder();
			str.AppendLine(Player1.HandZone.FullPrint());
			str.AppendLine(Player1.Hero.FullPrint());
			str.AppendLine(Player1.BoardZone.FullPrint());
			str.AppendLine(Player2.BoardZone.FullPrint());
			str.AppendLine(Player2.Hero.FullPrint());
			str.AppendLine(Player2.HandZone.FullPrint());
			return str.ToString();
		}
		
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
	}

	public partial class Game
	{
		/// <summary>
		/// Gets or sets the turn count.
		/// </summary>
		/// <value>The amount of player turns that happened in the game. When the game starts (after Mulligan),
		/// value will equal 1.</value>
		public int Turn
		{
			get { return this[GameTag.TURN]; }
			set { this[GameTag.TURN] = value; }
		}

		/// <summary>
		/// Gets or sets the game state.
		/// </summary>
		/// <value><see cref="State"/></value>
		public State State
		{
			get { return (State)this[GameTag.STATE]; }
			set { this[GameTag.STATE] = (int)value; }
		}

		/// <summary>
		/// Gets or sets the first card played this turn.
		/// </summary>
		/// <value>The entityID of the card.</value>
		public int FirstCardPlayedThisTurn
		{
			get { return this[GameTag.FIRST_CARD_PLAYED_THIS_TURN]; }
			set { this[GameTag.FIRST_CARD_PLAYED_THIS_TURN] = value; }
		}

		/// <summary>
		/// The controller which goes 'first'. This player's turn starts after Mulligan.
		/// </summary>
		/// <value><see cref="Controller"/></value>
		public Controller FirstPlayer
		{
			get => Player1[GameTag.FIRST_PLAYER] == 1 ? Player1 : Player2[GameTag.FIRST_PLAYER] == 1 ? Player2 : null;
			set => value[GameTag.FIRST_PLAYER] = 1;
		}

		/// <summary>
		/// Gets or sets the controller delegating the current turn.
		/// </summary>
		/// <value><see cref="Controller"/></value>
		public Controller CurrentPlayer
		{
			get => _currentPlayer;
			set
			{
				_currentPlayer = value;
				if (!History) return;
				value.Opponent[GameTag.CURRENT_PLAYER] = 0;
				value[GameTag.CURRENT_PLAYER] = 1;
			}
		}

		/// <summary>
		/// Gets the opponent controller of <see cref="CurrentPlayer"/>.
		/// </summary>
		/// <value><see cref="Controller"/></value>
		//public Controller CurrentOpponent
		//	=> Player1[GameTag.CURRENT_PLAYER] == 1 ? Player2 : Player2[GameTag.CURRENT_PLAYER] == 1 ? Player1 : null;
		public Controller CurrentOpponent => _currentPlayer == Player1 ? Player2 : Player1;

		/// <summary>
		/// Gets or sets the CURRENT step. These steps occur within <see cref="State.RUNNING"/> and
		/// indicate states which are used to process actions.
		/// </summary>
		/// <value><see cref="Step"/></value>
		public Step Step
		{
			get => (Step)_data[GameTag.STEP];
			set => this[GameTag.STEP] = (int)value;
		}

		/// <summary>HsServer: change the step without a history entry (engine-internal steps such as MAIN_COMBAT, which live
		/// never sends to the client).</summary>
		public void SetStepQuietly(Step step) => _data[GameTag.STEP] = (int)step;

		/// <summary>
		/// Gets or sets the NEXT step. <seealso cref="Step"/>
		/// </summary>
		/// <value><see cref="Step"/></value>
		public Step NextStep
		{
			set
			{
				// HsServer: a finished game stays finished (an attack's cleanup used to set MAIN_ACTION after the game over).
				if (State == State.COMPLETE && value != Step.FINAL_GAMEOVER) return;
				// HsServer: each turn step is its own Sequence: a hero that died in the previous one ends the game before the
				// next step runs (Rule 9).
				if (value != Step.FINAL_WRAPUP && value != Step.FINAL_GAMEOVER && CheckGameOver()) return;
				this[GameTag.NEXT_STEP] = (int)value;
				// HsServer: live step shape — the step's TRIGGER block ends right after its NEXT_STEP.
				if (_stepBlockOpen)
				{
					_stepBlockOpen = false;
					PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
				}
				// HsServer (2026-10-08): ranked spells rank up after the MAIN_READY block, before MAIN_START_TRIGGERS (live 212735 8925)
				if (value == Step.MAIN_START_TRIGGERS)
					foreach (Controller p in _players)
						if (p != null && p[HsTag.RANKS_PENDING] > 0)
						{
							p[HsTag.RANKS_PENDING] = 0;
							Actions.Generic.RankUpAll(p);
						}
				if (value == Step.MAIN_CLEANUP)
				{
					RemoveGhostlyCards();
					// HsServer (TITANS): the turn's end in hand (Actions/Titans.cs) — Blossom cards count down in their own blocks
					// (live: right after the end step's NEXT_STEP MAIN_CLEANUP), "if you play it this turn" marks go, cards locked
					// until this turn's end unlock
					Actions.Generic.BlossomAtTurnEnd(CurrentPlayer);
					Actions.Generic.ClearPlayThisTurnMarks(CurrentPlayer);
					Actions.Generic.UnlockHandCards(this, Turn);
					// HsServer: Remixed cards change in the hand of the player whose turn ends, after the end step's block
					// (live-20261007-zilliax-etc-remix: Remixed Rhapsody, before STEP MAIN_CLEANUP; Actions/Festival.cs)
					Actions.Generic.HandChangesAtTurnEnd(CurrentPlayer, Step.MAIN_CLEANUP);
				}
				// HsServer: Harmonic cards swap in the hand of the player whose turn ends, after the cleanup step's block
				// (live-20261007-vfx-decks: Harmonic Disco, its own TRIGGER block before STEP MAIN_NEXT; Actions/Festival.cs)
				if (value == Step.MAIN_NEXT) Actions.Generic.HandChangesAtTurnEnd(CurrentPlayer, Step.MAIN_NEXT);
				// HsServer: the starting hand's Remixed cards take a version when the game begins, after the first MAIN_READY
				// step block (live-20261007-zilliax-etc-remix; Actions/Festival.cs)
				if (value == Step.MAIN_START_TRIGGERS) Actions.Generic.RemixStartingHands(this);
				// HsServer: live (Genn) — Start of Game effects resolve after the mulligan's step block, in their own blocks,
				// before the game's EffectIndex 4 block. Their conditions were checked at game start, on the starting deck.
				// HsServer (TITANS): the starting hands are what the players hold once the mulligan is over (Generic.StartingHand)
				if (value == Step.MAIN_READY && (Step == Step.BEGIN_MULLIGAN || Step == Step.MAIN_BEGIN))
					Actions.Generic.RecordStartingHands(this);
				if (value == Step.MAIN_READY && Step == Step.BEGIN_MULLIGAN)
				{
					ProcessTasks();
					// HsServer (2026-10-09, Dalaran Heist): the set-aside Dungeon passives act right after the mulligan (live: the game
					// block after the mulligan's last block; Generic.PlayDungeonPassives — without a mulligan: MainBegin)
					Actions.Generic.PlayDungeonPassives(this);
					// HsServer (2026-10-09, Dalaran Heist Anomaly Mode): then the run's anomaly (Generic.PlayAnomaly; live 185040 5565)
					Actions.Generic.PlayAnomaly(this, Player1, _gameConfig.Player1Anomaly);
					Actions.Generic.PlayAnomaly(this, Player2, _gameConfig.Player2Anomaly);
				}
				// HsServer: after the mulligan, live has an empty TRIGGER block on the game (EffectIndex 4) before MAIN_READY.
				if (History && value == Step.MAIN_READY && Step == Step.BEGIN_MULLIGAN)
				{
					PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, Id, "", 4, 0));
					PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
				}
				// HsServer: live reports what the turn change altered (e.g. the hero's attack with its weapon) between
				// the step block and the next STEP.
				PublishDerived();
				GamesEventManager.NextStepEvent(this, value);
			}
		}

		private bool _stepBlockOpen;

		/// <summary>
		/// HsServer: a player's mulligan is over (live shape: a TRIGGER block setting MULLIGAN_STATE=DONE). When it was the
		/// last one, the same block moves the game on to MAIN_READY, followed by the game's empty EffectIndex 4 block.
		/// </summary>
		public void EndMulligan(Controller c)
		{
			OpenStepBlock(c.Id);
			c.MulliganState = Mulligan.DONE;
			if (Player1.MulliganState == Mulligan.DONE && Player2.MulliganState == Mulligan.DONE && Step == Step.BEGIN_MULLIGAN)
			{
				// (what MAIN_BEGIN did — the queued Start of Game effects — runs once this step block is closed, NextStep)
				if (History) // HsServer: live shuffles both decks here, before the first player's TURN 1
				{
					PowerHistory.Add(PowerHistoryBuilder.ShuffleDeck(Player1.PlayerId));
					PowerHistory.Add(PowerHistoryBuilder.ShuffleDeck(Player2.PlayerId));
				}
				CurrentPlayer[GameTag.TURN] = 1; // HsServer: live sets the first player's own turn 1 here, before NEXT_STEP
				NextStep = Step.MAIN_READY;
				return;
			}
			if (_stepBlockOpen)
			{
				_stepBlockOpen = false;
				PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			}
		}

		/// <summary>HsServer: open the current step's TRIGGER block (closed by the NEXT_STEP setter), as live reports steps.</summary>
		private void OpenStepBlock(int ownerId)
		{
			if (!History) return;
			PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, ownerId, "", -1, 0));
			_stepBlockOpen = true;
		}

		/// <summary>
		/// Gets or sets the number of killed minions for this turn.
		/// </summary>
		/// <value>The amount of killed minions.</value>
		public int NumMinionsKilledThisTurn
		{
			get
			{
				_data.TryGetValue(GameTag.NUM_MINIONS_KILLED_THIS_TURN, out int value);
				return value;
			}
			set { this[GameTag.NUM_MINIONS_KILLED_THIS_TURN] = value; }
		}

		/// <summary>
		/// The entityID of the character which wants to attack, by entering the
		/// next combat phase.
		/// </summary>
		public int ProposedAttacker
		{
			get
			{
				_data.TryGetValue(GameTag.PROPOSED_ATTACKER, out int value);
				return value;
			}
			set => this[GameTag.PROPOSED_ATTACKER] = value;
		}

		/// <summary>
		/// The entityID of the character which has to defend during the next
		/// combat phase.
		/// </summary>
		public int ProposedDefender
		{
			get
			{
				_data.TryGetValue(GameTag.PROPOSED_DEFENDER, out int value);
				return value;
			}
			set => this[GameTag.PROPOSED_DEFENDER] = value;
		}

		/// <summary>Gets the heroes.</summary>
		/// <value><see cref="Hero"/></value>
		public List<Hero> Heroes => new List<Hero> { Game.Player1.Hero, Game.Player2.Hero };

		/// <summary>Gets ALL minions (from both sides of the board).</summary>
		/// <value><see cref="Minion"/></value>
		public List<Minion> Minions
		{
			get
			{
				var list = new List<Minion>();
				list.AddRange(Game.Player1.BoardZone);
				list.AddRange(Game.Player2.BoardZone);
				return list;
			}
		}

		/// <summary>Gets ALL characters.</summary>
		/// <value><see cref="ICharacter"/></value>
		public List<ICharacter> Characters
		{
			get
			{
				var list = new List<ICharacter>();
				list.AddRange(Minions);
				list.AddRange(Heroes);
				return list;
			}
		}
	}
}
