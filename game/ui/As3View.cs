using System.Text.Json.Nodes;
using Godot;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using FlashPlayer = Ms2.Avm2.Flash.Player;
using AsName = Ms2.Avm2.Name;

namespace Ms2Godot.Ui;

/// <summary>
/// Draws the display list of a movie run by its own ActionScript (Ms2.Avm2's <see cref="Player"/>) and feeds it Godot input.
/// One Godot node per display object, kept in sync each frame: shapes are the layout's <see cref="ShapeNode"/>, text fields
/// are drawn from the runtime's lines (with its English fitting), bitmaps from Resource/Image, Graphics rects and
/// ellipses directly. Masks (timeline clip depths and script masks) clip to the mask's own shapes (<see cref="MaskShape"/>).
/// </summary>
public partial class As3View : Node2D
{
    public FlashPlayer Player { get; private set; }
    readonly UiAssets assets;
    readonly Dictionary<SymbolScope, UiScope> scopes = new();
    readonly List<Entry> entries = new();
    int gen;
    double frameTime;
    /// <summary>Advance the movie's frames in _Process (off for scripted captures that tick by hand).</summary>
    public bool AutoPlay = true;

    sealed class Entry
    {
        public AsDisplayObject Obj;
        public Node2D Holder;            // transform, visibility, colour
        public Node2D Content;           // what the object itself draws (shape, text, bitmap, graphics)
        public object ContentKey;
        public MaskShape Mask;           // set when this object masks following siblings or is masked by script
        public int Seen;
    }

    public As3View(FlashPlayer player, UiAssets assets)
    {
        Player = player; this.assets = assets;
        player.Translate ??= key => assets.Str(key);
        player.ImageSize ??= url => ResolveImage(url) is { } t ? ((int)t.GetSize().X, (int)t.GetSize().Y) : null;
        // the runtime breaks lines and sizes fields with the advances of the game font the field draws in (unhinted, as
        // FieldNode draws it), measured at 8x the size so the whole-pixel font sizes do not round the advances
        TextMeasure.HeightRatio = font => assets.LineRatios(font).Height;
        TextMeasure.Width = (font, size, s) =>
        {
            if (string.IsNullOrEmpty(s) || size <= 0) return 0;
            int px = Math.Max(1, (int)Math.Round(size * 8));
            return assets.FontFor(font).GetStringSize(s, HorizontalAlignment.Left, -1, px).X * size / px;
        };
    }

    /// <summary>Scale from stage units to this node's units (set by the host to fit the viewport).</summary>
    public float StageScale { get => Scale.X; set => Scale = new Vector2(value, value); }

    /// <summary>Milliseconds all movie views spent in _Process (ticks and display-list sync), and in the ticks alone, since the last reset (frame-time runs).</summary>
    public static double SpentMs, TickMs;

