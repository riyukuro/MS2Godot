using Ms2.Net.Packets;

namespace Ms2.Ui.Skills;

/// <summary>
/// AdditionalCli (client/ms2/additionalcli_1.cpp): one additional effect on a PC or an npc: the parts the buff strip reads and
/// its motion effects (<see cref="AdditionalBuffList.Effects"/>). The stance bar, shield bar and skill sets the client also
/// drives from it are not here.
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
    /// <summary>m_b40: brought by the field entry's list (DecodeListPacket, additionalcli_2.cpp:730), not added by an OnBuff.</summary>
    public bool FromList;
    /// <summary>m_bSuspended. STILL-OPEN: no packet the port reads sets it, so it stays false.</summary>
    public bool Suspended;
    /// <summary>m_spLevel (SkillDataTableCli::FUN_140aafef0 of the id and level).</summary>
    public AdditionalLevelData LevelData;
    /// <summary>m_nKeepEffect: the kept keep effect's id, 0 for none.</summary>
    public int KeepEffect { get; internal set; }
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
/// What an AdditionalCli's effects go through: CEffectService with a query whose actor is the owner (SetActor), whose object
/// kind is the caster's (WorldService::FUN_141bd19d0) and whose lookup id is the caster's (SetLookupId).
/// </summary>
public interface IBuffMotionEffects
{
    /// <summary>FUN_1411a49c0 with m_b30 set (kept, not culled by sight): the kept effect's id, 0 when it did not spawn.</summary>
    int Keep(string effect, int ownerId, int casterId);
    /// <summary>InsertEffect: played once on the owner, kept by nothing.</summary>
    void Insert(string effect, int ownerId, int casterId);
    /// <summary>FUN_1411a6060: a kept effect stopped at once.</summary>
    void Stop(int id);
    /// <summary>FUN_141b6efc0 of the owner's entity: the owner has a scene node.</summary>
    bool OwnerShown(int ownerId);
}

