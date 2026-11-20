// HsServer addition (not upstream SabberStone): The Dalaran Heist — chunk 1: bosses DALA_BOSS_01..25, their Normal ("p") and Heroic
// ("px") Hero Powers and the cards only they use (Akazamzarak's magical feats, the Kind Waitress, "Scourgelord" Dazzik, Hoard of Toys,
// the bosses' enchantments). Shared pieces: Actions/DalaranHeistGroup1.cs (Stormswell's OVERLOAD_SPELLS_EXTRA_CASTS read by CastSpell,
// "a Coin" for Street Smarts); Hero Powers with Combo run their ComboTask (Actions/HeroPower.cs). Every number goes through CardValues
// (V). Evidence: the decoded boss games (scratchpad boss-games, live-session-* / heist-session-*) and the client's card data; the
// wiki boss pages (hearthstone.wiki.gg/wiki/<boss>) only list the Hero Powers.
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
		/// <summary>Living minions on both boards (not Dormant / dying).</summary>
		private static List<Minion> HeistG1Minions(Controller c) =>
			c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList();

		/// <summary>A random element of <paramref name="list"/>, or null.</summary>
		private static T HeistG1Pick<T>(Game g, IList<T> list) where T : class => list.Count == 0 ? null : list[g.Random.Next(list.Count)];

		/// <summary>"Destroy a random minion …" for a list of candidates (none: nothing).</summary>
		private static void HeistG1DestroyRandom(Game g, IEntity source, List<Minion> pool)
		{
			if (HeistG1Pick(g, pool) is Minion m) Generic.Destroy(m, source);
		}

		/// <summary>A copy of <paramref name="card"/> summoned for <paramref name="c"/> as a 1/1 (Jepetto's DALA_BOSS_25e "Toyification":
		/// "Stats changed to 1/1"); <paramref name="original"/> (a drawn card) is copied as a copy, else a new card.</summary>
		private static void HeistG1SummonToy(Game g, Controller c, IEntity source, Card card, IPlayable original = null)
		{
			if (card == null || c.BoardZone.IsFull) return;
			IPlayable toy;
			if (original != null) toy = Generic.Copy(c, source, original, Zone.PLAY);
			else
			{
				toy = Entity.FromCard(c, card);
				Generic.SummonBlock.Invoke(g, (Minion)toy, -1, source);
			}
			if (toy is Minion m && m.Zone?.Type == Zone.PLAY) Give(g, source, m, "DALA_BOSS_25e");
		}

		/// <summary>
		/// Belnaara's flight (Take Flight! DALA_BOSS_07p → Flying! DALA_BOSS_07p2 → back). No live recording and no wiki notes; the
		/// cards' own text and data: 07p "After taking 15 damage, Belnaara will take flight. (@ damage remaining)" counts its
		/// TAG_SCRIPT_DATA_NUM_1 (15 in its data) down with the damage the hero takes (wiki x_Armor.txt:13 "Damage taken while
		/// protected by Armor still counts as taking damage"); at 0 the hero gets DALA_BOSS_07e "Belnaara is Immune for now" and the
		/// Hero Power becomes 07p2 "Belnaara will land in 2 turns. (@ turns remaining)" (its NUM_1 = 2), counted at the start of
		/// her turns (HearthstoneTopDecks Heroic guide: "If Belnaara receives 15 damage, he becomes immune to attacks for two
		/// turns"); at 0 she lands: 07e removed, Take Flight! again with a new count.
		/// </summary>
		private static void HeistG1TakeFlight(Game g, Controller c, IPlayable power)
		{
			if (!(power is HeroPower hp) || c.Hero.HeroPower != hp) return;
			int damage = Math.Max(0, g.CurrentEventData?.EventNumber ?? 0);
			if (damage == 0) return;
			hp[GameTag.TAG_SCRIPT_DATA_NUM_1] = Math.Max(0, hp[GameTag.TAG_SCRIPT_DATA_NUM_1] - damage);
			if (hp[GameTag.TAG_SCRIPT_DATA_NUM_1] > 0 || c.Hero.IsDead) return;
			Give(g, hp, c.Hero, "DALA_BOSS_07e");
			new ReplaceHeroPower(Cards.FromId("DALA_BOSS_07p2")).Process(g, c, hp, null);
		}

		private static void HeistG1Land(Game g, Controller c, IPlayable power)
		{
			if (!(power is HeroPower hp) || c.Hero.HeroPower != hp || g.CurrentPlayer != c) return;
			hp[GameTag.TAG_SCRIPT_DATA_NUM_1] = Math.Max(0, hp[GameTag.TAG_SCRIPT_DATA_NUM_1] - 1);
			if (hp[GameTag.TAG_SCRIPT_DATA_NUM_1] > 0) return;
			foreach (Enchantment e in c.Hero.AppliedEnchantments?.Where(e => e.Card.Id == "DALA_BOSS_07e").ToList() ?? new List<Enchantment>())
				RemoveEnchantmentTask.Task.Process(g, c, e, null);
			new ReplaceHeroPower(Cards.FromId("DALA_BOSS_07p")).Process(g, c, hp, null);
		}

		/// <summary>Apple Toss: damage to the lowest-Health enemy (ties: random); "If it dies, repeat this" — again while the one hit
		/// died (Health 0 or less), never twice at a dead one; stops when no enemy is left or a hero dies.</summary>
		private static void HeistG1AppleToss(Game g, Controller c, IEntity source, int damage)
		{
			for (int guard = 0; guard < 20; guard++)
			{
				var enemies = c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).Cast<ICharacter>().ToList();
				if (!c.Opponent.Hero.IsDead) enemies.Add(c.Opponent.Hero);
				if (enemies.Count == 0) return;
				int lowest = enemies.Min(e => e.Health);
				ICharacter target = HeistG1Pick(g, enemies.Where(e => e.Health == lowest).ToList());
				Generic.DamageCharFunc((IPlayable)source, target, damage, false);
				if (!(target.IsDead || target.Health <= 0 || target.ToBeDestroyed) || c.Hero.IsDead || c.Opponent.Hero.IsDead) return;
			}
		}

		static partial void HeistBossPowers1(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- 01 Chomper
			// [DALA_BOSS_01p] Chomp (2) — Lifesteal. Deal 1 damage to a minion. [01px] (3): 2 damage. (Live heist-session-20260929-114419
			// 14899: the damage in the POWER block, the drain in a TRIGGER LIFESTEAL block inside it — the engine's Lifesteal rule.)
			foreach ((string id, int dmg) in new[] { ("DALA_BOSS_01p", 1), ("DALA_BOSS_01px", 2) })
				cards.Add(id, new CardDef(TargetMinion, new Power { PowerTask = new DamageTask(V(id, "damage", dmg), EntityType.TARGET) }));

			// ---------------------------------------------------------------- 02 Awilo, Cooking Trainer
			// [DALA_BOSS_02p] The Feast — Passive. After you cast a spell, restore 3 Health to your hero. [02px]: 5. (Live
			// live-session-20261001-162435 17691: TRIGGER on the Hero Power, TriggerKeyword TRIGGER_VISUAL, at the end of the spell's PLAY
			// block: TARGET = the hero, the heal.)
			foreach ((string id, int heal) in new[] { ("DALA_BOSS_02p", 3), ("DALA_BOSS_02px", 5) })
				cards.Add(id, new CardDef(new Power
				{
					Trigger = new Trigger(TriggerType.AFTER_CAST)
					{
						TriggerSource = TriggerSource.FRIENDLY,
						SingleTask = new HealTask(V(id, "heal", heal), EntityType.HERO)
					}
				}));

			// ---------------------------------------------------------------- 03 The Great Akazamzarak
			// [DALA_BOSS_03p] Prestidigitation (1) / [03px] (0) — Add a random magical feat to your hand. (Its entourage: Reductomara,
			// Bunnifitronus, Presto Legendaro, Yoggers Poggers; live 6552: the feat FULL_ENTITY in hand, created by the Hero Power.)
			ISimpleTask addFeat = new CustomTask((g, c, s, t, stack) =>
			{
				string[] feats = ((IPlayable)s).Card.Entourage is { Length: > 0 } e ? e : new[] { "DALA_BOSS_03t", "DALA_BOSS_03t2", "DALA_BOSS_03t3", "DALA_BOSS_03t4" };
				AddToHand(c, Cards.FromId(feats[g.Random.Next(feats.Length)]));
			});
			cards.Add("DALA_BOSS_03p", new CardDef(new Power { PowerTask = addFeat }));
			cards.Add("DALA_BOSS_03px", new CardDef(new Power { PowerTask = addFeat }));
			// [DALA_BOSS_03t] Reductomara (1) — Reduce the cost of all spells in both player's hand and deck by (1). (Live
			// live-session-20261002-204752 7010: DALA_BOSS_03e on each spell, both players' hands and decks.)
			cards.Add("DALA_BOSS_03t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Controller p in new[] { c, c.Opponent })
						foreach (IPlayable spell in p.HandZone.GetAll().Concat(p.DeckZone.GetAll()).Where(x => x is Spell).ToList())
							Give(g, s, spell, "DALA_BOSS_03e");
				})
			}));
			cards.Add("DALA_BOSS_03e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("DALA_BOSS_03t", "less", 1))) }));
			// [DALA_BOSS_03t2] Bunnifitronus (1) — Transform a random minion into a random critter. (Entourage: Squirrel EX1_tk28, Sheep
			// CS2_tk1, Frog hexfrog; live heist-session-20260929-122856 30754: CHANGE_ENTITY of the minion.)
			cards.Add("DALA_BOSS_03t2", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(HeistG1Pick(g, HeistG1Minions(c)) is Minion m)) return;
					string[] critters = ((IPlayable)s).Card.Entourage is { Length: > 0 } e ? e : new[] { "EX1_tk28", "CS2_tk1", "hexfrog" };
					new TransformTask(Cards.FromId(critters[g.Random.Next(critters.Length)]), EntityType.TARGET).Process(g, c, s, m);
				})
			}));
			// [DALA_BOSS_03t3] Presto Legendaro (1) — Give each player a random Golden Legendary minion. (Live 25689: the caster's first,
			// then the opponent's, PREMIUM 1, created by the spell.)
			cards.Add("DALA_BOSS_03t3", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Controller p in new[] { c, c.Opponent })
					{
						Card legend = RandomCard(g, x => x.Type == CardType.MINION && Generic.IsLegendary(x));
						if (legend != null && !p.HandZone.IsFull)
							Generic.AddHandPhase.Invoke(p, Entity.FromCard(p, legend, new EntityData { [GameTag.PREMIUM] = 1, [GameTag.CREATOR] = s.Id }));
					}
				})
			}));
			// [DALA_BOSS_03t4] Yoggers Poggers (1) — Shuffle three Scrolls of Wonder into each player's deck. (Live 25274: the caster's
			// three in POWER 0, SLUSH_TIME 3000, the opponent's three in POWER 1 — Power.SecondPowerTask.)
			ISimpleTask Scrolls(bool opponent) => new CustomTask((g, c, s, t, stack) =>
			{
				Controller p = opponent ? c.Opponent : c;
				for (int i = 0; i < V("DALA_BOSS_03t4", "scrolls", 3); i++)
					Generic.ShuffleIntoDeck.Invoke(p, s, Entity.FromCard(p, Cards.FromId("LOOT_106t"), new EntityData { [GameTag.CREATOR] = s.Id }));
			});
			cards.Add("DALA_BOSS_03t4", new CardDef(new Power { PowerTask = Scrolls(false), SecondPowerTask = Scrolls(true) }));

			// ---------------------------------------------------------------- 04 Kaye Toogie
			// [DALA_BOSS_04p] Open Wormhole (2) — Both players summon a minion from their deck. They fight. (Live
			// live-session-20261001-162435 2919: the boss's minion, then the player's, both in play, then the boss's attacks the player's.)
			cards.Add("DALA_BOSS_04p", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Minion mine = SummonFromDeck(g, c, s, p => true);
					Minion theirs = SummonFromDeck(g, c.Opponent, s, p => true);
					if (mine != null && theirs != null && !mine.IsDead && !theirs.IsDead && theirs.Zone?.Type == Zone.PLAY)
						EffectAttack(g, mine, theirs);
				})
			}));

			// ---------------------------------------------------------------- 05 The Amazing "Bonepaw"
			// [DALA_BOSS_05p] Mana Echoes (2) — Cast a random spell that was cast this game (targets chosen randomly). (Live
			// live-session-20261001-162435 13124: a new copy cast inside the POWER block — SHOW_BIG_CARD, its POWER block, GRAVEYARD;
			// the spells either player cast, each cast one entry.)
			cards.Add("DALA_BOSS_05p", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var cast = SpellsCast(c).Concat(SpellsCast(c.Opponent)).Select(h => h.SourceCard).Where(x => x != null).ToList();
					if (HeistG1Pick(g, cast) is Card spell) new CastRandomSpellTask { Spell = spell }.Process(g, c, s, null);
				})
			}));

			// ---------------------------------------------------------------- 06 Dazzik "Hellscream"
			// [DALA_BOSS_06p] "Armor Up" (2) — Gain 2 Armor. [06px] "Tank Up!" (2) — Gain 4 Armor.
			cards.Add("DALA_BOSS_06p", new CardDef(new Power { PowerTask = new ArmorTask(V("DALA_BOSS_06p", "armor", 2)) }));
			cards.Add("DALA_BOSS_06px", new CardDef(new Power { PowerTask = new ArmorTask(V("DALA_BOSS_06px", "armor", 4)) }));
			// [DALA_BOSS_06dk] "Scourgelord" Dazzik (8, hero) — Battlecry: Equip a 4/3 Shadowmourne that also damages adjacent minions.
			// Scourgelord Garrosh's text and Hero Power (ICC_834h); his 10 Armor is card data.
			cards.Add("DALA_BOSS_06dk", CardDef.Like("ICC_834"));

			// ---------------------------------------------------------------- 07 Flight Master Belnaara
			// [DALA_BOSS_07p] Take Flight! — Passive. After taking 15 damage, Belnaara will take flight. (@ damage remaining)
			cards.Add("DALA_BOSS_07p", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) => HeistG1TakeFlight(g, c, s as IPlayable))
				}
			}));
			// [DALA_BOSS_07p2] Flying! — Passive. Belnaara will land in 2 turns. (@ turns remaining)
			cards.Add("DALA_BOSS_07p2", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) => HeistG1Land(g, c, s as IPlayable))
				}
			}));
			// [DALA_BOSS_07e] Flying! — Belnaara is Immune for now.
			// (live session-20261120-132514 37054: CANT_ATTACK, IMMUNE, HERO_FLYING on her hero while flying; all back at landing, 39096)
			cards.Add("DALA_BOSS_07e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.CANT_ATTACK, EffectOperator.SET, 1), Effects.Immune,
				new Effect(GameTag.HERO_FLYING, EffectOperator.SET, 1)) }));

			// ---------------------------------------------------------------- 08 Applebough
			// [DALA_BOSS_08p] Apple Toss (2) — Deal $1 damage to the lowest Health enemy. If it dies, repeat this. [08px] (3): $2.
			foreach ((string id, int dmg) in new[] { ("DALA_BOSS_08p", 1), ("DALA_BOSS_08px", 2) })
				cards.Add(id, new CardDef(new Power
				{
					PowerTask = new CustomTask((g, c, s, t, stack) => HeistG1AppleToss(g, c, s, V(id, "damage", dmg)))
				}));

			// ---------------------------------------------------------------- 09 Archivist Oshi
			// [DALA_BOSS_09p] Repeat History (2) — Summon a random friendly minion that died this turn. [09px]: … this game. (Live
			// live-session-20261002-204752 32716: a new copy, FULL_ENTITY in the POWER block — Generic.NewFromGraveyard.)
			ISimpleTask Resurrect(Func<Controller, List<IPlayable>> pool) => new CustomTask((g, c, s, t, stack) =>
			{
				if (c.BoardZone.IsFull || !(HeistG1Pick(g, pool(c).Where(p => p is Minion).ToList()) is IPlayable dead)) return;
				Generic.SummonBlock.Invoke(g, (Minion)Generic.NewFromGraveyard(c, dead), -1, s);
			});
			cards.Add("DALA_BOSS_09p", new CardDef(new Power
			{
				// the client's REQ_FRIENDLY_MINION_DIED_THIS_TURN (the engine has no check for it)
				PlayableIf = (PlayReq.REQ_FRIENDLY_MINION_DIED_THIS_TURN, ctl => Generic.DiedThisTurn(ctl).Count > 0),
				PowerTask = Resurrect(Generic.DiedThisTurn)
			}));
			cards.Add("DALA_BOSS_09px", new CardDef(new Power
			{
				PlayableIf = (PlayReq.REQ_FRIENDLY_MINION_DIED_THIS_GAME, ctl => ctl.DiedThisGameCards.Any(p => p is Minion)),
				PowerTask = Resurrect(ctl => ctl.DiedThisGameCards.ToList())
			}));

			// ---------------------------------------------------------------- 10 Mo Eniwhiskers
			// [DALA_BOSS_10p] Street Smarts — Passive. Whenever you play a Coin, draw a card. [10px]: Whenever a player plays a Coin.
			// (Live live-session-20261005-140614 38402, 39948, 40026, 43155: TRIGGER on the Hero Power, TRIGGER_VISUAL, after the Coin's
			// SLUSH_TIME and before its POWER block — "Whenever": the play trigger; the boss draws when the player plays one, a
			// Counterfeit Coin too: Generic.CountsAsCoin.)
			foreach ((string id, TriggerSource who) in new[] { ("DALA_BOSS_10p", TriggerSource.FRIENDLY), ("DALA_BOSS_10px", TriggerSource.ALL) })
				cards.Add(id, new CardDef(new Power
				{
					Trigger = new Trigger(TriggerType.PLAY_CARD)
					{
						TriggerSource = who,
						Condition = new SelfCondition(p => Generic.CountsAsCoin(p)),
						SingleTask = new DrawTask(V(id, "draw", 1))
					}
				}));

			// ---------------------------------------------------------------- 11 Noz Timbertail
			// [DALA_BOSS_11p] Backstabber — Passive. Whenever a Stealthed minion is summoned, give it +1 Attack. [11px]: +2 (DALA_BOSS_11ex).
			// (Live 11px: TRIGGER_VISUAL TRIGGER on the Hero Power right after the minion enters, before its Battlecry; TARGET = it.)
			foreach ((string id, string buff) in new[] { ("DALA_BOSS_11p", "DALA_BOSS_11e"), ("DALA_BOSS_11px", "DALA_BOSS_11ex") })
				cards.Add(id, new CardDef(new Power
				{
					Trigger = new Trigger(TriggerType.SUMMON)
					{
						TriggerSource = TriggerSource.ALL_MINIONS,
						Condition = new SelfCondition(p => p is Minion m && m.HasStealth),
						SingleTask = new AddEnchantmentTask(buff, EntityType.TARGET)
					}
				}));
			cards.Add("DALA_BOSS_11e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("DALA_BOSS_11p", "attack", 1))) }));
			cards.Add("DALA_BOSS_11ex", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("DALA_BOSS_11px", "attack", 2))) }));

			// ---------------------------------------------------------------- 12 Cravitz Lorent
			// [DALA_BOSS_12p] Forbidden Love (3) / [12px] (2) — Destroy a random minion for each player.
			ISimpleTask forbiddenLove = new CustomTask((g, c, s, t, stack) =>
			{
				Minion mine = HeistG1Pick(g, c.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList());
				Minion theirs = HeistG1Pick(g, c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList());
				foreach (Minion m in new[] { mine, theirs }.Where(m => m != null))
					Generic.Destroy(m, s, many: true);
			});
			cards.Add("DALA_BOSS_12p", new CardDef(new Power { PowerTask = forbiddenLove }));
			cards.Add("DALA_BOSS_12px", new CardDef(new Power { PowerTask = forbiddenLove }));

			// ---------------------------------------------------------------- 13 Albin Eastoft
			// [DALA_BOSS_13p] Slice and Dice (2) — Deal $1-$3 damage to three random minions. (Three different minions, each its own roll.)
			cards.Add("DALA_BOSS_13p", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var pool = HeistG1Minions(c);
					for (int i = 0; i < V("DALA_BOSS_13p", "minions", 3) && pool.Count > 0; i++)
					{
						Minion m = HeistG1Pick(g, pool);
						pool.Remove(m);
						Generic.DamageCharFunc((IPlayable)s, m, g.Random.Next(V("DALA_BOSS_13p", "min", 1), V("DALA_BOSS_13p", "max", 3) + 1), false);
					}
				})
			}));

			// ---------------------------------------------------------------- 14 Tierra Blythe
			// [DALA_BOSS_14p] Order Up! (2) — Summon a 2/2 Kind Waitress. [14px]: two.
			cards.Add("DALA_BOSS_14p", new CardDef(new Power { PowerTask = new SummonTask("DALA_BOSS_14t", V("DALA_BOSS_14p", "waitresses", 1)) }));
			cards.Add("DALA_BOSS_14px", new CardDef(new Power { PowerTask = new SummonTask("DALA_BOSS_14t", V("DALA_BOSS_14px", "waitresses", 2)) }));
			// [DALA_BOSS_14t] "Kind" Waitress (2) — Enemies attacking this have a 50% chance to attack someone else. (The Forgetful rule seen
			// from the defender — wiki tags it Forgetful: the new target any other character on her side, Generic Proposed Attack.)
			cards.Add("DALA_BOSS_14t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.ATTACKED_SELF,
					ProposedAttack = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventSource is ICharacter attacker && attacker.Controller != c)
							ForgetfulAttack(g, attacker, V("DALA_BOSS_14t", "chance %", 50));
					})
				}
			}));

			// ---------------------------------------------------------------- 15 Moon Priestess Nici
			// [DALA_BOSS_15p] Blessing of Elune — Passive. All healing is doubled. (Live live-session-20260929-183021 5251: ALL_HEALING_DOUBLE
			// 1 on BOTH players.)
			cards.Add("DALA_BOSS_15p", new CardDef(new Power
			{
				Aura = new Aura(AuraType.CONTROLLERS, new Effect(GameTag.ALL_HEALING_DOUBLE, EffectOperator.ADD, 1))
			}));

			// ---------------------------------------------------------------- 16 Alchemist Wendy
			// [DALA_BOSS_16p] Equivalent Exchange (1) / [16px] (0) — Swap a random card in each player's hand. (Live
			// live-session-20261004-222041 31229: the player's card to the boss's hand, the boss's to the player's — the same entities —,
			// then the marker DALA_BOSS_16e on each.)
			ISimpleTask exchange = new CustomTask((g, c, s, t, stack) =>
			{
				IPlayable mine = HeistG1Pick(g, c.HandZone.GetAll().ToList());
				IPlayable theirs = HeistG1Pick(g, c.Opponent.HandZone.GetAll().ToList());
				if (mine == null || theirs == null) return;
				Generic.AddHandPhase.Invoke(c, Generic.GiveTo(theirs, c, Zone.HAND));
				Generic.AddHandPhase.Invoke(c.Opponent, Generic.GiveTo(mine, c.Opponent, Zone.HAND));
				foreach (IPlayable swapped in new[] { theirs, mine }.Where(x => x.Zone?.Type == Zone.HAND))
					Give(g, s, swapped, "DALA_BOSS_16e");
			});
			cards.Add("DALA_BOSS_16p", new CardDef(new Power { PowerTask = exchange }));
			cards.Add("DALA_BOSS_16px", new CardDef(new Power { PowerTask = exchange }));

			// ---------------------------------------------------------------- 17 Tala Stonerage
			// [DALA_BOSS_17p] Twin Paths — Passive. Your Choose One cards and powers have both effects combined. (Ossirian Tear's rule.)
			cards.Add("DALA_BOSS_17p", new CardDef(new Power { Aura = new Aura(AuraType.CONTROLLER, new Effect(GameTag.CHOOSE_BOTH, EffectOperator.SET, 1)) }));

			// ---------------------------------------------------------------- 18 Disidra Stormglory
			// [DALA_BOSS_18p] Totemic Summons (2) / [18px] (1) — Summon ANY random Totem. (Live: any collectible Totem — Primalfin Totem,
			// Flametongue Totem, Grand Totem Eys'or, ETC_105, GDB_321…; FULL_ENTITY in the POWER block.)
			ISimpleTask totem = new CustomTask((g, c, s, t, stack) =>
				Summon(g, c, RandomBoardMinion(g, x => x.IsRace(Race.TOTEM)), s));
			cards.Add("DALA_BOSS_18p", new CardDef(new Power { PowerTask = totem }));
			cards.Add("DALA_BOSS_18px", new CardDef(new Power { PowerTask = totem }));

			// ---------------------------------------------------------------- 19 Linzi Redgrin
			// [DALA_BOSS_19p] Lil' Eviscerate (2) / [19px] (1) — Deal 1 damage. Combo: 2 instead. (Client COMBO 1 on both; the Hero
			// Power runs its ComboTask when Combo is active: Actions/HeroPower.cs.)
			foreach (string id in new[] { "DALA_BOSS_19p", "DALA_BOSS_19px" })
				cards.Add(id, new CardDef(TargetAny, new Power
				{
					PowerTask = new DamageTask(V(id, "damage", 1), EntityType.TARGET),
					ComboTask = new DamageTask(V(id, "combo damage", 2), EntityType.TARGET)
				}));

			// ---------------------------------------------------------------- 20 Vas'no
			// [DALA_BOSS_20p] Stormswell — Passive. Your spells with Overload cast twice. [20px]: thrice. (Generic.OVERLOAD_SPELLS_EXTRA_CASTS)
			cards.Add("DALA_BOSS_20p", new CardDef(new Power
			{
				Aura = new Aura(AuraType.CONTROLLER, new Effect(Generic.OVERLOAD_SPELLS_EXTRA_CASTS, EffectOperator.ADD, V("DALA_BOSS_20p", "extra casts", 1)))
			}));
			cards.Add("DALA_BOSS_20px", new CardDef(new Power
			{
				Aura = new Aura(AuraType.CONTROLLER, new Effect(Generic.OVERLOAD_SPELLS_EXTRA_CASTS, EffectOperator.ADD, V("DALA_BOSS_20px", "extra casts", 2)))
			}));

			// ---------------------------------------------------------------- 21 Whirt the All-Knowing
			// [DALA_BOSS_21p] Prediction (1) — Put a random class Secret into the battlefield. (Live: 29 uses, Hunter / Mage / Paladin /
			// Rogue Secrets — any class's; one it can hold: Generic.PutSecretIntoPlay.)
			cards.Add("DALA_BOSS_21p", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Card secret = RandomCard(g, x => x.Type == CardType.SPELL && x.IsSecret && Generic.CanHoldSecret(c, x));
					if (secret != null) Generic.PutSecretIntoPlay(c, (Spell)Entity.FromCard(c, secret, new EntityData { [GameTag.CREATOR] = s.Id }));
				})
			}));

			// ---------------------------------------------------------------- 22 Bookmaster Bae Chao
			// [DALA_BOSS_22p] Shhh! (3) — Silence ALL minions.
			cards.Add("DALA_BOSS_22p", new CardDef(new Power { PowerTask = new SilenceTask(EntityType.ALLMINIONS) }));
			// [DALA_BOSS_22px] Shhh! — Passive. After a minion is summoned, Silence it. (Live live-session-20261003-185040 21539: TRIGGER on
			// the Hero Power at the end of the minion's PLAY block, after its Battlecry: SILENCED.)
			cards.Add("DALA_BOSS_22px", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.ALL_MINIONS,
					SingleTask = new SilenceTask(EntityType.TARGET)
				}
			}));

			// ---------------------------------------------------------------- 23 Sharky McFin
			// [DALA_BOSS_23p] Sharkbite (2) — Destroy a random damaged minion. [23px]: … damaged enemy minion.
			cards.Add("DALA_BOSS_23p", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => HeistG1DestroyRandom(g, s, HeistG1Minions(c).Where(m => m.Damage > 0).ToList()))
			}));
			cards.Add("DALA_BOSS_23px", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					HeistG1DestroyRandom(g, s, HeistG1Minions(c).Where(m => m.Damage > 0 && m.Controller != c).ToList()))
			}));

			// ---------------------------------------------------------------- 24 Ranger Ar'ha
			// [DALA_BOSS_24p] Ar'ha's Call (2) / [24px] (0) — Your next Battlecry triggers an additional time. (No live use; the player
			// enchantment DALA_BOSS_24e "Triggers additional Battlecries" — the "next Battlecry twice" rule without "this turn".)
			cards.Add("DALA_BOSS_24p", new CardDef(new Power { PowerTask = new AddEnchantmentTask("DALA_BOSS_24e", EntityType.CONTROLLER) }));
			cards.Add("DALA_BOSS_24px", new CardDef(new Power { PowerTask = new AddEnchantmentTask("DALA_BOSS_24e", EntityType.CONTROLLER) }));
			cards.Add("DALA_BOSS_24e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.CONTROLLER, new Effect(GameTag.EXTRA_BATTLECRIES_BASE, EffectOperator.ADD, 1)),
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p.Card[GameTag.BATTLECRY] > 0),
					SingleTask = new CustomTask((g, c, s, t, stack) => (s as Enchantment)?.Remove())
				}
			}));

			// ---------------------------------------------------------------- 25 Jepetto Joybuzz
			// [DALA_BOSS_25p] Assembly — Passive. After a player draws a minion, they summon a 1/1 copy of it. [25px]: After you draw a
			// minion, summon a 1/1 copy of it. (DALA_BOSS_25e "Toyification": Stats changed to 1/1.)
			foreach ((string id, TriggerSource who) in new[] { ("DALA_BOSS_25p", TriggerSource.ALL), ("DALA_BOSS_25px", TriggerSource.FRIENDLY) })
				cards.Add(id, new CardDef(new Power
				{
					Trigger = new Trigger(TriggerType.DRAW)
					{
						TriggerSource = who,
						// (not the opening hand / mulligan: Generic.DuringOpeningHand)
						Condition = new SelfCondition(p => p is Minion && !Generic.DuringOpeningHand(p.Game)),
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							IPlayable drawn = t ?? g.CurrentEventData?.EventSource as IPlayable;
							if (drawn is Minion && drawn.Zone?.Type == Zone.HAND)
								HeistG1SummonToy(g, drawn.Controller, s, drawn.Card, drawn);
						})
					}
				}));
			cards.Add("DALA_BOSS_25e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("DALA_BOSS_25e", "stats", 1))) }));
			// [DALA_BOSS_25t] Hoard of Toys (4) — Summon 1/1 copies of five random minions.
			cards.Add("DALA_BOSS_25t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("DALA_BOSS_25t", "minions", 5) && !c.BoardZone.IsFull; i++)
						HeistG1SummonToy(g, c, s, RandomBoardMinion(g, x => true));
				})
			}));
		}
	}
}
