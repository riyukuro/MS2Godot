using Ms2.Swf.Abc;

namespace Ms2.Avm2;

/// <summary>The AVM2 bytecode interpreter: one call of an ABC method.</summary>
public static class Interp
{
    [ThreadStatic] static int depth;
    [ThreadStatic] static List<string> calls;
    /// <summary>Debug hook: called with each ABC method name as it starts.</summary>
    [ThreadStatic] public static Action<string> OnCall;

    /// <summary>The innermost <paramref name="n"/> AS3 methods running now (for error reports).</summary>
    public static string StackString(int n)
    {
        if (calls == null || calls.Count == 0) return "";
        return string.Join(" < ", Enumerable.Range(0, Math.Min(n, calls.Count)).Select(i => calls[calls.Count - 1 - i]));
    }

    /// <summary>Run every method in the interpreter even when translated code is loaded (the A/B oracle).</summary>
    public static bool UseOracle;
    /// <summary>Method calls run by translated code and by the interpreter (A/B bookkeeping).</summary>
    public static long CompiledCalls, InterpretedCalls;

    public static object Run(AbcMethod m, object self, object[] args)
    {
        if (!m.NativeResolved)
        {
            m.NativeResolved = true;
            if (m.Info.Body != null && m.Name.StartsWith("scaleform.gfx.", StringComparison.Ordinal) && m.Ctx.Rt.Natives.TryGetValue(m.Name, out var ov)) m.NativeOverride = ov;
        }
        if (m.NativeOverride != null) return m.NativeOverride.Invoke(self, args);
        if (m.Compiled != null && !UseOracle)
        {
            if (++depth > 400) { depth--; throw m.Ctx.Rt.MakeError("Error", "Error #1023: Stack overflow occurred.", 1023); }
            (calls ??= new()).Add(m.Name); OnCall?.Invoke(m.Name); CompiledCalls++;
            try { return m.Compiled(m, self, args); }
            finally { depth--; calls.RemoveAt(calls.Count - 1); }
        }
        var info = m.Info;
        var body = info.Body;
        if (body == null)
        {
            // a native method declared in AS3 (Scaleform extensions): the runtime may provide it
            var rt0 = m.Ctx.Rt;
            if (rt0.Natives.TryGetValue(m.Name, out var nat)) return nat.Invoke(self, args);
            rt0.NoteMissing("native " + m.Name);
            return Rt.Undef;
        }
        if (++depth > 400) { depth--; throw m.Ctx.Rt.MakeError("Error", "Error #1023: Stack overflow occurred.", 1023); }
        (calls ??= new()).Add(m.Name); OnCall?.Invoke(m.Name); InterpretedCalls++;
        try { return Exec(m, self, args); }
        finally { depth--; calls.RemoveAt(calls.Count - 1); }
    }

    static object Exec(AbcMethod m, object self, object[] args)
    {
        var ctx = m.Ctx; var rt = ctx.Rt; var info = m.Info; var body = info.Body; var abc = ctx.Abc;
        var code = ctx.CodeFor(info);
        var op = code.Op; var A = code.A; var B = code.B;
        var locals = new object[Math.Max(body.LocalCount, info.ParamCount + 2)];
        for (int i = 0; i < locals.Length; i++) locals[i] = Rt.Undef;
        locals[0] = self;
        // parameters: coerced to their types, optional ones defaulted
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

        var stack = new object[body.MaxStack + 4];
        int sp = 0;
        var scopes = new List<(object Obj, bool With)>(body.MaxScopeDepth + 1);
        int pc = 0;
        int n = op.Length;

        object Pop() => stack[--sp];
        void Push(object v) => stack[sp++] = v;

