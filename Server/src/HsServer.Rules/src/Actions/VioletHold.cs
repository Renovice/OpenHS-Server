// HsServer addition (not upstream SabberStone): Escape from Violet Hold (set 1988) shared pieces — summon N that attack, refresh the Hero
// Power, the Void, shuffling both hands, a Deathrattle that also triggers in hand or deck, hero damage turned into shuffled cards.
// Prepare: Actions/Prepare.cs. Tags: Enums/HsTag.cs 990750-990799.
using System;
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
		/// <summary>"Summon N X that attack it" (BEEEES!!!, Emergency Surgery): the summons first, then each one alive attacks the target in
		/// summon order until it is gone (live darkmoon 69906: deaths after each attack, the rest don't attack a dead target).</summary>
		public static List<Minion> SummonThatAttack(Controller c, IEntity source, Card card, int n, Minion target)
		{
			Game g = c.Game;
			var made = new List<Minion>();
			for (int i = 0; i < n && !c.BoardZone.IsFull && card != null; i++)
			{
				var m = (Minion)Entity.FromCard(c, card);
				SummonBlock.Invoke(g, m, -1, source);
				if (m.Zone?.Type == Zone.PLAY) made.Add(m);
			}
			EventMetaData temp = g.CurrentEventData;
			foreach (Minion m in made)
			{
				if (target == null || target.IsDead || target.ToBeDestroyed || target.Zone?.Type != Zone.PLAY) break;
				if (m.IsDead || m.ToBeDestroyed || m.Zone?.Type != Zone.PLAY) continue;
				AttackBlock.Invoke(c, m, target, true, true);
				g.DeathProcessingAndAuraUpdate();
			}
			g.CurrentEventData = temp;
			return made;
		}

		/// <summary>"Refresh your Hero Power" (Drink Blood): it can be used again this turn.</summary>
		public static void RefreshHeroPower(Controller c)
		{
			if (!(c.Hero?.HeroPower is HeroPower hp)) return;
			if (hp[GameTag.HEROPOWER_ACTIVATIONS_THIS_TURN] != 0) hp[GameTag.HEROPOWER_ACTIVATIONS_THIS_TURN] = 0;
			hp.IsExhausted = false;
		}

		// =========================================================================================================== the Void

		/// <summary>
		/// "Send your deck to the Void, except 1 card" (Irida Sinseeker; wiki: "removes the player's deck from the game"; "Cards generated
		/// from the Void are still considered to have started in the player's deck"). Live (dk-classxp 4472): the same entities go to
		/// SETASIDE (HsTag.IN_VOID marks them); the one left is random (wiki silent).
		/// </summary>
		public static void SendDeckToVoid(Controller c, int keep)
		{
			var deck = c.DeckZone.GetAll().ToList();
			var kept = deck.OrderBy(_ => c.Game.Random.Next()).Take(keep).Select(p => p.Id).ToHashSet();
			foreach (IPlayable card in deck.Where(p => !kept.Contains(p.Id)))
			{
				c.DeckZone.Remove(card);
				card[HsTag.IN_VOID] = 1;
				c.SetasideZone.Add(card);
			}
		}

		/// <summary>"At the start of your turns, get two cards from the Void" (JAIL_719e2; live dk-classxp 5343: the same entities to the
		/// hand; "Once the Void has been fully depleted, it will no longer generate cards"). A full hand burns them (doubt).</summary>
		public static void TakeFromVoid(Controller c, int n)
		{
			var voidCards = c.SetasideZone.GetAll().Where(p => p[HsTag.IN_VOID] > 0).ToList();
			for (int i = 0; i < n && voidCards.Count > 0; i++)
			{
				IPlayable card = voidCards[c.Game.Random.Next(voidCards.Count)];
				voidCards.Remove(card);
				card[HsTag.IN_VOID] = 0;
				c.SetasideZone.Remove(card);
				AddHandPhase.Invoke(c, card);
			}
		}

		/// <summary>
		/// "Shuffle both players' hands together" (Togwaggle, Smuggler King; wiki: "shuffles both hands together, then deals back a number of
		/// cards to each player equal to how many they had after this card was played"). Live (1417 6002): the same entities move (owner
		/// changes), enchantments kept (doubt); a hand-to-hand move, no "added to hand" triggers.
		/// </summary>
		public static void ShuffleHandsTogether(Game g)
		{
			Controller a = g.Player1, b = g.Player2;
			int na = a.HandZone.Count;
			var pool = a.HandZone.GetAll().Concat(b.HandZone.GetAll()).ToList();
			foreach (IPlayable card in pool) card.Zone.Remove(card);
			pool = pool.OrderBy(_ => g.Random.Next()).ToList();
			for (int i = 0; i < pool.Count; i++)
			{
				Controller to = i < na ? a : b;
				IPlayable card = pool[i];
				if (card.Controller != to) GiveTo(card, to, Zone.HAND);
				to.HandZone.Add(card);
			}
		}

		// =========================================================================================================== anywhere Deathrattle

		// =========================================================================================================== WHIZBANG shared rules

		/// <summary>The player gained Corpses: their hand cards with "(Gain @ Corpses to upgrade.)" (Power.CorpsesGainedTask) count them.</summary>
		internal static void CorpsesGained(Controller c, int amount)
		{
			Game g = c.Game;
			if (g == null) return;
			foreach (IPlayable card in c.HandZone.GetAll().Where(p => p.Card.Power?.CorpsesGainedTask != null).ToList())
				card.Card.Power.CorpsesGainedTask.Process(g, c, card, null, new TaskStack { Number = amount });
		}

		/// <summary>A Spellstone that counts DOWN (the Lesser Opal / Spinel: "@" = TAG_SCRIPT_DATA_NUM_1 left): <paramref name="n"/> less; at 0
		/// it becomes <paramref name="next"/> in hand (enchantments kept; the new one starts from its own data).</summary>
		public static void SpellstoneCountdown(IPlayable stone, int n, string next)
		{
			if (stone?.Zone?.Type != Zone.HAND || n <= 0 || !(Cards.TryFromId(next) is Card upgraded)) return;
			int left = Math.Max(0, stone[GameTag.TAG_SCRIPT_DATA_NUM_1] - n);
			stone[GameTag.TAG_SCRIPT_DATA_NUM_1] = left;
			if (left > 0) return;
			IPlayable changed = ChangeEntityBlock(stone.Controller, stone, upgraded, false);
			if (changed != null && changed[GameTag.TAG_SCRIPT_DATA_NUM_1] != upgraded[GameTag.TAG_SCRIPT_DATA_NUM_1])
				changed[GameTag.TAG_SCRIPT_DATA_NUM_1] = upgraded[GameTag.TAG_SCRIPT_DATA_NUM_1];
		}

		/// <summary>"Increase the duration of Auras in your hand, deck, and battlefield by N" (Cardboard Golem): every card with PALADIN_AURA
		/// (3429) there — its "Lasts @ turns" (TAG_SCRIPT_DATA_NUM_1) and its total (QUEST_PROGRESS_TOTAL) +N.</summary>
		public static void ExtendAuras(Controller c, int n)
		{
			foreach (IPlayable aura in c.HandZone.GetAll().Concat(c.DeckZone.GetAll()).Concat(c.SecretZone.GetAll()).Where(p => p.Card[HsTag.PALADIN_AURA] > 0).ToList())
			{
				aura[GameTag.TAG_SCRIPT_DATA_NUM_1] = aura[GameTag.TAG_SCRIPT_DATA_NUM_1] + n;
				aura[GameTag.QUEST_PROGRESS_TOTAL] = aura[GameTag.QUEST_PROGRESS_TOTAL] + n;
			}
		}

		/// <summary>"Take an extra turn" (Timewinder Zarimi): the player's NUM_TURNS_LEFT +1 and NUM_TURNS_LAST_AFFECTED_BY (3464) = the source —
		/// the client picks the extra-turn effect from it (EXTRA_TURNS_SPELL_OVERRIDE 3465; decomp Player.cs 1469).</summary>
		public static void GrantExtraTurn(Controller c, IEntity source)
		{
			if (c.Game.CurrentPlayer != c) return;
			c[(GameTag)3464] = source?.Id ?? 0;
			c.NumTurnsLeft++;
		}

		// =========================================================================================================== chunk 3 shared rules

		/// <summary>Client tags: "Is Reborn with full Health and enchantments" (2872, on Sinful Steed's card and set by Persisting Horror /
		/// Neigh!), an enchantment that gives that rule (4137: it goes when the minion comes back), COIN_CARD (2088: a Coin).</summary>
		public const GameTag FULL_HEALTH_REBORN = (GameTag)2872, GIVES_FULL_HEALTH_REBORN = (GameTag)4137; // (COIN_CARD: Actions/WildWest.cs)

		/// <summary>The set-aside copy of a "Reborn with full Health and enchantments" minion made as it died, ready to come back (live
		/// dk-classxp 34940-35029: its enchantments copied, DAMAGE 0, the rule's enchantment removed, HAS_BEEN_REBORN, CREATOR = the dead one).</summary>
		internal static void ReadyFullHealthReborn(Minion whole, Minion dead)
		{
			Game g = whole.Game;
			foreach (Enchantment e in whole.AppliedEnchantments?.Where(x => x.Card[GIVES_FULL_HEALTH_REBORN] > 0).ToList() ?? new List<Enchantment>())
				Tasks.SimpleTasks.RemoveEnchantmentTask.Task.Process(g, whole.Controller, e, null);
			if (whole.Damage != 0) whole.Damage = 0;
			whole[GameTag.CREATOR] = dead.Id;
			whole[(GameTag)1254] = dead.Id;
			whole[GameTag.HAS_BEEN_REBORN] = 1;
			whole[HsTag.REBORN_SUMMON] = 1;
		}

		/// <summary>A friendly minion came back through Reborn (the "were Reborn this game" list: Raith Van Geist; live darkmoon 53473: one
		/// resurrection per Reborn).</summary>
		internal static void NoteReborn(Controller c, Card card)
		{
			int n = c[HsTag.REBORN_THIS_GAME_COUNT];
			if (n < 20) c[HsTag.REBORN_THIS_GAME_BASE + n] = card.AssetId;
			c[HsTag.REBORN_THIS_GAME_COUNT] = n + 1;
		}

		/// <summary>The cards of the player's minions that were Reborn this game, in order (the first 20).</summary>
		public static List<Card> RebornThisGame(Controller c)
		{
			var list = new List<Card>();
			for (int i = 0; i < Math.Min(20, c[HsTag.REBORN_THIS_GAME_COUNT]); i++)
				if (Cards.FromAssetId(c[HsTag.REBORN_THIS_GAME_BASE + i]) is Card card) list.Add(card);
			return list;
		}

		/// <summary>"You always go second" in this player's deck (Power.AlwaysGoSecond: Aya, Lotus Kingpin).</summary>
		public static bool AlwaysGoesSecond(Controller c) => c.DeckZone.Any(p => p.Card.Power?.AlwaysGoSecond == true);

		/// <summary>"If your deck has no Neutral cards" (Vigilant Sentry, Scarlet Bruiser).</summary>
		public static bool NoNeutralInDeck(Controller c) => !c.DeckZone.Any(p => p.Card.HasClass(CardClass.NEUTRAL));

		/// <summary>A copy of one of the opponent's cards (Generic.Copy marks it; Azalina's copies too).</summary>
		public static bool CopiedFromOpponent(IPlayable p) => p != null && p[HsTag.COPIED_FROM_OPPONENT] > 0;

		/// <summary>The played card was paid with exactly 2 Mana (the Lotus cards; wiki: not paid with Health).</summary>
		public static bool PlayedFor2Mana(IPlayable p) => p != null && p[HsTag.PAID_MANA_PLUS1] == 3;

		/// <summary>The attacker was Stealthed as its attack began ("Whenever a friendly Stealthed minion attacks").</summary>
		public static bool AttackedStealthed(IEntity attacker) => attacker != null && attacker[HsTag.ATTACKED_STEALTHED] > 0;

		/// <summary>
		/// "Pick an upgraded counterfeit to replace your Coins this game" (Aya, Lotus Kingpin): a NEW Coin entering the hand becomes the
		/// player's counterfeit (HsTag.COUNTERFEIT_COIN) before the client sees it — every "get a Coin" effect at once. A Coin the client
		/// already knows (drawn, moved) stays.
		/// </summary>
		internal static IPlayable CounterfeitCoin(Controller c, IPlayable playable)
		{
			if (playable == null || c[HsTag.COUNTERFEIT_COIN] == 0 || playable.Zone != null || playable.Card[COIN_CARD] == 0 ||
			    playable.Card.AssetId == c[HsTag.COUNTERFEIT_COIN] || !(Cards.FromAssetId(c[HsTag.COUNTERFEIT_COIN]) is Card counterfeit))
				return playable;
			Game g = c.Game;
			if (g.History && !g.PowerHistory.DropUnsentCreation(playable.Id, g.HistoryMoveFloor())) return playable;
			var data = new EntityData();
			if (playable[GameTag.CREATOR] != 0) data[GameTag.CREATOR] = playable[GameTag.CREATOR];
			if (playable[GameTag.DISPLAYED_CREATOR] != 0) data[GameTag.DISPLAYED_CREATOR] = playable[GameTag.DISPLAYED_CREATOR];
			return Entity.FromCard(c, counterfeit, data);
		}

		/// <summary>
		/// "Give it this effect for a turn" (the Follow the X spells; live yogg-cthun 35231): the card gets the spell's one-turn enchantment
		/// (the visual) and, this turn, carries the spell (HsTag.FOLLOW_SPELL_A/B); the player gets "Following the X Player Enchant"
		/// (CAP_101e2), whose trigger runs the carried spells' effect after the card is played (RunFollowEffects) — that effect passes it on.
		/// </summary>
		public static void GiveFollowEffect(IEntity source, IPlayable card, Card spell, string enchantment)
		{
			if (card == null || spell == null) return;
			Game g = card.Game;
			IPlayable creator = source as IPlayable ?? card;
			if (Cards.TryFromId(enchantment) is Card e) AddEnchantmentBlock(g, e, creator, card, 0, 0, 0);
			if (card[HsTag.FOLLOW_TURN] != g.Turn)
			{
				card[HsTag.FOLLOW_SPELL_A] = 0;
				card[HsTag.FOLLOW_SPELL_B] = 0;
				card[HsTag.FOLLOW_TURN] = g.Turn;
			}
			if (card[HsTag.FOLLOW_SPELL_A] == 0 || card[HsTag.FOLLOW_SPELL_A] == spell.AssetId) card[HsTag.FOLLOW_SPELL_A] = spell.AssetId;
			else card[HsTag.FOLLOW_SPELL_B] = spell.AssetId;
			Controller c = card.Controller;
			if (c[HsTag.FOLLOW_PLAYER_TURN] != g.Turn && Cards.TryFromId("CAP_101e2") is Card player)
			{
				c[HsTag.FOLLOW_PLAYER_TURN] = g.Turn;
				AddEnchantmentBlock(g, player, creator, c, 0, 0, 0);
			}
		}

		/// <summary>After a card carrying a Follow the X effect is played this turn: each carried spell's effect, the card as its source.</summary>
		public static void RunFollowEffects(Controller c, IPlayable played)
		{
			Game g = c.Game;
			if (played == null || played[HsTag.FOLLOW_TURN] != g.Turn) return;
			int[] spells = { played[HsTag.FOLLOW_SPELL_A], played[HsTag.FOLLOW_SPELL_B] };
			played[HsTag.FOLLOW_SPELL_A] = 0;
			played[HsTag.FOLLOW_SPELL_B] = 0;
			foreach (int dbf in spells)
				if (dbf > 0 && Cards.FromAssetId(dbf)?.Power?.PowerTask is Tasks.ISimpleTask task)
				{
					g.TaskQueue.StartEvent();
					g.TaskQueue.Execute(task, c, played, null, 0, 0, 0);
					g.ProcessTasks();
					g.TaskQueue.EndEvent();
				}
		}

		/// <summary>An effect raised this card's Attack or Health: its "After this gains stats" (Power.ExtraOnStatGain) is due.</summary>
		internal static void NoteStatGain(IEntity entity)
		{
			if (!(entity is IPlayable p) || p.Card.Power?.ExtraOnStatGain == null || p[HsTag.STAT_GAIN_GUARD] > 0) return;
			p[HsTag.STATS_GAINED] = 1;
			p.Game.StatGainsPending = true;
		}

		/// <summary>
		/// "After this gains stats, gain an extra +1/+1 (wherever this is)" (Dalaran Champion; live 110138 3030: TRIGGER EffectIndex 5
		/// ShowInHistory with its enchantment): once the sequence settles, each card that gained stats gets its extra; that gain doesn't
		/// count again (STAT_GAIN_GUARD). Enchantment gains only (doubt: auras and set effects).
		/// </summary>
		public static void AfterStatGains(Game g)
		{
			if (!g.StatGainsPending) return;
			g.StatGainsPending = false;
			foreach (Controller c in new[] { g.Player1, g.Player2 })
				foreach (IPlayable p in c.HandZone.GetAll().Concat(c.DeckZone.GetAll()).Concat(c.BoardZone.GetAll()).Where(x => x[HsTag.STATS_GAINED] > 0).ToList())
				{
					p[HsTag.STATS_GAINED] = 0;
					if (!(Cards.TryFromId(p.Card.Power?.ExtraOnStatGain) is Card extra) || p is Minion m && m.Zone?.Type == Zone.PLAY && m.IsSilenced) continue;
					p[HsTag.STAT_GAIN_GUARD] = 1;
					if (g.History)
					{
						var block = PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, p.Id, "", 5, 0);
						block.ShowInHistory = true;
						g.PowerHistory.Add(block);
					}
					AddEnchantmentBlock(g, extra, p, p, 1, 1, 0);
					if (g.History) g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
					p[HsTag.STAT_GAIN_GUARD] = 0;
				}
		}

		/// <summary>Slime 'em!: the player's minions as they were slimed, left to right (Ectoplasm resummons its caster's: wiki).</summary>
		public static void RecordSlimed(Controller c)
		{
			var minions = c.BoardZone.GetAll().Where(m => !(m is Location)).ToList();
			c[HsTag.SLIMED_COUNT] = Math.Min(7, minions.Count);
			for (int i = 0; i < Math.Min(7, minions.Count); i++) c[HsTag.SLIMED_BASE + i] = minions[i].Card.AssetId;
		}

		/// <summary>The cards on the player's slime record.</summary>
		public static List<Card> Slimed(Controller c)
		{
			var list = new List<Card>();
			for (int i = 0; i < Math.Min(7, c[HsTag.SLIMED_COUNT]); i++)
				if (Cards.FromAssetId(c[HsTag.SLIMED_BASE + i]) is Card card) list.Add(card);
			return list;
		}

		// =========================================================================================================== chunk 4 shared rules

		/// <summary>The player's Cannoneers: its minions in play that have a Cannoneer's shot (Power.CannoneerShot), in order of play.</summary>
		public static List<Minion> Cannoneers(Controller c) =>
			c.BoardZone.GetAll().Where(m => m.Card.Power?.CannoneerShot != null && !m.IsSilenced).OrderBy(m => m.OrderOfPlay).ToList();

		/// <summary>"FIRE!": the Cannoneer's shot, once more per Captain Crowley (live S130515 46840: each shot its own effect, a kill resolved
		/// before the next shot picks among the survivors).</summary>
		public static void CannoneerFire(Minion cannoneer)
		{
			if (cannoneer?.Card.Power?.CannoneerShot is not Tasks.ISimpleTask shot) return;
			Game g = cannoneer.Game;
			Controller c = cannoneer.Controller;
			int shots = 1 + Math.Max(0, c.ControllerAuraEffects[HsTag.CANNONEER_EXTRA_SHOTS]);
			for (int i = 0; i < shots; i++)
			{
				if (cannoneer.Zone?.Type != Zone.PLAY || c.Opponent.Hero.IsDead) break;
				shot.Process(g, c, cannoneer, null);
				g.ProcessTasks();
				g.DeathProcessingAndAuraUpdate();
			}
		}

		/// <summary>Godfrey: a copy of the overdrawn card (its enchantments) set aside until there is room (live S131949 27646).</summary>
		internal static void SaveOverdrawn(Controller c, IPlayable burned)
		{
			IPlayable saved = Copy(c, burned, burned, Zone.SETASIDE, keepEnchantments: true);
			saved[HsTag.GODFREY_SAVED] = 1;
		}

		/// <summary>Godfrey's return (live S131949 27808: after a card is played, at the start of the owner's turn, at the end of each turn):
		/// saved cards, in random order, to the hand while there is room, each costing (1) less (wiki: "Returned cards appear in random
		/// order"; "Enchantments persist").</summary>
		public static void GodfreyReturn(Controller c, IEntity source)
		{
			Game g = c.Game;
			var saved = c.SetasideZone.GetAll().Where(p => p[HsTag.GODFREY_SAVED] > 0).OrderBy(_ => g.Random.Next()).ToList();
			foreach (IPlayable card in saved)
			{
				if (c.HandZone.IsFull) break;
				card[HsTag.GODFREY_SAVED] = 0;
				c.SetasideZone.Remove(card);
				if (AddHandPhase.Invoke(c, card) && Cards.TryFromId("GBL_003e") is Card less)
					AddEnchantmentBlock(g, less, source as IPlayable ?? card, card, 0, 0, 0);
			}
		}

		/// <summary>Warptooth: a friendly character took damage on its owner's turn; how many different ones this turn (wiki: "Four different
		/// friendly characters need to take damage").</summary>
		public static int NoteFriendlyDamaged(Controller c, ICharacter damaged)
		{
			Game g = c.Game;
			if (c[HsTag.WARPTOOTH_TURN] != g.Turn)
			{
				c[HsTag.WARPTOOTH_TURN] = g.Turn;
				c[HsTag.WARPTOOTH_COUNT] = 0;
			}
			int n = c[HsTag.WARPTOOTH_COUNT];
			for (int i = 0; i < Math.Min(n, 8); i++)
				if (c[HsTag.WARPTOOTH_BASE + i] == damaged.Id) return n;
			if (n < 8) c[HsTag.WARPTOOTH_BASE + n] = damaged.Id;
			c[HsTag.WARPTOOTH_COUNT] = n + 1;
			return n + 1;
		}

		/// <summary>
		/// A modular part of a Kazakus trial (Godfather Kazakus; live 174206 89836): a NEW spell of the part (CREATOR = the trial), shown in
		/// play, HISTORY_TARGET, SLUSH_TIME 3000, its own POWER block (ShowInHistory), then to the graveyard.
		/// </summary>
		public static void CastModularPart(Controller c, IPlayable trial, Card part)
		{
			if (part?.Power?.PowerTask is not Tasks.ISimpleTask task) return;
			Game g = c.Game;
			IPlayable spell = Entity.FromCard(c, part, new EntityData { { GameTag.CREATOR, trial.Id }, { GameTag.CREATOR_DBID, trial.Card.AssetId }, { (GameTag)1715, trial.Id } }, c.SetasideZone);
			if (g.History)
			{
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.HISTORY_TARGET, Data = 0, Info = new List<int> { spell.Id } });
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 3000, Info = new List<int> { spell.Id } });
				var block = PowerHistoryBuilder.BlockStart(BlockType.POWER, spell.Id, "", 0, 0);
				block.ShowInHistory = true;
				g.PowerHistory.Add(block);
			}
			g.TaskQueue.StartEvent();
			task.Process(g, c, spell, null);
			g.ProcessTasks();
			g.TaskQueue.EndEvent();
			if (g.History) g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			g.DeathProcessingAndAuraUpdate();
			if (spell.Zone?.Type == Zone.SETASIDE) c.GraveyardZone.Add(c.SetasideZone.Remove(spell));
		}

		/// <summary>"(Cast N spells to turn into a minion!)" — the minion: the card's COLLECTION_RELATED_CARD_DATABASE_ID (the client shows it on
		/// mouse-over).</summary>
		public static Card TurnsIntoMinion(Card card) =>
			Cards.FromAssetId(card[GameTag.COLLECTION_RELATED_CARD_DATABASE_ID]) is Card m && m.Type == CardType.MINION ? m : null;

		/// <summary>"(Also triggers in hand or deck.)" (IMPFERNAL!; wiki: it triggers when "Discarded", "Overdrawn", or destroyed in hand / deck
		/// by effects). Text marker on a Deathrattle card.</summary>
		public static bool DeathrattleAnywhere(Card card) => card?.Text?.Contains("Also triggers in hand or deck") == true;

		/// <summary>The Deathrattle of such a card leaving the hand or deck by a discard, an overdraw or a destroy (Generic.Destroy /
		/// DiscardBlock / AddHandPhaseAt's burn). Not on the board: "all other characters" = every character.</summary>
		internal static void AnywhereDeathrattle(IPlayable card)
		{
			if (!DeathrattleAnywhere(card?.Card) || !(card.Card.Power?.DeathrattleTask is Tasks.ISimpleTask task) || card.Controller == null) return;
			Game g = card.Game;
			g.TaskQueue.StartEvent();
			g.TaskQueue.Execute(task, card.Controller, card, null, 0, (int)GameTag.DEATHRATTLE, 0);
			g.ProcessTasks();
			g.TaskQueue.EndEvent();
		}
	}
}
