using Ms2.Net.Packets;

namespace Ms2.Ui.Skills;

/// <summary>
/// AdditionalCli (client/ms2/additionalcli_1.cpp): one additional effect on the local player, the parts the buff strip reads.
/// The effects, stance bar, shield bar and skill sets the client also drives from it are not here.
/// </summary>
public sealed class AdditionalBuff
{
    /// <summary>m_nKey (the effect's object id) and m_nCasterId (SetOwnerInfo @0x14096c2a0).</summary>
    public int Key, CasterId;
    /// <summary>The owner header's object id.</summary>
    public int OwnerId;
    /// <summary>ApplyState @0x14096c2e0: the start and end server ticks, id, level, overlap count and enabled flag.</summary>
    public uint StartTick, EndTick, Id;
    public ushort Level;
    public int OverlapCount;
    public byte Enabled;
    /// <summary>m_nShieldValue.</summary>
    public long Shield;
    /// <summary>m_bSuspended. STILL-OPEN: no packet the port reads sets it, so it stays false.</summary>
    public bool Suspended;
    /// <summary>m_spLevel (SkillDataTableCli::FUN_140aafef0 of the id and level).</summary>
    public AdditionalLevelData LevelData;
    readonly AdditionalBuffList list;

    internal AdditionalBuff(AdditionalBuffList list) => this.list = list;

    /// <summary>IsValid (:313): an owner and a level.</summary>
    public bool IsValid => LevelData != null;

    /// <summary>GetRemainTicks @0x14096d2e0: 0 without an end tick or past it, else the ticks left.</summary>
    public uint RemainTicks
    {
        get
        {
            if (EndTick == 0) return 0;
            uint now = list.ServerTick();
            return EndTick < now ? 0 : EndTick - now;
        }
    }

    /// <summary>FUN_14096d330: the end tick minus the start tick.</summary>
    public uint Duration => EndTick - StartTick;

    /// <summary>GetSortOrder @0x14096d350: 0 without a level; +20 with a UI priority, else +10 for keep condition 99; then +2
    /// when the owner cast it, else +1 when the local player did.</summary>
    public int SortOrder
    {
        get
        {
            if (LevelData == null) return 0;
            int order = 0;
            if (LevelData.Priority != 0) order += 20;
            else if (LevelData.IsKeepCondition99) order += 10;
            if (CasterId == OwnerId) order += 2;
            else if (list.IsUpperTargetId(CasterId)) order += 1;
            return order;
        }
    }

    /// <summary>IsActive @0x14096d7a0: not suspended, and for keep condition 99 the enabled flag.</summary>
    public bool IsActive => !Suspended && (!LevelData.IsKeepCondition99 || Enabled != 0);

    internal void ApplyState(BuffState s)
    {
        StartTick = s.StartTick;
        EndTick = s.EndTick;
        Id = s.Id;
        Level = s.Level;
        OverlapCount = s.OverlapCount;
        Enabled = s.Enabled;
    }
}

/// <summary>The panel the local player's buffs show on: UIMainPlayerInfo's AddBuffEffect, UpdateBuffEffect, RemoveBuffEffect and
/// FUN_1419f37f0 (the quick slot refresh).</summary>
public interface IBuffStrip
{
    void AddBuffEffect(AdditionalBuff buff);
    void UpdateBuffEffect(AdditionalBuff buff);
    void RemoveBuffEffect(int key);
    void QueueSlotRefresh(int flags);
}

/// <summary>
/// The local player's AdditionalCliList (additionalcli_2.cpp) as OnBuff (scenestategame.cpp:7591) and the own FieldAddUser fill
/// it. Only the buff strip parts are ported: the effects, costume sets, tail effects, skill sets, guild buffs and the shield
/// bar are not.
/// </summary>
public sealed class AdditionalBuffList
{
    /// <summary>m_map, a CAtlMap by key. STILL-OPEN: ClearAll walks the CAtlMap's hash order; here it is the add order.</summary>
    readonly Dictionary<int, AdditionalBuff> map = new();
    readonly Func<uint, ushort, AdditionalLevelData> levelOf;