    public override void _Process(double delta)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        Advance(delta);
        SpentMs += System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
    }

    void Advance(double delta)
    {
        if (AutoPlay)
        {
            frameTime += delta;
            double step = 1.0 / Math.Max(1, Player.FrameRate);
            int n = 0;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            while (frameTime >= step && n++ < 4) { frameTime -= step; Player.Tick(); }
            TickMs += System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            if (frameTime > step) frameTime = 0;
        }
        Sync();
    }

    // ---- display list sync -------------------------------------------------------------------------------------

    public void Sync()
    {
        gen++;
        var masks = new HashSet<AsDisplayObject>();
        CollectMasks(Player.Stage, masks);
        SyncChildren(Player.Stage, this, masks, null, null);
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var e = entries[i];
            if (e.Seen == gen) continue;
            e.Holder.GetParent()?.RemoveChild(e.Holder);
            e.Holder.QueueFree();
            e.Mask?.GetParent()?.RemoveChild(e.Mask);
            e.Mask?.QueueFree();
            if (ReferenceEquals(e.Obj.Tag, e)) e.Obj.Tag = null;
            entries.RemoveAt(i);
        }
    }

    static void CollectMasks(AsDisplayObject o, HashSet<AsDisplayObject> masks)
    {
        if (o.MaskObject != null) masks.Add(o.MaskObject);
        if (o is AsContainer c) foreach (var k in c.Children) CollectMasks(k, masks);
    }

    /// <summary>Puts the nodes of a container's children under <paramref name="into"/> in display order.</summary>
    void SyncChildren(AsContainer c, Node2D into, HashSet<AsDisplayObject> masks, NineCtx nine, Material blend)
    {
        var wanted = new List<Node>();
        var content = (into.HasMeta("as3content") ? into.GetMeta("as3content").AsGodotObject() : null) as Node2D;
        if (content != null) wanted.Add(content);
        MaskShape clip = null; int clipEnd = 0;
        foreach (var k in c.Children)
        {
            if (clip != null && k.Depth > clipEnd) clip = null;
            var e = SyncObject(k, masks, nine, blend);
            if (k.ClipDepth is int cd && k.Depth != int.MinValue)
            {
                // a timeline mask: following depths up to its clip depth draw only inside it; the mask itself is not drawn
                e.Mask ??= new MaskShape();
                e.Mask.Set(MaskShape.Polygons(k, Mat.Identity));
                e.Holder.Visible = false;
                Reparent(e.Holder, into, wanted);
                Reparent(e.Mask, into, wanted);
                ClearExcept(e.Mask, null);
                clip = e.Mask; clipEnd = cd;
                continue;
            }
            Node2D top = e.Holder;
            if (k.MaskObject != null)
            {
                e.Mask ??= new MaskShape();
                var mo = k.MaskObject;
                var toC = (mo.Parent?.ToGlobalMatrix() ?? Mat.Identity).Then(c.ToGlobalMatrix().Inverse());
                e.Mask.Set(MaskShape.Polygons(mo, toC));
                if (e.Holder.GetParent() != e.Mask) { e.Holder.GetParent()?.RemoveChild(e.Holder); e.Mask.AddChild(e.Holder); }
                top = e.Mask;
            }
            if (clip != null) { if (top.GetParent() != clip) { top.GetParent()?.RemoveChild(top); clip.AddChild(top); } clip.MoveChild(top, -1); }
            else Reparent(top, into, wanted);
        }
        // drop nodes that are no longer children here (their entries are freed after the pass if not seen elsewhere)
        foreach (var n in into.GetChildren())
            if (!wanted.Contains(n)) into.RemoveChild(n);
        for (int i = 0; i < wanted.Count; i++) into.MoveChild(wanted[i], i);
    }

    static void Reparent(Node2D n, Node2D into, List<Node> wanted)
    {
        if (n.GetParent() != into) { n.GetParent()?.RemoveChild(n); into.AddChild(n); }
        wanted.Add(n);
    }

    static void ClearExcept(Node2D n, Node keep)
    {
        foreach (var ch in n.GetChildren()) if (ch != keep) n.RemoveChild(ch);
    }

    Entry SyncObject(AsDisplayObject o, HashSet<AsDisplayObject> masks, NineCtx nine, Material blend)
    {
        blend = BlendFor(o.Blend) ?? blend;
        if (o.Tag is not Entry e)
        {
            e = new Entry { Obj = o, Holder = new Node2D { Name = string.IsNullOrEmpty(o.InstanceName) ? o.Class?.LocalName ?? "obj" : o.InstanceName } };
            o.Tag = e; entries.Add(e);
        }
        e.Seen = gen;
        var m = o.Matrix;
        // under a scale-9 sprite (whose holder drops its scale): only shape meshes are mapped through the grid
        // (TreeCacheShapeLayer's mesh key, sfr_render_03f.cpp:538); a text node keeps the whole matrix chain, so it gets
        // the sprite's scale
        if (nine != null && o is AsTextField or AsStaticText)
            m = new Mat(m.A * nine.Sx, m.B * nine.Sy, m.C * nine.Sx, m.D * nine.Sy, m.Tx * nine.Sx, m.Ty * nine.Sy);
        // scale-9 sprite with a scale: its own scale is applied by mapping its shapes (corners and edges keep their size)
        NineCtx childNine = null;
        if (o.Symbol?["scale9"] is JsonObject g9 && o is AsContainer)
        {
            double sx = Math.Sqrt(m.A * m.A + m.B * m.B), sy = Math.Sqrt(m.C * m.C + m.D * m.D);
            if (Math.Abs(sx - 1) > 1e-3 || Math.Abs(sy - 1) > 1e-3)
            {
                childNine = new NineCtx { Grid = UiBuilder.Rc(g9), Bounds = UiBuilder.Rc(o.Symbol["bounds"]), Sx = (float)sx, Sy = (float)sy };
                m = new Mat(1, 0, 0, 1, m.Tx, m.Ty);
            }
        }
        e.Holder.Transform = new Transform2D(new Vector2((float)m.A, (float)m.B), new Vector2((float)m.C, (float)m.D), new Vector2((float)m.Tx, (float)m.Ty));
        e.Holder.Visible = o.IsVisible && !masks.Contains(o);
        var cx = o.Color;
        e.Holder.Modulate = new Color((float)Math.Clamp(cx.Rm + cx.Ra / 255, 0, 1), (float)Math.Clamp(cx.Gm + cx.Ga / 255, 0, 1), (float)Math.Clamp(cx.Bm + cx.Ba / 255, 0, 1), (float)Math.Clamp(cx.Am + cx.Aa / 255, 0, 1));
        SyncContent(e, nine);
        if (e.Content != null && e.Content.Material != blend)
        {
            e.Content.Material = blend;
            foreach (var ch in e.Content.GetChildren()) if (ch is CanvasItem ci) ci.UseParentMaterial = true;   // ShapeNode's bitmap sprites
        }
        switch (o)
        {
            case AsContainer c: SyncChildren(c, e.Holder, masks, childNine, blend); break;
            case AsSimpleButton b:
            {
                var wanted = new List<Node>();
                if (e.Content != null) wanted.Add(e.Content);
                foreach (var s in b.Drawn) Reparent(SyncObject(s, masks, null, blend).Holder, e.Holder, wanted);
                foreach (var n in e.Holder.GetChildren()) if (!wanted.Contains(n)) e.Holder.RemoveChild(n);
                break;
            }
        }
        return e;
    }

    void SyncContent(Entry e, NineCtx nine)
    {
        var o = e.Obj;
        object key; Func<Node2D> make;
        switch (o)
        {
            case AsShape sh when sh.GraphicsOrNull is { Ops.Count: > 0 } g:
                key = g; make = () => new GraphicsNode(g); break;
            case AsShape when o.Symbol != null && o.Scope != null:
                key = (o.Symbol, nine?.Sx, nine?.Sy, nine?.Offset);
                make = () => new ShapeNode(Scope(o.Scope), o.Symbol, nine?.At(new Vector2((float)o.Matrix.Tx, (float)o.Matrix.Ty) + nine.Offset)); break;
            case AsMorphShape when o.Symbol != null && o.Scope != null:
                key = o.Symbol; make = () => new ShapeNode(Scope(o.Scope), o.Symbol); break;
            case AsTextField tf when (tf.Symbol?["text"] is JsonObject) && o.Scope != null:
                key = tf; make = () => new FieldText(tf, Scope(o.Scope)); break;
            case AsTextField tf:
                key = tf; make = () => new FieldNode(tf, assets); break;
            case AsStaticText when (o.Symbol?["text"]) != null && o.Scope != null:
                key = o.Symbol; make = () => new TextNode(Scope(o.Scope), o.Symbol, 1, 1); break;
            case AsBitmap bm when bm.bitmapData?.Source is string url:
                key = (url, bm.bitmapData.width, bm.bitmapData.height);
                make = () => ResolveImage(url) is { } t ? new Sprite2D { Texture = t, Centered = false, Scale = Fit(t, bm.bitmapData) } : (Node2D)null; break;
            case AsSprite sp when sp.GraphicsOrNull is { Ops.Count: > 0 } g2:
                key = g2; make = () => new GraphicsNode(g2); break;
            default: key = null; make = null; break;
        }
        if (!Equals(key, e.ContentKey))
        {
            if (e.Content != null) { e.Content.GetParent()?.RemoveChild(e.Content); e.Content.QueueFree(); e.Content = null; }
            e.ContentKey = key;
            if (make != null) { e.Content = make(); if (e.Content != null) e.Holder.AddChild(e.Content); }
            if (e.Content != null) e.Holder.SetMeta("as3content", e.Content); else if (e.Holder.HasMeta("as3content")) e.Holder.RemoveMeta("as3content");
        }
        if (e.Content is GraphicsNode or FieldNode) e.Content.QueueRedraw();
        if (e.Content is FieldText ft) ft.Sync();
    }

    static readonly Dictionary<string, Material> blends = new();

    /// <summary>Flash blend modes the movies use, as canvas blend modes (add and screen both add light; multiply darkens).</summary>
    static Material BlendFor(string mode)
    {
        CanvasItemMaterial.BlendModeEnum? m = mode switch
        {
            "add" or "screen" or "lighten" => CanvasItemMaterial.BlendModeEnum.Add,
            "multiply" or "darken" => CanvasItemMaterial.BlendModeEnum.Mul,
            "subtract" => CanvasItemMaterial.BlendModeEnum.Sub,
            _ => null,
        };
        if (m == null) return null;
        if (!blends.TryGetValue(mode, out var mat)) blends[mode] = mat = new CanvasItemMaterial { BlendMode = m.Value };
        return mat;
    }

    static Vector2 Fit(Texture2D t, AsBitmapData d)
    {
        var s = t.GetSize();
        return d.width > 0 && d.height > 0 && s.X > 0 && s.Y > 0 ? new Vector2(d.width / s.X, d.height / s.Y) : Vector2.One;
    }

    UiScope Scope(SymbolScope s)
    {
        if (!scopes.TryGetValue(s, out var u)) scopes[s] = u = new UiScope(s.Layout, assets);
        return u;
    }


    /// <summary>
    /// An image the host supplies for a url, asked first (null: not the host's). The client binds some movie images to its
    /// own textures rather than to files: UILoadingProgress::vfunc_25 (uiloadingprogress.cpp:215-232) initializes the
    /// image the movie loads as "img://&lt;id&gt;:screenShot" with the scene screenshot's render texture.
    /// </summary>
    public Func<string, Texture2D> ImageLookup;

    /// <summary>An image url as the movies build it (game: "img://icon:" + path, or a plain path) → a Resource/Image texture.</summary>
    Texture2D ResolveImage(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (ImageLookup?.Invoke(url) is { } supplied) return supplied;
        var p = url;
        if (p.StartsWith("img://", StringComparison.OrdinalIgnoreCase)) { int c = p.IndexOf(':', 6); p = c >= 0 ? p[(c + 1)..] : p[6..]; }
        p = p.Replace('\\', '/').TrimStart('.', '/');
        int at = p.IndexOf("resource/image/", StringComparison.OrdinalIgnoreCase);
        if (at >= 0) p = p[(at + "resource/image/".Length)..];
        return assets.ImageFile(p.ToLowerInvariant());
    }

    // ---- input ---------------------------------------------------------------------------------------------------

    public override void _UnhandledInput(InputEvent ev)
    {
        switch (ev)
        {
            case InputEventMouseMotion mm:
            {
                var p = ToLocal(mm.Position);
                Player.MouseMove(p.X, p.Y);
                break;
            }
            case InputEventMouseButton mb:
            {
                var p = ToLocal(mb.Position);
                Player.MouseMove(p.X, p.Y);
                if (mb.ButtonIndex == MouseButton.Left) Player.MouseButton(mb.Pressed);
                else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp) Player.Wheel(3);
                else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown) Player.Wheel(-3);
                break;
            }
            case InputEventKey k:
            {
                uint code = FlashKeyCode(k.Keycode);
                if (code == 0 && k.Unicode == 0) return;
                Player.Key(code, k.Pressed ? (uint)k.Unicode : 0, k.Pressed, k.ShiftPressed, k.CtrlPressed);
                break;
            }
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Godot key → Flash keyCode (letters and digits share their ASCII codes).</summary>
    public static uint FlashKeyCode(Key k) => k switch
    {
        >= Key.A and <= Key.Z => (uint)k,
        >= Key.Key0 and <= Key.Key9 => (uint)k,
        Key.Enter or Key.KpEnter => 13, Key.Tab => 9, Key.Backspace => 8, Key.Escape => 27, Key.Space => 32, Key.Delete => 46,
        Key.Left => 37, Key.Up => 38, Key.Right => 39, Key.Down => 40, Key.Home => 36, Key.End => 35, Key.Pageup => 33, Key.Pagedown => 34,
        Key.Shift => 16, Key.Ctrl => 17, Key.Alt => 18,
        >= Key.F1 and <= Key.F12 => 112 + (uint)(k - Key.F1),
        _ => 0,
    };
}

