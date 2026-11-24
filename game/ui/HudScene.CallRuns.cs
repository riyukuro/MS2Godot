using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>Scripted runs of the recall and the air taxi: the party window's recall button, a click on a recall notice and the world
/// map's air taxi request, as the movies send them.</summary>
public partial class HudScene
{
    /// <summary>Scripted runs: the party window opened (OpenDialog 0x4e45) and its recall button pressed (the movie's onPartyRecall).
    /// False when the window did not open.</summary>
    public bool PressPartyRecall()
    {
        OpenParty();
        if (party == null) return false;
        party.OnCallback("onPartyRecall", System.Array.Empty<object>());
        return true;
    }

    /// <summary>Scripted runs: the first recall notice slot that holds a time, or -1.</summary>
    public int RecallNoticeSlot()
    {
        if (recallNotify == null) return -1;
        for (int i = 0; i < RecallScrollNotifyController.Slots; i++)
            if (recallNotify.Time(i) > 0) return i;
        return -1;
    }

    /// <summary>Scripted runs: a click on the notice of <paramref name="slot"/> (the movie's OnNotify), which shows the confirm box.</summary>
    public bool ClickRecallNotice(int slot) => recallNotify != null && recallNotify.ClickNotice(slot);

    /// <summary>Scripted runs: the world map opened and its air taxi request for <paramref name="field"/> (the movie's onCashTaxi).
    /// False when the map did not open.</summary>
    public bool AirTaxiTo(int field)
    {
        OpenWorldMap();
        if (worldMapSide == null || worldMap == null) return false;
        worldMapSide.OnCallback("onCashTaxi", new object[] { (double)field });
        return true;
    }
}
