using Ms2.Net.Packets;

namespace Ms2.Ui.Hud;

/// <summary>
/// The cut scene cinematic pane's state (UICutSceneCinematic, ui id 0x4e52, uicutscenecinematic.cpp) as far as the Cinematic
/// packet drives it. KeyMode (OnPacketKeyMode @0x141909700) sets the cinematic key group: on is sub scene 0x6200041 (and ends an NPC
/// talk first, SendNpcTalkMode0), off is the field's 0x620001d (SetCinematicKeyMode @0x141908040). Start (StartCinematic
/// @0x141907f80) does nothing for id 0, else sets mode 1, the id and the text. Close (FUN_1419080b0) hides the script and
/// the direct pane.
/// Types 4 to 12 keep the pane's own lists as the client does: SetSkip (FUN_14190a4c0) sets the skip state to its kind and the
/// message, ResetSkip (@0x14190afb0) empties the talks and the message and sets the state 0, Talk (FUN_141909880) queues a line
/// unless its text is empty, ClearTalks empties the queue, BalloonTalk (FUN_141909eb0) queues a line in its object's group
/// (made on first use) unless its text is empty, ClearBalloonGroup (@0x14190a400) empties a group's lines, Opening and Intro
/// (@0x14190ac70, @0x14190aee0) set mode 2 or 3 and the text and turn the key group on when it is off.
/// STILL-OPEN: the pane's swf (the script and direct movies, the skip button and its state), what the key group does to the
/// player's input (the sub scene's own key table), the NPC talk end, the text's script translation (TranslateScriptText), the
/// trigger voice lookup that can lengthen a talk's duration, the balloon text-key lookup of a raw BalloonTalk, the balloon
/// shown flag and its removal, and the Caption packet (it only invokes the movie, so it changes nothing here).
/// </summary>
public sealed class CutSceneCinematic
{
    public bool KeyMode { get; private set; }
    public int Id { get; private set; }
    public string Text { get; private set; }
    /// <summary>Between a start and a close: the script and direct panes show (the client keeps the id and text after the close).</summary>
    public bool IsShowing { get; private set; }
    /// <summary>The client's m_nMode: 0 none, 1 cinematic, 2 opening, 3 intro.</summary>
    public int Mode { get; private set; }
    public bool IsOpening { get; private set; }
    /// <summary>The client's m_nSkipState: 0 no skip prompt, else the kind of the last SetSkip.</summary>
    public int SkipState { get; private set; }
    public string SkipMessage { get; private set; } = "";
    public List<CinematicPacket.Talk> Talks { get; } = new();
    /// <summary>The balloon lines of each object (the client's m_groups).</summary>
    public Dictionary<int, List<CinematicPacket.BalloonTalk>> BalloonGroups { get; } = new();

    public void Apply(CinematicPacket p)
    {
        switch (p)
        {
            case CinematicPacket.KeyMode k: KeyMode = k.On; break;
            case CinematicPacket.Start s:
                if (s.Id == 0) return;
                Mode = 1; Id = s.Id; Text = s.Text; IsShowing = true;
                break;
            case CinematicPacket.Close: IsShowing = false; break;
            case CinematicPacket.SetSkip k: SkipState = k.Kind; SkipMessage = k.Message; break;
            case CinematicPacket.ResetSkip: Talks.Clear(); SkipMessage = ""; SkipState = 0; break;
            case CinematicPacket.Talk t: if (t.Script.Length != 0) Talks.Add(t); break;
            case CinematicPacket.ClearTalks: Talks.Clear(); break;
            case CinematicPacket.BalloonTalk b:
                if (b.Text.Length == 0) break;
                if (!BalloonGroups.TryGetValue(b.ObjectId, out var group)) BalloonGroups[b.ObjectId] = group = new();
                group.Add(b);
                break;
            case CinematicPacket.ClearBalloonGroup g:
                if (BalloonGroups.TryGetValue(g.ObjectId, out var lines)) lines.Clear();
                break;
            case CinematicPacket.Opening o:
                Text = o.Text; IsOpening = o.IsOpening; Mode = 2;
                if (!KeyMode) KeyMode = true;
                break;
            case CinematicPacket.Intro i:
                Text = i.Text; Mode = 3;
                if (!KeyMode) KeyMode = true;
                break;
        }
    }
}
