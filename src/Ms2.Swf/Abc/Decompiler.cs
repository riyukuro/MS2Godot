using System.Text;

namespace Ms2.Swf.Abc;

/// <summary>
/// A rough AVM2 to C# decompiler: symbolic stack execution per basic block, simple structuring of if/else and while
/// loops, goto for the rest. Output is pseudo C# over a stand-in runtime where every ActionScript value is `dynamic`.
/// </summary>
public sealed class Decompiler
{
    readonly AbcFile abc;
    public int Unstructured, Unknown, TryBlocks, Switches, StackMerges, Methods;
    public HashSet<string> UnknownOps = new();

    /// <summary>When set, every declared type except the primitives is `dynamic` (compiles against stand-in classes).</summary>
    public bool AllDynamic;
    HashSet<string> typeNames;
    static readonly HashSet<string> Builtins = new("Math String Number int uint Boolean Array Object Date RegExp XML XMLList Vector Error TypeError RangeError ArgumentError isNaN isFinite parseInt parseFloat trace getTimer setTimeout clearTimeout setInterval clearInterval undefined NaN Infinity getDefinitionByName getQualifiedClassName getQualifiedSuperclassName escape unescape encodeURIComponent decodeURIComponent Function Class Namespace QName JSON".Split(' '));

    public Decompiler(AbcFile a)
    {
        abc = a;
        typeNames = new HashSet<string>(a.Instances.Select(i => a.Local(i.Name)));
        foreach (var i in a.Instances) { if (i.Super != 0) typeNames.Add(a.Local(i.Super)); foreach (var x in i.Interfaces) typeNames.Add(a.Local(x)); }
    }

    /// <summary>Unqualified member access in a method: plain if it is a class, builtin or declared member, else through dynamic this.</summary>
    string ScopeName(string n)
    {
        if (Builtins.Contains(n) || typeNames.Contains(n) || (n.Length > 0 && char.IsUpper(n[0])) || curMembers.Contains(n)) return n;
        return "((dynamic)this)." + n;
    }
    HashSet<string> curMembers = new(), curMethods = new();
    Dictionary<string, HashSet<string>> classMethods;
    HashSet<string> MethodsOf(string cls)
    {
        classMethods ??= abc.Instances.Select((i, k) => (Id(abc.Local(i.Name)), k)).GroupBy(x => x.Item1).ToDictionary(g => g.Key, g => g.SelectMany(x => abc.Instances[x.k].Traits.Concat(abc.Classes[x.k].Traits)).Where(t => t.Kind == TraitKind.Method).Select(t => Id(abc.Local(t.Name))).ToHashSet());
        return classMethods.TryGetValue(cls, out var h) ? h : null;
    }

    static readonly HashSet<string> Keywords = new("abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while".Split(' '));

    public static string Id(string s)
    {
        if (string.IsNullOrEmpty(s)) return "_";
        var sb = new StringBuilder();
        foreach (var ch in s) sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        var r = sb.ToString();
        if (char.IsDigit(r[0])) r = "_" + r;
        return Keywords.Contains(r) ? "@" + r : r;
    }

    public string TypeName(int mn)
    {
        if (mn == 0) return "dynamic";
        var n = abc.Local(mn);
        if (AllDynamic && n is not ("int" or "uint" or "Number" or "String" or "Boolean" or "void")) return "dynamic";
        return n switch
        {
            "int" => "int", "uint" => "uint", "Number" => "double", "String" => "string", "Boolean" => "bool", "void" => "void",
            "Object" or "*" or "Function" or "Class" or "XML" or "XMLList" or "RegExp" => "dynamic",
            "Array" => "AsArray", "Vector" => "dynamic",
            _ => Id(n),
        };
    }

    string Const(int kind, int idx) => kind switch
    {
        0x03 => abc.Ints[idx].ToString(),
        0x04 => abc.Uints[idx].ToString() + "u",
        0x06 => abc.Doubles[idx].ToString("R", System.Globalization.CultureInfo.InvariantCulture) + (double.IsFinite(abc.Doubles[idx]) ? "" : ""),
        0x01 => Quote(abc.Strings[idx]),
        0x0B => "true", 0x0A => "false", 0x0C => "null", 0x00 => "null",
        _ => "null",
    };

