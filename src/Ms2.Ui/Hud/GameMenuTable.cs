using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui.Hud;

/// <summary>One CGameMenuDef (placeholder/cgamemenutable.h): a menu entry's name (the movie's instance and icon name),
/// caption key, tooltip keys, key table command (m_nKeyBind), the dialog it toggles (m_nUiId) and the feature the client
/// tests before it registers the entry (0 = none). <see cref="Action"/> names the UIGameMenuDialog method bound to
/// m_onClick; <see cref="EnabledBy"/> / <see cref="NewBy"/> name the functions bound to m_isEnabled / m_hasNew.</summary>
public sealed record GameMenuDef(int Id, string Name, string Label, string Tooltip, string DisabledTooltip, int KeyBind, int UiId,
    int Feature, string Action = null, string EnabledBy = null, string NewBy = null);

/// <summary>The <see cref="GameMenuDef.Action"/> names the HUD answers itself (the rest are only logged).</summary>
public static class GameMenuActions
{
    /// <summary>The pssbshop entry's handler, ToggleShopTab(9) (@0x14193d5c0, uigamemenudialog.cpp:2209).</summary>
    public const string ToggleSystemShop = "ToggleSystemShop";
    /// <summary>The learning entry's handler.</summary>
    public const string OpenLearningQuest = "OpenLearningQuestDialog";
}

/// <summary>One CGameMenuItem: a table/gamemenu.xml menu or child, with its filter.</summary>
public sealed class GameMenuItem
{
    public int Id, Row, Col;
    /// <summary>m_eFilterType (ParseGameMenuFilter @0x140cf7a60): "", eventID, eventType, attendGift, cashAttend, season.</summary>
    public string FilterType = "";
    public string FilterValue = "";
    public readonly List<GameMenuItem> Children = new();

    /// <summary>HasId @0x140cf0b50.</summary>
    public bool HasId(int id) => Id == id || Children.Any(c => c.HasId(id));
}

