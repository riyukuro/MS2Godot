using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The item preview window (uiitempreviewdialog, UI 0x4ec2; needs MS2_DATA): OpenPreview's refusals and notices, the
/// shown item, the movie's message and initData, the close.</summary>
public class ItemPreviewTests
{
    static PackSet packs;
    static MovieSource src;

    static void Packs()
    {
        if (packs != null) return;
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs = TestData.Packs(d);
        src = TestData.Movies(packs);
    }

    sealed class Rig
    {
        public ItemPreviewController C = new();
        public List<string> Notices = new();
        public UiModel Model;
        public Item Item;
        public long Now = 1000;

        public Rig(bool player = true)
        {
            Packs();
            Model = new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = player };
            foreach (int id in new[] { 11300001, 12000001, 20000001, 20000002, 30000001 })
                if ((Item = Item.Create(Model.Tables.Items, id, 1)) != null && !Item.Data.IsGemItem && Item.Data.FunctionName != "ItemChangeBeauty") break;
            if (Item == null) Assert.Ignore("no known item");
            C.Notice = Notices.Add;
            C.Now = () => Now;
        }
    }

    [Test]
    public void ARefusedItemShowsItsNoticeAndOpensNothing()
    {
        var r = new Rig();
        Assert.That(r.C.Open(r.Model, null, 1), Is.False);
        Assert.That(r.C.Open(new Rig(player: false).Model, r.Item, 1), Is.False, "no local player");

        int old = r.Item.Data.DoNotPreview;
        r.Item.Data.DoNotPreview = 1;
        try
        {
            Assert.That(r.C.Open(r.Model, r.Item, 1), Is.False);
            Assert.That(r.Notices, Is.EqualTo(new[] { r.Model.Tables.Common(ItemPreviewController.NotPreviewableText) }));
            Assert.That(r.C.Shown, Is.Null);
        }
        finally { r.Item.Data.DoNotPreview = old; }

        r.Notices.Clear();
        r.Item.ExpiryTime = 500;
        Assert.That(r.C.Open(r.Model, r.Item, 1), Is.False);
        Assert.That(r.Notices, Is.EqualTo(new[] { r.Model.Tables.Common(ItemPreviewController.ExpiredText) }));
        Assert.That(r.C.Shown, Is.Null);
    }

    [Test]
    public void AnOpenedItemIsShownWithItsModeAndNoNotice()
    {
        var r = new Rig();
        r.Item.ExpiryTime = 5000;                       // not expired yet
        Assert.That(r.C.Open(r.Model, r.Item, 1), Is.True);
        Assert.That(r.Notices, Is.Empty);
        Assert.That(r.C.Arg, Is.EqualTo(1));
        Assert.That(r.C.Source, Is.SameAs(r.Item));
        Assert.That(r.C.Data, Is.SameAs(r.Item.Data));
    }

    /// <summary>The movie loaded over the controller's item (its OnLoadComplete runs in Load).</summary>
    static (UiHost Host, Runtime Rt) Load(Rig r) => Load(r.C, r.Model);

    static (UiHost Host, Runtime Rt) Load(ItemPreviewController c, UiModel model)
    {
        var (rt, p) = src.Open(c.Movie, x => x.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => k.TrimStart('$');
        var host = new UiHost(p, c, model);
        Assert.That(host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 10; i++) p.Tick();
        return (host, rt);
    }

    static bool ActionListShown(UiHost host) => Rt.ToBoolean(host.Get(host.Child("m_actionList"), "visible"));

    [Test]
    public void TheMovieLoadsDrivesItsRenderTargetAndTheCloseClearsTheItem()
    {
        var r = new Rig();
        Assert.That(r.Item.Data.IsEquip, Is.True, "the rig's item is an equipment item");
        r.Item.Uid = 0x7000_0000_0000_0123;
        Assert.That(r.C.Open(r.Model, r.Item, 1), Is.True);
        Assert.That(r.C.Avatar, Is.EqualTo(ItemPreviewController.AvatarKind.Pc));
        var (host, rt) = Load(r);
        Assert.That(ActionListShown(host), Is.True, "initData's showActionList for an equipment item");
        Assert.That(rt.Errors, Is.Empty);

        int closed = 0;
        r.C.Closed += () => closed++;
        r.C.Close();
        r.C.Close();
        Assert.That(closed, Is.EqualTo(1));
        Assert.That(r.C.Shown, Is.Null);
        Assert.That(r.C.Source, Is.Null);
    }

    [Test]
    public void TheEquipSlotShowsTheItemWithItsUidAndTheItemTooltip()
    {
        var r = new Rig();
        r.Item.Uid = 0x7000_0000_0000_0123;
        Assert.That(r.C.Open(r.Model, r.Item, 1), Is.True);
        var (host, rt) = Load(r);
        Assert.That(rt.Errors, Is.Empty);

        var slot = r.C.EquipSlot.ToDictionary(m => m.Item1, m => m.Item2);
        Assert.That(slot["itemID"], Is.EqualTo((uint)r.Item.Data.Id));
        Assert.That(slot["itemGUID"], Is.EqualTo("8070450532247929123"), "0x100: the uid as text");
        Assert.That(slot["disableDrag"], Is.EqualTo(1), "0x400");
        Assert.That(slot.ContainsKey("itemName"), Is.True, "0x800");

        var box = host.Child("m_equipContainer");
        Assert.That(Rt.ToNumber(host.Get(box, "numChildren")), Is.EqualTo(1), "equipItem(0x81) made the icon");
        var icon = host.Invoke(box, "getChildAt", 0);
        Assert.That(host.Get(icon, "_externalGetTooltip"), Is.InstanceOf<AsPlainFunction>(), "the icon's tooltip is GetItemTooltip");
        var tip = ((AsFunction)host.Get(icon, "_externalGetTooltip")).Call(null, Array.Empty<object>());
        Assert.That(Rt.ToNumber(host.Get(tip, "tooltipAlign")), Is.EqualTo(5));
        Assert.That(Rt.ToString(host.Get(tip, "tooltip")), Is.EqualTo("export_common_tooltip_kindsItem"));
        Assert.That(Rt.ToString(host.Get(tip, "itemName")), Is.EqualTo(slot["itemName"]), "the source item's tooltip");
    }

    [Test]
    public void TheAvatarAndTheActionListFollowTheItemKind()
    {
        Packs();
        var tables = new GameTables(packs, "en");
        var seen = new Dictionary<string, (int Id, ItemPreviewController.AvatarKind Avatar, bool Actions)>();
        foreach (int id in ItemFunctionSweepTests.ItemIds(packs))
        {
            var d = tables.Items.Get(id);
            if (d == null) continue;
            string kind = d.IsEquip ? "equip" : d.IsRideItem ? "ride" : d.IsSkillItem ? "skill" : d.Type == 0x18 ? "emotion"
                : d.IsInstallItem ? "install" : d.IsPetItem ? "pet" : d.IsConsumable ? "consumable" : null;
            if (kind == null || seen.ContainsKey(kind)) continue;
            seen[kind] = (id, ItemPreviewController.AvatarFor(d), ItemPreviewController.ShowsActionList(d));
        }
        TestContext.Out.WriteLine(string.Join("\n", seen.Select(kv => $"{kv.Key}: {kv.Value}")));
        Assert.That(seen.Keys, Is.SupersetOf(new[] { "equip", "ride", "install", "pet", "consumable" }));
        Assert.That(seen["equip"].Avatar, Is.EqualTo(ItemPreviewController.AvatarKind.Pc));
        Assert.That(seen["equip"].Actions, Is.True);
        Assert.That(seen["ride"].Avatar, Is.EqualTo(ItemPreviewController.AvatarKind.Pc));
        Assert.That(seen["ride"].Actions, Is.False);
        if (seen.TryGetValue("skill", out var skill)) Assert.That(skill.Actions, Is.False);
        if (seen.TryGetValue("emotion", out var emotion)) Assert.That((emotion.Avatar, emotion.Actions), Is.EqualTo((ItemPreviewController.AvatarKind.Pc, false)));
        Assert.That(seen["install"].Avatar, Is.EqualTo(ItemPreviewController.AvatarKind.Cube));
        Assert.That(seen["pet"].Avatar, Is.EqualTo(ItemPreviewController.AvatarKind.Pet));
        Assert.That(seen["consumable"].Avatar, Is.EqualTo(ItemPreviewController.AvatarKind.None));
    }

    [Test]
    public void ARideItemHidesTheActionList()
    {
        var r = new Rig();
        var ride = ItemFunctionSweepTests.ItemIds(packs).Select(id => r.Model.Tables.Items.Get(id)).FirstOrDefault(d => d is { IsRideItem: true, DoNotPreview: 0 });
        if (ride == null) Assert.Ignore("no ride item");
        Assert.That(r.C.Open(r.Model, Item.Create(r.Model.Tables.Items, ride.Id, 1), 1), Is.True);
        var (host, rt) = Load(r);
        Assert.That(ActionListShown(host), Is.False);
        Assert.That(rt.Errors, Is.Empty);
    }

    [Test]
    public void AShopItemWithAPaletteAndNoDefaultColourSaysItsColourIsRandom()
    {
        var r = new Rig();
        var models = r.Model.Tables.ItemModels;
        var dyed = ItemFunctionSweepTests.ItemIds(packs).Select(id => r.Model.Tables.Items.Get(id))
            .FirstOrDefault(d => d is { DoNotPreview: 0, IsGemItem: false } && d.FunctionName != "ItemChangeBeauty"
                && models.ForItem(d) is { Palette: not 0 } m && m.DefaultColor(0) == -1);
        if (dyed == null) Assert.Ignore("no item with a palette and no default colour");
        TestContext.Out.WriteLine($"item {dyed.Id}");
        foreach (var (arg, text) in new[] { (1, ItemPreviewController.NoSlotText), (0, ItemPreviewController.PlainText) })
        {
            var c = new ItemPreviewController { Notice = r.Notices.Add, Now = () => r.Now };
            Assert.That(c.Open(r.Model, Item.Create(r.Model.Tables.Items, dyed.Id, 1), arg), Is.True);
            var (host, _) = Load(c, r.Model);
            Assert.That(c.Message, Is.EqualTo(r.Model.Tables.Common(text)), $"mode {arg}");
            Assert.That(Rt.ToString(host.Get(host.Child("m_message"), "text")), Is.EqualTo(c.Message));
        }
        if (models.ForItem(r.Item.Data) is { Palette: not 0 } own && own.DefaultColor(0) == -1) return;
        var plain = new ItemPreviewController();
        Assert.That(plain.Open(r.Model, r.Item, 1), Is.True);
        Load(plain, r.Model);
        Assert.That(plain.Message, Is.EqualTo(r.Model.Tables.Common(ItemPreviewController.PlainText)), "an item with a default colour or no palette");
    }

    [Test]
    public void EveryItemHasItsAvatarActionListMessageAndEquipSlot()
    {
        Packs();
        var model = new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true };
        var kinds = new SortedDictionary<string, int>();
        int items = 0, refused = 0, noSlotText = 0, noActions = 0;
        var failed = new List<string>();
        foreach (int id in ItemFunctionSweepTests.ItemIds(packs))
        {
            if (model.Tables.Items.Get(id) is not { } d) continue;
            items++;
            var item = Item.Create(model.Tables.Items, id, 1);
            var c = new ItemPreviewController();
            try
            {
                if (!c.Open(model, item, 1)) { refused++; continue; }
                kinds[c.Avatar.ToString()] = kinds.GetValueOrDefault(c.Avatar.ToString()) + 1;
                if (c.Avatar == ItemPreviewController.AvatarKind.Pc && !ItemPreviewController.ShowsActionList(d)) noActions++;
                if (model.Tables.ItemModels.ForItem(d) is { Palette: not 0 } m && m.DefaultColor(0) == -1) noSlotText++;
                var slot = new List<(string, object)>();
                if (!ItemSlot.Build(slot, item, true, ItemPreviewController.EquipSlotFlags, model) || !slot.Any(x => x.Item1 == "itemGUID"))
                    failed.Add($"{id}: no slot");
            }
            catch (Exception e) { failed.Add($"{id}: {e.GetType().Name} {e.Message}"); }
        }
        TestContext.Out.WriteLine($"items {items}: refused {refused} (doNotPreview, a gem or a beauty coupon), avatars " +
                                  string.Join(", ", kinds.Select(k => $"{k.Key} {k.Value}")) +
                                  $"; {noActions} on the PC avatar without the action list; {noSlotText} with the random colour text in mode 1");
        foreach (var f in failed.Take(20)) TestContext.Out.WriteLine("  " + f);
        Assert.That(failed, Is.Empty);
        Assert.That(kinds.GetValueOrDefault("Pc"), Is.GreaterThan(0));
    }
}