        // runtime multiname parts: pops the name/namespace from the stack as needed
        Name RName(int mn, out object key, out bool hasKey)
        {
            var nm = ctx.NameAt(mn);
            int kind = abc.Multinames[mn].Kind;
            key = null; hasKey = false;
            switch (kind)
            {
                case 0x1B: case 0x1C:
                {
                    var k = Pop();
                    if (k is AsQName q) return new Name(q.Local, new[] { q.Ns });
                    key = k; hasKey = true;
                    return new Name(k is string s ? s : Rt.ToString(k), nm.Set);
                }
                case 0x0F: case 0x10:
                {
                    var nsv = Pop();
                    return new Name(nm.Local, new[] { nsv is AsNamespace an ? an.Ns : Ns.Public });
                }
                case 0x11: case 0x12:
                {
                    var k = Pop(); var nsv = Pop();
                    return new Name(Rt.ToString(k), new[] { nsv is AsNamespace an ? an.Ns : Ns.Public });
                }
            }
            return nm;
        }

        Scope Chain()
        {
            var s = m.Scope;
            foreach (var (o, w) in scopes) s = new Scope(o, w, s);
            return s;
        }

        object FindProp(in Name nm, bool strict)
        {
            for (int i = scopes.Count - 1; i >= 0; i--)
                if (ScopeHas(rt, scopes[i].Obj, nm, scopes[i].With)) return scopes[i].Obj;
            for (var s = m.Scope; s != null; s = s.Parent)
                if (ScopeHas(rt, s.Obj, nm, s.With)) return s.Obj;
            var d = rt.FindDefinition(nm);
            if (d != null) return d;
            // avmplus MethodEnv::findproperty: the global object's dynamic properties last (a name set through findproperty)
            if (GlobalHas(rt, m, scopes, nm, out var go)) return go;
            if (strict)
            {
                rt.NoteMissing("global " + Qual(nm));
                throw rt.ReferenceError($"Error #1065: Variable {nm.Local} is not defined.");
            }
            // findproperty: the global object
            var g = m.Scope; while (g?.Parent != null) g = g.Parent;
            return g?.Obj ?? (scopes.Count > 0 ? scopes[0].Obj : self);
        }

        object[] Args(int count)
        {
            if (count == 0) return Array.Empty<object>();
            var a = new object[count];
            for (int i = count - 1; i >= 0; i--) a[i] = Pop();
            return a;
        }

