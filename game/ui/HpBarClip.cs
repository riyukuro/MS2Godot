using Godot;

namespace Ms2Godot.Ui;

/// <summary>
/// The drawn part of a UIHpBar::Entity, one uihpbar movie hpbar_entity_HPBar instance, shared by the npc and PC bars.
/// <list type="bullet">
/// <item>Open (vfunc_5, AS open): "_rank" + rank, the gauge at the guide's place and height; m_c0 = ceil(guide width).
/// The open's walk of the movie is kept per rank (the frame's children and the guide's placement never change), so a
/// hide-and-show opens without re-reading the timeline.</item>
/// <item>Width (vfunc_6, when the HP changed): (int)((float)cur / (float)max x m_c0), left-anchored, SetWidthInt: the
/// gauge is built once per open at the guide's full width and then resized in place through its scale-9 context (the
/// corners keep their size, no rebuild per hit); label "friendly" or "hostile", set only when it changed.</item>
/// <item>A bar that was hidden (vfunc_7) opens again when it is next drawn.</item>
/// <item>Free (ReleaseEntity) hands the clip back to <paramref name="pool"/>, the free list the client keeps its cache
/// clips in (UIHpBar's m_78), instead of freeing it; the next Build takes from it.</item>
/// </list>
/// </summary>
public sealed class HpBarClip
{
    readonly UiMovie movie;
    float u;
    readonly Node parent;
    readonly Stack<MovieClipNode> pool;
    MovieClipNode node, gauge;
    Vector2 guidePos; float guideH; int fullWidth;
    int gaugeSymbol;                        // m_renderHp's symbol (hpbar_large_3)
    int openRank = -1;                      // the open walk is kept per rank
    bool needsOpen = true;
    bool? gaugeFriendly;                     // the label the gauge sits on

    public HpBarClip(UiMovie movie, float u, Node parent, Stack<MovieClipNode> pool = null)
    { this.movie = movie; this.u = u; this.parent = parent; this.pool = pool; }

    /// <summary>The viewport scale after a resize (<see cref="PlayerHpBar.Resize"/>).</summary>
    public void Rescale(float scale)
    {
        u = scale;
        if (node != null) node.Scale = new Vector2(u, u);
    }

    /// <summary>Whether the next draw opens the bar (vfunc_5): never opened, or hidden since.</summary>
    public bool NeedsOpen => needsOpen;

    /// <summary>Whether the bar is on screen now (a live run's check).</summary>
    public bool Drawn => node != null && node.Visible;

    /// <summary>The guide's full width at the current rank (m_c0), 0 before the bar first opens.</summary>
    public int FullWidth => fullWidth;

    /// <summary>vfunc_7: the bar hides; it opens again on its next draw.</summary>
    public void Hide()
    {
        if (node != null && node.Visible) { node.Visible = false; needsOpen = true; }
    }

    /// <summary>One draw (vfunc_5 when reopened, then vfunc_6) at the anchor's screen point; <paramref name="dirty"/> is the HP-changed flag (m_bc), cleared once drawn.</summary>
    public void Draw(Vector2 screen, int rank, bool friendly, long cur, long max, ref bool dirty)
    {
        if (movie == null || node == null && !Build()) return;
        node.Visible = true;
        if (rank != openRank) { Open(rank); dirty = true; }
        else if (needsOpen) { needsOpen = false; dirty = true; }     // the same rank reopens without re-reading the timeline
        if (dirty) { SetWidth(friendly, cur, max); dirty = false; }
        // vfunc_6 (uihpbar.cpp:159-167): the label follows a relation change whether or not the HP changed
        else if (gauge != null && gaugeFriendly != friendly) { gauge.GotoLabel(friendly ? "friendly" : "hostile"); gaugeFriendly = friendly; }
        node.Position = UiScale.StagePoint(screen, u);   // SetPositionFloat (uihpbar.cpp:171, 480, 618)
    }

