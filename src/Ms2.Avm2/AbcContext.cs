using Ms2.Swf.Abc;

namespace Ms2.Avm2;

/// <summary>One loaded ABC block: resolved constants, the classes its newclass created, decoded method bodies.</summary>
public sealed class AbcContext
{
    public readonly Runtime Rt;
    public readonly AbcFile Abc;
    public readonly string Label;
    readonly Ns[] ns;
    readonly Ns[][] sets;
    readonly Name?[] names;
    public readonly AsClass[] Classes;
    public readonly List<ScriptObject> Scripts = new();
    readonly Code[] code;
    readonly TraitTable[] activationTraits;

    public AbcContext(Runtime rt, AbcFile abc, string label)
    {
        Rt = rt; Abc = abc; Label = label;
        ns = new Ns[abc.Namespaces.Length];
        for (int i = 1; i < ns.Length; i++) ns[i] = Ns.FromAbc(abc.Namespaces[i].Kind, abc.Namespaces[i].Name);
        ns[0] = Ns.Public;
        sets = new Ns[abc.NsSets.Length][];
        for (int i = 0; i < sets.Length; i++) sets[i] = abc.NsSets[i].Select(x => ns[x]).ToArray();
        names = new Name?[abc.Multinames.Length];
        Classes = new AsClass[abc.Instances.Length];
        code = new Code[abc.Methods.Length];
        activationTraits = new TraitTable[abc.Methods.Length];
        CompiledBodies = GeneratedCode.Find(abc.Hash);
    }

    public Ns NsAt(int i) => i > 0 && i < ns.Length ? ns[i] : Ns.Public;

    /// <summary>The compile-time part of a multiname (local name and namespace set; runtime parts come from the stack).</summary>
    public Name NameAt(int mn)
    {
        if (names[mn] is { } n) return n;
        var m = Abc.Multinames[mn];
        Name r = m.Kind switch
        {
            0x07 or 0x0D => new Name(m.Name, new[] { NsAt(m.NsIndex) }, m.Kind == 0x0D),
            0x09 or 0x0E => new Name(m.Name, sets[m.NsSetIndex], m.Kind == 0x0E),
            0x1B or 0x1C => new Name(null, sets[m.NsSetIndex], m.Kind == 0x1C),
            0x0F or 0x10 => new Name(m.Name, null),
            0x1D => new Name(Abc.Multinames[m.TypeBase].Name, new[] { NsAt(Abc.Multinames[m.TypeBase].NsIndex) }),
            _ => new Name(m.Name, Name.PublicSet),
        };
        names[mn] = r;
        return r;
    }

    public string TypeNameAt(int mn)
    {
        if (mn <= 0) return null;
        var m = Abc.Multinames[mn];
        if (m.Kind == 0x1D) return "Vector";
        var n = NameAt(mn);
        if (n.Set != null && n.Set.Length == 1 && n.Set[0].Kind == NsKind.Public && n.Set[0].Uri.Length > 0) return n.Set[0].Uri + "." + n.Local;
        return n.Local;
    }

    public object Constant(int kind, int index) => kind switch
    {
        0x03 => Avm2.Rt.Box(Abc.Ints[index]),
        0x04 => Avm2.Rt.Uint(Abc.Uints[index]),
        0x06 => Avm2.Rt.Num(Abc.Doubles[index]),
        0x01 => Abc.Strings[index],
        0x0B => Avm2.Rt.True,
        0x0A => Avm2.Rt.False,
        0x0C => null,
        0x00 => Avm2.Rt.Undef,
        _ => Avm2.Rt.Undef,     // namespaces as values are not used by the UI code
    };

    static object TypeDefault(string type) => type switch
    {
        "int" or "uint" => Avm2.Rt.Box(0),
        "Number" => double.NaN,
        "Boolean" => Avm2.Rt.False,
        null or "*" => Avm2.Rt.Undef,
        _ => null,
    };

    public MethodImpl Method(int index, Scope scope, AsClass declaring) => new AbcMethod(this, Abc.Methods[index], scope, declaring);

