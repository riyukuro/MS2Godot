using System.Buffers.Binary;
using System.Text;

namespace Ms2.Formats.Nif;

/// <summary>A whole NIF / KF / KFM-free Gamebryo object file: header, decoded blocks, roots.</summary>
public sealed class NifFile
{
    public string HeaderLine;
    public uint Version, UserVersion;
    public List<string> Types = new();
    public List<NifStruct> Blocks = new();
    public List<string> Strings = new();
    public List<int> Roots = new();
    /// <summary>The file bytes and where each block lies in them (for diagnostics).</summary>
    public byte[] Raw; public List<(int start, int len)> BlockSpan = new();

    static readonly Encoding Latin1 = Encoding.Latin1;

    public static NifFile Load(NifSpec spec, byte[] b)
    {
        var n = new NifFile { Raw = b };
        int nl = Array.IndexOf(b, (byte)'\n');
        n.HeaderLine = Latin1.GetString(b, 0, nl);
        int o = nl + 1;
        uint U32() { var v = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o)); o += 4; return v; }
        ushort U16() { var v = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o)); o += 2; return v; }
        byte[] Sized() { int len = (int)U32(); var s = b.AsSpan(o, len).ToArray(); o += len; return s; }

        n.Version = U32();
        o += 1;                                      // endian
        n.UserVersion = U32();
        int nb = (int)U32();
        Sized();                                     // metadata
        int nt = U16();
        for (int i = 0; i < nt; i++) n.Types.Add(Latin1.GetString(Sized()));
        var idx = new ushort[nb];
        for (int i = 0; i < nb; i++) idx[i] = U16();
        var sizes = new uint[nb];
        for (int i = 0; i < nb; i++) sizes[i] = U32();
        int ns = (int)U32();
        U32();                                       // max string length
        for (int i = 0; i < ns; i++) n.Strings.Add(Latin1.GetString(Sized()));
        int ng = (int)U32();
        o += 4 * ng;
        for (int i = 0; i < nb; i++)
        {
            var raw = n.Types[idx[i] & 0x7fff];
            var t = raw.Split('\x01')[0];
            var r = new NifReader(spec, b, o);
            var blk = r.Struct(t, null, null, t);
            if (r.Pos != o + sizes[i])
                throw new InvalidDataException($"block {i} {t}: read {r.Pos - o} of {sizes[i]} bytes");
            n.Blocks.Add(blk);
            n.BlockSpan.Add((o, (int)sizes[i]));
            o += (int)sizes[i];
        }
        int nr = (int)U32();
        for (int i = 0; i < nr; i++) { n.Roots.Add((int)U32()); }
        return n;
    }

    public string Str(long i) => i < 0 || i == 0xFFFFFFFF || i >= Strings.Count ? null : Strings[(int)i];

    /// <summary>Reads a string-ish field: an index, or a struct with an Index field.</summary>
    public string StrField(NifStruct s, string field)
    {
        if (!s.TryGetValue(field, out var v)) return null;
        return v switch
        {
            long l => Str(l),
            NifStruct st when st.TryGetValue("Index", out var ix) && ix is long li => Str(li),
            _ => null,
        };
    }

    public string Name(int block) => StrField(Blocks[block], "Name");

    Dictionary<int, string> nodeNames;
    /// <summary>
    /// A node's name, unique in the file: a name more than one NiNode carries (an effect creature's mirrored leg bones
    /// named alike) keeps it on its first block and gets "#" + block index on the others. A controller is tied to its
    /// node block, not to the name, so both sides of a model (its bones, its controllers) use this.
    /// </summary>
    public string NodeName(int block)
    {
        if (nodeNames == null)
        {
            nodeNames = new Dictionary<int, string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Blocks.Count; i++)
            {
                if (Blocks[i].Type != "NiNode") continue;
                var n = Name(i);
                if (n != null && !seen.Add(n)) nodeNames[i] = n + "#" + i;
            }
        }
        return nodeNames.TryGetValue(block, out var u) ? u : Name(block);
    }
}
