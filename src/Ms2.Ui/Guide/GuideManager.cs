using System.Xml.Linq;
using Ms2.Net.Packets;

namespace Ms2.Ui.Guide;

/// <summary>
/// The guide (CGuideManager, client/ms2/guideentity_1b.cpp and the limit counts of guideaction.cpp): the guide events of
/// the guide tables, the saved progress the server keeps, the triggers of one update and the limits the running events
/// hold.
/// <para>
/// Every 250 ms (<see cref="Tick"/>) the occurrences raised since the last update queue the events indexed under them
/// (<see cref="ProcessOccurrences"/>), and each queued event takes a step of its state machine
/// (<see cref="UpdateEntities"/>); an event leaves the queue when it is cleared or a failed trigger condition stops it,
/// and comes back when an occurrence it waits on is raised. The triggers last one update.
/// </para>
/// <para>
/// The entities and both progress maps are CRBMaps keyed by the unsigned id, so they walk in ascending id order (the
/// order SendStates writes). Not ported: the guide xml reload on a file change (m_caches, a development build feature),
/// TestEntity and TestCondition (they print the conditions' debug text, vfunc_5 / vfunc_7).
/// </para>
/// </summary>
public sealed class GuideManager
{
    sealed class TriggerGroup
    {
        public readonly SortedSet<int> Values = new(), SubValues = new();
    }

    sealed class OccurContainer
    {
        public readonly SortedDictionary<int, List<GuideEntity>> ByValue = new();
        public readonly List<GuideEntity> All = new();
    }

    static readonly IComparer<int> Unsigned = Comparer<int>.Create((a, b) => ((uint)a).CompareTo((uint)b));

    public GuideWorld World { get; }

    /// <summary>m_n20: the tick of the last update.</summary>
    uint lastTick;
    /// <summary>m_nDebug: logs every trigger and every open and close.</summary>
    public bool Debug;
    /// <summary>m_bits: the byte ReceiveStates reads after the steps (a 1-byte bitset; STILL-OPEN what reads it).</summary>
    public byte RecordBits { get; private set; }
    /// <summary>m_n24 (GetStateChanged): the saved steps changed since the last <see cref="SendStates"/>.</summary>
    public bool StateChanged { get; private set; }

    readonly SortedDictionary<int, GuideEntity> entities = new(Unsigned);
    /// <summary>m_map60: save id → the highest saved step.</summary>
    readonly SortedDictionary<int, int> steps = new(Unsigned);
    /// <summary>m_map90: event id → 1 cleared (0 after a reset).</summary>
    readonly SortedDictionary<int, int> cleared = new(Unsigned);
    readonly Dictionary<GuideTrigger, TriggerGroup> triggers = new();
    /// <summary>m_names: the load errors, shown at the next update with an active window.</summary>
    readonly List<string> errors = new();

    readonly SortedSet<int> pending = new(), plain = new(), indexed = new();
    readonly List<(int Occurrence, int Sub)> subs = new();
    readonly SortedDictionary<int, OccurContainer> byOccur = new();
    readonly HashSet<int> queuedIds = new();
    readonly LinkedList<GuideEntity> queue = new();
    readonly SortedDictionary<string, int> names = new(StringComparer.Ordinal);

    /// <summary>s_limitCounts[25] and s_dialogLimits (guideaction.cpp): how many running events hold each limit.</summary>
    readonly int[] limitCounts = new int[LimitCount];
    /// <summary>The number of limits (s_limitCounts[25]).</summary>
    public const int LimitCount = 25;
    readonly Dictionary<string, int> dialogLimits = new(StringComparer.Ordinal);

    public GuideManager(GuideWorld world) => World = world;

    /// <summary>FUN_1409331e0: the session's guide; null offline and before the guide tables are loaded. The hooks of the
    /// client's controllers reach it through this (every client call site tests the pointer first).</summary>
    public static GuideManager Current { get; set; }

    /// <summary>The events in id order.</summary>
    public IEnumerable<GuideEntity> Entities => entities.Values;
    public int EntityCount => entities.Count;
    /// <summary>The load errors not shown yet ("Guide Error: [ID:n] text").</summary>
    public IReadOnlyList<string> LoadErrors => errors;
    /// <summary>The queued events in queue order.</summary>
    public IEnumerable<GuideEntity> Queued => queue;
    /// <summary>The occurrence kinds queued for the next update.</summary>
    public IReadOnlyCollection<int> PendingOccurrences => pending;
    /// <summary>The saved steps in save id order.</summary>
    public IEnumerable<KeyValuePair<int, int>> SavedSteps => steps;
    /// <summary>The occurrences the index files an event under with a sub key (m_setIndexed).</summary>
    public IReadOnlyCollection<int> IndexedOccurrences => indexed;

