// HsServer addition (not upstream SabberStone).
namespace SabberStoneCore.Enums
{
	/// <summary>
	/// Internal rule tags (ids &gt;= 900000, never sent to the client). A card's aura sets one on its controller (or itself)
	/// and the engine reads the tag, the way the real game marks rules with tags; no engine code looks for card ids.
	/// Client tags are used where the game has one (CANT_BE_FROZEN, MEGA_WINDFURY, STEADY_SHOT_CAN_TARGET, CANT_ATTACK,
	/// CARD_COSTS_HEALTH).
	/// </summary>
	public static class HsTag
	{
		/// <summary>Controller (2026-10-08): its hero was dead at a Death Creation Step — the game is lost even if a new hero replaces it
		/// (Advanced_rulebook.txt:713-717; Game.GraveYard latches it, CheckGameOver reads it).</summary>
		public const GameTag HERO_DEATH_LATCHED = (GameTag)990820;
		/// <summary>Controller (2026-10-08): the damage its own sources dealt to the enemy hero this game (Armor included) — the "deal damage
		/// to enemy heroes" quests: wiki Quest.txt:90 "Does not count damage caused by fatigue or the opponent's sources" (Character.TakeDamage).</summary>
		public const GameTag DAMAGE_DEALT_TO_ENEMY_HERO = (GameTag)990821;
		/// <summary>Objective / Paladin Aura (2026-10-08): the game turn it entered the Secret zone, and which script-data slot (1-3) carries
		/// its turns-elapsed counter (the first free one then) — Generic.ObjectiveTick.</summary>
		public const GameTag OBJECTIVE_CAST_TURN = (GameTag)990822, OBJECTIVE_COUNTER_SLOT = (GameTag)990823;
		/// <summary>Player (2026-10-08): its Mana Crystals went up at MAIN_READY; its ranked spells rank up after that step's block
		/// (live 212735 8925) — Game.NextStep.</summary>
		public const GameTag RANKS_PENDING = (GameTag)990824;
		/// <summary>Enchantment (2026-10-08): what a devourer holds was destroyed for good (Gigafin's Maw; the hold: EATEN_COUNT / EATEN_BASE).</summary>
		public const GameTag EATEN_GONE = (GameTag)990826;
		/// <summary>Player (2026-10-08): it started the game disguised (Maestra): its real hero card's dbf id, until the reveal.</summary>
		public const GameTag DISGUISED_REAL_HERO = (GameTag)990827;
		/// <summary>Controller: its Secrets trigger twice (Commander Rhyssa).</summary>
		public const GameTag SECRETS_TRIGGER_TWICE = (GameTag)990020;
		/// <summary>Controller: its Secrets can't trigger (Tight-Lipped Witness, on both players).</summary>
		public const GameTag SECRETS_CANT_TRIGGER = (GameTag)990021;
		/// <summary>Controller: it gains twice as many Corpses.</summary>
		public const GameTag CORPSE_GAINS_DOUBLED = (GameTag)990022;
		/// <summary>Controller: its Infuse cards also Infuse while in its deck (Afterlife Attendant).</summary>
		public const GameTag INFUSE_IN_DECK = (GameTag)990023;
		/// <summary>Weapon: it doesn't lose Durability (Stormhammer while you control a Dragon).</summary>
		public const GameTag KEEPS_DURABILITY = (GameTag)990024;

		/// <summary>Controller: its next Healing effect deals damage instead (Ruby Sanctum); used up by the next heal source.</summary>
		public const GameTag NEXT_HEAL_DAMAGES = (GameTag)990026;
		/// <summary>Controller: the entity whose heals are being turned into damage by NEXT_HEAL_DAMAGES.</summary>
		public const GameTag NEXT_HEAL_SOURCE = (GameTag)990027;
		/// <summary>Card (internal): up to three cards Zuramat's Prison discarded (card asset ids).</summary>
		public const GameTag PRISON_DISCARD_1 = (GameTag)990031, PRISON_DISCARD_2 = (GameTag)990032, PRISON_DISCARD_3 = (GameTag)990033;

		/// <summary>Minion: is taking damage redirected from another minion (Snapjaw Shellfighter) — it isn't redirected again.</summary>
		public const GameTag TAKING_REDIRECTED_DAMAGE = (GameTag)990034;
		/// <summary>Client tags: the card can't be played now, and shows it (Low Security Wing's lock).</summary>
		public const GameTag LITERALLY_UNPLAYABLE = (GameTag)1020, UNPLAYABLE_VISUALS = (GameTag)2798;

		/// <summary>Client keyword tags the engine enum lacks. The client animates a TRIGGER block by its TriggerKeyword
		/// (TriggerSpellController.GetActorTriggerSpellType), so a card's trigger names its keyword.</summary>
		public const GameTag SPELLBURST = (GameTag)1427, NON_KEYWORD_SPELLBURST = (GameTag)2672, FRENZY = (GameTag)1637,
			HONORABLE_KILL = (GameTag)1920, AVENGE = (GameTag)2129, OVERHEAL = (GameTag)2821, END_OF_TURN_TRIGGER = (GameTag)3744;

		/// <summary>Keywords whose trigger has its own client animation, in the order a card's trigger picks them.</summary>
		public static readonly GameTag[] TriggerKeywords =
			{ GameTag.START_OF_GAME, SPELLBURST, NON_KEYWORD_SPELLBURST, FRENZY, HONORABLE_KILL, AVENGE, GameTag.INSPIRE, OVERHEAL, END_OF_TURN_TRIGGER, LOCK_VISUAL, OBJECTIVE, GameTag.TRIGGER_VISUAL };

		/// <summary>
		/// Whether a card's keyworded TRIGGER block shows in the history (live ShowInHistory): yes for its own trigger
		/// (TRIGGER_VISUAL, Deathrattle, lock, cast-when-drawn, Objective…); no for the ones the client attaches to the
		/// attack/damage entry already on screen (Lifesteal, Overkill, Poisonous, Venomous, Honorable Kill), Freeze and an
		/// expiring one-turn effect.
		/// </summary>
		/// <summary>HsServer: TRIGGER blocks live marks IsDeferrable (all 23 live OVERHEAL blocks: audit 2026-10-08, visuals.md).</summary>
		public static bool IsDeferrable(int keyword) => keyword == (int)OVERHEAL;

		public static bool ShowsInHistory(int keyword) => keyword != 0 && !(keyword is 685 or 1675 or 923 or 363 or 1944 or 2853 or 1920
			or (int)GameTag.FREEZE or (int)GameTag.TAG_ONE_TURN_EFFECT
			// live: a Secret's TRIGGER block isn't flagged (59 of 71; only a few cards' are: Duplicate, Mysterious Summoning)
			or (int)GameTag.SECRET);

		/// <summary>Client player counters live keeps (card text "(X left!)", conditions): corpses spent this game, damage
		/// the player's hero took during the opponent's last turn, armor gained this game, the equipped weapon.</summary>
		public const GameTag CORPSES_SPENT_THIS_GAME = (GameTag)2639, DAMAGE_DEALT_TO_HERO_LAST_TURN = (GameTag)2349,
			ARMOR_GAINED_THIS_GAME = (GameTag)1828, MAIN_HAND_WEAPON_ENTITY = (GameTag)334;

		/// <summary>Client tags: DIVINE_SHIELD_DAMAGE (a shield absorbed a hit, flagged for the action), ELUSIVE.</summary>
		public const GameTag DIVINE_SHIELD_DAMAGE = (GameTag)3859, ELUSIVE = (GameTag)1211;
		/// <summary>Client tag: no summon voice line for this entity (live sets it around a transform).</summary>
		public const GameTag SUPPRESS_ALL_SUMMON_VO = (GameTag)1458;
		/// <summary>Client tags: a Discover option (2509); a copy's original entity (1565).</summary>
		public const GameTag WAS_DISCOVER_OPTION = (GameTag)2509, COPIED_FROM_ENTITY_ID = (GameTag)1565;

		/// <summary>On a card that replaces the played card in its hand slot (Twinspell's copy): with its CREATOR's
		/// CLIENT_LIST_REPLACEMENTS_WHEN_PLAYED (a tag list the adapter sends), the client plays TWINSPELL_SUMMON_IN.</summary>
		public const GameTag CREATED_AS_ON_PLAY_REPLACEMENT = (GameTag)3882, CLIENT_LIST_REPLACEMENTS_WHEN_PLAYED = (GameTag)3677;

		/// <summary>On a player: their spells cast twice (Stormcaller Bru'kan's SW_031t7e; Generic.CastSpell).</summary>
		public const GameTag SPELLS_CAST_TWICE = (GameTag)1681;

