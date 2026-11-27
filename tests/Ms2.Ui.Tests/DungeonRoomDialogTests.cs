using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The dungeon room window on its movie (needs MS2_DATA): the group list, the room list and its texts, the opening calls and
/// the party level (uidungeonroomdialog.cpp). Rooms are compared by id and count; a table without groups makes those tests
/// ignore.</summary>
public class DungeonRoomDialogTests
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

    sealed class Env
    {
        public readonly List<string> Notices = new();
        public PartyBook Book = new();
        public DungeonRoomDialogController C;
        public UiHost Host;
        public Player P;
    }

    static Env Make(Action<DungeonRoomDialogController> before = null, ushort level = 60, bool load = true, PartyBook book = null)
    {
        var t = Tables();
        var e = new Env();
        if (book != null) e.Book = book;
        e.C = new DungeonRoomDialogController(e.Book) { Notice = e.Notices.Add, LocalPlayer = () => (level, 0u) };
        before?.Invoke(e.C);
        if (!load) return e;
        var (rt, p) = src.Open(e.C.Movie, r => r.Budget = 20_000_000, e.C.Libraries);
        p.StageW = 1920; p.StageH = 1080;
        e.P = p;
        e.Host = new UiHost(p, e.C, new UiModel(t));
        Assert.That(e.Host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors) + " missing: " + string.Join("; ", rt.Missing.Keys));
        for (int i = 0; i < 5; i++) p.Tick();
        return e;
    }

    static DungeonRoomTable Rooms() => Tables().DungeonRooms;

    /// <summary>The first dungeon group type (1..14) with rooms; ignores the test where the data has none (GMS2).</summary>
    static int TypeWithRooms(int skip = 0)
    {
        for (int t = 1; t <= 14; t++)
            if (Rooms().GetGroupRooms(t).Count > 0 && skip-- <= 0) return t;
        Assert.Ignore("the room table has no groups");
        return 0;
    }

    static (bool Enabled, string Label) Item(Env e, int i)
    {
        var item = e.Host.Child("m_dungeonListItem" + i);
        Assert.That(item, Is.Not.Null, "renderer " + i);
        return (Rt.ToBoolean(e.Host.Get(item, "enabled")), Rt.ToString(e.Host.Get(item, "label")));
    }

    [Test]
    public void TheGroupMappingsAreInverse()
    {
        for (int type = 1; type <= 14; type++)
            Assert.That(DungeonRoomDialogController.TypeOfGroup(DungeonRoomDialogController.GroupOfType(type)), Is.EqualTo(type), "type " + type);
        Assert.That(DungeonRoomDialogController.TypeOfGroup(0), Is.EqualTo(0));
        Assert.That(DungeonRoomDialogController.TypeOfGroup(1), Is.EqualTo(0));
        Assert.That(DungeonRoomDialogController.GroupOfType(0), Is.EqualTo(1));
        Assert.That(DungeonRoomDialogController.GroupOfType(99), Is.EqualTo(1));
    }

    [Test]
    public void TheGroupListHasTheFavouritesRecommendedAndTheUngatedGroups()
    {
        if (!Rooms().RoomList.Any(r => r.GroupType > 0)) Assert.Ignore("this room table has no groups (GMS2)");
        var e = Make(c => c.SetGroupType(0));
        // Groups 0 (favourites) and 1 (recommended) have no type; types 1 and 2 (groups 2 and 3) need no feature.
        Assert.That(e.C.GroupIds.Take(4), Is.EqualTo(new[] { 0, 1, 2, 3 }));
        var combo = e.Host.Child("m_dungoenGroupCombo");
        var dp = e.Host.Get(combo, "dataProvider");
        Assert.That(Rt.ToInt32(e.Host.Get(dp, "length")), Is.EqualTo(e.C.GroupIds.Count));
        Assert.That(Rt.ToInt32(e.Host.Get(combo, "menuRowCount")), Is.EqualTo(e.C.GroupIds.Count));
    }

    [Test]
    public void AGatedGroupIsListedOnlyWithItsFeature()
    {
        if (!Rooms().RoomList.Any(r => r.GroupType > 0)) Assert.Ignore("this room table has no groups (GMS2)");
        // Group 4 (type 3) needs feature 0x80.
        var off = Make();
        Assert.That(off.C.GroupIds, Does.Not.Contain(4));
        var t = Tables();
        var model = new UiModel(t);
        model.Features.Add(0x80);
        var c = new DungeonRoomDialogController(new PartyBook()) { LocalPlayer = () => ((ushort)60, 0u) };
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000, c.Libraries);
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, model);
        Assert.That(h.Load(), Is.True);
        Assert.That(c.GroupIds, Does.Contain(4));
    }

    [Test]
    public void TheSetBeforeLoadCallsFindRoomsWithoutAModel()
    {
        // The openers call OpenRoom / OpenDungeonOrRoom before the movie loads (no host and no model yet): they read PreloadTables.
        var room = Rooms().RoomList.FirstOrDefault(r => r.Id > 100000);
        if (room == null) Assert.Ignore("the room table has no rooms");
        var c = new DungeonRoomDialogController(new PartyBook()) { PreloadTables = Tables() };
        Assert.That(c.OpenRoom(room.Id), Is.True);
        Assert.That(c.Room?.Id, Is.EqualTo(room.Id));
        var c2 = new DungeonRoomDialogController(new PartyBook()) { PreloadTables = Tables() };
        Assert.That(c2.OpenDungeonOrRoom(room.Id), Is.True);
        Assert.That(c2.Room?.Id, Is.EqualTo(room.Id));
        var c3 = new DungeonRoomDialogController(new PartyBook()) { PreloadTables = Tables(), Notice = _ => { } };
        Assert.That(c3.OpenRoom(uint.MaxValue), Is.False, "an unknown room opens nothing");
    }

    [Test]
    public void OpeningAGroupTypeListsItsRoomsInTheRenderers()
    {
        int type = TypeWithRooms();
        var e = Make(c => c.SetGroupType(type));
        int group = DungeonRoomDialogController.GroupOfType(type);
        var expected = Rooms().GetGroupRooms(type);
        Assert.That(e.C.CurrentGroup, Is.EqualTo(group));
        Assert.That(e.C.Rooms.Select(r => r.Id), Is.EqualTo(expected.Select(r => r.Id)));
        Assert.That(e.C.Room, Is.SameAs(e.C.Rooms[0]));
        var combo = e.Host.Child("m_dungoenGroupCombo");
        Assert.That(Rt.ToInt32(e.Host.Get(combo, "selectedIndex")), Is.EqualTo(e.C.GroupIds.ToList().IndexOf(group)));
        var list = e.Host.Child("m_dungeonList");
        Assert.That(Rt.ToInt32(e.Host.Get(list, "selectedIndex")), Is.EqualTo(0));
        for (int i = 1; i <= DungeonRoomDialogController.ItemCount; i++)
        {
            var (enabled, label) = Item(e, i);
            int row = Rt.ToInt32(e.Host.Get(e.Host.Child("m_dungeonListItem" + i), "index"));
            if (row < expected.Count)
            {
                Assert.That(enabled, Is.True, "item " + i);
                Assert.That(label, Is.EqualTo(e.C.GetRoomListText(expected[row], true)), "item " + i);
            }
            else
            {
                Assert.That(enabled, Is.False, "item " + i);
                Assert.That(label, Is.EqualTo(""), "item " + i);
            }
        }
    }

    [Test]
    public void ARoomLimitedByLevelHasTheLimitTextAppended()
    {
        var withLimit = Rooms().RoomList.FirstOrDefault(r => r.LimitPlayerLevel > 0);
        if (withLimit == null) Assert.Ignore("no room has a level limit");
        var e = Make();
        var plain = e.C.GetRoomListText(withLimit, false);
        var full = e.C.GetRoomListText(withLimit, true);
        Assert.That(full.Length, Is.GreaterThan(plain.Length));
        Assert.That(full, Does.StartWith(plain));
        Assert.That(e.C.GetRoomListText(null, true), Is.EqualTo(""));
    }

    [Test]
    public void ARoomOpensItsGroupAndRow()
    {
        int type = TypeWithRooms();
        var all = Rooms().GetGroupRooms(type);
        var room = all[all.Count - 1];
        var e = Make(c => c.SetRoom(room));
        Assert.That(e.C.CurrentGroup, Is.EqualTo(DungeonRoomDialogController.GroupOfType(room.GroupType)));
        Assert.That(e.C.Room.Id, Is.EqualTo(room.Id));
        var list = e.Host.Child("m_dungeonList");
        Assert.That(Rt.ToInt32(e.Host.Get(list, "selectedIndex")), Is.EqualTo(all.Count - 1));
    }

    /// <summary>A user pick: the control's row, then its "listIndexChange" (the dropdown and list raise it on a pick, not on a script set).</summary>
    static void Pick(Env e, object target, int index)
    {
        e.Host.Set(target, "selectedIndex", index);
        e.Host.Invoke(target, "validateNow");   // the tile list applies its selectedIndex when it validates
        var ev = e.Host.RT.Construct(e.Host.RT.GetClass("flash.events.Event"), new object[] { "listIndexChange" });
        e.Host.Invoke(target, "dispatchEvent", ev);
    }

    [Test]
    public void APickedGroupAndRowChangeTheShownRooms()
    {
        int type = TypeWithRooms();
        var e = Make(c => c.SetGroupType(type));
        int other = TypeWithRooms(1);
        int group = DungeonRoomDialogController.GroupOfType(other);
        if (!e.C.GroupIds.Contains(group)) Assert.Ignore("the second type's group is not listed");
        var combo = e.Host.Child("m_dungoenGroupCombo");
        Pick(e, combo, e.C.GroupIds.ToList().IndexOf(group));
        Assert.That(e.C.CurrentGroup, Is.EqualTo(group));
        Assert.That(e.C.Rooms.Select(r => r.Id), Is.EqualTo(Rooms().GetGroupRooms(other).Select(r => r.Id)));
        if (e.C.Rooms.Count > 1)
        {
            var list = e.Host.Child("m_dungeonList");
            Pick(e, list, 1);
            Assert.That(e.C.Room.Id, Is.EqualTo(e.C.Rooms[1].Id));
        }
    }

    [Test]
    public void ADungeonIdOpensItsFirstRoomAndAnUnknownOneSaysPreparing()
    {
        var e = Make();
        var room = Rooms().RoomList.FirstOrDefault(r => r.Id > 100000);
        if (room == null) Assert.Ignore("the room table has no room with an id over 100000");
        uint dungeon = room.Id / 1000;
        Assert.That(e.C.OpenDungeonOrRoom(room.Id), Is.True);
        Assert.That(e.C.Room.Id, Is.EqualTo(room.Id));
        Assert.That(e.C.OpenDungeonOrRoom(dungeon), Is.True);
        Assert.That(e.C.Room.Id, Is.EqualTo(Rooms().GetFirstRoom(dungeon).Id));
        Assert.That(e.Notices, Is.Empty);
        Assert.That(e.C.OpenDungeonOrRoom(100001), Is.False);   // over 100000: a room id the table lacks
        Assert.That(e.C.OpenDungeonOrRoom(77), Is.False);       // a dungeon id with no rooms
        Assert.That(e.Notices, Is.EqualTo(new[] { "s_msg_preparing", "s_msg_preparing" }));
        Assert.That(e.C.Room.Id, Is.EqualTo(Rooms().GetFirstRoom(dungeon).Id), "a failed open leaves the room");
    }

    [Test]
    public void AGroupWithNoRoomsShowsTheNoticeAndCloses()
    {
        int empty = 0;
        for (int t = 1; t <= 14 && empty == 0; t++)
            if (Rooms().GetGroupRooms(t).Count == 0) empty = t;
        if (empty == 0) Assert.Ignore("every group type has rooms (or the table has none and the test is moot)");
        var e = Make(c => c.SetGroupType(empty));
        Assert.That(e.Notices, Does.Contain("s_room_dungeon_notFoundList"));
        Assert.That(e.C.Room, Is.Null);
    }

    [Test]
    public void NoTypeGivenFallsBackFromTheFavouritesToTheRecommendedOrNormalGroup()
    {
        // The favourites (group 0) have no rooms, so the recommended or the normal group shows.
        var f = Make(c => c.SetGroupType(0));
        Assert.That(f.Notices, Does.Not.Contain("s_room_dungeon_notFoundList"));
        if (f.C.Room != null) Assert.That(f.C.CurrentGroup, Is.AnyOf(1, 2));
    }

    [Test]
    public void ThePartyLevelIsTheLowestOnlineMember()
    {
        var book = new PartyBook();
        PartyUser U(long id, short level, int gear) => new() { AccountId = id + 100, CharacterId = id, Name = "n" + id, MapId = 2000001, Level = level, Channel = 1, GearScore = gear };
        book.Apply(new PartyInfo(false, 9, 1, new[]
        {
            new PartyInfoMember(false, U(1, 50, 900)), new PartyInfoMember(false, U(2, 30, 700)), new PartyInfoMember(false, U(3, 20, 100)),
        }, false, 0), 1, false);
        book.Members.First(m => m.CharacterId == 3).Offline = true;
        var e = Make(book: book, level: 70, load: false);
        Assert.That(e.C.GetPartyLevel(), Is.EqualTo(((ushort)30, 700u)), "the offline member is skipped");
        var alone = Make(level: 70, load: false);
        Assert.That(alone.C.GetPartyLevel(), Is.EqualTo(((ushort)70, 0u)), "no party: the player's level");
        alone.C.LocalPlayer = () => null;
        Assert.That(alone.C.GetPartyLevel(), Is.EqualTo(((ushort)0, 0u)), "no player");
    }

    [Test]
    public void ThePartyPacketsRefreshAndTheClosedDialogForgetsItsRoom()
    {
        int type = TypeWithRooms();
        var e = Make(c => c.SetGroupType(type));
        Assert.That(e.C.Room, Is.Not.Null);
        e.C.SetPortalId(7);
        e.C.Close();
        Assert.That(e.C.Room, Is.Null);
        Assert.That(e.C.PortalId, Is.EqualTo(0));
        Assert.That(e.C.Rooms, Is.Empty);
        Assert.That(e.C.GroupIds, Is.Empty);
    }

    [Test]
    public void TheSeasonSummaryTooltipAnswersWithAnEmptyTextAndNeedsARoom()
    {
        int type = TypeWithRooms();
        var e = Make(c => c.SetGroupType(type));
        var tip = e.C.GetTooltip(new object[] { "m_seasonSummary" });
        Assert.That(tip, Is.Not.Null);
        Assert.That(tip["tooltip"], Is.EqualTo("export_common_tooltip_tooltipText"));
        Assert.That(tip["tooltipAlign"], Is.EqualTo(7));
        Assert.That(tip["text"], Is.EqualTo(""));
        e.C.SetRoom(null);
        Assert.That(e.C.GetTooltip(new object[] { "m_seasonSummary" }), Is.Null, "no room: no tooltip");
    }
}
