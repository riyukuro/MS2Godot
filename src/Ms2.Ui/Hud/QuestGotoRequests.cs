using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;
using QuestRecord = Ms2.Quest.QuestRecord;

namespace Ms2.Ui.Hud;

/// <summary>
/// The quest go-to requests of CQuestManager (questmanager_2.cpp:1433-1510) that the quest log's buttons
/// (UIQuestDialog::vfunc_28, uiquestdialog.cpp:462-530) and the quest guide's buttons (uiquesteventlistener.cpp:509-553)
/// call. Each looks the quest up (vfunc_o10_3; nothing without a record), checks the same button rule again with the
/// current field, and only then sends.
/// </summary>
public sealed class QuestGotoRequests
{
    public Func<int, QuestData> Data = _ => null;
    public Func<IQuestState> State = () => null;
    public Func<QuestButtonInputs> Inputs = () => new QuestButtonInputs();
    public Func<FieldMeta> Meta = () => null;
    public Func<DungeonRooms> Rooms = () => new DungeonRooms();
    public Action<IOutPacket> Send = _ => { };
    /// <summary>UIPopupNpcTalkDialog(0x4f4d)::StartQuestTalk(quest), the remote complete talk.</summary>
    public Action<int> StartPopupQuestTalk = _ => { };
    /// <summary>
    /// CStoryQuest::FUN_140d73970 (stringtable.cpp:276-288): FindQuestTalk(quest, 300) and UITalkManager::BeginTalkWithoutNpc(script,
    /// complete npc, 4, 0). The quest and the npc; it reports whether it began.
    /// </summary>
    public Func<int, int, bool> TalkWithoutNpc = (_, _) => false;

    (QuestData, QuestRecord) Find(int id)
    {
        var r = State()?.Get(id);
        return r == null ? (null, null) : (Data(id), r);
    }

    /// <summary>The callback name of either dialog to its request; false for any other name.</summary>
    public bool Handle(string name, int id)
    {
        switch (name.ToLowerInvariant())
        {
            case "ongotonpc": case "onmovefield": GotoNpc(id); return true;
            case "ongotodungeon": case "onmovedungeon": GotoDungeon(id); return true;
            case "ongotoreentry": case "onmovereentry": GotoReEntry(id); return true;
            case "ongotodispatch": case "onclickdispatch": Dispatch(id); return true;
            case "ongotofindparty": case "onclickpartyfind": PartyFind(id, false); return true;
            case "ongotofindpartycancel": case "onclickpartyfindcancel": PartyFind(id, true); return true;
        }
        return false;
    }

    /// <summary>SendQuestMode12 @0x1416cf510 (:1433): FUN_1407441f0 (the gotoNpc rule), then Quest u8 0xC, i32 quest.</summary>
    public bool GotoNpc(int id)
    {
        var (d, r) = Find(id);
        if (d == null) return false;
        var live = Inputs();
        if (!QuestCommon.GotoNpc(d, r, live.FieldId, live, Meta(), State())) return false;
        Send(new QuestGotoNpcRequest(id));
        return true;
    }

    /// <summary>SendQuestMode13 @0x1416cf630 (:1446): FUN_1407443e0 (the gotoDungeon rule), then Quest u8 0xD, i32 quest.</summary>
    public bool GotoDungeon(int id)
    {
        var (d, r) = Find(id);
        if (d == null) return false;
        var live = Inputs();
        if (!QuestCommon.GotoDungeon(d, r, live.FieldId, live, Rooms())) return false;
        Send(new QuestGotoDungeonRequest(id));
        return true;
    }

    /// <summary>SendQuestMode19 @0x1416cf750 (:1459): FUN_140744670 (the gotoReEntry rule), then Quest u8 0x13, i32 quest.</summary>
    public bool GotoReEntry(int id)
    {
        var (d, r) = Find(id);
        if (d == null) return false;
        var live = Inputs();
        if (!QuestCommon.GotoReEntry(d, r, live.FieldId, live)) return false;
        Send(new QuestGotoReEntryRequest(id));
        return true;
    }

    /// <summary>
    /// FUN_1416cf870 (:1472): FUN_140d738d0 opens the popup npc talk on the quest (StartQuestTalk); else FUN_140d738c0 runs
    /// CStoryQuest::FUN_140d738e0 (stringtable.cpp:291): m_78 = 1, then the talk without an npc. Returns which one ran: 1, 2 or 0.
    /// </summary>
    public int Dispatch(int id)
    {
        var (d, r) = Find(id);
        if (d == null) return 0;
        var live = Inputs();
        if (QuestCommon.Dispatch738d0(d, r, live)) { StartPopupQuestTalk(id); return 1; }
        if (QuestCommon.Dispatch738c0(d, r, live)) { TalkWithoutNpc(id, d.CompleteNpc); return 2; }
        return 0;
    }

