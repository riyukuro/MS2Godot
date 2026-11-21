using System.Numerics;

namespace Ms2.Formats.World;

/// <summary>
/// The pickup tests of CDropItem (dropitem.cpp:782-880) and the key handler MyPC::FUN_141530db0 (mypc.cpp:4707). The clock is the
/// game's millisecond tick. The drop-guide restriction (<c>IsRestricted(7)</c>) is not modelled.
/// </summary>
public static class DropPickup
{
    /// <summary>CDropItem::HasPickupDelayElapsed: a request is not repeated before 1000 ms.</summary>
    public const uint RepeatDelayMs = 1000;
    /// <summary>The shared pick timer after a pickup (OnPickedUp: <c>s_kPickTimer.Start(0x96)</c>).</summary>
    public const uint AfterPickupMs = 0x96;

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
}
