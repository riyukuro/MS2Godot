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
        var l = new List<object>();
        for (int i = 0; i < Items.Count; i++) if (!Rt.IsUndef(Items[i])) l.Add(Rt.ToString(Rt.Box(i)));
        l.AddRange(base.EnumKeys());
        return l;
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
        static object Norm(object o) => o is int or double or uint ? Rt.ToString(o) : o;
    }
    readonly Dictionary<object, object> map = new(new KeyCmp());
    readonly List<object> order = new();
    public override bool IsDynamic => true;

    public override bool TryGetKey(object key, out object v) { v = map.TryGetValue(key ?? NullKey, out var x) ? x : Rt.Undef; return true; }
    public override bool TrySetKey(object key, object v) { key ??= NullKey; if (!map.ContainsKey(key)) order.Add(key); map[key] = v; return true; }
    public override bool TryHasKey(object key, out bool has) { has = map.ContainsKey(key ?? NullKey); return true; }
    public override bool TryDeleteKey(object key, out bool done) { key ??= NullKey; done = map.Remove(key); if (done) order.Remove(key); return true; }
    public override IList<object> EnumKeys() => order.ToList();
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
