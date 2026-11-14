// HsServer addition (not upstream SabberStone): the shared mechanics of Caverns of Time (WONDERS, client set 1898).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SabberStoneCore.Auras;
using SabberStoneCore.Conditions;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.SimpleTasks;
using SabberStoneCore.Triggers;

namespace SabberStoneCore.Actions
{
	/// <summary>
	/// HsServer (WONDERS, 2026-10-07): the shared rules Caverns of Time needs — the C'Thun ritual as live sends it since patch
	/// 27.2 (and the cards that follow its gains), the in-hand shifters, "Go Dormant, revive after N die", Cenarion Hold's
	/// "next Choose One card this turn", Secrets put up by an effect and destroyed at the owner's next turn, "your next
	/// Battlecry this turn triggers twice", Poisoned Blade, "can't be Overloaded", "your spells have Lifesteal", "a card from
	/// your opponent's class", Spare Parts and "summon copies of deck cards". The "counts as a copy" reprints (client tag 858)
	/// are a loader rule (Loader/ReprintCards.cs: CountsAsCopyOf).
	/// </summary>
	public static partial class Generic
	{
		// =========================================================================================================== C'Thun

		/// <summary>Client player tags: the C'Thun ritual's running totals (live: CTHUN_ATTACK_BUFF / CTHUN_HEALTH_BUFF on the player
		/// entity, set to the new totals by every ritual; CTHUN_TAUNT_BUFF from Twilight Geomancer). FX_DATANUM_3: the ritual's
		/// C'Thun dbf on the cultist (live 38857).</summary>
		public const GameTag CTHUN_ATTACK_BUFF = (GameTag)3054, CTHUN_HEALTH_BUFF = (GameTag)3053, CTHUN_TAUNT_BUFF = (GameTag)3034,
			FX_DATANUM_3 = (GameTag)3109;

		/// <summary>Live's ritual effect (every cultist: RecordedSubSpells.tsv; opened and closed at once, no targets).</summary>
		private const string CthunRitualFx = "ReuseFX_CThun_Ritual_CardFromScript:75a0982284ddccf4c8553b51db790aa7";

		private static Card _cthun;

		/// <summary>The C'Thun a ritual buffs: the C'Thun card (client tag 3076) <paramref name="source"/> names as its related card
		/// (the cultists' COLLECTION_RELATED_CARD_DATABASE_ID), else the first collectible C'Thun card (OG_280).</summary>
		public static Card CthunCard(Card source = null)
		{
			if (source != null && source[GameTag.COLLECTION_RELATED_CARD_DATABASE_ID] is int related && related > 0 &&
			    Cards.FromAssetId(related) is Card named && SelfCondition.IsCthunCard(named))
				return named;
			return _cthun ??= Cards.All.Where(c => c.Collectible && SelfCondition.IsCthunCard(c)).OrderBy(c => c.AssetId).FirstOrDefault();
		}

		/// <summary>
		/// The player's "ideal C'Thun" (wiki C'Thun: 10-Attack checks pass if "Your ideal C'Thun has 10+ Attack, even without having
		/// C'Thun"): the C'Thun card's base stats plus the ritual totals. ATK, HEALTH or TAUNT; any other tag 0.
		/// </summary>
		public static int IdealCthun(Controller c, GameTag tag)
		{
			Card cthun = CthunCard();
			switch (tag)
			{
				case GameTag.ATK: return (cthun?.ATK ?? 0) + c[CTHUN_ATTACK_BUFF];
				case GameTag.HEALTH: return (cthun?.Health ?? 0) + c[CTHUN_HEALTH_BUFF];
				case GameTag.TAUNT: return c[CTHUN_TAUNT_BUFF] > 0 || cthun?.Taunt == true ? 1 : 0;
				default: return 0;
			}
		}

