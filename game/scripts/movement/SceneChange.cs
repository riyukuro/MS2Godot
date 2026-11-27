namespace Ms2Godot.Movement;

/// <summary>
/// WorldService's m_928 (worldservice_1.cpp): FUN_141bc8e00 (:4995-5009) sets it when the field's colliders changed (an
/// entity's collision group switched with a refresh, CPhysXHelper::SetEntityCollisionGroup physxhelper.cpp:418; a breakable's
/// box released, breakableobject.cpp:747; an interact object's swap, interactobject.cpp:901), and the world update clears it
/// at its end (:9842). PCBase::vfunc_68 reads it (FUN_141bc9a80, :451): while it is set a body standing still runs its own
/// step (gravity) instead of holding, so it falls when its ground went away. The own player reads it once a frame
/// (<see cref="ReadAndClear"/>), which clears it, as the client's one world update does.
/// </summary>
public static class SceneChange
{
    static bool changed;

    /// <summary>FUN_141bc8e00: the colliders changed.</summary>
    public static void Report() => changed = true;

    /// <summary>FUN_141bc9a80 for this frame's move; the flag is cleared.</summary>
    public static bool ReadAndClear()
    {
        bool was = changed;
        changed = false;
        return was;
    }
}
