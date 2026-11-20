using System.Reflection;
using System.Reflection.Emit;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using Type = System.Type;

namespace HsServer.Tools;

/// <summary>
/// `livecheck`, part 4: the engine's own counters (HsTag, GameTag ≥ 990000: "cast a Nature spell while holding this", "Discovered
/// this turn" …). Live never sends them, so a rebuilt position has only those the rebuild fills from the recorded history
/// (<see cref="FilledCounters"/>). Which counters a card's script reads is found in its compiled code: the IL of every delegate of
/// its Power (custom tasks, conditions, triggers) and of the engine methods they call, for HsTag constants that are read (a getter
/// follows them). A difference on an action where a card that acted reads a counter the rebuild couldn't fill is UNSURE, with the
/// counters named — not a rules difference until that counter is rebuilt.
/// </summary>
internal static partial class LiveCheck
{
    /// <summary>Counters the rebuild sets from live's history (Rebuild): they don't make an action unsure.</summary>
    private static readonly HashSet<int> FilledCounters = new()
    {
        (int)HsTag.OWN_TURNS_STARTED,
        (int)HsTag.ADDED_TO_HAND_TURN, (int)HsTag.HELD_SINCE_PLAY, // (from live's history: FillHandCounters)
        (int)HsTag.QUICKDRAW_PLAY, // (set at the play from ADDED_TO_HAND_TURN)
        (int)HsTag.HERO_DAMAGE_THIS_TURN, // (FillHeroDamage)
        (int)HsTag.FORGES_THIS_GAME, // (live's FORGED cards)
    };

    private static HashSet<int> effectSetTags;

    /// <summary>HsTags an aura or an enchantment sets as its effect (Commander Rhyssa's SECRETS_TRIGGER_TWICE): the rebuild's auras and
    /// re-added enchantments set them again, they aren't history.</summary>
    private static HashSet<int> EffectSetTags()
    {
        if (effectSetTags != null) return effectSetTags;
        var tags = new HashSet<int>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Walk(object o, int depth)
        {
            if (o == null || depth > 8) return;
            if (o is SabberStoneCore.Enchants.Effect e)
            {
                if ((int)e.Tag >= 990000) tags.Add((int)e.Tag);
                return;
            }
            Type type = o.GetType();
            if (type.IsPrimitive || type.IsEnum || o is string || o is Delegate || type.IsValueType || !seen.Add(o)) return;
            if (o is Card || o is SabberStoneCore.Model.Game || o is SabberStoneCore.Model.Entities.IEntity) return;
            if (o is System.Collections.IEnumerable list && o is not System.Collections.IDictionary)
            {
                foreach (object x in list) Walk(x, depth + 1);
                return;
            }
            if (type.Assembly != EngineAssembly) return;
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
                foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (!f.FieldType.IsPrimitive && !f.FieldType.IsEnum && f.FieldType != typeof(string))
                        try { Walk(f.GetValue(o), depth + 1); } catch (Exception) { }
        }
        foreach (Card c in Cards.All)
            if (c.Power is { } power)
            {
                Walk(power.Aura, 0);
                Walk(power.Enchant, 0);
            }
        return effectSetTags = tags;
    }

    private static Dictionary<int, string> counterNames;

    internal static string CounterName(int tag)
    {
        if (counterNames == null)
        {
            var names = new Dictionary<int, string>();
            foreach (FieldInfo f in typeof(HsTag).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (f.FieldType == typeof(GameTag) && f.GetValue(null) is GameTag t && (int)t >= 990000 && !names.ContainsKey((int)t))
                    names[(int)t] = f.Name;
            counterNames = names;
        }
        return counterNames.TryGetValue(tag, out string n) ? n : tag.ToString();
    }

    private static readonly Dictionary<string, HashSet<int>> countersRead = new();

    /// <summary>The engine counters (HsTag) <paramref name="card"/>'s script reads, the ones the rebuild fills left out.</summary>
    internal static HashSet<int> UnfilledCountersRead(Card card)
    {
        if (card?.Power == null) return new HashSet<int>();
        lock (countersRead)
        {
            if (!countersRead.TryGetValue(card.Id, out HashSet<int> found))
            {
                found = new HashSet<int>();
                var methods = new HashSet<MethodBase>();
                var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
                CollectDelegates(card.Power, 0, seen, methods);
                var scanned = new HashSet<MethodBase>();
                foreach (MethodBase m in methods) ScanIl(m, 0, scanned, found);
                countersRead[card.Id] = found;
            }
            HashSet<int> state = EffectSetTags();
            return found.Where(t => !FilledCounters.Contains(t) && !state.Contains(t)).ToHashSet();
        }
    }

    private static readonly Assembly EngineAssembly = typeof(Card).Assembly;

    /// <summary>Every delegate's method reachable from a Power (its tasks, triggers, auras, conditions), not into game objects.</summary>
    private static void CollectDelegates(object o, int depth, HashSet<object> seen, HashSet<MethodBase> methods)
    {
        if (o == null || depth > 14) return;
        Type type = o.GetType();
        if (type.IsPrimitive || type.IsEnum || o is string || type.IsValueType && type.Assembly != EngineAssembly) return;
        if (!seen.Add(o)) return;
        if (o is Delegate d)
        {
            foreach (Delegate one in d.GetInvocationList())
            {
                methods.Add(one.Method);
                if (one.Target != null) CollectDelegates(one.Target, depth + 1, seen, methods); // (a closure's captured tasks)
            }
            return;
        }
        if (o is Card || o is SabberStoneCore.Model.Game || o is SabberStoneCore.Model.Entities.IEntity) return;
        if (o is System.Collections.IEnumerable list && o is not System.Collections.IDictionary)
        {
            foreach (object x in list) CollectDelegates(x, depth + 1, seen, methods);
            return;
        }
        if (type.Assembly != EngineAssembly && !type.Name.Contains("DisplayClass", StringComparison.Ordinal)) return;
        for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (f.FieldType.IsPrimitive || f.FieldType.IsEnum || f.FieldType == typeof(string)) continue;
                try { CollectDelegates(f.GetValue(o), depth + 1, seen, methods); } catch (Exception) { }
            }
    }

