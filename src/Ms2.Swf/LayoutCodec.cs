using System.Buffers.Binary;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Ms2.Swf;

/// <summary>
/// The symbols of a layout (its "symbols" object, or a library entry's) by id. Over a built layout it is the object itself;
/// over a stored layout (<see cref="LayoutCodec"/>) each symbol is decoded the first time it is asked for, so a load
/// costs only the symbols it places. The symbols that have a class or an export name are listed apart
/// (<see cref="Named"/>), in id order like the object, so a class lookup decodes nothing but the symbol it finds.
/// </summary>
[JsonConverter(typeof(LayoutSymbols.Converter))]
public sealed class LayoutSymbols : IEnumerable<KeyValuePair<string, JsonObject>>
{
    /// <summary>A symbol with a class (its "class") or an export name (its "export").</summary>
    public readonly record struct Name(int Id, string Class, string Export);

    // a built layout's object
    readonly JsonObject obj;
    // a stored one: its entries' keys and byte ranges in the reader's body, and the symbols decoded so far
    readonly LayoutCodec.Reader reader;
    readonly string[] keys;
    readonly int[] starts;
    readonly Dictionary<string, int> index;
    readonly JsonObject[] decoded;
    Name[] named;

    LayoutSymbols(JsonObject o) => obj = o;

    internal LayoutSymbols(LayoutCodec.Reader r, string[] keys, int[] starts, Name[] named)
    {
        reader = r; this.keys = keys; this.starts = starts; this.named = named;
        index = new Dictionary<string, int>(keys.Length, StringComparer.Ordinal);
        for (int i = 0; i < keys.Length; i++) index.TryAdd(keys[i], i);
        decoded = new JsonObject[keys.Length];
    }

    static readonly ConditionalWeakTable<JsonObject, LayoutSymbols> wrappers = new();

    /// <summary>The symbols of a layout's "symbols" node: a built object or a stored set; null for anything else.</summary>
    public static LayoutSymbols Of(JsonNode node) => node switch
    {
        JsonObject o => wrappers.GetValue(o, x => new LayoutSymbols(x)),
        JsonValue v when v.TryGetValue<LayoutSymbols>(out var s) => s,
        _ => null,
    };

    /// <summary>True over a stored layout (symbols decoded on demand).</summary>
    public bool Stored => reader != null;

    public int Count => obj?.Count ?? keys.Length;

    public JsonObject Get(int id) => Get(id.ToString());

    public JsonObject Get(string key)
    {
        if (obj != null) return obj.TryGetPropertyValue(key, out var n) ? n as JsonObject : null;
        return index.TryGetValue(key, out var i) ? At(i) : null;
    }

    JsonObject At(int i)
    {
        var d = Volatile.Read(ref decoded[i]);
        if (d != null) return d;
        var fresh = reader.Symbol(starts[i]);
        return Interlocked.CompareExchange(ref decoded[i], fresh, null) ?? fresh;
    }

    /// <summary>The symbols with a class or an export name, in id order.</summary>
    public IReadOnlyList<Name> Named
    {
        get
        {
            if (named != null) return named;
            var l = new List<Name>();
            foreach (var (k, s) in obj)
                if (s is JsonObject so && ((string)so["class"] ?? (string)so["export"]) != null)
                    l.Add(new Name(int.Parse(k), (string)so["class"], (string)so["export"]));
            return named = l.ToArray();
        }
    }

    /// <summary>The first symbol (in id order) whose class is <paramref name="className"/>, or null.</summary>
    public JsonObject OfClass(string className, out int id)
    {
        foreach (var n in Named)
            if (n.Class == className) { id = n.Id; return Get(n.Id); }
        id = -1;
        return null;
    }

    /// <summary>A copy that shares nothing a caller can change: a built object is deep-cloned; a stored set decodes its
    /// symbols again (a fresh decode is the copy).</summary>
    public LayoutSymbols Clone() => obj != null ? new LayoutSymbols((JsonObject)obj.DeepClone()) : new LayoutSymbols(reader, keys, starts, named);

    public IEnumerator<KeyValuePair<string, JsonObject>> GetEnumerator()
    {
        if (obj != null)
        {
            foreach (var (k, v) in obj) yield return KeyValuePair.Create(k, v as JsonObject);
            yield break;
        }
        for (int i = 0; i < keys.Length; i++) yield return KeyValuePair.Create(keys[i], At(i));
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Writes the set as the object it stands for (a stored layout serialized or deep-cloned as JSON).</summary>
    public sealed class Converter : JsonConverter<LayoutSymbols>
    {
        public override LayoutSymbols Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException("layout symbols are read from a layout file, not JSON");

        public override void Write(Utf8JsonWriter writer, LayoutSymbols value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var (k, s) in value)
            {
                writer.WritePropertyName(k);
                if (s == null) writer.WriteNullValue(); else s.WriteTo(writer, options);
            }
            writer.WriteEndObject();
        }
    }
}

