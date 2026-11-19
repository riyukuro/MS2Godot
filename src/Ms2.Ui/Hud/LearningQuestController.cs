using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Ms2.Avm2;
using Ms2.Quest;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// UILearningQuestDialog (UI id 0x4f18, ms2/client/ms2/uilearningquestdialog.cpp): the game menu's learning quests. The
/// tree lists the chapters of the learning quests' required quests (Table/learningQuest.xml) with their quests not yet
/// rewarded; the selected learning quest shows its guide video, its accept and complete rewards, and its OK button
/// sends Quest 0x10 to start it. The dialog object lives for the whole session (the client builds it at UI start): it
/// keeps the seen marks of the files learningQuestCharacter.xml and learningQuestAccount.xml, and opens by itself on a
/// quest or level change when a learning quest not seen before can start (vfunc_72).
/// </summary>
/// <remarks>
/// STILL-OPEN: HasNoHome (mypc.cpp:2075) needs the character's UGC map info (m_sp1618), which the HUD is not fed, so
/// <see cref="HasNoHome"/> answers as the client without it (no home); the model house dialog 0x4f3f it then opens is
/// not in the HUD. FUN_140930b70 (QuestCommon) reads DAT_143ab2878/287c, which nothing feeds here (0: allowed). The
/// status listener's vfunc_12 and which status events reach vfunc_oa0_9 are not traced; the HUD calls
/// <see cref="MarkDirty"/> on a level change and a quest record change.
/// </remarks>
public sealed class LearningQuestController : UiController
{
    /// <summary>CLearningQuestData (learningquesttable.cpp), one &lt;learning&gt; row. <see cref="QuestKind"/> is the
    /// required quest's GetKind (basic account, m_2c): 0 a character quest, else an account quest.</summary>
    public sealed record Learning(int Id, int Category, string Movie, ushort ReqLevel, int ReqQuest, int ReqField, int GotoField,
                                  int GotoPortal, int QuestKind);

    public override string TypeName => "LearningQuestDialog";     // GetTypeName (@0x140ea11b0)
    public override string Movie => "uilearningquestdialog";
    protected override string EntityMethod => "setData";          // LearningQuestDialog

    public QuestTable Table;
    /// <summary>CQuestManager's records and vfunc_1.</summary>
    public IQuestState State;
    /// <summary>CLearningQuestTable's m_map, by id (<see cref="Load"/>).</summary>
    public SortedDictionary<int, Learning> Rows = new();
    /// <summary>WorldService::GetLocalPlayer is set.</summary>
    public Func<bool> HasLocalPlayer = () => false;
    /// <summary>The local player's level (PCBase vfunc_78).</summary>
    public Func<int> PlayerLevel = () => 0;
    /// <summary>WorldService::GetFieldId.</summary>
    public Func<int> FieldId = () => 0;
    /// <summary>GFxSceneService::GetSubSceneId is 0x620001d (the field scene).</summary>
    public Func<bool> InFieldScene = () => true;
    /// <summary>FUN_140930b70 (QuestCommon.Field930b70): the field lets the guide open.</summary>
    public Func<bool> FieldAllows = () => true;
    /// <summary>The GetStatusChar flag (status/displayLearningQuestV2): set, no learning quest opens by itself.</summary>
    public Func<bool> AutoOpenOff = () => false;
    /// <summary>The local player's HasNoHome (see the remarks).</summary>
    public Func<bool> HasNoHome = () => true;
    /// <summary>GetCharacterFolder / GetAccountFolder joined with the two seen files; null without a character.</summary>
    public string CharacterFile, AccountFile;

    /// <summary>Quest mode 0x10 with the learning id (<see cref="Ms2.Net.Packets.QuestLearningRequest"/>).</summary>
    public event Action<int> SendStart;
    /// <summary>CUIService::OpenDialog(0x4f3f), UIUGCModelHouseDialog, while it is closed.</summary>
    public event Action OpenModelHouse;
    /// <summary>FUN_141924680: the dialog opened (ClosePopupMenu, vfunc_8(1)).</summary>
    public event Action OpenRequested;

    ItemSlotBinder binder;                         // m_1a8
    string shownVideo = "";                        // m_1a0
    bool dirty;                                    // m_188
    int learningId;                                // m_18c
    int category;                                  // m_190
    int questId;                                   // m_194
    string movie = "";                             // m_198
    readonly Dictionary<int, int> seen = new();    // m_2c8
    /// <summary>m_310: each row's new mark of the category it heads, set at game start (<see cref="OnGameStart"/>) and
    /// cleared once the tree has shown it. The client keeps it on the one dialog object; a host that builds the
    /// controller again (a new field scene) passes the session's map.</summary>
    public Dictionary<int, int> NewMarks = new();
    XDocument characterDoc = new(), accountDoc = new();   // m_1e8, m_258

