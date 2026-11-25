using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;

namespace Ms2.Ui.Hud;

/// <summary>
/// The cut scene movie pane's state (UICutSceneMovie, ui id 0x4e7e, uicutscenemovie.cpp). The trigger UI's start (type 9
/// sub 4, CTriggerService::FUN_141863b70 @0x141863b70, triggerservice.cpp:962) stops a movie that still plays
/// (vfunc_53), stores the cut scene id (SetCutSceneId @0x14190f0b0) and starts the path (vfunc_52). Sub 5
/// (StopCutScene @0x141863c50) calls StopIfCutScene (@0x14190f0d0): stop when the id is the playing one. The player's skip
/// calls <see cref="Exit"/> (UICutSceneMovie::Exit @0x141910220): with a cut scene id it sends the Trigger request
/// (type 9, sub 5, the id) and waits for the server's sub 5 to stop it; without one it stops at once.
/// The skip is the only send: a usm that plays to its end stops itself with nothing sent (vfunc_31 @0x14190f7d0 takes the
/// player's "end" to vfunc_53, uicutscenemovie.cpp:279-284), and so does a load error (vfunc_30, :271-275); only a swf
/// movie's own stop (onSwfStop, :300-301) goes through Exit. A server that waits for the movie's stop therefore waits
/// its own time when the player does not skip. In the client the skip is the movie's exit button: onExit (:296-298) asks
/// box 0x88c, whose OK (:312-322) runs Exit, hides the button and logs "cut_scene_stop". The pane's own key handler
/// (vfunc_13 @0x14190f510, :246-254) takes Esc to the same box while the button shows and no box is up
/// (<see cref="AskExitOnEsc"/>); the field's Esc does not skip (FUN_1418fcb70 returns at the playing movie,
/// uichatballoon.cpp:528-530, <see cref="TakesEsc"/>). Nothing in the client stops the pane on a field change: a usm plays
/// to its end, a load error, the server's stop or a confirmed skip.
/// The HUD plays a usm path (<see cref="IsUsm"/>, UICutSceneMovie::Play @0x14190fef0 picks the player by the extension) and
/// stops the pane at its end or on a load error (<see cref="Stop"/>). A swf path plays in the pane's own movie
/// (<see cref="CutSceneMovieController"/>, PlaySwf), whose onSwfStop calls <see cref="Exit"/>.
/// STILL-OPEN: the exit button's own widget on a usm (the pane's movie is not loaded for a usm here: <see cref="ExitShown"/>
/// stands in for m_exit's visibility) and, for the intro story, the video time that shows it (vfunc_15 @0x14190f600);
/// the "cut_scene_stop" log (CBigDataLogSystem) the box's OK writes.
/// </summary>
public sealed class CutSceneMovie
{
    readonly Action<IOutPacket> send;

    public CutSceneMovie(Action<IOutPacket> send) { this.send = send; }

    /// <summary>The movie path of the playing cut scene, or null.</summary>
    public string Path { get; private set; }
    public int CutSceneId { get; private set; }
    public bool IsPlaying => Path != null;

    /// <summary>The Trigger requests <see cref="Exit"/> sent (a live check counts them).</summary>
    public int Sends { get; private set; }

    /// <summary>m_exit shows (ShowSkipButton @0x14190fcb0, from OnLoadComplete @0x14190ee90 unless the intro story: that one
    /// shows it later, vfunc_15) and is hidden by the box's OK.</summary>
    public bool ExitShown { get; private set; }

    /// <summary>vfunc_52 (@0x14190ec00, :80): m_bIntroStory is the path "Common/MS2IntroStory.usm".</summary>
    public static bool IsIntroStory(string path) => path == "Common/MS2IntroStory.usm";

    /// <summary>The sound fade the host runs (CSoundEventMgr's fade of the environment and bgm categories, 1000 ms): true fades
    /// them out, false back in. Called only when the state changes (<see cref="SoundFaded"/>).</summary>
    public Action<bool> SoundFade { get; set; }

    /// <summary>m_bSoundFaded (uicutscenemovie.cpp:197-213): the pane has faded the sound out and not yet back in.</summary>
    public bool SoundFaded { get; private set; }

    /// <summary>UICutSceneMovie::SetSoundFade @0x14190f270 (uicutscenemovie.cpp:197-213): returns when m_bSoundFaded already
    /// has the value; else the host reapplies the sound options and starts the 1000 ms fade of categories 2 and 1.</summary>
    void SetSoundFade(bool fade)
    {
        if (SoundFaded == fade) return;
        SoundFade?.Invoke(fade);
        SoundFaded = fade;
    }

