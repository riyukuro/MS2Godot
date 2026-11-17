using Godot;

namespace Ms2Godot.Ui;

/// <summary>
/// The drawn part of a UIHpBar::Entity, one uihpbar movie hpbar_entity_HPBar instance, shared by the npc and PC bars.
/// <list type="bullet">
/// <item>Open (vfunc_5, AS open): "_rank" + rank, the gauge at the guide's place and height; m_c0 = ceil(guide width).</item>
/// <item>Width (vfunc_6, when the HP changed): (int)((float)cur / (float)max x m_c0), left-anchored; no tween, no trail;
/// label "friendly" or "hostile".</item>
/// <item>A bar that was hidden (vfunc_7) opens again when it is next drawn.</item>
/// </list>
/// </summary>
public sealed class HpBarClip
{
    readonly UiMovie movie;
    readonly float u;
    readonly Node parent;
    MovieClipNode node, gauge;
    Vector2 guidePos; float guideH; int fullWidth;
    int gaugeSymbol;                        // m_renderHp's symbol (hpbar_large_3)
    bool needsOpen = true;

    public HpBarClip(UiMovie movie, float u, Node parent) { this.movie = movie; this.u = u; this.parent = parent; }

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
        if (needsOpen) { Open(rank); dirty = true; }
        if (dirty) { SetWidth(friendly, cur, max); dirty = false; }
        node.Position = screen;
    }

    bool Build()
    {
        node = movie.BuildClass("hpbar_entity_HPBar", u);
        if (node == null) return false;
        node.Scale = new Vector2(u, u);
        parent.AddChild(node);
        return true;
    }

    /// <summary>AS open: the rank's frame, and the guide's place and size there (read before it is hidden).</summary>
    void Open(int rank)
    {
        gauge?.QueueFree();
        gauge = null;
        node.GotoLabel("_rank" + rank);
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
        needsOpen = false;
    }

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
        gauge?.QueueFree();
        gauge = null;
        int w = Width(cur, max, fullWidth);
        if (w <= 0) return;
        gauge = movie.BuildSymbolSized(gaugeSymbol, u, w, guideH);
        if (gauge == null) return;
        gauge.GotoLabel(friendly ? "friendly" : "hostile");
        gauge.Position = guidePos;
        node.AddChild(gauge);
    }

    /// <summary>The actor left the world (ReleaseEntity).</summary>
    public void Free()
    {
        node?.QueueFree();
        node = null; gauge = null; needsOpen = true;
    }
}
