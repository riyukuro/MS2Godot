using System.Globalization;
using Ms2.Avm2;
using Ms2.Quest;
using Ms2.Ui.Items;
using Ms2.Ui.Skills;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIAdventureLevelDialog (UI id 0x4f67, ms2/client/ms2/uiadventureleveldialog.cpp): the game menu's adventure level
/// (prestige) dialog, with feature 0xfb (the exp bar's OnClickBar and the game menu open it; without the feature the
/// bar opens the older 0x4f87). Its level guide lists the level rewards (an item or stat points) with their claim
/// state, the ability list the adventure abilities with their step, and the mission list the server's missions with
/// their item reward.
/// </summary>
/// <remarks>
/// The player's adventure data (DAT_143ab2958 exp, DAT_143ab3308 the claimed levels, DAT_143ab3318 the missions) is
/// <see cref="PlayerState"/>'s, filled by the Prestige packet (CSceneStateGame::OnPrestige); vfunc_141 is
/// <see cref="PlayerState.AdventureLevel"/>.
/// STILL-OPEN: the client's GetCurrent() is dereferenced without a check; with no current ability (no level 1 effect of
/// eventBuffType 3) every ability without an enabled buff is locked here.
/// </remarks>
public sealed class AdventureLevelController : UiController
{
    public override string TypeName => "AdventureLevelDialog";      // GetTypeName (@0x140ba2ba0)
    public override string Movie => "uiadventureleveldialog";
    protected override string EntityMethod => "setLevelAndExp";

    public AdventureLevelTable Table = new();
    /// <summary>AdditionalCliList::FUN_140970020(effect, level, 0, 1) over the player's own buffs: an enabled buff of the
    /// effect at that level.</summary>
    public Func<uint, ushort, bool> HasEnabledBuff = (_, _) => false;

    /// <summary>Prestige mode 3 with the level (<see cref="Ms2.Net.Packets.PrestigeLevelRewardRequest"/>).</summary>
    public event Action<int> SendClaimLevel;
    /// <summary>Prestige mode 5 with the mission (<see cref="Ms2.Net.Packets.PrestigeMissionRewardRequest"/>).</summary>
    public event Action<int> SendClaimMission;

    readonly List<string> binds = new();       // m_binds

    /// <summary>vfunc_67 (@0x140ba2c50): the status listener removed, the bound slots destroyed (the names stay listed,
    /// so the next ones count on), then UIDialog's.</summary>
    public override void Close()
    {
        foreach (var b in binds) Model.ItemSlots.Remove(b);      // DestroyItemInfoSlot
        base.Close();
    }

