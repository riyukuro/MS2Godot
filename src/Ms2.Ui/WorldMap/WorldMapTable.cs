using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui.WorldMap;

/// <summary>
/// The data side of WorldMapManager (ms2/guess/worldmapmanager.cpp): the world map tiles of table/worldmap.xml's files
/// (LoadWorldMapFiles :392), the field name tags (LoadFieldNameInfo :553) and the fields the search finds
/// (LoadFieldTiles :527), with the CFieldDataCli rows (commonfiles/fielddata.cpp vfunc_1) and the continents
/// (CContinentRegionTable, MapContinentTable.xml) they read.
/// </summary>
public sealed class WorldMapTable
{
    /// <summary>UIWorldMap::MapTile (placeholder/uiworldmap.h).</summary>
    public sealed class Tile
    {
        public int TileType;            // m_18 "tileType" (0 the map, 1 the sky fortress, 3 not numbered)
        public int Index;               // m_1c the order in its world
        public bool Town, Public;       // m_20 "type", m_21 "public"
        public int X, Y, Z, Size;       // m_24..m_30
        public uint FieldId;            // m_34 "code"
        public int World;               // m_38
        public string NormalImage = "", SealedImage = "";   // m_40, m_48
        public uint Distance;           // m_50 to the current tile (BuildWorldTiles)
        public int ShadowGate, ShadowGateField;   // m_54, m_58
        public int NameAlign;           // m_5c
        public string NameMovie = "";   // m_60 "tagMovie"
        public int NameOffsetX, NameOffsetY;      // m_68, m_6c
        public uint Dungeon, Quest;     // m_70, m_74
        public string CustomContinent = "";       // m_78
        public Tile Clone() => (Tile)MemberwiseClone();   // CopyMapTile (:1605)
    }

    /// <summary>The CFieldDataCli members the world map reads.</summary>
    public sealed class Field
    {
        public int Id, Continent, MapType, PkMode, MapLevel, IndoorMask = 1, CashTaxiNotDestination;
        /// <summary>m_nCashTaxiNotDeparture: &lt;cashCall cashTaxiNotDeparture&gt; (fielddata.cpp:736); m_nEnterAdminLevel:
        /// &lt;property enterAdminLevel&gt; (:669).</summary>
        public int CashTaxiNotDeparture, EnterAdminLevel;
        public bool CashTaxiDepartureAllowed => CashTaxiNotDeparture == 0;   // fielddata.cpp:799
        public bool MapSearch = true;   // the ctor's m_bMapSearch(true) (fielddata.cpp:548)
        public bool IsIndoor => IndoorMask == 2 || IndoorMask == 4;     // IsIndoor (fielddata.cpp:757)
        public bool CashTaxiDestinationAllowed => CashTaxiNotDestination == 0;   // fielddata.cpp:802
    }

    /// <summary>UIWorldMap::FieldSearchInfo: the field, its name (string table 3) and the tile it maps to.</summary>
    public sealed class SearchInfo
    {
        public uint FieldId;            // m_18
        public string Name = "";        // m_20
        public uint MappedId;           // m_28 the parent field, else the field
    }

    /// <summary>m_18: the tiles of each world (std::map, ascending).</summary>
    public readonly SortedDictionary<int, List<Tile>> Worlds = new();
    readonly Dictionary<uint, Tile> tiles = new();              // m_48
    readonly Dictionary<int, Tile> defaults = new();            // m_58 the "default" tile of a continent
    /// <summary>m_8: the fields of the &lt;portal&gt; rows.</summary>
    public readonly SortedSet<uint> PortalFields = new();
    /// <summary>m_68: the fields the search finds (std::map, ascending).</summary>
    public readonly SortedDictionary<uint, SearchInfo> Search = new();
    readonly Dictionary<int, Field> fields = new();
    readonly Dictionary<int, int> representWorld = new();
    /// <summary>m_78 (SelectFieldTile / SelectedField).</summary>
    public uint SelectedField;

    public IReadOnlyDictionary<int, Field> Fields => fields;
    public Func<int, int?> ParentField = _ => null;

    public static WorldMapTable Load(PackSet packs, Func<int, string> mapName, Func<int, int?> parentField)
    {
        var t = new WorldMapTable { ParentField = parentField ?? (_ => null) };
        t.LoadContinents(packs);
        t.LoadFields(packs);
        var k = FeatureElements.Constants(packs);
        t.LoadWorldMapFiles(packs, (int)k.Float("WorldMapAdjustTileX", -15), (int)k.Float("WorldMapAdjustTileY", -35));
        t.LoadFieldTiles(mapName);
        return t;
    }

    static XDocument Xml(PackSet packs, string name)
    {
        // GetXmlPath("Table", name): the locale folder first
        return TablePath.Resolve(packs, name) is { } p ? GameStrings.Parse(packs.Read("Xml", p)) : null;
    }

