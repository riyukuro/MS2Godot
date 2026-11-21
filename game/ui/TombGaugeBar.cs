using Godot;
using Ms2.Formats.World;
using Ms2Godot.Movement;

namespace Ms2Godot.Ui;

/// <summary>
/// A dead PC's tomb hit counter, the own PC's (the HUD's) or another player's (the remote layer's): UITombStoneBar (ui id 0x4e64, movie uitombgaugebar, AS class TombGaugeBar), one
/// Entity per PC made from the cache linkage "tombGaugeBar" (UITombStoneBar ctor, uihpbar.cpp:555). PC::FUN_141657730
/// (pc.cpp:1623) gets it, none while the revival is blocked.
/// <list type="bullet">
/// <item>Show (FUN_141992e40, from PC::FUN_14165cac0 once the tomb is built): hit and max back to 5, the keep-alive on
/// (FUN_141992140(1), FUN_140f49470), active (vfunc_2); the open (vfunc_5) plays "start".</item>
/// <item>A hit (FUN_141992ea0 from the Tombstone packet): "hit{stage}" for a stage below 5 (<see cref="Ms2.Ui.Hud.Tombstone.GaugeStage"/>).
/// The break hides it (FUN_14165d550: vfunc_3, the keep-alive off).</item>
/// <item>Hidden (vfunc_8, uihpbar.cpp:484) when the PC is not dead, the revival is blocked, there is no tomb, the
/// keep-alive is off, or the camera eye is farther than HpBarDistance from the PC (m_54).</item>
/// <item>Place (vfunc_10, vfunc_6): the tomb's x and y with the PC's Eff_UI anchor's height,
/// moved down by OffsetPcHpbar and FUN_1412762d0's camera shift, 35 up, in stage units at the movie scale.</item>
/// </list>
/// STILL-OPEN: m_50 (vfunc_8's early show) is not known and taken as 0; PC::vfunc_122's PvP relation is not ported (no
/// PvP state: 0); the end0-end4 labels are not played by any decompiled caller.
/// </summary>
public sealed partial class TombGaugeBar : CanvasLayer, IMovieLayer
{
    public const string Movie = "uitombgaugebar", Linkage = "tombGaugeBar";

    readonly UiMovie movie;
    readonly Func<Vector3?> anchor;
    readonly float offset, distance;
    float u = 1;
    MovieClipNode clip;
    PlayerTombstone tomb;
    bool keepAlive, needsOpen;
    Camera3D camera;
    Field field;

    /// <summary>The own tomb (null without one), whether the own PC is dead, whether the revival is blocked (PC::IsRevivalBlocked).</summary>
    public Func<PlayerTombstone> Tomb = () => null;
    public Func<bool> Dead = () => false, RevivalBlocked = () => false;

    /// <summary><paramref name="anchor"/>: the PC's Eff_UI point (null while it has none).</summary>
    public TombGaugeBar(UiAssets ui, ClientConstants constants, Func<Vector3?> anchor, Vector2 window)
    {
        Name = "TombGaugeBar";
        Layer = 5;
        MovieLayerHost.Bind(this, Ms2.Ui.CommonDepth.HpBar);
        Offset = UiScale.MovieOrigin;
        this.anchor = anchor;
        offset = constants.Float("OffsetPcHpbar", 32);
        distance = constants.Float("HpBarDistance", 400);
        u = UiScale.For(window);
        try { movie = new UiMovie(ui, Movie); } catch (Exception e) { GD.PushWarning("tomb gauge bar movie: " + e.Message); }
    }

    public TombGaugeBar() { }

    public override void _EnterTree() => AddToGroup(IMovieLayer.Group);

    /// <summary><see cref="IMovieLayer"/>: the new viewport scale. STILL-OPEN: the built clip is only scaled.</summary>
    public void Resize(Vector2 window)
    {
        u = UiScale.For(window);
        if (clip != null) clip.Scale = new Vector2(u, u);
    }

    /// <summary>The field was swapped under the HUD: camera and field are found again.</summary>
    public void FieldChanged() { camera = null; field = null; }

    /// <summary>The clip shown (for the checks): null before the first show.</summary>
    public MovieClipNode Clip => clip;
    /// <summary>The last label played (for the checks).</summary>
    public string LastLabel { get; private set; }

    void Play(string label)
    {
        LastLabel = label;
        clip?.GotoLabel(label, animate: true);
    }

    /// <summary>FUN_141992e40.</summary>
    void Open()
    {
        if (movie == null) return;
        if (clip == null)
        {
            clip = movie.BuildClass(Linkage, u);
            if (clip == null) return;
            clip.Scale = new Vector2(u, u);
            clip.Visible = false;
            AddChild(clip);
        }
        keepAlive = true;
        needsOpen = true;
    }

    bool Hidden()
    {
        if (!Dead()) return true;
        if (RevivalBlocked() || tomb == null || !IsInstanceValid(tomb)) return true;
        if (!keepAlive) return true;
        if (anchor?.Invoke() == null) return true;
        var eye = field?.CameraEye ?? camera.GlobalPosition;
        return eye.DistanceTo(tomb.GlobalPosition) > distance;      // FUN_140f48e20 > m_54 (the tomb stands at the PC)
    }

    public override void _Process(double delta)
    {
        var t = Tomb();
        if (t != null && !IsInstanceValid(t)) t = null;
        if (t != tomb)
        {
            tomb = t;
            keepAlive = false;
            if (t != null && !RevivalBlocked()) Open();      // PC::FUN_141657730 gives no entity while blocked
        }
        if (tomb != null)
        {
            if (tomb.PendingStage is int stage && !RevivalBlocked()) Play("hit" + stage);   // a packet, not a frame
            tomb.PendingStage = null;
            if (tomb.State.State == Ms2.Ui.Hud.Tombstone.Break) keepAlive = false;     // FUN_14165d550: vfunc_3
        }
        if (clip == null) return;
        camera ??= GetViewport().GetCamera3D();
        field ??= GetTree().GetFirstNodeInGroup(Field.Group) as Field;
        if (camera == null || Hidden()) { if (clip.Visible) { clip.Visible = false; needsOpen = true; } return; }
        var a = anchor().Value;
        var at = new Vector3(tomb.GlobalPosition.X, a.Y, tomb.GlobalPosition.Z);   // vfunc_10: the tomb's x,y, the anchor's z
        if (camera.IsPositionBehind(at)) { clip.Visible = false; needsOpen = true; return; }
        clip.Visible = true;
        if (needsOpen) { needsOpen = false; Play("start"); }       // vfunc_5
        var p = camera.UnprojectPosition(at);
        p.Y += (offset + PlayerNameTag.PitchShift(field, at) - 35f) * u;
        clip.Position = UiScale.StagePoint(p, u);
    }
}
