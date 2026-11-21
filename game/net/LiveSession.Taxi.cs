using System;
using Ms2.Formats.Model;
using Ms2.Ui;

namespace Ms2Godot.Net;

/// <summary>
/// TaxiStationAction (action key 8, <see cref="Ms2.Ui.TaxiStationAction"/>) over the live field: its poll runs in the
/// interact poll between gathering and the interact npc, and the key runs <see cref="TaxiKey"/>.
/// STILL-OPEN: Unk_PCMove::FUN_141683ef0 (the can-move check) is not ported, so it is taken as passing; the field's other
/// gates (the move cover, a field move) already stop the poll.
/// STILL-OPEN: the home privilege (FUN_14050da30) is unknown, so a home station never takes the key.
/// </summary>
public partial class LiveSession
{
    /// <summary>The taxi action, its requested list (DAT_143ab3098) and the field's stations.</summary>
    public readonly TaxiStationAction TaxiAction = new();
    /// <summary>TaxiStationAction::vfunc_2 (@0x14095bef0, actionkeyobject.cpp:811-821): the HUD opens the world map (0x4e42)
    /// when closed, calls SetTaxiMode(1) and adds guide trigger 0x11. Unset: the key does nothing.</summary>
    public Action TaxiKey;
    bool taxiPolled;
    Vec3 taxiAt;
    Func<bool> taxiPoll;

    /// <summary>vfunc_1 at the last polled point: the current field is DAT_143ab2870, the found list DAT_143ab3078.</summary>
    bool PollTaxi()
    {
        TaxiAction.Send ??= Send;
        bool hasPlayer = selfObject >= 0 && player != null && IsInstanceValid(player);
        return TaxiAction.Poll(hasPlayer, Me.Dead, true, InteractPlayerStateId(), taxiAt, null, Portals?.FieldId ?? 0,
                               Ms2Godot.Ui.StubHudData.Instance.FoundTaxis);
    }

    void UseTaxi()
    {
        if (selfObject < 0 || player == null || !IsInstanceValid(player)) return;   // vfunc_2: no player, return
        TaxiKey?.Invoke();
    }
}
