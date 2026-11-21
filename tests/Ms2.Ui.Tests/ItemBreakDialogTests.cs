using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;
using Maple2.PacketLib.Tools;

namespace Ms2.Ui.Tests;

/// <summary>The item break dialogs (uiitembreakdialog, uiitembreakdonationdialog) over the manager (needs MS2_DATA): the movie's
/// children, the sends on load / close / break, the rarity confirms, the result and preview rows, the slot paths.</summary>
public class ItemBreakDialogTests
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

    static string Hex(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return Convert.ToHexString(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    sealed class Rig
    {
        public ItemBreakDialogController C;
        public ItemBreakManager M = new();
        public List<IOutPacket> Sent = new();
        public List<MessageBoxData> Boxes = new();
        public List<string> Sounds = new();
        public Dictionary<long, Item> Inv = new();
        public UiModel Model;
        public UiHost Host;

        public Rig(bool donation = false)
        {
            Packs();
            Model = new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true };
            M.Send = Sent.Add;
            M.Find = u => Inv.GetValueOrDefault(u);
            M.ShowBox = Boxes.Add;
            C = new ItemBreakDialogController(donation) { Mgr = M, ShowData = Boxes.Add };
            C.Sound += Sounds.Add;
            var (rt, p) = src.Open(C.Movie, r => r.Budget = 20_000_000);
            p.StageW = 1280; p.StageH = 720;
            p.Translate = k => k.TrimStart('$');
            Host = new UiHost(p, C, Model);
            Assert.That(Host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
            for (int i = 0; i < 10; i++) p.Tick();
        }

        public Item Add(long uid, int rarity = 3)
        {
            int id = new[] { 20000001, 20000002, 30000001, 11300001, 12000001 }.FirstOrDefault(i => !string.IsNullOrEmpty(Model.Tables.ItemName(i)));
            if (id == 0) Assert.Ignore("no known item");
            var it = Item.Create(Model.Tables.Items, id, rarity);
            it.Uid = uid; it.Rarity = rarity;
            it.Record = new EquipItem { Id = id, Uid = uid };
            Inv[uid] = it;
            return it;
        }

        public void Put(long uid, int slot, int amount = 1)
        {
            M.OnPacket(new ItemDismantlePacket(1) { Uid = uid, Slots = new[] { new DismantleSlot((ushort)slot, uid, amount) } });
        }
    }

    [Test]
    public void LoadSendsModeOneAndBindsTheBag()
    {
        var r = new Rig();
        Assert.That(r.M.DialogOpen, Is.True);
        Assert.That(r.M.Type, Is.EqualTo(1));
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ItemDismantleRequest.SetMode(1))));
        Assert.That(Rt.ToString(r.Host.Get(r.Host.Child("m_itemBag"), "bagId")), Is.EqualTo("ItemBreakDialog"));
        Assert.That(r.C.GetBagSlotBindings("ItemBreakDialog").Count(), Is.EqualTo(100));
        Assert.That(r.Host.Child("m_break"), Is.Not.Null);
    }

    [Test]
    public void DonationLoadSendsModeTwoUnderItsOwnName()
    {
        var r = new Rig(true);
        Assert.That(r.M.Type, Is.EqualTo(2));
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ItemDismantleRequest.SetMode(2))));
        Assert.That(Rt.ToString(r.Host.Get(r.Host.Child("m_itemBag"), "bagId")), Is.EqualTo("ItemBreakDonationDialog"));
    }

    [Test]
    public void InventoryButtonsAreRefreshedWhenTheDialogIsUnloadedNotWhenTheCloseStarts()
    {
        // vfunc_26 (@0x1419a1830) is the unload, after the close tween: the inventory's open-dialog test (vfunc_55) then sees it closed
        var r = new Rig();
        var closedAtEvent = new List<bool>();
        r.C.InventoryButtons += () => closedAtEvent.Add(r.C.IsClosed);
        r.C.Close();
        for (int i = 0; i < 300 && !r.C.IsClosed; i++) r.Host.Player.Tick();
        Assert.That(r.C.IsClosed, Is.True);
        Assert.That(closedAtEvent, Is.EqualTo(new[] { true }));
    }

    [Test]
    public void CloseSendsTheResetAndClosesTheManager()
    {
        var r = new Rig();
        r.Sent.Clear();
        r.C.Close();
        Assert.That(r.M.DialogOpen, Is.False);
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ItemDismantleRequest.SetMode(0))));
    }

    [Test]
    public void ListedItemIsBoundAndRemoveSendsModeTwo()
    {
        var r = new Rig();
        var it = r.Add(5);
        r.Put(5, 3);
        Assert.That(r.C.GetBinding("ItemBreakDialog.3"), Is.Not.Null);
        Assert.That(r.C.GetBinding("ItemBreakDialog.4"), Is.Null);
        Assert.That(r.C.BreakSlotUid("ItemBreakDialog.3"), Is.EqualTo(5));
        r.Sent.Clear();
        r.C.RemoveFromSlot(3);
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ItemDismantleRequest.RemoveItem(5))));
    }

    [Test]
    public void RightClickAddOnlyWhenIdle()
    {
        var r = new Rig();
        var it = r.Add(5);
        r.Sent.Clear();
        r.C.AddFromInventory(it);
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ItemDismantleRequest.AddItem(5, 1, -1))));
        r.Sent.Clear();
        r.C.StartBreak();
        r.C.AddFromInventory(r.Add(6));
        Assert.That(r.Sent, Is.Empty, "no add while a break runs");
    }

    [Test]
    public void BreakWithNothingListedShowsTheNoticeBoxAndSendsNothing()
    {
        var r = new Rig();
        r.Sent.Clear();
        r.C.ClickBreak();
        Assert.That(r.Boxes.Single().Type, Is.EqualTo(ItemBreakDialogController.NoItemsBoxType));
        Assert.That(r.Sent, Is.Empty);
        Assert.That(r.C.InProgress, Is.False);
    }

    [Test]
    public void CommonRarityStartsAtOnceAndOnSendBreakSendsModeThree()
    {
        var r = new Rig();
        r.Add(5, 3); r.Put(5, 0);
        r.Sent.Clear();
        r.C.ClickBreak();
        Assert.That(r.Boxes, Is.Empty);
        Assert.That(r.C.InProgress, Is.True);
        Assert.That(r.Sounds, Does.Contain("s_strExtractProgress"));
        Assert.That(r.C.OnCallback("onSendBreak", Array.Empty<object>()), Is.True);
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ItemDismantleRequest.Start())));
    }

    [Test]
    public void RarityFourAsksYesNoAndYesStarts()
    {
        var r = new Rig();
        r.Add(5, 4); r.Put(5, 0);
        r.C.ClickBreak();
        var box = r.Boxes.Single();
        Assert.That(box.Type, Is.EqualTo(ItemBreakDialogController.CommonBoxType));
        Assert.That(r.C.InProgress, Is.False);
        box.Callback(1, box);
        Assert.That(r.C.InProgress, Is.False, "no starts the break not");
        box.Callback(0, box);
        Assert.That(r.C.InProgress, Is.True);
    }

    [TestCase(5)]
    [TestCase(6)]
    public void RarityFiveAndSixNeedTheCheckbox(int rarity)
    {
        var r = new Rig();
        r.Add(5, rarity); r.Put(5, 0);
        r.C.ClickBreak();
        var box = r.Boxes.Single();
        Assert.That(box.Type, Is.EqualTo(ItemBreakDialogController.LegendaryBoxType));
        Assert.That(box.MustCheck, Is.True);
        Assert.That(box.EnterAllowed, Is.False);
        Assert.That(r.C.InProgress, Is.False);
        box.Callback(0, box);
        Assert.That(r.C.InProgress, Is.True);
    }

    [Test]
    public void ResultRowsBindTheRewardsSkipZeroAmountAndEndTheBreak()
    {
        var r = new Rig();
        r.Add(5); r.Put(5, 0);
        r.C.StartBreak();
        int id = new[] { 20000001, 20000002, 30000001, 11300001, 12000001 }.First(i => !string.IsNullOrEmpty(r.Model.Tables.ItemName(i)));
        r.M.OnPacket(new ItemDismantlePacket(3) { Type = 1, Rewards = new[] { new DismantleReward(id, 4), new DismantleReward(id + 1, 0) } });
        Assert.That(r.C.InProgress, Is.False);
        Assert.That(r.Sounds, Does.Contain("s_strExtractEnd"));
        Assert.That(r.M.HasBreakItems, Is.False, "mode 3 clears the list");
        Assert.That(r.Model.ItemSlots.Keys.Where(k => k.StartsWith("ItemBreakDialog_")).Count(), Is.EqualTo(1));
        r.C.Close();
        Assert.That(r.Model.ItemSlots.Keys.Where(k => k.StartsWith("ItemBreakDialog_")), Is.Empty, "close releases the bindings");
    }

    [Test]
    public void PreviewSkipsEmptyRangesAndBindsPerItem()
    {
        var r = new Rig();
        int id = new[] { 20000001, 20000002, 30000001, 11300001, 12000001 }.First(i => !string.IsNullOrEmpty(r.Model.Tables.ItemName(i)));
        r.M.OnPacket(new ItemDismantlePacket(5) { Previews = new[] { new DismantlePreview(id, 1, 3), new DismantlePreview(id + 1, 0, 0) } });
        Assert.That(r.Model.ItemSlots.Keys.Where(k => k.StartsWith("ItemBreakDialog_preview_")).Count(), Is.EqualTo(1));
    }

    [Test]
    public void BundleRegisterSendsTheTypeAndRankOnlyWhenIdle()
    {
        var r = new Rig();
        r.C.OnCallback("OnBundleType", new object[] { 6 });
        r.C.OnCallback("OnBundleRank", new object[] { 4 });
        r.Sent.Clear();
        r.C.OnCallback("OnBundleRegister", Array.Empty<object>());
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ItemDismantleRequest.Options(6, 4))));
        r.C.StartBreak();
        r.Sent.Clear();
        r.C.OnCallback("OnBundleRegister", Array.Empty<object>());
        Assert.That(r.Sent, Is.Empty);
    }

    [Test]
    public void DonationDoesNotTakeTheBundleCallbacks()
    {
        var r = new Rig(true);
        Assert.That(r.C.OnCallback("OnBundleRegister", Array.Empty<object>()), Is.False);
    }

    [Test]
    public void ScrollTargetFollowsTheClientRule()
    {
        Assert.That(ItemBreakDialogController.ScrollTarget(true, 0, 44, 0), Is.EqualTo(6));
        Assert.That(ItemBreakDialogController.ScrollTarget(true, 0, 4, 0), Is.EqualTo(0));
        Assert.That(ItemBreakDialogController.ScrollTarget(false, 25, 99, 0), Is.EqualTo(3), "below the 3 rows: row 5 at the bottom");
        Assert.That(ItemBreakDialogController.ScrollTarget(false, 5, 99, 3), Is.EqualTo(1), "above the top");
        Assert.That(ItemBreakDialogController.ScrollTarget(false, 12, 99, 1), Is.EqualTo(1), "visible");
    }

    [Test]
    public void DroppedOnInventoryReplacesOrRemoves()
    {
        var r = new Rig();
        var a = r.Add(5); a.Slot = 7; r.Put(5, 2);
        var b = r.Add(6);
        r.Sent.Clear();
        r.C.DroppedOnInventory(5, b);
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ItemDismantleRequest.AddItem(6, 1, 7))));
        r.Sent.Clear();
        r.C.DroppedOnInventory(5, null);
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ItemDismantleRequest.RemoveItem(5))));
    }

    [Test]
    public void IdleProcessAllocatesLittle()
    {
        var r = new Rig();
        r.Add(5); r.Put(5, 0);
        for (int i = 0; i < 30; i++) r.Host.Player.Tick();
        Assert.That(Alloc.Least(() => { for (int i = 0; i < 100; i++) r.Host.Player.Tick(); }), Is.LessThan(2_000_000));
    }
}
