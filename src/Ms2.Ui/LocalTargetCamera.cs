using System.Numerics;
using Ms2.Formats.World;
using Ms2.Net;

namespace Ms2.Ui;

/// <summary>A camera mode's shown or placed camera (Unk_CameraPose: m_pos, m_rot), in MS2 axes (z up), the rotation in
/// degrees (<see cref="CameraMath.FromEuler(float, float, float)"/>).</summary>
public record struct CameraPose(Vector3 Position, Vector3 Rotation);

/// <summary>A camera mode's setting (Unk_CameraSetting, cameramode.h:50) without the ortho flag: fov, the depth of field's
/// near and far, position and rotation in MS2 axes.</summary>
public record struct LocalCameraSetting(float Fov, float DofNear, float DofFar, Vector3 Position, Vector3 Rotation);

/// <summary>
/// One local camera of the field (Unk_CameraSetup), as WorldService::FUN_141bbd640 (@0x141bbd640,
/// worldservice_1.cpp:10223-10253) reads it from an entity of the Camera model with the MS2LocalTargetCamera mixin and
/// keeps it with CameraMgr::SetEntitySetup (@0x140aed500, camera.cpp:1411). An unset property keeps the flat's default.
/// </summary>
public struct LocalCameraSetup
{
    /// <summary>m_00 .. m_18: FOV, DOFNear, DOFFar, Position, Rotation.</summary>
    public LocalCameraSetting Setting;
    /// <summary>m_28: 1 starts within <see cref="Scope"/> of the position, 2 from the trigger's SetLocalCamera, 3 inside the
    /// trigger box <see cref="BoxId"/>.</summary>
    public int Operate;
    /// <summary>m_2c: the id SetLocalCamera names.</summary>
    public int LocalCameraId;
    /// <summary>m_30, m_34.</summary>
    public float Scope;
    public int BoxId;
    /// <summary>m_38: the camera's distance from the look point; m_3c: the move's time in seconds.</summary>
    public float TargetDistance, InterpolationTime;
    /// <summary>m_40: added to the look point.</summary>
    public Vector3 OffsetPosition;
    /// <summary>m_4c: the player's rotation in degrees, each angle through <see cref="NormalizeDegrees"/>.</summary>
    public Vector3 CharacterAngle;
    /// <summary>m_58: the player turns to <see cref="CharacterAngle"/>; m_59: the player may move.</summary>
    public bool ChangeCharacterAngle, CharacterMovable;
    /// <summary>m_5c, m_60: the ease of the move in and out (<see cref="LocalTargetCamera.Ease"/>).</summary>
    public int StartInterpolator, EndInterpolator;
    /// <summary>m_64, m_68: the zoom steps' range; m_6c: one step's distance.</summary>
    public int ZoomMin, ZoomMax, ZoomValue;

    /// <summary>ContainsModel(0xbcf2d8da) and ContainsModel(0x529425d9): the entity is a local camera.</summary>
    public static bool Is(FieldEntity e) => e.HasMixin("Camera") && e.HasMixin("MS2LocalTargetCamera");

    public static LocalCameraSetup From(FieldEntity e)
    {
        static Vector3 V(FieldEntity e, string key) { var v = e.Vector(key); return new Vector3((float)v.X, (float)v.Y, (float)v.Z); }
        var angle = V(e, "CharacterAngle");
        return new LocalCameraSetup
        {
            Setting = new LocalCameraSetting((float)e.Number("FOV"), (float)e.Number("DOFNear"), (float)e.Number("DOFFar"),
                                             V(e, "Position"), V(e, "Rotation")),
            BoxId = (int)e.Number("BoxID"),
            Scope = (float)e.Number("Scope"),
            LocalCameraId = (int)(uint)e.Number("LocalCameraID"),
            Operate = (int)(uint)e.Number("Operate"),
            TargetDistance = (float)e.Number("TargetDistance"),
            OffsetPosition = V(e, "OffsetPosition"),
            InterpolationTime = (float)e.Number("InterpolationTime"),
            ChangeCharacterAngle = e.Bool("ChangeCharacterAngle"),
            // FUN_140291bc0 (@0x140291bc0, commonutil.cpp:1527)
            CharacterAngle = new Vector3(NormalizeDegrees(angle.X), NormalizeDegrees(angle.Y), NormalizeDegrees(angle.Z)),
            CharacterMovable = e.Bool("CharacterMovable"),
            StartInterpolator = (int)(uint)e.Number("StartInterpolator"),
            EndInterpolator = (int)(uint)e.Number("EndInterpolator"),
            ZoomMin = (int)e.Number("LocalTargetZoomMin"),
            ZoomMax = (int)e.Number("LocalTargetZoomMax"),
            ZoomValue = (int)e.Number("LocalTargetZoomValue"),
        };
    }

