using System.Globalization;
using System.Numerics;
using System.Xml.Linq;

namespace Ms2.Ui.Skills;

/// <summary>A &lt;move&gt; of magicPath.xml (CMagicData): the times a magic's tick reads, in seconds, and its fire point: the
/// owner's node "fireNode" (m_strFireNode), or "fireFixedPosition" (m_ptFireFixed) when that is not zero
/// (CMagic::FUN_1414609e0, magicdata.cpp:1397), plus "fireOffsetPosition" (m_ptFireOffset) turned by "rotation" (m_nRotation,
/// 1 by the ctor, :212) and "dirRotZDegree" (m_fDirRotZ). "ignoreCancelAtSpawnTime" (XmlGetBool, :340) keeps a magic in its
/// spawn wait from the skill state's cancel (CMagicModel::CanCancel).</summary>
public sealed record MagicPathMove(float DelayTime, float SpawnTime, float LifeTime, float DestroyTime, string FireNode = "",
    Vector3 FireFixed = default, Vector3 FireOffset = default, int Rotation = 1, float DirRotZ = 0, bool IgnoreCancelAtSpawnTime = false);

/// <summary>
/// The magic paths (LoadMagicPath @0x14145c4d0, magicdata.cpp:302): ./data/xml/table/magicPath.xml, each &lt;type id&gt; a list of
/// its &lt;move&gt; rows; a repeated id replaces the earlier list (SetAt). The default id 0 is then set to one row of zeros, so an
/// attack without a magicPathID still gets a magic. Read on first use (FUN_14145c300 reloads a dirty cache).
/// STILL-OPEN: only the times and the fire point are read; the fire offsets, the flight (vel, distance, control values) and the rest are not.
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
                .Select(m => new MagicPathMove(Xml.Float(m, "delayTime"), Xml.Float(m, "spawnTime"), Xml.Float(m, "lifeTime"), Xml.Float(m, "destroyTime"),
                    (string)m.Attribute("fireNode") ?? "", Point3((string)m.Attribute("fireFixedPosition")),
                    Point3((string)m.Attribute("fireOffsetPosition")),
                    m.Attribute("rotation") != null ? Xml.Int(m, "rotation") : 1, Xml.Float(m, "dirRotZDegree"),
                    Xml.Bool(m, "ignoreCancelAtSpawnTime")))
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

    /// <summary>FUN_14028f060 (commonutil.cpp:1172): "x,y,z" split at ","; exactly three floats, else zero.</summary>
    public static Vector3 Point3(string s)
    {
        if (string.IsNullOrEmpty(s)) return Vector3.Zero;
        var parts = s.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) return Vector3.Zero;
        var v = new float[3];
        for (int i = 0; i < 3; i++)
            v[i] = float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;
        return new Vector3(v[0], v[1], v[2]);
    }

    /// <summary>FUN_14028d260: seconds to whole milliseconds (truncated).</summary>
    public static int Ms(float seconds) => (int)(long)(seconds * 1000.0f);
}
