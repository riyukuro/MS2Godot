using System.IO.Compression;

namespace Ms2.Swf;

/// <summary>
/// A parsed SWF / Scaleform GFX movie: the definitions and display lists that matter for rebuilding the layout
/// in another UI toolkit. Shape outlines, glyphs and ActionScript bytecode are not decoded.
/// </summary>
public sealed class SwfMovie
{
    public string Signature;                    // FWS CWS ZWS GFX CFX
    public int Version;
    public Rect Stage;
    public double FrameRate;
    public int FrameCount;
    public Rgba Background;
    public bool? ActionScript3;
    public int ExporterVersion, ExporterBitmapFormat;
    public string ExporterName;
    public Timeline Root = new();
    public Dictionary<int, Character> Characters = new();
    public List<ImportGroup> Imports = new();
    public Dictionary<int, string> FontNames = new();
    public Dictionary<int, string> SymbolClasses = new();
    public Dictionary<int, string> Exports = new();
    public List<(string Name, int Scene)> Scenes = new();
    public SortedDictionary<int, TagStat> Stats = new();
    public List<string> Problems = new();
    public int AbcBytes;
    /// <summary>Raw ABC blocks (DoABC tags) in file order.</summary>
    public List<(string Name, byte[] Data)> AbcBlocks = new();

    public static SwfMovie Load(string path) => Parse(File.ReadAllBytes(path));

    public static SwfMovie Parse(byte[] file)
    {
        if (file.Length < 8) throw new InvalidDataException("too short for a SWF header");
        var m = new SwfMovie { Signature = new string(new[] { (char)file[0], (char)file[1], (char)file[2] }), Version = file[3] };
        byte[] body;
        switch (m.Signature)
        {
            case "FWS": case "GFX": body = file; break;
            case "CWS": case "CFX":
            {
                using var z = new ZLibStream(new MemoryStream(file, 8, file.Length - 8), CompressionMode.Decompress);
                var ms = new MemoryStream();
                z.CopyTo(ms);
                body = new byte[8 + ms.Length];
                Array.Copy(file, body, 8);
                ms.ToArray().CopyTo(body, 8);
                break;
            }
            default: throw new NotSupportedException($"unsupported signature {m.Signature}");
        }
        // The declared length can be shorter than the decompressed buffer (zlib padding): stop at it.
        int declared = (int)BitConverter.ToUInt32(file, 4);
        int end = declared > 8 && declared <= body.Length ? declared : body.Length;
        var r = new SwfReader(body, 8, end);
        m.Stage = ReadRect(r);
        m.FrameRate = r.Fixed8();
        m.FrameCount = r.U16();
        m.ParseTags(r, m.Root, top: true);
        m.Root.FrameCount = m.FrameCount;
        m.Finish();
        return m;
    }

    // ---- primitives -------------------------------------------------------------------------------------

    public static Rect ReadRect(SwfReader r)
    {
        int n = (int)r.UB(5);
        int x0 = r.SB(n), x1 = r.SB(n), y0 = r.SB(n), y1 = r.SB(n);
        r.Align();
        return new Rect(x0 / 20.0, y0 / 20.0, (x1 - x0) / 20.0, (y1 - y0) / 20.0);
    }

    static Matrix ReadMatrix(SwfReader r)
    {
        double a = 1, d = 1, b = 0, c = 0;
        if (r.Bit()) { int n = (int)r.UB(5); a = r.FB(n); d = r.FB(n); }
        if (r.Bit()) { int n = (int)r.UB(5); b = r.FB(n); c = r.FB(n); }
        int nt = (int)r.UB(5);
        int tx = r.SB(nt), ty = r.SB(nt);
        r.Align();
        return new Matrix(a, b, c, d, tx / 20.0, ty / 20.0);
    }

    static ColorXform ReadColorXform(SwfReader r, bool alpha)
    {
        bool hasAdd = r.Bit(), hasMul = r.Bit();
        int n = (int)r.UB(4);
        double rm = 1, gm = 1, bm = 1, am = 1; int ra = 0, ga = 0, ba = 0, aa = 0;
        if (hasMul) { rm = r.SB(n) / 256.0; gm = r.SB(n) / 256.0; bm = r.SB(n) / 256.0; if (alpha) am = r.SB(n) / 256.0; }
        if (hasAdd) { ra = r.SB(n); ga = r.SB(n); ba = r.SB(n); if (alpha) aa = r.SB(n); }
        r.Align();
        return new ColorXform(rm, gm, bm, am, ra, ga, ba, aa);
    }