		/// <summary>QUESTLINE (1725): a Quest in stages; QUESTLINE_PART (1993) its stage. A stage's QUEST_REWARD_DATABASE_ID is
		/// the next stage (the last stage's: the final reward card). Tasks/SimpleTasks/QuestlineStageTask.</summary>
		public const GameTag QUESTLINE = (GameTag)1725, QUESTLINE_PART = (GameTag)1993;

		/// <summary>Card data (unnamed in the client): a Hero Power's upgraded version (dbf id; Reinforce → The Silver Hand).</summary>
		public const GameTag UPGRADED_HERO_POWER = (GameTag)1086;

		/// <summary>Internal: the game turn a card was drawn on (Final Showdown's "the cards drawn"); on a player, an
		/// enchantment (dbf id) each card they draw gets (Demonslayer Kurtrus); on a Questline stage, 991000 + key marks
		/// what was already counted ("different" cards, costs, schools, spells).</summary>
		public const GameTag DRAWN_ON_TURN = (GameTag)990040, DRAWN_CARD_ENCHANTMENT = (GameTag)990041, QUEST_COUNTED = (GameTag)991000;
		/// <summary>Card: the game turn it last entered a hand (any way; the starting hand: before turn 1) — Quickdraw.</summary>
		public const GameTag ADDED_TO_HAND_TURN = (GameTag)990042;
		/// <summary>Card: it is being played from hand the turn it entered it (Quickdraw is on for this play).</summary>
		public const GameTag QUICKDRAW_PLAY = (GameTag)990043;
		/// <summary>Client card tag: Quickdraw (2905).</summary>
		public const GameTag QUICKDRAW = (GameTag)2905;
		/// <summary>Controller: cards Forged this game.</summary>
		public const GameTag FORGES_THIS_GAME = (GameTag)990044;
		/// <summary>On a player: the turn in which their next card costs Corpses instead of Mana (Exarch Maladaar);
		/// cleared when a card is paid that way.</summary>
		public const GameTag NEXT_CARD_COSTS_CORPSES = (GameTag)990045;
		/// <summary>On a player: how many turn-time limits are active (aura; Nozdormu) and the sum of their seconds
		/// (<see cref="SabberStoneCore.Model.Entities.Controller.TurnLimitSeconds"/>); the server's turn timer reads them.</summary>
		public const GameTag TURN_LIMITS = (GameTag)990046, TURN_LIMIT_SECONDS = (GameTag)990047;

		/// <summary>Spell: its Spell Damage is already counted by the effect, so its hits get none (Generic.DamageCharFunc):
		/// in its number of hits (Arcane Missiles, Avenging Wrath: EnqueueTask / EnqueueNumberTask spellDmg; rule book
		/// Spell_Damage: "the spell will thus shoot an increased number of missiles, but each one will still deal 1 damage"),
		/// or in a running total a script splits (Rolling Fireball's excess, Collateral Damage's excess to the hero).</summary>
		public const GameTag SPELL_DAMAGE_COUNTED = (GameTag)990048;

		/// <summary>Secret: it triggered; it goes to the graveyard after its TRIGGER block (live, 74 of 74: the block holds only
		/// the effect, then SLUSH_TIME 5000 on the Secret, then ZONE GRAVEYARD; no REVEALED: TaskProcessor).</summary>
		public const GameTag SECRET_TRIGGERED = (GameTag)990049;

		/// <summary>On a player (controller aura): its characters can't be healed (Repulsive Gargantuan on the opponent:
		/// "Enemy characters can't be healed"). Character.TakeHeal reads it with the character's own CANT_BE_HEALED.</summary>
		public const GameTag CHARACTERS_CANT_BE_HEALED = (GameTag)990050;

		/// <summary>Minion: its Dormant counts down turns (TAG_SCRIPT_DATA_NUM_2, BoardZone.AwakenDormant); without it, only its
		/// condition awakens it (Generic.Awaken / AwakenProgress). Its condition's progress (The Darkness: Candles drawn).
		/// A copy of a Dormant minion that already awakened, made straight into play: it doesn't start Dormant (Generic.Copy;
		/// wiki Dormant).</summary>
		public const GameTag DORMANT_TIMED = (GameTag)990051, AWAKEN_PROGRESS = (GameTag)990052, AWAKENED_COPY = (GameTag)990053;

		/// <summary>Minion: the turns a turn-counted Dormant still sleeps (live sends no count on the minion: its counter
		/// enchantment shows it); the entity id of its counter enchantment (Actions/Dormant.cs).</summary>
		public const GameTag DORMANT_TURNS_LEFT = (GameTag)990054, DORMANT_COUNTER = (GameTag)990055;

		/// <summary>Player: the card whose play is still resolving (set when it's played, cleared when its play ends). It
		/// isn't one of the "cards you've played" its own effect counts (Generic.CardsPlayedBefore).</summary>
		public const GameTag RESOLVING_PLAY = (GameTag)990056;

		/// <summary>Client card tags marking a deck-gated mechanic family: its cards are Discovered only by a player whose deck
		/// has a card of the family (Cards.DeckAllows). 3623 (unnamed): the 5 Galakronds + the 17 Invoke cards; 4538
		/// (unnamed): the 21 Herald cards; 3692 CARES_ABOUT_IMBUE_CARDS: the 19 Imbue cards + Merry Moonkin. (Excavate and
		/// C'Thun have no such tag and aren't gated: user 2026-10-05.)</summary>
		/// <summary>Player (an aura on both, Mo'arg Artificer): minions take double damage from spells, once per aura — the
		/// rule book's damage-doubling step (DH2a: "Then apply damage-doubling effects"), after Spell Damage.</summary>
		public const GameTag MINIONS_TAKE_DOUBLE_SPELL_DAMAGE = (GameTag)990057;

		/// <summary>A minion that is "Permanently Stealthed" (Akama Prime; an aura of its own, so Silence ends it): its Stealth
		/// never breaks — attacking or dealing damage doesn't remove it, so it never comes back either (user 2026-10-05: "it
		/// doesn't disappear to begin with, if it did it will retrigger any stealth based synergies").</summary>
		public const GameTag PERMANENT_STEALTH = (GameTag)990058;

		/// <summary>Player (an aura, Professor Slate): your spells are Poisonous — a minion they damage is destroyed.</summary>
		public const GameTag SPELLS_POISONOUS = (GameTag)990059;

		/// <summary>Player: how many times their hero's Health changed during their own turns (Flesh Giant), and the game turn
		/// it last changed (Brittlebone Destroyer: "this turn"). Health, not Armor: damage Armor absorbs doesn't count.</summary>
		public const GameTag HERO_HEALTH_CHANGES_OWN_TURNS = (GameTag)990060, HERO_HEALTH_CHANGED_TURN = (GameTag)990061;

		/// <summary>Spell: both players' "died this game" counts when its effect began (Headmaster Kel'Thuzad: "if the spell
		/// destroys any minions").</summary>
		public const GameTag DIED_BEFORE_SPELL_MINE = (GameTag)990062, DIED_BEFORE_SPELL_THEIRS = (GameTag)990063;

		/// <summary>A hand card set aside until the end of the turn (Secret Passage, Mindrender Illucia): its player's
		/// set-aside enchantment's id. A card that only lives in the hand until then (the Passage's deck cards, Illucia's
		/// copies): HAND_UNTIL_TURN_END = that enchantment's id.</summary>
		public const GameTag SET_ASIDE_HAND = (GameTag)990064, HAND_UNTIL_TURN_END = (GameTag)990065;

		/// <summary>A played Choose One card: the option picked (1, 2; 0 = both). Its Overload is the option's (Runic Carvings:
		/// only "Alarm the Forest" overloads).</summary>
		public const GameTag CHOSEN_OPTION = (GameTag)990066;

		/// <summary>Client card tags (missing from the engine's enum): CORRUPT (the card has Corrupt) and CORRUPTED_CARD (it is a
		/// Corrupted version).</summary>
		public const GameTag CORRUPT = (GameTag)1524, CORRUPTED_CARD = (GameTag)1551;

		/// <summary>Player: how many of their Secrets have triggered (been revealed) this game (Sayge, Seer of Darkmoon).</summary>
		public const GameTag SECRETS_REVEALED = (GameTag)990067;

