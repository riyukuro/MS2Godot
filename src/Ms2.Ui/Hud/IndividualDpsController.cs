using Ms2.Net.Packets;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// The personal damage meter, UIIndividualDPSDialog (client/ms2/uiindividualdpsdialog.cpp; UI id 0x4edf, vfunc_48 @0x140f4b2a0),
/// for its movie uiindividualdpsdialog (IndividualDPSDialog). It shows the local player's own value of the battle record
/// manager's individual group (group 0) in one of nine modes (the party meter's seven, then exp and meso), with the group's
/// elapsed time, refreshed every 100 ms. The ctor (@0x140f4ab90) sets m_2c 0x11, group 0, mode 0 and SetFlag(0, 1) (always
/// on) and has no SetTweenEnabled; the dtor is empty. It opens from the options' Apply and the game scene's open list while
/// the individual damage meter is on (IsIndividualDamageMeterOn, gameoptionstruct.cpp:1268: the option switch and the allowed
/// flag), and closes itself the frame that stops holding.
/// </summary>
public sealed class IndividualDpsController : UiController
{
    public const int UiId = 0x4edf;
    /// <summary>m_nGroup: the individual group of the record manager.</summary>
    public const int Group = 0;
    /// <summary>The modes: the record manager's value text types 0..8.</summary>
    public const int ModeCount = 9;

    public override string Movie => "uiindividualdpsdialog";
    /// <summary>The CommonDepth layer of the movie: UIIndividualDPSDialog's ctor (@0x140f4ab90) sets m_2c to 0x11, as the party meter.</summary>
    public override int LoadLayer => CommonDepth.Party;
    protected override string EntityMethod => "setMeterText";
    /// <summary>GetTypeName (@0x140f4ac70).</summary>
    public override string TypeName => "IndividualDPSDialog";
    protected override bool TweenEnabled => false;
    public override bool AlwaysOn => true;

    readonly BattleRecordManager records;
    ulong? lastTick;                                    // m_tick (Unk_IntervalTimer)
    /// <summary>The close has started: the frame update asks for it once.</summary>
    public bool Closing { get; private set; }

    /// <summary>m_nMode: the meter shown, 0..8.</summary>
    public int Mode { get; private set; }
    /// <summary>The last meter text and time written into the movie, for checks; null before the first write.</summary>
    public string MeterText { get; private set; }
    public string TimeText { get; private set; }
    public string Title { get; private set; }

    /// <summary>The local player's character id (PCBase::FUN_141656a80), 0 without a local player.</summary>
    public Func<long> SelfCharacterId = () => 0;
    /// <summary>IsIndividualDamageMeterOn of the saved interface option (the switch and the allowed flag).</summary>
    public Func<bool> MeterOn = () => true;
    /// <summary>vfunc_28's OnCloseDialog first line: the option's switch (+3) is cleared.</summary>
    public Action ClearMeterOption = () => { };

    public IndividualDpsController(BattleRecordManager records) => this.records = records;

    string ModeName(int mode) => BattleRecordManager.ModeKey(mode) is { Length: > 0 } key ? Model.Tables.Common(key) ?? "" : "";

    /// <summary>
    /// OnLoadComplete (@0x140f4afc0): UIDialog's, the 100 ms timer armed, then with a local player the individual group started
    /// unless it runs, the player's record added, the title and the meter text.
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        lastTick = null;
        Closing = false;
        long self = SelfCharacterId();
        if (self == 0) return;
        records.StartGroupIfInactive(Group);
        records.AddRecord(Group, self);
        OnTextToggle();
        UpdateMeterText();
    }

    /// <summary>vfunc_28 (@0x140f4b070): OnCloseDialog first (before UIDialog's) clears the option's switch, stops the group and
    /// closes.</summary>
    public override bool OnCallbackBeforeDialog(string name, object[] args)
    {
        if (!string.Equals(name, "OnCloseDialog", StringComparison.OrdinalIgnoreCase)) return false;
        ClearMeterOption();
        records.StopGroup(Group);
        CloseOnce();
        return true;
    }

    /// <summary>vfunc_28 after UIDialog's: onReset restarts the group, onTextToggle goes to the next mode (0..8).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (string.Equals(name, "onReset", StringComparison.OrdinalIgnoreCase)) { records.StartGroup(Group); return true; }
        if (string.Equals(name, "onTextToggle", StringComparison.OrdinalIgnoreCase))
        {
            Mode++;
            if (Mode >= ModeCount) Mode = 0;
            OnTextToggle();
            return true;
        }
        return false;
    }

    /// <summary>OnTextToggle (@0x140f4acc0): setTitle with s_individualdps_titlebar of the mode's name.</summary>
    void OnTextToggle()
    {
        if (Host?.Entity == null) return;
        Title = ClientFormat.Format(Model.Tables.Common("s_individualdps_titlebar") ?? "", ModeName(Mode));
        Host.Invoke("setTitle", (object)Title);
    }

    /// <summary>UpdateMeterText (@0x140f4ade0): with a local player, setMeterText of the record manager's meter text for the
    /// player (empty without a record) and setTime of the group's elapsed time.</summary>
    public void UpdateMeterText()
    {
        if (Host?.Entity == null) return;
        long self = SelfCharacterId();
        if (self == 0) return;
        MeterText = PartyDpsController.MeterText(records, Group, self, Mode);
        Host.Invoke("setMeterText", (object)MeterText);
        TimeText = records.ElapsedTimeText(Group);
        Host.Invoke("setTime", (object)TimeText);
    }

    /// <summary>CloseDialog(0x4edf) from the options' Apply: vfunc_67, which keeps the group running.</summary>
    public void CloseFromOptions() => CloseOnce();

    void CloseOnce()
    {
        if (IsClosed || Closing) return;
        Closing = true;
        Close();
    }

    /// <summary>vfunc_72 (@0x140f4b1b0), each frame: UIDialog's, then with the meter off it closes (vfunc_67); else every 100 ms
    /// with a movie and a local player the meter text.</summary>
    public void Update(ulong nowMs)
    {
        if (IsClosed || Closing) return;
        if (!MeterOn()) { CloseOnce(); return; }
        if (Host?.Entity == null) return;
        lastTick ??= nowMs;
        if (nowMs - lastTick.Value < 100) return;
        lastTick = nowMs;
        if (SelfCharacterId() == 0) return;
        UpdateMeterText();
    }
}
