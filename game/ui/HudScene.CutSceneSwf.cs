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
/// STILL-OPEN: the movie's sound (SetMasterVolume from the movie volume option), the exit button's box 0x88c (Esc skips),
/// the start offset.
/// </summary>
public partial class HudScene
{
    CanvasLayer cutSceneSwfLayer;
    CutSceneMovieController cutSceneSwfPane;
    MovieDialog cutSceneSwfMovie;
    string cutSceneSwfPath;

    /// <summary>Each frame: a swf path that starts makes the pane and plays it; the path that stops (Esc, the movie's own end, the
    /// server's stop) hides it; the pane goes with its onHideComplete. A usm path or none leaves the pane alone.</summary>
    void TickCutSceneSwf(Ms2.Ui.Hud.CutSceneMovie movie)
    {
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
        if (cutSceneSwfLayer == null) AddChild(cutSceneSwfLayer = new CanvasLayer { Layer = 12 });
        cutSceneSwfMovie = new MovieDialog(assets, c, hudModel, size);
        cutSceneSwfLayer.AddChild(cutSceneSwfMovie);
        c.Play(path);
        GD.Print($"cut scene movie {movie.CutSceneId}: {path} plays in the pane");
    }

    void FreeCutSceneSwf()
    {
        if (cutSceneSwfMovie == null) { cutSceneSwfPane = null; return; }
        cutSceneSwfMovie.Host.Unload();
        cutSceneSwfMovie.QueueFree();
        cutSceneSwfMovie = null;
        cutSceneSwfPane = null;
    }
}
