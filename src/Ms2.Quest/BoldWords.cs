using System.Text;
using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Quest;

/// <summary>
/// CReservedWordTable::m_mapBold (reservedwordtable.cpp:205-220): the object words with $item, $map, $npc, $npcName,
/// $npcPlural, $npcNamePlural, $dungeonTitle, $quest and $petCollectGroup replaced by the Rw_*Bold functions
/// (reservedwordtable.cpp:930-1066), which wrap the name in a link.
/// </summary>
public sealed partial class ReservedWords
{
    /// <summary>
    /// CFeatureManager::IsFeatureEnabled by feature id; the client asks for 0x2f, "GameDictionary" (FeatureTable.Names[0x2f - 1]).
    /// Defaults to false for every id, as a client without the feature.
    /// </summary>
    public Func<int, bool> FeatureEnabled = _ => false;

    /// <summary>The feature id of "GameDictionary" (reservedwordtable.cpp:938, 995; itemdata.cpp:386; npcdata.cpp:1470).</summary>
    public const int GameDictionaryFeature = 0x2f;

    // The link colour of every Bold word (reservedwordtable.cpp:940-1066).
    const string LinkColor = "#5c2b00";

    /// <summary>The ids of CItemDataTable (itemdatatable.cpp:743-765, LoadItemDataFile): an item exists when its element has an available environment.</summary>
    HashSet<long> itemIds = new();
    /// <summary>The npcs of FindNpcData with their "friendly" attribute (npcdata.cpp:1449).</summary>
    Dictionary<long, int> npcFriendly = new();
    /// <summary>GameDictionaryManager m_items, m_npcs, m_mobs (gamedictionarymanager.cpp:52-128, LoadEntry): the ids whose key has a value other than 0.</summary>
    HashSet<long> dictItems = new(), dictNpcs = new(), dictMobs = new();
    /// <summary>WorldMapManager m_68 (worldmapmanager.cpp:527, LoadFieldTiles) keys; HasFieldSearchInfo (worldmapmanager.cpp:924) is a find in it.</summary>
    HashSet<long> fieldSearch = new();

    /// <summary>
    /// ReplaceBoldWords (reservedwordtable.cpp:378): FUN_1416fc320 over m_mapBold, the object words with the nine
    /// link words in place of their plain ones. "$pp" is still the particle word, read over the text with the links in.
    /// </summary>
    public string ReplaceBoldWords(string str) => Scan(str, BoldWord);

    string BoldWord(string token, string name, string arg, string whole)
    {
        long id = FieldMissions.Int(arg);
        switch (name)
        {
            case "$item": return ItemBold(id);
            case "$map": return MapBold(id);
            case "$npc": return NpcLink(id, NpcFullName(id));                     // Rw_NpcBold
            case "$npcName": return NpcLink(id, Get(NpcName, id));               // Rw_NpcNameBold
            case "$npcPlural": return NpcLink(id, NpcPluralName(id));            // Rw_NpcPluralBold
            case "$npcNamePlural": return NpcLink(id, Get(NpcNamePlural, id));   // Rw_NpcNamePluralBold
            case "$dungeonTitle": return DungeonTitleBold(id);
            case "$quest": return QuestBold(id);
            case "$petCollectGroup": return PetCollectGroupBold(id);
            default: return ObjectWord(token, name, arg, whole);                 // m_mapBold = m_mapObject copy (reservedwordtable.cpp:205-210)
        }
    }

    /// <summary>
    /// Rw_NpcLink (reservedwordtable.cpp:930-947): a link only for an npc FindNpcData has (id 0 has none, npcdata.cpp:1047)
    /// whose m_bBossNotifyDefault is set, else the plain text. With the GameDictionary feature the link is the
    /// "npcdictionary" event. Without it the client builds a "webPage" link from "maview:/Encyclopedia/..." through
    /// FUN_14016a730 (STILL-OPEN, not in the decomp); m_bBossNotifyDefault is only ever set with the feature on
    /// (npcdata.cpp:1470), so that branch cannot be reached with <see cref="FeatureEnabled"/> consistent, and the text is returned.
    /// </summary>
    string NpcLink(long id, string text)
    {
        if (!npcFriendly.TryGetValue(id, out int friendly) || !BossNotifyDefault(id, friendly)) return text;
        if (!FeatureEnabled(GameDictionaryFeature)) return text;
        return $"<u><a href='event:npcdictionary,{id}'><FONT color='{LinkColor}'>{text}</FONT></a></u>";
    }

