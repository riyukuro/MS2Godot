namespace Ms2.Ui.Hud;

/// <summary>UIMainPlayerInfo (ms2/client/ms2/uimainplayerinfo.cpp): the HP/SP orb, EP gauge, name tag and the quick slots.</summary>
public sealed class MainPlayerInfoController : UiController, Skills.IBuffStrip
{
    /// <summary>The ctor (@0x1419ee140) and UIBuiltInDialog's (uibuiltindialog.cpp:19) have no SetTweenEnabled: m_c4 stays 0,
    /// no open / close tween.</summary>
    protected override bool TweenEnabled => false;

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
        InitEffects();
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
        UpdateEffectPositions();
        Host.Invoke(Slot, "onSlotModeChange", 0);       // SyncSlotMode, m_nSlotMode = 0
    }

    // ---- the buff strip ----

    /// <summary>m_effectManager: the "m_effectManager" clip's buffs and debuffs; null before the load.</summary>
    BuffEffectManager effects;
    /// <summary>m_pending: the buffs added or updated before the load, by key.</summary>
    readonly Dictionary<int, Skills.AdditionalBuff> pendingBuffs = new();
    /// <summary>GetTickCountMs2, the add tick and cooldown ring clock.</summary>
    public Func<uint> TickMs = () => (uint)Environment.TickCount64;
    /// <summary>IsUpperTargetId (the buff list's).</summary>
    public Func<int, bool> IsUpperTargetId = _ => false;

    /// <summary>The effect part of OnLoadInit (uimainplayerinfo.cpp:485, with a local player): the manager over m_effectManager
    /// with tooltip align 5, 0x15 shown, 7 columns, rows going up; its positions; then the pending buffs.</summary>
    void InitEffects()
    {
        if (!Model.HasLocalPlayer || Host.Child("m_effectManager") is not { } clip) return;
        effects = new BuffEffectManager(Host, Model, clip) { TooltipAlign = 5, Max = 0x15, Columns = 7, Up = true, TickMs = () => TickMs(), IsUpperTargetId = id => IsUpperTargetId(id) };
        UpdateEffectPositions();
        foreach (var b in pendingBuffs.Values.ToList()) AddBuffEffect(b);
        pendingBuffs.Clear();
    }

    /// <summary>UpdateEffectPositions @0x1419f21b0: the buffs at (-395, -12) and the debuffs at (0x48, -12); with the expanded
    /// pad (-478, -60) and (0x5d, -60).</summary>
    void UpdateEffectPositions()
    {
        if (effects == null) return;
        if (SlotExpand) effects.SetPosA(-478, -60); else effects.SetPosA(-395, -12);
        if (SlotExpand) effects.SetPosB(0x5d, -60); else effects.SetPosB(0x48, -12);
    }

    /// <summary>AddBuffEffect @0x1419f0fe0: to the manager, else pending; then FUN_1419f37f0(5).</summary>
    public void AddBuffEffect(Skills.AdditionalBuff buff)
    {
        if (effects != null) effects.AddEffect(buff);
        else pendingBuffs[buff.Key] = buff;
        QueueSlotRefresh(RefreshGray | RefreshRecharge);
    }

    /// <summary>UpdateBuffEffect @0x1419f10b0.</summary>
    public void UpdateBuffEffect(Skills.AdditionalBuff buff)
    {
        if (effects != null) effects.UpdateEffect(buff);
        else pendingBuffs[buff.Key] = buff;
        QueueSlotRefresh(RefreshGray | RefreshRecharge);
    }

    /// <summary>RemoveBuffEffect @0x1419f1180.</summary>
    public void RemoveBuffEffect(int key)
    {
        if (effects != null) effects.RemoveEffect(key);
        else pendingBuffs.Remove(key);
        QueueSlotRefresh(RefreshGray | RefreshRecharge);
    }

    /// <summary>The effect part of vfunc_15 @0x1419ef820, every frame with a local player: m_effectManager-&gt;Update().</summary>
    public void UpdateEffects()
    {
        if (!Model.HasLocalPlayer) return;
        effects?.Update();
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
        if (e is StatusEvent.Hp or StatusEvent.Sp or StatusEvent.Ep or StatusEvent.Level) QueueSlotRefresh(RefreshGray);     // :717-763
    }

    /// <summary>UIMainPlayerInfo::GetSlotName (uimainplayerinfo.cpp:1074, :1424): the slot bag "SlotInfo.skill.0", 0x19 slots
    /// named "&lt;bag&gt;.&lt;i&gt;" (FUN_1422cc860).</summary>
    public const string SlotBag = "SlotInfo.skill.0";
    public const int SlotCount = 0x19;
    /// <summary>The SkillSlot ctor's m_kKeys (skillslot.cpp:310): slots 22..24 follow actions 300003, 300033, 300001 instead
    /// of their quick slot action.</summary>
    static readonly Dictionary<int, long> SlotKeys = new() { [0x16] = 0x493e3, [0x17] = 0x49401, [0x18] = 0x493e1 };
    const int PetRankFeature = 0x8c;

    /// <summary>The player a skill tooltip reads (its job system, quests, quick slots, hands); null = no player.</summary>
    public Func<Skills.SkillTooltipPlayer> TooltipPlayer;

    /// <summary>The local player state the quick slots read (inventory, skill book, field, ...); null = a world with no player
    /// state beyond the model's tables and level.</summary>
    public Func<Skills.SlotWorld> SlotWorld;

    /// <summary>The fed player state, else one with only the model's tables and level.</summary>
    Skills.SlotWorld World() => SlotWorld?.Invoke()
        ?? new Skills.SlotWorld { Tables = Model.Tables.Skills, ItemData = Model.Tables.Items.Get, Level = Model.Player.Level, HasPc = Model.HasLocalPlayer };

    /// <summary>
    /// SkillSlot::vfunc_8 @0x14175e9e0 (skillslot.cpp:793): the slot object a bound quick slot holds, for the current page's
    /// entry (CKeyTable::GetQuickSlotEntry, keytable.cpp:1574; all zero = an empty slot). The state comes from
    /// <see cref="Skills.SkillSlotState"/>; the cooldown and recharge parts read the skill system, which Godot does not run yet,
    /// so they stay 0.
    /// </summary>
    public override object GetBinding(string name)
    {
        if (!name.StartsWith(SlotBag + ".", StringComparison.Ordinal)
            || !int.TryParse(name.AsSpan(SlotBag.Length + 1), System.Globalization.NumberStyles.None, null, out var i) || i >= SlotCount)
            return null;
        var e = Model.ServerKeys is { } t ? t.Entry(t.CurrentPage, i) : default;
        var w = World();
        var s = new Skills.SkillSlotState(w, (uint)e.SkillId, e.ItemId, e.ItemUid);
        int hotkey = Hotkey(i);
        float total = s.CooldownDuration(), remain = s.CooldownRemain();
        if (w.HasPc && s.SkillId != 0 && w.Tables.GetSkillData(s.SkillId) is { Condition.UseCoolDownShowUI: false })
        {
            total = 0;
            remain = 0;
        }
        var d = new List<(string, object)>();                                   // ASSlot(2): an object of movie 2 (asitemslot.cpp:740)
        var item = s.Item();
        if (item != null) d.Add(("itemGrade", item.Rarity));                    // FUN_1409adc90; the ugc tag needs the ugc block (not kept)
        string icon = s.IconName();
        if (s.Replacement() is { } r && r.Icon.Length != 0) icon = r.Icon;    // FUN_141761530: the replacement's own icon
        if (Model.IsFeatureEnabled(PetRankFeature))
        {
            if (item != null && w.ItemData(item.Id) is { IsPetItem: true })
            {
                d.Add(("itemPetRank", item.Rarity));
                d.Add(("dropShadowDisable", 1));
            }
        }
        else d.Add(("itemPet", s.IsPetItem ? 1 : 0));
        d.Add(("iconPath", icon));
        bool gray = s.IsDisabled(hotkey);
        int count = s.ItemCount(false);
        d.Add(("grayColor", gray ? 1 : 0));
        d.Add(("itemCount", count));
        shown[i] = (gray, count, s.RechargeRemaining);
        d.Add(("itemPcBang", s.IsPcBangItem ? 1 : 0));
        d.Add(("itemLock", 0));                                                 // FUN_14175cc60 returns 0
        d.Add(("shortcutKey", Model.Keys.GetKeyName(hotkey)));
        d.Add(("itemVIP", s.IsVipItem ? 1 : 0));
        d.Add(("disableDrag", SlotKeys.ContainsKey(i)));                        // IsFixed (0) || HasKeyBinding (:1433)
        if (s.RechargeMax > 0) d.Add(("rechargeRemainCount", s.RechargeRemaining));
        d.Add(("totalTime", (double)total));                                    // ASSlot::SetTotalTime / SetRemainTime (:1066, :1060)
        d.Add(("remainTime", (double)remain));
        // SetCooldownStrings (:1072): GetStringCommon 0x5cb/0x5ca/0x5cc/0x5ce = s_word_sec/min/hour/day (commonstringtable.cpp:3095)
        d.Add(("cooldownSecStr", Model.Tables.Common("s_word_sec") ?? ""));
        d.Add(("cooldownMinStr", Model.Tables.Common("s_word_min") ?? ""));
        d.Add(("cooldownHourStr", Model.Tables.Common("s_word_hour") ?? ""));
        d.Add(("cooldownDayStr", Model.Tables.Common("s_word_day") ?? ""));
        return Host.Obj(d.ToArray());
    }

    /// <summary>
    /// SkillSlot::vfunc_0 (ITooltip, skillslot.cpp:716) through NotifyDragSlot::GetSlotTooltip: null for a name that is not
    /// a slot; an empty object when the slot gives none. With m_bFixed 0 (no slot of this bar is fixed) every filled
    /// tooltip gets the right-click remove hint, SetEnablePuton(GetStringCommon(0x759)) = s_msg_rclick_putoff_qslot.
    /// - An item id whose item is gone: ASTooltip(player, 0x40, 2).FUN_1409e1770(id, GetItemRank(id)).
    ///   STILL-OPEN: the item tooltip by id is not ported, so that slot shows none.
    /// - An item that FUN_14053e460 allows: FUN_1409e0d40 over the item, the same flags.
    /// - Else the skill: none for an item kind skill (FUN_1407e9d70); a key bound slot (22..24) gets its action's text
    ///   (GetActionLabel, :1467) aligned 5; else FUN_1409e1c50(skill, the book's level or 1, 0) with flags 0x40.
    /// </summary>
    public override IDictionary<string, object> GetSlotTooltip(string name)
    {
        if (!name.StartsWith(SlotBag + ".", StringComparison.Ordinal)
            || !int.TryParse(name.AsSpan(SlotBag.Length + 1), System.Globalization.NumberStyles.None, null, out var i) || i >= SlotCount)
            return null;
        return SlotTooltip(i) ?? new AsTooltip();
    }

    const int SlotTooltipFlags = 0x40;

    AsTooltip SlotTooltip(int i)
    {
        var e = Model.ServerKeys is { } t ? t.Entry(t.CurrentPage, i) : default;
        var w = World();
        var s = new Skills.SkillSlotState(w, (uint)e.SkillId, e.ItemId, e.ItemUid);
        var item = s.Item();
        string removeHint = Model.Tables.Common("s_msg_rclick_putoff_qslot") ?? "";     // 0x759
        if (item == null && e.ItemId != 0) return null;
        if (item != null && w.ItemData(item.Id) is { } data && data.IsQuickSlotUsable)
        {
            if (!Model.HasLocalPlayer) return null;
            var tip = new Items.ItemTooltip(Model, SlotTooltipFlags);
            if (!tip.Fill(InventoryController.ToItem(item, Model.Tables.Items))) return null;
            tip.SetEnablePuton(removeHint);
            return tip.Data;
        }
        uint skillId = s.EffectiveSkillId;
        if (w.Tables.GetSkillData(skillId) is { IsItemKind: true }) return null;
        if (SlotKeys.TryGetValue(i, out var action))
        {
            var text = AsTooltip.Text(ActionLabel(action));
            text?.SetTooltipAlign(5);
            return text;
        }
        if (!Model.HasLocalPlayer) return null;
        var player = TooltipPlayer?.Invoke();
        int level = w.Jobs?.LevelData(skillId)?.Level ?? 1;
        var skillTip = Skills.SkillTooltip.Build(Model.Tables, player, skillId, level, SlotTooltipFlags, false);
        if (skillTip == null) return null;
        skillTip["enablePuton"] = removeHint;
        return skillTip;
    }

    /// <summary>SkillSlot::GetActionLabel (skillslot.cpp:1467): the text of a key bound slot's action.</summary>
    string ActionLabel(long action) => action switch
    {
        0x493e3 => Model.Tables.Common("s_quick_slot_item_get") ?? "",
        0x49401 => Model.Tables.Common("s_quick_slot_attack") ?? "",
        0x493e1 => Model.Tables.Common("s_quick_slot_jump") ?? "",
        _ => "",
    };

    // ---- the slot refresh: queued flags flushed every 200 ms ----

    public const int RefreshGray = 1, RefreshItemCount = 2, RefreshRecharge = 4;
    /// <summary>The gray state, item count and recharge count each slot last showed (SkillSlot m_nDisabled, m_nItemCount,
    /// m_nRecharge).</summary>
    readonly (bool Gray, int ItemCount, int Recharge)[] shown = new (bool, int, int)[SlotCount];
    int pendingRefresh;

    /// <summary>FUN_1419f37f0 (uimainplayerinfo.cpp:1527): queue a refresh of every slot. The client queues
    /// <see cref="RefreshGray"/> on a level, HP, SP or EP change (vfunc_oa0_9/3/4/5, :713-763) and on a bag item added or removed
    /// (vfunc_21/22, :839-849); gray | recharge on buff and cooldown changes. STILL-OPEN: flag 8 (SkillSlot::FUN_14175f510,
    /// part of the buff list's 0xd) is not ported.</summary>
    public void QueueSlotRefresh(int flags) => pendingRefresh |= flags;

    /// <summary>
    /// UIMainPlayerInfo::FUN_1419f3590 (uimainplayerinfo.cpp:1476), which the panel's vfunc_15 runs when its 200 ms timer fires
    /// (:640): SkillSlotBag::RefreshAllSlots (skillslot.cpp:1590) gives each slot SkillSlot::FUN_14175f280 (:912) with the queued
    /// flags, and a slot whose gray state, item count or recharge count changed is updated. The client passes only the
    /// changed members to the slot's updateData; here the slot's whole vfunc_8 object is pushed again (its other members
    /// read the same state).
    /// </summary>
    public void FlushSlotRefresh()
    {
        int flags = pendingRefresh;
        if (flags == 0) return;
        pendingRefresh = 0;
        if (Model.ServerKeys is not { } t) return;
        var w = World();
        for (int i = 0; i < SlotCount; i++)
        {
            var e = t.Entry(t.CurrentPage, i);
            var s = new Skills.SkillSlotState(w, (uint)e.SkillId, e.ItemId, e.ItemUid);
            var was = shown[i];
            bool changed = ((flags & RefreshGray) != 0 && s.IsDisabled(Hotkey(i)) != was.Gray)
                || ((flags & RefreshItemCount) != 0 && s.ItemCount(false) != was.ItemCount)
                || ((flags & RefreshRecharge) != 0 && s.RechargeRemaining != was.Recharge);
            if (changed) Host.Push(SlotBag + "." + i);
        }
    }

    /// <summary>
    /// Pushes every slot again (each SkillSlot's vfunc_8 rebuild): when the player state the slots read first arrives or is
    /// replaced (the own FieldAddUser's skill book, a Job packet), and for a bag change, where the client's UpdateSlotForItem
    /// (uimainplayerinfo.cpp:1357) rebinds the item's slot (vfunc_10) before queueing the gray refresh.
    /// </summary>
    public void RebindSlots()
    {
        for (int i = 0; i < SlotCount; i++) Host.Push(SlotBag + "." + i);
    }

    /// <summary>GetHotkey(0) (skillslot.cpp:1235); GetHotkeyText (:1229) is its key name.</summary>
    int Hotkey(int slot)
    {
        var keys = Model.Keys;
        return SlotKeys.TryGetValue(slot, out var action) ? keys.FindKeyByActionAndPriority(action, 0) : keys.FindKeyByQuickSlot(slot, 0);
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
