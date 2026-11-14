// HsServer addition (not upstream SabberStone): TITANS (2023, client set 1858) with its Fall of Ulduar mini-set — chunk 2: Mage,
// Hunter and Druid cards (Norgannon, Aggramar, Eonar, Sif, Hodir, Freya…). Shared pieces: CardSets/HsServer/TitansCardsGenHs.cs
// (Titans and their abilities, related tokens) and Actions/Titans.cs (Titans, Blossom, Magnetic from the client data, excess attack
// damage, spell schools cast, hand counters). Every number goes through CardValues (V).
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
		// ---------------------------------------------------------------- chunk 2 helpers

		/// <summary>Live's Aqua Archivist effect around its player enchantment (practice-innkeeper-20260929-1x-heropower Power.log
		/// 2761: SUB_SPELL in its POWER block, Source = Targets = the Archivist; RecordedSubSpellSources.tsv 798 "self"). Its role
		/// (no Buff / AE in the name) isn't one the engine wraps by itself.</summary>
		private const string TitansC2WaterSplash = "ReuseFX_Water_Impact_Splash_Super:b8689edf11acc08409650df54d6f3c8c";

		/// <summary>A recorded effect prefab (live) played from <paramref name="source"/> around <paramref name="action"/>.
		/// Game.OpenSubSpell refuses the same prefab twice for one source, so a later recorded-table row can't double it.</summary>
		private static void TitansC2RecordedFx(IPlayable source, string prefab, IEnumerable<int> targets, System.Action action)
		{
			bool open = source != null && source.Game.OpenSubSpell(source, prefab, targets);
			action();
			if (open) source.Game.CloseSubSpells(source.Id);
		}

		/// <summary>The current "power" (TAG_SCRIPT_DATA_NUM_1, the "@" of its text) of the Titan's ability
		/// <paramref name="abilityId"/>: its own ability entity's (Norgannon doubles them), else the card data's.</summary>
		private static int TitansC2Power(IEntity titan, string abilityId)
		{
			Card card = Cards.FromId(abilityId);
			IPlayable ability = titan is IPlayable p ? Generic.TitanAbility(p, Generic.TitanAbilityIndex(p, card)) : null;
			return ability?[GameTag.TAG_SCRIPT_DATA_NUM_1] ?? card[GameTag.TAG_SCRIPT_DATA_NUM_1];
		}

		/// <summary>
		/// "Enemy cards cost (N) more next turn" with an amount known only when it happens (Norgannon's Ancient Knowledge: its
		/// ability's doubled power). The OpponentNextTurnCost pattern (Cold Feet / Speaker Stomper: the enchantment on the OPPONENT's
		/// player, controlled by the caster — live live-20261004-night/222041 3121-3143, JAM_034e ATTACHED = the opponent player,
		/// CONTROLLER = the caster; an OP_HAND cost aura removed when the opponent's turn ends), with the aura made for this amount
		/// and put on that enchantment (an enchantment card without an Enchant exists in every game, AI copies too; its aura is
		/// copied with it). The aura and its removal are the caster's (the enchantment's controller): the opponent's hand, their
		/// turn's end. <paramref name="shownOnCards"/>: what the affected cards show.
		/// </summary>
		private static void TitansC2OpponentNextTurnCost(Game g, Controller c, IPlayable source, string enchantmentId, int more, string shownOnCards)
		{
			if (more <= 0) return;
			Generic.AddEnchantmentBlock(g, Cards.FromId(enchantmentId), source, c.Opponent, more, 0, 0);
			if (!(c.Opponent.AppliedEnchantments?.LastOrDefault(e => e.Card.Id == enchantmentId) is Enchantment holder) || holder.OngoingEffect != null)
				return;
			new Aura(AuraType.OP_HAND, Effects.AddCost(more))
			{
				ShowAs = shownOnCards,
				RemoveTrigger = (TriggerType.TURN_END, SelfCondition.IsOpTurn)
			}.Activate(holder);
		}

		/// <summary>"(Improved by each spell school you've cast this game!)": the card's own number (card data
		/// TAG_SCRIPT_DATA_NUM_1, its base) + the spell schools its player has cast (Generic.SpellSchoolsCast) — Sif, Inquisitive
		/// Creation (live-20261007-vfx-decks 18296-18300: 1 + schools).</summary>
		private static int TitansC2ImprovedBySchools(IPlayable p) =>
			p.Card[GameTag.TAG_SCRIPT_DATA_NUM_1] + (p.Controller != null ? Generic.SpellSchoolsCast(p.Controller).Count : 0);

		/// <summary>
		/// Sif's "Spell Damage +@": live (live-20261007-vfx-decks 9427, 12065, 18296-18300) keeps TAG_SCRIPT_DATA_NUM_1 and
		/// SPELLPOWER both at 1 + schools on every Sif, in hand and in play, right after a new school is played and before that
		/// spell's POWER block (so the spell itself gets it). Power.HandCounter is that number and the game keeps it current after
		/// every action (Game.UpdateMouseOverCards, also between a spell's play and its cast); the minion's Spell Damage is its own
		/// SPELLPOWER tag (Minion.SpellPower — an aura's AuraEffects don't reach it), so the counter keeps that too. A Silenced Sif
		/// keeps none (Minion.Silence sets it to 0). Sif is the only constructed card with a counted "Spell Damage +@".
		/// </summary>
		private static int TitansC2SifSpellDamage(IPlayable p)
		{
			int n = TitansC2ImprovedBySchools(p);
			bool held = p.Zone?.Type == Zone.HAND;
			bool inPlay = p.Zone?.Type == Zone.PLAY && !(p is Minion m && m.IsSilenced);
			if ((held || inPlay) && p[GameTag.SPELLPOWER] != n) p[GameTag.SPELLPOWER] = n;
			return n;
		}

		/// <summary>
		/// Discovery of Magic's offer: spells (any class: "from any class") of spell schools the player hasn't cast this game, each
		/// option of a different school (wiki Discovery of Magic: "Each Discover choice has a different spell school"); with fewer
		/// than three schools left, the rest are more spells of those schools (patch 28.2.1: "will now always offer three Discover
		/// options ... fill in with random spell(s) from the same spell school(s)"); none left: nothing ("does nothing after you have
		/// cast all possible spell schools"). The schools are the ones the pool's spells have (client SPELL_SCHOOL).
		/// </summary>
		private static List<Card> TitansC2SchoolOffer(Game g, Controller c, int count = 0)
		{
			int options = count > 0 ? count : V("TTN_476", "options", 3); // (Siren Song: two)
			HashSet<int> cast = Generic.SpellSchoolsCast(c);
			Dictionary<int, List<Card>> bySchool = Cards.GenerationPool(g, c)
				.Where(x => x.Type == CardType.SPELL && x[SPELL_SCHOOL] > 0 && !cast.Contains(x[SPELL_SCHOOL]))
				.GroupBy(x => x[SPELL_SCHOOL]).ToDictionary(x => x.Key, x => x.ToList());
			var offer = new List<Card>();
			foreach (int school in RandomOf(g, bySchool.Keys, options))
				offer.Add(bySchool[school][g.Random.Next(bySchool[school].Count)]);
			var rest = bySchool.Values.SelectMany(x => x).Where(x => offer.All(o => o.Id != x.Id)).ToList();
			while (offer.Count < options && rest.Count > 0)
			{
				Card more = rest[g.Random.Next(rest.Count)];
				rest.RemoveAll(x => x.Id == more.Id);
				offer.Add(more);
			}
			return offer;
		}

		/// <summary>Forbidden Fruit: "Spend all your Mana" (the NX2_032 way: what's left is used up), then "Gain that much Attack
		/// this turn" (<paramref name="attack"/>: YOG_529e, a one-turn ADD of the amount as script data — a one-turn SET can't be
		/// undone) and / or "twice as much Armor" (<paramref name="armor"/>).</summary>
		private static ISimpleTask TitansC2ForbiddenFruit(bool attack, bool armor) => new CustomTask((g, c, s, t, stack) =>
		{
			int mana = System.Math.Max(0, c.RemainingMana);
			c.UsedMana += mana;
			if (mana <= 0) return;
			if (attack) Generic.AddEnchantmentBlock(g, Cards.FromId("YOG_529e"), (IPlayable)s, c.Hero, mana, 0, 0);
			if (armor) c.Hero.GainArmor((IPlayable)s, mana * V("YOG_529", "armor per mana", 2));
		});

		// ---------------------------------------------------------------- chunk 2 cards

		private static void TitansChunk2(IDictionary<string, CardDef> cards)
		{
			// ================================================================ Mage

			// [TTN_071] Sif (6) — Spell Damage +@ (Improved by each spell school you've cast this game!) (card data: SPELLPOWER 1,
			// TAG_SCRIPT_DATA_NUM_1 1 = the base). No effect of its own: its number (and Spell Damage) is TitansC2SifSpellDamage.
			cards.Add("TTN_071", new CardDef(new Power { HandCounter = TitansC2SifSpellDamage, NoScript = true }));

			// [TTN_075] Norgannon (6) — Titan. After this uses an ability, double the power of the other abilities.
			// Each ability's power is its own ability entity's TAG_SCRIPT_DATA_NUM_1 (card data 5 / 1 / 1; the client's "@"), so the
			// doubling shows on the unused abilities' text: x2 after the first ability, x4 for the last (forum: "needs 2 turns to
			// double itself to 20 damage", us.forums.blizzard.com/t/mage-titan-norgannon-too-op/114165). The prefabs are Norgannon's
			// own (SubSpells.tsv: BuffImpact_Trigger for the doubling, Missile / AE_Debuff / Impact_Secret for the abilities — names
			// that aren't the abilities', so the abilities place them: ownFx). TTN_075e "Can't attack" is unused (the Titan rule),
			// TTN_075t3e / TTN_075t3e2 ("Your next spell costs (2) less") are pre-release leftovers of Unlimited Potential.
			cards.Add("TTN_075", TitansTitan(afterAbility: new CustomTask((g, c, s, t, stack) =>
			{
				if (!(s is IPlayable titan)) return;
				List<IPlayable> others = TitansUnusedAbilities(titan);
				if (others.Count == 0) return;
				Generic.NamedFx(titan, "BuffImpact_Trigger", null, () =>
				{
					foreach (IPlayable ability in others)
						ability[GameTag.TAG_SCRIPT_DATA_NUM_1] = ability[GameTag.TAG_SCRIPT_DATA_NUM_1] * V("TTN_075", "factor", 2);
				});
			})));
			// [TTN_075t] Progenitor's Power — Deal @ damage. (Its power; the Titan deals it: no Spell Damage.)
			cards.Add("TTN_075t", TitansAbility(new CustomTask((g, c, s, t, stack) =>
			{
				if (!(t is ICharacter target) || !(s is IPlayable titan)) return;
				int damage = TitansC2Power(s, "TTN_075t");
				Generic.NamedFx(titan, "Missile", new[] { target.Id }, () => Generic.DamageCharFunc(titan, target, damage, false));
			}), new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 } }, ownFx: true));
			// [TTN_075t2] Ancient Knowledge — Enemy cards cost (@) more next turn. (TTN_075t2e "Your cards cost more this turn." holds
			// the aura, TTN_075t2e2 "Costs more." shows on the cards.)
			cards.Add("TTN_075t2", TitansAbility(new CustomTask((g, c, s, t, stack) =>
			{
				if (!(s is IPlayable titan)) return;
				int more = TitansC2Power(s, "TTN_075t2");
				Generic.NamedFx(titan, "AE_Debuff", null, () => TitansC2OpponentNextTurnCost(g, c, titan, "TTN_075t2e", more, "TTN_075t2e2"));
			}), ownFx: true));
			cards.Add("TTN_075t2e", new CardDef(new Power { NoScript = true })); // the aura is made per use (TitansC2OpponentNextTurnCost)
			// [TTN_075t3] Unlimited Potential — Cast @ random Mage Secrets. (Generic.CastRandomSecrets: not already up, the Secret
			// zone's limits — forum "[Underground Arena] Norgannon doesn't cast any secrets!", us.forums.blizzard.com/…/158252.)
			cards.Add("TTN_075t3", TitansAbility(new CustomTask((g, c, s, t, stack) =>
			{
				if (!(s is IPlayable titan)) return;
				int secrets = TitansC2Power(s, "TTN_075t3");
				Generic.NamedFx(titan, "Impact_Secret", null, () => Generic.CastRandomSecrets(c, titan, x => x.HasClass(CardClass.MAGE), secrets));
			}), ownFx: true));

			// [TTN_077] Chill-o-matic (2) — Magnetic. Freeze any character damaged by this minion. (Card data FREEZE: the engine's
			// Freeze-on-damage; Magnetic from the client data — its TTN_077e magnetize enchantment gets +2/+3 and Freeze from the
			// data, CardContainer.MagnetizeEnchantmentsFromData.)
			cards.Add("TTN_077", new CardDef(new Power { NoScript = true }));

			// [TTN_085] Wisdom of Norgannon (5) — Draw 2 cards. Costs (1) less for each different spell school you've cast this game.
			cards.Add("TTN_085", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => Generic.SpellSchoolsCast(p.Controller).Count * V("TTN_085", "discount", 1)),
				PowerTask = ComplexTask.Repeat(new DrawTask(), V("TTN_085", "draw", 2))
			}));

			// [TTN_095] Aqua Archivist (2) — Battlecry: The next Elemental you play costs (2) less. (TTN_095e on the player — live
			// ATTACHED 2 — inside live's Water splash; the Elementals in hand show no enchantment of their own: no ShowAs.)
			cards.Add("TTN_095", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					TitansC2RecordedFx((IPlayable)s, TitansC2WaterSplash, new[] { s.Id }, () =>
						Generic.AddEnchantmentBlock(g, Cards.FromId("TTN_095e"), (IPlayable)s, c, 0, 0, 0)))
			}));
			cards.Add("TTN_095e", NextCardEffect(Effects.ReduceCost(V("TTN_095", "discount", 2)),
				SelfCondition.IsRace(Race.ELEMENTAL), false)); // (BT_416e's way)

			// [TTN_475] Unchained Gladiator (4) — Battlecry: Draw a card. Repeat for each Elemental you played last turn. (@)
			cards.Add("TTN_475", new CardDef(new Power
			{
				HandCounter = p => p.Controller.NumElementalsPlayedLastTurn,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int draws = V("TTN_475", "draw", 1) + c.NumElementalsPlayedLastTurn;
					for (int i = 0; i < draws && !c.Hero.IsDead; i++) Generic.Draw(c);
				})
			}));

			// [TTN_476] Discovery of Magic (1) — Discover a spell from a spell school you haven't cast this game (from any class).
			// (Live 581: three schools, any class; its OVERRIDE_HISTORY / EFFECT_TIMING and Runes effect are recorded.)
			cards.Add("TTN_476", new CardDef(new Power { PowerTask = new DiscoverFromTask((g, c, s) => TitansC2SchoolOffer(g, c)) }));

			// [TTN_478] Inquisitive Creation (4) — Battlecry: Deal @ damage to all enemy minions. (Improved by each spell school you've
			// cast this game!) (A minion's damage: no Spell Damage.)
			cards.Add("TTN_478", new CardDef(new Power
			{
				HandCounter = TitansC2ImprovedBySchools,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					DamageAll(s, c.Opponent.BoardZone.GetAll(m => !m.IsDead && !m.ToBeDestroyed), TitansC2ImprovedBySchools((IPlayable)s), false))
			}));

			// [TTN_480] Elemental Inspiration (7) — For each spell school you have cast this game, summon a 4/5 Vortex with a random
			// Bonus Effect. (@) The eight Vortexes (TTN_480t..t7) carry their Bonus Effect in their card data: one of the card's
			// RelatedCards at random each time (repeats: live 14105-14210 t6, t6, t3, t, t6); the recorded Teleporter spawn effect
			// wraps them (SummonBlock).
			cards.Add("TTN_480", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				HandCounter = p => Generic.SpellSchoolsCast(p.Controller).Count,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int vortexes = Generic.SpellSchoolsCast(c).Count;
					for (int i = 0; i < vortexes && !c.BoardZone.IsFull; i++) Summon(g, c, TitansRandomRelatedMinion(g, s), s);
				})
			}));

			// [YOG_507] Void Scripture (2) — Discover a spell. If you have enough Mana to play it, cast a copy of it at a random enemy.
			// Live (vfx-decks 5746): the pick goes to the hand (REVEALED, SHOW_BIG_CARD 2 — not for a Secret: patch 28.0.0 "will no
			// longer reveal a Secret selected and immediately cast by its effect"), then a NEW copy (CREATOR / 1715 = Void Scripture)
			// is cast like an effect's random cast (Generic.CastDrawnCopy's way); no Mana is spent.
			cards.Add("YOG_507", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL, afterPick: new CustomTask((g, c, s, picked, stack) =>
				{
					if (picked == null || picked.Cost > c.RemainingMana || c.Hero.IsDead || c.Opponent.Hero.IsDead) return;
					if (g.History && picked.Zone?.Type == Zone.HAND && !picked.Card.IsSecret)
					{
						if (picked[GameTag.REVEALED] == 0) picked[GameTag.REVEALED] = 1;
						g.PowerHistory.Add(new SabberStoneCore.Kettle.PowerHistoryMetaData { Type = MetaDataType.SHOW_BIG_CARD, Data = 2, Info = new List<int> { picked.Id } });
					}
					IPlayable copy = Entity.FromCard(c, picked.Card, new EntityData { [GameTag.CREATOR] = s.Id, [(GameTag)1715] = s.Id });
					new CastRandomSpellTask(enemiesIfPossible: true).Process(g, c, s, copy);
				}))
			}));

			// [YOG_518] Meddlesome Servant (3) — Battlecry: If you've cast 5 or more spells this game, draw 2 cards. ({0} left!) (Ready!)
			// (Data: ENTITY_TAG_THRESHOLD 2 / 5; the count is the spells played this game.)
			cards.Add("YOG_518", new CardDef(new Power
			{
				ThresholdCount = p => p.Controller.NumSpellsPlayedThisGame,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!Generic.ThresholdMet((IPlayable)s)) return;
					for (int i = 0; i < V("YOG_518", "draw", 2) && !c.Hero.IsDead; i++) Generic.Draw(c);
				})
			}));

			// [YOG_519] Tainted Remnant (5) — Battlecry: If you played an Elemental last turn, deal 7 damage randomly split between all
			// enemies. (One damage at a time, each at a random living enemy; the card's Shadowbolt missile per hit: MissilePerHit.)
			cards.Add("YOG_519", new CardDef(new Power
			{
				PowerTask = ComplexTask.Conditional(SelfCondition.ElementalPlayedLastTurn,
					new EnqueueTask(V("YOG_519", "damage", 7), ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, 1)))
			}));

			// ================================================================ Hunter

			// [TTN_078] Observer of Myths (2) — After you summon a minion with more Attack than this, give all friendly minions +1
			// Attack. (Its current Attack against the summoned one's, after that one's Battlecry — Liadrin's RLK_924 way.)
			cards.Add("TTN_078", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t is Minion summoned && s is Minion me && summoned.AttackDamage > me.AttackDamage)
							GiveAll(g, s, c.BoardZone.GetAll(m => !m.IsDead && !m.ToBeDestroyed), "TTN_078e");
					})
				}
			}));
			cards.Add("TTN_078e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TTN_078", "attack", 1))) }));

			// [TTN_079] Always a Bigger Jormungar (2) — Give a minion +2 Attack and "Excess damage dealt by attacks hits the enemy hero."
			// (TTN_079e1 the +2 — ENCHANTMENT_INVISIBLE; TTN_079e the flag HsTag.EXCESS_ATTACK_DAMAGE_TO_HERO, which Attack.cs reads:
			// once however many copies, forum us.forums.blizzard.com/t/trampling-rhino-aabj/114602.)
			cards.Add("TTN_079", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("TTN_079e1", EntityType.TARGET), new AddEnchantmentTask("TTN_079e", EntityType.TARGET))
			}));
			cards.Add("TTN_079e1", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TTN_079", "attack", 2))) }));
			cards.Add("TTN_079e", new CardDef(new Power { Enchant = new Enchant(new Effect(HsTag.EXCESS_ATTACK_DAMAGE_TO_HERO, EffectOperator.SET, 1)) }));

			// [TTN_080] Fable Stablehand (3) — Rush. Battlecry: If you control a minion with 4 or more Attack, gain +2/+2. (The text has
			// no "another": a hand-buffed Stablehand counts itself — DOUBT, forum question us.forums.blizzard.com/t/3-new-hunter-cards/110186.)
			cards.Add("TTN_080", new CardDef(new Power
			{
				PowerTask = ComplexTask.Conditional(
					new SelfCondition(me => me.Controller.BoardZone.GetAll(m => !m.IsDead && m.AttackDamage >= V("TTN_080", "attack", 4)).Length > 0),
					new AddEnchantmentTask("TTN_080e1", EntityType.SOURCE))
			}));
			cards.Add("TTN_080e1", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TTN_080", "stats", 2))) }));

			// [TTN_081] Awakening Tremors (1) — Get three 4/1 Worms. They cost (1). (TTN_081t Bursting Jormungar costs 1 in the data.)
			cards.Add("TTN_081", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TTN_081", "worms", 3); i++) AddToHand(c, Cards.FromId("TTN_081t"));
				})
			}));

			// [TTN_087] Absorbent Parasite (2) — Magnetic, Rush. Can Magnetize to Mechs and Beasts. (All from the client data:
			// MAGNETIC, RUSH, MAGNETIC_TO_RACE 20 — Generic.MagnetizeHost; TTN_087e from the data.)
			cards.Add("TTN_087", new CardDef(new Power { NoScript = true }));

			// [TTN_088] Starstrung Bow (6) — Costs (1) less for each friendly Secret that has triggered this game. (HsTag.SECRETS_REVEALED:
			// counted since the game started, Sigils not.)
			cards.Add("TTN_088", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller[HsTag.SECRETS_REVEALED] * V("TTN_088", "discount", 1))
			}));

			// [TTN_092] Aggramar, the Avenger (6) — Titan. Battlecry: Equip a 3/3 Taeshalach. (TTN_092t; the equip effect is the
			// card's client play effect.) Its abilities enchant whatever weapon is equipped (forum "the Titan abilities apply to all
			// weapons, and not just Taeshalach", us.forums.blizzard.com/t/hunter-titan-aggramar-the-avenger/109699); without a weapon an
			// ability can't be used (REQ_WEAPON_EQUIPPED, the Silverleaf Poison requirement — DOUBT, unrecorded).
			cards.Add("TTN_092", TitansTitan(new Power { PowerTask = new WeaponTask("TTN_092t") }));
			var aggramarWeapon = new Dictionary<PlayReq, int> { { PlayReq.REQ_WEAPON_EQUIPPED, 0 } };
			// [TTN_092t1] Maintain Order — Give your weapon "After your hero attacks, draw a card." (TTN_092e, BAR_318e's way)
			cards.Add("TTN_092t1", TitansAbility(new AddEnchantmentTask("TTN_092e", EntityType.WEAPON), aggramarWeapon));
			cards.Add("TTN_092e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = new DrawTask() }
			}));
			// [TTN_092t2] Commanding Presence — Give your weapon "After your hero attacks, summon a 3/3 Enforcer with Taunt."
			cards.Add("TTN_092t2", TitansAbility(new AddEnchantmentTask("TTN_092e2", EntityType.WEAPON), aggramarWeapon));
			cards.Add("TTN_092e2", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("TTN_092e2t"), s))
				}
			}));
			// [TTN_092t3] Swift Slash — Give your weapon +2 Attack and "Your hero is Immune while attacking." (BAR_321e's way)
			cards.Add("TTN_092t3", TitansAbility(new AddEnchantmentTask("TTN_092e3", EntityType.WEAPON), aggramarWeapon));
			cards.Add("TTN_092e3", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.ATK, EffectOperator.ADD, V("TTN_092t3", "attack", 2)), new Effect(GameTag.IMMUNE_WHILE_ATTACKING, EffectOperator.SET, 1))
			}));

			// [TTN_504] Bait and Switch (2) — Secret: When a friendly minion is attacked, give it +3/+3. (Autodefense Matrix's way: in the
			// ATTACK, before the damage; live 214023-214056 wraps TTN_504e in its FangRoar effect.)
			cards.Add("TTN_504", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.ENEMY,
					Condition = new SelfCondition(p => p.Game.CurrentEventData?.EventTarget is Minion defender && defender.Controller != p.Controller),
					SingleTask = ComplexTask.Secret(new CustomTask((g, c, s, t, stack) =>
					{
						if (!(g.CurrentEventData?.EventTarget is Minion defender) || defender.Controller != c) return;
						// (its FangRoar effect: RecordedSubSpells + RecordedSubSpellBlocks TRIGGER, placed by the shared path)
						Give(g, s, defender, "TTN_504e");
					}))
				}
			}));
			cards.Add("TTN_504e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TTN_504", "stats", 3))) }));

			// [TTN_752] Hodir, Father of Giants (8) — Battlecry: Set the stats of the next three minions you play to 8/8.
			// The count is the player's HsTag.NEXT_MINIONS_PLAYED_LEFT (rule state, AI copies too); TTN_752e "Giantize!" on the player
			// shows it and carries the PLAY_MINION trigger (when the minion is played, before its Battlecry: RLK_659e's moment) that
			// gives TTN_752e1 "Gigantic!: 8/8." inside Hodir's own Impact effect (SubSpells.tsv TTN_752 — unrecorded framing, DOUBT).
			// A second Hodir starts the count again at 3 (both enchantments count the same minions); the first Giantize! acts for both.
			cards.Add("TTN_752", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					c[HsTag.NEXT_MINIONS_PLAYED_LEFT] = System.Math.Max(c[HsTag.NEXT_MINIONS_PLAYED_LEFT], V("TTN_752", "minions", 3));
					Generic.AddEnchantmentBlock(g, Cards.FromId("TTN_752e"), (IPlayable)s, c, 0, 0, 0);
				})
			}));
			cards.Add("TTN_752e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment me) || c.AppliedEnchantments?.FirstOrDefault(e => e.Card.Id == me.Card.Id) != me) return;
						if (c[HsTag.NEXT_MINIONS_PLAYED_LEFT] > 0 && t is Minion played)
						{
							c[HsTag.NEXT_MINIONS_PLAYED_LEFT] = c[HsTag.NEXT_MINIONS_PLAYED_LEFT] - 1;
							IPlayable hodir = me.Creator ?? me;
							Generic.NamedFx(hodir, "HodirFatherGiants_Impact", new[] { played.Id }, () => Give(g, hodir, played, "TTN_752e1"));
						}
						if (c[HsTag.NEXT_MINIONS_PLAYED_LEFT] <= 0)
							foreach (Enchantment giantize in c.AppliedEnchantments.Where(e => e.Card.Id == me.Card.Id).ToList())
								giantize.Remove();
					})
				}
			}));
			cards.Add("TTN_752e1", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("TTN_752", "stats", 8))) }));

			// [YOG_082] Celestial Shot (3) — Deal $3 damage. Your next spell has Spell Damage +2. (YOG_082e on the player, BAR_064e's
			// way without "this turn": the +2 comes after the damage — wiki "The card's own damage does not receive the spell damage
			// bonus" — and the next spell played removes it after its cast; this one's play started before it existed.)
			// Cast twice (an Arcane spell: Vexallus, Stormcaller Bru'kan): one buff, not two — wiki Celestial Shot, patch 28.0.0.189017:
			// "Fixed a bug where a doubled Celestial Shot would result in an extra damage buff" — the second cast replaces the first
			// cast's YOG_082e (RemoveEnchantmentTask reverts its +2).
			cards.Add("YOG_082", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("YOG_082", "damage", 3), EntityType.TARGET, true),
					new CustomTask((g, c, s, t, stack) =>
					{
						foreach (Enchantment earlier in c.AppliedEnchantments?.Where(e => e.Card.Id == "YOG_082e" && e.Creator?.Id == s.Id).ToList()
						                                 ?? new List<Enchantment>())
							RemoveEnchantmentTask.Task.Process(g, c, earlier, null);
					}),
					new AddEnchantmentTask("YOG_082e", EntityType.CONTROLLER))
			}));
			cards.Add("YOG_082e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.SPELLPOWER, EffectOperator.ADD, V("YOG_082", "spell damage", 2))),
				Trigger = new Trigger(TriggerType.AFTER_CAST) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = RemoveEnchantmentTask.Task }
			}));

			// [YOG_505] Bestial Madness (2) — Give +1 Attack to all minions in your hand, deck, and battlefield.
			cards.Add("YOG_505", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					// (minion cards: a Location card is a Minion entity in the engine, not a minion — wiki Location)
					GiveAll(g, s, c.HandZone.Where(p => p.Card.Type == CardType.MINION), "YOG_505e");
					GiveAll(g, s, c.DeckZone.Where(p => p.Card.Type == CardType.MINION), "YOG_505e");
					GiveAll(g, s, c.BoardZone.GetAll(m => !m.IsDead && !m.ToBeDestroyed), "YOG_505e");
				})
			}));
			cards.Add("YOG_505e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("YOG_505", "attack", 1))) }));

			// [YOG_506] Twisted Frostwing (4) — Rush. Deathrattle: Summon a Chimaera with stats equal to this minion's Attack. (Its last
			// Attack with its buffs — AV_325's way; 0 Attack: none, DOUBT.)
			cards.Add("YOG_506", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					int stats = s is Minion me ? me.AttackDamage : 0;
					if (stats > 0) SummonWithStats(g, c, Cards.FromId("YOG_506t"), stats, stats, s);
				})
			}));

			// ================================================================ Druid

			// [TTN_503] Disciple of Eonar (4) — Battlecry: Your next Choose One card or power has both effects combined. The rule state is
			// the player's HsTag.NEXT_CHOOSE_ONE_BOTH (Controller.ChooseBoth; spent by the next Choose One activation in
			// Playable.ActivateTask, also a random cast or Cast When Drawn — wiki Disciple of Eonar bug fixes); TTN_503e "Symbiotic" on
			// the player shows it and leaves once it is spent.
			cards.Add("TTN_503", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					// a flag, not a count: every Disciple's "your next Choose One card" is the same next card (the engine's
					// next-card rule, NextCardEffect: the first matching card spends them all). DOUBT: two Disciples unrecorded.
					c[HsTag.NEXT_CHOOSE_ONE_BOTH] = 1;
					Generic.AddEnchantmentBlock(g, Cards.FromId("TTN_503e"), (IPlayable)s, c, 0, 0, 0);
				})
			}));
			var chooseOneCard = new SelfCondition(p => p.ChooseOne && p.ChooseOnePlayables != null);
			ISimpleTask symbioticSpent = new CustomTask((g, c, s, t, stack) =>
			{
				if (c[HsTag.NEXT_CHOOSE_ONE_BOTH] <= 0 && s is Enchantment symbiotic) symbiotic.Remove();
			});
			cards.Add("TTN_503e", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					// a Choose One card played
					new Trigger(TriggerType.AFTER_PLAY_CARD) { TriggerSource = TriggerSource.FRIENDLY, Condition = chooseOneCard, SingleTask = symbioticSpent },
					// a Choose One Hero Power used
					new Trigger(TriggerType.INSPIRE) { TriggerSource = TriggerSource.FRIENDLY, Condition = chooseOneCard, SingleTask = symbioticSpent },
					// spent another way (a random cast, Cast When Drawn): gone by the end of the turn
					new Trigger(TriggerType.TURN_END)
					{
						EitherTurn = true,
						Condition = new SelfCondition(p => p.Controller[HsTag.NEXT_CHOOSE_ONE_BOTH] <= 0),
						SingleTask = RemoveEnchantmentTask.Task
					})
			}));

			// [TTN_903] Eonar, the Life-Binder (10) — Titan. After this uses an ability, summon a 5/5 Ancient with Taunt. Live (vfx-decks
			// 159559-159590, 160499-160560): each ability inside its own Eonar prefab (Generic.TitanAbilityFx by the ability's name:
			// Flourish / SpontaneousGrowth / BountifulHarvest), then the TRIGGER block (effect index = the ability's) where Eonar's
			// OverrideSpawn (Source = Eonar, Targets = []) wraps the Timeless Ancient, placed right of Eonar.
			cards.Add("TTN_903", TitansTitan(afterAbility: new CustomTask((g, c, s, t, stack) =>
			{
				if (!(s is IPlayable eonar)) return;
				Generic.NamedFx(eonar, "OverrideSpawn", null, () => Summon(g, c, Cards.FromId("TTN_903t4"), s));
			})));
			// [TTN_903t] Spontaneous Growth — Draw cards until your hand is full. (Stops when the deck is empty: no Fatigue loop —
			// Malygos the Spellweaver's way; DOUBT, unrecorded with an empty deck.)
			cards.Add("TTN_903t", TitansAbility(new CustomTask((g, c, s, t, stack) =>
			{
				for (int i = 0; i < 20 && !c.HandZone.IsFull && !c.DeckZone.IsEmpty && !c.Hero.IsDead; i++)
					if (Generic.Draw(c) == null) break;
			})));
			// [TTN_903t2] Bountiful Harvest — Restore your hero to full Health. (Healed by the Titan, inside Eonar's BountifulHarvest
			// prefab aimed at the hero — as live aims Flourish (159561); at full Health too: DOUBT, unrecorded, live-checks.)
			cards.Add("TTN_903t2", TitansAbility(new CustomTask((g, c, s, t, stack) =>
				Generic.NamedFx((IPlayable)s, "_BountifulHarvest_", new[] { c.Hero.Id }, () =>
				{
					if (c.Hero.Damage > 0) c.Hero.TakeHeal((IPlayable)s, c.Hero.Damage);
				})), ownFx: true));
			// [TTN_903t3] Flourish — Refresh your Mana Crystals. (Live 159561-159563: RESOURCES_USED 0 — Forest Warden Omu's way —
			// inside TTNFX_EonarTheLifebinder_Flourish_Super from Eonar, Targets = the hero: the effect changes only the player's
			// mana, so the collected targets (DAMAGE / HEALING / buffs) would be empty — the script names the hero.)
			cards.Add("TTN_903t3", TitansAbility(new CustomTask((g, c, s, t, stack) =>
				Generic.NamedFx((IPlayable)s, "_Flourish_", new[] { c.Hero.Id }, () => c.UsedMana = 0)), ownFx: true));

			// [TTN_926] Ancient of Growth (7) — Choose One - Summon three 2/2 Treants; or Transform your Treants into 5/5 Ancients with
			// Taunt. (Options TTN_926a / TTN_926b; both combined: a then b, the text's order — the default path, no parent task.)
			cards.Add("TTN_926a", new CardDef(new Power { PowerTask = new SummonTask("TTN_950t2", V("TTN_926a", "treants", 3)) }));
			cards.Add("TTN_926b", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion treant in c.BoardZone.GetAll(m => !m.IsDead && !m.ToBeDestroyed && IsTreant(m)))
						Generic.ChangeEntityBlock(c, treant, Cards.FromId("TTN_903t4"), true);
				})
			}));

			// [TTN_927] Conservator Nymph (3) — Battlecry: Transform a friendly Treant into a 5/5 Ancient with Taunt. (The player picks:
			// a Treant target — PlayReq.REQ_TARGET_IS_TREANT, client tag 2831.)
			cards.Add("TTN_927", new CardDef(new Dictionary<PlayReq, int>
			{
				{ PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_TARGET_IS_TREANT, 0 }
			}, new Power { PowerTask = new TransformTask("TTN_903t4", EntityType.TARGET) }));

			// [TTN_930] Frost Lotus Seedling (3) — Draw 1 card. Gain 5 Armor. (Blossoms in @ turns.) (Blossom is the engine's:
			// Generic.BlossomAtTurnEnd → TTN_930t.)
			cards.Add("TTN_930", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(ComplexTask.Repeat(new DrawTask(), V("TTN_930", "draw", 1)), new ArmorTask(V("TTN_930", "armor", 5)))
			}));
			// [TTN_930t] Frost Lotus Blossom (3) — Draw 2 cards. Gain 10 Armor.
			cards.Add("TTN_930t", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(ComplexTask.Repeat(new DrawTask(), V("TTN_930t", "draw", 2)), new ArmorTask(V("TTN_930t", "armor", 10)))
			}));

			// [TTN_940] Freya, Keeper of Nature (8) — Choose One - Duplicate your hand; or Summon copies of all other friendly minions.
			// Options in the client's order (live subOption 0 = TTN_940b, 1 = TTN_940a); both combined: b then a. Real copies (Generic.Copy:
			// enchantments, cost changes and damage kept), not new cards (Elise's way).
			// [TTN_940b] Seeds of the Future — Duplicate your hand. (Left to right, until the hand is full; live 158266-158390.)
			cards.Add("TTN_940b", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable card in c.HandZone.GetAll().ToList())
					{
						if (c.HandZone.IsFull) break;
						if (card.Zone?.Type == Zone.HAND) Generic.Copy(c, s, card, Zone.HAND);
					}
				})
			}));
			// [TTN_940a] Guardians of Life — Summon copies of all other friendly minions. (Right-most first, each copy right of its
			// original, as live 179049-179075: Freya, A, B → Freya, A, A', B, B'.)
			cards.Add("TTN_940a", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					List<Minion> others = c.BoardZone.GetAll(m => m.Id != s.Id && !m.IsDead && !m.ToBeDestroyed).ToList(); // (no Locations / Dormant)
					for (int i = others.Count - 1; i >= 0 && !c.BoardZone.IsFull; i--)
						if (others[i].Zone?.Type == Zone.PLAY) Generic.Copy(c, s, others[i], Zone.PLAY, position: others[i].ZonePosition + 1);
				})
			}));

			// [TTN_950] Forest Seedlings (1) — Summon two 1/1 Saplings. (Blossoms in @ turns.) (→ TTN_950t3, the engine's Blossom.)
			cards.Add("TTN_950", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } },
				new Power { PowerTask = new SummonTask("TTN_950t", V("TTN_950", "saplings", 2), SummonSide.SPELL) }));
			// [TTN_950t3] Forest Blossoms (1) — Summon two 2/2 Treants.
			cards.Add("TTN_950t3", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } },
				new Power { PowerTask = new SummonTask("TTN_950t2", V("TTN_950t3", "treants", 2), SummonSide.SPELL) }));

			// [TTN_954] Cultivation (8) — Give your minions +2/+2. Costs (1) less for each Treant you've summoned this game.
			cards.Add("TTN_954", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.SummonedThisGame.Count(card => SelfCondition.IsTreantCard(card)) * V("TTN_954", "discount", 1)),
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.BoardZone.GetAll(m => !m.IsDead && !m.ToBeDestroyed), "TTN_954e"))
			}));
			cards.Add("TTN_954e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TTN_954", "stats", 2))) }));

			// [TTN_955] Lifebinder's Gift (2) — Choose One - Get 2 random Nature spells; or Reduce the Cost of spells in your hand by (1).
			// (Options TTN_955A / TTN_955B; combined: A then B, so the new spells cost less too. Live's hand-buff effect is recorded.)
			// [TTN_955A] Lifebinder's Bloom — Get 2 random Nature spells. (Any class: hearthstonetopdecks "can give you any nature spell,
			// including those from other classes".)
			cards.Add("TTN_955A", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TTN_955A", "spells", 2); i++)
						if (RandomCard(g, x => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == NATURE) is Card spell) AddToHand(c, spell);
				})
			}));
			// [TTN_955B] Lifebinder's Growth — Reduce the Cost of spells in your hand by (1).
			cards.Add("TTN_955B", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.HandZone.Where(p => p is Spell), "TTN_955Be"))
			}));
			cards.Add("TTN_955Be", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("TTN_955B", "discount", 1))) }));

			// [YOG_403] Aerosoilizer (3) — Battlecry and Deathrattle: Summon a 2/2 Treant. (Right of it; the Deathrattle's in its slot.)
			ISimpleTask aerosoilizer = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("TTN_950t2"), s));
			cards.Add("YOG_403", new CardDef(new Power { PowerTask = aerosoilizer, DeathrattleTask = aerosoilizer }));

			// [YOG_528] Contaminated Lasher (3) — Battlecry: If you've cast 5 or more spells this game, refresh 4 Mana Crystals.
			// ({0} left!) (Ready!) (Data: ENTITY_TAG_THRESHOLD 2 / 5.)
			cards.Add("YOG_528", new CardDef(new Power
			{
				ThresholdCount = p => p.Controller.NumSpellsPlayedThisGame,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (Generic.ThresholdMet((IPlayable)s)) RefreshMana(c, V("YOG_528", "mana", 4));
				})
			}));

			// [YOG_529] Forbidden Fruit (0) — Spend all your Mana. Choose One - Gain that much Attack this turn; or twice as much Armor.
			// The card's own task is its "both combined" version (Playable.ActivateTask): the Mana is spent once and gives both — run
			// one option after the other, the second would find no Mana left. (Remaining Mana as the player has it, temporary too.)
			cards.Add("YOG_529", new CardDef(new Power { PowerTask = TitansC2ForbiddenFruit(true, true) }));
			// [YOG_529a] Sinful Sampling — Spend all your Mana. Gain that much Attack this turn.
			cards.Add("YOG_529a", new CardDef(new Power { PowerTask = TitansC2ForbiddenFruit(true, false) }));
			// [YOG_529b] Reject Temptation — Spend all your Mana. Gain twice as much Armor.
			cards.Add("YOG_529b", new CardDef(new Power { PowerTask = TitansC2ForbiddenFruit(false, true) }));
			cards.Add("YOG_529e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0)) { UseScriptTag = true, IsOneTurnEffect = true } }));
		}
	}
}
