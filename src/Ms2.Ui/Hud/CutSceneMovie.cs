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
/// box 0x88c, whose OK (:312-322) runs Exit, hides the button and logs "cut_scene_stop"; the field's Esc does not skip
/// (FUN_1418fcb70 returns at the playing movie, uichatballoon.cpp:528-530).
/// The HUD plays a usm path (<see cref="IsUsm"/>, UICutSceneMovie::Play @0x14190fef0 picks the player by the extension) and
/// stops the pane at its end or on a load error (<see cref="Stop"/>). A swf path plays in the pane's own movie
/// (<see cref="CutSceneMovieController"/>, PlaySwf), whose onSwfStop calls <see cref="Exit"/>.
/// STILL-OPEN: the exit button and its timing, and box 0x88c: the port's Esc
/// (<see cref="SkipOnEsc"/>) stands in for the button's OK.
/// </summary>
public sealed class CutSceneMovie
{
    readonly Action<IOutPacket> send;

    public CutSceneMovie(Action<IOutPacket> send) { this.send = send; }

    /// <summary>The movie path of the playing cut scene, or null.</summary>
    public string Path { get; private set; }
    public int CutSceneId { get; private set; }
    public bool IsPlaying => Path != null;

    /// <summary>FUN_141863b70: a movie that plays is stopped first, then the id is stored and the path starts.</summary>
    public void Start(string path, int cutSceneId)
    {
        if (IsPlaying) Stop();
        CutSceneId = cutSceneId;
        Path = path;
    }

    /// <summary>UICutSceneMovie::Play (@0x14190fef0, uicutscenemovie.cpp:377-389): PathFindExtension ".usm" plays in the
    /// VideoPlayer (PlayUsm), ".swf" in the pane's movie (PlaySwf).</summary>
    public static bool IsUsm(string path) => path != null && path.EndsWith(".usm", StringComparison.OrdinalIgnoreCase);

    /// <summary>vfunc_53 (@0x14190ed50): stops the movie and clears the cut scene id (m_nCutSceneId = 0, uicutscenemovie.cpp:124).</summary>
    public void Stop()
    {
        Path = null;
        CutSceneId = 0;
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
        else send(new CutSceneExitTrigger(CutSceneId));
    }

    /// <summary>The port's Esc step for the movie (FUN_1419102c0 = it plays): skips it, taking the key, in place of the movie's
    /// exit button and its box 0x88c (the client's field Esc only stops at a playing movie).</summary>
    public bool SkipOnEsc()
    {
        if (!IsPlaying) return false;
        Exit();
        return true;
    }
}