/// <summary>
/// A PC's or an npc's AdditionalCliList (additionalcli_2.cpp) as OnBuff (scenestategame.cpp:7591) and the FieldAddUser /
/// FieldAddNpc lists fill it. Ported: the buff strip parts and the motion effects (<see cref="Effects"/>, run each frame by
/// <see cref="UpdateEffects"/>); the costume sets, tail effects, skill sets, guild buffs and the shield bar are not.
/// STILL-OPEN in the effects: a portrait PC's list (IsPortraitOwner) is never made here; AdditionalCli::Update's battle riding
/// suspension (m_bSuspended) and FUN_14096db40's immediateActiveRequireSkill disable are not ported; PlayStartEffect and
/// PlayInvokeEffect skip a caster whose GameObject vfunc_25 (+0x40) is set, which is not known here, so they never skip; the
/// IgnoreOptimalHideStartEffect flag goes nowhere, as the port has no optimal hide.
/// Kept as the client has them: a keep effect that never spawns (its xml missing, every part refused, the owner not shown) is
/// tried again each frame, an overlap keep's start row inserted again with each try (AdditionalCli::Update,
/// additionalcli_1.cpp:269); a keep is not culled by the view (m_b30), so a far owner is not such a case. A Modify of a key not
/// held makes an effect that is never listed (DecodeModifyPacket @0x14096f820): a keep its overlap count change spawns is
/// stopped by nothing, as in the client.
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
    /// <summary>The effect service the effects play through; null = none are played.</summary>
    public IBuffMotionEffects Effects;

    /// <param name="levelOf">SkillDataTableCli::FUN_140aafef0: the effect level, null when unknown.</param>
    public AdditionalBuffList(Func<uint, ushort, AdditionalLevelData> levelOf) => this.levelOf = levelOf;

    public IReadOnlyCollection<AdditionalBuff> Buffs => map.Values;

    /// <summary>FUN_14096fc80 @0x14096fc80 (additionalcli_2.cpp:336): the entry of the key (the effect's object id), null for none.</summary>
    public AdditionalBuff Find(int key) => map.GetValueOrDefault(key);

    /// <summary>
    /// FUN_140970020 @0x140970020 (additionalcli_2.cpp:383) through FUN_14096fe60 with bAll clear: an enabled effect of the id,
    /// at the level unless it is 0, whose overlap count passes <see cref="CompareOverlapCount"/> unless the count is 0.
    /// </summary>
    public bool Has(uint id, ushort level, uint count, int compare)
    {
        foreach (var b in map.Values)
        {
            if (b.Enabled == 0 || b.Id != id) continue;
            if (level != 0 && b.Level != level) continue;
            if (count != 0 && !CompareOverlapCount(compare, count, (uint)b.OverlapCount)) continue;
            return true;
        }
        return false;
    }

    /// <summary>FUN_14096fe60 with bAll set (guildmanager.cpp:807): a record of the id counts enabled or not, at the level unless it
    /// is 0; the overlap count is not asked.</summary>
    public bool HasAny(uint id, ushort level)
    {
        foreach (var b in map.Values)
            if (b.Id == id && (level == 0 || b.Level == level)) return true;
        return false;
    }

    /// <summary>AdditionalCliList::HasEnabledWithEventBuffType (additionalcli_2.cpp:407): an enabled effect whose level data has
    /// the event buff type.</summary>
    public bool HasEnabledWithEventBuffType(int type)
    {
        foreach (var b in map.Values)
            if (b.Enabled != 0 && b.LevelData != null && b.LevelData.EventBuffType == (uint)type) return true;
        return false;
    }

    /// <summary>AdditionalCliList::GetOverlapCount (additionalcli_2.cpp:669): the overlap count of the first effect of the id, at the
    /// level unless it is 0, enabled or not; 0 when none.</summary>
    public int OverlapCountOf(uint id, ushort level)
    {
        foreach (var b in map.Values)
            if (b.Id == id && (level == 0 || b.Level == level)) return b.OverlapCount;
        return 0;
    }

    /// <summary>
    /// The overlap test @0x140975ed0 (additionalcli_2.cpp:142), the SkillCompareSign of the count against the effect's overlap:
    /// 1 equal, 2 count above it, 3 count at or above, 4 count below, 5 count at or below; any other sign fails.
    /// </summary>
    public static bool CompareOverlapCount(int compare, uint count, uint overlap) => compare switch
    {
        1 => count == overlap,
        2 => count > overlap,
        3 => count >= overlap,
        4 => count < overlap,
        5 => count <= overlap,
        _ => false,
    };

    /// <summary>
    /// AdditionalCustomCli's ctor @0x14096dcb0 (additionalcli_1.cpp:701): an effect the client makes itself, not held in the
    /// list (UIMainPlayerInfo::AddCustomBuffEffect hands it to the strip). The key; the caster (and owner) is the local
    /// player; the start is the server tick now and the end <paramref name="endTick"/>; the level is the id's level 1, and with
    /// one the overlap count is <paramref name="count"/> capped at its maxBuffCount. The id and level stay 0 and the effect
    /// enabled (the base ctor, :90).
    /// </summary>
    public AdditionalBuff Custom(int key, uint id, uint endTick, uint count, int ownerObjectId)
    {
        var b = new AdditionalBuff(this)
        {
            Key = key, CasterId = ownerObjectId, OwnerId = ownerObjectId, StartTick = ServerTick(), EndTick = endTick,
            OverlapCount = (int)count, Enabled = 1, LevelData = levelOf(id, 1),
        };
        if (b.LevelData != null) b.OverlapCount = (int)Math.Min(b.LevelData.MaxBuffCount, count);
        return b;
    }

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

    /// <summary>IsDisableRevivalHere @0x140970ad0 (additionalcli_2.cpp:581), m_bDisableRevivalHere as FUN_140972a00 (:1100)
    /// sums it, like <see cref="IsPvpOnOff"/>: an enabled, unsuspended effect whose level has disableRevivalHere.</summary>
    public bool IsDisableRevivalHere
    {
        get
        {
            foreach (var b in map.Values)
                if (b.Enabled != 0 && !b.Suspended && b.LevelData.DisableRevivalHere) return true;
            return false;
        }
    }

    /// <summary>The list is the own player's (AdditionalCliList::GetOwnerMyPC is set): only then the special effects run
    /// (<see cref="ReduceSkillGroupCooltime"/>).</summary>
    public bool OwnerIsMyPc;
    /// <summary>CGuildManager::FUN_1413bce40(id, level): an effect added to the own player (Add) or set by a modify with the state
    /// flag (Modify); the guild's pending skill uses (mode 0x5b) wait for it.</summary>
    public event Action<int, int> OwnEffectAdded;
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
    /// GetSpecialAbility @0x140970c70 (additionalcli_2.cpp:844), m_status.m_mapSA[<paramref name="type"/>] as FUN_140972a00
    /// (:1058-1110) sums it: over the enabled, unsuspended effects with a level (FUN_14096d050), each level's
    /// <see cref="AdditionalLevelData.SpecialAbilities"/> value of the type times the overlap count.
    /// </summary>
    public int SpecialAbility(int type)
    {
        int sum = 0;
        foreach (var b in map.Values)
            if (b.Enabled != 0 && !b.Suspended && b.LevelData?.SpecialAbilities is { } sa && sa.TryGetValue(type, out int v))
                sum += v * b.OverlapCount;
        return sum;
    }

    /// <summary>
    /// GetInvincible @additionalcli_2.cpp:587, m_nInvincible as FUN_140972a00 (:1075) sums it: the last enabled, unsuspended
    /// effect (in the map's walk) whose level's invincible is not 0 gives it, else 0.
    /// STILL-OPEN: the client walks the CAtlMap's hash order; here it is the add order (they differ only with two such effects).
    /// </summary>
    public int Invincible
    {
        get
        {
            int kind = 0;
            foreach (var b in map.Values)
                if (b.Enabled != 0 && !b.Suspended && b.LevelData.Invincible != 0) kind = b.LevelData.Invincible;
            return kind;
        }
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
    /// AdditionalCliList::FUN_1409713b0 (additionalcli_2.cpp:786-833), each frame: the first enabled effect whose level has a
    /// &lt;Motion stun&gt;, its stun, the sub StateStun is entered with (<see cref="Ms2.Ui.OwnStun.BuffSub"/>) and its stun
    /// animation; null when none. The owner's checks (alive, the current state's vfunc_17) are the caller's.
    /// STILL-OPEN: m_map is a CAtlMap walked in its hash order; here it is the add order.
    /// </summary>
    public (int Stun, int Sub, string Animation)? StunEntry(bool ownerIsPc)
    {
        foreach (var b in map.Values)
        {
            if (b.Enabled == 0 || b.LevelData.Stun == 0) continue;
            int stun = unchecked((int)b.LevelData.Stun);
            return (stun, Ms2.Ui.OwnStun.BuffSub(stun, ownerIsPc, b.FromList), b.LevelData.StunAnimation);
        }
        return null;
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

    /// <summary>
    /// m_motion.m_nSuperArmor as FUN_140972a00 (additionalcli_2.cpp:1077-1080) sums it: the largest &lt;Motion superArmor&gt;
    /// of the enabled, unsuspended effects that <see cref="SuperArmorApplies"/> lets count in the field.
    /// </summary>
    /// <param name="mapType9">CFieldData::FUN_1403744a0 of the field id: its map type is 9.</param>
    public uint SuperArmor(bool mapType9)
    {
        uint v = 0;
        foreach (var b in map.Values)
        {
            if (b.Enabled == 0 || b.Suspended) continue;
            uint armor = b.LevelData.SuperArmor;
            if (SuperArmorApplies(armor, b.LevelData.SuperArmorApplyFieldType, mapType9) && armor > v) v = armor;
        }
        return v;
    }

    /// <summary>AdditionalCliList::FUN_1409726e0 @0x1409726e0 (additionalcli_2.cpp:1008): no armor never counts, field type 0
    /// counts everywhere, else type 2 counts only on map type 9 and the others only off it.</summary>
    public static bool SuperArmorApplies(uint armor, int applyFieldType, bool mapType9)
    {
        if (armor == 0) return false;
        if (applyFieldType == 0) return true;
        return (applyFieldType == 2) == mapType9;
    }

    /// <summary>OnBuff's three modes for the local player.</summary>
    public void Apply(BuffPacket p)
    {
        switch (p.Mode)
        {
            case BuffPacket.Add: Add(p); break;
            case BuffPacket.Remove: Remove(p.Key, p.CasterObjectId); break;
            case BuffPacket.Modify: Modify(p); break;
        }
    }

    /// <summary>Mode 0: AdditionalCli::FUN_14096be10 (owner, state, value, then the level), then AdditionalCliList::Add
    /// @0x14096f070: nothing for an invalid effect or a key already held; else kept, FUN_14096c4c0 (the start effect),
    /// AddBuffUI, and FUN_1419f37f0(0xd).</summary>
    public bool Add(BuffPacket p)
    {
        var b = new AdditionalBuff(this) { OwnerId = p.OwnerObjectId, Key = p.Key, CasterId = p.CasterObjectId };
        if (p.State is { } s) b.ApplyState(s);
        b.Shield = p.Shield ?? 0;
        b.FromList = p.FromList;
        b.LevelData = levelOf(b.Id, b.Level);
        if (!b.IsValid || map.ContainsKey(b.Key)) return false;
        map[b.Key] = b;
        StartIfNotListed(b);
        AddBuffUI(b);
        if (OwnerIsMyPc) OwnEffectAdded?.Invoke((int) b.Id, b.Level);   // CGuildManager FUN_1413bce40 (additionalcli_2.cpp:216)
        Strip?.QueueSlotRefresh(0xd);
        return true;
    }

    /// <summary>Mode 1: Remove @0x14096f500: StopEffects with the packet's caster, RemoveBuffUI, drop it, FUN_1419f37f0(0xd).</summary>
    public void Remove(int key, int casterId)
    {
        if (!map.TryGetValue(key, out var b)) return;
        StopEffects(b, casterId);
        map.Remove(key);
        RemoveBuffUI(b);
        Strip?.QueueSlotRefresh(0xd);
    }

    /// <summary>
    /// Mode 2: DecodeModifyPacket @0x14096f820. A key not held with flag 1 decodes into a new effect (the ctor's enabled flag
    /// and overlap count 1) that is never kept (the client's start effect and UpdateBuffUI still run for it, and its strip has
    /// no such key). AdditionalCli::DecodeModifyPacket @0x14096bf40: the owner header, then flag 1 the state (its level must
    /// exist, else nothing more): a changed enabled flag of a keep condition 99 effect stops its effects (StopEffects with its
    /// caster) when it was enabled, else restarts the keep effect (RestartKeepEffect); else a changed overlap count of an effect
    /// with an overlap keep effect plays the overlap effects and returns before the shield. Flag 2 the shield. After flag 1 a
    /// valid effect gets FUN_14096c4c0 (the start effect) and UpdateBuffUI.
    /// </summary>
    public void Modify(BuffPacket p)
    {
        if (!map.TryGetValue(p.Key, out var b))
        {
            if ((p.Flags & BuffPacket.ModifyState) == 0) return;
            b = new AdditionalBuff(this) { Enabled = 1, OverlapCount = 1 };
        }
        b.OwnerId = p.OwnerObjectId;
        b.Key = p.Key;
        b.CasterId = p.CasterObjectId;
        bool overlapReturn = false;
        if ((p.Flags & BuffPacket.ModifyState) != 0 && p.State is { } s)
        {
            if (levelOf(s.Id, s.Level) is not { } level) return;
            byte wasEnabled = b.Enabled;
            b.LevelData = level;
            bool countChanged = b.OverlapCount != s.OverlapCount;
            b.ApplyState(s);
            if (wasEnabled != b.Enabled)
            {
                if (level.IsKeepCondition99)
                {
                    if (wasEnabled != 0) StopEffects(b, b.CasterId);
                    else RestartKeepEffect(b);
                }
            }
            else if (countChanged && level.HasOverlapKeepEffect)
            {
                PlayOverlapEffects(b);
                overlapReturn = true;
            }
        }
        if (!overlapReturn && (p.Flags & BuffPacket.ModifyValue) != 0) b.Shield = p.Shield ?? 0;
        if (!b.IsValid) return;
        if ((p.Flags & BuffPacket.ModifyState) != 0)
        {
            StartIfNotListed(b);
            UpdateBuffUI(b);
            if (OwnerIsMyPc) OwnEffectAdded?.Invoke((int) b.Id, b.Level);   // CGuildManager FUN_1413bce40 (additionalcli_2.cpp:301)
        }
    }

    /// <summary>ClearAll @0x140970490: RemoveKeepEffect and RemoveBuffUI for each, then empty.</summary>
    public void ClearAll()
    {
        foreach (var b in map.Values)
        {
            RemoveKeepEffect(b);
            RemoveBuffUI(b);
        }
        map.Clear();
    }

    /// <summary>The effects went with the field (the client's MyPC and its list are made anew in the next field, whose entry list
    /// plays no start effect): the keep effect ids are forgotten, not stopped, and the next update keeps them anew.</summary>
    public void ForgetEffects()
    {
        foreach (var b in map.Values) b.KeepEffect = 0;
    }

    /// <summary>
    /// UpdateEffects @0x1409728b0's AdditionalCli::Update @0x14096c520 for each effect, once per frame: nothing for a disabled keep
    /// condition 99 effect; else, with no keep effect, the overlap effects when it has an overlap keep effect, else the keep
    /// effect. A keep effect that did not spawn is tried again the next frame, as the client does.
    /// </summary>
    public void UpdateEffects()
    {
        if (Effects == null || map.Count == 0) return;
        foreach (var b in map.Values)
        {
            if (b.Enabled == 0 && b.LevelData.IsKeepCondition99) continue;
            if (b.KeepEffect != 0) continue;
            if (b.LevelData.HasOverlapKeepEffect) PlayOverlapEffects(b);
            else PlayKeepEffect(b);
        }
    }

    /// <summary>PlayInvokeEffectsByEvent @0x14096fb20: the invoke effect of each enabled effect whose level's invoke event is
    /// <paramref name="invokeEvent"/> (1 a hit, PC::vfunc_33 / vfunc_35 and CNpc::vfunc_33 when the attack's invokeEffectDisable
    /// is 0; 0 a dot tick, PC::vfunc_34 and CNpc::vfunc_34).</summary>
    public void PlayInvokeEffectsByEvent(int invokeEvent)
    {
        if (Effects == null) return;
        foreach (var b in map.Values)
        {
            if (b.Enabled == 0) continue;
            if (b.LevelData.InvokeEvent == invokeEvent) PlayInvokeEffect(b);
        }
    }

    /// <summary>FUN_14096fc00 @0x14096fc00 (0x3d mode 4, scenestategame.cpp:5697): the invoke effect of the effect of the key.</summary>
    public void PlayInvokeEffect(int key)
    {
        if (Effects != null && map.TryGetValue(key, out var b)) PlayInvokeEffect(b);
    }

    /// <summary>FUN_14096c4c0 @0x14096c4c0: nothing for a disabled keep condition 99 effect; the start effect unless the field
    /// entry's list brought it (m_b40).</summary>
    void StartIfNotListed(AdditionalBuff b)
    {
        if (b.Enabled == 0 && b.LevelData.IsKeepCondition99) return;
        if (!b.FromList) PlayStartEffect(b);
    }

    /// <summary>PlayStartEffect @0x14096d860: the start effect on the owner when it has one and the owner is shown.</summary>
    void PlayStartEffect(AdditionalBuff b)
    {
        if (Effects == null || b.LevelData.StartEffect.Length == 0) return;
        if (!Effects.OwnerShown(b.OwnerId)) return;
        Effects.Insert(b.LevelData.StartEffect, b.OwnerId, b.CasterId);
    }

    /// <summary>PlayStopEffect @0x14096d9d0: the stop effect on the owner, kind and lookup from <paramref name="casterId"/>.</summary>
    void PlayStopEffect(AdditionalBuff b, int casterId)
    {
        if (Effects == null || b.LevelData.StopEffect.Length == 0) return;
        Effects.Insert(b.LevelData.StopEffect, b.OwnerId, casterId);
    }

    /// <summary>PlayKeepEffect @0x14096c7a0: the keep effect kept on the owner.</summary>
    void PlayKeepEffect(AdditionalBuff b)
    {
        if (Effects == null || b.LevelData.KeepEffect.Length == 0) return;
        b.KeepEffect = Effects.Keep(b.LevelData.KeepEffect, b.OwnerId, b.CasterId);
    }

    /// <summary>PlayInvokeEffect @0x14096cec0: nothing for a disabled keep condition 99 effect; else the invoke effect on the owner.</summary>
    void PlayInvokeEffect(AdditionalBuff b)
    {
        if (b.Enabled == 0 && b.LevelData.IsKeepCondition99) return;
        if (b.LevelData.InvokeEffect.Length == 0) return;
        Effects.Insert(b.LevelData.InvokeEffect, b.OwnerId, b.CasterId);
    }

    /// <summary>StopEffects @0x14096c650: RemoveKeepEffect, then the stop effect with <paramref name="casterId"/>.</summary>
    void StopEffects(AdditionalBuff b, int casterId)
    {
        RemoveKeepEffect(b);
        PlayStopEffect(b, casterId);
    }

    /// <summary>RestartKeepEffect @0x14096c740: the overlap effects with an overlap keep effect, else the keep effect anew.</summary>
    void RestartKeepEffect(AdditionalBuff b)
    {
        if (b.LevelData.HasOverlapKeepEffect) PlayOverlapEffects(b);
        else
        {
            RemoveKeepEffect(b);
            PlayKeepEffect(b);
        }
    }

    /// <summary>RemoveKeepEffect @0x14096ce50: the keep effect stopped, then with an overlap keep effect the end effect of the
    /// current count.</summary>
    void RemoveKeepEffect(AdditionalBuff b)
    {
        if (b.KeepEffect == 0) return;
        Effects?.Stop(b.KeepEffect);
        b.KeepEffect = 0;
        if (b.LevelData.HasOverlapKeepEffect) PlayOverlapEndEffect(b, 0);
    }

    /// <summary>PlayOverlapEffects @0x14096ca50: the start effect of the count, then FUN_14096ca80 (the count's keep effect).</summary>
    void PlayOverlapEffects(AdditionalBuff b)
    {
        if (Effects == null) return;
        uint count = (uint)b.OverlapCount;
        var o = b.LevelData.OverlapKeep;
        // PlayOverlapStartEffect @0x14096c8e0: the start row of exactly the count
        if (OverlapKeepEffects.Exact(o.Start, count) is { } start) Effects.Insert(start, b.OwnerId, b.CasterId);
        // FUN_14096ca80: a kept effect is replaced only when the keep rows have the count itself, ending with the end effect of
        // the count below (at least 1); then the keep row of the largest count not above it
        if (b.KeepEffect != 0)
        {
            if (OverlapKeepEffects.Exact(o.Keep, count) == null) return;
            Effects.Stop(b.KeepEffect);
            b.KeepEffect = 0;
            PlayOverlapEndEffect(b, (int)Math.Max(1u, count - 1));
        }
        if (OverlapKeepEffects.Floor(o.Keep, count) is { } keep) b.KeepEffect = Effects.Keep(keep, b.OwnerId, b.CasterId);
    }

    /// <summary>PlayOverlapEndEffect @0x14096ccc0: 0 = the current count; the end row of the largest count not above it.</summary>
    void PlayOverlapEndEffect(AdditionalBuff b, int count)
    {
        if (Effects == null) return;
        if (count == 0) count = b.OverlapCount;
        if ((uint)count == uint.MaxValue) return;   // upper_bound(count + 1) of the unsigned count wraps to 0: no row
        if (OverlapKeepEffects.Floor(b.LevelData.OverlapKeep.End, (uint)count) is { } end) Effects.Insert(end, b.OwnerId, b.CasterId);
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