		/// <summary>Player (Il'gynoth's aura): their Lifesteal damages the enemy hero instead of healing them.</summary>
		public const GameTag LIFESTEAL_DAMAGES_ENEMY = (GameTag)990068;
		/// <summary>Player (Deathwarden's aura on both players: "Deathrattles can't trigger").</summary>
		public const GameTag DEATHRATTLES_CANT_TRIGGER = (GameTag)990069;
		/// <summary>Character (Moonfang): "Can only take 1 damage at a time" — after damage doubling (rule book: "Moonfang will
		/// take 1 damage from spells, even when doubled by Mo'arg").</summary>
		public const GameTag TAKES_ONE_DAMAGE = (GameTag)990070;
		/// <summary>Player (Lightsteed's aura): their healing also gives the minions it affects +2 Health (wiki: "The minions do
		/// not have to actually be healed, only affected by a source of healing").</summary>
		public const GameTag HEALING_GIVES_HEALTH = (GameTag)990071;
		/// <summary>Client player tags (the engine's enum lacks them): which of C'Thun's four pieces the player has played since
		/// the last reform — live sets each to 1 when its piece is played and all to 0 when C'Thun reforms; the pieces'
		/// "(@/4)" counts them.</summary>
		public const GameTag PLAYED_CTHUN_EYE = (GameTag)1764, PLAYED_CTHUN_BODY = (GameTag)1765, PLAYED_CTHUN_MAW = (GameTag)1766,
			PLAYED_CTHUN_HEART = (GameTag)1767;
		/// <summary>A Corrupt card in hand that the card being played will corrupt: fixed when that card is played (live
		/// marks it then with an unnamed tag, 2003), corrupted at the end of the play.</summary>
		public const GameTag CORRUPTING = (GameTag)990073;
		/// <summary>Acrobatics' player enchantment: which of its two drawn cards were played this turn (bits 1 / 2).</summary>
		public const GameTag ACROBATICS_PLAYED = (GameTag)990074;
		/// <summary>Player (Auctioneer Jaxon's aura): a Trade Discovers a card from the deck to draw instead.</summary>
		public const GameTag TRADE_DISCOVERS = (GameTag)990075;
		/// <summary>Player (Voidtouched Attendant's aura on both players): their hero takes this much extra damage from all sources.</summary>
		public const GameTag HERO_EXTRA_DAMAGE = (GameTag)990076;
		/// <summary>Player: how deep "repeat the last …" effects are nested now (a repeat of a repeat can't loop).</summary>
		public const GameTag REPEAT_DEPTH = (GameTag)990077;
		/// <summary>Character: it was Honorably Killed (took exactly lethal damage on its enemy's turn: Korrak the Bloodrager).</summary>
		public const GameTag HONORABLY_KILLED = (GameTag)990078;
		/// <summary>Character: "Frozen until …" (Frozen Mammoth, Isiset's Constructs): the end-of-turn thaw leaves it Frozen; the
		/// effect that ends it unfreezes it.</summary>
		public const GameTag STAYS_FROZEN = (GameTag)990079;
		/// <summary>Client card tag (unnamed in the client enum, only Saidan the Scarlet carries it): its Attack / Health gains
		/// are doubled, wherever it is (Effect.Gain).</summary>
		public const GameTag DOUBLES_STAT_GAINS = (GameTag)2028;
		/// <summary>Client card tag (unnamed in the client enum): one of Kazakusan's Treasures (exactly its 28 RelatedCards).</summary>
		public const GameTag KAZAKUSAN_TREASURE = (GameTag)2054;
		/// <summary>Player: Abyssal Curses this player has given (each new one deals 1 more).</summary>
		public const GameTag CURSES_GIVEN = (GameTag)990080;
		/// <summary>An Abyssal Curse: the PlayerId of the player who gave it (Za'qul heals that player).</summary>
		public const GameTag CURSE_GIVER = (GameTag)990081;
		/// <summary>A card in hand: how many plays its owner had made when it entered the hand ("the spells you've cast while
		/// holding this": the PlayHistory after that). 0 for the starting hand.</summary>
		public const GameTag HELD_SINCE_PLAY = (GameTag)990082;
		/// <summary>Hero: its extra attacks this turn may only target minions (Multi-Strike).</summary>
		public const GameTag EXTRA_ATTACK_MINIONS_ONLY = (GameTag)990083;

		/// <summary>Player: how many of their minions / weapons had died (Controller.DiedThisGame) when their last turn ended —
		/// "a friendly Undead died after your last turn" counts the ones after it (Generic.UndeadDiedAfterYourLastTurn).</summary>
		public const GameTag DIED_AT_LAST_TURN_END = (GameTag)990084;

		/// <summary>Player: the turn their spells can't target heroes (Silvermoon Arcanist; Spell.TargetingRequirements); and
		/// the player enchantment showing it (RLK_218e3).</summary>
		public const GameTag SPELLS_CANT_TARGET_HEROES_TURN = (GameTag)990085, SPELLS_CANT_TARGET_HEROES_SHOWN = (GameTag)990086;

		/// <summary>Player (an aura, Enchanter): enemy minions take double damage during this player's turn, once per aura —
		/// the damage-doubling step (Generic.DamageCharFunc).</summary>
		public const GameTag ENEMY_MINIONS_DOUBLE_DAMAGE_ON_MY_TURN = (GameTag)990087;

		/// <summary>Player: the dbf ids of the three minions Souleater's Scythe consumed (990088..990090; its Bound Souls
		/// Discover them).</summary>
		public const GameTag SCYTHE_SOUL_1 = (GameTag)990088;

		/// <summary>Player: how many times their hero attacked this game (Shockspitter; Actions/Attack.cs).</summary>
		public const GameTag HERO_ATTACKS_THIS_GAME = (GameTag)990091;

		/// <summary>Player (an aura, Vexallus): their Arcane spells cast twice (Actions/CastSpell.cs, with SPELLS_CAST_TWICE).</summary>
		public const GameTag ARCANE_SPELLS_CAST_TWICE = (GameTag)990092;

		/// <summary>Player: Grand Magister Rommath's "(Once per game)" used.</summary>
		public const GameTag ROMMATH_USED = (GameTag)990093;

		/// <summary>Player: the last game turn their summoned Dragons gain Taunt and Divine Shield (Timewarden: "until the end
		/// of your next turn"; Actions/Summon.cs gives RLK_527e2).</summary>
		public const GameTag DRAGONS_GAIN_TAUNT_SHIELD_UNTIL = (GameTag)990094;

		/// <summary>Player: the turn their next Paladin minion costs Health instead of Mana (Blood Crusader; Playable.CostsHealth,
		/// used up when one is played).</summary>
		public const GameTag NEXT_PALADIN_MINION_COSTS_HEALTH = (GameTag)990095;

		/// <summary>Player: Riff Stoppers waiting (ETC_363e2: the next Riff's "Finale: Play your last Riff" uses one up instead;
		/// the count works without the history-only enchantment instance).</summary>
		public const GameTag RIFF_STOPPERS = (GameTag)990096;

		/// <summary>Player: characters their effects Overhealed this game (Character.Overheal; "for each one Overhealed").</summary>
		public const GameTag OVERHEALS_DONE = (GameTag)990097;

		/// <summary>Player: their Hero Power gains this much more Armor / gives this much more Attack (Free Spirit, Groovy
		/// Cat: Actions/HeroPower.cs).</summary>
		public const GameTag HERO_POWER_EXTRA_ARMOR = (GameTag)990098, HERO_POWER_EXTRA_ATTACK = (GameTag)990099;

		/// <summary>Player: Lightshows they cast this game ("Your future Lightshows shoot one more beam").</summary>
		public const GameTag LIGHTSHOWS_CAST = (GameTag)990100;

		/// <summary>Player: Rock Duel's Molten Pick passed this turn (it explodes at the end of its holder's turn).</summary>
		public const GameTag ROCK_PICK_PASSED_TURN = (GameTag)990101;

		/// <summary>Player: the cost Symphony of Sins / Photographer Fizzle used ("Once per game").</summary>
		public const GameTag FIZZLE_SNAPSHOT_TAKEN = (GameTag)990102;

		/// <summary>Player: Love Everlasting's discount lasts while they play a spell each of their turns.</summary>
		public const GameTag SPELL_PLAYED_TURN = (GameTag)990103;

		/// <summary>A spell DJ Manastorm set to (0): the Manastorm's entity id (the others cost (1) more after one is cast).</summary>
		public const GameTag MANASTORM_DISCOUNT = (GameTag)990104;

		/// <summary>Player: Armor their hero gained this turn, and the turn that count belongs to (Character.GainArmor).</summary>
		public const GameTag ARMOR_GAINED_THIS_TURN = (GameTag)990105, ARMOR_GAINED_TURN = (GameTag)990106;

		/// <summary>Player: an "after you gain Armor, gain N more" bonus is being given (Razorfen Rockstar): that gain doesn't
		/// trigger the bonus again (two Rockstars each add 2 to the first gain, not to each other's).</summary>
		public const GameTag BONUS_ARMOR_GAINING = (GameTag)990107;

		/// <summary>A sideboard card's entity set aside at game start (E.T.C.'s band): the dbf id of the card whose sideboard it
		/// is (Actions/Sideboards.cs); 0 once picked.</summary>
		public const GameTag SIDEBOARD_OF = (GameTag)990181;

		/// <summary>Fizzle's Snapshot: the dbf ids of the snapshotted cards (990110..990119).</summary>
		public const GameTag SNAPSHOT_CARD_1 = (GameTag)990110;
		public const int SNAPSHOT_CARDS = 10;

