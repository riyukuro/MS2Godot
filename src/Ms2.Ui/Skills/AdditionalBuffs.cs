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
