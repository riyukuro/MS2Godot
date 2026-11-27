using Godot;
using Ms2.Audio;

namespace Ms2Godot;

/// <summary>
/// The 3D sounds' listener and distance rolloff. The client's listener is the camera's actor with the camera's facing
/// (CSoundService::OnTick @0x1417b0730, soundservice.cpp:975-999: set3DListenerAttributes at CameraMgr::FUN_140a38d60's
/// position, forward FUN_140af0340, up FUN_140af0250), not the camera itself. The client leaves the 3D math to FMOD
/// (set3DSettings(1, 1, 1), soundservice.cpp:520), so the gain is the rolloff the event's mode asks for
/// (<see cref="DistanceRolloff"/>): its own "(distance)" volume envelope with FMOD_3D_CUSTOMROLLOFF (every complex 3D event),
/// FMOD's linear, linear square or inverse model on its range otherwise. The players with one have Godot's attenuation off,
/// a max distance of 0 and no attenuation filter, since Godot 4.7 fades by 1 - d / max distance and shelves the highs with any
/// model (<see cref="Player3DOutput"/>), and take that gain each frame, set only when it moves (<see cref="RolloffGains"/>). A
/// looping one is stopped while the listener is past its curve's silent distance and played again as it comes back, at the
/// time FMOD's event has reached by then and at its full level, as the FMOD probe renders a virtual voice coming back
/// (<see cref="EventFadeIn.LoopPosition"/>); a one-shot there plays on at the silent volume, keeping its time the same way.
/// Godot pans by the source's azimuth in the listener's own plane, leaving out its part along the listener's up, where FMOD
/// pans by the direction's side component alone, so the listener here is level with the camera's right
/// (<see cref="Player3DOutput.PanBasis"/>). A clip without one keeps Godot's inverse distance from the event's range.
/// </summary>
public partial class FieldAudio
{
    readonly RolloffGains gains3d = new(EffectVoiceSlots.ClientChannels);
    /// <summary>Whether the voice's sound has started (its start delay is over), so a loop stopped past its silent distance
    /// plays again on the way back.</summary>
    readonly bool[] started3d = new bool[EffectVoiceSlots.ClientChannels];
    /// <summary>A voice's loop range in frames and its rate, and when its sound started (<see cref="loopClock"/>): a loop
    /// stopped past its silent distance plays again from where FMOD's event would be by then, with no new fade-in
    /// (<see cref="EventFadeIn.LoopPosition"/>).</summary>
    readonly int[] loopBegin3d = new int[EffectVoiceSlots.ClientChannels], loopEnd3d = new int[EffectVoiceSlots.ClientChannels],
        rate3d = new int[EffectVoiceSlots.ClientChannels];
    readonly double[] startedAt3d = new double[EffectVoiceSlots.ClientChannels];
    AudioListener3D listener;
    bool listenerOn;

    /// <summary>Where the 3D sounds are heard from this frame (<see cref="HasListener"/> false: nowhere yet).</summary>
    public Vector3 ListenerAt { get; private set; }
    public bool HasListener { get; private set; }
    /// <summary>The listener's unit right this frame (the camera's; zero with no camera), for the side a stereo clip is on.</summary>
    public Vector3 ListenerRight { get; private set; }

    /// <summary>The listener for this frame: at <see cref="Viewer"/> with the camera's facing; at the camera when there is
    /// no viewer (Godot's own listener then).</summary>
    void UpdateListener()
    {
        var cam = GetViewport()?.GetCamera3D();
        if (Viewer?.Invoke() is { } eye)
        {
            if (listener == null) AddChild(listener = new AudioListener3D { Name = "Listener" });
            var t = new Transform3D(cam != null ? LevelBasis(cam.GlobalBasis) : Basis.Identity, eye);
            if (!listenerOn || t != lastListener) { listener.GlobalTransform = t; lastListener = t; }
            if (!listenerOn) { listener.MakeCurrent(); listenerOn = true; }
            ListenerAt = eye;
            ListenerRight = cam != null ? cam.GlobalBasis.X.Normalized() : Vector3.Right;
            HasListener = true;
            return;
        }
        if (listenerOn) { listener.ClearCurrent(); listenerOn = false; }
        HasListener = cam != null;
        if (cam != null) { ListenerAt = cam.GlobalPosition; ListenerRight = cam.GlobalBasis.X.Normalized(); }
    }
    Transform3D lastListener;

    /// <summary>The camera's basis made level for Godot's pan (<see cref="Player3DOutput.PanBasis"/>).</summary>
    static Basis LevelBasis(Basis cam) =>
        Player3DOutput.PanBasis(ToNumerics(cam.X), out var r, out var u, out var b)
            ? new Basis(new Vector3(r.X, r.Y, r.Z), new Vector3(u.X, u.Y, u.Z), new Vector3(b.X, b.Y, b.Z)) : cam;

