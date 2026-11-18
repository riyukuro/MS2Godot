using Ms2.Avm2;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIQuestDialog (ms2/client/ms2/uiquestdialog.cpp): the quest log. This part drives the list: the tab and sub-tab
/// (SetTab / SetSubTab / GetTab / GetSubTab), the filter check boxes (ChangeTab, RefreshTab's reads) and the list data
/// (RefreshEpisodes / RefreshList / RefreshCompleted build the tree, then setList and updatePaper). The tree comes from
/// <see cref="QuestLog"/>, the same one the hand-ported dialog draws. The detail pane (RefreshDetail, SelectQuest) is not
/// driven yet.
/// </summary>
public sealed class QuestDialogController : UiController
{
    public override string Movie => "uiquestdialog";
    protected override string EntityMethod => "setList";

    public QuestLog Log;
    /// <summary>m_180 / m_184: the tab and sub-tab the dialog opens on (ctor: 0 and 1).</summary>
    public int OpenTab = 0, OpenSubTab = 1;
    /// <summary>m_filterA / m_filterB per tab (ResetFilters): "proper level" and "progress only".</summary>
    readonly bool[] filterA = { false, false, true, false }, filterB = { true, true, true, true };
    /// <summary>CGameOption etc m_20 / m_21 / m_22: displayEpicQuest, displayWorldQuestV2, displayRepeatQuest.</summary>
    public bool DisplayEpic = true, DisplayWorld = true, DisplayRepeat = true;

    /// <summary>OnLoadInit (uiquestdialog.cpp:314) without the detail pane and the focus quest, then OnLoadComplete.</summary>
    public override void OnLoadComplete()
    {
        SetTab(OpenTab);
        SetSubTab(OpenSubTab);
        ChangeTab(GetTab());
        RefreshTab();
        Host.Invoke(Host.Entity, "initializeSetting", "epic", DisplayEpic);
        Host.Invoke(Host.Entity, "initializeSetting", "world", DisplayWorld);
        Host.Invoke(Host.Entity, "initializeSetting", "repeat", DisplayRepeat);
        base.OnLoadComplete();
    }

    object Bar(string name) => Host.Child(name);

    /// <summary>GetTab: m_tab's selected index, 4 without the bar.</summary>
    public int GetTab() => Bar("m_tab") is { } b ? (int)Rt.ToNumber(Host.Get(b, "selectedIndex")) : 4;
    /// <summary>GetSubTab: m_subTab's selected index, 3 without the bar.</summary>
    public int GetSubTab() => Bar("m_subTab") is { } b ? (int)Rt.ToNumber(Host.Get(b, "selectedIndex")) : 3;

    /// <summary>SetTab: m_tab's selected index (not for 4), then updateSubTab.</summary>
    public void SetTab(int tab)
    {
        if (tab == 4) return;
        if (Bar("m_tab") is { } b) Host.Set(b, "selectedIndex", tab);
        Host.Invoke("updateSubTab");
    }

    /// <summary>SetSubTab: m_subTab's selected index (not for 3), then updateSubTab.</summary>
    public void SetSubTab(int tab)
    {
        if (tab == 3) return;
        if (Bar("m_subTab") is { } b) Host.Set(b, "selectedIndex", tab);
        Host.Invoke("updateSubTab");
    }

    /// <summary>ChangeTab: the tab's filters onto the two check boxes.</summary>
    public void ChangeTab(int tab)
    {
        if (tab == 4) return;
        if (Host.Child("m_properLevel") is { } a) Host.Set(a, "selected", filterA[tab]);
        if (Host.Child("m_progressOnly") is { } p) Host.Set(p, "selected", filterB[tab]);
    }

    bool Selected(object o) => o != null && Rt.ToBoolean(Host.Get(o, "selected"));

    /// <summary>
    /// RefreshTab: the tree for the tab and sub-tab, with "proper level" selected and "progress only" shown and selected,
    /// then setList(root) and updatePaper. No tree (no local player) leaves the list as it is.
    /// </summary>
    public void RefreshTab()
    {
        if (Log == null) return;
        (Log.Tab, Log.SubTab) = (GetTab(), GetSubTab());
        Log.ProperLevel = Selected(Host.Child("m_properLevel"));
        var po = Host.Child("m_progressOnly");
        Log.ProgressOnly = po != null && Rt.ToBoolean(Host.Get(po, "visible")) && Selected(po);
        var root = Log.Build();
        if (root == null) return;
        Host.Invoke("setList", Node(root, Log.SubTab == 0, true));
        Host.Invoke("updatePaper");
    }

    /// <summary>
    /// The GFx object of a tree node, with the members in the order the client sets them: the root (type, nodes); a quest
    /// (AddQuestNode: id, regist, forceRegist, progress, label, isNew, icon, rendererClassName; RefreshCompleted's: id,
    /// regist, progress as an int, label, isNew, icon, rendererClassName); a chapter (chapter, level, label, type,
    /// rendererClassName, nodes); a continent (label, type, rendererClassName); a region (code, field, type,
    /// rendererClassName).
    /// </summary>
    AsObject Node(QuestLogNode n, bool startable, bool root = false)
    {
        var m = new List<(string, object)>();
        if (root) { m.Add(("type", n.Type)); m.Add(("nodes", Nodes(n, startable))); return Host.Obj(m.ToArray()); }
        switch (n.Renderer)
        {
            case "qd_renderer_questList" when startable:
                m.AddRange(new (string, object)[] { ("id", n.Id), ("regist", 0), ("progress", 0), ("label", n.Label), ("isNew", 0), ("icon", "") });
                break;
            case "qd_renderer_questList":
                m.AddRange(new (string, object)[] { ("id", n.Id), ("regist", n.Regist), ("forceRegist", n.ForceRegist), ("progress", n.Progress),
                                                    ("label", n.Label), ("isNew", n.IsNew), ("icon", n.Icon) });
                break;
            case "qd_renderer_chapter":
                m.AddRange(new (string, object)[] { ("chapter", n.Chapter), ("level", n.Level), ("label", n.Label), ("type", n.Type) });
                break;
            case "qd_renderer_continent":
                m.AddRange(new (string, object)[] { ("label", n.Label), ("type", n.Type) });
                break;
            case "qd_renderer_region":
                m.AddRange(new (string, object)[] { ("code", n.Code), ("field", n.Field), ("type", n.Type) });
                break;
            default: throw new InvalidOperationException("quest log node renderer " + n.Renderer);
        }
        m.Add(("rendererClassName", n.Renderer));
        if (n.Renderer == "qd_renderer_chapter") m.Add(("nodes", Nodes(n, startable)));
        return Host.Obj(m.ToArray());
    }

    AsArray Nodes(QuestLogNode n, bool startable) =>
        new(Host.RT, (n.Nodes ?? new()).Select(c => (object)Node(c, startable)).ToList());
}