		/// <summary>Client card tag (unnamed in the client enum): a Libram (the 12 "Libram of ..." cards carry it).</summary>
		public const GameTag LIBRAM = (GameTag)1546;

		public static readonly GameTag[] DeckGatedFamilies = { (GameTag)3623, (GameTag)4538, (GameTag)3692 };

		/// <summary>Client card tags of a Paladin Aura (TITANS onward: "Lasts @ turns"): PALADIN_AURA 3429, OBJECTIVE 2311; its
		/// duration is QUEST_PROGRESS_TOTAL (535). Live (TIME_700, live-20261003-dk-classxp): played into the Secret zone, each
		/// end of its owner's turn QUEST_PROGRESS +1 in a TRIGGER block (TriggerKeyword OBJECTIVE), to the graveyard at the total.</summary>
		public const GameTag PALADIN_AURA = (GameTag)3429, OBJECTIVE = (GameTag)2311;

		/// <summary>HsServer (Festival corrections 2026-10-07): an Objective's length in its owner's turns, kept on the server when
		/// the opponent mustn't see it (Rhythm and Roots' ETC_387t: the client reads its 2 or 4 from HIDDEN_CHOICE, which only the
		/// owner gets; QUEST_PROGRESS_TOTAL would show the opponent which option was picked).</summary>
		public const GameTag HIDDEN_OBJECTIVE_TURNS = (GameTag)990121;

		/// <summary>Client card tag (unnamed in the client enum, not read by the client) on the "(Secretly)" choices: Rhythm and
		/// Roots, Fatespinner. The server keeps the chosen option from the opponent (ClientStream: no SubOption for them).</summary>
		public const GameTag SECRET_CHOICE = (GameTag)818;

		/// <summary>HsServer: on a weapon replaced by a newly equipped one (Hero.AddWeapon), the new weapon's entity id. Advanced
		/// rulebook § Playing a weapon: "Finally your old weapon is destroyed" — Instrument Smasher reads who replaced it.</summary>
		public const GameTag REPLACED_BY_WEAPON = (GameTag)990120;

		/// <summary>HsServer: on a hero, the part of its latest damage that reached its Health (Armor took the rest; Character.
		/// TakeDamage). Read by its damage triggers at the event: Arcanite Ripper improves only when the Health changed (wiki
		/// patch 27.6.0: "Fixed a bug where Arcanite Ripper would upgrade if the attacker took damage equal to the Armor they
		/// had").</summary>
		public const GameTag HERO_HEALTH_LOST_LAST_DAMAGE = (GameTag)990122;

		/// <summary>Client card tag: the card is Temporary (3630; the Gifts reference it). Live (live-20261003-dk-classxp, a
		/// Temporary JAIL_433): the card in hand carries TEMPORARY 1 and VALEERASHADOW 1 (its shadowy outline) under the
		/// invisible enchantment GBL_999e; when it is played the enchantment leaves and both go back to 0.</summary>
		public const GameTag TEMPORARY = (GameTag)3630;
		/// <summary>Client tag: turns a card has spent in hand (+1 when its owner's turn ends; 1 when it arrives).</summary>
		public const GameTag NUM_TURNS_IN_HAND = (GameTag)273;

		/// <summary>Client card tag: Outcast (1333); TAG_CARD_ALTERNATE_COST (2837: 0 Mana, 1 Health, 2 Armor, 3 Corpses...).</summary>
		public const GameTag OUTCAST = (GameTag)1333, CARD_ALTERNATE_COST = (GameTag)2837;

		/// <summary>Client tag: the lock icon (Mug's Magic, JAIL_800hp1: live TriggerKeyword=LOCK_VISUAL).</summary>
		public const GameTag LOCK_VISUAL = (GameTag)4414;

		/// <summary>Controller: only the left- and right-most cards in hand can be played (Prosecutor Mel'tranix).</summary>
		public const GameTag ONLY_OUTER_CARDS_PLAYABLE = (GameTag)990025;

		/// <summary>Client card tags: "Infuse (N Demons / Beasts / Totems)" counts only that minion type (unnamed in the enum;
		/// each is set on exactly those cards in the client DB).</summary>
		public const GameTag INFUSE_DEMONS = (GameTag)2432, INFUSE_BEASTS = (GameTag)2448, INFUSE_TOTEMS = (GameTag)2449;

		// ---------------------------------------------------------------- TITANS (set 1858): 990200-990249 (Actions/Titans.cs)

		/// <summary>Titan: the entity ids of its three ability entities (SETASIDE, CREATOR = PARENT_CARD = the Titan), in
		/// ability order (Generic.TitanAbility). Never copied to another entity: each Titan owns its own.</summary>
		public const GameTag TITAN_ABILITY_ENTITY_1 = (GameTag)990200, TITAN_ABILITY_ENTITY_2 = (GameTag)990201,
			TITAN_ABILITY_ENTITY_3 = (GameTag)990202;
		/// <summary>Titan: the ability it used last (1..3) and that ability's target (entity id, 0 for none) — what its
		/// "After this uses an ability" effect reads (Power.TitanAbilityTask).</summary>
		public const GameTag TITAN_LAST_ABILITY = (GameTag)990203, TITAN_LAST_ABILITY_TARGET = (GameTag)990204;
		/// <summary>Player: Plagues their effects shuffled into the enemy deck this game (Chained Guardian).</summary>
		public const GameTag PLAGUES_SHUFFLED_INTO_ENEMY_DECK = (GameTag)990205;
		/// <summary>Player (the one Plagues are shuffled into): Plagues they draw are unending — a new copy is shuffled back
		/// after each one (Helya; one per Helya).</summary>
		public const GameTag PLAGUES_UNENDING = (GameTag)990206;
		/// <summary>Player: the game turn they last drew a spell (Draw.cs); Card: the game turn it was the first spell its
		/// owner drew that turn (Jotun, the Eternal: "the first spell you draw each turn").</summary>
		public const GameTag SPELL_DRAWN_TURN = (GameTag)990207, FIRST_SPELL_DRAWN_ON_TURN = (GameTag)990208;
		/// <summary>Player (a controller aura on both players, Razorscale): the cost floors in force — how many, and their
		/// sum (Generic.CostFloor: cards can't cost less than the floor; applied after every other cost change).</summary>
		public const GameTag COST_FLOOR_SOURCES = (GameTag)990209, COST_FLOOR_SUM = (GameTag)990210;
		/// <summary>Player: the starting hand (dbf ids, 990211..990220) and its size, recorded when the mulligan ends
		/// (Generic.StartingHand: Starlight Whelp).</summary>
		public const GameTag STARTING_HAND_1 = (GameTag)990211, STARTING_HAND_COUNT = (GameTag)990221;
		public const int STARTING_HAND_SLOTS = 10;
		/// <summary>Minion (an enchantment's effect, Always a Bigger Jormungar's TTN_079e): excess damage its attacks deal to
		/// a minion hits the enemy hero (Actions/Attack.cs; once however many).</summary>
		public const GameTag EXCESS_ATTACK_DAMAGE_TO_HERO = (GameTag)990222;
		/// <summary>Player: their next Choose One card or Hero Power has both effects combined (Disciple of Eonar), as many
		/// times as given; spent by the next one (Playable.ActivateTask; Controller.ChooseBoth).</summary>
		public const GameTag NEXT_CHOOSE_ONE_BOTH = (GameTag)990223;
		/// <summary>Player: "the next N minions you play" still waiting (Hodir, Father of Giants).</summary>
		public const GameTag NEXT_MINIONS_PLAYED_LEFT = (GameTag)990224;
		/// <summary>Player: the excess Health their effects Overhealed this game (Character.Overheal; Grace of the Highfather:
		/// Generic.OverhealedDuring).</summary>
		public const GameTag OVERHEALED_AMOUNT = (GameTag)990225;
		/// <summary>Player: the game turn their hero last lost Armor (absorbed damage or paid it: Generic.PayArmor) and how much
		/// it lost that turn ("If your Armor changed this turn": Stoneskin Armorer).</summary>
		public const GameTag ARMOR_LOST_TURN = (GameTag)990226, ARMOR_LOST_THIS_TURN = (GameTag)990227;
		/// <summary>Card: the entity id of the minion that took it into its owner's hand (Kologarn: "Move any in your hand to
		/// your opponent's"). Kept by every move of the same entity (Generic.ResetForBackwardMove).</summary>
		public const GameTag STOLEN_BY = (GameTag)990228;
		/// <summary>Card: the game turn it was marked "if you play it this turn" (NEW_1308e; Generic.MarkPlayThisTurn).</summary>
		public const GameTag PLAY_THIS_TURN_MARK = (GameTag)990229;
		/// <summary>Card in hand: it can't be played until the end of this game turn (Generic.LockInHand: Frozen Over's "Your
		/// opponent can not play them next turn").</summary>
		public const GameTag LOCKED_UNTIL_TURN = (GameTag)990230;
		/// <summary>Card in hand: the dbf id of the enchantment showing its lock (Frozen Solid), removed with the lock.</summary>
		public const GameTag LOCK_SHOWN_ENCHANTMENT = (GameTag)990233;
		/// <summary>Minion: the entity id of the permanent it opened (Sargeras' Twisting Nether).</summary>
		public const GameTag OPENED_PERMANENT = (GameTag)990231;
		/// <summary>A Twisting Nether portal: how many times the Demons it summons are empowered (Sargeras' Legion Invasion!: the
		/// portal of the Sargeras that used it — wiki Sargeras: "the Sargeras will only affect the one that it summoned").</summary>
		public const GameTag PORTAL_DEMONS_EMPOWERED = (GameTag)990232;
		/// <summary>Character: Immune for the Combat Sequence it is attacking in — it had "Immune while attacking" when the
		/// Sequence started (its own IMMUNE_WHILE_ATTACKING, or a hero's weapon's: Spark of Ra-den, Swift Slash). Set and cleared
		/// by Generic.AttackBlock; Character.TakeDamage prevents the damage.</summary>
		public const GameTag IMMUNE_THIS_ATTACK = (GameTag)990234;
		/// <summary>Card: the game turn it last shifted on in hand (Disguised K'thir: "Each turn ... transform" — once a turn, whether
		/// its own trigger or its Disguise Shift enchantment's fires).</summary>
		public const GameTag SHIFTED_ON_TURN = (GameTag)990235;
		/// <summary>Player: the healing effect a "your next healing effect deals damage instead" rule was spent on (Shadowtouched
		/// Kvaldir, Ruby Sanctum): with NEXT_HEAL_SOURCE (its source), the move and the task it ran in (Game.MoveNumber /
		/// TasksThisMove: "the same effect"). Character.TakeHeal.</summary>
		public const GameTag NEXT_HEAL_EFFECT_MOVE = (GameTag)990236, NEXT_HEAL_EFFECT_TASK = (GameTag)990237;
		/// <summary>Minion: the player (PlayerId) whose Controller.SummonedThisGame counted it when it was summoned (Astral
		/// Automaton's "each other … you've summoned": it is one of them only when it was counted for its controller).</summary>
		public const GameTag SUMMON_COUNTED_FOR = (GameTag)990238;
		// (990239-990249: free for the TITANS card writers; add them here)

