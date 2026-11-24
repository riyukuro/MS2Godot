using Godot;

namespace Ms2Godot.Ui;

/// <summary>
/// The cut scene movie pane's usm player (UICutSceneMovie::PlayUsm @0x14190ffc0, uicutscenemovie.cpp:392-404: the movie's
/// VideoPlayer plays the path under the movie folder). The picture covers the screen on black, over the HUD (the pane is on
/// UI layer 2, vfunc_52 :87). The usm's end (vfunc_31 "end", :279-284) and a load error (vfunc_30 "error", :271-275) stop
/// the pane (vfunc_53) with nothing sent, so the server's trigger goes on at its own fallback unless the player skips (Esc asks box 0x88c,
/// <see cref="Ms2.Ui.Hud.CutSceneMovie.AskExitOnEsc"/>; its OK runs Exit).
/// STILL-OPEN: the movie's sound (the usm audio is not decoded), the exit button's widget, the start offset
/// (Seek(m_68)). The swf movies are <see cref="HudScene.TickCutSceneSwf"/>.
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
        AddChild(cutSceneLayer = new CanvasLayer { Layer = 12, Visible = false });
        cutSceneLayer.AddChild(cutSceneBack = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Stop });
        cutSceneLayer.AddChild(cutSceneVideo = new VideoPlayerNode());
        cutSceneVideo.Finished += () =>
        {
            var movie = Ms2Godot.Net.LiveSession.Instance?.Movie;
            GD.Print($"cut scene movie {movie?.CutSceneId}: {cutScenePath} ended, stopped");
            if (movie?.IsPlaying == true && movie.Path == cutScenePath) movie.Stop();      // vfunc_31 "end" -> vfunc_53
        };
    }
}
