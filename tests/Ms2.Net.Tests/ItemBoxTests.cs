using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>OnItemBox (scenestategame.cpp:8407) and the multi open dialog's request (uiitemmultiopenboxdialog.cpp:137).</summary>
public class ItemBoxTests {
    static ByteReader Reader(ByteWriter w) => new(w.Buffer.AsSpan(0, w.Length).ToArray());

    [Test] public void TheResultIsReadAsTheClientReadsIt() {
        var w = new ByteWriter(); w.WriteInt(20300001); w.WriteInt(3); w.WriteShort(ItemBoxResult.ResultInventoryFull);
        var m = ItemBoxResult.Read(Reader(w));
        Assert.That((m.ItemId, m.Opened, m.Result), Is.EqualTo((20300001, 3, 4)));
    }

    [Test] public void ATrailingByteIsALayoutError() {
        var w = new ByteWriter(); w.WriteInt(1); w.WriteInt(1); w.WriteShort(2); w.WriteByte(0);
        Assert.Throws<PacketLayoutException>(() => ItemBoxResult.Read(Reader(w)));
    }

    [Test] public void TheRequestIsIdSlotCountKey() {
        var w = new ByteWriter(); new ItemBoxRequest(20300001, 7, 5, "k").Write(w);
        var r = Reader(w);
        Assert.That((r.ReadInt(), r.ReadShort(), r.ReadInt(), r.ReadUnicodeString()), Is.EqualTo((20300001, (short) 7, 5, "k")));
        r.ExpectEnd("t");
    }
}