    /// <summary>Each frame: the listener, then the rolloff gain of every 3D voice and ambient player that has a curve.</summary>
    void RollOff()
    {
        UpdateListener();
        if (!HasListener) return;
        var l = ToNumerics(ListenerAt);
        var r = ToNumerics(ListenerRight);
        for (int i = 0; i < EffectVoiceSlots.ClientChannels; i++)
        {
            if (gains3d.Update(i, l, r, out float db)) voices3d[i].VolumeDb = db;
            if (gains3d.PanChanged(i, out float pan)) voices3d[i].PanningStrength = pan;
            if (gains3d.PastSilentChanged(i, out bool past))
            {
                if (past) voices3d[i].Stop();
                else if (started3d[i]) voices3d[i].Play((float)EventFadeIn.LoopPosition(loopClock - startedAt3d[i], loopBegin3d[i], loopEnd3d[i], rate3d[i]));
            }
        }
        Ambient?.RollOff(l, r);
    }

    /// <summary>A voice's attenuation for <paramref name="clip"/> at <paramref name="own"/> gain: its curve's gain from the
    /// listener with Godot's attenuation, max distance fade and filter off, or without a curve Godot's inverse distance and
    /// filter from the event's range (3000 / 300 when it has none). The curve also gives the panning strength (its pan level).
    /// The voice is reused, so each branch sets all of them.</summary>
    void SetAttenuation(AudioStreamPlayer3D v, int slot, SoundClip clip, float own, Vector3 position, StereoChannels stereo)
    {
        started3d[slot] = false;
        if (v.Stream is AudioStreamWav w && w.LoopMode == AudioStreamWav.LoopModeEnum.Forward)
            (loopBegin3d[slot], loopEnd3d[slot], rate3d[slot]) = (w.LoopBegin, w.LoopEnd, w.MixRate);
        else (loopBegin3d[slot], loopEnd3d[slot], rate3d[slot]) = (0, 0, 0);
        if (clip.Rolloff is { } c)
        {
            v.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled;
            v.MaxDistance = Player3DOutput.CurveMaxDistance(c);
            v.AttenuationFilterDb = Player3DOutput.CurveFilterDb;
            gains3d.Set(slot, c, own, ToNumerics(position), stopsPastSilent: clip.Loops, stereoCorrelation: stereo.Correlation,
                leftShare: stereo.LeftShare);
            v.VolumeDb = HasListener && gains3d.Update(slot, ToNumerics(ListenerAt), ToNumerics(ListenerRight), out float db) ? db : Mathf.LinearToDb(own);
            if (gains3d.PanChanged(slot, out float pan)) v.PanningStrength = pan;   // FMOD's pan level (Player3DOutput)
            return;
        }
        gains3d.Clear(slot);
        v.AttenuationFilterDb = Player3DOutput.DefaultFilterDb;
        v.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance;
        v.PanningStrength = 1;
        v.VolumeDb = Mathf.LinearToDb(own);
        v.MaxDistance = clip.MaxDistance > 0 ? clip.MaxDistance : 3000;
        v.UnitSize = clip.MinDistance > 0 ? clip.MinDistance : 300;
    }

    /// <summary>The voice's sound starts: played unless it is a loop past its curve's silent distance, which plays when the
    /// listener comes back.</summary>
    void Start3d(AudioStreamPlayer3D v, int slot)
    {
        started3d[slot] = true;
        startedAt3d[slot] = loopClock;
        if (!gains3d.IsPast(slot)) v.Play();
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
            + $"out {(clip.Rolloff != null ? Player3DOutput.Gain(db, Player3DOutput.CurveMaxDistance(clip.Rolloff), d) : float.NaN):F3}, "
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
            if (voices3d[i] is { } v && IsInstanceValid(v) && (v.Playing || gains3d.Has(i) && gains3d.IsPast(i)))
                sb.Append($"3d{i} at {ListenerAt.DistanceTo(v.GlobalPosition):F0} {(gains3d.Has(i) ? $"curve {Mathf.DbToLinear(v.VolumeDb):F3} out {(v.Playing ? Output(v) : 0):F3}{(gains3d.IsPast(i) ? " stopped past silent" : "")}" : $"inverse {Mathf.DbToLinear(v.VolumeDb):F3}")}; ");
        if (Ambient != null) sb.Append(Ambient.DescribeRolloff(l, ToNumerics(CameraAt), 8));
        return sb.ToString();
    }

    /// <summary>The gain a playing curve player puts out now under its cutoff, from its own volume and max distance (the live
    /// check); NaN with a high shelf (a filter dB not 0, which a curve player should not have).</summary>
    public float Output(AudioStreamPlayer3D v) => v.AttenuationFilterDb != 0 && v.AttenuationModel == AudioStreamPlayer3D.AttenuationModelEnum.Disabled ? float.NaN
        : Player3DOutput.Gain(v.VolumeDb, v.MaxDistance, ListenerAt.DistanceTo(v.GlobalPosition), v.MaxDb);
}