    static string Quote(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s)
            switch (c)
            {
                case '"': sb.Append("\\\""); break; case '\\': sb.Append("\\\\"); break; case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break; case '\t': sb.Append("\\t"); break;
                default: if (c < 32) sb.Append("\\u" + ((int)c).ToString("x4")); else sb.Append(c); break;
            }
        return sb.Append('"').ToString();
    }

    // ---- classes -----------------------------------------------------------------------------------------

    public string Class(int ci)
    {
        var inst = abc.Instances[ci]; var cls = abc.Classes[ci];
        var sb = new StringBuilder();
        string name = Id(abc.Local(inst.Name));
        string pkg = abc.Name(inst.Name); int dot = pkg.LastIndexOf('.');
        sb.Append("// ").Append(abc.Name(inst.Name)).AppendLine();
        var bases = new List<string>();
        if (!inst.IsInterface && inst.Super != 0 && abc.Local(inst.Super) != "Object") bases.Add(Id(abc.Local(inst.Super)));
        else if (!inst.IsInterface) bases.Add("AsObject");
        if (!AllDynamic) foreach (var i in inst.Interfaces) bases.Add(Id(abc.Local(i)));   // (dynamic mode: interfaces are not enforced)
        sb.Append("public ").Append(inst.IsInterface ? "interface " : "class ").Append(name).Append(bases.Count > 0 ? " : " + string.Join(", ", bases) : "").AppendLine(" {");
        var declared = new HashSet<string>();
        curMembers = new HashSet<string>(); curMethods = new HashSet<string>();
        for (int k = ci; k >= 0;)
        {
            foreach (var t in abc.Instances[k].Traits.Concat(abc.Classes[k].Traits)) { curMembers.Add(Id(abc.Local(t.Name))); if (t.Kind == TraitKind.Method) curMethods.Add(Id(abc.Local(t.Name))); }
            var sup = abc.Instances[k].Super; int nk = -1;
            if (sup != 0) for (int q = 0; q < abc.Instances.Length; q++) if (abc.Instances[q].Name == sup || abc.Name(abc.Instances[q].Name) == abc.Name(sup)) { nk = q; break; }
            k = nk == k ? -1 : nk;
        }
        EmitTraits(sb, cls.Traits, true, name, declared);
        EmitTraits(sb, inst.Traits, false, name, declared);
        if (!inst.IsInterface)
        {
            var init = abc.Methods[inst.Init];
            sb.Append("  public ").Append(name).Append('(').Append(Params(init, out var rest)).AppendLine(") {");
            sb.Append(Body(init, name, inst.Init, ctor: true, staticCtx: false)).AppendLine("  }");
            var cinit = abc.Methods[cls.Init];
            if (cinit.Body != null && cinit.Body.Code.Length > 4)
            {
                sb.Append("  static ").Append(name).AppendLine("() {");
                sb.Append(Body(cinit, name, cls.Init, true, true)).AppendLine("  }");
            }
        }
        // anonymous functions created in this class are emitted as methods
        foreach (var fn in pendingFns.ToList())
        {
            pendingFns.Remove(fn);
            var m = abc.Methods[fn];
            sb.Append("  public dynamic fn_").Append(fn).Append('(').Append(Params(m, out _)).AppendLine(") {");
            sb.Append(Body(m, name, fn, false, false)).AppendLine("  }");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    readonly List<int> pendingFns = new();

    string Params(MethodInfo m, out bool rest)
    {
        rest = m.NeedRest;
        var l = new List<string>();
        int firstOpt = m.ParamCount - (m.Options?.Count ?? 0);
        for (int i = 0; i < m.ParamCount; i++)
        {
            string t = TypeName(m.ParamTypes[i]);
            string n = m.ParamNames != null && m.ParamNames[i].Length > 0 ? Id(m.ParamNames[i]) : "a" + (i + 1);
            string def = "";
            if (m.Options != null && i >= firstOpt)
            {
                var (k, v) = m.Options[i - firstOpt];
                string c = Const(k, v);
                def = " = " + (c == "null" && t is "int" or "uint" or "double" or "bool" ? "default" : c);
            }
            l.Add(t + " " + n + def);
        }
        if (rest) l.Add("params dynamic[] rest");
        return string.Join(", ", l);
    }

    void EmitTraits(StringBuilder sb, List<Trait> traits, bool isStatic, string cls, HashSet<string> declared)
    {
        string st = isStatic ? "static " : "";
        foreach (var t in traits)
        {
            string n = Id(abc.Local(t.Name));
            switch (t.Kind)
            {
                case TraitKind.Slot:
                    sb.Append("  public ").Append(st).Append(TypeName(t.TypeName)).Append(' ').Append(n).AppendLine(";"); declared.Add(n); break;
                case TraitKind.Const:
                    sb.Append("  public ").Append(st).Append(TypeName(t.TypeName)).Append(' ').Append(n);
                    if (t.ValueIndex != 0) sb.Append(" = ").Append(Const(t.ValueKind, t.ValueIndex));
                    sb.AppendLine(";"); declared.Add(n); break;
                case TraitKind.Method:
                {
                    var m = abc.Methods[t.MethodIndex];
                    string ret = TypeName(m.ReturnType);
                    sb.Append("  public ").Append(st).Append((t.Attrs & 2) != 0 ? "new " : "").Append(ret).Append(' ').Append(n).Append('(').Append(Params(m, out _)).Append(')');
                    if (m.Body == null) { sb.AppendLine(";"); if (!abc.Instances.Any(i => i.IsInterface && false)) { } break; }
                    sb.AppendLine(" {").Append(Body(m, cls, t.MethodIndex, false, isStatic)).AppendLine("  }");
                    declared.Add(n); break;
                }
                case TraitKind.Getter: case TraitKind.Setter:
                {
                    var m = abc.Methods[t.MethodIndex];
                    bool g = t.Kind == TraitKind.Getter;
                    string ty = g ? TypeName(m.ReturnType) : "void";
                    sb.Append("  // ").Append(g ? "get " : "set ").AppendLine(n);
                    sb.Append("  public ").Append(st).Append(ty).Append(' ').Append(g ? "get_" : "set_").Append(n).Append('(').Append(Params(m, out _)).Append(')');
                    if (m.Body == null) { sb.AppendLine(";"); break; }
                    sb.AppendLine(" {").Append(Body(m, cls, t.MethodIndex, false, isStatic)).AppendLine("  }");
                    break;
                }
                case TraitKind.Class: case TraitKind.Function: break;
            }
        }
    }

    // ---- method bodies -----------------------------------------------------------------------------------

    sealed class Block
    {
        public int Start, End, Index;
        public List<string> Stmts = new();
        // terminator
        public string Cond;          // conditional jump: condition text meaning "jump taken"
        public int TargetBlock = -1; // jump target block index
        public bool Uncond, Return, Throw;
        public Insn SwitchInsn; public string SwitchExpr;
        public List<string> StackOut = new();
        public List<string> StackIn; public bool Seen;
        public string JoinCond, FirstCond; public bool TryDone;
    }

    bool curVoid;
    string Body(MethodInfo m, string cls, int mi, bool ctor, bool staticCtx)
    {
        curVoid = ctor || staticCtx && abc.Name(m.ReturnType) == "void" || abc.Name(m.ReturnType) == "void";
        Methods++;
        var bd = m.Body;
        if (bd == null) return "";
        var sb = new StringBuilder();
        try { return DecompileBody(m, cls, ctor, staticCtx); }
        catch (Exception e)
        {
            Unknown++;
            return "    // decompile failed: " + e.GetType().Name + ": " + e.Message + "\n    throw new NotImplementedException();\n";
        }
    }

    string DecompileBody(MethodInfo m, string cls, bool ctor, bool staticCtx)
    {
        var bd = m.Body;
        var code = Disasm.Decode(bd.Code);
        var offToIdx = new Dictionary<int, int>();
        for (int i = 0; i < code.Count; i++) offToIdx[code[i].Off] = i;
        // leaders
        var leaders = new SortedSet<int> { 0 };
        for (int i = 0; i < code.Count; i++)
        {
            var c = code[i];
            if (c.IsJump) { leaders.Add(offToIdx[c.Target]); if (i + 1 < code.Count) leaders.Add(i + 1); }
            else if (c.Cases != null) { foreach (var t in c.Cases) leaders.Add(offToIdx[t]); if (i + 1 < code.Count) leaders.Add(i + 1); }
            else if (c.Op is 0x47 or 0x48 or 0x03) { if (i + 1 < code.Count) leaders.Add(i + 1); }
        }
        foreach (var ex in bd.Exceptions) { leaders.Add(offToIdx[ex.Target]); leaders.Add(offToIdx[ex.From]); if (offToIdx.TryGetValue(ex.To, out var ti)) leaders.Add(ti); }
        var starts = leaders.ToList();
        var blocks = new List<Block>();
        var blockOfInsn = new Dictionary<int, int>();
        for (int b = 0; b < starts.Count; b++)
        {
            int s = starts[b], e = b + 1 < starts.Count ? starts[b + 1] : code.Count;
            blocks.Add(new Block { Start = s, End = e, Index = b });
            blockOfInsn[s] = b;
        }
        if (bd.Exceptions.Count > 0) TryBlocks++;
        var names = new LocalNames(m, ctor);
        var catchBlocks = new HashSet<int>(bd.Exceptions.Select(x => blockOfInsn[offToIdx[x.Target]]));

        foreach (var b in blocks)
        {
            var st = b.StackIn != null ? new List<string>(b.StackIn) : new List<string>();
            if (catchBlocks.Contains(b.Index) && st.Count == 0) st.Add("exception");
            Simulate(b, code, st, offToIdx, blockOfInsn, names, cls, staticCtx);
            b.StackOut = st;
            // propagate stack
            var succ = new List<int>();
            var last = code[b.End - 1];
            if (last.IsJump) succ.Add(blockOfInsn[offToIdx[last.Target]]);
            if (!(last.Op is 0x10 or 0x47 or 0x48 or 0x03) && last.Cases == null && b.End < code.Count) succ.Add(b.Index + 1);
            foreach (var sIdx in succ)
            {
                var s = blocks[sIdx];
                bool viaJump = last.IsJump && sIdx == blockOfInsn[offToIdx[last.Target]] && !(sIdx == b.Index + 1 && !last.IsJump);
                var outStack = st;
                bool condJump = last.IsCondJump && sIdx == blockOfInsn[offToIdx[last.Target]];
                if (condJump && s.JoinCond == null && sIdx != b.Index + 1) s.JoinCond = b.Cond;
                if (s.StackIn == null) { s.StackIn = new List<string>(outStack); s.FirstCond = condJump ? b.Cond : null; }
                else if (!s.StackIn.SequenceEqual(outStack) && s.StackIn.Count == outStack.Count)
                {
                    StackMerges++;
                    var merged = new List<string>();
                    for (int k = 0; k < outStack.Count; k++)
                    {
                        string a1 = s.StackIn[k], c2 = outStack[k];
                        if (a1 == c2) merged.Add(a1);
                        else if (s.FirstCond != null) merged.Add($"({s.FirstCond} ? {a1} : {c2})");
                        else if (b.JoinCond != null) merged.Add($"({b.JoinCond} ? {c2} : {a1})");
                        else merged.Add($"AsRt.Merge({a1}, {c2})");
                    }
                    s.StackIn = merged;
                }
            }
        }
        // structured emission
        var sw = new StringBuilder();
        var usedLabels = new HashSet<int>();
        var labelNeeded = new HashSet<int>();
        curOffBlock = new Dictionary<int, int>();
        foreach (var kv in offToIdx) if (blockOfInsn.TryGetValue(kv.Value, out var bi)) curOffBlock[kv.Key] = bi;
        curEx = bd.Exceptions.Select(x => (blockOfInsn[offToIdx[x.From]], offToIdx.TryGetValue(x.To, out var ti) && blockOfInsn.TryGetValue(ti, out var tb) ? tb : -1, blockOfInsn[offToIdx[x.Target]], false)).ToList();
        int u0 = Unstructured, s0 = Switches;
        Emit(new StringBuilder(), blocks, 0, blocks.Count, 2, labelNeeded, new HashSet<int>());   // pass 1 only collects goto targets
        Unstructured = u0;
        for (int q = 0; q < curEx.Count; q++) curEx[q] = (curEx[q].fromBlock, curEx[q].toBlock, curEx[q].target, false);
        Emit(sw, blocks, 0, blocks.Count, 2, labelNeeded, new HashSet<int>());
        // locals
        var decl = new StringBuilder();
        foreach (var l in names.Declared.OrderBy(x => x)) decl.Append("    dynamic ").Append(names.Local(l)).AppendLine(";");
        if (false) decl.AppendLine("    // exceptions: " + string.Join(", ", bd.Exceptions.Select(x => $"[{x.From},{x.To})->{x.Target}")));
        return decl.ToString() + sw;
    }

    sealed class LocalNames
    {
        readonly MethodInfo m; readonly bool ctor;
        public SortedSet<int> Declared = new();
        public LocalNames(MethodInfo m, bool ctor) { this.m = m; this.ctor = ctor; }
        public string Local(int i)
        {
            if (i == 0) return "this";
            if (i <= m.ParamCount) return m.ParamNames != null && m.ParamNames[i - 1].Length > 0 ? Id(m.ParamNames[i - 1]) : "a" + i;
            if (m.NeedRest && i == m.ParamCount + 1) return "rest";
            return "l" + i;
        }
        public string Use(int i) { if (i > m.ParamCount + (m.NeedRest ? 1 : 0)) Declared.Add(i); return Local(i); }
    }

    static readonly string[] BoolStarts = { "AsRt.Eq(", "AsRt.StrictEq(", "AsRt.InstanceOf(", "AsRt.In(", "AsRt.IsTypeLate(", "AsRt.ToBool(", "true", "false" };
    readonly HashSet<string> boolExprs = new();

    string Mark(string e) { boolExprs.Add(e); return e; }

    bool IsBool(string e) => boolExprs.Contains(e) || e.StartsWith("!") && e.Length > 1 && (e[1] == '(' || char.IsLetter(e[1])) && IsAtom(e.Substring(1)) || BoolStarts.Any(x => e.StartsWith(x)) && IsAtom(e) || e.StartsWith("(") && e.EndsWith(" is dynamic)");

    static bool IsAtom(string e)
    {
        // a single call or parenthesised group: parentheses balance and never close before the end
        int d = 0;
        for (int i = 0; i < e.Length; i++)
        {
            if (e[i] == '(') d++;
            else if (e[i] == ')') { d--; if (d == 0 && i < e.Length - 1) return false; }
            else if (d == 0 && e[i] == ' ') return false;
        }
        return d == 0;
    }

    string Bool(string e) => IsBool(e) ? e : Mark($"AsRt.ToBool({e})");

    string Not(string e)
    {
        if (e.StartsWith("!") && IsBool(e) && IsAtom(e.Substring(1))) return e.Substring(1);
        if (IsBool(e) && IsAtom(e)) return Mark("!" + e);
        return Mark($"!AsRt.ToBool({e})");
    }

    string Neg(string c)
    {
        if (c.StartsWith("!") && IsAtom(c.Substring(1))) return c.Substring(1);
        if (IsAtom(c)) return "!" + c;
        return "!(" + c + ")";
    }

    static string NegOld(string c)
    {
        if (c.StartsWith("!(") && c.EndsWith(")") && Balanced(c.Substring(2, c.Length - 3))) return c.Substring(2, c.Length - 3);
        return "!(" + c + ")";
    }
    static bool Balanced(string s) { int d = 0; foreach (var ch in s) { if (ch == '(') d++; else if (ch == ')') { d--; if (d < 0) return false; } } return d == 0; }

    string Mn(int mn, List<string> st, out string recvExprName, out bool computed)
    {
        var m = abc.Multinames[mn];
        recvExprName = null; computed = false;
        string nsx = null;
        if (m.IsRuntimeNs && m.Kind != 0x11 && m.Kind != 0x12) { /* RTQName: ns on stack */ }
        if (m.Kind is 0x1B or 0x1C or 0x11 or 0x12) { var n = Pop(st); computed = true; if (m.IsRuntimeNs) Pop(st); return n; }
        if (m.Kind is 0x0F or 0x10) { Pop(st); }
        return Id(abc.Local(mn));
    }

    static string Pop(List<string> st)
    {
        if (st.Count == 0) return "/*underflow*/_";
        var v = st[^1]; st.RemoveAt(st.Count - 1); return v;
    }

    static string Recv(string r) => r.StartsWith("§F:") ? null : r;

    string Member(string recv, string name, bool computed)
    {
        if (recv.StartsWith("§F:")) return computed ? $"Scope[{name}]" : ScopeName(name);
        return computed ? $"{recv}[{name}]" : $"{recv}.{name}";
    }

    void Simulate(Block b, List<Insn> code, List<string> st, Dictionary<int, int> offToIdx, Dictionary<int, int> blockOfInsn, LocalNames ln, string cls, bool staticCtx)
    {
        for (int i = b.Start; i < b.End; i++)
        {
            var c = code[i];
            switch (c.Op)
            {
                case 0x01: case 0x02: case 0x09: case 0xF0: case 0xF1: case 0xEF: case 0xF2: case 0xF3: case 0x30: case 0x1D: break;
                case 0x03: b.Stmts.Add($"throw {Pop(st)};"); b.Throw = true; break;
                case 0x08: ln.Declared.Add(c.A); break;
                case 0x1C: Pop(st); break; // pushwith
                case 0x20: st.Add("null"); break; case 0x21: st.Add("null /*undefined*/"); break;
                case 0x24: st.Add(c.A.ToString()); break; case 0x25: st.Add(((short)c.A).ToString()); break;
                case 0x26: st.Add("true"); break; case 0x27: st.Add("false"); break; case 0x28: st.Add("double.NaN"); break;
                case 0x29: { var v = Pop(st); if (!v.StartsWith("(") && !v.StartsWith("!") && v.EndsWith(")") && v.Contains('(')) b.Stmts.Add(v + ";"); else if (System.Text.RegularExpressions.Regex.IsMatch(v, @"\w\(") && v.EndsWith(")")) b.Stmts.Add("_ = " + v + ";"); break; }
                case 0x2A: { var v = Pop(st); st.Add(v); st.Add(v); break; }
                case 0x2B: { var a = Pop(st); var d = Pop(st); st.Add(a); st.Add(d); break; }
                case 0x2C: st.Add(Quote(abc.Strings[c.A])); break;
                case 0x2D: st.Add(abc.Ints[c.A].ToString()); break; case 0x2E: st.Add(abc.Uints[c.A] + "u"); break;
                case 0x2F: { double dv = abc.Doubles[c.A]; st.Add(double.IsNaN(dv) ? "double.NaN" : double.IsInfinity(dv) ? (dv > 0 ? "double.PositiveInfinity" : "double.NegativeInfinity") : dv.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + (dv == Math.Floor(dv) && Math.Abs(dv) < 1e15 ? ".0" : "")); break; }
                case 0x31: st.Add("/*ns*/null"); break;
                case 0x32: st.Add($"hasnext2({ln.Use(c.A)}, {ln.Use(c.B)})"); break;
                case 0x1E: { var idx = Pop(st); var o = Pop(st); st.Add($"nextname({o}, {idx})"); break; }
                case 0x23: { var idx = Pop(st); var o = Pop(st); st.Add($"nextvalue({o}, {idx})"); break; }
                case 0x1F: { var idx = Pop(st); var o = Pop(st); st.Add($"hasnext({o}, {idx})"); break; }
                case 0x40: { pendingFns.Add(c.A); st.Add($"new AsFunction(fn_{c.A})"); break; }
                case 0x41: { var args = PopArgs(st, c.A); var self = Pop(st); var f = Pop(st); st.Add($"{f}.call({self}{(args.Length > 0 ? ", " + args : "")})"); break; }
                case 0x42: { var args = PopArgs(st, c.A); var f = Pop(st); st.Add($"AsRt.New({f}{(args.Length > 0 ? ", " + args : "")})"); break; }
                case 0x45: case 0x4E: { var args = PopArgs(st, c.B); var n = Mn(c.A, st, out _, out var comp); var r = Pop(st); string call = $"base.{n}({args})"; if (c.Op == 0x4E) b.Stmts.Add(call + ";"); else st.Add(call); break; }
                case 0x46: case 0x4C: case 0x4F:
                {
                    var args = PopArgs(st, c.B);
                    var n = Mn(c.A, st, out _, out var comp);
                    var r = Pop(st);
                    string call;
                    if (comp) call = $"{Member(r, n, true)}({args})";
                    else if (r.StartsWith("§F:") && n is "int" or "uint" or "Number" or "String" or "Boolean" or "Object" or "Array" or "XML" or "XMLList" or "Function" or "Class" or "Vector") call = $"AsRt.Conv_{n}({args})";
                    else if (r.StartsWith("§F:")) call = $"{ScopeName(n)}({args})";
                    else call = $"{r}.{n}({args})";
                    if (c.Op == 0x4F) b.Stmts.Add(call + ";"); else st.Add(call);
                    break;
                }
                case 0x4A:
                {
                    var args = PopArgs(st, c.B);
                    var n = Mn(c.A, st, out _, out var comp);
                    var r = Pop(st);
                    st.Add(r.StartsWith("§F:") ? $"new {(n == "Object" ? "AsObject" : n == "Array" ? "AsArray" : n)}({args})" : $"AsRt.New({r}.{n}{(args.Length > 0 ? ", " + args : "")})");
                    break;
                }
                case 0x49: { var args = PopArgs(st, c.A); Pop(st); b.Stmts.Add(ctorSuperCall(args)); break; }
                case 0x47: b.Stmts.Add(curVoid ? "return;" : "return default;"); b.Return = true; break;
                case 0x48: b.Stmts.Add($"return {Pop(st)};"); b.Return = true; break;
                case 0x53: { for (int k = 0; k < c.A; k++) Pop(st); break; }
                case 0x55: { var items = new List<string>(); for (int k = 0; k < c.A; k++) { var v = Pop(st); var key = Pop(st); items.Insert(0, $"[{key}] = {v}"); } st.Add($"new AsObject {{ {string.Join(", ", items)} }}"); break; }
                case 0x56: { var items = new List<string>(); for (int k = 0; k < c.A; k++) items.Insert(0, Pop(st)); st.Add($"new AsArray({string.Join(", ", items)})"); break; }
                case 0x57: st.Add("/*activation*/new AsObject()"); break;
                case 0x58: { var bs = Pop(st); st.Add($"/*newclass*/{Id(abc.Local(abc.Instances[c.A].Name))}"); break; }
                case 0x59: { var n = Mn(c.A, st, out _, out var comp); var r = Pop(st); st.Add($"{r}.descendants({Quote(n)})"); break; }
                case 0x5A: st.Add("exception"); break;
                case 0x5D: case 0x5E: case 0x5F:
                {
                    var n = Mn(c.A, st, out _, out var comp);
                    st.Add("§F:" + (comp ? n : n));
                    break;
                }
                case 0x60: { var n = Mn(c.A, st, out _, out var comp); st.Add(comp ? n : curMethods.Contains(n) ? $"AsRt.M(this, \"{n}\")" : ScopeName(n)); break; }
                case 0x61: case 0x68:
                {
                    var v = Pop(st);
                    var n = Mn(c.A, st, out _, out var comp);
                    var r = Pop(st);
                    b.Stmts.Add($"{Member(r, n, comp)} = {v};");
                    break;
                }
                case 0x66:
                {
                    var n = Mn(c.A, st, out _, out var comp);
                    var r = Pop(st);
                    if (!comp && (r == "this" || r.StartsWith("§F:")) && curMethods.Contains(n)) st.Add($"AsRt.M(this, \"{n}\")");
                    else if (!comp && System.Text.RegularExpressions.Regex.IsMatch(r, @"^\w+$") && MethodsOf(r)?.Contains(n) == true) st.Add($"AsRt.M(null, \"{r}.{n}\")");
                    else if (!comp && r == "rest" && n == "length") st.Add("rest.Length");
                    else st.Add(Member(r, n, comp));
                    break;
                }
                case 0x6A: { var n = Mn(c.A, st, out _, out var comp); var r = Pop(st); st.Add($"delete({Member(r, n, comp)})"); break; }
                case 0x04: { var n = Mn(c.A, st, out _, out var comp); var r = Pop(st); st.Add($"base.{n}"); break; }
                case 0x05: { var v = Pop(st); var n = Mn(c.A, st, out _, out var comp); var r = Pop(st); b.Stmts.Add($"base.{n} = {v};"); break; }
                case 0x62: st.Add(c.A == 0 && staticCtx ? cls : ln.Use(c.A)); break;
                case 0xD0: case 0xD1: case 0xD2: case 0xD3: st.Add(c.Op == 0xD0 && staticCtx ? cls : ln.Use(c.Op - 0xD0)); break;
                case 0x63: case 0xD4: case 0xD5: case 0xD6: case 0xD7:
                {
                    int idx = c.Op == 0x63 ? c.A : c.Op - 0xD4;
                    var v = Pop(st);
                    if (idx == 0 || v == "exception") break;
                    b.Stmts.Add($"{ln.Use(idx)} = {v};");
                    break;
                }
                case 0x64: st.Add("/*global*/this"); break;
                case 0x65: st.Add($"/*scope{c.A}*/this"); break;
                case 0x6C: { var r = Pop(st); st.Add($"{r}.slot{c.A}"); break; }
                case 0x6D: { var v = Pop(st); var r = Pop(st); if (r == "exception") break; b.Stmts.Add($"{r}.slot{c.A} = {v};"); break; }
                case 0x6E: st.Add($"global.slot{c.A}"); break;
                case 0x6F: { var v = Pop(st); b.Stmts.Add($"global.slot{c.A} = {v};"); break; }
                case 0x70: st.Add($"Convert.ToString({Pop(st)})"); break;
                case 0x73: st.Add($"(int)({Pop(st)})"); break; case 0x74: st.Add($"(uint)({Pop(st)})"); break;
                case 0x75: st.Add($"(double)({Pop(st)})"); break; case 0x76: st.Add(Bool(Pop(st))); break;
                case 0x77: case 0x82: case 0x78: break;
                case 0x80: case 0x86: break;
                case 0x85: st.Add($"AsRt.ToString({Pop(st)})"); break;
                case 0x87: { var t = Pop(st); break; }
                case 0x90: case 0xC4: st.Add($"-({Pop(st)})"); break;
                case 0x91: case 0xC0: st.Add($"({Pop(st)} + 1)"); break; case 0x93: case 0xC1: st.Add($"({Pop(st)} - 1)"); break;
                case 0x92: case 0xC2: { var l = ln.Use(c.A); b.Stmts.Add($"{l}++;"); break; }
                case 0x94: case 0xC3: { var l = ln.Use(c.A); b.Stmts.Add($"{l}--;"); break; }
                case 0x95: st.Add($"AsRt.TypeOf({Pop(st)})"); break;
                case 0x96: st.Add(Not(Pop(st))); break;
                case 0x97: st.Add($"(~(int){Pop(st)})"); break;
                case 0xA0: case 0xC5: { var r = Pop(st); var l = Pop(st); st.Add($"({l} + {r})"); break; }
                case 0xA1: case 0xC6: { var r = Pop(st); var l = Pop(st); st.Add($"({l} - {r})"); break; }
                case 0xA2: case 0xC7: { var r = Pop(st); var l = Pop(st); st.Add($"({l} * {r})"); break; }
                case 0xA3: { var r = Pop(st); var l = Pop(st); st.Add($"({l} / {r})"); break; }
                case 0xA4: { var r = Pop(st); var l = Pop(st); st.Add($"({l} % {r})"); break; }
                case 0xA5: { var r = Pop(st); var l = Pop(st); st.Add($"((int){l} << (int){r})"); break; }
                case 0xA6: { var r = Pop(st); var l = Pop(st); st.Add($"((int){l} >> (int){r})"); break; }
                case 0xA7: { var r = Pop(st); var l = Pop(st); st.Add($"((int)((uint){l} >> (int){r}))"); break; }
                case 0xA8: { var r = Pop(st); var l = Pop(st); st.Add($"((int){l} & (int){r})"); break; }
                case 0xA9: { var r = Pop(st); var l = Pop(st); st.Add($"((int){l} | (int){r})"); break; }
                case 0xAA: { var r = Pop(st); var l = Pop(st); st.Add($"((int){l} ^ (int){r})"); break; }
                case 0xAB: { var r = Pop(st); var l = Pop(st); st.Add(Mark($"AsRt.Eq({l}, {r})")); break; }
                case 0xAC: { var r = Pop(st); var l = Pop(st); st.Add(Mark($"AsRt.StrictEq({l}, {r})")); break; }
                case 0xAD: { var r = Pop(st); var l = Pop(st); st.Add(Mark($"({l} < {r})")); break; }
                case 0xAE: { var r = Pop(st); var l = Pop(st); st.Add(Mark($"({l} <= {r})")); break; }
                case 0xAF: { var r = Pop(st); var l = Pop(st); st.Add(Mark($"({l} > {r})")); break; }
                case 0xB0: { var r = Pop(st); var l = Pop(st); st.Add(Mark($"({l} >= {r})")); break; }
                case 0xB1: { var r = Pop(st); var l = Pop(st); st.Add(Mark($"AsRt.InstanceOf({l}, {TypeVal(r)})")); break; }
                case 0xB2: { var v = Pop(st); st.Add($"({v} is {TypeName(c.A)})"); break; }
                case 0xB3: { var t = Pop(st); var v = Pop(st); st.Add(Mark($"AsRt.IsTypeLate({v}, {TypeVal(t)})")); break; }
                case 0xB4: { var o = Pop(st); var n = Pop(st); st.Add($"AsRt.In({n}, {o})"); break; }
                case 0x43: case 0x44: Unknown++; UnknownOps.Add(c.Name); break;
                case 0x1B:
                {
                    Switches++;
                    b.SwitchInsn = c; b.SwitchExpr = Pop(st);
                    break;
                }
                default:
                    if (c.IsJump)
                    {
                        string cond = null;
                        switch (c.Op)
                        {
                            case 0x10: b.Uncond = true; break;
                            case 0x11: cond = Bool(Pop(st)); break;
                            case 0x12: cond = Not(Pop(st)); break;
                            case 0x13: case 0x14: case 0x15: case 0x16: case 0x17: case 0x18: case 0x19: case 0x1A:
                            case 0x0C: case 0x0D: case 0x0E: case 0x0F:
                            {
                                var r = Pop(st); var l = Pop(st);
                                cond = c.Op switch
                                {
                                    0x13 => $"AsRt.Eq({l}, {r})", 0x14 => $"!AsRt.Eq({l}, {r})", 0x15 => $"({l} < {r})", 0x16 => $"({l} <= {r})",
                                    0x17 => $"({l} > {r})", 0x18 => $"({l} >= {r})", 0x19 => $"AsRt.StrictEq({l}, {r})", 0x1A => $"!AsRt.StrictEq({l}, {r})",
                                    0x0C => $"!({l} < {r})", 0x0D => $"!({l} <= {r})", 0x0E => $"!({l} > {r})", _ => $"!({l} >= {r})",
                                };
                                break;
                            }
                        }
                        b.Cond = cond;
                        b.TargetBlock = blockOfInsn[offToIdx[c.Target]];
                    }
                    else { Unknown++; UnknownOps.Add(c.Name); }
                    break;
            }
        }
    }

    static string TypeVal(string t) => System.Text.RegularExpressions.Regex.IsMatch(t, @"^[A-Z]\w*$") ? $"typeof({t})" : t;

    string ctorSuperCall(string args) => $"__super({args});";

    static string PopArgs(List<string> st, int n)
    {
        var a = new string[n];
        for (int k = n - 1; k >= 0; k--) a[k] = Pop(st);
        return string.Join(", ", a);
    }

    // ---- structuring -------------------------------------------------------------------------------------

    void Ind(StringBuilder sb, int d) { sb.Append(' ', d * 2); }

    void Emit(StringBuilder sb, List<Block> bl, int from, int to, int depth, HashSet<int> labels, HashSet<int> done)
    {
        for (int i = from; i < to; i++)
        {
            var b = bl[i];
            // a loop head: unconditional forward jump to a block that ends with a conditional jump back to i+1
            if (b.Uncond && b.TargetBlock > i && b.TargetBlock < to && bl[b.TargetBlock].Cond != null && bl[b.TargetBlock].TargetBlock == i + 1)
            {
                foreach (var s in b.Stmts) { Ind(sb, depth); sb.AppendLine(s); }
                int c = b.TargetBlock;
                var cb = bl[c];
                Ind(sb, depth);
                if (cb.Stmts.Count == 0)
                {
                    sb.Append("while (").Append(cb.Cond).AppendLine(") {");
                    Emit(sb, bl, i + 1, c, depth + 1, labels, done);
                }
                else
                {
                    sb.AppendLine("while (true) {");
                    Emit(sb, bl, i + 1, c, depth + 1, labels, done);
                    foreach (var s in cb.Stmts) { Ind(sb, depth + 1); sb.AppendLine(s); }
                    Ind(sb, depth + 1); sb.Append("if (!(").Append(cb.Cond).AppendLine(")) break;");
                }
                Ind(sb, depth); sb.AppendLine("}");
                i = c;
                continue;
            }
            var tryEx = curEx.FirstOrDefault(x => !x.done && x.fromBlock == i);
            if (tryEx.fromBlock == i && tryEx.toBlock >= 0 && tryEx.toBlock < bl.Count && bl[tryEx.toBlock].Uncond && bl[tryEx.toBlock].TargetBlock > tryEx.target && bl[tryEx.toBlock].TargetBlock <= to && tryEx.target > tryEx.toBlock)
            {
                curEx[curEx.IndexOf(tryEx)] = (tryEx.fromBlock, tryEx.toBlock, tryEx.target, true);
                int j = bl[tryEx.toBlock].TargetBlock;
                Ind(sb, depth); sb.AppendLine("try {");
                Emit(sb, bl, i, tryEx.toBlock, depth + 1, labels, done);
                Ind(sb, depth); sb.AppendLine("} catch (Exception exception) {");
                Emit(sb, bl, tryEx.target, j, depth + 1, labels, done);
                Ind(sb, depth); sb.AppendLine("}");
                i = j - 1;
                continue;
            }
            if (labels.Contains(i)) { Ind(sb, depth - 1); sb.AppendLine($"L{i}: ;"); }
            foreach (var s in b.Stmts) { Ind(sb, depth); sb.AppendLine(FixMarkers(s)); }
            if (b.SwitchInsn != null)
            {
                Unstructured++;
                Ind(sb, depth); sb.Append("switch ((int)(").Append(b.SwitchExpr).AppendLine(")) {");
                var tg = b.SwitchInsn.Cases;
                for (int k = 1; k < tg.Length; k++) { Ind(sb, depth + 1); sb.AppendLine($"case {k - 1}: goto L{BlockAt(bl, tg[k])};"); labels.Add(BlockAt(bl, tg[k])); }
                Ind(sb, depth + 1); sb.AppendLine($"default: goto L{BlockAt(bl, tg[0])};"); labels.Add(BlockAt(bl, tg[0]));
                Ind(sb, depth); sb.AppendLine("}");
            }
            else if (b.Cond != null)
            {
                int t = b.TargetBlock;
                if (t > i + 1 && t <= to)
                {
                    // forward conditional: body is i+1 .. t-1; else part if the last body block jumps over [t, e)
                    var last = bl[t - 1];
                    if (last.Uncond && last.TargetBlock > t && last.TargetBlock <= to && last.Stmts.Count >= 0 && t - 1 > i)
                    {
                        int e = last.TargetBlock;
                        Ind(sb, depth); sb.Append("if (").Append(Neg(b.Cond)).AppendLine(") {");
                        Emit(sb, bl, i + 1, t - 1, depth + 1, labels, done);
                        foreach (var s in last.Stmts) { Ind(sb, depth + 1); sb.AppendLine(s); }
                        Ind(sb, depth); sb.AppendLine("} else {");
                        Emit(sb, bl, t, e, depth + 1, labels, done);
                        Ind(sb, depth); sb.AppendLine("}");
                        i = e - 1;
                    }
                    else
                    {
                        Ind(sb, depth); sb.Append("if (").Append(Neg(b.Cond)).AppendLine(") {");
                        Emit(sb, bl, i + 1, t, depth + 1, labels, done);
                        Ind(sb, depth); sb.AppendLine("}");
                        i = t - 1;
                    }
                }
                else
                {
                    Unstructured++;
                    Ind(sb, depth); sb.Append("if (").Append(b.Cond).AppendLine($") goto L{t};"); labels.Add(t);
                }
            }
            else if (b.Uncond)
            {
                if (b.TargetBlock == i + 1) continue;
                Unstructured++;
                Ind(sb, depth); sb.AppendLine($"goto L{b.TargetBlock};"); labels.Add(b.TargetBlock);
            }
        }
    }

    List<(int fromBlock, int toBlock, int target, bool done)> curEx = new();
    string FixMarkers(string s) => s.Contains("§F:") ? System.Text.RegularExpressions.Regex.Replace(s, @"§F:(\w+)", m => ScopeName(m.Groups[1].Value)) : s;
    int BlockAt(List<Block> bl, int off) => curOffBlock[off];
    Dictionary<int, int> curOffBlock;
}
