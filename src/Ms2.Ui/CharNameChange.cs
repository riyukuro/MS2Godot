using Ms2.Quest;
using Ms2.Ui.Hud;

namespace Ms2.Ui;

/// <summary>
/// The character name change item (function ChangeCharName):
/// <list type="bullet">
/// <item>The use (<see cref="InputBox"/>): ChangeCharName @0x140d236a0 (unit_140d1ae30_itemfunctiondispatch.cpp:1483), a type 10
/// box of common string 1 tagged changeCharName with the item's uid and SetInput(2, "", 12, 0, 0).</item>
/// <item>Its yes (<see cref="Answer"/>): UIMessageBox::HandleChangeCharNameButton @0x1419feae0 (uimessagebox.cpp:2086), the
/// name check, then SendOp CheckCharName with the name and the uid.</item>
/// <item>The server's answer (<see cref="ResultBox"/>): CSceneStateGame::OnCheckCharNameResult @0x141737db0
/// (scenestategame.cpp:4698).</item>
/// <item>The confirm's yes: UIMessageBox::HandleConfirmChangeCharNameButton @0x1419fce20 (uimessagebox.cpp:1624), the item of
/// argument 0 used with the name of argument 1 (FUN_140a63880).</item>
/// </list>
/// </summary>
public static class CharNameChange
{
    /// <summary>s_strChangeCharName (unit_140d1ae30_itemfunctiondispatch.cpp:296): the input box's tag.</summary>
    public const string Tag = "changeCharName";
    /// <summary>s_confirmChangeCharName (uimessagebox.cpp:52): the confirm box's tag.</summary>
    public const string ConfirmTag = "confirmChangeCharName";
    /// <summary>The input's character limit (SetInput's third argument).</summary>
    public const int MaxLength = 12;

    /// <summary>The input box for the item of <paramref name="itemUid"/>, not shown yet.</summary>
    public static MessageBoxData InputBox(long itemUid, Func<int, string> common)
    {
        var d = MessageBoxQueue.Create(10, common(1) ?? "", null, Tag);
        d.ItemUid = itemUid;
        d.SetInput(2, "", MaxLength, 0, 0);
        return d;
    }

    /// <summary>What the input box's button does.</summary>
    public enum Action
    {
        /// <summary>The box stays with <c>Error</c> under its input (UpdateErrorMessage).</summary>
        ShowError,
        /// <summary>CheckCharName with the name and the box's item uid; the box closes.</summary>
        Send,
        /// <summary>The box closes.</summary>
        Close,
    }

    /// <summary>
    /// HandleChangeCharNameButton: any button but 0 closes; button 0 with no name shows common string 0xd; a name FUN_141276a30
    /// refuses shows its message (<paramref name="nameError"/>: null when the name passes); else the check is sent.
    /// </summary>
    public static (Action Do, string Error) Answer(int button, string name, Func<string, string> nameError, Func<int, string> common)
    {
        if (button != 0) return (Action.Close, null);
        if (string.IsNullOrEmpty(name)) return (Action.ShowError, common(0xd) ?? "");
        if (nameError?.Invoke(name) is { } error) return (Action.ShowError, error);
        return (Action.Send, null);
    }

    /// <summary>
    /// OnCheckCharNameResult: a used name is a plain box (type 0, no tag) of common string 0x2a with the name; else the local
    /// player's name and the new one in common string 0x28, a type 1 box tagged <see cref="ConfirmTag"/> with the item uid and
    /// the name as its arguments (shown after FUN_14151f200, the shown box closed). Null without the local player (nothing).
    /// </summary>
    public static MessageBoxData ResultBox(bool used, long itemUid, string name, string myName, Func<int, string> common)
    {
        if (used) return MessageBoxQueue.Create(0, ClientFormat.Format(common(0x2a) ?? "", name ?? ""), null, "");
        if (myName == null) return null;
        var d = MessageBoxQueue.Create(1, ClientFormat.Format(common(0x28) ?? "", myName, name ?? ""), null, ConfirmTag);
        d.Append(itemUid);
        d.AddStringArg(name ?? "");
        return d;
    }
}
