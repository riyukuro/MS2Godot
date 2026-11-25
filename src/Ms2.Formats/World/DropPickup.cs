using System.Numerics;

namespace Ms2.Formats.World;

/// <summary>
/// The pickup tests of CDropItem (dropitem.cpp:782-880) and the key handler MyPC::FUN_141530db0 (mypc.cpp:4712). The clock is the
/// game's millisecond tick. The drop-guide restriction (<c>IsRestricted(7)</c>) is not modelled.
/// </summary>
public static class DropPickup
{
    /// <summary>CDropItem::HasPickupDelayElapsed: a request is not repeated before 1000 ms.</summary>
    public const uint RepeatDelayMs = 1000;
    /// <summary>The shared pick timer after a pickup (OnPickedUp: <c>s_kPickTimer.Start(0x96)</c>).</summary>
    public const uint AfterPickupMs = 0x96;
    /// <summary>The shared pick timer a CDropItem constructor restarts (dropitem.cpp:212: <c>s_kPickTimer.Start(10)</c>).</summary>
    public const uint AfterCreateMs = 10;

    /// <summary>The distance the pickup is allowed from: DropItemMaxLength for an item, DropMoneyMaxLength for a meso.</summary>
    public static float Range(DropConstants k, bool meso) => meso ? k.MoneyMaxLength : k.MaxLength;

    /// <summary>
    /// FUN_1411903c0 with nA = 1, nB = 0: the drop resting (state 0, so no picker set), the shared timer and the drop's own request
    /// delay over, the pet-trap fade over, the actor within <paramref name="range"/>, and the owner rule.
    /// </summary>
    public static bool Eligible(int state, uint nowMs, uint globalUntilMs, uint lastRequestMs, uint fadeUntilMs, float distance, float range, bool ownerAllowed)
    {
        if (nowMs < globalUntilMs) return false;                   // !s_kPickTimer.IsExpired()
        if (state != DropMotion.Rest) return false;                // FUN_1411900d0 && nA, or m_nState && nA
        if (lastRequestMs + RepeatDelayMs > nowMs) return false;   // !HasPickupDelayElapsed
        if (nowMs < fadeUntilMs) return false;                     // GetTickCountMs2() < m_nFadeTime
        if (distance > range) return false;
        return ownerAllowed;
    }

    /// <summary>
    /// CDropItem::FUN_141190640 (dropitem.cpp:906), the get key's test before the request: room in the bag, or a stack to add to, or
    /// the key was freshly pressed (bForce). It has no item test: a currency or special id skips only FUN_141190900's bag-full
    /// refusal (<see cref="BagFull"/>), not this.
    /// </summary>
    public static bool BagAllows(bool hasEmptySlot, bool canStack, bool force) => hasEmptySlot || canStack || force;

    /// <summary>
    /// The range a resting drop has for the summoned pet's item pick (WorldService::FUN_141bd1a50, worldservice_1.cpp:6347, from
    /// StateSummonItemPick, statesummon.cpp:902): state 0's FUN_141191a10 sets m_f27c to the distance from the drop's origin to where
    /// it is now plus 10 (dropitem.cpp:1211), and FUN_141190120 tests against the larger of that and m_f21c (:811).
    /// <paramref name="drift"/> is that distance and <paramref name="maxLength"/> m_f21c. The player's own pickup uses m_f21c alone
    /// (FUN_141190320).
    /// </summary>
    public static float WidenedRange(float drift, float maxLength) => MathF.Max(drift + 10f, maxLength);

    /// <summary>OnFieldRemoveItem keeps a drop that is being picked up (FUN_1411900d0: a picker set and the state not resting).</summary>
    public static bool KeepsOnRemove(int pickerId, int state) => pickerId != 0 && state != DropMotion.Rest;

    /// <summary>
    /// The fade time SetState(1) and vfunc_53 give a drop (dropitem.cpp:470-478, 1137-1146): a pet item with the pet-trap flag, or a 0xc80
    /// drop that is not a pet item with it, stays hidden and unpickable for PetTrapDropVisibleDelay; any other drop has none (0).
    /// </summary>
    public static uint FadeUntil(uint createdMs, ushort dropKind, bool petItem, bool petTrap, uint delayMs) =>
        petTrap && (petItem || dropKind == 0xc80) ? createdMs + delayMs : 0;

    /// <summary>The drop is still hidden (vfunc_23(1)) and, for a 0xc80 non-pet drop, not yet thrown: before its fade time (dropitem.cpp:1137-1150).</summary>
    public static bool StillHidden(uint nowMs, uint fadeUntilMs) => fadeUntilMs != 0 && nowMs < fadeUntilMs;

