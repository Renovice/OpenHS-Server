// HsServer addition (not upstream SabberStone): editable card numbers.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SabberStoneCore.Loader
{
	/// <summary>
	/// The numbers in HsServer's card scripts (damage, healing, buffs, amounts), by card id and name, so they can be edited
	/// without touching code (a future in-game card editor). A script asks <c>CardValues.Get("CS3_022", "damage", 2)</c>:
	/// the default is the card's value when it was scripted, an override from <see cref="OverridesFile"/> replaces it. Every value
	/// asked for is recorded in <see cref="Defaults"/>, which lists everything editable.
	/// Scripts are built when the cards load, so overrides apply from the next start.
	///
	/// HsServer (2026-11-20, user: "the DB should be auto variable"): a value follows the card's CURRENT client text. The baseline
	/// (<see cref="BaselineFile"/>, written by `HsServer cardvalues baseline`) holds, per value, the text it was scripted against and
	/// which number of that text it is. When a patch rewords the card and its text keeps its shape (the same words, only numbers
	/// changed), the value is read from the same place in the new text; when the shape changed, or the number was never placed (it
	/// appears twice in the text, or not at all), the scripted value stays and the card is listed in <see cref="NeedsReview"/> (the
	/// `patchreview` / `cardvalues` reports). Rule changes in code are never automatic.
	/// </summary>
	public static class CardValues
	{
		/// <summary>JSON: { "CS3_022": { "damage": 3 }, ... }. Set before the cards load; missing file = defaults.</summary>
		public static string OverridesFile { get; set; }

		/// <summary>The baseline TSV (card, name, scripted value, number index, text). Set before the cards load; missing file or
		/// null: the scripted values as they are.</summary>
		public static string BaselineFile { get; set; }

		/// <summary>The card's current text (default: the client DB's).</summary>
		public static System.Func<string, string> CurrentText { get; set; } = ReprintCards.CurrentClientText;

		private static Dictionary<string, Dictionary<string, int>> _overrides;
		private static Dictionary<(string Card, string Name), (int Index, string Text)> _baseline;
		private static readonly Dictionary<(string Card, string Name), int> _defaults = new Dictionary<(string, string), int>();
		private static readonly Dictionary<(string Card, string Name), (int Scripted, int Now)> _followed = new Dictionary<(string, string), (int, int)>();
		private static readonly HashSet<(string Card, string Name)> _review = new HashSet<(string, string)>();

		/// <summary>Every editable value: (card id, name) → scripted default.</summary>
		public static IReadOnlyDictionary<(string Card, string Name), int> Defaults => _defaults;

		/// <summary>Values that followed a reworded text: (card, name) → (scripted, now).</summary>
		public static IReadOnlyDictionary<(string Card, string Name), (int Scripted, int Now)> Followed => _followed;

		/// <summary>Values whose card text changed in a way the baseline can't map: their script needs a look.</summary>
		public static IReadOnlyCollection<(string Card, string Name)> NeedsReview => _review;

		public static int Get(string cardId, string name, int defaultValue)
		{
			_defaults[(cardId, name)] = defaultValue;
			if (_overrides == null)
			{
				_overrides = new Dictionary<string, Dictionary<string, int>>();
				if (OverridesFile != null && File.Exists(OverridesFile))
					_overrides = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(File.ReadAllText(OverridesFile))
					             ?? _overrides;
			}
			if (_overrides.TryGetValue(cardId, out var values) && values.TryGetValue(name, out int value)) return value;
			return FromText(cardId, name, defaultValue);
		}

		/// <summary>The value from the card's current text (see the class summary), else the scripted one.</summary>
		private static int FromText(string cardId, string name, int scripted)
		{
			LoadBaseline();
			if (!_baseline.TryGetValue((cardId, name), out var b)) return scripted;
			string now = Plain(CurrentText?.Invoke(cardId));
			if (now == null || now == b.Text) return scripted;
			if (b.Index < 0 || Shape(now) != Shape(b.Text)) { _review.Add((cardId, name)); return scripted; }
			var numbers = Numbers(now);
			if (b.Index >= numbers.Count) { _review.Add((cardId, name)); return scripted; }
			int followed = numbers[b.Index];
			if (followed != scripted) _followed[(cardId, name)] = (scripted, followed);
			return followed;
		}

		/// <summary>Reads the baseline again on the next value asked (a tool or test that changes <see cref="BaselineFile"/>).</summary>
		public static void ReloadBaseline()
		{
			_baseline = null;
			_followed.Clear();
			_review.Clear();
		}

		private static void LoadBaseline()
		{
			if (_baseline != null) return;
			_baseline = new Dictionary<(string, string), (int, string)>();
			if (BaselineFile == null || !File.Exists(BaselineFile)) return;
			foreach (string line in File.ReadLines(BaselineFile).Skip(1))
			{
				string[] f = line.Split('\t');
				if (f.Length >= 5 && int.TryParse(f[3], out int index)) _baseline[(f[0], f[1])] = (index, f[4]);
			}
		}

		/// <summary>The baseline rows for the values asked so far against the current texts (`HsServer cardvalues baseline`): each
		/// value's place among its text's numbers (-1: it appears twice, or not at all — never followed).</summary>
		public static IEnumerable<string> BaselineRows()
		{
			yield return "card\tname\tvalue\tindex\ttext";
			foreach (var ((card, name), value) in _defaults.OrderBy(k => k.Key.Card, System.StringComparer.Ordinal).ThenBy(k => k.Key.Name, System.StringComparer.Ordinal))
			{
				string text = Plain(CurrentText?.Invoke(card));
				if (text == null) continue;
				var numbers = Numbers(text);
				int index = numbers.Count(n => n == value) == 1 ? numbers.IndexOf(value) : -1;
				yield return $"{card}\t{name}\t{value}\t{index}\t{text}";
			}
		}

		/// <summary>The text without markup, card-text placeholders ({0}) and spacing differences.</summary>
		internal static string Plain(string text) => text == null ? null
			: Regex.Replace(Regex.Replace(text, @"<[^>]*>|\[x\]|\{\d+\}|&", " "), @"[\s_]+", " ").Trim();

		private static List<int> Numbers(string text) => Regex.Matches(text, @"\d+").Select(m => int.Parse(m.Value)).ToList();

		private static string Shape(string text) => Regex.Replace(text, @"\d+", "#");
	}
}
