using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CTradeManagerEx (TradeBook), its item table, the trade item rules (ItemTradeRules), the dialog's fee and meso
/// range, and the number text the fee uses. No data needed; texts are the common ids, so no language's strings.</summary>
public class TradeTests
{
    sealed class Dialog : ITradeDialog
    {
        public string Partner { get; set; } = "";
        public bool Closed;
        public readonly List<string> Calls = new();
        public void Close() => Closed = true;
        public void SetTradeSlotItem(bool mine, int slot, Item item) => Calls.Add($"set {mine} {slot} {item?.Uid}");
        public void ClearTradeSlot(bool mine, int slot) => Calls.Add($"clear {mine} {slot}");
        public void SetSideMeso(int side, long meso) => Calls.Add($"meso {side} {meso}");
        public void SetLatch(bool mine, bool on) => Calls.Add($"latch {mine} {on}");
        public void RequestFailed(int result) => Calls.Add($"failed {result}");
    }

    static (TradeBook, List<IOutPacket>, List<(string, int, bool)>) NewBook()
    {
        var sent = new List<IOutPacket>();
        var notices = new List<(string, int, bool)>();
        var b = new TradeBook { Send = sent.Add, Common = id => $"#{id:x}" };
        b.Notice += (t, type, log) => notices.Add((t, type, log));
        return (b, sent, notices);
    }