    // ---- loading ---------------------------------------------------------------------------------------------------

    /// <summary>Load (@0x14138b3e0): the events of the table's files in load order (LoadFile @0x14138b4d0), then
    /// PrepareEntities, RefreshEntities, BuildOccurIndex and QueueAllEntities. A bad event or a duplicated id adds a load
    /// error and is left out.</summary>
    public void Load(GuideTable table)
    {
        entities.Clear();
        foreach (var file in table.Files)
        {
            foreach (var e in file.Events)
            {
                var entity = new GuideEntity(this);
                try
                {
                    entity.Load(e);
                    if (entities.ContainsKey(entity.Id))
                        throw new GuideLoadException("Guide Entity is Duplicated");
                    entities[entity.Id] = entity;
                }
                catch (GuideLoadException ex)
                {
                    errors.Add($"Guide Error: [ID:{(uint)entity.Id}] {ex.Message}");
                }
            }
        }
        foreach (var entity in entities.Values) entity.UpdateConditions();
        foreach (var entity in entities.Values) entity.MoveStartedConditions();
        BuildOccurIndex();
        QueueAllEntities();
    }

    /// <summary>FindEntity (@0x14138b990).</summary>
    public GuideEntity FindEntity(int id) => entities.TryGetValue(id, out var e) ? e : null;

    /// <summary>BuildOccurIndex (@0x14138dc50): each event under each kind of its trigger conditions, by the kind's sub key
    /// (0 the whole kind).</summary>
    void BuildOccurIndex()
    {
        byOccur.Clear();
        indexed.Clear();
        foreach (var entity in entities.Values)
        {
            var occurs = new SortedDictionary<int, int>();
            entity.CollectTriggerValues(occurs);
            foreach (var (key, sub) in occurs)
            {
                if (key == 0) continue;
                if (!byOccur.TryGetValue(key, out var c)) byOccur[key] = c = new OccurContainer();
                if (sub != 0)
                {
                    if (!c.ByValue.TryGetValue(sub, out var list)) c.ByValue[sub] = list = new List<GuideEntity>();
                    list.Add(entity);
                }
                else c.All.Add(entity);
            }
        }
        foreach (var (key, c) in byOccur)
            if (c.ByValue.Count != 0) indexed.Add(key);
    }

    /// <summary>QueueAllEntities (@0x14138dac0).</summary>
    void QueueAllEntities()
    {
        queue.Clear();
        queuedIds.Clear();
        foreach (var entity in entities.Values)
        {
            queue.AddLast(entity);
            queuedIds.Add(entity.Id);
        }
    }

    /// <summary>ClearEntities (@0x14138ba40): every event back to state 0, all queued.</summary>
    public void ClearEntities()
    {
        foreach (var entity in entities.Values) entity.Clear(true);
        QueueAllEntities();
    }

    // ---- the update ------------------------------------------------------------------------------------------------

    /// <summary>Tick (@0x14138baf0).</summary>
    public void Tick()
    {
        uint now = World.Now;
        if (now - lastTick > 250)
        {
            ProcessOccurrences();
            UpdateEntities();
            lastTick = now;
        }
    }

    /// <summary>StopAndUpdate (@0x14138b140).</summary>
    public void StopAndUpdate()
    {
        UpdateEntities();
        ClearEntities();
    }

    /// <summary>UpdateEntities (@0x14138bb50): not while the guide event pane is missing or loading, a cut scene plays or
    /// the sub scene is 0x6200031; a step of each queued event, then the triggers are dropped.</summary>
    public void UpdateEntities()
    {
        var pane = World.Pane;
        if (pane == null || !pane.Loaded) return;
        if (World.CutScenePlaying) return;
        if (World.SubSceneId == 0x6200031) return;
        if (errors.Count != 0 && World.AppActive)
        {
            foreach (var s in errors) World.GuideLog(s);
            errors.Clear();
        }
        int mode = World.GuideMode;
        for (var node = queue.First; node != null;)
        {
            var next = node.Next;
            if (ProcessEntity(node.Value, mode))
            {
                queuedIds.Remove(node.Value.Id);
                queue.Remove(node);
            }
            node = next;
        }
        triggers.Clear();
    }

