using Ms2.Quest;
using Ms2.Ui.Hud;

namespace Ms2.Ui;

/// <summary>
/// The forced rename on character select, for a character whose name the client's checker no longer takes (too long):
/// <list type="bullet">
/// <item>The box (<see cref="Create"/>): a type 10 input box with s_char_rename_input and the tag changeInvalidCharName, the
/// character id as its argument, SetInput(2, "", 12, 0, 0). Opened by the game start's check (FUN_1418f13f0,
/// uicharacterlistdialog.cpp:1470-1490, while the list's end flag m_1e8 is set) and by LoginToGame result 0x31
/// (CNetworkService::OnLoginToGame, networkservice.cpp:913-916).</item>
/// <item>Its buttons (<see cref="Answer"/>): UIMessageBox::HandleChangeInvalidCharNameButton @0x1419fecc0
/// (uimessagebox.cpp:2119-2156), from FUN_1419fa910's "changeInvalidCharName,&lt;typed name&gt;".</item>
/// <item>The login server's answer, CharacterList 7 (<see cref="AnswerText"/>): UICharacterListDialog::vfunc_79 case 0xc
/// (uicharacterlistdialog.cpp:605-633).</item>
/// </list>
/// </summary>
public static class InvalidNameRename
{
    public const string Tag = "changeInvalidCharName";

    /// <summary>CBanTextChecker::FUN_140225210's result for a name over its length (CheckTextWithLimit's too long): the
    /// result the start's check opens the box for.</summary>
    public const int TooLong = 2;

    /// <summary>The rename box for character <paramref name="characterId"/>, not shown yet.</summary>
    public static MessageBoxData Create(long characterId, Func<string, string> commonByKey)
    {
        var d = MessageBoxQueue.Create(10, commonByKey("s_char_rename_input") ?? "", null, Tag);
        d.Append(characterId);
        d.SetInput(2, "", 12, 0, 0);
        return d;
    }

    /// <summary>What a button of the box does.</summary>
    public enum Action
    {
        /// <summary>The box stays with <c>Error</c> under its input (UpdateErrorMessage).</summary>
        ShowError,
        /// <summary>SendCharacterManagementMode5(the box's argument 0, the name): the rename request; the box closes.</summary>
        Send,
        /// <summary>FUN_1418f20d0: the list's input on (FUN_1412761e0(m_30, 1)); the box closes.</summary>
        Cancel,
    }

    /// <summary>
    /// HandleChangeInvalidCharNameButton: button 0 with no name shows common string 0xd; a name FUN_141276a30 refuses shows
    /// its message (<paramref name="nameError"/>: the checker's message, null when the name passes); else the request goes.
    /// Any other button cancels.
    /// </summary>
    public static (Action Do, string Error) Answer(int button, string name, Func<string, string> nameError, Func<int, string> common)
    {
        if (button != 0) return (Action.Cancel, null);
        if (string.IsNullOrEmpty(name)) return (Action.ShowError, common(0xd) ?? "");
        if (nameError?.Invoke(name) is { } error) return (Action.ShowError, error);
        return (Action.Send, null);
    }

    /// <summary>
    /// CharacterList 7's box text: null for code 1 (the row takes the new name, updateCharacterName); 2 common string 0x2a
    /// with the name (FormatStringId), 6 0x29 with the name, any other 0x2c with the name and the code (FUN_14028dee0).
    /// </summary>
    public static string AnswerText(int code, string name, Func<int, string> common) => code switch
    {
        1 => null,
        2 => ClientFormat.Format(common(0x2a) ?? "", name ?? ""),
        6 => ClientFormat.Format(common(0x29) ?? "", name ?? ""),
        _ => ClientFormat.Format(common(0x2c) ?? "", name ?? "", code),
    };
}