    static byte[] Bytes(IOutPacket p) { var w = new Maple2.PacketLib.Tools.ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    [Test]
    public void TheTableTakesTheFirstFreeSlotAndRefusesTakenOnes()
    {
        var t = new TradeItemTable();
        t.Init();
        var d = new ItemData { Id = 1 };
        Item I(long uid, int slot) { var i = Item.Create(d, 1); i.Uid = uid; i.Slot = slot; return i; }
        Assert.That(t.AddItem(I(1, 2)), Is.True);
        Assert.That(t.AddItem(I(1, 3)), Is.False, "a uid in already");
        Assert.That(t.AddItem(I(2, 2)), Is.False, "a taken slot");
        Assert.That(t.AddItem(I(3, 5)), Is.False, "past the last slot");
        var free = I(4, -1);
        Assert.That(t.AddItem(free), Is.True);
        Assert.That(free.Slot, Is.EqualTo(0), "slot -1 takes the first free one");
        t.RemoveItem(1);
        Assert.That((t.Find(1), t.FindFreeSlot()), Is.EqualTo(((Item)null, 1)));
        for (long u = 10; u < 14; u++) t.AddItem(I(u, -1));
        Assert.That(t.FindFreeSlot(), Is.EqualTo(-1));
        Assert.That(t.AddItem(I(20, -1)), Is.False, "no free slot");
    }

    [Test]
    public void TheMesoSendSkipsTheAmountSentLast()
    {
        var (b, sent, _) = NewBook();
        b.SendMeso(0);
        Assert.That(sent, Is.Empty, "0 is the last sent at the start");
        b.SendMeso(500);
        b.SendMeso(500);
        Assert.That(sent.Count, Is.EqualTo(1));
        Assert.That(Bytes(sent[0])[0], Is.EqualTo(10));
        Assert.That(b.LastMesoSent, Is.EqualTo(500));
    }

    [Test]
    public void RequestTradeChecksTheDialogThenSendsTheAsk()
    {
        var (b, sent, notices) = NewBook();
        b.CanOpenDialog = () => false;
        b.RequestTrade(5);
        Assert.That((sent.Count, notices.Count), Is.EqualTo((0, 0)), "a dialog that may not open: nothing");
        b.CanOpenDialog = () => true;
        var dlg = new Dialog();
        b.Dialog = () => dlg;
        b.RequestTrade(5);
        Assert.That(notices, Is.EqualTo(new[] { ("#841", 1, true) }));
        Assert.That(sent, Is.Empty);
        b.Dialog = () => null;
        b.TradeBlocked = () => true;
        b.RequestTrade(5);
        Assert.That(sent, Is.Empty, "blocked");
        b.TradeBlocked = () => false;
        b.RequestTrade(0);
        Assert.That(sent, Is.Empty, "no character id");
        b.RequestTrade(5);
        Assert.That(Bytes(sent.Single()), Is.EqualTo(new byte[] { 0, 5, 0, 0, 0, 0, 0, 0, 0 }));
    }

    [Test]
    public void CanStartTradeIsFalseWithAPartnerOrTheDialog()
    {
        var (b, _, notices) = NewBook();
        Assert.That(b.CanStartTrade(), Is.True);
        b.Begin(7, "a", "k");
        Assert.That(b.CanStartTrade(), Is.False);
        Assert.That(notices.Single(), Is.EqualTo(("#841", 1, true)));
        b.Reset();
        Assert.That((b.Partner, b.PartnerName, b.CanStartTrade()), Is.EqualTo((0L, "", true)));
    }

    [Test]
    public void ARequestIsRefusedBusyOrShownAsABox()
    {
        var (b, sent, _) = NewBook();
        b.RefuseTrade = () => true;
        b.OnTrade(new TradeMessage(0) { Name = "n", Id = 5 });
        Assert.That(Bytes(sent.Single())[0], Is.EqualTo(4), "the option declines");
        sent.Clear();
        b.RefuseTrade = () => false;
        b.BoxShown = () => true;
        b.OnTrade(new TradeMessage(0) { Name = "n", Id = 5 });
        Assert.That(Bytes(sent.Single())[0], Is.EqualTo(14), "a box up: busy");
        sent.Clear();
        b.BoxShown = () => false;
        MessageBoxData box = null;
        b.ShowBox = x => box = x;
        b.RequestDuration = () => 20;
        b.OnTrade(new TradeMessage(0) { Name = "n", Id = 5 });
        Assert.That((box?.Extra, box?.Type), Is.EqualTo(("requestTradeEx", 1)));
        Assert.That(box.ArgLong(0, 0), Is.EqualTo(5L));
        Assert.That(Bytes(sent.Single())[0], Is.EqualTo(2), "shown");
    }

    [Test]
    public void TheStartNeedsThePartnerInTheField()
    {
        var (b, _, _) = NewBook();
        var opened = new List<int>();
        b.OpenDialog += opened.Add;
        b.OnTrade(new TradeMessage(5) { Id = 9 });
        Assert.That(opened, Is.Empty, "not in the field");
        b.FindPlayer = id => id == 9 ? new TradePartner("p", "key") : null;
        b.SendMeso(300);
        b.OnTrade(new TradeMessage(5) { Id = 9 });
        Assert.That(opened.Single(), Is.EqualTo(TradeBook.DialogId));
        Assert.That((b.Partner, b.PartnerName, b.PartnerKey, b.LastMesoSent), Is.EqualTo((9L, "p", "key", 0L)));
    }

    [Test]
    public void LockAndUnlockLatchTheSideAndKeepMine()
    {
        var (b, _, _) = NewBook();
        var dlg = new Dialog();
        var chat = new List<string>();
        b.ChatLine += (t, _) => chat.Add(t);
        b.OnTrade(new TradeMessage(11) { Flag = 1 });
        Assert.That(b.Confirmed, Is.False, "no dialog: nothing");
        b.Dialog = () => dlg;
        b.OnTrade(new TradeMessage(11) { Flag = 1 });
        b.OnTrade(new TradeMessage(11) { Flag = 0 });
        Assert.That(b.Confirmed, Is.True);
        b.OnTrade(new TradeMessage(12) { Flag = 0 });
        Assert.That(b.Confirmed, Is.True, "the partner's unlock leaves mine");
        b.OnTrade(new TradeMessage(12) { Flag = 1 });
        Assert.That(b.Confirmed, Is.False);
        Assert.That(dlg.Calls, Is.EqualTo(new[] { "latch True True", "latch False True", "latch False False", "latch True False" }));
        Assert.That(chat, Is.EqualTo(new[] { "#845", "#844" }));
        b.OnTrade(new TradeMessage(10) { Flag = 1, Meso = 77 });
        Assert.That(dlg.Calls.Last(), Is.EqualTo("meso 1 77"));
    }

    [Test]
    public void TheFinishClosesTheDialogAndClearsTheMesosSent()
    {
        var (b, _, _) = NewBook();
        var dlg = new Dialog { Partner = "p" };
        b.Dialog = () => dlg;
        var closed = new List<int>();
        var chat = new List<string>();
        b.CloseDialog += closed.Add;
        b.ChatLine += (t, _) => chat.Add(t);
        b.SendMeso(10);
        b.OnTrade(new TradeMessage(6) { Flag = 1 });
        Assert.That((closed.Single(), b.LastMesoSent), Is.EqualTo((TradeBook.DialogId, 0L)));
        Assert.That(chat.Single(), Is.EqualTo("#826"));
    }

    [TestCase(0L, 0f, 3, 0L)]
    [TestCase(1000L, 0f, 3, 30L)]
    [TestCase(1000L, 0.5f, 3, 15L)]
    [TestCase(999L, 0f, 3, 29L)]
    [TestCase(1000L, 2f, 3, 0L)]
    public void TheFee(long meso, float rate, int percent, long want) =>
        Assert.That(TradeDialogController.TradeFee(meso, rate, percent), Is.EqualTo(want));

    [TestCase(0L, true)]
    [TestCase(99L, false)]
    [TestCase(100L, true)]
    [TestCase(1000L, true)]
    [TestCase(1001L, false)]
    public void TheMesoRange(long meso, bool want) => Assert.That(TradeDialogController.MesoInRange(meso, 100, 1000), Is.EqualTo(want));

    [TestCase(0, 1)]
    [TestCase(9, 1)]
    [TestCase(10, 2)]
    [TestCase(99999, 5)]
    [TestCase(-5, 2)]
    public void TheCountBoxDigits(int n, int want) => Assert.That(InventoryController.DigitCount(n), Is.EqualTo(want));

    [Test]
    public void TheNumberStringMasksTheNonZeroGroups()
    {
        string Key(string k) => k + "|{0}|{1}|{2}";
        Assert.That(NumberText.NumberString("123", Key, 2), Is.EqualTo(NumberText.Group("123", 2)), "one group: grouped");
        // language 2 (3 digits a group): 1,000,005 -> groups 5, 0, 1, 0
        Assert.That(NumberText.NumberString("1000005", Key, 2), Is.EqualTo("s_number_string_0101|5|0|1"));
        // language 0 (4 digits a group): 120000 -> 0, 12
        Assert.That(NumberText.NumberString("120000", Key, 0), Is.EqualTo("s_number_string_0010|0|12|0"));
        Assert.That(NumberText.DigitGroupSize(1), Is.EqualTo(4));
        Assert.That(NumberText.DigitGroupSize(4), Is.EqualTo(3));
    }

    static Item Trade(int rule, Action<EquipItem> f = null)
    {
        var i = Item.Create(new ItemData { Id = 1 }, 1);
        i.Record = new EquipItem();
        i.Record.Transfer.Rule = rule;
        f?.Invoke(i.Record);
        return i;
    }

    [Test]
    public void TheTradeRuleNeedsBit4OrACountAndNoBindOrLock()
    {
        Func<int, ItemData> none = _ => null;
        Assert.That(ItemTradeRules.CanTrade(Trade(4), none, 0, out _), Is.True);
        Assert.That(ItemTradeRules.CanTrade(Trade(0), none, 0, out int r), Is.False);
        Assert.That(r, Is.EqualTo(1));
        Assert.That(ItemTradeRules.CanTrade(Trade(0x10, e => e.Transfer.Count = 1), none, 0, out _), Is.True);
        Assert.That(ItemTradeRules.CanTrade(Trade(0x10), none, 0, out _), Is.False, "the count rule with none left");
        Assert.That(ItemTradeRules.CanTrade(Trade(4, e => e.Transfer.HasBinding = true), none, 0, out _), Is.False);
        var locked = Trade(4);
        locked.LockState = 1;
        Assert.That(ItemTradeRules.CanTrade(locked, none, 0, out _), Is.False);
        var unlocking = Trade(4);
        unlocking.UnlockTime = 100;
        Assert.That(ItemTradeRules.IsLocked(unlocking, 50), Is.True, "unlock time ahead");
        Assert.That(ItemTradeRules.IsLocked(unlocking, 100), Is.False);
        Assert.That(ItemTradeRules.CanTrade(Trade(4, e => e.Enchant.HistoryEnabled = false), none, 0, out r), Is.False);
        Assert.That(r, Is.EqualTo(1));
        Assert.That(ItemTradeRules.CanTrade(null, none, 0, out _), Is.False);
    }

    [Test]
    public void AGemMustBeFreeAndOfATradeableType()
    {
        var tradeable = new ItemData { Id = 2, TransferType = 0 };
        var bound = new ItemData { Id = 3, TransferType = 2 };
        Func<int, ItemData> data = id => id == 2 ? tradeable : id == 3 ? bound : null;
        Item With(params SocketGem[] gems) => Trade(4, e => { e.Socket.MaxSlot = gems.Length; e.Socket.Gems.AddRange(gems); });
        Assert.That(ItemTradeRules.CanTrade(With(new SocketGem { GemId = 2 }, null, new SocketGem { GemId = 99 }), data, 0, out _), Is.True,
            "empty slots and gems with no data are skipped");
        Assert.That(ItemTradeRules.CanTrade(With(new SocketGem { GemId = 3 }), data, 0, out int r), Is.False);
        Assert.That(r, Is.EqualTo(2));
        Assert.That(ItemTradeRules.CanTrade(With(new SocketGem { GemId = 2, HasBind = true, BindCharacterId = 1 }), data, 0, out _), Is.False);
        var gemLocked = With(new SocketGem { GemId = 2, HasLock = true, LockState = 0, LockExpireTime = 10 });
        Assert.That(ItemTradeRules.CanTrade(gemLocked, data, 5, out _), Is.False);
        Assert.That(ItemTradeRules.NotLocked(gemLocked, 5, out r), Is.False);
        Assert.That(r, Is.EqualTo(2));
        Assert.That(ItemTradeRules.NotLocked(gemLocked, 10, out _), Is.True, "the lock expired");
        Assert.That(new[] { 0, 5, 2, 4, 3, 1 }.Select(ItemTradeRules.ComputeRule), Is.EqualTo(new[] { 4, 0x10, 8, 8, 0x18, 0 }));
    }

    [Test]
    public void TheCountBoxIsForAnUnlockedStack()
    {
        var stack = Item.Create(new ItemData { Id = 1, SlotMax = 100 }, 1);
        stack.Count = 5;
        Assert.That(ItemTradeRules.AsksCount(stack, 0), Is.True);
        stack.Count = 1;
        Assert.That(ItemTradeRules.AsksCount(stack, 0), Is.False, "a count of 1");
        stack.Count = 5;
        stack.LockState = 1;
        Assert.That(ItemTradeRules.AsksCount(stack, 0), Is.False, "locked");
        var gear = Item.Create(new ItemData { Id = 1, Type = 1, SlotMax = 100 }, 1);
        gear.Count = 5;
        Assert.That(ItemTradeRules.AsksCount(gear, 0), Is.False, "equipment");
    }
}
