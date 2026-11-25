using Godot;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The cut scene movie pane's swf player (UICutSceneMovie::PlaySwf @0x141910110, uicutscenemovie.cpp:407-415): the pane's movie
/// (uicutscenemovie, <see cref="CutSceneMovieController"/> on the generated route) loads the swf cut scene movie itself
/// (playSwfMovie), plays it on its black backdrop and calls onSwfStop at its last frame. The pane is on UI layer 2 (vfunc_52
/// :87), so it covers the HUD: it has its own canvas layer above the HUD's. The stop (vfunc_53) hides the movie and the pane goes
/// when its onHideComplete comes.
/// The movie's sound is the swf's own stream sound (SoundStreamHead + one SoundStreamBlock per frame, ADPCM or MP3: every shipped
/// cut scene swf has exactly this and no event sounds, no DefineSound, no Sound in its script): the client hands it to its GFx
/// sound renderer, whose master volume PlaySwf sets first to <c>MuteAll ? 0 : GetMovieVolume()</c> (GFxManager::SetMasterVolume
/// @0x1422afdd0, uicutscenemovie.cpp:411-413); here <see cref="Ms2.Formats.Options.GameOption.Sound.EffectiveMovieVolume"/> is that
/// value and the stream plays in one player, from where the movie is when it has decoded, until the pane is unloaded.
/// The player keeps its own gain and is not on <see cref="SoundBuses.Movie"/>: that bus is the hud video's (UIVideoContents::SetVideoMuted
/// @0x14135afa0, uiupperhuddialog.cpp:1144-1158, which only CSoundEventMgr::SetAllMute reaches, soundeventmgr.cpp:1711-1723 through
/// CUIService::FUN_141adbf70, uiservice.cpp:1241-1246), and the window's focus loss mutes it without the focus gain unmuting it
/// (windowservice.cpp:801-831). The cut scene movie never reads that mute: its volume is set once, when it plays (:397, :413), so a
/// cut scene after an alt-tab is audible in the client and here.
/// STILL-OPEN: SetSoundFade (@0x14190f270: the music categories 2 and 1 fade out for the movie and back in after, :198-213), the
/// start offset.
/// </summary>
public partial class HudScene
{
    CanvasLayer cutSceneSwfLayer;
    CutSceneMovieController cutSceneSwfPane;
    MovieDialog cutSceneSwfMovie;
    string cutSceneSwfPath;
    AudioStreamPlayer cutSceneSwfSound;
    int cutSceneSwfSoundGeneration;
    readonly System.Collections.Concurrent.ConcurrentQueue<Action> cutSceneSwfSoundReady = new();

    /// <summary>Harness: the cut scene swf's stream sound plays now.</summary>
    public bool CutSceneSwfSoundPlaying => cutSceneSwfSound != null && GodotObject.IsInstanceValid(cutSceneSwfSound) && cutSceneSwfSound.Playing;

    /// <summary>Each frame: a swf path that starts makes the pane and plays it; the path that stops (Esc, the movie's own end, the
    /// server's stop) hides it; the pane goes with its onHideComplete. A usm path or none leaves the pane alone.</summary>
    void TickCutSceneSwf(Ms2.Ui.Hud.CutSceneMovie movie)
    {
        while (cutSceneSwfSoundReady.TryDequeue(out var start)) start();
        string path = movie?.IsPlaying == true && !Ms2.Ui.Hud.CutSceneMovie.IsUsm(movie.Path) ? movie.Path : null;
        if (path != cutSceneSwfPath)
        {
            if (cutSceneSwfPane != null && !cutSceneSwfPane.Hiding) cutSceneSwfPane.Stop();      // vfunc_53 -> the movie's hide
            if (path != null) { FreeCutSceneSwf(); OpenCutSceneSwf(movie, path); }               // a new movie replaces the old at once
            cutSceneSwfPath = path;
        }
    }

    void OpenCutSceneSwf(Ms2.Ui.Hud.CutSceneMovie movie, string path)
    {
        if (assets == null || hudModel == null) return;
        var c = new CutSceneMovieController(movie);
        var size = ScreenMode.PixelSize(GetWindow());
        c.HideComplete += () => { if (cutSceneSwfPane == c) FreeCutSceneSwf(); };
        cutSceneSwfPane = c;
        if (cutSceneSwfLayer == null) AddChild(cutSceneSwfLayer = new CanvasLayer { Layer = 11 });   // under the message box layer (12)
        cutSceneSwfMovie = new MovieDialog(assets, c, hudModel, size);
        cutSceneSwfLayer.AddChild(cutSceneSwfMovie);
        c.Play(path);
        StartCutSceneSwfSound(movie.CutSceneId, path);
        if (Ms2Godot.Net.LiveSession.LiveCheck) GD.Print($"cut scene movie {movie.CutSceneId}: {path} plays in the pane");
    }

