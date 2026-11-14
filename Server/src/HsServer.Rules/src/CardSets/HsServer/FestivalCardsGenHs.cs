// HsServer addition (not upstream SabberStone): Festival of Legends (2023) and its Audiopocalypse mini-set — the set's shared
// card pieces (Finale, Soloist, minion types played, improving instruments, "while this is alive" fans) and the neutral cards.
// Class cards: FestivalCardsGenHs2.cs (Death Knight … Paladin) and FestivalCardsGenHs3.cs (Priest … Warrior). The engine
// rules are in Actions/Festival.cs (Finale glow, Harmonic / Remixed changes in hand, Fatigue, Overheal counts). Every number
// goes through CardValues (V).
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
		// ---------------------------------------------------------------- shared pieces of the set

		/// <summary>"Finale: …" (Generic.Finale: no Mana left when the effect asks).</summary>
		private static ISimpleTask Finale(ISimpleTask then) => ComplexTask.If((g, c, s) => Generic.Finale(s), then);

		/// <summary>"Soloist" — "Battlecry: If you control no other minions, …" (the check first, so the card glows).</summary>
		private static ISimpleTask IfSoloist(ISimpleTask then) => ComplexTask.If((g, c, s) => Generic.Soloist(c, s), then);

		private static readonly Dictionary<PlayReq, int> TargetIfFinale =
			new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE_AND_FINALE, 0 } };

		/// <summary>The minion types (Quilboar 43, Naga 92 and Draenei 2 are the client's newer ones).</summary>
		private static readonly Race[] MinionTypes =
		{
			Race.MURLOC, Race.DEMON, Race.MECHANICAL, Race.ELEMENTAL, Race.BEAST, Race.TOTEM, Race.PIRATE, Race.DRAGON, Race.UNDEAD,
			(Race)43, (Race)92, (Race)2,
		};

		private static List<Race> TypesOf(Card card) => Generic.MinionTypesOf(card); // (the shared list: Actions/LostCity.cs)

		/// <summary>
		/// "Minions of a different type": the most of <paramref name="minions"/> that can each stand for a type no other one
		/// does (wiki The One-Amalgam Band: "Playing an All-type minion does not count as having every type played, it only
		/// counts as 1"; "for dual-type minions, they don't count as playing 2 minion types, only one"). Returns the chosen
		/// minions (a maximum matching of minions to types, the untyped left out), picked at random among equals.
		/// </summary>
		private static List<T> OfDifferentTypes<T>(Game g, IEnumerable<T> minions, System.Func<T, Card> cardOf)
		{
			var list = minions.Where(m => TypesOf(cardOf(m)).Count > 0).ToList();
			// random order first: which of several same-type minions is chosen is random
			for (int i = list.Count - 1; i > 0; i--)
			{
				int j = g.Random.Next(i + 1);
				(list[i], list[j]) = (list[j], list[i]);
			}
			var owner = new Dictionary<Race, int>(); // type -> index in list
			bool Assign(int i, HashSet<Race> seen)
			{
				foreach (Race r in TypesOf(cardOf(list[i])))
				{
					if (!seen.Add(r)) continue;
					if (!owner.TryGetValue(r, out int other) || Assign(other, seen))
					{
						owner[r] = i;
						return true;
					}
				}
				return false;
			}
			for (int i = 0; i < list.Count; i++) Assign(i, new HashSet<Race>());
			return owner.Values.Distinct().OrderBy(i => i).Select(i => list[i]).ToList();
		}

		/// <summary>"For each minion of a different type you've played this game" (the card itself not counted).</summary>
		private static int DifferentTypesPlayed(Game g, Controller c, IEntity self) =>
			OfDifferentTypes(g, c.PlayHistory.Where(h => h.SourceCard?.Type == CardType.MINION && h.SourceId != self.Id), h => h.SourceCard).Count;

		/// <summary>"(Play X while equipped to improve!)": the instrument's number (TAG_SCRIPT_DATA_NUM_1, its "@") goes up.</summary>
		private static void Improve(IEntity weapon, int by = 1)
		{
			if (weapon is Weapon w && w.Controller?.Hero?.Weapon?.Id == w.Id) w[GameTag.TAG_SCRIPT_DATA_NUM_1] += by;
		}

		/// <summary>An improving instrument's trigger: <paramref name="type"/> events from <paramref name="source"/> matching
		/// <paramref name="when"/> improve it while it is equipped.</summary>
		private static Trigger Improves(TriggerType type, TriggerSource source, SelfCondition when = null) => new Trigger(type)
		{
			TriggerSource = source,
			Condition = when,
			SingleTask = new CustomTask((g, c, s, t, stack) => Improve(s))
		};

		/// <summary>The instrument's number (its "@").</summary>
		private static int Num1(IEntity s) => s[GameTag.TAG_SCRIPT_DATA_NUM_1];

		/// <summary>
		/// "Choose a minion. It has X while this is alive" (Rowdy / Obsessive / Annoying Fan): the chosen minion gets the
		/// fan's enchantment, which leaves when the fan is no longer alive on the battlefield or is Silenced (checked at each
		/// death and turn change). The fan shows ETC_107e2 ("Has an extra effect from {0}").
		/// </summary>
		private static CardDef FanEnchant(params IEffect[] effects)
		{
			ISimpleTask check = new CustomTask((g, c, s, t, stack) =>
			{
				if (s is Enchantment e && (!(e.Creator is Minion fan) || fan.Zone?.Type != Zone.PLAY || fan.IsDead || fan.IsSilenced))
					e.Remove();
			});
			return new CardDef(new Power
			{
				Enchant = new Enchant(effects),
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.DEATH) { TriggerSource = TriggerSource.ALL, SingleTask = check },
					new Trigger(TriggerType.TURN_START) { EitherTurn = true, SingleTask = check },
					new Trigger(TriggerType.TURN_END) { EitherTurn = true, SingleTask = check })
			});
		}

		private static ISimpleTask FanBattlecry(string enchantment) => new CustomTask((g, c, s, t, stack) =>
		{
			if (!(t is Minion chosen) || !(s is Minion fan)) return;
			Give(g, s, chosen, enchantment);
			Give(g, s, fan, "ETC_107e2");
		});

		/// <summary>A random Bonus Effect the minion doesn't have yet, <paramref name="n"/> times (wiki Bonus effect: "Taunt,
		/// Windfury, Divine Shield, Poisonous, Elusive, Rush, Lifesteal, or Reborn") — the shared Generic.GainBonusEffects (WILD_WEST:
		/// the Rush rule, Elusive as the two "can't be targeted" tags).</summary>
		private static void GainBonusEffects(Game g, Minion me, int n) => Generic.GainBonusEffects(g, me, n);

		/// <summary>The opponent's next turn: their cards matching <paramref name="which"/> cost more (Cold Feet, Speaker
		/// Stomper; the Forensic Duster pattern, removed when the opponent's turn ends). The enchantment goes on the OPPONENT's
		/// player, controlled by the caster (live live-20261004-night/222041 3121-3143: JAM_034e ATTACHED = the opponent player,
		/// CONTROLLER = the caster): its OP_HAND aura and its removal are the caster's — EntityType.OP_CONTROLLER.</summary>
		private static CardDef OpponentNextTurnCost(int more, SelfCondition which, string shownOnCards) => new CardDef(new Power
		{
			Aura = new Aura(AuraType.OP_HAND, Effects.AddCost(more))
			{
				Condition = which,
				ShowAs = shownOnCards,
				RemoveTrigger = (TriggerType.TURN_END, SelfCondition.IsOpTurn)
			}
		});

		/// <summary>The cards the player played before <paramref name="self"/> (newest last).</summary>
		private static IEnumerable<PlayHistoryEntry> PlayedBefore(Controller c, IEntity self) => c.PlayHistory.Where(h => h.SourceId != self.Id);

		// ---------------------------------------------------------------- the set

		private static void Festival(IDictionary<string, CardDef> cards)
		{
			FestivalClasses1(cards);
			FestivalClasses2(cards);

			// ---------------------------------------------------------------- Neutral
			// [ETC_080] E.T.C., Band Manager (4) — While building your deck, assemble a band of 3 cards. Battlecry: Discover one!
			// The band is the deck's sideboard (client MAX_SIDEBOARD_CARDS 3; Actions/Sideboards.cs): Discover one of it; the pick
			// leaves the band for the rest of the game (wiki: "Once a card is picked to be discovered from E.T.C.'s sideboard, it
			// is removed from there for the rest of the game"). The band is the player's: a generated or stolen E.T.C. uses its
			// player's own, and none offers nothing (wiki: "which may be non-existent if you are not playing E.T.C. yourself").
			cards.Add("ETC_080", new CardDef(new Power { PowerTask = Generic.DiscoverFromSideboard() }));
			// [ETC_086] Amplified Elekk (10) — Taunt. Deathrattle: Deal 3 damage to all enemy minions.
			cards.Add("ETC_086", new CardDef(new Power { DeathrattleTask = new DamageTask(V("ETC_086", "damage", 3), EntityType.OP_MINIONS) }));
			// [ETC_087] Audio Amplifier (2) — Battlecry: Set your maximum Mana and hand size to 11. (MAXRESOURCES / MAXHANDSIZE:
			// Generic.ChangeManaCrystal and HandZone read the player's own limits.)
			cards.Add("ETC_087", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int max = V("ETC_087", "maximum", 11);
					c[GameTag.MAXRESOURCES] = max;
					c[GameTag.MAXHANDSIZE] = max;
				})
			}));
			// [ETC_088] Ghost Writer (5) — Battlecry: Discover a spell. Finale: Discover another.
			cards.Add("ETC_088", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL),
					Finale(DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL)))
			}));
			// [ETC_089] Static Waveform (3) — At the start of EACH turn, lose 1 Attack or Health (chosen randomly).
			cards.Add("ETC_089", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					EitherTurn = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion me) Give(g, s, me, g.Random.Next(2) == 0 ? "ETC_089e" : "ETC_089e2");
					})
				}
			}));
			cards.Add("ETC_089e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(-V("ETC_089", "loss", 1))) }));
			cards.Add("ETC_089e2", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(-V("ETC_089", "loss", 1))) }));
			// [ETC_098] Worgen Roadie (3) — Battlecry: Summon a 0/3 Instrument Case for your opponent. (Break it for a random weapon!)
			cards.Add("ETC_098", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Summon(g, c.Opponent, Cards.FromId("ETC_098t"), s))
			}));
			// [ETC_098t] Instrument Case — Deathrattle: Add a random weapon to your opponent's hand.
			cards.Add("ETC_098t", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => { if (RandomCard(g, x => x.Type == CardType.WEAPON) is Card w) AddToHand(c.Opponent, w); })
			}));
			// [ETC_099] Concert Promo-Drake (8) — Tradeable. Finale: Destroy an enemy minion.
			cards.Add("ETC_099", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_IF_AVAILABLE_AND_FINALE, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } }, new Power
			{
				PowerTask = Finale(new DestroyTask(EntityType.TARGET))
			}));
			// [ETC_101] Cowbell Soloist (3) — Battlecry: If you control no other minions, deal 2 damage.
			cards.Add("ETC_101", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE_AND_SOLOIST, 0 } }, new Power
			{
				PowerTask = IfSoloist(new DamageTask(V("ETC_101", "damage", 2), EntityType.TARGET))
			}));
			// [ETC_102] Air Guitarist (1) — Battlecry: Give your weapon +1 Durability.
			cards.Add("ETC_102", new CardDef(new Power { PowerTask = new AddEnchantmentTask("ETC_102e", EntityType.WEAPON) }));
			cards.Add("ETC_102e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.DURABILITY, EffectOperator.ADD, V("ETC_102", "durability", 1))) }));
			// [ETC_103] Hipster (2) — Battlecry: Discover a spell from your opponent's class that isn't in their deck.
			cards.Add("ETC_103", new CardDef(new Power
			{
				PowerTask = new DiscoverFromTask((g, c, s) =>
				{
					var inDeck = new HashSet<string>(c.Opponent.DeckZone.Select(p => p.Card.Id));
					return Cards.GenerationPool(g, c).Where(x => x.Type == CardType.SPELL && x.HasClass(c.Opponent.HeroClass) && !inDeck.Contains(x.Id));
				})
			}));
			// [ETC_104] Crowd Surfer (1) — Deathrattle: Give ANY other minion +1/+1 and this Deathrattle. (ETC_104e carries it.)
			ISimpleTask surf = new CustomTask((g, c, s, t, stack) =>
			{
				IPlayable dead = s is Enchantment e ? e.Target as IPlayable : s as IPlayable;
				var all = g.Player1.BoardZone.Concat(g.Player2.BoardZone).Where(m => m != dead && !m.IsDead && !m.ToBeDestroyed).ToList();
				if (all.Count > 0) Give(g, dead ?? (IPlayable)s, all[g.Random.Next(all.Count)], "ETC_104e");
			});
			cards.Add("ETC_104", new CardDef(new Power { DeathrattleTask = surf }));
			cards.Add("ETC_104e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ETC_104", "stats", 1))), DeathrattleTask = surf }));
			// [ETC_105] Stereo Totem (2) — At the end of your turn, give a random minion in your hand +2/+2.
			cards.Add("ETC_105", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = BuffRandomInHand(p => p is Minion, "ETC_105e") }
			}));
			cards.Add("ETC_105e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ETC_105", "stats", 2))) }));
			// [ETC_106] Frequency Oscillator (1) — Battlecry: The next Mech you play costs (1) less.
			cards.Add("ETC_106", new CardDef(new Power { PowerTask = new AddEnchantmentTask("ETC_106e", EntityType.CONTROLLER) }));
			cards.Add("ETC_106e", NextCardCost(Effects.ReduceCost(V("ETC_106", "discount", 1)), new SelfCondition(p => p is Minion && IsMech(p)), "GBL_003e"));
			// [ETC_107] Rowdy Fan (3) — Battlecry: Choose a minion. It has +4 Attack while this is alive.
			cards.Add("ETC_107", new CardDef(BattlecryMinion, new Power { PowerTask = FanBattlecry("ETC_107e") }));
			cards.Add("ETC_107e", FanEnchant(Effects.Attack_N(V("ETC_107", "attack", 4))));
			cards.Add("ETC_107e2", new CardDef(new Power { Enchant = new Enchant(new IEffect[0]) }));
			// [ETC_108] Obsessive Fan (4) — Battlecry: Choose a minion. It has Stealth while this is alive.
			cards.Add("ETC_108", new CardDef(BattlecryMinion, new Power { PowerTask = FanBattlecry("ETC_108e") }));
			cards.Add("ETC_108e", FanEnchant(Effects.StealthEff));
			// [ETC_109] Annoying Fan (1) — Battlecry: Choose a minion. It can't attack while this is alive.
			cards.Add("ETC_109", new CardDef(BattlecryMinion, new Power { PowerTask = FanBattlecry("ETC_109e") }));
			cards.Add("ETC_109e", FanEnchant(new Effect(GameTag.CANT_ATTACK, EffectOperator.SET, 1)));
			// [ETC_110] Cover Artist (4) — Battlecry: Transform into a 3/3 copy of a minion. (Faceless Manipulator's
			// TransformCopyTask, then ETC_110e sets 3/3.)
			cards.Add("ETC_110", new CardDef(BattlecryMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion target) || !(s is Minion me) || me.Zone?.Type != Zone.PLAY) return;
					new TransformCopyTask().Process(g, c, me, target);
					if (g.IdEntityDic.TryGetValue(me.Id, out IPlayable now) && now.Zone?.Type == Zone.PLAY) Give(g, now, now, "ETC_110e");
				})
			}));
			cards.Add("ETC_110e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("ETC_110", "stats", 3))) }));
			// [ETC_113] Photographer Fizzle (3) — Battlecry: Take a Snapshot of your current hand and shuffle it into your deck.
			// (Once per game.) The Snapshot keeps the cards (their dbf ids on it: HsTag.SNAPSHOT_CARD_1.., the count in its
			// TAG_SCRIPT_DATA_NUM_1 for its "{0} snapshotted cards"); Fizzle's Snapshot adds copies of them.
			cards.Add("ETC_113", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c[HsTag.FIZZLE_SNAPSHOT_TAKEN] > 0) return;
					c[HsTag.FIZZLE_SNAPSHOT_TAKEN] = 1;
					IPlayable snapshot = Entity.FromCard(c, Cards.FromId("ETC_113t"));
					var hand = c.HandZone.Where(p => p.Id != s.Id).Take(HsTag.SNAPSHOT_CARDS).ToList();
					for (int i = 0; i < hand.Count; i++) snapshot[HsTag.SNAPSHOT_CARD_1 + i] = hand[i].Card.AssetId;
					snapshot[GameTag.TAG_SCRIPT_DATA_NUM_1] = hand.Count;
					Generic.ShuffleIntoDeck.Invoke(c, s, snapshot);
				})
			}));
			cards.Add("ETC_113t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < HsTag.SNAPSHOT_CARDS; i++)
						if (s[HsTag.SNAPSHOT_CARD_1 + i] > 0 && Cards.FromAssetId(s[HsTag.SNAPSHOT_CARD_1 + i]) is Card card) AddToHand(c, card);
				})
			}));
			// [ETC_325] Audio Medic (2) — Rush. Finale: Gain Lifesteal.
			cards.Add("ETC_325", new CardDef(new Power
			{
				PowerTask = Finale(new CustomTask((g, c, s, t, stack) => { if (s is Minion m) m[GameTag.LIFESTEAL] = 1; }))
			}));
			// [ETC_326] Paparazzi (3) — Battlecry: Discover a Legendary minion.
			cards.Add("ETC_326", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY)
			}));
			// [ETC_336] Freebird (4) — Charge. Battlecry: Gain +1/+1 for each other Freebird you've played this game.
			cards.Add("ETC_336", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = PlayedBefore(c, s).Count(h => h.SourceCard?.Name == ((IPlayable)s).Card.Name);
					for (int i = 0; i < n; i++) Give(g, s, (IPlayable)s, "ETC_336e");
				})
			}));
			cards.Add("ETC_336e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ETC_336", "stats", 1))) }));
			// [ETC_349] Unpopular Has-Been (6) — Deathrattle: Summon a random 5-Cost minion from the past. (Wiki Toki,
			// Time-Tinker: "from the past" is "any card set that has become exclusive to Wild format".)
			cards.Add("ETC_349", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					int cost = V("ETC_349", "cost", 5);
					Card self = Cards.GeneratingCard(g);
					var pool = Cards.All.Where(x => x.Collectible && x.Type == CardType.MINION && x.Cost == cost && Cards.FromThePast(x)
					                                && x.Id != self?.Id && !x.NeverRandomlyOnBoard).ToList();
					if (pool.Count > 0) Summon(g, c, pool[g.Random.Next(pool.Count)], s);
				})
			}));
			// [ETC_350] Party Animal (2) — Battlecry: Give +1/+1 to each minion of a different type in your hand.
			cards.Add("ETC_350", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, OfDifferentTypes(g, c.HandZone.Where(p => Generic.IsMinionCard(p)), p => p.Card), "ETC_350e"))
			}));
			cards.Add("ETC_350e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ETC_350", "stats", 1))) }));
			// [ETC_409] The One-Amalgam Band (7) — Battlecry: Gain a random Bonus Effect for each minion of a different type
			// you've played this game.
			cards.Add("ETC_409", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GainBonusEffects(g, s as Minion, DifferentTypesPlayed(g, c, s)))
			}));
			// [ETC_418] Instrument Tech (2) — Battlecry: Draw a weapon.
			cards.Add("ETC_418", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p is Weapon)) }));
			// [ETC_419] Mish-Mash Mosher (8) — Rush. After this attacks, gain +1 Attack and attack a random enemy minion.
			cards.Add("ETC_419", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me) || me.IsDead || me.ToBeDestroyed || me.Zone?.Type != Zone.PLAY) return;
						Give(g, s, me, "ETC_419e");
						var enemies = c.Opponent.BoardZone.Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList();
						if (enemies.Count > 0) Generic.AttackBlock.Invoke(c, me, enemies[g.Random.Next(enemies.Count)], true, true);
					})
				}
			}));
			cards.Add("ETC_419e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("ETC_419", "attack", 1))) }));
			// [ETC_420] Outfit Tailor (3) — Battlecry: Give a friendly minion Attack and Health equal to this minion's.
			cards.Add("ETC_420", new CardDef(BattlecryFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m && s is Minion me)
						Generic.AddEnchantmentBlock(g, Cards.FromId("ETC_420e"), me, m, me.AttackDamage, me.Health, 0);
				})
			}));
			cards.Add("ETC_420e", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackHealthScriptTag }));
			// [ETC_422] Metrognome (3) — After you play a {0}-Cost card, draw a {1}-Cost card. (Then increase!) (Its numbers are
			// its TAG_SCRIPT_DATA_NUM_1 / _2, 1 and 2; both go up by 1 each time.)
			cards.Add("ETC_422", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(g.CurrentEventData?.EventSource is IPlayable played) || !(s is Minion me) || PlayedCost(played) != me[GameTag.TAG_SCRIPT_DATA_NUM_1]) return;
						int draw = me[GameTag.TAG_SCRIPT_DATA_NUM_2];
						DrawWhere(g, c, p => p.Cost == draw);
						me[GameTag.TAG_SCRIPT_DATA_NUM_1] += 1;
						me[GameTag.TAG_SCRIPT_DATA_NUM_2] += 1;
					})
				}
			}));
			// [ETC_425] Pozzik, Audio Engineer (4) — Battlecry: Add two 3/3 Bots to your opponent's hand. Deathrattle: Summon them
			// for yourself. (Wiki: "Pozzik only summons the Audio Bots from the opponent's hand (or deck, if they're shuffled
			// there)"; played ones stay theirs. Its Bots: CREATOR = this Pozzik.)
			cards.Add("ETC_425", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("ETC_425", "bots", 2); i++) AddToHand(c.Opponent, Cards.FromId("ETC_425t"));
				}),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var bots = c.Opponent.HandZone.Concat(c.Opponent.DeckZone).Where(p => p.Card.Id == "ETC_425t" && p[GameTag.CREATOR] == s.Id).ToList();
					foreach (IPlayable bot in bots)
					{
						if (c.BoardZone.IsFull) break;
						Generic.GiveTo(bot, c);
						Generic.SummonBlock.Invoke(g, (Minion)bot, -1, s);
					}
				})
			}));
			// [ETC_540] Pyrotechnician (4) — After you cast a spell, add a random Fire spell to your hand.
			cards.Add("ETC_540", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (RandomCard(g, x => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == FIRE) is Card fire) AddToHand(c, fire);
					})
				}
			}));
			// [ETC_541] Tony, King of Piracy (7) — Battlecry: Replace your deck with a copy of your opponent's. Finale: Draw a card.
			cards.Add("ETC_541", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) =>
					{
						var theirs = c.Opponent.DeckZone.Select(p => p.Card).ToList();
						foreach (IPlayable old in c.DeckZone.ToList())
							c.SetasideZone.Add(c.DeckZone.Remove(old));
						foreach (Card card in theirs)
							if (!c.DeckZone.IsFull) c.DeckZone.Add(Entity.FromCard(c, card));
						c.DeckZone.Shuffle();
					}),
					Finale(new DrawTask()))
			}));
			// [ETC_542] Festival Security (3) — Taunt. Finale: Force all enemy minions to attack this.
			cards.Add("ETC_542", new CardDef(new Power
			{
				PowerTask = Finale(new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion enemy in c.Opponent.BoardZone.ToList())
					{
						if (!(s is Minion me) || me.IsDead || me.ToBeDestroyed || me.Zone?.Type != Zone.PLAY) break;
						if (enemy.IsDead || enemy.ToBeDestroyed || enemy.AttackDamage <= 0 || enemy.CantAttack) continue;
						Generic.AttackBlock.Invoke(c.Opponent, enemy, me, true, true);
					}
				}))
			}));
			// [ETC_543] Candleraiser (4) — Divine Shield. Finale: Give adjacent minions Divine Shield.
			cards.Add("ETC_543", new CardDef(new Power
			{
				PowerTask = Finale(new CustomTask((g, c, s, t, stack) =>
				{
					if (s is Minion me) foreach (Minion m in Neighbors(me).ToList()) m.HasDivineShield = true;
				}))
			}));
			// [ETC_742] Rolling Stone (2) — Rush. Battlecry: If the last card you played costs (1), gain +1/+1.
			cards.Add("ETC_742", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => PlayedBefore(c, s).LastOrDefault() is { SourceCard: { } } last && last.Cost == V("ETC_742", "cost", 1),
					new AddEnchantmentTask("ETC_742e", EntityType.SOURCE))
			}));
			cards.Add("ETC_742e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ETC_742", "stats", 1))) }));
			// [JAM_033] Remixed Musician (3) — Rush. Gains an extra effect in your hand that changes each turn. (Generic.
			// RemixOnEnteringHand / HandChangesAtTurnEnd; its versions' keywords are their card data.)
			cards.Add("JAM_033", new CardDef(new Power { NoScript = true }));
			foreach (string v in new[] { "JAM_033t", "JAM_033t2", "JAM_033t3", "JAM_033t4" })
				cards.Add(v, new CardDef(new Power { NoScript = true }));
			// [JAM_034] Speaker Stomper (4) — Tradeable. Battlecry: Enemy spells cost (2) more next turn.
			cards.Add("JAM_034", new CardDef(new Power { PowerTask = new AddEnchantmentTask("JAM_034e", EntityType.OP_CONTROLLER) }));
			cards.Add("JAM_034e", OpponentNextTurnCost(V("JAM_034", "more", 2), SelfCondition.IsSpell, "JAM_034e2"));
			// [JAM_035] Grimtotem Buzzkill (4) — Battlecry: Discard a weapon to draw 3 cards. (A random weapon in hand.)
			cards.Add("JAM_035", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var weapons = c.HandZone.Where(p => p is Weapon).ToList();
					if (weapons.Count == 0) return;
					Generic.DiscardBlock.Invoke(c, weapons[g.Random.Next(weapons.Count)]);
					for (int i = 0; i < V("JAM_035", "draw", 3); i++) Generic.Draw(c);
				})
			}));
			// [JAM_036] Magatha, Bane of Music (5) — Battlecry: Draw 5 cards. Give any spells drawn to your opponent. (A steal is
			// a zone move: Generic.GiveTo; a full hand burns it.)
			cards.Add("JAM_036", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var spells = new List<IPlayable>();
					for (int i = 0; i < V("JAM_036", "draw", 5); i++)
						if (Generic.Draw(c) is Spell drawn && drawn.Zone?.Type == Zone.HAND) spells.Add(drawn);
					foreach (IPlayable spell in spells)
					{
						Generic.GiveTo(spell, c.Opponent);
						if (!c.Opponent.HandZone.IsFull) Generic.AddHandPhase.Invoke(c.Opponent, spell);
						else c.Opponent.GraveyardZone.Add(spell);
					}
				})
			}));
			// [JAM_037] Elite Tauren Champion (5) — Finale: Start a ROCK DUEL! Players must spend all their Mana each turn or
			// else they take 8 (or more) damage! (Wiki: "Molten Pick of ROCK is generated for the opposing player"; "The Pick
			// gets transferred to the other player's hand immediately as the current holder spends all their mana"; it comes
			// back "with +2 damage"; "If a player is holding the Pick and didn't spend all their mana that turn, the spell is
			// removed from the game and the holding player takes the damage"; a full hand: "the Pick is removed from the game
			// instead and the effect doesn't trigger".)
			cards.Add("JAM_037", new CardDef(new Power
			{
				PowerTask = Finale(new CustomTask((g, c, s, t, stack) => GivePick(c.Opponent, null)))
			}));
			ISimpleTask passPick = new CustomTask((g, c, s, t, stack) =>
			{
				if (!(s is Spell pick) || pick.Zone?.Type != Zone.HAND || g.CurrentPlayer != c || c.RemainingMana != 0) return;
				GivePick(c.Opponent, pick);
			});
			cards.Add("JAM_037t", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.AFTER_PLAY_CARD) { TriggerActivation = TriggerActivation.HAND, TriggerSource = TriggerSource.FRIENDLY, SingleTask = passPick },
					new Trigger(TriggerType.INSPIRE) { TriggerActivation = TriggerActivation.HAND, TriggerSource = TriggerSource.FRIENDLY, SingleTask = passPick },
					new Trigger(TriggerType.TURN_END)
					{
						TriggerActivation = TriggerActivation.HAND,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (!(s is Spell pick) || pick.Zone?.Type != Zone.HAND) return;
							int damage = pick[GameTag.TAG_SCRIPT_DATA_NUM_1];
							c.SetasideZone.Add(c.HandZone.Remove(pick));
							Generic.DamageCharFunc(pick, c.Hero, damage, false);
						})
					})
			}));
		}

		/// <summary>Rock Duel: the Molten Pick goes to <paramref name="to"/>'s hand (unplayable; its damage +2 each pass), or is
		/// removed with a full hand.</summary>
		private static void GivePick(Controller to, Spell pick)
		{
			int damage = pick == null ? Cards.FromId("JAM_037t")[GameTag.TAG_SCRIPT_DATA_NUM_1] : pick[GameTag.TAG_SCRIPT_DATA_NUM_1] + V("JAM_037", "more damage", 2);
			if (pick != null)
				pick.Controller.SetasideZone.Add(pick.Controller.HandZone.Remove(pick));
			if (to.HandZone.IsFull) return;
			IPlayable next = Entity.FromCard(to, Cards.FromId("JAM_037t"));
			next[GameTag.TAG_SCRIPT_DATA_NUM_1] = damage;
			next[HsTag.LITERALLY_UNPLAYABLE] = 1;
			Generic.AddHandPhase.Invoke(to, next);
		}
	}
}
