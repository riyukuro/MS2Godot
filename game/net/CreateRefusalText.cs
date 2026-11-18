namespace Ms2Godot.Net;

/// <summary>
/// The text UICharacterCreateDialog::OnCreateCharacterAnswer shows for a refusal (uicameraeditdialog.cpp:1608-1685), chosen by the
/// answer's kind. The client's numeric string ids are CommonString's table (commonstringtable.cpp:1617-3846) of stringcommon keys:
/// kind 1 -> id 0xb s_char_err_name, 0xb -> 0x10 s_char_err_already_taken, 8 -> 0xf s_char_err_ban_all (name), 9 -> 0xe
/// s_char_err_ban_any (name), 6 -> 9 s_char_err_char_count (the character limit; 10 s_char_err_char_count_by_gameevent while an event
/// value is set, not modelled), 10 -> 0x11 s_char_err_invalid_def_item, 0xc -> 0x12 s_char_err_job_forbidden, 0xe -> 0x13
/// s_char_err_creation_restriction, 0xf -> 0x8ba s_ban_check_err_any, any other kind -> 0xc s_char_err_system (the client formats
/// the kind number into it; the text has no placeholder).
/// </summary>
public static class CreateRefusalText
{
    /// <param name="maxCharacters">DAT_1439f4a84, the account's character limit (kind 6).</param>
    public static string For(int kind, string name, Func<string, string> common, int maxCharacters = 6)
    {
        string Fmt(string key, string arg) => common(key)?.Replace("{0}", arg);
        string text = kind switch
        {
            1 => common("s_char_err_name"),
            0xb => common("s_char_err_already_taken"),
            8 => Fmt("s_char_err_ban_all", name),
            9 => Fmt("s_char_err_ban_any", name),
            6 => Fmt("s_char_err_char_count", maxCharacters.ToString()),
            10 => common("s_char_err_invalid_def_item"),
            0xc => common("s_char_err_job_forbidden"),
            0xe => common("s_char_err_creation_restriction"),
            0xf => common("s_ban_check_err_any"),
            _ => Fmt("s_char_err_system", kind.ToString()),
        };
        return string.IsNullOrEmpty(text) ? $"Could not create the character (error {kind})." : text;
    }
}
