using System.Text;

namespace Ms2.Swf.Abc;

/// <summary>
/// Translates an ABC block's method bodies into C# that runs on the Ms2.Avm2 runtime: one static method per body with
/// the interpreter's contract (<c>object M(AbcMethod m, object self, object[] args)</c>), registered under the block's
/// hash. The operand stack becomes C# locals (its depth at each instruction is fixed, so a merge point needs nothing);
/// the registers stay in the interpreter's array; every instruction calls the interpreter's own semantics (Ms2.Avm2.Gen,
/// Rt, Runtime). Branches are labels and gotos; a body with exception handlers runs inside one try whose catch picks the
/// handler as the interpreter does and jumps to it. Classes and traits are not translated: the runtime still builds
/// them from the player's ABC at load, and the bodies read constants from it.
/// A body never names a constant pool index: each pool operand is <c>K[j]</c>, the j-th entry of
/// <see cref="ConstOperands"/> of the running method, which the runtime derives from that method's own bytecode. So a
/// body is the same text wherever the same code sits (the CLIK and shared ms2 classes every movie carries), and one
/// translated method serves them all.
/// </summary>
public sealed class Translator
{
    readonly AbcFile abc;
    public int Methods, Skipped;
    public readonly HashSet<string> Unsupported = new();
    /// <summary>Version 4: registers and the scope stack in C# locals (the scope helpers take the stack as a span).
    /// A method that needs what it cannot do is translated as version 3, counted here by reason.</summary>
    public readonly bool V4;
    public readonly Dictionary<string, int> Fallbacks = new();

    public Translator(AbcFile abc, bool v4 = false) { this.abc = abc; V4 = v4; }

    /// <summary>The placeholder <see cref="Body"/> puts where the method's name goes.</summary>
    public const string NamePlaceholder = "@NAME@";

    /// <summary>
    /// The C# of one method body, named <see cref="NamePlaceholder"/>; null when the method has no body or cannot be
    /// translated (it then runs in the interpreter).
    /// </summary>
    public string Body(MethodInfo m)
    {
        if (m.Body == null) return null;
        try
        {
            var t = Method(m);
            if (t == null) { Skipped++; return null; }
            Methods++;
            return t;
        }
        catch (Exception e) { Skipped++; Unsupported.Add("method failed: " + e.Message); return null; }
    }

    /// <summary>Opcodes whose first operand is an index into a constant pool of the block (multiname, string, int, uint,
    /// double, namespace, method or class).</summary>
    static bool PoolOperand(byte op) => op is 0x04 or 0x05 or 0x2C or 0x2D or 0x2E or 0x2F or 0x31 or 0x40 or 0x45 or 0x46
        or 0x4A or 0x4C or 0x4E or 0x4F or 0x58 or 0x59 or 0x5D or 0x5E or 0x5F or 0x60 or 0x61 or 0x66 or 0x68 or 0x6A
        or 0x80 or 0x86 or 0xB2;

    /// <summary>The pool operands of a body in instruction order: what a translated body reads as <c>K</c>.</summary>
    public static int[] ConstOperands(MethodInfo m)
    {
        if (m.Body == null) return Array.Empty<int>();
        var k = new List<int>();
        foreach (var x in Disasm.Decode(m.Body.Code)) if (PoolOperand(x.Op)) k.Add(x.A);
        return k.ToArray();
    }

    /// <summary>The usings and pragmas a file of translated classes starts with.</summary>
    public const string Header =
        "// <ms2-generated> translated from the player's own movie data; never commit this file\n" +
        "#pragma warning disable CS0162, CS0164, CS0219, CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625\n" +
        "using System;\nusing System.Collections.Generic;\nusing Ms2.Avm2;\n\n";

    // ---- one method --------------------------------------------------------------------------------------------

    sealed class Code
    {
        public List<Insn> Ins;
        public int[] Target; public int[][] Cases;
        public (int From, int To, int Target)[] Handlers;
        /// <summary>Per instruction, its pool operand's index in <see cref="ConstOperands"/>; -1 for none.</summary>
        public int[] KAt;
    }

