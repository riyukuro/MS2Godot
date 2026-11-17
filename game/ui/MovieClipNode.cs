using System.Text.Json.Nodes;
using Godot;

namespace Ms2Godot.Ui;

/// <summary>
/// A flash sprite: children follow the display-list lifespans of its timeline; <see cref="GotoFrame"/> and
/// <see cref="GotoLabel"/> choose which are visible and where. Widgets (button, checkbox, text input) attach to it.
/// </summary>
/// <summary>Clips its content to the (axis aligned) rectangle of a flash mask shape, following the mask when code moves or scales it.</summary>
public partial class MaskGroup : Control
{
    public readonly Node2D Inner = new();
    readonly MovieClipNode.Child mask;

    public MaskGroup(MovieClipNode.Child mask)
    {
        this.mask = mask;
        ClipContents = true; MouseFilter = MouseFilterEnum.Ignore;
        AddChild(Inner);
        Update();
    }

    public void Update()
    {
        var xf = mask.Node.Transform;
        bool visible = mask.Node.Visible;
        if (applied && xf == lastXf && visible == lastVisible) return;     // nothing moved: no property writes
        applied = true; lastXf = xf; lastVisible = visible;
        var b = mask.MaskBounds;
        var r = new Rect2(xf * b.Position, Vector2.Zero);
        r = r.Expand(xf * new Vector2(b.End.X, b.Position.Y)).Expand(xf * new Vector2(b.Position.X, b.End.Y)).Expand(xf * b.End);
        if (!visible) r = new Rect2(-100000, -100000, 200000, 200000);   // mask not on the stage: no clipping
        Position = r.Position; Size = r.Size;
        Inner.Position = -r.Position;
    }

    // The mask's transform and visibility change only in MovieClipNode.GotoFrame (timeline moves, Override, SetChildX,
    // HideNamed all end there), which calls Update for its groups; nothing has to run per frame.
    bool applied, lastVisible; Transform2D lastXf;
}

public partial class MovieClipNode : Node2D
{
    public sealed class Child
    {
        public JsonObject Def;
        public Node2D Node;
        public string Name;
        public int First, Last = int.MaxValue;
        public Transform2D Base;
        public Color BaseColor = Colors.White;
        public Transform2D? Override;      // set by code (bar fill masks)
        public bool Hidden;                // set by code (AS3 visible = false): kept over timeline moves
        public bool IsMask;
        public Rect2 MaskBounds;
        public bool Static9;              // drawn by a Scale9Node: ignores later matrix moves
        public List<(int Frame, Transform2D? M, Color? C, bool? Visible)> Moves = new();
    }

    public UiMovie Movie;
    public UiScope Scope;
    public JsonObject Timeline, Symbol;
    public int SymbolId;
    public string ClassName;
    public List<string> Bases = new();
    /// <summary>The symbol's class has frame code (addFrameScript); without it the timeline never stops.</summary>
    public bool FrameScripts;
    public Widget Widget;
    public Rect2 Bounds;
    public int Frame { get; private set; }
    public int FrameCount;
    public readonly Dictionary<string, int> Labels = new();
    public readonly List<Child> Children = new();
    public readonly double sx, sy;

    readonly NineCtx nine;

    public MovieClipNode(UiMovie movie, UiScope scope, JsonObject timeline, JsonObject symbol, double sx, double sy, NineCtx nine = null)
    {
        this.nine = nine;
        SetProcess(false);     // idle until PlayRange/Loop starts a timeline (see _Process)
        Movie = movie; Scope = scope; Timeline = timeline; Symbol = symbol; this.sx = sx; this.sy = sy;
        ClassName = (string)symbol?["class"];
        if (symbol?["bases"] is JsonArray b) foreach (var x in b) Bases.Add((string)x);
        FrameScripts = (bool?)symbol?["frameScripts"] ?? false;
        Bounds = UiBuilder.Rc(symbol?["bounds"]);
        if (nine != null && symbol?["scale9"] != null) Bounds = nine.Map(Bounds);
        FrameCount = timeline == null ? 1 : (int)(timeline["frames"] ?? 1);
        if (timeline?["labels"] is JsonArray ls) foreach (var l in ls) Labels.TryAdd((string)l["name"], (int)l["frame"]);
    }

    /// <summary>SWF blend modes on a placed child: add / screen / lighten draw additively, multiply and subtract as Godot's; the rest normally.</summary>
    static void ApplyBlend(Node node, int mode)
    {
        CanvasItemMaterial.BlendModeEnum? b = mode switch
        {
            3 => CanvasItemMaterial.BlendModeEnum.Mul,
            4 or 5 or 8 => CanvasItemMaterial.BlendModeEnum.Add,
            9 => CanvasItemMaterial.BlendModeEnum.Sub,
            _ => null,
        };
        if (b == null) return;
        var mat = new CanvasItemMaterial { BlendMode = b.Value };
        void Set(Node n)
        {
            if (n is CanvasItem ci && ci.Material == null) ci.Material = mat;
            foreach (var ch in n.GetChildren()) Set(ch);
        }
        Set(node);
    }

