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

    static double ToNum(object o) => o switch
    {
        long l => l, float f => f, null => 0,
        Array a => a.Length != 0 ? 1 : 0,
        NifStruct s => s.Count != 0 ? 1 : 0,
        _ => 0,
    };

    static double Lookup(NifStruct d, Dictionary<string, double> args, NifSpec spec, string name)
    {
        switch (name)
        {
            case "Version": return spec.Version;
            case "User Version": return spec.UserVersion;
        }
        if (name.StartsWith("BS Header")) return spec.BsVersion;
        if (name.StartsWith("#ARG")) return args[name.Trim('#')];
        object cur = d;
        foreach (var k in name.Split('\\'))
            cur = cur is NifStruct s && s.TryGetValue(k, out var v) ? v : 0L;
        return ToNum(cur);
    }

    public NifStruct Struct(string name, string tmpl, Dictionary<string, double> args, string btype)
    {
        var d = new NifStruct { Type = name };
        Func<string, double> get = nm => Lookup(d, args, spec, nm);
        foreach (var a in spec.Fields(name))
        {
            if (a.OnlyT != null && !spec.IsA(btype, a.OnlyT)) continue;
            if (a.ExcludeT != null && spec.IsA(btype, a.ExcludeT)) continue;
            if (a.Cond != null && a.Cond.Eval(get) == 0) continue;
            Dictionary<string, double> sub = null;
            if (a.Args.Length > 0)
            {
                sub = new Dictionary<string, double>();
                foreach (var (k, e) in a.Args) sub[k] = e.Eval(get);
            }
            var t = a.Type == "#T#" ? tmpl : a.Type;
            var ft = a.Template == "#T#" ? tmpl : a.Template;
            object v;
            if (a.Length != null)
            {
                int n = (int)a.Length.Eval(get);
                if (a.Width != null)
                {
                    int[] widths;
                    var st = spec.Storage(t);
                    if (IsBasic(st) && d.TryGetValue(a.WidthName, out var wl) && wl is long[] arr)
                        widths = arr.Select(x => (int)x).ToArray();
                    else
                        widths = Enumerable.Repeat((int)a.Width.Eval(get), n).ToArray();
                    var rows = new object[widths.Length];
                    for (int i = 0; i < rows.Length; i++) rows[i] = Value(t, ft, sub, widths[i], a.Binary, btype);
                    v = rows;
                }
                else v = Value(t, ft, sub, n, a.Binary, btype);
            }
            else v = Value(t, ft, sub, null, false, btype);
            d[a.Name] = v;
        }
        return d;
    }
}