    /// <summary>The field's local cameras in the xblock's order.
    /// STILL-OPEN: CameraMgr keeps them in a map keyed by the entity pointer (m_entitySetups), so the client's walk order,
    /// which decides between two setups that both match, is its allocation order.</summary>
    public static List<LocalCameraSetup> All(IEnumerable<FieldEntity> entities)
    {
        var list = new List<LocalCameraSetup>();
        foreach (var e in entities)
            if (Is(e)) list.Add(From(e));
        return list;
    }

    /// <summary>NormalizeDegrees (commonutil.cpp:1518): the whole degrees, mod 360, made positive.</summary>
    public static float NormalizeDegrees(float f)
    {
        int n = (int)f % 360;
        if (n < 0) n += 360;
        return n;
    }

    /// <summary>CameraMgr::FUN_140aecbb0's walk (camera.cpp:1150-1155): the first setup of Operate 2 with that id; id 0
    /// finds none.</summary>
    public static LocalCameraSetup? FindTrigger(IReadOnlyList<LocalCameraSetup> setups, int id)
    {
        if (id == 0) return null;
        foreach (var s in setups)
            if (s.Operate == 2 && s.LocalCameraId == id) return s;
        return null;
    }

    /// <summary>CameraMgr::FUN_140aece50's walk (camera.cpp:1211-1235): the first setup the target stands in, Operate 1 by
    /// the distance to its position (at most Scope) and Operate 3 by its trigger box.</summary>
    public static LocalCameraSetup? FindAuto(IReadOnlyList<LocalCameraSetup> setups, Vector3 target, Func<int, Vector3, bool> boxContains)
    {
        foreach (var s in setups)
        {
            if (s.Operate == 1 && (target - s.Setting.Position).Length() <= s.Scope) return s;
            if (s.Operate == 3 && boxContains(s.BoxId, target)) return s;
        }
        return null;
    }

    /// <summary>
    /// TriggerObject::Contains (@0x140de5f60, triggeractioncli.cpp:400-427) for an MS2TriggerBox: the box of
    /// ShapeDimensions about Position turned by Rotation.z (NxMat34::rotZ, NxBoxContainsPoint), in MS2 axes.
    /// </summary>
    public static bool BoxContains(Vector3 position, Vector3 size, float rotZDeg, Vector3 p)
    {
        var half = size * 0.5f;
        float a = rotZDeg * 3.1415927f / 180f, c = MathF.Cos(a), s = MathF.Sin(a);
        var d = p - position;
        return MathF.Abs(c * d.X + s * d.Y) <= half.X && MathF.Abs(-s * d.X + c * d.Y) <= half.Y && MathF.Abs(d.Z) <= half.Z;
    }
}

/// <summary>The camera target the local mode follows (CameraMgr::FUN_140a38d60), in MS2 axes.</summary>
public interface ILocalCameraTarget
{
    /// <summary>vfunc_17 / vfunc_18: the target's position; null without a target.</summary>
    Vector3? Position { get; }
    /// <summary>The target is a player (DynamicCast&lt;PCBase&gt;).</summary>
    bool IsPc { get; }
    /// <summary>vfunc_20 / vfunc_21 with PCBase::SetTargetYaw: the target's rotation in degrees.</summary>
    Vector3 Rotation { get; set; }
    /// <summary>CameraMgr::FUN_140aed0d0: m_24c, the local cameras are on.</summary>
    bool LocalCamerasOn { get; }
    /// <summary>CTriggerService::ObjectContains (@0x141863100, triggerservice.cpp:784).</summary>
    bool BoxContains(int boxId, Vector3 p);
}