    Code Decode(MethodInfo m)
    {
        var ins = Disasm.Decode(m.Body.Code);
        var at = new Dictionary<int, int>();
        for (int i = 0; i < ins.Count; i++) at[ins[i].Off] = i;
        at[m.Body.Code.Length] = ins.Count;
        int Idx(int off)
        {
            if (at.TryGetValue(off, out var x)) return x;
            int best = int.MaxValue, idx = at.Count;
            foreach (var (o, i) in at) if (o >= off && o < best) { best = o; idx = i; }
            return idx;
        }
        var kAt = new int[ins.Count];
        for (int i = 0, j = 0; i < ins.Count; i++) kAt[i] = PoolOperand(ins[i].Op) ? j++ : -1;
        return new Code
        {
            Ins = ins, KAt = kAt,
            Target = ins.Select(i => i.Target >= 0 ? Idx(i.Target) : -1).ToArray(),
            Cases = ins.Select(i => i.Cases?.Select(Idx).ToArray()).ToArray(),
            Handlers = m.Body.Exceptions.Select(e => (Idx(e.From), Idx(e.To), Idx(e.Target))).ToArray(),
        };
    }

    /// <summary>How many stack values a multiname takes at run time (the name and/or namespace).</summary>
    int RCount(int mn) => abc.Multinames[mn].Kind switch
    {
        0x1B or 0x1C or 0x0F or 0x10 => 1,
        0x11 or 0x12 => 2,
        _ => 0,
    };

    /// <summary>(pops, pushes) of an instruction, null for one the interpreter does not run.</summary>
    (int Pop, int Push)? Effect(Insn x)
    {
        int a = x.A, b = x.B;
        switch (x.Op)
        {
            case 0x01: case 0x02: case 0x09: case 0xEF: case 0xF0: case 0xF1: case 0xF2: case 0xF3: case 0x78: case 0x82: case 0x06:
            case 0x08: case 0x10: case 0x1D: case 0x47: case 0x92: case 0x94: case 0xC2: case 0xC3: return (0, 0);
            case 0x07: case 0x03: case 0x11: case 0x12: case 0x1B: case 0x1C: case 0x29: case 0x30: case 0x48: case 0x63:
            case 0xD4: case 0xD5: case 0xD6: case 0xD7: case 0x6F: return (1, 0);
            case 0x04: return (1 + RCount(a), 1);
            case 0x05: return (2 + RCount(a), 0);
            case >= 0x0C and <= 0x0F: case >= 0x13 and <= 0x1A: return (2, 0);
            case 0x1E: case 0x23: case 0x1F: return (2, 1);
            case 0x32: case 0x20: case 0x21: case 0x24: case 0x25: case 0x26: case 0x27: case 0x28: case 0x2C: case 0x2D: case 0x2E:
            case 0x2F: case 0x31: case 0x40: case 0x57: case 0x5A: case 0x5F: case 0x60: case 0x62: case 0x64: case 0x65: case 0x6E:
            case 0xD0: case 0xD1: case 0xD2: case 0xD3: return (0, 1);
            case 0x2A: return (1, 2);
            case 0x2B: return (2, 2);
            case 0x41: return (2 + a, 1);
            case 0x42: case 0x53: return (1 + a, 1);
            case 0x45: case 0x46: case 0x4C: case 0x4A: return (1 + RCount(a) + b, 1);
            case 0x4E: case 0x4F: return (1 + RCount(a) + b, 0);
            case 0x49: return (1 + a, 0);
            case 0x55: return (2 * a, 1);
            case 0x56: return (a, 1);
            case 0x58: return (1, 1);
            case 0x59: return (1 + RCount(a), 1);
            case 0x5D: case 0x5E: return (RCount(a), 1);
            case 0x61: case 0x68: return (2 + RCount(a), 0);
            case 0x66: case 0x6A: return (1 + RCount(a), 1);
            case 0x6C: return (1, 1);
            case 0x6D: return (2, 0);
            case 0x70: case 0x71: case 0x72: case 0x73: case 0x74: case 0x75: case 0x76: case 0x77: case 0x80: case 0x85: case 0x86:
            case 0x90: case 0x91: case 0x93: case 0x95: case 0x96: case 0x97: case 0xB2: case 0xC0: case 0xC1: case 0xC4: return (1, 1);
            case 0x87: case 0xB3: return (2, 1);
            case >= 0xA0 and <= 0xB1: case 0xB4: case 0xC5: case 0xC6: case 0xC7: return (2, 1);
            case 0x43: case 0x44: return (1 + b, 1);
        }
        return null;
    }

