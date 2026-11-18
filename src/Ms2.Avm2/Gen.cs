using System.IO;
using System.Reflection;
using Ms2.Swf.Abc;

namespace Ms2.Avm2;

/// <summary>A translated ABC method body: the same contract as one interpreted call (<see cref="Interp.Run"/>).</summary>
public delegate object CompiledBody(AbcMethod m, object self, object[] args);

/// <summary>
/// The translated method bodies loaded into this process, by the SHA-1 of the ABC block they were translated from. A
/// generated assembly registers its tables (<see cref="Register"/>) through its <c>Ms2UiGen.Init</c>; a block with no
/// table runs in the interpreter.
/// </summary>
public static class GeneratedCode
{
    static readonly Dictionary<string, CompiledBody[]> byHash = new(StringComparer.OrdinalIgnoreCase);

    public static void Register(string hash, CompiledBody[] bodies) { lock (byHash) byHash[hash] = bodies; }

    /// <summary>A block's bodies as ids into the assembly's shared table (-1: none), one per method index.</summary>
    public static void Register(string hash, CompiledBody[] table, int[] ids)
    {
        var b = new CompiledBody[ids.Length];
        for (int i = 0; i < ids.Length; i++) if (ids[i] >= 0) b[i] = table[ids[i]];
        Register(hash, b);
    }

    public static CompiledBody[] Find(string hash)
    {
        if (hash == null) return null;
        lock (byHash) return byHash.TryGetValue(hash, out var b) ? b : null;
    }

    public static int Count { get { lock (byHash) return byHash.Count; } }

    /// <summary>Loads a generated assembly and runs its registration (a static <c>Ms2UiGen.Init()</c>).</summary>
    public static int LoadAssembly(string path)
    {
        // into the load context that holds this runtime (a host such as Godot runs its game assemblies in their own),
        // so the generated code binds to the same Ms2.Avm2
        var ctx = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(typeof(GeneratedCode).Assembly)
                  ?? System.Runtime.Loader.AssemblyLoadContext.Default;
        var asm = ctx.LoadFromAssemblyPath(Path.GetFullPath(path));
        int before = Count;
        asm.GetType("Ms2UiGen")?.GetMethod("Init", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
        return Count - before;
    }
}

/// <summary>
/// What translated code calls for each AVM2 instruction: the interpreter's own semantics (<see cref="Interp"/>), so the
/// two paths differ only in dispatch. Every member mirrors one interpreter case.
/// </summary>
public static class Gen
{
    /// <summary>The method's registers: this, the parameters coerced to their types (optional ones defaulted), the rest array.</summary>
    public static object[] Locals(AbcMethod m, object self, object[] args)
    {
        var ctx = m.Ctx; var rt = ctx.Rt; var info = m.Info; var body = info.Body;
        var locals = new object[Math.Max(body.LocalCount, info.ParamCount + 2)];
        for (int i = 0; i < locals.Length; i++) locals[i] = Rt.Undef;
        locals[0] = self;
        int np = info.ParamCount;
        int optStart = np - (info.Options?.Count ?? 0);
        for (int i = 0; i < np; i++)
        {
            object v;
            if (i < args.Length) v = args[i];
            else if (i >= optStart) { var (k, idx) = info.Options[i - optStart]; v = ctx.Constant(k, idx); }
            else v = Rt.Undef;
            var pt = ctx.TypeNameAt(info.ParamTypes[i]);
            locals[i + 1] = pt == null ? v : rt.CoerceTo(pt, v);
        }
        if (info.NeedRest) locals[np + 1] = new AsArray(rt, args.Length > np ? args[np..] : Array.Empty<object>());
        else if ((info.Flags & 1) != 0) locals[np + 1] = new AsArray(rt, args);
        return locals;
    }

    public static void Tick(Runtime rt) { if (rt.Budget > 0 && --rt.budgetLeft < 0) throw new BudgetExceeded(); }

    // ---- names -----------------------------------------------------------------------------------------------

    public static Name N(AbcMethod m, int mn) => m.Ctx.NameAt(mn);
    /// <summary>A multiname whose name is on the stack (MultinameL): an AS QName gives its own namespace.</summary>
    public static Name LName(in Name nm, object k) =>
        k is AsQName q ? new Name(q.Local, new[] { q.Ns }) : new Name(k is string s ? s : Rt.ToString(k), nm.Set);
    public static object LKey(object k) => k is AsQName ? null : k;
    public static bool LHas(object k) => k is not AsQName;
    /// <summary>RTQName: the namespace from the stack.</summary>
    public static Name NsName(in Name nm, object nsv) => new(nm.Local, new[] { nsv is AsNamespace an ? an.Ns : Ns.Public });
    /// <summary>RTQNameL: the name and the namespace from the stack.</summary>
    public static Name NsLName(object k, object nsv) => new(Rt.ToString(k), new[] { nsv is AsNamespace an ? an.Ns : Ns.Public });

