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
/// </summary>
public sealed class TriggerCamera
{
    public const int Target = 2, FlyBy = 3, Lock = 4, Action = 5;

    readonly ITargetCamera target;
    int mode = Target;          // the current mode's type (GetModeType, camera.h:248)
    int flyToMode;              // CFlyByCameraMode::m_nMode
    CameraSetting modeSetting;  // the current lock / fly-by mode's m_setting
    CameraSetting setting;      // m_setting
    bool flyBy;                 // m_bFlyBy
    CameraFlyBy fly;            // m_flyBy
    int pendingMode;            // m_nPendingMode
    float interpElapsed, elapsed, duration;

    /// <summary>The trigger service's FUN_1418631a0 (RunNextCameraStep), which Update calls after a pending mode.</summary>
    public System.Action RunNextCameraStep;

    public TriggerCamera(ITargetCamera target) { this.target = target; }

    /// <summary>GetModeType: 2 follow, 3 fly-by, 4 lock.</summary>
    public int ModeType => mode;
    /// <summary>The mode a running fly-by ends in.</summary>
    public int FlyByMode => flyToMode;
    /// <summary>The mode set after the current fly-by, applied on the next <see cref="Update"/> (0: none).</summary>
    public int PendingMode => pendingMode;
    /// <summary>The current mode's setting (the follow mode's own while it is current).</summary>
    public CameraSetting Current => mode == Target ? target.Setting : modeSetting;
    /// <summary>The current mode's setting while it is not the follow mode.</summary>
    public CameraSetting? Shown => mode == Target ? null : modeSetting;
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
        if (nMode is not (Target or Lock)) return;
        pendingMode = 0;
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
            return;
        }
        mode = Lock;
        modeSetting = setting;
    }

    /// <summary>CameraMgr::Update (camera.cpp:819-825): a pending mode is set (with bKeep) and the next camera step runs;
    /// then the mode's frame.</summary>
    public void Update(float dt)
    {
        if (pendingMode != 0)
        {
            SetMode(pendingMode, true);
            pendingMode = 0;
            RunNextCameraStep?.Invoke();
        }
        if (mode == FlyBy) FlyFrame(dt);
    }

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
