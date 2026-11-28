using Ms2.Net.Packets;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// The chaos damage meter, UIChaosDPSDialog (client/ms2/uichaosdpsdialog.cpp; UI id 0x4f23, vfunc_48 @0x140e09160), for its movie
/// uichaosdpsdialog (ChaosDPSDialog). It is the party meter's body over the record manager's chaos group (group 2): the same
/// seven modes, ranking, 100 ms refresh, reset, notice and member removal (ctor @0x140e05460, ranking FUN_140e05950, comparator
/// FUN_140e066f0 on group 2, vfunc_28 @0x140e06080), with the s_chaosdps_* strings. It differs in two places: OnCloseDialog first
/// clears the chaos meter's option flag, and the frame update (vfunc_72 @0x140e06630) closes it when the chaos meter is off,
/// when out of a party, or when feature 0xbe is not enabled.
/// </summary>
public sealed class ChaosDpsController : PartyDpsController
{
    public new const int UiId = 0x4f23;
    /// <summary>m_nGroup: the chaos group of the record manager.</summary>
    public new const int Group = 2;

    public override string Movie => "uichaosdpsdialog";
    /// <summary>GetTypeName (@0x140e05560).</summary>
    public override string TypeName => "ChaosDPSDialog";
    protected override int GroupId => Group;
    protected override string KeyPrefix => "s_chaosdps";

    /// <summary>IsChaosDamageMeterOn of the saved interface option (the switch and the allowed flag).</summary>
    public Func<bool> MeterOn = () => true;
    /// <summary>CFeatureManager::IsFeatureEnabled(0xbe).</summary>
    public Func<bool> FeatureEnabled = () => true;
    /// <summary>vfunc_28's OnCloseDialog first line: the chaos meter's option switch is cleared.</summary>
    public Action ClearMeterOption = () => { };

    public ChaosDpsController(PartyBook party, BattleRecordManager records) : base(party, records) => this.party = party;

    readonly PartyBook party;

    protected override void OnCloseDialogOption() => ClearMeterOption();
    protected override bool ShouldClose() => !MeterOn() || !party.IsInParty || !FeatureEnabled();
}
