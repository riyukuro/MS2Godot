using System;
using System.Numerics;
using Ms2.Net.Packets;
using Ms2.Net.Wire;

namespace Ms2.Ui;

/// <summary>What <see cref="CoupleEffect"/> reads and does in the field (WorldService, CEffectService, the constants).</summary>
public interface ICoupleWorld
{
    /// <summary>WorldService::FUN_141bd1530: the field's PC of a character id (the own one too); null when none.</summary>
    CouplePc FindPc(long characterId);
    /// <summary>CoupleEffectSystem::FUN_140b0ef20 (@0x140b0ef20) false: the field data has the limit content 0x400.</summary>
    bool CoupleEffectLimited { get; }
    /// <summary>CEffectService::FUN_1411a49c0 with owner 0 at a transform of scale 1, the identity rotation and this client
    /// space translate: the kept effect's id, 0 when it did not spawn.</summary>
    int SpawnEffect(string effect, Vector3 at);
    /// <summary>CEffectService::FUN_1411a6060: a kept effect stopped at once.</summary>
    void RemoveEffect(int id);
    /// <summary>FUN_140710590: a packet sent (nothing offline).</summary>
    void Send(IOutPacket packet);
    /// <summary>The constants' CoupleEffectCheckTick (ms) and CoupleEffectCheckRadius (clientconstants.h:807-808).</summary>
    uint CheckTick { get; }
    float CheckRadius { get; }
    /// <summary>GetTickCountMs2: the client's millisecond tick.</summary>
    uint Now { get; }
    /// <summary>CItemGemPropertyTable's character effect gems.</summary>
    CharEffectGems Gems { get; }
}

/// <summary>
/// A PC's side of the buddy badge (pc.cpp:2261-2320, :3534-3543): its couple effect system (PC m_spCouple11d8) and the pending
/// show (m_n11d0) a BuddyBadge show leaves when the PC has none yet, with what the system reads of the PC: its character id
/// (FUN_141656a80), object id, gender, name (vfunc_77), place (vfunc_18, client space) and whether it is MyPC.
/// </summary>
public sealed class CouplePc
{
    public long CharacterId;
    public int ObjectId;
    public int Gender;
    public string Name = "";
    public bool IsMine;
    public Func<Vector3> Position = () => Vector3.Zero;

    /// <summary>PC m_spCouple11d8: null without a worn buddy badge.</summary>
    public CoupleEffect Couple { get; private set; }
    /// <summary>PC m_n11d0: a show came before the system was made.</summary>
    public bool Pending { get; private set; }

    /// <summary>
    /// PC::CreateCoupleEffect (@0x1416636e0) from CGemCharEffect::vfunc_6: a new system from the badge item (its couple id, item
    /// id, link flag and couple name) and the gem id, the old one released after (its destructor's SetEffectMode(1)); a pending
    /// show spawns the new one's effect and is cleared.
    /// </summary>
    public void CreateCouple(ICoupleWorld world, EquipItem item, int gemId)
    {
        var old = Couple;
        Couple = new CoupleEffect(world, this, item.CoupleId, item.Id, item.CoupleFlag, item.CoupleName ?? "", gemId);
        old?.Release();
        if (Pending)
        {
            Couple.SpawnEffect();
            Pending = false;
        }
    }

    /// <summary>PC::ClearCouple (@0x141663870) from CGemCharEffect::vfunc_7: the system released, the pending show cleared.</summary>
    public void ClearCouple()
    {
        Release();
        Pending = false;
    }

    /// <summary>The PC's destructor: the system released (CoupleEffectSystem::~CoupleEffectSystem).</summary>
    public void Release()
    {
        var old = Couple;
        Couple = null;
        old?.Release();
    }

    /// <summary>PC::FUN_141663620 (@0x141663620), BuddyBadge kinds 0 and 2: the system's effect spawned, else a pending show.</summary>
    public void Show()
    {
        if (Couple != null) Couple.SpawnEffect();
        else Pending = true;
    }

