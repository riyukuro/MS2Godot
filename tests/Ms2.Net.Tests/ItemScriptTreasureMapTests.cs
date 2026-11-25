using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The treasure map dialog's ItemScript subs (UIItemTreasureMapDialog::vfunc_79, uiitemtreasuremapdialog.cpp:174), its cost
/// request (SendTreasureMapMode2, :233), and the box dialogs' ItemScript modes 0 and 5, read and written as the client does.</summary>
public class ItemScriptTreasureMapTests {
    static ByteReader Reader(ByteWriter w) => new(w.Buffer.AsSpan(0, w.Length).ToArray());

    [Test] public void SubTwoReadsTheUidAndTheCost() {
        var w = new ByteWriter(); w.WriteByte(2); w.WriteLong(0x1122334455L); w.WriteLong(-1);
        var r = Reader(w);
        Assert.That(ItemScriptTreasureMap.TryRead(r), Is.EqualTo(new ItemScriptTreasureMap.Cost(0x1122334455L, -1)));
        Assert.That(r.Available, Is.Zero);
    }

    [Test] public void SubThreeReadsTheItemOnlyWhenItIsOk() {
        var w = new ByteWriter(); w.WriteByte(3); w.WriteBool(true); w.WriteInt(30001025); w.WriteInt(3);
        var r = Reader(w);
        Assert.That(ItemScriptTreasureMap.TryRead(r), Is.EqualTo(new ItemScriptTreasureMap.Result(true, 30001025, 3)));
        Assert.That(r.Available, Is.Zero);
        w = new ByteWriter(); w.WriteByte(3); w.WriteBool(false);
        r = Reader(w);
        Assert.That(ItemScriptTreasureMap.TryRead(r), Is.EqualTo(new ItemScriptTreasureMap.Result(false, 0, 0)));
        Assert.That(r.Available, Is.Zero, "the end of the appraise has no item");
    }

    [TestCase(0)] [TestCase(4)] [TestCase(5)]
    public void OtherSubsAreNotTheDialogs(int sub) {
        var w = new ByteWriter(); w.WriteByte((byte) sub); w.WriteInt(0);
        Assert.That(ItemScriptTreasureMap.TryRead(Reader(w)), Is.Null);
    }

    [Test] public void TheCostRequestIsSubTwoWithTheUidAndTheFreeFlag() {
        var p = new TreasureMapCostRequest(77, true);
        Assert.That(p.Op, Is.EqualTo(Outbound.TreasureMap));
        Assert.That((ushort) Outbound.TreasureMap, Is.EqualTo(0x9F), "SendOp TreasureMap (commonfiles/opcodes.h:462)");
        var w = new ByteWriter(); p.Write(w);
        var r = Reader(w);
        Assert.That((r.ReadByte(), r.ReadLong(), r.ReadBool(), r.Available), Is.EqualTo(((byte) 2, 77L, true, 0)));
    }

    [Test] public void TheBoxModesReadTheirItems() {
        var w = new ByteWriter(); w.WriteByte(5); w.WriteInt(2);
        w.WriteInt(20000001); w.WriteInt(3); w.WriteInt(1); w.WriteBool(false);
        w.WriteInt(20000002); w.WriteInt(1); w.WriteInt(4); w.WriteBool(true);
        var r = Reader(w);
        var box = ItemScriptBoxResult.TryRead(r);
        Assert.That(box.Mode, Is.EqualTo(ItemScriptBoxResult.GachaMode));
        Assert.That(box.Items, Is.EqualTo(new[] { new ItemScriptBoxItem(20000001, 3, 1, false), new ItemScriptBoxItem(20000002, 1, 4, true) }));
        Assert.That(r.Available, Is.Zero);
        w = new ByteWriter(); w.WriteByte(2); w.WriteLong(1); w.WriteLong(0);
        Assert.That(ItemScriptBoxResult.TryRead(Reader(w)), Is.Null, "the treasure map's sub is not a box result");
    }
}
