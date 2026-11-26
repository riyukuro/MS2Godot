using Godot;
using Ms2.Audio;
using Ms2.Formats.World;

namespace Ms2Godot;

/// <summary>
/// The field's sound entities (flat mixin MS2Sound: soundEventID "MS2Sound/Sound/AMB/11100003", SoundRadius): one looped
/// positional player each, at the entity's position, at the event's volume. Its gain by distance is the event's own
/// "(distance)" curve from the listener (<see cref="DistanceRolloff"/>, see FieldAudio.Rolloff.cs), set by <see cref="RollOff"/>;
/// a 2D event (Tria's town ambience AMB/11100015) is not attenuated or panned; a 3D event without a curve keeps Godot's
/// inverse distance from 60 (or its min) to its max (or SoundRadius).
/// The field load creates them for every MS2Sound entity with a sound event except the trigger objects (WorldService's entity
/// add, worldservice_1.cpp:10336-10350, skips an entity whose model contains MS2TriggerObject): a trigger sound of kind AMB
/// starts only on its Trigger packet event (<see cref="SetTriggerSound"/>).
/// STILL-OPEN: the same load skips an entity with timeShowEnabled set (the day / night ambience, MS2TimeAmbSetting), which is not
/// ported; those still play from the load here.
/// </summary>
public partial class AmbientSounds : Node3D
{
    readonly SoundLibrary library;
    readonly Dictionary<string, Task<Pcm16>> decoded = new();
    public int Count { get; private set; }

    /// <summary>The ambient players of every field now in the tree (made, not yet freed with their field).</summary>
    public static int Live { get; private set; }
    public readonly List<string> Log = new();

    public AmbientSounds(SoundLibrary library) { this.library = library; Name = "AmbientSounds"; }

    sealed class Sound
    {
        public AudioStreamPlayer3D Player;
        public bool On = true;    // CSoundEvent m_nStopped clear: the manager's update starts it
        public int Index;         // in gains
        public SoundClip Clip;
        public System.Numerics.Vector3 At;
        public float OldMin, OldMax;   // the inverse-distance range a clip without a curve plays with
    }

    readonly List<Sound> rolled = new();
    readonly RolloffGains gains = new(16);

    /// <summary>The curve gain of every playing ambient player with one, for a listener at <paramref name="listener"/>;
    /// the volume is set only when it moves.</summary>
    public void RollOff(System.Numerics.Vector3 listener)
    {
        for (int i = 0; i < rolled.Count; i++)
        {
            var s = rolled[i];
            if (gains.Update(s.Index, listener, out float db)) s.Player.VolumeDb = db;
            if (gains.PastSilentChanged(s.Index, out bool past))
            {
                // a loop: stopped while silent, played again on the way back
                if (past) s.Player.Stop();
                else if (s.On && s.Player.Stream != null) s.Player.Play();
            }
        }
    }

