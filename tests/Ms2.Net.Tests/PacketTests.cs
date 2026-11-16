using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class PacketTests {
    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    [Test] public void ResponseLoginRoundTrips() {
        var a = new ResponseLogin(2, "user", "pass", 0, Enumerable.Range(0, 16).Select(i => (byte) i).ToArray(), 1, 2, "en");
        var back = ResponseLogin.Read(new ByteReader(Bytes(a)));
        Assert.That(back.User, Is.EqualTo("user")); Assert.That(back.Language, Is.EqualTo("en")); Assert.That(back.MachineId, Is.EqualTo(a.MachineId));
        var b = new ResponseLoginB(1, "u", "p", 0, 5, new byte[16], 6, 7);
        Assert.That(ResponseLoginB.Read(new ByteReader(Bytes(b))).Value, Is.EqualTo(5));
    }

    [Test] public void SelectCharacterLayout() {
        var bytes = Bytes(new SelectCharacter(0x0102030405060708));
        Assert.That(bytes, Is.EqualTo(new byte[] { 0, 8, 7, 6, 5, 4, 3, 2, 1 }));
        Assert.That(SelectCharacter.Read(new ByteReader(bytes)).CharacterId, Is.EqualTo(0x0102030405060708));
    }

    [Test] public void UserSyncOutRoundTripsWithOptionalFields() {
        var rec = new StateSync { State = 1, SubState = 2, Position = new Vector3S(10, -20, 30), Rotation = 90, Animation = 0x81, Float1 = 1.5f, Float2 = 2.5f,
            Speed = new Vector3S(1, 2, 3), Unknown1 = 2, Rotation2 = 900, Flags = StateSync.Flag.Animation | StateSync.Flag.Flag3 | StateSync.Flag.Flag5,
            Flag3Value = 7, AnimationName = "Run_A", Flag5Vec = new Vector3(1, 2, 3), Flag5Text = "x", SyncNumber = 42 };
        var o = new UserSyncOut { SyncNumber = 3, ServerTick = 100, ClientTick = 200 };
        o.Records.Add((rec, 201));
        var back = UserSyncOut.Read(new ByteReader(Bytes(o)));
        Assert.That(back.Records.Count, Is.EqualTo(1));
        var r = back.Records[0];
        Assert.That(r.Tick, Is.EqualTo(201));
        Assert.That(r.Record.Position, Is.EqualTo(rec.Position)); Assert.That(r.Record.AnimationName, Is.EqualTo("Run_A"));
        Assert.That(r.Record.Float2, Is.EqualTo(2.5f)); Assert.That(r.Record.Flag5Vec, Is.EqualTo(rec.Flag5Vec)); Assert.That(r.Record.SyncNumber, Is.EqualTo(42));
    }

    [Test] public void LoginToGameOkAndError() {
        var w = new ByteWriter();
        w.WriteByte(0); w.WriteBytes(new byte[] { 127, 0, 0, 1 }); w.WriteShort(20001); w.WriteLong(-5); w.WriteInt(2000062);
        var p = LoginToGame.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()));
        Assert.That(p.Ok); Assert.That(p.Address.ToString(), Is.EqualTo("127.0.0.1")); Assert.That(p.Port, Is.EqualTo(20001)); Assert.That(p.MapId, Is.EqualTo(2000062));
        Assert.That(p.Token, Is.EqualTo(ulong.MaxValue - 4));
    }

    [Test] public void TrailingBytesAreAnError() {
        var w = new ByteWriter(); w.WriteInt(1); w.WriteByte(0);
        Assert.Throws<PacketLayoutException>(() => TickRequest.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray())));
    }
}
