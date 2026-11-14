// HsServer addition (not upstream SabberStone): Caverns of Time (WONDERS, client set 1898) — chunk 3: Paladin, Priest and
// Rogue cards (Timeless Blessing … Convert). The "counts as a copy" cards (client tag 858) are not registered under their WON
// id: their updated Wild original is rewritten here under its own id and the WON card plays it (Loader/ReprintCards.cs
// CountsAsCopyOf; WondersCardsGenHs.cs). Shared pieces: Actions/Wonders.cs, WondersCardsGenHs.cs. Every number goes through
// CardValues (V).
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Actions;
using SabberStoneCore.Auras;
using SabberStoneCore.Conditions;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
using SabberStoneCore.Loader;
using SabberStoneCore.src.Loader;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks;
using SabberStoneCore.Tasks.SimpleTasks;
using SabberStoneCore.Triggers;

namespace SabberStoneCore.CardSets.HsServer
{
	public static partial class CurrentCoreCardsGen
	{
		/// <summary>Every minion alive on both boards (BoardZone.GetAll: no Dormant minions, no Locations — they aren't minions to an
		/// effect), but <paramref name="except"/>.</summary>
		private static List<Minion> WondersC3AllMinions(Controller c, IEntity except = null) =>
			c.BoardZone.GetAll(m => !m.IsDead && !m.ToBeDestroyed && !ReferenceEquals(m, except))
				.Concat(c.Opponent.BoardZone.GetAll(m => !m.IsDead && !m.ToBeDestroyed && !ReferenceEquals(m, except))).ToList();

		/// <summary>A new card put into the hand with an enchantment ("Get two random Dragons with +2/+2"); nothing on a full hand.</summary>
		private static void WondersC3AddToHandWith(Game g, Controller c, IEntity source, Card card, string enchantment)
		{
			if (card == null || c.HandZone.IsFull) return;
			IPlayable p = Entity.FromCard(c, card);
			if (Generic.AddHandPhase.Invoke(c, p) && p.Zone?.Type == Zone.HAND) Give(g, source, p, enchantment);
		}

		/// <summary>The rest of an effect after the pick it just offered (Choice.ThenTask on the last choice of <paramref name="source"/>,
		/// as Titans.cs does for a Titan ability; values only: an AI's copy of the game runs it too); at once when nothing was offered.</summary>
		private static void WondersC3AfterPick(Game g, Controller c, IEntity source, bool offered, ISimpleTask then)
		{
			Choice last = c.Choice;
			while (last?.NextChoice != null) last = last.NextChoice;
			if (offered && last != null && last.SourceId == source.Id && last.ThenTask == null)
				last.ThenTask = then;
			else
				then.Process(g, c, source, null);
		}