    static Rgba ReadRgb(SwfReader r) => new(r.U8(), r.U8(), r.U8(), 255);
    static Rgba ReadRgba(SwfReader r) => new(r.U8(), r.U8(), r.U8(), r.U8());

    void Stat(int code, int kind)
    {
        if (!Stats.TryGetValue(code, out var s)) Stats[code] = s = new TagStat();
        if (kind == 0) s.Parsed++; else if (kind == 1) s.Skipped++; else s.Failed++;
    }

    // ---- tag loop ---------------------------------------------------------------------------------------

    void ParseTags(SwfReader r, Timeline tl, bool top)
    {
        var ops = new List<TimelineOp>();
        while (r.Remaining >= 2)
        {
            int h = r.U16();
            int code = h >> 6, len = h & 63;
            if (len == 63) len = (int)r.U32();
            int start = r.Position, end = start + len;
            if (end > r.End) { Problems.Add($"tag {code} at {start} overruns the movie"); Stat(code, 2); break; }
            if (code == 0) { Stat(0, 0); break; }
            var tr = new SwfReader(r.Buffer, start, end);
            int kind;
            try { kind = HandleTag(code, tr, tl, ops, top); }
            catch (Exception e)
            {
                kind = 2;
                Problems.Add($"tag {code} at {start} len {len}: {e.GetType().Name}: {e.Message}");
            }
            Stat(code, kind);
            r.Seek(end);
        }
        if (ops.Count > 0) tl.Frames.Add(ops);
    }

    /// <returns>0 parsed, 1 skipped (nothing layout-relevant in it)</returns>
    int HandleTag(int code, SwfReader r, Timeline tl, List<TimelineOp> ops, bool top)
    {
        switch (code)
        {
            case 1: // ShowFrame
                tl.Frames.Add(new List<TimelineOp>(ops)); ops.Clear(); return 0;
            case 4: case 26: case 70: ops.Add(new TimelineOp { Place = ReadPlace(r, code) }); return 0;
            case 5: r.U16(); ops.Add(new TimelineOp { RemoveDepth = r.U16() }); return 0;
            case 28: ops.Add(new TimelineOp { RemoveDepth = r.U16() }); return 0;
            case 43: // FrameLabel
                tl.Labels.Add(new FrameLabel { Frame = tl.Frames.Count, Name = r.CString() }); return 0;
            case 9: Background = ReadRgb(r); return 0;
            case 69: { uint f = r.U32(); ActionScript3 = (f & 8) != 0; return 0; }
            case 2: case 22: case 32: case 83: ReadShape(r, code); return 0;
            case 37: ReadEditText(r); return 0;
            case 39: ReadSprite(r); return 0;
            case 7: case 34: ReadButton(r, code); return 0;
            case 56: { int n = r.U16(); for (int i = 0; i < n; i++) { int id = r.U16(); Exports[id] = r.CString(); } return 0; }
            case 76: { int n = r.U16(); for (int i = 0; i < n; i++) { int id = r.U16(); SymbolClasses[id] = r.CString(); } return 0; }
            case 57: case 71: ReadImport(r, code); return 0;
            case 78: { int id = r.U16(); Get(id, CharKind.Other, code).Scale9 = ReadRect(r); return 0; }
            case 86: ReadSceneData(r); return 0;
            case 48: case 75: ReadFont(r, code); return 0;
            case 88: { int id = r.U16(); FontNames[id] = r.CString(); return 0; }
            case 10: case 91: Register(r.U16(), CharKind.Font, code); return 0;
            case 6: case 21: case 35: case 90: Register(r.U16(), CharKind.Image, code); return 0;
            case 20: case 36: { var c = Register(r.U16(), CharKind.Image, code); r.U8(); c.ImageWidth = r.U16(); c.ImageHeight = r.U16(); return 0; }
            case 11: case 33: { var c = Register(r.U16(), CharKind.StaticText, code); c.Bounds = ReadRect(r); return 0; }
            case 46: case 84: { var c = Register(r.U16(), CharKind.MorphShape, code); c.Bounds = ReadRect(r); return 0; }
            case 60: Register(r.U16(), CharKind.Video, code); return 0;
            case 14: Register(r.U16(), CharKind.Sound, code); return 0;
            case 87: Register(r.U16(), CharKind.Binary, code); return 0;
            case 82: { r.U32(); var n = r.CString(); AbcBytes += r.Remaining; AbcBlocks.Add((n, r.Bytes(r.Remaining))); return 1; }
            case 72: AbcBytes += r.Remaining; AbcBlocks.Add(("", r.Bytes(r.Remaining))); return 1;
            case 1000: ReadExporterInfo(r); return 0;
            case 1008: ReadSubImage(r); return 0;
            case 1009: ReadExternalImage(r); return 0;
            default: return 1;   // DoAction, JPEGTables, sound streams, font glyph tables, metadata, ...
        }
    }