        while (true)
        {
            try
            {
                while (pc < n)
                {
                    if (rt.Budget > 0 && --rt.budgetLeft < 0) throw new BudgetExceeded();
                    int at = pc++;
                    switch (op[at])
                    {
                        case 0x01: case 0x02: case 0x09: case 0xEF: case 0xF0: case 0xF1: case 0xF2: case 0xF3: case 0x78: case 0x82: case 0x06: break;
                        case 0x07: Pop(); break;
                        case 0x03: throw new AsThrow(Pop());
                        case 0x04: // getsuper
                        {
                            var nm = RName(A[at], out _, out _); var obj = Pop();
                            Push(SuperGet(rt, m, obj, nm));
                            break;
                        }
                        case 0x05: // setsuper
                        {
                            var v = Pop(); var nm = RName(A[at], out _, out _); var obj = Pop();
                            SuperSet(rt, m, obj, nm, v);
                            break;
                        }
                        case 0x08: locals[A[at]] = Rt.Undef; break;
                        case 0x0C: { var b = Pop(); var a = Pop(); if (Rt.LessThan(a, b) != true) pc = code.Target[at]; break; }      // ifnlt
                        case 0x0D: { var b = Pop(); var a = Pop(); if (Rt.LessThan(b, a) != false) pc = code.Target[at]; break; }     // ifnle
                        case 0x0E: { var b = Pop(); var a = Pop(); if (Rt.LessThan(b, a) != true) pc = code.Target[at]; break; }      // ifngt
                        case 0x0F: { var b = Pop(); var a = Pop(); if (Rt.LessThan(a, b) != false) pc = code.Target[at]; break; }     // ifnge
                        case 0x10: pc = code.Target[at]; break;
                        case 0x11: if (Rt.ToBoolean(Pop())) pc = code.Target[at]; break;
                        case 0x12: if (!Rt.ToBoolean(Pop())) pc = code.Target[at]; break;
                        case 0x13: { var b = Pop(); var a = Pop(); if (Rt.LooseEquals(a, b)) pc = code.Target[at]; break; }
                        case 0x14: { var b = Pop(); var a = Pop(); if (!Rt.LooseEquals(a, b)) pc = code.Target[at]; break; }
                        case 0x15: { var b = Pop(); var a = Pop(); if (Rt.LessThan(a, b) == true) pc = code.Target[at]; break; }
                        case 0x16: { var b = Pop(); var a = Pop(); if (Rt.LessThan(b, a) == false) pc = code.Target[at]; break; }
                        case 0x17: { var b = Pop(); var a = Pop(); if (Rt.LessThan(b, a) == true) pc = code.Target[at]; break; }
                        case 0x18: { var b = Pop(); var a = Pop(); if (Rt.LessThan(a, b) == false) pc = code.Target[at]; break; }
                        case 0x19: { var b = Pop(); var a = Pop(); if (Rt.StrictEquals(a, b)) pc = code.Target[at]; break; }
                        case 0x1A: { var b = Pop(); var a = Pop(); if (!Rt.StrictEquals(a, b)) pc = code.Target[at]; break; }
                        case 0x1B: // lookupswitch
                        {
                            int idx = Rt.ToInt32(Pop()); var cs = code.Cases[at];
                            pc = idx >= 0 && idx + 1 < cs.Length ? cs[idx + 1] : cs[0];
                            break;
                        }
                        case 0x1C: scopes.Add((Pop(), true)); break;
                        case 0x1D: scopes.RemoveAt(scopes.Count - 1); break;
                        case 0x1E: { int i = Rt.ToInt32(Pop()); var o = Pop(); Push(EnumName(rt, o, i)); break; }
                        case 0x23: { int i = Rt.ToInt32(Pop()); var o = Pop(); Push(EnumValue(rt, o, i)); break; }
                        case 0x1F: { int i = Rt.ToInt32(Pop()); var o = Pop(); Push(Rt.Box(EnumNext(o, i))); break; }
                        case 0x32: // hasnext2
                        {
                            var o = locals[A[at]]; int i = Rt.ToInt32(locals[B[at]]);
                            int nx = EnumNext(o, i);
                            locals[B[at]] = Rt.Box(nx);
                            if (nx == 0) locals[A[at]] = null;
                            Push(Rt.Box(nx != 0));
                            break;
                        }
                        case 0x20: Push(null); break;
                        case 0x21: Push(Rt.Undef); break;
                        case 0x24: Push(Rt.Box(A[at])); break;
                        case 0x25: Push(Rt.Box((short)A[at])); break;
                        case 0x26: Push(Rt.True); break;
                        case 0x27: Push(Rt.False); break;
                        case 0x28: Push(double.NaN); break;
                        case 0x29: sp--; break;
                        case 0x2A: stack[sp] = stack[sp - 1]; sp++; break;
                        case 0x2B: { var t = stack[sp - 1]; stack[sp - 1] = stack[sp - 2]; stack[sp - 2] = t; break; }
                        case 0x2C: Push(abc.Strings[A[at]]); break;
                        case 0x2D: Push(Rt.Box(abc.Ints[A[at]])); break;
                        case 0x2E: Push(Rt.Uint(abc.Uints[A[at]])); break;
                        case 0x2F: Push(Rt.Num(abc.Doubles[A[at]])); break;
                        case 0x30: scopes.Add((Pop(), false)); break;
                        case 0x31: Push(new AsNamespace(rt, ctx.NsAt(A[at]))); break;
                        case 0x40: // newfunction
                        {
                            var f = new AsPlainFunction(rt, new AbcMethod(ctx, abc.Methods[A[at]], Chain(), null) { Name = "function" });
                            f.SetDynamic("prototype", new AsObject(rt.ObjectClass));
                            Push(f);
                            break;
                        }
                        case 0x41: // call
                        {
                            var a = Args(A[at]); var recv = Pop(); var f = Pop();
                            Push(rt.Invoke(f, recv, a, "value"));
                            break;
                        }
                        case 0x42: // construct
                        {
                            var a = Args(A[at]); var c = Pop();
                            Push(rt.Construct(c, a, "value"));
                            break;
                        }
                        case 0x43: case 0x44:
                            throw rt.MakeError("VerifyError", "callmethod/callstatic not supported");
                        case 0x45: case 0x4E: // callsuper / callsupervoid
                        {
                            var a = Args(B[at]); var nm = RName(A[at], out _, out _); var recv = Pop();
                            var r = SuperCall(rt, m, recv, nm, a);
                            if (op[at] == 0x45) Push(r);
                            break;
                        }
                        case 0x46: case 0x4C: case 0x4F: // callproperty / callproplex / callpropvoid
                        {
                            var a = Args(B[at]); var nm = RName(A[at], out var key, out var hk); var obj = Pop();
                            var r = rt.CallProperty(obj, nm, a, key, hk);
                            if (op[at] != 0x4F) Push(r);
                            break;
                        }
                        case 0x47: return Rt.Undef;
                        case 0x48: return Pop();
                        case 0x49: // constructsuper
                        {
                            var a = Args(A[at]); var obj = Pop();
                            m.Declaring?.Super?.Ctor?.Invoke(obj, a);
                            break;
                        }
                        case 0x4A: // constructprop
                        {
                            var a = Args(B[at]); var nm = RName(A[at], out var key, out var hk); var obj = Pop();
                            var c = rt.GetProperty(obj, nm, key, hk);
                            Push(rt.Construct(c, a, Qual(nm)));
                            break;
                        }
                        case 0x53: // applytype
                        {
                            var a = Args(A[at]); var f = Pop();
                            Push(rt.ApplyType(f, a));
                            break;
                        }
                        case 0x55: // newobject
                        {
                            int c = A[at]; var o = new AsObject(rt.ObjectClass);
                            var kv = Args(c * 2);
                            for (int i = 0; i < c; i++) o.SetDynamic(Rt.ToString(kv[2 * i]), kv[2 * i + 1]);
                            Push(o);
                            break;
                        }
                        case 0x56: Push(new AsArray(rt, Args(A[at]))); break;
                        case 0x57: // newactivation
                        {
                            var t = ctx.ActivationTraits(info);
                            var o = new AsObject { Class = rt.ObjectClass, Traits = t, Slots = t.NewSlots() };
                            Push(o);
                            break;
                        }
                        case 0x58: // newclass
                        {
                            var sup = Pop() as AsClass;
                            Push(ctx.NewClass(A[at], sup, Chain()));
                            break;
                        }
                        case 0x59: Pop(); throw rt.MakeError("Error", "E4X descendants are not supported");
                        case 0x5A: // newcatch
                        {
                            var h = info.Body.Exceptions[A[at]];
                            var t = new TraitTable();
                            if (h.VarName != 0) t.AddSlot(ctx.NsAt(abc.Multinames[h.VarName].NsIndex), abc.Multinames[h.VarName].Name, 1, Rt.Undef, null, false);
                            Push(new AsObject { Class = rt.ObjectClass, Traits = t, Slots = t.NewSlots() });
                            break;
                        }
                        case 0x5D: case 0x5E: // findpropstrict / findproperty
                        {
                            var nm = RName(A[at], out _, out _);
                            Push(FindProp(nm, op[at] == 0x5D));
                            break;
                        }
                        case 0x5F: // finddef
                        {
                            var nm = ctx.NameAt(A[at]);
                            Push((object)rt.FindDefinition(nm) ?? FindProp(nm, true));
                            break;
                        }
                        case 0x60: // getlex
                        {
                            var nm = ctx.NameAt(A[at]);
                            var holder = FindProp(nm, true);
                            Push(rt.GetProperty(holder, nm));
                            break;
                        }
                        case 0x61: case 0x68: // setproperty / initproperty
                        {
                            var v = Pop(); var nm = RName(A[at], out var key, out var hk); var obj = Pop();
                            rt.SetProperty(obj, nm, v, key, hk, op[at] == 0x68);
                            break;
                        }
                        case 0x62: Push(locals[A[at]]); break;
                        case 0x63: locals[A[at]] = Pop(); break;
                        case 0xD0: case 0xD1: case 0xD2: case 0xD3: Push(locals[op[at] - 0xD0]); break;
                        case 0xD4: case 0xD5: case 0xD6: case 0xD7: locals[op[at] - 0xD4] = Pop(); break;
                        case 0x64: { var g = m.Scope; while (g?.Parent != null) g = g.Parent; Push(g?.Obj ?? (scopes.Count > 0 ? scopes[0].Obj : null)); break; }
                        case 0x65: Push(scopes[A[at]].Obj); break;
                        case 0x66: // getproperty
                        {
                            var nm = RName(A[at], out var key, out var hk); var obj = Pop();
                            Push(rt.GetProperty(obj, nm, key, hk));
                            break;
                        }
                        case 0x6A: // deleteproperty
                        {
                            var nm = RName(A[at], out var key, out var hk); var obj = Pop();
                            Push(Rt.Box(rt.DeleteProperty(obj, nm, key, hk)));
                            break;
                        }
                        case 0x6C: { var o = (AsObject)Pop(); Push(o.Slots[A[at] - 1]); break; }
                        case 0x6D: { var v = Pop(); var o = (AsObject)Pop(); o.Slots[A[at] - 1] = CoerceSlot(rt, o, A[at] - 1, v); break; }
                        case 0x6E: { var g = (AsObject)GlobalOf(m, scopes); Push(g.Slots[A[at] - 1]); break; }
                        case 0x6F: { var v = Pop(); var g = (AsObject)GlobalOf(m, scopes); g.Slots[A[at] - 1] = v; break; }
                        case 0x70: case 0x71: case 0x72: Push(Rt.ToString(Pop())); break;
                        case 0x73: Push(Rt.Box(Rt.ToInt32(Pop()))); break;
                        case 0x74: Push(Rt.Uint(Rt.ToUint32(Pop()))); break;
                        case 0x75: Push(Rt.Num(Rt.ToNumber(Pop()))); break;
                        case 0x76: Push(Rt.Box(Rt.ToBoolean(Pop()))); break;
                        case 0x77: { var v = stack[sp - 1]; if (Rt.IsNullish(v)) throw rt.TypeError("Error #1009: Cannot access a property or method of a null object reference."); break; }
                        case 0x80: // coerce
                        {
                            var t = ctx.TypeNameAt(A[at]);
                            stack[sp - 1] = Coerce(rt, ctx, A[at], t, stack[sp - 1]);
                            break;
                        }
                        case 0x85: stack[sp - 1] = Rt.CoerceString(stack[sp - 1]); break;
                        case 0x86: { var t = TypeClass(rt, ctx, A[at]); var v = Pop(); Push(t == null || rt.IsType(v, t) ? v : null); break; }
                        case 0x87: { var t = Pop() as AsClass; var v = Pop(); Push(t == null || rt.IsType(v, t) ? v : null); break; }
                        case 0x90: Push(Rt.Num(-Rt.ToNumber(Pop()))); break;
                        case 0x91: { var v = Pop(); Push(v is int i && i < int.MaxValue ? Rt.Box(i + 1) : Rt.Num(Rt.ToNumber(v) + 1)); break; }
                        case 0x93: { var v = Pop(); Push(v is int i && i > int.MinValue ? Rt.Box(i - 1) : Rt.Num(Rt.ToNumber(v) - 1)); break; }
                        case 0x92: { var v = locals[A[at]]; locals[A[at]] = v is int i && i < int.MaxValue ? Rt.Box(i + 1) : Rt.Num(Rt.ToNumber(v) + 1); break; }
                        case 0x94: { var v = locals[A[at]]; locals[A[at]] = v is int i && i > int.MinValue ? Rt.Box(i - 1) : Rt.Num(Rt.ToNumber(v) - 1); break; }
                        case 0x95: Push(Rt.TypeOf(Pop())); break;
                        case 0x96: Push(Rt.Box(!Rt.ToBoolean(Pop()))); break;
                        case 0x97: Push(Rt.Box(~Rt.ToInt32(Pop()))); break;
                        case 0xA0: { var b = Pop(); var a = Pop(); Push(Rt.Add(a, b)); break; }
                        case 0xA1: { var b = Pop(); var a = Pop(); Push(a is int x && b is int y ? Rt.Num((double)x - y) : Rt.Num(Rt.ToNumber(a) - Rt.ToNumber(b))); break; }
                        case 0xA2: { var b = Pop(); var a = Pop(); Push(Rt.Num(Rt.ToNumber(a) * Rt.ToNumber(b))); break; }
                        case 0xA3: { var b = Pop(); var a = Pop(); Push(Rt.Num(Rt.ToNumber(a) / Rt.ToNumber(b))); break; }
                        case 0xA4: { var b = Pop(); var a = Pop(); Push(Rt.Num(Rt.ToNumber(a) % Rt.ToNumber(b))); break; }
                        case 0xA5: { var b = Pop(); var a = Pop(); Push(Rt.Box(Rt.ToInt32(a) << (Rt.ToInt32(b) & 31))); break; }
                        case 0xA6: { var b = Pop(); var a = Pop(); Push(Rt.Box(Rt.ToInt32(a) >> (Rt.ToInt32(b) & 31))); break; }
                        case 0xA7: { var b = Pop(); var a = Pop(); Push(Rt.Uint(Rt.ToUint32(a) >> (Rt.ToInt32(b) & 31))); break; }
                        case 0xA8: { var b = Pop(); var a = Pop(); Push(Rt.Box(Rt.ToInt32(a) & Rt.ToInt32(b))); break; }
                        case 0xA9: { var b = Pop(); var a = Pop(); Push(Rt.Box(Rt.ToInt32(a) | Rt.ToInt32(b))); break; }
                        case 0xAA: { var b = Pop(); var a = Pop(); Push(Rt.Box(Rt.ToInt32(a) ^ Rt.ToInt32(b))); break; }
                        case 0xAB: { var b = Pop(); var a = Pop(); Push(Rt.Box(Rt.LooseEquals(a, b))); break; }
                        case 0xAC: { var b = Pop(); var a = Pop(); Push(Rt.Box(Rt.StrictEquals(a, b))); break; }
                        case 0xAD: { var b = Pop(); var a = Pop(); Push(Rt.Box(Rt.LessThan(a, b) == true)); break; }
                        case 0xAE: { var b = Pop(); var a = Pop(); Push(Rt.Box(Rt.LessThan(b, a) == false)); break; }
                        case 0xAF: { var b = Pop(); var a = Pop(); Push(Rt.Box(Rt.LessThan(b, a) == true)); break; }
                        case 0xB0: { var b = Pop(); var a = Pop(); Push(Rt.Box(Rt.LessThan(a, b) == false)); break; }
                        case 0xB1: { var b = Pop(); var a = Pop(); Push(Rt.Box(InstanceOf(rt, a, b))); break; }
                        case 0xB2: { var t = TypeClass(rt, ctx, A[at]); var v = Pop(); Push(Rt.Box(t == null || rt.IsType(v, t))); break; }
                        case 0xB3: { var t = Pop() as AsClass; var v = Pop(); Push(Rt.Box(t != null && rt.IsType(v, t))); break; }
                        case 0xB4: { var o = Pop(); var k = Pop(); Push(Rt.Box(rt.HasProperty(o, Name.Public(k is string s ? s : Rt.ToString(k)), k, true))); break; }
                        case 0xC0: Push(Rt.Box(Rt.ToInt32(Pop()) + 1)); break;
                        case 0xC1: Push(Rt.Box(Rt.ToInt32(Pop()) - 1)); break;
                        case 0xC2: locals[A[at]] = Rt.Box(Rt.ToInt32(locals[A[at]]) + 1); break;
                        case 0xC3: locals[A[at]] = Rt.Box(Rt.ToInt32(locals[A[at]]) - 1); break;
                        case 0xC4: Push(Rt.Box(-Rt.ToInt32(Pop()))); break;
                        case 0xC5: { var b = Pop(); var a = Pop(); Push(Rt.Box(Rt.ToInt32(a) + Rt.ToInt32(b))); break; }
                        case 0xC6: { var b = Pop(); var a = Pop(); Push(Rt.Box(Rt.ToInt32(a) - Rt.ToInt32(b))); break; }
                        case 0xC7: { var b = Pop(); var a = Pop(); Push(Rt.Box(Rt.ToInt32(a) * Rt.ToInt32(b))); break; }
                        default:
                            throw rt.MakeError("VerifyError", "unsupported opcode 0x" + op[at].ToString("x2"));
                    }
                }
                return Rt.Undef;
            }
            catch (Exception e) when (e is AsThrow || (e is not BudgetExceeded && e is not StackOverflowException && e is not OutOfMemoryException))
            {
                var thrown = e is AsThrow t ? t : Wrap(rt, e);
                int at = pc - 1;
                int h = -1;
                for (int i = 0; i < code.Handlers.Length; i++)
                {
                    var hd = code.Handlers[i];
                    if (at < hd.From || at >= hd.To) continue;
                    if (hd.Type != 0)
                    {
                        var tc = TypeClass(rt, ctx, hd.Type);
                        if (tc != null && !rt.IsType(thrown.Value, tc)) continue;
                    }
                    h = i; break;
                }
                if (h < 0) { if (e is AsThrow) throw; throw thrown; }
                sp = 0; Push(thrown.Value);
                scopes.Clear();
                pc = code.Handlers[h].Target;
            }
        }
    }

