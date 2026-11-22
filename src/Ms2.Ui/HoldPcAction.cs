namespace Ms2.Ui;

/// <summary>What <see cref="HoldPcAction"/> reads of the own pc and the field.</summary>
public interface IHoldPcWorld
{
    /// <summary>A local player that is not dead and whose move gate (FUN_141683ef0) passes.</summary>
    bool HoldPcUsable { get; }
    /// <summary>The own pc's state id (GetCurStateId).</summary>
    int HoldPcState { get; }
    /// <summary>The own pc's object id.</summary>
    int HoldPcSelfId { get; }
    /// <summary>The own pc rides (GetRidee).</summary>
    bool HoldPcRiding { get; }
    /// <summary>PC::FindHoldTarget (WorldService::FindNearestActor): the actor with the nearest hold node within
    /// <paramref name="range"/> of the own feet, the own pc included, and that node's name; 0 when none.</summary>
    int FindHoldTarget(float range, out string node);
    /// <summary>WorldService::FindPCById finds another player.</summary>
    bool IsPc(int objectId);
    /// <summary>MyPC::FUN_1415279d0: the player's mount can take the own pc (see <see cref="HoldPcAction.CanBoard"/>).</summary>
    bool CanBoard(int objectId);
    /// <summary>PC::FUN_1416642a0: the player's m_b1224 (ResponseRide mode 6), no boarding.</summary>
    bool NoBoarding(int objectId);
}

/// <summary>
/// HoldPCAction (action key 0x1b, actionkeyobject.cpp:2298-2380): board another player's mount. The poll (vfunc_1) keeps
/// the player found (m_n18); the press (vfunc_2) rides off with a RideOffActionHold when the own pc rides, else sends
/// RequestRide mode 3 for that player.
/// </summary>
public sealed class HoldPcAction
{
    /// <summary>vfunc_5: the action's key.</summary>
    public const int Key = 0x1b;
    /// <summary>vfunc_6: the label while a player is kept.</summary>
    public const string LabelKey = "s_action_hold_pc";
    /// <summary>The ClientConstants FindHoldTargetRange default (GetClientConstants()->FindHoldTargetRange.Get(230)).</summary>
    public const float DefaultRange = 230f;
    /// <summary>MyPC::vfunc_121's case 7 searches 20 further than the poll (mypc.cpp:3449).</summary>
    public const float RideOffExtraRange = 20f;
    /// <summary>The own states the key refuses: 0x2b in both, 0x20 (StateHold) in both, 0x2f in the poll.</summary>
    public const int StateRefused = 0x2b, StateHold = 0x20, StatePollRefused = 0x2f;

    public enum Press { None, RideOff, Board }

    /// <summary>m_n18: the player the last poll kept.</summary>
    public int Target { get; private set; }

    /// <summary>
    /// vfunc_1 (@0x14095c720): 0 for no usable player or state 0x2b; the hold target found within
    /// <paramref name="range"/>; the own pc's id refuses; a target that is not a player clears m_n18 and refuses; for a
    /// player, state 0x20, a refused FUN_1415279d0, state 0x2f or its no-boarding flag refuse (m_n18 kept as it was), else
    /// m_n18 is the player.
    /// </summary>
    public bool Poll(IHoldPcWorld w, float range = DefaultRange)
    {
        if (!w.HoldPcUsable) return false;
        int state = w.HoldPcState;
        if (state == StateRefused) return false;
        int id = w.FindHoldTarget(range, out _);
        if (id == 0) return false;
        if (id == w.HoldPcSelfId) return false;
        if (!w.IsPc(id)) { Target = 0; return false; }
        if (state == StateHold) return false;
        if (!w.CanBoard(id)) return false;
        if (state == StatePollRefused) return false;
        if (w.NoBoarding(id)) return false;
        Target = id;
        return true;
    }

    /// <summary>vfunc_2 (@0x14095c9a0): nothing for no usable player or state 0x2b / 0x20; a rider rides off
    /// (SendRequestRideMode1(RideOffActionHold(0))); else mode 3 for m_n18 when one is kept.</summary>
    public Press Use(IHoldPcWorld w)
    {
        if (!w.HoldPcUsable) return Press.None;
        int state = w.HoldPcState;
        if (state == StateRefused || state == StateHold) return Press.None;
        if (w.HoldPcRiding) return Press.RideOff;
        return Target != 0 ? Press.Board : Press.None;
    }

    /// <summary>vfunc_6 (@0x14095cad0): the label key while m_n18 is set, else none.</summary>
    public string Label => Target != 0 ? LabelKey : null;

    /// <summary>
    /// MyPC::FUN_1415279d0 (@0x1415279d0, mypc.cpp:2010-2038): the other player has a ridee with a free passenger seat
    /// (PCBase::HasFreePassengerSeat: m_mapB28's count below the ridee's passenger count), the own pc sees it
    /// (HasLineOfSightTo); with feature 0xad on, it must also be a friend (CBuddyManager FUN_140ae4d70 by its character id),
    /// a party member (FUN_1415278d0) or of the own non-empty guild name.
    /// </summary>
    public static bool CanBoard(bool hasRidee, int passengers, int seats, bool lineOfSight, bool friendsOnly, bool buddy,
        bool partyMember, string ownGuild, string otherGuild)
    {
        if (!hasRidee || passengers >= seats || !lineOfSight) return false;
        if (!friendsOnly) return true;
        if (buddy || partyMember) return true;
        return !string.IsNullOrEmpty(ownGuild) && ownGuild == otherGuild;
    }

    /// <summary>The feature CanBoard's friend test hangs on (CFeatureManager::IsFeatureEnabled(0xad)).</summary>
    public const int FriendsOnlyFeature = 0xad;

    /// <summary>
    /// MyPC::vfunc_121's case 7 (mypc.cpp:3446-3466), after the ride off: the hold target within the range + 20; a player
    /// with a ridee that FUN_1415279d0 accepts gets mode 3; one refused, or a player not found, nothing; any other target
    /// would enter StateHold on it (vfunc_59(0x20)), which <see cref="RideOffOutcome.Hold"/> reports.
    /// </summary>
    public static RideOffOutcome AfterRideOff(IHoldPcWorld w, bool targetHasRidee, int target, out int boardId)
    {
        boardId = 0;
        if (target == 0) return RideOffOutcome.None;
        if (!w.IsPc(target)) return RideOffOutcome.None;
        if (!targetHasRidee) return RideOffOutcome.Hold;
        if (!w.CanBoard(target)) return RideOffOutcome.None;
        boardId = target;
        return RideOffOutcome.Board;
    }

    public enum RideOffOutcome { None, Board, Hold }
}
