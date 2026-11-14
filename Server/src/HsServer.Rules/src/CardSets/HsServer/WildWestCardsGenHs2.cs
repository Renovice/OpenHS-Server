// HsServer addition (not upstream SabberStone): Showdown in the Badlands / Delve into Deepholm — chunk 1: Neutral, Death Knight
// and Demon Hunter cards (spec: research spec_chunk1; shared pieces: WildWestCardsGenHs.cs, Actions/WildWest.cs). Every number
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
		/// <summary>Minion: the bullets Gattlesnake has loaded (rule state; WW_431e only shows them).</summary>
		private const GameTag WildWestC1Bullets = (GameTag)990256;

		/// <summary>Minion: Ogre-Gang Rider rolled "buff the hero instead" for this attack.</summary>
		private const GameTag WildWestC1RiderInstead = (GameTag)990257;

		/// <summary>Flint Firearm: a random Quickdraw card to the hand, marked "if you play it this turn" (WW_379e on the player,
		/// NUM_1 = the card; the TTN_317 pattern: live-20261005 140614 57060-57460).</summary>
		private static void WildWestC1Flint(Game g, Controller c, IEntity source)
		{
			if (c.HandZone.IsFull || !(RandomCard(g, x => x[HsTag.QUICKDRAW] > 0) is Card quick)) return;
			IPlayable card = Entity.FromCard(c, quick, new EntityData { [GameTag.CREATOR] = source.Id });
			if (!Generic.AddHandPhase.Invoke(c, card)) return;
			card[GameTag.DISPLAYED_CREATOR] = source.Id;
			Generic.MarkPlayThisTurn(card, source);
			Generic.AddEnchantmentBlock(g, Cards.FromId("WW_379e"), (IPlayable)source, c, card.Id, 0, 0);
		}

		/// <summary>Up to <paramref name="n"/> shots of <paramref name="damage"/> at a random living enemy each (one target round per
		/// shot).</summary>
		private static void WildWestC1Shots(Game g, Controller c, IEntity source, int n, int damage)
		{
			for (int i = 0; i < n; i++)
			{
				var enemies = Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed).ToList();
				if (enemies.Count == 0) break;
				g.PowerHistory?.NewTargetRound();
				Generic.DamageCharFunc((IPlayable)source, enemies[g.Random.Next(enemies.Count)], damage, false);
			}
		}

		private static void WildWestChunk1(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Delve into Deepholm
			// [DEEP_012] Shadestone Skulker (1) — Rush. Battlecry: Take your weapon and gain its stats. Deathrattle: Give it back.
			// (Generic.BorrowWeapon: the weapon leaves without being destroyed — wiki; the Skulker takes its Attack and the
			// Durability left as Health; DEEP_012e "Holding {0}" (NUM_1 = the weapon) carries the give-back Deathrattle, so a
			// Skulker that borrowed nothing has none — live-20261005 140614 606-2034; once per Skulker: wiki patch 28.4.1.)
			cards.Add("DEEP_012", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me) || !(Generic.BorrowWeapon(c, s) is Weapon w)) return;
					Generic.AddEnchantmentBlock(g, Cards.FromId("DEEP_012e"), me, me, w.Id, 0, 0);
					me.AttackDamage = w.AttackDamage;
					me.BaseHealth = System.Math.Max(1, w.Durability);
				})
			}));
			cards.Add("DEEP_012e", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => Generic.ReturnBorrowedWeapon(c, s))
			}));
			// [DEEP_013] Fel Fissure (4) — Deal $2 damage to all minions. At the start of your next turn, deal $2 more damage to all
			// minions. (Its Sigil DEEP_013t: live c3_li_1003_185040 110339-111910, straight into the Secret zone.)
			cards.Add("DEEP_013", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("DEEP_013", "damage", 2), EntityType.ALLMINIONS, true),
					new CustomTask((g, c, s, t, stack) => Generic.PutSigilIntoPlay(c, s, Cards.FromId("DEEP_013t"))))
			}));
			cards.Add("DEEP_013t", Sigil("DEEP_013t", new DamageTask(V("DEEP_013t", "damage", 2), EntityType.ALLMINIONS, true)));
			// [DEEP_015] Prosthetic Hand (3) — Magnetic, Reborn. Can Magnetize to Mechs or Undead. (Magnetic from the card data:
			// MAGNETIC_TO_RACE; the host gets its keywords.)
			cards.Add("DEEP_015", new CardDef(new Power { NoScript = true }));
			// [DEEP_016] Quartzite Crusher (4, weapon) — Lifesteal. Freeze any character damaged by your hero. (Its own FREEZE:
			// Generic.HeroFreezesWhatItDamages.)
			cards.Add("DEEP_016", new CardDef(new Power { NoScript = true }));
			// [DEEP_017] Mining Casualties (2) — Summon two Silver Hand Recruits with "Deathrattle: Summon a 1/1 Frail Ghoul".
			cards.Add("DEEP_017", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("DEEP_017", "recruits", 2) && !c.BoardZone.IsFull; i++)
					{
						var recruit = (Minion)Entity.FromCard(c, Cards.FromId("CS2_101t"));
						Generic.SummonBlock.Invoke(g, recruit, -1, s);
						if (recruit.Zone?.Type == Zone.PLAY) Give(g, s, recruit, "DEEP_017e");
					}
				})
			}));
			cards.Add("DEEP_017e", new CardDef(new Power { DeathrattleTask = new SummonTask("HERO_11bpt", SummonSide.DEATHRATTLE) }));
			// [DEEP_034] Shale Spider (2) — Battlecry: If you played an Elemental last turn, draw a card.
			cards.Add("DEEP_034", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new ConditionTask(EntityType.SOURCE, SelfCondition.ElementalPlayedLastTurn), new FlagTask(true, new DrawTask()))
			}));
			// [DEEP_035] Iridescent Gyreworm (3) — Deathrattle: Give each of your minions a random Bonus Effect.
			cards.Add("DEEP_035", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().Where(m => m != s && !m.IsDead && !m.ToBeDestroyed).ToList())
						Generic.GainBonusEffects(g, m, V("DEEP_035", "effects", 1));
				})
			}));
			// [DEEP_036] Therazane (7) — Taunt. Deathrattle: Double the stats of all Elementals in your hand and deck. (DEEP_036e:
			// + its current Attack / Health, Lor'themar's way.)
			cards.Add("DEEP_036", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable p in c.HandZone.Concat(c.DeckZone).Where(x => Generic.IsMinionCard(x) && IsRaceCard(x, Race.ELEMENTAL)).ToList())
						Generic.AddEnchantmentBlock(g, Cards.FromId("DEEP_036e"), (IPlayable)s, p, ((Minion)p).AttackDamage, ((Minion)p).Health, 0);
				})
			}));
			cards.Add("DEEP_036e", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackHealthScriptTag }));
			// [DEEP_037] Maruut Stonebinder (7) — Battlecry: If your deck started with no duplicates, Discover an Elemental to summon.
			// Add the others to your hand. (World Pillar Fragment's helper — wiki trivia: the same outcome.)
			cards.Add("DEEP_037", new CardDef(new Power
			{
				PowerTask = WildWestIfNoDuplicates(WildWestDiscoverToSummonOthersToHand(x => x.Type == CardType.MINION && x.IsRace(Race.ELEMENTAL)))
			}));

			// ---------------------------------------------------------------- Showdown in the Badlands: Neutral
			// [WW_025] Azerite Giant (8) — Costs (1) less for each turn in a row you've played an Elemental.
			cards.Add("WW_025", new CardDef(new Power { Aura = new AdaptiveCostEffect(p => Generic.ElementalStreak(p.Controller)) }));
			// [WW_044] Tram Mechanic (1) — Deathrattle: Get a Barrel of Sludge.
			cards.Add("WW_044", new CardDef(new Power { DeathrattleTask = new AddCardTo("WW_044t", EntityType.HAND) }));
			// [WW_044t] Barrel of Sludge (3) — When this is played, discarded, or destroyed, deal $4 damage to the lowest Health enemy.
			cards.Add("WW_044t", new CardDef(WildWestPlayedDiscardedOrDestroyed(() => new CustomTask((g, c, s, t, stack) =>
			{
				if (LowestHealthEnemy(g, c) is ICharacter low) Generic.DamageCharFunc((IPlayable)s, low, V("WW_044t", "damage", 4), true);
			}))));
			// [WW_300] Trapdoor Spider (2) — Stealth, Poisonous. After your opponent plays a minion, attack it. (Gankster's trigger.)
			cards.Add("WW_300", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.ENEMY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventSource is Minion played && played.Zone?.Type == Zone.PLAY && !played.IsDead && s is Minion me && !me.IsDead)
							EffectAttack(g, me, played);
					})
				}
			}));
			// [WW_331] Miracle Salesman (1) — Deathrattle: Get a Tradeable Snake Oil.
			cards.Add("WW_331", new CardDef(new Power { DeathrattleTask = new AddCardTo("WW_331t", EntityType.HAND) }));
			// [WW_331t] Snake Oil (0) — Tradeable. Deal $0 damage. This seems useless... (Spell Damage makes it hit.)
			cards.Add("WW_331t", new CardDef(TargetAny, new Power { PowerTask = new DamageTask(0, EntityType.TARGET, true) }));
			// [WW_332] Snake Oil Seller (4) — Deathrattle: Shuffle 2 Tradeable Snake Oils into your opponent's deck.
			cards.Add("WW_332", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("WW_332", "oils", 2); i++)
						Generic.ShuffleIntoDeck.Invoke(c.Opponent, s, Entity.FromCard(c.Opponent, Cards.FromId("WW_331t"), new EntityData { [GameTag.CREATOR] = s.Id }));
				})
			}));
			// [WW_333] Howdyfin (2) — Whenever your hand has less than 3 cards in it, get a random Murloc. (A state check: it keeps
			// refilling to 3 — wiki note, RidiculousHat.)
			cards.Add("WW_333", new CardDef(new Power
			{
				StateCheck = Generic.HandBelow(V("WW_333", "hand", 3)),
				StateTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.MURLOC)) is Card murloc) AddToHand(c, murloc);
				})
			}));
			// [WW_351] Cattle Rustler (5) — Battlecry: Draw a Beast. It costs (3) less.
			cards.Add("WW_351", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (DrawOne(g, c, p => p is Minion && IsRaceCard(p, Race.BEAST)) is IPlayable beast) Give(g, s, beast, "WW_351e");
				})
			}));
			cards.Add("WW_351e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("WW_351", "discount", 3))) }));
			// [WW_359] Sheriff Barrelbrim (4) — Battlecry: If you have 20 or less Health, open the Badlands Jail.
			cards.Add("WW_359", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.Hero.Health <= V("WW_359", "health", 20),
					new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("WW_359t"), s)))
			}));
			// [WW_359t] Badlands Jail (Location) — Make a minion go Dormant for 3 turns. (No counter enchantment in the data.)
			cards.Add("WW_359t", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m && m.Zone?.Type == Zone.PLAY && !(m is Location)) Generic.MakeDormant(m, V("WW_359t", "turns", 3), null, (IPlayable)s);
				})
			}));
			// [WW_360] Azerite Chain Gang (4) — Taunt. Battlecry and Quickdraw: Summon a copy of this.
			cards.Add("WW_360", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new SummonCopyTask(EntityType.SOURCE), WildWestIfQuickdraw(new SummonCopyTask(EntityType.SOURCE)))
			}));
			// [WW_373] Reska, the Pit Boss (20) — Rush. Costs (1) less for each minion that died this game. Deathrattle: Take control of
			// a random enemy minion.
			cards.Add("WW_373", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.DiedThisGameCards.Concat(p.Controller.Opponent.DiedThisGameCards).Count(x => x is Minion)),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList();
					if (enemies.Count > 0) new ControlTask(EntityType.TARGET).Process(g, c, s, enemies[g.Random.Next(enemies.Count)]);
				})
			}));
			// [WW_379] Flint Firearm (2) — Battlecry: Get a random Quickdraw card. If you play it this turn, repeat this.
			cards.Add("WW_379", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => WildWestC1Flint(g, c, s)) }));
			cards.Add("WW_379e", new CardDef(new Power
			{
				Enchant = new Enchant(new IEffect[0]) { IsOneTurnEffect = true },
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p.Controller.AppliedEnchantments?.Any(e => e.Card.Id == "WW_379e" && e[GameTag.TAG_SCRIPT_DATA_NUM_1] == p.Id) == true),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable played = t ?? g.CurrentEventData?.EventSource as IPlayable;
						if (!(s is Enchantment mark) || played == null || mark[GameTag.TAG_SCRIPT_DATA_NUM_1] != played.Id) return;
						IPlayable flint = mark.Creator ?? (IPlayable)c.Hero;
						mark.Remove();
						WildWestC1Flint(g, c, flint);
					})
				}
			}));
			// [WW_383] Dryscale Deputy (2) — Battlecry: The next time you draw a spell, get a copy of it.
			cards.Add("WW_383", new CardDef(new Power { PowerTask = new AddEnchantmentTask("WW_383e", EntityType.CONTROLLER) }));
			cards.Add("WW_383e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DRAW)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p is Spell),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable drawn = g.CurrentEventData?.EventSource as IPlayable ?? t;
						if (!(s is Enchantment e) || !(drawn is Spell)) return;
						e.Remove();
						if (!c.HandZone.IsFull) Generic.Copy(c, e.Creator ?? (IPlayable)c.Hero, drawn, Zone.HAND);
					})
				}
			}));
			// [WW_391] Gold Panner (2) — At the end of your turn, draw a card.
			cards.Add("WW_391", new CardDef(new Power { Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new DrawTask() } }));
			// [WW_397] Dang-Blasted Elemental (4) — Taunt. Deathrattle: Deal 2 damage to all minions except friendly Elementals.
			cards.Add("WW_397", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
					DamageAll(s, g.Player1.BoardZone.GetAll().Concat(g.Player2.BoardZone.GetAll())
						.Where(m => !m.IsDead && !(m.Controller == c && m.IsRace(Race.ELEMENTAL))).Cast<ICharacter>().ToList(), V("WW_397", "damage", 2), false))
			}));
			// [WW_398] Gaslight Gatekeeper (4) — Battlecry: Shuffle your hand into your deck, then draw that many cards. (Plot Twist's
			// tasks; a backward move wipes enchantments.)
			cards.Add("WW_398", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new IncludeTask(EntityType.HAND), new CountTask(EntityType.STACK), new MoveToDeck(EntityType.STACK), new DrawNumberTask())
			}));
			// [WW_399] High Noon Duelist (3) — Deathrattle: Both players DRAW! Destroy the card that costs less.
			cards.Add("WW_399", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => Generic.DuelDraw(c, s)) }));
			// [WW_418] Ogre-Gang Outlaw (3) — Rush. 50% chance to attack the wrong enemy.
			cards.Add("WW_418", new CardDef(new Power { Trigger = Forgetful("WW_418") }));
			// [WW_419] Ogre-Gang Rider (4) — Rush. 50% chance to give your hero +3 Attack this turn instead of attacking. (The roll in
			// its ATTACK trigger, FORGETFUL keyword like the recorded Ace; Power.AttackInstead acts on it: the attack is used up.)
			cards.Add("WW_419", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					KeywordOverride = GameTag.FORGETFUL,
					SingleTask = new CustomTask((g, c, s, t, stack) => s[WildWestC1RiderInstead] = g.Random.Next(100) < V("WW_419", "chance %", 50) ? 1 : 0)
				},
				AttackInstead = (g, attacker, defender) =>
				{
					if (attacker[WildWestC1RiderInstead] == 0) return null;
					attacker[WildWestC1RiderInstead] = 0;
					return () => Generic.AddEnchantmentBlock(g, Cards.FromId("WW_419e"), (IPlayable)attacker, attacker.Controller.Hero, 0, 0, 0);
				}
			}));
			cards.Add("WW_419e", OneTurn(Effects.Attack_N(V("WW_419", "attack", 3))));
			// [WW_420] Ogre-Gang Ace (5) — Rush. Whenever this attacks, gain Divine Shield. (50% chance to gain Lifesteal instead.)
			// (Live ×5: TRIGGER FORGETFUL in the attack, a plain keyword tag, no enchantment.)
			cards.Add("WW_420", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					KeywordOverride = GameTag.FORGETFUL,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me)) return;
						if (g.Random.Next(100) < V("WW_420", "lifesteal chance %", 50)) me[GameTag.LIFESTEAL] = 1;
						else me.HasDivineShield = true;
					})
				}
			}));
			// [WW_421] Kingpin Pud (6) — Battlecry: Resurrect your Ogre-Gang. Give them Windfury. (The Ogre-Gang: its client related
			// cards; plus Boulderfist Ogre — wiki "as an 'easter egg', Boulderfist Ogre can also be resurrected". Each death once.)
			cards.Add("WW_421", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var gang = new HashSet<string>(ReprintCards.RelatedCardIds(Cards.SelfOf(s)?.Id ?? "WW_421") ?? new List<string>());
					foreach (IPlayable dead in c.DiedThisGameCards.Where(p => p is Minion && (gang.Contains(p.Card.Id) || p.Card.Name == "Boulderfist Ogre")).ToList())
					{
						if (c.BoardZone.IsFull) break;
						var back = (Minion)Generic.NewFromGraveyard(c, dead);
						Generic.SummonBlock.Invoke(g, back, -1, s);
						if (back.Zone?.Type == Zone.PLAY) back[GameTag.WINDFURY] = 1;
					}
				})
			}));
			// [WW_423] Saloon Brewmaster (2) — Battlecry: Return a friendly minion to your hand. Give it +2/+2.
			cards.Add("WW_423", new CardDef(BattlecryFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m && m.Zone?.Type == Zone.PLAY && Generic.ReturnToHand(c, m) is IPlayable back) Give(g, s, back, "WW_423e");
				})
			}));
			cards.Add("WW_423e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("WW_423", "stats", 2))) }));
			// [WW_428] Eroded Sediment (3) — Battlecry: If you played an Elemental last turn, Discover any Elemental from the past.
			cards.Add("WW_428", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new ConditionTask(EntityType.SOURCE, SelfCondition.ElementalPlayedLastTurn),
					new FlagTask(true, new DiscoverFromTask((g, c, s) => Cards.AllWild.Where(x => x.Type == CardType.MINION && x.IsRace(Race.ELEMENTAL) && Cards.FromThePast(x)))))
			}));
			// [WW_431] Gattlesnake (3) — At the end of your turn, load two bullets that deal 1 damage each. Deathrattle: Fire at random
			// enemies! (The count in a minion tag; one WW_431e per bullet shows them. They add up.)
			cards.Add("WW_431", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me)) return;
						for (int i = 0; i < V("WW_431", "bullets", 2); i++)
						{
							me[WildWestC1Bullets] += 1;
							Give(g, s, me, "WW_431e");
						}
					})
				},
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => WildWestC1Shots(g, c, s, s[WildWestC1Bullets], V("WW_431", "damage", 1)))
			}));
			cards.Add("WW_431e", new CardDef(new Power { Enchant = new Enchant(new IEffect[0]) }));
			// [WW_433] Linedance Partner (3) — Battlecry: If you're holding another 3-Cost card, summon a random 3-Cost minion.
			cards.Add("WW_433", new CardDef(new Power
			{
				PowerTask = WildWestIfHoldingOtherOfCost(V("WW_433", "cost", 3), new CustomTask((g, c, s, t, stack) => Summon(g, c, RandomMinionOfCost(g, V("WW_433", "cost", 3)), s)))
			}));
			// [WW_434] Sunspot Dragon (6) — Tradeable, Lifesteal. Quickdraw: Deal 6 damage.
			cards.Add("WW_434", new CardDef(WildWestQuickdrawTarget, new Power
			{
				PowerTask = WildWestIfQuickdraw(new DamageTask(V("WW_434", "damage", 6), EntityType.TARGET))
			}));
			// [WW_435] Bunny Stomper (2) — Your Beasts have Rush.
			cards.Add("WW_435", new CardDef(new Power
			{
				Aura = new Aura(AuraType.BOARD, "WW_435e") { Condition = new SelfCondition(p => p is Minion m && m.IsRace(Race.BEAST)) }
			}));
			cards.Add("WW_435e", new CardDef(new Power { Enchant = new Enchant(Effects.Rush) }));
			// [WW_440] Thunderbringer (8) — Taunt. Deathrattle: Summon an Elemental and Beast from your deck.
			cards.Add("WW_440", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					SummonFromDeck(g, c, s, p => IsRaceCard(p, Race.ELEMENTAL));
					SummonFromDeck(g, c, s, p => IsRaceCard(p, Race.BEAST));
				})
			}));
			// [WW_827] Whelp Wrangler (2) — At the end of your turn, get a 1/2 Whelp with Taunt.
			cards.Add("WW_827", new CardDef(new Power { Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new AddCardTo("WW_816t", EntityType.HAND) } }));
			// [WW_900] Horseshoe Slinger (2) — Battlecry: Deal 2 damage to a random enemy minion. Quickdraw: And one of its neighbors.
			cards.Add("WW_900", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList();
					if (enemies.Count == 0) return;
					Minion hit = enemies[g.Random.Next(enemies.Count)];
					var neighbors = Neighbors(hit).Where(m => !m.IsDead && !m.Untouchable).ToList();
					int damage = V("WW_900", "damage", 2);
					Generic.DamageCharFunc((IPlayable)s, hit, damage, false);
					if (SelfCondition.IsQuickdraw.Eval((IPlayable)s) && neighbors.Count > 0)
					{
						g.PowerHistory?.NewTargetRound();
						Generic.DamageCharFunc((IPlayable)s, neighbors[g.Random.Next(neighbors.Count)], damage, false);
					}
				})
			}));
			// [WW_901] Greedy Partner (2) — Battlecry: If you're holding another 2-Cost card, get a Coin.
			cards.Add("WW_901", new CardDef(new Power { PowerTask = WildWestIfHoldingOtherOfCost(V("WW_901", "cost", 2), new AddCardTo("GAME_005", EntityType.HAND)) }));
			// [WW_906] Rowdy Partner (4) — Battlecry: If you're holding another 4-Cost card, deal 4 damage.
			cards.Add("WW_906", new CardDef(WildWestHoldingCostTarget(4), new Power
			{
				PowerTask = WildWestIfHoldingOtherOfCost(V("WW_906", "cost", 4), new DamageTask(V("WW_906", "damage", 4), EntityType.TARGET))
			}));

			// ---------------------------------------------------------------- Death Knight
			// [WW_354] Fistful of Corpses (1) — Deal damage to a minion equal to your Corpses. (Not spent; no "$": no Spell Damage.)
			cards.Add("WW_354", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is ICharacter m) Generic.DamageCharFunc((IPlayable)s, m, c.Corpses, false); })
			}));
			// [WW_358] Farm Hand (3) — Battlecry: Discover an Undead. Quickdraw: It costs (2) less.
			cards.Add("WW_358", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.IsRace(Race.UNDEAD), null,
					new CustomTask((g, c, s, t, stack) => { if (t != null && s is IPlayable me && SelfCondition.IsQuickdraw.Eval(me)) Give(g, s, t, "WW_358e"); }))
			}));
			cards.Add("WW_358e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("WW_358", "discount", 2))) }));
			// [WW_368] Crop Rotation (3) — Summon four 1/1 Undead with Rush that die at the end of turn.
			cards.Add("WW_368", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = ComplexTask.Repeat(new SummonTask("WW_368t", SummonSide.SPELL), V("WW_368", "undead", 4))
			}));
			cards.Add("WW_368t", new CardDef(new Power { Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new DestroyTask(EntityType.SOURCE) } }));

			// ---------------------------------------------------------------- Demon Hunter
			// [WW_400] Snake Eyes (3) — Battlecry: Roll two dice, then Discover two cards of those Costs. (Doubles get an extra
			// Discover!) (Wiki: two of the "Rolled a N!" cards at random; doubles: three Discovers of that Cost.)
			cards.Add("WW_400", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int a = 1 + g.Random.Next(6), b = 1 + g.Random.Next(6);
					var rolls = a == b ? new[] { a, a, a } : new[] { a, b };
					foreach (int cost in rolls)
					{
						int n = cost;
						DiscoverFromTask.ClassOrNeutral((c2, x) => x.Cost == n).Process(g, c, s, null, stack);
					}
				})
			}));
			// [WW_401] Gunslinger Kurtrus (5) — Battlecry: If your deck started with no duplicates, fire six 2 damage shots at minions
			// in the enemy's hand.
			cards.Add("WW_401", new CardDef(new Power
			{
				PowerTask = WildWestIfNoDuplicates(new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("WW_401", "shots", 6); i++)
					{
						var minions = c.Opponent.HandZone.Where(p => Generic.IsMinionCard(p)).ToList();
						if (minions.Count == 0) break;
						Generic.DamageInHand(minions[g.Random.Next(minions.Count)], V("WW_401", "damage", 2), s);
					}
				}))
			}));
			// [WW_402] Blindeye Sharpshooter (3) — After you play a Naga, deal 2 damage to a random enemy and draw a spell. (Then
			// switch!) / After you cast a spell, deal 2 damage to a random enemy and draw a Naga. (Then switch!) (TSC_620's switch;
			// a doubled cast is one cast: one AFTER_CAST.)
			cards.Add("WW_402", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.AFTER_PLAY_MINION)
					{
						TriggerSource = TriggerSource.FRIENDLY,
						Condition = new SelfCondition(IsNaga),
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (s[GameTag.USE_ALTERNATE_CARD_TEXT] != 0) return;
							WildWestC1Shots(g, c, s, 1, V("WW_402", "damage", 2));
							DrawWhere(g, c, p => p is Spell);
							s[GameTag.USE_ALTERNATE_CARD_TEXT] = 1;
						})
					},
					new Trigger(TriggerType.AFTER_CAST)
					{
						TriggerSource = TriggerSource.FRIENDLY,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (s[GameTag.USE_ALTERNATE_CARD_TEXT] != 1) return;
							WildWestC1Shots(g, c, s, 1, V("WW_402", "damage", 2));
							DrawWhere(g, c, p => p is Minion && IsNaga(p));
							s[GameTag.USE_ALTERNATE_CARD_TEXT] = 0;
						})
					})
			}));
			// [WW_403] Pocket Sand (2) — Deal $3 damage. Quickdraw: Your opponent's next card costs (1) more. (Live ×6: the Quickdraw
			// in its second POWER block; WW_403e on the opponent, each of their cards marked WW_403e1.)
			cards.Add("WW_403", new CardDef(TargetAny, new Power
			{
				PowerTask = new DamageTask(V("WW_403", "damage", 3), EntityType.TARGET, true),
				SecondPowerTask = WildWestIfQuickdraw(new AddEnchantmentTask("WW_403e", EntityType.OP_CONTROLLER))
			}));
			cards.Add("WW_403e", NextCardCost(Effects.AddCost(V("WW_403", "more", 1)), new SelfCondition(p => true), "WW_403e1", opponent: true));
			// [WW_404] Oasis Outlaws (1) — Discover a Naga. If you've played a Naga while holding this, reduce its Cost by (1).
			cards.Add("WW_404", new CardDef(new Power
			{
				Trigger = NagaWhileHolding,
				ThresholdValue = 1,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					DiscoverFromTask.ClassOrNeutral((c2, x) => x.Type == CardType.MINION && x.IsRace(NAGA), Held(s) ? GenericCostLess(V("WW_404", "discount", 1)) : null)
						.Process(g, c, s, null, stack))
			}));
			// [WW_405] Fan the Hammer (4) — Deal $6 damage split among the lowest Health enemies. (Spell Damage adds shots; each shot
			// re-picks the lowest-Health enemy.)
			cards.Add("WW_405", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int shots = Generic.SpellDamageAmount((IPlayable)s, V("WW_405", "damage", 6));
					for (int i = 0; i < shots; i++)
					{
						if (!(LowestHealthEnemy(g, c) is ICharacter low)) break;
						g.PowerHistory?.NewTargetRound();
						Generic.DamageCharFunc((IPlayable)s, low, 1, false);
					}
				})
			}));
			// [WW_406] Midnight Wolf (6) — Rush. Outcast: Summon a copy of this.
			cards.Add("WW_406", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new ConditionTask(EntityType.SOURCE, PlayedAsOutcast), new FlagTask(true, new SummonCopyTask(EntityType.SOURCE)))
			}));
			// [WW_407] Parched Desperado (2) — Battlecry: If you've cast a spell while holding this, give your hero +3 Attack this turn.
			cards.Add("WW_407", new CardDef(HeldSpell(p => p is Spell, new AddEnchantmentTask("WW_407e", EntityType.HERO))));
			cards.Add("WW_407e", OneTurn(Effects.Attack_N(V("WW_407", "attack", 3))));
			// [WW_408] Bartend-O-Bot (2) — Battlecry: Draw an Outcast card and slide it to the left side of your hand.
			cards.Add("WW_408", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (DrawOne(g, c, p => p.Card[OUTCAST] > 0) is IPlayable drawn) Generic.MoveInHand(drawn, 0);
				})
			}));
			// [WW_409] Load the Chamber (3) — Deal $2 damage. Your next Naga, Fel spell, and weapon cost (1) less.
			cards.Add("WW_409", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("WW_409", "damage", 2), EntityType.TARGET, true),
					new AddEnchantmentTask("WW_409e", EntityType.CONTROLLER), new AddEnchantmentTask("WW_409e2", EntityType.CONTROLLER),
					new AddEnchantmentTask("WW_409e3", EntityType.CONTROLLER))
			}));
			int loaded = V("WW_409", "discount", 1);
			cards.Add("WW_409e", NextCardCost(Effects.ReduceCost(loaded), new SelfCondition(p => p is Minion && IsNaga(p)), "WW_409e1"));
			cards.Add("WW_409e2", NextCardCost(Effects.ReduceCost(loaded), new SelfCondition(p => p is Spell && p[SPELL_SCHOOL] == FEL), "WW_409e1"));
			cards.Add("WW_409e3", NextCardCost(Effects.ReduceCost(loaded), new SelfCondition(p => p is Weapon), "WW_409e1"));
		}
	}
}
