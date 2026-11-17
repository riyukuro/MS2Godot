using System.Buffers.Binary;
using System.Text;

namespace Ms2.Video.Usm;

/// <summary>
/// A CRI @UTF table (the header tables inside a USM): named, typed columns over a number of rows. Big-endian.
/// Layout: "@UTF", size; then from the base (offset 8) u16 version, u16 rows offset, u32 strings offset, u32 data
/// offset, u32 table name, u16 column count, u16 row width, u32 row count, the column list, the rows, the strings and
/// the data blobs (all offsets from the base).
/// </summary>
public sealed class UtfTable
{
    const int StorageMask = 0xf0, StorageZero = 0x10, StorageConstant = 0x30, StorageRow = 0x50;
    const int TypeMask = 0x0f;

    public string Name { get; private set; }
    public int RowCount => rows.Count;
    public IReadOnlyList<string> Columns => columns;

    readonly List<string> columns = new();
    readonly List<Dictionary<string, object>> rows = new();

    /// <summary>The value of <paramref name="column"/> in row <paramref name="row"/>, or null.</summary>
    public object this[int row, string column] => row >= 0 && row < rows.Count && rows[row].TryGetValue(column, out var v) ? v : null;

    /// <summary>A numeric column as long (0 if missing or not a number).</summary>
    public long Long(int row, string column) => this[row, column] switch
    {
        byte b => b, sbyte sb => sb, ushort us => us, short s => s, uint ui => ui, int i => i,
        ulong ul => (long)ul, long l => l, float f => (long)f, double d => (long)d, _ => 0,
    };

    public string String(int row, string column) => this[row, column] as string;

    /// <summary>Parses the table that starts with "@UTF" at <paramref name="offset"/>.</summary>
    public static UtfTable Read(ReadOnlySpan<byte> data, int offset = 0)
    {
        var src = data[offset..];
        if (src.Length < 32 || src[0] != '@' || src[1] != 'U' || src[2] != 'T' || src[3] != 'F')
            throw new InvalidDataException("not a @UTF table");
        int size = (int)U32(src, 4);
        var t = src.Slice(8, Math.Min(size, src.Length - 8));
        int rowsOffset = U16(t, 2);
        int stringsOffset = (int)U32(t, 4), dataOffset = (int)U32(t, 8), nameOffset = (int)U32(t, 12);
        int columnCount = U16(t, 16), rowWidth = U16(t, 18);
        int rowCount = (int)U32(t, 20);

        var table = new UtfTable { Name = Str(t, stringsOffset, nameOffset) };
        var cols = new List<(int Flags, string Name, object Constant)>();
        int p = 24;
        for (int c = 0; c < columnCount; c++)
        {
            int flags = t[p++];
            string name = Str(t, stringsOffset, (int)U32(t, p)); p += 4;
            object constant = null;
            if ((flags & StorageMask) == StorageConstant) constant = Value(t, ref p, flags & TypeMask, stringsOffset, dataOffset);
            else if ((flags & StorageMask) == StorageZero) constant = Zero(flags & TypeMask);
            cols.Add((flags, name, constant));
            table.columns.Add(name);
        }
        for (int r = 0; r < rowCount; r++)
        {
            var row = new Dictionary<string, object>();
            int q = rowsOffset + r * rowWidth;
            foreach (var (flags, name, constant) in cols)
                row[name] = (flags & StorageMask) == StorageRow ? Value(t, ref q, flags & TypeMask, stringsOffset, dataOffset) : constant;
            table.rows.Add(row);
        }
        return table;
    }

    static object Value(ReadOnlySpan<byte> t, ref int p, int type, int strings, int data)
    {
        object v;
        switch (type)
        {
            case 0: v = t[p]; p += 1; break;
            case 1: v = (sbyte)t[p]; p += 1; break;
            case 2: v = (ushort)U16(t, p); p += 2; break;
            case 3: v = (short)U16(t, p); p += 2; break;
            case 4: v = U32(t, p); p += 4; break;
            case 5: v = (int)U32(t, p); p += 4; break;
            case 6: v = BinaryPrimitives.ReadUInt64BigEndian(t[p..]); p += 8; break;
            case 7: v = BinaryPrimitives.ReadInt64BigEndian(t[p..]); p += 8; break;
            case 8: v = BinaryPrimitives.ReadSingleBigEndian(t[p..]); p += 4; break;
            case 9: v = BinaryPrimitives.ReadDoubleBigEndian(t[p..]); p += 8; break;
            case 0xa: v = Str(t, strings, (int)U32(t, p)); p += 4; break;
            case 0xb:
                int off = (int)U32(t, p), len = (int)U32(t, p + 4);
                p += 8;
                v = data + off + len <= t.Length ? t.Slice(data + off, len).ToArray() : Array.Empty<byte>();
                break;
            default: throw new InvalidDataException($"@UTF column type {type:x}");
        }
        return v;
    }

    static object Zero(int type) => type switch
    {
        0 => (byte)0, 1 => (sbyte)0, 2 => (ushort)0, 3 => (short)0, 4 => 0u, 5 => 0, 6 => 0ul, 7 => 0L,
        8 => 0f, 9 => 0d, 0xa => null, _ => null,
    };

    static string Str(ReadOnlySpan<byte> t, int strings, int offset)
    {
        int start = strings + offset;
        if (start < 0 || start >= t.Length) return null;
        int end = t[start..].IndexOf((byte)0);
        return Encoding.UTF8.GetString(end < 0 ? t[start..] : t.Slice(start, end));
    }

    static int U16(ReadOnlySpan<byte> s, int p) => BinaryPrimitives.ReadUInt16BigEndian(s[p..]);
    static uint U32(ReadOnlySpan<byte> s, int p) => BinaryPrimitives.ReadUInt32BigEndian(s[p..]);
}
