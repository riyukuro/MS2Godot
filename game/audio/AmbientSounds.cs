using Godot;
using Ms2.Audio;
using Ms2.Formats.World;

namespace Ms2Godot;

/// <summary>
/// The field's sound entities (flat mixin MS2Sound: soundEventID "MS2Sound/Sound/AMB/11100003", SoundRadius): one looped
/// positional player each, at the entity's position. The attenuation range is the event's 3D min / max distance (inverse-distance
/// roll-off from the min distance, silent beyond the max), the volume the event's.
/// </summary>
public partial class AmbientSounds : Node3D
{
    readonly SoundLibrary library;
    readonly Dictionary<string, Task<Pcm16>> decoded = new();
    public int Count { get; private set; }
    public readonly List<string> Log = new();

    public AmbientSounds(SoundLibrary library) { this.library = library; Name = "AmbientSounds"; }

    public void Build(FieldMap map)
    {
        foreach (var e in map.Entities)
        {
            if (!e.HasMixin("MS2Sound")) continue;
            var id = e.Get("soundEventID");
            if (string.IsNullOrEmpty(id)) continue;
            var clip = library.ResolveEvent(id);
            if (clip == null) { Log.Add($"{e.Name}: event {id} not found"); continue; }
            var pos = FieldBuilder.ToGodotPoint(e.Position);
            float max = clip.MaxDistance > 0 ? clip.MaxDistance : (float)e.Number("SoundRadius", 1500);
            float min = clip.MinDistance > 0 ? clip.MinDistance : 60;
            var player = new AudioStreamPlayer3D
            {
                Name = e.Name, Position = pos, VolumeDb = Mathf.LinearToDb(clip.Volume),
                AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance, UnitSize = min, MaxDistance = max,
            };
            AddChild(player);
            Count++;
            Log.Add($"{e.Name} at {pos} -> {id} = {clip.Name} vol {clip.Volume:F2} range {min}..{max}");
            var key = clip.Name;
            if (!decoded.TryGetValue(key, out var task)) decoded[key] = task = Task.Run(() => SafeDecode.Decode(clip));
            task.ContinueWith(t => { if (t.IsFaulted || t.Result == null) return; var pcm = t.Result; Callable.From(() => Start(player, pcm)).CallDeferred(); });
        }
    }

    static void Start(AudioStreamPlayer3D p, Pcm16 pcm)
    {
        if (!GodotObject.IsInstanceValid(p)) return;
        p.Stream = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits, Stereo = pcm.Channels == 2, MixRate = pcm.Rate, Data = pcm.ToBytes(),
            LoopMode = AudioStreamWav.LoopModeEnum.Forward, LoopBegin = 0, LoopEnd = pcm.Frames,
        };
        p.Play();
    }
}
