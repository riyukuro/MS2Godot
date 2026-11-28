using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Net;

// The own pet's summon from a pet item: MyPC::FUN_14152aa90, FUN_14152b2d0 and FUN_14152b8d0, and the ride-off actions 9 / 10.
public partial class LiveSession
{
    bool petFieldUse = true;

    /// <summary>The current field's CFieldData m_nUsePet (&lt;ui usePet&gt;, default 1); no field reads false, unreadable field
    /// data the default.</summary>
    bool FieldUsesPet()
    {
        if (FieldId is not int id) return false;
        ReadFieldUse(id);
        return petFieldUse;
    }

    /// <summary>MyPC::FUN_1416769f0: the summoned pet item's uid (PCBase m_nUidB78, which ResponsePet 0 / 1 and FieldAddUser's
    /// pet item set), 0 for none.</summary>
    public long SummonedPetUid => Me.PetUid;

    /// <summary>
    /// MyPC::FUN_14152aa90 (@0x14152aa90, mypc.cpp:3735-3796) for the bag item <paramref name="uid"/>: the facts, then
    /// <see cref="PetItemUse.Decide"/>; this runs every outcome but the bind box, which the inventory shows on
    /// <see cref="PetItemUse.Outcome.BindConfirm"/>. The ride-offs go out through SendRequestRideMode1 (the action's m_n20 0, so a
    /// battle or object mount refuses with s_action_cant_ride); the unsummon is SendRequestPet (RequestPet mode 1).
    /// </summary>
    public PetItemUse.Outcome UsePetItem(long uid, bool confirmed, bool bindsOnUse)
    {
        if (player == null || !IsInstanceValid(player) || selfObject <= 0) return PetItemUse.Outcome.None;
        var item = Items.Get(uid);
        var data = item != null ? ItemDataOf(item.Id) : null;
        int petId = data?.PetId ?? 0;
        var f = new PetItemFacts
        {
            PetRowKnown = petId != 0 && PetRowKnown(petId),
            NpcKnown = petId != 0 && PetNpcKnown(petId),
            FieldUsesPet = FieldUsesPet(),
            HouseForbidsPet = CubeMap.PermissionApplies(PetHousePermission, OwnAccountId, true, Party.IsMemberByAccountId),
            Confirmed = confirmed,
            BindsOnUse = bindsOnUse,
            ItemUid = uid,
            SummonedUid = SummonedPetUid,
            Riding = player.Mount != null,
        };
        var outcome = PetItemUse.Decide(f, out int noticeId, out string noticeKey);
        switch (outcome)
        {
            case PetItemUse.Outcome.Notice:
                ShowPetNotice(new PetNotice(noticeId, noticeKey, null));
                break;
            case PetItemUse.Outcome.RideOffSummon:
                RideOffForPet(RideOffSummonPetOnRequest.ActionType, new RideOffSummonPetOnRequest(uid));
                break;
            case PetItemUse.Outcome.RideOffTransfer:
                RideOffForPet(RideOffSummonPetTransferRequest.ActionType, new RideOffSummonPetTransferRequest(uid));
                break;
            case PetItemUse.Outcome.Summon:
                SummonPet(uid, false);
                break;
            case PetItemUse.Outcome.Transfer:
                SummonPet(uid, true);
                break;
            case PetItemUse.Outcome.Unsummon:
                Send(new PetUnsummonRequest(uid));          // SendRequestPet(nSummoned), the summoned uid is this item's
                break;
        }
        return outcome;
    }

    /// <summary>WorldService::FUN_141bdb660's kind for pets (7, mypc.cpp:3749).</summary>
    const int PetHousePermission = 7;

