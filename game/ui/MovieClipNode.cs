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
        var b = mask.MaskBounds;
        var pts = new[] { xf * b.Position, xf * new Vector2(b.End.X, b.Position.Y), xf * new Vector2(b.Position.X, b.End.Y), xf * b.End };
        var r = new Rect2(pts[0], Vector2.Zero);
        foreach (var p in pts) r = r.Expand(p);
        if (!mask.Node.Visible) r = new Rect2(-100000, -100000, 200000, 200000);   // mask not on the stage: no clipping
        Position = r.Position; Size = r.Size;
        Inner.Position = -r.Position;
    }

    public override void _Process(double delta) => Update();
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
        Movie = movie; Scope = scope; Timeline = timeline; Symbol = symbol; this.sx = sx; this.sy = sy;
        ClassName = (string)symbol?["class"];
        if (symbol?["bases"] is JsonArray b) foreach (var x in b) Bases.Add((string)x);
        Bounds = UiBuilder.Rc(symbol?["bounds"]);
        if (nine != null && symbol?["scale9"] != null) Bounds = nine.Map(Bounds);
        FrameCount = timeline == null ? 1 : (int)(timeline["frames"] ?? 1);
        if (timeline?["labels"] is JsonArray ls) foreach (var l in ls) Labels.TryAdd((string)l["name"], (int)l["frame"]);
    }

    public bool IsA(string cls) => ClassName == cls || Bases.Contains(cls);

    public void Build()
    {
        MaskGroup group = null; int groupEnd = 0;
        if (Timeline?["children"] is JsonArray ch)
            foreach (var c in ch.OrderBy(x => (int)x["depth"]).ThenBy(x => (int)x["first"]))
            {
                int id = (int)c["id"];
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
                    groupEnd = (int)c["clipDepth"];
                    AddChild(group); group.AddChild(holder);
                }
                else if (group != null) group.Inner.AddChild(node);
                else AddChild(node);
                if (node is MovieClipNode mc) UiWidgets.Attach(mc);
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
        if (animate && end > f && IsInsideTree()) PlayRange(f, end); else { playing = false; GotoFrame(end); }
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
            if (!vis) c.Node.Visible = false;
        }
    }

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

    public void PlayRange(int from, int to)
    {
        rangeFrom = from; rangeTo = to; rangeT = 0; playing = true;
        GotoFrame(from);
    }

    public override void _Process(double delta)
    {
        if (!playing) return;
        rangeT += delta;
        int f = rangeFrom + (int)(rangeT * Movie.Fps);
        if (f >= rangeTo) { GotoFrame(rangeTo); playing = false; return; }
        GotoFrame(f);
    }
}
