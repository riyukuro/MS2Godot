namespace Ms2.Quest;

/// <summary>
/// CQuestNavigationMap's trace table (questnavigation.cpp): for every quest, the condition index -> the fields each of
/// its npcs, interact objects and liftables is in (InitTraceTable @0x140c91690), and the trace of an active quest to the
/// nearest of those fields (TraceQuest @0x140c92730). The map paths are <see cref="QuestMapGraph"/>.
/// <para>
/// InitTraceTable's feature 0x77 and micro-game (FUN_1412755a0) skips are not modelled: the table is always built.
/// </para>
/// </summary>
public sealed class QuestNavigationMap
{
    /// <summary>TraceTarget (m_targets[3]): kind (0 npc, 1 interact, 2 liftable) -> field -> ids, in the order added.</summary>
    public sealed class Target
    {
        public readonly Dictionary<int, List<int>>[] ByKind = { new(), new(), new() };

        /// <summary>AddMaps (@0x140c93490): the id goes into each field's list.</summary>
        public void AddMaps(int kind, int id, IEnumerable<int> maps)
        {
            foreach (int map in maps)
            {
                if (!ByKind[kind].TryGetValue(map, out var ids)) ByKind[kind][map] = ids = new List<int>();
                ids.Add(id);
            }
        }

        /// <summary>HasTargets (@0x140c93430).</summary>
        public bool HasTargets => ByKind[0].Count > 0 || ByKind[1].Count > 0 || ByKind[2].Count > 0;
    }

    readonly QuestTable table;
    readonly FieldMeta meta;
    readonly QuestMapGraph graph;
    /// <summary>m_98: quest id -> condition index -> target.</summary>
    readonly Dictionary<int, Dictionary<int, Target>> traceTable = new();
    static readonly List<int> NoFields = new();

    public QuestNavigationMap(QuestTable table, FieldMeta meta, QuestMapGraph graph)
    {
        this.table = table;
        this.meta = meta;
        this.graph = graph;
        InitTraceTable();
    }

    public QuestMapGraph Graph => graph;

    /// <summary>The targets of a quest's condition, null when it has none.</summary>
    public Target TargetOf(int questId, int condition) =>
        traceTable.TryGetValue(questId, out var t) && t.TryGetValue(condition, out var c) ? c : null;

    /// <summary>ClassifyCondition (@0x140c92620): 0 npc, 1 interact object, 2 liftable, 3 none.</summary>
    public static int ClassifyCondition(int type) => type switch
    {
        0xc or 0x1a or 0x1b => 0,
        0x1d or 0x1e => 1,
        0x9c => 2,
        _ => 3,
    };

    /// <summary>FindConditionMaps (@0x140c92510): a type 0xc condition with target ids names its fields.</summary>
    static List<int> FindConditionMaps(QuestCondition c)
    {
        if (c.Type != 0xc || c.Target.Ids.Count == 0) return null;
        var maps = new List<int>(c.Target.Ids.Count);
        foreach (long id in c.Target.Ids) maps.Add((int)id);
        return maps;
    }

    List<int> Fields(int kind, int id)
    {
        var from = kind == 0 ? meta.NpcFields : kind == 1 ? meta.InteractFields : meta.LiftableFields;
        return from.TryGetValue(id, out var l) ? l : NoFields;
    }

    void InitTraceTable()
    {
        foreach (var q in table.Quests)
        {
            var conditions = new Dictionary<int, Target>();
            traceTable[q.Id] = conditions;
            for (int c = 0; c < q.Conditions.Count; c++)
            {
                var target = new Target();
                if (q.Navigation.TryGetValue(c, out var navi))
                {
                    foreach (var n in navi)
                    {
                        if (n.Kind > 2) continue;
                        foreach (uint id in n.Ids)
                        {
                            if (n.Maps.Length != 0)
                                foreach (uint map in n.Maps) target.AddMaps(n.Kind, (int)id, new[] { (int)map });
                            else target.AddMaps(n.Kind, (int)id, Fields(n.Kind, (int)id));
                        }
                    }
                }
                else
                {
                    var cond = q.Conditions[c];
                    int kind = ClassifyCondition(cond.Type);
                    if (kind <= 2)
                        foreach (long id in cond.Code.Ids)
                            target.AddMaps(kind, (int)id, FindConditionMaps(cond) ?? Fields(kind, (int)id));
                }
                if (target.HasTargets) conditions[c] = target;
            }
        }
    }

    /// <summary>CQuest::IsConditionComplete: an active quest whose count for the condition reached its goal.</summary>
    static bool ConditionComplete(QuestRecord r, QuestData d, int i) =>
        r.State == 1 && r.Progress != null && i < r.Progress.Length && i < d.Conditions.Count && r.Progress[i] >= d.Conditions[i].Goal;

    /// <summary>
    /// TraceQuest (@0x140c92730): for a complete quest, its complete npc in the nearest field it is in; else the ids of
    /// every unfinished condition merged per field, and the context of the nearest of those fields. Null when the quest is
    /// not active, has no data or no field is reachable from <paramref name="currentField"/>.
    /// </summary>
    public TraceContext TraceQuest(QuestRecord record, int currentField)
    {
        if (record == null || !record.IsActive) return null;
        var data = table.Quest(record.Id);
        if (data == null) return null;
        if (record.Complete)
        {
            var done = new TraceContext();
            done.NpcIds.Add(data.CompleteNpc);
            done.MapId = graph.NearestMap(currentField, Fields(0, data.CompleteNpc));
            return done;
        }
        if (!traceTable.TryGetValue(record.Id, out var conditions)) return null;
        var maps = new List<int>();
        var contexts = new Dictionary<int, TraceContext>();
        for (int c = 0; c < data.Conditions.Count; c++)
        {
            if (ConditionComplete(record, data, c) || !conditions.TryGetValue(c, out var target)) continue;
            for (int kind = 0; kind < 3; kind++)
                foreach (var (map, ids) in target.ByKind[kind])
                {
                    if (!contexts.TryGetValue(map, out var ctx))
                    {
                        contexts[map] = ctx = new TraceContext { MapId = map };
                        maps.Add(map);
                    }
                    Merge(ctx, kind, ids);
                }
        }
        return contexts.TryGetValue(graph.NearestMap(currentField, maps), out var nearest) ? nearest : null;
    }

    /// <summary>FUN_140c92fc0: the ids of an active quest's unfinished conditions that are in <paramref name="mapId"/>, merged into <paramref name="context"/>.</summary>
    public void AddFieldTargets(QuestRecord record, int mapId, TraceContext context)
    {
        if (record == null || !record.IsActive) return;
        var data = table.Quest(record.Id);
        if (data == null || !traceTable.TryGetValue(record.Id, out var conditions)) return;
        for (int c = 0; c < data.Conditions.Count; c++)
        {
            if (ConditionComplete(record, data, c) || !conditions.TryGetValue(c, out var target)) continue;
            for (int kind = 0; kind < 3; kind++)
                if (target.ByKind[kind].TryGetValue(mapId, out var ids)) Merge(context, kind, ids);
        }
    }

    static void Merge(TraceContext ctx, int kind, List<int> ids)
    {
        var into = kind == 0 ? ctx.NpcIds : kind == 1 ? ctx.InteractIds : ctx.LiftableIds;
        foreach (int id in ids) into.Add(id);
    }
}
