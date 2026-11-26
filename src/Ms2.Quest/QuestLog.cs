namespace Ms2.Quest;

/// <summary>
/// One node of the quest list's tree (the ASObj the dialog builds for QuestDialog.setList): a continent or region
/// header, a chapter with its quests, or a quest.
/// </summary>
public sealed class QuestLogNode
{
    /// <summary>TreeViewConstant: 0 leaf, 1 open, 2 closed.</summary>
    public int Type;
    public string Renderer;        // rendererClassName
    public string Label;
    public int Chapter;            // a chapter node's id (0 for the others)
    public string Level;           // a chapter node's level range
    public uint Id;                // a quest node's id
    public int Regist, ForceRegist, IsNew;
    public bool Progress;
    public string Icon;            // a quest node's state icon frame
    public string Code, Field;     // a region node's island and region names
    /// <summary>The child nodes; null when the node has none (a leaf).</summary>
    public List<QuestLogNode> Nodes;

    // set by QuestLogTree (TreeViewDataProvider.preProcess)
    public QuestLogNode Parent, NextSibling;
    public bool IsRoot;
    /// <summary>Per ancestor below the root: 1 when that ancestor has a next sibling, else 0.</summary>
    public List<int> DepthIcons;
}

/// <summary>
/// TreeViewDataProvider (scaleform.ms2.controls): the rows of a setList tree. The root is not a row; a node's children
/// are rows only while it is open. Ported as the AS has it, including its quirk: an open node with an empty child list
/// ends its own branch, and the walk goes on at its parent's next sibling (its own next siblings are not counted in
/// <see cref="Length"/> nor reached by <see cref="Range"/>, although <see cref="ItemAt"/> still finds them).
/// </summary>
public sealed class QuestLogTree
{
    public const int TypeLeaf = 0, TypeOpen = 1, TypeClosed = 2;
    public readonly QuestLogNode Root;
    /// <summary>m_length (validateLength: computeLength(root)).</summary>
    public int Length { get; private set; }

    public QuestLogTree(QuestLogNode root)
    {
        Root = root;
        // preProcessRoot
        root.IsRoot = true;
        PreProcess(root, null, null);
        if (root.Nodes is { Count: > 0 }) root.Nodes[0].IsRoot = true;
        Length = ComputeLength(root);
    }

    /// <summary>preProcess: the parent, the next sibling and the depth icons; a node with children closed unless it says otherwise, else a leaf.</summary>
    static void PreProcess(QuestLogNode n, QuestLogNode parent, List<int> depthIcons)
    {
        n.DepthIcons = depthIcons ?? new List<int>();
        n.Parent = parent;
        n.Type = n.Nodes == null ? TypeLeaf : n.Type == 0 ? TypeClosed : n.Type;
        if (n.Nodes == null) return;
        var icons = new List<int>(n.DepthIcons);
        if (!n.IsRoot) icons.Add(n.NextSibling == null ? 0 : 1);
        for (int i = 0; i < n.Nodes.Count; i++)
        {
            n.Nodes[i].NextSibling = i + 1 < n.Nodes.Count ? n.Nodes[i + 1] : null;
            PreProcess(n.Nodes[i], n, icons);
        }
    }

    /// <summary>The next node of the walk computeLength and loadObjects share (into open nodes, else on, else up).</summary>
    static QuestLogNode Next(QuestLogNode n)
    {
        var at = n;
        // an empty Array is truthy in AS: an open node with no children yields nodes[0] = undefined
        var next = at.Type == TypeOpen && at.Nodes != null ? (at.Nodes.Count > 0 ? at.Nodes[0] : null) : at.NextSibling;
        while (next == null && at.Parent != null)
        {
            next = at.Parent.NextSibling;
            at = at.Parent;
        }
        return next;
    }

    static int ComputeLength(QuestLogNode root)
    {
        var end = root.NextSibling ?? root.Parent;
        int count = 0;
        for (var n = root; n != null && n != end; n = Next(n)) count++;
        return count - 1;
    }

