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

    /// <summary>CDropItem::FUN_141190640: room in the bag, or a stack to add to, or the key was freshly pressed (bForce).</summary>
    public static bool BagAllows(bool hasEmptySlot, bool canStack, bool force) => hasEmptySlot || canStack || force;

    /// <summary>OnFieldRemoveItem keeps a drop that is being picked up (FUN_1411900d0: a picker set and the state not resting).</summary>
    public static bool KeepsOnRemove(int pickerId, int state) => pickerId != 0 && state != DropMotion.Rest;

    /// <summary>
    /// The fade time SetState(1) and vfunc_53 give a drop (dropitem.cpp:470-478, 1137-1146): a pet item with the pet-trap flag, or a 0xc80
    /// drop that is not a pet item with it, stays hidden and unpickable for PetTrapDropVisibleDelay; any other drop has none (0).
    /// </summary>
    public static uint FadeUntil(uint createdMs, ushort dropKind, bool petItem, bool petTrap, uint delayMs) =>
        petTrap && (petItem || dropKind == 0xc80) ? createdMs + delayMs : 0;

    /// <summary>
    /// MyPC::FUN_141530db0 (mypc.cpp:4712-4721) and UpdateAutoLoot (:4555): no pickup while dead, nor while riding a mount whose row
    /// has disablePickUp set.
    /// </summary>
    public static bool PickupAllowed(bool dead, bool rideDisablesPickup) => !dead && !rideDisablesPickup;
}

/// <summary>One drop as the key handler sees it.</summary>
public struct PickupCandidate
{
    public int ObjectId, State;
    public uint LastRequest, FadeUntil;
    public float Distance;
    public bool Meso, Owner;
}

/// <summary>
/// The "get" key's choice (mypc.cpp:4740-4757), Godot-free: the mesos in range (CollectDropItems, at most 0x80) are stamped as requested
/// before FindNearestDropItem(1, 0) runs, so a meso in both ranges is sent once; with no drop in range the nearest one that ignores
/// the owner rule (FindNearestDropItem(1, 1)) hops (SetState(5)).
/// </summary>
public sealed class PickupPlanner
{
    public readonly List<PickupCandidate> Candidates = new();
    /// <summary>Indices into <see cref="Candidates"/> of the mesos to send.</summary>
    public readonly List<int> Mesos = new();
    /// <summary>The index to request (-1 none), and the index that hops when there is none to request.</summary>
    public int Nearest = -1, Hop = -1;

    public void Clear() { Candidates.Clear(); Mesos.Clear(); Nearest = Hop = -1; }

    public void Plan(uint nowMs, uint globalUntilMs, float itemRange, float moneyRange, int maxMesos)
    {
        Mesos.Clear(); Nearest = Hop = -1;
        for (int i = 0; i < Candidates.Count; i++)
        {
            var c = Candidates[i];
            if (c.Meso && Mesos.Count < maxMesos && DropPickup.Eligible(c.State, nowMs, globalUntilMs, c.LastRequest, c.FadeUntil, c.Distance, moneyRange, c.Owner))
                Mesos.Add(i);
        }
        foreach (int i in Mesos) { var c = Candidates[i]; c.LastRequest = nowMs; Candidates[i] = c; }   // FUN_141191030
        float best = float.MaxValue, bestHop = float.MaxValue;
        for (int i = 0; i < Candidates.Count; i++)
        {
            var c = Candidates[i];
            if (c.Distance < best && DropPickup.Eligible(c.State, nowMs, globalUntilMs, c.LastRequest, c.FadeUntil, c.Distance, itemRange, c.Owner)) { best = c.Distance; Nearest = i; }
            if (c.Distance < bestHop && DropPickup.Eligible(c.State, nowMs, globalUntilMs, c.LastRequest, c.FadeUntil, c.Distance, itemRange, true)) { bestHop = c.Distance; Hop = i; }
        }
        if (Nearest >= 0) Hop = -1;
    }
}
