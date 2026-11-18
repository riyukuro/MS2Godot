using Godot;
using Ms2.Formats.World;
using Ms2Godot.World;

namespace Ms2Godot.Ui;

/// <summary>
/// Floating combat numbers (UIDamageText, the uidamagetext movie), default mode only (UpdateNormals + UpdateOwnerFollowers;
/// the old "Clearing" mode is a dev toggle). Ported grades: 0 Normal ("{0}") on an npc ("Monster" glyphs, dmg_ani_npcnormal)
/// and on the own PC ("User" glyphs, dmg_ani_pcnormal), and 5 Recover ("+{0}", "Recover" glyphs, dmg_ani_recover) on the own
/// PC. Critical, miss, block, the empty-text recover grades 6 and 7, exp, meso and items are not ported here.
/// <list type="bullet">
/// <item>Text: plain decimal, no grouping. Glyphs left to right, each advancing by its right edge plus the NormalMovie spacing
/// (-15 for digits, -12 for signs), a leading "+" on top; the number is centred on its own bounds, and the ani is raised by
/// half the number's height.</item>
/// <item>Spawn (damage): the hit point, else the target's Eff_Damage point, stored once; no jitter.
/// Recover numbers follow the owner's Eff_Damage every frame. A number is not created when the point is behind
/// the camera, nor for a target out of show distance; off-screen points still spawn.</item>
/// <item>Stack (damage, per target): every number takes the target's next index; the newest 3 are visible (up to 26 over all
/// targets); a visible number with newer ones above it (nBehind) and older than 100 ms jumps once: gotoAndPlay("_next"), a
/// no-op on an ani without that label (the clip plays on), and is no longer moved. The others are raised by
/// (nBehind - 1) lines of DamageGap (30). The entry leaves the list after 1000 ms; the clip removes itself at its ani's
/// RemoveAni frame.</item>
/// <item>Scale: 1 when the point is no farther from the camera than the player, else 0.5 + 0.5 x clamp((DamageDistance - d) /
/// DamageDistance) with d its distance to the player (DamageDistance, 2500 in the client's constants);
/// recover numbers on the own PC are always 1.</item>
/// <item>Place: the target's "Eff_Damage" dummy (its position without one) on screen in stage units, plus FUN_1412762d0
/// (the camera-pitch shift); a follower adds PCBase::m_f178 too, OffsetPcDamagenumber (0 in the client's constants).</item>
/// </list>
/// </summary>
public sealed partial class DamageText : CanvasLayer
{
    const int Line = 30, JumpCount = 3, MaxShown = 25, MaxList = 35, MaxIndex = 1000, LifeMs = 1000, JumpMs = 100;
    const float Distance = 2500;

    /// <summary>An ani: its class and the frames whose script dispatches RemoveAni (as_uidamagetext: npcnormal 28 and 42, pcnormal 34, recover 28 and 44).</summary>
    sealed record Ani(string Class, int[] Remove);
    static readonly Ani NpcNormal = new("dmg_ani_npcnormal", [28, 42]);
    static readonly Ani PcNormal = new("dmg_ani_pcnormal", [34]);
    static readonly Ani Recover = new("dmg_ani_recover", [28, 44]);

    sealed class Clip
    {
        public Node2D Wrap;
        public MovieClipNode Ani;
        public int[] Remove;
        public double Frame;          // the ani's playhead, at the movie's 60 fps
        public bool Gone;
    }

    sealed class Entry
    {
        public object Owner;
        public Vector3 Pos;            // the stored world point (damage)
        public Func<Vector3?> Follow;  // recover: the owner's point now
        public int Index;
        public int State;              // 0 shown, 1 jumped, 3 hidden
        public bool CanJump = true;
        public double Start = -1;
        public Clip Clip;
    }

    readonly UiMovie movie;
    readonly float u = 1;
    readonly List<Entry> normals = new(), followers = new();
    /// <summary>CounterInfo, per owner: how many of its numbers are in the list (m_nCount) and the index the newest took (m_nIndex).</summary>
    sealed class Counter { public int Count = 1, Index; }
    readonly Dictionary<object, Counter> normalCounter = new(), followerCounter = new();
    readonly float showDistance = Distance;
    readonly List<Clip> clips = new();
    readonly Ms2.Formats.Pack.PackSet packs;
    double now;                        // ms
    Camera3D camera;
    Field field;
    Player player;
    IReadOnlyList<EffectDummy> pcDummies;
    Node3D pcModel;

