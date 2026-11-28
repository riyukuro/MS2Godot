namespace Ms2.Avm2;

/// <summary>Array (also used for Vector.&lt;T&gt;): a dense list plus dynamic properties.</summary>
public class AsArray : AsObject
{
    public readonly List<object> Items;
    public AsArray(Runtime rt, IEnumerable<object> items = null, AsClass cls = null)
    {
        Items = items == null ? new List<object>() : new List<object>(items);
        if (rt != null) Init(cls ?? rt.ArrayClass);
    }
    public AsArray() { Items = new List<object>(); }
    public override bool IsDynamic => true;

    public int Length
    {
        get => Items.Count;
        set
        {
            if (value < Items.Count) Items.RemoveRange(value, Items.Count - value);
            else while (Items.Count < value) Items.Add(Rt.Undef);
        }
    }

    static bool Index(object key, out int i)
    {
        switch (key)
        {
            case int x when x >= 0: i = x; return true;
            case double d when d >= 0 && d == Math.Truncate(d) && d < int.MaxValue: i = (int)d; return true;
            case uint u when u < int.MaxValue: i = (int)u; return true;
            case string s when s.Length > 0 && s.Length < 10 && s[0] >= '0' && s[0] <= '9' && (s.Length == 1 || s[0] != '0') && int.TryParse(s, out var p): i = p; return true;
        }
        i = -1; return false;
    }

    public override bool TryGetKey(object key, out object v)
    {
        if (Index(key, out var i)) { v = i < Items.Count ? Items[i] : Rt.Undef; return true; }
        v = null; return false;
    }

    public override bool TrySetKey(object key, object v)
    {
        if (!Index(key, out var i)) return false;
        if (i >= Items.Count) { if (i > 10_000_000) throw new ArgumentOutOfRangeException(nameof(key)); Length = i + 1; }
        Items[i] = v;
        return true;
    }

    public override bool TryHasKey(object key, out bool has)
    {
        if (Index(key, out var i)) { has = i < Items.Count && !Rt.IsUndef(Items[i]); return true; }
        has = false; return false;
    }

    public override bool TryDeleteKey(object key, out bool done)
    {
        if (Index(key, out var i)) { if (i < Items.Count) Items[i] = Rt.Undef; done = true; return true; }
        done = false; return false;
    }

    public override IList<object> EnumKeys()
    {
        var l = new List<object>(Items.Count + DynCount);
        for (int i = 0; i < Items.Count; i++) if (!Rt.IsUndef(Items[i])) l.Add(Rt.IntString(i));
        AddDynamicKeys(l);
        return l;
    }

    // List<T> counts its own changes (every Add, Remove, Insert, Sort and item set): code changes Items directly, so that
    // count is the items' version
    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "_version")]
    static extern ref int ListVersion(List<object> list);
    internal static bool NoListVersion;      // a runtime without the count: check the kept list against the items instead

    internal override long EnumVersion
    {
        get
        {
            if (!NoListVersion)
                try { return (long)ListVersion(Items) << 32 | (uint)base.EnumVersion; }
                catch (MissingFieldException) { NoListVersion = true; }
            return base.EnumVersion;
        }
    }

    /// <summary>With the items' version: the version alone. Without it: the index keys are the indices of the items that are
    /// not holes, in order, then the dynamic keys (unchanged with the version), checked by a walk with no allocation.</summary>
    internal override bool EnumStill(IList<object> keys, long version)
    {
        if (version != EnumVersion) return false;
        if (!NoListVersion) return true;
        int j = 0;
        for (int i = 0; i < Items.Count; i++)
        {
            if (Rt.IsUndef(Items[i])) continue;
            if (j >= keys.Count || keys[j] is not string s || !IsIndex(s, i)) return false;
            j++;
        }
        return keys.Count == j + DynCount;
    }

    // s is the decimal string of i (i >= 0)
    static bool IsIndex(string s, int i)
    {
        int n = s.Length;
        do
        {
            if (n == 0 || s[--n] != '0' + i % 10) return false;
            i /= 10;
        } while (i != 0);
        return n == 0;
    }

    public override object ToPrimitive(bool numberHint) => string.Join(",", Items.Select(x => Rt.IsNullish(x) ? "" : Rt.ToString(x)));
}

/// <summary>flash.utils.Dictionary: keys compared by identity (objects) or value (primitives).</summary>
public sealed class AsDictionary : AsObject
{
    sealed class KeyCmp : IEqualityComparer<object>
    {
        public new bool Equals(object a, object b) => a is AsObject || b is AsObject ? ReferenceEquals(a, b) : Rt.StrictEquals(Norm(a), Norm(b));
        public int GetHashCode(object o) => o is AsObject ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o) : Norm(o)?.GetHashCode() ?? 0;
        // a number key is its string (shared for small ints): 1, 1.0 and "1" are one key
        static object Norm(object o) => o switch
        {
            int i => Rt.IntString(i),
            uint u => u < int.MaxValue ? Rt.IntString((int)u) : Rt.ToString(u),
            double d => d >= 0 && d < 4096 && d == Math.Truncate(d) ? Rt.IntString((int)d) : Rt.ToString(d),
            _ => o,
        };
    }
    readonly Dictionary<object, object> map = new(new KeyCmp());
    readonly List<object> order = new();
    int orderVersion;                        // bumped when a key is added or deleted
    public override bool IsDynamic => true;

    public override bool TryGetKey(object key, out object v) { v = map.TryGetValue(key ?? NullKey, out var x) ? x : Rt.Undef; return true; }
    public override bool TrySetKey(object key, object v) { key ??= NullKey; if (!map.ContainsKey(key)) { order.Add(key); orderVersion++; } map[key] = v; return true; }
    public override bool TryHasKey(object key, out bool has) { has = map.ContainsKey(key ?? NullKey); return true; }
    public override bool TryDeleteKey(object key, out bool done) { key ??= NullKey; done = map.Remove(key); if (done) { order.Remove(key); orderVersion++; } return true; }
    public override IList<object> EnumKeys() => order.ToList();
    internal override long EnumVersion => orderVersion;
    static readonly object NullKey = "null";
}

public sealed class AsNamespace : AsObject
{
    public readonly Ns Ns;
    public AsNamespace(Runtime rt, Ns ns) { Ns = ns; Init(rt.NamespaceClass); }
    public override object ToPrimitive(bool numberHint) => Ns.Uri;
}

public sealed class AsQName : AsObject
{
    public readonly Ns Ns; public readonly string Local;
    public AsQName(Ns ns, string local) { Ns = ns; Local = local; }
}
