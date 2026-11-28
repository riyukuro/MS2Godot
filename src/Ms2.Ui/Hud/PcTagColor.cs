namespace Ms2.Ui.Hud;

/// <summary>The constant a player's name tag colour comes from (PC::vfunc_53, pc.cpp:4455-4466).</summary>
public enum PcTagColor
{
    /// <summary>NameTagColorPC.</summary>
    Pc,
    /// <summary>NameTagColorMyPC.</summary>
    MyPc,
    /// <summary>NameTagColorPCHostile.</summary>
    Hostile,
    /// <summary>NameTagColorParty.</summary>
    Party,
}

public static class PcTagColors
{
    /// <summary>
    /// PC::vfunc_53's relation colour, set every frame (pc.cpp:4458-4465): the local player's own, else hostile when the local
    /// player's vfunc_122 says so (<see cref="Skills.SkillHit.PcHostile"/>), else party for a member of the local player's party
    /// (FUN_1415278d0), else the plain PC colour. A hostile party member stays hostile: the hostile test comes first.
    /// </summary>
    public static PcTagColor Of(bool self, bool hostile, bool party) =>
        self ? PcTagColor.MyPc : hostile ? PcTagColor.Hostile : party ? PcTagColor.Party : PcTagColor.Pc;
}
