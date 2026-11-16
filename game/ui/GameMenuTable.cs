using System.Xml.Linq;

namespace Ms2Godot.Ui;

/// <summary>
/// The game menu's top-level buttons from table/gamemenu.xml (menuSet "game"), as the client's CGameMenuTable reads them:
/// a menu id with its column and row, optional locale and feature gates, and child entries for the group buttons.
/// The client's menu definitions (CGameMenuTable::RegisterDefs) give each id its icon/instance name and caption key.
/// </summary>
public static class GameMenuTable
{
    public sealed record Item(string Id, string Name, string Caption, int Col, int Row, bool Group);

    /// <summary>Menu table id -> (name used for the instance and the export_common_menu_ icon, caption string key).</summary>
    static readonly Dictionary<string, (string Name, string Caption)> Defs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CharacterGroup"] = ("charactergroup", "$s_gamemenu_btn_character_group"),
        ["ContentsGroup"] = ("contentsgroup", "$s_gamemenu_btn_contents_group"),
        ["GameGroup"] = ("gamegroup", "$s_gamemenu_btn_game_group"),
        ["CommunityGroup"] = ("communitygroup", "$s_gamemenu_btn_community_group"),
        ["Home"] = ("home", "$s_gamemenu_btn_home"),
        ["Option"] = ("option", "$s_gamemenu_btn_gameoption"),
        ["VIP"] = ("vip", "$s_gamemenu_btn_vip"),
        ["Design"] = ("design", "$s_gamemenu_btn_design"),
        ["SkyFortress"] = ("skyFortress", "$s_gamemenu_popup_sky_fortress"),
        ["MeratMarket"] = ("ugcshop", "$s_gamemenu_btn_ugc_shop"),
        ["CashShop"] = ("cashshop", "$s_gamemenu_btn_cashshop"),
        ["Ranking"] = ("ranking", "$s_gamemenu_btn_ranking"),
        ["ChangeJob"] = ("jobchange", "$s_gamemenu_btn_jobchange"),
    };

    /// <summary>Top-level items for a locale (e.g. "KR"); <paramref name="featureOn"/> decides the feature gates.</summary>
    public static List<Item> TopItems(XDocument table, string locale, Func<string, bool> featureOn)
    {
        var list = new List<Item>();
        var set = table?.Root?.Elements("menuSet").FirstOrDefault(s => (string)s.Attribute("type") == "game");
        if (set == null) return list;
        foreach (var m in set.Elements("menu"))
        {
            var loc = (string)m.Attribute("locale");
            if (loc != null && !string.Equals(loc, locale, StringComparison.OrdinalIgnoreCase)) continue;
            var feature = (string)m.Attribute("feature");
            if (feature != null && !featureOn(feature)) continue;
            var id = (string)m.Attribute("id");
            if (id == null || !Defs.TryGetValue(id, out var def)) continue;
            list.Add(new Item(id, def.Name, def.Caption, (int?)m.Attribute("col") ?? 0, (int?)m.Attribute("row") ?? 0, m.Elements("child").Any()));
        }
        return list;
    }
}
