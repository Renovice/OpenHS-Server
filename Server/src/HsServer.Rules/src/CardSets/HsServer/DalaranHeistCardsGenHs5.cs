// HsServer addition (not upstream SabberStone): The Dalaran Heist — chunk 5: Bob's Tavern, the "Friendly Encounter" between bosses
// 3-4 and 5-6 (Bartender Bob DALA_BOSS_99h, his rare stand-in Bartendotron DALA_BOSS_98h, the tavern spells DALA_901..914 and their
// run enchantments). Spec and evidence: RESEARCH/heist-tavern.md (27 live tavern games: scratchpad tavern/, play_DALA_9xx_*.txt).
// Shared pieces: Actions/DalaranHeistGroup5.cs (the run deck and its edits, the PUZZLE_MODE start, squelched triggers); the server
// side (save data, the tavern's place in the run): Server/src/HsServer/Lobby/HeistTavern.cs. Every number goes through CardValues (V).
//
// wiki The Dalaran Heist (https://hearthstone.wiki.gg/wiki/The_Dalaran_Heist): "4 minions from the player's deck will be placed on the
// board, and 4 random minions will be placed on Bob's side of the board"; "the player has 2 Gold to spend in the tavern". wiki Bartender
// Bob (https://hearthstone.wiki.gg/wiki/Bartender_Bob): Bartendotron "only offers Mech minions".
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Actions;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.src.Loader;
using SabberStoneCore.Tasks;
using SabberStoneCore.Tasks.SimpleTasks;
using SabberStoneCore.Triggers;