    /// <summary>PC::FUN_141663680 (@0x141663680), BuddyBadge kind 1: the system's effect removed, else no pending show.</summary>
    public void Hide()
    {
        if (Couple != null) Couple.RemoveEffect();
        else Pending = false;
    }

    /// <summary>PC::FUN_1416634a0 (@0x1416634a0), MyPC's at a FieldAddUser: the system's newcomer hook.</summary>
    public void UserAdded(long characterId) => Couple?.UserAdded(characterId);

    /// <summary>PC::FUN_141663500 (@0x141663500), MyPC's at a FieldRemoveUser before the PC goes: the system's leave hook.</summary>
    public void UserRemoved(int objectId) => Couple?.UserRemoved(objectId);

    /// <summary>PC::GetCoupleType (@0x141663940): the system's couple type, 0 without one.</summary>
    public int CoupleType => Couple?.CoupleType ?? 0;
    /// <summary>PC::FUN_141663990 (@0x141663990): the system's GetCharacterId, 0 without one.</summary>
    public long CoupleCharacterId => Couple?.CharacterId ?? 0;
    /// <summary>PC::GetCoupleName (@0x1416638b0): the system's name, "" without one.</summary>
    public string CoupleName => Couple?.Name ?? "";

    /// <summary>The PC's update (pc.cpp:4476): the system's Update.</summary>
    public void Update() => Couple?.Update();
}

/// <summary>
/// CoupleEffectSystem (unit_140b0e5f0_coupleeffectsystem.cpp): a worn buddy badge's couple effect, played between its owner and
/// the partner. The badge opener's badge is linked (CItem::IsCoupleLinked) and its couple id is the partner; the partner's badge
/// is not linked and its couple id is the opener, so both systems' <see cref="CharacterId"/> is the opener's. Only MyPC's linked
/// system watches the distance (<see cref="Update"/>); the server's BuddyBadge shows and hides the others'.
/// </summary>
public sealed class CoupleEffect
{
    readonly ICoupleWorld world;
    readonly CouplePc owner;

    /// <summary>m_bLinked, m_nCoupleType (the badge's item id), m_nPartnerId (the badge's couple id), m_strName (its couple
    /// name) and m_nGemId.</summary>
    public bool Linked { get; }
    public int CoupleType { get; }
    public long PartnerId { get; }
    public string Name { get; }
    public int GemId { get; }

    /// <summary>m_bInRange, m_bActive, m_bRequested, m_nEffectId and m_posEffect.</summary>
    public bool InRange { get; private set; }
    public bool Active { get; private set; }
    public bool Requested { get; private set; }
    public int EffectId { get; private set; }
    public Vector3 EffectPosition { get; private set; }

    // m_timer (Timer, +0x28): Start(ms) sets the period from now; IsExpired is false before any start (period 0)
    uint timerStart, timerPeriod;
    // m_tick (Unk_IntervalTimer, +0x64): the last tick Check fired at, 0 before the first
    uint tickLast;

    /// <summary>The constructor (@0x140b0e5f0): the fields, then FUN_140b0ecd0.</summary>
    public CoupleEffect(ICoupleWorld world, CouplePc owner, long partnerId, int coupleType, bool linked, string name, int gemId)
    {
        this.world = world;
        this.owner = owner;
        PartnerId = partnerId;
        CoupleType = coupleType;
        Linked = linked;
        Name = name ?? "";
        GemId = gemId;
        Start();
    }

    /// <summary>The destructor (@0x140b0e730): SetEffectMode(1), which sends for any PC's system.</summary>
    public void Release() => SetEffectMode(1);

