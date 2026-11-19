using Ms2.Quest;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIEventWidgetDialog (ms2/client/ms2/uileveluppackagewidgetdialog.cpp), the C++ side of uieventwidgetdialog: an event's
/// progress widget, the movie's EventWidgetDialog filled by setData(title, binding, current, max, text). Each subclass is
/// one event; at field entry the client opens every widget whose event is on (<see cref="ShouldOpen"/>, FUN_140c59350,
/// uiservice.cpp:777).
/// </summary>
public abstract class EventWidgetController : UiController
{
    public override string Movie => "uieventwidgetdialog";     // vfunc_7 "EventWidgetDialog"

    /// <summary>UIEventWidgetDialog's ctor sets the flag 0 (uileveluppackagewidgetdialog.cpp:159): vfunc_56, an
    /// always-on one.</summary>
    public override bool AlwaysOn => true;
    protected override string EntityMethod => "setData";
    public override string TypeName => "EventWidgetDialog";   // GetTypeName (@0x140c58e60)

    /// <summary>No SetTweenEnabled in the ctors (@0x140c58d30, @0x140c593d0, @0x140c59f10): m_c4 stays 0, so the widget
    /// opens without startOpen and closes at once.</summary>
    protected override bool TweenEnabled => false;

    /// <summary>m_178: the reward item's slot binder, null on the base widget.</summary>
    protected ItemSlotBinder Binder;

    /// <summary>onClickReward (vfunc_86) and onOpenUI (vfunc_87): what the subclass sends or opens.</summary>
    public event Action ClickReward, OpenUi;

    /// <summary>vfunc_84: the event is on, so the widget opens at field entry.</summary>
    public abstract bool ShouldOpen();

    /// <summary>vfunc_85 (@0x140c592f0): the binder cleared; a subclass then fills the data, false = no data.</summary>
    protected virtual bool BuildData(Dictionary<string, object> data)
    {
        Binder?.Clear();
        return true;
    }

    /// <summary>The data object vfunc_85 builds, null when it gives 0 (a test hook; the binder is refilled).</summary>
    public Dictionary<string, object> Data()
    {
        var d = new Dictionary<string, object>();
        return BuildData(d) ? d : null;
    }

    /// <summary>OnLoadComplete (@0x140c58f90): UIDialog's, then UpdateData.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        UpdateData();
    }

    /// <summary>UpdateData (@0x140c59210): no data closes the widget, else setData(data).</summary>
    public void UpdateData()
    {
        if (Host?.Entity == null) return;
        var data = new Dictionary<string, object>();
        if (!BuildData(data)) { Close(); return; }
        Host.Invoke("setData", Host.ToAs(data));
    }

    /// <summary>vfunc_67 (@0x140c58f30): the binder cleared, then UIDialog's.</summary>
    public override void Close()
    {
        Binder?.Clear();
        base.Close();
    }

    /// <summary>vfunc_28 (@0x140c58fc0), after UIDialog's: onClickReward and onOpenUI.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (name.Equals("onClickReward", StringComparison.OrdinalIgnoreCase)) { ClickReward?.Invoke(); return true; }
        if (name.Equals("onOpenUI", StringComparison.OrdinalIgnoreCase)) { OpenUi?.Invoke(); return true; }
        return false;
    }

    /// <summary>
    /// vfunc_43 (@0x140c59070): UIDialog's test (placed from its file, not re-aligned, a drag bar, top left on the stage)
    /// keeps the place; otherwise ASDisplayObject::vfunc_2(5, 0, 0x3c).
    /// </summary>
    public override void Layout(double stageW, double stageH)
    {
        if (KeepsPlace(stageW, stageH)) return;
        Host.Align(Host.Entity, 5, 0, 0x3c);
    }
}

/// <summary>
/// UILevelUpPackageWidgetDialog (UI id 0x4f5e): the level-up package, the type 3 quests with guide type level (1), each
/// rewarded at the level of its first condition's target.
/// </summary>
public sealed class LevelUpPackageWidgetController : EventWidgetController
{
    public override string TypeName => "LevelUpPackageWidget";     // GetTypeName (@0x140c59590)

    public QuestTable Table;
    /// <summary>CQuestManager's records (IsQuestRewarded).</summary>
    public IQuestState State;

    readonly UiModel model;
    int questId;       // m_198

    /// <summary>The ctor (@0x140c593d0): the binder "LevelUpPackageWidget". The widget reads the player from the model it
    /// opens with, also before it is loaded (vfunc_84).</summary>
    public LevelUpPackageWidgetController(UiModel model)
    {
        this.model = model;
        Binder = new ItemSlotBinder(model, "LevelUpPackageWidget");
    }