    public bool IsA(string cls) => ClassName == cls || Bases.Contains(cls);

    public void Build()
    {
        MaskGroup group = null; int groupEnd = 0;
        if (Timeline?["children"] is JsonArray ch)
            foreach (var c in ch.OrderBy(x => (int)x["depth"]).ThenBy(x => (int)x["first"]))
            {
                int id = c["id"] == null ? -1 : (int)c["id"];
                var m = UiBuilder.ToTransform(c["matrix"]);
                double csx = Math.Sqrt(m.X.X * m.X.X + m.X.Y * m.X.Y), csy = Math.Sqrt(m.Y.X * m.Y.X + m.Y.Y * m.Y.Y);
                var childSym = Scope.Symbol(id);
                Node2D node; bool nine9 = false;
                if (childSym != null && childSym["scale9"] != null && (string)childSym["kind"] == "sprite" && (Math.Abs(csx - 1) > 1e-3 || Math.Abs(csy - 1) > 1e-3))
                {
                    var ctx = new NineCtx { Grid = UiBuilder.Rc(childSym["scale9"]), Bounds = UiBuilder.Rc(childSym["bounds"]), Sx = (float)csx, Sy = (float)csy };
                    node = UiBuilder.BuildSprite(Movie, Scope, id, sx, sy, ctx);
                    nine9 = true;
                    m = new Transform2D(Vector2.Right, Vector2.Down, m.Origin);
                }
                else if (id < 0) node = Movie.BuildClass((string)c["class"], sx * csx, sy * csy) ?? Movie.Assets.Common.BuildClass((string)c["class"], sx * csx, sy * csy) ?? new MovieClipNode(Movie, Scope, null, null, sx * csx, sy * csy) { ClassName = (string)c["class"] };   // placed by class name (an imported library symbol)
                else node = UiBuilder.Build(Movie, Scope, id, sx * csx, sy * csy, nine?.At(nine.Offset + m.Origin));
                if (node == null) continue;
                var child = new Child
                {
                    Def = (JsonObject)c, Node = node, Name = (string)c["name"], First = (int)c["first"], Static9 = nine9,
                    Last = c["last"] == null ? int.MaxValue : (int)c["last"], Base = m,
                };
                if (c["color"] != null) child.BaseColor = ToColor(c["color"]);
                if (c["moves"] is JsonArray mv)
                    foreach (var mo in mv)
                    {
                        Transform2D? t = (bool)mo["hasMatrix"] && !nine9 && nine == null ? UiBuilder.ToTransform(mo["matrix"]) : null;
                        child.Moves.Add(((int)mo["frame"], t, mo["color"] != null ? ToColor(mo["color"]) : null, mo["visible"] == null ? null : (bool)mo["visible"]));
                    }
                Children.Add(child);
                int depth = (int)c["depth"];
                if (group != null && depth > groupEnd) group = null;
                if (c["clipDepth"] != null)
                {
                    // a mask: its rectangle clips the following depths; the mask itself is not drawn
                    child.IsMask = true; child.MaskBounds = UiBuilder.Rc(childSym?["bounds"]);
                    var holder = new Node2D { Visible = false };
                    holder.AddChild(node);
                    group = new MaskGroup(child);
                    maskGroups.Add(group);
                    groupEnd = (int)c["clipDepth"];
                    AddChild(group); group.AddChild(holder);
                }
                else if (group != null) group.Inner.AddChild(node);
                else AddChild(node);
                if (node is MovieClipNode mc)
                {
                    UiWidgets.Attach(mc);
                    if (c["filters"] != null) foreach (var sub in mc.AllClips()) foreach (var ch2 in sub.Children) if (ch2.Node is TextNode t2) t2.ApplyFilters(c["filters"], (float)(sy * csy));
                }
                else if (node is TextNode tn && c["filters"] != null) tn.ApplyFilters(c["filters"], (float)(sy * csy));
                if (c["blend"] != null) ApplyBlend(node, (int)c["blend"]);
            }
        GotoFrame(0);
    }

    static Color ToColor(JsonNode c) => new((float)(double)c["mul"][0], (float)(double)c["mul"][1], (float)(double)c["mul"][2], (float)(double)c["mul"][3]);