    /// <summary>
    /// Update (@0x140b0e7a0): an active linked system of MyPC in a field without the limit content 0x400, once a second
    /// (m_tick.Check(1000)), with the partner of the same couple type whose system's character id is the owner's: within
    /// CoupleEffectCheckRadius it turns the effect on when it comes in range and, once the effect's CheckTick has passed,
    /// takes it off when the pair's middle moved more than 50 from it (the next second spawns it at the new middle); beyond
    /// the radius it takes the effect off.
    /// </summary>
    public void Update()
    {
        if (!Active || !Linked || !owner.IsMine || world.CoupleEffectLimited) return;
        if (!TickDue(1000)) return;
        var partner = world.FindPc(PartnerId);
        if (partner == null) return;
        if (CoupleType == 0 || CoupleType != partner.CoupleType) return;
        if (owner.CharacterId != partner.CoupleCharacterId) return;
        float distance = Vector3.Distance(owner.Position(), partner.Position());
        if (!(distance > world.CheckRadius))
        {
            if (InRange)
            {
                if (TimerExpired())
                {
                    float moved = Vector3.Distance(EffectPosition, Middle());
                    if (moved > 50f && EffectId != 0) SetEffectMode(1);
                }
            }
            else
            {
                InRange = true;
                if (EffectId == 0) SetEffectMode(0);
            }
        }
        else
        {
            InRange = false;
            if (EffectId != 0) SetEffectMode(1);
        }
    }

    /// <summary>FUN_140b0ead0 (@0x140b0ead0), a player entering the field: FUN_140b0ecd0, then for MyPC's linked system with
    /// its effect on, BuddyBadge mode 2 with the newcomer.</summary>
    public void UserAdded(long newcomerId)
    {
        Start();
        if (!owner.IsMine) return;
        if (Linked && EffectId > 0) world.Send(new BuddyBadgeNewcomerRequest(CharacterId, newcomerId));
    }

    /// <summary>FUN_140b0eba0 (@0x140b0eba0), a player leaving the field: without the partner, or when the partner is the one
    /// leaving, the effect off (SetEffectMode(1)) and the system inactive.</summary>
    public void UserRemoved(int objectId)
    {
        var partner = world.FindPc(PartnerId);
        if (partner == null || partner.ObjectId == objectId)
        {
            SetEffectMode(1);
            Active = false;
        }
    }

    /// <summary>GetCharacterId (@0x140b0ec80): 0 without an owner, the owner's character id when linked, else the partner's.</summary>
    public long CharacterId => owner == null ? 0 : Linked ? owner.CharacterId : PartnerId;

    /// <summary>FUN_140b0ecd0 (@0x140b0ecd0): with the partner in the field, out of range, active, and the start request.</summary>
    void Start()
    {
        if (world.FindPc(PartnerId) == null) return;
        InRange = false;
        Active = true;
        SendStartRequest();
    }

    /// <summary>SpawnEffect (@0x140b0ed30): with no effect, the effect (<see cref="EffectName"/>) at the pair's middle, kept;
    /// then m_timer started for CoupleEffectCheckTick.</summary>
    public void SpawnEffect()
    {
        if (EffectId != 0) return;
        EffectPosition = Middle();
        EffectId = world.SpawnEffect(EffectName(), EffectPosition);
        timerPeriod = world.CheckTick;
        timerStart = world.Now;
    }

    /// <summary>RemoveEffect (@0x140b0eed0): the effect stopped, no effect and out of range.</summary>
    public void RemoveEffect()
    {
        if (EffectId == 0) return;
        world.RemoveEffect(EffectId);
        EffectId = 0;
        InRange = false;
    }

    /// <summary>GetEffectName (@0x140b0efb0): "" without the partner or the gem's data, else the gem's couple effect of the
    /// owner's and the partner's genders.</summary>
    public string EffectName()
    {
        var partner = world.FindPc(PartnerId);
        if (partner == null) return "";
        var gem = world.Gems?.Find(GemId);
        if (gem == null) return "";
        return gem.CoupleEffect(owner.Gender, partner.Gender);
    }

