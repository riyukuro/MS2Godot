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
    /// <summary>
    /// PortalAction::vfunc_6 (@0x140955aa0, actionkeyobject.cpp:971-1021): "" without the portal's entity; a portal without a
    /// target map has its action text (CPortal::GetActionText, <see cref="MouseInteract.PortalActionText"/>). Else the target's
    /// mapNamePortalTooltip (string table 0xf), its mapName (3) when that is empty; a target missing from the field table gives
    /// common 0x632; one that cannot be entered (enterAdminLevel, FUN_140a55d20) gets " &lt;common 0x99d&gt;" after the name;
    /// the name goes into common 0x633. STILL-OPEN: a home portal whose owner's map CubeMapInformationService knows
    /// (m_unk_158) gives common 0x632; the owners are not ported, so the name branch runs.
    /// </summary>
    public static string Portal(GameTables t, bool hasEntity, int type, int targetMap, Func<int, Ms2.Formats.World.FieldData> field)
    {
        if (!hasEntity) return "";
        if (targetMap == 0) return MouseInteract.PortalActionText(t, type) ?? "";
        string name = t.StringTableText(0xf, targetMap);
        if (name.Length == 0) name = t.StringTableText(3, targetMap);
        if (field(targetMap) is not { } f) return t.Common(0x632) ?? "";
        if (f.EnterAdminLevel > 0) name += " <" + (t.Common(0x99d) ?? "") + ">";
        return ClientFormat.Format(t.Common(0x633) ?? "", name);
    }

    /// <summary>LiftCubeAction::vfunc_6 (@0x140957740, actionkeyobject.cpp:1348-1354): "" without the lift
    /// (FindLiftByName(m_str18)); else GetActionCaption(0x63a, the name of the lift's item, string table 1).</summary>
    public static string Lift(GameTables t, bool found, int itemId) =>
        found ? Of(t.Common(0x63a) ?? "", t.StringTableText(1, itemId)) : "";
}