		private static void WondersChunk3(IDictionary<string, CardDef> cards)
		{
			// ================================================================ Paladin

			// [WON_051] Timeless Blessing (4) — Give four random minions in your hand +4/+4, +3/+3, +2/+2, and +1/+1.
			// (random minions of the hand, a hidden-zone pick the text calls random; in pick order the biggest first — with fewer than
			// four, the first picks get the biggest buffs: no source, doubtful. WON_051e "+{0}/+{0}": one enchantment, its number in
			// TAG_SCRIPT_DATA_NUM_1 / _2.)
			cards.Add("WON_051", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int most = V("WON_051", "most", 4);
					List<IPlayable> picks = RandomOf(g, c.HandZone.Where(p => Generic.IsMinionCard(p)), V("WON_051", "minions", 4));
					for (int i = 0; i < picks.Count && most - i > 0; i++)
						Generic.AddEnchantmentBlock(g, Cards.FromId("WON_051e"), (IPlayable)s, picks[i], most - i, most - i, 0);
				})
			}));
			cards.Add("WON_051e", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackHealthScriptTag }));

			// [WON_052] Bronze Dragonknight (4) — Taunt. Battlecry: If this has 5 or more Attack, summon a copy of this.
			// (its Attack when the Battlecry resolves; the copy keeps its state and enchantments — rules E10 "Copies to play ... All
			// state and enchantments are copied", Generic.Copy play → play — lands right of it, and its Battlecry doesn't fire. The
			// check first: the card glows in hand while it holds, PoweredUp.)
			cards.Add("WON_052", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => s is Minion me && me.AttackDamage >= V("WON_052", "attack", 5),
					new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion me && me.Zone?.Type == Zone.PLAY && !c.BoardZone.IsFull) Generic.Copy(c, s, me, Zone.PLAY);
					}))
			}));

			// [WON_053] Runi, Time Explorer (3) — Battlecry: Discover a location from the FUTURE!
			// (three different of its seven related Locations, WON_053t…t7, to the hand — wiki Runi Notes: "rather than adding them to
			// the player's hand" is about the unused trigger sounds; they go to the hand.)
			cards.Add("WON_053", new CardDef(new Power
			{
				PowerTask = new DiscoverFromTask((g, c, s) =>
					(ReprintCards.RelatedCardIds(s.Card.Id) ?? ReprintCards.RelatedCardIds("WON_053") ?? new List<string>())
					.Select(Cards.TryFromId).Where(x => x != null && x.Type == CardType.LOCATION))
			}));
			// [WON_053t] Outskirts of Lordaeron (3) — Choose a minion. Destroy all minions with less Attack than it.
			// (any minion of either side; every other minion of both boards with less Attack at resolution is destroyed, the chosen one
			// and equal Attack stay)
			cards.Add("WON_053t", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion chosen)) return;
					int attack = chosen.AttackDamage;
					foreach (Minion m in WondersC3AllMinions(c, chosen).Where(m => m.AttackDamage < attack).ToList())
						Generic.Destroy(m, s, many: true);
				})
			}));
			// [WON_053t2] Wind Rider Roost (3) — Summon a random 3-Cost minion. Give it Charge.
			// (WON_053t2e "Roost Boost": Charge; the client's CHARGE row on the Location is a reference)
			cards.Add("WON_053t2", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Card card = RandomMinionOfCost(g, V("WON_053t2", "cost", 3));
					if (card == null || c.BoardZone.IsFull) return;
					var m = (Minion)Entity.FromCard(c, card);
					Generic.SummonBlock.Invoke(g, m, -1, s);
					if (m.Zone?.Type == Zone.PLAY) Give(g, s, m, "WON_053t2e");
				})
			}));
			cards.Add("WON_053t2e", new CardDef(new Power { Enchant = new Enchant(Effects.Charge) }));
			// [WON_053t3] Xenedar (3) — Your next Battlecry this turn triggers twice.
			// (Generic.NextBattlecryTwice: WON_053te on the player (EXTRA_BATTLECRIES_BASE), WON_053t3e on the Location
			// (CUSTOM_KEYWORD_EFFECT) — by live Murmuring Elemental's pattern, the older enchantment on the player; unrecorded, doubtful)
			cards.Add("WON_053t3", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.NextBattlecryTwice(c, (IPlayable)s, "WON_053te", "WON_053t3e"))
			}));
			cards.Add("WON_053te", new CardDef(Generic.NextBattlecryTwicePlayerPower));
			cards.Add("WON_053t3e", new CardDef(Generic.NextBattlecryTwiceSourcePower));
			// [WON_053t4] The Nighthold (3) — Cast three random Paladin Secrets.
			// (cast, one at a time, never one already up, stops when the Secret zone is full: wiki The Nighthold 27.4.0 "could try to
			// cast Secrets that were already active" fixed; Generic.CastRandomSecrets)
			cards.Add("WON_053t4", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					Generic.CastRandomSecrets(c, s, x => x.HasClass(CardClass.PALADIN), V("WON_053t4", "secrets", 3)))
			}));
			// [WON_053t5] Valdrakken (3) — Get two random Dragons with +2/+2.
			// (any class: the text names none; each a new card with WON_053t5e "Dragonflight")
			cards.Add("WON_053t5", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("WON_053t5", "dragons", 2); i++)
						WondersC3AddToHandWith(g, c, s, RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.DRAGON)), "WON_053t5e");
				})
			}));
			cards.Add("WON_053t5e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("WON_053t5", "stats", 2))) }));
			// [WON_053t6] Ruins of Korune (3) — Draw 2 cards. Swap their Costs.
			// (two draws; only when both reached the hand are their current Costs swapped — wiki 27.4.0: it "could make a drawn card cost
			// 0 if paired with an invalid card ... it will now keep its original cost". WON_053t6e "Swapped Cost.": the new Cost in
			// TAG_SCRIPT_DATA_NUM_1, as Vast Wisdom's RLK_546e.)
			cards.Add("WON_053t6", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					IPlayable a = Generic.Draw(c);
					IPlayable b = Generic.Draw(c);
					if (a == null || b == null || a == b || a.Zone?.Type != Zone.HAND || b.Zone?.Type != Zone.HAND) return;
					int costA = a.Cost, costB = b.Cost;
					Give(g, s, a, "WON_053t6e", costB);
					Give(g, s, b, "WON_053t6e", costA);
				})
			}));
			cards.Add("WON_053t6e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(0)) { UseScriptTag = true } }));
			// [WON_053t7] Temple of Earth (3) — Choose a friendly minion. Summon a copy of it with +3 Health and Taunt that can't attack.
			// (the copy keeps the minion's current state — wiki 27.6.0 "used base HP instead of current HP" fixed — and gets
			// WON_053t7e "Defender of Deepholm" before it is summoned (Generic.Copy's prepare); a plain enchantment, not an aura — wiki
			// 27.4.0 "granted an aura that would apply through stat-setting cards (like Equality)" fixed)
			cards.Add("WON_053t7", new CardDef(LocationFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m) || c.BoardZone.IsFull) return;
					Generic.Copy(c, s, m, Zone.PLAY, prepare: copy => Give(g, s, copy, "WON_053t7e"));
				})
			}));
			cards.Add("WON_053t7e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Health_N(V("WON_053t7", "health", 3)), new Effect(GameTag.TAUNT, EffectOperator.SET, 1),
					new Effect(GameTag.CANT_ATTACK, EffectOperator.SET, 1))
			}));

			// [OG_311 / WON_333] A Light in the Darkness (2) — Discover a Paladin minion. Give it +2/+2.
			// (27.2: was "Discover a minion. Give it +1/+1"; the text states the pool: Paladin minions, no neutrals — live
			// live-20261004-night/224614 52153 offered SCH_712 and BOT_906. OG_311e "Beacon of Hope" on the pick.)
			cards.Add("OG_311", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.Where((c, x) => x.Type == CardType.MINION && x.HasClass(CardClass.PALADIN), "OG_311e")
			}));
			cards.Add("OG_311e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("OG_311", "stats", 2))) }));

			// ================================================================ Priest

			// [LOE_006 / WON_056] Museum Curator (2) — Battlecry: Discover a Deathrattle card. It costs (1) less.
			// (27.2 added "It costs (1) less"; class + neutral pool, any card type with Deathrattle — wiki Notes: as a non-Priest "it is
			// possible to discover Deathrattle weapons". WON_056e "Curated" on the pick: LOE_006 has no enchantment of its own, as live
			// AT_036 uses the WON token.)
			cards.Add("LOE_006", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x[GameTag.DEATHRATTLE] > 0, "WON_056e")
			}));
			cards.Add("WON_056e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("LOE_006", "less", 1))) }));

			// [KAR_204 / WON_057] Onyx Bishop (4) — Battlecry: Summon a random friendly minion that died this game.
			// (wiki Notes: "This minion's Battlecry functions the same as Resurrect" — a new entity of a random dead friendly minion)
			cards.Add("KAR_204", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => ResurrectOne(g, c, s, _ => true))
			}));

			// [AT_012 / WON_058] Spawn of Shadows (5) — Battlecry and Inspire: Deal 4 damage to each hero.
			// (both heroes in one damage event; the Inspire trigger plays the client's DarkBomb_FX, effect index 0)
			cards.Add("AT_012", new CardDef(WondersBattlecryAndInspire(new DamageTask(V("AT_012", "damage", 4), EntityType.HEROES))));

			// [AT_018 / WON_063] Confessor Paletress (7) — Battlecry and Inspire: Summon a random Legendary minion.
			// (a collectible Legendary minion of any class, never a Colossal — wiki 28.2.0 "could summon Colossal minions" fixed;
			// right of her; the recorded ATFX_Paletress_Inspire_OverrideSpawn wraps it (Summon's Spawn role). Never herself: the
			// user rule "a card never generates itself" (RandomCard) — the wiki says "including another Confessor Paletress": doubtful.)
			cards.Add("AT_018", new CardDef(WondersBattlecryAndInspire(new CustomTask((g, c, s, t, stack) =>
				Summon(g, c, RandomCard(g, x => x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY && !x.NeverRandomlyOnBoard), s)))));

			// [WON_064] Shadow Word: Forbid (2) — Tradeable. Destroy a 4-Attack minion. Corrupt: Destroy ALL 4-Attack minions.
			// (a minion with exactly that Attack: PlayReq REQ_TARGET_EXACT_ATTACK 143, the client's own; Corrupt / Trade are data:
			// its Corrupted version is WON_064ts by COLLECTION_RELATED_CARD_DATABASE_ID — Generic.CorruptedVersion)
			cards.Add("WON_064", new CardDef(new Dictionary<PlayReq, int>
				{
					{ PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_TARGET_EXACT_ATTACK, V("WON_064", "attack", 4) }
				},
				new Power { PowerTask = new DestroyTask(EntityType.TARGET) }));
			// [WON_064ts] Shadow Word: Forbid (Corrupted) (2) — Tradeable. Destroy ALL 4-Attack minions.
			// (every minion of both boards with exactly that Attack when it resolves, destroyed together)
			cards.Add("WON_064ts", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int attack = V("WON_064ts", "attack", 4);
					foreach (Minion m in WondersC3AllMinions(c).Where(m => m.AttackDamage == attack).ToList())
						Generic.Destroy(m, s, many: true);
				})
			}));

			// [WON_066] Murozond, Thief of Time (7) — Battlecry: If your deck has no duplicates, Discover a Dragon. Deal damage equal to
			// its Cost to all other minions.
			// (both sentences sit under the condition — HearthstoneTopDecks: "The damage effect is conditional—it only triggers if your
			// deck contains no duplicate cards"; Priest + neutral Dragons (Discover's default pool); after the pick, the pick's Cost
			// as damage from Murozond to every other minion of both boards at once. A full hand skips the Discover and so the damage
			// (DiscoverFromTask): doubtful.) Live (live-20261007-zilliax-etc-remix 63979-64190): the damage inside the recorded
			// Breath of the Infinite FullBoard effect (Source 0, one TARGET list), after the pick — not around the Discover (OwnFx).
			cards.Add("WON_066", new CardDef(new Power
			{
				OwnFx = true,
				PowerTask = ComplexTask.If((g, c, s) => NoDuplicatesInDeck(c),
					DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.IsRace(Race.DRAGON),
						afterPick: new CustomTask((g, c, s, t, stack) =>
						{
							int damage = t?.Cost ?? 0;
							if (damage > 0) Generic.WholeFx((IPlayable)s, () => DamageAll(s, WondersC3AllMinions(c, s), damage, false));
						})))
			}));

			// [GVG_011 / WON_314] Shrinkmeister (2) — Battlecry: Give a minion -3 Attack this turn.
			// (27.2: -2 → -3; wiki Notes: it "cannot lower a minion's Attack below 0" (the Attack shown stops at 0) and with no enemy
			// minion a friendly one must be chosen — the upstream play requirements are kept)
			cards.Add("GVG_011", new CardDef(BattlecryMinion, new Power { PowerTask = new AddEnchantmentTask("GVG_011a", EntityType.TARGET) }));
			cards.Add("GVG_011a", OneTurn(new Effect(GameTag.ATK, EffectOperator.SUB, V("GVG_011", "attack", 3))));

			// [AT_015 / WON_342] Convert (3) — Put a copy of an enemy minion into your hand. It costs (1).
			// (27.2 added "It costs (1)": a new card of it (play → hand: no enchantments, rules E10) with the client's generic "Cost =
			// 1" GBL_001e — neither card has its own Cost enchantment; doubtful which one live attaches)
			cards.Add("AT_015", new CardDef(TargetEnemyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m) || c.HandZone.IsFull) return;
					IPlayable copy = Generic.Copy(c, s, m, Zone.HAND);
					if (copy?.Zone?.Type == Zone.HAND) Give(g, s, copy, "GBL_001e");
				})
			}));

			// ================================================================ Rogue

			// [CFM_690 / WON_070] Jade Shuriken (2) — Deal $3 damage. Combo: Summon a Jade Golem.
			// (27.2: 2 → 3 damage; the client's CHOOSE_ONE / JADE_GOLEM rows on it are references — no Choose One)
			cards.Add("CFM_690", new CardDef(TargetAny, new Power
			{
				PowerTask = new DamageTask(V("CFM_690", "damage", 3), EntityType.TARGET, true),
				ComboTask = ComplexTask.Create(
					new DamageTask(V("CFM_690", "damage", 3), EntityType.TARGET, true),
					ComplexTask.SummonJadeGolem(SummonSide.SPELL))
			}));

			// [AT_033 / WON_071] Burgle (3) — Get 3 random cards (from your opponent's class).
			// (each a random collectible card of the opponent's class; against a classless hero The Coin — wiki Burgle: "Playing Burgle
			// against neutral heroes will generate 2 copies of The Coin" (old 2-card text); Generic.OpponentClassCard)
			cards.Add("AT_033", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("AT_033", "cards", 3); i++) AddToHand(c, Generic.OpponentClassCard(g, c));
				})
			}));

			// [AT_036 / WON_076] Anub'arak (8) — Deathrattle: Summon a 4/4 Nerubian with "Deathrattle: Summon Anub'arak."
			// (27.2: "changed from returning the card to hand to summoning a 4/4 token". Live: AT_036's Deathrattle (TRIGGER, effect
			// index 1) summons WON_076t, and WON_076t's (index 0) a new WON_076 — live-20261006-glugg/220718 9278-9304, 9734-9760.)
			cards.Add("AT_036", new CardDef(new Power
			{
				DeathrattleTask = new SummonTask("WON_076t", SummonSide.DEATHRATTLE),
				DeathrattleEffectIndex = 1
			}));
			// [AT_036t / WON_076t] Nerubian (4) — Deathrattle: Summon Anub'arak. (WON_076t plays this: tag 858 → AT_036t)
			cards.Add("AT_036t", new CardDef(new Power { DeathrattleTask = new SummonTask("WON_076", SummonSide.DEATHRATTLE) }));

			// [WON_077] Mount Hyjal Imposter (4) — Each turn this is in your hand, transform it into a random 4-Cost minion that gains
			// Stealth. (the shared Imposter: WON_077 / WON_077e Shifting / WON_077e2 Stealth — WondersImposter, Generic.ShiftInHand)
			WondersImposter(cards, "WON_077");

			// [WON_078] Jade Telegram (4) — Look at 3 cards in your opponent's hand and shuffle one of them into their deck. Summon a
			// Jade Golem.
			// (the player picks one of up to three different cards of the opponent's hand — set-aside copies of them, as Stelina; the
			// real card goes back into their deck (a backward move: enchantments gone); then the Golem, after the pick (Choice.ThenTask),
			// at once with an empty enemy hand. Live location-20260930 Power.log 59175-59420: the option copy (LINKED_ENTITY /
			// COPIED_FROM_ENTITY_ID), the pick, the card revealed, HISTORY_TARGET, moved to the deck, then
			// CFMFX_JadeGolem_OverrideSpawn (recorded) and the Golem in the same POWER block.)
			cards.Add("WON_078", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					bool offered = Generic.DiscoverFromCards(c, s, Generic.PickFrom(c.Opponent.HandZone, g, _ => true),
						(g2, c2, s2, picked) => { if (picked.Zone?.Type == Zone.HAND) Generic.MoveToDeck(picked.Controller, s2, picked); });
					WondersC3AfterPick(g, c, s, offered, ComplexTask.SummonJadeGolem(SummonSide.SPELL));
				})
			}));

			// [WON_079] The Scarab Lord (4) — Battlecry: Summon a 0/1 Gong for your opponent. Combo: Gain Rush.
			// (Combo replaces the plain effect: the same Gong, and Rush on itself — the client's RUSH row on it is a reference)
			ISimpleTask scarabGong = new CustomTask((g, c, s, t, stack) => Summon(g, c.Opponent, Cards.FromId("WON_079t"), s));
			cards.Add("WON_079", new CardDef(new Power
			{
				PowerTask = scarabGong,
				ComboTask = ComplexTask.Create(scarabGong, new CustomTask((g, c, s, t, stack) =>
				{
					if (s is Minion me && me.Zone?.Type == Zone.PLAY) me.IsRush = true;
				}))
			}));
			// [WON_079t] Scarab Gong (4) — Whenever this takes damage, fill your enemy's board with 1/1 Scarabs.
			// ("your enemy" = the Gong's controller's opponent, The Scarab Lord's owner; it fires also when the damage kills it — a
			// "whenever this takes damage" trigger; 27.6 "Whenever" instead of "After". Its client sub-spell
			// WONFX_ScarabGong_Trigger_Super (CardDef m_SubSpellEffectDefs) around the summons: Generic.NamedFx.)
			cards.Add("WON_079t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						Controller enemy = c.Opponent;
						if (enemy.BoardZone.IsFull || !(s is IPlayable gong)) return;
						Generic.NamedFx(gong, "ScarabGong_Trigger", null, () =>
						{
							for (int i = 0; i < Game.MAX_MINIONS_ON_BOARD && !enemy.BoardZone.IsFull; i++)
								Summon(g, enemy, Cards.FromId("WON_079t2"), s);
						});
					})
				}
			}));

			// [OG_330 / WON_317] Undercity Huckster (2) — Deathrattle: Get a random card (from your opponent's class).
			// (the shared "from your opponent's class": wiki Undercity Huckster Notes "When playing against an adventure boss,
			// Undercity Huckster's Deathrattle will generate The Coin" — Generic.OpponentClassCard)
			cards.Add("OG_330", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => AddToHand(c, Generic.OpponentClassCard(g, c)))
			}));

			// Done elsewhere, nothing to add here:
			// [OG_334 / WON_313] Hooded Acolyte — upstream HEAL → RitualTask(1) + the client's Taunt (ReprintCards.Reviewed).
			// [AT_028 / WON_316] Shado-Pan Rider — AT_028e from the client text, +4 (ReprintCards.Reviewed).
			// [AT_034 / WON_318] Poisoned Blade — WildPoolCardsGen.cs: Generic.HeroPowerBuffsWeapon("AT_034e").
			// [LOE_012 / WON_340] Tomb Pillager — upstream "Get a Coin" (ReprintCards.Reviewed "reworded").
		}
	}
}
