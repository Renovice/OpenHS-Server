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

using SabberStoneCore.Auras;
using SabberStoneCore.Enums;
using SabberStoneCore.Tasks;
using SabberStoneCore.Tasks.SimpleTasks;
using SabberStoneCore.Triggers;

namespace SabberStoneCore.Enchants
{
	public class Power
	{
		public string InfoCardId { get; set; } = null;

		public IAura Aura { get; set; }

		public Enchant Enchant { get; set; }

		public Trigger Trigger { get; set; }

		public ISimpleTask PowerTask { get; set; }

		public ISimpleTask DeathrattleTask { get; set; }

		public ISimpleTask ComboTask { get; set; }

		public ISimpleTask TopdeckTask { get; set; }

		/// <summary>HsServer: after a Casts When Drawn card's replacement draw (Draw.OnDrawn): Corrupted Blood "After you draw,
		/// shuffle two copies of this into your deck".</summary>
		public ISimpleTask AfterReplacementDrawTask { get; set; }

		/// <summary>HsServer: a Casts When Drawn card whose replacement draw another entity's trigger makes (live: The
		/// Darkness's Candle — "Darkness Candle Detect" LOOT_526et on the Darkness's owner draws it, after "Darkness
		/// Awaits" counts the Candle; live-20261004-night). The engine then draws none in the card's own block.</summary>
		public bool ReplacementDrawnElsewhere { get; set; }

		public ISimpleTask OverkillTask { get; set; }

		/// <summary>HsServer: Honorable Kill — runs when this (or the weapon, or a minion with this enchantment) deals exactly
		/// lethal damage on its owner's turn (Character.TakeDamage). Its target: the character it killed.</summary>
		public ISimpleTask HonorableKillTask { get; set; }

		/// <summary>HsServer: "When this awakens, ..." (a Dormant minion waking up; Stag Wildseed).</summary>
		public ISimpleTask AwakenTask { get; set; }

		/// <summary>HsServer: the card's version for a game board (Board.json id → card id; null = itself). Wiki Transfer
		/// Student: "At the start of game, Transfer Student's effect is automatically replaced by one of the following ones,
		/// depending on the board". The game creates the card as that version (Cards.ForBoard).</summary>
		public System.Func<int, string> BoardVersion { get; set; }

		/// <summary>HsServer: what the card does when it is Corrupted beyond its Corrupted version (Horrendous Growth: "Can be
		/// Corrupted endlessly" — +1/+1 each time it stays itself). Runs with the card as source and target.</summary>
		public ISimpleTask CorruptTask { get; set; }

		/// <summary>HsServer: the card's text is all engine rules (keywords and a Corrupt into its Corrupted version: Carousel
		/// Gryphon, Dancing Cobra...): no script, and it counts as scripted.</summary>
		public bool NoScript { get; set; }

		/// <summary>HsServer (livecheck 2026-10-10): "(Won't trigger Gallywix.)" (Gallywix's Coin GVG_028t): no Gallywix effect sees it
		/// played — Trade Prince Gallywix GVG_028 nor the Heist boss Trade Prince Gallywix's Greed is Good DALA_BOSS_57p (live
		/// session-20261005-140614 74794-75011: two Coins played, uncopied and uncounted; the third card is the "first card" bought).
		/// Read through Actions.Generic.TriggersGallywix.</summary>
		public bool WontTriggerGallywix { get; set; }

		/// <summary>HsServer (TITANS review 2026-10-07): a Titan ability whose POWER block live sends without ShowInHistory
		/// (live-20261007-vfx-decks: Vision of Heroes 43756 / 48179, Shargahn's Wrath 71778 / 304257, Roaring Oceans 305086; the
		/// others — Strike from History, Shape the Stars, Eonar's three, Lord of Skies — have it). Recorded per ability: no rule
		/// in the data says which.</summary>
		public bool NotInHistory { get; set; }

