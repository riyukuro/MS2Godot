using Godot;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// UICutSceneCinematic (0x4e52, movie uicutscenecinematic, uicutscenecinematic.cpp): the pane of the Cinematic packet (0x68). The
/// model (<see cref="CutSceneCinematic"/>, the session's) holds the state and timed lists; the movie runs on the generated route
/// (<see cref="CutSceneCinematicController"/>), made on the first Cinematic packet. The environment is the HUD's: the trigger
/// strings, the npc names, the voice, the skip box, the sends, and the field's chat balloons for the balloon talks.
/// STILL-OPEN: the voice length (FindTriggerVoice / GetSoundLength), so a talk lasts its packet duration; the raw BalloonTalk text key
/// lookup; the key group's effect on the sub scene's input.
/// </summary>
public partial class HudScene
{
    CutSceneCinematicController cinematicPane;
    MovieDialog cinematicMovie;
    Ms2Godot.Net.LiveSession cinematicLive;     // the session whose model is wired

    /// <summary>Each frame: wire the session's model once, make the pane on its first packet, run its timers.</summary>
    void CinematicTick()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live != cinematicLive)
        {
            CinematicUnwire();
            if (live != null && assets != null && hudModel != null) { cinematicLive = live; CinematicWire(live, live.Cinematic); }
        }
        var cine = cinematicLive?.Cinematic;
        if (cine == null) return;
        if (cinematicPane == null && cine.Packets > 0 && uiRoot != null) OpenCinematicPane(cine);
        if (cine.Packets > 0) cine.Update();
    }

    void OpenCinematicPane(CutSceneCinematic cine)
    {
        var c = new CutSceneCinematicController(cine);
        var size = ScreenMode.PixelSize(GetWindow());
        c.ClientWidth = (int)size.X; c.ClientHeight = (int)size.Y;
        c.MovieScale = UiScale.ViewportFactor(size.X);   // the movie's stage is the window times this (UiScale), as the talk pane passes it
        c.Unloaded += () =>
        {
            cinematicMovie?.Host.Unload(); cinematicMovie?.QueueFree(); cinematicMovie = null;
            if (cinematicPane == c) { cinematicPane = null; if (cine.Pane == c) cine.Pane = null; }
        };
        cinematicPane = c;
        cinematicMovie = new MovieDialog(assets, c, hudModel, size) { Visible = cine.KeyMode };
        uiRoot.AddChild(cinematicMovie);
    }

    /// <summary>The pane is a dialog of the cinematic sub scene 0x6200041: entering it shows the dialog (UISceneGameCinematicCutScene
    /// vfunc_48 -> CUIService::FUN_141ada6f0, uiservice.cpp:846), leaving it hides it (vfunc_49 -> FUN_141adad20, :904). The key
    /// group's change (SetCinematicKeyMode, the KeyMode packet, an opening or intro, a field's unload) is that sub scene change.</summary>
    void CinematicSubScene(bool on)
    {
        if (cinematicMovie != null) cinematicMovie.Visible = on;
    }

    void CinematicWire(Ms2Godot.Net.LiveSession live, CutSceneCinematic cine)
    {
        var t = hudModel.Tables;
        cine.Translate = s =>
        {
            if (string.IsNullOrEmpty(s)) return "";
            var text = s.Contains('$') ? t.TriggerStrings.GetString(s) ?? s : s;
            text = text.Replace("\\n", "\n");
            return t.ReservedWords.ReplaceValueWords(t.ReservedWords.ReplaceObjectWords(text), hudModel.HasLocalPlayer ? hudModel.Player.Name : null);
        };
        cine.Clock = () => (long)Time.GetTicksMsec();
        cine.PlayVoice = tag => Ms2Godot.FieldAudio.Instance?.PlayVoiceTag(tag);
        cine.NpcName = id => t.NpcName(id);
        cine.PlayerName = () => hudModel.HasLocalPlayer ? hudModel.Player.Name : null;
        cine.SendTrigger = live.SendCinematicSkipTrigger;
        cine.ShowSkipBox = (key, answer) =>
            MessageBoxQueue.ShowWithCallback(t.Common(key) ?? "", 1, (button, _) => { answer(button); return true; });
        cine.BalloonActorReady = id => Ms2Godot.World.NpcEmoticons.Live?.ObjectBalloonReady(id) == true;
        cine.ShowBalloon = (id, text) => Ms2Godot.World.NpcEmoticons.Live?.ShowObjectBalloon(id, text);
        cine.HideBalloon = id => Ms2Godot.World.NpcEmoticons.Live?.HideObjectBalloon(id);
        cine.KeyModeChanged += CinematicSubScene;
    }

    void CinematicUnwire()
    {
        if (cinematicLive?.Cinematic is { } wired) wired.KeyModeChanged -= CinematicSubScene;
        if (cinematicMovie != null) { cinematicMovie.Host.Unload(); cinematicMovie.QueueFree(); cinematicMovie = null; }
        cinematicPane = null;
        cinematicLive = null;
    }

    /// <summary>The pane's key handler (UICutSceneCinematic vfunc_13): Shift+Esc ends the cinematic (the trigger, the key group off,
    /// the pane closed), plain Esc asks to skip (RequestSkip). True when the pane took the key; false when no cinematic is up.</summary>
    bool CinematicEscape(bool shift)
    {
        var cine = cinematicLive?.Cinematic;
        if (cine == null || !(cine.KeyMode || cine.Mode != 0)) return false;
        if (shift) cine.ForceEnd(); else cine.RequestSkip();
        return true;
    }
}
