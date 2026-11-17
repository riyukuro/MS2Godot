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
/// <item>Spawn (damage): the target's Eff_Damage point, stored once; no jitter. Recover numbers follow the owner's Eff_Damage
/// every frame. A number is not created when the point is behind the camera; off-screen points still spawn.</item>
/// <item>Stack (damage, per target): every number takes the target's next index; the newest 3 are visible (25 over all
/// targets); a visible number with newer ones above it (nBehind) and older than 100 ms jumps once: gotoAndPlay("_next"), a
/// no-op on an ani without that label (the clip plays on), and is no longer moved. The others are raised by
/// (nBehind - 1) lines of DamageGap (30). The entry leaves the list after 1000 ms; the clip removes itself at its ani's
/// RemoveAni frame.</item>
/// <item>Scale: 1 when the point is no farther from the camera than the player, else 0.5 + 0.5 x clamp((2500 - d) / 2500)
/// with d its distance to the player; recover numbers on the own PC are always 1.</item>
/// <item>Place: the screen point in stage units, plus FUN_1412762d0 (the camera-pitch shift) for the own PC's numbers. An
/// npc's numbers hang from <see cref="NpcPresence.Anchor"/> plus the npc's Eff_Damage - Eff_UI dummy delta: the anchor
/// the npc's name tag and HP bar share stands in for the Eff_UI point and its shifts.</item>
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
        public bool OwnPc;
    }

    readonly UiMovie movie;
    readonly float u = 1;
    readonly List<Entry> normals = new(), followers = new();
    readonly Dictionary<object, int> normalIndex = new(), followerIndex = new();
    readonly List<Clip> clips = new();
    readonly Ms2.Formats.Pack.PackSet packs;
    readonly Dictionary<NpcRow, Vector3> npcDelta = new();
    double now;                        // ms
    Camera3D camera;
    Field field;
    Player player;
    NpcPresence npcs;
    IReadOnlyList<EffectDummy> pcDummies;
    Node3D pcModel;

    public DamageText(UiAssets ui, Vector2 window)
    {
        Name = "DamageText";
        Layer = 5;                     // with the name tags and HP bars, added after them; the client's order of the movies is not known
        Offset = UiScale.MovieOrigin;
        packs = ui.Packs;
        u = UiScale.For(window);
        try { movie = new UiMovie(ui, "uidamagetext"); } catch (Exception e) { GD.PushWarning("damage text movie: " + e.Message); }
    }

    public DamageText() { }

    /// <summary>A hit on an npc by the local player (grade 0); other grades are not ported.</summary>
    public void OnNpc(FieldNpcs.Placed n, long value, int grade)
    {
        if (grade != 0 || !Ready()) return;
        if (npcs == null) return;
        var p = npcs.Anchor(n) + NpcDelta(n);
        Spawn(n, p, null, "Monster", value.ToString(), NpcNormal, false);
    }

    /// <summary>A hit (grade 0) or a heal (grade 5) on the own PC.</summary>
    public void OnPlayer(long value, int grade)
    {
        if (!Ready() || player == null) return;
        var point = PcPoint();
        if (point == null) return;
        if (grade == 0) Spawn(player, point.Value, null, "User", Math.Abs(value).ToString(), PcNormal, true);
        else if (grade == 5) Spawn(player, point.Value, PcPoint, "Recover", "+" + Math.Abs(value), Recover, true);
    }

    bool Ready()
    {
        if (movie == null) return false;
        camera ??= GetViewport().GetCamera3D();
        field ??= Find<Field>(GetTree().Root);
        player ??= Find<Player>(GetTree().Root);
        npcs ??= Find<NpcPresence>(GetTree().Root);
        if (player != null && pcModel == null)
        {
            pcDummies = EffectDummyTable.Load(packs).Pc(player.Gender);
            pcModel = player.Visual?.GetNodeOrNull<Node3D>("Model");
        }
        return camera != null;
    }

    /// <summary>The own PC's Eff_Damage point, its position when the dummy is missing.</summary>
    Vector3? PcPoint() => player == null ? null : DummyAnchor.Point(pcDummies, "Eff_Damage", pcModel, player.RootName) ?? player.GlobalPosition;

    /// <summary>The npc's Eff_Damage point less its Eff_UI point (Eff_Damage hangs under Eff_UI, which hangs under the model root).</summary>
    Vector3 NpcDelta(FieldNpcs.Placed n)
    {
        if (npcDelta.TryGetValue(n.Row, out var d)) return d;
        var model = n.Body?.GetNodeOrNull<Node3D>("Model");
        var dmg = DummyAnchor.Point(n.Row.EffectDummies, "Eff_Damage", model, "Scene Root");
        var ui = DummyAnchor.Point(n.Row.EffectDummies, "Eff_UI", model, "Scene Root");
        d = dmg is { } a && ui is { } b ? a - b : Vector3.Zero;
        return npcDelta[n.Row] = d;
    }

    void Spawn(object owner, Vector3 pos, Func<Vector3?> follow, string set, string text, Ani ani, bool ownPc)
    {
        if (camera.IsPositionBehind(pos)) return;       // world -> screen failed: nothing is created
        var clip = Build(set, text, ani);
        if (clip == null) return;
        var e = new Entry { Owner = owner, Pos = pos, Follow = follow, Clip = clip, OwnPc = ownPc };
        bool follower = follow != null;
        var counters = follower ? followerIndex : normalIndex;
        e.Index = counters.TryGetValue(owner, out var i) ? (i + 1) % MaxIndex : 0;
        counters[owner] = e.Index;
        var list = follower ? followers : normals;
        // InsertDamageInfo: the local player's numbers go to the head (all ported ones are the local player's); 35 at most
        list.Insert(0, e);
        if (list.Count > MaxList) { Drop(list[^1]); list.RemoveAt(list.Count - 1); }
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

    void UpdateNormals()
    {
        int shown = 0;
        float line = Line;   // floor(30 x 100 / 100) for the local player's numbers
        for (int i = 0; i < normals.Count; i++)
        {
            var e = normals[i];
            if (e.Start < 0) e.Start = now;
            if (now - e.Start > LifeMs) { Drop(e); normals.RemoveAt(i--); continue; }
            if (!e.CanJump) continue;
            int latest = normalIndex[e.Owner];
            int behind = (latest - e.Index + MaxIndex) % MaxIndex;
            bool inWindow = behind < JumpCount;
            if (!inWindow || shown >= MaxShown)   // crits, never hidden, are not ported
            {
                // hidden: state 3, the clip hidden and frozen
                e.State = 3;
                if (!e.Clip.Gone) e.Clip.Wrap.Visible = false;
                continue;
            }
            shown++;
            e.State = 0;
            if (e.Clip.Gone) continue;
            var w = e.Clip.Wrap;
            var stage = camera.UnprojectPosition(e.Pos);
            stage.Y -= Math.Max(0, behind - 1) * line * u;
            if (behind > 0 && now - e.Start > JumpMs)
            {
                if (e.Clip.Ani.HasLabel("_next") && !e.Clip.Gone) e.Clip.Frame = e.Clip.Ani.Labels["_next"];
                e.State = 1;
                e.CanJump = false;
            }
            if (e.OwnPc) stage.Y += PitchShift(e.Pos) * u;
            w.Position = stage.Round();
            float s = DistanceScale(e.Pos);
            w.Scale = new Vector2(s * u, s * u);
            w.Visible = true;
        }
    }

    void UpdateFollowers()
    {
        for (int i = 0; i < followers.Count; i++)
        {
            var e = followers[i];
            if (e.Start < 0) e.Start = now;
            if (now - e.Start > LifeMs) { Drop(e); followers.RemoveAt(i--); continue; }
            if (!e.CanJump) continue;
            if (e.Follow() is not { } p || camera.IsPositionBehind(p)) continue;
            if (e.Clip.Gone) continue;
            var w = e.Clip.Wrap;
            int behind = (followerIndex[e.Owner] - e.Index + MaxIndex) % MaxIndex;
            if (behind > 0 && now - e.Start > JumpMs)
            {
                if (e.Clip.Ani.HasLabel("_next") && !e.Clip.Gone) e.Clip.Frame = e.Clip.Ani.Labels["_next"];
                e.State = 1;
                e.CanJump = false;
            }
            var stage = camera.UnprojectPosition(p);
            stage.Y += PitchShift(p) * u;            // PCBase::m_f178 is taken as 0
            w.Position = stage.Round();
            w.Scale = new Vector2(u, u);             // FUN_141914c40: 1 for the own PC
            w.Visible = true;
        }
    }

    /// <summary>FUN_141914a90: 1 unless the point is farther from the camera than the player is.</summary>
    float DistanceScale(Vector3 p)
    {
        if (player == null) return 1;
        var eye = camera.GlobalPosition;
        if (eye.DistanceTo(p) <= eye.DistanceTo(player.GlobalPosition)) return 1;
        return 0.5f + 0.5f * Mathf.Clamp((Distance - p.DistanceTo(player.GlobalPosition)) / Distance, 0, 1);
    }

    /// <summary>FUN_1412762d0, as for the own name tag (<see cref="PlayerNameTag"/>).</summary>
    float PitchShift(Vector3 anchor)
    {
        if (field?.CameraEye is not { } eye) return 0;
        float pitch = Mathf.Clamp(Mathf.RadToDeg(field.CameraPitch), 0, 90);
        float d = new Vector2(anchor.X - eye.X, anchor.Z - eye.Z).Length();
        float r = d > Field.MinCameraDistance ? Field.MinCameraDistance / d : 1f;
        return Mathf.Floor((-40f + 70f * pitch / 90f) * r);
    }

    static T Find<T>(Node n) where T : Node
    {
        if (n is T t) return t;
        foreach (var c in n.GetChildren()) if (Find<T>(c) is { } f) return f;
        return null;
    }
}