    static bool Ends(byte op) => op is 0x03 or 0x10 or 0x1B or 0x47 or 0x48;

    // the method being translated as version 4: its scope depth per instruction and its register count
    bool v4;
    int[] sdepth;
    int regs, used;
    /// <summary>The deepest local scope stack of a version 4 body: the size of Gen.ScopeStack (Ms2.Avm2), kept equal by hand.</summary>
    public const int ScopeMax = 32;

    string Reg(int a) => v4 ? "r" + a : $"L[{a}]";
    string Scopes(int i)
    {
        if (!v4) return "S";
        int k = sdepth[i];
        return k == 0 ? "default" : $"Gen.Sc(in c, {k})";
    }

    static bool RegOp(byte op) => op is 0x08 or 0x62 or 0x63 or 0x92 or 0x94 or 0xC2 or 0xC3 or 0x32;

    /// <summary>Why <paramref name="m"/> cannot be version 4 (null when it can): fills the scope depths and register count.</summary>
    string V4Check(MethodInfo m, Code c, int[] depth)
    {
        var ins = c.Ins; int n = ins.Count;
        regs = Math.Max(m.Body.LocalCount, m.ParamCount + 2);
        used = 1;
        var sd = new int[n + 1];
        Array.Fill(sd, -1);
        var work = new Stack<int>();
        string bad = null;
        void Reach(int i, int d)
        {
            if (i < 0 || i > n) return;
            if (sd[i] == -1) { sd[i] = d; work.Push(i); }
            else if (sd[i] != d) bad ??= "scope depth";
        }
        Reach(0, 0);
        foreach (var h in c.Handlers) Reach(h.Target, 0);
        while (work.Count > 0 && bad == null)
        {
            int i = work.Pop();
            if (i >= n || depth[i] < 0) continue;
            var x = ins[i];
            int d = sd[i];
            switch (x.Op)
            {
                case 0x1C: return "with";
                case 0x30: if (++d > ScopeMax) return "scope depth > " + ScopeMax; break;
                case 0x1D: if (--d < 0) return "scope underflow"; break;
                case 0x65: if (x.A >= d) return "scope index"; break;
            }
            if (RegOp(x.Op) && (x.A >= regs || x.Op == 0x32 && x.B >= regs)) return "register range";
            if (x.Op is >= 0xD0 and <= 0xD7 && (x.Op & 3) >= regs) return "register range";
            if (RegOp(x.Op)) used = Math.Max(used, Math.Max(x.A, x.Op == 0x32 ? x.B : 0) + 1);
            if (x.Op is >= 0xD0 and <= 0xD7) used = Math.Max(used, (x.Op & 3) + 1);
            if (x.Op == 0x1B) { foreach (int t in c.Cases[i]) Reach(t, d); continue; }
            if (x.Op >= 0x0C && x.Op <= 0x1A) Reach(c.Target[i], d);
            if (!Ends(x.Op)) Reach(i + 1, d);
        }
        sdepth = sd;
        return bad;
    }

