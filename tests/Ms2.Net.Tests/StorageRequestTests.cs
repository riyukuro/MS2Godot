using System.Collections.Generic;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

// Outbound 0x18: the bank's sends and the checks the client runs before each (client/ms2/store.cpp, uistatuseventlistener.cpp).
public class StorageRequestTests {
    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
    static byte[] Expect(System.Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    class Rig {
        public StorageBook Book = new();
        public StorageSender S;
        public List<IOutPacket> Sent = new();
        public List<StorageErrorText> Notices = new();
        public Dictionary<long, EquipItem> Inv = new();
        public long InvMeso = 1000;
        public Rig() {
            S = new StorageSender(Book) {
                Send = Sent.Add, Notify = Notices.Add, InventoryMeso = () => InvMeso,
                FindInventoryItem = u => Inv.TryGetValue(u, out var i) ? i : null, DepositMax = 500, WithdrawMax = 400,
            };
        }
    }

    [Test] public void EveryModeWritesTheClientsBytes() {
        Assert.That(Bytes(new StorageDepositRequest(0, 7, 3, 5)), Is.EqualTo(Expect(w => { w.WriteByte(0); w.WriteLong(0); w.WriteLong(7); w.WriteShort(3); w.WriteInt(5); })));
        Assert.That(Bytes(new StorageWithdrawRequest(0, 7, 3, 5)), Is.EqualTo(Expect(w => { w.WriteByte(1); w.WriteLong(0); w.WriteLong(7); w.WriteShort(3); w.WriteInt(5); })));
        Assert.That(Bytes(new StorageMoveRequest(0, 7, 9)), Is.EqualTo(Expect(w => { w.WriteByte(2); w.WriteLong(0); w.WriteLong(7); w.WriteShort(9); })));
        Assert.That(Bytes(new StorageMesoRequest(0, 1, 250)), Is.EqualTo(Expect(w => { w.WriteByte(3); w.WriteLong(0); w.WriteByte(1); w.WriteLong(250); })));
        Assert.That(Bytes(new StorageExtendRequest(0)), Is.EqualTo(Expect(w => { w.WriteByte(6); w.WriteLong(0); })));
        Assert.That(Bytes(new StorageCloseRequest(0)), Is.EqualTo(Expect(w => { w.WriteByte(8); w.WriteLong(0); })));
        Assert.That(Bytes(new StorageLoadRequest(0)), Is.EqualTo(Expect(w => { w.WriteByte(0xc); w.WriteLong(0); })));
        Assert.That(Bytes(new StorageSortRequest()), Is.EqualTo(new byte[] { 0xf }));
        Assert.That(new StorageSortRequest().Op, Is.EqualTo(Outbound.RequestItemStorage));
        Assert.That((ushort) Outbound.RequestItemStorage, Is.EqualTo(0x18));
    }

    [Test] public void DepositNeedsTheInventoryItemAndRefusesTheBoundScoreAndSummonedPet() {
        var r = new Rig(); var bound = new List<bool>();
        r.S.BindNotice = bound.Add;
        r.S.IsScoreItem = id => id == 1; r.S.PlayingScoreUid = () => 11;
        r.S.IsPetItem = id => id == 2; r.S.SummonUid = () => 12;
        r.Inv[10] = new EquipItem { Id = 5, Uid = 10, Amount = 3 };
        r.Inv[11] = new EquipItem { Id = 1, Uid = 11, Amount = 1 };
        r.Inv[12] = new EquipItem { Id = 2, Uid = 12, Amount = 1 };
        Assert.That(r.S.Deposit(99, 0, 1), Is.False);            // not in the inventory
        Assert.That(r.S.Deposit(11, 0, 1), Is.False);            // the played score
        Assert.That(r.S.Deposit(12, 0, 1), Is.False);            // the summoned pet
        Assert.That(r.Sent, Is.Empty);
        Assert.That(bound, Is.EqualTo(new[] { true, false }));
        Assert.That(r.S.Deposit(10, 4, 3), Is.True);
        Assert.That(Bytes(r.Sent[0]), Is.EqualTo(Bytes(new StorageDepositRequest(0, 10, 4, 3))));
        Assert.That(r.Book.Count, Is.EqualTo(0));                 // nothing changes before the server answers
    }

    [Test] public void WithdrawStopsAtAnItemTheCharacterCannotMove() {
        var r = new Rig();
        r.Book.TryAdd(new EquipItem { Id = 5, Uid = 20, Amount = 2, InventorySlot = 1 });
        r.S.CanMove = _ => false;
        Assert.That(r.S.Withdraw(20, 0, 2), Is.False);
        Assert.That(r.Sent, Is.Empty);
        r.S.CanMove = _ => true;
        Assert.That(r.S.Withdraw(20, 6, 2), Is.True);
        Assert.That(Bytes(r.Sent[0]), Is.EqualTo(Bytes(new StorageWithdrawRequest(0, 20, 6, 2))));
        Assert.That(r.Book.Find(20), Is.Not.Null);
    }

    [Test] public void MovingASlotOntoItselfOrAnEmptySlotSendsNothing() {
        var r = new Rig();
        r.Book.TryAdd(new EquipItem { Id = 5, Uid = 20, Amount = 2, InventorySlot = 1 });
        Assert.That(r.S.MoveSlot(1, 1), Is.False);
        Assert.That(r.S.MoveSlot(2, 3), Is.False);
        Assert.That(r.Sent, Is.Empty);
        Assert.That(r.S.MoveSlot(1, 3), Is.True);
        Assert.That(Bytes(r.Sent[0]), Is.EqualTo(Bytes(new StorageMoveRequest(0, 20, 3))));
        Assert.That(r.Book.Slot(1), Is.Not.Null);
    }

    [Test] public void DepositMesosRefusesEachCaseWithItsNotice() {
        var r = new Rig();
        Assert.That(r.S.DepositMesos(2000), Is.False);            // more than the inventory holds
        Assert.That(r.S.DepositMesos(0), Is.False);
        Assert.That(r.S.DepositMesos(-5), Is.False);
        Assert.That(r.S.DepositMesos(600), Is.False);             // over DepositMax
        r.InvMeso = 5_000_000_000; r.S.DepositMax = long.MaxValue;
        r.Book.Apply(new StorageMessage.Currency(1_999_999_990));
        Assert.That(r.S.DepositMesos(11), Is.False);              // the bank would pass 2e9
        Assert.That(r.Sent, Is.Empty);
        Assert.That(r.Notices.ConvertAll(n => (n.StringId, n.Number)), Is.EqualTo(new (int, long?)[] {
            (0x2b6, null), (0x2b2, null), (0xee, 500), (0xee, 500), (0x2b3, 2_000_000_000) }));
        Assert.That(r.S.DepositMesos(10), Is.True);
        Assert.That(Bytes(r.Sent[0]), Is.EqualTo(Bytes(new StorageMesoRequest(0, 1, 10))));
        Assert.That(r.Book.Meso, Is.EqualTo(1_999_999_990));
    }

    [Test] public void WithdrawMesosRefusesEachCaseWithItsNotice() {
        var r = new Rig();
        r.Book.Apply(new StorageMessage.Currency(300));
        Assert.That(r.S.WithdrawMesos(301), Is.False);
        Assert.That(r.S.WithdrawMesos(0), Is.False);
        Assert.That(r.S.WithdrawMesos(-1), Is.False);
        Assert.That(r.Sent, Is.Empty);
        Assert.That(r.Notices.ConvertAll(n => (n.StringId, n.Number)), Is.EqualTo(new (int, long?)[] { (0x2b5, null), (0x2b4, null), (0xee, 300) }));
        Assert.That(r.S.WithdrawMesos(300), Is.True);
        Assert.That(Bytes(r.Sent[0]), Is.EqualTo(Bytes(new StorageMesoRequest(0, 0, 300))));
        Assert.That(r.Book.Meso, Is.EqualTo(300));
    }

    [Test] public void TheMesoButtonsNeedALivePlayer() {
        var r = new Rig();
        Assert.That(r.S.CanStartMeso(), Is.True);
        r.S.PlayerAlive = () => false;
        Assert.That(r.S.CanStartMeso(), Is.False);
    }

    [Test] public void ExpandSendsModeSixOrOpensTheServiceDialog() {
        var r = new Rig(); int service = 0;
        r.S.ServiceRequested += () => service++;
        r.S.ExpandSlots();
        Assert.That(Bytes(r.Sent[0]), Is.EqualTo(Bytes(new StorageExtendRequest(0))));
        r.S.PlatformCanBuy = () => false;
        r.S.ExpandSlots();
        Assert.That(r.Sent, Has.Count.EqualTo(1)); Assert.That(service, Is.EqualTo(1));
    }

    [Test] public void LoadCloseAndSortSend() {
        var r = new Rig();
        r.S.RequestLoad(); r.S.RequestClose(); r.S.RequestSort();
        Assert.That(r.Sent.ConvertAll(Bytes), Is.EqualTo(new[] { Bytes(new StorageLoadRequest(0)), Bytes(new StorageCloseRequest(0)), new byte[] { 0xf } }));
    }
}
