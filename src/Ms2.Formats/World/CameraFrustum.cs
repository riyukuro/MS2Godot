using System.Globalization;

namespace Ms2.Formats.World;

/// <summary>
/// The near and far planes of a camera entity's view frustum. CameraService (CameraService.cpp:273-350) takes the entity's
/// MinimumNearPlane and MaximumFarToNearRatio when it has them (else NiCamera's 0.1 and 10000, NiCamera.cpp:40-41), and its
/// NearPlane and FarPlane; NiCamera::SetViewFrustum (NiCamera.cpp:61-78) then raises the near plane to far / ratio and
/// then to the minimum. The field camera is the MS2GameplayCamera entity (CameraMgr, camera.cpp:307 and :913 the only
/// SetActiveCamera): cut scene cameras (TriggerObjectCamera::vfunc_3, triggeractioncli.cpp:858-873) move it and set its
/// FOV, DOFNear and DOFFar, never its planes, so the camera modes' 250 / 18000 are the depth of field, not the frustum.
/// </summary>
public readonly record struct CameraFrustum(float Near, float Far)
{
    public const string GameplayCamera = "MS2GameplayCamera";
    public const float DefaultMinNear = 0.1f, DefaultMaxFarNearRatio = 1e4f;

    /// <summary>MS2GameplayCamera.flat's planes, for data without it.</summary>
    public static readonly CameraFrustum GameplayDefault = new(10, 100000);

    /// <summary>NiCamera::SetViewFrustum's near plane: at least far / ratio, then at least the minimum.</summary>
    public static CameraFrustum Clamp(float near, float far, float minNear, float maxFarNearRatio)
    {
        float n = near;
        float fromRatio = far / maxFarNearRatio;
        if (n < fromRatio) n = fromRatio;
        if (n < minNear) n = minNear;
        return new CameraFrustum(n, far);
    }

    /// <summary>The flat's frustum, or null when it lacks NearPlane or FarPlane (CameraService then leaves the frustum).</summary>
    public static CameraFrustum? Of(FlatModel flat)
    {
        if (flat == null || !Float(flat, "NearPlane", out float near) || !Float(flat, "FarPlane", out float far)) return null;
        float min = Float(flat, "MinimumNearPlane", out float m) ? m : DefaultMinNear;
        float ratio = Float(flat, "MaximumFarToNearRatio", out float r) ? r : DefaultMaxFarNearRatio;
        return Clamp(near, far, min, ratio);
    }

    /// <summary>The field camera's frustum from the data's MS2GameplayCamera flat, else <see cref="GameplayDefault"/>.</summary>
    public static CameraFrustum Gameplay(FlatLibrary flats) => Of(flats?.Get(GameplayCamera)) ?? GameplayDefault;

    static bool Float(FlatModel flat, string name, out float v)
    {
        v = 0;
        return flat.Props.TryGetValue(name, out var s) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }
}
