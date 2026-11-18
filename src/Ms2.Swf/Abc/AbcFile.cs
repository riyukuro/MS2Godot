using System.Text;

namespace Ms2.Swf.Abc;

public sealed class Namespace { public int Kind; public string Name; }

public sealed class Multiname
{
    public int Kind;                    // 0x07 QName ... 0x1D TypeName
    public string Name;                 // null for runtime/any
    public int NsIndex, NsSetIndex;
    public int TypeBase; public int[] TypeParams;
    public bool IsQName => Kind is 0x07 or 0x0D;
    public bool IsRuntimeName => Kind is 0x1B or 0x1C or 0x11 or 0x12;
    public bool IsRuntimeNs => Kind is 0x0F or 0x10 or 0x11 or 0x12;
}

public sealed class MethodInfo
{
    public int Index;
    public int ParamCount, ReturnType;
    public int[] ParamTypes;
    public int Name, Flags;
    public List<(int Kind, int Val)> Options;
    public string[] ParamNames;
    public MethodBody Body;
    public bool NeedRest => (Flags & 4) != 0;
    public bool HasOptional => (Flags & 8) != 0;
}

public sealed class ExceptionInfo { public int From, To, Target, ExcType, VarName; }

public sealed class MethodBody
{
    public int Method, MaxStack, LocalCount, InitScopeDepth, MaxScopeDepth;
    public byte[] Code;
    public List<ExceptionInfo> Exceptions = new();
    public List<Trait> Traits = new();
}

public enum TraitKind { Slot = 0, Method = 1, Getter = 2, Setter = 3, Class = 4, Function = 5, Const = 6 }

public sealed class Trait
{
    public int Name;                    // multiname index
    public TraitKind Kind;
    public int Attrs;                   // 1 final, 2 override, 4 metadata
    public int SlotId, TypeName, ValueIndex, ValueKind;   // slot/const
    public int ClassIndex;              // class trait
    public int MethodIndex;             // method/getter/setter/function; DispId in SlotId
}

public sealed class InstanceInfo
{
    public int Name, Super, Flags, ProtectedNs;
    public int[] Interfaces;
    public int Init;
    public List<Trait> Traits = new();
    public bool IsInterface => (Flags & 4) != 0;
    public bool IsSealed => (Flags & 1) != 0;
}

public sealed class ClassInfo { public int Init; public List<Trait> Traits = new(); }
public sealed class ScriptInfo { public int Init; public List<Trait> Traits = new(); }

/// <summary>An ABC (ActionScript 3 bytecode) block: constant pool, methods, classes, scripts, method bodies.</summary>
public sealed class AbcFile
{
    public int Minor, Major;
    public int[] Ints; public uint[] Uints; public double[] Doubles; public string[] Strings;
    public Namespace[] Namespaces; public int[][] NsSets; public Multiname[] Multinames;
    public MethodInfo[] Methods;
    public InstanceInfo[] Instances; public ClassInfo[] Classes; public ScriptInfo[] Scripts;

    int p; byte[] b;

    /// <summary>The SHA-1 of the block's bytes (hex): the key translated code is registered under.</summary>
    public string Hash;

