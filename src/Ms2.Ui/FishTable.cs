using System.Globalization;
using System.Xml.Linq;

namespace Ms2.Ui;

/// <summary>
/// CFishData (commonfiles/fishsimultedata.cpp:100, Load @0x1403b1100): one fish of Table/fish.xml.
/// </summary>
public sealed class FishData
{
    /// <summary>CFishData::Load: id, fishMastery, rank, ignoreSpotMastery, fishingBook (default 1), companion, habitat (name,
    /// see <see cref="FishingSpots.HabitatId"/>) and the four size ranges.</summary>
    public uint Id, Mastery;
    public int Rank;
    public bool IgnoreSpotMastery;
    public bool FishingBook = true;
    public uint Companion;
    public int Habitat = 8;
    /// <summary>m_aSize: small [a, b-1], middle [b, c-1], big [c, d-1], huge [d, d*10] from smallSize "a-b" and bigSize "c-d".</summary>
    public readonly (uint Min, uint Max)[] Sizes = new (uint, uint)[4];

    /// <summary>CFishData::Load: null when smallSize or bigSize is not two numbers (the client's Load returns 0).</summary>
    public static FishData Load(XElement e)
    {
        var f = new FishData
        {
            Id = U(e, "id"), Mastery = U(e, "fishMastery"), Rank = (int)U(e, "rank"), IgnoreSpotMastery = U(e, "ignoreSpotMastery") != 0,
            Companion = U(e, "companion"),
            Habitat = FishingSpots.HabitatId((string)e.Attribute("habitat") ?? ""),
        };
        if (e.Attribute("fishingBook") != null) f.FishingBook = U(e, "fishingBook") != 0;
        var small = Pair((string)e.Attribute("smallSize"));
        var big = Pair((string)e.Attribute("bigSize"));
        if (small == null || big == null) return null;
        f.Sizes[0] = (small[0], small[1] - 1);
        f.Sizes[2] = (big[0], big[1] - 1);
        f.Sizes[1] = (small[1], big[0] - 1);
        f.Sizes[3] = (big[1], big[1] * 10);
        return f;
    }

    static uint U(XElement e, string a) => uint.TryParse((string)e.Attribute(a), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    static uint[] Pair(string s)
    {
        var parts = (s ?? "").Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return null;
        return uint.TryParse(parts[0].Trim(), out var a) && uint.TryParse(parts[1].Trim(), out var b) ? new[] { a, b } : null;
    }

    /// <summary>GetSizeClass @0x1403b1520: the first range holding the size (Unk_FishRange::IsInRange, min and max inclusive), 4 = none.</summary>
    public int GetSizeClass(uint size)
    {
        for (int i = 0; i < 4; i++)
            if (Sizes[i].Min <= size && size <= Sizes[i].Max) return i;
        return 4;
    }

    /// <summary>GetSizeGrade @0x1403b1590: class 0 is 1, class 1 is 2, class 2 and 3 are 3, none 0.</summary>
    public int GetSizeGrade(uint size) => GetSizeClass(size) switch { 0 => 1, 1 => 2, 2 or 3 => 3, _ => 0 };

    /// <summary>GetIconPath @0x1403b1600.</summary>
    public string IconPath => $"./Data/Resource/Image/Fishing/{Id}.png";
}

/// <summary>
/// CFishingTable's fish (Load, Table/fish.xml) and habitat maps (LoadHabitats @0x1403b27e0, Table/fishHabitat.xml): the fish
/// by id and the maps each is found in.
/// </summary>
public sealed class FishTable
{
    readonly SortedDictionary<uint, FishData> fish = new();
    readonly Dictionary<uint, List<uint>> maps = new();

    public static readonly FishTable Empty = new(null, null, _ => true);

    /// <param name="available">IsElementAvailable on each &lt;fish&gt; row of fish.xml.</param>
    public FishTable(XDocument fishXml, XDocument habitatXml, Func<XElement, bool> available)
    {
        foreach (var e in fishXml?.Root?.Elements("fish") ?? Enumerable.Empty<XElement>())
        {
            if (!available(e)) continue;
            if (FishData.Load(e) is { } f) fish[f.Id] = f;                 // m_mapFish.SetAt
        }
        foreach (var e in habitatXml?.Root?.Elements("fish") ?? Enumerable.Empty<XElement>())
        {
            if (!uint.TryParse((string)e.Attribute("id"), out var id)) id = 0;
            if (!maps.TryGetValue(id, out var list)) maps[id] = list = new List<uint>();
            // FUN_140274fc0: the numbers of "a, b, ..." appended
            foreach (var s in ((string)e.Attribute("habitat") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (uint.TryParse(s.Trim(), out var m)) list.Add(m);
        }
    }

    public int Count => fish.Count;

    /// <summary>The fish row, null for an unknown id.</summary>
    public FishData Get(uint id) => fish.TryGetValue(id, out var f) ? f : null;

    /// <summary>GetFishIdsByHabitat @0x1403b30b0: the fish of a habitat, only those of the book when <paramref name="bookOnly"/>.
    /// STILL-OPEN: the client walks a CAtlMap (hash order); the book sorts the ids afterwards, so only equal-keyed ties could differ.</summary>
    public List<uint> IdsByHabitat(int habitat, bool bookOnly)
        => fish.Values.Where(f => f.Habitat == habitat && (!bookOnly || f.FishingBook)).Select(f => f.Id).ToList();

    /// <summary>FUN_1403b2ee0 @0x1403b2ee0: the first three maps of the fish that exist (<paramref name="mapExists"/>), as
    /// "$map:{id}$" joined by ", "; "" without one.</summary>
    public string MapList(uint fishId, Func<uint, bool> mapExists)
    {
        if (!maps.TryGetValue(fishId, out var list)) return "";
        var parts = new List<string>();
        foreach (var m in list)
        {
            if (!mapExists(m)) continue;
            parts.Add("$map:" + m.ToString(CultureInfo.InvariantCulture) + "$");
            if (parts.Count >= 3) break;
        }
        return string.Join(", ", parts);
    }
}
