namespace Ms2.Ui.Hud;

/// <summary>UIMainPlayerInfo (ms2/client/ms2/uimainplayerinfo.cpp): the HP/SP orb, EP gauge, name tag and the quick slots.</summary>
public sealed class MainPlayerInfoController : UiController, Skills.IBuffStrip, IDragSlotOwner
{
    /// <summary>The ctor (@0x1419ee140) and UIBuiltInDialog's (uibuiltindialog.cpp:19) have no SetTweenEnabled: m_c4 stays 0,
    /// no open / close tween.</summary>
    protected override bool TweenEnabled => false;

    public override string Movie => "uimainplayerinfo";
    protected override string EntityMethod => "enabledExpansionSlotToggle";

    /// <summary>UIMainPlayerInfo is a UIBuiltInDialog (uimainplayerinfo.h:59): the ctor's flag 0 (vfunc_56).</summary>
    public override bool AlwaysOn => true;

    /// <summary>GetTypeName @0x1419eea30 (uimainplayerinfo.cpp:415): "MainPlayerInfo". The guide's lookups find the
    /// panel by it (GuideUIByName2, uiguideeventarrow.cpp:679).</summary>
    public override string TypeName => "MainPlayerInfo";

    /// <summary>CGameOption game pad state m_bExpand.</summary>
    public bool SlotExpand;
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

    /// <summary>SetGamepadEnabled's call (gameoptionstruct.cpp:2594-2595): the slot mode follows the pad switch; nothing before the load (m_30 is null).</summary>
    public void SetGamePadMode(bool use)
    {
        SlotExpand = use;
        if (Host.Child("m_slot") != null) ChangeSkillSlot(use ? 1 : 0);
    }

    /// <summary>MS2InputService::GetPadKind (FUN_140a58540), for the pad key captions.</summary>
    public Func<int> PadKind = () => 0;

    /// <summary>
    /// UpdateModifiedKeys @0x1419f1e60 (uimainplayerinfo.cpp:1136-1152): with the movie loaded and the pad's expanded slot bar, the keys
    /// bound to the actions 800001, 800002 and 800003 at priority 2 give the three modifier captions (CKeyTable::FUN_141431820, upper
    /// case), passed as modifiedKey1..3 to setModifiedKey.
    /// </summary>
    public void UpdateModifiedKeys()
    {
        if (Host.Entity == null || !SlotExpand || Model.Keys is not { } keys) return;
        var caption = new Dictionary<string, object>();
        for (int i = 0; i < 3; i++)
            caption["modifiedKey" + (i + 1)] = keys.GamepadKeyText(keys.FindKeyByActionAndPriority(800001 + i, 2), SlotExpand, PadKind()).ToUpperInvariant();
        Host.Invoke("setModifiedKey", caption);
    }