/// <summary>
/// CGameMenuTable (client/ms2/gameeventmanagercli.cpp): the menu definitions RegisterDefs (@0x140cf1110) builds, and the
/// menu sets LoadXml (@0x140cf6ef0) reads from table/gamemenu.xml.
/// </summary>
public sealed class GameMenuTable
{
    /// <summary>RegisterDefs in its order; an entry whose feature is off is not registered. Id 51's dialog is 0x4f67 with
    /// feature 251 on, else 0x4f87.</summary>
    static readonly GameMenuDef[] AllDefs =
    {
        new(1, "worldmap", "$s_gamemenu_btn_worldmap", "s_tooltip_gamemenu_worldmap", "", 400006, 0x4e42, 0),
        new(2, "mail", "$s_gamemenu_btn_mail", "s_tooltip_gamemenu_mail", "", 400013, 0x4e49, 0),
        new(3, "community", "$s_gamemenu_btn_community", "s_tooltip_gamemenu_community", "", 400012, 0x4e4d, 0),
        new(4, "guild", "$s_gamemenu_popup_guild", "s_tooltip_gamemenu_guild", "", 400014, 0x4e4f, 0),
        new(5, "levelup", "$s_gamemenu_popup_levelup", "s_tooltip_gamemenu_levelup", "", 400022, 0x4efa, 0),
        new(6, "arcade", "$s_gamemenu_popup_arcade", "s_tooltip_gamemenu_arcade", "", 400023, 0x4efd, 27),
        new(7, "option", "$s_gamemenu_btn_gameoption", "s_tooltip_gamemenu_gameoption", "", 0, 0x4e21, 0),
        new(8, "characterinfo", "$s_gamemenu_btn_userinfo", "s_tooltip_gamemenu_userinfo", "", 400002, 0x2711, 0),
        new(9, "inven", "$s_gamemenu_btn_inven", "s_tooltip_gamemenu_inven", "", 400001, 0x4e2d, 0),
        new(10, "skillbook", "$s_gamemenu_btn_skillbook", "s_tooltip_gamemenu_skillbook", "", 400005, 0x4f2d, 0),
        new(11, "quest", "$s_gamemenu_btn_quest", "s_tooltip_gamemenu_quest", "", 400007, 0x4e3e, 0),
        new(12, "achievement", "$s_gamemenu_btn_achievement", "s_tooltip_gamemenu_acheivement", "", 400009, 0x4e73, 0),
        new(13, "dungeonSearch", "$s_gamemenu_btn_dungeonSearch", "s_tooltip_gamemenu_dungeonSearch", "", 400018, 0, 0, Action: "FUN_14193d5e0"),
        new(14, "maver", "$s_gamemenu_popup_maview", "s_tooltip_gamemenu_maview", "", 400015, 0x4e6a, 59),
        new(15, "ugcshop", "$s_gamemenu_btn_ugc_shop", "s_tooltip_gamemenu_ugc_shop", "", 400016, 0x4ec1, 0, Action: "FUN_14193b630", NewBy: "CMeratMarketMan::HasBuyableAdProduct"),
        new(16, "cashshop", "$s_gamemenu_btn_cashshop", "s_tooltip_gamemenu_cashshop", "", 400017, 0x4ebc, 0, Action: "FUN_14193b5b0"),
        new(17, "beauty", "$s_gamemenu_popup_beauty", "s_tooltip_gamemenu_beauty", "", 400024, 0x4efe, 0),
        new(18, "blackmarket", "$s_gamemenu_popup_blackmarket", "s_tooltip_gamemenu_blackmarket", "", 400021, 0x4e97, 0),
        new(19, "individualbattle", "$s_gamemenu_popup_individualbattle", "s_tooltip_gamemenu_individualbattle", "", 400020, 0x4f04, 0, Action: "FUN_14193b670"),
        new(20, "characterability", "$s_gamemenu_popup_characterability", "s_tooltip_gamemenu_enable_characterability", "", 400025, 0x4f0c, 0),
        new(21, "design", "$s_gamemenu_btn_design", "s_tooltip_gamemenu_design", "", 400028, 0x4e69, 0, Action: "FUN_14193bbe0"),
        new(22, "home", "$s_gamemenu_btn_home", "s_tooltip_gamemenu_home", "s_returnhome_disable", 300035, 0, 0, Action: "FUN_14193b750"),
        new(23, "vip", "$s_gamemenu_btn_vip", "s_tooltip_gamemenu_vip", "", 400026, 0x4f1b, 74, Action: "FUN_14193bda0"),
        new(24, "mastery", "$s_gamemenu_btn_mastery", "s_tooltip_gamemenu_mastery", "", 400029, 0x4f1e, 0),
        new(25, "partysearch", "$s_gamemenu_btn_partysearch", "s_tooltip_gamemenu_partysearch", "", 400027, 0x4eda, 0),
        new(26, "learning", "$s_gamemenu_btn_learning", "s_tooltip_gamemenu_learning_quest", "", 400030, 0x4f18, 0, Action: GameMenuActions.OpenLearningQuest),
        new(27, "compactkey", "$s_gamemenu_btn_compactkey", "s_tooltip_gamemenu_compactkey", "", 400031, 0x4ef6, 0),
        new(28, "dictionary", "$s_gamemenu_btn_dictionary", "s_tooltip_gamemenu_dictionary", "", 400032, 0x4f0b, 0),
        new(29, "ranking", "$s_gamemenu_btn_ranking", "s_tooltip_gamemenu_ranking", "", 400033, 0x4f24, 0),
        new(30, "beauty_hair", "$s_gamemenu_btn_beauty_hair", "s_tooltip_gamemenu_beauty_hair", "", 300042, 0, 0, Action: "FUN_14193bf70"),
        new(31, "beauty_item", "$s_gamemenu_btn_beauty_item", "s_tooltip_gamemenu_beauty_item", "", 300043, 0, 0, Action: "FUN_14193bfd0"),
        new(32, "beauty_skin", "$s_gamemenu_btn_beauty_skin", "s_tooltip_gamemenu_beauty_skin", "", 300044, 0, 0, Action: "FUN_14193bfa0"),
        new(33, "club", "$s_gamemenu_btn_club", "s_tooltip_gamemenu_club", "", 400034, 0x4f25, 0),
        new(34, "epic_restart", "$s_gamemenu_btn_epic_restart", "s_tooltip_gamemenu_epic_restart", "", 0, 0, 0, Action: "FUN_14193c000"),
        new(35, "levelup_package", "$s_gamemenu_btn_levelup_package", "s_tooltip_gamemenu_levelup_package", "", 0, 0x4f2f, 0, Action: "FUN_14193c3c0", EnabledBy: "UILevelUpPackageDialog::HasOpenPackageQuest"),
        new(36, "maver", "$s_gamemenu_popup_adventurebar", "s_tooltip_gamemenu_adventurebar", "", 400015, 0x4e6a, 92),
        new(37, "stamp", "$s_gamemenu_popup_stampevent", "s_tooltip_gamemenu_stampevent", "", 0, 0x4f33, 0, Action: "OpenStampEventDialog"),
        new(38, "attend", "$s_gamemenu_popup_attendgift", "s_tooltip_gamemenu_attendgift", "", 0, 0x4eb8, 0, Action: "FUN_14193c630"),
        new(39, "pet", "$s_gamemenu_btn_pet", "s_tooltip_gamemenu_pet", "", 400035, 0x4f38, 140, Action: "FUN_14193c730"),
        new(40, "live", "$s_gamemenu_btn_live_cam", "s_tooltip_gamemenu_live_cam", "", 400015, 0x4e6a, 0, Action: "FUN_14193c780"),
        new(41, "timerun", "$s_gamemenu_btn_time_run_event", "s_tooltip_gamemenu_time_run_event", "", 0, 0x4f43, 0, Action: "FUN_14193c800", EnabledBy: "UITimeRunEventDialog::FUN_140ee3290"),
        new(42, "mentoring", "$s_gamemenu_btn_mentoring", "s_tooltip_gamemenu_mentoring", "", 0, 0x4f4b, 0, Action: "FUN_14193c930", EnabledBy: "UIMentoringDialog::IsAvailable"),
        new(43, "dailymission", "$s_gamemenu_popup_dailymission", "s_tooltip_gamemenu_dailymission", "", 400036, 0x4f53, 0, EnabledBy: "UIDailyMissionDialog::FUN_140bffff0"),
        new(44, "fame", "$s_gamemenu_popup_fame", "s_tooltip_gamemenu_fame", "", 0, 0x4f54, 0, Action: "FUN_14193cea0"),
        new(45, "gallery", "$s_gamemenu_popup_galleryevent", "s_tooltip_gamemenu_gallery", "", 0, 0x4f59, 0, Action: "FUN_14193cf60"),
        new(46, "skyFortress", "$s_gamemenu_popup_sky_fortress", "s_tooltip_gamemenu_sky_fortress", "", 0, 0, 0, Action: "FUN_14193d030"),
        new(47, "rps", "$s_gamemenu_popup_rps", "s_tooltip_gamemenu_rps", "", 0, 0x4f5f, 0, Action: "FUN_14193d250"),
        new(48, "characterSelect", "$s_gamemenu_popup_characterselect", "s_tooltip_gamemenu_characterselect", "", 0, 0, 0, Action: "FUN_14193d3d0"),
        new(49, "end", "$s_gamemenu_popup_end", "s_tooltip_gamemenu_end", "", 0, 0, 0, Action: "FUN_14193d400"),
        new(51, "adventure_level", "$s_gamemenu_popup_adventure_level", "s_tooltip_gamemenu_adventure_level", "", 0, 0x4f67, 0),
        new(52, "world_champion", "$s_gamemenu_popup_world_champion", "s_tooltip_gamemenu_world_champion", "", 0, 0, 199, Action: "SendWorldChampionMode8"),
        new(53, "cashAttend", "$s_gamemenu_popup_cashAttend", "s_tooltip_gamemenu_cashAttend", "", 0, 0x4eb9, 0, Action: "FUN_14193d560"),
        new(54, "jobchange", "$s_gamemenu_popup_changejob", "s_tooltip_gamemenu_changejob", "", 0, 0, 0, Action: "FUN_14193d590", EnabledBy: "UIChangeJobDialog::CanChangeJob"),
        new(55, "lapenshard", "$s_gamemenu_btn_lapenshard", "s_tooltip_gamemenu_lapenshard", "", 0, 0x4f6d, 211),
        new(57, "coupledance", "$s_gamemenu_popup_coupledance", "s_tooltip_gamemenu_coupledance", "", 0, 0x4f71, 0, Action: "FUN_14193d320"),
        new(59, "pssbshop", "$s_gamemenu_btn_pssbshop", "s_tooltip_gamemenu_pssbshop", "", 0, 0, 0, Action: GameMenuActions.ToggleSystemShop),
        new(60, "collectitemgroupevent", "$s_gamemenu_btn_collectitemgroupevent", "s_tooltip_gamemenu_collectitemgroupevent", "", 0, 0x4f81, 0),
        new(61, "bingoevent", "$s_gamemenu_btn_bingoevent", "s_tooltip_gamemenu_bingoevent", "", 0, 0x4f83, 0),
        new(62, "payback", "$s_gamemenu_btn_payback", "s_tooltip_gamemenu_payback", "", 0, 0x4f93, 0, Action: "FUN_14193d700", EnabledBy: "UIPaybackDialog::IsPaybackAvailable"),
        new(63, "wedding", "$s_gamemenu_btn_wedding", "s_tooltip_gamemenu_wedding", "", 0, 0x4f8c, 0),
        new(64, "longtermattendgift", "$s_gamemenu_popup_longtermattendgift", "s_tooltip_gamemenu_longtermattendgift", "", 0, 0x4f98, 0, Action: "OpenLongTermAttendGiftDialog"),
        new(1000, "marketgroup", "$s_gamemenu_btn_market_group", "", "", 0, 0, 0),
        new(1001, "charactergroup", "$s_gamemenu_btn_character_group", "", "", 0, 0, 0),
        new(1002, "contentsgroup", "$s_gamemenu_btn_contents_group", "", "", 0, 0, 0),
        new(1004, "communitygroup", "$s_gamemenu_btn_community_group", "", "", 0, 0, 0),
        new(1003, "gamegroup", "$s_gamemenu_btn_game_group", "", "", 0, 0, 0),
        new(2000, "optiongroup_gamepad", "$s_gamemenu_btn_systemgroup", "", "", 0, 0, 0),
        new(2001, "charactergroup_gamepad", "$s_gamemenu_btn_character_group", "", "", 0, 0, 0),
        new(2002, "contentsgroup_gamepad", "$s_gamemenu_btn_contents_group", "", "", 0, 0, 0),
        new(2003, "gamegroup_gamepad", "$s_gamemenu_btn_game_group", "", "", 0, 0, 0),
        new(2004, "communitygroup_gamepad", "$s_gamemenu_btn_community_group", "", "", 0, 0, 0),
        new(2005, "marketgroup_gamepad", "$s_gamemenu_btn_market_group", "", "", 0, 0, 0),
    };

