// HsServer addition (not upstream SabberStone): Showdown in the Badlands / Delve into Deepholm — chunk 4: Shaman, Warrior and
// Warlock cards (spec: research spec_chunk4; shared pieces: WildWestCardsGenHs.cs, Actions/WildWest.cs). Every
// number goes through CardValues (V).
using System;
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
		/// <summary>Spell: the Cactus Cutter that drew it (Cactus Cutter: "If you cast it this turn").</summary>
		private const GameTag WildWestC4CutterOf = (GameTag)990258;

		/// <summary>Spell: the turn Cactus Cutter drew it.</summary>
		private const GameTag WildWestC4CutterTurn = (GameTag)990259;

		/// <summary>One random card of <paramref name="from"/> that matches <paramref name="where"/>, or null.</summary>
		private static IPlayable WildWestC4RandomOf(Game g, IEnumerable<IPlayable> from, Func<IPlayable, bool> where)
		{
			var list = from.Where(where).ToList();
			return list.Count == 0 ? null : list[g.Random.Next(list.Count)];
		}

		/// <summary>Opens the card's recorded effect named <paramref name="part"/> with <paramref name="targets"/> and closes it again
		/// at once (live: an empty SUB_SPELL before its step — Aftershocks' Landslide, Furnace Fuel's BuffImpact).</summary>
		private static void WildWestC4EmptyFx(Game g, IPlayable source, string part, IEnumerable<int> targets)
		{
			if (!g.History) return;
			if (g.OpenSubSpell(source, SabberStoneCore.Loader.SubSpells.FindNamed(source.Card.Id, part), targets,
				    shownSource: source is Spell ? source.Controller.Hero.Id : 0))
				g.CloseSubSpells(source.Id);
		}

		private static void WildWestChunk4(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Delve into Deepholm
			// [DEEP_008] Needlerock Totem (2) — At the end of your turn, gain 2 Armor and draw a card.
			cards.Add("DEEP_008", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = ComplexTask.Create(new ArmorTask(V("DEEP_008", "armor", 2)), new DrawTask())
				}
			}));
			// [DEEP_010] Aftershocks (4) — Deal $1 damage to all minions, three times. Costs (2) less if you cast a spell last turn.
			// (Wiki Aftershocks: Spell Damage applies to each instance; minions can die between them. Live live-20261005/184804
			// 7429-7536: per wave an empty Landslide SUB_SPELL at the living minions, the damage, a DEATHS block.)
			cards.Add("DEEP_010", new CardDef(new Power
			{
				OwnFx = true,
				Aura = new AdaptiveCostEffect(p => V("DEEP_010", "discount", 2), EffectOperator.SUB, SelfCondition.CastSpellLastTurn),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("DEEP_010", "waves", 3); i++)
					{
						var minions = c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll())
							.Where(m => !m.IsDead && !m.ToBeDestroyed).Cast<ICharacter>().ToList();
						if (minions.Count == 0) break;
						WildWestC4EmptyFx(g, (IPlayable)s, "Landslide", minions.Select(m => m.Id));
						DamageAll(s, minions, V("DEEP_010", "damage", 1), true);
						g.DeathProcessingAndAuraUpdate();
					}
				})
			}));
			// [DEEP_011] Burning Heart (1) — Deal $2 damage to a minion. If it survives, give your hero +3 Attack this turn. (Live ×3:
			// the Fireball missile, then the Rage BuffImpact around DEEP_011e on the hero.)
			cards.Add("DEEP_011", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Generic.DamageCharFunc((IPlayable)s, m, V("DEEP_011", "damage", 2), true);
					if (m.IsDead || m.ToBeDestroyed || m.Zone?.Type != Zone.PLAY) return;
					Generic.NamedFx((IPlayable)s, "BuffImpact", new[] { c.Hero.Id }, () => Give(g, s, c.Hero, "DEEP_011e"));
				})
			}));
			cards.Add("DEEP_011e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("DEEP_011", "attack", 3))) { IsOneTurnEffect = true } }));
			// [DEEP_019] Crimson Expanse (4, Location) — Choose a damaged minion. Summon a copy of it that goes Dormant for one turn.
			// (An exact copy — wiki patch 29.4.0; live-20261007-vfx-decks 240398-241147: right of a friendly original, DEEP_019e.)
			cards.Add("DEEP_019", new CardDef(new Dictionary<PlayReq, int>
			{
				{ PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_DAMAGED_TARGET, 0 }
			}, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m) || c.BoardZone.IsFull) return;
					int position = m.Controller == c && m.Zone?.Type == Zone.PLAY ? m.ZonePosition + 1 : -1;
					if (Generic.Copy(c, s, m, Zone.PLAY, position: position) is Minion copy)
						Generic.MakeDormant(copy, V("DEEP_019", "turns", 1), "DEEP_019e", (IPlayable)s);
				})
			}));
			// [DEEP_020] Deepminer Brann (6) — Battlecry: If your deck started with no duplicates, your Battlecries trigger twice for the
			// rest of the game. (EXTRA_BATTLECRIES_BASE: never three times with Brann Bronzebeard.)
			cards.Add("DEEP_020", new CardDef(new Power
			{
				PowerTask = WildWestIfNoDuplicates(new CustomTask((g, c, s, t, stack) =>
					Generic.NamedFx((IPlayable)s, "OnHero", new[] { c.Hero.Id }, () =>
						new AddEnchantmentTask("DEEP_020e", EntityType.CONTROLLER).Process(g, c, s, t))))
			}));
			cards.Add("DEEP_020e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.CONTROLLER, new Effect(GameTag.EXTRA_BATTLECRIES_BASE, EffectOperator.ADD, 1))
			}));
			// [DEEP_030] Elementium Geode (2) — Battlecry and Deathrattle: Draw a card. Deal 2 damage to your hero.
			cards.Add("DEEP_030", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new DrawTask(), new DamageTask(V("DEEP_030", "damage", 2), EntityType.HERO)),
				DeathrattleTask = ComplexTask.Create(new DrawTask(), new DamageTask(V("DEEP_030", "damage", 2), EntityType.HERO))
			}));
			// [DEEP_031] Chaos Creation (6) — Deal $6 damage. Summon a random 6-Cost minion. Destroy the bottom 6 cards of your deck.
			// (Live install-204752 130009-130233: no Battlecry for the summon; the Fel DeckAE around the 6 destroys only.)
			cards.Add("DEEP_031", new CardDef(TargetAny, new Power
			{
				OwnFx = true,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter target) Generic.DamageCharFunc((IPlayable)s, target, V("DEEP_031", "damage", 6), true);
					if (RandomMinionOfCost(g, V("DEEP_031", "cost", 6)) is Card summon) Summon(g, c, summon, s);
					Generic.DestroyBottom(c, s, V("DEEP_031", "cards", 6), "DeckAE");
				})
			}));
			// [DEEP_032] Soulfreeze (1) — Freeze a minion and its neighbors. Deal damage to your hero equal to the number Frozen. (Live
			// live-20261006-secrets-generators/224302 12999-13060: the neighbours then the target Frozen, the Frost Impact on the hero.)
			cards.Add("DEEP_032", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					var frozen = Neighbors(m).Where(n => n.Zone?.Type == Zone.PLAY && !n.Untouchable && !(n is Location)).Cast<Minion>().ToList();
					frozen.Add(m);
					foreach (Minion f in frozen) ComplexTask.Freeze(EntityType.TARGET).Process(g, c, s, f);
					Generic.NamedFx((IPlayable)s, "Frost_Impact", new[] { c.Hero.Id }, () =>
						Generic.DamageCharFunc((IPlayable)s, c.Hero, frozen.Count, false));
				})
			}));

			// ---------------------------------------------------------------- Shaman
			// [WW_010] Doctor Holli'dae (5) — Battlecry: If your deck started with no duplicates, equip the Staff of the Nine Frogs.
			cards.Add("WW_010", new CardDef(new Power { PowerTask = WildWestIfNoDuplicates(new WeaponTask("WW_010t")) }));
			// [WW_010t] Staff of the Nine Frogs — After your hero attacks, summon a {0}/{1} Frog with Taunt. (Each Frog is bigger than
			// the last!) (The weapon's TAG_SCRIPT_DATA_NUM_1 / _2 = the next Frog: rule state and the client's {0}/{1}.)
			cards.Add("WW_010t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Weapon staff)) return;
						SummonWithStats(g, c, Cards.FromId("WW_010hexfrog"), staff[GameTag.TAG_SCRIPT_DATA_NUM_1], staff[GameTag.TAG_SCRIPT_DATA_NUM_2], staff);
						int grow = V("WW_010t", "growth", 1);
						staff[GameTag.TAG_SCRIPT_DATA_NUM_1] += grow;
						staff[GameTag.TAG_SCRIPT_DATA_NUM_2] += grow;
					})
				}
			}));
			// [WW_024] Living Prairie (5) — Battlecry: If you played an Elemental last turn, summon two 3/3 Cows with Rush.
			cards.Add("WW_024", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new ConditionTask(EntityType.SOURCE, SelfCondition.ElementalPlayedLastTurn),
					new FlagTask(true, new CustomTask((g, c, s, t, stack) =>
					{
						for (int i = 0; i < V("WW_024", "cows", 2); i++) Summon(g, c, Cards.FromId("WW_024t"), s);
					})))
			}));
			// [WW_026] Skarr, the Catastrophe (7) — Battlecry: Deal @ damage to all enemies (improved by each turn in a row you've
			// played an Elemental).
			cards.Add("WW_026", new CardDef(new Power
			{
				HandCounter = WildWestStreakCounter,
				PowerTask = new CustomTask((g, c, s, t, stack) => DamageAll(s, Enemies(c), Generic.ElementalStreakCounter((IPlayable)s), false))
			}));
			// [WW_027] Trusty Companion (2) — Give a minion +2/+3. If it has a minion type, draw one of that type. (Wiki: no type, no
			// draw; a dual-type minion draws one of either type.)
			cards.Add("WW_027", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Give(g, s, m, "WW_027e");
					var types = Enum.GetValues(typeof(Race)).Cast<Race>()
						.Where(r => r != Race.INVALID && r != Race.BLANK && r != Race.ALL && m.Card.IsRace(r)).ToList();
					if (types.Count > 0) DrawOne(g, c, p => p is Minion && types.Any(r => p.Card.IsRace(r)));
				})
			}));
			cards.Add("WW_027e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("WW_027", "attack", 2)), Effects.Health_N(V("WW_027", "health", 3))) }));
			// [WW_080] Amphibious Elixir (2) — Restore #5 Health. Discover a spell.
			cards.Add("WW_080", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new HealTask(V("WW_080", "heal", 5), EntityType.TARGET),
					DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL))
			}));
			// [WW_090] Giant Tumbleweed!!! (7) — Deal $6 damage to all minions. Summon a 6/6 Tumbleweed.
			cards.Add("WW_090", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					DamageAll(s, c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => !m.IsDead).Cast<ICharacter>().ToList(),
						V("WW_090", "damage", 6), true);
					Summon(g, c, Cards.FromId("WW_090t"), s);
				})
			}));
			// [WW_325] Dehydrate (3) — Lifesteal. Deal $4 damage to a minion. Quickdraw: Costs (1).
			cards.Add("WW_325", new CardDef(TargetMinion, new Power
			{
				Aura = WildWestQuickdrawCost(V("WW_325", "cost", 1)),
				PowerTask = new DamageTask(V("WW_325", "damage", 4), EntityType.TARGET, true)
			}));
			// [WW_326] Minecart Cruiser (3) — Rush, Overload: (2) Battlecry: If you played an Elemental last turn, don't Overload.
			cards.Add("WW_326", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new ConditionTask(EntityType.SOURCE, SelfCondition.ElementalPlayedLastTurn),
					new FlagTask(true, new CustomTask((g, c, s, t, stack) => ((Entity)s)._data[HsTag.SKIP_OWN_OVERLOAD] = 1)))
			}));
			// [WW_327] Cactus Cutter (2) — Battlecry: Draw a spell. If you cast it this turn, gain +1/+2 and Taunt. (The spell carries
			// WW_327e this turn — Generic.MarkPlayThisTurn; casting it — a play, never an effect's cast — gives the Cutter WW_327e2
			// while it is in play.)
			cards.Add("WW_327", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(DrawOne(g, c, p => p is Spell) is IPlayable spell) || spell.Zone?.Type != Zone.HAND) return;
					spell[WildWestC4CutterOf] = s.Id;
					spell[WildWestC4CutterTurn] = g.Turn;
					Generic.MarkPlayThisTurn(spell, s, "WW_327e", GameTag.TAG_ONE_TURN_EFFECT, (GameTag)1054);
				}),
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable played = t ?? g.CurrentEventData?.EventSource as IPlayable;
						if (!(played is Spell) || played[WildWestC4CutterOf] != s.Id || played[WildWestC4CutterTurn] != g.Turn) return;
						played[WildWestC4CutterOf] = 0;
						if (s is Minion cutter && cutter.Zone?.Type == Zone.PLAY) Give(g, s, cutter, "WW_327e2");
					})
				}
			}));
			cards.Add("WW_327e", new CardDef(new Power { Enchant = new Enchant(new IEffect[0]) { IsOneTurnEffect = true } }));
			cards.Add("WW_327e2", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("WW_327", "attack", 1)), Effects.Health_N(V("WW_327", "health", 2)), new Effect(GameTag.TAUNT, EffectOperator.SET, 1))
			}));
			// [WW_382] Walking Mountain (9) — Rush, Lifesteal, Mega-Windfury Overload: (2) (keywords only)
			cards.Add("WW_382", new CardDef(new Power { PowerTask = Nothing }));

			// ---------------------------------------------------------------- Warrior
			// [WW_346] Blast Tortoise (6) — Taunt Battlecry: Deal damage to all enemy minions equal to this minion's Attack.
			cards.Add("WW_346", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (s is Minion me) DamageAll(s, c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead).Cast<ICharacter>().ToList(), me.AttackDamage, false);
				})
			}));
			// [WW_347] Battlepickaxe (3) — After you play a Taunt minion, gain +1 Durability. (Wiki: played, not summoned otherwise.)
			cards.Add("WW_347", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p is Minion m && m.HasTaunt),
					SingleTask = new AddEnchantmentTask("WW_347e", EntityType.SOURCE)
				}
			}));
			cards.Add("WW_347e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.DURABILITY, EffectOperator.ADD, V("WW_347", "durability", 1))) }));
			// [WW_348] Misfire (2) — Deal $3, $2, and $1 damage to random minions. Quickdraw: Choose the targets. (Live 140614
			// 81477-81700, Quickdraw: the play target takes the first, then two picks with deaths between; NUM_1 counts down.)
			cards.Add("WW_348", new CardDef(WildWestQuickdrawMinionTarget, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int[] hits = { V("WW_348", "first", 3), V("WW_348", "second", 2), V("WW_348", "third", 1) };
					if (SelfCondition.IsQuickdraw.Eval((IPlayable)s) && t is Minion first)
					{
						s[GameTag.TAG_SCRIPT_DATA_NUM_1] = hits.Length;
						Generic.DamageCharFunc((IPlayable)s, first, hits[0], true);
						g.DeathProcessingAndAuraUpdate();
						// (the shot number from the earlier picks: the pick may resolve on an AI's copy — Generic.PickTargetsInOrder)
						Generic.PickTargetsInOrder(c, s, hits.Length - 1, ch => ch is Minion && Generic.Pickable(ch.Game.CurrentPlayer, ch, true),
							(g2, c2, s2, ch, before) => Generic.DamageCharFunc((IPlayable)s2, ch, hits[Math.Min(1 + before.Count, hits.Length - 1)], true),
							different: false, counter: GameTag.TAG_SCRIPT_DATA_NUM_1);
						return;
					}
					foreach (int damage in hits)
					{
						var minions = c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll())
							.Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable && m.Health > 0).ToList();
						if (minions.Count == 0) break;
						g.PowerHistory?.NewTargetRound();
						Generic.DamageCharFunc((IPlayable)s, minions[g.Random.Next(minions.Count)], damage, true);
					}
				})
			}));
			// [WW_367] Unlucky Powderman (2) — Taunt Deathrattle: Give Taunt minions in your hand and deck +1/+1.
			cards.Add("WW_367", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.HandZone.GetAll().Concat(c.DeckZone.GetAll()).Where(p => p is Minion m && m.HasTaunt).ToList(), "WW_367e"))
			}));
			cards.Add("WW_367e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("WW_367", "stats", 1))) }));
			// [WW_372] Boomboss Tho'grun (8) — Battlecry: Shuffle 3 T.N.T. into your opponent's deck.
			cards.Add("WW_372", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("WW_372", "tnt", 3); i++)
						Generic.ShuffleIntoDeck.Invoke(c.Opponent, s, Entity.FromCard(c.Opponent, Cards.FromId("WW_372t")));
				})
			}));
			// [WW_372t] T.N.T. (6) — Casts When Drawn. Destroy a random card in your hand, deck, and board (except T.N.T.). (Wiki
			// Boomboss: "cannot destroy other TNT"; forum t/136383: any minion or Location on the board, never the weapon, a Dormant
			// minion or a permanent object.)
			ISimpleTask tnt = new CustomTask((g, c, s, t, stack) =>
			{
				Card self = ((IPlayable)s).Card;
				if (WildWestC4RandomOf(g, c.HandZone.GetAll(), p => p.Card != self && p != s) is IPlayable inHand)
					Generic.Destroy(inHand, s, many: false);
				if (WildWestC4RandomOf(g, c.DeckZone.GetAll(), p => p.Card != self) is IPlayable inDeck)
					Generic.Destroy(inDeck, s, many: false);
				if (WildWestC4RandomOf(g, c.BoardZone.GetAll(), p => p is Minion m && !m.IsDead && !m.ToBeDestroyed && !m.Untouchable && m[GameTag.DORMANT] == 0) is IPlayable onBoard)
					Generic.Destroy(onBoard, s, many: false);
			});
			cards.Add("WW_372t", new CardDef(new Power { PowerTask = tnt, TopdeckTask = tnt }));

			// ---------------------------------------------------------------- Warlock
			// [WW_041] Disposal Assistant (2) — Battlecry and Deathrattle: Put a Barrel of Sludge on the bottom of your deck.
			cards.Add("WW_041", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.PutOnBottom(c, s, Cards.FromId("WW_044t"))),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => Generic.PutOnBottom(c, s, Cards.FromId("WW_044t")))
			}));
			// [WW_042] Waste Remover (4) — At the end of your turn, destroy the bottom 3 cards of your deck.
			cards.Add("WW_042", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) => Generic.DestroyBottom(c, s, V("WW_042", "cards", 3)))
				}
			}));
			// [WW_043] Sludge on Wheels (3) — Rush. Whenever this takes damage, get a Barrel of Sludge and add one to the bottom of
			// your deck.
			cards.Add("WW_043", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						AddToHand(c, Cards.FromId("WW_044t"));
						Generic.PutOnBottom(c, s, Cards.FromId("WW_044t"));
					})
				}
			}));
			// [WW_091] Pop'gar the Putrid (4) — Your Fel spells cost (2) less and have Lifesteal. Battlecry: Get two Barrels of Sludge.
			// (Fel Lifesteal: a controller aura tag read by Spell.HasLifeSteal wherever the spell is — wiki patch 28.0.0: a discarded
			// Barrel too.)
			cards.Add("WW_091", new CardDef(new Power
			{
				Aura = new MultiAura(
					new Aura(AuraType.HAND, Effects.ReduceCost(V("WW_091", "discount", 2))) { Condition = new SelfCondition(p => p is Spell && p[SPELL_SCHOOL] == FEL) },
					new Aura(AuraType.CONTROLLER, new Effect(Generic.SpellSchoolLifestealTag(FEL), EffectOperator.ADD, 1))),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("WW_091", "barrels", 2); i++) AddToHand(c, Cards.FromId("WW_044t"));
				})
			}));
			// [WW_092] Fracking (1) — Look at the bottom 3 cards of your deck. Draw one and destroy the others. (The player picks:
			// user rule "not random unless it says so".)
			cards.Add("WW_092", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var bottom = c.DeckZone.GetAll().Take(V("WW_092", "cards", 3)).ToList();
					if (bottom.Count == 0) return;
					Generic.DiscoverFromDeck(c, s, bottom, (g2, c2, s2, card) =>
					{
						Generic.Draw(c2, card);
						foreach (IPlayable other in bottom.Where(p => p != card && p.Zone?.Type == Zone.DECK).ToList())
							Generic.Destroy(other, s2, many: false);
					});
				})
			}));
			// [WW_436] Trolley Problem (3) — Discard your lowest Cost spell. Summon two 3/3 Tram Cars with Rush. Quickdraw: Don't discard.
			cards.Add("WW_436", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!SelfCondition.IsQuickdraw.Eval((IPlayable)s))
					{
						var spells = c.HandZone.GetAll().Where(p => p is Spell).ToList();
						if (spells.Count > 0)
						{
							int lowest = spells.Min(p => p.Cost);
							var ties = spells.Where(p => p.Cost == lowest).ToList();
							IPlayable discard = ties[g.Random.Next(ties.Count)];
							Generic.NoteAffected(discard, s.Id);
							Generic.DiscardBlock(c, discard);
						}
					}
					for (int i = 0; i < V("WW_436", "cars", 2); i++) Summon(g, c, Cards.FromId("WW_437t"), s);
				})
			}));
			// [WW_441] Furnace Fuel (3) — When this is played, discarded, or destroyed, draw 2 cards. (Live discard 140614 6141-6233:
			// an empty BuffImpact at the hero, then the draws.)
			Power fuel = WildWestPlayedDiscardedOrDestroyed(() => new CustomTask((g, c, s, t, stack) =>
			{
				WildWestC4EmptyFx(g, (IPlayable)s, "BuffImpact", new[] { c.Hero.Id });
				for (int i = 0; i < V("WW_441", "cards", 2); i++) Generic.Draw(c);
			}));
			fuel.OwnFx = true;
			cards.Add("WW_441", new CardDef(fuel));
		}
	}
}
