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
    /// <summary>
    /// TalkAction::vfunc_6 (@0x140957f30, actionkeyobject.cpp:1430-1441): "" without a kept npc (m_n18); else the npc's
    /// actionName (string table 0xe by npc id), common 0x62d when it has none, then common 0x649 formatted with the npc's
    /// name (m_strName) appended when that is not empty.
    /// </summary>
    public static string Talk(GameTables t, int npcId, string npcName)
    {
        if (npcId == 0) return "";
        string text = t.StringTableText(0xe, npcId);
        if (text.Length == 0) text = t.Common(0x62d) ?? "";
        string name = ClientFormat.Format(t.Common(0x649) ?? "", npcName ?? "");
        return name.Length != 0 ? text + name : text;
    }
}
