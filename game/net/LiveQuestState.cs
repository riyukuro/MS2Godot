using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Quest;
using QuestRecord = Ms2.Quest.QuestRecord;

namespace Ms2Godot.Net;

/// <summary>
/// The character's quest state from a live session: the records the quest packets built (<see cref="QuestBook"/>), with
/// CQuest::IsComplete and CQuestCommon::IsExpired worked out from the quest data, and CanStart as CQuestManager::vfunc_1
/// (questmanager_2.cpp @0x1416c9e10) through <see cref="QuestRules"/>. The time is the server's
/// (<see cref="ClientSession.ServerTime"/>, CMainSystem::GetServerTime) on the zone it sent (<see cref="MsTime"/>).
/// What the session does not carry yet (gear score, job group, sub job, guild, achievements, fame, mastery, dungeon records,
/// mentoring, vip, equipment, the term tags, the stamp event, the character and guild member times)
/// is unknown, so a quest whose rules read it is not offered (CanStart false), never guessed. A quest of a term tag
/// counts as not expired while the tags are unknown.
/// </summary>
public sealed class LiveQuestState : IQuestState, IQuestContext
{
    readonly QuestBook book;
    readonly QuestTable table;
    readonly ClientSession session;
    readonly QuestRules rules;
    readonly Func<int, int?> parentField;

    /// <param name="parentField">CFieldMetaTable::GetParentField: a field's outdoor field, null when it has none.</param>
    public LiveQuestState(QuestBook book, QuestTable table, ClientSession session, long characterId, int playerLevel, int? fieldId,
                          Func<int, int?> parentField)
    {
        this.book = book; this.table = table; this.session = session; this.parentField = parentField;
        CharacterId = characterId; PlayerLevel = playerLevel; FieldId = fieldId;
        rules = new QuestRules(table, this);
    }

    QuestRecord Map(Ms2.Net.Packets.QuestRecord q)
    {
        var data = table.Quest(q.Id);
        bool complete = data != null && QuestRules.IsComplete(data, q.State, q.Progress);
        bool expired = data != null && rules.IsExpired(data, q.StartTime) == true;
        return new QuestRecord(q.Id, q.State, q.StartTime, q.CompleteTime, q.Tracked ? 1 : 0, complete, expired, q.Progress);
    }

    public IReadOnlyList<QuestRecord> Records => book.All.OrderBy(q => q.Id).Select(Map).ToList();

    public QuestRecord Get(int questId) => book.Get(questId) is { } q ? Map(q) : null;

    public bool CanStart(int questId) => rules.CanStart(questId) == true;

    public int PlayerLevel { get; }
    public long CharacterId { get; }
    /// <summary>The term tag windows are not in the session yet: no npc's timed field is open.</summary>
    public bool NpcFieldOpen(int npcId, int fieldId) => false;

    // ---- IQuestContext ----------------------------------------------------------------------------------------------
    public long ServerTime => session?.ServerTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public long ZoneOffset => MsTime.CalcZoneOffset;
    /// <summary>m_selector: 0 from the ctor; only the SetQuestSeed user command (usercommandcli.cpp @0x14136d970) sets another.</summary>
    public int? Selector => 0;
    public int? Level => PlayerLevel;
    public int? GearScore => null;
    public int? JobGroup => null;
    public int? SubJob => null;
    public long? CharacterTime => null;
    public bool? InGuild => null;
    public int? GuildLevel => null;
    public int? GuildNpcLevel => null;
    public long? GuildMemberTime => null;
    public bool? AchievementDone(uint id) => null;
    public bool? AchievementGradeCleared(uint id, int grade) => null;
    public int? FameGrade(int alliance) => null;
    public int? MasteryGrade(int type) => null;
    public int? DungeonRecord(uint dungeon) => null;
    public int? DungeonEnterState(uint dungeon) => null;
    public int? DungeonHelperReward(uint dungeon) => null;
    public int? MentoringRole => null;
    public bool? Vip => null;
    public bool? HasEquipment(uint[] items) => null;
    public (bool Known, TermTag Tag) TermTag(string name) => (false, null);
    public bool? HasStamp(int questId) => null;
    public int? FieldId { get; }
    public int? ParentField(int field) => parentField?.Invoke(field);
    /// <summary>OnQuest 0x1f / 0x20 put an id in the daily or weekly counts with the value 1.</summary>
    public int? DailyCount(int questId) => book.DailyCounts.Contains(questId) ? 1 : 0;
    public int? WeeklyCount(int questId) => book.WeeklyCounts.Contains(questId) ? 1 : 0;
}
