using Godot;

namespace Ms2Godot.World;

/// <summary>
/// A camera mode's setting (Unk_CameraSetting, cameramode.h:50): field of view, the depth of field's near and far
/// distances, position and rotation in MS2 axes (z up; the rotation in degrees, <see cref="Ms2.Net.CameraMath.FromEuler"/>),
/// and whether the projection is orthographic.
/// </summary>
public struct CameraSetting
{
    public float Fov, DofNear, DofFar;
    public Vector3 Position, Rotation;
    public bool Ortho;

    /// <summary>
    /// from.Scaled(1 - t).Added(to.Scaled(t)) (camera.cpp:357-413): every number moves linearly; Scaled and Added keep
    /// the left side's ortho flag, so the result has from's.
    /// </summary>
    public static CameraSetting Lerp(in CameraSetting from, in CameraSetting to, float t) => new()
    {
        Fov = from.Fov * (1f - t) + to.Fov * t,
        DofNear = from.DofNear * (1f - t) + to.DofNear * t,
        DofFar = from.DofFar * (1f - t) + to.DofFar * t,
        Position = from.Position * (1f - t) + to.Position * t,
        Rotation = from.Rotation * (1f - t) + to.Rotation * t,
        Ortho = from.Ortho,
    };

    /// <summary>The Godot camera transform of the setting: the rotation's view direction, up and right vectors
    /// (<see cref="Ms2.Net.CameraMath.FromEuler"/>) and the position, each taken from MS2 axes (x, y, z) to (x, z, -y).</summary>
    public readonly Transform3D Transform()
    {
        var m = Ms2.Net.CameraMath.FromEuler(Rotation.X, Rotation.Y, Rotation.Z);
        Vector3 Col(int c) => new((float)m[0, c], (float)m[2, c], (float)-m[1, c]);
        var dir = Col(0);
        // Godot's camera looks along -z with +y up and +x right
        return new Transform3D(new Basis(Col(2), Col(1), -dir), new Vector3(Position.X, Position.Z, -Position.Y));
    }
}

/// <summary>A camera move between two settings (Unk_CameraFlyBy): its interpolator kind, duration in seconds, from and to.</summary>
public struct CameraFlyBy
{
    public int Kind;
    public float Duration;
    public CameraSetting From, To;
}

/// <summary>The field's follow camera (CTargetCameraMode) as the camera manager reads it.</summary>
public interface ITargetCamera
{
    /// <summary>The mode's setting (its fov, near, far, rotation, position and ortho getters).</summary>
    CameraSetting Setting { get; }
}

/// <summary>The follow camera's side of the local camera mode (0x10, <see cref="Ms2.Ui.LocalTargetCamera"/>), in MS2 axes.</summary>
public interface ILocalCameraFollow
{
    /// <summary>What the local mode's constructor reads of the follow mode (localtargetcameramode.cpp:80-131).</summary>
    Ms2.Ui.LocalCameraPrev LocalPrev { get; }
    /// <summary>m_50: the follow mode's distance (its vfunc_7 keeps it in g_143aef240 when m_bc is set).</summary>
    float FollowDistance { get; }
    /// <summary>The local mode's vfunc_7 (FUN_14183dfe0: m_c0 = shown, m_f8 = 1) and the follow mode's vfunc_6(1) after it
    /// (targetcameramode.cpp:137-160: m_f0 = 1): the follow camera eases on from the local mode's shown camera, its own
    /// distance and turn as they were.</summary>
    void TakeShown(Ms2.Ui.CameraPose shown);
    /// <summary>CameraMgr::SetTarget(player, 1) (camera.cpp:882-899) into the follow mode's vfunc_24 (targetcameramode.cpp:
    /// 364-397): m_f0 = 0, so the follow camera is placed at once, at <paramref name="distance"/> (null: the farthest, m_54)
    /// and the field's turn (m_68 = m_48).</summary>
    void Retarget(float? distance);
}

