using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Layouts from UIItemRemakeScrollDialog::FUN_140f59230 (@0x140f59230) and its readers and sends
/// (uiitemremakescrolldialog.cpp:357-580, :516, :702) and CCItemRemakeOptionScrollPacket (itemremakescrolldata.cpp:625-680).</summary>
public class ChangeAttributesScrollPacketTests {
    static ByteReader Reader(ByteWriter w) => new(w.Buffer.AsSpan(0, w.Length).ToArray());
    static string Hex(IOutPacket p) {
        var w = new ByteWriter();
        p.Write(w);
        return Convert.ToHexString(w.Buffer.AsSpan(0, w.Length).ToArray());
    }

    [Test] public void OpcodesMatchBothClients() {
        // KMS2 RecvOp 201 / SendOp 0x76 (commonfiles/opcodes.h:208, :423); GMS2 upstream SendOp / RecvOp ChangeAttributesScroll
        Assert.That(((ushort)Inbound.ChangeAttributesScroll, (ushort)Outbound.ChangeAttributesScroll),
            Is.EqualTo(((ushort)0xC9, (ushort)0x76)));
        Assert.That((ProtocolProfile.Gms2.Number(Inbound.ChangeAttributesScroll), ProtocolProfile.Gms2.Number(Outbound.ChangeAttributesScroll)),
            Is.EqualTo(((ushort)0xCB, (ushort)0x77)));
    }

    [Test] public void UseReadsTheScrollUid() {
        var w = new ByteWriter(); w.WriteByte(0); w.WriteLong(0x1122334455L);
        var r = Reader(w); var p = ChangeAttributesScrollPacket.Read(r);
        Assert.That((p.Type, p.Uid, p.HasBody, r.Available), Is.EqualTo(((byte)0, 0x1122334455L, false, 0)));
    }

    [Test] public void PreviewAndSelectLeaveTheBody() {
        // FUN_140f5a7e0 / FUN_140f5a9c0: ApplyOptionClient::vfunc_2, i64 uid then CItem::ReadFromStream
        foreach (byte type in new byte[] { 2, 3 }) {
            var w = new ByteWriter(); w.WriteByte(type); w.WriteLong(99); w.WriteByte(0xCD);
            var r = Reader(w); var p = ChangeAttributesScrollPacket.Read(r);
            Assert.That((p.Type, p.Uid, p.HasBody, r.Available), Is.EqualTo((type, 99L, true, 1)));
        }
    }

    [Test] public void ErrorReadsFlagAndCode() {
        // OnRemakeError (:557): u8 flag, i32 code
        var w = new ByteWriter(); w.WriteByte(4); w.WriteByte(1); w.WriteInt(14);
        var r = Reader(w); var p = ChangeAttributesScrollPacket.Read(r);
        Assert.That((p.Type, p.Flag, p.Code, p.HasBody, r.Available), Is.EqualTo(((byte)4, (byte)1, 14, false, 0)));
    }

    [Test] public void UnknownTypeReadsNothing() {
        var w = new ByteWriter(); w.WriteByte(1); w.WriteByte(0xEE);
        var r = Reader(w); var p = ChangeAttributesScrollPacket.Read(r);
        Assert.That((p.Type, r.Available), Is.EqualTo(((byte)1, 1)));
    }

    [Test] public void RemakeWritesScrollItemIngredientLockAndRows() {
        // SendRemakeRequest (:516): Encode1(1) then RemakeOption::vfunc_1 (@0x140617a90)
        Assert.That(Hex(ChangeAttributesScrollRequest.Remake(1, 2, 3, true, new RemakeLock(4, 0x0506))),
            Is.EqualTo("01" + "0100000000000000" + "0200000000000000" + "0300000000000000" + "01" + "01" + "04" + "0605"));
        Assert.That(Hex(ChangeAttributesScrollRequest.Remake(1, 2, 0, false)),
            Is.EqualTo("01" + "0100000000000000" + "0200000000000000" + "0000000000000000" + "00" + "00"));
    }

    [Test] public void ApplyWritesTheUid() {
        // ApplySelectedOption (:702): Encode1(3) then ApplyOptionBase::vfunc_1, i64 uid
        Assert.That(Hex(ChangeAttributesScrollRequest.Apply(0x0A0B)), Is.EqualTo("03" + "0B0A000000000000"));
        Assert.That(ChangeAttributesScrollRequest.Apply(1).Op, Is.EqualTo(Outbound.ChangeAttributesScroll));
    }
}
