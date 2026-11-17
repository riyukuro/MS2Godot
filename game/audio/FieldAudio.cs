using System.Xml.Linq;
using Godot;
using Ms2.Audio;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2Godot;

/// <summary>
/// Sound of a loaded field, after CSoundEventMgr::PlayMusic and the fielddata bgm entry: the field's
/// table/fielddata.xml entry names a music cue (&lt;bgm id="35" param="5"/&gt;); the cue is resolved through MS2Sound.fev to
/// the bank sample and played looped. Also plays UI sounds by event name.
/// </summary>
public partial class FieldAudio : Node
{
    public const string UiClickWave = "System_Button_click_01";   // MS2Sound_System.fsb, the client's button click

    public static FieldAudio Instance { get; private set; }

    SoundLibrary library;
    SystemSoundTable systemSounds;
    public SoundLibrary Library => library;
    public AmbientSounds Ambient { get; set; }
    public FootstepSounds Footsteps { get; private set; }
    AudioStreamPlayer music;
    AudioStreamPlayer ui;
    MusicPlan plan;

    /// <summary>Music category volume, the client's CSoundEventMgr::m_fNowMusicVolume (sound options
    /// slider), multiplied onto the FEV-authored cue volume. The client default is 1.0 (full); we start
    /// lower so background music does not dominate. Settable to drive a volume slider later.</summary>
    public float MusicVolume { get; set; } = 0.5f;

    public int BgmCue { get; private set; }
    public string BgmName => plan?.Loop.Name;
    public bool MusicPlaying => music != null && music.Playing;
    public double MusicPosition => music?.GetPlaybackPosition() ?? 0;