/// <summary>
/// The part of CameraMgr (camera.cpp) the camera trigger objects drive: the follow mode (2, <see cref="ITargetCamera"/>),
/// the lock mode (4, CLockCameraMode, which does nothing per frame, lockcameramode.cpp:27) and the fly-by mode (3,
/// CFlyByCameraMode, flybycameramode.cpp) between them. A mode keeps the setting it last applied, and a new one starts
/// from the previous one's (CCameraMode::FUN_140afdaa0, cameramode.cpp:277-292).
/// <see cref="Shown"/> is what the camera shows while the mode is not the follow one.
/// STILL-OPEN: the action camera (mode 5, CActionCameraMode with a camera path row) and the other modes; the depth of
/// field (FUN_140af0b70, FUN_140af99d0 / FUN_140af9950 apply near and far to the DoF post effect), the orthographic
/// projection, the camera effect state and FUN_140af1a90 / FUN_140af7be0; the remembered setting (RememberSetting,
/// m_248) and what reads it; the PC's FUN_1416781a0 on a mode change; the fly-by's mid-point helper FUN_14121f910.
/// The local camera mode (0x10, CLocalTargetCameraMode) runs over the field's local camera setups (<see cref="Setups"/>):
/// the trigger's LocalCamera packet enters and leaves an Operate 2 setup (<see cref="EnterLocal"/>), and each frame the
/// auto check enters an Operate 1 or 3 setup the player stands in (<see cref="AutoCheck"/>).
/// STILL-OPEN (local mode): entering from a mode other than the follow one (the fly-by, lock or another local mode: the
/// constructor's non-target branch and m_1ec of that mode), which does nothing here; a fly-by toward or from the local mode;
/// the camera's target an NPC (the packet's object id: SetTarget(npc), FUN_141bdb5a0, FUN_141bd9e60, m_27c); the order of
/// the auto check (WorldService's update, worldservice_1.cpp:9778) against CameraMgr::Update, taken as before it.
/// </summary>
public sealed class TriggerCamera
{
    public const int Target = 2, FlyBy = 3, Lock = 4, Action = 5, LocalTarget = 0x10;

    readonly ITargetCamera target;
    int mode = Target;          // the current mode's type (GetModeType, camera.h:248)
    int flyToMode;              // CFlyByCameraMode::m_nMode
    CameraSetting modeSetting;  // the current lock / fly-by mode's m_setting
    CameraSetting setting;      // m_setting
    bool flyBy;                 // m_bFlyBy
    CameraFlyBy fly;            // m_flyBy
    int pendingMode;            // m_nPendingMode
    float interpElapsed, elapsed, duration;
    ILocalCameraFollow follow;
    Ms2.Ui.ILocalCameraTarget localTarget;
    System.Func<int, System.Numerics.Vector3, bool> localBoxContains;   // localTarget.BoxContains, made once in Bind: AutoCheck runs per frame
    Ms2.Ui.LocalTargetCamera local;
    bool keepDistance;          // the follow mode's m_bc (FUN_14183cc80)
    float keptDistance;         // g_143aef240

    /// <summary>m_entitySetups: the field's local camera setups (WorldService::FUN_141bbd640 at the field load).</summary>
    public readonly List<Ms2.Ui.LocalCameraSetup> Setups = new();
    /// <summary>The running local mode while the mode is <see cref="LocalTarget"/>.</summary>
    public Ms2.Ui.LocalTargetCamera Local => mode == LocalTarget ? local : null;

    /// <summary>The trigger service's FUN_1418631a0 (RunNextCameraStep), which Update calls after a pending mode.</summary>
    public System.Action RunNextCameraStep;

    public TriggerCamera(ITargetCamera target) { this.target = target; }

    /// <summary>Binds the follow camera's local-mode side and the camera target (the own player).</summary>
    public void Bind(ILocalCameraFollow f, Ms2.Ui.ILocalCameraTarget t) { follow = f; localTarget = t; localBoxContains = t != null ? t.BoxContains : null; }

    /// <summary>GetModeType: 2 follow, 3 fly-by, 4 lock.</summary>
    public int ModeType => mode;
    /// <summary>The mode a running fly-by ends in.</summary>
    public int FlyByMode => flyToMode;
    /// <summary>The mode set after the current fly-by, applied on the next <see cref="Update"/> (0: none).</summary>
    public int PendingMode => pendingMode;
    /// <summary>The current mode's setting (the follow mode's own while it is current).</summary>
    public CameraSetting Current => mode == Target ? target.Setting : mode == LocalTarget ? LocalSetting() : modeSetting;
    /// <summary>The current mode's setting while it is not the follow mode.</summary>
    public CameraSetting? Shown => mode == Target ? null : mode == LocalTarget ? LocalSetting() : modeSetting;

    // the local mode's m_setting: its fov and the shown camera (vfunc_39 puts it there)
    CameraSetting LocalSetting() => new()
    {
        Fov = local.Fov, DofNear = local.PreviousSetting.DofNear, DofFar = local.PreviousSetting.DofFar,
        Position = V(local.Shown.Position), Rotation = V(local.Shown.Rotation),
    };

