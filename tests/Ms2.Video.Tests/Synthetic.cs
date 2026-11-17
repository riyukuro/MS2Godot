using System.Buffers.Binary;
using System.Text;

namespace Ms2.Video.Tests;

/// <summary>Hand-built MPEG-1 streams, @UTF tables and USM files for the tests.</summary>
static class Synthetic
{
    public sealed class BitWriter
    {
        readonly List<byte> bytes = new();
        int acc, n;

        public BitWriter Bits(int value, int count)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                acc = (acc << 1) | ((value >> i) & 1);
                if (++n == 8) { bytes.Add((byte)acc); acc = 0; n = 0; }
            }
            return this;
        }

        public BitWriter Code(string bits)
        {
            foreach (var c in bits) Bits(c == '1' ? 1 : 0, 1);
            return this;
        }

        public BitWriter Align() { while (n != 0) Bits(0, 1); return this; }

        public BitWriter StartCode(int code) { Align(); bytes.AddRange(new byte[] { 0, 0, 1, (byte)code }); return this; }

        public BitWriter Raw(byte[] b) { Align(); bytes.AddRange(b); return this; }

        public byte[] ToArray() { Align(); return bytes.ToArray(); }
    }

    // dct_dc_size_luminance / chrominance codes for sizes 0-11 (MPEG-1 to 8, MPEG-2 Tables B-12 / B-13 above)
    static readonly string[] LumaSize = { "100", "00", "01", "101", "110", "1110", "11110", "111110", "1111110", "11111110", "111111110", "111111111" };
    static readonly string[] ChromaSize = { "00", "01", "10", "110", "1110", "11110", "111110", "1111110", "11111110", "111111110", "1111111110", "1111111111" };

    static void Dc(BitWriter w, int diff, bool chroma)
    {
        int size = 0;
        for (int a = Math.Abs(diff); a > 0; a >>= 1) size++;
        w.Code((chroma ? ChromaSize : LumaSize)[size]);
        if (size > 0) w.Bits(diff > 0 ? diff : diff + (1 << size) - 1, size);
        w.Code("10");   // end_of_block
    }

    /// <summary>A flat colour per picture: Y, Cb, Cr.</summary>
    public record Picture(int Y, int Cb, int Cr, bool Predicted = false);

    /// <summary>
    /// A 16 x 16 stream (one macroblock): every picture is an intra picture of one flat colour, or a P picture that copies
    /// its reference unchanged. <paramref name="dcPrecision"/> above 8 adds TMPGEnc's IDCPREC user data.
    /// </summary>
    public static byte[] MakeMpeg1(int rateCode, int dcPrecision, params Picture[] pictures)
    {
        var w = new BitWriter();
        w.StartCode(0xB3).Bits(16, 12).Bits(16, 12).Bits(1, 4).Bits(rateCode, 4)
            .Bits(0x3ffff, 18).Bits(1, 1).Bits(1, 10).Bits(0, 1).Bits(0, 1).Bits(0, 1);
        if (dcPrecision > 8)
            w.StartCode(0xB2).Raw(Encoding.ASCII.GetBytes("TMPGEXS\0" + "00000028" + "IDCPREC\0" + "00000008" + $"{dcPrecision - 8:X8}" + "TMPGEXE\0" + "00000000"));
        int p = dcPrecision - 8, reset = 128 << p, scale = 1 << p;   // DC units: 1 / 2^p of a pixel step
        for (int i = 0; i < pictures.Length; i++)
        {
            var pic = pictures[i];
            w.StartCode(0x00).Bits(i, 10).Bits(pic.Predicted ? 2 : 1, 3).Bits(0xffff, 16);
            if (pic.Predicted) w.Bits(0, 1).Bits(1, 3);       // full_pel_forward, forward_f_code
            w.Bits(0, 1);                                     // extra_bit_picture
            w.StartCode(0x01).Bits(1, 5).Bits(0, 1);          // slice 1: quantizer_scale, extra_bit_slice
            w.Code("1");                                      // macroblock_address_increment 1
            if (pic.Predicted)
            {
                w.Code("001");                                // P: forward motion, not coded
                w.Code("1").Code("1");                        // motion codes 0, 0
                continue;
            }
            w.Code("1");                                      // I: intra
            Dc(w, pic.Y * scale - reset, false);
            for (int b = 1; b < 4; b++) Dc(w, 0, false);
            Dc(w, pic.Cb * scale - reset, true);
            Dc(w, pic.Cr * scale - reset, true);
        }
        w.StartCode(0xB7);
        w.Raw(new byte[256]);   // the sequence header read wants room for both matrices
        return w.ToArray();
    }

    // ---- @UTF ----

    public sealed class Column
    {
        public string Name;
        public int Type;               // 0 u8 .. 0xa string, 0xb data
        public bool Constant, Zero;
        public object[] Values;        // one per row (or [0] for a constant)
    }

    public static byte[] Utf(string tableName, int rows, params Column[] cols)
    {
        var strings = new MemoryStream();
        var stringIndex = new Dictionary<string, int>();
        int Str(string s)
        {
            if (stringIndex.TryGetValue(s, out int at)) return at;
            at = (int)strings.Length;
            var b = Encoding.ASCII.GetBytes(s + "\0");
            strings.Write(b);
            stringIndex[s] = at;
            return at;
        }
        Str("<NULL>");
        int name = Str(tableName);
        var data = new MemoryStream();

        byte[] Value(int type, object v)
        {
            var b = new byte[type switch { 0 or 1 => 1, 2 or 3 => 2, 4 or 5 or 8 => 4, 6 or 7 or 9 or 0xb => 8, _ => 4 }];
            switch (type)
            {
                case 0: case 1: b[0] = Convert.ToByte(v); break;
                case 2: case 3: BinaryPrimitives.WriteUInt16BigEndian(b, (ushort)Convert.ToInt32(v)); break;
                case 4: case 5: BinaryPrimitives.WriteUInt32BigEndian(b, (uint)Convert.ToInt64(v)); break;
                case 6: case 7: BinaryPrimitives.WriteUInt64BigEndian(b, (ulong)Convert.ToInt64(v)); break;
                case 8: BinaryPrimitives.WriteSingleBigEndian(b, Convert.ToSingle(v)); break;
                case 9: BinaryPrimitives.WriteDoubleBigEndian(b, Convert.ToDouble(v)); break;
                case 0xa: BinaryPrimitives.WriteUInt32BigEndian(b, (uint)Str((string)v)); break;
                case 0xb:
                    var blob = (byte[])v;
                    BinaryPrimitives.WriteUInt32BigEndian(b, (uint)data.Length);
                    BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(4), (uint)blob.Length);
                    data.Write(blob);
                    break;
            }
            return b;
        }

        var colBytes = new MemoryStream();
        foreach (var c in cols)
        {
            int storage = c.Zero ? 0x10 : c.Constant ? 0x30 : 0x50;
            colBytes.WriteByte((byte)(storage | c.Type));
            var nb = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(nb, (uint)Str(c.Name));
            colBytes.Write(nb);
            if (c.Constant) colBytes.Write(Value(c.Type, c.Values[0]));
        }
        var rowBytes = new MemoryStream();
        int rowWidth = 0;
        for (int r = 0; r < rows; r++)
        {
            long before = rowBytes.Length;
            foreach (var c in cols.Where(c => !c.Constant && !c.Zero)) rowBytes.Write(Value(c.Type, c.Values[r]));
            rowWidth = (int)(rowBytes.Length - before);
        }
        int rowsOffset = 24 + (int)colBytes.Length;
        int stringsOffset = rowsOffset + (int)rowBytes.Length;
        int dataOffset = stringsOffset + (int)strings.Length;
        int size = dataOffset + (int)data.Length;
        var t = new byte[8 + size];
        "@UTF"u8.CopyTo(t);
        BinaryPrimitives.WriteUInt32BigEndian(t.AsSpan(4), (uint)size);
        var s = t.AsSpan(8);
        BinaryPrimitives.WriteUInt16BigEndian(s[2..], (ushort)rowsOffset);
        BinaryPrimitives.WriteUInt32BigEndian(s[4..], (uint)stringsOffset);
        BinaryPrimitives.WriteUInt32BigEndian(s[8..], (uint)dataOffset);
        BinaryPrimitives.WriteUInt32BigEndian(s[12..], (uint)name);
        BinaryPrimitives.WriteUInt16BigEndian(s[16..], (ushort)cols.Length);
        BinaryPrimitives.WriteUInt16BigEndian(s[18..], (ushort)rowWidth);
        BinaryPrimitives.WriteUInt32BigEndian(s[20..], (uint)rows);
        colBytes.ToArray().CopyTo(s[24..]);
        rowBytes.ToArray().CopyTo(s[rowsOffset..]);
        strings.ToArray().CopyTo(s[stringsOffset..]);
        data.ToArray().CopyTo(s[dataOffset..]);
        return t;
    }

    // ---- USM ----

    public static byte[] Chunk(string sig, int type, int channel, byte[] payload, int padding = 0)
    {
        var c = new byte[0x20 + payload.Length + padding];
        Encoding.ASCII.GetBytes(sig).CopyTo(c, 0);
        BinaryPrimitives.WriteUInt32BigEndian(c.AsSpan(4), (uint)(c.Length - 8));
        c[9] = 0x18;
        BinaryPrimitives.WriteUInt16BigEndian(c.AsSpan(10), (ushort)padding);
        c[12] = (byte)channel;
        c[15] = (byte)type;
        payload.CopyTo(c, 0x20);
        for (int i = 0; i < padding; i++) c[0x20 + payload.Length + i] = 0xee;   // junk the reader must drop
        return c;
    }

    public static byte[] VideoHeader(int width, int height, int fpsN, int fpsD, int frames, int dcPrecision) =>
        Utf("VIDEO_HDRINFO", 1,
            new Column { Name = "width", Type = 4, Values = new object[] { width } },
            new Column { Name = "height", Type = 4, Values = new object[] { height } },
            new Column { Name = "disp_width", Type = 4, Values = new object[] { width } },
            new Column { Name = "disp_height", Type = 4, Values = new object[] { height } },
            new Column { Name = "mpeg_dcprec", Type = 0, Values = new object[] { dcPrecision } },
            new Column { Name = "mpeg_codec", Type = 0, Constant = true, Values = new object[] { 1 } },
            new Column { Name = "total_frames", Type = 4, Values = new object[] { frames } },
            new Column { Name = "framerate_n", Type = 4, Values = new object[] { fpsN } },
            new Column { Name = "framerate_d", Type = 4, Values = new object[] { fpsD } });

    /// <summary>A USM with the given video stream cut into chunks of <paramref name="chunk"/> bytes (each padded).</summary>
    public static byte[] MakeUsm(byte[] video, byte[] videoHeader, int chunk = 37, byte[] audio = null)
    {
        var o = new MemoryStream();
        var dir = Utf("CRIUSF_DIR_STREAM", 2,
            new Column { Name = "filename", Type = 0xa, Values = new object[] { "test.usm", "test.avi" } },
            new Column { Name = "filesize", Type = 4, Values = new object[] { 0, video.Length } },
            new Column { Name = "stmid", Type = 4, Values = new object[] { 0, 0x40534656 } },
            new Column { Name = "chno", Type = 2, Values = new object[] { 0xffff, 0 } });
        o.Write(Chunk("CRID", 1, 0, dir));
        o.Write(Chunk("@SFV", 1, 0, videoHeader));
        if (audio != null) o.Write(Chunk("@SFA", 1, 0, Utf("AUDIO_HDRINFO", 1, new Column { Name = "sampling_rate", Type = 4, Values = new object[] { 48000 } })));
        o.Write(Chunk("@SFV", 2, 0, Encoding.ASCII.GetBytes("#HEADER END     ===============\0")));
        for (int i = 0; i < video.Length; i += chunk)
        {
            o.Write(Chunk("@SFV", 0, 0, video.AsSpan(i, Math.Min(chunk, video.Length - i)).ToArray(), padding: i % 3));
            if (audio != null && i == 0) o.Write(Chunk("@SFA", 0, 0, audio));
        }
        o.Write(Chunk("@SFV", 2, 0, Encoding.ASCII.GetBytes("#CONTENTS END   ===============\0")));
        o.Write(Chunk("@ALP", 0, 0, new byte[4]));
        return o.ToArray();
    }
}