/// <summary>
/// The follow camera (mode 2, CTargetCameraMode) a local mode starts from, as its constructor reads it (CCameraMode::
/// FUN_140afdaa0, cameramode.cpp:277, and CLocalTargetCameraMode's, localtargetcameramode.cpp:98-129), in MS2 axes.
/// </summary>
public struct LocalCameraPrev
{
    /// <summary>vfunc_1: the mode's type.</summary>
    public int Type;
    /// <summary>m_68 (vfunc_23) and m_6c (vfunc_22): the eye's yaw and its angle from the up axis in radians, eye = center +
    /// (d sin yaw, d cos yaw, d cos pitch) (targetcameramode.cpp:287-289).</summary>
    public float Yaw, Pitch;
    /// <summary>m_50 (vfunc_19): the eye's distance in the ground plane.</summary>
    public float Distance;
    /// <summary>m_setting: the shown camera's setting (vfunc_39 puts the shown camera there).</summary>
    public LocalCameraSetting Setting;
    /// <summary>m_78 (vfunc_35): the look point.</summary>
    public Vector3 Center;
    /// <summary>m_c0, m_d8: the shown and placed cameras.</summary>
    public CameraPose Shown, Placed;
    /// <summary>m_9c, m_a0, m_a4 (FUN_14183d9a0): the final, current and update loose of the shown camera's ease.</summary>
    public float LooseFinal, LooseNow, LooseUpdate;
    /// <summary>m_58 (nearest), m_54 (farthest), m_60 (zoomOutHeightOffset), m_64 (zoomInHeightOffset) for vfunc_27.</summary>
    public float Nearest, Farthest, ZoomOutHeight, ZoomInHeight;
}

/// <summary>
/// CLocalTargetCameraMode (localtargetcameramode.cpp), the camera mode 0x10 a field's local camera runs: it moves from the
/// follow camera to the setup's view of the target over InterpolationTime, holds it there with the wheel's zoom steps, and
/// on an abort (the target walked out of the setup's scope or box, or the local cameras were turned off) moves back and
/// sets the previous mode pending. Positions are in MS2 axes (z up), angles of a rotation in degrees.
/// STILL-OPEN: FUN_140aec8a0(false) when the target is gone (a target-less frame only returns here); the m_290 jump
/// (FUN_140d4fa60, FUN_140aeb920); the base CCameraMode::vfunc_6(0) for a player target (FUN_1416781a0); the near plane
/// check is the local mode's vfunc_38, which returns 0 (localtargetcameramode.h:57), so nothing here pushes the eye out.
/// </summary>
public sealed class LocalTargetCamera
{
    LocalCameraSetup setup;          // m_108
    bool interpolating;              // m_178
    bool nearAuto, boxAuto;          // m_17c, m_180
    bool returning;                  // m_184
    float elapsed;                   // m_188
    float startDistance;             // m_18c
    float startFov;                  // m_190
    Vector3 startRotation;           // m_194
    Vector3 startPosition;           // m_1a0
    Vector3 pcRotation;              // m_1ac
    readonly float prevYaw, prevPitch, prevDistance;   // m_1b8, m_1bc, m_1c0
    readonly LocalCameraSetting prevSetting;           // m_1c4
    readonly int prevType;           // m_1ec
    int zoomStep;                    // m_1f0

    // the CTargetCameraMode part
    float distance, pitch;           // m_50, m_6c
    readonly float nearest, farthest, zoomOutHeight, zoomInHeight;   // m_58, m_54, m_60, m_64
    Vector3 center;                  // m_78
    CameraPose shown, placed;        // m_c0, m_d8
    float looseFinal, looseNow, looseUpdate;   // m_9c, m_a0, m_a4
    bool blending, eased;            // m_fc, m_f0
    float blendTime;                 // m_100
    LocalCameraSetting setting;      // m_setting