		/// <summary>
		/// HsServer (TITANS review 2026-10-07): a play requirement the card's own effect implies where no client PlayReq states it
		/// exactly — a "choose an enemy minion" picked while it resolves (Generic.PickTargets: Strike from History, Chaotic
		/// Consumption) needs one the player could pick (wiki Titan: "If a Titan's ability requires a target but there are no valid
		/// targets, it can't be used"). While <c>Check</c> fails the card (or Titan ability) can't be used and its option shows
		/// <c>Error</c> (a client PlayReq). Added to the card's play checks at load (CardContainer).
		/// </summary>
		public (PlayReq Error, System.Func<SabberStoneCore.Model.Entities.Controller, bool> Check)? PlayableIf { get; set; }

		/// <summary>HsServer (ISLAND_VACATION): a target requirement that reads the card itself (Undercooked Calamari: "an enemy minion
		/// with Attack less than or equal to this minion's"): Check(card, target); Error is the client's option error.</summary>
		public (PlayReq Error, System.Func<SabberStoneCore.Model.Entities.IPlayable, SabberStoneCore.Model.Entities.ICharacter, bool> Check)? TargetIf { get; set; }

		/// <summary>HsServer: runs inside the minion's own "goes Dormant" block (Actions/Dormant.cs): The Darkness attaches its
		/// "Darkness Candle Detect" to its owner there (live).</summary>
		public ISimpleTask DormantTask { get; set; }

		/// <summary>HsServer: runs after the card is Traded (Doggie Biscuit).</summary>
		public ISimpleTask TradeTask { get; set; }

		/// <summary>HsServer: the card a card in hand shows on hover (client DISPLAY_CARD_ON_MOUSEOVER = its dbf id): Brilliant
		/// Macaw's last Battlecry, Grey Sage Parrot's last big spell… Game keeps it current (Game.UpdateMouseOverCards).</summary>
		public System.Func<SabberStoneCore.Model.Entities.IPlayable, SabberStoneCore.Model.Card> MouseOverCard { get; set; }

		/// <summary>HsServer: the card's own counter for a "({0} left!) / (Ready!)" text (client ENTITY_TAG_THRESHOLD_TAG_ID = 2,
		/// TAG_SCRIPT_DATA_NUM_1, against ENTITY_TAG_THRESHOLD_VALUE): what it has counted so far (Kazakusan: other Dragons
		/// played). The game keeps it on the card in hand (Game.UpdateHandCounters); its effect checks Generic.ThresholdMet.</summary>
		public System.Func<SabberStoneCore.Model.Entities.IPlayable, int> ThresholdCount { get; set; }

		/// <summary>HsServer: the count a card's "If you've … while holding this" needs when its data names none ("If you've cast
		/// a spell while holding this": 1); the count itself is its TAG_SCRIPT_DATA_NUM_1, raised by its hand trigger.</summary>
		public int ThresholdValue { get; set; }

		/// <summary>HsServer (TITANS): a card's own number for its text (the client's SCRIPT_DATA_NUM_1 text builder: "@ (@)" —
		/// Mindbender's "(N)", Elemental Inspiration's spell schools; "Improved by …" — Sif; Chaotic Tendril's "@-Cost"): the
		/// game keeps TAG_SCRIPT_DATA_NUM_1 = this on the card in hand and on the minion in play (Game.UpdateMouseOverCards),
		/// with no glow (unlike <see cref="ThresholdCount"/>). Its effect may read the tag.</summary>
		public System.Func<SabberStoneCore.Model.Entities.IPlayable, int> HandCounter { get; set; }

		/// <summary>HsServer (TITANS): a Titan's "After this uses an ability, …" — runs after each ability (Generic.UseTitanAbility)
		/// inside the Titan's TRIGGER block whose effect index is the ability's (the client plays that ability's trigger effect
		/// and voice line). Source: the Titan; the ability used: its HsTag.TITAN_LAST_ABILITY (1..3) and
		/// HsTag.TITAN_LAST_ABILITY_TARGET.</summary>
		public ISimpleTask TitanAbilityTask { get; set; }

		/// <summary>HsServer (2026-10-08): the effect index of the Titan's first "after this uses an ability" TRIGGER block (ability slot N
		/// sends base + N): 0 for most; 1 when the card's index 0 is another trigger's (live Amitus: the damage cap at 0, the abilities at
		/// 1-3 — titans-vfx 190857).</summary>
		public int TitanAbilityEffectIndexBase { get; set; }

