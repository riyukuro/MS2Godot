using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The dungeon lobby notice (UIDungeonLobby, uidungeonlobby.cpp) on its movie (needs MS2_DATA): the title and sub title the packet's
/// room and value give, and the web guide button for the raid groups. No test asserts a language-specific string.</summary>
public class DungeonLobbyTests
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

    static DungeonLobbyController Open(int roomId, int size, out UiHost host)
    {
        var t = Tables();
        var c = new DungeonLobbyController();
        c.OnRoomPacket(new DungeonWaiting(roomId, size));
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000, c.Libraries);
        p.StageW = 1920; p.StageH = 1080;
        host = new UiHost(p, c, new UiModel(t));
        Assert.That(host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors) + " missing: " + string.Join("; ", rt.Missing.Keys));
        for (int i = 0; i < 5; i++) p.Tick();
        return c;
    }

    static string Text(UiHost h, string name)
    {
        var n = h.Child(name);
        Assert.That(n, Is.Not.Null, name);
        var f = h.Has(n, "htmlText") ? n : h.Child("textField", n);
        return Rt.ToString(h.Get(f, "text"));
    }

    static DungeonRoomData RoomOfGroup(Func<int, bool> group)
    {
        var r = Tables().DungeonRooms.RoomList.FirstOrDefault(x => group(x.GroupType));
        if (r == null) Assert.Ignore("no room of that group");
        return r;
    }

    [Test]
    public void TheClientsTypeNameAndUiIdAreTheDialogs()
    {
        var c = new DungeonLobbyController();
        Assert.That(c.TypeName, Is.EqualTo("DungeonLobby"));
        Assert.That(DungeonLobbyController.UiId, Is.EqualTo(0x4f21));
    }

    [Test]
    public void ThePacketSetsTheRoomAndTheSecondValue()
    {
        var c = new DungeonLobbyController();
        c.OnRoomPacket(new DungeonWaiting(77, 5));
        Assert.That((c.RoomId, c.Size), Is.EqualTo((77, 5)));
    }

    [Test]
    public void TheTitleAndTheSubTitleCarryTheRoomAndTheValue()
    {
        var room = RoomOfGroup(g => g > 0);
        Open((int)room.Id, 7, out var h);
        string title = Text(h, "m_title"), sub = Text(h, "m_subTitle");
        string name = Tables().FieldEnteranceName((int)room.Id) ?? "";
        Assert.That(title, Does.Contain(name));
        Assert.That(sub, Does.Contain("7"));
    }

    [Test]
    public void ARoomNotInTheTableLeavesTheTexts()
    {
        Open(int.MaxValue, 3, out var h);
        Assert.That(Text(h, "m_subTitle"), Does.Not.Contain("3").Or.Empty);
    }

    [Test]
    public void TheWebGuideButtonIsRightOfTheTitleForARaidAndHiddenForAnotherGroup()
    {
        var raid = RoomOfGroup(g => g == 2 || g == 3);
        Open((int)raid.Id, 1, out var hr);
        var web = hr.Child("m_webGuideButton");
        Assert.That(Rt.ToBoolean(hr.Get(web, "visible")), Is.True);
        double x = Rt.ToNumber(hr.Get(web, "x")), titleX = Rt.ToNumber(hr.Get(hr.Child("m_title"), "x"));
        Assert.That(x, Is.GreaterThan(titleX));

        var normal = RoomOfGroup(g => g > 0 && g != 2 && g != 3);
        Open((int)normal.Id, 1, out var hn);
        Assert.That(Rt.ToBoolean(hn.Get(hn.Child("m_webGuideButton"), "visible")), Is.False);
    }
}