/// <summary>
/// Clips its children to a Flash mask's shapes; the mask itself is not shown. Scaleform draws a mask's subtree into the
/// stencil with colour writes off (HAL::PushMask_BeginSubmit, MaskPrimitive::EmitToHAL, sfr_render_04f.cpp:721) and then
/// draws the masked content where the stencil reached the mask (HAL::EndMaskSubmit, sfr_d3d9_01e.cpp:367): the mask's
/// coverage counts, not its colour or alpha. So the node draws each shape's fill polygons opaque, without edge smoothing,
/// in the masked content's parent space.
/// </summary>
public partial class MaskShape : Node2D
{
    List<Vector2[]> polys = new();

    public MaskShape() { ClipChildren = ClipChildrenMode.Only; }

    /// <summary>The mask's polygons this frame; redrawn only when they changed.</summary>
    public void Set(List<Vector2[]> p)
    {
        bool same = p.Count == polys.Count;
        for (int i = 0; same && i < p.Count; i++) same = p[i].AsSpan().SequenceEqual(polys[i]);
        if (same) return;
        polys = p;
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var poly in polys)
            if (poly.Length >= 3 && Geometry2D.TriangulatePolygon(poly).Length > 0) DrawColoredPolygon(poly, Colors.White);
    }

    /// <summary>
    /// The fill polygons of a mask object and its visible descendants, mapped by <paramref name="toSpace"/> (from the mask's
    /// parent space). Shapes give the layout's fill paths, Graphics their filled rectangles and ellipses; a shape whose
    /// fills are only bitmaps (the layout keeps no outline for those) and a text field give their bounds.
    /// </summary>
    public static List<Vector2[]> Polygons(AsDisplayObject mask, Mat toSpace)
    {
        var list = new List<Vector2[]>();
        Collect(mask, toSpace, list, true);
        return list;
    }

    static void Collect(AsDisplayObject o, Mat parentToSpace, List<Vector2[]> list, bool root)
    {
        if (!root && !o.IsVisible) return;
        var m = o.Matrix.Then(parentToSpace);
        Vector2 P(double x, double y) { var (gx, gy) = m.Apply(x, y); return new Vector2((float)gx, (float)gy); }
        Vector2[] Box(RectD r) => new[] { P(r.X, r.Y), P(r.X + r.W, r.Y), P(r.X + r.W, r.Y + r.H), P(r.X, r.Y + r.H) };
        bool drew = false;
        if (o.Symbol?["paths"] is JsonArray ps)
            foreach (var p in ps)
            {
                if (p["pts"] is not JsonArray a || a.Count < 6) continue;
                var pts = new Vector2[a.Count / 2];
                for (int i = 0; i < pts.Length; i++) pts[i] = P((double)a[2 * i], (double)a[2 * i + 1]);
                list.Add(pts);
                drew = true;
            }
        if (!drew && (o.Symbol?["bitmaps"] is JsonArray || o is AsTextField || o is AsStaticText))
        {
            var b = o.LocalBounds();
            if (!b.IsEmpty) list.Add(Box(b));
        }
        var g = o switch { AsSprite sp => sp.GraphicsOrNull, AsShape sh => sh.GraphicsOrNull, _ => null };
        if (g != null)
            foreach (var (op, v, _, alpha) in g.Ops)
            {
                if (alpha <= 0) continue;
                if (op == "rect") { list.Add(Box(new RectD(v[0], v[1], v[2], v[3]))); continue; }
                var pts = new Vector2[24];
                for (int i = 0; i < pts.Length; i++)
                {
                    double t = i * Math.PI * 2 / pts.Length;
                    pts[i] = P(v[0] + v[2] / 2 * (1 + Math.Cos(t)), v[1] + v[3] / 2 * (1 + Math.Sin(t)));
                }
                list.Add(pts);
            }
        if (o is AsContainer c)
            foreach (var k in c.Children) Collect(k, m, list, false);
    }
}

