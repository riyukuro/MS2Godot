namespace Ms2Godot.Net;

/// <summary>
/// The text UICharacterCreateDialog::OnCreateCharacterAnswer shows for a refusal (uicameraeditdialog.cpp:1608-1685), chosen by the
/// answer's kind from stringcommon: 1 -> 0xb, 0xb -> 0x10, 8 -> 0xf and 9 -> 0xe with the name, 6 -> 9 (or 10 with the event's value),
/// 10 -> 0x11, 0xc -> 0x12, 0xe -> 0x13, 0xf -> 0x8ba, any other kind -> 0xc with the kind number. The id of the "other" and kind 6
/// texts is read from stringcommon as well; the table the client's BlockFormat reads is not confirmed, so a missing id falls back to a plain line.
/// </summary>
public static class CreateRefusalText
{
    public static string For(int kind, string name, Func<string, string> common)
    {
        string Get(int id) => common(id.ToString());
        string Fmt(int id, string arg) => Get(id)?.Replace("{0}", arg).Replace("%s", arg).Replace("%d", arg);
        string text = kind switch
        {
            1 => Get(0xb),
            0xb => Get(0x10),
            8 => Fmt(0xf, name),
            9 => Fmt(0xe, name),
            6 => Fmt(9, "0"),
            10 => Get(0x11),
            0xc => Get(0x12),
            0xe => Get(0x13),
            0xf => Get(0x8ba),
            _ => Fmt(0xc, kind.ToString()),
        };
        return string.IsNullOrEmpty(text) ? $"Could not create the character (error {kind})." : text;
    }
}
