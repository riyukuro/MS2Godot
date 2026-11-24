using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using NUnit.Framework;
using Maple2.PacketLib.Tools;

namespace Ms2.Ui.Tests;

/// <summary>The item lock dialog (uiitemlockdialog) over the lock book (needs MS2_DATA): the movie's children, the packets the
/// dialog sends on load / close / confirm / drop with the client's bytes, the refused cases sending nothing, and the
/// CanBeItemLocked rule. Texts are not compared, so no language's strings.</summary>
public class ItemLockDialogTests
{
    static PackSet packs;
    static MovieSource src;

    static void Packs()
    {
        if (packs != null) return;
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs = new PackSet(d);
        src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
    }

    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    sealed class Rig
    {
        public ItemLockDialogController C;
        public ItemLockBook Book = new();
        public List<IOutPacket> Sent = new();
        public List<MessageBoxData> Boxes = new();
        public List<string> Notices = new();
        public List<(int, int)> BookNotices = new();
        public Dictionary<long, EquipItem> Inv = new();
        public UiModel Model;
        public UiHost Host;

        public Rig()
        {
            Packs();
            Model = new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true };
            Book.Send = Sent.Add;
            Book.Find = u => Inv.TryGetValue(u, out var i) ? i : null;
            Book.CanBeLocked = i => i.Id < 1000;
            Book.Notice += (a, b) => BookNotices.Add((a, b));
            C = new ItemLockDialogController { Book = Book, ShowData = Boxes.Add };
            C.FindItem = uid => Inv.TryGetValue(uid, out var e) ? Wrap(e) : null;
            C.NoticeText += Notices.Add;
            var (rt, p) = src.Open(C.Movie, r => r.Budget = 20_000_000);
            p.StageW = 1280; p.StageH = 720;
            p.Translate = k => k.TrimStart('$');
            Host = new UiHost(p, C, Model);
            Assert.That(Host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
            for (int i = 0; i < 10; i++) p.Tick();
        }

        public EquipItem Add(long uid, int id = 1, byte lockState = 0)
        {
            var e = new EquipItem { Id = id, Uid = uid, LockState = lockState };
            Inv[uid] = e;
            return e;
        }

        Ms2.Ui.Items.Item Wrap(EquipItem e)
        {
            var item = Ms2.Ui.Items.Item.Create(Model.Tables.Items, RealId(), 1);
            if (item == null) Assert.Ignore("no item data");
            item.Uid = e.Uid; item.Record = e;
            return item;
        }

        int RealId()
        {
            int id = new[] { 20000001, 20000002, 30000001, 11300001, 12000001 }.FirstOrDefault(i => !string.IsNullOrEmpty(Model.Tables.ItemName(i)));
            if (id == 0) Assert.Ignore("no known item");
            return id;
        }

        /// <summary>The server's mode 1: the item sits in slot <paramref name="slot"/>.</summary>
        public void Put(long uid, int slot)
        {
            var w = new ByteWriter(); w.WriteByte(1); w.WriteLong(uid); w.WriteShort((short)slot);
            Book.Apply(ItemLockMessage.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()), Book.Find));
        }
    }

    [Test]
    public void InventoryButtonsAreRefreshedWhenTheDialogIsUnloadedNotWhenTheCloseStarts()
    {
        // the unload (vfunc_26 after the close tween) is what the inventory's open-dialog test (vfunc_55) sees as closed
        var r = new Rig();
        var closedAtEvent = new List<bool>();
        r.C.InventoryButtons += () => closedAtEvent.Add(r.C.IsClosed);
        r.C.Close();
        for (int i = 0; i < 300 && !r.C.IsClosed; i++) r.Host.Player.Tick();
        Assert.That(r.C.IsClosed, Is.True);
        Assert.That(closedAtEvent, Is.EqualTo(new[] { true }));
    }

    [Test]
    public void LoadingOpensTheBookAndSendsTheResetOnce()
    {
        var r = new Rig();
        Assert.That(r.Book.DialogOpen, Is.True);
        Assert.That(r.Book.Mode, Is.EqualTo(0));
        Assert.That(r.Sent.Select(Bytes).Single(), Is.EqualTo(new byte[] { 0 }), "SendRequestItemLockMode0");
        Assert.That(r.Host.Child("m_itemBag"), Is.Not.Null);
        Assert.That(r.Host.Child("m_lock"), Is.Not.Null);
        Assert.That(r.Host.Child("m_unLock"), Is.Not.Null);
        Assert.That(Rt.ToString(r.Host.Get(r.Host.Child("m_itemBag"), "bagId")), Is.EqualTo(ItemLockDialogController.Bag));
        Assert.That(r.C.GetBagSlotBindings(ItemLockDialogController.Bag).Count(), Is.EqualTo(ItemLockBook.Slots));
    }

    [Test]
    public void TheLockButtonsFollowTheBag()
    {
        var r = new Rig();
        Assert.That(Rt.ToBoolean(r.Host.Get(r.Host.Child("m_lock"), "enabled")), Is.False);
        r.Add(5);
        r.Put(5, 0);
        Assert.That(Rt.ToBoolean(r.Host.Get(r.Host.Child("m_lock"), "enabled")), Is.True);
        Assert.That(Rt.ToBoolean(r.Host.Get(r.Host.Child("m_unLock"), "enabled")), Is.True);
        Assert.That(r.C.GetBinding(ItemLockDialogController.SlotName(0)), Is.Not.Null);
        Assert.That(r.C.GetBinding(ItemLockDialogController.SlotName(1)), Is.Null);
    }

    [Test]
    public void AResultRefreshesTheBagAndOnChangeTabWithoutArgsDoesNothing()
    {
        var r = new Rig();
        int n = 0;
        r.Book.BagChanged += () => n++;
        r.Book.Apply(new ItemLockMessage.Result(3));
        Assert.That(n, Is.EqualTo(1));
        r.Sent.Clear();
        Assert.That(r.C.OnCallback("OnChangeTab", Array.Empty<object>()), Is.False);
        Assert.That(r.Sent, Is.Empty);
    }

    [Test]
    public void TheConfirmBoxSendsModeThreeOnlyOnYes()
    {
        var r = new Rig();
        r.Sent.Clear();
        Assert.That(r.C.OnCallback("OnItemLock", Array.Empty<object>()), Is.False, "an empty bag asks nothing");
        Assert.That(r.Boxes, Is.Empty);
        r.Add(5); r.Put(5, 2);
        Assert.That(r.C.OnCallback("OnItemLock", Array.Empty<object>()), Is.True);
        var box = r.Boxes.Single();
        Assert.That(box.Extra, Is.EqualTo(ItemLockDialogController.ConfirmTag));
        Assert.That(box.Callback(1, null), Is.True);
        Assert.That(r.Sent, Is.Empty, "no");
        Assert.That(box.Callback(0, null), Is.True);
        Assert.That(Bytes(r.Sent.Single()), Is.EqualTo(new byte[] { 3, 0 }));
    }

    [Test]
    public void TheUnlockTabAsksItsOwnBoxAndSendsItsMode()
    {
        var r = new Rig();
        r.Add(5); r.Put(5, 0);
        r.Sent.Clear();
        r.C.ChangeTab(1);
        Assert.That(Bytes(r.Sent.Single()), Is.EqualTo(new byte[] { 0 }), "the bag is dropped with a mode 0 send");
        Assert.That(r.Book.Mode, Is.EqualTo(1));
        r.Sent.Clear();
        r.Add(6, 1, 1); r.Put(6, 0);
        r.C.OnCallback("OnItemUnLock", Array.Empty<object>());
        var box = r.Boxes.Single();
        Assert.That(box.Extra, Is.Not.EqualTo(ItemLockDialogController.ConfirmTag));
        box.Callback(0, null);
        Assert.That(Bytes(r.Sent.Single()), Is.EqualTo(new byte[] { 3, 1 }));
    }

    [Test]
    public void AnInventoryDropSelectsAndARefusedOneSendsNothing()
    {
        var r = new Rig();
        r.Sent.Clear();
        var good = r.Add(9, 1);
        var inv = new InvOwner { Uid = 9, Found = r.C.FindItem(9) };
        r.C.SlotDroppedOn(ItemLockDialogController.SlotName(0), "InventoryDialog.0", inv);
        Assert.That(Bytes(r.Sent.Single()), Is.EqualTo(new byte[] { 1, 0 }.Concat(BitConverter.GetBytes(9L)).ToArray()));
        Assert.That(inv.Refreshed, Is.EqualTo(1));
        r.Sent.Clear();
        good.Id = 5000;     // not lockable
        r.C.SlotDroppedOn(ItemLockDialogController.SlotName(0), "InventoryDialog.0", inv);
        Assert.That(r.Sent, Is.Empty);
        Assert.That(r.BookNotices.Single().Item1, Is.EqualTo(ItemLockBook.NoticeCannotLock));
        r.C.SlotDroppedOn("Other.0", "InventoryDialog.0", inv);
        Assert.That(r.Sent, Is.Empty, "a slot that is not the bag's");
    }

    [Test]
    public void ARightClickSelectsOnlyWhileTheDialogIsOpen()
    {
        var r = new Rig();
        r.Sent.Clear();
        r.Add(9, 1);
        r.C.SelectFromInventory(r.C.FindItem(9));
        Assert.That(r.Sent.Count, Is.EqualTo(1));
        r.Sent.Clear();
        r.C.Close();
        r.C.SelectFromInventory(r.C.FindItem(9));
        Assert.That(r.Sent.Select(Bytes).Where(b => b[0] == 1), Is.Empty);
    }

    [Test]
    public void DraggingABagItemOntoAnInventorySlotReplacesOrRemoves()
    {
        var r = new Rig();
        r.Add(1, 1); r.Put(1, 0);
        r.Sent.Clear();
        var lockable = r.Add(2, 1);
        r.C.DroppedOnInventory(1, r.C.FindItem(2));
        Assert.That(Bytes(r.Sent.Single()), Is.EqualTo(new byte[] { 1, 0 }.Concat(BitConverter.GetBytes(2L)).ToArray()), "the slot's item replaces it");
        r.Sent.Clear();
        r.C.DroppedOnInventory(1, null);
        Assert.That(Bytes(r.Sent.Single()), Is.EqualTo(new byte[] { 2 }.Concat(BitConverter.GetBytes(1L)).ToArray()), "an empty cell removes");
        r.Sent.Clear();
        lockable.Id = 5000;
        r.C.DroppedOnInventory(1, r.C.FindItem(2));
        Assert.That(Bytes(r.Sent.Single())[0], Is.EqualTo(2), "an item that cannot lock removes");
    }

    [Test]
    public void ALockSlotsRightClickTakesItsItemOutAndItsShiftClickLinksIt()
    {
        // NotifyDragSlot::SlotUse button 1 -> ItemLockSlot::vfunc_14 (mode 2 with the item uid); the preview key: raised;
        // button 0: pushed again; SlotShiftClick -> vfunc_15 (the item linked)
        var r = new Rig();
        r.Add(5); r.Put(5, 2);
        r.Sent.Clear();
        bool preview = false;
        var raised = new List<string>(); r.C.Button += raised.Add;
        r.C.ActionKeyDown = k => preview && k == 0x49406;
        var linked = new List<long>(); r.C.LinkItem = i => linked.Add(i.Uid);
        string Slot(int i) => ItemLockDialogController.SlotName(i);

        Assert.That(r.C.OnCallback("notifySlotUse", new object[] { Slot(2), 1 }), Is.True);
        Assert.That(r.Sent.Select(Bytes).Single(), Is.EqualTo(new byte[] { 2, 5, 0, 0, 0, 0, 0, 0, 0 }));
        r.C.OnCallback("notifySlotUse", new object[] { Slot(2), 0 });
        r.C.OnCallback("notifySlotUse", new object[] { Slot(3), 1 });
        Assert.That(r.Sent.Count, Is.EqualTo(1), "button 0 and an empty slot send nothing");
        preview = true;
        r.C.OnCallback("notifySlotUse", new object[] { Slot(2), 1 });
        Assert.That((r.Sent.Count, raised.Single()), Is.EqualTo((1, "notifySlotUse")), "the preview key: the preview, raised");

        Assert.That(r.C.OnCallback("notifySlotShiftClick", new object[] { Slot(2) }), Is.True);
        r.C.OnCallback("notifySlotShiftClick", new object[] { Slot(3) });
        Assert.That(linked, Is.EqualTo(new[] { 5L }));
        Assert.That(r.C.OnCallback("notifySlotUse", new object[] { "Other.0", 1 }), Is.False);
    }

    [Test]
    public void ALockSlotShowsNoItemOrTooltipWithoutTheLocalPlayer()
    {
        // ItemLockSlot::vfunc_8 / vfunc_0 (itemlockslot.cpp:114, :132): no local player, 0
        var r = new Rig();
        r.Add(5); r.Put(5, 2);
        string slot = ItemLockDialogController.SlotName(2);
        Assert.That(r.C.GetBinding(slot), Is.Not.Null);
        Assert.That(r.C.GetSlotTooltip(slot), Is.Not.Empty);
        r.Model.HasLocalPlayer = false;
        Assert.That(r.C.GetBinding(slot), Is.Null);
        Assert.That(r.C.GetSlotTooltip(slot), Is.Empty);
    }

    [Test]
    public void ClosingSendsTheResetAndIgnoresLaterPackets()
    {
        var r = new Rig();
        r.Add(5);
        r.Sent.Clear();
        r.C.Close();
        Assert.That(Bytes(r.Sent.Single()), Is.EqualTo(new byte[] { 0 }));
        Assert.That(r.Book.DialogOpen, Is.False);
        r.Put(5, 0);
        Assert.That(r.Book[0], Is.EqualTo(0), "no dialog, no bag");
    }

    [Test]
    public void CanBeItemLockedFollowsTheClientsRule()
    {
        var rec = new EquipItem { LockState = 0 };
        Assert.That(ItemLockDialogController.CanBeItemLocked(rec, new Ms2.Ui.Items.ItemData { Type = 1 }), Is.True);
        Assert.That(ItemLockDialogController.CanBeItemLocked(rec, new Ms2.Ui.Items.ItemData { Type = 7 }), Is.False, "a type outside the set");
        Assert.That(ItemLockDialogController.CanBeItemLocked(rec, new Ms2.Ui.Items.ItemData { Type = 1, PCBang = true }), Is.False);
        Assert.That(ItemLockDialogController.CanBeItemLocked(rec, new Ms2.Ui.Items.ItemData { Type = 1, Vip = true }), Is.False);
        Assert.That(ItemLockDialogController.CanBeItemLocked(rec, new Ms2.Ui.Items.ItemData { Type = 1, Wedding = true }), Is.False);
        Assert.That(ItemLockDialogController.CanBeItemLocked(new EquipItem { LockState = 1 }, new Ms2.Ui.Items.ItemData { Type = 1 }), Is.False, "already locked");
        foreach (int t in new[] { 5, 9, 11, 12, 13, 21 })
            Assert.That(ItemLockDialogController.CanBeItemLocked(rec, new Ms2.Ui.Items.ItemData { Type = t }), Is.True, "type " + t);
    }

    [Test]
    public void TheUnlockWaitIsTheClientConstantInHours()
    {
        Packs();
        Assert.That(new GameTables(packs, "en").ItemUnLockTime, Is.GreaterThan(0u));
    }

    sealed class InvOwner : IInvenSlotOwner
    {
        public long? Uid; public Ms2.Ui.Items.Item Found; public int Refreshed;
        public long? InvenSlotUid(string name) => Uid;
        public Ms2.Ui.Items.Item FindItem(long uid) => Found;
        public bool CanEquipItem(Ms2.Ui.Items.Item item, int slot, int layer) => false;
        public void RequestEquipItem(Ms2.Ui.Items.Item item, int slot, int layer) { }
        public bool IsDragSlot(string name) => true;
        public void RefreshSlot(string name) { Refreshed++; }
        public void SlotDropUnresolved(string name) { }
    }
}
