namespace Ms2.Ui;

/// <summary>The live field actions the action key can run, in the client's key order (Unk_ActionKeyManager's CRBMap).</summary>
public enum ActionKeyTarget
{
    /// <summary>m_nCur 0x1e: no action applies.</summary>
    None,
    /// <summary>GatheringAction (4), TaxiStationAction (8), InteractNpcAction (0x11) and InteractAction (0x12).</summary>
    Interact,
    /// <summary>PortalAction (0x13).</summary>
    Portal,
    /// <summary>TalkAction (0x14, actionkeyobject.cpp:511).</summary>
    Talk,
    /// <summary>LiftCubeAction (0x15, actionkeyobject.cpp:510, vfunc_5 @0x140957720).</summary>
    Lift,
    /// <summary>BillBoardInteractAction (0x18).</summary>
    BillBoard,
    /// <summary>PutDownLiftableAction (0xc), in the cube-mode map only (actionkeyobject.cpp:534).</summary>
    PutDownLiftable,
    /// <summary>ReactorAction (0x1a, actionkeyobject.cpp:2860-2990), in the normal map only.</summary>
    Reactor,
    /// <summary>HoldPCAction (0x1b, actionkeyobject.cpp:2298-2380), in the normal map only: board another player's mount.</summary>
    HoldPc,
}

/// <summary>
/// Unk_ActionKeyManager::Update (@0x140952ef0, actionkeyobject.cpp:549-603) and ActivateCurrentAction (@0x140953310,
/// :608-624): the actions are polled at most every 100 ms, by ascending key, and the first whose vfunc_1 applies is the
/// current one; the key runs only that action, so a refused action never hands the key to a later one.
/// </summary>
public struct ActionKeyPoll
{
    /// <summary>The poll interval (nNow - m_nNextTick &lt; 100 returns, :552).</summary>
    public const uint IntervalMs = 100;

    uint nextTick;
    bool started;

    /// <summary>The current action (m_nCur).</summary>
    public ActionKeyTarget Current;

    /// <summary>True when a poll is due at <paramref name="nowMs"/> (unsigned, so it survives the tick counter's wrap);
    /// the poll's time is then kept.</summary>
    public bool Due(uint nowMs)
    {
        if (started && nowMs - nextTick < IntervalMs) return false;
        started = true;
        nextTick = nowMs;
        return true;
    }

    /// <summary>The first action that applies, by key: interact (4 / 0x11 / 0x12), portal (0x13), talk (0x14), lift (0x15),
    /// billboard (0x18), reactor (0x1a), hold pc (0x1b).</summary>
    public static ActionKeyTarget Pick(bool interact, bool portal, bool talk, bool lift, bool billBoard, bool reactor = false,
        bool holdPc = false) =>
        interact ? ActionKeyTarget.Interact
        : portal ? ActionKeyTarget.Portal
        : talk ? ActionKeyTarget.Talk
        : lift ? ActionKeyTarget.Lift
        : billBoard ? ActionKeyTarget.BillBoard
        : reactor ? ActionKeyTarget.Reactor
        : holdPc ? ActionKeyTarget.HoldPc
        : ActionKeyTarget.None;

    /// <summary>The cube-mode map (m_actionsCubeMode, actionkeyobject.cpp:531-534), used while the local pc is in cube mode
    /// (GetActiveActionMap @0x140953770, :707-717): put down liftable (0xc), portal (0x13), talk (0x14). Its BuyCubeAction
    /// (0x16) never applies (vfunc_1 @0x140956f50 returns 0, :1224).</summary>
    public static ActionKeyTarget PickCubeMode(bool putDown, bool portal, bool talk) =>
        putDown ? ActionKeyTarget.PutDownLiftable
        : portal ? ActionKeyTarget.Portal
        : talk ? ActionKeyTarget.Talk
        : ActionKeyTarget.None;

    /// <summary>ActivateCurrentAction (:612-614): the current action runs only if the active map has its key, so an action
    /// picked before the cube mode changed (until the next poll) does nothing. Portal and talk are in both maps; put down
    /// liftable only in the cube-mode one.</summary>
    public readonly bool InActiveMap(bool cubeMode) =>
        cubeMode
            ? Current is ActionKeyTarget.None or ActionKeyTarget.PutDownLiftable or ActionKeyTarget.Portal or ActionKeyTarget.Talk
            : Current != ActionKeyTarget.PutDownLiftable;

    /// <summary>ActivateCurrentAction: the action the key runs, none while an item is held (:620,
    /// MyPC::GetHeldItem).</summary>
    public readonly ActionKeyTarget OnPress(bool heldItem) => heldItem ? ActionKeyTarget.None : Current;
}
