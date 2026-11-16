namespace Ms2.Swf.Abc;

public sealed class Insn
{
    public int Off, Next;
    public byte Op;
    public string Name;
    public int A, B;            // operands (u30 / byte / constant index)
    public int Target = -1;     // absolute code offset for jumps
    public int[] Cases;         // lookupswitch: default first, then cases (absolute offsets)
    public bool IsJump => Op >= 0x0C && Op <= 0x1A;
    public bool IsCondJump => Op >= 0x0C && Op <= 0x1A && Op != 0x10;
}

public static class Disasm
{
    // kinds: u = u30, b = signed byte, j = s24 branch, m = multiname + argc, '-' = none
    static readonly Dictionary<int, (string Name, string Kind)> T = Build();

    static Dictionary<int, (string, string)> Build()
    {
        var d = new Dictionary<int, (string, string)>();
        void A(int op, string n, string k = "") => d[op] = (n, k);
        A(0x01, "bkpt"); A(0x02, "nop"); A(0x03, "throw"); A(0x04, "getsuper", "u"); A(0x05, "setsuper", "u"); A(0x06, "dxns", "u"); A(0x07, "dxnslate");
        A(0x08, "kill", "u"); A(0x09, "label");
        string[] jn = { "ifnlt", "ifnle", "ifngt", "ifnge", "jump", "iftrue", "iffalse", "ifeq", "ifne", "iflt", "ifle", "ifgt", "ifge", "ifstricteq", "ifstrictne" };
        for (int i = 0; i < jn.Length; i++) A(0x0C + i, jn[i], "j");
        A(0x1B, "lookupswitch", "S"); A(0x1C, "pushwith"); A(0x1D, "popscope"); A(0x1E, "nextname"); A(0x1F, "hasnext"); A(0x20, "pushnull"); A(0x21, "pushundefined");
        A(0x23, "nextvalue"); A(0x24, "pushbyte", "b"); A(0x25, "pushshort", "u"); A(0x26, "pushtrue"); A(0x27, "pushfalse"); A(0x28, "pushnan"); A(0x29, "pop");
        A(0x2A, "dup"); A(0x2B, "swap"); A(0x2C, "pushstring", "u"); A(0x2D, "pushint", "u"); A(0x2E, "pushuint", "u"); A(0x2F, "pushdouble", "u"); A(0x30, "pushscope");
        A(0x31, "pushnamespace", "u"); A(0x32, "hasnext2", "uu");
        A(0x40, "newfunction", "u"); A(0x41, "call", "u"); A(0x42, "construct", "u"); A(0x43, "callmethod", "uu"); A(0x44, "callstatic", "uu"); A(0x45, "callsuper", "uu");
        A(0x46, "callproperty", "uu"); A(0x47, "returnvoid"); A(0x48, "returnvalue"); A(0x49, "constructsuper", "u"); A(0x4A, "constructprop", "uu"); A(0x4C, "callproplex", "uu");
        A(0x4E, "callsupervoid", "uu"); A(0x4F, "callpropvoid", "uu");
        A(0x53, "applytype", "u"); A(0x55, "newobject", "u"); A(0x56, "newarray", "u"); A(0x57, "newactivation"); A(0x58, "newclass", "u"); A(0x59, "getdescendants", "u"); A(0x5A, "newcatch", "u");
        A(0x5D, "findpropstrict", "u"); A(0x5E, "findproperty", "u"); A(0x5F, "finddef", "u"); A(0x60, "getlex", "u"); A(0x61, "setproperty", "u"); A(0x62, "getlocal", "u");
        A(0x63, "setlocal", "u"); A(0x64, "getglobalscope"); A(0x65, "getscopeobject", "b"); A(0x66, "getproperty", "u"); A(0x68, "initproperty", "u"); A(0x6A, "deleteproperty", "u");
        A(0x6C, "getslot", "u"); A(0x6D, "setslot", "u"); A(0x6E, "getglobalslot", "u"); A(0x6F, "setglobalslot", "u");
        A(0x70, "convert_s"); A(0x71, "esc_xelem"); A(0x72, "esc_xattr"); A(0x73, "convert_i"); A(0x74, "convert_u"); A(0x75, "convert_d"); A(0x76, "convert_b"); A(0x77, "convert_o");
        A(0x78, "checkfilter"); A(0x80, "coerce", "u"); A(0x82, "coerce_a"); A(0x85, "coerce_s"); A(0x86, "astype", "u"); A(0x87, "astypelate");
        A(0x90, "negate"); A(0x91, "increment"); A(0x92, "inclocal", "u"); A(0x93, "decrement"); A(0x94, "declocal", "u"); A(0x95, "typeof"); A(0x96, "not"); A(0x97, "bitnot");
        string[] bin = { "add", "subtract", "multiply", "divide", "modulo", "lshift", "rshift", "urshift", "bitand", "bitor", "bitxor", "equals", "strictequals", "lessthan", "lessequals", "greaterthan", "greaterequals", "instanceof" };
        for (int i = 0; i < bin.Length; i++) A(0xA0 + i, bin[i]);
        A(0xB2, "istype", "u"); A(0xB3, "istypelate"); A(0xB4, "in");
        A(0xC0, "increment_i"); A(0xC1, "decrement_i"); A(0xC2, "inclocal_i", "u"); A(0xC3, "declocal_i", "u"); A(0xC4, "negate_i"); A(0xC5, "add_i"); A(0xC6, "subtract_i"); A(0xC7, "multiply_i");
        for (int i = 0; i < 4; i++) { A(0xD0 + i, "getlocal" + i); A(0xD4 + i, "setlocal" + i); }
        A(0xEF, "debug", "D"); A(0xF0, "debugline", "u"); A(0xF1, "debugfile", "u"); A(0xF2, "bkptline", "u"); A(0xF3, "timestamp");
        return d;
    }

