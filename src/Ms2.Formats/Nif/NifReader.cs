using System.Buffers.Binary;

namespace Ms2.Formats.Nif;

/// <summary>
/// A decoded struct or block: field name -> value, in file order.
/// Values: long (integer basics), float (float/hfloat), NifStruct, long[]/float[] (basic arrays),
/// byte[] (binary arrays), object[] (arrays of structs or of rows).
/// </summary>
public sealed class NifStruct : Dictionary<string, object>
{
    public string Type;

    public NifStruct() { }
    public NifStruct(int capacity) : base(capacity) { }

    public long Int(string k) => this[k] switch { long l => l, float f => (long)f, _ => throw new InvalidCastException(k) };
    public float Float(string k) => this[k] switch { float f => f, long l => l, _ => throw new InvalidCastException(k) };
    public NifStruct Struct(string k) => (NifStruct)this[k];
    public long[] Ints(string k) => this[k] as long[] ?? Array.Empty<long>();
    public float[] Floats(string k) => this[k] as float[] ?? Array.Empty<float>();
    public NifStruct[] Structs(string k) => this[k] is object[] a ? a.Cast<NifStruct>().ToArray() : Array.Empty<NifStruct>();
    public byte[] Bytes(string k) => this[k] switch { byte[] b => b, long[] l => l.Select(x => (byte)x).ToArray(), _ => throw new InvalidCastException(k) };
    public bool Has(string k) => ContainsKey(k);
    public long IntOr(string k, long def) => TryGetValue(k, out var v) && v is long l ? l : def;
}

/// <summary>Reads nif.xml-described structs from a byte buffer (little endian).</summary>
public sealed class NifReader
{
    readonly NifSpec spec;
    readonly byte[] b;
    public int Pos;

    public NifReader(NifSpec spec, byte[] buf, int pos) { this.spec = spec; b = buf; Pos = pos; }

    static readonly Dictionary<string, (char fmt, int size)> Basic = new()
    {
        ["uint64"] = ('Q', 8), ["int64"] = ('q', 8), ["ulittle32"] = ('I', 4), ["uint"] = ('I', 4), ["int"] = ('i', 4),
        ["ushort"] = ('H', 2), ["short"] = ('h', 2), ["char"] = ('B', 1), ["sbyte"] = ('b', 1), ["byte"] = ('B', 1),
        ["normbyte"] = ('B', 1), ["bool"] = ('B', 1), ["BlockTypeIndex"] = ('H', 2), ["FileVersion"] = ('I', 4),
        ["float"] = ('f', 4), ["hfloat"] = ('e', 2), ["Ptr"] = ('i', 4), ["Ref"] = ('i', 4), ["StringOffset"] = ('I', 4),
        ["NiFixedString"] = ('I', 4),
    };

    public static bool IsBasic(string t) => Basic.ContainsKey(t);

    bool IsFloat(char f) => f == 'f' || f == 'e';

    long ReadInt(char f)
    {
        var s = b.AsSpan(Pos);
        long v = f switch
        {
            'Q' => (long)BinaryPrimitives.ReadUInt64LittleEndian(s),
            'q' => BinaryPrimitives.ReadInt64LittleEndian(s),
            'I' => BinaryPrimitives.ReadUInt32LittleEndian(s),
            'i' => BinaryPrimitives.ReadInt32LittleEndian(s),
            'H' => BinaryPrimitives.ReadUInt16LittleEndian(s),
            'h' => BinaryPrimitives.ReadInt16LittleEndian(s),
            'B' => s[0],
            'b' => (sbyte)s[0],
            _ => throw new InvalidOperationException(f.ToString()),
        };
        return v;
    }

