namespace Ms2.Quest;

/// <summary>
/// CQuestNavigator (questnavigator.cpp): the tracked quest the guide beam points at. It keeps the selected quest
/// (<see cref="Result"/>.QuestId, saved in the game option Etc m_n30), re-traces it each frame
/// (FUN_140c99ee0 -> TraceQuestTarget) and rebuilds <see cref="Context"/>, the targets of every active quest in the
/// current field, after a quest event (m_68).
/// <para>
/// The world is the host's: the field id, the local player's position, the nearest npc / interact object / liftable of an
/// id set (WorldService::FindNearest*), the field's portals and CanShowBeam. Feature 0x77 is taken as on.
/// </para>
/// </summary>
public sealed class QuestNavigator
{
    /// <summary>The nearest object of an id set: its position and distance, or null when none is in the field.</summary>
    public delegate (double X, double Y, double Z, float Dist)? FindNearest(HashSet<int> ids);

    readonly QuestTable table;
    readonly IQuestState quests;
    /// <summary>GetQuestNavigationMap(); null while the map paths are not built (IsPathsBuilt 0).</summary>
    public QuestNavigationMap Map;

    public Func<int> FieldId = () => 0;
    /// <summary>The local player's position (PCBase vfunc_18), null when there is none.</summary>
    public Func<(double X, double Y, double Z)?> PlayerPosition = () => null;
    public FindNearest NearestNpc = _ => null;
    public FindNearest NearestInteract = _ => null;
    public FindNearest NearestLiftable = _ => null;
    /// <summary>GetFieldObjectsByType(6): the field's portals in their order.</summary>
    public Func<IReadOnlyList<TracePortal>> Portals = () => Array.Empty<TracePortal>();
    /// <summary>CanShowBeam (@0x140c9af60): the field state, a local player, not riding without a beam, not hidden.</summary>
    public Func<bool> CanShowBeam = () => false;
    /// <summary>CGameOption Etc m_n30, the tracked quest id: read on NotifyCreateMyPC, written by SelectQuest.</summary>
    public Func<int> LoadTracked = () => 0;
    public Action<int> SaveTracked = _ => { };
    public TraceStrings Strings = TraceStrings.Default;

    /// <summary>m_18: the trace result.</summary>
    public readonly TraceResult Result = new();
    /// <summary>m_70: the targets of the active quests in the current field.</summary>
    public readonly TraceContext Context = new();
    /// <summary>m_10 is loaded (NotifyCompleteMyPC): the frame traces only then.</summary>
    public bool HasBeam { get; private set; }
    /// <summary>m_10's culling: the beam is up.</summary>
    public bool BeamShown { get; private set; }
    /// <summary>m_68: rebuild the context next frame.</summary>
    bool dirty;
    int tracked;
    // TraceQuest's record and context, kept between quest events: the client re-traces each frame, but the trace reads only
    // the record (changed by the quest events), the field and the map, so a frame between events gets the same context
    bool traceStale = true;
    int naviQuest = -1;
    bool naviUsed;
    int tracedQuest, tracedField;
    QuestNavigationMap tracedMap;
    QuestRecord tracedRecord;
    TraceContext tracedContext;
    List<int> tracedPath;       // FindMapPath(current, context field) for a target in another field, with its second field
    int tracedNext;
    // the last distance text by its value bucket, so an unchanged distance makes no new string
    int distanceKey = int.MinValue;
    string distanceText;
    TraceStrings distanceStrings;

    public QuestNavigator(QuestTable table, IQuestState quests)
    {
        this.table = table;
        this.quests = quests;
    }

    /// <summary>CQuest::GetTrackingState: a forced quest is always tracked, else the record's flag.</summary>
    int TrackingState(QuestRecord r) => table.Quest(r.Id) is { ForceRegist: true } ? 1 : r.Tracking;

    bool UseNavi(int questId) => table.Quest(questId) is { UseNavi: true };

    public bool IsSelected(int questId) => Result.QuestId == questId;

    /// <summary>SelectQuest (@0x140c9a420).</summary>
    public void SelectQuest(int questId)
    {
        traceStale = true;
        Result.Clear();
        Result.QuestId = questId;
        if (tracked != questId) { tracked = questId; SaveTracked(questId); }
    }

    /// <summary>vfunc_o178_1 (@0x140c99c40): a quest was accepted or changed; with none selected, a tracked navi quest is.</summary>
    public void OnQuestAccepted(QuestRecord q)
    {
        dirty = traceStale = true;
        if (IsSelected(0) && TrackingState(q) != 0 && UseNavi(q.Id)) SelectQuest(q.Id);
    }

    /// <summary>vfunc_o178_2 (@0x140c99d20) and vfunc_o178_5 (@0x140c99da0): the quest is dropped from the beam.</summary>
    public void OnQuestDropped(int questId)
    {
        dirty = traceStale = true;
        if (IsSelected(questId)) SelectQuest(0);
    }

    /// <summary>vfunc_o178_3 (@0x140c99d80) and vfunc_o178_7 (@0x140c99e00): progress changed.</summary>
    public void OnQuestProgress() => dirty = traceStale = true;

    /// <summary>vfunc_o178_6 (@0x140c99be0): the selected quest is dropped when it is no longer tracked.</summary>
    public void OnQuestTracking(QuestRecord q)
    {
        traceStale = true;
        if (IsSelected(q.Id) && TrackingState(q) == 0) SelectQuest(0);
    }

