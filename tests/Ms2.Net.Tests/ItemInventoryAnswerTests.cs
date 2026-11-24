using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// The bag requests and answers of RequestItemInventory / ItemInventory on both profiles: the GMS2 sort and expand commands
/// (upstream ItemInventoryHandler Sort = 10, Expand = 11), the GMS2 tab numbers, OnItemInventory's mode 0xd answers
/// (scenestategame.cpp:2973) and the expand events (modes 0xa and 0xc).
/// </summary>
public class ItemInventoryAnswerTests {
    static byte[] Bytes(IOutPacket p, ProtocolProfile profile) {
        var w = new ByteWriter();
        if (p is IProfileOutPacket pp) pp.Write(w, profile); else p.Write(w);
        return w.ToArray();
    }

    [Test]
    public void SortIsMode8OnKmsAndMode10WithTheWireTabOnGms2() {
        Assert.That(Bytes(new ItemInventorySortRequest(3, true), ProtocolProfile.Kms2525), Is.EqualTo(new byte[] { 8, 3, 1 }));
        // the recorded GMS2 sort: 0a 00 00
        Assert.That(Bytes(new ItemInventorySortRequest(0, false), ProtocolProfile.Gms2), Is.EqualTo(new byte[] { 10, 0, 0 }));
        // client tab 10 (consumable) is the wire's 11; client 13 (lapenshard) is 15
        Assert.That(Bytes(new ItemInventorySortRequest(10, false), ProtocolProfile.Gms2), Is.EqualTo(new byte[] { 10, 11, 0 }));
        Assert.That(Bytes(new ItemInventorySortRequest(13, true), ProtocolProfile.Gms2), Is.EqualTo(new byte[] { 10, 15, 1 }));
    }

    [Test]
    public void ExpandIsMode9OnKmsAndMode11WithTheWireTabOnGms2() {
        Assert.That(Bytes(new ItemInventoryExpandRequest(9), ProtocolProfile.Kms2525), Is.EqualTo(new byte[] { 9, 9 }));
        Assert.That(Bytes(new ItemInventoryExpandRequest(9), ProtocolProfile.Gms2), Is.EqualTo(new byte[] { 11, 10 }));
        Assert.That(Bytes(new ItemInventoryExpandRequest(2), ProtocolProfile.Gms2), Is.EqualTo(new byte[] { 11, 2 }));
    }

    [Test]
    public void DropAndDestroyAreTheSameOnBothProfiles() {
        foreach (var pr in new[] { ProtocolProfile.Kms2525, ProtocolProfile.Gms2 }) {
            Assert.That(Bytes(new ItemInventoryDropRequest(0x0102030405060708L, 5), pr), Is.EqualTo(Convert.FromHexString("04080706050403020105000000")));
            Assert.That(Bytes(new ItemInventoryDestroyRequest(0x0102030405060708L), pr), Is.EqualTo(Convert.FromHexString("050807060504030201")));
        }
    }

