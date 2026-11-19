using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// The tracker's routing of the live field's stat and battle packets: the player form of OnStat (the npc form keeps
/// its NpcHpEvent route) and OnUserBattle, which the live head bars hang on.
/// </summary>
public class FieldTrackerTests {
    static byte[] Bytes(ByteWriter w) => w.Buffer.AsSpan(0, w.Length).ToArray();
    static RawPacket Packet(Inbound op, byte[] body) => new((ushort) op, body);

    [Test]
    public void PlayerStatRoutesToUserStat() {
        var w = new ByteWriter();
        w.WriteInt(77); w.WriteByte(0); w.WriteByte(2);
        w.WriteByte(4); w.WriteLong(1000); w.WriteLong(900); w.WriteLong(650);   // health: total, base, current
        w.WriteByte(7); w.WriteInt(100); w.WriteInt(100); w.WriteInt(40);
        var t = new FieldTracker();
        t.Inbound(Packet(Inbound.Stat, Bytes(w)));
        var e = t.Events.Single() as UserStatEvent;
        Assert.That(e, Is.Not.Null);
        Assert.That(e.Stat.ObjectId, Is.EqualTo(77));
        Assert.That(e.Stat.TryGet(PlayerStat.Health, out long max, out long cur), Is.True);
        Assert.That((max, cur), Is.EqualTo((1000L, 650L)));
        Assert.That(t.PlayerStatDrops, Is.EqualTo(0));
    }

    [Test]
    public void PlayerStatInitFormRoutesToUserStat() {
        var w = new ByteWriter();
        w.WriteInt(5); w.WriteByte(0); w.WriteByte(35);
        for (int a = 0; a < 35; a++) {
            if (a == 4) { w.WriteLong(500); w.WriteLong(500); w.WriteLong(480); }
            else { w.WriteInt(a); w.WriteInt(a); w.WriteInt(a + 1); }
        }
        var t = new FieldTracker();
        t.Inbound(Packet(Inbound.Stat, Bytes(w)));
        var e = t.Events.Single() as UserStatEvent;
        Assert.That(e, Is.Not.Null);
        Assert.That(e.Stat.Values.Count, Is.EqualTo(35));
        Assert.That(e.Stat.TryGet(PlayerStat.Health, out long max, out long cur), Is.True);
        Assert.That((max, cur), Is.EqualTo((500L, 480L)));
    }

    [Test]
    public void ServerUpdateEntityFormIsDropped() {
        // the kms2525 StatsPacket.Update(entity) broadcast: the same 0x23 count as the client's Init form but another
        // body, which PCBase::vfunc_90 does not decode — rejected and counted
        var w = new ByteWriter();
        w.WriteInt(77); w.WriteByte(0); w.WriteByte(35);
        for (int i = 0; i < 3; i++) { w.WriteLong(500); w.WriteInt(1); w.WriteInt(2); w.WriteInt(3); w.WriteInt(4); }
        var t = new FieldTracker();
        t.Inbound(Packet(Inbound.Stat, Bytes(w)));
        Assert.That(t.Events, Is.Empty);
        Assert.That(t.PlayerStatDrops, Is.EqualTo(1));
    }

    [Test]
    public void UserBattleRoutesToTheStanceEvent() {
        var w = new ByteWriter();
        w.WriteInt(88); w.WriteByte(1);
        var t = new FieldTracker();
        t.Inbound(Packet(Inbound.UserBattle, Bytes(w)));
        Assert.That(t.Events.Single(), Is.EqualTo(new UserBattleEvent(0, 88, 1)));
    }
}
