using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui;

/// <summary>
/// The game tables the HUD controllers read: table/nextexp.xml (CExpTable), string/&lt;lang&gt;/jobname.xml (string table 6)
/// and stringcommon.xml (GetStringCommonByKey). Read from the client's own Xml.m2d; <see cref="Empty"/> without data.
/// </summary>
public sealed class GameTables
{
    readonly Dictionary<int, long> nextExp = new();
    readonly Dictionary<string, string> jobNames = new();
    readonly Dictionary<string, string> mapNames = new();
    readonly Dictionary<string, string> mapTileDescs = new(), npcFunctionNames = new(), fieldEnterances = new(), raceNames = new();
    readonly Func<string, string> common = _ => null;
    readonly Dictionary<string, string> titleNames = new(), itemNames = new();
    readonly Dictionary<int, string> statNames = new(), specialNames = new(), statOptions = new();

    public static readonly GameTables Empty = new();

    // the client reads its tables once (each table singleton loads at startup or on first use) and every window reads the
    // same ones; nothing changes a GameTables after it is built, so the windows' models share one per pack set and language
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, System.Collections.Concurrent.ConcurrentDictionary<string, GameTables>> shared = new();

    /// <summary>The tables of <paramref name="packs"/> in <paramref name="language"/>, built on the first call (main thread).</summary>
    public static GameTables Shared(PackSet packs, string language) =>
        shared.GetOrCreateValue(packs).GetOrAdd(language, l => new GameTables(packs, l));

    GameTables() { }