    /// <summary>
    /// CLearningQuestTable::Load (@0x14064cc80) over FUN_14028faa0(&lt;ms2&gt;, "learning", "id") (the caller's
    /// FeatureElements.Collect): each row read by CLearningQuestData::Read (@0x14064ca50), a later row of an id replacing
    /// the earlier (SetAt). A row whose required quest has no data or without gotoField stops the load there (the rows read
    /// so far stay, as the client's map keeps them).
    /// </summary>
    public static SortedDictionary<int, Learning> Load(IEnumerable<XElement> rows, QuestTable quests)
    {
        var map = new SortedDictionary<int, Learning>();
        static int Int(XElement e, string a) =>
            int.TryParse((string)e.Attribute(a), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
        static int UInt(XElement e, string a) =>
            uint.TryParse((string)e.Attribute(a), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint v) ? (int)v : 0;
        foreach (var e in rows)
        {
            int reqQuest = UInt(e, "reqQuest");
            var quest = quests?.Quest(reqQuest);
            int gotoField = UInt(e, "gotoField");
            var row = new Learning(Int(e, "id"), Int(e, "category"), (string)e.Attribute("movie") ?? "", (ushort)Int(e, "reqLevel"),
                                   reqQuest, UInt(e, "reqField"), gotoField, Int(e, "gotoPortal"), quest?.Account == true ? 1 : 0);
            if (quest == null || gotoField == 0) break;
            map[row.Id] = row;
        }
        return map;
    }

    // ---- CQuestManager (questmanager_2.cpp) ----------------------------------------------------------------------

    /// <summary>HasQuestInGroup (@0x1416ce340): the character has a record of a quest of the chapter (GetQuestIdsFiltered 4, 11).</summary>
    bool HasQuestInGroup(int chapter) => State.Records.Any(q => Table.Quest(q.Id)?.Chapter == chapter);

    /// <summary>AreGroupQuestsComplete: every quest of the chapter but those excepted from the chapter clear is rewarded.</summary>
    bool AreGroupQuestsComplete(int chapter)
    {
        foreach (uint id in Table.QuestIdsByChapter(chapter))
        {
            var d = Table.Quest(id);
            if (d == null || d.ExceptChapterClear) continue;
            if (State.Get((int)id) is not { State: 2 }) return false;
        }
        return true;
    }

    /// <summary>FUN_1416ce260 (questmanager_2.cpp:1228): a chapter not begun and not done whose first quest can start.</summary>
    bool CanBeginChapter(int chapter)
    {
        if (HasQuestInGroup(chapter) || AreGroupQuestsComplete(chapter)) return false;
        var ids = Table.QuestIdsByChapter(chapter);
        return ids.Count != 0 && State.CanStart((int)ids[0]);
    }

    /// <summary>
    /// FUN_1416d0260 (questmanager_2.cpp:1610): the learning quest can be taken: with the requirements checked its field
    /// (if any) is the current one and the player has its level; the required quest has data; when it is in progress or
    /// rewarded its chapter is not all rewarded, else it can be accepted (vfunc_1).
    /// </summary>
    public bool CanTake(Learning lq, bool checkRequirements)
    {
        if (lq == null || !HasLocalPlayer() || State == null || Table == null) return false;
        if (checkRequirements)
        {
            if (lq.ReqField != 0 && lq.ReqField != FieldId()) return false;
            if (lq.ReqLevel > (ushort)PlayerLevel()) return false;
        }
        var data = Table.Quest(lq.ReqQuest);
        if (data == null) return false;
        var rec = State.Get(lq.ReqQuest);
        if (rec is { IsActive: true } or { IsRewarded: true }) return !AreGroupQuestsComplete(data.Chapter);
        return State.CanStart(lq.ReqQuest);
    }

    /// <summary>CountAvailableLearningQuests (questmanager_2.cpp:1630): the rows <see cref="CanTake"/> without the requirements.</summary>
    public int CountAvailable() => Rows.Values.Count(r => CanTake(r, false));

    // ---- the notifies and listeners --------------------------------------------------------------------------------

    /// <summary>OnChangeServer (@0x140ea1c00): the seen files read, then <see cref="MarkDirty"/>.</summary>
    public void OnChangeServer()
    {
        LoadSeen();
        MarkDirty();
    }

    /// <summary>FUN_140ea1c30 (@0x140ea1c30), NotifyGameStart: each row's new mark is whether it cannot be taken now.</summary>
    public void OnGameStart()
    {
        NewMarks.Clear();
        foreach (var r in Rows.Values) NewMarks[r.Id] = CanTake(r, false) ? 0 : 1;
    }

    /// <summary>FUN_140ea1d70 (@0x140ea1d70), NotifyChangeMyPC: the seen marks dropped.</summary>
    public void OnChangeMyPc() => seen.Clear();

    /// <summary>MarkDirty (@0x140ea3910), from vfunc_o178_2, vfunc_oa0_9 and vfunc_12: with a local player in the field
    /// scene and the seen marks loaded, the next <see cref="Update"/> looks for a new learning quest.</summary>
    public void MarkDirty()
    {
        if (!HasLocalPlayer() || !InFieldScene() || seen.Count == 0) return;
        dirty = true;
    }

    /// <summary>vfunc_72 (@0x140ea1580), each frame: after a <see cref="MarkDirty"/>, <see cref="PickFirst"/>(1).</summary>
    public void Update()
    {
        if (!dirty) return;
        PickFirst(true);
        dirty = false;
    }

    // ---- the dialog ------------------------------------------------------------------------------------------------

    /// <summary>vfunc_67 (@0x140ea1220): the bound slots dropped, the shown video forgotten, then UIDialog's.</summary>
    public override void Close()
    {
        binder?.Clear();
        shownVideo = "";
        base.Close();
    }

    /// <summary>OnLoadComplete (@0x140ea1270): UIDialog's, then <see cref="SetQuestData"/>(1).</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        SetQuestData(true);
    }

