// HsServer addition (not upstream SabberStone): why an entity or a target can't be used, as the client's PlayError codes.
// Live AllOptions list every usable-looking entity (hand, heroes, hero powers, board) — the unusable ones with the first
// requirement they fail — and, for a targeted option, the characters it can't target with the reason
// (RESEARCH/captures/*/session-*.jsonl, decoded with `HsServer power`). The reasons come from the same checks that decide
// what is playable, and an entity or target is only given an error when the engine really offers no move for it.
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Model
{
	public static class PlayErrors
	{
		/// <summary>Client PlayError codes the engine's 2021 enum doesn't name.</summary>
		public const PlayReq REQ_NOT_EXHAUSTED_LOCATION = (PlayReq)130, REQ_LOCATION_TARGET = (PlayReq)131;

		/// <summary>Adds what live adds to the options: the play keyword, the unusable entities with their error, and the
		/// invalid targets of targeted options with theirs.</summary>
		public static void Add(Controller me, PowerAllOptions result)
		{
			Game game = me.Game;
			var played = new HashSet<int>();
			var forged = new HashSet<int>();
			foreach (PowerOption option in result.PowerOptionList)
			{
				if (option.MainOption == null) continue;
				if (!game.IdEntityDic.TryGetValue(option.MainOption.EntityId, out IPlayable source)) continue;
				// HsServer (TITANS): a Titan's abilities option (Actions/Titans.cs): each targeted ability's invalid targets
				if (option.MainOption.PowerKeyword == (int)Actions.Generic.TITAN_OPTION && source is Minion titan)
				{
					played.Add(source.Id);
					foreach (PowerSubOption sub in option.SubOptions)
						if (sub.PlayError == 0 && game.IdEntityDic.TryGetValue(sub.EntityId, out IPlayable ability))
						{
							AddTargetErrors(me, sub, ability);
							// the Titan itself: REQ_NONSELF_TARGET (live Shape the Stars)
							if (sub.Targets != null && !sub.Targets.Contains(titan.Id) && sub.TargetErrors.All(t => t.Id != titan.Id) &&
							    ability.Card.PlayRequirements?.ContainsKey(PlayReq.REQ_NONSELF_TARGET) == true)
								sub.TargetErrors.Add((titan.Id, (int)PlayReq.REQ_NONSELF_TARGET, 0));
						}
					continue;
				}
				if (option.MainOption.PowerKeyword == 0)
				{
					played.Add(source.Id);
					option.MainOption.PowerKeyword = Keyword(source);
				}
				else
				{
					if (option.MainOption.PowerKeyword == (int)Actions.Generic.FORGE) forged.Add(source.Id);
					continue; // a trade or forge has no targets
				}
				AddTargetErrors(me, option.MainOption, source);
				foreach (PowerSubOption sub in option.SubOptions)
					if (game.IdEntityDic.TryGetValue(sub.EntityId, out IPlayable subSource))
						AddTargetErrors(me, sub, subSource);
			}

			foreach (IPlayable entity in Candidates(me))
			{
				if (played.Contains(entity.Id)) continue;
				PlayReq error = OptionError(me, entity);
				if (error == 0) continue; // no reason we can name: leave it out rather than guess
				result.PowerOptionList.Add(new PowerOption
				{
					OptionType = OptionType.POWER,
					MainOption = new PowerSubOption { EntityId = entity.Id, PlayError = (int)error, PlayErrorParam = Param(entity.Card, error), PowerKeyword = Keyword(entity) }
				});
			}

			// HsServer (TITANS): a Titan whose abilities are all used gets, next to its attack, an option saying its abilities
			// are spent (live AllOptions: {Titan, REQ_SUBCARD_IS_PLAYABLE})
			if (me == game.CurrentPlayer)
				foreach (Minion titan in me.BoardZone.GetAll())
					if (Actions.Generic.IsTitan(titan) && !Actions.Generic.TitanHasAbilitiesLeft(titan))
						result.PowerOptionList.Add(new PowerOption
						{
							OptionType = OptionType.POWER,
							MainOption = new PowerSubOption { EntityId = titan.Id, PlayError = (int)PlayReq.REQ_SUBCARD_IS_PLAYABLE }
						});

			// live (live-20261002-forge-drink): a Forge card it can't forge yet gets its own unusable option, after its play
			// option, with the FORGE keyword (REQ_ENOUGH_MANA when the 2 mana are missing)
			if (me == game.CurrentPlayer)
				foreach (IPlayable card in me.HandZone.GetAll())
					if (!forged.Contains(card.Id) && Actions.Generic.ForgedCard(card) != null && me.ManaToSpend < Actions.Generic.ForgeCost(card))
						result.PowerOptionList.Add(new PowerOption
						{
							OptionType = OptionType.POWER,
							MainOption = new PowerSubOption { EntityId = card.Id, PlayError = (int)PlayReq.REQ_ENOUGH_MANA, PowerKeyword = (int)Actions.Generic.FORGE }
						});
		}

		/// <summary>Live errorParam: the card's own value for the failed requirement (REQ_TARGET_MIN_ATTACK 5...), else 0.</summary>
		private static int Param(Card card, PlayReq error) =>
			card.PlayRequirements != null && card.PlayRequirements.TryGetValue(error, out int value) ? value : 0;

		/// <summary>Heroes sit outside the engine's zones but are in play.</summary>
		private static bool InPlay(IPlayable p) => p is Hero || p.Zone?.Type == Zone.PLAY;

		/// <summary>Live marks a Battlecry card's play option with BATTLECRY (a trade option has TRADEABLE instead); HsServer
		/// (WILD_WEST): a card without Battlecry whose Quickdraw is on with QUICKDRAW (live-20261005 140614 81935: Sunspot Dragon's
		/// option keyword=2905; Horseshoe Slinger, with Battlecry, 218).</summary>
		private static int Keyword(IPlayable p) =>
			p.Zone?.Type != Zone.HAND ? 0
			: p.Card[GameTag.BATTLECRY] > 0 ? (int)GameTag.BATTLECRY
			: p.Card[HsTag.QUICKDRAW] > 0 && Actions.Generic.IsQuickdrawNow(p) ? (int)HsTag.QUICKDRAW : 0;

		/// <summary>Live's order: hand, own hero, hero power, own board; then the opponent's hero, hero power and board.</summary>
		private static IEnumerable<IPlayable> Candidates(Controller me)
		{
			foreach (IPlayable p in me.HandZone.GetAll()) yield return p;
			foreach (Controller c in new[] { me, me.Opponent })
			{
				yield return c.Hero;
				if (c.Hero.HeroPower != null) yield return c.Hero.HeroPower;
				foreach (Minion m in c.BoardZone.GetAll()) yield return m;
			}
		}

		/// <summary>The first requirement <paramref name="p"/> fails for <paramref name="me"/> (0 if none is known).</summary>
		public static PlayReq OptionError(Controller me, IPlayable p)
		{
			if (p.Controller != me || me != me.Game.CurrentPlayer) return PlayReq.REQ_YOUR_TURN;
			switch (p)
			{
				case HeroPower power:
					// live (Mind Spike-type lock): the Hero Power can't be used at all
					if (me[GameTag.HERO_POWER_DISABLED] > 0 || me.ControllerAuraEffects[GameTag.HERO_POWER_DISABLED] > 0)
						return PlayReq.REQ_NOT_DISABLED_HERO_POWER;
					if (power.IsExhausted) return PlayReq.REQ_NOT_EXHAUSTED_HERO_POWER;
					if (me.ManaToSpend < power.Cost) return PlayReq.REQ_ENOUGH_MANA;
					return CardError(me, p);
				case Location location:
					if (location.OnCooldown) return REQ_NOT_EXHAUSTED_LOCATION;
					return 0;
				// HsServer (TITANS): a Titan with abilities left that can't use one now (live 159657: it already acted —
				// REQ_SUBCARD_IS_PLAYABLE; also when no ability has a valid target)
				case Minion titan when InPlay(p) && Actions.Generic.TitanHasAbilitiesLeft(titan):
					if (titan.IsFrozen) return PlayReq.REQ_ATTACKER_NOT_FROZEN;
					if (titan.CantAttack) return PlayReq.REQ_ATTACKER_CAN_ATTACK;
					return PlayReq.REQ_SUBCARD_IS_PLAYABLE;
				case Character character when InPlay(p):
					return AttackerError(character);
			}
			if (p.Zone?.Type != Zone.HAND) return 0;
			if (Playable.IsPlayRestricted(p) || p.Card.HideStat) return PlayReq.REQ_LITERALLY_UNPLAYABLE;
			PlayReq cost = Playable.CostError(me, p); // Mana or what it costs instead (the engine's IsPlayable uses the same check)
			if (cost != 0) return cost;
			// (HsServer 2026-10-08: a Magnetic minion can still go onto a Mech — wiki Magnetic.txt:7)
			if ((p is Minion) && me.BoardZone.IsFull && !Actions.Generic.MagneticOntoFullBoard(me, p.Card)) return PlayReq.REQ_MINION_CAP;
			if (p is Spell s && (s.IsSecret || s.IsQuest || s[GameTag.SIDEQUEST] > 0))
			{
				if (s.IsQuest && me.SecretZone.Quest != null) return PlayReq.REQ_MAX_QUESTS;
				if (me.SecretZone.IsFull) return PlayReq.REQ_SECRET_ZONE_CAP;
				if ((s.IsSecret || s[GameTag.SIDEQUEST] > 0) && me.SecretZone.Any(x => x.Card.Id == s.Card.Id)) return PlayReq.REQ_UNIQUE_SECRET_OR_QUEST; // HsServer: Sidequests too
			}
			PlayReq card = CardError(me, p);
			if (card != 0) return card;
			if (p.ChooseOne && p.ChooseOnePlayables != null && !p.ChooseOnePlayables.Any(o => o != null && CardError(me, o) == 0))
				return PlayReq.REQ_SUBCARD_IS_PLAYABLE;
			return 0;
		}

		private static PlayReq CardError(Controller me, IPlayable p)
		{
			PlayReq failed = p.Card.FailedPlayCheck(me);
			if (failed != 0) return failed;
			if (p.Card.MustHaveTargetToPlay && !p.HasAnyValidPlayTargets && !Actions.Generic.DropsTarget(p)) return PlayReq.REQ_TARGET_TO_PLAY;
			return 0;
		}

		private static PlayReq AttackerError(Character c)
		{
			if (c.IsFrozen) return PlayReq.REQ_ATTACKER_NOT_FROZEN;
			if (c.AttackDamage <= 0) return PlayReq.REQ_ATTACK_GREATER_THAN_0;
			if (c.CantAttack) return PlayReq.REQ_ATTACKER_CAN_ATTACK;
			if (c is Minion m && m.IsExhausted && m.NumAttacksThisTurn == 0) return PlayReq.REQ_NOT_MINION_JUST_PLAYED;
			if (c.IsExhausted) return PlayReq.REQ_NOT_EXHAUSTED_ACTIVATE;
			return 0;
		}

		/// <summary>Adds each character in play the option can't target, with the reason (live lists them after the valid
		/// ones). Options without a target list (or targeting cards in hand) get none.</summary>
		private static void AddTargetErrors(Controller me, PowerSubOption option, IPlayable source)
		{
			if (option.Targets == null || option.Targets.Count == 0) return;
			Game game = me.Game;
			if (option.Targets.Any(t => !game.IdEntityDic.TryGetValue(t, out IPlayable e) || !InPlay(e))) return;
			bool attack = source is Character && InPlay(source);
			if (source is Location) return;
			var valid = new HashSet<int>(option.Targets);
			var candidates = new List<Character> { me.Hero, me.Opponent.Hero };
			candidates.AddRange(me.BoardZone.GetAll());
			candidates.AddRange(me.Opponent.BoardZone.GetAll());
			foreach (Character target in candidates)
			{
				if (valid.Contains(target.Id)) continue;
				PlayReq error = attack ? AttackTargetError((Character)source, target) : TargetError(source, target);
				if (error != 0)
					option.TargetErrors.Add((target.Id, (int)error, Param(source.Card, error)));
			}
		}

		/// <summary>Why <paramref name="source"/> (a card or hero power being played) can't target <paramref name="target"/>.</summary>
		public static PlayReq TargetError(IPlayable source, Character target)
		{
			Card card = source.Card;
			bool enemy = target.Controller != source.Controller;
			if (target is Location)
				return card.PlayRequirements?.ContainsKey(REQ_LOCATION_TARGET) == true ? 0 : PlayReq.REQ_HERO_OR_MINION_TARGET;
			if (card.PlayRequirements?.ContainsKey(REQ_LOCATION_TARGET) == true) return REQ_LOCATION_TARGET;
			if (target is Minion { Untouchable: true }) return 0; // Dormant: not a target at all
			switch (card.TargetingType)
			{
				case TargetingType.AllMinions:
				case TargetingType.FriendlyMinions:
				case TargetingType.EnemyMinions:
					if (target is Hero) return PlayReq.REQ_MINION_TARGET;
					break;
				case TargetingType.Heroes:
					if (target is Minion) return PlayReq.REQ_HERO_TARGET;
					break;
			}
			switch (card.TargetingType)
			{
				case TargetingType.FriendlyCharacters:
				case TargetingType.FriendlyMinions:
					if (enemy) return PlayReq.REQ_FRIENDLY_TARGET;
					break;
				case TargetingType.EnemyCharacters:
				case TargetingType.EnemyMinions:
					if (!enemy) return PlayReq.REQ_ENEMY_TARGET;
					break;
			}
			if (enemy && target.HasStealth) return PlayReq.REQ_NONSTEALTH_ENEMY_TARGET;
			if (enemy && target.IsImmune) return PlayReq.REQ_ENEMY_TARGET_NOT_IMMUNE;
			if (source is Spell && target.CantBeTargetedBySpells) return PlayReq.REQ_CAN_BE_TARGETED_BY_SPELLS;
			if (source is HeroPower && target.CantBeTargetedByHeroPowers) return PlayReq.REQ_CAN_BE_TARGETED_BY_HERO_POWERS;
			PlayReq failed = card.FailedTargetCheck(target);
			if (failed == 0 && card.Power?.TargetIf is { } own && !own.Check(source, target)) return own.Error; // HsServer (ISLAND_VACATION)
			return failed;
		}

		/// <summary>Why <paramref name="attacker"/> can't attack <paramref name="target"/>.</summary>
		public static PlayReq AttackTargetError(Character attacker, Character target)
		{
			if (target.Controller == attacker.Controller) return PlayReq.REQ_ENEMY_TARGET;
			if (target is Location || target is Minion { Untouchable: true }) return 0;
			if (target.HasStealth) return PlayReq.REQ_NONSTEALTH_ENEMY_TARGET;
			if (target is Hero)
			{
				if (attacker is Minion m && m.AttackableByRush) return PlayReq.REQ_MINION_TARGET; // live (Soldier of the Infinite)
				if (attacker.CantAttackHeroes) return PlayReq.REQ_MINION_TARGET;
			}
			if (attacker[GameTag.IGNORE_TAUNT] == 0 && !target.HasTaunt &&
			    target.Controller.BoardZone.GetAll().Any(m => m.HasTaunt && !m.HasStealth && !m.Untouchable))
				return PlayReq.REQ_TARGET_TAUNTER;
			return 0;
		}
	}
}
