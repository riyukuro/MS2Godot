namespace Ms2.Ui;

/// <summary>What <see cref="PetItemUse.Decide"/> reads of the item, the field and the own pc.</summary>
public struct PetItemFacts
{
    /// <summary>CPetDataTable::Find(the item's GetPetId) finds a row, and FindNpcData(the row's id) its npc.</summary>
    public bool PetRowKnown, NpcKnown;
    /// <summary>The current field's CFieldData m_nUsePet (&lt;ui usePet&gt;).</summary>
    public bool FieldUsesPet;
    /// <summary>WorldService::FUN_141bdb660(7): the house restricts pets for the own pc (CubeMapInfo.PermissionApplies(7)).</summary>
    public bool HouseForbidsPet;
    /// <summary>The bind box was already answered (the caller's bConfirmed).</summary>
    public bool Confirmed;
    /// <summary>GetTransfer()->HasBindingRule() and not HasBinding(): the use binds the item.</summary>
    public bool BindsOnUse;
    /// <summary>The item's uid and MyPC::FUN_1416769f0 (the summoned pet item's uid, 0 for none).</summary>
    public long ItemUid, SummonedUid;
    /// <summary>IsRiding.</summary>
    public bool Riding;
}

/// <summary>
/// MyPC::FUN_14152aa90 (@0x14152aa90, mypc.cpp:3735-3796): a pet item used from the bag (InvenSlot's double click,
/// invenslot.cpp:868-871, and vfunc_14 case 16, :1071-1078, the right click while the pet dialog 0x4f38 leads).
/// </summary>
public static class PetItemUse
{
    public enum Outcome
    {
        /// <summary>No pet row or no npc row: nothing.</summary>
        None,
        /// <summary>A refusal: <c>noticeId</c> (FUN_141275260(id, 1, 0)) or <c>noticeKey</c> (ShowSystemNotice of the common
        /// string).</summary>
        Notice,
        /// <summary>The bind box (<see cref="BindKey"/>, tag <see cref="BindBoxTag"/>); its yes uses the item again confirmed.</summary>
        BindConfirm,
        /// <summary>No pet out and riding: SendRequestRideMode1(RideOffActionSummonPetOn(uid)).</summary>
        RideOffSummon,
        /// <summary>No pet out: FUN_14152b2d0(uid) (<see cref="PetSummonChecks.Summon"/>).</summary>
        Summon,
        /// <summary>This pet is out: SendRequestPet(uid) (RequestPet mode 1).</summary>
        Unsummon,
        /// <summary>Another pet out and riding: SendRequestRideMode1(RideOffActionSummonPetTransfer(uid)).</summary>
        RideOffTransfer,
        /// <summary>Another pet out: FUN_14152b8d0(uid) (<see cref="PetSummonChecks.Transfer"/>).</summary>
        Transfer,
    }

    /// <summary>FUN_141275260(0x654, 1, 0): the field or the state refuses a pet.</summary>
    public const int RefusedNotice = 0x654;
    /// <summary>GetStringByKey: the house restricts pets.</summary>
    public const string HouseForbidsKey = "s_ugcmap_fun_pvp_ffa_forbiden_summon_pet";
    /// <summary>The bind box text (FUN_14028dfa0 with vfunc_77, the player's name) and its tag (s_strSummonBindPet).</summary>
    public const string BindKey = "s_msg_transfer_bind_pet_summon", BindBoxTag = "summonBindPet";

    /// <summary>
    /// No pet row (GetPetId) or no npc row: nothing. The field without usePet: notice 0x654. The house restricts pets
    /// (FUN_141bdb660(7)): <see cref="HouseForbidsKey"/>. Not confirmed and the use binds the item: the bind box. Then by the
    /// summoned uid: none -> the ride-off summon when riding, else the summon checks; this item -> the unsummon; another ->
    /// the ride-off transfer when riding, else the transfer checks.
    /// STILL-OPEN: CreateMessageBox returning null (the code then goes on to the summon) does not happen here.
    /// </summary>
    public static Outcome Decide(in PetItemFacts f, out int noticeId, out string noticeKey)
    {
        noticeId = 0;
        noticeKey = null;
        if (!f.PetRowKnown || !f.NpcKnown) return Outcome.None;
        if (!f.FieldUsesPet) { noticeId = RefusedNotice; return Outcome.Notice; }
        if (f.HouseForbidsPet) { noticeKey = HouseForbidsKey; return Outcome.Notice; }
        if (!f.Confirmed && f.BindsOnUse) return Outcome.BindConfirm;
        if (f.SummonedUid == 0) return f.Riding ? Outcome.RideOffSummon : Outcome.Summon;
        if (f.SummonedUid == f.ItemUid) return Outcome.Unsummon;
        return f.Riding ? Outcome.RideOffTransfer : Outcome.Transfer;
    }
}

/// <summary>What <see cref="PetSummonChecks"/> reads of the item, the own pc and the field.</summary>
public struct PetSummonFacts
{
    /// <summary>FindItemByUid finds the item.</summary>
    public bool ItemFound;
    /// <summary>A PC bang item off a PC bang (IsPCBangItem, FUN_141658f30); a VIP item (FUN_140525db0) without IsVip; a wedding
    /// limit (HasWeddingLimit) without vfunc_146.</summary>
    public bool PcBangRefused, VipRefused, WeddingRefused;
    /// <summary>MyPC::FUN_141675ed0: the own pc is in water.</summary>
    public bool OnWater;
    /// <summary>The item's GetRequiredLevel and the own level (vfunc_78).</summary>
    public int RequiredLevel, Level;
    /// <summary>The current field's CFieldData m_nUsePet.</summary>
    public bool FieldUsesPet;
    /// <summary>GetCurStateId.</summary>
    public int State;
}

