using System.Text.Json.Nodes;
using Godot;
using Ms2.Swf.Clik;

namespace Ms2Godot.Ui;

/// <summary>
/// A placed vertical CLIK ScrollBar drawn from its <see cref="ScrollBarModel"/> and driven by the mouse as ScrollBar wires
/// its parts in configUI: the arrows are auto repeating Buttons (ButtonEvent.PRESS scrolls a page, then each repeat CLICK
/// another: handleUpArrowPress / handleUpArrowClick and the down ones), the thumb's MOUSE_DOWN starts a drag
/// (handleThumbPress) that the stage's MOUSE_MOVE moves (doDrag) and its MOUSE_UP ends (endDrag), the track's MOUSE_DOWN
/// pages towards the mouse (handleTrackPress; the track takes the mouse only while the thumb shows and the bar is enabled)
/// and a wheel turn over the bar scrolls it (handleMouseWheel). The arrows stay built; the track and the thumb are built
/// again only when their size changes (setSize on a 9-slice symbol rebuilds its grid; a plain track symbol is scaled).
/// </summary>
public sealed class ClikScrollBarView
{
    public readonly ScrollBarModel Model;
    /// <summary>The node the parts are drawn in, at the placed bar's place (the bar's own units).</summary>
    public readonly Node2D View;
    readonly UiMovie movie;
    readonly double u;
    readonly UiScope scope;
    readonly MovieClipNode.Child trackPart, thumbPart;
    readonly MovieClipNode up, down;
    MovieClipNode track, thumb;
    double trackH = -1, thumbH = -1;
    int drawn = -1;
    readonly Dictionary<Node, int> depths = new();

    /// <summary>A part added in the order of its depth in the bar symbol.</summary>
    void Add(MovieClipNode n, MovieClipNode.Child c)
    {
        int d = c?.Def["depth"] is JsonNode dn ? (int)dn : int.MaxValue;
        depths[n] = d;
        View.AddChild(n);
        foreach (var o in View.GetChildren())
            if (o != n && depths.TryGetValue(o, out int od) && od > d) { View.MoveChild(n, o.GetIndex()); break; }
    }

    /// <param name="parent">The clip the bar is placed in; the placed bar is taken out of its display list.</param>
    /// <param name="barChild">The placed bar; <paramref name="bar"/> its clip, whose named parts are built again here.</param>
    /// <param name="u">The scale the movie is drawn at on top (text crispness).</param>
    public ClikScrollBarView(UiMovie movie, double u, MovieClipNode parent, MovieClipNode.Child barChild, MovieClipNode bar, ScrollBarModel model)
    {
        this.movie = movie; this.u = u; Model = model; scope = bar.Scope;
        trackPart = bar.Direct("track"); thumbPart = bar.Direct("thumb");
        View = new Node2D { Position = barChild.Base.Origin };
        parent.AddChild(View);
        parent.MoveChild(View, bar.GetIndex());
        barChild.Last = -1;
        parent.GotoFrame(parent.Frame);
        up = Arrow(bar.Direct("upArrow"), model.ScrollUp);
        down = Arrow(bar.Direct("downArrow"), model.ScrollDown);
    }

    static int? Id(MovieClipNode.Child c) => c?.Def["id"] is JsonNode id ? (int)id : null;

    /// <summary>A part at its placed scale.</summary>
    MovieClipNode Part(MovieClipNode.Child c)
    {
        if (Id(c) is not int id) return null;
        float sx = c.Base.X.Length(), sy = c.Base.Y.Length();
        var n = UiBuilder.BuildSprite(movie, scope, id, u * sx, u * sy);
        if (n != null) n.Scale = new Vector2(sx, sy);
        return n;
    }

    /// <summary>
    /// A part's height set (the track's by Constraints, the thumb's by setSize): a 9-slice symbol is built with its grid at
    /// the size (its width the placed one); another is scaled to the height (DisplayObject.height: the bounds' height).
    /// </summary>
    MovieClipNode Sized(MovieClipNode.Child c, double h)
    {
        if (Id(c) is not int id || scope.Symbol(id) is not { } sym || sym["bounds"] is not JsonNode bn) return null;
        float bw = UiBuilder.F(bn["w"], 0), bh = UiBuilder.F(bn["h"], 0);
        float sx = c.Base.X.Length();
        if (sym["scale9"] != null) return movie.BuildSymbolSized(scope, id, u, bw * sx, (float)h);
        float sy = bh > 0 ? (float)h / bh : 0;
        var n = UiBuilder.BuildSprite(movie, scope, id, u * sx, u * sy);
        if (n != null) n.Scale = new Vector2(sx, sy);
        return n;
    }

    /// <summary>A part's placed width.</summary>
    float PlacedWidth(MovieClipNode.Child c) =>
        Id(c) is int id && scope.Symbol(id)?["bounds"] is JsonNode bn ? UiBuilder.F(bn["w"], 0) * c.Base.X.Length() : 0;

    MovieClipNode Arrow(MovieClipNode.Child c, Action scroll)
    {
        var n = Part(c);
        if (n == null) return null;
        n.Position = c.Base.Origin;
        if (n.Widget == null) UiWidgets.Attach(n);
        if (n.Widget is ButtonWidget b)
        {
            b.AutoRepeat = true;                    // ScrollBar.configUI: upArrow.autoRepeat = downArrow.autoRepeat = true
            b.Press += _ => scroll();
            b.RepeatClick += _ => scroll();
        }
        Add(n, c);
        return n;
    }