    /// <summary>
    /// MyPC::FUN_141530db0 (mypc.cpp:4712-4721) and UpdateAutoLoot (:4555): no pickup while dead, nor while riding a mount whose row
    /// has disablePickUp set.
    /// </summary>
    public static bool PickupAllowed(bool dead, bool rideDisablesPickup) => !dead && !rideDisablesPickup;

    /// <summary>The system notice FUN_141190900 shows when the bag has no room (FUN_141275260(0x43, 1, 1)).</summary>
    public const int BagFullNotice = 0x43;
    /// <summary>The notice the get key shows when the only drop near is out of reach (mypc.cpp:4750: FUN_141275260(0x45, 1, 1)).</summary>
    public const int TooFarNotice = 0x45;
    /// <summary>s_strWarningBag: the system sound of the bag-full refusal.</summary>
    public const string BagFullSound = "system_notice_error_warningBag";
    /// <summary>OnFieldRemoveItem's fade: vfunc_40(1, 0, 0, 0.5), alpha 1 to 0 over half a second before the drop is freed.</summary>
    public const float RemoveFadeSeconds = 0.5f;

    /// <summary>The alpha of a removed drop <paramref name="seconds"/> into its fade.</summary>
    public static float RemoveAlpha(float seconds) => seconds >= RemoveFadeSeconds ? 0 : 1 - Math.Max(seconds, 0) / RemoveFadeSeconds;

    /// <summary>FUN_14053f8c0: the three meso ids.</summary>
    public static bool IsMesoId(int id) => id is 0x55d4a81 or 0x55d4a82 or 0x55d4a83;
    /// <summary>FUN_14053f900: the special ids (EP and SP balls and the like) that skip the bag test and the toast.</summary>
    public static bool IsSpecialId(int id) => id is 0x55d4a88 or 0x55d4a89 or 0x55d4a8a or 0x55d4a93;
    /// <summary>The SP and EP orbs, the two special ids InitDrop gives the DropSPEPBall constants (dropitem.cpp:269-270).</summary>
    public static bool IsSpepBallId(int id) => id is 0x55d4a89 or 0x55d4a8a;
    /// <summary>FUN_14053f940: the merat ids.</summary>
    public static bool IsMeratId(int id) => id is 0x55d4a84 or 0x55d4a8b or 0x55d4a8f or 0x55d4a90;
    /// <summary>FUN_14053f990 through FUN_1403ba790: the token ids (the currency kinds it maps to a non-zero type).</summary>
    public static bool IsTokenId(int id) => id is 90000001 or 90000002 or 90000003 or 90000004 or 90000006 or 90000011 or 90000013
        or 90000014 or 90000015 or 90000016 or 90000017 or 90000018 or 90000020 or 90000022 or 90000023 or 90000024 or 90000025 or 90000027;
    /// <summary>CItem::IsCurrency: a meso, a merat or a token.</summary>
    public static bool IsCurrencyId(int id) => IsMesoId(id) || IsMeratId(id) || IsTokenId(id);

    /// <summary>
    /// FUN_141190900: whether the bag-full refusal (notice 0x43, the warning sound, the hop) comes before the request. A currency or
    /// special id is sent with no test; otherwise room in the item's tab sends it, and with no room a stack to add to does. The
    /// request is sent in every case.
    /// </summary>
    public static bool BagFull(int itemId, bool tabHasRoom, bool canStack) =>
        !IsCurrencyId(itemId) && !IsSpecialId(itemId) && !tabHasRoom && !canStack;

    /// <summary>
    /// OnPickedUp's reward toast (dropitem.cpp:1007-1016 and UIRewardNotify::FUN_141abfa10): the local player picked up an item of
    /// rarity 1 or more that is not a meso (<paramref name="meso"/>: CDropItem::IsMeso, a meso id with an amount), a token, a merat
    /// or a special id.
    /// </summary>
    public static bool Toasts(int itemId, int rarity, bool meso) =>
        rarity >= 1 && !meso && !IsTokenId(itemId) && !IsMeratId(itemId) && !IsSpecialId(itemId);

    /// <summary>
    /// CItem::FUN_140528980 (item.cpp:1717), OnPickedUp's test before guide trigger 0xc (ItemPickup): false for an expired item
    /// (IsExpired: an expiry above 0 and not after the server's now) that is neither a pet nor one with a maid id.
    /// STILL-OPEN: a score item's tests (its notes and its custom score's state) are taken as passed.
    /// </summary>
    public static bool GuideCounts(long expiry, long now, bool pet, bool hasMaidId) =>
        !(expiry > 0 && expiry <= now && !pet && !hasMaidId);
}

