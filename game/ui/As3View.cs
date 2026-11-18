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
    readonly HashSet<Entry> entries = new();
    readonly Dictionary<Node, Entry> byNode = new();          // each entry's holder and mask node
    readonly List<Node> detached = new();                     // nodes the sync took out of their parents (Sweep)
    readonly HashSet<Entry> scriptMasked = new();             // synced objects with a script mask
    static readonly HashSet<AsDisplayObject> noMasks = new();
    HashSet<AsDisplayObject> masks = noMasks;                 // their mask objects (not drawn)
    Transform2D viewXf;
    bool full = true;
    long changesSeen = -1;                                    // AsDisplayObject.Changes after the last sync
    int gen;
    double frameTime, caretTime;
    readonly TextCaret caret = new();

    public override void _Notification(int what)
    {
        // the caret is in the tree only on a focused field's holder, and goes with it; without a focus it is out of the
        // tree and goes with the view
        if (what == NotificationPredelete && IsInstanceValid(caret) && caret.GetParent() == null) caret.Free();
        else if (what == NotificationEnterTree) inTree.Add(this);
        else if (what == NotificationExitTree) inTree.Remove(this);
    }
    /// <summary>Advance the movie's frames in _Process (off for scripted captures that tick by hand).</summary>
    public bool AutoPlay = true;

    sealed class Entry
    {
        public AsDisplayObject Obj;
        public Node2D Holder;            // transform, visibility, colour
        public Node2D Content;           // what the object itself draws (shape, text, bitmap, graphics)
        public object ContentKey;
        public MaskShape Mask;           // set when this object masks following siblings or is masked by script
        public int Seen;                 // the last sync that walked it (MS2_PERF_SYNCCHECK)
        public bool ClipMask;            // a timeline mask (not drawn)
        public int Z;                    // the holder's z index (the dragged sprite's, see SyncObject)
        // the holder's transform, visibility and colour and the content's material as last set (the node defaults at first):
        // a property is set again only when it changes
        public Transform2D Xf = Transform2D.Identity;
        public bool Visible = true;
        public Color Modulate = Colors.White;
        public Material ContentMaterial;
    }

    /// <summary>
    /// The children each parent node was last given (<see cref="Apply"/>), in order: a parent given the same list again with
    /// the same child count keeps its children as they are, without reading them back or moving them.
    /// </summary>
    readonly Dictionary<Node, List<Node>> applied = new();

    public As3View(FlashPlayer player, UiAssets assets)
    {
        Player = player; this.assets = assets;
        player.Translate ??= key => assets.Str(key);
        player.ImageSize ??= url => ResolveImage(url) is { } t ? ((int)t.GetSize().X, (int)t.GetSize().Y) : null;
        // the runtime breaks lines and sizes fields with the advances of the game font the field draws in (unhinted, as
        // FieldNode draws it), measured at 8x the size so the whole-pixel font sizes do not round the advances
        TextMeasure.HeightRatio = font => assets.LineRatios(font).Height;
        TextMeasure.AscentRatio = font => assets.AscentRatio(font);
        player.ExportedImageSize ??= (movie, name) =>
            assets.ExportedImage(movie, player.Main.Key, name) is { } img && img.Region.Size.X > 0 ? (img.Region.Size.X, img.Region.Size.Y) : null;
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

    /// <summary>Milliseconds all movie views spent in <see cref="Sync"/> (without the caret and the check mode), and the movie frames ticked, since the start (frame-time runs).</summary>
    public static double SyncMs;
    public static long Ticks;

    /// <summary>Milliseconds all movie views spent in the 50 ms mouse-over sample (with the covered retest) and in the mouse
    /// moves they took, since the start (frame-time runs).</summary>
    public static double MouseMs;

    /// <summary>MS2_PERF: called once with the time of the next _Process (ticks and display-list sync), then cleared.</summary>
    public Action<double> NextProcessMs;

    public override void _Process(double delta)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        Advance(delta);
        double ms = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        SpentMs += ms;
        if (NextProcessMs is { } report) { NextProcessMs = null; report(ms); }
    }

    void Advance(double delta)
    {
        if (AutoPlay)
        {
            frameTime += delta;
            double step = 1.0 / Math.Max(1, Player.FrameRate);
            int n = 0;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            while (frameTime >= step && n++ < 4) { frameTime -= step; Player.Tick(); Ticks++; }
            TickMs += System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            if (frameTime > step) frameTime = 0;
        }
        caretTime += delta;
        Sync();
        long m0 = System.Diagnostics.Stopwatch.GetTimestamp();
        SampleMouseOver();
        MouseMs += System.Diagnostics.Stopwatch.GetElapsedTime(m0).TotalMilliseconds;
    }

    // ---- display list sync -------------------------------------------------------------------------------------

    /// <summary>
    /// Brings the nodes up to the display list. Only what the display objects marked since the last sync is walked (see
    /// <see cref="AsDisplayObject.MarkChanged"/>): a subtree with no mark below its root keeps its nodes as they are. A
    /// change of the screen scale (the view's or an ancestor node's transform) walks everything, since text is laid out
    /// and drawn at that scale. One view consumes a player's marks.
    /// </summary>
    public void Sync()
    {
        gen++;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var gx = IsInsideTree() ? GetGlobalTransform() : Transform;
        bool deep = full || gx.X != viewXf.X || gx.Y != viewXf.Y;
        full = false; viewXf = gx;
        if (deep) moves++;
        // a script mask's polygons are mapped through the mask's and the masked object's ancestors, which mark neither
        // the masked object nor always this stage (a mask out of the display list): after any mark anywhere the masked
        // objects are walked (those marks are this view's own: they are not counted as changes for the other views)
        if (AsDisplayObject.Changes != changesSeen && scriptMasked.Count > 0)
        {
            long changes = AsDisplayObject.Changes, own = Player.RT.DisplayMarks;
            foreach (var e in scriptMasked) e.Obj.MarkChanged();
            AsDisplayObject.Changes = changes; Player.RT.DisplayMarks = own;
        }
        var stage = Player.Stage;
        if (deep || stage.ChangedBelow)
        {
            bool kidsDeep = deep || stage.ChangedDeep;
            stage.ClearMarks();
            SyncChildren(stage, this, null, null, null, kidsDeep);
            Sweep();
            UpdateMasks();
        }
        changesSeen = AsDisplayObject.Changes;
        SyncMs += System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        var focus = Player.Focus as AsTextField;
        var fe = focus?.Tag as Entry;
        caret.Update(focus, fe?.Holder, fe?.Content, caretTime);
        caretTime = 0;
        if (SyncCheck) Check();
    }

    /// <summary>
    /// Frees the entries whose holders the sync took out of the view's nodes (a container's Apply or a button's state
    /// change detached them, and no other parent took them in): those objects left the display list. The nodes under a
    /// freed holder go with it, and so do their entries.
    /// </summary>
    void Sweep()
    {
        if (detached.Count == 0) return;
        foreach (var n in detached)
            if (byNode.TryGetValue(n, out var e) && entries.Contains(e) && !Attached(e.Holder)) Free(e);
        detached.Clear();
    }

    bool Attached(Node n)
    {
        for (var p = n.GetParent(); p != null; p = p.GetParent()) if (p == this) return true;
        return false;
    }

    void Free(Entry root)
    {
        var stack = new Stack<Node>();
        stack.Push(root.Holder);
        if (root.Mask != null) stack.Push(root.Mask);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (byNode.TryGetValue(n, out var e) && e.Holder == n) Drop(e);
            foreach (var ch in n.GetChildren()) stack.Push(ch);
        }
        root.Holder.GetParent()?.RemoveChild(root.Holder);
        root.Holder.QueueFree();
        root.Mask?.GetParent()?.RemoveChild(root.Mask);
        root.Mask?.QueueFree();
    }

    void Drop(Entry e)
    {
        entries.Remove(e);
        scriptMasked.Remove(e);
        byNode.Remove(e.Holder);
        applied.Remove(e.Holder);
        if (e.Mask != null) { byNode.Remove(e.Mask); applied.Remove(e.Mask); }
        if (ReferenceEquals(e.Obj.Tag, e)) e.Obj.Tag = null;
    }

    /// <summary>
    /// The objects used as script masks are not drawn. The set is the masks of the objects synced (kept current every
    /// sync, see <see cref="Sync"/>); an object that became or stopped being a mask during the pass is set again here.
    /// </summary>
    void UpdateMasks()
    {
        HashSet<AsDisplayObject> now = null;
        foreach (var e in scriptMasked) if (e.Obj.MaskObject is { } mo) (now ??= new()).Add(mo);
        now ??= noMasks;
        if (now.Count == 0 && masks.Count == 0) return;
        foreach (var o in masks) if (!now.Contains(o)) Revisible(o, now);
        foreach (var o in now) if (!masks.Contains(o)) Revisible(o, now);
        masks = now;
    }

    void Revisible(AsDisplayObject o, HashSet<AsDisplayObject> set)
    {
        if (o.Tag is not Entry e || !entries.Contains(e)) return;
        bool visible = o.IsVisible && !set.Contains(o) && !e.ClipMask;
        if (visible != e.Visible) { Checked(true, "mask visible", e); e.Holder.Visible = e.Visible = visible; }
    }

    /// <summary>
    /// Puts the nodes of a container's children under <paramref name="into"/> in display order (after the container's own
    /// <paramref name="content"/>); the children a timeline mask clips go under the mask's node, in display order. A child
    /// with no mark below it keeps its nodes; <paramref name="deep"/> walks every child (an ancestor's context changed).
    /// </summary>
    void SyncChildren(AsContainer c, Node2D into, Node2D content, NineCtx nine, Material blend, bool deep)
    {
        var wanted = new List<Node>();
        if (content != null) wanted.Add(content);
        MaskShape clip = null; List<Node> clipped = null; int clipEnd = 0;
        List<(MaskShape Mask, List<Node> Kids)> clips = null;
        Mat? toC = null;
        foreach (var k in c.Children)
        {
            if (clip != null && k.Depth > clipEnd) clip = null;
            bool clipMask = k.ClipDepth is int && k.Depth != int.MinValue;
            bool visit = deep || k.ChangedBelow || k.Tag is not Entry;
            var e = visit ? SyncObject(k, nine, blend, clipMask, deep) : (Entry)k.Tag;
            if (clipMask)
            {
                // a timeline mask: following depths up to its clip depth draw only inside it; the mask itself is not drawn
                if (e.Mask == null) { e.Mask = new MaskShape(); byNode[e.Mask] = e; }
                if (visit) Checked(e.Mask.Set(MaskShape.Polygons(k, Mat.Identity)), "clip mask polygons", e);
                wanted.Add(e.Holder);
                wanted.Add(e.Mask);
                clip = e.Mask; clipEnd = (int)k.ClipDepth;
                (clips ??= new()).Add((clip, clipped = new List<Node>()));
                continue;
            }
            Node2D top = e.Holder;
            if (k.MaskObject != null)
            {
                if (e.Mask == null) { e.Mask = new MaskShape(); byNode[e.Mask] = e; }
                var mo = k.MaskObject;
                toC ??= c.ToGlobalMatrix().Inverse();
                Checked(e.Mask.Set(MaskShape.Polygons(mo, (mo.Parent?.ToGlobalMatrix() ?? Mat.Identity).Then(toC.Value))), "script mask polygons", e);
                if (e.Holder.GetParent() != e.Mask) { Checked(true, "masked holder parent", e); e.Holder.GetParent()?.RemoveChild(e.Holder); e.Mask.AddChild(e.Holder); }
                top = e.Mask;
            }
            if (clip != null) clipped.Add(top);
            else wanted.Add(top);
        }
        if (clips != null) foreach (var (mask, kids) in clips) Apply(mask, kids);
        Apply(into, wanted);
    }

    /// <summary>
    /// Makes <paramref name="want"/> the children of <paramref name="into"/>, in that order: nodes from elsewhere are moved
    /// in, others are dropped (their entries are freed after the pass if no other parent took them, <see cref="Sweep"/>).
    /// Skipped when the parent was given the same list last time and still has as many children (every move between
    /// parents goes through here, and the parent losing a node is given a list without it).
    /// </summary>
    void Apply(Node2D into, List<Node> want)
    {
        if (applied.TryGetValue(into, out var last) && last.Count == want.Count && into.GetChildCount() == want.Count)
        {
            bool same = true;
            for (int i = 0; same && i < want.Count; i++) same = ReferenceEquals(last[i], want[i]);
            if (same) return;
        }
        applied[into] = want;
        var keep = new HashSet<Node>(want);
        bool changed = false;
        foreach (var n in want)
            if (n.GetParent() != into) { n.GetParent()?.RemoveChild(n); into.AddChild(n); changed = true; }
        foreach (var n in into.GetChildren())
            if (!keep.Contains(n)) { into.RemoveChild(n); detached.Add(n); changed = true; }
        for (int i = 0; i < want.Count; i++)
        {
            if (checking && !changed && into.GetChild(i) != want[i]) changed = true;
            into.MoveChild(want[i], i);
        }
        Checked(changed, "children", into);
    }

    static void Reparent(Node2D n, Node2D into, List<Node> wanted)
    {
        if (n.GetParent() != into) { n.GetParent()?.RemoveChild(n); into.AddChild(n); }
        wanted.Add(n);
    }

    /// <summary>
    /// Syncs one display object's holder and content (when it or an ancestor's context is marked) and walks into its
    /// marked children; the object's marks are cleared.
    /// </summary>
    Entry SyncObject(AsDisplayObject o, NineCtx nine, Material blend, bool clipMask, bool deep)
    {
        bool fresh = o.Tag is not Entry e;
        bool self = deep || fresh || o.ChangedSelf;
        bool kidsDeep = deep || fresh || o.ChangedDeep;
        o.ClearMarks();
        blend = BlendFor(o.Blend) ?? blend;
        if (fresh)
        {
            Checked(true, "unsynced object", o);
            e = new Entry { Obj = o, Holder = new Node2D { Name = string.IsNullOrEmpty(o.InstanceName) ? o.Class?.LocalName ?? "obj" : o.InstanceName } };
            o.Tag = e; entries.Add(e); byNode[e.Holder] = e;
        }
        else e = (Entry)o.Tag;
        e.Seen = gen;
        var m = o.Matrix;
        // Scale-9: every node under a grid sprite keeps its matrix; only shape meshes are made through the grid, from
        // the shape's matrices up to the nearest grid sprite (NF_PartOfScale9 passes down to every child,
        // TreeShape::NodeData::updateCache sfr_render_03f.cpp:183; TreeCacheShapeLayer::calcMeshKey :534 stops at the
        // first node with a grid). Text is not a shape (TreeCacheText, sfr_render_04l.cpp:115) and is drawn plainly.
        NineCtx childNine = nine?.With(Xf(m.Then(FromXf(nine.Chain.Value))));
        if (o.Symbol?["scale9"] is JsonObject g9 && o is AsContainer)
        {
            // a grid at scale 1 maps nothing, but it still ends the outer grid's reach
            double sx = Math.Sqrt(m.A * m.A + m.B * m.B), sy = Math.Sqrt(m.C * m.C + m.D * m.D);
            childNine = Math.Abs(sx - 1) > 1e-3 || Math.Abs(sy - 1) > 1e-3
                ? new NineCtx { Grid = UiBuilder.Rc(g9), Bounds = UiBuilder.Rc(o.Symbol["bounds"]), Sx = (float)sx, Sy = (float)sy, Chain = Transform2D.Identity }
                : null;
        }
        if (self)
        {
            var xf = Xf(m);
            if (xf != e.Xf) { Checked(true, "transform", e); e.Holder.Transform = e.Xf = xf; }
            // a timeline mask's own node is not drawn (SyncChildren clips the following depths to it)
            e.ClipMask = clipMask;
            bool visible = o.IsVisible && !masks.Contains(o) && !clipMask;
            if (visible != e.Visible) { Checked(true, "visible", e); e.Holder.Visible = e.Visible = visible; }
            // DragManager's drag canvas is CommonDepth's layer under the mouse cursor (DragManager.init: DEPTH_LAYER_MOUSECURSOR
            // - 1) on the one stage every dialog shares: the dragged sprite is drawn over the other movies too, not only its own
            int z = o == Player.DrawnOnTop ? (int)RenderingServer.CanvasItemZMax - 1 : 0;
            if (z != e.Z) { Checked(true, "z", e); e.Holder.ZAsRelative = z == 0; e.Holder.ZIndex = e.Z = z; }
            var cx = o.Color;
            var modulate = new Color((float)Math.Clamp(cx.Rm + cx.Ra / 255, 0, 1), (float)Math.Clamp(cx.Gm + cx.Ga / 255, 0, 1), (float)Math.Clamp(cx.Bm + cx.Ba / 255, 0, 1), (float)Math.Clamp(cx.Am + cx.Aa / 255, 0, 1));
            if (modulate != e.Modulate) { Checked(true, "modulate", e); e.Holder.Modulate = e.Modulate = modulate; }
            SyncContent(e, nine);
            if (e.Content != null && e.ContentMaterial != blend)
            {
                Checked(true, "material", e);
                e.Content.Material = e.ContentMaterial = blend;
                foreach (var ch in e.Content.GetChildren()) if (ch is CanvasItem ci) ci.UseParentMaterial = true;   // ShapeNode's bitmap sprites
            }
            if (o.MaskObject != null) scriptMasked.Add(e);
            else scriptMasked.Remove(e);
        }
        switch (o)
        {
            case AsContainer c: SyncChildren(c, e.Holder, e.Content, childNine, blend, kidsDeep); break;
            case AsSimpleButton b:
            {
                var wanted = new List<Node>();
                if (e.Content != null) wanted.Add(e.Content);
                foreach (var s in b.Drawn)
                {
                    var se = kidsDeep || s.ChangedBelow || s.Tag is not Entry ? SyncObject(s, childNine, blend, false, kidsDeep) : (Entry)s.Tag;
                    if (se.Holder.GetParent() != e.Holder) Checked(true, "button state", e);
                    Reparent(se.Holder, e.Holder, wanted);
                }
                foreach (var n in e.Holder.GetChildren())
                    if (!wanted.Contains(n)) { Checked(true, "button state", e); e.Holder.RemoveChild(n); detached.Add(n); }
                break;
            }
        }
        return e;
    }

    // ---- MS2_PERF_SYNCCHECK ----------------------------------------------------------------------------------------

    /// <summary>MS2_PERF_SYNCCHECK: after every sync, a full sync checks that the marked one left nothing to change.</summary>
    static readonly bool SyncCheck = !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("MS2_PERF_SYNCCHECK"));

    /// <summary>MS2_PERF_SYNCCHECK: differences the full syncs found, and the full syncs run, over every view.</summary>
    public static long CheckDiffs, CheckRuns;
    bool checking;

    /// <summary>
    /// The full sync as it was before the marks (every object walked, the masks collected from the whole display list,
    /// the entries not reached freed), run right after the marked sync: whatever it still has to change is logged.
    /// </summary>
    void Check()
    {
        checking = true;
        try
        {
            gen++;
            var all = new HashSet<AsDisplayObject>();
            CollectMasks(Player.Stage, all);
            if (!all.SetEquals(masks)) Checked(true, "script mask set", null);
            masks = all;
            Player.Stage.ClearMarks();
            SyncChildren(Player.Stage, this, null, null, null, true);
            foreach (var e in entries) if (e.Seen != gen) Checked(true, "entry kept for an object not in the display list", e);
            Sweep();
            UpdateMasks();
            CheckRuns++;
        }
        finally { checking = false; }
    }

    static void CollectMasks(AsDisplayObject o, HashSet<AsDisplayObject> masks)
    {
        if (o.MaskObject != null) masks.Add(o.MaskObject);
        if (o is AsContainer c) foreach (var k in c.Children) CollectMasks(k, masks);
    }

    /// <summary>In the check sync, logs a change it made (the marked sync missed it).</summary>
    void Checked(bool changed, string what, object at)
    {
        if (!checking || !changed) return;
        if (CheckDiffs++ < 200)
        {
            var o = (at as Entry)?.Obj ?? at as AsDisplayObject;
            string where = o != null ? $"{o.Class?.LocalName} {o.InstanceName}" : at is Node n ? n.Name.ToString() : "";
            GD.PrintErr($"MS2_PERF_SYNCCHECK {Player.Main?.Key}: {what} at {where}");
        }
    }

    static Transform2D Xf(Mat m) => new(new Vector2((float)m.A, (float)m.B), new Vector2((float)m.C, (float)m.D), new Vector2((float)m.Tx, (float)m.Ty));
    static Mat FromXf(Transform2D t) => new(t.X.X, t.X.Y, t.Y.X, t.Y.Y, t.Origin.X, t.Origin.Y);

    void SyncContent(Entry e, NineCtx nine)
    {
        var o = e.Obj;
        // the key and the node are picked by the same cases (ContentKey, MakeContent); the node is made only when the key
        // changed, so a frame with nothing new allocates nothing here
        object key = ContentKey(o, nine, out var sn);
        if (!Equals(key, e.ContentKey))
        {
            Checked(true, "content", e);
            if (e.Content != null) { e.Content.GetParent()?.RemoveChild(e.Content); e.Content.QueueFree(); e.Content = null; }
            e.ContentKey = key;
            e.Content = MakeContent(o, sn);
            if (e.Content != null) e.Holder.AddChild(e.Content);
            e.ContentMaterial = e.Content?.Material;
        }
        if (e.Content is GraphicsNode or FieldNode) e.Content.QueueRedraw();
        // a field made in this pass builds once its holder is in the tree (FieldText's TreeEntered), so the check skips it
        if (e.Content is FieldText ft) { bool pending = !ft.Built; Checked(ft.Sync() && !pending, "text", e); }
    }

    /// <summary>
    /// What a display object's own content is drawn from: its node is rebuilt when this changes. A shape under a scale-9
    /// grid also keys on the grid it is drawn through (<paramref name="sn"/>); outside a grid its symbol alone is the key.
    /// </summary>
    static object ContentKey(AsDisplayObject o, NineCtx nine, out NineCtx sn)
    {
        sn = null;
        switch (o)
        {
            case AsShape sh when sh.GraphicsOrNull is { Ops.Count: > 0 } g: return g;
            case AsShape or AsMorphShape when o.Symbol != null && o.Scope != null:
                sn = nine?.With(Xf(o.Matrix.Then(FromXf(nine.Chain.Value))));
                return sn == null ? o.Symbol : (o.Symbol, sn.Grid, sn.Bounds, sn.Sx, sn.Sy, sn.Chain);
            case AsTextField tf: return tf;
            case AsStaticText when (o.Symbol?["text"]) != null && o.Scope != null: return o.Symbol;
            case AsBitmap bm when bm.bitmapData?.Source is string url: return (url, bm.bitmapData.width, bm.bitmapData.height);
            case AsBitmap ib when ib.bitmapData?.ImageScope is { } isc: return (isc, ib.bitmapData.ImageId);
            case AsSprite sp when sp.GraphicsOrNull is { Ops.Count: > 0 } g2: return g2;
            default: return null;
        }
    }

    /// <summary>The node for a display object's content, by the cases of <see cref="ContentKey"/>.</summary>
    Node2D MakeContent(AsDisplayObject o, NineCtx sn)
    {
        switch (o)
        {
            case AsShape sh when sh.GraphicsOrNull is { Ops.Count: > 0 } g: return new GraphicsNode(g);
            case AsShape or AsMorphShape when o.Symbol != null && o.Scope != null: return new ShapeNode(Scope(o.Scope), o.Symbol, sn);
            case AsTextField tf when (tf.Symbol?["text"] is JsonObject) && o.Scope != null: return new FieldText(tf, Scope(o.Scope));
            case AsTextField tf: return new FieldNode(tf, assets);
            case AsStaticText when (o.Symbol?["text"]) != null && o.Scope != null: return new TextNode(Scope(o.Scope), o.Symbol, 1, 1);
            case AsBitmap bm when bm.bitmapData?.Source is string url:
                return ResolveImage(url) is { } t ? new Sprite2D { Texture = t, Centered = false, Scale = Fit(t, bm.bitmapData) } : null;
            case AsBitmap ib when ib.bitmapData?.ImageScope is { } isc:
                // an image resource placed on a timeline (Player.Instantiate): its atlas region at its own size
                return Scope(isc).Bitmap(ib.bitmapData.ImageId) is { } r
                    ? new Sprite2D { Texture = r.Tex, Centered = false, RegionEnabled = true, RegionRect = r.Region } : null;
            case AsSprite sp when sp.GraphicsOrNull is { Ops.Count: > 0 } g2: return new GraphicsNode(g2);
            default: return null;
        }
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
        if (!scopes.TryGetValue(s, out var u)) scopes[s] = u = new UiScope(s.Layout, assets) { MovieKey = s.Key, RootKey = Player.Main.Key };
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
        // GetProfileImagePath's "./Custom/Download/Profile/<file>": a downloaded profile picture (none until it is in)
        if (Ms2.Ui.ProfileImages.FileOfPath(url) is { } profile) return Net.ProfileImageService.Texture(profile);
        var p = url;
        if (p.StartsWith("img://", StringComparison.OrdinalIgnoreCase)) { int c = p.IndexOf(':', 6); p = c >= 0 ? p[(c + 1)..] : p[6..]; }
        p = p.Replace('\\', '/').TrimStart('.', '/');
        int at = p.IndexOf("resource/image/", StringComparison.OrdinalIgnoreCase);
        if (at >= 0) p = p[(at + "resource/image/".Length)..];
        return assets.ImageFile(p.ToLowerInvariant());
    }

    // ---- input ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Whether the cursor is over the movie, as the game last sampled it. CUIService::FUN_141aec760 (uiservice.cpp:2896-2910)
    /// hit-tests the UI movie at the cursor when more than 50 ms have passed since the last test (MovieImpl::HitTest,
    /// HitTest_ShapesNoInvisible) and stores the result on the movie (GFxManager::FUN_1422aef20). The game's mouse handling
    /// returns early while it is set: CSceneStateGame::OnWndMessage for moves and double clicks (scenestategame.cpp:2034-2048)
    /// and CMouseController::OnMouseMessage for every mouse message (mousecontroller.cpp:336-344). The movies get every
    /// mouse message either way (GFxManager::HandleWindowMessage, gfxmanager.cpp:952-966).
    /// </summary>
    public bool MouseOverUi { get; private set; }
    ulong lastMouseTest;

    void SampleMouseOver()
    {
        ulong now = Time.GetTicksMsec();
        if (now - lastMouseTest <= 50) return;
        lastMouseTest = now;
        // a view leaving the tree (a scene change) has no viewport: no sample
        if (!IsInsideTree() || GetViewport() is not { } vp) { MouseOverUi = false; return; }
        var p = StagePoint(vp.GetMousePosition());
        MouseOverUi = IsVisibleInTree() && Player.HitTestShapes(p.X, p.Y);
        // Flag_NeedMouseUpdate (set after each advanced frame, sfr_gfx_01i.cpp:4107): the mouse's top-most entity is found
        // again at its last position (a drag keeps its own). The answer can only differ when this movie's display objects
        // were marked (its runtime's DisplayMarks; the script-mask re-marks after other movies' marks are not counted), the
        // view moved on screen, or another movie's object came over the mouse or left it, so only then is it searched again.
        if (Player.Dragging || !Player.MouseActivated) return;
        bool covered = Covered(vp);
        long marks = Player.RT.DisplayMarks;
        if (marks == retestMarks && moves == retestMoves && covered == retestCovered) return;
        retestMarks = marks; retestMoves = moves; retestCovered = covered;
        Retests++;
        long r0 = System.Diagnostics.Stopwatch.GetTimestamp();
        Player.MouseRetest(covered);
        RetestMs += System.Diagnostics.Stopwatch.GetElapsedTime(r0).TotalMilliseconds;
    }

    // ---- the movies' one stage ---------------------------------------------------------------------------------------
    //
    // The client's dialogs are all in main.gfx's one stage (GFxMovieMain), so a dialog over another one takes the mouse
    // from it: the top-most entity is the upper dialog's object, and what the mouse was over below rolls out (the
    // TooltipManager's ROLL_OUT then destroys its tooltip). Here each movie has its own stage, so a view told that another
    // movie's object is over the mouse (Covered) gives its player no top-most entity there. The tooltip movie (no input,
    // its own movie in the client too) covers nothing.

    static readonly HashSet<As3View> inTree = new();
    long moves;                                             // the syncs that found the view moved on screen (a deep sync)
    long retestMarks = -1, retestMoves;                     // DisplayMarks and moves at the last retest
    /// <summary>The mouse retests all movie views made since the start, and the milliseconds they took (frame-time runs).</summary>
    public static long Retests;
    public static double RetestMs;
    bool retestCovered;                                     // whether another movie covered the point at the last retest

    /// <summary>Whether the view takes the mouse: in the tree, shown, its input on.</summary>
    bool TakesMouse => Player != null && IsInsideTree() && IsVisibleInTree() && IsProcessingUnhandledInput();

    /// <summary>Whether this view is drawn over the other: a higher canvas layer, or on the same one later in the tree.</summary>
    bool DrawnOver(As3View other)
    {
        int a = GetCanvasLayerNode()?.Layer ?? 0, b = other.GetCanvasLayerNode()?.Layer ?? 0;
        return a != b ? a > b : IsGreaterThan(other);
    }

    /// <summary>Whether another view drawn over this one had its shapes under the mouse at its last 50 ms sample
    /// (<see cref="MouseOverUi"/>, CUIService's hit test): no hit test of its own, so a view that covers nothing costs a
    /// flag read.</summary>
    bool Covered(Viewport vp)
    {
        foreach (var v in inTree)
            if (v != this && v.MouseOverUi && v.TakesMouse && v.GetViewport() == vp && v.DrawnOver(this)) return true;
        return false;
    }

    /// <summary>A mouse move this view takes (its shapes are under the mouse): the views it is drawn over do not see the
    /// event, so they get it covered.</summary>
    void CoverBelow(Vector2 viewport)
    {
        var vp = GetViewport();
        foreach (var v in inTree)
            if (v != this && v.TakesMouse && v.GetViewport() == vp && DrawnOver(v) && !v.Player.Dragging)
            {
                var p = v.StagePoint(viewport);
                v.Player.MouseMove(p.X, p.Y, covered: true);
            }
    }

    /// <summary>A viewport point in stage units (through the canvas layer the view is drawn in).</summary>
    Vector2 StagePoint(Vector2 viewport) => GetGlobalTransformWithCanvas().AffineInverse() * viewport;

    /// <summary>The mouse in stage units, (NaN, NaN) out of the tree.</summary>
    public (double X, double Y) StageMouse()
    {
        if (!IsInsideTree() || GetViewport() is not { } vp) return (double.NaN, double.NaN);
        var p = StagePoint(vp.GetMousePosition());
        return (p.X, p.Y);
    }

    /// <summary>
    /// While this movie drags a sprite (DragManager's startDrag) it gets the mouse before any other movie: the client's
    /// movies share one stage, so the drag follows the mouse over other dialogs and the release ends it
    /// (DragManager.handleEndDragEvent on the stage's MOUSE_UP) wherever it happens.
    /// </summary>
    public override void _Input(InputEvent ev)
    {
        // no capture for a hidden view or a drag sprite that left the stage
        if (Player == null || !Player.Dragging || !IsVisibleInTree() || Player.DraggedSprite is not { OnStage: true }) return;
        switch (ev)
        {
            // the button came up where this window did not see it (focus lost: Godot releases the pressed buttons, or another
            // window): the release, here
            case InputEventMouseMotion mm when !Input.IsMouseButtonPressed(MouseButton.Left):
            {
                var p = StagePoint(mm.Position);
                Player.MouseMove(p.X, p.Y);
                Player.MouseButton(false);
                break;
            }
            case InputEventMouseMotion mm:
            {
                var p = StagePoint(mm.Position);
                Player.MouseMove(p.X, p.Y);
                break;
            }
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } mb:
            {
                var p = StagePoint(mb.Position);
                Player.MouseMove(p.X, p.Y);
                Player.MouseButton(false);
                break;
            }
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        switch (ev)
        {
            case InputEventMouseMotion mm:
            {
                long m0 = System.Diagnostics.Stopwatch.GetTimestamp();
                var p = StagePoint(mm.Position);
                Player.MouseMove(p.X, p.Y);
                if (MouseOverUi) CoverBelow(mm.Position);
                MouseMs += System.Diagnostics.Stopwatch.GetElapsedTime(m0).TotalMilliseconds;
                if (!MouseOverUi) return;
                break;
            }
            case InputEventMouseButton mb:
            {
                var p = StagePoint(mb.Position);
                Player.MouseMove(p.X, p.Y);
                if (mb.ButtonIndex == MouseButton.Left) Player.MouseButton(mb.Pressed);
                else if (mb.ButtonIndex == MouseButton.Right) Player.MouseButton(mb.Pressed, FlashPlayer.RightButton);    // WM_RBUTTONDOWN/UP
                else if (mb.ButtonIndex == MouseButton.Middle) Player.MouseButton(mb.Pressed, FlashPlayer.MiddleButton);
                else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp) Player.Wheel(3);
                else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown) Player.Wheel(-3);
                if (!MouseOverUi) return;   // the click goes on to the game
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

    /// <summary>The mask's polygons this frame; redrawn only when they changed (then true).</summary>
    public bool Set(List<Vector2[]> p)
    {
        bool same = p.Count == polys.Count;
        for (int i = 0; same && i < p.Count; i++) same = p[i].AsSpan().SequenceEqual(polys[i]);
        if (same) return false;
        polys = p;
        QueueRedraw();
        return true;
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

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<JsonArray, double[]> points = new();

    /// <summary>A layout path's point list as numbers, read once per path.</summary>
    static double[] Points(JsonArray a) => points.GetValue(a, x => x.Select(v => (double)v).ToArray());

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
                var d = Points(a);
                var pts = new Vector2[d.Length / 2];
                for (int i = 0; i < pts.Length; i++) pts[i] = P(d[2 * i], d[2 * i + 1]);
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
    /// <summary>The text node was built (a field made in a sync pass builds once it is in the tree).</summary>
    public bool Built => built != null;
    string built;
    int version = -1;

    public FieldText(AsTextField tf, UiScope scope)
    {
        this.tf = tf; this.scope = scope;
        // a field made during a sync pass is synced before its holder joins the tree: it syncs once it is in
        TreeEntered += () => Callable.From(() => { Sync(); }).CallDeferred();
    }

    /// <summary>Builds the text node again or sets its text when the field changed; true when it did either.</summary>
    public bool Sync()
    {
        if (!IsInsideTree()) return false;
        var gs = GetGlobalTransform().Scale;
        float sx = Math.Max(0.001f, Math.Abs(gs.X)), sy = Math.Max(0.001f, Math.Abs(gs.Y));
        var f = tf.Format;
        string D(object v) => v == null || Rt.IsNullish(v) ? "" : Rt.ToString(v);
        var filters = Filters();
        string key = string.Join("|", tf.Box.X, tf.Box.Y, tf.Box.W, tf.Box.H, D(f?.font), D(f?.size), D(f?.color), D(f?.align),
            D(f?.leading), tf.wordWrap, tf.multiline, MathF.Round(sx * 1000), MathF.Round(sy * 1000), filters?.ToJsonString(), tf.antiAliasType, tf.TextAutoSize);
        bool rebuilt = key != built;
        if (rebuilt)
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
            def["readable"] = tf.antiAliasType == "advanced" ? true : null;
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
        if (tf.Version == version) return rebuilt;
        version = tf.Version;
        if (tf.Html != null && !(tf.Html.Length > 1 && tf.Html[0] == '$')) node.SetHtml(tf.Html);
        else node.Text = tf.Text;
        // TextFieldEx.setTextAutoSize(SHRINK / FIT): LineBuffer::Scale (sfr_render_04d.cpp:394) scales every line's offsets and
        // sizes and every glyph from the text rect's origin, keeping the lines' breaks
        float scale = tf.TextAutoSize is "shrink" or "fit" ? node.TextAutoSizeScale(tf.TextAutoSize) : 1;
        var origin = new Vector2((float)tf.Box.X + 2, (float)tf.Box.Y + 2);
        node.Scale = new Vector2(scale, scale);
        node.RecenterScaled(scale);
        node.Position = origin - origin * scale + new Vector2(0, (float)(tf.FirstLineTop(node.NumLines, scale) - (tf.Box.Y + 2)));
        return true;
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
