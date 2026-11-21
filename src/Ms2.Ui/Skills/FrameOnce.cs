namespace Ms2.Ui.Skills;

/// <summary>
/// One step per frame: the client's StateSkill::vfunc_8 @0x1417e6990 runs FUN_1417ee120's end step once a frame
/// (stateskill.cpp:2889-2928), but here the sequence's end (an animation callback) and the HUD's frame both reach it.
/// <see cref="Take"/> is true for the first call in a frame only. 0 B.
/// </summary>
public struct FrameOnce
{
    ulong last;   // the frame taken + 1, 0 for none

    /// <summary>True when <paramref name="frame"/> was not taken yet; it is taken then.</summary>
    public bool Take(ulong frame)
    {
        if (last == frame + 1) return false;
        last = frame + 1;
        return true;
    }
}
