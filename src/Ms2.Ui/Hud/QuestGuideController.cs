using System.Globalization;
using Ms2.Avm2;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIQuestGuideDialog (ms2/client/ms2/uiquesteventlistener.cpp), the C++ side of uiquestguidedialog, the quest tracker: the
/// field mission panel and an entity per tracked quest in progress, sent to the movie's own QuestGuideDialog, which lays
/// them out, pages them and draws their conditions, icons and buttons.
/// </summary>
public sealed class QuestGuideController : UiController
{
    /// <summary>The ctor (@0x141aa97d0) has no SetTweenEnabled: m_c4 stays 0, no open / close tween.</summary>
    protected override bool TweenEnabled => false;

    public override string Movie => "uiquestguidedialog";

    /// <summary>The CommonDepth layer of the movie: UIQuestGuideDialog's ctor sets m_2c to 0xd (uiquesteventlistener.cpp:337).</summary>
    public override int LoadLayer => CommonDepth.LowerBuiltinDialog;

    /// <summary>UIQuestGuideDialog's ctor sets the flag 0 (uiquesteventlistener.cpp:338): vfunc_56, an always-on
    /// one — the quest tracker never blocks the guide's own arrows.</summary>
    public override bool AlwaysOn => true;
    public override string TypeName => "QuestGuideDialog";  // GetTypeName @0x141aa9a90 (uiquesteventlistener.cpp:350)
    protected override string EntityMethod => "buildAll";

    public QuestTable Table;
    /// <summary>The character's quest state (CQuestManager's records).</summary>
    public IQuestState State;
    /// <summary>The field missions (QuestDataTable::GetQuestIdsByMap) and CFieldMetaTable::GetParentField.</summary>
    public FieldMissions Missions;
    public Func<int, int?> ParentField;
    /// <summary>WorldService::GetFieldId.</summary>
    public int FieldId;
    /// <summary>The goto buttons of a quest (SetQuestGotoButtons' checks, QuestCommon); null = the array is not set.</summary>
    public Func<QuestData, QuestRecord, List<string>> GotoButtons;
    /// <summary>CFeatureManager::IsFeatureEnabled by id.</summary>
    public Func<int, bool> FeatureOn = _ => true;
    /// <summary>Client::Constants QuestGuidePageCount / QuestGuideMaxCount (Define defaults 4 and 40,
    /// unit_1402ab440_constantsblocks.cpp:917-918).</summary>
    public int PageCount = 4, MaxCount = 40;

    /// <summary>FUN_141275260(id, 1, 1): the system notice of a common string id (AddQuestEntity's 0x5f1, the tracker is full).</summary>
    public Action<int> Notice;

    /// <summary>m_30: the movie is up.</summary>
    public bool Loaded => Host?.Entity != null;

    bool missionToggle;     // m_bMissionToggle
    int recommendQuest;     // m_nRecommendQuest
    readonly List<int> addQueue = new(), removeQueue = new();      // m_addQueue, m_removeQueue
    bool recommendDirty, rebuild;      // m_bRecommendDirty, m_bRebuild

