namespace Ms2.Ui;

/// <summary>The live field actions the action key can run, in the client's key order (Unk_ActionKeyManager's CRBMap).</summary>
public enum ActionKeyTarget
{
    /// <summary>m_nCur 0x1e: no action applies.</summary>
    None,
    /// <summary>GatheringAction (4), InteractNpcAction (0x11) and InteractAction (0x12).</summary>
    Interact,
    /// <summary>PortalAction (0x13).</summary>
    Portal,
    /// <summary>TalkAction (0x14, actionkeyobject.cpp:511).</summary>
    Talk,
    /// <summary>BillBoardInteractAction (0x18).</summary>
    BillBoard,
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

    /// <summary>The first action that applies, by key: interact (4 / 0x11 / 0x12), portal (0x13), talk (0x14), billboard
    /// (0x18).</summary>
    public static ActionKeyTarget Pick(bool interact, bool portal, bool talk, bool billBoard) =>
        interact ? ActionKeyTarget.Interact
        : portal ? ActionKeyTarget.Portal
        : talk ? ActionKeyTarget.Talk
        : billBoard ? ActionKeyTarget.BillBoard
        : ActionKeyTarget.None;

    /// <summary>ActivateCurrentAction: the action the key runs, none while an item is held (:620,
    /// MyPC::GetHeldItem).</summary>
    public readonly ActionKeyTarget OnPress(bool heldItem) => heldItem ? ActionKeyTarget.None : Current;
}
