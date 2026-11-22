using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;

namespace Ms2.Ui;

/// <summary>What <see cref="RideItemUse.Decide"/> reads of the own pc, the item and the field.</summary>
public struct RideItemFacts
{
    /// <summary>MyPC m_spB00: a mount action is kept, and its type (m_nType).</summary>
    public bool HasMountAction;
    public int MountActionType;
    /// <summary>GetRideeData(item's ridee id) finds the riding row.</summary>
    public bool RideeKnown;
    /// <summary>The item's ridee id and the own pc's current one (GetRideeId, 0 when not riding).</summary>
    public int ItemRideeId, CurrentRideeId;
    /// <summary>IsRiding: the own pc has a ridee.</summary>
    public bool Riding;
    /// <summary>The own mount's state id (GetRidee()->GetCurStateId) while riding.</summary>
    public int MountState;
    /// <summary>CanUseItem (unit_140d1ae30_itemfunctiondispatch.cpp:430), which shows its own box when it refuses.</summary>
    public bool CanUseItem;
    /// <summary>MyPC::FUN_1415271e0 (mypc.cpp:4946) of the own pc (or its mount) with the riding row: not in water, or the
    /// row's water fields let it.</summary>
    public bool SkillUsable;
    /// <summary>MyPC::FUN_141527160(0x26): the own state is 1, 0xb, 0xf, 0x21 or 0x3a.</summary>
    public bool StateAllows;
    /// <summary>WorldService::FUN_141bcb210 (worldservice_1.cpp:5476): the map info's +0xac is 1 and the cube map info's
    /// flag 5 is off.</summary>
    public bool MapAllows;
    /// <summary>The refusal notices' tests: IsExpired; a PC bang item off a PC bang (FUN_141658f30); a VIP item
    /// (FUN_140525db0) without VIP; a wedding limit without vfunc_146; FUN_1406343b0 (the owner bind) refused.</summary>
    public bool Expired, PcBangRefused, VipRefused, WeddingRefused, BindRefused;
}

/// <summary>
/// MyPC::UseRideeItem (@0x14152ba80, mypc.cpp:4627): a ride item used from the bag (InvenSlot::vfunc_13 -> FUN_140a635f0's
/// item skill -> StateSkill::UseStateSkill kind 0x26, stateskill.cpp:2009-2021).
/// </summary>
public static class RideItemUse
{
    public enum Outcome
    {
        /// <summary>No riding row: nothing.</summary>
        None,
        /// <summary>The same ridee: SendRequestRideMode1(0).</summary>
        RideOff,
        /// <summary>vfunc_59(0x26, StateSummonData(uid, 0)): <see cref="RideSummonCast"/>.</summary>
        Summon,
        /// <summary>Riding another mount: RequestRide mode 2 (<see cref="RideAlongRequest"/>).</summary>
        RideAlong,
        /// <summary>A refusal: <c>noticeId</c> (FUN_141275260(id, 1, 0)) or <c>noticeKey</c> (ShowSystemNotice of the
        /// common string).</summary>
        Notice,
    }

    /// <summary>Notice 0x655 (a mount action of another type, the fallback refusal), 0xe1 (expired), 0xe2 (PC bang),
    /// 0xfe (owner bind).</summary>
    public const int RefusedNotice = 0x655, ExpiredNotice = 0xe1, PcBangNotice = 0xe2, BindNotice = 0xfe;
    public const string VipKey = "s_item_err_puton_vip", WeddingKey = "s_item_err_puton_wedding";

    /// <summary>
    /// A kept mount action that is not type 1: notice 0x655. No riding row: nothing. The same ridee: ride off. Not riding:
    /// CanUseItem, FUN_1415271e0, FUN_141527160(0x26) and FUN_141bcb210 all pass: the summon; else the first refusal of
    /// expired (0xe1), PC bang (0xe2), VIP, wedding, then the owner bind (0xfe) or 0x655. Riding: CanUseItem, the mount in
    /// state 1, FUN_1415271e0 of the mount and FUN_141bcb210: mode 2; else the same notices without the bind test.
    /// </summary>
    public static Outcome Decide(in RideItemFacts f, out int noticeId, out string noticeKey)
    {
        noticeId = 0;
        noticeKey = null;
        if (f.HasMountAction && f.MountActionType != 1) { noticeId = RefusedNotice; return Outcome.Notice; }
        if (!f.RideeKnown) return Outcome.None;
        if (f.CurrentRideeId == f.ItemRideeId) return Outcome.RideOff;
        if (!f.Riding)
        {
            if (f.CanUseItem && f.SkillUsable && f.StateAllows && f.MapAllows) return Outcome.Summon;
            Refusal(f, true, out noticeId, out noticeKey);
            return Outcome.Notice;
        }
        if (f.CanUseItem && f.MountState == 1 && f.SkillUsable && f.MapAllows) return Outcome.RideAlong;
        Refusal(f, false, out noticeId, out noticeKey);
        return Outcome.Notice;
    }

    static void Refusal(in RideItemFacts f, bool bindTest, out int id, out string key)
    {
        id = 0;
        key = null;
        if (f.Expired) id = ExpiredNotice;
        else if (f.PcBangRefused) id = PcBangNotice;
        else if (f.VipRefused) key = VipKey;
        else if (f.WeddingRefused) key = WeddingKey;
        else id = bindTest && f.BindRefused ? BindNotice : RefusedNotice;
    }