    /// <summary>Parse_14021b830 (commonenum.cpp, the table at 0x143aac760): a menu id by its XML name, 0 for an unknown one.</summary>
    static readonly Dictionary<string, int> Ids = new(StringComparer.Ordinal)
    {
        ["WorldMap"] = 1, ["Mail"] = 2, ["Commuty"] = 3, ["Guild"] = 4, ["LevelUp"] = 5, ["Arcade"] = 6, ["Option"] = 7,
        ["Character"] = 8, ["Inventory"] = 9, ["Skill"] = 10, ["Quest"] = 11, ["Achieve"] = 12, ["DungeonSearch"] = 13,
        ["Maview"] = 14, ["MeratMarket"] = 15, ["CashShop"] = 16, ["Beauty"] = 17, ["BlackMarket"] = 18, ["IndividualBattle"] =
        19, ["CharacterAbility"] = 20, ["Design"] = 21, ["Home"] = 22, ["VIP"] = 23, ["Mastery"] = 24, ["PartySearch"] = 25,
        ["LearningQuest"] = 26, ["CompactKey"] = 27, ["Dictionary"] = 28, ["Ranking"] = 29, ["BeautyHair"] = 30, ["BeautyItem"]
        = 31, ["BeautySkin"] = 32, ["Club"] = 33, ["EpicRestart"] = 34, ["LevelUpPackage"] = 35, ["AdventureBar"] = 36,
        ["StampEvent"] = 37, ["AttendGift"] = 38, ["Pet"] = 39, ["LiveBroadCast"] = 40, ["TimeRunEvent"] = 41, ["Mentoring"] =
        42, ["DailyMission"] = 43, ["FamePointBoard"] = 44, ["GalleryEvent"] = 45, ["SkyFortress"] = 46, ["Rps"] = 47,
        ["CharacterSelect"] = 48, ["GameExit"] = 49, ["SurvivalMap"] = 50, ["AdventureLevel"] = 51, ["WorldChampion"] = 52,
        ["CashAttend"] = 53, ["ChangeJob"] = 54, ["Lapenshard"] = 55, ["MushkingRoyal"] = 56, ["CoupleDanceEvent"] = 57,
        ["MeratCharge"] = 58, ["PssbShop"] = 59, ["CollectItemGroupEvent"] = 60, ["BingoEvent"] = 61, ["Payback"] = 62,
        ["Wedding"] = 63, ["LongTermAttendGift"] = 64, ["MarketGroup"] = 1000, ["CharacterGroup"] = 1001, ["ContentsGroup"] =
        1002, ["GameGroup"] = 1003, ["CommunityGroup"] = 1004, ["GamePad_SystemGroup"] = 2000, ["GamePad_CharacterGroup"] =
        2001, ["GamePad_ContentsGroup"] = 2002, ["GamePad_GameGroup"] = 2003, ["GamePad_CommunityGroup"] = 2004,
        ["GamePad_MarketGroup"] = 2005,
    };