/// <summary>Rectangles and ellipses drawn by ActionScript through flash.display.Graphics.</summary>
public partial class GraphicsNode : Node2D
{
    readonly AsGraphics g;
    public GraphicsNode(AsGraphics g) { this.g = g; }
    public override void _Draw()
    {
        foreach (var (op, v, color, alpha) in g.Ops)
        {
            if (alpha <= 0) continue;
            var c = new Color(((color >> 16) & 255) / 255f, ((color >> 8) & 255) / 255f, (color & 255) / 255f, (float)alpha);
            var r = new Rect2((float)v[0], (float)v[1], (float)v[2], (float)v[3]);
            if (op == "rect") DrawRect(r, c);
            else
            {
                var pts = new Vector2[24];
                for (int i = 0; i < pts.Length; i++)
                {
                    float a = i * Mathf.Tau / pts.Length;
                    pts[i] = r.GetCenter() + new Vector2(Mathf.Cos(a) * r.Size.X / 2, Mathf.Sin(a) * r.Size.Y / 2);
                }
                DrawColoredPolygon(pts, c);
            }
        }
    }
}

/// <summary>
/// A runtime text field: the lines the runtime laid out (English breaking, single-line captions shrunk to 80% then cut
/// with an ellipsis), in the game font of its format, aligned in its box. Drawn at the on-screen scale for crisp glyphs.
/// </summary>
public partial class FieldNode : Node2D
{
    readonly AsTextField tf;
    readonly UiAssets assets;
    public FieldNode(AsTextField tf, UiAssets assets) { this.tf = tf; this.assets = assets; }

