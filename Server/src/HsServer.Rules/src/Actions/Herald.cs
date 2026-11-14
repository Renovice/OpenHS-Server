// HsServer addition (not upstream SabberStone): Herald (CATACLYSM, set 1980). Data-driven: the Herald Colossals are the COLOSSAL cards
// of the Herald family (client 4538), the Soldiers the cards of the same class whose text says "Herald twice to upgrade" and that are
// no limb. Rule state in the client's own player tags (HERALD_COLOSSAL_CLASS / _AMOUNT).
using System;
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>Client player tags: the Herald counter (4327), the class whose Colossal the player's Heralds use (4534); card tags:
		/// HERALD (4309), the Herald family (4538).</summary>
		public const GameTag HERALD_COLOSSAL_AMOUNT = (GameTag)4327, HERALD_COLOSSAL_CLASS = (GameTag)4534, HERALD = (GameTag)4309,
			HERALD_FAMILY = (GameTag)4538;

		private static Dictionary<int, (Card Soldier, Card Colossal)> _heraldClasses;

		/// <summary>class → its Soldier and Colossal (the Herald family's COLOSSAL cards; the Soldier: same class, "Herald</b> twice to
		/// upgrade" in its text, a minion that is no limb).</summary>
		private static Dictionary<int, (Card Soldier, Card Colossal)> HeraldClasses
		{
			get
			{
				if (_heraldClasses != null) return _heraldClasses;
				var map = new Dictionary<int, (Card, Card)>();
				foreach (Card colossal in Cards.All.Where(x => x[Colossal] > 0 && x[HERALD_FAMILY] > 0 && x[GameTag.COLLECTIBLE] > 0).OrderBy(x => x.Id))
				{
					int cls = colossal[GameTag.CLASS];
					Card soldier = Cards.All.Where(x => x.Type == CardType.MINION && x[GameTag.CLASS] == cls && x[ColossalLimb] == 0 &&
					                                  x.Text?.Contains("Herald</b> twice to upgrade") == true).OrderBy(x => x.Id).FirstOrDefault();
					if (soldier != null && !map.ContainsKey(cls)) map[cls] = (soldier, colossal);
				}
				return _heraldClasses = map;
			}
		}

		/// <summary>Is <paramref name="card"/> upgraded by Heralds (a Soldier or a Herald Colossal's limb: "Herald twice to upgrade")?</summary>
		public static bool HeraldUpgraded(Card card) => card?.Text?.Contains("Herald</b> twice to upgrade") == true;

		/// <summary>
		/// The class whose Colossal a Herald uses (ClayByte, Blizzard, quoted on wiki Herald: "If you're a herald class, then that's the
		/// colossal it will choose. Otherwise, if the Herald card has a class (and it's not one of the generic Herald cards like Envoy of
		/// the End), that's the colossal it will choose. Otherwise, if your opponent has a Herald class"); 0: none (doubt — "something
		/// else happens").
		/// </summary>
		public static int HeraldClass(Controller c, Card heraldCard)
		{
			var classes = HeraldClasses;
			if (classes.ContainsKey((int)c.HeroClass)) return (int)c.HeroClass;
			int own = heraldCard?[GameTag.CLASS] ?? 0;
			if (heraldCard != null && heraldCard[GameTag.MULTIPLE_CLASSES] == 0 && classes.ContainsKey(own)) return own;
			if (classes.ContainsKey((int)c.Opponent.HeroClass)) return (int)c.Opponent.HeroClass;
			return 0;
		}

		/// <summary>The Herald upgrade (wiki: "doubling their stats and effects with two Heralds and quadrupling them after four").</summary>
		public static int HeraldScale(Controller c) => c[HERALD_COLOSSAL_AMOUNT] >= 4 ? 4 : c[HERALD_COLOSSAL_AMOUNT] >= 2 ? 2 : 1;

		/// <summary>A new minion of <paramref name="card"/> for <paramref name="c"/>, with the Herald upgrade when the card takes one (its
		/// Attack, Health, Cost and number ×1/×2/×4 as base stats; live 73157: the 3rd Soldier made 2/4 cost 2 with NUM_1 2).</summary>
		internal static Minion HeraldScaled(Controller c, Card card, EntityData data = null)
		{
			data ??= new EntityData();
			int scale = HeraldUpgraded(card) ? HeraldScale(c) : 1;
			if (scale > 1)
			{
				data[GameTag.ATK] = card.ATK * scale;
				data[GameTag.HEALTH] = card.Health * scale;
				data[GameTag.COST] = card.Cost * scale;
				data[GameTag.TAG_SCRIPT_DATA_NUM_1] = card[GameTag.TAG_SCRIPT_DATA_NUM_1] * scale;
			}
			return (Minion)Entity.FromCard(c, card, data);
		}

		private static string _heraldSpawnFx, _heraldBuffFx;

		/// <summary>The Herald keyword's effects (every Herald card's recorded CATAFX_Herald_OverrideSpawn_Super / CATAFX_Herald_Buff).</summary>
		private static string HeraldFx(Card card, string part, ref string cached)
		{
			string own = Loader.SubSpells.FindNamed(card.Id, part);
			if (own != null) return own;
			if (cached != null) return cached;
			foreach (Card other in Cards.All.Where(x => x[HERALD] > 0).OrderBy(x => x.Id))
				if (Loader.SubSpells.FindNamed(other.Id, part) is string found)
					return cached = found;
			return null;
		}

		/// <summary>Game start: each player of a Herald class shows it (live 67958 / 236121: HERALD_COLOSSAL_CLASS at BEGIN_MULLIGAN).</summary>
		public static void HeraldGameStart(Game g)
		{
			foreach (Controller p in new[] { g.Player1, g.Player2 })
				if (HeraldClasses.ContainsKey((int)p.HeroClass) && p[HERALD_COLOSSAL_CLASS] == 0)
					p[HERALD_COLOSSAL_CLASS] = (int)p.HeroClass;
		}

		/// <summary>
		/// Herald (wiki Herald: "a Soldier minion with the stats and effects of the appendage from the class's synergistic Colossal minion is
		/// summoned"; "Upgrading only happens after the Soldier has been summoned"; "Heralding still summons the Soldier even if the player
		/// does not include the referenced Colossal"). Live (darkmoon 64839, vfx 72797-73300 / 260288): inside the card's block the Soldier
		/// comes in CATAFX_Herald_OverrideSpawn_Super (Source = the card) at the summon rule's place, USE_ALTERNATE_CARD_TEXT 2 on it, an
		/// empty CATAFX_Herald_Buff, then the player's HERALD_COLOSSAL_AMOUNT + 1 and HISTORY_TARGET the Soldier. A full board: no Soldier,
		/// the count still rises (doubt). Returns the Soldier (or null).
		/// </summary>
		public static Minion Herald(Controller c, IPlayable source)
		{
			Game g = c.Game;
			int cls = HeraldClass(c, source?.Card);
			if (cls == 0 || !HeraldClasses.TryGetValue(cls, out var pair)) return null;
			if (c[HERALD_COLOSSAL_CLASS] == 0) c[HERALD_COLOSSAL_CLASS] = cls;
			Minion soldier = null;
			if (!c.BoardZone.IsFull)
			{
				soldier = HeraldScaled(c, pair.Soldier, new EntityData { { GameTag.CREATOR, source.Id }, { GameTag.CREATOR_DBID, source.Card.AssetId } });
				bool fx = g.History && g.OpenSubSpell(source, HeraldFx(source.Card, "Herald_OverrideSpawn", ref _heraldSpawnFx), null);
				SummonBlock.Invoke(g, soldier, -1, source);
				if (soldier.Zone?.Type == Zone.PLAY) soldier[GameTag.USE_ALTERNATE_CARD_TEXT] = 2;
				else soldier = null;
				if (fx) g.CloseSubSpells(source.Id);
			}
			if (g.History && g.OpenSubSpell(source, HeraldFx(source.Card, "Herald_Buff", ref _heraldBuffFx), null, shownSource: Game.NoSubSpellSource))
				g.CloseSubSpells(source.Id);
			c[HERALD_COLOSSAL_AMOUNT] = c[HERALD_COLOSSAL_AMOUNT] + 1;
			if (soldier != null && g.History)
				g.PowerHistory.Add(new Kettle.PowerHistoryMetaData { Type = MetaDataType.HISTORY_TARGET, Data = 0, Info = new List<int> { soldier.Id } });
			return soldier;
		}
	}
}