    static int Int(XElement e, string name, int def = 0) =>
        int.TryParse(((string)e?.Attribute(name))?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : def;
    static bool Bool(XElement e, string name, bool def = false) =>
        (string)e?.Attribute(name) is { } s ? s.Trim() is "1" or "true" or "True" : def;

    /// <summary>CContinentRegionTable (continentregiontable.cpp:48): representWorldmapContinent by continent id.</summary>
    void LoadContinents(PackSet packs)
    {
        foreach (var c in Xml(packs, "MapContinentTable.xml")?.Root?.Elements("continent") ?? Enumerable.Empty<XElement>())
            representWorld[Int(c, "id")] = Int(c, "representWorldmapContinent");
    }

    /// <summary>CFieldData::vfunc_1 (fielddata.cpp:640-750) over the environment FUN_14028f8d0 picks.</summary>
    void LoadFields(PackSet packs)
    {
        // the table (locale folder first), else the per-map files of data that has none
        foreach (var fd in (Xml(packs, "fielddata.xml") ?? FieldData.Document(packs))?.Root?.Elements("fieldData") ?? Enumerable.Empty<XElement>())
        {
            var env = FeatureElements.SelectEnvironment(packs, fd);
            if (env == null) continue;
            var f = new Field { Id = Int(fd, "id") };
            var prop = env.Element("property");
            f.Continent = Int(prop, "continentCode");
            f.MapType = Int(prop, "mapType");
            f.PkMode = Int(prop, "pkMode");
            f.MapSearch = Bool(prop, "mapSearch", true);
            f.MapLevel = Int(env.Element("drop"), "maplevel");
            f.CashTaxiNotDestination = Int(env.Element("cashCall"), "cashTaxiNotDestination");
            f.CashTaxiNotDeparture = Int(env.Element("cashCall"), "cashTaxiNotDeparture");
            f.EnterAdminLevel = Int(prop, "enterAdminLevel");
            if (env.Element("indoor") is { } indoor) f.IndoorMask = 1 << Int(indoor, "type");
            fields[f.Id] = f;
        }
    }

    public Field FindField(uint id) => fields.TryGetValue((int)id, out var f) ? f : null;

    /// <summary>WorldOfField (:915): the continent's world map continent, else the continent itself.</summary>
    public int WorldOfField(int continent) => representWorld.TryGetValue(continent, out int w) ? w : continent;

    /// <summary>LoadWorldMapFiles (:392): every file of worldmap.xml; its environment's portals and maps.</summary>
    void LoadWorldMapFiles(PackSet packs, int adjustX, int adjustY)
    {
        const string tilePath = "./Data/Resource/Image/Map/Worldmap/";     // CommonResource WORLDMAP_TILE_PATH (+0xd0)
        var names = (Xml(packs, "worldmap.xml")?.Root?.Elements("xml") ?? Enumerable.Empty<XElement>())
            .Where(e => FeatureElements.IsAvailable(packs, e)).Select(e => (string)e.Attribute("name"));      // LoadWorldMapNames (:623)
        var custom = new SortedDictionary<int, List<Tile>>();
        foreach (var name in names)
        {
            var root = Xml(packs, name)?.Root;
            if (root == null || root.Name != "ms2worldmap") continue;
            var group = FeatureElements.SelectEnvironment(packs, root);
            if (group == null) continue;
            foreach (var p in group.Elements("portal").Where(e => FeatureElements.IsAvailable(packs, e)))
                if (FindField((uint)Int(p, "code")) != null) PortalFields.Add((uint)Int(p, "code"));
            foreach (var m in group.Elements("map").Where(e => FeatureElements.IsAvailable(packs, e)))
            {
                var tile = new Tile { FieldId = (uint)Int(m, "code") };
                var field = FindField(tile.FieldId);
                if (field == null) continue;
                tile.World = WorldOfField(field.Continent);
                tile.X = Int(m, "x"); tile.Y = Int(m, "y"); tile.Z = Int(m, "z"); tile.Size = Int(m, "size");
                tile.Town = Bool(m, "type"); tile.Public = Bool(m, "public");
                bool isDefault = Bool(m, "default");
                tile.TileType = Int(m, "tileType");
                tile.Dungeon = (uint)Int(m, "dungeon"); tile.Quest = (uint)Int(m, "quest");
                tile.CustomContinent = (string)m.Attribute("customContinent") ?? "";
                // the shadow gate (an npc of kind 0x55 with a CJobConditionTable move field) is not read here
                tile.X += adjustX; tile.Y += adjustY;
                tile.NormalImage = string.Format(CultureInfo.InvariantCulture, "{0}{1:D8}.png", tilePath, tile.FieldId);
                tile.SealedImage = string.Format(CultureInfo.InvariantCulture, "{0}G_{1:D8}.png", tilePath, tile.FieldId);
                if (tile.CustomContinent.Length != 0)
                {
                    foreach (var w in tile.CustomContinent.Split(','))
                        if (int.TryParse(w.Trim(), out int world)) Add(custom, world, tile);
                }
                else Add(Worlds, tile.World, tile);
                tiles[tile.FieldId] = tile;
                if (isDefault) defaults[field.Continent] = tile;
            }
        }
        foreach (var (world, list) in custom)
            foreach (var src in list)
            {
                var copy = src.Clone();
                copy.World = world;
                Add(Worlds, world, copy);
            }
        foreach (var list in Worlds.Values)
        {
            Sort(list, TileLess);
            int order = 1;
            foreach (var tile in list)
                if (tile.TileType != 3) tile.Index = order++;
        }
        LoadFieldNameInfo(packs);
    }

    static void Add(SortedDictionary<int, List<Tile>> map, int key, Tile t)
    {
        if (!map.TryGetValue(key, out var l)) map[key] = l = new List<Tile>();
        l.Add(t);
    }

    /// <summary>std::sort with the strict weak order (List.Sort wants a comparison).</summary>
    public static void Sort(List<Tile> list, Func<Tile, Tile, bool> less) =>
        list.Sort((a, b) => less(a, b) ? -1 : less(b, a) ? 1 : 0);

    /// <summary>
    /// WorldMapTileLess (:1449): by tile type, then z, then the tile centre turned by FromEulerAnglesXYZ(pi/4, 0, pi/4)
    /// (efd Matrix3: Rx * Ry * Rz, a point times the matrix as a row vector), the greater y first.
    /// </summary>
    public static bool TileLess(Tile a, Tile b)
    {
        if (a.TileType != b.TileType) return a.TileType < b.TileType;
        if (a.Z != b.Z) return a.Z < b.Z;
        return RotatedY(a) > RotatedY(b);
    }

    static float RotatedY(Tile t)
    {
        float x = t.X + t.Size / 2.0f, y = t.Y + t.Size / 2.0f, z = t.Z;
        float s = MathF.Sin(0.7853982f), c = MathF.Cos(0.7853982f);
        // (Rx * Rz)[i][1]: row 0 sin, row 1 cos * cos, row 2 -sin * cos
        return x * s + y * (c * c) + z * (-s * c);
    }

    /// <summary>LoadFieldNameInfo (:553): worldMapFieldNameInfo.xml's tag movie, alignment and offsets per tile.</summary>
    void LoadFieldNameInfo(PackSet packs)
    {
        if (tiles.Count == 0) return;
        foreach (var k in Xml(packs, "worldMapFieldNameInfo.xml")?.Root?.Elements("key") ?? Enumerable.Empty<XElement>())
        {
            if (!FeatureElements.IsAvailable(packs, k)) continue;
            uint id = (uint)Int(k, "mapCode");
            if (id == 0 || FindTile(id) is not { } tile) continue;
            tile.NameAlign = AlignFromName((string)k.Attribute("align") ?? "");
            tile.NameMovie = (string)k.Attribute("tagMovie") ?? "";
            tile.NameOffsetX = Int(k, "alignOffsetX");
            tile.NameOffsetY = Int(k, "alignOffsetY");
        }
    }

    /// <summary>GetAlignFromName @0x140d01040 (unit_140cff310_gfxutilwrappers.cpp).</summary>
    public static int AlignFromName(string s) => s.ToLowerInvariant() switch
    {
        "topleft" => 5, "topcenter" => 1, "topright" => 6, "centerleft" => 3, "center" => 0,
        "centerright" => 4, "bottomleft" => 7, "bottomcenter" => 2, "bottomright" => 8, _ => 0,
    };

    /// <summary>LoadFieldTiles (:527): every field with map search on whose field (or parent) has a tile.</summary>
    void LoadFieldTiles(Func<int, string> mapName)
    {
        foreach (var f in fields.Values)
        {
            var info = new SearchInfo { FieldId = (uint)f.Id, Name = mapName?.Invoke(f.Id) ?? "" };
            info.MappedId = ParentField(f.Id) is int p ? (uint)p : (uint)f.Id;
            if (FindTile(info.MappedId) == null || !f.MapSearch) continue;
            Search[(uint)f.Id] = info;
        }
    }

    /// <summary>FindMapTileByKey (:768).</summary>
    public Tile FindTile(uint key) => tiles.TryGetValue(key, out var t) ? t : null;

    /// <summary>FindMapTile (:875): the default tile of a continent.</summary>
    public Tile DefaultTile(int continent) => defaults.TryGetValue(continent, out var t) ? t : null;

    /// <summary>GetWorldTiles (:753).</summary>
    public List<Tile> WorldTiles(int world) => Worlds.TryGetValue(world, out var l) ? l : new List<Tile>();

    /// <summary>FindFieldSearchInfo (:788): the first search entry whose name is the text.</summary>
    public SearchInfo FindSearch(string name) => Search.Values.FirstOrDefault(i => i.Name == name);

    /// <summary>SelectFieldTile (:851).</summary>
    public void SelectFieldTile(uint key)
    {
        if (tiles.ContainsKey(key)) SelectedField = key;
        else if (ParentField((int)key) is int p && tiles.ContainsKey((uint)p)) SelectedField = (uint)p;
    }
}