		/// <summary>HsServer (2026-11-19): each ability's "after this uses an ability" TRIGGER index when they don't follow base + N (an Old
		/// God: live C'Thun 3,2,1,0; N'Zoth 1,3,0,2 — in ability order, its RelatedCards order). Null: base + N.</summary>
		public int[] TitanAbilityEffectIndexes { get; set; }

		/// <summary>HsServer (2026-11-19): an Old God's TRIGGER index for the extra action its first ability of a turn gives it
		/// (Generic.FinishTitanAbility; live C'Thun 6, N'Zoth 5, Yogg-Saron 11, Y'Shaarj 11).</summary>
		public int OldGodExtraActionIndex { get; set; }

		/// <summary>HsServer (2026-11-19): the slot an Old God marks its extra action in, when not TAG_SCRIPT_DATA_NUM_2 (Generic.OldGodMark).</summary>
		public GameTag? OldGodMarkTag { get; set; }

		/// <summary>HsServer (2026-10-08): a deck card that makes its owner start the game as another class (Maestra of the Masquerade): the
		/// hero is built disguised (Game setup: Generic.DisguiseHero) and this enchantment card (on the player) reveals it.</summary>
		public string StartsDisguisedWith { get; set; }

		/// <summary>HsServer (2026-10-08): a hero card's step once its Hero Power is in place — rulebook :403 replaces the Hero Power after
		/// the Battlecry, so what the card does to its own new Hero Power runs here (Bru'kan: an Invocation from the moment he's played).
		/// Source: the hero; inside the Hero Power's POWER block (index 1).</summary>
		public ISimpleTask AfterHeroPowerTask { get; set; }

		/// <summary>HsServer: "Whenever this attacks, … attack instead" (Neptulon the Tidehunter's Hands): given the attacker and
		/// the defender after the attack's own triggers, the attack that replaces it (null: it attacks as normal). The
		/// replaced attack is used up without combat (Actions/Attack.cs).</summary>
		public System.Func<SabberStoneCore.Model.Game, SabberStoneCore.Model.Entities.ICharacter, SabberStoneCore.Model.Entities.ICharacter, System.Action> AttackInstead { get; set; }

		/// <summary>HsServer: "If this would transform into a minion, summon that minion instead" (Baroness Vashj): on the
		/// battlefield an effect's transform of it into a minion summons that minion instead, and it stays (Generic.ChangeEntityBlock;
		/// not in hand or deck — Blizzard forums: Lady Prestor doesn't trigger it; Silence removes it).</summary>
		public bool SummonInsteadOfTransform { get; set; }

		/// <summary>HsServer: "Deathrattles given to this minion trigger twice" (Scourge Troll): its enchantments' Deathrattles run
		/// twice (Enchantment.Remove's Deathrattle loop; not after a Silence).</summary>
		public bool GivenDeathrattlesTwice { get; set; }

		/// <summary>HsServer: "If this would transform into a minion, it transforms into one that costs (N) more" (Plucky Podling):
		/// on the battlefield an effect's transform of it into a minion becomes a random minion costing N more than that one
		/// (Generic.ChangeEntityBlock; live: Revolve's 1-Cost → a 3-Cost, Primordial Wave's 2-Cost → a 4-Cost).</summary>
		public int TransformCostsMore { get; set; }

		/// <summary>HsServer: "While this is in your hand or deck, it gains a copy of every Dark Gift given to your minions" (Wallow,
		/// the Wretched): the card scripts' Dark Gift giver copies each given gift onto it.</summary>
		public bool CopiesDarkGifts { get; set; }

		/// <summary>HsServer: "Whenever you heal an enemy, this attacks it." (Wilted Shadow): before the heal (Character.TakeHeal; wiki
		/// Wilted Shadow: it "attacks before the healing occurs", "checks for death immediately after attacking", a full-Health enemy
		/// isn't healed after).</summary>
		public bool AttacksHealedEnemy { get; set; }

		/// <summary>HsServer: an interactable object's use (client INTERACTABLE_OBJECT: the Underfel Rift — "Throw a card in to …"):
		/// target = the hand card thrown in (Generic.UseInteractable).</summary>
		public Tasks.ISimpleTask InteractTask { get; set; }

