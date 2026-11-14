// HsServer addition (not upstream SabberStoneCore): Whizbang's Workshop (WHIZBANG, set 1897; TOY_ + MIS_ Dr. Boom's Incredible Inventions) —
// chunk 1: Neutral, Death Knight, Demon Hunter (spec: research spec_chunk1). Shared pieces: Miniaturize / Gigantify (Generic.OnPlayReplacement,
// the Mini plays its original's script: Loader), the excess hit (Generic.DamageWithExcess), Spellstones that count down
// (Generic.SpellstoneCountdown, Power.CorpsesGainedTask), "(Trade to upgrade!)" (Generic.Trade), the starting deck's runes
// (Generic.DeckStartedWithRune), Power.ExactLethal, the Wonders temporary Secrets, Deios's HERO_POWER_TWICE. Every number goes through V.
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
		/// <summary>Client EVIL_TWIN_MUSTACHE (2611): the funny mustache (Actor.ActivateEvilTwinMustache).</summary>
		private const GameTag EVIL_TWIN_MUSTACHE = (GameTag)2611;

		private static bool IsUndeadCard(IPlayable p) => p is Minion && p.Card.IsRace(Race.UNDEAD);

		/// <summary>A minion to the board next to the source (null: full / none).</summary>
		private static Minion SummonNew(Game g, Controller c, Card card, IEntity source, int position = -1)
		{
			if (card == null || c.BoardZone.IsFull) return null;
			var m = (Minion)Entity.FromCard(c, card);
			Generic.SummonBlock.Invoke(g, m, position, source);
			return m.Zone?.Type == Zone.PLAY ? m : null;
		}

		/// <summary>"Swap Attack / Health / stats with another minion" (the Origami cards; rule book: swaps set both to the other's current
		/// values at once): both get <paramref name="enchantment"/> with the other's numbers.</summary>
		private static void SwapStats(Game g, IEntity source, Minion a, Minion b, string enchantment, bool attack, bool health)
		{
			if (a == null || b == null || a.Id == b.Id) return;
			int aAtk = a.AttackDamage, aHp = a.Health, bAtk = b.AttackDamage, bHp = b.Health;
			Generic.AddEnchantmentBlock(g, Cards.FromId(enchantment), source as IPlayable ?? a, a, attack ? bAtk : bHp, attack && health ? bHp : 0, 0);
			Generic.AddEnchantmentBlock(g, Cards.FromId(enchantment), source as IPlayable ?? a, b, attack ? aAtk : aHp, attack && health ? aHp : 0, 0);
		}

		/// <summary>Dr. Stitchensew's next Discover (5, 3, then 1-Cost): the pick is stitched (HsTag.STITCH_1..3, dbf ids), no card to hand.</summary>
		private static void StitchNext(Controller c, IEntity doctor, int step)
		{
			int[] costs = { V("TOY_830", "first", 5), V("TOY_830", "second", 3), V("TOY_830", "third", 1) };
			Game g = c.Game;
			if (step >= costs.Length)
			{
				if (doctor is IPlayable doc && doc[HsTag.STITCH_1] > 0) GiveStitch(g, doc, doc, doc[HsTag.STITCH_1], doc[HsTag.STITCH_2], doc[HsTag.STITCH_3]);
				return;
			}
			var pool = DiscoverFromTask.PickDistinct(g, Cards.FormatTypeCards(g.FormatType)
				.Where(x => x.Type == CardType.MINION && x.Cost == costs[step] && (x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL)) && !x.NeverGenerated), 3);
			if (pool.Count == 0) { StitchNext(c, doctor, step + 1); return; }
			Generic.CreateChoiceCards.Invoke(c, doctor, null, ChoiceType.GENERAL, ChoiceAction.STACK, pool.ToArray(), new CustomTask((g2, c2, s2, pick, st) =>
			{
				if (pick != null)
				{
					s2[HsTag.STITCH_1 + step] = pick.Card.AssetId;
					pick.Zone?.Remove(pick);
					c2.GraveyardZone.Add(pick);
				}
				StitchNext(c2, s2, step + 1);
			}));
		}

		/// <summary>TOY_830e "Deathrattle: Summon {0}." on <paramref name="minion"/>: HIDDEN_CHOICE = the next stitched minion (the client's
		/// text), the rest of the chain on the enchantment (HsTag.STITCH_2 / _3).</summary>
		private static void GiveStitch(Game g, IEntity source, IPlayable minion, int next, int then, int last)
		{
			if (next <= 0 || !(Cards.TryFromId("TOY_830e") is Card stitch)) return;
			Generic.AddEnchantmentBlock(g, stitch, source as IPlayable ?? minion, minion, 0, 0, 0);
			if (minion.AppliedEnchantments?.LastOrDefault(e => e.Card.Id == "TOY_830e") is Enchantment e)
			{
				e[GameTag.HIDDEN_CHOICE] = next;
				e[HsTag.STITCH_2] = then;
				e[HsTag.STITCH_3] = last;
			}
		}

		/// <summary>Pro Gamer's choice for <paramref name="who"/>: Rock, Paper or Scissors (MIS_916a/b/c); <paramref name="picked"/> gets 1/2/3.</summary>
		private static void RockPaperScissors(Controller who, IEntity source, Action<Game, Controller, IEntity, int> picked)
		{
			Card[] options = { Cards.FromId("MIS_916a"), Cards.FromId("MIS_916b"), Cards.FromId("MIS_916c") };
			Generic.CreateChoiceCards.Invoke(who, source, null, ChoiceType.GENERAL, ChoiceAction.STACK, options, new CustomTask((g, c, s, pick, st) =>
			{
				int choice = pick == null ? 1 + g.Random.Next(3) : Array.FindIndex(options, o => o.Id == pick.Card.Id) + 1;
				pick?.Zone?.Remove(pick);
				if (pick != null) c.GraveyardZone.Add(pick);
				picked(g, c, s, choice);
			}));
		}

		/// <summary>A first-edition Demon Hunter card (Ci'Cigi's RelatedCards).</summary>
		private static Card FirstEditionDemonHunterCard(Game g)
		{
			var pool = ReprintCards.RelatedCardIds("TOY_913")?.Select(Cards.TryFromId).Where(x => x != null).ToList();
			return pool == null || pool.Count == 0 ? null : pool[g.Random.Next(pool.Count)];
		}

		private static void Whizbang(IDictionary<string, CardDef> cards)
		{
			WhizbangChunk1(cards);
			WhizbangChunk2(cards);
			WhizbangChunk3(cards);
			WhizbangChunk4(cards);
		}

		private static void WhizbangChunk1(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Neutral
			// [TOY_000] Tar Slime (1) — Taunt. Has +2 Attack during your opponent's turn. (live dungar 5437: plain ATK changes at the switch)
			cards.Add("TOY_000", new CardDef(new Power
			{
				Aura = new Aura(AuraType.SELF, Effects.Attack_N(V("TOY_000", "attack", 2))) { Condition = new SelfCondition(p => p.Game.CurrentPlayer != p.Controller), Restless = true }
			}));
			// [TOY_006] Scarab Keychain (1) — Battlecry: Discover a 2-Cost card.
			cards.Add("TOY_006", new CardDef(new Power { PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Cost == V("TOY_006", "cost", 2)) }));
			// [TOY_054] Card Grader (3) — Battlecry: If you've cast a spell while holding this, Discover a card from your deck.
			cards.Add("TOY_054", new CardDef(HeldSpell(p => p is Spell, DiscoverFromYourDeck())));
			// [TOY_307] Sweetened Snowflurry (3) — Miniaturize. Battlecry: Get 2 random Temporary Frost spells. (any class; GBL_999e)
			cards.Add("TOY_307", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TOY_307", "spells", 2); i++)
						if (GetCard(c, RandomCard(g, IsFrostCard), s) is IPlayable got) Generic.MakeTemporary(got, s);
				})
			}));
			// [TOY_312] Nostalgic Gnome (4) — Miniaturize. Rush. After this minion deals exact lethal damage on your turn, draw a card.
			cards.Add("TOY_312", new CardDef(new Power { ExactLethal = true, HonorableKillTask = new DrawTask() }));
			// [TOY_340] Nostalgic Initiate (3) — Miniaturize. The first time you cast a spell, gain +2/+2. (Spellburst spelled out)
			cards.Add("TOY_340", new CardDef(new Power { Trigger = Spellburst(new AddEnchantmentTask("TOY_340t", EntityType.SOURCE)) }));
			cards.Add("TOY_340t", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TOY_340", "stats", 2))) }));
			// [TOY_341] Nostalgic Clown (5) — Miniaturize. Battlecry: If you've played a higher Cost card while holding this, deal 4 damage. (higher
			// than the holder's own Cost: wiki 30.0 — the Mini counts the big Clown)
			cards.Add("TOY_341", new CardDef(BattlecryAny, new Power
			{
				Trigger = new Trigger(TriggerType.PLAY_CARD)
				{
					TriggerActivation = TriggerActivation.HAND,
					TriggerSource = TriggerSource.FRIENDLY,
					CountsWhileHeld = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is IPlayable me) || me.Zone?.Type != Zone.HAND || t == null || t.Id == me.Id || Generic.ThresholdMet(me)) return;
						if (PlayedCost(t) > me.Cost) me[GameTag.TAG_SCRIPT_DATA_NUM_1] = me[GameTag.TAG_SCRIPT_DATA_NUM_1] + 1;
					})
				},
				ThresholdValue = 1,
				PowerTask = ComplexTask.If((g, c, s) => Generic.ThresholdMet(s), new DamageTask(V("TOY_341", "damage", 4), EntityType.TARGET))
			}));
			// [TOY_386] Giftwrapped Whelp (1) — Battlecry: If you're holding a Dragon, give it and this minion +1/+1. (several: your pick — the
			// hand-pick rule; doubt)
			cards.Add("TOY_386", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.HandZone.Any(p => p.Id != s.Id && p.Card.IsRace(Race.DRAGON)), new CustomTask((g, c, s, t, stack) =>
				{
					var dragons = c.HandZone.GetAll().Where(p => p.Id != s.Id && p.Card.IsRace(Race.DRAGON)).ToList();
					if (dragons.Count == 1) { Give(g, s, dragons[0], "TOY_386e"); Give(g, s, (IPlayable)s, "TOY_386e"); return; }
					Generic.DiscoverFromCards(c, s, Generic.PickFrom(dragons, g, p => true), (g2, c2, s2, pick) =>
					{
						Give(g2, s2, pick, "TOY_386e");
						if (s2 is IPlayable me) Give(g2, s2, me, "TOY_386e");
					});
				}))
			}));
			cards.Add("TOY_386e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TOY_386", "stats", 1))) }));
			// [TOY_390] Clearance Promoter (3) — Deathrattle: Reduce the Cost of two spells in your hand by (1).
			cards.Add("TOY_390", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable p in c.HandZone.GetAll().Where(p => p is Spell).OrderBy(_ => g.Random.Next()).Take(V("TOY_390", "spells", 2)).ToList())
						Give(g, s, p, "TOY_390e");
				})
			}));
			cards.Add("TOY_390e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("TOY_390", "less", 1))) }));
			// [TOY_391] Caricature Artist (4) — Battlecry: Draw a minion that costs (5) or more. Give it a funny mustache!
			cards.Add("TOY_391", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (DrawOne(g, c, p => p is Minion && p.Cost >= V("TOY_391", "cost", 5)) is IPlayable drawn) Give(g, s, drawn, "TOY_391e");
				})
			}));
			cards.Add("TOY_391e", new CardDef(new Power { Enchant = new Enchant(new Effect(EVIL_TWIN_MUSTACHE, EffectOperator.SET, 1)) }));
			// [TOY_509] Wind-Up Musician (6) — Tradeable. Battlecry: Deal @ damage to all enemy minions. (Trade to upgrade!)
			cards.Add("TOY_509", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					DamageAll(s, c.Opponent.BoardZone.GetAll().Where(m => !(m is Location)).Cast<ICharacter>(), Math.Max(1, s[GameTag.TAG_SCRIPT_DATA_NUM_1]), false))
			}));
			// [TOY_517] Plucky Paintfin (3) — Poisonous. Battlecry: Draw a Rush minion.
			cards.Add("TOY_517", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => DrawOne(g, c, p => p is Minion && p.Card[GameTag.RUSH] > 0)) }));
			// [TOY_518] Treasure Distributor (1) — After you summon a Pirate, give it +1 Attack.
			cards.Add("TOY_518", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					Condition = SelfCondition.IsRace(Race.PIRATE),
					SingleTask = new AddEnchantmentTask("TOY_518e", EntityType.TARGET)
				}
			}));
			cards.Add("TOY_518e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TOY_518", "attack", 1))) }));
			// [TOY_520] Observer of Mysteries (3) — Battlecry: Cast 2 random Secrets. At the start of your turn, destroy them. (live inst 10159: put
			// into the Secret zone, any class — Disco at the End of Time's shape)
			cards.Add("TOY_520", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var me = (IPlayable)s;
					Generic.AddEnchantmentBlock(g, Cards.FromId("TOY_520e"), me, c, 0, 0, 0);
					Generic.MarkSecretsForDestroy(Generic.PutRandomSecrets(c, me, null, V("TOY_520", "secrets", 2)), me);
				})
			}));
			cards.Add("TOY_520e", new CardDef(Generic.DestroyMarkedSecretsPower));
			// [TOY_528] Sing-Along Buddy (3) — Your Hero Power triggers twice. (Deios's HERO_POWER_TWICE: two Buddies still twice)
			cards.Add("TOY_528", new CardDef(new Power { Aura = new Aura(AuraType.CONTROLLER, new Effect(Generic.HERO_POWER_TWICE, EffectOperator.ADD, 1)) }));
			// [TOY_530] Playhouse Giant (25) — Costs (1) less for each card you've drawn this game.
			cards.Add("TOY_530", new CardDef(new Power { Aura = new AdaptiveCostEffect(p => p.Controller[HsTag.CARDS_DRAWN_THIS_GAME]) }));
			// [TOY_531] Li'Na, Shop Manager (6) — Whenever you cast a spell, fill your board with random minions of that Cost. (the Cost it was
			// played for: forum 155099)
			cards.Add("TOY_531", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.CAST_SPELL)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t == null) return;
						int cost = PlayedCost(t);
						for (int guard = 0; guard < 7 && !c.BoardZone.IsFull; guard++)
							if (SummonNew(g, c, RandomMinionOfCost(g, cost), s) == null) break;
					})
				}
			}));
			// [TOY_601] Factory Assemblybot (10) — Miniaturize. At the end of your turn, summon a 6/7 Bot that attacks a random enemy.
			cards.Add("TOY_601", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(SummonNew(g, c, Cards.FromId("TOY_601t2"), s) is Minion bot)) return;
						var enemies = AttackableEnemies(c);
						if (enemies.Count > 0) EffectAttack(g, bot, enemies[g.Random.Next(enemies.Count)]);
					})
				}
			}));
			// [TOY_646] Messmaker (3) — Lifesteal, Taunt. Deathrattle: Deal 1 damage to all enemies.
			cards.Add("TOY_646", new CardDef(new Power { DeathrattleTask = new DamageTask(V("TOY_646", "damage", 1), EntityType.ENEMIES) }));
			// [TOY_670] Giggling Toymaker (4) — Deathrattle: Summon two 1/2 Mechs with Taunt and Divine Shield. (its COLLECTION_RELATED card:
			// CORE_GVG_085 Annoy-o-Tron)
			cards.Add("TOY_670", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					Card mech = Cards.FromAssetId(s.Card[GameTag.COLLECTION_RELATED_CARD_DATABASE_ID]);
					for (int i = 0; i < V("TOY_670", "mechs", 2); i++) Summon(g, c, mech, s);
				})
			}));
			// [TOY_700] Splendiferous Whizbang (4) — You start the game with one of Whizbang's Experimental Decks! (in play a vanilla 4/5 — wiki;
			// the deck swap is a deck-building rule: doubt list)
			cards.Add("TOY_700", new CardDef(new Power { PowerTask = Nothing }));
			// [TOY_703] Colifero the Artist (8) — Battlecry: Draw a minion. Transform all other friendly minions into copies of it. (with its
			// enchantments: wiki 30.2 fix)
			cards.Add("TOY_703", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(DrawOne(g, c, p => p is Minion) is IPlayable drawn)) return;
					var buffs = drawn.AppliedEnchantments?.Select(e => (e.Card, e[GameTag.TAG_SCRIPT_DATA_NUM_1], e[GameTag.TAG_SCRIPT_DATA_NUM_2])).ToList();
					foreach (Minion m in c.BoardZone.GetAll().Where(m => m.Id != s.Id && !(m is Location)).ToList())
					{
						Generic.TransformBlock.Invoke(c, drawn.Card, m);
						if (buffs != null && m.Zone?.Type == Zone.PLAY)
							foreach (var (card, n1, n2) in buffs) Generic.AddEnchantmentBlock(g, card, (IPlayable)s, m, n1, n2, 0);
					}
				})
			}));
			// [TOY_814] Bucket of Soldiers (3) — Deathrattle: Summon five 1/1 Soldiers with random Bonus Effects. (its RelatedCards; repeats allowed)
			cards.Add("TOY_814", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TOY_814", "soldiers", 5); i++) Summon(g, c, Generic.RandomRelated(g, s.Card, CardType.MINION), s);
				})
			}));
			// [TOY_820] Forgotten Animatronic (5) — At the end of your turn, destroy a minion with less Attack than this.
			cards.Add("TOY_820", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me)) return;
						var lower = c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll())
							.Where(m => m.Id != me.Id && !(m is Location) && !m.IsDead && !m.ToBeDestroyed && !m.Untouchable && m.AttackDamage < me.AttackDamage).ToList();
						if (lower.Count > 0) Generic.Destroy(lower[g.Random.Next(lower.Count)], s);
					})
				}
			}));
			// [TOY_866] Corridor Sleeper (1) — Starts Dormant. After 7 minions die, awaken. (both sides: "7 minions")
			cards.Add("TOY_866", new CardDef(new Power { Trigger = Generic.AwakenAfterDeaths(null, TriggerSource.ALL_MINIONS_EXCEPT_SELF) }));
			// [TOY_878] Cosplay Contestant (3) — After your opponent plays a minion, transform into a 3/4 copy of it. (live heist 21738: index 2)
			cards.Add("TOY_878", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.ENEMY,
					EffectIndex = 2,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me) || me.Zone?.Type != Zone.PLAY || !(t is Minion played)) return;
						if (!Generic.TransformBlock.Invoke(c, played.Card, me)) return;
						me[HsTag.COPIED_FROM_ENTITY_ID] = played.Id;
						Generic.AddEnchantmentBlock(g, Cards.FromId("TOY_878e"), me, me, V("TOY_878", "attack", 3), V("TOY_878", "health", 4), 0);
					})
				}
			}));
			cards.Add("TOY_878e", new CardDef(new Power { Enchant = Enchants.Enchants.SetAttackHealthScriptTag }));
			// [TOY_891] Workshop Janitor (5) — Battlecry: If you control a location, draw 2 cards.
			cards.Add("TOY_891", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.BoardZone.GetSpan().ToArray().Any(m => m is Location), ComplexTask.Repeat(new DrawTask(), V("TOY_891", "cards", 2)))
			}));
			// [TOY_893] Nesting Golem (4) — Deathrattle: Resummon this with -1/-1. (of its base stats: forum 123499; none at 0 Health)
			cards.Add("TOY_893", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.BoardZone.IsFull || !(s is IPlayable dead) || Generic.PermanentBase(dead, GameTag.HEALTH) - V("TOY_893", "less", 1) < 1) return;
					if (Generic.CopyWithPermanentStats(c, s, dead, -V("TOY_893", "less", 1), -V("TOY_893", "less", 1)) is Minion golem)
						Generic.SummonBlock.Invoke(g, golem, -1, s);
				})
			}));
			// [TOY_894] Origami Frog (5) — Rush. Battlecry: Swap Attack with another minion. / [TOY_895] Origami Crane — Health. / [TOY_896] Origami
			// Dragon — stats. (the enchantments by their text: Frog Paper / Crane Paper / Folding Paper)
			cards.Add("TOY_894", new CardDef(BattlecryMinion, new Power { PowerTask = new CustomTask((g, c, s, t, stack) => SwapStats(g, s, s as Minion, t as Minion, "TOY_896e", true, false)) }));
			cards.Add("TOY_895", new CardDef(BattlecryMinion, new Power { PowerTask = new CustomTask((g, c, s, t, stack) => SwapStats(g, s, s as Minion, t as Minion, "TOY_895e", false, true)) }));
			cards.Add("TOY_896", new CardDef(BattlecryMinion, new Power { PowerTask = new CustomTask((g, c, s, t, stack) => SwapStats(g, s, s as Minion, t as Minion, "TOY_894e", true, true)) }));
			cards.Add("TOY_896e", new CardDef(new Power { Enchant = Enchants.Enchants.SetAttackScriptTag }));
			cards.Add("TOY_895e", new CardDef(new Power { Enchant = Enchants.Enchants.SetHealthScriptTag }));
			cards.Add("TOY_894e", new CardDef(new Power { Enchant = Enchants.Enchants.SetAttackHealthScriptTag }));
			// [TOY_897] Floppy Hydra (3) — Deathrattle: Shuffle a copy of this into your deck with permanently doubled Attack and Health. (base stats:
			// wiki "4→8→16→32", 32.2 fix: no enchantments)
			cards.Add("TOY_897", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is IPlayable dead)) return;
					IPlayable copy = Generic.CopyWithPermanentStats(c, s, dead, Generic.PermanentBase(dead, GameTag.ATK), Generic.PermanentBase(dead, GameTag.HEALTH));
					Generic.ShuffleIntoDeck.Invoke(c, s, copy);
				})
			}));
			// [TOY_943] Rumble Enthusiast (3) — After you play the left- or right-most card in your hand, deal 1 damage to a random enemy.
			cards.Add("TOY_943", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = PlayedAsOutcast,
					SingleTask = ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, V("TOY_943", "damage", 1))
				}
			}));
			// [TOY_960] Joymancer Jepetto (8) — Battlecry: Get copies of every 1-Attack or 1-Health minion you've played this game. (printed stats)
			cards.Add("TOY_960", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (PlayHistoryEntry h in c.PlayHistory.Where(h => h.SourceId != s.Id && h.SourceCard?.Type == CardType.MINION &&
						         (h.SourceCard.ATK == 1 || h.SourceCard.Health == 1)).ToList())
					{
						if (c.HandZone.IsFull) break;
						GetCard(c, h.SourceCard, s);
					}
				})
			}));
			// [MIS_025] The Replicator-inator (5) — Miniaturize, Gigantify. After you play a minion with the same Attack as this, summon a copy of it.
			cards.Add("MIS_025", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion me && t is Minion played && played.Id != me.Id && played.Zone?.Type == Zone.PLAY && played.AttackDamage == me.AttackDamage && !c.BoardZone.IsFull)
							Generic.Copy(c, s, played, Zone.PLAY);
					})
				}
			}));
			// [MIS_026] Puppetmaster Dorian (5) — After you draw a minion, get a 1/1 copy of it that costs (1).
			cards.Add("MIS_026", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DRAW)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable drawn = t ?? g.CurrentEventData?.EventSource as IPlayable;
						if (!(Generic.IsMinionCard(drawn)) || drawn.Zone?.Type != Zone.HAND || c.HandZone.IsFull) return;
						if (Generic.Copy(c, s, drawn, Zone.HAND) is IPlayable copy && copy.Zone?.Type == Zone.HAND) Give(g, s, copy, "MIS_026e");
					})
				}
			}));
			cards.Add("MIS_026e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.SetAttackHealth(V("MIS_026", "stats", 1)).Append(Effects.SetCost(V("MIS_026", "cost", 1))).ToArray())
			}));
			// [MIS_308] Explodineer (3) — At the end of your turn, shuffle a Bomb into your opponent's deck. When drawn, it explodes for 5 damage.
			cards.Add("MIS_308", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
						Generic.ShuffleIntoDeck.Invoke(c.Opponent, s, Entity.FromCard(c.Opponent, Cards.FromId("BOT_511t"), new EntityData { { GameTag.CREATOR, s.Id } })))
				}
			}));
			// [MIS_314] Building-Block Golem (5) — Rush. Deathrattle: Summon three random 1-Cost minions.
			cards.Add("MIS_314", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("MIS_314", "minions", 3); i++) Summon(g, c, RandomMinionOfCost(g, V("MIS_314", "cost", 1)), s);
				})
			}));
			// [MIS_916] Pro Gamer (2) — Battlecry: Challenge your opponent to a game of Rock-Paper-Scissors! The winner draws 2 cards. (wiki: the
			// opponent picks at the start of their turn; a tie: nothing; Pro Gamer gone by then: nothing)
			cards.Add("MIS_916", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => RockPaperScissors(c, s, (g2, c2, s2, pick) =>
				{
					s2[HsTag.RPS_PICK] = pick;
					if (s2 is IPlayable gamer && Cards.TryFromId("MIS_916e") is Card roshambo) Generic.AddEnchantmentBlock(g2, roshambo, gamer, gamer, 0, 0, 0);
				})),
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					EitherTurn = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion gamer) || gamer.Zone?.Type != Zone.PLAY || g.CurrentPlayer != c.Opponent || gamer[HsTag.RPS_PICK] == 0) return;
						int mine = gamer[HsTag.RPS_PICK];
						gamer[HsTag.RPS_PICK] = 0;
						RockPaperScissors(c.Opponent, gamer, (g2, them, s2, theirs) =>
						{
							if (theirs == mine) return;
							Controller me = them.Opponent;
							Controller winner = (mine % 3) + 1 == theirs ? them : me; // Rock 1 < Paper 2 < Scissors 3 < Rock
							for (int i = 0; i < V("MIS_916", "cards", 2); i++) Generic.Draw(winner);
						});
					})
				}
			}));
			cards.Add("MIS_916e", new CardDef(new Power { Enchant = new Enchant() }));
			foreach (string option in new[] { "MIS_916a", "MIS_916b", "MIS_916c" }) cards.Add(option, new CardDef(new Power { PowerTask = Nothing }));

			// ---------------------------------------------------------------- Death Knight
			// [MIS_006] Toysnatching Geist (3) — Gigantify. Battlecry: Discover an Undead. Reduce its Cost by this minion's Attack.
			cards.Add("MIS_006", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.IsRace(Race.UNDEAD), null,
					new CustomTask((g, c, s, t, stack) => { if (t != null && s is Minion me && me.AttackDamage > 0) Give(g, s, t, "MIS_006e1", me.AttackDamage); }))
			}));
			cards.Add("MIS_006e1", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(0)) { UseScriptTag = true } }));
			// [MIS_100] Helm of Humiliation (4) — Give a minion -5/-5. Give a random minion in your hand +5/+5.
			cards.Add("MIS_100", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("MIS_100e", EntityType.TARGET), BuffRandomInHand(p => p is Minion, "MIS_100e1"))
			}));
			cards.Add("MIS_100e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(-V("MIS_100", "minus", 5))) }));
			cards.Add("MIS_100e1", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("MIS_100", "plus", 5))) }));
			// [TOY_821] Rambunctious Stuffy (3) — Rush. After you cast a Frost spell, gain Reborn.
			cards.Add("TOY_821", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => IsFrostSpell(p)),
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me && me[GameTag.REBORN] == 0) me[GameTag.REBORN] = 1; })
				}
			}));
			// [TOY_822] Silk Stitching (2) — Choose a friendly minion. Discover a spell that costs (4) or less for it to cast when it dies.
			cards.Add("TOY_822", new CardDef(TargetFriendlyMinion, new Power
			{
				PowerTask = new DiscoverFromTask((g, c, s) => Cards.FormatTypeCards(g.FormatType).Where(x => x.Type == CardType.SPELL &&
						x.Cost <= V("TOY_822", "cost", 4) && (x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL))), null,
					new CustomTask((g, c, s, t, stack) =>
					{
						if (t == null || !(s is IPlayable silk) || !g.IdEntityDic.TryGetValue(silk.CardTarget, out IPlayable minion) || minion.Zone?.Type != Zone.PLAY) return;
						Generic.AddEnchantmentBlock(g, Cards.FromId("TOY_822e"), silk, minion, t.Id, 0, 0); // NUM_1 = the set-aside spell ("Cast {0}")
					}), ChoiceAction.STACK)
			}));
			cards.Add("TOY_822e", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t != null && g.IdEntityDic.TryGetValue(t[GameTag.TAG_SCRIPT_DATA_NUM_1], out IPlayable spell))
						new CastRandomSpellTask { Spell = spell.Card }.Process(g, c, s, null);
				})
			}));
			// [TOY_823] Rainbow Seamstress (3) — Battlecry: If your deck started with a Blood, Frost, or Unholy card, gain Lifesteal, Reborn, or Rush
			// respectively.
			cards.Add("TOY_823", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => Generic.DeckStartedWithRune(c, 7), new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me)) return;
					if (Generic.DeckStartedWithRune(c, 1)) me[GameTag.LIFESTEAL] = 1;
					if (Generic.DeckStartedWithRune(c, 2)) me[GameTag.REBORN] = 1;
					if (Generic.DeckStartedWithRune(c, 4)) me[GameTag.RUSH] = 1;
				}))
			}));
			// [TOY_824] Darkthorn Quilter (4) — At the end of your turn, deal this minion's Attack damage randomly split among enemies.
			cards.Add("TOY_824", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion me && me.AttackDamage > 0)
							new EnqueueTask(me.AttackDamage, ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, 1)).Process(g, c, s, null);
					})
				}
			}));
			// [TOY_825] Lesser Spinel Spellstone (1) — Give Undead in your hand +1/+1. (Gain @ Corpses to upgrade.) → TOY_825t (+2/+2) → TOY_825t2
			foreach (var (id, buff, next) in new[] { ("TOY_825", "TOY_825e", "TOY_825t"), ("TOY_825t", "TOY_825e2", "TOY_825t2"), ("TOY_825t2", "TOY_825e3", null) })
				cards.Add(id, new CardDef(new Power
				{
					PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.HandZone.GetAll().Where(IsUndeadCard).ToList(), buff)),
					CorpsesGainedTask = next == null ? null : new CustomTask((g, c, s, t, stack) => Generic.SpellstoneCountdown((IPlayable)s, stack.Number, next))
				}));
			cards.Add("TOY_825e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TOY_825", "stats", 1))) }));
			cards.Add("TOY_825e2", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TOY_825t", "stats", 2))) }));
			cards.Add("TOY_825e3", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TOY_825t2", "stats", 3))) }));
			// [TOY_826] Threads of Despair (2) — Give all minions "Deathrattle: Deal 1 damage to all minions."
			cards.Add("TOY_826", new CardDef(new Power { PowerTask = new AddEnchantmentTask("TOY_826e", EntityType.ALLMINIONS) }));
			cards.Add("TOY_826e", new CardDef(new Power { DeathrattleTask = new DamageTask(V("TOY_826", "damage", 1), EntityType.ALLMINIONS) }));
			// [TOY_828] Amateur Puppeteer (5) — Miniaturize, Taunt. Deathrattle: Give Undead in your hand +2/+2.
			cards.Add("TOY_828", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.HandZone.GetAll().Where(IsUndeadCard).ToList(), "TOY_828e4"))
			}));
			cards.Add("TOY_828e4", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TOY_828", "stats", 2))) }));
			// [TOY_830] Dr. Stitchensew (5) — Battlecry: Discover a 5, 3, and 1-Cost minion to stitch to this. Deathrattle: Summon the 5-Cost minion.
			// (wiki: the 5-Cost gets the Deathrattle summoning the 3-Cost, and so on)
			cards.Add("TOY_830", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => StitchNext(c, s, 0)) }));
			cards.Add("TOY_830e", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Enchantment e) || !(Cards.FromAssetId(e[GameTag.HIDDEN_CHOICE]) is Card next)) return;
					if (SummonNew(g, c, next, s) is Minion summoned) GiveStitch(g, s, summoned, e[HsTag.STITCH_2], e[HsTag.STITCH_3], 0);
				})
			}));

			// ---------------------------------------------------------------- Demon Hunter
			// [MIS_102] Return Policy (3) — Discover a friendly Deathrattle card you've played this game. Trigger its Deathrattle. (its own
			// Deathrattle: wiki 29.6 fix)
			cards.Add("MIS_102", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var played = c.PlayHistory.Select(h => h.SourceCard).Where(x => x != null && x[GameTag.DEATHRATTLE] > 0 && x.Power?.DeathrattleTask != null)
						.GroupBy(x => x.Id).Select(x => x.First()).ToList();
					var offer = DiscoverFromTask.PickDistinct(g, played, 3);
					if (offer.Count == 0) return;
					Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.STACK, offer.ToArray(), new CustomTask((g2, c2, s2, pick, st) =>
					{
						if (pick == null) return;
						Generic.TriggerDeathrattleOf(s2, pick);
					}));
				})
			}));
			// [MIS_710] Sock Puppet Slitherspear (1) — This minion's Attack is improved by your hero's.
			cards.Add("MIS_710", new CardDef(new Power { Aura = new AdaptiveEffect(GameTag.ATK, EffectOperator.ADD, p => Math.Max(0, p.Controller.Hero.AttackDamage)) }));
			// [MIS_911] Gibbering Reject (4) — After your hero attacks, summon another Gibbering Reject.
			cards.Add("MIS_911", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = new CustomTask((g, c, s, t, stack) => SummonNew(g, c, Cards.FromId("MIS_911"), s)) }
			}));
			// [TOY_028] Spirit of the Team (2) — Stealth for 1 turn. Your hero has +2 Attack on your turn.
			cards.Add("TOY_028", new CardDef(new Power
			{
				Trigger = TriggerLibrary.OneTurnStealth,
				Aura = new Aura(AuraType.HERO, "TOY_028e") { Condition = SelfCondition.IsCurrentPlayer, Restless = true }
			}));
			cards.Add("TOY_028e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TOY_028", "attack", 2))) }));
			// [TOY_640] Workshop Mishap (4) — Deal $5 damage to a minion. Excess damages both neighbors. Outcast: Gain Lifesteal. (live vfx 276716:
			// the capped hit, then the AdjacentBurst effect sourced at the target)
			cards.Add("TOY_640", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m) || !(s is Spell me)) return;
					if (PlayedAsOutcast.Eval(me)) me[GameTag.LIFESTEAL] = 1;
					var row = m.Controller.BoardZone;
					var neighbours = new[] { m.ZonePosition - 1, m.ZonePosition + 1 }.Where(i => i >= 0 && i < row.Count).Select(i => row[i]).Where(x => !(x is Location)).ToList();
					int excess = Generic.DamageWithExcess(me, m, V("TOY_640", "damage", 5), true);
					if (excess <= 0 || neighbours.Count == 0) return;
					string burst = SubSpells.FindNamed("TOY_640", "AdjacentBurst");
					bool open = g.History && burst != null && g.OpenSubSpell(m, burst, new[] { m.Id });
					foreach (Minion n in neighbours) Generic.DamageCharFunc(me, n, excess, false);
					if (open) g.CloseSubSpells(m.Id);
				})
			}));
			// [TOY_641] Umpire's Grasp (4, weapon) — Deathrattle: Draw a Demon and reduce its Cost by (2).
			cards.Add("TOY_641", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => { if (DrawOne(g, c, p => IsDemon(p)) is IPlayable drawn) Give(g, s, drawn, "TOY_641e"); })
			}));
			cards.Add("TOY_641e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("TOY_641", "less", 2))) }));
			// [TOY_642] Ball Hog (4) — Lifesteal. Battlecry and Deathrattle: Deal 3 damage to the lowest Health enemy. (live night 76554: its dart)
			ISimpleTask ballHog = new CustomTask((g, c, s, t, stack) =>
			{
				if (!(LowestHealthEnemy(g, c) is ICharacter low)) return;
				Generic.MissileFx((IPlayable)s, low, () => Generic.DamageCharFunc((IPlayable)s, low, V("TOY_642", "damage", 3), false));
			});
			cards.Add("TOY_642", new CardDef(new Power { PowerTask = ballHog, DeathrattleTask = ballHog }));
			// [TOY_643] Blind Box (2) — Get 2 random Demons. Outcast: Discover them instead. (random: any class — live 13797; Discover: class + neutral)
			cards.Add("TOY_643", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (PlayedAsOutcast.Eval((IPlayable)s))
						DiscoverFromTask.ClassOrNeutral((c2, x) => x.Type == CardType.MINION && x.IsRace(Race.DEMON), null,
							DiscoverFromTask.ClassOrNeutral((c3, x) => x.Type == CardType.MINION && x.IsRace(Race.DEMON))).Process(g, c, s, null);
					else
						for (int i = 0; i < V("TOY_643", "demons", 2); i++) GetCard(c, RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.DEMON)), s);
				})
			}));
			// [TOY_644] Red Card (1) — Make a minion go Dormant for 2 turns.
			cards.Add("TOY_644", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m) Generic.MakeDormant(m, V("TOY_644", "turns", 2), "Toy_644e1", (IPlayable)s); })
			}));
			// [TOY_645] Lesser Opal Spellstone (2) — Draw 1 card. (Attack with your hero @ times to upgrade.) → TOY_645t (2) → TOY_645t1 (3)
			foreach (var (id, draws, next) in new[] { ("TOY_645", 1, "TOY_645t"), ("TOY_645t", 2, "TOY_645t1"), ("TOY_645t1", 3, null) })
				cards.Add(id, new CardDef(new Power
				{
					PowerTask = ComplexTask.Repeat(new DrawTask(), V(id, "cards", draws)),
					Trigger = next == null ? null : new Trigger(TriggerType.AFTER_ATTACK)
					{
						TriggerActivation = TriggerActivation.HAND,
						TriggerSource = TriggerSource.HERO,
						SingleTask = new CustomTask((g, c, s, t, stack) => Generic.SpellstoneCountdown((IPlayable)s, 1, next))
					}
				}));
			// [TOY_647] Magtheridon, Unreleased (8) — Dormant for 2 turns. While Dormant, deal 3 damage to all enemies at the end of your turn.
			cards.Add("TOY_647", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					WhileDormant = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion me && me[GameTag.DORMANT] > 0) new DamageTask(V("TOY_647", "damage", 3), EntityType.ENEMIES).Process(g, c, s, null);
					})
				}
			}));
			// [TOY_652] Window Shopper (5) — Miniaturize. Battlecry: Discover a Demon. Set its stats and Cost to this minion's.
			cards.Add("TOY_652", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.IsRace(Race.DEMON), null,
					new CustomTask((g, c, s, t, stack) =>
					{
						if (t == null || !(s is Minion me)) return;
						Generic.AddEnchantmentBlock(g, Cards.FromId("TOY_652e"), me, t, me.AttackDamage, me.Health, 0);
						Generic.AddEnchantmentBlock(g, Cards.FromId("TOY_652e2"), me, t, me.Cost, 0, 0);
					}))
			}));
			cards.Add("TOY_652e", new CardDef(new Power { Enchant = Enchants.Enchants.SetAttackHealthScriptTag }));
			cards.Add("TOY_652e2", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(0)) { UseScriptTag = true } }));
			// [TOY_913] Ci'Cigi (3) — Battlecry, Outcast, and Deathrattle: Get a random first-edition Demon Hunter card (in mint condition).
			cards.Add("TOY_913", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					GetCard(c, FirstEditionDemonHunterCard(g), s);
					if (PlayedAsOutcast.Eval((IPlayable)s)) GetCard(c, FirstEditionDemonHunterCard(g), s);
				}),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => GetCard(c, FirstEditionDemonHunterCard(g), s))
			}));
			// the first-edition cards (Patch 17.0.0 numbers, wiki)
			cards.Add("TOY_400t3", new CardDef(new Power { PowerTask = ComplexTask.Create(new AddEnchantmentTask("TOY_400t3e", EntityType.HERO), new AddCardTo("TOY_400t3t", EntityType.HAND)) }));
			cards.Add("TOY_400t3t", new CardDef(new Power { PowerTask = new AddEnchantmentTask("TOY_400t3e", EntityType.HERO) }));
			cards.Add("TOY_400t3e", OneTurn(Effects.Attack_N(V("TOY_400t3", "attack", 1))));
			cards.Add("TOY_913t2", new CardDef(new Power // Battlefiend: After your hero attacks, gain +1 Attack.
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = new AddEnchantmentTask("BT_351e", EntityType.SOURCE) }
			}));
			cards.Add("TOY_913t1", new CardDef(new Power { NoScript = true })); // Aldrachi Warblades: Lifesteal
			cards.Add("TOY_401t2", new CardDef(new Power // Altruis the Outcast
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = PlayedAsOutcast,
					SingleTask = new DamageTask(V("TOY_401t2", "damage", 1), EntityType.ENEMIES)
				}
			}));
			cards.Add("TOY_400t8", new CardDef(TargetMinion, new Power // Eye Beam: Lifesteal. Deal $3 damage to a minion. Outcast: This costs (0).
			{
				PowerTask = new DamageTask(V("TOY_400t8", "damage", 3), EntityType.TARGET, true),
				Aura = new AdaptiveCostEffect(p => InOutcastSlot(p) ? p.Card.Cost : 0)
			}));
			cards.Add("TOY_913t3", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE_AND_HERO_ATTACKED_THIS_TURN, 0 } }, new Power
			{
				PowerTask = new DamageTask(V("TOY_913t3", "damage", 4), EntityType.TARGET) // Glaivebound Adept
			}));
			cards.Add("TOY_400t4", new CardDef(new Power // Imprisoned Antaen: when this awakens, 10 split among all enemies
			{
				AwakenTask = new EnqueueTask(V("TOY_400t4", "damage", 10), ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, 1))
			}));
			cards.Add("TOY_400t6", new CardDef(new Power // Metamorphosis: the Demonic Blast Hero Power for 2 uses, then the old one back
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c[HS_SCRIPT_B] == 0) c[HS_SCRIPT_B] = c.Hero.HeroPower.Card.AssetId;
					new ReplaceHeroPower(Cards.FromId("TOY_400t6p")).Process(g, c, s, null);
				})
			}));
			cards.Add("TOY_400t6p", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("TOY_400t6p", "damage", 5), EntityType.TARGET), new CustomTask((g, c, s, t, stack) =>
				{
					new ReplaceHeroPower(Cards.FromId("TOY_400t6p2")).Process(g, c, s, null);
					c.Hero.HeroPower.IsExhausted = true;
				}))
			}));
			cards.Add("TOY_400t6p2", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("TOY_400t6p", "damage", 5), EntityType.TARGET), new CustomTask((g, c, s, t, stack) =>
				{
					int original = c[HS_SCRIPT_B];
					c[HS_SCRIPT_B] = 0;
					if (original > 0) new ReplaceHeroPower(Cards.FromAssetId(original)).Process(g, c, s, null);
					c.Hero.HeroPower.IsExhausted = true;
				}))
			}));
			cards.Add("TOY_401t", new CardDef(new Power // Skull of Gul'dan: Draw 3 cards. Outcast: Reduce their Cost by (3).
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					bool outcast = PlayedAsOutcast.Eval((IPlayable)s);
					for (int i = 0; i < V("TOY_401t", "cards", 3); i++)
						if (Generic.Draw(c) is IPlayable drawn && outcast && drawn.Zone?.Type == Zone.HAND) GiveCostLess(g, s, drawn, V("TOY_401t", "less", 3));
				})
			}));
			cards.Add("TOY_400t7", new CardDef(new Power // Warglaives of Azzinoth: After attacking a minion, your hero may attack again.
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (g.CurrentEventData?.EventTarget is Minion) c.Hero.IsExhausted = false; })
				}
			}));
			cards.Add("TOY_400t5", new CardDef(new Power // Priestess of Fury: at the end of your turn, 6 split among all enemies
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new EnqueueTask(V("TOY_400t5", "damage", 6), ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, 1)) }
			}));
		}
	}
}