    public static AbcFile Parse(byte[] data)
    {
        var f = new AbcFile { b = data, Hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(data)) };
        f.Read();
        f.b = null;
        return f;
    }

    int U30() { uint v = 0; int s = 0; while (true) { byte x = b[p++]; v |= (uint)(x & 0x7f) << s; if ((x & 0x80) == 0) break; s += 7; if (s > 35) throw new InvalidDataException("bad u30"); } return (int)v; }
    int U8() => b[p++];
    int U16() { int v = b[p] | b[p + 1] << 8; p += 2; return v; }
    int S24() { int v = b[p] | b[p + 1] << 8 | b[p + 2] << 16; p += 3; return (v << 8) >> 8; }

    void Read()
    {
        Minor = U16(); Major = U16();
        int n = U30(); Ints = new int[Math.Max(n, 1)];
        for (int i = 1; i < n; i++) { uint v = 0; int s = 0; while (true) { byte x = b[p++]; v |= (uint)(x & 0x7f) << s; if ((x & 0x80) == 0) break; s += 7; } Ints[i] = (int)v; }
        n = U30(); Uints = new uint[Math.Max(n, 1)];
        for (int i = 1; i < n; i++) { uint v = 0; int s = 0; while (true) { byte x = b[p++]; v |= (uint)(x & 0x7f) << s; if ((x & 0x80) == 0) break; s += 7; } Uints[i] = v; }
        n = U30(); Doubles = new double[Math.Max(n, 1)];
        for (int i = 1; i < n; i++) { Doubles[i] = BitConverter.ToDouble(b, p); p += 8; }
        n = U30(); Strings = new string[Math.Max(n, 1)]; Strings[0] = "";
        for (int i = 1; i < n; i++) { int l = U30(); Strings[i] = Encoding.UTF8.GetString(b, p, l); p += l; }
        n = U30(); Namespaces = new Namespace[Math.Max(n, 1)]; Namespaces[0] = new Namespace { Name = "*" };
        for (int i = 1; i < n; i++) Namespaces[i] = new Namespace { Kind = U8(), Name = Strings[U30()] };
        n = U30(); NsSets = new int[Math.Max(n, 1)][]; NsSets[0] = Array.Empty<int>();
        for (int i = 1; i < n; i++) { int c = U30(); NsSets[i] = new int[c]; for (int j = 0; j < c; j++) NsSets[i][j] = U30(); }
        n = U30(); Multinames = new Multiname[Math.Max(n, 1)]; Multinames[0] = new Multiname { Kind = 0, Name = "*" };
        for (int i = 1; i < n; i++)
        {
            var m = new Multiname { Kind = U8() };
            switch (m.Kind)
            {
                case 0x07: case 0x0D: m.NsIndex = U30(); m.Name = Strings[U30()]; break;
                case 0x0F: case 0x10: m.Name = Strings[U30()]; break;
                case 0x11: case 0x12: break;
                case 0x09: case 0x0E: m.Name = Strings[U30()]; m.NsSetIndex = U30(); break;
                case 0x1B: case 0x1C: m.NsSetIndex = U30(); break;
                case 0x1D:
                    m.TypeBase = U30(); int c = U30(); m.TypeParams = new int[c]; for (int j = 0; j < c; j++) m.TypeParams[j] = U30(); break;
                default: throw new InvalidDataException("multiname kind 0x" + m.Kind.ToString("x"));
            }
            Multinames[i] = m;
        }
        n = U30(); Methods = new MethodInfo[n];
        for (int i = 0; i < n; i++)
        {
            var m = new MethodInfo { Index = i, ParamCount = U30(), ReturnType = U30() };
            m.ParamTypes = new int[m.ParamCount]; for (int j = 0; j < m.ParamCount; j++) m.ParamTypes[j] = U30();
            m.Name = U30(); m.Flags = U8();
            if ((m.Flags & 8) != 0) { int c = U30(); m.Options = new(); for (int j = 0; j < c; j++) { int v = U30(); m.Options.Add((U8(), v)); } }
            if ((m.Flags & 0x80) != 0) { m.ParamNames = new string[m.ParamCount]; for (int j = 0; j < m.ParamCount; j++) m.ParamNames[j] = Strings[U30()]; }
            Methods[i] = m;
        }
        n = U30(); for (int i = 0; i < n; i++) { U30(); int c = U30(); for (int j = 0; j < c * 2; j++) U30(); }   // metadata
        n = U30(); Instances = new InstanceInfo[n]; Classes = new ClassInfo[n];
        for (int i = 0; i < n; i++)
        {
            var ii = new InstanceInfo { Name = U30(), Super = U30(), Flags = U8() };
            if ((ii.Flags & 8) != 0) ii.ProtectedNs = U30();
            int c = U30(); ii.Interfaces = new int[c]; for (int j = 0; j < c; j++) ii.Interfaces[j] = U30();
            ii.Init = U30(); ii.Traits = ReadTraits();
            Instances[i] = ii;
        }
        for (int i = 0; i < n; i++) Classes[i] = new ClassInfo { Init = U30(), Traits = ReadTraits() };
        n = U30(); Scripts = new ScriptInfo[n];
        for (int i = 0; i < n; i++) Scripts[i] = new ScriptInfo { Init = U30(), Traits = ReadTraits() };
        n = U30();
        for (int i = 0; i < n; i++)
        {
            var bd = new MethodBody { Method = U30(), MaxStack = U30(), LocalCount = U30(), InitScopeDepth = U30(), MaxScopeDepth = U30() };
            int len = U30(); bd.Code = new byte[len]; Array.Copy(b, p, bd.Code, 0, len); p += len;
            int ec = U30();
            for (int j = 0; j < ec; j++) bd.Exceptions.Add(new ExceptionInfo { From = U30(), To = U30(), Target = U30(), ExcType = U30(), VarName = U30() });
            bd.Traits = ReadTraits();
            Methods[bd.Method].Body = bd;
        }
    }

    List<Trait> ReadTraits()
    {
        int n = U30(); var l = new List<Trait>(n);
        for (int i = 0; i < n; i++)
        {
            var t = new Trait { Name = U30() };
            int k = U8(); t.Kind = (TraitKind)(k & 15); t.Attrs = k >> 4;
            switch (t.Kind)
            {
                case TraitKind.Slot: case TraitKind.Const:
                    t.SlotId = U30(); t.TypeName = U30(); t.ValueIndex = U30(); if (t.ValueIndex != 0) t.ValueKind = U8(); break;
                case TraitKind.Class: t.SlotId = U30(); t.ClassIndex = U30(); break;
                case TraitKind.Function: t.SlotId = U30(); t.MethodIndex = U30(); break;
                default: t.SlotId = U30(); t.MethodIndex = U30(); break;
            }
            if ((t.Attrs & 4) != 0) { int c = U30(); for (int j = 0; j < c; j++) U30(); }
            l.Add(t);
        }
        return l;
    }

    // ---- names -------------------------------------------------------------------------------------------

    public string Ns(int i) => i < Namespaces.Length ? Namespaces[i].Name : "?";

    /// <summary>Qualified display name: package.Name, or Name for the unnamed package.</summary>
    public string Name(int mn)
    {
        if (mn <= 0 || mn >= Multinames.Length) return "*";
        var m = Multinames[mn];
        switch (m.Kind)
        {
            case 0x07: case 0x0D:
            {
                var ns = Namespaces[m.NsIndex];
                bool pkg = ns.Kind is 0x16 or 0x17;
                return pkg && ns.Name.Length > 0 ? ns.Name + "." + m.Name : m.Name ?? "*";
            }
            case 0x1D:
                return Name(m.TypeBase) + ".<" + string.Join(",", m.TypeParams.Select(Name)) + ">";
            case 0x09: case 0x0E:
            {
                // multiname with a namespace set: use the first package namespace if any
                var set = NsSets[m.NsSetIndex];
                var ns = set.Select(x => Namespaces[x]).FirstOrDefault(x => x.Kind == 0x16 && x.Name.Length > 0);
                return ns != null ? ns.Name + "." + m.Name : m.Name;
            }
            default: return m.Name ?? "<runtime>";
        }
    }

    /// <summary>Just the local part of a name.</summary>
    public string Local(int mn)
    {
        if (mn <= 0 || mn >= Multinames.Length) return "*";
        var m = Multinames[mn];
        return m.Kind == 0x1D ? Local(m.TypeBase) : m.Name ?? "<runtime>";
    }

    public string ClassName(int i) => Name(Instances[i].Name);
}