		/// <summary>HsServer: "Whenever you play a Temporary card …" — see Generic.IsTemporary.</summary>
		public int ElementalExtraDamage { get; set; }

		/// <summary>HsServer: in-place Infuse thresholds (2: "Infuse ({0}): ... Infuse ({1}): ...", Huntsman Altimor).</summary>
		public int InfuseStages { get; set; } = 1;

		/// <summary>HsServer: "Endlessly Infuse (N)" (Sire Denathrius): every N deaths add 1 to TAG_SCRIPT_DATA_NUM_2.</summary>
		public bool EndlessInfuse { get; set; }

		/// <summary>HsServer: the effect index of the card's Deathrattle TRIGGER block (see Trigger.EffectIndex): which of its
		/// client trigger effects plays (Gnomelia: 1, her area blast; user 2026-10-04: "a deathrattle AoE vfx").</summary>
		public int DeathrattleEffectIndex { get; set; }

		/// <summary>HsServer: the script places the card's buff / area effect itself (Generic.WholeFx) instead of around the
		/// whole action (TaskProcessor): Overseer Frigidara's blast only plays when it deals damage.</summary>
		public bool OwnFx { get; set; }

		/// <summary>HsServer: the card's second effect, in its own POWER block after its main one (effect index 1,
		/// ShowInHistory, a SLUSH_TIME 3000 between them; Generic.SecondPowerBlock). Live (live-20261007-vfx-decks): the Riffs'
		/// "Finale: Play your last Riff".</summary>
		public Tasks.ISimpleTask SecondPowerTask { get; set; }

		/// <summary>HsServer (CATACLYSM): "When summoned, ..." (the Colossal limbs and Herald Soldiers): run by SummonBlock as the minion
		/// lands, in its own TRIGGER block (effect index 1) nested in the summoning block (live vfx 260347, 277581; darkmoon 64945).</summary>
		public Tasks.ISimpleTask WhenSummonedTask { get; set; }

		/// <summary>HsServer (CATACLYSM): "Takes N extra damage from all sources" (Selfless Protector) — every damage it takes, while it
		/// isn't silenced (Character.TakeDamage, at Voidtouched Attendant's point).</summary>
		public int ExtraDamageTaken { get; set; }

		/// <summary>HsServer (CATACLYSM): "When your hero would lose Health on your turn, gain that much max Health instead" (Arisen
		/// Onyxia): the enchantment the hero gets with the amount (Character.TakeDamage).</summary>
		public string HealthLossToMaxHealth { get; set; }

		/// <summary>HsServer (CATACLYSM): "Cards adjacent to this in hand cost (1) more" (Sabotage!): the enchantment each neighbour in the
		/// hand carries, one per such card next to it (Generic.SyncAdjacentInHand).</summary>
		public string AdjacentInHand { get; set; }

		/// <summary>HsServer (CATACLYSM): "While holding this, if …, transform into …" (Genn, Cursed King): the condition and the card it
		/// becomes (Generic.HandTransforms, after each sequence; one-way).</summary>
		public (System.Func<Model.Entities.IPlayable, bool> When, string Into)? HandTransform { get; set; }

		/// <summary>HsServer (CATACLYSM): "If you spent N Mana while holding this. ({0} left!)" (Felwood Treant, Broodwatcher, Merithra): its
		/// TAG_SCRIPT_DATA_NUM_1 counts the Mana still to spend down in hand (Generic.ManaSpent); 0 = ready (USE_ALTERNATE_CARD_TEXT 1).</summary>
		public bool ManaSpentWhileHeld { get; set; }

		/// <summary>HsServer (CATACLYSM): "Whenever you spend your last Mana Crystal" (Crystalspine Cub), in play (Generic.ManaSpent).</summary>
		public ISimpleTask LastManaSpentTask { get; set; }

		/// <summary>HsServer (CATACLYSM): "After one of [its] Legs gains stats, this gains them too" (Wickerfang): the enchantment it gets
		/// with the gains of its own appendages (Generic.AddEnchantmentBlock).</summary>
		public string CopiesLimbGains { get; set; }

