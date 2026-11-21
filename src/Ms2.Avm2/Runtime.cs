using Ms2.Swf.Abc;

namespace Ms2.Avm2;

/// <summary>A script's global object: its traits (classes, functions, vars) are the definitions the domain exports.</summary>
public sealed class ScriptObject : AsObject
{
    public AbcContext Ctx;
    public ScriptInfo Info;
    public bool Started;
    public override bool IsDynamic => true;
}

/// <summary>
/// One AVM2 instance: native classes and global functions, the scripts of loaded ABC blocks (one application domain),
/// and the property operations the interpreter uses. Errors and missing APIs are recorded for the coverage scan.
/// </summary>
public sealed partial class Runtime
{
    public AsClass ObjectClass, ClassClass, FunctionClass, ArrayClass, StringClass, NumberClass, IntClass, UintClass, BooleanClass,
        ErrorClass, NamespaceClass, VectorClass;
    /// <summary>Native definitions (classes and functions) by local name.</summary>
    readonly AsObject nativeGlobals = new();
    readonly TraitTable nativeTable = new();
    /// <summary>Script definitions by local name.</summary>
    readonly Dictionary<string, List<(Ns Ns, ScriptObject Script)>> defs = new();
    public readonly List<AbcContext> Abcs = new();

    /// <summary>Unimplemented things the code reached: "flash.display.MovieClip.foo", "class flash.net.Socket", ...</summary>
    public readonly Dictionary<string, int> Missing = new();
    public readonly List<string> Errors = new();
    public Action<string> Trace = s => { };
    /// <summary>The runtime running on this thread (static natives use it).</summary>
    [ThreadStatic] public static Runtime Current;
    /// <summary>Instruction budget per top-level call (guards against endless loops in a scan); 0 = none.</summary>
    public long Budget;
    internal long budgetLeft;

    public Runtime()
    {
        nativeGlobals.Traits = nativeTable;
        nativeGlobals.Slots = Array.Empty<object>();
        Builtins.Install(this);
    }

    public void NoteMissing(string what)
    {
        Missing.TryGetValue(what, out var n);
        Missing[what] = n + 1;
    }

    // ---- definitions --------------------------------------------------------------------------------------

    /// <summary>Registers a native class or function under its qualified name (pkg.Name).</summary>
    public void DefineGlobal(string qname, object value)
    {
        int dot = qname.LastIndexOf('.');
        var ns = Ns.Get(NsKind.Public, dot < 0 ? "" : qname[..dot]);
        var local = dot < 0 ? qname : qname[(dot + 1)..];
        var b = nativeTable.AddSlot(ns, local, 0, value, null, true);
        nativeGlobals.Slots = nativeTable.NewSlots();
        // keep earlier values (NewSlots resets to defaults, which hold the values)
        _ = b;
    }

    public object GetGlobal(string qname)
    {
        int dot = qname.LastIndexOf('.');
        var n = new Name(dot < 0 ? qname : qname[(dot + 1)..], new[] { Ns.Get(NsKind.Public, dot < 0 ? "" : qname[..dot]) });
        var o = FindDefinition(n);
        return o == null ? Rt.Undef : GetProperty(o, n);
    }

    public AsClass GetClass(string qname) => GetGlobal(qname) as AsClass;

    /// <summary>The object that holds a definition: a script global (run first if needed) or the native globals.</summary>
    public AsObject FindDefinition(in Name n)
    {
        if (n.Local != null && defs.TryGetValue(n.Local, out var l))
            foreach (var (ns, so) in l)
                foreach (var want in n.Set)
                    if (ReferenceEquals(ns, want) || (ns.Kind == NsKind.Public && want.Kind == NsKind.Public && ns.Uri == want.Uri))
                    {
                        EnsureScript(so);
                        return so;
                    }
        if (nativeTable.Find(n) != null) return nativeGlobals;
        if (AutoClass != null && n.Local != null)
            foreach (var ns in n.Set)
                if (ns.Kind == NsKind.Public && AutoClass(ns.Uri, n.Local) != null) return nativeTable.Find(n) != null ? nativeGlobals : null;
        return null;
    }

    /// <summary>
    /// Makes a class for a definition no script has: a symbol linked to a class name with no code, which Flash backs with a
    /// generated MovieClip (or other base) subclass. Gets (package, local name); defines the class and returns it, or null.
    /// </summary>
    public Func<string, string, AsClass> AutoClass;

    void EnsureScript(ScriptObject so)
    {
        if (so.Started) return;
        so.Started = true;
        var init = so.Ctx.Method(so.Info.Init, new Scope(so, false, null), null);
        init.Invoke(so, Array.Empty<object>());
    }

