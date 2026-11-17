using Godot;
using Ms2.Formats.World;
using Ms2Godot.Ui;

namespace Ms2Godot.World;

/// <summary>
/// An npc's head HP bar (UIHpBar::Entity, the uihpbar movie's hpbar_entity_HPBar), for rows with hpBar="1".
/// <list type="bullet">
/// <item>Active (m_4c) once the npc enters battle stance (CNpc::vfunc_66 with a stance, FUN_141992180), or from spawn
/// for property tag 2 always_display_hp_bar, which is also shown from spawn. Leaving the stance changes nothing: an npc
/// bar has no time-out (the full-HP 2000 ms hide of FUN_141992230 is the PC's), it goes only with the npc's death or
/// removal, or while its anchor is behind the camera.</item>
/// <item>Shown (m_b8) once the local player damages the npc (PCBase::vfunc_112); others' hits never show it.</item>
/// <item>Open (vfunc_5, AS open): "_rank" + the row's class (10 with big_hp_bar), the gauge at the guide's place and
/// height, label "friendly" for rows with friendly 1, else "hostile"; m_c0 = ceil(guide width).</item>
/// <item>Width (vfunc_6, when the HP changed): (int)((float)cur / (float)max x m_c0), left-anchored; no tween, no
/// trail.</item>
/// <item>Place: the npc's anchor on screen, plus OffsetNpcHpbar and the camera shift in stage units. OffsetNpcHpbar
/// equals OffsetNpcNametag (5 and 5 in the client's constants) and both get the same camera shift, so the bar goes
/// where the npc's name tag goes (<see cref="NpcPresence"/> places both from one anchor).</item>
/// </list>
/// </summary>
public sealed class NpcHpBar
{
    public readonly FieldNpcs.Placed Npc;
    public bool Active, Shown, Dead;
    long cur, max;
    bool dirty, needsOpen = true;
    readonly UiMovie movie;
    readonly float u;
    MovieClipNode node, gauge;
    Vector2 guidePos; float guideH; int fullWidth;
    int gaugeSymbol;                        // m_renderHp's symbol (hpbar_large_3)
    readonly Node parent;

    public NpcHpBar(FieldNpcs.Placed npc, UiMovie movie, float u, Node parent)
    {
        Npc = npc; this.movie = movie; this.u = u; this.parent = parent;
        if (npc.Row.AlwaysDisplayHpBar) { Shown = true; Activate(cur, max); }
    }

    /// <summary>The rank of the bar (FUN_141992680): the row's class, 10 with big_hp_bar.</summary>
    public int Rank => Npc.Row.BigHpBar ? 10 : Npc.Row.Class;

    /// <summary>FUN_141992180: the HP, changed or not, and the bar made active.</summary>
    public void Activate(long cur, long max)
    {
        dirty = dirty || this.cur != cur || this.max != max;
        this.cur = cur; this.max = max;
        Active = true;
    }

    /// <summary>FUN_141992230 (OnStat, vfunc_74) for an npc: the HP; a change while active is drawn on the next frame.</summary>
    public void Stat(long cur, long max)
    {
        bool changed = dirty || this.cur != cur || this.max != max;
        this.cur = cur; this.max = max;
        if (Active && changed) dirty = true;
    }

    /// <summary>One frame (UIHudEntity::vfunc_15) with the anchor's screen point, or null when it cannot be projected.</summary>
    public void Update(Vector2? screen)
    {
        if (!Active) return;
        if (screen == null || !Shown || Dead || movie == null)
        {
            if (node != null && node.Visible) { node.Visible = false; needsOpen = true; }
            return;
        }
        if (node == null && !Build()) return;
        node.Visible = true;
        if (needsOpen) Open();
        if (dirty) { SetWidth(); dirty = false; }
        node.Position = screen.Value;
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
    void Open()
    {
        gauge?.QueueFree();
        gauge = null;
        node.GotoLabel("_rank" + Rank);
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
        dirty = true;
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

    void SetWidth()
    {
        gauge?.QueueFree();
        gauge = null;
        int w = max == 0 ? 0 : (int)((float)cur / (float)max * fullWidth);
        if (w <= 0) return;
        gauge = movie.BuildSymbolSized(gaugeSymbol, u, w, guideH);
        if (gauge == null) return;
        gauge.GotoLabel(Npc.Row.Friendly == 1 ? "friendly" : "hostile");
        gauge.Position = guidePos;
        node.AddChild(gauge);
    }

    /// <summary>The npc left the world (ReleaseEntity).</summary>
    public void Release()
    {
        node?.QueueFree();
        node = null; gauge = null;
        Active = Shown = false;
    }
}