    /// <summary>GetServerTick.</summary>
    public Func<uint> ServerTick = () => 0;
    /// <summary>g_upperTargetId: the first i32 of ServerEnter (CSceneStateGame::OnServerEnter, scenestategame.cpp:2071).</summary>
    public int UpperTargetId;
    /// <summary>FindMainPlayerInfo(20063); null = no panel.</summary>
    public IBuffStrip Strip;

    /// <param name="levelOf">SkillDataTableCli::FUN_140aafef0: the effect level, null when unknown.</param>
    public AdditionalBuffList(Func<uint, ushort, AdditionalLevelData> levelOf) => this.levelOf = levelOf;

    public IReadOnlyCollection<AdditionalBuff> Buffs => map.Values;

    /// <summary>IsUpperTargetId @0x140930110 (unit_14092f420_accountsessionstate.cpp:523): <c>g_upperTargetId == nValue</c>.</summary>
    public bool IsUpperTargetId(int objectId) => UpperTargetId == objectId;

    /// <summary>
    /// IsPvpOnOff @additionalcli_2.cpp:563, m_bPvpOnOff as AdditionalCliList::FUN_140972a00 (:1059-1082) sums it: set when an
    /// effect is enabled (m_bEnabled, the state's flag), not suspended and its level has pvpOnOff. The client sums on the
    /// list's Update only when m_bDirty is set (additionalcli_2.cpp:450); this reads the held effects at the call. That gives
    /// the same set only while every change goes through SetDirty. STILL-OPEN: the battle-riding suspension
    /// (additionalcli_1.cpp:276-285) and FUN_14096db40 (additionalcli_2.cpp:687, :694) do not set it, so the client's sums
    /// (this one, <see cref="ForceApplyTarget"/>, <see cref="IsHidden"/>) stay stale until the next buff packet; neither is
    /// ported, so they agree today.
    /// </summary>
    public bool IsPvpOnOff
    {
        get
        {
            foreach (var b in map.Values)
                if (b.Enabled != 0 && !b.Suspended && b.LevelData.PvpOnOff) return true;
            return false;
        }
    }

    /// <summary>The list is the own player's (AdditionalCliList::GetOwnerMyPC is set): only then the special effects run
    /// (<see cref="ReduceSkillGroupCooltime"/>).</summary>
    public bool OwnerIsMyPc;
    /// <summary>FUN_140768090 @0x140768090 (questmasktable.cpp:485): the roll in [0, 1) a special effect's probability is
    /// tested against.</summary>
    public Func<float> Roll = Random.Shared.NextSingle;

    /// <summary>
    /// The cooldown group reduce of FUN_1417664e0 (skillslot.cpp:2279-2284): a CSEReduceSkillGroupCooltimeCtx of
    /// <paramref name="groupId"/> through AdditionalCliList::FUN_140972380 @0x140972380 (additionalcli_2.cpp:956). Only on the own
    /// player's list; each enabled effect (FUN_14096d050: a level, enabled or ticks left; suspension is not tested) whose level's
    /// special effect is kind 4 and whose probability is not under the roll runs CSEReduceSkillGroupCooltime::vfunc_1
    /// @0x1408275a0 (specialeffectfunctionmanager.cpp:26): a matching "groupID" adds its "reduceRate".
    /// </summary>
    public float ReduceSkillGroupCooltime(uint groupId)
    {
        if (!OwnerIsMyPc) return 0f;
        float rate = 0f;
        foreach (var b in map.Values)
        {
            var lv = b.LevelData;
            if (b.Enabled == 0 || lv == null || lv.SpecialEffectType != 4) continue;
            if (lv.SpecialEffectProbability < Roll()) continue;
            if ((uint)lv.ValueInt("groupID") != groupId) continue;
            rate += lv.ValueFloat("reduceRate");
        }
        return rate;
    }

