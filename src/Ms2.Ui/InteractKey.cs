using Ms2.Formats.Model;

namespace Ms2.Ui;

/// <summary>
/// The interact part of the action key: GatheringAction (4), TaxiStationAction (8), InteractNpcAction (0x11) and
/// InteractAction (0x12) polled in
/// key order, and the client state the interaction gates read (MyPC::GetCurStateId) while one of their casts runs.
/// </summary>
public static class InteractKey
{
    /// <summary>StateInteraction's state id (vfunc_59(0x19), actionkeyobject.cpp:1574-1590).</summary>
    public const int StateInteraction = 0x19;
    /// <summary>StateInteractionNpc's state id (vfunc_59(0x1a), actionkeyobject.cpp:1650).</summary>
    public const int StateInteractionNpc = 0x1a;

    /// <summary>The player's state id: 0x19 while the StateInteraction cast runs, 0x1a while the StateInteractionNpc cast
    /// runs, else <paramref name="otherState"/>. Neither is a state MyPC::FUN_141527160 lets the next interaction start
    /// from (mypc.cpp:1873-1882).</summary>
    public static int StateId(LiveInteractObjects objects, LiveInteractNpc npcs, int otherState)
    {
        if (objects != null && objects.IsCasting) return StateInteraction;
        if (npcs != null && npcs.IsCasting) return StateInteractionNpc;
        return otherState;
    }

    /// <summary>As the other overload, with StateReact: 0x3d (<see cref="ReactState.StateId"/>) while the react cast or the
    /// create animation runs. 0x3d is none of the states FUN_141527160 lets the npc or object interaction start from, so a
    /// running react refuses them; TaxiStationAction::vfunc_1 refuses only 0x25 and still polls (actionkeyobject.cpp:769).</summary>
    public static int StateId(LiveInteractObjects objects, LiveInteractNpc npcs, ReactState react, int otherState)
    {
        int id = StateId(objects, npcs, otherState);
        return react != null ? react.CurStateId(id) : id;
    }

    /// <summary>The server put the own pc into a state (<see cref="Ms2.Net.Packets.MyCharacter.StateEntered"/>: OnUserState,
    /// OnDeadUser, OnRevival): MyPC::vfunc_59 -> CStateManager::ChangeState leaves the current state, so a running
    /// StateInteraction (0x19, stateinteractdata.cpp:330), StateInteractionNpc (0x1a, :613) or StateReact (0x3d) runs its
    /// vfunc_5, which cancels the bar without a send. The same state again leaves it too unless the state's vfunc_25 keeps
    /// it (statemanager.cpp:297): StateInteraction and StateReact return false, StateInteractionNpc has State::vfunc_25,
    /// which returns true, so a server 0x1a keeps the npc cast.</summary>
    public static void LeaveForServerState(int state, LiveInteractObjects objects, LiveInteractNpc npcs, ReactState react)
    {
        objects?.CancelCast();
        if (state != 0x1a) npcs?.CancelCast();
        react?.End();
    }

    /// <summary>The polls by ascending key; <paramref name="npc"/> tells whether the npc action took the key. 0 B.</summary>
    public static bool Poll(LiveInteractObjects objects, LiveInteractNpc npcs, Vec3 at, Vec3 npcAt, out bool npc) =>
        Poll(objects, npcs, at, npcAt, null, out npc, out _);

    /// <summary>As the other overload, with TaxiStationAction (key 8) polled between gathering (4) and the interact npc
    /// (0x11); <paramref name="taxi"/> is its vfunc_1 and runs only when gathering doesn't apply. 0 B with a cached
    /// delegate.</summary>
    public static bool Poll(LiveInteractObjects objects, LiveInteractNpc npcs, Vec3 at, Vec3 npcAt, System.Func<bool> taxi,
                            out bool npc, out bool onTaxi)
    {
        npc = false;
        onTaxi = false;
        if (objects != null && objects.PollGatheringAction(at)) return true;
        if (taxi != null && taxi()) { onTaxi = true; return true; }
        if (npcs != null && npcs.Poll(npcAt)) { npc = true; return true; }
        return objects != null && objects.PollInteractAction(at);
    }

    /// <summary>The key on the action the poll picked: only that action's vfunc_2 runs; its own state gate refuses it while
    /// another interaction's cast runs, and that cast keeps running.</summary>
    public static bool Use(LiveInteractObjects objects, LiveInteractNpc npcs, bool npc, Vec3 at) =>
        npc ? npcs != null && npcs.Use() : objects != null && objects.Use(at);
}