    /// <summary>EGameMenuSet (ParseGameMenuSet @0x140cf7b30): game 0, gamePad 1, mushkingRoyal 2, mushkingRoyalPad 3.</summary>
    public const int SetGame = 0;

    readonly Dictionary<int, GameMenuDef> defs = new();
    readonly List<GameMenuDef> order = new();
    readonly List<GameMenuItem>[] sets = { new(), new(), new(), new() };

    /// <summary>Init @0x140cf10d0: RegisterDefs, then LoadXml. <paramref name="featureOn"/> is CFeatureManager by id,
    /// <paramref name="available"/> IsElementAvailable (the element's locale and feature).</summary>
    public GameMenuTable(XDocument xml, Func<int, bool> featureOn, Func<XElement, bool> available)
    {
        foreach (var d in AllDefs)
        {
            if (d.Feature != 0 && !featureOn(d.Feature)) continue;
            var def = d.Id == 51 && !featureOn(251) ? d with { UiId = 0x4f87 } : d;
            defs[def.Id] = def;
            order.Add(def);
        }
        var root = xml?.Root;
        if (root == null || root.Name != "ms2") return;
        foreach (var set in root.Elements("menuSet"))
        {
            if (!available(set)) continue;
            int s = SetOf((string)set.Attribute("type"));
            foreach (var menu in set.Elements("menu"))
                if (available(menu)) sets[s].Add(Load(menu, available));
        }
    }

