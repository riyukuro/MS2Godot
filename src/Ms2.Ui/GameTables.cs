using System.Globalization;
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

    public static readonly GameTables Empty = new();

    GameTables() { }

    public GameTables(PackSet packs, params string[] languages)
    {
        Items = new Items.ItemDataTable(packs);
        GachaBoxes = new Items.GachaBoxTable(packs);
        if (packs.Exists("Xml", "table/nextexp.xml"))
            foreach (var e in GameStrings.Parse(packs.Read("Xml", "table/nextexp.xml")).Descendants("exp"))
                if (int.TryParse((string)e.Attribute("level"), out var lv) && long.TryParse((string)e.Attribute("value"), out var v))
                    nextExp.TryAdd(lv, v);
        var s = new GameStrings(packs, languages);
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
        var k = new ClientConstants(packs);
        AdventureLevelLvUpExp = (long)k.Float("AdventureLevelLvUpExp", AdventureLevelLvUpExp);
        AdventureLevelStartLevel = (int)k.Float("AdventureLevelStartLevel", AdventureLevelStartLevel);
        MaxChatLength = (int)k.Float("MaxChatLength", MaxChatLength);
        MeratConsumeWorldChat = (uint)k.Float("MeratConsumeWorldChat", MeratConsumeWorldChat);
        MeratConsumeChannelChat = (uint)k.Float("MeratConsumeChannelChat", MeratConsumeChannelChat);
        foreach (var (cur, key) in TokenCapKeys) TokenCaps[cur] = (long)k.Float(key, TokenCaps[cur]);
        foreach (var key in ChatTextColors.Keys.ToList()) ChatTextColors[key] = Rgb(k.Color(key, Unpack(ChatTextColors[key])));
        // UIMessengerBrowser::LoadDefaultWindows (uimessenger.cpp:3149): DefaultMessenger0..99.xml in the default option folder
        for (int i = 0; i < 100; i++)
        {
            var path = $"table/default/defaultmessenger{i}.xml";
            if (packs.Exists("Xml", path)) DefaultMessengers.Add(GameStrings.Parse(packs.Read("Xml", path)));
        }
    }

    static (float, float, float) Unpack(uint c) => (((c >> 16) & 0xff) / 255f, ((c >> 8) & 0xff) / 255f, (c & 0xff) / 255f);
    static uint Rgb((float r, float g, float b) c)
        => ((uint)MathF.Round(c.r * 255) << 16) | ((uint)MathF.Round(c.g * 255) << 8) | (uint)MathF.Round(c.b * 255);

    readonly Dictionary<string, string> commonResource = new();

    /// <summary>A CommonResource member by its commonResource.xml key (e.g. HDR_PATH); "" when absent.</summary>
    public string CommonResource(string key) => commonResource.TryGetValue(key, out var v) ? v : "";

    /// <summary>Exp needed from <paramref name="level"/> to the next level; 0 = unknown level.</summary>
    public long NextExp(int level) => nextExp.TryGetValue(level, out var v) ? v : 0;
    public int MaxLevel => nextExp.Count == 0 ? 0 : nextExp.Keys.Max();
    /// <summary>The job's display name by sub job code (string table 6); the code as text when unknown.</summary>
    public string JobName(int subJob) => jobNames.TryGetValue(subJob.ToString(CultureInfo.InvariantCulture), out var n) && n != null ? n : subJob.ToString(CultureInfo.InvariantCulture);
    public string Common(string key) => common(key);
    /// <summary>CItemDataTable, read per file on first use.</summary>
    public readonly Items.ItemDataTable Items = Ms2.Ui.Items.ItemDataTable.Empty;
    /// <summary>ItemGachaBoxTable (table/gacha_info.xml), read on first use.</summary>
    public readonly Items.GachaBoxTable GachaBoxes = Ms2.Ui.Items.GachaBoxTable.Empty;
    /// <summary>Client::Constants AdventureLevelLvUpExp / AdventureLevelStartLevel (table/constants.xml; the Define defaults
    /// 15000 and 50, unit_1402ab440_constantsblocks.cpp:1155, :1157).</summary>
    public long AdventureLevelLvUpExp = 15000;
    public int AdventureLevelStartLevel = 50;
    /// <summary>Client::Constants MaxChatLength, MeratConsumeWorldChat, MeratConsumeChannelChat (Define defaults 50, 5, 2:
    /// unit_1402ab440_constantsblocks.cpp:1121, :970, :971).</summary>
    public int MaxChatLength = 50;
    public uint MeratConsumeWorldChat = 5, MeratConsumeChannelChat = 2;
    /// <summary>The token caps by currency (GetCurrencyAmountAndCap, uicommon.cpp): Client::Constants HonorTokenMax,
    /// KarmaTokenMax, LuTokenMax, HabiTokenMax, ReverseCoinMax, MentorTokenMax, MenteeTokenMax (Define default 4000 each,
    /// unit_1402ab440_constantsblocks.cpp:972-978); 0xf and 0x10 have no cap.</summary>
    public readonly Dictionary<int, long> TokenCaps = new() { [4] = 4000, [5] = 4000, [6] = 4000, [7] = 4000, [0xc] = 4000, [0xd] = 4000, [0xe] = 4000, [0xf] = 0, [0x10] = 0 };
    static readonly (int, string)[] TokenCapKeys =
    {
        (4, "HonorTokenMax"), (5, "KarmaTokenMax"), (6, "LuTokenMax"), (7, "HabiTokenMax"), (0xc, "ReverseCoinMax"),
        (0xd, "MentorTokenMax"), (0xe, "MenteeTokenMax"),
    };
    /// <summary>The chat colour constants (table/colorconstants.xml; Define defaults 0xffffff, constantsblocks.cpp:1426-1437),
    /// read as the subject colours s_color10 / 16..20 (texttohtml.cpp:300-310).</summary>
    public readonly Dictionary<string, uint> ChatTextColors = new()
    {
        ["chatTextNormal"] = 0xffffff, ["chatTextParty"] = 0xffffff, ["chatTextGuild"] = 0xffffff,
        ["chatTextSuper"] = 0xffffff, ["chatTextWorld"] = 0xffffff, ["chatTextChannel"] = 0xffffff,
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
