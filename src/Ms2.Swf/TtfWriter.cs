using System.Text;

namespace Ms2.Swf;

/// <summary>Writes a TrueType font (quadratic outlines, BMP cmap) from SWF glyph outlines, so a UI toolkit can load the game's own font.</summary>
public static class TtfWriter
{
    const int Units = 2048;

    public static byte[] Build(FontData f)
    {
        double k = (double)Units / f.EmSquare;
        int n = f.Glyphs.Count + 1;                       // glyph 0 = .notdef
        var glyf = new MemoryStream(); var loca = new List<int> { 0 };
        var adv = new int[n]; var lsb = new short[n];
        int xMin = int.MaxValue, yMin = int.MaxValue, xMax = int.MinValue, yMax = int.MinValue, maxPts = 0, maxCont = 0, advMax = 0;
        loca.Add(0);   // .notdef is empty
        adv[0] = Units / 2;
        for (int i = 0; i < f.Glyphs.Count; i++)
        {
            var g = f.Glyphs[i];
            adv[i + 1] = Math.Max(0, (int)Math.Round(g.Advance * k));
            advMax = Math.Max(advMax, adv[i + 1]);
            var contours = g.Contours.Select(c => c.Select(p => ((int)Math.Round(p.X * k), (int)Math.Round(-p.Y * k), p.On)).ToList()).ToList();
            if (contours.Count == 0) { loca.Add((int)glyf.Length); continue; }
            int gx0 = contours.Min(c => c.Min(p => p.Item1)), gx1 = contours.Max(c => c.Max(p => p.Item1));
            int gy0 = contours.Min(c => c.Min(p => p.Item2)), gy1 = contours.Max(c => c.Max(p => p.Item2));
            lsb[i + 1] = (short)gx0;
            xMin = Math.Min(xMin, gx0); yMin = Math.Min(yMin, gy0); xMax = Math.Max(xMax, gx1); yMax = Math.Max(yMax, gy1);
            int pts = contours.Sum(c => c.Count); maxPts = Math.Max(maxPts, pts); maxCont = Math.Max(maxCont, contours.Count);
            var w = new BE(glyf);
            w.S16(contours.Count); w.S16(gx0); w.S16(gy0); w.S16(gx1); w.S16(gy1);
            int end = -1;
            foreach (var c in contours) { end += c.Count; w.U16(end); }
            w.U16(0);                                          // no instructions
            var all = contours.SelectMany(c => c).ToList();
            foreach (var p in all) w.U8(p.Item3 ? 1 : 0);     // flags: on curve, 16-bit deltas
            int px = 0; foreach (var p in all) { w.S16(p.Item1 - px); px = p.Item1; }
            int py = 0; foreach (var p in all) { w.S16(p.Item2 - py); py = p.Item2; }
            while (glyf.Length % 4 != 0) glyf.WriteByte(0);
            loca.Add((int)glyf.Length);
        }
        if (xMin == int.MaxValue) { xMin = yMin = 0; xMax = yMax = Units; }
        int ascent = f.HasLayout && f.Ascent > 0 ? (int)Math.Round(f.Ascent * k) : (int)(Units * 0.88);
        int descent = f.HasLayout && f.Descent > 0 ? (int)Math.Round(f.Descent * k) : (int)(Units * 0.12);
        int lineGap = Math.Max(0, (int)Math.Round(f.Leading * k));

        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        // head
        { var m = new MemoryStream(); var w = new BE(m);
          w.U32(0x00010000); w.U32(0x00010000); w.U32(0); w.U32(0x5F0F3CF5); w.U16(0x000B); w.U16(Units);
          w.U32(0); w.U32(0); w.U32(0); w.U32(0);                              // created, modified
          w.S16(xMin); w.S16(yMin); w.S16(xMax); w.S16(yMax);
          w.U16((f.Bold ? 1 : 0) | (f.Italic ? 2 : 0)); w.U16(8); w.S16(2); w.S16(1); w.S16(0);
          tables["head"] = m.ToArray(); }
        // hhea
        { var m = new MemoryStream(); var w = new BE(m);
          w.U32(0x00010000); w.S16(ascent); w.S16(-descent); w.S16(lineGap); w.U16(advMax); w.S16(0); w.S16(0); w.S16(xMax);
          w.S16(1); w.S16(0); w.S16(0); w.U32(0); w.U32(0); w.S16(0); w.U16(n);
          tables["hhea"] = m.ToArray(); }
        // maxp
        { var m = new MemoryStream(); var w = new BE(m);
          w.U32(0x00010000); w.U16(n); w.U16(maxPts); w.U16(maxCont); w.U16(0); w.U16(0); w.U16(2);
          for (int i = 0; i < 8; i++) w.U16(0);
          tables["maxp"] = m.ToArray(); }
        // hmtx
        { var m = new MemoryStream(); var w = new BE(m); for (int i = 0; i < n; i++) { w.U16(adv[i]); w.S16(lsb[i]); } tables["hmtx"] = m.ToArray(); }
        // loca (long) + glyf
        { var m = new MemoryStream(); var w = new BE(m); foreach (var o in loca) w.U32((uint)o); tables["loca"] = m.ToArray(); }
        tables["glyf"] = glyf.ToArray();
        // cmap format 4
        { var pairs = f.Glyphs.Select((g, i) => (Code: (int)g.Code, Gid: i + 1)).Where(p => p.Code != 0 && p.Code < 0xFFFF).GroupBy(p => p.Code).Select(g => g.First()).OrderBy(p => p.Code).ToList();
          var segs = new List<(int Start, int End, int Delta)>();
          foreach (var p in pairs)
          {
              if (segs.Count > 0 && segs[^1].End + 1 == p.Code && ((p.Gid - p.Code) & 0xFFFF) == segs[^1].Delta) segs[^1] = (segs[^1].Start, p.Code, segs[^1].Delta);
              else segs.Add((p.Code, p.Code, (p.Gid - p.Code) & 0xFFFF));
          }
          segs.Add((0xFFFF, 0xFFFF, 1));
          int sc = segs.Count; int sr = 2 * (1 << (int)Math.Floor(Math.Log2(sc)));
          var m = new MemoryStream(); var w = new BE(m);
          w.U16(0); w.U16(1); w.U16(3); w.U16(1); w.U32(12);
          w.U16(4); w.U16(16 + sc * 8); w.U16(0); w.U16(sc * 2); w.U16(sr); w.U16((int)Math.Log2(sr / 2)); w.U16(sc * 2 - sr);
          foreach (var s in segs) w.U16(s.End); w.U16(0);
          foreach (var s in segs) w.U16(s.Start);
          foreach (var s in segs) w.U16(s.Delta);
          foreach (var s in segs) w.U16(0);
          tables["cmap"] = m.ToArray(); }
        // name
        { var strs = new[] { (1, f.Name), (2, f.Bold ? "Bold" : "Regular"), (4, f.Name), (6, f.Name.Replace(' ', '-')) };
          var m = new MemoryStream(); var w = new BE(m);
          w.U16(0); w.U16(strs.Length); w.U16(6 + strs.Length * 12);
          int off = 0; var data = new MemoryStream();
          foreach (var (id, str) in strs) { var b = Encoding.BigEndianUnicode.GetBytes(str); w.U16(3); w.U16(1); w.U16(0x409); w.U16(id); w.U16(b.Length); w.U16(off); data.Write(b); off += b.Length; }
          data.WriteTo(m); tables["name"] = m.ToArray(); }
        // post 3.0
        { var m = new MemoryStream(); var w = new BE(m); w.U32(0x00030000); for (int i = 0; i < 7; i++) w.U32(0); tables["post"] = m.ToArray(); }
        // OS/2 (version 1)
        { var m = new MemoryStream(); var w = new BE(m);
          w.U16(1); w.S16(adv.Skip(1).DefaultIfEmpty(Units / 2).Sum() / Math.Max(1, n - 1)); w.U16(f.Bold ? 700 : 400); w.U16(5); w.U16(0);
          for (int i = 0; i < 10; i++) w.S16(0);                       // subscript/superscript/strikeout metrics + family class
          for (int i = 0; i < 10; i++) w.U8(0);                          // panose
          w.U32(0); w.U32(0); w.U32(0); w.U32(0);                        // unicode ranges
          w.U32(0x20202020); w.U16(f.Bold ? 0x20 : 0x40); w.U16(32); w.U16(0xFFFD);
          w.S16(ascent); w.S16(-descent); w.S16(lineGap); w.U16(ascent); w.U16(descent);
          w.U32(1); w.U32(0);
          tables["OS/2"] = m.ToArray(); }

        // assemble
        var outp = new MemoryStream(); var hdr = new BE(outp);
        int tc = tables.Count; int sel = (int)Math.Floor(Math.Log2(tc)); int range = (1 << sel) * 16;
        hdr.U32(0x00010000); hdr.U16(tc); hdr.U16(range); hdr.U16(sel); hdr.U16(tc * 16 - range);
        int offset = 12 + tc * 16;
        var body = new MemoryStream();
        foreach (var (tag, data) in tables)
        {
            var padded = data.Concat(new byte[(4 - data.Length % 4) % 4]).ToArray();
            hdr.Tag(tag); hdr.U32(Checksum(padded)); hdr.U32((uint)(offset + body.Length)); hdr.U32((uint)data.Length);
            body.Write(padded);
        }
        body.WriteTo(outp);
        return outp.ToArray();
    }

    static uint Checksum(byte[] d) { uint s = 0; for (int i = 0; i + 3 < d.Length; i += 4) s += (uint)(d[i] << 24 | d[i + 1] << 16 | d[i + 2] << 8 | d[i + 3]); return s; }

    sealed class BE
    {
        readonly Stream s;
        public BE(Stream s) { this.s = s; }
        public void U8(int v) => s.WriteByte((byte)v);
        public void U16(int v) { s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }
        public void S16(int v) => U16(v & 0xFFFF);
        public void U32(uint v) { U16((int)(v >> 16)); U16((int)(v & 0xFFFF)); }
        public void Tag(string t) { foreach (var c in t) s.WriteByte((byte)c); }
    }
}
