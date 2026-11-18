namespace Ms2.Quest;

/// <summary>
/// One quest record of the character (CQuest), as the quest packet gives it (CQuestManager::OnQuest mode 0x16: id,
/// state, start time, complete time, tracking flag, then the condition progress).
/// </summary>
/// <param name="State">GetState: 1 in progress (IsActive), 2 rewarded (IsRewarded).</param>
/// <param name="Tracking">m_trackingState, the registered-in-the-guide flag the packet sent (GetTrackingState adds the forced ones).</param>
/// <param name="Complete">IsComplete: every condition met (from the progress counts).</param>
/// <param name="Expired">IsExpired: the quest's period is over (needs the server time).</param>
/// <param name="Progress">m_progress: each condition's count (GetProgress), null when the packet gave none.</param>
public sealed record QuestRecord(int Id, int State, long StartTime, long CompleteTime, int Tracking, bool Complete, bool Expired,
                                 long[] Progress = null)
{
    public bool IsActive => State == 1;
    public bool IsRewarded => State == 2;
}

/// <summary>
/// STUB for the network lane: the character's quest state that only the server supplies (CQuestManager and the parts of
/// CQuestManagerCommon that read it). The quest log reads everything live through this; <see cref="NoQuestState"/> is
/// what an offline client has.
/// </summary>
public interface IQuestState
{
    /// <summary>m_quests20: every quest record, in quest id order (a CRBMap).</summary>
    IReadOnlyList<QuestRecord> Records { get; }

    /// <summary>vfunc_o10_3: the record of a quest, null when the character has none.</summary>
    QuestRecord Get(int questId);

    /// <summary>
    /// CQuestManager::vfunc_1(id, 0): the quest can be accepted now (CanAcceptQuest's requirements, the group pick, the
    /// day of week, the event schedule, the repeat reset, the field of a field mission, the stamp event, the daily and
    /// weekly counts). It reads the server time, the game rules and the records.
    /// </summary>
    bool CanStart(int questId);

    /// <summary>The local player's level (PCBase vfunc_78).</summary>
    int PlayerLevel { get; }

    /// <summary>DAT_143ab2828, the character id: the key of the dialog's chapter open flags.</summary>
    long CharacterId { get; }

    /// <summary>FUN_1406fbb50 for an (npc, field) pair with a time window (FieldMeta.TimedNpcFields): the server time is inside it.</summary>
    bool NpcFieldOpen(int npcId, int fieldId);
}

/// <summary>The offline quest state: no records, nothing can be accepted, the player level the HUD has.</summary>
public sealed class NoQuestState : IQuestState
{
    public NoQuestState(int playerLevel) { PlayerLevel = playerLevel; }
    public IReadOnlyList<QuestRecord> Records { get; } = Array.Empty<QuestRecord>();
    public QuestRecord Get(int questId) => null;
    public bool CanStart(int questId) => false;
    public int PlayerLevel { get; }
    public long CharacterId => 0;
    public bool NpcFieldOpen(int npcId, int fieldId) => false;
}
