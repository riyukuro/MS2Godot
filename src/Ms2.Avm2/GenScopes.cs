namespace Ms2.Avm2;

/// <summary>
/// What a v4 translated body calls where a v3 body passes its scope stack: the same semantics as the v3 helpers in
/// <see cref="Gen"/>, with the method's local scopes held in one ScopeStack local of the body and passed as a span (innermost last).
/// A v4 body has no with scope, so every local scope is an ordinary one.
/// </summary>
public static partial class Gen
{
    /// <summary>
    /// A v4 body's local scope stack, one local of the body (<see cref="Sc"/>, <see cref="At"/>): a single non-generic type
    /// instead of a collection expression per depth, whose span helpers the JIT would compile once per depth.
    /// </summary>
    [System.Runtime.CompilerServices.InlineArray(Size)]
    public struct ScopeStack
    {
        /// <summary>Equal to Translator.ScopeMax: a deeper body is translated as version 3.</summary>
        public const int Size = Ms2.Swf.Abc.Translator.ScopeMax;
        object e;
    }

    /// <summary>The innermost <paramref name="n"/> scopes of <paramref name="s"/>, outermost first.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<object> Sc(in ScopeStack s, int n) =>
        System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
            ref System.Runtime.CompilerServices.Unsafe.As<ScopeStack, object>(ref System.Runtime.CompilerServices.Unsafe.AsRef(in s)), n);

    /// <summary>Scope <paramref name="i"/> of <paramref name="s"/>.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static ref object At(ref ScopeStack s, int i) =>
        ref System.Runtime.CompilerServices.Unsafe.Add(ref System.Runtime.CompilerServices.Unsafe.As<ScopeStack, object>(ref s), i);

    /// <summary>Register <paramref name="i"/> (1 and up) at entry, as <see cref="Locals"/> fills it: a parameter coerced to
    /// its type (an optional one defaulted), the rest or arguments array, or undefined.</summary>
    public static object Reg(AbcMethod m, object[] args, int i)
    {
        var info = m.Info; var ctx = m.Ctx;
        int np = info.ParamCount;
        if (i <= np)
        {
            int p = i - 1;
            int optStart = np - (info.Options?.Count ?? 0);
            object v;
            if (p < args.Length) v = args[p];
            else if (p >= optStart) { var (k, idx) = info.Options[p - optStart]; v = ctx.Constant(k, idx); }
            else v = Rt.Undef;
            var pt = ctx.TypeNameAt(info.ParamTypes[p]);
            return pt == null ? v : ctx.Rt.CoerceTo(pt, v);
        }
        if (i == np + 1)
        {
            if (info.NeedRest) return new AsArray(ctx.Rt, args.Length > np ? args[np..] : Array.Empty<object>());
            if ((info.Flags & 1) != 0) return new AsArray(ctx.Rt, args);
        }
        return Rt.Undef;
    }

    /// <summary>The parameters in registers <paramref name="from"/> and up, which the body does not read: coerced as
    /// <see cref="Locals"/> does, for the coercion's side effects (valueOf, a TypeError).</summary>
    public static void Params(AbcMethod m, object[] args, int from)
    {
        for (int i = from; i <= m.Info.ParamCount; i++) Reg(m, args, i);
    }

    /// <summary>findproperty / findpropstrict, as the v3 <c>FindProp</c>.</summary>
    public static object FindPropS(AbcMethod m, ReadOnlySpan<object> scopes, object self, in Name nm, bool strict)
    {
        var rt = m.Ctx.Rt;
        for (int i = scopes.Length - 1; i >= 0; i--)
            if (Interp.ScopeHas(rt, scopes[i], nm, false)) return scopes[i];
        for (var s = m.Scope; s != null; s = s.Parent)
            if (Interp.ScopeHas(rt, s.Obj, nm, s.With)) return s.Obj;
        var d = rt.FindDefinition(nm);
        if (d != null) return d;
        object global = scopes.Length == 0 && m.Scope == null ? null : GlobalOfV(m, scopes);
        if (global is AsObject g && rt.HasProperty(g, nm)) return global;     // avmplus: the global object's dynamic properties
        if (strict)
        {
            rt.NoteMissing("global " + Interp.Qual(nm));
            throw rt.ReferenceError($"Error #1065: Variable {nm.Local} is not defined.");
        }
        var root = m.Scope; while (root?.Parent != null) root = root.Parent;
        return root?.Obj ?? (scopes.Length > 0 ? scopes[0] : self);
    }

    /// <summary>getlex: findpropstrict, then getproperty on what it found.</summary>
    public static object GetLexV(AbcMethod m, ReadOnlySpan<object> scopes, object self, int mn)
    {
        var nm = m.Ctx.NameAt(mn);
        return m.Ctx.Rt.GetProperty(FindPropS(m, scopes, self, nm, true), nm);
    }

    /// <summary>finddef: the definition, else findpropstrict.</summary>
    public static object FindDefV(AbcMethod m, ReadOnlySpan<object> scopes, object self, int mn)
    {
        var nm = m.Ctx.NameAt(mn);
        return (object)m.Ctx.Rt.FindDefinition(nm) ?? FindPropS(m, scopes, self, nm, true);
    }

    /// <summary>The scope chain a closure or class captures: the method's own, then these local scopes.</summary>
    public static Scope ChainV(AbcMethod m, ReadOnlySpan<object> scopes)
    {
        var s = m.Scope;
        foreach (var o in scopes) s = new Scope(o, false, s);
        return s;
    }

    /// <summary>getglobalscope.</summary>
    public static object GlobalScopeV(AbcMethod m, ReadOnlySpan<object> scopes)
    {
        var g = m.Scope; while (g?.Parent != null) g = g.Parent;
        return g?.Obj ?? (scopes.Length > 0 ? scopes[0] : null);
    }

    static object GlobalOfV(AbcMethod m, ReadOnlySpan<object> scopes)
    {
        var g = m.Scope; while (g?.Parent != null) g = g.Parent;
        if (g?.Obj is { } o) return o;
        if (scopes.Length == 0) throw new ArgumentOutOfRangeException("index");
        return scopes[0];
    }

    public static object GetGlobalSlotV(AbcMethod m, ReadOnlySpan<object> scopes, int slot) => ((AsObject)GlobalOfV(m, scopes)).Slots[slot - 1];
    public static void SetGlobalSlotV(AbcMethod m, ReadOnlySpan<object> scopes, int slot, object v) => ((AsObject)GlobalOfV(m, scopes)).Slots[slot - 1] = v;

    public static object NewFunctionV(AbcMethod m, ReadOnlySpan<object> scopes, int index)
    {
        var rt = m.Ctx.Rt;
        var f = new AsPlainFunction(rt, new AbcMethod(m.Ctx, m.Ctx.Abc.Methods[index], ChainV(m, scopes), null) { Name = "function" });
        f.SetDynamic("prototype", new AsObject(rt.ObjectClass));
        return f;
    }

    public static object NewClassV(AbcMethod m, ReadOnlySpan<object> scopes, int index, object super) =>
        m.Ctx.NewClass(index, super as AsClass, ChainV(m, scopes));
}