    /// <summary>The table read from the client's data (table/gamemenu.xml) with its features.</summary>
    public static GameMenuTable Load(PackSet packs, Func<int, bool> featureOn)
    {
        var xml = packs.Exists("Xml", "table/gamemenu.xml") ? GameStrings.Parse(packs.Read("Xml", "table/gamemenu.xml")) : null;
        return new GameMenuTable(xml, featureOn, e => LocaleInList((string)e.Attribute("locale") ?? "")
            && LobbyFeatures.IsEnabledByName(packs, (string)e.Attribute("feature")));
    }

    /// <summary>CFeatureManager::IsLocaleInList: empty, or a comma list holding the client's locale.</summary>
    static bool LocaleInList(string list) => list.Length == 0
        || list.Split(',').Any(l => string.Equals(l.Trim(), LobbyFeatures.LocaleName, StringComparison.OrdinalIgnoreCase));

    static int SetOf(string type) =>
        string.Equals(type, "gamePad", StringComparison.OrdinalIgnoreCase) ? 1
        : string.Equals(type, "mushkingRoyal", StringComparison.OrdinalIgnoreCase) ? 2
        : string.Equals(type, "mushkingRoyalPad", StringComparison.OrdinalIgnoreCase) ? 3 : SetGame;

    /// <summary>CGameMenuItem::Load @0x140cf0970: id, filterType, filterValue, row, col, and the available children.</summary>
    static GameMenuItem Load(XElement e, Func<XElement, bool> available)
    {
        var item = new GameMenuItem
        {
            Id = Ids.TryGetValue((string)e.Attribute("id") ?? "", out int id) ? id : 0,
            FilterType = (string)e.Attribute("filterType") ?? "",
            FilterValue = (string)e.Attribute("filterValue") ?? "",
            Row = Int((string)e.Attribute("row")),
            Col = Int((string)e.Attribute("col")),
        };
        foreach (var c in e.Elements("child"))
            if (available(c)) item.Children.Add(Load(c, available));
        return item;
    }