    string Method(MethodInfo m)
    {
        var c = Decode(m);
        var ins = c.Ins;
        int n = ins.Count;
        var depth = new int[n + 1];
        Array.Fill(depth, -1);
        var work = new Stack<int>();
        void Reach(int i, int d)
        {
            if (i < 0 || i > n) return;
            if (depth[i] == -1) { depth[i] = d; work.Push(i); }
            else if (depth[i] != d) throw new InvalidOperationException($"stack depth {depth[i]} and {d} meet at {i}");
        }
        Reach(0, 0);
        foreach (var h in c.Handlers) Reach(h.Target, 1);
        while (work.Count > 0)
        {
            int i = work.Pop();
            if (i >= n) continue;
            var x = ins[i];
            var e = Effect(x);
            if (e == null) { Unsupported.Add(x.Name ?? ("0x" + x.Op.ToString("x2"))); continue; }
            int d = depth[i] - e.Value.Pop + e.Value.Push;
            if (d < 0) throw new InvalidOperationException($"stack underflow at {i}");
            if (x.Op == 0x1B) { foreach (int t in c.Cases[i]) Reach(t, d); continue; }
            if (x.Op >= 0x0C && x.Op <= 0x1A) Reach(c.Target[i], d);
            if (!Ends(x.Op)) Reach(i + 1, d);
        }
        int maxDepth = depth.Max() + 2;
        var labels = new HashSet<int>();
        for (int i = 0; i < n; i++)
        {
            if (depth[i] < 0) continue;
            if (c.Target[i] >= 0) labels.Add(c.Target[i]);
            if (c.Cases[i] != null) foreach (int t in c.Cases[i]) labels.Add(t);
        }
        foreach (var h in c.Handlers) labels.Add(h.Target);
        bool eh = c.Handlers.Length > 0;
        v4 = false;
        if (V4)
        {
            var why = V4Check(m, c, depth);
            if (why != null) Fallbacks[why] = Fallbacks.GetValueOrDefault(why) + 1;
            else v4 = true;
        }

        var sb = new StringBuilder();
        string ind = "        ";
        if (eh)
        {
            sb.AppendLine("        int E = -1, P = 0;");
            sb.AppendLine("        for (;;)");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            ind = "                ";
            sb.Append(ind).Append("switch (E) { ");
            for (int h = 0; h < c.Handlers.Length; h++) sb.Append($"case {h}: goto L{c.Handlers[h].Target}; ");
            sb.AppendLine("}");
        }
        for (int i = 0; i < n; i++)
        {
            if (labels.Contains(i)) sb.Append(ind).AppendLine($"L{i}: Gen.Tick(rt);");
            if (depth[i] < 0) continue;
            if (eh) sb.Append(ind).Append($"P = {i}; ");
            else sb.Append(ind);
            sb.AppendLine(Insn(c, i, depth[i]));
        }
        if (labels.Contains(n)) sb.Append(ind).AppendLine($"L{n}:");
        sb.Append(ind).AppendLine("return Rt.Undef;");
        if (eh)
        {
            sb.AppendLine("            }");
            sb.AppendLine($"            catch (Exception ex) when (Gen.Catchable(ex)) {{ E = Gen.Handler(m, ex, P, out s0);{(v4 ? "" : " S.Clear();")} }}");
            sb.AppendLine("        }");
        }
        sb.AppendLine("    }");

        var head = new StringBuilder();
        head.AppendLine($"    public static object {NamePlaceholder}(AbcMethod m, object self, object[] args)");
        head.AppendLine("    {");
        if (!v4) head.AppendLine("        var rt = m.Ctx.Rt; var abc = m.Ctx.Abc; var K = m.K; var L = Gen.Locals(m, self, args); var S = new List<(object Obj, bool With)>();");
        else
        {
            head.Append("        var rt = m.Ctx.Rt; var abc = m.Ctx.Abc; var K = m.K;");
            head.AppendLine();
            // Only the registers the code uses, each filled at run time from the method's signature (Gen.Reg), and the
            // parameters past them coerced for their side effects (Gen.Params): the text does not depend on the signature,
            // so bodies with the same code share one method, as in version 3.
            head.Append("        object r0 = self");
            for (int i = 1; i < used; i++) head.Append($", r{i} = Gen.Reg(m, args, {i})");
            head.AppendLine(";");
            head.AppendLine($"        Gen.Params(m, args, {used});");
            if (sdepth.Max() > 0) head.AppendLine("        Gen.ScopeStack c = default;");
        }
        head.Append("        object ");
        for (int i = 0; i < maxDepth; i++) head.Append(i == 0 ? "" : ", ").Append($"s{i} = null");
        head.AppendLine(";");
        return head.Append(sb).ToString();
    }

    /// <summary>The name expression of a multiname whose runtime parts sit on the stack from slot <paramref name="top"/> down.</summary>
    (string Name, string Key, string Has) NameAt(int mn, string k, int top)
    {
        switch (abc.Multinames[mn].Kind)
        {
            case 0x1B: case 0x1C: return ($"Gen.LName(Gen.N(m, {k}), s{top})", $"Gen.LKey(s{top})", $"Gen.LHas(s{top})");
            case 0x0F: case 0x10: return ($"Gen.NsName(Gen.N(m, {k}), s{top})", "null", "false");
            case 0x11: case 0x12: return ($"Gen.NsLName(s{top}, s{top - 1})", "null", "false");
        }
        return ($"Gen.N(m, {k})", "null", "false");
    }

