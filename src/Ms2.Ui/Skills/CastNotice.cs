namespace Ms2.Ui.Skills;

/// <summary>
/// CSkillSystem::FUN_141769440 @0x141769440 (skillslot.cpp:2902-3004): the center notice and the system sound a refused cast
/// shows, for every reason the client's switch lists. The notice is FUN_141275260(id, 1, 0) (the stringcommon text of the
/// id, nothing when it is empty) or ShowSystemNotice(BlockFormat(id), 1, 0) (reasons 17 and 19, the same text with no
/// arguments), or ShowSystemNotice(GetStringCommonByKey(key), 1, 0) (reasons 29 and 32). The sounds are the exe's
/// DAT_143aea1c0..0x143aea1e0 strings (skillslot.cpp:294-298). Reasons 0, 1, 12, 13 and the ones past the switch (0x21-0x23
/// among them) show nothing.
/// STILL-OPEN: <see cref="SkillCast.Check"/> does not produce 8-10 (an item skill's recovery at full HP / SP / EP,
/// skillslot.cpp:3060-3081, needs the additional effect recovery block, which is not ported), so only their notices are here.
/// </summary>
public static class CastNotice
{
    public const string NoSpSound = "system_notice_error_sp";                           // DAT_143aea1c0
    public const string NoEpSound = "system_notice_error_ep";                           // DAT_143aea1c8
    public const string NotThisPlaceSound = "system_notice_error_warningNotThisPlace";  // DAT_143aea1d0
    public const string WeaponSound = "system_notice_error_warningWeapon";              // DAT_143aea1d8
    public const string NotThisTimeSound = "system_notice_error_warningNotThisTime";    // DAT_143aea1e0

    /// <summary>A refusal's notice: the stringcommon <see cref="StringId"/>, or the stringcommon <see cref="Key"/> when
    /// the client looks the text up by key, and the system <see cref="Sound"/> (null for none).</summary>
    public readonly record struct Notice(int StringId, string Key, string Sound)
    {
        /// <summary>The reason shows something.</summary>
        public bool Shows => StringId != 0 || Key != null;
    }

    /// <summary>The notice of <paramref name="why"/>, in the switch's order; <see cref="Notice.Shows"/> false for a reason the
    /// switch does not list.</summary>
    public static Notice Of(int why) => why switch
    {
        2 => new(0x55, null, null),
        3 => new(0x53, null, null),
        4 => new(0x34, null, null),
        6 => new(0x47, null, NoSpSound),
        7 => new(0x48, null, NoEpSound),
        14 => new(0x59, null, null),
        15 or 16 => new(0x53, null, NotThisPlaceSound),
        17 => new(0x5f, null, NotThisPlaceSound),
        19 => new(0x61, null, NotThisPlaceSound),
        20 => new(0x57, null, null),
        21 => new(0x56, null, null),
        22 => new(0x53, null, null),
        23 => new(0x53, null, NotThisPlaceSound),
        24 or 25 or 26 => new(0x59, null, null),
        5 or 11 => new(0x30, null, null),
        27 => new(0x53, null, NotThisPlaceSound),
        28 => new(0x45d, null, WeaponSound),
        29 => new(0, "s_err_cannot_use_here_pet_trap", NotThisPlaceSound),
        30 or 31 => new(0x59, null, NotThisTimeSound),
        32 => new(0, "s_err_not_allow_pvpField", NotThisPlaceSound),
        18 => new(0x528, null, null),
        8 => new(0x529, null, null),
        9 => new(0x52a, null, null),
        10 => new(0x52b, null, null),
        _ => default,
    };
}