		/// <summary>
		/// "Give your C'Thun +X/+Y <i>(wherever it is)</i>" — one ritual, as live sends it since patch 27.2 (wiki C'Thun: "Cards that
		/// buff C'Thun now permanently increment its attack and health, instead of granting an enchantment"; live-20261001-1417,
		/// live-20261002-progression, 12 rituals): inside the cultist's block (OVERRIDE_HISTORY comes from RecordedBlockMeta) the
		/// player's CTHUN_ATTACK_BUFF / CTHUN_HEALTH_BUFF go to the new totals, the cultist gets FX_DATANUM_1 = the player entity and
		/// FX_DATANUM_3 = C'Thun's dbf, the ritual effect plays (from the cultist in a POWER block, from the player in a TRIGGER
		/// block, or as recorded), every C'Thun of the player — deck, hand, play, graveyard — gets plain base Attack / Health (no
		/// enchantment), the cards that follow C'Thun's gains (Power.FollowsCthunGains) get the same, and a display copy of C'Thun
		/// with the resulting stats is shown (history only). <paramref name="taunt"/>: Twilight Geomancer's "and Taunt".
		/// </summary>
		public static void CthunRitual(Controller c, IEntity source, int atk, int hp, bool taunt = false)
		{
			if (c == null || atk == 0 && hp == 0 && !taunt) return;
			Game g = c.Game;
			if (atk != 0) c[CTHUN_ATTACK_BUFF] += atk;
			if (hp != 0) c[CTHUN_HEALTH_BUFF] += hp;
			if (taunt && c[CTHUN_TAUNT_BUFF] == 0) c[CTHUN_TAUNT_BUFF] = 1;
			var cultist = source as IPlayable;
			Card cthun = CthunCard(cultist?.Card);
			if (g.History && cultist != null && !(cultist is Enchantment))
			{
				cultist[GameTag.FX_DATANUM_1] = c.Id;
				if (cthun != null) cultist[FX_DATANUM_3] = cthun.AssetId;
				string fx = Loader.SubSpells.Named(cultist.Card.Id, "CThun_Ritual") ?? CthunRitualFx;
				bool trigger = g.PowerHistory.CurrentEffectBlock?.BlockType == BlockType.TRIGGER;
				if (g.OpenSubSpell(cultist, fx, null, shownSource: trigger ? c.Id : 0))
					g.CloseSubSpells(cultist.Id);
			}
			foreach (IPlayable p in OwnedEverywhere(c).Where(p => SelfCondition.IsCthunCard(p.Card)).ToList())
				SyncCthun(p, atk, hp, taunt);
			if (atk != 0 || hp != 0)
				foreach (IPlayable p in c.DeckZone.GetAll().Concat(c.HandZone.GetAll()).Concat(c.BoardZone.GetAll())
					         .Where(p => p.Card.Power?.FollowsCthunGains == true && !(p is Minion m && m.Zone?.Type == Zone.PLAY && m.IsSilenced)).ToList())
					GainPermanentStats(p, atk, hp);
			if (g.History && cthun != null && cultist != null)
				CthunRitualDisplay(c, cultist, cthun);
		}

		/// <summary>
		/// A C'Thun card follows its owner's ritual totals: one that already does gets the new gain (+<paramref name="atk"/> /
		/// +<paramref name="hp"/>, Taunt); one that never did (made later, stolen, shuffled in) gets the whole totals (wiki C'Thun:
		/// "If a player uses Thoughtsteal to copy the opponent's C'Thun, the copy will benefit from the stealing player's C'Thun
		/// buffs"). C'Thun's own zone trigger (CopyCthun) calls it with no gain when it enters a hand or play.
		/// </summary>
		public static void SyncCthun(IPlayable p, int atk = 0, int hp = 0, bool taunt = false)
		{
			Controller c = p?.Controller;
			if (c == null) return;
			if (p[HsTag.CTHUN_FOLLOWING] == 0)
			{
				atk = c[CTHUN_ATTACK_BUFF];
				hp = c[CTHUN_HEALTH_BUFF];
				taunt = c[CTHUN_TAUNT_BUFF] > 0;
				p[HsTag.CTHUN_FOLLOWING] = 1;
			}
			GainPermanentStats(p, atk, hp);
			if (taunt && p is Minion m && !m.HasTaunt)
				m.HasTaunt = true;
		}

		/// <summary>
		/// Permanent base stats (no enchantment): the character's own Attack / Health raised as plain TAG_CHANGEs (live: C'Thun in
		/// hand, live1417 33475) and remembered (HsTag.PERMANENT_ATK_GAIN / PERMANENT_HEALTH_GAIN), so Silence and backward moves
		/// keep them (wiki C'Thun: "they do not disappear even if C'Thun is silenced, returned to the hand").
		/// </summary>
		public static void GainPermanentStats(IPlayable p, int atk, int hp)
		{
			if (!(p is Character ch) || atk == 0 && hp == 0) return;
			_ = ch.AttackDamage; // (fills the cached base values)
			_ = ch.BaseHealth;
			if (atk != 0) ch.AttackDamage = (ch._modifiedATK ?? ch.Card.ATK) + atk;
			if (hp != 0) ch.BaseHealth = (ch._modifiedHealth ?? ch.Card.Health) + hp;
			if (atk != 0) p[HsTag.PERMANENT_ATK_GAIN] += atk;
			if (hp != 0) p[HsTag.PERMANENT_HEALTH_GAIN] += hp;
		}