    /// <summary>findObjectByIndex: the index-th node of the open preorder walk (0 the root); the last node when past the end.</summary>
    public QuestLogNode FindObjectByIndex(int index)
    {
        var stack = new Stack<QuestLogNode>();
        stack.Push(Root);
        int i = 0;
        var last = Root;
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n == null) break;
            last = n;
            if (i == index) break;
            if (n.Nodes != null && n.Type == TypeOpen)
                for (int k = n.Nodes.Count - 1; k > -1; k--) stack.Push(n.Nodes[k]);
            i++;
        }
        return last;
    }

    /// <summary>requestItemAt: row index (findObjectByIndex(index + 1)).</summary>
    public QuestLogNode ItemAt(int index) => FindObjectByIndex(index + 1);

    /// <summary>requestItemRange: rows start..end inclusive (loadObjects), padded with nulls past the end of the walk.</summary>
    public List<QuestLogNode> Range(int start, int end)
    {
        var list = new List<QuestLogNode>();
        var n = FindObjectByIndex(start + 1);
        int left = end - start;
        list.Add(n);
        left--;
        while (n != null && left >= 0)
        {
            n = Next(n);
            if (n != null) { list.Add(n); left--; }
        }
        while (left >= 0) { list.Add(null); left--; }
        return list;
    }
}

/// <summary>
/// UIQuestDialog's quest list (RefreshTab and the builders it calls), over the static quest table and the character's
/// quest state (<see cref="IQuestState"/>, a stub until the network lane fills it).
/// </summary>
public sealed class QuestLog
{
    readonly QuestTable table;
    public QuestTable Table => table;
    public IQuestState State;
    /// <summary>The selected tab (GetTab: m_tab's selected index; 4 without the bar) and sub-tab (GetSubTab; 3 without).</summary>
    public int Tab, SubTab;
    /// <summary>m_properLevel's selected state (CollectRecommended reads it whether or not it is visible).</summary>
    public bool ProperLevel = true;
    /// <summary>m_progressOnly is visible and selected (AddQuestNode / GroupChapters).</summary>
    public bool ProgressOnly;
    /// <summary>m_188, the quest the dialog was opened to show (FocusQuest); 0 for none.</summary>
    public int Focus;
    /// <summary>m_pinned, the quests marked new (HasPinned): each quest taken goes in (vfunc_o178_1), OnSelectQuest takes it
    /// out (RemovePinned). The dialog keeps it from the start of the game, so a host may give the list it keeps.</summary>
    public List<int> Pinned = new();
    /// <summary>m_flags: per sub-tab, per character id, each chapter's open flag (GetFlags).</summary>
    readonly Dictionary<long, Dictionary<int, bool>>[] flags = { new(), new(), new() };

    /// <summary>The map data BuildChapterFieldCodes reads; the codes are built on the first list that needs them.</summary>
    public FieldMeta Meta;
    public IReadOnlyDictionary<int, (int Continent, int Region)> FieldCodes;

    public QuestLog(QuestTable table, IQuestState state)
    {
        this.table = table;
        State = state;
    }

    /// <summary>
    /// RefreshTab: sub-tab 0 lists the startable quests (RefreshCompleted), else the epic tab lists its chapters
    /// (RefreshEpisodes) and the other tabs their chapters by continent and region (RefreshList). The root node of
    /// setList (type 1); null when RefreshEpisodes has no local player, so the list is left as it was.
    /// </summary>
    public QuestLogNode Build()
    {
        if (SubTab == 0) return RefreshCompleted();
        return Tab switch
        {
            0 => RefreshEpisodes(),
            1 or 2 or 3 => RefreshList(),
            _ => null,
        };
    }

    static QuestLogNode Root(List<QuestLogNode> nodes) => new() { Type = 1, Nodes = nodes };

