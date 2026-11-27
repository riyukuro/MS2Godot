using Ms2.Formats.Pack;
using Maple2.PacketLib.Tools;
using Ms2.Formats.World;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;
using Ms2.Ui.Hud;
using NUnit.Framework;
using QuestRecord = Ms2.Quest.QuestRecord;

namespace Ms2.Ui.Tests;

/// <summary>
/// The quest windows' go-to requests (CQuestManager::SendQuestMode12 / 13 / 19, FUN_1416cf870, FUN_1416cf980) and the story
/// quest's dispatch box (CStoryQuest::FUN_140d73960, SendQuestMode20).
/// </summary>
public class QuestGotoTests
{
    sealed class States : IQuestState
    {
        public readonly Dictionary<int, QuestRecord> Map = new();
        public IReadOnlyList<QuestRecord> Records => Map.Values.ToList();
        public QuestRecord Get(int questId) => Map.TryGetValue(questId, out var r) ? r : null;
        public bool CanStart(int questId) => false;
        public int PlayerLevel => 1;
        public long CharacterId => 1;
        public bool NpcFieldOpen(int npcId, int fieldId) => true;
    }

    static QuestRecord Rec(int id, int state, bool complete) => new(id, state, 0, 0, 0, complete, false);

    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    static DungeonRooms Rooms(int group = 1) => DungeonRooms.Parse(System.Xml.Linq.XDocument.Parse(
        $@"<ms2><dungeonRoom dungeonRoomID=""7"" groupType=""{group}"" fieldIDs=""50,51"" maxUserCount=""4"" /></ms2>"), _ => true);

    static (QuestGotoRequests Goto, States State, List<IOutPacket> Sent, QuestButtonInputs Live) Rig(QuestData d)
    {
        var st = new States();
        var sent = new List<IOutPacket>();
        var live = new QuestButtonInputs { FieldId = 10, FieldExists = _ => true };
        var g = new QuestGotoRequests
        {
            Data = id => id == d.Id ? d : null,
            State = () => st,
            Inputs = () => live,
            Meta = () => FieldMeta.Parse(@"<ms2><npc id=""5""><field id=""20"" /></npc></ms2>"),
            Rooms = () => Rooms(),
            Send = sent.Add,
        };
        return (g, st, sent, live);
    }

    [Test]
    public void TheNewRequestsKeepTheClientBytes()
    {
        // SendQuestMode19 (questmanager_2.cpp:1459): Quest u8 0x13, i32 quest
        Assert.That(Bytes(new QuestGotoReEntryRequest(0x01020304)), Is.EqualTo(new byte[] { 0x13, 4, 3, 2, 1 }));
        // FUN_1416cf980 (:1486): Party u8 0x20, u8 type, i32 1, i32 room, bool false
        Assert.That(Bytes(new PartyDungeonQuestFindRequest(2, 21001001)),
            Is.EqualTo(new byte[] { 0x20, 2, 1, 0, 0, 0, 0x29, 0x73, 0x40, 0x01, 0 }));
        Assert.That(new PartyDungeonQuestFindRequest(2, 1).Op, Is.EqualTo(Outbound.Party));
        Assert.That(new QuestGotoReEntryRequest(1).Op, Is.EqualTo(Outbound.Quest));
    }

    [Test]
    public void GotoNpcSendsOnlyWhenTheRuleHolds()
    {
        var d = new QuestData { Id = 3, GotoNpcEnable = 1, GotoNpcField = 20, CompleteNpc = 5 };
        var (g, st, sent, live) = Rig(d);
        Assert.That(g.GotoNpc(3), Is.False, "no record: vfunc_o10_3 is null");
        st.Map[3] = Rec(3, 1, false);
        Assert.That(g.GotoNpc(3), Is.False, "not complete");
        st.Map[3] = Rec(3, 1, true);
        live.FieldId = 20;
        Assert.That(g.GotoNpc(3), Is.False, "already in the target field");
        live.FieldId = 10;
        Assert.That(g.Handle("onMoveField", 3), Is.True);
        Assert.That(sent.Select(Bytes), Is.EqualTo(new[] { new byte[] { 0xC, 3, 0, 0, 0 } }));
    }

