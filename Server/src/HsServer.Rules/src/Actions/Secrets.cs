using System.Linq;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>HsServer: can this player have <paramref name="secret"/> in play? Not with the Secret zone full (a Quest
		/// counts) or, for a Secret or Sidequest, a copy of it already up (Advanced rulebook § Limits, rule Z3; the same rule as a
		/// cast one, CastSpell). Other cards in the zone (a Paladin Aura, an Objective: Rhythm and Roots' ETC_387t) may repeat.</summary>
		public static bool CanHoldSecret(Controller c, Card secret) =>
			!c.SecretZone.IsFull && !((secret.IsSecret || secret[GameTag.SIDEQUEST] > 0) && c.SecretZone.Any(p => p.Card.Id == secret.Id)) &&
			!(secret.IsQuest && c.SecretZone.Quest != null); // (one Quest at a time: engine-rules.md "One Quest")

		/// <summary>
		/// HsServer (livecheck 2026-10-10, user "follow live"): a card that is two Quests (Reach Equilibrium TLC_817: "Quest: Cast 4 Holy
		/// spells. Reward: Life's Breath. Quest: Cast 4 Shadow spells. Reward: Death's Touch.") puts both up at once — the one exception
		/// to "one active Quest" (engine-rules.md). Live session-20261001-130515 13259: the played card goes SECRET → SETASIDE inside its
		/// PLAY block, then each half is a new entity (FULL_ENTITY TLC_817t, TLC_817t2, CREATOR = the card) in SECRET with its own
		/// progress and reward. The halves are the card's client RelatedCards that are Quests carrying the card's reward as their
		/// QUEST_HALF_OF (BACON_EVOLUTION_CARD_ID 2519) — only TLC_817 in the 36.6 card data (its tag 4310). False: not a split card.
		/// </summary>
		public static bool SplitQuest(Controller c, Spell quest)
		{
			int reward = quest.Card[GameTag.QUEST_REWARD_DATABASE_ID];
			if (reward <= 0 || !c.SecretZone.IsActiveQuest(quest)) return false;
			var halves = (Loader.ReprintCards.RelatedCardIds(quest.Card.Id) ?? System.Array.Empty<string>()).Select(Cards.TryFromId)
				.Where(h => h != null && h.IsQuest && h[Model.Zones.SecretZone.QUEST_HALF_OF] == reward).OrderBy(h => h.AssetId).ToList();
			if (halves.Count < 2) return false;
			c.SetasideZone.Add(c.SecretZone.Remove(quest));
			foreach (Card card in halves)
			{
				var half = (Spell)Entity.FromCard(c, card, new EntityData { [GameTag.CREATOR] = quest.Id, [GameTag.ZONE] = (int)Zone.SECRET }); // (live: FULL_ENTITY in SECRET)
				half.Power?.Trigger?.Activate(half);
				c.SecretZone.Add(half);
				half.IsExhausted = true;
			}
			return true;
		}

		/// <summary>HsServer (2026-10-08): the player's Secrets — only the cards with the Secret keyword. The Secret zone also holds
		/// Sidequests, Paladin Auras, Sigils and Objectives, which "a Secret" never means (soak 1100840: Horde Operative copied a
		/// non-Secret from it and threw).</summary>
		public static System.Collections.Generic.List<Spell> SecretsOf(Controller c) =>
			c.SecretZone.OfType<Spell>().Where(s => s.IsSecret).ToList();

		/// <summary>HsServer (2026-10-08): "If you control a Secret": a card with the Secret keyword is up (SecretsOf).</summary>
		public static bool ControlsSecret(Controller c) => c.SecretZone.Any(p => p is Spell s && s.IsSecret);

		/// <summary>HsServer: a Secret put straight into play (Mysterious Challenger, Kezan Mystic, Mad Scientist): an
		/// existing entity (from the deck, stolen) or a new one; its trigger armed; false when it can't be held.</summary>
		public static bool PutSecretIntoPlay(Controller c, Spell secret)
		{
			if (!CanHoldSecret(c, secret.Card)) return false;
			secret.Zone?.Remove(secret);
			secret.Power?.Trigger?.Activate(secret);
			c.SecretZone.Add(secret);
			if (c == c.Game.CurrentPlayer)
				secret.IsExhausted = true; // put up on its owner's turn: not active until the opponent's
			return true;
		}

		/// <summary>HsServer: a Secret destroyed: its trigger off, into its owner's graveyard. <paramref name="reveal"/>: also
		/// revealed (REVEALED, "Secret revealed" triggers) — the rule book names only Kezan Mystic's fallback ("a random Secret
		/// is destroyed and revealed", Advanced rulebook Z3); a plain destroy isn't (live: no REVEALED). With the
		/// <paramref name="source"/> that destroys it, as live (live-20261006-secrets-generators: Chief Inspector, Eater of
		/// Secrets, SI:7 Infiltrator): in the source's effect a META_DATA TARGET naming the Secret, then the Secret shown into
		/// the graveyard with LAST_AFFECTED_BY = the source and ZONE_POSITION 0 — one Secret after another, nothing between.</summary>
		public static void DestroySecret(Spell secret, bool reveal, IEntity source = null)
		{
			Game g = secret.Game;
			secret.ActivatedTrigger?.Remove();
			if (source != null && g.History)
				NameTargets(g, new[] { secret.Id });
			if (source != null)
				NoteAffected(secret, source.Id);
			if (reveal) secret[GameTag.REVEALED] = 1;
			secret.Controller.GraveyardZone.Add(secret.Zone.Remove(secret));
			secret[GameTag.ZONE_POSITION] = 0;
			if (reveal)
				g.RaiseEvent(() => g.TriggerManager.OnSecretRevealedTrigger(secret));
		}
	}
}