    /// <summary>GetSubTabFilter: the sub-tab, 4 without one.</summary>
    int SubTabFilter => SubTab is 0 or 1 or 2 ? SubTab : 4;
    bool IsSubTabOne => SubTabFilter == 1;

    /// <summary>RefreshEpisodes: each epic chapter the character has quests of (CollectEpisodes), tutorials skipped.</summary>
    QuestLogNode RefreshEpisodes()
    {
        var nodes = new List<QuestLogNode>();
        foreach (int chapter in CollectEpisodes())
        {
            if (table.IsTutorial(chapter)) continue;
            AddQuestNode(chapter, nodes);
        }
        return Root(nodes);
    }

    /// <summary>
    /// RefreshList: per continent group (GroupChapters), a continent node when the group has a name, then per region a
    /// region node when the region has a name (with the island name of its first chapter's continent code), then each
    /// chapter's node.
    /// </summary>
    QuestLogNode RefreshList()
    {
        var nodes = new List<QuestLogNode>();
        foreach (var (continent, regions) in GroupChapters())
        {
            if (continent.Length != 0)
                nodes.Add(new QuestLogNode { Label = continent, Type = 0, Renderer = "qd_renderer_continent" });
            foreach (var (region, chapters) in regions)
            {
                if (chapters.Count == 0) continue;
                string regionName = table.Words.Get(ReservedWords.RegionName, region);
                if (regionName.Length != 0)
                {
                    int code = table.ChapterContinentCode(chapters[0]);
                    nodes.Add(new QuestLogNode
                    {
                        Code = table.Words.Get(ReservedWords.IslandName, code), Field = regionName, Type = 0,
                        Renderer = "qd_renderer_region",
                    });
                }
                foreach (int chapter in chapters) AddQuestNode(chapter, nodes);
            }
        }
        return Root(nodes);
    }

    /// <summary>
    /// RefreshCompleted (the startable list): for each recommended quest (CollectRecommended), its chapter's node with the
    /// chapter's startable quests from it on (AddChapterQuests); a chapter without text or without such quests is left out.
    /// </summary>
    QuestLogNode RefreshCompleted()
    {
        var nodes = new List<QuestLogNode>();
        foreach (var d in CollectRecommended())
        {
            var text = table.ChapterText(d.Chapter);
            if (text == null) continue;
            var children = new List<QuestLogNode>();
            var node = new QuestLogNode
            {
                Chapter = d.Chapter, Level = LevelRange(d.Chapter), Label = text.Name,
                Type = IsChapterOpen(d.Chapter) ? 1 : 2, Renderer = "qd_renderer_chapter", Nodes = children,
            };
            if (AddChapterQuests(d, children)) nodes.Add(node);
        }
        return Root(nodes);
    }

    /// <summary>
    /// AddQuestNode: the chapter's node with a node per quest of the chapter the character has a record of (in the
    /// chapter's order), but not a rewarded daily or weekly one, and with "progress only" only those in progress. Nothing
    /// for a tutorial chapter or when no quest is listed; a chapter without text adds an empty node.
    /// </summary>
    bool AddQuestNode(int chapter, List<QuestLogNode> into)
    {
        if (table.IsTutorial(chapter)) return false;
        bool added = false;
        var children = new List<QuestLogNode>();
        foreach (uint id in table.QuestIdsByChapter(chapter))
        {
            var q = State.Get((int)id);
            if (q == null) continue;
            var d = table.Quest(id);
            if (q.IsRewarded && d != null && (d.Repeat == 2 || d.Repeat == 3)) continue;
            if (ProgressOnly && q.State != 1) continue;
            children.Add(new QuestLogNode
            {
                Id = (uint)q.Id, Regist = TrackingState(q), ForceRegist = table.ForceRegist(q.Id) ? 1 : 0,
                Progress = q.State == 1, Label = QuestListLabel(q.Id), IsNew = Pinned.Contains(q.Id) ? 1 : 0,
                Icon = QuestStateName(q), Renderer = "qd_renderer_questList",
            });
            added = true;
        }
        if (!added) return false;
        var chapterNode = new QuestLogNode();
        var text = table.ChapterText(chapter);
        if (text != null)
        {
            chapterNode.Chapter = chapter;
            chapterNode.Level = LevelRange(chapter);
            chapterNode.Label = text.Name;
            chapterNode.Type = IsChapterOpen(chapter) ? 1 : 2;
            chapterNode.Renderer = "qd_renderer_chapter";
            chapterNode.Nodes = children;
        }
        into.Add(chapterNode);
        return true;
    }

