namespace Ms2.Ui;

/// <summary>
/// The Job packet's mode 3 (CSceneStateGame::OnJob, scenestategame.cpp:7537-7565): the error byte picks a common string, shown
/// with ShowSystemNotice(text, 1, 1); error 18 reads an i32, formats s_err_job_inventory_full with it, and shows it in a
/// box (ShowMessageBox(text, 0, 0, L"")) before the notice. An error the switch does not name formats s_err_job_unknown
/// with the error; error 0, and an empty text, show nothing. The packet's PC is found first (FindPCById), not tested for
/// the local player.
/// </summary>
public static class JobErrorNotice
{
    /// <summary>The common string id of each named error (GetStringCommon 0x76..0x7f).</summary>
    public static int? CommonId(int error) => error switch
    {
        9 => 0x76,          // s_err_job_not_enough_meso
        10 => 0x77,         // s_err_job_not_enough_level
        2 => 0x78,          // s_err_job_bad_job
        12 => 0x79,         // s_err_job_no_penalty
        11 => 0x7a,         // s_err_job_no_home
        3 or 4 => 0x7b,     // s_err_job_not_complete_quest
        5 => 0x7c,          // s_err_job_privilege
        15 => 0x7d,         // s_err_job_dayofweek
        8 => 0x7e,          // s_err_job_debuff
        14 => 0x7f,         // s_err_job_guild
        _ => null,
    };

    /// <summary>Error 18's string (BlockFormat(0x80, value)).</summary>
    public const int InventoryFull = 18, InventoryFullId = 0x80;
    /// <summary>The other errors' string (FormatStringId(0x81, error)).</summary>
    public const int UnknownId = 0x81;

    /// <summary>What mode 3 shows: the notice text and, for error 18, the box text (null when no box). Both empty for
    /// error 0 or a text the string table does not have.</summary>
    public static (string Notice, string Box) Text(int error, int value, Func<int, string> common)
    {
        if (error == 0) return ("", null);
        if (error == InventoryFull)
        {
            string t = Ms2.Quest.ClientFormat.Format(common(InventoryFullId) ?? "", value);
            return (t, t);
        }
        if (CommonId(error) is int id) return (common(id) ?? "", null);
        return (Ms2.Quest.ClientFormat.Format(common(UnknownId) ?? "", error), null);
    }
}
