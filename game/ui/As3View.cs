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
/// ellipses directly. Masks (timeline clip depths and script masks) clip to the mask's bounds.
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
        public MaskRect Mask;            // set when this object masks following siblings or is masked by script
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

    public override void _Process(double delta)
    {
        if (AutoPlay)
        {
            frameTime += delta;
            double step = 1.0 / Math.Max(1, Player.FrameRate);
            int n = 0;
            while (frameTime >= step && n++ < 4) { frameTime -= step; Player.Tick(); }
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
        MaskRect clip = null; int clipEnd = 0;
        foreach (var k in c.Children)
        {
            if (clip != null && k.Depth > clipEnd) clip = null;
            var e = SyncObject(k, masks, nine, blend);
            if (k.ClipDepth is int cd && k.Depth != int.MinValue)
            {
                // a timeline mask: following depths up to its clip depth draw only inside it; the mask itself is not drawn
                e.Mask ??= new MaskRect();
                e.Mask.Rect = Rect(k.Matrix.Apply(k.LocalBounds()));
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
                e.Mask ??= new MaskRect();
                e.Mask.Rect = Rect(k.MaskObject.GetBounds(c));
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

    static Rect2 Rect(RectD r) => r.IsEmpty ? new Rect2() : new Rect2((float)r.X, (float)r.Y, (float)r.W, (float)r.H);

    /// <summary>An image url as the movies build it (game: "img://icon:" + path, or a plain path) → a Resource/Image texture.</summary>
    Texture2D ResolveImage(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
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

/// <summary>Clips its children to a rectangle (a Flash mask, by its bounds); the rectangle itself is not shown.</summary>
public partial class MaskRect : Node2D
{
    Rect2 rect;
    public Rect2 Rect { get => rect; set { if (rect != value) { rect = value; QueueRedraw(); } } }
    public MaskRect() { ClipChildren = ClipChildrenMode.Only; }
    public override void _Draw() { if (rect.Size.X > 0 && rect.Size.Y > 0) DrawRect(rect, Colors.White); }
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
