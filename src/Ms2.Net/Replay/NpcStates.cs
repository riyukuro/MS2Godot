namespace Ms2.Net.Replay;

/// <summary>The states a CNpc has (CNpc init, unit_14108e1e0_npcactorandvibrate.cpp:479-490) and which of them a sync block enters.</summary>
public static class NpcStates
{
    /// <summary>StateIdleNPC 1, StateWalkNPC 2, StateJumpNpc 6, StateDeadNPC 0xc, StateHitNPC 0xd, StateSkill 0x10, StateSpawn 0x11,
    /// StateStun 0x12, StateTalkNpc 0x16, StateRegen 0x17, StatePuppetNpc 0x23, StateSummonPortal 0x38.</summary>
    public static bool Registered(byte state) => state is 1 or 2 or 6 or 0xc or 0xd or 0x10 or 0x11 or 0x12 or 0x16 or 0x17 or 0x23 or 0x38;

    /// <summary>
    /// Whether a sync block with a new serial changes the npc to <paramref name="state"/> (ApplySyncBlock :1024-1032): not gosNone (0),
    /// not 0x12 (the block returns before the change), and only a state the npc has: CStateManager::ChangeState returns for any other
    /// (statemanager.cpp:295), so the npc stays in its state (an emotion state 0xb, which PCs have, is one).
    /// </summary>
    public static bool BlockEnters(byte state) => state != 0x12 && Registered(state);
}
