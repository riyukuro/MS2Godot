using Godot;
using Ms2.Audio;
using Ms2.Formats.World;

namespace Ms2Godot;

/// <summary>
/// Footsteps after CGroundEffectDataManager::PlayEffects: the ground material under the character (the cube entity's MapAttribute)
/// picks groundeffect/&lt;ground&gt;.xml, the step's action (walk, run0, run1, land) the sound events. The client fires a step when the
/// animation's text key "run*" / "walk*" is reached (PCBase::TextKeyEvent); the player's KF files in this client data carry
/// only start/end keys, so steps follow a fixed cadence of two per animation cycle (run0 then run1, the two feet) unless
/// <see cref="TextKeys"/> supplies keys for an animation.
/// </summary>
public partial class FootstepSounds : Node
{
    const float Cell = 150f;   // cube size of the field's ground entities

    SoundLibrary library;
    /// <summary>The sound bank; null plays the steps' effect files only.</summary>
    public SoundLibrary Library { get => library; set => library = value; }
    readonly GroundEffectTable table;
    readonly CharacterBody3D body;
    readonly AnimationPlayer anim;
    readonly Dictionary<(int, int, int), string> grounds = new();
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

    public FootstepSounds(SoundLibrary library, GroundEffectTable table, FieldMap map, CharacterBody3D body, AnimationPlayer anim)
    {
        this.library = library; this.table = table; this.body = body; this.anim = anim;
        Name = "FootstepSounds";
        foreach (var e in map.Entities)
        {
            var g = e.Get("MapAttribute");
            if (string.IsNullOrEmpty(g)) continue;
            var p = e.Position;
            grounds[((int)Math.Floor(p.X / Cell), (int)Math.Floor(p.Y / Cell), (int)Math.Floor(p.Z / Cell))] = g;
        }
        for (int i = 0; i < voices.Length; i++) { voices[i] = new AudioStreamPlayer3D { MaxDistance = 3000, UnitSize = 300 }; body.AddChild(voices[i]); }
    }

    /// <summary>The ground material at a Godot-space point (standing on it), "default" when there is none.</summary>
    public string GroundAt(Vector3 p)
    {
        int cx = (int)Math.Floor(p.X / Cell), cy = (int)Math.Floor(-p.Z / Cell), cz = (int)Math.Floor((p.Y - 1f) / Cell);
        for (int dz = 0; dz < 4; dz++)
            if (grounds.TryGetValue((cx, cy, cz - dz), out var g)) return g;
        return "default";
    }

    public override void _Process(double delta)
    {
        string name = anim.CurrentAnimation.ToString();
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
        LastGround = GroundAt(body.GlobalPosition);
        LastAction = action;
        stepCount++;
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
            var v = voices[nextVoice++ % voices.Length];
            v.Stream = wav;
            v.VolumeDb = Mathf.LinearToDb(clip.Volume);
            v.Play();
        }
    }

    /// <summary>The landing sound (the client plays the "land" action from StateLand).</summary>
    public void Land() => Step("land");
}
