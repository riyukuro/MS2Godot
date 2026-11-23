using System.Collections.Generic;
using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Flow;

/// <summary>
/// The front-end's message boxes, shown in the client's one message box layer (<see cref="Ui.MessageBoxLayer"/>): the
/// login refusal notice, the character delete confirmation and the character name box.
/// </summary>
public static class FrontEndBoxes
{
    /// <summary>GetStringCommon by id for the ids used here (commonstringtable.cpp).</summary>
    static readonly Dictionary<int, string> Keys = new()
    {
        [1] = "s_char_input_name", [4] = "s_char_ask_delete", [5] = "s_char_ask_delete_with_wait",
        [7] = "s_char_ask_delete_confirm_checkbox", [0x18] = "s_char_err_delete_name",
        [0x97b] = "s_common_sec", [0x97c] = "s_common_min", [0x97d] = "s_common_hour",
        [0x97e] = "s_common_min_sec", [0x97f] = "s_common_hour_min", [0x980] = "s_common_hour_min_sec",
    };

    public static string Common(int id) => Keys.TryGetValue(id, out var k) ? MessageBoxData.CommonByKey(k) ?? "" : "";
    static string Format(int id, params object[] args) => ClientFormat.Format(Common(id), args);

    /// <summary>
    /// CNetworkService::OnLoginResult (networkservice.cpp:991-1066): the refused result's box (<see cref="LoginRefusalBox.Build"/>),
    /// shown now (FUN_140d73c60). Result 7's start and end are the two i64 after the block type and an i32 and an i64
    /// (LoginResultPacket.Restricted); result 0x1a's buy button opens the founders page (OnFoundersMsgBoxResult). STILL-OPEN:
    /// result 7's CMsTime::SetTimeZone and 0x1a's scene change (FUN_1417e25a0(0x6200085)) are not ported.
    /// </summary>
    public static void LoginRefused(LoginResult r)
    {
        long start = 0, end = 0;
        if (r.State == 7 && r.Tail is { Length: >= 28 } t) { start = System.BitConverter.ToInt64(t, 12); end = System.BitConverter.ToInt64(t, 20); }
        MessageBoxQueue.Show(LoginRefusalBox.Build(r.State, r.Const1, r.Message, start, end, r.TimeBase,
            (button, _) => { if (button == 0) Godot.OS.ShellOpen(LoginRefusalBox.FoundersUrl); return true; }));
    }

    /// <summary>FUN_141b708a0 @0x141b708a0 (util.cpp:999): a time span as text.</summary>
    public static string Duration(uint seconds, bool showSeconds)
    {
        if (seconds < 60) return Format(0x97b, seconds);
        if (seconds < 3600)
        {
            uint m = seconds / 60, s = seconds % 60;
            return s == 0 ? Format(0x97c, m) : Format(0x97e, m, s);
        }
        uint h = seconds / 3600, mm = seconds / 60 % 60, rest = seconds % 60;
        if (showSeconds && rest != 0) return Format(0x980, h, mm, rest);
        return mm == 0 ? Format(0x97d, h) : Format(0x97f, h, mm);
    }

    /// <summary>
    /// UICharacterListDialog::vfunc_28 "OnDeletePC" (uicharacterlistdialog.cpp:2222-2244): a character at or above
    /// CharacterDestroyDivisionLevel gets s_char_ask_delete_with_wait (the limit, CharacterDestroyWaitSecond as text, the
    /// name), a lower one s_char_ask_delete (the name); then box 0xe tagged "deletePC" with a name input and the checkbox.
    /// </summary>
    public static MessageBoxData DeleteConfirm(CharacterEntry pc, int divisionLevel, uint waitSeconds, IMessageBoxListener listener)
    {
        string name = pc.User.Name ?? "";
        string msg = pc.User.Level >= divisionLevel
            ? Format(5, divisionLevel, Duration(waitSeconds, false), name)
            : Format(4, name);
        if (string.IsNullOrEmpty(msg)) return null;
        var box = MessageBoxQueue.Create(0xe, msg, listener, "deletePC");
        box.SetInput(2, "", 0, 0, 0);
        box.SetCheckbox0(Common(7), false);
        MessageBoxQueue.Show(box);   // FUN_140d73c60
        return box;
    }

    /// <summary>
    /// UICharacterListDialog::OnMessage @0x1418eb540 for the delete box: any answer closes it (FUN_14151f200); OK with
    /// "deletePC,&lt;typed&gt;" whose name matches the character (case-insensitive) sends the delete (FUN_1418f13d0), any
    /// other OK shows s_char_err_delete_name (FUN_140d73bc0(0x18, 0, 0, "")).
    /// </summary>
    public sealed class DeleteListener : IMessageBoxListener
    {
        readonly string name;
        readonly System.Action delete;
        public DeleteListener(string name, System.Action delete) { this.name = name ?? ""; this.delete = delete; }

        public bool OnMessageBox(MessageBoxController sender, int button, string tag)
        {
            MessageBoxQueue.Next();
            if (button != 0) return true;
            var parts = (tag ?? "").Split(',', 2);   // FUN_14028cb70(tag, parts, ",", 2)
            if (parts.Length != 2 || parts[0] != "deletePC") return true;
            if (parts[1].Length > 0 && string.Equals(parts[1], name, System.StringComparison.OrdinalIgnoreCase))
            {
                delete?.Invoke();
                return true;
            }
            MessageBoxQueue.ShowMessageBox(Common(0x18), 0, null, "");
            return true;
        }
    }

    /// <summary>
    /// UICharacterCreateDialog::OnCreate @0x1418e6210 (uicameraeditdialog.cpp:2127): FUN_140d73e40(10, 1, 0, "") (common
    /// string 1), SetInput(2, "", 12, 0, 0), the callback FUN_1418e7790: OK passes the box's answer string (m_str218, the
    /// movie's "," + typed name) trimmed of ',' to <paramref name="submit"/> and keeps the box; any other button closes it.
    /// The box closes on the server's answer (FUN_14151f200 in vfunc_79 / OnCreateCharacterAnswer).
    /// </summary>
    public static MessageBoxData NameBox(System.Action<string> submit)
    {
        var box = MessageBoxQueue.CreateWithCallback(Common(1), 10, (n, d) =>
        {
            if (n != 0) return true;
            submit?.Invoke((d.ClickedTag ?? "").Trim(','));
            return false;
        });
        box.SetInput(2, "", 12, 0, 0);
        MessageBoxQueue.Show(box);
        return box;
    }
}