    /// <summary>
    /// AddChapterQuests: the chapter's quests from the given one on (in the chapter's order), not locking ones and not
    /// field quests (type 8), each one startable by the given quest's repeat kind: a repeatable (1) one when the given
    /// quest is not in progress, a daily or weekly one when it can be accepted, else when the given quest is neither in
    /// progress nor rewarded.
    /// </summary>
    bool AddChapterQuests(QuestData from, List<QuestLogNode> into)
    {
        if (from == null) return false;
        bool before = true, added = false;
        foreach (uint id in table.QuestIdsByChapter(from.Chapter))
        {
            if (from.Id == (int)id) before = false;
            if (before) continue;
            var d = table.Quest(id);
            if (d.Chapter != from.Chapter) continue;
            if (d.Locking) continue;
            if (d.Type == 8) continue;
            if (from.Repeat == 1)
            {
                if (IsActive(from.Id)) continue;
            }
            else if (from.Repeat == 2 || from.Repeat == 3)
            {
                if (!State.CanStart((int)id)) continue;
            }
            else
            {
                if (IsActive(from.Id)) continue;
                if (IsRewarded(from.Id)) continue;
            }
            into.Add(new QuestLogNode
            {
                Id = id, Regist = 0, Progress = false, Label = QuestTitle((int)id), IsNew = 0, Icon = "",
                Renderer = "qd_renderer_questList",
            });
            added = true;
        }
        return added;
    }

    /// <summary>
    /// GetChapterIDs: the chapters (m_chapter's order) that, on the in-progress sub-tab, have a quest record and are not
    /// all complete, and on the others are all complete (AreGroupQuestsComplete).
    /// </summary>
    List<int> ChapterIds()
    {
        var ids = new List<int>();
        bool one = IsSubTabOne;
        foreach (int chapter in table.ChapterIds)
        {
            if (one)
            {
                if (!HasQuestInGroup(chapter)) continue;
                if (AreGroupQuestsComplete(chapter)) continue;
            }
            else if (!AreGroupQuestsComplete(chapter)) continue;
            ids.Add(chapter);
        }
        return ids;
    }

    /// <summary>
    /// CollectEpisodes: the chapters whose first listed quest (CollectQuests) is an epic one, sorted by that quest's
    /// start time on the in-progress sub-tab, else by the last listed quest's complete time (a CRBMultiMap: equal keys
    /// keep their order).
    /// </summary>
    List<int> CollectEpisodes()
    {
        var sorted = new List<(long Key, int Chapter)>();
        foreach (int chapter in ChapterIds())
        {
            var quests = CollectQuests(chapter);
            if (quests.Count == 0) continue;
            if (table.Quest(quests[0].Id)?.Type != 0) continue;
            long key = IsSubTabOne ? quests[0].StartTime : quests[^1].CompleteTime;
            int at = sorted.Count;
            while (at > 0 && sorted[at - 1].Key > key) at--;
            sorted.Insert(at, (key, chapter));
        }
        return sorted.Select(s => s.Chapter).ToList();
    }

    /// <summary>
    /// CollectQuests: the records (id order) of the chapter in the sub-tab's state (in progress on the in-progress
    /// sub-tab, rewarded on the others) that belong to the current tab and are not expired.
    /// </summary>
    List<QuestRecord> CollectQuests(int chapter)
    {
        int state = IsSubTabOne ? 4 : 2;
        var list = new List<QuestRecord>();
        foreach (var q in QuestsFiltered(state, 11, chapter))
        {
            if (!IsInCurrentTab(q.Id)) continue;
            if (q.Expired) continue;
            list.Add(q);
        }
        return list;
    }