    /// <summary>FUN_140b0f160 (@0x140b0f160): the middle of the owner and the partner, zero without the partner.</summary>
    Vector3 Middle()
    {
        var partner = world.FindPc(PartnerId);
        if (partner == null) return Vector3.Zero;
        return (owner.Position() + partner.Position()) / 2f;
    }

    /// <summary>SendStartRequest (@0x140b0f260): once, for MyPC with the partner in the field whose name is not the badge's
    /// couple name, BuddyBadge mode 3 with the partner (the server renames the badges).</summary>
    void SendStartRequest()
    {
        if (Requested || !owner.IsMine) return;
        var partner = world.FindPc(PartnerId);
        if (partner == null) return;
        if (string.Equals(Name, partner.Name ?? "", StringComparison.Ordinal)) return;
        world.Send(new BuddyBadgeNameRequest(PartnerId));
        Requested = true;
    }

    /// <summary>SetEffectMode (@0x140b0f3a0): a linked system spawns (0) or removes (1) its effect; any system then sends
    /// BuddyBadge with the mode and <see cref="CharacterId"/>.</summary>
    void SetEffectMode(int mode)
    {
        if (Linked)
        {
            if (mode == 0) SpawnEffect();
            else if (mode == 1) RemoveEffect();
        }
        world.Send(new BuddyBadgeModeRequest((byte)mode, CharacterId));
    }

    /// <summary>Unk_IntervalTimer::Check (@0x140268870): due on the first call and then once per interval, re-armed when due.</summary>
    bool TickDue(uint interval)
    {
        uint now = world.Now;
        if (tickLast != 0 && unchecked(now - tickLast) < interval) return false;
        tickLast = now;
        return true;
    }

    /// <summary>Timer::IsExpired (@0x140868c00): false before a start (period 0), else the period has passed.</summary>
    bool TimerExpired() => timerPeriod != 0 && unchecked(world.Now - timerStart) >= timerPeriod;
}

/// <summary>
/// CGemCharEffect (itemgem.cpp:791-830), a PC's worn buddy badge gem (type 7). Applying it (vfunc_6) plays the gem's effect of
/// the wearer's gender on the wearer (SetActor, owner 0), keeps its id and makes the PC's couple system
/// (<see cref="CouplePc.CreateCouple"/>); without the gem's data nothing happens. Removing it (vfunc_7) stops the kept effects
/// and clears the couple. Both skip a ClonePC; the field's PCs are none.
/// </summary>
public sealed class CharEffectGem
{
    readonly ICoupleWorld world;
    readonly CouplePc pc;
    readonly Func<string, int> spawnOnPc;
    readonly Action<int> stop;
    // m_listEffects
    readonly System.Collections.Generic.List<int> effects = new();

    /// <param name="spawnOnPc">FUN_1411a49c0 with the PC as the actor: the kept effect's id, 0 when it did not spawn.</param>
    /// <param name="stop">FUN_1411a6060: a kept effect stopped at once.</param>
    public CharEffectGem(ICoupleWorld world, CouplePc pc, Func<string, int> spawnOnPc, Action<int> stop)
    {
        this.world = world;
        this.pc = pc;
        this.spawnOnPc = spawnOnPc;
        this.stop = stop;
    }

    public CouplePc Pc => pc;
    /// <summary>The kept effect ids (m_listEffects).</summary>
    public int EffectCount => effects.Count;

    /// <summary>vfunc_6 (@0x1405a5e20) of the worn item and its gem id.</summary>
    public bool Apply(EquipItem item, int gemId)
    {
        var data = world.Gems?.Find(gemId);
        if (data == null) return false;
        int h = spawnOnPc(data.EffectByGender(pc.Gender));
        if (h > 0) effects.Add(h);
        if (item != null) pc.CreateCouple(world, item, gemId);
        return true;
    }

    /// <summary>vfunc_7 (@0x1405a6080).</summary>
    public void Remove()
    {
        foreach (int h in effects) stop(h);
        effects.Clear();
        pc.ClearCouple();
    }
}
