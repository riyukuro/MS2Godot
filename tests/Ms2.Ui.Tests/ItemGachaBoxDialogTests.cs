using System.Globalization;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The capsule machine of an OpenGachaBox item (UIItemGachaBoxDialog, uiitemgachaboxdialog, UI id 0x4f8b), needs MS2_DATA:
/// every gacha item of the data and the list its machine shows for each job group and gender, then the dialog over the real
/// movie: its bags, the open checks and their refusals (nothing sent), the use it sends, the coin shop and the result's slots.
/// No language's strings are compared.
/// </summary>
public class ItemGachaBoxDialogTests
{
    static PackSet packs;
    static MovieSource src;
    static readonly int[] JobGroups = { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110 };

    static void Packs()
    {
        if (packs != null) return;
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs = TestData.Packs(d);
        src = TestData.Movies(packs);
    }

    /// <summary>The OpenGachaBox items whose gacha (argument 0) is in the gacha table and lists something for job 10 / gender 0.</summary>
    static IEnumerable<int> GachaItems(GameTables t) => ItemFunctionSweepTests.ItemIds(packs).Where(id =>
        t.Items.Get(id) is { FunctionName: "OpenGachaBox" } d && t.GachaBoxes.HasBox(d.FunctionArgInt(0, 0)));

    [Test]
    public void EveryGachaItemsMachineListsItsDropsByRankAcrossItsThreeBags()
    {
        Packs();
        var t = new GameTables(packs, "en");
        int items = 0, noGacha = 0, listed = 0, emptyForAll = 0, someEmpty = 0, withMachine = 0, withCoin = 0;
        var bad = new List<string>();
        foreach (int id in ItemFunctionSweepTests.ItemIds(packs))
        {
            if (t.Items.Get(id) is not { FunctionName: "OpenGachaBox" } d) continue;
            items++;
            int gacha = d.FunctionArgInt(0, 0);
            if (!t.GachaBoxes.HasBox(gacha)) { noGacha++; continue; }
            if (d.FunctionArgInt(1, 0) != 0) withMachine++;
            if (t.GachaBoxes.Coin(gacha) is { ItemId: > 0 }) withCoin++;
            int empty = 0;
            foreach (int job in JobGroups)
                for (int gender = 0; gender < 2; gender++)
                {
                    var list = ItemGachaBoxDialogController.Drops(t, gacha, job, gender);
                    if (list.Count == 0) { empty++; continue; }
                    for (int i = 1; i < list.Count; i++)
                        if (list[i].Rank > list[i - 1].Rank) { bad.Add($"{id}: gacha {gacha} not sorted by rank"); break; }
                    if (list.Any(e => t.Items.Get(e.ItemId) == null)) bad.Add($"{id}: gacha {gacha} lists an item without data");
                }
            if (empty == JobGroups.Length * 2) emptyForAll++;
            else { listed++; if (empty > 0) someEmpty++; }
        }
        TestContext.Out.WriteLine($"OpenGachaBox items: {items}; {noGacha} name no gacha of the table; {listed} list drops " +
                                  $"({someEmpty} not for every job and gender), {emptyForAll} list nothing (no initData, as in the client); " +
                                  $"{withMachine} need a capsule machine item, {withCoin} have a coin shop");
        foreach (var b in bad.Take(40)) TestContext.Out.WriteLine("  " + b);
        Assume.That(items, Is.GreaterThan(0), "no OpenGachaBox item");
        Assert.That(bad, Is.Empty);
        // the bags hold 14 slots each and every one of the 35 slots is shown
        Assert.That(ItemGachaBoxDialogController.BagSlotOrder.Select(b => b.Length), Is.EqualTo(new[] { 14, 14, 14 }));
        Assert.That(ItemGachaBoxDialogController.BagSlotOrder.SelectMany(b => b).Distinct().Count(), Is.EqualTo(ItemGachaBoxDialogController.SlotCount));
        Assert.That(ItemGachaBoxDialogController.BagSlotOrder[2].Take(7), Is.EqualTo(ItemGachaBoxDialogController.BagSlotOrder[0].Skip(7)),
            "bag 2 starts with bag 0's last seven slots");
    }

    sealed class Rig
    {
        public readonly ItemGachaBoxDialogController C = new();
        public readonly UiModel Model;
        public UiHost Host;
        public readonly Item Box;
        public readonly int Gacha;
        public int Have = 1, MachineHave;
        public bool UseOk = true;
        public readonly List<string> Uses = new();
        public readonly List<SystemShopCoinRequest> Shops = new();
        public readonly List<string> Notices = new();
        public readonly List<MessageBoxData> Boxes = new();
        public readonly List<int> Dialogs = new();
        public readonly List<string> Sounds = new();

