using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// The quest log's (UIQuestDialog) and the minimap's (UIMinimapDialog) UIQuestEventListener slots, fed from the live quest
/// book as CQuestManager's quest packet fires UIQuestEventDispatcher (questmanager_2.cpp:1017-1082,
/// uiquesteventlistener.cpp:223): a start (vfunc_1), progress (vfunc_3) and a reward (vfunc_2) go to slots 1 / 3 / 2 for
/// every quest type but 3, 4, 5, 7 and 10 (slots 8 - 12, which neither dialog overrides); a give-up or an expiry (vfunc_5)
/// to slot 5, a tracking change (vfunc_6) to slot 6 and a result (vfunc_7) to slot 7 for every quest.
/// </summary>
public sealed class QuestEventListeners
{
    /// <summary>The quest's data, null when the quest data lacks it.</summary>
    public Func<int, QuestData> Data = _ => null;
    /// <summary>The quest log's controller, null before the log first opens.</summary>
    public Func<QuestDialogController> QuestLog = () => null;
    public Func<MinimapController> Minimap = () => null;

    /// <summary>UIQuestDialog's m_pinned. The client's dialog listens from the HUD's start; the quests taken before the log
    /// first opens are kept here, and the log's <see cref="Ms2.Quest.QuestLog.Pinned"/> is this list.</summary>
    public readonly List<int> Pinned = new();

    /// <summary>The dispatcher's default case: the types whose vfunc_1 / _2 / _3 go to slots 1 / 2 / 3.</summary>
    public static bool GeneralType(int type) => type is not (3 or 4 or 5 or 7 or 10);

    // CQuest::Init fails for a quest without data and OnQuest keeps no such quest (questmanager_2.cpp:1010): the
    // dispatcher never sees one
    QuestData General(int id) => Data(id) is { } d && GeneralType(d.Type) ? d : null;

    public void Started(int id)
    {
        if (General(id) == null) return;
        // vfunc_o178_1 @0x141a9aa90: the pin goes in even while the log is closed
        if (QuestLog() is { } log) log.OnQuestAccepted(id);
        else Pinned.Add(id);
        Minimap()?.OnQuestEvent(true);
    }

    public void Progress(int id)
    {
        if (General(id) == null) return;
        Minimap()?.OnQuestEvent(true);        // the log's vfunc_o178_3 is empty
    }

    public void Rewarded(int id)
    {
        if (General(id) is not { } d) return;
        // vfunc_o178_2 @0x141a9ab10: CloseChapter even while closed, where it sets nothing (no m_subTab without the movie).
        // A log not made yet has no flags to close; it starts each chapter on IsSubTabOne when it reads it.
        QuestLog()?.OnQuestRewarded(d.Chapter);
        Minimap()?.OnQuestEvent(true);
    }

    public void Removed(int id)
    {
        if (Data(id) == null) return;
        QuestLog()?.OnQuestRemoved();
        Minimap()?.OnQuestEvent(true);
    }

    public void Tracking(int id)
    {
        if (Data(id) == null) return;
        QuestLog()?.OnQuestTracking();
    }

    public void Result(int id, int result) => Minimap()?.OnQuestResult();
}