    [Test]
    public void GotoDungeonRechecksTheFieldAndTheService()
    {
        var d = new QuestData { Id = 4, GotoDungeonState = 1, GotoDungeonRoom = 7 };
        var (g, st, sent, live) = Rig(d);
        st.Map[4] = Rec(4, 1, false);
        live.ServiceType = 2;
        Assert.That(g.GotoDungeon(4), Is.False, "FUN_140930b70");
        live.ServiceType = 0;
        live.FieldId = 51;
        Assert.That(g.GotoDungeon(4), Is.False, "in a field of the room");
        live.FieldId = 10;
        Assert.That(g.Handle("ongotodungeon", 4), Is.True);
        Assert.That(sent.Select(Bytes), Is.EqualTo(new[] { new byte[] { 0xD, 4, 0, 0, 0 } }));
    }

    [Test]
    public void GotoInstanceSendsModeDAndHidesInsideTheInstance()
    {
        var d = new QuestData { Id = 6, GotoDungeonState = 3, GotoDungeonField = 1100001 };
        var (g, st, sent, live) = Rig(d);
        st.Map[6] = Rec(6, 1, false);
        live.InstanceId = 1100001;
        Assert.That(g.GotoDungeon(6), Is.False, "DAT_143ab2880 is the instance");
        live.InstanceId = 0;
        live.ServiceType = 2;
        Assert.That(g.GotoDungeon(6), Is.True, "no room: FUN_140930b70 is not asked");
        Assert.That(sent.Select(Bytes), Is.EqualTo(new[] { new byte[] { 0xD, 6, 0, 0, 0 } }));
    }

    [Test]
    public void ReEntryAndPartyFindSendTheirPackets()
    {
        var d = new QuestData { Id = 8, GotoDungeonState = 3, GotoDungeonRoom = 7, UseReEntryHiddenMap = true };
        var (g, st, sent, live) = Rig(d);
        st.Map[8] = Rec(8, 1, false);
        live.DirectQuestFindParty = true;
        live.LocalPlayerAlive = true;
        live.HasFieldData = true;
        d.Fields = new uint[] { 30 };
        Assert.That(g.GotoReEntry(8), Is.False, "FUN_1409312f0: no channel (DAT_1439f4a7c 0)");
        live.Flag1439f4a7c = true;
        live.ServiceType = 1;
        Assert.That(g.GotoReEntry(8), Is.False, "FUN_1409312f0: FUN_140930b10, service type not 0");
        live.ServiceType = 0;
        live.FieldId = 30;
        Assert.That(g.GotoReEntry(8), Is.False, "already in the first field");
        live.FieldId = 10;
        Assert.That(g.Handle("onMoveReEntry", 8), Is.True);
        Assert.That(sent.Select(Bytes), Is.EqualTo(new[] { new byte[] { 0x13, 8, 0, 0, 0 } }), "SendQuestMode19");
        bool party = QuestCommon.HasMultiUserRoom(d, st.Map[8], live.FieldId, live, Rooms());
        Assert.That(party, Is.True);
        sent.Clear();
        Assert.That(g.Handle("onClickPartyFind", 8), Is.True);
        Assert.That(g.Handle("onClickPartyFindCancel", 8), Is.True);
        byte type = (byte)QuestCommon.ClubBuffTypeOf(1);
        Assert.That(sent.Select(Bytes), Is.EqualTo(new[]
        {
            new byte[] { 0x20, type, 1, 0, 0, 0, 7, 0, 0, 0, 0 },
            Bytes(new PartyDungeonSearchCancelRequest(type)),
        }));
        Assert.That(g.Handle("onClose", 8), Is.False);
    }

    [Test]
    public void DispatchButtonOpensThePopupTalkForAFieldQuest()
    {
        var d = new QuestData { Id = 9, Type = 8 };
        var (g, st, _, live) = Rig(d);
        int popup = 0;
        g.StartPopupQuestTalk = q => popup = q;
        st.Map[9] = Rec(9, 1, false);
        Assert.That(g.Dispatch(9), Is.EqualTo(0));
        st.Map[9] = Rec(9, 1, true);
        Assert.That(g.Dispatch(9), Is.EqualTo(1));
        Assert.That(popup, Is.EqualTo(9));
    }

