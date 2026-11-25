using Maple2.PacketLib.Tools;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The dungeon room records (DungeonRecordManagerCli, dungeonrecordcli.cpp; DungeonRecord, dungeonrecord.cpp): the table the 0x1A packets
/// build and the questions the dungeon room window asks over it. The room rules use the room table (needs MS2_DATA), compared by counts and
/// flags, never by text.</summary>
public class DungeonRecordBookTests
{
    static GameTables tables;

    static GameTables Tables()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        return tables ??= new GameTables(new PackSet(d), "en");
    }

    const long Now = 1_000_000;

    static DungeonRecordBook Book(Action<DungeonRecordBook> setup = null)
    {
        var b = new DungeonRecordBook { ServerTime = () => Now, CharacterId = () => 77 };
        setup?.Invoke(b);
        return b;
    }

    static DungeonRoomRecord Rec(uint room, long unionCooldown = 0, byte unionClears = 0, byte subClears = 0, long subCooldown = 0,
        byte extraSub = 0, byte extra = 0, long clearTime = 0, int total = 0, short lifetime = -1, long cooldown = 0, short current = -1, byte flags = 0) =>
        new(room, unionCooldown, unionClears, subClears, subCooldown, extraSub, extra, clearTime, total, lifetime, cooldown, current, flags) { Key = room };

    /// <summary>A packet body the way the server writes it (mode first), read back through the reader.</summary>
    static RoomDungeonReply Wire(ProtocolProfile profile, Action<BinaryWriter> body)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        body(w);
        return RoomDungeonReply.Read(new ByteReader(ms.ToArray()), profile);
    }

    static void WriteRecord(BinaryWriter w, DungeonRoomRecord r)
    {
        w.Write(r.RoomId); w.Write(r.UnionCooldown); w.Write(r.UnionClears); w.Write(r.UnionSubClears); w.Write(r.UnionSubCooldown);
        w.Write(r.ExtraSubClears); w.Write(r.ExtraClears); w.Write(r.ClearTime); w.Write(r.TotalClears); w.Write(r.LifetimeRecord);
        w.Write(r.Cooldown); w.Write(r.CurrentRecord); w.Write(r.Flags);
    }

    [Test]
    public void RecordTableIsReplacedByRecordsAndAddedToByAddRecord()
    {
        var b = Book();
        var a = Rec(101, flags: 2);
        var c = Rec(102);
        var all = Wire(ProtocolProfile.Kms2525, w => { w.Write((byte) 5); w.Write(2); w.Write(a.RoomId); WriteRecord(w, a); w.Write(c.RoomId); WriteRecord(w, c); });
        Assert.That(b.Apply(all), Is.True);
        Assert.That(b.RecordCount, Is.EqualTo(2));
        Assert.That(b.Peek(101).Flags, Is.EqualTo(2));

        var add = Wire(ProtocolProfile.Kms2525, w => { w.Write((byte) 6); WriteRecord(w, Rec(103, total: 4)); });
        Assert.That(b.Apply(add), Is.True);
        Assert.That(b.RecordCount, Is.EqualTo(3));
        Assert.That(b.Peek(103).TotalClears, Is.EqualTo(4));

        // OnRecords is RemoveAll then the map: a second whole table drops the rest.
        var one = Wire(ProtocolProfile.Kms2525, w => { w.Write((byte) 5); w.Write(1); w.Write(c.RoomId); WriteRecord(w, c); });
        b.Apply(one);
        Assert.That(b.RecordCount, Is.EqualTo(1));
        Assert.That(b.Peek(101), Is.Null, "Peek does not insert");
        Assert.That(b.RecordCount, Is.EqualTo(1));
    }

    [Test]
    public void GetRecordInsertsAZeroedRecordOnAMiss()
    {
        var b = Book();
        var r = b.GetRecord(555);
        Assert.That(b.RecordCount, Is.EqualTo(1));
        Assert.That((r.RoomId, r.TotalClears, r.LifetimeRecord, r.Flags), Is.EqualTo((555u, 0, (short) -1, (byte) 0)));
        Assert.That(b.GetRecord(555), Is.SameAs(r));
    }

    [Test]
    public void RewardsAndBoostAreKeptAndClearDropsThem()
    {
        var b = Book();
        var rewards = Wire(ProtocolProfile.Kms2525, w => { w.Write((byte) 0x13); w.Write(1); w.Write(9u); w.Write(9u); w.Write(5); w.Write(Now + 10); });
        Assert.That(b.Apply(rewards), Is.True);
        Assert.That(b.RankRewardCount, Is.EqualTo(1));
        Assert.That(b.GetRankRewardCount(9), Is.EqualTo(5));
        b.ServerTime = () => Now + 10;
        Assert.That(b.GetRankRewardCount(9), Is.EqualTo(0), "the reward counts while the server time is before its time");
        Assert.That(b.GetRankRewardCount(8), Is.EqualTo(0));

        b.ServerTime = () => Now;
        b.Apply(Wire(ProtocolProfile.Kms2525, w => { w.Write((byte) 0x16); w.Write(Now + 60); w.Write(77L); }));
        Assert.That((b.BoostA, b.BoostB), Is.EqualTo((Now + 60, 77L)));
        Assert.That(b.IsBoostActive(), Is.True);
        b.Clear();
        Assert.That((b.RecordCount, b.RankRewardCount, b.BoostA, b.BoostB), Is.EqualTo((0, 0, 0L, 0L)));
    }

    [Test]
    public void BoostIsActiveOnlyForTheEnteredCharacterBeforeItsEnd()
    {
        long other = 78, end = Now + 60;
        var b = Book(x => { x.Apply(Wire(ProtocolProfile.Kms2525, w => { w.Write((byte) 0x16); w.Write(end); w.Write(other); })); });
        Assert.That(b.IsBoostActive(), Is.False, "another character's boost");
        b.CharacterId = () => other;
        Assert.That(b.IsBoostActive(), Is.True);
        b.ServerTime = () => end;
        Assert.That(b.IsBoostActive(), Is.False, "at the end time");
    }

    [Test]
    public void TimedCountsAreZeroOnceTheirTimeIsReached()
    {
        var r = Rec(1, unionCooldown: Now + 5, unionClears: 3, subClears: 2, subCooldown: Now + 5, extraSub: 4, extra: 6, clearTime: Now + 5);
        Assert.That((DungeonRecordBook.BonusRewardCount(r, Now), DungeonRecordBook.ClearCount(r, Now), DungeonRecordBook.ExtraCount(r, Now), DungeonRecordBook.BonusCount(r, Now)),
            Is.EqualTo((3, 2, 6, 4)));
        long at = Now + 5;
        Assert.That((DungeonRecordBook.BonusRewardCount(r, at), DungeonRecordBook.ClearCount(r, at), DungeonRecordBook.ExtraCount(r, at), DungeonRecordBook.BonusCount(r, at)),
            Is.EqualTo((0, 0, 0, 0)));
    }

    [Test]
    public void TheTimedValueIsReadTheClientsWayFromTheTenBytesAfterTheLifetimeRecord()
    {
        // The client reads a u16 value then an i64 expiry; the wire reader holds the same ten bytes as an i64 cooldown and an i16.
        ushort value = 1234;
        long expire = Now + 100;
        var bytes = new byte[10];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        BitConverter.GetBytes(expire).CopyTo(bytes, 2);
        long cooldown = BitConverter.ToInt64(bytes, 0);
        short current = BitConverter.ToInt16(bytes, 8);
        var r = Rec(1, cooldown: cooldown, current: current);
        Assert.That(DungeonRecordBook.Raw68(r), Is.EqualTo(value));
        Assert.That(DungeonRecordBook.Expire68(r), Is.EqualTo(expire));
        Assert.That(DungeonRecordBook.Get68(r, Now), Is.EqualTo(value));
        Assert.That(DungeonRecordBook.Get68(r, expire), Is.EqualTo(0), "0 once the time reached the expiry");
    }

    [Test]
    public void AnUnsetTimedValueFromTheRecordedPacketIsZero()
    {
        // gms2 recording: the ten bytes are 00.. FFFF as written for an unset record (cooldown 0, current record -1).
        var r = Rec(1, cooldown: 0, current: -1);
        Assert.That(DungeonRecordBook.Get68(r, Now), Is.EqualTo(0));
        var b = Book(x => x.Apply(Wire(ProtocolProfile.Gms2, w => { w.Write((byte) 6); WriteRecord(w, r); })));
        Assert.That(b.GetRoomScore68(1), Is.EqualTo(0));
        Assert.That(b.GetRoomScore60(1), Is.EqualTo(-1));
        Assert.That(b.GetScoredRecords(), Is.Empty);
    }

    [Test]
    public void FlagBitsAreTheClientsBits()
    {
        var b = Book(x => x.Apply(Wire(ProtocolProfile.Kms2525, w => { w.Write((byte) 6); WriteRecord(w, Rec(7, flags: 3)); })));
        Assert.That(DungeonRecordBook.TestFlag(b.Peek(7), 0), Is.True);
        Assert.That(b.IsSeasonRoom(7), Is.True);
        Assert.That(DungeonRecordBook.TestFlag(b.Peek(7), 2), Is.False);
        Assert.That(DungeonRecordBook.TestFlag(b.Peek(7), 8), Is.False);
        Assert.That(b.IsSeasonRoom(8), Is.False);
    }

    [Test]
    public void TheRoomRulesFollowTheRoomTable()
    {
        var t = Tables();
        var room = t.DungeonRooms.GetRoomsForLevel(120, 100000).FirstOrDefault(r => r.RewardCount > 0 && r.SubRewardCount == 0);
        if (room == null) Assert.Ignore("the room table has no room with a reward count and no sub rewards");
        int max = (int) room.RewardCount;
        var b = Book(x => x.Rooms = t.DungeonRooms);

        // No record: a zeroed one counts nothing, so there are clears left (0 < max).
        Assert.That(b.CanEnter(room.Id), Is.True);
        Assert.That(b.HasProgress(room.Id), Is.False);

        // Clears reach the maximum: no count left.
        b.Apply(Wire(ProtocolProfile.Kms2525, w => { w.Write((byte) 6); WriteRecord(w, Rec(room.Id, unionCooldown: Now + 100, unionClears: (byte) max)); }));
        Assert.That(b.MaxCount(b.Peek(room.Id)), Is.EqualTo(max));
        Assert.That(b.CanEnter(room.Id), Is.False);
        Assert.That(b.HasProgress(room.Id), Is.True);

        // The bonus count raises the maximum while the record has not expired.
        b.Apply(Wire(ProtocolProfile.Kms2525, w => { w.Write((byte) 6); WriteRecord(w, Rec(room.Id, unionCooldown: Now + 100, unionClears: (byte) max, extraSub: 2)); }));
        Assert.That(b.MaxCount(b.Peek(room.Id)), Is.EqualTo(max + 2));
        Assert.That(b.GetRoomBonusCount(room.Id), Is.EqualTo(2));
        Assert.That(b.CanEnter(room.Id), Is.True);

        // Once expired the clears and the bonus are gone.
        b.ServerTime = () => Now + 100;
        Assert.That(b.MaxCount(b.Peek(room.Id)), Is.EqualTo(max));
        Assert.That(b.GetRoomBonusCount(room.Id), Is.EqualTo(0));
        Assert.That(b.CanEnter(room.Id), Is.True);
    }

    [Test]
    public void AnExtraRewardEventKeepsTheRoomOpenUntilItsCountIsMade()
    {
        var t = Tables();
        var room = t.DungeonRooms.GetRoomsForLevel(120, 100000).FirstOrDefault(r => r.RewardCount > 0 && r.SubRewardCount == 0);
        if (room == null) Assert.Ignore("the room table has no room with a reward count and no sub rewards");
        var b = Book(x => { x.Rooms = t.DungeonRooms; x.ExtraRewardEventCount = id => id == room.Id ? 3u : null; });
        b.Apply(Wire(ProtocolProfile.Kms2525, w => { w.Write((byte) 6); WriteRecord(w, Rec(room.Id, unionCooldown: Now + 100, unionClears: (byte) room.RewardCount, extra: 2, clearTime: Now + 100)); }));
        Assert.That(b.IsExtraExhausted(b.Peek(room.Id)), Is.True, "2 of 3 extra clears: still has extra clears to make");
        Assert.That(b.CanEnter(room.Id), Is.True);
        b.Apply(Wire(ProtocolProfile.Kms2525, w => { w.Write((byte) 6); WriteRecord(w, Rec(room.Id, unionCooldown: Now + 100, unionClears: (byte) room.RewardCount, extra: 3, clearTime: Now + 100)); }));
        Assert.That(b.IsExtraExhausted(b.Peek(room.Id)), Is.False);
        Assert.That(b.CanEnter(room.Id), Is.False);
    }

    [Test]
    public void SubRewardRoomsStopAtTheirSubClears()
    {
        var t = Tables();
        var room = t.DungeonRooms.GetRoomsForLevel(120, 100000).FirstOrDefault(r => r.SubRewardCount > 0 && r.SubRewardCount < 200);
        if (room == null) Assert.Ignore("the room table has no sub reward room");
        var b = Book(x => x.Rooms = t.DungeonRooms);
        b.Apply(Wire(ProtocolProfile.Kms2525, w => { w.Write((byte) 6); WriteRecord(w, Rec(room.Id, subClears: (byte) room.SubRewardCount, subCooldown: Now + 100)); }));
        Assert.That(b.CanEnter(room.Id), Is.False);
        b.ServerTime = () => Now + 100;
        Assert.That(b.CanEnter(room.Id), Is.True, "the sub clears reset with their cooldown");
    }

    [Test]
    public void HelperRewardNeedsTheClearCountAndTheFlag()
    {
        var t = Tables();
        var room = t.DungeonRooms.GetRoomsForLevel(120, 100000).FirstOrDefault(r => r.DungeonHelperRequireClearCount > 0 && r.DungeonHelperRequireClearCount < 1000);
        if (room == null) Assert.Ignore("the room table has no helper reward room");
        int need = (int) room.DungeonHelperRequireClearCount;
        var b = Book(x => x.Rooms = t.DungeonRooms);
        Assert.That(b.CanClaimHelperReward(room.Id), Is.False, "no record");
        b.Apply(Wire(ProtocolProfile.Kms2525, w => { w.Write((byte) 6); WriteRecord(w, Rec(room.Id, total: need, flags: 0)); }));
        Assert.That(b.CanClaimHelperReward(room.Id), Is.False, "no flag");
        b.Apply(Wire(ProtocolProfile.Kms2525, w => { w.Write((byte) 6); WriteRecord(w, Rec(room.Id, total: need - 1, flags: 1)); }));
        Assert.That(b.CanClaimHelperReward(room.Id), Is.False, "too few clears");
        b.Apply(Wire(ProtocolProfile.Kms2525, w => { w.Write((byte) 6); WriteRecord(w, Rec(room.Id, total: need, flags: 1)); }));
        Assert.That(b.CanClaimHelperReward(room.Id), Is.True);
    }

    [Test]
    public void ARecordWithoutARoomRowHasNoMaximum()
    {
        var t = Tables();
        var b = Book(x => x.Rooms = t.DungeonRooms);
        var r = b.GetRecord(uint.MaxValue - 5);
        Assert.That(b.MaxCount(r), Is.EqualTo(0));
        Assert.That(b.CanEnter(r.RoomId), Is.False, "0 < 0 is false");
        Assert.That(b.GetExtraTicketCost(null), Is.EqualTo(0u));
        Assert.That(b.GetSeasonRooms(), Is.Empty);
    }

    [Test]
    public void SeasonRoomsAreTheFlaggedRecordsThatHaveARoom()
    {
        var t = Tables();
        var room = t.DungeonRooms.GetRoomsForLevel(120, 100000).FirstOrDefault();
        if (room == null) Assert.Ignore("the room table has no rooms");
        var b = Book(x => x.Rooms = t.DungeonRooms);
        b.Apply(Wire(ProtocolProfile.Kms2525, w => { w.Write((byte) 5); w.Write(2); w.Write(room.Id); WriteRecord(w, Rec(room.Id, flags: 2)); w.Write(uint.MaxValue - 5); WriteRecord(w, Rec(uint.MaxValue - 5, flags: 2)); }));
        Assert.That(b.GetSeasonRooms().Select(r => r.Id), Is.EqualTo(new[] { room.Id }));
    }

    // ---- outbound packets of the room panel (uidungeonroomdialog.cpp) ----

    static byte[] Bytes(IOutPacket p)
    {
        var w = new ByteWriter();
        p.Write(w);
        return w.ToArray();
    }

    [Test]
    public void RoomPanelRequestsWriteTheClientsFields()
    {
        Assert.That(Bytes(new RoomDungeonEntryRequest(0x01020304, true, 0x0A0B0C0D, true)), Is.EqualTo(new byte[] { 2, 4, 3, 2, 1, 1, 0x0D, 0x0C, 0x0B, 0x0A, 1 }));
        Assert.That(Bytes(new RoomDungeonEntryRequest(5, false, 6, false)), Is.EqualTo(new byte[] { 2, 5, 0, 0, 0, 0, 6, 0, 0, 0, 0 }));
        Assert.That(Bytes(new RoomDungeonUseExtraRewardRequest(5)), Is.EqualTo(new byte[] { 8, 5, 0, 0, 0 }));
        Assert.That(Bytes(new RoomDungeonFindHelperRequest(5)), Is.EqualTo(new byte[] { 0xf, 5, 0, 0, 0 }));
        Assert.That(Bytes(new RoomDungeonFindMemberRequest(5)), Is.EqualTo(new byte[] { 0x10, 5, 0, 0, 0 }));
        Assert.That(Bytes(new RoomDungeonChangeExpertRequest(5)), Is.EqualTo(new byte[] { 0x11, 5, 0, 0, 0 }));
        Assert.That(Bytes(new RoomDungeonLeaveRequest()), Is.EqualTo(new byte[] { 1 }));
        Assert.That(Bytes(new RoomDungeonFavoriteRequest(5, true)), Is.EqualTo(new byte[] { 0x18, 5, 0, 0, 0, 1 }));
        Assert.That(Bytes(new PartyDungeonSearchStartRequest(5)), Is.EqualTo(new byte[] { 0x21, 5, 0, 0, 0 }));
        Assert.That(Bytes(new PartyDungeonSearchCancelRequest(3)), Is.EqualTo(new byte[] { 0x22, 3 }));
    }

    [Test]
    public void RoomPanelRequestsGoOutOnTheRoomDungeonAndPartyOpcodes()
    {
        Assert.That(new RoomDungeonEntryRequest(1, false, 0, false).Op, Is.EqualTo(Outbound.RoomDungeon));
        Assert.That(new RoomDungeonLeaveRequest().Op, Is.EqualTo(Outbound.RoomDungeon));
        Assert.That(new PartyDungeonSearchStartRequest(1).Op, Is.EqualTo(Outbound.Party));
        Assert.That(new PartyDungeonSearchCancelRequest(1).Op, Is.EqualTo(Outbound.Party));
    }
}
