namespace Ms2.Ui.Hud;

/// <summary>
/// GetImageFileInfo's D3DXGetImageInfoFromFile for the upload folder's formats: the size from the file's header (the image
/// itself is not decoded), by content as D3DX does. PNG (IHDR), DDS (DDS_HEADER) and JPEG (the first SOF segment); null
/// for anything else or a short header.
/// </summary>
public static class UgcImageInfo
{
    public static (int Width, int Height)? Size(Stream s)
    {
        var head = new byte[32];
        int n = ReadUp(s, head, 0, head.Length);
        if (n >= 24 && head[0] == 0x89 && head[1] == (byte)'P' && head[2] == (byte)'N' && head[3] == (byte)'G'
            && head[12] == (byte)'I' && head[13] == (byte)'H' && head[14] == (byte)'D' && head[15] == (byte)'R')
            return Positive(BE32(head, 16), BE32(head, 20));
        if (n >= 20 && head[0] == (byte)'D' && head[1] == (byte)'D' && head[2] == (byte)'S' && head[3] == (byte)' ')
            return Positive(LE32(head, 16), LE32(head, 12));
        if (n >= 4 && head[0] == 0xff && head[1] == 0xd8) return Jpeg(s, head, n);
        return null;
    }

    static (int, int)? Jpeg(Stream s, byte[] head, int n)
    {
        // the bytes read so far, then the rest of the stream, walked marker by marker
        var rest = new MemoryStream();
        rest.Write(head, 2, n - 2);
        s.CopyTo(rest);
        var b = rest.GetBuffer();
        int len = (int)rest.Length, i = 0;
        while (i + 1 < len)
        {
            if (b[i] != 0xff) return null;
            byte m = b[i + 1];
            if (m == 0xff) { i++; continue; }                                   // fill bytes
            if (m == 0x01 || m >= 0xd0 && m <= 0xd7) { i += 2; continue; }      // no length
            if (m == 0xd9 || m == 0xda || i + 3 >= len) return null;            // end or scan before any frame
            int seg = b[i + 2] << 8 | b[i + 3];
            bool sof = m >= 0xc0 && m <= 0xcf && m != 0xc4 && m != 0xc8 && m != 0xcc;
            if (sof)
            {
                if (i + 8 >= len) return null;
                return Positive(b[i + 7] << 8 | b[i + 8], b[i + 5] << 8 | b[i + 6]);
            }
            i += 2 + seg;
        }
        return null;
    }

    static (int, int)? Positive(long w, long h) => w > 0 && h > 0 && w <= int.MaxValue && h <= int.MaxValue ? ((int)w, (int)h) : null;
    static long BE32(byte[] b, int o) => (long)b[o] << 24 | (long)b[o + 1] << 16 | (long)b[o + 2] << 8 | b[o + 3];
    static long LE32(byte[] b, int o) => b[o] | (long)b[o + 1] << 8 | (long)b[o + 2] << 16 | (long)b[o + 3] << 24;

    static int ReadUp(Stream s, byte[] buf, int off, int count)
    {
        int total = 0;
        while (total < count)
        {
            int r = s.Read(buf, off + total, count - total);
            if (r <= 0) break;
            total += r;
        }
        return total;
    }
}