    /// <summary>The Data/Sound folder under the client's Data folder; null when there is none.</summary>
    public bool Open(string dataDir, PackSet packs = null)
    {
        Instance = this;
        var dir = System.IO.Path.Combine(dataDir, "Sound");
        if (!System.IO.File.Exists(System.IO.Path.Combine(dir, "MS2Sound.fev")))
        {
            GD.PushWarning("audio: no Sound/MS2Sound.fev under " + dataDir);
            return false;
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        library = new SoundLibrary(dir);
        GD.Print($"audio: MS2Sound.fev read in {sw.ElapsedMilliseconds} ms: {library.Fev.Banks.Count} banks, {library.Fev.Defs.Count} sound definitions, {library.Fev.EventCount} events, {library.Fev.CueIds.Count()} music cues");
        if (packs != null)
            try { systemSounds = SystemSoundTable.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/systemsound.xml"))); }
            catch (Exception ex) { GD.PushWarning("audio: systemsound.xml: " + ex.Message); }
        music = new AudioStreamPlayer { Name = "Music" };
        ui = new AudioStreamPlayer { Name = "Ui" };
        AddChild(music);
        AddChild(ui);
        return true;
    }

    /// <summary>The bgm cue of a field's fielddata entry (default environment), 0 when the field has none.</summary>
    public static int FieldBgmCue(PackSet packs, string xblockName)
    {
        var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/fielddata.xml")));
        foreach (var fd in doc.Root.Elements("fieldData"))
        {
            var env = fd.Elements("environment").FirstOrDefault(e => e.Attribute("locale") == null);
            var xb = (string)env?.Element("xblock")?.Attribute("name");
            if (xb == null || !xb.Equals(xblockName, StringComparison.OrdinalIgnoreCase)) continue;
            return (int?)env.Element("bgm")?.Attribute("id") ?? 0;
        }
        return 0;
    }

    /// <summary>
    /// PlayMusic(cue): resolve, decode on a worker thread and start looped. Returns false when the cue is unknown.
    /// </summary>
    public bool PlayBgm(int cue)
    {
        BgmCue = cue;
        if (library == null || cue == 0) return false;
        plan = library.ResolveMusic(cue);
        if (plan == null)
        {
            GD.PushWarning($"audio: music cue {cue} not found in MS2Sound.fev");
            return false;
        }
        var p = plan;
        music.Stop();
        Unchain();
        System.Threading.Tasks.Task.Run(() =>
        {
            var intro = p.Intro != null ? SafeDecode.Decode(p.Intro) : null;
            var loop = SafeDecode.Decode(p.Loop);
            if (loop == null) return;   // undecodable loop: the cue stays silent (warning logged)
            Callable.From(() => StartMusic(p, intro, loop)).CallDeferred();
        });
        return true;
    }

    Pcm16 loopPcm;

    void StartMusic(MusicPlan p, Pcm16 intro, Pcm16 loop)
    {
        if (p != plan) return;   // another cue was requested meanwhile
        loopPcm = loop;
        if (intro != null) { music.Stream = Stream(intro, false); music.Finished += OnMusicFinished; chained = true; }
        else music.Stream = Stream(loop, true);
        music.VolumeDb = Mathf.LinearToDb(p.Loop.Volume * MusicVolume);
        music.Play();
        GD.Print($"audio: bgm cue {p.Cue} -> {(intro != null ? p.Intro.Name + " + " : "")}{p.Loop.Name} ({loop.Seconds:F1} s, {loop.Rate} Hz, {loop.Channels} ch), playing={music.Playing}");
    }

    bool chained;   // the intro is playing and OnMusicFinished starts the loop

    void Unchain()
    {
        if (!chained) return;
        music.Finished -= OnMusicFinished;
        chained = false;
    }

    void OnMusicFinished()
    {
        Unchain();
        music.Stream = Stream(loopPcm, true);
        music.Play();
    }

    /// <summary>Play a sound event by name (event volume from the FEV).</summary>
    public bool PlayEvent(string eventName)
    {
        var clip = library?.ResolveEvent(eventName);
        if (clip == null) return false;
        PlayClip(clip);
        GD.Print($"audio: event {eventName} -> {clip.Name} volume {clip.Volume:F2}");
        return true;
    }

    /// <summary>
    /// A UI sound by the client's key (table/systemsound.xml: button_click, dialog_open, dialog_close, ...), played as the
    /// event the table names. Returns false when the key has no sound.
    /// </summary>
    public bool PlayUi(string key)
    {
        var path = systemSounds?.Resolve(key);
        return path != null && PlayEvent(path);
    }

    public bool PlayUiClick() => PlayUi("button_click");

    /// <summary>The field's sound: bgm cue, ambient sound entities and, when a player is given, footsteps. Never throws.</summary>
    public static FieldAudio Start(Node parent, string dataDir, PackSet packs, string fieldName, FieldMap map, CharacterBody3D player = null, AnimationPlayer anim = null)
    {
        try
        {
            var a = new FieldAudio { Name = "FieldAudio" };
            parent.AddChild(a);
            if (!a.Open(dataDir, packs)) return a;
            a.PlayBgm(FieldBgmCue(packs, fieldName));
            a.Ambient = new AmbientSounds(a.library);
            a.AddChild(a.Ambient);
            a.Ambient.Build(map);
            GD.Print($"audio: {a.Ambient.Count} ambient sound entities");
            if (player != null && anim != null)
            {
                var ground = new GroundEffectTable(p => { try { return System.Text.Encoding.UTF8.GetString(packs.Read("Xml", p)); } catch { return null; } });
                a.Footsteps = new FootstepSounds(a.library, ground, map, player, anim);
                a.AddChild(a.Footsteps);
            }
            return a;
        }
        catch (Exception ex) { GD.PushWarning("audio: " + ex.Message); return null; }
    }

    readonly AudioStreamPlayer3D[] voices3d = new AudioStreamPlayer3D[16];
    int nextVoice3d;
    readonly Dictionary<string, AudioStreamWav> decoded = new();
    public int PositionalPlayed;

    /// <summary>
    /// Play a sound event at a world position (effect soundEvent parts, npc sounds). <paramref name="path"/> is the event name or
    /// "group/name"; <paramref name="delay"/> seconds later. Attenuation follows the event's 3D distances.
    /// </summary>
    public bool PlayEventAt(string path, Vector3 position, float delay = 0)
    {
        var clip = library?.ResolveEvent(path);
        if (clip == null) return false;
        if (!decoded.TryGetValue(clip.Name, out var wav))
        {
            var pcm = SafeDecode.Decode(clip);
            decoded[clip.Name] = wav = pcm == null ? null : new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, Stereo = pcm.Channels == 2, MixRate = pcm.Rate, Data = pcm.ToBytes() };
        }
        if (wav == null) return false;
        var v = voices3d[nextVoice3d++ % voices3d.Length];
        if (v == null) { v = voices3d[(nextVoice3d - 1) % voices3d.Length] = new AudioStreamPlayer3D { MaxDistance = 3000, UnitSize = 300 }; AddChild(v); }
        v.Stream = wav;
        v.GlobalPosition = position;
        v.VolumeDb = Mathf.LinearToDb(clip.Volume);
        v.MaxDistance = clip.MaxDistance > 0 ? clip.MaxDistance : 3000;
        v.UnitSize = clip.MinDistance > 0 ? clip.MinDistance : 300;
        if (delay > 0) GetTree().CreateTimer(delay).Timeout += () => v.Play(); else v.Play();
        PositionalPlayed++;
        return true;
    }

    void PlayClip(SoundClip clip)
    {
        var pcm = SafeDecode.Decode(clip);
        if (pcm == null) return;
        ui.Stream = Stream(pcm, false);
        ui.VolumeDb = Mathf.LinearToDb(clip.Volume);
        ui.Play();
    }

    // The banks hold MPEG layer II, which Godot's mp3 decoder rejects: play the decoded PCM.
    static AudioStreamWav Stream(Pcm16 pcm, bool loop) => new()
    {
        Format = AudioStreamWav.FormatEnum.Format16Bits,
        Stereo = pcm.Channels == 2,
        MixRate = pcm.Rate,
        Data = pcm.ToBytes(),
        LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled,
        LoopBegin = 0,
        LoopEnd = pcm.Frames,
    };
}