		// ---------------------------------------------------------------- WILD_WEST (Showdown in the Badlands, set 1892): 990250-990299
		// (Actions/WildWest.cs)
		/// <summary>Player: 1 = their deck started with no duplicates, -1 = it had some; 0 = never recorded (Generic.RecordStartingDecks,
		/// Generic.DeckStartedWithNoDuplicates: "If your deck started with no duplicates").</summary>
		public const GameTag DECK_STARTED_NO_DUPLICATES = (GameTag)990250;
		/// <summary>Player: the turns in a row before this one in which they played an Elemental (rolled at their turn start:
		/// Generic.RollElementalStreak; Generic.ElementalStreak adds this turn).</summary>
		public const GameTag ELEMENTAL_STREAK_PRIOR = (GameTag)990251;
		/// <summary>Card: its own Overload is skipped on this play ("Battlecry: … don't Overload", Minecart Cruiser; Generic.OverloadBlock
		/// still sends the empty effect-index-1 POWER block, live).</summary>
		public const GameTag SKIP_OWN_OVERLOAD = (GameTag)990252;
		/// <summary>Card: the game turn on which it casts twice if played from hand (Stargazing; Generic.MarkCastsTwice / CastsTwiceNow).</summary>
		public const GameTag CASTS_TWICE_TURN = (GameTag)990253;
		/// <summary>Card: the dbf id of its "if you play it this turn" marker enchantment when it isn't NEW_1308e (Stargazing's WW_425e;
		/// Generic.MarkPlayThisTurn's overload).</summary>
		public const GameTag PLAY_THIS_TURN_MARKER = (GameTag)990254;
		/// <summary>Minion: the entity id of the weapon it took (Shadestone Skulker; Generic.BorrowWeapon / ReturnBorrowedWeapon).</summary>
		public const GameTag BORROWED_WEAPON = (GameTag)990255;
		// (990256-990279: free for the WILD_WEST card writers; add them here)
		/// <summary>Player (controller aura, ADD): "Your &lt;school&gt; spells have Lifesteal" — this + the client SPELL_SCHOOL (1..15:
		/// 990281-990295; Generic.SpellSchoolLifestealTag, Spell.HasLifeSteal). Pop'gar the Putrid: Fel = 7.</summary>
		public const GameTag LIFESTEAL_SPELL_SCHOOL_BASE = (GameTag)990280;
		// (990296-990299: free for the WILD_WEST card writers)

		// ---------------------------------------------------------------- WHIZBANG (990300-990349)
		/// <summary>Player: the runes of its starting deck (bit 1 Blood, 2 Frost, 4 Unholy; Generic.RecordStartingDecks) + 8 = recorded.</summary>
		public const GameTag DECK_STARTED_RUNES = (GameTag)990300;
		/// <summary>Minion: Pro Gamer's pick (1 Rock, 2 Paper, 3 Scissors).</summary>
		public const GameTag RPS_PICK = (GameTag)990301;
		/// <summary>Minion: Dr. Stitchensew's stitched minions (dbf ids, the next one first).</summary>
		public const GameTag STITCH_1 = (GameTag)990302, STITCH_2 = (GameTag)990303, STITCH_3 = (GameTag)990304;
		/// <summary>Player aura: Owlonius's "Your spells get double bonus from Spell Damage" (each one doubles again).</summary>
		public const GameTag SPELL_DAMAGE_DOUBLERS = (GameTag)990305;
		/// <summary>Spell: the order it triggered as a Secret this game (Product 9).</summary>
		public const GameTag SECRET_TRIGGERED_ORDER = (GameTag)990306;
		/// <summary>Player: "your next card this turn costs N less" with N kept here (Sparkling Phial, Part Scrapper), and its turn.</summary>
		public const GameTag NEXT_CARD_LESS = (GameTag)990307, NEXT_CARD_LESS_TURN = (GameTag)990308;
		/// <summary>Player: Timewinder Zarimi's extra turn used ("once per game").</summary>
		public const GameTag ZARIMI_USED = (GameTag)990309;
		/// <summary>Minion: a copy Shoplifter Goldbeard made (its entity id: that one doesn't copy it again).</summary>
		public const GameTag GOLDBEARD_COPY = (GameTag)990310;
		/// <summary>Card: Repackaged Box's cards (dbf ids, 14 slots from REPACKAGED_BASE).</summary>
		public const GameTag REPACKAGED_BASE = (GameTag)990311; // ..990324
		/// <summary>Player: the card whose Battlecry Shudderblock empowers (while it resolves: no damage to the enemy hero).</summary>
		public const GameTag SHUDDERBLOCK_CARD = (GameTag)990325;
		/// <summary>Entity: the turn its "(once per turn)" trigger last ran.</summary>
		public const GameTag ONCE_PER_TURN = (GameTag)990326;
		// (990327-990349: free for the WHIZBANG card writers)

		// ---------------------------------------------------------------- WONDERS (Caverns of Time, set 1898): 990350-990399
		// (Actions/Wonders.cs)
		/// <summary>Player: the game turn on which their next Choose One CARD has both effects combined (Cenarion Hold: "Your
		/// next Choose One card this turn" — cards only, wiki 28.6.0 "could be used up by a Choose One Hero Power" was a bug;
		/// this turn only). Controller.ChooseBothCards; spent in Playable.ActivateTask.</summary>
		public const GameTag CHOOSE_ONE_CARDS_BOTH_TURN = (GameTag)990350;
		/// <summary>Secret: the entity id of the effect (its creator) whose enchantment destroys it at the start of its owner's
		/// next turn (Disco at the End of Time; Generic.PutRandomSecrets / Generic.DestroyMarkedSecrets).</summary>
		public const GameTag DESTROY_SECRET_WITH = (GameTag)990351;
		/// <summary>Player: the game turn they can't be Overloaded (Pebbly Page; Generic.AddOverload gives 0).</summary>
		public const GameTag CANT_BE_OVERLOADED_TURN = (GameTag)990352;
		/// <summary>Player (a controller aura, ADD): "Your spells have Lifesteal" (Hallazeal the Ascended; a one-turn one: Omega
		/// Mind's Mind Meld). Spell.HasLifeSteal.</summary>
		public const GameTag SPELLS_HAVE_LIFESTEAL = (GameTag)990353;
		/// <summary>Player: their Hero Power's weapon equip was replaced this use by the equipped weapon's "Your Hero Power gives
		/// this … instead" (Poisoned Blade: Power.HeroPowerBuffInstead; WeaponTask). The weapon's Inspire trigger spends it.</summary>
		public const GameTag HERO_POWER_EQUIP_REPLACED = (GameTag)990354;
		/// <summary>Character: base Attack / Health it gained permanently without an enchantment (C'Thun's ritual "wherever it is",
		/// Eyestalk of C'Thun's gains): Silence and backward moves keep them (wiki C'Thun: "they do not disappear even if C'Thun
		/// is silenced, returned to the hand"). Generic.GainPermanentStats.</summary>
		public const GameTag PERMANENT_ATK_GAIN = (GameTag)990355, PERMANENT_HEALTH_GAIN = (GameTag)990356;
		/// <summary>C'Thun card: it already carries its owner's ritual totals (Generic.CthunRitual / Generic.SyncCthun: a C'Thun
		/// that never followed them gets the whole totals at its first ritual or when it enters a hand or play).</summary>
		public const GameTag CTHUN_FOLLOWING = (GameTag)990357;
		/// <summary>Player: the entity id of the last C'Thun ritual display copy (live: the next copy's COPIED_FROM_ENTITY_ID when
		/// the player has no C'Thun card).</summary>
		public const GameTag CTHUN_DISPLAY_COPY = (GameTag)990358;
		// (990359-990399: free for the WONDERS card writers; add them here)

