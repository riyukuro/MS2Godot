using Godot;
using Ms2.Audio;

namespace Ms2Godot;

/// <summary>
/// Footsteps after CGroundEffectDataManager::PlayEffects: the ground material of what the character touched (<see cref="Ground"/>)
/// picks groundeffect/&lt;ground&gt;.xml, the step's action (walk, run0, run1, land) the sound events. The client fires a step when the
/// animation's text key "run*" / "walk*" is reached (PCBase::TextKeyEvent); the player's KF files in this client data carry
/// only start/end keys, so steps follow a fixed cadence of two per animation cycle (run0 then run1, the two feet) unless
/// <see cref="TextKeys"/> supplies keys for an animation.
/// </summary>
public partial class FootstepSounds : Node
{
    SoundLibrary library;
    /// <summary>The sound bank; null plays the steps' effect files only.</summary>
    public SoundLibrary Library { get => library; set => library = value; }
    readonly GroundEffectTable table;
    readonly CharacterBody3D body;
    readonly AnimationPlayer anim;
    readonly Dictionary<string, AudioStreamWav> decoded = new();
    readonly AudioStreamPlayer3D[] voices = new AudioStreamPlayer3D[4];
    int nextVoice;
    double lastPos = -1;
    string lastAnim = "";
    int stepCount;
    readonly Random rng = new();

    /// <summary>Optional text keys (time in seconds, key text) per animation name; when present they replace the cadence.</summary>
    public readonly Dictionary<string, List<(double time, string key)>> TextKeys = new();
    /// <summary>Whether the character stands on the ground (defaults to the body's floor contact).</summary>
    public Func<bool> OnFloor;
    public string LastGround { get; private set; } = "default";
    public string LastAction { get; private set; } = "";
    public int Steps => stepCount;
    /// <summary>Called with every effect file a step picks, for its visual parts (the sounds play here).</summary>
    public Action<string> EffectFile;

    /// <summary>
    /// The ground material for a step or a landing: PCBase::FUN_141671480 (pcbase.cpp:2488), the material of the entity
    /// in the controller's hit report (the player's <c>GroundMaterial</c>). The client reads no position grid: a cell
    /// lookup by position picked the neighbouring or a lower cube near an edge (water under the side of a stone cube).
    /// "" plays nothing (PlayEffects returns on an empty ground, groundeffectdata.cpp:275); null is "default".
    /// </summary>
    public Func<string> Ground;

    public FootstepSounds(SoundLibrary library, GroundEffectTable table, CharacterBody3D body, AnimationPlayer anim)
    {
        this.library = library; this.table = table; this.body = body; this.anim = anim;
        Name = "FootstepSounds";
        for (int i = 0; i < voices.Length; i++) { voices[i] = new AudioStreamPlayer3D { MaxDistance = 3000, UnitSize = 300 }; body.AddChild(voices[i]); }
    }

    public override void _Process(double delta)
    {
        // CurrentAnimation makes a tracked StringName wrapper per read: dispose it
        string name;
        using (var current = anim.CurrentAnimation) name = current.ToString();
        var speed = new Vector2(body.Velocity.X, body.Velocity.Z).Length();
        bool moving = speed > 10 && !name.StartsWith("swim") && (name.Contains("run") || name.Contains("walk")) && (OnFloor?.Invoke() ?? body.IsOnFloor());
        if (!moving) { lastPos = -1; lastAnim = name; return; }
        double len = anim.CurrentAnimationLength;
        double pos = anim.CurrentAnimationPosition;
        if (name != lastAnim || lastPos < 0) { lastPos = pos; lastAnim = name; return; }
        if (TextKeys.TryGetValue(name, out var keys))
        {
            foreach (var (t, k) in keys)
                if (k.StartsWith("run") || k.StartsWith("walk"))
                    if (Crossed(lastPos, pos, t, len)) Step(k);
        }
        else if (len > 0)
        {
            // two steps per cycle: at the start (foot 0) and half way (foot 1)
            string a0 = name.Contains("walk") ? "walk" : "run0", a1 = name.Contains("walk") ? "walk" : "run1";
            if (Crossed(lastPos, pos, 0, len)) Step(a0);
            if (Crossed(lastPos, pos, len / 2, len)) Step(a1);
        }
        lastPos = pos;
    }

    static bool Crossed(double from, double to, double t, double len) => to >= from ? from < t && t <= to : (t > from || t <= to);

    /// <summary>Play the sounds of one action ("walk", "run0", "run1", "land") on the ground under the character.</summary>
    public void Step(string action)
    {
        LastGround = Ground == null ? "default" : Ground();
        LastAction = action;
        stepCount++;
        if (string.IsNullOrEmpty(LastGround)) return;
        var files = table.PickFiles(LastGround, action, rng);
        foreach (var f in files) EffectFile?.Invoke(f);
        if (library == null) return;
        foreach (var ev in files.SelectMany(table.SoundsOf))
        {
            var clip = library.ResolveEvent(ev, rng);
            if (clip == null) continue;
            if (!decoded.TryGetValue(clip.Name, out var wav))
            {
                var pcm = SafeDecode.Decode(clip);
                decoded[clip.Name] = wav = pcm == null ? null : new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, Stereo = pcm.Channels == 2, MixRate = pcm.Rate, Data = pcm.ToBytes() };
            }
            if (wav == null) continue;
            var curve = clip.Rolloff;
            var fa = GetParent() as FieldAudio;
            float d = curve != null && fa is { HasListener: true } ? fa.ListenerAt.DistanceTo(body.GlobalPosition) : 0;
            if (curve != null && fa is { HasListener: true } && d >= curve.Silent) continue;   // past its silent distance: nothing to hear
            var v = voices[nextVoice++ % voices.Length];
            v.Stream = wav;
            v.VolumeDb = Mathf.LinearToDb(clip.Volume);
            if (curve != null && fa is { HasListener: true })
            {
                // the step's curve gain from the listener (FieldAudio.Rolloff.cs), at its start: a step is short. Godot's
                // attenuation, max distance fade and filter off (Player3DOutput)
                v.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled;
                v.MaxDistance = Player3DOutput.CurveMaxDistance(curve);
                v.AttenuationFilterDb = Player3DOutput.CurveFilterDb;
                float level = curve.PanLevelAt(d);
                v.PanningStrength = Player3DOutput.PanningStrength(level);   // FMOD's pan level, its gain on the volume
                v.VolumeDb = RolloffGains.ToDb(clip.Volume * curve.GainAt(d) * Player3DOutput.PanLevelGain(level));
            }
            else
            {
                v.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance;
                v.MaxDistance = 3000;
                v.AttenuationFilterDb = Player3DOutput.DefaultFilterDb;
                v.PanningStrength = 1;
            }
            v.Bus = SoundBuses.Of(clip.Category);
            v.Play();
        }
    }

    /// <summary>The landing sound (the client plays the "land" action from StateLand).</summary>
    public void Land() => Step("land");
}