        public Rig(int itemId)
        {
            Model = new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true };
            Box = Item.Create(Model.Tables.Items, itemId, 1);
            Box.Uid = 77;
            Gacha = Box.Data.FunctionArgInt(0, 0);
            int machine = Box.Data.FunctionArgInt(1, 0);
            C.FindItemById = id => id == itemId && Have > 0 ? Box : null;
            C.HaveCount = id => id == itemId ? Have : id == machine ? MachineHave : 0;
            C.RequestUse = (item, mode) => { Uses.Add(mode); return UseOk; };
            C.Self = () => (10, 0);
            C.Send = Shops.Add;
            C.PlaySound = k => { Sounds.Add(k); return 1; };
            C.NoticeText += Notices.Add;
            C.ShowBox = Boxes.Add;
            C.OpenDialog = Dialogs.Add;
            C.SetBoxItemId(Model, itemId);
        }

        public void Load()
        {
            var (rt, p) = src.Open(C.Movie, r => r.Budget = 20_000_000);
            p.StageW = 1280; p.StageH = 720;
            p.Translate = k => k.TrimStart('$');
            Host = new UiHost(p, C, Model);
            Assert.That(Host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
            for (int i = 0; i < 10; i++) p.Tick();
        }
    }

    static int FirstGachaItem(bool listed = true)
    {
        Packs();
        var t = new GameTables(packs, "en");
        foreach (int id in GachaItems(t))
            if (!listed || ItemGachaBoxDialogController.Drops(t, t.Items.Get(id).FunctionArgInt(0, 0), 10, 0).Count > 0) return id;
        Assert.Ignore("no OpenGachaBox item with a listed gacha in this data");
        return 0;
    }

    [Test]
    public void TheMachinesBagsHoldTheGachasDropsInTheClientsSlotOrder()
    {
        var r = new Rig(FirstGachaItem());
        Assert.That(r.C.BoxItemId, Is.EqualTo(r.Box.ItemId));
        r.Load();
        var drops = ItemGachaBoxDialogController.Drops(r.Model.Tables, r.Gacha, 10, 0);
        for (int k = 0; k < ItemGachaBoxDialogController.BagCount; k++)
        {
            string bag = "ItemGachaBoxBag" + k.ToString(CultureInfo.InvariantCulture);
            var names = r.C.GetBagSlotBindings(bag).ToList();
            Assert.That(names, Has.Count.EqualTo(14), bag);
            for (int i = 0; i < names.Count; i++)
            {
                var slot = r.Model.ItemSlots[names[i]];
                int at = ItemGachaBoxDialogController.BagSlotOrder[k][i] % drops.Count;
                Assert.That(slot.Item.ItemId, Is.EqualTo(drops[at].ItemId), $"{bag} slot {i}");
                Assert.That(slot.IconType, Is.EqualTo(ItemGachaBoxDialogController.IconType));
                Assert.That(slot.Flags, Is.EqualTo(ItemGachaBoxDialogController.SlotFlags));
            }
        }
        r.C.Unload();
        Assert.That(r.Model.ItemSlots.Keys.Where(n => n.StartsWith("ItemGachaBoxBag", StringComparison.Ordinal)), Is.Empty, "the unload releases the bags");
    }

    [Test]
    public void TheOpenChecksRefuseWithoutTheItemsAndSendNothing()
    {
        var r = new Rig(FirstGachaItem(false));
        r.Load();
        r.Have = 0;
        r.C.OnCallback("onCanOpenBox", new object[] { false });
        Assert.That(r.Boxes, Has.Count.EqualTo(1), "no box item: the shop link box");
        Assert.That(r.Boxes[0].Type, Is.EqualTo(1));
        r.Boxes[0].Callback(0, r.Boxes[0]);
        Assert.That(r.Dialogs, Is.EqualTo(new[] { ItemGachaBoxDialogController.ShopLinkDialogId }), "its yes opens the shop");
        r.C.SendOpenBox();
        Assert.That(r.Uses, Is.Empty);

        r.Have = 9;
        r.C.OnCallback("onCanOpenBox", new object[] { true });
        Assert.That(r.Boxes, Has.Count.EqualTo(2), "nine boxes for a ten open: the shop link box");
        r.C.OnCallback("onSendOpenBox", new object[] { 1 });
        Assert.That(r.Uses, Is.Empty, "nothing sent for a refused open");
        Assert.That(r.C.Sent, Is.False);
    }