    /// <summary>
    /// FUN_1416cf980 (:1486): HasMultiUserRoom and the room, then Party u8 0x22, u8 Unk_ClubBuffTypeOf(group type) to leave the
    /// search; else Party u8 0x20, u8 that type, i32 1, i32 room, bool false.
    /// </summary>
    public bool PartyFind(int id, bool leave)
    {
        var (d, r) = Find(id);
        if (d == null) return false;
        var live = Inputs();
        var rooms = Rooms();
        if (!QuestCommon.HasMultiUserRoom(d, r, live.FieldId, live, rooms)) return false;
        var room = rooms.GetRoom(d.GotoDungeonRoom);
        if (room == null) return false;
        byte type = (byte)QuestCommon.ClubBuffTypeOf(room.GroupType);
        Send(leave ? new PartyDungeonSearchCancelRequest(type) : new PartyDungeonQuestFindRequest(type, d.GotoDungeonRoom));
        return true;
    }
}

/// <summary>
/// The story quest's dispatch box (CStoryQuest, stringtable.cpp:197-270). DecodeStart sets m_7c = 3 when the quest's
/// &lt;dispatch type&gt; is monologueAccept (3); Complete sets m_7c = 4 for monologueComplete (4). CQuestManager::UpdateQuests
/// (questmanager_2.cpp:674) calls FUN_140d73960 on every quest each second; in the game scene it shows a "monologue" box
/// (0x28): the QuestDispatchScript text (string table 71) formatted with the dispatch field's map name, the player's name as
/// its title, s_quest_dispatch_yes / s_quest_dispatch_no, and the quest id and m_7c as its data. Yes sends SendQuestMode20.
/// The alliance quest's override (type 9, which clears m_7c while the alliance dialog can accept or complete it) is not ported:
/// that dialog is not.
/// </summary>
public sealed class QuestDispatchBoxes
{
    readonly SortedDictionary<int, int> pending = new();     // quest id -> m_7c, walked in m_quests20 (quest id) order

    public Func<int, QuestData> Data = _ => null;
    /// <summary>CStringTable::FUN_141811980(0x47, id).</summary>
    public Func<int, string> Script = _ => "";
    /// <summary>CStringTable::FUN_141811980(3, field), the map name.</summary>
    public Func<int, string> MapName = _ => "";
    public Func<string, string> ReplaceValueWords = s => s;
    public Func<string> PlayerName = () => "";
    /// <summary>GetStringCommonByKey.</summary>
    public Func<string, string> CommonByKey = k => MessageBoxData.CommonByKey(k);
    public Action<MessageBoxData> Show = MessageBoxQueue.Show;
    public Action<IOutPacket> Send = _ => { };

    /// <summary>A box waits for the next second's tick.</summary>
    public bool Pending => pending.Count != 0;

    /// <summary>CStoryQuest::DecodeStart (:197): monologueAccept.</summary>
    public void OnStarted(int id) => Mark(id, 3);

    /// <summary>CStoryQuest::Complete (:241): monologueComplete.</summary>
    public void OnRewarded(int id) => Mark(id, 4);

    void Mark(int id, int kind)
    {
        if (Data(id) is { } d && QuestCommon.IsStory(d.Type) && d.DispatchType == kind) pending[id] = kind;
    }

    /// <summary>The record is gone (RemoveQuest, the list rebuilt): its m_7c with it.</summary>
    public void OnRemoved(int id) => pending.Remove(id);

    public void Clear() => pending.Clear();

    /// <summary>FUN_140d73960 on every quest, once a second; <paramref name="inGame"/> is the sub-scene 0x620001d check.</summary>
    public int Tick(bool inGame)
    {
        if (!inGame || pending.Count == 0) return 0;
        int shown = 0;
        foreach (var (id, kind) in pending)
        {
            if (Data(id) is not { } d) continue;
            Show(Build(id, kind, d));
            ++shown;
        }
        pending.Clear();
        return shown;
    }

    /// <summary>The box of stringtable.cpp:252-266.</summary>
    public MessageBoxData Build(int id, int kind, QuestData d)
    {
        string text = ClientFormat.Format(Script(d.DispatchScript) ?? "", MapName(d.DispatchField) ?? "");
        text = ReplaceValueWords(text);
        var box = MessageBoxQueue.Create(0x28, text, null, "");
        box.Append((uint)id);
        box.Append(kind);
        box.Title = PlayerName() ?? "";
        box.Callback = Answer;
        box.SetButtonLabels(CommonByKey("s_quest_dispatch_yes"), CommonByKey("s_quest_dispatch_no"), "");
        return box;
    }

    /// <summary>CQuestManager::SendQuestMode20 @0x1416d0ea0 (questmanager_2.cpp:1795): only the yes (result 0) sends Quest u8 0x14,
    /// i32 the box's quest id, i16 its m_7c; either answer closes the box.</summary>
    public bool Answer(int result, MessageBoxData box)
    {
        if (result != 0) return true;
        Send(new QuestDispatchYesRequest((int)box.ArgUInt(0, 0), (short)box.ArgInt(1, 0)));
        return true;
    }
}
