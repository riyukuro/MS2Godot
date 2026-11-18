namespace Ms2.Ui.Hud;

/// <summary>UIMainPlayerInfo (ms2/client/ms2/uimainplayerinfo.cpp): the HP/SP orb, EP gauge, name tag and the quick slots.</summary>
public sealed class MainPlayerInfoController : UiController
{
    public override string Movie => "uimainplayerinfo";
    protected override string EntityMethod => "enabledExpansionSlotToggle";

    /// <summary>CGameOption game pad state m_bExpand, and the pad kind (2 = no key captions).</summary>
    public bool SlotExpand;
    public bool PadShowsKeys = true;
    public bool SkillMinimize, CommonMinimize;
    /// <summary>The quick slot page (CKeyTable current page + 1).</summary>
    public int ShortkeyPage = 1;

    /// <summary>DAT_1439f8ccc: the bottom bar height the client keeps for the built-in HUD (set at run time, not in the
    /// decomp's data); the exp bar's 21.</summary>
    public int BottomBarHeight = 21;

    long prevEp;

    /// <summary>DAT_1439f8cd0: the adventure level bar's height, the second bottom bar (feature 0xbd).</summary>
    public int AdventureBarHeight = 21;
    public const int AdventureLevelFeature = 0xbd;

    /// <summary>vfunc_43 (uimainplayerinfo.cpp:535): bottom centre, above the bottom bars: FUN_1418dd0c0 = height - 2, and
    /// FUN_1418dd110 = the adventure bar's height - 2 with feature 0xbd, else 0 (uibuiltindialog.cpp:45, :65).</summary>
    public override void Layout(double stageW, double stageH)
        => Host.Align(Host.Entity, 2, 0, -(BottomBarHeight - 2 + (Model.IsFeatureEnabled(AdventureLevelFeature) ? AdventureBarHeight - 2 : 0)));

    object PlayerInfo => Host.Child("m_playerInfo");
    object Slot => Host.Child("m_slot");

