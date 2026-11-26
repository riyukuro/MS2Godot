using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIFameContentsNotify (uirewardnotify.cpp:682-830, "FameContentsNotify", UI id 0x4f5a, a UIHeadUpDisplay), the C++ side
/// of uifamecontentsnotify: the alliance quest banners. CAllianceQuest::DecodeStart queues the opening banner and
/// CFieldQuest / CAllianceQuest::Complete the ending one (stringtable.cpp, 0x141810210 / 0x141810140 / 0x1418102b0).
/// The queue (m_list) holds (alliance, opening) pairs, each once; the movie is loaded while it is not empty and shows the
/// head (addNotify); the movie's onHideComplete drops the head and shows the next, or unloads it.
/// </summary>
public sealed class FameContentsNotifyController : UiController
{
    public override string Movie => "uifamecontentsnotify";
    protected override string EntityMethod => "addNotify";
    protected override bool IsDialog => false;                  // a UIHeadUpDisplay (m_2c 0x1f)
    /// <summary>GetTypeName (@0x141ac23e0).</summary>
    public override string TypeName => "FameContentsNotify";
    /// <summary>vfunc_48 (@0x141ac3fa0).</summary>
    public const int UiId = 0x4f5a;

    public FameTexts Fame = new();
    /// <summary>The quest's alliance (GetQuestData(id)->m_4c), null without the quest.</summary>
    public Func<long, int?> QuestAlliance = _ => null;

    /// <summary>UIHeadUpDisplay::vfunc_2: the host loads the movie.</summary>
    public Action LoadMovie;
    /// <summary>UIHeadUpDisplay::vfunc_3: the host unloads the movie.</summary>
    public Action UnloadMovie;
    /// <summary>After each addNotify, with its object (for the tests and the live run's log).</summary>
    public event Action<Dictionary<string, object>> Notified;

    readonly LinkedList<(int Alliance, bool Opening)> list = new();     // m_list
    /// <summary>The queued banners, head first.</summary>
    public IEnumerable<(int Alliance, bool Opening)> Pending => list;

    bool Loaded => Host?.Entity != null;                       // m_30

    /// <summary>The quest's questType (FUN_14074bc90), null without the quest.</summary>
    public Func<long, int?> QuestType = _ => null;

    /// <summary>
    /// CQuestManager::OnQuest's banner calls: mode 2 (Start) creates the quest by its type (CreateQuestByType,
    /// questmanager_2.cpp:803), and a CAllianceQuest (type 9)'s DecodeStart queues the opening banner; mode 4 (Complete)
    /// of a known quest calls its Complete, where CFieldQuest (type 8) and CAllianceQuest queue the ending one.
    /// <paramref name="known"/> is whether a Complete's quest was there before (vfunc_o10_3).
    /// </summary>
    public void OnQuest(Ms2.Net.Packets.QuestMessage m, bool known)
    {
        switch (m)
        {
            case Ms2.Net.Packets.QuestMessage.Start s when QuestType(s.Id) == 9:
                QueueOpening(s.Id);
                break;
            case Ms2.Net.Packets.QuestMessage.Complete c when known && QuestType(c.Id) is 8 or 9:
                QueueEnding(c.Id);
                break;
        }
    }

    /// <summary>QueueOpeningBanner (@0x141ac25b0).</summary>
    public void QueueOpening(long questId) => Queue(questId, true);

    /// <summary>QueueEndingBanner (@0x141ac26a0).</summary>
    public void QueueEnding(long questId) => Queue(questId, false);

    /// <summary>The two Queue*Banner: nothing without the quest, nor when the pair is queued already; else it goes to the
    /// tail and, with no movie, the movie loads (vfunc_2).</summary>
    void Queue(long questId, bool opening)
    {
        if (QuestAlliance(questId) is not int alliance) return;
        if (list.Contains((alliance, opening))) return;
        list.AddLast((alliance, opening));
        if (!Loaded) LoadMovie?.Invoke();
    }

    /// <summary>OnFadeInComplete (@0x141ac2790, NotifyFadeInComplete): the movie loads (vfunc_2) when the queue is not
    /// empty.</summary>
    public void OnFadeInComplete()
    {
        if (list.Count != 0 && !Loaded) LoadMovie?.Invoke();
    }

    /// <summary>OnLoadComplete (@0x141ac2470): UIObject's, then the head is shown, or with none the movie unloads.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (!SendHead()) UnloadMovie?.Invoke();
    }

    /// <summary>vfunc_28 (@0x141ac2510): onHideComplete drops the head, then shows the next, or with none unloads.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (!string.Equals(name, "onHideComplete", StringComparison.OrdinalIgnoreCase)) return false;
        if (list.Count != 0) list.RemoveFirst();
        if (!SendHead()) UnloadMovie?.Invoke();
        return true;
    }

    /// <summary>vfunc_18 (@0x141ac24b0): onUpdatePosition, which centres the banner (x = width / 2, y = height * 0.38).</summary>
    public override void Layout(double stageW, double stageH)
    {
        if (Loaded) Host.Invoke(Host.Entity, "onUpdatePosition");
    }

    /// <summary>FUN_141ac27d0 (@0x141ac27d0): false with an empty queue; else the head's addNotify and true.</summary>
    bool SendHead()
    {
        if (list.Count == 0) return false;
        if (!Loaded) return true;
        var notice = Notice(list.First.Value.Alliance, list.First.Value.Opening);
        Host.Invoke(Host.Entity, "addNotify", notice);
        Notified?.Invoke(notice);
        return true;
    }

    /// <summary>
    /// The addNotify object of FUN_141ac27d0: alliance = "Opening_" / "Ending_" + Name_14021a6b0, msg =
    /// s_fame_contents_notify_opening / _ending with the alliance name, and current, max and point of
    /// GetFamePointProgress.
    /// </summary>
    public Dictionary<string, object> Notice(int alliance, bool opening)
    {
        var (cur, max, text) = Fame.Progress(alliance);
        string key = opening ? "s_fame_contents_notify_opening" : "s_fame_contents_notify_ending";
        return new Dictionary<string, object>
        {
            ["alliance"] = (opening ? "Opening_" : "Ending_") + FameTable.AllianceName(alliance),
            ["msg"] = ClientFormat.Format(Fame.CommonByKey(key) ?? "", Fame.AllianceName(alliance)),
            ["current"] = cur,
            ["max"] = max,
            ["point"] = text,
        };
    }
}