    /// <summary>RideeData::Basic (unit_140781d80_ridingtable.cpp:256, :360-361): rideSummonCastTime in seconds as
    /// milliseconds; without it the ClientConstants SummonRideeDuration (5000 when the tables do not set it).</summary>
    public static int CastMs(float? rideSummonCastTime, int summonRideeDuration = DefaultSummonDuration) =>
        rideSummonCastTime is { } s ? (int)(uint)(s * 1000f) : summonRideeDuration;

    /// <summary>ClientConstants SummonRideeDuration's default (constantsblocks.cpp:880).</summary>
    public const int DefaultSummonDuration = 5000;
}

/// <summary>What <see cref="RideSummonCast"/> reads from the game and how it answers.</summary>
public interface IRideSummonHost
{
    bool HasPlayer { get; }
    void Send(IOutPacket packet);
    /// <summary>UICastingBar 0x4e72 StartCastingText(listener, text, ms, false, true, ""). False without a casting bar;
    /// the casting time then runs on <see cref="NowMs"/>.</summary>
    bool StartCasting(ICastingEventListener listener, string text, int durationMs);
    /// <summary>UICastingBar 0x4e72 FUN_1418e1390 (cancel).</summary>
    void CancelCasting();
    long NowMs { get; }
    void PlayPlayerAnim(string animName, bool loop);
    void StopPlayerInteractAnim();
    /// <summary>The ride item's riding row is still there (the uid finds the item and GetRideeData its row).</summary>
    bool RideeRowOf(long itemUid, out int rideeId);
    /// <summary>CGameOption FUN_14124ab20()'s +2 byte, RideOnActionUseItem m_b28.</summary>
    bool RideOption { get; }
}

/// <summary>
/// StateSummon (0x26) for a ride item (statesummon.cpp:180-402): the casting bar with the common string 0x650 for the row's
/// cast time and "Object_React_A" (m_n10 0x82); a finished cast sends RequestRide mode 0 with a RideOnActionUseItem and
/// returns to state 1; move input leaves for state 2, which cancels the bar.
/// STILL-OPEN: the pet item side (label 0x64f, PetSummonCastTime, "Emotion_Pet_A", Eff_PetOn.xml, RequestPet 0 / 3), the
/// owner's zero move (vfunc_3) and FUN_14166e0a0 at the exit, and FUN_1417b9460's queued skill while no key is held.
/// </summary>
public sealed class RideSummonCast : ICastingEventListener
{
    /// <summary>The casting bar's common string (StateSummon::vfunc_4, a ride item).</summary>
    public const int LabelId = 0x650;
    /// <summary>vfunc_9's sequence for m_n10 0x82.</summary>
    public const string Animation = "Object_React_A";

    readonly IRideSummonHost host;
    bool active, finished;
    long itemUid, endMs;

    public RideSummonCast(IRideSummonHost host) { this.host = host; }

    /// <summary>The summon state runs.</summary>
    public bool IsCasting => active;
    /// <summary>StateSummonData m_pSummon: the item uid of the running summon, 0 when none.</summary>
    public long ItemUid => active ? itemUid : 0;

    /// <summary>
    /// vfunc_4 (@0x1417f7630): m_n48 = 0, the casting bar with <paramref name="label"/> (GetStringCommon(0x650)) for
    /// <paramref name="castMs"/>, the held item put back (SendRequestCube), and vfunc_9's sequence.
    /// </summary>
    public void Start(long uid, int castMs, string label, HeldItem held)
    {
        if (!host.HasPlayer) return;
        if (active) Cancel();
        itemUid = uid;
        active = true;
        finished = false;
        held?.SendRequestCube(host.Send);
        host.PlayPlayerAnim(Animation, false);
        endMs = 0;
        if (!host.StartCasting(this, label ?? "", castMs)) endMs = host.NowMs + castMs;
    }

    /// <summary>OnCastingFinished (@0x1417f81e0): m_n48 = 1.</summary>
    public void OnCastingFinished() { if (active) finished = true; }

    /// <summary>vfunc_6 (@0x1417f8190): back to state 1.</summary>
    public void OnCastingCancelled() { if (active) End(); }

    /// <summary>
    /// vfunc_8 (@0x1417f7b60): a finished cast sends RequestRide mode 0 (RideOnActionUseItem: the row's ridee id, the item
    /// uid, the option byte) when the item's riding row is found, then state 1; else move input leaves for state 2 (the
    /// exit, vfunc_5, cancels the bar). Returns true when the state ended. No allocation unless the cast finishes.
    /// The own pc gone (SetMyPC(0) of a field change frees MyPC and its state manager) ends the state with it, without a send
    /// or a bar cancel: the bar went with the old field's HUD, and the new field's bar is not this state's.
    /// </summary>
    public bool Tick(bool moveInput)
    {
        if (!active) return false;
        if (!host.HasPlayer) { End(); return true; }
        if (!finished && endMs != 0 && host.NowMs >= endMs) finished = true;
        if (finished)
        {
            if (host.RideeRowOf(itemUid, out int rideeId)) host.Send(new RideOnItemRequest(rideeId, itemUid, host.RideOption));
            End();
            return true;
        }
        if (moveInput) { Cancel(); return true; }
        return false;
    }

    /// <summary>vfunc_5 (@0x1417f7af0): leaving the state cancels the casting bar.</summary>
    public void Cancel()
    {
        if (!active) return;
        End();
        host.CancelCasting();
    }

    void End()
    {
        active = false;
        finished = false;
        endMs = 0;
        host.StopPlayerInteractAnim();
    }
}