    float ReadFloat(char f) => f == 'f'
        ? BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(Pos))
        : (float)BinaryPrimitives.ReadHalfLittleEndian(b.AsSpan(Pos));

    object ReadBasic(string t, int? n, bool binary)
    {
        var (f, size) = Basic[t];
        return ReadBasic(f, size, n, binary);
    }

    /// <summary>The basic read of a resolved storage type: its format and size, for a field that knows them.</summary>
    internal static bool BasicOf(string storage, out char fmt, out int size)
    {
        if (storage != null && Basic.TryGetValue(storage, out var fs)) { (fmt, size) = fs; return true; }
        fmt = '\0'; size = 0;
        return false;
    }

    object ReadBasic(char f, int size, int? n, bool binary)
    {
        if (n is null)
        {
            object v = IsFloat(f) ? (object)ReadFloat(f) : (object)ReadInt(f);
            Pos += size;
            return v;
        }
        int cnt = n.Value;
        if (binary)
        {
            var bytes = b.AsSpan(Pos, cnt).ToArray();
            Pos += cnt;
            return bytes;
        }
        if (IsFloat(f))
        {
            var a = new float[cnt];
            for (int i = 0; i < cnt; i++) { a[i] = ReadFloat(f); Pos += size; }
            return a;
        }
        var l = new long[cnt];
        for (int i = 0; i < cnt; i++) { l[i] = ReadInt(f); Pos += size; }
        return l;
    }

    object Value(string t, string tmpl, Dictionary<string, double> args, int? n, bool binary, string btype)
    {
        if (t == "#T#") t = tmpl;
        t = spec.Storage(t);
        if (Basic.ContainsKey(t)) return ReadBasic(t, n, binary);
        if (n is not null)
        {
            var a = new object[n.Value];
            for (int i = 0; i < a.Length; i++) a[i] = Struct(t, tmpl, args, btype);
            return a;
        }
        return Struct(t, tmpl, args, btype);
    }

    /// <summary>Value of a field whose own type is not the template: its storage, basic format and struct fields were resolved
    /// once (NifSpec.Fields), so this reads what Value(t, ...) reads without looking the type up again.</summary>
    object Value(NifField a, string t, string tmpl, Dictionary<string, double> args, int? n, bool binary, string btype)
    {
        if (a.Store == null) return Value(t, tmpl, args, n, binary, btype);
        if (a.Fmt != '\0') return ReadBasic(a.Fmt, a.Size, n, binary);
        var fields = a.Sub ??= spec.Fields(a.Store);
        if (n is not null)
        {
            var arr = new object[n.Value];
            for (int i = 0; i < arr.Length; i++) arr[i] = Struct(fields, a.Store, tmpl, args, btype);
            return arr;
        }
        return Struct(fields, a.Store, tmpl, args, btype);
    }

    internal static double ToNum(object o) => o switch
    {
        long l => l, float f => f, null => 0,
        Array a => a.Length != 0 ? 1 : 0,
        NifStruct s => s.Count != 0 ? 1 : 0,
        _ => 0,
    };

    public NifStruct Struct(string name, string tmpl, Dictionary<string, double> args, string btype) =>
        Struct(spec.Fields(name), name, tmpl, args, btype);

    NifStruct Struct(List<NifField> fields, string name, string tmpl, Dictionary<string, double> args, string btype)
    {
        var d = new NifStruct(fields.Count) { Type = name };
        var scope = new NifScope(d, args, spec);
        for (int fi = 0; fi < fields.Count; fi++)
        {
            var a = fields[fi];
            if (a.OnlyT != null && !spec.IsA(btype, a.OnlyT)) continue;
            if (a.ExcludeT != null && spec.IsA(btype, a.ExcludeT)) continue;
            if (a.Cond != null && a.Cond.Eval(scope) == 0) continue;
            Dictionary<string, double> sub = null;
            if (a.Args.Length > 0)
            {
                sub = new Dictionary<string, double>();
                foreach (var (k, e) in a.Args) sub[k] = e.Eval(scope);
            }
            var t = a.Type == "#T#" ? tmpl : a.Type;
            var ft = a.Template == "#T#" ? tmpl : a.Template;
            object v;
            if (a.Length != null)
            {
                int n = (int)a.Length.Eval(scope);
                if (a.Width != null)
                {
                    int[] widths;
                    var st = spec.Storage(t);
                    if (IsBasic(st) && d.TryGetValue(a.WidthName, out var wl) && wl is long[] arr)
                    {
                        widths = new int[arr.Length];
                        for (int i = 0; i < widths.Length; i++) widths[i] = (int)arr[i];
                    }
                    else
                    {
                        widths = new int[n];
                        Array.Fill(widths, (int)a.Width.Eval(scope));
                    }
                    var rows = new object[widths.Length];
                    for (int i = 0; i < rows.Length; i++) rows[i] = Value(a, t, ft, sub, widths[i], a.Binary, btype);
                    v = rows;
                }
                else v = Value(a, t, ft, sub, n, a.Binary, btype);
            }
            else v = Value(a, t, ft, sub, null, false, btype);
            d[a.Name] = v;
        }
        return d;
    }
}

/// <summary>
/// What a compiled expression's names resolve against while a struct is read: the fields read so far, the template
/// arguments and the file version. The order is the reader's: Version, User Version, BS Header*, #ARG*, then a field
/// (a\b walks sub-structs); a missing field is 0, an array or struct is 1 when not empty.
/// </summary>
internal readonly struct NifScope
{
    internal enum Kind : byte { Version, UserVersion, BsVersion, Arg, Field, Path }

    readonly NifStruct d;
    readonly Dictionary<string, double> args;
    readonly NifSpec spec;

    public NifScope(NifStruct d, Dictionary<string, double> args, NifSpec spec) { this.d = d; this.args = args; this.spec = spec; }

    internal static Kind Classify(string name) => name switch
    {
        "Version" => Kind.Version,
        "User Version" => Kind.UserVersion,
        _ when name.StartsWith("BS Header", StringComparison.Ordinal) => Kind.BsVersion,
        _ when name.StartsWith("#ARG", StringComparison.Ordinal) => Kind.Arg,
        _ when name.IndexOf('\\') < 0 => Kind.Field,
        _ => Kind.Path,
    };

    internal double Get(Kind kind, string name, string argKey, string[] path)
    {
        switch (kind)
        {
            case Kind.Version: return spec.Version;
            case Kind.UserVersion: return spec.UserVersion;
            case Kind.BsVersion: return spec.BsVersion;
            case Kind.Arg: return args[argKey];
            case Kind.Field: return d.TryGetValue(name, out var v) ? NifReader.ToNum(v) : 0;
        }
        object cur = d;
        foreach (var k in path)
            cur = cur is NifStruct s && s.TryGetValue(k, out var v) ? v : 0L;
        return NifReader.ToNum(cur);
    }
}