    static QuestDispatchBoxes Boxes(QuestData d, List<MessageBoxData> shown, List<IOutPacket> sent) => new()
    {
        Data = id => id == d.Id ? d : null,
        Script = s => s == 1 ? "Go to {0} now?" : "",
        MapName = f => f == 2000313 ? "Tria" : "",
        ReplaceValueWords = t => t,
        PlayerName = () => "Hero",
        CommonByKey = k => k,
        Show = shown.Add,
        Send = sent.Add,
    };

    [Test]
    public void TheDispatchBoxWaitsForTheTickAndAsksTheClientText()
    {
        var d = new QuestData { Id = 10003105, Type = 0, DispatchType = 3, DispatchField = 2000313, DispatchScript = 1 };
        var shown = new List<MessageBoxData>();
        var sent = new List<IOutPacket>();
        var b = Boxes(d, shown, sent);
        b.OnRewarded(10003105);
        Assert.That(b.Pending, Is.False, "monologueAccept is set by DecodeStart only");
        b.OnStarted(10003105);
        Assert.That(b.Tick(false), Is.EqualTo(0), "not in the game sub-scene: m_7c stays");
        Assert.That(b.Tick(true), Is.EqualTo(1));
        Assert.That(b.Pending, Is.False, "m_7c = 0 after the box");
        var box = shown.Single();
        Assert.That((box.Type, box.Msg, box.Title, box.Btn0, box.Btn1), Is.EqualTo((0x28, "Go to Tria now?", "Hero", "s_quest_dispatch_yes", "s_quest_dispatch_no")));
        Assert.That(box.Args, Is.EqualTo(new[] { "10003105", "3" }));
        Assert.That(box.Callback(1, box), Is.True);
        Assert.That(sent, Is.Empty, "no: nothing sent");
        Assert.That(box.Callback(0, box), Is.True);
        Assert.That(sent.Select(Bytes), Is.EqualTo(new[] { new byte[] { 0x14, 0xA1, 0xA2, 0x98, 0, 3, 0 } }));
    }

    [Test]
    public void TheDispatchBoxNeedsAStoryQuestAndItsOwnKind()
    {
        var shown = new List<MessageBoxData>();
        var b = Boxes(new QuestData { Id = 1, Type = 3, DispatchType = 3 }, shown, new List<IOutPacket>());
        b.OnStarted(1);
        Assert.That(b.Pending, Is.False, "type 3 is not a CStoryQuest");
        var c = Boxes(new QuestData { Id = 2, Type = 1, DispatchType = 4 }, shown, new List<IOutPacket>());
        c.OnStarted(2);
        Assert.That(c.Pending, Is.False);
        c.OnRewarded(2);
        Assert.That(c.Pending, Is.True, "Complete: monologueComplete");
        c.OnRemoved(2);
        Assert.That(c.Pending, Is.False);
        var e = Boxes(new QuestData { Id = 3, Type = 0, DispatchType = 1 }, shown, new List<IOutPacket>());
        e.OnStarted(3);
        e.OnRewarded(3);
        Assert.That(e.Pending, Is.False, "directAccept / directComplete raise no box");
    }

    [Test]
    public void TheGuideAndTheLogNamesMapToTheSameRequests()
    {
        var d = new QuestData { Id = 3, GotoNpcEnable = 1, GotoNpcField = 20, CompleteNpc = 5 };
        var (g, st, sent, _) = Rig(d);
        st.Map[3] = Rec(3, 1, true);
        Assert.That(g.Handle("ongotonpc", 3) && g.Handle("onMoveField", 3), Is.True);
        Assert.That(sent.Count, Is.EqualTo(2));
        foreach (var n in new[] { "onGotoDungeon", "onMoveDungeon", "onGotoReEntry", "onMoveReEntry", "onGotoDispatch", "onClickDispatch",
                     "onGotoFindParty", "onGotoFindPartyCancel" })
            Assert.That(g.Handle(n, 3), Is.True, n);
        Assert.That(g.Handle("onTrace", 3), Is.False);
    }

