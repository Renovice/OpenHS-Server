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
using System;
using System.Collections.Generic;
using SabberStoneCore.Auras;
using SabberStoneCore.Enchants;
using SabberStoneCore.Model;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Model.Zones;
using SabberStoneCore.Tasks.PlayerTasks;

namespace SabberStoneCore.Actions
{
	/// <summary>
	/// Container of game logic functionality, which is invoked by processing a selected option
	/// through <see cref="Game.Process(PlayerTask)"/>.
	/// </summary>
	public static partial class Generic
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
	{
		public static Func<IPlayable, ICharacter, int, bool, int> DamageCharFunc
			=> (source, target, amount, applySpellDmg) =>
			{
				if (source is Spell spell) // HsServer: only spells get Spell Damage (Lightning Jolt is a hero power)
				{
					// HsServer: rule book (Spell_Damage): "Spell Damage increases the damage dealt by any spell, with only a few
					// exceptions" (in-hand / on-draw spells, a character as the source). The client marks the affected numbers
					// with "$" in the card text (and AFFECTED_BY_SPELL_POWER), the exceptions have none: that decides it, not
					// whether a script remembered to ask (Eviscerate, Holy Fire, Starfall... never got it).
					if (applySpellDmg || spell.Card.IsAffectedBySpellDamage && spell[HsTag.SPELL_DAMAGE_COUNTED] == 0)
					{
						// HsServer: plus Spell Damage for the spell's school ("Fire Spell Damage +2"), client tag SPELL_SCHOOL (1635),
						// and a spell's own "Spell Damage +N" (Silvermoon Farstrider, Arcane Quiver: on the spell card) —
						// Generic.SpellDamageBonus (Actions/WildWest.cs; also "a minion of that Cost": SpellDamageAmount)
						amount += SpellDamageBonus(spell);
					}
					// rule book DH2a: "Then apply damage-doubling effects like Prophet Velen" — every spell, after Spell Damage; not a split
					// spell's hit: its count was doubled (SPELL_DAMAGE_COUNTED 2, x_Damage.txt:32 / :62 "other than Arcane Missiles, Avenging
					// Wrath, Spreading Madness, Volcano, Timepiece of Horror…")
					if (source.Controller.ControllerAuraEffects[GameTag.SPELLPOWER_DOUBLE] > 0 && spell[HsTag.SPELL_DAMAGE_COUNTED] != 2)
						amount *= (int)Math.Pow(2, source.Controller.ControllerAuraEffects[GameTag.SPELLPOWER_DOUBLE]);
					// the same step: "minions take double damage from spells" (Mo'arg Artificer, an aura on both players; the rule
					// book: it "continuously references other cards' properties")
					if (target is Minion && target.Controller.ControllerAuraEffects[HsTag.MINIONS_TAKE_DOUBLE_SPELL_DAMAGE] > 0)
						amount *= (int)Math.Pow(2, target.Controller.ControllerAuraEffects[HsTag.MINIONS_TAKE_DOUBLE_SPELL_DAMAGE]);
				}
				else if (source is HeroPower)
				{
					// TODO: Consider this part only when TGT or Rumble is loaded
					amount += source.Controller.Hero.HeroPowerDamage;
					if (source.Controller.ControllerAuraEffects[GameTag.HERO_POWER_DOUBLE] > 0)
						amount *= (int)Math.Pow(2, source.Controller.ControllerAuraEffects[GameTag.HERO_POWER_DOUBLE]);
				}
				// HsServer: "Enemy minions take double damage during your turn" (Enchanter, an aura on its owner) — every source,
				// the damage-doubling step
				if (target is Minion && target.Controller?.Opponent is Controller doubler && doubler == target.Game.CurrentPlayer &&
				    doubler.ControllerAuraEffects[HsTag.ENEMY_MINIONS_DOUBLE_DAMAGE_ON_MY_TURN] > 0)
					amount *= (int)Math.Pow(2, doubler.ControllerAuraEffects[HsTag.ENEMY_MINIONS_DOUBLE_DAMAGE_ON_MY_TURN]);
				MissilePerHit(source, target);
				return target.TakeDamage(source, amount);
			};

		/// <summary>
		/// HsServer: a card whose effect is a missile (its server-named / recorded Missile) fires one at EACH character it
		/// damages, from the card (a spell: its caster's hero), each with its own TARGET (live: Fel Barrage's two shots each a
		/// SUB_SPELL + TARGET of its own; user 2026-10-05: Noxious Cadaver fires at the enemy AND at your hero). The open missile
		/// aimed at this character that hasn't hit yet (TaskProcessor: the chosen target) is it; any other hit closes it and
		/// fires a new one. In the card's own play effect, or re-aiming an open one (a trigger with a Battlecry missile doesn't get one).
		/// A card that places its own effects (Power.OwnFx) does it itself. Not for one step that damages a group ("1 damage to
		/// all other minions": Game.AreaDamage) — that is an area hit, not missiles.
		/// </summary>
		private static void MissilePerHit(IPlayable source, ICharacter target)
		{
			Game g = source?.Game;
			if (g == null || !g.History || target == null || source.Card.Power?.OwnFx == true || g.AreaDamage) return;
			if (source.Card.IsTitan) return; // HsServer (TITANS): a Titan's missiles belong to its abilities (Generic.TitanAbilityFx)
			string missile = Loader.SubSpells.Find(source.Card.Id, Loader.SubSpells.Role.Missile);
			if (missile == null) return;
			var open = g.PowerHistory.OpenSubSpellOf(missile);
			// the open missile is this hit's while it hasn't hit anything yet (aimed at this character, or a Deathrattle's that
			// collects); one that already hit is spent: the next hit — even at the same character — fires a new one (Fel Barrage)
			if (open is { } o && o.Hits == 0 && (o.Start.Targets.Contains(target.Id) || o.Start.CollectTargets)) return;
			bool ownPlay = g.PowerHistory.CurrentEffectBlock is { BlockType: BlockType.POWER } block && block.Source == source.Id;
			if (open == null && !ownPlay) return;
			if (open != null) g.CloseSubSpells(source.Id);
			if (g.OpenSubSpell(source, missile, new[] { target.Id }, shownSource: source is Spell ? source.Controller.Hero.Id : 0))
				g.PowerHistory.NewTargetRound();
		}

		public static Func<Controller, int, bool> AddTempMana
			=> delegate (Controller c, int amount)
			{
				// HsServer: rules (user, 2026-10-01) — temporary mana can take you above 10 (The Coin at 10/10 gives 11)
				c.TemporaryMana += amount;
				return true;
			};

		public static Func<Controller, int, bool, bool> ChangeManaCrystal
			=> delegate (Controller c, int amount, bool fill)
			{
				int baseMana = c.BaseMana;
				int total = baseMana + amount;
				// HsServer: the player's own maximum (MAXRESOURCES: 10, Audio Amplifier's 11)
				int max = c[GameTag.MAXRESOURCES] > 0 ? c[GameTag.MAXRESOURCES] : Controller.MaxResources;
				if (total > max)
				{
					c.Game.Log(LogLevel.INFO, BlockType.PLAY, "ChangeManaCrystal", !c.Game.Logging ? "" : $"{c.Name} is already capped in {max} mana crystals.");
					if (!fill)
						c.UsedMana += max - c.BaseMana;
					c.BaseMana = max;

				}
				else if (total < 0)
				{
					c.Game.Log(LogLevel.INFO, BlockType.PLAY, "ChangeManaCrystal", !c.Game.Logging ? "" : $"{c.Name} is back to minimum of 0 mana crystals.");
					c.BaseMana = 0;
				}
				else
				{
					c.BaseMana = total;
					if (!fill)
						c.UsedMana += amount;
				}

				return true;
			};

		/// <summary>
		/// HsServer: rules C2 Z1 / Full Zone Instant Removal — a minion in play moving into a full zone (bounce into a full
		/// hand, stolen onto a full board, shuffled into a full deck) dies at once: it leaves play straight into its
		/// owner's Graveyard (it can't trigger or be triggered), and its Death Event (Deathrattle, on-death triggers)
		/// resolves in the next Death Phase in order of play.
		/// </summary>
		public static void FullZoneKill(Minion m)
		{
			m.LastBoardPosition = m.ZonePosition;
			m.Zone.Remove(m);
			m.InstantlyRemoved = true;
			m.DestroyedByEffect = true;
			m.ToBeDestroyed = true; // queues the Death Event
			m.Controller.GraveyardZone.Add(m);
		}

		/// <summary>
		/// HsServer: a Quest is completed — it is revealed and leaves play now; its reward comes at the Quest Reward Step
		/// (after the Phase it was completed in, rules D4 / A5), so the completing effect can't touch it, and a reward for
		/// a full hand waits until there is room.
		/// </summary>
		public static void CompleteQuest(Controller c, Spell quest, Card reward)
		{
			quest[GameTag.REVEALED] = 1;
			quest.ActivatedTrigger?.Remove();
			c.SecretZone.RemoveQuest(quest);
			c.GraveyardZone.Add(quest);
			if (reward != null)
				c.PendingQuestRewards.Add((reward, quest.Id));
		}

		/// <summary>
		/// HsServer: engine rule (user, 2026-09-30) — a card an effect generates is a NEW entity, never the old one, even
		/// when it's a copy of it. A dead card never leaves the Graveyard: "return this to your hand", "shuffle your dead
		/// minions into your deck", "summon a minion that died" all make a fresh card of it (CREATOR = the dead one).
		/// Only real moves (draw, a bounce from the board, shuffling a card back) keep the entity.
		/// </summary>
		public static IPlayable NewFromGraveyard(Controller c, IPlayable dead)
		{
			IPlayable fresh = Entity.FromCard(c, dead.Card);
			fresh[GameTag.CREATOR] = dead.Id;
			c.Game.DeadPoolRead = (c.Game.MoveNumber, c.Game.TasksThisMove); // a resurrect: its summons line up right (SummonPosition)
			return fresh;
		}

		/// <summary>
		/// HsServer (user decision 2026-10-10, "follow live"): a dead card an effect brings back AS ITSELF — returned to a hand,
		/// shuffled into a deck, gone Dormant to awaken as itself — leaves the Graveyard for SETASIDE; the card that comes back
		/// is a new entity (<see cref="NewFromGraveyard"/>, user rule: generated cards are new entities). Live (decoded
		/// session-20261003-185040 18204: Kingsbane id 33 GRAVEYARD → its DEATHRATTLE block → ZONE SETASIDE, then the new copy's
		/// FULL_ENTITY and SHUFFLE_DECK; Deathbringer Saurfang 20261004-163220 4036, Elder Taggawag 20261002-204752 94487,
		/// Lucentbark 20261005-140614 49763, Elixir of Hope's "return this" 20261004-224614 79240, Cheat Death's "return it"
		/// 20260929-183021 29962: 46 of the 48 recorded moves in 110 sessions). The exception live shows: a minion shuffled back "keeping
		/// any enchantments" (Immortal Prelate, Adaptive Amalgam: SpecificTask.ImmortalPrelate) ends back in the Graveyard (2 of 2). A
		/// resurrect ("summon a minion that died", Reborn) leaves it there. Nothing happens to a card not in the Graveyard (a Deathrattle
		/// triggered while alive).
		/// </summary>
		public static void SetAsideDead(IPlayable dead)
		{
			if (dead?.Zone?.Type != Zone.GRAVEYARD) return;
			dead.Controller.SetasideZone.Add(dead.Zone.Remove(dead));
		}

		/// <summary>HsServer: a dead card coming back as itself: set aside (<see cref="SetAsideDead"/>), and its new card.</summary>
		public static IPlayable ReplaceDead(Controller c, IPlayable dead)
		{
			SetAsideDead(dead);
			return NewFromGraveyard(c, dead);
		}

		/// <summary>HsServer: return <paramref name="minion"/> to <paramref name="c"/>'s hand; the card now in hand (a new one
		/// for a minion that already died), or null if none got there.</summary>
		public static IPlayable ReturnToHand(Controller c, Minion minion)
		{
			if (minion.Zone?.Type == Zone.GRAVEYARD)
			{
				if (c.HandZone.IsFull) return null;
				IPlayable fresh = ReplaceDead(c, minion);
				return AddHandPhase.Invoke(c, fresh) ? fresh : null;
			}
			return ReturnToHandBlock.Invoke(c, minion) && minion.Zone?.Type == Zone.HAND ? minion : null;
		}

		public static Func<Controller, Minion, bool> ReturnToHandBlock
			=> delegate (Controller c, Minion minion)
			{
				// HsServer: a minion that already died ("Deathrattle: return this minion to your hand", Elixir of Hope)
				// comes back as a NEW card: nothing that happened to the dead one follows the new one (user rule + rules C3: a
				// backward move resets everything); the dead one is set aside, as live (ReplaceDead). A full hand creates
				// nothing (rules C2 Z0).
				if (minion.Zone?.Type == Zone.GRAVEYARD)
					return !c.HandZone.IsFull && AddHandPhase.Invoke(c, ReplaceDead(c, minion));
				ExitCombat(minion);

				if (c.HandZone.IsFull && minion.Zone?.Type == Zone.PLAY)
				{
					FullZoneKill(minion); // rules C2 Z1: bounced into a full hand
					return false;
				}

				if (!RemoveFromZone.Invoke(c, minion))
					return false;

				// reset minion (HsServer: ResetForBackwardMove — a Titan gets new abilities, identity marks stay)
				ResetForBackwardMove(minion);

				c.Game.Log(LogLevel.INFO, BlockType.PLAY, "ReturnToHandBlock", !c.Game.Logging ? "" : $"{c.Name} gets {minion} returned.");

				// (HsServer EMERALD_DREAM: "Whenever this enters your hand from the battlefield" — Harbinger of the Blighted)
				minion[HsTag.ENTERED_HAND_FROM_PLAY] = 1;
				bool added = AddHandPhase.Invoke(c, minion);
				minion[HsTag.ENTERED_HAND_FROM_PLAY] = 0;
				return added;
			};

		/// <summary>
		/// HsServer: user rule (2026-09-30) — a card moving "backward" (board → hand/deck, hand → deck) loses its
		/// enchantments; moving forward (deck → hand → board) keeps them. Tradeable trades and Dark Gift-style "put on top
		/// with +X" are exceptions by design (they don't come through here). Call before the entity's Reset.
		/// </summary>
		public static void RemoveEnchantmentsOnBackwardMove(IPlayable p)
		{
			List<Enchantment> enchantments = p.AppliedEnchantments;
			if (enchantments == null)
				return;
			for (int i = enchantments.Count - 1; i >= 0; i--)
				enchantments[i].Remove();
		}

		public static Func<Controller, IPlayable, bool> RemoveFromZone
			=> delegate (Controller c, IPlayable playable)
			{
				playable.Zone.Remove(playable);
				return true;
			};

		public static Func<Controller, IPlayable, bool> AddHandPhase
			=> delegate (Controller c, IPlayable playable) { return AddHandPhaseAt(c, playable, -1); };

		/// <summary>
		/// HsServer (2026-11-14): the to-hand flight of a card created by <paramref name="creator"/>'s own effect when nothing
		/// recorded names one. Live (154 sessions, 166 creator/block pairs; FINDINGS 2026-11-14): the client flies a new hand
		/// card from its CREATOR only inside a spawn-override effect (Card.ActivateHandSpawnSpell: no override = it just appears
		/// in its slot). Cards whose own client effect for the block does it (ClientOwnFx: Fire Fly, Babbling Book, Zola…) get
		/// no server effect (68/77); cards with only the generic Play / Trigger get a server SpawnToHand SUB_SPELL (87/89) —
		/// a themed one per card where recorded, else live's plain one (Low Security Wing's). Only in the creator's own block and
		/// when no other effect is open (the card then arrives inside that one, as live).
		/// </summary>
		internal static string PlainSpawnToHand(Game g, IPlayable creator)
		{
			if (!g.History || creator?.Card == null || g.AnySubSpellOpen) return null;
			if (g.PowerHistory.CurrentEffectBlock is not { } block || block.Source != creator.Id) return null;
			if (Loader.BlockMeta.HasOwnFx(creator.Card.Id, block.BlockType)) return null;
			// (recorded effects for this block but no to-hand one: live sent none — Spirit Gatherer, Paleomancy)
			foreach (var a in Loader.SubSpells.Anchors(creator.Card.Id))
				if (block.BlockType == BlockType.POWER ? a.Block == "POWER" : a.Block != "POWER")
					return null;
			return Game.GenericSpawnToHand;
		}

		/// <summary>HsServer: AddHandPhase into hand slot <paramref name="position"/> (-1: right-most; Twinspell's copy
		/// takes the played card's slot).</summary>
		public static bool AddHandPhaseAt(Controller c, IPlayable playable, int position, IPlayable replacing = null)
			{
				if (c.HandZone.IsFull)
				{
					c.Game.Log(LogLevel.INFO, BlockType.PLAY, "AddHandPhase", !c.Game.Logging ? "" : $"Hand ist full. Card {playable} drawn is burnt to graveyard.");
					// HsServer (VIOLET_HOLD): Godfrey the Betrayer — "Overdrawn cards return to your hand when you have space" (wiki: it still
					// burns; enchantments kept)
					if (playable != null && c[HsTag.GODFREY_RULE] > 0 && c.LastCardDrawn == playable.Id && playable[HsTag.DRAWN_ON_TURN] == c.Game.Turn)
						SaveOverdrawn(c, playable);
					c.GraveyardZone.Add(playable);
					// HsServer (VIOLET_HOLD): IMPFERNAL! — an overdrawn card's "(Also triggers in hand or deck.)" Deathrattle
					if (playable != null && c.LastCardDrawn == playable.Id && playable[HsTag.DRAWN_ON_TURN] == c.Game.Turn) AnywhereDeathrattle(playable);
					return false;
				}

				playable = CounterfeitCoin(c, playable); // HsServer (VIOLET_HOLD): Aya, Lotus Kingpin
				c.Game.Log(LogLevel.INFO, BlockType.PLAY, "AddHandPhase", !c.Game.Logging ? "" : $"adding to hand {playable}.");
				// HsServer: a card an effect just created enters the hand in its FULL_ENTITY, as live: in HAND at its slot,
				// CREATOR (the client flies it in from there), CREATOR_DBID, SPAWN_TIME_COUNT, NUM_TURNS_IN_HAND; then
				// DISPLAYED_CREATOR ("Created by"). A card that already exists on the client (a draw, a shown Discover pick)
				// is revealed with SHOW_ENTITY.
				bool fresh = c.Game.History && playable != null && playable.Zone == null &&
				             c.Game.PowerHistory.UnsentCreation(playable.Id, c.Game.HistoryMoveFloor(), out _, neverInZone: true) >= 0;
				if (fresh)
				{
					// the creator: as given, else the card whose effect is running (live: the effect's source)
					int creatorId = playable[GameTag.CREATOR] != 0 ? playable[GameTag.CREATOR]
						: playable[GameTag.DISPLAYED_CREATOR] != 0 ? playable[GameTag.DISPLAYED_CREATOR]
						: c.Game.PowerHistory.CurrentEffectSource;
					var data = ((Entity)playable)._data;
					c.HandZone.Add(playable, position);
					bool spawnFx = false;
					if (creatorId != 0 && creatorId != playable.Id && c.Game.IdEntityDic.TryGetValue(creatorId, out IPlayable creator))
					{
						// live: the card flies in from its creator inside the creator's SpawnToHand SUB_SPELL — a server-picked
						// ReuseFX per card (RecordedSubSpells.tsv; Low Security Wing's is the plain flight). Cards live sends
						// without one (Sludge Slurper, Lorewalker Cho, Mukla) get none here either.
						// (one effect's cards share it, they arrive at once: Game.JoinSpawnGroup)
						spawnFx = c.Game.JoinSpawnGroup(creator, Loader.SubSpells.Find(creator.Card.Id, Loader.SubSpells.Role.SpawnToHand) ?? PlainSpawnToHand(c.Game, creator), playable.Id);
						data[GameTag.CREATOR] = creatorId;
						if (playable[GameTag.DISPLAYED_CREATOR] == 0)
							data[GameTag.DISPLAYED_CREATOR] = creatorId;
						data[GameTag.CREATOR_DBID] = creator.Card.AssetId;
						data[(GameTag)1556] = 1; // SPAWN_TIME_COUNT
					}
					if (playable[(GameTag)273] == 0)
						data[(GameTag)273] = 1; // NUM_TURNS_IN_HAND
					if (!c.Game.PowerHistory.PlaceCreatedInHand(playable.Id, c.Game.HistoryMoveFloor(), () => PowerHistoryBuilder.FullEntity(playable)))
						c.Game.PowerHistory.Add(PowerHistoryBuilder.ShowEntity(playable));
				}
				else
				{
					// add draw block show entity
					if (playable != null && playable[HsTag.NUM_TURNS_IN_HAND] == 0)
						((Entity)playable)._data[HsTag.NUM_TURNS_IN_HAND] = 1; // HsServer: live, a card enters the hand at 1
					// HsServer (2026-10-09): only a card coming from somewhere the client couldn't see is revealed — a minion bounced
					// from the board (Freezing Trap) or a card back from the graveyard is public: live only moves it. Ours re-sent it as
					// a SHOW_ENTITY still "in play at position 0" before the move, and the client lost track of the board (the user's other
					// minions stopped lighting up as attackers)
					if (c.Game.History && playable != null && playable[GameTag.ZONE] is not ((int)Zone.PLAY or (int)Zone.GRAVEYARD))
						c.Game.PowerHistory.Add(PowerHistoryBuilder.ShowEntity(playable));
					// HsServer: the mulligan's replacement takes the returned card's slot (HandZone.Replace, live)
					if (replacing != null && replacing.Zone == c.HandZone) c.HandZone.Replace(replacing, playable);
					else c.HandZone.Add(playable, position);
				}
				if (playable != null) // HsServer: Quickdraw — the turn it entered the hand (the starting hand: turn 0)
					((Entity)playable)._data[HsTag.ADDED_TO_HAND_TURN] = c.Game.Turn;
				if (playable != null) // HsServer: "… while holding this" — the plays before it entered the hand (Commander Sivara)
				{
					((Entity)playable)._data[HsTag.HELD_SINCE_PLAY] = c.PlayHistory.Count;
					if (playable.Card.Power?.Trigger?.CountsWhileHeld == true && playable[GameTag.TAG_SCRIPT_DATA_NUM_1] != 0)
						playable[GameTag.TAG_SCRIPT_DATA_NUM_1] = 0; // its count starts over
				}
				if (playable?.Zone?.Type == Zone.HAND) // HsServer: "added to your hand this game" (Wildpaw Gnoll)
					c.AddedToHandThisGame.Add(playable.Card);
				if (playable?.Zone?.Type == Zone.HAND) // HsServer: a ranked spell entering the hand takes its rank (live)
					RankUp(c, playable);
				if (playable?.Zone?.Type == Zone.HAND) // HsServer: a Remixed card entering the hand takes a version (Festival.cs)
					RemixOnEnteringHand(c, playable);
				if (playable?.Zone?.Type == Zone.HAND && PrintedTemporary(playable.Card)) // HsServer (ISLAND_VACATION): Deadline
					MakeTemporary(playable, playable);
				if (c.Game.TriggerManager.HasAddToHandTrigger) // HsServer: "after a card is added to your hand" triggers
				{
					c.Game.TaskQueue.StartEvent();
					c.Game.TriggerManager.OnAddToHandTrigger(playable);
					c.Game.ProcessTasks();
					c.Game.TaskQueue.EndEvent();
				}

				return true;
			}

		public static Func<Controller, IPlayable, bool> DiscardBlock
			=> delegate (Controller c, IPlayable playable)
			{
				c.Game.TaskQueue.StartEvent();
				c.Game.TriggerManager.OnDiscardTrigger(playable);
				IPlayable discard = c.HandZone.Remove(playable);
				c.Game.Log(LogLevel.INFO, BlockType.PLAY, "DiscardBlock", !c.Game.Logging ? "" : $"{discard} is beeing discarded.");
				c.GraveyardZone.Add(discard);

				c.DiscardedEntities.Add(discard.Id);
				c.LastCardDiscarded = discard.Id;
				c.Game.ProcessTasks();
				c.Game.TaskQueue.EndEvent();
				AnywhereDeathrattle(discard); // HsServer (VIOLET_HOLD): IMPFERNAL! — "(Also triggers in hand or deck.)"
				return true;
			};

		/// <summary>HsServer: the Joust reveal of the two deck cards (either may be null): both shown, META JOUST, hidden again
		/// (JoustBlock; WILD_WEST's Duel draw, Generic.DuelDraw).
		/// (2026-10-09) Live shows a Joust with NEW entities, never the deck cards themselves (live session-20261007-113244, Raven
		/// Familiar's JOUST block: FULL_ENTITY of two new hidden entities in DECK, a SHOW_ENTITY of each with REVEALED 1, META JOUST
		/// Data = the first, Info = both, then each HIDE_ENTITY to DECK, REVEALED 0, to SETASIDE — RevealFromDeck's shape, for both
		/// decks). Ours re-sent the deck cards' own FULL_ENTITY — an entity the client already had (soak client view, Gadgetzan
		/// Jouster) — and showed the real deck cards.</summary>
		public static void JoustReveal(Game game, IPlayable card, IPlayable cardOp)
		{
			if (!game.History || card == null && cardOp == null) return;
			var shown = new List<IPlayable>();
			foreach (IPlayable p in new[] { card, cardOp })
				if (p != null)
					shown.Add(Entity.FromCard(p.Controller, p.Card, new EntityData { { GameTag.ZONE, (int)Zone.DECK } }));
			var info = new List<int>();
			foreach (IPlayable s in shown)
			{
				s.NativeTags[GameTag.REVEALED] = 1;
				game.PowerHistory.Add(PowerHistoryBuilder.ShowEntity(s));
				info.Add(s.Id);
			}
			game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.JOUST, Data = info[0], Info = info });
			foreach (IPlayable s in shown)
			{
				game.PowerHistory.Add(new PowerHistoryHideEntity { EntityID = s.Id, Zone = Zone.DECK });
				s[GameTag.REVEALED] = 0;
				s.Controller.SetasideZone.Add(s);
			}
		}

		public static Func<Controller, CardType, IPlayable> JoustBlock
			=> delegate (Controller c, CardType type)
			{
				IPlayable card, cardOp;
				{
					var rnd = c.Game.Random;
					var span = c.DeckZone.GetSpan();
					Span<int> buffer = stackalloc int[Math.Max(c.DeckZone.Count, c.Opponent.DeckZone.Count)];
					int k = 0;
					for (int i = 0; i < span.Length; i++)
					{
						if (span[i].Card.Type != type) continue;
						buffer[k++] = i;
					}
					card = k == 0 ? null : span[buffer[rnd.Next(k)]]; // HsServer: the matching card (upstream: span[rnd.Next(k)])

					span = c.Opponent.DeckZone.GetSpan();
					k = 0;
					for (int i = 0; i < span.Length; i++)
					{
						if (span[i].Card.Type != type) continue;
						buffer[k++] = i;
					}
					cardOp = k == 0 ? null : span[buffer[rnd.Next(k)]];
				}

				JoustReveal(c.Game, card, cardOp);

				bool success = (card?.Cost ?? -1) > (cardOp?.Cost ?? -1);
				c.Game.Log(LogLevel.INFO, BlockType.JOUST, "JoustBlock", !c.Game.Logging ? "" : $"{c.Name} initiatets joust with {card} {card?.Cost} vs. {cardOp?.Cost} {cardOp}, {(success ? "Won" : "Loose")} the joust.");

				// TODO shuffle deck .... or ... just let it be?
				return success ? card : null;
			};

		/// <summary>Client player tags (live): shuffle instances into this deck (4103) / by this player into its own deck (4307).</summary>
		public const GameTag SHUFFLED_INTO_DECK = (GameTag)4103, SHUFFLED_INTO_OWN_DECK = (GameTag)4307;

		public static Func<Controller, IEntity, IPlayable, bool> ShuffleIntoDeck
			=> delegate (Controller c, IEntity sender, IPlayable playable)
			{
				if (c.DeckZone.IsFull)
				{
					c.Game.Log(LogLevel.INFO, BlockType.PLAY, "ShuffleIntoDeck",
						!c.Game.Logging ? "" : "Can't add a card to full deck.");
					return false;
				}

				c.Game.Log(LogLevel.INFO, BlockType.PLAY, "ShuffleIntoDeck", !c.Game.Logging ? "" : $"adding to deck {playable}.");

				// don't activate powers when shuffling cards back into the deck
				//c.DeckZone.Add(playable, c.DeckZone.Count == 0 ? -1 : Util.Random.Next(c.DeckZone.Count + 1));
				c.DeckZone.AddAtRandomPosition(playable);
				// HsServer: wiki Shuffle into deck — "When a card is shuffled into the deck, the entire deck is shuffled" (a card
				// put on top / bottom before — Dredge, Imp Gang Stooge — loses its place; user 2026-10-07). Not Trade: "it does
				// not shuffle the deck" (wiki Tradeable).
				c.DeckZone.Shuffle();

				// HsServer (LOST_CITY): "times you've shuffled cards into your deck" counts each shuffle INSTANCE, not each card (wiki
				// Lie in Wait: "Academic Espionage would count as 1 instance … not 10"); live client player tags 4103 (cards were
				// shuffled into this deck, by anyone) and 4307 (this player shuffled cards into its own deck): one effect's
				// shuffle = the same move, task and sender
				{
					Game g = c.Game;
					int senderId = sender?.Id ?? 0;
					if (c[HsTag.SHUFFLE_INSTANCE_MOVE] != g.MoveNumber || c[HsTag.SHUFFLE_INSTANCE_TASK] != g.TasksThisMove ||
					    c[HsTag.SHUFFLE_INSTANCE_SENDER] != senderId)
					{
						c[HsTag.SHUFFLE_INSTANCE_MOVE] = g.MoveNumber;
						c[HsTag.SHUFFLE_INSTANCE_TASK] = g.TasksThisMove;
						c[HsTag.SHUFFLE_INSTANCE_SENDER] = senderId;
						c[SHUFFLED_INTO_DECK] = c[SHUFFLED_INTO_DECK] + 1;
						if (sender?.Controller == c) c[SHUFFLED_INTO_OWN_DECK] = c[SHUFFLED_INTO_OWN_DECK] + 1;
					}
				}

				if (sender is IPlayable p && c.Game.TriggerManager.HasShuffleIntoDeckTrigger)
				{
					EventMetaData temp = c.Game.CurrentEventData;

					c.Game.CurrentEventData = new EventMetaData(p, playable);

					c.Game.RaiseEvent(() => c.Game.TriggerManager.OnShuffleIntoDeckTrigger(playable));

					c.Game.CurrentEventData = temp;
				}

				// add hide entity 
				if (c.Game.History)
				{
					c.Game.PowerHistory.Add(PowerHistoryBuilder.HideEntity(playable));
					// HsServer (2026-10-08): live sends ONE SHUFFLE_DECK per shuffle, after all its cards (and the player's 4103 / 4307), at the
					// block's end (live-20261001-1624 7497: three cards, one SHUFFLE_DECK) — Game.NoteShuffle
					c.Game.NoteShuffle(c.PlayerId);
				}

				return true;
			};

		public static Func<Controller, Card, Minion, bool> TransformBlock
			=> delegate (Controller c, Card card, Minion oldMinion)
			{
				if (oldMinion.Zone?.Type != Zone.PLAY)
					return false;
				// HsServer: Baroness Vashj summons instead and stays in the fight (wiki patch 36.4.0.250339: "Fixed a bug where Mystic
				// Misdirection could cancel Baroness Vashj's attack"): out of combat only when it really transforms
				if (!(card.Type == CardType.MINION && !oldMinion.IsSilenced && oldMinion.Card.Power?.SummonInsteadOfTransform == true))
					ExitCombat(oldMinion);

				// HsServer: live transforms a minion in place — the same entity gets a CHANGE_ENTITY (Hex, Evolve, Explosive
				// Evolution captures), a fresh minion of the new card (rules E9, ChangeEntityBlock). Replacing it with a new
				// entity made the client remove it and summon another (a "resummon" instead of a transform).
				if (card.Type == CardType.MINION)
				{
					// live (Evolve, Primordial Wave, Hex): the transformed minion's summon voice is muted around the change
					oldMinion[HsTag.SUPPRESS_ALL_SUMMON_VO] = 1;
					ChangeEntityBlock(c, oldMinion, card, true);
					// the fresh minion has no such tag any more: tell the client explicitly
					if (c.Game.History)
						c.Game.PowerHistory.AddUnchecked(PowerHistoryBuilder.TagChange(oldMinion.Id, HsTag.SUPPRESS_ALL_SUMMON_VO, 0));
					c.Game.Log(LogLevel.INFO, BlockType.PLAY, "TransformBlock", !c.Game.Logging ? "" : $"{oldMinion} got transformed into {card}.");
					return true;
				}
				return ReplaceMinionBlock(c, card, oldMinion);
			};

		/// <summary>
		/// Upstream's replace-with-a-new-entity transform, kept only for the upstream scripts that opt into it
		/// (TransformTask useOldMechanism: The Darkness turns itself dormant mid-play and keeps its own Battlecry).
		/// </summary>
		public static Func<Controller, Card, Minion, bool> ReplaceMinionBlock
			=> delegate (Controller c, Card card, Minion oldMinion)
			{
				if (oldMinion.Zone?.Type != Zone.PLAY)
					return false;

				if (!(Entity.FromCard(c, card) is Minion newMinion))
				{
					c.Game.Log(LogLevel.WARNING, BlockType.PLAY, "TransformBlock", !c.Game.Logging ? "" : $"missing final tranformation.");
					return false;
				}

				//oldMinion[GameTag.LINKED_ENTITY] = newMinion.Id;
				//newMinion[GameTag.LINKED_ENTITY] = oldMinion.Id;
				if (c.Game.CurrentEventData?.EventSource == oldMinion)
					c.Game.CurrentEventData.EventSource = newMinion;

				c.BoardZone.Replace(oldMinion, newMinion);

				c.Game.Log(LogLevel.INFO, BlockType.PLAY, "TransformBlock", !c.Game.Logging ? "" : $"{oldMinion} got transformed into {newMinion}.");
				return true;
			};

		/// <summary>
		/// Creates a new <see cref="Enchantment"/> and attaches it to the given target.
		/// </summary>
		/// <param name="g">The game context.</param>
		/// <param name="enchantmentCard">The base Card for the enchantment.</param>
		/// <param name="creator">The creator entity.</param>
		/// <param name="target">The target entity.</param>
		/// <param name="num1">ScriptTag1</param>
		/// <param name="num2">ScriptTag2</param>
		/// <param name="entityId">The entity ID to be stored in the enchantment. (e.g. carnivorous Cube)</param>
		/// <param name="owner">HsServer (2026-10-09): the player the enchantment belongs to (its triggers' "friendly" and "your turn"), when
		/// not the creator's — a game-wide effect that gives each player their own copy (a Dalaran Heist anomaly's "… Player
		/// Enchantment" on each player: Generic.GiveEachPlayer).</param>
		public static void AddEnchantmentBlock(in Game g, in Card enchantmentCard, in IPlayable creator, IEntity target, int num1 = -1, int num2 = -1, int entityId = 0,
			Controller owner = null)
		{
			// HsServer (WONDERS): a reprint gives its own twin of its original's enchantment (WON_009 Addled Grizzly: WON_009e for
			// OG_313e; Loader.ReprintCards.TwinEnchantment), the same card otherwise
			if (creator != null && Loader.ReprintCards.TwinEnchantment(creator.Card.Id, enchantmentCard.Id) is string twinId
			    && Cards.TryFromId(twinId) is Card twinCard && twinCard.Power != null)
			{
				AddEnchantmentBlock(g, twinCard, creator, target, num1, num2, entityId, owner);
				return;
			}
			Power power = enchantmentCard.Power; // HsServer: null for a plain marker (a Dormant counter: Actions/Dormant.cs)

			if (power?.Enchant is OngoingEnchant &&
			    target is IPlayable entity &&
			    entity.OngoingEffect is OngoingEnchant ongoingEnchant)
			{	// Increment the count of existing OngoingEnchant
				ongoingEnchant.Count++;
				return;
			}

			// HsServer: also a plain marker (a script looks for it: Immolate's "on fire") and one that captures a card (Nellie's
			// crew) — they must exist in games without history too
			if (g.History || power?.Aura != null || power?.Trigger != null || power?.DeathrattleTask != null || enchantmentCard.Modular ||
			    entityId > 0 || power != null && power.Enchant == null)
			{	// Create Enchantment instance Only when it is needed.
				// As an owner entity for Auras, Triggers or Deathrattle tasks.
				// We also maintain Modular (Magnetic) Enchantments for Kangor's Endless Army.
				Enchantment enchantment = Enchantment.GetInstance(owner ?? creator.Controller, in creator, in target, in enchantmentCard, num1, num2);

				power?.Aura?.Activate(enchantment);
				power?.Trigger?.Activate(enchantment);

				if (power?.Enchant?.RemoveWhenPlayed ?? false)
					Enchant.RemoveWhenPlayedTrigger.Activate(enchantment);

				if (entityId > 0)
				{
					enchantment.CapturedCard = g.IdEntityDic[entityId].Card;
					if (g.Logging)
						g.Log(LogLevel.DEBUG, BlockType.POWER,
							"AddEnchantmentBlock", $"{g.IdEntityDic[entityId]} is captured in {enchantment}.");
				}
			}

			//	no indicator enchantment entities when History option is off
			int atkBefore = target is Minion limb0 ? limb0.AttackDamage : 0, healthBefore = target is Minion limb1 ? limb1.BaseHealth : 0;
			power?.Enchant?.ActivateTo(target, num1, num2);
			// HsServer: live (Crusader Aura live-20261008-issues 13901, Watfin live-20261008-120539): after the buff's stat changes, the target's
			// 4741 and LAST_AFFECTED_BY = the enchantment's creator
			if (target is IPlayable buffed && creator != null) NoteAffected(buffed, creator);
			// HsServer (CATACLYSM): Wickerfang — "After one of Wickerfang's Legs gains stats, this gains them too"
			if (target is Minion leg && leg[ColossalLimb] > 0 && g.IdEntityDic.TryGetValue(leg[GameTag.CREATOR], out IPlayable bodyP) &&
			    bodyP is Minion body && body.Zone?.Type == Zone.PLAY && !body.IsSilenced && body.Card.Power?.CopiesLimbGains is string gains &&
			    Cards.TryFromId(gains) is Card gainsCard)
			{
				int dAtk = Math.Max(0, leg.AttackDamage - atkBefore), dHealth = Math.Max(0, leg.BaseHealth - healthBefore);
				if (dAtk > 0 || dHealth > 0) AddEnchantmentBlock(g, gainsCard, body, body, dAtk, dHealth, 0);
			}
		}

		/// <summary>HsServer: an amount enchantment already on its target grows by (<paramref name="add1"/>, <paramref name="add2"/>)
		/// instead of a second one (live Eater of Secrets: one OG_254e, its minion's Attack / Health up per Secret). Its
		/// TAG_SCRIPT_DATA_NUM_1 / _2 follow (the client's "+N/+N").</summary>
		public static void GrowEnchantment(Enchantment enchantment, int add1, int add2)
		{
			Enchant enchant = enchantment?.Card.Power?.Enchant;
			if (enchant == null || enchantment.Target == null) return;
			enchant.ActivateTo(enchantment.Target, add1, add2);
			if (enchantment[GameTag.TAG_SCRIPT_DATA_NUM_1] > 0) enchantment[GameTag.TAG_SCRIPT_DATA_NUM_1] += add1;
			if (enchantment[GameTag.TAG_SCRIPT_DATA_NUM_2] > 0) enchantment[GameTag.TAG_SCRIPT_DATA_NUM_2] += add2;
		}

		/// <summary>
		/// HsServer: a card leaves its owner's hand without being discarded ("Replace your hand": Harth Stonebrew) — set
		/// aside, no discard triggers, no graveyard. The one action for it (never HandZone.Remove + SetasideZone.Add in a script).
		/// </summary>
		/// <summary>HsServer: "summon a minion from your hand" (Beastmaster Leoroxx, Fel Summoner, Parachute Brigand, Dragon
		/// Roar): the same entity moves hand → play (a zone move keeps it) and is summoned (Blizzard placement from
		/// <paramref name="source"/>). Taking it out with RemoveFromHand first left it listed in set-aside (set soak).</summary>
		public static bool SummonFromHand(Controller c, Minion minion, IEntity source)
		{
			if (minion?.Zone?.Type != Zone.HAND || c.BoardZone.IsFull) return false;
			c.HandZone.Remove(minion);
			SummonBlock.Invoke(c.Game, minion, -1, source);
			return true;
		}

		/// <summary>
		/// HsServer: Corrupt (wiki: "activates when the user plays a card with a higher mana cost than the Corrupt card's cost
		/// while in hand"; "Corrupt triggers on the current cost of both cards"; "Corrupt cards keep any in-hand enchantments
		/// when they become Corrupted"). After <paramref name="played"/> is played from hand, every Corrupt card left in hand
		/// costing less than it was played for becomes its Corrupted version (an in-hand CHANGE_ENTITY, enchantments kept).
		/// </summary>
		/// Live (live-20261006, Pit Master): which cards will corrupt is fixed when the card is played (<see cref="MarkCorrupting"/>,
		/// right after the play is paid and counted: a Corrupt card drawn by the played card's effect isn't corrupted); at the end
		/// of the PLAY block, before Combo turns on, each one gets its own TRIGGER block (TriggerKeyword CORRUPT, effect index 0):
		/// TRANSFORMED_FROM_CARD = the old card, TARGET = itself, HAS_ACTIVATE_POWER 0, the CHANGE_ENTITY, HAS_ACTIVATE_POWER 1.
		public static void CorruptHand(Controller c, IPlayable played)
		{
			Game g = c.Game;
			foreach (IPlayable card in c.HandZone.GetAll())
			{
				if (card[HsTag.CORRUPTING] == 0) continue;
				card[HsTag.CORRUPTING] = 0;
				Card version = CorruptedVersion(card.Card);
				Tasks.ISimpleTask onCorrupt = card.Card.Power?.CorruptTask;
				if (g.History)
				{
					PowerHistoryBlockStart block = PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, card.Id, "", 0, 0);
					block.TriggerKeyword = (int)HsTag.CORRUPT;
					g.PowerHistory.Add(block);
				}
				card[GameTag.TRANSFORMED_FROM_CARD] = card.Card.AssetId;
				if (g.History)
					g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.TARGET, Data = 0, Info = new List<int> { card.Id } });
				IPlayable now = card;
				if (version != null && version != card.Card)
				{
					bool activatePower = HasActivatePowerInHand(card); // live: minions / Locations only (Festival.cs)
					if (activatePower) card[HasActivatePower] = 0;
					now = ChangeEntityBlock(c, card, version, false);
					if (activatePower) now[HasActivatePower] = 1;
				}
				onCorrupt?.Process(g, c, now, now);
				if (g.History)
					g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			}
		}

		/// <summary>HsServer: when a card is played, the Corrupt cards in hand costing less than it was played for will
		/// corrupt at the end of the play (wiki Corrupt: "Corrupt triggers on the current cost of both cards").</summary>
		public static void MarkCorrupting(Controller c, IPlayable played)
		{
			int paid = played[GameTag.TAG_LAST_KNOWN_COST_IN_HAND];
			foreach (IPlayable card in c.HandZone.GetAll())
				if (card[HsTag.CORRUPT] > 0 && card.Cost < paid)
					card[HsTag.CORRUPTING] = 1;
		}

		private const GameTag TimesBeenTransformed = (GameTag)2321, HasActivatePower = (GameTag)2840;

		/// <summary>HsServer: a Corrupt card's Corrupted version: its related card (client COLLECTION_RELATED_CARD_DATABASE_ID:
		/// Insight → Insight (Corrupted)); a Corrupted card that can Corrupt again has none, its next is "&lt;base&gt;t(N+1)"
		/// (Cascading Disaster t → t2); with no next it stays itself (Horrendous Growth: "Can be Corrupted endlessly").</summary>
		public static Card CorruptedVersion(Card card)
		{
			if (card[GameTag.COLLECTION_RELATED_CARD_DATABASE_ID] is int related && related > 0 &&
			    Model.Cards.FromAssetId(related) is Card version && version != card && version[HsTag.CORRUPTED_CARD] > 0)
				return version;
			// no related card: the version named after it ("<id>t" / "<id>a": Faire Arborist, Pit Master, Sweet Tooth)
			foreach (string suffix in new[] { "t", "a" }) // (Transfer Student's Darkmoon version is both: SCH_199t26 → t26t)
				if (Model.Cards.TryFromId(card.Id + suffix) is Card named && named[HsTag.CORRUPTED_CARD] > 0)
					return named;
			var m = System.Text.RegularExpressions.Regex.Match(card.Id, @"^(.*t)(\d*)$");
			if (m.Success && card[HsTag.CORRUPTED_CARD] > 0)
			{
				int n = m.Groups[2].Value.Length == 0 ? 1 : int.Parse(m.Groups[2].Value);
				if (Model.Cards.TryFromId(m.Groups[1].Value + (n + 1)) is Card next && next[HsTag.CORRUPTED_CARD] > 0) return next;
			}
			return card;
		}

		/// <summary>HsServer: minions in play change sides together, each into board slot <c>position</c> of the other player
		/// (Silas Darkmoon's rotation: the outermost minions switch control). All leave first, then all arrive, so two full
		/// boards can swap; as a control change (ControlTask) a minion with no room on the other side dies, and Charge / Rush
		/// keep it ready.</summary>
		public static void SwitchSides(IList<(Minion minion, int position)> moves)
		{
			var leaving = new List<(Minion minion, Controller to, int position)>();
			foreach ((Minion minion, int position) in moves)
				if (minion?.Zone?.Type == Zone.PLAY)
					leaving.Add((minion, minion.Controller.Opponent, position));
			if (leaving.Count == 0) return;
			var arriving = new List<(Minion minion, Controller to, int position)>();
			var free = new Dictionary<Controller, int>();
			foreach (var (minion, to, _) in leaving)
			{
				free[to] = to.BoardZone.FreeSpace;
				free[minion.Controller] = minion.Controller.BoardZone.FreeSpace;
			}
			foreach (var (minion, to, _) in leaving)
				free[minion.Controller]++; // the slot it leaves
			foreach (var (minion, to, position) in leaving)
			{
				if (free[to] <= 0) { FullZoneKill(minion); continue; }
				free[to]--;
				arriving.Add((minion, to, position));
			}
			foreach (var (minion, _, _) in arriving)
				minion.Zone.Remove(minion);
			if (arriving.Count == 0) return;
			arriving[0].minion.Game.AuraUpdate();
			foreach (var (moved, to, position) in arriving)
			{
				moved.Controller = to;
				moved[GameTag.CONTROLLER] = to.PlayerId;
				to.BoardZone.Add(moved, Math.Max(0, Math.Min(position, to.BoardZone.Count)));
				if (moved.HasCharge) moved.IsExhausted = false;
				else if (moved.IsRush) { moved.IsExhausted = false; moved.AttackableByRush = true; }
			}
		}

		public static void RemoveFromHand(Controller c, IPlayable card)
		{
			if (card?.Zone?.Type != Zone.HAND) return;
			c.SetasideZone.Add(c.HandZone.Remove(card));
		}

		/// <summary>
		/// HsServer: rules C1 / A5 — an entity removed from play (a replaced hero or Hero Power) can't trigger, be triggered
		/// or emit auras any more: its trigger and aura are switched off. Every hero / Hero Power replacement uses this.
		/// </summary>
		public static void RetireFromPlay(IPlayable p)
		{
			if (p == null)
				return;
			p.ActivatedTrigger?.Remove();
			p.OngoingEffect?.Remove();
		}

		/// <summary>
		/// HsServer (2026-10-08): <paramref name="runner"/> runs <paramref name="card"/>'s effect (its PowerTask) — "Repeat the last Battlecry
		/// you played" (Brilliant Macaw, Bolner), "Repeat all other Battlecries" (Shudderwock). Live (dungar-student 164101 20705, 31443):
		/// a hidden copy of the card, a shown copy (1715 = the runner, REVEALED, META SHOW_BIG_CARD), the effect in a POWER block on the
		/// runner at index 0 with EffectCardId = the card (the client plays that card's effects from it; ours: Game.RecordedFxAt reads it),
		/// then SLUSH_TIME 1000 on the shown copy and the hidden one leaves the game. <paramref name="inHistory"/>: the block's
		/// ShowInHistory (live: Macaw's 8 of 8; Shudderwock's 2 of 7). <paramref name="proxy"/>: the hidden copy when the caller made it
		/// (Shudderwock picks its random target with it).
		/// </summary>
		public static void RunCardEffect(Game g, Controller c, IPlayable runner, Card card, IPlayable target, bool inHistory, IPlayable proxy = null)
		{
			Tasks.ISimpleTask effect = card?.Power?.PowerTask;
			if (effect == null || runner == null) return;
			bool history = g.History;
			IPlayable shown = null;
			if (history)
			{
				proxy ??= Entity.FromCard(c, card, null, c.SetasideZone);
				shown = Entity.FromCard(c, card, null, c.SetasideZone);
				shown[(GameTag)1715] = runner.Id;
				shown[GameTag.REVEALED] = 1;
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SHOW_BIG_CARD, Data = 0, Info = new List<int> { shown.Id } });
			}
			g.TaskQueue.StartEvent();
			g.TaskQueue.Execute(effect, c, runner, target, 0, -1, 0, inHistory, card.Id);
			g.ProcessTasks();
			g.TaskQueue.EndEvent();
			if (history)
			{
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 1000, Info = new List<int> { shown.Id } });
				if (proxy?.Zone?.Type == Zone.SETASIDE)
				{
					proxy.Zone.Remove(proxy);
					proxy[GameTag.ZONE] = (int)Zone.REMOVEDFROMGAME;
				}
			}
		}

		/// <summary>
		/// HsServer (2026-10-08): two cards in hand become one (a Mixed Concoction, Sol'etos' halves). Live (heist Power.log 11032,
		/// darkmoon 50136): the arriving card is in the hand first; a TRIGGER block (index 0) on the WAITING card; both go to SETASIDE;
		/// the result is a NEW entity at the end of the hand (CREATOR / COPIED_FROM_ENTITY_ID = the waiting card; generated cards are new
		/// entities). <paramref name="displayedCreator"/>: it also says "Created by" the waiting card (Sol'etos). Returns the new card.
		/// </summary>
		public static IPlayable CombineInHand(Controller c, IPlayable waiting, IPlayable arriving, Card result, bool displayedCreator = false)
		{
			Game g = c.Game;
			if (g.History)
				g.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, waiting.Id, "", 0, 0));
			if (waiting[GameTag.TRANSFORMED_FROM_CARD] == 0) waiting[GameTag.TRANSFORMED_FROM_CARD] = waiting.Card.AssetId;
			foreach (IPlayable p in new[] { arriving, waiting })
				if (p != null && p.Zone?.Type != Zone.SETASIDE)
				{
					p.Zone?.Remove(p);
					c.SetasideZone.Add(p);
				}
			var tags = new EntityData { [GameTag.CREATOR] = waiting.Id, [HsTag.COPIED_FROM_ENTITY_ID] = waiting.Id };
			if (displayedCreator) tags[GameTag.DISPLAYED_CREATOR] = waiting.Id;
			IPlayable combined = Entity.FromCard(c, result, tags);
			AddHandPhase.Invoke(c, combined);
			if (g.History)
				g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			return combined;
		}

		public static Func<Controller, IPlayable, Card, bool, IPlayable> ChangeEntityBlock
			=> delegate (Controller c, IPlayable p, Card newCard, bool removeEnchantments)
			{
				c.Game.Log(LogLevel.VERBOSE, BlockType.TRIGGER, "ChangeEntityBlock",
					!c.Game.Logging ? "" : $"{p} is changed into {newCard}.");
				// HsServer (TITANS): a Titan's ability entities belong to its card: they go with the old card, new ones come
				// with a new Titan (after the change)
				int[] oldTitanAbilities = p.Card.IsTitan && p.Card != newCard ? TitanAbilityIds(p) : null;

				// HsServer: "If this would transform into a minion, summon that minion instead" (Baroness Vashj, Power.
				// SummonInsteadOfTransform): on the battlefield, an effect's transform into a minion summons it on its right and
				// it stays as it is
				if (removeEnchantments && p is Minion vashj && vashj.Zone?.Type == Zone.PLAY && !vashj.IsSilenced &&
				    vashj.Card.Power?.SummonInsteadOfTransform == true && newCard.Type == CardType.MINION)
				{
					if (!vashj.Controller.BoardZone.IsFull && Entity.FromCard(vashj.Controller, newCard) is Minion summoned)
						SummonBlock.Invoke(c.Game, summoned, vashj.ZonePosition + 1, vashj);
					return vashj;
				}

				// HsServer (EMERALD_DREAM): "If this would transform into a minion, it transforms into one that costs (N) more" (Plucky
				// Podling, Power.TransformCostsMore): on the battlefield, an effect's transform into a minion picks one costing N more
				// than that minion (the evolve rules from its Cost); live: its TRIGGER block (TRIGGER_VISUAL) around the change
				bool podling = false;
				if (removeEnchantments && p is Minion plucky && plucky.Zone?.Type == Zone.PLAY && !plucky.IsSilenced &&
				    plucky.Card.Power?.TransformCostsMore > 0 && newCard.Type == CardType.MINION &&
				    Cards.EvolvePickFromCost(c.Game.FormatType, newCard.Cost, plucky.Card.Power.TransformCostsMore, c.Game.Random, c, true) is Card bigger)
				{
					newCard = bigger;
					podling = c.Game.History;
					if (podling)
					{
						var block = Kettle.PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, plucky.Id, "", 0, 0);
						block.ShowInHistory = true;
						block.TriggerKeyword = (int)GameTag.TRIGGER_VISUAL;
						c.Game.PowerHistory.Add(block);
					}
				}
				try
				{
				// HsServer (2026-10-08): a minion resulting from a non-spell transformation on the battlefield counts as summoned for the
				// Summon Resolution Step ("whenever you summon": Starving Buzzard, Undertaker…) — rulebook :1446 "minions resulting from
				// non-spell transformations are counted as 'summoning a minion' … e.g. Faceless Manipulator, Recombobulator, Poultryizer,
				// Lady Naz'jar's Hero Power, Thrall, Deathseer's Battlecry and Hero Power, Sherazin"; :726 a spell's transform doesn't.
				// (Not an after-summon event: :1442 the Summon Event only.)
				bool onBoard = p is Minion && p.Zone?.Type == Zone.PLAY && newCard.Type == CardType.MINION && !(c.Game.TaskQueue.CurrentSource is Spell);
				IPlayable changed = ChangeEntityCore(c, p, newCard, removeEnchantments, oldTitanAbilities);
				if (onBoard && changed is Minion summoned && summoned.Zone?.Type == Zone.PLAY && !c.Game.SummonedMinions.Contains(summoned))
					c.Game.SummonedMinions.Add(summoned);
				return changed;
				}
				finally
				{
					if (podling) c.Game.PowerHistory.Add(Kettle.PowerHistoryBuilder.BlockEnd());
				}
			};

		/// <summary>HsServer: the entity's enchantments only add to <paramref name="tag"/> (at least one ADD, none SET): its cached value is
		/// the old card's plus those. A stat set without one (a Worgen's swap, Floop) is not.</summary>
		private static bool StatAddedByEnchantment(IPlayable p, GameTag tag)
		{
			bool added = false;
			foreach (Enchantment e in p.AppliedEnchantments ?? new List<Enchantment>())
				foreach (IEffect x in e.Card.Power?.Enchant?.Effects ?? System.Array.Empty<IEffect>())
				{
					EffectOperator? op = x is Effect fx && fx.Tag == tag ? fx.Operator : x is IStatEffect st && st.StatTag == tag ? st.Operator : (EffectOperator?)null;
					if (op == EffectOperator.SET) return false;
					if (op == EffectOperator.ADD) added = true;
				}
			return added;
		}

		private static IPlayable ChangeEntityCore(Controller c, IPlayable p, Card newCard, bool removeEnchantments, int[] oldTitanAbilities)
		{
			// HsServer (2026-10-08): live sends a 1069 pulse (the new card's dbf id, then 0) on the entity right before its CHANGE_ENTITY —
			// 690 of 712 CHANGE_ENTITYs in the captures (ranks, Blossoms, Starship upgrades, transforms)
			if (c.Game.History && !c.Game.SilentChanges && p.Card != newCard && newCard != null)
			{
				c.Game.PowerHistory.Add(PowerHistoryBuilder.TagChange(p.Id, (GameTag)1069, newCard.AssetId));
				c.Game.PowerHistory.Add(PowerHistoryBuilder.TagChange(p.Id, (GameTag)1069, 0));
			}
			{
				//if (!(p.Zone is HandZone hand))
				//	throw new InvalidOperationException($"{p} is not in Hand. ({p.Zone})");

				if (removeEnchantments)
				{
					// 12.0 Game Mechanics Update: Clear all applied enchantments when shifting
					if (p.AppliedEnchantments != null)
						for (int i = p.AppliedEnchantments.Count - 1; i >= 0; i--)
							p.AppliedEnchantments[i].Remove();

					if (p is Minion m)
						m.ResetAttributes();
					else
						((Playable) p).ResetCost();
				}

				p.ActivatedTrigger?.Remove();
				p.OngoingEffect?.Remove();

				HandZone hand = p.Zone as HandZone;
				BoardZone board = p.Zone as BoardZone;
				int id = p.Id;

				// Detach the target from Auras
				if (hand != null)
					hand.Auras.ForEach(a => a.DeApply(p));
				else if
					(board != null)
				{
					board.Auras.ForEach(a => a.DeApply(p));

					if (p is Minion { Untouchable: true })
					{
						board.DecrementUntouchablesCount();
					}
				}


				// HsServer: a card in hand or deck an effect transforms says "Created by" it (user 2026-10-06, Cera'thine
				// Fleetrunner: "it says created by on it"; the client's Created by line reads DISPLAYED_CREATOR). Upgrades of a card
				// (Corrupt, ranks, Forge: removeEnchantments false) and a card transforming itself don't.
				if (removeEnchantments && board == null && c.Game.History && c.Game.PowerHistory.CurrentEffectBlock is { } by
				    && by.Source != p.Id && by.Source > 3)
				{
					((Entity)p)._data[GameTag.CREATOR] = by.Source;
					((Entity)p)._data[GameTag.DISPLAYED_CREATOR] = by.Source;
				}

				if (removeEnchantments && board != null && p is Minion fresh && newCard.Type == CardType.MINION)
				{
					// HsServer: rules E9 — a board transform leaves a fresh minion. Live's CHANGE_ENTITY lists every tag
					// the old minion had plus the new card's, each with its new value.
					List<GameTag> had = fresh.BecomeFresh(newCard);
					// HsServer: live's CHANGE_ENTITY names what transformed it (Hex, Polymorph: CREATOR = the spell)
					if (c.Game.History && !c.Game.SilentChanges && c.Game.PowerHistory.CurrentEffectBlock is { } effect && effect.Source != p.Id && effect.Source > 3)
						((Entity)fresh)._data[GameTag.CREATOR] = effect.Source; // sent inside the CHANGE_ENTITY below
					if (c.Game.History) // (HsServer 2026-10-09: even when silent — Game.SilentChanges)
					{
						EntityData tags = new EntityData();
						foreach (GameTag t in had)
							if (Entity.InHistory(t))
								tags[t] = fresh[t];
						foreach (KeyValuePair<GameTag, int> item in newCard.Tags)
							if (Entity.InHistory(item.Key))
								tags[item.Key] = fresh[item.Key];
						foreach (GameTag t in new[] { GameTag.ENTITY_ID, GameTag.CONTROLLER, GameTag.ZONE, GameTag.ZONE_POSITION })
							tags.Remove(t);
						tags[(GameTag)2321] = fresh[(GameTag)2321]; // TIMES_BEEN_TRANSFORMED
						if (fresh[GameTag.CREATOR] > 0) tags[GameTag.CREATOR] = fresh[GameTag.CREATOR];

						c.Game.PowerHistory.Add(new PowerHistoryChangeEntity()
						{
							CardId = newCard.Id,
							Entity = new PowerHistoryEntity() { Id = p.Id, Name = "", Tags = tags }
						});
					}

					if (fresh._costManager != null)
						fresh._modifiedCost = fresh._costManager.EntityChanged(newCard.Cost);
				}
				else if (p.Card.Type == newCard.Type)
				{
					// HsServer: live — every CHANGE_ENTITY carries TIMES_BEEN_TRANSFORMED, counting up (live-20261006)
					((Entity)p)._data[TimesBeenTransformed] = p[TimesBeenTransformed] + 1;
					if (c.Game.History) // (HsServer 2026-10-09: even when silent — Game.SilentChanges)
					{
						EntityData differTags = new EntityData();
						Dictionary<GameTag, int> newTags = newCard.Tags;
						foreach (KeyValuePair<GameTag, int> item in p.Card.Tags)
							differTags.Add(item.Key,
								newTags.TryGetValue(item.Key, out int value)
									? value
									: 0);
						foreach (KeyValuePair<GameTag, int> item in newTags)
							if (!differTags.ContainsKey(item.Key))
								differTags.Add(item);
						differTags[TimesBeenTransformed] = p[TimesBeenTransformed];
						if (removeEnchantments && p[GameTag.DISPLAYED_CREATOR] > 0)
						{
							differTags[GameTag.CREATOR] = p[GameTag.CREATOR];
							differTags[GameTag.DISPLAYED_CREATOR] = p[GameTag.DISPLAYED_CREATOR];
						}

						c.Game.PowerHistory.Add(new PowerHistoryChangeEntity()
						{
							CardId = newCard.Id,
							Entity = new PowerHistoryEntity()
							{
								Id = p.Id,
								Name = "",
								Tags = differTags
							}
						});
					}


					// HsServer: the entity's own values of the old card's tags (cost, stats, keywords, possibly changed by
					// enchantments in hand) move by the difference between the two cards: a Forged Storm Giant (8 → 6) costs 6,
					// a +1/+1 in hand stays on top of the new stats (wiki Forge: in-hand enchantments are kept).
					Card oldCard = p.Card;
					var data = ((Entity)p)._data;
					foreach (GameTag t in System.Linq.Enumerable.ToList(System.Linq.Enumerable.Union(oldCard.Tags.Keys, newCard.Tags.Keys)))
					{
						if (t == GameTag.ENTITY_ID || t == GameTag.ZONE || t == GameTag.CONTROLLER || t == GameTag.ZONE_POSITION) continue;
						if (!data.TryGetValue(t, out int had)) continue;
						oldCard.Tags.TryGetValue(t, out int was);
						newCard.Tags.TryGetValue(t, out int now);
						if (was != now) data[t] = had + (now - was);
					}
					// a cached stat still at the old card's base value follows the new card (a Corrupted Horrendous Growth stayed
					// 2/2 instead of its version's 3/3). A stat an effect set (a Worgen's swap; without history it lives only in
					// the cache) keeps its value, as before.
					// HsServer (2026-11-14): a cached stat an enchantment ADDED to (a Heist run buff +1/+1 on Stonetalon Striker) moves by
					// the difference too — it stayed 4/4 as a 6/6 Dragon (user's game); only a stat an effect SET keeps its value.
					if (!removeEnchantments && p is Character ch)
					{
						if (ch._modifiedATK == oldCard.ATK) ch._modifiedATK = newCard.ATK;
						else if (ch._modifiedATK.HasValue && StatAddedByEnchantment(p, GameTag.ATK)) ch._modifiedATK += newCard.ATK - oldCard.ATK;
						if (ch._modifiedHealth == oldCard.Health) ch._modifiedHealth = newCard.Health;
						else if (ch._modifiedHealth.HasValue && StatAddedByEnchantment(p, GameTag.HEALTH)) ch._modifiedHealth += newCard.Health - oldCard.Health;
					}
					p.Card = newCard;
					if (p is Hero changedHero && changedHero.Controller?.Hero == changedHero)
						changedHero.Controller.HeroCardChanged(newCard); // the hero's class is the player's class
					Playable pp = (Playable)p;
					if (pp._costManager != null)
						pp._modifiedCost = pp._costManager.EntityChanged(newCard.Cost);
					else if (pp._modifiedCost.HasValue) // the cached cost (no cost effects): the same shift
						pp._modifiedCost += newCard.Cost - oldCard.Cost;
				}
				else
				{
					// HsServer: the new entity stays its owner's (c is whoever's effect it is: Shenanigans turned the
					// opponent's drawn minion into a Banana that was the Secret owner's, in the opponent's hand)
					Controller owner = p.Controller ?? c;
					((Entity)p)._data[TimesBeenTransformed] = p[TimesBeenTransformed] + 1;
					Playable entity;
					switch (newCard.Type)
					{
						case CardType.MINION:
							entity = new Minion(owner, newCard, p.NativeTags, id);
							break;
						case CardType.SPELL:
							entity = new Spell(owner, newCard, p.NativeTags, id);
							break;
						case CardType.HERO:
							entity = new Hero(owner, newCard, p.NativeTags, id);
							break;
						case CardType.WEAPON:
							entity = new Weapon(owner, newCard, p.NativeTags, id);
							break;
						case CardType.LOCATION: // HsServer
							entity = new Location(owner, newCard, p.NativeTags, id);
							break;
						default:
							throw new ArgumentNullException();
					}

					if (c.Game.History) // (HsServer 2026-10-09: even when silent — Game.SilentChanges)
					{
						EntityData differTags = new EntityData();
						Dictionary<GameTag, int> newTags = newCard.Tags;
						// HsServer: the old card's tags (was c.Card.Tags: the player's)
						foreach (KeyValuePair<GameTag, int> item in p.Card.Tags)
							differTags.Add(item.Key,
								newTags.TryGetValue(item.Key, out int value)
									? value
									: 0);
						foreach (KeyValuePair<GameTag, int> item in newTags)
							if (!differTags.ContainsKey(item.Key))
								differTags.Add(item);
						differTags[TimesBeenTransformed] = p[TimesBeenTransformed];
						if (removeEnchantments && p[GameTag.DISPLAYED_CREATOR] > 0)
						{
							differTags[GameTag.CREATOR] = p[GameTag.CREATOR];
							differTags[GameTag.DISPLAYED_CREATOR] = p[GameTag.DISPLAYED_CREATOR];
						}

						c.Game.PowerHistory.Add(new PowerHistoryChangeEntity()
						{
							CardId = newCard.Id,
							Entity = new PowerHistoryEntity()
							{
								Id = p.Id,
								Name = "",
								Tags = differTags
							}
						});
					}

					if (hand != null)
						hand.ChangeEntity(p, entity);
					else if
						(board != null)
						board.ChangeEntity((Minion)p, (Minion)entity);
					else if (p.Zone is DeckZone deck)
						deck.ChangeEntity(p, entity);

					c.Game.IdEntityDic[id] = entity;

					Playable pp = (Playable)p;
					if (pp._costManager != null)
						entity._modifiedCost = pp._costManager.EntityChanged(newCard.Cost);
					entity._costManager = pp._costManager;
					p = entity;
				}

				if (newCard.ChooseOne)
				{
					EntityData tags = null;
					if (c.Game.History && !c.Game.SilentChanges)
					{
						tags = new EntityData
						{
							{GameTag.CREATOR, id},
							{GameTag.PARENT_CARD, id}
						};
					}

					// HsServer: the new card's Choose One options from the card data (as Entity.FromCard)
					Card[] options = Cards.ChooseOneOptions(newCard);
					if (options == null)
						p.ChooseOnePlayables = null;
					else
					{
						p.ChooseOnePlayables = new IPlayable[options.Length];
						for (int i = 0; i < options.Length; i++)
							p.ChooseOnePlayables[i] = Entity.FromCard(c, options[i], tags, c.SetasideZone);
					}
				}

				// HsServer (TITANS): the old card's Titan abilities leave; a new Titan gets its own (Actions/Titans.cs)
				if (oldTitanAbilities != null)
				{
					RetireTitanAbilities(c.Game, oldTitanAbilities);
					foreach (GameTag t in new[] { HsTag.TITAN_ABILITY_ENTITY_1, HsTag.TITAN_ABILITY_ENTITY_2, HsTag.TITAN_ABILITY_ENTITY_3 })
						((Entity)p)._data[t] = 0;
				}
				if (newCard.IsTitan && (oldTitanAbilities != null || TitanAbility(p, 0) == null))
					CreateTitanAbilities(c, p);

				//switch (p.Zone.Type)
				//{
				//	case Zone.HAND:
				//		p.Power?.Trigger?.Activate(p, TriggerActivation.HAND);
				//		if (p.Power?.Aura is AdaptiveCostEffect e)
				//			e.Activate((Playable)p);
				//		break;
				//	case Zone.DECK:
				//		p.Power?.Trigger?.Activate(p, TriggerActivation.DECK);
				//		break;
				//	case Zone.PLAY:
				//		BoardZone.ActivateAura((Minion) p);
				//		break;
				//}


				// Reapply auras
				if (hand != null)
				{
					p.Power?.Trigger?.Activate(p, TriggerActivation.HAND);
					if (SabberStoneCore.Auras.MultiAura.CostEffect(p.Power?.Aura) is AdaptiveCostEffect e) // HsServer: also inside a MultiAura
						e.Activate((Playable) p);
					hand.Auras.ForEach(a => a.EntityAdded(p));
				}
				else if (board != null)
				{
					Minion m = (Minion)p;
					if (m.Controller == c.Game.CurrentPlayer)
					{
						if (!m.HasCharge)
						{
							if (m.IsRush)
							{
								m.IsExhausted = false;
								m.AttackableByRush = true;
								c.Game.RushMinions.Add(m.Id);
							}
							else
								m.IsExhausted = true;
						}
						else
							m.IsExhausted = false;
					}
					else
						m.IsExhausted = true;
					BoardZone.ActivateAura(m);
					board.Auras.ForEach(a => a.EntityAdded(p));
					board.AdjacentAuras.ForEach(a => a.BoardChanged = true);
				}
				else if (p.Zone?.Type == Zone.DECK)
				{
					p.Power?.Trigger?.Activate(p, TriggerActivation.DECK);
				}
				else if (p.Zone?.Type == Zone.HAND && p.ActivatedTrigger == null)
				{
					// HsServer: a card transformed in hand gets its new card's in-hand trigger (Lady Naz'jar keeps transforming)
					p.Power?.Trigger?.Activate(p, TriggerActivation.HAND);
				}
				else if (p is Weapon weapon && weapon.Controller?.Hero?.Weapon?.Id == weapon.Id)
				{
					// HsServer: an equipped weapon (no zone: the hero holds it) transformed in place (Suckerhook): its new
					// trigger and aura start working, as when it is equipped
					weapon.Card.Power?.Aura?.Activate(weapon);
					weapon.Card.Power?.Trigger?.Activate(weapon);
				}

				// Not sure C'Thun from Shifter Zerus will have Proxy's buffs

				// HsServer: transformed into a Colossal on the board: its appendages come too (wiki Colossal)
				if (removeEnchantments && p is Minion colossal && colossal.Zone?.Type == Zone.PLAY && newCard[Colossal] > 0)
					SummonAppendages(c.Game, colossal);

				return p;
			}
		}

		/// <param name="ownPowerBlock">HsServer: the card's own POWER block (effect index 0), sent when its effect opened none;
		/// it holds the Overload if any (see <see cref="PlayedPowerBlock"/>).</param>
		/// <summary>HsServer: how much a played card overloads: a Choose One card's the picked option's (Runic Carvings: only
		/// its Rush option overloads; both picked = the larger), any other card its own (Card.Overload).</summary>
		public static int OverloadOf(IPlayable source)
		{
			Card[] options = source.Card.ChooseOne ? Model.Cards.ChooseOneOptions(source.Card) : null;
			int most = 0;
			bool any = false;
			if (options != null)
				foreach (Card option in options)
					if (option.HasOverload)
					{
						any = true;
						most = Math.Max(most, option.Overload);
					}
			if (any)
			{
				int k = source[HsTag.CHOSEN_OPTION];
				return k >= 1 && k <= options.Length ? (options[k - 1].HasOverload ? options[k - 1].Overload : 0) : most;
			}
			return source.Card.HasOverload ? source.Card.Overload : 0;
		}

		public static void OverloadBlock(Controller controller, IPlayable source, bool history, bool ownPowerBlock = false)
		{
			// HsServer (VIOLET_HOLD): "Overload that player" (Disguised Detective; wiki: "whichever player controls it at that moment") — the
			// side it was played on
			if (source?.Card.Text?.Contains("<b>Overload</b> that player") == true && source.Controller != null) controller = source.Controller;
			int owed = OverloadOf(source);
			// HsServer (WILD_WEST): "Battlecry: …, don't Overload" (Minecart Cruiser: HsTag.SKIP_OWN_OVERLOAD) — live
			// (live-20261007-vfx-decks 70659-70700): the card's empty effect-index-1 POWER block still comes, with no OVERLOAD_OWED
			bool skipped = owed > 0 && source[HsTag.SKIP_OWN_OVERLOAD] > 0;
			if (skipped) ((Entity)source)._data[HsTag.SKIP_OWN_OVERLOAD] = 0;
			bool overload = owed > 0 && !skipped && !(source is Spell s && s.IsCountered);
			if (!overload && !ownPowerBlock && !skipped)
				return;

			if (history)
				controller.Game.PowerHistory.Add(
					PowerHistoryBuilder.BlockStart(BlockType.POWER, source.Id, "", ownPowerBlock ? 0 : 1, 0));

			// HsServer (WONDERS): through AddOverload — nothing while the player can't be Overloaded (Pebbly Page); then no
			// "whenever you Overload" triggers either (wiki Overload, the countered-spell case)
			int amount = overload ? AddOverload(controller, owed) : 0;
			if (history)
				controller.Game.PowerHistory.Add(
					PowerHistoryBuilder.BlockEnd());

			if (overload && amount > 0)
				controller.Game.RaiseEvent(() => controller.Game.TriggerManager.OnOverloadTrigger(source, amount));
		}

		/// <summary>HsServer: a played card's POWER block and its Overload, once its effect resolved (<paramref name="mark"/> =
		/// PowerHistory.Full.Count before it). Live (2026-10-05, every capture): a played minion always opens its POWER block
		/// (1787 of 1789; the 2 without were countered by Objection!), an empty one when it has no effect (no Battlecry, a
		/// Combo not active); the Overload comes after the effect in a POWER block of its own (effect index 1; 144 of 144),
		/// or inside the card's only POWER block when it has no effect (Earth Elemental, 27 of 27). After-play triggers come
		/// after it (Unbound Elemental, "After your opponent plays a minion" Secrets).</summary>
		public static void PlayedPowerBlock(Controller c, IPlayable card, int mark)
		{
			Game game = c.Game;
			bool opened = !game.History;
			for (int i = mark; !opened && i < game.PowerHistory.Full.Count; i++)
				opened = game.PowerHistory.Full[i] is PowerHistoryBlockStart b && b.BlockType == BlockType.POWER && b.Source == card.Id;
			OverloadBlock(c, card, game.History, ownPowerBlock: !opened);
		}

		/// <summary>
		/// HsServer: "Reveal a &lt;X&gt; in your deck" (Spire Security, Raven Familiar's family): a random matching deck card, which stays in the
		/// deck. Live (night 222041 10195, Raven Familiar): inside the card's POWER block a JOUST block: a NEW entity (hidden, ZONE DECK) shown
		/// as the card, META JOUST, hidden again, REVEALED 0, to SETASIDE, SLUSH_TIME 7000; nothing to reveal (11201, Deadly Arsenal): a
		/// REVEAL_CARD block Target -1 with only SLUSH_TIME 7000. Returns the deck card, or null.
		/// </summary>
		public static IPlayable RevealFromDeck(Controller c, IPlayable source, Func<IPlayable, bool> which)
		{
			Game game = c.Game;
			var matching = new List<IPlayable>();
			foreach (IPlayable p in c.DeckZone.GetAll()) if (which(p)) matching.Add(p);
			IPlayable card = matching.Count > 0 ? matching[game.Random.Next(matching.Count)] : null;
			if (!game.History) return card;
			game.PowerHistory.Add(PowerHistoryBuilder.BlockStart(card == null ? BlockType.REVEAL_CARD : BlockType.JOUST, source.Id, "", 0, card == null ? -1 : 0));
			if (card != null)
			{
				IPlayable shown = Entity.FromCard(c, card.Card, new EntityData { { GameTag.ZONE, (int)Zone.DECK } });
				shown.NativeTags[GameTag.REVEALED] = 1;
				game.PowerHistory.Add(PowerHistoryBuilder.ShowEntity(shown));
				game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.JOUST, Data = shown.Id, Info = new List<int> { shown.Id } });
				game.PowerHistory.Add(new PowerHistoryHideEntity { EntityID = shown.Id, Zone = Zone.DECK });
				shown[GameTag.REVEALED] = 0;
				c.SetasideZone.Add(shown);
			}
			game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 7000, Info = new List<int> { source.Id } });
			game.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			return card;
		}

		/// <summary>HsServer (WHIZBANG): client tags MINI (3319) / GIGANTIC (3400) on a Mini / Gigantic card, CREATED_BY_MINIATURIZE (3433) /
		/// CREATED_BY_GIGANTIFY (3434) on the new card (= the played one).</summary>
		public const GameTag MINI = (GameTag)3319, GIGANTIC = (GameTag)3400, CREATED_BY_MINIATURIZE = (GameTag)3433, CREATED_BY_GIGANTIFY = (GameTag)3434;

		// Work in progress
		public static void RevealCardBlock(IPlayable source, IPlayable target)
		{
			Game game = source.Game;
			if (!game.History) return;

			game.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.REVEAL_CARD, source.Id, "", 0, target.Id));
			target.NativeTags[GameTag.REVEALED] = 1;
			game.PowerHistory.Add(PowerHistoryBuilder.ShowEntity(target));
			game.PowerHistory.Add(PowerHistoryBuilder.HideEntity(target));
			target[GameTag.REVEALED] = 0;
			// ShowEntity with Zone = SETASIDE ????
		}

		// TODO: Posionous Block
		public static Func<bool, ICharacter, ICharacter, bool> PoisonousBlock
			=> delegate (bool history, ICharacter source, ICharacter target)
			{
				if (source[GameTag.POISONOUS] != 1)
					return false;

				if (history)
				{
					source.Game.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, source.Id, "", -1,
						0)); //	SubOption = -1, TriggerKeyWord = POISONOUS
							 //[DebugPrintPower] META_DATA - Meta=TARGET Data = 0 Info=1
							 //[DebugPrintPower] Info[0] = [entityName=Goldshire Footman id=47 zone=PLAY zonePos=1 cardId=CS1_042 player=2]
				}

				if (target is Playable poisoned)
					poisoned.DestroyedByEffect = true;
				target.ToBeDestroyed = true;


				if (history)
					source.Game.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());

				return true;
			};
	}
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