    /// <summary>
    /// OnLoadComplete (uiquesteventlistener.cpp:391): UIDialog's, setMaxEntity(QuestGuidePageCount, QuestGuideMaxCount),
    /// the saved page (m_pageIndex, none: 0), then BuildAll.
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        Host.Invoke("setMaxEntity", PageCount, MaxCount);
        Host.Set(Host.Entity, "m_pageIndex", 0);
        recommendQuest = 0;
        BuildAll();
    }

    /// <summary>vfunc_43 (uiquesteventlistener.cpp:381): UIDialog's, then ApplyAlignment (:1073) with CAlignmentInfo(6, 0, 330):
    /// top right, 330 down, by the dialog's size.</summary>
    public override void Layout(double stageW, double stageH)
    {
        base.Layout(stageW, stageH);
        Host.Align(Host.Entity, 6, 0, 330);
    }

    /// <summary>
    /// BuildAll (uiquesteventlistener.cpp:730): clearAll, AddMission, then buildAll with every tracked quest in progress
    /// (GetQuestsFiltered(1, 0xb, -1), GetTrackingState) and the recommended quest.
    /// </summary>
    public void BuildAll()
    {
        Host.Invoke("clearAll");
        AddMission();
        var list = new List<object>();
        foreach (var r in State.Records)
        {
            if (r.State != 1 || TrackingState(r) == 0 || !QuestCommon.InMainQuestMap(Table.Quest(r.Id))) continue;
            list.Add(QuestEntity(r));
        }
        recommendQuest = RecommendedQuest();
        Host.Invoke("buildAll", list);
    }

    /// <summary>
    /// AddMission (uiquesteventlistener.cpp:759): outside a dungeon stage, addMission(obj, "field") with FUN_141aaddb0's
    /// conditions and the panel's toggle, when the field has missions.
    /// </summary>
    void AddMission()
    {
        var goals = Missions?.Goals(FieldId, ParentField, State);
        if (goals == null || goals.Count == 0) return;
        var conditions = goals.Select(g => (object)new Dictionary<string, object> { ["text"] = g.Text, ["satisfied"] = g.Satisfied ? 1 : 0 }).ToList();
        Host.Invoke("addMission", Host.Obj(("conditions", conditions), ("toggle", missionToggle ? 1 : 0)), "field");
    }

    // ---- UIQuestEventListener: the quest manager's events (UIQuestEventDispatcher, questmanager_2.cpp:1004-1083) --------------
    // The dispatcher's vfunc_1 / 2 / 3 (@0x141aa88e0, @0x141aa8a30, @0x141aa8b70) send the quest types 3, 4, 5, 7 and 10 to slots
    // of their own (8-12; the tracker's slot 9 updates the dungeon mission, not here); the other types go to slots 1, 2 and 3.
    // A start queued before the movie is up is listed by OnLoadComplete's BuildAll and added again on the next frame; the
    // movie's createEntity takes an entity of the same id off first (hasEntity, removeEntity), so it is not listed twice.

    /// <summary>OnQuest mode 2 (a quest started), vfunc_o178_1 (@0x141aab3b0): a tracked quest is queued for adding; a main
    /// quest (type 0) marks the recommendation dirty.</summary>
    public void OnQuestStarted(int id)
    {
        var d = Table.Quest(id);
        if (!QuestCommon.InMainQuestMap(d) || State.Get(id) is not { } r || TrackingState(r) == 0) return;
        addQueue.Add(id);
        if (d?.Type == 0) recommendDirty = true;
    }

    /// <summary>OnQuest mode 4 (a quest rewarded), vfunc_o178_2 (@0x141aab420): queued for removal; a main quest marks the
    /// recommendation dirty.</summary>
    public void OnQuestRewarded(int id)
    {
        var d = Table.Quest(id);
        if (!QuestCommon.InMainQuestMap(d)) return;
        removeQueue.Add(id);
        if (d?.Type == 0) recommendDirty = true;
    }

    /// <summary>OnQuest mode 3 (a quest's progress), vfunc_o178_3 (@0x141aab480): a tracked quest's entity is updated.</summary>
    public void OnQuestProgress(int id)
    {
        if (!QuestCommon.InMainQuestMap(Table.Quest(id)) || State.Get(id) is not { } r || TrackingState(r) == 0) return;
        UpdateQuestEntity(r);
    }

    /// <summary>OnQuest mode 6 or 7 (a quest removed or expired), vfunc_o178_5 (@0x141aab510): its entity goes now; a main
    /// quest marks the recommendation dirty.</summary>
    public void OnQuestRemoved(int id)
    {
        RemoveQuestEntity(id);
        if (Table.Quest(id)?.Type == 0) recommendDirty = true;
    }

    /// <summary>OnQuest mode 9 (the tracking flag), vfunc_o178_6 (@0x141aab570): unless the tracker is closed (vfunc_55), a
    /// tracked quest is added, an untracked one removed.</summary>
    public void OnQuestTracking(int id)
    {
        if (IsClosed || State.Get(id) is not { } r) return;
        if (TrackingState(r) != 0) AddQuestEntity(id);
        else RemoveQuestEntity(id);
    }

    /// <summary>OnQuest mode 5, vfunc_o178_7 (@0x141aab4d0): AddQuestEntity of the id. With result 0 the quest manager has
    /// just dropped the quest (RemoveQuest(id, 1), questmanager_2.cpp:1050-1053), so AddQuestEntity finds nothing
    /// (uiquesteventlistener.cpp:805) and an entity already listed stays, as in the client. A give-up is answered with
    /// mode 6 (<see cref="OnQuestRemoved"/>).</summary>
    public void OnQuestResult(int id, int result) => AddQuestEntity(id);

    /// <summary>FUN_141aacf20: the whole list is built again on the next frame.</summary>
    public void QueueRebuild() => rebuild = true;

    /// <summary>
    /// vfunc_15 (@0x141aaa450), each frame in the game scene (sub scene 0x620001d) with the movie up: the removals, the
    /// recommendation, the additions, then a queued rebuild. RefreshRecommendedQuest is not ported (no recommended quest is
    /// picked here, see <see cref="RecommendedQuest"/>): the dirty flag is only cleared.
    /// </summary>
    public void Tick()
    {
        if (!Loaded) return;
        if (removeQueue.Count > 0)
        {
            foreach (int id in removeQueue) RemoveQuestEntity(id);
            removeQueue.Clear();
        }
        recommendDirty = false;
        if (addQueue.Count > 0)
        {
            foreach (int id in addQueue) AddQuestEntity(id);
            addQueue.Clear();
        }
        if (rebuild)
        {
            rebuild = false;
            BuildAll();
        }
    }

    /// <summary>
    /// AddQuestEntity (@0x141aac190): a quest the character has and is not rewarded; with the tracker full (canAdd) only a
    /// forced one (FUN_14074b790), else the notice 0x5f1; then addEntity with its entity.
    /// </summary>
    bool AddQuestEntity(int id)
    {
        if (State.Get(id) is not { } r || r.IsRewarded) return false;
        if (!CanAdd() && !Table.ForceRegist(id))
        {
            Notice?.Invoke(0x5f1);
            return false;
        }
        if (!Loaded) return true;
        Host.Invoke("addEntity", QuestEntity(r));
        return true;
    }

    /// <summary>CanAdd (@0x141aacd70): the movie's canAdd; false without the movie.</summary>
    bool CanAdd() => Loaded && Rt.ToBoolean(Host.Invoke("canAdd"));

    /// <summary>RemoveQuestEntity (@0x141aac360): the movie's removeEntity with the id.</summary>
    void RemoveQuestEntity(int id)
    {
        if (Loaded) Host.Invoke("removeEntity", (uint)id);
    }

    /// <summary>
    /// UpdateQuestEntity (@0x141aabe50): updateEntity with the id, satisfied, progress 1, the icon, the title, then the goto
    /// buttons, conditions, period and navigation state as the entity has them.
    /// </summary>
    void UpdateQuestEntity(QuestRecord r)
    {
        if (!Loaded) return;
        var d = Table.Quest(r.Id);
        var o = new Dictionary<string, object>
        {
            ["id"] = (uint)r.Id,
            ["satisfied"] = r.Complete ? 1 : 0,
            ["progress"] = 1,
            ["icon"] = QuestIcon(d, r),
            ["title"] = NameWithLevel(r.Id),
        };
        EntityTail(o, d, r);
        Host.Invoke("updateEntity", o);
    }

    /// <summary>CQuest::GetTrackingState: a forced quest (FUN_14074b790) is always tracked, else the record's flag.</summary>
    int TrackingState(QuestRecord r) => Table.ForceRegist(r.Id) ? 1 : r.Tracking;

    /// <summary>FUN_141aad6b0 (uiquesteventlistener.cpp:1038): one tracked quest's entity.</summary>
    Dictionary<string, object> QuestEntity(QuestRecord r)
    {
        var d = Table.Quest(r.Id);
        var o = new Dictionary<string, object>
        {
            ["id"] = (uint)r.Id,
            ["type"] = d?.Type ?? 0,
            ["repeat"] = d != null && d.Repeat != 0 ? 1 : 0,
            ["satisfied"] = r.Complete ? 1 : 0,
            ["progress"] = 1,
            ["icon"] = QuestIcon(d, r),
            ["title"] = NameWithLevel(r.Id),
            ["chapterName"] = ChapterName(r.Id),
            ["questIndex"] = QuestIndex(d?.Chapter ?? 0, r.Id),
            ["acceptedDate"] = (double)r.StartTime,
            ["forceRegistGuide"] = Table.ForceRegist(r.Id) && d != null && d.Fields.Contains((uint)FieldId),
        };
        EntityTail(o, d, r);
        return o;
    }

    /// <summary>The members FUN_141aad6b0 and UpdateQuestEntity both end with: SetQuestGotoButtons, SetQuestConditions,
    /// SetQuestPeriod, SetQuestNavigationState.</summary>
    void EntityTail(Dictionary<string, object> o, QuestData d, QuestRecord r)
    {
        // "alliance" (GetAllianceSymbol, questmasktable.cpp:562: the fame table's symbol) is not sent: no fame table here
        // SetQuestGotoButtons (:897): the quest dialog's goto buttons without the give-up one
        if (GotoButtons?.Invoke(d, r) is { } buttons) o["gotoButton"] = buttons.Where(b => b != "giveup").Cast<object>().ToList();
        o["conditions"] = Conditions(d, r);
        if (d != null && QuestCommon.IsTimeLimited(d) && r.Expired)
        {
            // SetQuestPeriod (:857): an expired quest; the end time of a running one needs the server time (not here)
            o["period"] = Table.Common("s_quest_end_time_expired") ?? "";
            o["expired"] = 1;
        }
        // SetQuestNavigationState (:883): feature 0x77 gives the quest's navigation flag and whether the navigator traces it
        o["navi"] = 0;
        o["tracing"] = 0;
    }

    /// <summary>SetQuestConditions (uiquesteventlistener.cpp:833): each condition's text and whether its goal is reached,
    /// then the complete text (satisfied 0) for a completed quest.</summary>
    List<object> Conditions(QuestData d, QuestRecord r)
    {
        var l = new List<object>();
        if (d == null) return l;
        for (int i = 0; i < d.Conditions.Count; i++)
        {
            long progress = r.Progress != null && i < r.Progress.Length ? r.Progress[i] : 0;
            l.Add(new Dictionary<string, object> { ["text"] = Table.ConditionText(r.Id, i, State), ["satisfied"] = progress >= d.Conditions[i].Goal });
        }
        if (r.Complete) l.Add(new Dictionary<string, object> { ["text"] = Table.CompleteText(r.Id), ["satisfied"] = 0 });
        return l;
    }

    /// <summary>GetQuestIcon (uiquesteventlistener.cpp:1028): a complete quest's SelectStringByQuestId("epic", "satisfied",
    /// "repeat", "event") by its kind (cinematiccomponent.cpp:810); "progress" otherwise.</summary>
    static string QuestIcon(QuestData d, QuestRecord r)
    {
        if (!r.Complete) return "progress";
        return d?.Type switch { 0 => "epic", 2 => "event", 1 or 6 => d.Repeat != 0 ? "repeat" : "satisfied", _ => "satisfied" };
    }

    /// <summary>GetQuestNameWithLevel (questuidata.cpp:256): s_quest_name_with_level with GetQuestName and the quest level.</summary>
    string NameWithLevel(int id)
    {
        string name = Table.QuestName(id);
        var d = Table.Quest(id);
        return d == null ? name : ClientFormat.Format(Table.Common("s_quest_name_with_level") ?? "{0}", name, d.Level);
    }

    /// <summary>
    /// GetQuestChapterName (uiquesteventlistener.cpp:964): "[kind] chapter", the kind by quest type: common strings 1501-1503
    /// (s_quest_main_quest, s_quest_sub_quest, s_quest_event_quest, commonstringtable.cpp:3114-3116), s_quest_guild_quest,
    /// s_quest_field_quest, s_quest_alliance_quest.
    /// </summary>
    string ChapterName(int id)
    {
        var d = Table.Quest(id);
        if (d == null) return "";
        string kind = d.Type switch
        {
            0 => "s_quest_main_quest", 1 => "s_quest_sub_quest", 2 => "s_quest_event_quest", 6 => "s_quest_guild_quest",
            8 => "s_quest_field_quest", 9 => "s_quest_alliance_quest", _ => null,
        } is { } key ? Table.Common(key) ?? "" : "";
        return ClientFormat.Format("[{0}] {1}", kind, Table.ChapterText(d.Chapter)?.Name ?? "");
    }

    /// <summary>
    /// FUN_141aad4d0 (uiquesteventlistener.cpp:1019): "{index}/{count}": the quest's place among the character's quests of
    /// the chapter by start time (GetQuestIndexInChapter, -1 when absent) over the chapter's quest count.
    /// </summary>
    string QuestIndex(int chapter, int id)
    {
        int count = Table.QuestIdsByChapter(chapter).Count;
        int index = -1, n = 0;
        foreach (var q in State.Records.Where(q => Table.Quest(q.Id) is { } qd && qd.Chapter == chapter && QuestCommon.InMainQuestMap(qd)).OrderBy(q => q.StartTime))
        {
            ++n;
            if (q.Id == id) { index = n; break; }
        }
        return index.ToString(CultureInfo.InvariantCulture) + "/" + count.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// FUN_141aae6d0 (uiquesteventlistener.cpp:1192): with feature 0xd0, none while any quest is in progress or none is
    /// rewarded. The pick among the rewarded ones (FUN_141aa4980's time, GetNextQuestId) is not ported: none.
    /// </summary>
    int RecommendedQuest()
    {
        if (!FeatureOn(0xd0)) return 0;
        if (State.Records.Any(r => r.State == 1)) return 0;
        if (!State.Records.Any(r => r.State == 2)) return 0;
        return 0;
    }

    /// <summary>
    /// The guide's go-to buttons (uiquesteventlistener.cpp:509-553): onMoveField, onMoveDungeon, onMoveReEntry, onClickDispatch,
    /// onClickPartyFind and onClickPartyFindCancel with the quest id. <see cref="QuestGotoRequests.Handle"/> sends them.
    /// </summary>
    public event Action<string, int> Goto;

    /// <summary>vfunc_28 (uiquesteventlistener.cpp:456): the movie's calls.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        switch (name)
        {
            case "onFieldMissionToggle":
                if (args.Length > 0) missionToggle = args[0] is bool b && b;
                return false;
            case "onMoveField": case "onMoveDungeon": case "onMoveReEntry": case "onClickDispatch": case "onClickPartyFind":
            case "onClickPartyFindCancel":
                // :509-553: SendQuestMode12 / 13 / 19, FUN_1416cf870, FUN_1416cf980(id, 0 / 1) with PopUInt of the first argument
                if (args.Length > 0) Goto?.Invoke(name, (int)Ms2.Avm2.Rt.ToUint32(args[0]));
                return false;
            case "onClose": case "onTrace": case "onEntityOverflow": case "onLinkQuestDialog": case "onLinkFieldMission":
                return false;       // the dialogs they open are not here
        }
        return false;
    }
}
