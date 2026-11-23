namespace Ms2.Ui.Skills;

/// <summary>
/// CSkillSystem::FUN_141769440 @0x141769440 (skillslot.cpp:2880-3004): the center notice (FUN_141275260(id, 1, 0), a stringcommon
/// id) and the system sound a refused cast shows. Only the EP reasons are mapped here: 3 (an EP skill in a field without EP
/// skills) shows 0x53, 7 (not enough EP) shows 0x48 with the system_notice_error_ep sound (the ride's no-EP notice is the same
/// string and sound, ridee.cpp:347), and 10 (a recovery skill at full EP) shows 0x52b. STILL-OPEN: the other reasons' notices;
/// reason 10 itself is not produced by <see cref="CastGate.Why"/> (the recovery effect check needs the additional effect
/// recovery block, which is not ported), so only its notice is here.
/// </summary>
public static class CastNotice
{
    public const string NoEpSound = "system_notice_error_ep";

    /// <summary>The notice's stringcommon id and the sound key (null for none) of <paramref name="why"/>; id 0 when this reason has no mapped notice.</summary>
    public static (int StringId, string Sound) Of(int why) => why switch
    {
        3 => (0x53, null),
        7 => (0x48, NoEpSound),
        10 => (0x52b, null),
        _ => (0, null),
    };
}
