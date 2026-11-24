namespace Ms2.Quest;

/// <summary>
/// The quest mark of an npc: QuestDataTable::GetQuestIdsByNpc / HasNpcQuests (questdata.cpp @0x14074e0f0, @0x14074e030) and
/// CQuestManager::GetNpcQuestState with its per-quest score FUN_1416d1100 (questmanager_2.cpp @0x1416d12d0, @0x1416d1100).
/// The overhead emoticon (CNpc's RefreshQuestState) and the minimap read it.
/// </summary>
public sealed class NpcQuestMarks
{
    /// <summary>The mark: State 0 can start, 1 in progress, 2 can complete, 4 none; the quest it is for; Icon the quest's
    /// type (FUN_14074bc90), 11 with no quest (Reset).</summary>
    public readonly record struct Mark(int State, int QuestId, int Icon)
    {
        public static readonly Mark None = new(4, 0, 11);
    }

    readonly QuestTable table;
    readonly Dictionary<int, List<int>> byNpc = new();

    /// <summary>CGameOption etc DisplayEpic / DisplayWorld / DisplayRepeat (IsQuestDisplayed), all on by default.</summary>
    public bool DisplayEpic = true, DisplayWorld = true, DisplayRepeat = true;

    /// <summary>
    /// The npc lists (m_430, questdata.cpp:2449): each quest under its start npc, and under its complete npc when that
    /// differs. Each list is sorted as SortNpcQuestIds (@0x140750d90) sorts it: the same type by level ascending, else by the type's rank (epic 1, event 2, world 3, others 4).
    /// STILL-OPEN: the client's sort is std::sort over the load order, so the order of quests of the same type and level
    /// is not defined; here they keep the quest id order.
    /// </summary>
    public NpcQuestMarks(QuestTable table)
    {
        this.table = table;
        foreach (var q in table.Quests.OrderBy(q => q.Id))
        {
            Add(q.StartNpc, q.Id);
            if (q.CompleteNpc != q.StartNpc) Add(q.CompleteNpc, q.Id);
        }
        foreach (var list in byNpc.Values)
        {
            var sorted = list.Select(id => table.Quest(id)).OrderBy(d => TypeRank(d.Type)).ThenBy(d => d.Level).ThenBy(d => d.Id)
                             .Select(d => d.Id).ToList();
            list.Clear();
            list.AddRange(sorted);
        }
    }

    void Add(int npc, int quest)
    {
        if (!byNpc.TryGetValue(npc, out var list)) byNpc[npc] = list = new List<int>();
        list.Add(quest);
    }

    static int TypeRank(int type) => type switch { 0 => 1, 2 => 2, 1 => 3, _ => 4 };

    /// <summary>HasNpcQuests: the npc starts or completes a quest.</summary>
    public bool HasQuests(int npc) => byNpc.ContainsKey(npc);

    /// <summary>GetQuestIdsByNpc: the npc's quests in table order, empty for none.</summary>
    public IReadOnlyList<int> Quests(int npc) => byNpc.TryGetValue(npc, out var l) ? l : Array.Empty<int>();

    /// <summary>
    /// GetNpcQuestState(npc, state, all): the highest scoring quest (the first one on a tie), skipping the hidden kinds
    /// unless <paramref name="all"/>.
    /// </summary>
    public Mark Get(int npc, IQuestContext ctx, QuestRules rules, bool all = false)
    {
        var best = Mark.None;
        int bestScore = 0;
        foreach (int id in Quests(npc))
        {
            if (!all && !IsDisplayed(id)) continue;
            int score = Score(npc, id, ctx, rules, out int state);
            if (score > bestScore)
            {
                bestScore = score;
                best = new Mark(state, id, table.Quest(id)?.Type ?? 11);
            }
        }
        return best;
    }