    static Vector3 V(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
    /// <summary>The follow mode's setting (GetTargetMode's getters).</summary>
    public CameraSetting TargetSetting => target.Setting;
    /// <summary>m_setting, the setting the next mode applies.</summary>
    public CameraSetting Setting => setting;
    public CameraFlyBy FlyByData => fly;

    /// <summary>SetPendingMode (camera.h:280): m_nPendingMode = <paramref name="n"/>; the next <see cref="Update"/> sets it.</summary>
    public void SetPendingMode(int n) => pendingMode = n;

    /// <summary>FUN_140aed720 (camera.h:264): m_setting = setting.</summary>
    public void SetSetting(in CameraSetting s) => setting = s;

    /// <summary>MakeFlyBy (@0x140aeb1a0, camera.cpp:1252-1270): kind 1, the duration, from the current mode's setting to <paramref name="to"/>.</summary>
    public CameraFlyBy MakeFlyBy(float dur, in CameraSetting to) => new() { Kind = 1, Duration = dur, From = Current, To = to };

    /// <summary>SetFlyBy (@0x140aed580, camera.cpp:1018-1036): the next SetMode flies when the kind is not 0. Without a
    /// fly-by given, from is the current mode's setting.</summary>
    public void SetFlyBy(in CameraFlyBy? fb, int kind, float dur)
    {
        flyBy = kind != 0;
        if (fb is { } f) fly = f;
        else { fly.Kind = kind; fly.Duration = dur; fly.From = Current; }
    }

    /// <summary>
    /// SetMode (camera.cpp:1997-2185) for modes 2 and 4 (3 and the others are ignored). With a fly-by set, a fly-by
    /// toward the mode starts from the current setting (m_setting takes it); with <paramref name="keep"/> its end takes
    /// the mode's fov, near, far, ortho and position, but not its rotation. The position is the mode's stored one
    /// (camera.cpp:2068): the follow mode's raised target plus eye offset (:2076-2083) is taken only when the current mode
    /// is already the follow mode, and this is called from the lock mode (GetModeType, camera.h:248, gives 4); the
    /// recomputed eye of :2086 is taken only without a camera target, which a field always has (its player). Without one the mode starts at once: the follow mode with its own setting (m_setting takes it), the
    /// lock mode with m_setting.
    /// </summary>
    public void SetMode(int nMode, bool keep)
    {
        if (nMode is not (Target or Lock or LocalTarget)) return;
        if ((flyBy || mode == FlyBy) && (nMode == LocalTarget || mode == LocalTarget)) return;   // STILL-OPEN: no fly-by with the local mode
        pendingMode = 0;
        // the old mode's vfunc_7: the local mode started from the follow one hands it its shown camera
        if (mode == LocalTarget && nMode != LocalTarget && local.PreviousType == Target) follow?.TakeShown(local.Shown);
        if (flyBy)
        {
            setting = Current;
            if (keep)
            {
                // spNext: the follow mode, or a new lock mode, which starts from the current mode's setting
                var next = nMode == Target ? target.Setting : Current;
                fly.To.Fov = next.Fov;
                fly.To.DofNear = next.DofNear;
                fly.To.DofFar = next.DofFar;
                fly.To.Position = next.Position;
                fly.To.Ortho = next.Ortho;
            }
            modeSetting = Current;
            mode = FlyBy;
            flyToMode = nMode;
            flyBy = false;
            // CFlyByCameraMode::vfunc_6 (flybycameramode.cpp:115-150): a new interpolator over m_flyBy
            interpElapsed = elapsed = 0;
            duration = fly.Duration;
            return;
        }
        if (nMode == Target)
        {
            mode = Target;
            setting = target.Setting;
            local = null;
            return;
        }
        if (nMode == LocalTarget)
        {
            // the follow mode's vfunc_7 (targetcameramode.cpp:162-166): g_143aef240 = m_bc ? m_50 : 0
            if (mode == Target) keptDistance = keepDistance ? follow.FollowDistance : 0;
            // the constructor over the follow mode, then vfunc_11 .. vfunc_5 of m_setting (the follow mode's own, so they
            // change nothing) and vfunc_6(1)
            local = new Ms2.Ui.LocalTargetCamera(follow.LocalPrev);
            mode = LocalTarget;
            return;
        }
        local = null;
        mode = Lock;
        modeSetting = setting;
    }

    /// <summary>CameraMgr::Update (camera.cpp:819-825): a pending mode is set (with bKeep) and the next camera step runs;
    /// then the mode's frame.</summary>
    public void Update(float dt, int zoom = 0)
    {
        AutoCheck();
        if (pendingMode != 0)
        {
            SetMode(pendingMode, true);
            pendingMode = 0;
            RunNextCameraStep?.Invoke();
        }
        if (mode == FlyBy) FlyFrame(dt);
        else if (mode == LocalTarget)
        {
            local.Frame(dt, zoom, localTarget);
            if (local.PendingMode != 0) { pendingMode = local.PendingMode; local.PendingMode = 0; }
        }
    }

    /// <summary>FUN_140aed0d0 / m_24c: the local cameras are on (the field property kind 10 turns them off).</summary>
    public bool LocalCamerasOn = true;

    /// <summary>
    /// CameraMgr::FUN_140aecbb0 (@0x140aecbb0, camera.cpp:1143-1194), the LocalCamera packet: the first Operate 2 setup of
    /// that id (none for id 0 or an unknown id: nothing happens). In the follow mode the follow mode keeps its distance for
    /// the leave (FUN_14183cc80(1): m_bc = 1). Enter: no fly-by, m_setting = the current mode's, the local mode, and its
    /// Start with the setup. Leave: FUN_140aed260 (:1331-1346) back to the mode the local one started from (the follow
    /// one), then SetTarget(player, 1), which places the follow camera at once at the kept distance (m_bc with a kept
    /// distance) or the farthest. Returns false when the id names no setup.
    /// </summary>
    public bool EnterLocal(int id, int objectId, bool enter)
    {
        if (id == 0 || Ms2.Ui.LocalCameraSetup.FindTrigger(Setups, id) is not { } s) return false;
        bool wasTarget = mode == Target;
        if (wasTarget) keepDistance = true;
        if (enter)
        {
            if (!wasTarget || follow == null) return true;   // STILL-OPEN: entering from another mode
            StartLocal(s);
            // STILL-OPEN: objectId != 0 (the camera's target an NPC)
        }
        else
        {
            if (mode == LocalTarget)
            {
                flyBy = false;
                int n = local.PreviousType != 0 ? local.PreviousType : Target;
                setting = ToSetting(local.PreviousSetting);
                SetMode(n, true);
            }
            // SetTarget(player, 1): m_spMode->vfunc_24, the follow mode's when it is current
            if (mode == Target) follow?.Retarget(keepDistance && keptDistance != 0 ? keptDistance : null);
            if (wasTarget) keepDistance = false;
        }
        return true;
    }

    /// <summary>
    /// CameraMgr::FUN_140aece50 (@0x140aece50, camera.cpp:1197-1238): with the local cameras on, setups held, and the
    /// follow mode current (FUN_140aed4b0 holds for it, IsLocalTargetMode does not), the first Operate 1 setup within its
    /// Scope of the player or Operate 3 setup whose trigger box holds the player starts the local mode.
    /// </summary>
    public void AutoCheck()
    {
        if (!LocalCamerasOn || Setups.Count == 0 || mode != Target || follow == null) return;
        if (localTarget?.Position is not { } p) return;
        if (Ms2.Ui.LocalCameraSetup.FindAuto(Setups, p, localBoxContains) is { } s) StartLocal(s);
    }

    void StartLocal(in Ms2.Ui.LocalCameraSetup s)
    {
        flyBy = false;
        setting = Current;
        SetMode(LocalTarget, true);
        local?.Start(s, localTarget);
    }

    static CameraSetting ToSetting(in Ms2.Ui.LocalCameraSetting s) => new()
    {
        Fov = s.Fov, DofNear = s.DofNear, DofFar = s.DofFar, Position = V(s.Position), Rotation = V(s.Rotation),
    };

    /// <summary>
    /// CFlyByCameraMode::vfunc_8 (@0x14121f260, flybycameramode.cpp:74-89) over the interpolator of the fly-by's kind
    /// (vfunc_6: 1 linear, 2 log, 3 exp, any other linear; flybycameramode.cpp:209-277): t = elapsed / duration, eased
    /// by FUN_141b6fdb0 (util.cpp:768-779) for log and exp; past 1 the interpolator calls back (FUN_14121f9f0: m_setting
    /// takes the mode's setting, the previous frame's, and the fly-by's mode becomes pending) and gives to, else
    /// from (1 - t) + to t. The rotation instead goes by the linear ratio, at most 1, through FUN_140afdec0.
    /// </summary>
    void FlyFrame(float dt)
    {
        interpElapsed += dt;
        float t = interpElapsed / fly.Duration;
        if (fly.Kind == 2) t = Mathf.Log(t * 10f + 1f) / 2.397895f;
        else if (fly.Kind == 3) t = (Mathf.Exp(t * 5f) - 1f) / 147.41316f;
        CameraSetting data;
        if (t > 1f)
        {
            setting = modeSetting;
            pendingMode = flyToMode;
            data = fly.To;
        }
        else data = CameraSetting.Lerp(fly.From, fly.To, t);
        elapsed += dt;
        float ratio = Mathf.Min(elapsed / duration, 1f);
        var (rx, ry, rz) = Ms2.Net.CameraMath.RotationLerp(ratio, (fly.From.Rotation.X, fly.From.Rotation.Y, fly.From.Rotation.Z),
                                                          (fly.To.Rotation.X, fly.To.Rotation.Y, fly.To.Rotation.Z));
        data.Rotation = new Vector3(rx, ry, rz);
        modeSetting = data;
    }
}
