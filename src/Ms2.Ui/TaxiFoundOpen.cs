using Ms2.Net.Packets;
using Ms2.Ui.Guide;

namespace Ms2.Ui;

/// <summary>
/// The open branch of CSceneStateGame::OnTaxi (@0x141731470, scenestategame.cpp:7902-7925), for a Taxi packet with ids and
/// bOpen set (the ids joining the found list is the session's): the centre notice of common string 0x7c8 (FUN_141275260(0x7c8,
/// 3, 1)), the system sound "system_Taxi_Find" (DAT_143ae79e8), the minimap's EnableTaxiFrame with the first id, the field's
/// stations (FUN_141739450(true): TaxiStations' own subscription) and the guide trigger 0x10 (TaxiFind).
/// OnTaxi's last call, CAutoFindTaxiStation::FUN_1409f1970 (the auto-find camera's fly back), is the session's
/// (<see cref="AutoFindTaxiStation.OnTaxiFound"/>).
/// </summary>
public sealed class TaxiFoundOpen
{
    public const int NoticeId = 0x7c8;
    public const string Sound = "system_Taxi_Find";

    /// <summary>GetStringCommon(id).</summary>
    public Func<int, string> CommonById = _ => "";
    /// <summary>ShowSystemNotice(text, type, log).</summary>
    public Action<string, int, bool> Notice;
    /// <summary>PlaySystemSound(name, "").</summary>
    public Action<string> PlaySound;
    /// <summary>FindMinimapDialog(0x4e29)->EnableTaxiFrame(first) when the minimap is there.</summary>
    public Action<int> EnableTaxiFrame;
    /// <summary>CGuideManager::AddTrigger(type, 0, 0).</summary>
    public Action<GuideTrigger> AddGuideTrigger;

    public void OnTaxi(TaxiFound t)
    {
        if (t.Fields.Length == 0) return;                // the client returns on an empty list
        if (!t.Open) return;
        string text = CommonById(NoticeId) ?? "";
        if (text.Length != 0) Notice?.Invoke(text, 3, true);     // FUN_141275260: nothing for an empty string
        PlaySound?.Invoke(Sound);
        EnableTaxiFrame?.Invoke(t.Fields[0]);
        AddGuideTrigger?.Invoke(GuideTrigger.TaxiFind);
    }
}