    Character Get(int id, CharKind kind, int code)
    {
        if (!Characters.TryGetValue(id, out var c)) Characters[id] = c = new Character { Id = id, Kind = kind, TagCode = code };
        return c;
    }

    Character Register(int id, CharKind kind, int code)
    {
        var c = Get(id, kind, code);
        c.Kind = kind; c.TagCode = code;
        return c;
    }

    // ---- placement --------------------------------------------------------------------------------------

    Placement ReadPlace(SwfReader r, int code)
    {
        var p = new Placement();
        if (code == 4)
        {
            p.CharacterId = r.U16(); p.Depth = r.U16(); p.Matrix = ReadMatrix(r);
            if (r.Remaining > 0) p.Color = ReadColorXform(r, false);
            return p;
        }
        int f = r.U8();
        int f3 = code == 70 ? r.U8() : 0;
        p.Depth = r.U16();
        p.Move = (f & 1) != 0;
        if (code == 70 && (f3 & 8) != 0) p.ClassName = r.CString();   // the HasImage bit (16) is set by the GFx exporter without a class name
        if ((f & 2) != 0) p.CharacterId = r.U16();
        if ((f & 4) != 0) p.Matrix = ReadMatrix(r);
        if ((f & 8) != 0) p.Color = ReadColorXform(r, true);
        if ((f & 16) != 0) p.Ratio = r.U16();
        if ((f & 32) != 0) p.Name = r.CString();
        if ((f & 64) != 0) p.ClipDepth = r.U16();
        if (code == 70)
        {
            if ((f3 & 1) != 0) p.Filters = ReadFilters(r);
            if ((f3 & 2) != 0) p.BlendMode = r.U8();
            if ((f3 & 4) != 0) p.CacheAsBitmap = r.U8() != 0;
            if ((f3 & 32) != 0) p.Visible = r.U8() != 0;
            // background colour and clip actions are not needed
        }
        return p;
    }

    static readonly string[] FilterNames = { "dropShadow", "blur", "glow", "bevel", "gradientGlow", "convolution", "colorMatrix", "gradientBevel" };

    static List<FilterInfo> ReadFilters(SwfReader r)
    {
        int n = r.U8();
        var list = new List<FilterInfo>();
        for (int i = 0; i < n; i++)
        {
            int t = r.U8();
            string name = t < FilterNames.Length ? FilterNames[t] : "unknown" + t;
            string color = null; double bx = 0, by = 0;
            switch (t)
            {
                case 0: color = ReadRgba(r).Hex; bx = r.Fixed16(); by = r.Fixed16(); r.Skip(4 + 4 + 2 + 1); break;
                case 1: bx = r.Fixed16(); by = r.Fixed16(); r.Skip(1); break;
                case 2: color = ReadRgba(r).Hex; bx = r.Fixed16(); by = r.Fixed16(); r.Skip(2 + 1); break;
                case 3: color = ReadRgba(r).Hex; r.Skip(4); bx = r.Fixed16(); by = r.Fixed16(); r.Skip(4 + 4 + 2 + 1); break;
                case 4: case 7: { int c = r.U8(); r.Skip(c * 5); bx = r.Fixed16(); by = r.Fixed16(); r.Skip(4 + 4 + 2 + 1); break; }
                case 5: { int mx = r.U8(), my = r.U8(); r.Skip(8 + 4 * mx * my + 4 + 1); break; }
                case 6: r.Skip(80); break;
                default: throw new InvalidDataException("filter type " + t);
            }
            list.Add(new FilterInfo(name, color, bx, by));
        }
        return list;
    }

