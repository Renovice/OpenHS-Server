// HsServer addition (not upstream SabberStone): TITANS (client set 1858) with Fall of Ulduar — chunk 3: Priest, Rogue and
// Paladin cards (Aman'Thul, V-07-TR-0N Prime, Amitus and the rest). Shared pieces: TitansCardsGenHs.cs (Titans, related
// tokens) and the engine's Actions/Titans.cs (Titan abilities, Magnetic from the client data, removal from the game,
// pre-damage changes, Overheal amount, "didn't start in your deck", copies with permanent stats). Research spec:
// scratchpad sets/TITANS/spec_chunk3.md. Every number goes through CardValues (V).
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
		/// <summary>"For each other &lt;this card&gt; you've summoned this game" (Astral Automaton): the player's summons of this card
		/// (Controller.SummonedThisGame, played ones included), less this one only when it was one of them (the shared
		/// Generic.OthersSummonedThisGame: a transformed-into or stolen Automaton was never counted).</summary>
		private static int TitansC3OtherSummoned(IPlayable p)
		{
			string id = p.Card.Id;
			return Generic.OthersSummonedThisGame(p, x => x.Id == id);
		}

		/// <summary>"Did not start in your deck" for a play-history entry's entity (Generic.DidntStartInDeck; an entity this game no
		/// longer knows: by its id against the first generated one).</summary>
		private static bool TitansC3DidntStartInDeck(Game g, int id) =>
			g.IdEntityDic.TryGetValue(id, out IPlayable p) ? Generic.DidntStartInDeck(p) : id >= g.FirstGeneratedEntityId;

		/// <summary>Several characters hit by one damage step (an area hit: every Damage Event first, then their triggers — rules DH1,
		/// DamageTask's batch; no missile per hit).</summary>
		private static void TitansC3DamageGroup(Game g, Controller c, IEntity source, IEnumerable<IPlayable> group, int amount, bool spell)
		{
			List<IPlayable> list = group.ToList();
			if (list.Count == 0 || amount <= 0) return;
			new DamageTask(amount, EntityType.STACK, spell).Process(g, c, source, null, new TaskStack { Playables = list });
		}

		/// <summary>
		/// V-07-TR-0N Prime: "This minion's abilities repeat on another random friendly minion." The ability on the Titan itself, then
		/// the whole ability again on one other random friendly minion (alive, awake), which is that run's own source ("Gain" = it;
		/// its damage is its own). Sources: outof.games / topdecks summary "it casts the entire ability on itself, then casts the
		/// entire ability on another friendly minion"; hearthpwn forum (rogue, 255007): "when it plays them another friendly minion
		/// also does it". No other friendly minion: no repeat.
		/// </summary>
		private static void TitansC3ThenRepeat(Game g, Controller c, IEntity titan, System.Action<Minion> ability)
		{
			if (!(titan is Minion me)) return;
			ability(me);
			if (RandomFriendly(g, c, m => !m.Untouchable, except: me) is Minion other)
				ability(other);
		}

		/// <summary>The Titan's own effect prefab named <paramref name="part"/> (its client CardDef: V-07's TTNFX_AttachCannons_Rocket_
		/// Missile / TTNFX_FullPower_Impact / TTNFX_MaximizeDefences_Impact) around <paramref name="action"/>, played from
		/// <paramref name="from"/> (the Titan, or the minion its ability repeats on), at <paramref name="targets"/>. Unrecorded
		/// placement (no live V-07 game in any capture).</summary>
		private static void TitansC3Fx(IPlayable titan, string part, IPlayable from, IEnumerable<int> targets, System.Action action)
		{
			Game g = titan.Game;
			string prefab = g.History ? SabberStoneCore.Loader.SubSpells.FindNamed(titan.Card.Id, part) : null;
			bool open = prefab != null && g.OpenSubSpell(titan, prefab, targets, shownSource: from == null || from.Id == titan.Id ? 0 : from.Id);
			action();
			if (open) g.CloseSubSpells(titan.Id);
		}

		private static void TitansChunk3(IDictionary<string, CardDef> cards)
		{
			// ================================================================ Priest

			// [TTN_401] Astral Automaton (1) — Has +1/+1 for each other Astral Automaton you've summoned this game.
			// (its own aura: card tag AURA; forum us.forums.blizzard.com 140957: "Automatons are enchanted by their own aura. If you
			// silence an automaton, it will lose its own aura". Counted by the card itself — Controller.SummonedThisGame. DOUBT: live
			// may show TTN_401e and may apply it in hand — the wiki says only in play.)
			cards.Add("TTN_401", new CardDef(new Power
			{
				Aura = new MultiAura(
					new AdaptiveEffect(GameTag.ATK, EffectOperator.ADD, p => V("TTN_401", "stats", 1) * TitansC3OtherSummoned(p)),
					new AdaptiveEffect(GameTag.HEALTH, EffectOperator.ADD, p => V("TTN_401", "stats", 1) * TitansC3OtherSummoned(p)))
			}));

			// [TTN_429] Aman'Thul (7) — Titan. After this uses an ability, Discover any Legendary minion from any class.
			// (live-20261007-vfx-decks 42717-42800: inside the after-ability TRIGGER block an EMPTY SUB_SPELL
			// TTNFX_Amanthul_Impact_Discover_Trigger from the Titan, then the Discover — offers of every class, Neutral included)
			cards.Add("TTN_429", TitansTitan(new Power { OwnFx = true }, afterAbility: new CustomTask((g, c, s, t, stack) =>
			{
				Generic.NamedFx((IPlayable)s, "Impact_Discover_Trigger", System.Array.Empty<int>(), () => { });
				new DiscoverFromTask((g2, c2, s2) => Cards.GenerationPool(g2, c2)
						.Where(x => x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY))
					.Process(g, c, s, null);
			})));
			// [TTN_429t] Shape the Stars — Choose a non-Titan minion. Summon a copy of it with +2/+2.
			// (any minion of either side but a Titan or Aman'Thul itself: live targets; the copy keeps the original's enchantments —
			// live copied DREAM_05e — and lands right of the Titan; the engine wraps it in TTNFX_Amanthul_ShapeTheStars_OverrideSpawn
			// from nowhere, Source 0, Generic.TitanAbilityFx)
			// (live 45066-45140: the copy is made, its copied enchantments and TTN_429e attached, THEN it is summoned — Generic.Copy's
			// prepare; the prefab plays from nowhere at no target: Generic.UseTitanAbility, Spawn role)
			cards.Add("TTN_429t", TitansAbility(new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion target) || c.BoardZone.IsFull || !(s is Minion titan)) return;
					Generic.Copy(c, s, target, Zone.PLAY, position: titan.Zone?.Type == Zone.PLAY ? titan.ZonePosition + 1 : -1,
						prepare: copy => Give(g, s, copy, "TTN_429e"));
				}),
				new Dictionary<PlayReq, int>
				{
					{ PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_NONSELF_TARGET, 0 },
					{ PlayReq.REQ_TARGET_IS_NON_TITAN, 0 }
				}));
			cards.Add("TTN_429e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TTN_429t", "stats", 2))) }));
			// [TTN_429t2] Strike from History — Choose two enemy minions. Remove them from the game.
			// (live 35327-35379: each pick a TARGET choice from the Titan, resolved before the next — Generic.PickTargets — inside its
			// own SUB_SPELL TTNFX_Amanthul_StrikeFromHistory_MinionRemove_Super from the Titan at the pick; Generic.RemoveFromGame: no
			// death, no Deathrattle. Usable with one enemy minion (live 48008), not with none.)
			// "Choose" = targeting by the opponent: no Stealth / Immune / untouchable enemy (Generic.Pickable; wiki Stealth.txt:7); the
			// ability needs one such minion (Power.PlayableIf — wiki Titan: "If a Titan's ability requires a target but there are no
			// valid targets, it can't be used"), shown as REQ_MINIMUM_ENEMY_MINIONS.
			cards.Add("TTN_429t2", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_MINIMUM_ENEMY_MINIONS, 1 } }, new Power
			{
				OwnFx = true,
				PlayableIf = (PlayReq.REQ_MINIMUM_ENEMY_MINIONS, ctl => Generic.HasPickableEnemyMinion(ctl, false)),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int me = c.PlayerId; // (a value, not the player: the pick may run on an AI's copy of the game)
					// live 42719-42720 / 48014-48015: OVERRIDE_HISTORY and EFFECT_TIMING naming the Titan open its POWER block; after
					// the first pick EFFECT_TIMING again, then the second pick — or, with no enemy minion left, the
					// StrikeFromHistory effect played empty (48028-48030)
					if (g.History)
						foreach (MetaDataType meta in new[] { MetaDataType.OVERRIDE_HISTORY, MetaDataType.EFFECT_TIMING })
							g.PowerHistory.Add(new SabberStoneCore.Kettle.PowerHistoryMetaData { Type = meta, Data = 0, Info = new List<int> { s.Id } });
					Generic.PickTargets(c, s, V("TTN_429t2", "minions", 2),
						ch => ch is Minion m && m.Controller.PlayerId != me && Generic.Pickable(m.Controller.Opponent, m, false),
						(g2, c2, s2, pick) => Generic.NamedFx((IPlayable)s2, "StrikeFromHistory_MinionRemove", new[] { pick.Id },
							() => Generic.RemoveFromGame((IPlayable)pick, s2)),
						betweenShots: (g2, c2, s2, another) =>
						{
							if (!g2.History) return;
							g2.PowerHistory.Add(new SabberStoneCore.Kettle.PowerHistoryMetaData { Type = MetaDataType.EFFECT_TIMING, Data = 0, Info = new List<int> { s2.Id } });
							if (!another) Generic.NamedFx((IPlayable)s2, "StrikeFromHistory_MinionRemove", System.Array.Empty<int>(), () => { });
						});
				})
			}));
			// [TTN_429t3] Vision of Heroes — Summon a random 6-Cost minion. Give it Taunt and Lifesteal.
			// (the board pool: no Colossals — wiki: "could summon Colossal minions, fixed 2023-08-04"; live: TAUNT / LIFESTEAL as plain
			// tags, no enchantment; the engine wraps it in TTNFX_Amanthul_VisionOfHeroes_OverrideSpawn, Source 0)
			cards.Add("TTN_429t3", TitansAbility(new CustomTask((g, c, s, t, stack) =>
			{
				if (c.BoardZone.IsFull || !(RandomMinionOfCost(g, V("TTN_429t3", "cost", 6)) is Card card)) return;
				var m = (Minion)Entity.FromCard(c, card);
				Generic.SummonBlock.Invoke(g, m, -1, s);
				if (m.Zone?.Type != Zone.PLAY) return;
				m.HasTaunt = true;
				m.HasLifeSteal = true;
			}), notInHistory: true)); // (live 43756 / 48179: its POWER block without ShowInHistory)

			// [TTN_441] Shapeless Constellation (8) — Battlecry: Transform into an 8/8 copy of a random minion in your hand.
			// (Cover Artist's way: TransformCopyTask, then its Attack and Health set; no minion in hand: nothing)
			cards.Add("TTN_441", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me) || me.Zone?.Type != Zone.PLAY) return;
					var minions = c.HandZone.Where(p => Generic.IsMinionCard(p)).ToList();
					if (minions.Count == 0) return;
					new TransformCopyTask().Process(g, c, me, minions[g.Random.Next(minions.Count)]);
					if (g.IdEntityDic.TryGetValue(me.Id, out IPlayable now) && now.Zone?.Type == Zone.PLAY) Give(g, now, now, "TTN_441e2");
				})
			}));
			cards.Add("TTN_441e2", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("TTN_441", "stats", 8))) }));

			// [TTN_481] Ra-den (6) — Taunt. Deathrattle: Summon each other minion you've played this game that did not start in your deck.
			// (in the order played, repeats too, each a new minion of the played card, until the board is full; live
			// live-20261004-night/224614 65660-65907: six summons; the client plays its own Deathrattle OverrideSpawn)
			cards.Add("TTN_481", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (PlayHistoryEntry h in c.PlayHistory.ToList())
					{
						if (c.BoardZone.IsFull) break;
						if (h.SourceCard?.Type != CardType.MINION || h.SourceId == s.Id || !TitansC3DidntStartInDeck(g, h.SourceId)) continue;
						Summon(g, c, h.SourceCard, s);
					}
				})
			}));

			// [TTN_482] Student of the Stars (3) — Deathrattle: Shuffle a copy of this into your deck with permanent +3/+3.
			// (a new card with raised base stats, no enchantment: Generic.CopyWithPermanentStats — live Bottomfeeder's way)
			cards.Add("TTN_482", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is IPlayable me) || c.DeckZone.IsFull) return;
					int n = V("TTN_482", "stats", 3);
					Generic.ShuffleIntoDeck.Invoke(c, s, Generic.CopyWithPermanentStats(c, s, me, n, n));
				})
			}));

			// [TTN_483] Serenity (4) — Give all enemy minions -2 Attack. Destroy any with 0 Attack.
			// (live vfx-decks 199580: ReuseFX_Holy_AE_Equality_Debuff_Super from the hero, a recorded server effect — RecordedSubSpells)
			cards.Add("TTN_483", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					GiveAll(g, s, c.Opponent.BoardZone.Where(m => !m.IsDead && !m.Untouchable), "TTN_483e");
					foreach (Minion zero in c.Opponent.BoardZone.Where(m => !m.IsDead && !m.Untouchable && m.AttackDamage <= 0).ToList())
						Generic.Destroy(zero, s);
				})
			}));
			cards.Add("TTN_483e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(-V("TTN_483", "attack", 2))) }));

			// [TTN_484] False Disciple (2) — Battlecry: Discover a Legendary Priest minion from the past.
			// ("from the past": a set no longer in Standard — Cards.FromThePast; wiki: "see Game format#Related cards"; the pool from
			// the Wild cards whatever the game's format)
			cards.Add("TTN_484", new CardDef(new Power
			{
				PowerTask = new DiscoverFromTask((g, c, s) => Cards.AllWild.Where(x =>
					x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY && x.HasClass(CardClass.PRIEST) && Cards.FromThePast(x)))
			}));

			// [TTN_485] The Stars Align (3) — Transform minions in your hand into ones that cost (3) more. (They keep their original Cost.)
			// (the wiki's cost-based transform rules — Cards.EvolvePick: the card's Cost + 3, any class; a transform keeps no
			// enchantment; TTN_485e1 holds the Cost it had, as Deck of Lunacy / Spiritsinger's way: DMF_108e)
			cards.Add("TTN_485", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int more = V("TTN_485", "more", 3);
					foreach (IPlayable minion in c.HandZone.Where(p => Generic.IsMinionCard(p)).ToList())
					{
						int cost = minion.Cost;
						if (!(Cards.EvolvePick(g.FormatType, minion, more, g.Random, c) is Card into)) continue;
						IPlayable now = Generic.ChangeEntityBlock(c, minion, into, true);
						Generic.AddEnchantmentBlock(g, Cards.FromId("TTN_485e1"), (IPlayable)s, now, cost, 0, 0);
					}
				})
			}));
			cards.Add("TTN_485e1", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(0)) { UseScriptTag = true } }));

			// [TTN_745] Grace of the Highfather (3) — Restore 8 Health. Discover a card that costs the amount Overhealed.
			// (live vfx-decks 48359: no Overheal, no Discover; 56107: a full-Health minion healed 8 → 8-Cost offers. A heal turned
			// into damage Overheals nothing.)
			cards.Add("TTN_745", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is ICharacter target)) return;
					int over = Generic.OverhealedDuring(c, () => target.TakeHeal((IPlayable)s, V("TTN_745", "heal", 8)));
					if (over > 0) DiscoverFromTask.ClassOrNeutral((c2, x) => x.Cost == over).Process(g, c, s, null);
				})
			}));

			// [YOG_300] Shadowtouched Kvaldir (1) — Battlecry: Your next healing effect deals damage instead.
			// (the "next heal deals damage" player rule — Ruby Sanctum's, without "this turn": spent by the first healing effect —
			// Character.TakeHeal, wiki patch 28.6.0.193541 — YOG_300e leaves when the next heal finds it spent. DOUBT (spec M7):
			// live may remove YOG_300e as soon as the converting effect ends.)
			cards.Add("YOG_300", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new SetControllerGameTagTask(HsTag.NEXT_HEAL_SOURCE, 0),
					new AddEnchantmentTask("YOG_300e", EntityType.CONTROLLER))
			}));
			cards.Add("YOG_300e", new CardDef(new Power { Enchant = new Enchant(new Effect(HsTag.NEXT_HEAL_DAMAGES, EffectOperator.SET, 1)) }));

			// [YOG_508] Twilight Torrent (2) — Choose a character. If it's friendly, restore 6 Health. If it's an enemy, deal 3 damage.
			// (live install-1003 7310: any character; the client plays its own Twilight Torrent missile)
			cards.Add("YOG_508", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is ICharacter target)) return;
					if (target.Controller == c) target.TakeHeal((IPlayable)s, V("YOG_508", "heal", 6));
					else Generic.DamageCharFunc((IPlayable)s, target, V("YOG_508", "damage", 3), true);
				})
			}));

			// [YOG_520] Soulburner Varia (3) — After a friendly Undead dies, deal 2 damage to the enemy hero and get a random Shadow
			// Priest spell.
			cards.Add("YOG_520", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEATH)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					Condition = new SelfCondition(IsUndead),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						Generic.DamageCharFunc((IPlayable)s, c.Opponent.Hero, V("YOG_520", "damage", 2), false);
						if (RandomCard(g, x => x.Type == CardType.SPELL && x.HasClass(CardClass.PRIEST) && x[SPELL_SCHOOL] == SHADOW) is Card spell)
							AddToHand(c, spell);
					})
				}
			}));

			// ================================================================ Rogue

			// [TTN_719] From the Scrapheap (2) — Get three 1/1 Magnetic Sparkbots with random Bonus Effects.
			// (a random Sparkbot of the card's client RelatedCards each time, repeats allowed: live vfx-decks 89069; the Sparkbots
			// TTN_719t..t7 and their magnetize enchantments TTN_719e..e7 work from the client data: keyword-only cards, Magnetic from
			// tag 2787 — no script)
			cards.Add("TTN_719", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TTN_719", "sparkbots", 3); i++)
						if (TitansRandomRelatedMinion(g, s) is Card sparkbot) AddToHand(c, sparkbot);
				})
			}));

			// [TTN_721] V-07-TR-0N Prime (6) — Titan. This minion's abilities repeat on another random friendly minion.
			// (the repeat is part of each ability: TitansC3ThenRepeat; its prefabs are named after the ability in other words, so each
			// ability places them itself — ownFx)
			cards.Add("TTN_721", TitansTitan());
			// [TTN_721t] Attach the Cannons! — Gain +2/+1. Deal 4 damage to a random enemy.
			cards.Add("TTN_721t", TitansAbility(new CustomTask((g, c, s, t, stack) =>
				TitansC3ThenRepeat(g, c, s, m =>
				{
					var titan = (IPlayable)s;
					Give(g, titan, m, "TTN_721te");
					List<ICharacter> enemies = Enemies(c);
					if (enemies.Count == 0) return;
					ICharacter enemy = enemies[g.Random.Next(enemies.Count)];
					TitansC3Fx(titan, "AttachCannons_Rocket_Missile", m, new[] { enemy.Id },
						() => Generic.DamageCharFunc(m, enemy, V("TTN_721t", "damage", 4), false));
				})), ownFx: true, notInHistory: true /* live: never ShowInHistory (titans-vfx 190857 / 174206 captures) */));
			cards.Add("TTN_721te", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("TTN_721t", "attack", 2)), Effects.Health_N(V("TTN_721t", "health", 1)))
			}));
			// [TTN_721t1] Full Power! — Gain +1/+2. Draw a card.
			cards.Add("TTN_721t1", TitansAbility(new CustomTask((g, c, s, t, stack) =>
				TitansC3ThenRepeat(g, c, s, m =>
				{
					var titan = (IPlayable)s;
					TitansC3Fx(titan, "FullPower_Impact", m, new[] { m.Id }, () => Give(g, titan, m, "TTN_721t1e"));
					for (int i = 0; i < V("TTN_721t1", "draw", 1); i++) Generic.Draw(c);
				})), ownFx: true, notInHistory: true /* live: never ShowInHistory (titans-vfx 190857 / 174206 captures) */));
			cards.Add("TTN_721t1e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("TTN_721t1", "attack", 1)), Effects.Health_N(V("TTN_721t1", "health", 2)))
			}));
			// [TTN_721t2] Maximize Defenses! — Gain +3 Health and Elusive.
			cards.Add("TTN_721t2", TitansAbility(new CustomTask((g, c, s, t, stack) =>
				TitansC3ThenRepeat(g, c, s, m =>
				{
					var titan = (IPlayable)s;
					TitansC3Fx(titan, "MaximizeDefences_Impact", m, new[] { m.Id }, () => Give(g, titan, m, "TTN_721t2e2"));
				})), ownFx: true, notInHistory: true /* live: never ShowInHistory (titans-vfx 190857 / 174206 captures) */));
			cards.Add("TTN_721t2e2", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Health_N(V("TTN_721t2", "health", 3)), Effects.CantBeTargetedBySpellsAndHeroPowers)
			}));

			// [TTN_726] Tar Slick (1) — Minions take double damage this turn. Deal 1 damage.
			// (live install session-20260929-183021 62375-63135: TTN_726e on the caster's PLAYER first, then the damage — a minion
			// target takes it doubled)
			cards.Add("TTN_726", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(
					new AddEnchantmentTask("TTN_726e", EntityType.CONTROLLER),
					new DamageTask(V("TTN_726", "damage", 1), EntityType.TARGET, true))
			}));
			// [TTN_726e] Oily — Minions take double damage until end of turn. (live: a PREDAMAGE trigger on every minion of both sides,
			// effect index 1, rewriting PREDAMAGE; at the turn's end its own trigger, effect index 0, removes it — rule book DH7)
			cards.Add("TTN_726e", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.PREDAMAGE)
					{
						TriggerSource = TriggerSource.ALL_MINIONS,
						EffectIndex = 1,
						FastExecution = true,
						SingleTask = Generic.PreDamageMultiply(V("TTN_726", "factor", 2))
					},
					new Trigger(TriggerType.TURN_END) { EffectIndex = 0, SingleTask = RemoveEnchantmentTask.Task })
			}));

			// [TTN_728] Pit Stop (2) — Discover a Mech from your deck. Give it +2/+1.
			// (Warsong Wrangler's way: the pick is drawn, then buffed; the client's DeckBuff effect is placed by the engine)
			cards.Add("TTN_728", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					Generic.DiscoverFromDeck(c, s, Generic.PickFromDeck(c, IsMech), (g2, c2, s2, mech) =>
					{
						if (mech.Zone?.Type == Zone.DECK) Generic.Draw(c2, mech);
						if (mech.Zone?.Type == Zone.HAND) Give(g2, s2, mech, "TTN_728e1");
					}))
			}));
			cards.Add("TTN_728e1", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("TTN_728", "attack", 2)), Effects.Health_N(V("TTN_728", "health", 1)))
			}));

			// [TTN_921] Coppertail Snoop (3) — Magnetic. Whenever this attacks, get a Coin.
			// (Magnetic from the client data; magnetized, its trigger goes with it onto the Mech — Generic.Magnetize)
			cards.Add("TTN_921", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) => AddToHand(c, Cards.FromId("GAME_005")))
				}
			}));

			// [TTN_922] Gear Shift (1) — Shuffle the two left-most cards in your hand into your deck. Draw 3 cards.
			// (live live-20261005/115158 1019: the two moves, left-most first, inside ReuseFX_Generic_HandToDeck_Super from the spell,
			// then the draws inside ReuseFX_Mech_BuffImpact_Gears_Sparks_DeckBuff_Super from nowhere; a backward move resets the cards)
			cards.Add("TTN_922", new CardDef(new Power
			{
				OwnFx = true,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var spell = (IPlayable)s;
					Generic.NamedFx(spell, "HandToDeck", null, () =>
					{
						for (int i = 0; i < V("TTN_922", "shuffle", 2) && c.HandZone.Count > 0; i++)
							Generic.MoveToDeck(c, s, c.HandZone[0]);
					});
					Generic.NamedFx(spell, "DeckBuff", null, () =>
					{
						for (int i = 0; i < V("TTN_922", "draw", 3); i++) Generic.Draw(c);
					});
				})
			}));

			// [TTN_923] SP-3Y3-D3R (3) — Magnetic. Stealth for 1 turn.
			// (TriggerLibrary.OneTurnStealth: live vfx-decks 91975 — at its owner's next turn, effect index 2, TriggerKeyword
			// STEALTH; magnetized, TTN_923e gives the Mech the one-turn Stealth from the client data)
			cards.Add("TTN_923", new CardDef(new Power { Trigger = TriggerLibrary.OneTurnStealth }));

			// [TTN_925] Kaja'mite Creation (2) — Battlecry: Discover a spell from another class that costs (3) or less.
			cards.Add("TTN_925", new CardDef(new Power
			{
				PowerTask = new DiscoverFromTask((g, c, s) =>
					OtherClassCards(g, c).Where(x => x.Type == CardType.SPELL && x.Cost <= V("TTN_925", "cost", 3)))
			}));

			// [YOG_410] Assembly Line (1) — Discover a Mech from another class.
			// (YOG_410e / YOG_410e1 are the early version's leftovers: unused by the current text)
			cards.Add("YOG_410", new CardDef(new Power
			{
				PowerTask = new DiscoverFromTask((g, c, s) =>
					OtherClassCards(g, c).Where(x => x.Type == CardType.MINION && x.IsRace(Race.MECHANICAL)))
			}));

			// [YOG_526] Tentacle Grip (2) — Deal 3 damage. Combo: Get a 1/1 Chaotic Tendril. (YOG_514: its own script, chunk 1)
			cards.Add("YOG_526", new CardDef(TargetAny, new Power
			{
				PowerTask = new DamageTask(V("YOG_526", "damage", 3), EntityType.TARGET, true),
				ComboTask = ComplexTask.Create(
					new DamageTask(V("YOG_526", "damage", 3), EntityType.TARGET, true),
					new CustomTask((g, c, s, t, stack) => AddToHand(c, Cards.FromId("YOG_514"))))
			}));

			// [YOG_527] Tiny Worldbreaker (4) — Battlecry: If you control another Mech, deal 4 damage.
			// (a chosen target, only while another Mech is in play — REQ_TARGET_IF_AVAILABLE_AND_FRIENDLY_RACE, South Coast Chieftain's
			// shared rule: the card glows then. DOUBT: unrecorded — targeted by the Battlecry template, the text says nothing random)
			cards.Add("YOG_527", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE_AND_FRIENDLY_RACE, (int)Race.MECHANICAL } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter target && c.BoardZone.Any(m => m.Id != s.Id && m.IsRace(Race.MECHANICAL)))
						Generic.DamageCharFunc((IPlayable)s, target, V("YOG_527", "damage", 4), false);
				})
			}));

			// ================================================================ Paladin

			// [TTN_852] Noble Minibot (2) — Magnetic. After this attacks, give a random minion in your hand +1/+1.
			// (live vfx-decks 181105: its TRIGGER block has effect index 1; magnetized, the trigger goes onto the Mech)
			cards.Add("TTN_852", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					EffectIndex = 1,
					SingleTask = BuffRandomInHand(p => p is Minion, "TTN_852e2")
				}
			}));
			cards.Add("TTN_852e2", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TTN_852", "stats", 1))) }));

			// [TTN_853] Judge Unworthy (4) — Set an enemy minion's Health to 1, then deal 1 damage to all enemies.
			// (Health set: its damage goes too — wiki 27.4: a damaged minion given Divine Shield gets Health 1 and loses the shield to
			// the 1; the damage one area hit inside the card's HolyNova shockwave)
			cards.Add("TTN_853", new CardDef(TargetEnemyMinion, new Power
			{
				OwnFx = true,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion target && !target.IsDead) Give(g, s, target, "TTN_853e");
					Generic.WholeFx((IPlayable)s, () =>
						new DamageTask(V("TTN_853", "damage", 1), EntityType.ENEMIES, true).Process(g, c, s, null));
				})
			}));
			cards.Add("TTN_853e", new CardDef(new Power { Enchant = new Enchant(Effects.SetMaxHealth(V("TTN_853", "health", 1))) }));

			// [TTN_854] Inventor's Aura (2) — Your Mechs cost (1) less. Lasts 2 turns.
			// (a Paladin Aura — PaladinAura: it sits in the hero's Secret slots, visible, like Secrets / Objectives / Wildpaw Cavern
			// (user 2026-10-07: "by secret slot i dont mean invsible i mean literally just the same slots as secrets and or auras and
			// effect of that kind like wildpaw cavern"); its duration from the card data, QUEST_PROGRESS_TOTAL 2, own turns; the cost
			// change shows as the client's TTN_854e)
			cards.Add("TTN_854", new CardDef(PaladinAura(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("TTN_854", "less", 1))) { Condition = new SelfCondition(IsMech), ShowAs = "TTN_854e" }
			})));

			// [TTN_856] Disciple of Amitus (5) — At the end of your turn, summon a 2/2 Earthen that gains +2/+2 for each other Earthen
			// you've summoned this game. (the gain is the Earthen Golem's own)
			cards.Add("TTN_856", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("TTN_900t"), s))
				}
			}));

			// [TTN_857] Tyr (6) — Battlecry: Resurrect a 2, 3, and 4-Attack minion from your class.
			// (one random dead friendly minion of each Attack — the card's — of your hero's class; none of one: that one is skipped)
			cards.Add("TTN_857", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (int attack in new[] { V("TTN_857", "attack 1", 2), V("TTN_857", "attack 2", 3), V("TTN_857", "attack 3", 4) })
						ResurrectOne(g, c, s, p => p.Card.ATK == attack && p.Card.HasClass(c.HeroClass));
				})
			}));

			// [TTN_858] Amitus, the Peacekeeper (7) — Titan. Taunt. Your minions can't take more than 2 damage at a time.
			// (the cap: a pre-damage trigger on your minions, Amitus included — rule book DH7c, Generic.PreDamageCap; its abilities
			// play the client's own sub-option effects (m_SubOptionEffectDefs, by the PLAY / POWER block's SubOption), so they send
			// no prefab of their own — ownFx)
			// HsServer (2026-10-08, live titans-vfx 190857): the cap's TRIGGER block is index 0 with ShowInHistory on EVERY hit of his
			// minions (41 empty for hits of 1-2, 13 lowering the damage); his abilities' blocks are 1-3 (TitanAbilityEffectIndexBase).
			cards.Add("TTN_858", TitansTitan(new Power
			{
				Trigger = Generic.PreDamageCapTrigger(TriggerSource.MINIONS, V("TTN_858", "cap", 2), effectIndex: 0, everyHit: true),
				TitanAbilityEffectIndexBase = 1
			}));
			// [TTN_858t1] Reinforced — Draw 2 minions. Set their Attack, Health, and Cost to 2. (deck picks: random)
			cards.Add("TTN_858t1", TitansAbility(new CustomTask((g, c, s, t, stack) =>
			{
				for (int i = 0; i < V("TTN_858t1", "minions", 2); i++)
					if (DrawOne(g, c, p => p is Minion) is IPlayable drawn)
					{
						Give(g, s, drawn, "TTN_858t1e");
						Give(g, s, drawn, "TTN_858t1e1");
					}
			}), ownFx: true, notInHistory: true /* live: never ShowInHistory (titans-vfx 190857 / 174206 captures) */));
			cards.Add("TTN_858t1e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("TTN_858t1", "stats", 2))) }));
			cards.Add("TTN_858t1e1", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(V("TTN_858t1", "cost", 2))) }));
			// [TTN_858t2] Empowered — Give your other minions +2/+2.
			cards.Add("TTN_858t2", TitansAbility(new CustomTask((g, c, s, t, stack) =>
				GiveAll(g, s, c.BoardZone.Where(m => m.Id != s.Id && !m.IsDead && !m.Untouchable), "TTN_858t2e1")), ownFx: true));
			cards.Add("TTN_858t2e1", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TTN_858t2", "stats", 2))) }));
			// [TTN_858t3] Pacified — Set the Attack and Health of all enemy minions to 2.
			cards.Add("TTN_858t3", TitansAbility(new CustomTask((g, c, s, t, stack) =>
				GiveAll(g, s, c.Opponent.BoardZone.Where(m => !m.IsDead && !m.Untouchable), "TTN_858t3e")), ownFx: true));
			cards.Add("TTN_858t3e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("TTN_858t3", "stats", 2))) }));

			// [TTN_900] Stoneheart King (4) — Deathrattle: Summon a 2/2 Earthen that gains +2/+2 for each other Earthen you've
			// summoned this game.
			cards.Add("TTN_900", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("TTN_900t"), s))
			}));
			// [TTN_900t] Earthen Golem (2) — After this is summoned, gain +2/+2 for each other Earthen you've summoned this game.
			// (wiki Earthen Golem: "not an ongoing effect ... the stat buff is applied once, after the Earthen Golem is summoned";
			// "triggers both when the card is played, as well as when it is summoned". Counted by the card itself in
			// Controller.SummonedThisGame, which takes it only after its after-summon triggers: the others. One TTN_900e2 "+2/+2"
			// each — the enchantment's text is fixed.)
			cards.Add("TTN_900t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me) || me.Zone?.Type != Zone.PLAY) return;
						int others = c.SummonedThisGame.Count(x => x.Id == me.Card.Id);
						for (int i = 0; i < others; i++) Give(g, me, me, "TTN_900e2");
					})
				}
			}));
			cards.Add("TTN_900e2", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TTN_900t", "stats", 2))) }));

			// [TTN_906] X-21 Repairbot (1) — Deathrattle: Return any Mechs Magnetized to this to your hand.
			// (the magnetize enchantments stay listed on the dead minion — GraveyardZone keeps Modular ones; each one's creator is
			// the magnetized minion, set aside: back to the hand, a backward move — its state resets. DOUBT: live may make new cards.)
			cards.Add("TTN_906", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is IPlayable bot) || bot.AppliedEnchantments == null) return;
					foreach (Enchantment e in bot.AppliedEnchantments.Where(x => x.Card.Modular).ToList())
						if (e.Creator is Minion part && IsMech(part) && part.Zone?.Type == Zone.SETASIDE && !c.HandZone.IsFull)
							Generic.ReturnToHand(c, part);
				})
			}));

			// [TTN_907] Astral Serpent (4) — At the end of your turn, if this didn't attack, draw 2 cards.
			cards.Add("TTN_907", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					Condition = new SelfCondition(p => p is Minion m && m.NumAttacksThisTurn == 0),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						for (int i = 0; i < V("TTN_907", "draw", 2); i++) Generic.Draw(c);
					})
				}
			}));

			// [YOG_509] Keeper's Strength (4) — Give a friendly minion +2/+2. Deal its Attack damage to all other minions.
			// (its Attack after the buff; no "$": no Spell Damage; one area hit)
			cards.Add("YOG_509", new CardDef(TargetFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion target)) return;
					Give(g, s, target, "YOG_509e");
					TitansC3DamageGroup(g, c, s,
						c.BoardZone.Concat(c.Opponent.BoardZone).Where(m => m != target && !m.IsDead && !m.Untouchable),
						target.AttackDamage, false);
				})
			}));
			cards.Add("YOG_509e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("YOG_509", "stats", 2))) }));

			// [YOG_510] Alarmed Securitybot (4) — Deathrattle: Draw another minion. Set its Attack, Health, and Cost to 5.
			cards.Add("YOG_510", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(DrawOne(g, c, p => p is Minion) is IPlayable drawn)) return;
					Give(g, s, drawn, "YOG_510e");
					Give(g, s, drawn, "YOG_510e2");
				})
			}));
			cards.Add("YOG_510e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("YOG_510", "stats", 5))) }));
			cards.Add("YOG_510e2", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(V("YOG_510", "cost", 5))) }));
		}
	}
}
