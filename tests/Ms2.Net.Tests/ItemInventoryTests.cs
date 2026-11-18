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

    // Unk_Inventory::AddItem (0x140a5d120): a duplicate uid, an occupied slot or a slot out of range is refused and the old item stays.
    [Test] public void AddRefusesDuplicateUidOccupiedAndOutOfRangeSlot() {
        var book = new ItemBook { TabOf = id => id / 10000000, BaseSlots = tab => 4 };
        book.Apply(Read(book, w => { w.WriteByte(6); w.WriteShort(1); Item(w, 20000001, 11, 1, 5); }));
        book.Apply(Read(book, w => { w.WriteByte(0); Item(w, 20000009, 11, 2, 9); }));          // same uid
        Assert.That(book.Get(11).Id, Is.EqualTo(20000001)); Assert.That(book.Get(11).Amount, Is.EqualTo(5));
        book.Apply(Read(book, w => { w.WriteByte(0); Item(w, 20000002, 12, 1, 1); }));          // same tab, slot 1 taken
        Assert.That(book.Get(12), Is.Null);
        book.Apply(Read(book, w => { w.WriteByte(0); Item(w, 30000002, 13, 1, 1); }));          // other tab, slot 1 free
        Assert.That(book.Get(13), Is.Not.Null);
        book.Apply(Read(book, w => { w.WriteByte(0); Item(w, 20000003, 14, 4, 1); }));          // slot 4 of 4
        Assert.That(book.Get(14), Is.Null);
        Assert.That(book.TryAdd(new EquipItem { Id = 20000004, Uid = 15, InventorySlot = 3 }), Is.EqualTo(0));
        Assert.That(book.TryAdd(new EquipItem { Id = 20000004, Uid = 15, InventorySlot = 2 }), Is.EqualTo(17));
        Assert.That(book.TryAdd(new EquipItem { Id = 20000004, Uid = 16, InventorySlot = 3 }), Is.EqualTo(17));
        Assert.That(book.TryAdd(new EquipItem { Id = 20000004, Uid = 17, InventorySlot = 9 }), Is.EqualTo(29));
    }

    // CItem::SetCountClamped (0x140526c70): an unsigned compare with the max stack.
    [Test] public void AmountIsClampedUnsignedToTheMaxStack() {
        var book = new ItemBook { MaxStackOf = id => 100 };
        book.Apply(Read(book, w => { w.WriteByte(6); w.WriteShort(1); Item(w, 20000001, 11, 1, 5); }));
        book.Apply(Read(book, w => { w.WriteByte(2); w.WriteLong(11); w.WriteInt(60); })); Assert.That(book.Get(11).Amount, Is.EqualTo(60));
        book.Apply(Read(book, w => { w.WriteByte(2); w.WriteLong(11); w.WriteInt(500); })); Assert.That(book.Get(11).Amount, Is.EqualTo(100));
        book.Apply(Read(book, w => { w.WriteByte(2); w.WriteLong(11); w.WriteInt(-1); })); Assert.That(book.Get(11).Amount, Is.EqualTo(100));
    }

    // Unk_Inventory::SetExtraSlots (0x140a5cb80): tabs outside 0-14 are ignored; a smaller capacity removes the items past it.
    [Test] public void ExpandCountIgnoresBadTabsAndShrinks() {
        var book = new ItemBook { TabOf = id => id / 10000000, BaseSlots = tab => 4 };
        book.Apply(Read(book, w => { w.WriteByte(12); w.WriteByte(2); w.WriteInt(6); }));
        book.Apply(Read(book, w => { w.WriteByte(6); w.WriteShort(2); Item(w, 20000001, 11, 1, 1); Item(w, 20000002, 12, 8, 1); }));
        Assert.That(book.All.Count, Is.EqualTo(2));                                              // slot 8 < 4 + 6
        book.Apply(Read(book, w => { w.WriteByte(12); w.WriteByte(15); w.WriteInt(1); }));
        Assert.That(book.ExtraSlots.ContainsKey(15), Is.False);
        book.Apply(Read(book, w => { w.WriteByte(12); w.WriteByte(2); w.WriteInt(2); }));        // capacity 6: slot 8 goes
        Assert.That(book.Get(12), Is.Null); Assert.That(book.Get(11), Is.Not.Null); Assert.That(book.ExtraSlots[2], Is.EqualTo(2));
    }

    // CItem::FUN_140523c40 (item.cpp:2759) fails for an item without data: no body is read and nothing is added.
    [Test] public void UnknownItemIdReadsNoBodyAndIsNotAdded() {
        ItemReader.Known = id => id != 20000002;
        try {
            var book = new ItemBook();
            var w = new ByteWriter(); w.WriteByte(6); w.WriteShort(3); Item(w, 20000001, 11, 1, 1); Item(w, 20000002, 12, 2, 1); Item(w, 20000003, 13, 3, 1);
            var r = new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
            var m = (ItemMessage.Load) ItemMessage.Read(r, book.Get); book.Apply(m);
            Assert.That(m.Items.Select(i => i.Uid), Is.EqualTo(new long[] { 11 }));
            Assert.That(r.Available, Is.EqualTo(BodyLen + 3 * 0 + BodyLen + 18));                // the unknown record's body and the rest stay unread
            var w2 = new ByteWriter(); w2.WriteByte(0); Item(w2, 20000002, 14, 1, 1);
            var r2 = new ByteReader(w2.Buffer.AsSpan(0, w2.Length).ToArray());
            book.Apply(ItemMessage.Read(r2, book.Get)); Assert.That(book.Get(14), Is.Null); Assert.That(r2.Available, Is.EqualTo(BodyLen));
        } finally { ItemReader.Known = null; }
    }

    [Test] public void PutOnOrOffModesFollowTheClientReader() {
        static ItemPutOnOrOff Rd(System.Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return ItemPutOnOrOff.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray())); }
        Assert.That(Rd(w => { w.WriteByte(2); w.WriteLong(77); }), Is.EqualTo(new ItemPutOnOrOff.Equip(77)));
        Assert.That(Rd(w => { w.WriteByte(3); w.WriteLong(78); }), Is.EqualTo(new ItemPutOnOrOff.Unequip(78)));
        Assert.That(Rd(w => { w.WriteByte(5); w.WriteInt(9); w.WriteLong(79); w.WriteByte(4); w.WriteByte(1); }), Is.EqualTo(new ItemPutOnOrOff.MoveWorn(9, 79, 4, 1)));
        Assert.That(Rd(w => { w.WriteByte(4); }), Is.EqualTo(new ItemPutOnOrOff.Other(4)));
    }

    // FUN_140a66510 / FUN_140a66750 against the KMS2 table/constants.xml entries (bagSlotTab*Count) and the switch order at 0x140a66510.
    [Test] public void BagSlotsFollowTheClientSwitchAndConstants() {
        var xml = new System.Collections.Generic.Dictionary<string, int[]> {
            ["bagSlotTabGameCount"] = new[] { 48, 48 }, ["bagSlotTabSkinCount"] = new[] { 156, 156 }, ["bagSlotTabSummonCount"] = new[] { 48, 48 },
            ["bagSlotTabMaterialCount"] = new[] { 48, 48 }, ["bagSlotTabMasteryCount"] = new[] { 126, 48 }, ["bagSlotTabLifeCount"] = new[] { 48, 48 },
            ["bagSlotTabQuestCount"] = new[] { 48, 48 }, ["bagSlotTabGemCount"] = new[] { 48, 48 }, ["bagSlotTabPetCount"] = new[] { 60, 78 },
            ["bagSlotTabActiveSkillCount"] = new[] { 84, 48 }, ["bagSlotTabCoinCount"] = new[] { 48, 48 }, ["bagSlotTabBadgeCount"] = new[] { 60, 48 },
            ["bagSlotTabMiscCount"] = new[] { 84, 48 }, ["bagSlotTabLapenShardCount"] = new[] { 48, 48 }, ["bagSlotTabPieceCount"] = new[] { 48, 48 },
        };
        int[] want = { 48, 156, 48, 48, 48, 48, 48, 84, 126, 60, 84, 48, 60, 48, 48 };
        for (int t = 0; t < 15; t++) Assert.That(BagSlots.Base(t, true, k => xml.GetValueOrDefault(k)), Is.EqualTo(want[t]), "tab " + t);
        Assert.That(BagSlots.Max(9, true, k => xml.GetValueOrDefault(k)), Is.EqualTo(78));
        Assert.That(BagSlots.Base(1, false, k => xml.GetValueOrDefault(k)), Is.EqualTo(48));      // BagRenewal01 off
        Assert.That(BagSlots.Base(1, true, k => null), Is.EqualTo(48));                           // entry absent
        Assert.Throws<System.ArgumentOutOfRangeException>(() => BagSlots.Base(15, true, k => null));
    }
}
