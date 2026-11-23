using Ms2.Net.Packets;

namespace Ms2.Ui.Hud;

/// <summary>
/// The cut scene cinematic pane's state (UICutSceneCinematic, ui id 0x4e52, uicutscenecinematic.cpp) as far as the Cinematic
/// packet drives it. KeyMode (OnPacketKeyMode @0x141909700) sets the cinematic key group: on is sub scene 0x6200041 (and ends an NPC
/// talk first, SendNpcTalkMode0), off is the field's 0x620001d (SetCinematicKeyMode @0x141908040). Start (StartCinematic
/// @0x141907f80) does nothing for id 0, else sets mode 1, the id and the text. Close (FUN_1419080b0) hides the script and
/// the direct pane.
/// STILL-OPEN: the pane's swf (the script and direct movies, the skip button and its state), what the key group does to the
/// player's input (the sub scene's own key table), the NPC talk end, the text's script translation (TranslateScriptText),
/// and types 4 to 12.
/// </summary>
public sealed class CutSceneCinematic
{
    public bool KeyMode { get; private set; }
    public int Id { get; private set; }
    public string Text { get; private set; }
    /// <summary>Between a start and a close: the script and direct panes show (the client keeps the id and text after the close).</summary>
    public bool IsShowing { get; private set; }

    public void Apply(CinematicPacket p)
    {
        switch (p)
        {
            case CinematicPacket.KeyMode k: KeyMode = k.On; break;
            case CinematicPacket.Start s:
                if (s.Id == 0) return;
                Id = s.Id; Text = s.Text; IsShowing = true;
                break;
            case CinematicPacket.Close: IsShowing = false; break;
        }
    }
}
