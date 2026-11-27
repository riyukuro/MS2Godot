using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Layouts from UIItemRemakeOptionDialog::vfunc_79 (@0x14129daa0) and its readers and sends
/// (unit_14129cfa0_itemremakeoptiondialog.cpp:321-1180, itemremakescrolldata.cpp:456-626).</summary>
public class ChangeAttributesPacketTests {
    static ByteReader Reader(ByteWriter w) => new(w.Buffer.AsSpan(0, w.Length).ToArray());
    static string Hex(IOutPacket p) {
        var w = new ByteWriter();
        p.Write(w);
        return Convert.ToHexString(w.Buffer.AsSpan(0, w.Length).ToArray());
    }

    [Test] public void OpcodesMatchBothClients() {
        // KMS2 RecvOp 200 / SendOp 0x75 (commonfiles/opcodes.h:207, :422); GMS2 upstream SendOp / RecvOp ChangeAttributes
        Assert.That(((ushort)Inbound.ChangeAttributes, (ushort)Outbound.ChangeAttributes), Is.EqualTo(((ushort)0xC8, (ushort)0x75)));
        Assert.That((ProtocolProfile.Gms2.Number(Inbound.ChangeAttributes), ProtocolProfile.Gms2.Number(Outbound.ChangeAttributes)),
            Is.EqualTo(((ushort)0xCA, (ushort)0x76)));
    }

    [Test] public void LastResultStopsAtEachEarlyReturn() {
        // FUN_1412a2c50: result 0 ends; result 2 ends after the item id; else the uid, the apply uid and the body
        var w = new ByteWriter(); w.WriteByte(6); w.WriteByte(0);
        var r = Reader(w); var p = ChangeAttributesPacket.Read(r);
        Assert.That((p.Kind, p.Result, p.HasBody, r.Available), Is.EqualTo(((byte)6, (byte)0, false, 0)));
        w = new ByteWriter(); w.WriteByte(6); w.WriteByte(2); w.WriteInt(15000001);
        r = Reader(w); p = ChangeAttributesPacket.Read(r);
        Assert.That((p.Result, p.ItemId, p.HasBody, r.Available), Is.EqualTo(((byte)2, 15000001, false, 0)));
        w = new ByteWriter(); w.WriteByte(6); w.WriteByte(1); w.WriteInt(15000001); w.WriteLong(77); w.WriteLong(78); w.WriteByte(0xAB);
        r = Reader(w); p = ChangeAttributesPacket.Read(r);
        Assert.That((p.Result, p.ItemId, p.Uid, p.ApplyUid, p.HasBody), Is.EqualTo(((byte)1, 15000001, 77L, 78L, true)));
        Assert.That(r.Available, Is.EqualTo(1), "the item body is left to the caller");
    }

    [Test] public void AcceptedReadsTheUidAndIngredients() {
        // FUN_14129f940: i64 uid, CIngredientContainer (u8 count, then i32 id, i32 tag, i32 count)
        var w = new ByteWriter(); w.WriteByte(4); w.WriteLong(41); w.WriteByte(2);
        w.WriteInt(0); w.WriteInt(0x60); w.WriteInt(300);
        w.WriteInt(30000001); w.WriteInt(0); w.WriteInt(5);
        var r = Reader(w); var p = ChangeAttributesPacket.Read(r);
        Assert.That((p.Kind, p.Uid, r.Available), Is.EqualTo(((byte)4, 41L, 0)));
        Assert.That(p.Ingredients, Is.EqualTo(new[] { new EnchantIngredient(0, 0x60, 300), new EnchantIngredient(30000001, 0, 5) }));
    }

    [Test] public void ResultAndAppliedLeaveTheBody() {
        foreach (byte kind in new byte[] { 1, 2 }) {
            var w = new ByteWriter(); w.WriteByte(kind); w.WriteLong(99); w.WriteByte(0xCD);
            var r = Reader(w); var p = ChangeAttributesPacket.Read(r);
            Assert.That((p.Kind, p.ApplyUid, p.HasBody, r.Available), Is.EqualTo((kind, 99L, true, 1)));
        }
    }

    [Test] public void ErrorReadsFlagAndCodeAndDebugKindReadsNothing() {
        var w = new ByteWriter(); w.WriteByte(5); w.WriteByte(1); w.WriteInt(9);
        var r = Reader(w); var p = ChangeAttributesPacket.Read(r);
        Assert.That((p.Flag, p.Code, r.Available), Is.EqualTo(((byte)1, 9, 0)));
        w = new ByteWriter(); w.WriteByte(8);
        r = Reader(w); p = ChangeAttributesPacket.Read(r);
        Assert.That((p.Kind, r.Available), Is.EqualTo(((byte)8, 0)));
    }

    [Test] public void SendsWriteEachMode() {
        // SendRemakeRequest: mode 0, uid, i64 0, the lock count and each u8 index + i16 value
        Assert.That(Hex(ChangeAttributesRequest.Remake(0x0102)), Is.EqualTo("00" + "0201000000000000" + "0000000000000000" + "00"));
        Assert.That(Hex(ChangeAttributesRequest.Remake(0x0102, new RemakeLock(1, 0x0203))),
            Is.EqualTo("00" + "0201000000000000" + "0000000000000000" + "01" + "01" + "0302"));
        Assert.That(Hex(ChangeAttributesRequest.Apply(5)), Is.EqualTo("02" + "0500000000000000"));
        Assert.That(Hex(ChangeAttributesRequest.Keep()), Is.EqualTo("0301"));
        Assert.That(Hex(ChangeAttributesRequest.Select(6)), Is.EqualTo("04" + "0600000000000000"));
        Assert.That(Hex(ChangeAttributesRequest.Opened()), Is.EqualTo("0601"));
        Assert.That(ChangeAttributesRequest.Opened().Op, Is.EqualTo(Outbound.ChangeAttributes));
    }
}