    public DamageText(UiAssets ui, ClientConstants constants, Vector2 window)
    {
        showDistance = constants.Float("DamageDistance", Distance);
        Name = "DamageText";
        Layer = 5;                     // with the name tags and HP bars, added after them; the client's order of the movies is not known
        MovieLayerHost.Bind(this, Ms2.Ui.CommonDepth.Damage);   // UIDamageText's layer is 8 (m_2c, uidamagetext.cpp:207)
        Offset = UiScale.MovieOrigin;
        packs = ui.Packs;
        u = UiScale.For(window);
        try { movie = new UiMovie(ui, "uidamagetext"); } catch (Exception e) { GD.PushWarning("damage text movie: " + e.Message); }
    }

    public DamageText() { }

    /// <summary>
    /// A hit on an npc by the local player (grade 0); other grades are not ported. FUN_141911e90: nothing when the npc is
    /// out of show distance (FUN_141914d40, IsWithinShowDistance: its distance to the local player, kept each frame by
    /// WorldService, over DamageDistance); the number stands at the damage record's hit point <paramref name="hit"/>, or at
    /// the npc's Eff_Damage point (GetAnchorPosition) when that is zero or not given.
    /// </summary>
    public void OnNpc(FieldNpcs.Placed n, long value, int grade, Vector3? hit = null)
    {
        if (grade != 0 || !Ready() || !WithinShowDistance(n.Root.GlobalPosition)) return;
        var p = hit is { } h && h != Vector3.Zero ? h : NpcPresence.Dummy(n, "Eff_Damage") ?? n.Root.GlobalPosition;
        Spawn(n, p, null, "Monster", value.ToString(), NpcNormal);
    }

    /// <summary>IsWithinShowDistance: the actor's distance to the local player (GameObject m_unk_bc) is at most DamageDistance.</summary>
    bool WithinShowDistance(Vector3 actor) => player != null && actor.DistanceTo(player.GlobalPosition) <= showDistance;

    /// <summary>A hit (grade 0) or a heal (grade 5) on the own PC.</summary>
    public void OnPlayer(long value, int grade)
    {
        if (!Ready() || player == null) return;
        var point = PcPoint();
        if (point == null) return;
        if (grade == 0) Spawn(player, point.Value, null, "User", Math.Abs(value).ToString(), PcNormal);
        else if (grade == 5) Spawn(player, point.Value, PcPoint, "Recover", "+" + Math.Abs(value), Recover);
    }

    bool Ready()
    {
        if (movie == null) return false;
        camera ??= GetViewport().GetCamera3D();
        field ??= GetTree().GetFirstNodeInGroup(Field.Group) as Field;
        player ??= GetTree().GetFirstNodeInGroup(Player.Group) as Player;
        if (player != null && pcModel == null)
        {
            pcDummies = EffectDummyTable.Load(packs).Pc(player.Gender);
            pcModel = player.Visual?.GetNodeOrNull<Node3D>("Model");
        }
        return camera != null;
    }

    /// <summary>The own PC's Eff_Damage point, its position when the dummy is missing.</summary>
    Vector3? PcPoint() => player == null ? null : DummyAnchor.Point(pcDummies, "Eff_Damage", pcModel, player.RootName) ?? player.GlobalPosition;

    void Spawn(object owner, Vector3 pos, Func<Vector3?> follow, string set, string text, Ani ani)
    {
        if (camera.IsPositionBehind(pos)) return;       // world -> screen failed: nothing is created
        var clip = Build(set, text, ani);
        if (clip == null) return;
        var e = new Entry { Owner = owner, Pos = pos, Follow = follow, Clip = clip };
        bool follower = follow != null;
        var counters = follower ? followerCounter : normalCounter;
        var list = follower ? followers : normals;
        // InsertDamageInfo: the local player's numbers go to the head (all ported ones are the local player's). With more
        // than 35 in the list before the insert, the oldest goes and its owner's count drops (DecrementCounter).
        bool over = list.Count > MaxList;
        list.Insert(0, e);
        if (over) { var old = list[^1]; list.RemoveAt(list.Count - 1); Remove(old, counters); }
        // GetOrCreateCounter: the owner's next index (back to 0 at 1000), or a new counter at 0
        if (counters.TryGetValue(owner, out var c)) { c.Count++; if (++c.Index >= MaxIndex) c.Index = 0; }
        else counters[owner] = c = new Counter();
        e.Index = c.Index;
    }

    /// <summary>An entry leaves its list: DecrementCounter (the counter goes once its owner has none left), then the entry's dtor.</summary>
    void Remove(Entry e, Dictionary<object, Counter> counters)
    {
        if (counters.TryGetValue(e.Owner, out var c) && --c.Count <= 0) counters.Remove(e.Owner);
        Drop(e);
    }