    /// <summary>OnCreateMyPC (@0x140c9a0e0): restore the tracked quest from the option when it is still tracked.</summary>
    public void OnCreateMyPc()
    {
        traceStale = true;
        Result.Clear();
        Result.QuestId = 0;
        tracked = LoadTracked();
        if (quests.Get(tracked) is { } q && TrackingState(q) != 0) Result.QuestId = tracked;
    }

    /// <summary>OnCompleteMyPC (@0x140c9a1c0): the beam is made (culled) and the context rebuilt.</summary>
    public void OnCompleteMyPc()
    {
        HasBeam = true;
        BeamShown = false;
        RebuildTraceContext();
    }

    /// <summary>RebuildTraceContext (@0x140c9ac80): every active quest's targets in the current field.</summary>
    public void RebuildTraceContext()
    {
        Context.NpcIds.Clear();
        Context.InteractIds.Clear();
        Context.LiftableIds.Clear();
        if (Map == null) return;
        int field = FieldId();
        foreach (var r in quests.Records)
            if (r.State == 1) Map.AddFieldTargets(r, field, Context);
    }

    /// <summary>FUN_140c99ee0, each frame: re-trace the selected quest while the beam exists, then the context when dirty.</summary>
    public void Frame()
    {
        if (PlayerPosition() == null) return;
        if (HasBeam)
        {
            Result.Clear();
            int failed = TraceQuestTarget();
            BeamShown = failed == 0 && CanShowBeam();
        }
        if (dirty)
        {
            dirty = false;
            RebuildTraceContext();
        }
    }

    /// <summary>
    /// TraceQuestTarget (@0x140c9a4c0): 0 with <see cref="Result"/> filled, 1 when the quest cannot be navigated, 2 while
    /// the paths are not built. Same field: the nearest npc (kind 2 once the quest is complete), interact object or
    /// liftable, the closest kind winning (ties keep the earlier). Another field: the first shown portal whose target is on
    /// the path, else the first portal to the next field on it when that is shown.
    /// </summary>
    public int TraceQuestTarget()
    {
        if (Map == null)
        {
            Result.DistanceText = Strings.Initializing;
            return 2;
        }
        int questId = Result.QuestId;
        if (questId != naviQuest)                       // the table's SortedDictionary lookup allocates on .NET 8
            (naviQuest, naviUsed) = (questId, UseNavi(questId));
        if (!naviUsed) return 1;
        int current = FieldId();
        if (traceStale || tracedQuest != questId || tracedField != current || tracedMap != Map)
        {
            traceStale = false;
            (tracedQuest, tracedField, tracedMap) = (questId, current, Map);
            tracedRecord = quests.Get(questId);
            tracedContext = Map.TraceQuest(tracedRecord, current);
            tracedPath = tracedContext != null && tracedContext.MapId != current ? Map.Graph.FindMapPath(current, tracedContext.MapId) : null;
            tracedNext = tracedPath is { Count: > 1 } ? tracedPath[1] : 0;     // GetNextMapOnPath over the same path
        }
        var record = tracedRecord;
        var context = tracedContext;
        if (context == null) return 1;
        int failed = 1;
        if (context.MapId == current)
        {
            float best = float.MaxValue;
            if (NearestNpc(context.NpcIds) is { } n) Take(n, record.Complete ? TraceKind.NpcDone : TraceKind.Npc);
            if (NearestInteract(context.InteractIds) is { } i) Take(i, TraceKind.InteractObject);
            if (NearestLiftable(context.LiftableIds) is { } l) Take(l, TraceKind.Liftable);

            void Take((double X, double Y, double Z, float Dist) hit, TraceKind kind)
            {
                failed = 0;
                if (!(hit.Dist < best)) return;
                best = hit.Dist;
                Result.Kind = kind;
                (Result.X, Result.Y, Result.Z) = (hit.X, hit.Y, hit.Z);
            }
        }
        else if (tracedPath is { } path)
        {
            var portals = Portals();
            bool found = false;
            for (int k = 0; k < portals.Count; k++)
            {
                var p = portals[k];
                if (!p.Shown || !path.Contains(p.TargetField)) continue;
                SetPortal(p);
                found = true;
                break;
            }
            if (!found)
            {
                int next = tracedNext;
                if (next != 0)
                    for (int k = 0; k < portals.Count; k++)
                        if (portals[k] is var p && p.TargetField == next)
                        {
                            // FindPortalByDestMap: the first portal to the field, then IsShown
                            if (p.Shown) SetPortal(p);
                            break;
                        }
            }

            void SetPortal(TracePortal p)
            {
                Result.Kind = TraceKind.Portal;
                (Result.X, Result.Y, Result.Z) = (p.X, p.Y, p.Z);
                failed = 0;
            }
        }
        if (failed == 0 && PlayerPosition() is { } me)
        {
            var target = (Result.X, Result.Y, Result.Z);
            Result.BeamYawDeg = QuestTrace.BeamYaw(me, target);
            Result.DistanceText = DistanceText(QuestTrace.Distance(me, target));
            Result.Context = context;
        }
        return failed;
    }

    /// <summary><see cref="QuestTrace.DistanceText"/>, the string made again only when its number or the strings change.</summary>
    string DistanceText(double dist)
    {
        int n = (int)dist;
        int key = n > 100000 ? -1 - n / 100000 : n >= 100 && n < 100000 ? n / 100 : 0;
        if (key != distanceKey || !ReferenceEquals(Strings, distanceStrings))
        {
            (distanceKey, distanceStrings) = (key, Strings);
            distanceText = QuestTrace.DistanceText(dist, Strings);
        }
        return distanceText;
    }
}