    /// <summary>m_68: vfunc_52's second argument (uicutscenemovie.cpp:78-81, named fVolume there), the time PlayUsm seeks to after Play
    /// (:399, ASVideoPlayer::Seek @0x1422E9690, an Invoke("seek", fTime)). The three callers, FUN_141863b70 (triggerservice.cpp:973),
    /// PlayDefaultScene (:452) and PlayPendingScene (:462), all pass 0.0f, so the seek is to the start of a movie that has just begun.</summary>
    public float StartSeconds { get; private set; }

    /// <summary>FUN_141863b70: a movie that plays is stopped first, then the id is stored and the path starts. vfunc_52
    /// (@0x14190ec00, :80-86) stores the path and the seek time (m_68) and returns at an empty path, else SetSoundFade(true).
    /// STILL-OPEN: no client caller passes a start time other than 0, and the host's player has no seek, so the host plays from the start.</summary>
    public void Start(string path, int cutSceneId, float startSeconds = 0f)
    {
        if (IsPlaying) Stop();
        CutSceneId = cutSceneId;
        Path = path;
        StartSeconds = startSeconds;
        ExitShown = !IsIntroStory(path);
        if (!string.IsNullOrEmpty(path)) SetSoundFade(true);
    }

    /// <summary>UICutSceneMovie::Play (@0x14190fef0, uicutscenemovie.cpp:377-389): PathFindExtension ".usm" plays in the
    /// VideoPlayer (PlayUsm), ".swf" in the pane's movie (PlaySwf).</summary>
    public static bool IsUsm(string path) => path != null && path.EndsWith(".usm", StringComparison.OrdinalIgnoreCase);

    /// <summary>vfunc_53 (@0x14190ed50): stops the movie, SetSoundFade(false) (:113) and clears the cut scene id (m_nCutSceneId = 0, uicutscenemovie.cpp:124).</summary>
    public void Stop()
    {
        SetSoundFade(false);
        Path = null;
        CutSceneId = 0;
        ExitShown = false;
    }

    /// <summary>UICutSceneMovie::StopIfCutScene (@0x14190f0d0): the id has to be the stored one.</summary>
    public void StopIfCutScene(int cutSceneId)
    {
        if (CutSceneId == cutSceneId) Stop();
    }

    /// <summary>UICutSceneMovie::Exit (@0x141910220): without a cut scene id the movie stops; otherwise the Trigger request is sent and the movie plays on until the server's stop.</summary>
    public void Exit()
    {
        if (CutSceneId == 0) Stop();
        else { Sends++; send(new CutSceneExitTrigger(CutSceneId)); }
    }

    /// <summary>The pane's own m_exit widget when the pane has one (a swf movie's pane: true while it is visible, false while the
    /// movie has no m_exit yet), null for a usm, which has no widget here and uses <see cref="ExitShown"/> in its place.</summary>
    public Func<bool> ExitButtonVisible { get; set; }

    bool ExitVisible => ExitButtonVisible?.Invoke() ?? ExitShown;

    /// <summary>The pane's key handler (vfunc_13 @0x14190f510, uicutscenemovie.cpp:246-254): Esc with the exit button visible and
    /// no message box up asks box 0x88c (FUN_140d73bc0(0x88c, 1, this, "")) and takes the key. Nothing is sent before the answer.</summary>
    public bool AskExitOnEsc()
    {
        if (!IsPlaying || !ExitVisible || !MessageBoxQueue.IsIdle) return false;
        AskExit();
        return true;
    }

    /// <summary>onExit from the movie (the exit button's click, vfunc_28 @0x14190f840, :296-298): the same box, asked without the
    /// key handler's idle check.</summary>
    public void AskExit() => MessageBoxQueue.ShowCommon(0x88c, 1, answer ??= new Answer(this), "");

    /// <summary>The box's yes ran Exit: the pane shows its exit button and plays its "hide" (:315-318).</summary>
    public event Action ExitAnswered;

    /// <summary>The field's Esc at a playing movie (FUN_1419102c0, FUN_1418fcb70 :528-530): it returns without skipping.</summary>
    public bool TakesEsc() => IsPlaying;

    Answer answer;

    /// <summary>OnMessage (@0x14190f980, :312-322): the box's first button runs Exit, then m_exit->vfunc_3(1) (SetVisible(true))
    /// and vfunc_13("hide") (gotoAndPlay "hide"), and shows the next queued box (FUN_14151f200); the answer is handled (1). The
    /// button stays visible, so a later Esc asks the box again until the server's stop comes. Any other button leaves the box to
    /// close itself. The "cut_scene_stop" log (CBigDataLogSystem) is STILL-OPEN.</summary>
    sealed class Answer(CutSceneMovie m) : IMessageBoxListener
    {
        public bool OnMessageBox(MessageBoxController sender, int button, string tag)
        {
            if (button != 0) return false;
            m.Exit();
            m.ExitShown = true;
            m.ExitAnswered?.Invoke();
            MessageBoxQueue.Next();
            return true;
        }
    }
}
