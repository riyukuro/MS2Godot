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
    /// <summary>InteractAction::vfunc_6 (@0x140958a80, actionkeyobject.cpp:1605-1616): "" when the object's action name (string
    /// table 0xb by its row) is empty, else GetActionCaption(0x634, name). InteractNpcAction::vfunc_6 (@0x140959240, :1684-1689)
    /// is GetActionCaption(0x634, the npc's name).</summary>
    public static string Interact(string format0x634, string name) =>
        string.IsNullOrEmpty(name) ? "" : Of(format0x634, name);

    /// <summary>BillBoardInteractAction::vfunc_6 (@0x14095d9a0, actionkeyobject.cpp:2568-2586): "" when the board's action name
    /// (string table 0xb by its row) is empty; else "{name} - {text}", the board's text cut to its first 10 characters and
    /// "..." when longer.</summary>
    public static string BillBoard(string name, string boardText)
    {
        if (string.IsNullOrEmpty(name)) return "";
        string text = boardText ?? "";
        if (text.Length > 10) text = ClientFormat.Format("{0}...", text.Substring(0, 10));
        return ClientFormat.Format("{0} - {1}", name, text);
    }
}
