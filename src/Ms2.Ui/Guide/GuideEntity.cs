using System.Xml.Linq;

namespace Ms2.Ui.Guide;

/// <summary>
/// A guide event (CGuideEntity, client/ms2/guideentity_9.cpp @0x1413a2c00..0x1413a3be0): one &lt;event&gt; of the guide
/// tables. It waits in <see cref="State"/> 0 until its trigger and open conditions hold, runs its actions (state 1 until
/// every action is done, then 2) and closes when its close conditions hold or is cleared when its cancel conditions do.
/// The open conditions with an occurrence kind move to <see cref="TriggerConditions"/> after the load
/// (<see cref="MoveStartedConditions"/>); the manager indexes the event by them.
/// </summary>
public sealed class GuideEntity
{
    readonly GuideManager manager;

    /// <summary>m_nId: the "id" attribute.</summary>
    public int Id;
    /// <summary>m_nState: 0 idle, 1 opening (the actions run), 2 open (waiting to close).</summary>
    public int State { get; internal set; }
    /// <summary>m_strDesc: the "desc" attribute (FUN_1413a4b50 is false, so it is always read).</summary>
    public string Desc = "";
    /// <summary>m_nForce: "isForce"; a forced event runs while the guide option is off.</summary>
    public int Force;
    /// <summary>m_nSaveId and m_nSaveStep: the saved progress the event sets when it closes.</summary>
    public int SaveId, SaveStep;
    /// <summary>m_nNoSave: "isNoSave".</summary>
    public int NoSave;

    public List<GuideCondition> TriggerConditions { get; private set; } = new();
    public List<GuideCondition> OpenConditions { get; private set; } = new();
    public List<GuideCondition> CloseConditions { get; private set; } = new();
    public List<GuideCondition> CancelConditions { get; private set; } = new();
    public List<GuideAction> Actions { get; } = new();

    public GuideEntity(GuideManager manager) => this.manager = manager;

    /// <summary>IsSaveSkipped (@0x1413a2c60).</summary>
    public bool IsSaveSkipped => SaveId == 0 || NoSave != 0;

    /// <summary>CheckStart (@0x1413a2ca0): 1 when every trigger and open condition holds. On a failed trigger condition
    /// <paramref name="open"/> is true, on a failed open condition false.</summary>
    public int CheckStart(out bool open)
    {
        open = false;
        foreach (var c in TriggerConditions)
            if (c.Check(manager) == 0) { open = true; return 0; }
        foreach (var c in OpenConditions)
            if (c.Check(manager) == 0) { open = false; return 0; }
        return 1;
    }

    /// <summary>CheckClose (@0x1413a2dd0): every close condition holds (an empty list does).</summary>
    public int CheckClose()
    {
        foreach (var c in CloseConditions)
            if (c.Check(manager) == 0) return 0;
        return 1;
    }

    /// <summary>CheckCancel (@0x1413a2e70): an empty list never cancels; else every cancel condition holds.</summary>
    public int CheckCancel()
    {
        if (CancelConditions.Count == 0) return 0;
        foreach (var c in CancelConditions)
            if (c.Check(manager) == 0) return 0;
        return 1;
    }

    /// <summary>StartActions (@0x1413a3210).</summary>
    public void StartActions()
    {
        foreach (var a in Actions) a.Start(manager);
    }

    /// <summary>StopActions (@0x1413a32a0).</summary>
    public void StopActions()
    {
        foreach (var a in Actions) a.Stop(manager);
    }

    /// <summary>AreActionsDone (@0x1413a3330).</summary>
    public bool AreActionsDone()
    {
        foreach (var a in Actions)
            if (!a.IsDone(manager)) return false;
        return true;
    }

    /// <summary>Load (@0x1413a33c0): the attributes, then the open, close and cancel &lt;condition&gt; lists and the
    /// openAction &lt;action&gt; list, in the element order. A bad condition or action throws
    /// <see cref="GuideLoadException"/>.</summary>
    public void Load(XElement e)
    {
        GuideXml.UInt(e, "id", ref Id);
        GuideXml.UInt(e, "saveId", ref SaveId);
        GuideXml.UInt(e, "saveStep", ref SaveStep);
        GuideXml.Int(e, "isForce", ref Force);
        GuideXml.Int(e, "isNoSave", ref NoSave);
        GuideXml.String(e, "desc", ref Desc);
        foreach (var child in e.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "open": LoadConditions(child, OpenConditions); break;
                case "close": LoadConditions(child, CloseConditions); break;
                case "cancel": LoadConditions(child, CancelConditions); break;
                case "openAction":
                    foreach (var a in child.Elements("action")) Actions.Add(GuideAction.Create(a));
                    break;
            }
        }
    }

    static void LoadConditions(XElement e, List<GuideCondition> list)
    {
        foreach (var c in e.Elements("condition")) list.Add(GuideCondition.Create(c));
    }

    /// <summary>Clear (@0x1413a35d0): stops a running event, back to state 0 (through <see cref="SetState"/> when
    /// <paramref name="notify"/>), and resets every condition.</summary>
    public void Clear(bool notify)
    {
        if (State == 1 || State == 2) StopActions();
        if (notify) SetState(0);
        else State = 0;
        foreach (var c in OpenConditions) c.Reset();
        foreach (var c in CloseConditions) c.Reset();
        foreach (var c in CancelConditions) c.Reset();
    }

    /// <summary>SetState (@0x1413a3670): the state, and a GuideOpen occurrence.</summary>
    public void SetState(int state)
    {
        State = state;
        manager.QueueOccurrence(GuideOccurrence.GuideOpen, 0);
    }

    /// <summary>UpdateConditions (@0x1413a36b0): prunes the open, close and cancel lists (an AllOf hands over its
    /// children).</summary>
    public void UpdateConditions()
    {
        OpenConditions = Prune(OpenConditions);
        CloseConditions = Prune(CloseConditions);
        CancelConditions = Prune(CancelConditions);
    }

    static List<GuideCondition> Prune(List<GuideCondition> list)
    {
        var keep = new List<GuideCondition>();
        foreach (var c in list)
            if (!c.Prune(keep)) keep.Add(c);
        return keep;
    }

    /// <summary>MoveStartedConditions (@0x1413a3720): the open conditions with a kind go to the trigger list.</summary>
    public void MoveStartedConditions()
    {
        var keep = new List<GuideCondition>();
        foreach (var c in OpenConditions)
            (c.Kind != 0 ? TriggerConditions : keep).Add(c);
        OpenConditions = keep;
    }

    /// <summary>CollectTriggerValues (@0x1413a3820): per kind of the trigger conditions, its condition's sub key; a kind
    /// two conditions share files under 0 (the whole kind).</summary>
    public void CollectTriggerValues(SortedDictionary<int, int> values)
    {
        foreach (var c in TriggerConditions)
        {
            if (c.Kind == 0) continue;
            int value = c.SubKey(manager);
            if (values.ContainsKey(c.Kind)) values[c.Kind] = 0;
            else values.Add(c.Kind, value);
        }
    }
}