		/// <summary>After a backward move or a reset: the permanent gains back on the character's base stats.</summary>
		internal static void RestorePermanentStats(IPlayable p)
		{
			if (!(p is Character ch)) return;
			int atk = p[HsTag.PERMANENT_ATK_GAIN], hp = p[HsTag.PERMANENT_HEALTH_GAIN];
			if (atk != 0)
			{
				ch._modifiedATK = ch.Card.ATK + atk;
				((Entity)p)._data[GameTag.ATK] = ch.Card.ATK + atk;
			}
			if (hp != 0)
			{
				ch._modifiedHealth = ch.Card.Health + hp;
				((Entity)p)._data[GameTag.HEALTH] = ch.Card.Health + hp;
			}
		}

		private static IEnumerable<IPlayable> OwnedEverywhere(Controller c) =>
			c.DeckZone.GetAll().Concat(c.HandZone.GetAll()).Concat(c.BoardZone.GetAll()).Concat(c.GraveyardZone.GetAll());

		/// <summary>Live's ritual display (live1417 23768-23830, 25806-25848): a new C'Thun card set aside with the resulting stats,
		/// REVEALED, COPIED_FROM_ENTITY_ID = the player's C'Thun (else the last display), META HISTORY_TARGET_DONT_DUPLICATE_UNTIL_END,
		/// then out of the game. DOUBT: live put Twilight Elder's end-of-turn displays in the graveyard; all go out of the game here.</summary>
		private static void CthunRitualDisplay(Controller c, IPlayable cultist, Card cthun)
		{
			Game g = c.Game;
			IPlayable real = OwnedEverywhere(c).FirstOrDefault(p => SelfCondition.IsCthunCard(p.Card));
			int copiedFrom = real?.Id ?? c[HsTag.CTHUN_DISPLAY_COPY];
			var data = new EntityData
			{
				[GameTag.CREATOR] = cultist.Id,
				[GameTag.ATK] = cthun.ATK + c[CTHUN_ATTACK_BUFF],
				[GameTag.HEALTH] = cthun.Health + c[CTHUN_HEALTH_BUFF],
				[GameTag.ZONE] = (int)Zone.SETASIDE,
			};
			if (c[CTHUN_TAUNT_BUFF] > 0) data[GameTag.TAUNT] = 1;
			IPlayable display = Entity.FromCard(c, cthun, data);
			if (copiedFrom > 0) display[HsTag.COPIED_FROM_ENTITY_ID] = copiedFrom;
			display[GameTag.REVEALED] = 1;
			g.PowerHistory.Add(new PowerHistoryMetaData
				{ Type = MetaDataType.HISTORY_TARGET_DONT_DUPLICATE_UNTIL_END, Data = 0, Info = new List<int> { display.Id } });
			((Entity)display)._data[GameTag.ZONE] = (int)Zone.REMOVEDFROMGAME;
			g.PowerHistory.Add(PowerHistoryBuilder.TagChange(display.Id, GameTag.ZONE, (int)Zone.REMOVEDFROMGAME));
			c[HsTag.CTHUN_DISPLAY_COPY] = display.Id;
		}

		// =========================================================================================================== Shifting in hand

		/// <summary>
		/// "Each turn this is in your hand, transform it into …" (Shifter Zerus, Bandersmosh, Disguised K'thir, the Caverns of Time
		/// Imposters). Rule book (Advanced_rulebook.txt:743, 1554-1559): Shifter Zerus "is still the same Entity but has changed his
		/// Card ID, and but no outside Enchantments remain attached ... attaching the Enchantment 'Shifting' (OG_123e) to himself.
		/// This Enchantment is detached if Shifter Zerus enters play"; an untransformed one triggers as a Hand Trigger, the
		/// Shifting enchantment as a Play Trigger, at the Start of Turn Phase (Advanced_rulebook.txt:321).
		/// <para>Once a turn (HsTag.SHIFTED_ON_TURN; a shifter that became another shifter has both triggers) and only in the hand:
		/// TRANSFORMED_FROM_CARD = the shifter (its shifting enchantment's DEFINING_ENCHANTMENT, else the card it is named after —
		/// live: Shifting Scroll 43420, Bandersmosh 55643; the client's tooltip reads it: TooltipPanelManager, "Shifting adds
		/// Poisonous" for Durnholde Imposter), the in-hand CHANGE_ENTITY with its outside enchantments gone, then
		/// <paramref name="shifting"/> (its Enchant sets the SHIFTING_* tag, RemoveWhenPlayed, a TURN_START trigger calling this)
		/// and <paramref name="alsoGains"/> (kept on play: DRG_096e2, the Imposters' "&lt;id&gt;e2") attached again.</para>
		/// <paramref name="source"/>: the card (its own hand trigger) or the shifting enchantment (its trigger). Returns the card.
		/// </summary>
		public static IPlayable ShiftInHand(IEntity source, Func<IPlayable, Card> into, Card shifting, params Card[] alsoGains)
		{
			IPlayable me = source is Enchantment e ? e.Target as IPlayable : source as IPlayable;
			Game g = me?.Game;
			if (me == null || me.Zone?.Type != Zone.HAND || me[HsTag.SHIFTED_ON_TURN] == g.Turn) return null;
			Card card = into(me);
			if (card == null) return null;
			int shifter = ShifterDbf(me, shifting);
			if (shifter > 0) me[GameTag.TRANSFORMED_FROM_CARD] = shifter;
			IPlayable now = ChangeEntityBlock(me.Controller, me, card, true);
			if (now == null) return null;
			if (shifter > 0 && now[GameTag.TRANSFORMED_FROM_CARD] != shifter) now[GameTag.TRANSFORMED_FROM_CARD] = shifter;
			now[HsTag.SHIFTED_ON_TURN] = g.Turn;
			if (now.Zone?.Type == Zone.HAND)
			{
				if (shifting != null) AddEnchantmentBlock(g, shifting, now, now, 0, 0, 0);
				foreach (Card gain in alsoGains)
					if (gain != null) AddEnchantmentBlock(g, gain, now, now, 0, 0, 0);
			}
			return now;
		}