    /// <summary>FindNpcData(the pet row's id) finds the npc.</summary>
    static bool PetNpcKnown(int petId)
    {
        try { return Ms2Godot.World.FieldContents.Npcs(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir)).Find(petId) != null; }
        catch { return false; }
    }

    /// <summary>SendRequestRideMode1 with a RideOffActionSummonPetOn / Transfer (m_n20 0).</summary>
    void RideOffForPet(int actionType, IOutPacket request)
    {
        var ride = player.Mount?.Data;
        if (RideOff.Request(true, ride != null, ride?.Type ?? 0, ride?.EnableRideOffUI ?? false, actionType, false, request, Send)
                == RideOff.Outcome.CantRide)
            SystemNoticeSink?.Invoke(MessageBoxData.Common(RideOff.CantRideNoticeId));
    }

    /// <summary>
    /// FUN_14152b2d0 (<paramref name="transfer"/> false) or FUN_14152b8d0 (true) for the item <paramref name="uid"/>: the facts,
    /// <see cref="PetSummonChecks.Summon"/> / <see cref="PetSummonChecks.Transfer"/>, then the refusal's notice or
    /// vfunc_59(0x26, StateSummonData(uid, transfer)), the shared summon state.
    /// </summary>
    void SummonPet(long uid, bool transfer)
    {
        if (player == null || !IsInstanceValid(player) || selfObject <= 0) return;
        var f = PetSummonFactsOf(uid);
        var result = transfer ? PetSummonChecks.Transfer(f, out var notice) : PetSummonChecks.Summon(f, out notice);
        if (result == PetSummonChecks.Result.Notice) ShowPetNotice(notice);
        else if (result == PetSummonChecks.Result.Start) SummonState.Start(uid, transfer, HeldItem);
    }

    PetSummonFacts PetSummonFactsOf(long uid)
    {
        var item = Items.Get(uid);
        var data = item != null ? ItemDataOf(item.Id) : null;
        var pc = new PcItemUse.Pc
        {
            ServerTime = () => ServerTime,
            PcBangGrade = Me.User?.PcBangGrade ?? 0,
            PremiumTime = Me.PremiumTime,
            Manager = Wedding,
            CharacterId = CharacterId,
        };
        return new PetSummonFacts
        {
            ItemFound = item != null,
            PcBangRefused = data != null && !PcItemUse.PcBangOk(data, pc),     // IsPCBangItem without FUN_141658f30
            VipRefused = data != null && !PcItemUse.VipOk(data, pc),           // FUN_140525db0 without IsVip
            WeddingRefused = data != null && !PcItemUse.WeddingOk(data, pc),   // HasWeddingLimit without vfunc_146
            OnWater = player.OnWater,                                         // FUN_141675ed0
            RequiredLevel = data?.LevelLimit ?? 0,                            // GetRequiredLevel
            Level = Me.User?.Level ?? 0,                                      // vfunc_78
            FieldUsesPet = FieldUsesPet(),
            State = InteractPlayerStateId(),                                  // GetCurStateId
        };
    }

    /// <summary>FUN_141275260(id, 1, 0) for a common string by id; ShowSystemNotice of GetStringCommonByKey for a key, formatted
    /// (FUN_14028dfa0) with the argument when there is one.</summary>
    void ShowPetNotice(PetNotice n)
    {
        string text = n.Key != null ? MessageBoxData.CommonByKey(n.Key) : MessageBoxData.Common(n.Id);
        if (string.IsNullOrEmpty(text)) return;
        if (n.Arg is uint arg) text = Ms2.Quest.ClientFormat.Format(text, arg);
        SystemNoticeSink?.Invoke(text);
    }

    /// <summary>MyPC's ride-off action switch, cases 9 and 10 (mypc.cpp:3474-3486): after the ride off the summon checks
    /// (FUN_14152b2d0) or the transfer checks (FUN_14152b8d0) run with the action's item uid (m_n28).</summary>
    void PetAfterRideOff(RideOffAction a)
    {
        if (a is RideOffSummonPetOn on) SummonPet(on.Value28, false);
        else if (a is RideOffSummonPetTransfer t) SummonPet(t.Value28, true);
    }
}