		/// <summary>HsServer (CATACLYSM): "Adjacent minions have +{0} Attack" with its own number (Charged Hand of Al'Akir, Soldier of
		/// Al'Akir: TAG_SCRIPT_DATA_NUM_1, Herald-upgraded): each neighbour carries this enchantment with that number (live vfx 73135:
		/// one CATA_153e each, its TAG_SCRIPT_DATA_NUM_1 = the source's) — Generic.SyncAdjacentOnBoard.</summary>
		public string AdjacentOnBoard { get; set; }
		/// <summary>HsServer (2026-11-16): a player enchantment that is a charge "your next spell of this school casts twice" (its
		/// TAG_SCRIPT_DATA_NUM_1 = the school; Therazane's BE_337e2) — Generic.SchoolSpellCastsTwice.</summary>
		public bool SchoolCastsTwice { get; set; }
		/// <summary>HsServer (2026-11-19): a player enchantment that is a charge "your next spell casts twice" (Brain of Yogg-Saron's
		/// BE_009t1e, "this turn": a one-turn enchantment) — spent by the next spell the player plays (Generic.NextSpellCastsTwice).</summary>
		public bool NextSpellCastsTwice { get; set; }
		/// <summary>HsServer (2026-11-20): a hero card whose player never draws at the start of their turn, silently (a boss without a deck:
		/// Commander Bolan — live session-20261007-113244, wiki "Deck: None"; no block, unlike Generic.CancelTurnDraw). Game.MainDraw.</summary>
		public bool SkipsTurnDraw { get; set; }
		/// <summary>HsServer (2026-11-16): Assemble's halves for a card that can't be split automatically (Generic.AssembleHalves): its
		/// first half (makes the Power running a task at its moment) and its second (the effect).</summary>
		public System.Func<SabberStoneCore.Tasks.ISimpleTask, Power> AssembleWhen { get; set; }
		public SabberStoneCore.Tasks.ISimpleTask AssembleWhat { get; set; }
		/// <summary>HsServer (2026-11-19): the first half is the one that aims ("Battlecry: Deal 6 damage to a minion. If it survives,&amp;"
		/// — Stone Trogg): an Assembled minion takes its target requirements from this card, not from the second half's.</summary>
		public bool AssembleWhenAims { get; set; }

		/// <summary>HsServer (CATACLYSM): "Whenever you Shatter a card" (Misplaced Pyromancer), in play — run after each split of its owner's
		/// Shatter card (Generic.ShatterHands).</summary>
		public ISimpleTask AfterShatterTask { get; set; }

		/// <summary>HsServer (VIOLET_HOLD): "When you Prepare while holding this" (Jailbird) — run in hand after each Prepare of another card
		/// (Generic.Prepare; the Mana spent: player HsTag.LAST_PREPARE_SPENT).</summary>
		public ISimpleTask AfterPrepareTask { get; set; }

		/// <summary>HsServer (VIOLET_HOLD): "Instead of damaging heroes, this shuffles that many X into their deck" (The Living Plague): the
		/// card shuffled per damage this minion would deal to a hero (Character.TakeDamage).</summary>
		public string HeroDamageShuffles { get; set; }

		/// <summary>HsServer (VIOLET_HOLD): "After this gains stats, gain an extra +1/+1 (wherever this is)" (Dalaran Champion): the enchantment
		/// it gets after each Attack / Health gain from an effect (Generic.AfterStatGains).</summary>
		public string ExtraOnStatGain { get; set; }

		/// <summary>HsServer (VIOLET_HOLD): "You always go second." (Aya, Lotus Kingpin) — in the deck at the start of the game.</summary>
		public bool AlwaysGoSecond { get; set; }

		/// <summary>HsServer (WHIZBANG): "(Gain @ Corpses to upgrade.)" — run for each of its owner's Corpse gains while in hand (stack.Number =
		/// how many; Generic.CorpsesGained).</summary>
		public ISimpleTask CorpsesGainedTask { get; set; }

		/// <summary>HsServer (WHIZBANG): "After this minion deals exact lethal damage on your turn" — Honorable Kill spelled out: its
		/// HonorableKillTask runs without the HONORABLE_KILL tag (Nostalgic Gnome), in a TRIGGER_VISUAL block.</summary>
		public bool ExactLethal { get; set; }

