// HsServer addition (not upstream SabberStone): Caverns of Time (WONDERS, client set 1898) — chunk 4: Shaman, Warlock and
// Warrior cards (shared pieces: WondersCardsGenHs.cs; rules: Actions/Wonders.cs). 14 of the chunk's 20 cards are "counts as a
// copy" reprints (client tag 858, Loader/ReprintCards.CountsAsCopyOf): their Wild ORIGINAL is rewritten here under its own id to
// its current client text (an HsServer definition replaces the 2021 one and lifts the stale-script gate) and the WON_ copy plays
// it; the WON_ id itself is never registered. The originals' enchantments keep their ids — a WON_ copy shows its twin
// (WON_093e = AT_024e, WON_110e = CFM_752e, WON_099e = AT_021e) by itself (ReprintCards.TwinEnchantment).
// Already current, nothing to write: WON_099 = AT_021 Tiny Knight of Evil, WON_323 = EX1_320 Bane of Doom (ReprintCards.Reviewed).
// Every number goes through CardValues (V), keyed by the original's id.
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
		/// <summary>Draws <paramref name="n"/> cards, one at a time (fatigue and overdraw as a normal draw).</summary>
		private static void WondersC4Draw(Controller c, int n)
		{
			for (int i = 0; i < n; i++) Generic.Draw(c);
		}

		private static void WondersChunk4(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Shaman

			// [WON_082 = CFM_707] Jade Lightning (3) — Deal $3 damage. Summon a Jade Golem.
			// (Wiki Jade Lightning, 27.2.0: "Now reads: 'Deal 3 damage. Summon a Jade Golem.'" — was 4 damage; "First, it deals
			// damage ... Then it summons the Jade Golem". The 2021 play requirement is kept: a hero is always a legal target.)
			cards.Add("CFM_707", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 } }, new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("CFM_707", "damage", 3), EntityType.TARGET, true),
					ComplexTask.SummonJadeGolem(SummonSide.SPELL))
			}));

			// [WON_085 = AT_049] Thunder Bluff Valiant (5) — Battlecry and Inspire: Give your Totems +2 Attack.
			// (A real Battlecry — wiki 27.4.0: "Fixed a bug where Thunder Bluff Valiant's new Battlecry effect was not treated as a
			// Battlecry" — and the same task as its Inspire trigger: WondersBattlecryAndInspire. Totems = friendly minions of race
			// TOTEM, ALL included (Minion.IsRace); Dormant ones and Locations aren't touched (BoardZone.GetAll).)
			cards.Add("AT_049", new CardDef(WondersBattlecryAndInspire(new CustomTask((g, c, s, t, stack) =>
				GiveAll(g, s, c.BoardZone.GetAll(m => m.IsRace(Race.TOTEM)), "AT_049e")))));
			// [AT_049e] Power of the Bluff — Increased Attack. (One enchantment per buff; the 2021 OngoingEnchant counted them on
			// whichever OngoingEnchant the totem had.)
			cards.Add("AT_049e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("AT_049", "attack", 2))) }));

			// [WON_090] Pebbly Page (2) — Battlecry: Draw an Overload card. You can't be Overloaded this turn.
			// (The draw is a hidden-zone pick: random, the card's own OVERLOAD keyword — SW_095's predicate. The protection comes
			// whatever was drawn — wiki Pebbly Page: "Fixed a bug where Pebbly Page's Overload protection effect would not trigger
			// if no Overload card was drawn". Rule: Generic.CantBeOverloadedThisTurn (player HsTag; WON_090e "Pebbled", a
			// client-only TAG_ONE_TURN_EFFECT enchantment on the player, shows it).)
			cards.Add("WON_090", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					DrawOne(g, c, p => HasOverloadCard(p.Card));
					Generic.CantBeOverloadedThisTurn(c, (IPlayable)s, "WON_090e");
				})
			}));

			// [WON_091] Totally Totems (4) — Summon all FIVE?! basic Totems. Overload: (1)
			// (Wiki Totally Totems: "summons all 5 totems that have ever been in the pool of shaman's hero power, Totemic Call".
			// Live (live-20261007-vfx-decks/power-decoded.txt 74763-74930): one POWER block, the recorded spawn effect
			// (RecordedSubSpells.tsv WON_091) around five FULL_ENTITYs in the order Stoneclaw, Searing, Wrath of Air, Strength,
			// Healing (not the RelatedCards order) — kept as a fixed order; then the Overload block (client OVERLOAD tag, engine).
			// A spell that only summons needs a free slot: REQ_NUM_MINION_SLOTS. A fuller board gets the first ones.)
			cards.Add("WON_091", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (string totem in new[] { "CS2_051", "CS2_050", "CS2_052", "CS2_058", "NEW1_009" })
						Summon(g, c, Cards.FromId(totem), s);
				})
			}));

			// [CS2_058] Strength Totem (1) — At the end of your turn, give another friendly minion +1 Attack.
			// (Totally Totems' fifth totem; client-only, unscripted until now. AT_132_SHAMANe is the same card (same name and text,
			// Totemic Slam's). A trigger's pick stays random.)
			var strengthTotem = new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (RandomFriendly(g, c, m => !m.Untouchable, s) is Minion other) Give(g, s, other, "CS2_058e");
					})
				}
			});
			cards.Add("CS2_058", strengthTotem);
			cards.Add("AT_132_SHAMANe", strengthTotem);
			// [CS2_058e] Strength of Earth — +1 Attack.
			cards.Add("CS2_058e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("CS2_058", "attack", 1))) }));

			// [WON_320 = AT_048] Healing Wave (3) — Restore #8 Health. Reveal a minion in each deck. If yours costs more, restore
			// #16 instead. (Wiki Healing Wave, 27.2.0: healing 7/14 → 8/16. The Joust: RevealTask.)
			cards.Add("AT_048", new CardDef(TargetAny, new Power
			{
				PowerTask = new RevealTask(
					new HealTask(V("AT_048", "joust heal", 16), EntityType.TARGET),
					new HealTask(V("AT_048", "heal", 8), EntityType.TARGET))
			}));

			// [WON_336 = OG_209] Hallazeal the Ascended (5) — Spell Damage +1. Your spells have Lifesteal.
			// (Spell Damage +1: its client SPELLPOWER tag. Lifesteal: the controller aura Generic.SpellsHaveLifestealAura
			// (Spell.HasLifeSteal) — wiki Hallazeal: "triggers from all damage dealt by the controlling player's spells, even if
			// that damage is dealt to friendly characters". Replaces the 2021 "restore that much Health" trigger.)
			cards.Add("OG_209", new CardDef(new Power { Aura = Generic.SpellsHaveLifestealAura }));

			// ---------------------------------------------------------------- Warlock

			// [WON_093 = AT_024] Demonfuse (2) — Give a Demon +3/+3.
			// (Wiki Demonfuse, 27.2.0: "previously: '... Give your opponent a Mana Crystal.'" — no Mana Crystal any more.)
			cards.Add("AT_024", new CardDef(new Dictionary<PlayReq, int>
				{
					{ PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_RACE, (int)Race.DEMON }
				},
				new Power { PowerTask = new AddEnchantmentTask("AT_024e", EntityType.TARGET) }));
			// [AT_024e] Dark Fusion — +3/+3. (WON_093e is its twin.)
			cards.Add("AT_024e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("AT_024", "stats", 3))) }));

			// [WON_095 = GVG_015] Darkbomb (2) — Deal $3 damage to a character. If it dies, draw a Shadow spell.
			// ("If it dies": dead or pending destroy right after the damage, inside the block — live Bane of Doom
			// (live-20261004-night 224614 38889) resolves its "if it dies" before DEATHS; the draw is a random deck Shadow spell.)
			cards.Add("GVG_015", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("GVG_015", "damage", 3), EntityType.TARGET, true),
					new CustomTask((g, c, s, t, stack) =>
					{
						if (t is ICharacter hit && hit.IsDead) DrawOne(g, c, p => p is Spell && p.Card[SPELL_SCHOOL] == SHADOW);
					}))
			}));

			// [WON_097 = OG_116] Spreading Madness (3) — Deal $13 damage randomly split among ALL characters.
			// (Wiki, 27.2.0: buffed "from 9". Spell Damage adds missiles: the card's client tag 349, EnqueueTask spellDmg.)
			cards.Add("OG_116", new CardDef(new Power
			{
				PowerTask = new EnqueueTask(V("OG_116", "damage", 13), ComplexTask.DamageRandomTargets(1, EntityType.ALL, 1), true)
			}));

			// [WON_103] Chamber of Viscidus (3, Location) — Look at 3 cards in your hand and choose one to discard. Draw two cards.
			// (Activation = PowerTask. "Look at 3 cards" = up to 3 random different cards of your hand, the player picks one
			// (Generic.DiscoverFromCards, as Coilfang Constrictor's look into a hand); the real card is discarded
			// (Generic.DiscardBlock: discard triggers, DiscardedEntities), then two draws. Wiki Chamber of Viscidus, 27.2.2: "Fixed a
			// bug where Chamber of Viscidus could not be activated without cards in hand to discard" — an empty hand just draws.)
			cards.Add("WON_103", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int draws = V("WON_103", "draw", 2);
					List<IPlayable> shown = Generic.PickFrom(c.HandZone, g, null, V("WON_103", "cards", 3));
					bool offered = shown.Count > 0 && Generic.DiscoverFromCards(c, s, shown, (g2, c2, s2, pick) =>
					{
						if (pick?.Zone?.Type == Zone.HAND) Generic.DiscardBlock.Invoke(c2, pick);
						WondersC4Draw(c2, draws);
					});
					if (!offered) WondersC4Draw(c, draws);
				})
			}));

			// [WON_104] Witch of the Arch-Thief (4) — Battlecry: Summon a 1/3 Voidwalker with Taunt. If your opponent has more
			// minions, repeat.
			// (Summon first, then compare; repeat while the opponent has more minions (Dormant minions and Locations don't count:
			// BoardZone.GetAll) and the board has room. Placement: the shared summon rule (right of her, then alternating).)
			cards.Add("WON_104", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Card voidwalker = Cards.FromId("CS2_065");
					for (int i = 0; i < c.BoardZone.MaxSize && !c.BoardZone.IsFull; i++)
					{
						Summon(g, c, voidwalker, s);
						if (c.Opponent.BoardZone.GetAll().Length <= c.BoardZone.GetAll().Length) break;
					}
				})
			}));

			// [WON_105 = OG_121] Cho'gall (8) — Battlecry: Return all cards you discarded this game to your hand. They cost Health
			// instead of Mana.
			// (Every card of DiscardedEntities, oldest first, comes back as a NEW card (Generic.NewFromGraveyard: generated cards
			// are new entities) until the hand is full, with OG_121e2 "Dark Power" (CARD_COSTS_HEALTH). OG_121e is the old one-turn
			// "next spell" enchantment (TAG_ONE_TURN_EFFECT) — not used. Burned cards were never discarded (rule book DC4).)
			cards.Add("OG_121", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (int id in c.DiscardedEntities.ToList())
					{
						if (c.HandZone.IsFull) break;
						if (!g.IdEntityDic.TryGetValue(id, out IPlayable discarded)) continue;
						IPlayable back = Generic.NewFromGraveyard(c, discarded);
						if (Generic.AddHandPhase.Invoke(c, back) && back.Zone?.Type == Zone.HAND) Give(g, s, back, "OG_121e2");
					}
				})
			}));
			// [OG_121e2] Dark Power — Costs Health instead of Mana.
			cards.Add("OG_121e2", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.CARD_COSTS_HEALTH, EffectOperator.SET, 1)) }));

			// [WON_322 = OG_302] Usher of Souls (4) — Whenever a minion dies, give your C'Thun +1/+1 (wherever it is).
			// (Wiki, 27.2.0: "friendly minion" → "minion": any side. The ritual: RitualTask → Generic.CthunRitual.)
			cards.Add("OG_302", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEATH)
				{
					TriggerSource = TriggerSource.ALL_MINIONS_EXCEPT_SELF,
					SingleTask = new RitualTask(V("OG_302", "stats", 1))
				}
			}));

			// ---------------------------------------------------------------- Warrior

			// [WON_110 = CFM_752] Stolen Goods (2) — Draw a Taunt minion. Give it +2/+2.
			// (Wiki, 27.2.0: was "Give a random Taunt minion in your hand +3/+3". Rule book DC3: an effect that draws then acts on the
			// card needs it to reach the hand (DrawOne: null when burnt). Live (224614 116666): no Taunt minion → empty block.)
			cards.Add("CFM_752", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (DrawOne(g, c, p => p is Minion && p[GameTag.TAUNT] > 0) is IPlayable drawn) Give(g, s, drawn, "CFM_752e");
				})
			}));
			// [CFM_752e] Smuggling — +2/+2 from Stolen Goods. (WON_110e is its twin.)
			cards.Add("CFM_752e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("CFM_752", "stats", 2))) }));

			// [WON_114 = GVG_056] Iron Juggernaut (6) — Battlecry and Deathrattle: Shuffle a Mine into your opponent's deck. When
			// drawn, explode for 10 damage!
			// (Live: Battlecry live-20261004-night/224614 37350 — plain POWER block, DISPLAYED_CREATOR, shuffle (AddCardTo OP_DECK);
			// Deathrattle 222041 21345 — the same inside "GVGFX_IronJuggernaut_SpawnMines_FX" (NamedFx: plays once the recorded
			// table has the prefab). The Mine GVG_056t (Casts When Drawn, 10 damage) is the 2021 script.)
			ISimpleTask mine = new AddCardTo("GVG_056t", EntityType.OP_DECK);
			cards.Add("GVG_056", new CardDef(new Power
			{
				PowerTask = mine,
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
					Generic.NamedFx((IPlayable)s, "SpawnMines", new int[0], () => mine.Process(g, c, s, t, stack)))
			}));

			// [WON_115] Blast from the Past (4) — Get 2 Spare Parts. Summon two 1/1 Boom Bots. Shuffle a Bomb into your opponent's
			// deck.
			// (In text order. Spare Parts: Generic.RandomSparePart (the 7 PART_ spells, independent picks); Boom Bot GVG_110t and
			// Bomb BOT_511t (Casts When Drawn, 5 damage) are their 2021 scripts.)
			cards.Add("WON_115", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("WON_115", "spare parts", 2); i++)
						if (Generic.RandomSparePart(g) is Card part) AddToHand(c, part);
					for (int i = 0; i < V("WON_115", "boom bots", 2); i++)
						Summon(g, c, Cards.FromId("GVG_110t"), s);
					new AddCardTo("BOT_511t", EntityType.OP_DECK).Process(g, c, s, t, stack);
				})
			}));

			// [WON_116] Ivory Rook (4) — Battlecry: Discover a Taunt minion. Gain Armor equal to its Cost.
			// (The rule's default pool: class + neutral (DiscoverFromTask.ClassOrNeutral, as I Know a Guy). "Its Cost" = the picked
			// card's base Cost — wiki Ivory Knight, the card it throws back to: "the base mana cost of the discovered card (prior to
			// the application of any cost-modifying effects)".)
			cards.Add("WON_116", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x[GameTag.TAUNT] > 0,
					afterPick: new CustomTask((g, c, s, picked, stack) =>
					{
						if (picked != null && picked.Card.Cost > 0) c.Hero.GainArmor((IPlayable)s, picked.Card.Cost);
					}))
			}));

			// [WON_117 = CFM_643] Hobart Grapplehammer (4) — Battlecry: If you have a weapon equipped, give all minions in your hand
			// and deck +2/+2.
			// (Wiki, 27.2.0: was "Give all weapons in your hand and deck +1 Attack". The enchantment is WON_117e "Smuggling +2/+2."
			// for both ids: CFM_643e / CFM_643e2 still carry the old "+1 Attack from Hobart Grapplehammer" text. Live (224614
			// 59960): no weapon → an empty POWER block.)
			cards.Add("CFM_643", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.Hero.Weapon != null)
						GiveAll(g, s, c.HandZone.Concat(c.DeckZone).Where(p => Generic.IsMinionCard(p)).ToList(), "WON_117e");
				})
			}));
			// [WON_117e] Smuggling — +2/+2.
			cards.Add("WON_117e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("CFM_643", "stats", 2))) }));
		}
	}
}