    // ---- sprites, buttons, text ---------------------------------------------------------------------------

    void ReadSprite(SwfReader r)
    {
        int id = r.U16();
        var c = Register(id, CharKind.Sprite, 39);
        c.Timeline = new Timeline { FrameCount = r.U16() };
        ParseTags(new SwfReader(r.Buffer, r.Position, r.End), c.Timeline, top: false);
    }

    void ReadButton(SwfReader r, int code)
    {
        var c = Register(r.U16(), CharKind.Button, code);
        c.Button = new List<ButtonRecord>();
        if (code == 34) { c.ButtonTrackAsMenu = (r.U8() & 1) != 0; r.U16(); }
        while (r.Remaining > 0)
        {
            int f = r.U8();
            if (f == 0) break;
            var b = new ButtonRecord { Up = (f & 1) != 0, Over = (f & 2) != 0, Down = (f & 4) != 0, Hit = (f & 8) != 0 };
            b.CharacterId = r.U16(); b.Depth = r.U16(); b.Matrix = ReadMatrix(r);
            if (code == 34)
            {
                b.Color = ReadColorXform(r, true);
                if ((f & 0x10) != 0) ReadFilters(r);
                if ((f & 0x20) != 0) r.U8();
            }
            c.Button.Add(b);
        }
    }

    void ReadEditText(SwfReader r)
    {
        var c = Register(r.U16(), CharKind.EditText, 37);
        c.Bounds = ReadRect(r);
        int f1 = r.U8(), f2 = r.U8();
        var t = c.Text = new EditText
        {
            WordWrap = (f1 & 64) != 0, Multiline = (f1 & 32) != 0, Password = (f1 & 16) != 0, ReadOnly = (f1 & 8) != 0,
            AutoSize = (f2 & 64) != 0, NoSelect = (f2 & 16) != 0, Border = (f2 & 8) != 0, Html = (f2 & 2) != 0, UseOutlines = (f2 & 1) != 0,
        };
        if ((f1 & 1) != 0) t.FontId = r.U16();
        if ((f2 & 128) != 0) t.FontClass = r.CString();
        if ((f1 & 1) != 0 || (f2 & 128) != 0) t.Size = r.U16() / 20.0;
        if ((f1 & 4) != 0) t.Color = ReadRgba(r);
        if ((f1 & 2) != 0) t.MaxLength = r.U16();
        t.Align = "left";
        if ((f2 & 32) != 0)
        {
            t.Align = r.U8() switch { 1 => "right", 2 => "center", 3 => "justify", _ => "left" };
            t.LeftMargin = r.U16() / 20.0; t.RightMargin = r.U16() / 20.0; t.Indent = r.U16() / 20.0; t.Leading = r.S16() / 20.0;
        }
        t.Variable = r.CString();
        if ((f1 & 128) != 0) t.InitialText = r.CString();
        if (string.IsNullOrEmpty(t.Variable)) t.Variable = null;
    }

    void ReadImport(SwfReader r, int code)
    {
        var g = new ImportGroup { Url = r.CString() };
        if (code == 71) { r.U8(); r.U8(); }
        int n = r.U16();
        for (int i = 0; i < n; i++) { int id = r.U16(); g.Assets.Add((id, r.CString())); }
        Imports.Add(g);
    }

    void ReadSceneData(SwfReader r)
    {
        // Scenes and frame labels use variable-length 32-bit integers (encoded u32).
        uint Enc() { uint v = 0; int s = 0; while (true) { byte b = r.U8(); v |= (uint)(b & 0x7f) << s; if ((b & 0x80) == 0) break; s += 7; if (s > 35) throw new InvalidDataException("bad encoded u32"); } return v; }
        uint n = Enc();
        for (uint i = 0; i < n; i++) { int off = (int)Enc(); Scenes.Add((r.CString(), off)); }
        // frame labels are already in the FrameLabel tags; nothing to add
    }

    // ---- fonts ------------------------------------------------------------------------------------------