    public GameTables(PackSet packs, params string[] languages)
    {
        this.packs = packs;
        Items = new Items.ItemDataTable(packs);
        GachaBoxes = new Items.GachaBoxTable(packs);
        ItemSockets = new Items.ItemSocketTable(packs);
        ItemModels = new Items.ItemModelTable(packs);
        GemstoneUpgrades = new Items.GemstoneUpgradeTable(packs);
        // CGloabalFactor::LoadMultiControl (gloabalfactor.cpp:128): Table/multicontrol.xml, the locale folder first; the
        // tooltip reads only type equipmentSellPrice (3), keyed by rank
        foreach (var path in new[] { "table/kr/multicontrol.xml", "table/multicontrol.xml" })
        {
            if (!packs.Exists("Xml", path)) continue;
            foreach (var e in GameStrings.Parse(packs.Read("Xml", path)).Root?.Elements("multi") ?? Enumerable.Empty<XElement>())
            {
                if (!string.Equals((string)e.Attribute("type"), "equipmentSellPrice", StringComparison.OrdinalIgnoreCase)) continue;
                int key = Ms2.Ui.Items.ItemData.Atoi((string)e.Attribute("key") ?? "0");
                float value = float.TryParse(((string)e.Attribute("value"))?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 1f;
                equipmentSellRates.TryAdd(key, value);                                // std::map::insert keeps the first
            }
            break;
        }
        if (packs.Exists("Xml", "table/nextexp.xml"))
            foreach (var e in GameStrings.Parse(packs.Read("Xml", "table/nextexp.xml")).Descendants("exp"))
                if (int.TryParse((string)e.Attribute("level"), out var lv) && long.TryParse((string)e.Attribute("value"), out var v))
                    nextExp.TryAdd(lv, v);
        var s = strings = new GameStrings(packs, languages);
        jobNames = s.Table("jobname", "name");
        mapNames = s.Table("mapname", "name");
        mapTileDescs = s.Table("maptiledesc", "name");
        // CommonResource::LoadMembers (commonresource.cpp:91): Table/commonResource.xml, each member <KEY value="..."/>
        if (packs.Exists("Xml", "table/commonresource.xml"))
            foreach (var e in GameStrings.Parse(packs.Read("Xml", "table/commonresource.xml")).Root.Elements())
                commonResource.TryAdd(e.Name.LocalName, (string)e.Attribute("value") ?? "");
        npcFunctionNames = s.Table("npcfunctionname", "name");
        fieldEnterances = s.Table("stringfieldenterance", "name");
        raceNames = s.Table("racename", "name");
        common = s.Common();
        titleNames = s.Table("titlename", "name");
        itemNames = s.Table("itemname", "name");
        statNames = CommonTexts(packs, "table/statstringtable.xml", s.Table("stringstatname", "name"));
        statOptions = Options(packs, "table/statstringtable.xml");
        specialNames = CommonTexts(packs, "table/specialabilitystringtable.xml", s.Table("stringspecialabilityname", "name"));
        // CFishingTable::LoadSpots (fishsimultedata.cpp:331) and CMasteryTable::LoadMastery (masterytable.cpp:173)
        XDocument Xml(string path) => packs.Exists("Xml", path) ? GameStrings.Parse(packs.Read("Xml", path)) : null;
        Fishing = new FishingSpots(Xml("table/fishingspot.xml"), Xml("table/mastery.xml"), e => FeatureElements.IsAvailable(packs, e));
        var k = FeatureElements.Constants(packs);
        AdventureLevelLvUpExp = (long)k.Float("AdventureLevelLvUpExp", AdventureLevelLvUpExp);
        AdventureLevelStartLevel = (int)k.Float("AdventureLevelStartLevel", AdventureLevelStartLevel);
        MaxChatLength = (int)k.Float("MaxChatLength", MaxChatLength);
        PCBangItemDefaultPeriod = (uint)k.Float("PCBangItemDefaultPeriod", PCBangItemDefaultPeriod);
        MeratConsumeWorldChat = (uint)k.Float("MeratConsumeWorldChat", MeratConsumeWorldChat);
        MeratConsumeChannelChat = (uint)k.Float("MeratConsumeChannelChat", MeratConsumeChannelChat);
        foreach (var (cur, key) in TokenCapKeys) TokenCaps[cur] = (long)k.Float(key, TokenCaps[cur]);
        // ConstantsBlocks (unit_1402ab440_constantsblocks.cpp:1169): read by JobSystem::GetLevelSkillPoint (jobsystem.cpp:301)
        SkillPointLimitLevel1 = (int)k.Float("SkillPointLimitLevel1", SkillPointLimitLevel1);
        SkillPointLimitLevel2 = (int)k.Float("SkillPointLimitLevel2", SkillPointLimitLevel2);
        BothHandGearScorePenaltyDiv = k.Float("BothHandGearScorePenaltyDiv", BothHandGearScorePenaltyDiv);
        ClubNameLengthMin = (int)k.Float("ClubNameLengthMin", ClubNameLengthMin);
        ClubNameLengthMax = (int)k.Float("ClubNameLengthMax", ClubNameLengthMax);
        // JobTable::Load (jobtable.cpp:309): table/job.xml; SkillDataTable::LoadSkillData (skilloverlapcompare.cpp:1195): every
        // ./Data/Xml/SkillData/*.xml (FindSkillFiles :1352, not recursive), read on first use
        List<XElement> collect(XElement parent, string child, string key) => FeatureElements.Collect(packs, parent, child, key);
        var skillFiles = packs.List("Xml").Where(n => n.StartsWith("skilldata/", StringComparison.OrdinalIgnoreCase)
            && n.IndexOf('/', 10) < 0 && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Skills = new Skills.SkillTables(new Skills.JobTable(Xml("table/job.xml"), collect),
            new Skills.SkillDataTable(() => skillFiles.Select(Xml), collect));
        foreach (var key in ChatTextColors.Keys.ToList()) ChatTextColors[key] = Rgb(k.Color(key, Unpack(ChatTextColors[key])));
        // UIMessengerBrowser::LoadDefaultWindows (uimessenger.cpp:3149): DefaultMessenger0..99.xml in the default option folder
        for (int i = 0; i < 100; i++)
        {
            var path = $"table/default/defaultmessenger{i}.xml";
            if (packs.Exists("Xml", path)) DefaultMessengers.Add(GameStrings.Parse(packs.Read("Xml", path)));
        }
    }

    /// <summary>LoadStatDefinitions (stat.cpp:172) / LoadSpecialAbilityStringTable (specialability.cpp:170): per &lt;key id&gt; of
    /// the table, the name string whose id is its commonStringID (std::map operator[]: "" for a missing one).</summary>
    static Dictionary<int, string> CommonTexts(PackSet packs, string table, Dictionary<string, string> names)
    {
        var d = new Dictionary<int, string>();
        if (!packs.Exists("Xml", table)) return d;
        foreach (var e in GameStrings.Parse(packs.Read("Xml", table)).Root.Elements("key"))
            if (int.TryParse((string)e.Attribute("id"), out int id))
                d[id] = names.TryGetValue((string)e.Attribute("commonStringID") ?? "", out var n) && n != null ? n : "";
        return d;
    }

    /// <summary>LoadStatDefinitions (stat.cpp:172): per &lt;key id&gt; its "option" (the StatDefinition's name).</summary>
    static Dictionary<int, string> Options(PackSet packs, string table)
    {
        var d = new Dictionary<int, string>();
        if (!packs.Exists("Xml", table)) return d;
        foreach (var e in GameStrings.Parse(packs.Read("Xml", table)).Root.Elements("key"))
            if (int.TryParse((string)e.Attribute("id"), out int id)) d[id] = (string)e.Attribute("option") ?? "";
        return d;
    }

    /// <summary>GetStatName (stat.cpp:271): the stat's option name, "" past 0x23 or without one.</summary>
    public string StatOption(int stat) => (uint)stat < 0x23 && statOptions.TryGetValue(stat, out var n) ? n : "";
    /// <summary>GetStatCommonText (stat.cpp:280): the stat's common text, "" past 0x23 or without one.</summary>
    public string StatName(int stat) => (uint)stat < 0x23 && statNames.TryGetValue(stat, out var n) ? n : "";
    /// <summary>GetSATypeName (itemsocketsystemutil.cpp:2132): the special ability's common text (SpecialAbilityInfo m_strName).</summary>
    public string SpecialAbilityName(int type) => specialNames.TryGetValue(type, out var n) ? n : "";
    /// <summary>CStringTable::FUN_141811980(0x16, id): the title name (table/string.xml 22 = titleName), "" when unknown. The
    /// reserved word tokens (CReservedWordTable::ReplaceTokensForPc / ForGender, util.cpp:1439) are not replaced.</summary>
    public string TitleName(int id) => titleNames.TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var n) && n != null ? n : "";

    static (float, float, float) Unpack(uint c) => (((c >> 16) & 0xff) / 255f, ((c >> 8) & 0xff) / 255f, (c & 0xff) / 255f);
    static uint Rgb((float r, float g, float b) c)
        => ((uint)MathF.Round(c.r * 255) << 16) | ((uint)MathF.Round(c.g * 255) << 8) | (uint)MathF.Round(c.b * 255);

    readonly Dictionary<string, string> commonResource = new();

    /// <summary>The fishing spots and fishing mastery grades (CFishingTable, CMasteryTable type 1); empty without data.</summary>
    public FishingSpots Fishing { get; } = new(null, null, _ => true);

    /// <summary>CStringTable::FUN_141811980(1, id): the item name (string table 1, itemname.xml), "" when unknown.</summary>
    public string ItemName(int id) => itemNames.TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var n) && n != null ? n : "";
    /// <summary>A CommonResource member by its commonResource.xml key (e.g. HDR_PATH); "" when absent.</summary>
    public string CommonResource(string key) => commonResource.TryGetValue(key, out var v) ? v : "";

