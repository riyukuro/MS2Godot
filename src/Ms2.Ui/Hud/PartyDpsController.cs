using System.Globalization;
using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// The party's damage meter, UIPartyDPSDialog (client/ms2/uipartydialog.cpp:1191-1414; UI id 0x4e8c), for its movie
/// uipartydpsdialog (PartyDPSDialog). It ranks the party's members by the meter value of the battle record manager's party
/// group (group 1) in one of seven modes (damage, boss damage, the two per second, hit rate, critical rate, damage taken),
/// refreshing every 100 ms. The ctor (@0x141a7b050) sets m_2c 0x11, group 1, mode 0 and flag 0; the dtor is empty;
/// vfunc_67 (@0x141a7bbf0) is UIDialog's close.
/// </summary>
public class PartyDpsController : UiController
{
    public const int UiId = 0x4e8c;                     // vfunc_48 (@0x141a7c4b0)
    /// <summary>m_nGroup: the party group of the record manager.</summary>
    public const int Group = 1;
    /// <summary>The rows the movie lists (the top rank and nine more).</summary>
    public const int ShownRows = 10;

    public override string Movie => "uipartydpsdialog";

    /// <summary>The CommonDepth layer of the movie: UIPartyDPSDialog's ctor sets m_2c to 0x11 (uipartydialog.cpp:1200).</summary>
    public override int LoadLayer => CommonDepth.Party;
    protected override string EntityMethod => "setAscendingMeterValue";
    /// <summary>GetTypeName (@0x141a7b150).</summary>
    public override string TypeName => "PartyDPSDialog";
    /// <summary>UIPartyDPSDialog's ctor (@0x141a7b050, uipartydialog.cpp:1198) has no SetTweenEnabled: m_c4 stays 0, so CloseDialog
    /// unloads at once (FUN_141924330, uidialog.cpp:393) and a following OpenDialog loads it again.</summary>
    protected override bool TweenEnabled => false;

    readonly PartyBook party;
    readonly BattleRecordManager records;
    /// <summary>m_members: the last ranking (RebuildRanking), the notice's rows.</summary>
    readonly List<PartyMember> ranking = new();
    ulong? lastTick;                                    // m_tick
    /// <summary>The close has started (the close tween may still run): the frame update asks for it once.</summary>
    public bool Closing { get; private set; }

    /// <summary>m_nMode: the meter shown, 0..6 (the record manager's value text types).</summary>
    public int Mode { get; private set; }
    /// <summary>The last ranking, for checks.</summary>
    public IReadOnlyList<PartyMember> Ranking => ranking;

    /// <summary>The local player's character id (PCBase::FUN_141656a80), 0 without a local player.</summary>
    public Func<long> SelfCharacterId = () => 0;
    /// <summary>CMsTime(GetServerTime()): the server time as the client's local clock shows it.</summary>
    public Func<DateTime> ServerNow = () => DateTime.Now;
    /// <summary>FUN_141276f80 with a CommandChat of kind 7 (the party chat).</summary>
    public Action<string> PartyChat = _ => { };

    public PartyDpsController(PartyBook party, BattleRecordManager records)
    {
        this.party = party;
        this.records = records;
    }

    /// <summary>The record manager's group the meter reads: the party group (1); the chaos meter's is 2.</summary>
    protected virtual int GroupId => Group;
    /// <summary>The common string key prefix of the meter's titles and notice lines.</summary>
    protected virtual string KeyPrefix => "s_partydps";
    /// <summary>vfunc_28's OnCloseDialog before the group stops (the chaos meter clears its option flag here).</summary>
    protected virtual void OnCloseDialogOption() { }
    /// <summary>vfunc_72's close condition: the party meter closes out of a party.</summary>
    protected virtual bool ShouldClose() => !party.IsInParty;

    string CommonByKey(string key) => Model.Tables.Common(key) ?? "";
    string CommonKeyF(string key, params object[] args) => ClientFormat.Format(CommonByKey(key), args);

    /// <summary>FUN_140a04bc0: the mode's name, a common string.</summary>
    string ModeName(int mode) => BattleRecordManager.ModeKey(mode) is { Length: > 0 } key ? CommonByKey(key) : "";