/// <summary>
/// A layout (<see cref="LayoutJson.Build"/>) as bytes and back. The read gives the same tree as the build: the same
/// properties in the same order, each value of the same CLR type (an int stays an int, a float a float), except that the
/// layout's symbol objects (its "symbols" and each library entry's) come back as a <see cref="LayoutSymbols"/> that
/// decodes a symbol when it is asked for. Strings are stored once each and decoded on first use. The library can be left
/// out on read (a movie used as an import is laid out without its own library). A body that does not decode throws
/// InvalidDataException.
/// </summary>
public static class LayoutCodec
{
    const uint Magic = 0x314C534D;   // "MSL1"

    enum Tag : byte { Null, Object, Array, Int32, Int64, Double, Single, True, False, String, UInt32, Symbols, Skippable }

    // ---- write ----

    public static byte[] Write(JsonObject layout)
    {
        var w = new Writer();
        w.Top(layout);
        return w.Finish();
    }

    sealed class Writer
    {
        readonly Dictionary<string, int> strings = new(StringComparer.Ordinal);
        readonly List<string> list = new();
        readonly MemoryStream body = new();

        int Str(string s)
        {
            if (!strings.TryGetValue(s, out var i)) { strings[s] = i = list.Count; list.Add(s); }
            return i;
        }

        public void Top(JsonObject o)
        {
            Byte(Tag.Object);
            VarU((uint)o.Count);
            foreach (var (k, v) in o)
            {
                VarU((uint)Str(k));
                if (k == "symbols" && v != null) SymbolSet(LayoutSymbols.Of(v) ?? throw Bad(k));
                else if (k == "library" && v is JsonObject lib)
                {
                    // length-prefixed, so a read without the library skips it whole
                    var inner = new Writer();
                    inner.ShareStrings(this);
                    inner.Library(lib);
                    var bytes = inner.body.ToArray();
                    Byte(Tag.Skippable);
                    VarU((uint)bytes.Length);
                    body.Write(bytes);
                }
                else Node(v);
            }
        }

        Writer parent;
        void ShareStrings(Writer p) => parent = p;
        int S(string s) => parent != null ? parent.S(s) : Str(s);

        void Library(JsonObject lib)
        {
            Byte(Tag.Object);
            VarU((uint)lib.Count);
            foreach (var (k, entry) in lib)
            {
                VarU((uint)S(k));
                if (entry is not JsonObject eo) { Node(entry); continue; }
                Byte(Tag.Object);
                VarU((uint)eo.Count);
                foreach (var (ek, ev) in eo)
                {
                    VarU((uint)S(ek));
                    if (ek == "symbols" && ev != null) SymbolSet(LayoutSymbols.Of(ev) ?? throw Bad(ek));
                    else Node(ev);
                }
            }
        }

        static InvalidDataException Bad(string k) => new($"layout: \"{k}\" is not a symbol object");

        void SymbolSet(LayoutSymbols set)
        {
            var parts = new List<(int Key, byte[] Bytes)>();
            foreach (var (k, s) in set)
            {
                var one = new Writer();
                one.ShareStrings(this);
                one.Node(s);
                parts.Add((S(k), one.body.ToArray()));
            }
            Byte(Tag.Symbols);
            VarU((uint)parts.Count);
            foreach (var (key, bytes) in parts) { VarU((uint)key); VarU((uint)bytes.Length); }
            foreach (var (_, bytes) in parts) body.Write(bytes);
            var keyIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            int i = 0;
            foreach (var (k, _) in set) keyIndex.TryAdd(k, i++);
            var named = set.Named;
            VarU((uint)named.Count);
            foreach (var n in named)
            {
                VarU((uint)keyIndex[n.Id.ToString()]);
                VarU(n.Class == null ? 0u : (uint)S(n.Class) + 1);
                VarU(n.Export == null ? 0u : (uint)S(n.Export) + 1);
            }
        }

        void Node(JsonNode n)
        {
            switch (n)
            {
                case null: Byte(Tag.Null); return;
                case JsonObject o:
                    Byte(Tag.Object);
                    VarU((uint)o.Count);
                    foreach (var (k, v) in o) { VarU((uint)S(k)); Node(v); }
                    return;
                case JsonArray a:
                    Byte(Tag.Array);
                    VarU((uint)a.Count);
                    foreach (var v in a) Node(v);
                    return;
                case JsonValue v:
                    Value(v);
                    return;
            }
            throw new InvalidDataException("layout: unknown node " + n.GetType());
        }

