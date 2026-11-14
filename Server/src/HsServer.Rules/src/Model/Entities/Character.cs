#region copyright
// SabberStone, Hearthstone Simulator in C# .NET Core
// Copyright (C) 2017-2019 SabberStone Team, darkfriend77 & rnilva
//
// SabberStone is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as
// published by the Free Software Foundation, either version 3 of the
// License.
// SabberStone is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
#endregion
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SabberStoneCore.Enums;
using SabberStoneCore.Tasks.SimpleTasks;
using SabberStoneCore.Kettle;
using SabberStoneCore.Tasks;

namespace SabberStoneCore.Model.Entities
{
	/// <summary>
	/// Represents an entity in game which behaves as an actor. 
	/// The actions it can perform, as well as actions it undergoes,
	/// are defined by this interface.
	/// 
	/// The properties defined by this type are non complex, they have a
	/// very superficial meaning.
	/// </summary>
	public partial interface ICharacter : IPlayable
	{
		/// <summary>
		/// Indicates if this character can continue performing actions.
		/// </summary>
		bool IsDead { get; }

		/// <summary>
		/// Indicates if this character has the possibility to attack.
		/// </summary>
		bool CanAttack { get; }

		/// <summary>
		/// Contains a sequence of valid targets, also Characters, which
		/// can be attacked.
		/// </summary>
		IEnumerable<ICharacter> ValidAttackTargets { get; }

		/// <summary>
		/// Indicates if the provided target is attackable.
		/// 
		/// A quick solution would be to test if there are no taunt minions
		/// present and the target is not immune.
		/// </summary>
		/// <param name="target"></param>
		/// <returns></returns>
		bool IsValidAttackTarget(ICharacter target);

		/// <summary>
		/// This character takes damage from a certain other entity.
		/// </summary>
		/// <param name="source"></param>
		/// <param name="damage"></param>
		/// <returns></returns>
		int TakeDamage(IPlayable source, int damage);

		/// <summary>
		/// This character gets healed by a certain other entity.
		/// </summary>
		/// <param name="source"></param>
		/// <param name="heal"></param>
		void TakeHeal(IPlayable source, int heal);

		/// <summary>
		/// This character receives a heal for it's maximum health.
		/// </summary>
		/// <param name="source"></param>
		void TakeFullHeal(IPlayable source);

		/// <summary>
		/// This character receives a specified amount of armor.
		/// </summary>
		/// <param name="source"></param>
		/// <param name="armor"></param>
		void GainArmor(IPlayable source, int armor);

		event TriggerManager.TriggerHandler AfterAttackTrigger;
		void OnAfterAttackTrigger();

		bool HasAnyValidAttackTargets { get; }
	}

	/// <summary>
	/// Base implementation of ICharacter.
	/// <seealso cref="ICharacter"/>
	/// <seealso cref="Playable"/>
	/// </summary>
	public abstract partial class Character : Playable, ICharacter
	{
		public event TriggerManager.TriggerHandler PreDamageTrigger;
		public event TriggerManager.TriggerHandler TakeDamageTrigger;
		public event TriggerManager.TriggerHandler AfterAttackTrigger;

		/// <summary>
		/// Build a new character from the provided data.
		/// </summary>
		/// <param name="controller">Owner of the character; not specifically limited to players.</param>
		/// <param name="card">The card which this character embodies.</param>
		/// <param name="tags">Properties of this entity.</param>
		/// <param name="id">Integral id of this entity. </param>
		protected Character(in Controller controller, in Card card, in IDictionary<GameTag, int> tags, in int id)
			: base(in controller, in card, in tags, in id)
		{

		}

		/// <summary>
		/// A copy constructor. This constructor is only used to the inherited copy constructors.
		/// </summary>
		/// <param name="controller">The target <see cref="T:SabberStoneCore.Model.Entities.Controller" /> instance.</param>
		/// <param name="character">The source <see cref="T:SabberStoneCore.Model.Entities.Character`1" />.</param>
		protected Character(in Controller controller, in Character character) : base(in controller, character)
		{
			character.CopyInternalAttributes(this);
		}

		/// <summary>
		/// Character is dead or destroyed.
		/// </summary>
		public bool IsDead => Health <= 0 || ToBeDestroyed;

		/// <summary>HsServer (EMERALD_DREAM): client IMMUNE_TO_FIRE_SPELLS (Fyrakk the Blazing).</summary>
		public const GameTag IMMUNE_TO_FIRE_SPELLS = (GameTag)4066;

		public override int this[GameTag t]
		{
			get
			{
				switch (t)
				{
					case GameTag.ATK:
						return AttackDamage;
					case GameTag.HEALTH:
						return BaseHealth;
					case GameTag.DAMAGE:
						return Damage;
					case GameTag.STEALTH:
						return HasStealth ? 1 : 0;
					case GameTag.IMMUNE:
						return IsImmune ? 1 : 0;
					case GameTag.TAUNT:
						return HasTaunt ? 1 : 0;
					case GameTag.CANT_BE_TARGETED_BY_SPELLS:
					case GameTag.CANT_BE_TARGETED_BY_HERO_POWERS:
						return CantBeTargetedBySpells ? 1 : 0;
					case GameTag.NUM_ATTACKS_THIS_TURN:
						return _numAttackThisTurn;
					default:
						return base[t];
				}
			}
			set
			{
				switch (t)
				{
					case GameTag.ATK:
						AttackDamage = value;
						return;
					case GameTag.HEALTH:
						BaseHealth = value;
						return;
					case GameTag.DAMAGE:
						Damage = value;
						return;
					case GameTag.STEALTH:
						HasStealth = value > 0;
						return;
					case GameTag.IMMUNE:
						IsImmune = value > 0;
						return;
					case GameTag.TAUNT:
						HasTaunt = value > 0;
						return;
					case GameTag.CANT_BE_TARGETED_BY_SPELLS:
					case GameTag.CANT_BE_TARGETED_BY_HERO_POWERS:
						CantBeTargetedBySpells = value > 0;
						return;
					case GameTag.NUM_ATTACKS_THIS_TURN:
						_numAttackThisTurn = value;
						return;
					default:
						base[t] = value;
						return;
				}
			}
		}

		/// <summary>
		/// Character can attack.
		/// </summary>
		public virtual bool CanAttack => !IsExhausted && !IsFrozen && HasAnyValidAttackTargets && !CantAttack;

		/// <summary>
		/// Indicates if the provided character can be attacked by this character.
		/// </summary>
		/// <param name="target"></param>
		/// <returns></returns>
		public virtual bool IsValidAttackTarget(ICharacter target)
		{
			// got target but isn't contained in valid targets
			if (!ValidAttackTargets.Contains(target))
			{
				Game.Log(LogLevel.WARNING, BlockType.ACTION, "Character", !Game.Logging? "":$"{this} has an invalid target {target}.");
				return false;
			}

			if (target is Hero)
			{
				if (CantAttackHeroes || (this is Minion m && m.AttackableByRush))
				{
					Game.Log(LogLevel.WARNING, BlockType.ACTION, "Character", !Game.Logging ? "" : $"Can't attack Heroes!");
					return false;
				}
			}

			return true;
		}

		/// <summary>
		/// Returns a sequence of characters which are attackable.
		/// </summary>
		public IEnumerable<ICharacter> ValidAttackTargets
		{
			get
			{
				bool tauntFlag = false;
				var allTargets = new List<ICharacter>(4);
				var allTargetsTaunt = new List<ICharacter>(2);
				foreach (Minion minion in Controller.Opponent.BoardZone.GetAll())
				{
					// HsServer (2026-10-08): an Immune minion can't be attacked and its Taunt is suppressed, as Stealth (wiki x_Attack.txt:49:
					// "If all enemy Taunt minions are Immune or in Stealth, the Taunt ability is suppressed"; the options already skip it)
					if (!minion.HasStealth && !minion.IsImmune)
					{
						if (minion.HasTaunt && this[GameTag.IGNORE_TAUNT] == 0) // HsServer: Kayn Sunfury
						{
							allTargetsTaunt.Add(minion);
							tauntFlag = true;
							continue;
						}
						if (!tauntFlag)
							allTargets.Add(minion);
					}
				}
				if (tauntFlag)
					return allTargetsTaunt;

				Hero opHero = Controller.Opponent.Hero;

				if (!(this is Minion m && m.AttackableByRush) && !CantAttackHeroes && !opHero.IsImmune && !opHero.HasStealth)
					allTargets.Add(opHero);

				return allTargets;
			}
		}