    public override void _Draw()
    {
        var font = assets.FontFor(tf.FontClass);
        var (lines, fit) = tf.Fitted();
        var gs = GetGlobalTransform().Scale;
        float screen = Math.Max(0.01f, Math.Abs(gs.Y));
        float size = (float)(tf.FontSize * fit);
        // Scaleform's glyph cache rasterizes at the ramp size of the screen size (SnapFontSizeToRamp) and draws that raster
        // scaled to the exact screen size, as TextNode does: px is the raster size, k the raster's scale to field units
        int px = (int)TextNode.RampSize(size * screen);
        float k = size / px;
        var col = tf.Format?.color is { } cv && !Rt.IsNullish(cv) ? Rt.ToUint32(cv) : 0u;
        var color = new Color(((col >> 16) & 255) / 255f, ((col >> 8) & 255) / 255f, (col & 255) / 255f);
        var align = tf.Format?.align as string ?? "left";
        var box = tf.Box;
        // lines advance by the line height plus the field's leading; the baseline is the font's ascent below the line top
        float lineH = (float)TextMeasure.LineHeight(tf.FontClass, size, tf.Leading);
        float ascent = assets.AscentRatio(tf.FontClass) * size;
        DrawSetTransform(Vector2.Zero, 0, new Vector2(k, k));
        float y = (float)tf.FirstLineTop(lines.Count);
        foreach (var line in lines)
        {
            float w = font.GetStringSize(line, HorizontalAlignment.Left, -1, px).X * k;
            float x = (float)box.X + 2;
            if (align == "center") x = (float)(box.X + (box.W - w) / 2);
            else if (align == "right") x = (float)(box.X + box.W - 2 - w);
            DrawString(font, new Vector2(x, y + ascent) / k, line, HorizontalAlignment.Left, -1, px, color);
            y += lineH;
        }
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}

/// <summary>
/// A text field of the movie's layout, drawn by the layout's text node (<see cref="TextNode"/>: Scaleform's line layout,
/// glyph-cache raster, html runs with their colours, sizes and underlines, drop shadows and glows) from the field's
/// current state: its box and text format as the ActionScript left them, its text or html text, and its filters (the ones
/// ActionScript set, else its timeline placement's). The node is built again when the box, the format or the screen
/// scale changes; the text is set again when the field changes. The first line starts where DocView::Format's vertical
/// alignment puts it (<see cref="AsTextField.FirstLineTop"/>).
/// </summary>
public partial class FieldText : Node2D
{
    readonly AsTextField tf;
    readonly UiScope scope;
    TextNode node;
    string built;
    int version = -1;