        void Value(JsonValue v)
        {
            if (!v.TryGetValue<object>(out var x)) throw new InvalidDataException("layout: value without a CLR value");
            switch (x)
            {
                case int i: Byte(Tag.Int32); VarU(Zig(i)); return;
                case long l: Byte(Tag.Int64); VarU64(Zig64(l)); return;
                case double d: Byte(Tag.Double); Span<byte> b8 = stackalloc byte[8]; BinaryPrimitives.WriteDoubleLittleEndian(b8, d); body.Write(b8); return;
                case float f: Byte(Tag.Single); Span<byte> b4 = stackalloc byte[4]; BinaryPrimitives.WriteSingleLittleEndian(b4, f); body.Write(b4); return;
                case bool t: Byte(t ? Tag.True : Tag.False); return;
                case string s: Byte(Tag.String); VarU((uint)S(s)); return;
                case uint u: Byte(Tag.UInt32); VarU(u); return;
                case LayoutSymbols set: SymbolSet(set); return;
            }
            // anything else would come back as another type: refuse it rather than store it changed
            throw new InvalidDataException("layout: value of type " + x.GetType() + " is not stored");
        }

        void Byte(Tag t) => body.WriteByte((byte)t);
        void VarU(uint u) { while (u >= 0x80) { body.WriteByte((byte)(u | 0x80)); u >>= 7; } body.WriteByte((byte)u); }
        void VarU64(ulong u) { while (u >= 0x80) { body.WriteByte((byte)(u | 0x80)); u >>= 7; } body.WriteByte((byte)u); }
        static uint Zig(int i) => (uint)((i << 1) ^ (i >> 31));
        static ulong Zig64(long l) => (ulong)((l << 1) ^ (l >> 63));

        public byte[] Finish()
        {
            var head = new MemoryStream();
            Span<byte> b4 = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(b4, Magic);
            head.Write(b4);
            void H(uint u) { while (u >= 0x80) { head.WriteByte((byte)(u | 0x80)); u >>= 7; } head.WriteByte((byte)u); }
            H((uint)list.Count);
            foreach (var s in list)
            {
                var b = Encoding.UTF8.GetBytes(s);
                H((uint)b.Length);
                head.Write(b);
            }
            body.Position = 0;
            body.CopyTo(head);
            return head.ToArray();
        }
    }

    // ---- read ----

    /// <summary>The layout stored in <paramref name="bytes"/>; without its "library" when <paramref name="withLibrary"/> is false.</summary>
    public static JsonObject Read(byte[] bytes, bool withLibrary = true)
    {
        var r = new Reader(bytes);
        int p = r.BodyStart;
        var top = r.TopObject(ref p, withLibrary);
        if (p != bytes.Length) throw new InvalidDataException("layout: trailing bytes");
        return top;
    }

    internal sealed class Reader
    {
        readonly byte[] b;
        readonly int[] strStart, strLen;
        readonly string[] strs;
        public readonly int BodyStart;

        public Reader(byte[] bytes)
        {
            b = bytes;
            if (b.Length < 5 || BinaryPrimitives.ReadUInt32LittleEndian(b) != Magic) throw new InvalidDataException("layout: not a layout file");
            int p = 4;
            int n = (int)VarU(ref p);
            if (n < 0 || n > b.Length) throw new InvalidDataException("layout: bad string count");
            strStart = new int[n]; strLen = new int[n]; strs = new string[n];
            for (int i = 0; i < n; i++)
            {
                int len = (int)VarU(ref p);
                if (len < 0 || p + len > b.Length) throw new InvalidDataException("layout: bad string");
                strStart[i] = p; strLen[i] = len;
                p += len;
            }
            BodyStart = p;
        }

        string Str(uint i)
        {
            if (i >= strs.Length) throw new InvalidDataException("layout: bad string index");
            return strs[i] ??= Encoding.UTF8.GetString(b, strStart[i], strLen[i]);
        }

        uint VarU(ref int p)
        {
            uint u = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                if (p >= b.Length) throw new InvalidDataException("layout: short body");
                byte x = b[p++];
                u |= (uint)(x & 0x7F) << shift;
                if (x < 0x80) return u;
            }
            throw new InvalidDataException("layout: bad number");
        }

