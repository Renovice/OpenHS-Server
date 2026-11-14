// HsServer addition (not upstream SabberStone): Escape from Violet Hold (VIOLET_HOLD, set 1988) — chunk 4: Warrior, Warlock, Shaman (spec:
// research spec_chunk4). Shared pieces (Actions/VioletHold.cs): Cannoneers (Power.CannoneerShot, Generic.CannoneerFire, Crowley's
// HsTag.CANNONEER_EXTRA_SHOTS), Godfrey's saved overdraws (AddHandPhase, Generic.GodfreyReturn), Warptooth's count
// (Generic.NoteFriendlyDamaged), Kazakus's modular parts (Generic.CastModularPart), "(Cast N spells to turn into a minion!)"
// (Generic.TurnsIntoMinion), passive Hero Powers by their text (HeroPower.IsPassiveHeroPower), the Follow the X effect (chunk 3).
// Every number goes through CardValues (V).
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
		private const string IMP_FORMANT = "CAP_400t2t";

		/// <summary>"Put N 3/3 Imp-formants into the enemy deck" (live s071519 3098: CONTROLLER = the deck's owner, CREATOR = the card).</summary>
		private static void ImpFormantsToEnemyDeck(Controller c, IEntity source, int n)
		{
			for (int i = 0; i < n; i++)
				Generic.ShuffleIntoDeck.Invoke(c.Opponent, source, Entity.FromCard(c.Opponent, Cards.FromId(IMP_FORMANT), new EntityData { { GameTag.CREATOR, source.Id } }));
		}

		/// <summary>Two 1/1 Cannoneers (Land Ho!, Hook n' Heave, Captain Crowley; live: one OverrideSpawn around both).</summary>
		private static void SummonCannoneers(Game g, Controller c, IEntity source)
		{
			for (int i = 0; i < V("CAP_107t", "summoned", 2); i++) Summon(g, c, Cards.FromId("CAP_107t"), source);
		}

		/// <summary>A random playable card in hand matching <paramref name="kind"/> gets the Follow effect of <paramref name="spellId"/>.</summary>
		private static void FollowToPlayable(Game g, Controller c, IEntity source, string spellId, string enchantment, Func<IPlayable, bool> kind)
		{
			var playable = c.HandZone.GetAll().Where(p => p.Id != source.Id && p.IsPlayable && kind(p)).ToList();
			if (playable.Count > 0) Generic.GiveFollowEffect(source, playable[g.Random.Next(playable.Count)], Cards.FromId(spellId), enchantment);
		}

		/// <summary>"(Cast @ spells to turn into a minion!)" (Molten Gold, Frostshatter, Stormfury; live darkmoon 1023: after each spell its
		/// owner casts, a TRIGGER block counting NUM_1 down; at 0 another one with the in-hand transform into the card's minion — the same
		/// entity, its cost modifiers kept).</summary>
		private static Trigger CastToTransform(int countIndex, int transformIndex) => new Trigger(TriggerType.AFTER_CAST)
		{
			TriggerActivation = TriggerActivation.HAND,
			TriggerSource = TriggerSource.FRIENDLY,
			EffectIndex = countIndex,
			SingleTask = new CustomTask((g, c, s, t, stack) =>
			{
				if (!(s is IPlayable me) || me.Zone?.Type != Zone.HAND || t?.Id == me.Id) return;
				me[GameTag.TAG_SCRIPT_DATA_NUM_1] = Math.Max(0, me[GameTag.TAG_SCRIPT_DATA_NUM_1] - 1);
				if (me[GameTag.TAG_SCRIPT_DATA_NUM_1] > 0 || !(Generic.TurnsIntoMinion(me.Card) is Card minion)) return;
				if (g.History)
				{
					var block = Kettle.PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, me.Id, "", transformIndex, 0);
					g.PowerHistory.Add(block);
				}
				Generic.NamedFx(me, "Transform_CardInHand", new[] { me.Id }, () => Generic.ChangeEntityBlock(c, me, minion, false));
				if (g.History) g.PowerHistory.Add(Kettle.PowerHistoryBuilder.BlockEnd());
			})
		};

		/// <summary>Tiny Pal's ammunition: the other ones offered; the pick becomes the weapon (live darkmoon 20668: the pick's WeaponEquip effect
		/// from the weapon, CHANGE_ENTITY of the weapon — its Durability kept). Wiki: "The player cannot choose the same ammunition".
		/// <paramref name="weapon"/>: this Tiny Pal — on play its Battlecry resolves before it is equipped (Advanced rulebook, Playing a
		/// weapon: "The card enters Play as a weapon" … "Equipping Phase: The Battlecry of your new weapon (if any) is resolved … Finally
		/// your old weapon is destroyed"), so it isn't the hero's weapon yet: live session-20261001-130515 82129, the 4 options in its
		/// POWER block and the pick's CHANGE_ENTITY of the played weapon (livecheck 2026-10-10: ours offered nothing on play).</summary>
		private static void TinyPalChoose(Controller c, string current, Weapon weapon)
		{
			if (weapon == null || weapon.ToBeDestroyed) return;
			Card[] offer = new[] { "JAIL_458t1", "JAIL_458t2", "JAIL_458t3", "JAIL_458t4" }.Where(id => id != current)
				.Select(Cards.TryFromId).Where(x => x != null).ToArray();
			Generic.CreateChoiceCards.Invoke(c, weapon, null, ChoiceType.GENERAL, ChoiceAction.STACK, offer, new CustomTask((g, c2, s2, pick, st) =>
			{
				if (pick == null) return;
				pick.Zone?.Remove(pick);
				c2.GraveyardZone.Add(pick);
				// (still this Tiny Pal: being equipped, or the hero's weapon)
				if (!(s2 is Weapon w) || w.ToBeDestroyed || w.Zone != null && c2.Hero.Weapon != w) return;
				int used = w.Damage;
				string equip = SubSpells.FindNamed(pick.Card.Id, "WeaponEquip");
				bool open = g.History && equip != null && g.OpenSubSpell(w, equip, null);
				Generic.ChangeEntityBlock(c2, w, pick.Card, false);
				if (w.Damage != used) w.Damage = used;
				if (open) g.CloseSubSpells(w.Id);
			}));
		}

		/// <summary>An ammunition's "After your hero attacks, …. Choose another ammunition."</summary>
		private static CardDef TinyPalAmmo(string id, Action<Game, Controller, IPlayable> effect) => new CardDef(new Power
		{
			Trigger = new Trigger(TriggerType.AFTER_ATTACK)
			{
				TriggerSource = TriggerSource.HERO,
				EffectIndex = 1,
				SingleTask = new CustomTask((g, c, s, t, stack) =>
				{
					effect(g, c, (IPlayable)s);
					g.DeathProcessingAndAuraUpdate();
					TinyPalChoose(c, id, c.Hero.Weapon);
				})
			}
		});

		/// <summary>Godfather Kazakus's nine trial effects, its three lengths and their trial spells (RelatedCards).</summary>
		private static readonly string[] KazakusEffects = { "CAP_405t1", "CAP_405t2", "CAP_405t3", "CAP_405t4", "CAP_405t5", "CAP_405t6", "CAP_405t7", "CAP_405t8", "CAP_405t9" };
		private static readonly (string choice, string trial)[] KazakusLengths = { ("CAP_405tb1b", "CAP_405tb1"), ("CAP_405tb2b", "CAP_405tb2"), ("CAP_405tb3b", "CAP_405tb3") };

		/// <summary>Kazakus's plot: two different effects (three random at a time of nine; wiki), then the length.</summary>
		private static void KazakusPlot(Controller c, IEntity source, List<Card> picked)
		{
			Game g = c.Game;
			if (picked.Count < 2)
			{
				var offer = DiscoverFromTask.PickDistinct(g, KazakusEffects.Select(Cards.TryFromId).Where(x => x != null && picked.All(p => p.Id != x.Id)), 3);
				Generic.CreateChoiceCards.Invoke(c, source, null, ChoiceType.GENERAL, ChoiceAction.STACK, offer.ToArray(), new CustomTask((g2, c2, s2, pick, st) =>
				{
					if (pick == null) return;
					pick.Zone?.Remove(pick);
					c2.GraveyardZone.Add(pick);
					KazakusPlot(c2, s2, picked.Append(pick.Card).ToList());
				}));
				return;
			}
			Card[] lengths = KazakusLengths.Select(l => Cards.TryFromId(l.choice)).Where(x => x != null).ToArray();
			Generic.CreateChoiceCards.Invoke(c, source, null, ChoiceType.GENERAL, ChoiceAction.STACK, lengths, new CustomTask((g2, c2, s2, pick, st) =>
			{
				if (pick == null) return;
				pick.Zone?.Remove(pick);
				c2.GraveyardZone.Add(pick);
				string trialId = KazakusLengths.FirstOrDefault(l => l.choice == pick.Card.Id).trial;
				if (!(Cards.TryFromId(trialId) is Card trial)) return;
				// the parts in resolution order (their TAG_SCRIPT_DATA_NUM_1: live 174206 89836 Sentenced 1 before Criminal Contract 6)
				var parts = picked.OrderBy(p => p[GameTag.TAG_SCRIPT_DATA_NUM_1]).ToList();
				IPlayable made = Entity.FromCard(c2, trial, new EntityData
				{
					{ GameTag.CREATOR, s2.Id }, { GameTag.DISPLAYED_CREATOR, s2.Id },
					{ GameTag.MODULAR_ENTITY_PART_1, parts[0].AssetId }, { GameTag.MODULAR_ENTITY_PART_2, parts[1].AssetId },
					{ GameTag.ADDITIONAL_PLAY_REQS_1, parts[0].AssetId }, { GameTag.ADDITIONAL_PLAY_REQS_2, parts[1].AssetId },
				});
				Generic.AddHandPhase.Invoke(c2, made);
			}));
		}

		/// <summary>A trial's two parts, in order.</summary>
		private static void KazakusVerdict(Controller c, IPlayable trial)
		{
			foreach (GameTag k in new[] { GameTag.MODULAR_ENTITY_PART_1, GameTag.MODULAR_ENTITY_PART_2 })
				if (Cards.FromAssetId(trial[k]) is Card part) Generic.CastModularPart(c, trial, part);
		}

		/// <summary>A Grueling / Unending trial: an Objective; at the start of its owner's turns it counts, at its total the verdict.</summary>
		private static CardDef KazakusObjective => new CardDef(new Power
		{
			Trigger = new Trigger(TriggerType.TURN_START)
			{
				SingleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Spell trial) || trial.Zone?.Type != Zone.SECRET || g.CurrentPlayer != c) return;
					if (!Generic.ObjectiveTick(trial, trial.QuestTotalProgress)) return; // (2026-10-08: the shared tick / expiry)
					trial.ActivatedTrigger?.Remove();
					KazakusVerdict(c, trial);
					Generic.ObjectiveExpires(c, trial);
				})
			}
		});

		private static void VioletHoldChunk4(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Warrior (Cannoneers)
			// [CAP_107t] Cannoneer — At the end of your turn, deal 1 damage to a random enemy. (live darkmoon 1178: each shot its own missile)
			cards.Add("CAP_107t", new CardDef(new Power
			{
				CannoneerShot = new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed && x.Health > 0).ToList();
					if (enemies.Count == 0) return;
					ICharacter victim = enemies[g.Random.Next(enemies.Count)];
					Generic.MissileFx((IPlayable)s, victim, () => Generic.DamageCharFunc((IPlayable)s, victim, V("CAP_107t", "damage", 1), false));
				}),
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new CustomTask((g, c, s, t, stack) => Generic.CannoneerFire((Minion)s)) }
			}));
			// [CAP_101] Follow the Fuse (1) — Deal $2 damage to a random enemy. Give a playable Pirate in your hand this effect for a turn.
			cards.Add("CAP_101", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, V("CAP_101", "damage", 2), true),
					new CustomTask((g, c, s, t, stack) => FollowToPlayable(g, c, s, "CAP_101", "CAP_101e", p => p is Minion && p.Card.IsRace(Race.PIRATE))))
			}));
			cards.Add("CAP_101e", new CardDef(new Power { Enchant = new Enchant { IsOneTurnEffect = true } }));
			// [CAP_102] Land Ho! (4) — Draw 2 cards. Summon two 1/1 Cannoneers.
			cards.Add("CAP_102", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("CAP_102", "cards", 2); i++) Generic.Draw(c);
					SummonCannoneers(g, c, s);
				})
			}));
			// [CAP_103] Hand Cannon (3, weapon) — After your hero attacks, your Cannoneers FIRE!
			cards.Add("CAP_103", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					EffectIndex = 1,
					SingleTask = new CustomTask((g, c, s, t, stack) => { foreach (Minion m in Generic.Cannoneers(c)) Generic.CannoneerFire(m); })
				}
			}));
			// [CAP_104] Blastpowder Engineer (2) — On your turn, friendly Pirates deal 1 more damage. (live night 92787: a PREDAMAGE rewrite, its
			// own attacks and Cannoneer shots too)
			cards.Add("CAP_104", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PREDAMAGE)
				{
					TriggerSource = TriggerSource.ALL,
					FastExecution = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentPlayer != c || g.CurrentEventData == null || g.CurrentEventData.EventNumber <= 0) return;
						if (g.CurrentEventData.EventSource is Minion pirate && pirate.Controller == c && pirate.IsRace(Race.PIRATE))
							g.CurrentEventData.EventNumber += V("CAP_104", "more", 1);
					})
				}
			}));
			// [CAP_105] Hook n' Heave (2) — Discover a Pirate. Summon two 1/1 Cannoneers. (live S130515 59739: the Discover first)
			cards.Add("CAP_105", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.IsRace(Race.PIRATE), null,
					new CustomTask((g, c, s, t, stack) => SummonCannoneers(g, c, s)))
			}));
			// [CAP_106] Captain Crowley (5) — Your Cannoneers fire an additional shot. Battlecry: Summon two 1/1 Cannoneers. (live: CAP_106e
			// "Bombardment" on the player while it is in play; per Crowley — doubt)
			cards.Add("CAP_106", new CardDef(new Power
			{
				Aura = new Aura(AuraType.CONTROLLER, new Effect(HsTag.CANNONEER_EXTRA_SHOTS, EffectOperator.ADD, V("CAP_106", "shots", 1))) { ShowAs = "CAP_106e" },
				PowerTask = new CustomTask((g, c, s, t, stack) => SummonCannoneers(g, c, s))
			}));
			// [CAP_107] Cannonmaster (1) — Battlecry: Get a 1/1 Cannoneer that deals 1 damage to a random enemy at end of turn.
			cards.Add("CAP_107", new CardDef(new Power { PowerTask = new AddCardTo("CAP_107t", EntityType.HAND) }));

			// ---------------------------------------------------------------- Warlock (Imp-formants)
			// [CAP_400t2t] Imp-formant — Lifesteal. Summoned When Drawn (for your opponent). (live s071519 3551: a new minion for the drawer's
			// opponent, copied from the drawn card — its enchantments come along: doubt)
			cards.Add(IMP_FORMANT, new CardDef(new Power
			{
				TopdeckTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!c.Opponent.BoardZone.IsFull) Generic.Copy(c.Opponent, s, (IPlayable)s, Zone.PLAY);
				})
			}));
			// [CAP_400] Kabal Conspirator (2) — Deathrattle: Put two 3/3 Imp-formants into the enemy deck. They summon for YOU when drawn.
			cards.Add("CAP_400", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => ImpFormantsToEnemyDeck(c, s, V("CAP_400", "imps", 2))) }));
			// [CAP_401] Corrupt Constable (4) — Battlecry: If the enemy deck has any Imp-formants, move one to the top and give it +2/+2.
			cards.Add("CAP_401", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var imps = c.Opponent.DeckZone.GetAll().Where(p => p.Card.Id == IMP_FORMANT).ToList();
					if (imps.Count == 0) return;
					IPlayable imp = imps[g.Random.Next(imps.Count)];
					c.Opponent.DeckZone.Remove(imp);
					c.Opponent.DeckZone.Add(imp); // the top
					Give(g, s, imp, "CAP_401e");
				})
			}));
			cards.Add("CAP_401e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("CAP_401", "stats", 2))) }));
			// [CAP_402] Follow the Evidence (1) — Put a 3/3 Imp-formant into the enemy deck. Give a playable card in your hand this effect for a turn.
			cards.Add("CAP_402", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					ImpFormantsToEnemyDeck(c, s, V("CAP_402", "imps", 1));
					FollowToPlayable(g, c, s, "CAP_402", "CAP_402e", p => true);
				})
			}));
			cards.Add("CAP_402e", new CardDef(new Power { Enchant = new Enchant { IsOneTurnEffect = true } }));
			// [CAP_403] Frame Job (5) — Destroy two random enemy minions. Discover a minion in the enemy deck to put on top.
			cards.Add("CAP_403", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = c.Opponent.BoardZone.GetAll().Where(m => !(m is Location) && !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).OrderBy(_ => g.Random.Next()).Take(V("CAP_403", "minions", 2)).ToList();
					foreach (Minion m in enemies) Generic.Destroy(m, s);
					g.DeathProcessingAndAuraUpdate();
					Generic.DiscoverFromCards(c, s, Generic.PickFrom(c.Opponent.DeckZone.GetAll(), g, p => Generic.IsMinionCard(p)), (g2, c2, s2, real) =>
					{
						if (real.Zone?.Type != Zone.DECK) return;
						real.Controller.DeckZone.Remove(real);
						real.Controller.DeckZone.Add(real); // the top
					});
				})
			}));
			// [CAP_404] Harsh Sentence (2) — Enemy minions cost (2) more next turn. Put two 3/3 Imp-formants into the enemy deck.
			cards.Add("CAP_404", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("CAP_404e", EntityType.OP_CONTROLLER),
					new CustomTask((g, c, s, t, stack) => ImpFormantsToEnemyDeck(c, s, V("CAP_404", "imps", 2))))
			}));
			cards.Add("CAP_404e", OpponentNextTurnCost(V("CAP_404", "more", 2), SelfCondition.IsMinion, null));
			// [CAP_405] Godfather Kazakus (3) — Battlecry: Plot a custom sham trial! Then choose the trial's length.
			cards.Add("CAP_405", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => KazakusPlot(c, s, new List<Card>())) }));
			cards.Add("CAP_405tb1", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => KazakusVerdict(c, (IPlayable)s)) }));
			cards.Add("CAP_405tb2", KazakusObjective);
			cards.Add("CAP_405tb3", KazakusObjective);
			foreach (string length in new[] { "CAP_405tb1b", "CAP_405tb2b", "CAP_405tb3b" })
				cards.Add(length, new CardDef(new Power { PowerTask = Nothing }));
			// the nine effects
			cards.Add("CAP_405t1", new CardDef(new Power // Detained for Destruction — Force each minion to attack another minion.
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(x => !(x is Location)).OrderBy(x => x.OrderOfPlay).ToList())
					{
						if (m.IsDead || m.ToBeDestroyed || m.Zone?.Type != Zone.PLAY || m.Untouchable) continue;
						var others = c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll())
							.Where(x => x.Id != m.Id && !(x is Location) && !x.IsDead && !x.ToBeDestroyed && !x.Untouchable).ToList();
						if (others.Count == 0) break;
						EffectAttack(g, m, others[g.Random.Next(others.Count)]);
						g.DeathProcessingAndAuraUpdate();
					}
				})
			}));
			cards.Add("CAP_405t2", new CardDef(new Power // Convicted for Conspiracy — Take control of a random enemy minion.
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = c.Opponent.BoardZone.GetAll().Where(m => !(m is Location) && !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList();
					if (enemies.Count > 0 && !c.BoardZone.IsFull) new ControlTask(EntityType.TARGET).Process(g, c, s, enemies[g.Random.Next(enemies.Count)]);
				})
			}));
			cards.Add("CAP_405t3", new CardDef(new Power // Sentenced for Smuggling — Steal 2 cards from your opponent's hand.
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable card in c.Opponent.HandZone.GetAll().OrderBy(_ => g.Random.Next()).Take(V("CAP_405t3", "cards", 2)).ToList())
						Generic.TakeIntoHand(c, card);
				})
			}));
			cards.Add("CAP_405t4", new CardDef(new Power { PowerTask = ComplexTask.Repeat(new DrawTask(), V("CAP_405t4", "cards", 3)) })); // Crate of Contraband
			cards.Add("CAP_405t5", new CardDef(new Power // Spurious Shiv — Give minions in your hand and battlefield +3/+3.
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable p in c.HandZone.GetAll().Where(p => p is Minion).Concat(c.BoardZone.GetAll().Where(m => !(m is Location))).ToList()) Give(g, s, p, "CAP_405t5e");
				})
			}));
			cards.Add("CAP_405t5e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("CAP_405t5", "stats", 3))) }));
			cards.Add("CAP_405t6", new CardDef(new Power // Criminal Contract — Summon three random 3-Cost minions.
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("CAP_405t6", "minions", 3); i++) Summon(g, c, RandomMinionOfCost(g, V("CAP_405t6", "cost", 3)), s); })
			}));
			cards.Add("CAP_405t7", new CardDef(new Power // Potion of Perjury — Reduce the Cost of minions in your hand by (2).
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { foreach (IPlayable p in c.HandZone.GetAll().Where(p => Generic.IsMinionCard(p)).ToList()) Give(g, s, p, "CAP_405t7e"); })
			}));
			cards.Add("CAP_405t7e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("CAP_405t7", "less", 2))) }));
			cards.Add("CAP_405t8", new CardDef(new Power { PowerTask = new HealTask(V("CAP_405t8", "heal", 12), EntityType.HERO) })); // Swill of Suggestibility
			cards.Add("CAP_405t9", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("CORE_LOOT_368"), s)) })); // Tonic of Tyranny
			// [CAP_406] Kabal Mastermind (5) — Taunt. Battlecry: Whenever you summon an Imp-formant this game, give it +2/+2.
			cards.Add("CAP_406", new CardDef(new Power { PowerTask = new AddEnchantmentTask("CAP_406e", EntityType.CONTROLLER) }));
			cards.Add("CAP_406e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.SUMMON)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t is Minion m && m.Card.Id == IMP_FORMANT && m.Controller == c) Give(g, s, m, "CAP_406e2");
					})
				}
			}));
			cards.Add("CAP_406e2", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("CAP_406", "stats", 2))) }));

			// ---------------------------------------------------------------- Warrior
			// [JAIL_029] Rioter (3) — After a friendly minion survives damage, give it +1 Attack.
			cards.Add("JAIL_029", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.MINIONS,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t is Minion m && !m.IsDead && !m.ToBeDestroyed && m.Health > 0 && m.Zone?.Type == Zone.PLAY) Give(g, s, m, "JAIL_029e");
					})
				}
			}));
			cards.Add("JAIL_029e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("JAIL_029", "attack", 1))) }));
			// [JAIL_307] Crowd Control (5) — Deal $2 damage to all minions, twice. If your deck has 25 or more cards, this costs (2) less. (live
			// 184804 6495: deaths and Deathrattles between the waves)
			cards.Add("JAIL_307", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.DeckZone.Count >= V("JAIL_307", "deck", 25) ? V("JAIL_307", "less", 2) : 0),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("JAIL_307", "waves", 2); i++)
					{
						DamageAll(s, c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => !(m is Location)).Cast<ICharacter>(), V("JAIL_307", "damage", 2), true);
						g.DeathProcessingAndAuraUpdate();
					}
				})
			}));
			// [JAIL_311] Scrappy Defender (4) — Taunt. Has +5 Attack if your deck has 25 or more cards.
			cards.Add("JAIL_311", new CardDef(new Power
			{
				Aura = new Aura(AuraType.SELF, Effects.Attack_N(V("JAIL_311", "attack", 5)))
				{
					Condition = new SelfCondition(p => p.Controller.DeckZone.Count >= V("JAIL_311", "deck", 25)),
					Restless = true
				}
			}));
			// [JAIL_376] Ball and Chain (1, weapon) — Deathrattle: Give your damaged minions +1/+2.
			cards.Add("JAIL_376", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => { foreach (Minion m in c.BoardZone.GetAll().Where(m => m.Damage > 0).ToList()) Give(g, s, m, "JAIL_376e"); })
			}));
			cards.Add("JAIL_376e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("JAIL_376", "attack", 1)), Effects.Health_N(V("JAIL_376", "health", 2))) }));
			// [JAIL_384] Chainbreaker Hogger (8) — Taunt. Start of Game: Duplicate all other Legendary cards in your deck.
			cards.Add("JAIL_384", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.GAME_START)
				{
					TriggerActivation = TriggerActivation.DECK,
					RemoveAfterTriggered = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						foreach (IPlayable legend in c.DeckZone.GetAll().Where(p => p.Id != s.Id && p.Card.Rarity == Rarity.LEGENDARY).ToList())
							Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, legend.Card, new EntityData { { GameTag.CREATOR, s.Id } }));
					})
				}
			}));
			// [JAIL_386] Scramble for Gear (1) — Gain 2 Armor. Shuffle five Gear spells into your deck that give 2 Armor when drawn.
			cards.Add("JAIL_386", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new ArmorTask(V("JAIL_386", "armor", 2)), new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("JAIL_386", "gear", 5); i++)
						Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId("JAIL_386t"), new EntityData { { GameTag.CREATOR, s.Id } }));
				}))
			}));
			cards.Add("JAIL_386t", new CardDef(new Power { TopdeckTask = new ArmorTask(V("JAIL_386t", "armor", 2)) }));
			// [JAIL_387] Release the Beasts (2) — Give minions in your hand +1/+1. Legendary minions get an extra +2/+1.
			cards.Add("JAIL_387", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable p in c.HandZone.GetAll().Where(p => Generic.IsMinionCard(p)).ToList())
					{
						Give(g, s, p, "JAIL_387e");
						if (p.Card.Rarity == Rarity.LEGENDARY) Give(g, s, p, "JAIL_387e2");
					}
				})
			}));
			cards.Add("JAIL_387e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("JAIL_387", "stats", 1))) }));
			cards.Add("JAIL_387e2", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("JAIL_387", "attack", 2)), Effects.Health_N(V("JAIL_387", "health", 1))) }));
			// [JAIL_421] Warptooth (4) — Charge. If four friendly characters take damage on one of your turns, summon this from hand or deck.
			cards.Add("JAIL_421", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerActivation = TriggerActivation.HAND_OR_DECK,
					TriggerSource = TriggerSource.FRIENDLY,
					EffectIndex = 1,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentPlayer != c || !(t is ICharacter damaged) || !(s is Minion me)) return;
						if (Generic.NoteFriendlyDamaged(c, damaged) < V("JAIL_421", "characters", 4) || c.BoardZone.IsFull) return;
						if (me.Zone?.Type != Zone.HAND && me.Zone?.Type != Zone.DECK) return;
						me.Zone.Remove(me);
						Generic.SummonBlock.Invoke(g, me, -1, null);
					})
				}
			}));
			// [JAIL_435] Rampaging Hound (7) — Prepare. Battlecry: Force all enemy minions to attack this.
			cards.Add("JAIL_435", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var hound = (Minion)s;
					foreach (Minion m in c.Opponent.BoardZone.GetAll().Where(x => !(x is Location)).OrderBy(x => x.OrderOfPlay).ToList())
					{
						if (hound.IsDead || hound.ToBeDestroyed || hound.Zone?.Type != Zone.PLAY) break;
						if (m.IsDead || m.ToBeDestroyed || m.Untouchable || m.Zone?.Type != Zone.PLAY) continue;
						EffectAttack(g, m, hound);
						g.DeathProcessingAndAuraUpdate();
					}
				})
			}));
			// [JAIL_455] Disguised Watchman (2) — Can be played on either side. Battlecry: Deal 1 damage to all other friendly minions, twice.
			// ("friendly": its own side, the opponent's when played there)
			cards.Add("JAIL_455", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var me = (Minion)s;
					for (int i = 0; i < V("JAIL_455", "waves", 2); i++)
					{
						DamageAll(s, me.Controller.BoardZone.GetAll().Where(m => m.Id != me.Id && !(m is Location)).Cast<ICharacter>(), V("JAIL_455", "damage", 1), false);
						g.DeathProcessingAndAuraUpdate();
					}
				})
			}));

			// ---------------------------------------------------------------- Warlock
			// [JAIL_303] Ancient Augur (4) — Battlecry: Look at 3 cards in your opponent's hand and secretly choose one. Deathrattle: Discard it.
			// (it must still be in their hand — doubt)
			cards.Add("JAIL_303", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					Generic.DiscoverFromCards(c, s, Generic.PickFrom(c.Opponent.HandZone.GetAll(), g, p => true), (g2, c2, s2, real) =>
					{
						s2[HsTag.AUGUR_CHOICE] = real.Id;
						if (s2 is IPlayable augur) Give(g2, s2, augur, "JAIL_303e");
					})),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (g.IdEntityDic.TryGetValue(s[HsTag.AUGUR_CHOICE], out IPlayable chosen) && chosen.Zone?.Type == Zone.HAND && chosen.Controller == c.Opponent)
						Generic.DiscardBlock.Invoke(c.Opponent, chosen);
				})
			}));
			cards.Add("JAIL_303e", new CardDef(new Power { Enchant = new Enchant() }));
			// [JAIL_399] Imp Gang Stooge (3) — Taunt. Deathrattle: Put two 8/8 Demons with Taunt and Lifesteal on the bottom of your deck.
			cards.Add("JAIL_399", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("JAIL_399", "demons", 2); i++) Generic.PutOnBottom(c, s, Cards.FromId("JAIL_399t1")); })
			}));
			// [JAIL_509] Godfrey the Betrayer (4) — Start of Game: Overdrawn cards return to your hand when you have space. They cost (1) less.
			// (from the opening hand too: live darkmoon 25001)
			cards.Add("JAIL_509", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.GAME_START)
				{
					TriggerActivation = TriggerActivation.HAND_OR_DECK,
					RemoveAfterTriggered = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						c[HsTag.GODFREY_RULE] = c[HsTag.GODFREY_RULE] + 1;
						if (c[HsTag.GODFREY_RULE] == 1) new AddEnchantmentTask("JAIL_509e", EntityType.CONTROLLER).Process(g, c, s, null);
					})
				}
			}));
			cards.Add("JAIL_509e", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.AFTER_PLAY_CARD) { TriggerSource = TriggerSource.ALL, EffectIndex = 5, SingleTask = new CustomTask((g, c, s, t, stack) => Generic.GodfreyReturn(c, s)) },
					new Trigger(TriggerType.TURN_START) { EffectIndex = 6, SingleTask = new CustomTask((g, c, s, t, stack) => { if (g.CurrentPlayer == c) Generic.GodfreyReturn(c, s); }) },
					new Trigger(TriggerType.TURN_END) { EitherTurn = true, EffectIndex = 7, SingleTask = new CustomTask((g, c, s, t, stack) => Generic.GodfreyReturn(c, s)) })
			}));
			// [JAIL_510] Annihilation (8) — Destroy all minions. Summon any Demons in the bottom 3 cards of your deck. (deaths first: doubt)
			cards.Add("JAIL_510", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => !(m is Location)).ToList()) Generic.Destroy(m, s);
					g.DeathProcessingAndAuraUpdate();
					var bottom = c.DeckZone.GetAll().Take(V("JAIL_510", "cards", 3)).Where(p => Generic.IsMinionCard(p) && p.Card.IsRace(Race.DEMON)).ToList();
					foreach (IPlayable demon in bottom)
					{
						if (c.BoardZone.IsFull) break;
						c.DeckZone.Remove(demon);
						Generic.SummonBlock.Invoke(g, (Minion)demon, -1, s);
					}
				})
			}));
			// [JAIL_511] Spire of Solitude (5, Location) — Summon a Demon with stats equal to your hand size. It attacks a random enemy minion.
			cards.Add("JAIL_511", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = c.HandZone.Count;
					if (c.BoardZone.IsFull || !(TitansC4SummonWithStats(g, c, Cards.FromId("JAIL_511t"), n, n, n, s) is Minion demon) || demon.Zone?.Type != Zone.PLAY) return;
					var enemies = c.Opponent.BoardZone.GetAll().Where(m => !(m is Location) && !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList();
					if (enemies.Count > 0) EffectAttack(g, demon, enemies[g.Random.Next(enemies.Count)]);
				})
			}));
			// [JAIL_513] Caged Cranium (3) — Taunt. Battlecry: Gain +1 Health for each card in your hand.
			cards.Add("JAIL_513", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = c.HandZone.Count * V("JAIL_513", "health", 1);
					if (n > 0) Give(g, s, (IPlayable)s, "JAIL_513e", n);
				})
			}));
			cards.Add("JAIL_513e", new CardDef(new Power { Enchant = Enchants.Enchants.AddHealthScriptTag }));
			// [JAIL_514] The Unseen Atlas (10) — Draw 3 cards. Costs (1) less for each card in your hand. (itself included: live darkmoon 27576)
			cards.Add("JAIL_514", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.HandZone.Count * V("JAIL_514", "less", 1)),
				PowerTask = ComplexTask.Repeat(new DrawTask(), V("JAIL_514", "cards", 3))
			}));
			// [JAIL_515] Shadow Rounds (2) — Deal $2 damage to an enemy minion. If it dies, cast this on another random enemy minion. (live
			// S130515 98140: all in one POWER block, deaths after each kill)
			cards.Add("JAIL_515", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Minion target = t as Minion;
					for (int guard = 0; target != null && guard < 8; guard++)
					{
						Minion shot = target;
						Generic.MissileFx((IPlayable)s, shot, () => Generic.DamageCharFunc((IPlayable)s, shot, V("JAIL_515", "damage", 2), true));
						g.DeathProcessingAndAuraUpdate();
						if (!(shot.IsDead || shot.ToBeDestroyed || shot.Zone?.Type != Zone.PLAY)) break;
						var others = c.Opponent.BoardZone.GetAll().Where(m => m.Id != shot.Id && !(m is Location) && !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList();
						target = others.Count > 0 ? others[g.Random.Next(others.Count)] : null;
					}
				})
			}));
			// [JAIL_906] Moragg (9) — Prepare. Deathrattle: Summon a random Demon from your deck. Give it "Deathrattle: Summon Moragg."
			cards.Add("JAIL_906", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (SummonFromDeck(g, c, s, p => p.Card.IsRace(Race.DEMON)) is Minion demon && demon.Zone?.Type == Zone.PLAY) Give(g, s, demon, "JAIL_906e");
				})
			}));
			cards.Add("JAIL_906e", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("JAIL_906"), s)) }));
			// [JAIL_997] Demonic Confinement (2) — Make a minion go Dormant for 2 turns. If it's a friendly Demon, give it +3/+3 instead.
			cards.Add("JAIL_997", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					if (m.Controller == c && m.IsRace(Race.DEMON)) Give(g, s, m, "JAIL_997e1");
					else Generic.MakeDormant(m, V("JAIL_997", "turns", 2), "JAIL_997e3", (IPlayable)s);
				})
			}));
			cards.Add("JAIL_997e1", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("JAIL_997", "stats", 3))) }));

			// ---------------------------------------------------------------- Shaman
			// [JAIL_452] Disguised Detective (1) — Can be played on either side. Overload that player for (2). (the OVERLOAD tag, owed by the
			// side it lands on: Generic.OverloadBlock)
			cards.Add("JAIL_452", new CardDef(new Power { NoScript = true }));
			// [JAIL_457] Hijacked Securitybot (4) — Prepare. Battlecry: Give your other minions +1/+1.
			cards.Add("JAIL_457", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().Where(m => m.Id != s.Id && !(m is Location)).ToList())
						Generic.AddEnchantmentBlock(g, Cards.FromId("JAIL_457e"), (IPlayable)s, m, V("JAIL_457", "attack", 1), V("JAIL_457", "health", 1), 0);
				})
			}));
			cards.Add("JAIL_457e", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackHealthScriptTag }));
			// [JAIL_458] Tiny Pal (4, weapon) — Battlecry: Choose your elemental ammunition! (After your hero attacks, choose another).
			cards.Add("JAIL_458", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => TinyPalChoose(c, null, s as Weapon)) }));
			cards.Add("JAIL_458t1", TinyPalAmmo("JAIL_458t1", (g, c, w) => // Ice: Freeze 2 other random enemies.
			{
				IPlayable defender = g.CurrentEventData?.EventTarget;
				foreach (ICharacter e in Enemies(c).Where(x => x.Id != defender?.Id && !x.IsDead && !x.ToBeDestroyed).OrderBy(_ => g.Random.Next()).Take(V("JAIL_458t1", "enemies", 2)).ToList())
					e.IsFrozen = true;
			}));
			cards.Add("JAIL_458t2", TinyPalAmmo("JAIL_458t2", (g, c, w) => DamageAll(w, Enemies(c), V("JAIL_458t2", "damage", 1), false))); // Fire
			cards.Add("JAIL_458t3", TinyPalAmmo("JAIL_458t3", (g, c, w) => // Earth: a random 3-Cost minion with Taunt (set directly: live 34103)
			{
				if (c.BoardZone.IsFull || !(RandomMinionOfCost(g, V("JAIL_458t3", "cost", 3)) is Card card)) return;
				var m = (Minion)Entity.FromCard(c, card);
				Generic.SummonBlock.Invoke(g, m, -1, w);
				if (m.Zone?.Type == Zone.PLAY) m[GameTag.TAUNT] = 1;
			}));
			cards.Add("JAIL_458t4", TinyPalAmmo("JAIL_458t4", (g, c, w) => // Air: a random Battlecry minion, (2) less
			{
				if (GetCard(c, RandomCard(g, x => x.Type == CardType.MINION && x[GameTag.BATTLECRY] > 0), w) is IPlayable got)
					Give(g, w, got, GenericCostLess(V("JAIL_458t4", "less", 2)));
			}));
			// [JAIL_800] Mug'Zee (6) — Start of Game: If your deck has no other minions, get Mug's Hero Power. If it has no spells, get Zee's! (the
			// starting deck and hand: patch 2026-07-16; both: Mug's replaces, Zee's is a second Hero Power — wiki)
			cards.Add("JAIL_800", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.GAME_START)
				{
					TriggerActivation = TriggerActivation.HAND_OR_DECK,
					RemoveAfterTriggered = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						bool mug = !c.DeckCards.Any(x => x.Type == CardType.MINION && x.Id != s.Card.Id);
						bool zee = !c.DeckCards.Any(x => x.Type == CardType.SPELL);
						if (mug) new ReplaceHeroPower(Cards.FromId("JAIL_800hp1")).Process(g, c, s, null);
						if (zee && mug) Generic.AddSecondHeroPower(c, Cards.FromId("JAIL_800hp2"), (IPlayable)s);
						else if (zee) new ReplaceHeroPower(Cards.FromId("JAIL_800hp2")).Process(g, c, s, null);
					})
				}
			}));
			// [JAIL_800hp1] Mug's Magic — Passive. Your first minion each turn costs (2) less. (Unlocks on Turn 3.) (live dk-classxp: from the
			// owner's 3rd turn JAIL_800hp1e on the player at the turn start)
			cards.Add("JAIL_800hp1", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentPlayer != c || !(s is IPlayable hp)) return;
						hp[HsTag.OWN_TURNS_STARTED] = hp[HsTag.OWN_TURNS_STARTED] + 1;
						if (hp[HsTag.OWN_TURNS_STARTED] < Math.Max(1, hp.Card[GameTag.TAG_SCRIPT_DATA_NUM_1])) return;
						if (hp[GameTag.USE_ALTERNATE_CARD_TEXT] != 1) hp[GameTag.USE_ALTERNATE_CARD_TEXT] = 1;
						// (one at a time: an unused discount is still up — live session-20260929-171955: JAIL_800hp1e id 87 added at the
						// owner's turn 3, kept through a turn with no minion played and the opponent's turn, used by the next minion; no
						// second one meanwhile)
						if (PlayerEnchantment(c, "JAIL_800hp1e") == null)
							new AddEnchantmentTask("JAIL_800hp1e", EntityType.CONTROLLER).Process(g, c, s, null);
					})
				}
			}));
			// ("each turn", not "this turn": live keeps it until a minion is played — 1761: through the turn end, ours dropped it there)
			cards.Add("JAIL_800hp1e", NextCardEffect(Effects.ReduceCost(V("JAIL_800hp1", "less", 2)), SelfCondition.IsMinion, thisTurnOnly: false));
			// [JAIL_800hp2] Zee's Might — Every fifth minion you play triggers its Battlecry twice. ({0} left!) (live 112456: the count after each
			// minion played; the fifth one's Battlecry runs twice)
			cards.Add("JAIL_800hp2", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.PLAY_MINION)
					{
						TriggerSource = TriggerSource.FRIENDLY,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (s is IPlayable hp && t is Minion m && hp[GameTag.TAG_SCRIPT_DATA_NUM_1] >= V("JAIL_800hp2", "every", 5)) m[Game.DOUBLE_BATTLECRY] = 1;
						})
					},
					new Trigger(TriggerType.AFTER_PLAY_MINION)
					{
						TriggerSource = TriggerSource.FRIENDLY,
						EffectIndex = 1,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (!(s is IPlayable hp)) return;
							int every = V("JAIL_800hp2", "every", 5), n = hp[GameTag.TAG_SCRIPT_DATA_NUM_1];
							hp[GameTag.TAG_SCRIPT_DATA_NUM_1] = n >= every ? 0 : n + 1 >= every - 1 ? every : n + 1;
						})
					})
			}));
			// [JAIL_801] Molten Gold (3) — Deal $4 damage. (Cast @ spells to turn into a minion!) / [JAIL_801t] Molten Gold Elemental — Battlecry:
			// Deal 4 damage.
			cards.Add("JAIL_801", new CardDef(TargetAny, new Power
			{
				Trigger = CastToTransform(4, 3),
				PowerTask = new DamageTask(V("JAIL_801", "damage", 4), EntityType.TARGET, true)
			}));
			cards.Add("JAIL_801t", new CardDef(BattlecryAny, new Power { PowerTask = new DamageTask(V("JAIL_801t", "damage", 4), EntityType.TARGET) }));
			// [JAIL_803] Frostshatter (5) — Freeze an enemy. Draw 2 cards. / [JAIL_803t] Frostshatter Elemental — Battlecry: the same.
			cards.Add("JAIL_803", new CardDef(TargetEnemy, new Power
			{
				Trigger = CastToTransform(3, 2),
				PowerTask = ComplexTask.Create(ComplexTask.Freeze(EntityType.TARGET), ComplexTask.Repeat(new DrawTask(), V("JAIL_803", "cards", 2)))
			}));
			cards.Add("JAIL_803t", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } }, new Power
			{
				PowerTask = ComplexTask.Create(ComplexTask.Freeze(EntityType.TARGET), ComplexTask.Repeat(new DrawTask(), V("JAIL_803t", "cards", 2)))
			}));
			// [JAIL_805] Stormfury (7) — Lifesteal. Deal $2 damage to all enemy minions. / [JAIL_805t] Stormfury Elemental — Lifesteal. Battlecry: the same.
			cards.Add("JAIL_805", new CardDef(new Power
			{
				Trigger = CastToTransform(1, 0),
				PowerTask = new CustomTask((g, c, s, t, stack) => DamageAll(s, c.Opponent.BoardZone.GetAll().Where(m => !(m is Location)).Cast<ICharacter>(), V("JAIL_805", "damage", 2), true))
			}));
			cards.Add("JAIL_805t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => DamageAll(s, c.Opponent.BoardZone.GetAll().Where(m => !(m is Location)).Cast<ICharacter>(), V("JAIL_805t", "damage", 2), false))
			}));
			// [JAIL_802] Gallagio Goon (2) — After you play a Battlecry minion, give it +1/+1.
			cards.Add("JAIL_802", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t is Minion m && m.Id != s.Id && m.Card[GameTag.BATTLECRY] > 0 && m.Zone?.Type == Zone.PLAY) Give(g, s, m, "JAIL_802e");
					})
				}
			}));
			cards.Add("JAIL_802e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("JAIL_802", "stats", 1))) }));
			// [JAIL_806] Hexmarshal (6) — Battlecry: Get a random spell that costs (5) or more. If your deck started with no spells, it costs (5)
			// less. (any class: live gave a Priest spell to a Shaman)
			cards.Add("JAIL_806", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (GetCard(c, RandomCard(g, x => x.Type == CardType.SPELL && x.Cost >= V("JAIL_806", "cost", 5)), s) is IPlayable got && !c.DeckCards.Any(x => x.Type == CardType.SPELL))
						GiveCostLess(g, s, got, V("JAIL_806", "less", 5));
				})
			}));
		}
	}
}