    [Test]
    public void AnOpenSendsTheUseWithItsModeAndAFailedSendClosesTheDialog()
    {
        var r = new Rig(FirstGachaItem(false));
        r.Load();
        r.Have = 10;
        r.C.OnCallback("onCanOpenBox", new object[] { false });
        r.C.OnCallback("onSendOpenBox", new object[] { 1 });
        r.C.OnCallback("onCanOpenBox", new object[] { true });
        r.C.OnCallback("onSendOpenBox", new object[] { 1 });
        Assert.That(r.Uses, Is.EqualTo(new[] { "single", "multi" }));
        Assert.That(r.C.Sent, Is.True);
        Assert.That(r.Boxes, Is.Empty);
        r.UseOk = false;
        r.C.SendOpenBox();
        Assert.That(r.C.BoxItemId, Is.EqualTo(0), "a use not sent closes the dialog (vfunc_67 clears the box)");
    }

    [Test]
    public void TheCapsuleMachineIsNeededOnlyWithItsFeature()
    {
        Packs();
        var t = new GameTables(packs, "en");
        int id = GachaItems(t).FirstOrDefault(i => t.Items.Get(i).FunctionArgInt(1, 0) != 0);
        if (id == 0) Assert.Ignore("no OpenGachaBox item that names a capsule machine in this data");
        var r = new Rig(id) { Have = 1, MachineHave = 0 };
        r.Load();
        r.Model.Features.Remove(ItemGachaBoxDialogController.CapsuleMachineFeature);
        Assert.That(r.C.CanOpenBox(), Is.EqualTo(0));
        r.Model.Features.Add(ItemGachaBoxDialogController.CapsuleMachineFeature);
        Assert.That(r.C.CanOpenBox(), Is.EqualTo(2));
        r.C.OnCallback("onCanOpenBox", new object[] { false });
        Assert.That(r.Notices, Has.Count.EqualTo(1), "the machine notice");
        r.MachineHave = 1;
        Assert.That(r.C.CanOpenBox(), Is.EqualTo(0));
    }

    [Test]
    public void TheCoinShopButtonAsksTheSystemShopForTheGachasCoin()
    {
        Packs();
        var t = new GameTables(packs, "en");
        int id = GachaItems(t).FirstOrDefault(i => t.GachaBoxes.Coin(t.Items.Get(i).FunctionArgInt(0, 0)) is { ItemId: > 0 });
        if (id == 0) Assert.Ignore("no gacha with a coin item in this data");
        var r = new Rig(id);
        r.Load();
        r.C.OnCallback("onOpenCoinShop", Array.Empty<object>());
        Assert.That(r.Shops.Select(s => s.ItemId), Is.EqualTo(new[] { t.GachaBoxes.Coin(r.Gacha).Value.ItemId }));
    }

    [Test]
    public void TheResultBindsOneSlotOrTenAndIgnoresTheLulluMode()
    {
        var r = new Rig(FirstGachaItem());
        r.Load();
        var drops = ItemGachaBoxDialogController.Drops(r.Model.Tables, r.Gacha, 10, 0);
        int a = drops[0].ItemId, b = drops[^1].ItemId;
        r.Have = 10;
        r.C.SendOpenBox();
        Assert.That(r.C.Sent, Is.True);
        r.C.OnResult(new ItemScriptBoxResult(ItemScriptBoxResult.LulluMode, new[] { new ItemScriptBoxItem(a, 1, 1, false) }));
        Assert.That(r.C.SlotBinds, Is.Empty, "mode 0 is the Lullu box's");
        Assert.That(r.C.Sent, Is.False, "any ItemScript clears the sent flag");

        r.C.OnResult(new ItemScriptBoxResult(ItemScriptBoxResult.GachaMode, new[] { new ItemScriptBoxItem(a, 3, 1, false) }));
        Assert.That(r.C.SlotBinds, Is.EqualTo(new[] { "ItemGacha_ItemGachaBoxDialog_1" }));
        Assert.That(r.Model.ItemSlots["ItemGacha_ItemGachaBoxDialog_1"].Item.ItemId, Is.EqualTo(a));
        Assert.That(r.Sounds, Does.Contain(ItemGachaBoxDialogController.NormalSound));
        Assert.That(r.Sounds, Does.Not.Contain(ItemGachaBoxDialogController.SpecialSound));

        var ten = Enumerable.Range(0, 10).Select(i => new ItemScriptBoxItem(i % 2 == 0 ? a : b, 1, 1, i == 4)).ToList();
        r.C.OnResult(new ItemScriptBoxResult(ItemScriptBoxResult.GachaMode, ten));
        Assert.That(r.C.SlotBinds, Has.Count.EqualTo(10));
        Assert.That(r.Model.ItemSlots.ContainsKey("ItemGacha_ItemGachaBoxDialog_1"), Is.True);
        Assert.That(r.Model.ItemSlots["ItemGacha_ItemGachaBoxDialog_2"].Item.ItemId, Is.EqualTo(b));
        Assert.That(r.Sounds, Does.Contain(ItemGachaBoxDialogController.SpecialSound));
    }
}