    [Test]
    public void Gms2TabsSkipTheWireNumbers8And14() {
        var wire = Enumerable.Range(0, 15).Select(Gms2InventoryTab.ToWire).ToArray();
        Assert.That(wire, Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 9, 10, 11, 12, 13, 15, 16 }));
        for (int t = 0; t < 15; t++) Assert.That(Gms2InventoryTab.FromWire(Gms2InventoryTab.ToWire(t)), Is.EqualTo(t));
        foreach (int w in new[] { 8, 14, 17, -1 }) Assert.That(Gms2InventoryTab.FromWire(w), Is.EqualTo(-1));
    }

    [Test]
    public void Gms2ResetAndExpandCountNameTheClientTab() {
        var w = new ByteWriter(); w.WriteByte(13); w.WriteInt(11);
        Assert.That(ItemMessage.Read(new ByteReader(w.ToArray()), _ => null, ProtocolProfile.Gms2), Is.EqualTo(new ItemMessage.Reset(10)));
        w = new ByteWriter(); w.WriteByte(14); w.WriteByte(16); w.WriteInt(12);
        Assert.That(ItemMessage.Read(new ByteReader(w.ToArray()), _ => null, ProtocolProfile.Gms2), Is.EqualTo(new ItemMessage.ExpandCount(14, 12)));
        w = new ByteWriter(); w.WriteByte(14); w.WriteByte(8); w.WriteInt(12);
        Assert.That(ItemMessage.Read(new ByteReader(w.ToArray()), _ => null, ProtocolProfile.Gms2), Is.EqualTo(new ItemMessage.ExpandCount(-1, 12)));
        // KMS keeps the number as it is
        w = new ByteWriter(); w.WriteByte(12); w.WriteByte(11); w.WriteInt(12);
        Assert.That(ItemMessage.Read(new ByteReader(w.ToArray()), _ => null, ProtocolProfile.Kms2525), Is.EqualTo(new ItemMessage.ExpandCount(11, 12)));
    }

    [Test]
    public void AGms2ResetEmptiesTheClientTabNotTheWireNumber() {
        var book = new ItemBook { TabOf = id => id / 1000 };
        book.Apply(new ItemMessage.Add(new EquipItem { Id = 10001, Uid = 1, InventorySlot = 0 }));
        book.Apply(new ItemMessage.Add(new EquipItem { Id = 11001, Uid = 2, InventorySlot = 0 }));
        var w = new ByteWriter(); w.WriteByte(13); w.WriteInt(11);
        book.Apply(ItemMessage.Read(new ByteReader(w.ToArray()), _ => null, ProtocolProfile.Gms2));
        Assert.That(book.All.Select(i => i.Uid), Is.EqualTo(new long[] { 2 }));
    }

    [Test]
    public void ExpandModesRaiseTheirEvents() {
        var book = new ItemBook();
        int done = 0; var set = new List<int>();
        book.ExpandCompleted += () => done++;
        book.ExtraSlotsSet += set.Add;
        book.Apply(new ItemMessage.ExpandComplete());
        book.Apply(new ItemMessage.ExpandCount(2, 6));
        Assert.That(done, Is.EqualTo(1));
        Assert.That(set, Is.EqualTo(new[] { 2 }));
        Assert.That(book.ExtraSlots[2], Is.EqualTo(6));
    }

    [Test]
    public void ErrorCodesAnswerAsTheClientSwitch() {
        Assert.That(ItemInventoryAnswer.ExpandDone, Is.EqualTo(new ItemInventoryAnswer(ItemAnswerKind.Box, 0x2a2)));
        Assert.That(ItemInventoryAnswer.Error(0xc, 0, 0), Is.EqualTo(new ItemInventoryAnswer(ItemAnswerKind.Box, 0xeb)));
        Assert.That(ItemInventoryAnswer.Error(0xd, 0, 0), Is.EqualTo(new ItemInventoryAnswer(ItemAnswerKind.Notice, 0x43, Sound: "system_notice_error_warningBag")));
        Assert.That(ItemInventoryAnswer.Error(0x1f, 0, 0).StringId, Is.EqualTo(0x72));
        Assert.That(ItemInventoryAnswer.Error(0x22, 0, 0).Kind, Is.EqualTo(ItemAnswerKind.Service));
        Assert.That(ItemInventoryAnswer.Error(0x23, 0, 0).StringId, Is.EqualTo(0xfd));
        Assert.That(ItemInventoryAnswer.Error(0x24, 0, 0).StringId, Is.EqualTo(0xfe));
        Assert.That(ItemInventoryAnswer.Error(0x25, 0, 0), Is.EqualTo(new ItemInventoryAnswer(ItemAnswerKind.BoxKey, 0, "s_item_err_Invalid_slot_codi")));
        Assert.That(ItemInventoryAnswer.Error(0x26, 0, 0), Is.EqualTo(new ItemInventoryAnswer(ItemAnswerKind.BoxKey, 0, "s_item_err_not_active_tab")));
        Assert.That(ItemInventoryAnswer.Error(0x28, 20000001, 3), Is.EqualTo(new ItemInventoryAnswer(ItemAnswerKind.BoxItem, 0x941, ItemId: 20000001, Number: 3)));
        Assert.That(ItemInventoryAnswer.Error(0x29, 20000001, 3), Is.EqualTo(new ItemInventoryAnswer(ItemAnswerKind.NoticeItem, 0x942, ItemId: 20000001, Number: 3)));
        Assert.That(ItemInventoryAnswer.Error(0x2a, 0, 0).StringId, Is.EqualTo(0xd7));
        Assert.That(ItemInventoryAnswer.Error(17, 0, 0), Is.EqualTo(new ItemInventoryAnswer(ItemAnswerKind.BoxCode, 0xcd, Number: 17)));
    }
}