		// ---------------------------------------------------------------- ISLAND_VACATION (Perils in Paradise, set 1905): 990400-990449
		// (Actions/Paradise.cs)
		/// <summary>Minion: the entity id of the Nerubian Egg its Battlecry summoned (Terrible Chef: "Deathrattle: Destroy it").</summary>
		public const GameTag LINKED_EGG = (GameTag)990400;
		/// <summary>Minion: the entity id of the friendly minion its Battlecry destroyed (Carnivorous Cubicle: "Summon a copy of it at
		/// the end of your turns").</summary>
		public const GameTag DIGESTED_ENTITY = (GameTag)990401;
		/// <summary>Minion: the turn and count of its activations this turn (Bayfin Bodybuilder: capped at 15 — wiki patch 30.0.3).</summary>
		public const GameTag ACTIVATIONS_TURN = (GameTag)990402, ACTIVATIONS_THIS_TURN = (GameTag)990403;
		/// <summary>Spell: the entity ids packed into it (Carry-On Suitcase; CARDTEXT_ENTITY_0 / _1 for the client's text).</summary>
		public const GameTag PACKED_1 = (GameTag)990404, PACKED_2 = (GameTag)990405;
		/// <summary>Scenic Vista (Mistah Vistah's objective): its owner's PlayHistory length when it began ("every spell you
		/// played while this was in play").</summary>
		public const GameTag PLAY_HISTORY_MARK = (GameTag)990406;
		/// <summary>Hand card: a Fortune (Narain Soothfancy): it mirrors the top card of its owner's deck (Generic.UpdateFortunes).</summary>
		public const GameTag FORTUNE = (GameTag)990407;
		/// <summary>Sanc'Azel: the minion and its location (each one's entity id of the other; Generic.TurnIntoLocation).</summary>
		public const GameTag LINKED_FORM = (GameTag)990408;
		/// <summary>Player (controller aura, ADD): "Your spells that cost (2) or less cast twice" (Sunsapper Lynessa; the cost is
		/// Generic.CheapSpellsMaxCost).</summary>
		public const GameTag CHEAP_SPELLS_CAST_TWICE = (GameTag)990409;
		/// <summary>Player: the damage their hero took this turn (both players' reset at every turn change; live client tag 4453
		/// counts the times the same way; "If your hero took damage this turn").</summary>
		public const GameTag HERO_DAMAGE_THIS_TURN = (GameTag)990410;
		/// <summary>Location: the card its last use drew (Knickknack Shack: "If you play it this turn, reopen this").</summary>
		public const GameTag DREW_CARD = (GameTag)990411;
		/// <summary>Location: the turn of <see cref="DREW_CARD"/>.</summary>
		public const GameTag DREW_ON_TURN = (GameTag)990412;
		/// <summary>Carress, Cabaret Star in hand: the spell schools played while holding it (bit = 1 &lt;&lt; school).</summary>
		public const GameTag SCHOOLS_SEEN = (GameTag)990413;
		/// <summary>Minion Sandwich (The Ryecleaver): the entity ids of the minions stuffed in it (990420 + i, up to 10).</summary>
		public const GameTag STUFFED_BASE = (GameTag)990420;
		// (990414-990419, 990430-990449: free for the ISLAND_VACATION card writers)

		// ---------------------------------------------------------------- SPACE (The Great Dark Beyond, set 1935): 990450-990549
		// (Actions/Starship.cs)
		/// <summary>Starship: the set-aside copies of its pieces (990450 + i, up to 30), in the order they were added.</summary>
		public const GameTag STARSHIP_PIECE_BASE = (GameTag)990450;
		/// <summary>Starship: its launch ability entity (GDB_905, PARENT_CARD = the Starship).</summary>
		public const GameTag STARSHIP_LAUNCH_ENTITY = (GameTag)990480;
		/// <summary>Player: Starships built / launched this game.</summary>
		public const GameTag STARSHIPS_BUILT = (GameTag)990481, STARSHIPS_LAUNCHED = (GameTag)990482;
		/// <summary>Player: the Starships launched this game (990483 + i, up to 10; Jim Raynor).</summary>
		public const GameTag LAUNCHED_STARSHIP_BASE = (GameTag)990483;
		/// <summary>Hand card: the turn and the times a card next to it in hand was played (Orbital Moon "If you played an adjacent
		/// card this turn", Red Giant "for each adjacent card played while in hand"). PlayCard.</summary>
		public const GameTag ADJACENT_PLAYED_TURN = (GameTag)990493, ADJACENT_PLAYED_COUNT = (GameTag)990494;
		/// <summary>Minion: summoned by its own Reborn (Auchenai Death-Speaker: "After another friendly minion is Reborn").</summary>
		public const GameTag REBORN_SUMMON = (GameTag)990495;
		/// <summary>Player: cards drawn / destroyed (in hand or deck) this game (The Ceaseless Expanse).</summary>
		public const GameTag CARDS_DRAWN_THIS_GAME = (GameTag)990496, CARDS_DESTROYED_THIS_GAME = (GameTag)990497;
		/// <summary>Player: "Your Asteroids deal N more damage" (Bolide Behemoth).</summary>
		public const GameTag ASTEROID_BONUS = (GameTag)990498;
		/// <summary>Player: cards Discovered this game / the turn of the last and the count that turn (Alien Encounters, Parallax
		/// Cannon; Generic.NoteDiscovered).</summary>
		public const GameTag DISCOVERED_THIS_GAME = (GameTag)990499, DISCOVERED_TURN = (GameTag)990500, DISCOVERED_THIS_TURN = (GameTag)990501;
		/// <summary>Pocket Dimension: the spells its choices offered (990502 + i, up to 30: dbf ids).</summary>
		public const GameTag POCKET_SEEN_BASE = (GameTag)990502;
		/// <summary>Card: the cost it was played for + 1 (0 = not played; PlayCard.PayPhase). "If this costs (0)" (Librams of
		/// Clarity/Divinity/Faith, Void Ray) and every "the cost it was played for" check (PlayedCost): a card discounted to 0 has no
		/// TAG_LAST_KNOWN_COST_IN_HAND to tell 0 from "unknown".</summary>
		public const GameTag PLAYED_COST_PLUS1 = (GameTag)990532;
		/// <summary>Player: "The next Combo minion you play triggers its Combo twice" charges (Lucky Comet; PlayCard).</summary>
		public const GameTag NEXT_COMBO_TWICE = (GameTag)990533;
		/// <summary>Source of a pick chain (Generic.PickTargetsInOrder): its own pointer, kept in the game the pick resolves in
		/// (Grunty: the next Murloc to shoot). Never captured in the card's code: a pick may resolve on an AI's copy.</summary>
		public const GameTag PICK_POINTER = (GameTag)990534;
		/// <summary>Enchantment: a stat set that can end (Generic.GiveStatSet): its target's own Attack / Health before it, and a
		/// marker that they are recorded.</summary>
		public const GameTag STAT_SET_BASE_ATK = (GameTag)990535, STAT_SET_BASE_HEALTH = (GameTag)990536, STAT_SET_RECORDED = (GameTag)990537;
		/// <summary>Minion: extra attacks this turn (the turn they were given in, and how many): "can attack again" (Exarch Akama;
		/// Generic.GrantExtraAttack, Generic.AttacksPerTurn).</summary>
		public const GameTag EXTRA_ATTACKS_TURN = (GameTag)990538, EXTRA_ATTACKS = (GameTag)990539;
		/// <summary>Player: the minions Replicating Spores summon (Spore Empress Moldara): how many, then their dbf ids from
		/// 990541 (up to 9).</summary>
		public const GameTag SPORE_COUNT = (GameTag)990540, SPORE_CARD_BASE = (GameTag)990541;
		// ---------------------------------------------------------------- EMERALD_DREAM (Into the Emerald Dream, set 1946): 990550-990599
		/// <summary>Player: "Your Leeches steal N more Health" (Hideous Husk's aura).</summary>
		public const GameTag LEECH_BONUS = (GameTag)990550;
		/// <summary>Minion: the one of Bloodthistle Illusionist's pair that dies when it takes damage (secret: rule state only).</summary>
		public const GameTag SECRET_ILLUSION = (GameTag)990551;
		/// <summary>Minion (Ursoc): the minions its attacks killed — how many, then their entity ids from 990553 (up to 14).</summary>
		public const GameTag KILLS_COUNT = (GameTag)990552, KILLS_BASE = (GameTag)990553;
		/// <summary>Player: "Your Divine Shields take N hits to break" (Toreth's aura); minion: the hits its shield took so far.</summary>
		public const GameTag DIVINE_SHIELD_HITS = (GameTag)990567, SHIELD_HITS_TAKEN = (GameTag)990568;
		/// <summary>Card: entering the hand from the battlefield right now (Harbinger of the Blighted; Generic.ReturnToHandBlock).</summary>
		public const GameTag ENTERED_HAND_FROM_PLAY = (GameTag)990569;
		/// <summary>Player: "The next N spells you play cast twice" (Tyrande) — charges, spent by played spells only.</summary>
		public const GameTag PLAYED_SPELLS_CAST_TWICE = (GameTag)990570;
		/// <summary>Player: Aviana's lunar cycle — turns left before "your cards cost (1)".</summary>
		public const GameTag LUNAR_CYCLE = (GameTag)990571;
		/// <summary>Player: "The next card you play costs your OPPONENT'S Health instead of Mana (up to N)" (Agamaggan): N while it lasts.</summary>
		public const GameTag NEXT_CARD_COSTS_OPPONENT_HEALTH = (GameTag)990572;
		/// <summary>Minion (Hungering Ancient): the deck minions it ate — how many, then their entity ids from 990574 (up to 16).</summary>
		public const GameTag EATEN_COUNT = (GameTag)990573, EATEN_BASE = (GameTag)990574;
		// (990590-990599: free for the EMERALD_DREAM card writers)