		private static readonly Regex EnchantmentStem = new Regex(@"^(.+?)e\d*$");

		private static int ShifterDbf(IPlayable me, Card shifting)
		{
			if (shifting != null && shifting[GameTag.DEFINING_ENCHANTMENT] > 0) return shifting[GameTag.DEFINING_ENCHANTMENT];
			if (shifting != null && EnchantmentStem.Match(shifting.Id) is { Success: true } m && Cards.TryFromId(m.Groups[1].Value) is Card named)
				return named.AssetId;
			return me[GameTag.TRANSFORMED_FROM_CARD] > 0 ? me[GameTag.TRANSFORMED_FROM_CARD] : me.Card.AssetId;
		}

		// =========================================================================================================== Go Dormant on death

		/// <summary>
		/// "Deathrattle: Go Dormant. Revive after N friendly X die" (The Rat King SW_323, Malorne GVG_035 / WON_011): a NEW entity of
		/// the dead minion's card in its slot, made Dormant with its counter enchantment <paramref name="counterId"/> (its
		/// SCORE_VALUE_1 is N: "Awaken after @ friendly Beasts die"); a full board: nothing. Its <see cref="AwakenAfterDeaths"/>
		/// trigger counts. Returns the Dormant minion, or null.
		/// </summary>
		public static Minion GoDormantOnDeath(Controller c, IEntity dead, string counterId)
		{
			if (c == null || c.BoardZone.IsFull || !(dead is IPlayable p) || p.Card.Type != CardType.MINION) return null;
			var revived = (Minion)Entity.FromCard(c, p.Card);
			SummonBlock.Invoke(c.Game, revived, (dead as Minion)?.LastBoardPosition ?? -1, dead);
			if (revived.Zone?.Type == Zone.PLAY) MakeDormant(revived, 0, counterId, revived);
			return revived.Zone?.Type == Zone.PLAY ? revived : null;
		}

