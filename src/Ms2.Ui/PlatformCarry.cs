using Ms2.Formats.Model;

namespace Ms2.Ui;

/// <summary>
/// PCBase::vfunc_68's ride (pcbase.cpp:1797-1817), which every PC runs, the remote ones too: when the hit report's entity is a
/// field marker's (WorldService::FindMarkerByEntity) and the same one as last frame, its Position property's change since
/// then carries the body. A change that is not upward joins the frame's move; an upward one puts the body there at once
/// (vfunc_19, no sweep). Any change sets bPushed. A different entity, or none, starts over (m_unk_ae0 / m_posAE8).
/// </summary>
public sealed class PlatformCarry
{
    string id;      // PCBase m_unk_ae0: the marker the last frame stood by
    Vec3 last;      // PCBase m_posAE8: that marker's position then (field units)

    /// <summary>One frame: <paramref name="change"/> is the marker's move since the last frame (field units, zero when none);
    /// true when it moved (bPushed).</summary>
    public bool Step(BreakableObjects markers, string hitMarker, out Vec3 change)
    {
        var marker = hitMarker != null ? markers?.Find(hitMarker) : null;
        change = default;
        bool pushed = false;
        if (marker != null && hitMarker == id)
        {
            change = marker.Position - last;
            pushed = change.X != 0 || change.Y != 0 || change.Z != 0;
        }
        if (marker != null) last = marker.Position;
        id = marker != null ? hitMarker : null;
        return pushed;
    }

    /// <summary>:1806: an upward change (z &gt; 0) is set at once; any other joins the controller's move.</summary>
    public static bool SetsAtOnce(Vec3 change) => change.Z > 0;
}