    /// <summary>The constructor (@0x140d4dce0, localtargetcameramode.cpp:80-131) over the follow mode, then vfunc_6
    /// (:139-143, m_f0 = 1: the first frame eases from the follow mode's shown camera).</summary>
    public LocalTargetCamera(in LocalCameraPrev prev)
    {
        // CCameraMode::FUN_140afdaa0 (cameramode.cpp:277-292)
        distance = prev.Distance;
        pitch = prev.Pitch;
        nearest = prev.Nearest;
        farthest = prev.Farthest;
        zoomOutHeight = prev.ZoomOutHeight;
        zoomInHeight = prev.ZoomInHeight;
        prevType = prev.Type;
        prevYaw = prev.Yaw;
        prevPitch = prev.Pitch;
        prevDistance = prev.Distance;
        interpolating = true;
        elapsed = 0;
        startFov = prev.Setting.Fov;
        startRotation = prev.Setting.Rotation;
        startPosition = prev.Setting.Position;
        startDistance = (prev.Setting.Position - prev.Center).Length();
        center = prev.Center;
        setting = prevSetting = prev.Setting;
        // the follow mode is a CTargetCameraMode: its shown and placed cameras and its loose
        shown = prev.Shown;
        placed = prev.Placed;
        looseFinal = prev.LooseFinal;
        looseNow = prev.LooseNow;
        looseUpdate = prev.LooseUpdate;
        eased = true;
    }

    /// <summary>m_setting.m_0 (FUN_140ad4b30): the mode's fov, as vfunc_11 sets it.</summary>
    public float Fov => setting.Fov;
    /// <summary>m_c0: the shown camera (vfunc_39 puts the owner's camera there).</summary>
    public CameraPose Shown => shown;
    /// <summary>m_d8: the placed camera.</summary>
    public CameraPose Placed => placed;
    /// <summary>FUN_140af8230 (m_1ec): the mode it started from.</summary>
    public int PreviousType => prevType;
    /// <summary>FUN_140af8210 (m_1c4): the previous mode's setting.</summary>
    public LocalCameraSetting PreviousSetting => prevSetting;
    /// <summary>The setup it runs (m_108).</summary>
    public LocalCameraSetup Setup => setup;
    /// <summary>m_178 is clear: the camera holds the setup's view.</summary>
    public bool Steady => !interpolating;
    /// <summary>m_184: an abort is moving the camera back.</summary>
    public bool Returning => returning;
    /// <summary>m_1f0: the wheel's zoom step.</summary>
    public int ZoomStep => zoomStep;
    /// <summary>The mode vfunc_8 set pending with SetPendingMode (0: none); the owner takes it.</summary>
    public int PendingMode;
    /// <summary>The setting vfunc_8 gave FUN_140aed720 at the end of a return, or null.</summary>
    public LocalCameraSetting? ManagerSetting;

    /// <summary>FUN_140d4f680 (@0x140d4f680, :352-372): the setup starts; a player target's rotation is where the player's
    /// turn starts, the setup's angle keeps the player's x and y, and the shorter way round goes through 360.</summary>
    public void Start(in LocalCameraSetup s, ILocalCameraTarget target)
    {
        if (s.Operate == 1) nearAuto = true;
        else if (s.Operate == 3) boxAuto = true;
        setup = s;
        interpolating = true;
        zoomStep = 0;
        if (target.Position != null && target.IsPc)
        {
            pcRotation = target.Rotation;
            setup.CharacterAngle.X = pcRotation.X;
            setup.CharacterAngle.Y = pcRotation.Y;
            if (pcRotation.Z > 180f && setup.CharacterAngle.Z < 180f) setup.CharacterAngle.Z += 360f;
            else if (pcRotation.Z < 180f && setup.CharacterAngle.Z > 180f) pcRotation.Z += 360f;
        }
    }

    /// <summary>FUN_140d4f830 (@0x140d4f830, :376-397): the move back starts from where the camera is, toward the previous
    /// mode's setting and distance, over at most the time the move in took.</summary>
    public void Abort()
    {
        interpolating = true;
        returning = true;
        setup.InterpolationTime = setup.InterpolationTime > elapsed ? elapsed : setup.InterpolationTime;
        elapsed = 0;
        setup.Setting = prevSetting;
        setup.TargetDistance = startDistance;
        setup.OffsetPosition = Vector3.Zero;
        setup.ChangeCharacterAngle = false;
        setup.Setting.Fov = startFov;
        startDistance = distance;
        startFov = setting.Fov;
        startRotation = setting.Rotation;
        startPosition = setting.Position;
    }

