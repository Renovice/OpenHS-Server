// HsServer addition (not upstream SabberStone): what the server sends for each minion that enters play Dormant.
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace SabberStoneCore.Loader
{
	/// <summary>
	/// HsServer: per Dormant minion (client tag 1652), its counter enchantment (the "awaken condition" enchantment whose
	/// SCORE_VALUE_1 is the total, shown by the client as "Awaken in @"), and the effect indexes of its own TRIGGER blocks
	/// that make it Dormant and that run "When this awakens" — live per card, not in the client data (DormantCards.tsv,
	/// RESEARCH/vfx/tools/dormant_cards.py). Unrecorded minions get the generator's rule.
	/// </summary>
	public static class DormantCards
	{
		public sealed class Entry
		{
			public string Counter;
			public int Total, EnterIndex, EnterKeyword, AwakenIndex;
		}

		private static Dictionary<string, Entry> _map;

		/// <summary>The minion's entry (a Core / Legacy reprint and its original share it), or null.</summary>
		public static Entry Of(string cardId)
		{
			if (string.IsNullOrEmpty(cardId)) return null;
			Dictionary<string, Entry> map = Map;
			if (map.TryGetValue(cardId, out Entry e)) return e;
			foreach (string prefix in new[] { "CORE_", "LEG_", "VAN_" })
				if (cardId.StartsWith(prefix, System.StringComparison.Ordinal) && map.TryGetValue(cardId.Substring(prefix.Length), out e))
					return e;
			return ReprintCards.CanonicalOf.TryGetValue(cardId, out string original) && map.TryGetValue(original, out e) ? e : null;
		}

		private static Dictionary<string, Entry> Map
		{
			get
			{
				if (_map != null) return _map;
				var map = new Dictionary<string, Entry>();
				Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SabberStoneCore.Resources.DormantCards.tsv");
				if (stream != null)
					using (var reader = new StreamReader(stream))
					{
						string line;
						while ((line = reader.ReadLine()) != null)
						{
							if (line.Length == 0 || line[0] == '#') continue;
							string[] p = line.Split('\t');
							if (p.Length < 6) continue;
							int.TryParse(p[2], out int total);
							int.TryParse(p[3], out int enter);
							int.TryParse(p[4], out int keyword);
							int.TryParse(p[5], out int awaken);
							map[p[0]] = new Entry { Counter = p[1], Total = total, EnterIndex = enter, EnterKeyword = keyword, AwakenIndex = awaken };
						}
					}
				return _map = map;
			}
		}
	}
}
