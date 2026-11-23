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
/// STILL-OPEN: the movie itself (the usm player and the swf movie are not ported, so the pane shows only a label with the
/// path), the skip button's timing and the confirm message box (0x88c) in front of the skip.
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

    /// <summary>vfunc_53: stops the movie.</summary>
    public void Stop() => Path = null;

    /// <summary>UICutSceneMovie::StopIfCutScene (@0x14190f0d0): the id has to be the stored one (it stays after a stop, as in the client).</summary>
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

    /// <summary>The field's Esc step for the movie (FUN_1419102c0 = it plays): skips it, taking the key.</summary>
    public bool SkipOnEsc()
    {
        if (!IsPlaying) return false;
        Exit();
        return true;
    }
}