    void ReadFont(SwfReader r, int code)
    {
        int id = r.U16(); int flags = r.U8(); r.U8();
        var name = r.String8().TrimEnd('\0');
        FontNames[id] = name;
        var c = Register(id, CharKind.Font, code); c.FontName = name;
        var f = new FontData { Name = name, HasLayout = (flags & 0x80) != 0, Bold = (flags & 1) != 0, Italic = (flags & 2) != 0, EmSquare = code == 75 ? 20480 : 1024 };
        bool wideOffsets = (flags & 8) != 0, wideCodes = (flags & 4) != 0 || code == 75;
        int n = r.U16();
        int tableStart = r.Position;
        var offs = new int[n];
        for (int i = 0; i < n; i++) offs[i] = wideOffsets ? (int)r.U32() : r.U16();
        int codeTable = wideOffsets ? (int)r.U32() : r.U16();
        for (int i = 0; i < n; i++)
        {
            var g = new Glyph();
            var gr = new SwfReader(r.Buffer, tableStart + offs[i], r.End);
            ReadGlyph(gr, g);
            f.Glyphs.Add(g);
        }
        r.Seek(tableStart + codeTable);
        for (int i = 0; i < n; i++) f.Glyphs[i].Code = wideCodes ? r.U16() : r.U8();
        if (f.HasLayout)
        {
            f.Ascent = r.S16(); f.Descent = r.S16(); f.Leading = r.S16();
            for (int i = 0; i < n; i++) f.Glyphs[i].Advance = r.S16();
        }
        c.Font = f;
    }

    /// <summary>Glyph outline: a SHAPE without styles. Curved edges give an off-curve control point then the anchor.</summary>
    static void ReadGlyph(SwfReader r, Glyph g)
    {
        int nf = (int)r.UB(4); r.UB(4);
        int x = 0, y = 0;
        List<(int, int, bool)> cur = null;
        void Flush()
        {
            if (cur != null && cur.Count >= 3)
            {
                if (cur[^1].Item1 == cur[0].Item1 && cur[^1].Item2 == cur[0].Item2 && cur[^1].Item3) cur.RemoveAt(cur.Count - 1);
                if (cur.Count >= 3) g.Contours.Add(cur);
            }
            cur = null;
        }
        while (true)
        {
            if (r.UB(1) == 1)
            {
                cur ??= new List<(int, int, bool)> { (x, y, true) };
                if (r.UB(1) == 1)
                {
                    int n = (int)r.UB(4) + 2; int dx = 0, dy = 0;
                    if (r.Bit()) { dx = r.SB(n); dy = r.SB(n); } else if (r.Bit()) dy = r.SB(n); else dx = r.SB(n);
                    x += dx; y += dy; cur.Add((x, y, true));
                }
                else
                {
                    int n = (int)r.UB(4) + 2;
                    int cx = x + r.SB(n), cy = y + r.SB(n); x = cx + r.SB(n); y = cy + r.SB(n);
                    cur.Add((cx, cy, false)); cur.Add((x, y, true));
                }
                continue;
            }
            uint fl = r.UB(5);
            if (fl == 0) break;
            if ((fl & 1) != 0) { Flush(); int n = (int)r.UB(5); x = r.SB(n); y = r.SB(n); }
            if ((fl & 2) != 0) r.UB(nf);
            if ((fl & 4) != 0) r.UB(nf);
            if ((fl & 8) != 0) r.UB(0);
            if ((fl & 16) != 0) throw new InvalidDataException("new styles in a glyph");
        }
        Flush();
    }

    // ---- GFx extension tags ---------------------------------------------------------------------------------

    void ReadExporterInfo(SwfReader r)
    {
        ExporterVersion = r.U16();
        if (ExporterVersion >= 0x10a) r.U32();
        ExporterBitmapFormat = r.U16();
        r.String8();                      // prefix
        ExporterName = r.String8();
    }

    void ReadExternalImage(SwfReader r)
    {
        // 1009: id, flags, bitmap format, width, height, export name (u8 string), file name (u8 string)
        var c = Register(r.U16(), CharKind.Image, 1009);
        r.U16(); r.U16();
        c.ImageWidth = r.U16(); c.ImageHeight = r.U16();
        r.String8();
        c.ImageFile = r.String8();
    }