    /// <summary>Exp needed from <paramref name="level"/> to the next level; 0 = unknown level.</summary>
    public long NextExp(int level) => nextExp.TryGetValue(level, out var v) ? v : 0;
    public int MaxLevel => nextExp.Count == 0 ? 0 : nextExp.Keys.Max();
    /// <summary>The job's display name by sub job code (string table 6); the code as text when unknown.</summary>
    public string JobName(int subJob) => jobNames.TryGetValue(subJob.ToString(CultureInfo.InvariantCulture), out var n) && n != null ? n : subJob.ToString(CultureInfo.InvariantCulture);
    public string Common(string key) => common(key);
    /// <summary>GetStringCommon (commonutil.cpp:860): the common string of the id (CommonString::RegisterIds keys); "" when
    /// the id or its key is unknown.</summary>
    public string Common(int id) => Ms2.Formats.Text.CommonStringIds.Key(id) is { } key ? common(key) ?? "" : "";
    Hud.ServerStringGroup triggerStrings;
    /// <summary>CServerString m_trigger: string/&lt;language&gt;/stringtrigger.xml, read on first use.</summary>
    public Hud.ServerStringGroup TriggerStrings => triggerStrings ??= packs == null ? Hud.ServerStringGroup.Empty
        : Hud.ServerStringGroup.Load(packs, strings.Languages, "stringtrigger.xml", e => FeatureElements.IsAvailable(packs, e), ReservedWords.ReplaceObjectWords);
    /// <summary>CItemDataTable, read per file on first use.</summary>
    public readonly Items.ItemDataTable Items = Ms2.Ui.Items.ItemDataTable.Empty;
    /// <summary>ItemGachaBoxTable (table/gacha_info.xml), read on first use.</summary>
    public readonly Items.GachaBoxTable GachaBoxes = Ms2.Ui.Items.GachaBoxTable.Empty;
    /// <summary>table/itemSocket.xml (CItemSocketTable); empty without data.</summary>
    public readonly Items.ItemSocketTable ItemSockets = Ms2.Ui.Items.ItemSocketTable.Empty;
    /// <summary>itemmodel/*.xml (CItemDataTable::GetModel); empty without data.</summary>
    public readonly Items.ItemModelTable ItemModels = Ms2.Ui.Items.ItemModelTable.Empty;
    readonly Dictionary<int, float> equipmentSellRates = new();
    /// <summary>CGloabalFactor::GetMultiControlFactor(3, rank) (gloabalfactor.cpp:189): the equipmentSellPrice rate, 1 when none.</summary>
    public float EquipmentSellRate(int rank) => equipmentSellRates.TryGetValue(rank, out var v) ? v : 1f;
    /// <summary>Table/itemGemstoneUpgrade.xml (CItemGemStoneUpgradeTable); empty without data.</summary>
    public readonly Items.GemstoneUpgradeTable GemstoneUpgrades = Ms2.Ui.Items.GemstoneUpgradeTable.Empty;
    /// <summary>Client::Constants AdventureLevelLvUpExp / AdventureLevelStartLevel (table/constants.xml; the Define defaults
    /// 15000 and 50, unit_1402ab440_constantsblocks.cpp:1155, :1157).</summary>
    public long AdventureLevelLvUpExp = 15000;
    public int AdventureLevelStartLevel = 50;
    /// <summary>Client::Constants MaxChatLength, MeratConsumeWorldChat, MeratConsumeChannelChat (Define defaults 50, 5, 2:
    /// unit_1402ab440_constantsblocks.cpp:1121, :970, :971).</summary>
    public int MaxChatLength = 50;
    /// <summary>Client::Constants PCBangItemDefaultPeriod in minutes (Define default 1440: unit_1402ab440_constantsblocks.cpp:981).</summary>
    public uint PCBangItemDefaultPeriod = 1440;
    public uint MeratConsumeWorldChat = 5, MeratConsumeChannelChat = 2;
    /// <summary>Client::Constants SkillPointLimitLevel1 / SkillPointLimitLevel2 (defaults 80, 100).</summary>
    public int SkillPointLimitLevel1 = 80, SkillPointLimitLevel2 = 100;
    /// <summary>Client::Constants BothHandGearScorePenaltyDiv (default 4.0, unit_1402ab440_constantsblocks.cpp:1248): the
    /// divisor of the main-hand item level with one one-handed weapon (PC::vfunc_126, pc.cpp:4235).</summary>
    public float BothHandGearScorePenaltyDiv = 4.0f;
    /// <summary>Client::Constants ClubNameLengthMin / ClubNameLengthMax (Define defaults 2, 12:
    /// unit_1402ab440_constantsblocks.cpp:1195, :1196), read by UIPartyDialog::OnCreateClubClick and the club name check.</summary>
    public int ClubNameLengthMin = 2, ClubNameLengthMax = 12;
    /// <summary>table/job.xml and skilldata/*.xml (JobTable, SkillDataTable); empty without data.</summary>
    public readonly Skills.SkillTables Skills = Ms2.Ui.Skills.SkillTables.Empty;
    /// <summary>CStringTable::FUN_141811980(4, id): the skill's name (string table 4 "skillName"); "" when unknown.</summary>
    public string SkillName(uint id) => Lazy(ref skillNames, "skillname", "name").TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var n) ? n ?? "" : "";
    /// <summary>CStringTable::FUN_141811980(7, id): string table 7 "popupMenu" (a popup menu item's label); "" when unknown.</summary>
    public string PopupMenuName(int id) => Lazy(ref popupMenuNames, "popupmenu", "name").TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var n) ? n ?? "" : "";
    /// <summary>CStringTable::FUN_141811980(8, id): string table 8 "slotName" (a weapon kind's name); "" when unknown.</summary>
    public string SlotName(int id) => Lazy(ref slotNames, "slotname", "name").TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var n) ? n ?? "" : "";
    /// <summary>The "name" text of the skill's row in stringSkillDescription.xml (CStringTable::GetSkillDescription
    /// @stringtable.cpp:750 reads the tag "name"); null when the skill has no row.</summary>
    public string SkillDescriptionText(uint id) => Lazy(ref skillDescs, "stringskilldescription", "name").TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var n) ? n ?? "" : null;

    /// <summary>
    /// CStringTable::GetItemDescription (stringtable.cpp:737): the <paramref name="tag"/> text of the item's row in
    /// korItemDescription.xml, "" without one. The rows are read as FUN_141814750 (:1113) does: each available &lt;key&gt;
    /// (IsElementAvailable) by id, a later one replacing an earlier one (SetAt), with FUN_141815650 (:1259) keeping the
    /// non-empty tags with "\n" turned into line breaks. The text is returned with its object words replaced
    /// (CDesc::FUN_141813a30, :574: ReplaceObjectWords on first use). With several string languages, the first language
    /// that has the row is used (the client loads one language's file).
    /// </summary>
    public string ItemDescription(int id, string tag)
    {
        if (itemDescs == null)
        {
            itemDescs = new Dictionary<int, Dictionary<string, string>>();
            foreach (var lang in strings?.Languages ?? Array.Empty<string>())
            {
                string entry = $"string/{lang}/koritemdescription.xml";
                if (!packs.Exists("Xml", entry)) continue;
                var file = new Dictionary<int, Dictionary<string, string>>();
                foreach (var e in GameStrings.Parse(packs.Read("Xml", entry)).Root.Elements("key"))
                {
                    if (!FeatureElements.IsAvailable(packs, e)) continue;
                    var desc = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var t in ItemDescTags)
                        if ((string)e.Attribute(t) is { Length: > 0 } v) desc[t] = v.Replace("\\n", "\n");
                    file[ReservedWords.Atoi((string)e.Attribute("id") ?? "")] = desc;
                }
                foreach (var (k, v) in file) itemDescs.TryAdd(k, v);
            }
        }
        if (!itemDescs.TryGetValue(id, out var d) || !d.TryGetValue(tag, out var text)) return "";
        if (text.Contains('$'))
        {
            d[tag] = text = ReservedWords.ReplaceObjectWords(text);
        }
        return text;
    }
    /// <summary>ParseItemDesc (stringtable.cpp:1247): the tags kept, with "name" (read first by FUN_141815650).</summary>
    static readonly string[] ItemDescTags = { "name", "mainDescription", "functionDescription", "tooltipDescription", "guideDescription" };
    readonly PackSet packs;
    Dictionary<int, Dictionary<string, string>> itemDescs;
    ReservedWords reservedWords;
    /// <summary>The reserved word table for value words (CReservedWordTable), over these string tables.</summary>
    public ReservedWords ReservedWords => reservedWords ??= new ReservedWords(strings, null);

    GameStrings strings;
    Dictionary<string, string> skillNames, slotNames, skillDescs, popupMenuNames;
    /// <summary>CStringTable::FUN_141811980(13, id): string table 13 "categoryName", a shop tab's name (CShopCategoryTable
    /// TabInfo::GetName, shopcategorytable.cpp:152); "" when unknown.</summary>
    public string CategoryName(int id) => Lazy(ref categoryNames, "categoryname", "name").TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var n) ? n ?? "" : "";
    /// <summary>CStringTable::FUN_141811980(0x53, id): string table 83 "stringShopTitle", a shop's title by shop id; "" when unknown.</summary>
    public string ShopTitle(int id) => Lazy(ref shopTitles, "stringshoptitle", "name").TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var n) ? n ?? "" : "";
    Dictionary<string, string> categoryNames, shopTitles;
    /// <summary>CStringTable::FUN_141811980(0x27, id): string table 39 "nametagSymbolTitle", a name tag symbol's name; ""
    /// when unknown.</summary>
    public string NametagSymbolTitle(int id) => Lazy(ref nametagSymbolTitles, "nametagsymboltitle", "name").TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var n) ? n ?? "" : "";
    Dictionary<string, string> nametagSymbolTitles;
    NametagSymbols nametagSymbols;
    /// <summary>CNametagSymbolTable (Table/nametagSymbol.xml), read on first use.</summary>
    public NametagSymbols NametagSymbols => nametagSymbols ??= packs == null ? NametagSymbols.Empty : new NametagSymbols(packs);

    Dictionary<string, string> Lazy(ref Dictionary<string, string> d, string table, string attr)
        => d ??= strings?.Table(table, attr) ?? new Dictionary<string, string>();
    /// <summary>The token caps by currency (GetCurrencyAmountAndCap, uicommon.cpp): Client::Constants HonorTokenMax,
    /// KarmaTokenMax, LuTokenMax, HabiTokenMax, ReverseCoinMax, MentorTokenMax, MenteeTokenMax (Define default 4000 each,
    /// unit_1402ab440_constantsblocks.cpp:972-978); 0xf and 0x10 have no cap.</summary>
    public readonly Dictionary<int, long> TokenCaps = new() { [4] = 4000, [5] = 4000, [6] = 4000, [7] = 4000, [0xc] = 4000, [0xd] = 4000, [0xe] = 4000, [0xf] = 0, [0x10] = 0 };
    static readonly (int, string)[] TokenCapKeys =
    {
        (4, "HonorTokenMax"), (5, "KarmaTokenMax"), (6, "LuTokenMax"), (7, "HabiTokenMax"), (0xc, "ReverseCoinMax"),
        (0xd, "MentorTokenMax"), (0xe, "MenteeTokenMax"),
    };
    /// <summary>The chat colour constants (table/colorconstants.xml; Define defaults 0xffffff, constantsblocks.cpp:1426-1438),
    /// read as the subject colours s_color10 / 16..20 (texttohtml.cpp:300-310) and the system line colour s_color21.</summary>
    public readonly Dictionary<string, uint> ChatTextColors = new()
    {
        ["chatTextNormal"] = 0xffffff, ["chatTextParty"] = 0xffffff, ["chatTextGuild"] = 0xffffff,
        ["chatTextSuper"] = 0xffffff, ["chatTextWorld"] = 0xffffff, ["chatTextChannel"] = 0xffffff,
        ["chatTextSystemMessage"] = 0xffffff,
    };
    /// <summary>The default messenger windows' documents (table/default/defaultmessenger&lt;i&gt;.xml), in order.</summary>
    public readonly List<System.Xml.Linq.XDocument> DefaultMessengers = new();
    /// <summary>The world map's field description (string table 0x4b, string/&lt;lang&gt;/maptiledesc.xml); null = none.</summary>
    public string MapTileDesc(int mapId) => mapTileDescs.TryGetValue(mapId.ToString("D8", CultureInfo.InvariantCulture), out var n) ? n : null;
    /// <summary>An npc's function title (string table 10 "npcFunctionName", table/string.xml); null = none.</summary>
    public string NpcFunctionName(int npcId) => npcFunctionNames.TryGetValue(npcId.ToString("D8", CultureInfo.InvariantCulture), out var n) ? n : null;
    /// <summary>A dungeon room's name (string table 20 "stringFieldEnterance", by room id); null = none.</summary>
    public string FieldEnteranceName(int roomId) => fieldEnterances.TryGetValue(roomId.ToString("D8", CultureInfo.InvariantCulture), out var n) ? n : null;
    /// <summary>A race's name (string table 17 "raceName", parser raceName: by <see cref="Ms2.Formats.World.NpcTable.RaceKeys"/>); null = none.</summary>
    public string RaceName(int race) => Ms2.Formats.World.NpcTable.RaceName(raceNames, race);
    /// <summary>The map's display name (string table mapname, id = 8-digit map id); null when unknown.</summary>
    public string MapName(int mapId) => mapNames.TryGetValue(mapId.ToString("D8", CultureInfo.InvariantCulture), out var n) ? n : null;
}