    /// <summary>Turns ABC traits into bindings on a table (classes, scripts, activations, catch scopes).</summary>
    public void ApplyTraits(TraitTable table, List<Trait> traits, Scope scope, AsClass declaring, bool isStatic)
    {
        int baseSlots = table.SlotCount;
        foreach (var t in traits)
        {
            var mn = Abc.Multinames[t.Name];
            var tns = NsAt(mn.NsIndex);
            string local = mn.Name;
            switch (t.Kind)
            {
                case TraitKind.Slot: case TraitKind.Const:
                {
                    var type = TypeNameAt(t.TypeName);
                    var def = t.ValueIndex != 0 ? Constant(t.ValueKind, t.ValueIndex) : TypeDefault(type);
                    int id = t.SlotId == 0 ? 0 : t.SlotId > baseSlots || baseSlots == 0 ? t.SlotId : baseSlots + t.SlotId;
                    table.AddSlot(tns, local, id, def, type, t.Kind == TraitKind.Const);
                    break;
                }
                case TraitKind.Class:
                {
                    int id = t.SlotId == 0 ? 0 : t.SlotId;
                    var b = table.AddSlot(tns, local, id, null, "Class", false);
                    b.Kind = BindingKind.Class;
                    break;
                }
                case TraitKind.Function:
                {
                    var b = table.AddSlot(tns, local, t.SlotId, null, "Function", false);
                    // the script init assigns it (newfunction) in most compilers; give it a value anyway
                    b.Default = new AsPlainFunction(Rt, Method(t.MethodIndex, scope, declaring));
                    table.SlotDefaults[b.Slot] = b.Default;
                    break;
                }
                case TraitKind.Method:
                    table.Set(new Binding { Ns = tns, Local = local, Kind = BindingKind.Method, Method = Named(Method(t.MethodIndex, scope, declaring), declaring, local) });
                    break;
                case TraitKind.Getter: case TraitKind.Setter:
                {
                    var existing = table.Find(new Name(local, new[] { tns }));
                    var b = existing != null && existing.Kind == BindingKind.Accessor && ReferenceEquals(existing.Ns, tns) ? existing.Clone()
                        : new Binding { Ns = tns, Local = local, Kind = BindingKind.Accessor };
                    var m = Named(Method(t.MethodIndex, scope, declaring), declaring, local);
                    if (t.Kind == TraitKind.Getter) b.Getter = m; else b.Setter = m;
                    table.Set(b);
                    break;
                }
            }
        }
    }

    static MethodImpl Named(MethodImpl m, AsClass c, string local) { m.Name = (c?.Name ?? "") + "/" + local; return m; }

    /// <summary>newclass: builds the class object for instance/class info <paramref name="index"/> with base <paramref name="super"/>.</summary>
    public AsClass NewClass(int index, AsClass super, Scope scope)
    {
        var ii = Abc.Instances[index];
        var ci = Abc.Classes[index];
        var qn = Abc.Multinames[ii.Name];
        var cns = NsAt(qn.NsIndex);
        var c = new AsClass
        {
            Runtime = Rt, LocalName = qn.Name, Ns = cns, Name = cns.Uri.Length > 0 && cns.Kind != NsKind.Private ? cns.Uri + "." + qn.Name : qn.Name,
            Super = super, InstancesDynamic = !ii.IsSealed, IsInterface = ii.IsInterface, IsFinal = (ii.Flags & 2) != 0,
        };
        c.Class = Rt.ClassClass;
        c.Allocator = super?.Allocator;
        c.NativeType = super?.NativeType;
        c.IsErrorClass = super?.IsErrorClass ?? false;
        foreach (var i in ii.Interfaces)
        {
            var iname = NameAt(i);
            var holder = Rt.FindDefinition(iname);
            if (holder != null && Rt.GetProperty(holder, iname) is AsClass ic) c.Interfaces.Add(ic);
        }
        var classScope = new Scope(c, false, scope);
        c.InstanceTraits = new TraitTable(super?.InstanceTraits);
        ApplyTraits(c.InstanceTraits, ii.Traits, classScope, c, false);
        c.Traits = new TraitTable();
        ApplyTraits(c.Traits, ci.Traits, classScope, c, true);
        c.Slots = c.Traits.NewSlots();
        c.Prototype = new AsObject { Class = Rt.ObjectClass, IsPrototypeObject = true, ProtoParent = super?.Prototype ?? Rt.ObjectClass?.Prototype };
        c.Prototype.SetDynamic("constructor", c);
        c.Ctor = Named(Method(ii.Init, classScope, c), c, "constructor");
        Classes[index] = c;
        var cinit = Method(ci.Init, classScope, c);
        cinit.Invoke(c, Array.Empty<object>());
        return c;
    }

