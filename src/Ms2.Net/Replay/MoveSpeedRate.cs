using System;

namespace Ms2.Net.Replay;

/// <summary>
/// PCBase::vfunc_96 (@0x141671680, pcbase.cpp:2269-2275): a ridee's (class 0x6200045) move-speed rate is CLua::CalcRmsiR of its
/// current mount speed (stat 0x20), any other PC's CLua::CalcMsiR of its current movement speed (stat 0xe; the map type
/// it also passes is not handed to the script, luautil.cpp:458-470). Both script functions of Data/lua/Precompiled.m2d
/// luapack64.o, calc_msiR (source lines 93-96) and calc_rmsiR (lines 100-103), are clip_value(x / 100, 0.07, 1.7)
/// (clip_value, lines 83-87: the low bound first, then the high one), in double, returned as a float.
/// </summary>
public static class MoveSpeedRate
{
    /// <summary>PCBase stat ids: movement speed and mount speed (PC::vfunc_90's 0x23 block, pc.cpp:1653-1658).</summary>
    public const int MovementSpeedStat = 0xe, MountSpeedStat = 0x20;
    /// <summary>The stats PCBase keeps per kind (PCBase::vfunc_120 copies ids 0 to 0x22, pcbase.cpp:3317-3320).</summary>
    public const int StatCount = 0x23;

    /// <summary>calc_msiR.</summary>
    public static float Pc(long movementSpeed) => Clip(movementSpeed);

    /// <summary>calc_rmsiR.</summary>
    public static float Ridee(long mountSpeed) => Clip(mountSpeed);

    static float Clip(long stat)
    {
        double x = (double)stat / 100;
        if (x < 0.07) x = 0.07;
        if (1.7 < x) x = 1.7;
        return (float)x;
    }
}