    /// <summary>
    /// The parts of a bar symbol as ScrollBar finds them by name (upArrow, track, downArrow, thumb): each one's y and height
    /// in the symbol (its symbol's bounds through its placement's y scale), for <see cref="ScrollBarModel"/>.
    /// </summary>
    public static ScrollBarModel ModelOf(MovieClipNode bar, MovieClipNode.Child barChild)
    {
        (double Y, double H) Part(string name)
        {
            var c = bar.Direct(name);
            var b = c?.Def["id"] is JsonNode id ? bar.Scope.Symbol((int)id)?["bounds"] : null;
            return (c?.Base.Origin.Y ?? 0, b == null ? 0 : UiBuilder.F(b["h"], 0) * c.Base.Y.Length());
        }
        var up = Part("upArrow"); var track = Part("track"); var down = Part("downArrow"); var thumb = Part("thumb");
        // the symbol's own size (a placed scale-9 bar is built sliced at its placed size)
        var size = bar.Scope.Symbol(bar.SymbolId)?["bounds"] is JsonNode sb ? UiBuilder.Rc(sb).Size : bar.Bounds.Size;
        return new ScrollBarModel(size.X, size.Y, up.H, track.Y, track.H, down.Y, down.H, thumb.Y, thumb.H,
            PlacedScaleY(barChild));
    }

    /// <summary>A placement's y scale: a scale-9 sprite is sliced at its placed size, its transform without the scale.</summary>
    static double PlacedScaleY(MovieClipNode.Child c) =>
        c.Static9 && c.Def["matrix"] is JsonObject m ? Math.Abs(UiBuilder.F(m["d"], 1)) : c.Base.Y.Length();

    /// <summary>The bar's buttons, for the mouse router.</summary>
    public IEnumerable<MovieClipNode> Buttons
    {
        get
        {
            if (up != null) yield return up;
            if (down != null) yield return down;
            if (thumb != null) yield return thumb;
        }
    }

    /// <summary>A point of the canvas in the bar's units.</summary>
    Vector2 Local(Vector2 p) => View.GetGlobalTransform().AffineInverse() * p;

    /// <summary>Whether a canvas point is over the drawn bar.</summary>
    public bool Over(Vector2 p) =>
        View.IsVisibleInTree() && new Rect2(0, 0, (float)Model.Width, (float)Model.ContentHeight).HasPoint(Local(p));

    /// <summary>
    /// The mouse for what the buttons do not take: the thumb's press starts the drag; the stage's moves drag it and its
    /// release ends it; a left press the router gave to no button goes to the track when it is under the mouse.
    /// </summary>
    /// <param name="picked">The button the router gave a left press to (null: none).</param>
    public void Handle(InputEvent e, ButtonWidget picked)
    {
        if (e is InputEventMouseMotion mm) { if (Model.Dragging) Model.Drag(Local(mm.Position).Y); return; }
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mb) return;
        if (!mb.Pressed) { Model.EndDrag(); return; }
        var p = Local(mb.Position);
        if (picked != null)
        {
            if (thumb != null && picked == thumb.Widget) Model.ThumbPress(p.Y);
            return;
        }
        if (track == null || !track.IsVisibleInTree() || !Model.TrackEnabled) return;
        if (!new Rect2(trackPart.Base.Origin.X, (float)Model.TrackY, PlacedWidth(trackPart), (float)Model.TrackHeight).HasPoint(p)) return;
        Model.TrackPress(p.Y, mb.ShiftPressed);
    }

    /// <summary>The arrows' repeat timers.</summary>
    public void Tick(double ms)
    {
        (up?.Widget as ButtonWidget)?.Tick(ms);
        (down?.Widget as ButtonWidget)?.Tick(ms);
    }

    /// <summary>
    /// The bar's validation (UIComponent.validateNow): the model draws what was invalidated and, when it drew, the parts
    /// go to their places: the arrows enabled or not, the track at its height, the thumb at its height and place, shown or
    /// not, enabled with the bar.
    /// </summary>
    public void Validate()
    {
        Model.Validate();
        if (Model.Draws == drawn) return;
        drawn = Model.Draws;
        View.Scale = new Vector2(1, (float)Model.ScaleY);
        if (up != null) up.Widget?.Set("enabled", Model.UpEnabled);
        if (down != null) { down.Position = new Vector2(down.Position.X, (float)Model.DownY); down.Widget?.Set("enabled", Model.DownEnabled); }
        if (track == null || trackH != Model.TrackHeight)
        {
            if (track != null) { depths.Remove(track); track.QueueFree(); }
            trackH = Model.TrackHeight;
            track = Sized(trackPart, trackH);
            if (track != null) Add(track, trackPart);
        }
        if (track != null) track.Position = new Vector2(trackPart.Base.Origin.X, (float)Model.TrackY);
        if (thumb == null || thumbH != Model.ThumbHeight)
        {
            if (thumb != null) { depths.Remove(thumb); thumb.QueueFree(); }
            thumbH = Model.ThumbHeight;
            thumb = Sized(thumbPart, thumbH);
            if (thumb != null)
            {
                if (thumb.Widget == null) UiWidgets.Attach(thumb);
                Add(thumb, thumbPart);
            }
        }
        if (thumb != null)
        {
            thumb.Position = new Vector2(thumbPart.Base.Origin.X, (float)Model.ThumbY);
            thumb.Visible = Model.ThumbVisible;
            if (thumb.Widget != null) thumb.Widget.Set("enabled", Model.Enabled);
            else thumb.GotoLabel(Model.Enabled ? "up" : "disabled");
        }
    }
}