		// ---------------------------------------------------------------- LOST_CITY (The Lost City of Un'Goro, set 1952): 990600-990649
		/// <summary>Player: "Your next Kindred triggers twice" (Primalfin Challenger) — charges; card: its Kindred runs again in block 1.</summary>
		public const GameTag KINDRED_TWICE = (GameTag)990600, KINDRED_AGAIN = (GameTag)990601;
		/// <summary>Player (the Maps): the Discovered card, the two others, the turn ("If you play it this turn, also pick one of the others").</summary>
		public const GameTag MAP_PICK = (GameTag)990602, MAP_OTHER_A = (GameTag)990603, MAP_OTHER_B = (GameTag)990604, MAP_TURN = (GameTag)990605;
		// (990606: Platysaur's card — LostCityCardsGenHs.cs)
		/// <summary>Player: the last Discover's two unpicked options (entity ids; The Origin Stone plays copies of them).</summary>
		public const GameTag LAST_DISCOVER_OTHER_A = (GameTag)990607, LAST_DISCOVER_OTHER_B = (GameTag)990608;
		/// <summary>Player (controller aura): "Whenever you cast a 1-Cost spell, cast it twice" (Niri of the Crater).</summary>
		public const GameTag EXACT_COST_SPELLS_CAST_TWICE = (GameTag)990609;
		/// <summary>Quest: the turn it last counted (Restore the Wild: once per turn); the values it counted (The Food Chain: a bitmask).</summary>
		public const GameTag QUEST_COUNTED_TURN = (GameTag)990610, QUEST_COUNTED_MASK = (GameTag)990611;
		/// <summary>Player: the shuffle instance last counted (Game.MoveNumber, TasksThisMove, sender) — Generic.ShuffleIntoDeck.</summary>
		public const GameTag SHUFFLE_INSTANCE_MOVE = (GameTag)990612, SHUFFLE_INSTANCE_TASK = (GameTag)990613, SHUFFLE_INSTANCE_SENDER = (GameTag)990614;
		/// <summary>Player: Dive the Golakka Depths' completions (Murlocs you summon gain +N/+N).</summary>
		public const GameTag GOLAKKA_COMPLETIONS = (GameTag)990618;
		/// <summary>Card: it was Temporary when its play began (its GBL_999e leaves at the play: Generic.IsTemporary).</summary>
		public const GameTag PLAYED_AS_TEMPORARY = (GameTag)990619;
		/// <summary>Player (controller aura): "Your Elementals deal N extra damage" (Bralma Searstone).</summary>
		public const GameTag ELEMENTAL_EXTRA_DAMAGE = (GameTag)990620;
		/// <summary>Player: the Hero Power a temporary chain swaps back to (dbf; Story of Sulfuras).</summary>
		public const GameTag RESTORE_HERO_POWER = (GameTag)990621;
		/// <summary>Player: the Adaptations picked this game — how many, then their dbf ids from 990623 (up to 20; Ashalon).</summary>
		public const GameTag ADAPT_PICKS = (GameTag)990622, ADAPT_PICK_BASE = (GameTag)990623;
		// (990643-990649: free for the LOST_CITY card writers)

		// ---------------------------------------------------------------- TIME_TRAVEL (Across the Timeways, set 1957): 990650-990699
		/// <summary>Player: the hero's "Deathrattle: Spend up to N Corpses to resurrect with that much Health." (Husk) — N while it lasts.</summary>
		public const GameTag HERO_RESURRECT_CORPSES = (GameTag)990650;
		/// <summary>Player: the Boons given to Bwonsamdi (bits: Power, Longevity, Speed).</summary>
		public const GameTag BWONSAMDI_BOONS = (GameTag)990651;
		/// <summary>Minion (Timeway Warden): the minion it imprisoned.</summary>
		public const GameTag IMPRISONED = (GameTag)990652;
		// (990653-990654: Morchie's flag, Futuristic Forefather's guess — TimeTravelCardsGenHs.cs)
		/// <summary>Spell: "Casts twice." (Eternalized: The Well of Eternity's empowered spells) — CastSpell repeats it.</summary>
		public const GameTag SPELL_CASTS_TWICE = (GameTag)990655;
		/// <summary>Player: the hand's size when the last card was played from it (Precise Shot: "EXACTLY in the center of your hand").</summary>
		public const GameTag PLAYED_HAND_COUNT = (GameTag)990656;
		/// <summary>Player (Timelooper Toki): marked spells played so far for its player enchantment.</summary>
		public const GameTag TOKI_PLAYED = (GameTag)990657;
		/// <summary>Minion: the weapon it holds, set aside (Muradin, High King: "Bring the High King's Hammer to ME!").</summary>
		public const GameTag HELD_WEAPON = (GameTag)990658;
		/// <summary>Minion: the card whose Cost it set to INFINITY (Acolyte of Infinity: "Deathrattle: Change it back").</summary>
		public const GameTag INFINITY_COST_CARD = (GameTag)990659;
		// (990660-990699: free for the TIME_TRAVEL card writers)

