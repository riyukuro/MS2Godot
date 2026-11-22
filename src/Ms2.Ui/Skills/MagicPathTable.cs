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
    Vector3 FireFixed = default, Vector3 FireOffset = default, int Rotation = 1, float DirRotZ = 0, bool IgnoreCancelAtSpawnTime = false)
{
    /// <summary>"vel" (m_fVelocity): the flight speed per second; 0 makes no flight (CMagic::FUN_14145ec50).</summary>
    public float Velocity { get; init; }
    /// <summary>"distance" (m_fDistance): how far the target is set from the start, and the flight's 2D stop distance.</summary>
    public float Distance { get; init; }
    /// <summary>"direction" (m_ptDirection), unitized; UNIT_Y by the ctor (magicdata.cpp:216).</summary>
    public Vector3 Direction { get; init; } = Vector3.UnitY;
    /// <summary>"controlValue0" / "controlValue1" (m_ptControl): the range of the random control point
    /// (CMagicData::FUN_14145c030), and "controlEndOffsetValue" (m_ptControlEnd) added to the target.</summary>
    public Vector3 Control0 { get; init; }
    public Vector3 Control1 { get; init; }
    public Vector3 ControlEnd { get; init; }
    /// <summary>"controlRate" (m_fControlRate) and "catmullrom" (m_nCatmullRom): the curve's split and kind.</summary>
    public float ControlRate { get; init; }
    public int CatmullRom { get; init; }
    /// <summary>"nonTargetMoveDistance" (m_fNonTargetMoveDistance).</summary>
    public float NonTargetMoveDistance { get; init; }
    /// <summary>"moveEndHoldDuration" (m_nMoveEndHoldDuration), milliseconds.</summary>
    public int MoveEndHoldDuration { get; init; }
    /// <summary>"explosionByDestroy" (XmlGetBool, m_nExplosionByDestroy, 1 in the CMagicData ctor): the end of the magic sends
    /// its splash (CMagic::FUN_14145f570) even without a ray stop or a fill.</summary>
    public bool ExplosionByDestroy { get; init; } = true;
    /// <summary>"ignorePhysxTestInitPosition" (XmlGetBool): skips the start ray of an arrow magic (CMagic::FUN_1414609e0).</summary>
    public bool IgnorePhysxTestInitPosition { get; init; }
    /// <summary>"traceTargetDuration" (m_fTraceTargetDuration), seconds: past it (from the state tick, state 2 on) the magic no
    /// longer follows its target entity (CMagic::UpdateTargetPosition, magicdata.cpp:1459); 0 follows it always.</summary>
    public float TraceTargetDuration { get; init; }
    /// <summary>"piercingAttackInterval" (m_nPiercingAttackInterval, unsigned), milliseconds: how often a piercing arrow
    /// (bounceType 3 and 4) forgets the targets it pierced (CMagic::FUN_141466da0); 0 never.</summary>
    public uint PiercingAttackInterval { get; init; }
    /// <summary>"piercingAttackMaxTargetCount" (m_nPiercingAttackMaxTargetCount): the most targets a piercing arrow hits
    /// between two intervals (CMagic::FUN_141465b30); 0 no cap.</summary>
    public int PiercingAttackMaxTargetCount { get; init; }
    /// <summary>"lookAtType" (m_nLookAtType, magicdata.cpp:346): how the magic scene turns (CMagic::UpdateLookAtRotation): 0
    /// against the direction, 1 the same flattened, 2 against the last move.</summary>
    public int LookAtType { get; init; }
}

/// <summary>
/// The magic paths (LoadMagicPath @0x14145c4d0, magicdata.cpp:302): ./data/xml/table/magicPath.xml, each &lt;type id&gt; a list of
/// its &lt;move&gt; rows; a repeated id replaces the earlier list (SetAt). The default id 0 is then set to one row of zeros, so an
/// attack without a magicPathID still gets a magic. Read on first use (FUN_14145c300 reloads a dirty cache).
/// STILL-OPEN: traceTargetOffsetPos is not read: UpdateTargetPosition uses it for a target without collision parts, which the
/// port's body box stand-in never is (reachable in the client, not here).
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
                    Xml.GetBool(m, "ignoreCancelAtSpawnTime", false))
                {
                    Velocity = Xml.Float(m, "vel"), Distance = Xml.Float(m, "distance"),
                    Direction = SkillHit.Unitize(string.IsNullOrEmpty((string)m.Attribute("direction")) ? Vector3.UnitY : Point3((string)m.Attribute("direction"))),
                    Control0 = Point3((string)m.Attribute("controlValue0")), Control1 = Point3((string)m.Attribute("controlValue1")),
                    ControlEnd = Point3((string)m.Attribute("controlEndOffsetValue")),
                    ControlRate = Xml.Float(m, "controlRate"), CatmullRom = Xml.Int(m, "catmullrom"),
                    NonTargetMoveDistance = Xml.Float(m, "nonTargetMoveDistance"), MoveEndHoldDuration = Xml.Int(m, "moveEndHoldDuration"),
                    IgnorePhysxTestInitPosition = Xml.GetBool(m, "ignorePhysxTestInitPosition", false),
                    ExplosionByDestroy = Xml.GetBool(m, "explosionByDestroy", true),
                    TraceTargetDuration = Xml.Float(m, "traceTargetDuration"),
                    PiercingAttackInterval = Xml.UInt(m, "piercingAttackInterval"),
                    PiercingAttackMaxTargetCount = Xml.Int(m, "piercingAttackMaxTargetCount"),
                    LookAtType = Xml.Int(m, "lookAtType"),
                })
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
