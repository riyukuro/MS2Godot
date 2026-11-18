using Ms2.Quest;

namespace Ms2Godot.Ui;

/// <summary>
/// SHOT FIXTURE, not game state: made-up quest records so a shot can show the quest log's rows (--questlog-fixture). The
/// real state comes from the server (<see cref="IQuestState"/>); offline the character has none. Picked from the quest
/// table only, in its own order: of the first two epic chapters with a name, the first has every quest rewarded, the
/// second its first quest rewarded, its second in progress and registered, its third in progress with every condition met;
/// the first quest of each of the first three world chapters with a name is in progress; and the first quest of every
/// chapter the character has no record of can be accepted, when its level is set (the level 0 development chapters are
/// left out).
/// </summary>
public sealed class QuestShotState : IQuestState
{
    readonly QuestTable table;
    readonly SortedDictionary<int, QuestRecord> records = new();

    public QuestShotState(QuestTable table, int playerLevel)
    {
        this.table = table;
        PlayerLevel = playerLevel;
        var named = table.ChapterIds.Where(c => table.ChapterText(c) != null && !table.IsTutorial(c)
                                               && table.QuestIdsByChapter(c).Count >= 3).ToList();
        var epic = named.Where(c => table.Quest(table.QuestIdsByChapter(c)[0]).Type == 0).Take(2).ToList();
        if (epic.Count > 0)
            foreach (uint id in table.QuestIdsByChapter(epic[0])) Add((int)id, 2, 0, false);
        if (epic.Count > 1)
        {
            var ids = table.QuestIdsByChapter(epic[1]);
            Add((int)ids[0], 2, 0, false);
            Add((int)ids[1], 1, 1, false);
            Add((int)ids[2], 1, 0, true);
        }
        foreach (int c in named.Where(c => table.Quest(table.QuestIdsByChapter(c)[0]).Type == 1).Take(3))
            Add((int)table.QuestIdsByChapter(c)[0], 1, 0, false);
    }

    void Add(int id, int state, int tracking, bool complete) =>
        records[id] = new QuestRecord(id, state, id, state == 2 ? id : 0, tracking, complete, false);

    public IReadOnlyList<QuestRecord> Records => records.Values.ToList();
    public QuestRecord Get(int questId) => records.TryGetValue(questId, out var q) ? q : null;

    public bool CanStart(int questId)
    {
        var d = table.Quest(questId);
        if (d == null || d.Level == 0 || records.ContainsKey(questId)) return false;
        var ids = table.QuestIdsByChapter(d.Chapter);
        return ids.Count > 0 && ids[0] == (uint)questId && !ids.Any(i => records.ContainsKey((int)i));
    }

    public int PlayerLevel { get; }
    public long CharacterId => 0;
    public bool NpcFieldOpen(int npcId, int fieldId) => false;
}