    /// <summary>A C# failure inside a native (bad cast, null) as an AS error, so AS code can catch it.</summary>
    internal static AsThrow Wrap(Runtime rt, Exception e) => e switch
    {
        InvalidCastException => rt.TypeError("Error #1034: Type Coercion failed: " + e.Message),
        NullReferenceException => rt.TypeError("Error #1009: Cannot access a property or method of a null object reference. (native) " + e.StackTrace?.Split('\n').FirstOrDefault()?.Trim()),
        IndexOutOfRangeException or ArgumentOutOfRangeException => rt.RangeError("Error #1125: The index is out of range. " + e.Message),
        _ => rt.MakeError("Error", "internal " + e.GetType().Name + ": " + e.Message),
    };

    internal static string Qual(in Name n) => n.Set != null && n.Set.Length > 0 && n.Set[0].Uri.Length > 0 && n.Set[0].Kind == NsKind.Public && n.Set.Length == 1 ? n.Set[0].Uri + "." + n.Local : n.Local;

    internal static bool ScopeHas(Runtime rt, object o, in Name n, bool with)
    {
        if (o is not AsObject ao) return false;
        if (ao.Traits?.Find(n) != null) return true;
        if (with && rt.HasProperty(ao, n)) return true;
        return false;
    }

    /// <summary>avmplus MethodEnv::findproperty, after the definitions: the global object (scope 0) has the name as a
    /// property, as `x = v` with no declaration leaves it there (findproperty gives the global object to setproperty).</summary>
    internal static bool GlobalHas(Runtime rt, AbcMethod m, List<(object Obj, bool With)> scopes, in Name n, out object global)
    {
        global = scopes.Count == 0 && m.Scope == null ? null : GlobalOf(m, scopes);
        return global is AsObject g && rt.HasProperty(g, n);
    }

