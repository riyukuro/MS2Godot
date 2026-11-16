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
        var bytes = Bytes(new SelectCharacter(0x0102030405060708, 3));
        Assert.That(bytes, Is.EqualTo(new byte[] { 0, 8, 7, 6, 5, 4, 3, 2, 1, 3, 0 }));
        Assert.That(SelectCharacter.Read(new ByteReader(bytes)), Is.EqualTo(new SelectCharacter(0x0102030405060708, 3)));
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

    [Test] public void ManagementCommandsRoundTrip() {
        Assert.That(ManageCharacter.Read(new ByteReader(Bytes(new ManageCharacter(CharacterIdCommand.ConfirmDelete, 9)))), Is.EqualTo(new ManageCharacter(CharacterIdCommand.ConfirmDelete, 9)));
        Assert.That(RenameCharacter.Read(new ByteReader(Bytes(new RenameCharacter(5, "Neu")))), Is.EqualTo(new RenameCharacter(5, "Neu")));
        var c = new CreateCharacter(1, 20, "Name", 0x11, 0x22, new byte[] { 1, 2, 3 }, 77);
        var back = CreateCharacter.Read(new ByteReader(Bytes(c)));
        Assert.That(back.Name, Is.EqualTo("Name")); Assert.That(back.Items, Is.EqualTo(c.Items)); Assert.That(back.Elapsed, Is.EqualTo(77));
        Assert.That(StateChange.Read(new ByteReader(Bytes(new StateChange(0, 1, 5, new Vector3(1, 2, 3))))).Position, Is.EqualTo(new Vector3(1, 2, 3)));
        Assert.That(Bytes(new StateChange(1, 0, 5, default)).Length, Is.EqualTo(6));
    }

    [Test] public void CreateItemBlockRoundTrip() {
        var dye = new EquipColor(new Rgba(1, 2, 3, 255), new Rgba(4, 5, 6, 255), new Rgba(7, 8, 9, 255), 3, 2);
        var items = new[] {
            new CreateItem(10200001, EquipSlot.HR, dye, Enumerable.Range(0, 14).Select(i => i * 0.5f).ToArray()),
            new CreateItem(10400001, EquipSlot.FD, default, new float[4]),
            new CreateItem(11400001, EquipSlot.CL, dye, Array.Empty<float>()),
        };
        var block = CreateCharacter.EncodeItems(items);
        // count, then per item 4 id + 1 slot + 20 colour + 4 per float
        Assert.That(block.Length, Is.EqualTo(1 + (25 + 56) + (25 + 16) + 25));
        Assert.That(block[0], Is.EqualTo(3)); Assert.That(block[5], Is.EqualTo((byte) EquipSlot.HR));
        var back = CreateCharacter.DecodeItems(block);
        Assert.That(back.Select(b => (b.Id, b.Slot, b.Color)), Is.EqualTo(items.Select(b => (b.Id, b.Slot, b.Color))));
        Assert.That(back[0].Appearance, Is.EqualTo(items[0].Appearance));
        var c = CreateCharacter.Read(new ByteReader(Bytes(new CreateCharacter(1, 10, "Maple", 0, 0, block, 12))));
        Assert.That(CreateCharacter.DecodeItems(c.Items).Count, Is.EqualTo(3));
        Assert.Throws<ArgumentException>(() => CreateCharacter.EncodeItems(new[] { new CreateItem(1, EquipSlot.HR, default, new float[2]) }));
    }
}
