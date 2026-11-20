// HsServer addition (not upstream SabberStone): Perils in Paradise — chunk 2: Hunter, Mage and Druid cards and the Travel
// Agency's (spec: research spec_chunk2; shared pieces: IslandCardsGenHs.cs, Actions/Paradise.cs). Every number goes through
// CardValues (V).
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
		/// <summary>
		/// "Light … on fire. In 3 turns, any still in hand are destroyed" (Immolate TID_718 on the opponent's hand, Burndown
		/// WORK_026 on yours; wiki Burndown: "works similarly to Immolate"): the player enchantment counting at the end of each
		/// of its owner's turns, the cast turn first (user ruling 2026-10-06); at <paramref name="turns"/> the marked cards still
		/// in that hand leave for the graveyard — not a discard (wiki Immolate: "doesn't discard the affected cards").
		/// </summary>
		/// <param name="anywhere">"They are destroyed in 3 turns" (Keeper of Flame): the marked cards burn wherever they are — in hand
		/// or deck to the graveyard, on the battlefield destroyed.</param>
		private static CardDef FireCounter(string markId, string valueCard, bool opponentsHand, bool anywhere = false) => new CardDef(new Power
		{
			Trigger = new Trigger(TriggerType.TURN_END)
			{
				SingleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Enchantment e)) return;
					e[GameTag.TAG_SCRIPT_DATA_NUM_1] = e[GameTag.TAG_SCRIPT_DATA_NUM_1] + 1;
					if (e[GameTag.TAG_SCRIPT_DATA_NUM_1] < V(valueCard, "turns", 3)) return;
					Controller victim = opponentsHand ? c.Opponent : c;
					foreach (IPlayable card in victim.HandZone.Where(x => x.AppliedEnchantments?.Any(a => a.Card.Id == markId) == true).ToList())
					{
						Generic.NoteAffected(card, e.Creator?.Id ?? 0);
						victim.GraveyardZone.Add(victim.HandZone.Remove(card));
					}
					if (anywhere)
					{
						foreach (IPlayable card in victim.DeckZone.Where(x => x.AppliedEnchantments?.Any(a => a.Card.Id == markId) == true).ToList())
							victim.GraveyardZone.Add(victim.DeckZone.Remove(card));
						foreach (Minion m in victim.BoardZone.GetAll().Where(x => x.AppliedEnchantments?.Any(a => a.Card.Id == markId) == true).ToList())
							Generic.Destroy(m, e.Creator);
					}
					e.Remove();
				})
			}
		});

		/// <summary>Summon each card and let every one still alive attack <paramref name="target"/> (Furious Fowls) or a random
		/// enemy (Tsunami): all summoned first, then each attacks in turn (Nagrand Slam ruling).</summary>
		private static void IslandSummonThenAttack(Game g, Controller c, IEntity source, Card card, int n, ICharacter target)
		{
			var summoned = new List<Minion>();
			for (int i = 0; i < n; i++)
				if (IslandSummon(g, c, card, source) is Minion m) summoned.Add(m);
			foreach (Minion m in summoned)
			{
				if (m.IsDead || m.ToBeDestroyed || m.Zone?.Type != Zone.PLAY) continue;
				ICharacter defender = target;
				if (defender == null)
				{
					var enemies = AttackableEnemies(c);
					if (enemies.Count == 0) break;
					defender = enemies[g.Random.Next(enemies.Count)];
				}
				else if (defender.IsDead || defender.ToBeDestroyed || defender.Zone?.Type != Zone.PLAY) break;
				EffectAttack(g, m, defender);
			}
		}

		private static void IslandChunk2(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Hunter
			// [VAC_407] Chatty Macaw (3) — Battlecry: Repeat the last spell you cast at an enemy (at a random enemy if possible).
			// (Forum t/148838: a spell played at an enemy; PlayHistory = played spells only — user ruling 2026-10-07.)
			cards.Add("VAC_407", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var last = c.PlayHistory.LastOrDefault(h => h.SourceCard?.Type == CardType.SPELL && h.TargetController == c.Opponent.PlayerId && h.TargetCard != null);
					if (last.SourceCard != null) Generic.RepeatCard(g, c, s, last.SourceCard, enemiesIfPossible: true);
				})
			}));
			// [VAC_408] Birdwatching (2) — Discover a minion from your deck. Give all copies of it +2/+1 (wherever they are). (Forum
			// t/151123: the copies at that moment; hand, deck and play.)
			cards.Add("VAC_408", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					Generic.DiscoverFromDeck(c, s, Generic.PickFromDeck(c, p => p is Minion), (g2, c2, s2, card) =>
					{
						Card picked = card.Card;
						Generic.Draw(c2, card);
						GiveAll(g2, s2, c2.HandZone.GetAll().Concat(c2.DeckZone.GetAll()).Concat(c2.BoardZone.GetAll()).Where(p => p.Card == picked).ToList(), "VAC_408e");
					}))
			}));
			cards.Add("VAC_408e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("VAC_408", "attack", 2)), Effects.Health_N(V("VAC_408", "health", 1))) }));
			// [VAC_409] Parrot Sanctuary (2, Location) — Your next Battlecry minion costs (1) less. After you play a Battlecry minion,
			// reopen this. (Patch 31.2.0: the card as played counts, even if it transformed.)
			cards.Add("VAC_409", new CardDef(new Power
			{
				PowerTask = new AddEnchantmentTask("VAC_409e", EntityType.CONTROLLER),
				Trigger = Reopens(TriggerType.AFTER_PLAY_MINION, TriggerSource.FRIENDLY,
					new SelfCondition(p => p.Controller.PlayHistory.LastOrDefault().SourceCard?[GameTag.BATTLECRY] > 0))
			}));
			cards.Add("VAC_409e", NextCardCost(Effects.ReduceCost(V("VAC_409", "discount", 1)), new SelfCondition(p => p is Minion && p.Card[GameTag.BATTLECRY] > 0), null));
			// [VAC_410] Furious Fowls (6) — Choose an enemy. Summon two 3/3 Birds with Immune while attacking to attack it.
			cards.Add("VAC_410", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter target) IslandSummonThenAttack(g, c, s, Cards.FromId("VAC_410t"), V("VAC_410", "birds", 2), target);
				})
			}));
			// [VAC_412] Catch of the Day (1) — Rush. Battlecry: Summon a 2/1 Worm for your opponent.
			cards.Add("VAC_412", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Summon(g, c.Opponent, Cards.FromId("VAC_412t"), s)) }));
			// [VAC_413] Ranger Gilly (5) — Warrior Tourist. At the end of your turn, get a 2/3 Crocolisk. Deathrattle: Give all minions
			// in your hand +2/+3.
			cards.Add("VAC_413", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new AddCardTo("VAC_413t", EntityType.HAND) },
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.HandZone.GetAll().Where(p => p is Minion && !(p is Location)).ToList(), "VAC_413e3"))
			}));
			cards.Add("VAC_413e3", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("VAC_413", "attack", 2)), Effects.Health_N(V("VAC_413", "health", 3))) }));
			// [VAC_415] Sasquawk (7) — Battlecry: Repeat each card you played last turn. (Forum: minions resummoned without Battlecry,
			// spells recast at random targets.)
			cards.Add("VAC_415", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (PlayHistoryEntry h in c.PlayHistory.Where(h => h.Turn == g.Turn - 2 && h.SourceCard != null).ToList())
						Generic.RepeatCard(g, c, s, h.SourceCard);
				})
			}));
			// [VAC_416] Death Roll (5) — Destroy an enemy minion. Deal damage equal to its Attack randomly split among all enemies.
			// (Spell Damage adds hits: @spelldmg + AFFECTED_BY_SPELL_POWER.)
			cards.Add("VAC_416", new CardDef(TargetEnemyMinion, new Power
			{
				PowerTask = ComplexTask.Create(new GetGameTagTask(GameTag.ATK, EntityType.TARGET), new DestroyTask(EntityType.TARGET),
					new EnqueueNumberTask(ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, 1), spellDmg: true))
			}));
			// [VAC_960] Trusty Fishing Rod (3, weapon) — After your hero attacks, summon a 1-Cost minion from your deck.
			cards.Add("VAC_960", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) => SummonFromDeck(g, c, s, p => p is Minion && p.Cost == V("VAC_960", "cost", 1)))
				}
			}));
			// [VAC_961] Pet Parrot (2) — Battlecry: Repeat the last 1-Cost card you played. (The cost it was played for.)
			cards.Add("VAC_961", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var last = c.PlayHistory.LastOrDefault(h => h.SourceId != s.Id && h.Cost == V("VAC_961", "cost", 1) && h.SourceCard != null);
					if (last.SourceCard != null) Generic.RepeatCard(g, c, s, last.SourceCard);
				})
			}));
			// [WORK_018] Workhorse (3) — Deathrattle: Summon two 2/1 Ponies.
			cards.Add("WORK_018", new CardDef(new Power { DeathrattleTask = new SummonTask("WORK_018t", V("WORK_018", "ponies", 2), SummonSide.DEATHRATTLE) }));
			// [WORK_019] Cash Cow (5) — Taunt. Whenever this takes damage, get a Coin.
			cards.Add("WORK_019", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE) { TriggerSource = TriggerSource.SELF, SingleTask = new AddCardTo("GAME_005", EntityType.HAND) }
			}));
			// [WORK_020] Monkey Business (4) — Add 8 Bananas to your hand. Any that can't fit are randomly fed to friendly minions in
			// play. (Wiki: each one that doesn't fit gives a random friendly minion +1/+1.)
			cards.Add("WORK_020", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = V("WORK_020", "bananas", 8);
					for (int i = 0; i < n; i++)
					{
						if (!c.HandZone.IsFull) { AddToHand(c, Cards.FromId("EX1_014t")); continue; }
						var minions = c.BoardZone.GetAll().Where(m => !m.IsDead && !m.Untouchable).ToList();
						if (minions.Count > 0) Give(g, s, minions[g.Random.Next(minions.Count)], "EX1_014te");
					}
				})
			}));

			// ---------------------------------------------------------------- Mage
			// [VAC_424] Raylla, Sand Sculptor (4) — Paladin Tourist. After you cast a spell, summon a random 2-Cost minion and give it
			// Divine Shield.
			cards.Add("VAC_424", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (IslandSummon(g, c, RandomMinionOfCost(g, V("VAC_424", "cost", 2)), s, s is Minion me && me.Zone?.Type == Zone.PLAY ? me.ZonePosition + 1 : -1) is Minion m)
							m.HasDivineShield = true;
					})
				}
			}));
			// [VAC_428] Go with the Flow (1) — Choose a minion. If it's an enemy, Freeze it. If it's friendly, give it Spell Damage +1.
			cards.Add("VAC_428", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					if (m.Controller != c) IslandFreeze(g, c, s, m);
					else Give(g, s, m, "VAC_428e");
				})
			}));
			cards.Add("VAC_428e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.SPELLPOWER, EffectOperator.ADD, V("VAC_428", "spell damage", 1))) }));
			// [VAC_431] Under the Sea (6) — Draw a different spell. Summon a random minion of that spell's Cost. ("Different": not
			// another Under the Sea.)
			cards.Add("VAC_431", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (DrawOne(g, c, p => p is Spell && p.Card.Name != ((IPlayable)s).Card.Name) is IPlayable drawn)
						Summon(g, c, RandomMinionOfCost(g, drawn.Cost), s);
				})
			}));
			// [VAC_435] Marooned Archmage (3) — Your first spell each turn costs (2) less. (The first one played: PlayHistory.)
			cards.Add("VAC_435", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("VAC_435", "discount", 2)))
				{
					Condition = new SelfCondition(p => p is Spell && !p.Controller.PlayHistory.Any(h => h.Turn == p.Game.Turn && h.SourceCard?.Type == CardType.SPELL)),
					Restless = true
				}
			}));
			// [VAC_443] Surfalopod (5) — Battlecry: The next spell you draw is Cast When Drawn. (Runic Adornment's pattern:
			// Generic.CastDrawnCopy.)
			cards.Add("VAC_443", new CardDef(new Power { PowerTask = new AddEnchantmentTask("VAC_443e", EntityType.CONTROLLER) }));
			cards.Add("VAC_443e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DRAW)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p is Spell),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e) || !((t ?? g.CurrentEventData?.EventSource as IPlayable) is Spell drawn) || drawn.Zone?.Type != Zone.HAND) return;
						e.Remove();
						Generic.CastDrawnCopy(c, e, drawn, false, false);
					})
				}
			}));
			// [VAC_509] Tsunami (8) — Summon three 3/6 Water Elementals that Freeze. They attack random enemies.
			cards.Add("VAC_509", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => IslandSummonThenAttack(g, c, s, Cards.FromId("VAC_509t"), V("VAC_509", "elementals", 3), null))
			}));
			// [VAC_522] Tide Pools (3, Location) — Discover a spell that costs (3) or less. After you cast a spell, reopen this.
			cards.Add("VAC_522", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL && x.Cost <= V("VAC_522", "cost", 3)),
				Trigger = Reopens(TriggerType.AFTER_CAST)
			}));
			// [VAC_524] King Tide (4) — Battlecry: Both players' spells cost (5) until the end of your next turn.
			cards.Add("VAC_524", new CardDef(new Power
			{
				// (2026-10-08: the opponent's half sits on the opponent's player — live VAC_524e2 ATTACHED = the enemy player, CONTROLLER = ours)
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("VAC_524e", EntityType.CONTROLLER), new AddEnchantmentTask("VAC_524e2", EntityType.OP_CONTROLLER))
			}));
			// ("your next turn": the King Tide player's turn ends — the enchantment's CONTROLLER on both halves, live)
			Trigger kingTideEnds = new Trigger(TriggerType.TURN_END)
			{
				EitherTurn = true,
				SingleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Enchantment e) || g.CurrentPlayer != e.Controller) return;
					e[GameTag.TAG_SCRIPT_DATA_NUM_1] = e[GameTag.TAG_SCRIPT_DATA_NUM_1] + 1;
					if (e[GameTag.TAG_SCRIPT_DATA_NUM_1] >= 2) e.Remove();
				})
			};
			cards.Add("VAC_524e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.SetCost(V("VAC_524", "cost", 5))) { Condition = SelfCondition.IsSpell },
				Trigger = kingTideEnds
			}));
			cards.Add("VAC_524e2", new CardDef(new Power
			{
				Aura = new Aura(AuraType.OP_HAND, Effects.SetCost(V("VAC_524", "cost", 5))) { Condition = SelfCondition.IsSpell },
				Trigger = kingTideEnds
			}));
			// [VAC_953] Rising Waves (3) — Deal $2 damage to all minions. If none die, deal $2 more. (Live: no deaths between the
			// waves — mortal wounds decide; an empty board gets both; each wave its own recorded effect.)
			cards.Add("VAC_953", new CardDef(new Power
			{
				OwnFx = true,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					List<ICharacter> Minions() => c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => !m.IsDead && !m.ToBeDestroyed).Cast<ICharacter>().ToList();
					var first = Minions();
					Generic.WholeFx((IPlayable)s, () => DamageAll(s, first, V("VAC_953", "damage", 2), true));
					if (first.Any(m => m.IsDead || m.ToBeDestroyed || m.Health <= 0)) return;
					var second = Minions();
					Generic.WholeFx((IPlayable)s, () => DamageAll(s, second, V("VAC_953", "damage", 2), true));
				})
			}));
			// [WORK_012] Huddle Up (7) — Fill your board with random Naga.
			cards.Add("WORK_012", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					while (!c.BoardZone.IsFull)
					{
						Card naga = RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(NAGA));
						if (naga == null || IslandSummon(g, c, naga, s) == null) break;
					}
				})
			}));
			// [WORK_026] Burndown (3) — Draw 3 cards and light them on fire. In 3 turns, any still in hand are destroyed!
			cards.Add("WORK_026", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("WORK_026", "cards", 3); i++)
						if (Generic.Draw(c) is IPlayable drawn && drawn.Zone?.Type == Zone.HAND) Give(g, s, drawn, "WORK_026e3");
					Generic.AddEnchantmentBlock(g, Cards.FromId("WORK_026e"), (IPlayable)s, c, 0, 0, 0);
				})
			}));
			cards.Add("WORK_026e3", new CardDef(new Power()));
			cards.Add("WORK_026e", FireCounter("WORK_026e3", "WORK_026", opponentsHand: false));
			// [WORK_063] Portalmancer Skyla (6) — Rogue Tourist. Battlecry: Swap the Costs of the lowest and highest Cost spells in
			// your hand.
			cards.Add("WORK_063", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var spells = c.HandZone.GetAll().Where(p => p is Spell).ToList();
					if (spells.Count < 2) return;
					int lowCost = spells.Min(p => p.Cost), highCost = spells.Max(p => p.Cost);
					if (lowCost == highCost) return;
					var lows = spells.Where(p => p.Cost == lowCost).ToList();
					var highs = spells.Where(p => p.Cost == highCost).ToList();
					IPlayable lo = lows[g.Random.Next(lows.Count)], hi = highs[g.Random.Next(highs.Count)];
					Give(g, s, lo, "WORK_063e", highCost);
					Give(g, s, hi, "WORK_063e", lowCost);
				})
			}));
			cards.Add("WORK_063e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(0)) { UseScriptTag = true } }));

			// ---------------------------------------------------------------- Druid
			// [VAC_506] Cruise Captain Lora (6) — Battlecry: Summon 2 random locations. (Live NIGHT 14226: usable at once.)
			cards.Add("VAC_506", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("VAC_506", "locations", 2); i++) Summon(g, c, RandomCard(g, x => x.Type == CardType.LOCATION), s);
				})
			}));
			// [VAC_508] Trail Mix (2) — Gain 2 Mana Crystals next turn only.
			cards.Add("VAC_508", new CardDef(new Power { PowerTask = new AddEnchantmentTask("VAC_508e", EntityType.CONTROLLER) }));
			cards.Add("VAC_508e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						Generic.AddTempMana(c, V("VAC_508", "mana", 2));
						if (s is Enchantment e) e.Remove();
					})
				}
			}));
			// [VAC_511] Dozing Dragon (5) — Dormant for 2 turns. While Dormant, summon a 3/5 Dragon with Taunt at the end of your turn.
			// (Live VFX 150092: right of the Dragon, from the turn it was played.)
			cards.Add("VAC_511", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					WhileDormant = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion me && me[GameTag.DORMANT] > 0) Summon(g, c, Cards.FromId("VAC_511t"), s, me.ZonePosition + 1);
					})
				}
			}));
			// [VAC_517] Hiking Trail (3, Location) — Discover a Taunt minion. After you gain Armor, reopen this.
			cards.Add("VAC_517", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x[GameTag.TAUNT] > 0),
				Trigger = Reopens(TriggerType.ARMOR)
			}));
			// [VAC_518] Tortollan Traveler (3) — Taunt. Deathrattle: Draw another Taunt minion. Reduce its Cost by (2).
			cards.Add("VAC_518", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (DrawOne(g, c, p => p is Minion m && m.HasTaunt) is IPlayable drawn && drawn.Zone?.Type == Zone.HAND) Give(g, s, drawn, "VAC_518e");
				})
			}));
			cards.Add("VAC_518e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("VAC_518", "discount", 2))) }));
			// [VAC_519] Mistah Vistah (5) — Mage Tourist. Battlecry: In 3 turns, replay every spell you've cast between now and then.
			// (vicioussyndicate: at the start of the 3rd turn after, "every spell you've played since the moment you played Mistah
			// Vistah".)
			cards.Add("VAC_519", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var vista = (Spell)Entity.FromCard(c, Cards.FromId("VAC_519t3"), new EntityData
					{
						[GameTag.CREATOR] = s.Id, [HsTag.PLAY_HISTORY_MARK] = c.PlayHistory.Count
					});
					Generic.PutSecretIntoPlay(c, vista);
				})
			}));
			cards.Add("VAC_519t3", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Spell vista) || vista.Zone?.Type != Zone.SECRET) return;
						vista.QuestProgress++;
						if (vista.QuestProgress < vista.Card[GameTag.QUEST_PROGRESS_TOTAL]) return;
						foreach (PlayHistoryEntry h in c.PlayHistory.Skip(vista[HsTag.PLAY_HISTORY_MARK]).Where(h => h.SourceCard?.Type == CardType.SPELL).ToList())
							Generic.RepeatCard(g, c, vista, h.SourceCard);
						vista.ActivatedTrigger?.Remove();
						if (vista.Zone?.Type == Zone.SECRET) c.GraveyardZone.Add(c.SecretZone.Remove(vista));
					})
				}
			}));
			// [VAC_907] Sleep Under the Stars (8) — Choose Thrice - Draw 2 cards; Gain 5 Armor; Refresh 3 Mana Crystals.
			cards.Add("VAC_907", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.ChooseMultiple(c, (IPlayable)s, Generic.ChooseCount(((IPlayable)s).Card)))
			}));
			cards.Add("VAC_907t1", new CardDef(new Power { PowerTask = ComplexTask.Repeat(new DrawTask(), V("VAC_907t1", "cards", 2)) }));
			cards.Add("VAC_907t2", new CardDef(new Power { PowerTask = new ArmorTask(V("VAC_907t2", "armor", 5)) }));
			cards.Add("VAC_907t3", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => RefreshMana(c, V("VAC_907t3", "crystals", 3))) }));
			// [VAC_948] Hydration Station (8) — Resurrect three of your different highest Cost Taunt minions. (Patch 30.2.0: playable
			// only once a Taunt minion died.)
			cards.Add("VAC_948", new CardDef(new Power
			{
				PlayableIf = (PlayReq.REQ_FRIENDLY_MINION_DIED_THIS_GAME, ctl => ctl.DiedThisGameCards.Any(p => p is Minion && p.Card[GameTag.TAUNT] > 0)),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var dead = c.DiedThisGameCards.Where(p => p is Minion && p.Card[GameTag.TAUNT] > 0).GroupBy(Generic.UniqueCardKey)
						.Select(grp => grp.First()).OrderByDescending(p => p.Card.Cost).ThenBy(_ => g.Random.Next()).Take(V("VAC_948", "minions", 3)).ToList();
					foreach (IPlayable p in dead)
					{
						if (c.BoardZone.IsFull) break;
						Generic.SummonBlock.Invoke(g, (Minion)Generic.NewFromGraveyard(c, p), -1, s);
					}
				})
			}));
			// [VAC_949] New Heights (3) — Increase your maximum Mana by 3 and gain an empty Mana Crystal. (Up to 99.)
			cards.Add("VAC_949", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int max = c[GameTag.MAXRESOURCES] > 0 ? c[GameTag.MAXRESOURCES] : 10;
					c[GameTag.MAXRESOURCES] = Math.Min(99, max + V("VAC_949", "maximum", 3));
					Generic.ChangeManaCrystal(c, 1, false);
				})
			}));
			// [VAC_950] Bouldering Buddy (7) — Rush, Taunt. Costs (1) if you have at least 10 Mana Crystals.
			cards.Add("VAC_950", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => V("VAC_950", "cost", 1), EffectOperator.SET, new SelfCondition(p => p.Controller.BaseMana >= V("VAC_950", "crystals", 10)))
			}));
			// [WORK_024] Handle with Bear (2) — Get two 3/3 Bears with Taunt. Each turn they are in your hand, they gain +1/+1.
			cards.Add("WORK_024", new CardDef(new Power { PowerTask = ComplexTask.Repeat(new AddCardTo("WORK_024t", EntityType.HAND), V("WORK_024", "bears", 2)) }));
			// [WORK_024t] Careful Bear — At the start of your turn, if this is in your hand, gain +1/+1. (Live: one WORK_024te growing
			// in place, effect index 1.)
			cards.Add("WORK_024t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					TriggerActivation = TriggerActivation.HAND,
					EffectIndex = 1,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
						Generic.GiveStacked(g, s, (IPlayable)s, Cards.FromId("WORK_024te"), V("WORK_024t", "growth", 1), V("WORK_024t", "growth", 1)))
				}
			}));
			cards.Add("WORK_024te", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackHealthScriptTag }));
			// [WORK_025] Number Cruncher (4) — Rush, Taunt. Choose Thrice - Gain +2 Attack; or +2 Health.
			cards.Add("WORK_025", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.ChooseMultiple(c, (IPlayable)s, Generic.ChooseCount(((IPlayable)s).Card)))
			}));
			cards.Add("WORK_025a", new CardDef(new Power { PowerTask = new AddEnchantmentTask("WORK_025ae", EntityType.TARGET) }));
			cards.Add("WORK_025b", new CardDef(new Power { PowerTask = new AddEnchantmentTask("WORK_025be", EntityType.TARGET) }));
			cards.Add("WORK_025ae", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("WORK_025", "attack", 2))) }));
			cards.Add("WORK_025be", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("WORK_025", "health", 2))) }));
			// [WORK_050] Un'Goro Brochure (4) — Draw two minions. Give them +2/+2. (Flips each turn.) / [WORK_050t] Dalaran Brochure —
			// Draw two spells. They cost (1) less. (Flips each turn: Generic.HarmonicPartner, end of its owner's turn.)
			cards.Add("WORK_050", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("WORK_050", "minions", 2); i++)
						if (DrawOne(g, c, p => p is Minion) is IPlayable drawn && drawn.Zone?.Type == Zone.HAND) Give(g, s, drawn, "WORK_050e");
				})
			}));
			cards.Add("WORK_050e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("WORK_050", "stats", 2))) }));
			cards.Add("WORK_050t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("WORK_050t", "spells", 2); i++)
						if (DrawOne(g, c, p => p is Spell) is IPlayable drawn && drawn.Zone?.Type == Zone.HAND) GiveCostLess(g, s, drawn, V("WORK_050t", "discount", 1));
				})
			}));
		}
	}
}