    /// <summary>FUN_141b6fdb0 (util.cpp:767-779) clamped to 0..1 (FUN_141b6fe40, :781-787): 2 log, 3 exp, any other linear.</summary>
    public static float Ease(float v, int kind)
    {
        float t = kind switch
        {
            2 => MathF.Log(v * 10f + 1f) / 2.397895f,
            3 => (MathF.Exp(v * 5f) - 1f) / 147.41316f,
            _ => v,
        };
        return Math.Clamp(t, 0f, 1f);
    }

    /// <summary>CTargetCameraMode::vfunc_27 (@0x14183bc30, targetcameramode.cpp:429-436): the look point's height above the
    /// target for the current distance.</summary>
    float Height()
    {
        float range = farthest - nearest;
        return range > 0 ? zoomInHeight + (zoomOutHeight - zoomInHeight) * ((distance - nearest) / range) : zoomInHeight;
    }

    static Vector3 ViewDirection(Vector3 rot)
    {
        Span<double> m = stackalloc double[9];
        CameraMath.FromEuler(rot.X, rot.Y, rot.Z, m);
        return new Vector3((float)m[0], (float)m[3], (float)m[6]);   // GetCol(0)
    }

    static Vector3 EulerOf(Vector3 center, Vector3 eye)
    {
        var (x, y, z) = CameraMath.Euler(eye.X, eye.Y, eye.Z, center.X, center.Y, center.Z);
        return new Vector3(x, y, z);
    }

    /// <summary>
    /// vfunc_8 (@0x140d4e390, :159-322): one frame of <paramref name="dt"/> seconds; <paramref name="zoom"/> is the frame's
    /// zoom input (CameraMgr::FUN_140a38d00's x: +1 in, -1 out, 0 none). The checks first: the local cameras turned off
    /// abort, and an auto setup aborts once the target leaves its scope or box. Then the look point (the target plus the
    /// height), and the move in, the move back or the steady view, each shown through <see cref="ShowFrame"/>.
    /// </summary>
    public void Frame(float dt, int zoom, ILocalCameraTarget target)
    {
        if (!target.LocalCamerasOn) Abort();
        else if (nearAuto)
        {
            if (target.Position is { } p && !returning && setup.Scope < (p - setup.Setting.Position).Length()) Abort();
        }
        else if (boxAuto)
        {
            if (target.Position is { } p && !returning && !target.BoxContains(setup.BoxId, p)) Abort();
        }

        if (target.Position is not { } pos) return;
        center = pos;
        center.Z += Height();
        var c = center;
        if (interpolating)
        {
            elapsed += dt;
            if (returning)
            {
                pitch = Math.Clamp(pitch, 0.15f, 1.57f);
                // FUN_1409f25a0 is Sin, FUN_1409f2310 Cos (autofindtaxistation.cpp:165-168 over commonutil.cpp:1851-1860)
                var eye = new Vector3(prevDistance * MathF.Sin(prevYaw) + c.X, prevDistance * MathF.Cos(prevYaw) + c.Y,
                                      prevDistance * MathF.Cos(prevPitch) + c.Z);
                setup.Setting.Position = eye;
                setup.Setting.Rotation = EulerOf(c, eye);
                if (elapsed >= setup.InterpolationTime)
                {
                    placed = new CameraPose(setup.Setting.Position, setup.Setting.Rotation);
                    ShowFrame(dt);
                    interpolating = false;
                    ManagerSetting = setup.Setting;
                    PendingMode = prevType is 0 or 2 ? 2 : prevType;
                    setting.Fov = setup.Setting.Fov;
                }
                else
                {
                    float t = Ease(elapsed / setup.InterpolationTime, setup.EndInterpolator);
                    distance = setup.TargetDistance * t + (1f - t) * startDistance;
                    c += setup.OffsetPosition * t;
                    var at = startPosition + t * (setup.Setting.Position - startPosition);
                    var delta = c - at;
                    if (delta.X == delta.Y) at.X += 5f;
                    placed = new CameraPose(at, EulerOf(c, at));
                    ShowFrame(dt);
                    setting.Fov = startFov + t * (setup.Setting.Fov - startFov);
                }
            }
            else if (elapsed >= setup.InterpolationTime)
            {
                var dir = ViewDirection(setup.Setting.Rotation);
                c += setup.OffsetPosition;
                distance = setup.TargetDistance;
                placed = new CameraPose(-dir * setup.TargetDistance + c, setup.Setting.Rotation);
                ShowFrame(dt);
                interpolating = false;
                if (setup.ChangeCharacterAngle) target.Rotation = setup.CharacterAngle;
                setting.Fov = setup.Setting.Fov;
            }
            else
            {
                float t = Ease(elapsed / setup.InterpolationTime, setup.StartInterpolator);
                distance = setup.TargetDistance * t + (1f - t) * startDistance;
                var (rx, ry, rz) = CameraMath.RotationLerp(t, (startRotation.X, startRotation.Y, startRotation.Z),
                    (setup.Setting.Rotation.X, setup.Setting.Rotation.Y, setup.Setting.Rotation.Z));
                var dir = ViewDirection(new Vector3(rx, ry, rz));
                c += setup.OffsetPosition * t;
                var at = -dir * distance + c;
                placed = new CameraPose(at, EulerOf(c, at));
                ShowFrame(dt);
                if (setup.ChangeCharacterAngle) target.Rotation = pcRotation + t * (setup.CharacterAngle - pcRotation);
                setting.Fov = startFov + t * (setup.Setting.Fov - startFov);
            }
        }
        else
        {
            if (zoom != 0)
            {
                zoomStep = (int)(zoomStep - (float)zoom);
                if (zoomStep < setup.ZoomMin) zoomStep = setup.ZoomMin;
                if (zoomStep > setup.ZoomMax) zoomStep = setup.ZoomMax;
            }
            var dir = ViewDirection(setup.Setting.Rotation);
            c += setup.OffsetPosition;
            placed = new CameraPose(-dir * (setup.TargetDistance + zoomStep * setup.ZoomValue) + c, setup.Setting.Rotation);
            if (setting.Fov != setup.Setting.Fov) setting.Fov = setup.Setting.Fov;
            ShowFrame(dt);
        }
    }

