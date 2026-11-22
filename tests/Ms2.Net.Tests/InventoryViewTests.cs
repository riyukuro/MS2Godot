using System.Linq;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class InventoryViewTests {
    sealed class Rec : IInventoryBinding {
        public List<string> Log = new();
        public void initData(int b, bool d) => Log.Add($"init {b} {d}");
        public void updateButtonState(InventoryButtonState s) => Log.Add("state");
        public void setNew(int b, bool n) => Log.Add($"new {b} {n}");
        public void m_expand(string t) => Log.Add("expand " + t);
        public void bindSlotBag(int tab, IReadOnlyList<BagCell> c) => Log.Add($"bag {tab} {c.Count} used {c.Count(x => !x.Empty)}");
        public void refreshSlot(int tab, int slot, BagCell c) => Log.Add($"slot {tab} {slot} {c.Uid}");
    }

    static EquipItem It(int id, long uid, short slot, int amount = 1) => new() { Id = id, Uid = uid, InventorySlot = slot, Amount = amount };
    // tab = id / 10000000, capacity 10 + extra
    static ItemBook Book() => new() { TabOf = id => id / 10000000, BaseSlots = _ => 10 };

    [Test] public void CountOfSumsTheAmountsOfOneIdAndRarityAcrossTabsWithoutAllocating() {
        var b = Book();
        var x = It(20000001, 1, 0, 5); x.Rarity = 2; b.TryAdd(x);
        var y = It(20000001, 2, 1, 3); y.Rarity = 2; b.TryAdd(y);
        var z = It(20000001, 3, 2, 7); z.Rarity = 1; b.TryAdd(z);
        Assert.That(b.CountOf(20000001, 2), Is.EqualTo(8));
        Assert.That(b.CountOf(20000001, 1), Is.EqualTo(7));
        Assert.That(b.CountOf(20000002, 2), Is.EqualTo(0));
        for (int i = 0; i < 100; i++) b.CountOf(20000001, 2);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) b.CountOf(20000001, 2);
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(256));
    }

    [Test] public void CellsAreOrderedByInventorySlotAndCapacityIsBasePlusExtra() {
        var b = Book(); b.TryAdd(It(20000001, 1, 3, 5)); b.TryAdd(It(20000002, 2, 0));
        var v = new InventoryView(b);
        Assert.That(v.Capacity(2), Is.EqualTo(10)); Assert.That(v.Cells(2)[3], Is.EqualTo(new BagCell(1, 20000001, 5)));
        Assert.That(v.Cells(2)[0].Uid, Is.EqualTo(2)); Assert.That(v.ExpandLabel(2), Is.EqualTo("2/10")); Assert.That(v.ExpandLabel(3), Is.EqualTo("0/10"));
        b.Apply(new ItemMessage.ExpandCount(2, 6));
        Assert.That(v.Capacity(2), Is.EqualTo(16)); Assert.That(v.Cells(2).Count, Is.EqualTo(16)); Assert.That(v.ExpandLabel(2), Is.EqualTo("2/16"));
        Assert.That(v.Capacity(15), Is.EqualTo(0));
    }

    [Test] public void IncomingSlotIsZeroWithStackRoomElseTheFirstEmptyCell() {
        var b = Book(); b.MaxStackOf = _ => 10;
        b.TryAdd(It(20000001, 1, 0, 8)); b.TryAdd(It(20000002, 2, 1));
        var v = new InventoryView(b);
        Assert.That(v.FindEmptySlot(2), Is.EqualTo(2));
        Assert.That(v.StackRoom(2, It(20000001, 9, 0)), Is.EqualTo(2));
        Assert.That(v.SlotForIncoming(2, It(20000001, 9, 0), 2), Is.EqualTo(0), "fits the stack");
        Assert.That(v.SlotForIncoming(2, It(20000001, 9, 0), 3), Is.EqualTo(2), "does not fit: the empty cell");
        b.MaxStackOf = _ => 1;
        Assert.That(v.StackRoom(2, It(20000001, 9, 0)), Is.EqualTo(0));
        Assert.That(v.FindEmptySlot(15), Is.EqualTo(-1));
    }

    [Test] public void NewFlagIsSetByNotifyNewAndClearedByTheClientPaths() {
        var b = Book(); b.TryAdd(It(20000001, 1, 0)); b.TryAdd(It(30000001, 2, 0));
        var v = new InventoryView(b); var r = new Rec(); v.Bind(r, 4, true);
        v.Update(); r.Log.Clear();
        b.Apply(new ItemMessage.NotifyNew(1, 0, ""));
        Assert.That(b.Get(1).IsNew, Is.True); Assert.That(v.HasNew(2), Is.True); Assert.That(v.HasNew(3), Is.False);
        v.Update();
        Assert.That(r.Log, Does.Contain("new 2 True")); Assert.That(r.Log, Does.Contain("new 3 False"));
        r.Log.Clear(); v.ClearNew(1); v.Update();
        Assert.That(b.Get(1).IsNew, Is.False); Assert.That(r.Log, Does.Contain("new 2 False"));
        b.Apply(new ItemMessage.NotifyNew(2, 0, "")); v.ClearNewInTab(3); Assert.That(v.HasNew(3), Is.False);
        b.Apply(new ItemMessage.NotifyNew(99, 0, "")); // unknown uid: nothing
        r.Log.Clear(); v.TabButtons = new[] { 0, 1, -1, 7 }; b.Apply(new ItemMessage.NotifyNew(1, 0, "")); v.Update();
        Assert.That(r.Log, Does.Not.Contain("new 2 True")); Assert.That(r.Log.Where(l => l.StartsWith("new")).Count(), Is.EqualTo(3));
    }

    [Test] public void BindSelectsTheTabsButton() {
        var b = Book(); var v = new InventoryView(b) { TabButtons = new[] { 0, 1, 2, 3, 6, 7, 8, 14, 5, 9, 11, 12, 13, 10, 4 } }; var r = new Rec();
        v.Bind(r, 14); Assert.That(v.Tab, Is.EqualTo(14)); Assert.That(r.Log, Is.EqualTo(new[] { "init 4 False" }));
        Assert.That(v.TabButton(15), Is.EqualTo(0)); Assert.That(v.TabButton(-1), Is.EqualTo(0));
    }

    [Test] public void SingleItemRefreshesOneSlotAndWholeTabRebuildsOnLoadResetExpand() {
        var b = Book(); var v = new InventoryView(b); var r = new Rec(); v.Bind(r, 2, false); v.Update();
        Assert.That(r.Log.Take(1), Is.EqualTo(new[] { "init 2 False" }));
        v.SelectTab(2); v.Update(); r.Log.Clear();
        b.Apply(new ItemMessage.Add(It(20000001, 5, 4)));
        Assert.That(r.Log, Is.EqualTo(new[] { "slot 2 4 5" }));
        v.Update(); Assert.That(r.Log.Last(), Is.EqualTo("expand 1/10")); Assert.That(r.Log.Any(l => l.StartsWith("bag")), Is.False);
        r.Log.Clear(); b.Apply(new ItemMessage.Remove(5)); Assert.That(r.Log, Is.EqualTo(new[] { "slot 2 4 0" }));
        r.Log.Clear(); b.Apply(new ItemMessage.Add(It(30000001, 6, 1))); Assert.That(r.Log, Is.Empty);   // another tab: no slot refresh on the shown tab
        b.Apply(new ItemMessage.Reset(2)); v.Update(); Assert.That(r.Log, Does.Contain("bag 2 10 used 0"));
        r.Log.Clear(); b.Apply(new ItemMessage.ExpandCount(2, 2)); v.Update(); Assert.That(r.Log, Does.Contain("bag 2 12 used 0")); Assert.That(r.Log, Does.Contain("expand 0/12"));
    }

    [Test] public void TabSwitchSendsNothingAndSortSendsTheShownTab() {
        var b = Book(); var v = new InventoryView(b); var sent = new List<(int, bool)>(); v.RequestSort = (t, s) => sent.Add((t, s));
        b.Apply(new ItemMessage.Reset(1)); b.Apply(new ItemMessage.ExpandCount(1, 1)); v.Bind(new Rec());
        Assert.That(sent, Is.Empty);
        v.SelectTab(3); v.SelectTab(99); Assert.That(sent, Is.Empty); Assert.That(v.Tab, Is.EqualTo(3));
        Assert.That(v.ClickSort(), Is.EqualTo(InventoryView.SortClick.Nothing), "an empty tab sends nothing"); Assert.That(sent, Is.Empty);
        b.Apply(new ItemMessage.Add(It(30000001, 1, 0)));
        Assert.That(v.ClickSort(), Is.EqualTo(InventoryView.SortClick.Sent)); Assert.That(sent, Is.EqualTo(new[] { (3, false) }));
        var w = new ByteWriter(); new ItemInventorySortRequest(3, true).Write(w);
        Assert.That(w.Buffer.AsSpan(0, w.Length).ToArray(), Is.EqualTo(new byte[] { 8, 3, 1 })); Assert.That(new ItemInventorySortRequest(3, true).Op, Is.EqualTo(Outbound.RequestItemInventory));
        Assert.That((ushort) Outbound.RequestItemInventory, Is.EqualTo(0x17));
    }

    [Test] public void TheMoveRequestIsTheClients() {
        var w = new ByteWriter(); new ItemInventoryMoveRequest(0x0102030405060708, 0x0a0b).Write(w);
        Assert.That(w.Buffer.AsSpan(0, w.Length).ToArray(), Is.EqualTo(new byte[] { 3, 8, 7, 6, 5, 4, 3, 2, 1, 0x0b, 0x0a }));
        Assert.That(new ItemInventoryMoveRequest(1, 2).Op, Is.EqualTo(Outbound.RequestItemInventory));
    }

    [Test] public void TheEquipRequestsAreTheClients() {
        var w = new ByteWriter(); new ItemEquipRequest(0x0102030405060708, 5, 2).Write(w);
        Assert.That(w.Buffer.AsSpan(0, w.Length).ToArray(), Is.EqualTo(new byte[] { 0, 8, 7, 6, 5, 4, 3, 2, 1, 5, 2 }));
        w = new ByteWriter(); new ItemUnequipRequest(0x0102030405060708).Write(w);
        Assert.That(w.Buffer.AsSpan(0, w.Length).ToArray(), Is.EqualTo(new byte[] { 1, 8, 7, 6, 5, 4, 3, 2, 1 }));
        Assert.That(new ItemEquipRequest(1, 2, 0).Op, Is.EqualTo(Outbound.ItemEquip));
        Assert.That((ushort)Outbound.ItemEquip, Is.EqualTo(0x16));
    }

    [Test] public void AnEmptyCellIsLookedForInPairs() {
        var b = new ItemBook { TabOf = id => id / 10000000, BaseSlots = _ => 3 };
        var v = new InventoryView(b);
        Assert.That(v.HasEmptyCell(3), Is.True);
        b.Apply(new ItemMessage.Add(It(30000001, 1, 0))); b.Apply(new ItemMessage.Add(It(30000002, 2, 2)));
        Assert.That(v.HasEmptyCell(3), Is.False, "three cells: the middle one is not looked at");
        Assert.That((v.HasEmptyCell(-1), v.HasEmptyCell(15)), Is.EqualTo((false, false)));
        Assert.That(v.TabOfItem(30000001), Is.EqualTo(3));
    }

    [Test] public void TheUseRequestIsTheClients() {
        var w = new ByteWriter(); new ItemUseRequest(0x0102030405060708, "ab").Write(w);
        Assert.That(w.Buffer.AsSpan(0, w.Length).ToArray(), Is.EqualTo(new byte[] { 8, 7, 6, 5, 4, 3, 2, 1, 2, 0, (byte)'a', 0, (byte)'b', 0 }));
        Assert.That((ushort)new ItemUseRequest(1, "").Op, Is.EqualTo(0x19));
    }

    [Test] public void SortWithAnExpiredItemWaitsForTheBoxAnswer() {
        var b = Book(); var v = new InventoryView(b) { ServerTime = () => 1000 }; var sent = new List<(int, bool)>(); v.RequestSort = (t, s) => sent.Add((t, s));
        b.Apply(new ItemMessage.Add(It(30000001, 1, 0))); b.Apply(new ItemMessage.Add(new EquipItem { Id = 30000002, Uid = 2, InventorySlot = 1, Amount = 1, ExpiryTime = 2000 }));
        b.Apply(new ItemMessage.Add(new EquipItem { Id = 20000002, Uid = 3, InventorySlot = 0, Amount = 1, ExpiryTime = 1000 }));   // expired, but on tab 2
        v.Bind(new Rec(), 3);
        Assert.That(v.CountExpired(), Is.Zero, "expiry after now, and 0 = none");
        Assert.That(v.ClickSort(), Is.EqualTo(InventoryView.SortClick.Sent));
        b.Apply(new ItemMessage.Add(new EquipItem { Id = 30000003, Uid = 4, InventorySlot = 2, Amount = 1, ExpiryTime = 1000 }));
        sent.Clear();
        Assert.That(v.CountExpired(), Is.EqualTo(1));
        Assert.That(v.ClickSort(), Is.EqualTo(InventoryView.SortClick.Confirm)); Assert.That(sent, Is.Empty);
        v.OnSortConfirm(0); v.OnSortConfirm(1);
        Assert.That(sent, Is.EqualTo(new[] { (3, true), (3, false) }));
    }

    [Test] public void IconPathFollowsCItemDataGetIconPath() {
        Assert.That(ItemIcon.Path(20000001, ""), Is.EqualTo("./Data/Resource/Image/item/icon/20000001.png"));
        Assert.That(ItemIcon.Path(1, null), Is.EqualTo("./Data/Resource/Image/item/icon/1.png"));
        Assert.That(ItemIcon.Path(1, "abc/x"), Is.EqualTo("./Data/Resource/Image/item/icon/abc/x.png"));
        Assert.That(ItemIcon.Path(1, "./Data/a.png"), Is.EqualTo("./Data/a.png"));
    }
}