    public FieldText(AsTextField tf, UiScope scope)
    {
        this.tf = tf; this.scope = scope;
        // a field made during a sync pass is synced before its holder joins the tree: it syncs once it is in
        TreeEntered += () => Callable.From(Sync).CallDeferred();
    }

    public void Sync()
    {
        if (!IsInsideTree()) return;
        var gs = GetGlobalTransform().Scale;
        float sx = Math.Max(0.001f, Math.Abs(gs.X)), sy = Math.Max(0.001f, Math.Abs(gs.Y));
        var f = tf.Format;
        string D(object v) => v == null || Rt.IsNullish(v) ? "" : Rt.ToString(v);
        var filters = Filters();
        string key = string.Join("|", tf.Box.X, tf.Box.Y, tf.Box.W, tf.Box.H, D(f?.font), D(f?.size), D(f?.color), D(f?.align),
            D(f?.leading), tf.wordWrap, tf.multiline, MathF.Round(sx * 1000), MathF.Round(sy * 1000), filters?.ToJsonString());
        if (key != built)
        {
            node?.QueueFree();
            node = null;
            var def = (JsonObject)tf.Symbol["text"].DeepClone();
            if (f?.font is string font) def["fontClass"] = font;
            if (f?.size is { } size && !Rt.IsNullish(size)) def["size"] = Rt.ToNumber(size);
            if (f?.color is { } c && !Rt.IsNullish(c)) def["color"] = $"#{Rt.ToUint32(c) & 0xffffff:x6}";
            if (f?.align is string align) def["align"] = align;
            if (f?.leading is { } l && !Rt.IsNullish(l)) def["leading"] = Rt.ToNumber(l);
            def["wordWrap"] = tf.wordWrap;
            def["multiline"] = tf.multiline;
            def["initial"] = "";
            var sym = new JsonObject
            {
                ["bounds"] = new JsonObject { ["x"] = tf.Box.X, ["y"] = tf.Box.Y, ["w"] = tf.Box.W, ["h"] = tf.Box.H },
                ["text"] = def,
            };
            node = new TextNode(scope, sym, sx, sy);
            AddChild(node);
            if (filters != null) node.ApplyFilters(filters, sy);
            built = key;
            version = -1;
        }
        if (tf.Version == version) return;
        version = tf.Version;
        if (tf.Html != null && !(tf.Html.Length > 1 && tf.Html[0] == '$')) node.SetHtml(tf.Html);
        else node.Text = tf.Text;
        node.Position = new Vector2(0, (float)(tf.FirstLineTop(node.NumLines) - (tf.Box.Y + 2)));
    }

    /// <summary>The field's filters as the layout writes them: the ones ActionScript set (angle in degrees), else the placement's.</summary>
    JsonArray Filters()
    {
        if (tf.Filters is not AsArray a) return tf.PlaceFilters;
        var list = new JsonArray();
        foreach (var x in a.Items)
            switch (x)
            {
                case AsDropShadowFilter d:
                    list.Add(new JsonObject { ["type"] = "dropShadow", ["color"] = $"#{d.color & 0xffffff:x6}", ["blurX"] = d.blurX, ["blurY"] = d.blurY,
                        ["angle"] = d.angle * Math.PI / 180, ["distance"] = d.distance, ["strength"] = d.strength });
                    break;
                case AsGlowFilter g:
                    list.Add(new JsonObject { ["type"] = "glow", ["color"] = $"#{g.color & 0xffffff:x6}", ["blurX"] = g.blurX, ["blurY"] = g.blurY,
                        ["strength"] = g.strength });
                    break;
            }
        return list;
    }
}