    /// <summary>
    /// FUN_1416d1100: can start (CQuestManager::vfunc_1 with the npc) 0x20 and state 0; in progress at this npc
    /// (FUN_140746850, not yet complete) 0x10 and state 1; completable at this npc (FUN_140746ba0 = 0) 0x30 and state 2; else
    /// 0 and state 4. Then the kind: epic | 4, event | 3, repeatable | 1, others | 2. A start check the rules cannot answer
    /// (null) counts as no.
    /// </summary>
    public int Score(int npc, int questId, IQuestContext ctx, QuestRules rules, out int state)
    {
        int flags;
        var r = ctx.Get(questId);
        var data = table.Quest(questId);
        if (rules.CanStart(questId, npc) == true) { state = 0; flags = 0x20; }
        else if (r != null && data != null && !r.Expired && r.State == 1 && !r.Complete && data.CompleteNpc == npc) { state = 1; flags = 0x10; }
        else if (r != null && !r.Expired && data != null && r.State == 1 && (npc == 0 || data.CompleteNpc == npc) && r.Complete) { state = 2; flags = 0x30; }
        else { state = 4; return 0; }
        if (data == null) return 0;
        if (data.Type == 0) flags |= 4;
        else if (data.Type == 2) flags |= 3;
        else if (data.Repeat != 0) flags |= 1;
        else flags |= 2;
        return flags;
    }

    /// <summary>
    /// TalkAction::vfunc_1's talk check on the npc FindTalkableNpc found (actionkeyobject.cpp:1385-1398, @0x140957bb0): the
    /// key talks to it when GetNpcQuestState(npc, state, 1) gives a quest (icon not 0xb), when its kind is not 0, when it has
    /// npc talks (HasNpcTalks, talkscript.cpp:345), or when <see cref="HasTalkScriptForNpc"/>. A quest-only npc (no kind,
    /// no npc script, e.g. a job tutorial's first quest giver) talks through its quests.
    /// </summary>
    public bool HasTalk(int npc, int kind, bool hasNpcTalks, IQuestContext ctx, QuestRules rules, Func<int, int, bool> hasQuestTalk)
    {
        if (Get(npc, ctx, rules, all: true).Icon != Mark.None.Icon) return true;
        if (kind != 0) return true;
        if (hasNpcTalks) return true;
        return HasTalkScriptForNpc(npc, ctx, hasQuestTalk);
    }

    /// <summary>
    /// CQuestManager::HasTalkScriptForNpc (questmanager_2.cpp:1342, @0x1416cebf0): a started quest (GetQuestsFiltered state 1,
    /// any type, any group) whose start npc (m_2e8) is the npc and that has a quest talk 200 (FindQuestTalk(quest, 200)).
    /// </summary>
    public bool HasTalkScriptForNpc(int npc, IQuestContext ctx, Func<int, int, bool> hasQuestTalk)
    {
        foreach (int id in Quests(npc))
            if (table.Quest(id)?.StartNpc == npc && ctx.Get(id) is { State: 1 } && hasQuestTalk(id, 200)) return true;
        return false;
    }

    /// <summary>IsQuestDisplayed: epic by DisplayEpic, event always, repeatable by DisplayRepeat, world by DisplayWorld,
    /// others always; a quest with no data never.</summary>
    public bool IsDisplayed(int questId)
    {
        var d = table.Quest(questId);
        if (d == null) return false;
        if (d.Type == 0) return DisplayEpic;
        if (d.Type == 2) return true;
        if (d.Repeat != 0) return DisplayRepeat;
        if (d.Type == 1) return DisplayWorld;
        return true;
    }

    /// <summary>
    /// SelectStringByQuestKind (cinematiccomponent.cpp:810): the icon suffix of a quest's kind: epic "_epic", event "_event",
    /// world or the repeatable types 1 and 6 "_repeat" when repeatable, else "".
    /// </summary>
    public static string Suffix(QuestData q) => q?.Type switch
    {
        0 => "_epic",
        2 => "_event",
        1 or 6 => q.Repeat != 0 ? "_repeat" : "",
        _ => "",
    };
}
