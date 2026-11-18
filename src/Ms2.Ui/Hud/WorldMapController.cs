using System.Globalization;
using Ms2.Avm2;
using Ms2.Quest;
using Ms2.Ui.WorldMap;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIWorldMapDialog (ms2/client/ms2/uiworldmapdialog.cpp, UI id 0x4e42, "WorldMapDialog"), the C++ side of the world map
/// movie uiworldmapdialog: it opens on the world of the field the player is in, asks the server for the icons (packet
/// RequestWorldmap mode 0) and on the answer queues the world's tiles, sorted by distance to the current tile, which go to
/// the movie's createMap five a frame. The radio buttons, the world tabs, a tile click and the search land here.
/// The tile data is WorldMapManager's (<see cref="WorldMapTable"/>).
/// </summary>
public sealed class WorldMapController : UiController
{
    public override string Movie => "uiworldmapdialog";
    protected override string EntityMethod => "initializeIsland";
    public override string TypeName => "WorldMapDialog";

    public WorldMapTable Table;
    public FieldMeta Meta;
    /// <summary>WorldService::GetFieldId.</summary>
    public int FieldId;
    /// <summary>PC::GetJobGroupId (initializeIsland), vfunc_77 the name, vfunc_78 the level.</summary>
    public int JobGroupId, PlayerLevel = 1;
    public string PlayerName = "";
    /// <summary>MyPC GetHomeMapId (0 = no home) and GetHomeAddress.</summary>
    public int HomeField;
    public string HomeAddress = "";
    /// <summary>DAT_143ab3058: the fields the player has been to (the server sends them); the others are sealed.</summary>
    public IEnumerable<uint> VisitedFields = Array.Empty<uint>();
    /// <summary>DAT_143ab3078: the taxi stations the player has found (the server sends them).</summary>
    public ICollection<uint> FoundTaxis = Array.Empty<uint>();
    /// <summary>CQuestManager IsQuestRewarded (a tile with a quest shows once it is rewarded, MapTile::FUN_141b94b90).</summary>
    public Func<uint, bool> QuestRewarded = _ => false;
    /// <summary>DungeonRoomTable: false when the tile's dungeon room is closed today (MapTile::FUN_141b94b90).</summary>
    public Func<uint, bool> DungeonOpenToday = _ => true;
    /// <summary>
    /// SendRequestWorldmapMode0 / Mode1 (packet RequestWorldmap: mode, then the radio mode and the world / the world).
    /// Null = no server: the icon request is answered at once with no icons (<see cref="OnIconPacket"/>) and mode 1
    /// (hot spots) gets no answer.
    /// </summary>
    public Action<int, int, int> SendRequestWorldmap;
    /// <summary>g_143ab2a90.FUN_140a5f7b0(10 / 0xb): the air taxi tickets (setAirTaxiCount).</summary>
    public int AirTaxiTickets, AdvancedAirTaxiTickets;

    // ---- the dialog's state (uiworldmapdialog.h) ----
    int world;                 // m_nWorld (GetSelectedWorld)
    int field;                 // m_nField (FUN_141b69690): the field the map is opened on
    int mode;                  // m_nMode: the radio button (0 default, 1 hot spots, 2 quests, 3 hunting, 4 fishing)
    bool searching;            // m_1c0
    uint selected;             // m_1c4: the searched / clicked field
    uint pending;              // m_1c8 (SetPendingField)
    string pendingSearch = ""; // m_strSearch (SetPendingSearch)
    bool missionMode;          // m_210
    bool missionAttack;        // m_214
    bool skyFortress;          // m_218
    readonly int[] islandIndex = new int[2];   // m_islandIndex
    readonly HashSet<uint> visited = new();    // m_1a0
    readonly LinkedList<WorldMapTable.Tile> queue = new();   // m_tiles
    /// <summary>m_display.m_18[3] / [4]: the setting block's displayProperQuest / displayProperDungeon.</summary>
    public bool DisplayProperQuest, DisplayProperDungeon;

    public int SelectedWorld => world;
    public int Mode => mode;
    public uint SelectedField => selected;
    public int QueuedTiles => queue.Count;