    /// <summary>
    /// CTargetCameraMode::vfunc_39 (@0x14183da00, targetcameramode.cpp:800-876): the shown camera eases to the placed one.
    /// It closes t = dt * gap * loose (plus a blend that grows over 3 s once within 10) of the gap; past 1 or with no gap it
    /// takes the placed camera, and the rotation goes by FUN_140afdec0. As the field's follow camera does
    /// (Field.EaseCamera), the per-frame share is the client's at 60 fps, t60 = gap * loose / 60 + blend, taken as the
    /// same share per second at any frame rate: 1 - (1 - t60)^(dt * 60).
    /// </summary>
    void ShowFrame(float dt)
    {
        if (eased)
        {
            float dist = (placed.Position - shown.Position).Length(), blend = 0;
            if (blending)
            {
                if (dist >= 10f) blending = false;
                else
                {
                    blendTime += dt;
                    if (blendTime >= 3f) { blend = 1f; blending = false; }
                    else blend = Ease(blendTime / 3f, 3);
                }
            }
            else if (!NearlyZero(dist) && dist <= 10f) { blending = true; blendTime = 0; }
            if (NearlyZero(dist)) shown = placed;
            else
            {
                float speed = MathF.Abs(looseUpdate) * dt;
                if (looseNow != looseFinal)
                    looseNow = MathF.Abs(looseNow - looseFinal) < speed ? looseFinal : looseNow > looseFinal ? looseNow - speed : looseNow + speed;
                float t60 = dist * looseNow / 60f + blend;
                if (t60 > 1f) shown = placed;
                else
                {
                    float t = dt <= 0 ? 0 : 1f - MathF.Pow(1f - t60, dt * 60f);
                    var rot = shown.Rotation;
                    if (rot != placed.Rotation)
                    {
                        var (x, y, z) = CameraMath.RotationLerp(t, (rot.X, rot.Y, rot.Z), (placed.Rotation.X, placed.Rotation.Y, placed.Rotation.Z));
                        rot = new Vector3(x, y, z);
                    }
                    shown = new CameraPose(shown.Position + t * (placed.Position - shown.Position), rot);
                }
            }
        }
        else
        {
            shown = placed;
            eased = true;
        }
        setting.Position = shown.Position;
        setting.Rotation = shown.Rotation;
    }

    static bool NearlyZero(float f) => MathF.Abs(f) < 1e-5f;
}
