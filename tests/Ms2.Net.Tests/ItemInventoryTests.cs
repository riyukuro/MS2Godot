using System.Linq;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class ItemInventoryTests {
    // The item body of an item with default blocks (ItemReader.ReadBody): the length is measured by parsing zeros, then amount is set.
    static readonly int BodyLen = Measure();
    static int Measure() { var r = new ByteReader(new byte[400]); ItemReader.ReadInventory(r); return r.Position - 18; }

    static void Item(ByteWriter w, int id, long uid, short slot, int amount) {
        w.WriteInt(id); w.WriteLong(uid); w.WriteShort(slot); w.WriteInt(1);
        var body = new byte[BodyLen]; System.BitConverter.GetBytes(amount).CopyTo(body, 0); w.WriteBytes(body);
    }
    static ItemMessage Read(ItemBook book, System.Action<ByteWriter> f) {
        var w = new ByteWriter(); f(w);
        var r = new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
        return ItemMessage.Read(r, book.Get);
    }

    [Test] public void LoadAddAmountMoveRemove() {
        var book = new ItemBook(); var seen = new List<long>(); book.Changed += seen.Add;
        book.Apply(Read(book, w => { w.WriteByte(6); w.WriteShort(2); Item(w, 20000001, 11, 3, 5); Item(w, 20000002, 12, 4, 1); }));
        Assert.That(book.All.Count, Is.EqualTo(2)); Assert.That(book.Get(11).Amount, Is.EqualTo(5)); Assert.That(book.Get(12).InventorySlot, Is.EqualTo(4));
        book.Apply(Read(book, w => { w.WriteByte(0); Item(w, 20000003, 13, 9, 2); }));
        Assert.That(book.Get(13).Id, Is.EqualTo(20000003));
        book.Apply(Read(book, w => { w.WriteByte(2); w.WriteLong(11); w.WriteInt(8); })); Assert.That(book.Get(11).Amount, Is.EqualTo(8));
        book.Apply(Read(book, w => { w.WriteByte(3); w.WriteLong(11); w.WriteShort(4); w.WriteLong(12); w.WriteShort(3); }));
        Assert.That(book.Get(11).InventorySlot, Is.EqualTo(4)); Assert.That(book.Get(12).InventorySlot, Is.EqualTo(3));
        book.Apply(Read(book, w => { w.WriteByte(14); w.WriteLong(13); var b = new byte[BodyLen]; b[0] = 7; w.WriteBytes(b); }));
        Assert.That(book.Get(13).Amount, Is.EqualTo(7)); Assert.That(book.Get(13).Id, Is.EqualTo(20000003));
        book.Apply(Read(book, w => { w.WriteByte(1); w.WriteLong(12); })); Assert.That(book.Get(12), Is.Null);
        Assert.That(seen, Is.EqualTo(new long[] { 0, 13, 11, 0, 13, 12 }));
    }

    [Test] public void ErrorAndExpandAndResetByTab() {
        var book = new ItemBook(); book.TabOf = id => id / 10000000;
        (int, int, int) err = default; book.ErrorReceived += (c, i, v) => err = (c, i, v);
        book.Apply(Read(book, w => { w.WriteByte(8); w.WriteInt(2); w.WriteShort(1); Item(w, 20000001, 1, 0, 1); }));
        book.Apply(Read(book, w => { w.WriteByte(11); w.WriteInt(2); })); Assert.That(book.All, Is.Empty);
        book.Apply(Read(book, w => { w.WriteByte(12); w.WriteByte(3); w.WriteInt(6); })); Assert.That(book.ExtraSlots[3], Is.EqualTo(6));
        book.Apply(Read(book, w => { w.WriteByte(13); w.WriteInt(0x28); w.WriteInt(555); w.WriteShort(30); })); Assert.That(err, Is.EqualTo((0x28, 555, 30)));
        Assert.That(Read(book, w => w.WriteByte(10)), Is.TypeOf<ItemMessage.ExpandComplete>());
        Assert.That(Read(book, w => w.WriteByte(99)), Is.TypeOf<ItemMessage.Other>());
    }
}
