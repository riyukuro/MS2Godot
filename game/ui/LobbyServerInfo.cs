using Godot;

namespace Ms2Godot.Ui;

/// <summary>
/// The lobby's server line (character select and create), as the client fills it. FUN_141900750 (uicommon.cpp:1493-1529)
/// builds the initData object both dialogs get (UICharacterListDialog::OnLoadComplete, uicharacterlistdialog.cpp:695-700;
/// UICharacterCreateDialog, uicameraeditdialog.cpp:1284-1286). Outside the global build (DAT_143ab0248 != 2):
/// <list type="bullet">
/// <item>name = the server list row's name;</item>
/// <item>then " &lt;font size='15'&gt;| CH.{0}&lt;/font&gt;", with channel = GetCurrentChannel() if over 1, else 1;</item>
/// <item>iconPath = the row's icon.</item>
/// </list>
/// The row is CServerListTable::GetServer(UIServerListDialog::GetServerId()). CServerListTable::Load
/// (serverlisttable.cpp:54-79) reads table/serverList.xml: name through GetStringCommonByKey (stringcommon.xml, "" for a
/// missing key: CommonString::MapText), icon as written. regionName / regionIconPath come only from the region list dialog,
/// which this build does not open, so the region field stays empty and hidden.
/// </summary>
public sealed record LobbyServerInfo(string Name, string IconPath)
{
    /// <summary>FUN_141900750 for the server <paramref name="serverId"/> and channel <paramref name="channel"/>.</summary>
    public static LobbyServerInfo Build(UiAssets assets, int serverId, int channel)
    {
        string name = "", icon = "";
        foreach (var row in assets.Table("table/serverlist.xml")?.Root?.Elements("server") ?? [])
        {
            if ((int?)row.Attribute("id") != serverId) continue;
            name = CommonString(assets, (string)row.Attribute("name") ?? "");
            icon = (string)row.Attribute("icon") ?? "";
            break;
        }
        return new LobbyServerInfo(name + $" <font size='15'>| CH.{(channel > 1 ? channel : 1)}</font>", icon);
    }

    /// <summary>GetStringCommonByKey: the key's text in the client language's stringcommon.xml, "" when absent.</summary>
    static string CommonString(UiAssets assets, string key)
    {
        if (key.Length == 0) return "";
        var doc = assets.Table($"string/{assets.Language}/stringcommon.xml");
        foreach (var e in doc?.Root?.Elements("key") ?? [])
            if ((string)e.Attribute("id") == key) return (string)e.Attribute("value") ?? "";
        return "";
    }

    /// <summary>
    /// The dialog's initData with this object (CharacterListDialog / CharacterCreateDialog .initData):
    /// CommonLobby.initServerInfo on m_serverName with a new m_serverIcon clip on m_content, and initRegionInfo with no region.
    /// CommonLobby.setServerInfo:
    /// <list type="bullet">
    /// <item>the field is hidden;</item>
    /// <item>a non-empty name turns on TEXTAUTOSZ_SHRINK, goes in as htmlText and shows the field;</item>
    /// <item>the icon is hidden until Common.loadImage has it.</item>
    /// </list>
    /// onLoadCompleteServerIcon puts the image, at its own size and smoothed (Common.loadComplete), at
    /// x = (int)(field.x - icon width), y = field.y - 5, and shows it.
    /// </summary>
    public void Apply(MovieClipNode content, UiAssets assets)
    {
        if (content == null) return;
        if (content.Direct("m_serverName") is { } field)
        {
            field.Hidden = Name.Length == 0;
            if (Name.Length > 0 && field.Node is TextNode t)
            {
                t.SetHtml(Name);
                t.ShrinkToFit();
            }
            content.GetNodeOrNull("m_serverIcon")?.Free();
            const string imageRoot = "./Data/Resource/Image/";
            var tex = IconPath.StartsWith(imageRoot, StringComparison.OrdinalIgnoreCase) ? assets.ImageFile(IconPath[imageRoot.Length..]) : null;
            if (tex != null)
            {
                var at = field.Base.Origin;
                content.AddChild(new Sprite2D
                {
                    Name = "m_serverIcon", Texture = tex, Centered = false, TextureFilter = CanvasItem.TextureFilterEnum.Linear,
                    Position = new Vector2((int)(at.X - tex.GetWidth()), at.Y - 5),
                });
            }
        }
        // initRegionInfo: regionName undefined, so setServerInfo leaves the field hidden
        if (content.Direct("m_regionName") is { } region) region.Hidden = true;
        content.GotoFrame(content.Frame);
    }
}
