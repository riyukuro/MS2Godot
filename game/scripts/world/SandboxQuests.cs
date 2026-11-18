using Ms2.Quest;
using Ms2Godot.Ui;

namespace Ms2Godot.World;

/// <summary>
/// The offline sandbox's quest records, for the npc talk. It plays both sides: the server keeps the records (accept starts
/// a quest, complete marks it rewarded), and the client's checks read them through <see cref="IQuestContext"/>, so the
/// quests the sandbox offers and the state each shows come from the ported client code (<see cref="QuestRules"/>:
/// CQuestManager::vfunc_1 and CQuestManagerCommon::CanAcceptQuest, questdata.xml's require) rather than the Maple2 server's own rules.
/// The records last for the session. No condition progress is kept: an accepted quest counts as having every condition met
/// (IsComplete). Nothing is granted.
/// </summary>
public sealed class SandboxQuests : IQuestContext
{
    readonly Dictionary<int, QuestRecord> records = new();
    long clock;

    public QuestRules Rules { get; }

    public SandboxQuests(QuestTable table)
    {
        Rules = new QuestRules(table, this);
    }

    /// <summary>Quest mode 2: start the quest when the client's vfunc_1 lets it start.</summary>
    public bool Accept(int questId)
    {
        if (Rules.CanStart(questId) != true) return false;
        records[questId] = new QuestRecord(questId, 1, ServerTime + ++clock, 0, 0, true, false);
        return true;
    }

    /// <summary>Quest mode 4: a started quest with every condition met becomes rewarded.</summary>
    public bool Complete(int questId)
    {
        if (Get(questId) is not { IsActive: true, Complete: true } r) return false;
        records[questId] = r with { State = 2, CompleteTime = ServerTime };
        return true;
    }

    // ---- IQuestContext: the sandbox character (StubHudData) ----------------------------------------------------------
    public QuestRecord Get(int questId) => records.TryGetValue(questId, out var r) ? r : null;
    public long ServerTime => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public long ZoneOffset => Ms2.Net.MsTime.CalcZoneOffset;
    public long CharacterId => StubHudData.Instance.Player.CharacterId;
    public int? Selector => 0;
    public int? Level => StubHudData.Instance.Player.Level;
    public int? GearScore => StubHudData.Instance.Player.GearScore;
    public int? JobGroup => StubHudData.Instance.Player.Job;
    public int? SubJob => StubHudData.Instance.Player.Job <= 1 ? 100 : (StubHudData.Instance.Player.Job / 10 + 1) * 100;
    public long? CharacterTime => null;
    public bool? InGuild => false;
    public int? GuildLevel => null;
    public int? GuildNpcLevel => null;
    public long? GuildMemberTime => null;
    public bool? AchievementDone(uint id) => false;
    public bool? AchievementGradeCleared(uint id, int grade) => false;
    public int? FameGrade(int alliance) => 0;
    public int? MasteryGrade(int type) => 0;
    public int? DungeonRecord(uint dungeon) => null;
    public int? DungeonEnterState(uint dungeon) => null;
    public int? DungeonHelperReward(uint dungeon) => null;
    public int? MentoringRole => 0;
    public bool? Vip => false;
    public bool? HasEquipment(uint[] items) => false;
    public (bool Known, TermTag Tag) TermTag(string name) => (false, null);
    public bool? HasStamp(int questId) => null;
    public int? FieldId => null;
    public int? ParentField(int field) => null;
    public int? DailyCount(int questId) => 0;
    public int? WeeklyCount(int questId) => 0;
}
