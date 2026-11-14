// HsServer addition (not upstream SabberStone): Showdown in the Badlands (WILD_WEST, client set 1892) and its Delve into
// Deepholm mini-set (DEEP_) — the set's shared rules: Quickdraw (starting hand, the Quickdraw-only target, the bonus that
// drops the target), "If your deck started with no duplicates", the Elemental streak, "If you're holding another N-Cost
// card", "When this is played, discarded, or destroyed", destroying the bottom of the deck, damage to minions in hand, a
// weapon that freezes, borrowing a weapon, sliding a card in hand, the Duel draw, random Bonus Effects, attacking all
// enemies, Spell Damage numbers, Bottles, splitting a card into halves, "casts twice if played this turn", state-check
// triggers (Howdyfin), Sigil tokens, "Your <school> spells have Lifesteal", "from another class", "(This stays in your
// hand.)", Coins. Each rule is data-driven (client tags / text markers), never a card id. Card scripts:
// CardSets/HsServer/WildWestCardsGenHs*.cs.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Auras;
using SabberStoneCore.Conditions;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		// =========================================================================================================== Quickdraw

		/// <summary>
		/// The starting hand never has Quickdraw (wiki Quickdraw, RESEARCH/sources/wiki/Quickdraw.txt: "Cards in the starting hand do
		/// not trigger Quickdraw effects, but cards mulliganed into by the player going first can trigger their Quickdraw effect if
		/// played on the first turn"). The opening draw happens at game turn 1 (Game.StartGame sets Turn = 1 first), so the opening
		/// cards (and the Coin) are marked turn 0 here (Game.BeginDraw); the mulligan's replacements keep turn 1 (AddHandPhase) —
		/// on for the first player's turn 1, off for the second player (their first turn is 2).
		/// </summary>
		internal static void StartingHandNotQuickdraw(Game g)
		{
			foreach (Controller c in new[] { g.Player1, g.Player2 })
				foreach (IPlayable p in c.HandZone)
					((Entity)p)._data[HsTag.ADDED_TO_HAND_TURN] = 0;
		}

		/// <summary>Client card tag 2433 (unnamed): on the cards whose bonus replaces their target ("Quickdraw: To all enemies
		/// instead" Heat Wave, "Finale: To all enemies" Hardcore Cultist, "If you've played another copy of this, hit all enemies
		/// instead" Sylvanas's Triumph — the only three in the data). Live (live-20261007-vfx-decks 1006-1035,
		/// live-20261006-secrets-generators/224302 12523-12793): while the bonus is on, the play option has no targets and the
		/// PLAY block has Target=0.</summary>
		public const GameTag BONUS_DROPS_TARGET = (GameTag)2433;

		/// <summary>The card's own bonus would apply if it were played now: its Quickdraw (QUICKDRAW and it entered the hand this
		/// turn), its Finale (FINALE and its Cost equals the Mana left) or its script's own "If …" check (PoweredUp.Check, evaluated
		/// where it resolves) — the same conditions as its POWERED_UP glow.</summary>
		public static bool BonusReady(IPlayable p)
		{
			if (p == null || p.Zone?.Type != Zone.HAND) return false;
			if (p.Card[HsTag.QUICKDRAW] > 0) return IsQuickdrawNow(p);
			if (p.Card[FINALE] > 0) return FinaleReady(p);
			return PoweredUp.Check(p.Card) is { } check && PoweredUp.Holds(p.Game, p, check);
		}

		/// <summary>The play takes no target now (tag 2433 and the bonus is ready): options without targets, a target-less play
		/// is valid even when the card otherwise needs a target (Playable.IsValidPlayTarget, Controller options, PlayErrors).</summary>
		public static bool DropsTarget(IPlayable p) => p != null && p.Card[BONUS_DROPS_TARGET] > 0 && BonusReady(p);

		/// <summary>REQ_TARGET_IF_AVAILABLE_AND_DRAWN_THIS_TURN (client PlayReq 95): a copy of the card in hand has Quickdraw on
		/// (live-20261005 140614 81935-81940: Sunspot Dragon's option has targets only while Quickdraw is on). Same shape as
		/// OutcastMet (the availability check knows the card, not the entity).</summary>
		public static bool QuickdrawMet(Controller c, Card card) => c.HandZone.Any(p => p.Card == card && IsQuickdrawNow(p));

		/// <summary>REQ_TARGET_IF_AVAILABLE_AND_NOT_DRAWN_THIS_TURN (client PlayReq 96): a copy of the card in hand without Quickdraw.</summary>
		public static bool NotQuickdrawMet(Controller c, Card card) => c.HandZone.Any(p => p.Card == card && !IsQuickdrawNow(p));

		// =========================================================================================================== deck started with no duplicates

		/// <summary>
		/// Records whether each player's deck has no two cards with the same card id as the game begins — after the decks are
		/// built (FillDecks included) and before Start of Game effects and the mulligan (Game constructor, where
		/// FirstGeneratedEntityId is set). Wiki Highlander (https://hearthstone.wiki.gg/wiki/Highlander): "only check if the deck
		/// contained no duplicates at the start of the game. That is to say, during the deck-building phase"; "Any cards being
		/// shuffled into the deck due to other Start of Game effects don't mitigate their condition"; "The highlander effect
		/// checks for duplicate card IDs". Sideboard cards are not deck cards (engine-rules.md).
		/// </summary>
		internal static void RecordStartingDecks(Game g)
		{
			foreach (Controller c in new[] { g.Player1, g.Player2 })
				if (c != null)
				{
					c[HsTag.DECK_STARTED_NO_DUPLICATES] = c.DeckZone.GroupBy(p => p.Card.Id).All(x => x.Count() == 1) ? 1 : -1;
					c[HsTag.DECK_STARTED_RUNES] = 8 | RunesOf(c.DeckZone.GetAll().Select(p => p.Card)); // (WHIZBANG: Rainbow Seamstress)
				}
		}

		/// <summary>HsServer (2026-10-08): a Start of Game card still counts in its deck or its opening hand. Start_of_Game.txt: "Start of
		/// Game specifically triggers before the mulligan" — our GAME_START tasks resolve after the opening draw and the mulligan (as live:
		/// the keyworded blocks come at MAIN_READY), so the card may be in hand by then; live fires it from there (Mug'Zee in hand,
		/// live-20261007-titans-vfx-new-cards 174206 14109 / 34238). Not once played, discarded or set aside.</summary>
		public static bool StartOfGameHolds(IEntity self) =>
			self is IPlayable me && me.Zone?.Type is Zone.DECK or Zone.HAND;

		/// <summary>HsServer (WHIZBANG): the runes among cards: 1 Blood (COST_BLOOD 2196), 2 Frost (2197), 4 Unholy (2198).</summary>
		public static int RunesOf(IEnumerable<Card> cards) =>
			(cards.Any(x => x[(GameTag)2196] > 0) ? 1 : 0) | (cards.Any(x => x[(GameTag)2197] > 0) ? 2 : 0) | (cards.Any(x => x[(GameTag)2198] > 0) ? 4 : 0);

		/// <summary>"If your deck started with a Blood / Frost / Unholy card" (Rainbow Seamstress): the recorded runes, else the current deck.</summary>
		public static bool DeckStartedWithRune(Controller c, int rune)
		{
			int recorded = c[HsTag.DECK_STARTED_RUNES];
			return ((recorded != 0 ? recorded : RunesOf(c.DeckZone.GetAll().Select(p => p.Card))) & rune) != 0;
		}

		/// <summary>"If your deck started with no duplicates" (Reno, Lone Ranger; Theldurin; Rheastrasza; Deepminer Brann…; patch
		/// 29.2.2.198608 changed "has" to "started with"). Not the current deck: Generic.RecordStartingDecks. A game whose decks
		/// were never recorded (a test building its own decks) checks the current deck.</summary>
		public static bool DeckStartedWithNoDuplicates(Controller c)
		{
			if (c == null) return false;
			int recorded = c[HsTag.DECK_STARTED_NO_DUPLICATES];
			if (recorded != 0) return recorded > 0;
			return c.DeckZone.GroupBy(p => p.Card.Id).All(x => x.Count() == 1);
		}

		/// <summary>The shared condition for a script's "If your deck started with no duplicates, …" (ConditionTask(SOURCE, it) +
		/// FlagTask: PoweredUp.Check then lights the card in hand).</summary>
		public static readonly SelfCondition DeckStartedWithNoDuplicatesCondition =
			new SelfCondition(p => DeckStartedWithNoDuplicates(p.Controller));

		// =========================================================================================================== Elemental streak

		/// <summary>
		/// At the start of each of the player's turns, before last turn's Elemental count rolls over (Game.MainReady): the turns in
		/// a row before this one in which they played an Elemental. Blizzard forum
		/// (us.forums.blizzard.com/en/hearthstone/t/elemental-mage-elem-counter-reset/137728): "if you don't play an elemental for a
		/// turn the counter resets to 0"; forum "A Row of Elementals" (…/t/a-row-of-elementals/117508): a countered Elemental
		/// (Objection) wasn't played.
		/// </summary>
		internal static void RollElementalStreak(Controller c)
		{
			int prior = c[HsTag.ELEMENTAL_STREAK_PRIOR];
			int next = c.NumElementalsPlayedThisTurn > 0 ? prior + 1 : 0;
			if (next != prior) c[HsTag.ELEMENTAL_STREAK_PRIOR] = next;
		}

		/// <summary>"Each turn in a row you've played an Elemental": the turns in a row before this one, plus this turn once an
		/// Elemental was played in it. Live (practice-innkeeper-20260929 Power.log): Azerite Giant created on turn 5 before any
		/// Elemental that turn counted 4; Lamplighter drawn on turn 6 after an Elemental that turn showed 1 + 6; Lamplighter
		/// played first on turn 7 dealt 7 — the card's own play doesn't count (NumElementalsPlayedThisTurn goes up when its play
		/// finishes, PlayCard; user ruling 2026-10-05).</summary>
		public static int ElementalStreak(Controller c) =>
			c == null ? 0 : c[HsTag.ELEMENTAL_STREAK_PRIOR] + (c.NumElementalsPlayedThisTurn > 0 ? 1 : 0);

		/// <summary>Power.HandCounter for an "@ (improved by each turn in a row you've played an Elemental)" card: its card's own
		/// TAG_SCRIPT_DATA_NUM_1 plus the streak (live Lamplighter: NUM_1 1 + 6 = 7; Skarr's "Deal @ damage" the same). A card
		/// whose @ is the streak alone (Overflow Surger "(@)", card NUM_1 0) shows the streak.</summary>
		public static int ElementalStreakCounter(IPlayable p) => p.Card[GameTag.TAG_SCRIPT_DATA_NUM_1] + ElementalStreak(p.Controller);

		// =========================================================================================================== holding another N-Cost card

		/// <summary>"If you're holding another N-Cost card" (Greedy / Linedance / Rowdy Partner): a card in hand other than
		/// <paramref name="self"/> whose current Cost is <paramref name="cost"/>. In the play's effect the card has left the hand.</summary>
		public static bool HoldingOtherOfCost(Controller c, IEntity self, int cost) =>
			c != null && c.HandZone.Any(p => p.Id != self?.Id && p.Cost == cost);

		/// <summary>The internal PlayReq REQ_TARGET_IF_AVAILABLE_AND_HOLDING_COST (value N): another hand card of that Cost (the
		/// card itself is still in hand while its target is chosen: one copy of it at that Cost doesn't count).</summary>
		public static bool HoldingOtherOfCostMet(Controller c, Card card, int cost)
		{
			int n = c.HandZone.Count(p => p.Cost == cost);
			if (c.HandZone.Any(p => p.Card == card && p.Cost == cost)) n--;
			return n > 0;
		}

		// =========================================================================================================== played, discarded, or destroyed

		/// <summary>Client tag 335 (unnamed, IsPowerKeywordTag; Sabber "InvisibleDeathrattle"): live sends it as the TriggerKeyword
		/// of the card's own discard block (BT_300 ×4, CATA_499 ×3, KAR_205 ×2, WW_441 — live-20261005/power-decoded-140614
		/// 6141-6233). Trigger.Keyword.</summary>
		public const GameTag DISCARD_KEYWORD = (GameTag)335;

		/// <summary>
		/// "When this is played, discarded, or destroyed, …" (Barrel of Sludge, Furnace Fuel) — the destroyed part: a card with
		/// Power.DestroyedTask destroyed in its owner's deck by an effect (Generic.Destroy's DECK case: Chaos Creation, Waste
		/// Remover, T.N.T., Fracking) runs it in its own TRIGGER block (effect index 0, TriggerKeyword 335 like its discard —
		/// unrecorded for the deck, live-checks), right away: Blizzard forum
		/// (…/t/barrel-of-sludge-not-triggering-when-destroyed/127913) "Your deck is destroyed in order. You first destroyed a
		/// furnace fuel, which drew you 2 cards …, then you resumed destroying the remaining cards". In hand a destroy is a discard
		/// (rule book C3) — its DISCARD trigger runs it. Not on overdraw (rule book DC4: "it just goes to the Graveyard") nor
		/// "remove" (forum …/infinite-arcane-does-not-trigger-barrel-of-sludge/117962: "Destroy to trigger Barrels and Remove to not").
		/// </summary>
		internal static void RunDestroyedTask(IPlayable card)
		{
			ISimpleTask task = card?.Card.Power?.DestroyedTask;
			if (task == null || card.Controller == null) return;
			Game g = card.Game;
			int keyword = card.Card[DISCARD_KEYWORD] > 0 ? (int)DISCARD_KEYWORD : 0;
			g.TaskQueue.StartEvent();
			g.TaskQueue.Execute(task, card.Controller, card, null, 0, keyword, 0);
			g.ProcessTasks();
			g.TaskQueue.EndEvent();
		}

		/// <summary>
		/// "Destroy the bottom N cards of your deck" (Chaos Creation, Waste Remover): one at a time from the bottom (deck slot 0;
		/// Generic.PutOnBottom), each through Generic.Destroy so a destroyed Barrel / Furnace Fuel resolves before the next. Live
		/// (live-install-20261004 session-20261002-204752, Chaos Creation 130179-130230): every card shown into the graveyard with
		/// LAST_AFFECTED_BY = the spell and no SUPPRESS_MILL_ANIMATION. <paramref name="fx"/>: the card's own deck effect name
		/// part (Chaos Creation's "DeckAE") opened around the destroys. Returns how many were destroyed.
		/// </summary>
		public static int DestroyBottom(Controller c, IEntity source, int n, string fx = null)
		{
			int done = 0;
			Action destroy = () =>
			{
				for (int i = 0; i < n && c.DeckZone.Count > 0; i++)
				{
					Destroy(c.DeckZone[0], source, many: false);
					done++;
				}
			};
			if (fx != null && source is IPlayable p) NamedFx(p, fx, null, destroy);
			else destroy();
			return done;
		}

		// =========================================================================================================== damage in hand

		/// <summary>
		/// Damage to a minion in hand (Gunslinger Kurtrus: "fire six 2 damage shots at minions in the enemy's hand"). Wiki
		/// (Gunslinger Kurtrus notes, ClayByte on Reddit 2023-10-30): "Kurtrus actually decrements the minion's remaining health,
		/// and if it reaches 0, the minion is removed from the opponent's hand (not discarded or destroyed)"; Divine Shield,
		/// Poisonous and Enchanter don't apply; removed minions "aren't added to any resurrection pools" nor give Corpses. So:
		/// DAMAGE on the card (no damage event, no triggers), and at 0 Health it is set aside (Generic.RemoveFromHand). A damaged
		/// one played later enters damaged. Returns whether it was removed.
		/// </summary>
		public static bool DamageInHand(IPlayable card, int amount, IEntity source)
		{
			if (!(card is Minion m) || m.Zone?.Type != Zone.HAND || amount <= 0) return false;
			if (source != null) NoteAffected(m, source.Id);
			int lethal = m.Health;
			m.Damage = m.Damage + Math.Min(amount, Math.Max(0, lethal));
			if (m.Health > 0) return false;
			// it never died: the 0-Health flags the damage set don't apply to a card that leaves the hand
			m.ToBeDestroyed = false;
			m.MortallyWounded = false;
			RemoveFromHand(m.Controller, m);
			return true;
		}

		// =========================================================================================================== a weapon that freezes

		/// <summary>"Freeze any character damaged by your hero" (Quartzite Crusher): the equipped weapon card's own FREEZE (the
		/// loader drops reference tags: Ice Breaker's / Might of Menethil's FREEZE are mentions). Keywords.cs FREEZE: "own tag =
		/// freezes what it damages". Attack.cs (the hero's attacks) and Character.TakeDamage (other damage the hero deals).</summary>
		public static bool HeroFreezesWhatItDamages(IEntity source) =>
			source is Hero h && h.Weapon != null && h.Weapon.Card.Freeze;

		// =========================================================================================================== borrow a weapon

		/// <summary>
		/// "Take your weapon" (Shadestone Skulker): the hero's weapon leaves play WITHOUT being destroyed — wiki: "removes the
		/// player's weapon (doesn't destroy it, meaning weapon Deathrattles and any effects triggered by weapon destruction won't
		/// trigger)" — into its owner's set-aside zone with its damage and enchantments (KEEP_ENCHANTMENTS for the clear; their
		/// triggers off), its trigger and aura off. <paramref name="holder"/> keeps its id (HsTag.BORROWED_WEAPON). Returns it.
		/// </summary>
		public static Weapon BorrowWeapon(Controller c, IEntity holder)
		{
			Weapon w = c?.Hero?.Weapon;
			if (w == null) return null;
			// (KEEP_ENCHANTMENTS only for the clear: set quietly, not a client change)
			var data = ((Entity)w)._data;
			int keep = w[GameTag.KEEP_ENCHANTMENTS];
			if (keep != 1) data[GameTag.KEEP_ENCHANTMENTS] = 1;
			c.Hero.ClearWeapon();
			if (keep != 1) data[GameTag.KEEP_ENCHANTMENTS] = keep;
			w.AppliedEnchantments?.ForEach(e => e.ActivatedTrigger?.Remove());
			c.SetasideZone.Add(w);
			if (holder != null) holder[HsTag.BORROWED_WEAPON] = w.Id;
			return w;
		}

		/// <summary>
		/// "Give it back": the same weapon entity is equipped again "exactly how it was when this was played (same attack, health,
		/// damage, and enchantments)" (wiki Shadestone Skulker) — its trigger, aura and enchantment triggers back on, then
		/// Hero.AddWeapon (a weapon equipped meanwhile is replaced the normal way: destroyed). Nothing when it is gone (not in
		/// set-aside any more). Returns whether it was equipped.
		/// </summary>
		public static bool ReturnBorrowedWeapon(Controller c, IEntity holder)
		{
			if (c == null || holder == null) return false;
			int id = holder[HsTag.BORROWED_WEAPON];
			if (id == 0 || !c.Game.IdEntityDic.TryGetValue(id, out IPlayable p) || !(p is Weapon w) || w.Zone?.Type != Zone.SETASIDE) return false;
			holder[HsTag.BORROWED_WEAPON] = 0;
			w.Zone.Remove(w);
			if (w.Controller != c) GiveTo(w, c);
			w.Card.Power?.Aura?.Activate(w);
			w.Card.Power?.Trigger?.Activate(w);
			if (w.AppliedEnchantments != null)
				foreach (Enchantment e in w.AppliedEnchantments)
					if (e.ActivatedTrigger == null) e.Power?.Trigger?.Activate(e);
			c.Hero.AddWeapon(w);
			return true;
		}

		// =========================================================================================================== slide in hand

		/// <summary>"Slide it to the left side of your hand" (Bartend-O-Bot): the card moves to hand slot <paramref name="position"/>
		/// (0: left-most) inside the hand — not a leave / enter (its triggers and enchantments stay; the client gets the new
		/// ZONE_POSITIONs from Game.PublishDerived). Unrecorded wire.</summary>
		public static void MoveInHand(IPlayable card, int position)
		{
			if (card?.Zone?.Type != Zone.HAND) return;
			var hand = card.Controller.HandZone;
			position = Math.Max(0, Math.Min(position, hand.Count - 1));
			while (card.ZonePosition > position) hand.Swap(card, hand[card.ZonePosition - 1]);
			while (card.ZonePosition < position) hand.Swap(card, hand[card.ZonePosition + 1]);
		}

		// =========================================================================================================== Duel

		/// <summary>
		/// "Both players DRAW! Destroy the card that costs less." (High Noon Duelist). Wiki note: "first a Joust is initiated, then
		/// depending on which player's card costs more, that card is drawn, and the other player's card is removed from their deck";
		/// patch 28.4.0.192412: "will no longer destroy any cards if both players draw cards of the same cost" — a tie: both draw.
		/// A random card of any type from each deck, revealed like a Joust (Generic.JoustReveal); the higher Cost is drawn by its
		/// owner, the other destroyed in its deck (Generic.Destroy: a Barrel there resolves). An empty deck has no card: the other
		/// player draws theirs (DOUBT, unsourced).
		/// </summary>
		public static void DuelDraw(Controller c, IEntity source)
		{
			Game g = c.Game;
			IPlayable mine = c.DeckZone.Count > 0 ? c.DeckZone[g.Random.Next(c.DeckZone.Count)] : null;
			IPlayable theirs = c.Opponent.DeckZone.Count > 0 ? c.Opponent.DeckZone[g.Random.Next(c.Opponent.DeckZone.Count)] : null;
			if (mine == null && theirs == null) return;
			JoustReveal(g, mine, theirs);
			int a = mine?.Cost ?? -1, b = theirs?.Cost ?? -1;
			if (a == b)
			{
				Draw(c, mine);
				Draw(c.Opponent, theirs);
			}
			else if (a > b)
			{
				Draw(c, mine);
				if (theirs?.Zone?.Type == Zone.DECK) Destroy(theirs, source);
			}
			else
			{
				Draw(c.Opponent, theirs);
				if (mine?.Zone?.Type == Zone.DECK) Destroy(mine, source);
			}
		}

		// =========================================================================================================== Bonus Effects

		/// <summary>The random Bonus Effects (wiki Bonus Effect, https://hearthstone.wiki.gg/wiki/Bonus_Effect: "Taunt, Windfury,
		/// Divine Shield, Poisonous, Elusive, Rush, Lifesteal, or Reborn"), in that order (Camouflage Mount's WW_810t1..t8 number
		/// them by its own order — map by keyword, not index).</summary>
		public static readonly GameTag[] BonusEffectKeywords =
		{
			GameTag.TAUNT, GameTag.WINDFURY, GameTag.DIVINE_SHIELD, GameTag.POISONOUS, HsTag.ELUSIVE, GameTag.RUSH,
			GameTag.LIFESTEAL, GameTag.REBORN,
		};

		/// <summary>Has <paramref name="m"/> this Bonus Effect keyword now (Elusive: both "can't be targeted" tags).</summary>
		public static bool HasBonusEffect(Minion m, GameTag keyword) => keyword == HsTag.ELUSIVE
			? m[GameTag.CANT_BE_TARGETED_BY_SPELLS] > 0 && m[GameTag.CANT_BE_TARGETED_BY_HERO_POWERS] > 0 || m.Card[HsTag.ELUSIVE] > 0
			: m[keyword] > 0;

		/// <summary>Can <paramref name="keyword"/> be <paramref name="m"/>'s random Bonus Effect now? Wiki Bonus Effect (patch
		/// 30.2.0): "Rush cannot be given to a minion as a random bonus effect if it can attack normally that turn, already did
		/// attack that turn, or if it's the opponent's turn" — Rush only for a minion of the player whose turn it is that is still
		/// asleep (exhausted) and hasn't attacked.</summary>
		public static bool CanGetBonusEffect(Minion m, GameTag keyword)
		{
			if (keyword != GameTag.RUSH) return true;
			return m.Controller == m.Game.CurrentPlayer && m.IsExhausted && m.NumAttacksThisTurn == 0 && !m.HasCharge;
		}

		/// <summary>A random Bonus Effect keyword for <paramref name="m"/> (one it doesn't have when <paramref name="notOwned"/>),
		/// under the Rush rule; null when none can be given.</summary>
		public static GameTag? RandomBonusEffect(Game g, Minion m, bool notOwned = true)
		{
			if (m == null) return null;
			var pool = BonusEffectKeywords.Where(k => (!notOwned || !HasBonusEffect(m, k)) && CanGetBonusEffect(m, k)).ToList();
			return pool.Count == 0 ? (GameTag?)null : pool[g.Random.Next(pool.Count)];
		}

		/// <summary>Give <paramref name="m"/> a Bonus Effect keyword permanently (a tag, like the Bonus Effect cards' own; Elusive =
		/// CANT_BE_TARGETED_BY_SPELLS + HERO_POWERS, what the rules read and Game.PublishDerived shows as ELUSIVE).</summary>
		public static void GiveBonusEffect(Minion m, GameTag keyword)
		{
			if (m == null) return;
			if (keyword == HsTag.ELUSIVE)
			{
				m[GameTag.CANT_BE_TARGETED_BY_SPELLS] = 1;
				m[GameTag.CANT_BE_TARGETED_BY_HERO_POWERS] = 1;
				return;
			}
			if (keyword == GameTag.RUSH)
			{
				m.IsRush = true;
				if (m.IsExhausted && m.NumAttacksThisTurn == 0) m.AttackableByRush = true;
				return;
			}
			m[keyword] = 1;
		}

		/// <summary><paramref name="n"/> random Bonus Effects the minion doesn't have (Iridescent Gyreworm: 1, Sparkbot-style "random
		/// Bonus Effects": n), each under the Rush rule.</summary>
		public static void GainBonusEffects(Game g, Minion m, int n)
		{
			for (int i = 0; i < n && m != null; i++)
				if (RandomBonusEffect(g, m) is GameTag k) GiveBonusEffect(m, k);
				else break;
		}

		// =========================================================================================================== attack all enemies

		/// <summary>
		/// "Attack all enemies" (Theldurin the Lost, Decimator Olgra): <paramref name="attacker"/> attacks each enemy minion left to
		/// right, then the enemy hero, each a full attack (Generic.AttackBlock, no exhaustion, the attack not used up) with deaths
		/// between; it stops when the attacker dies or leaves play. Order unsourced (wiki Olgra / Theldurin have no notes; DOUBT):
		/// Olgra's existing order kept.
		/// </summary>
		public static void AttackAllEnemies(Controller c, Minion attacker)
		{
			if (attacker == null) return;
			Game g = c.Game;
			var enemies = c.Opponent.BoardZone.GetAll().Cast<ICharacter>().Concat(new[] { c.Opponent.Hero }).ToList();
			EventMetaData temp = g.CurrentEventData;
			foreach (ICharacter enemy in enemies)
			{
				if (attacker.IsDead || attacker.ToBeDestroyed || attacker.Zone?.Type != Zone.PLAY) break;
				if (enemy.IsDead || enemy.ToBeDestroyed || enemy is Minion em && (em.Zone?.Type != Zone.PLAY || em.Untouchable)) continue;
				AttackBlock.Invoke(c, attacker, enemy, true, true);
				g.DeathProcessingAndAuraUpdate();
			}
			g.CurrentEventData = temp;
		}

		// =========================================================================================================== Spell Damage numbers

		/// <summary>The Spell Damage a spell's "$N" gets (Spell_Damage.txt: the player's Spell Damage, the spell school's, the
		/// spell's own SPELLPOWER; doubled for a "receives double Spell Damage" card) — Generic.DamageCharFunc's own step.</summary>
		public static int SpellDamageBonus(Spell spell)
		{
			Controller c = spell?.Controller;
			if (c == null) return 0;
			int spellPower = c.CurrentSpellPower + c.SchoolSpellPower(spell[(GameTag)1635]) + spell[GameTag.SPELLPOWER];
			return spell.ReceveivesDoubleSpellDamage ? spellPower * 2 : spellPower;
		}

		/// <summary>The number a spell's "$N" shows and deals before the target's own modifiers: N + Spell Damage, then Prophet
		/// Velen-style doubling (rule book DH2a). For a non-spell source: N. "Summon a random minion of that Cost" (Shimmer Shot)
		/// uses this.</summary>
		public static int SpellDamageAmount(IPlayable source, int amount)
		{
			if (!(source is Spell spell) || spell.Controller == null) return amount;
			amount += SpellDamageBonus(spell);
			if (spell.Controller.ControllerAuraEffects[GameTag.SPELLPOWER_DOUBLE] > 0)
				amount *= (int)Math.Pow(2, spell.Controller.ControllerAuraEffects[GameTag.SPELLPOWER_DOUBLE]);
			return amount;
		}

		// =========================================================================================================== Bottles

		/// <summary>
		/// "Save any excess in a 1-Cost Bottle" (Swarm of Lightbugs, Invasive Shadeleaf, Holy Springwater): a new
		/// <paramref name="bottle"/> card (the source's related card with text builder MULTIPLE_ALT_TEXT_SCRIPT_DATA_NUMS) with
		/// TAG_SCRIPT_DATA_NUM_1 = the excess and USE_ALTERNATE_CARD_TEXT = 1 (the client shows the "@" half with {0}) enters the
		/// hand (CREATOR = the source: its recorded SpawnToHand effect). Live (live-install-20261004 session-20261003-185040
		/// 97697-97760, Holy Springwater bottling 5): FULL_ENTITY with both tags, SUB_SPELL ReuseFX_Water_SpawnToHand_Splash_Super
		/// from the hero. Nothing for no excess or a full hand. A bottle's own number: <see cref="BottledAmount"/>; a bottle never
		/// bottles again (its text has no "save" clause; live Bottled Springwater healing 5 into a 2-damage minion made none).
		/// </summary>
		public static IPlayable Bottle(Controller c, IEntity source, Card bottle, int excess)
		{
			if (excess <= 0 || bottle == null || c.HandZone.IsFull) return null;
			IPlayable p = Entity.FromCard(c, bottle, new EntityData
			{
				[GameTag.CREATOR] = source?.Id ?? 0,
				[GameTag.TAG_SCRIPT_DATA_NUM_1] = excess,
				[GameTag.USE_ALTERNATE_CARD_TEXT] = 1,
			});
			return AddHandPhase.Invoke(c, p) ? p : null;
		}

		/// <summary>The amount a Bottle holds (its own TAG_SCRIPT_DATA_NUM_1; a bottle made without one: 0).</summary>
		public static int BottledAmount(IEntity bottle) => bottle?[GameTag.TAG_SCRIPT_DATA_NUM_1] ?? 0;

		// =========================================================================================================== split into halves

		/// <summary>A Cost-setting enchantment whose value is its TAG_SCRIPT_DATA_NUM_1 (Mes'Adune's WW_422e "Cost broken in half",
		/// Divergence's TIME_030e1).</summary>
		public static readonly Enchant SetCostScriptTag = new Enchant(Effects.SetCost(0)) { UseScriptTag = true };

		/// <summary>
		/// Draw a card matching <paramref name="which"/> and split it (Mes'Adune the Fractured: "Draw an Elemental. Split it into two
		/// halves."). Live (live-install-20261004 session-20260930-175606 3748-3805): the source's DrawAndHold SUB_SPELL (Source 0)
		/// around the draw, META HOLD_DRAWN_CARD + TARGET = the drawn card; then <see cref="SplitIntoHalves"/>. Returns the halves.
		/// </summary>
		public static List<IPlayable> DrawAndSplit(Controller c, IPlayable source, Func<IPlayable, bool> which, Card costEnchantment, Card statsEnchantment)
		{
			Game g = c.Game;
			var pool = c.DeckZone.Where(which).ToList();
			if (pool.Count == 0) return new List<IPlayable>();
			IPlayable pick = pool[g.Random.Next(pool.Count)];
			string hold = g.History ? Loader.SubSpells.FindNamed(source.Card.Id, "DrawAndHold") : null;
			bool open = hold != null && g.OpenSubSpell(source, hold, null, shownSource: Game.NoSubSpellSource);
			IPlayable drawn = Draw(c, pick);
			if (g.History && drawn?.Zone?.Type == Zone.HAND)
			{
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.HOLD_DRAWN_CARD, Data = 0, Info = new List<int> { drawn.Id } });
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.TARGET, Data = 0, Info = new List<int> { drawn.Id } });
			}
			if (open) g.CloseSubSpells(source.Id);
			return drawn?.Zone?.Type == Zone.HAND ? SplitIntoHalves(c, source, drawn, costEnchantment, statsEnchantment) : new List<IPlayable>();
		}

		/// <summary>
		/// "Split it into two halves" (Mes'Adune, Divergence): the card in hand is set aside and two NEW cards of it (premium kept —
		/// wiki patch 28.0.0.189384) take its slot, each with <paramref name="costEnchantment"/> (SetCostScriptTag, NUM_1 = its
		/// half) and, for a minion, <paramref name="statsEnchantment"/> (SetAttackHealthScriptTag, NUM_1 / NUM_2). Wiki
		/// Mes'Adune notes: halves of the current Cost / Attack / Health; "If the cost, attack, or health of the Elemental is not
		/// even, it will get split into two different numbers"; "The splitting on odd numbers is random"; 1 → 0 and 1, but "health
		/// always splits into 1 and 1". Each half enters inside the source's CardInHand_OverrideSpawn_Left / _Right SUB_SPELL
		/// (Source = the source; live). A full hand keeps what fits. Returns the halves in hand.
		/// </summary>
		public static List<IPlayable> SplitIntoHalves(Controller c, IPlayable source, IPlayable card, Card costEnchantment, Card statsEnchantment)
		{
			var halves = new List<IPlayable>();
			if (card?.Zone?.Type != Zone.HAND) return halves;
			Game g = c.Game;
			int slot = card.ZonePosition;
			int cost = card.Cost;
			int atk = card is Character ch ? ch.AttackDamage : 0, health = card is Character ch2 ? ch2.Health : 0;
			(int, int) Split(int n, bool atLeast1)
			{
				int big = (n + 1) / 2, small = n / 2;
				if (atLeast1) { big = Math.Max(1, big); small = Math.Max(1, small); }
				return g.Random.Next(2) == 0 ? (big, small) : (small, big);
			}
			(int c1, int c2) = Split(cost, false);
			(int a1, int a2) = Split(atk, false);
			(int h1, int h2) = Split(health, true);
			c.SetasideZone.Add(c.HandZone.Remove(card));
			var parts = new[] { ("CardInHand_OverrideSpawn_Left", c1, a1, h1), ("CardInHand_OverrideSpawn_Right", c2, a2, h2) };
			for (int i = 0; i < parts.Length; i++)
			{
				if (c.HandZone.IsFull) break;
				(string fx, int hc, int ha, int hh) = parts[i];
				IPlayable half = Entity.FromCard(c, card.Card, new EntityData
				{
					[GameTag.CREATOR] = source.Id,
					[GameTag.DISPLAYED_CREATOR] = source.Id,
					[GameTag.PREMIUM] = card[GameTag.PREMIUM],
				});
				string prefab = g.History ? Loader.SubSpells.FindNamed(source.Card.Id, fx) : null;
				bool open = prefab != null && g.OpenSubSpell(source, prefab, null);
				if (costEnchantment != null) AddEnchantmentBlock(g, costEnchantment, source, half, hc, 0, 0);
				if (statsEnchantment != null && half is Character) AddEnchantmentBlock(g, statsEnchantment, source, half, ha, hh, 0);
				if (AddHandPhaseAt(c, half, Math.Min(slot + i, c.HandZone.Count))) halves.Add(half);
				if (open) g.CloseSubSpells(source.Id);
			}
			return halves;
		}

		// =========================================================================================================== casts twice if played this turn

		/// <summary>
		/// "If you play it this turn, it casts twice" (Stargazing): the card gets <paramref name="enchantment"/> (WW_425e) and the
		/// highlight tags live shows (LUNAHIGHLIGHTHINT 1054 and 1140 — Sinestra's "casts twice" highlight, wire-conventions.md) via
		/// Generic.MarkPlayThisTurn; played from hand this turn, the spell casts twice (Generic.CastSpell, like SPELLS_CAST_TWICE).
		/// Live (live-install-20261004 session-20261002-204752 65551-65690): at the end of the turn a TRIGGER block on the
		/// enchantment (effect index 1) takes it away and clears the tags (Generic.ClearPlayThisTurnMarks).
		/// </summary>
		public static void MarkCastsTwice(IPlayable card, IEntity source, string enchantment)
		{
			if (card?.Zone?.Type != Zone.HAND) return;
			MarkPlayThisTurn(card, source, enchantment, (GameTag)1054, (GameTag)1140);
			card[HsTag.CASTS_TWICE_TURN] = card.Game.Turn;
		}

		/// <summary>Does <paramref name="spell"/> cast twice now: marked "casts twice if played this turn" for this turn and being
		/// played from hand (its player's resolving play, HsTag.RESOLVING_PLAY — not a cast by another card's effect).</summary>
		public static bool CastsTwiceNow(Spell spell) =>
			spell.Game.Turn > 0 && spell[HsTag.CASTS_TWICE_TURN] == spell.Game.Turn && spell.Controller?[HsTag.RESOLVING_PLAY] == spell.Id;

		// =========================================================================================================== state checks

		/// <summary>Runs per state check at most this many times for one source (a hand refills to at most its limit).</summary>
		private const int MaxStateRuns = 10;

		/// <summary>
		/// "Whenever your hand has less than 3 cards in it, get a random Murloc." (Howdyfin) — a state, not an event: Power.StateCheck
		/// / Power.StateTask on a minion in play (not Silenced, not Dormant). After each sequence settles (Game.DeathProcessingAndAuraUpdate,
		/// deaths done) the engine runs the task (source: the minion, its own TRIGGER block, effect index Power.StateEffectIndex,
		/// TriggerKeyword TRIGGER_VISUAL when the card has it) for as long as its check holds — one card at a time. Wiki Howdyfin
		/// note (RidiculousHat, x.com/RidiculousHat/status/1715088296563347603): "Howdyfin keeps filling the player's hand until
		/// they have 3 cards every time the hand size gets lower than 3"; from 3 cards Dark Bargain (discard 2) ends with 3 Murlocs:
		/// the check comes after the play resolves. Waits while its player has a choice open. Either player's turn.
		/// </summary>
		internal static void RunStateChecks(Game g)
		{
			if (g.StateChecksRunning || g.State != State.RUNNING || g.CurrentPlayer == null) return;
			List<IPlayable> sources = null;
			foreach (Controller p in new[] { g.CurrentPlayer, g.CurrentOpponent })
			{
				foreach (Minion m in p.BoardZone)
					if (m.Card.Power?.StateCheck != null && m.Card.Power.StateTask != null)
						(sources ??= new List<IPlayable>()).Add(m);
				// HsServer (SPACE): a Paladin Aura in the Secret zone is in play too ("While you have exactly 1 minion …": Celestial Aura)
				foreach (Spell aura in p.SecretZone)
					if (aura.Card.Power?.StateCheck != null && aura.Card.Power.StateTask != null)
						(sources ??= new List<IPlayable>()).Add(aura);
			}
			if (sources == null) return;
			g.StateChecksRunning = true;
			try
			{
				bool again = true;
				for (int sweep = 0; again && sweep < 3; sweep++)
				{
					again = false;
					foreach (IPlayable m in sources.OrderBy(x => x.OrderOfPlay))
						for (int i = 0; i < MaxStateRuns; i++)
						{
							if (m.Zone?.Type != (m is Minion ? Zone.PLAY : Zone.SECRET)) break;
							if (m is Minion sm && (sm.IsDead || sm.ToBeDestroyed || sm.IsSilenced || sm.Untouchable)) break;
							if (m.Controller.Choice != null || g.State != State.RUNNING) break;
							if (m.Controller.Hero.ToBeDestroyed || m.Controller.Opponent.Hero.ToBeDestroyed) break;
							if (!m.Card.Power.StateCheck(m)) break;
							int keyword = m.Card[GameTag.TRIGGER_VISUAL] > 0 ? (int)GameTag.TRIGGER_VISUAL : 0;
							g.TaskQueue.StartEvent();
							g.TaskQueue.Execute(m.Card.Power.StateTask, m.Controller, m, null, 0, keyword, m.Card.Power.StateEffectIndex);
							g.ProcessTasks();
							g.TaskQueue.EndEvent();
							g.DeathProcessingAndAuraUpdate();
							again = true;
						}
				}
			}
			finally
			{
				g.StateChecksRunning = false;
			}
		}

		/// <summary>Power.StateCheck for "Whenever your hand has less than <paramref name="n"/> cards in it".</summary>
		public static Func<IPlayable, bool> HandBelow(int n) => p => p.Controller != null && p.Controller.HandZone.Count < n && !p.Controller.HandZone.IsFull;

		// =========================================================================================================== Sigil tokens

		/// <summary>
		/// A Sigil card put into play by an effect (Fel Fissure's DEEP_013t "At the start of your next turn, deal $2 more damage to
		/// all minions." — the only non-collectible SIGIL card): a NEW entity (CREATOR = <paramref name="source"/>) straight into
		/// its owner's Secret zone, visible, its Sigil trigger armed (Generic.SigilPower: it acts at its owner's next turn start,
		/// then goes to the graveyard). User ruling (TITANS): Sigils sit visibly in the Secret slots. A full hero zone: the new card
		/// goes to the graveyard (rule book: 5 hero-zone cards; CastSpell's rule). Unrecorded wire (no SHOW_BIG_CARD here). Returns it.
		/// </summary>
		public static Spell PutSigilIntoPlay(Controller c, IEntity source, Card sigil)
		{
			if (c == null || sigil == null) return null;
			if (!(Entity.FromCard(c, sigil, new EntityData { [GameTag.CREATOR] = source?.Id ?? 0 }) is Spell s)) return null;
			if (!CanHoldSecret(c, sigil))
			{
				c.GraveyardZone.Add(s);
				return s;
			}
			PutSecretIntoPlay(c, s);
			return s;
		}

		// =========================================================================================================== spell-school Lifesteal

		/// <summary>The controller aura tag for "Your &lt;school&gt; spells have Lifesteal" (Pop'gar the Putrid: Fel = client SPELL_SCHOOL 7):
		/// HsTag.LIFESTEAL_SPELL_SCHOOL_BASE + school (a count per school, so two Pop'gars don't mix bits). Spell.HasLifeSteal reads it
		/// wherever the spell is — wiki Pop'gar patch 28.0.0.189384: "Fixed a bug where discarded Barrel of Sludge would not have
		/// Lifesteal while Pop'gar the Putrid was in play".</summary>
		public static GameTag SpellSchoolLifestealTag(int school) => (GameTag)((int)HsTag.LIFESTEAL_SPELL_SCHOOL_BASE + Math.Max(0, Math.Min(15, school)));

		/// <summary>Do <paramref name="c"/>'s spells of <paramref name="school"/> have Lifesteal now?</summary>
		public static bool SchoolHasLifesteal(Controller c, int school) =>
			c != null && school > 0 && school <= 15 && c.ControllerAuraEffects[SpellSchoolLifestealTag(school)] > 0;

		// =========================================================================================================== another class

		/// <summary>"A card from another class": a class card that isn't the player's class — never Neutral (wiki Stick Up patch
		/// 34.2.0.231720: "Stick Up can no longer incorrectly discover neutral cards"); dual-class cards by Card.HasClass
		/// (engine-rules.md). The same test as the scripts' OtherClassCards pool.</summary>
		public static bool FromAnotherClass(Card card, Controller c) =>
			card != null && c != null && !card.HasClass(c.HeroClass) && !card.HasClass(CardClass.NEUTRAL) && !card.HasClass(CardClass.INVALID);

		// =========================================================================================================== "(This stays in your hand.)"

		private static readonly ConcurrentDictionary<string, bool> _staysInHand = new ConcurrentDictionary<string, bool>();

		/// <summary>The client text marker of a card that comes back to the hand slot it was played from (TIME_042t, RLK_816t3 —
		/// both also in the client's ReplacementsWhenPlayed table — and WW_337t Mirage, which isn't).</summary>
		public static bool StaysInHand(Card card) => card != null && _staysInHand.GetOrAdd(card.Id, _ =>
			(card.ClientText ?? card.Text ?? "").Contains("(This stays in your hand.)"));

		/// <summary>
		/// A card in hand shifted by a card that "stays in your hand" (Mirage: WW_337e, whose DEFINING_ENCHANTMENT 469 names WW_337t)
		/// leaves that card in the slot it is played from (Generic.OnPlayReplacement). Wiki Mirage: "This card effectively has
		/// Twinspell, and functions as if it had the keyword endlessly"; Blizzard forum t/127043: playing the minion it transformed
		/// into leaves a Mirage in hand. Null for any other card.
		/// </summary>
		public static Card StaysInHandVia(IPlayable played)
		{
			if (played?.AppliedEnchantments == null) return null;
			foreach (Enchantment e in played.AppliedEnchantments)
				if (e.Card[DEFINING_ENCHANTMENT] > 0 && Cards.FromAssetId(e.Card[DEFINING_ENCHANTMENT]) is Card defined && StaysInHand(defined))
					return defined;
			return null;
		}

		/// <summary>Client tag DEFINING_ENCHANTMENT (469): on an enchantment, the card it stands for (Mirage's WW_337e → WW_337t).</summary>
		public const GameTag DEFINING_ENCHANTMENT = (GameTag)469;

		// =========================================================================================================== Coins

		/// <summary>Client card tag COIN_CARD (2088): The Coin and its prints (63 cards: cosmetic coins, Jade / Grimy / Kabal Coins).
		/// "After you play a Coin" (Wishing Well) reads the played SPELL's tag (the GAME_005e enchantment carries it too).</summary>
		public const GameTag COIN_CARD = (GameTag)2088;

		/// <summary>Is <paramref name="p"/> a Coin (a spell with COIN_CARD)?</summary>
		public static bool IsCoin(IEntity p) => p is Spell && p.Card[COIN_CARD] > 0;

		/// <summary>HsServer (2026-10-08): a player's coin skin — a COIN_CARD spell with The Coin's text (live: the player's Darkmoon Coin
		/// DMF_COIN1 for the starting Coin and "get a Coin"); it plays as The Coin (its script when it has none). Null for anything else.</summary>
		public static Card CoinSkin(string cardId)
		{
			if (string.IsNullOrEmpty(cardId) || cardId == "GAME_005" || !(Cards.TryFromId(cardId) is Card skin) || skin[COIN_CARD] == 0 ||
			    skin.Type != CardType.SPELL || !(Cards.TryFromId("GAME_005") is Card coin))
				return null;
			if (skin.Power == null) skin.Power = coin.Power; // (same text: The Coin's script)
			return skin;
		}

		// =========================================================================================================== cost glows

		/// <summary>
		/// A card whose own cost change depends on a condition (AdaptiveCostEffect with a SelfCondition: Aftershocks "Costs (2) less
		/// if you cast a spell last turn", Dehydrate "Quickdraw: Costs (1)") glows in hand while it holds. Live
		/// (live-20261005/power-decoded-184804.txt 6858-6860): Aftershocks at its owner's turn start COST 2 + POWERED_UP 1; at
		/// the play COST 4, POWERED_UP 0. Game.PublishDerived.
		/// </summary>
		public static bool HasCostCondition(Card card) => MultiAura.CostEffect(card?.Power?.Aura)?.OwnerCondition != null;

		/// <summary>The cost condition of <see cref="HasCostCondition"/> holds for the card now.</summary>
		public static bool CostConditionMet(IPlayable p) =>
			MultiAura.CostEffect(p?.Card.Power?.Aura)?.OwnerCondition is SelfCondition condition && condition.Eval(p);
	}
}

namespace SabberStoneCore.Model
{
	public partial class Game
	{
		/// <summary>HsServer (WILD_WEST): Generic.RunStateChecks is running (its own runs don't start another).</summary>
		internal bool StateChecksRunning;
	}
}