    /// <summary>Loads an ABC block into this domain (definitions only; scripts run when first used or by <see cref="RunScripts"/>).</summary>
    public AbcContext Load(AbcFile abc, string label = null)
    {
        var ctx = new AbcContext(this, abc, label);
        Abcs.Add(ctx);
        foreach (var si in abc.Scripts)
        {
            var so = new ScriptObject { Ctx = ctx, Info = si, Traits = new TraitTable() };
            so.Init(null); so.Traits = new TraitTable();
            ctx.ApplyTraits(so.Traits, si.Traits, new Scope(so, false, null), null, isStatic: true);
            so.Slots = so.Traits.NewSlots();
            so.Class = ObjectClass;
            ctx.Scripts.Add(so);
            foreach (var t in si.Traits)
            {
                var mn = abc.Multinames[t.Name];
                var ns = ctx.NsAt(mn.NsIndex);
                if (!defs.TryGetValue(mn.Name, out var l)) defs[mn.Name] = l = new();
                l.Add((ns, so));
            }
        }
        return ctx;
    }

    /// <summary>Runs the last script of each loaded ABC (the entry script, as the player does).</summary>
    public void RunScripts(AbcContext ctx)
    {
        if (ctx.Scripts.Count > 0) EnsureScript(ctx.Scripts[^1]);
    }

    // ---- errors -------------------------------------------------------------------------------------------

    public AsThrow MakeError(string cls, string msg, int id = 0)
    {
        var c = GetClass(cls) ?? ErrorClass;
        var e = c.Construct(msg, id);
        return new AsThrow(e);
    }

    public AsThrow TypeError(string msg) => MakeError("TypeError", msg, 1006);
    public AsThrow ReferenceError(string msg) => MakeError("ReferenceError", msg, 1069);
    public AsThrow ArgumentError(string msg) => MakeError("ArgumentError", msg, 1063);
    public AsThrow RangeError(string msg) => MakeError("RangeError", msg, 1125);

    // ---- classes of values --------------------------------------------------------------------------------

    public AsClass ClassOf(object v) => v switch
    {
        AsObject o => o.Class ?? ObjectClass,
        string => StringClass,
        int i => IntClass,
        double => NumberClass,
        uint => UintClass,
        bool => BooleanClass,
        _ => null,
    };

    internal AsObject ProtoOf(AsObject o)
    {
        if (o.HasDynamic("__proto__") && o.GetDynamic("__proto__") is AsObject p0) return p0;
        if (o.ProtoParent != null) return o.ProtoParent;
        if (o.IsPrototypeObject) return null;
        return o.Class?.Prototype;
    }

    public bool IsType(object v, AsClass t)
    {
        if (t == null) return true;
        if (v == null || Rt.IsUndef(v)) return false;
        if (t == ObjectClass) return true;
        if (t == IntClass) return v is int || (v is double d && d == Math.Truncate(d) && d >= int.MinValue && d <= int.MaxValue);
        if (t == UintClass) return (v is int i && i >= 0) || (v is double d2 && d2 == Math.Truncate(d2) && d2 >= 0 && d2 <= uint.MaxValue);
        if (t == NumberClass) return Rt.IsNumber(v);
        var c = ClassOf(v);
        if (c != null && c.TypeArg != null && t == VectorClass) return true;
        if (c != null && t.TypeArg != null && c.TypeArg != null) return true;     // Vector.<A> vs Vector.<B>: not checked
        if (c == VectorClass && t.TypeArg != null) return true;
        return c != null && c.IsSubclassOf(t);
    }

    // ---- property operations ------------------------------------------------------------------------------

    object ReadBinding(object obj, Binding b, object[] slots)
    {
        switch (b.Kind)
        {
            case BindingKind.Slot: case BindingKind.Const: case BindingKind.Class: return slots[b.Slot];
            case BindingKind.Method: return obj is AsObject o ? o.Closure(b.Method) : new AsMethodClosure(this, obj, b.Method);
            case BindingKind.Accessor:
                if (b.Getter == null) throw ReferenceError($"property {b.Local} is write-only");
                return b.Getter.Invoke(obj, Array.Empty<object>());
        }
        return Rt.Undef;
    }

    public object CoerceTo(string type, object v) => type switch
    {
        null or "*" => v,
        "int" => Rt.Box(Rt.ToInt32(v)),
        "uint" => Rt.Uint(Rt.ToUint32(v)),
        "Number" => Rt.Num(Rt.ToNumber(v)),
        "Boolean" => Rt.Box(Rt.ToBoolean(v)),
        "String" => Rt.CoerceString(v),
        "Object" => Rt.IsUndef(v) ? null : v,
        _ => Rt.IsUndef(v) ? null : v,
    };