    // ---- scopes ----------------------------------------------------------------------------------------------

    public static Scope Chain(AbcMethod m, List<(object Obj, bool With)> scopes)
    {
        var s = m.Scope;
        foreach (var (o, w) in scopes) s = new Scope(o, w, s);
        return s;
    }

    public static object FindProp(AbcMethod m, List<(object Obj, bool With)> scopes, object self, in Name nm, bool strict)
    {
        var rt = m.Ctx.Rt;
        for (int i = scopes.Count - 1; i >= 0; i--)
            if (Interp.ScopeHas(rt, scopes[i].Obj, nm, scopes[i].With)) return scopes[i].Obj;
        for (var s = m.Scope; s != null; s = s.Parent)
            if (Interp.ScopeHas(rt, s.Obj, nm, s.With)) return s.Obj;
        var d = rt.FindDefinition(nm);
        if (d != null) return d;
        if (strict)
        {
            rt.NoteMissing("global " + Interp.Qual(nm));
            throw rt.ReferenceError($"Error #1065: Variable {nm.Local} is not defined.");
        }
        var g = m.Scope; while (g?.Parent != null) g = g.Parent;
        return g?.Obj ?? (scopes.Count > 0 ? scopes[0].Obj : self);
    }

    public static object FindDef(AbcMethod m, List<(object Obj, bool With)> scopes, object self, int mn)
    {
        var nm = m.Ctx.NameAt(mn);
        return (object)m.Ctx.Rt.FindDefinition(nm) ?? FindProp(m, scopes, self, nm, true);
    }

    public static object GetLex(AbcMethod m, List<(object Obj, bool With)> scopes, object self, int mn)
    {
        var nm = m.Ctx.NameAt(mn);
        return m.Ctx.Rt.GetProperty(FindProp(m, scopes, self, nm, true), nm);
    }

    public static object GlobalScope(AbcMethod m, List<(object Obj, bool With)> scopes)
    {
        var g = m.Scope; while (g?.Parent != null) g = g.Parent;
        return g?.Obj ?? (scopes.Count > 0 ? scopes[0].Obj : null);
    }

    public static object GetGlobalSlot(AbcMethod m, List<(object Obj, bool With)> scopes, int slot) =>
        ((AsObject)Interp.GlobalOf(m, scopes)).Slots[slot - 1];
    public static void SetGlobalSlot(AbcMethod m, List<(object Obj, bool With)> scopes, int slot, object v) =>
        ((AsObject)Interp.GlobalOf(m, scopes)).Slots[slot - 1] = v;

    // ---- objects ---------------------------------------------------------------------------------------------

    public static object NewFunction(AbcMethod m, List<(object Obj, bool With)> scopes, int index)
    {
        var rt = m.Ctx.Rt;
        var f = new AsPlainFunction(rt, new AbcMethod(m.Ctx, m.Ctx.Abc.Methods[index], Chain(m, scopes), null) { Name = "function" });
        f.SetDynamic("prototype", new AsObject(rt.ObjectClass));
        return f;
    }

    public static object NewClass(AbcMethod m, List<(object Obj, bool With)> scopes, int index, object super) =>
        m.Ctx.NewClass(index, super as AsClass, Chain(m, scopes));

    public static object NewObject(Runtime rt, object[] kv)
    {
        var o = new AsObject(rt.ObjectClass);
        for (int i = 0; i < kv.Length / 2; i++) o.SetDynamic(Rt.ToString(kv[2 * i]), kv[2 * i + 1]);
        return o;
    }

    public static object NewActivation(AbcMethod m)
    {
        var t = m.Ctx.ActivationTraits(m.Info);
        return new AsObject { Class = m.Ctx.Rt.ObjectClass, Traits = t, Slots = t.NewSlots() };
    }

    public static object NewCatch(AbcMethod m, int index)
    {
        var abc = m.Ctx.Abc;
        var h = m.Info.Body.Exceptions[index];
        var t = new TraitTable();
        if (h.VarName != 0) t.AddSlot(m.Ctx.NsAt(abc.Multinames[h.VarName].NsIndex), abc.Multinames[h.VarName].Name, 1, Rt.Undef, null, false);
        return new AsObject { Class = m.Ctx.Rt.ObjectClass, Traits = t, Slots = t.NewSlots() };
    }

    public static object ConstructProp(Runtime rt, object obj, in Name nm, object key, bool hasKey, object[] a) =>
        rt.Construct(rt.GetProperty(obj, nm, key, hasKey), a, Interp.Qual(nm));

    public static void ConstructSuper(AbcMethod m, object obj, object[] a) => m.Declaring?.Super?.Ctor?.Invoke(obj, a);