    /// <summary>The package quest of the last <see cref="BuildData"/> (m_198); onClickReward sends Quest 0x18 with it.</summary>
    public int QuestId => questId;

    /// <summary>
    /// FUN_140c59bd0 (@0x140c59bd0): the quests by level (a later quest at the same level replaces the earlier); in level
    /// order, the first one reached and not rewarded, or the first one not reached. Its level is the range's high end and
    /// the level before it the low end (0 for the first). 0 without a local player or such a quest.
    /// </summary>
    public int FindQuest(out int low, out int high)
    {
        low = high = 0;
        if (!model.HasLocalPlayer || Table == null) return 0;
        int level = (ushort)model.Player.Level;
        var byLevel = new SortedDictionary<ushort, QuestData>();
        foreach (var q in Table.CollectType3QuestsByGuideType(1))
            byLevel[(ushort)q.Conditions[0].Target.FirstValue] = q;
        int prev = 0;
        foreach (var (key, q) in byLevel)
        {
            if (level < key || State?.Get((int)q.Id) is not { IsRewarded: true })
            {
                high = key;
                low = prev;
                return (int)q.Id;
            }
            prev = key;
        }
        return 0;
    }

    /// <summary>vfunc_84 (@0x140c597f0): a package quest is found.</summary>
    public override bool ShouldOpen() => FindQuest(out _, out _) != 0;

    /// <summary>
    /// vfunc_85 (@0x140c59830): the base's, then the package quest's last reward row with an item recommended for the
    /// player's job group (FUN_140748050), bound as a new item (rank as the rarity, count as the amount); the title
    /// string key, the progress from the range's low end and the level text. The client reads a null reward when no row
    /// suits the job; that throws here.
    /// </summary>
    protected override bool BuildData(Dictionary<string, object> data)
    {
        base.BuildData(data);
        if (!model.HasLocalPlayer) return false;
        questId = FindQuest(out int low, out int high);
        if (questId == 0) return false;
        var q = Table.Quest(questId);
        if (q == null) return false;
        QuestRewardItem reward = null;
        foreach (var item in q.Complete.ChoiceRewards)
            if (item.ItemId != 0 && model.Tables.Items.Get(item.ItemId) is { } d && d.IsJobRecommended(model.Player.JobGroup))
                reward = item;
        if (reward == null) throw new InvalidOperationException($"level-up package quest {questId}: no reward for job group {model.Player.JobGroup}");
        int level = (ushort)model.Player.Level;
        data["title"] = "$s_levelup_package_widget";
        data["binding"] = Binder.BindNewItem(reward.ItemId, reward.Rank, reward.Count);
        data["max"] = (int)((uint)high - (uint)low);
        data["current"] = (int)((uint)level - (uint)low);
        data["text"] = $"{level}/{high}";
        return true;
    }

    /// <summary>vfunc_oa0_9 (@0x140c59710): a level change updates the data.</summary>
    public override void OnStatus(StatusEvent e)
    {
        if (e == StatusEvent.Level) UpdateData();
    }

    /// <summary>vfunc_o178_8 (@0x140c59740): a quest update of a level guide quest updates the data.</summary>
    public void OnQuestUpdated(int id)
    {
        if (Table?.Quest(id) is { GuideType: 1 }) UpdateData();
    }
}

/// <summary>
/// UIBurningEventWidgetDialog (UI id 0x4f6a): the burning level-up event, from the game event's contexts
/// (CGameEventValueBurningLevelup::GetAll) and the player's event values (DAT_143ab2fc0). Neither is fed yet; with no
/// context vfunc_84 (@0x140c5a220) and vfunc_85 (@0x140c5a420) give 0, so the widget stays closed.
/// </summary>
public sealed class BurningEventWidgetController : EventWidgetController
{
    public override string TypeName => "BurningEventWidget";      // GetTypeName (@0x140c5a0b0)

    /// <summary>The ctor (@0x140c59f10): the binder "BurningEventWidget".</summary>
    public BurningEventWidgetController(UiModel model) { Binder = new ItemSlotBinder(model, "BurningEventWidget"); }

    public override bool ShouldOpen() => false;

    protected override bool BuildData(Dictionary<string, object> data)
    {
        base.BuildData(data);
        return false;
    }

    /// <summary>vfunc_oa0_9 (@0x140c5a1f0): a level change updates the data.</summary>
    public override void OnStatus(StatusEvent e)
    {
        if (e == StatusEvent.Level) UpdateData();
    }
}
