using System.Xml.Linq;

namespace Ms2.Ui.Skills;

/// <summary>A &lt;move&gt; of magicPath.xml (CMagicData): the times a magic's tick reads, in seconds.</summary>
public sealed record MagicPathMove(float DelayTime, float SpawnTime, float LifeTime, float DestroyTime);

/// <summary>
/// The magic paths (LoadMagicPath @0x14145c4d0, magicdata.cpp:302): ./data/xml/table/magicPath.xml, each &lt;type id&gt; a list of
/// its &lt;move&gt; rows; a repeated id replaces the earlier list (SetAt). The default id 0 is then set to one row of zeros, so an
/// attack without a magicPathID still gets a magic. Read on first use (FUN_14145c300 reloads a dirty cache).
/// STILL-OPEN: only the times are read; the fire offsets, the flight (vel, distance, control values) and the rest are not.
/// </summary>
public sealed class MagicPathTable
{
    // Zero before Empty: the static initializers run in order, and Empty's id 0 row is Zero
    static readonly MagicPathMove Zero = new(0, 0, 0, 0);
    public static readonly MagicPathTable Empty = new(null);
    readonly Dictionary<int, MagicPathMove[]> types = new();

    public MagicPathTable(XDocument doc)
    {
        foreach (var t in doc?.Root?.Elements("type") ?? Enumerable.Empty<XElement>())
            types[Xml.Int(t, "id")] = t.Elements("move")
                .Select(m => new MagicPathMove(Xml.Float(m, "delayTime"), Xml.Float(m, "spawnTime"), Xml.Float(m, "lifeTime"), Xml.Float(m, "destroyTime")))
                .ToArray();
        types[0] = new[] { Zero };
    }

    /// <summary>FUN_14145c300 @0x14145c300 (magicdata.cpp:278): the id's row at <paramref name="index"/> modulo the row count;
    /// null for an unknown id or an id without rows.</summary>
    public MagicPathMove Get(uint magicId, int index)
    {
        if (!types.TryGetValue((int)magicId, out var list) || list.Length == 0) return null;
        return list[(int)((ulong)(long)index % (ulong)list.Length)];
    }

    /// <summary>FUN_14028d260: seconds to whole milliseconds (truncated).</summary>
    public static int Ms(float seconds) => (int)(long)(seconds * 1000.0f);
}
