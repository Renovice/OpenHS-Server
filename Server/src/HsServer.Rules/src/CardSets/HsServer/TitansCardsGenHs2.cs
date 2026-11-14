// HsServer addition (not upstream SabberStone): TITANS (set 1858) with Fall of Ulduar — chunk 1: the Neutral cards, Death Knight
// and Demon Hunter cards of the chunk, The Primus / Argus / Yogg-Saron, Unleashed (Titans), Ignis' crafted weapon parts and
// Cho'gall's anomalies. Shared pieces: Actions/Titans.cs (engine rules) and TitansCardsGenHs.cs (Titans, Plagues, related
// tokens). Every number goes through CardValues (V).
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Actions;
using SabberStoneCore.Auras;
using SabberStoneCore.Conditions;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
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
		// ---------------------------------------------------------------- chunk 1 helpers

		/// <summary>The card's own effect prefab named <paramref name="part"/> (its client CardDef: SubSpells) around
		/// <paramref name="action"/>; <paramref name="fromNowhere"/>: played from no entity (live Source 0: a spawn or to-deck
		/// effect — Argus' Argunite Army, Runic Adornment's shuffle), else from the card. Without the prefab: just the action.</summary>
		private static void TitansC1Fx(IPlayable source, string part, bool fromNowhere, System.Action action)
		{
			Game g = source?.Game;
			string prefab = g != null && g.History ? SabberStoneCore.Loader.SubSpells.FindNamed(source.Card.Id, part) : null;
			bool open = prefab != null && g.OpenSubSpell(source, prefab, null, shownSource: fromNowhere ? Game.NoSubSpellSource : 0, collectTargets: true);
			action();
			if (open) g.CloseSubSpells(source.Id);
		}

		/// <summary>A 1/1 Chaotic Tendril (YOG_514) into the player's hand, made with its current "@-Cost" (its TAG_SCRIPT_DATA_NUM_1
		/// = 1 + the player's Tendril improvements, client player tag 2868 — live live-20261006-yogg-cthun 10384-10560; the card's
		/// Power.HandCounter keeps it current). False when the hand is full.</summary>
		private static bool TitansC1AddTendril(Controller c, IEntity source)
		{
			if (c.HandZone.IsFull) return false;
			IPlayable tendril = Entity.FromCard(c, Cards.FromId("YOG_514"), new EntityData
			{
				[GameTag.CREATOR] = source.Id,
				[GameTag.TAG_SCRIPT_DATA_NUM_1] = TitansC1TendrilCost(c),
			});
			return Generic.AddHandPhase.Invoke(c, tendril);
		}

		/// <summary>Chaotic Tendril's "@-Cost": its card data's base (YOG_514 TAG_SCRIPT_DATA_NUM_1 = 1) + the player's Tendril
		/// improvements (client player tag 2868).</summary>
		private static int TitansC1TendrilCost(Controller c) =>
			Cards.FromId("YOG_514")[GameTag.TAG_SCRIPT_DATA_NUM_1] + c[Generic.TENDRIL_IMPROVEMENTS];

		/// <summary>A random spell of exactly <paramref name="cost"/> for a random cast (Chaotic Tendril's "@-Cost spell"), from the
		/// same pool CastRandomSpellTask uses (the format's spells, no Quests, scripted, shown; never the casting card; deck-gated
		/// families only when the caster's deck opens them). Picked here: CastRandomSpellTask caches a condition's pool per card,
		/// and the Tendril's cost changes during the game.</summary>
		private static Card TitansC1RandomSpellOfCost(Game g, Controller c, IEntity source, int cost)
		{
			Card self = Cards.SelfOf(source);
			var pool = Cards.FormatTypeCards(g.FormatType)
				.Where(x => x.Type == CardType.SPELL && x.Cost == cost && !x.IsQuest && x.Implemented && !x.HideStat && x.Id != self?.Id && Cards.DeckAllows(c, x))
				.ToList();
			return pool.Count == 0 ? null : pool[g.Random.Next(pool.Count)];
		}

		/// <summary>Argus, the Emerald Star's aura (<paramref name="argus"/>): is <paramref name="p"/> to the left (or right) of an
		/// Argus on its board whose aura is on (not Silenced). All minions on that side, not only the adjacent ones (the card text;
		/// spec chunk 1). The union over several Arguses is the same as each one's own aura.</summary>
		private static bool TitansC1ArgusSide(IPlayable p, IAura argus, bool left)
		{
			if (!(p is Minion m) || m.Zone?.Type != Zone.PLAY) return false;
			foreach (Minion q in m.Controller.BoardZone)
				if (q.Id != m.Id && q.OngoingEffect != null && ReferenceEquals(q.Card.Power?.Aura, argus) &&
				    (left ? m.ZonePosition < q.ZonePosition : m.ZonePosition > q.ZonePosition))
					return true;
			return false;
		}

		/// <summary>Mecha-Leaper's Deathrattle: a random other friendly Mech (a Deathrattle picks at random) gets +2/+2 and this
		/// Deathrattle (TTN_734e2). Runs from the minion (its own) or from the enchantment (source = the dying minion).</summary>
		private static ISimpleTask TitansC1Leap => new CustomTask((g, c, s, t, stack) =>
		{
			var mechs = c.BoardZone.Where(m => m.Id != s.Id && !m.IsDead && !m.ToBeDestroyed && !m.Untouchable && m.IsRace(Race.MECHANICAL)).ToList();
			if (mechs.Count > 0) Give(g, s, mechs[g.Random.Next(mechs.Count)], "TTN_734e2");
		});

		/// <summary>Disguised K'thir's shift: in its owner's hand at the start of their turn, the card becomes a random card of the
		/// opponent's deck (the same entity, its outside enchantments gone: Shifter Zerus' rule, Advanced_rulebook.txt:742) and gets
		/// "Disguise Shift" (TTN_741e) again so it keeps shifting. An empty enemy deck: it stays. Once a turn ("Each turn"): a
		/// K'thir that became another Disguised K'thir (a mirror) has both its own trigger and the enchantment's —
		/// HsTag.SHIFTED_ON_TURN keeps it to one shift.</summary>
		private static ISimpleTask TitansC1Shift => new CustomTask((g, c, s, t, stack) =>
			// (WONDERS: the shared in-hand shift, Generic.ShiftInHand)
			Generic.ShiftInHand(s, me =>
			{
				var enemyDeck = me.Controller.Opponent.DeckZone.ToList();
				return enemyDeck.Count == 0 ? null : enemyDeck[g.Random.Next(enemyDeck.Count)].Card;
			}, Cards.FromId("TTN_741e")));

		/// <summary>The keyword / effect tags a rune shows on the weapon it is crafted into (live live-20261006-dredge 2351ff).</summary>
		private static readonly GameTag[] TitansC1RuneTags =
		{
			GameTag.POISONOUS, GameTag.LIFESTEAL, GameTag.WINDFURY, GameTag.IMMUNE_WHILE_ATTACKING, GameTag.BATTLECRY, GameTag.DEATHRATTLE,
			GameTag.TRIGGER_VISUAL,
		};

		private static void TitansC1MarkRune(IPlayable shown, Card rune)
		{
			foreach (GameTag tag in TitansC1RuneTags)
				if (rune[tag] > 0 && shown[tag] == 0) shown[tag] = rune[tag];
		}

		/// <summary>An effect rune at a weapon base's tier: the Shortsword the rune itself, the Axe its "t" version, the Greatmace its
		/// "t1" version (TTN_060t7 / t7t / t7t1; live: the Axe was offered t10t / t13t / t12t).</summary>
		private static Card TitansC1RuneVersion(Card rune, int tier) =>
			tier <= 0 ? rune : Cards.TryFromId(rune.Id + (tier == 1 ? "t" : "t1")) ?? rune;

		/// <summary>
		/// Ignis, the Eternal Flame's "craft a custom weapon" — the Kazakus pattern (BAR_079) with weapons. Live (live-20261006-dredge
		/// 2351-2580): three picks — a base (Runic Shortsword / Axe / Greatmace), then one of three keyword runes, then one of three
		/// effect runes at the base's tier; the picked base is a set-aside weapon (CREATOR / DISPLAYED_CREATOR = Ignis,
		/// HISTORY_TARGET_DONT_DUPLICATE_UNTIL_END) that shows each rune (its tags, MODULAR_ENTITY_PART_1 / _2); then a NEW weapon
		/// (COPIED_FROM_ENTITY_ID = that one, 3669) comes to the hand inside Ignis' SpawnToHand effect. The new weapon is
		/// Generic.CraftedCard: the base's Cost and stats, both runes' tags and effects (their CardDefs below). Which cards are bases
		/// and runes: Ignis' client RelatedCards (weapons = bases by Cost; spells with a "t" version = effect runes, the others
		/// keyword runes).
		/// </summary>
		private static void TitansC1Craft(Controller c, IPlayable ignis)
		{
			var related = SabberStoneCore.Loader.ReprintCards.RelatedCardIds(ignis.Card.Id)?.Select(Cards.TryFromId).Where(x => x != null).ToList()
			              ?? new List<Card>();
			List<Card> bases = related.Where(x => x.Type == CardType.WEAPON).OrderBy(x => x.Cost).ToList();
			List<Card> runes = related.Where(x => x.Type == CardType.SPELL).ToList();
			List<Card> keywordRunes = runes.Where(x => Cards.TryFromId(x.Id + "t") == null).ToList();
			List<Card> effectRunes = runes.Where(x => Cards.TryFromId(x.Id + "t") != null).ToList();
			if (bases.Count == 0 || keywordRunes.Count == 0 || effectRunes.Count == 0) return;
			int ignisId = ignis.Id;
			Generic.CreateChoiceEntities(c, ignis, OfferCards(c, ignis, bases.Select(x => x.Id)), ChoiceAction.STACK, new CustomTask((g2, c2, s2, basePick, st2) =>
			{
				if (basePick == null) return;
				Card look = basePick.Card;
				int tier = bases.FindIndex(x => x.Id == look.Id);
				IPlayable preview = Entity.FromCard(c2, look, new EntityData
				{
					[GameTag.CREATOR] = ignisId,
					[GameTag.DISPLAYED_CREATOR] = ignisId,
				}, c2.SetasideZone);
				int previewId = preview.Id;
				if (g2.History)
					g2.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.HISTORY_TARGET_DONT_DUPLICATE_UNTIL_END, Data = 0, Info = new List<int> { previewId } });
				var keywordOffer = OfferCards(c2, s2, Pick(g2, keywordRunes, 3).Select(x => x.Id));
				Generic.CreateChoiceEntities(c2, s2, keywordOffer, ChoiceAction.STACK, new CustomTask((g3, c3, s3, keywordPick, st3) =>
				{
					if (keywordPick == null) return;
					Card keyword = keywordPick.Card;
					if (g3.IdEntityDic.TryGetValue(previewId, out IPlayable shown1))
					{
						TitansC1MarkRune(shown1, keyword);
						shown1[GameTag.MODULAR_ENTITY_PART_1] = keyword.AssetId;
					}
					var effectOffer = OfferCards(c3, s3, Pick(g3, effectRunes.Select(x => TitansC1RuneVersion(x, tier)), 3).Select(x => x.Id));
					Generic.CreateChoiceEntities(c3, s3, effectOffer, ChoiceAction.STACK, new CustomTask((g4, c4, s4, effectPick, st4) =>
					{
						if (effectPick == null) return;
						Card effect = effectPick.Card;
						if (g4.IdEntityDic.TryGetValue(previewId, out IPlayable shown2))
						{
							TitansC1MarkRune(shown2, effect);
							shown2[GameTag.MODULAR_ENTITY_PART_2] = effect.AssetId;
						}
						IPlayable weapon = Entity.FromCard(c4, Generic.CraftedCard(look, keyword, effect), new EntityData
						{
							[GameTag.CREATOR] = ignisId,
							[GameTag.DISPLAYED_CREATOR] = ignisId,
							[HsTag.COPIED_FROM_ENTITY_ID] = previewId,
							[(GameTag)3669] = 1,
						});
						Generic.NamedFx((IPlayable)s4, "SpawnToHand", null, () => Generic.AddHandPhase.Invoke(c4, weapon));
						// live (live-20261006-dredge 2578-2582): 3669 = 1 while it arrives, 0 again after Ignis' block (the client
						// reads no such tag; ours resets it right after the arrival effect, inside the block)
						if (weapon[(GameTag)3669] != 0) weapon[(GameTag)3669] = 0;
					}));
				}));
			}));
		}

		/// <summary>An anomaly's rule for both players (the current player first; "Both players …").</summary>
		private static ISimpleTask TitansC1BothPlayers(System.Action<Game, Controller, IPlayable> rule) => new CustomTask((g, c, s, t, stack) =>
		{
			Controller first = g.CurrentPlayer ?? c;
			foreach (Controller player in new[] { first, first.Opponent })
				rule(g, player, (IPlayable)s);
		});

		/// <summary>An anomaly's player enchantment on <paramref name="player"/> (created by their hero: the enchantment is theirs, so
		/// "your turn" triggers and "your" auras are that player's).</summary>
		private static void TitansC1PlayerEnchant(Game g, Controller player, string id)
		{
			if (player?.Hero != null)
				Generic.AddEnchantmentBlock(g, Cards.FromId(id), player.Hero, player, 0, 0, 0);
		}

		// ---------------------------------------------------------------- chunk 1

		private static void TitansChunk1(IDictionary<string, CardDef> cards)
		{
			// ================================================================ Neutral

			// [TTN_083] Son of Hodir (8) — Battlecry: Shuffle four 8/8 Giants into your deck that are summoned when drawn.
			cards.Add("TTN_083", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TTN_083", "giants", 4); i++)
						Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId("TTN_083t"), new EntityData { [GameTag.CREATOR] = s.Id }));
				})
			}));
			// [TTN_083t] Frost Tyrant (8) — Summoned When Drawn. (Draw.OnDrawn sets it aside, then this summons the same entity — far
			// right, no summoner — and the engine draws the replacement; its TRIGGER keyword is SUMMONED_WHEN_DRAWN, Draw.TopdeckBlock.
			// A full board: it isn't summoned — DOUBT, the wiki is silent.)
			cards.Add("TTN_083t", new CardDef(new Power
			{
				TopdeckTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion tyrant) || tyrant.Zone?.Type != Zone.SETASIDE || c.BoardZone.IsFull) return;
					c.SetasideZone.Remove(tyrant);
					Generic.SummonBlock.Invoke(g, tyrant, -1, null);
				})
			}));

			// [TTN_330] Kologarn (8) — Rush. Whenever this attacks a minion, put it in your hand. Deathrattle: Move any in your hand to
			// your opponent's. (The Attack Event: the defender leaves play, so there is no combat — Attack.cs cancels it; web: "The
			// minion is brought into your hand the moment you attack it, so there is no actual combat". The stolen card keeps
			// HsTag.STOLEN_BY = this Kologarn through every move of that entity (wiki Kologarn: a stolen minion played and returned is
			// remembered; a new entity is not); hand only (wiki bug fix: not the deck). Client effects: TRIGGER effect index 0 =
			// AttackTrigger, the Deathrattle's index 1 = DeathRattleTrigger.)
			cards.Add("TTN_330", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					Condition = new SelfCondition(p => p.Game.CurrentEventData?.EventTarget is Minion),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(g.CurrentEventData?.EventTarget is Minion defender) || defender.Zone?.Type != Zone.PLAY) return;
						if (Generic.TakeIntoHand(c, defender)) defender[HsTag.STOLEN_BY] = s.Id;
					})
				},
				DeathrattleEffectIndex = 1,
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable stolen in c.HandZone.Where(p => p[HsTag.STOLEN_BY] == s.Id).ToList())
					{
						stolen[HsTag.STOLEN_BY] = 0;
						Generic.TakeIntoHand(c.Opponent, stolen);
					}
				})
			}));

			// [TTN_479] Flame Revenant (2) — After you summon an Elemental, give it +1/+1. (Live: TRIGGER 0, its buff from itself.)
			cards.Add("TTN_479", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					Condition = new SelfCondition(p => p is Minion m && m.IsRace(Race.ELEMENTAL)),
					SingleTask = new AddEnchantmentTask("TTN_479e", EntityType.TARGET)
				}
			}));
			cards.Add("TTN_479e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TTN_479", "stats", 1))) }));

			// [TTN_700] Containment Unit (7) — Magnetic. Deathrattle: Summon a random 8-Cost minion. (Magnetic is the engine's, from the
			// client data: its magnetize enchantment TTN_700e1 gets this Deathrattle at load — CardContainer.MagnetizeEnchantmentsFromData;
			// wiki Containment Unit: "the host gains the deathrattle effect".)
			cards.Add("TTN_700", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, RandomMinionOfCost(g, V("TTN_700", "cost", 8)), s))
			}));

			// [TTN_711] Saronite Tol'vir (4) — Taunt. Whenever this is attacked, draw a card. (The Attack Event, before damage; hero or
			// minion attackers — wiki. Whoever attacks: the text has no "enemy" — a friendly minion forced to attack it (Yogg-Saron's
			// Induce Insanity) counts; TriggerSource.ATTACKED_SELF, the shared "this is attacked" source.)
			cards.Add("TTN_711", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.ATTACKED_SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) => Generic.Draw(c))
				}
			}));

			// [TTN_712] Sharp-Eyed Seeker (3) — Battlecry: If there is a card in your deck that didn't start there, draw it. (A hidden
			// zone: a random one of them.)
			cards.Add("TTN_712", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.DeckZone.Any(Generic.DidntStartInDeck),
					new CustomTask((g, c, s, t, stack) => DrawOne(g, c, Generic.DidntStartInDeck)))
			}));

			// [TTN_713] Angry Helhound (4) — Rush. Has +4 Attack on your turn. (No client enchantment: the Attack is what shows.)
			cards.Add("TTN_713", new CardDef(new Power
			{
				Aura = new Aura(AuraType.SELF, Effects.Attack_N(V("TTN_713", "attack", 4))) { Condition = SelfCondition.IsCurrentPlayer, Restless = true }
			}));

			// [TTN_714] Runefueled Golem (4) — Battlecry: Discover a weapon from any class.
			cards.Add("TTN_714", new CardDef(new Power { PowerTask = DiscoverFromTask.Where((c, x) => x.Type == CardType.WEAPON) }));

			// [TTN_715] Time-Lost Protodrake (4) — Taunt. When you draw this, add a random Dragon to your hand. (It stays in hand: no
			// "Casts When Drawn" text, so no replacement draw — Card.ReplacedWhenDrawn.)
			cards.Add("TTN_715", new CardDef(new Power
			{
				TopdeckTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.DRAGON)) is Card dragon) AddToHand(c, dragon);
				})
			}));

			// [TTN_716] Flame Behemoth (6) — Battlecry: Get two random Magnetic Mechs. They cost (2) less. (Any class; minions with
			// their own Magnetic — Card.Modular — and the Mech type.)
			cards.Add("TTN_716", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TTN_716", "mechs", 2); i++)
					{
						if (c.HandZone.IsFull || !(RandomCard(g, x => x.Type == CardType.MINION && x.Modular && x.IsRace(Race.MECHANICAL)) is Card mech)) break;
						IPlayable got = Entity.FromCard(c, mech);
						if (Generic.AddHandPhase.Invoke(c, got)) Give(g, s, got, "TTN_716e");
					}
				})
			}));
			cards.Add("TTN_716e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("TTN_716", "discount", 2))) }));

			// [TTN_717] Algalon the Observer (4) — Battlecry: Replace your Hero Power with Algalon's Vision.
			cards.Add("TTN_717", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => new ReplaceHeroPower(Cards.FromId("TTN_717t")).Process(g, c, s, null))
			}));
			// [TTN_717t] Algalon's Vision (1) — Hero Power: Look at the top card of your opponent's deck. You may put it on the bottom of
			// their deck. (A choice between "Keep on Top!" TTN_717t2 and "Put on Bottom!" TTN_717t3, whose text names the card
			// — REFERENCE_SCRIPT_DATA_NUM_1_ENTITY: their TAG_SCRIPT_DATA_NUM_1 is a set-aside copy of the top card the chooser sees,
			// as Dredge's linked copies (Actions/Dredge.cs); wiki bug fix 2023-08-04: the opponent must not see it. "Bottom" moves the
			// real card to deck slot 0, no shuffle. TTN_717t1 "A New Reality" is the old version: not offered. DOUBT: unrecorded.)
			cards.Add("TTN_717t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					IPlayable top = c.Opponent.DeckZone.TopCard;
					if (top == null) return;
					IPlayable seen = Entity.FromCard(c, top.Card, new EntityData
					{
						[GameTag.CREATOR] = s.Id,
						[GameTag.LINKED_ENTITY] = top.Id,
						[HsTag.COPIED_FROM_ENTITY_ID] = top.Id,
					}, c.SetasideZone);
					var offer = new List<IPlayable>();
					foreach (string option in new[] { "TTN_717t2", "TTN_717t3" })
						offer.Add(Entity.FromCard(c, Cards.FromId(option), new EntityData
						{
							[GameTag.CREATOR] = s.Id,
							[GameTag.DISPLAYED_CREATOR] = s.Id,
							[GameTag.TAG_SCRIPT_DATA_NUM_1] = seen.Id,
						}, c.SetasideZone));
					Generic.CreateChoiceEntities(c, s, offer, ChoiceAction.STACK, new CustomTask((g2, c2, s2, picked, st2) =>
					{
						if (picked == null || picked.Card.Id != "TTN_717t3") return; // Keep on Top!: nothing moves
						if (!g2.IdEntityDic.TryGetValue(picked[GameTag.TAG_SCRIPT_DATA_NUM_1], out IPlayable copy) ||
						    !g2.IdEntityDic.TryGetValue(copy[GameTag.LINKED_ENTITY], out IPlayable real) || real.Zone?.Type != Zone.DECK)
							return;
						Controller owner = real.Controller;
						owner.DeckZone.Remove(real);
						owner.DeckZone.Add(real, 0); // the bottom (a deck reorder: Sphere of Sapience, Polkelt)
					}));
				})
			}));
			cards.Add("TTN_717t2", EngineOnly); // choice card only
			cards.Add("TTN_717t3", EngineOnly); // choice card only

			// [TTN_718] Starlight Whelp (3) — Battlecry: Get a random card from your starting hand. (Generic.StartingHand: the hand when
			// the mulligan ended — the Coin included, DOUBT; never a Starlight Whelp — forum: 16 plays never made one.)
			cards.Add("TTN_718", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					string self = ((IPlayable)s).Card.Id;
					var hand = Generic.StartingHand(c).Where(x => x.Id != self).ToList();
					if (hand.Count > 0) AddToHand(c, hand[g.Random.Next(hand.Count)]);
				})
			}));

			// [TTN_723] Tram Operator (5) — Battlecry: Draw a Mech. The next Mech you play costs (2) less. (TTN_723e on the player, shown
			// on the Mechs in hand as TTN_723e1; magnetizing a Mech is playing it — wiki Magnetic.)
			cards.Add("TTN_723", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) => DrawOne(g, c, IsMech)),
					new AddEnchantmentTask("TTN_723e", EntityType.CONTROLLER))
			}));
			cards.Add("TTN_723e", NextCardCost(Effects.ReduceCost(V("TTN_723", "discount", 2)), new SelfCondition(p => p is Minion && IsMech(p)), "TTN_723e1"));

			// [TTN_731] Careless Mechanist (3) — After you draw your second card in a turn, destroy this. (Any turn; draws before it was
			// played count toward the number: Controller.NumCardsDrawnThisTurn, client 399.)
			cards.Add("TTN_731", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DRAW)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p.Controller.NumCardsDrawnThisTurn == V("TTN_731", "draws", 2)),
					SingleTask = new DestroyTask(EntityType.SOURCE)
				}
			}));

			// [TTN_732] Invent-o-matic (2) — Whenever you Magnetize a minion, give it +1/+1. (TriggerType.MAGNETIZE: its target is the
			// minion magnetized to.)
			cards.Add("TTN_732", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.MAGNETIZE) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = new AddEnchantmentTask("TTN_732e", EntityType.TARGET) }
			}));
			cards.Add("TTN_732e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TTN_732", "stats", 1))) }));

			// [TTN_733] Relentless Worg (3) — Rush. After this attacks and kills a minion, restore this to full Health.
			cards.Add("TTN_733", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					Condition = new SelfCondition(p => KilledItsDefender(p.Game)),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion me && me.Zone?.Type == Zone.PLAY && !me.ToBeDestroyed && me.Health > 0 && me.Damage > 0)
							new HealTask(me.Damage, EntityType.SOURCE).Process(g, c, s, null);
					})
				}
			}));

			// [TTN_734] Mecha-Leaper (3) — Magnetic. Deathrattle: Give a friendly Mech +2/+2 and this Deathrattle. (Its magnetize
			// enchantment TTN_734e gets this Deathrattle from the data at load; TTN_734e2 carries it on.)
			cards.Add("TTN_734", new CardDef(new Power { DeathrattleTask = TitansC1Leap }));
			// [TTN_734e2] Mecha-Leaping — +2/+2. Deathrattle: Give a friendly Mech +2/+2 and this Deathrattle.
			cards.Add("TTN_734e2", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("TTN_734", "attack", 2)), Effects.Health_N(V("TTN_734", "health", 2)),
					new Effect(GameTag.DEATHRATTLE, EffectOperator.SET, 1)),
				DeathrattleTask = TitansC1Leap
			}));

			// [TTN_741] Disguised K'thir (5) — Each turn this is in your hand, transform into a random card in your opponent's deck.
			// (Shifter Zerus' pattern, OG_123 / OG_123e: each of its owner's turns; TTN_741e "Disguise Shift" keeps it going and leaves
			// when the card is played. DOUBT: live timing — assumed like Zerus.)
			cards.Add("TTN_741", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START) { TriggerActivation = TriggerActivation.HAND, SingleTask = TitansC1Shift }
			}));
			cards.Add("TTN_741e", new CardDef(new Power
			{
				Enchant = new Enchant() { RemoveWhenPlayed = true },
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = TitansC1Shift }
			}));

			// [TTN_742] Celestial Projectionist (2) — Battlecry: Choose a friendly minion. Add a Temporary copy of it to your hand. (A
			// copy from play to hand keeps no enchantments — Advanced rulebook § Copy effects; Temporary = the shared GBL_999e, not the
			// pre-30.0 TTN_742e.)
			cards.Add("TTN_742", new CardDef(BattlecryFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m) || m.Id == s.Id || c.HandZone.IsFull) return;
					IPlayable copy = Generic.Copy(c, s, m, Zone.HAND);
					if (copy?.Zone?.Type == Zone.HAND) Generic.MakeTemporary(copy, s);
				})
			}));

			// [TTN_751] Ignis, the Eternal Flame (4) — Battlecry: If you've Forged a card this game, craft a custom weapon.
			// (TitansC1Craft; Ignis places its own SpawnToHand effect around the new weapon.)
			cards.Add("TTN_751", new CardDef(new Power
			{
				OwnFx = true,
				PowerTask = ComplexTask.If((g, c, s) => SelfCondition.ForgedThisGame.Eval(s),
					new CustomTask((g, c, s, t, stack) => TitansC1Craft(c, (IPlayable)s)))
			}));
			// Ignis' weapon bases (TTN_060t Runic Shortsword 2/2, t1 Runic Axe 3/4, t2 Runic Greatmace 5/6 — "{0} {1}": MODULAR_ENTITY)
			// and keyword runes (Deceit of Loken: Poisonous; Hope of Sif: Lifesteal; Storm of Thorim: Windfury; Spark of Ra-den: Your
			// hero is Immune while attacking — IMMUNE_WHILE_ATTACKING) are choices and data only; the crafted weapon takes the tags.
			foreach (string part in new[] { "TTN_060t", "TTN_060t1", "TTN_060t2", "TTN_060t3", "TTN_060t4", "TTN_060t5", "TTN_060t9" })
				cards.Add(part, EngineOnly);
			// [TTN_060t6] Flame of Odyn — Also damages adjacent minions. (On the weapon: its hero's attacks cleave.)
			cards.Add("TTN_060t6", new CardDef(new Power { Trigger = Cleave(TriggerSource.HERO) }));
			// [TTN_060t7 / t7t / t7t1] Chill of Hodir — Deathrattle: Deal 1 / 2 / 4 damage to all enemies. (The weapon's Deathrattle.)
			foreach ((string id, int damage) in new[] { ("TTN_060t7", 1), ("TTN_060t7t", 2), ("TTN_060t7t1", 4) })
				cards.Add(id, new CardDef(new Power { DeathrattleTask = new DamageTask(V(id, "damage", damage), EntityType.ENEMIES) }));
			// [TTN_060t8 / t8t / t8t1] Light of Tyr — After your hero attacks, gain 2 / 4 / 8 Armor.
			foreach ((string id, int armor) in new[] { ("TTN_060t8", 2), ("TTN_060t8t", 4), ("TTN_060t8t1", 8) })
				cards.Add(id, new CardDef(new Power
				{
					Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = new ArmorTask(V(id, "armor", armor)) }
				}));
			// [TTN_060t10 / t10t / t10t1] Genius of Mimiron — After your hero attacks, summon a random 2 / 4 / 8-Cost minion.
			foreach ((string id, int cost) in new[] { ("TTN_060t10", 2), ("TTN_060t10t", 4), ("TTN_060t10t1", 8) })
			{
				int summonCost = V(id, "cost", cost);
				cards.Add(id, new CardDef(new Power
				{
					Trigger = new Trigger(TriggerType.AFTER_ATTACK)
					{
						TriggerSource = TriggerSource.HERO,
						SingleTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, RandomMinionOfCost(g, summonCost), s))
					}
				}));
			}
			// [TTN_060t12 / t12t / t12t1] Pain of Jotun — Battlecry: Deal 2 / 4 / 6 damage. (A targeted Battlecry on the weapon — live
			// dredge 3089-3131: any character, the weapon the source.)
			foreach ((string id, int damage) in new[] { ("TTN_060t12", 2), ("TTN_060t12t", 4), ("TTN_060t12t1", 6) })
				cards.Add(id, new CardDef(BattlecryAny, new Power { PowerTask = new DamageTask(V(id, "damage", damage), EntityType.TARGET) }));
			// [TTN_060t13 / t13t / t13t1] Wisdom of Freya — After your hero attacks, draw 1 / 2 / 3 cards.
			foreach ((string id, int draws) in new[] { ("TTN_060t13", 1), ("TTN_060t13t", 2), ("TTN_060t13t1", 3) })
				cards.Add(id, new CardDef(new Power
				{
					Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = new DrawTask(V(id, "draw", draws)) }
				}));

			// [TTN_754] Ravenous Kraken (3) — Battlecry: Destroy a friendly minion. Deathrattle: Summon a copy of it. ("a friendly
			// minion": the player picks. TTN_754e "Destroyed {0}" on the Kraken: NUM_1 = the destroyed minion, its card captured — the
			// instance exists without history too; a new base copy is summoned in the Kraken's slot.)
			cards.Add("TTN_754", new CardDef(BattlecryFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion victim) || victim.Id == s.Id || victim.Zone?.Type != Zone.PLAY) return;
					Generic.AddEnchantmentBlock(g, Cards.FromId("TTN_754e"), (IPlayable)s, (IPlayable)s, victim.Id, 0, victim.Id);
					Generic.Destroy(victim, s);
				})
			}));
			cards.Add("TTN_754e", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Enchantment e && e.CapturedCard != null) Summon(g, c, e.CapturedCard, s);
				})
			}));

			// [TTN_812] Victorious Vrykul (1) — After this attacks, get a 2/3 Val'kyr that costs (1). (TTN_812t costs 1 by its data.)
			cards.Add("TTN_812", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) => AddToHand(c, Cards.FromId("TTN_812t")))
				}
			}));

			// [TTN_832] Trogg Exile (3) — Rush. Battlecry: Deal 4 damage to your hero.
			cards.Add("TTN_832", new CardDef(new Power { PowerTask = new DamageTask(V("TTN_832", "damage", 4), EntityType.HERO) }));

			// [TTN_859] Fate Splitter (3) — Deathrattle: Get a copy of the last card your opponent played. (A new base card; nothing
			// when they played none.)
			cards.Add("TTN_859", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					List<PlayHistoryEntry> played = c.Opponent.PlayHistory;
					if (played.Count > 0 && played[played.Count - 1].SourceCard is Card last) AddToHand(c, last);
				})
			}));

			// [TTN_860] Drone Deconstructor (1) — Battlecry: Get a 1/1 Magnetic Sparkbot with a random Bonus Effect. (One of its eight
			// related Sparkbots TTN_719t..t7, each with its keyword built in; they magnetize from the client data — no script.)
			cards.Add("TTN_860", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (TitansRandomRelatedMinion(g, s) is Card sparkbot) AddToHand(c, sparkbot);
				})
			}));

			// [TTN_924] Razorscale (3) — Cards can't cost less than (2). (Generic.CostFloorAura: applied after every other cost change —
			// Advanced_rulebook.txt:870; both players. Its client enchantment TTN_924e "Razor's Edge: Costs at least (2)." shows on
			// each card the floor raises — the engine rule for hand cost auras (engine-rules.md: "a basic cost aura shows its client
			// card … on each card it affects"); Generic.CostFloorShownAuras. DOUBT: unrecorded, live-checks.)
			cards.Add("TTN_924", new CardDef(new Power
			{
				Aura = new MultiAura(new IAura[] { Generic.CostFloorAura(V("TTN_924", "floor", 2)) }
					.Concat(Generic.CostFloorShownAuras("TTN_924e")).ToArray())
			}));

			// [TTN_931] Imposing Anubisath (4) — Taunt. Can't attack. (Data: Taunt; CANT_ATTACK from its text — Loader/ReprintCards.)
			cards.Add("TTN_931", EngineOnly);

			// [YOG_411] Prison Breaker (4) — Battlecry: If you've cast 5 or more spells this game, deal 2 damage to all enemies.
			// @ ({0} left!)@ (Ready!) (The card's count: spells its owner played this game, client 1780; the threshold 5 is its data.)
			cards.Add("YOG_411", new CardDef(new Power
			{
				ThresholdCount = p => p.Controller.NumSpellsPlayedThisGame,
				PowerTask = ComplexTask.If((g, c, s) => Generic.ThresholdMet(s), new DamageTask(V("YOG_411", "damage", 2), EntityType.ENEMIES))
			}));

			// [YOG_514] Chaotic Tendril (1) — Battlecry: Cast a random @-Cost spell. Improve your future Chaotic Tendrils. (Live
			// live-20261006-yogg-cthun 10384-10560: the random spell cast, then the player's tag 2868 +1; every Tendril shows
			// 1 + it through Power.HandCounter.)
			cards.Add("YOG_514", new CardDef(new Power
			{
				HandCounter = p => TitansC1TendrilCost(p.Controller),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int cost = TitansC1TendrilCost(c);
					if (TitansC1RandomSpellOfCost(g, c, s, cost) is Card spell)
						new CastRandomSpellTask { Spell = spell }.Process(g, c, s, null);
					c[Generic.TENDRIL_IMPROVEMENTS] = c[Generic.TENDRIL_IMPROVEMENTS] + 1;
				})
			}));

			// [YOG_515] Eye of Chaos (5) — Battlecry: Get two 1/1 Chaotic Tendrils.
			cards.Add("YOG_515", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("YOG_515", "tendrils", 2); i++)
						if (!TitansC1AddTendril(c, s)) break;
				})
			}));

			// [YOG_530] Cho'gall, Twilight Chieftain (6) — Taunt, Lifesteal. Start of Game: 50% chance to corrupt the game with a random
			// anomaly. (From the deck, as Darkbishop Benedictus — the chance is its data, TAG_SCRIPT_DATA_NUM_1 = 50; wiki Cho'gall:
			// "When both players have Cho'gall … 100% chance", never two anomalies. A random one of its client RelatedCards, the
			// game's ANOMALY1 (Generic.StartAnomaly: the client's medallion), then that anomaly's own script applies its rule to both
			// players. DOUBT: unrecorded — the anomaly entity's zone, when the medallion shows.)
			cards.Add("YOG_530", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.GAME_START)
				{
					TriggerActivation = TriggerActivation.DECK,
					RemoveAfterTriggered = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is IPlayable chogall) || Generic.AnomalyActive(g)) return;
						bool both = c.Opponent.DeckZone.Concat(c.Opponent.HandZone).Any(p => p.Card.Id == chogall.Card.Id);
						int fromData = chogall.Card[GameTag.TAG_SCRIPT_DATA_NUM_1] > 0 ? chogall.Card[GameTag.TAG_SCRIPT_DATA_NUM_1] : 50;
						int chance = both ? 100 : V("YOG_530", "chance", fromData);
						if (g.Random.Next(100) >= chance) return;
						if (!(Generic.RandomRelated(g, chogall.Card, CardType.SPELL) is Card anomaly)) return;
						IPlayable started = Generic.StartAnomaly(g, anomaly, chogall);
						if (started != null) anomaly.Power?.PowerTask?.Process(g, c, started, null);
					})
				}
			}));

			// ================================================================ Death Knight

			// [TTN_450] Distressed Kvaldir (2) — Deathrattle: Shuffle two random Plagues into your opponent's deck.
			cards.Add("TTN_450", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TTN_450", "plagues", 2); i++) TitansShufflePlague(c, s, TitansRandomPlague(g));
				})
			}));
			// [TTN_450t] Blood Plague (1) — Casts When Drawn. Take 2 damage. Restore 2 Health to the enemy hero. ("you" = the drawer;
			// no "$": no Spell Damage.)
			cards.Add("TTN_450t", TitansPlague(ComplexTask.Create(
				new DamageTask(V("TTN_450t", "damage", 2), EntityType.HERO),
				new HealTask(V("TTN_450t", "heal", 2), EntityType.OP_HERO))));
			// [TTN_450t2] Unholy Plague (1) — Casts When Drawn. Take 2 damage. Summon a 2/2 Undead for your opponent. (RLK_070t Undead
			// Peasant — HearthstoneTopDecks "Cards Relating to Unholy Plague"; not in the client's RelatedCards, DOUBT-low.)
			cards.Add("TTN_450t2", TitansPlague(ComplexTask.Create(
				new DamageTask(V("TTN_450t2", "damage", 2), EntityType.HERO),
				new CustomTask((g, c, s, t, stack) => Summon(g, c.Opponent, Cards.FromId("RLK_070t"), s)))));
			// [TTN_450t3] Frost Plague (1) — Casts When Drawn. Take 2 damage. Your next card costs (1) more (up to 10). (TTN_450e on the
			// drawer, TTN_450e2 on their cards; wiki Frost Plague: "Multiple applications would stack".)
			cards.Add("TTN_450t3", TitansPlague(ComplexTask.Create(
				new DamageTask(V("TTN_450t3", "damage", 2), EntityType.HERO),
				new AddEnchantmentTask("TTN_450e", EntityType.CONTROLLER))));
			cards.Add("TTN_450e", NextCardCost(Effects.AddCostNotAbove10(V("TTN_450t3", "more", 1)), new SelfCondition(p => true), "TTN_450e2"));

			// [TTN_454] Down with the Ship (2) — Deal $3 damage. Shuffle two random Plagues into your opponent's deck.
			cards.Add("TTN_454", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("TTN_454", "damage", 3), EntityType.TARGET, true),
					new CustomTask((g, c, s, t, stack) =>
					{
						for (int i = 0; i < V("TTN_454", "plagues", 2); i++) TitansShufflePlague(c, s, TitansRandomPlague(g));
					}))
			}));

			// [TTN_455] Tomb Traitor (4) — Battlecry: Destroy a Plague in your opponent's deck to deal 3 damage to all enemy minions.
			// ("to": the damage only when one was destroyed — forum eu.forums.blizzard.com/…/tomb-traitor-doesnt-always-work-bug/10242.)
			cards.Add("TTN_455", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => TitansEnemyDeckHasPlague(c), new CustomTask((g, c, s, t, stack) =>
				{
					if (TitansDestroyPlague(c, s))
						new DamageTask(V("TTN_455", "damage", 3), EntityType.OP_MINIONS).Process(g, c, s, null);
				}))
			}));

			// [TTN_459] Chained Guardian (11) — Rush, Reborn. Costs (1) less for each Plague shuffled into the enemy deck this game.
			// (The player's count, HsTag.PLAGUES_SHUFFLED_INTO_ENEMY_DECK — Helya's unending re-shuffles count too, DOUBT.)
			cards.Add("TTN_459", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => V("TTN_459", "discount", 1) * p.Controller[HsTag.PLAGUES_SHUFFLED_INTO_ENEMY_DECK])
			}));

			// [TTN_735] Northern Navigation (2) — Discover a spell from your deck. If it's a Frost spell, Freeze a random enemy minion.
			// (Live: the DiscoverFromDeck shape — live-install session-20261003-185040.)
			cards.Add("TTN_735", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					Generic.DiscoverFromDeck(c, s, Generic.PickFromDeck(c, p => p is Spell), (g2, c2, s2, picked) =>
					{
						bool frost = IsFrostSpell(picked);
						Generic.Draw(c2, picked);
						if (!frost) return;
						var enemies = c2.Opponent.BoardZone.Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList();
						if (enemies.Count > 0) FreezeAll(new ICharacter[] { enemies[g2.Random.Next(enemies.Count)] });
					}))
			}));

			// [TTN_736] Staff of the Primus (1) — After your hero attacks, shuffle a random Plague into your opponent's deck.
			cards.Add("TTN_736", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) => TitansShufflePlague(c, s, TitansRandomPlague(g)))
				}
			}));

			// [TTN_744] Frozen Over (2) — Both players draw 2 cards. Your opponent can not play them next turn. (You first, the text's
			// order — DOUBT. The cards the opponent actually drew are locked until the end of their next turn: Generic.LockInHand,
			// shown as TTN_744e1.)
			cards.Add("TTN_744", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = V("TTN_744", "draw", 2);
					for (int i = 0; i < n; i++) Generic.Draw(c);
					int until = g.CurrentPlayer == c.Opponent ? g.Turn + 2 : g.Turn + 1;
					for (int i = 0; i < n; i++)
						if (Generic.Draw(c.Opponent) is IPlayable drawn && drawn.Zone?.Type == Zone.HAND && drawn.Controller == c.Opponent)
							Generic.LockInHand(drawn, s, until, "TTN_744e1");
				})
			}));
			cards.Add("TTN_744e1", EngineOnly); // the lock's marker (Generic.LockInHand)

			// [TTN_850] Helya (4) — Battlecry: Shuffle all three Plagues into your opponent's deck. Plagues they draw this game are
			// unending. (The rule on the receiving player — HsTag.PLAGUES_UNENDING, TitansPlague re-shuffles; wiki Helya: "after the
			// opponent draws one of the Plague cards, a copy is immediately shuffled back into their deck". TTN_850e1 "Diseases are
			// unending" shows it on Helya's own player — live 1132 21327: ATTACHED = her player, CONTROLLER = hers.)
			cards.Add("TTN_850", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Card plague in TitansPlagues) TitansShufflePlague(c, s, plague);
					c.Opponent[HsTag.PLAGUES_UNENDING] = c.Opponent[HsTag.PLAGUES_UNENDING] + 1;
					Generic.AddEnchantmentBlock(g, Cards.FromId("TTN_850e1"), (IPlayable)s, c, 0, 0, 0);
				})
			}));
			cards.Add("TTN_850e1", EngineOnly); // its marker (the rule is the player tag)

			// [YOG_512] Sickly Grimewalker (3) — After you summon an Undead, give it Poisonous. (No client enchantment: the keyword itself.)
			cards.Add("YOG_512", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					Condition = new SelfCondition(p => IsUndead(p)),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t is Minion m && m[GameTag.POISONOUS] == 0) m[GameTag.POISONOUS] = 1;
					})
				}
			}));

			// [TTN_737] The Primus (8) — Titan. After this uses an ability, Discover a card with that Rune. (The rune of the ability
			// used: its card's COST_BLOOD / COST_FROST / COST_UNHOLY; Death Knight cards with that rune — DiscoverRune.)
			cards.Add("TTN_737", TitansTitan(afterAbility: new CustomTask((g, c, s, t, stack) =>
			{
				Card used = TitansLastAbility(s);
				if (used == null) return;
				GameTag? rune = used[COST_BLOOD] > 0 ? COST_BLOOD : used[COST_FROST] > 0 ? COST_FROST : used[COST_UNHOLY] > 0 ? COST_UNHOLY : (GameTag?)null;
				if (rune is GameTag r) DiscoverRune(r).Process(g, c, s, null);
			})));
			// [TTN_737t] Runes of Blood — Destroy an enemy minion. This minion and your hero gain its Health. (Its two prefabs placed
			// here: Destroy at the minion, the Absorb missile to the Titan and the hero; TTN_737te carries the Health, NUM_1.)
			cards.Add("TTN_737t", TitansAbility(new CustomTask((g, c, s, t, stack) =>
			{
				if (!(t is Minion victim) || !(s is Minion titan)) return;
				int health = System.Math.Max(0, victim.Health);
				Card runes = Cards.FromId("TTN_737t");
				Generic.AbilityFx(titan, runes, "Destroy", new[] { victim.Id }, () => Generic.Destroy(victim, titan));
				if (health <= 0) return;
				Generic.AbilityFx(titan, runes, "Absorb", new[] { titan.Id, c.Hero.Id }, () =>
				{
					Give(g, titan, titan, "TTN_737te", health);
					Give(g, titan, c.Hero, "TTN_737te", health);
				});
			}), TargetEnemyMinion, ownFx: true));
			cards.Add("TTN_737te", new CardDef(new Power { Enchant = new Enchant(GameTag.HEALTH, EffectOperator.ADD) }));
			// [TTN_737t3] Runes of Frost — The next spell you cast costs (3) less and has Spell Damage +3. (TTN_737e on the player: the
			// cost on the spells in hand shown as TTN_737e1, Spell Damage on the player — Talented Arcanist's BAR_064e — both until a
			// spell of theirs is cast.)
			cards.Add("TTN_737t3", TitansAbility(new AddEnchantmentTask("TTN_737e", EntityType.CONTROLLER)));
			cards.Add("TTN_737e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("TTN_737t3", "discount", 3))) { Condition = SelfCondition.IsSpell, ShowAs = "TTN_737e1" },
				Enchant = new Enchant(new Effect(GameTag.SPELLPOWER, EffectOperator.ADD, V("TTN_737t3", "spell damage", 3))),
				Trigger = new Trigger(TriggerType.AFTER_CAST) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = RemoveEnchantmentTask.Task }
			}));
			// [TTN_737t1] Runes of the Unholy — Summon two 3/3 Undead with Taunt and Reborn. (TTN_737t2, beside the Titan.)
			cards.Add("TTN_737t1", TitansAbility(new CustomTask((g, c, s, t, stack) =>
			{
				for (int i = 0; i < V("TTN_737t1", "undead", 2); i++) Summon(g, c, Cards.FromId("TTN_737t2"), s);
			})));
			cards.Add("TTN_737t2", EngineOnly); // Servant of the Primus: Taunt, Reborn (data)

			// ================================================================ Demon Hunter

			// [TTN_840] Crystalline Statue (2) — Rush. Starts Dormant. After you draw 4 cards, this awakens. (Starting Dormant is the
			// engine's; each of its owner's draws while it is Dormant counts on its TTN_840e1 counter — Generic.AwakenProgress, as
			// Stockades Prisoner; awake, it stops listening.)
			cards.Add("TTN_840", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DRAW)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					WhileDormant = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me)) return;
						if (me[GameTag.DORMANT] > 0) Generic.AwakenProgress(me, 1, V("TTN_840", "draws", 4));
						if (me[GameTag.DORMANT] == 0) me.ActivatedTrigger?.Remove();
					})
				}
			}));

			// [TTN_841] Momentum (5) — Give your hero +4 Attack this turn. Costs (1) less for each card you've drawn this turn.
			cards.Add("TTN_841", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => V("TTN_841", "discount", 1) * p.Controller.NumCardsDrawnThisTurn),
				PowerTask = new AddEnchantmentTask("TTN_841e1", EntityType.HERO)
			}));
			cards.Add("TTN_841e1", OneTurn(Effects.Attack_N(V("TTN_841", "attack", 4))));

			// [TTN_842] Jotun, the Eternal (5) — Battlecry: For the rest of the game, cast a copy of the first spell you draw each turn
			// at enemies. (TTN_842t1 on the player — live ATTACHED = the player, live-20261005/112456.)
			cards.Add("TTN_842", new CardDef(new Power { PowerTask = new AddEnchantmentTask("TTN_842t1", EntityType.CONTROLLER) }));
			// [TTN_842t1] Jotun's Swiftness — Casts a copy of the first spell you draw each turn at enemies. (Either player's turn; the
			// first spell drawn this turn is marked at the draw, also before Jotun was played — wiki bug fix; the drawn spell stays in
			// hand, a NEW copy is cast at enemies: Generic.CastDrawnCopy, live 6849-6950.)
			cards.Add("TTN_842t1", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DRAW)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => Generic.FirstSpellDrawnThisTurn(p)),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t != null && s is IPlayable granter) Generic.CastDrawnCopy(c, granter, t, true, true);
					})
				}
			}));

			// [TTN_844] Argunite Golem (3) — Has +1 Attack for each card you've drawn this turn. (Its client enchantment TTN_844e
			// "Shining: Increased Attack." — ENRAGED 1 in the card data — shows on it while the bonus is above 0: a SELF marker aura,
			// Restless, ShowAs; the Attack is the AdaptiveEffect's. DOUBT: unrecorded, live-checks.)
			cards.Add("TTN_844", new CardDef(new Power
			{
				Aura = new MultiAura(
					new AdaptiveEffect(GameTag.ATK, EffectOperator.ADD, p => V("TTN_844", "attack", 1) * p.Controller.NumCardsDrawnThisTurn),
					new Aura(AuraType.SELF)
					{
						Condition = new SelfCondition(p => p.Zone?.Type == Zone.PLAY && p.Controller.NumCardsDrawnThisTurn > 0),
						Restless = true,
						ShowAs = "TTN_844e"
					})
			}));

			// [TTN_845] Runic Adornment (2) — Discover a spell that costs (3) or less. Shuffle 2 copies into your deck that Cast When
			// Drawn. (Live live-20261007-vfx-decks 261197ff: the pick goes to hand; two NEW copies — COPIED_FROM_ENTITY_ID = the pick —
			// are shuffled in inside its SpawnToDeck effect, Source 0, each with TTN_845e.)
			cards.Add("TTN_845", new CardDef(new Power
			{
				OwnFx = true,
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL && x.Cost <= V("TTN_845", "cost", 3), null,
					new CustomTask((g, c, s, t, stack) =>
					{
						if (t == null || !(s is IPlayable adornment)) return;
						TitansC1Fx(adornment, "SpawnToDeck", true, () =>
						{
							for (int i = 0; i < V("TTN_845", "copies", 2); i++)
							{
								if (c.DeckZone.IsFull) break;
								IPlayable copy = Entity.FromCard(c, t.Card, new EntityData
								{
									[GameTag.CREATOR] = s.Id,
									[HsTag.COPIED_FROM_ENTITY_ID] = t.Id,
								});
								if (!Generic.ShuffleIntoDeck.Invoke(c, s, copy)) break;
								Give(g, s, copy, "TTN_845e");
							}
						});
					}))
			}));
			// [TTN_845e] Jotun's Haste — Cast When Drawn. (Drawn, the card is set aside, a NEW copy is cast — CREATOR = this
			// enchantment — and its owner draws the replacement, all in this enchantment's block: Generic.CastDrawnCopy, live
			// 265588-265720. TTN_845e1 "Jotun's Blessing" is referenced by no card: unused.)
			cards.Add("TTN_845e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DRAW)
				{
					TriggerSource = TriggerSource.ENCHANTMENT_TARGET,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t != null && s is IPlayable granter) Generic.CastDrawnCopy(c, granter, t, false, false);
					})
				}
			}));

			// [TTN_861] Disciple of Argus (3) — Deathrattle: Summon two 2/2 Elementals with Taunt. (TTN_862t4 Crystal Elemental.)
			cards.Add("TTN_861", new CardDef(new Power { DeathrattleTask = new SummonTask("TTN_862t4", V("TTN_861", "elementals", 2), SummonSide.DEATHRATTLE) }));

			// [YOG_401] Sigil of Time (3) — At the start of your next turn, draw 3 additional cards. (A Sigil: it waits in its owner's
			// hero zone — the Secret slots, visible, like Paladin Auras / Wildpaw Cavern (user) — and acts at their next
			// MAIN_START_TRIGGERS, before the turn's own draw: Generic.SigilPower.)
			cards.Add("YOG_401", Sigil("YOG_401", new DrawTask(V("YOG_401", "draw", 3))));

			// [YOG_402] Mindbender (4) — Battlecry: Deal 1 damage for each card you have drawn this turn. @ (@) (A targeted Battlecry —
			// the text names no "random" (user rule); one hit — DOUBT, unrecorded. Its "(N)" in hand: Power.HandCounter.)
			cards.Add("YOG_402", new CardDef(BattlecryAny, new Power
			{
				HandCounter = p => p.Controller.NumCardsDrawnThisTurn,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int damage = V("YOG_402", "damage", 1) * c.NumCardsDrawnThisTurn;
					if (damage > 0 && t is ICharacter target) Generic.DamageCharFunc((IPlayable)s, target, damage, false);
				})
			}));

			// [YOG_521] Saronite Shambler (2) — Battlecry: If you've cast 5 or more spells this game, give your hero +4 Attack this turn.
			// @ ({0} left!)@ (Ready!)
			cards.Add("YOG_521", new CardDef(new Power
			{
				ThresholdCount = p => p.Controller.NumSpellsPlayedThisGame,
				PowerTask = ComplexTask.If((g, c, s) => Generic.ThresholdMet(s), new AddEnchantmentTask("YOG_521e", EntityType.HERO))
			}));
			cards.Add("YOG_521e", OneTurn(Effects.Attack_N(V("YOG_521", "attack", 4))));

			// [TTN_862] Argus, the Emerald Star (7) — Titan. Minions to the left of this have Rush, and ones to the right have Lifesteal.
			// (Every friendly minion on that side; a Restless board aura — positions change without zone moves. No client enchantment:
			// the keywords themselves.)
			MultiAura argusAura = null;
			argusAura = new MultiAura(
				new Aura(AuraType.BOARD, Effects.Rush) { Condition = new SelfCondition(p => TitansC1ArgusSide(p, argusAura, true)), Restless = true },
				new Aura(AuraType.BOARD, Effects.Lifesteal) { Condition = new SelfCondition(p => TitansC1ArgusSide(p, argusAura, false)), Restless = true });
			cards.Add("TTN_862", TitansTitan(new Power { Aura = argusAura }));
			// [TTN_862t1] Crystal Carving — Discover a Deathrattle minion. It costs (3) less. (The Discover's custom reveal is the
			// client's: TTNFX_ArgusTheEmeraldStar_Discover_CustomReveal.)
			cards.Add("TTN_862t1", TitansAbility(DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x[GameTag.DEATHRATTLE] > 0, "TTN_862t1e")));
			cards.Add("TTN_862t1e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("TTN_862t1", "discount", 3))) }));
			// [TTN_862t2] Show of Force — Reduce the Cost of all minions in your hand by (2). (Argus' HandBuff effect at them; TTN_862t3e
			// "Blessed by Argus: Costs (2) less" — its id says t3, its number is this ability's.)
			cards.Add("TTN_862t2", TitansAbility(new CustomTask((g, c, s, t, stack) =>
			{
				var minions = c.HandZone.Where(p => Generic.IsMinionCard(p)).ToList();
				if (minions.Count == 0) return;
				Generic.NamedFx((IPlayable)s, "HandBuff", minions.Select(m => m.Id), () => GiveAll(g, s, minions, "TTN_862t3e"));
			}), ownFx: true));
			cards.Add("TTN_862t3e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("TTN_862t2", "discount", 2))) }));
			// [TTN_862t3] Argunite Army — Summon four 2/2 Elementals with Taunt. (Inside Argus' OverrideCustomSpawn, from nowhere as
			// live's Titan spawns; they spread to both sides of Argus — "2 Crystal Elementals to the right and 2 to the left",
			// PlayHearthstone 2023-07-05.)
			cards.Add("TTN_862t3", TitansAbility(new CustomTask((g, c, s, t, stack) =>
			{
				TitansC1Fx((IPlayable)s, "OverrideCustomSpawn", true, () =>
				{
					for (int i = 0; i < V("TTN_862t3", "elementals", 4); i++) Summon(g, c, Cards.FromId("TTN_862t4"), s);
				});
			}), ownFx: true));
			cards.Add("TTN_862t4", EngineOnly); // Crystal Elemental: Taunt (data)

			// ================================================================ Yogg-Saron, Unleashed

			// [YOG_516] Yogg-Saron, Unleashed (9) — Titan. After this uses an ability, cast two random spells. (Live
			// live-20261006-yogg-cthun: the casts inside the Titan's TRIGGER block, CastRandomSpellTask's shape.)
			cards.Add("YOG_516", TitansTitan(afterAbility: new CustomTask((g, c, s, t, stack) =>
			{
				for (int i = 0; i < V("YOG_516", "spells", 2); i++) new CastRandomSpellTask().Process(g, c, s, null);
			})));
			// [YOG_516t] Reign of Chaos — Take control of an enemy minion. (Its effect: Yogg's ReignOfChaos prefab, the engine's.)
			cards.Add("YOG_516t", TitansAbility(new ControlTask(EntityType.TARGET), TargetEnemyMinion, notInHistory: true /* live: never ShowInHistory (titans-vfx 190857 / 174206 captures) */));
			// [YOG_516t2] Induce Insanity — Force each enemy minion to attack a random enemy minion. (In order of play; one with no other
			// enemy minion left, or gone, skips.)
			cards.Add("YOG_516t2", TitansAbility(new CustomTask((g, c, s, t, stack) =>
			{
				EventMetaData outer = g.CurrentEventData;
				foreach (Minion attacker in c.Opponent.BoardZone.GetAll().OrderBy(m => m.OrderOfPlay).ToList())
				{
					if (attacker.IsDead || attacker.ToBeDestroyed || attacker.Untouchable || attacker.Zone?.Type != Zone.PLAY) continue;
					var others = c.Opponent.BoardZone.GetAll()
						.Where(m => m.Id != attacker.Id && !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList();
					if (others.Count == 0) continue;
					EffectAttack(g, attacker, others[g.Random.Next(others.Count)]);
				}
				g.CurrentEventData = outer;
			}), notInHistory: true /* live: never ShowInHistory (titans-vfx 190857 / 174206 captures) */));
			// [YOG_516t3] Tentacle Swarm — Fill your hand with 1/1 Chaotic Tendrils.
			cards.Add("YOG_516t3", TitansAbility(new CustomTask((g, c, s, t, stack) =>
			{
				while (TitansC1AddTendril(c, s)) { }
			})));

			// ================================================================ Cho'gall's anomalies (TTN_002t*)
			// Each anomaly's script applies its rule to both players when Cho'gall starts it (YOG_530). Rules that last are a player
			// enchantment on each player (its hero the creator, so the enchantment is theirs). DOUBT: no recording of any anomaly.

			// [TTN_002t1] Surging Inspiration — Both players start with upgraded Hero Powers. (The client's UPGRADED_HERO_POWER, as
			// Justicar Trueheart.)
			cards.Add("TTN_002t1", new CardDef(new Power
			{
				PowerTask = TitansC1BothPlayers((g, player, s) =>
				{
					HeroPower power = player.Hero?.HeroPower;
					if (power == null) return;
					int upgraded = SabberStoneCore.Loader.ReprintCards.UpgradedHeroPower(power.Card, player.Hero.Card);
					if (upgraded > 0 && Cards.FromAssetId(upgraded) is Card better)
						new ReplaceHeroPower(better).Process(g, player, s, null);
				})
			}));
			// [TTN_002t5] Gift of Gluttony — Both players start with two more drawn cards.
			cards.Add("TTN_002t5", new CardDef(new Power
			{
				PowerTask = TitansC1BothPlayers((g, player, s) =>
				{
					for (int i = 0; i < V("TTN_002t5", "draw", 2); i++) Generic.Draw(player);
				})
			}));
			// [TTN_002t9] Light in the Dark — Both players' Hero Powers cost (1) less. (TTN_002t9e on each player, shown on the Hero
			// Power.)
			cards.Add("TTN_002t9", new CardDef(new Power { PowerTask = TitansC1BothPlayers((g, player, s) => TitansC1PlayerEnchant(g, player, "TTN_002t9e")) }));
			cards.Add("TTN_002t9e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HEROPOWER, Effects.ReduceCost(V("TTN_002t9", "discount", 1))) { ShowAs = "TTN_002t9e" }
			}));
			// [TTN_002t11] Uncontrollable Growth — Both players start with an extra Mana Crystal. (An empty one now: filled at their
			// first turn.)
			cards.Add("TTN_002t11", new CardDef(new Power
			{
				PowerTask = TitansC1BothPlayers((g, player, s) => Generic.ChangeManaCrystal.Invoke(player, V("TTN_002t11", "crystals", 1), false))
			}));
			// [TTN_002t14] Approaching Nightmare — Both players start with Yogg-Saron, Hope's End in their hand. (OG_134.)
			cards.Add("TTN_002t14", new CardDef(new Power { PowerTask = TitansC1BothPlayers((g, player, s) => AddToHand(player, Cards.FromId("OG_134"))) }));
			// [TTN_002t15] Driven to Excess — Whenever a player ends their turn with unspent Mana, they draw a card.
			cards.Add("TTN_002t15", new CardDef(new Power { PowerTask = TitansC1BothPlayers((g, player, s) => TitansC1PlayerEnchant(g, player, "TTN_002t15e")) }));
			cards.Add("TTN_002t15e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { Condition = new SelfCondition(p => p.Controller.RemainingMana > 0), SingleTask = new DrawTask() }
			}));
			// [TTN_002t20] Driven to Greed — Whenever a player ends their turn with unspent Mana, they get a Coin. (GAME_005.)
			cards.Add("TTN_002t20", new CardDef(new Power { PowerTask = TitansC1BothPlayers((g, player, s) => TitansC1PlayerEnchant(g, player, "TTN_002t20e")) }));
			cards.Add("TTN_002t20e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					Condition = new SelfCondition(p => p.Controller.RemainingMana > 0),
					SingleTask = new CustomTask((g, c, s, t, stack) => AddToHand(c, Cards.FromId("GAME_005")))
				}
			}));
			// [TTN_002t21] Twist Reality — The first time a player plays a card on their turn, shuffle a copy of it into their deck.
			cards.Add("TTN_002t21", new CardDef(new Power { PowerTask = TitansC1BothPlayers((g, player, s) => TitansC1PlayerEnchant(g, player, "TTN_002t21e")) }));
			cards.Add("TTN_002t21e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p.Game.CurrentPlayer == p.Controller && p.Controller.NumCardsPlayedThisTurn == 1),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t != null && !c.DeckZone.IsFull) Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, t.Card));
					})
				}
			}));
			// [TTN_002t22] Seeing Double — Both players start with 2 cards copied from the other player's hand. (Both picks first, from
			// the hands as they were.)
			cards.Add("TTN_002t22", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Controller first = g.CurrentPlayer ?? c;
					int n = V("TTN_002t22", "cards", 2);
					var picks = new[] { first, first.Opponent }.Select(who => (who, RandomOf(g, who.Opponent.HandZone.ToList(), n))).ToList();
					foreach ((Controller player, List<IPlayable> copied) in picks)
						foreach (IPlayable card in copied)
							if (!player.HandZone.IsFull) Generic.Copy(player, s, card, Zone.HAND);
				})
			}));
			// [TTN_002t24] Severed Souls — Both heroes start with +5 Health.
			cards.Add("TTN_002t24", new CardDef(new Power
			{
				PowerTask = TitansC1BothPlayers((g, player, s) =>
				{
					if (player.Hero != null) player.Hero.BaseHealth = player.Hero.BaseHealth + V("TTN_002t24", "health", 5);
				})
			}));
			// [TTN_002t30] Fortifications — At the end of each players' turns, give a random one of their minions +1 Health.
			// (TTN_002t30e on each player; the buff is TTN_02t30e1 "Fortified" — the client's odd id, named after no card: loaded
			// because this script defines it, ReprintCards.ScriptedClientCard.)
			cards.Add("TTN_002t30", new CardDef(new Power { PowerTask = TitansC1BothPlayers((g, player, s) => TitansC1PlayerEnchant(g, player, "TTN_002t30e")) }));
			cards.Add("TTN_002t30e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					Condition = new SelfCondition(p => p.Controller.BoardZone.Any(m => !m.IsDead && !m.Untouchable)),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (Cards.TryFromId("TTN_02t30e1") is Card fortified && RandomFriendly(g, c, m => !m.Untouchable) is Minion lucky)
							Generic.AddEnchantmentBlock(g, fortified, (IPlayable)s, lucky, 0, 0, 0);
					})
				}
			}));
			cards.Add("TTN_02t30e1", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("TTN_002t30", "health", 1))) }));
			// [TTN_002t32] Fast Track — Reduce the Cost of all cards in both player's starting hands by (1). (GBL_003e "Costs (1) less".)
			cards.Add("TTN_002t32", new CardDef(new Power { PowerTask = TitansC1BothPlayers((g, player, s) => GiveAll(g, s, player.HandZone.ToList(), "GBL_003e")) }));
			// [TTN_002t35] Your Eyes Betray You — Both players' cards are Golden. (PREMIUM on every card they have now; cards made later
			// stay normal — DOUBT.)
			cards.Add("TTN_002t35", new CardDef(new Power
			{
				PowerTask = TitansC1BothPlayers((g, player, s) =>
				{
					var all = player.HandZone.ToList().Concat(player.DeckZone.ToList()).Concat(player.BoardZone.GetAll().Cast<IPlayable>()).ToList();
					if (player.Hero != null) all.Add(player.Hero);
					if (player.Hero?.HeroPower != null) all.Add(player.Hero.HeroPower);
					foreach (IPlayable card in all)
						if (card[GameTag.PREMIUM] == 0) card[GameTag.PREMIUM] = 1;
				})
			}));
			// [TTN_002t36] Shifting Futures — Both players start with 2 copies of Shifter Zerus in their hand. (OG_123.)
			cards.Add("TTN_002t36", new CardDef(new Power
			{
				PowerTask = TitansC1BothPlayers((g, player, s) =>
				{
					for (int i = 0; i < V("TTN_002t36", "copies", 2); i++) AddToHand(player, Cards.FromId("OG_123"));
				})
			}));
			// [TTN_002t41] Control the Makers — Both heroes have +1 Attack on their turns. (TTN_002t41e on each player gives its hero
			// TTN_002t41e1 "+1 Attack this turn" at the start of each of their turns — the client marks t41e1 one-turn,
			// TAG_ONE_TURN_EFFECT.)
			cards.Add("TTN_002t41", new CardDef(new Power { PowerTask = TitansC1BothPlayers((g, player, s) => TitansC1PlayerEnchant(g, player, "TTN_002t41e")) }));
			cards.Add("TTN_002t41e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = new AddEnchantmentTask("TTN_002t41e1", EntityType.HERO) }
			}));
			cards.Add("TTN_002t41e1", OneTurn(Effects.Attack_N(V("TTN_002t41", "attack", 1))));
			// [TTN_002t43] Army of the Corrupted — The first minion played on each player's turns costs (1) less. (TTN_002t43e on each
			// player, shown on the minions as TTN_002t43e1, while they played no minion this turn.)
			cards.Add("TTN_002t43", new CardDef(new Power { PowerTask = TitansC1BothPlayers((g, player, s) => TitansC1PlayerEnchant(g, player, "TTN_002t43e")) }));
			cards.Add("TTN_002t43e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("TTN_002t43", "discount", 1)))
				{
					Condition = new SelfCondition(p => p is Minion && p.Game.CurrentPlayer == p.Controller && p.Controller.NumMinionsPlayedThisTurn == 0),
					Restless = true,
					ShowAs = "TTN_002t43e1"
				}
			}));
			// [TTN_002t44] Opportunity Knocks — The first card drawn on a player's turn is one they can afford to play. (At the start of
			// their turn, before its draw, a random card of their deck they can afford with this turn's Mana goes on top when the top
			// one isn't — DOUBT: live's way is unrecorded.)
			cards.Add("TTN_002t44", new CardDef(new Power { PowerTask = TitansC1BothPlayers((g, player, s) => TitansC1PlayerEnchant(g, player, "TTN_002t44e")) }));
			cards.Add("TTN_002t44e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable top = c.DeckZone.TopCard;
						if (top == null || top.Cost <= c.RemainingMana) return;
						var affordable = c.DeckZone.Where(x => x.Cost <= c.RemainingMana).ToList();
						if (affordable.Count == 0) return;
						IPlayable pick = affordable[g.Random.Next(affordable.Count)];
						c.DeckZone.Remove(pick);
						c.DeckZone.Add(pick); // the top
					})
				}
			}));
			// [TTN_002t45] Spread of Corruption — The first spell cast on each player's turns costs (1) less. (TTN_002t45e1 "Your spells
			// cost (1) less" on each player, while they cast no spell this turn.)
			cards.Add("TTN_002t45", new CardDef(new Power { PowerTask = TitansC1BothPlayers((g, player, s) => TitansC1PlayerEnchant(g, player, "TTN_002t45e1")) }));
			cards.Add("TTN_002t45e1", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("TTN_002t45", "discount", 1)))
				{
					Condition = new SelfCondition(p => p is Spell && p.Game.CurrentPlayer == p.Controller &&
					                                   !p.Controller.PlayHistory.Any(h => h.Turn == p.Game.Turn && h.SourceCard?.Type == CardType.SPELL)),
					Restless = true
				}
			}));
			// [TTN_002t50] Shifting Fate — Both players start with Gear Shift in their hand. (TTN_922.)
			cards.Add("TTN_002t50", new CardDef(new Power { PowerTask = TitansC1BothPlayers((g, player, s) => AddToHand(player, Cards.FromId("TTN_922"))) }));
		}
	}
}
