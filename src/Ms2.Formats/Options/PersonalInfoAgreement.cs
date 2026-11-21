namespace Ms2.Formats.Options;

/// <summary>CPersonalInfoAgreement (gameoptionstruct.cpp:3363-3500, 0xc bytes): the agreement bits (2 and 4 group 0, the log
/// agreement; 8, 0x10 and 0x20 group 1) and the house visit flag derived from bit 2. The option page keeps one copy and
/// CGameOption (+0x1b4) the stored one.</summary>
public sealed class PersonalInfoAgreement
{
    /// <summary>m_bAgreeVisitHome (+0), the page's "agree" answer.</summary>
    public bool Agree;
    /// <summary>m_nFlags (+4).</summary>
    public uint Flags;

    public bool HasFlag(uint mask) => (Flags & mask) != 0;

    /// <summary>@0x141261f00.</summary>
    public bool HasGroupAgreement(int group) => group switch
    {
        0 => HasFlag(2) || HasFlag(4),
        1 => HasFlag(8) || HasFlag(0x10) || HasFlag(0x20),
        _ => false,
    };

    /// <summary>@0x141261fd0.</summary>
    public void ClearGroupFlags(int group)
    {
        if (group == 0) Flags &= ~6u;
        else if (group == 1) Flags &= ~0x38u;
    }

    /// <summary>@0x141261d40: Agree follows bit 2.</summary>
    public void RefreshAgree() => Agree = HasFlag(2);

    /// <summary>@0x141261e90: the whole flag word replaced, Agree refreshed.</summary>
    public void SetFlagsAndRefresh(uint flags)
    {
        Flags = flags;
        RefreshAgree();
    }

    /// <summary>@0x141261e40: the group's bits replaced, then Agree refreshed.</summary>
    public void SetGroup(int group, uint flags)
    {
        ClearGroupFlags(group);
        Flags |= flags;
        RefreshAgree();
    }

    /// <summary>@0x141261da0.</summary>
    public void CopyFrom(PersonalInfoAgreement o) { Agree = o.Agree; Flags = o.Flags; }
}

/// <summary>What MyPC::SendHouseSetting (@0x141530720, mypc.cpp:4385-4396) does to the agreement after its packet, with feature 0x117 on.</summary>
public static class HouseSettingAgreement
{
    /// <summary>Kind 0, 1 or 2 sets group 1 to flag 8, 0x10 or 0x20 (FUN_141261e40(1, mask)) and returns the group to send (SendAgreement(mask)); any other kind changes nothing and returns 0.</summary>
    public static int Apply(PersonalInfoAgreement agreement, int kind)
    {
        uint mask = kind switch { 0 => 8u, 1 => 0x10u, 2 => 0x20u, _ => 0u };
        if (mask == 0) return 0;
        agreement.SetGroup(1, mask);
        return (int) mask;
    }
}
