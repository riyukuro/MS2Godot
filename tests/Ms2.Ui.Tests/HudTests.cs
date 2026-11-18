using Ms2.Net.Packets;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The HUD movies run by their own ActionScript and fed by the Ms2.Ui controllers (needs MS2_DATA).</summary>
public class HudTests
{
    static PackSet packs;
    static KeyTable keys;
    static MovieSource src;
    static GameTables tables;

    static UiModel Model()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
            tables = new GameTables(packs, "en");
            keys = new KeyTable(packs, "en");
        }
        var m = new UiModel(tables) { Keys = keys };
        UiFixture.Apply(m);
        m.ServerTime = () => new DateTime(2026, 1, 1, 9, 5, 0);
        return m;
    }

    static UiHost Load(UiController c, UiModel m)
    {
        var (rt, p) = src.Open(c.Movie, r => { r.Budget = 20_000_000; r.FixedWallClock = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc); });
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, m);
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();
        TestContext.WriteLine($"{c.Movie}: calls [{string.Join(", ", h.Calls)}] unhandled [{string.Join(", ", h.Unhandled)}]");
        foreach (var e in rt.Errors) TestContext.WriteLine("  error: " + e);
        foreach (var (k, n) in rt.Missing) TestContext.WriteLine($"  missing: {k} x{n}");
        return h;
    }

    static string Text(UiHost h, string name, AsDisplayObject from = null) => (h.FindNamed(name, from) as AsTextField)?.Text;

    [Test]
    public void FeatureIdsGoThroughTheFeatureTable()
    {
        // CFeatureManager::IsFeatureEnabled: 0 off, 1 on, other ids by their name (FeatureTable)
        var m = new UiModel(GameTables.Empty) { FeatureEnabledByName = n => n == "SuperWorldChat" };
        Assert.That(m.IsFeatureEnabled(0), Is.False);
        Assert.That(m.IsFeatureEnabled(1), Is.True);
        Assert.That(m.IsFeatureEnabled(0x19), Is.True);
        Assert.That(m.IsFeatureEnabled(0x18), Is.False);
        Assert.That(m.IsFeatureEnabled(0x118), Is.False);
        // without the table, the ids the host listed
        Assert.That(new UiModel(GameTables.Empty).IsFeatureEnabled(Feature.GamePad), Is.True);
    }

    [Test]
    public void TablesReadNextExpAndJobNames()
    {
        var m = Model();
        Assert.That(m.Tables.NextExp(1), Is.GreaterThan(0));
        Assert.That(m.Tables.NextExp(21), Is.GreaterThan(m.Tables.NextExp(1)));
        Assert.That(m.Tables.JobName(100), Is.EqualTo("Knight"));
        Assert.That(m.Tables.Common("s_tooltip_rest_exp"), Does.Contain("{1:.2f}"));
    }

    [Test]
    public void ExpBarShowsPercentAndAnswersGetExp()
    {
        var m = Model();
        var c = new ExpBarController();
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(Text(h, "textField", h.Child("m_point") as AsDisplayObject), Is.EqualTo("38.00%"));
        // vfunc_43: the component's size is the stage width (its m_bg reflows to it) and alignment centres it
        var bg = h.Child("m_bg");
        Assert.That(Rt.ToNumber(h.Get(bg, "width")), Is.EqualTo(1920).Within(1));
        double x = Rt.ToNumber(h.Get(h.Entity, "x")), w = Rt.ToNumber(h.Get(h.Entity, "width"));
        Assert.That(x + w / 2, Is.EqualTo(960).Within(1));
        Assert.That(Rt.ToNumber(h.Get(h.Entity, "y")), Is.EqualTo(1080 - 21).Within(1));
        var ud = c.GetUserData("getExp", Array.Empty<object>());
        Assert.That((string)ud["text"], Does.StartWith($"{m.Player.Exp:N0} / {m.NextLevelExp:N0}".Replace(' ', ',')));
        Assert.That((string)ud["text"], Does.Contain("Level 21"));
        // status event 3 redraws
        m.Player.Exp = m.NextLevelExp / 2; m.Notify(StatusEvent.Exp); h.Player.Tick();
        Assert.That(Text(h, "textField", h.Child("m_point") as AsDisplayObject), Is.EqualTo("50.00%"));
    }

    [Test]
    public void AdventureLevelExpBarPointAndLayout()
    {
        var m = Model();
        m.Features.Add(AdventureLevelExpBarController.Feature);
        m.Player.Level = 60;
        m.Player.AdventureExp = m.Tables.AdventureLevelLvUpExp / 4;
        var c = new AdventureLevelExpBarController();
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        // setPoint(25, ...): the movie's own label text
        Assert.That(Text(h, "textField", h.Child("m_point") as AsDisplayObject), Is.EqualTo("25.00 %"));
        // vfunc_43: stage width, alignment puts it right over the exp bar (setExpBarHeight 19)
        double y = Rt.ToNumber(h.Get(h.Entity, "y"));
        Assert.That(y, Is.EqualTo(1080 - 19 - 21).Within(1));
        var ud = c.GetUserData("getExp", Array.Empty<object>());
        Assert.That((string)ud["text"], Does.Contain($"{m.Player.AdventureExp:N0} / {m.Tables.AdventureLevelLvUpExp:N0}"));
    }

    [Test]
    public void MainPlayerInfoTakesTheServerKeyTable()
    {
        var m = Model();
        // the player's saved bindings: slot 0's action on key 30 (A) and slot 4's on key 16 (Q); page 2 of 3 is current
        var bindings = new[] { new KeyBinding(30, 1, 500001, 0, 0), new KeyBinding(16, 1, 500005, 0, 0) };
        var pages = new IReadOnlyList<(int, QuickSlotEntry)>[] { Array.Empty<(int, QuickSlotEntry)>(), new[] { (3, new QuickSlotEntry(10000001, 0, 0)) }, Array.Empty<(int, QuickSlotEntry)>() };
        var t = new ServerKeyTable();
        t.Apply(new Maple2.PacketLib.Tools.ByteReader(ServerKeyTable.Write(0, false, bindings, 1, pages)));
        m.Keys = new KeyTable(packs, "en"); m.ServerKeys = t; m.Keys.Apply(t);
        var c = new MainPlayerInfoController();
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(c.ShortkeyPage, Is.EqualTo(2));
        var slot0 = (AsObject)h.ToAs(c.GetBinding("SlotInfo.skill.0.0"));
        Assert.That(Rt.ToString(h.Get(slot0, "shortcutKey")), Is.EqualTo("A"));
        Assert.That(Rt.ToString(h.Get((AsObject)h.ToAs(c.GetBinding("SlotInfo.skill.0.4")), "shortcutKey")), Is.EqualTo("Q"));
        Assert.That(Rt.ToString(h.Get((AsObject)h.ToAs(c.GetBinding("SlotInfo.skill.0.1")), "shortcutKey")), Is.EqualTo(""));   // cleared, not in the packet
        // the filled slot 3 of the current page: skill 10000001's icon (not learnt: no skill book, so gray)
        var slot3 = (AsObject)h.ToAs(c.GetBinding("SlotInfo.skill.0.3"));
        TestContext.WriteLine(Rt.ToString(h.Get(slot3, "iconPath")));
        Assert.That(Rt.ToString(h.Get(slot3, "iconPath")), Is.EqualTo(m.Tables.Skills.GetSkillData(10000001)?.Icon ?? ""));
        Assert.That(Rt.ToNumber(h.Get(slot3, "grayColor")), Is.EqualTo(1));
    }

    [Test]
    public void QuickSlotTooltipsFollowTheSlotKind()
    {
        // SkillSlot::vfunc_0 (skillslot.cpp:716): a skill slot's skill tooltip with the remove hint, a key-bound slot's action
        // label, nothing for an empty slot
        var m = Model();
        m.HasLocalPlayer = true;   // vfunc_0 shows a skill or item tooltip only with a local PC
        var pages = new IReadOnlyList<(int, QuickSlotEntry)>[] { new[] { (3, new QuickSlotEntry(10500001, 0, 0)) } };   // Arrow Stream
        var t = new ServerKeyTable();
        t.Apply(new Maple2.PacketLib.Tools.ByteReader(ServerKeyTable.Write(0, false, Array.Empty<KeyBinding>(), 0, pages)));
        m.Keys = new KeyTable(packs, "en"); m.ServerKeys = t; m.Keys.Apply(t);
        var c = new MainPlayerInfoController();
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        string hint = m.Tables.Common("s_msg_rclick_putoff_qslot");
        Assert.That(hint, Is.Not.Empty);
        var skill = c.GetSlotTooltip(MainPlayerInfoController.SlotBag + ".3");
        Assert.That(skill, Is.Not.Null.And.Not.Empty);
        Assert.That(skill["enablePuton"], Is.EqualTo(hint));
        var key = c.GetSlotTooltip(MainPlayerInfoController.SlotBag + ".22");
        Assert.That(key["text"], Is.EqualTo(m.Tables.Common("s_quick_slot_item_get")));
        Assert.That(key["tooltipAlign"], Is.EqualTo(5));
        Assert.That(c.GetSlotTooltip(MainPlayerInfoController.SlotBag + ".1"), Is.Empty);
        Assert.That(c.GetSlotTooltip("SlotInfo.other.1"), Is.Null);
    }

    [Test]
    public void PartyWindowShowsTheFixtureParty()
    {
        // UIPartyDialog: RefreshAll's initMembers with each member's FillMemberObject, then the party packets' window calls
        var m = Model();
        var book = PartyFixture.Book(2000062, m.Player);
        var c = new PartyController { Party = book, SelfCharacterId = PartyFixture.SelfCharacterId, MapName = id => id == 2000001 ? "Tria" : "Field" };
        book.MemberAdded += c.AddMember;
        book.MemberRemoved += c.RemoveMember;
        book.MemberUpdated += c.UpdateMember;
        book.ChiefChanged += c.ChangeChief;
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        for (int i = 0; i < 120; i++) h.Player.Tick();      // the movie adds the members one by one on a 100 ms timer
        AsObject Member(long id) => h.Invoke(h.Entity, "getMemberAni", id.ToString()) as AsObject;
        foreach (long id in new long[] { 2, 3, 4, 5 }) Assert.That(Member(id), Is.Not.Null, $"member {id}");
        AsObject Data(long id) => (AsObject)h.Get(Member(id), "object");
        Assert.That(Rt.ToBoolean(h.Get(Data(2), "chief")), Is.True);
        // the own member leads; the leader passes to member 3 (OnPartyLeaderChange -> changeChief)
        book.Apply(new PartyLeaderChange(3), PartyFixture.SelfCharacterId, true);
        Assert.That(Rt.ToBoolean(h.Get(Data(2), "chief")), Is.False);
        Assert.That(Rt.ToBoolean(h.Get(Data(3), "chief")), Is.True);
        // the channel members: member 4 is in another map and channel ("diff"), member 3 is with the own member
        var elsewhere = c.MemberObject(book.Find(4), 0x80);
        Assert.That(elsewhere["ch"], Is.EqualTo("diff"));
        Assert.That(elsewhere["chName"], Is.EqualTo(m.Tables.Common("s_party_ch").Replace("{0}", "1")));
        Assert.That(c.MemberObject(book.Find(3), 0x80)["ch"], Is.EqualTo(""));
        // member 4 leaves (removeMember): the movie takes it out of its map
        book.Apply(new PartyLeave(4, false), PartyFixture.SelfCharacterId, true);
        Assert.That(Member(4), Is.Null);
        Assert.That(h.RT.Errors, Is.Empty);
        // the tooltip: online with channel and map, offline
        string Tip(long id) => (string)c.GetUserData("getTooltip", new object[] { id.ToString() })?["text"];
        Assert.That(Tip(3), Is.EqualTo(m.Tables.Common("s_tooltip_party").Replace("{0}", m.Tables.Common("s_word_online"))
            .Replace("{1}", m.Tables.Common("s_tooltip_party_ch").Replace("{0}", m.Player.Channel.ToString()) + " Field")));
        Assert.That(Tip(5), Is.EqualTo(m.Tables.Common("s_word_offline")));
        // the movie's right click raises the member menu (raise-only in phase 1)
        long menuFor = 0;
        c.MemberMenu += (x, y, id) => menuFor = id;
        Assert.That(c.OnCallback("onPopupMenu", new object[] { 10.0, 20.0, "3" }), Is.True);
        Assert.That(menuFor, Is.EqualTo(3));
    }

    [Test]
    public void PartySlotMenuListsTheClientItems()
    {
        // UIPopUpMenu for the party slots (FUN_141a83810 with 0x63fe): the own slot, an online member, an offline member,
        // as the leader and after the lead passes; the movie's "open" takes the list and MenuItemClick raises the item
        var m = Model();
        var book = PartyFixture.Book(2000062, m.Player);
        var c = new PopupMenuController { Party = book, SelfCharacterId = PartyFixture.SelfCharacterId, ProfileImagePath = (_, _) => "" };
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        bool voteKick = m.IsFeatureEnabled(0xe0);
        int[] Menu(long id)
        {
            c.OpenForPartySlot(100, 200, id, m.Player);
            var items = c.Items.ToArray();
            c.ClosePopup();
            return items;
        }
        int[] With(bool on, params int[] ids) => on ? ids : Array.Empty<int>();
        const int self = (int)PartyFixture.SelfCharacterId;
        // the own slot: view character, leave, ready check (the leader)
        Assert.That(Menu(self), Is.EqualTo(new[] { PopupMenuController.ViewOwnCharacter, PopupMenuController.LeaveParty, PopupMenuController.ReadyCheck }));
        // an online member as the leader: view, whisper, group chat, house, home, friend, kick, leader, (vote), block, report
        Assert.That(Menu(3), Is.EqualTo(new[] { 0x66, 0x65, 0x1f6, 0x1f7, 0x1f8, 0x12d, 0xca, 0xcb }.Concat(With(voteKick, 0xcc)).Concat(new[] { 400, 0x1f5 })));
        // an offline member: no whisper, group chat, leader change or report
        Assert.That(Menu(5), Is.EqualTo(new[] { 0x66, 0x1f7, 0x1f8, 0x12d, 0xca }.Concat(With(voteKick, 0xcc)).Concat(new[] { 400 })));
        // not the leader: the own slot only leaves, a member only has the vote kick of the party items
        book.Apply(new PartyLeaderChange(3), PartyFixture.SelfCharacterId, true);
        Assert.That(Menu(self), Is.EqualTo(new[] { PopupMenuController.ViewOwnCharacter, PopupMenuController.LeaveParty }));
        Assert.That(Menu(4), Is.EqualTo(new[] { 0x66, 0x65, 0x1f6, 0x1f7, 0x1f8, 0x12d }.Concat(With(voteKick, 0xcc)).Concat(new[] { 400, 0x1f5 })));
        // the movie's list: open, labelled from string table 7, and a click raises the item for the target
        c.OpenForPartySlot(100, 200, 3, m.Player);
        Assert.That(c.IsClosed, Is.False);
        Assert.That(c.InfoString, Is.EqualTo("partySlot"));
        Assert.That(m.Tables.PopupMenuName(0x66), Is.Not.Empty);
        var menu = h.Child("m_menu") as AsObject;
        Assert.That(Rt.ToBoolean(h.Get(menu, "visible")), Is.True);
        (int id, string name) clicked = default;
        c.ItemClicked += (id, t) => clicked = (id, t.Name);
        c.OnCallback("MenuItemClick", new object[] { (uint)0x66 });
        Assert.That(clicked, Is.EqualTo((0x66, "FixtureArcher")));
        // a disband closes it (FUN_141a80a50 mode 7)
        c.OnPartyMessage(new PartyDisband());
        Assert.That(c.IsClosed, Is.True);
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void PartySlotMenuItemsSendTheClientRequests()
    {
        // UIPopUpMenu's party handlers (RegisterMenuHandlers): leave / ready check send at once, kick and leader change ask
        // first (OnMessage on yes), the vote kick asks with its own callback; the own slot's view opens the own info
        var m = Model();
        var book = PartyFixture.Book(2000062, m.Player);
        var c = new PopupMenuController { Party = book, SelfCharacterId = PartyFixture.SelfCharacterId, ProfileImagePath = (_, _) => "" };
        var sent = new List<object>();
        var boxes = new List<MessageBoxData>();
        int nexts = 0, myInfo = 0;
        c.Send = sent.Add;
        c.ShowBox = boxes.Add;
        c.NextBox = () => nexts++;
        c.OpenMyInfo += () => myInfo++;
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        c.OpenForPartySlot(100, 200, PartyFixture.SelfCharacterId, m.Player);
        c.ItemClick(PopupMenuController.ViewOwnCharacter);
        c.ItemClick(PopupMenuController.LeaveParty);
        c.ItemClick(PopupMenuController.ReadyCheck);
        Assert.That(myInfo, Is.EqualTo(1));
        Assert.That(sent, Is.EqualTo(new object[] { new PartyLeaveRequest(), new PartyReadyCheckRequest() }));
        c.ClosePopup();
        sent.Clear();

        c.OpenForPartySlot(100, 200, 3, m.Player);
        c.ItemClick(PopupMenuController.ViewOwnCharacter);              // not the own character
        Assert.That(myInfo, Is.EqualTo(1));
        c.ItemClick(PopupMenuController.KickFromParty);
        Assert.That(boxes, Has.Count.EqualTo(1));
        Assert.That(boxes[0].Type, Is.EqualTo(1));
        Assert.That(boxes[0].Extra, Is.EqualTo("expelMember:FixtureArcher"));
        Assert.That(boxes[0].Listener, Is.SameAs(c));
        Assert.That(boxes[0].Msg, Is.EqualTo(Ms2.Quest.ClientFormat.Format(m.Tables.Common("s_party_check_expel_member"), "FixtureArcher")));
        c.OnMessageBox(null, 1, boxes[0].Extra);                        // no: nothing sent, the box goes on
        Assert.That(sent, Is.Empty);
        Assert.That(nexts, Is.EqualTo(1));
        c.OnMessageBox(null, 0, boxes[0].Extra);
        Assert.That(sent, Is.EqualTo(new object[] { new PartyKickRequest(3) }));
        c.OnMessageBox(null, 0, "expelMember:NoSuchMember");            // no member by that name: nothing
        Assert.That(sent, Has.Count.EqualTo(1));
        c.ItemClick(PopupMenuController.MakePartyLeader);
        Assert.That(boxes[1].Extra, Is.EqualTo("changeChief:FixtureArcher"));
        c.OnMessageBox(null, 0, boxes[1].Extra);
        Assert.That(sent[^1], Is.EqualTo(new PartyLeaderChangeRequest("FixtureArcher")));
        c.ItemClick(PopupMenuController.VoteToKick);
        Assert.That(boxes[2].HasCallback, Is.True);
        Assert.That(boxes[2].Callback(1, boxes[2]), Is.True);
        Assert.That(sent, Has.Count.EqualTo(2));
        Assert.That(boxes[2].Callback(0, boxes[2]), Is.True);
        Assert.That(sent[^1], Is.EqualTo(new PartyVoteKickRequest(3)));
        // an unported item still raises
        int raised = 0;
        c.ItemClicked += (_, _) => raised++;
        c.ItemClick(PopupMenuController.Whisper);
        Assert.That(raised, Is.EqualTo(1));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void PartyWindowButtonsSendTheClientRequests()
    {
        // UIPartyDialog: enter dungeon sends at once, give up asks, create club asks for a name and checks it (CreateClub)
        var m = Model();
        var book = PartyFixture.Book(2000062, m.Player);
        var c = new PartyController { Party = book, SelfCharacterId = PartyFixture.SelfCharacterId, MapName = _ => "Field" };
        var sent = new List<object>();
        var boxes = new List<MessageBoxData>();
        var notices = new List<string>();
        c.Send = sent.Add;
        c.ShowBox = boxes.Add;
        c.SystemNotice += notices.Add;
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        c.OnEnterDungeonClick();
        Assert.That(sent, Is.EqualTo(new object[] { new RoomDungeonEnterRequest(0) }));
        c.OnGiveUpDungeonClick();
        Assert.That(boxes[0].Type, Is.EqualTo(1));
        Assert.That(boxes[0].Msg, Is.EqualTo(m.Tables.Common("s_party_dungeon_giveup")));
        boxes[0].Callback(1, boxes[0]);
        Assert.That(sent, Has.Count.EqualTo(1));
        boxes[0].Callback(0, boxes[0]);
        Assert.That(sent[^1], Is.EqualTo(new DungeonGiveUpRequest()));
        sent.Clear();

        c.OnCreateClubClick();
        var box = boxes[1];
        Assert.That(box.Type, Is.EqualTo(10));
        Assert.That(box.InputMaxChars, Is.EqualTo(m.Tables.ClubNameLengthMax));
        box.ClickedTag = "ab cd";
        Assert.That(box.Callback(0, box), Is.True);
        Assert.That(notices, Is.EqualTo(new[] { m.Tables.Common("s_club_err_clubname_has_blank") }));
        box.ClickedTag = "a";                                           // too short: silent
        box.Callback(0, box);
        Assert.That(notices, Has.Count.EqualTo(1));
        box.ClickedTag = "";                                            // empty: the box stays
        Assert.That(box.Callback(0, box), Is.False);
        Assert.That(sent, Is.Empty);
        box.ClickedTag = "Club1,";
        Assert.That(box.Callback(0, box), Is.True);
        Assert.That(sent, Is.EqualTo(new object[] { new ClubCreateRequest("Club1") }));
        // not the leader: no box, and a create is refused
        book.Apply(new PartyLeaderChange(3), PartyFixture.SelfCharacterId, true);
        c.OnCreateClubClick();
        Assert.That(boxes, Has.Count.EqualTo(2));
        Assert.That(c.CreateClub("Club2"), Is.False);
        Assert.That(notices[^1], Is.EqualTo(m.Tables.Common("s_club_err_create_no_party")));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void MainPlayerInfoInitAndStatusEvents()
    {
        var m = Model();
        var h = Load(new MainPlayerInfoController(), m);
        Assert.That(h.RT.Errors, Is.Empty);
        // vfunc_43: bottom centre above the exp bar
        double x = Rt.ToNumber(h.Get(h.Entity, "x")), w = Rt.ToNumber(h.Get(h.Entity, "width"));
        double y = Rt.ToNumber(h.Get(h.Entity, "y")), ht = Rt.ToNumber(h.Get(h.Entity, "height"));
        Assert.That(x + w / 2, Is.EqualTo(960).Within(1));
        Assert.That(y + ht, Is.EqualTo(1080 - 19).Within(1));
        var pi = h.Child("m_playerInfo") as AsDisplayObject;
        Assert.That(pi, Is.Not.Null);
        var texts = AllTexts(pi);
        TestContext.WriteLine("texts: " + string.Join(" | ", texts));
        // the quick slots bind "SlotInfo.skill.0.N" and show the key bound to each slot's action (DefaultKey.xml)
        Assert.That(h.BindingNames, Does.Contain("SlotInfo.skill.0.0").And.Contain("SlotInfo.skill.0.24"));
        Assert.That(h.RT.Errors, Is.Empty);
        var slotTexts = AllTexts(h.Child("m_slot") as AsDisplayObject);
        Assert.That(slotTexts.Where(t => t.StartsWith("textField=")).Take(48).Where((_, n) => n % 2 == 0).Select(t => t[10..]),
            Is.EqualTo(new[] { "Q", "W", "E", "R", "A", "S", "D", "F", "1", "2", "3", "4", "5", "6", "7", "8", "Insert", "Home", "Page Up", "Delete", "End", "Page Dn", "Z", "X" }));
        Assert.That(texts, Has.Some.Contains("1630"));
        Assert.That(texts, Has.Some.Contains("2100"));
        m.Player.HpCur = 1000; m.Notify(StatusEvent.Hp);
        for (int i = 0; i < 60; i++) h.Player.Tick();
        Assert.That(AllTexts(pi), Has.Some.Contains("1000"));
    }

    [Test]
    public void UpperHudMoneyAndClock()
    {
        var m = Model();
        var h = Load(new UpperHudController(), m);
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(Text(h, "mesoText"), Is.EqualTo("1,284,530"));
        Assert.That(Text(h, "maratText"), Is.EqualTo("350"));
        Assert.That(Text(h, "timeText"), Is.EqualTo("09:05"));
        m.Player.Mesos = 7; m.Notify(StatusEvent.Meso);
        Assert.That(Text(h, "mesoText"), Is.EqualTo("7"));
    }

    [Test]
    public void UpperHudMeratFeatureAndTick()
    {
        var m = Model();
        var now = new DateTime(2026, 1, 1, 9, 5, 0);
        m.ServerTime = () => now;
        m.Features.Remove(Feature.Merat);
        var c = new UpperHudController();
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        // vfunc_46: no merats without feature 0x3d
        Assert.That((h.FindNamed("m_marat") as AsDisplayObject)?.visible, Is.False);
        // vfunc_15: the clock follows the server time once a second
        now = now.AddMinutes(2);
        c.Process(500);
        Assert.That(Text(h, "timeText"), Is.EqualTo("09:05"));
        c.Process(1500);
        Assert.That(Text(h, "timeText"), Is.EqualTo("09:07"));
    }

    [Test]
    public void UpperHpBarShowsTheTarget()
    {
        var m = Model();
        var mob = new UpperHpBarController.Actor { ObjectId = 7, IsNpc = true, NpcClass = 1, Hp = 600, MaxHp = 1000, BaseMaxHp = 1000, Name = "Mob", TypeName = "Plant", Level = 3 };
        var c = new UpperHpBarController { LocalTargetId = 1, FindActor = id => id == 7 ? mob : null };
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        // a hit by someone else does not bring a new target up
        c.Hit(mob, 2);
        Assert.That(c.Shown, Is.EqualTo(0));
        c.Hit(mob, 1);
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(c.Shown, Is.EqualTo(7));
        var texts = AllTexts(h.Entity as AsDisplayObject);
        TestContext.WriteLine(string.Join(" | ", texts));
        Assert.That(texts, Has.Some.Contains("Mob"));
        // the per-frame update follows the HP
        mob.Hp = 250;
        c.Process();
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(AllTexts(h.Entity as AsDisplayObject), Has.Some.Contains("250"));
        c.Remove(mob);
        Assert.That(c.Shown, Is.EqualTo(0));
    }

    [Test]
    public void MinimapInitDataAndEntities()
    {
        var m = Model();
        m.Field.Name = "Tria";
        var c = new MinimapController { ImageName = "map/minimap/02000001.dds", LocalPlayer = () => (default, 90f, false) };
        c.Marks.Add(new() { Entity = new Ms2.Formats.World.MinimapIcons.Entity("portal", "", "portal", "Exit", true, false), Position = () => default, ShowGuide = true });
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(c.FieldClip, Is.Not.Null);
        // the movie's onFieldLoadComplete: BuildEntities through createEntity
        Assert.That(c.OnCallback("onFieldLoadComplete", new object[] { c.FieldClip }), Is.True);
        Assert.That(c.Marks[0].Clip, Is.Not.Null);
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void UpperHudTokenAndMeratTooltips()
    {
        var m = Model();
        m.Features.Add(Feature.PvPRankingDuelMode);
        m.Features.Add(Feature.RedMerat);
        m.Player.Tokens[4] = 1234; m.Player.Tokens[6] = 5;
        m.Player.MeratWallet = new long[] { 100, 20, 0, 7 };
        m.Player.RedMerat = 3;
        var c = new UpperHudController();
        Load(c, m);
        var t = c.GetUserData("getTooltip", new object[] { "m_tokenPocketButton" });
        Assert.That(t["text"], Is.EqualTo(m.Tables.Common("s_upper_hud_token_pocket")
            + "\n<img src='export_common_icon_honor' vspace='-6'/> 1,234 / " + N(m.Tables.TokenCaps[4])
            + "\n<img src='export_common_icon_lu' vspace='-6'/> 5 / " + N(m.Tables.TokenCaps[6])));
        static string N(long n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        Assert.That((t["onDelay"], t["tooltipAlign"], t["offsetX"], t["offsetY"]), Is.EqualTo((0, 7, 5, 0x16)));
        TestContext.WriteLine("token pocket: " + string.Join(" | ", TooltipTests.Draw(t)));
        var mt = c.GetUserData("getTooltip", new object[] { "m_marat" });
        var shown = TooltipTests.Draw(mt);
        TestContext.WriteLine("merat: " + mt["text"] + " => " + string.Join(" | ", shown));
        Assert.That((string)mt["text"], Does.Contain("120").And.Contain("7").And.Not.Contain("{"));
        Assert.That(c.GetUserData("getTooltip", new object[] { "m_effectHotTime" }), Is.Null, "no hot time event, no tooltip");
        c.HotTimeTexts.Add("Hot time A"); c.HotTimeTexts.Add("Hot time B");
        Assert.That(c.GetUserData("getTooltip", new object[] { "m_effectHotTime" })["text"], Is.EqualTo("Hot time A\nHot time B"));
    }

    [Test]
    public void MinimapFieldNameTooltip()
    {
        var m = Model();
        m.Field.Name = "Tria";
        var c = new MinimapController { ImageName = "map/minimap/02000001.dds", LocalPlayer = () => (default, 90f, false) };
        Load(c, m);
        Assert.That(c.GetUserData("getTooltip", new object[] { "m_fieldname" }), Is.Null, "no field data, no tooltip");
        m.Field.MapLevel = 10;
        var d = c.GetUserData("getTooltip", new object[] { "m_fieldname" });
        Assert.That(d["text"], Is.EqualTo(Ms2.Quest.ClientFormat.Format(m.Tables.Common("s_minimap_tooltip_field_info"), "Tria", 10u)));
        Assert.That(d["tooltipAlign"], Is.EqualTo(7));
        var shown = TooltipTests.Draw(d);
        TestContext.WriteLine("m_fieldname: " + string.Join(" | ", shown));
        Assert.That(shown, Has.Some.Contains("Tria"));
        // Tria is fishing spot 02000001 (water, minMastery 0): s_worldmap_tooltip_fishingspot_low with the habitat and
        // Grade(1, 0) = 1 -> s_fishing_grade_Lv1; the restored client shows "Freshwater / Fishing - Beginner I"
        m.Field.MapId = 2000001;
        var f = c.GetUserData("getTooltip", new object[] { "m_fieldname" });
        var line = Ms2.Quest.ClientFormat.Format(m.Tables.Common("s_worldmap_tooltip_fishingspot_low"),
            m.Tables.Common("s_fishing_habitat_water"), m.Tables.Common("s_fishing_grade_Lv1"));
        Assert.That(f["text"], Is.EqualTo((string)d["text"] + "\n" + line));
        var fishing = TooltipTests.Draw(f);
        TestContext.WriteLine("m_fieldname with spot: " + string.Join(" | ", fishing));
        Assert.That(string.Join(" ", fishing), Does.Contain("Freshwater").And.Contain("Beginner I"));
        Assert.That(m.Tables.Fishing.Grade(0), Is.EqualTo(1));
        Assert.That(m.Tables.Fishing.Grade(80), Is.EqualTo(2));
    }

    [Test]
    public void QuestGuideBuildsTrackedQuests()
    {
        var m = Model();
        // synthetic quests: 1 and 2 in progress and tracked, 3 in progress and not tracked
        var t = new Ms2.Quest.QuestTable("KR", _ => true) { Common = k => k == "s_quest_name_with_level" ? "{0} (Lv. {1})" : "" };
        t.AddQuests(System.Xml.Linq.XDocument.Parse(@"<ms2>
            <quest id=""1""><basic questType=""0"" standardLevel=""3"" /><condition type=""npc"" code=""7"" value=""1"" /></quest>
            <quest id=""2""><basic questType=""1"" standardLevel=""4"" /><condition type=""npc"" code=""7"" value=""2"" /></quest>
            <quest id=""3""><basic questType=""1"" standardLevel=""5"" /><condition type=""npc"" code=""7"" value=""1"" /></quest></ms2>"));
        t.BuildIndexes();
        t.AddDescriptions(System.Xml.Linq.XDocument.Parse(@"<ms2>
            <quest questID=""1"" name=""First"" /><quest questID=""2"" name=""Second"" /><quest questID=""3"" name=""Third"" /></ms2>"));
        var state = new Ms2.Quest.QuestShotState(t, 10, new[] { 1, 2 }, tracking: 1);
        var c = new QuestGuideController { Table = t, State = state };
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        var container = h.Get(h.Entity, "m_dragContainer") as AsContainer;
        Assert.That(container, Is.Not.Null);
        Assert.That(container.Children.Count, Is.EqualTo(2), "one entity per tracked quest in progress");
        // shown at gotoAndStop(11): the "show" slide's last move (an identity matrix) puts the body back at x 0
        foreach (AsContainer e in container.Children) Assert.That(e.Children[0].x, Is.EqualTo(0));
        var texts = AllTexts(container);
        Assert.That(texts, Has.Some.Contains("First (Lv. 3)"));
        Assert.That(texts, Has.Some.Contains("Second (Lv. 4)"));
        Assert.That(texts, Has.None.Contains("Third"));
    }

    [Test]
    public void MessengerTabsTargetsAndKeys()
    {
        var m = Model();
        Assert.That(m.Tables.DefaultMessengers, Is.Not.Empty);
        var c = new MessengerController(m.Tables.DefaultMessengers[0], "messengerdialog0.xml", true);
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        // defaultmessenger0.xml: two tabs, 408 x 270, bottom left at offset (0, -38)
        Assert.That(c.Tabs.Select(t => t.Name), Is.EqualTo(new[] { "$messenger_tab_name_entire", "$messenger_tab_name_item" }));
        var bar = (AsObject)h.Child("m_buttonBar");
        Assert.That(Rt.ToNumber(h.Get(h.Get(bar, "dataProvider"), "length")), Is.EqualTo(2));
        Assert.That(Rt.ToNumber(h.Get(bar, "selectedIndex")), Is.EqualTo(0));
        double x = Rt.ToNumber(h.Get(h.Entity, "x")), y = Rt.ToNumber(h.Get(h.Entity, "y")), ht = Rt.ToNumber(h.Get(h.Entity, "height"));
        Assert.That(x, Is.EqualTo(0));
        Assert.That(y, Is.EqualTo(1080 - (int)ht - 38));
        // the target list without feature 0x19: world, channel, social, party, guild, guild megaphone; social selected
        var subject = (AsObject)h.Child("m_subject");
        Assert.That(Rt.ToNumber(h.Get(h.Get(subject, "dataProvider"), "length")), Is.EqualTo(6));
        Assert.That(Rt.ToNumber(h.Get(subject, "selectedIndex")), Is.EqualTo(2));
        Assert.That(Rt.ToNumber(h.Get(subject, "menuRowCount")), Is.EqualTo(6));
        // Return focuses the input; Return again with text releases it and clears it (the line is not sent here)
        var input = (AsObject)h.Child("m_textInput");
        Assert.That(c.OnKey(MessengerController.DikReturn, false), Is.True);
        Assert.That(Rt.ToNumber(h.Get(input, "focused")), Is.EqualTo(1));
        h.Set(input, "text", "HelloWorld");
        Assert.That(c.OnKey(MessengerController.DikReturn, false), Is.True);
        Assert.That(Rt.ToNumber(h.Get(input, "focused")), Is.EqualTo(0));
        Assert.That(Rt.ToString(h.Get(input, "text")), Is.EqualTo(""));
        // Escape is used only while the input is focused
        Assert.That(c.OnKey(0, true), Is.False);
        c.OnKey(MessengerController.DikReturn, false);
        Assert.That(c.OnKey(0, true), Is.True);
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void MessengerSuperChatRowWithTheLiveFeatures()
    {
        var m = Model();
        m.FeatureEnabledByName = f => LobbyFeatures.IsEnabled(packs, f);      // KR Live: SuperWorldChat (0x19) and ChatEmoticon on
        var c = new MessengerController(m.Tables.DefaultMessengers[0], "messengerdialog0.xml", true);
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        // super, world, channel, social, party, guild, guild megaphone; social still selected (the first of m_subjects)
        var subject = (AsObject)h.Child("m_subject");
        var dp = h.Get(subject, "dataProvider");
        Assert.That(Rt.ToNumber(h.Get(dp, "length")), Is.EqualTo(7));
        Assert.That(Rt.ToString(h.Get(h.Get(dp, "0"), "frame")), Is.EqualTo("super"));
        Assert.That(Rt.ToString(h.Get(h.Get(dp, "0"), "iconPath")), Is.EqualTo("export_common_icon_megaphone6"));
        Assert.That(Rt.ToNumber(h.Get(subject, "selectedIndex")), Is.EqualTo(3));
        Assert.That(Rt.ToBoolean(h.Get(h.Child("m_chatEmoticonOpen"), "visible")), Is.True);
    }

    [Test]
    public void GroupMessengerBrowserInsertButtonOnly()
    {
        var m = Model();
        var c = new GroupMessengerBrowserController();
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        // m_container, added last by the constructor, holds only the insert button. The slot is private to the class, and
        // GFx::Value::GetMember looks a name up in the public namespace (AS3ValueObjectInterface::GetMember,
        // sfr_as3_18.cpp:7551), so it is taken from the display list
        var container = (AsContainer)((AsContainer)h.Entity).Children[3];
        Assert.That(container.Children.Count, Is.EqualTo(1));
        double y = Rt.ToNumber(h.Get(h.Entity, "y")), ht = Rt.ToNumber(h.Get(h.Entity, "height"));
        Assert.That(Rt.ToNumber(h.Get(h.Entity, "x")), Is.EqualTo(0));
        Assert.That(y, Is.EqualTo(1080 - (int)ht - 300));
    }

    [Test]
    public void NoticeTabClosedLeftMiddleAndToggles()
    {
        var m = Model();
        var c = new NoticeDialogController();
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        // OnLoadComplete: gotoAndStop("hideComplete"); vfunc_43: ASDisplayObject::vfunc_2(3, 0, 0)
        Assert.That(Rt.ToString(h.Get(h.Entity, "currentLabel")), Is.EqualTo("hideComplete"));
        double ht = Rt.ToNumber(h.Get(h.Entity, "height"));
        Assert.That(Rt.ToNumber(h.Get(h.Entity, "x")), Is.EqualTo(0));
        Assert.That(Rt.ToNumber(h.Get(h.Entity, "y")), Is.EqualTo((int)((1080 - (int)ht) * 0.5f)));
        var show = h.Child("m_showButton", h.Child("m_mainWindow"));
        Assert.That(show, Is.Not.Null);
        c.ToggleShowHide();                 // the show button's click: Show
        for (int i = 0; i < 30; i++) h.Player.Tick();
        Assert.That(Rt.ToString(h.Get(h.Entity, "currentLabel")), Is.EqualTo("show"));
        Assert.That(Rt.ToBoolean(h.Get(show, "selected")), Is.True);
        Assert.That(c.BrowserVisible, Is.True);
        c.ToggleShowHide();                 // and again: Hide
        for (int i = 0; i < 30; i++) h.Player.Tick();
        // gotoAndPlay("hide") plays on to the frame40 stop, in hideComplete
        Assert.That(Rt.ToString(h.Get(h.Entity, "currentLabel")), Is.EqualTo("hideComplete"));
        Assert.That(Rt.ToBoolean(h.Get(show, "selected")), Is.False);
        Assert.That(c.BrowserVisible, Is.False);
    }

    static List<string> AllTexts(AsDisplayObject o)
    {
        var l = new List<string>();
        void Walk(AsDisplayObject d)
        {
            if (d is AsTextField t && !string.IsNullOrEmpty(t.Text)) l.Add($"{t.InstanceName}={t.Text}");
            if (d is AsContainer c) foreach (var k in c.Children) Walk(k);
        }
        Walk(o);
        return l;
    }
}
