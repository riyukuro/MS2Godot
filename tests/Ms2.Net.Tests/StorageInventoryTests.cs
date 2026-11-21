using System.Collections.Generic;
using System.Linq;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

// Inbound 0x22: CStore::Dispatch (client/ms2/store.cpp @0x140de3480) applied to the bank model.
public class StorageInventoryTests {
    static readonly int BodyLen = Measure();
    static int Measure() { var r = new ByteReader(new byte[400]); ItemReader.ReadInventory(r); return r.Position - 18; }

    static void Item(ByteWriter w, int id, long uid, short slot, int amount) {
        w.WriteInt(id); w.WriteLong(uid); w.WriteShort(slot); w.WriteInt(1);
        var body = new byte[BodyLen]; System.BitConverter.GetBytes(amount).CopyTo(body, 0); w.WriteBytes(body);
    }
    static StorageMessage Read(StorageBook book, System.Action<ByteWriter> f) {
        var w = new ByteWriter(); f(w);
        return StorageMessage.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()), book.Id);
    }
    static void Send(StorageBook b, System.Action<ByteWriter> f) => b.Apply(Read(b, f));

    [Test] public void BeginItemsAddRemoveCountAndReady() {
        var b = new StorageBook(); var log = new List<string>();
        b.OpenRequested += () => log.Add("open"); b.BecameReady += () => log.Add("ready");
        b.ItemAdded += i => log.Add("add" + i.Uid); b.ItemRemoved += i => log.Add("rm" + i.Uid); b.ItemChanged += i => log.Add("chg" + i.Uid);
        Send(b, w => { w.WriteByte(4); w.WriteLong(0); w.WriteShort(2); });          // two items follow
        Assert.That(b.Ready, Is.False); Assert.That(b.Received, Is.EqualTo(2));
        Send(b, w => { w.WriteByte(5); w.WriteLong(0); w.WriteShort(2); Item(w, 20000001, 11, 3, 5); Item(w, 20000002, 12, 4, 1); });
        Assert.That(b.Ready, Is.True); Assert.That(b.Received, Is.EqualTo(0)); Assert.That(b.Count, Is.EqualTo(2));
        Assert.That(b.Slot(3).Uid, Is.EqualTo(11)); Assert.That(b.Slot(3).Amount, Is.EqualTo(5));
        Send(b, w => { w.WriteByte(0); w.WriteLong(0); Item(w, 20000003, 13, 9, 2); });
        Send(b, w => { w.WriteByte(9); w.WriteLong(0); w.WriteLong(11); w.WriteInt(8); });
        Send(b, w => { w.WriteByte(1); w.WriteLong(0); w.WriteLong(12); });
        Assert.That(b.Find(12), Is.Null); Assert.That(b.Slot(4), Is.Null); Assert.That(b.Find(11).Amount, Is.EqualTo(8));
        Assert.That(log, Is.EqualTo(new[] { "open", "add11", "add12", "ready", "add13", "chg11", "rm12" }));
    }

    [Test] public void BeginWithNoItemsIsReadyAtOnce() {
        var b = new StorageBook(); int ready = 0; b.BecameReady += () => ready++;
        Send(b, w => { w.WriteByte(4); w.WriteLong(0); w.WriteShort(0); });
        Assert.That(b.Ready, Is.True); Assert.That(ready, Is.EqualTo(1));
    }

    // CStore::Add: a duplicate uid or an occupied slot is refused (0x11), a slot past the capacity too (0xd); slot -1 takes the first empty slot.
    [Test] public void AddRefusalsAndAutoSlot() {
        var b = new StorageBook();
        Send(b, w => { w.WriteByte(8); w.WriteLong(0); w.WriteShort(1); Item(w, 20000001, 11, 1, 5); });
        Send(b, w => { w.WriteByte(0); w.WriteLong(0); Item(w, 20000009, 11, 2, 9); });      // same uid
        Assert.That(b.Find(11).Id, Is.EqualTo(20000001));
        Send(b, w => { w.WriteByte(0); w.WriteLong(0); Item(w, 20000002, 12, 1, 1); });      // slot 1 taken
        Assert.That(b.Find(12), Is.Null);
        Send(b, w => { w.WriteByte(0); w.WriteLong(0); Item(w, 20000003, 13, 36, 1); });     // 36 slots: 0-35
        Assert.That(b.Find(13), Is.Null);
        Send(b, w => { w.WriteByte(0); w.WriteLong(0); Item(w, 20000004, 14, -1, 1); });     // first empty
        Assert.That(b.Find(14).InventorySlot, Is.EqualTo(0));
    }

    [Test] public void SwapMovesBothAndAnEmptySideIsSkipped() {
        var b = new StorageBook(); var chg = new List<long>(); b.ItemChanged += i => chg.Add(i.Uid);
        Send(b, w => { w.WriteByte(8); w.WriteLong(0); w.WriteShort(2); Item(w, 20000001, 11, 1, 1); Item(w, 20000002, 12, 2, 1); });
        Send(b, w => { w.WriteByte(2); w.WriteLong(0); w.WriteLong(11); w.WriteShort(2); w.WriteLong(12); w.WriteShort(1); });
        Assert.That(b.Slot(2).Uid, Is.EqualTo(11)); Assert.That(b.Slot(1).Uid, Is.EqualTo(12));
        Send(b, w => { w.WriteByte(2); w.WriteLong(0); w.WriteLong(0); w.WriteShort(0); w.WriteLong(11); w.WriteShort(7); });
        Assert.That(b.Slot(7).Uid, Is.EqualTo(11)); Assert.That(b.Slot(2), Is.Null);
        Assert.That(chg, Is.EqualTo(new long[] { 11, 12, 11 }));
    }

    [Test] public void CurrencyExtendResetAndClose() {
        var b = new StorageBook(); long meso = -1; b.MesoChanged += m => meso = m;
        int open = 0, close = 0, grown = 0; b.OpenRequested += () => open++; b.CloseReceived += () => close++; b.Expanded += () => grown++;
        Send(b, w => { w.WriteByte(3); w.WriteLong(1234567890123); });
        Assert.That(b.Meso, Is.EqualTo(1234567890123)); Assert.That(meso, Is.EqualTo(1234567890123));
        Assert.That(b.Capacity, Is.EqualTo(36));
        Send(b, w => { w.WriteByte(0xd); w.WriteInt(6); });
        Assert.That(b.Capacity, Is.EqualTo(42)); Assert.That(b.SlotCount, Is.EqualTo(42)); Assert.That(grown, Is.EqualTo(1));
        Send(b, w => { w.WriteByte(8); w.WriteLong(0); w.WriteShort(1); Item(w, 20000001, 11, 41, 1); });
        Assert.That(b.Find(11), Is.Not.Null);
        Send(b, w => w.WriteByte(7)); Assert.That(close, Is.EqualTo(1));
        Send(b, w => w.WriteByte(0xe));                       // Reset: back to 36 slots, empty, not ready, dialog opened
        Assert.That(b.Count, Is.EqualTo(0)); Assert.That(b.Capacity, Is.EqualTo(36)); Assert.That(b.Ready, Is.False); Assert.That(open, Is.EqualTo(1));
        Send(b, w => w.WriteByte(0xb)); Assert.That(b.Capacity, Is.EqualTo(36));
    }

    // Every handler with an id checks it against CStore::Id() (0) and returns with the body unread.
    [Test] public void AnotherStoreIdIsRefused() {
        var b = new StorageBook();
        var m = Read(b, w => { w.WriteByte(0); w.WriteLong(77); w.WriteInt(1); });
        Assert.That(m, Is.EqualTo(new StorageMessage.IdMismatch(0, 77)));
        b.Apply(m); Assert.That(b.Count, Is.EqualTo(0));
        Assert.That(Read(b, w => { w.WriteByte(4); w.WriteLong(5); }), Is.TypeOf<StorageMessage.IdMismatch>());
        Assert.That(Read(b, w => { w.WriteByte(99); }), Is.TypeOf<StorageMessage.Other>());
    }

    [Test] public void TrailingBytesAreAnError() =>
        Assert.Throws<PacketLayoutException>(() => Read(new StorageBook(), w => { w.WriteByte(0xb); w.WriteByte(1); }));

    [Test] public void CountIsClampedToTheMaxStack() {
        var b = new StorageBook { MaxStackOf = _ => 99u };
        Send(b, w => { w.WriteByte(8); w.WriteLong(0); w.WriteShort(1); Item(w, 20000001, 11, 1, 5); });
        Send(b, w => { w.WriteByte(9); w.WriteLong(0); w.WriteLong(11); w.WriteInt(1000); });
        Assert.That(b.Find(11).Amount, Is.EqualTo(99));
        Send(b, w => { w.WriteByte(9); w.WriteLong(0); w.WriteLong(11); w.WriteInt(-1); });
        Assert.That(b.Find(11).Amount, Is.EqualTo(99));
    }

    [Test] public void ErrorCodesMapToTheClientsStrings() {
        var b = new StorageBook(); int got = 0; b.ErrorReceived += c => got = c;
        Send(b, w => { w.WriteByte(0x10); w.WriteInt(0x13); }); Assert.That(got, Is.EqualTo(0x13));
        Assert.That(StorageBook.ErrorText(0x13), Is.EqualTo(new StorageErrorText(StorageErrorKind.Notice, 0x2b2, null)));
        Assert.That(StorageBook.ErrorText(0xe, 60), Is.EqualTo(new StorageErrorText(StorageErrorKind.BoxWithNumber, 0x2b0, 60)));
        Assert.That(StorageBook.ErrorText(0x14).Number, Is.EqualTo(2000000000));
        Assert.That(StorageBook.ErrorText(0x99), Is.EqualTo(new StorageErrorText(StorageErrorKind.Notice, 0x2af, 0x99)));
    }

    [Test] public void AvailableSlotPrefersExistingStacks() {
        var b = new StorageBook { MaxStackOf = _ => 99u };
        Send(b, w => { w.WriteByte(8); w.WriteLong(0); w.WriteShort(1); Item(w, 20000001, 11, 0, 50); });
        var inv = new EquipItem { Id = 20000001, Uid = 5, Amount = 30 };
        Assert.That(b.AvailableSlot(inv, 30), Is.EqualTo(0));      // 49 free in the stack
        Assert.That(b.AvailableSlot(inv, 60), Is.EqualTo(1));      // does not fit: the first empty slot
    }
}