    /// <summary>PlaySwf's volume (@0x141910110, :411-413) and the movie's own stream sound. The stream decodes on a pool thread
    /// (an MP3 stream takes a fraction of a second) and starts at the movie's elapsed time when it is ready, so picture and sound stay
    /// together.</summary>
    void StartCutSceneSwfSound(int cutSceneId, string path)
    {
        StopCutSceneSwfSound();
        int generation = ++cutSceneSwfSoundGeneration;
        float volume = GameOptions.Current.SoundOption.EffectiveMovieVolume;
        ulong started = Time.GetTicksMsec();
        var packs = assets.Packs;
        bool live = Ms2Godot.Net.LiveSession.LiveCheck;
        Task.Run(() =>
        {
            Ms2.Audio.Pcm16 pcm = null;
            string note = null;
            try
            {
                var entry = packs.FindByFileName("Resource/Gfx", System.IO.Path.GetFileNameWithoutExtension(path) + ".gfx");
                if (entry == null) note = "the movie is not shipped";
                else
                {
                    var movie = Ms2.Swf.SwfMovie.Parse(packs.Read("Resource/Gfx", entry));
                    pcm = Ms2.Audio.SwfStreamDecoder.Decode(movie);
                    if (pcm == null) note = "the movie has no stream sound";
                    else note = $"{(movie.SoundStream.Codec == 1 ? "ADPCM" : movie.SoundStream.Codec == 2 ? "MP3" : "PCM")} {pcm.Rate} Hz x{pcm.Channels} {pcm.Seconds:F2}s";
                }
            }
            catch (Exception e) { note = $"cannot be decoded ({e.GetType().Name}: {e.Message})"; }
            cutSceneSwfSoundReady.Enqueue(() => FinishCutSceneSwfSound(generation, cutSceneId, path, pcm, note, volume, started, live));
        });
    }

    /// <summary>The main-thread end of <see cref="StartCutSceneSwfSound"/>: builds the player from the decoded PCM and starts it at the
    /// movie's elapsed time (volume 0 builds it without playing).</summary>
    void FinishCutSceneSwfSound(int generation, int cutSceneId, string path, Ms2.Audio.Pcm16 pcm, string note, float volume, ulong started, bool live)
    {
        if (generation != cutSceneSwfSoundGeneration || cutSceneSwfPane == null) return;      // the movie stopped while it decoded
        if (pcm == null) { if (live) GD.Print($"cut scene movie {cutSceneId}: {path} sound: {note}"); return; }
        var player = new AudioStreamPlayer
        {
            Name = "CutSceneSwfSound",
            Stream = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, Stereo = pcm.Channels == 2, MixRate = pcm.Rate, Data = pcm.ToBytes() },
            VolumeDb = volume <= 0f ? -80f : Mathf.LinearToDb(volume),
        };
        AddChild(player);
        float offset = (Time.GetTicksMsec() - started) / 1000f;
        if (volume > 0f && offset < pcm.Seconds) player.Play(offset);
        cutSceneSwfSound = player;
        if (live) GD.Print($"cut scene movie {cutSceneId}: {path} sound: {note}, volume {volume:F2} ({(volume > 0f ? $"plays from {offset:F2}s" : "silent")}), movie bus {SoundBuses.BusGain(SoundBuses.Movie):F2}, categories {(Ms2.Formats.Options.SoundMix.Shared.AppActive ? "on" : "muted")}");
    }

    /// <summary>Harness: the 0-based frame of the loaded cut scene swf's main timeline, or -1 without one (a before/after shot at the same frame).</summary>
    public int CutSceneSwfFrame()
    {
        var host = cutSceneSwfPane?.Host;
        if (host == null) return -1;
        var content = host.Get(host.Get(host.Entity, "m_swfLoader"), "content");
        return content is Ms2.Avm2.Flash.AsMovieClip mc ? mc.Frame : -1;
    }

    /// <summary>Harness: the exit button of the swf pane clicked with the mouse on the movie's own m_exit (the click reaches the movie's
    /// onExit, which asks box 0x88c). False without a pane or a visible button.</summary>
    public bool CutSceneSwfExitClickScripted()
    {
        var host = cutSceneSwfPane?.Host;
        if (host?.Child("m_exit") is not Ms2.Avm2.Flash.AsDisplayObject exit || !exit.IsVisible) return false;
        var b = exit.GetBounds(null);
        host.Player.Click(b.X + b.W / 2, b.Y + b.H / 2);
        return true;
    }

    void StopCutSceneSwfSound()
    {
        cutSceneSwfSoundGeneration++;
        if (cutSceneSwfSound != null && GodotObject.IsInstanceValid(cutSceneSwfSound))
        {
            cutSceneSwfSound.Stop();
            cutSceneSwfSound.QueueFree();
            if (Ms2Godot.Net.LiveSession.LiveCheck) GD.Print($"cut scene movie {Ms2Godot.Net.LiveSession.Instance?.Movie?.CutSceneId}: {cutSceneSwfPath} sound stopped");
        }
        cutSceneSwfSound = null;
    }

    void FreeCutSceneSwf()
    {
        StopCutSceneSwfSound();
        if (cutSceneSwfMovie == null) { cutSceneSwfPane = null; return; }
        cutSceneSwfMovie.Host.Unload();
        cutSceneSwfMovie.QueueFree();
        cutSceneSwfMovie = null;
        cutSceneSwfPane = null;
    }
}