		public bool HasAnyValidAttackTargets
		{
			get
			{
				var span = Controller.Opponent.BoardZone.GetSpan();
				for (int i = 0; i < span.Length; i++)
				{
					if (!(span[i].HasStealth || span[i].IsImmune))
						return true;
				}

				bool isOpHeroValidPlayTarget =
					!Controller.Opponent.Hero.HasStealth && !Controller.Opponent.Hero.IsImmune;

				// (HsServer 2026-10-08: a Rush-only minion can't attack the hero — as ValidAttackTargets; the condition was inverted)
				if (isOpHeroValidPlayTarget && !CantAttackHeroes && !(this is Minion m && m.AttackableByRush))
					return true; // Op Hero is a valid attack target

				return false;
			}
		}

		/// <summary>HsServer: <paramref name="source"/>'s Lifesteal for <paramref name="amount"/> damage — its owner's hero healed (or, with
		/// Il'gynoth, the enemy hero damaged), in a TRIGGER block on the source with TriggerKeyword LIFESTEAL (live: the client's visual).</summary>
		internal static void Lifesteal(Game game, IPlayable source, int amount)
		{
			if (game.History)
			{
				PowerHistoryBlockStart lifesteal = PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, source.Id, "", -1, 0); // live: no EffectCardId
				lifesteal.TriggerKeyword = (int)GameTag.LIFESTEAL;
				game.PowerHistory.Add(lifesteal);
			}
			game.Log(LogLevel.VERBOSE, BlockType.ATTACK, "TakeDamage", !game.Logging ? "" : $"lifesteal source {source} has damaged for {amount}.");
			// HsServer: Il'gynoth — "Your Lifesteal damages the enemy hero instead of healing you"
			if (source.Controller.ControllerAuraEffects[HsTag.LIFESTEAL_DAMAGES_ENEMY] > 0)
			{
				// that damage is the Lifesteal's effect, not Lifesteal damage again (as the inverted heal below)
				Character enemy = source.Controller.Opponent.Hero;
				if (!enemy._lifestealChecker)
				{
					enemy._lifestealChecker = true;
					enemy.TakeDamage(source, amount);
					enemy._lifestealChecker = false;
				}
			}
			else
				source.Controller.Hero.TakeHeal(source, amount);
			if (game.History)
				game.PowerHistory.Add(new PowerHistoryBlockEnd());

		}

		/// <summary>
		/// Inflict damage onto this character.
		/// The actual amount still needs to be determined by the current
		/// state of the game. eg: The presence of immunity effects can cause
		/// the damage to be ignored.
		/// </summary>
		/// <param name="source"></param>
		/// <param name="damage"></param>
		/// <returns></returns>
		// HsServer (2026-10-09, user decision): dealing damage doesn't break Stealth — only attacking does (Actions/Attack.cs).
		// Patch 11.0.0.23966 (2018-04-10, wiki Stealth.txt): "Minions now only break Stealth when attacking. Damage dealt by card
		// abilities, such as Knife Juggler's knife throw, no longer breaks Stealth." (The rulebook's DH5 / DH6 are 2015 tests.)
		// Live: titans-vfx 212735 — a Stealthed Animated Berserker's trigger damage kept it.
		// The one exception: a Titan's ability is its action instead of an attack — wiki Titan.txt:42 "A Titan will break Stealth
		// if it uses an ability that deals damage" (ClayByte, 2023-06-30). Any Damage Event counts, a shield's 0 too; a prevented one doesn't.
		private static void TitanBreaksStealth(IPlayable source)
		{
			if (source is Minion m && m.Id == m.Game.ActingTitanId && m.HasStealth && m[HsTag.PERMANENT_STEALTH] == 0)
				m.HasStealth = false;
		}

