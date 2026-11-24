using Godot;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The massive event pane (UIMassiveEvent, 0x4e4c, uimassiveevent.cpp): CUIService registers it at init (uiservice.cpp:2333)
/// and loads it with the game scene 0x620001d (uiservice.cpp:813), into the movie layer 0x1e (CommonDepth.MassiveEvent);
/// the MassiveEvent packet drives it (CSceneStateGame::OnMassiveEvent, @0x141722f70), its vfunc_15 runs the step countdown
/// each frame, and the game scene's field unload clears its stage (UISceneGamePlay::vfunc_56, uiscenegame.cpp:1153). It is
/// kept across relayouts.
/// </summary>
public partial class HudScene
{
    CanvasLayer massiveEventLayer;      // CommonDepth.MassiveEvent (30): over the dialogs and the cinematic, under the HUD's top layers
    MovieDialog massiveEventMovie;
    MassiveEventController massiveEvent;
    Action unwatchMassiveEvent;

    void MassiveEventPane()
    {
        if (massiveEvent != null) return;
        massiveEvent = new MassiveEventController
        {
            NowMs = () => (uint)Time.GetTicksMsec(),
            // PlaySystemSound (table/systemsound.xml through the audio bank); the print is the no-bank fallback
            SystemSound = key => { if (FieldAudio.Instance?.PlayUi(key) != true) NotApplied.Print($"massive event: system sound {key} is not played"); },
            ChatLine = (text, kind) => ChatLog.AddSystemLine(text, kind),
            Text = t => Notices?.Text(t) ?? t.Text ?? "",
            Unapplied = m => NotApplied.Print($"live: MassiveEvent kind {m.Kind} read, not applied (the event introduce dialog 0x4f06 is not ported)"),
            ClientSize = () =>
            {
                var size = (Vector2)ScreenMode.PixelSize(GetWindow());
                return ((int)size.X, (int)size.Y, UiScale.ViewportFactor(size.X));
            },
        };
        // over the HUD (10) and the dialogs (11), as layer 30 is over the dialog layer 25; it shares 12 with the cut scene movie
        // (33) and the message box (34), which are added after it, so they draw over it
        massiveEventLayer = new CanvasLayer { Layer = 12, Offset = UiScale.MovieOrigin };
        MovieLayerHost.Bind(massiveEventLayer, Ms2.Ui.CommonDepth.MassiveEvent);
        AddChild(massiveEventLayer);
        massiveEventMovie = new MovieDialog(assets, massiveEvent, hudModel, ScreenMode.PixelSize(GetWindow()));
        massiveEventLayer.AddChild(massiveEventMovie);
        if (Net.LiveSession.Instance is { } live)
        {
            Action<Ms2.Net.Packets.MassiveEventMessage> on = massiveEvent.Apply;
            live.MassiveEventReceived += on;
            unwatchMassiveEvent = () => live.MassiveEventReceived -= on;
        }
    }

    /// <summary>UIMassiveEvent::vfunc_15 (@0x1419f5a30): the step countdown.</summary>
    void MassiveEventTick() => massiveEvent?.Tick();

    void MassiveEventUnwatch()
    {
        unwatchMassiveEvent?.Invoke();
        unwatchMassiveEvent = null;
    }
}