    /// <summary>OnLoadComplete (@0x140ba2d00): UIDialog's; with a local player the level guide, the level and exp, the
    /// abilities and the missions.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (!Model.HasLocalPlayer) return;
        FillLevelGuide();
        SetLevelAndExp();
        FillAbilityList();
        FillMissionList();
    }

    /// <summary>vfunc_28 (@0x140ba2d70), after UIDialog's: onReceiveItem (PopUInt cut to an unsigned short) and
    /// onReceiveMissionReward (PopUInt).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (name.Equals("onReceiveItem", StringComparison.OrdinalIgnoreCase))
        {
            if (args is { Length: > 0 }) ClaimLevelReward((ushort)Rt.ToUint32(args[0]));
        }
        else if (name.Equals("onReceiveMissionReward", StringComparison.OrdinalIgnoreCase))
        {
            if (args is { Length: > 0 }) ClaimMissionReward(Rt.ToUint32(args[0]));
        }
        else return false;
        return true;
    }

    /// <summary>
    /// vfunc_49 (@0x140ba2ee0): with a local player, the ability of the id's effect at its step (or at level 1) as a buff
    /// tooltip, ASTooltip(ret, 2).FUN_1409e2750(attr, 6, 0); none for an unknown ability.
    /// </summary>
    public override IDictionary<string, object> GetTooltip(object[] args)
    {
        if (!Model.HasLocalPlayer) return null;
        uint id = args is { Length: > 0 } ? Rt.ToUint32(args[0]) : 0;
        var a = Table.GetAbility(id);
        if (a.EffectId == 0) return null;
        var attr = Attr(a, a.GetCount(Level));
        return attr == null ? null : BuffTooltip.Build(Model.Tables, attr, 6, 0);
    }

    /// <summary>
    /// The bound slots' ITooltip::vfunc_0 through NotifyDragSlot::GetSlotTooltip: a stat reward's text tooltip
    /// (ItemInfoSlot vfunc_0 :314, align 4), an item's FUN_1409e0d40 with the slot's flags (0x800); null for a name this
    /// dialog did not bind, an empty object when the slot gives none.
    /// </summary>
    public override IDictionary<string, object> GetSlotTooltip(string name)
    {
        if (!binds.Contains(name) || !Model.ItemSlots.TryGetValue(name, out var slot)) return null;
        if (slot.Item == null) return slot.TextTooltip() ?? new AsTooltip();
        if (!Model.HasLocalPlayer) return new AsTooltip();
        var tip = new ItemTooltip(Model, slot.Flags);
        return tip.Fill(slot.Item) ? tip.Data : new AsTooltip();
    }

    /// <summary>
    /// The status listener: vfunc_oa0_30 (@0x140ba30a0, event 0x11, the adventure level) refreshes the level guide, the
    /// level and exp and the abilities; vfunc_oa0_31 (@0x140ba3140, event 0x12, the exp) the level and exp. Neither runs
    /// while closed or before the movie loads.
    /// </summary>
    public override void OnStatus(StatusEvent e)
    {
        if (IsClosed || Host?.Entity == null) return;
        if (e == StatusEvent.AdventureLevel)
        {
            FillLevelGuide();
            SetLevelAndExp();
            FillAbilityList();
        }
        else if (e == StatusEvent.AdventureExp) SetLevelAndExp();
    }

    /// <summary>RefreshLevelGuide (@0x140ba31c0): the level guide once the movie is loaded (OnPrestige kind 4).</summary>
    public void RefreshLevelGuide()
    {
        if (Host?.Entity == null) return;
        FillLevelGuide();
    }

    /// <summary>RefreshMissionList (@0x140ba3200): the missions once the movie is loaded (OnPrestige kinds 6, 7).</summary>
    public void RefreshMissionList()
    {
        if (Host?.Entity == null) return;
        FillMissionList();
    }

    /// <summary>GetClaimableMissionCount (@0x140ba3240): the missions not received whose progress reached the count.</summary>
    public static int GetClaimableMissionCount(AdventureLevelTable table, PlayerState pc)
    {
        int n = 0;
        foreach (var (id, data) in pc.AdventureMissions)
        {
            if (data.Complete) continue;
            var m = table.GetMission(id);
            if (ReferenceEquals(m, AdventureLevelTable.EmptyMission)) continue;
            if (data.Progress < m.Count) continue;
            n++;
        }
        return n;
    }

    /// <summary>vfunc_141: the player's adventure level.</summary>
    uint Level => (uint)Pc.AdventureLevel;

    /// <summary>FUN_140aafef0(effect, step), else at level 1.</summary>
    AdditionalLevelData Attr(AdventureLevelTable.Ability a, ushort step) =>
        Model.Tables.Additional.GetLevel(a.EffectId, step) ?? Model.Tables.Additional.GetLevel(a.EffectId, 1);

    /// <summary>
    /// FillLevelGuide (@0x140ba3330): each level of the reward table (the old entries stay; addLevelGuide updates an
    /// entry of the same id): its row, then an item reward, else a stat reward; a level with neither is not added.
    /// </summary>
    void FillLevelGuide()
    {
        if (Host?.Entity == null) return;
        foreach (uint l in Table.Levels)
        {
            ushort level = (ushort)l;
            var obj = new List<(string, object)>();
            if (!GuideRow(obj, level)) continue;
            if (!BindItemReward(obj, level) && !BindStatReward(obj, level)) continue;
            Host.Invoke("addLevelGuide", ObjOf(obj));
        }
    }

    /// <summary>FUN_140ba34b0 (@0x140ba34b0): setLevelAndExp(level, exp / AdventureLevelLvUpExp), both as floats.</summary>
    void SetLevelAndExp()
    {
        if (Host?.Entity == null || !Model.HasLocalPlayer) return;
        float ratio = (float)Pc.AdventureExp / (float)Model.Tables.AdventureLevelLvUpExp;
        Host.Invoke("setLevelAndExp", Level, ratio);
    }

    /// <summary>
    /// FillAbilityList (@0x140ba35a0): m_abilityList reset (resetRenderer, FUN_1422dd1d0), then each ability in
    /// required-level order with an effect at its step (or level 1): id, isLock (below the required level, or no enabled
    /// buff of the step when it is not the current ability), imgPath, abilityStatus (GetPercent), and level (the
    /// required level when locked, else the step's start level below the last step); vfunc_24(true).
    /// </summary>
    void FillAbilityList()
    {
        if (Host?.Entity == null || !Model.HasLocalPlayer) return;
        if (Host.Child("m_abilityList") is not { } list) return;
        var dp = Reset(list);
        uint level = Level;
        foreach (uint id in Table.AbilityIds)
        {
            var a = Table.GetAbility(id);
            ushort step = a.GetCount(level);
            var attr = Attr(a, step);
            if (attr == null) continue;
            bool locked = level < a.RequireLevel;
            if (!HasEnabledBuff(a.EffectId, step) && a.EffectId != (Table.Current?.EffectId ?? 0)) locked = true;
            var obj = new List<(string, object)>
            {
                ("id", a.EffectId), ("isLock", locked), ("imgPath", attr.Icon ?? ""), ("abilityStatus", a.GetPercent(level)),
            };
            if (locked) obj.Add(("level", a.RequireLevel));
            else if (step < a.MaxCount) obj.Add(("level", (uint)a.GetLevelOfCount(level)));
            if (!Rt.IsNullish(dp)) Host.Invoke(dp, "push", ObjOf(obj));     // AddItem
        }
        Validate(list);
    }

    /// <summary>
    /// FillMissionList (@0x140ba3a30): m_missionList reset, then each of the player's missions with a table row: id, the
    /// item reward (FUN_140ba45c0), missionName (s_adventure_level_mission_NameKey with the count), received and
    /// completed (progress at the count); vfunc_24(true).
    /// </summary>
    void FillMissionList()
    {
        if (Host?.Entity == null || !Model.HasLocalPlayer) return;
        if (Host.Child("m_missionList") is not { } list) return;
        var dp = Reset(list);
        foreach (var (id, data) in Pc.AdventureMissions)
        {
            var m = Table.GetMission(id);
            if (ReferenceEquals(m, AdventureLevelTable.EmptyMission)) continue;
            var obj = new List<(string, object)> { ("id", id) };
            BindMissionReward(obj, id);
            obj.Add(("missionName", ClientFormat.Format(Model.Tables.Common("s_adventure_level_mission_NameKey") ?? "", m.Count)));
            obj.Add(("received", data.Complete));
            obj.Add(("completed", data.Progress >= m.Count));
            if (!Rt.IsNullish(dp)) Host.Invoke(dp, "push", ObjOf(obj));
        }
        Validate(list);
    }

    /// <summary>A level row's state (FUN_140ba3d60): "active", "inactive" when its reward is claimed, "lock" above the
    /// player's adventure level.</summary>
    public static string GuideState(PlayerState pc, uint level)
    {
        string state = "active";
        if (pc.AdventureClaimed.Contains(level)) state = "inactive";
        if (level > (uint)pc.AdventureLevel) state = "lock";
        return state;
    }

    /// <summary>
    /// FUN_140ba3d60 (@0x140ba3d60): a level row: id, state ("active"; "inactive" when claimed; "lock" above the
    /// adventure level), level (at least 1) and levelStr (s_adventure_level_level).
    /// </summary>
    bool GuideRow(List<(string, object)> obj, uint level)
    {
        if (Host?.Entity == null || !Model.HasLocalPlayer) return false;
        obj.Add(("id", level));
        obj.Add(("state", GuideState(Pc, level)));
        obj.Add(("level", Math.Max(level, 1u)));
        obj.Add(("levelStr", ClientFormat.Format(Model.Tables.Common("s_adventure_level_level") ?? "", level)));
        return true;
    }

    /// <summary>
    /// FUN_140ba3f80 (@0x140ba3f80): an item reward (type 1) made as a new item (rank as the rarity) with its count, bound
    /// as "AdventureLevelDialog.&lt;n&gt;" (CreateItemInfoSlot, icon type 0x415, flags 0x800).
    /// </summary>
    bool BindItemReward(List<(string, object)> obj, uint level)
    {
        var r = Table.GetReward(level);
        if (ReferenceEquals(r, AdventureLevelTable.EmptyReward) || r.Type != 1) return false;
        if (!Model.HasLocalPlayer) return false;
        var item = Item.Create(Model.Tables.Items, (int)r.Id, r.Rank);     // FUN_14052b130(id, rank, 0)
        if (item == null) return false;
        item.SetCountClamped(r.Value);
        string bind = NextBind();
        Model.ItemSlots[bind] = new ItemInfoSlot(bind, item, 0, 0x415, 0x800);
        obj.Add(("binding", bind));
        binds.Add(bind);
        return true;
    }

    /// <summary>
    /// FUN_140ba4250 (@0x140ba4250): a stat point reward (type 2): the state set to "inactive" at or below the adventure
    /// level and to "lock" below AdventureLevelStartLevel (FUN_1409c0dc0 replaces the member), then a slot without an item
    /// (FUN_141415560): the stat point icon (CommonResource m_strStatPointIconPath), the tip s_adventure_level_stat_reward with the value,
    /// flags 1, tip align 4, the value as the count text.
    /// </summary>
    bool BindStatReward(List<(string, object)> obj, uint level)
    {
        var r = Table.GetReward(level);
        if (ReferenceEquals(r, AdventureLevelTable.EmptyReward) || r.Type != 2) return false;
        if (!Model.HasLocalPlayer) return false;
        if (level <= Level) SetMember(obj, "state", "inactive");
        if ((ushort)Pc.Level < (ushort)Model.Tables.AdventureLevelStartLevel) SetMember(obj, "state", "lock");
        string bind = NextBind();
        Model.ItemSlots[bind] = new ItemInfoSlot(bind, Model.Tables.CommonResource(StatPointIconKey),
            ClientFormat.Format(Model.Tables.Common("s_adventure_level_stat_reward") ?? "", r.Value), 1, 4,
            r.Value.ToString(CultureInfo.InvariantCulture));       // Unk_SocketFormat("{0}", value)
        obj.Add(("binding", bind));
        binds.Add(bind);
        return true;
    }

    /// <summary>CommonResource::m_strStatPointIconPath (commonresource.cpp:139): the stat point icon's commonResource.xml key.</summary>
    public const string StatPointIconKey = "stat_point_icon_path";

    /// <summary>
    /// FUN_140ba45c0 (@0x140ba45c0): a mission's item reward (m_nOne 1) made with its count, bound like the level rewards,
    /// and slotName, the item name (string table 1) coloured by the rarity (FUN_14183e9c0).
    /// </summary>
    void BindMissionReward(List<(string, object)> obj, uint missionId)
    {
        var m = Table.GetMission(missionId);
        if (ReferenceEquals(m, AdventureLevelTable.EmptyMission) || m.One != 1) return;
        if (!Model.HasLocalPlayer) return;
        var item = Item.Create(Model.Tables.Items, (int)m.ItemId, m.ItemRank);
        if (item == null) return;
        item.SetCountClamped(m.ItemCount);
        string bind = NextBind();
        Model.ItemSlots[bind] = new ItemInfoSlot(bind, item, 0, 0x415, 0x800);
        obj.Add(("binding", bind));
        obj.Add(("slotName", RankColored(Model.Tables.ItemName(item.Data.Id), item.Rarity)));
        binds.Add(bind);
    }

    /// <summary>FUN_14183e9c0 (texttohtml.cpp:327) through FUN_14183e930: &lt;FONT color='#%x'&gt;text&lt;/FONT&gt; with the
    /// grade's GetTextColor.</summary>
    string RankColored(string text, int rank) =>
        $"<FONT color='#{Model.Tables.Items.TextColor(rank).ToString("x", CultureInfo.InvariantCulture)}'>{text}</FONT>";

    /// <summary>ClaimLevelReward (@0x140ba4960): mode 3 for a reward that is not stat points.</summary>
    void ClaimLevelReward(uint level)
    {
        var r = Table.GetReward(level);
        if (ReferenceEquals(r, AdventureLevelTable.EmptyReward) || r.Type == 2) return;
        SendClaimLevel?.Invoke((int)level);
    }

    /// <summary>ClaimMissionReward (@0x140ba4a10): mode 5 for a mission of the table the player has, not received, whose
    /// progress reached the count.</summary>
    void ClaimMissionReward(uint id)
    {
        var m = Table.GetMission(id);
        if (ReferenceEquals(m, AdventureLevelTable.EmptyMission)) return;
        if (!Pc.AdventureMissions.TryGetValue(id, out var data)) return;
        if (data.Complete || data.Progress < m.Count) return;
        SendClaimMission?.Invoke((int)id);
    }

    string NextBind() => string.Create(CultureInfo.InvariantCulture, $"{TypeName}.{binds.Count + 1}");

    static void SetMember(List<(string, object)> obj, string name, object value)
    {
        int i = obj.FindIndex(m => m.Item1 == name);
        if (i >= 0) obj[i] = (name, value);
        else obj.Add((name, value));
    }

    object ObjOf(List<(string, object)> obj) => Host.Obj(obj.ToArray());

    /// <summary>resetRenderer, then the provider's cleanUp (ASCoreList FUN_1422dd1d0); the provider.</summary>
    object Reset(object list)
    {
        Host.Invoke(list, "resetRenderer");
        var dp = Host.Get(list, "dataProvider");
        if (!Rt.IsNullish(dp)) Host.Invoke(dp, "cleanUp");
        return dp;
    }

    /// <summary>ASUIComponent::vfunc_24(true) (asuicomponent.cpp:323): invalidate, then validateNow.</summary>
    void Validate(object list)
    {
        Host.Invoke(list, "invalidate");
        Host.Invoke(list, "validateNow");
    }
}