    /// <summary>
    /// The skill stat option <paramref name="type"/> of <paramref name="skillId"/> (in skill group <paramref name="skillGroupId"/>)
    /// as CEquipment::FUN_1411e1250 / FUN_1411e13e0 (equipment.cpp:751, :770) read it from the stats
    /// AdditionalCliList::CollectInvokeEffectStats @0x140971720 (additionalcli_2.cpp:851) builds: each enabled effect with a
    /// level (FUN_14096d050) and an invoke effect (FUN_140192ac0) adds, for each listed type that is a skill option
    /// (FUN_14081d8a0: 0x3e9-0x3ec), its value and rate times the overlap count, when its skillID is the skill or, without one
    /// and without an effectID, its skillGroupID is the skill's group. The first adds set the option; later ones add only
    /// when their count is over 0 or their rate over epsilon (InvokeEffectOption::Accumulate @0x140267220). Returns the count
    /// (GetCount) and the rate (GetAmount) in <paramref name="amount"/>.
    /// STILL-OPEN: the client collects only when the equipment refreshes (CEquipment::FUN_1411e7320, equipment.cpp:3054), so
    /// its stats can lag the buffs; this reads the held effects at the call. The effect options (effectID / effectGroupID,
    /// FUN_14081d8e0) have no reader here.
    /// </summary>
    public int SkillInvokeOption(int type, uint skillId, uint skillGroupId, out float amount)
    {
        amount = 0f;
        if (type < 0x3e9 || type > 0x3ec) return 0;
        int count = 0;
        bool set = false;
        foreach (var b in map.Values)
        {
            var lv = b.LevelData;
            if (b.Enabled == 0 || lv == null || !lv.HasInvokeEffect) continue;
            bool hit = lv.InvokeSkillId != 0 ? lv.InvokeSkillId == skillId
                : lv.InvokeEffectId == 0 && lv.InvokeSkillGroupId != 0 && skillGroupId != 0 && lv.InvokeSkillGroupId == skillGroupId;
            if (!hit) continue;
            for (int i = 0; i < lv.InvokeTypes.Length; i++)
            {
                if (lv.InvokeTypes[i] != type) continue;
                int c = (i < lv.InvokeValues.Length ? lv.InvokeValues[i] : 0) * b.OverlapCount;
                float r = (i < lv.InvokeRates.Length ? lv.InvokeRates[i] : 0f) * b.OverlapCount;
                if (!set) { count = c; amount = r; set = true; }
                else if (c > 0 || r > float.Epsilon) { count += c; amount += r; }
            }
        }
        return count;
    }

    /// <summary>
    /// GetForceApplyTarget @0x140970b10 (additionalcli_2.cpp:593), m_nForceApplyTarget as FUN_140972a00 (:1096) sums it: the OR
    /// of the levels' forceApplyTarget over the enabled, unsuspended effects.
    /// </summary>
    public int ForceApplyTarget
    {
        get
        {
            int bits = 0;
            foreach (var b in map.Values)
                if (b.Enabled != 0 && !b.Suspended) bits |= b.LevelData.ForceApplyTarget;
            return bits;
        }
    }

    /// <summary>
    /// IsHidden @0x140970990 (additionalcli_2.cpp:537), m_motion.m_nHide as FUN_140972a00 (:1072) sums it: set when an enabled,
    /// unsuspended effect's level has a nonzero &lt;Motion hide&gt;.
    /// </summary>
    public bool IsHidden
    {
        get
        {
            foreach (var b in map.Values)
                if (b.Enabled != 0 && !b.Suspended && b.LevelData.Hide != 0) return true;
            return false;
        }
    }

    /// <summary>OnBuff's three modes for the local player.</summary>
    public void Apply(BuffPacket p)
    {
        switch (p.Mode)
        {
            case BuffPacket.Add: Add(p); break;
            case BuffPacket.Remove: Remove(p.Key); break;
            case BuffPacket.Modify: Modify(p); break;
        }
    }

