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
public sealed class WorldMapController : UiController, IMessageBoxListener
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
    /// <summary>QuestDataTable's field mission quests (questType 4): their number is the exploration total.</summary>
    public FieldMissions Missions;
    /// <summary>CFieldMissionTable: the exploration rewards by round.</summary>
    public FieldMissionTable MissionRewards;
    /// <summary>
    /// CQuestManager m_group: the last exploration round rewarded (the server's quest packet), 0 for none; and
    /// CountCompletedQuests80, the field missions completed (state 2 in m_quests80). Both 0 without the server.
    /// </summary>
    public int MissionGroup, CompletedMissions;

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
        UpdateFieldMission();
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
        TaxiMode = 0;                 // m_194 = 0; vfunc_47(true)
    }

    /// <summary>m_194: set by <see cref="SetTaxiMode"/>; a found station's tile then shows "default" here and "active"
    /// elsewhere.</summary>
    public int TaxiMode { get; private set; }

    /// <summary>
    /// SetTaxiMode (@0x141b5af80, uiworldmapdialog.cpp:1502): m_194 = n, then vfunc_47(m_194 == 0). Tiles built after the
    /// call read it.
    /// STILL-OPEN: vfunc_47 (FUN_141901420 with vfunc_48, the s_keyInput layer release FUN_141901070 / FUN_141900f70) is not
    /// ported.
    /// </summary>
    public void SetTaxiMode(int n) => TaxiMode = n;

    // ---- the exploration panel (m_fieldMission) ----

    /// <summary>CQuestManager::GetNextRound (questmanager_2.cpp:1571): the round after the last one rewarded.</summary>
    int NextRound => MissionRewards?.GetNextRound(MissionGroup) ?? 0;

    /// <summary>
    /// UpdateFieldMission (:1664): the panel's data when there are field mission quests: the next round (the last round
    /// once it is rewarded, IsLastRound, then the claim is off), the completed share as string 0x98e (built into the client:
    /// the number with a % sign), done / total, the round's goal (s_fieldmission_desc), whether there is a round before and
    /// after, whether the claim is on (IsRoundTargetReached: done &gt;= the round) and the reward.
    /// </summary>
    void UpdateFieldMission()
    {
        var o = FieldMissionData();
        if (o == null) return;
        if (Host.Child("m_fieldMission") is { } panel) Host.Invoke(panel, "setData", o);
        if (missionMode) Host.Invoke("setFieldMissionMode", 1);
    }

    /// <summary>UpdateFieldMission's object for m_fieldMission.setData; null when there are no field missions.</summary>
    public Dictionary<string, object> FieldMissionData()
    {
        int total = Missions?.QuestCount ?? 0;
        if (total == 0 || MissionRewards == null) return null;
        int round = NextRound;
        bool enable = CompletedMissions >= round;
        if (MissionGroup == MissionRewards.GetMaxRound())
        {
            round = MissionRewards.GetMaxRound();
            enable = false;
        }
        var o = new Dictionary<string, object>
        {
            ["rewardCount"] = round,
            ["rate"] = (CompletedMissions * 100 / total).ToString(CultureInfo.InvariantCulture) + "%",
            ["count"] = $"{CompletedMissions}/{total}",
            ["desc"] = Fill(Common("s_fieldmission_desc"), round),
            ["prev"] = MissionRewards.GetPrevRound(round) != 0,
            ["next"] = MissionRewards.GetNextRound(round) != 0,
            ["enable"] = enable ? 1 : 0,
        };
        SetFieldMissionReward(round, o);
        return o;
    }

    /// <summary>UpdateFieldMissionRound (:1796): another round's goal and reward; the claim only on the next round.</summary>
    void UpdateFieldMissionRound(int round)
    {
        bool enable = NextRound == round && CompletedMissions >= NextRound;
        var o = new Dictionary<string, object>
        {
            ["rewardCount"] = round,
            ["enable"] = enable ? 1 : 0,
            ["desc"] = Fill(Common("s_fieldmission_desc"), round),
            ["prev"] = MissionRewards.GetPrevRound(round) != 0,
            ["next"] = MissionRewards.GetNextRound(round) != 0,
        };
        SetFieldMissionReward(round, o);
        if (Host.Child("m_fieldMission") is { } panel) Host.Invoke(panel, "setData", o);
    }

    /// <summary>
    /// SetFieldMissionReward (:1707): an item reward is a new item of the reward's id and rank (FUN_140523c40) with its
    /// count, bound as the dialog's type name (CreateItemInfoSlot with no index, icon type or flags), and the text
    /// s_fieldmission_reward. STILL-OPEN: the stat point (type 2) and skill point (type 3) rewards bind a slot-text slot
    /// (FUN_141415560: CommonResource m_1d0 or GetPath(subJob), the tip s_fieldmission_stat_reward /
    /// s_fieldmission_skill_reward, the value as the count), which ItemInfoSlot does not build; only their text
    /// (s_fieldmission_point_reward) is set.
    /// </summary>
    void SetFieldMissionReward(int round, Dictionary<string, object> o)
    {
        var reward = MissionRewards.GetReward(round);
        if (reward == null) return;
        if (reward.Type == 1)
        {
            var item = Items.Item.Create(Model.Tables.Items, reward.ItemId, reward.ItemRank);
            if (item == null) return;
            item.SetCountClamped(reward.ItemCount);
            Model.ItemSlots.Remove(TypeName);                                               // DestroyItemInfoSlot
            Model.ItemSlots[TypeName] = new Items.ItemInfoSlot(TypeName, item, 0, 0, 0);     // CreateItemInfoSlot
            o["binding"] = TypeName;
            o["reward"] = Common("s_fieldmission_reward");
        }
        else if (reward.Type is 2 or 3)
            o["reward"] = Common("s_fieldmission_point_reward");
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
    /// npc quest, party and buddy icons are not ported.
    /// </summary>
    public void OnIconPacket()
    {
        if (Host?.Entity == null || IsClosed) return;
        dungeonIcons.Clear();                                   // LoadIconPacket: ClearIconInfos
        foreach (uint id in VisitedFields) visited.Add(id);     // FUN_141b58f00
        AddDungeonIcons();
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
        // FUN_141b5f740 (field mission) and SetEventScript are not ported
        SetDungeonScript(t.FieldId, o);
        Host.Invoke("createMap", o);
    }

    /// <summary>
    /// The dungeon part of the manager's icon infos (IconInfo m_48: m_18 the dungeon, m_1c isUseBossIcon, m_1d
    /// isTimeUp == 1) by field. LoadIconPacket's ClearIconInfos and ClearDungeonInfos empty it.
    /// </summary>
    readonly Dictionary<uint, (int Dungeon, bool Boss, bool TimeUp)> dungeonIcons = new();

    /// <summary>
    /// AddDungeonIcons (:1336): for each field with dungeon (portalType 8) portals and no dungeon yet, each of its
    /// dungeons whose first room passes the display filter sets the field's dungeon (the last one wins).
    /// </summary>
    void AddDungeonIcons()
    {
        if (Rooms == null || Meta == null) return;
        foreach (int field in FieldMeta.AtlMapOrder(Meta.PortalType8.Keys))
        {
            if (dungeonIcons.ContainsKey((uint)field)) continue;
            foreach (int dungeon in Meta.PortalType8[field])
            {
                var rooms = Rooms.GetRooms(dungeon);
                if (rooms.Count == 0 || !DungeonShown(rooms[0])) continue;
                dungeonIcons[(uint)field] = (dungeon, rooms[0].IsUseBossIcon, rooms[0].IsTimeUp == 1);
            }
        }
    }

    /// <summary>
    /// DisplaySetting::FUN_141b95270 (worldmapmanager.cpp:351): with "display proper dungeons" on (m_18[4]) only a room
    /// whose limitPlayerLevel suits the player's level.
    /// </summary>
    bool DungeonShown(DungeonRoom room) => !DisplayProperDungeon || LevelInRange(room.LimitPlayerLevel, PlayerLevel);

    /// <summary>IsDungeonLevelInRange (dungeonroom.cpp:751), the arguments as FUN_141b95270 passes them.</summary>
    static bool LevelInRange(int level, int dungeonLevel) =>
        Math.Abs((ushort)level - (ushort)dungeonLevel) <= 5 || dungeonLevel >= 50 && level >= 50 && level <= dungeonLevel;

    /// <summary>WorldMapManager::SetDungeonScript (worldmapmanager.cpp:1169): portalID and dungeonType.</summary>
    void SetDungeonScript(uint field, Dictionary<string, object> o)
    {
        if (!dungeonIcons.TryGetValue(field, out var d) || d.Dungeon == 0) return;
        o["portalID"] = d.Dungeon;
        o["dungeonType"] = d.Boss ? (d.TimeUp ? "boss_special" : "boss") : (d.TimeUp ? "dungeon_special" : "dungeon");
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

    /// <summary>CFishingManager::GetSpotDescription (fishingmanager.cpp:1102) with the player's fishing mastery (GetMastery(1)).</summary>
    string FishingSpot(uint field) => Model.Tables.Fishing.Description((int)field, Model.Player.FishingMastery, Common);

    /// <summary>GetTileTooltip (:1566): name and level (red-free above the player's level + 5), the fishing spot, the hint.</summary>
    string TileTooltip(WorldMapTable.Tile t, WorldMapTable.Field f)
    {
        string level = Fill(Common("s_worldmap_tooltip_level"), f.MapLevel);       // 0x713
        string text = Fill(Common(PlayerLevel + 5 < f.MapLevel ? "s_worldmap_tooltip_mapname_high" : "s_worldmap_tooltip_mapname_low"),
            MapName(t.FieldId), level);                                          // 0x717 / 0x716
        string fishing = FishingSpot(t.FieldId);
        if (fishing.Length > 0) text += "\n" + fishing;
        string hint = Common("s_worldmap_tooltip_dbclick");
        if (hint.Length > 0) text += "\n" + hint;
        return text;
    }

    /// <summary>
    /// FUN_141b5ee80 (:2080): a taxi station's icon state: found ones "enabled" ("default" here, "active" elsewhere while
    /// m_194, <see cref="TaxiMode"/>, is set), the others "disabled". The fare line is CTaxiManager::GetTaxiFare (<see cref="TaxiManager"/>).
    /// </summary>
    internal void TaxiMembers(WorldMapTable.Tile t, Dictionary<string, object> o)
    {
        if (Meta != null && Meta.TaxiStations.TryGetValue((int)t.FieldId, out bool homePrivilege) && !homePrivilege)
        {
            string state = "disabled";
            bool here = false;
            if (FoundTaxis.Contains(t.FieldId))
            {
                here = t.FieldId == field;
                state = TaxiMode != 0 ? (here ? "default" : "active") : "enabled";
            }
            // nFare (GetTaxiFare from the field the map is opened on, only for a found station): above 0 the cost line
            // (0x725 with the grouped fare, FUN_1409404f0) and no description; otherwise the description (:2108-2117)
            long fare = FoundTaxis.Contains(t.FieldId) ? Taxi.GetTaxiFare(field, (int)t.FieldId, (short)PlayerLevel) : 0;
            string cost = "", desc = "";
            if (fare > 0) cost = Fill(Model.Tables.Common(0x725), fare.ToString("N0", CultureInfo.InvariantCulture));
            else desc = here ? Common("s_worldmap_tooltip_taxi_current")                            // 0x727
                : state == "disabled" ? Common("s_worldmap_tooltip_taxi_seal")                       // 0x726
                : Common("s_worldmap_tooltip_taxi_other_continent");                                 // 0x728
            // FUN_141b71c00: "goodtaxi" from TaxiCallBestDriverLevel (not in the tables) up
            o[PlayerLevel >= TaxiCallBestDriverLevel ? "goodtaxi" : "taxi"] = state;
            o["taxicost"] = cost;
            o["taxidesc"] = desc;
            o["taxiTitle"] = Fill(Common("s_worldmap_tooltip_taxi_title"), MapName(t.FieldId));   // 0x724
        }
        else
        {
            o["taxi"] = ""; o["goodtaxi"] = ""; o["taxicost"] = "";
        }
    }


    // ---- the taxi (uiworldmapdialog.cpp OnTaxi :516, OnMessage :755, TakeTaxi @0x141b5a550) ----
    TaxiManager taxi; FieldMeta taxiMeta;
    /// <summary>TSingleton&lt;CTaxiManager&gt; over the field's portal graph.</summary>
    internal TaxiManager Taxi => taxi != null && ReferenceEquals(taxiMeta, Meta) ? taxi : (taxi = new TaxiManager(taxiMeta = Meta));
    uint taxiTarget;             // m_190
    /// <summary>GetLocalPlayer != null (TakeTaxi :1371). False = no player: nothing runs.</summary>
    public Func<bool> HasPlayer = () => false;
    /// <summary>IsDead.</summary>
    public Func<bool> PlayerDead = () => false;
    /// <summary>GetCurStateId.</summary>
    public Func<int> PlayerStateId = () => 0;
    /// <summary>FUN_141527160(state): whether the player can enter the state (OnTaxi :536, TakeTaxi).</summary>
    public Func<int, bool> CanEnterState = _ => false;
    /// <summary>WorldService::FUN_141bcfd30(0x85359406, position, TaxiStationFindDistance, 1, 0) != 0.</summary>
    public Func<bool> StationNear = () => false;
    /// <summary>DAT_143ab2898, the mesos held.</summary>
    public Func<long> Mesos = () => 0;
    /// <summary>IsRiding.</summary>
    public Func<bool> PlayerRiding = () => false;
    /// <summary>RideOffActionTaxi(0) with m_n28 = field, SendRequestRideMode1. STILL-OPEN: no port; null does nothing.</summary>
    public Action<int> RideOffForTaxi;
    /// <summary>Turn toward the station (yaw + 180), then vfunc_59(0x25, StateTaxiCallData(field)).</summary>
    public Action<int> StartTaxiCall;
    const int StateTaxiCall = 0x25, StateTaxiBoard = 0x2a;

    /// <summary>CFieldData::FUN_140374410 (fielddata.cpp:637): m_nMapType == 3.</summary>
    bool FieldMapType3(int id) => Table.FindField((uint)id) is { MapType: 3 };

    /// <summary>FUN_14151f150, how a box is shown (a seam for tests).</summary>
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;
    /// <summary>The notice FUN_140d73bc0(id, 0, 0, ""): common string <paramref name="id"/>.</summary>
    void Notice(int id) => ShowBox(MessageBoxQueue.Create(0, MessageBoxData.Common(id), null, ""));

    /// <summary>
    /// OnTaxi (:516-545). Nothing while m_214 (the mission attack) is set; the name is read, m_190 = the field. Same field:
    /// notice 0x8a; a fare of 0 or less (another continent, no route): the s_err_taxi_continent box; the player cannot enter
    /// StateTaxiCall (0x25): notice 0x93; else the yes / no box (0x8b: the map name and the fare) with the tag "onTaxi".
    /// A missing player returns before the call (the callback's own player check, :373).
    /// </summary>
    void OnTaxi(object[] args)
    {
        if (missionAttack || args.Length < 2 || !HasPlayer()) return;
        taxiTarget = UInt(args, 1);          // args: the name (FUN_1422b8f40), then the field (PopUInt)
        long fare = Taxi.GetTaxiFare(field, (int)taxiTarget, (short)PlayerLevel);
        if (field == taxiTarget) { Notice(0x8a); return; }
        if (fare <= 0) { ShowBox(MessageBoxQueue.Create(0, Common("s_err_taxi_continent"), null, "")); return; }
        if (!CanEnterState(StateTaxiCall)) { Notice(0x93); return; }
        ShowBox(MessageBoxQueue.Create(1, Fill(Model.Tables.Common(0x8b), MapName(taxiTarget), fare), this, "onTaxi"));
    }

    /// <summary>
    /// OnMessage (:755-775): a box's answer is taken from the queue first (FUN_14151f200); yes (0) on "onTaxi" is TakeTaxi(m_190),
    /// handled. Any other yes is handled too (return 1 at :774); another answer is not.
    /// STILL-OPEN: "onPortal", "onCashTaxi" and "onTopMostLayer" (MoveByPortal, TakeCashTaxi, the layer) are not ported here.
    /// </summary>
    public bool OnMessageBox(MessageBoxController sender, int button, string tag)
    {
        MessageBoxQueue.Next();
        if (button != 0) return false;
        if (tag == "onTaxi") { TakeTaxi((int)taxiTarget); return true; }
        return true;     // :774: any yes answer is handled, whatever the tag (onPortal, onCashTaxi, onTopMostLayer do their part below)
    }

    /// <summary>
    /// TakeTaxi (@0x141b5a550, :1369): no player, a dead player, or state 0x25 / 0x2a: nothing. No station near: notice 0x89;
    /// a field of map type 3: 0x51; the current field: 0x8a; mesos less than the fare: 0x34. Then the dialog closes; a
    /// rider rides off (RideOffActionTaxi) and the taxi comes after the dismount; otherwise, if the player can enter state
    /// 0x25, it turns to the station and enters it with the field.
    /// </summary>
    void TakeTaxi(int fieldId)
    {
        if (!HasPlayer() || PlayerDead()) return;
        int st = PlayerStateId();
        if (st == StateTaxiCall || st == StateTaxiBoard) return;
        if (!StationNear()) { Notice(0x89); return; }
        if (FieldMapType3(fieldId)) { Notice(0x51); return; }
        if (field == fieldId) { Notice(0x8a); return; }
        long fare = Taxi.GetTaxiFare(field, fieldId, (short)PlayerLevel);
        if (Mesos() - fare < 0) { Notice(0x34); return; }
        Close();                                                    // CloseDialog(0x4e42)
        if (PlayerRiding()) { RideOffForTaxi?.Invoke(fieldId); return; }
        if (!CanEnterState(StateTaxiCall)) return;
        StartTaxiCall?.Invoke(fieldId);
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

    /// <summary>NpcDataTableCli (npcdata/*.xml, NpcDataCli::LoadXml): the npcs the search and the field panel list.</summary>
    public Ms2.Formats.World.NpcTable Npcs;
    /// <summary>DungeonRoomTable: the rooms behind a field's dungeon (portalType 8) portals.</summary>
    public DungeonRooms Rooms;
    /// <summary>
    /// CFieldMetaTable::FUN_1406fbb50 for an (npc, field) pair with a time window (FieldMeta.TimedNpcFields): the server
    /// time is inside it. Offline it is shut, as the quest log's state has it (NoQuestState).
    /// </summary>
    public Func<int, int, bool> NpcFieldOpen = (_, _) => false;

    bool NpcHere(int npc, int field) => Meta == null || !Meta.TimedNpcFields.Contains((npc, field)) || NpcFieldOpen(npc, field);

    /// <summary>
    /// FUN_1401aa6a0 (arcademaindata.cpp:60): "&lt;title&gt; &lt;name&gt;" from the string tables 0x13 (npctitle, the
    /// $npcTitle reserved word) and 2 (npcname), or the name alone when the npc has no title.
    /// </summary>
    string NpcName(int npc)
    {
        var row = Npcs?.Find(npc);
        string name = row?.Name ?? "";
        return string.IsNullOrEmpty(row?.Title) ? name : $"{row.Title} {name}";
    }

    /// <summary>
    /// NpcSearchName (worldmapmanager.cpp:1218): a monster as s_worldmap_search_monster (name, level, race), an npc with a
    /// function title as s_worldmap_search_npc_function (title, name), else the name; in an indoor field (one with a
    /// parent) s_worldmap_search_npc_indoor adds the field's name.
    /// </summary>
    string NpcSearchName(int npc, int fieldId)
    {
        string text;
        var row = Npcs?.Find(npc);
        if (row != null && row.Friendly == 0)
            text = Fill(Common("s_worldmap_search_monster"), NpcName(npc), row.Level, Model.Tables.RaceName(row.Race) ?? "");
        else
        {
            string title = Model.Tables.NpcFunctionName(npc) ?? "";
            text = title.Length > 0 ? Fill(Common("s_worldmap_search_npc_function"), title, NpcName(npc)) : NpcName(npc);
        }
        if (fieldId != 0 && Table.ParentField(fieldId) != null)
            return Fill(Common("s_worldmap_search_npc_indoor"), text, MapName((uint)fieldId));
        return text;
    }

    /// <summary>A dungeon row: "[{0}] {1}" of the group's name (FUN_140e14b70: TranslateEnumDungeonGroup,
    /// translateenum.cpp:50) and the room's (string table 0x14), the raid icon for group 2.</summary>
    Dictionary<string, object> DungeonItem(DungeonRoom room, uint fieldId) => new()
    {
        ["text"] = Fill("[{0}] {1}", GroupName(room.GroupType), Model.Tables.FieldEnteranceName(room.Id) ?? ""),
        ["id"] = fieldId,
        ["icon"] = room.GroupType == 2 ? "dungeon_raid" : "dungeon",
        ["iconData"] = (uint)room.Id,
    };

    static readonly string[] GroupKeys =
    {
        null, "normal", "raid", "chaosRaid", "reverseRaid", "lapenta", "guildRaid", "darkStream", "worldBoss", "item", "vip",
        "event", "fameChallenge", "colosseum", "turka",
    };
    string GroupName(int group) => group > 0 && group < GroupKeys.Length ? Common("s_enum_dungeon_group_" + GroupKeys[group]) : "";

    /// <summary>The npc's list: bosses (class 4, 6), elites (3, 7), else monsters.</summary>
    static List<object> ByClass(int cls, List<object> bosses, List<object> elites, List<object> monsters) =>
        cls is 4 or 6 ? bosses : cls is 3 or 7 ? elites : monsters;

    /// <summary>
    /// BuildFieldDetails (worldmapmanager.cpp:1248): the title (name and level), the dungeons behind the field's portalType 8
    /// portals (by group, then level: WorldMapDungeonLess :1440), the npcs of the field and its connected fields
    /// (FUN_1406fc2f0, those with a search entry; each name once) as monsters, elites, bosses and notable npcs, the
    /// connected maps, the description and the cash taxi flag. The shadow gate (npc kind 0x55, CJobConditionTable) and a
    /// boss's spawn guarantee time (the server's) are not ported.
    /// </summary>
    public Dictionary<string, object> FieldDetails(uint id)
    {
        if (Table.FindField(id) is not { } f) return null;
        var dungeons = new List<object>();
        if (Rooms != null && Meta != null && Meta.PortalType8.TryGetValue((int)id, out var portals))
        {
            var rooms = portals.SelectMany(Rooms.GetRooms).ToList();
            rooms.Sort((a, b) => a.GroupType != b.GroupType ? a.GroupType.CompareTo(b.GroupType) : a.DungeonLevel.CompareTo(b.DungeonLevel));
            foreach (var r in rooms) dungeons.Add(DungeonItem(r, id));
        }
        var monsters = new List<object>(); var bosses = new List<object>(); var elites = new List<object>();
        var npcs = new List<object>(); var gates = new List<object>();
        var connected = Meta?.ConnectedFields((int)id) ?? new List<int> { (int)id };
        var seen = new HashSet<string>();
        foreach (int c in connected)
        {
            if (!Table.Search.ContainsKey((uint)c) || Meta == null || !Meta.FieldNpcs.TryGetValue(c, out var ids)) continue;
            foreach (int npc in ids)
            {
                if (!NpcHere(npc, c)) continue;                         // FUN_1406fb610
                var row = Npcs?.Find(npc);
                if (row == null || NpcName(npc).Length == 0) continue;
                string name = NpcSearchName(npc, c);
                if (!seen.Add(name)) continue;
                if (row.Kind == 0x55) continue;                         // the shadow gate: CJobConditionTable is not ported
                if (row.Friendly is 1 or 2)
                {
                    if (row.Icon.Length == 0 && row.Kind == 0) continue;
                    npcs.Add(new Dictionary<string, object> { ["text"] = name });
                }
                else if (row.Friendly == 0)
                    ByClass(row.Class, bosses, elites, monsters).Add(new Dictionary<string, object> { ["text"] = name });
            }
        }
        var indoors = new List<object>();
        foreach (int c in connected)
        {
            if (!Table.Search.ContainsKey((uint)c) || c == id) continue;
            indoors.Add(new Dictionary<string, object> { ["text"] = MapName((uint)c) });
        }
        return new Dictionary<string, object>
        {
            ["fieldSN"] = id,
            ["title"] = Fill(Common("s_worldmap_search_map_title"), MapName(id), f.MapLevel),
            ["fishingSpot"] = FishingSpot(id),
            ["mapDesc"] = Model.Tables.MapTileDesc((int)id) ?? "",
            ["dungeon"] = dungeons, ["monster"] = monsters, ["bossMonster"] = bosses, ["eliteMonster"] = elites,
            ["shadowgate"] = gates, ["indoor"] = indoors, ["npc"] = npcs,
            ["cashtaxi"] = !f.IsIndoor && f.CashTaxiDestinationAllowed,
        };
    }

    /// <summary>
    /// BuildNameSearch (worldmapmanager.cpp:955): the text (case-insensitive, FUN_14083fec0) in the search fields' names
    /// (outdoor ones as maps, with the cash taxi icon where one may land; indoor ones as connected maps), in the dungeon
    /// room names behind the portalType 8 portals of fields with a tile, and in the npcs' search names (by m_npcFields'
    /// hash order, each name once; an npc counts in the first field it stands in, which must have a tile or an outdoor
    /// field with one). The shadow gates and a boss's spawn guarantee time are not ported.
    /// </summary>
    public Dictionary<string, object> NameSearch(string text)
    {
        bool Has(string s) => s.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        var maps = new List<object>();
        var indoors = new List<object>();
        foreach (var info in Table.Search.Values)
        {
            if (!Has(info.Name)) continue;
            if (info.MappedId != info.FieldId)
                indoors.Add(new Dictionary<string, object> { ["text"] = info.Name, ["id"] = info.MappedId });
            else
            {
                var item = new Dictionary<string, object> { ["text"] = info.Name, ["id"] = info.FieldId, ["iconData"] = info.FieldId };
                if (Table.FindField(info.FieldId) is { } f && !f.IsIndoor && f.CashTaxiDestinationAllowed) item["icon"] = "cashTaxi";
                maps.Add(item);
            }
        }
        var dungeons = new List<object>();
        if (Rooms != null && Meta != null)
            foreach (int field in FieldMeta.AtlMapOrder(Meta.PortalType8.Keys))
            {
                if (Table.FindTile((uint)field) == null) continue;
                foreach (int portal in Meta.PortalType8[field])
                    foreach (var room in Rooms.GetRooms(portal))
                        if (Has(Model.Tables.FieldEnteranceName(room.Id) ?? "")) dungeons.Add(DungeonItem(room, (uint)field));
            }
        var monsters = new List<object>(); var bosses = new List<object>(); var elites = new List<object>();
        var npcs = new List<object>(); var gates = new List<object>();
        if (Npcs != null && Meta != null)
        {
            var seen = new HashSet<string>();
            // m_npcFields holds the npcs with npc data (LoadFieldMetaData skips the others)
            foreach (int npc in FieldMeta.AtlMapOrder(Meta.NpcFields.Keys.Where(n => Npcs.Find(n) != null)))
            {
                var fields = Meta.NpcFields[npc];
                if (fields.Count == 0) continue;
                int field = fields[0];
                if (Table.FindField((uint)field) == null) continue;
                var row = Npcs.Find(npc);
                if (!NpcHere(npc, field)) continue;
                if (Table.FindTile((uint)field) == null && (Table.ParentField(field) is not int outdoor || Table.FindTile((uint)outdoor) == null)) continue;
                if (NpcName(npc).Length == 0) continue;
                string name = NpcSearchName(npc, field);
                if (!seen.Add(name)) continue;
                if (row.Kind == 0x55) continue;                         // the shadow gate: CJobConditionTable is not ported
                if (row.Friendly is 1 or 2)
                {
                    if (row.Icon.Length == 0 && row.Kind == 0) continue;
                    if (!Has(name)) continue;
                    npcs.Add(new Dictionary<string, object> { ["text"] = name, ["id"] = (uint)field });
                }
                else if (row.Friendly == 0)
                {
                    if (!Has(name)) continue;
                    if (row.Class is 6 or 7 && Table.ParentField(field) == null) continue;
                    ByClass(row.Class, bosses, elites, monsters).Add(new Dictionary<string, object> { ["text"] = name, ["id"] = (uint)field });
                }
            }
        }
        var o = new Dictionary<string, object>
        {
            ["map"] = maps, ["dungeon"] = dungeons, ["monster"] = monsters, ["eliteMonster"] = elites, ["bossMonster"] = bosses,
            ["shadowgate"] = gates, ["indoor"] = indoors, ["npc"] = npcs,
        };
        if (maps.Count + dungeons.Count + monsters.Count + elites.Count + bosses.Count + npcs.Count + indoors.Count == 0)
            o["title"] = Common("s_err_worldmap_search_none");
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
    void OnDisplaySettingsChanged()
    {
        dungeonIcons.Clear();       // ClearDungeonInfos
        AddDungeonIcons();
        BuildWorldTiles();
    }

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
            case "onclickmissionrewardprev":
                if (args.Length > 0 && MissionRewards?.GetPrevRound(Int(args, 0)) is int prev and not 0) UpdateFieldMissionRound(prev);
                return true;
            case "onclickmissionrewardnext":
                if (args.Length > 0 && MissionRewards?.GetNextRound(Int(args, 0)) is int next and not 0) UpdateFieldMissionRound(next);
                return true;
            case "ontaxi": OnTaxi(args); return true;
            // the cash taxi, a portal move, the boss / dungeon dialogs and the mission reward (SendQuest :1782, the
            // Quest packet 0x1a once the round's target is reached) move the player or spend: not here
            case "oncashtaxi": case "onportal": case "onboss": case "ondungeon": case "onclicksearchinfoicon":
            case "onclickmissionreward":
                return true;
        }
        return false;
    }
}
