using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Cache;
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

    /// <summary>
    /// TextToHtml's chat colours s_color0 .. s_color24 (FUN_14183e690, texttohtml.cpp:285): the Client::Constants colour
    /// values from +0xb528 in steps of 0x58 (clientconstants.h:682-706), read from table/colorconstants.xml (Define defaults
    /// 0xffffff, unit_1402ab440_constantsblocks.cpp:1416-1440). Declared before <see cref="Empty"/>, which reads it.
    /// </summary>
    public static readonly string[] ChatColorKeys =
    {
        "chatUserNormal", "chatUserNotice", "chatUserWhisper", "chatUserMapNotice", "chatUserParty", "chatUserGuild",
        "chatUserSuper", "chatUserWorld", "chatUserChannel", "chatUserClub",
        "chatTextNormal", "chatTextNotice", "chatTextMapNotice", "chatTextWhisperTo", "chatTextWhisperFail", "chatTextWhisperFrom",
        "chatTextParty", "chatTextGuild", "chatTextSuper", "chatTextWorld", "chatTextChannel", "chatTextSystemMessage",
        "chatTextMinorSystemMessage", "chatTextGMColor", "chatTextClub",
    };
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
        Pets = new Items.PetDataTable(packs);
        ItemSockets = new Items.ItemSocketTable(packs);
        ItemModels = new Items.ItemModelTable(packs);
        GemstoneUpgrades = new Items.GemstoneUpgradeTable(packs);
        Lapenshards = new Items.LapenshardTable(packs);
        MasteryRecipes = new Items.MasteryRecipeTable(packs);
        var s = strings = new GameStrings(packs, languages);
        var r = read = CacheStore.Load(packs, CachePiece, CacheConfig(packs, s.Languages), CacheBuild, () => Parse(packs, s), _ => { });
        equipmentSellRates = r.EquipmentSellRates;
        nextExp = r.NextExp;
        jobNames = r.JobNames;
        mapNames = r.MapNames;
        mapTileDescs = r.MapTileDescs;
        commonResource = r.CommonResource;
        npcFunctionNames = r.NpcFunctionNames;
        fieldEnterances = r.FieldEnterances;
        raceNames = r.RaceNames;
        common = GameStrings.Common(r.CommonStrings, r.KeyNames);
        titleNames = r.TitleNames;
        itemNames = r.ItemNames;
        statNames = r.StatNames;
        statOptions = r.StatOptions;
        specialNames = r.SpecialNames;
        ItemOptions = new Items.ItemOptionTables(packs, r.StatValueNames, r.SpecialValueNames);
        Fishing = r.Fishing;
        XDocument Xml(string path) => packs.Exists("Xml", path) ? GameStrings.Parse(packs.Read("Xml", path)) : null;
        Fame = r.Fame;
        var k = FeatureElements.Constants(packs);
        AdventureLevelLvUpExp = (long)k.Float("AdventureLevelLvUpExp", AdventureLevelLvUpExp);
        AdventureLevelStartLevel = (int)k.Float("AdventureLevelStartLevel", AdventureLevelStartLevel);
        CharacterAbilityDefaultPoint = (uint)k.Float("CharacterAbilityDefaultPoint", CharacterAbilityDefaultPoint);
        CharacterAbilityResetCoolTimeMinute = (uint)k.Float("CharacterAbilityResetCoolTimeMinute", CharacterAbilityResetCoolTimeMinute);
        CharacterAbilityResetMeso = (long)k.Float("CharacterAbilityResetMeso", CharacterAbilityResetMeso);
        CharacterAbilityResetMerat = (int)k.Float("CharacterAbilityResetMerat", CharacterAbilityResetMerat);
        CharacterAbilityOpenQuestID = (uint)k.Float("CharacterAbilityOpenQuestID", CharacterAbilityOpenQuestID);
        MaxChatLength = (int)k.Float("MaxChatLength", MaxChatLength);
        PartyVoteReadyTagExpireSeconds = (int)k.Float("PartyVoteReadyTagExpireSeconds", PartyVoteReadyTagExpireSeconds);
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
        GuildCreatePrice = (int)k.Float("GuildCreatePrice", GuildCreatePrice);
        ShopBuyConfirmPrice = (uint)k.Float("ShopBuyConfirmPrice", ShopBuyConfirmPrice);
        ShopSellConfirmPrice = (uint)k.Float("ShopSellConfirmPrice", ShopSellConfirmPrice);
        ExtendAutoFishMaxCount = (uint)k.Float("extendAutoFishMaxCount", ExtendAutoFishMaxCount);
        ExtendAutoPlayInstrumentMaxCount = (uint)k.Float("extendAutoPlayInstrumentMaxCount", ExtendAutoPlayInstrumentMaxCount);
        GuildCreateMinLevel = (int)k.Float("GuildCreateMinLevel", GuildCreateMinLevel);
        PropertyProtectionTime = (int)k.Float("PropertyProtectionTime", PropertyProtectionTime);
        // Value<__int64> BlackMarketSellMinPrice / BlackMarketSellMaxPrice (read whole: an integer text), min clamped to 0 and
        // max raised to min (unit_1402ab440_constantsblocks.cpp:942-945); Value<int> BlackMarketSellEndDay (:1002)
        (BlackMarketSellMinPrice, BlackMarketSellMaxPrice) = BlackMarketPrices(k.String, BlackMarketSellMinPrice, BlackMarketSellMaxPrice);
        BlackMarketSellEndDay = (int)k.Float("BlackMarketSellEndDay", BlackMarketSellEndDay);
        GuildNameLengthMin = (int)k.Float("GuildNameLengthMin", GuildNameLengthMin);
        GuildNameLengthMax = (int)k.Float("GuildNameLengthMax", GuildNameLengthMax);
        UGCImgUploadSizeLimit = (int)k.Float("UGCImgUploadSizeLimit", UGCImgUploadSizeLimit);
        UGCImgFileCountCheck = (int)k.Float("UGCImgFileCountCheck", UGCImgFileCountCheck);
        // Common::Constants Value<__int64> TradeMinMeso / TradeMaxMeso (read whole: an integer text) and Value<int>
        // TradeRequestDuration / TradeFeePercent (unit_1402ab440_constantsblocks.cpp:891, :1007-1009)
        long whole(string key, long def) => long.TryParse(k.String(key), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out long v) ? v : (long)k.Float(key, def);
        TradeRequestDuration = (int)whole("TradeRequestDuration", TradeRequestDuration);
        TradeMinMeso = whole("TradeMinMeso", TradeMinMeso);
        TradeMaxMeso = whole("TradeMaxMeso", TradeMaxMeso);
        TradeFeePercent = (int)whole("TradeFeePercent", TradeFeePercent);
        // Value<int> StoreExpandMaxSlotCount / StoreExpandPrice1Row / StoreDepositMax / StoreWithdrawMax (constantsblocks.cpp:929-932)
        StoreExpandMaxSlotCount = (int)whole("StoreExpandMaxSlotCount", StoreExpandMaxSlotCount);
        StoreExpandPrice1Row = (int)whole("StoreExpandPrice1Row", StoreExpandPrice1Row);
        StoreDepositMax = (int)whole("StoreDepositMax", StoreDepositMax);
        StoreWithdrawMax = (int)whole("StoreWithdrawMax", StoreWithdrawMax);
        ItemUnLockTime = (uint)whole("ItemUnLockTime", ItemUnLockTime);
        // SkillDataTable::LoadSkillData (skilloverlapcompare.cpp:1195): every ./Data/Xml/SkillData/*.xml (FindSkillFiles
        // :1352, not recursive), read on first use
        List<XElement> collect(XElement parent, string child, string key) => FeatureElements.Collect(packs, parent, child, key);
        var skillFiles = packs.List("Xml").Where(n => n.StartsWith("skilldata/", StringComparison.OrdinalIgnoreCase)
            && n.IndexOf('/', 10) < 0 && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Skills = new Skills.SkillTables(r.Jobs, new Skills.SkillDataTable(() => skillFiles.Select(Xml), collect));
        additionalCache = packs.DataDir is { } dataDir ? Ms2.Ui.Skills.AdditionalCache.Open(AdditionalCacheOf(dataDir)) : null;
        Additional = NewAdditionalTable(packs, additionalCache);
        foreach (var key in ChatTextColors.Keys.ToList()) ChatTextColors[key] = Rgb(k.Color(key, Unpack(ChatTextColors[key])));
        MentoringShowIcon = r.MentoringShowIcon;
        foreach (var (level, accumExp) in r.GuildAccumExp) GuildAccumExp[level] = accumExp;
        // UIMessengerBrowser::LoadDefaultWindows (uimessenger.cpp:3149): DefaultMessenger0..99.xml in the default option folder
        for (int i = 0; i < 100; i++)
        {
            var path = $"table/default/defaultmessenger{i}.xml";
            if (packs.Exists("Xml", path)) DefaultMessengers.Add(GameStrings.Parse(packs.Read("Xml", path)));
        }
    }

    /// <summary>The tables the constructor reads (all but the constants and the documents it keeps), as one file of the
    /// per-install cache (<see cref="CacheStore"/>).</summary>
    sealed class ReadTables
    {
        public Dictionary<int, float> EquipmentSellRates = new();
        public Dictionary<int, long> NextExp = new();
        public Dictionary<string, string> CommonResource = new();
        public Dictionary<string, string> JobNames, MapNames, MapTileDescs, NpcFunctionNames, FieldEnterances, RaceNames,
            CommonStrings, KeyNames, TitleNames, ItemNames;
        public Dictionary<int, string> StatNames, StatOptions, SpecialNames;
        /// <summary>String tables stringstatname and stringspecialabilityname by id (the item option tables' value names).</summary>
        public Dictionary<string, string> StatValueNames, SpecialValueNames;
        public FishingSpots Fishing;
        /// <summary>CFameTable's fameDef.xml part.</summary>
        public FameTable Fame;
        /// <summary>MentoringConfig isShowIcon (default true) and the guild property map (accumExp by level).</summary>
        public bool MentoringShowIcon = true;
        public Dictionary<int, int> GuildAccumExp = new();
        public Skills.JobTable Jobs;
    }

    readonly ReadTables read;

    /// <summary>The first difference of two loads' read tables, null when they are the same (a cached load against a parse).</summary>
    public static string ReadDiff(GameTables a, GameTables b) => Snapshot.Diff(a.read, b.read);

    const string CachePiece = "gametables";

    /// <summary>The cache configuration: the string languages in order and the feature levels the rows are picked by.</summary>
    static string CacheConfig(PackSet packs, string[] languages) => $"{string.Join(",", languages)}|{LobbyFeatures.Stamp(packs)}";

    static string CacheBuild => CacheStore.Build(typeof(GameTables).Assembly, typeof(GameStrings).Assembly);

    /// <summary>Writes the cache file of <paramref name="language"/>'s tables when it is missing (the setup step); true when written.</summary>
    public static bool BuildCache(PackSet packs, string language, CacheStore cache)
    {
        var s = new GameStrings(packs, language);
        return cache.Ensure(CachePiece, CacheConfig(packs, s.Languages), CacheBuild, () => Parse(packs, s));
    }

    static ReadTables Parse(PackSet packs, GameStrings s)
    {
        var r = new ReadTables();
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
                r.EquipmentSellRates.TryAdd(key, value);                              // std::map::insert keeps the first
            }
            break;
        }
        if (packs.Exists("Xml", "table/nextexp.xml"))
            foreach (var e in GameStrings.Parse(packs.Read("Xml", "table/nextexp.xml")).Descendants("exp"))
                if (int.TryParse((string)e.Attribute("level"), out var lv) && long.TryParse((string)e.Attribute("value"), out var v))
                    r.NextExp.TryAdd(lv, v);
        r.JobNames = s.Table("jobname", "name");
        r.MapNames = s.Table("mapname", "name");
        r.MapTileDescs = s.Table("maptiledesc", "name");
        // CommonResource::LoadMembers (commonresource.cpp:91): Table/commonResource.xml, each member <KEY value="..."/>
        if (packs.Exists("Xml", "table/commonresource.xml"))
            foreach (var e in GameStrings.Parse(packs.Read("Xml", "table/commonresource.xml")).Root.Elements())
                r.CommonResource.TryAdd(e.Name.LocalName, (string)e.Attribute("value") ?? "");
        r.NpcFunctionNames = s.Table("npcfunctionname", "name");
        r.FieldEnterances = s.Table("stringfieldenterance", "name");
        r.RaceNames = s.Table("racename", "name");
        (r.CommonStrings, r.KeyNames) = s.CommonTables();
        r.TitleNames = s.Table("titlename", "name");
        r.ItemNames = s.Table("itemname", "name");
        // the item option tables read the same two string tables (only looked up, as CommonTexts does)
        r.StatValueNames = s.Table("stringstatname", "name");
        r.SpecialValueNames = s.Table("stringspecialabilityname", "name");
        r.StatNames = CommonTexts(packs, "table/statstringtable.xml", r.StatValueNames);
        r.StatOptions = Options(packs, "table/statstringtable.xml");
        r.SpecialNames = CommonTexts(packs, "table/specialabilitystringtable.xml", r.SpecialValueNames);
        // CFishingTable::LoadSpots (fishsimultedata.cpp:331) and CMasteryTable::LoadMastery (masterytable.cpp:173)
        XDocument Xml(string path) => packs.Exists("Xml", path) ? GameStrings.Parse(packs.Read("Xml", path)) : null;
        r.Fishing = new FishingSpots(Xml("table/fishingspot.xml"), Xml("table/mastery.xml"), e => FeatureElements.IsAvailable(packs, e));
        // CFameTable::LoadFameDef (famegradedata.cpp:606): GetXmlPath("Table", "fameDef.xml")
        r.Fame = new FameTable(Xml("table/famedef.xml"), e => FeatureElements.IsAvailable(packs, e));
        // MentoringDataTable::Load (mentoringdatatable.cpp:55): GetXmlPath("Table", "MentoringConfig.xml"), <ms2>, <MentoringConfig>
        if (Xml("table/mentoringconfig.xml")?.Root is { Name.LocalName: "ms2" } mentoring && mentoring.Element("MentoringConfig") is { } mentoringConfig)
            Ms2.Ui.Items.ItemData.Bool(mentoringConfig, "isShowIcon", ref r.MentoringShowIcon);
        // CGuildXmlData::LoadGuildProperty (@0x1404d5a50, guilddef_2.cpp:489): Table/GuildProperty.xml, <ms2>, the
        // <property> elements collected by level (FUN_14028faa0), each read by GuildProperty::FUN_1404d4990 ("level" f18,
        // "accumExp" f1c) into a map by level
        if (Xml("table/guildproperty.xml")?.Root is { Name.LocalName: "ms2" } guildRoot)
            foreach (var e in FeatureElements.Collect(packs, guildRoot, "property", "level"))
            {
                int.TryParse((string)e.Attribute("level"), out int level);
                int.TryParse((string)e.Attribute("accumExp"), out int accumExp);
                r.GuildAccumExp[level] = accumExp;
            }
        // JobTable::Load (jobtable.cpp:309): table/job.xml
        r.Jobs = new Skills.JobTable(Xml("table/job.xml"), (parent, child, key) => FeatureElements.Collect(packs, parent, child, key));
        return r;
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
    /// <summary>CFameTable's fameDef.xml part (the officer npcs' alliance symbols); empty without data.</summary>
    public FameTable Fame { get; } = FameTable.Empty;

    /// <summary>CStringTable::FUN_141811980(1, id): the item name (string table 1, itemname.xml), "" when unknown; names
    /// holding "$" get the object words (e.g. "$map:02000302$ Treasure Key"), as CStringTable::Load @0x141810f70 does for
    /// every table at load (stringtable.cpp:640).</summary>
    public string ItemName(int id)
    {
        var key = id.ToString(CultureInfo.InvariantCulture);
        if (!itemNames.TryGetValue(key, out var n) || n == null) return "";
        if (n.Contains('$')) itemNames[key] = n = ReservedWords.ReplaceObjectWords(n);
        return n;
    }
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
    Hud.UserCommands userCommands;
    /// <summary>CUserCommandCli: Table/chatCommand.xml with string table 0x38 (string/&lt;language&gt;/stringchatcommand.xml
    /// "name"), read on first use.</summary>
    public Hud.UserCommands UserCommands => userCommands ??= packs == null ? new Hud.UserCommands()
        : new Hud.UserCommands(packs, strings?.Table("stringchatcommand", "name"), common);
    /// <summary>CItemDataTable, read per file on first use.</summary>
    public readonly Items.ItemDataTable Items = Ms2.Ui.Items.ItemDataTable.Empty;
    /// <summary>ItemGachaBoxTable (table/gacha_info.xml), read on first use.</summary>
    public readonly Items.GachaBoxTable GachaBoxes = Ms2.Ui.Items.GachaBoxTable.Empty;
    /// <summary>CPetDataTable (table/petproperty.xml), read on first use.</summary>
    public readonly Items.PetDataTable Pets = Ms2.Ui.Items.PetDataTable.Empty;
    /// <summary>table/itemSocket.xml (CItemSocketTable); empty without data.</summary>
    public readonly Items.ItemSocketTable ItemSockets = Ms2.Ui.Items.ItemSocketTable.Empty;
    /// <summary>The stat and special ability definitions, CItemOptionTable's random and variation rows and
    /// CItemEnchantLimitTable; empty without data.</summary>
    public readonly Items.ItemOptionTables ItemOptions = Ms2.Ui.Items.ItemOptionTables.Empty;
    /// <summary>itemmodel/*.xml (CItemDataTable::GetModel); empty without data.</summary>
    public readonly Items.ItemModelTable ItemModels = Ms2.Ui.Items.ItemModelTable.Empty;
    readonly Dictionary<int, float> equipmentSellRates = new();
    /// <summary>CGloabalFactor::GetMultiControlFactor(3, rank) (gloabalfactor.cpp:189): the equipmentSellPrice rate, 1 when none.</summary>
    public float EquipmentSellRate(int rank) => equipmentSellRates.TryGetValue(rank, out var v) ? v : 1f;
    /// <summary>Table/itemGemstoneUpgrade.xml (CItemGemStoneUpgradeTable); empty without data.</summary>
    public readonly Items.GemstoneUpgradeTable GemstoneUpgrades = Ms2.Ui.Items.GemstoneUpgradeTable.Empty;
    /// <summary>Table/masteryReceipe.xml (CMasteryTable's recipes); empty without data.</summary>
    public readonly Items.MasteryRecipeTable MasteryRecipes = Ms2.Ui.Items.MasteryRecipeTable.Empty;
    /// <summary>Table/itemLapenshardUpgrade.xml (CItemLapenshardTable); empty without data.</summary>
    public readonly Items.LapenshardTable Lapenshards = Ms2.Ui.Items.LapenshardTable.Empty;
    /// <summary>Client::Constants AdventureLevelLvUpExp / AdventureLevelStartLevel (table/constants.xml; the Define defaults
    /// 15000 and 50, unit_1402ab440_constantsblocks.cpp:1155, :1157).</summary>
    public long AdventureLevelLvUpExp = 15000;
    public int AdventureLevelStartLevel = 50;
    /// <summary>Client::Constants CharacterAbilityDefaultPoint, CharacterAbilityResetCoolTimeMinute, CharacterAbilityResetMeso,
    /// CharacterAbilityResetMerat, CharacterAbilityOpenQuestID (table/constants.xml; the Define defaults 10, 120, 2500, 100 and
    /// 90000060, unit_1402ab440_constantsblocks.cpp:1045-1049).</summary>
    public uint CharacterAbilityDefaultPoint = 10, CharacterAbilityResetCoolTimeMinute = 120, CharacterAbilityOpenQuestID = 90000060;
    public long CharacterAbilityResetMeso = 2500;
    public int CharacterAbilityResetMerat = 100;
    /// <summary>Client::Constants MaxChatLength, MeratConsumeWorldChat, MeratConsumeChannelChat (Define defaults 50, 5, 2:
    /// unit_1402ab440_constantsblocks.cpp:1121, :970, :971).</summary>
    public int MaxChatLength = 50;
    /// <summary>PartyVoteReadyTagExpireSeconds (+0xe608, default 10): how long the ready marks stay after a ready check ends.</summary>
    public int PartyVoteReadyTagExpireSeconds = 10;
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
    /// <summary>Client::Constants GuildCreatePrice / GuildCreateMinLevel (Define defaults 1000, 0:
    /// unit_1402ab440_constantsblocks.cpp:947, :948) and GuildNameLengthMin / GuildNameLengthMax (2, 10: :1193, :1194), read by
    /// the guild create box (UIGuildDialog::OnClickCreateGuild), the create send (CGuildManager::FUN_1413b6d10) and
    /// CBanTextChecker::CheckGuildName.</summary>
    public int GuildCreatePrice = 1000, GuildCreateMinLevel = 0, GuildNameLengthMin = 2, GuildNameLengthMax = 10;
    /// <summary>Client::Constants PropertyProtectionTime (+0x6aa8, Define default 0: unit_1402ab440_constantsblocks.cpp:1040),
    /// shown by the black market's result 37 (CBlackMarketMan::FUN_140a21ca0).</summary>
    public int PropertyProtectionTime = 0;
    /// <summary>Client::Constants ShopBuyConfirmPrice (+0xc188) and ShopSellConfirmPrice (+0xc130, Define defaults 0u:
    /// unit_1402ab440_constantsblocks.cpp:1451-1452), the meso thresholds that pop a confirmation box in
    /// UIShopDialog::ShowPurchaseBill (uishopdialog.cpp:3175) and SellItem (uimessagebox.cpp:680).</summary>
    public uint ShopBuyConfirmPrice = 0, ShopSellConfirmPrice = 0;
    /// <summary>Client::Constants extendAutoFishMaxCount (+0xdd20) and extendAutoPlayInstrumentMaxCount (+0xdd78, Define
    /// defaults 8u: unit_1402ab440_constantsblocks.cpp:1531-1532; uisystemshopdialog.cpp:280/:332).</summary>
    public uint ExtendAutoFishMaxCount = 8, ExtendAutoPlayInstrumentMaxCount = 8;
    /// <summary>Client::Constants BlackMarketSellMinPrice / BlackMarketSellMaxPrice (Define defaults 10 and 9900000000, the
    /// min at least 0 and the max at least the min: unit_1402ab440_constantsblocks.cpp:942-945), the register price limits
    /// (BlackMarketPageItemRegister::OnClickRegister), and BlackMarketSellEndDay (7: :1002), the sale days its texts show.</summary>
    public long BlackMarketSellMinPrice = 10, BlackMarketSellMaxPrice = 9900000000;

    /// <summary>
    /// The Value&lt;__int64&gt; reads of BlackMarketSellMinPrice / BlackMarketSellMaxPrice: the integer text read whole (a
    /// float would round 9900000000 to 9900000256), the default when the tables do not set it; then min clamped to 0 and
    /// max raised to min (unit_1402ab440_constantsblocks.cpp:942-945).
    /// </summary>
    /// <param name="text">The constant's text, or null when it is not in the tables.</param>
    public static (long min, long max) BlackMarketPrices(Func<string, string> text, long defMin, long defMax)
    {
        long read(string key, long def)
        {
            string s = text(key);
            if (s == null) return def;
            if (long.TryParse(s.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long v)) return v;
            return double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) ? (long)d : def;
        }
        long min = Math.Max(0, read("BlackMarketSellMinPrice", defMin));
        return (min, Math.Max(min, read("BlackMarketSellMaxPrice", defMax)));
    }
    public int BlackMarketSellEndDay = 7;
    /// <summary>CGuildXmlData's property map: the accumulated exp (accumExp) by guild level, in level order.</summary>
    public readonly SortedDictionary<int, int> GuildAccumExp = new();

    /// <summary>CGuildXmlData::GetLevelByExp (@0x1404d7130, guilddef_2.cpp:621): 0 without the table; the level of the first
    /// entry, in level order, whose accumExp is above <paramref name="exp"/>; else GetMaxLevel, the last level.</summary>
    public int GuildLevelByExp(int exp)
    {
        if (GuildAccumExp.Count == 0) return 0;
        foreach (var (level, accumExp) in GuildAccumExp)
            if (exp < accumExp) return level;
        return GuildAccumExp.Keys.Last();
    }
    /// <summary>Client::Constants UGCImgUploadSizeLimit / UGCImgFileCountCheck (Define defaults 1024, 300:
    /// unit_1402ab440_constantsblocks.cpp:772, :773): the largest width and height a UGC image may have, and the file count
    /// at which CUgcManager::RefreshLocalFiles warns (ugcmanager_4.cpp:2794).</summary>
    public int UGCImgUploadSizeLimit = 1024, UGCImgFileCountCheck = 300;
    /// <summary>Client::Constants TradeRequestDuration in seconds (Define default 20: unit_1402ab440_constantsblocks.cpp:891):
    /// the timer of the trade request box (CTradeManagerEx::OnTradeRequest, trademanagerex_1.cpp:385).</summary>
    public int TradeRequestDuration = 20;
    /// <summary>Client::Constants TradeMinMeso / TradeMaxMeso (Define defaults 100 and 300000000:
    /// unit_1402ab440_constantsblocks.cpp:1007, :1008): GetMinMesoOffer / GetMaxMesoOffer
    /// (unit_14087d0c0_trademesolimits.cpp, +0x5f00 / +0x5f60).</summary>
    public long TradeMinMeso = 100, TradeMaxMeso = 300000000;
    /// <summary>Client::Constants TradeFeePercent (Define default 10: unit_1402ab440_constantsblocks.cpp:1009):
    /// GetTradeFeePercent (+0x5fc0).</summary>
    public int TradeFeePercent = 10;
    /// <summary>Client::Constants StoreExpandMaxSlotCount (+0x44a8), StoreExpandPrice1Row (+0x4500), StoreDepositMax (+0x4558) and
    /// StoreWithdrawMax (+0x45b0), all Define default 0 (unit_1402ab440_constantsblocks.cpp:929-932): the bank's most extra slots,
    /// the price of an expansion and the most mesos one deposit / withdrawal moves.</summary>
    public int StoreExpandMaxSlotCount, StoreExpandPrice1Row, StoreDepositMax, StoreWithdrawMax;
    /// <summary>Client::Constants ItemUnLockTime (Define default 259200, constantsblocks.cpp:1027): seconds an unlock request waits; the lock dialog shows it in hours.</summary>
    public uint ItemUnLockTime = 259200;
    /// <summary>table/job.xml and skilldata/*.xml (JobTable, SkillDataTable); empty without data.</summary>
    public readonly Skills.SkillTables Skills = Ms2.Ui.Skills.SkillTables.Empty;
    /// <summary>additional/*.xml (SkillDataTableCli GetAdditional / FUN_140aafef0); empty without data.</summary>
    public readonly Skills.AdditionalTable Additional = Ms2.Ui.Skills.AdditionalTable.Empty;
    readonly Ms2.Ui.Skills.AdditionalCache additionalCache;

    /// <summary>The per-install <see cref="Ms2.Ui.Skills.AdditionalCache"/> file of a data folder (the game sets it: user://uigen/KEY/...);
    /// null = none, the additional tables parse their XML on first use.</summary>
    public static Func<string, string> AdditionalCacheOf = _ => null;

    /// <summary>SkillDataTableCli's additional effects: ./Data/Xml/Additional/*.xml (not recursive), feature selected.</summary>
    internal static Ms2.Ui.Skills.AdditionalTable NewAdditionalTable(PackSet packs, Ms2.Ui.Skills.AdditionalCache cache = null)
    {
        List<string> files = null;
        return new Ms2.Ui.Skills.AdditionalTable(
            () => files ??= packs.List("Xml").Where(n => n.StartsWith("additional/", StringComparison.OrdinalIgnoreCase)
                && n.IndexOf('/', 11) < 0 && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n, StringComparer.Ordinal).ToList(),
            path => packs.Exists("Xml", path) ? GameStrings.Parse(packs.Read("Xml", path)) : null,
            e => LobbyFeatures.IsEnabledByName(packs, (string)e.Attribute("feature")), cache, SpecialAbilityTypes(packs));
    }

    /// <summary>
    /// The &lt;SpecialAbility&gt; type names of the additional effects: GetSATypeByName @0x14081d2f0 over g_mapSANameToType,
    /// which FUN_14081c880 (specialability.cpp:101) fills from the rows 1-0xce of LoadSpecialAbilityStringTable (:131,
    /// table/specialAbilityStringTable.xml &lt;key id option valueType&gt;) by case-insensitive option name, a name's first row
    /// kept (std::map insert). The resolver gives 0 for an unknown name and for a row whose valueType is 0 (the
    /// m_bAllowAdditional test, additionalactionadditional.cpp:1240).
    /// </summary>
    internal static Func<string, int> SpecialAbilityTypes(PackSet packs)
    {
        const string table = "table/specialabilitystringtable.xml";
        return SpecialAbilityTypes(packs.Exists("Xml", table) ? GameStrings.Parse(packs.Read("Xml", table)) : null);
    }

    internal static Func<string, int> SpecialAbilityTypes(XDocument doc)
    {
        var rows = new Dictionary<int, (string Option, int ValueType)>();
        if (doc?.Root != null)
            foreach (var e in doc.Root.Elements("key"))
                if (int.TryParse((string)e.Attribute("id"), out int id) && id is > 0 and < 0xcf)
                    rows[id] = ((string)e.Attribute("option") ?? "", Ms2.Ui.Skills.Xml.Int(e, "valueType"));
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < 0xcf; i++)
            byName.TryAdd(rows.TryGetValue(i, out var r) ? r.Option : "", i);
        return name => byName.TryGetValue(name, out int t) && rows.TryGetValue(t, out var r) && r.ValueType != 0 ? t : 0;
    }

    /// <summary>FUN_14081dcd0 (specialability.cpp:467): whether the special ability's row (DAT_143ab1a40[id], filled by
    /// LoadSpecialAbilityStringTable at the row's id, :185-195) has valueType 2; false for an id without a row.</summary>
    public static Func<int, bool> SpecialAbilityPerMille(PackSet packs)
    {
        const string table = "table/specialabilitystringtable.xml";
        return SpecialAbilityPerMille(packs.Exists("Xml", table) ? GameStrings.Parse(packs.Read("Xml", table)) : null);
    }

    public static Func<int, bool> SpecialAbilityPerMille(XDocument doc)
    {
        var rows = new Dictionary<int, int>();
        if (doc?.Root != null)
            foreach (var e in doc.Root.Elements("key"))
                if (int.TryParse((string)e.Attribute("id"), out int id))
                    rows[id] = Ms2.Ui.Skills.Xml.Int(e, "valueType");
        return id => rows.TryGetValue(id, out int t) && t == 2;
    }

    /// <summary>One language's stringAdditionalDescription.xml as LoadLevelDescFiles reads it (see
    /// <see cref="AdditionalDescription"/>): per id, per level, the ParseSkillDesc tags; null without the file.</summary>
    internal static Dictionary<uint, SortedDictionary<ushort, Dictionary<string, string>>> LevelDescFile(PackSet packs, string lang)
    {
        string entry = $"string/{lang}/stringadditionaldescription.xml";
        if (!packs.Exists("Xml", entry)) return null;
        var root = GameStrings.Parse(packs.Read("Xml", entry)).Root;
        if (root?.Name != "ms2") return null;
        var file = new Dictionary<uint, SortedDictionary<ushort, Dictionary<string, string>>>();
        foreach (var e in FeatureElements.Collect(packs, root, "key", "id", "level"))
        {
            uint key = (uint)ReservedWords.Atoi((string)e.Attribute("id") ?? "");
            ushort lv = e.Attribute("level") is { } la ? (ushort)ReservedWords.Atoi((string)la) : (ushort)1;
            var desc = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var t in SkillDescTags)
                if ((string)e.Attribute(t) is { Length: > 0 } v) desc[t] = v.Replace("\\n", "\n");
            if (!file.TryGetValue(key, out var levels)) file[key] = levels = new SortedDictionary<ushort, Dictionary<string, string>>();
            levels[lv] = desc;
        }
        return file;
    }

    /// <summary>
    /// CStringTable::FUN_141812290 (stringtable.cpp:774): the <paramref name="tag"/> text of an additional effect's level in
    /// stringAdditionalDescription.xml. LoadLevelDescFiles (:1171) reads the available &lt;key&gt; rows one per id and level
    /// (FUN_14028fd90: a later one replaces the kept one when SelectFeatureElement picks it), each with the ParseSkillDesc
    /// tags (:1289; non-empty, "\n" as a line break). FUN_141815160 (:1216) takes the level's row, else FUN_141815330: the
    /// row of the highest level not above it ("" when the first is higher). The text's object words are replaced on first
    /// use (CDesc::FUN_141813a30, :574). "tooltipDescription" is then filled with the level's comma separated stringParam
    /// (FUN_141813790, :984), and "name" gets "$level$" replaced by the level (ReplaceLevelToken). With several string
    /// languages, the first language that has the effect is used (the client loads one language's file).
    /// </summary>
    public string AdditionalDescription(uint id, ushort level, string tag)
    {
        var langs = strings?.Languages ?? Array.Empty<string>();
        SortedDictionary<ushort, Dictionary<string, string>> byLevel = null;
        if (additionalCache?.Valid == true)
        {
            // the cache: the first language whose file has the id, decoded once
            levelDescs ??= new Dictionary<uint, SortedDictionary<ushort, Dictionary<string, string>>>();
            if (!levelDescs.TryGetValue(id, out byLevel))
            {
                foreach (var lang in langs)
                    if ((byLevel = additionalCache.ReadDesc(lang.ToLowerInvariant(), id)) != null) break;
                levelDescs[id] = byLevel;
            }
        }
        else
        {
            if (levelDescs == null)
            {
                levelDescs = new Dictionary<uint, SortedDictionary<ushort, Dictionary<string, string>>>();
                foreach (var lang in langs)
                    if (LevelDescFile(packs, lang) is { } file)
                        foreach (var (k, v) in file) levelDescs.TryAdd(k, v);
            }
            levelDescs.TryGetValue(id, out byLevel);
        }
        string text = "";
        if (byLevel != null)
        {
            Dictionary<string, string> d = null;
            if (!byLevel.TryGetValue(level, out d))
                foreach (var (lv, row) in byLevel)
                {
                    if (level < lv) break;
                    d = row;
                }
            if (d != null && d.TryGetValue(tag, out var t))
            {
                if (t.Contains('$')) d[tag] = t = ReservedWords.ReplaceObjectWords(t);
                text = t;
            }
        }
        if (string.Equals(tag, "tooltipDescription", StringComparison.OrdinalIgnoreCase)
            && Additional.GetLevel(id, level) is { StringParam.Length: > 0 } lvData)
            text = Ms2.Quest.ClientFormat.Format(text, lvData.StringParam.Split(',').Cast<object>().ToArray());
        if (string.Equals(tag, "name", StringComparison.OrdinalIgnoreCase))
            text = text.Replace("$level$", level.ToString(CultureInfo.InvariantCulture));
        return text;
    }
    /// <summary>ParseSkillDesc (stringtable.cpp:1289): "name" (read first by FUN_141815650) and its tags.</summary>
    static readonly string[] SkillDescTags = { "name", "uiDescription", "mainDescription", "functionDescription", "tooltipDescription", "guideDescription" };
    Dictionary<uint, SortedDictionary<ushort, Dictionary<string, string>>> levelDescs;

    /// <summary>CStringTable::FUN_141811980(4, id): the skill's name (string table 4 "skillName"); "" when unknown.</summary>
    public string SkillName(uint id) => Lazy(ref skillNames, "skillname", "name").TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var n) ? n ?? "" : "";

    /// <summary>
    /// CDynamicActionData::Find (@0x140347050, dynamicactiondata.cpp) for the skill: the row's "reserved" action paths,
    /// each "/" + one part of the attribute split at "|" (FUN_14025a680, CStringW::Tokenize: empty parts skipped);
    /// null when no row has the skill. The rows are CDynamicActionData::Load's (@0x140347180): korDynamicAction.xml of
    /// the string folder (FUN_14071ae20), the &lt;key&gt; children of &lt;ms2&gt; picked by FUN_14028faa0(root, "key",
    /// "skillID"), and Find takes the first row in that order. With several string languages, the first language whose
    /// file loads is used (the client loads one language's file).
    /// </summary>
    public IReadOnlyList<string> DynamicActionReserved(uint skillId)
    {
        if (dynamicActions == null)
        {
            dynamicActions = new Dictionary<uint, string[]>();
            foreach (var lang in strings?.Languages ?? Array.Empty<string>())
            {
                string entry = $"string/{lang}/kordynamicaction.xml";
                if (!packs.Exists("Xml", entry)) continue;
                var root = GameStrings.Parse(packs.Read("Xml", entry)).Root;
                if (root == null || root.Name.LocalName != "ms2") break;      // no <ms2>: Load returns with no rows
                foreach (var e in FeatureElements.Collect(packs, root, "key", "skillID"))
                {
                    uint id = unchecked((uint)ReservedWords.Atoi((string)e.Attribute("skillID") ?? ""));
                    if (dynamicActions.ContainsKey(id)) continue;      // Find: the first row with the id
                    string[] parts = ((string)e.Attribute("reserved") ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < parts.Length; i++) parts[i] = "/" + parts[i];
                    dynamicActions[id] = parts;
                }
                break;
            }
        }
        return dynamicActions.TryGetValue(skillId, out var r) ? r : null;
    }
    Dictionary<uint, string[]> dynamicActions;
    /// <summary>CStringTable::FUN_141811980(7, id): string table 7 "popupMenu" (a popup menu item's label); "" when unknown.</summary>
    /// <summary>CStringTable::FUN_141811980(0x2f, id): stringMasteryReceipeName, empty when none.</summary>
    public string MasteryRecipeName(int id) => Lazy(ref masteryRecipeNames, "stringmasteryreceipename", "name").TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var n) ? n ?? "" : "";
    Dictionary<string, string> masteryRecipeNames;
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
    /// <summary>CStringTable::FUN_141811980(0x4d, id): string table 77 "jobGroupName", a job group's name (1 Beginner, 10
    /// Knight ... 110, 999); "" when unknown.</summary>
    public string JobGroupName(int id) => Lazy(ref jobGroupNames, "jobgroupname", "name").TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var n) ? n ?? "" : "";
    Dictionary<string, string> jobGroupNames;
    /// <summary>CStringTable::FUN_141811980(0x27, id): string table 39 "nametagSymbolTitle", a name tag symbol's name; ""
    /// when unknown.</summary>
    public string NametagSymbolTitle(int id) => Lazy(ref nametagSymbolTitles, "nametagsymboltitle", "name").TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var n) ? n ?? "" : "";
    Dictionary<string, string> nametagSymbolTitles;

    /// <summary>CStringTable::FUN_141811980(2, id): the npc name (string table 2, npcname.xml), "" when unknown.</summary>
    public string NpcName(int id) => Lazy(ref npcNames, "npcname", "name").TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var n) ? n ?? "" : "";
    /// <summary>CStringTable::FUN_141811980(0x13, id): the npc title (string table 0x13, npctitle.xml), "" when unknown.</summary>
    public string NpcTitle(int id) => Lazy(ref npcTitles, "npctitle", "name").TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var n) ? n ?? "" : "";
    /// <summary>FUN_1401aa6a0 (arcademaindata.cpp:62): "&lt;title&gt; &lt;name&gt;", or the name alone when the npc has no title.</summary>
    public string NpcNameWithTitle(int id) => NpcTitle(id) is { Length: > 0 } t ? t + " " + NpcName(id) : NpcName(id);
    Dictionary<string, string> npcNames, npcTitles;
    /// <summary>CStringTable::FUN_141811980(0x23 / 0x24 / 0x25, id): string tables 35 "recommendContentsName", 36
    /// "recommendContentsSearch" and 37 "recommendContentsTooltip", a level-up guide row's label, world map search text and
    /// tooltip; "" when unknown.</summary>
    public string RecommendContentsName(int id) => Recommend(ref recommendNames, "recommendcontentsname", id);
    public string RecommendContentsSearch(int id) => Recommend(ref recommendSearches, "recommendcontentssearch", id);
    public string RecommendContentsTooltip(int id) => Recommend(ref recommendTooltips, "recommendcontentstooltip", id);
    /// <summary>A recommend-contents string; texts holding "$" get the object words, as CStringTable::Load @0x141810f70
    /// does for every table at load.</summary>
    string Recommend(ref Dictionary<string, string> d, string table, int id)
    {
        var t = Lazy(ref d, table, "name");
        if (!t.TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var n) || n == null) return "";
        if (n.Contains('$')) t[id.ToString(CultureInfo.InvariantCulture)] = n = ReservedWords.ReplaceObjectWords(n);
        return n;
    }
    Dictionary<string, string> recommendNames, recommendSearches, recommendTooltips;
    NametagSymbols nametagSymbols;
    FishTable fish;
    TitleTags titleTags;
    /// <summary>CFishingTable's fish and habitat maps (Table/fish.xml, fishHabitat.xml), read on first use.</summary>
    public FishTable Fish => fish ??= packs == null ? FishTable.Empty
        : new FishTable(Xml("table/fish.xml"), Xml("table/fishhabitat.xml"), e => FeatureElements.IsAvailable(packs, e));
    /// <summary>CTitleTable's tags (Table/titleTag.xml), read on first use.</summary>
    public TitleTags TitleTags => titleTags ??= packs == null ? TitleTags.Empty
        : new TitleTags(Xml("table/titletag.xml"), e => FeatureElements.IsAvailable(packs, e));
    Ms2.Ui.Skills.SkillCompactControlTable skillCompactControl;
    /// <summary>CSkillCompactControlTable (Table/SkillCompactControl.xml), read on first use; empty with feature 0x10
    /// (CompactControl) off, as Load @0x140dca800 reads nothing then.</summary>
    public Ms2.Ui.Skills.SkillCompactControlTable SkillCompactControl => skillCompactControl ??= packs == null
        ? Ms2.Ui.Skills.SkillCompactControlTable.Empty
        : new Ms2.Ui.Skills.SkillCompactControlTable(Xml("table/skillcompactcontrol.xml"), e => FeatureElements.IsAvailable(packs, e),
            LobbyFeatures.IsEnabled(packs, "CompactControl"));
    XDocument Xml(string path) => packs.Exists("Xml", path) ? GameStrings.Parse(packs.Read("Xml", path)) : null;
    /// <summary>CNametagSymbolTable (Table/nametagSymbol.xml), read on first use.</summary>
    public NametagSymbols NametagSymbols => nametagSymbols ??= packs == null ? NametagSymbols.Empty : new NametagSymbols(packs);
    BlackMarketTable blackMarket;
    /// <summary>CBlackMarketTable (Table/blackMarketTable.xml), read on first use.</summary>
    public BlackMarketTable BlackMarket => blackMarket ??= packs == null || !packs.Exists("Xml", "table/blackmarkettable.xml")
        ? BlackMarketTable.Empty
        : new BlackMarketTable(GameStrings.Parse(packs.Read("Xml", "table/blackmarkettable.xml")),
            f => LobbyFeatures.IsEnabledByName(packs, f), CategoryName, ItemOptions.FindStatByName, ItemOptions.FindSpecialByName,
            StatName, SpecialAbilityName);

    /// <summary>
    /// CStringTable::FUN_141811980(table, id) for the tables of table/string.xml the trophy dialog reads: 1 itemName,
    /// 20 stringFieldEnterance, 24 achieveName, 29 stringFishName, 30 stringFishDesc, 31 stringFishDetailDesc, 59 petName, 74 achieveCategoryName, and 80
    /// specialQuestType (string/&lt;lang&gt;/specialquesttype.xml, the character ability dialog's); "" for an unknown id or
    /// another table.
    /// </summary>
    public string StringTableText(int table, long id)
    {
        string file = table switch
        {
            1 => "itemname", 0x2f => "stringmasteryreceipename", 0x30 => "stringmasteryreceipedesc", 0x14 => "stringfieldenterance", 0x18 => "achievename", 0x1d => "stringfishname", 0x1e => "stringfishdesc", 0x1f => "stringfishdetaildesc",
            0x3b => "petname", 0x4a => "achievecategoryname", 0x50 => "specialquesttype", _ => null,
        };
        if (file == null) return "";
        if (!numberedTables.TryGetValue(file, out var d)) numberedTables[file] = d = strings?.Table(file, "name") ?? new Dictionary<string, string>();
        return d.TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var n) ? n ?? "" : "";
    }
    readonly Dictionary<string, Dictionary<string, string>> numberedTables = new();

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
    /// <summary>The chat colours by key (<see cref="ChatColorKeys"/>).</summary>
    public readonly Dictionary<string, uint> ChatTextColors = ChatColorKeys.ToDictionary(k => k, _ => 0xffffffu);
    /// <summary>s_color<paramref name="n"/> (<see cref="ChatColorKeys"/>).</summary>
    public uint ChatColor(int n) => ChatTextColors[ChatColorKeys[n]];
    /// <summary>MentoringDataTable m_bShowIcon (+0x30, mentoringdatatable.cpp:37 default true): table/MentoringConfig.xml
    /// &lt;ms2&gt;&lt;MentoringConfig isShowIcon&gt; (LoadConfig :77, XmlGetAttr bool).</summary>
    public bool MentoringShowIcon = true;
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