    /// <summary>ProcessEntity (@0x14138c2d0): one step; true takes the event off the queue (it is cleared, or a trigger
    /// condition failed).</summary>
    bool ProcessEntity(GuideEntity e, int mode)
    {
        bool start = !(e.Force == 0 && mode == 0);
        if (e.State == 0)
        {
            if (!start) return false;
            if (IsCleared(e.Id)) return true;
            if (e.CheckStart(out bool open) != 0)
            {
                e.StartActions();
                e.SetState(1);
                if (Debug) World.GuideLog($"[Guide] {(uint)e.Id}:{e.Desc} Open");
            }
            else if (open) return true;
        }
        if (e.State == 1)
        {
            if (e.AreActionsDone()) e.SetState(2);
        }
        if (e.State == 2)
        {
            if (e.CheckClose() != 0)
            {
                e.StopActions();
                if (Debug) World.GuideLog($"[Guide] {(uint)e.Id}:{e.Desc} Close");
                FinishEntity(e);
                e.SetState(0);
            }
            else if (e.CheckCancel() != 0) e.Clear(true);
            else if (!start) e.Clear(true);
        }
        return false;
    }

    // ---- occurrences ------------------------------------------------------------------------------------------------

    /// <summary>QueueOccurrence (@0x14138d7e0): an occurrence for the next update, with a sub key (0 the whole kind).</summary>
    public void QueueOccurrence(GuideOccurrence occurrence, int sub) => QueueOccurrence((int)occurrence, sub);

    public void QueueOccurrence(int occurrence, int sub)
    {
        if (sub == 0) plain.Add(occurrence);
        else subs.Add((occurrence, sub));
        pending.Add(occurrence);
    }

    /// <summary>InternOccurrence (@0x14138d870): the sub key of a name (a dialog's): 1, 2, … in first-use order.</summary>
    public int InternOccurrence(string name)
    {
        if (names.TryGetValue(name, out int id)) return id;
        id = names.Count + 1;
        names.Add(name, id);
        return id;
    }

    /// <summary>ProcessOccurrences (@0x14138be90).</summary>
    public void ProcessOccurrences()
    {
        QueueSubkeyEntities();
        QueueWholeSubkeyEntities();
        QueueBaseEntities();
        plain.Clear();
        subs.Clear();
        pending.Clear();
    }

    /// <summary>QueueSubkeyEntities (@0x14138e0c0): the events of each (kind, sub key) raised, unless the whole kind was.</summary>
    void QueueSubkeyEntities()
    {
        foreach (var (occ, sub) in subs)
        {
            if (plain.Contains(occ)) continue;
            if (!byOccur.TryGetValue(occ, out var c)) continue;
            if (!c.ByValue.TryGetValue(sub, out var list)) continue;
            QueueCandidates(list);
        }
    }

    /// <summary>QueueWholeSubkeyEntities (@0x14138e400): every sub key's events of each kind raised whole.</summary>
    void QueueWholeSubkeyEntities()
    {
        foreach (int occ in plain)
        {
            if (!byOccur.TryGetValue(occ, out var c)) continue;
            foreach (var list in c.ByValue.Values) QueueCandidates(list);
        }
    }

    /// <summary>QueueBaseEntities (@0x14138e6d0): the events filed under the whole kind of each kind raised.</summary>
    void QueueBaseEntities()
    {
        foreach (int occ in pending)
        {
            if (!byOccur.TryGetValue(occ, out var c)) continue;
            QueueCandidates(c.All);
        }
    }

    /// <summary>The three passes' test: an event not queued yet is queued when it would start or only an open condition
    /// fails.</summary>
    void QueueCandidates(List<GuideEntity> list)
    {
        foreach (var e in list)
        {
            if (queuedIds.Contains(e.Id)) continue;
            if (e.CheckStart(out bool open) == 1 || !open)
            {
                queuedIds.Add(e.Id);
                queue.AddLast(e);
            }
        }
    }

    // ---- triggers ----------------------------------------------------------------------------------------------------

    /// <summary>AddTrigger (@0x14138bf00): a trigger for this update (not while a cut scene plays).</summary>
    public void AddTrigger(GuideTrigger type, int value, int sub = 0)
    {
        if (World.CutScenePlaying) return;
        if (!triggers.TryGetValue(type, out var g)) triggers[type] = g = new TriggerGroup();
        g.Values.Add(value);
        g.SubValues.Add(sub);
        if (Debug) World.GuideLog($"Guide Trigger:{type} value:{value} subValue:{sub}");
    }