    // ---- decoded bodies -----------------------------------------------------------------------------------

    /// <summary>The translated method bodies registered for this block's hash, by method index (null: none).</summary>
    public CompiledBody[] CompiledBodies;

    public sealed class Code
    {
        public byte[] Op; public int[] A, B, Target; public int[][] Cases; public int[] Off;
        public (int From, int To, int Target, int Type, int Var)[] Handlers;
    }

    public Code CodeFor(MethodInfo m)
    {
        var c = code[m.Index];
        if (c != null) return c;
        var ins = Disasm.Decode(m.Body.Code);
        var at = new Dictionary<int, int>();
        for (int i = 0; i < ins.Count; i++) at[ins[i].Off] = i;
        at[m.Body.Code.Length] = ins.Count;
        int Idx(int off) => at.TryGetValue(off, out var x) ? x : NearestAfter(at, off);
        c = new Code
        {
            Op = ins.Select(i => i.Op).ToArray(), A = ins.Select(i => i.A).ToArray(), B = ins.Select(i => i.B).ToArray(),
            Target = ins.Select(i => i.Target >= 0 ? Idx(i.Target) : -1).ToArray(),
            Cases = ins.Select(i => i.Cases?.Select(Idx).ToArray()).ToArray(),
            Off = ins.Select(i => i.Off).ToArray(),
            Handlers = m.Body.Exceptions.Select(e => (Idx(e.From), Idx(e.To), Idx(e.Target), e.ExcType, e.VarName)).ToArray(),
        };
        code[m.Index] = c;
        return c;
    }

    static int NearestAfter(Dictionary<int, int> at, int off)
    {
        int best = int.MaxValue, idx = at.Count;
        foreach (var (o, i) in at) if (o >= off && o < best) { best = o; idx = i; }
        return idx;
    }

    public TraitTable ActivationTraits(MethodInfo m)
    {
        var t = activationTraits[m.Index];
        if (t != null) return t;
        t = new TraitTable();
        ApplyTraits(t, m.Body.Traits, null, null, false);
        return activationTraits[m.Index] = t;
    }
}

/// <summary>An ABC method with its captured scope chain and the class it belongs to (for super calls).</summary>
public sealed class AbcMethod : MethodImpl
{
    public readonly AbcContext Ctx;
    public readonly MethodInfo Info;
    public readonly Scope Scope;
    public readonly AsClass Declaring;
    public AbcMethod(AbcContext ctx, MethodInfo info, Scope scope, AsClass declaring)
    {
        Ctx = ctx; Info = info; Scope = scope; Declaring = declaring; Name = declaring?.Name + "/m" + info.Index;
        Compiled = ctx.CompiledBodies is { } cb && info.Index < cb.Length ? cb[info.Index] : null;
    }
    /// <summary>The translated body of this method (<see cref="GeneratedCode"/>), null to interpret it.</summary>
    public readonly CompiledBody Compiled;
    /// <summary>The runtime's native body for this method, if any: Scaleform replaces the stub bodies the movies carry
    /// for scaleform.gfx.* (e.g. Extensions.isScaleform returns false in the SWF). Resolved on the first call.</summary>
    internal MethodImpl NativeOverride;
    internal bool NativeResolved;
    public override object Invoke(object self, object[] args) => Interp.Run(this, self, args);
}
