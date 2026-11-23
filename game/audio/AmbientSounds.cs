using Godot;
using Ms2.Audio;
using Ms2.Formats.World;

namespace Ms2Godot;

/// <summary>
/// The field's sound entities (flat mixin MS2Sound: soundEventID "MS2Sound/Sound/AMB/11100003", SoundRadius): one looped
/// positional player each, at the entity's position. The attenuation range is the event's 3D min / max distance (inverse-distance
/// roll-off from the min distance, silent beyond the max), the volume the event's.
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
    public readonly List<string> Log = new();

    public AmbientSounds(SoundLibrary library) { this.library = library; Name = "AmbientSounds"; }

    sealed class Sound
    {
        public AudioStreamPlayer3D Player;
        public bool On = true;    // CSoundEvent m_nStopped clear: the manager's update starts it
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
        if (!on) s.Player.Stop();
        else if (s.Player.Stream != null && !s.Player.Playing) s.Player.Play();
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
            Name = e.Name, Position = pos, VolumeDb = Mathf.LinearToDb(clip.Volume),
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance, UnitSize = min, MaxDistance = max,
        };
        AddChild(player);
        var sound = sounds[e] = new Sound { Player = player };
        Count++;
        Log.Add($"{e.Name} at {pos} -> {id} = {clip.Name} vol {clip.Volume:F2} range {min}..{max}");
        var key = clip.Name;
        if (!decoded.TryGetValue(key, out var task)) decoded[key] = task = Task.Run(() => SafeDecode.Decode(clip));
        task.ContinueWith(t => { if (t.IsFaulted || t.Result == null) return; var pcm = t.Result; var data = pcm.ToBytes(); started.Enqueue(() => Start(sound, pcm, data)); });
    }

    /// <summary>The clips decoded on the pool threads, started on the main thread (<see cref="_Process"/>): a pool thread makes
    /// no engine call, since one finishing during the quit reached the engine's teardown (see FieldAudio).</summary>
    readonly System.Collections.Concurrent.ConcurrentQueue<Action> started = new();

    public override void _Process(double delta)
    {
        while (started.TryDequeue(out var start)) start();
    }

    static void Start(Sound s, Pcm16 pcm, byte[] data)
    {
        var p = s.Player;
        if (!GodotObject.IsInstanceValid(p) || !p.IsInsideTree()) return;   // the field changed or quit while the clip decoded
        p.Stream = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits, Stereo = pcm.Channels == 2, MixRate = pcm.Rate, Data = data,
            LoopMode = AudioStreamWav.LoopModeEnum.Forward, LoopBegin = 0, LoopEnd = pcm.Frames,
        };
        if (s.On) p.Play();
    }
}