    /// <summary>Mode 0: AdditionalCli::FUN_14096be10 (owner, state, value, then the level), then AdditionalCliList::Add
    /// @0x14096f070: nothing for an invalid effect or a key already held; else kept, AddBuffUI, and FUN_1419f37f0(0xd).</summary>
    public bool Add(BuffPacket p)
    {
        var b = new AdditionalBuff(this) { OwnerId = p.OwnerObjectId, Key = p.Key, CasterId = p.CasterObjectId };
        if (p.State is { } s) b.ApplyState(s);
        b.Shield = p.Shield ?? 0;
        b.LevelData = levelOf(b.Id, b.Level);
        if (!b.IsValid || map.ContainsKey(b.Key)) return false;
        map[b.Key] = b;
        AddBuffUI(b);
        Strip?.QueueSlotRefresh(0xd);
        return true;
    }

    /// <summary>Mode 1: Remove @0x14096f500: RemoveBuffUI, drop it, FUN_1419f37f0(0xd).</summary>
    public void Remove(int key)
    {
        if (!map.Remove(key, out var b)) return;
        RemoveBuffUI(b);
        Strip?.QueueSlotRefresh(0xd);
    }

    /// <summary>
    /// Mode 2: DecodeModifyPacket @0x14096f820. A key not held with flag 1 decodes into a new effect that is never kept (the
    /// client's UpdateBuffUI still runs for it, and its strip has no such key). AdditionalCli::DecodeModifyPacket @0x14096bf40:
    /// the owner header, then flag 1 the state (its level must exist, else nothing more), flag 2 the shield. After flag 1 a
    /// valid effect gets UpdateBuffUI.
    /// </summary>
    public void Modify(BuffPacket p)
    {
        if (!map.TryGetValue(p.Key, out var b))
        {
            if ((p.Flags & BuffPacket.ModifyState) == 0) return;
            b = new AdditionalBuff(this);
        }
        b.OwnerId = p.OwnerObjectId;
        b.Key = p.Key;
        b.CasterId = p.CasterObjectId;
        if ((p.Flags & BuffPacket.ModifyState) != 0 && p.State is { } s)
        {
            if (levelOf(s.Id, s.Level) is not { } level) return;
            b.LevelData = level;
            b.ApplyState(s);
        }
        if ((p.Flags & BuffPacket.ModifyValue) != 0) b.Shield = p.Shield ?? 0;
        if (!b.IsValid) return;
        if ((p.Flags & BuffPacket.ModifyState) != 0) UpdateBuffUI(b);
    }

    /// <summary>ClearAll @0x140970490: RemoveBuffUI for each, then empty.</summary>
    public void ClearAll()
    {
        foreach (var b in map.Values) RemoveBuffUI(b);
        map.Clear();
    }

    /// <summary>Gives every held effect to a panel that was made after them (AddBuffUI again).</summary>
    public void ReplayTo(IBuffStrip strip)
    {
        foreach (var b in map.Values)
            if (b.LevelData.ShowIcon(IsUpperTargetId(b.CasterId))) strip.AddBuffEffect(b);
    }

    /// <summary>AddBuffUI @0x140973950: the UI property shows the icon for the caster (FUN_1401927c0) and the owner is the
    /// local player: UIMainPlayerInfo::AddBuffEffect.</summary>
    void AddBuffUI(AdditionalBuff b)
    {
        if (b.LevelData.ShowIcon(IsUpperTargetId(b.CasterId))) Strip?.AddBuffEffect(b);
    }

    /// <summary>RemoveBuffUI @0x140973cc0: UIMainPlayerInfo::RemoveBuffEffect(key), whatever the icon state.</summary>
    void RemoveBuffUI(AdditionalBuff b) => Strip?.RemoveBuffEffect(b.Key);

    /// <summary>AdditionalCli::UpdateBuffUI @0x14096d430: UpdateBuffEffect when the icon shows for the caster.</summary>
    void UpdateBuffUI(AdditionalBuff b)
    {
        if (b.LevelData.ShowIcon(IsUpperTargetId(b.CasterId))) Strip?.UpdateBuffEffect(b);
    }
}