    void ReadSubImage(SwfReader r)
    {
        // 1008: id, atlas image id, x0, y0, x1, y1 (pixels in the atlas)
        var c = Register(r.U16(), CharKind.SubImage, 1008);
        c.AtlasImageId = r.U16();
        int x0 = r.U16(), y0 = r.U16(), x1 = r.U16(), y1 = r.U16();
        c.AtlasRect = new Rect(x0, y0, x1 - x0, y1 - y0);
        c.Bounds = new Rect(0, 0, x1 - x0, y1 - y0);
    }

    // ---- shapes -----------------------------------------------------------------------------------------

    void ReadShape(SwfReader r, int code)
    {
        int ver = code switch { 2 => 1, 22 => 2, 32 => 3, _ => 4 };
        var c = Register(r.U16(), CharKind.Shape, code);
        c.Bounds = ReadRect(r);
        if (ver == 4) { ReadRect(r); r.U8(); }
        c.BitmapFills = new List<BitmapFill>();
        var table = new List<Rgba> { null };           // fill style table, 1-based; null for non-solid fills
        ReadFillStyles(r, ver, c, table);
        ReadLineStyles(r, ver, c);
        int nf = (int)r.UB(4), nl = (int)r.UB(4);
        // vector fills: every sub path is filled with its right (else left) fill style when that is a solid colour
        var paths = new List<VectorPath>();
        var cur = new List<float>();
        int x = 0, y = 0, f0 = 0, f1 = 0;
        void Flush()
        {
            int fill = f1 != 0 ? f1 : f0;
            if (cur.Count >= 6 && fill > 0 && fill < table.Count && table[fill] != null)
                paths.Add(new VectorPath(table[fill], cur.ToArray()));
            cur.Clear();
        }
        void Pt(int px, int py) { cur.Add(px / 20f); cur.Add(py / 20f); }
        while (true)
        {
            if (r.UB(1) == 1)
            {
                if (cur.Count == 0) Pt(x, y);
                if (r.UB(1) == 1)       // straight edge
                {
                    int n = (int)r.UB(4) + 2;
                    int dx = 0, dy = 0;
                    if (r.Bit()) { dx = r.SB(n); dy = r.SB(n); }
                    else if (r.Bit()) dy = r.SB(n);
                    else dx = r.SB(n);
                    x += dx; y += dy; Pt(x, y);
                }
                else
                {
                    int n = (int)r.UB(4) + 2;
                    int cx = x + r.SB(n), cy = y + r.SB(n), ax = cx + r.SB(n), ay = cy + r.SB(n);
                    for (int k = 1; k <= 6; k++)
                    {
                        float t = k / 6f, u = 1 - t;
                        Pt((int)(u * u * x + 2 * u * t * cx + t * t * ax), (int)(u * u * y + 2 * u * t * cy + t * t * ay));
                    }
                    x = ax; y = ay;
                }
                continue;
            }
            uint fl = r.UB(5);
            if (fl == 0) break;
            bool restyle = (fl & (2 | 4 | 16)) != 0;
            if ((fl & 1) != 0) { Flush(); int n = (int)r.UB(5); x = r.SB(n); y = r.SB(n); }
            else if (restyle) Flush();
            if ((fl & 2) != 0) f0 = (int)r.UB(nf);
            if ((fl & 4) != 0) f1 = (int)r.UB(nf);
            if ((fl & 8) != 0) r.UB(nl);
            if ((fl & 16) != 0)
            {
                r.Align();
                table = new List<Rgba> { null }; f0 = f1 = 0;
                ReadFillStyles(r, ver, c, table);
                ReadLineStyles(r, ver, c);
                nf = (int)r.UB(4); nl = (int)r.UB(4);
            }
        }
        Flush();
        if (paths.Count > 0) c.Paths = paths;
        if (c.BitmapFills.Count == 0) c.BitmapFills = null;
    }

    void ReadFillStyles(SwfReader r, int ver, Character c, List<Rgba> table = null)
    {
        int n = r.U8();
        if (n == 0xFF && ver >= 2) n = r.U16();
        for (int i = 0; i < n; i++) ReadFillStyle(r, ver, c, table);
    }