    static string Args(int from, int count)
    {
        if (count == 0) return "Array.Empty<object>()";
        return "new object[] { " + string.Join(", ", Enumerable.Range(from, count).Select(k => "s" + k)) + " }";
    }

    string Insn(Code c, int i, int d)
    {
        var x = c.Ins[i];
        int a = x.A, b = x.B;
        string T(int k) => "s" + (d - 1 - k);        // k-th from the top before the instruction
        string ka = c.KAt[i] >= 0 ? $"K[{c.KAt[i]}]" : null;   // the pool operand, read through K
        string Push(string v) => $"s{d} = {v};";
        string Go(int t) => $"goto L{t};";
        string Cmp(string cond) => $"if ({cond}) {Go(c.Target[i])}";
        switch (x.Op)
        {
            case 0x01: case 0x02: case 0x09: case 0xEF: case 0xF0: case 0xF1: case 0xF2: case 0xF3: case 0x78: case 0x82: case 0x06: return ";";
            case 0x07: return ";";
            case 0x03: return $"throw new AsThrow({T(0)});";
            case 0x04: { int r = RCount(a); var nm = NameAt(a, ka, d - 1); return $"{T(r)} = Gen.GetSuper(m, {T(r)}, {nm.Name});"; }
            case 0x05: { int r = RCount(a); var nm = NameAt(a, ka, d - 2); return $"Gen.SetSuper(m, {T(r + 1)}, {nm.Name}, {T(0)});"; }
            case 0x08: return $"{Reg(a)} = Rt.Undef;";
            case 0x0C: return Cmp($"Rt.LessThan({T(1)}, {T(0)}) != true");
            case 0x0D: return Cmp($"Rt.LessThan({T(0)}, {T(1)}) != false");
            case 0x0E: return Cmp($"Rt.LessThan({T(0)}, {T(1)}) != true");
            case 0x0F: return Cmp($"Rt.LessThan({T(1)}, {T(0)}) != false");
            case 0x10: return Go(c.Target[i]);
            case 0x11: return Cmp($"Rt.ToBoolean({T(0)})");
            case 0x12: return Cmp($"!Rt.ToBoolean({T(0)})");
            case 0x13: return Cmp($"Rt.LooseEquals({T(1)}, {T(0)})");
            case 0x14: return Cmp($"!Rt.LooseEquals({T(1)}, {T(0)})");
            case 0x15: return Cmp($"Rt.LessThan({T(1)}, {T(0)}) == true");
            case 0x16: return Cmp($"Rt.LessThan({T(0)}, {T(1)}) == false");
            case 0x17: return Cmp($"Rt.LessThan({T(0)}, {T(1)}) == true");
            case 0x18: return Cmp($"Rt.LessThan({T(1)}, {T(0)}) == false");
            case 0x19: return Cmp($"Rt.StrictEquals({T(1)}, {T(0)})");
            case 0x1A: return Cmp($"!Rt.StrictEquals({T(1)}, {T(0)})");
            case 0x1B:
            {
                var cs = c.Cases[i];
                var sw = new StringBuilder($"switch (Gen.Switch({T(0)}, {cs.Length - 1})) {{ ");
                for (int k = 1; k < cs.Length; k++) sw.Append($"case {k - 1}: {Go(cs[k])} ");
                sw.Append($"default: {Go(cs[0])} }}");
                return sw.ToString();
            }
            case 0x1C: return $"S.Add(({T(0)}, true));";
            case 0x1D: return v4 ? $"Gen.At(ref c, {sdepth[i] - 1}) = null;" : "S.RemoveAt(S.Count - 1);";
            case 0x1E: return $"{T(1)} = Gen.NextName(rt, {T(1)}, {T(0)});";
            case 0x23: return $"{T(1)} = Gen.NextValue(rt, {T(1)}, {T(0)});";
            case 0x1F: return $"{T(1)} = Gen.HasNext({T(1)}, {T(0)});";
            case 0x32: return Push($"Gen.HasNext2(ref {Reg(a)}, ref {Reg(b)})");
            case 0x20: return Push("null");
            case 0x21: return Push("Rt.Undef");
            case 0x24: return Push($"Rt.Box({a})");
            case 0x25: return Push($"Rt.Box({(short)a})");
            case 0x26: return Push("Rt.True");
            case 0x27: return Push("Rt.False");
            case 0x28: return Push("double.NaN");
            case 0x29: return ";";
            case 0x2A: return Push(T(0));
            case 0x2B: return $"{{ var t = {T(0)}; {T(0)} = {T(1)}; {T(1)} = t; }}";
            case 0x2C: return Push($"abc.Strings[{ka}]");
            case 0x2D: return Push($"Rt.Box(abc.Ints[{ka}])");
            case 0x2E: return Push($"Rt.Uint(abc.Uints[{ka}])");
            case 0x2F: return Push($"Rt.Num(abc.Doubles[{ka}])");
            case 0x30: return v4 ? $"Gen.At(ref c, {sdepth[i]}) = {T(0)};" : $"S.Add(({T(0)}, false));";
            case 0x31: return Push($"new AsNamespace(rt, m.Ctx.NsAt({ka}))");
            case 0x40: return Push(v4 ? $"Gen.NewFunctionV(m, {Scopes(i)}, {ka})" : $"Gen.NewFunction(m, S, {ka})");
            case 0x41: { int f = d - 2 - a; return $"s{f} = rt.Invoke(s{f}, s{f + 1}, {Args(f + 2, a)}, \"value\");"; }
            case 0x42: { int f = d - 1 - a; return $"s{f} = rt.Construct(s{f}, {Args(f + 1, a)}, \"value\");"; }
            case 0x43: case 0x44: return "throw rt.MakeError(\"VerifyError\", \"callmethod/callstatic not supported\");";
            case 0x45: case 0x4E:
            {
                int r = RCount(a); int o = d - 1 - b - r; var nm = NameAt(a, ka, o + r);
                string call = $"Gen.CallSuper(m, s{o}, {nm.Name}, {Args(o + 1 + r, b)})";
                return x.Op == 0x45 ? $"s{o} = {call};" : call + ";";
            }
            case 0x46: case 0x4C: case 0x4F:
            {
                int r = RCount(a); int o = d - 1 - b - r; var nm = NameAt(a, ka, o + r);
                string call = $"rt.CallProperty(s{o}, {nm.Name}, {Args(o + 1 + r, b)}, {nm.Key}, {nm.Has})";
                return x.Op != 0x4F ? $"s{o} = {call};" : call + ";";
            }
            case 0x47: return "return Rt.Undef;";
            case 0x48: return $"return {T(0)};";
            case 0x49: { int o = d - 1 - a; return $"Gen.ConstructSuper(m, s{o}, {Args(o + 1, a)});"; }
            case 0x4A:
            {
                int r = RCount(a); int o = d - 1 - b - r; var nm = NameAt(a, ka, o + r);
                return $"s{o} = Gen.ConstructProp(rt, s{o}, {nm.Name}, {nm.Key}, {nm.Has}, {Args(o + 1 + r, b)});";
            }
            case 0x53: { int f = d - 1 - a; return $"s{f} = rt.ApplyType(s{f}, {Args(f + 1, a)});"; }
            case 0x55: { int f = d - 2 * a; return $"s{f} = Gen.NewObject(rt, {Args(f, 2 * a)});"; }
            case 0x56: { int f = d - a; return $"s{f} = new AsArray(rt, {Args(f, a)});"; }
            case 0x57: return Push("Gen.NewActivation(m)");
            case 0x58: return v4 ? $"{T(0)} = Gen.NewClassV(m, {Scopes(i)}, {ka}, {T(0)});" : $"{T(0)} = Gen.NewClass(m, S, {ka}, {T(0)});";
            case 0x59: return "throw rt.MakeError(\"Error\", \"E4X descendants are not supported\");";
            case 0x5A: return Push($"Gen.NewCatch(m, {a})");
            case 0x5D: case 0x5E:
            {
                int r = RCount(a); int o = d - r; var nm = NameAt(a, ka, d - 1);
                string strict = x.Op == 0x5D ? "true" : "false";
                if (v4) return $"s{o} = Gen.FindPropS(m, {Scopes(i)}, self, {nm.Name}, {strict});";
                return $"s{o} = Gen.FindProp(m, S, self, {nm.Name}, {strict});";
            }
            case 0x5F: return Push(v4 ? $"Gen.FindDefV(m, {Scopes(i)}, self, {ka})" : $"Gen.FindDef(m, S, self, {ka})");
            case 0x60: return Push(v4 ? $"Gen.GetLexV(m, {Scopes(i)}, self, {ka})" : $"Gen.GetLex(m, S, self, {ka})");
            case 0x61: case 0x68:
            {
                int r = RCount(a); int o = d - 2 - r; var nm = NameAt(a, ka, d - 2);
                return $"rt.SetProperty(s{o}, {nm.Name}, {T(0)}, {nm.Key}, {nm.Has}, {(x.Op == 0x68 ? "true" : "false")});";
            }
            case 0x62: return Push(Reg(a));
            case 0x63: return $"{Reg(a)} = {T(0)};";
            case 0xD0: case 0xD1: case 0xD2: case 0xD3: return Push(Reg(x.Op - 0xD0));
            case 0xD4: case 0xD5: case 0xD6: case 0xD7: return $"{Reg(x.Op - 0xD4)} = {T(0)};";
            case 0x64: return Push(v4 ? $"Gen.GlobalScopeV(m, {Scopes(i)})" : "Gen.GlobalScope(m, S)");
            case 0x65: return Push(v4 ? $"Gen.At(ref c, {a})" : $"S[{a}].Obj");
            case 0x66:
            {
                int r = RCount(a); int o = d - 1 - r; var nm = NameAt(a, ka, d - 1);
                return $"s{o} = rt.GetProperty(s{o}, {nm.Name}, {nm.Key}, {nm.Has});";
            }
            case 0x6A:
            {
                int r = RCount(a); int o = d - 1 - r; var nm = NameAt(a, ka, d - 1);
                return $"s{o} = Rt.Box(rt.DeleteProperty(s{o}, {nm.Name}, {nm.Key}, {nm.Has}));";
            }
            case 0x6C: return $"{T(0)} = Gen.GetSlot({T(0)}, {a});";
            case 0x6D: return $"Gen.SetSlot(rt, {T(1)}, {a}, {T(0)});";
            case 0x6E: return Push(v4 ? $"Gen.GetGlobalSlotV(m, {Scopes(i)}, {a})" : $"Gen.GetGlobalSlot(m, S, {a})");
            case 0x6F: return v4 ? $"Gen.SetGlobalSlotV(m, {Scopes(i)}, {a}, {T(0)});" : $"Gen.SetGlobalSlot(m, S, {a}, {T(0)});";
            case 0x70: case 0x71: case 0x72: return $"{T(0)} = Rt.ToString({T(0)});";
            case 0x73: return $"{T(0)} = Rt.Box(Rt.ToInt32({T(0)}));";
            case 0x74: return $"{T(0)} = Rt.Uint(Rt.ToUint32({T(0)}));";
            case 0x75: return $"{T(0)} = Rt.Num(Rt.ToNumber({T(0)}));";
            case 0x76: return $"{T(0)} = Rt.Box(Rt.ToBoolean({T(0)}));";
            case 0x77: return $"Gen.CheckNull(rt, {T(0)});";
            case 0x80: return $"{T(0)} = Gen.Coerce(m, {ka}, {T(0)});";
            case 0x85: return $"{T(0)} = Rt.CoerceString({T(0)});";
            case 0x86: return $"{T(0)} = Gen.AsType(m, {ka}, {T(0)});";
            case 0x87: return $"{T(1)} = Gen.AsTypeLate(rt, {T(1)}, {T(0)});";
            case 0x90: return $"{T(0)} = Rt.Num(-Rt.ToNumber({T(0)}));";
            case 0x91: return $"{T(0)} = Gen.Inc({T(0)});";
            case 0x93: return $"{T(0)} = Gen.Dec({T(0)});";
            case 0x92: return $"{Reg(a)} = Gen.Inc({Reg(a)});";
            case 0x94: return $"{Reg(a)} = Gen.Dec({Reg(a)});";
            case 0x95: return $"{T(0)} = Rt.TypeOf({T(0)});";
            case 0x96: return $"{T(0)} = Rt.Box(!Rt.ToBoolean({T(0)}));";
            case 0x97: return $"{T(0)} = Rt.Box(~Rt.ToInt32({T(0)}));";
            case 0xA0: return $"{T(1)} = Rt.Add({T(1)}, {T(0)});";
            case 0xA1: return $"{T(1)} = Gen.Sub({T(1)}, {T(0)});";
            case 0xA2: return $"{T(1)} = Rt.Num(Rt.ToNumber({T(1)}) * Rt.ToNumber({T(0)}));";
            case 0xA3: return $"{T(1)} = Rt.Num(Rt.ToNumber({T(1)}) / Rt.ToNumber({T(0)}));";
            case 0xA4: return $"{T(1)} = Rt.Num(Rt.ToNumber({T(1)}) % Rt.ToNumber({T(0)}));";
            case 0xA5: return $"{T(1)} = Rt.Box(Rt.ToInt32({T(1)}) << (Rt.ToInt32({T(0)}) & 31));";
            case 0xA6: return $"{T(1)} = Rt.Box(Rt.ToInt32({T(1)}) >> (Rt.ToInt32({T(0)}) & 31));";
            case 0xA7: return $"{T(1)} = Rt.Uint(Rt.ToUint32({T(1)}) >> (Rt.ToInt32({T(0)}) & 31));";
            case 0xA8: return $"{T(1)} = Rt.Box(Rt.ToInt32({T(1)}) & Rt.ToInt32({T(0)}));";
            case 0xA9: return $"{T(1)} = Rt.Box(Rt.ToInt32({T(1)}) | Rt.ToInt32({T(0)}));";
            case 0xAA: return $"{T(1)} = Rt.Box(Rt.ToInt32({T(1)}) ^ Rt.ToInt32({T(0)}));";
            case 0xAB: return $"{T(1)} = Rt.Box(Rt.LooseEquals({T(1)}, {T(0)}));";
            case 0xAC: return $"{T(1)} = Rt.Box(Rt.StrictEquals({T(1)}, {T(0)}));";
            case 0xAD: return $"{T(1)} = Rt.Box(Rt.LessThan({T(1)}, {T(0)}) == true);";
            case 0xAE: return $"{T(1)} = Rt.Box(Rt.LessThan({T(0)}, {T(1)}) == false);";
            case 0xAF: return $"{T(1)} = Rt.Box(Rt.LessThan({T(0)}, {T(1)}) == true);";
            case 0xB0: return $"{T(1)} = Rt.Box(Rt.LessThan({T(1)}, {T(0)}) == false);";
            case 0xB1: return $"{T(1)} = Rt.Box(Gen.InstanceOf(rt, {T(1)}, {T(0)}));";
            case 0xB2: return $"{T(0)} = Rt.Box(Gen.IsType(m, {ka}, {T(0)}));";
            case 0xB3: return $"{T(1)} = Rt.Box(Gen.IsTypeLate(rt, {T(1)}, {T(0)}));";
            case 0xB4: return $"{T(1)} = Rt.Box(Gen.In(rt, {T(1)}, {T(0)}));";
            case 0xC0: return $"{T(0)} = Rt.Box(Rt.ToInt32({T(0)}) + 1);";
            case 0xC1: return $"{T(0)} = Rt.Box(Rt.ToInt32({T(0)}) - 1);";
            case 0xC2: return $"{Reg(a)} = Rt.Box(Rt.ToInt32({Reg(a)}) + 1);";
            case 0xC3: return $"{Reg(a)} = Rt.Box(Rt.ToInt32({Reg(a)}) - 1);";
            case 0xC4: return $"{T(0)} = Rt.Box(-Rt.ToInt32({T(0)}));";
            case 0xC5: return $"{T(1)} = Rt.Box(Rt.ToInt32({T(1)}) + Rt.ToInt32({T(0)}));";
            case 0xC6: return $"{T(1)} = Rt.Box(Rt.ToInt32({T(1)}) - Rt.ToInt32({T(0)}));";
            case 0xC7: return $"{T(1)} = Rt.Box(Rt.ToInt32({T(1)}) * Rt.ToInt32({T(0)}));";
        }
        Unsupported.Add(x.Name ?? ("0x" + x.Op.ToString("x2")));
        return $"throw rt.MakeError(\"VerifyError\", \"unsupported opcode 0x{x.Op:x2}\");";
    }
}
