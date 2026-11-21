using System.Collections.Generic;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

// Inbound 0x97 and outbound 0x5E: UIItemEnchantDialog (client ms2/uiitemenchantdialog.cpp).
public class ItemEnchantPacketTests {
    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
    static ItemEnchantMessage Read(System.Action<ByteWriter> f, System.Func<long, EquipItem> find = null) {
        var w = new ByteWriter(); f(w);
        return ItemEnchantMessage.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()), find ?? (_ => null));
    }
    static readonly int BodyLen = Measure();
    static int Measure() { var r = new ByteReader(new byte[400]); ItemReader.ReadInventory(r); return r.Position - 18; }
    static void Body(ByteWriter w) { for (int i = 0; i < BodyLen; i++) w.WriteByte(0); }
    static void Options(ByteWriter w) { w.WriteInt(2); w.WriteShort(7); w.WriteInt(70); w.WriteShort(9); w.WriteInt(-5); }

    [Test] public void OpcodesAreTheClients() {
        Assert.That((ushort) Inbound.ItemEnchant, Is.EqualTo(0x97));
        Assert.That((ushort) Outbound.RequestItemEnchant, Is.EqualTo(0x5E));
    }

    [Test] public void TheSendsWriteTheClientsBytes() {
        Assert.That(Bytes(new ItemEnchantLoadRequest()), Is.EqualTo(new byte[] { 0 }));
        var w = new ByteWriter(); w.WriteByte(1); w.WriteByte(2); w.WriteLong(0x1122334455);
        Assert.That(Bytes(new ItemEnchantSelectRequest(2, 0x1122334455)), Is.EqualTo(w.Buffer.AsSpan(0, w.Length).ToArray()));
        w = new ByteWriter(); w.WriteByte(2); w.WriteLong(9); w.WriteByte(1);
        Assert.That(Bytes(new ItemEnchantIngredientRequest(9, 1)), Is.EqualTo(w.Buffer.AsSpan(0, w.Length).ToArray()));
        w = new ByteWriter(); w.WriteByte(3); w.WriteInt(-1);
        Assert.That(Bytes(new ItemEnchantFailStackRequest(-1)), Is.EqualTo(w.Buffer.AsSpan(0, w.Length).ToArray()));
        w = new ByteWriter(); w.WriteByte(4); w.WriteLong(5); w.WriteLong(6);
        Assert.That(Bytes(new ItemEnchantRequest(5, 6)), Is.EqualTo(w.Buffer.AsSpan(0, w.Length).ToArray()));
        w = new ByteWriter(); w.WriteByte(6); w.WriteLong(5);
        Assert.That(Bytes(new ItemEnchantExpRequest(5)), Is.EqualTo(w.Buffer.AsSpan(0, w.Length).ToArray()));
        w = new ByteWriter(); w.WriteByte(9); w.WriteLong(5);
        Assert.That(Bytes(new ItemEnchantRefundRequest(5)), Is.EqualTo(w.Buffer.AsSpan(0, w.Length).ToArray()));
        Assert.That(Bytes(new ItemEnchantDebugRequest(0xf)), Is.EqualTo(new byte[] { 0xf }));
        Assert.That(Bytes(new ItemEnchantDebugRequest(0x13)), Is.EqualTo(new byte[] { 0x13 }));
        w = new ByteWriter(); w.WriteByte(0x10); w.WriteLong(5); w.WriteInt(10);
        Assert.That(Bytes(new ItemEnchantDebugUidRequest(0x10, 5, 10)), Is.EqualTo(w.Buffer.AsSpan(0, w.Length).ToArray()));
        w = new ByteWriter(); w.WriteByte(0x11); w.WriteLong(5);
        Assert.That(Bytes(new ItemEnchantDebugUidRequest(0x11, 5, 10)), Is.EqualTo(w.Buffer.AsSpan(0, w.Length).ToArray()));
        Assert.That(new ItemEnchantRequest(1, 2).Op, Is.EqualTo(Outbound.RequestItemEnchant));
    }

    [Test] public void TheTalkCompanionHasTwoShapes() {
        var w = new ByteWriter(); w.WriteByte(6); w.WriteInt(11); w.WriteInt(22); w.WriteShort(1); w.WriteLong(5);
        Assert.That(Bytes(new ItemEnchantTalkRequest(true, 11, 22, 5)), Is.EqualTo(w.Buffer.AsSpan(0, w.Length).ToArray()));
        w = new ByteWriter(); w.WriteByte(4); w.WriteShort(1); w.WriteLong(5);
        Assert.That(Bytes(new ItemEnchantTalkRequest(false, 11, 22, 5)), Is.EqualTo(w.Buffer.AsSpan(0, w.Length).ToArray()));
        Assert.That(new ItemEnchantTalkRequest(true, 0, 0, 0).Op, Is.EqualTo(Outbound.NpcTalk));
    }

    [Test] public void Mode5ReadsTheOphelia() {
        var m = (ItemEnchantMessage.Info) Read(w => {
            w.WriteByte(5); w.WriteShort(1); w.WriteLong(77);
            w.WriteByte(2); for (int i = 0; i < 2; i++) { w.WriteInt(100 + i); w.WriteInt(i); w.WriteInt(3 + i); }
            w.WriteShort(0x1234); Options(w);
            for (int i = 1; i <= 5; i++) w.WriteFloat(i);
            w.WriteLong(8); w.WriteLong(9); w.WriteByte(1); w.WriteInt(555); w.WriteShort(4); w.WriteInt(2);
        });
        Assert.That(m.ExpMode, Is.EqualTo(1)); Assert.That(m.Uid, Is.EqualTo(77));
        Assert.That(m.Ingredients, Is.EqualTo(new[] { new EnchantIngredient(100, 0, 3), new EnchantIngredient(101, 1, 4) }));
        Assert.That(m.InfoFlags, Is.EqualTo(0x1234));
        Assert.That(m.Options, Is.EqualTo(new Dictionary<int, int> { [7] = 70, [9] = -5 }));
        Assert.That(m.Rates, Is.EqualTo(new EnchantRates(1, 2, 3, 4, 5)));
        Assert.That((m.Protector0, m.Protector1, m.FailStackMode), Is.EqualTo((8L, 9L, (byte) 1)));
        Assert.That((m.ResultItemId, m.ResultRank, m.ResultCount), Is.EqualTo((555, (short) 4, 2)));
    }

    [Test] public void Mode5ReadsThePeachyAndStopsForAnyOtherMode() {
        var m = (ItemEnchantMessage.Info) Read(w => {
            w.WriteByte(5); w.WriteShort(2); w.WriteLong(77); w.WriteByte(0); w.WriteShort(0); w.WriteInt(0);
            w.WriteInt(555); w.WriteShort(4); w.WriteInt(2);
        });
        Assert.That(m.Rates.Base, Is.EqualTo(100f)); Assert.That(m.ResultItemId, Is.EqualTo(555));
        var o = (ItemEnchantMessage.Info) Read(w => { w.WriteByte(5); w.WriteShort(3); w.WriteLong(77); w.WriteByte(0); w.WriteShort(0); w.WriteInt(0); });
        Assert.That(o.ExpMode, Is.EqualTo(3)); Assert.That(o.ResultItemId, Is.EqualTo(0));
    }

    [Test] public void ChargeAndFailStackModes() {
        var c = (ItemEnchantMessage.ChargeChanged) Read(w => { w.WriteByte(6); w.WriteLong(3); w.WriteInt(1234); });
        Assert.That((c.Uid, c.Charge), Is.EqualTo((3L, 1234)));
        var f = (ItemEnchantMessage.FailStackInfo) Read(w => {
            w.WriteByte(7); w.WriteInt(2); w.WriteInt(3); w.WriteInt(2); w.WriteLong(10); w.WriteLong(11);
            for (int i = 1; i <= 5; i++) w.WriteFloat(i * 2);
        });
        Assert.That((f.FailStack, f.FailStackCount), Is.EqualTo((2, 3))); Assert.That(f.Uids, Is.EqualTo(new long[] { 10, 11 }));
        Assert.That(f.Rates, Is.EqualTo(new EnchantRates(2, 4, 6, 8, 10)));
        var s = (ItemEnchantMessage.FailStackList) Read(w => { w.WriteByte(8); w.WriteInt(5); w.WriteInt(1); w.WriteLong(12); });
        Assert.That((s.FailStackCount, s.Uids), Is.EqualTo((5, new long[] { 12 })));
        Assert.That(Read(w => w.WriteByte(9)), Is.InstanceOf<ItemEnchantMessage.Refunded>());
        Assert.That(Read(w => w.WriteByte(16)), Is.InstanceOf<ItemEnchantMessage.IngredientCounts>());
        Assert.That(((ItemEnchantMessage.Error) Read(w => { w.WriteByte(12); w.WriteShort(3); })).Code, Is.EqualTo(3));
        Assert.That(((ItemEnchantMessage.Other) Read(w => w.WriteByte(99))).Mode, Is.EqualTo(99));
    }

    [Test] public void ResultModesReadTheBodyThenTheirTail() {
        EquipItem Known(long u) => u == 5 ? new EquipItem { Id = 1, Uid = 5 } : null;
        var ok = (ItemEnchantMessage.Success) Read(w => { w.WriteByte(10); w.WriteLong(5); Body(w); Options(w); }, Known);
        Assert.That(ok.Item, Is.Not.Null); Assert.That(ok.Options[7], Is.EqualTo(70));
        var fail = (ItemEnchantMessage.Failure) Read(w => { w.WriteByte(11); w.WriteLong(5); Body(w); w.WriteInt(1); w.WriteInt(2); w.WriteLong(3); w.WriteInt(4); }, Known);
        Assert.That((fail.A, fail.B, fail.C, fail.FailType), Is.EqualTo((1, 2, 3L, 4)));
        var unk = (ItemEnchantMessage.Success) Read(w => { w.WriteByte(10); w.WriteLong(6); }, Known);
        Assert.That(unk.Item, Is.Null);
        var up = (ItemEnchantMessage.ItemUpdate) Read(w => { w.WriteByte(15); w.WriteLong(5); Body(w); }, Known);
        Assert.That(up.Item.Uid, Is.EqualTo(5));
    }
}
