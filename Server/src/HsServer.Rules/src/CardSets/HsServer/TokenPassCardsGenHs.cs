// HsServer addition (not upstream SabberStone): the token pass (2026-10-09). The coverage tool counted collectible cards only, so
// the non-collectible cards that scripted cards create (summon, offer, equip, transform into…) were never checked: `HsServer cards
// --tokens` lists them now (CardCoverage.Tokens). This file scripts the real gaps from shared pieces and marks the cards that are
// shown only or built by a mechanic (EngineOnly). Rules the pass added elsewhere (all data-driven): ReprintCards.IsKeywordOnly
// (Freeze on damage, Immune while attacking, Can't attack, no Corpse, Magnetic, Echo, Mega-Windfury), ClientTags (a weapon's
// "Your hero is Immune while attacking", "Can't Attack." in any case), CardContainer (Starships, unplayable-only cards, a card printed
// again sharing its twin's script), ReprintCards.HeroPowerText (the "Hero Power" header). Shared helpers: Actions/TokenPass.cs.
// Every number goes through CardValues (V).
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
		/// <summary>Kazakus' potion looks by size (the client's "Kazakus Potion" cards: Cost 1 / 5 / 10).</summary>
		private static readonly string[] KazakusPotions = { "CFM_621t", "CFM_621t14", "CFM_621t15" };

		/// <summary>
		/// "Create a custom spell" (Kazakus CFM_621): pick a size (its Entourage: Lesser / Greater / Superior Potion), then two of three
		/// ingredients of that size, the second offer without the first pick (wiki Mixologist: "works exactly like Kazakus" — the same
		/// two-pick flow); the potion goes to the hand (Generic.CraftPotion). Was SabberStone's PotionGenerating, whose potion was a copy of
		/// the size card ("Lesser Potion", no effect): every Kazakus potion did nothing.
		/// </summary>
		private static void KazakusCraft(Controller c, IEntity source)
		{
			string[] sizes = (source as IPlayable)?.Card.Entourage;
			if (sizes == null || sizes.Length == 0 || c.HandZone.IsFull) return;
			Generic.CreateChoiceCards.Invoke(c, source, null, ChoiceType.GENERAL, ChoiceAction.STACK, sizes.Select(Cards.FromId).ToArray(), new CustomTask((g, c1, s1, size, st1) =>
			{
				if (size == null) return;
				int cost = size.Card.Cost;
				Card look = KazakusPotions.Select(Cards.TryFromId).FirstOrDefault(x => x != null && x.Cost == cost);
				var ingredients = KazakusPower.Ingredients(cost).ToList();
				if (look == null || ingredients.Count < 2) return;
				Generic.CreateChoiceCards.Invoke(c1, s1, null, ChoiceType.GENERAL, ChoiceAction.STACK, Pick(g, ingredients, 3).ToArray(), new CustomTask((g2, c2, s2, one, st2) =>
				{
					if (one == null) return;
					Generic.CreateChoiceCards.Invoke(c2, s2, null, ChoiceType.GENERAL, ChoiceAction.STACK, Pick(g2, ingredients.Where(x => x.Id != one.Card.Id), 3).ToArray(), new CustomTask((g3, c3, s3, two, st3) =>
					{
						if (two == null || c3.HandZone.IsFull) return;
						IPlayable potion = Entity.FromCard(c3, Generic.CraftPotion(look, one.Card, two.Card), new EntityData
						{
							[GameTag.CREATOR] = s3.Id, [GameTag.DISPLAYED_CREATOR] = s3.Id, [GameTag.LAST_AFFECTED_BY] = s3.Id
						});
						Generic.AddHandPhase.Invoke(c3, potion);
					}));
				}));
			}));
		}

		/// <summary>"At the start / end of your turn, &lt;effect&gt;" on a minion.</summary>
		private static Trigger TokenOwnTurn(TriggerType type, ISimpleTask effect) => new Trigger(type) { SingleTask = effect };

		/// <summary>A random minion in play (either side), or null.</summary>
		private static Minion TokenRandomMinion(Game g)
		{
			var all = g.Player1.BoardZone.GetAll().Concat(g.Player2.BoardZone.GetAll()).Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList();
			return all.Count > 0 ? all[g.Random.Next(all.Count)] : null;
		}

		private static void TokenPass(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- shown only / built by a mechanic: no script of their own

			foreach (string id in new[]
			{
				// "Dummy effect" holders the client only shows: Generic.PlayDummyEffect plays the Heist ones (DALA_*d, Wisdomball's
				// Legendary transform DALA_740ts4); Grizzled Wizard's DRG_401d and Val'anyr's re-equip LOOT_500d are never played by our
				// scripts (visual DOUBT, no recording of either)
				"DALA_724d", "DALA_728d", "DALA_744d", "DALA_746d", "DALA_740ts4", "DRG_401d", "LOOT_500d",
				// the looks of crafted cards: Kazakus' potions (Generic.CraftPotion), Mixologist's Special, Deathstalker Rexxar's Zombeast
				// (Card.CreateZombeastCard: the first Beast's card with both Beasts' stats and text)
				"CFM_621t", "CFM_621t14", "CFM_621t15", "VAC_523t", "ICC_828t",
				// choices their creator's script resolves (never cast): Blademaster Okani's Counters, Vulpera Scoundrel's mystery choice,
				// Dreamplanner Zephrys' Tours, Guidance's Spirit Path
				"TSC_032t", "TSC_032t2", "ULD_209t", "WORK_027t1", "WORK_027t2", "WORK_027t3", "YOP_024t",
				// Magtheridon's Warder: its text is a reminder; Magtheridon's own trigger counts the Warders (OutlandCardsGenHs)
				"BT_850t",
				// Husk's "Eternal Life": the shown card of his player enchantment (client VISIBLE_PLAYER_ENCHANTMENT_CARD 4508, the only
				// card with it); TIME_618e does the rule. Live (HsRecorder Hearthstone_2026_10_07_11_32_45 Power.log ~47438) reveals it in
				// Husk's POWER block; ours doesn't (visual DOUBT)
				"TIME_618t",
				// flavor text only: Gelbin's Chicken ("Hey Chicken!"), Avatar of the Coin, the "deleted card" stand-in NOOOOOOOOOOOO
				"Mekka4t", "GAME_002", "GAME_006",
			})
				cards.Add(id, EngineOnly);

			// ---------------------------------------------------------------- Kazakus

			// [CFM_621] Kazakus (4) — Battlecry: If your deck has no duplicates, create a custom spell.
			cards.Add("CFM_621", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new ConditionTask(EntityType.SOURCE, SelfCondition.IsNoDupeInDeck),
					new FlagTask(true, new CustomTask((g, c, s, t, stack) => KazakusCraft(c, s))))
			}));

			// ---------------------------------------------------------------- Gelbin Mekkatorque's inventions (EX1_112)

			// [Mekka1] Homing Chicken (1) — At the start of your turn, destroy this minion and draw 3 cards.
			cards.Add("Mekka1", new CardDef(new Power
			{
				Trigger = TokenOwnTurn(TriggerType.TURN_START, ComplexTask.Create(
					new DestroyTask(EntityType.SOURCE),
					ComplexTask.Repeat(new DrawTask(), V("Mekka1", "cards", 3))))
			}));
			// [Mekka2] Repair Bot (1) — At the end of your turn, restore 6 Health to a damaged character (any side).
			cards.Add("Mekka2", new CardDef(new Power
			{
				Trigger = TokenOwnTurn(TriggerType.TURN_END, ComplexTask.Create(
					new IncludeTask(EntityType.ALL),
					new FilterStackTask(SelfCondition.IsDamaged),
					new RandomTask(1, EntityType.STACK),
					new HealTask(V("Mekka2", "heal", 6), EntityType.STACK)))
			}));
			// [Mekka3] Emboldener 3000 (1) — At the end of your turn, give a random minion +1/+1 (any minion, itself too). Mekka3e "Emboldened!".
			cards.Add("Mekka3", new CardDef(new Power
			{
				Trigger = TokenOwnTurn(TriggerType.TURN_END, new CustomTask((g, c, s, t, stack) =>
				{
					if (TokenRandomMinion(g) is Minion m) Give(g, s, m, "Mekka3e");
				}))
			}));
			cards.Add("Mekka3e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("Mekka3", "stats", 1))) }));
			// [Mekka4] Poultryizer (1) — At the start of your turn, transform a random minion into a 1/1 Chicken (any minion, itself too).
			cards.Add("Mekka4", new CardDef(new Power
			{
				Trigger = TokenOwnTurn(TriggerType.TURN_START, new CustomTask((g, c, s, t, stack) =>
				{
					if (TokenRandomMinion(g) is Minion m) Generic.ChangeEntityBlock(c, m, Cards.FromId("Mekka4t"), true);
				}))
			}));

			// ---------------------------------------------------------------- Arch-Thief Rafaam's Artifacts (LOE_092)

			// [LOEA16_3] Lantern of Power (10) — Give a minion +10/+10.
			cards.Add("LOEA16_3", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("LOEA16_3e", EntityType.TARGET) }));
			cards.Add("LOEA16_3e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("LOEA16_3", "stats", 10))) }));
			// [LOEA16_4] Timepiece of Horror (10) — Deal $10 damage randomly split among all enemies.
			cards.Add("LOEA16_4", new CardDef(new Dictionary<PlayReq, int>(), new Power
			{
				PowerTask = new EnqueueTask(V("LOEA16_4", "damage", 10), ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, 1), true)
			}));
			// [LOEA16_5] Mirror of Doom (10) — Fill your board with 3/3 Mummy Zombies.
			cards.Add("LOEA16_5", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = c.BoardZone.FreeSpace;
					Generic.BeginSummonGroup(g, s, n);
					for (int i = 0; i < n && !c.BoardZone.IsFull; i++) Summon(g, c, Cards.FromId("LOEA16_5t"), s);
				})
			}));

			// ---------------------------------------------------------------- the Dungeon Run's treasures (Ol' Toomba / Mama Diggs in the Heist)

			// [LOOTA_806] Wand of Disintegration (3) — Silence and destroy all enemy minions.
			cards.Add("LOOTA_806", new CardDef(new Dictionary<PlayReq, int>(), new Power
			{
				PowerTask = ComplexTask.Create(new SilenceTask(EntityType.OP_MINIONS), new DestroyTask(EntityType.OP_MINIONS))
			}));
			// [LOOTA_811] Orb of Destruction (3) — Destroy 2 of your opponent's Mana Crystals and they discard 2 cards.
			cards.Add("LOOTA_811", new CardDef(new Dictionary<PlayReq, int>(), new Power
			{
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) => c.Opponent.BaseMana = System.Math.Max(0, c.Opponent.BaseMana - V("LOOTA_811", "crystals", 2))),
					new RandomTask(V("LOOTA_811", "discards", 2), EntityType.OP_HAND),
					new DiscardTask(EntityType.STACK))
			}));
			// [LOOTA_812] Boots of Haste (1) — Your minions cost (0) this turn. (LOOTA_812e on the player: a hand aura until the turn ends)
			cards.Add("LOOTA_812", new CardDef(new Dictionary<PlayReq, int>(), new Power { PowerTask = new AddEnchantmentTask("LOOTA_812e", EntityType.CONTROLLER) }));
			cards.Add("LOOTA_812e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.SetCost(0)) { Condition = SelfCondition.IsMinion },
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = RemoveEnchantmentTask.Task }
			}));
			// [LOOTA_816] Party Portal (1, 0/8) — Whenever you cast a spell, summon a random minion of the same Cost.
			cards.Add("LOOTA_816", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.CAST_SPELL)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t != null && RandomMinionOfCost(g, t.Cost) is Card minion) Summon(g, c, minion, s);
					})
				}
			}));
			// [LOOTA_819] Archmage Staff (weapon) — At the start of your turn, add a random Mage spell to your hand.
			cards.Add("LOOTA_819", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (RandomCard(g, x => x.Type == CardType.SPELL && x.HasClass(CardClass.MAGE)) is Card spell) AddToHand(c, spell);
					})
				}
			}));
			// [LOOTA_820] Aleatoric Cube (2) — Shuffle both decks together and give half to each player. Your cards cost (2) less.
			// ("Your cards": the hand and the new deck — DOUBT, the wiki doesn't say; LOOTA_820e "Mysterious Power: Costs (2) less.")
			cards.Add("LOOTA_820", new CardDef(new Dictionary<PlayReq, int>(), new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var mine = Generic.ShuffleDecksTogether(c);
					foreach (IPlayable card in c.HandZone.GetAll().Concat(mine).ToList()) Give(g, s, card, "LOOTA_820e");
				})
			}));
			cards.Add("LOOTA_820e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("LOOTA_820", "discount", 2))) }));
			// [LOOTA_827] Embers of Ragnaros (3) — Shoot three fireballs at random enemies that deal $8 damage each (a new target each).
			cards.Add("LOOTA_827", new CardDef(new Dictionary<PlayReq, int>(), new Power
			{
				PowerTask = new EnqueueTask(V("LOOTA_827", "fireballs", 3),
					ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, V("LOOTA_827", "damage", 8), true))
			}));
			// [LOOTA_834] Gloves of Mugging (1) — Steal 3 cards from your opponent's hand. (as Sentenced for Smuggling CAP_405t3)
			cards.Add("LOOTA_834", new CardDef(new Dictionary<PlayReq, int>(), new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable card in c.Opponent.HandZone.GetAll().OrderBy(_ => g.Random.Next()).Take(V("LOOTA_834", "cards", 3)).ToList())
						Generic.TakeIntoHand(c, card);
				})
			}));
			// [LOOTA_835] Greedy Pickaxe (weapon) — After your hero attacks, gain an empty Mana Crystal.
			cards.Add("LOOTA_835", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new ManaCrystalEmptyTask(V("LOOTA_835", "crystals", 1))
				}
			}));
			// [LOOTA_836] Bag of Coins (0) — Fill your hand with Coins.
			cards.Add("LOOTA_836", new CardDef(new Dictionary<PlayReq, int>(), new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					while (!c.HandZone.IsFull) AddToHand(c, Cards.FromId("GAME_005"));
				})
			}));
			// [LOOTA_837] Horn of Cenarius (2) — Recruit 3 minions.
			cards.Add("LOOTA_837", new CardDef(new Dictionary<PlayReq, int>(), new Power { PowerTask = new RecruitTask(V("LOOTA_837", "minions", 3)) }));
			// [LOOTA_838] Dr. Boom's Boombox (4) — Summon 7 'Boom Bots'.
			cards.Add("LOOTA_838", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = System.Math.Min(V("LOOTA_838", "bots", 7), c.BoardZone.FreeSpace);
					Generic.BeginSummonGroup(g, s, n);
					for (int i = 0; i < n && !c.BoardZone.IsFull; i++) Summon(g, c, Cards.FromId("GVG_110t"), s);
				})
			}));
			// [LOOTA_839] Scroll of Confusion (0) — Each minion randomly chooses a side of the battlefield.
			cards.Add("LOOTA_839", new CardDef(new Dictionary<PlayReq, int>(), new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.MinionsChooseSides(g))
			}));
			// [LOOTA_841] Portable Forge (1) — Discover a Legendary weapon and equip it. (any class: the Dungeon Run's treasures ignore
			// the hero's class — DOUBT)
			cards.Add("LOOTA_841", new CardDef(new Dictionary<PlayReq, int>(), new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var offer = Pick(g, Cards.GenerationPool(g, c).Where(x => x.Type == CardType.WEAPON && x.Rarity == Rarity.LEGENDARY), 3);
					if (offer.Count == 0) return;
					Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.STACK, offer.ToArray(),
						new CustomTask((g2, c2, s2, chosen, st) =>
						{
							if (chosen != null) new WeaponTask(chosen.Card.Id).Process(g2, c2, s2, null);
						}));
				})
			}));
			// [LOOTA_842b] Hilt of Quel'Delar (1) — Give a minion +3/+3. Wiki Hilt of Quel'Delar: "If the player has Hilt of Quel'Delar
			// and Blade of Quel'Delar in their deck, at the Start of Game, they are removed from deck, but a Quel'Delar is added to their
			// hand" — the Kazakusan / Eudora forge (ForgeQuelDelar) with the Dungeon Run's pieces, run by the Hilt at the game's start.
			cards.Add("LOOTA_842b", new CardDef(TargetMinion, new Power
			{
				PowerTask = new AddEnchantmentTask("LOOTA_842be", EntityType.TARGET),
				Trigger = new Trigger(TriggerType.GAME_START)
				{
					TriggerActivation = TriggerActivation.DECK,
					SingleTask = new CustomTask((g, c, s, t, stack) => ForgeQuelDelar(g, c, s, "LOOTA_842a", "LOOTA_842b", "LOOTA_842t")),
					RemoveAfterTriggered = true
				}
			}));
			cards.Add("LOOTA_842be", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("LOOTA_842b", "stats", 3))) }));
			// [LOOTA_842t] Forging Quel'Delar — the pieces leave the deck; Quel'Delar into your hand.
			cards.Add("LOOTA_842t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable p in QuelDelarPieces(c, "LOOTA_842a", "LOOTA_842b")) c.SetasideZone.Add(p.Zone.Remove(p));
					AddToHand(c, Cards.FromId("LOOTA_842"));
				})
			}));
			// [LOOTA_842] Quel'Delar (1, weapon) — After your hero attacks, deal 6 damage to all enemies. (as Kazakusan's ONY_005tc7)
			cards.Add("LOOTA_842", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var enemies = new List<ICharacter> { c.Opponent.Hero };
						enemies.AddRange(c.Opponent.BoardZone.Where(m => !m.IsDead));
						foreach (ICharacter e in enemies) Generic.DamageCharFunc((IPlayable)s, e, V("LOOTA_842", "damage", 6), false);
					})
				}
			}));
			// [LOOTA_843] THE CANDLE (1) — Deal $4 damage to all enemy minions. Shuffle this into your deck. (a new copy: a card going
			// back into the deck keeps nothing — Recursive Module TOY_330t92)
			cards.Add("LOOTA_843", new CardDef(new Dictionary<PlayReq, int>(), new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("LOOTA_843", "damage", 4), EntityType.OP_MINIONS, true),
					new CopyTask(EntityType.SOURCE, Zone.DECK))
			}));
			// [LOOTA_847] Mask of Mimicry (1) — Choose a minion. Minions in your hand become copies of it.
			cards.Add("LOOTA_847", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion model)) return;
					foreach (IPlayable m in c.HandZone.GetAll().Where(p => Generic.IsMinionCard(p)).ToList())
						Generic.ChangeEntityBlock(c, m, model.Card, true);
				})
			}));

			// ---------------------------------------------------------------- PARTY PORTAL!'s Partygoers (Heist boss Hero Power)

			// [KARA_13_20] Party Elemental (3) — "Comes with a party!" Wiki Party Elemental: when summoned it brings two copies of "the
			// other Party Elemental" (TB_KaraPortals_003, a vanilla 3/2), "not stated in its card text".
			cards.Add("KARA_13_20", new CardDef(new Power
			{
				WhenSummonedTask = ComplexTask.Repeat(new SummonTask("TB_KaraPortals_003", SummonSide.RIGHT), V("KARA_13_20", "elementals", 2))
			}));
			// [KARA_13_22] Mime (5) — Always silenced. (Silenced as it lands — enchantments given later stay: DOUBT, the wiki is silent)
			cards.Add("KARA_13_22", new CardDef(new Power { WhenSummonedTask = new SilenceTask(EntityType.SOURCE) }));

			// ---------------------------------------------------------------- others

			// [SCH_199t29] Transfer Student (Alterac Valley board) — Battlecry: Deal 1 damage. Honorable Kill: Gain +1/+1.
			cards.Add("SCH_199t29", new CardDef(BattlecryAny, new Power
			{
				PowerTask = new DamageTask(V("SCH_199t29", "damage", 1), EntityType.TARGET),
				HonorableKillTask = new AddEnchantmentTask("SCH_199t29e", EntityType.SOURCE)
			}));
			cards.Add("SCH_199t29e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("SCH_199t29", "stats", 1))) }));

			// [WW_400t1..t6] Rolled a One! … Rolled a Six! — Discover a N-Cost card. (Snake Eyes' dice: wiki Snake Eyes "one of the related
			// 'Rolled a X!' cards is chosen at random". Snake Eyes WW_400 rolls itself and Discovers directly; whether live shows these
			// cards is unrecorded — visual DOUBT. Scripted to their text so a generated one works.)
			string[] dice = { "WW_400t1", "WW_400t2", "WW_400t3", "WW_400t4", "WW_400t5", "WW_400t6" };
			for (int n = 1; n <= dice.Length; n++)
			{
				int cost = n;
				cards.Add(dice[n - 1], new CardDef(new Dictionary<PlayReq, int>(), new Power
				{
					PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Cost == cost)
				}));
			}
		}
	}
}