    public static object GetSuper(AbcMethod m, object obj, in Name nm) => Interp.SuperGet(m.Ctx.Rt, m, obj, nm);
    public static void SetSuper(AbcMethod m, object obj, in Name nm, object v) => Interp.SuperSet(m.Ctx.Rt, m, obj, nm, v);
    public static object CallSuper(AbcMethod m, object obj, in Name nm, object[] a) => Interp.SuperCall(m.Ctx.Rt, m, obj, nm, a);

    public static object GetSlot(object o, int slot) => ((AsObject)o).Slots[slot - 1];
    public static void SetSlot(Runtime rt, object o, int slot, object v)
    {
        var ao = (AsObject)o;
        ao.Slots[slot - 1] = Interp.CoerceSlot(rt, ao, slot - 1, v);
    }

    // ---- types -----------------------------------------------------------------------------------------------

    public static object Coerce(AbcMethod m, int mn, object v) => Interp.Coerce(m.Ctx.Rt, m.Ctx, mn, m.Ctx.TypeNameAt(mn), v);
    public static object AsType(AbcMethod m, int mn, object v)
    {
        var t = Interp.TypeClass(m.Ctx.Rt, m.Ctx, mn);
        return t == null || m.Ctx.Rt.IsType(v, t) ? v : null;
    }
    public static object AsTypeLate(Runtime rt, object v, object t) => t is not AsClass c || rt.IsType(v, c) ? v : null;
    public static bool IsType(AbcMethod m, int mn, object v)
    {
        var t = Interp.TypeClass(m.Ctx.Rt, m.Ctx, mn);
        return t == null || m.Ctx.Rt.IsType(v, t);
    }
    public static bool IsTypeLate(Runtime rt, object v, object t) => t is AsClass c && rt.IsType(v, c);
    public static bool InstanceOf(Runtime rt, object v, object t) => Interp.InstanceOf(rt, v, t);
    public static bool In(Runtime rt, object k, object o) => rt.HasProperty(o, Name.Public(k is string s ? s : Rt.ToString(k)), k, true);
    public static object CheckNull(Runtime rt, object v)
    {
        if (Rt.IsNullish(v)) throw rt.TypeError("Error #1009: Cannot access a property or method of a null object reference.");
        return v;
    }

    // ---- arithmetic the interpreter does inline ----------------------------------------------------------------

    public static object Inc(object v) => v is int i && i < int.MaxValue ? Rt.Box(i + 1) : Rt.Num(Rt.ToNumber(v) + 1);
    public static object Dec(object v) => v is int i && i > int.MinValue ? Rt.Box(i - 1) : Rt.Num(Rt.ToNumber(v) - 1);
    public static object Sub(object a, object b) => a is int x && b is int y ? Rt.Num((double)x - y) : Rt.Num(Rt.ToNumber(a) - Rt.ToNumber(b));

    // ---- for-in ----------------------------------------------------------------------------------------------

    public static object NextName(Runtime rt, object o, object i) => Interp.EnumName(rt, o, Rt.ToInt32(i));
    public static object NextValue(Runtime rt, object o, object i) => Interp.EnumValue(rt, o, Rt.ToInt32(i));
    public static object HasNext(object o, object i) => Rt.Box(Interp.EnumNext(o, Rt.ToInt32(i)));
    public static object HasNext2(ref object o, ref object i)
    {
        int nx = Interp.EnumNext(o, Rt.ToInt32(i));
        i = Rt.Box(nx);
        if (nx == 0) o = null;
        return Rt.Box(nx != 0);
    }

    public static int Switch(object v, int cases)
    {
        int idx = Rt.ToInt32(v);
        return idx >= 0 && idx < cases ? idx : -1;
    }

    // ---- exceptions ------------------------------------------------------------------------------------------

    public static bool Catchable(Exception e) =>
        e is AsThrow || (e is not BudgetExceeded && e is not StackOverflowException && e is not OutOfMemoryException);

    /// <summary>
    /// The handler of instruction <paramref name="at"/> that takes the thrown value (the interpreter's search, by range
    /// then type): its index, or the exception rethrown when none does.
    /// </summary>
    public static int Handler(AbcMethod m, Exception e, int at, out object value)
    {
        var rt = m.Ctx.Rt;
        var thrown = e is AsThrow t ? t : Interp.Wrap(rt, e);
        var hs = m.Ctx.CodeFor(m.Info).Handlers;
        for (int i = 0; i < hs.Length; i++)
        {
            var hd = hs[i];
            if (at < hd.From || at >= hd.To) continue;
            if (hd.Type != 0)
            {
                var tc = Interp.TypeClass(rt, m.Ctx, hd.Type);
                if (tc != null && !rt.IsType(thrown.Value, tc)) continue;
            }
            value = thrown.Value;
            return i;
        }
        if (e is AsThrow) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e).Throw();
        throw thrown;
    }
}