    // ---- strings ----
    string Common(string key) => Model.Tables.Common(key) ?? "";
    static string Fill(string text, params object[] args)
    {
        for (int i = 0; i < args.Length; i++)
            text = text.Replace("{" + i + "}", Convert.ToString(args[i], CultureInfo.InvariantCulture));
        return text;
    }
    string MapName(uint id) => Model.Tables.MapName((int)id) ?? "";   // CStringTable FUN_141811980(3, id)

    /// <summary>OnLoadComplete (:370).</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (Table == null) return;
        // the boss icon tooltip (GetBossIconTooltip :779): no boss icons without the server's icon packet
        Func<object[], object> bossTooltip = _ => null;
        Host.Invoke("setTooltipFunctionBossIcon", bossTooltip);
        Host.Invoke("initializeIsland", (uint)JobGroupId, Host.Obj(("karkarIsland", Model.IsFeatureEnabled(3))));
        for (int i = 0; i < 2; i++) Host.Invoke("setCachedIslandIndexByWorld", (uint)i, (uint)islandIndex[i]);
        field = GetFieldToShow();
        if (Table.FindField((uint)field) is not { } f) return;
        world = Table.WorldOfField(f.Continent);
        SendRequestWorldmapMode0();
        SetFieldSearchInfo((uint)field, missionMode);
        // UpdateFieldMission (:1664): the field mission panel is not ported
        if (missionMode) Select((uint)field);
    }

    /// <summary>vfunc_67 (:298): the island indices are kept for the next open; the queue and the search are dropped.</summary>
    public override void Close()
    {
        for (int i = 0; i < 2; i++) islandIndex[i] = Rt.ToInt32(Host.Invoke("getCachedIslandIndex", (uint)i));
        base.Close();
        queue.Clear();
        searching = false;
        selected = 0;
        pending = 0;
        visited.Clear();
        missionMode = false;
        missionAttack = false;
    }

    /// <summary>GetFieldToShow (:843): the field, else its parent, else the manager's selected field.</summary>
    int GetFieldToShow()
    {
        int f = FieldId;
        if (Table.FindTile((uint)f) != null) return f;
        if (Table.ParentField(f) is int p) return p;
        uint def = Table.SelectedField;
        if (def > 0) return Table.FindTile(def) != null ? (int)def : f;
        return f;
    }

    /// <summary>SendRequestWorldmapMode0 (:1140); offline the answer comes at once.</summary>
    void SendRequestWorldmapMode0()
    {
        if (SendRequestWorldmap != null) SendRequestWorldmap(0, mode, world);
        else OnIconPacket();
    }

    /// <summary>
    /// OnIconPacket (:884), packet 0x84 mode 0: the icons (LoadIconPacket), the visited fields, then the tiles. The quest,
    /// npc quest, dungeon, party and buddy icons are not ported.
    /// </summary>
    public void OnIconPacket()
    {
        if (Host?.Entity == null || IsClosed) return;
        foreach (uint id in VisitedFields) visited.Add(id);     // FUN_141b58f00
        SelectPendingField();
        ApplyPendingSearch();
        BuildWorldTiles();
    }

    void SelectPendingField()
    {
        if (pending == 0) return;
        SelectFieldByName(MapName(pending));
        SetFieldSearchInfo(pending, false);
        pending = 0;
    }

    void ApplyPendingSearch()
    {
        if (pendingSearch.Length == 0) return;
        if (Host.Child("m_fieldSearch") is { } input) Host.Set(input, "text", pendingSearch);
        NameSearch(pendingSearch);
        pendingSearch = "";
    }

    /// <summary>SetPendingField (:1630): select a field once the dialog is open.</summary>
    public void SetPendingField(uint id)
    {
        pending = id;
        if (!IsClosed) SelectPendingField();
    }

    /// <summary>SetPendingSearch (:1639).</summary>
    public void SetPendingSearch(string text)
    {
        pendingSearch = text ?? "";
        if (!IsClosed) ApplyPendingSearch();
    }

    /// <summary>GetCurrentTile (:1815): the searched tile in this world, else the field's, else the world's default.</summary>
    WorldMapTable.Tile GetCurrentTile()
    {
        var tile = Table.FindTile((uint)field);
        if (searching)
        {
            var s = Table.FindTile(selected);
            if (s == null) return tile;
            if (s.World == world) return s;
        }
        if (Table.FindField((uint)field) is { } f && (Table.WorldOfField(f.Continent) != world || tile == null))
            tile = Table.DefaultTile(world);
        return tile;
    }

    /// <summary>
    /// BuildWorldTiles (:939): the selected world's tiles by type, then by distance to the current tile (FUN_141b62110),
    /// queued for createMap; the movie is cleared and told the world, the tile count, the radio button and the grid bounds.
    /// </summary>
    void BuildWorldTiles()
    {
        var cur = GetCurrentTile();
        if (cur == null) return;
        queue.Clear();
        var groups = Table.Worlds.Where(kv => kv.Key == world).Select(kv => kv.Value).ToList();
        int count = groups.Sum(g => g.Count);
        Host.Invoke("clearMapTile");
        Host.Invoke("selectWorld", world);
        Host.Invoke("makeDisplayTile", count);
        Host.Invoke("setInitalRadioBtn", mode);
        Host.Invoke("setVisibleRadioGroup", 0);
        Host.Invoke("setAirTaxiCount", AirTaxiTickets, AdvancedAirTaxiTickets);
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var g in groups)
        {
            var sorted = new List<WorldMapTable.Tile>();
            foreach (var t in g)
            {
                float dx = t.X - cur.X, dy = t.Y - cur.Y, dz = t.Z - cur.Z;
                t.Distance = (uint)MathF.Sqrt(dx * dx + dy * dy + dz * dz);     // GetDistance
                sorted.Add(t);
                maxX = Math.Max(maxX, t.X); minX = Math.Min(minX, t.X);
                maxY = Math.Max(maxY, t.Y); minY = Math.Min(minY, t.Y);
            }
            WorldMapTable.Sort(sorted, (a, b) => a.TileType == b.TileType ? a.Distance < b.Distance : a.TileType < b.TileType);
            foreach (var t in sorted) queue.AddLast(t);
        }
        Host.Invoke("precalculateSize", minX, maxX, minY, maxY);
        Host.Invoke("setInitalPos", cur.X, cur.Y, cur.Z, cur.Size);
    }

    /// <summary>vfunc_72 (:320) every frame: FlushQueuedTiles (:1544), five tiles a frame, then the radio group and onTileComplete.</summary>
    public void Process()
    {
        if (Host?.Entity == null || IsClosed || queue.Count == 0) return;
        for (int i = 0; queue.Count > 0 && i < 5; i++)
        {
            var t = queue.First.Value;
            queue.RemoveFirst();
            CreateMapTile(t);
            UpdateAdditionalInfo(t);
        }
        if (queue.Count == 0)
        {
            Host.Invoke("setVisibleRadioGroup", 1);
            Host.Invoke("onTileComplete");
        }
    }

    /// <summary>MapTile::FUN_141b94b90 (wireframemanager.cpp:117): a dungeon tile closed today or a quest tile not yet rewarded is not shown.</summary>
    bool TileShown(WorldMapTable.Tile t) =>
        (t.Dungeon == 0 || DungeonOpenToday(t.Dungeon)) && (t.Quest == 0 || QuestRewarded(t.Quest));

    /// <summary>CreateMapTile (:1597): the tile's object for createMap.</summary>
    void CreateMapTile(WorldMapTable.Tile t)
    {
        if (!TileShown(t)) return;
        var o = new Dictionary<string, object>();
        TileMembers(t, o);
        // the icon parts (FUN_141b5e160 boss, e330 portals, e580 npc quests, e9e0 party, ec30 buddies): no icons offline
        o["portal"] = Array.Empty<object>(); o["portaltimes"] = Array.Empty<object>();
        o["quest"] = Array.Empty<object>(); o["questType"] = Array.Empty<object>(); o["questLevel"] = Array.Empty<object>();
        o["party"] = Array.Empty<object>(); o["partyChannel"] = Array.Empty<object>();
        o["buddy"] = Array.Empty<object>(); o["buddyChannel"] = Array.Empty<object>();
        TaxiMembers(t, o);
        SpecialNpcMembers(t, o);
        // FUN_141b5f740 (field mission), SetEventScript and SetDungeonScript are not ported
        Host.Invoke("createMap", o);
    }

    /// <summary>FUN_141b5d4b0 (:1869): the tile's own members.</summary>
    void TileMembers(WorldMapTable.Tile t, Dictionary<string, object> o)
    {
        var f = Table.FindField(t.FieldId);
        if (f == null) return;
        // the shadow gate (m_54) is not read from the npcs: none
        o["shadowgate"] = "";
        o["mypc"] = field == t.FieldId && world == t.World ? Fill(Common("s_worldmap_tooltip_myname"), PlayerName) : "";   // 0x70e
        string house = Fill(Common("s_worldmap_tooltip_myhome"), HomeAddress);    // 0x70f
        o["myhouse"] = HomeField != 0 && ParentFieldId(HomeField) == t.FieldId ? house : "";
        o["index"] = t.Index;
        o["town"] = t.Town;
        o["publicField"] = t.Public;
        o["fieldName"] = MapName(t.FieldId);
        o["pvpField"] = f.PkMode;
        bool showName = !t.Town;
        // CQuestManager FUN_1416d0800 (a quest's name movie for the field) is not ported
        if (t.NameMovie.Length != 0)
        {
            o["nameMovie"] = t.NameMovie;
            o["nameAlign"] = t.NameAlign;
            o["nameAlignOffsetX"] = t.NameOffsetX;
            o["nameAlignOffsetY"] = t.NameOffsetY;
            showName = false;
        }
        o["showFieldName"] = showName;
        o["fieldTooltip"] = TileTooltip(t, f);
        o["fieldSN"] = t.FieldId;
        o["continent"] = t.World;
        o["gridX"] = t.X; o["gridY"] = t.Y; o["gridZ"] = t.Z;
        o["size"] = t.Size;
        o["tileType"] = t.TileType;
        o["normalImage"] = t.NormalImage;
        o["sealedImage"] = t.SealedImage;
        o["usingShadow"] = t.World == 105;     // WorldMapShadowUsingContinent (constantsblocks.cpp:1566, not in constants.xml)
        o["sealed"] = !visited.Contains(t.FieldId);
        if (t.TileType == 1) o["tracing"] = skyFortress ? 1 : 0;
    }

    int ParentFieldId(int id) => Table.ParentField(id) ?? id;    // GetParentFieldId (:2257)

    /// <summary>GetTileTooltip (:1566): name and level (red-free above the player's level + 5), the fishing spot, the hint.</summary>
    string TileTooltip(WorldMapTable.Tile t, WorldMapTable.Field f)
    {
        string level = Fill(Common("s_worldmap_tooltip_level"), f.MapLevel);       // 0x713
        string text = Fill(Common(PlayerLevel + 5 < f.MapLevel ? "s_worldmap_tooltip_mapname_high" : "s_worldmap_tooltip_mapname_low"),
            MapName(t.FieldId), level);                                          // 0x717 / 0x716
        // CFishingManager GetSpotDescription is not ported
        string hint = Common("s_worldmap_tooltip_dbclick");
        if (hint.Length > 0) text += "\n" + hint;
        return text;
    }

    /// <summary>
    /// FUN_141b5ee80 (:2080): a taxi station's icon state: found ones "enabled" ("default" here, "active" elsewhere while
    /// m_194 is set), the others "disabled". The fare (CTaxiManager GetTaxiFare) is not ported: no fare line.
    /// </summary>
    void TaxiMembers(WorldMapTable.Tile t, Dictionary<string, object> o)
    {
        if (Meta != null && Meta.TaxiStations.TryGetValue((int)t.FieldId, out bool homePrivilege) && !homePrivilege)
        {
            string state = "disabled";
            bool here = false;
            if (FoundTaxis.Contains(t.FieldId))
            {
                here = t.FieldId == field;
                state = "enabled";
            }
            string desc = here ? Common("s_worldmap_tooltip_taxi_current")                          // 0x727
                : state == "disabled" ? Common("s_worldmap_tooltip_taxi_seal")                       // 0x726
                : Common("s_worldmap_tooltip_taxi_other_continent");                                 // 0x728
            // FUN_141b71c00: "goodtaxi" from TaxiCallBestDriverLevel (not in the tables) up
            o[PlayerLevel >= TaxiCallBestDriverLevel ? "goodtaxi" : "taxi"] = state;
            o["taxicost"] = "";
            o["taxidesc"] = desc;
            o["taxiTitle"] = Fill(Common("s_worldmap_tooltip_taxi_title"), MapName(t.FieldId));   // 0x724
        }
        else
        {
            o["taxi"] = ""; o["goodtaxi"] = ""; o["taxicost"] = "";
        }
    }

    /// <summary>Client::Constants TaxiCallBestDriverLevel (+0x2b28).</summary>
    public int TaxiCallBestDriverLevel = int.MaxValue;

    /// <summary>WorldmapSpecialFunctionNpcID / Frame / Tooltip (the ctor :265, client constants).</summary>
    public List<(int Npc, string Frame, string Tooltip)> SpecialNpcs = new();

    /// <summary>FUN_141b5f540 (:2136): the first special npc standing in the field gives its frame and tooltip.</summary>
    void SpecialNpcMembers(WorldMapTable.Tile t, Dictionary<string, object> o)
    {
        if (Meta == null) return;
        foreach (var (npc, frame, tip) in SpecialNpcs)
            if (Meta.NpcFields.TryGetValue(npc, out var fs) && fs.Contains((int)t.FieldId))
            {
                o["specialFunctionNpc"] = frame;
                o["specialFunctionNpcTooltip"] = Common(tip);
                return;
            }
    }

    /// <summary>UpdateAdditionalInfo (:1029): the radio mode's part of a tile (updateAdditional).</summary>
    void UpdateAdditionalInfo(WorldMapTable.Tile t)
    {
        var o = new Dictionary<string, object> { ["index"] = t.Index, ["fieldSN"] = t.FieldId };
        switch (mode)
        {
            case 3:     // FUN_141b60000: a hunting ground within 3 levels
                if (Table.FindField(t.FieldId) is { MapType: 4 or 0xb } f && Math.Abs(f.MapLevel - PlayerLevel) <= 3) o["recommendMap"] = 1;
                break;
            case 2:     // FUN_141b5fb80: the quest lines (AddQuestIcons is not ported: none)
                if (world == t.World) { o["quest"] = Array.Empty<object>(); o["questType"] = Array.Empty<object>(); o["questLevel"] = Array.Empty<object>(); }
                break;
            // 1 (ranking) comes from the server; 4 (fishing, FUN_141b60170) needs the fishing table: not ported
        }
        if (selected == t.FieldId) o["fieldSearchID"] = selected;
        Host.Invoke("updateAdditional", o);
    }

    /// <summary>UpdateAdditionalInfos (:1010): every tile of the world, once the queue is empty.</summary>
    void UpdateAdditionalInfos()
    {
        if (queue.Count != 0) return;
        foreach (var t in Table.WorldTiles(world)) UpdateAdditionalInfo(t);
    }

    /// <summary>SendRequestWorldmapMode1 (:1840): the radio mode's data.</summary>
    void SendRequestWorldmapMode1()
    {
        if (queue.Count != 0) return;
        switch (mode)
        {
            case 1: SendRequestWorldmap?.Invoke(1, 0, world); break;
            case 2: UpdateAdditionalInfos(); break;    // ClearAddtionalInfos + AddQuestIcons (not ported)
            case 0: case 3: case 4: UpdateAdditionalInfos(); break;
        }
    }

    /// <summary>FUN_141b61ff0 (:2506): the selected field; the old and new tiles are redrawn.</summary>
    void Select(uint key)
    {
        uint old = selected;
        selected = key;
        if (Table.FindTile(old) is { } o) UpdateAdditionalInfo(o);
        if (Table.FindTile(key) is { } n) UpdateAdditionalInfo(n);
    }

    /// <summary>SetFieldSearchInfo (:2359): m_searchInfo.setData(BuildFieldSearch).</summary>
    void SetFieldSearchInfo(uint id, bool mission)
    {
        if (Host.Child("m_searchInfo") is not { } info) return;
        // BuildMissionSearch (worldmapmanager.cpp:1403) is not ported; BuildFieldSearch returns 1 either way
        var o = mission ? new Dictionary<string, object>() : FieldDetails(id);
        if (o != null) Host.Invoke(info, "setData", o);
    }

    /// <summary>
    /// BuildFieldDetails (worldmapmanager.cpp:1248): the title (name and level), the description and the connected maps.
    /// The dungeons (DungeonRoomTable), the npcs and monsters of the connected fields (NpcDataTableCli) and the fishing
    /// spot are not ported: empty lists.
    /// </summary>
    Dictionary<string, object> FieldDetails(uint id)
    {
        if (Table.FindField(id) is not { } f) return null;
        var indoors = new List<object>();
        foreach (int c in Meta?.ConnectedFields((int)id) ?? new List<int> { (int)id })
        {
            if (!Table.Search.ContainsKey((uint)c) || c == id) continue;
            indoors.Add(new Dictionary<string, object> { ["text"] = MapName((uint)c) });
        }
        return new Dictionary<string, object>
        {
            ["fieldSN"] = id,
            ["title"] = Fill(Common("s_worldmap_search_map_title"), MapName(id), f.MapLevel),
            ["fishingSpot"] = "",
            ["mapDesc"] = Model.Tables.MapTileDesc((int)id) ?? "",
            ["dungeon"] = Array.Empty<object>(), ["monster"] = Array.Empty<object>(), ["bossMonster"] = Array.Empty<object>(),
            ["eliteMonster"] = Array.Empty<object>(), ["shadowgate"] = Array.Empty<object>(), ["indoor"] = indoors,
            ["npc"] = Array.Empty<object>(),
            ["cashtaxi"] = !f.IsIndoor && f.CashTaxiDestinationAllowed,
        };
    }

    /// <summary>
    /// BuildNameSearch (worldmapmanager.cpp:955): the search fields whose name holds the text (case-insensitive,
    /// FUN_14083fec0): outdoor ones as maps (a cash taxi icon where it may land), indoor ones as connected maps. The
    /// dungeon, npc and monster lists are not ported.
    /// </summary>
    Dictionary<string, object> NameSearch(string text)
    {
        var maps = new List<object>();
        var indoors = new List<object>();
        foreach (var info in Table.Search.Values)
        {
            if (info.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (info.MappedId != info.FieldId)
                indoors.Add(new Dictionary<string, object> { ["text"] = info.Name, ["id"] = info.MappedId });
            else
            {
                var item = new Dictionary<string, object> { ["text"] = info.Name, ["id"] = info.FieldId, ["iconData"] = info.FieldId };
                if (Table.FindField(info.FieldId) is { } f && !f.IsIndoor && f.CashTaxiDestinationAllowed) item["icon"] = "cashTaxi";
                maps.Add(item);
            }
        }
        var o = new Dictionary<string, object>
        {
            ["map"] = maps, ["dungeon"] = Array.Empty<object>(), ["monster"] = Array.Empty<object>(),
            ["eliteMonster"] = Array.Empty<object>(), ["bossMonster"] = Array.Empty<object>(), ["shadowgate"] = Array.Empty<object>(),
            ["indoor"] = indoors, ["npc"] = Array.Empty<object>(),
        };
        if (maps.Count == 0 && indoors.Count == 0) o["title"] = Common("s_err_worldmap_search_none");
        if (Host.Child("m_searchInfo") is { } panel) Host.Invoke(panel, "setData", o);   // FUN_141b60d40 (:2371)
        return o;
    }

    /// <summary>SearchFieldByName (:2287): the platform's text check (IClientPlatform vfunc_37) passes offline.</summary>
    void SearchFieldByName(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        NameSearch(text);
    }

    /// <summary>
    /// SelectFieldByName (:2299): the named field's tile is brought up (another world is switched to); no search entry is
    /// message 0xa6, a field without a tile 0xa7.
    /// </summary>
    void SelectFieldByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        var info = Table.FindSearch(name);
        if (info == null) { MessageBoxQueue.ShowMessageBox(Common("s_err_worldmap_search_nothing_field"), 0, null, "onTopMostLayer"); return; }
        var tile = Table.FindTile(info.MappedId);
        if (tile == null) { MessageBoxQueue.ShowMessageBox(Common("s_err_worldmap_search_invisible_field"), 0, null, "onTopMostLayer"); return; }
        ApplySearchedField();
        selected = info.MappedId;
        searching = true;
        if (tile.TileType == 0)
        {
            if (tile.World != world) { world = tile.World; SendRequestWorldmapMode0(); }
            else
            {
                UpdateAdditionalInfo(tile);
                Host.Invoke("setInitalPos", tile.X, tile.Y, tile.Z, tile.Size);
            }
        }
        else if (Host.Child("m_skyFortressMode") is { } sky && Host.Child("m_toggle", sky) is { } toggle)
        {
            Host.Set(toggle, "selected", true);
            skyFortress = true;
        }
    }

    /// <summary>ApplySearchedField (:2270): a pending search's tile is redrawn and centred.</summary>
    void ApplySearchedField()
    {
        if (!searching || Table.FindTile(selected) is not { } tile) return;
        searching = false;
        selected = 0;
        UpdateAdditionalInfo(tile);
        Host.Invoke("setInitalPos", tile.X, tile.Y, tile.Z, tile.Size);
    }

    /// <summary>OnDisplaySettingsChanged (:1648): the icons are rebuilt (none here) and the world again.</summary>
    void OnDisplaySettingsChanged() => BuildWorldTiles();

    /// <summary>
    /// vfunc_13 (:328), Enter: in the focused search box processEnterKey runs the search; else the box takes the focus.
    /// True = taken.
    /// </summary>
    public bool EnterKey()
    {
        if (Host?.Entity == null || IsClosed) return false;
        if (Host.Child("m_fieldSearch") is not { } input) return true;
        if (Rt.ToBoolean(Host.Get(input, "focused")))
        {
            if (Rt.ToBoolean(Host.Invoke(input, "processEnterKey")))
            {
                Host.Set(input, "focused", false);
                SearchFieldByName(Rt.ToString(Host.Get(input, "text")));
            }
        }
        else Host.Set(input, "focused", true);
        return true;
    }

    static int Int(object[] a, int i) => i < a.Length ? Rt.ToInt32(a[i]) : 0;
    static uint UInt(object[] a, int i) => i < a.Length ? Rt.ToUint32(a[i]) : 0;
    static bool Bool(object[] a, int i) => i < a.Length && Rt.ToBoolean(a[i]);
    static string Str(object[] a, int i) => i < a.Length ? Rt.ToString(a[i]) : "";

    /// <summary>vfunc_28 (:482).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (Table == null) return false;
        switch (name.ToLowerInvariant())
        {
            case "onclose": Close(); return true;
            case "onradiobtn": mode = Int(args, 0); SendRequestWorldmapMode1(); return true;
            case "onchangeworld": world = Int(args, 0); SendRequestWorldmapMode0(); return true;
            case "onselected": SearchFieldByName(Str(args, 0)); return true;
            case "onsetting":
                DisplayProperQuest = Bool(args, 0);
                DisplayProperDungeon = Bool(args, 1);
                OnDisplaySettingsChanged();
                return true;
            case "onmouseclicktile":
            {
                uint key = UInt(args, 0);
                SetFieldSearchInfo(key, Bool(args, 1));
                Select(key);
                if (Host.Child("m_fieldSearch") is { } input) Host.Set(input, "text", "");
                // the mission attack prompt, the dungeon room dialog (0x4ebd) and Ctrl's /MoveField are not here
                return true;
            }
            case "onclicksearchinfo":
            {
                uint key = UInt(args, 0);
                Select(key);
                SelectFieldByName(MapName(key));
                return true;
            }
            case "oncllckfieldmission":
                SetFieldSearchInfo(UInt(args, 0), Bool(args, 1));
                missionMode = Bool(args, 1);
                return true;
            case "onclickskyfortressmode": skyFortress = Bool(args, 0); return true;
            // a taxi, the cash taxi, a portal move, the boss / dungeon dialogs and the mission rewards move the player or
            // spend: not here
            case "ontaxi": case "oncashtaxi": case "onportal": case "onboss": case "ondungeon": case "onclicksearchinfoicon":
            case "onclickmissionreward": case "onclickmissionrewardprev": case "onclickmissionrewardnext":
                return true;
        }
        return false;
    }
}