    /// <summary>
    /// GroupChapters: the chapters (GetChapterIDs) with a listed quest (as AddQuestNode lists them), not tutorials, by
    /// continent name (string table 25 of the chapter's continent code) then region code, in the order of the two
    /// CAtlMaps (CStringElementTraits hash for the names, the code itself for the regions).
    /// </summary>
    List<(string Continent, List<(int Region, List<int> Chapters)> Regions)> GroupChapters()
    {
        EnsureFieldCodes();
        var groups = new Dictionary<string, Dictionary<int, List<int>>>();
        var groupOrder = new List<string>();
        var regionOrder = new Dictionary<string, List<int>>();
        foreach (int chapter in ChapterIds())
        {
            if (table.IsTutorial(chapter)) continue;
            bool kept = false;
            foreach (var q in CollectQuests(chapter))
            {
                var d = table.Quest(q.Id);
                if (q.IsRewarded && d != null && (d.Repeat == 2 || d.Repeat == 3)) continue;
                if (ProgressOnly && q.State != 1) continue;
                kept = true;
            }
            if (!kept) continue;
            int region = table.ChapterRegionCode(chapter);
            string name = table.Words.Get(ReservedWords.ContinentName, table.ChapterContinentCode(chapter));
            if (!groups.TryGetValue(name, out var g)) { groups[name] = g = new Dictionary<int, List<int>>(); groupOrder.Add(name); regionOrder[name] = new List<int>(); }
            if (!g.TryGetValue(region, out var ids)) { g[region] = ids = new List<int>(); regionOrder[name].Add(region); }
            ids.Add(chapter);
        }
        return AtlStringMapOrder(groupOrder)
            .Select(n => (n, FieldMeta.AtlMapOrder(regionOrder[n]).Select(r => (r, groups[n][r])).ToList())).ToList();
    }

    void EnsureFieldCodes()
    {
        if (table.FieldCodesBuilt || Meta == null || FieldCodes == null) return;
        table.BuildChapterFieldCodes(Meta, FieldCodes, State.NpcFieldOpen);
    }

    /// <summary>
    /// CollectRecommended: per chapter (m_chapter's order), the first quest of the chapter that can be accepted, or that
    /// follows the first quest in progress, kept when it is in the current tab, not of a tutorial chapter, not hidden
    /// from the startable list (property 1), not already taken and, with "proper level", not more than 5 levels below the
    /// player (the focused quest's chapter is exempt); for a quest in progress the level test reads that quest's level,
    /// not the following one's (as the exe). One per chapter, sorted by level (equal levels keep their order).
    /// </summary>
    List<QuestData> CollectRecommended()
    {
        bool proper = ProperLevel;
        int focusChapter = Focus != 0 ? table.Quest(Focus)?.Chapter ?? 0 : 0;
        int level = State.PlayerLevel;
        var seenQuests = new HashSet<int>();
        var seenChapters = new HashSet<int>();
        var sorted = new List<QuestData>();
        void Insert(QuestData d)
        {
            int at = sorted.Count;
            while (at > 0 && sorted[at - 1].Level > d.Level) at--;
            sorted.Insert(at, d);
        }
        foreach (int chapter in table.ChapterIds)
        {
            foreach (uint id in table.QuestIdsByChapter(chapter))
            {
                var d = table.Quest(id);
                if (State.CanStart((int)id))
                {
                    if (proper && chapter != focusChapter && level - d.Level > 5) continue;
                    if (seenQuests.Contains(d.Id)) continue;
                    if (seenChapters.Contains(chapter)) continue;
                    if (table.IsTutorial(d.Chapter)) continue;
                    if (!IsInCurrentTab(d.Id)) continue;
                    if (d.HasProperty(1)) continue;
                    Insert(d);
                    seenQuests.Add(d.Id);
                    seenChapters.Add(chapter);
                    break;
                }
                if (IsActive((int)id))
                {
                    bool found = false;
                    foreach (uint f in d.Following)
                    {
                        var next = table.Quest(f);
                        if (next == null) continue;
                        if (table.IsTutorial(next.Chapter)) continue;
                        if (proper && chapter != focusChapter && level - d.Level > 5) continue;
                        if (seenQuests.Contains(next.Id)) continue;
                        if (seenChapters.Contains(chapter)) continue;
                        if (!IsInCurrentTab(next.Id)) continue;
                        if (next.HasProperty(1)) continue;
                        Insert(next);
                        seenChapters.Add(chapter);
                        seenQuests.Add(next.Id);
                        found = true;
                    }
                    if (found) break;
                }
            }
        }
        return sorted;
    }

