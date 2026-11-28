namespace Ms2.Ui.Hud;

/// <summary>
/// The rules of another player's head bar (UIHpBar::Entity of a PC, uihpbar.cpp) that read the PvP state: the PC's point
/// (PC::HasPvPPoint, CPvPInfo::HasPoint) and the local player's relation to it (PC::vfunc_122, true when hostile).
/// </summary>
public static class PcHpBarRules
{
    /// <summary>The frame of a PC with a PvP point (FUN_141992680: m_b0 = m_c4, 100 from the ctor); every other PC's is 1.</summary>
    public const int PvpRank = 100;

    /// <summary>
    /// PC::vfunc_80 (pc.cpp:2758-2772): the bar is made when the status option at +0xc (DisplayPartyHpGauge) is on and the PC
    /// has a PvP point, is a party member (MyPC::FUN_1415278d0) or is hostile; else none.
    /// </summary>
    public static bool Makes(bool partyGauge, bool hasPoint, bool partyMember, bool hostile) =>
        partyGauge && (hasPoint || partyMember || hostile);

    /// <summary>
    /// FUN_1419923f0 (uihpbar.cpp:313-354), read every frame: a hostile PC's bar follows the monster option (+0xe,
    /// DisplayMonsterHpGauge); any other PC's needs a party member and follows the party option (+0xc).
    /// </summary>
    public static bool Gate(bool partyGauge, bool monsterGauge, bool partyMember, bool hostile) =>
        hostile ? monsterGauge : partyMember && partyGauge;

    /// <summary>
    /// UIHpBar::Entity::vfunc_8's PC branch (uihpbar.cpp:184-228) once the gate, the shown flag (m_b8) and the death let the
    /// bar through: true hides it. A hostile PC hidden by its buffs (PCBase::FUN_141674bc0, the buff list's IsHidden) hides;
    /// a ride with nameTagHide hides; a PC with a point shows unless hidden by its buffs; then the battle stance or hp below
    /// max shows; the rest goes to the base's distance test (<paramref name="far"/>, Entity::vfunc_8's m_54).
    /// </summary>
    public static bool Hidden(bool hostile, bool buffHidden, bool rideHidesTag, bool hasPoint, bool inBattle, bool hurt, bool far)
    {
        if (hostile && buffHidden) return true;
        if (rideHidesTag) return true;
        if (hasPoint) return buffHidden;
        if (inBattle || hurt) return false;
        return far;
    }

    /// <summary>FUN_141992680: the frame the bar opens at (vfunc_5 and the ctor only, so a change waits for the next open).</summary>
    public static int Rank(bool hasPoint) => hasPoint ? PvpRank : 1;

    /// <summary>FUN_141992580 (uihpbar.cpp:357-377): m_b4, 1 ("friendly") unless the local player finds the PC hostile (0,
    /// "hostile"); the local PC's own bar is always friendly. vfunc_6 sets the gauge's label when it changes (:159-167).</summary>
    public static bool Friendly(bool self, bool hostile) => self || !hostile;
}
