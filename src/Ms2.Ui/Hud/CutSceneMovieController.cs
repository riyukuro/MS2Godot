using Ms2.Avm2;

namespace Ms2.Ui.Hud;

/// <summary>
/// UICutSceneMovie (ms2/client/ms2/uicutscenemovie.cpp, UI id 0x4e7e, movie "uicutscenemovie"): the C++ side of the movie's
/// CutSceneMovie entity, on the generated route, for a swf cut scene movie. UICutSceneMovie::PlaySwf (@0x141910110, :407-415)
/// only calls the movie's playSwfMovie(path); the movie's own Loader loads the second movie (Loader.load of the path), and
/// once it is loaded (onSwfLoadComplete) aligns it on the stage and listens to its ENTER_FRAME: at its last frame the movie
/// stops it and calls onSwfStop (onPlayEnterFrame), which UICutSceneMovie::vfunc_28 (@0x14190f840, :296-301) takes to Exit().
/// The stop (vfunc_53 @0x14190ed50, :107-129) calls the movie's hide (a tween out of the black backdrop and the content); the
/// movie's onHideComplete (unloadAndStop of the loaded movie, then the call below) ends the pane (:288-293).
/// The state (the path, the cut scene id, Exit's send) is <see cref="CutSceneMovie"/>.
/// The exit button's click (onExit) and Esc ask box 0x88c (<see cref="CutSceneMovie.AskExit"/>, <see cref="CutSceneMovie.AskExitOnEsc"/>);
/// the box's OK runs Exit, shows m_exit and plays its "hide". The movie's sound is the swf's stream sound, played by the HUD (the
/// volume is PlaySwf's, :411-413).
/// STILL-OPEN: the start offset, the "cut_scene_stop" log.
/// </summary>
public sealed class CutSceneMovieController : UiController
{
    public override string Movie => "uicutscenemovie";
    /// <summary>CutSceneMovie's own method (the movie's CutSceneMovie class).</summary>
    protected override string EntityMethod => "playSwfMovie";
    protected override bool IsDialog => false;                  // a UISwfEntity on layer 2
    public override string TypeName => "CutSceneMovie";

    public readonly CutSceneMovie State;

    /// <summary>The path playSwfMovie got, or null when none plays (m_strPath).</summary>
    public string Playing { get; private set; }
    /// <summary>The movie's hide was called and its onHideComplete has not come.</summary>
    public bool Hiding { get; private set; }
    /// <summary>The movie's onHideComplete came: the pane is done (FUN_1422dc080, m_strPath emptied).</summary>
    public event Action HideComplete;
    /// <summary>The movie's swf ended (onSwfStop), after Exit ran.</summary>
    public int SwfStops { get; private set; }

    string pending;
    readonly Func<bool> gamepad;
    readonly Func<string> gamepadKeyName;

    /// <param name="gamepad">The game option's gamepad flag (ShowSkipButton :562): the skip key is the action key's name.</param>
    /// <param name="gamepadKeyName">GetActionKeyName(0x49409, 2) of the key table.</param>
    public CutSceneMovieController(CutSceneMovie state, Func<bool> gamepad = null, Func<string> gamepadKeyName = null)
    {
        State = state;
        this.gamepad = gamepad;
        this.gamepadKeyName = gamepadKeyName;
        state.ExitButtonVisible = ExitButtonVisible;         // Esc asks only while m_exit is there and visible (vfunc_13 :246-254)
        state.ExitAnswered += OnExitAnswered;
    }

    /// <summary>m_exit's visibility (ASDisplayObject::GetVisible): false until the movie is loaded and ShowSkipButton ran.</summary>
    bool ExitButtonVisible() => Host?.Entity != null && Host.Child("m_exit") is { } exit && Rt.ToBoolean(Host.Get(exit, "visible"));

    /// <summary>OnMessage's yes (@0x14190f980, :315-318): m_exit->vfunc_3(1) (SetVisible(true)) and vfunc_13("hide") (gotoAndPlay "hide").</summary>
    void OnExitAnswered()
    {
        if (Host?.Entity == null || Host.Child("m_exit") is not { } exit) return;
        Host.SetVisible(exit, true);
        Host.Invoke(exit, "gotoAndPlay", "hide");
    }

    public bool Loaded => Host?.Entity != null;

    /// <summary>OnLoadInit (:430-437): the video player's size goes to setStageSize; OnLoadComplete (:229-238): m_exit is found
    /// and the skip button shows (ShowSkipButton, not for the intro story). A swf that was waiting for the load plays now
    /// (vfunc_11 :400-411: Play once the movie is loaded and the local player is there).</summary>
    public override void OnLoadComplete()
    {
        if (Host.Entity == null) return;
        if (Host.Child("m_videoPlayer") is { } player)
            Host.Invoke("setStageSize", (object)Rt.ToInt32(Host.Get(player, "width")), Rt.ToInt32(Host.Get(player, "height")));
        ShowSkipButton();
        if (pending != null) { var p = pending; pending = null; Play(p); }
    }

    /// <summary>ShowSkipButton (@0x14190fcb0, :540-557): m_exit shown and played from "show", then setSkipKey with the skip key
    /// text and the skip label (the gamepad's action key name when the gamepad is on).</summary>
    void ShowSkipButton()
    {
        if (Host.Child("m_exit") is not { } exit) return;
        Host.SetVisible(exit, true);
        Host.Invoke(exit, "gotoAndPlay", "show");
        string key = "$s_cutscene_skip_key";
        if (gamepad?.Invoke() == true && gamepadKeyName?.Invoke() is { } name) key = name;
        Host.Invoke("setSkipKey", (object)key, "$s_cutscene_skip");
    }

    /// <summary>UICutSceneMovie::PlaySwf (@0x141910110): playSwfMovie(path). Before the movie is loaded the path waits.</summary>
    public void Play(string path)
    {
        Playing = path;
        Hiding = false;
        if (!Loaded) { pending = path; return; }
        Host.Invoke(Host.Entity, "playSwfMovie", path);
    }

    /// <summary>vfunc_53 (:112-114): hide on the movie, when it has loaded.</summary>
    public void Stop()
    {
        pending = null;
        if (Playing == null) return;
        Hiding = true;
        if (Loaded) Host.Invoke("hide"); else Finish();
    }

    void Finish()
    {
        Hiding = false;
        Playing = null;
        HideComplete?.Invoke();
    }

    /// <summary>vfunc_28 (@0x14190f840): onHideComplete empties the path; onExit (the exit button's click) asks box 0x88c (:296-298,
    /// <see cref="CutSceneMovie.AskExit"/>); onSwfStop is Exit (a cut scene id sends the Trigger request and the movie waits for the
    /// server's stop).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        switch (name)
        {
            case "onHideComplete":
                Finish();
                return true;
            case "onExit":
                State.AskExit();
                return true;
            case "onSwfStop":
                SwfStops++;
                State.Exit();
                return false;     // the client returns 0 after Exit (:301 falls out of the if chain)
        }
        return false;
    }
}