/// <summary>One drop as the key handler sees it.</summary>
public struct PickupCandidate
{
    public int ObjectId, State;
    public uint LastRequest, FadeUntil;
    public float Distance;
    public bool Meso, Owner;
    /// <summary>CDropItem m_n294 (FUN_141533e40): a pickup was requested and not refused with mode 1 since.</summary>
    public bool Requested;
    /// <summary>The drop came out of the local player (GetOwnerObjId() == the player's object id, mypc.cpp:4728).</summary>
    public bool FromSelf;
    /// <summary>The auto loot options take the item (<see cref="Ms2.Formats.Options.GameOption.AutoLoot.Takes"/>).</summary>
    public bool AutoTakes;
    /// <summary>The drop's own item range, m_f21c (FUN_141190320): DropSPEPBallMaxLength for an SP or EP orb; 0 takes the frame's.</summary>
    public float Range;
}

/// <summary>
/// One frame of MyPC::FUN_141530db0 (mypc.cpp:4712-4760), Godot-free. CollectDropItems(items, mesos, 0, 1, 0) takes the eligible drops
/// once at the start (worldservice_1.cpp:6388: a meso in the money range, any other drop in the item range, the owner rule kept).
/// Auto loot then requests every collected item not flagged, not from the player and taken by the options (<see cref="Auto"/>; each
/// request stamps the drop). With the meso option the collected mesos are sent (FUN_1415310e0, at most 0x80); with the get key held
/// the same list is sent again (FUN_1411900d0 is still false for a resting drop, so a meso goes twice in that frame, as in the
/// client), then FindNearestDropItem(1, 0) over the stamped drops, else the nearest out-of-owner one hops (FindNearestDropItem(1, 1)).
/// </summary>
public sealed class PickupPlanner
{
    public readonly List<PickupCandidate> Candidates = new();
    /// <summary>Indices into <see cref="Candidates"/> of the mesos to send.</summary>
    public readonly List<int> Mesos = new();
    /// <summary>Indices of the items auto loot requests, in the candidates' order.</summary>
    public readonly List<int> Auto = new();
    /// <summary>How many RequestMesoPickup packets of <see cref="Mesos"/> the frame sends: one for the meso option, one for the key.</summary>
    public int MesoSends;
    /// <summary>The index to request (-1 none), and the index that hops when there is none to request.</summary>
    public int Nearest = -1, Hop = -1;

    public void Clear() { Candidates.Clear(); Mesos.Clear(); Auto.Clear(); MesoSends = 0; Nearest = Hop = -1; }

    /// <summary>The get key alone (no auto loot): the frame with the key held and the meso option off.</summary>
    public void Plan(uint nowMs, uint globalUntilMs, float itemRange, float moneyRange, int maxMesos) =>
        Frame(nowMs, globalUntilMs, itemRange, moneyRange, maxMesos, autoMeso: false, key: true);

    public void Frame(uint nowMs, uint globalUntilMs, float itemRange, float moneyRange, int maxMesos, bool autoMeso, bool key)
    {
        Mesos.Clear(); Auto.Clear(); MesoSends = 0; Nearest = Hop = -1;
        for (int i = 0; i < Candidates.Count; i++)
        {
            var c = Candidates[i];
            if (c.Meso)
            {
                if (Mesos.Count < maxMesos && DropPickup.Eligible(c.State, nowMs, globalUntilMs, c.LastRequest, c.FadeUntil, c.Distance, moneyRange, c.Owner))
                    Mesos.Add(i);
            }
            else if (!c.Requested && !c.FromSelf && c.AutoTakes
                     && DropPickup.Eligible(c.State, nowMs, globalUntilMs, c.LastRequest, c.FadeUntil, c.Distance, c.Range > 0 ? c.Range : itemRange, c.Owner))
                Auto.Add(i);
        }
        foreach (int i in Auto) { var c = Candidates[i]; c.LastRequest = nowMs; c.Requested = true; Candidates[i] = c; }   // SendRequestItemPickup
        if (autoMeso && Mesos.Count > 0) MesoSends++;
        if (!key) { if (MesoSends == 0) Mesos.Clear(); return; }
        if (Mesos.Count > 0) MesoSends++;
        foreach (int i in Mesos) { var c = Candidates[i]; c.LastRequest = nowMs; Candidates[i] = c; }   // FUN_141191030
        float best = float.MaxValue, bestHop = float.MaxValue;
        for (int i = 0; i < Candidates.Count; i++)
        {
            var c = Candidates[i];
            float range = c.Range > 0 ? c.Range : itemRange;
            if (c.Distance < best && DropPickup.Eligible(c.State, nowMs, globalUntilMs, c.LastRequest, c.FadeUntil, c.Distance, range, c.Owner)) { best = c.Distance; Nearest = i; }
            if (c.Distance < bestHop && DropPickup.Eligible(c.State, nowMs, globalUntilMs, c.LastRequest, c.FadeUntil, c.Distance, range, true)) { bestHop = c.Distance; Hop = i; }
        }
        if (Nearest >= 0) Hop = -1;
    }
}
