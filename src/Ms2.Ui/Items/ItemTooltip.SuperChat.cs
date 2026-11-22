namespace Ms2.Ui.Items;

public sealed partial class ItemTooltip
{
    /// <summary>
    /// FUN_1409d4460 -> FUN_1409d8300 (astooltip.cpp:3294): an item whose function is "SuperWorldChat" previews its super
    /// chat line: "previewSuperWorldChat" = { text, colorIdx }, text the s_html_chat_super_preview format of the item's icon,
    /// colour and name, colorIdx its size. Icon, size and colour come from FUN_14183eba0 (<see cref="Hud.ChatHtml.SuperChatIcon"/>),
    /// starting from the super chat colours s_color6 and s_color18.
    /// </summary>
    void SetSuperWorldChatPreview(int itemId)
    {
        var d = Tables.Items.Get(itemId);
        if (d == null || d.FunctionName != "SuperWorldChat") return;
        uint color = Tables.ChatColor(6), color2 = Tables.ChatColor(18);
        Hud.ChatHtml.SuperChatIcon(Tables, itemId, out string icon, out uint size, ref color, ref color2);
        Data["previewSuperWorldChat"] = new Dictionary<string, object>
        {
            ["text"] = CommonF("s_html_chat_super_preview", icon, color, Tables.ItemName(itemId)),
            ["colorIdx"] = size,
        };
    }
}
