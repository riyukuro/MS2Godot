using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The item enchant dialog (uiitemenchantdialog, 0x4e92) over the enchant book (needs MS2_DATA): the movie's children,
/// both bags, the setEnchantInfo object of each mode, the count / fee / stat texts, every refusal and confirm box, the callbacks'
/// packets and the close. Texts are not compared, so no language's strings.</summary>
public class ItemEnchantDialogTests
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

    sealed class Rig
    {
        public ItemEnchantDialogController C;
        public ItemEnchantBook Book = new();
        public List<IOutPacket> Sent = new();
        public List<MessageBoxData> Boxes = new();
        public List<string> Notices = new();
        public Dictionary<long, Ms2.Ui.Items.Item> Inv = new();
        public UiModel Model;
        public UiHost Host;
        public int Closed;

        public Rig(byte expMode = 1)
        {
            Packs();
            Model = new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true };
            Book.SetModeByName(expMode == 2 ? "expMode" : "");
            Book.Find = u => Inv.TryGetValue(u, out var i) ? i : null;
            Book.Send = Sent.Add;
            Book.CanEnchantItem = i => true;
            C = new ItemEnchantDialogController
            {
                Book = Book, ShowData = Boxes.Add,
                BagItems = cat => cat == 0 ? Inv.Values.ToList() : Array.Empty<Ms2.Ui.Items.Item>(),
                CountOf = (id, rank) => 0,
            };
            C.NoticeText += Notices.Add;
            C.Closed += () => Closed++;
            var (rt, p) = src.Open(C.Movie, r => r.Budget = 20_000_000);
            p.StageW = 1280; p.StageH = 720;
            p.Translate = k => k.TrimStart('$');
            Host = new UiHost(p, C, Model);
            Assert.That(Host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
            for (int i = 0; i < 10; i++) p.Tick();
        }

        int RealId() => new[] { 20000001, 20000002, 30000001, 11300001, 12000001 }.FirstOrDefault(i => !string.IsNullOrEmpty(Model.Tables.ItemName(i)));

        public Ms2.Ui.Items.Item Add(long uid, int level = 0, int charge = 0)
        {
            int id = RealId();
            if (id == 0) Assert.Ignore("no known item");
            var item = Ms2.Ui.Items.Item.Create(Model.Tables.Items, id, 1);
            item.Uid = uid; item.EnchantLevel = level;
            item.Record = new EquipItem { Id = id, Uid = uid };
            item.Record.Enchant.Level = level; item.Record.Enchant.Charge = charge; item.Record.Enchant.Version = 1;
            item.Record.Options.Stat[0][1] = 200;
            Inv[uid] = item;
            return item;
        }

        public void Select(long uid, ItemEnchantMessage.Info info)
        {
            Book.Select(uid);
            Book.Apply(info);
            for (int i = 0; i < 3; i++) Host.Player.Tick();
        }

        public ItemEnchantMessage.Info Info(long uid, short exp = 1, EnchantIngredient[] ing = null, int resultId = 0, int resultCount = 1,
            Dictionary<int, int> options = null, EnchantRates rates = default, long p0 = 0, long p1 = 0, byte failMode = 0) =>
            new(exp, uid, ing ?? new EnchantIngredient[0], 0, options ?? new Dictionary<int, int> { [1] = 100 }, rates, p0, p1, failMode,
                resultId == 0 ? RealId() : resultId, 1, resultCount);

        public object Get(object o, string k) => Host.Get(o, k);
    }

    [Test]
    public void LoadingBindsTheBagAndSendsMode0()
    {
        var r = new Rig();
        Assert.That(r.Book.IsOpen, Is.True);
        Assert.That(r.Sent.OfType<ItemEnchantLoadRequest>().Count(), Is.EqualTo(1));
        Assert.That(r.Host.Child("m_itemBag", r.Host.Child("m_enchantAni")), Is.Not.Null);
        Assert.That(Rt.ToString(r.Host.Get(r.Host.Child("m_itemBag", r.Host.Child("m_enchantAni")), "bagId")), Is.EqualTo(ItemEnchantDialogController.Bag));
        foreach (var n in new[] { "m_compareEnchant", "m_debugEnchantCount" })
            if (r.Host.Child(n, r.Host.Child("m_enchantAni")) is { } c) Assert.That(Rt.ToBoolean(r.Host.Get(c, "visible")), Is.False, n);
    }

    [Test]
    public void TheEnchantableBagHoldsTheUnenchantedItemsThatPassTheLimit()
    {
        var r = new Rig();
        r.Add(5); r.Add(6, level: 3); r.Add(7);
        r.Book.CanEnchantItem = i => i.Uid != 7;
        var list = r.C.EnchantableItems();
        Assert.That(list.Select(i => i.Uid), Is.EqualTo(new long[] { 5 }));
    }

    [Test]
    public void TheInfoObjectOfTheOpheliaCarriesTheItemRateAndStats()
    {
        var r = new Rig();
        r.Add(5);
        r.Select(5, r.Info(5, rates: new EnchantRates(40, 0, 25, 5, 2)));
        var o = r.C.BuildInfo();
        Assert.That(Rt.ToString(r.Get(o, "guid")), Is.EqualTo("5"));
        Assert.That(Rt.ToString(r.Get(o, "frame")), Is.EqualTo("Enchant"));
        Assert.That(Rt.ToString(r.Get(o, "prop")), Is.Not.Empty, "the rate text");
        Assert.That(r.C.Chance, Is.EqualTo(47f));
        Assert.That(Rt.ToString(r.Get(o, "enchantItem")), Is.EqualTo(ItemEnchantDialogController.SelectedSlot));
        Assert.That(r.Model.ItemSlots.ContainsKey(ItemEnchantDialogController.SelectedSlot), Is.True);
        Assert.That(r.Get(o, "options"), Is.Not.Null);
    }

    [Test]
    public void TheFrameFollowsTheModeAndTheFailStack()
    {
        var r = new Rig();
        r.Add(5);
        r.Select(5, r.Info(5, failMode: 1));
        Assert.That(Rt.ToString(r.Get(r.C.BuildInfo(), "frame")), Is.EqualTo("EnchantFailStack"));
        var p = new Rig(2);
        p.Add(5);
        p.Select(5, p.Info(5, exp: 2));
        Assert.That(Rt.ToString(p.Get(p.C.BuildInfo(), "frame")), Is.EqualTo("EnchantExpGauge"));
        Assert.That(p.Get(p.C.BuildInfo(), "expText"), Is.Not.Null);
        Assert.That(p.Get(p.C.BuildInfo(), "failStack"), Is.Null.Or.EqualTo(Undefined.Value));
    }

    [Test]
    public void IngredientTextsAndTheFeeFollowHaveAndNeed()
    {
        var r = new Rig();
        Assert.That(r.C.IngredientCountText(1, 3), Is.Not.EqualTo(r.C.IngredientCountText(3, 3)), "lack text vs the plain one");
        r.Add(5);
        int id = r.Inv[5].ItemId;
        r.C.CountOf = (i, _) => 1;
        r.Select(5, r.Info(5, ing: new[] { new EnchantIngredient(id, 0x60, 4), new EnchantIngredient(id, 1, 1) }));
        var o = r.C.BuildInfo();
        Assert.That(Rt.ToString(r.Get(o, "enchantFee")), Is.Not.Empty, "(4 - 1) * 1000");
        Assert.That(r.Model.ItemSlots.ContainsKey(ItemEnchantDialogController.IngredientPrefix + "0"), Is.True);
        Assert.That(r.Model.ItemSlots.ContainsKey(ItemEnchantDialogController.IngredientPrefix + "1"), Is.True);
        r.C.CountOf = (i, _) => 9;
        Assert.That(Rt.ToString(r.Get(r.C.BuildInfo(), "enchantFee")), Is.Empty);
    }

    [Test]
    public void AnOldChargeAsksTheRefundBoxFirst()
    {
        var r = new Rig();
        var it = r.Add(5, charge: 7); it.Record.Enchant.Version = 0;
        r.Sent.Clear();
        Assert.That(r.C.OnCallback("onSelectItem", new object[] { "ItemEnchantDialog.5" }), Is.True);
        Assert.That(r.Boxes, Has.Count.EqualTo(1));
        Assert.That(r.Sent, Is.Empty);
        r.Boxes[0].Callback(0, r.Boxes[0]);
        Assert.That(r.Sent.Single(), Is.EqualTo(new ItemEnchantRefundRequest(5)));
    }

    [Test]
    public void EnchantAsksTheProtectorBoxWithTheCheckboxAndThenTheWarning()
    {
        var r = new Rig();
        r.Add(5); r.Add(30);
        r.Select(5, r.Info(5, rates: new EnchantRates(40, 0, 25, 0, 0), p0: 30));
        r.Sent.Clear();
        r.C.OnCallback("onEnchant", new object[0]);
        Assert.That(r.Boxes, Has.Count.EqualTo(1));
        Assert.That(r.Boxes[0].Type, Is.EqualTo(ItemEnchantDialogController.ProtectorBoxType));
        Assert.That(r.Boxes[0].Check0Selected, Is.False);
        Assert.That(r.Sent, Is.Empty);
        r.Boxes[0].Callback(0, r.Boxes[0]);                  // yes, unchecked: the dont-use warning
        Assert.That(r.Boxes, Has.Count.EqualTo(2)); Assert.That(r.Sent, Is.Empty);
        r.Boxes[1].Callback(0, r.Boxes[1]);
        Assert.That(r.Sent[0], Is.EqualTo(new ItemEnchantRequest(5, 0)));
        r.Sent.Clear(); r.Boxes.Clear();
        r.C.OnCallback("onEnchant", new object[0]);
        r.Boxes[0].Check0Selected = true;
        r.Boxes[0].Callback(0, r.Boxes[0]);
        r.Boxes[1].Callback(0, r.Boxes[1]);
        Assert.That(r.Sent[0], Is.EqualTo(new ItemEnchantRequest(5, 30)), "the first protector");
    }

    [Test]
    public void EnchantWithoutAProtectorAsksAPlainBoxAndWithoutARateSendsAtOnce()
    {
        var r = new Rig();
        r.Add(5);
        r.Select(5, r.Info(5, rates: new EnchantRates(40, 0, 25, 0, 0)));
        r.Sent.Clear();
        r.C.OnCallback("onEnchant", new object[0]);
        Assert.That(r.Boxes.Single().Type, Is.EqualTo(1));
        r.Boxes[0].Callback(0, r.Boxes[0]);
        Assert.That(r.Sent[0], Is.EqualTo(new ItemEnchantRequest(5, 0)));
        var q = new Rig(); q.Add(5); q.Select(5, q.Info(5)); q.Sent.Clear(); q.Boxes.Clear();
        q.C.OnCallback("onEnchant", new object[0]);
        Assert.That(q.Boxes, Is.Empty); Assert.That(q.Sent[0], Is.EqualTo(new ItemEnchantRequest(5, 0)));
    }

    [Test]
    public void AChargeAsksThePaybackBoxAndPeachyAsksTheBindingBox()
    {
        var r = new Rig();
        r.Add(5, charge: 50);
        r.Select(5, r.Info(5)); r.Sent.Clear();
        r.C.OnCallback("onEnchant", new object[0]);
        Assert.That(r.Boxes, Has.Count.EqualTo(1));
        Assert.That(r.Sent, Is.Empty);
        r.Boxes[0].Callback(1, r.Boxes[0]);                  // no
        Assert.That(r.Sent, Is.Empty);
        r.Boxes[0].Callback(0, r.Boxes[0]);
        Assert.That(r.Sent.OfType<ItemEnchantRequest>().Single(), Is.EqualTo(new ItemEnchantRequest(5, 0)));
        var p = new Rig(2);
        p.Add(5); p.Book.NeedsBindingBox = _ => true;
        p.Select(5, p.Info(5, exp: 2)); p.Sent.Clear();
        p.C.OnCallback("onEnchant", new object[0]);
        Assert.That(p.Boxes, Has.Count.EqualTo(1)); Assert.That(p.Sent, Is.Empty);
        p.Boxes[0].Callback(0, p.Boxes[0]);
        Assert.That(p.Sent[0], Is.EqualTo(new ItemEnchantExpRequest(5)));
    }

    [Test]
    public void AnEnchantedIngredientAsksBeforeMode2()
    {
        var r = new Rig();
        r.Add(5); r.Add(8, level: 2);
        r.Select(5, r.Info(5)); r.Sent.Clear();
        r.C.OnCallback("onSelectEquipIngredient", new object[] { "ItemEnchantDialog.equip.8", 1 });
        Assert.That(r.Boxes, Has.Count.EqualTo(1)); Assert.That(r.Sent, Is.Empty);
        r.Boxes[0].Callback(0, r.Boxes[0]);
        Assert.That(r.Sent.Single(), Is.EqualTo(new ItemEnchantIngredientRequest(8, 1)));
    }

    [Test]
    public void FailStackButtonsRetryAndCompleteSendTheirPackets()
    {
        var r = new Rig();
        r.Add(5); r.Select(5, r.Info(5)); r.Sent.Clear();
        r.Book.Apply(new ItemEnchantMessage.FailStackInfo(4, 0, new long[0], default)); r.Sent.Clear();
        r.C.OnCallback("onClickFailStackUp", new object[0]);
        r.C.OnCallback("onClickFailStackDown", new object[0]);
        Assert.That(r.Sent, Is.EqualTo(new IOutPacket[] { new ItemEnchantFailStackRequest(5), new ItemEnchantFailStackRequest(3) }));
        r.Sent.Clear(); r.C.OnCallback("onRetry", new object[0]);
        Assert.That(r.Sent.Single(), Is.EqualTo(new ItemEnchantStepRequest(false, 0, 0, 4)));
        r.Sent.Clear(); r.C.OnCallback("onComplete", new object[0]);
        Assert.That(r.Sent.Single(), Is.EqualTo(new ItemEnchantStepRequest(false, 0, 0, 3)));
        Assert.That(r.C.OnCallback("onUnknown", new object[0]), Is.False);
    }

    [Test]
    public void ASuccessFillsTheResultAndClosingRestoresAndSendsNothingElse()
    {
        var r = new Rig();
        r.Add(5); r.Select(5, r.Info(5));
        var body = new EquipItem { Id = r.Inv[5].ItemId, Uid = 5 }; body.Enchant.Level = 1;
        r.Book.Apply(new ItemEnchantMessage.Success(5, body, new Dictionary<int, int> { [1] = 100 }));
        Assert.That(r.Book.State, Is.EqualTo(1));
        Assert.That(r.Model.ItemSlots.ContainsKey(ItemEnchantDialogController.ResultSlot), Is.True);
        r.C.Close();
        Assert.That(r.Book.IsOpen, Is.False);
        Assert.That(r.Closed, Is.EqualTo(1));
        Assert.That(r.Model.ItemSlots.Keys.Any(k => k.StartsWith("ItemEnchantDialog")), Is.False, "all slots released");
        r.C.Close();
        Assert.That(r.Closed, Is.EqualTo(1), "closing twice is one close");
    }

    [Test]
    public void AnInfoPacketForAnotherModeClosesTheDialog()
    {
        var r = new Rig();
        r.Add(5);
        r.Book.Apply(r.Info(5, exp: 2));
        Assert.That(r.Closed, Is.EqualTo(1));
    }

    [Test]
    public void SlotNamesParseTheUidAfterTheLastDot()
    {
        var r = new Rig();
        r.Add(5); r.Add(6);
        r.C.BindEnchantable();
        r.Sent.Clear();
        r.C.OnCallback("onSelectItem", new object[] { "5" });
        Assert.That(r.Sent.OfType<ItemEnchantSelectRequest>().Single().Uid, Is.EqualTo(5));
        Assert.That(r.C.GetBagSlotBindings(ItemEnchantDialogController.Bag).Count(), Is.EqualTo(2));
    }
}
