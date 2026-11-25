using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The trade dialog (uitradedialogex) and the inventory's trade adds, run with their movies (needs MS2_DATA): the
/// lock's meso check (ValidateMeso), InvenSlot::TryAddToTrade and the count box's answer (TradeIn). Texts are not
/// compared, so no language's strings.</summary>
public class TradeDialogTests
{
    static PackSet packs;
    static MovieSource src;

    static PackSet Packs()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = TestData.Packs(d);
            src = TestData.Movies(packs);
        }
        return packs;
    }

    static UiModel NewModel()
    {
        var m = new UiModel(new GameTables(Packs(), "en")) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(Packs(), f) };
        m.Tables.TradeMinMeso = 100;
        m.Tables.TradeMaxMeso = 1_000_000;
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

    static (TradeBook, List<IOutPacket>) NewBook()
    {
        var sent = new List<IOutPacket>();
        return (new TradeBook { Send = sent.Add, Common = id => $"#{id:x}" }, sent);
    }

    [Test]
    public void TheLockSendsItOnlyForMesosInRangeAndOwned()
    {
        Packs();
        var (book, sent) = NewBook();
        var boxes = new List<int>();
        var c = new TradeDialogController { Book = book, OwnedMeso = () => 5000 };
        c.ShowBox = (_, type) => boxes.Add(type);
        var h = Load(c, NewModel());
        string Input() => Rt.ToString(h.Get(h.Child("m_myInputMeso"), "text"));
        Assert.That(c.OnCallback("onLock", new object[] { "1000" }), Is.True);
        Assert.That(sent, Is.EqualTo(new[] { TradeRequest.SetMeso(1000), TradeRequest.Lock() }));
        Assert.That(boxes, Is.Empty);
        foreach (var (text, why) in new[] { ("50", "below the least"), ("2000000", "above the most"), ("6000", "more than owned") })
        {
            sent.Clear(); boxes.Clear();
            c.OnCallback("onLock", new object[] { "1000" });     // a nonzero amount sent last, so the 0 below is sent
            sent.Clear();
            c.OnCallback("onLock", new object[] { text });
            Assert.That(sent, Is.EqualTo(new[] { TradeRequest.SetMeso(0) }), why + ": mode 10 with 0, no lock");
            Assert.That((boxes.Count, boxes[0], Input()), Is.EqualTo((1, 0, "0")), why + ": one box, the input reset");
        }
    }

    /// <summary>InvenSlot::vfunc_18's trade slot branch (invenslot.cpp:1202): a trade slot dropped on a bag slot plays the
    /// item's sound and, on my side only, sends the remove (TradeSlot::SendRemoveItem, mode 9 with the uid and the index);
    /// the trade slot is pushed again either way.</summary>
    [Test]
    public void ATradeSlotDroppedOnTheBagTakesMyItemOut()
    {
        Packs();
        var (inv, book, sent, _) = LoadInventory();
        inv.ShowTab(3); inv.Process();
        var trade = new TradeDialogController { Book = book, OwnedMeso = () => 5000 };
        var sounds = new List<long>();
        trade.ItemSound += i => sounds.Add(i.Uid);
        Load(trade, NewModel());
        trade.SetTradeSlotItem(true, 2, Tradeable(11, 1, 1));
        trade.SetTradeSlotItem(false, 1, Tradeable(12, 1, 1));

        inv.SlotDroppedOn(InventoryController.SlotName(5), TradeDialogController.SlotName(true, 2), trade);
        Assert.That((sent.Single(), sounds.Single()), Is.EqualTo((TradeRequest.RemoveItem(11, 2), 11L)));
        // the partner's slot: the sound only
        inv.SlotDroppedOn(InventoryController.SlotName(5), TradeDialogController.SlotName(false, 1), trade);
        Assert.That((sent.Count, sounds.Last()), Is.EqualTo((1, 12L)));
        // my empty slot: no sound, the remove with uid 0
        inv.SlotDroppedOn(InventoryController.SlotName(5), TradeDialogController.SlotName(true, 0), trade);
        Assert.That((sent.Last(), sounds.Count), Is.EqualTo((TradeRequest.RemoveItem(0, 0), 2)));
        Assert.That(trade.TradeSlotDroppedOnBag("InventoryDialog.0"), Is.False, "no trade slot");
    }

    static Item Tradeable(long uid, uint count, uint slotMax)
    {
        var i = Item.Create(new ItemData { Id = 1, SlotMax = slotMax }, 1);
        i.Uid = uid;
        i.Count = count;
        i.Record = new EquipItem();
        i.Record.Transfer.Rule = 4;
        return i;
    }

    static (InventoryController, TradeBook, List<IOutPacket>, List<MessageBoxData>) LoadInventory()
    {
        var bag = new ItemBook { TabOf = id => id / 10000000, BaseSlots = _ => 48 };
        bag.Apply(new ItemMessage.LoadTab(3, new[] { new EquipItem { Id = 30000001, Uid = 3, InventorySlot = 0, Amount = 7 } }));
        var c = new InventoryController { View = new InventoryView(bag) };
        var boxes = new List<MessageBoxData>();
        c.ShowBox = boxes.Add;
        Load(c, NewModel());
        var (book, sent) = NewBook();
        return (c, book, sent, boxes);
    }

    static void Fill(TradeBook book)
    {
        for (long u = 100; u < 100 + 5; u++) Assert.That(book.Items.AddItem(Tradeable(u, 1, 1)), Is.True);
        Assert.That(book.Items.FindFreeSlot(), Is.EqualTo(-1));
    }

    [Test]
    public void TryAddToTradeSendsTheStackOrAsksTheCount()
    {
        Packs();
        var (c, book, sent, boxes) = LoadInventory();
        int sounds = 0;
        c.ItemSound += _ => sounds++;
        Assert.That(c.TryAddToTrade(Tradeable(9, 1, 1)), Is.EqualTo(1));
        Assert.That((sent.Count, boxes.Count, sounds), Is.EqualTo((0, 0, 0)), "no trade dialog: nothing more");
        c.Trade = () => book;
        Assert.That(c.TryAddToTrade(Tradeable(9, 1, 1)), Is.EqualTo(1));
        Assert.That((sent.Single(), sounds), Is.EqualTo((TradeRequest.AddItem(9, 1, 0), 1)), "a single item: added whole");
        sent.Clear();
        Assert.That(c.TryAddToTrade(Tradeable(8, 5, 100)), Is.EqualTo(1));
        var box = boxes.Single();
        Assert.That((sent.Count, box.Type, box.Extra, box.ItemUid, box.InputType, box.InputDefault, box.InputMaxChars),
            Is.EqualTo((0, 5, InventoryController.TradeInBox, 8L, 1, "5", 3)), "a stack: the count box, digits of the slot max");
        c.ShiftHeld = () => true;
        Assert.That(c.TryAddToTrade(Tradeable(8, 5, 100)), Is.EqualTo(1));
        Assert.That(sent.Single(), Is.EqualTo(TradeRequest.AddItem(8, 5, 0)), "Shift: the whole stack");
        sent.Clear(); boxes.Clear();
        Fill(book);
        Assert.That(c.TryAddToTrade(Tradeable(7, 1, 1)), Is.EqualTo(0));
        Assert.That((sent.Count, boxes.Single().Type), Is.EqualTo((0, 0)), "a full table: a box, nothing sent");
    }

    [Test]
    public void TradeInChecksTheCountThenNeedsTheDialogAndAFreeSlot()
    {
        Packs();
        var (c, book, sent, _) = LoadInventory();
        int notices = 0, sounds = 0;
        c.NoticeText += _ => notices++;
        c.ItemSound += _ => sounds++;
        c.Trade = () => book;
        c.TradeIn(3, 0);
        c.TradeIn(3, 8);
        Assert.That((notices, sent.Count), Is.EqualTo((2, 0)), "0 and more than the stack of 7: a notice each");
        c.Trade = () => null;
        c.TradeIn(3, 7);
        Assert.That((notices, sent.Count), Is.EqualTo((2, 0)), "no trade dialog: nothing");
        c.Trade = () => book;
        c.TradeIn(3, 7);
        Assert.That((sent.Single(), sounds), Is.EqualTo((TradeRequest.AddItem(3, 7, 0), 1)));
        sent.Clear();
        Fill(book);
        c.TradeIn(3, 1);
        Assert.That((notices, sent.Count), Is.EqualTo((2, 0)), "a full table: nothing");
    }
    sealed class Dialog : ITradeDialog
    {
        public string Partner { get; set; } = "";
        public bool Closed;
        public readonly List<string> Calls = new();
        public void Close() => Closed = true;
        public void SetTradeSlotItem(bool mine, int slot, Item item) => Calls.Add($"set {mine} {slot} {item?.Uid} {item?.Slot} {item?.Count}");
        public void ClearTradeSlot(bool mine, int slot) => Calls.Add($"clear {mine} {slot}");
        public void SetSideMeso(int side, long meso) { }
        public void SetLatch(bool mine, bool on) { }
        public void RequestFailed(int result) { }
    }

    [Test]
    public void AnAddedItemGoesToTheSlotAndMineAlsoToTheTable()
    {
        Packs();
        var (book, _) = NewBook();
        book.ItemData = new ItemDataTable(Packs());
        var dlg = new Dialog();
        book.Dialog = () => dlg;
        var events = new List<(long, bool)>();
        book.ItemEvent += (it, added) => events.Add((it.Uid, added));
        TradeMessage Added(byte mine, long uid, int id, int slot) =>
            new(8) { Flag = mine, Id = uid, TradeSlot = slot, Item = new EquipItem { Id = id, Uid = uid, Rarity = 1, Amount = 3, InventorySlot = (short)slot } };
        book.OnTrade(Added(1, 11, 30000001, 2));
        uint count = book.Items.Find(11)?.Count ?? 0;
        Assert.That(count, Is.InRange(1u, 3u), "the count clamped to the slot max");
        Assert.That((events.Single(), book.Items.Find(11).Slot, dlg.Calls.Single()), Is.EqualTo(((11L, true), 2, $"set True 2 11 2 {count}")));
        book.OnTrade(Added(0, 12, 30000001, 1));
        Assert.That((events.Count, book.Items.Find(12), dlg.Calls.Last()), Is.EqualTo((1, (Item)null, $"set False 1 12 1 {count}")),
            "the partner's: the slot only");
        Assert.That(dlg.Closed, Is.False);
        int missing = UiDataRows.MissingItemId(book.ItemData);
        if (UiDataRows.IsKr(Packs())) Assert.That(missing, Is.EqualTo(1));
        book.OnTrade(Added(1, 13, missing, 3));
        Assert.That((dlg.Closed, dlg.Calls.Count, events.Count, book.Items.Find(13)), Is.EqualTo((true, 2, 1, (Item)null)),
            "an item the data does not know closes the dialog");
        dlg.Closed = false;
        book.OnTrade(new TradeMessage(8) { Flag = 1, Id = 14, TradeSlot = 3 });
        Assert.That(dlg.Closed, Is.True, "an item left unread (no item) closes it too");

        // code 9
        dlg.Calls.Clear();
        book.OnTrade(new TradeMessage(9) { Flag = 1, Id = 99, TradeSlot = 2 });
        Assert.That((dlg.Calls.Count, events.Count), Is.EqualTo((0, 1)), "mine with a uid not in the table: nothing");
        book.OnTrade(new TradeMessage(9) { Flag = 1, Id = 11, TradeSlot = 2 });
        Assert.That((events.Last(), book.Items.Find(11), dlg.Calls.Single()), Is.EqualTo(((11L, false), (Item)null, "clear True 2")));
        book.OnTrade(new TradeMessage(9) { Flag = 0, Id = 12, TradeSlot = 1 });
        Assert.That((events.Count, dlg.Calls.Last()), Is.EqualTo((2, "clear False 1")), "the partner's: the slot only");
    }
}
