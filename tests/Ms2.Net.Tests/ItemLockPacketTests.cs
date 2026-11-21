using System.Collections.Generic;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

// Inbound 0xDB and outbound 0x87: CItemLockManager (client unnamed/unit_1406125d0_itemlockmanager.cpp).
public class ItemLockPacketTests {
    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
    static readonly int BodyLen = Measure();
    static int Measure() { var r = new ByteReader(new byte[400]); ItemReader.ReadInventory(r); return r.Position - 18; }

    /// <summary>A body whose lock fields (offsets 40 and 41 of the body) are set.</summary>
    static byte[] Body(byte lockState, long unlock) {
        var b = new byte[BodyLen]; b[40] = lockState; System.BitConverter.GetBytes(unlock).CopyTo(b, 41); return b;
    }

    class Rig {
        public ItemLockBook B = new() { DialogOpen = true };
        public List<IOutPacket> Sent = new();
        public List<(int, int)> Notices = new();
        public List<long> Refreshed = new();
        public int Binds;
        public Dictionary<long, EquipItem> Inv = new();
        public Rig() {
            B.Send = Sent.Add; B.Find = u => Inv.TryGetValue(u, out var i) ? i : null;
            B.CanBeLocked = i => i.Id < 1000;
            B.Notice += (a, b) => Notices.Add((a, b)); B.ItemRefresh += Refreshed.Add; B.BagChanged += () => Binds++;
            B.StoreItem = i => Inv[i.Uid] = i;
        }
        public EquipItem Add(long uid, int id = 1, byte lockState = 0) { var i = new EquipItem { Id = id, Uid = uid, LockState = lockState }; Inv[uid] = i; return i; }
        public ItemLockMessage Read(System.Action<ByteWriter> f) {
            var w = new ByteWriter(); f(w);
            return ItemLockMessage.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()), B.Find);
        }
    }

    [Test] public void TheSendsWriteTheClientsBytes() {
        Assert.That(Bytes(new ItemLockResetRequest()), Is.EqualTo(new byte[] { 0 }));
        var w = new ByteWriter(); w.WriteByte(1); w.WriteByte(1); w.WriteLong(0x1122334455);
        Assert.That(Bytes(new ItemLockSelectRequest(1, 0x1122334455)), Is.EqualTo(w.Buffer.AsSpan(0, w.Length).ToArray()));
        w = new ByteWriter(); w.WriteByte(2); w.WriteLong(9);
        Assert.That(Bytes(new ItemLockRemoveRequest(9)), Is.EqualTo(w.Buffer.AsSpan(0, w.Length).ToArray()));
        Assert.That(Bytes(new ItemLockConfirmRequest(1)), Is.EqualTo(new byte[] { 3, 1 }));
        Assert.That((ushort) Outbound.RequestItemLock, Is.EqualTo(0x87));
        Assert.That((ushort) Inbound.ItemLock, Is.EqualTo(0xDB));
        Assert.That(new ItemLockResetRequest().Op, Is.EqualTo(Outbound.RequestItemLock));
    }

    [Test] public void ModeOnePutsTheItemInItsSlotOnlyForAKnownItem() {
        var t = new Rig(); t.Add(11);
        t.B.Apply(t.Read(w => { w.WriteByte(1); w.WriteLong(11); w.WriteShort(4); }));
        Assert.That(t.B[4], Is.EqualTo(11)); Assert.That(t.B.HasLockedItem, Is.True); Assert.That(t.Binds, Is.EqualTo(1));
        Assert.That(t.Refreshed, Is.EqualTo(new long[] { 11 }));
        t.B.Apply(t.Read(w => { w.WriteByte(1); w.WriteLong(99); w.WriteShort(5); }));      // unknown uid: no slot, still refreshed
        Assert.That(t.B[5], Is.EqualTo(0)); Assert.That(t.Binds, Is.EqualTo(1)); Assert.That(t.Refreshed, Is.EqualTo(new long[] { 11, 99 }));
        t.B.Apply(t.Read(w => { w.WriteByte(1); w.WriteLong(11); w.WriteShort(0x12); }));   // slot out of range: nothing at all
        Assert.That(t.Refreshed.Count, Is.EqualTo(2));
    }

    [Test] public void PacketsAreIgnoredWithoutTheDialog() {
        var t = new Rig(); t.Add(11); t.B.DialogOpen = false;
        t.B.Apply(t.Read(w => { w.WriteByte(1); w.WriteLong(11); w.WriteShort(4); }));
        Assert.That(t.B.HasLockedItem, Is.False); Assert.That(t.Binds, Is.EqualTo(0));
    }

    [Test] public void ModeTwoRemovesAndModeThreeRereadsTheBodiesAndRemoves() {
        var t = new Rig(); t.Add(11); t.Add(12, 5);
        t.B.Apply(t.Read(w => { w.WriteByte(1); w.WriteLong(11); w.WriteShort(0); }));
        t.B.Apply(t.Read(w => { w.WriteByte(1); w.WriteLong(12); w.WriteShort(1); }));
        t.B.Apply(t.Read(w => { w.WriteByte(2); w.WriteLong(11); }));
        Assert.That(t.B[0], Is.EqualTo(0)); Assert.That(t.B[1], Is.EqualTo(12));
        t.B.Apply(t.Read(w => { w.WriteByte(3); w.WriteByte(1); w.WriteLong(12); w.WriteBytes(Body(1, 77)); }));
        Assert.That(t.B.HasLockedItem, Is.False);
        Assert.That((t.Inv[12].LockState, t.Inv[12].UnlockTime, t.Inv[12].Id), Is.EqualTo(((byte) 1, 77L, 5)));
        Assert.That(t.Refreshed, Is.EqualTo(new long[] { 11, 12, 11, 12 }));
    }

    [Test] public void ModeFourNoticesByCode() {
        var t = new Rig();
        t.B.Apply(t.Read(w => { w.WriteByte(4); w.WriteInt(1); }));
        t.B.Apply(t.Read(w => { w.WriteByte(4); w.WriteInt(3); }));
        t.B.Apply(t.Read(w => { w.WriteByte(4); w.WriteInt(9); }));
        Assert.That(t.Notices, Is.EqualTo(new[] { (0xc45, 0), (0x43, 0), (0xc45, 9) }));
    }

    [Test] public void SelectRunsTheTabsChecksAndSendsNothingWhenRefused() {
        var t = new Rig(); var ok = t.Add(21, 1); var big = t.Add(22, 5000); var locked = t.Add(23, 1, 1);
        Assert.That(t.B.Select(big), Is.False);                       // mode 0: CanBeItemLocked false
        t.Sent.Clear(); t.Notices.Clear();
        Assert.That(t.B.Select(big), Is.False); Assert.That(t.Sent, Is.Empty); Assert.That(t.Notices, Is.EqualTo(new[] { (0xc46, 0) }));
        Assert.That(t.B.Select(ok), Is.True);
        Assert.That(Bytes(t.Sent[0]), Is.EqualTo(new byte[] { 1, 0, 21, 0, 0, 0, 0, 0, 0, 0 }));
        t.B.Apply(t.Read(w => { w.WriteByte(1); w.WriteLong(21); w.WriteShort(0); }));
        t.Sent.Clear(); t.Notices.Clear();
        Assert.That(t.B.Select(ok), Is.False); Assert.That(t.Sent, Is.Empty); Assert.That(t.Notices, Is.EqualTo(new[] { (ItemLockBook.SelectedNotice, 0) }));
        t.B.Mode = 1; t.Notices.Clear();
        Assert.That(t.B.Select(big), Is.False); Assert.That(t.Notices, Is.EqualTo(new[] { (0xc47, 0) }));   // mode 1: not locked
        Assert.That(t.B.Select(locked), Is.True);
        Assert.That(Bytes(t.Sent[0]), Is.EqualTo(new byte[] { 1, 1, 23, 0, 0, 0, 0, 0, 0, 0 }));
        Assert.That(t.B.Select(null), Is.False);
    }

    [Test] public void ResetClearsTheBagAndSends() {
        var t = new Rig(); t.Add(11);
        t.B.Apply(t.Read(w => { w.WriteByte(1); w.WriteLong(11); w.WriteShort(3); }));
        t.B.SendReset();
        Assert.That(t.B.HasLockedItem, Is.False); Assert.That(Bytes(t.Sent[0]), Is.EqualTo(new byte[] { 0 }));
    }

    [Test] public void MissingItemsAreAskedOnceAndConfirmSendsTheTab() {
        var t = new Rig(); t.Add(11);
        t.B.Apply(t.Read(w => { w.WriteByte(1); w.WriteLong(11); w.WriteShort(2); }));
        t.B.RequestUnlockMissing(); Assert.That(t.Sent, Is.Empty);    // the item is there
        t.Inv.Clear();
        t.B.RequestUnlockMissing(); t.B.RequestUnlockMissing();
        Assert.That(t.Sent.Count, Is.EqualTo(1)); Assert.That(Bytes(t.Sent[0]), Is.EqualTo(new byte[] { 2, 11, 0, 0, 0, 0, 0, 0, 0 }));
        t.Sent.Clear(); t.B.Mode = 1; t.B.Confirm();
        Assert.That(Bytes(t.Sent[0]), Is.EqualTo(new byte[] { 3, 1 }));
        t.B.Remove(5); Assert.That(Bytes(t.Sent[1]), Is.EqualTo(new byte[] { 2, 5, 0, 0, 0, 0, 0, 0, 0 }));
    }

    [Test] public void CanAddFollowsTheTabAndTheBag() {
        var t = new Rig(); var a = t.Add(31, 1); var l = t.Add(32, 1, 1);
        Assert.That(t.B.CanAdd(a), Is.True); Assert.That(t.B.CanAdd(l), Is.True);   // CanBeLocked here only tests the id
        t.B.Apply(t.Read(w => { w.WriteByte(1); w.WriteLong(31); w.WriteShort(0); }));
        Assert.That(t.B.CanAdd(a), Is.False);
        t.B.Mode = 1; Assert.That(t.B.CanAdd(l), Is.True); Assert.That(t.B.CanAdd(t.Add(33, 1)), Is.False);
    }
}