    /// <summary>
    /// m_bBossNotifyDefault (npcdata.cpp:1470-1490): false unless the feature is on; then, with the dictionary manager, "Get(1, id) == 0"
    /// for a friendly npc (GetFriendly() &gt; 0) or "Get(2, id) == 0" for a monster, where Get returns a null pointer for an id the
    /// dictionary lacks. Taken literally: the flag is set for an id the dictionary does NOT hold (polarity unverified against the binary).
    /// </summary>
    bool BossNotifyDefault(long id, int friendly)
    {
        if (!FeatureEnabled(GameDictionaryFeature)) return false;
        return friendly > 0 ? !dictNpcs.Contains(id) : !dictMobs.Contains(id);
    }

    /// <summary>
    /// Rw_ItemBold (reservedwordtable.cpp:982-1020): "" for id 0 and for an item the table lacks. The item's name text is
    /// returned unless its flag09 is set (itemdata.cpp:386: the GameDictionary feature on and the item in the dictionary),
    /// when it is the "itemdictionary" link. Without the feature the client builds a "webPage" link from FUN_14016a730
    /// (STILL-OPEN); flag09 needs the feature, so that branch is not reached and the name is returned.
    /// </summary>
    string ItemBold(long id)
    {
        if (id == 0) return "";
        if (!itemIds.Contains(id)) return "";
        string name = Get(ItemName, id);
        if (FeatureEnabled(GameDictionaryFeature) && dictItems.Contains(id))
            return $"<u><a href='event:itemdictionary,{id}'><FONT color='{LinkColor}'>{name}</FONT></a></u>";
        return name;
    }

    /// <summary>Rw_MapBold (reservedwordtable.cpp:1022-1037): "" for id 0; the "worldmap" link when WorldMapManager::HasFieldSearchInfo(id), else the map name.</summary>
    string MapBold(long id)
    {
        if (id == 0) return "";
        string name = Get(MapName, id);
        return fieldSearch.Contains(id) ? $"<u><a href='event:worldmap,{id}'><FONT color='{LinkColor}'>{name}</FONT></a></u>" : name;
    }

    /// <summary>Rw_DungeonTitleBold (reservedwordtable.cpp:1039-1046): "" for id 0, else the "dungeon" link around the dungeon title (FUN_1416ff950, the $dungeonTitle text).</summary>
    string DungeonTitleBold(long id) =>
        id == 0 ? "" : $"<u><a href='event:dungeon,{id}'><FONT color='{LinkColor}'>{Get(FieldEnterance, id)}</FONT></a></u>";

    /// <summary>Rw_QuestBold (reservedwordtable.cpp:1049-1058): "" for id 0, else the "quest" link around GetQuestName.</summary>
    string QuestBold(long id) =>
        id == 0 ? "" : $"<u><a href='event:quest,{id}'><FONT color='{LinkColor}'>{QuestName?.Invoke((int)id) ?? ""}</FONT></a></u>";

    /// <summary>Rw_PetCollectGroupBold (reservedwordtable.cpp:1060-1066): the "petCollectGroup" link, no test for id 0.</summary>
    string PetCollectGroupBold(long id) =>
        $"<u><a href='event:petCollectGroup,{id}'><FONT color='{LinkColor}'>{Get(PetCollectGroupName, id)}</FONT></a></u>";