    bool Build()
    {
        node = pool is { Count: > 0 } ? pool.Pop() : movie.BuildClass("hpbar_entity_HPBar", u);
        if (node == null) return false;
        node.Scale = new Vector2(u, u);
        node.Visible = false;
        if (node.GetParent() == null) parent.AddChild(node);   // a pooled clip keeps its layer, the pool is per layer
        return true;
    }

    /// <summary>AS open: the rank's frame, and the guide's place and size there (read before it is hidden).</summary>
    void Open(int rank)
    {
        needsOpen = false; openRank = rank;
        gauge?.QueueFree(); gauge = null; gaugeFriendly = null;    // the frame may name another gauge symbol
        node.GotoLabel(RankLabel(rank));
        var guide = node.Children.FirstOrDefault(c => c.Name == "m_guideHp" && node.Frame >= c.First && node.Frame <= c.Last);
        var bounds = guide?.Def?["id"] is { } id && node.Scope.Symbol((int)id)?["bounds"] is { } b ? UiBuilder.Rc(b) : new Rect2(0, 0, 58, 5);
        var m = guide == null ? Transform2D.Identity : PlacedAt(guide.Def, node.Frame);
        guidePos = m * bounds.Position;
        guideH = bounds.Size.Y * m.Y.Length();
        fullWidth = (int)Math.Ceiling(bounds.Size.X * m.X.Length() - 1e-4);   // GetWidthCeil
        var render = node.Children.FirstOrDefault(c => c.Name == "m_renderHp" && node.Frame >= c.First && node.Frame <= c.Last);
        gaugeSymbol = render?.Def?["id"] is { } rid ? (int)rid : 0;
        foreach (var c in node.Children)                                     // configUI hides the guide; the gauge is drawn below
            if (c.Name is "m_guideHp" or "m_renderHp") c.Node.Visible = false;
    }

    static readonly string[] rankLabels = new string[16];
    static string RankLabel(int rank) => (uint)rank < rankLabels.Length ? rankLabels[rank] ??= "_rank" + rank : "_rank" + rank;

    /// <summary>An instance's placement matrix at a frame, from the timeline (a 9-slice instance's node is built at scale 1).</summary>
    static Transform2D PlacedAt(System.Text.Json.Nodes.JsonObject def, int frame)
    {
        var m = UiBuilder.ToTransform(def["matrix"]);
        if (def["moves"] is System.Text.Json.Nodes.JsonArray mv)
            foreach (var mo in mv)
                if ((int)mo["frame"] <= frame && (bool)mo["hasMatrix"]) m = UiBuilder.ToTransform(mo["matrix"]);
        return m;
    }

    /// <summary>vfunc_6's width: (int)((float)cur / (float)max x m_c0).</summary>
    public static int Width(long cur, long max, int fullWidth) => max == 0 ? 0 : (int)((float)cur / (float)max * fullWidth);

    void SetWidth(bool friendly, long cur, long max)
    {
        if (gauge == null)
        {
            gauge = movie.BuildSymbolSized(gaugeSymbol, u, fullWidth, guideH);
            if (gauge == null) return;
            gauge.Position = guidePos;
            node.AddChild(gauge);
        }
        if (gaugeFriendly != friendly) { gauge.GotoLabel(friendly ? "friendly" : "hostile"); gaugeFriendly = friendly; }
        int w = Width(cur, max, fullWidth);
        gauge.Visible = w > 0;
        // SetWidthInt without the rebuild: the gauge's shapes draw through their scale-9 context, so the new width is a
        // context update (the corners keep their size) and a redraw
        foreach (var c in gauge.Children)
            if (c.Node is ShapeNode s && s.Nine is { } ctx) { ctx.Sx = w / Math.Max(ctx.Bounds.Size.X, 0.001f); s.QueueRedraw(); }
    }

    /// <summary>The actor left the world (ReleaseEntity): back to the free list, or freed without one.</summary>
    public void Free()
    {
        gauge?.QueueFree(); gauge = null; gaugeFriendly = null;
        if (node != null && pool != null) { node.Visible = false; pool.Push(node); }
        else node?.QueueFree();
        node = null; needsOpen = true; openRank = -1;
    }
}
