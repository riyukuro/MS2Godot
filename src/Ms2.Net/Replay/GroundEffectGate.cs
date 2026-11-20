namespace Ms2.Net.Replay;

/// <summary>
/// The tests a PC's step or landing passes before its ground effects play.
/// </summary>
public static class GroundEffectGate
{
    /// <summary>JobGroupID 0x6e, whose "run*" steps are skipped in the battle stance (groundeffectdata.cpp:278-280).</summary>
    public const int HoveringJobGroup = 0x6e;
    /// <summary>The class id PCBase::TextKeyEvent skips (pcbase.cpp:1002, :1006).</summary>
    public const int SkippedClass = 0x6200028;

    /// <summary>
    /// PCBase::TextKeyEvent (@0x14166bf40, pcbase.cpp:997-1008): a text key starting "run" or "walk" plays that key's
    /// ground effects unless the PC is in state 0 (StateNone) or is class 0x6200028.
    /// </summary>
    public static bool StepKey(string textKey, int state, int classId) =>
        textKey != null && (textKey.StartsWith("run", StringComparison.Ordinal) || textKey.StartsWith("walk", StringComparison.Ordinal))
        && state != 0 && classId != SkippedClass;

    /// <summary>
    /// CGroundEffectDataManager::PlayEffects (@0x141377420, groundeffectdata.cpp:275-291), its tests in order: a PC of job
    /// group 0x6e in the battle stance (vfunc_65) skips an action that starts with "run"; an empty stored ground material
    /// (FUN_141671480) plays nothing; an object with m_unk_b0 set (GameObject::FUN_14123c660, set through vfunc_23, which
    /// the first-person camera uses to hide the own PC, fpscameramode.cpp:106-218) plays nothing; and while
    /// COptimizer::IsUpdateEnabled is off (m_nUpdateOn and m_nUpdateOn2, optimizer.cpp:639-650) only MyPC (kind 0x6200004)
    /// or a ridee MyPC rides plays (FUN_141b71120, util.cpp:1145-1158). There is no distance test.
    /// </summary>
    public static bool Allows(bool isPc, int jobGroup, bool battleStance, string action, string ground, bool flaggedB0,
        bool optimizerUpdate, bool isMyPcOrItsRidee)
    {
        if (isPc && jobGroup == HoveringJobGroup && battleStance && action.StartsWith("run", StringComparison.Ordinal)) return false;
        if (string.IsNullOrEmpty(ground)) return false;
        if (flaggedB0) return false;
        if (!optimizerUpdate && !isMyPcOrItsRidee) return false;
        return true;
    }
}
