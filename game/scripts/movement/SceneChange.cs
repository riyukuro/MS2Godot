namespace Ms2Godot.Movement;

/// <summary>
/// WorldService's m_928 (worldservice_1.cpp): FUN_141bc8e00 (:4995-5009) sets it when the field's colliders changed (an
/// entity's collision group switched with a refresh, CPhysXHelper::SetEntityCollisionGroup physxhelper.cpp:418; a breakable's
/// box released, breakableobject.cpp:747; an interact object's swap, interactobject.cpp:901), and the world update clears it
/// at its end (:9842). PCBase::vfunc_68 reads it (FUN_141bc9a80, :451): while it is set a body standing still runs its own
/// step (gravity) instead of holding, so it falls when its ground went away. The own player and every remote PC read it
/// for the frame (<see cref="Read"/>, Ms2.Ui.FrameFlag): all of one frame see the same value, as the client's PCBases do
/// in its one world update.
/// </summary>
public static class SceneChange
{
    static readonly Ms2.Ui.FrameFlag changed = new();

    /// <summary>FUN_141bc8e00: the colliders changed.</summary>
    public static void Report() => changed.Set();

    /// <summary>FUN_141bc9a80 for this frame's moves.</summary>
    public static bool Read() => changed.Read(Godot.Engine.GetProcessFrames());
}
