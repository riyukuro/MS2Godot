using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The DungeonReward packet (UIDungeonRewardDialog::OnPacket @0x140e22130, uidungeonrewarddialog.cpp:386) and the close request.</summary>
public class DungeonRewardPacketTests {
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    static void Info(ByteWriter w, (byte key, int value)[] rewards, (int id, int count, int rank, bool a, bool b, bool c)[] items) {
        w.WriteInt(rewards.Length);
        foreach (var (key, value) in rewards) { w.WriteByte(key); w.WriteInt(value); }
        w.WriteInt(items.Length);
        foreach (var (id, count, rank, a, b, c) in items) { w.WriteInt(id); w.WriteInt(count); w.WriteInt(rank); w.WriteBool(a); w.WriteBool(b); w.WriteBool(c); }
    }

    [Test]
    public void TheOpIsTheClientsRecvOpOnBothProfiles() {
        Assert.That((ushort)Inbound.DungeonReward, Is.EqualTo(0x1B));
        Assert.That(RcLane.Gms2Inbound(Inbound.DungeonReward), Is.EqualTo((ushort)0x1B));
    }

    [Test]
    public void TheCloseRequestIsTheSendOpWithByteSix() {
        var w = new ByteWriter();
        var close = new DungeonRewardCloseRequest();
        close.Write(w);
        Assert.That(close.Op, Is.EqualTo(Outbound.DungeonReward));
        Assert.That(w.Buffer.AsSpan(0, w.Length).ToArray(), Is.EqualTo(new byte[] { 6 }));
    }

    [Test]
    public void ModeZeroIsTheResult() {
        var p = DungeonRewardPacket.Read(R(w => {
            w.WriteByte(0); w.WriteBool(true); w.WriteInt(21001001); w.WriteBool(true); w.WriteInt(95); w.WriteInt(1200); w.WriteInt(3400);
            w.WriteByte(0x05); w.WriteByte(0x01); w.WriteByte(0); w.WriteByte(0x80);
            Info(w, new (byte, int)[] { (1, 500), (2, 7000) }, new[] { (20000001, 3, 2, true, false, true) });
            Info(w, new (byte, int)[] { (1, 50) }, new (int, int, int, bool, bool, bool)[0]);
        }));
        Assert.That(p.Mode, Is.EqualTo(0));
        Assert.That(p.Win, Is.True);
        Assert.That(p.RoomId, Is.EqualTo(21001001));
        Assert.That(p.Party, Is.True);
        Assert.That((p.Seconds, p.BestScore, p.RunScore), Is.EqualTo((95, 1200, 3400)));
        Assert.That(p.Main.GetMeso(), Is.EqualTo(500));
        Assert.That(p.Main.GetExp(), Is.EqualTo(7000));
        Assert.That(p.Main.Items, Is.EqualTo(new[] { new DungeonRewardItem(20000001, 3, 2, true, false, true) }));
        Assert.That(p.Bonus.GetMeso(), Is.EqualTo(50));
        Assert.That(p.Bonus.Items, Is.Empty);
    }

    [Test]
    public void TheBitsAreByteAndBitPositions() {
        var p = DungeonRewardPacket.Read(R(w => {
            w.WriteByte(0); w.WriteBool(false); w.WriteInt(1); w.WriteBool(false); w.WriteInt(0); w.WriteInt(0); w.WriteInt(0);
            w.WriteByte(0x05); w.WriteByte(0x01); w.WriteByte(0); w.WriteByte(0x80);
            Info(w, new (byte, int)[0], new (int, int, int, bool, bool, bool)[0]);
            Info(w, new (byte, int)[0], new (int, int, int, bool, bool, bool)[0]);
        }));
        var set = new List<int>();
        for (int i = 0; i < 40; i++) if (p.IsBitSet(i)) set.Add(i);
        Assert.That(set, Is.EqualTo(new[] { 0, 2, 8, 31 }));
    }

    [Test]
    public void ModeOneIsARound() {
        var p = DungeonRewardPacket.Read(R(w => {
            w.WriteByte(1); w.WriteInt(4); w.WriteInt(2);
            Info(w, new (byte, int)[] { (2, 900) }, new (int, int, int, bool, bool, bool)[0]);
        }));
        Assert.That((p.Round, p.Stage), Is.EqualTo((4, 2)));
        Assert.That(p.Main.GetExp(), Is.EqualTo(900));
    }

    [Test]
    public void ModeTwoIsOnlyTheInfo() {
        var p = DungeonRewardPacket.Read(R(w => {
            w.WriteByte(2);
            Info(w, new (byte, int)[0], new[] { (30000001, 1, 0, false, false, false) });
        }));
        Assert.That(p.Main.Items.Count, Is.EqualTo(1));
        Assert.That(p.RoomId, Is.EqualTo(0));
    }

    [Test]
    public void ModeThreeIsTheWinAndTheInfo() {
        var p = DungeonRewardPacket.Read(R(w => {
            w.WriteByte(3); w.WriteBool(true);
            Info(w, new (byte, int)[] { (1, 11) }, new (int, int, int, bool, bool, bool)[0]);
        }));
        Assert.That(p.Win, Is.True);
        Assert.That(p.Main.GetMeso(), Is.EqualTo(11));
    }

    [Test]
    public void AnotherModeReadsNothing() {
        var p = DungeonRewardPacket.Read(R(w => { w.WriteByte(9); w.WriteInt(123); }));
        Assert.That(p.Mode, Is.EqualTo(9));
        Assert.That(p.Main.HasRewards, Is.False);
        Assert.That(p.RoomId, Is.EqualTo(0));
    }

    [Test]
    public void ALaterEntryOfAKeyReplacesTheEarlierAndTheValueIsUnsigned() {
        var info = DungeonRewardInfo.Read(R(w => Info(w, new (byte, int)[] { (1, 5), (1, -1) }, new (int, int, int, bool, bool, bool)[0])));
        Assert.That(info.Rewards.Count, Is.EqualTo(1));
        Assert.That(info.GetMeso(), Is.EqualTo(uint.MaxValue));
        Assert.That(info.GetReward(7), Is.EqualTo(0));
        Assert.That(info.HasRewards, Is.True);
        Assert.That(new DungeonRewardInfo().HasRewards, Is.False);
    }
}
