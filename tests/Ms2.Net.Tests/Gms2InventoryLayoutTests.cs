using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The item inventory commands of the GMS2 profile, built from upstream ItemInventoryPacket.cs (command numbers, Add's unicode slot, item bodies).</summary>
public class Gms2InventoryLayoutTests {
    static readonly ProtocolProfile Gms = ProtocolProfile.Gms2;

    static void Head(ByteWriter w, int id, long uid, short slot) { w.WriteInt(id); w.WriteLong(uid); w.WriteShort(slot); w.WriteInt(2); }

    [Test]
    public void LoadIsCommand7WithGms2Bodies() {
        var w = new ByteWriter();
        w.WriteByte(7); w.WriteShort(2);
        for (int i = 0; i < 2; i++) { Head(w, 20000001 + i, 100 + i, (short) i); Gms2CharacterListLayoutTests.WriteBody(w); }
        var r = new ByteReader(w.ToArray());
        var m = (ItemMessage.Load) ItemMessage.Read(r, _ => null, Gms);
        Assert.That(m.Items.Select(i => i.Id), Is.EqualTo(new[] { 20000001, 20000002 }));
        Assert.That(m.Items[1].InventorySlot, Is.EqualTo(1));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test]
    public void ABadgeTabItemCarriesTheBadgeBlockInAnInventoryList() {
        var old = ItemReader.ExtraOf;
        ItemReader.ExtraOf = id => id == 70000001 ? ItemExtra.Gem : ItemExtra.None;
        try {
            var w = new ByteWriter();
            w.WriteByte(7); w.WriteShort(2);
            Head(w, 70000001, 100, 0);
            Gms2CharacterListLayoutTests.WriteBody(w, extra: x => {
                x.WriteByte(1); x.WriteByte(1); x.WriteUnicodeString("70000001");
                for (int i = 0; i < 10; i++) x.WriteBool(i % 2 == 0);
            });
            Head(w, 20000001, 101, 1); Gms2CharacterListLayoutTests.WriteBody(w);
            var r = new ByteReader(w.ToArray());
            var m = (ItemMessage.Load) ItemMessage.Read(r, _ => null, Gms);
            Assert.That(m.Items[0].GemType, Is.EqualTo(1)); Assert.That(m.Items[0].GemId, Is.EqualTo(70000001));
            Assert.That(m.Items[1].Id, Is.EqualTo(20000001));
            Assert.That(r.Available, Is.EqualTo(0));
        } finally { ItemReader.ExtraOf = old; }
    }

    [Test]
    public void ErrorIsTheCodeAloneEvenForTheLevelRestrictedCodes() {
        foreach (int code in new[] { 13, 0x28, 0x29 }) {
            var w = new ByteWriter(); w.WriteByte(15); w.WriteInt(code);
            var r = new ByteReader(w.ToArray());
            Assert.That(ItemMessage.Read(r, _ => null, Gms), Is.EqualTo(new ItemMessage.Error(code, 0, 0)));
            Assert.That(r.Available, Is.EqualTo(0));
        }
    }

    [Test]
    public void AddCarriesAUnicodeSlotAfterTheRarity() {
        var w = new ByteWriter();
        w.WriteByte(0); Head(w, 20000001, 5, 3); w.WriteUnicodeString(""); Gms2CharacterListLayoutTests.WriteBody(w);
        var r = new ByteReader(w.ToArray());
        var m = (ItemMessage.Add) ItemMessage.Read(r, _ => null, Gms);
        Assert.That(m.Item.Uid, Is.EqualTo(5));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test]
    public void LoadTabNotifyResetExpandAndUpdateUseTheGms2Numbers() {
        var w = new ByteWriter();
        w.WriteByte(10); w.WriteInt(1); w.WriteShort(1); Head(w, 20000001, 9, 0); Gms2CharacterListLayoutTests.WriteBody(w);
        var t = (ItemMessage.LoadTab) ItemMessage.Read(new ByteReader(w.ToArray()), _ => null, Gms);
        Assert.That(t.Tab, Is.EqualTo(1)); Assert.That(t.Items.Count, Is.EqualTo(1));

        w = new ByteWriter(); w.WriteByte(8); w.WriteLong(9); w.WriteInt(4); w.WriteUnicodeString("");
        Assert.That(ItemMessage.Read(new ByteReader(w.ToArray()), _ => null, Gms), Is.EqualTo(new ItemMessage.NotifyNew(9, 4, "")));

        w = new ByteWriter(); w.WriteByte(13); w.WriteInt(3);
        Assert.That(ItemMessage.Read(new ByteReader(w.ToArray()), _ => null, Gms), Is.EqualTo(new ItemMessage.Reset(3)));

        w = new ByteWriter(); w.WriteByte(14); w.WriteByte(2); w.WriteInt(5);
        Assert.That(ItemMessage.Read(new ByteReader(w.ToArray()), _ => null, Gms), Is.EqualTo(new ItemMessage.ExpandCount(2, 5)));

        w = new ByteWriter(); w.WriteByte(12);
        Assert.That(ItemMessage.Read(new ByteReader(w.ToArray()), _ => null, Gms), Is.EqualTo(new ItemMessage.ExpandComplete()));

        w = new ByteWriter(); w.WriteByte(15); w.WriteInt(7);
        Assert.That(ItemMessage.Read(new ByteReader(w.ToArray()), _ => null, Gms), Is.EqualTo(new ItemMessage.Error(7, 0, 0)));

        w = new ByteWriter(); w.WriteByte(1); w.WriteLong(9);
        Assert.That(ItemMessage.Read(new ByteReader(w.ToArray()), _ => null, Gms), Is.EqualTo(new ItemMessage.Remove(9)));
    }

    [Test]
    public void UpdateItemIsCommand16WithTheGms2Body() {
        var known = new EquipItem { Id = 20000001, Uid = 9, Rarity = 2 };
        var w = new ByteWriter(); w.WriteByte(16); w.WriteLong(9); Gms2CharacterListLayoutTests.WriteBody(w);
        var r = new ByteReader(w.ToArray());
        var m = (ItemMessage.Update) ItemMessage.Read(r, u => u == 9 ? known : null, Gms);
        Assert.That(m.Item, Is.Not.Null);
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test]
    public void KmsModeNumbersAreUnchanged() {
        var w = new ByteWriter(); w.WriteByte(11); w.WriteInt(3);
        Assert.That(ItemMessage.Read(new ByteReader(w.ToArray()), _ => null, ProtocolProfile.Kms2525), Is.EqualTo(new ItemMessage.Reset(3)));
        w = new ByteWriter(); w.WriteByte(11); w.WriteInt(3);   // 11 under GMS2 is not a command upstream writes
        Assert.That(ItemMessage.Read(new ByteReader(w.ToArray()), _ => null, Gms), Is.TypeOf<ItemMessage.Other>());
    }
}
