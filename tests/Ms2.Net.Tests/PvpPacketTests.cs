using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// The Pvp packet (CSceneStateGame::OnPvp @0x141732350, scenestategame.cpp:4986) and PlayerKillNotice
/// (OnPlayerKillNotice @0x141733850, scenestategame.cpp:4344).
/// </summary>
public class PvpPacketTests {
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    [Test]
    public void TheOpsAreTheClientsRecvOps() {
        Assert.That((ushort)Inbound.Pvp, Is.EqualTo(0xB6));
        Assert.That((ushort)Inbound.PlayerKillNotice, Is.EqualTo(0xA3));
        Assert.That(RcLane.Gms2Inbound(Inbound.Pvp), Is.EqualTo(0xB8));
        Assert.That(RcLane.Gms2Inbound(Inbound.PlayerKillNotice), Is.EqualTo(0xA5));
    }

    [Test]
    public void ModeDReadsTheObjectThePointTheTeamAndTheMode() {
        var m = PvpMessage.Read(R(w => { w.WriteByte((byte)'d'); w.WriteInt(77); w.WriteInt(102); w.WriteByte(3); w.WriteByte(10); }));
        Assert.That(m, Is.EqualTo(new PvpPointInfo(77, 102, 3, 10)));
    }

    [Test]
    public void ModeEReadsTheWinAndModeFNothing() {
        Assert.That(PvpMessage.Read(R(w => { w.WriteByte((byte)'e'); w.WriteBool(true); })), Is.EqualTo(new PvpWinLose(true)));
        Assert.That(PvpMessage.Read(R(w => w.WriteByte((byte)'f'))), Is.EqualTo(new PvpClear()));
    }

    [Test]
    public void AnUnportedModeLeavesItsBodyUnread() =>
        Assert.That(PvpMessage.Read(R(w => { w.WriteByte(9); w.WriteInt(5); })), Is.EqualTo(new PvpOther(9)));

    [Test]
    public void ALongerPointBodyIsRefused() =>
        Assert.That(() => PvpMessage.Read(R(w => { w.WriteByte((byte)'d'); w.WriteInt(1); w.WriteInt(0); w.WriteByte(0); w.WriteByte(0); w.WriteByte(1); })), Throws.Exception);

    [Test]
    public void KillModeZeroReadsTheKillerAndTheVictim() {
        var m = PlayerKillNotice.Read(R(w => { w.WriteByte(0); w.WriteUnicodeString("Ann"); w.WriteUnicodeString("Bob"); }));
        Assert.That((m.Mode, m.Killer, m.Victim, m.Text), Is.EqualTo(((byte)0, "Ann", "Bob", (StringIdText)null)));
    }

    [Test]
    public void KillModeOneReadsAStringIdText() {
        var m = PlayerKillNotice.Read(R(w => { w.WriteByte(1); w.WriteBool(true); w.WriteInt(4); w.WriteInt(0x2a); w.WriteInt(1); w.WriteUnicodeString("Ann"); }));
        Assert.That(m.Mode, Is.EqualTo(1));
        Assert.That((m.Text.Formatted, m.Text.Kind, m.Text.Id, m.Text.Args), Is.EqualTo((true, 4, 0x2a, new[] { "Ann" })));
    }

    [Test]
    public void AnotherKillModeReadsOnlyTheMode() {
        var m = PlayerKillNotice.Read(R(w => { w.WriteByte(2); w.WriteInt(9); }));
        Assert.That((m.Mode, m.Text), Is.EqualTo(((byte)2, (StringIdText)null)));
    }
}