    /// <summary>ChangeSkillSlot (uimainplayerinfo.cpp:1121): m_30.changeSkillSlot(slot, showKey), then SyncSlotMode.</summary>
    void ChangeSkillSlot(int slot)
    {
        Host.Invoke("changeSkillSlot", slot, PadKind() != PadKinds.Dual);   // bShowKey: the dual pad shows no key captions (:1126)
        UpdateModifiedKeys();
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

    /// <summary>The local player's buff list, which makes the custom effects (AdditionalCustomCli); null = none yet.</summary>
    public Skills.AdditionalBuffList OwnBuffs;
    /// <summary>The local player's object id (the custom effects' caster and owner).</summary>
    public Func<int> LocalObjectId = () => 0;

    /// <summary>GetBuffEffectId (uimainplayerinfo.cpp:1455): a custom effect's strip key, its id + 1000000000.</summary>
    public static int GetBuffEffectId(int id) => id + 1000000000;

    /// <summary>AddCustomBuffEffect @0x1419f1200: nothing without a local player; else the id's old custom effect removed, a
    /// new AdditionalCustomCli (key GetBuffEffectId, level 1 of the id, ending at <paramref name="endTick"/>, count 1) added
    /// when valid, then FUN_1419f37f0(5).</summary>
    public void AddCustomBuffEffect(int id, int endTick)
    {
        if (!Model.HasLocalPlayer || OwnBuffs == null) return;
        RemoveCustomBuffEffect(id);
        var buff = OwnBuffs.Custom(GetBuffEffectId(id), (uint)id, (uint)endTick, 1, LocalObjectId());
        if (buff.IsValid) AddBuffEffect(buff);
        QueueSlotRefresh(RefreshGray | RefreshRecharge);
    }

    /// <summary>RemoveCustomBuffEffect @0x1419f1370: RemoveBuffEffect of the id's key, then FUN_1419f37f0(5).</summary>
    public void RemoveCustomBuffEffect(int id)
    {
        RemoveBuffEffect(GetBuffEffectId(id));
        QueueSlotRefresh(RefreshGray | RefreshRecharge);
    }

    /// <summary>The MyPC part of PC::FUN_141661370 (pc.cpp:3386-3406): the running timer's custom effect removed, then on a
    /// start with an end tick (<see cref="InvincibleBlink.BuffEnd"/>, 0 for other timers) the new timer's added.</summary>
    public void InvincibleBuff(int oldTimerId, int timerId, int endTick)
    {
        if (oldTimerId != 0) RemoveCustomBuffEffect(oldTimerId);
        if (timerId != 0 && endTick != 0) AddCustomBuffEffect(timerId, endTick);
    }

    /// <summary>The effect part of vfunc_15 @0x1419ef820, every frame with a local player: m_effectManager-&gt;Update().</summary>
    public void UpdateEffects()
    {
        if (!Model.HasLocalPlayer) return;
        effects?.Update();
    }

    /// <summary>RefreshQuickSlots (uimainplayerinfo.cpp:1071): m_slot.updateBindSlot(expand). The slot contents come
    /// through DataBinding ("&lt;slot name&gt;.N" bindings) once the key table is wired.</summary>
    public void RefreshQuickSlots() => Host.Invoke(Slot, "updateBindSlot", SlotExpand);

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
            ("curHP", Pc.HpCur),
            ("curSP", (uint)Pc.SpCur),
            ("curEP", (uint)Pc.EpCur),
            ("maxEP", (uint)Pc.EpMax),
            ("battle", Pc.Battle ? 1 : 0),
            ("maxHP", Pc.HpMax),
            ("maxSP", (uint)Pc.SpMax)));
        Host.Invoke(pi, "setName", Pc.NameWithTitle);      // UpdateName
    }

    void OnRiding(bool riding)
    {
        Host.Invoke(PlayerInfo, "onRiding", Host.Obj(("riding", riding ? 1 : 0), ("curHP", Pc.HpCur), ("maxHP", Pc.HpMax),
            ("curSP", (uint)Pc.SpCur), ("maxSP", (uint)Pc.SpMax)));
    }

    /// <summary>The status listener slots vfunc_oa0_3..9 (uimainplayerinfo.cpp:703-759, 1021).</summary>
    public override void OnStatus(StatusEvent e)
    {
        var pi = PlayerInfo;
        switch (e)
        {
            case StatusEvent.Hp: Host.Invoke(pi, "setHP", (int)Pc.HpCur, (int)Pc.HpMax); break;
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

    /// <summary>Each quick slot's own AS object, the data its binding was made with (SkillSlot::m_value18); null = not bound.</summary>
    readonly object[] slotObjects = new object[SlotCount];

    /// <summary>SkillSlot::vfunc_9 @0x14175dec0 (skillslot.cpp:668): the slot keeps its AS object, whose startIconTween
    /// (m_spTween) <see cref="StartIconTween"/> calls.</summary>
    public override void OnBindData(string name, object data)
    {
        int i = SlotIndex(name);
        if (i >= 0) slotObjects[i] = data;
    }

    /// <summary>The AS object quick slot <paramref name="i"/> was bound with; null = not bound.</summary>
    public object SlotObject(int i) => i >= 0 && i < SlotCount ? slotObjects[i] : null;

    /// <summary>CInputKeyHandler::FUN_14116d7a0(0xf), <see cref="InputBlock"/>.</summary>
    public Func<bool> InputBlocked = () => InputBlock.Active;

    /// <summary>SkillSlot::FUN_14175f200 (skillslot.cpp:902): unless input is blocked (<see cref="InputBlocked"/>), the slot's
    /// startIconTween (BaseSlot: the icon's shrink and grow tween, unless it already runs).</summary>
    void StartIconTween(int i)
    {
        if (InputBlocked()) return;
        if (slotObjects[i] is { } o) Host?.Invoke(o, "startIconTween");
    }

    /// <summary>
    /// SkillSlot::vfunc_8 @0x14175e9e0 (skillslot.cpp:793): the slot object a bound quick slot holds, for the current page's
    /// entry (CKeyTable::GetQuickSlotEntry, keytable.cpp:1574; all zero = an empty slot). The state comes from
    /// <see cref="Skills.SkillSlotState"/>; the cooldown and recharge parts read the live session's cooldown table (<see cref="Skills.SkillCooltimes"/>),
    /// 0 offline.
    /// </summary>
    public override object GetBinding(string name)
    {
        if (!name.StartsWith(SlotBag + ".", StringComparison.Ordinal)
            || !int.TryParse(name.AsSpan(SlotBag.Length + 1), System.Globalization.NumberStyles.None, null, out var i) || i >= SlotCount)
            return null;
        var e = Shown(i);
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
    /// a slot; an empty object when the slot gives none. A filled tooltip of a slot that is not fixed (IsFixed() == 0,
    /// :725; no slot of this bar is) gets the right-click remove hint, SetEnablePuton(GetStringCommon(0x759)) = s_msg_rclick_putoff_qslot.
    /// - An item id whose item is gone: ASTooltip(player, 0x40, 2).FUN_1409e1770(id, GetItemRank(id)) (:722), GetItemRank
    ///   being the data's default rank (its rank field, 1 when that is 0).
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
        var e = Shown(i);
        var w = World();
        var s = new Skills.SkillSlotState(w, (uint)e.SkillId, e.ItemId, e.ItemUid);
        var item = s.Item();
        string removeHint = Model.Tables.Common("s_msg_rclick_putoff_qslot") ?? "";     // 0x759
        if (item == null && e.ItemId != 0)
        {
            var gone = new Items.ItemTooltip(Model, SlotTooltipFlags);
            int rank = Items.ItemTooltip.ItemRank(Model.Tables.Items, e.ItemId);
            if (!gone.FillById(e.ItemId, rank)) return null;
            if (!IsFixed) gone.SetEnablePuton(removeHint);
            return gone.Data;
        }
        if (item != null && w.ItemData(item.Id) is { } data && data.IsQuickSlotUsable)
        {
            if (!Model.HasLocalPlayer) return null;
            var tip = new Items.ItemTooltip(Model, SlotTooltipFlags);
            if (!tip.Fill(InventoryController.ToItem(item, Model.Tables.Items))) return null;
            if (!IsFixed) tip.SetEnablePuton(removeHint);
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
        if (!IsFixed) skillTip["enablePuton"] = removeHint;
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
        var w = World();
        for (int i = 0; i < SlotCount; i++)
        {
            var e = Shown(i);
            var s = new Skills.SkillSlotState(w, (uint)e.SkillId, e.ItemId, e.ItemUid);
            var was = shown[i];
            bool changed = ((flags & RefreshGray) != 0 && s.IsDisabled(Hotkey(i)) != was.Gray)
                || ((flags & RefreshItemCount) != 0 && s.ItemCount(false) != was.ItemCount)
                || ((flags & RefreshRecharge) != 0 && s.RechargeRemaining != was.Recharge);
            if (changed) Host.Push(SlotBag + "." + i);
        }
    }

    /// <summary>
    /// UIMainPlayerInfo::vfunc_oa0_25 @0x1419f0280 (uimainplayerinfo.cpp:774) with SkillSlot::vfunc_23 @0x14175f0c0 (skillslot.cpp:880)
    /// on every quick slot: a cooldown started on <paramref name="key"/> (<see cref="Skills.SkillCooltimes.Started"/>). A slot whose
    /// skill shows it runs the slot's onCooldownStart(remain, total) and queues the gray and recharge refresh (FUN_1419f37f0(5)).
    /// STILL-OPEN: the ChromaSDK SetCooldown call.
    /// </summary>
    public void OnCooldownStarted(ulong key)
    {
        var w = World();
        for (int i = 0; i < SlotCount; i++)
        {
            if (slotObjects[i] is not { } o) continue;
            var e = Shown(i);
            if (!new Skills.SkillSlotState(w, (uint)e.SkillId, e.ItemId, e.ItemUid).NotifyCooldownStart(key, out float remain, out float total)) continue;
            Host?.Invoke(o, "onCooldownStart", (double)remain, (double)total);
            QueueSlotRefresh(RefreshGray | RefreshRecharge);
        }
    }

    /// <summary>
    /// UIMainPlayerInfo::vfunc_oa0_26 @0x1419f0320 (:781) with SkillSlot::vfunc_24 @0x14175f180 (skillslot.cpp:892): a cooldown ended on
    /// <paramref name="key"/> (<see cref="Skills.SkillCooltimes.Ended"/>). A slot whose skill has no time left runs the slot's
    /// onCooldownEnd (the ready flash) and queues the gray and recharge refresh. STILL-OPEN: the ChromaSDK ResetCooldown call.
    /// </summary>
    public void OnCooldownEnded(ulong key)
    {
        var w = World();
        for (int i = 0; i < SlotCount; i++)
        {
            if (slotObjects[i] is not { } o) continue;
            var e = Shown(i);
            if (!new Skills.SkillSlotState(w, (uint)e.SkillId, e.ItemId, e.ItemUid).NotifyCooldownEnd(key)) continue;
            Host?.Invoke(o, "onCooldownEnd");
            QueueSlotRefresh(RefreshGray | RefreshRecharge);
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

    /// <summary>
    /// SkillSlot::vfunc_25 @0x14175fbf0 of slot <paramref name="i"/> (<see cref="Skills.SkillSlotState.Item"/>): the uid of the item
    /// the shown slot finds (by its uid, item id or skill), 0 when none; CKeyTable::GetQuickSlotItemUidByKey (keytable.cpp:1546)
    /// reads it through the main player info's GetSlot.
    /// </summary>
    public long SlotItemUid(int i)
    {
        if (i < 0 || i >= SlotCount) return 0;
        var e = Shown(i);
        return new Skills.SkillSlotState(World(), (uint)e.SkillId, e.ItemId, e.ItemUid).Item()?.Uid ?? 0;
    }

    /// <summary>
    /// The skill, item id and item uid each slot shows (SkillSlot m_nSkillId, m_nItemId, m_nItemUid): the slot's own copy, which
    /// SkillSlotBag::vfunc_16 (skillslot.cpp:1635) builds from the current page (GetQuickSlotEntry) and
    /// <see cref="OnShortkeyPageNotify"/> reloads; a quick slot edit outside a skill set changes only this copy.
    /// </summary>
    readonly Ms2.Net.Packets.QuickSlotEntry[] slotFields = new Ms2.Net.Packets.QuickSlotEntry[SlotCount];
    /// <summary>The page <see cref="slotFields"/> came from; -1 until the bag is built from a key table.</summary>
    public int SlotFieldsPage { get; private set; } = -1;

    /// <summary>Slot <paramref name="i"/>'s own entry (<see cref="slotFields"/>); the first read builds them from the current
    /// page (vfunc_16).</summary>
    public Ms2.Net.Packets.QuickSlotEntry ShownEntry(int i) => i >= 0 && i < SlotCount ? Shown(i) : default;

    Ms2.Net.Packets.QuickSlotEntry Shown(int i)
    {
        if (SlotFieldsPage < 0 && Model.ServerKeys is { } t)
        {
            SlotFieldsPage = t.CurrentPage;
            for (int k = 0; k < SlotCount; k++) slotFields[k] = t.Entry(t.CurrentPage, k);
        }
        return slotFields[i];
    }

    static readonly int[] UpdateOrder = { 4, 5, 6, 7, 0, 1, 2, 3, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24 };

    /// <summary>
    /// UIMainPlayerInfo::OnShortkeyPageNotify (uimainplayerinfo.cpp:997), which CKeyTable::SetQuickSlotPage (keytable.cpp:2547)
    /// posts from OnKeyTable mode 0 and 7 (keytable.cpp:285-370): setShortkeyPageNumber(cur + 1), then
    /// UpdateQuickSlots @0x1419f2560 (:1209) forced when the page changed. It walks the slots in the order 4..7, 0..3, 8..24: a
    /// slot whose page entry is empty empties (vfunc_11) and is pushed (vfunc_10); a slot whose entry equals its own is skipped
    /// unless forced; else it takes the entry and is pushed. STILL-OPEN: mode 7's GetLocked early return.
    /// </summary>
    public void OnShortkeyPageNotify(int prev, int cur)
    {
        ShortkeyPage = cur + 1;
        Host.Invoke("setShortkeyPageNumber", ShortkeyPage);
        if (Model.ServerKeys is not { } t) return;
        bool force = prev != cur || SlotFieldsPage < 0;
        SlotFieldsPage = t.CurrentPage;
        foreach (int i in UpdateOrder)
        {
            var e = t.Entry(t.CurrentPage, i);
            if (!e.IsEmpty && !force && e.Equals(slotFields[i])) continue;
            slotFields[i] = e;
            Host.Push(SlotBag + "." + i);
        }
    }

    /// <summary>A KeyTable packet taken into <see cref="UiModel.ServerKeys"/>: when SetQuickSlotPage took its page
    /// (<see cref="Ms2.Net.Packets.ServerKeyTable.PageNotified"/>), the NotifyShortkeyPage it posts
    /// (<see cref="OnShortkeyPageNotify"/>); else the slots keep their own entries.</summary>
    public void OnKeyTable()
    {
        if (Model.ServerKeys is { PageNotified: true } t) OnShortkeyPageNotify(SlotFieldsPage, t.CurrentPage);
    }

    /// <summary>GetHotkey(0) (skillslot.cpp:1235); GetHotkeyText (:1229) is its key name.</summary>
    int Hotkey(int slot)
    {
        var keys = Model.Keys;
        return SlotKeys.TryGetValue(slot, out var action) ? keys.FindKeyByActionAndPriority(action, 0) : keys.FindKeyByQuickSlot(slot, 0);
    }

    // ---- the quick slot edits ---------------------------------------------------------------------------------------------

    /// <summary>The KeyTable sends of the quick slot edits (FUN_140710590).</summary>
    public Action<Ms2.Net.Wire.IOutPacket> Send = _ => { };
    /// <summary>FUN_141275260(id, 1, 1): a system notice of a common string id.</summary>
    public Action<int> Notice = _ => { };
    /// <summary>FUN_140d73bc0(id, 0, 0, ""): a notice box of a common string id.</summary>
    public Action<int> NoticeBox = _ => { };

    /// <summary>
    /// The quick slot index of a name ("&lt;bag&gt;.&lt;i&gt;", i below 0x19), else -1: SkillSlotBag names its slots
    /// "SlotInfo.skill.0.&lt;i&gt;" (GetSlotName @0x1419f31f0, uimainplayerinfo.cpp:1424, with FUN_1417633f0(.., 0x19)) and
    /// SkillSlot::GetIndex is that i, which FUN_14092f690 sends as mode 3's slot (skillslot.cpp:522, :552). Slot i's key is the
    /// one bound to action 500001 + i (SkillSlot::FUN_141760a10 skillslot.cpp:1246; CKeyTable::InitKeys m_quickSlotActions,
    /// keytable.cpp:653; FindKeyByQuickSlot :1202), so with DefaultKey.xml slots 0-7 are Q W E R A S D F, 8-15 the keys 1-8
    /// (the slot labelled "2" is index 9), 16-21 Insert Home PageUp Delete End PageDown, and 22-24 have none.
    /// </summary>
    static int SlotIndex(string name) =>
        name != null && name.StartsWith(SlotBag + ".", StringComparison.Ordinal)
        && int.TryParse(name.AsSpan(SlotBag.Length + 1), System.Globalization.NumberStyles.None, null, out var i) && i < SlotCount ? i : -1;

    /// <summary>SkillSlot::IsFixed (m_bFixed): 0 for every slot of this bar; a slot tooltip's remove hint and
    /// FUN_141761990 test it (skillslot.cpp:725, :1433).</summary>
    static bool IsFixed => false;

    /// <summary>SkillSlot::FUN_141761990 (skillslot.cpp:1433): IsFixed or a key bound slot (22..24).</summary>
    static bool IsFixedOrBound(int slot) => IsFixed || SlotKeys.ContainsKey(slot);

    /// <summary>A SkillSlot of the bar.</summary>
    public bool IsDragSlot(string name) => SlotIndex(name) >= 0;

    /// <summary>DragSlot::vfunc_10: the slot pushed again.</summary>
    public void RefreshSlot(string name) { if (IsDragSlot(name)) Host.Push(name); }

    /// <summary>
    /// SkillSlot::vfunc_18 @0x14175d530 (skillslot.cpp:507). A fixed or key bound slot only pushes both again. Else by the source:
    /// - another quick slot: <see cref="Skills.QuickSlotEdit.Add"/> of the source's entry on this slot; when taken the two
    ///   slots swap their own entries, and with a skill set (FUN_140930d70) SetQuickSlotEntry writes both to the page;
    /// - a book slot (<see cref="SkillBookTreeController.BookSkillId"/>, BookSlot::FUN_140a366a0): Add of the skill alone; when
    ///   taken this slot holds it (the page is not written);
    /// - a bag slot (InvenSlot, <see cref="IInvenSlotOwner.InvenSlotUid"/>): <see cref="Skills.QuickSlotEdit.AddFromInventory"/>;
    ///   when taken this slot holds its entry, written to the page only with a skill set.
    /// Outside a skill set the page changes only by the server's KeyTable mode 0 / 7 (<see cref="OnShortkeyPageNotify"/>).
    /// Then the source and this slot are pushed again (vfunc_10).
    /// STILL-OPEN: a lapenshard ItemSlot source.
    /// </summary>
    public void SlotDroppedOn(string name, string source, IDragSlotOwner sourceOwner)
    {
        int i = SlotIndex(name);
        if (i >= 0 && !IsFixedOrBound(i) && Model.ServerKeys is { } t)
        {
            int page = t.CurrentPage;                                           // CKeyTable::FUN_140a700b0
            var w = World();
            if (sourceOwner == this && SlotIndex(source) is var j and >= 0)
            {
                var from = Shown(j);
                var to = Shown(i);
                if (Skills.QuickSlotEdit.Add(w, Model.IsFeatureEnabled, Send, Notice, from.SkillId, from.ItemId, from.ItemUid, page, i))
                {
                    slotFields[j] = to;                                         // skillslot.cpp:516-530
                    slotFields[i] = from;
                    if (w.SkillSetActive)                                       // :531-545
                    {
                        t.SetEntry(t.CurrentPage, i, from);
                        t.SetEntry(t.CurrentPage, j, to);
                    }
                }
            }
            else if (sourceOwner is SkillBookTreeController book)
            {
                int skill = (int)book.BookSkillId(source);
                if (Skills.QuickSlotEdit.Add(w, Model.IsFeatureEnabled, Send, Notice, skill, 0, 0, page, i))
                    slotFields[i] = new Ms2.Net.Packets.QuickSlotEntry(skill, 0, 0);
            }
            else if (sourceOwner is IInvenSlotOwner bag && bag.InvenSlotUid(source) is long uid)
            {
                if (Skills.QuickSlotEdit.AddFromInventory(w, Model.IsFeatureEnabled, Send, Notice, NoticeBox, uid, page, i) is { } e)
                {
                    slotFields[i] = e;                                          // skillslot.cpp:590-604
                    if (w.SkillSetActive) t.SetEntry(t.CurrentPage, i, e);
                }
            }
            // STILL-OPEN: the lapenshard ItemSlot source (skillslot.cpp:612-640, FUN_140a6fef0) has no owner here.
        }
        sourceOwner?.RefreshSlot(source);
        RefreshSlot(name);
    }

    /// <summary>The open FUN_1422aeed0(2) pick between vfunc_12 and vfunc_17 (<see cref="IDragSlotOwner.SlotDropUnresolved"/>):
    /// vfunc_12, the slot pushed again. STILL-OPEN: the vfunc_17 branch (<see cref="RemoveSlot"/>) is not reached.</summary>
    public void SlotDropUnresolved(string name) => RefreshSlot(name);

    /// <summary>
    /// SkillSlot::vfunc_14 @0x14175d260 (skillslot.cpp:454) then vfunc_17 @0x14175d2b0 (:463), the right click: a fixed or key
    /// bound slot does nothing; else <see cref="Skills.QuickSlotEdit.Remove"/> of its own entry; refused, the slot is pushed again
    /// (vfunc_12); with a skill set the page slot FindQuickSlotBySkillAndItem @0x14142e150 finds is emptied; then the slot empties
    /// (vfunc_11) and is pushed. After the mode 5 send, CKeyTable::FUN_14142f470 (<see cref="Skills.QuickSlotEdit.PutBackJobItem"/>)
    /// may put the bag's job item of slot 8 or 9 back on the page (and on the slot when the page is the current one); it reads
    /// the shown slots before the slot empties, and vfunc_11 still empties the shown slot until the next page notify.
    /// vfunc_14 runs the icon tween (<see cref="StartIconTween"/>) after the fixed or bound check, before vfunc_17.
    /// </summary>
    public void RemoveSlot(int i)
    {
        if (i < 0 || i >= SlotCount || IsFixedOrBound(i)) return;
        StartIconTween(i);                                                      // skillslot.cpp:458
        if (Model.ServerKeys is not { } t) return;
        int page = t.CurrentPage;
        var e = Shown(i);
        var w = World();
        if (!Skills.QuickSlotEdit.Remove(w, Send, e.SkillId, e.ItemId, e.ItemUid, page, i,
                () => Skills.QuickSlotEdit.PutBackJobItem(w, Model.IsFeatureEnabled, Send, Notice, Shown,
                    (slot, entry) => { t.SetEntry(page, slot, entry); if (page == t.CurrentPage) slotFields[slot] = entry; }, page, i)))
        {
            Host.Push(SlotBag + "." + i);                                       // vfunc_12
            return;
        }
        if (w.SkillSetActive)                                                   // skillslot.cpp:470-480
        {
            int found = t.FindQuickSlotBySkillAndItem(e.SkillId, e.ItemId, e.ItemUid);
            if (found != -1) t.SetEntry(t.CurrentPage, found, default);
        }
        slotFields[i] = default;                                                // vfunc_11
        Host.Push(SlotBag + "." + i);
    }

    /// <summary>A clicked slot's key press (WorldService::FUN_141bc9fd0: the key queued as pressed).</summary>
    public Action<int> PressKey = _ => { };
    /// <summary>A clicked slot's skill without a key (FUN_14092f4e0, <see cref="Skills.QuickSlotEdit.UseSkill"/>).</summary>
    public Action<uint> UseSkill = _ => { };
    /// <summary>GetKeyState(VK_SHIFT) &amp; 0x8000.</summary>
    public Func<bool> ShiftHeld = () => false;
    /// <summary>ShowSystemNotice(GetStringCommonByKey(key), 1, 1).</summary>
    public Action<string> NoticeText = _ => { };

    /// <summary>
    /// SkillSlot::vfunc_13 @0x14175cdd0 (skillslot.cpp:399), the click to use: <see cref="Skills.SkillSlotState.Click"/> of the
    /// current page's entry with the slot's key (<see cref="Hotkey"/>), then the slot pushed again (vfunc_10). The icon tween
    /// (<see cref="StartIconTween"/>) comes first.
    /// </summary>
    public void UseSlot(int i)
    {
        if (i < 0 || i >= SlotCount) return;
        StartIconTween(i);                                                      // skillslot.cpp:401
        var e = Shown(i);
        var click = new Skills.SkillSlotState(World(), (uint)e.SkillId, e.ItemId, e.ItemUid).Click(Hotkey(i));
        switch (click.Kind)
        {
            case Skills.SlotClickKind.Notice: Notice(click.Value); break;
            case Skills.SlotClickKind.NoticeText: NoticeText(click.TextKey); break;
            case Skills.SlotClickKind.Press: PressKey(click.Value); break;
            case Skills.SlotClickKind.Cast: UseSkill((uint)click.Value); break;
        }
        Host.Push(SlotBag + "." + i);
    }

    /// <summary>
    /// NotifyDragSlot::SlotUse @0x142302210 (notifydragslot.cpp:33) for a quick slot: button 0 without shift (GetKeyState(VK_SHIFT),
    /// <see cref="ShiftHeld"/>) is vfunc_13 (<see cref="UseSlot"/>),
    /// button 1 (the right click) vfunc_14 (<see cref="RemoveSlot"/>), SkillSlot keeping DragSlot's vfunc_19 (0).
    /// </summary>
    bool SlotUse(object[] args)
    {
        int i = SlotIndex(args.ElementAtOrDefault(0) as string);
        if (i < 0) return false;
        int button = args.Length < 2 ? 0 : Convert.ToInt32(args[1], System.Globalization.CultureInfo.InvariantCulture);
        if (button == 0) { if (!ShiftHeld()) UseSlot(i); }
        else if (button == 1) RemoveSlot(i);
        else return false;
        return true;
    }

    /// <summary>Raised when the player switches quick slot pages (onNextPage / onPrevPage -> CKeyTable::SetQuickSlotPage -> SendKeyTableMode8, keytable.cpp:2558).</summary>
    public event Action<int> QuickSlotPageChanged;

    /// <summary>FUN_140930d70 (uimainplayerinfo.cpp:1488-1489, 1504-1505; unit_14092f420_accountsessionstate.cpp:865-871):
    /// true when the local PC is in skill-set mode, blocking quick-slot page changes. STILL-OPEN: skill-set state is not ported.</summary>
    public Func<bool> HasSkillSet;

    /// <summary>Syncs the displayed 1-based page number from <see cref="UiModel.ServerKeys"/> (SetShortkeyPageNumber, uimainplayerinfo.cpp:1202).</summary>
    public void SyncQuickSlotPage()
    {
        if (Model.ServerKeys is { Loaded: true } t) ShortkeyPage = t.CurrentPage + 1;
        if (Host?.Entity != null) Host.Invoke("setShortkeyPageNumber", ShortkeyPage);
    }

    public override bool OnCallback(string name, object[] args)
    {
        switch (name)
        {
            case "notifySlotUse": return SlotUse(args);
            case "onNextPage":
                // UIMainPlayerInfo::OnNextPage (@0x1419f36c0, uimainplayerinfo.cpp:1502-1513)
                if (HasSkillSet?.Invoke() == true) return true;
                if (Model.ServerKeys is { Loaded: true } nextTable)
                {
                    if (nextTable.PageCount == 0) return true;
                    int next = (nextTable.CurrentPage + 1) % nextTable.PageCount;
                    nextTable.SetCurrentPage(next);
                    SyncQuickSlotPage();
                    RebindSlots();
                    QuickSlotPageChanged?.Invoke(next);
                }
                else { ShortkeyPage++; Host.Invoke("setShortkeyPageNumber", ShortkeyPage); }
                return true;
            case "onPrevPage":
                // UIMainPlayerInfo::OnPrevPage (@0x1419f3630, uimainplayerinfo.cpp:1486-1499)
                if (HasSkillSet?.Invoke() == true) return true;
                if (Model.ServerKeys is { Loaded: true } prevTable)
                {
                    if (prevTable.PageCount == 0) return true;
                    int prev = prevTable.CurrentPage - 1;
                    if (prev < 0) prev += prevTable.PageCount;
                    prevTable.SetCurrentPage(prev);
                    SyncQuickSlotPage();
                    RebindSlots();
                    QuickSlotPageChanged?.Invoke(prev);
                }
                else if (ShortkeyPage > 1) { ShortkeyPage--; Host.Invoke("setShortkeyPageNumber", ShortkeyPage); }
                return true;
            case "onRideOff": return true;
            case "OnToggleSkillExpansionSlot": SkillMinimize = !SkillMinimize; return true;
            case "OnToggleCommonExpansionSlot": CommonMinimize = !CommonMinimize; return true;
        }
        return false;
    }
}
