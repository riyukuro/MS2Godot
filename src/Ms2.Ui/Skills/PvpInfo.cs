namespace Ms2.Ui.Skills;

/// <summary>
/// A PC's CPvPInfo (commonfiles/pvpinfo.cpp): the point, team and mode CPvPInfo::ReadPointTeamMode reads (pvpinfo.cpp:156,
/// from FieldAddUser and Pvp 'd'); the ctor zeroes them.
/// </summary>
public readonly record struct PvpInfo(int Point, byte Team, byte Mode)
{
    /// <summary>CPvPInfo::HasPoint @0x140743550: 0 &lt; m_nPoint (PC::HasPvPPoint).</summary>
    public bool HasPoint => 0 < Point;

    /// <summary>
    /// PC::vfunc_116 (pc.cpp:4490), the PC's team for the hit filter and the interaction kind: the point when it has one, else 1.
    /// STILL-OPEN: the hide-and-seek sides (0xc9 / 0xca) and the water gun battle team (feature 0x10e) come before the 1; neither
    /// event is ported, so no PC has them.
    /// </summary>
    public int PcTeam => HasPoint ? Point : 1;
}