		/// <summary>HsServer (VIOLET_HOLD): a Cannoneer's shot — "your Cannoneers FIRE!" (Hand Cannon) runs it; Captain Crowley adds shots
		/// (HsTag.CANNONEER_EXTRA_SHOTS). Generic.CannoneerFire.</summary>
		public ISimpleTask CannoneerShot { get; set; }

		/// <summary>HsServer: the card costs Health instead of Mana while this holds (Death Metal Knight).</summary>
		public System.Func<Model.Entities.IPlayable, bool> CostsHealthWhile { get; set; }

		/// <summary>HsServer: the play targets a card in its owner's hand (client tag CAN_TARGET_CARDS_IN_HAND: "Choose a card
		/// in your hand", Bibliomite): which hand cards are valid. The client aims at the hand when the option lists hand
		/// cards (Entity.HasHandTarget). With no valid card the card plays without a target. Null: no hand target.</summary>
		public Conditions.SelfCondition HandTarget { get; set; }

		/// <summary>HsServer (events): in its owner's hand the card names the top card of their deck (Sharp-Eyed Lookout "(It's {5}!)": client
		/// text builder MULTIPLE_ALT_TEXT_SCRIPT_DATA_NUMS_REF_SDN6_CARD_DBID — TAG_SCRIPT_DATA_NUM_6 = the card's dbf id, USE_ALTERNATE_CARD_TEXT
		/// 1). Kept up to date by Generic.SyncTopOfDeckText; a hidden card's tag changes never reach the opponent (ClientStream).</summary>
		public bool ShowsTopOfDeck { get; set; }

		/// <summary>HsServer: a Drink spell (Perils in Paradise). The client data has no tag for it (its next drink is the
		/// on-play replacement); quests count "Drink spells" by this.</summary>
		public bool Drink { get; set; }

		/// <summary>HsServer: "Spend N Corpses to ..." — the Corpses the card spends (its card value, read once when the card
		/// is defined). The card glows in hand (POWERED_UP) while its owner has that many (live: Rite of Atrocity lit at 2,
		/// Grave Strength at 5+); Generic.SpendCorpses(c, source) spends it. 0: no fixed Corpse cost ("up to N" cards).</summary>
		public int CorpseCost { get; set; }

		/// <summary>HsServer: runs once per Corpse its owner spends, on each card of theirs with it in hand and in the deck
		/// (Generic.CorpsesSpent): Climactic Necrotic Explosion "(Randomly improved by Corpses you've spent)" — wiki: "any one
		/// of these numbers on the card can individually upgrade by 1". Source and target: the card.</summary>
		public ISimpleTask CorpseSpentTask { get; set; }

		/// <summary>HsServer: a sideboard card's deck-building effect ("While building your deck, pick a 2-Cost minion. Ten
		/// copies join your deck!"): runs once for the card in its owner's deck when the game starts, before the first
		/// player is chosen and the opening hands are drawn (Generic.SideboardEffects; live Commander Beatrix,
		/// live-20261003-dk-classxp). Source: the card.</summary>
		public ISimpleTask SideboardTask { get; set; }

		/// <summary>HsServer (WONDERS): "Whenever your C'Thun gains Attack or Health, this does too (wherever it is)" (Eyestalk of
		/// C'Thun). No client tag marks it (no trigger, no enchantment): the shared ritual (Generic.CthunRitual) gives every
		/// friendly card with it in the deck, hand or play (not silenced) the same permanent base gain. Only ritual gains count
		/// (Blizzard forum: a Stolen Goods buff to C'Thun in hand did not reach Eyestalk).</summary>
		public bool FollowsCthunGains { get; set; }

		/// <summary>HsServer (WONDERS): a weapon's "Your Hero Power gives this … instead of replacing it" (Poisoned Blade): the
		/// enchantment id its owner's weapon-equipping Hero Power gives it instead of equipping (WeaponTask skips the equip and
		/// marks the player, HsTag.HERO_POWER_EQUIP_REPLACED; the weapon's Inspire trigger gives it — Generic.HeroPowerBuffsWeapon).
		/// Wiki Poisoned Blade: "only Hero Powers which would equip a weapon will grant this weapon +1 Attack".</summary>
		public string HeroPowerBuffInstead { get; set; }