    public object GetProperty(object obj, in Name n, object key = null, bool hasKey = false)
    {
        if (Rt.IsNullish(obj)) throw TypeError($"Cannot access a property or method of a null object reference ({n.Local}).");
        if (obj is AsObject o)
        {
            if (hasKey && key is not string && o.TryGetKey(key, out var kv)) return kv;
            if (o.Traits != null)
            {
                var b = o.Traits.Find(n);
                if (b != null) return ReadBinding(o, b, o.Slots);
            }
            if (n.HasPublic && n.Local != null)
            {
                if (o.TryGetKey(n.Local, out var v)) return v;
                if (o.HasDynamic(n.Local)) return o.GetDynamic(n.Local);
                for (var p = ProtoOf(o); p != null; p = ProtoOf(p))
                {
                    if (p.HasDynamic(n.Local)) return p.GetDynamic(n.Local);
                    if (p.Traits != null && p is not AsClass && p.Traits.Find(n) is { } pb) return ReadBinding(o, pb, p.Slots);
                }
                // A class object is an Object: it inherits Object's methods (hasOwnProperty, ...) (its table is copied from Object before the methods are added to Object).
                if (o is AsClass && ObjectClass.InstanceTraits.Find(n) is { } cb) return ReadBinding(o, cb, null);
            }
            if (o.Class != null && !o.Class.InstancesDynamic && !(o is AsClass) && !(o is ScriptObject))
            {
                NoteMissing(MissingName(o.Class, n.Local));
                throw ReferenceError($"Property {n.Local} not found on {o.Class.Name} and there is no default value.");
            }
            return Rt.Undef;
        }
        var c = ClassOf(obj);
        var pb2 = c?.InstanceTraits.Find(n) ?? c?.InstanceTraits.FindAny(n.Local);
        if (pb2 != null) return ReadBinding(obj, pb2, null);
        if (c != null && n.Local != null)
            for (var p = c.Prototype; p != null; p = ProtoOf(p)) if (p.HasDynamic(n.Local)) return p.GetDynamic(n.Local);
        if (c != null) NoteMissing(MissingName(c, n.Local));
        return Rt.Undef;
    }

    /// <summary>Missing-API key: the native class that should have had the member.</summary>
    string MissingName(AsClass c, string local)
    {
        var nc = c; while (nc != null && !nc.IsNative) nc = nc.Super;
        return (nc ?? c).Name + "." + local;
    }

    public void SetProperty(object obj, in Name n, object value, object key = null, bool hasKey = false, bool init = false)
    {
        if (Rt.IsNullish(obj)) throw TypeError($"Cannot access a property or method of a null object reference ({n.Local}).");
        if (obj is not AsObject o) { NoteMissing("set on primitive " + n.Local); return; }
        if (hasKey && key is not string && o.TrySetKey(key, value)) return;
        if (o.Traits != null)
        {
            var b = o.Traits.Find(n);
            if (b != null)
            {
                switch (b.Kind)
                {
                    case BindingKind.Slot: case BindingKind.Const: case BindingKind.Class:
                        o.Slots[b.Slot] = CoerceTo(b.TypeName, value); return;
                    case BindingKind.Accessor:
                        if (b.Setter == null) throw ReferenceError($"Illegal write to read-only property {n.Local} on {o.Class?.Name}.");
                        b.Setter.Invoke(o, new[] { value }); return;
                    case BindingKind.Method:
                        throw ReferenceError($"Cannot assign to a method {n.Local} on {o.Class?.Name}.");
                }
            }
        }
        if (n.Local != null && o.TrySetKey(n.Local, value)) return;
        if (o.IsDynamic || o is AsClass) { o.SetDynamic(n.Local, value); return; }
        NoteMissing(MissingName(o.Class, n.Local));
        throw ReferenceError($"Cannot create property {n.Local} on {o.Class?.Name}.");
    }

    public bool HasProperty(object obj, in Name n, object key = null, bool hasKey = false)
    {
        if (obj is not AsObject o) return obj != null && ClassOf(obj)?.InstanceTraits.Find(n) != null;
        if (hasKey && key is not string && o.TryHasKey(key, out var hk)) return hk;
        if (o.Traits?.Find(n) != null) return true;
        if (n.Local != null && o.TryHasKey(n.Local, out var h2)) return h2;
        if (n.Local != null && o.HasDynamic(n.Local)) return true;
        for (var p = ProtoOf(o); p != null; p = ProtoOf(p)) if (n.Local != null && p.HasDynamic(n.Local)) return true;
        return false;
    }