    /// <summary>Goes to a state label. A state plays from its label to the frame before the next label and holds there (CLIK stops at the end of each state); with <paramref name="animate"/> the frames are played at the movie's frame rate.</summary>
    public void GotoLabel(string label, bool animate = false)
    {
        if (!Labels.TryGetValue(label, out var f)) return;
        int end = FrameCount - 1;
        foreach (var l in Labels.Values) if (l > f && l - 1 < end) end = l - 1;
        if (animate && end > f && IsInsideTree()) PlayRange(f, end); else { playing = false; SetProcess(false); GotoFrame(end); }
    }

    /// <summary>The child named <paramref name="name"/> in this clip or, failing that, in any clip below it.</summary>
    public Child ChildNamed(string name)
    {
        foreach (var c in AllClips()) { var ch = c.Children.FirstOrDefault(x => x.Name == name); if (ch != null) return ch; }
        return null;
    }

    /// <summary>Takes every child with this instance name, anywhere below, off the stage.</summary>
    public void HideNamed(string name)
    {
        foreach (var c in AllClips().ToList())
        {
            bool any = false;
            foreach (var ch in c.Children) if (ch.Name == name) { ch.Last = -1; any = true; }
            if (any) c.GotoFrame(c.Frame);
        }
    }

    public bool HasLabel(string label) => Labels.ContainsKey(label);

    public void GotoFrame(int f)
    {
        f = Math.Clamp(f, 0, Math.Max(0, FrameCount - 1));
        Frame = f;
        foreach (var c in Children)
        {
            bool on = f >= c.First && f <= c.Last;
            c.Node.Visible = on;
            if (!on) continue;
            var m = c.Base; var col = c.BaseColor; bool vis = true;
            foreach (var (fr, mm, cc, v) in c.Moves)
            {
                if (fr > f) break;
                if (mm != null) m = mm.Value;
                if (cc != null) col = cc.Value;
                if (v != null) vis = v.Value;
            }
            c.Node.Transform = c.Override ?? m;
            c.Node.Modulate = col;
            if (!vis || c.Hidden) c.Node.Visible = false;
        }
        foreach (var g in maskGroups) g.Update();
    }

    readonly List<MaskGroup> maskGroups = new();

    /// <summary>Instance by AS3 path ("m_dialog.m_inputID"); unnamed sprite children are searched through.</summary>
    public MovieClipNode FindClip(string path)
    {
        var segs = path.Split('.');
        return FindFrom(this, segs, 0);
    }

    static MovieClipNode FindFrom(MovieClipNode n, string[] segs, int i)
    {
        if (i == segs.Length) return n;
        foreach (var c in n.Children)
            if (c.Name == segs[i] && c.Node is MovieClipNode mc)
            {
                var r = FindFrom(mc, segs, i + 1);
                if (r != null) return r;
            }
        foreach (var c in n.Children)
            if (c.Name == null && c.Node is MovieClipNode mc)
            {
                var r = FindFrom(mc, segs, i);
                if (r != null) return r;
            }
        if (i == 0 || segs.Length == 1)      // a lone name: search the named sub clips too
            foreach (var c in n.Children)
                if (c.Name != null && c.Name != segs[i] && c.Node is MovieClipNode mc)
                {
                    var r = FindFrom(mc, segs, i);
                    if (r != null) return r;
                }
        return null;
    }

    /// <summary>All text nodes named "textField" directly in this clip (one per frame state).</summary>
    public IEnumerable<TextNode> TextFields => Children.Where(c => c.Name == "textField" && c.Node is TextNode).Select(c => (TextNode)c.Node);

    /// <summary>Sets every text child with this instance name (one exists per frame state).</summary>
    public void SetNamedText(string name, string text)
    {
        foreach (var c in Children) if (c.Name == name && c.Node is TextNode t) t.Text = text;
    }

    public void SetNamedVisible(string name, bool visible)
    {
        foreach (var c in Children) if (c.Name == name) c.Last = visible ? int.MaxValue : -1;
        GotoFrame(Frame);
    }

    public void SetText(string text) { foreach (var t in TextFields) t.Text = text; }

    /// <summary>AS3 htmlText on this clip's text fields (TextNode.SetHtml).</summary>
    public void SetHtmlText(string html) { foreach (var t in TextFields) t.SetHtml(html); }

    /// <summary>A named child text field's htmlText (AS3 clip.name.htmlText = html).</summary>
    public void SetNamedHtml(string name, string html)
    {
        foreach (var c in Children) if (c.Name == name && c.Node is TextNode t) t.SetHtml(html);
    }

    /// <summary>A direct child by instance name.</summary>
    public Child Direct(string name) => Children.FirstOrDefault(c => c.Name == name);