    public static string NameOf(int op) => T.TryGetValue(op, out var t) ? t.Name : "op_0x" + op.ToString("x2");

    public static List<Insn> Decode(byte[] code)
    {
        var l = new List<Insn>();
        int p = 0;
        int U30() { uint v = 0; int s = 0; while (true) { byte x = code[p++]; v |= (uint)(x & 0x7f) << s; if ((x & 0x80) == 0) break; s += 7; } return (int)v; }
        int S24() { int v = code[p] | code[p + 1] << 8 | code[p + 2] << 16; p += 3; return (v << 8) >> 8; }
        while (p < code.Length)
        {
            var i = new Insn { Off = p, Op = code[p++] };
            if (!T.TryGetValue(i.Op, out var t)) { i.Name = "op_0x" + i.Op.ToString("x2"); i.Next = p; l.Add(i); continue; }
            i.Name = t.Name;
            switch (t.Kind)
            {
                case "u": i.A = U30(); break;
                case "uu": i.A = U30(); i.B = U30(); break;
                case "b": i.A = (sbyte)code[p++]; break;
                case "j": { int o = S24(); i.Target = p + o; break; }
                case "S":
                {
                    int baseOff = i.Off;
                    int d = S24(); int n = U30();
                    i.Cases = new int[n + 2]; i.Cases[0] = baseOff + d;
                    for (int k = 0; k <= n; k++) i.Cases[k + 1] = baseOff + S24();
                    break;
                }
                case "D": i.A = code[p++]; i.B = U30(); p++; U30(); break;
            }
            i.Next = p;
            l.Add(i);
        }
        return l;
    }
}

public static class AbcHash
{
    static readonly HashSet<int> MnOps = new() { 0x04, 0x05, 0x45, 0x46, 0x4A, 0x4C, 0x4E, 0x4F, 0x59, 0x5D, 0x5E, 0x5F, 0x60, 0x61, 0x66, 0x68, 0x6A, 0x80, 0x86, 0xB2 };

    /// <summary>Text form of a method body independent of the constant pool layout (so equal code in two movies hashes equal).</summary>
    public static string Normalised(AbcFile abc, MethodInfo m, int depth = 0)
    {
        if (m.Body == null) return "";
        var sb = new System.Text.StringBuilder();
        sb.Append(m.ParamCount).Append(':').Append(abc.Name(m.ReturnType)).Append('|');
        foreach (var i in Disasm.Decode(m.Body.Code))
        {
            sb.Append(i.Name);
            if (MnOps.Contains(i.Op)) sb.Append(' ').Append(abc.Name(i.A));
            else if (i.Op == 0x2C) sb.Append(" \"").Append(abc.Strings[i.A]);
            else if (i.Op == 0x2D) sb.Append(' ').Append(abc.Ints[i.A]);
            else if (i.Op == 0x2E) sb.Append(' ').Append(abc.Uints[i.A]);
            else if (i.Op == 0x2F) sb.Append(' ').Append(abc.Doubles[i.A]);
            else if (i.Op == 0x40 && depth < 6) sb.Append(" {").Append(Normalised(abc, abc.Methods[i.A], depth + 1)).Append('}');
            else if (i.Op == 0x58) sb.Append(' ').Append(abc.Name(abc.Instances[i.A].Name));
            else if (i.Op is 0xEF or 0xF0 or 0xF1) continue;   // debug info
            else { sb.Append(' ').Append(i.A); if (i.B != 0) sb.Append(',').Append(i.B); if (i.Target >= 0) sb.Append("->").Append(i.Target); }
            sb.Append(';');
        }
        return sb.ToString();
    }
}
