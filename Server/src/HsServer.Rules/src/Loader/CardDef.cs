using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;

namespace SabberStoneCore.src.Loader
{
	public class CardDef
	{
		public Dictionary<PlayReq, int> PlayReqs { get; private set; }

		public string[] Entourage { get; private set; }

		public Power Power { get; private set; }

		/// <summary>HsServer: this card plays exactly like <see cref="PlaysAs"/>'s definition (resolved after every set is loaded:
		/// CardDefs). For a reworded reprint whose old text means the same (Vanilla "Enrage: +5 Attack" = "+5 Attack while
		/// damaged"); a card script says so, card by card.</summary>
		public string PlaysAs { get; private set; }

		/// <summary>HsServer: a card that plays exactly like <paramref name="cardId"/> (see <see cref="PlaysAs"/>).</summary>
		public static CardDef Like(string cardId) => new CardDef { PlaysAs = cardId };

		public CardDef()
		{
		}
		public CardDef(Dictionary<PlayReq, int> playReq)
		{
			PlayReqs = playReq;
		}

		public CardDef(Power power)
		{
			Power = power;
		}

		public CardDef(string[] entourage, Power power)
		{
			Entourage = entourage;
			Power = power;
		}

		public CardDef(Dictionary<PlayReq, int> playReq, Power power)
		{
			Entourage = null;
			PlayReqs = playReq;
			Power = power;
		}

		public CardDef(string[] entourage, Dictionary<PlayReq, int> playReq, Power power)
		{
			Entourage = entourage;
			PlayReqs = playReq;
			Power = power;
		}
	}
}
