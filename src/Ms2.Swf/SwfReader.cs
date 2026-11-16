using System.Text;

namespace Ms2.Swf;

/// <summary>Little-endian byte and bit reader over one tag body (or the whole movie body).</summary>
public sealed class SwfReader
{
    readonly byte[] buf;
    int pos;
    int bitPos;           // bits already consumed in buf[pos], 0 = byte aligned
    public int End { get; }

    public SwfReader(byte[] data, int start, int end)
    {
        buf = data; pos = start; End = end;
    }

    public byte[] Buffer => buf;
    public int Position => pos;
    public int Remaining => End - pos;
    public bool AtEnd => pos >= End;

    void Need(int n)
    {
        if (pos + n > End) throw new EndOfStreamException($"need {n} bytes at {pos}, end {End}");
    }

    public void Align() { if (bitPos != 0) { bitPos = 0; pos++; } }

    public void Skip(int n) { Align(); Need(n); pos += n; }

    public void Seek(int p) { pos = p; bitPos = 0; }

    public byte U8() { Align(); Need(1); return buf[pos++]; }
    public ushort U16() { Align(); Need(2); var v = (ushort)(buf[pos] | buf[pos + 1] << 8); pos += 2; return v; }
    public short S16() => (short)U16();
    public uint U32() { Align(); Need(4); var v = BitConverter.ToUInt32(buf, pos); pos += 4; return v; }
    public int S32() => (int)U32();

    /// <summary>8.8 fixed point.</summary>
    public double Fixed8() => S16() / 256.0;
    /// <summary>16.16 fixed point.</summary>
    public double Fixed16() => S32() / 65536.0;

    public byte[] Bytes(int n) { Align(); Need(n); var r = new byte[n]; Array.Copy(buf, pos, r, 0, n); pos += n; return r; }

    /// <summary>Zero-terminated UTF-8 string.</summary>
    public string CString()
    {
        Align();
        int s = pos;
        while (pos < End && buf[pos] != 0) pos++;
        if (pos >= End) throw new EndOfStreamException("unterminated string");
        var r = Encoding.UTF8.GetString(buf, s, pos - s);
        pos++;
        return r;
    }

    /// <summary>Length-prefixed (u8) string, used by the GFx extension tags.</summary>
    public string String8()
    {
        int n = U8();
        return Encoding.UTF8.GetString(Bytes(n));
    }

    public uint UB(int n)
    {
        uint v = 0;
        for (int i = 0; i < n; i++)
        {
            Need(1);
            v = v << 1 | (uint)((buf[pos] >> (7 - bitPos)) & 1);
            if (++bitPos == 8) { bitPos = 0; pos++; }
        }
        return v;
    }

    public int SB(int n)
    {
        if (n == 0) return 0;
        uint v = UB(n);
        if ((v & 1u << (n - 1)) != 0) return (int)(v | ~0u << n);
        return (int)v;
    }

    public double FB(int n) => SB(n) / 65536.0;
    public bool Bit() => UB(1) != 0;
}
