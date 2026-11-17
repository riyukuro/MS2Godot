using System.Xml.Linq;
using Godot;

namespace Ms2Godot.Ui.Native;

/// <summary>
/// The game menu (UIGameMenuDialog) without Flash: the scene holds the dialog's art and its fixed buttons; this script is
/// RefreshMenu / addMenuEntity / addEntity ported. The entries come from the player's table/gamemenu.xml (menuSet "game")
/// gated by locale and features; entries and children with a filterType (events, seasons, attendance) need the server's
/// event state and are left out. Each entry is a UIGameMenuDialog_menu / _group / _big_menu at (col * 56, 120 - row * 60)
/// in m_menus with its caption tag (bottom aligned) and a <see cref="NMenuButton"/>. A click calls the host with the
/// menu's name, as ExternalInterface.call(id, name) does.
/// </summary>
public partial class NGameMenu : Control
{
    const string Movie = "uigamemenudialog";
    public string Locale = "KR";
    public Func<string, bool> FeatureOn = _ => true;
    public readonly List<string> Calls = new();

    /// <summary>
    /// CGameMenuTable's menu definitions (the client's RegisterDefs joined with its static name table):
    /// table id -> (instance and icon name, caption string key).
    /// </summary>
    static readonly Dictionary<string, (string Name, string Caption)> Defs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WorldMap"] = ("worldmap", "$s_gamemenu_btn_worldmap"), ["Mail"] = ("mail", "$s_gamemenu_btn_mail"),
        ["Commuty"] = ("community", "$s_gamemenu_btn_community"), ["Guild"] = ("guild", "$s_gamemenu_popup_guild"),
        ["LevelUp"] = ("levelup", "$s_gamemenu_popup_levelup"), ["Arcade"] = ("arcade", "$s_gamemenu_popup_arcade"),
        ["Option"] = ("option", "$s_gamemenu_btn_gameoption"), ["Character"] = ("characterinfo", "$s_gamemenu_btn_userinfo"),
        ["Inventory"] = ("inven", "$s_gamemenu_btn_inven"), ["Skill"] = ("skillbook", "$s_gamemenu_btn_skillbook"),
        ["Quest"] = ("quest", "$s_gamemenu_btn_quest"), ["Achieve"] = ("achievement", "$s_gamemenu_btn_achievement"),
        ["DungeonSearch"] = ("dungeonSearch", "$s_gamemenu_btn_dungeonSearch"), ["Maview"] = ("maver", "$s_gamemenu_popup_maview"),
        ["MeratMarket"] = ("ugcshop", "$s_gamemenu_btn_ugc_shop"), ["CashShop"] = ("cashshop", "$s_gamemenu_btn_cashshop"),
        ["Beauty"] = ("beauty", "$s_gamemenu_popup_beauty"), ["BlackMarket"] = ("blackmarket", "$s_gamemenu_popup_blackmarket"),
        ["IndividualBattle"] = ("individualbattle", "$s_gamemenu_popup_individualbattle"),
        ["CharacterAbility"] = ("characterability", "$s_gamemenu_popup_characterability"),
        ["Design"] = ("design", "$s_gamemenu_btn_design"), ["Home"] = ("home", "$s_gamemenu_btn_home"), ["VIP"] = ("vip", "$s_gamemenu_btn_vip"),
        ["Mastery"] = ("mastery", "$s_gamemenu_btn_mastery"), ["PartySearch"] = ("partysearch", "$s_gamemenu_btn_partysearch"),
        ["LearningQuest"] = ("learning", "$s_gamemenu_btn_learning"), ["CompactKey"] = ("compactkey", "$s_gamemenu_btn_compactkey"),
        ["Dictionary"] = ("dictionary", "$s_gamemenu_btn_dictionary"), ["Ranking"] = ("ranking", "$s_gamemenu_btn_ranking"),
        ["BeautyHair"] = ("beauty_hair", "$s_gamemenu_btn_beauty_hair"), ["BeautyItem"] = ("beauty_item", "$s_gamemenu_btn_beauty_item"),
        ["BeautySkin"] = ("beauty_skin", "$s_gamemenu_btn_beauty_skin"), ["Club"] = ("club", "$s_gamemenu_btn_club"),
        ["EpicRestart"] = ("epic_restart", "$s_gamemenu_btn_epic_restart"), ["LevelUpPackage"] = ("levelup_package", "$s_gamemenu_btn_levelup_package"),
        ["AdventureBar"] = ("maver", "$s_gamemenu_popup_adventurebar"), ["StampEvent"] = ("stamp", "$s_gamemenu_popup_stampevent"),
        ["AttendGift"] = ("attend", "$s_gamemenu_popup_attendgift"), ["Pet"] = ("pet", "$s_gamemenu_btn_pet"),
        ["LiveBroadCast"] = ("live", "$s_gamemenu_btn_live_cam"), ["TimeRunEvent"] = ("timerun", "$s_gamemenu_btn_time_run_event"),
        ["Mentoring"] = ("mentoring", "$s_gamemenu_btn_mentoring"), ["DailyMission"] = ("dailymission", "$s_gamemenu_popup_dailymission"),
        ["FamePointBoard"] = ("fame", "$s_gamemenu_popup_fame"), ["GalleryEvent"] = ("gallery", "$s_gamemenu_popup_galleryevent"),
        ["SkyFortress"] = ("skyFortress", "$s_gamemenu_popup_sky_fortress"), ["Rps"] = ("rps", "$s_gamemenu_popup_rps"),
        ["CharacterSelect"] = ("characterSelect", "$s_gamemenu_popup_characterselect"), ["GameExit"] = ("end", "$s_gamemenu_popup_end"),
        ["AdventureLevel"] = ("adventure_level", "$s_gamemenu_popup_adventure_level"), ["WorldChampion"] = ("world_champion", "$s_gamemenu_popup_world_champion"),
        ["CashAttend"] = ("cashAttend", "$s_gamemenu_popup_cashAttend"), ["ChangeJob"] = ("jobchange", "$s_gamemenu_popup_changejob"),
        ["Lapenshard"] = ("lapenshard", "$s_gamemenu_btn_lapenshard"), ["CoupleDanceEvent"] = ("coupledance", "$s_gamemenu_popup_coupledance"),
        ["PssbShop"] = ("pssbshop", "$s_gamemenu_btn_pssbshop"), ["CollectItemGroupEvent"] = ("collectitemgroupevent", "$s_gamemenu_btn_collectitemgroupevent"),
        ["BingoEvent"] = ("bingoevent", "$s_gamemenu_btn_bingoevent"), ["Payback"] = ("payback", "$s_gamemenu_btn_payback"),
        ["Wedding"] = ("wedding", "$s_gamemenu_btn_wedding"), ["LongTermAttendGift"] = ("longtermattendgift", "$s_gamemenu_popup_longtermattendgift"),
        ["MarketGroup"] = ("marketgroup", "$s_gamemenu_btn_market_group"), ["CharacterGroup"] = ("charactergroup", "$s_gamemenu_btn_character_group"),
        ["ContentsGroup"] = ("contentsgroup", "$s_gamemenu_btn_contents_group"), ["GameGroup"] = ("gamegroup", "$s_gamemenu_btn_game_group"),
        ["CommunityGroup"] = ("communitygroup", "$s_gamemenu_btn_community_group"),
    };

    public sealed record Entry(string Id, string Name, string Caption, int Col, int Row, List<(string Name, string Caption)> Children);

    bool Allowed(XElement e)
    {
        var loc = (string)e.Attribute("locale");
        if (loc != null && !string.Equals(loc, Locale, StringComparison.OrdinalIgnoreCase)) return false;
        var feature = (string)e.Attribute("feature");
        if (feature != null && !FeatureOn(feature)) return false;
        return e.Attribute("filterType") == null;
    }

    public List<Entry> Entries(XDocument table)
    {
        var list = new List<Entry>();
        var set = table?.Root?.Elements("menuSet").FirstOrDefault(s => (string)s.Attribute("type") == "game");
        foreach (var m in set?.Elements("menu") ?? Enumerable.Empty<XElement>())
        {
            if (!Allowed(m) || !Defs.TryGetValue((string)m.Attribute("id") ?? "", out var def)) continue;
            List<(string, string)> kids = null;
            if (m.Elements("child").Any())
            {
                kids = new();
                foreach (var c in m.Elements("child"))
                    if (Allowed(c) && Defs.TryGetValue((string)c.Attribute("id") ?? "", out var cd)) kids.Add((cd.Name, cd.Caption));
            }
            list.Add(new Entry((string)m.Attribute("id"), def.Name, def.Caption, (int?)m.Attribute("col") ?? 0, (int?)m.Attribute("row") ?? 0, kids));
        }
        return list;
    }

    /// <summary>RefreshMenu: builds the entries; the host sets <see cref="Locale"/> and <see cref="FeatureOn"/> first.</summary>
    public void Build()
    {
        var menus = GetNode<Control>("m_menus");
        // configUI: the quest and pet attack buttons are hidden; no pet (initPetInfo) and no contract (initContract(true))
        foreach (var name in new[] { "m_camera", "m_directory" })
            if (GetNodeOrNull<NButton>(name) is { } b) { string n = name; b.Click += () => Host(n == "m_camera" ? "OnCamera" : "OnDirectory"); }
        foreach (var e in Entries(NativeUi.Assets.Table("table/gamemenu.xml")))
        {
            // addEntity: big menus are flagged by the client's code (isBig), none in the KR table's set
            string cls = e.Children != null ? "UIGameMenuDialog_group" : "UIGameMenuDialog_menu";
            var parts = NativeUi.Art.Parts(Movie, cls);
            var entry = new Control { Name = e.Name, Position = new Vector2(e.Col * 56, 120 - e.Row * 60), MouseFilter = MouseFilterEnum.Ignore };
            menus.AddChild(entry);
            menus.MoveChild(entry, 0);                                   // addChildAt(entity, 0)
            var bp = parts.First(p => p.Name == "m_button");
            var btn = new NMenuButton
            {
                Movie = Movie, Clip = NativeUi.Art.Symbol(Movie, "#" + bp.Id)?["class"]?.ToString() ?? "UIGameMenuDialog_dropdown_menu",
                LayerName = e.Name, Position = bp.Xf.Origin, TooltipText = () => NativeUi.Str(e.Caption),
            };
            if (e.Children != null) btn.Items = e.Children;
            btn.Menu += Host;
            entry.AddChild(btn);
            // setMenuTag: the caption, bottom aligned, no hit test
            var skin = NativeUi.Art.Bake(Movie, cls, 0, true);
            var tag = skin?.Texts.FirstOrDefault(t => t.Name == "tag");
            if (tag != null)
            {
                var l = NativeUi.PlaceLabel(entry, tag, Vector2.Zero, NativeUi.Str(e.Caption));
                // VALIGN_BOTTOM in a 56 x 37 box with word wrap (a word wider than the box breaks inside it); a caption
                // that needs more lines than fit shows from its first line, clipped (as the client draws "Board Sky
                // Fortress": only "Board" is seen)
                l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                l.ClipText = true;
                l.VerticalAlignment = VerticalAlignment.Bottom;
                Callable.From(() => { if (l.GetLineCount() > l.GetVisibleLineCount()) l.VerticalAlignment = VerticalAlignment.Top; }).CallDeferred();
            }
        }
    }

    void Host(string name) { Calls.Add(name); UiBridge.Raise("uigamemenudialog", name); }
}