    /// <summary>OnLoadComplete (uimainplayerinfo.cpp:506).</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        ChangeSkillSlot(SlotExpand ? 1 : 0);
        InitPlayerInfo();
        RefreshQuickSlots();
        // vfunc_oa0_20(IsRiding): not riding and slot mode 0 -> nothing to send (uimainplayerinfo.cpp:795)
        if (Pc.Riding) OnRiding(true);
        Host.Invoke("enabledExpansionSlotToggle", true);
        Host.Invoke("expansionSlotActive", SkillMinimize, CommonMinimize);
        if (Model.ServerKeys is { Loaded: true } t) ShortkeyPage = t.CurrentPage + 1;   // SetShortkeyPageNumber(m_nQuickSlotPage + 1)
        Host.Invoke("setShortkeyPageNumber", ShortkeyPage);
        prevEp = Pc.EpCur;
    }

    /// <summary>ChangeSkillSlot (uimainplayerinfo.cpp:1121): m_30.changeSkillSlot(slot, showKey), then SyncSlotMode.</summary>
    void ChangeSkillSlot(int slot)
    {
        Host.Invoke("changeSkillSlot", slot, PadShowsKeys);
        Host.Invoke(Slot, "onSlotModeChange", 0);       // SyncSlotMode, m_nSlotMode = 0
    }

    /// <summary>RefreshQuickSlots (uimainplayerinfo.cpp:1071): m_slot.updateBindSlot(expand). The slot contents come
    /// through DataBinding ("&lt;slot name&gt;.N" bindings) once the key table is wired.</summary>
    void RefreshQuickSlots() => Host.Invoke(Slot, "updateBindSlot", SlotExpand);

    /// <summary>InitPlayerInfo (uimainplayerinfo.cpp:1394): m_playerInfo visible, then init(obj).</summary>
    void InitPlayerInfo()
    {
        var pi = PlayerInfo;
        if (pi == null) return;
        Host.SetVisible(pi, true);
        Host.Invoke(pi, "init", Host.Obj(
            ("riding", Pc.Riding ? 1 : 0),
            ("level", (uint)Pc.Level),
            ("jobCode", (uint)Pc.SubJob),
            ("jobName", Model.Tables.JobName(Pc.SubJob)),
            ("name", Pc.NameWithTitle),
            ("curHP", (uint)Pc.HpCur),
            ("curSP", (uint)Pc.SpCur),
            ("curEP", (uint)Pc.EpCur),
            ("maxEP", (uint)Pc.EpMax),
            ("battle", Pc.Battle ? 1 : 0),
            ("maxHP", (uint)Pc.HpMax),
            ("maxSP", (uint)Pc.SpMax)));
        Host.Invoke(pi, "setName", Pc.NameWithTitle);      // UpdateName
    }

    void OnRiding(bool riding)
    {
        Host.Invoke(PlayerInfo, "onRiding", Host.Obj(("riding", riding ? 1 : 0), ("curHP", (uint)Pc.HpCur), ("maxHP", (uint)Pc.HpMax),
            ("curSP", (uint)Pc.SpCur), ("maxSP", (uint)Pc.SpMax)));
    }

    /// <summary>The status listener slots vfunc_oa0_3..9 (uimainplayerinfo.cpp:703-759, 1021).</summary>
    public override void OnStatus(StatusEvent e)
    {
        var pi = PlayerInfo;
        switch (e)
        {
            case StatusEvent.Hp: Host.Invoke(pi, "setHP", (uint)Pc.HpCur, (uint)Pc.HpMax); break;
            case StatusEvent.Sp: Host.Invoke(pi, "setSP", (uint)Pc.SpCur, (uint)Pc.SpMax); break;
            case StatusEvent.Ep: Host.Invoke(pi, "setEP", (uint)prevEp, (uint)Pc.EpCur, (uint)Pc.EpMax); prevEp = Pc.EpCur; break;
            case StatusEvent.Job: Host.Invoke(pi, "setJob", (uint)Pc.SubJob, Model.Tables.JobName(Pc.SubJob)); break;
            case StatusEvent.Name: Host.Invoke(pi, "setName", Pc.NameWithTitle); break;
            case StatusEvent.Level: Host.Invoke(pi, "setLevel", (uint)Pc.Level); break;
        }
    }

    /// <summary>UIMainPlayerInfo::GetSlotName (uimainplayerinfo.cpp:1074, :1424): the slot bag "SlotInfo.skill.0", 0x19 slots
    /// named "&lt;bag&gt;.&lt;i&gt;" (FUN_1422cc860).</summary>
    public const string SlotBag = "SlotInfo.skill.0";
    public const int SlotCount = 0x19;
    /// <summary>The SkillSlot ctor's m_kKeys (skillslot.cpp:310): slots 22..24 follow actions 300003, 300033, 300001 instead
    /// of their quick slot action.</summary>
    static readonly Dictionary<int, long> SlotKeys = new() { [0x16] = 0x493e3, [0x17] = 0x49401, [0x18] = 0x493e1 };
    const int PetRankFeature = 0x8c;

    /// <summary>SkillSlot::vfunc_8 (skillslot.cpp:793): the slot object a bound quick slot holds. The slot's skill / item is
    /// the current page's entry (CKeyTable::GetQuickSlotEntry, keytable.cpp:1574). Only empty slots are modelled: no icon,
    /// IsDisabled = 1 (no skill id, :1306), count 0, no cooldown. A filled slot needs the skill data, skill book and item
    /// book the client reads there; it stays unbound (null) until those are ported.</summary>
    public override object GetBinding(string name)
    {
        if (!name.StartsWith(SlotBag + ".", StringComparison.Ordinal)
            || !int.TryParse(name.AsSpan(SlotBag.Length + 1), System.Globalization.NumberStyles.None, null, out var i) || i >= SlotCount)
            return null;
        // A filled slot stays unbound until the state SkillSlot::vfunc_8 (skillslot.cpp:793) reads is modelled:
        // - grayColor: IsDisabled (:1283) asks JobSystemCli::vfunc_2 (jobsystemcli.cpp:81). That also counts the skill set,
        //   the skill-tree level, the lapenshard level (feature 0xd3) and the sub-skill parent's tree level, then the
        //   cast gate FUN_141765a70 and buff masks.
        // - iconPath / itemCount / itemPcBang / itemVIP: the inventory (vfunc_25 :1048, FindItemBySkillId) and the
        //   changeSkill replacement (FUN_141761530).
        // - totalTime / remainTime / rechargeRemainCount: cooldown and recharge state (unless condition@useCoolDownShowUI is 0).
        // - Item entries: item data, CanUseItem and the item counts.
        if (Model.ServerKeys is { } t && !t.Entry(t.CurrentPage, i).IsEmpty) return null;
        var d = new List<(string, object)>();                                   // ASSlot(2): an object of movie 2 (asitemslot.cpp:740)
        if (!Model.IsFeatureEnabled(PetRankFeature)) d.Add(("itemPet", 0));    // IsPetItem, no item
        d.Add(("iconPath", ""));
        d.Add(("grayColor", 1));
        d.Add(("itemCount", 0));
        d.Add(("itemPcBang", 0));
        d.Add(("itemLock", 0));                                                 // FUN_14175cc60 returns 0
        d.Add(("shortcutKey", HotkeyText(i)));
        d.Add(("itemVIP", 0));
        d.Add(("disableDrag", SlotKeys.ContainsKey(i)));                        // IsFixed (0) || HasKeyBinding (:1433)
        d.Add(("totalTime", 0.0));                                              // ASSlot::SetTotalTime / SetRemainTime (:1066, :1060)
        d.Add(("remainTime", 0.0));
        // SetCooldownStrings (:1072): GetStringCommon 0x5cb/0x5ca/0x5cc/0x5ce = s_word_sec/min/hour/day (commonstringtable.cpp:3095)
        d.Add(("cooldownSecStr", Model.Tables.Common("s_word_sec") ?? ""));
        d.Add(("cooldownMinStr", Model.Tables.Common("s_word_min") ?? ""));
        d.Add(("cooldownHourStr", Model.Tables.Common("s_word_hour") ?? ""));
        d.Add(("cooldownDayStr", Model.Tables.Common("s_word_day") ?? ""));
        return Host.Obj(d.ToArray());
    }

    /// <summary>GetHotkeyText / GetHotkey(0) (skillslot.cpp:1229, :1235).</summary>
    string HotkeyText(int slot)
    {
        var keys = Model.Keys;
        int key = SlotKeys.TryGetValue(slot, out var action) ? keys.FindKeyByActionAndPriority(action, 0) : keys.FindKeyByQuickSlot(slot, 0);
        return keys.GetKeyName(key);
    }

    public override bool OnCallback(string name, object[] args)
    {
        switch (name)
        {
            case "onNextPage": ShortkeyPage++; Host.Invoke("setShortkeyPageNumber", ShortkeyPage); return true;
            case "onPrevPage": if (ShortkeyPage > 1) ShortkeyPage--; Host.Invoke("setShortkeyPageNumber", ShortkeyPage); return true;
            case "onRideOff": return true;
            case "OnToggleSkillExpansionSlot": SkillMinimize = !SkillMinimize; return true;
            case "OnToggleCommonExpansionSlot": CommonMinimize = !CommonMinimize; return true;
        }
        return false;
    }
}
