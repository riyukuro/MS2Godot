using System.Globalization;
using System.Xml.Linq;
using Ms2.Avm2;
using Ms2.Quest;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// UILevelUpGuideDialog (UI id 0x4efa, ms2/client/ms2/uilevelupguidedialog.cpp): the game menu's level-up guide. Its
/// guide bar lists the level guide quests (type 3, guide type 1) with each one's job reward, its event panel
/// (m_eventDialog) the event guide quests (guide type 2) the character has; the contents list the recommended contents
/// (Table/recommendContents.xml) of the selected level step, filtered by the three kind check boxes. The dialog object is
/// kept between opens (the check boxes and the bound slot names carry over).
/// </summary>
/// <remarks>
/// STILL-OPEN: the quest updates (UIQuestEventListener vfunc_o178_8) need the quest event dispatcher, which the HUD has
/// not got yet; <see cref="OnQuestUpdated"/> is there for it. The boss tooltip's reward preview
/// (WorldMapManager::BuildNpcRewardPreview, worldmapmanager.cpp:1133: CRewardPreviewTable) is not ported, so it shows
/// the text only.
/// </remarks>
public sealed class LevelUpGuideController : UiController
{
    /// <summary>RecommendContnets (@0x140f8a590), one &lt;contents&gt; row. <see cref="Kind"/> is 0 common, 1 boss (Code
    /// is the npc), 2 dungeon; the ctor leaves it unset for another type (the data has none: -1 here).</summary>
    public sealed record Contents(int Id, ushort MinLevel, ushort MaxLevel, int Code, int Sorting, string Type, string Img,
                                  string Link, int Kind);

    public override string TypeName => "LevelUpGuideDialog";      // GetTypeName (@0x140f84520)
    public override string Movie => "uilevelupguidedialog";
    protected override string EntityMethod => "setPC";     // LevelUpGuideDialog; the dialog comes before its m_eventDialog

    public QuestTable Table;
    /// <summary>CQuestManager's records (vfunc_o10_3).</summary>
    public IQuestState State;
    /// <summary>The npcs (FindNpcData) of the boss rows.</summary>
    public Ms2.Formats.World.NpcTable Npcs;
    /// <summary>CFieldMetaTable's npc fields (FindNpcField).</summary>
    public FieldMeta Meta;
    /// <summary>FUN_1406fbb50 for an (npc, field) pair with a time window: the server time is inside it.</summary>
    public Func<int, int, bool> NpcFieldOpen = (_, _) => false;
    /// <summary>The rows by id (m_1e0, a CRBMap), from <see cref="LoadContents"/>.</summary>
    public SortedDictionary<int, Contents> Rows = new();

    /// <summary>Quest mode 0x18 with the quest (<see cref="Ms2.Net.Packets.QuestGuideRewardRequest"/>).</summary>
    public event Action<int> SendGuideReward;
    /// <summary>The world map opened (FUN_141924680) with a pending field (0: none) and search text ("": none).</summary>
    public event Action<uint, string> OpenWorldMap;
    /// <summary>UIDungeonRoomDialog (0x4ebd): RequestCloseDialog, OpenRoom(code), OpenDialog.</summary>
    public event Action<int> OpenDungeonRoom;
    /// <summary>Another row's link: CUIService::FindObjectByName(link) as a UIDialog, opened (FUN_141924680).</summary>
    public event Action<string> OpenDialog;

    readonly List<ushort> levelSteps = new();      // m_188
    readonly List<string> slots = new();           // m_1a8
    ushort low, high;                              // m_1c8
    readonly bool[] filters = { true, true, true };   // m_1cc (ctor 1, 1, 1)
    ushort maxStep;                                // m_1d8

    static readonly string[] FilterBoxes = { "m_contentsEtc", "m_contentsBoss", "m_contentsDungeon" };