    internal static object GlobalOf(AbcMethod m, List<(object Obj, bool With)> scopes)
    {
        var g = m.Scope; while (g?.Parent != null) g = g.Parent;
        return g?.Obj ?? scopes[0].Obj;
    }

    internal static object CoerceSlot(Runtime rt, AsObject o, int slot, object v)
    {
        var types = o.Traits?.SlotTypes;
        return types != null && slot < types.Count ? rt.CoerceTo(types[slot], v) : v;
    }

    internal static AsClass TypeClass(Runtime rt, AbcContext ctx, int mn)
    {
        if (mn == 0) return null;
        var nm = ctx.NameAt(mn);
        if (nm.Local == "*" || nm.Local == null) return null;
        var holder = rt.FindDefinition(nm);
        if (holder == null) { rt.NoteMissing("type " + Qual(nm)); return null; }
        var v = rt.GetProperty(holder, nm);
        if (ctx.Abc.Multinames[mn].Kind == 0x1D) return rt.VectorClass;
        return v as AsClass;
    }

    internal static object Coerce(Runtime rt, AbcContext ctx, int mn, string t, object v)
    {
        switch (t)
        {
            case "int": case "uint": case "Number": case "Boolean": case "String": return rt.CoerceTo(t, v);
            case null: case "*": return v;
        }
        if (Rt.IsNullish(v)) return null;
        var c = TypeClass(rt, ctx, mn);
        if (c == null || c.IsInterface) return v;
        if (!rt.IsType(v, c))
            throw rt.TypeError($"Error #1034: Type Coercion failed: cannot convert {Rt.Describe(v)} to {c.Name}.");
        return v;
    }

