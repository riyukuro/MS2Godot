using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The Lullu box (UIItemLulluBoxDialog, uiitemlulluboxdialog, UI id 0x4f2b), needs MS2_DATA: every OpenItemBoxLullu /
/// OpenItemBoxLulluSimple item of the data with its key and box tags and its preview, then the dialog over the real movie: the
/// refusals (nothing sent), the buy box of the auto pay, the use it sends on the key item and the result's slots. No
/// language's strings are compared.
/// </summary>
public class ItemLulluBoxDialogTests
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

    static bool IsLullu(ItemData d) => d?.FunctionName is ItemLulluBoxDialogController.BoxFunction or ItemLulluBoxDialogController.SimpleFunction;

    [Test]
    public void EveryLulluItemNamesItsKeyAndBoxTagsAndListsItsPreview()
    {
        Packs();
        var t = new GameTables(packs, "en");
        int items = 0, simple = 0, withPrice = 0, previewed = 0;
        var bad = new List<string>();
        foreach (int id in ItemFunctionSweepTests.ItemIds(packs))
        {
            if (t.Items.Get(id) is not { } d || !IsLullu(d)) continue;
            items++;
            bool isSimple = d.FunctionName == ItemLulluBoxDialogController.SimpleFunction;
            if (isSimple) simple++;
            TestContext.Out.WriteLine($"  {id} {d.FunctionName}: {d.FunctionParameter}");
            int key = ItemTags.ByName(d.FunctionXmlString("keyItemTag"));
            if (key == 0) { bad.Add($"{id}: key tag '{d.FunctionXmlString("keyItemTag")}' is no item tag"); continue; }
            if (t.Items.TagItemId(key) == 0) bad.Add($"{id}: no item of key tag {key}");
            if (!isSimple)
            {
                int box = ItemTags.ByName(d.FunctionXmlString("boxItemTag"));
                if (box == 0) bad.Add($"{id}: box tag '{d.FunctionXmlString("boxItemTag")}' is no item tag");
                if (d.FunctionXmlInt("boxPrice") >= 0 && d.HasFunctionXmlKey("boxPrice")) withPrice++;
            }
            if (JobGroups.Any(j => ItemLulluBoxDialogController.PreviewDrops(t, d, j, 0).Count > 0)) previewed++;
        }
        TestContext.Out.WriteLine($"Lullu items: {items} ({simple} simple), {withPrice} with a box price, {previewed} with a preview");
        foreach (var b in bad) TestContext.Out.WriteLine("  " + b);
        if (items == 0) Assert.Ignore("no Lullu box item in this data");
        Assert.That(bad, Is.Empty);
    }

    sealed class Rig
    {
        public readonly ItemLulluBoxDialogController C = new();
        public readonly UiModel Model;
        public UiHost Host;
        public readonly ItemData Data;
        public readonly int KeyTag, BoxTag;
        public int Keys, Boxes;
        public long Meso;
        public bool UseOk = true;
        public readonly Item Key;
        public readonly List<string> Uses = new();
        public readonly List<string> Notices = new();
        public readonly List<MessageBoxData> Shown = new();
        public readonly List<string> Sounds = new();

        public Rig(int itemId)
        {
            Model = new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true };
            Data = Model.Tables.Items.Get(itemId);
            KeyTag = ItemTags.ByName(Data.FunctionXmlString("keyItemTag"));
            BoxTag = ItemTags.ByName(Data.FunctionXmlString("boxItemTag"));
            Key = Item.Create(Model.Tables.Items, Model.Tables.Items.TagItemId(KeyTag) is var k and not 0 ? k : itemId, 1);
            Key.Uid = 88;
            C.TagCount = tag => tag == KeyTag ? Keys : tag == BoxTag ? Boxes : 0;
            C.FindByTag = tag => tag == KeyTag && Keys > 0 ? Key : null;
            C.RequestUse = (item, mode) => { Assert.That(item, Is.SameAs(Key), "the use is the key item's"); Uses.Add(mode); return UseOk; };
            C.Self = () => (10, 0);
            C.Mesos = () => Meso;
            C.PlaySound = Sounds.Add;
            C.NoticeText += Notices.Add;
            C.ShowBox = Shown.Add;
            C.SetBoxItemId(itemId);
            var (rt, p) = src.Open(C.Movie, r => r.Budget = 20_000_000);
            p.StageW = 1280; p.StageH = 720;
            p.Translate = k2 => k2.TrimStart('$');
            Host = new UiHost(p, C, Model);
            Assert.That(Host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
            for (int i = 0; i < 10; i++) p.Tick();
        }

        public void SetSkip(bool on) => Host.Set(Host.Child("m_isSkipEffect"), "selected", on);
        public void SetAutoPay(bool on) => Host.Set(Host.Child("m_autoPayBox"), "selected", on);
        public bool AutoPay => Rt.ToBoolean(Host.Get(Host.Child("m_autoPayBox"), "selected"));
    }

    static int FirstLullu(string function, bool needPrice = false)
    {
        Packs();
        var t = new GameTables(packs, "en");
        foreach (int id in ItemFunctionSweepTests.ItemIds(packs))
            if (t.Items.Get(id) is { } d && d.FunctionName == function && ItemTags.ByName(d.FunctionXmlString("keyItemTag")) != 0
                && (!needPrice || d.HasFunctionXmlKey("boxPrice") && d.FunctionXmlInt("boxPrice") >= 0))
                return id;
        Assert.Ignore($"no {function} item{(needPrice ? " with a box price" : "")} in this data");
        return 0;
    }

    [Test]
    public void TheBoxIsRefusedWithoutKeysOrBoxesAndSendsNothing()
    {
        var r = new Rig(FirstLullu(ItemLulluBoxDialogController.BoxFunction));
        Assert.That(r.C.Simple, Is.False);
        r.SetSkip(true);
        r.C.OnSelectMode(0);
        Assert.That(r.C.CanOpen(), Is.EqualTo(3), "no key");
        Assert.That(r.Notices.Count + r.Shown.Count, Is.EqualTo(1), "the key's lack: a notice or the shop link box");
        r.Keys = 1;
        r.SetAutoPay(false);
        Assert.That(r.C.CanOpen(), Is.EqualTo(2), "a key, no box, no auto pay");
        r.C.OnCallback("onSendOpenBox", Array.Empty<object>());
        r.C.SendOpenBox();
        Assert.That(r.Uses, Is.Empty, "nothing sent for a refused open");
    }

    [Test]
    public void WithoutBoxesThePricedBoxAsksToBuyThemAndItsYesTurnsTheAutoPayOn()
    {
        var r = new Rig(FirstLullu(ItemLulluBoxDialogController.BoxFunction, true));
        int price = r.Data.FunctionXmlInt("boxPrice");
        r.Keys = 10;
        r.SetSkip(true);
        r.SetAutoPay(false);
        r.Meso = (long)price * 10;
        r.C.OnSelectMode(1);
        Assert.That(r.Uses, Is.Empty, "the buy is asked first");
        Assert.That(r.Shown, Has.Count.EqualTo(1));
        Assert.That(r.Shown[0].Type, Is.EqualTo(1));
        r.Shown[0].Callback(0, r.Shown[0]);
        Assert.That(r.AutoPay, Is.True, "yes checks the auto pay");
        Assert.That(r.Uses, Is.EqualTo(new[] { "multiautoPay" }), "and opens ten, paying for the boxes");

        var poor = new Rig(r.Data.Id) { Keys = 1, Meso = price - 1 };
        poor.SetSkip(true);
        poor.SetAutoPay(true);
        Assert.That(poor.C.CanOpen(), Is.EqualTo(price > 0 ? 1 : 0), "the mesos do not cover the box");
        if (price > 0)
        {
            poor.C.OnSelectMode(0);
            Assert.That(poor.Uses, Is.Empty);
            Assert.That(poor.Shown, Has.Count.EqualTo(1), "the not enough money box");
        }
    }

    [Test]
    public void AnOpenSendsTheKeysUseAndTheResultBindsItsSlots()
    {
        var r = new Rig(FirstLullu(ItemLulluBoxDialogController.BoxFunction));
        r.Keys = 10; r.Boxes = 10;
        r.SetSkip(true);
        r.C.OnSelectMode(0);
        Assert.That(r.Uses, Is.EqualTo(new[] { "single" }));
        Assert.That(r.C.Sent, Is.True);
        r.C.OnSelectMode(1);
        Assert.That(r.Uses, Has.Count.EqualTo(1), "nothing more until the result");

        var preview = ItemLulluBoxDialogController.PreviewDrops(r.Model.Tables, r.Data, 10, 0);
        Assert.That(r.C.PreviewSlots, Has.Count.EqualTo(preview.Count), "the preview bag");
        int a = preview.Count > 0 ? preview[0].ItemId : r.Key.ItemId;
        r.C.OnResult(new ItemScriptBoxResult(ItemScriptBoxResult.GachaMode, new[] { new ItemScriptBoxItem(a, 1, 1, false) }));
        Assert.That(r.C.SlotBinds, Is.Empty, "mode 5 is the gacha's");
        Assert.That(r.C.Sent, Is.False);
        r.C.OnResult(new ItemScriptBoxResult(ItemScriptBoxResult.LulluMode, new[] { new ItemScriptBoxItem(a, 2, 1, true) }));
        Assert.That(r.C.SlotBinds, Is.EqualTo(new[] { "ItemLullu_ItemLulluBoxDialog_1" }));
        Assert.That(r.Model.ItemSlots["ItemLullu_ItemLulluBoxDialog_1"].Item.ItemId, Is.EqualTo(a));
        Assert.That(r.Sounds, Is.EqualTo(new[] { ItemLulluBoxDialogController.OpenSound, ItemLulluBoxDialogController.SpecialSound }));

        r.Sounds.Clear();
        r.C.OnSelectMode(1);
        Assert.That(r.Uses, Is.EqualTo(new[] { "single", "multi" }));
        var ten = Enumerable.Range(0, 10).Select(i => new ItemScriptBoxItem(a, 1, 1, false)).ToList();
        r.C.OnResult(new ItemScriptBoxResult(ItemScriptBoxResult.LulluMode, ten));
        Assert.That(r.C.SlotBinds, Has.Count.EqualTo(10));
        Assert.That(r.Sounds.Take(2), Is.EqualTo(new[] { ItemLulluBoxDialogController.OpenSound, ItemLulluBoxDialogController.TenSound }));

        r.UseOk = false;
        r.C.OnSelectMode(0);
        Assert.That(r.Uses, Is.EqualTo(new[] { "single", "multi", "single" }));
        Assert.That(r.C.Sent, Is.False, "a use not sent is not waited for");
        Assert.That(r.C.SlotBinds, Is.Empty, "and the dialog closed (vfunc_67 releases the slots)");
    }

    [Test]
    public void TheSimpleBoxNeedsOnlyItsKeys()
    {
        var r = new Rig(FirstLullu(ItemLulluBoxDialogController.SimpleFunction));
        Assert.That(r.C.Simple, Is.True);
        r.SetSkip(true);
        Assert.That(r.C.CanOpen(), Is.EqualTo(3));
        r.Keys = 1;
        Assert.That(r.C.CanOpen(), Is.EqualTo(0));
        r.C.OnSelectMode(0);
        Assert.That(r.Uses, Is.EqualTo(new[] { "single" }), "no autoPay on the simple box");
    }
}
