namespace Ms2.Quest;

public enum TraceKind { None = 0, Npc = 1, NpcDone = 2, InteractObject = 3, Portal = 4, Liftable = 5 }

/// <summary>A portal of the current field as the trace sees it (a shown field object of type 6).</summary>
public readonly record struct TracePortal(int TargetField, double X, double Y, double Z, bool Shown = true);

/// <summary>A point a quest points at inside the current field (MS2 units, Z up), or none.</summary>
public readonly record struct TraceTarget(TraceKind Kind, double X, double Y, double Z);

/// <summary>QuestNavigation::TraceResult: what the guide shows.</summary>
public sealed class TraceResult
{
    public TraceKind Kind;
    public double X, Y, Z;              // the point the beam aims at (m_20)
    public double BeamYawDeg;           // the beam effect's Z rotation (m_2c.z; x and y are 0)
    public string DistanceText = "";    // m_38
    /// <summary>m_40: the trace context the result was made with (null = none), set when CQuestNavigator::TraceQuestTarget (@0x140c9a4c0, questnavigator.cpp:407) finds a target.</summary>
    public TraceContext Context;
}

/// <summary>
/// QuestNavigation::TraceContext (cquestnavigationmap.h:24): what the tracked quests lead to on the field, rebuilt by
/// CQuestNavigator::RebuildTraceContext (questnavigator.cpp:420). CQuestNavigator::Context() is its m_70.
/// </summary>
public sealed class TraceContext
{
    /// <summary>m_18: the map the sets below are for.</summary>
    public int MapId;
    /// <summary>m_20 (set&lt;unsigned&gt;): the npc ids.</summary>
    public readonly HashSet<int> NpcIds = new();
    /// <summary>m_30 (set&lt;int&gt;): the interact object row ids.</summary>
    public readonly HashSet<int> InteractIds = new();
    /// <summary>m_40 (set&lt;unsigned&gt;): the liftable ids.</summary>
    public readonly HashSet<int> LiftableIds = new();
}

/// <summary>The strings the distance text uses (stringcommon s_quest_navigation_*).</summary>
public sealed class TraceStrings
{
    public string Dest = "Arrived";
    public string DistanceKm = "{0} km";
    public string DistanceM = "{0} m";
    public string Initializing = "Tracking";
    public static TraceStrings Default => new();
}

/// <summary>CQuestNavigator::TraceQuestTarget, the part after the quest's target map is known, plus the beam and the distance text.</summary>
public static class QuestTrace
{
    /// <summary>
    /// Same map: the target point inside it (the caller found the nearest npc / interact object / liftable; none = the quest cannot
    /// be traced). Another map: the first shown portal of the field whose target is anywhere on the map path, else the portal toward
    /// the next map on it. Returns null when the guide has nothing to show.
    /// </summary>
    public static TraceResult Trace(QuestMapGraph graph, int currentMap, int targetMap, TraceTarget? inField,
        IReadOnlyList<TracePortal> portals, (double x, double y, double z) player, TraceStrings strings = null)
    {
        strings ??= TraceStrings.Default;
        TraceKind kind = TraceKind.None;
        double x = 0, y = 0, z = 0;
        if (targetMap == currentMap)
        {
            if (inField is { } t && t.Kind != TraceKind.None) { kind = t.Kind; (x, y, z) = (t.X, t.Y, t.Z); }
        }
        else
        {
            var path = graph.FindMapPath(currentMap, targetMap);
            if (path != null)
            {
                foreach (var p in portals)
                {
                    if (!p.Shown || !path.Contains(p.TargetField)) continue;
                    kind = TraceKind.Portal; (x, y, z) = (p.X, p.Y, p.Z);
                    break;
                }
                if (kind == TraceKind.None)
                {
                    int next = graph.GetNextMapOnPath(currentMap, targetMap);
                    if (next != 0)
                        foreach (var p in portals)
                            if (p.TargetField == next && p.Shown) { kind = TraceKind.Portal; (x, y, z) = (p.X, p.Y, p.Z); break; }
                }
            }
        }
        if (kind == TraceKind.None) return null;
        return new TraceResult
        {
            Kind = kind, X = x, Y = y, Z = z,
            BeamYawDeg = BeamYaw(player, (x, y, z)),
            DistanceText = DistanceText(Distance(player, (x, y, z)), strings),
        };
    }

    /// <summary>FUN_140c9b0a0 -> FUN_140a0b080(player, target, +Z): the look-at Euler angles of (player - target) are pure yaw,
    /// atan2(-dy, dx) in degrees, plus 90.</summary>
    public static double BeamYaw((double x, double y, double z) player, (double x, double y, double z) target)
    {
        double fx = player.x - target.x, fy = player.y - target.y;
        return Math.Atan2(-fy, fx) * 180.0 / Math.PI + 90.0;
    }

    public static double Distance((double x, double y, double z) a, (double x, double y, double z) b)
    {
        double dx = a.x - b.x, dy = a.y - b.y, dz = a.z - b.z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>GetDistanceText: whole units (cm) truncated; over 100000 km, 100..99999 metres, else the "arrived" text
    /// (exactly 100000 falls through to it as well).</summary>
    public static string DistanceText(double dist, TraceStrings s)
    {
        int n = (int)dist;
        if (n > 100000) return string.Format(s.DistanceKm, n / 100000);
        if (n < 100000 && n >= 100) return string.Format(s.DistanceM, n / 100);
        return s.Dest;
    }
}