    // ---- chapter open flags ------------------------------------------------------------------------------------------

    /// <summary>
    /// GetFlags: the sub-tab's flags for this character, a chapter seen the first time starting open on the in-progress
    /// sub-tab and closed on the others. Null without a sub-tab.
    /// </summary>
    Dictionary<int, bool> Flags(int chapter)
    {
        if (SubTab is < 0 or > 2) return null;
        if (!flags[SubTab].TryGetValue(State.CharacterId, out var f)) flags[SubTab][State.CharacterId] = f = new Dictionary<int, bool>();
        if (!f.ContainsKey(chapter)) f[chapter] = IsSubTabOne;
        return f;
    }

    /// <summary>CloseChapter @0x141aa2520 (uiquestdialog.cpp:1910): once every quest of the chapter is rewarded
    /// (AreGroupQuestsComplete), its flag on the sub-tab is closed; nothing without a sub-tab.</summary>
    public void CloseChapter(int chapter)
    {
        if (!AreGroupQuestsComplete(chapter)) return;
        if (Flags(chapter) is { } f) f[chapter] = false;
    }

    /// <summary>IsChapterOpen: the focused quest's chapter is open, else its flag.</summary>
    public bool IsChapterOpen(int chapter)
    {
        var focus = table.Quest(Focus);
        if (focus != null && focus.Chapter == chapter) return true;
        var f = Flags(chapter);
        return f != null && f[chapter];
    }

    /// <summary>SelectChapter: toggles the chapter's flag; true when it was open (the close sound plays, else the open one).</summary>
    public bool? SelectChapter(int chapter)
    {
        var f = Flags(chapter);
        if (f == null) return null;
        bool open = f[chapter];
        f[chapter] = !open;
        return open;
    }

    /// <summary>ClearFlags: every sub-tab's flags dropped.</summary>
    public void ClearFlags() { foreach (var f in flags) f.Clear(); }

    // ---- quest helpers -------------------------------------------------------------------------------------------

    /// <summary>
    /// TabOfQuest: the quest's tabIndex when set; field and alliance quests 3; daily and weekly quests 1; else epic 0,
    /// world 2, event and type 6 1; -1 for other types or an unknown quest.
    /// </summary>
    public int TabOfQuest(int id)
    {
        var d = table.Quest(id);
        if (d == null) return -1;
        if (d.TabIndex != -1) return d.TabIndex;
        if (d.Type == 9 || d.Type == 8) return 3;
        if (d.Repeat == 2 || d.Repeat == 3) return 1;
        return d.Type switch { 0 => 0, 1 => 2, 2 or 6 => 1, _ => -1 };
    }

    public bool IsInCurrentTab(int id) => Tab == TabOfQuest(id);

    bool IsActive(int id) => State.Get(id)?.IsActive ?? false;
    bool IsRewarded(int id) => State.Get(id)?.IsRewarded ?? false;

