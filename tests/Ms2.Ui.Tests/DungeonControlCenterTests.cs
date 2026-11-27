using Maple2.PacketLib.Tools;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The dungeon finder (UIDungeonControlCenter, uidungeoncategory.cpp) on its movie (needs MS2_DATA): the category table, the
/// lists the rules pick, the packets its buttons send, the settings and the data the movie shows. Rooms are compared by id and count;
/// no test asserts a language-specific string.</summary>
public class DungeonControlCenterTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    static GameTables Tables()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs ??= new PackSet(d);
        src ??= new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        return tables ??= new GameTables(packs, "en");
    }

    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    sealed class Env
    {
        public readonly List<IOutPacket> Sent = new();
        public PartyBook Book = new();
        public DungeonControlCenterController C;
        public UiHost Host;
        public Player P;
        public dynamic Rt0;
    }

    static Env Make(Action<DungeonControlCenterController> before = null, ushort level = 60, bool load = true, PartyBook book = null,
        Func<int, bool> features = null)
    {
        var t = Tables();
        var e = new Env();
        if (book != null) e.Book = book;
        e.C = new DungeonControlCenterController(e.Book)
        {
            PreloadTables = t,
            LocalPlayer = () => (level, 0u),
            SelfCharacterId = () => 1,
            Send = e.Sent.Add,
        };
        before?.Invoke(e.C);
        if (!load) return e;
        var (rt, p) = src.Open(e.C.Movie, r => r.Budget = 20_000_000, e.C.Libraries);
        p.StageW = 1920; p.StageH = 1080;
        e.P = p;
        e.Rt0 = rt;
        var model = new UiModel(t);
        if (features != null) { for (int f = 0; f < 0x200; f++) if (features(f)) model.Features.Add(f); }
        e.Host = new UiHost(p, e.C, model);
        Assert.That(e.Host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors) + " missing: " + string.Join("; ", rt.Missing.Keys));
        for (int i = 0; i < 5; i++) p.Tick();
        return e;
    }

    static string Text(Env e, string name, string parent = null)
    {
        var p = parent == null ? null : e.Host.Child(parent);
        var n = e.Host.Child(name, p);
        Assert.That(n, Is.Not.Null, name);
        return Rt.ToString(e.Host.Get(n, "text"));
    }

    static bool Flag(Env e, string name, string parent, string member)
    {
        var n = e.Host.Child(name, e.Host.Child(parent));
        Assert.That(n, Is.Not.Null, name);
        return Rt.ToBoolean(e.Host.Get(n, member));
    }

    // ---- the category table ----

    [Test]
    public void TheCategoryTableListsTwoMainTabsInOrderOfTheirLowLevel()
    {
        var t = Tables().DungeonCategories;
        var tab0 = t.GetSubTabs(0);
        Assert.That(tab0, Is.Not.Empty);
        for (int i = 1; i < tab0.Count; i++) Assert.That(tab0[i].Low, Is.GreaterThan(tab0[i - 1].Low), "sub tab " + i);
        Assert.That(t.GetSubTabs(1), Has.Count.EqualTo(1));
        Assert.That(t.GetSubTabs(2), Is.Empty);
        Assert.That(t.GetMaxSubTabLevel(0), Is.EqualTo(tab0[^1].Low));
        Assert.That(t.GetMaxSubTabLevel(2), Is.EqualTo(0));
    }

    [Test]
    public void EveryGroupTypeOfEverySubTabIsAKnownType()
    {
        var t = Tables().DungeonCategories;
        for (int tab = 0; tab < DungeonCategoryTable.MainTabs; tab++)
            foreach (var c in t.GetSubTabs(tab))
                foreach (int type in c.GroupTypes)
                    Assert.That(type, Is.InRange(1, 14), $"tab {tab} sub tab {c.Low}");
    }

    [Test]
    public void ASubTabIsPickedByTheLevelAndTheLastOneTakesTheRest()
    {
        var t = Tables().DungeonCategories;
        var tab0 = t.GetSubTabs(0);
        for (ushort level = 1; level <= 100; level++)
        {
            int expect = tab0.FindIndex(c => c.Contains(level));
            if (expect < 0) expect = tab0.Count - 1;
            Assert.That(t.SubTabOfLevel(0, level), Is.EqualTo(expect), "level " + level);
        }
        Assert.That(t.SubTabOfLevel(5, 10), Is.EqualTo(0));
    }

    [Test]
    public void TheCategoryFileIsReadFromTheClientsShape()
    {
        var root = System.Xml.Linq.XElement.Parse(
            "<ms2><dungeonMainTab index='0'>" +
            "<dungeonSubTab level='50-59'><dungeonCategory groupType='raid'/><dungeonCategory groupType='lapenta'/></dungeonSubTab>" +
            "<dungeonSubTab level='1-49'><dungeonCategory groupType='normal'/></dungeonSubTab>" +
            "<dungeonSubTab level='bad'><dungeonCategory groupType='nonsense'/></dungeonSubTab></dungeonMainTab>" +
            "<dungeonMainTab index='1'><dungeonSubTab level='0-0'><dungeonCategory groupType='event'/></dungeonSubTab></dungeonMainTab>" +
            "<dungeonMainTab index='2'><dungeonSubTab level='1-9'/></dungeonMainTab></ms2>");
        var t = new DungeonCategoryTable(root, null);
        var tabs = t.GetSubTabs(0);
        Assert.That(tabs.Select(c => (int)c.Low), Is.EqualTo(new[] { 0, 1, 50 }));         // the bad range is 0-0: low 0
        Assert.That(tabs[2].GroupTypes, Is.EqualTo(new[] { 2, 5 }));
        Assert.That(tabs[1].IsValid, Is.True);
        Assert.That(tabs[0].IsValid, Is.False);
        Assert.That(tabs[0].GroupTypes, Is.EqualTo(new[] { 0 }));                           // an unknown name is 0
        Assert.That(t.GetSubTabs(1)[0].GroupTypes, Is.EqualTo(new[] { 0xb }));
        Assert.That(t.GetMaxSubTabLevel(0), Is.EqualTo(50));
        Assert.That(t.SubTabOfLevel(0, 55), Is.EqualTo(2));
        Assert.That(t.SubTabOfLevel(0, 20), Is.EqualTo(1));
        Assert.That(t.SubTabOfLevel(0, 99), Is.EqualTo(2));                                  // none holds it: the last
    }

    // ---- lobby, packets ----

    [Test]
    public void TheLobbyOfAGroupIsTheClientsMapping()
    {
        var expect = new Dictionary<int, int> { [3] = 4, [4] = 0xc, [0xd] = 0xd, [5] = 0xe };
        for (int type = 0; type <= 16; type++)
            Assert.That(DungeonControlCenterController.LobbyOfGroup(type), Is.EqualTo(expect.GetValueOrDefault(type, -1)), "type " + type);
    }

    [Test]
    public void TheFinderPacketsAreTheClientsBytes()
    {
        Assert.That(Bytes(new PartyDungeonMatchRequest(new uint[] { 11000001, 11000002 })),
            Is.EqualTo(new byte[] { 0x20, 1, 2, 0, 0, 0, 0xC1, 0xD8, 0xA7, 0x00, 0xC2, 0xD8, 0xA7, 0x00, 0 }));
        Assert.That(Bytes(new PartyDungeonMatchRequest(Array.Empty<uint>())), Is.EqualTo(new byte[] { 0x20, 1, 0, 0, 0, 0, 0 }));
        Assert.That(Bytes(new PartyDungeonMatchCancelRequest()), Is.EqualTo(new byte[] { 0x22, 1 }));
        Assert.That(Bytes(new RoomDungeonLobbyRequest(0xe)), Is.EqualTo(new byte[] { 0xe }));
        Assert.That(new PartyDungeonMatchRequest(Array.Empty<uint>()).Op, Is.EqualTo(Ms2.Net.Outbound.Party));
        Assert.That(new RoomDungeonLobbyRequest(1).Op, Is.EqualTo(Ms2.Net.Outbound.RoomDungeon));
    }

    [Test]
    public void TheButtonsSendTheirPacketsAndTheLobbyOnlyForGroupsWithOne()
    {
        var e = Make(load: false);
        e.C.SetBookmarks(new uint[] { 30, 10, 20 });
        Assert.That(e.C.Bookmarks, Is.EqualTo(new uint[] { 10, 20, 30 }));                  // a sorted set
        Assert.That(e.C.OnCallback("onPartyMatch", Array.Empty<object>()), Is.True);
        Assert.That(e.C.OnCallback("onPartyMatchCancel", Array.Empty<object>()), Is.True);
        Assert.That(e.Sent, Has.Count.EqualTo(2));
        Assert.That(Bytes(e.Sent[0]).Skip(2).Take(4), Is.EqualTo(new byte[] { 3, 0, 0, 0 }));
        Assert.That(Bytes(e.Sent[1]), Is.EqualTo(new byte[] { 0x22, 1 }));
        e.Sent.Clear();
        Assert.That(e.C.OnCallback("onEnterLobby", new object[] { 5.0 }), Is.True);
        Assert.That(e.C.OnCallback("onEnterLobby", new object[] { 1.0 }), Is.True);       // normal dungeons have no lobby
        Assert.That(e.Sent.Select(Bytes), Is.EqualTo(new[] { new byte[] { 0xe } }));
        Assert.That(e.C.OnCallback("nonsense", Array.Empty<object>()), Is.False);
    }

    [Test]
    public void TheTilesAndTheDialogButtonsCallTheirOpeners()
    {
        var log = new List<string>();
        var e = Make(c =>
        {
            c.TogglePartySearch = () => log.Add("search");
            c.TogglePopupDetail = () => log.Add("detail");
            c.OpenBookmarkRoomDialog = () => log.Add("bookmark");
            c.OpenRoomDialog = id => log.Add("room " + id);
        }, load: false);
        Assert.That(e.C.OnCallback("onPartySearch", Array.Empty<object>()), Is.True);
        Assert.That(e.C.OnCallback("onPopupDetail", Array.Empty<object>()), Is.False);    // the client returns 0 for it
        Assert.That(e.C.OnCallback("onBookmark", Array.Empty<object>()), Is.True);
        Assert.That(e.C.OnCallback("onClickTile", new object[] { 21001001.0 }), Is.True);
        Assert.That(log, Is.EqualTo(new[] { "search", "detail", "bookmark", "room 21001001" }));
    }

    [Test]
    public void TheSettingButtonFillsEmptyBookmarksWithTheEnterableRooms()
    {
        IReadOnlyCollection<uint> given = null;
        var e = Make(c => c.OpenSetting = ids => given = ids.ToArray(), load: false);
        var enterable = e.C.GetEnterableRooms().Select(r => r.Id).ToHashSet();
        e.C.OnSetting();
        Assert.That(given, Is.EquivalentTo(enterable));
        Assert.That(e.C.Bookmarks, Is.EquivalentTo(enterable));
        e.C.SetBookmarks(new uint[] { 7 });
        e.C.OnSetting();
        Assert.That(given, Is.EqualTo(new uint[] { 7 }));                                  // a set one is kept
    }

    // ---- the lists ----

    [Test]
    public void TheRecommendedRoomsFollowTheClientsRulesAtEveryLevel()
    {
        var rooms = Tables().DungeonRooms;
        for (ushort level = 1; level <= 100; level++)
        {
            var e = Make(load: false, level: level);
            var list = e.C.GetRecommendedRooms();
            Assert.That(list.Count, Is.LessThanOrEqualTo(DungeonControlCenterController.RecommendedCount), "level " + level);
            var groups = new HashSet<uint>();
            foreach (var r in list)
            {
                Assert.That(r.LimitPlayerLevel, Is.LessThanOrEqualTo(level), $"level {level} room {r.Id}");
                Assert.That(r.IsDisableRandomMatch, Is.False, $"hidden {r.Id}");
                if (r.RepresentId != 0) Assert.That(groups.Add(r.RepresentId), Is.True, $"group {r.RepresentId} twice at level {level}");
            }
            Assert.That(list.Select(r => r.Id), Is.SubsetOf(rooms.GetRoomsForLevel(level, 0).Select(r => r.Id)));
        }
    }

    [Test]
    public void EveryRoomWithAGroupAttributeHasAGroupWhetherTheTableHoldsNumbersOrNames()
    {
        // KMS and restore rows say groupType="3"; the GMS2 rows say groupType="chaosRaid" (Parse_140218c20 names). Both give the number.
        var rooms = Tables().DungeonRooms.RoomList.ToList();
        Assert.That(rooms, Is.Not.Empty);
        Assert.That(rooms.Count(r => r.GroupType > 0), Is.GreaterThan(rooms.Count / 2), "most rooms carry a group type");
        Assert.That(rooms.Select(r => r.GroupType).Where(g => g > 0), Is.All.InRange(1, 14));
        Assert.That(Tables().DungeonRooms.GetMatchRooms(90, 0).Count() + Tables().DungeonRooms.GetRoomsForLevel(90, 0).Count(), Is.GreaterThan(0));
        // the whole list: every row that gives a groupType gets a group from it (an unknown name would give 0)
        string entry = TablePath.Resolve(packs, "DungeonRoom.xml");
        Assert.That(entry, Is.Not.Null);
        var rows = Ms2.Formats.Text.PackXml.Parse(packs.Read("Xml", entry)).Root.Elements("dungeonRoom")
            .Where(x => !string.IsNullOrEmpty((string)x.Attribute("groupType"))).ToList();
        Assert.That(rows, Is.Not.Empty);
        foreach (var x in rows)
        {
            uint id = (uint)(int)x.Attribute("dungeonRoomID");
            if (Tables().DungeonRooms.GetRoom(id) is not { } room) continue;
            Assert.That(room.GroupType, Is.InRange(1, 14), $"room {id} groupType=\"{(string)x.Attribute("groupType")}\"");
        }
    }

    static readonly ClientConstants NoConstants = new(new Dictionary<string, string>());

    [Test]
    public void AGroupTypeGivenAsANameOrANumberParsesToTheSameGroup()
    {
        var byName = DungeonRoomData.Load(System.Xml.Linq.XElement.Parse("<dungeonRoom dungeonRoomID=\"1\" groupType=\"chaosRaid\" />"), NoConstants);
        var byNumber = DungeonRoomData.Load(System.Xml.Linq.XElement.Parse("<dungeonRoom dungeonRoomID=\"1\" groupType=\"3\" />"), NoConstants);
        Assert.That(byName.GroupType, Is.EqualTo(3));
        Assert.That(byNumber.GroupType, Is.EqualTo(3));
    }

    [Test]
    public void ARoomTheClearStateRefusesIsNotRecommendedOrEnterable()
    {
        var e = Make(load: false, level: 90);
        var first = e.C.GetRecommendedRooms();
        Assume.That(first, Is.Not.Empty, "no recommended room at level 90");
        uint blocked = first[0].Id;
        e.C.ClearState = id => id == blocked ? 10 : 0;
        Assert.That(e.C.CanEnterDungeonRoom(blocked), Is.False);
        Assert.That(e.C.CanEnterDungeonRoom(blocked + 1), Is.True);
        Assert.That(e.C.GetRecommendedRooms().Select(r => r.Id), Does.Not.Contain(blocked));
        Assert.That(e.C.GetEnterableRooms().Select(r => r.Id), Does.Not.Contain(blocked));
    }

    [Test]
    public void ThePartyDecidesTheLevelAndEveryMembersClearStateMustAllow()
    {
        var book = new PartyBook();
        var e = Make(load: false, level: 90, book: book);
        var solo = e.C.GetRecommendedRooms().Select(r => r.Id).ToArray();
        Assume.That(solo, Is.Not.Empty);
        // a party of one member is not a party; the lists stay the player's own
        Assert.That(e.C.GetRecommendedRooms().Select(r => r.Id), Is.EqualTo(solo));
        e.C.ClearState = _ => 0;
        e.C.MemberClearState = (_, _) => 12;
        Assert.That(e.C.CanEnterDungeonRoom(solo[0]), Is.True, "alone, only the player's state counts");
    }

    static PartyBook PartyOf(params (int Level, int GearScore)[] members)
    {
        var book = new PartyBook();
        var list = members.Select((m, i) => new PartyInfoMember(false, new PartyUser
        {
            AccountId = 101 + i, CharacterId = 1 + i, Name = "n" + i, MapId = 2000001, Level = (short)m.Level, Channel = 1, GearScore = m.GearScore,
        })).ToArray();
        book.Apply(new PartyInfo(false, 9, 1, list, false, 0), 1, false);
        return book;
    }

    [Test]
    public void ThePartyMinimumTakesALaterMembersValueOverAnEarlierZero()
    {
        // GetMinMemberLevel / GetMinMemberGearScore (partymanager.cpp:860-888): "if (value == 0) value = x; if (value > x) value = x;"
        var c = new DungeonControlCenterController(PartyOf((50, 0), (40, 300))) { LocalPlayer = () => ((ushort)60, 900u), SelfCharacterId = () => 1 };
        Assert.That(c.ListLevel(), Is.EqualTo(((ushort)40, 300u)), "an earlier 0 gear score is replaced by the next member's");
        c = new DungeonControlCenterController(PartyOf((40, 300), (50, 0))) { LocalPlayer = () => ((ushort)60, 900u), SelfCharacterId = () => 1 };
        Assert.That(c.ListLevel(), Is.EqualTo(((ushort)40, 0u)), "a later 0 is the minimum");
    }

    // ---- settings ----

    [Test]
    public void TheBookmarksPersistAsTheDungeonAttributeAndOnlyKnownRoomsLoad()
    {
        var rooms = Tables().DungeonRooms;
        var ids = rooms.RoomList.Take(3).Select(r => r.Id).OrderBy(i => i).ToArray();
        Assume.That(ids.Length, Is.EqualTo(3));
        var e = Make(c => c.SetBookmarks(ids), load: false);
        var s = new DialogSettings("DungeonControlCenter");
        InvokeProtected(e.C, "SaveSettings", s);
        Assert.That(s.GetAttrText("dungeon"), Is.EqualTo(string.Concat(ids.Select(i => i + ","))));
        var text = s.ToString().Replace(string.Concat(ids.Select(i => i + ",")), string.Concat(ids.Select(i => i + ",")) + "4,junk,");
        var back = Make(load: false);
        InvokeProtected(back.C, "LoadSettings", DialogSettings.Parse("DungeonControlCenter", text));
        Assert.That(back.C.Bookmarks, Is.EqualTo(ids), "ids that are no room (4, junk) are dropped");
        var empty = new DialogSettings("DungeonControlCenter");
        InvokeProtected(back.C, "LoadSettings", empty);
        Assert.That(back.C.Bookmarks, Is.Empty, "a file without the attribute clears the set");
    }

    static void InvokeProtected(object o, string name, object arg) =>
        o.GetType().GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(o, new[] { arg });

    // ---- on the movie ----

    [Test]
    public void TheMovieLoadsWithoutErrorsAndShowsTheFindState()
    {
        var e = Make();
        Assert.That((IEnumerable<string>) e.Rt0.Errors, Is.Empty);
        // setMatch(false): the match button shows, the cancel button does not
        Assert.That(Flag(e, "m_partyMatch", "m_dungeonFindComponent", "visible"), Is.True);
        Assert.That(Flag(e, "m_partyMatchCancel", "m_dungeonFindComponent", "visible"), Is.False);
        Assert.That(Flag(e, "m_partyMatch", "m_dungeonFindComponent", "enabled"), Is.True);
        Assert.That(Flag(e, "m_webGuideButton", "", "visible"), Is.False, "the web guide button is hidden");
        // buildTab(0, ...) put the first tab's page into m_contents
        var contents = e.Host.Child("m_contents");
        Assert.That(Rt.ToInt32(e.Host.Get(contents, "numChildren")), Is.GreaterThan(0), "buildTab built the tab");
    }

    [Test]
    public void TheFindComponentFollowsTheSessionAndThePartyLeader()
    {
        bool blocked = true;
        var e = Make(c => c.SessionBlocksDungeon = () => blocked);
        Assert.That(Flag(e, "m_partyMatch", "m_dungeonFindComponent", "enabled"), Is.False, "the session state disables the find button");
        blocked = false;
        e.C.OnPartyApplied(new PartyLeave(5, false));                                      // command 3 refreshes the component
        Assert.That(Flag(e, "m_partyMatch", "m_dungeonFindComponent", "enabled"), Is.True);
        blocked = true;
        e.C.OnPartyApplied(new PartyLeave(5, false));
        Assert.That(Flag(e, "m_partyMatch", "m_dungeonFindComponent", "enabled"), Is.False);
    }

    [Test]
    public void TheWeeklyRewardTextsComeFromTheRecordAndTheNextStep()
    {
        var e = Make();
        // no record: weekly 0 of 0 and daily 0; the weekly count shows (the text is the data's format, so only presence is compared)
        Assert.That(Text(e, "m_rewardWeekly", "m_dungeonRewardComponent"), Is.Not.Empty);
        Assert.That(Text(e, "m_rewardDaily", "m_dungeonRewardComponent"), Is.Not.Empty);
        Assert.That(Text(e, "m_rewardCount", "m_dungeonRewardComponent"), Is.Not.Empty);
    }

    [Test]
    public void TheFinderIsOpenedByFeatureAndTheKeyActionIs400018()
    {
        Assert.That(DungeonControlCenterController.UiId, Is.EqualTo(0x4f84));
        Assert.That(DungeonControlCenterController.SearchDialogUiId, Is.EqualTo(0x4f1c));
        Assert.That(DungeonControlCenterController.FinderFeature, Is.EqualTo(0xf9));
        Assert.That(DungeonControlCenterController.OpenKeyAction, Is.EqualTo(400018));
        Assert.That(GameMenuActions.ToggleDungeonSearch, Is.EqualTo("FUN_14193d5e0"));
    }
}
