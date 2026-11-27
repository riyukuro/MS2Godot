using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;
using Maple2.PacketLib.Tools;

namespace Ms2.Ui.Tests;

/// <summary>The Change Attributes window (uiitemenchantdialog as UIItemRemakeOptionDialog, UI 0x4eeb; needs MS2_DATA): the open
/// send, the pick, the price, the lock and high-roll boxes, the sends, the answers and the close. Every refusal sends nothing and
/// leaves the held item as it was.</summary>
public class ItemRemakeOptionDialogTests
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

    static string Hex(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return Convert.ToHexString(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    /// <summary>A level 50 armour item (equip subtype 0xd) whose random option has a high stat for stat 0 in the KMS data.</summary>
    const int ArmourId = 11300073;

    sealed class Rig
    {
        public ItemRemakeOptionDialogController C = new();
        public List<IOutPacket> Sent = new();
        public List<string> Notices = new(), Sounds = new();
        public List<(string, int)> Chat = new();
        public List<int> Talks = new();
        public List<MessageBoxData> Boxes = new();
        public List<Item> Stored = new();
        public List<(int Tag, int Rarity)> TagAsks = new();
        public Dictionary<long, Item> Inv = new();
        public List<Item> Bag = new(), Worn = new();
        public Dictionary<int, int> ByTag = new();
        public long Meso;
        public int Cleared, ClosedCount;
        public UiModel Model;
        public UiHost Host;

        public Rig()
        {
            Packs();
            Model = new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true };
            if (Model.Tables.Items.Get(ArmourId) == null) Assert.Ignore("the data has no item " + ArmourId);
            C.Send = Sent.Add;
            C.FindItem = u => Inv.GetValueOrDefault(u);
            C.BagItems = _ => Bag;
            C.WornItems = () => Worn;
            C.CountByTag = (t, r) => { TagAsks.Add((t, r)); return ByTag.GetValueOrDefault(t); };
            C.Meso = () => Meso;
            C.Store = Stored.Add;
            C.ShowData = Boxes.Add;
            C.ClearBoxes = () => Cleared++;
            C.NoticeText += Notices.Add;
            C.ChatLine += (t, k) => Chat.Add((t, k));
            C.PlaySound += Sounds.Add;
            C.TalkModify += Talks.Add;
            C.Closed += () => ClosedCount++;
            var (rt, p) = src.Open(C.Movie, r => r.Budget = 20_000_000);
            p.StageW = 1280; p.StageH = 720;
            p.Translate = k => k.TrimStart('$');
            Host = new UiHost(p, C, Model);
            Assert.That(Host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
            for (int i = 0; i < 10; i++) p.Tick();
        }

        public string T(string key) => Model.Tables.Common(key);
        public string F(string key, params object[] a) => ClientFormat.Format(T(key), a);

        /// <summary>A held item of the armour id with the given group 2 stats (none: an option that can not be remade).</summary>
        public Item Add(long uid, int rarity = 4, bool worn = false, params (int Stat, int Value)[] stats)
        {
            var it = Item.Create(Model.Tables.Items, ArmourId, rarity);
            it.Uid = uid; it.Rarity = rarity;
            var e = new EquipItem { Id = ArmourId, Uid = uid, RemakeCount = 3 };
            foreach (var (s, v) in stats) e.Options.Stat[2][s] = v;
            it.Record = e;
            Inv[uid] = it;
            (worn ? Worn : Bag).Add(it);
            return it;
        }

        public Item AddGood(long uid, int rarity = 4) => Add(uid, rarity, false, (1, 10), (3, 12));

        public void Open() { C.OnPacket(new ChangeAttributesPacket(6) { Result = 0 }, null); Sent.Clear(); }

        /// <summary>The server accepts the pick of <paramref name="uid"/> with the ingredients.</summary>
        public void Accept(long uid, params EnchantIngredient[] ing)
        {
            C.OnPacket(new ChangeAttributesPacket(4) { Uid = uid, Ingredients = ing.ToList() }, null);
            Sent.Clear();
        }

        /// <summary>A body reader that gives a copy with the stats and the remake count.</summary>
        public static Func<EquipItem, EquipItem> Body(int remakeCount, params (int Stat, int Value)[] stats) => held =>
        {
            var e = new EquipItem { Id = held.Id, Uid = held.Uid, RemakeCount = remakeCount };
            foreach (var (s, v) in stats) e.Options.Stat[2][s] = v;
            return e;
        };
    }

    static string Stats(Item i) => string.Join(",", i.Record.Options.Stat[2].OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"));

    [Test]
    public void LoadSendsOpenedAndLocksInput()
    {
        // OnLoadComplete (:238): state 0, input off, mode 6 [1]
        var r = new Rig();
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ChangeAttributesRequest.Opened())));
        Assert.That((r.C.State, r.C.InputLocked, r.C.IsClosed), Is.EqualTo((0, true, false)));
        Assert.That(r.Host.Child("m_enchantAni"), Is.Not.Null);
        r.Open();
        Assert.That((r.C.State, r.C.InputLocked), Is.EqualTo((1, false)), "kind 6 result 0: input on, state 1");
    }

    [Test]
    public void BagHoldsOnlyRemakeableItems()
    {
        // FUN_14129dd50: the category 0 items, then the worn ones, that CanRemakeOption(2) passes
        var r = new Rig();
        r.AddGood(1);
        r.Add(2, 3, false, (1, 10));          // rarity 3
        r.Add(3, 4, false);                   // no group 2 option
        r.Add(4, 4, true, (1, 10));           // worn
        r.C.FillBag();
        var bound = r.C.GetBagSlotBindings(ItemRemakeOptionDialogController.Bag).ToHashSet();
        Assert.That(bound, Is.EquivalentTo(new[] { 1L, 4L }.Select(ItemRemakeOptionDialogController.BagName)));
    }

    [Test]
    public void AnItemAddedWhileOpenJoinsTheBagAtTheNextFill()
    {
        // OnLoadComplete registers an item event listener (:243) whose vftable 0x142ed6b28 keeps every empty base slot:
        // only the load and the apply (:555) fill the bag
        var r = new Rig();
        r.AddGood(1);
        r.C.FillBag();
        r.AddGood(2);
        var name = ItemRemakeOptionDialogController.BagName;
        Assert.That(r.C.GetBagSlotBindings(ItemRemakeOptionDialogController.Bag), Is.EquivalentTo(new[] { name(1) }));
        r.C.FillBag();
        Assert.That(r.C.GetBagSlotBindings(ItemRemakeOptionDialogController.Bag), Is.EquivalentTo(new[] { name(1), name(2) }));
    }

    [Test]
    public void PickNeedsTheOpenAnswerAndARemakeableHeldItem()
    {
        // SendChangeAttributesMode4 (:602)
        var r = new Rig();
        r.AddGood(41);
        r.Add(42, 3, false, (1, 10));
        r.Sent.Clear();
        r.C.SendChangeAttributesMode4(41);
        Assert.That(r.Sent, Is.Empty, "state 0: nothing");
        r.Open();
        r.C.SendChangeAttributesMode4(99);
        Assert.That((r.Sent.Count, r.Notices.Single()), Is.EqualTo((0, r.T("s_itemremake_error_server_not_in_inven"))));
        r.C.SendChangeAttributesMode4(42);
        Assert.That((r.Sent.Count, r.Notices.Last()), Is.EqualTo((0, r.T("s_itemremake_error_server_impossible"))));
        Assert.That(r.C.OnCallback("onSelectItem", new object[] { "41" }), Is.True);
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ChangeAttributesRequest.Select(41))));
    }

    [Test]
    public void AcceptedPicksTheItemAndCountsItsOptions()
    {
        // FUN_14129f940 (:626)
        var r = new Rig();
        var it = r.AddGood(41);
        r.Open();
        r.Accept(99);
        Assert.That((r.C.State, r.C.Cur, r.Notices.Single()), Is.EqualTo((2, (Item)null, r.T("s_itemremake_error_server_not_in_inven"))));
        r.Accept(41);
        Assert.That((r.C.State, r.C.Cur, r.C.OptionCount), Is.EqualTo((2, it, 2)));
        Assert.That(r.Talks, Is.EqualTo(new[] { 100 }));
        Assert.That(r.Sounds, Is.EqualTo(new[] { "Enchant_ItemChoice" }));
        Assert.That(r.Model.ItemSlots.ContainsKey(ItemRemakeOptionDialogController.SelectedSlot), Is.True);
    }

    [Test]
    public void ShortIngredientRefusesTheRemakeAndSendsNothing()
    {
        // CanPayRemakePrice (:775): a short row that is not the meso tag fails with lack_price
        var r = new Rig();
        var it = r.AddGood(41);
        r.Open();
        int lockItem = r.Model.Tables.Items.TagItemId(0x66);
        Assert.That(lockItem, Is.Not.Zero);
        r.Accept(41, new EnchantIngredient(lockItem, 0, 2));
        r.ByTag[0x66] = 1;
        string before = Stats(it);
        r.C.OnClickRemake(41, -1);
        Assert.That((r.Sent.Count, r.Notices.Single(), r.C.State), Is.EqualTo((0, r.T("s_itemremake_error_server_lack_price"), 2)));
        Assert.That((Stats(it), it.Record.RemakeCount), Is.EqualTo((before, 3)), "nothing changes before the server answers");
        r.ByTag[0x66] = 2;
        r.C.OnClickRemake(41, -1);
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ChangeAttributesRequest.Remake(41))));
        Assert.That((r.C.State, r.C.InputLocked, r.C.SwallowsKeys), Is.EqualTo((3, true, true)));
    }

    [Test]
    public void ShortMesoTagRowIsPaidWithMeso()
    {
        // a short tag 0x60 row costs (need - have) * 1000 meso
        var r = new Rig();
        r.AddGood(41);
        r.Open();
        r.Accept(41, new EnchantIngredient(0, 0x60, 3));
        Assert.That(r.Model.ItemSlots.ContainsKey(ItemRemakeOptionDialogController.IngredientPrefix + "0"), Is.True);
        r.ByTag[0x60] = 1;
        r.Meso = 1999;
        r.C.OnClickRemake(41, -1);
        Assert.That((r.Sent.Count, r.Notices.Single()), Is.EqualTo((0, r.T("s_itemremake_error_server_lack_price"))));
        r.Meso = 2000;
        r.C.OnClickRemake(41, -1);
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ChangeAttributesRequest.Remake(41))));
    }

    [Test]
    public void LockAsksThenNeedsTwoRowsAndALockItem()
    {
        // OnClickRemake (:487) asks; the yes runs SendRemakeRequest (:988)
        var r = new Rig();
        r.AddGood(41);
        var one = r.Add(42, 4, false, (1, 10));
        r.Open();

        r.C.OnClickRemake(99, 0);
        Assert.That((r.Boxes.Count, r.Sent.Count, r.Notices.Single()), Is.EqualTo((0, 0, r.T("s_itemremake_error_server_not_in_inven"))));

        r.Accept(42);
        r.C.OnClickRemake(42, 0);
        var ask = r.Boxes.Single();
        Assert.That(ask.Type, Is.EqualTo(1));
        Assert.That(r.Sent, Is.Empty, "the ask sends nothing");
        ask.Callback(0, ask);
        Assert.That(r.Boxes.Last().Msg, Is.EqualTo(r.T("s_itemremake_notice_disable_lock_item_option_error_count")));
        Assert.That((r.Sent.Count, r.C.State), Is.EqualTo((0, 2)), "one row: no lock");

        r.Accept(41);
        r.Boxes.Clear();
        r.C.OnClickRemake(41, 0);
        r.Boxes.Single().Callback(1, r.Boxes[0]);
        Assert.That((r.Sent.Count, r.Boxes.Count), Is.EqualTo((0, 1)), "no: nothing");
        r.Boxes[0].Callback(0, r.Boxes[0]);
        Assert.That(r.Sent, Is.Empty, "no lock item held");
        Assert.That(r.Boxes.Last().Msg, Is.EqualTo(r.F("s_itemremake_notice_disable_lock_item_option_when_click_remake", r.C.LockItemText(0x66, 0))));
        Assert.That(r.TagAsks.Last(), Is.EqualTo((0x66, 0)), "without feature 0xa5 the lock level is 0");

        r.ByTag[0x66] = 1;
        r.Model.Features.Add(0xa5);
        r.Boxes.Clear();
        r.C.OnClickRemake(41, 1);
        r.Boxes.Single().Callback(0, r.Boxes[0]);
        Assert.That(r.TagAsks.Last(), Is.EqualTo((0x66, 4)), "feature 0xa5: the rarity");
        // the second row (by the stat order) is stat 3: index 0 (a stat) and its id
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ChangeAttributesRequest.Remake(41, new RemakeLock(0, 3)))));
        Assert.That(r.C.State, Is.EqualTo(3));
        Assert.That(one.Record.Options.Stat[2][1], Is.EqualTo(10));
    }

    [Test]
    public void LockButtonAsksTheSameTests()
    {
        // GetEnableLockOption (:566)
        var r = new Rig();
        r.AddGood(41);
        r.Add(42, 4, false, (1, 10));
        r.Open();
        r.Accept(42);
        Assert.That(r.C.LockEnabled(), Is.False);
        Assert.That(r.Boxes.Last().Msg, Is.EqualTo(r.T("s_itemremake_notice_disable_lock_item_option_error_count")));
        r.Accept(41);
        Assert.That(r.C.LockEnabled(), Is.False, "no lock item");
        r.ByTag[0x66] = 1;
        int boxes = r.Boxes.Count;
        Assert.That(r.C.LockEnabled(), Is.True);
        Assert.That(r.Boxes.Count, Is.EqualTo(boxes));
    }

    [Test]
    public void HighRollAsksBeforeTheRemake()
    {
        // StartRemake (:944): rarity 5 or more with a high roll (FUN_1409e4950) asks in box 0x11
        var r = new Rig();
        var it = r.Add(41, 5, false, (0, 1_000_000), (3, 12));
        if (!r.Model.Tables.ItemOptions.IsHighRoll(it.Data, 0, 0, 1_000_000))
        {
            Assert.That(UiDataRows.IsKr(packs), Is.False, "the KMS data has the high stat");
            Assert.Ignore("no highStat in this data");
        }
        var four = r.Add(42, 4, false, (0, 1_000_000), (3, 12));
        r.Open();
        r.Accept(42);
        r.C.StartRemake(42, -1, true);
        Assert.That((r.Boxes.Count, r.Sent.Select(Hex).Single()), Is.EqualTo((0, Hex(ChangeAttributesRequest.Remake(42)))), "rarity 4 sends at once");
        r.Sent.Clear();

        r.Accept(41);
        r.C.StartRemake(41, -1, true);
        var box = r.Boxes.Single();
        Assert.That((box.Type, box.ItemUid, r.Sent.Count), Is.EqualTo((0x11, 41L, 0)));
        Assert.That(box.Msg, Does.EndWith("\n" + r.T("s_itemremake_ask_high_sa_item_option")));
        box.Callback(1, box);
        Assert.That((r.Sent.Count, r.C.Cur, r.C.OptionCount), Is.EqualTo((0, (Item)null, 0)), "no: the pick is cleared");
        Assert.That(r.Model.ItemSlots.ContainsKey(ItemRemakeOptionDialogController.SelectedSlot), Is.False);

        r.Accept(41);
        r.C.StartRemake(41, -1, true);
        r.Boxes.Last().Callback(0, r.Boxes.Last());
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ChangeAttributesRequest.Remake(41))));
        Assert.That(Stats(four), Is.EqualTo("0=1000000,3=12"));
    }

    [Test]
    public void RemakeResultThenApplyNewSendsApplyAndStores()
    {
        // FUN_1412a30f0 (:1110), OnClickApply (:520), FUN_1412a3330 (:1131)
        var r = new Rig();
        var it = r.AddGood(41);
        r.Open();
        r.Accept(41);
        r.C.OnClickRemake(41, -1);
        r.Sent.Clear();
        Assert.That((Stats(it), it.Record.RemakeCount), Is.EqualTo(("1=10,3=12", 3)), "nothing changes before the result");

        r.C.OnPacket(new ChangeAttributesPacket(1) { ApplyUid = 41, HasBody = true }, Rig.Body(4, (5, 7), (6, 8)));
        Assert.That(r.C.New, Is.Not.Null.And.Not.SameAs(it));
        Assert.That((Stats(r.C.New), Stats(it), it.Record.RemakeCount), Is.EqualTo(("5=7,6=8", "1=10,3=12", 4)),
            "the result only moves the held item's remake count");
        Assert.That(r.Sounds.Last(), Is.EqualTo("Enchant_Progress"));
        Assert.That(r.Stored, Is.Empty);

        var next = r.C.New;
        r.C.OnClickApply(1);
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ChangeAttributesRequest.Apply(41))));
        Assert.That(r.Stored.Single(), Is.SameAs(next), "the client stores the pick before the server's answer");
        Assert.That((r.C.State, r.Talks.Last(), r.Sounds.Last()), Is.EqualTo((4, 0x67, "Enchant_SuccessEffect")));
        Assert.That(r.C.New, Is.SameAs(next), "after mode 2 the pick stays for the answer");

        r.C.OnPacket(new ChangeAttributesPacket(2) { ApplyUid = 41, HasBody = true }, Rig.Body(4, (5, 7), (6, 8)));
        Assert.That(r.Stored.Count, Is.EqualTo(2));
        Assert.That(Stats(r.Stored[1]), Is.EqualTo("5=7,6=8"));
        Assert.That((r.C.Cur, r.C.New), Is.EqualTo(((Item)null, (Item)null)));
    }

    [Test]
    public void ApplyKeepAndRefusal()
    {
        var r = new Rig();
        var it = r.AddGood(41);
        r.Open();
        r.Accept(41);
        r.C.OnClickRemake(41, -1);
        r.C.OnPacket(new ChangeAttributesPacket(1) { ApplyUid = 41, HasBody = true }, Rig.Body(4, (5, 7)));
        r.Sent.Clear();

        r.C.OnClickApply(-1);
        Assert.That((r.Sent.Count, r.Stored.Count, r.Notices.Single()), Is.EqualTo((0, 0, r.T("s_itemremake_error_server_fail_apply_option"))));
        Assert.That(Stats(it), Is.EqualTo("1=10,3=12"));

        r.C.OnClickApply(0);
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ChangeAttributesRequest.Keep())));
        Assert.That(r.Stored.Single(), Is.SameAs(it));
        Assert.That((r.C.Cur, r.C.New, Stats(it)), Is.EqualTo(((Item)null, (Item)null, "1=10,3=12")));
    }

    [Test]
    public void RemakeResultForAGoneItemShowsItsNotice()
    {
        var r = new Rig();
        r.AddGood(41);
        r.Open();
        r.C.OnPacket(new ChangeAttributesPacket(1) { ApplyUid = 41, HasBody = true }, Rig.Body(4));
        Assert.That(r.C.New, Is.Null, "no pick: nothing");
        r.Accept(41);
        r.C.OnPacket(new ChangeAttributesPacket(1) { ApplyUid = 77, HasBody = true }, Rig.Body(4, (5, 7)));
        Assert.That(r.Notices.Single(), Is.EqualTo(r.T("s_itemremake_error_server_not_in_inven")));
        Assert.That(r.Sounds, Does.Not.Contain("Enchant_Progress"));
    }

    [Test]
    public void LastResultKinds()
    {
        // FUN_1412a2c50 (:1069)
        var r = new Rig();
        var it = r.AddGood(41);
        r.Add(42, 3, false, (1, 10));
        r.C.OnPacket(new ChangeAttributesPacket(6) { Result = 2, ItemId = ArmourId }, null);
        string gone = r.F("s_itemremake_error_prev_option_item_not_exist", r.Model.Tables.ItemName(ArmourId));
        Assert.That((r.C.State, r.Boxes.Single().Msg, r.Boxes[0].Type), Is.EqualTo((1, gone, 0)));
        Assert.That(r.Chat.Single(), Is.EqualTo((gone, 0x14)));

        r.C.OnPacket(new ChangeAttributesPacket(6) { Result = 1, ItemId = ArmourId, Uid = 42, ApplyUid = 42, HasBody = true }, Rig.Body(4));
        Assert.That((r.C.State, r.C.Cur, r.Notices.Single()), Is.EqualTo((1, (Item)null, r.T("s_itemremake_error_server_impossible"))));

        r.C.OnPacket(new ChangeAttributesPacket(6) { Result = 1, ItemId = ArmourId, Uid = 41, ApplyUid = 41, HasBody = true }, Rig.Body(9, (5, 7)));
        Assert.That((r.C.State, r.C.Cur, Stats(r.C.New), it.Record.RemakeCount), Is.EqualTo((3, it, "5=7", 9)));
        Assert.That(Stats(it), Is.EqualTo("1=10,3=12"));
        Assert.That(r.Sent.Count(p => Hex(p) != Hex(ChangeAttributesRequest.Opened())), Is.Zero);
    }

    [Test]
    public void ErrorShowsTheCodesNoticeAndEndsTheRemake()
    {
        // OnRemakeError (:1152)
        var r = new Rig();
        r.AddGood(41);
        r.Open();
        r.Accept(41);
        r.C.OnClickRemake(41, -1);
        Assert.That(r.C.State, Is.EqualTo(3));
        r.C.OnPacket(new ChangeAttributesPacket(5) { Flag = 1, Code = 9 }, null);
        Assert.That((r.C.State, r.C.InputLocked), Is.EqualTo((3, false)), "flag 1 keeps the remake");
        r.C.OnPacket(new ChangeAttributesPacket(5) { Flag = 0, Code = 9 }, null);
        Assert.That(r.C.State, Is.EqualTo(2));
        var keys = new Dictionary<int, string>
        {
            [1] = "s_itemremake_error_server_not_in_inven", [2] = "s_itemremake_error_server_impossible",
            [3] = "s_itemremake_error_server_null_status", [4] = "s_itemremake_error_server_null_status",
            [5] = "s_itemremake_error_server_lack_price", [6] = "s_itemremake_error_server_fail_apply_option",
            [7] = "s_itemremake_error_server_fail_apply_option", [8] = "s_itemremake_error_server_default",
            [9] = "s_itemremake_error_server_fail_lack_lock_consume_item", [10] = "s_content_shutdown_notice",
            [0] = "s_itemremake_error_server_default",
        };
        r.Notices.Clear();
        foreach (var (code, key) in keys)
        {
            r.C.OnPacket(new ChangeAttributesPacket(5) { Flag = 0, Code = code }, null);
            Assert.That(r.Notices.Last(), Is.EqualTo(r.F(key, 0, code)), "code " + code);
        }
        Assert.That(r.Sent.Count, Is.EqualTo(1), "only the remake");
    }

    [Test]
    public void CloseClearsTheBoxesAndSlotsAndIgnoresLaterAnswers()
    {
        // vfunc_67 (:302)
        var r = new Rig();
        r.AddGood(41);
        r.Open();
        r.C.FillBag();
        r.Accept(41);
        r.C.OnClickRemake(41, 0);
        var ask = r.Boxes.Single();
        r.C.Close();
        Assert.That((r.Cleared, r.ClosedCount, r.C.IsClosed, r.C.State, r.C.Cur), Is.EqualTo((1, 1, true, 0, (Item)null)));
        Assert.That(r.Model.ItemSlots.Keys.Where(k => k.StartsWith(ItemRemakeOptionDialogController.Bag + ".", StringComparison.Ordinal)), Is.Empty);
        r.ByTag[0x66] = 1;
        int boxes = r.Boxes.Count;
        ask.Callback(0, ask);
        Assert.That(r.Boxes.Count, Is.EqualTo(boxes), "a yes after the close does nothing");
        r.C.OnPacket(new ChangeAttributesPacket(4) { Uid = 41, Ingredients = new() }, null);
        Assert.That((r.Sent.Count, r.C.Cur), Is.EqualTo((0, (Item)null)), "after the close nothing is sent or picked");
        r.C.Close();
        Assert.That(r.Cleared, Is.EqualTo(1));
    }

    [Test]
    public void Statics()
    {
        // itemremakescrolldata.cpp: FUN_140616b60, GetDiscountedIngredientCount, GetLockItemId, CanLockOptions
        Assert.That(ItemRemakeOptionDialogController.Sale(0, 0, out uint d0), Is.EqualTo(0));
        Assert.That((ItemRemakeOptionDialogController.Sale(500, 300, out uint d1), d1), Is.EqualTo((1, 500u)));
        Assert.That((ItemRemakeOptionDialogController.Sale(300, 500, out uint d2), d2), Is.EqualTo((2, 500u)));
        Assert.That((ItemRemakeOptionDialogController.Sale(500, 500, out uint d3), d3), Is.EqualTo((1, 500u)));
        Assert.That(d0, Is.Zero);
        Assert.That(ItemRemakeOptionDialogController.DiscountedIngredientCount(10, 2000, 0.5f), Is.EqualTo(8));
        Assert.That(ItemRemakeOptionDialogController.DiscountedIngredientCount(10, 9000, 0.5f), Is.EqualTo(5), "capped at the limit");
        Assert.That(ItemRemakeOptionDialogController.DiscountedIngredientCount(1, 5000, 0.5f), Is.EqualTo(1), "at least 1");

        Packs();
        var t = new GameTables(packs, "en");
        var d = t.Items.Get(ArmourId);
        if (d == null) Assert.Ignore("the data has no item " + ArmourId);
        var it = Item.Create(d, 4);
        var on = new HashSet<int> { 0x8a, 0x8b, 0x9b };
        Assert.That(ItemRemakeOptionDialogController.LockItemId(it, t.ItemModels), Is.EqualTo(0x66));
        Assert.That(ItemRemakeOptionDialogController.CanLockOptions(it, t.ItemModels, on.Contains), Is.True);
        Assert.That(ItemRemakeOptionDialogController.CanLockOptions(it, t.ItemModels, _ => false), Is.False);
        Assert.That(ItemRemakeOptionDialogController.LockItemLevel(it, _ => true), Is.EqualTo(4));
        Assert.That(ItemRemakeOptionDialogController.LockItemLevel(it, _ => false), Is.Zero);
        d.Subtype = 0x12;
        Assert.That(ItemRemakeOptionDialogController.LockItemId(it, t.ItemModels), Is.EqualTo(0x67));
        d.Subtype = 0; d.Type = 0xb;
        Assert.That(ItemRemakeOptionDialogController.LockItemId(it, t.ItemModels), Is.EqualTo(0x68));
        Assert.That(ItemRemakeOptionDialogController.LockItemLevel(it, _ => true), Is.Zero, "a pet's level is 0");
        Assert.That(ItemRemakeOptionDialogController.CanLockOptions(it, t.ItemModels, on.Contains), Is.True);
    }
}
