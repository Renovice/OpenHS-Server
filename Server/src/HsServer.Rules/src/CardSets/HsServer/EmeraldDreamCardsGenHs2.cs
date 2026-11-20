// HsServer addition (not upstream SabberStone): Into the Emerald Dream — chunk 1: the Neutral, Death Knight and Demon Hunter cards
// and the FIR_ mini-set (spec: research spec_chunk1). Shared pieces: EmeraldDreamCardsGenHs.cs (Dark Gift, Kindred),
// Actions/Imbue.cs. Every number goes through CardValues (V).
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
		private const int FIRE_SCHOOL = 2;

		/// <summary>"Summon a random Dormant Dreadseed" (data: the card's related minions — Hound, Crow, Serpent).</summary>
		private static void SummonDreadseed(Game g, Controller c, IEntity source)
		{
			var seeds = (ReprintCards.RelatedCardIds(((IPlayable)source).Card.Id) ?? new List<string>())
				.Select(Cards.TryFromId).Where(x => x != null && x.Type == CardType.MINION).ToList();
			if (seeds.Count == 0) seeds = new[] { "EDR_840t", "EDR_840t1", "EDR_840t2" }.Select(Cards.FromId).ToList();
			Summon(g, c, seeds[g.Random.Next(seeds.Count)], source);
		}

		/// <summary>A Leech's steal: its number (TAG_SCRIPT_DATA_NUM_1) plus the player's "Your Leeches steal N more" (Hideous Husk).</summary>
		private static int LeechSteal(IPlayable p) => p.Card[GameTag.TAG_SCRIPT_DATA_NUM_1] + (p.Controller?.ControllerAuraEffects[HsTag.LEECH_BONUS] ?? 0);

		/// <summary>"Resurrect a friendly Deathrattle minion that costs … Summon a copy of it." (wiki 2025-03-28: the card's own
		/// Deathrattle — granted ones don't count, Silenced ones do.)</summary>
		private static void FelResurrect(Game g, Controller c, IEntity source, Func<Card, bool> cost)
		{
			string self = ((IPlayable)source).Card.Id;
			if (ResurrectOne(g, c, source, p => p.Card.Deathrattle && cost(p.Card) && (p.Card.Id != self || !source.Card.Text.Contains("different")))
			    is Minion back && back.Zone?.Type == Zone.PLAY && !c.BoardZone.IsFull)
				Generic.Copy(c, source, back, Zone.PLAY, position: back.ZonePosition + 1);
		}

		/// <summary>Ursoc: a minion its attack killed (the Deathrattle resurrects these).</summary>
		private static void UrsocKill(Game g, Minion ursoc, Minion victim)
		{
			if (victim == null || !(victim.IsDead || victim.ToBeDestroyed || victim.Zone?.Type == Zone.GRAVEYARD)) return;
			int n = ursoc[HsTag.KILLS_COUNT];
			for (int i = 0; i < n; i++) if (ursoc[HsTag.KILLS_BASE + i] == victim.Id) return;
			if (n >= 14) return;
			ursoc[HsTag.KILLS_BASE + n] = victim.Id;
			ursoc[HsTag.KILLS_COUNT] = n + 1;
			Give(g, ursoc, ursoc, "EDR_819e", victim.Id);
		}

		private static void EmeraldChunk1(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Neutral
			// [EDR_000] Ysera, Emerald Aspect (9) — Start of Game: Increase both players' maximum Mana by 5. Battlecry: Gain 3 Mana
			// Crystals. (live: crystals past the cap come back as refreshed Mana — glugg 8887, dec1/204312 10629)
			cards.Add("EDR_000", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.GAME_START)
				{
					TriggerActivation = TriggerActivation.DECK,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!Generic.StartOfGameHolds(s)) return;
						foreach (Controller p in new[] { c, c.Opponent })
							p[GameTag.MAXRESOURCES] = (p[GameTag.MAXRESOURCES] > 0 ? p[GameTag.MAXRESOURCES] : 10) + V("EDR_000", "max mana", 5);
					})
				},
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int max = c[GameTag.MAXRESOURCES] > 0 ? c[GameTag.MAXRESOURCES] : 10;
					int gain = V("EDR_000", "crystals", 3);
					int room = Math.Max(0, max - c.BaseMana);
					c.BaseMana += Math.Min(gain, room);
					c.UsedMana = Math.Max(0, c.UsedMana - Math.Max(0, gain - room)); // past the cap: refreshed
				})
			}));
			// [EDR_102] Treacherous Tormentor (4) — Battlecry: Discover a Legendary minion with a Dark Gift.
			cards.Add("EDR_102", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => DiscoverWithDarkGift(g, c, s, Cards.GenerationPool(g, c).Where(x => x.Type == CardType.MINION &&
					x.Collectible && x.Rarity == Rarity.LEGENDARY && (x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL)))))
			}));
			// [EDR_105] Creature of Madness (2) — Battlecry: Discover a 3-Cost minion with a Dark Gift.
			cards.Add("EDR_105", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => DiscoverWithDarkGift(g, c, s, Cards.GenerationPool(g, c).Where(x => x.Type == CardType.MINION &&
					x.Collectible && x.Cost == V("EDR_105", "cost", 3) && (x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL)))))
			}));
			// [EDR_110] Sporegnasher (5) — Poisonous. Deathrattle: Deal 1 damage to a random enemy minion.
			cards.Add("EDR_110", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed).ToList();
					if (enemies.Count > 0) Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], V("EDR_110", "damage", 1), false);
				})
			}));
			// [EDR_254] Animated Moonwell (3) — After you cast a spell, gain Attack equal to its Cost.
			cards.Add("EDR_254", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if ((t ?? g.CurrentEventData?.EventSource as IPlayable) is IPlayable spell && PlayedCost(spell) is int cost && cost > 0)
							Give(g, s, (IPlayable)s, "EDR_254e1", cost);
					})
				}
			}));
			cards.Add("EDR_254e1", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackScriptTag }));
			// [EDR_260] Illusory Greenwing (4) — Taunt. Deathrattle: Shuffle two 4/5 Dragons with Taunt into your deck. They're Summoned When Drawn.
			cards.Add("EDR_260", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("EDR_260", "illusions", 2); i++)
						Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId("EDR_260t"), new EntityData { [GameTag.CREATOR] = s.Id }));
				})
			}));
			// [EDR_260t] Illusion — Summoned When Drawn. (live yogg-cthun 58921: a NEW copy enters play, the drawn card stays aside)
			cards.Add("EDR_260t", new CardDef(new Power
			{
				TopdeckTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (s is IPlayable drawn && !c.BoardZone.IsFull) Generic.Copy(c, drawn, drawn, Zone.PLAY);
				})
			}));
			// [EDR_453] Briarspawn Drake (10) — At the end of your turn, attack a random enemy minion (excess damage hits the enemy hero).
			cards.Add("EDR_453", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me)) return;
						var enemies = c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed).ToList();
						if (enemies.Count == 0) return;
						me[HsTag.EXCESS_ATTACK_DAMAGE_TO_HERO] = 1;
						EffectAttack(g, me, enemies[g.Random.Next(enemies.Count)]);
						me[HsTag.EXCESS_ATTACK_DAMAGE_TO_HERO] = 0;
					})
				}
			}));
			// [EDR_469] Slumbering Sprite (1) — Starts Dormant. After you use your Hero Power, this awakens.
			cards.Add("EDR_469", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.INSPIRE)
				{
					WhileDormant = true,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me && me[GameTag.DORMANT] > 0) Generic.Awaken(me); })
				}
			}));
			// [EDR_470] Barkshield Sentinel (2) — Taunt. After you use your Hero Power, gain +2 Health.
			cards.Add("EDR_470", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.INSPIRE) { SingleTask = new AddEnchantmentTask("EDR_470e", EntityType.SOURCE) }
			}));
			cards.Add("EDR_470e", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("EDR_470", "health", 2))) }));
			// [EDR_484] Scavenging Flytrap (7) — After a minion dies, gain its Attack.
			cards.Add("EDR_484", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEATH)
				{
					TriggerSource = TriggerSource.ALL_MINIONS_EXCEPT_SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion me && me.Zone?.Type == Zone.PLAY && t is Minion dead && dead.AttackDamage > 0)
							Give(g, s, me, "EDR_484e", dead.AttackDamage);
					})
				}
			}));
			cards.Add("EDR_484e", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackScriptTag }));
			// [EDR_492] Mother Duck (4) — Battlecry: Summon three 1/1 Ducklings with Rush.
			cards.Add("EDR_492", new CardDef(new Power { PowerTask = new SummonTask("EDR_492t", V("EDR_492", "ducklings", 3), SummonSide.ALTERNATE) }));
			cards.Add("EDR_492t", new CardDef(new Power { NoScript = true }));
			// [EDR_495] Twisted Treant (2) — Deathrattle: Give a random minion in each player's hand -2 Attack.
			cards.Add("EDR_495", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Controller p in new[] { c, c.Opponent })
					{
						var minions = p.HandZone.GetAll().Where(x => Generic.IsMinionCard(x)).ToList();
						if (minions.Count > 0) Give(g, s, minions[g.Random.Next(minions.Count)], "EDR_495e");
					}
				})
			}));
			cards.Add("EDR_495e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(-V("EDR_495", "attack", 2))) }));
			// [EDR_530] Daydreaming Pixie (2) — At the end of your turn, get a random Nature spell.
			cards.Add("EDR_530", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (RandomCard(g, x => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == NATURE) is Card spell) AddToHand(c, spell);
					})
				}
			}));
			// [EDR_571] Fae Trickster (3) — Deathrattle: Draw a spell that costs (5) or more.
			cards.Add("EDR_571", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p is Spell && p.Cost >= V("EDR_571", "cost", 5)))
			}));
			// [EDR_572] Tormented Dreadwing (5) — Deathrattle: Draw 2 Dragons. Reduce their Costs by (1).
			cards.Add("EDR_572", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("EDR_572", "dragons", 2); i++)
						if (DrawOne(g, c, p => p is Minion m && m.IsRace(Race.DRAGON)) is IPlayable dragon) Give(g, s, dragon, "GBL_003e");
				})
			}));
			// [EDR_780] Bloodthistle Illusionist (3) — Battlecry: Summon a copy of this. One secretly dies when it takes damage.
			// (one of the two at random is the illusion — rule state HsTag.SECRET_ILLUSION; both show "Illusion?")
			cards.Add("EDR_780", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me) || c.BoardZone.IsFull) return;
					if (!(Generic.Copy(c, s, me, Zone.PLAY, position: me.ZonePosition + 1) is Minion copy) || copy.Zone?.Type != Zone.PLAY) return;
					Minion illusion = g.Random.Next(2) == 0 ? me : copy;
					illusion[HsTag.SECRET_ILLUSION] = 1;
					Give(g, s, me, "EDR_780e1");
					Give(g, s, copy, "EDR_780e1");
				})
			}));
			cards.Add("EDR_780e1", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.ENCHANTMENT_TARGET,
					Condition = new SelfCondition(p => p[HsTag.SECRET_ILLUSION] > 0),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Enchantment e && e.Target is Minion m && m[HsTag.SECRET_ILLUSION] > 0 && !m.IsDead) Generic.Destroy(m, e);
					})
				}
			}));
			// [EDR_800] Flutterwing Guardian (4) — Taunt, Divine Shield. Battlecry: Imbue your Hero Power.
			// [EDR_852] Bitterbloom Knight (2) — Battlecry: Imbue your Hero Power.
			cards.Add("EDR_800", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Generic.Imbue(c, s)) }));
			cards.Add("EDR_852", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Generic.Imbue(c, s)) }));
			// [EDR_844] Naralex, Herald of the Flights (7) — Your first Dragon each turn costs (1).
			cards.Add("EDR_844", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.SetCost(V("EDR_844", "cost", 1)))
				{
					Condition = new SelfCondition(p => p is Minion m && m.IsRace(Race.DRAGON) &&
						!p.Controller.PlayHistory.Any(h => h.Turn == p.Game.Turn && h.SourceCard?.Type == CardType.MINION && h.SourceCard.IsRace(Race.DRAGON))),
					Restless = true
				}
			}));
			// [EDR_846] Shaladrassil (8) — Get all 5 Dream cards. If you've played a higher Cost card while holding this, corrupt them!
			// (its related cards: the Dream cards and its own EDR_846t* corrupted ones; added in a random order — wiki)
			cards.Add("EDR_846", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PLAY_CARD)
				{
					TriggerActivation = TriggerActivation.HAND,
					TriggerSource = TriggerSource.FRIENDLY,
					CountsWhileHeld = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is IPlayable me) || me.Zone?.Type != Zone.HAND || !(g.CurrentEventData?.EventSource is IPlayable played) || played.Id == me.Id) return;
						if (!Generic.ThresholdMet(me) && PlayedCost(played) > me.Cost) me[GameTag.TAG_SCRIPT_DATA_NUM_1] = 1;
					})
				},
				ThresholdValue = 1,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					bool corrupt = Held(s);
					string own = ((IPlayable)s).Card.Id;
					var set = (ReprintCards.RelatedCardIds(own) ?? new List<string>())
						.Where(id => id.StartsWith(own + "t", StringComparison.Ordinal) == corrupt).Select(Cards.TryFromId)
						.Where(x => x != null && x.Type != CardType.ENCHANTMENT).ToList();
					if (!corrupt && set.Count == 0) set = new[] { "DREAM_01", "DREAM_02", "DREAM_03", "DREAM_04", "DREAM_05" }.Select(Cards.FromId).ToList();
					foreach (Card card in set.OrderBy(_ => g.Random.Next())) AddToHand(c, card);
				})
			}));
			// [EDR_846t1] Corrupted Nightmare (0) — Give a minion Immune this turn and +5/+5.
			cards.Add("EDR_846t1", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("EDR_846te2", EntityType.TARGET), new AddEnchantmentTask("EDR_846t1e", EntityType.TARGET))
			}));
			cards.Add("EDR_846t1e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("EDR_846t1", "stats", 5))) }));
			cards.Add("EDR_846te2", OneTurn(Effects.Immune));
			// [EDR_846t2] Corrupted Dream (0) — Shuffle a minion into its owner's deck.
			cards.Add("EDR_846t2", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m) || m.Zone?.Type != Zone.PLAY) return;
					Controller owner = m.Controller;
					m.Zone.Remove(m);
					Generic.RetireFromPlay(m);
					Generic.ShuffleIntoDeck.Invoke(owner, s, Generic.NewFromGraveyard(owner, m));
				})
			}));
			// [EDR_846t3] Corrupted Laughing Sister (2) — Elusive. Your hero is Elusive.
			cards.Add("EDR_846t3", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HERO, Effects.CantBeTargetedBySpellsAndHeroPowers) { ShowAs = "EDR_846t3e" }
			}));
			// [EDR_846t4] Corrupted Awakening (2) — Deal $5 damage to all enemies. (ESPECIALLY Ysera!) (wiki: "will destroy ANY minion where
			// the character is Ysera, then damage enemies")
			cards.Add("EDR_846t4", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => m.Card[Generic.YSERA] > 0).ToList()) // (every Ysera: client tag 4392)
						Generic.Destroy(m, s);
					new DamageTask(V("EDR_846t4", "damage", 5), EntityType.ENEMIES, true).Process(g, c, s, null);
				})
			}));
			cards.Add("EDR_846t5", new CardDef(new Power { NoScript = true }));
			// [EDR_849] Dreambound Raptor (1) — After you play a minion, give it a random Bonus Effect.
			cards.Add("EDR_849", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					EffectIndex = 1,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if ((t ?? g.CurrentEventData?.EventSource as IPlayable) is Minion m && m != s && m.Zone?.Type == Zone.PLAY) GainBonusEffects(g, m, 1);
					})
				}
			}));
			// [EDR_856] Nightmare Lord Xavius (4) — Battlecry: Discover a minion from your deck. Give it a Dark Gift.
			cards.Add("EDR_856", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					DiscoverWithDarkGift(g, c, s, null, null, fromDeck: c.DeckZone.GetAll().Where(p => Generic.IsMinionCard(p)).ToList()))
			}));
			// [EDR_860] Resplendent Dreamweaver (4) — Lifesteal. Battlecry: If you've Imbued your Hero Power twice, deal 4 damage to a minion.
			cards.Add("EDR_860", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } }, new Power
			{
				TargetIf = (PlayReq.REQ_TARGET_IF_AVAILABLE, (src, target) => Generic.ImbueCount(src.Controller) >= V("EDR_860", "imbues", 2)),
				GlowWhile = p => Generic.ImbueCount(p.Controller) >= V("EDR_860", "imbues", 2),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m && Generic.ImbueCount(c) >= V("EDR_860", "imbues", 2)) Generic.DamageCharFunc((IPlayable)s, m, V("EDR_860", "damage", 4), false);
				})
			}));
			// [EDR_861] Tranquil Treant (2) — Deathrattle: Both players gain an empty Mana Crystal.
			cards.Add("EDR_861", new CardDef(new Power { DeathrattleTask = ComplexTask.Create(new ManaCrystalEmptyTask(1), new ManaCrystalEmptyTask(1, opponent: true)) }));
			// [EDR_873] Envoy of the Glade (4) — Battlecry: Transform all Neutral cards in your deck into random Druid ones.
			cards.Add("EDR_873", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var druid = Cards.GenerationPool(g, c).Where(x => x.HasClass(CardClass.DRUID) && !x.HasClass(CardClass.NEUTRAL)).ToList();
					if (druid.Count == 0) return;
					foreach (IPlayable p in c.DeckZone.Where(p => p.Card.HasClass(CardClass.NEUTRAL)).ToList())
						Generic.ChangeEntityBlock(c, p, druid[g.Random.Next(druid.Count)], true);
				})
			}));
			// [EDR_888] Malorne the Waywatcher (8) — Battlecry: Discover a Legendary Wild God. If you've Imbued your Hero Power 4 times,
			// set its Cost to (1). (the Wild Gods: its related cards, any class — wiki lists the same 11)
			cards.Add("EDR_888", new CardDef(new Power
			{
				ThresholdCount = p => Generic.ImbueCount(p.Controller),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var gods = (ReprintCards.RelatedCardIds(((IPlayable)s).Card.Id) ?? new List<string>()).Select(Cards.TryFromId)
						.Where(x => x != null && x.Type == CardType.MINION);
					Card[] offer = DiscoverFromTask.PickDistinct(g, gods, 3).ToArray();
					if (offer.Length == 0) return;
					bool imbued = Generic.ThresholdMet((IPlayable)s);
					Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.STACK, offer, new CustomTask((g2, c2, s2, pick, st) =>
					{
						if (pick == null || c2.HandZone.IsFull) return;
						pick.Zone?.Remove(pick);
						Generic.AddHandPhase.Invoke(c2, pick);
						if (imbued) Give(g2, s2, pick, "GBL_001e");
					}));
				})
			}));
			// [EDR_889] Petal Peddler (2) — At the end of your turn, give another random friendly Dragon +1/+1.
			cards.Add("EDR_889", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var dragons = c.BoardZone.GetAll().Where(m => m != s && m.IsRace(Race.DRAGON) && !m.IsDead).ToList();
						if (dragons.Count > 0) Give(g, s, dragons[g.Random.Next(dragons.Count)], "EDR_889e");
					})
				}
			}));
			cards.Add("EDR_889e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("EDR_889", "stats", 1))) }));
			// [EDR_942] Curious Cumulus (4) — At the end of your turn, give your hero Divine Shield.
			cards.Add("EDR_942", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new CustomTask((g, c, s, t, stack) => c.Hero[GameTag.DIVINE_SHIELD] = 1) }
			}));
			// [EDR_971] Critter Caretaker (1) — At the end of your turn, restore #3 Health to both heroes.
			cards.Add("EDR_971", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new HealTask(V("EDR_971", "heal", 3), EntityType.HEROES) }
			}));
			// [EDR_978] Meadowstrider (8) — Taunt. Deathrattle: Put a Meadowstrider on the bottom of your deck. It costs (1).
			cards.Add("EDR_978", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.DeckZone.IsFull) return;
					IPlayable fresh = Entity.FromCard(c, ((IPlayable)s).Card, new EntityData { [GameTag.CREATOR] = s.Id });
					Generic.ShuffleIntoDeck.Invoke(c, s, fresh);
					if (fresh.Zone?.Type == Zone.DECK) { c.DeckZone.Remove(fresh); c.DeckZone.Add(fresh, 0); }
					Give(g, s, fresh, "GBL_001e");
				})
			}));
			// [EDR_979] Ancient of Yore (5) — Dormant for 2 turns. While Dormant, gain 3 Armor and draw a card at the end of your turn.
			cards.Add("EDR_979", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					WhileDormant = true,
					Condition = new SelfCondition(p => true),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me) || me[GameTag.DORMANT] <= 0) return;
						c.Hero.Armor += V("EDR_979", "armor", 3);
						Generic.Draw(c);
					})
				}
			}));
			// [EDR_999] Gnawing Greenfin (1) — Battlecry: Get a random Murloc.
			cards.Add("EDR_999", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.MURLOC)) is Card murloc) AddToHand(c, murloc); })
			}));
			// [FIR_921] Petal Picker (3) — Battlecry: If you've Imbued your Hero Power twice, draw 2 cards.
			cards.Add("FIR_921", new CardDef(new Power
			{
				GlowWhile = p => Generic.ImbueCount(p.Controller) >= V("FIR_921", "imbues", 2),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (Generic.ImbueCount(c) >= V("FIR_921", "imbues", 2))
						for (int i = 0; i < V("FIR_921", "draw", 2); i++) Generic.Draw(c);
				})
			}));
			// [FIR_929] Living Flame (2) — Deathrattle: Draw a Fire spell.
			cards.Add("FIR_929", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p is Spell && p.Card[SPELL_SCHOOL] == FIRE_SCHOOL)) }));
			// [FIR_940] Zaqali Flamemancer (6) — Battlecry: If every card in your hand is of a different Cost, reduce their Costs by (2).
			bool allDifferent(Controller c, IEntity self)
			{
				var hand = c.HandZone.GetAll().Where(p => p.Id != self.Id).ToList();
				return hand.Count > 0 && hand.Select(p => p.Cost).Distinct().Count() == hand.Count;
			}
			cards.Add("FIR_940", new CardDef(new Power
			{
				GlowWhile = p => allDifferent(p.Controller, p),
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (allDifferent(c, s)) GiveAll(g, s, c.HandZone.GetAll().ToList(), "GBL_002e"); })
			}));
			// [FIR_958] Tindral Sageswift (4) — Deathrattle: Deal 1 damage to all enemies. If it's your opponent's turn, deal 4 damage instead.
			cards.Add("FIR_958", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
					new DamageTask(g.CurrentPlayer != c ? V("FIR_958", "their turn", 4) : V("FIR_958", "damage", 1), EntityType.ENEMIES).Process(g, c, s, null))
			}));
			// [FIR_959] Fyrakk the Blazing (10) — Immune to Fire spells. Battlecry: Cast 15 Mana worth of Fire spells at random enemies.
			// (wiki: enemy targets first, else friendly, a spell with none fails; failed spells still count)
			cards.Add("FIR_959", new CardDef(new Power
			{
				Aura = new AdaptiveEffect(new SelfCondition(_ => true), Character.IMMUNE_TO_FIRE_SPELLS),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int budget = V("FIR_959", "mana", 15);
					while (budget > 0 && !c.Hero.IsDead && !c.Opponent.Hero.IsDead)
					{
						int left = budget;
						if (!(RandomCard(g, x => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == FIRE_SCHOOL && x.Cost >= 1 && x.Cost <= left) is Card fire)) break;
						budget -= fire.Cost;
						new CastRandomSpellTask(enemiesIfPossible: true) { Spell = fire }.Process(g, c, s, null);
					}
				})
			}));
			// [FIR_901] Frostburn Matriarch (5) — Taunt. Battlecry: If you're holding a minion with a Dark Gift, summon two 4/4 Dragons with Taunt.
			cards.Add("FIR_901", new CardDef(new Power
			{
				GlowWhile = p => p.Controller.HandZone.GetAll().Any(x => x.Id != p.Id && Generic.IsMinionCard(x) && HasDarkGift(x)),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.HandZone.GetAll().Any(x => x.Id != s.Id && Generic.IsMinionCard(x) && HasDarkGift(x)))
						for (int i = 0; i < V("FIR_901", "dragons", 2); i++) Summon(g, c, Cards.FromId("FIR_901t"), s);
				})
			}));
			cards.Add("FIR_901t", new CardDef(new Power { NoScript = true }));

			// ---------------------------------------------------------------- Death Knight
			// [EDR_810] Hideous Husk (6) — Your Leeches steal 1 more Health from their victims. Battlecry: Summon two 0/2 Leeches.
			cards.Add("EDR_810", new CardDef(new Power
			{
				Aura = new Aura(AuraType.CONTROLLER, new Effect(HsTag.LEECH_BONUS, EffectOperator.ADD, V("EDR_810", "more", 1))),
				PowerTask = new SummonTask("EDR_810t", V("EDR_810", "leeches", 2))
			}));
			// [EDR_810t] Bloated Leech — At the end of your turn, your hero steals @ Health from the lowest Health enemy.
			// (live dk-classxp 30212: max Health moved — EDR_810e on the victim, EDR_810e2 on the hero)
			cards.Add("EDR_810t", new CardDef(new Power
			{
				HandCounter = LeechSteal,
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is IPlayable leech) || !(LowestHealthEnemy(g, c) is ICharacter victim)) return;
						int n = LeechSteal(leech);
						if (n <= 0) return;
						Give(g, s, victim, "EDR_810e", -n);
						Give(g, s, c.Hero, "EDR_810e2", n);
					})
				}
			}));
			cards.Add("EDR_810e", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(0)) { UseScriptTag = true } }));
			cards.Add("EDR_810e2", new CardDef(new Power { Enchant = Enchants.Enchants.AddHealthScriptTag }));
			// [EDR_812] Grotesque Runeblade (2, weapon) — Battlecry: If the last card you played had an Unholy rune, gain +1 Attack.
			// Repeat for Blood and +1 Durability.
			cards.Add("EDR_812", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					PlayHistoryEntry last = c.PlayHistory.LastOrDefault(h => h.SourceId != s.Id);
					if (last.SourceCard == null || !(c.Hero.Weapon is Weapon me)) return;
					if (last.SourceCard[COST_UNHOLY] > 0) Give(g, s, me, "EDR_812e");
					if (last.SourceCard[COST_BLOOD] > 0) Give(g, s, me, "EDR_812e1");
				})
			}));
			cards.Add("EDR_812e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("EDR_812", "attack", 1))) }));
			cards.Add("EDR_812e1", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.DURABILITY, EffectOperator.ADD, V("EDR_812", "durability", 1))) }));
			// [EDR_814] Infested Breath (2) — Deal $2 damage. Summon a 0/2 Leech.
			cards.Add("EDR_814", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("EDR_814", "damage", 2), EntityType.TARGET, true), new SummonTask("EDR_810t", 1, SummonSide.SPELL))
			}));
			// [EDR_816] Monstrous Mosquito (1) — At the end of your turn, give your other minions +1 Attack.
			cards.Add("EDR_816", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.BoardZone.GetAll().Where(m => m != s && !m.IsDead).ToList(), "EDR_816e"))
				}
			}));
			cards.Add("EDR_816e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("EDR_816", "attack", 1))) }));
			// [EDR_817] Sanguine Infestation (5) — Draw 2 cards. Summon two 0/2 Leeches.
			cards.Add("EDR_817", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("EDR_817", "draw", 2); i++) Generic.Draw(c); }),
					new SummonTask("EDR_810t", V("EDR_817", "leeches", 2), SummonSide.SPELL))
			}));
			// [EDR_818] Nythendra (7) — Taunt. Deathrattle: Split into 1/1 Beetles. At the start of your turn, reform with any remaining.
			// (one Beetle per point of its Attack when it died — wiki Tips; the board limits them)
			cards.Add("EDR_818", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me)) return;
					int n = Math.Max(0, me.AttackDamage);
					Generic.NamedFx(me, "Death_Impact", new[] { me.Id }, () => { });
					for (int i = 0; i < n && !c.BoardZone.IsFull; i++) Summon(g, c, Cards.FromId("EDR_818t"), s, me.LastBoardPosition);
				})
			}));
			// [EDR_818t] Nythendric Beetle — At the start of your turn, combine with any other friendly Beetles and reform Nythendra.
			// (all the Beetles leave without dying; a new Nythendra with Attack, Health and Cost = their number, in the first's slot)
			cards.Add("EDR_818t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me) || me.Zone?.Type != Zone.PLAY) return;
						var beetles = c.BoardZone.GetAll().Where(m => m.Card == me.Card && !m.IsDead).OrderBy(m => m.ZonePosition).ToList();
						if (beetles.Count == 0) return;
						int slot = beetles[0].ZonePosition, n = beetles.Count;
						foreach (Minion b in beetles) Generic.ExpireToGraveyard(b, b); // (live: each its own LAST_AFFECTED_BY)
						if (!(Cards.TryFromId(me.Card.Id.Substring(0, me.Card.Id.Length - 1)) is Card nythendra)) return;
						var reformed = (Minion)Entity.FromCard(c, nythendra, new EntityData { [GameTag.ATK] = n, [GameTag.HEALTH] = n, [GameTag.COST] = n });
						Generic.SummonBlock.Invoke(g, reformed, Math.Min(slot, c.BoardZone.Count), s);
					})
				}
			}));
			// [EDR_819] Ursoc (9) — Battlecry: Attack ALL other minions. Deathrattle: Resurrect any this killed.
			// (wiki: "only resurrects minions killed by his attacks", on its owner's side; Deathwing, Mad Aspect's attack loop)
			cards.Add("EDR_819", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me)) return;
					var others = c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => m.Id != me.Id).ToList();
					EventMetaData temp = g.CurrentEventData;
					while (others.Count > 0)
					{
						if (me.IsDead || me.ToBeDestroyed || me.Zone?.Type != Zone.PLAY) break;
						Minion victim = others[g.Random.Next(others.Count)];
						others.Remove(victim);
						if (victim.IsDead || victim.ToBeDestroyed) continue;
						Generic.AttackBlock.Invoke(c, me, victim, true, true);
						UrsocKill(g, me, victim);
						g.DeathProcessingAndAuraUpdate();
					}
					g.CurrentEventData = temp;
				}),
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me && g.CurrentEventData?.EventTarget is Minion victim) UrsocKill(g, me, victim); })
				},
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < s[HsTag.KILLS_COUNT] && !c.BoardZone.IsFull; i++)
						if (g.IdEntityDic.TryGetValue(s[HsTag.KILLS_BASE + i], out IPlayable dead) && dead is Minion)
							Generic.SummonBlock.Invoke(g, (Minion)Generic.NewFromGraveyard(c, dead), -1, s);
				})
			}));
			cards.Add("EDR_819e", new CardDef(new Power { NoScript = true }));
			// [FIR_900] Cremate (3) — Discover a minion with a Dark Gift. It costs (2) less.
			cards.Add("FIR_900", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => DiscoverWithDarkGift(g, c, s, Cards.GenerationPool(g, c).Where(x => x.Type == CardType.MINION &&
					x.Collectible && (x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL))), picked => Give(g, s, picked, "GBL_002e")))
			}));

			// ---------------------------------------------------------------- Demon Hunter
			// [EDR_421] Omen (10) — Rush, Windfury. Deathrattle: Deal @ damage to all enemies. (Improves after this attacks!)
			cards.Add("EDR_421", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) => s[GameTag.TAG_SCRIPT_DATA_NUM_1] = s[GameTag.TAG_SCRIPT_DATA_NUM_1] + V("EDR_421", "improve", 1))
				},
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
					new DamageTask(Math.Max(1, s[GameTag.TAG_SCRIPT_DATA_NUM_1]), EntityType.ENEMIES).Process(g, c, s, null))
			}));
			// [EDR_493] Alara'shi (5) — Battlecry: Transform minions in your hand into random Demons. (They keep their original stats and Cost.)
			cards.Add("EDR_493", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable p in c.HandZone.GetAll().Where(p => Generic.IsMinionCard(p)).ToList())
					{
						int atk = p[GameTag.ATK], health = p[GameTag.HEALTH], cost = p.Cost;
						if (!(RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.DEMON)) is Card demon)) continue;
						IPlayable now = Generic.ChangeEntityBlock(c, p, demon, false);
						if (now == null) continue;
						Generic.AddEnchantmentBlock(g, Cards.FromId("EDR_493e2"), (IPlayable)s, now, atk, health, 0);
						Generic.AddEnchantmentBlock(g, Cards.FromId("EDR_493e"), (IPlayable)s, now, cost, 0, 0);
					}
				})
			}));
			cards.Add("EDR_493e2", new CardDef(new Power { Enchant = Enchants.Enchants.SetAttackHealthScriptTag }));
			cards.Add("EDR_493e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(0)) { UseScriptTag = true } }));
			// [EDR_820] Wyvern's Slumber (3) — Choose One - Summon two Dormant Dreadseeds; or Deal $2 damage to all minions.
			cards.Add("EDR_820", new CardDef(new Power()));
			cards.Add("EDR_820a", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					IEntity from = g.IdEntityDic.TryGetValue(s[GameTag.CREATOR], out IPlayable parent) ? parent : s;
					for (int i = 0; i < V("EDR_820a", "seeds", 2); i++) SummonDreadseed(g, c, from.Card.Id == "EDR_820a" ? s : from);
				})
			}));
			cards.Add("EDR_820b", new CardDef(new Power { PowerTask = new DamageTask(V("EDR_820b", "damage", 2), EntityType.ALLMINIONS, true) }));
			// [EDR_840] Grim Harvest (2) — Draw a card. Summon a random Dormant Dreadseed.
			cards.Add("EDR_840", new CardDef(new Power { PowerTask = ComplexTask.Create(new DrawTask(), new CustomTask((g, c, s, t, stack) => SummonDreadseed(g, c, s))) }));
			// The Dreadseeds: Hound (2 turns; awakening: hero +3 Attack this turn), Crow (1 turn), Serpent (3 turns)
			cards.Add("EDR_840t", new CardDef(new Power { AwakenTask = new AddEnchantmentTask("EDR_840te", EntityType.HERO) }));
			cards.Add("EDR_840te", OneTurn(Effects.Attack_N(V("EDR_840t", "attack", 3))));
			cards.Add("EDR_840t1", new CardDef(new Power { NoScript = true }));
			cards.Add("EDR_840t2", new CardDef(new Power { NoScript = true }));
			// [EDR_841] Dreadsoul Corrupter (4) — Battlecry and Deathrattle: Summon a random Dormant Dreadseed.
			var dreadseed = new CustomTask((g, c, s, t, stack) => SummonDreadseed(g, c, s));
			cards.Add("EDR_841", new CardDef(new Power { PowerTask = dreadseed, DeathrattleTask = dreadseed }));
			// [EDR_842] Defiled Spear (4, weapon) — After your hero attacks an enemy, deal your hero's Attack damage to another random enemy.
			cards.Add("EDR_842", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable attacked = g.CurrentEventData?.EventTarget as IPlayable;
						var others = Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed && x.Id != attacked?.Id).ToList();
						if (others.Count > 0 && c.Hero.AttackDamage > 0)
							Generic.DamageCharFunc((IPlayable)s, others[g.Random.Next(others.Count)], c.Hero.AttackDamage, false);
					})
				}
			}));
			// [EDR_882] Jumpscare! (2) — Discover a Demon that costs (5) or more with a Dark Gift. Shuffle the other two into your deck.
			cards.Add("EDR_882", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => DiscoverWithDarkGift(g, c, s, Cards.GenerationPool(g, c).Where(x => x.Type == CardType.MINION &&
					x.Collectible && x.IsRace(Race.DEMON) && x.Cost >= V("EDR_882", "cost", 5) && (x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL))), shuffleOthers: true))
			}));
			// [EDR_890] Nightmare Dragonkin (3) — Deathrattle: Reduce the Cost of the right-most card in your hand by (2).
			cards.Add("EDR_890", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => { if (c.HandZone.Count > 0) Give(g, s, c.HandZone[c.HandZone.Count - 1], "GBL_002e"); })
			}));
			// [EDR_891] Ravenous Felhunter (5) — Deathrattle: Resurrect a friendly Deathrattle minion that costs (4) or less. Summon a copy.
			cards.Add("EDR_891", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => FelResurrect(g, c, s, x => x.Cost <= V("EDR_891", "cost", 4)))
			}));
			// [EDR_892] Ferocious Felbat (7) — Deathrattle: Resurrect a different friendly Deathrattle minion that costs (5) or more. Summon a copy.
			cards.Add("EDR_892", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => FelResurrect(g, c, s, x => x.Cost >= V("EDR_892", "cost", 5)))
			}));
			// [FIR_902] Sigil of Cinder (2) — At the start of your next turn, deal $6 damage randomly split among all enemies.
			cards.Add("FIR_902", Sigil("FIR_902", new CustomTask((g, c, s, t, stack) =>
			{
				int shots = V("FIR_902", "damage", 6) + Generic.SpellDamageAmount((IPlayable)s, 0);
				for (int i = 0; i < shots; i++)
				{
					var enemies = Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed && x.Health > 0).ToList();
					if (enemies.Count == 0) break;
					Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], 1, false);
				}
			})));
			// [FIR_904] Felfire Blaze (2) — After you cast a Fel spell, destroy this and deal 2 damage to all enemies.
			cards.Add("FIR_904", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = IsSchool(FEL),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me) || me.IsDead) return;
						Generic.Destroy(me, me);
						new DamageTask(V("FIR_904", "damage", 2), EntityType.ENEMIES).Process(g, c, s, null);
					})
				}
			}));
			// [FIR_952] Scorchreaver (4) — Battlecry: Discover a Fel spell. Reduce the Cost of Fel spells in your hand by (1).
			// (the Discovered spell reaches the hand at the end of the sequence — rule book Discover — so it isn't reduced)
			cards.Add("FIR_952", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.HandZone.GetAll().Where(p => p is Spell && p.Card[SPELL_SCHOOL] == FEL).ToList(), "GBL_003e")),
					DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == FEL))
			}));
		}
	}
}
