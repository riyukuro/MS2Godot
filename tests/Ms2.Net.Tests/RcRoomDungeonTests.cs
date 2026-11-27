using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The dungeon room records (0x1A) are read by RoomDungeonReply (DungeonRecordManagerCli::OnPacket, dungeonrecordcli.cpp:203).
/// Layouts: the GMS2 packet layout notes. The GMS2 error and open-dialog numbers are in no recording.</summary>
public class RcRoomDungeonTests {
    static RoomDungeonReply Gms2(string hex, out ByteReader r) {
        r = new ByteReader(RcPkt.Hex(hex));
        return RoomDungeonReply.Read(r, ProtocolProfile.Gms2);
    }

    // gms2 recording: mode 6, one record (49 bytes)
    const string AddRecord = "06EE0300008066AB5E00000000090200CFA45E000000000000CB2FA25E0000000000000000FFFF0000000000000000FFFF00";

    [Test] public void AddedRecordIsReadInTheRecordedOrder() {
        var m = Gms2(AddRecord, out var r);
        Assert.That(m.Kind, Is.EqualTo(RoomDungeonKind.AddRecord));
        var rec = m.Record;
        Assert.That(rec.RoomId, Is.EqualTo(0x3EEu));
        Assert.That((rec.UnionCooldown, rec.UnionClears, rec.UnionSubClears), Is.EqualTo((0x5EAB6680L, (byte) 9, (byte) 2)));
        Assert.That(rec.UnionSubCooldown, Is.EqualTo(0x5EA4CF00L));
        Assert.That((rec.ExtraSubClears, rec.ExtraClears, rec.ClearTime), Is.EqualTo(((byte) 0, (byte) 0, 0x5EA22FCBL)));
        Assert.That((rec.TotalClears, rec.LifetimeRecord, rec.Cooldown, rec.CurrentRecord, rec.Flags), Is.EqualTo((0, (short) -1, 0L, (short) -1, (byte) 0)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void NoRecordsAnswerIsEmpty() {
        var m = Gms2("0500000000", out var r);
        Assert.That(m.Kind, Is.EqualTo(RoomDungeonKind.Records));
        Assert.That(m.Records, Is.Empty);
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void RankRewardAddIsReadAndKeepsItsOwnId() {
        // gms2 recording: mode 0x15 (17 bytes)
        var m = Gms2("15" + "19270000" + "05000000" + "807C865E00000000", out var r);
        Assert.That(m.Kind, Is.EqualTo(RoomDungeonKind.AddReward));
        Assert.That((m.Reward.Id, m.Reward.Value, m.Reward.Time), Is.EqualTo((0x2719u, 5, 0x5E867C80L)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void EmptyRewardTableIsRead() {
        var m = Gms2("1400000000", out var r);
        Assert.That(m.Kind, Is.EqualTo(RoomDungeonKind.Rewards));
        Assert.That(m.Rewards, Is.Empty);
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void ClosingPhotoLineIsRead() {
        // gms2 recording: mode 0x0B, one member
        var m = Gms2("0B0101000000770854CCC618182CFFFFFFFF080000000B000000", out var r);
        Assert.That(m.Kind, Is.EqualTo(RoomDungeonKind.ClosingPhoto));
        Assert.That(m.Result, Is.EqualTo((byte) 1));
        Assert.That(m.Closing, Is.EqualTo(new[] { new ClosingPhotoEntry(0x2C1818C6CC540877L, -1, 8, 11) }));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void RewardNoticeIsKindAndValue() {
        var m = Gms2("0701" + "00000000", out var r);
        Assert.That((m.Kind, m.NoticeKind, m.NoticeValue), Is.EqualTo((RoomDungeonKind.RewardNotice, (byte) 1, 0)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void BoostStateIsTwoLongs() {
        var m = Gms2("17" + "0100000000000000" + "0200000000000000", out var r);
        Assert.That((m.Kind, m.BoostA, m.BoostB), Is.EqualTo((RoomDungeonKind.BoostState, 1L, 2L)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void AModeTheProfileDoesNotNameReadsNothingMore() {
        var m = Gms2("7F0102", out var r);
        Assert.That(m.Kind, Is.EqualTo(RoomDungeonKind.Unknown));
        Assert.That(r.Available, Is.EqualTo(2));
    }

    [Test] public void ACountPastTheBodyIsRefused() {
        var w = new ByteWriter();
        w.WriteByte(5); w.WriteInt(1000); w.WriteInt(1);
        Assert.Throws<InvalidDataException>(() => RoomDungeonReply.Read(new ByteReader(w.ToArray()), ProtocolProfile.Gms2));
    }

    [Test] public void KmsModesKeepTheirNumbers() {
        foreach (var (mode, kind) in new[] {
            ((byte) 5, RoomDungeonKind.Records), ((byte) 6, RoomDungeonKind.AddRecord), ((byte) 7, RoomDungeonKind.RewardNotice),
            ((byte) 10, RoomDungeonKind.ClosingPhoto), ((byte) 11, RoomDungeonKind.OpenDialog), ((byte) 0x12, RoomDungeonKind.Error),
            ((byte) 0x13, RoomDungeonKind.Rewards), ((byte) 0x14, RoomDungeonKind.AddReward), ((byte) 0x16, RoomDungeonKind.BoostState) })
            Assert.That(RoomDungeonReply.KindOf(mode, ProtocolProfile.Kms2525), Is.EqualTo(kind), $"mode {mode}");
    }

    [Test] public void KmsErrorCarriesErrorAndArgument() {
        var w = new ByteWriter();
        w.WriteByte(0x12); w.WriteInt(0x20); w.WriteInt(7);
        var r = new ByteReader(w.ToArray());
        var m = RoomDungeonReply.Read(r, ProtocolProfile.Kms2525);
        Assert.That((m.Kind, m.Error, m.ErrorArg), Is.EqualTo((RoomDungeonKind.Error, 0x20, 7)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void KmsOpenDialogHasNoBody() {
        var r = new ByteReader(new byte[] { 11 });
        Assert.That(RoomDungeonReply.Read(r, ProtocolProfile.Kms2525).Kind, Is.EqualTo(RoomDungeonKind.OpenDialog));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void EveryRecordedGms2PacketOfAKnownModeReadsToTheEnd() {
        var all = Gms2Sniffs.Inbound("gms2", ProtocolProfile.Gms2.Number(Inbound.RoomDungeon));
        Assert.That(all, Is.Not.Empty);
        int seen = 0;
        foreach (var (file, data) in all) {
            var r = new ByteReader(data);
            var m = RoomDungeonReply.Read(r, ProtocolProfile.Gms2);
            if (m.Kind == RoomDungeonKind.Unknown) continue;
            seen++;
            Assert.That(r.Available, Is.EqualTo(0), file);
        }
        Assert.That(seen, Is.GreaterThan(0));
    }

    [Test] public void NumberOnBothProfiles() {
        Assert.That(ProtocolProfile.Kms2525.Number(Inbound.RoomDungeon), Is.EqualTo((ushort) 0x1A));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.RoomDungeon), Is.EqualTo((ushort) 0x1A));
    }

    static RoomDungeonReply Kms(params byte[] body) => RoomDungeonReply.Read(new ByteReader(body), ProtocolProfile.Kms2525);

    // UIDungeonControlCenter::OnDungeonRewardPacket (@0x140c0cf20) compares the raw first byte to 0x17, which the record manager does not read.
    [Test] public void TheKmsWeeklyRefreshByteReachesTheControlCenterAsItIs() {
        var m = Kms(0x17);
        Assert.That(m.Kind, Is.EqualTo(RoomDungeonKind.Unknown));
        Assert.That(m.ClientCommand(ProtocolProfile.Kms2525), Is.EqualTo(RoomDungeonReply.WeeklyRefreshMode));
    }

    [Test] public void TheGms2ByteSeventeenIsTheBoostStateAndRefreshesNothing() {
        var m = RoomDungeonReply.Read(new ByteReader(new byte[] { 0x17, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }), ProtocolProfile.Gms2);
        Assert.That(m.Kind, Is.EqualTo(RoomDungeonKind.BoostState));
        Assert.That(m.ClientCommand(ProtocolProfile.Gms2), Is.EqualTo(0));
    }

    [Test] public void TheMappedModesKeepTheirCommandNumbers() {
        Assert.That(Gms2(AddRecord, out _).ClientCommand(ProtocolProfile.Gms2), Is.EqualTo(6));
        Assert.That(Kms(7, 1, 0, 0, 0, 0).ClientCommand(ProtocolProfile.Kms2525), Is.EqualTo(7));
        Assert.That(Kms(new byte[17].Select((b, i) => i == 0 ? (byte)0x16 : b).ToArray()).ClientCommand(ProtocolProfile.Kms2525), Is.EqualTo(0), "the boost state raises no command");
    }
}