    Clip Build(string set, string text, Ani ani)
    {
        var a = movie.BuildClass(ani.Class, u);
        if (a == null) return null;
        var number = new Node2D { Name = "m_dmg" };
        float x = 0, h = 0;
        Node2D plus = null;
        foreach (char ch in text)
        {
            string key = ch == '+' ? "Plus" : ch == '-' ? "Minus" : char.IsDigit(ch) ? ch.ToString() : null;
            if (key == null || movie.BuildClass(set + key, u) is not { } g) continue;
            g.Position = new Vector2(x, 0);
            number.AddChild(g);
            if (ch == '+' && plus == null) plus = g;
            x += g.Bounds.End.X + (char.IsDigit(ch) ? -15 : -12);
            h = Math.Max(h, g.Bounds.End.Y);
        }
        if (plus != null) number.MoveChild(plus, -1);   // a leading "+" goes on top
        float w = 0;
        foreach (var c in number.GetChildren()) if (c is MovieClipNode g) w = Math.Max(w, g.Position.X + g.Bounds.End.X);
        number.Position = new Vector2(-w / 2, -h / 2);
        if (a.Direct("dummy")?.Node is MovieClipNode dummy)
        {
            foreach (var c in dummy.Children) c.Last = -1;  // the placeholder
            dummy.GotoFrame(0);
            dummy.AddChild(number);
        }
        a.Position = new Vector2(0, -h / 2);
        var wrap = new Node2D { Name = "wrapper", Visible = false };   // created hidden, shown on its first placement
        wrap.AddChild(a);
        AddChild(wrap);
        a.GotoFrame(0);
        var clip = new Clip { Wrap = wrap, Ani = a, Remove = ani.Remove };
        clips.Add(clip);
        return clip;
    }

    void Drop(Entry e)
    {
        // the entry's dtor: a clip that has not jumped is stopped and removed
        if (e.State != 1) Kill(e.Clip);
    }

    void Kill(Clip c)
    {
        if (c.Gone) return;
        c.Gone = true;
        c.Wrap.QueueFree();
    }

    public override void _Process(double delta)
    {
        if (movie == null) return;
        now += delta * 1000;
        camera ??= GetViewport().GetCamera3D();
        if (camera == null) return;
        // the clips play on their own and dispatch RemoveAni at their script frames
        foreach (var c in clips)
        {
            if (c.Gone) continue;
            int before = (int)c.Frame;
            c.Frame += delta * movie.Fps;
            int after = Math.Min((int)c.Frame, c.Ani.FrameCount - 1);
            for (int f = before + 1; f <= after && !c.Gone; f++) if (c.Remove.Contains(f)) Kill(c);
            if (!c.Gone) c.Ani.GotoFrame(after);
        }
        clips.RemoveAll(c => c.Gone);
        UpdateFollowers();
        UpdateNormals();
    }

    /// <summary>
    /// UpdateNormals. Each entry in list order; one past its 1000 ms (from its first update) leaves the list. One that may
    /// still jump (m_bCanJump):
    /// <list type="bullet">
    /// <item>Hidden when its owner has no counter, when more than 25 have been shown this frame (so 26 can show), or when its
    /// index is outside the owner's newest 3 (the window nFirst = newest - 2 to nNext = newest + 1, taken around the wrap at
    /// 1000 when nFirst is negative). Hiding a shown one (state 0) sets state 3, hides the clip and ends its jumping, so it
    /// is never shown again.</item>
    /// <item>Otherwise placed only when its point projects (CameraMgr FUN_140af0700; nothing at all happens when it does not):
    /// counted as shown, made visible, raised by (nBehind - 1) lines with nBehind = nNext - 1 - index; with nBehind &gt; 0
    /// and over 100 ms old (state 0) it jumps ("_next", state 1) and is no longer moved; then the pitch shift, the place and
    /// the distance scale.</item>
    /// </list>
    /// </summary>
    void UpdateNormals()
    {
        if (normals.Count == 0) { normalCounter.Clear(); return; }
        int shown = 0, first = 0, next = 0, index = 0;
        float line = Line;   // floor(30 x 100 / 100) for the local player's numbers
        for (int i = 0; i < normals.Count; i++)
        {
            var e = normals[i];
            if (e.Start >= 0 && now - e.Start > LifeMs) { normals.RemoveAt(i--); Remove(e, normalCounter); continue; }
            if (!e.CanJump) continue;
            // crits (never hidden, stacked by two lines) are not ported
            bool hide = !normalCounter.TryGetValue(e.Owner, out var c) || shown > MaxShown;
            if (!hide)
            {
                first = c.Index - JumpCount + 1;
                next = c.Index + 1;
                index = e.Index;
                hide = first >= 0 ? index < first || next <= index : next < index && index <= MaxIndex + first;
            }
            if (e.Start < 0) e.Start = now;
            if (hide)
            {
                if (e.State == 0)
                {
                    e.State = 3;
                    if (!e.Clip.Gone) e.Clip.Wrap.Visible = false;
                    e.CanJump = false;
                }
                continue;
            }
            if (camera.IsPositionBehind(e.Pos)) continue;   // world -> screen failed
            shown++;
            var stage = camera.UnprojectPosition(e.Pos);
            int behind = next - 1 - index;
            stage.Y -= Math.Max(0, behind - 1) * line * u;
            if (behind > 0 && e.State == 0 && now - e.Start > JumpMs) Jump(e);
            stage.Y += PitchShift(e.Pos) * u;
            if (e.Clip.Gone) continue;
            var w = e.Clip.Wrap;
            w.Visible = true;
            w.Position = UiScale.StagePoint(stage, u);
            float s = DistanceScale(e.Pos);
            w.Scale = new Vector2(s * u, s * u);
        }
    }

