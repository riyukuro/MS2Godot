using Godot;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>OnTaxi's open branch (scenestategame.cpp:7902-7925) on the HUD: <see cref="Ms2.Ui.TaxiFoundOpen"/>.</summary>
public partial class HudScene
{
    Action unwatchTaxiFound;

    void WatchTaxiFound()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live == null || unwatchTaxiFound != null) return;
        var open = new Ms2.Ui.TaxiFoundOpen
        {
            CommonById = Ms2.Ui.Hud.MessageBoxData.Common,
            Notice = (text, type, log) =>
            {
                if (SystemText == null) return;
                SystemText.ShowMessage(text, type, 0);
                if (log) ChatLog.AddSystemLine(text, TalkKind.SystemNotice);
            },
            PlaySound = key => FieldAudio.Instance?.PlayUi(key),
            // the body reads the current field's house flag (FUN_1418fd2f0), not the id passed
            EnableTaxiFrame = _ => minimapSide?.EnableTaxiFrame(HomeOptionHost.FieldIsHome(assets.Packs, live.Portals?.FieldId ?? 0)),
            AddGuideTrigger = t => Ms2.Ui.Guide.GuideManager.Current?.AddTrigger(t, 0, 0),
        };
        Action<TaxiFound> on = open.OnTaxi;
        live.TaxiFound += on;
        unwatchTaxiFound = () => live.TaxiFound -= on;
    }
}
