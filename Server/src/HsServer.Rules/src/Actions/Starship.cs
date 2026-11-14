// HsServer addition (not upstream SabberStone): The Great Dark Beyond (SPACE, set 1935) — Starships. Tags: Enums/HsTag.cs
// 990450-990499.
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
		/// <summary>Client card tags: STARSHIP_PIECE (3568), its "add to the Starship" trigger keyword (3684), STARSHIP (3555, the
		/// keyword tooltip), LAUNCHPAD (3563: a Starship waiting for launch), TAG_LAUNCHPAD_ABILITY (3628: its GDB_905), the
		/// player's STARSHIP_LAUNCH_COST_DISCOUNT (3640), STARSHIP_LAUNCH_TRIGGER (4013: a piece whose effect fires on launch), the
		/// pieces' effect enchantments (3870), HIDE_HEALTH_NUMBER (3471: on the Starship tokens).</summary>
		public const GameTag STARSHIP_PIECE = (GameTag)3568, STARSHIP_PIECE_KEYWORD = (GameTag)3684, STARSHIP = (GameTag)3555,
			LAUNCHPAD = (GameTag)3563, LAUNCHPAD_ABILITY = (GameTag)3628, STARSHIP_LAUNCH_COST_DISCOUNT = (GameTag)3640,
			STARSHIP_LAUNCH_TRIGGER = (GameTag)4013, PIECE_EFFECT = (GameTag)3870;

		/// <summary>The launch ability card (client GameUtils.STARSHIP_LAUNCH_CARD_ID): its COST is the launch's.</summary>
		public const string LaunchCardId = "GDB_905";

		/// <summary>Does <paramref name="m"/> add itself to a Starship when it dies (a Starship Piece not silenced — cardgamer:
		/// "Silencing a Starship Piece prevents it from contributing")?</summary>
		public static bool IsStarshipPiece(Minion m) =>
			m != null && !(m is Location) && m.Card[STARSHIP_PIECE] > 0 && m.Card[STARSHIP_PIECE_KEYWORD] > 0 && !m.IsSilenced;

		/// <summary>The player's Starship waiting for launch (LAUNCHPAD), or null. "If you're building a Starship" (wiki Starship).</summary>
		public static Minion BuildingStarship(Controller c)
		{
			if (c?.BoardZone == null) return null;
			// (the whole board: BoardZone.GetAll() leaves out untouchables, and a Starship on its launchpad is one)
			foreach (Minion m in c.BoardZone.GetSpan())
				if (m[LAUNCHPAD] > 0 && !m.ToBeDestroyed) return m;
			return null;
		}

		public static bool IsBuildingStarship(Controller c) => BuildingStarship(c) != null;

		/// <summary>The Starship an effect belongs to: the minion itself, or the one its piece enchantment sits on.</summary>
		public static Minion StarshipOf(IEntity source) => source is Enchantment e ? e.Target as Minion : source as Minion;

		/// <summary>The pieces stored in a Starship (their set-aside copies, in the order they were added).</summary>
		public static List<IPlayable> StarshipPieces(Minion starship)
		{
			var pieces = new List<IPlayable>();
			if (starship == null) return pieces;
			for (int i = 0; i < 30; i++)
			{
				int id = starship[HsTag.STARSHIP_PIECE_BASE + i];
				if (id <= 0) break;
				if (starship.Game.IdEntityDic.TryGetValue(id, out IPlayable p)) pieces.Add(p);
			}
			return pieces;
		}

		private static readonly Dictionary<CardClass, Card> _starshipFor = new Dictionary<CardClass, Card>();

		/// <summary>The Starship token for <paramref name="cls"/> (data: STARSHIP + HIDE_HEALTH_NUMBER, no STARSHIP_PIECE; the class's
		/// own, else the Neutral one — wiki Starship: Paladin, Shaman, Warrior launch Battlecruiser; Mage and Priest the Exile's Hope).</summary>
		public static Card StarshipCardFor(CardClass cls)
		{
			lock (_starshipFor)
			{
				if (_starshipFor.TryGetValue(cls, out Card known)) return known;
				var ships = Cards.All.Where(x => x.Type == CardType.MINION && x[STARSHIP] > 0 && x[HIDE_HEALTH_NUMBER] > 0 && x[STARSHIP_PIECE] == 0).ToList();
				Card card = ships.FirstOrDefault(x => x.Class != CardClass.NEUTRAL && x.HasClass(cls))
				            ?? ships.FirstOrDefault(x => x.Class == CardClass.NEUTRAL && x[GameTag.MULTIPLE_CLASSES] == 0);
				return _starshipFor[cls] = card;
			}
		}

		/// <summary>The piece's effect enchantment (client tag 3870, its id starting with the piece's), or null.</summary>
		public static Card PieceEffectOf(Card piece) =>
			piece == null ? null : Cards.All.FirstOrDefault(x => x.Type == CardType.ENCHANTMENT && x[PIECE_EFFECT] > 0 && x.Id.StartsWith(piece.Id, StringComparison.Ordinal) &&
				x.Id.Length > piece.Id.Length && char.IsLetter(x.Id[piece.Id.Length]));

		/// <summary>
		/// A Starship Piece died (Game.GraveYard queues this before its own Deathrattle: live live-20261005/140614 Arkonite, a
		/// TRIGGER block with TriggerKeyword 3684 at effect index 0, then its Deathrattle at index 1). The owner's Starship gets it;
		/// with none, a new one is built where the piece died (wiki Starship: "When a player's first Starship Piece dies in a game,
		/// a Permanent Starship is summoned for them"; after a launch the next piece builds a new one). It gains the piece card's
		/// base stats and keywords (hearthstonetopdecks: "adding their base stats and effects together") and its effect
		/// enchantment (forum t/138331: the effects read the Starship as "this minion").
		/// </summary>
		public static readonly ISimpleTask StarshipPieceTask = new CustomTask((g, c, s, t, stack) =>
		{
			if (!(s is Minion piece)) return;
			Controller owner = piece.Controller;
			Minion ship = BuildingStarship(owner);
			if (ship == null)
			{
				if (owner.BoardZone.IsFull) return; // (no source: the piece is lost — doubt)
				Card shipCard = StarshipCardFor(owner.HeroClass);
				if (shipCard == null) return;
				ship = (Minion)Entity.FromCard(owner, shipCard, new EntityData
				{
					[GameTag.ATK] = 0, [GameTag.HEALTH] = 0, [GameTag.COST] = 0, [LAUNCHPAD] = 1, [GameTag.CREATOR] = owner.PlayerId,
				});
				NamedFx(piece, "Overridespawn", null, () =>
					SummonBlock.Invoke(g, ship, Math.Max(0, Math.Min(piece.LastBoardPosition, owner.BoardZone.Count)), null));
				if (ship.Zone?.Type != Zone.PLAY) return;
				ship.IsExhausted = true;
				IPlayable launch = Entity.FromCard(owner, Cards.FromId(LaunchCardId), new EntityData
				{
					[GameTag.CREATOR] = ship.Id, [GameTag.PARENT_CARD] = ship.Id, [LAUNCHPAD_ABILITY] = 1,
				}, owner.SetasideZone);
				ship[HsTag.STARSHIP_LAUNCH_ENTITY] = launch.Id;
				owner[HsTag.STARSHIPS_BUILT] = owner[HsTag.STARSHIPS_BUILT] + 1;
			}
			AddPiece(g, owner, ship, piece);
		});

		/// <summary>The piece flying into its Starship (live: every recorded piece — Arkonite, Viper, Starlight Reactor, Dimensional Core…
		/// — adds its stats, keywords and effect inside GDBFX_Starship_Piece_Missile_Buff_Super, Source = the piece, Targets = the
		/// Starship: RecordedSubSpellAnchors.tsv, live-20261006-dungar-student). The piece's own recorded one when it has it.</summary>
		private const string PieceIntoShipFx = "GDBFX_Starship_Piece_Missile_Buff_Super:405c86c2816bc1f4e911ffb51443f542";

		private static void AddPiece(Game g, Controller owner, Minion ship, Minion piece)
		{
			bool fx = g.History && g.OpenSubSpell(piece, Loader.SubSpells.FindNamed(piece.Card.Id, "Starship_Piece_Missile_Buff") ?? PieceIntoShipFx,
				new[] { ship.Id });
			AddPieceStats(g, owner, ship, piece);
			if (fx) g.CloseSubSpells(piece.Id);
		}

		private static void AddPieceStats(Game g, Controller owner, Minion ship, Minion piece)
		{
			Card card = piece.Card;
			ship[GameTag.ATK] = ship[GameTag.ATK] + card.ATK;
			ship[GameTag.HEALTH] = ship[GameTag.HEALTH] + card.Health;
			ship[GameTag.COST] = ship[GameTag.COST] + card.Cost;
			foreach (int tag in Loader.ReprintCards.AbilityTags)
				if (card[(GameTag)tag] > 0 && ship[(GameTag)tag] == 0 && (GameTag)tag != STARSHIP_PIECE && (GameTag)tag != STARSHIP_PIECE_KEYWORD)
					ship[(GameTag)tag] = 1;
			foreach (GameTag tag in new[] { GameTag.TAUNT, GameTag.DIVINE_SHIELD, GameTag.RUSH, GameTag.WINDFURY, GameTag.LIFESTEAL,
				         GameTag.DEATHRATTLE, HsTag.SPELLBURST, GameTag.POISONOUS, GameTag.STEALTH, STARSHIP_LAUNCH_TRIGGER, GameTag.CHARGE, GameTag.REBORN })
				if (card[tag] > 0 && ship[tag] == 0) ship[tag] = 1;
			if (piece[GameTag.PREMIUM] > ship[GameTag.PREMIUM]) ship[GameTag.PREMIUM] = piece[GameTag.PREMIUM];
			int count = StarshipPieces(ship).Count;
			if (count < 30)
			{
				IPlayable stored = Entity.FromCard(owner, card, new EntityData
				{
					[GameTag.CREATOR] = owner.PlayerId, [GameTag.PARENT_CARD] = ship.Id, [GameTag.REVEALED] = 1, [HsTag.LITERALLY_UNPLAYABLE] = 1,
				}, owner.SetasideZone);
				ship[HsTag.STARSHIP_PIECE_BASE + count] = stored.Id;
				ship[GameTag.TAG_SCRIPT_DATA_NUM_1] = count + 1;
				if (ship[GameTag.USE_ALTERNATE_CARD_TEXT] == 0) ship[GameTag.USE_ALTERNATE_CARD_TEXT] = 1;
			}
			if (PieceEffectOf(card) is Card effect)
				AddEnchantmentBlock(g, effect, piece, ship, 0, 0, 0);
		}

		/// <summary>The launch's cost: the launch card's COST less the player's discount (client GameUtils.StarshipLaunchCost).</summary>
		public static int StarshipLaunchCost(Controller c) =>
			Math.Max(0, (Cards.TryFromId(LaunchCardId)?.Cost ?? 5) - c[STARSHIP_LAUNCH_COST_DISCOUNT]);

		/// <summary>"Your next Starship launch costs (N) less" (SCV, Lock On, Salvage the Bunker): the player's discount (live: it
		/// adds up and goes back to 0 at the launch).</summary>
		public static void DiscountStarshipLaunch(Controller c, int n) => c[STARSHIP_LAUNCH_COST_DISCOUNT] = c[STARSHIP_LAUNCH_COST_DISCOUNT] + n;

		/// <summary>Can <paramref name="c"/> launch <paramref name="ship"/> now (its owner's turn, waiting, enough mana)?</summary>
		public static bool CanLaunch(Controller c, Minion ship) =>
			ship != null && ship.Controller == c && c.Game.CurrentPlayer == c && ship[LAUNCHPAD] > 0 && ship.Zone?.Type == Zone.PLAY &&
			c.ManaToSpend >= StarshipLaunchCost(c);

		/// <summary>
		/// Launch the Starship (live dungar-student 19102-19460, 140614 Celestial Archive): a PLAY block on the Starship paying the
		/// launch cost (not a play: no "after you play" counters), a POWER block making it a minion (UNTOUCHABLE 0, LAUNCHPAD 0,
		/// HIDE_STATS 0), each stored piece's launch effect in a TRIGGER block (effect index 1, keyword STARSHIP_LAUNCH_TRIGGER or
		/// TRIGGER_VISUAL), then the launch card and the stored pieces leave (REMOVEDFROMGAME) and the discount goes to 0. It counts
		/// as just played (Rush may attack minions). <paramref name="free"/>: The Exodar launches without paying.
		/// </summary>
		public static bool LaunchStarship(Controller c, Minion ship, bool free = false)
		{
			Game g = c.Game;
			if (ship == null || ship[LAUNCHPAD] == 0 || ship.Zone?.Type != Zone.PLAY) return false;
			if (!free && !CanLaunch(c, ship)) return false;
			bool history = g.History;
			bool ownBlock = !free;
			if (ownBlock && history) g.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.PLAY, ship.Id, "", 0, 0));
			if (!free)
			{
				int cost = StarshipLaunchCost(c);
				if (cost > 0)
				{
					int tempUsed = Math.Min(c.TemporaryMana, cost);
					if (tempUsed > 0) c.TemporaryMana -= tempUsed;
					c.UsedMana += cost - tempUsed;
					c.TotalManaSpentThisGame += cost;
				}
			}
			if (history)
			{
				PowerHistoryBlockStart power = PowerHistoryBuilder.BlockStart(BlockType.POWER, ship.Id, "", 0, 0);
				power.ShowInHistory = true;
				g.PowerHistory.Add(power);
			}
			ship[LAUNCHPAD] = 0;
			ship[GameTag.HIDE_STATS] = 0;
			ship[GameTag.ELITE] = 1;
			if (history) g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());

			RunLaunchEffects(c, ship);

			// the launch card and the stored pieces go; the launched Starship is remembered (Jim Raynor)
			if (ship[HsTag.STARSHIP_LAUNCH_ENTITY] > 0 && g.IdEntityDic.TryGetValue(ship[HsTag.STARSHIP_LAUNCH_ENTITY], out IPlayable launch) && launch.Zone != null)
			{
				launch.Zone.Remove(launch);
				launch[GameTag.PARENT_CARD] = 0;
				launch[GameTag.ZONE] = (int)Zone.REMOVEDFROMGAME;
			}
			int launched = c[HsTag.STARSHIPS_LAUNCHED];
			if (launched < 10) c[HsTag.LAUNCHED_STARSHIP_BASE + launched] = ship.Id;
			c[HsTag.STARSHIPS_LAUNCHED] = launched + 1;
			if (c[STARSHIP_LAUNCH_COST_DISCOUNT] > 0) c[STARSHIP_LAUNCH_COST_DISCOUNT] = 0;
			UpgradeAfterLaunch(g); // (live vfx-decks 192841: Hellion in hand transforms inside the launch, after its triggers)

			// it counts as just played: summoning sickness unless Charge / Rush
			ship[GameTag.NUM_TURNS_IN_PLAY] = 0;
			if (g.CurrentPlayer == c && !ship.HasCharge)
			{
				if (ship.IsRush) { ship.IsExhausted = false; ship.AttackableByRush = true; g.RushMinions.Add(ship.Id); }
				else ship.IsExhausted = true;
			}
			else if (ship.HasCharge) ship.IsExhausted = false;
			g.DeathProcessingAndAuraUpdate();
			if (ownBlock)
			{
				c.NumOptionsPlayedThisTurn++;
				if (history) g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
				EndPlayerPlay(g, ship);
			}
			return true;
		}

		/// <summary>The pieces of a Starship, card by card (stored copies keep the piece cards even after the launch removed them).</summary>
		public static List<Card> StarshipPieceCards(Minion ship) => StarshipPieces(ship).Select(p => p.Card).ToList();

		/// <summary>The Starships this player launched this game (Jim Raynor: "Relaunch every Starship you launched").</summary>
		public static List<Minion> LaunchedStarships(Controller c)
		{
			var ships = new List<Minion>();
			for (int i = 0; i < Math.Min(10, c[HsTag.STARSHIPS_LAUNCHED]); i++)
				if (c.Game.IdEntityDic.TryGetValue(c[HsTag.LAUNCHED_STARSHIP_BASE + i], out IPlayable p) && p is Minion m) ships.Add(m);
			return ships;
		}

		/// <summary>
		/// Xor'toth's Stars (wiki Xor'toth: "The 5 damage to all enemies is only dealt once, after the two generated spells are
		/// adjacent to each other in the player's hand"): after each sequence, a hand card with Power.CollidesWith next to that
		/// card runs its CollisionTask once; both leave the hand (set aside: no discard).
		/// </summary>
		internal static void CheckHandCollisions(Game g)
		{
			foreach (Controller c in new[] { g.Player1, g.Player2 })
			{
				if (c?.HandZone == null || c.HandZone.Count < 2) continue;
				var hand = c.HandZone.GetAll().ToList();
				for (int i = 0; i + 1 < hand.Count; i++)
				{
					IPlayable a = hand[i], b = hand[i + 1];
					IPlayable star = a.Card.Power?.CollidesWith == b.Card.Id ? a : b.Card.Power?.CollidesWith == a.Card.Id ? b : null;
					if (star == null) continue;
					IPlayable other = star == a ? b : a;
					ISimpleTask task = star.Card.Power.CollisionTask;
					RemoveFromHand(c, star);
					RemoveFromHand(c, other);
					if (task != null)
					{
						g.TaskQueue.StartEvent();
						g.TaskQueue.Execute(task, c, star, null, 0, (int)GameTag.TRIGGER_VISUAL, 0);
						g.ProcessTasks();
						g.TaskQueue.EndEvent();
					}
					return;
				}
			}
		}

		/// <summary>The client's in-hand upgrade effect per class (fx_prefabs.tsv BARFX_RankedSpell_Upgrade_Impact_*: live Hellion
		/// used the Paladin one, vfx-decks 192841).</summary>
		private static readonly Dictionary<CardClass, string> UpgradeFx = new Dictionary<CardClass, string>
		{
			[CardClass.PALADIN] = "BARFX_RankedSpell_Upgrade_Impact_Holy_Paladin:bf5b5dbee69a5534488d425e35df5769",
			[CardClass.SHAMAN] = "BARFX_RankedSpell_Upgrade_Impact_Lightning_Shaman:cbf3a2a738c7f5e43bd9b9a147ceaecf",
			[CardClass.WARRIOR] = "BARFX_RankedSpell_Upgrade_Impact_Physical_Warrior:fde8a0bd6b5f6ba4c86a02bf6aa6da73",
			[CardClass.PRIEST] = "BARFX_RankedSpell_Upgrade_Impact_Holy_Priest:b662b4955d089b4438e6457f8e6f6482",
			[CardClass.MAGE] = "BARFX_RankedSpell_Upgrade_Impact_Frost_Mage:c39cad0b4c163d648b2474cf3544bc81",
			[CardClass.ROGUE] = "BARFX_RankedSpell_Upgrade_Impact_Sneaky_Rogue:82287664ac59b8c4c901a02b2ff91ead",
			[CardClass.HUNTER] = "BARFX_RankedSpell_Upgrade_Impact_Physical_Hunter:a2b9de43a878f2b4dbea861d439d46d0",
			[CardClass.DRUID] = "BARFX_RankedSpell_Upgrade_Impact_Nature_Druid:adea29e0a6b630a49a8d20b57cd1f851",
			[CardClass.WARLOCK] = "BARFX_RankedSpell_Upgrade_Impact_Fel_Warlock:368d1b5dc7248eb4f8eca235f34a1b3d",
			[CardClass.DEMONHUNTER] = "BARFX_RankedSpell_Upgrade_Impact_Fel_DemonHunter:4d572b53a8719874bb96b63eb4799b98",
		};

		/// <summary>
		/// "(Transforms if you launched a Starship this game.)" (Hellion, Siege Tank, Thor; Power.UpgradeAfterLaunch): once a player
		/// launched a Starship (their own launches and The Exodar's — wiki Thor: "Relaunching Starships with Jim Raynor will not
		/// upgrade Thor"), such cards in their hand transform, at the launch and whenever one enters the hand later (after each
		/// sequence). Live (vfx-decks 192841): a TRIGGER block on the card (effect index 0), TARGET itself, HAS_ACTIVATE_POWER 0,
		/// SUB_SPELL BARFX_RankedSpell_Upgrade_Impact_<class> (Source and Targets the card) around the 1069 pulse and the
		/// CHANGE_ENTITY, HAS_ACTIVATE_POWER 1.
		/// </summary>
		internal static void UpgradeAfterLaunch(Game g)
		{
			foreach (Controller c in new[] { g.Player1, g.Player2 })
			{
				if (c?.HandZone == null || c[HsTag.STARSHIPS_LAUNCHED] <= 0) continue;
				foreach (IPlayable card in c.HandZone.GetAll().ToList())
				{
					if (!(card.Card.Power?.UpgradeAfterLaunch is string id) || !(Cards.TryFromId(id) is Card upgraded) || upgraded == card.Card) continue;
					bool activatePower = HasActivatePowerInHand(card);
					if (g.History)
					{
						g.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, card.Id, "", 0, 0));
						g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.TARGET, Data = 0, Info = new List<int> { card.Id } });
					}
					if (activatePower) card[HasActivatePower] = 0;
					bool open = g.History && UpgradeFx.TryGetValue(card.Card.Class, out string fx) && g.OpenSubSpell(card, fx, new[] { card.Id });
					IPlayable now = ChangeEntityBlock(c, card, upgraded, false); // (its 1069 pulse: ChangeEntityCore)
					if (open) g.CloseSubSpells(card.Id);
					if (activatePower && now != null) now[HasActivatePower] = 1;
					if (g.History) g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
				}
			}
		}

		/// <summary>
		/// "Play another Templar to merge into an Archon!" (Dark Templar, High Templar; Power.MergesInto). Wiki Dark Templar: "When
		/// Dark Templar is on the battlefield, playing another Dark Templar or High Templar will fuse them together and summon
		/// Archon." / "The transformation doesn't happen in-hand; Both Templars must be played." An after-play trigger on the
		/// Templar in play: when another partner (same MergesInto) is played and both are still in play, both leave play without
		/// dying (no Deathrattles) and the merged card is summoned in the first one's slot.
		/// </summary>
		public static Triggers.Trigger MergeTrigger() => new Triggers.Trigger(TriggerType.AFTER_PLAY_MINION)
		{
			TriggerSource = TriggerSource.FRIENDLY,
			Condition = new Conditions.SelfCondition(p => p.Card.Power?.MergesInto != null),
			SingleTask = new CustomTask((g, c, s, t, stack) =>
			{
				if (!(s is Minion me) || !(g.CurrentEventData?.EventSource is Minion played) || played == me) return;
				string into = me.Card.Power?.MergesInto;
				if (into == null || played.Card.Power?.MergesInto != into || !(Cards.TryFromId(into) is Card merged)) return;
				if (me.Zone?.Type != Zone.PLAY || played.Zone?.Type != Zone.PLAY || me.ToBeDestroyed || played.ToBeDestroyed) return;
				int slot = me.ZonePosition;
				RemoveFromGame(played, me);
				RemoveFromGame(me, me);
				SummonBlock.Invoke(g, (Minion)Entity.FromCard(c, merged, new EntityData { [GameTag.CREATOR] = me.Id }),
					Math.Max(0, Math.Min(slot, c.BoardZone.Count)), me);
			})
		};

		/// <summary>
		/// A stat-setting enchantment that can end ("While you have exactly 1 minion, its Attack and Health are 10": Celestial
		/// Aura): the target's own Attack / Health (without auras) are recorded on it before the set, so <see cref="EndStatSet"/> can
		/// take it back. Returns the enchantment.
		/// </summary>
		public static Enchantment GiveStatSet(Game g, IPlayable source, Minion target, string enchantmentId)
		{
			if (target == null || !(Cards.TryFromId(enchantmentId) is Card card)) return null;
			int atk = target._modifiedATK ?? target.Card.ATK, health = target._modifiedHealth ?? target.Card.Health;
			AddEnchantmentBlock(g, card, source, target, 0, 0, 0);
			Enchantment made = target.AppliedEnchantments?.LastOrDefault(e => e.Card == card);
			if (made == null) return null;
			made[HsTag.STAT_SET_BASE_ATK] = atk;
			made[HsTag.STAT_SET_BASE_HEALTH] = health;
			made[HsTag.STAT_SET_RECORDED] = 1;
			return made;
		}

		/// <summary>
		/// A stat set from <see cref="GiveStatSet"/> ends: Attack and maximum Health go back to the recorded values plus what was
		/// gained since the set (buffs after it stack on top — wiki Celestial Aura), current Health per the rule book (Advanced
		/// rulebook § Health H2: "Lowering maximum Health only lowers current Health if current exceeds the new maximum"); then
		/// the enchantment goes. Any other enchantment is just removed.
		/// </summary>
		public static void EndStatSet(Enchantment e)
		{
			if (e == null) return;
			if (e[HsTag.STAT_SET_RECORDED] > 0 && e.Target is Minion m && e.Card.Power?.Enchant?.Effects is SabberStoneCore.Enchants.IEffect[] effects)
			{
				int? setAtk = null, setHealth = null;
				foreach (SabberStoneCore.Enchants.IEffect fx in effects)
					if (fx.Operator == SabberStoneCore.Enchants.EffectOperator.SET && fx.Tag == GameTag.ATK) setAtk = fx.Value;
					else if (fx.Operator == SabberStoneCore.Enchants.EffectOperator.SET && fx.Tag == GameTag.HEALTH) setHealth = fx.Value;
				// (the own values, without auras: _modifiedATK / _modifiedHealth; the ATK / HEALTH setters take those)
				if (setAtk is int a)
					m[GameTag.ATK] = (m._modifiedATK ?? a) - a + e[HsTag.STAT_SET_BASE_ATK];
				if (setHealth is int h)
				{
					int current = m.Health;
					m[GameTag.HEALTH] = (m._modifiedHealth ?? h) - h + e[HsTag.STAT_SET_BASE_HEALTH];
					int maxNow = m.BaseHealth;
					m.Damage = Math.Max(0, maxNow - Math.Min(current, maxNow));
				}
			}
			e.Remove();
		}

		/// <summary>
		/// "Can attack again" (Exarch Akama: "After this attacks, all other friendly minions can attack again"): one more attack
		/// this turn (Generic.AttacksPerTurn), and a minion that used its attacks may attack again — only one that could attack
		/// this turn (wiki Exarch Akama: "Minions lacking the ability to attack that turn cannot attack from this effect"; "Extra
		/// attacks can accumulate"; "Rush minions played that turn can only attack enemy minions again").
		/// </summary>
		public static void GrantExtraAttack(Minion m)
		{
			if (m == null || m is Location || m.Untouchable || m.Zone?.Type != Zone.PLAY) return;
			Game g = m.Game;
			int extra = m[HsTag.EXTRA_ATTACKS_TURN] == g.Turn ? m[HsTag.EXTRA_ATTACKS] : 0;
			m[HsTag.EXTRA_ATTACKS_TURN] = g.Turn;
			m[HsTag.EXTRA_ATTACKS] = extra + 1;
			bool canAttackThisTurn = m.Controller == g.CurrentPlayer && !m.CantAttack && !m.IsFrozen &&
			                         (m[GameTag.NUM_TURNS_IN_PLAY] > 0 || m.HasCharge || m.IsRush || m.NumAttacksThisTurn > 0);
			if (canAttackThisTurn && m.IsExhausted && m.NumAttacksThisTurn < AttacksPerTurn(m))
				m.IsExhausted = false;
		}

		/// <summary>Each piece's launch effect on the Starship, in the order the pieces were added (a TRIGGER block each, effect index
		/// 1, keyword STARSHIP_LAUNCH_TRIGGER or TRIGGER_VISUAL); then the Starship's STARSHIP_LAUNCH_TRIGGER goes to 0.</summary>
		private static void RunLaunchEffects(Controller c, Minion ship)
		{
			Game g = c.Game;
			foreach (Enchantment e in ship.AppliedEnchantments?.Where(x => x.Card.Power?.LaunchTask != null).ToList() ?? new List<Enchantment>())
			{
				if (ship.Zone?.Type != Zone.PLAY) break;
				Card pieceCard = Cards.All.FirstOrDefault(x => x[STARSHIP_PIECE] > 0 && e.Card.Id.StartsWith(x.Id, StringComparison.Ordinal));
				int keyword = pieceCard != null && pieceCard[STARSHIP_LAUNCH_TRIGGER] > 0 ? (int)STARSHIP_LAUNCH_TRIGGER : (int)GameTag.TRIGGER_VISUAL;
				g.TaskQueue.StartEvent();
				g.TaskQueue.Execute(e.Card.Power.LaunchTask, c, ship, null, 0, keyword, 1);
				g.ProcessTasks();
				g.TaskQueue.EndEvent();
			}
			if (ship[STARSHIP_LAUNCH_TRIGGER] > 0) ship[STARSHIP_LAUNCH_TRIGGER] = 0;
		}

		/// <summary>Client tag IS_RELAUNCHED_STARSHIP (4030): a Starship Jim Raynor relaunched.</summary>
		public const GameTag IS_RELAUNCHED_STARSHIP = (GameTag)4030;

		/// <summary>
		/// "Relaunch every Starship you launched this game" (Jim Raynor). Live (dungar-student 30040-30260): a NEW Starship of the
		/// same card enters play already launched (IS_RELAUNCHED_STARSHIP 1), made of the same pieces (their stats, keywords and
		/// effect enchantments again), then its launch effects fire again. It is not a launch: no cost, no discount reset, not
		/// counted (wiki Thor: "Relaunching Starships with Jim Raynor will not upgrade Thor").
		/// </summary>
		public static Minion RelaunchStarship(Controller c, Minion launched, IEntity source)
		{
			Game g = c.Game;
			if (launched == null || c.BoardZone.IsFull) return null;
			var ship = (Minion)Entity.FromCard(c, launched.Card, new EntityData
			{
				[GameTag.ATK] = 0, [GameTag.HEALTH] = 0, [GameTag.COST] = 0, [IS_RELAUNCHED_STARSHIP] = 1,
				[GameTag.CREATOR] = source?.Id ?? c.PlayerId,
			});
			List<IPlayable> pieces = StarshipPieces(launched);
			SummonBlock.Invoke(g, ship, -1, source);
			if (ship.Zone?.Type != Zone.PLAY) return null;
			foreach (IPlayable piece in pieces)
				if (piece is Minion stored) AddPiece(g, c, ship, stored);
			ship[GameTag.ELITE] = 1;
			RunLaunchEffects(c, ship);
			return ship;
		}
	}
}