    /// <summary>CheckTrigger (@0x14138c0d0): the trigger was added this update, with the value and sub value when set.</summary>
    public bool CheckTrigger(GuideTrigger type, int? value, int? sub)
    {
        if (!triggers.TryGetValue(type, out var g)) return false;
        if (value.HasValue && !g.Values.Contains(value.Value)) return false;
        if (sub.HasValue && !g.SubValues.Contains(sub.Value)) return false;
        return true;
    }

    /// <summary>CheckTriggerRange (@0x14138c210): the trigger was added this update with a value in the range.</summary>
    public bool CheckTriggerRange(GuideTrigger type, GuideRange range) =>
        triggers.TryGetValue(type, out var g) && g.Values.Any(range.Contains);

    // ---- progress ----------------------------------------------------------------------------------------------------

    /// <summary>IsCleared (@0x14138caa0): the event is known and either everything is cleared (save id 1, see
    /// <see cref="SetInitialState"/>), its save id reached its step, or it closed since the last load of the steps.</summary>
    public bool IsCleared(int id)
    {
        var e = FindEntity(id);
        if (e == null) return false;
        if (e.SaveId != 0 && steps.ContainsKey(1)) return true;
        if (steps.TryGetValue(e.SaveId, out int step) && (uint)step >= (uint)e.SaveStep) return true;
        return cleared.TryGetValue(e.Id, out int c) && c == 1;
    }

    /// <summary>FinishById (@0x14138c680).</summary>
    public void FinishById(int id)
    {
        var e = FindEntity(id);
        if (e != null) FinishEntity(e);
    }

    /// <summary>FinishEntity (@0x14138c710): cleared, a GuideClear occurrence of its id, and its step saved unless the
    /// save id holds a later one.</summary>
    void FinishEntity(GuideEntity e)
    {
        cleared[e.Id] = 1;
        QueueOccurrence(GuideOccurrence.GuideClear, e.Id);
        if (e.IsSaveSkipped) return;
        if (steps.TryGetValue(e.SaveId, out int step) && (uint)step > (uint)e.SaveStep) return;
        steps[e.SaveId] = e.SaveStep;
        StateChanged = true;
    }

    /// <summary>ResetEntity (@0x14138c850): not cleared, and its save id one step back.</summary>
    void ResetEntity(GuideEntity e)
    {
        int step = e.SaveStep;
        if ((uint)step > 0) step--;
        cleared[e.Id] = 0;
        steps[e.SaveId] = step;
        QueueOccurrence(GuideOccurrence.GuideClear, 0);
    }

    /// <summary>SendStates (@0x14138b170): the saved steps in save id order.</summary>
    public GuideRecordOut SendStates()
    {
        var list = steps.Select(p => (p.Key, p.Value)).ToList();
        StateChanged = false;
        return new GuideRecordOut(list);
    }

    /// <summary>ReceiveStates (@0x14138b280): every event cleared, the steps set (save id 0 skipped; the steps already
    /// kept stay), and the bits.</summary>
    public void ReceiveStates(GuideRecordIn record)
    {
        ClearEntities();
        foreach (var (key, value) in record.Steps)
        {
            if (key == 0) continue;
            steps[key] = value;
        }
        RecordBits = record.Bits;
    }

    /// <summary>ClearStates (@0x14138b3a0).</summary>
    public void ClearStates()
    {
        cleared.Clear();
        steps.Clear();
    }

    /// <summary>SetInitialState (@0x14138d790): save id 1 at step 1, which clears every event with a save id.</summary>
    public void SetInitialState()
    {
        steps[1] = 1;
        StateChanged = true;
        QueueOccurrence(GuideOccurrence.GuideClear, 0);
    }

    /// <summary>ResetClearState (@0x14138cc40).</summary>
    public void ResetClearState()
    {
        cleared.Clear();
        steps.Clear();
        QueueOccurrence(GuideOccurrence.GuideClear, 0);
        StateChanged = true;
        ClearEntities();
        World.GuideLog("[Guide] Complete ResetClearState");
    }

    // ---- the debug commands --------------------------------------------------------------------------------------------

    /// <summary>ToggleDebug (@0x14138d460).</summary>
    public void ToggleDebug()
    {
        Debug = !Debug;
        World.GuideLog($"[Guide] Debug Message {(Debug ? "On" : "Off")}!");
    }