    /// <summary>The nearest <paramref name="n"/> ambient players by the listener, with the gain now and the gain of the
    /// old falloff (Godot's inverse distance from 60 or the min, measured from the camera), for the live check.</summary>
    public string DescribeRolloff(System.Numerics.Vector3 listener, System.Numerics.Vector3 camera, int n)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var s in sounds.Values.OrderBy(s => System.Numerics.Vector3.Distance(listener, s.At)).Take(n))
        {
            float d = System.Numerics.Vector3.Distance(listener, s.At), dc = System.Numerics.Vector3.Distance(camera, s.At);
            float now = s.Clip.Rolloff != null ? s.Clip.Volume * s.Clip.Rolloff.GainAt(d) : s.Clip.Is2D ? s.Clip.Volume : float.NaN;
            var m = Ms2Godot.World.FieldPortals.ToMs2(s.Player.GlobalPosition);
            var ev = s.Clip.Event;
            sb.Append($"{s.Player.Name} {ev?.FullPath ?? s.Clip.Name} {(ev == null ? "?" : ev.Is3D ? "3d" : "2d")}{(ev?.IsSimple == true ? " simple" : "")} {s.Clip.Name} [{m.X:F0} {m.Y:F0} {m.Z:F0}] at {d:F0} (camera {dc:F0}): gain {now:F3} applied {Mathf.DbToLinear(s.Player.VolumeDb):F3}{(s.Clip.Rolloff != null && GetParent() is FieldAudio fa ? $" out {fa.Output(s.Player):F3}" : "")}{(s.Clip.Rolloff != null && s.On && gains.IsPast(s.Index) ? " stopped past silent" : "")}, "
                + $"old {s.Clip.Volume * FieldAudio.OldFalloff(dc, s.OldMin, s.OldMax):F3}; ");
        }
        return sb.ToString();
    }

    readonly Dictionary<FieldEntity, Sound> sounds = new();

    public void Build(FieldMap map)
    {
        foreach (var e in map.Entities)
        {
            if (!e.HasMixin("MS2Sound") || e.HasMixin("MS2TriggerObject")) continue;
            Create(e);
        }
    }

    /// <summary>
    /// A trigger sound's AMB event (CSoundEventMgr::FUN_14177ce30 @0x14177ce30, soundeventmgr.cpp:1412-1460, from
    /// TriggerObjectSound::vfunc_2, triggeractioncli.cpp:583-589): an entity without a sound yet gets one when on (created,
    /// placed and left for the manager's update to start, StopOrReset(1)); one it has is stopped when off (StopOrReset(0))
    /// and set to start again when on. An entity's event name does not change here, so the branch that replaces a sound
    /// whose name differs (:1443-1451) is not reached.
    /// </summary>
    public void SetTriggerSound(FieldEntity e, bool on)
    {
        if (!sounds.TryGetValue(e, out var s))
        {
            if (on) Create(e);
            return;
        }
        s.On = on;
        if (s.Clip.Rolloff is { } curve)
        {
            if (on) { gains.Set(s.Index, curve, s.Clip.Volume, s.At, stopsPastSilent: true); Place(s); }
            else gains.Clear(s.Index);
        }
        if (!on) s.Player.Stop();
        else if (s.Player.Stream != null && !s.Player.Playing && !Past(s)) s.Player.Play();
    }

    /// <summary>Whether the entity has a sound and it is set to play (null: no sound).</summary>
    public bool? SoundOn(FieldEntity e) => sounds.TryGetValue(e, out var s) ? s.On : null;

    /// <summary>The entity's player, or null.</summary>
    public AudioStreamPlayer3D PlayerOf(FieldEntity e) => sounds.TryGetValue(e, out var s) ? s.Player : null;

    void Create(FieldEntity e)
    {
        var id = e.Get("soundEventID");
        if (string.IsNullOrEmpty(id)) return;
        var clip = library.ResolveEvent(id);
        if (clip == null) { Log.Add($"{e.Name}: event {id} not found"); return; }
        var pos = FieldBuilder.ToGodotPoint(e.Position);
        float max = clip.MaxDistance > 0 ? clip.MaxDistance : (float)e.Number("SoundRadius", 1500);
        float min = clip.MinDistance > 0 ? clip.MinDistance : 60;
        var player = new AudioStreamPlayer3D
        {
            Name = e.Name, Position = pos, VolumeDb = Mathf.LinearToDb(clip.Volume), Bus = SoundBuses.Of(clip.Category),
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance, UnitSize = min, MaxDistance = max,
        };
        var sound = sounds[e] = new Sound { Player = player, Clip = clip, At = new(pos.X, pos.Y, pos.Z), OldMin = min, OldMax = max };
        if (clip.Rolloff is { } curve)
        {
            // Godot's attenuation, max distance fade and filter off: the curve is the gain, and the loop is stopped past its
            // silent distance (FieldAudio.Rolloff.cs)
            player.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled;
            player.MaxDistance = Player3DOutput.CurveMaxDistance(curve);
            player.AttenuationFilterDb = Player3DOutput.CurveFilterDb;
            sound.Index = rolled.Count;
            gains.Set(sound.Index, curve, clip.Volume, sound.At, stopsPastSilent: true);
            rolled.Add(sound);
            Place(sound);
        }
        else if (clip.Is2D)
        {
            // a 2D event: FMOD neither attenuates nor pans it, and the client starts every environment sound with no range
            // check (CSoundEventMgr::StartEnvironmentSounds @0x14177d230, soundeventmgr.cpp:1465-1481), so it is heard
            // across the field at its own volume
            player.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled;
            player.MaxDistance = 0;
            player.AttenuationFilterDb = 0;   // Godot shelves the highs by the volume too (Player3DOutput)
            player.PanningStrength = 0;
        }
        AddChild(player);
        Count++;
        Live++;
        Log.Add($"{e.Name} at {pos} -> {id} = {clip.Name} vol {clip.Volume:F2} range {min}..{max}");
        var key = clip.Name;
        if (!decoded.TryGetValue(key, out var task)) decoded[key] = task = Task.Run(() =>
        {
            var pcm = SafeDecode.Decode(clip, out var failure);
            if (failure != null) EngineCalls.Warn(failure);
            return pcm;
        });
        task.ContinueWith(t => { if (t.IsFaulted || t.Result == null) return; var pcm = t.Result; var data = pcm.ToBytes(); started.Enqueue(() => Start(sound, pcm, data)); });
    }

    /// <summary>The clips decoded on the pool threads, started on the main thread (<see cref="_Process"/>): a pool thread makes
    /// no engine call, since one finishing during the quit reached the engine's teardown (see FieldAudio).</summary>
    readonly System.Collections.Concurrent.ConcurrentQueue<Action> started = new();

    /// <summary>The players free with the field (the client's map leave removes the point sounds, CEffectService::vfunc_3
    /// @0x1411a8210, FUN_1411a6490 effectservice.cpp:1595-1621).</summary>
    public override void _ExitTree()
    {
        Live -= Count;
        Count = 0;
    }

    public override void _Process(double delta)
    {
        while (started.TryDequeue(out var start)) start();
    }

    /// <summary>A curve sound's volume for the listener now, and which side of its silent distance it is on.</summary>
    void Place(Sound s)
    {
        if (GetParent() is FieldAudio fa && fa.HasListener && gains.Update(s.Index, new(fa.ListenerAt.X, fa.ListenerAt.Y, fa.ListenerAt.Z), out float db))
            s.Player.VolumeDb = db;
    }

    /// <summary>A curve sound past its silent distance: not played until the listener comes back.</summary>
    bool Past(Sound s) => s.Clip.Rolloff != null && gains.IsPast(s.Index);

    void Start(Sound s, Pcm16 pcm, byte[] data)
    {
        var p = s.Player;
        if (!GodotObject.IsInstanceValid(p) || !p.IsInsideTree()) return;   // the field changed or quit while the clip decoded
        p.Stream = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits, Stereo = pcm.Channels == 2, MixRate = pcm.Rate, Data = data,
            LoopMode = AudioStreamWav.LoopModeEnum.Forward, LoopBegin = 0, LoopEnd = pcm.Frames,
        };
        if (s.On && !Past(s)) p.Play();
    }
}