    /// <summary>vfunc_28 (@0x140ea12a0), after UIDialog's: onOk and onSelectQuest.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (name.Equals("onOk", StringComparison.OrdinalIgnoreCase))
        {
            if (!HasLocalPlayer() || !Rows.TryGetValue(learningId, out var lq)) return false;
            if (HasNoHome() && lq.GotoField == 62000000) OpenModelHouse?.Invoke();
            else SendStart?.Invoke(learningId);
            return false;
        }
        if (name.Equals("onSelectQuest", StringComparison.OrdinalIgnoreCase))
        {
            if (args is not { Length: > 0 }) return false;           // ASData::SetArgsIfAny
            int quest = (int)Rt.ToUint32(args[0]);                    // PopUInt
            int id = args.Length > 1 ? Rt.ToInt32(args[1]) : 0;      // PopInt
            if (questId != quest && Rows.TryGetValue(id, out var lq))
            {
                questId = quest;
                learningId = id;
                category = lq.Category;
                movie = lq.Movie;
                SetQuestData(false);
            }
            return false;
        }
        return false;
    }

    /// <summary>
    /// FUN_140ea1e20 (@0x140ea1e20): in the field scene with the dialog closed (and, for a new one, the display flag off)
    /// on a field that allows it, the first row in id order that can be taken (the requirements checked for a new one;
    /// a new one also skips the seen rows and marks the one it takes seen, saving the files) becomes the dialog's and the
    /// dialog opens. False when none is found.
    /// </summary>
    public bool PickFirst(bool fresh)
    {
        if (!InFieldScene()) return false;
        if (!IsClosed) return false;                              // FUN_141924630
        if (fresh && AutoOpenOff()) return false;
        if (!FieldAllows()) return false;
        Learning found = null;
        foreach (var r in Rows.Values)
        {
            if (!CanTake(r, fresh)) continue;
            if (fresh)
            {
                if (seen.TryGetValue(r.Id, out int s) && s != 0) continue;
                seen[r.Id] = 1;
                SaveSeen();
            }
            found = r;
            break;
        }
        if (found == null) return false;
        learningId = found.Id;
        questId = found.ReqQuest;
        movie = found.Movie;
        category = found.Category;
        OpenRequested?.Invoke();
        return true;
    }

    /// <summary>
    /// SetQuestData (@0x140ea15d0): the selected quest's accept rewards (bound slots) and complete rewards, the tree when
    /// picking, the video when it changed, setData; then the title (s_learning_quest_name with the category's name) and
    /// the OK button enabled when the learning quest can be taken.
    /// </summary>
    void SetQuestData(bool pick)
    {
        binder ??= new ItemSlotBinder(Model, TypeName);
        var data = new List<(string, object)>();
        if (Table?.Quest(questId) is { } quest)
        {
            var accept = new List<(string, object)>();
            for (int i = 0; i < quest.AcceptRewards.Count; i++)
            {
                var r = quest.AcceptRewards[i];
                accept.Add(("reward" + i, binder.BindNewItem(r.ItemId, r.Rank, r.Count)));
            }
            data.Add(("rewardAccept", Host.Obj(accept.ToArray())));
            data.Add(("rewardComplete", Host.Obj(CompleteReward(quest.Complete).ToArray())));
        }
        if (pick) BuildQuestTree(data);
        if (shownVideo != movie)
        {
            data.Add(("video", $"Common/{movie}.usm"));
            shownVideo = movie;
        }
        Host.Invoke("setData", Host.Obj(data.ToArray()));
        string categoryName = Model.Tables.Common($"s_learning_quest_category_{category}") ?? "";
        if (Host.Child("m_name") is { } nameField)
        {
            string text = ClientFormat.Format(Model.Tables.Common("s_learning_quest_name") ?? "", categoryName);
            text = Model.Tables.ReservedWords.ReplaceValueWords(text, Model.HasLocalPlayer ? Pc.Name : null);
            Host.Set(nameField, "text", text);                         // SetTextW
        }
        if (Host.Child("m_ok") is { } ok)
            Host.Set(ok, "enabled", Rows.TryGetValue(learningId, out var lq) && CanTake(lq, false));
    }

    /// <summary>
    /// BuildQuestTree (@0x140ea2e50): the chapters of the rows' required quests (each with the first row that names it),
    /// sorted by their first quest's level (qsort FUN_140ea3980); a chapter with quests, not all rewarded, begun or ready
    /// to begin, is a category node (open when it holds the selected quest) over its quests not rewarded. The node index
    /// counts each category and, under an open one, each quest; the selected quest's index is the list's selectedIndex.
    /// </summary>
    void BuildQuestTree(List<(string, object)> data)
    {
        var chapters = new List<int>();
        var firstOfChapter = new Dictionary<int, int>();
        foreach (var r in Rows.Values)
        {
            var q = Table.Quest(r.ReqQuest);
            if (q == null) continue;
            if (!chapters.Contains(q.Chapter)) { chapters.Add(q.Chapter); firstOfChapter[q.Chapter] = r.Id; }
        }
        CrtQsort.Sort(chapters, CompareChapters);
        var nodes = new List<object>();
        int node = 0, selected = -1;
        foreach (int chapter in chapters)
        {
            var quests = Table.QuestIdsByChapter(chapter);
            if (quests.Count == 0) continue;
            if (AreGroupQuestsComplete(chapter)) continue;
            if (!HasQuestInGroup(chapter) && !CanBeginChapter(chapter)) continue;
            int first = firstOfChapter.TryGetValue(chapter, out int f) ? f : 0;
            int type = quests.Contains((uint)questId) ? 1 : 2;
            int fresh = NewMarks.TryGetValue(first, out int n) ? n : 1;
            NewMarks[first] = 0;
            var children = new List<object>();
            node++;
            foreach (uint id in quests)
            {
                if (State.Get((int)id) is { IsRewarded: true }) continue;
                children.Add(Host.Obj(("id", id), ("guide", first), ("label", Table.QuestName((int)id)),
                                      ("rendererClassName", "UILearningQuestDialog_renderer_contents")));
                if ((int)id == questId) selected = node;
                if (type == 1) node++;
            }
            nodes.Add(Host.Obj(("label", Table.ChapterText(chapter)?.Name ?? ""), ("type", type),      // GetChapterName
                               ("rendererClassName", "UILearningQuestDialog_renderer_category"), ("nodes", children), ("isNew", fresh)));
        }
        data.Add(("list", Host.Obj(("type", 1), ("nodes", nodes))));
        data.Add(("selectedIndex", selected));
    }

    /// <summary>FUN_140ea3980 (@0x140ea3980): by GetQuestLevel of each chapter's first quest, 1 when greater else -1 (so
    /// never 0 for two chapters with quests); 0 when either has none.</summary>
    int CompareChapters(int a, int b)
    {
        var qa = Table.QuestIdsByChapter(a);
        var qb = Table.QuestIdsByChapter(b);
        if (qa.Count == 0 || qb.Count == 0) return 0;
        ushort la = (ushort)(Table.Quest(qa[0])?.Level ?? 0), lb = (ushort)(Table.Quest(qb[0])?.Level ?? 0);
        return la > lb ? 1 : -1;
    }

    /// <summary>
    /// BuildCompleteReward (@0x140ea3570): exp and meso (s_learning_quest_exp / _meso over the number with the thousands
    /// separator, FUN_14083cb20 of FUN_140719630) when not 0, then the choice rewards as bound slots.
    /// </summary>
    List<(string, object)> CompleteReward(CompleteReward reward)
    {
        var o = new List<(string, object)>();
        if (reward.Exp != 0)
            o.Add(("exp", ClientFormat.Format(Model.Tables.Common("s_learning_quest_exp") ?? "",
                                              NumberText.Group(reward.Exp.ToString(CultureInfo.InvariantCulture)))));
        if (reward.Money != 0)
            o.Add(("meso", ClientFormat.Format(Model.Tables.Common("s_learning_quest_meso") ?? "",
                                               NumberText.Group(reward.Money.ToString(CultureInfo.InvariantCulture)))));
        for (int i = 0; i < reward.ChoiceRewards.Count; i++)
        {
            var r = reward.ChoiceRewards[i];
            o.Add(("reward" + i, binder.BindNewItem(r.ItemId, r.Rank, r.Count)));
        }
        return o;
    }

    // ---- the seen files ---------------------------------------------------------------------------------------------

    /// <summary>
    /// LoadLearningQuest (@0x140ea2120): every row unseen, then learningQuestCharacter.xml's &lt;ms2&gt;&lt;v1
    /// learning{id}=...&gt; for the character quests and learningQuestAccount.xml's for the account quests.
    /// </summary>
    public void LoadSeen()
    {
        seen.Clear();
        foreach (var r in Rows.Values) seen[r.Id] = 0;
        characterDoc = ReadFile(CharacterFile);
        accountDoc = ReadFile(AccountFile);
        void Read(XDocument doc, bool account)
        {
            if (doc.Root is not { Name.LocalName: "ms2" } root || root.Element("v1") is not { } list) return;
            foreach (var r in Rows.Values)
            {
                if ((r.QuestKind != 0) != account) continue;
                int.TryParse((string)list.Attribute($"learning{r.Id}"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v);
                seen[r.Id] = v;      // XmlGetAttr leaves 0 for a missing attribute
            }
        }
        Read(characterDoc, false);
        Read(accountDoc, true);
    }

    /// <summary>
    /// SaveLearningQuest (@0x140ea26e0): &lt;ms2&gt;&lt;v1&gt; made when missing, learning{id} set for each seen mark of
    /// the kind, each file saved. Client quirk kept in part: when the account file has no &lt;ms2&gt;, its declaration goes
    /// into the character document (m_1e8) instead, so the account file is written without one; the character document's
    /// second declaration is not kept (an XDocument holds one).
    /// </summary>
    void SaveSeen()
    {
        XElement List(XDocument doc, bool declare)
        {
            if (doc.Root is not { Name.LocalName: "ms2" } root)
            {
                if (declare) doc.Declaration = new XDeclaration("1.0", "utf-8", null);
                root = new XElement("ms2");
                doc.Add(root);
            }
            var list = root.Element("v1");
            if (list == null) root.Add(list = new XElement("v1"));
            return list;
        }
        var charList = List(characterDoc, true);
        foreach (var (id, v) in seen.OrderBy(kv => kv.Key))     // CAtlMap order: one bin per id for ids below the bin count
            if (Rows.TryGetValue(id, out var r) && r.QuestKind == 0) charList.SetAttributeValue($"learning{id}", v);
        WriteFile(CharacterFile, characterDoc);
        var accList = List(accountDoc, false);
        foreach (var (id, v) in seen.OrderBy(kv => kv.Key))
            if (Rows.TryGetValue(id, out var r) && r.QuestKind != 0) accList.SetAttributeValue($"learning{id}", v);
        WriteFile(AccountFile, accountDoc);
    }

    static XDocument ReadFile(string path)
    {
        try { return path != null && File.Exists(path) ? XDocument.Load(path) : new XDocument(); }
        catch (Exception e) when (e is IOException or XmlException or UnauthorizedAccessException) { return new XDocument(); }
    }

    static void WriteFile(string path, XDocument doc)
    {
        if (path == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var settings = new XmlWriterSettings { Indent = true, IndentChars = "    ", OmitXmlDeclaration = doc.Declaration == null,
                                                   Encoding = new System.Text.UTF8Encoding(false) };
            using var w = XmlWriter.Create(path, settings);
            doc.Save(w);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