		/// <summary>HsServer (WILD_WEST): "When this is played, discarded, or destroyed, …" — the destroyed part: runs when an effect
		/// destroys the card in its owner's deck (Generic.Destroy → Generic.RunDestroyedTask; its own TRIGGER block, TriggerKeyword
		/// 335). Played = PowerTask, discarded (also a destroy in hand) = a DISCARD hand trigger. Source: the card (in the
		/// graveyard).</summary>
		public ISimpleTask DestroyedTask { get; set; }

		/// <summary>HsServer (SPACE): a Starship Piece's effect enchantment's "When this is launched, …" (Starport's Viking, Medivac
		/// …): run at the launch with the Starship as its source (Generic.LaunchStarship).</summary>
		public ISimpleTask LaunchTask { get; set; }

		/// <summary>HsServer (SPACE): a hand card that acts once it is next to <see cref="CollidesWith"/> (a card id) in the same hand
		/// (Xor'toth's Stars: "Once this is next to Star of Conclusion, deal $5 damage to all enemies"): <see cref="CollisionTask"/>
		/// runs once with this card as its source, then both leave the hand (Generic.CheckHandCollisions).</summary>
		public string CollidesWith { get; set; }
		public ISimpleTask CollisionTask { get; set; }

		/// <summary>HsServer (SPACE): "If this costs (0), …" (Librams of Clarity/Divinity/Faith, Void Ray): the card glows in hand
		/// while it costs 0 (Game.PublishDerived; the check itself is the cost it was played for, PlayedForZero).</summary>
		public bool GlowAtZeroCost { get; set; }

		/// <summary>HsServer (SPACE): a card in hand glows (POWERED_UP) while this holds — a condition its effect checks that no
		/// other glow rule covers ("If you're building a Starship, summon them now": Bad Omen, live POWERED_UP while building).</summary>
		public System.Func<SabberStoneCore.Model.Entities.IPlayable, bool> GlowWhile { get; set; }

		/// <summary>HsServer (SPACE): "(Transforms if you launched a Starship this game.)" (Hellion, Siege Tank, Thor): the card id
		/// it becomes in hand once its owner launched a Starship (Generic.UpgradeAfterLaunch).</summary>
		public string UpgradeAfterLaunch { get; set; }

		/// <summary>HsServer (SPACE): "Play another Templar to merge into an Archon!" — the card id two partners (cards with the same
		/// value) merge into (Generic.MergeTask).</summary>
		public string MergesInto { get; set; }

		/// <summary>HsServer (WILD_WEST): a "whenever &lt;state&gt;" ability of a minion in play (Howdyfin: "Whenever your hand has less
		/// than 3 cards in it, get a random Murloc"): after each sequence settles the engine runs <see cref="StateTask"/> for as
		/// long as this holds (Generic.RunStateChecks; Generic.HandBelow(n)). Argument: the minion.</summary>
		public System.Func<SabberStoneCore.Model.Entities.IPlayable, bool> StateCheck { get; set; }

		/// <summary>HsServer (WILD_WEST): what <see cref="StateCheck"/> runs (source: the minion; its own TRIGGER block).</summary>
		public ISimpleTask StateTask { get; set; }

		/// <summary>HsServer (WILD_WEST): the effect index of <see cref="StateTask"/>'s TRIGGER block (default 0).</summary>
		public int StateEffectIndex { get; set; }

		/// <summary>HsServer (WILD_WEST): whether <see cref="SecondPowerTask"/>'s POWER block shows in the history. Null: the rule —
		/// a Quickdraw card's (client tag 2905) doesn't (live Pocket Sand live-20261005 140614 57233-57320, Rehydrate
		/// live-20261007-vfx-decks 138254-138300 / 144013-144075: no ShowInHistory), any other's does (the Riffs).</summary>
		public bool? SecondPowerInHistory { get; set; }

		internal static Power OneTurnStealthEnchantmentPower =>
			new Power
			{
				Enchant = new Enchant(Effects.StealthEff),
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = RemoveEnchantmentTask.Task,
					RemoveAfterTriggered = true,
				}
			};
	}
}