    /// <summary>
    /// LoadRecommendContents (@0x140f85ba0, the ctor's): each &lt;contents&gt; under &lt;ms2&gt; that IsElementAvailable,
    /// its id, minLevel, maxLevel, code, sorting (0x7fffffff when missing), type, img and link; a later row of the same id
    /// replaces the earlier (SetAt).
    /// </summary>
    public static SortedDictionary<int, Contents> LoadContents(XDocument doc, Func<XElement, bool> available)
    {
        var rows = new SortedDictionary<int, Contents>();
        if (doc?.Root is not { Name.LocalName: "ms2" } root) return rows;
        static int Int(XElement e, string a, int def) =>
            int.TryParse((string)e.Attribute(a), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : def;
        foreach (var e in root.Elements("contents"))
        {
            if (!available(e)) continue;
            string type = (string)e.Attribute("type") ?? "";
            int kind = type switch { "common" => 0, "boss" => 1, "dungeon" => 2, _ => -1 };
            var row = new Contents(Int(e, "id", 0), (ushort)Int(e, "minLevel", 0), (ushort)Int(e, "maxLevel", 0), Int(e, "code", 0),
                                   Int(e, "sorting", 0x7fffffff), type, (string)e.Attribute("img") ?? "", (string)e.Attribute("link") ?? "", kind);
            rows[row.Id] = row;
        }
        return rows;
    }

    /// <summary>
    /// vfunc_67 (@0x140f84610): the bound slots destroyed (the names stay listed, so the next ones count on), the check
    /// boxes' states kept, then UIDialog's.
    /// </summary>
    public override void Close()
    {
        foreach (var s in slots) Model.ItemSlots.Remove(s);      // DestroyItemInfoSlot
        for (int i = 0; i < 3; i++)
            if (Host.Child(FilterBoxes[i]) is { } box) filters[i] = Rt.ToBoolean(Host.Get(box, "selected"));
        base.Close();
    }

    /// <summary>
    /// OnLoadComplete (@0x140f84900): UIDialog's; with a local player the level steps for its level, the check boxes as
    /// kept, the guide bar, the contents and the event guides.
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (!Model.HasLocalPlayer) return;
        CollectLevelSteps((ushort)Pc.Level);
        for (int i = 0; i < 3; i++)
            if (Host.Child(FilterBoxes[i]) is { } box) Host.Set(box, "selected", filters[i]);
        FillGuideBar();
        UpdateContentsList();
        FillEventGuides();
    }

