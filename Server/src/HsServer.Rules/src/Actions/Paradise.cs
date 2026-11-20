// HsServer addition (not upstream SabberStone): Perils in Paradise (ISLAND_VACATION, set 1905, with the Traveling Travel
// Agency mini-set WORK_) — the set's shared rules. Tags: Enums/HsTag.cs 990400-990449.
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
		/// <summary>
		/// Reopen (wiki Reopen: "refreshes a location that has used its ability, allowing it to be used again"; "no effect if
		/// the location was already ready"): its cooldown and exhaustion are cleared. The reopen trigger's block shows even
		/// when there's nothing to clear (live Tide Pools VAC_522, live-20261006-secrets-generators 1492 / 35915: 12 of 24 empty).
		/// </summary>
		public static void Reopen(IEntity location)
		{
			if (!(location is Location l) || l.Zone?.Type != Zone.PLAY) return;
			if (l.OnCooldown) l.OnCooldown = false;
			if (l.IsExhausted) l.IsExhausted = false;
		}

		/// <summary>"Put … on top of your deck" (Demonic Deal): a new card at the deck's top (the last slot, DeckZone.TopCard),
		/// not shuffled; CREATOR = the effect's source (PutOnBottom's sibling).</summary>
		public static IPlayable PutOnTop(Controller c, IEntity source, Card card)
		{
			if (card == null || c.DeckZone.IsFull) return null;
			IPlayable p = Entity.FromCard(c, card, new EntityData { { GameTag.CREATOR, source.Id } });
			c.DeckZone.Add(p);
			if (c.Game.History) c.Game.PowerHistory.Add(PowerHistoryBuilder.HideEntity(p));
			return p;
		}

		/// <summary>
		/// A minion "falls asleep" (Sleepy Resident; wiki: "Falling asleep refers to summoning sickness"): as if just summoned —
		/// it can't attack this turn unless it has Charge; a Rush minion may still attack minions (BoardZone.Add's rule). Only
		/// matters to the player whose turn it is (the other side's minions wake at their turn start anyway).
		/// </summary>
		public static void FallAsleep(Minion m)
		{
			if (m == null || m is Location || m.Zone?.Type != Zone.PLAY || m.Controller != m.Game.CurrentPlayer || m.HasCharge) return;
			if (m.IsRush && m.NumAttacksThisTurn == 0)
			{
				m.AttackableByRush = true;
				m.Game.RushMinions.Add(m.Id);
			}
			else
				m.IsExhausted = true;
		}

		/// <summary>"Equip a random weapon from your deck" (Selfless Sidekick, Weapons Attendant): the deck's weapon itself (a zone
		/// move), its aura and trigger switched on.</summary>
		public static Weapon EquipFromDeck(Controller c, Func<Weapon, bool> which = null)
		{
			var weapons = c.DeckZone.OfType<Weapon>().Where(w => which?.Invoke(w) ?? true).ToList();
			if (weapons.Count == 0) return null;
			var weapon = weapons[c.Game.Random.Next(weapons.Count)];
			c.DeckZone.Remove(weapon);
			weapon.Card.Power?.Aura?.Activate(weapon);
			weapon.Card.Power?.Trigger?.Activate(weapon);
			c.Hero.AddWeapon(weapon);
			return weapon;
		}

		/// <summary>"The newest expansion" (Resort Valet): the expansion in Standard now with the highest ReleaseOrder
		/// (CardSet.json; mini-set cards carry their expansion's set — wiki Travelmaster Dungar).</summary>
		public static int NewestExpansion() => Loader.ReprintCards.NewestExpansion();

		/// <summary>"From another class" for a card in hand / play (Concierge): not the hero's class and not Neutral (engine-rules
		/// "Dual- and tri-class cards": Card.HasClass).</summary>
		public static bool OfAnotherClass(IPlayable p) =>
			p?.Controller != null && !p.Card.HasClass(p.Controller.HeroClass) && !p.Card.HasClass(CardClass.NEUTRAL);

		/// <summary>How many picks a "Choose Twice / Choose Thrice" card makes (client text marker), or 0.</summary>
		public static int ChooseCount(Card card) =>
			card?.Text == null ? 0 : card.Text.Contains("Choose Thrice") ? 3 : card.Text.Contains("Choose Twice") ? 2 : 0;

		/// <summary>
		/// "Choose Twice / Thrice - A; B; C" (Sleep Under the Stars, Number Cruncher, Forest Lord Cenarius): not Choose One (wiki
		/// Choose multiple: "The player can choose the same effect multiple times"; not affected by Fandral Staghelm). The
		/// options are the card's RelatedCards that are spells, offered again for each pick; each pick casts a NEW card of it at
		/// the chooser (live EDR_209, live-20261007-vfx-decks 146632-146990: the picked option to the graveyard, a new entity
		/// cast with CARD_TARGET = the chooser, shown big).
		/// </summary>
		public static void ChooseMultiple(Controller c, IPlayable chooser, int picks)
		{
			if (picks <= 0 || chooser == null) return;
			Card[] options = Loader.ReprintCards.RelatedCardIds(chooser.Card.Id)?.Select(Cards.TryFromId)
				.Where(x => x != null && x.Type == CardType.SPELL).ToArray();
			if (options == null || options.Length == 0) return;
			CreateChoiceCards.Invoke(c, chooser, null, ChoiceType.GENERAL, ChoiceAction.STACK, options, new CustomTask((g, c2, s, pick, stack) =>
			{
				if (pick == null) return;
				if (pick.Zone?.Type == Zone.SETASIDE) c2.GraveyardZone.Add(c2.SetasideZone.Remove(pick));
				CastOption(c2, chooser, pick.Card);
				ChooseMultiple(c2, chooser, picks - 1);
			}));
		}

		/// <summary>A chosen option cast by its chooser: a new card of it, its effect aimed at the chooser (CARD_TARGET).</summary>
		public static void CastOption(Controller c, IPlayable chooser, Card option)
		{
			Game g = c.Game;
			IPlayable spell = Entity.FromCard(c, option, new EntityData { [GameTag.CREATOR] = chooser.Id, [GameTag.CARD_TARGET] = chooser.Id });
			c.GraveyardZone.Add(spell);
			if (g.History)
			{
				var shown = new List<int> { spell.Id };
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SHOW_BIG_CARD, Data = 1, Info = shown });
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.HISTORY_TARGET, Data = 0, Info = shown });
			}
			if (spell.Card.Power?.PowerTask is ISimpleTask task)
			{
				task.Process(g, c, spell, chooser);
				g.ProcessTasks();
			}
		}

		/// <summary>
		/// "Repeat" a card the player played (Sasquawk, Pet Parrot, Chatty Macaw, Mistah Vistah): it is brought in by the effect
		/// (Generic.PlayByEffect: a minion / Location summoned far right without its Battlecry, a spell cast at a random target, a
		/// weapon equipped) — forum "Sasquawk Mechanic Intentional?": "it resummons minions ... Battlecries don't repeat"; "it
		/// recasts spells ... and spells target different minions". <paramref name="enemiesIfPossible"/>: Chatty Macaw's "at an
		/// enemy (at a random enemy if possible)".
		/// </summary>
		public static void RepeatCard(Game g, Controller c, IEntity source, Card card, bool enemiesIfPossible = false)
		{
			if (card == null) return;
			if (card.Type == CardType.SPELL && enemiesIfPossible)
				new CastRandomSpellTask(enemiesIfPossible: true).Process(g, c, source, Entity.FromCard(c, card));
			else
				PlayByEffect(g, c, source, card);
		}

		/// <summary>Client tag SPAWN_TIME_COUNT (1556): an enchantment's ×N banner (EnchantmentBanner reads it).</summary>
		public const GameTag SPAWN_TIME_COUNT = (GameTag)1556;

		/// <summary>An enchantment that grows in place when given again (live Careful Bear, live-20261007-zilliax-etc-remix 15396:
		/// one WORK_024te, SPAWN_TIME_COUNT up, the stats +1/+1): the target's existing one from the same card gets the amounts,
		/// else a new one with them (its Enchant reads TAG_SCRIPT_DATA_NUM_1 / _2).</summary>
		public static void GiveStacked(Game g, IEntity source, IPlayable target, Card enchantment, int add1, int add2)
		{
			if (target == null || enchantment == null) return;
			Enchantment existing = target.AppliedEnchantments?.LastOrDefault(e => e.Card == enchantment);
			if (existing != null)
			{
				GrowEnchantment(existing, add1, add2);
				if (g.History) existing[SPAWN_TIME_COUNT] = Math.Max(1, existing[SPAWN_TIME_COUNT]) + 1;
			}
			else
				AddEnchantmentBlock(g, enchantment, source as IPlayable ?? target, target, add1, add2, 0);
		}

		/// <summary>Client player tag 4453: the times the hero was damaged this turn (live: zeroed for both players at every turn
		/// change). HsTag.HERO_DAMAGE_THIS_TURN holds the amount.</summary>
		public const GameTag HERO_DAMAGED_TIMES_THIS_TURN = (GameTag)4453;

		/// <summary>Client player tag 3687: the times the hero took damage on its own turns this game (live: never reset; moves
		/// with 3110). Sauna Regular: "each time your hero has taken damage on your turn".</summary>
		public const GameTag HERO_DAMAGED_TIMES_ON_OWN_TURNS = (GameTag)3687;

		/// <summary>Sunsapper Lynessa's "(2) or less" (set by its script from CardValues).</summary>
		public static int CheapSpellsMaxCost { get; set; } = 2;

		/// <summary>"Your spells that cost (2) or less cast twice" (HsTag.CHEAP_SPELLS_CAST_TWICE): the cost it was played for
		/// (forum t/136476: a Prismatic Beam reduced to 2 casts twice).</summary>
		internal static bool CheapSpellCastsTwice(Controller c, Spell spell)
		{
			if (c.ControllerAuraEffects[HsTag.CHEAP_SPELLS_CAST_TWICE] <= 0) return false;
			PlayHistoryEntry played = c.PlayHistory.LastOrDefault(h => h.SourceId == spell.Id);
			int cost = played.SourceCard != null ? played.Cost : spell.Cost;
			return cost <= CheapSpellsMaxCost;
		}

		/// <summary>The client's spell schools (SPELL_SCHOOL 1-7: Arcane, Fire, Frost, Nature, Holy, Shadow, Fel).</summary>
		public const int SpellSchools = 7;

		/// <summary>
		/// (2026-11-16) "Your next spell of each school casts twice" (Therazane the Earthmother): one charge per school, a player
		/// enchantment whose Power.SchoolCastsTwice is set (its TAG_SCRIPT_DATA_NUM_1 = the school), spent by the first spell of that
		/// school the player plays. Live (session-20261023-152211, BE_337): one BE_337e2 on the player per school, a BE_337e "Casts
		/// Twice" on each spell of that school in hand; the spell cast, its school's mark goes to the graveyard.
		/// </summary>
		public static void GiveSchoolsCastTwice(Controller c, IPlayable source, Card playerMark, Card cardMark)
		{
			Game g = c.Game;
			for (int school = 1; school <= SpellSchools; school++)
			{
				AddEnchantmentBlock(g, playerMark, source, c, school, -1);
				Enchantment e = c.AppliedEnchantments?.LastOrDefault(x => x.Card == playerMark);
				if (e == null || cardMark == null) continue;
				foreach (IPlayable card in c.HandZone.GetAll().Where(x => x is Spell && x[(GameTag)1635] == school))
					AddEnchantmentBlock(g, cardMark, e, card);
			}
		}

		/// <summary>Is <paramref name="spell"/> the next of its school: spends that school's charge and the marks it put on cards.</summary>
		public static bool SchoolSpellCastsTwice(Controller c, Spell spell)
		{
			int school = spell[(GameTag)1635];
			if (school <= 0) return false;
			Enchantment charge = c.AppliedEnchantments?.FirstOrDefault(e => e.Card.Power?.SchoolCastsTwice == true && e.ScriptTag1 == school);
			if (charge == null) return false;
			foreach (IPlayable card in c.HandZone.GetAll().Concat(new IPlayable[] { spell }))
				foreach (Enchantment on in card.AppliedEnchantments?.Where(x => x.Creator == charge).ToList() ?? new List<Enchantment>())
					on.Remove();
			charge.Remove();
			return true;
		}

		/// <summary>(2026-11-19) "Your next spell this turn casts twice" (Brain of Yogg-Saron): spends the player's oldest such charge
		/// (Power.NextSpellCastsTwice).</summary>
		public static bool NextSpellCastsTwice(Controller c)
		{
			Enchantment charge = c.AppliedEnchantments?.FirstOrDefault(e => e.Card.Power?.NextSpellCastsTwice == true);
			if (charge == null) return false;
			charge.Remove();
			return true;
		}

		/// <summary>Niri of the Crater's "1-Cost" (set by its script from CardValues).</summary>
		public static int ExactCostSpellsCost { get; set; } = 1;

		/// <summary>"Whenever you cast a 1-Cost spell, cast it twice" (HsTag.EXACT_COST_SPELLS_CAST_TWICE): the cost it was played for.</summary>
		internal static bool ExactCostSpellCastsTwice(Controller c, Spell spell)
		{
			if (c.ControllerAuraEffects[HsTag.EXACT_COST_SPELLS_CAST_TWICE] <= 0) return false;
			PlayHistoryEntry played = c.PlayHistory.LastOrDefault(h => h.SourceId == spell.Id);
			int cost = played.SourceCard != null ? played.Cost : spell.Cost;
			return cost == ExactCostSpellsCost;
		}

		/// <summary>"From another class" (wiki Class: "every class of the card must be different from the player to qualify";
		/// Neutral is no class — wiki Conniving Conman 30.4.0: not Reno, Lone Ranger). The player's current class.</summary>
		public static bool IsFromAnotherClass(Controller c, Card card) =>
			card != null && !card.HasClass(CardClass.NEUTRAL) && !card.HasClass(CardClass.INVALID) && !card.HasClass(c.HeroClass);

		/// <summary>The cards this player played from another class (PlayHistory: played cards only, no Hero Powers).</summary>
		public static IEnumerable<PlayHistoryEntry> PlayedFromAnotherClass(Controller c) =>
			c.PlayHistory.Where(h => h.SourceCard != null && h.SourceCard.Type != CardType.HERO_POWER && IsFromAnotherClass(c, h.SourceCard));

		/// <summary>"Put … on top of your deck" for a card that exists (Envoy of Prosperity): a backward move (enchantments go),
		/// the deck's top slot, no shuffle; a full deck: the graveyard (rules C2 Z1).</summary>
		public static bool PutOnTopOfDeck(Controller c, IPlayable p)
		{
			if (p == null || p.Zone?.Type == Zone.DECK) return false;
			if (c.DeckZone.IsFull)
			{
				if (p.Zone != null && p.Zone.Type != Zone.GRAVEYARD) p.Controller.GraveyardZone.Add(p.Zone.Remove(p));
				return false;
			}
			IPlayable moved = p.Zone?.Remove(p) ?? p;
			ResetForBackwardMove(moved);
			c.DeckZone.Add(moved);
			if (c.Game.History) c.Game.PowerHistory.Add(PowerHistoryBuilder.HideEntity(moved));
			return true;
		}

		/// <summary>
		/// Fortunes (Narain Soothfancy: "copies of the top card of your deck"; wiki: "The Fortunes dynamically update themselves
		/// whenever the top card of the player's deck is changed"): a hand card with HsTag.FORTUNE = its marker enchantment's dbf
		/// (VAC_420e1, SHIFTING; its DEFINING_ENCHANTMENT = the blank Fortune VAC_420t for an empty deck) becomes the deck's top
		/// card again whenever that changed (after each sequence, Game.DeathProcessingAndAuraUpdate).
		/// </summary>
		internal static void UpdateFortunes(Game g)
		{
			foreach (Controller c in new[] { g.Player1, g.Player2 })
			{
				if (c?.HandZone == null) continue;
				foreach (IPlayable p in c.HandZone.GetAll())
				{
					if (p[HsTag.FORTUNE] <= 0) continue;
					Card marker = Cards.FromAssetId(p[HsTag.FORTUNE]);
					Card top = c.DeckZone.Count > 0 ? c.DeckZone.TopCard.Card : (marker != null ? Cards.FromAssetId(marker[(GameTag)469]) : null);
					if (top == null || marker == null || p.Card == top) continue;
					IPlayable changed = ChangeEntityBlock.Invoke(c, p, top, true);
					if (changed == null) continue;
					changed[HsTag.FORTUNE] = marker.AssetId;
					AddEnchantmentBlock(g, marker, changed, changed, 0, 0, 0);
				}
			}
		}

		/// <summary>
		/// Sanc'Azel: "After this attacks, turn into a location". Live (vfx-decks 208748; titans 144826 26828): a NEW location each time
		/// (FULL_ENTITY in SETASIDE: NUM_1 = the minion's Attack, its damage, NUM_2 = the minion) takes the minion's slot inside the card's
		/// OverrideSpawn effect; the minion is set aside. A second time it keeps the previous location's cooldown and exhaustion. Its
		/// Health = the minion's max Health (wiki Sanc'Azel: Attack and Health tied to the location's buff and durability).
		/// </summary>
		public static Location TurnIntoLocation(Minion m, Card locationCard)
		{
			if (m == null || m.Zone?.Type != Zone.PLAY || m.IsDead || m.ToBeDestroyed || locationCard == null) return null;
			Controller c = m.Controller;
			Game g = m.Game;
			int pos = m.ZonePosition;
			Location before = m[HsTag.LINKED_FORM] > 0 && g.IdEntityDic.TryGetValue(m[HsTag.LINKED_FORM], out IPlayable linked) ? linked as Location : null;
			var loc = (Location)Entity.FromCard(c, locationCard, new EntityData { [GameTag.CREATOR] = m.Id }, c.SetasideZone);
			loc[GameTag.HEALTH] = m[GameTag.HEALTH];
			loc[GameTag.TAG_SCRIPT_DATA_NUM_1] = m.AttackDamage;
			loc.Damage = m.Damage;
			loc[GameTag.TAG_SCRIPT_DATA_NUM_2] = m.Id;
			if (before != null)
			{
				if (before.OnCooldown) loc.OnCooldown = true;
				if (before.IsExhausted) loc.IsExhausted = true;
			}
			loc[HsTag.LINKED_FORM] = m.Id;
			m[HsTag.LINKED_FORM] = loc.Id;
			c.SetasideZone.Remove(loc);
			c.SetasideZone.Add(c.BoardZone.Remove(m));
			c.BoardZone.Add(loc, Math.Min(pos, c.BoardZone.Count));
			return loc;
		}

		/// <summary>Sanc'Azel's location "Turn back into a minion": a NEW copy of the minion (live vfx-decks 208870: FULL_ENTITY VAC_923, its
		/// damage, COPIED_FROM_ENTITY_ID = the first minion) in the location's slot, with the location's damage (<paramref name="damage"/>:
		/// after this use); the location is set aside (it keeps its cooldown: wiki "a closed location that cannot be activated again this
		/// turn" — the next location takes it).</summary>
		public static Minion TurnBackIntoMinion(Location loc, int damage)
		{
			if (loc == null || loc.Zone?.Type != Zone.PLAY || loc[HsTag.LINKED_FORM] <= 0) return null;
			Game g = loc.Game;
			if (!g.IdEntityDic.TryGetValue(loc[HsTag.LINKED_FORM], out IPlayable linked) || !(linked is Minion m) || m.Zone?.Type != Zone.SETASIDE) return null;
			Controller c = loc.Controller;
			int pos = loc.ZonePosition;
			int first = m[HsCopiedFrom] > 0 ? m[HsCopiedFrom] : m.Id;
			if (!(Copy(c, loc, m, Zone.SETASIDE, keepEnchantments: true) is Minion back)) return null;
			back[HsCopiedFrom] = first;
			back.Damage = damage;
			back.NumAttacksThisTurn = 0;
			back[HsTag.LINKED_FORM] = loc.Id;
			c.SetasideZone.Add(c.BoardZone.Remove(loc));
			c.SetasideZone.Remove(back);
			c.BoardZone.Add(back, Math.Min(pos, c.BoardZone.Count));
			return back;
		}

		private const GameTag HsCopiedFrom = HsTag.COPIED_FROM_ENTITY_ID;

		/// <summary>
		/// "Excess damage" (Piercing Shot, Unstable Shadow Blast, Invasive Shadeleaf, Char, Fine Print): the part of the hit above
		/// the minion's current Health, worked out BEFORE the damage — Blizzard (forum t/70422, Unstable Shadow Blast on a Divine
		/// Shield minion): "the effect does damage equal to that minion's current health, the rest to the target Hero. Divine
		/// Shield negates damage, it doesn't change the amount of incoming damage." <paramref name="spell"/>: the amount gets
		/// Spell Damage ($N).
		/// </summary>
		public static int ExcessDamage(IPlayable source, Minion m, int amount, bool spell) =>
			m == null ? 0 : Math.Max(0, (spell ? SpellDamageAmount(source, amount) : amount) - Math.Max(0, m.Health));

		private static readonly System.Text.RegularExpressions.Regex PrintedTemporaryText = new System.Text.RegularExpressions.Regex(
			@"^\s*(?:\[x\])?\s*(?:<b>[^<]+</b>,?\s*)*<b>Temporary</b>");

		/// <summary>A card with Temporary as its own keyword (Deadline: wiki "the first card to have the keyword on its own"; the
		/// keyword line of its text — the client tag TEMPORARY sits on the cards that GIVE it): Temporary whenever it enters its
		/// owner's hand (wiki Temporary: "only discarded at the end of the controller's turn").</summary>
		public static bool PrintedTemporary(Card card) =>
			card?.ClientText is string text && text.Contains("Temporary") && PrintedTemporaryText.IsMatch(text);
	}
}