    /// <summary>ShowEntityState (@0x14138d160): one event's state, or (id 0) every running one's.</summary>
    public void ShowEntityState(int id)
    {
        if (id == 0)
        {
            World.GuideLog("[Guide] -- ShowOpenGuide Start ---");
            foreach (var e in entities.Values)
                if (e.State > 0 && e.State <= 2) ShowEntityState(e.Id);
            return;
        }
        var entity = FindEntity(id);
        if (entity == null) { World.GuideLog("[Guide] Not Found Guide!!"); return; }
        string state = entity.State switch { 0 => "Init", 1 => "Opening", 2 => "Open", _ => "Unknown" };
        World.GuideLog($"[Guide] {(uint)id}:{entity.Desc} State({state}) isClear({(IsCleared(entity.Id) ? "O" : "X")})");
    }

    /// <summary>ChangeEntityState (@0x14138d520): a cleared idle event back to not cleared, an idle one opened, an open
    /// one closed.</summary>
    public void ChangeEntityState(int id)
    {
        var e = FindEntity(id);
        if (e == null) { World.GuideLog("[Guide] Not Found Guide!!"); return; }
        if (e.State == 0)
        {
            if (IsCleared(id))
            {
                ResetEntity(e);
                e.Clear(true);
                World.GuideLog("[Guide] ChangeState (Clear->Init)");
            }
            else
            {
                e.StartActions();
                e.State = 1;
                World.GuideLog("[Guide] ChangeState (Init->Openning)");
            }
        }
        else if (e.State == 1) World.GuideLog("[Guide] ChangeState(Openning) Please Wait Moment");
        else if (e.State == 2)
        {
            FinishEntity(e);
            e.Clear(true);
            World.GuideLog("[Guide] ChangeState (Open->Clear)");
        }
    }

    // ---- limits --------------------------------------------------------------------------------------------------------

    /// <summary>AddLimit (@0x1413837a0).</summary>
    public void AddLimit(GuideLimit limit)
    {
        if (limit == GuideLimit.None) return;
        limitCounts[(int)limit]++;
    }

    /// <summary>FUN_1413837e0: a limit released (the client asserts on one not held).</summary>
    public void RemoveLimit(GuideLimit limit)
    {
        if (limit == GuideLimit.None) return;
        if (limitCounts[(int)limit] == 0) return;
        limitCounts[(int)limit]--;
    }

    /// <summary>IsRestricted (@0x14138c900) = IsLimitActive (@0x141383760).</summary>
    public bool IsRestricted(GuideLimit limit) => limitCounts[(int)limit] > 0;

    /// <summary>IsSkillRestricted (@0x14138c930): PlayerControl holds every skill, PlayerMove the move skills.</summary>
    public bool IsSkillRestricted(int skillId)
    {
        if (IsRestricted(GuideLimit.PlayerControl)) return true;
        if (IsRestricted(GuideLimit.PlayerMove))
            return skillId >= GuideNames.ActionMoveUp && skillId <= GuideNames.ActionMoveDownRight;
        return false;
    }

    /// <summary>IsActionRestricted (@0x14138c9c0): the cube key limit of a cube action type (CubeKeyAll holds every one;
    /// types 1 and 0x1e none).</summary>
    public bool IsActionRestricted(int type)
    {
        if (IsRestricted(GuideLimit.CubeKeyAll)) return true;
        return type switch
        {
            1 or 0x1e => false,
            0x11 => IsRestricted(GuideLimit.CubeKeyUp),
            0x1f => IsRestricted(GuideLimit.CubeKeyDown),
            0x39 => IsRestricted(GuideLimit.CubeKeyInstall),
            0x12 => IsRestricted(GuideLimit.CubeKeyRemove),
            0x13 => IsRestricted(GuideLimit.CubeKeyRotate),
            _ => IsRestricted(GuideLimit.CubeKeySelect),
        };
    }

    /// <summary>AddDialogLimit (@0x141383b00).</summary>
    public void AddDialogLimit(string name) => dialogLimits[name] = dialogLimits.TryGetValue(name, out int n) ? n + 1 : 1;

    /// <summary>FUN_141383b90: a dialog limit released.</summary>
    public void RemoveDialogLimit(string name)
    {
        if (dialogLimits.TryGetValue(name, out int n) && n > 0) dialogLimits[name] = n - 1;
    }