    /// <summary>
    /// UpdateOwnerFollowers: every frame for as long as the entry lives (it does not stop at the jump), at the owner's
    /// Eff_Damage point now. The entry leaves the list when its owner is gone or out of show distance (the clip stopped and
    /// hidden first); while the point does not project the clip is hidden. It jumps once, over 100 ms old in state 0, when
    /// the owner has a newer number (nBehind = the owner's newest index - its own).
    /// </summary>
    void UpdateFollowers()
    {
        if (followers.Count == 0) { followerCounter.Clear(); return; }
        for (int i = 0; i < followers.Count; i++)
        {
            var e = followers[i];
            bool remove = e.Start >= 0 && now - e.Start > LifeMs;
            if (!remove)
            {
                if (e.Follow() is not { } p) remove = true;
                else
                {
                    if (e.Start < 0) e.Start = now;
                    if (!WithinShowDistance(e.Owner is Node3D o ? o.GlobalPosition : p))
                    {
                        if (!e.Clip.Gone) e.Clip.Wrap.Visible = false;
                        remove = true;
                    }
                    else if (camera.IsPositionBehind(p)) { if (!e.Clip.Gone) e.Clip.Wrap.Visible = false; }
                    else
                    {
                        var stage = camera.UnprojectPosition(p);
                        if (followerCounter.TryGetValue(e.Owner, out var c) && c.Index - e.Index > 0 && e.State == 0 && now - e.Start > JumpMs) Jump(e);
                        stage.Y += PitchShift(p) * u;            // GetHeightOffset; + PCBase::m_f178 = OffsetPcDamagenumber, 0
                        if (!e.Clip.Gone)
                        {
                            var w = e.Clip.Wrap;
                            w.Position = UiScale.StagePoint(stage, u);
                            w.Scale = new Vector2(u, u);             // FUN_141914c40: 1 for the own PC
                            w.Visible = true;
                        }
                    }
                }
            }
            if (remove) { followers.RemoveAt(i--); Remove(e, followerCounter); }
        }
    }

    /// <summary>gotoAndPlay("_next") on the ani (a no-op on an ani without that label: the clip plays on), state 1, no more jumping.</summary>
    void Jump(Entry e)
    {
        if (!e.Clip.Gone && e.Clip.Ani.HasLabel("_next")) e.Clip.Frame = e.Clip.Ani.Labels["_next"];
        e.State = 1;
        e.CanJump = false;
    }

    /// <summary>FUN_141914a90: 1 unless the point is farther from the camera than the player is.</summary>
    float DistanceScale(Vector3 p)
    {
        if (player == null) return 1;
        var eye = camera.GlobalPosition;
        if (eye.DistanceTo(p) <= eye.DistanceTo(player.GlobalPosition)) return 1;
        return 0.5f + 0.5f * Mathf.Clamp((showDistance - p.DistanceTo(player.GlobalPosition)) / showDistance, 0, 1);
    }

    /// <summary>FUN_1412762d0, as for the name tags (<see cref="PlayerNameTag.PitchShift"/>).</summary>
    float PitchShift(Vector3 anchor) => PlayerNameTag.PitchShift(field, anchor);
}
