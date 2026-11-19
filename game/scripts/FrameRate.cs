using Godot;

namespace Ms2Godot;

/// <summary>
/// Frame-rate independence for the per-frame forms the client (or this port) tuned at 60 frames a second: a step that
/// closes a fixed share of a gap each frame. Each gives the 60 fps value exactly at a
/// 1/60 s frame and the same result per second at any other frame time.
/// </summary>
public static class FrameRate
{
    /// <summary>The reference rate the per-frame forms are kept at.</summary>
    public const float Reference = 60f;

    /// <summary>The frame time in 60 fps frames (1 at 1/60 s).</summary>
    public static float Frames(float dt) => dt * Reference;

    /// <summary>The share of a gap to close this frame for <paramref name="at60"/> per 60 fps frame: 1 - (1 - at60)^(dt*60).</summary>
    public static float Ease(float at60, float dt)
    {
        if (at60 >= 1f) return 1f;
        if (at60 <= 0f || dt <= 0f) return 0f;
        return 1f - Mathf.Pow(1f - at60, Frames(dt));
    }
}