        ulong VarU64(ref int p)
        {
            ulong u = 0;
            for (int shift = 0; shift < 70; shift += 7)
            {
                if (p >= b.Length) throw new InvalidDataException("layout: short body");
                byte x = b[p++];
                u |= (ulong)(x & 0x7F) << shift;
                if (x < 0x80) return u;
            }
            throw new InvalidDataException("layout: bad number");
        }

        Tag TagAt(ref int p)
        {
            if (p >= b.Length) throw new InvalidDataException("layout: short body");
            return (Tag)b[p++];
        }

        int Count(ref int p)
        {
            var n = VarU(ref p);
            if (n > b.Length) throw new InvalidDataException("layout: bad count");
            return (int)n;
        }

        public JsonObject TopObject(ref int p, bool withLibrary)
        {
            if (TagAt(ref p) != Tag.Object) throw new InvalidDataException("layout: not an object");
            int n = Count(ref p);
            var o = new JsonObject();
            for (int i = 0; i < n; i++)
            {
                var k = Str(VarU(ref p));
                if (p < b.Length && b[p] == (byte)Tag.Skippable && k == "library")
                {
                    p++;
                    int len = Count(ref p);
                    int end = p + len;
                    if (end > b.Length) throw new InvalidDataException("layout: short library");
                    if (withLibrary) { o.Add(k, Node(ref p)); if (p != end) throw new InvalidDataException("layout: bad library length"); }
                    p = end;
                    continue;
                }
                o.Add(k, Node(ref p));
            }
            return o;
        }

        /// <summary>The symbol whose bytes start at <paramref name="start"/>.</summary>
        public JsonObject Symbol(int start)
        {
            int p = start;
            return Node(ref p) as JsonObject ?? throw new InvalidDataException("layout: a symbol is not an object");
        }