    public bool DeleteProperty(object obj, in Name n, object key = null, bool hasKey = false)
    {
        if (obj is not AsObject o) return false;
        if (hasKey && key is not string && o.TryDeleteKey(key, out var d)) return d;
        if (n.Local != null && o.TryDeleteKey(n.Local, out var d2)) return d2;
        if (o.Traits?.Find(n) != null) return false;
        return n.Local != null && o.DeleteDynamic(n.Local);
    }

    public object CallProperty(object obj, in Name n, object[] args, object key = null, bool hasKey = false)
    {
        if (Rt.IsNullish(obj)) throw TypeError($"Cannot access a property or method of a null object reference ({n.Local}).");
        if (obj is AsObject o && o.Traits != null && !(hasKey && key is not string))
        {
            var b = o.Traits.Find(n);
            if (b != null && b.Kind == BindingKind.Method) return b.Method.Invoke(o, args);
        }
        else if (obj is not AsObject)
        {
            var c = ClassOf(obj);
            var b = c?.InstanceTraits.Find(n) ?? c?.InstanceTraits.FindAny(n.Local);
            if (b != null && b.Kind == BindingKind.Method) return b.Method.Invoke(obj, args);
        }
        var f = GetProperty(obj, n, key, hasKey);
        return Invoke(f, obj, args, n.Local, obj);
    }

    public object Invoke(object f, object self, object[] args, string what, object on = null)
    {
        if (f is AsFunction fn) return fn.Call(self, args);
        if (f is AsClass cls) return CallClass(cls, args);
        var c = on == null ? null : ClassOf(on);
        if (c != null && Rt.IsUndef(f)) NoteMissing(MissingName(c, what));
        throw TypeError($"{what} is not a function ({(c == null ? "?" : c.Name)}).");
    }

    /// <summary>A class called as a function: a conversion (int(x), String(x), Array(...)) or a type cast.</summary>
    public object CallClass(AsClass cls, object[] args)
    {
        if (cls.CallAsFunction != null) return cls.CallAsFunction(args);
        var v = args.Length > 0 ? args[0] : Rt.Undef;
        if (Rt.IsNullish(v)) return null;
        if (!IsType(v, cls)) throw TypeError($"Type Coercion failed: cannot convert {Rt.Describe(v)} to {cls.Name}.");
        return v;
    }

    public object Construct(object ctor, object[] args, string what = null)
    {
        if (ctor is AsClass c) return c.Construct(args);
        if (ctor is AsFunction f) return f.Construct(args);
        if (what != null && Rt.IsUndef(ctor)) NoteMissing("class " + what);
        throw TypeError($"Instantiation attempted on a non-constructor ({what}).");
    }

    // ---- construction hooks (the display list builds timeline children before the constructor runs) ------------

    public Action<AsObject> OnBeforeConstruct, OnAfterConstruct;
    internal void BeforeConstruct(AsObject o) => OnBeforeConstruct?.Invoke(o);
    internal void AfterConstruct(AsObject o) => OnAfterConstruct?.Invoke(o);

    // ---- top-level calls with error capture ---------------------------------------------------------------

    /// <summary>Runs a call from the host; an AS exception or an internal failure is recorded (and returned as false).</summary>
    public bool Guard(string what, Action a) => Guard(what, null, a, static f => f());

    /// <summary>
    /// <see cref="Guard(string, Action)"/> for the calls made every frame: the state goes in as an argument (no closure
    /// per call) and the label is <paramref name="what"/>, a space and <paramref name="detail"/> (when not null), made only
    /// when an error is recorded.
    /// </summary>
    public bool Guard<T>(string what, string detail, T state, Action<T> a)
    {
        var prev = Current;
        Current = this;
        if (guardDepth == 0) budgetLeft = Budget;
        guardDepth++;
        try { a(state); return true; }
        catch (AsThrow t) { OutputError(what, detail, t); return false; }
        catch (BudgetExceeded) when (guardDepth == 1) { Errors.Add(Label(what, detail) + ": instruction budget exceeded"); return false; }
        catch (Exception e) when (e is not OutOfMemoryException) { Errors.Add(Label(what, detail) + ": internal " + e.GetType().Name + ": " + e.Message + " @ " + e.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("Ms2.Avm2"))?.Trim()); return false; }
        finally { guardDepth--; Current = prev ?? this; }
    }
    static string Label(string what, string detail) => detail == null ? what : what + " " + detail;

    /// <summary>Records an AS3 exception the caller ignores and goes on from (VM::OutputAndIgnoreException, sfr_as3_09.cpp:
    /// the error is output and cleared).</summary>
    public void OutputError(string what, string detail, AsThrow t) =>
        Errors.Add(Label(what, detail) + ": " + Rt.Describe(t.Value) + (t.AsStack.Length > 0 ? "   [in " + t.AsStack + "]" : ""));
    int guardDepth;
}

public sealed class BudgetExceeded : Exception { }
