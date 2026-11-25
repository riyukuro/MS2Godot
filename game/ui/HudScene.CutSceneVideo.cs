using Godot;

namespace Ms2Godot.Ui;

/// <summary>
/// The cut scene movie pane's usm player (UICutSceneMovie::PlayUsm @0x14190ffc0, uicutscenemovie.cpp:392-404: the movie's
/// VideoPlayer plays the path under the movie folder). The picture covers the screen on black, over the HUD (the pane is on
/// UI layer 2, vfunc_52 :87). The usm's end (vfunc_31 "end", :279-284) and a load error (vfunc_30 "error", :271-275) stop
/// the pane (vfunc_53) with nothing sent, so the server's trigger goes on at its own fallback unless the player skips (Esc asks box 0x88c,
/// <see cref="Ms2.Ui.Hud.CutSceneMovie.AskExitOnEsc"/>; its OK runs Exit).
/// The movie's sound is the usm's ADX stream, played at <c>MuteAll ? 0 : GetMovieVolume()</c> (:397, <see cref="Ms2.Formats.Options.GameOption.Sound.EffectiveMovieVolume"/>),
/// set before Play as the client does (see <see cref="VideoPlayerNode"/>).
/// The start offset (Seek(m_68), :399) is <see cref="Ms2.Ui.Hud.CutSceneMovie.StartSeconds"/>: every client caller passes 0.0f, so playing from the start is the seek.
/// STILL-OPEN: the exit button's widget, a start time above 0 (the player has no seek; no caller sends one). The swf movies are <see cref="HudScene.TickCutSceneSwf"/>.
/// </summary>
public partial class HudScene
{
    CanvasLayer cutSceneLayer;
    ColorRect cutSceneBack;
    VideoPlayerNode cutSceneVideo;
    string cutScenePath;

    /// <summary>Harness: the usm layer is up.</summary>
    public bool CutSceneVideoShown => cutSceneLayer?.Visible == true;

    /// <summary>Plays the pane's usm while it plays: true when a usm shows (or ended this frame), false for a swf or no movie.
    /// Called every frame, with a movie or not, so the layer goes when the movie stops or the session ends.</summary>
    bool TickCutSceneVideo(Ms2.Ui.Hud.CutSceneMovie movie)
    {
        string path = movie?.IsPlaying == true ? movie.Path : null;
        if (path != cutScenePath)
        {
            cutScenePath = path;
            cutSceneVideo?.Stop();
            if (cutSceneLayer != null) cutSceneLayer.Visible = false;
            if (Ms2.Ui.Hud.CutSceneMovie.IsUsm(path))
            {
                EnsureCutSceneLayer();
                cutSceneVideo.SoundVolume = GameOptions.Current.SoundOption.EffectiveMovieVolume;      // SetVolume before Play (:397)
                if (movie.StartSeconds != 0f) GD.Print($"cut scene movie {movie.CutSceneId}: start at {movie.StartSeconds}s is not supported, plays from the start");      // Seek(m_68) (:399)
                if (cutSceneVideo.Play(assets.Packs, path, false)) { cutSceneLayer.Visible = true; GD.Print($"cut scene movie {movie.CutSceneId}: {path} plays"); }
                else { GD.Print($"cut scene movie {movie.CutSceneId}: {path} cannot be read, stopped"); movie.Stop(); cutScenePath = null; return true; }   // vfunc_30
            }
        }
        if (cutSceneVideo == null || path == null || !Ms2.Ui.Hud.CutSceneMovie.IsUsm(path)) return false;
        var size = GetViewport().GetVisibleRect().Size;
        cutSceneBack.Size = size;
        var v = cutSceneVideo.Video;
        float scale = v == null || v.Width <= 0 || v.Height <= 0 ? 1 : Mathf.Min(size.X / v.Width, size.Y / v.Height);
        var fit = v == null ? size : new Vector2(v.Width * scale, v.Height * scale);   // the whole picture, centred on black
        cutSceneVideo.Size = fit;
        cutSceneVideo.Position = (size - fit) / 2;
        return true;
    }

    void EnsureCutSceneLayer()
    {
        if (cutSceneLayer != null) return;
        AddChild(cutSceneLayer = new CanvasLayer { Layer = 11, Visible = false });   // under the message box layer (12): the exit box 0x88c shows over the movie
        cutSceneLayer.AddChild(cutSceneBack = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Stop });
        cutSceneLayer.AddChild(cutSceneVideo = new VideoPlayerNode());
        cutSceneVideo.SoundStarted += note =>
        {
            if (Ms2Godot.Net.LiveSession.LiveCheck) GD.Print($"cut scene movie {Ms2Godot.Net.LiveSession.Instance?.Movie?.CutSceneId}: {cutScenePath} sound: {note}, player {(cutSceneVideo.SoundPlaying ? "playing" : "idle")} {cutSceneVideo.SoundVolumeDb?.ToString("F1") ?? "-"} dB");
        };
        cutSceneVideo.Finished += () =>
        {
            var movie = Ms2Godot.Net.LiveSession.Instance?.Movie;
            GD.Print($"cut scene movie {movie?.CutSceneId}: {cutScenePath} ended, stopped");
            if (movie?.IsPlaying == true && movie.Path == cutScenePath) movie.Stop();      // vfunc_31 "end" -> vfunc_53
        };
    }
}