    void ReadFillStyle(SwfReader r, int ver, Character c, List<Rgba> table = null)
    {
        int t = r.U8();
        if (t != 0x00) table?.Add(null);
        switch (t)
        {
            case 0x00: { var col = ver >= 3 ? ReadRgba(r) : ReadRgb(r); c.SolidFill ??= col; table?.Add(col); break; }
            case 0x10: case 0x12: case 0x13:
            {
                ReadMatrix(r);
                int cnt = r.U8() & 15;   // spread and interpolation in the high bits
                for (int i = 0; i < cnt; i++) { r.U8(); if (ver >= 3) ReadRgba(r); else ReadRgb(r); }
                if (t == 0x13) r.Fixed8();
                break;
            }
            case 0x40: case 0x41: case 0x42: case 0x43:
            {
                int id = r.U16();
                var m = ReadMatrix(r);
                if (id != 0xFFFF) c.BitmapFills.Add(new BitmapFill(id, m, t == 0x40 || t == 0x41, t == 0x40 || t == 0x42));
                break;
            }
            default: throw new InvalidDataException("fill style type 0x" + t.ToString("x"));
        }
    }

    void ReadLineStyles(SwfReader r, int ver, Character c)
    {
        int n = r.U8();
        if (n == 0xFF && ver >= 2) n = r.U16();
        for (int i = 0; i < n; i++)
        {
            r.U16();                                   // width
            if (ver < 4) { if (ver >= 3) ReadRgba(r); else ReadRgb(r); continue; }
            int f1 = r.U8(), f2 = r.U8();
            if (((f1 >> 4) & 3) == 2) r.U16();         // miter limit
            if ((f1 & 8) != 0) ReadFillStyle(r, ver, c); else ReadRgba(r);
        }
    }

    // ---- post processing ----------------------------------------------------------------------------------

    void Finish()
    {
        foreach (var (id, name) in SymbolClasses) if (Characters.TryGetValue(id, out var c)) c.ClassName = name;
        foreach (var (id, name) in Exports) if (Characters.TryGetValue(id, out var c)) c.ExportName = name;
        foreach (var c in Characters.Values)
            if (c.Text?.FontId is int fid && FontNames.TryGetValue(fid, out var fn)) c.Text.FontName = fn;
        if (SymbolClasses.TryGetValue(0, out var rootClass)) RootClass = rootClass;
        var memo = new Dictionary<int, Rect>(); var busy = new HashSet<int>();
        foreach (var c in Characters.Values) if (c.Kind is CharKind.Sprite or CharKind.Button) c.Bounds = BoundsOf(c.Id, memo, busy);
    }

    public string RootClass;

    /// <summary>Union of every placement a sprite ever makes (all frames), in the sprite's own space.</summary>
    Rect BoundsOf(int id, Dictionary<int, Rect> memo, HashSet<int> busy)
    {
        if (memo.TryGetValue(id, out var r)) return r;
        if (!Characters.TryGetValue(id, out var c)) return null;
        if (c.Kind is not (CharKind.Sprite or CharKind.Button)) return c.Bounds;
        if (!busy.Add(id)) return null;                // cycle
        Rect u = null;
        if (c.Kind == CharKind.Button)
        {
            foreach (var b in c.Button) u = Rect.Union(u, b.Matrix.Transform(BoundsOf(b.CharacterId, memo, busy)));
        }
        else
        {
            // a move without a new matrix keeps the old one, so track the last matrix per depth
            var chars = new Dictionary<int, int>();
            var mats = new Dictionary<int, Matrix>();
            foreach (var f in c.Timeline.Frames)
                foreach (var op in f)
                {
                    if (op.Place == null) { chars.Remove(op.RemoveDepth); mats.Remove(op.RemoveDepth); continue; }
                    var p = op.Place;
                    if (p.CharacterId is int cid) { chars[p.Depth] = cid; if (!p.Move) mats[p.Depth] = Matrix.Identity; }
                    if (p.Matrix != null) mats[p.Depth] = p.Matrix;
                    if (p.ClipDepth != null) continue;   // masks do not add to the visible extent
                    if (chars.TryGetValue(p.Depth, out var ch))
                        u = Rect.Union(u, (mats.TryGetValue(p.Depth, out var mm) ? mm : Matrix.Identity).Transform(BoundsOf(ch, memo, busy)));
                }
        }
        busy.Remove(id);
        memo[id] = u;
        return u;
    }
}
