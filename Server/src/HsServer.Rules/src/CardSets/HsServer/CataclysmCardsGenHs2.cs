// HsServer addition (not upstream SabberStone): CATACLYSM — chunk 2: Druid, Mage, Hunter and the MEND_ mini-set (spec: research
// spec_chunk2). Shared pieces: Shatter (Actions/Shatter.cs: the whole card = both halves), Generic.ManaSpent / SpendAllMana (the "spent N
// Mana while holding this" countdowns, the last Mana Crystal), SPELL_DAMAGE_THIS_TURN (client 4587), Magmaw's leftover appendages,
// Wickerfang's limb gains (Power.CopiesLimbGains), the Leylines (client 4283 / 4285 / 4288), the player's Animal Companions
// (Generic.AnimalCompanions / UpgradeCompanions / SummonCompanions), ASLEEP_UNTIL_TURN. Every number goes through CardValues (V).
using System;
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

		/// <summary>"If you didn't play a minion last turn" (your previous turn: two game turns ago).</summary>
		private static bool NoMinionPlayedLastTurn(Controller c) => !c.PlayHistory.Any(h => h.Turn == c.Game.Turn - 2 && h.SourceCard?.Type == CardType.MINION);

		/// <summary>"(While in hand, play a Dragon to become a N/N Dragon!)" (Stonetalon Striker, Ebonscale Scout, Ebyssian): after you play a
		/// Dragon, the card in hand becomes its Dragon version (Generic.ChangeInHand, enchantments kept). Live (Ebonscale Scout,
		/// Hearthstone_2026_11_09_16_08_27): the card's TRIGGER block, TARGET itself, then the change inside
		/// <see cref="DragonAwakensFx"/> (Source 0, the card its target) — the same block for every card of the mechanic.</summary>
		private static Trigger DragonAwakens(string version) => new Trigger(TriggerType.AFTER_PLAY_MINION)
		{
			TriggerActivation = TriggerActivation.HAND,
			TriggerSource = TriggerSource.FRIENDLY,
			Condition = new SelfCondition(p => p is Minion m && m.IsRace(Race.DRAGON)),
			SingleTask = new CustomTask((g, c, s, t, stack) =>
			{
				if (s is not IPlayable me || me.Zone?.Type != Zone.HAND || Cards.TryFromId(version) is not Card dragon) return;
				bool fx = false;
				if (g.History)
				{
					g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.TARGET, Data = 0, Info = new List<int> { me.Id } });
					fx = g.OpenSubSpell(me, SubSpells.FindNamed(me.Card.Id, "LevelUp_Gold") ?? DragonAwakensFx, new[] { me.Id });
				}
				Generic.ChangeInHand(c, me, dragon, ownBlock: false);
				if (fx) g.CloseSubSpells(me.Id);
			})
		};

		/// <summary>Live's effect around the "play a Dragon to become a N/N Dragon" change (recorded on Ebonscale Scout).</summary>
		private const string DragonAwakensFx = "ReuseFX_Generic_BuffImpact_LevelUp_Gold_Super:3de1c5e7c0102bf4b96565ec743ad2c6";

		/// <summary>The Leylines' "… {1} times" (Generic.LeylineNumbers: the player's extra triggers and effect increase).</summary>
		private static ISimpleTask Leyline(Action<Game, Controller, IEntity, int> run) => new CustomTask((g, c, s, t, stack) =>
		{
			var (times, amount) = Generic.LeylineNumbers(c, (IPlayable)s);
			for (int i = 0; i < times; i++)
			{
				if (c.Hero.IsDead || c.Opponent.Hero.IsDead) break;
				run(g, c, s, amount);
			}
		});

		/// <summary>A Leyline upgrade's effect (MEND_Arcanomicon_LeyLine_BuffImpact_Hero_Super, live darkmoon 57545: Source 0 on the hero).</summary>
		private static void LeylineUpgrade(Game g, Controller c, IEntity s, Action change)
		{
			bool fx = g.History && g.OpenSubSpell((IPlayable)s, SubSpells.FindNamed(((IPlayable)s).Card.Id, "LeyLine_BuffImpact"), new[] { c.Hero.Id },
				shownSource: Game.NoSubSpellSource);
			change();
			if (fx) g.CloseSubSpells(s.Id);
		}

		/// <summary>"Choose one to summon" among the player's Animal Companions (Spiritspeaker, Roam Free).</summary>
		private static void ChooseCompanion(Controller c, IEntity s)
		{
			var companions = Generic.AnimalCompanions(c);
			if (companions.Count == 0 || c.BoardZone.IsFull) return;
			Generic.CreateChoiceEntities(c, s, OfferCards(c, s, companions.Select(x => x.Id)), ChoiceAction.STACK, new CustomTask((g2, c2, s2, pick, st) =>
			{
				if (pick == null) return;
				Card chosen = pick.Card;
				if (pick.Zone?.Type == Zone.SETASIDE) c2.GraveyardZone.Add(pick.Zone.Remove(pick));
				Generic.SummonCompanions(c2, s2, which: new[] { chosen });
			}));
		}

		private static void CataclysmChunk2(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Druid
			// [CATA_130] Crystalspine Cub (1) — Whenever you spend your last Mana Crystal, gain +1/+1.
			cards.Add("CATA_130", new CardDef(new Power { LastManaSpentTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me) Give(g, s, me, "CATA_130e"); }) }));
			cards.Add("CATA_130e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("CATA_130", "stats", 1))) }));
			// [CATA_131] Felwood Treant (2) — Battlecry: Gain a temporary Mana Crystal. If you spent 4 Mana while holding this, it's permanent.
			cards.Add("CATA_131", new CardDef(new Power
			{
				ManaSpentWhileHeld = true,
				GlowWhile = Generic.ManaSpentReady,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (Generic.ManaSpentReady(s)) new ManaCrystalFullTask(V("CATA_131", "crystals", 1)).Process(g, c, s, null);
					else new TempManaTask(V("CATA_131", "crystals", 1)).Process(g, c, s, null);
				})
			}));
			// [CATA_132] Broodwatcher (4) — Battlecry: Get two 3/3 Whelps with Taunt. If you spent 8 Mana while holding this, summon them.
			cards.Add("CATA_132", new CardDef(new Power
			{
				ManaSpentWhileHeld = true,
				GlowWhile = Generic.ManaSpentReady,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					bool ready = Generic.ManaSpentReady(s);
					for (int i = 0; i < V("CATA_132", "whelps", 2); i++)
						if (ready) Summon(g, c, Cards.FromId("CATA_132t"), s);
						else GetCard(c, Cards.FromId("CATA_132t"), s);
				})
			}));
			// [CATA_133] Iridescent Flitterwing (5) — Elusive. At the end of your turn, give your other minions +1/+1.
			cards.Add("CATA_133", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.BoardZone.GetAll().Where(m => m.Id != s.Id && !(m is Location) && !m.Untouchable), "CATA_133e"))
				}
			}));
			cards.Add("CATA_133e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("CATA_133", "stats", 1))) }));
			// [CATA_134] Wildwood Circle (4) — Shatter. Summon two 2/2 Treants. Give your minions "Deathrattle: Summon a 2/2 Treant." (the
			// halves: t summons, t2 gives)
			ISimpleTask treants = new CustomTask((g, c, s, t, stack) =>
			{
				Generic.BeginSummonGroup(g, s, V("CATA_134", "treants", 2));
				for (int i = 0; i < V("CATA_134", "treants", 2); i++) Summon(g, c, Cards.FromId("CATA_134t3"), s);
			});
			ISimpleTask wildwood = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.BoardZone.GetAll().Where(m => !(m is Location) && !m.Untouchable), "CATA_134e"));
			cards.Add("CATA_134", new CardDef(new Power { PowerTask = ComplexTask.Create(treants, wildwood) }));
			cards.Add("CATA_134t", new CardDef(new Power { PowerTask = treants }));
			cards.Add("CATA_134t2", new CardDef(new Power { PowerTask = wildwood }));
			cards.Add("CATA_134e", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("CATA_134t3"), s)) }));
			// [CATA_135] Mossbinding (2) — Summon two 1/2 Golems. Spend all your Mana to give them +1/+1 for each Mana spent.
			cards.Add("CATA_135", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var golems = new List<Minion>();
					Generic.BeginSummonGroup(g, s, V("CATA_135", "golems", 2));
					for (int i = 0; i < V("CATA_135", "golems", 2); i++)
						if (SummonGet(g, c, Cards.FromId("CATA_135t"), s) is Minion m) golems.Add(m);
					int spent = Generic.SpendAllMana(c);
					if (spent > 0)
						foreach (Minion m in golems.Where(m => m.Zone?.Type == Zone.PLAY))
							Generic.AddEnchantmentBlock(g, Cards.FromId("CATA_135e"), (IPlayable)s, m, spent, spent, 0);
				})
			}));
			cards.Add("CATA_135e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(0)) { UseScriptTag = true } }));
			// [CATA_136] Azshara's Triumph (1) — Shuffle 5 random minions into your deck that cost (8) or more. Double their stats.
			cards.Add("CATA_136", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("CATA_136", "minions", 5); i++)
					{
						if (!(RandomCard(g, x => x.Type == CardType.MINION && x.Cost >= V("CATA_136", "cost", 8)) is Card card)) break;
						IPlayable p = Entity.FromCard(c, card, new EntityData { { GameTag.CREATOR, s.Id } });
						Generic.ShuffleIntoDeck.Invoke(c, s, p);
						if (p.Zone?.Type == Zone.DECK) Generic.AddEnchantmentBlock(g, Cards.FromId("CATA_136e"), (IPlayable)s, p, card.ATK, card.Health, 0);
					}
				})
			}));
			cards.Add("CATA_136e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0), Effects.Health_N(0)) { UseScriptTag = true } }));
			// [CATA_138] Forest's Gift (3) — Give a friendly minion +1/+1 for each minion you control.
			cards.Add("CATA_138", new CardDef(TargetFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = c.BoardZone.GetAll().Count(m => !(m is Location) && !m.Untouchable);
					if (t is Minion m && n > 0) Generic.AddEnchantmentBlock(g, Cards.FromId("CATA_138e"), (IPlayable)s, m, n, n, 0);
				})
			}));
			cards.Add("CATA_138e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(0)) { UseScriptTag = true } }));
			// [CATA_139] Wickerfang (6) — Colossal +4. After one of Wickerfang's Legs gains stats, this gains them too.
			cards.Add("CATA_139", new CardDef(new Power { CopiesLimbGains = "CATA_139e" }));
			cards.Add("CATA_139e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0), Effects.Health_N(0)) { UseScriptTag = true } }));
			// [CATA_139t..t4] Wickerfang's Leg — At the end of your turn, gain +1/+1.
			foreach (string id in new[] { "CATA_139t", "CATA_139t2", "CATA_139t3", "CATA_139t4" })
				cards.Add(id, new CardDef(new Power
				{
					Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me) Give(g, s, me, "CATA_139te"); }) }
				}));
			cards.Add("CATA_139te", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("CATA_139t", "stats", 1))) }));
			// [CATA_140] Merithra of the Dream (8) — Battlecry: Fill your hand with random Dragons. If you spent 25 Mana while holding this, they
			// cost (1).
			cards.Add("CATA_140", new CardDef(new Power
			{
				ManaSpentWhileHeld = true,
				GlowWhile = Generic.ManaSpentReady,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					bool ready = Generic.ManaSpentReady(s);
					for (int guard = 0; guard < 10 && !c.HandZone.IsFull; guard++)
						if (GetCard(c, RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.DRAGON)), s) is IPlayable got) { if (ready) Give(g, s, got, "GBL_001e"); }
						else break;
				})
			}));
			// [MEND_040] Ash Worm (1) — Starts Dormant. When your board is full, awaken. (its counter MEND_040e2: DormantCards.tsv)
			cards.Add("MEND_040", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					WhileDormant = true,
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me && me[GameTag.DORMANT] > 0 && c.BoardZone.IsFull) Generic.Awaken(me); })
				}
			}));
			// [MEND_041] Wizened Wildspeaker (5) — Taunt. Battlecry: If you didn't play a minion last turn, refresh 3 Mana Crystals.
			cards.Add("MEND_041", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => NoMinionPlayedLastTurn(c), new CustomTask((g, c, s, t, stack) => RefreshMana(c, V("MEND_041", "refresh", 3))))
			}));
			// [MEND_042] Lifebloom (9) — Restore #8 Health to all friendly characters. Summon two random 8-Cost minions.
			cards.Add("MEND_042", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new HealTask(V("MEND_042", "heal", 8), EntityType.FRIENDS), new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("MEND_042", "minions", 2); i++) Summon(g, c, RandomMinionOfCost(g, V("MEND_042", "cost", 8)), s);
				}))
			}));
			// [MEND_043] Heartroot Stones (3) — Draw a card and gain 3 Armor. If you didn't play a minion last turn, do it again.
			ISimpleTask heartroot = ComplexTask.Create(new DrawTask(), new ArmorTask(V("MEND_043", "armor", 3)));
			cards.Add("MEND_043", new CardDef(new Power { PowerTask = ComplexTask.Create(heartroot, ComplexTask.If((g, c, s) => NoMinionPlayedLastTurn(c), heartroot)) }));
			// [MEND_044] Tranquil Clearing (2, Location) — Give a minion +2 Health and Taunt. It falls asleep until the end of your next turn.
			// (any minion — doubt; HsTag.ASLEEP_UNTIL_TURN keeps it exhausted through your next turn)
			cards.Add("MEND_044", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Give(g, s, m, "MEND_044e4");
					Give(g, s, m, "MEND_044e3");
					m[HsTag.ASLEEP_UNTIL_TURN] = g.Turn + (g.CurrentPlayer == c ? 2 : 1);
					m.IsExhausted = true;
				})
			}));
			cards.Add("MEND_044e4", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("MEND_044", "health", 2)), new Effect(GameTag.TAUNT, EffectOperator.SET, 1)) }));
			cards.Add("MEND_044e3", new CardDef(new Power { Enchant = new Enchant() }));
			// [MEND_045] Seeding Dragon (4) — Taunt. Deathrattle: Get a random Dragon. It costs (2) less.
			cards.Add("MEND_045", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (GetCard(c, RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.DRAGON)), s) is IPlayable got) GiveCostLess(g, s, got, V("MEND_045", "less", 2));
				})
			}));
			// [MEND_046] Bashana Runetotem (7) — Battlecry: Get three 2/2 Treants. Carve 12 Mana worth of Nature spells into them. (live
			// 140614: three Nature spells of any class whose Costs sum to 12; the Treant casts its spell at the target it was played with)
			cards.Add("MEND_046", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int total = V("MEND_046", "mana", 12), count = V("MEND_046", "treants", 3);
					var pool = Cards.GenerationPool(g, c).Where(x => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == NATURE && x.Cost <= total).ToList();
					List<Card> spells = null;
					for (int tries = 0; tries < 60 && spells == null && pool.Count > 0; tries++)
					{
						var pick = new List<Card>();
						int left = total;
						for (int i = 0; i < count - 1; i++)
						{
							var fits = pool.Where(x => x.Cost <= left).ToList();
							if (fits.Count == 0) break;
							Card one = fits[g.Random.Next(fits.Count)];
							pick.Add(one);
							left -= one.Cost;
						}
						var last = pool.Where(x => x.Cost == left).ToList();
						if (pick.Count == count - 1 && last.Count > 0) { pick.Add(last[g.Random.Next(last.Count)]); spells = pick; }
					}
					foreach (Card spell in spells ?? new List<Card>())
						if (GetCard(c, Cards.FromId("MEND_046t"), s) is IPlayable treant)
						{
							IPlayable shown = Entity.FromCard(c, spell, new EntityData { { GameTag.CREATOR, s.Id } }, c.SetasideZone);
							Generic.CarrySpell(treant, shown); // (the client's "Cast {0}."; live ADDITIONAL_PLAY_REQS_1 = the spell)
						}
				})
			}));
			// [MEND_046t] Treant — Battlecry: Cast {0}. (Played with the spell's target: live 140614 35272 — Generic.CastCarriedSpells.)
			cards.Add("MEND_046t", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Generic.CastCarriedSpells(c, (IPlayable)s, t)) }));

			// ---------------------------------------------------------------- Mage
			// [CATA_452] Spellweaver's Brilliance (10) — Summon a 6/6 Dragon. Costs (1) less for each damage you dealt with spells this turn.
			cards.Add("CATA_452", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller[Generic.SPELL_DAMAGE_THIS_TURN] * V("CATA_452", "less", 1)),
				PowerTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("CATA_452t"), s))
			}));
			// [CATA_458] Archmage Kalec (4) — Battlecry: Give all spells in your hand and deck Spell Damage +1.
			cards.Add("CATA_458", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.HandZone.GetAll().Concat(c.DeckZone.GetAll()).Where(p => p is Spell), "CATA_458e"))
			}));
			cards.Add("CATA_458e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.SPELLPOWER, EffectOperator.ADD, V("CATA_458", "spell damage", 1))) }));
			// [CATA_483] Unstable Spellcaster (4) — Spell Damage +1. Battlecry: If you dealt damage with a spell this turn, summon a copy of this.
			cards.Add("CATA_483", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c[Generic.SPELL_DAMAGE_THIS_TURN] > 0, new CustomTask((g, c, s, t, stack) =>
				{
					if (s is Minion me && me.Zone?.Type == Zone.PLAY && !c.BoardZone.IsFull) Generic.Copy(c, s, me, Zone.PLAY);
				}))
			}));
			// [CATA_484] Winterspring Whelp (1) — Battlecry: Discover a 1-Cost spell from any class.
			cards.Add("CATA_484", new CardDef(new Power { PowerTask = DiscoverFromTask.Where((c, x) => x.Type == CardType.SPELL && x.Cost == V("CATA_484", "cost", 1)) }));
			// [CATA_485] Sleet Storm (1) — Deal $2 damage. Deal $1 damage to a random enemy minion.
			cards.Add("CATA_485", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter ch) Generic.DamageCharFunc((IPlayable)s, ch, V("CATA_485", "damage", 2), true);
					var enemies = c.Opponent.BoardZone.GetAll().Where(m => !(m is Location) && !m.Untouchable && !m.IsDead && !m.ToBeDestroyed && m.Health > 0).ToList();
					if (enemies.Count > 0) Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], V("CATA_485", "random", 1), true);
				})
			}));
			// [CATA_487] Raincaller (2) — The first time you deal damage with a spell each turn, gain +2 Attack. (once a turn: its CATA_487e1)
			cards.Add("CATA_487", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEAL_DAMAGE)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					EitherTurn = true,
					Condition = new SelfCondition(p => p is Spell),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me) || me.AppliedEnchantments?.Any(e => e.Card == Cards.FromId("CATA_487e1")) == true) return;
						Give(g, s, me, "CATA_487e");
						Give(g, s, me, "CATA_487e1");
					})
				}
			}));
			cards.Add("CATA_487e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("CATA_487", "attack", 2))) }));
			cards.Add("CATA_487e1", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.TRIGGER_VISUAL, EffectOperator.SET, 0)) { IsOneTurnEffect = true } }));
			// [CATA_488] Vulcanos (7) — Colossal +2. At the end of your turn, deal 3 damage to all other minions.
			cards.Add("CATA_488", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) => DamageAll(s, AllOtherMinions(c, s).Cast<ICharacter>(), V("CATA_488", "damage", 3), false))
				}
			}));
			// [CATA_488t / t2] Plume of Vulcanos — Whenever this takes damage, get a random Fire spell. It costs (3) less.
			foreach (string id in new[] { "CATA_488t", "CATA_488t2" })
				cards.Add(id, new CardDef(new Power
				{
					Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
					{
						TriggerSource = TriggerSource.SELF,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (GetCard(c, RandomCard(g, x => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == FIRE_SCHOOL), s) is IPlayable fire)
								GiveCostLess(g, s, fire, V("CATA_488t", "less", 3));
						})
					}
				}));
			// [CATA_489] Arcane Flow (4) — Shatter. Deal $4 damage. Deal $2 damage to all enemies. (halves: t the 4, t2 the 2; live 11842:
			// the target takes both)
			ISimpleTask flowHit = new CustomTask((g, c, s, t, stack) => { if (t is ICharacter ch) Generic.DamageCharFunc((IPlayable)s, ch, V("CATA_489", "damage", 4), true); });
			ISimpleTask flowAll = new CustomTask((g, c, s, t, stack) => DamageAll(s, Enemies(c), V("CATA_489", "all", 2), true));
			cards.Add("CATA_489", new CardDef(TargetAny, new Power { PowerTask = ComplexTask.Create(flowHit, flowAll) }));
			cards.Add("CATA_489t", new CardDef(TargetAny, new Power { PowerTask = flowHit }));
			cards.Add("CATA_489t2", new CardDef(new Power { PowerTask = flowAll }));
			// [CATA_978] Sindragosa's Triumph (5) — Deal $8 damage to a minion. Reduce the Cost of a random card in your hand by the excess
			// damage. (its Spell Damage counted by the script: client 349)
			cards.Add("CATA_978", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					int damage = V("CATA_978", "damage", 8) + c.CurrentSpellPower;
					if (s is Spell me) me[HsTag.SPELL_DAMAGE_COUNTED] = 1;
					int excess = m.HasDivineShield || m.IsImmune ? 0 : Math.Max(0, damage - Math.Max(0, m.Health));
					Generic.DamageCharFunc((IPlayable)s, m, damage, false);
					var hand = c.HandZone.GetAll().ToList();
					if (excess > 0 && hand.Count > 0) Give(g, s, hand[g.Random.Next(hand.Count)], "CATA_978e", excess);
				})
			}));
			cards.Add("CATA_978e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(0)) { UseScriptTag = true } }));
			// [CATA_979] Conjuration Specialist (3) — Battlecry: Choose a spell in your hand. Split it into two random spells of the same Cost.
			cards.Add("CATA_979", new CardDef(new Power
			{
				HandTarget = SelfCondition.IsSpell,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Spell spell) || spell.Zone?.Type != Zone.HAND || spell.Controller != c) return;
					int cost = spell.Cost;
					Generic.RemoveFromHand(c, spell);
					for (int i = 0; i < V("CATA_979", "spells", 2); i++) GetCard(c, RandomCard(g, x => x.Type == CardType.SPELL && x.Cost == cost), s);
				})
			}));
			// [MEND_500] Bursting Leyline (4) — Deal ${0} damage to a random enemy minion. Excess damage hits the enemy hero. (… {1} times)
			cards.Add("MEND_500", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_MINIMUM_ENEMY_MINIONS, 1 } }, new Power
			{
				PowerTask = Leyline((g, c, s, amount) =>
				{
					var enemies = c.Opponent.BoardZone.GetAll().Where(m => !(m is Location) && !m.Untouchable && !m.IsDead && !m.ToBeDestroyed && m.Health > 0).ToList();
					if (enemies.Count == 0) return;
					Minion m = enemies[g.Random.Next(enemies.Count)];
					int damage = amount + c.CurrentSpellPower;
					if (s is Spell me) me[HsTag.SPELL_DAMAGE_COUNTED] = 1;
					int excess = m.HasDivineShield || m.IsImmune ? 0 : Math.Max(0, damage - Math.Max(0, m.Health));
					Generic.DamageCharFunc((IPlayable)s, m, damage, false);
					if (excess > 0) Generic.DamageCharFunc((IPlayable)s, c.Opponent.Hero, excess, false);
				})
			}));
			// [MEND_501] Ley Walker (3) — Battlecry: Your Leylines cost (1) less this game. Deathrattle: Get a random Leyline.
			cards.Add("MEND_501", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.AddEnchantmentBlock(g, Cards.FromId("MEND_501t2e"), (IPlayable)s, c, 0, 0, 0)),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var leylines = Cards.All.Where(x => x[Generic.LEYLINE] > 0 && x[GameTag.COLLECTIBLE] > 0).OrderBy(x => x.Id).ToList();
					if (leylines.Count > 0) GetCard(c, leylines[g.Random.Next(leylines.Count)], s);
				})
			}));
			cards.Add("MEND_501t2e", new CardDef(new Power { Aura = new Aura(AuraType.HAND, Effects.ReduceCost(1)) { Condition = new SelfCondition(p => p.Card[Generic.LEYLINE] > 0) } }));
			// [MEND_502] Crystallized Leyline (5) — Summon a random {0}-Cost minion. (… {1} times)
			cards.Add("MEND_502", new CardDef(new Power { PowerTask = Leyline((g, c, s, amount) => Summon(g, c, RandomMinionOfCost(g, amount), s)) }));
			// [MEND_503] Surge Needle (4) — Battlecry: Your Leylines trigger an additional time this game.
			cards.Add("MEND_503", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => LeylineUpgrade(g, c, s, () => c[Generic.LEYLINE_EXTRA_TRIGGERS] = c[Generic.LEYLINE_EXTRA_TRIGGERS] + 1))
			}));
			// [MEND_504] Leyline Nexus (2) — Draw a card. It costs ({0}) less. / Draw {1} cards. They cost ({0}) less.
			cards.Add("MEND_504", new CardDef(new Power
			{
				PowerTask = Leyline((g, c, s, amount) =>
				{
					if (Generic.Draw(c) is IPlayable drawn && drawn.Zone?.Type == Zone.HAND) Give(g, s, drawn, "MEND_504e", amount);
				})
			}));
			cards.Add("MEND_504e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(0)) { UseScriptTag = true, RemoveWhenPlayed = true } }));
			// [MEND_505] The Arcanomicon (6) — Get all 3 Leylines. Choose an upgrade for your Leylines.
			cards.Add("MEND_505", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Card leyline in Cards.All.Where(x => x[Generic.LEYLINE] > 0 && x[GameTag.COLLECTIBLE] > 0).OrderBy(x => x.Id).ToList())
						GetCard(c, leyline, s);
				}), ChooseAndCast("MEND_505t2", "MEND_505t", "MEND_505t3"))
			}));
			// [MEND_505t] Energize — Your Leylines trigger an additional time this game.
			cards.Add("MEND_505t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => LeylineUpgrade(g, c, s, () => c[Generic.LEYLINE_EXTRA_TRIGGERS] = c[Generic.LEYLINE_EXTRA_TRIGGERS] + 1))
			}));
			// [MEND_505t2] Unblock — Your Leylines cost (2) less this game. (two MEND_501t2e on the player, live vfx 15481)
			cards.Add("MEND_505t2", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => LeylineUpgrade(g, c, s, () =>
				{
					for (int i = 0; i < V("MEND_505t2", "less", 2); i++) Generic.AddEnchantmentBlock(g, Cards.FromId("MEND_501t2e"), (IPlayable)s, c, 0, 0, 0);
				}))
			}));
			// [MEND_505t3] Empower — Increase the effects of your Leylines by 2 this game.
			cards.Add("MEND_505t3", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					LeylineUpgrade(g, c, s, () => c[Generic.LEYLINE_EFFECT_INCREASE] = c[Generic.LEYLINE_EFFECT_INCREASE] + V("MEND_505t3", "increase", 2)))
			}));
			// [MEND_506] Mystic Runesaber (2) — Elusive. Battlecry: Increase the effects of your Leylines by 1 this game.
			cards.Add("MEND_506", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					LeylineUpgrade(g, c, s, () => c[Generic.LEYLINE_EFFECT_INCREASE] = c[Generic.LEYLINE_EFFECT_INCREASE] + V("MEND_506", "increase", 1)))
			}));

			// ---------------------------------------------------------------- Hunter
			// [CATA_550] Magmaw (7) — Colossal +99. Summon any leftover appendages when there is room. (Generic.SummonLeftovers)
			cards.Add("CATA_550", new CardDef(new Power { NoScript = true }));
			// [CATA_550t..t6] Magmaw's Body — Deathrattle: Give a random friendly minion +2 Attack.
			foreach (string id in new[] { "CATA_550t", "CATA_550t2", "CATA_550t3", "CATA_550t4", "CATA_550t5", "CATA_550t6" })
				cards.Add(id, new CardDef(new Power
				{
					DeathrattleTask = new CustomTask((g, c, s, t, stack) => { if (RandomFriendly(g, c) is Minion m) Give(g, s, m, "CATA_550e1"); })
				}));
			cards.Add("CATA_550e1", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("CATA_550t", "attack", 2))) }));
			// [CATA_551] Stonetalon Striker (3) — Taunt. (While in hand, play a Dragon to become a 6/6 Dragon!)
			cards.Add("CATA_551", new CardDef(new Power { Trigger = DragonAwakens("CATA_551t") }));
			cards.Add("CATA_551t", new CardDef(new Power { NoScript = true }));
			// [CATA_552] Ebonscale Scout (6) — Battlecry: Deal damage equal to this minion's Attack. (… an 8/8 Dragon!)
			ISimpleTask scout = new CustomTask((g, c, s, t, stack) => { if (t is ICharacter ch && s is Minion me) Generic.DamageCharFunc(me, ch, me.AttackDamage, false); });
			cards.Add("CATA_552", new CardDef(BattlecryAny, new Power { Trigger = DragonAwakens("CATA_552t"), PowerTask = scout }));
			cards.Add("CATA_552t", new CardDef(BattlecryAny, new Power { PowerTask = scout }));
			// [CATA_553] Ebyssian (7) — Battlecry: Your Dragons have Rush this game. (… a 12/12 Dragon!) (BOT_238e's way: a player aura)
			cards.Add("CATA_553", new CardDef(new Power { Trigger = DragonAwakens("CATA_553t"), PowerTask = new AddEnchantmentTask("CATA_553e", EntityType.CONTROLLER) }));
			cards.Add("CATA_553t", new CardDef(new Power { PowerTask = new AddEnchantmentTask("CATA_553e", EntityType.CONTROLLER) }));
			cards.Add("CATA_553e", new CardDef(new Power { Aura = new Aura(AuraType.BOARD, "CATA_553e2") { Condition = SelfCondition.IsRace(Race.DRAGON) } }));
			cards.Add("CATA_553e2", new CardDef(new Power { Enchant = new Enchant(GameTag.RUSH, EffectOperator.SET, 1) }));
			// [CATA_554] Earthen Roar (1) — Set an enemy minion's Health to 1. If you're holding a Dragon, pick another.
			cards.Add("CATA_554", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } }, new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("CATA_554e", EntityType.TARGET), ComplexTask.If((g, c, s) => HoldingDragon(c, s), new CustomTask((g, c, s, t, stack) =>
				{
					int first = t?.Id ?? 0;
					Generic.PickTargets(c, s, 1, ch => ch is Minion m && m.Controller != c && m.Id != first && !m.Untouchable,
						(g2, c2, s2, other) => { if (other is Minion m) Give(g2, s2, m, "CATA_554e"); });
				})))
			}));
			cards.Add("CATA_554e", new CardDef(new Power { Enchant = new Enchant(Effects.SetMaxHealth(V("CATA_554", "health", 1))) }));
			// [CATA_557] Sylvanas's Triumph (2) — Deal $3 damage. If you've played another copy of this, hit all enemies instead. (client 2433:
			// powered up, it takes no target)
			cards.Add("CATA_557", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.PlayHistory.Any(h => h.SourceCard == s.Card && h.SourceId != s.Id),
					new CustomTask((g, c, s, t, stack) => DamageAll(s, Enemies(c), V("CATA_557", "damage", 3), true)),
					new CustomTask((g, c, s, t, stack) => { if (t is ICharacter ch) Generic.DamageCharFunc((IPlayable)s, ch, V("CATA_557", "damage", 3), true); }))
			}));
			// [CATA_560] Confront the Tol'vir (4) — Summon each 1-Cost minion you've played this game. (in play order; each play counts)
			cards.Add("CATA_560", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (PlayHistoryEntry h in c.PlayHistory.Where(h => h.SourceCard?.Type == CardType.MINION && h.SourceCard.Cost == V("CATA_560", "cost", 1)).ToList())
					{
						if (c.BoardZone.IsFull) break;
						Summon(g, c, h.SourceCard, s);
					}
				})
			}));
			// [CATA_566] Tol'vir Carver (3) — Battlecry: Choose a card in your hand. At the start of your turns, reduce its Cost by (1).
			cards.Add("CATA_566", new CardDef(new Power
			{
				HandTarget = SelfCondition.AnyCard,
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t?.Zone?.Type == Zone.HAND && t.Controller == c) Give(g, s, t, "CATA_566e"); })
			}));
			cards.Add("CATA_566e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.ReduceCost(0)) { UseScriptTag = true },
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Enchantment e) Generic.GrowEnchantment(e, V("CATA_566", "less", 1), 0); })
				}
			}));
			// [CATA_820] Supply Run (4) — Shatter. Draw 3 minions. Give minions in your hand +2/+2. (halves: t draws, t2 buffs)
			ISimpleTask supplyDraw = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p is Minion, V("CATA_820", "draws", 3)));
			ISimpleTask supplyBuff = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.HandZone.GetAll().Where(p => Generic.IsMinionCard(p)), "CATA_820e"));
			cards.Add("CATA_820", new CardDef(new Power { PowerTask = ComplexTask.Create(supplyDraw, supplyBuff) }));
			cards.Add("CATA_820t", new CardDef(new Power { PowerTask = supplyDraw }));
			cards.Add("CATA_820t2", new CardDef(new Power { PowerTask = supplyBuff }));
			cards.Add("CATA_820e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("CATA_820", "stats", 2))) }));
			// [MEND_300] Tame Pet (1) — Replace your future Animal Companions with random Beasts that cost (1) more. Draw a card.
			cards.Add("MEND_300", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new CustomTask((g, c, s, t, stack) => Generic.UpgradeCompanions(c, s, V("MEND_300", "more", 1))), new DrawTask())
			}));
			// [MEND_301] Spiritspeaker (4) — Battlecry: Choose an Animal Companion to summon.
			cards.Add("MEND_301", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => ChooseCompanion(c, s)) }));
			// [MEND_302] Wasteland Vanguard (4) — Battlecry: Deal 3 damage split among all enemies. If any die, deal 3 more. (random 1-damage
			// hits; wiki: the extra 3 once)
			cards.Add("MEND_302", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					void Split()
					{
						for (int i = 0; i < V("MEND_302", "damage", 3); i++)
						{
							var enemies = Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed && x.Health > 0).ToList();
							if (enemies.Count == 0) break;
							Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], 1, false);
						}
					}
					int before = c.Opponent.DiedThisGame.Count;
					Split();
					g.DeathProcessingAndAuraUpdate();
					if (c.Opponent.DiedThisGame.Count > before) Split();
				})
			}));
			// [MEND_303] Migrating Elekk (2) — Taunt. Battlecry: Replace your future Animal Companions with random Beasts that cost (1) more.
			cards.Add("MEND_303", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Generic.UpgradeCompanions(c, s, V("MEND_303", "more", 1))) }));
			// [MEND_304] Talya Earthstrider (5) — Battlecry: Your cards that summon Animal Companions summon 1 more this game.
			cards.Add("MEND_304", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => c[HsTag.COMPANION_EXTRA] = c[HsTag.COMPANION_EXTRA] + V("MEND_304", "more", 1))
			}));
			// [MEND_305] Nurturing Nature (2) — Give a friendly Beast +2/+2. Give a random Beast in your hand +2/+2.
			cards.Add("MEND_305", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_RACE, (int)Race.BEAST } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m) Give(g, s, m, "MEND_305e");
					var beasts = c.HandZone.GetAll().Where(p => p is Minion hm && hm.IsRace(Race.BEAST)).ToList();
					if (beasts.Count > 0) Give(g, s, beasts[g.Random.Next(beasts.Count)], "MEND_305e");
				})
			}));
			cards.Add("MEND_305e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("MEND_305", "stats", 2))) }));
			// [MEND_307] Roam Free (7) — Replace your future Animal Companions with random Beasts that cost (2) more. Choose one to summon.
			cards.Add("MEND_307", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Generic.UpgradeCompanions(c, s, V("MEND_307", "more", 2));
					ChooseCompanion(c, s);
				})
			}));
		}
	}
}