    [Test]
    public void TheGuideRaisesGotoForItsSixNamesWithTheFirstArgument()
    {
        // UIQuestEventListener vfunc_28 (uiquesteventlistener.cpp:509-553): PopUInt of the first argument, the movie call is not taken
        var guide = new QuestGuideController();
        var got = new List<(string, int)>();
        guide.Goto += (n, id) => got.Add((n, id));
        var names = new[] { "onMoveField", "onMoveDungeon", "onMoveReEntry", "onClickDispatch", "onClickPartyFind", "onClickPartyFindCancel" };
        foreach (var n in names) Assert.That(guide.OnCallback(n, new object[] { 3.0 }), Is.False, n);
        Assert.That(got, Is.EqualTo(names.Select(n => (n, 3)).ToList()));
        got.Clear();
        Assert.That(guide.OnCallback("onMoveField", Array.Empty<object>()), Is.False);
        foreach (var n in new[] { "onClose", "onTrace", "onLinkQuestDialog", "onGotoNpc" }) guide.OnCallback(n, new object[] { 3.0 });
        Assert.That(got, Is.Empty, "no argument, or a name that is not a go-to");

        // the HUD hands the event to QuestGotoRequests.Handle: the guide's "onMoveField" sends SendQuestMode12
        var d = new QuestData { Id = 3, GotoNpcEnable = 1, GotoNpcField = 20, CompleteNpc = 5 };
        var (g, st, sent, _) = Rig(d);
        st.Map[3] = Rec(3, 1, true);
        guide.Goto += (n, id) => g.Handle(n, id);
        guide.OnCallback("onMoveField", new object[] { 3.0 });
        Assert.That(sent.Select(Bytes), Is.EqualTo(new[] { new byte[] { 0xC, 3, 0, 0, 0 } }));
    }
}