		/// <summary>The trigger of a <see cref="GoDormantOnDeath"/> minion: while it is Dormant, each other friendly minion dying that
		/// <paramref name="counts"/> (null: any) is one step (AwakenProgress; it awakens at its counter's total).</summary>
		/// (WHIZBANG: <paramref name="source"/> ALL_MINIONS_EXCEPT_SELF for "After 7 minions die" — Corridor Sleeper, both sides.)
		public static Trigger AwakenAfterDeaths(Func<Minion, bool> counts = null, TriggerSource source = TriggerSource.MINIONS_EXCEPT_SELF) => new Trigger(TriggerType.DEATH)
		{
			TriggerSource = source,
			WhileDormant = true,
			Condition = counts == null ? null : new SelfCondition(p => p is Minion m && counts(m)),
			SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me && me[GameTag.DORMANT] > 0) AwakenProgress(me, 1); })
		};

		// =========================================================================================================== Choose One

		/// <summary>
		/// "Your next Choose One card this turn has both effects combined" (Cenarion Hold): the player's
		/// HsTag.CHOOSE_ONE_CARDS_BOTH_TURN = this turn (Controller.ChooseBothCards; spent by the next Choose One card's activation —
		/// played, cast by an effect or Cast When Drawn, as Disciple of Eonar — never by a Hero Power: wiki Cenarion Hold 28.6.0 "Fixed
		/// a bug where Cenarion Hold's effect could be used up by a Choose One Hero Power"). <paramref name="enchantmentId"/> on the
		/// player shows it (its Power: <see cref="NextChooseOneCardBothShownPower"/>). DOUBT: whether live also sets the client's
		/// CHOOSE_BOTH player tag (unrecorded, as Disciple of Eonar).
		/// </summary>
		public static void NextChooseOneCardBothThisTurn(Controller c, IPlayable source, string enchantmentId)
		{
			c[HsTag.CHOOSE_ONE_CARDS_BOTH_TURN] = c.Game.Turn;
			if (enchantmentId != null && Cards.TryFromId(enchantmentId) is Card shown)
				AddEnchantmentBlock(c.Game, shown, source, c, 0, 0, 0);
		}

		/// <summary>The Power of the enchantment that shows <see cref="NextChooseOneCardBothThisTurn"/> on the player (WON_015e "Cenari On
		/// Hold"): it leaves once spent (after a card of theirs is played) or at the end of the turn.</summary>
		public static Power NextChooseOneCardBothShownPower => new Power
		{
			Trigger = new MultiTrigger(
				new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Enchantment shown && c[HsTag.CHOOSE_ONE_CARDS_BOTH_TURN] != g.Turn) shown.Remove();
					})
				},
				new Trigger(TriggerType.TURN_END) { EitherTurn = true, SingleTask = RemoveEnchantmentTask.Task })
		};

		// =========================================================================================================== Secrets

		/// <summary>
		/// "Cast N random Secrets" as Disco at the End of Time does it live (live-20261006-secrets-generators 5703-5868): one at a
		/// time, a random Secret of the generation pool matching <paramref name="which"/> (any class) that the player can hold
		/// (CanHoldSecret: the one-copy rule; not one already put by this call) is PUT straight into the Secret zone — a FULL_ENTITY
		/// in SECRET (CREATOR / DISPLAYED_CREATOR = <paramref name="source"/>, client tag 3736 = 1..N), no PLAY / POWER block, no
		/// spell counters; EXHAUSTED when put up on its owner's turn (PutSecretIntoPlay). Stops when the zone is full or none is
		/// left. Returns the Secrets put up. (Generic.CastRandomSecrets casts them instead: Norgannon, The Nighthold.)
		/// </summary>
		public static List<Spell> PutRandomSecrets(Controller c, IEntity source, Func<Card, bool> which, int n)
		{
			Game g = c.Game;
			var put = new List<Spell>();
			for (int i = 0; i < n; i++)
			{
				if (c.SecretZone.IsFull) break;
				List<Card> pool = Cards.GenerationPool(g, c)
					.Where(x => x.IsSecret && x.Type == CardType.SPELL && (which == null || which(x)) && CanHoldSecret(c, x)).ToList();
				if (pool.Count == 0) break;
				var secret = (Spell)Entity.FromCard(c, pool[g.Random.Next(pool.Count)], new EntityData
				{
					[GameTag.CREATOR] = source.Id,
					[GameTag.DISPLAYED_CREATOR] = source.Id,
					[(GameTag)3736] = put.Count + 1,
				});
				if (!PutSecretIntoPlay(c, secret)) break;
				put.Add(secret);
			}
			return put;
		}

		/// <summary>
		/// "At the start of your turn, destroy them" (Disco at the End of Time): <paramref name="secrets"/> are marked
		/// (HsTag.DESTROY_SECRET_WITH = <paramref name="by"/>, the effect that put them up); the enchantment <paramref name="by"/>
		/// creates on the player (Power: <see cref="DestroyMarkedSecretsPower"/>) destroys them at its owner's next turn start.
		/// The rule state is on the Secrets (an AI copy of the game keeps it).
		/// </summary>
		public static void MarkSecretsForDestroy(IEnumerable<Spell> secrets, IEntity by)
		{
			foreach (Spell s in secrets)
				s[HsTag.DESTROY_SECRET_WITH] = by.Id;
		}

		/// <summary>
		/// The enchantment's turn-start effect (live 6632-6697, MAIN_START_TRIGGERS, its TRIGGER block index 0): an artificial
		/// history tile (its creator's card, out of the game) naming every Secret it marked — also those already triggered — then
		/// each one still up is destroyed without a reveal (LAST_AFFECTED_BY = the enchantment; no TARGET meta), then the
		/// enchantment goes to the graveyard.
		/// </summary>
		public static void DestroyMarkedSecrets(IEntity granter)
		{
			if (!(granter is Enchantment e)) return;
			Controller c = e.Controller;
			Game g = c.Game;
			int by = e.Creator?.Id ?? 0;
			var marked = by == 0 ? new List<Spell>()
				: g.IdEntityDic.Values.OfType<Spell>().Where(s => s[HsTag.DESTROY_SECRET_WITH] == by && s.Controller == c).OrderBy(s => s.Id).ToList();
			if (marked.Count > 0)
				ArtificialTriggerTile(c, e, e.Creator?.Card, marked.Select(s => s.Id));
			foreach (Spell s in marked)
			{
				s[HsTag.DESTROY_SECRET_WITH] = 0;
				if (s.Zone?.Type != Zone.SECRET) continue;
				NoteAffected(s, e.Id);
				DestroySecret(s, reveal: false);
			}
			e.RemoveToGraveyard();
		}

		/// <summary>The Power of the marking enchantment (WON_040e "End of the Disco"): <see cref="DestroyMarkedSecrets"/> at the start
		/// of its owner's turn.</summary>
		public static Power DestroyMarkedSecretsPower => new Power
		{
			Trigger = new Trigger(TriggerType.TURN_START)
			{
				SingleTask = new CustomTask((g, c, s, t, stack) => DestroyMarkedSecrets(s))
			}
		};

		/// <summary>
		/// An artificial history tile inside a trigger's block (live Disco 6633-6640, Jotun's draw-and-hold: Generic.CastDrawnCopy):
		/// a copy of <paramref name="card"/> (the granting card) in REMOVEDFROMGAME, CREATOR = <paramref name="granter"/>; META
		/// BEGIN_ARTIFICIAL_HISTORY_TRIGGER_TILE, OVERRIDE_HISTORY (it), HISTORY_TARGET (<paramref name="targets"/>), and — unless
		/// <paramref name="end"/> is false — END_ARTIFICIAL_HISTORY_TILE naming the game entity. History only.
		/// </summary>
		/// <summary>HsServer (2026-10-08): the trigger tile of a card's own effect at the end of its block — live Ohn'ahra (install 195600 9127):
		/// BEGIN_ARTIFICIAL_HISTORY_TRIGGER_TILE and OVERRIDE_HISTORY naming the card itself, HISTORY_TARGET = what it played, no END.</summary>
		public static void SelfTriggerTile(IPlayable source, IEnumerable<int> targets)
		{
			Game g = source?.Game;
			if (g == null || !g.History) return;
			g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.BEGIN_ARTIFICIAL_HISTORY_TRIGGER_TILE, Data = 0, Info = new List<int> { source.Id } });
			g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.OVERRIDE_HISTORY, Data = 0, Info = new List<int> { source.Id } });
			g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.HISTORY_TARGET, Data = 0, Info = targets?.ToList() ?? new List<int>() });
		}

		public static void ArtificialTriggerTile(Controller c, IEntity granter, Card card, IEnumerable<int> targets, bool end = true)
		{
			Game g = c.Game;
			if (!g.History) return;
			int tileId = 0;
			if (card != null)
				tileId = Entity.FromCard(c, card, new EntityData
				{
					[GameTag.CREATOR] = granter.Id,
					[GameTag.ZONE] = (int)Zone.REMOVEDFROMGAME,
				}).Id;
			g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.BEGIN_ARTIFICIAL_HISTORY_TRIGGER_TILE, Data = 0, Info = new List<int> { tileId } });
			if (tileId > 0)
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.OVERRIDE_HISTORY, Data = 0, Info = new List<int> { tileId } });
			g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.HISTORY_TARGET, Data = 0, Info = targets?.ToList() ?? new List<int>() });
			if (end)
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.END_ARTIFICIAL_HISTORY_TILE, Data = 0, Info = new List<int> { g.Id } });
		}

		// =========================================================================================================== Battlecries

		/// <summary>
		/// "Your next Battlecry this turn triggers twice" (Murmuring Elemental LOOT_517, Runi's Xenedar WON_053t3). Live
		/// (live-20261004-night/224614 3430-3517): <paramref name="playerEnchantment"/> on the player sets EXTRA_BATTLECRIES_BASE
		/// (PlayCard doubles Battlecries on Controller.ExtraBattlecry), <paramref name="sourceEnchantment"/> on the source sets
		/// CUSTOM_KEYWORD_EFFECT; both leave at the end of the turn (TRIGGER blocks, index 0), the player's also once a card of theirs
		/// with Battlecry other than its creator was played. Their Powers: <see cref="NextBattlecryTwicePlayerPower"/> /
		/// <see cref="NextBattlecryTwiceSourcePower"/>. With Brann both are the same flag: twice, not three times.
		/// </summary>
		public static void NextBattlecryTwice(Controller c, IPlayable source, string playerEnchantment, string sourceEnchantment)
		{
			Game g = c.Game;
			if (playerEnchantment != null) AddEnchantmentBlock(g, Cards.FromId(playerEnchantment), source, c, 0, 0, 0);
			if (sourceEnchantment != null && source != null && !(source is Enchantment))
				AddEnchantmentBlock(g, Cards.FromId(sourceEnchantment), source, source, 1, 0, 0);
		}

		/// <summary>The player's "next Battlecry twice" enchantment (LOOT_517e, WON_053te): EXTRA_BATTLECRIES_BASE (ADD: Brann's and
		/// another's don't cancel each other) until the end of the turn or a friendly Battlecry card other than its creator is played.</summary>
		public static Power NextBattlecryTwicePlayerPower => new Power
		{
			Aura = new Aura(AuraType.CONTROLLER, new Effect(GameTag.EXTRA_BATTLECRIES_BASE, EffectOperator.ADD, 1))
			{
				RemoveTrigger = (TriggerType.TURN_END, null),
			},
			Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
			{
				TriggerSource = TriggerSource.FRIENDLY,
				SingleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (s is Enchantment e && t != null && t.Card[GameTag.BATTLECRY] > 0 && !ReferenceEquals(t, e.Creator) && t.Id != e.Creator?.Id)
						e.Remove();
				})
			}
		};

		/// <summary>The source's "next Battlecry twice" enchantment (LOOT_517e2, WON_053t3e): CUSTOM_KEYWORD_EFFECT for this turn (a
		/// one-turn ADD of the script value 1: a one-turn SET can't be undone).</summary>
		public static Power NextBattlecryTwiceSourcePower => new Power
		{
			Enchant = new Enchant(new Effect(GameTag.CUSTOM_KEYWORD_EFFECT, EffectOperator.ADD, 0)) { UseScriptTag = true, IsOneTurnEffect = true }
		};

		// =========================================================================================================== Poisoned Blade

		/// <summary>
		/// "Your Hero Power gives this +1 Attack instead of replacing it" (Poisoned Blade AT_034 / WON_318). Wiki Poisoned Blade: "This
		/// weapon's ongoing effect specifically modifies Dagger Mastery, Poisoned Daggers, and Ancient Blades, preventing them from
		/// equipping a weapon to replace this one"; "This weapon's triggered effect triggers in the Inspire Phase after the player's
		/// Hero Power is used"; "After Patch 27.2.0.183876, only Hero Powers which would equip a weapon will grant this weapon +1
		/// Attack". The weapon's Power: Power.HeroPowerBuffInstead = <paramref name="enchantmentId"/> (WeaponTask skips a Hero Power's
		/// equip while it is equipped and marks the player, HsTag.HERO_POWER_EQUIP_REPLACED) and an Inspire trigger that spends the
		/// mark and gives the enchantment (its TRIGGER block, index 0: the client's PoisonedBlade_Throw_FX).
		/// </summary>
		public static Power HeroPowerBuffsWeapon(string enchantmentId) => new Power
		{
			HeroPowerBuffInstead = enchantmentId,
			Trigger = new Trigger(TriggerType.INSPIRE)
			{
				Condition = new SelfCondition(p => p.Controller[HsTag.HERO_POWER_EQUIP_REPLACED] > 0),
				SingleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Weapon w) || c[HsTag.HERO_POWER_EQUIP_REPLACED] == 0) return;
					c[HsTag.HERO_POWER_EQUIP_REPLACED] = 0;
					if (Cards.TryFromId(enchantmentId) is Card buff) AddEnchantmentBlock(g, buff, w, w, 0, 0, 0);
				})
			}
		};

		/// <summary>WeaponTask: does <paramref name="c"/>'s equipped weapon replace this Hero Power equip (Power.HeroPowerBuffInstead)?
		/// Marks the player when it does.</summary>
		internal static bool HeroPowerEquipReplaced(Controller c, IEntity source)
		{
			if (!c.ResolvingHeroPower || !(source is HeroPower) || !(c.Hero?.Weapon is Weapon held) ||
			    held.Card.Power?.HeroPowerBuffInstead == null || held.Zone?.Type == Zone.GRAVEYARD)
				return false;
			c[HsTag.HERO_POWER_EQUIP_REPLACED] = 1;
			return true;
		}

		// =========================================================================================================== Overload

		/// <summary>
		/// Overload owed by a player — the one place every "Overload: (N)" / "Overload you for (N)" goes through: OVERLOAD_OWED and
		/// OVERLOAD_THIS_GAME go up by <paramref name="amount"/>; nothing while the player can't be Overloaded this turn (Pebbly Page:
		/// HsTag.CANT_BE_OVERLOADED_TURN; wiki Overload: a countered Overload spell gives "not ... any Overload, and thus their
		/// Tunnel Troggs will not trigger"). Returns what was owed (0: no "whenever you Overload" triggers).
		/// </summary>
		public static int AddOverload(Controller c, int amount)
		{
			if (c == null || amount <= 0) return 0;
			if (CantBeOverloaded(c)) return 0;
			c.OverloadOwed += amount;
			c.OverloadThisGame += amount;
			return amount;
		}

		/// <summary>"You can't be Overloaded this turn" holds (Pebbly Page).</summary>
		public static bool CantBeOverloaded(Controller c) => c.Game.Turn > 0 && c[HsTag.CANT_BE_OVERLOADED_TURN] == c.Game.Turn;

		/// <summary>"You can't be Overloaded this turn" (Pebbly Page): the rule state on the player (HsTag.CANT_BE_OVERLOADED_TURN),
		/// <paramref name="enchantmentId"/> (WON_090e "Pebbled", TAG_ONE_TURN_EFFECT) on the player shows it. Overload already owed
		/// stays (DOUBT: prevention only).</summary>
		public static void CantBeOverloadedThisTurn(Controller c, IPlayable source, string enchantmentId)
		{
			c[HsTag.CANT_BE_OVERLOADED_TURN] = c.Game.Turn;
			if (enchantmentId != null && Cards.TryFromId(enchantmentId) is Card shown)
				AddEnchantmentBlock(c.Game, shown, source, c, 0, 0, 0);
		}

		// =========================================================================================================== Lifesteal

		/// <summary>"Your spells have Lifesteal" (Hallazeal the Ascended OG_209 / WON_336): a controller aura (Spell.HasLifeSteal; every
		/// damage its controller's spells deal heals that hero, friendly targets too — wiki Hallazeal: "triggers from all damage dealt
		/// by the controlling player's spells, even if that damage is dealt to friendly characters").</summary>
		public static Aura SpellsHaveLifestealAura => new Aura(AuraType.CONTROLLER, new Effect(HsTag.SPELLS_HAVE_LIFESTEAL, EffectOperator.ADD, 1));

		// =========================================================================================================== Random cards

		/// <summary>
		/// "a random card (from your opponent's class)" (Burgle, Undercity Huckster, Shadow Rager…): a random collectible card of the
		/// opponent's class (not the generating card); against a classless hero (many Dalaran Heist bosses) The Coin — wiki Burgle:
		/// "Playing Burgle against neutral heroes will generate 2 copies of The Coin".
		/// </summary>
		public static Card OpponentClassCard(Game g, Controller c)
		{
			CardClass cls = c.Opponent.HeroClass;
			if (cls != CardClass.NEUTRAL && cls != CardClass.INVALID)
			{
				Card self = Cards.GeneratingCard(g);
				var pool = Cards.GenerationPool(g, c).Where(x => x.Id != self?.Id && x.HasClass(cls) && !x.HasClass(CardClass.NEUTRAL)).ToList();
				if (pool.Count > 0) return pool[g.Random.Next(pool.Count)];
			}
			return Cards.FromId("GAME_005");
		}

		private static List<Card> _spareParts;

		/// <summary>The Spare Parts (GvG's PART_001…PART_007 spells: the client names them PART_; no client tag marks them — SPARE_PART
		/// 388 is only a reference on the cards that make them).</summary>
		public static IReadOnlyList<Card> SpareParts => _spareParts ??= Cards.All
			.Where(c => c.Id.StartsWith("PART_", StringComparison.Ordinal) && c.Type == CardType.SPELL).OrderBy(c => c.Id, StringComparer.Ordinal).ToList();

		/// <summary>A random Spare Part (independent picks: repeats possible).</summary>
		public static Card RandomSparePart(Game g) => SpareParts.Count == 0 ? null : SpareParts[g.Random.Next(SpareParts.Count)];

		// =========================================================================================================== Deck copies

		/// <summary>
		/// "Summon copies of N &lt;cards&gt; in your deck" (Trial of the Jormungars WON_028; Crane Game TOY_884): N different deck cards
		/// matching <paramref name="which"/> picked at random (a hidden zone), each summoned as a copy (Generic.Copy: deck → play keeps
		/// its enchantments; the deck is unchanged) while the board has room. Returns the copies.
		/// </summary>
		public static List<Minion> SummonCopiesFromDeck(Controller c, IEntity source, Func<IPlayable, bool> which, int n)
		{
			Game g = c.Game;
			var pool = c.DeckZone.GetAll().Where(p => p.Card.Type == CardType.MINION && (which == null || which(p))).ToList();
			var copies = new List<Minion>();
			for (int i = 0; i < n && pool.Count > 0; i++)
			{
				if (c.BoardZone.IsFull) break;
				IPlayable pick = pool[g.Random.Next(pool.Count)];
				pool.Remove(pick);
				if (Copy(c, source, pick, Zone.PLAY) is Minion m) copies.Add(m);
			}
			return copies;
		}
	}
}
