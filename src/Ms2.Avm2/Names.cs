namespace Ms2.Avm2;

public enum NsKind { Public, Internal, Protected, Explicit, Private }

/// <summary>
/// A namespace. Public, package-internal and explicit namespaces are interned by URI; every private namespace is its own
/// object. All protected namespaces are treated as one (a subclass sees its bases' protected members under its own protected
/// namespace in AVM2; folding them gives the same result for the code compilers emit).
/// </summary>
public sealed class Ns
{
    public readonly NsKind Kind;
    public readonly string Uri;
    Ns(NsKind k, string uri) { Kind = k; Uri = uri; }

    // read on every global lookup by name (each event, display object and frame script resolves its class): a hit takes no lock.
    // One table per kind, keyed by the URI alone: a string key and a class value use the runtime's shared generic code,
    // where a tuple key would make the JIT compile the whole dictionary again for it
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Ns>[] interned =
        { new(), new(), new(), new(), new() };
    public static readonly Ns Public = Get(NsKind.Public, "");
    public static readonly Ns Protected = new(NsKind.Protected, "");
    /// <summary>The AS3 namespace (http://adobe.com/AS3/2006/builtin) the compiler uses for some builtin methods.</summary>
    public static readonly Ns AS3 = Get(NsKind.Public, "http://adobe.com/AS3/2006/builtin");

    public static Ns Get(NsKind k, string uri)
    {
        if (k == NsKind.Protected) return Protected;
        if (k == NsKind.Private) return new Ns(k, uri);
        var table = interned[(int)k];
        return table.TryGetValue(uri, out var n) ? n : table.GetOrAdd(uri, new Ns(k, uri));
    }

    /// <summary>From an ABC namespace kind byte.</summary>
    public static Ns FromAbc(int kind, string name) => kind switch
    {
        0x08 or 0x16 => Get(NsKind.Public, name ?? ""),
        0x17 => Get(NsKind.Internal, name ?? ""),
        0x18 or 0x1A => Protected,
        0x19 => Get(NsKind.Explicit, name ?? ""),
        0x05 => new Ns(NsKind.Private, name ?? ""),
        _ => Get(NsKind.Public, name ?? ""),
    };

    public bool IsPublicLike => Kind == NsKind.Public && (Uri.Length == 0 || ReferenceEquals(this, AS3));

    public override string ToString() => Kind == NsKind.Public ? Uri : Kind.ToString().ToLowerInvariant() + ":" + Uri;
}

/// <summary>A resolved name for a lookup: a local name and the namespaces it may be in.</summary>
public readonly struct Name
{
    public readonly string Local;
    public readonly Ns[] Set;
    public readonly bool Attribute;
    public Name(string local, Ns[] set, bool attr = false) { Local = local; Set = set; Attribute = attr; }
    public static Name Public(string local) => new(local, PublicSet);
    public static readonly Ns[] PublicSet = { Ns.Public };
    /// <summary>Whether the set reaches public names (dynamic properties live there).</summary>
    public bool HasPublic { get { foreach (var n in Set) if (n.Kind == NsKind.Public) return true; return false; } }
    public override string ToString() => Local;
}

public enum BindingKind { Slot, Const, Method, Accessor, Class }

/// <summary>A trait of a class, a script, an activation or a class object.</summary>
public sealed class Binding
{
    public Ns Ns;
    public string Local;
    public BindingKind Kind;
    public int Slot;                // Slot/Const/Class: index into the owner's slots
    public MethodImpl Method;       // Method
    public MethodImpl Getter, Setter;
    public string TypeName;         // slot type (for coercion), qualified
    public object Default;          // slot initial value
    public Binding Clone() => (Binding)MemberwiseClone();
}

/// <summary>Traits by local name; several namespaces may share a local name.</summary>
public sealed class TraitTable
{
    readonly Dictionary<string, Binding[]> map = new();
    public int SlotCount;
    public readonly List<object> SlotDefaults = new();
    public readonly List<string> SlotTypes = new();

    public TraitTable() { }
    public TraitTable(TraitTable baseTable)
    {
        if (baseTable == null) return;
        foreach (var (k, v) in baseTable.map) map[k] = v.ToArray();
        SlotCount = baseTable.SlotCount;
        SlotDefaults.AddRange(baseTable.SlotDefaults);
        SlotTypes.AddRange(baseTable.SlotTypes);
    }

    public IEnumerable<Binding> All => map.Values.SelectMany(x => x);

    public Binding Find(in Name n)
    {
        if (n.Local == null || !map.TryGetValue(n.Local, out var arr)) return null;
        foreach (var b in arr)
            foreach (var ns in n.Set)
                if (ReferenceEquals(b.Ns, ns) || (b.Ns.Kind == NsKind.Public && ns.Kind == NsKind.Public && b.Ns.IsPublicLike && ns.IsPublicLike)) return b;
        // a call through an interface type names the interface's namespace ("pkg:IName"); the implementing class's public member answers it
        foreach (var ns in n.Set)
            if (ns.Kind == NsKind.Public && ns.Uri.IndexOf(':') > 0 && !ns.Uri.Contains("://"))
                foreach (var b in arr) if (b.Ns.IsPublicLike || ReferenceEquals(b.Ns, ns)) return b;
        return null;
    }

    public Binding FindPublic(string local) => Find(Name.Public(local));

    /// <summary>Any binding with this local name (used when a namespace could not be matched, e.g. natives called from code
    /// compiled against another namespace).</summary>
    public Binding FindAny(string local) => local != null && map.TryGetValue(local, out var arr) && arr.Length > 0 ? arr[0] : null;

    /// <summary>Adds or replaces (override) the binding with the same namespace and local name.</summary>
    public void Set(Binding b)
    {
        if (!map.TryGetValue(b.Local, out var arr)) { map[b.Local] = new[] { b }; return; }
        for (int i = 0; i < arr.Length; i++)
            if (ReferenceEquals(arr[i].Ns, b.Ns)) { arr[i] = b; return; }
        map[b.Local] = arr.Append(b).ToArray();
    }

    /// <summary>Adds a slot binding with a fresh or given (1-based, 0 = next) slot id.</summary>
    public Binding AddSlot(Ns ns, string local, int slotId, object def, string type, bool isConst)
    {
        int idx = slotId > 0 ? slotId - 1 : SlotCount;
        while (SlotDefaults.Count <= idx) { SlotDefaults.Add(Rt.Undef); SlotTypes.Add(null); }
        SlotDefaults[idx] = def; SlotTypes[idx] = type;
        SlotCount = Math.Max(SlotCount, idx + 1);
        var b = new Binding { Ns = ns, Local = local, Kind = isConst ? BindingKind.Const : BindingKind.Slot, Slot = idx, TypeName = type, Default = def };
        Set(b);
        return b;
    }

    public object[] NewSlots()
    {
        var s = new object[SlotCount];
        for (int i = 0; i < SlotCount; i++) s[i] = i < SlotDefaults.Count ? SlotDefaults[i] : Rt.Undef;
        return s;
    }
}