    /// <summary>GetQuestsFiltered: the records (id order) in a state (4 any), of a type (11 any), of a chapter (-1 any).</summary>
    IEnumerable<QuestRecord> QuestsFiltered(int state, int type, int chapter)
    {
        foreach (var q in State.Records)
        {
            if (state != 4 && state != q.State) continue;
            var d = table.Quest(q.Id);
            if (type != 11 && d?.Type != type) continue;
            if (chapter != -1 && d?.Chapter != chapter) continue;
            yield return q;
        }
    }

    /// <summary>HasQuestInGroup: the character has a record of a quest of the chapter.</summary>
    bool HasQuestInGroup(int chapter) => QuestsFiltered(4, 11, chapter).Any();

    /// <summary>AreGroupQuestsComplete: every quest of the chapter but those excepted from the chapter clear is rewarded.</summary>
    bool AreGroupQuestsComplete(int chapter)
    {
        foreach (uint id in table.QuestIdsByChapter(chapter))
        {
            var d = table.Quest(id);
            if (d == null || d.ExceptChapterClear) continue;
            var q = State.Get((int)id);
            if (q == null || q.State != 2) return false;
        }
        return true;
    }

    /// <summary>CQuest::GetTrackingState: forced quests are always registered, else the record's flag.</summary>
    int TrackingState(QuestRecord q) => table.ForceRegist(q.Id) ? 1 : q.Tracking;

    /// <summary>
    /// QuestStateName: "fail" when expired; when complete "satisfied_epic" (epic), "satisfied_event" (event),
    /// "satisfied_repeat" (a repeatable world or type 6 quest) or "satisfied"; "complete" when rewarded; else "progress".
    /// </summary>
    string QuestStateName(QuestRecord q)
    {
        if (q.Expired) return "fail";
        if (q.Complete)
        {
            var d = table.Quest(q.Id);
            return d?.Type switch
            {
                0 => "satisfied_epic",
                2 => "satisfied_event",
                1 or 6 => d.Repeat != 0 ? "satisfied_repeat" : "satisfied",
                _ => "satisfied",
            };
        }
        if (q.IsRewarded) return "complete";
        return "progress";
    }

    /// <summary>QuestTitle: GetQuestName with the value words replaced (the type 8 / 9 prefixes it builds are overwritten).</summary>
    public string QuestTitle(int id) => table.Words.ReplaceValueWords(table.QuestName(id));

    /// <summary>QuestListLabel: s_quest_list_quest_name with the title and the quest level; empty for an unknown quest.</summary>
    public string QuestListLabel(int id)
    {
        var d = table.Quest(id);
        if (d == null) return "";
        return table.Common("s_quest_list_quest_name").Replace("{0}", QuestTitle(id)).Replace("{1}", d.Level.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>LevelRange: s_quest_list_level with the lowest and highest quest level of the chapter, s_quest_list_level_equal when they are equal; empty without quests.</summary>
    public string LevelRange(int chapter)
    {
        var ids = table.QuestIdsByChapter(chapter);
        if (ids.Count == 0) return "";
        int min = int.MaxValue, max = 0;
        foreach (uint id in ids)
        {
            int l = table.Quest(id).Level;
            if (min > l) min = l;
            if (max < l) max = l;
        }
        string I(int v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (min == max) return table.Common("s_quest_list_level_equal").Replace("{0}", I(min));
        return table.Common("s_quest_list_level").Replace("{0}", I(min)).Replace("{1}", I(max));
    }

    /// <summary>
    /// The order a CAtlMap&lt;CStringW, ...&gt;(17, 0.75, 0.25, 2.25) gives its keys: <see cref="FieldMeta.AtlMapOrder{T}"/>
    /// with CStringElementTraits' hash.
    /// </summary>
    public static List<string> AtlStringMapOrder(IEnumerable<string> insertOrder) => FieldMeta.AtlMapOrder(insertOrder, Hash);

    /// <summary>CStringElementTraits&lt;CStringW&gt;::Hash: (hash &lt;&lt; 5) + hash + each character.</summary>
    public static uint Hash(string s)
    {
        uint h = 0;
        foreach (char c in s) h = (h << 5) + h + c;
        return h;
    }
}
