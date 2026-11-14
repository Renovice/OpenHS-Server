// HsServer addition (not upstream SabberStone): Into the Emerald Dream (EMERALD_DREAM, set 1946) — Imbue. Tags: Enums/HsTag.cs
// 990550-990599.
using System;
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.SimpleTasks;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>Client player tag (unnamed, 3527): how many times the player Imbued (live: 1, 2, 3… on the player entity).</summary>
		public const GameTag IMBUE_COUNT = (GameTag)3527;

		/// <summary>Client card tag CARES_ABOUT_IMBUE_CARDS (3692): its RelatedCards name the class's Imbue Hero Power.</summary>
		public const GameTag CARES_ABOUT_IMBUE_CARDS = (GameTag)3692;

		private static Dictionary<CardClass, Card> _imbueHeroPowers;

		/// <summary>
		/// The class's Imbue Hero Power (data: the HERO_POWER card related to any card that cares about Imbue cards, by its class —
		/// one per class: Druid, Hunter, Mage, Paladin, Priest, Rogue, Shaman, Death Knight; wiki Imbue: "Only Death Knights, Druids,
		/// Hunters, Mages, Paladins, Priests, Rogues, and Shamans have special Imbue hero powers"), or null.
		/// </summary>
		public static Card ImbueHeroPower(CardClass cls)
		{
			if (_imbueHeroPowers == null)
			{
				var map = new Dictionary<CardClass, Card>();
				foreach (Card card in Cards.All.Where(x => x[CARES_ABOUT_IMBUE_CARDS] > 0))
					foreach (string id in Loader.ReprintCards.RelatedCardIds(card.Id) ?? new List<string>())
						if (Cards.TryFromId(id) is Card hp && hp.Type == CardType.HERO_POWER && hp.Class != CardClass.NEUTRAL && !map.ContainsKey(hp.Class))
							map[hp.Class] = hp;
				_imbueHeroPowers = map;
			}
			return _imbueHeroPowers.TryGetValue(cls, out Card found) ? found : null;
		}

		/// <summary>"If you've Imbued your Hero Power N times": the player's Imbues so far.</summary>
		public static int ImbueCount(Controller c) => c?[IMBUE_COUNT] ?? 0;

		/// <summary>
		/// "Imbue your Hero Power" (wiki Imbue: "The first time a player plays an Imbue card, it will replace their hero power with a
		/// special one specific to that class"; "When an Imbue card is played while the player already has the special hero power, it
		/// will instead increment all numbers on that hero power except the cost by 1"; "The first Imbue will always replace the
		/// player's hero power, even if they already replaced it with e.g. a hero card"; "If the player replaces their Imbue hero
		/// power with another hero power, and they Imbue again, they will continue from whatever numbers"; no special hero power for
		/// the class: "it will have no effect"). Live (darkmoon 74185-74253, 74915-74922; dk-classxp 39411): the effect
		/// EDRFX_Imbue_HeroPower_Buffimpact_Super on the current Hero Power around the player's count and, the first time, the new
		/// Hero Power; its TAG_SCRIPT_DATA_NUM_1 = the count.
		/// </summary>
		public static void Imbue(Controller c, IEntity source)
		{
			Game g = c.Game;
			if (!(ImbueHeroPower(c.HeroClass) is Card blessing)) return;
			string fx = source is IPlayable card ? Loader.SubSpells.FindNamed(card.Card.Id, "Imbue") : null;
			bool open = fx != null && g.History && c.Hero?.HeroPower != null &&
			            g.OpenSubSpell((IPlayable)source, fx, new[] { c.Hero.HeroPower.Id }, shownSource: Game.NoSubSpellSource);
			int count = c[IMBUE_COUNT] + 1;
			c[IMBUE_COUNT] = count;
			if (c.Hero?.HeroPower?.Card != blessing)
				new ReplaceHeroPower(blessing).Process(g, c, source, null);
			if (open) g.CloseSubSpells(source.Id);
			if (c.Hero?.HeroPower is HeroPower hp && hp.Card == blessing)
			{
				hp[GameTag.TAG_SCRIPT_DATA_NUM_1] = count;
				// the cards this Hero Power made carry its number (live 201822: every Emerald Portal in deck and hand)
				foreach (IPlayable made in c.DeckZone.GetAll().Concat(c.HandZone.GetAll()).Where(p => p[GameTag.CREATOR] == hp.Id).ToList())
					made[GameTag.TAG_SCRIPT_DATA_NUM_1] = count;
			}
		}

		/// <summary>
		/// "(Cast @ spells to …)" while in hand (Wish / Ritual / Light of the New Moon, the Violet Hold Frostshatter family; wiki: "This
		/// only counts spells cast while this is in the player's hand"): after each friendly spell its TAG_SCRIPT_DATA_NUM_1 counts down
		/// (live dk-classxp 1076: a TRIGGER block, effect index 3); at 0 it becomes its upgraded version (its single related card, else
		/// "&lt;id&gt;t") — live: effect index 2, the card's Transform effect around the change.
		/// </summary>
		public static Triggers.Trigger CastSpellsToUpgrade() => new Triggers.Trigger(TriggerType.AFTER_CAST)
		{
			TriggerActivation = TriggerActivation.HAND,
			TriggerSource = TriggerSource.FRIENDLY,
			EffectIndex = 3,
			SingleTask = new CustomTask((g, c, s, t, stack) =>
			{
				if (!(s is IPlayable card) || card.Zone?.Type != Zone.HAND || card[GameTag.TAG_SCRIPT_DATA_NUM_1] <= 0) return;
				int left = card[GameTag.TAG_SCRIPT_DATA_NUM_1] - 1;
				card[GameTag.TAG_SCRIPT_DATA_NUM_1] = left;
				if (left > 0) return;
				var related = Loader.ReprintCards.RelatedCardIds(card.Card.Id)?.Select(Cards.TryFromId).Where(x => x != null && x.Type == card.Card.Type).ToList();
				Card version = related?.Count == 1 ? related[0] : Cards.TryFromId(card.Card.Id + "t");
				if (version == null) return;
				// (2026-10-08, live dk-classxp 3928: the countdown's block ends first; the upgrade is its own index-2 block — no TARGET, the
				// Transform effect around the 1069 pulse and the CHANGE_ENTITY, HAS_ACTIVATE_POWER from the new card)
				g.TaskQueue.EnqueueTriggerBlock(new CustomTask((g2, c2, s2, t2, st2) =>
				{
					if (s2 is IPlayable held && held.Zone?.Type == Zone.HAND)
						NamedFx(held, "Transform", new[] { held.Id }, () => ChangeInHand(c2, held, version, 2, ownBlock: false));
				}), c, card, 2);
			})
		};

		/// <summary>
		/// Toreth ("Your Divine Shields take three hits to break"): the client draws a shield by its DIVINE_SHIELD value = the hits it
		/// takes (decompiled Actor.UpdateDivineShield: 1 → more = "EmpoweredDivineShield", back to 1 = "RevertDivineShield") and its
		/// cracks by DIVINE_SHIELD_DAMAGE. Each friendly shield shows the player's DIVINE_SHIELD_HITS while it is above 1; without it,
		/// back to 1 (wiki: "When Toreth leaves the battlefield, all active Divine Shields will remain regardless of how many hits
		/// they've taken" — the next hit breaks it; doubt whether live keeps the count).
		/// </summary>
		public static void SyncShieldHits(Game g)
		{
			foreach (Controller p in new[] { g.Player1, g.Player2 })
			{
				if (p == null) continue;
				int hits = p.ControllerAuraEffects[HsTag.DIVINE_SHIELD_HITS];
				foreach (Minion m in p.BoardZone.GetSpan())
				{
					if (m == null || !m.HasDivineShield) continue;
					int want = hits > 1 ? hits : 1;
					if (m[GameTag.DIVINE_SHIELD] != want) m[GameTag.DIVINE_SHIELD] = want;
					if (want == 1 && m[HsTag.SHIELD_HITS_TAKEN] != 0)
					{
						m[HsTag.SHIELD_HITS_TAKEN] = 0;
						m[HsTag.DIVINE_SHIELD_DAMAGE] = 0;
					}
				}
			}
		}

		/// <summary>
		/// "Then trigger it" (Wisprider: "Imbue your Hero Power, then trigger it"): the Hero Power's effect runs from it, in its own
		/// POWER block (live vfx 8770-8862: ShowInHistory, the Hero Power's own effects) — not a use: no Mana, no use counters, not
		/// exhausted. A Hero Power that takes a target takes a random valid one (doubt: unrecorded).
		/// </summary>
		public static void TriggerHeroPower(Controller c, IEntity source)
		{
			Game g = c.Game;
			if (!(c.Hero?.HeroPower is HeroPower hp) || !(hp.Card.Power?.PowerTask is Tasks.ISimpleTask task)) return;
			IPlayable target = null;
			if (hp.Card.MustHaveTargetToPlay || hp.Card.TargetingType != TargetingType.None)
			{
				var targets = hp.ValidPlayTargets.Cast<IPlayable>().ToList();
				if (targets.Count == 0 && hp.Card.MustHaveTargetToPlay) return;
				if (targets.Count > 0) target = targets[g.Random.Next(targets.Count)];
			}
			if (g.History)
			{
				var block = Kettle.PowerHistoryBuilder.BlockStart(BlockType.POWER, hp.Id, "", 0, target?.Id ?? 0);
				block.ShowInHistory = true;
				g.PowerHistory.Add(block);
			}
			g.TaskQueue.StartEvent();
			g.TaskQueue.Execute(task, c, hp, target, 0, 0, 0);
			g.ProcessTasks();
			g.TaskQueue.EndEvent();
			if (g.History) g.PowerHistory.Add(Kettle.PowerHistoryBuilder.BlockEnd());
		}

		/// <summary>
		/// A Smoldering card (client IMMOLATING: Smoldering Grove / Strength / Ascent): at the end of each of its owner's turns in hand
		/// its number goes up (TAG_SCRIPT_DATA_NUM_1 +1) while turns are left (TAG_SCRIPT_DATA_NUM_2 −1, IMMOLATESTAGE −1: the client's
		/// flames; at 0 its text says "Discards this turn!"); with none left it is discarded. (Timing: the end of the turn — patch 35.4.0
		/// "could complete all upgrades in a single turn while Drakkari Enchanter was active"; doubt, the wiki's tag says start.)
		/// </summary>
		/// <summary>Client IMMOLATESTAGE (2600): a Smoldering card's flames.</summary>
		public const GameTag IMMOLATESTAGE = (GameTag)2600;

		public static Triggers.Trigger SmolderingInHand() => new Triggers.Trigger(TriggerType.TURN_END)
		{
			TriggerActivation = TriggerActivation.HAND,
			SingleTask = new CustomTask((g, c, s, t, stack) =>
			{
				if (!(s is IPlayable card) || card.Zone?.Type != Zone.HAND) return;
				if (card[GameTag.TAG_SCRIPT_DATA_NUM_2] <= 0) { DiscardBlock.Invoke(c, card); return; }
				card[GameTag.TAG_SCRIPT_DATA_NUM_1] = card[GameTag.TAG_SCRIPT_DATA_NUM_1] + 1;
				card[GameTag.TAG_SCRIPT_DATA_NUM_2] = card[GameTag.TAG_SCRIPT_DATA_NUM_2] - 1;
				card[IMMOLATESTAGE] = Math.Max(0, card[IMMOLATESTAGE] - 1);
				if (card[GameTag.TAG_SCRIPT_DATA_NUM_2] <= 0) card[GameTag.USE_ALTERNATE_CARD_TEXT] = 1;
			})
		};

		/// <summary>
		/// "Friendly Beasts deal double damage" (Goldrinn): a pre-damage trigger on the card whose condition is the damage's SOURCE
		/// (<paramref name="sourceMatch"/>: a Beast on its side), doubling it; live (night 19508): a TRIGGER block (keyword AURA) with META CRITICAL_HIT on the damaged
		/// character, then the new PREDAMAGE. One per Goldrinn: several stack (wiki: "doubling the Beasts' attack each time").
		/// </summary>
		public static Triggers.Trigger DoublesDamageOf(string ownerCardId, Func<IPlayable, bool> sourceMatch) => new Triggers.Trigger(TriggerType.PREDAMAGE)
		{
			TriggerSource = TriggerSource.ALL,
			FastExecution = true,
			// (the damage's source: one of the owner's own kind, while a card of the owner's id is on that source's side)
			Condition = new Conditions.SelfCondition(p => p.Game.CurrentEventData is { EventNumber: > 0, EventSource: IPlayable from } && sourceMatch(from) &&
				from.Controller.BoardZone.GetAll().Any(m => m.Card.Id == ownerCardId)),
			SingleTask = new CustomTask((g, c, s, t, stack) =>
			{
				if (!(g.CurrentEventData?.EventSource is IPlayable from) || from.Controller != c || !sourceMatch(from)) return;
				if (g.History && t != null)
					g.PowerHistory.Add(new Kettle.PowerHistoryMetaData { Type = MetaDataType.CRITICAL_HIT, Data = 0, Info = new List<int> { t.Id } }); // CRITICAL_HIT
				PreDamageMultiply(2).Process(g, c, s, t, stack);
			})
		};
	}
}