        JsonNode Node(ref int p)
        {
            switch (TagAt(ref p))
            {
                case Tag.Null: return null;
                case Tag.Object:
                {
                    int n = Count(ref p);
                    var o = new JsonObject();
                    for (int i = 0; i < n; i++) { var k = Str(VarU(ref p)); o.Add(k, Node(ref p)); }
                    return o;
                }
                case Tag.Array:
                {
                    int n = Count(ref p);
                    var items = new JsonNode[n];
                    for (int i = 0; i < n; i++) items[i] = Node(ref p);
                    return new JsonArray(items);
                }
                case Tag.Int32: { var z = VarU(ref p); return JsonValue.Create((int)(z >> 1) ^ -(int)(z & 1)); }
                case Tag.Int64: { var z = VarU64(ref p); return JsonValue.Create((long)(z >> 1) ^ -(long)(z & 1)); }
                case Tag.Double:
                    if (p + 8 > b.Length) throw new InvalidDataException("layout: short body");
                    p += 8;
                    return JsonValue.Create(BinaryPrimitives.ReadDoubleLittleEndian(b.AsSpan(p - 8)));
                case Tag.Single:
                    if (p + 4 > b.Length) throw new InvalidDataException("layout: short body");
                    p += 4;
                    return JsonValue.Create(BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(p - 4)));
                case Tag.True: return JsonValue.Create(true);
                case Tag.False: return JsonValue.Create(false);
                case Tag.String: return JsonValue.Create(Str(VarU(ref p)));
                case Tag.UInt32: return JsonValue.Create(VarU(ref p));
                case Tag.Symbols: return JsonValue.Create(Symbols(ref p));
            }
            throw new InvalidDataException("layout: bad tag");
        }

        LayoutSymbols Symbols(ref int p)
        {
            int n = Count(ref p);
            var keys = new string[n];
            var starts = new int[n];
            var lens = new int[n];
            for (int i = 0; i < n; i++) { keys[i] = Str(VarU(ref p)); lens[i] = Count(ref p); }
            for (int i = 0; i < n; i++)
            {
                starts[i] = p;
                p += lens[i];
                if (p > b.Length) throw new InvalidDataException("layout: short symbols");
            }
            int m = Count(ref p);
            var named = new LayoutSymbols.Name[m];
            for (int i = 0; i < m; i++)
            {
                int e = (int)VarU(ref p);
                if (e >= n) throw new InvalidDataException("layout: bad symbol index");
                uint c = VarU(ref p), x = VarU(ref p);
                named[i] = new LayoutSymbols.Name(int.Parse(keys[e]), c == 0 ? null : Str(c - 1), x == 0 ? null : Str(x - 1));
            }
            return new LayoutSymbols(this, keys, starts, named);
        }
    }

    // ---- copies and comparison ----

    /// <summary>
    /// A deep copy of a layout (built or stored) that keeps a stored layout's symbols stored (<see cref="LayoutSymbols.Clone"/>);
    /// JsonNode.DeepClone would decode them all.
    /// </summary>
    public static JsonNode Clone(JsonNode n)
    {
        switch (n)
        {
            case null: return null;
            case JsonObject o:
            {
                var c = new JsonObject();
                foreach (var (k, v) in o) c.Add(k, Clone(v));
                return c;
            }
            case JsonArray a:
            {
                var items = new JsonNode[a.Count];
                for (int i = 0; i < items.Length; i++) items[i] = Clone(a[i]);
                return new JsonArray(items);
            }
            case JsonValue v when v.TryGetValue<LayoutSymbols>(out var s): return JsonValue.Create(s.Clone());
            default: return n.DeepClone();
        }
    }

    /// <summary>The first difference of two layouts (a symbol set compared symbol by symbol, values by CLR type and
    /// value, properties in order), or null when they are the same.</summary>
    public static string Diff(JsonNode a, JsonNode b, string path = "")
    {
        var sa = Set(a); var sb = Set(b);
        if (sa != null || sb != null)
        {
            // a stored set against a built object: the object as a set
            sa ??= a is JsonObject ? LayoutSymbols.Of(a) : null;
            sb ??= b is JsonObject ? LayoutSymbols.Of(b) : null;
            if (sa == null || sb == null) return $"{path}: symbols vs {(sa == null ? Kind(a) : Kind(b))}";
            if (sa.Count != sb.Count) return $"{path}: {sa.Count} symbols vs {sb.Count}";
            using (var ea = sa.GetEnumerator())
            using (var eb = sb.GetEnumerator())
                while (ea.MoveNext() && eb.MoveNext())
                {
                    if (ea.Current.Key != eb.Current.Key) return $"{path}: symbol {ea.Current.Key} vs {eb.Current.Key}";
                    if (Diff(ea.Current.Value, eb.Current.Value, path + "/" + ea.Current.Key) is { } d) return d;
                }
            var na = sa.Named; var nb = sb.Named;
            if (na.Count != nb.Count) return $"{path}: {na.Count} named vs {nb.Count}";
            for (int i = 0; i < na.Count; i++) if (na[i] != nb[i]) return $"{path}: named {na[i]} vs {nb[i]}";
            return null;
        }
        switch (a)
        {
            case null: return b == null ? null : $"{path}: null vs {Kind(b)}";
            case JsonObject oa:
            {
                if (b is not JsonObject ob) return $"{path}: object vs {Kind(b)}";
                if (oa.Count != ob.Count) return $"{path}: {oa.Count} properties vs {ob.Count}";
                using var ea = oa.GetEnumerator();
                using var eb = ob.GetEnumerator();
                while (ea.MoveNext() && eb.MoveNext())
                {
                    if (ea.Current.Key != eb.Current.Key) return $"{path}: property {ea.Current.Key} vs {eb.Current.Key}";
                    if (Diff(ea.Current.Value, eb.Current.Value, path + "/" + ea.Current.Key) is { } d) return d;
                }
                return null;
            }
            case JsonArray aa:
            {
                if (b is not JsonArray ab) return $"{path}: array vs {Kind(b)}";
                if (aa.Count != ab.Count) return $"{path}: {aa.Count} items vs {ab.Count}";
                for (int i = 0; i < aa.Count; i++) if (Diff(aa[i], ab[i], $"{path}[{i}]") is { } d) return d;
                return null;
            }
            case JsonValue va:
            {
                if (b is not JsonValue vb) return $"{path}: value vs {Kind(b)}";
                va.TryGetValue<object>(out var xa); vb.TryGetValue<object>(out var xb);
                if (xa?.GetType() != xb?.GetType()) return $"{path}: {xa?.GetType().Name} vs {xb?.GetType().Name}";
                bool same = xa switch
                {
                    double da => BitConverter.DoubleToInt64Bits(da) == BitConverter.DoubleToInt64Bits((double)xb),
                    float fa => BitConverter.SingleToInt32Bits(fa) == BitConverter.SingleToInt32Bits((float)xb),
                    _ => Equals(xa, xb),
                };
                return same ? null : $"{path}: {xa} vs {xb}";
            }
        }
        return $"{path}: {Kind(a)}";
    }

    static LayoutSymbols Set(JsonNode n) => n is JsonValue v && v.TryGetValue<LayoutSymbols>(out var s) ? s : null;

    static string Kind(JsonNode n) => n switch { null => "null", JsonObject => "object", JsonArray => "array", _ => "value" };
}
