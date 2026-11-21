using Ms2.Formats.Model;

namespace Ms2.Ui;

/// <summary>
/// The interact part of the action key: GatheringAction (4), InteractNpcAction (0x11) and InteractAction (0x12) polled in
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

    /// <summary>The polls by ascending key; <paramref name="npc"/> tells whether the npc action took the key. 0 B.</summary>
    public static bool Poll(LiveInteractObjects objects, LiveInteractNpc npcs, Vec3 at, Vec3 npcAt, out bool npc)
    {
        npc = false;
        if (objects != null && objects.PollGatheringAction(at)) return true;
        if (npcs != null && npcs.Poll(npcAt)) { npc = true; return true; }
        return objects != null && objects.PollInteractAction(at);
    }

    /// <summary>The key on the action the poll picked: only that action's vfunc_2 runs; its own state gate refuses it while
    /// another interaction's cast runs, and that cast keeps running.</summary>
    public static bool Use(LiveInteractObjects objects, LiveInteractNpc npcs, bool npc, Vec3 at) =>
        npc ? npcs != null && npcs.Use() : objects != null && objects.Use(at);
}