    /// <summary>Moves a direct child's registration point to <paramref name="x"/> (AS3 child.x = x).</summary>
    public void SetChildX(Child c, float x)
    {
        if (c == null) return;
        var b = c.Base; c.Base = new Transform2D(b.X, b.Y, new Vector2(x, b.Origin.Y));
        GotoFrame(Frame);
    }

    /// <summary>
    /// The bounding box of what is shown at the current frame, in this clip's coordinates (AS3 getBounds(this)).
    /// With <paramref name="fitText"/> text fields count only as wide as their text plus the 2 px gutters (autoSize).
    /// </summary>
    public Rect2? ContentRect(bool fitText = false)
    {
        Rect2? r = null;
        foreach (var c in Children)
        {
            if (!(Frame >= c.First && Frame <= c.Last) || !c.Node.Visible || c.IsMask) continue;
            Rect2? local = c.Node switch
            {
                MovieClipNode mc => mc.ContentRect(fitText),
                TextNode t => fitText ? new Rect2(t.Bounds.Position, new Vector2(t.TextWidth + 4, t.Bounds.Size.Y)) : t.Bounds,
                _ => c.Def["id"] != null && Scope.Symbol((int)c.Def["id"]) is { } sym ? UiBuilder.Rc(sym["bounds"]) : null,
            };
            if (local == null) continue;
            // a 9-slice child is built at its placed size with a unit matrix: its symbol bounds take the placed scale
            if (c.Static9 && c.Def["matrix"] is JsonObject m9 && c.Def["id"] != null && Scope.Symbol((int)c.Def["id"])?["bounds"] is { } sb)
            {
                var r9 = UiBuilder.Rc(sb);
                local = new Rect2(r9.Position, r9.Size * new Vector2(Math.Abs(UiBuilder.F(m9["a"], 1)), Math.Abs(UiBuilder.F(m9["d"], 1))));
            }
            var xf = c.Node.Transform; var b = local.Value;
            foreach (var p in new[] { xf * b.Position, xf * new Vector2(b.End.X, b.Position.Y), xf * new Vector2(b.Position.X, b.End.Y), xf * b.End })
                r = r == null ? new Rect2(p, Vector2.Zero) : r.Value.Expand(p);
        }
        return r;
    }

    /// <summary>A direct child's width as AS3 reports it (bounding box in this clip's coordinates).</summary>
    public float ChildWidth(Child c, bool fitText = false)
    {
        if (c?.Node is MovieClipNode mc && mc.ContentRect(fitText) is { } b)
        {
            var xf = new Transform2D(c.Node.Transform.X, c.Node.Transform.Y, Vector2.Zero);
            return Math.Abs((xf * b.End).X - (xf * b.Position).X);
        }
        return c?.Node is TextNode t ? (fitText ? t.TextWidth + 4 : t.Bounds.Size.X) : 0;
    }

    public IEnumerable<MovieClipNode> AllClips()
    {
        yield return this;
        foreach (var c in Children) if (c.Node is MovieClipNode mc) foreach (var x in mc.AllClips()) yield return x;
    }

    /// <summary>Bounds in the coordinates of the canvas (viewport).</summary>
    public Rect2 GlobalBounds()
    {
        var xf = GetGlobalTransform();
        var b = Bounds;
        var pts = new[] { xf * b.Position, xf * new Vector2(b.End.X, b.Position.Y), xf * new Vector2(b.Position.X, b.End.Y), xf * b.End };
        var r = new Rect2(pts[0], Vector2.Zero);
        foreach (var p in pts) r = r.Expand(p);
        return r;
    }

    // timeline player for state tweens and popup animations
    bool playing; int rangeFrom, rangeTo; double rangeT;

    bool looping;

    public void PlayRange(int from, int to)
    {
        rangeFrom = from; rangeTo = to; rangeT = 0; playing = true; looping = false;
        SetProcess(true);      // the next frame ticks, as if _Process had always run
        GotoFrame(from);
    }

    /// <summary>
    /// Plays the whole timeline over and over at the movie's frame rate: a plain MovieClip with no frame script (no stop())
    /// runs on from its last frame back to frame 0.
    /// </summary>
    public void Loop()
    {
        if (FrameCount < 2) return;
        PlayRange(0, FrameCount - 1);
        looping = true;
    }

    public override void _Process(double delta)
    {
        if (!playing) { SetProcess(false); return; }
        rangeT += delta;
        if (looping) { GotoFrame((int)(rangeT * Movie.Fps) % FrameCount); return; }
        int f = rangeFrom + (int)(rangeT * Movie.Fps);
        if (f >= rangeTo) { GotoFrame(rangeTo); playing = false; SetProcess(false); return; }
        GotoFrame(f);
    }
}
