// HsServer addition (not upstream SabberStone): Caverns of Time (WONDERS, client set 1898, 2023) — the set's shared card
// pieces. The engine rules are in Actions/Wonders.cs (C'Thun ritual, in-hand shift, Go Dormant on death, Cenarion Hold's
// Choose One, Disco's Secrets, next Battlecry twice, Poisoned Blade, can't be Overloaded, spells have Lifesteal, opponent-class
// cards, Spare Parts, deck copies); the "counts as a copy" reprints (client tag 858: 54 of the set's cards play their updated
// Wild original's script) are a loader rule (Loader/ReprintCards.cs CountsAsCopyOf). Every number goes through CardValues (V).
// Card scripts: the chunk files add their cards through Wonders(cards).
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
		// ---------------------------------------------------------------- shared pieces of the set

		/// <summary>
		/// "Battlecry and Inspire: X" (Spawn of Shadows AT_012 / WON_058, Confessor Paletress AT_018 / WON_063, Thunder Bluff Valiant
		/// AT_049 / WON_085): the same task as the card's Battlecry (a real Battlecry: wiki Thunder Bluff Valiant 27.4.0 "Fixed a bug
		/// where Thunder Bluff Valiant's new Battlecry effect was not treated as a Battlecry" — Brann doubles it) and as its Inspire
		/// trigger (rules B9: Inspire Phase, after the Hero Power; TriggerKeyword INSPIRE). The Inspire is the card's SECOND effect:
		/// effect index 1 (live Thunder Bluff Valiant, live-20261007-zilliax-etc-remix 112381; Inspire-only cards use 0).
		/// </summary>
		private static Power WondersBattlecryAndInspire(ISimpleTask effect) => new Power
		{
			PowerTask = effect,
			Trigger = new Trigger(TriggerType.INSPIRE) { SingleTask = effect, EffectIndex = 1 }
		};

		/// <summary>
		/// The Imposters — "Each turn this is in your hand, transform it into a random N-Cost minion that gains &lt;keyword&gt;"
		/// (Durnholde Imposter WON_026, Black Morass Imposter WON_039, Mount Hyjal Imposter WON_077). Registers the three cards:
		/// <list type="bullet">
		/// <item><paramref name="id"/>: a hand trigger at the start of its owner's turn (Game.MainStartTriggers, before the draw).</item>
		/// <item>"&lt;id&gt;e" Shifting (DEFINING_ENCHANTMENT = the Imposter: the shared shift's TRANSFORMED_FROM_CARD, which the client's
		/// tooltip reads for "Shifting adds Poisonous / Spell Damage / Stealth"): SHIFTING_MINION (live Bandersmosh, NIGHT 13710),
		/// detached when played, and the same trigger so it keeps shifting.</item>
		/// <item>"&lt;id&gt;e2": the keyword the minion gains, kept when played — from its client text (WON_026e2 "Poisonous", WON_039e2
		/// "Spell Damage +1", WON_077e2 "Stealth": no tag on it).</item>
		/// </list>
		/// N = the Imposter's own Cost (V(id, "cost")); the pool is RandomMinionOfCost (the board pool: wiki Mount Hyjal Imposter
		/// lists "Summoned 4-Cost minions"). Rule: Generic.ShiftInHand (Shifter Zerus' rule book entry).
		/// </summary>
		private static void WondersImposter(IDictionary<string, CardDef> cards, string id)
		{
			ISimpleTask shift = new CustomTask((g, c, s, t, stack) =>
				Generic.ShiftInHand(s, me => RandomMinionOfCost(g, V(id, "cost", Cards.FromId(id).Cost)),
					Cards.FromId(id + "e"), Cards.FromId(id + "e2")));
			cards.Add(id, new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START) { TriggerActivation = TriggerActivation.HAND, SingleTask = shift }
			}));
			cards.Add(id + "e", new CardDef(new Power
			{
				Enchant = new Enchant(GameTag.SHIFTING_MINION, EffectOperator.SET, 1) { RemoveWhenPlayed = true },
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = shift }
			}));
			cards.Add(id + "e2", new CardDef(new Power { Enchant = WondersKeywordEnchant(id, id + "e2") }));
		}

		/// <summary>The keywords an enchantment's client text gives ("&lt;b&gt;Stealth&lt;/b&gt;.", "&lt;b&gt;Spell Damage +1&lt;/b&gt;."),
		/// as SET 1 (Spell Damage: ADD its number, V(<paramref name="card"/>, "spell damage")). Read when the card is defined.</summary>
		private static Enchant WondersKeywordEnchant(string card, string enchantmentId)
		{
			string text = Regex.Replace(ReprintCards.ClientText(enchantmentId) ?? "", "<[^>]+>", "");
			var effects = new List<IEffect>();
			if (Regex.Match(text, @"Spell Damage \+(\d+)") is { Success: true } sd)
				effects.Add(new Effect(GameTag.SPELLPOWER, EffectOperator.ADD, V(card, "spell damage", int.Parse(sd.Groups[1].Value))));
			foreach ((string word, GameTag tag) in new[]
			         {
				         ("Stealth", GameTag.STEALTH), ("Poisonous", GameTag.POISONOUS), ("Taunt", GameTag.TAUNT), ("Divine Shield", GameTag.DIVINE_SHIELD),
				         ("Rush", GameTag.RUSH), ("Charge", GameTag.CHARGE), ("Windfury", GameTag.WINDFURY), ("Lifesteal", GameTag.LIFESTEAL),
				         ("Reborn", GameTag.REBORN),
			         })
				if (Regex.IsMatch(text, @"(^|[^\w])" + word + @"([^\w]|$)"))
					effects.Add(new Effect(tag, EffectOperator.SET, 1));
			return new Enchant(effects.ToArray());
		}

		/// <summary>"Whenever your C'Thun gains Attack or Health, this does too (wherever it is)" (Eyestalk of C'Thun): the shared
		/// ritual gives it the same permanent gain (Power.FollowsCthunGains; Generic.CthunRitual).</summary>
		private static Power WondersFollowsCthun => new Power { FollowsCthunGains = true };

		// ---------------------------------------------------------------- registration

		/// <summary>The set's card scripts (the chunk files' methods are called here).</summary>
		private static void Wonders(IDictionary<string, CardDef> cards)
		{
			WondersChunk1(cards);
			WondersChunk2(cards);
			WondersChunk3(cards);
			WondersChunk4(cards);
		}
	}
}