    /// <summary>ParseDoubleW of GetRecordValueText: the meter value, 0 without a record.</summary>
    double MeterValue(long characterId, int mode) => NumberText.ParseDoubleW(records.RecordValueText(GroupId, characterId, mode));

    /// <summary>
    /// FUN_140a04830 (battlerecordmanager.cpp:296): the meter text, empty without a record. The rates (4, 5) are
    /// FormatStringId(0x98e, "{0:0.2f}" of ParseFloatW), the built-in string whose text is the number with a % sign; the
    /// others "{0}" of FUN_14083cb20, the digits grouped.
    /// </summary>
    public static string MeterText(BattleRecordManager records, int group, long characterId, int mode)
    {
        if (!records.HasRecord(group, characterId)) return "";
        string value = records.RecordValueText(group, characterId, mode);
        return mode switch
        {
            0 or 1 or 2 or 3 or 6 or 7 or 8 => NumberText.Group(value),
            4 or 5 => ClientFormat.Format("{0:0.2f}", (float)NumberText.ParseDoubleW(value)) + "%",
            _ => "",
        };
    }

    /// <summary>
    /// OnLoadComplete (@0x141a7bc10): UIDialog's, the 100 ms timer armed, the party group started unless it runs, then the
    /// title (<see cref="OnTextToggle"/>) and the ranking.
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        lastTick = null;
        Closing = false;
        records.StartGroupIfInactive(GroupId);
        OnTextToggle();
        RebuildRanking();
    }

    /// <summary>vfunc_28 (@0x141a7bc70): OnCloseDialog stops the party group and closes, before UIDialog's.</summary>
    public override bool OnCallbackBeforeDialog(string name, object[] args)
    {
        if (!string.Equals(name, "OnCloseDialog", StringComparison.OrdinalIgnoreCase)) return false;
        OnCloseDialogOption();
        records.StopGroup(GroupId);
        Closing = true;
        Close();
        return true;
    }

    /// <summary>vfunc_67, UIDialog's close, the way ToggleDialog (uiservice.cpp:1628, FUN_1419246f0) and Escape
    /// (uidialog.cpp:840, vfunc_13 to vfunc_69) close the meter: the party group keeps running, with its tallies and timer.
    /// Only the movie's own close button sends OnCloseDialog, which stops the group.</summary>
    public override void EscapeClose() => CloseKeepingTheGroup();

    /// <summary>The close that keeps the group running.</summary>
    public void CloseKeepingTheGroup()
    {
        if (IsClosed || Closing) return;
        Closing = true;
        Close();
    }

    /// <summary>vfunc_28 after UIDialog's: onReset restarts the group, onNotice posts the ranking to the party chat,
    /// onTextToggle goes to the next mode (0..6).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (string.Equals(name, "onReset", StringComparison.OrdinalIgnoreCase)) { records.StartGroup(GroupId); return true; }
        if (string.Equals(name, "onNotice", StringComparison.OrdinalIgnoreCase)) { PartyChat(NoticeText()); return true; }
        if (string.Equals(name, "onTextToggle", StringComparison.OrdinalIgnoreCase))
        {
            Mode++;
            if (Mode >= 7) Mode = 0;
            OnTextToggle();
            return true;
        }
        return false;
    }

    /// <summary>
    /// The onNotice text: s_partydps_notice_history_prefix of the server time's hour, minute, second and the mode's name,
    /// a s_partydps_notice_history_row (rank, name, meter text) for each of the last ranking's first ten, then
    /// s_partydps_notice_history_total_time of the group's elapsed time (FUN_141b70810).
    /// </summary>
    public string NoticeText()
    {
        var now = ServerNow();
        var text = new System.Text.StringBuilder(CommonKeyF(KeyPrefix + "_notice_history_prefix", now.Hour, now.Minute, now.Second, ModeName(Mode)));
        string row = CommonByKey(KeyPrefix + "_notice_history_row");
        for (int i = 0; i < ranking.Count && i < ShownRows; i++)
        {
            var m = ranking[i];
            text.Append(ClientFormat.Format(row, i + 1, m.User.Name, MeterText(records, GroupId, m.CharacterId, Mode)));
        }
        text.Append(CommonKeyF(KeyPrefix + "_notice_history_total_time", records.ElapsedTimeText(GroupId)));
        return text.ToString();
    }

    /// <summary>OnTextToggle (@0x141a7b3c0): the title s_partydps_titlebar of the mode's name; mode 6 (damage taken) ranks
    /// ascending.</summary>
    void OnTextToggle()
    {
        if (Host?.Entity == null) return;
        Host.Invoke("setTitle", (object)CommonKeyF(KeyPrefix + "_titlebar", ModeName(Mode)));
        Host.Invoke("setAscendingMeterValue", Mode == 6);
    }

    /// <summary>FUN_141a7c2a0: the ranking's order, the larger meter value first (the smaller for mode 6). The client's
    /// std::sort leaves ties in no set order; here they keep the party's order.</summary>
    void Sort()
    {
        values.Clear();
        foreach (var m in ranking) values.Add(MeterValue(m.CharacterId, Mode));
        bool ascending = Mode == 6;
        for (int i = 1; i < ranking.Count; i++)          // insertion sort: stable, no allocation, ten rows at most
        {
            var m = ranking[i]; double v = values[i];
            int j = i - 1;
            for (; j >= 0 && (ascending ? values[j] > v : values[j] < v); j--) { ranking[j + 1] = ranking[j]; values[j + 1] = values[j]; }
            ranking[j + 1] = m; values[j + 1] = v;
        }
    }
    readonly List<double> values = new();

    /// <summary>FillMeterRow (@0x141a7b1a0): the row object, rank, level, job, name, meter value and text.</summary>
    AsObject Row(PartyMember m, int rank) => Host.Obj(
        ("rank", (uint)rank), ("level", (int)m.User.Level), ("job", m.User.Job), ("name", m.User.Name ?? ""),
        ("meterValue", MeterValue(m.CharacterId, Mode)), ("meterText", MeterText(records, GroupId, m.CharacterId, Mode)));

    /// <summary>
    /// RebuildRanking (@0x141a7b540): setTime of the elapsed time; with members, the ranking rebuilt and sorted, the last's
    /// value as setBottomMeterValue, UpdateMember for the first ten, and SetMyRank for the local player's row at any rank.
    /// </summary>
    public void RebuildRanking()
    {
        if (Host?.Entity == null) return;
        Host.Invoke("setTime", (object)records.ElapsedTimeText(GroupId));
        if (party.Members.Count == 0) return;
        ranking.Clear();
        foreach (var m in party.Members) if (m != null) ranking.Add(m);
        if (ranking.Count == 0) return;
        Sort();
        Host.Invoke("setBottomMeterValue", MeterValue(ranking[^1].CharacterId, Mode));
        long self = SelfCharacterId();
        for (int i = 0; i < ranking.Count; i++)
        {
            var m = ranking[i];
            if (i < ShownRows) Host.Invoke("UpdateMember", Row(m, i + 1));
            if (self != 0 && m.CharacterId == self) Host.Invoke("SetMyRank", Row(m, i + 1));
        }
    }

    /// <summary>RemoveMemberIfUnderLimit (@0x141a7bb70): the party's RemoveMemberAt; under ten members the movie drops its
    /// last row.</summary>
    public void RemoveMemberIfUnderLimit()
    {
        if (Host?.Entity == null) return;
        if (party.Members.Count < ShownRows) Host.Invoke("RemoveMember");
    }

    /// <summary>vfunc_72 (@0x141a7c210), each frame: out of a party it closes; else every 100 ms the ranking.</summary>
    public void Update(ulong nowMs)
    {
        if (IsClosed || Closing) return;
        if (ShouldClose()) { Closing = true; Close(); return; }
        if (Host?.Entity == null) return;
        lastTick ??= nowMs;
        if (nowMs - lastTick.Value < 100) return;
        lastTick = nowMs;
        RebuildRanking();
    }
}