		public int TakeDamage(IPlayable source, int damage)
		{
			Game game = Game;
			var hero = this as Hero;
			var minion = this as Minion;

			if (minion != null && minion.Zone.Type != Enums.Zone.PLAY)
				return 0;
			// HsServer (TITANS review 2026-10-07): an untouchable minion takes no damage — a Dormant minion is "Untouchable for
			// both players" (RESEARCH/sources/wiki/Dormant.txt), a permanent "cannot be affected or targeted by any means" (wiki
			// Sargeras: The Twisting Nether). (A Location keeps its own durability rule: Generic.ActivateLocation.)
			if (minion != null && minion.Untouchable && !(minion is Location))
				return 0;

			// HsServer: Voidtouched Attendant — "Both heroes take one extra damage from all sources"
			if (this is Hero && damage > 0)
				damage += Controller.ControllerAuraEffects[HsTag.HERO_EXTRA_DAMAGE];
			// HsServer (VIOLET_HOLD): The Living Plague — "Instead of damaging heroes, this shuffles that many Blights into their deck" (wiki:
			// attacks, a weapon attacking into it, Misdirection onto its own hero)
			if (hero != null && damage > 0 && source is Minion plague && !plague.IsSilenced && plague.Card.Power?.HeroDamageShuffles is string blightId &&
			    Model.Cards.TryFromId(blightId) is Card blight)
			{
				for (int i = 0; i < damage; i++)
					Actions.Generic.ShuffleIntoDeck.Invoke(hero.Controller, plague, Entity.FromCard(hero.Controller, blight, new EntityData { { GameTag.CREATOR, plague.Id } }));
				return 0;
			}
			// HsServer (CATACLYSM): Selfless Protector — "Takes one extra damage from all sources" (its own, while not silenced)
			if (damage > 0 && this is Minion extra && !extra.IsSilenced && extra.Card.Power?.ExtraDamageTaken is int more1 && more1 > 0)
				damage += more1;
			// HsServer (LOST_CITY): Bralma Searstone — "Your Elementals deal 1 extra damage" (wiki: every damage instance whose source is
			// a friendly Elemental — attacks, Battlecries, Deathrattles, each split hit)
			if (damage > 0 && source != null && source.Card.Type == CardType.MINION && source.Card.IsRace(Race.ELEMENTAL) &&
			    source.Controller?.ControllerAuraEffects[HsTag.ELEMENTAL_EXTRA_DAMAGE] is int more && more > 0)
				damage += more;
			// HsServer: Moonfang — "Can only take 1 damage at a time" (after Spell Damage and doubling, as the rule book's note)
			if (this[HsTag.TAKES_ONE_DAMAGE] > 0 && damage > 1)
				damage = 1;

			// HsServer: fatigue is counted by the draw that causes it (Draw.PreDrawPhase), not here; this is only for the log.
			bool fatigue = hero != null && this == source;

			// HsServer: rules DH2a step 4 — an Immune target or 0 proposed damage prevents the event: nothing else happens
			// (a Divine Shield isn't lost, the source keeps Stealth). Immunity gained from a predamage trigger (Ice Block)
			// is checked again below.
			if (damage <= 0 || IsImmune)
			{
				game.Log(LogLevel.INFO, BlockType.ACTION, "Character", !game.Logging ? "" : $"{this} took no damage ({(IsImmune ? "immune" : "0 damage")}).");
				return 0;
			}

			// HsServer (2026-10-07): "Immune while attacking" for the Combat Sequence it attacks in (Generic.AttackBlock marks it;
			// rule book Advanced_rulebook.txt:509). Live (live-20261001-startofgame 2652ff, Ravenous Raptor DINO_136t hit back by
			// its defender): only the damage's first PREDAMAGE pulse, then nothing — no IMMUNE tag, no DAMAGE.
			if (this[HsTag.IMMUNE_THIS_ATTACK] > 0)
			{
				game.Log(LogLevel.INFO, BlockType.ACTION, "Character", !game.Logging ? "" : $"{this} took no damage (immune while attacking).");
				if (_history)
				{
					ClientTag(2187, source.Id);
					PreDamage = damage;
					ClientTag(1173, Id);
					PreDamage = 0;
					ClientTag(2187, 0);
					ClientTag(1173, 0);
				}
				return 0;
			}

			// HsServer (EMERALD_DREAM): "Immune to Fire spells" (Fyrakk; client IMMUNE_TO_FIRE_SPELLS 4066, live on it as it enters
			// play): no damage from a Fire spell
			if (this[IMMUNE_TO_FIRE_SPELLS] > 0 && source is Spell fireSpell && fireSpell.Card[(GameTag)1635] == 2)
			{
				game.Log(LogLevel.INFO, BlockType.ACTION, "Character", !game.Logging ? "" : $"{this} is immune to Fire spells.");
				return 0;
			}

			// HsServer (SPACE): a hero can have Divine Shield too (Artanis' Twin Blades: "Give ... your hero ... Divine Shield")
			if (hero != null && hero[GameTag.DIVINE_SHIELD] > 0)
			{
				game.Log(LogLevel.INFO, BlockType.ACTION, "Character", !game.Logging ? "" : $"{this} divine shield absorbed incoming damage.");
				hero[GameTag.DIVINE_SHIELD] = 0;
				TitanBreaksStealth(source);
				return 0;
			}

			// HsServer (EMERALD_DREAM): "Your Divine Shields take three hits to break" (Toreth; wiki: "Cracks appear after blocking the
			// first hit, and larger cracks appear after blocking the second"): the shield absorbs the hit and stays until its last one
			if (minion != null && minion.HasDivineShield && minion.Controller.ControllerAuraEffects[HsTag.DIVINE_SHIELD_HITS] is int shieldHits &&
			    shieldHits > 1 && minion[HsTag.SHIELD_HITS_TAKEN] + 1 < shieldHits)
			{
				minion[HsTag.SHIELD_HITS_TAKEN] = minion[HsTag.SHIELD_HITS_TAKEN] + 1;
				minion[HsTag.DIVINE_SHIELD_DAMAGE] = minion[HsTag.SHIELD_HITS_TAKEN];
				TitanBreaksStealth(source);
				return 0;
			}

			if (minion != null && minion.HasDivineShield)
			{
				game.Log(LogLevel.INFO, BlockType.ACTION, "Character", !game.Logging? "":$"{this} divine shield absorbed incoming damage.");
				minion[HsTag.DIVINE_SHIELD_DAMAGE] = 1; // HsServer: live, right before DIVINE_SHIELD = 0
				minion.HasDivineShield = false;
				TitanBreaksStealth(source);
				return 0;
			}

			int armor = hero?.Armor ?? 0;

			// HsServer: rules E1 — damage Armor absorbs still counts as damage dealt (triggers, Lifesteal, "was damaged",
			// stats). `amount` is the whole damage; only the part past the Armor reaches Health (below).
			int amount = damage;

			// Damage event is created
			// Collect all the tasks and sort them by order of play
			// Death phase and aura update are not emerge here

			// place event related data
			game.TaskQueue.StartEvent();
			EventMetaData temp = game.CurrentEventData;
			game.CurrentEventData = new EventMetaData(source, this, amount);

			// HsServer: live damage sequence (official Power.log): a first PREDAMAGE pulse framed by tag 2187 = source
			// and tag 1173 = the target itself, then PREDAMAGE = amount again for the pre-damage triggers (e.g. Ice Block).
			if (_history)
			{
				ClientTag(2187, source.Id);
				PreDamage = amount;
				ClientTag(1173, Id);
				ClientTag(2187, 0);
				PreDamage = 0;
				ClientTag(1173, 0);
				PreDamage = amount;
			}

			// Predamage triggers (e.g. Ice Block)
			if (PreDamageTrigger != null || game.TriggerManager.HasPreDamageTrigger) // HsServer: + game-wide (Snapjaw Shellfighter)
			{
				PreDamageTrigger?.Invoke(this);
				game.TriggerManager.OnPreDamageTrigger(this);
				game.ProcessTasks();
				amount = game.CurrentEventData.EventNumber;
				if (amount == 0)
				{
					if (_history)
						PreDamage = 0;

					game.TaskQueue.EndEvent();
					game.CurrentEventData = temp;
					return 0;
				}
			}
			if (IsImmune)
			{
				game.TaskQueue.EndEvent();
				game.CurrentEventData = temp;

				game.Log(LogLevel.INFO, BlockType.ACTION, "Character", !game.Logging ? "" : $"{this} is immune.");
				if (_history)
					PreDamage = 0;
				return 0;
			}

			TitanBreaksStealth(source);

			// reset predamage
			if (_history)
				PreDamage = 0;

			// HsServer: live order after the pulses: META_DATA DAMAGE (the whole damage, Armor included), ARMOR, the
			// times-damaged counter (tag 4741), LAST_AFFECTED_BY, then DAMAGE (only when Health drops). The client shows
			// the damage number from the DAMAGE metadata.
			if (_history && amount > 0)
				game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.DAMAGE, Data = amount, Info = new List<int> { Id } });

			// Armor takes the damage first (the amount after predamage triggers).
			int healthLoss = amount;
			if (armor > 0)
			{
				int absorbed = Math.Min(armor, amount);
				hero.Armor = armor - absorbed; // (the Armor setter notes the loss: "If your Armor changed this turn")
				healthLoss -= absorbed;
			}
			// HsServer (TITANS): the damage a hero takes on its own turns this game (client player tag 3110, live; Imprisoned
			// Horror: "each damage you've taken on your turns") — the whole damage, Armor-absorbed included
			if (hero != null && amount > 0 && game.CurrentPlayer == hero.Controller)
				hero.Controller[Actions.Generic.DAMAGE_TAKEN_ON_OWN_TURNS] = hero.Controller[Actions.Generic.DAMAGE_TAKEN_ON_OWN_TURNS] + amount;

			// HsServer (CATACLYSM): the damage the player dealt with spells this turn (client player tag 4587, live: each Spell source's
			// damage, not Hero Powers'; reset at the turn change)
			if (amount > 0 && source is Spell && source.Controller != null)
				source.Controller[Actions.Generic.SPELL_DAMAGE_THIS_TURN] = source.Controller[Actions.Generic.SPELL_DAMAGE_THIS_TURN] + amount;

			if (amount > 0)
				Actions.Generic.NoteAffected(this, source); // TIMES_AFFECTED (4741) + LAST_AFFECTED_BY, one counter for every effect
			// HsServer (2026-10-08): damage the other player's sources deal to this hero (quests; fatigue's source is the hero itself)
			if (hero != null && amount > 0 && source?.Controller is Controller dealer && dealer != hero.Controller)
				dealer[HsTag.DAMAGE_DEALT_TO_ENEMY_HERO] = dealer[HsTag.DAMAGE_DEALT_TO_ENEMY_HERO] + amount;

			// HsServer (CATACLYSM): Arisen Onyxia — "When your hero would lose Health on your turn, gain that much max Health instead"
			// (wiki: "Losing health is distinct from taking damage"; Armor still absorbs damage first)
			healthLoss = OnyxiaTakesHealthLoss(hero, healthLoss);

			// HsServer (2026-10-08): "can't be reduced below 1 Health" (HEALTH_MINIMUM: Commanding Shout, REV_337e) holds at the Health loss;
			// the damage dealt stays whole — x_Damage.txt:75 "its current Health is prevented from dropping below 1; however, Commanding
			// Shout does not modify how much damage was considered to be dealt (for example when queried by Imp-losion)"; rulebook DH4c.
			// (Was a pre-damage trigger lowering the damage itself.)
			if (minion != null && healthLoss > 0 && minion[GameTag.HEALTH_MINIMUM] > 0)
				healthLoss = Math.Max(0, Math.Min(healthLoss, minion.Health - minion[GameTag.HEALTH_MINIMUM]));

			// final damage is beeing accumulated
			Damage += healthLoss;
			// HsServer (CATACLYSM): the minion that killed it (Faceless Replicator) — the first minion whose damage took it to 0
			if (minion != null && healthLoss > 0 && minion.Health <= 0 && minion[HsTag.KILLED_BY] == 0 && source is Minion killer1)
				minion[HsTag.KILLED_BY] = killer1.Id;
			if (hero != null && healthLoss > 0) HeroHealthChanged();
			// HsServer: what of this damage reached the hero's Health, for its damage triggers (Arcanite Ripper: Armor alone
			// isn't a Health change)
			if (hero != null) hero[HsTag.HERO_HEALTH_LOST_LAST_DAMAGE] = healthLoss;

			// HsServer: rules D2 — Poisonous destroys any minion the source damages, whatever the damage came from (attack,
			// Battlecry, trigger, Deathrattle). A hero attacking with a Poisonous weapon is handled in combat.
			if (minion != null && amount > 0 && !minion.ToBeDestroyed &&
			    (source is Minion { Poisonous: true } || source is Weapon { Poisonous: true } ||
			     source is Minion { IsSilenced: false } old && old.Card.DestroysWhatItDamages || // Vanilla wording of Poisonous
			     source is Spell && source.Controller.ControllerAuraEffects[HsTag.SPELLS_POISONOUS] > 0)) // Professor Slate
			{
				game.Log(LogLevel.VERBOSE, BlockType.ACTION, "Character", !game.Logging ? "" : $"poisonous {source} destroyed {this}.");
				Actions.Generic.PoisonousKill(game, source, minion);
			}

			game.Log(LogLevel.INFO, BlockType.ACTION, "Character", !game.Logging? "":$"{this} took damage for {amount}({damage}). {(fatigue ? "(fatigue)" : "")}");

			//LastAffectedBy = source.Id;	TODO

			// on-damage triggers
			TakeDamageTrigger?.Invoke(this);
			game.TriggerManager.OnDamageTrigger(this);
			game.TriggerManager.OnDealDamageTrigger(source);

			// Check if the source is Overkill
			if (source.HasOverkill && source.Controller == game.CurrentPlayer && Health < 0)
			{
				game.Log(LogLevel.VERBOSE, BlockType.TRIGGER, "TakeDamage", !_logging ? "" : $"{source}' Overkill is triggered.");

				// HsServer: an unscripted (placeholder) Overkill card has no task to run.
				ISimpleTask task = source is Hero h ? h.Weapon?.Card.Power?.OverkillTask : source.Card.Power?.OverkillTask;
				if (task != null)
					game.TaskQueue.Enqueue(task, source.Controller, source, null, (int)GameTag.OVERKILL); // live TriggerKeyword=OVERKILL
			}

			// HsServer: Honorable Kill (wiki: "triggers when a minion or spell deals exact damage required to kill a character,
			// during the owner's turn"; it triggers even if that minion dies in the same combat; Overkill doesn't). The card's
			// own (its HONORABLE_KILL tag: silence removes it) and any an enchantment grants (Wing Commander Mulverick).
			if (Health == 0 && source.Controller == game.CurrentPlayer)
			{
				this[HsTag.HONORABLY_KILLED] = 1;
				IPlayable killer = source is Hero byHero ? byHero.Weapon : source;
				if (killer != null)
				{
					// (a hero power can't be silenced, and its data may lack the tag: Magister Dawngrasp's Arcane Burst)
					if ((killer[HsTag.HONORABLE_KILL] > 0 || killer is HeroPower) && killer.Card.Power?.HonorableKillTask is ISimpleTask own)
						game.TaskQueue.Enqueue(own, source.Controller, killer, this, (int)HsTag.HONORABLE_KILL);
					// HsServer (WHIZBANG): "After this minion deals exact lethal damage on your turn" (Nostalgic Gnome: no tag, its TRIGGER_VISUAL)
					else if (killer.Card.Power?.ExactLethal == true && !(killer is Minion quiet && quiet.IsSilenced) && killer.Card.Power.HonorableKillTask is ISimpleTask spelled)
						game.TaskQueue.Enqueue(spelled, source.Controller, killer, this, (int)GameTag.TRIGGER_VISUAL);
					if (killer.AppliedEnchantments != null)
						foreach (Enchantment e in killer.AppliedEnchantments.ToList())
							if (e.Power?.HonorableKillTask is ISimpleTask granted)
								game.TaskQueue.Enqueue(granted, source.Controller, killer, this, (int)HsTag.HONORABLE_KILL);
				}
			}

			// HsServer: inside an area effect the hit's triggers wait until every target is hit (rules DH1).
			if (!game.TaskQueue.DeferCurrentEvent(game.CurrentEventData))
				game.ProcessTasks();
			game.TaskQueue.EndEvent();
			game.CurrentEventData = temp;

			// HsServer (WILD_WEST): "Freeze any character damaged by your hero" (Quartzite Crusher: the equipped weapon card's own
			// FREEZE; Generic.HeroFreezesWhatItDamages) — its attacks and any other damage the hero deals
			if (amount > 0 && !IsFrozen && Actions.Generic.HeroFreezesWhatItDamages(source))
				IsFrozen = true;

			// Check if the source is lifesteal
			if (source.HasLifeSteal && !_lifestealChecker && !game.RulesDamage)
			{
				// HsServer (2026-10-08): in combat the heal comes after both hits (Generic.AttackBlock pays it: live 208 of 208 attacks —
				// DAMAGE, DAMAGE, then the LIFESTEAL block; wiki Lifesteal.txt:13-14)
				if (game.DeferredLifesteal != null)
					game.DeferredLifesteal.Add((source, amount));
				else
					Lifesteal(game, source, amount);
			}

			if (hero != null)
				hero.DamageTakenThisTurn += amount;
			// HsServer (ISLAND_VACATION): "this turn" for either player (live player tag 4453 = times damaged this turn, zeroed for
			// both players at every turn change; 3687 = times damaged on its own turns this game, next to 3110)
			if (hero != null && amount > 0)
			{
				hero.Controller[HsTag.HERO_DAMAGE_THIS_TURN] = hero.Controller[HsTag.HERO_DAMAGE_THIS_TURN] + amount;
				hero.Controller[Actions.Generic.HERO_DAMAGED_TIMES_THIS_TURN] = hero.Controller[Actions.Generic.HERO_DAMAGED_TIMES_THIS_TURN] + 1;
				if (game.CurrentPlayer == hero.Controller)
					hero.Controller[Actions.Generic.HERO_DAMAGED_TIMES_ON_OWN_TURNS] = hero.Controller[Actions.Generic.HERO_DAMAGED_TIMES_ON_OWN_TURNS] + 1;
			}

			if (source.Card.Type == CardType.HERO_POWER)
				source.Controller.NumHeroPowerDamageThisGame += amount;

			return amount;
		}

		/// <summary>
		/// Heal up all taken damage.
		/// </summary>
		/// <param name="source"></param>
		public void TakeFullHeal(IPlayable source)
		{
			// HsServer: rules E2 — "restore to full" proposes the maximum Health (it matters for Overheal and for a heal
			// Auchenai turns into damage); the Health actually restored is still only the damage taken.
			TakeHeal(source, BaseHealth);
		}

		/// <summary>
		/// Heal a specified amount of health.
		/// </summary>
		/// <param name="source"></param>
		/// <param name="heal"></param>
		/// <summary>HsServer: the "next Healing effect deals damage" rule was used up: remove what granted it. The rule is the
		/// player's tag (its enchantment only shows it: an AI's copy of the game has no enchantment entity to remove), so it is
		/// cleared directly too.</summary>
		private static void ClearNextHealDamages(Controller c)
		{
			c[HsTag.NEXT_HEAL_SOURCE] = 0;
			c[HsTag.NEXT_HEAL_EFFECT_MOVE] = 0;
			c[HsTag.NEXT_HEAL_EFFECT_TASK] = 0;
			for (int i = (c.AppliedEnchantments?.Count ?? 0) - 1; i >= 0; i--)
			{
				Enchantment e = c.AppliedEnchantments[i];
				if (e.Card.Power?.Enchant?.Effects?.Any(x => x is Enchants.Effect f && f.Tag == HsTag.NEXT_HEAL_DAMAGES) == true)
					RemoveEnchantmentTask.Task.Process(e.Game, e.Controller, e, null); // reverts its effects too
			}
			if (c[HsTag.NEXT_HEAL_DAMAGES] > 0) c[HsTag.NEXT_HEAL_DAMAGES] = 0;
		}

		/// <summary>HsServer: the hero's Health changed (damage past its Armor, or healing that restored some): counted for
		/// "this turn" and "during your turns" effects (HsTag.HERO_HEALTH_CHANGED_TURN / HERO_HEALTH_CHANGES_OWN_TURNS).</summary>
		/// <summary>HsServer (CATACLYSM): Arisen Onyxia turns a Health loss on its owner's turn into max Health; what is left to lose.</summary>
		private static int OnyxiaTakesHealthLoss(Hero hero, int healthLoss)
		{
			if (hero != null && healthLoss > 0 && hero.Game.CurrentPlayer == hero.Controller &&
			    hero.Controller.BoardZone.FirstOrDefault(m => !m.IsSilenced && !m.IsDead && m.Card.Power?.HealthLossToMaxHealth != null) is Minion onyxia &&
			    Model.Cards.TryFromId(onyxia.Card.Power.HealthLossToMaxHealth) is Card blackScales)
			{
				Actions.Generic.AddEnchantmentBlock(hero.Game, blackScales, onyxia, hero, healthLoss, 0, 0);
				return 0;
			}
			return healthLoss;
		}

		/// <summary>
		/// HsServer (livecheck 2026-10-10): a Health cost paid ("costs Health instead of Mana"): the hero loses that much Health — not
		/// damage. Wiki Change_Cost_to_Health (https://hearthstone.wiki.gg/wiki/Change_Cost_to_Health, patch 26.0.0.170824): "Immune and
		/// Divine Shield no longer prevent health from being lost if a card's cost is changed to health"; "Playing a card for health also
		/// no longer counts as taking damage, and as such always removes health only and ignores armor." Live: every recorded Health
		/// payment with Armor up left the Armor (75 of 75 in 110 sessions, e.g. session-20260929-122856 26858: META DAMAGE 3, DAMAGE 3 → 6, ARMOR 6 kept;
		/// 20261004-222041 21566). Its client marks: META DAMAGE (the amount), the times-affected counter, DAMAGE. No damage triggers.
		/// Arisen Onyxia still turns it into max Health on its owner's turn ("would lose Health").
		/// </summary>
		public void LoseHealthForCost(int amount)
		{
			if (amount <= 0 || !(this is Hero hero)) return;
			if (Game.History)
			{
				Game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.DAMAGE, Data = amount, Info = new List<int> { Id } });
				this[Actions.Generic.TIMES_AFFECTED] = this[Actions.Generic.TIMES_AFFECTED] + 1;
			}
			int healthLoss = OnyxiaTakesHealthLoss(hero, amount);
			if (healthLoss <= 0) return;
			Damage += healthLoss;
			HeroHealthChanged();
		}

		private void HeroHealthChanged()
		{
			Controller owner = Controller;
			owner[HsTag.HERO_HEALTH_CHANGED_TURN] = Game.Turn;
			if (Game.CurrentPlayer == owner)
				owner[HsTag.HERO_HEALTH_CHANGES_OWN_TURNS] = owner[HsTag.HERO_HEALTH_CHANGES_OWN_TURNS] + 1;
		}

		public void TakeHeal(IPlayable source, int heal)
		{
			// HsServer: Lightsteed — "Your healing effects also give affected minions +2 Health" (healed or not)
			if (this is Minion && source?.Controller is Controller steedOwner && steedOwner.ControllerAuraEffects[HsTag.HEALING_GIVES_HEALTH] > 0 &&
			    Model.Cards.TryFromId("YOP_008e") is Card lightsteed)
				for (int i = 0; i < steedOwner.ControllerAuraEffects[HsTag.HEALING_GIVES_HEALTH]; i++)
					Actions.Generic.AddEnchantmentBlock(Game, lightsteed, source, this, 0, 0, 0);
			// HsServer (CATACLYSM): Cleansing Cleric — "Your healing effects restore 2 more Health" (client CURRENT_HEALING_POWER 2333; the
			// client adds it to every # before the doublers; wiki: per healing instance)
			if (heal > 0 && source?.Controller is Controller healingPower && healingPower[(GameTag)2333] > 0)
				heal += healingPower[(GameTag)2333];
			int proposedHeal = heal; // before the healing doublers (an inverted heal is doubled as damage instead)
			// HsServer (TIME_TRAVEL): a spell's healing by the spell doubler (Atiesh: "Double the damage and healing of your spells"), a
			// Hero Power's by the Hero Power doubler (Clockwork Automaton: "... of your Hero Power"; Velen has both) — was: both by the spell one
			if (source is Spell && source.Controller.ControllerAuraEffects[GameTag.SPELL_HEALING_DOUBLE] > 0)
				heal *= (int) Math.Pow(2, source.Controller.ControllerAuraEffects[GameTag.SPELL_HEALING_DOUBLE]);
			else if (source is HeroPower && source.Controller.ControllerAuraEffects[GameTag.HERO_POWER_DOUBLE] > 0)
				heal *= (int) Math.Pow(2, source.Controller.ControllerAuraEffects[GameTag.HERO_POWER_DOUBLE]);

			if (source.Controller.ControllerAuraEffects[GameTag.ALL_HEALING_DOUBLE] > 0)
				heal *= (int) Math.Pow(2, source.Controller.ControllerAuraEffects[GameTag.ALL_HEALING_DOUBLE]);

			// HsServer: "your next Healing effect deals damage instead" (Ruby Sanctum, Shadowtouched Kvaldir): every heal of the
			// first healing EFFECT after it converts — its source, in the same move and task (Game.MoveNumber / TasksThisMove);
			// any later heal, the same source's next one too (a Hero Power used again, a Lifesteal minion's next attack), finds
			// the rule spent: it is cleared and that heal heals. Wiki Shadowtouched Kvaldir, patch 28.6.0.193541: "Fixed a bug
			// where Shadowtouched Kvaldir's effect would apply to all end of turn healing effects on the turn played, instead of
			// just the first one to trigger".
			Controller healer = source.Controller;
			bool nextHealDamages = false;
			if (healer.ControllerAuraEffects[HsTag.NEXT_HEAL_DAMAGES] > 0 || healer[HsTag.NEXT_HEAL_DAMAGES] > 0)
			{
				int first = healer[HsTag.NEXT_HEAL_SOURCE];
				if (first == 0)
				{
					healer[HsTag.NEXT_HEAL_SOURCE] = source.Id;
					healer[HsTag.NEXT_HEAL_EFFECT_MOVE] = Game.MoveNumber;
					healer[HsTag.NEXT_HEAL_EFFECT_TASK] = Game.TasksThisMove;
					nextHealDamages = true;
				}
				else if (first == source.Id && healer[HsTag.NEXT_HEAL_EFFECT_MOVE] == Game.MoveNumber &&
				         healer[HsTag.NEXT_HEAL_EFFECT_TASK] == Game.TasksThisMove)
					nextHealDamages = true;
				else
					ClearNextHealDamages(healer);
			}

			if (source.Controller.RestoreToDamage || nextHealDamages)
			{
				if (_lifestealChecker)
					return;

				_lifestealChecker = true;
				// HsServer: rules DH2a steps 1-3 — the heal becomes a Damage Event first, then Spell Damage / Hero Power
				// bonuses and the doublers apply to it (an Auchenai-inverted Holy Nova gets Spell Damage).
				Actions.Generic.DamageCharFunc(source, this, proposedHeal, true);
				_lifestealChecker = false;
				return;
			}
			// HsServer: "can't be healed" (Repulsive Gargantuan: a rule on the player; or the character's own CANT_BE_HEALED):
			// the heal doesn't happen (an inverted heal above is damage, not a heal, so it still does).
			if (this[GameTag.CANT_BE_HEALED] > 0 || Controller.ControllerAuraEffects[HsTag.CHARACTERS_CANT_BE_HEALED] > 0)
				return;
			// HsServer (LOST_CITY): "Whenever you heal an enemy, this attacks it." (Wilted Shadow, Power.AttacksHealedEnemy) — before the
			// heal; the healed one dead: no heal; a full-Health one: no heal after (the remembered missing Health)
			if (source?.Controller is Controller friend && friend != Controller && !_lifestealChecker)
			{
				var shadows = friend.BoardZone.GetAll().Where(m => m.Card.Power?.AttacksHealedEnemy == true && !m.IsSilenced && !m.IsDead && !m.ToBeDestroyed).ToList();
				if (shadows.Count > 0)
				{
					int missing = Damage;
					foreach (Minion shadow in shadows)
					{
						if (IsDead || ToBeDestroyed || shadow.Zone?.Type != SabberStoneCore.Enums.Zone.PLAY) break;
						Actions.Generic.AttackBlock.Invoke(friend, shadow, this, true, true);
						Game.DeathProcessingAndAuraUpdate();
					}
					if (IsDead || ToBeDestroyed || Zone?.Type == SabberStoneCore.Enums.Zone.GRAVEYARD) return;
					heal = Math.Min(heal, missing);
					if (heal <= 0) return;
				}
			}
			// HsServer: Overheal — the part of the heal beyond full Health (healing an undamaged character is all overheal).
			int overheal = heal - Damage;
			// we don't heal undamaged entities
			if (Damage == 0)
			{
				// HsServer: live still shows the attempt (PREHEALING pulse, the heal's TARGET): a Lifesteal on a full-Health
				// hero animates its drain; an empty block showed nothing.
				if (_history && heal > 0)
					this[GameTag.PREHEALING] = heal;
				// live (all 23 OVERHEAL blocks, e.g. live-20261006-yogg-cthun 33099): the Overheal trigger inside the heal, between
				// PREHEALING n and 0 (IsDeferrable: the client plays it after the heal)
				if (overheal > 0) Overheal(source, overheal);
				if (_history && heal > 0)
					this[GameTag.PREHEALING] = 0;
				return;
			}

			int amount = Damage > heal ? heal : Damage;
			if (Game.Logging)
				Game.Log(LogLevel.INFO, BlockType.ACTION, "Character", $"{this} took healing for {amount}.");
			// HsServer: live heal sequence (official Power.log): PREHEALING pulse with the requested amount, then
			// META_DATA HEALING with the amount actually restored, then the new DAMAGE.
			if (_history)
				this[GameTag.PREHEALING] = heal;
			// live: the Overheal trigger inside the heal, before PREHEALING 0, the HEALING metadata and the new DAMAGE (see above)
			if (overheal > 0) Overheal(source, overheal);
			if (_history)
			{
				this[GameTag.PREHEALING] = 0;
				Game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.HEALING, Data = amount, Info = new List<int> { Id } });
			}
			Damage -= amount;
			if (this is Hero && amount > 0) HeroHealthChanged();

			// Heal event created
			// Process gathered tasks
			Game.TaskQueue.StartEvent();
			EventMetaData temp = Game.CurrentEventData;
			Game.CurrentEventData = new EventMetaData(source, this, amount);
			Game.TriggerManager.OnHealTrigger(this);
			// HsServer: inside an area heal the triggers wait until every target is healed (rules DH1).
			if (!Game.TaskQueue.DeferCurrentEvent(Game.CurrentEventData))
				Game.ProcessTasks();
			Game.TaskQueue.EndEvent();
			Game.CurrentEventData = temp;

			if (this is Hero)
				Controller.AmountHeroHealedThisTurn += amount;
			if (source?.Controller != null) // HsServer
			{
				source.Controller.AmountHealedThisTurn += amount;
				source.Controller.AmountHealedThisGame += amount; // live AMOUNT_HEALED_THIS_GAME
			}
		}

		// HsServer: raises the Overheal event for this character (Holy Champion, Mana Geode...).
		private void Overheal(IPlayable source, int excess)
		{
			if (source?.Controller != null) source.Controller[HsTag.OVERHEALS_DONE]++; // HsServer: "for each one Overhealed"
			if (source?.Controller != null) // HsServer (TITANS): "the amount Overhealed" (Grace of the Highfather)
				source.Controller[HsTag.OVERHEALED_AMOUNT] = source.Controller[HsTag.OVERHEALED_AMOUNT] + excess;
			Game.TaskQueue.StartEvent();
			EventMetaData temp = Game.CurrentEventData;
			Game.CurrentEventData = new EventMetaData(source, this, excess);
			Game.TriggerManager.OnOverhealTrigger(this);
			Game.ProcessTasks();
			Game.TaskQueue.EndEvent();
			Game.CurrentEventData = temp;
		}

		/// <summary>
		/// Gain the specified amount of armor.
		/// </summary>
		/// <param name="source"></param>
		/// <param name="armor"></param>
		/// <summary>HsServer: a hero gained Attack from an effect — HERO_ATTACK triggers (event number = the amount). HsServer
		/// (ISLAND_VACATION): a minion that gained Attack raises ATTACK_GAINED (Service Ace: "After this minion gains Attack").</summary>
		public void RaiseAttackGained(int amount)
		{
			EventMetaData temp = Game.CurrentEventData;
			Game.CurrentEventData = new EventMetaData(this, this, amount);
			if (this is Hero)
				Game.RaiseEvent(() => Game.TriggerManager.OnHeroAttackTrigger(this));
			else
				Game.RaiseEvent(() => Game.TriggerManager.OnAttackGainedTrigger(this));
			Game.CurrentEventData = temp;
		}

		public void GainArmor(IPlayable source, int armor)
		{
			Game.Log(LogLevel.INFO, BlockType.ACTION, "Character", !Game.Logging? "":$"{this} gaining armor for {armor}.");
			Armor += armor;
			if (armor > 0)
			{
				Controller[HsTag.ARMOR_GAINED_THIS_GAME] += armor; // HsServer: live ARMOR_GAINED_THIS_GAME
				// HsServer: "Armor gained this turn" (Zok Fogsnout): the count with the turn it belongs to
				if (Controller[HsTag.ARMOR_GAINED_TURN] != Game.Turn)
				{
					Controller[HsTag.ARMOR_GAINED_TURN] = Game.Turn;
					Controller[HsTag.ARMOR_GAINED_THIS_TURN] = 0;
				}
				Controller[HsTag.ARMOR_GAINED_THIS_TURN] += armor;
			}
			EventMetaData temp = Game.CurrentEventData;
			Game.CurrentEventData = new EventMetaData(source, this, armor);
			Game.RaiseEvent(() => Game.TriggerManager.OnArmorTrigger(this));
			Game.CurrentEventData = temp;
		}

		public void OnAfterAttackTrigger()
		{
			AfterAttackTrigger?.Invoke(this);
			Game.TriggerManager.OnAfterAttackTrigger(this); // HsServer: game-wide listeners
		}

		public override string Hash(params GameTag[] ignore)
		{
			var sb = new StringBuilder(base.Hash(ignore));
			sb.Append($"[A:{_modifiedATK}, ");
			sb.Append($"H:{_modifiedHealth}, ");
			sb.Append($"D:{_damage}]");
			return sb.ToString();
		}
	}

	public partial interface ICharacter
	{
		/// <summary>
		/// The amount of damage this character can output.
		/// </summary>
		int AttackDamage { get; set; }

		/// <summary>
		/// The amount of damage taken during the evaluation of one phase.
		/// </summary>
		int Damage { get; set; }

		/// <summary>
		/// The amount of damage this character is about to take.
		/// </summary>
		int PreDamage { get; set; }

		/// <summary>
		/// The amount of health this character has remaing. This value is returned
		/// AFTER SUBTRACTING the pending damage for this entity.
		/// </summary>
		int Health { get; set; }

		/// <summary>
		/// The starting amount of health of this character.
		/// </summary>
		int BaseHealth { get; }

		/// <summary>
		/// This character is currently attacking another character.
		/// </summary>
		bool IsAttacking { get; set; }

		/// <summary>
		/// This character is currently defending against another character.
		/// </summary>
		bool IsDefending { get; set; }

		/// <summary>
		/// Amount of attacks this character has executed during this turn.
		/// </summary>
		int NumAttacksThisTurn { get; set; }

		/// <summary>HsServer: attacks made by this character this game (client tag 4163, history only).</summary>
		int AttacksThisGame { get; set; }

		/// <summary>
		/// Character is member of Race.
		/// Characters of Race.ALL.  IE Amalgam.IsRace(Race.MULROC/Race.DRAGON/...) => true
		/// </summary>
		bool IsRace(Race race);

		///// <summary>
		///// Character should exit combat.
		///// </summary>
		//bool ShouldExitCombat { get; set; }

		/// <summary>
		/// Character is frozen.
		/// </summary>
		bool IsFrozen { get; set; }

		/// <summary>
		/// Character is silenced.
		/// </summary>
		bool IsSilenced { get; set; }

		/// <summary>
		/// Character is immune.
		/// </summary>
		bool IsImmune { get; set; }

		/// <summary>
		/// Character has taunt.
		/// </summary>
		bool HasTaunt { get; set; }

		/// <summary>
		/// Character has windfury.
		/// </summary>
		bool HasWindfury { get; }

		/// <summary>
		/// Character has stealth.
		/// </summary>
		bool HasStealth { get; }

		/// <summary>
		/// Character can't be targeted by spells.
		/// </summary>
		bool CantBeTargetedBySpells { get; set; }

		/// <summary>
		/// Character can't be targeted by opponents.
		/// </summary>
		bool CantBeTargetedByOpponents { get; set; }

		/// <summary>
		/// Character can't be targeted by heropowers.
		/// </summary>
		bool CantBeTargetedByHeroPowers { get; }

	}

	public abstract partial class Character
	{
		private bool _lifestealChecker;

		internal int? _modifiedATK;
		internal int? _modifiedHealth;
		internal bool? _modifiedStealth;
		internal bool? _modifiedImmune;
		internal bool? _modifiedTaunt;
		internal bool? _modifiedCantBeTargetedBySpells;

		internal int _damage;
		internal int _numAttackThisTurn;

		internal void CopyInternalAttributes(in Character copy)
		{
			copy._modifiedATK = _modifiedATK;
			copy._modifiedHealth = _modifiedHealth;
			copy._damage = _damage;
			copy._numAttackThisTurn = _numAttackThisTurn;
			copy._modifiedStealth = _modifiedStealth;
			copy._modifiedImmune = _modifiedImmune;
			copy._modifiedTaunt = _modifiedTaunt;
			copy._modifiedCantBeTargetedBySpells = _modifiedCantBeTargetedBySpells;
		}

#pragma warning disable CS1591 // Fehledes XML-Kommentar für öffentlich sichtbaren Typ oder Element

		public virtual int AttackDamage
		{
			get
			{
				int value = _modifiedATK ?? (_modifiedATK = Card.ATK).Value;

				value += AuraEffects?.ATK ?? 0;

				return value < 0 ? 0 : value;
			}
			set
			{
				if (_logging)
					Game.Log(LogLevel.DEBUG, BlockType.TRIGGER, "Entity", !Game.Logging ? "" : $"{this} set data {GameTag.ATK} to {value}");
				if (_history && value + (AuraEffects?.ATK ?? 0) != AttackDamage)
				{
					// HsServer: the client shows the tag as is, so report the value with auras (was the base value).
					Game.PowerHistory.Add(PowerHistoryBuilder.TagChange(Id, GameTag.ATK, Math.Max(0, value + (AuraEffects?.ATK ?? 0))));
					_data[GameTag.ATK] = value;
				}

				_modifiedATK = value;
			}
		}

		public int BaseHealth
		{
			get
			{
				int value = _modifiedHealth ?? (_modifiedHealth = Card.Health).Value;

				return value + (AuraEffects?.Health ?? 0);
			}
			set
			{
				if (_logging)
					Game.Log(LogLevel.DEBUG, BlockType.TRIGGER, "Entity", !Game.Logging ? "" : $"{this} set data {GameTag.HEALTH} to {value}");
				// HsServer: compare with the health (upstream compared with the attack) and report the value with auras.
				if (_history && value + (AuraEffects?.Health ?? 0) != BaseHealth)
				{
					Game.PowerHistory.Add(PowerHistoryBuilder.TagChange(Id, GameTag.HEALTH, value + (AuraEffects?.Health ?? 0)));
					_data[GameTag.HEALTH] = value;
				}

				_modifiedHealth = value;
			}
		}

		public int Damage
		{
			get => _damage;
			set
			{
				// don't allow negative values
				if (value < 0)
					value = 0;
				else if (BaseHealth <= value)
				{
					// HsServer: reversible until the Death Creation Step (rules A5/A6) — a hero too (Advanced_rulebook.txt:593, :671: healed
					// back in the same Phase it lives)
					if (!ToBeDestroyed && (this is Minion || this is Hero))
						MortallyWounded = true;
					MarkLethal(); // (no TO_BE_DESTROYED to the client for lethal damage: live)
				}
				// HsServer (2026-10-08): a mortally wounded hero healed back above 0 lives (the minions' check is the Death Creation Step's)
				else if (this is Hero && MortallyWounded && !DestroyedByEffect && ToBeDestroyed)
				{
					MortallyWounded = false;
					ToBeDestroyed = false;
				}

				if (_logging)
					Game.Log(LogLevel.DEBUG, BlockType.TRIGGER, "Entity", !Game.Logging ? "" : $"{this} set data {GameTag.DAMAGE} to {value}");
				if (_history && value != _damage)
				{
					Game.PowerHistory.Add(PowerHistoryBuilder.TagChange(Id, GameTag.DAMAGE, value));
					_data[GameTag.DAMAGE] = value;
				}

				_damage = value;
			}
		}

		public int Health
		{
			get => BaseHealth - _damage;
			set
			{
				if (value == 0)
				{
					if (!ToBeDestroyed && (this is Minion || this is Hero))
						MortallyWounded = true;
					MarkLethal(); // (no TO_BE_DESTROYED to the client for lethal damage: live)
				}
				else if (this is Hero && MortallyWounded && !DestroyedByEffect && ToBeDestroyed)
				{
					MortallyWounded = false;
					ToBeDestroyed = false;
				}

				_modifiedHealth = value;
				_damage = 0;

				if (_logging)
				{
					Game.Log(LogLevel.DEBUG, BlockType.TRIGGER, "Entity", !Game.Logging ? "" : $"{this} set data {GameTag.HEALTH} to {value}");
					Game.Log(LogLevel.DEBUG, BlockType.TRIGGER, "Entity", !Game.Logging ? "" : $"{this} set data {GameTag.DAMAGE} to {value}");
				}

				if (_history)
				{
					Game.PowerHistory.Add(PowerHistoryBuilder.TagChange(Id, GameTag.HEALTH, value));
					_data[GameTag.HEALTH] = value;
					Game.PowerHistory.Add(PowerHistoryBuilder.TagChange(Id, GameTag.DAMAGE, 0));
					_data[GameTag.DAMAGE] = 0;
				}
			}
		}

		public bool CantAttack
		{
			get
			{
				if (!_data.TryGetValue(GameTag.CANT_ATTACK, out int value))
					return Card.CantAttack;
				
				return value > 0;
			}
			set => this[GameTag.CANT_ATTACK] = value ? 1 : 0;
		}

		public virtual bool CantAttackHeroes
		{
			get
			{
				_data.TryGetValue(GameTag.CANNOT_ATTACK_HEROES, out int value);
				return value > 0 || AuraEffects?.CannotAttackHeroes == true; // HsServer: or an aura (Supreme Abyssal)
			}
			set => this[GameTag.CANNOT_ATTACK_HEROES] = value ? 1 : 0;
		}

		public bool CantBeTargetedBySpells
		{
			get => (AuraEffects?.CantBeTargetedBySpells ?? false) ||
			       (_modifiedCantBeTargetedBySpells ??
			        (_modifiedCantBeTargetedBySpells = Card.CantBeTargetedBySpells).Value);
			set
			{
				_modifiedCantBeTargetedBySpells = value;
				if (_history)
				{
					base[GameTag.CANT_BE_TARGETED_BY_SPELLS] = value ? 1 : 0;
					base[GameTag.CANT_BE_TARGETED_BY_HERO_POWERS] = value ? 1 : 0;
				}
			}
		}

		public bool CantBeTargetedByHeroPowers
		{
			get => CantBeTargetedBySpells;
		}

		public int Armor
		{
			get
			{
				_data.TryGetValue(GameTag.ARMOR, out int value);
				return value;
			}
			set
			{
				int old = Armor;
				this[GameTag.ARMOR] = value;
				// HsServer (TITANS review 2026-10-07): every Armor loss counts for "If your Armor changed this turn" (Stoneskin
				// Armorer) — absorbed damage, an Armor cost (Anub'Rekhan, Smelt, Vezax), "lose N Armor" (Frozen Buckler), "remove all
				// Armor" (DRG_069) — in this one place (spec M3: live player tag 3143 "when Armor absorbs damage ... and when an
				// effect makes you lose Armor")
				if (value < old && this is Hero && Controller != null)
					Actions.Generic.NoteArmorLost(Controller, old - value);
			}
		}

		public int LastAffectedBy
		{
			get
			{
				_data.TryGetValue(GameTag.LAST_AFFECTED_BY, out int value);
				return value;
			}
			set { this[GameTag.LAST_AFFECTED_BY] = value; }
		}

		public bool CantBeTargetedByOpponents
		{
			get { return this[GameTag.CANT_BE_TARGETED_BY_OPPONENTS] == 1; }
			set { this[GameTag.CANT_BE_TARGETED_BY_OPPONENTS] = value ? 1 : 0; }
		}

		public bool IsAttacking
		{
			get { return this[GameTag.ATTACKING] == 1; }
			set { this[GameTag.ATTACKING] = value ? 1 : 0; }
		}

		public bool IsDefending
		{
			get { return this[GameTag.DEFENDING] == 1; }
			set { this[GameTag.DEFENDING] = value ? 1 : 0; }
		}

		public virtual bool IsImmune
		{
			get => _modifiedImmune == true;
			set
			{
				// HsServer: report before storing (see NumAttacksThisTurn).
				base[GameTag.IMMUNE] = value ? 1 : 0;
				_modifiedImmune = value;
			}
		}

		/// <summary>HsServer: CANT_BE_FROZEN (264), e.g. granted by Living Dragonbreath's aura.</summary>
		public static bool CantBeFrozen(ICharacter c) => c[GameTag.CANT_BE_FROZEN] > 0;

		public bool IsFrozen
		{
			get
			{
				_data.TryGetValue(GameTag.FROZEN, out int value);
				return value == 1 || AuraEffects?[GameTag.FROZEN] > 0; // HsServer (livecheck 2026-10-10): an aura's keyword counts (as Taunt's)
			}
			set
			{
				if (value)
				{
					if (CantBeFrozen(this)) return; // HsServer: Living Dragonbreath
					base[GameTag.FROZEN] = 1;
					Game.RaiseEvent(() => Game.TriggerManager.OnFreezeTrigger(this));
				}
				else
				{
					base[GameTag.FROZEN] = 0;
				}
			}
		}

		public bool IsSilenced
		{
			get { return _data.ContainsKey(GameTag.SILENCED); }
			set { this[GameTag.SILENCED] = value ? 1 : 0; }
		}

		public bool HasTaunt
		{
			get => (AuraEffects?.Taunt ?? false) || (_modifiedTaunt ?? (_modifiedTaunt = Card.Taunt).Value);
			set
			{
				// HsServer: report before storing (see NumAttacksThisTurn).
				base[GameTag.TAUNT] = value ? 1 : 0;
				_modifiedTaunt = value;
			}
		}

		public abstract bool HasWindfury { get; set; }

		public bool HasStealth
		{
			// HsServer (livecheck 2026-10-10): an aura's Stealth counts and attacking doesn't end it (Red Herring REV_014 "Your non-Red Herring
			// minions have Stealth": live session-20261007-190857, the played minion gets STEALTH 1 from the aura, keeps it after attacking
			// and loses it when the Herring dies); the property read only the minion's own Stealth
			get => (AuraEffects?[GameTag.STEALTH] > 0) || (_modifiedStealth ?? (_modifiedStealth = Card.Stealth).Value);
			set
			{
				// HsServer: report before storing (see NumAttacksThisTurn).
				base[GameTag.STEALTH] = value ? 1 : 0;
				_modifiedStealth = value;
			}
		}

		public int NumAttacksThisTurn
		{
			get => _numAttackThisTurn;
			set
			{
				// HsServer: report before storing — the base setter only records values that differ from the current one,
				// so storing first meant NUM_ATTACKS_THIS_TURN never reached the history (upstream bug).
				if (_history)
					base[GameTag.NUM_ATTACKS_THIS_TURN] = value;
				_numAttackThisTurn = value;
			}
		}

		private int _timesDamaged;

		/// <summary>HsServer: attacks made by this character this game (client tag 4163, history only).</summary>
		public int AttacksThisGame { get; set; }

		/// <summary>HsServer: a client tag outside the engine's own tag range (>= 1000), recorded in history only.</summary>
		private void ClientTag(int tag, int value) => Game.PowerHistory.Add(PowerHistoryBuilder.TagChange(Id, (GameTag)tag, value));

		public int PreDamage
		{
			// HsServer: a copied game's entities may not carry the key (Game.Clone: the Rewind point, AI search copies)
			get => _data.TryGetValue(GameTag.PREDAMAGE, out int value) ? value : 0;
			set => this[GameTag.PREDAMAGE] = value;
		}

		// HsServer: also a type given in play (an "is this type" tag on the entity), like a dual-type card's second type
		public bool IsRace(Race race) => Card.IsRace(race) || Card.RaceTag(race) is { } tag && this[tag] > 0;

		public bool ShouldExitCombat
		{
			get { return this[GameTag.SHOULDEXITCOMBAT] == 1; }
			set { this[GameTag.SHOULDEXITCOMBAT] = value ? 1 : 0; }
		}
#pragma warning restore CS1591 // Fehledes XML-Kommentar für öffentlich sichtbaren Typ oder Element

	}
}
