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

    /// <summary>vfunc_43 (uimainplayerinfo.cpp:535): bottom centre, above the bottom bar (FUN_1418dd0c0 = height - 2; the
    /// second bar of feature 0xbd is off).</summary>
    public override void Layout(double stageW, double stageH) => Host.Align(Host.Entity, 2, 0, -(BottomBarHeight - 2));

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