		// ---------------------------------------------------------------- CATACLYSM (990700-990749)
		/// <summary>Minion: the minion whose damage or destroy effect killed it (Faceless Replicator: "the minion that killed this").</summary>
		public const GameTag KILLED_BY = (GameTag)990700;
		/// <summary>Minion: the card it discarded / remembers (Gemstone Hoarder: "Get it back").</summary>
		public const GameTag REMEMBERED_CARD = (GameTag)990701;
		/// <summary>Minion: its Attack when it died (its enchantments go before its Deathrattle runs: Blackwing Experiment's spell).</summary>
		public const GameTag LAST_ATTACK_IN_PLAY = (GameTag)990702;
		/// <summary>Player: the three current Animal Companions (dbf ids; 0 = the default Huffer / Leokk / Misha) and their Cost.</summary>
		public const GameTag COMPANION_1 = (GameTag)990703, COMPANION_2 = (GameTag)990704, COMPANION_3 = (GameTag)990705;
		/// <summary>Player: "Your cards that summon Animal Companions summon N more this game" (Talya Earthstrider).</summary>
		public const GameTag COMPANION_EXTRA = (GameTag)990706;
		/// <summary>Minion: asleep (exhausted) through the game turn it names (Tranquil Clearing: "falls asleep until the end of your
		/// next turn").</summary>
		public const GameTag ASLEEP_UNTIL_TURN = (GameTag)990707;
		// (990708: was CARRIED_SPELL, Bashana's Treant; now the client's ADDITIONAL_PLAY_REQS_1 / TAG_SCRIPT_DATA_ENT_1: Generic.CarrySpell)
		/// <summary>Player (controller aura): "Your spells from other classes cast twice" (Sinestra).</summary>
		public const GameTag OTHER_CLASS_SPELLS_CAST_TWICE = (GameTag)990709;
		/// <summary>Player (controller aura): "Your minions' end of turn effects trigger twice" (Sandfury Aura; not with Drakkari's).</summary>
		public const GameTag MINION_EXTRA_END_TURN_EFFECT = (GameTag)990710;
		/// <summary>Minion: the cards it devoured (entity ids) — Iso'rath's "Deathrattle: Return them".</summary>
		public const GameTag DEVOURED_1 = (GameTag)990711, DEVOURED_2 = (GameTag)990712;
		/// <summary>Player: "When you reach full Health, deal 15 damage to your opponent" pending (Alexstrasza, Guardian of Life: one per
		/// Battlecry).</summary>
		public const GameTag FULL_HEALTH_DAMAGE_PENDING = (GameTag)990713;
		/// <summary>Player: "Instead of drawing each turn, Discover a card from your deck" (Commander Geddon) — the dbf id of the cost
		/// enchantment the pick gets (CATA_591e2).</summary>
		public const GameTag TURN_DRAW_DISCOVER = (GameTag)990714;
		/// <summary>Player: the attacks friendly characters made this game (Muradin's Last Stand).</summary>
		public const GameTag FRIENDLY_ATTACKS_THIS_GAME = (GameTag)990715;
		/// <summary>Player (controller aura): "Your Arms and Soldiers destroy minions in the enemy's deck instead" (Cho'gall, Mastermind).</summary>
		public const GameTag ARMS_DESTROY_ENEMY_DECK = (GameTag)990716;
		/// <summary>HsServer (events): "You can only play minions / spells for the rest of the turn" (Arrival of the Old Gods / Titans): the
		/// only card type its player may play (player, from a one-turn enchantment); a hand card it locks (Generic.SyncTurnPlayRestriction).</summary>
		public const GameTag ONLY_PLAY_CARDTYPE = (GameTag)990717, LOCKED_BY_TURN_RESTRICTION = (GameTag)990718;
		/// <summary>HsServer (events): an adjoining run is being cast (Dragon Soul's Essences: a chain-cast one starts no chain of its own —
		/// wiki 36.2.0 fix "Sinestra could cast an Essence twice"); "the next Zombeast you craft costs (N) less" (Storm the Gates: Choose.cs).</summary>
		public const GameTag CASTING_ADJOINING_RUN = (GameTag)990719, CRAFTED_ZOMBEAST_DISCOUNT = (GameTag)990720;
		// (990721-990749: free for the CATACLYSM card writers)

		// ---------------------------------------------------------------- VIOLET_HOLD (990750-990799)
		/// <summary>Player: the Mana the last Prepare spent (Jailbird: "reduce this card's Cost by the same amount").</summary>
		public const GameTag LAST_PREPARE_SPENT = (GameTag)990750;
		/// <summary>Minion: played on the opponent's side (Free placement: "Can be played on either side") — the play stays its player's.</summary>
		public const GameTag PLAYED_DISGUISED = (GameTag)990751;
		/// <summary>Card (set aside): in its owner's Void (Irida Sinseeker).</summary>
		public const GameTag IN_VOID = (GameTag)990752;
		/// <summary>Player: the investigated card (dbf id) and the turn its play pays off (Inspector Murloc Holmes).</summary>
		public const GameTag INVESTIGATED_CARD = (GameTag)990753, INVESTIGATE_TURN = (GameTag)990754;
		/// <summary>Enchantment (Chef Neth'rek's JAIL_860e): its owner's turns ended so far.</summary>
		public const GameTag CHEF_TURNS_ENDED = (GameTag)990755;
		/// <summary>Card: a copy of one of the opponent's cards (Theft Accusation, Azalina, Mind Sweeper; Generic.Copy marks it).</summary>
		public const GameTag COPIED_FROM_OPPONENT = (GameTag)990003;
		/// <summary>Character: it was Stealthed when its current attack began (SI:7 Slayer, Mathias Shaw, Tricks of the Trade).</summary>
		public const GameTag ATTACKED_STEALTHED = (GameTag)990756;
		/// <summary>Player: how many of its minions were Reborn this game, and their cards (dbf ids) in REBORN_THIS_GAME_BASE + i
		/// (Raith Van Geist; 20 kept).</summary>
		public const GameTag REBORN_THIS_GAME_COUNT = (GameTag)990757, REBORN_THIS_GAME_BASE = (GameTag)990760;
		/// <summary>Dead minion: its set-aside copy for a "Reborn with full Health and enchantments" (Sinful Steed, Persisting Horror).</summary>
		public const GameTag REBORN_SNAPSHOT = (GameTag)990758;
		/// <summary>Card: the Mana it was paid with + 1 (0 = paid otherwise: Health, Armor, Corpses) — "played for 2 Mana".</summary>
		public const GameTag PAID_MANA_PLUS1 = (GameTag)990759;
		/// <summary>Player: cards played for 2 Mana this game (Jade Guardians).</summary>
		public const GameTag PLAYED_FOR_2_MANA = (GameTag)990780;
		/// <summary>Player: the counterfeit Coin's dbf id that replaces its Coins (Aya, Lotus Kingpin).</summary>
		public const GameTag COUNTERFEIT_COIN = (GameTag)990781;
		/// <summary>Card: the "Follow the X" spells (dbf) whose effect it carries this turn (FOLLOW_TURN), up to two.</summary>
		public const GameTag FOLLOW_SPELL_A = (GameTag)990782, FOLLOW_SPELL_B = (GameTag)990783, FOLLOW_TURN = (GameTag)990784;
		/// <summary>Player: the turn its "Following the X" player enchantment was given.</summary>
		public const GameTag FOLLOW_PLAYER_TURN = (GameTag)990785;
		/// <summary>Card: it gained stats (Power.ExtraOnStatGain pending) / its own extra is being applied (no new gain counted).</summary>
		public const GameTag STATS_GAINED = (GameTag)990786, STAT_GAIN_GUARD = (GameTag)990787;
		/// <summary>Player: its slimed minions (Slime 'em!): count, then the cards (dbf) in SLIMED_BASE + i (7 kept).</summary>
		public const GameTag SLIMED_COUNT = (GameTag)990788, SLIMED_BASE = (GameTag)990790;
		/// <summary>HsServer: while a turn-time limit shows on a player's TIMEOUT (Generic.SyncTurnLimit), its TIMEOUT without
		/// the limit (raised at the end of its turns as usual) and 1 (the limit is shown).</summary>
		public const GameTag TURN_SECONDS_BASE = (GameTag)990797, TURN_LIMIT_SHOWN = (GameTag)990798;
		// (990799: free for the VIOLET_HOLD card writers)

		// ---------------------------------------------------------------- VIOLET_HOLD (part 2: 990800-990849)
		/// <summary>Player aura: extra shots of its Cannoneers (Captain Crowley's Bombardment).</summary>
		public const GameTag CANNONEER_EXTRA_SHOTS = (GameTag)990800;
		/// <summary>Player: Godfrey the Betrayer's rule (overdrawn cards come back); card: saved by it (set aside, waiting for room).</summary>
		public const GameTag GODFREY_RULE = (GameTag)990802, GODFREY_SAVED = (GameTag)990803;
		/// <summary>Player: Warptooth's count — the turn, how many friendly characters took damage that turn, their ids (8 slots).</summary>
		public const GameTag WARPTOOTH_TURN = (GameTag)990804, WARPTOOTH_COUNT = (GameTag)990805, WARPTOOTH_BASE = (GameTag)990806;
		/// <summary>Minion: the card Ancient Augur secretly chose (entity id).</summary>
		public const GameTag AUGUR_CHOICE = (GameTag)990815;
		/// <summary>Hero Power: its owner's turns started with it (Mug's Magic: unlocks on Turn 3).</summary>
		public const GameTag OWN_TURNS_STARTED = (GameTag)990816;
		// (990817-990849: free for the VIOLET_HOLD card writers)

		/// <summary>Game: the turn step that waits for a start-of-turn pick (its Step value; 0: none). Live resolves a pick a
		/// "start of your turn" effect offers before the turn's draw (Sphere of Sapience, live-20261002 session-20261002-204752:
		/// the choice in MAIN_START_TRIGGERS, NEXT_STEP MAIN_START and the draw after the pick). Game.MainStartTriggers / Process.</summary>
		public const GameTag STEP_AFTER_CHOICE = (GameTag)990900;
	}
}