    static int Int(string s) => int.TryParse(s?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

    /// <summary>GetTopItems @0x140cf7710.</summary>
    public IReadOnlyList<GameMenuItem> TopItems(int set = SetGame) => sets[set];

    /// <summary>Find @0x140cf7690.</summary>
    public GameMenuDef Find(int id) => defs.TryGetValue(id, out var d) ? d : null;

    /// <summary>FindByName @0x140cf7470: the first definition with the name, in the map's order. Two registered entries
    /// share "maver" (14 Maview, 36 the adventure bar); both toggle dialog 0x4e6a. The map's order is taken as the
    /// registration order here.</summary>
    public GameMenuDef FindByName(string name) => order.FirstOrDefault(d => d.Name == name);

    /// <summary>FindIdByUiId @0x140cf73a0 (same order note as <see cref="FindByName"/>).</summary>
    public int FindIdByUiId(int uiId) => order.FirstOrDefault(d => d.UiId == uiId)?.Id ?? 0;

    /// <summary>FindTopId @0x140cf72f0: the top item holding the id, 0 for none.</summary>
    public int FindTopId(int id, int set = SetGame) => sets[set].FirstOrDefault(i => i.HasId(id))?.Id ?? 0;

    /// <summary>IsChild @0x140cf71f0.</summary>
    public bool IsChild(int id, int set = SetGame) => FindTopId(id, set) != id;

    /// <summary>FindTopItem @0x140cf77a0.</summary>
    public GameMenuItem FindTopItem(int id, int set = SetGame) => sets[set].FirstOrDefault(i => i.Id == id);
}