namespace SabberStoneCore.CardSets.HsServer
{
	public static partial class CurrentCoreCardsGen
	{
		// (live AllOptions: the player's hero REQ_MINION_TARGET, Bob and his minions REQ_FRIENDLY_TARGET; DALA_901 the reverse)
		private static readonly Dictionary<PlayReq, int> TavernFriendlyMinion =
			new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 } };
		private static readonly Dictionary<PlayReq, int> TavernEnemyMinion =
			new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } };

		/// <summary>The hand Bob deals (27 live games: always 5 different spells): slot 1 one of the "take the enemy minions" cards, slot 2
		/// one of the "remove" cards, slots 3-5 three different others. Weights unknown (live: 903 and 912 came up more often) — even.</summary>
		private static readonly string[] TavernTakeSpells = { "DALA_901", "DALA_906" };
		private static readonly string[] TavernRemoveSpells = { "DALA_902", "DALA_909" };
		private static readonly string[] TavernOtherSpells =
			{ "DALA_903", "DALA_904", "DALA_905", "DALA_907", "DALA_908", "DALA_910", "DALA_911", "DALA_912", "DALA_913", "DALA_914" };

		/// <summary>Live MISSION_EVENT values (client DALA_Tavern): 101 = Bob's welcome (the hand is dealt), 102 = his goodbye.</summary>
		private const int TavernWelcomeEvent = 101, TavernGoodbyeEvent = 102;

		/// <summary>A minion put on the tavern's board: who placed it (1254), no Zzz (live NUM_TURNS_IN_PLAY 3, EXHAUSTED 0).</summary>
		private static void TavernSettle(Minion m, IEntity placedBy)
		{
			m[Generic.PLACED_BY] = placedBy.Id;
			m[GameTag.NUM_TURNS_IN_PLAY] = 3;
			m.IsExhausted = false;
		}

		/// <summary>
		/// Up to 4 random minions of <paramref name="player"/>'s deck onto the board (fewer when the deck has fewer: live 2 or 3 seen),
		/// to the right, unable to attack. Live (183021 L20900): SHOW_ENTITY into PLAY with 1254 and 1286, then CANT_ATTACK, a 750 ms pause
		/// each; Brood's (play_DALA_912): a TARGET each and 1000 ms (the last 1500).
		/// </summary>
		private static void TavernShowDeckMinions(Game g, Controller player, IPlayable source, bool brood)
		{
			var pool = player.DeckZone.OfType<Minion>().Where(m => Generic.IsMinionCard(m)).ToList();
			int n = System.Math.Min(V("DALA_BOSS_99h", "deck_minions", 4), pool.Count);
			for (int i = 0; i < n && !player.BoardZone.IsFull; i++)
			{
				Minion m = pool[g.Random.Next(pool.Count)];
				pool.Remove(m);
				Generic.RemoveFromZone.Invoke(player, m);
				Generic.SummonBlock.Invoke(g, m, -1, source);
				if (m.Zone?.Type != Zone.PLAY) continue;
				TavernSettle(m, source);
				m[GameTag.CANT_ATTACK] = 1;
				if (brood) Generic.NameTargets(g, new[] { m.Id });
				Generic.ArtificialPause(g, source, brood ? (i == n - 1 ? 1500 : 1000) : 750);
			}
		}

		/// <summary>
		/// 4 random minions for the tavern's host (<paramref name="host"/>): any set and class (live AT_009, RLK_593, ONY_004, TOY_960…),
		/// only Mechs for Bartendotron (its hero's TAVERN_MINION_RACE). Live: FULL_ENTITY in PLAY, CREATOR = the source, 1254; no pause
		/// (Brood's: a TARGET and 1000 ms each). The player's generation rules (user 2026-11-20): Bob offers only what random generation could give
		/// the PLAYER (Cards.DeckAllows: no Imbue / Herald / Invoke cards unless their starting deck — this tavern's — has one), whoever's
		/// effect summons them.
		/// </summary>
		private static void TavernHostMinions(Game g, Controller host, IPlayable source, bool brood)
		{
			int race = host.Hero[Generic.TAVERN_MINION_RACE];
			int n = V("DALA_BOSS_99h", "host_minions", 4);
			for (int i = 0; i < n && !host.BoardZone.IsFull; i++)
			{
				Card card = RandomBoardMinion(g, x => (race == 0 || x.IsRace((Race)race)) && Cards.DeckAllows(host.Opponent, x));
				if (card == null) break;
				var m = (Minion)Entity.FromCard(host, card, new EntityData
				{
					[GameTag.CREATOR] = source.Id, [GameTag.CREATOR_DBID] = source.Card.AssetId,
				});
				Generic.SummonBlock.Invoke(g, m, -1, source);
				if (m.Zone?.Type != Zone.PLAY) continue;
				TavernSettle(m, source);
				if (brood)
				{
					Generic.NameTargets(g, new[] { m.Id });
					Generic.ArtificialPause(g, source, 1000);
				}
			}
		}

		/// <summary>The 5 tavern spells (live 183021 L21180: FULL_ENTITY in HAND, CREATOR = Bob, COIN_MANA_GEM — a coin cost shows).</summary>
		private static void TavernDealHand(Game g, Controller player, IPlayable host)
		{
			var others = TavernOtherSpells.ToList();
			var hand = new List<string>
			{
				TavernTakeSpells[g.Random.Next(TavernTakeSpells.Length)],
				TavernRemoveSpells[g.Random.Next(TavernRemoveSpells.Length)],
			};
			for (int i = 0; i < V("DALA_BOSS_99h", "other_spells", 3) && others.Count > 0; i++)
			{
				string id = others[g.Random.Next(others.Count)];
				others.Remove(id);
				hand.Add(id);
			}
			foreach (string id in hand)
				if (Cards.TryFromId(id) is Card card && !player.HandZone.IsFull)
					Generic.AddHandPhase.Invoke(player, Entity.FromCard(player, card, new EntityData
					{
						[GameTag.CREATOR] = host.Id, [GameTag.CREATOR_DBID] = host.Card.AssetId, [GameTag.COIN_MANA_GEM] = 1,
					}));
		}

		/// <summary>
		/// Bob's (and Bartendotron's) three triggers. Live (183021 L20900-21252, L21950): index 0 at the player's first turn start — the
		/// tavern is set (the player's minions, the host's minions, 2 gold, the hand, MISSION_EVENT 101); index 1 cancels the player's
		/// turn draw at MAIN_START; index 2 at the host's own turn start — MISSION_EVENT 102 and the host destroys itself: the player
		/// always wins (DEATHS, PLAYSTATE WON).
		/// </summary>
		private static Trigger TavernHostTriggers(Race race) => new MultiTrigger(
			new Trigger(TriggerType.TURN_START)
			{
				EitherTurn = true, TriggerSource = TriggerSource.ENEMY, EffectIndex = 0,
				SingleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Hero host)) return;
					Controller player = c.Opponent;
					host[Generic.TAVERN_MINION_RACE] = (int)race;
					TavernShowDeckMinions(g, player, host, brood: false);
					TavernHostMinions(g, c, host, brood: false);
					player.BaseMana = V("DALA_BOSS_99h", "gold", 2);
					TavernDealHand(g, player, host);
					g[GameTag.MISSION_EVENT] = TavernWelcomeEvent;
					Generic.CancelTurnDraw(player, host, 1);
				}),
			},
			new Trigger(TriggerType.TURN_START)
			{
				EffectIndex = 2,
				SingleTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) => g[GameTag.MISSION_EVENT] = TavernGoodbyeEvent),
					new DestroyTask(EntityType.HERO),
					// (live, after the host's death: its stats shown again, both Hero Powers enabled — the game is over)
					new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Hero host && host[GameTag.HIDE_STATS] > 0) host[GameTag.HIDE_STATS] = 0;
						foreach (Controller p in new[] { c, c.Opponent })
							if (p[GameTag.HERO_POWER_DISABLED] > 0) p[GameTag.HERO_POWER_DISABLED] = 0;
					})),
			});

		/// <summary>The friendly minion a spell targets (null otherwise).</summary>
		private static Minion TavernTarget(Controller c, IPlayable t) => t is Minion m && m.Controller == c && m.Zone?.Type == Zone.PLAY ? m : null;

		/// <summary>A run enchantment given in this game (the buff shows) and recorded for the run.</summary>
		private static void TavernRunBuff(Game g, Controller c, IEntity source, IPlayable card, string enchantment)
		{
			if (card == null) return;
			Give(g, source, card, enchantment);
			Generic.AddRunEnchantment(c, card, enchantment);
		}

		/// <summary>New copies of <paramref name="card"/> shuffled into the deck, each joining the run deck (Recruit a Veteran, The Gang's
		/// All Here!; live: a new hidden DECK entity, 4103/4307, SHUFFLE_DECK). Copies of a deck card (<paramref name="original"/>) keep its run
		/// enchantments (user 2026-11-20).</summary>
		private static void TavernNewDeckCards(Controller c, IEntity source, Card card, int copies, IPlayable original = null)
		{
			for (int i = 0; i < copies && card != null && !c.DeckZone.IsFull; i++)
			{
				IPlayable copy = Entity.FromCard(c, card);
				Generic.AddToRunDeck(c, copy);
				if (original != null) Generic.CopyRunEnchantments(c, original, copy);
				Generic.ShuffleIntoDeck.Invoke(c, source, copy);
			}
		}

		static partial void HeistTavern(IDictionary<string, CardDef> cards)
		{
			// ------------------------------------------------------------------------------------------------ the hosts

			// [DALA_BOSS_99h] Bartender Bob — the tavern's host (scenarios 3236 / 3359 DALA_Tavern, Heroic); the server's scenario setup
			// gives the game its tags (PUZZLE_MODE, squelched triggers, Hero Powers disabled, the player's hero Immune).
			cards.Add("DALA_BOSS_99h", new CardDef(new Power { Trigger = TavernHostTriggers(Race.INVALID) }));

			// [DALA_BOSS_98h] Bartendotron — "On rare occasions … Bartendotron instead of Bartender Bob", a variant that "only offers Mech
			// minions" (wiki Bartender Bob); the server picks him.
			cards.Add("DALA_BOSS_98h", new CardDef(new Power { Trigger = TavernHostTriggers(Race.MECHANICAL) }));

			// ------------------------------------------------------------------------------------------------ the spells

			// [DALA_901] Recruit (1) — Choose an enemy minion. Add it to your Adventure Deck.
			cards.Add("DALA_901", new CardDef(TavernEnemyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m) Generic.RecruitToRunDeck(c, s, m);
				})
			}));

			// [DALA_902] Dismiss (0) — Choose a friendly minion. Remove it from your Adventure Deck.
			cards.Add("DALA_902", new CardDef(TavernFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.DismissFromRunDeck(c, s, TavernTarget(c, t)))
			}));

			// [DALA_903] Take a Chance (1) — Reduce the cost of a random card in your Adventure Deck to (0).
			// (live: any card of the run deck — a spell END_023 was picked, a minion on the board too — shown by REVEAL_CARD)
			cards.Add("DALA_903", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					List<IPlayable> run = Generic.RunDeckCards(c);
					if (run.Count == 0) return;
					IPlayable card = run[g.Random.Next(run.Count)];
					Generic.RevealRunDeckCard(c, (IPlayable)s, card, 0);
					TavernRunBuff(g, c, s, card, "DALA_903e");
				})
			}));
			cards.Add("DALA_903e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(V("DALA_903e", "cost", 0))) }));

			// [DALA_904] Good Food (1) — Increase your starting Health by 5. (live: only a TARGET on the player's hero — the recorded
			// block meta TARGETHERO@start gives it)
			cards.Add("DALA_904", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.AddRunStartingHealth(c, V("DALA_904", "health", 5)))
			}));

			// [DALA_905] Right Hand Man (1) — Choose a friendly minion. It always starts in your hand.
			// (live: only the TARGET — no enchantment entity in the tavern; the run's DALA_905e puts it in later opening hands)
			cards.Add("DALA_905", new CardDef(TavernFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (TavernTarget(c, t) is Minion m) Generic.AddRunEnchantment(c, m, "DALA_905e");
				})
			}));
			cards.Add("DALA_905e", new CardDef(new Power { Enchant = new Enchant(new Effect(Generic.STARTS_IN_HAND, EffectOperator.SET, 1)) }));

			// [DALA_906] Round of Drinks (1) — Add all enemy minions to your Adventure Deck. They gain +1/+1 for this run.
			// Live (play_DALA_906): POWER index 0 names every enemy minion; SLUSH_TIME 3000; POWER index 1 recruits them left to right,
			// each followed by its hidden DALA_906e (SecondPowerTask).
			cards.Add("DALA_906", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.NameTargets(g, c.Opponent.BoardZone.GetAll().Select(m => m.Id))),
				SecondPowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.Opponent.BoardZone.GetAll().ToList())
					{
						Generic.NameTargets(g, new[] { m.Id });
						if (Generic.RecruitToRunDeck(c, s, m)) TavernRunBuff(g, c, s, m, "DALA_906e");
					}
				})
			}));
			cards.Add("DALA_906e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DALA_906e", "stats", 1))) }));

			// [DALA_907] Recruit a Veteran (1) — Discover a Legendary minion. Add it to your Adventure Deck.
			// (live options: JAIL_407, VAC_959, LOE_076 — any class; the pick: a new hidden deck card)
			cards.Add("DALA_907", new CardDef(new Power
			{
				PowerTask = new DiscoverFromTask(
					(g, c, s) => Cards.GenerationPool(g, c).Where(x => x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY),
					afterPick: new CustomTask((g, c, s, picked, stack) =>
					{
						if (picked == null) return;
						TavernNewDeckCards(c, s, picked.Card, 1);
						Generic.NameTargets(g, new[] { picked.Id });
					}),
					action: ChoiceAction.STACK)
			}));

			// [DALA_908] Tell a Story (1) — Give a friendly minion +2/+2 for this run.
			cards.Add("DALA_908", new CardDef(TavernFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => TavernRunBuff(g, c, s, TavernTarget(c, t), "DALA_908e"))
			}));
			cards.Add("DALA_908e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DALA_908e", "stats", 2))) }));

			// [DALA_909] You're All Fired! (0) — Remove all friendly minions in play from your Adventure Deck.
			// (live: per minion left to right, a TARGET and the Dismiss)
			cards.Add("DALA_909", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().ToList())
					{
						Generic.NameTargets(g, new[] { m.Id });
						Generic.DismissFromRunDeck(c, s, m);
					}
				})
			}));

			// [DALA_910] The Gang's All Here! (1) — Choose a friendly minion. Add three new copies of it to your Adventure Deck.
			cards.Add("DALA_910", new CardDef(TavernFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (TavernTarget(c, t) is Minion m) TavernNewDeckCards(c, s, m.Card, V("DALA_910", "copies", 3), m);
				})
			}));

			// [DALA_911] Kindle (0) — Look at four spells from your Adventure Deck. Choose one to remove.
			// (live: copies of up to 4 deck spells offered; the picked copy and then the deck card itself go to the graveyard)
			cards.Add("DALA_911", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					Generic.DiscoverFromCards(c, s, Generic.PickFromDeck(c, p => p is Spell, V("DALA_911", "look", 4)), (g2, c2, s2, card) =>
					{
						Generic.RemoveFromRunDeck(c2, card);
						if (card.Zone?.Type == Zone.DECK) Generic.DeckCardToGraveyard(card, s2.Id, suppressAnimation: false);
					}))
			}));

			// [DALA_912] Brood (0) — Fill the tavern with new minions.
			// Live (play_DALA_912): every minion goes back to its owner's deck (the player's first, 250 ms each), two 1000 ms pauses,
			// then 4 new minions of the player's deck and 4 new ones for the host, each named and paused.
			cards.Add("DALA_912", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var source = (IPlayable)s;
					foreach (Minion m in c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).ToList())
					{
						Generic.MoveToDeck(m.Controller, s, m);
						Generic.ArtificialPause(g, s, 250);
					}
					Generic.ArtificialPause(g, s, 1000);
					Generic.ArtificialPause(g, s, 1000);
					TavernShowDeckMinions(g, c, source, brood: true);
					TavernHostMinions(g, c.Opponent, source, brood: true);
				})
			}));

			// [DALA_913] Tall Tales (1) — Choose a friendly minion. It gains +4/+4 but costs (2) more for this run.
			// (never played in the 27 recorded taverns: shaped like Tell a Story with its DALA_913e "Gossip")
			cards.Add("DALA_913", new CardDef(TavernFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => TavernRunBuff(g, c, s, TavernTarget(c, t), "DALA_913e"))
			}));
			cards.Add("DALA_913e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.AttackHealth_N(V("DALA_913e", "stats", 4)).Append(Effects.AddCost(V("DALA_913e", "cost", 2))).ToArray())
			}));

			// [DALA_914] The Upper Hand (1) — Look at four spells from your deck. Reduce the cost of one by (3) for this run.
			// (live: Kindle's copies and choice; after the pick its copy to the graveyard, then Take a Chance's reveal of the card)
			cards.Add("DALA_914", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					Generic.DiscoverFromCards(c, s, Generic.PickFromDeck(c, p => p is Spell, V("DALA_914", "look", 4)), (g2, c2, s2, card) =>
					{
						// (live: a TARGET on the picked copy, then the reveal)
						if (c2.GraveyardZone.LastOrDefault(p => p[GameTag.LINKED_ENTITY] == card.Id) is IPlayable picked)
							Generic.NameTargets(g2, new[] { picked.Id });
						Generic.RevealRunDeckCard(c2, (IPlayable)s2, card, System.Math.Max(0, card.Cost - V("DALA_914e", "discount", 3)));
						TavernRunBuff(g2, c2, s2, card, "DALA_914e");
					}))
			}));
			cards.Add("DALA_914e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("DALA_914e", "discount", 3))) }));
		}
	}
}