    private static readonly Dictionary<short, OpCode> OpCodesByValue =
        typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (OpCode)f.GetValue(null)).ToDictionary(o => o.Value);

    /// <summary>A method the scan follows from a card's script: the card sets' own helpers (WhileHolding …), and a small engine
    /// helper that reads a counter (Generic.DiscoveredThisTurn) — not the engine's general machinery (summon, damage: rules that
    /// read their own state for every card).</summary>
    private static bool Followed(MethodBase callee, int depth)
    {
        if (callee.Module.Assembly != EngineAssembly) return false;
        string ns = callee.DeclaringType?.Namespace ?? "";
        if (ns.StartsWith("SabberStoneCore.CardSets", StringComparison.Ordinal)) return depth < 3;
        if (!callee.IsStatic || depth >= 2 || !ns.StartsWith("SabberStoneCore.Actions", StringComparison.Ordinal)) return false;
        try { return (callee.GetMethodBody()?.GetILAsByteArray()?.Length ?? int.MaxValue) < 160; } catch (Exception) { return false; }
    }

    /// <summary>HsTag constants read in a method (a getter call follows the constant), and in the helpers it calls (Followed).</summary>
    private static void ScanIl(MethodBase m, int depth, HashSet<MethodBase> scanned, HashSet<int> found)
    {
        if (m == null || !scanned.Add(m)) return;
        byte[] il;
        try { il = m.GetMethodBody()?.GetILAsByteArray(); } catch (Exception) { return; }
        if (il == null) return;
        Type[] typeArgs = m.DeclaringType?.IsGenericType == true ? m.DeclaringType.GetGenericArguments() : null;
        Type[] methodArgs = m.IsGenericMethod ? m.GetGenericArguments() : null;
        var pending = new List<(int Tag, int Left)>();
        int pos = 0;
        while (pos < il.Length)
        {
            OpCode op;
            if (il[pos] == 0xFE && pos + 1 < il.Length) { op = OpCodesByValue.GetValueOrDefault(unchecked((short)(0xFE00 | il[pos + 1]))); pos += 2; }
            else { op = OpCodesByValue.GetValueOrDefault(il[pos]); pos += 1; }
            int operand = 0;
            switch (op.OperandType)
            {
                case OperandType.InlineNone: break;
                case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: pos += 1; break;
                case OperandType.InlineVar: pos += 2; break;
                case OperandType.InlineI8: case OperandType.InlineR: pos += 8; break;
                case OperandType.InlineSwitch:
                    int n = BitConverter.ToInt32(il, pos);
                    pos += 4 + 4 * n;
                    break;
                default:
                    operand = BitConverter.ToInt32(il, pos);
                    pos += 4;
                    break;
            }
            if (op == OpCodes.Ldc_I4 && operand >= 990000 && operand < 1000000) pending.Add((operand, 4));
            if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj || op == OpCodes.Ldftn || op == OpCodes.Ldvirtftn)
            {
                MethodBase callee = null;
                try { callee = m.Module.ResolveMethod(operand, typeArgs, methodArgs); } catch (Exception) { }
                if (callee != null)
                {
                    // (a constant then a getter: read; a setter only writes it)
                    if (pending.Count > 0 && callee.Name != "set_Item")
                        foreach (var p in pending) found.Add(p.Tag);
                    if (pending.Count > 0) pending.Clear();
                    if (Followed(callee, depth)) ScanIl(callee, depth + 1, scanned, found);
                }
            }
            else if (pending.Count > 0)
            {
                for (int i = pending.Count - 1; i >= 0; i--)
                    if (pending[i].Left <= 1) pending.RemoveAt(i);
                    else pending[i] = (pending[i].Tag, pending[i].Left - 1);
            }
        }
    }

    /// <summary>The counters that the cards that acted read and the rebuild couldn't fill (empty: none).</summary>
    private static List<string> UnfilledCountersActing(LiveAction a)
    {
        var ids = new List<int>();
        if (!a.EndTurn) ids.Add(a.Source);
        ids.AddRange(a.BlockSources);
        var tags = new SortedSet<string>(StringComparer.Ordinal);
        foreach (int id in ids.Distinct())
        {
            LiveEntity e = a.Before.GetValueOrDefault(id) ?? a.After.GetValueOrDefault(id);
            if (e?[GameTag.CARDTYPE] is (int)CardType.GAME or (int)CardType.PLAYER) continue;
            if (CardFor(a.CardOf(id)) is Card c)
                foreach (int t in UnfilledCountersRead(c)) tags.Add(c.Id + ":" + CounterName(t));
        }
        return tags.ToList();
    }
}