/// <summary>A refusal of the summon or the transfer: a common string by id (FUN_141275260(id, 1, 0)), or by key shown with
/// ShowSystemNotice; <see cref="Arg"/> is FUN_14028dfa0's format argument (the required level) when not null.</summary>
public readonly record struct PetNotice(int Id, string Key, uint? Arg);

/// <summary>MyPC::FUN_14152b2d0 and FUN_14152b8d0 (mypc.cpp:3220-3282) and the pet summon state table
/// (unit_14072f7f0_petsummonstate.cpp).</summary>
public static class PetSummonChecks
{
    public enum Result
    {
        /// <summary>No item: nothing.</summary>
        None,
        /// <summary>The refusal in the <see cref="PetNotice"/>.</summary>
        Notice,
        /// <summary>vfunc_59(0x26, StateSummonData(uid, transfer)): the summon state.</summary>
        Start,
    }

    public const int PcBangNotice = 0xe2;
    public const string VipKey = "s_item_err_puton_vip", WeddingKey = "s_item_err_puton_wedding",
        LevelKey = "s_pet_error_summon_level_limit";

    /// <summary>
    /// FUN_14152b2d0 (@0x14152b2d0, mypc.cpp:3243-3282), in its order, the first refusal returning: no item -> nothing; a PC
    /// bang item off a PC bang -> 0xe2; VIP -> <see cref="VipKey"/>; wedding -> <see cref="WeddingKey"/>; in water -> 0x654;
    /// the required level above the own level (as an unsigned short) -> <see cref="LevelKey"/> with the level; the field
    /// without usePet -> 0x654; a state <see cref="StateAllows"/> refuses -> its <see cref="StateRefusalKey"/>, or 0x654 when
    /// the key is empty. Else the summon state (transfer 0).
    /// </summary>
    public static Result Summon(in PetSummonFacts f, out PetNotice notice)
    {
        notice = default;
        if (!f.ItemFound) return Result.None;
        if (f.PcBangRefused) { notice = new PetNotice(PcBangNotice, null, null); return Result.Notice; }
        if (f.VipRefused) { notice = new PetNotice(0, VipKey, null); return Result.Notice; }
        if (f.WeddingRefused) { notice = new PetNotice(0, WeddingKey, null); return Result.Notice; }
        if (f.OnWater) { notice = new PetNotice(PetItemUse.RefusedNotice, null, null); return Result.Notice; }
        if (f.RequiredLevel > (ushort)f.Level) { notice = new PetNotice(0, LevelKey, (uint)f.RequiredLevel); return Result.Notice; }
        if (!f.FieldUsesPet) { notice = new PetNotice(PetItemUse.RefusedNotice, null, null); return Result.Notice; }
        if (!StateAllows(f.State))
        {
            string key = StateRefusalKey(f.State);
            notice = key.Length < 1 ? new PetNotice(PetItemUse.RefusedNotice, null, null) : new PetNotice(0, key, null);
            return Result.Notice;
        }
        return Result.Start;
    }

    /// <summary>
    /// FUN_14152b8d0 (@0x14152b8d0, mypc.cpp:3220-3238): no item -> nothing; a state FUN_14072f970 refuses -> 0x654 (no key
    /// here); the field without usePet -> 0x654. Else the summon state with the transfer flag (StateSummonData(uid, 1)).
    /// The other summon checks (PC bang, VIP, wedding, water, level) are not made on a transfer.
    /// </summary>
    public static Result Transfer(in PetSummonFacts f, out PetNotice notice)
    {
        notice = default;
        if (!f.ItemFound) return Result.None;
        if (!StateAllows(f.State) || !f.FieldUsesPet) { notice = new PetNotice(PetItemUse.RefusedNotice, null, null); return Result.Notice; }
        return Result.Start;
    }

    /// <summary>FUN_14072f890 (@0x14072f890, unit_14072f7f0_petsummonstate.cpp:40-60), and FUN_14072f970 which returns it:
    /// a pet summon may not start from states 3, 5, 6, 7, 8, 9, 0x10, 0x12, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20, 0x2b, 0x2d
    /// and 0x2f.</summary>
    public static bool StateAllows(int state) => state switch
    {
        3 or 6 or 7 or 9 or 0x1d or 0x1e or 5 or 0x1f or 0x1b or 0x1c or 8 or 0x2f or 0x2d or 0x12 or 0x2b or 0x20 or 0x10 => false,
        _ => true,
    };

    /// <summary>FUN_14072f9a0 (@0x14072f9a0, unit_14072f7f0_petsummonstate.cpp:70-76): the common string key of a refusing
    /// state; "" for the states without one.</summary>
    public static string StateRefusalKey(int state) => state switch
    {
        0x2f => "s_pet_cant_summon_state_play_instrument",
        0x2d => "s_pet_cant_summon_state_fishing",
        0x10 => "s_pet_cant_summon_state_skill",
        _ => "",
    };
}
