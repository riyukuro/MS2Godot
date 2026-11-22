using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The bank dialog (uistoredialog) shown and filled from the store (needs MS2_DATA): initData's meso, the bag's slot
/// bindings by capacity, the "m_expand" count text, the item events, the name search and the idle per-frame cost. Texts
/// are not compared, so no language's strings.</summary>
public class StoreDialogTests
{
    static PackSet packs;
    static MovieSource src;

    static PackSet Packs()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        }
        return packs;
    }

    static UiModel NewModel()
    {
        var m = new UiModel(new GameTables(Packs(), "en")) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(Packs(), f) };
        m.HasLocalPlayer = true;
        return m;
    }

    static UiHost Load(UiController c, UiModel model)
    {
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => k.TrimStart('$');
        var h = new UiHost(p, c, model);
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 10; i++) p.Tick();
        return h;
    }

    static EquipItem Item(int id, long uid, short slot, int amount = 1) => new() { Id = id, Uid = uid, InventorySlot = slot, Amount = amount };

    static StorageBook NewBook(long meso = 0)
    {
        var b = new StorageBook();
        b.Apply(new StorageMessage.Begin(0, 0));
        b.Apply(new StorageMessage.Currency(meso));
        return b;
    }

    static int RealId(UiModel model)
    {
        int id = new[] { 20000001, 20000002, 30000001, 11300001, 12000001 }.FirstOrDefault(i => !string.IsNullOrEmpty(model.Tables.ItemName(i)));
        if (id == 0) Assert.Ignore("no known item");
        return id;
    }

    static string Digits(string s) => new(s.Where(char.IsDigit).ToArray());

    [Test]
    public void TheMovieIsFilledFromTheStore()
    {
        Packs();
        var book = NewBook(1234567);
        book.Apply(new StorageMessage.Add(0, Item(20000001, 1, 0)));
        book.Apply(new StorageMessage.Add(0, Item(20000002, 2, 5)));
        var c = new StoreDialogController { Book = book };
        var h = Load(c, NewModel());
        c.Process();
        Assert.That(Digits(Rt.ToString(h.Get(h.Child("m_meso"), "text"))), Is.EqualTo("1234567"), "initData's meso");
        Assert.That(Rt.ToString(h.Get(h.Child("m_expand"), "text")), Is.EqualTo("2/36"), "count/capacity");
        Assert.That(c.Pending, Is.EqualTo(0));
    }

    [Test]
    public void TheBagHasOneSlotPerCapacityAndOnlyFilledSlotsBind()
    {
        Packs();
        var book = NewBook();
        var model = NewModel();
        book.Apply(new StorageMessage.Add(0, Item(RealId(model), 1, 3)));
        var c = new StoreDialogController { Book = book };
        Load(c, model);
        Assert.That(c.GetBagSlotBindings(StoreDialogController.Bag).Count(), Is.EqualTo(36));
        Assert.That(c.GetBinding(StoreDialogController.SlotName(3)), Is.Not.Null);
        Assert.That(c.GetBinding(StoreDialogController.SlotName(4)), Is.Null);
        book.Apply(new StorageMessage.Extend(6));
        Assert.That(c.GetBagSlotBindings(StoreDialogController.Bag).Count(), Is.EqualTo(42));
        Assert.That(c.Pending & StoreDialogController.PendingBag, Is.Not.EqualTo(0), "an expansion rebuilds the bag");
        c.Process();
        var h = c.Host;
        Assert.That(Rt.ToString(h.Get(h.Child("m_expand"), "text")), Is.EqualTo("1/42"));
    }

    [Test]
    public void ItemEventsRefreshTheCountText()
    {
        Packs();
        var book = NewBook();
        var c = new StoreDialogController { Book = book };
        var h = Load(c, NewModel());
        c.Process();
        book.Apply(new StorageMessage.Add(0, Item(20000001, 1, 0)));
        Assert.That(c.Pending, Is.EqualTo(StoreDialogController.PendingText));
        c.Process();
        Assert.That(Rt.ToString(h.Get(h.Child("m_expand"), "text")), Is.EqualTo("1/36"));
        book.Apply(new StorageMessage.Remove(0, 1));
        c.Process();
        Assert.That(Rt.ToString(h.Get(h.Child("m_expand"), "text")), Is.EqualTo("0/36"));
        book.Apply(new StorageMessage.Currency(77));
        Assert.That(Digits(Rt.ToString(h.Get(h.Child("m_meso"), "text"))), Is.EqualTo("77"));
    }

    [Test]
    public void BuyingSlotsExtendsThenPlaysTheExpandEffectAndKeepsTheDialogOpen()
    {
        var w = Rig();
        Load(w.C, w.Model);
        int closed = 0, effects = 0;
        w.C.Closed += () => closed++;
        w.C.ExpandEffect += () => effects++;
        w.Sent.Clear();
        w.Book.Apply(new StorageMessage.Extend(6));      // OnExtend: only the size changes, no dialog event
        Assert.That(effects, Is.EqualTo(0));
        w.Book.Apply(new StorageMessage.Close());        // type 7: the expand effect, not a close
        Assert.That(effects, Is.EqualTo(1));
        Assert.That(closed, Is.EqualTo(0), "the server's slot-purchase confirmation does not close the bank");
        Assert.That(w.Sent, Is.Empty, "and sends nothing (no sort)");
    }

    [Test]
    public void TheSearchScrollsToTheItemAndABoxAnswersNoMatch()
    {
        Packs();
        var model = NewModel();
        int id = RealId(model);
        var book = NewBook();
        book.Apply(new StorageMessage.Add(0, Item(id, 1, 20)));
        var boxes = new List<int>();
        var c = new StoreDialogController { Book = book, ShowBox = (_, t) => boxes.Add(t) };
        Load(c, model);
        Assert.That(c.FindSlotIndexByItemName("  " + model.Tables.ItemName(id).ToUpperInvariant() + " "), Is.EqualTo(20), "lowercased, trimmed");
        c.OnCallback("OnItemSearch", new object[] { "no such item name ☃" });
        Assert.That(boxes, Is.EqualTo(new[] { 0 }));
        boxes.Clear();
        c.OnCallback("OnItemSearch", new object[] { model.Tables.ItemName(id) });
        Assert.That(boxes, Is.Empty);
    }

    [Test]
    public void IdleProcessAllocatesNothing()
    {
        Packs();
        var c = new StoreDialogController { Book = NewBook() };
        Load(c, NewModel());
        for (int i = 0; i < 5000; i++) c.Process();     // past the tiered JIT's call counting
        // 100000 calls: one allocating call would cost 2.4 MB at the least; the Release runtime charges the thread a
        // constant (24 bytes) at some point in any long loop, so the bound is a few hundred bytes
        long a = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100_000; i++) c.Process();
        long window = GC.GetAllocatedBytesForCurrentThread() - a;
        Assert.That(window, Is.LessThan(256), "Process allocates per call");
    }

    // ---- the sends -------------------------------------------------------------------------------------------------

    sealed class Wire
    {
        public readonly List<Ms2.Net.Wire.IOutPacket> Sent = new();
        public readonly List<string> Notices = new();
        public readonly List<MessageBoxData> Boxes = new();
        public StoreDialogController C;
        public StorageSender Sender;
        public StorageBook Book;
        public UiModel Model;
        public long InvMeso = 1000;
        public EquipItem InvItem;
    }

    static Wire Rig(long meso = 0, bool ready = true)
    {
        Packs();
        var w = new Wire { Model = NewModel() };
        w.Book = ready ? NewBook(meso) : new StorageBook();
        w.Book.MaxStackOf = _ => 99;
        w.Sender = new StorageSender(w.Book) { Send = w.Sent.Add, InventoryMeso = () => w.InvMeso, DepositMax = 100000, WithdrawMax = 100000,
            FindInventoryItem = u => w.InvItem != null && w.InvItem.Uid == u ? w.InvItem : null };
        w.C = new StoreDialogController { Book = w.Book, Sender = w.Sender, ShowData = w.Boxes.Add };
        w.C.NoticeText += w.Notices.Add;
        return w;
    }

    [Test]
    public void LoadingASlowStoreAsksForItAndSortOnCloseAndTheSortButtonAreSent()
    {
        var w = Rig(ready: false);
        Load(w.C, w.Model);
        Assert.That(w.Sent.Select(p => p.GetType()), Is.EqualTo(new[] { typeof(StorageLoadRequest) }), "OnLoadInit: RequestLoad");
        w.Sent.Clear();
        w.C.OnCallback("onClickSort", new object[0]);
        Assert.That(w.Sent.Single(), Is.InstanceOf<StorageCloseRequest>(), "the sort button is CStore::RequestClose");
        w.Sent.Clear();
        w.C.Close();
        Assert.That(w.Sent.Single(), Is.InstanceOf<StorageSortRequest>(), "closing the dialog asks the server to sort the bank");
    }

    [Test]
    public void ClosingAnNpcOpenedBankEndsTheTalkBeforeTheSort()
    {
        var w = Rig();
        var order = new List<string>();
        w.C.IsTalking = () => true;
        w.C.EndTalk = () => order.Add("talk0");
        w.Sender.Send = p => order.Add(p.GetType().Name);
        Load(w.C, w.Model);
        w.Sent.Clear();
        w.C.Close();
        Assert.That(order, Is.EqualTo(new[] { "talk0", nameof(StorageSortRequest) }));
    }

    [Test]
    public void ClosingABankOpenedWithoutATalkSendsNoTalkEnd()
    {
        var w = Rig();
        int ends = 0;
        w.C.IsTalking = () => false;
        w.C.EndTalk = () => ends++;
        Load(w.C, w.Model);
        w.Sent.Clear();
        w.C.Close();
        Assert.That(ends, Is.EqualTo(0));
        Assert.That(w.Sent.Single(), Is.InstanceOf<StorageSortRequest>());
    }

    [Test]
    public void ARealStoreIsNotAskedForAgain()
    {
        var w = Rig();
        Load(w.C, w.Model);
        Assert.That(w.Sent, Is.Empty);
    }

    [Test]
    public void TheMesoButtonsAskInAnInputBoxAndItsAnswerIsChecked()
    {
        var w = Rig(meso: 500);
        Load(w.C, w.Model);
        w.C.OnCallback("onClickDeposit", new object[0]);
        var box = w.Boxes.Single();
        Assert.That(box.Callback(0, box), Is.True);       // no typed text: nothing, the box closes
        Assert.That(w.Sent, Is.Empty);
        box.ClickedTag = "deposit,300";
        Assert.That(box.Callback(1, box), Is.True, "the no button");
        Assert.That(w.Sent, Is.Empty);
        Assert.That(box.Callback(0, box), Is.True);
        var req = (StorageMesoRequest)w.Sent.Single();
        Assert.That((req.Action, req.Amount), Is.EqualTo(((byte)1, 300L)));
        w.Sent.Clear();
        box.ClickedTag = "deposit,5000";                   // more than the bag holds: refused, the box stays
        Assert.That(box.Callback(0, box), Is.False);
        Assert.That(w.Sent, Is.Empty);
        w.C.OnCallback("onClickWithDraw", new object[0]);
        var box2 = w.Boxes[1];
        box2.ClickedTag = "withdraw,200";
        Assert.That(box2.Callback(0, box2), Is.True);
        var out2 = (StorageMesoRequest)w.Sent.Single();
        Assert.That((out2.Action, out2.Amount), Is.EqualTo(((byte)0, 200L)));
        w.Sent.Clear();
        box2.ClickedTag = "withdraw,501";
        Assert.That(box2.Callback(0, box2), Is.False, "more than the bank holds");
        Assert.That(w.Sent, Is.Empty);
    }

    [Test]
    public void TheMesoButtonsAreRefusedWithADeadPlayer()
    {
        var w = Rig(meso: 500);
        w.Sender.PlayerAlive = () => false;
        var shown = new List<StorageErrorText>();
        w.Sender.Notify = shown.Add;
        Load(w.C, w.Model);
        w.C.OnCallback("onClickDeposit", new object[0]);
        w.C.OnCallback("onClickWithDraw", new object[0]);
        Assert.That(w.Boxes, Is.Empty);
        Assert.That(shown, Is.EqualTo(new[] { new StorageErrorText(StorageErrorKind.Notice, 0x42, null), new StorageErrorText(StorageErrorKind.Notice, 0x42, null) }));
    }

    [Test]
    public void TheExpandButtonConfirmsThenSendsOnYes()
    {
        var w = Rig();
        w.Book.MaxExtended = 100;                          // room to grow: the confirm box is offered
        w.C.ExpandPrice = () => 1000;
        Load(w.C, w.Model);
        w.C.OnCallback("onClickExpandSlot", new object[0]);
        var box = w.Boxes.Single();
        Assert.That(box.Callback(1, box), Is.True);
        Assert.That(w.Sent, Is.Empty, "the no button");
        Assert.That(box.Callback(0, box), Is.True);
        Assert.That(w.Sent.Single(), Is.InstanceOf<StorageExtendRequest>());
    }

    [Test]
    public void ADropBetweenBankSlotsMovesAndOntoItselfDoesNot()
    {
        var w = Rig();
        w.Book.Apply(new StorageMessage.Add(0, Item(20000001, 7, 2)));
        Load(w.C, w.Model);
        var from = StoreDialogController.SlotName(2);
        w.C.SlotDroppedOn(from, from, w.C);
        Assert.That(w.Sent, Is.Empty);
        w.C.SlotDroppedOn(StoreDialogController.SlotName(9), from, w.C);
        var move = (StorageMoveRequest)w.Sent.Single();
        Assert.That((move.Uid, move.ToSlot), Is.EqualTo((7L, (short)9)));
    }

    sealed class Bag : IInvenSlotOwner
    {
        public long? Uid; public Ms2.Ui.Items.Item Found;
        public long? InvenSlotUid(string name) => Uid;
        public Ms2.Ui.Items.Item FindItem(long uid) => Found;
        public bool CanEquipItem(Ms2.Ui.Items.Item item, int slot, int layer) => false;
        public void RequestEquipItem(Ms2.Ui.Items.Item item, int slot, int layer) { }
        public bool IsDragSlot(string name) => true;
        public void RefreshSlot(string name) { Refreshed++; }
        public int Refreshed;
        public void SlotDropUnresolved(string name) { }
    }

    static Ms2.Ui.Items.Item Bagged(UiModel model, long uid, int count, EquipItem record)
    {
        var item = Ms2.Ui.Items.Item.Create(model.Tables.Items, RealId(model), 1);
        if (item == null) Assert.Ignore("no item data");
        item.Uid = uid; item.Count = (uint)count; item.Record = record;
        return item;
    }

    [Test]
    public void ADropFromTheBagDepositsAndARefusedOneSendsNothing()
    {
        var w = Rig();
        Load(w.C, w.Model);
        var item = Bagged(w.Model, 11, 1, null);
        w.InvItem = new EquipItem { Id = item.Data.Id, Uid = 11, Amount = 1 };
        item.Record = w.InvItem;
        var bag = new Bag { Uid = 11, Found = item };
        w.C.SlotDroppedOn(StoreDialogController.SlotName(4), "InventoryDialog.0", bag);
        var dep = (StorageDepositRequest)w.Sent.Single();
        Assert.That((dep.Uid, dep.Slot, dep.Count), Is.EqualTo((11L, (short)4, 1)));
        Assert.That(bag.Refreshed, Is.EqualTo(1), "the source is pushed again, nothing moves before the server answers");
        w.Sent.Clear();
        w.Book.Allowed = _ => false;
        w.C.SlotDroppedOn(StoreDialogController.SlotName(4), "InventoryDialog.0", bag);
        Assert.That(w.Sent, Is.Empty, "an item the bank refuses");
        Assert.That(w.Notices.Count, Is.EqualTo(1));
        w.Book.Allowed = null;
        bag.Found = null;                                  // gone from the bag
        w.C.SlotDroppedOn(StoreDialogController.SlotName(4), "InventoryDialog.0", bag);
        Assert.That(w.Sent, Is.Empty);
    }

    [Test]
    public void ARightClickInTheBagDepositsTheStackIntoTheFirstFreeSlot()
    {
        var w = Rig();
        w.Book.Apply(new StorageMessage.Add(0, Item(20000001, 7, 0)));
        Load(w.C, w.Model);
        var item = Bagged(w.Model, 12, 1, null);
        w.InvItem = new EquipItem { Id = item.Data.Id, Uid = 12, Amount = 1 };
        item.Record = w.InvItem;
        w.C.DepositFromInventory(item);
        var dep = (StorageDepositRequest)w.Sent.Single();
        Assert.That((dep.Uid, dep.Slot), Is.EqualTo((12L, (short)1)), "AvailableSlot: the first empty slot");
    }

    static (InventoryView Bag, ItemBook Book) BagWith(params EquipItem[] items)
    {
        var b = new ItemBook { TabOf = id => id / 10000000, BaseSlots = _ => 4, MaxStackOf = _ => 99 };
        foreach (var it in items) b.TryAdd(it);
        return (new InventoryView(b), b);
    }

    static Wire BankedRig(EquipItem banked, InventoryView bag, long self = 7)
    {
        var w = Rig();
        w.Book.Apply(new StorageMessage.Add(0, banked));
        w.Sender.CanMove = it => StorageSender.BindingAllows(it, self);
        w.C.InventoryBag = () => bag;
        w.C.SelfId = () => self;
        w.C.TabName = t => "tab" + t;
        w.C.OpenInventory = () => w.C;                     // any open window
        Load(w.C, w.Model);
        w.Sent.Clear();
        return w;
    }

    [Test]
    public void ARightClickInTheBankWithdrawsTheStackIntoTheFirstFreeBagSlot()
    {
        var banked = Item(20000001, 50, 2, 1);
        var (bag, _) = BagWith(Item(20000002, 60, 0, 1));
        var w = BankedRig(banked, bag);
        w.C.OnCallback("notifySlotUse", new object[] { StoreDialogController.SlotName(2), 1 });
        var wd = (StorageWithdrawRequest)w.Sent.Single();
        Assert.That((wd.Uid, wd.Slot, wd.Count), Is.EqualTo((50L, (short)1, 1)), "FindEmptySlot: the first empty cell of the item's tab");
        w.Sent.Clear();
        w.C.OnCallback("notifySlotUse", new object[] { StoreDialogController.SlotName(2), 0 });
        Assert.That(w.Sent.Single(), Is.InstanceOf<StorageWithdrawRequest>(), "the double click is the same withdraw");
        w.Sent.Clear();
        w.C.ShiftHeld = () => true;
        w.C.OnCallback("notifySlotUse", new object[] { StoreDialogController.SlotName(2), 0 });
        Assert.That(w.Sent, Is.Empty, "a double click with Shift does nothing");
    }

    [Test]
    public void AStackableWithdrawGoesToSlotZeroWhenTheBagStacksHaveRoom()
    {
        var banked = Item(20000001, 50, 2, 1);
        var (bag, _) = BagWith(Item(20000001, 60, 0, 90), Item(20000002, 61, 1, 1));
        var w = BankedRig(banked, bag);
        w.C.OnCallback("notifySlotUse", new object[] { StoreDialogController.SlotName(2), 1 });
        Assert.That(((StorageWithdrawRequest)w.Sent.Single()).Slot, Is.EqualTo((short)0), "FUN_140a612b0: room in a stack");
    }

    [Test]
    public void AWithdrawWithNoBagRoomOrAForeignBindingOrNoInventorySendsNothing()
    {
        var banked = Item(20000001, 50, 2, 1);
        var (full, _) = BagWith(Item(20000002, 1, 0), Item(20000002, 2, 1), Item(20000002, 3, 2), Item(20000002, 4, 3));
        var w = BankedRig(banked, full);
        w.C.OnCallback("notifySlotUse", new object[] { StoreDialogController.SlotName(2), 1 });
        Assert.That(w.Sent, Is.Empty, "no free slot in the tab");
        Assert.That(w.Notices.Count, Is.EqualTo(1), "notice 0x44");
        var (bag, _) = BagWith();
        var bound = Item(20000001, 51, 3, 1); bound.BindingId = 99;
        w = BankedRig(bound, bag);
        int boxes = 0;
        w.C.ShowBox = (_, _) => boxes++;
        w.C.OnCallback("notifySlotUse", new object[] { StoreDialogController.SlotName(3), 1 });
        Assert.That(w.Sent, Is.Empty, "bound to another character");
        Assert.That(boxes, Is.EqualTo(1));
        w = BankedRig(banked, bag);
        w.C.OpenInventory = () => null;
        w.C.OnCallback("notifySlotUse", new object[] { StoreDialogController.SlotName(2), 1 });
        Assert.That(w.Sent, Is.Empty, "the inventory window is not open");
    }
}