/// <summary>
/// Every quest of MS2_DATA (KMS2_DEV) and of MS2_GMS2_DATA (GMS2) with a gotoNpc, a gotoDungeon (room or gotoInstanceID) or a
/// dispatch: in the record the client's rule asks for, the button is built and its press sends the request, or the dispatch
/// box is built with the client's text. The expected outcome is the client's rule written out here, both ways: each go-to
/// button is also checked hidden in every record and input the client hides it in (pvpinfo.cpp:238-300, questmanager_1.cpp:245-260,
/// questdata.cpp:222-240), and re-entry is checked over every quest, shown only on the useReEntryHiddenMap ones.
/// </summary>
[NonParallelizable]
public class QuestGotoDataSweepTests
{
    sealed class States : IQuestState
    {
        public QuestRecord Rec;
        public IReadOnlyList<QuestRecord> Records => new[] { Rec };
        public QuestRecord Get(int questId) => Rec?.Id == questId ? Rec : null;
        public bool CanStart(int questId) => false;
        public int PlayerLevel => 1;
        public long CharacterId => 1;
        public bool NpcFieldOpen(int npcId, int fieldId) => true;
    }

    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    [TestCase("MS2_DATA", "KR")]
    [TestCase("MS2_GMS2_DATA", "NA")]
    public void EveryGotoAndDispatchResolves(string env, string locale)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} is not set");
        var packs = TestData.Packs(dir);
        var strings = new GameStrings(packs, "en");
        var quests = QuestTable.Load(packs, strings.Languages, locale, _ => true);
        var tables = new GameTables(packs, "en");
        var meta = FieldMeta.Load(packs);
        var rooms = DungeonRooms.Load(packs, e => FeatureElements.IsAvailable(packs, e));
        var st = new States();
        var sent = new List<IOutPacket>();
        var live = new QuestButtonInputs { FieldExists = _ => true, LocalPlayerAlive = true, HasFieldData = true };
        var g = new QuestGotoRequests { Data = quests.Quest, State = () => st, Inputs = () => live, Meta = () => meta, Rooms = () => rooms, Send = sent.Add };
        var shown = new List<MessageBoxData>();
        var boxes = new QuestDispatchBoxes
        {
            Data = quests.Quest, Script = tables.QuestDispatchScript, MapName = f => tables.MapName(f) ?? "", ReplaceValueWords = t => t,
            PlayerName = () => "P", CommonByKey = k => k, Show = shown.Add,
        };

        var fail = new List<string>();
        var ids = new SortedDictionary<string, List<int>>();
        void Add(string k, int id) { if (!ids.TryGetValue(k, out var l)) ids[k] = l = new List<int>(); l.Add(id); }

        void Reset(int field)
        {
            live.FieldId = field;
            live.ServiceType = live.FieldKind = live.InstanceId = 0;
            live.Flag1439f4a7c = true; live.LocalPlayerAlive = true; live.HasFieldData = true;
        }
        // The button in the quest log's list against its press: both agree, and both match what the client rule gives.
        void Expect(int id, string button, Func<int, bool> press, Type request, bool shown, string why)
        {
            sent.Clear();
            var b = QuestCommon.Buttons(quests.Quest(id), st.Rec, live, meta, st, rooms);
            bool listed = b != null && b.Contains(button);
            bool pressed = press(id);
            bool sentOne = sent.Count == 1 && sent[0].GetType() == request;
            if (listed != shown || pressed != shown || sentOne != shown || (!shown && sent.Count != 0))
                fail.Add($"{id} {button} {why}: client {(shown ? "shows" : "hides")} it, list {listed} press {pressed} sent {sent.Count}");
        }
        QuestRecord R(int id, int state, bool complete) => new(id, state, 0, 0, 0, complete, false);

        foreach (var d in quests.Quests.OrderBy(q => q.Id))
        {
            int id = d.Id;
            if (d.GotoNpcEnable != 0)
            {
                Add("gotoNpc", id);
                // complete; standing in a field that is not the target and holds no complete npc (pvpinfo.cpp:238-261)
                var npcFields = meta.NpcFields.TryGetValue(d.CompleteNpc, out var nf) ? nf : new List<int>();
                int here = 2000062;
                while (here == d.GotoNpcField || npcFields.Contains(here)) here++;
                st.Rec = new QuestRecord(id, 1, 0, 0, 0, true, false);
                live.FieldId = here;
                sent.Clear();
                var b = QuestCommon.Buttons(d, st.Rec, live, meta, st, rooms);
                if (d.GotoNpcField == 0) Add("gotoNpc without a field (client hides it)", id);
                else if (b == null || !b.Contains("gotoNpc") || !g.GotoNpc(id) || sent.Count != 1 || sent[0] is not QuestGotoNpcRequest { QuestId: var q } || q != id)
                    fail.Add($"{id} gotoNpc to {d.GotoNpcField}");
                else if (tables.MapName(d.GotoNpcField) == null) fail.Add($"{id} gotoNpc field {d.GotoNpcField} has no map name");
                // hidden: not complete (:247); standing in the target (:257) or in a field of the complete npc (:253-255)
                Reset(here);
                st.Rec = R(id, 1, false);
                Expect(id, "gotoNpc", g.GotoNpc, typeof(QuestGotoNpcRequest), false, "incomplete");
                st.Rec = R(id, 2, false);
                Expect(id, "gotoNpc", g.GotoNpc, typeof(QuestGotoNpcRequest), false, "not started or done, incomplete");
                st.Rec = R(id, 1, true);
                if (d.GotoNpcField != 0)
                {
                    Reset(d.GotoNpcField);
                    Expect(id, "gotoNpc", g.GotoNpc, typeof(QuestGotoNpcRequest), false, "in the target field");
                }
                if (npcFields.Count > 0)
                {
                    Add("gotoNpc with a complete npc field (hidden there)", id);
                    Reset(npcFields[0]);
                    Expect(id, "gotoNpc", g.GotoNpc, typeof(QuestGotoNpcRequest), false, $"in the complete npc's field {npcFields[0]}");
                }
                Reset(here);
                live.FieldExists = f => f != here;
                Expect(id, "gotoNpc", g.GotoNpc, typeof(QuestGotoNpcRequest), false, "in a field without field data (:240)");
                live.FieldExists = _ => true;
            }
            else if (d.GotoNpcField != 0)
            {
                // gotoNpc off (m_300.m_0 == 0, :245) with a target: hidden even when complete and away
                Add("gotoNpc field without the enable (hidden)", id);
                Reset(2000062);
                st.Rec = R(id, 1, true);
                Expect(id, "gotoNpc", g.GotoNpc, typeof(QuestGotoNpcRequest), false, "not enabled");
            }
            if (d.GotoDungeonRoom != 0 || d.GotoDungeonField != 0)
            {
                Add(d.GotoDungeonRoom != 0 ? "gotoDungeon" : "gotoInstanceID", id);
                int kind = d.GotoDungeonState;
                st.Rec = kind == 2 ? new QuestRecord(id, 1, 0, 0, 0, true, false) : new QuestRecord(id, 1, 0, 0, 0, false, false);
                live.FieldId = 2000062;
                live.ServiceType = live.FieldKind = live.InstanceId = 0;
                sent.Clear();
                var b = QuestCommon.Buttons(d, st.Rec, live, meta, st, rooms);
                if (kind is < 1 or > 3) Add("goto state outside 1-3 (client hides it)", id);
                else if (d.GotoDungeonRoom != 0 && rooms.GetRoom(d.GotoDungeonRoom) == null) Add("gotoDungeon room not in the room table (client hides it, pvpinfo.cpp:277)", id);
                else if (b == null || !b.Contains("gotoDungeon") || !g.GotoDungeon(id) || sent.Count != 1 || sent[0] is not QuestGotoDungeonRequest { QuestId: var q } || q != id)
                    fail.Add($"{id} gotoDungeon room {d.GotoDungeonRoom} instance {d.GotoDungeonField} state {kind}");
                var room = d.GotoDungeonRoom != 0 ? rooms.GetRoom(d.GotoDungeonRoom) : null;
                bool roomOk = d.GotoDungeonRoom == 0 || room != null;
                // every record against the client's state table (:281-294): 1 active and incomplete, 2 complete, 3 active
                foreach (var (state, complete) in new[] { (1, false), (1, true), (2, true), (2, false), (0, false) })
                {
                    bool want = roomOk && kind switch { 1 => state == 1 && !complete, 2 => complete, 3 => state == 1, _ => false };
                    Reset(2000062);
                    if (room != null && Array.IndexOf(room.FieldIds, 2000062) >= 0) break;
                    st.Rec = R(id, state, complete);
                    Expect(id, "gotoDungeon", g.GotoDungeon, typeof(QuestGotoDungeonRequest), want, $"state {kind}, record {state}/{complete}");
                }
                if (kind is >= 1 and <= 3 && roomOk)
                {
                    st.Rec = kind == 2 ? R(id, 1, true) : R(id, 1, false);
                    if (room != null && room.FieldIds.Length > 0)
                    {
                        Reset(room.FieldIds[0]);
                        Expect(id, "gotoDungeon", g.GotoDungeon, typeof(QuestGotoDungeonRequest), false, $"in the room's field {room.FieldIds[0]} (:279)");
                    }
                    // FUN_140930b70 is asked only with a room (questmanager_1.cpp:251)
                    Reset(2000062);
                    live.ServiceType = 2;
                    Expect(id, "gotoDungeon", g.GotoDungeon, typeof(QuestGotoDungeonRequest), room == null, "service type 2");
                    Reset(2000062);
                    live.FieldKind = 1;
                    Expect(id, "gotoDungeon", g.GotoDungeon, typeof(QuestGotoDungeonRequest), room == null, "field kind 1");
                    if (d.GotoDungeonField != 0)
                    {
                        Reset(2000062);
                        live.InstanceId = d.GotoDungeonField;
                        Expect(id, "gotoDungeon", g.GotoDungeon, typeof(QuestGotoDungeonRequest), false, "inside the instance (DAT_143ab2880, :252)");
                    }
                }
            }
            // re-entry over every quest (questdata.cpp:222-240 then FUN_1409312f0): useReEntryHiddenMap, active, a first field
            // that is not here, a channel, service type 0, alive, field data
            int first = d.Fields.Length == 0 ? 0 : (int)d.Fields[0];
            if (d.UseReEntryHiddenMap || first != 0)
            {
                int away = 2000062;
                while (away == first) away++;
                bool can = d.UseReEntryHiddenMap && first != 0;
                if (d.UseReEntryHiddenMap) Add(first != 0 ? "gotoReEntry" : "gotoReEntry without a first field (client hides it)", id);
                st.Rec = R(id, 1, false);
                Reset(away);
                Expect(id, "gotoReEntry", g.GotoReEntry, typeof(QuestGotoReEntryRequest), can, d.UseReEntryHiddenMap ? "active, away" : "no useReEntryHiddenMap");
                if (can)
                {
                    if (sent.Count == 1 && Bytes(sent[0]) is var bytes && !bytes.SequenceEqual(new byte[] { 0x13 }.Concat(BitConverter.GetBytes(id))))
                        fail.Add($"{id} gotoReEntry bytes {Convert.ToHexString(bytes)}");
                    st.Rec = R(id, 1, true);
                    Expect(id, "gotoReEntry", g.GotoReEntry, typeof(QuestGotoReEntryRequest), true, "active and complete");
                    st.Rec = R(id, 2, true);
                    Expect(id, "gotoReEntry", g.GotoReEntry, typeof(QuestGotoReEntryRequest), false, "done");
                    st.Rec = R(id, 0, false);
                    Expect(id, "gotoReEntry", g.GotoReEntry, typeof(QuestGotoReEntryRequest), false, "not started");
                    st.Rec = R(id, 1, false);
                    Reset(first);
                    Expect(id, "gotoReEntry", g.GotoReEntry, typeof(QuestGotoReEntryRequest), false, "in the first field");
                    foreach (var (why, set) in new (string, Action)[]
                             {
                                 ("no channel", () => live.Flag1439f4a7c = false), ("service type 1", () => live.ServiceType = 1),
                                 ("dead", () => live.LocalPlayerAlive = false), ("no field data", () => live.HasFieldData = false),
                             })
                    {
                        Reset(away);
                        set();
                        Expect(id, "gotoReEntry", g.GotoReEntry, typeof(QuestGotoReEntryRequest), false, why);
                    }
                }
                Reset(2000062);
            }
            if (d.DispatchType != 0)
            {
                if (d.DispatchType is 1 or 2) { Add("dispatch direct (server moves, no box)", id); continue; }
                if (!QuestCommon.IsStory(d.Type)) { Add("dispatch on a non-story quest (no box)", id); continue; }
                Add(d.DispatchType == 3 ? "dispatch monologueAccept" : "dispatch monologueComplete", id);
                shown.Clear();
                if (d.DispatchType == 3) boxes.OnStarted(id); else boxes.OnRewarded(id);
                boxes.Tick(true);
                if (shown.Count != 1) { fail.Add($"{id} dispatch {d.DispatchType}: no box"); continue; }
                var box = shown[0];
                string map = tables.MapName(d.DispatchField);
                if (string.IsNullOrEmpty(tables.QuestDispatchScript(d.DispatchScript))) fail.Add($"{id} dispatch script {d.DispatchScript} has no text");
                else if (map == null && box.Msg != tables.QuestDispatchScript(d.DispatchScript).Replace("{0}", ""))
                    fail.Add($"{id} dispatch field {d.DispatchField}: no map name, box '{box.Msg}'");
                else if (map == null) Add("dispatch field without a map name in the data (the box shows an empty name)", id);
                else if (!box.Msg.Contains(map) || box.Args[0] != id.ToString() || box.Args[1] != d.DispatchType.ToString())
                    fail.Add($"{id} dispatch box '{box.Msg}'");
            }
        }

        foreach (var (k, l) in ids) TestContext.Out.WriteLine($"{env} {k}: {l.Count}: {string.Join(' ', l)}");
        foreach (var f in fail) TestContext.Out.WriteLine($"{env} FAIL {f}");
        Assert.That(ids.ContainsKey("gotoDungeon") && ids.ContainsKey("gotoNpc") && ids.ContainsKey("gotoReEntry"), Is.True, "the data has go-to quests");
        Assert.That(fail, Is.Empty);
    }
}