    internal static bool InstanceOf(Runtime rt, object v, object t)
    {
        if (t is AsClass c) return rt.IsType(v, c);
        if (t is AsFunction f && v is AsObject o && f.GetDynamic("prototype") is AsObject proto)
            for (var p = rt.ProtoOf(o); p != null; p = rt.ProtoOf(p)) if (p == proto) return true;
        return false;
    }

    // ---- super ---------------------------------------------------------------------------------------------

    internal static Binding SuperBinding(Runtime rt, AbcMethod m, in Name n)
    {
        var sup = m.Declaring?.Super;
        if (sup == null) throw rt.ReferenceError($"no super class for {n.Local}");
        return sup.InstanceTraits.Find(n);
    }

    internal static object SuperGet(Runtime rt, AbcMethod m, object obj, in Name n)
    {
        var b = SuperBinding(rt, m, n);
        if (b == null) return rt.GetProperty(obj, n);
        return b.Kind switch
        {
            BindingKind.Method => new AsMethodClosure(rt, obj, b.Method),
            BindingKind.Accessor => b.Getter != null ? b.Getter.Invoke(obj, Array.Empty<object>()) : Rt.Undef,
            _ => ((AsObject)obj).Slots[b.Slot],
        };
    }

    internal static void SuperSet(Runtime rt, AbcMethod m, object obj, in Name n, object v)
    {
        var b = SuperBinding(rt, m, n);
        if (b == null) { rt.SetProperty(obj, n, v); return; }
        if (b.Kind == BindingKind.Accessor) { b.Setter?.Invoke(obj, new[] { v }); return; }
        if (b.Kind is BindingKind.Slot or BindingKind.Const) ((AsObject)obj).Slots[b.Slot] = v;
    }

    internal static object SuperCall(Runtime rt, AbcMethod m, object obj, in Name n, object[] a)
    {
        var b = SuperBinding(rt, m, n);
        if (b == null) return rt.CallProperty(obj, n, a);
        if (b.Kind == BindingKind.Method) return b.Method.Invoke(obj, a);
        return rt.Invoke(SuperGet(rt, m, obj, n), obj, a, n.Local);
    }

    // ---- for-in --------------------------------------------------------------------------------------------

    internal static int EnumNext(object o, int i)
    {
        if (o is not AsObject ao) return 0;
        var keys = ao.EnumKeys();
        return i < keys.Count ? i + 1 : 0;
    }

    internal static object EnumName(Runtime rt, object o, int i)
    {
        if (o is not AsObject ao) return Rt.Undef;
        var keys = ao.EnumKeys();
        return i >= 1 && i <= keys.Count ? keys[i - 1] : Rt.Undef;
    }

    internal static object EnumValue(Runtime rt, object o, int i)
    {
        var k = EnumName(rt, o, i);
        if (Rt.IsUndef(k)) return k;
        return rt.GetProperty(o, Name.Public(k is string s ? s : Rt.ToString(k)), k, true);
    }
}
