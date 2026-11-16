using System.Xml.Linq;
using Godot;
using Ms2.Audio;
using Ms2.Formats.Pack;

namespace Ms2Godot;

/// <summary>
/// Sound of a loaded field, after CSoundEventMgr::PlayMusic and the fielddata bgm entry: the field's
/// table/fielddata.xml entry names a music cue (&lt;bgm id="35" param="5"/&gt;); the cue is resolved through MS2Sound.fev to
/// the bank sample and played looped. Also plays UI sounds by event name.
/// </summary>
public partial class FieldAudio : Node
{
    public const string UiClickWave = "System_Button_click_01";   // MS2Sound_System.fsb, the client's button click

    SoundLibrary library;
    AudioStreamPlayer music;
    AudioStreamPlayer ui;
    MusicPlan plan;

    public int BgmCue { get; private set; }
    public string BgmName => plan?.Loop.Name;
    public bool MusicPlaying => music != null && music.Playing;
    public double MusicPosition => music?.GetPlaybackPosition() ?? 0;

    /// <summary>The Data/Sound folder under the client's Data folder; null when there is none.</summary>
    public bool Open(string dataDir)
    {
        var dir = System.IO.Path.Combine(dataDir, "Sound");
        if (!System.IO.File.Exists(System.IO.Path.Combine(dir, "MS2Sound.fev")))
        {
            GD.PushWarning("audio: no Sound/MS2Sound.fev under " + dataDir);
            return false;
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        library = new SoundLibrary(dir);
        GD.Print($"audio: MS2Sound.fev read in {sw.ElapsedMilliseconds} ms: {library.Fev.Banks.Count} banks, {library.Fev.Defs.Count} sound definitions, {library.Fev.EventCount} events, {library.Fev.CueIds.Count()} music cues");
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
            var intro = p.Intro != null ? MpegDecoder.Decode(p.Intro) : null;
            var loop = MpegDecoder.Decode(p.Loop);
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
        music.VolumeDb = Mathf.LinearToDb(p.Loop.Volume);
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

    /// <summary>The button click.</summary>
    public bool PlayUiClick()
    {
        var clip = library?.FindSample("MS2Sound_System", UiClickWave);
        if (clip == null) return false;
        PlayClip(clip);
        return true;
    }

    void PlayClip(SoundClip clip)
    {
        ui.Stream = Stream(MpegDecoder.Decode(clip), false);
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
