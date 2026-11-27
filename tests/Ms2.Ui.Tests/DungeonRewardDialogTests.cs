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

/// <summary>The dungeon result window (UIDungeonRewardDialog, uidungeonrewarddialog.cpp) on its movie (needs MS2_DATA): the packet's modes and
/// what they open, the texts the result fills, the two timers, the bag and the close sends. No test asserts a language-specific string.</summary>
public class DungeonRewardDialogTests
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
        public DungeonRewardDialogController C;
        public UiHost Host;
        public Player Player;
        public long Now = 1000;
        public readonly List<IOutPacket> Sent = new();
        public UiModel Model;
    }

    static DungeonRoomData RoomOf(Func<DungeonRoomData, bool> pick, string what)
    {
        var r = Tables().DungeonRooms.RoomList.FirstOrDefault(pick);
        if (r == null) Assert.Ignore("no " + what);
        return r;
    }

    static int AnItemId()
    {
        foreach (int id in new[] { 20000001, 20000002, 30000001, 30000002, 40100001, 11000001, 90000001, 20300001 })
            if (Tables().Items.Get(id) != null) return id;
        Assert.Ignore("none of the sample items is in the table");
        return 0;
    }

    static void Info(ByteWriter w, (byte key, int value)[] rewards, (int id, int count, int rank)[] items)
    {
        w.WriteInt(rewards.Length);
        foreach (var (key, value) in rewards) { w.WriteByte(key); w.WriteInt(value); }
        w.WriteInt(items.Length);
        foreach (var (id, count, rank) in items) { w.WriteInt(id); w.WriteInt(count); w.WriteInt(rank); w.WriteBool(false); w.WriteBool(false); w.WriteBool(false); }
    }

    static DungeonRewardPacket Result(int roomId, bool win, bool party, int seconds, byte bits, (byte, int)[] main, (int, int, int)[] mainItems,
        (byte, int)[] bonus, (int, int, int)[] bonusItems)
    {
        var w = new ByteWriter();
        w.WriteByte(0); w.WriteBool(win); w.WriteInt(roomId); w.WriteBool(party); w.WriteInt(seconds); w.WriteInt(10); w.WriteInt(20);
        w.WriteByte(bits); w.WriteByte(0); w.WriteByte(0); w.WriteByte(0);
        Info(w, main, mainItems); Info(w, bonus, bonusItems);
        return DungeonRewardPacket.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()));
    }

    static DungeonRewardPacket Mode(byte mode, Action<ByteWriter> body = null)
    {
        var w = new ByteWriter();
        w.WriteByte(mode);
        body?.Invoke(w);
        return DungeonRewardPacket.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()));
    }

    static Env Open(DungeonRewardPacket packet, bool load = true, Action<DungeonRewardDialogController> setup = null)
    {
        var t = Tables();
        var e = new Env();
        e.Model = new UiModel(t);
        e.Model.Player.Name = "Tester";
        e.Model.Player.SubJob = 100;
        var c = new DungeonRewardDialogController { NowMs = () => e.Now, Send = p => e.Sent.Add(p) };
        e.C = c;
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000, c.Libraries);
        p.StageW = 1920; p.StageH = 1080;
        e.Player = p;
        e.Host = new UiHost(p, c, e.Model);
        setup?.Invoke(c);
        c.OnPacket(packet);
        if (!load) return e;
        Assert.That(e.Host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors) + " missing: " + string.Join("; ", rt.Missing.Keys));
        for (int i = 0; i < 5; i++) p.Tick();
        return e;
    }

    static string Text(Env e, string name)
    {
        var n = e.Host.FindNamed(name, null);
        Assert.That(n, Is.Not.Null, name);
        var f = e.Host.Has(n, "htmlText") ? n : e.Host.Child("textField", n);
        return Rt.ToString(e.Host.Get(f, "text"));
    }

    static bool Visible(Env e, string name) =>
        Rt.ToBoolean(e.Host.Get(e.Host.FindNamed(name, null), "visible"));

    [Test]
    public void TheClientsTypeNameAndUiIdAreTheDialogs()
    {
        var c = new DungeonRewardDialogController();
        Assert.That(c.TypeName, Is.EqualTo("DungeonRewardDialog"));
        Assert.That(DungeonRewardDialogController.UiId, Is.EqualTo(0x4ef9));
    }

    [Test]
    public void ModesZeroOneAndThreeOpenTheWindowAndModeTwoOnlyStores()
    {
        Tables();
        var c = new DungeonRewardDialogController();
        _ = new UiHost(src.Open(c.Movie, r => r.Budget = 20_000_000, c.Libraries).Item2, c, new UiModel(Tables()));
        Assert.That(c.OnPacket(Result(1, true, false, 5, 0, new (byte, int)[0], new (int, int, int)[0], new (byte, int)[0], new (int, int, int)[0])), Is.True);
        Assert.That(c.Type, Is.EqualTo(1));
        c.ResetState();
        Assert.That(c.OnPacket(Mode(1, w => { w.WriteInt(3); w.WriteInt(2); Info(w, new (byte, int)[] { (1, 5) }, new (int, int, int)[0]); })), Is.True);
        Assert.That((c.Type, c.Round, c.Stage), Is.EqualTo((2, 3, 2)));
        c.ResetState();
        Assert.That(c.OnPacket(Mode(2, w => Info(w, new (byte, int)[] { (1, 8) }, new (int, int, int)[0]))), Is.False);
        Assert.That(c.Type, Is.EqualTo(3));
        Assert.That(c.Main.GetMeso(), Is.EqualTo(8));
        c.ResetState();
        Assert.That(c.OnPacket(Mode(3, w => { w.WriteBool(false); Info(w, new (byte, int)[0], new (int, int, int)[0]); })), Is.True);
        Assert.That((c.Type, c.Win), Is.EqualTo((4, false)));
        c.ResetState();
        Assert.That(c.OnPacket(Mode(9)), Is.False);
        Assert.That(c.Type, Is.EqualTo(0));
        Assert.That((c.Win, c.Party, c.Room), Is.EqualTo((true, false, (DungeonRoomData)null)));
    }

    [Test]
    public void ThePacketFindsItsRoomBeforeTheMovieLoads()
    {
        var room = RoomOf(r => r.GroupType > 0, "a dungeon room");
        var c = new DungeonRewardDialogController { PreloadTables = Tables() };
        Info0(out var none);
        Assert.That(c.OnPacket(Result((int)room.Id, true, false, 1, 0b0000_0010, none.a, none.b, none.a, none.b)), Is.True);
        Assert.That(c.Room?.Id, Is.EqualTo(room.Id));
        Assert.That(c.TipLine(2), Is.Not.Empty);
    }

    [Test]
    public void TheResultFillsTheMesoAndExpTexts()
    {
        var room = RoomOf(r => r.GroupType > 0, "a dungeon room");
        var e = Open(Result((int)room.Id, true, false, 125, 0, new (byte, int)[] { (1, 12345), (2, 987) }, new (int, int, int)[0], new (byte, int)[0], new (int, int, int)[0]));
        Assert.That(Text(e, "m_mesoText"), Does.Contain("12").And.Contain("345"));
        Assert.That(Text(e, "m_expText"), Does.Contain("987"));
        Assert.That(Text(e, "m_charName"), Is.EqualTo("Tester"));
    }

    [Test]
    public void TheClearTimeIsMinutesAndSeconds()
    {
        var room = RoomOf(r => r.GroupType > 0, "a dungeon room");
        var e = Open(Result((int)room.Id, true, false, 125, 0, new (byte, int)[0], new (int, int, int)[0], new (byte, int)[0], new (int, int, int)[0]));
        var time = e.Host.FindNamed("m_timeMovie", null);
        var text = Rt.ToString(e.Host.Get(e.Host.Child("textField", time), "text"));
        Assert.That(text, Does.Contain("2").And.Contain("5"));
    }

    [Test]
    public void ThePartyLeaveButtonIsShownOnlyForAPartyResult()
    {
        var room = RoomOf(r => r.GroupType > 0, "a dungeon room");
        Info0(out var none);
        var solo = Open(Result((int)room.Id, true, false, 1, 0, none.a, none.b, none.a, none.b));
        Assert.That(Visible(solo, "m_partyLeaveButton"), Is.False);
        var party = Open(Result((int)room.Id, true, true, 1, 0, none.a, none.b, none.a, none.b));
        Assert.That(Visible(party, "m_partyLeaveButton"), Is.True);
    }

    static void Info0(out ((byte, int)[] a, (int, int, int)[] b) none) => none = (new (byte, int)[0], new (int, int, int)[0]);

    [Test]
    public void TheGuildBlockShowsOnlyForAGuildRaid()
    {
        Info0(out var none);
        var guild = RoomOf(r => r.GroupType == DungeonRewardDialogController.GuildRaidGroup, "a guild raid room");
        var g = Open(Result((int)guild.Id, true, false, 1, 0, new (byte, int)[] { (4, 77), (5, 66) }, none.b, none.a, none.b));
        Assert.That(Visible(g, "m_rewardGuild"), Is.True);
        Assert.That(Text(g, "m_guildFund"), Is.EqualTo("77"));
        Assert.That(Text(g, "m_guildExp"), Is.EqualTo("66"));
        var big = Open(Result((int)guild.Id, true, false, 1, 0, new (byte, int)[] { (4, 1234567), (5, 76543) }, none.b, none.a, none.b));
        Assert.That(Text(big, "m_guildFund"), Is.EqualTo(NumberText.Group("1234567")), "FUN_1409eb450: digits grouped");
        Assert.That(Text(big, "m_guildFund"), Is.Not.EqualTo("1234567"));
        Assert.That(Text(big, "m_guildExp"), Is.EqualTo(NumberText.Group("76543")));
        var other = RoomOf(r => r.GroupType > 0 && r.GroupType != DungeonRewardDialogController.GuildRaidGroup, "another room");
        var o = Open(Result((int)other.Id, true, false, 1, 0, none.a, none.b, none.a, none.b));
        Assert.That(Visible(o, "m_rewardGuild"), Is.False);
    }

    [Test]
    public void TheBonusJoinsTheTotalsAfterTheTwoTimers()
    {
        var room = RoomOf(r => r.GroupType > 0, "a dungeon room");
        Info0(out var none);
        var e = Open(Result((int)room.Id, true, false, 1, 0, new (byte, int)[] { (1, 100), (2, 10) }, none.b, new (byte, int)[] { (1, 50), (2, 5) }, none.b));
        string before = Text(e, "m_mesoText");
        Assert.That(before, Does.Contain("100"));
        e.C.Tick();
        e.Now += DungeonRewardDialogController.BonusDelayMs - 1;
        e.C.Tick();
        Assert.That(Text(e, "m_mesoText"), Is.EqualTo(before), "the first timer has not run out");
        e.Now += 1;
        e.C.Tick();                                                       // the first timer starts the second
        e.Now += DungeonRewardDialogController.TotalsDelayMs - 1;
        e.C.Tick();
        Assert.That(Text(e, "m_mesoText"), Is.EqualTo(before), "the second timer has not run out");
        e.Now += 1;
        e.C.Tick();
        Assert.That(Text(e, "m_mesoText"), Does.Contain("150"));
        Assert.That(Text(e, "m_expText"), Does.Contain("15"));
        Assert.That(Visible(e, "m_mesoEffect"), Is.True);
    }

    [Test]
    public void AResultWithoutABonusStartsNoTimer()
    {
        var room = RoomOf(r => r.GroupType > 0, "a dungeon room");
        Info0(out var none);
        var e = Open(Result((int)room.Id, true, false, 1, 0, new (byte, int)[] { (1, 100) }, none.b, none.a, none.b));
        string before = Text(e, "m_mesoText");
        e.Now += 10_000;
        e.C.Tick(); e.C.Tick();
        Assert.That(Text(e, "m_mesoText"), Is.EqualTo(before));
        Assert.That(Visible(e, "m_mesoEffect"), Is.False);
    }

    [Test]
    public void TheBagHoldsTheMainItemsAndTheBonusItemsJoinAfterTheTimers()
    {
        var room = RoomOf(r => r.GroupType > 0, "a dungeon room");
        int id = AnItemId();
        Info0(out var none);
        var e = Open(Result((int)room.Id, true, false, 1, 0, none.a, new[] { (id, 2, 1) }, none.a, new[] { (id, 1, 1), (id, 3, 1) }));
        Assert.That(e.C.GetBagSlotBindings(DungeonRewardDialogController.BagId).Count(), Is.EqualTo(1));
        e.Now += DungeonRewardDialogController.BonusDelayMs; e.C.Tick();
        e.Now += DungeonRewardDialogController.TotalsDelayMs; e.C.Tick();
        Assert.That(e.C.GetBagSlotBindings(DungeonRewardDialogController.BagId).Count(), Is.EqualTo(3));
        e.C.Close();
        Assert.That(e.C.GetBagSlotBindings(DungeonRewardDialogController.BagId).Count(), Is.EqualTo(0));
        Assert.That(e.Model.ItemSlots.Keys.Any(k => k.StartsWith(DungeonRewardDialogController.BagId)), Is.False);
    }

    [Test]
    public void TheCloseSendsOnlyForARoomThatLeavesAfterTheReward()
    {
        Info0(out var none);
        var leaves = RoomOf(r => r.GroupType > 0 && r.IsLeaveAfterCloseReward, "a room that leaves after the reward");
        var e = Open(Result((int)leaves.Id, true, false, 1, 0, none.a, none.b, none.a, none.b));
        e.C.OnCloseButton();
        Assert.That(e.Sent.Select(p => p.GetType()), Is.EqualTo(new[] { typeof(DungeonRewardCloseRequest) }));
        var stays = RoomOf(r => r.GroupType > 0 && !r.IsLeaveAfterCloseReward, "a room that stays");
        var s = Open(Result((int)stays.Id, true, false, 1, 0, none.a, none.b, none.a, none.b));
        s.C.OnCloseButton();
        Assert.That(s.Sent, Is.Empty, "the movie's close runs, nothing is sent");
    }

    [Test]
    public void ThePartyLeaveSendsThePartyLeaveThenTheClose()
    {
        Info0(out var none);
        var room = RoomOf(r => r.GroupType > 0, "a dungeon room");
        var e = Open(Result((int)room.Id, true, true, 1, 0, none.a, none.b, none.a, none.b));
        e.C.OnPartyLeaveButton();
        Assert.That(e.Sent.Select(p => p.GetType()), Is.EqualTo(new[] { typeof(PartyLeaveRequest), typeof(DungeonRewardCloseRequest) }));
        var solo = Open(Result((int)room.Id, true, false, 1, 0, none.a, none.b, none.a, none.b));
        solo.C.OnPartyLeaveButton();
        Assert.That(solo.Sent, Is.Empty);
    }

    [Test]
    public void TheTipHasALinePerSetFlagAndNothingForFlagZero()
    {
        Info0(out var none);
        var room = RoomOf(r => r.GroupType > 0, "a dungeon room");
        var none0 = Open(Result((int)room.Id, true, false, 1, 0b0000_0001, none.a, none.b, none.a, none.b), load: false);
        Assert.That(none0.C.Tip, Is.EqualTo(""));
        var two = Open(Result((int)room.Id, true, false, 1, 0b0000_1100, none.a, none.b, none.a, none.b), load: false);
        Assert.That(two.C.Tip.Split('\n').Length, Is.EqualTo(2));
        Assert.That(two.C.Tip, Is.EqualTo(two.C.TipLine(2) + "\n" + two.C.TipLine(3)));
        Assert.That(two.C.TipLine(10), Is.EqualTo(""));
    }

    [Test]
    public void TheCloseButtonOfARoomThatLeavesAfterTheRewardIsRelabeled()
    {
        Info0(out var none);
        var leaves = RoomOf(r => r.GroupType > 0 && r.IsLeaveAfterCloseReward, "a room that leaves after the reward");
        var stays = RoomOf(r => r.GroupType > 0 && !r.IsLeaveAfterCloseReward, "a room that stays");
        string Label(DungeonRoomData r)
        {
            var e = Open(Result((int)r.Id, true, false, 1, 0, none.a, none.b, none.a, none.b));
            return Rt.ToString(e.Host.Get(e.Host.FindNamed("m_closeButton", null), "label"));
        }
        Assert.That(Label(leaves), Is.Not.EqualTo(Label(stays)));
    }

    [Test]
    public void TheWeeklyRewardCountTextIsEmptyWithoutARecordAndCountsWithOne()
    {
        var room = RoomOf(r => r.GroupType > 0 && r.RewardCount > 0 && r.UnionRewardId == 0, "a weekly reward room");
        var book = new DungeonRecordBook { ServerTime = () => 1_000_000, Rooms = Tables().DungeonRooms };
        Assert.That(book.WeeklyRewardCountText(room.Id, "{0}/{1}"), Is.EqualTo(""));
        book.Apply(new RoomDungeonReply(RoomDungeonKind.AddRecord, 6, Array.Empty<DungeonRoomRecord>(),
            new DungeonRoomRecord(room.Id, 0, 0, 0, 0, 0, 0, 0, 0, -1, 0, -1, 0) { Key = room.Id }, 0, 0, 0,
            Array.Empty<ClosingPhotoEntry>(), 0, 0, Array.Empty<DungeonRankRewardEntry>(), null, 0, 0));
        Assert.That(book.WeeklyRewardCountText(room.Id, "{0}/{1}"), Does.Contain(" / "));
    }
}