    /// <summary>IsDialogLimited (@0x141383a70): a running event holds the dialog of a type name shut.</summary>
    public bool IsDialogLimited(string name) => dialogLimits.TryGetValue(name, out int n) && n > 0;

    /// <summary>The common string key of 0x88b (2187), the notice a refused dialog open shows.</summary>
    public const string DialogLimitNotice = "s_tutorial_dialog_limit";
    /// <summary>The common string key of 0x889 (2185), the notice a refused equip or unequip shows.</summary>
    public const string ItemPutOnOffNotice = "s_tutorial_itemputonoff_limit";

    /// <summary>
    /// UIDialog::vfunc_73's first test (uidialog.cpp:668): the open of a dialog of the type name is refused while a running
    /// event holds it shut; the caller then shows <see cref="DialogLimitNotice"/> (FUN_141275260(0x88b, 1, 1)). False
    /// without a guide. STILL-OPEN: the test's other half, WorldService::HasTutorial with the dialog's m_168 cleared
    /// (FUN_141926960, set by uidialog.cpp:1119), has no tutorial state here.
    /// </summary>
    public static bool RefusesDialogOpen(string typeName) => Current?.IsDialogLimited(typeName) == true;

    // ---- the listeners (guideentity_1b.cpp @0x14138e990..0x14138ec70) ------------------------------------------------

    /// <summary>vfunc_2 (@0x14138e990, the item add of the inventory listener): with a valid item (CItem::FUN_140528980),
    /// the AddItem trigger of its id and the AddItemByIconType trigger of its icon code, then the DesignItem and
    /// CountItem occurrences.</summary>
    public void OnItemAdded(int itemId, bool valid)
    {
        if (!valid) return;
        AddTrigger(GuideTrigger.AddItem, itemId);
        AddTrigger(GuideTrigger.AddItemByIconType, World.IconCodeOf(itemId));
        QueueOccurrence(GuideOccurrence.DesignItem, 0);
        QueueOccurrence(GuideOccurrence.CountItem, 0);
    }

    /// <summary>vfunc_3 (@0x14138ea00, the item remove): with a valid item, the DesignItem and CountItem occurrences.</summary>
    public void OnItemRemoved(bool valid)
    {
        if (!valid) return;
        QueueOccurrence(GuideOccurrence.DesignItem, 0);
        QueueOccurrence(GuideOccurrence.CountItem, 0);
    }

    /// <summary>vfunc_4 / vfunc_5 (@0x14138ead0 / 0x14138ea70, an item's amount or slot changed): with a valid item, the
    /// CountItem occurrence.</summary>
    public void OnItemChanged(bool valid)
    {
        if (!valid) return;
        QueueOccurrence(GuideOccurrence.CountItem, 0);
    }

    /// <summary>vfunc_ugc_5 (@0x14138eb30): a home bought.</summary>
    public void OnBuyHome() => AddTrigger(GuideTrigger.BuyHome, 0);

    /// <summary>vfunc_ugc_6 (@0x14138eb60): a home sold.</summary>
    public void OnSellHome() => AddTrigger(GuideTrigger.SellHome, 0);

    /// <summary>OnLearnEmotion (@0x14138eb90).</summary>
    public void OnLearnEmotion(int emotionId) => AddTrigger(GuideTrigger.LearnEmotion, emotionId);

    /// <summary>OnUseSkill (@0x14138ebd0).</summary>
    public void OnUseSkill(int skillId) => AddTrigger(GuideTrigger.UseSkill, skillId);

    /// <summary>OnChangeMyLevel (@0x14138ec10).</summary>
    public void OnChangeMyLevel() => QueueOccurrence(GuideOccurrence.MyPlayerLevel, 0);

    /// <summary>OnDungeonEnterState (@0x14138ec40).</summary>
    public void OnDungeonEnterState() => QueueOccurrence(GuideOccurrence.EnterDungeon, 0);

    /// <summary>vfunc_oa0_7 (@0x14138ec70, the job event of UIStatusEventDispatcher::NotifyStatusChanged 4, which passes the
    /// sub job): in the game scene (0x620000c) the JobCode occurrence, and the Job trigger unless the sub job is 1.</summary>
    public void OnJobChanged(int subJob)
    {
        if (World.SceneId != 0x620000c) return;
        QueueOccurrence(GuideOccurrence.JobCode, 0);
        if (subJob != 1) AddTrigger(GuideTrigger.Job, 0);
    }
}
