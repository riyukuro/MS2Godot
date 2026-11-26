using Godot;
using Ms2.Audio;

namespace Ms2Godot;

/// <summary>
/// The 3D sounds' listener and distance rolloff. The client's listener is the camera's actor with the camera's facing
/// (CSoundService::OnTick @0x1417b0730, soundservice.cpp:975-999: set3DListenerAttributes at CameraMgr::FUN_140a38d60's
/// position, forward FUN_140af0340, up FUN_140af0250), not the camera itself. The client leaves the 3D math to FMOD
/// (set3DSettings(1, 1, 1), soundservice.cpp:520), so the gain is the rolloff the event's mode asks for
/// (<see cref="DistanceRolloff"/>): its own "(distance)" volume envelope with FMOD_3D_CUSTOMROLLOFF (every complex 3D event),
/// FMOD's linear or log model on its range otherwise. The players with one have Godot's attenuation off and take that gain
/// each frame, set only when it moves (<see cref="RolloffGains"/>). A clip without one keeps Godot's inverse distance from
/// the event's range.
/// </summary>
public partial class FieldAudio
{
    readonly RolloffGains gains3d = new(EffectVoiceSlots.ClientChannels);
    AudioListener3D listener;
    bool listenerOn;

    /// <summary>Where the 3D sounds are heard from this frame (<see cref="HasListener"/> false: nowhere yet).</summary>
    public Vector3 ListenerAt { get; private set; }
    public bool HasListener { get; private set; }

    /// <summary>The listener for this frame: at <see cref="Viewer"/> with the camera's facing; at the camera when there is
    /// no viewer (Godot's own listener then).</summary>
    void UpdateListener()
    {
        var cam = GetViewport()?.GetCamera3D();
        if (Viewer?.Invoke() is { } eye)
        {
            if (listener == null) AddChild(listener = new AudioListener3D { Name = "Listener" });
            var t = new Transform3D(cam != null ? cam.GlobalBasis : Basis.Identity, eye);
            if (!listenerOn || t != lastListener) { listener.GlobalTransform = t; lastListener = t; }
            if (!listenerOn) { listener.MakeCurrent(); listenerOn = true; }
            ListenerAt = eye;
            HasListener = true;
            return;
        }
        if (listenerOn) { listener.ClearCurrent(); listenerOn = false; }
        HasListener = cam != null;
        if (cam != null) ListenerAt = cam.GlobalPosition;
    }
    Transform3D lastListener;

    /// <summary>Each frame: the listener, then the rolloff gain of every 3D voice and ambient player that has a curve.</summary>
    void RollOff()
    {
        UpdateListener();
        if (!HasListener) return;
        var l = ToNumerics(ListenerAt);
        for (int i = 0; i < EffectVoiceSlots.ClientChannels; i++)
            if (gains3d.Update(i, l, out float db)) voices3d[i].VolumeDb = db;
        Ambient?.RollOff(l);
    }

    /// <summary>A voice's attenuation for <paramref name="clip"/> at <paramref name="own"/> gain: its curve's gain from the
    /// listener with Godot's attenuation off and silent past the curve's last non-zero point, or without a curve Godot's
    /// inverse distance from the event's range (3000 / 300 when it has none).</summary>
    void SetAttenuation(AudioStreamPlayer3D v, int slot, SoundClip clip, float own, Vector3 position)
    {
        if (clip.Rolloff is { } c)
        {
            v.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled;
            v.MaxDistance = float.IsInfinity(c.Silent) ? 0 : c.Silent;
            gains3d.Set(slot, c, own, ToNumerics(position));
            v.VolumeDb = HasListener && gains3d.Update(slot, ToNumerics(ListenerAt), out float db) ? db : Mathf.LinearToDb(own);
            return;
        }
        gains3d.Clear(slot);
        v.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance;
        v.VolumeDb = Mathf.LinearToDb(own);
        v.MaxDistance = clip.MaxDistance > 0 ? clip.MaxDistance : 3000;
        v.UnitSize = clip.MinDistance > 0 ? clip.MinDistance : 300;
    }

    /// <summary>The volume a voice was last given by its curve (dB; NaN before the first), for the live check.</summary>
    public float RolloffDbOf(int slot) => gains3d.AppliedDb(slot);

    /// <summary>The gain of the falloff before the curves: Godot's inverse distance (unit size <paramref name="unit"/>, its
    /// 3 dB ceiling) heard from the camera, silent past <paramref name="max"/>.</summary>
    public static float OldFalloff(float d, float unit, float max) =>
        max > 0 && d > max ? 0 : MathF.Min(1.4125f, unit / MathF.Max(d, 1e-3f));

    Vector3 CameraAt => GetViewport()?.GetCamera3D() is { } c ? c.GlobalPosition : ListenerAt;

    void LogRolloff(SoundClip clip, float own, Vector3 position, float db)
    {
        float d = ListenerAt.DistanceTo(position), dc = CameraAt.DistanceTo(position);
        float unit = clip.MinDistance > 0 ? clip.MinDistance : 300, max = clip.MaxDistance > 0 ? clip.MaxDistance : 3000;
        GD.Print($"audio: rolloff 3d {clip.Name} at {d:F0} (camera {dc:F0}): gain {(clip.Rolloff != null ? Mathf.DbToLinear(db) : float.NaN):F3}, "
            + $"old {own * OldFalloff(dc, unit, max):F3}, curve {clip.Rolloff?.ToString() ?? "none"}");
    }

    /// <summary>The listener, the playing 3D voices and the nearest ambient players with their gain now and the old
    /// falloff's, for the live check (the "rolloff" step).</summary>
    public string DescribeRolloff()
    {
        var sb = new System.Text.StringBuilder();
        var l = ToNumerics(ListenerAt);
        var m = Ms2Godot.World.FieldPortals.ToMs2(ListenerAt);
        sb.Append($"listener {(HasListener ? $"[{m.X:F0} {m.Y:F0} {m.Z:F0}]" : "none")} camera at {ListenerAt.DistanceTo(CameraAt):F0}; ");
        for (int i = 0; i < EffectVoiceSlots.ClientChannels; i++)
            if (voices3d[i] is { } v && IsInstanceValid(v) && v.Playing)
                sb.Append($"3d{i} at {ListenerAt.DistanceTo(v.GlobalPosition):F0} {(gains3d.Has(i) ? $"curve {Mathf.DbToLinear(v.VolumeDb):F3}" : $"inverse {Mathf.DbToLinear(v.VolumeDb):F3}")}; ");
        if (Ambient != null) sb.Append(Ambient.DescribeRolloff(l, ToNumerics(CameraAt), 8));
        return sb.ToString();
    }
}