    /// <summary>vfunc_28 (@0x140f84bb0), after UIDialog's: onReceiveItem, onClickContents, onContentsFilter, onLevelSelect.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        object Arg(int i) => args != null && i < args.Length ? args[i] : null;
        if (name.Equals("onReceiveItem", StringComparison.OrdinalIgnoreCase))
        {
            if (args is { Length: > 0 }) ReceiveItem((int)Rt.ToUint32(Arg(0)));       // PopUInt
        }
        else if (name.Equals("onClickContents", StringComparison.OrdinalIgnoreCase))
        {
            if (args is { Length: > 0 }) OpenLinkedDialog(Rt.ToInt32(Arg(0)));
        }
        else if (name.Equals("onContentsFilter", StringComparison.OrdinalIgnoreCase))
            UpdateContentsList();
        else if (name.Equals("onLevelSelect", StringComparison.OrdinalIgnoreCase))
        {
            if (args is { Length: > 0 })
            {
                SetLevelRange((ushort)Rt.ToInt32(Arg(0)));      // PopInt into an unsigned short
                UpdateContentsList();
            }
        }
        else return false;
        return true;
    }

    /// <summary>vfunc_49 (@0x140f84dc0): a "contents" row's tooltip, by its id: a boss row's or a common row's text.</summary>
    public override IDictionary<string, object> GetTooltip(object[] args)
    {
        if (args is not { Length: > 0 } || Rt.ToString(args[0]) != "contents") return null;
        int id = args.Length > 1 ? Rt.ToInt32(args[1]) : 0;
        if (!Rows.TryGetValue(id, out var row)) return null;
        return row.Kind switch { 1 => BossTooltip(row), 0 => TextTooltip(row), _ => null };
    }

    /// <summary>vfunc_oa0_9 (@0x140f851f0): a level change sets the bar's player level, the level range and the contents.</summary>
    public override void OnStatus(StatusEvent e)
    {
        if (e != StatusEvent.Level || Host?.Entity == null) return;
        ushort level = (ushort)Pc.Level;
        Host.Invoke("setPC", (uint)level);
        SetLevelRange(level);
        UpdateContentsList();
    }

    /// <summary>
    /// vfunc_o178_8 (@0x140f84f30): while open, an updated quest with data is added again (addLevelGuide updates its entry):
    /// a level guide quest to the bar, an event guide quest to the event panel.
    /// </summary>
    public void OnQuestUpdated(int id)
    {
        if (IsClosed || Host?.Entity == null) return;
        if (Table?.Quest(id) is not { } q) return;
        if (q.GuideType == 1)
        {
            var obj = new List<(string, object)>();
            if (GuideRow(obj, id)) Host.Invoke("addLevelGuide", Host.Obj(obj.ToArray()));
        }
        else if (q.GuideType == 2 && Host.Child("m_eventDialog") is { } ev)
        {
            var obj = new List<(string, object)>();
            if (GuideRow(obj, id)) Host.Invoke(ev, "addLevelGuide", Host.Obj(obj.ToArray()));
        }
    }

    /// <summary>
    /// FUN_140f857e0 (@0x140f857e0): the reward of a completed guide quest with data, with a local player. A level guide
    /// quest of a beginner (job group 1) asks first (CreateMessageBox(1, s_recommend_contents_newbie_warn), the quest
    /// appended, SendQuestMode24 @0x140f85290 answering: OK sends); otherwise Quest 0x18 is sent at once.
    /// </summary>
    void ReceiveItem(int quest)
    {
        if (State?.Get(quest) is not { Complete: true }) return;
        if (Table?.Quest(quest) is not { } q) return;
        if (!Model.HasLocalPlayer) return;
        if (q.GuideType == 1 && Pc.JobGroup == 1)
        {
            var box = MessageBoxQueue.Create(1, Model.Tables.Common("s_recommend_contents_newbie_warn") ?? "", null, "");
            box.Append((uint)quest);
            box.Callback = (result, d) =>
            {
                if (result != 0) return true;
                SendGuideReward?.Invoke((int)d.ArgUInt(0, 0));
                return true;
            };
            MessageBoxQueue.Show(box);       // FUN_140d73c60
        }
        else SendGuideReward?.Invoke(quest);
    }

    /// <summary>
    /// CollectLevelSteps (@0x140f85330): the levels of the level guide quests with a condition (m_188) and the highest
    /// (m_1d8), then the range for the level.
    /// </summary>
    void CollectLevelSteps(ushort level)
    {
        levelSteps.Clear();
        maxStep = 0;
        foreach (var q in Table?.CollectType3QuestsByGuideType(1) ?? new List<QuestData>())
        {
            if (q.Conditions.Count == 0) continue;
            ushort l = QuestLevel(q);
            if (l > maxStep) maxStep = l;
            levelSteps.Add(l);
        }
        SetLevelRange(level);
    }

    /// <summary>LgQuestLevel: the first target value of the quest's first condition.</summary>
    static ushort QuestLevel(QuestData q) => (ushort)q.Conditions[0].Target.FirstValue;

    /// <summary>
    /// OpenLinkedDialog (@0x140f854b0): a row's link. The dungeon room dialog opens the room of its code; the world map
    /// takes a common or dungeon row's code as the field and its search text (string table 0x24), a boss row's npc's
    /// field (FindNpcField); another link opens the dialog of that name.
    /// </summary>
    void OpenLinkedDialog(int id)
    {
        if (!Rows.TryGetValue(id, out var row) || row.Link.Length == 0) return;
        if (row.Link == "DungeonRoomDialog") OpenDungeonRoom?.Invoke(row.Code);
        else if (row.Link == "WorldMapDialog")
        {
            uint field = 0;
            string search = "";
            if (row.Kind is 0 or 2)
            {
                if (row.Code != 0) field = (uint)row.Code;
                search = Model.Tables.RecommendContentsSearch(row.Id);
            }
            else if (FindNpcField(row.Code) is int f) field = (uint)f;
            OpenWorldMap?.Invoke(field, search);
        }
        else OpenDialog?.Invoke(row.Link);
    }

    /// <summary>CFieldMetaTable::FindNpcField (@0x1406fb700, fieldmetatable.cpp:505): the first of the npc's fields whose
    /// time window, if it has one, holds (FUN_1406fbb50).</summary>
    int? FindNpcField(int npc)
    {
        if (Meta == null || !Meta.NpcFields.TryGetValue(npc, out var fields)) return null;
        foreach (int f in fields)
            if (!Meta.TimedNpcFields.Contains((npc, f)) || NpcFieldOpen(npc, f)) return f;
        return null;
    }

    /// <summary>
    /// FUN_140f85f90 (@0x140f85f90): the level guide quests in step order (qsort with LgQuestOrder @0x140f87c80: a quest
    /// without exactly one condition last, else by level), each with a condition added to the bar; with a local player
    /// the bar's player level.
    /// </summary>
    /// <remarks>LgQuestOrder never answers 0; every level guide quest of the data has one condition and its own level, so
    /// a sort by (one condition, level) gives the qsort's order.</remarks>
    void FillGuideBar()
    {
        if (Host?.Entity == null) return;
        var quests = (Table?.CollectType3QuestsByGuideType(1) ?? new List<QuestData>())
            .OrderBy(q => q.Conditions.Count != 1 ? 1 : 0).ThenBy(q => q.Conditions.Count == 1 ? QuestLevel(q) : 0).ToList();
        foreach (var q in quests)
        {
            if (q.Conditions.Count == 0) continue;
            var obj = new List<(string, object)>();
            if (GuideRow(obj, q.Id)) Host.Invoke("addLevelGuide", Host.Obj(obj.ToArray()));
        }
        if (!Model.HasLocalPlayer) return;
        Host.Invoke("setPC", (uint)(ushort)Pc.Level);
    }

    /// <summary>
    /// UpdateContentsList (@0x140f861c0): the range's title (s_recommend_contents_title low, high, or
    /// s_recommend_contents_title_equal), then the list: resetRenderer, the provider's cleanUp, scroll 0, the rows in the
    /// range by (sorting, id) whose kind's check box is not cleared, each {id, type, label (string table 0x23), imgPath,
    /// clickable (a link)}; vfunc_24(true).
    /// </summary>
    void UpdateContentsList()
    {
        if (Host?.Entity == null) return;
        if (Host.Child("m_contentsLevel") is { } title)
        {
            string text = low != high
                ? ClientFormat.Format(Model.Tables.Common("s_recommend_contents_title") ?? "", low, high)
                : ClientFormat.Format(Model.Tables.Common("s_recommend_contents_title_equal") ?? "", low);
            Host.Set(title, "text", text);      // SetTextW
        }
        if (Host.Child("m_contentsList") is not { } list) return;
        Host.Invoke(list, "resetRenderer");
        var dp = Host.Get(list, "dataProvider");
        if (!Rt.IsNullish(dp)) Host.Invoke(dp, "cleanUp");       // ASCoreList FUN_1422dd1d0 (asdataprovider.cpp:61)
        Host.Set(list, "scrollPosition", 0);                     // ASTileList::SetScrollPosition (astilelist.cpp:84)
        var rows = Rows.Values.Where(InRange).OrderBy(r => r.Sorting).ThenBy(r => r.Id).ToList();   // std::sort, LgRowOrder
        foreach (var row in rows)
        {
            if (row.Kind is >= 0 and <= 2 && Host.Child(FilterBoxes[row.Kind]) is { } box && !Rt.ToBoolean(Host.Get(box, "selected"))) continue;
            if (!Rt.IsNullish(dp))
                Host.Invoke(dp, "push", Host.Obj(("id", row.Id), ("type", row.Type), ("label", Model.Tables.RecommendContentsName(row.Id)),
                                                 ("imgPath", row.Img), ("clickable", row.Link.Length != 0)));   // AddItem (ascorelist.cpp:159)
        }
        Host.Invoke(list, "invalidate");
        Host.Invoke(list, "validateNow");
    }

    /// <summary>
    /// FUN_140f869f0 (@0x140f869f0): with a local player, the event guide quests (guide type 2) the character has, not
    /// expired, in progress or rewarded, with a condition: each added to the event panel with disableEvent 1. Added ones
    /// set the panel's player level and show it; else it is hidden.
    /// </summary>
    void FillEventGuides()
    {
        if (!Model.HasLocalPlayer) return;
        if (Host.Child("m_eventDialog") is not { } ev) return;
        bool added = false;
        foreach (var q in Table?.CollectType3QuestsByGuideType(2) ?? new List<QuestData>())
        {
            if (State?.Get(q.Id) is not { } rec) continue;
            if (rec.Expired) continue;
            if (rec.State != 1 && rec.State != 2) continue;
            if (q.Conditions.Count == 0) continue;
            var obj = new List<(string, object)> { ("disableEvent", 1) };
            if (GuideRow(obj, q.Id))
            {
                Host.Invoke(ev, "addLevelGuide", Host.Obj(obj.ToArray()));
                added = true;
            }
        }
        if (added) Host.Invoke(ev, "setPC", (uint)(ushort)Pc.Level);
        Host.SetVisible(ev, added);
    }

    /// <summary>
    /// FUN_140f86dd0 (@0x140f86dd0): a guide entry: id, state ("active" for a complete quest, "inactive" for a rewarded
    /// one, else "lock"), level, levelStr (s_recommend_contents_max_level at the highest step, else
    /// s_recommend_contents_level), then the reward's binding. False without quest data, the movie or a condition.
    /// </summary>
    bool GuideRow(List<(string, object)> obj, int quest)
    {
        if (Table?.Quest(quest) is not { } q) return false;
        if (Host?.Entity == null) return false;
        var rec = State?.Get(quest);
        string state = rec is { Complete: true } ? "active" : rec is { IsRewarded: true } ? "inactive" : "lock";
        if (q.Conditions.Count == 0) return false;
        ushort level = QuestLevel(q);
        string levelStr = ClientFormat.Format(Model.Tables.Common(level == maxStep ? "s_recommend_contents_max_level" : "s_recommend_contents_level") ?? "", level);
        obj.Add(("id", (uint)q.Id));
        obj.Add(("state", state));
        obj.Add(("level", (int)(short)level));      // FUN_1409c48f0 (a short)
        obj.Add(("levelStr", levelStr));
        BindReward(obj, q);
        return true;
    }

    /// <summary>
    /// FUN_140f87270 (@0x140f87270, bName 0): a level or event guide quest's last choice reward with an item recommended
    /// for the player's job group (FUN_140748050), made as a new item (rank as the rarity) with its count, bound as
    /// "LevelUpGuideDialog.&lt;n&gt;" (CreateItemInfoSlot, icon type 0x415, flags 0x800; n counts every name bound before).
    /// </summary>
    void BindReward(List<(string, object)> obj, QuestData q)
    {
        if (!Model.HasLocalPlayer) return;
        QuestRewardItem reward = null;
        if (q.GuideType is 1 or 2)
            foreach (var item in q.Complete.ChoiceRewards)
                if (item.ItemId != 0 && Model.Tables.Items.Get(item.ItemId) is { } d && d.IsJobRecommended(Pc.JobGroup))
                    reward = item;
        if (reward == null) return;
        var made = Item.Create(Model.Tables.Items, reward.ItemId, reward.Rank);     // FUN_14052b130
        if (made == null) return;
        made.SetCountClamped(reward.Count);
        string bind = string.Create(CultureInfo.InvariantCulture, $"{TypeName}.{slots.Count + 1}");
        Model.ItemSlots[bind] = new ItemInfoSlot(bind, made, 0, 0x415, 0x800);
        obj.Add(("binding", bind));
        slots.Add(bind);
    }

    /// <summary>
    /// BuildBossTooltip (@0x140f876b0): for a boss row's npc with data, s_recommend_contents_tooltip_boss (its name and
    /// level), enableLClick "", the WorldMapTooltip_Boss class, delays 300 / 60000, align 1.
    /// </summary>
    AsTooltip BossTooltip(Contents row)
    {
        int npc = row.Code;
        if (Npcs?.Find(npc) is not { } n) return null;
        var tip = new AsTooltip();
        // STILL-OPEN: WorldMapManager::BuildNpcRewardPreview(npc, tip) (the "reward" previews, CRewardPreviewTable)
        tip["text"] = ClientFormat.Format(Model.Tables.Common("s_recommend_contents_tooltip_boss") ?? "", NpcName(n), (ushort)n.Level);
        tip["enableLClick"] = "";
        tip["tooltip"] = "WorldMapTooltip_Boss";        // SetTooltipClass
        tip["onDelay"] = 300;
        tip["offDelay"] = 60000;
        tip.SetTooltipAlign(1);
        return tip;
    }

    /// <summary>BuildTextTooltip (@0x140f878e0): string table 0x25's text for the row, as the text tooltip with align 1; none
    /// when empty.</summary>
    AsTooltip TextTooltip(Contents row)
    {
        string text = Model.Tables.RecommendContentsTooltip(row.Id);
        if (text.Length == 0) return null;
        var tip = new AsTooltip();
        tip["tooltip"] = "export_common_tooltip_tooltipText";
        tip["onDelay"] = 300;
        tip["offDelay"] = 60000;
        tip.SetTooltipAlign(1);
        tip["text"] = text;                             // SetText
        return tip;
    }

    /// <summary>FUN_1401aa6a0 (arcademaindata.cpp:60): "&lt;title&gt; &lt;name&gt;", or the name alone without a title.</summary>
    static string NpcName(Ms2.Formats.World.NpcRow n) => string.IsNullOrEmpty(n.Title) ? n.Name ?? "" : $"{n.Title} {n.Name}";

    /// <summary>FUN_140f87a30 (@0x140f87a30): a level from the row's min to max is in the shown range
    /// (Unk_DcLevelRange::FUN_1407790e0: low &lt;= level &lt;= high).</summary>
    bool InRange(Contents row)
    {
        for (int l = row.MinLevel; l <= row.MaxLevel; l++)
            if (low <= l && l <= high) return true;
        return false;
    }

    /// <summary>
    /// SetLevelRange (@0x140f87af0): from the highest step at or below the level (1 without one) to the step after it,
    /// less one (the highest step without one); at the highest step the range is that level alone.
    /// </summary>
    void SetLevelRange(ushort level)
    {
        ushort lo = 1, hi = maxStep;
        foreach (ushort s in levelSteps)
        {
            if (s <= level) { if (lo < s) lo = s; }
            else if (hi > s) hi = s;
        }
        low = lo;
        high = (ushort)(hi - 1);
        if (low == maxStep) high = maxStep;
    }
}
