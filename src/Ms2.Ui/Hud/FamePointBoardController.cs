using Ms2.Avm2;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIFamePointBoardDialog (uifamepointboarddialog.cpp, client 0x140c15460, UI id 0x4f54), the C++ side of
/// uifamepointboarddialog: one tab per alliance whose season quest is rewarded, and for the selected one its name, grade,
/// emblem, the fame gauge and the alliance's fame log. The game menu's entry 44 opens it (uigamemenudialog.cpp:2101).
/// </summary>
public sealed class FamePointBoardController : UiController
{
    public override string Movie => "uifamepointboarddialog";
    protected override string EntityMethod => "setData";
    /// <summary>GetTypeName (@0x140c15510).</summary>
    public override string TypeName => "FamePointBoardDialog";
    public const int UiId = 0x4f54;

    public FameTexts Fame = new();
    /// <summary>CQuestManager::IsQuestRewarded.</summary>
    public Func<long, bool> QuestRewarded = _ => false;
    /// <summary>GetServerTime (Unix seconds).</summary>
    public Func<long> ServerTime = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    /// <summary>The string table entry (CStringTable FUN_141811980(table, id)).</summary>
    public Func<int, long, string> TableString = (_, _) => "";
    /// <summary>CReservedWordTable::ReplaceObjectAndValueWords.</summary>
    public Func<string, string> Words = s => s;

    /// <summary>The fame log's string table (67, stringFameLog).</summary>
    public const int LogTable = 0x43;

    readonly List<int> tabs = new();
    /// <summary>The alliances of the tabs RebuildTabs added, in order (for the tests and the live run).</summary>
    public IReadOnlyList<int> Tabs => tabs;
    /// <summary>The alliance of the last SetAllianceData, 0 before one.</summary>
    public int Shown { get; private set; }

    /// <summary>OnLoadComplete (@0x140c15580): UIDialog's, then RebuildTabs.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        RebuildTabs();
    }

    /// <summary>vfunc_28 (@0x140c155b0): "onChangeTab" with an argument shows that alliance (SetAllianceData).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (!string.Equals(name, "onChangeTab", StringComparison.OrdinalIgnoreCase)) return false;
        if (args is { Length: > 0 }) SetAllianceData(Rt.ToInt32(args[0]));
        return true;
    }

    /// <summary>
    /// RebuildTabs (@0x140c15780): m_tab's data provider emptied (ClearDataProvider: cleanUp), then for each alliance of
    /// fame.xml whose season requireQuest is rewarded a row {label = the alliance name, alliance = its symbol, grade = the
    /// player's grade name, allianceCode} (AddItem: push); selectedIndex 0, which the movie's INDEX_CHANGE turns into
    /// onChangeTab; then vfunc_24(true): invalidate, validateNow.
    /// </summary>
    public void RebuildTabs()
    {
        tabs.Clear();
        if (Host?.Entity == null || Host.Child("m_tab") is not { } bar) return;
        var dp = Host.Get(bar, "dataProvider");
        if (!Rt.IsNullish(dp)) Host.Invoke(dp, "cleanUp");
        foreach (int a in Fame.Table.AllianceIds)
        {
            if (!QuestRewarded(Fame.Table.GetSeasonRequireQuest(a))) continue;
            tabs.Add(a);
            if (Rt.IsNullish(dp)) continue;
            Host.Invoke(dp, "push", new Dictionary<string, object>
            {
                ["label"] = Fame.AllianceName(a),
                ["alliance"] = Fame.AllianceSymbol(a),
                ["grade"] = Fame.GradeName(a),
                ["allianceCode"] = a,
            });
        }
        Host.Set(bar, "selectedIndex", 0);
        Host.Invoke(bar, "invalidate");
        Host.Invoke(bar, "validateNow");
    }

    /// <summary>
    /// SetAllianceData (@0x140c15a10): allianceName, allianceGrade, alliance (the symbol), currentFame, maxFame and
    /// famePoint (GetFamePointProgress), and log: each fame log entry of the alliance in time order as {msg = its
    /// stringFameLog text through ReplaceObjectAndValueWords, date = <see cref="DateText"/>}; then setData.
    /// </summary>
    public void SetAllianceData(int alliance)
    {
        Shown = alliance;
        if (Host?.Entity == null) return;
        Host.Invoke(Host.Entity, "setData", AllianceData(alliance));
    }

    /// <summary>The setData object of the alliance (see <see cref="SetAllianceData"/>).</summary>
    public Dictionary<string, object> AllianceData(int alliance)
    {
        var (cur, max, text) = Fame.Progress(alliance);
        long now = ServerTime();
        var log = new List<object>();
        foreach (var (time, id) in Fame.Book.Log)
        {
            if (!Fame.Table.IsLogOfAlliance(id, alliance)) continue;
            log.Add(new Dictionary<string, object>
            {
                ["msg"] = Words(TableString(LogTable, id) ?? "") ?? "",
                ["date"] = DateText(now, time, Fame.CommonByKey),
            });
        }
        return new Dictionary<string, object>
        {
            ["allianceName"] = Fame.AllianceName(alliance),
            ["allianceGrade"] = Fame.GradeName(alliance),
            ["alliance"] = Fame.AllianceSymbol(alliance),
            ["currentFame"] = cur,
            ["maxFame"] = max,
            ["famePoint"] = text,
            ["log"] = log,
        };
    }

    /// <summary>
    /// The log line's date (SetAllianceData): over 24 whole hours since the entry, s_fame_point_board_time3 with its
    /// year, month and day; over 60 whole minutes, s_fame_point_board_time2 with its hour and minute; else
    /// s_fame_point_board_time1 with the minutes. The entry's calendar time is the client's (CMsTime, MsTime.LocalTm).
    /// </summary>
    public static string DateText(long now, long time, Func<string, string> commonByKey)
    {
        long span = now - time;                     // ATL::CTimeSpan of the two CMsTime
        var t = Ms2.Net.MsTime.LocalTm(time);
        if (span / 3600 > 24)
            return ClientFormat.Format(commonByKey("s_fame_point_board_time3") ?? "", t.Year, t.Month, t.Day);
        if (span / 60 > 60)
            return ClientFormat.Format(commonByKey("s_fame_point_board_time2") ?? "", t.Hour, t.Minute);
        return ClientFormat.Format(commonByKey("s_fame_point_board_time1") ?? "", span / 60);
    }
}
