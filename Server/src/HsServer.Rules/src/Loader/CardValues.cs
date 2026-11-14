// HsServer addition (not upstream SabberStone): editable card numbers.
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SabberStoneCore.Loader
{
	/// <summary>
	/// The numbers in HsServer's card scripts (damage, healing, buffs, amounts), by card id and name, so they can be edited
	/// without touching code (a future in-game card editor). A script asks <c>CardValues.Get("CS3_022", "damage", 2)</c>:
	/// the default is the card's current value, an override from <see cref="OverridesFile"/> replaces it. Every value asked
	/// for is recorded in <see cref="Defaults"/>, which lists everything editable.
	/// Scripts are built when the cards load, so overrides apply from the next start.
	/// </summary>
	public static class CardValues
	{
		/// <summary>JSON: { "CS3_022": { "damage": 3 }, ... }. Set before the cards load; missing file = defaults.</summary>
		public static string OverridesFile { get; set; }

		private static Dictionary<string, Dictionary<string, int>> _overrides;
		private static readonly Dictionary<(string Card, string Name), int> _defaults = new Dictionary<(string, string), int>();

		/// <summary>Every editable value: (card id, name) → default.</summary>
		public static IReadOnlyDictionary<(string Card, string Name), int> Defaults => _defaults;

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
			return _overrides.TryGetValue(cardId, out var values) && values.TryGetValue(name, out int value) ? value : defaultValue;
		}
	}
}