    /// <summary>
    /// Loads what the Bold words test: the item and npc tables, the three dictionary tables and the world map's field search
    /// set. <paramref name="available"/> is IsElementAvailable; <paramref name="parentField"/> is CFieldMetaTable::GetParentField
    /// (<see cref="FieldMeta.ParentFields"/>).
    /// Where a file has several available environments the client picks one (FUN_14028f8d0, SelectFeatureElement,
    /// featuremanager.cpp:1049: the higher feature level, then the locale); here the last available one stands in for it.
    /// </summary>
    public void LoadBold(PackSet packs, Func<XElement, bool> available, IReadOnlyDictionary<int, int> parentField)
    {
        const string A = "Xml";
        XElement Pick(XElement e) => e.Elements("environment").Where(available).LastOrDefault();
        XDocument Doc(string entry)
        {
            try { return XDocument.Parse(Encoding.UTF8.GetString(packs.Read(A, entry)).TrimStart('﻿')); }
            catch (System.Xml.XmlException) { return null; }
        }
        var entries = packs.List(A).ToList();
        bool Under(string n, string dir) => n.StartsWith(dir, StringComparison.OrdinalIgnoreCase) && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

        // itemdatatable.cpp:743-765: <item id> with a picked environment
        itemIds = new();
        foreach (var entry in entries.Where(n => Under(n, "itemdata/")))
            foreach (var item in Doc(entry)?.Root?.Elements("item") ?? Enumerable.Empty<XElement>())
                if (Pick(item) != null) itemIds.Add(FieldMissions.Long((string)item.Attribute("id")));

        // npcdata.cpp:1449: friendly of the picked environment (FindNpcData)
        npcFriendly = new();
        foreach (var entry in entries.Where(n => Under(n, "npcdata/")))
            foreach (var npc in Doc(entry)?.Root?.Elements("npc") ?? Enumerable.Empty<XElement>())
            {
                var env = Pick(npc);
                if (env != null) npcFriendly[FieldMissions.Long((string)npc.Attribute("id"))] = FieldMissions.Int((string)env.Attribute("friendly"));
            }

        // gamedictionarymanager.cpp:52-128, 245-: <key id value>, value 0 skipped; no availability test in LoadEntry
        HashSet<long> Dict(string file)
        {
            var set = new HashSet<long>();
            string entry = entries.FirstOrDefault(n => string.Equals(n, "table/kr/" + file, StringComparison.OrdinalIgnoreCase))
                        ?? entries.FirstOrDefault(n => string.Equals(n, "table/" + file, StringComparison.OrdinalIgnoreCase));
            if (entry == null) return set;
            foreach (var k in Doc(entry)?.Root?.Elements("key") ?? Enumerable.Empty<XElement>())
                if (FieldMissions.Int((string)k.Attribute("value")) != 0) set.Add(FieldMissions.Long((string)k.Attribute("id")));
            return set;
        }
        dictNpcs = Dict("monsterwebfinder_npc.xml");
        dictMobs = Dict("monsterwebfinder_mob.xml");
        dictItems = Dict("itemwebfinder.xml");

        // fielddata.cpp:573-749: the CFieldData rows (a later duplicate id overrides); worldmapmanager.cpp:392-523: the map tiles
        var searchable = new Dictionary<long, bool>();
        var fd = entries.FirstOrDefault(n => string.Equals(n, "table/fielddata.xml", StringComparison.OrdinalIgnoreCase));
        foreach (var f in (fd == null ? null : Doc(fd))?.Root?.Elements("fieldData") ?? Enumerable.Empty<XElement>())
        {
            var env = Pick(f);
            if (env == null) continue;
            var xblock = env.Element("xblock");
            if (string.IsNullOrEmpty((string)xblock?.Attribute("name"))) continue;              // fielddata.cpp:654-658
            var prop = env.Element("property");
            bool search = true;                                                                  // m_bMapSearch default true (fielddata.cpp:548)
            if (prop != null)
            {
                int group = FieldMissions.Int((string)prop.Attribute("channelGroupID"));
                if (group < 0 || group >= 8) continue;                                           // fielddata.cpp:667
                if (FieldMissions.Tokens((string)prop.Attribute("enteranceBuffIDs"), ",").Length
                    != FieldMissions.Tokens((string)prop.Attribute("enteranceBuffLevels"), ",").Length) continue;   // fielddata.cpp:712
                var ms = (string)prop.Attribute("mapSearch");
                if (ms != null) search = ms.Trim() is not ("0" or "false" or "False" or "");     // fielddata.cpp:697
            }
            searchable[FieldMissions.Long((string)f.Attribute("id"))] = search;
        }
        var tiles = new HashSet<long>();
        string Table(string name) =>
            entries.FirstOrDefault(n => string.Equals(n, "table/kr/" + name, StringComparison.OrdinalIgnoreCase))
            ?? entries.FirstOrDefault(n => string.Equals(n, "table/" + name, StringComparison.OrdinalIgnoreCase));
        var list = Table("worldmap.xml") is { } wm ? Doc(wm) : null;
        foreach (var x in list?.Root?.Elements("xml") ?? Enumerable.Empty<XElement>())
        {
            if (!available(x) || Table(((string)x.Attribute("name") ?? "").ToLowerInvariant()) is not { } file) continue;
            var root = Doc(file)?.Root;
            if (root == null || root.Name != "ms2worldmap") continue;
            var group = Pick(root);
            if (group == null) continue;
            foreach (var m in group.Elements("map").Where(available))
            {
                long code = FieldMissions.Long((string)m.Attribute("code"));
                if (searchable.ContainsKey(code)) tiles.Add(code);                               // CFieldDataCli::Find(code)
            }
        }
        fieldSearch = new();
        foreach (var (id, search) in searchable)
        {
            long mapped = parentField != null && parentField.TryGetValue((int)id, out int p) ? p : id;   // worldmapmanager.cpp:538
            if (search && tiles.Contains(mapped)) fieldSearch.Add(id);                           // worldmapmanager.cpp:544-547
        }
    }
}
