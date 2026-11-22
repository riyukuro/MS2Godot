using Ms2.Quest;

namespace Ms2.Ui;

/// <summary>The action key's caption text (the UIActionTooltip text, Unk_ActionKeyManager::Update's vfunc_6).</summary>
public static class ActionCaption
{
    /// <summary>GetActionCaption (@0x140960c30, actionkeyobject.cpp:444-454): the format with the argument; with an empty
    /// argument the text is cut at its first '-'.</summary>
    public static string Of(string format, string arg)
    {
        arg ??= "";
        string text = ClientFormat.Format(format, arg);
        if (arg.Length == 0)
        {
            int pos = text.IndexOf('-');
            if (pos != -1) return text.Substring(0, pos);
        }
        return text;
    }
}
