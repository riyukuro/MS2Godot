using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;
using Maple2.PacketLib.Tools;

namespace Ms2.Ui.Tests;

/// <summary>The Change Attributes scroll window (uiitemremakescrolldialog as UIItemRemakeScrollDialog, UI 0x4f09; needs MS2_DATA):
/// the load, the slot drop and its checks, the lock and high-roll asks, the two sends, the answers, the error notices and the
/// close. Every refusal sends nothing and leaves the held item as it was.</summary>
public class ItemRemakeScrollDialogTests
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

    /// <summary>A level 50 armour item (equip subtype 0xd) of the KMS data whose random option holds the plain stats 4 and 16 (and 0, 1, 3 of the exclusive group).</summary>
    const int ArmourId = 11300073;
    const long ScrollUid = 900, LockUid = 950;

    sealed class Rig
    {
        public ItemRemakeScrollDialogController C = new();
        public List<IOutPacket> Sent = new();
        public List<string> Notices = new();
        public List<MessageBoxData> Boxes = new();
        public List<Item> Stored = new();
        public List<(int Tag, int Rarity)> TagAsks = new();
        public Dictionary<long, Item> Inv = new();
        public Dictionary<int, int> ByTag = new();
        public int ClosedCount;
        public UiModel Model;
        public UiHost Host;
        public Player Player;
        public int RowId;
        public ItemRemakeScrollData Row;

        /// <summary>The window opened with a scroll of a row for rank 4 and 5 level 50 armour (chest, subtype 0xd), non-pet.</summary>
        public Rig(bool holdScroll = true, Func<ItemRemakeScrollData, bool> pick = null)
        {
            Packs();
            Model = new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true };
            if (Model.Tables.Items.Get(ArmourId) == null) Assert.Ignore("the data has no item " + ArmourId);
            (RowId, Row) = FindRow(Model.Tables, pick ?? (r => r.PetKind == 0 && r.Items.Length == 0 && r.Slots.Contains(0xdu)
                && r.Ranks.Contains(4u) && r.Ranks.Contains(5u) && r.LevelMin <= 50 && (r.LevelMax == 0 || r.LevelMax >= 50)));
            if (holdScroll) AddScroll();
            C.ScrollUid = ScrollUid;
            C.Send = Sent.Add;
            C.FindItem = u => Inv.GetValueOrDefault(u);
            C.CountByTag = (t, r) => { TagAsks.Add((t, r)); return ByTag.GetValueOrDefault(t); };
            C.Store = Stored.Add;
            C.ShowData = Boxes.Add;
            C.NoticeText += Notices.Add;
            C.Closed += () => ClosedCount++;
            var (rt, p) = src.Open(C.Movie, r => r.Budget = 20_000_000);
            p.StageW = 1280; p.StageH = 720;
            p.Translate = k => k.TrimStart('$');
            Player = p;
            Host = new UiHost(p, C, Model);
            Assert.That(Host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
            Tick();
        }

        public void Tick() { for (int i = 0; i < 10; i++) Player.Tick(); }

        public Item AddScroll()
        {
            var d = new ItemData { Id = 20000001, Type = 3, FunctionName = "ItemRemakeScroll" };
            d.FunctionArgs.Add(RowId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var it = Item.Create(d, 4);
            it.Uid = ScrollUid;
            Inv[ScrollUid] = it;
            return it;
        }

        public string T(string key) => Model.Tables.Common(key);
        public string F(string key, params object[] a) => ClientFormat.Format(T(key), a);

        /// <summary>A held item of the armour id with the given group 2 stats.</summary>
        public Item Add(long uid, int rarity = 4, params (int Stat, int Value)[] stats)
        {
            var it = Item.Create(Model.Tables.Items, ArmourId, rarity);
            it.Uid = uid; it.Rarity = rarity;
            var e = new EquipItem { Id = ArmourId, Uid = uid };
            foreach (var (s, v) in stats) e.Options.Stat[2][s] = v;
            it.Record = e;
            Inv[uid] = it;
            return it;
        }

        public Item AddGood(long uid, int rarity = 4) => Add(uid, rarity, (4, 10), (16, 12));

        public void Confirm(params (string, object)[] fields) => C.OnCallback("onClickConfirm", new object[] { Host.Obj(fields) });

        /// <summary>The movie's lock toggle of row <paramref name="i"/> (m_option.m_lockN, read by getLockOptionIndex).</summary>
        public void Lock(int i)
        {
            Tick();
            var option = Host.Child("m_option");
            Assert.That(option, Is.Not.Null, "m_option");
            var box = Host.Invoke(option, "getChildByName", "m_lock" + i);
            Assert.That(box, Is.Not.Null, "m_lock" + i);
            Host.Set(box, "selected", true);
        }

        /// <summary>A body reader that gives a copy with the stats.</summary>
        public static Func<EquipItem, EquipItem> Body(params (int Stat, int Value)[] stats) => held =>
        {
            var e = new EquipItem { Id = held.Id, Uid = held.Uid };
            foreach (var (s, v) in stats) e.Options.Stat[2][s] = v;
            return e;
        };

        public void Roll(long uid, params (int, int)[] stats) => C.OnPacket(new ChangeAttributesScrollPacket(2) { Uid = uid, HasBody = true }, Body(stats));
        public void Applied(long uid, params (int, int)[] stats) => C.OnPacket(new ChangeAttributesScrollPacket(3) { Uid = uid, HasBody = true }, Body(stats));
    }

    static (int, ItemRemakeScrollData) FindRow(GameTables t, Func<ItemRemakeScrollData, bool> ok)
    {
        var path = TablePath.Resolve(packs, "itemremakescroll.xml");
        Assert.That(path, Is.Not.Null, "itemremakescroll.xml");
        var root = GameStrings.Parse(packs.Read("Xml", path)).Root!;
        foreach (var e in root.Elements("remakeScroll"))
        {
            int id = ItemData.Atoi((string)e.Attribute("id") ?? "");
            if (t.RemakeScroll(id) is { } r && ok(r)) return (id, r);
        }
        Assert.Fail("no remake scroll row of the kind in the data");
        return default;
    }

    static string Stats(Item i) => string.Join(",", i.Record.Options.Stat[2].OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"));

    [Test]
    public void LoadReadsTheScrollRow()
    {
        // OnLoadComplete (:250): state 1, the row's pet kind and lock flag
        var r = new Rig();
        Assert.That((r.C.State, r.C.Pet, r.C.LockOption, r.C.IsClosed), Is.EqualTo((1, r.Row.PetKind, r.Row.LockOption != 0, false)));
        Assert.That(r.C.IsDragSlot(ItemRemakeScrollDialogController.Slot) && r.C.IsDragSlot(ItemRemakeScrollDialogController.IngredientSlot), Is.True);
        Assert.That(r.Sent, Is.Empty, "the load sends nothing");
        Assert.That(r.C.ScrollLimitNotice(r.Row), Does.Contain(r.T("s_word_rank_04").Length > 0 ? r.T("s_word_rank_04") : ""));
        var none = new Rig(holdScroll: false);
        Assert.That((none.C.State, none.C.Pet, none.C.LockOption), Is.EqualTo((1, 0, false)), "no scroll: no row read");
    }

    [Test]
    public void ConfirmWithoutAnItemAsksForOne()
    {
        // FUN_140f5adb0 (:607)
        var r = new Rig();
        r.Confirm();
        Assert.That((r.Notices.Single(), r.Sent.Count, r.C.State), Is.EqualTo((r.T("s_itemremake_scroll_notice_puton_item"), 0, 1)));
    }

    [Test]
    public void EveryRefusedDropSendsNothingAndKeepsTheSlotEmpty()
    {
        // OnTargetSlotDrop (:1125): no scroll uid, the item not held, the scroll gone, and each code of the row's check
        var r = new Rig();
        var bad = r.Add(42, 3, (1, 10), (3, 12));                           // rarity 3: the rank refuses it when the row has no rank 3
        var bare = r.Add(43, 4);                                           // no group 2 option
        r.AddGood(41);

        void Refused(string why)
        {
            Assert.That((r.Sent.Count, r.Stored.Count, r.C.Target, r.C.TargetSlotItem, r.C.State), Is.EqualTo((0, 0, (Item)null, (Item)null, 1)), why);
        }
        r.C.ScrollUid = 0;
        r.C.DropTarget(41);
        Assert.That(r.Boxes.Last().Msg, Is.EqualTo(r.T("s_itemremake_scroll_error_have_not_scroll")));
        Refused("no scroll uid");
        r.C.ScrollUid = ScrollUid;
        r.C.DropTarget(99);
        Assert.That(r.Boxes.Last().Msg, Is.EqualTo(r.T("s_itemremake_scroll_error_have_not_item")));
        Refused("not held");
        r.Inv.Remove(ScrollUid);
        r.C.DropTarget(41);
        Assert.That(r.Boxes.Last().Msg, Is.EqualTo(r.T("s_itemremake_scroll_error_have_not_scroll")));
        Refused("the scroll gone");
        r.AddScroll();
        int boxes = r.Boxes.Count;
        if (!r.Row.Ranks.Contains(3u))
        {
            r.C.DropTarget(42);
            Assert.That(r.Notices.Last(), Is.EqualTo(r.T("s_itemremake_scroll_error_impossible_rank")));
            Refused("rank");
        }
        r.C.DropTarget(43);
        Assert.That(r.Notices.Last(), Is.EqualTo(r.T("s_itemremake_scroll_error_invalid_target_stat")));
        Refused("no option");
        Assert.That(r.Boxes.Count, Is.EqualTo(boxes), "a check code is a notice, not a box");
        Assert.That(Stats(bad) + "|" + Stats(bare), Is.EqualTo("1=10,3=12|"));
    }

    [Test]
    public void ADroppedItemShowsItsOptions()
    {
        var r = new Rig();
        var it = r.AddGood(41);
        r.C.DropTarget(41);
        Assert.That((r.C.State, r.C.Target, r.C.TargetSlotItem, r.C.OptionCount, r.Sent.Count), Is.EqualTo((2, it, it, 2, 0)));
        Assert.That(r.C.GetBinding(ItemRemakeScrollDialogController.Slot), Is.Not.Null);
        r.C.RemoveFromSlot(ItemRemakeScrollDialogController.Slot);
        Assert.That((r.C.State, r.C.Target, r.C.TargetSlotItem, r.C.OptionCount), Is.EqualTo((1, (Item)null, (Item)null, 0)), "taken off: state 1");
    }

    [Test]
    public void RemakeSendsKindOneWithTheScrollAndTheItem()
    {
        // FUN_140f5ae60 (:621) with no row locked -> FUN_140f59420 -> SendRemakeRequest (:433)
        var r = new Rig();
        var it = r.AddGood(41);
        r.C.DropTarget(41);
        r.Confirm(("lockOptionIndex", -1));
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo("01" + "8403000000000000" + "2900000000000000" + "0000000000000000" + "00" + "00"));
        Assert.That(r.Sent.Single().Op, Is.EqualTo(Outbound.ChangeAttributesScroll));
        Assert.That((r.C.InputEnabled, r.C.State, r.Stored.Count, Stats(it)), Is.EqualTo((false, 2, 0, "4=10,16=12")), "nothing changes before the answer");
    }

    [Test]
    public void RemakeRefusalsSendNothing()
    {
        var r = new Rig();
        var it = r.AddGood(41);
        r.C.DropTarget(41);
        r.Inv.Remove(ScrollUid);
        r.Confirm();
        Assert.That((r.Boxes.Last().Msg, r.Sent.Count), Is.EqualTo((r.T("s_itemremake_scroll_error_have_not_scroll"), 0)), "the scroll gone");
        r.AddScroll();
        r.Inv.Remove(41);
        r.Confirm();
        Assert.That((r.Notices.Last(), r.Sent.Count), Is.EqualTo((r.T("s_itemremake_scroll_error_invalid_target"), 0)), "the item gone");
        r.C.StartRemake(41, 0);
        r.C.SendRemakeRequest(41, 0);
        Assert.That((r.Sent.Count, r.C.InputEnabled, Stats(it)), Is.EqualTo((0, true, "4=10,16=12")));
    }

    [Test]
    public void HighRollAsksBeforeTheRemake()
    {
        // FUN_140f59420 (:386): rarity 5 or more with a high roll asks in box 0x11; its yes reads argument 2 (never set): 0
        var r = new Rig();
        var it = r.Add(41, 5, (0, 1_000_000), (4, 12));
        if (!r.Model.Tables.ItemOptions.IsHighRoll(it.Data, 0, 0, 1_000_000))
        {
            Assert.That(UiDataRows.IsKr(packs), Is.False, "the KMS data has the high stat");
            Assert.Ignore("no highStat in this data");
        }
        r.C.DropTarget(41);
        Assume.That(r.C.State, Is.EqualTo(2), "stat 0 is in the item's random option");
        r.Confirm();
        var box = r.Boxes.Single();
        Assert.That((box.Type, box.ItemUid, r.Sent.Count), Is.EqualTo((0x11, 41L, 0)));
        Assert.That(box.Msg, Does.EndWith("\n" + r.T("s_itemremake_scroll_ask_high_sa_item_option")));
        box.Callback(1, box);
        Assert.That(r.Sent, Is.Empty, "no: nothing");
        box.Callback(0, box);
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ChangeAttributesScrollRequest.Remake(ScrollUid, 41, 0, false))));
        Assert.That(Stats(it), Is.EqualTo("0=1000000,4=12"));
    }

    [Test]
    public void LockAsksThenNeedsTwoRowsAndALockItem()
    {
        // FUN_140f5ae60 (:621) asks; the yes runs SendRemakeRequest (:433): more than one row, a lock kind, a lock item held
        // the drop sets maxOptionLockCount from the lock item held then (FUN_140f5e7d0 :1160): none held, a lock tick is
        // undone by the movie and GetEnableLockOption (:1098) names the lock item
        var none = new Rig();
        none.Model.Features.Add(0x86);
        none.Model.Features.Add(0x8a);
        none.AddGood(41);
        none.C.DropTarget(41);
        none.Lock(1);
        Assert.That((none.Sent.Count, none.Boxes.Single().Msg), Is.EqualTo((0, none.F("s_itemremake_notice_disable_lock_item_option", LockText(none, 0x66, 0)))));
        Assert.That(none.Host.Invoke("getLockOptionIndex") is AsArray { Items.Count: 0 }, Is.True, "the tick is undone");

        var r = new Rig();
        r.Model.Features.Add(0x86);
        r.Model.Features.Add(0x8a);
        var it = r.AddGood(41);
        r.ByTag[0x66] = 1;
        r.C.DropTarget(41);
        r.Lock(1);
        r.Confirm(("lockOptionIndex", 1));
        var ask = r.Boxes.Single();
        Assert.That((ask.Type, r.Sent.Count), Is.EqualTo((1, 0)), "the ask sends nothing");
        Assert.That(ask.Msg, Is.EqualTo(r.F("s_itemremake_ask_lock_item_option", LockText(r, 0x66, 0))));
        ask.Callback(1, ask);
        Assert.That((r.Sent.Count, r.Boxes.Count), Is.EqualTo((0, 1)), "no: nothing");
        r.ByTag[0x66] = 0;
        ask.Callback(0, ask);
        Assert.That(r.Sent, Is.Empty, "no lock item held at the send");
        Assert.That(r.Boxes.Last().Msg, Is.EqualTo(r.F("s_itemremake_notice_disable_lock_item_option_when_click_remake", LockText(r, 0x66, 0))));
        Assert.That((r.Stored.Count, Stats(it)), Is.EqualTo((0, "4=10,16=12")));
        r.ByTag[0x66] = 1;
        r.Boxes.Clear();
        r.Confirm(("lockOptionIndex", 1));
        r.Boxes.Single().Callback(0, r.Boxes[0]);
        // the second row (by the stat order) is stat 16: index 0 (a stat) and its id
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ChangeAttributesScrollRequest.Remake(ScrollUid, 41, 0, false, new RemakeLock(0, 16)))));
        Assert.That((r.Stored.Count, Stats(it)), Is.EqualTo((0, "4=10,16=12")));

        var one = new Rig();
        one.Model.Features.Add(0x86);
        one.Model.Features.Add(0x8a);
        one.Add(42, 4, (4, 10));
        one.ByTag[0x66] = 1;
        one.C.DropTarget(42);
        one.Lock(0);
        one.Confirm(("lockOptionIndex", 0));
        one.Boxes.Single().Callback(0, one.Boxes[0]);
        Assert.That((one.Sent.Count, one.Boxes.Last().Msg), Is.EqualTo((0, one.T("s_itemremake_notice_disable_lock_item_option_error_count"))), "one row: no lock");
    }

    static string LockText(Rig r, int lockId, int level) => r.C.LockItemText(lockId, level);

    [Test]
    public void ANewRollIsShownAndChangesNothingHeld()
    {
        // FUN_140f5a7e0 (:524)
        var r = new Rig();
        var it = r.AddGood(41);
        r.C.DropTarget(41);
        r.Confirm();
        r.Inv.Remove(41);
        r.Roll(41, (1, 20));
        Assert.That((r.Boxes.Last().Msg, r.C.State, r.C.InputEnabled), Is.EqualTo((r.T("s_itemremake_scroll_error_have_not_item"), 2, true)), "the item gone");
        r.Inv[41] = it;
        r.Roll(41, (1, 20));
        Assert.That((r.C.State, Stats(r.C.Option), Stats(r.C.Target), r.Stored.Count, Stats(it)), Is.EqualTo((3, "1=20", "4=10,16=12", 0, "4=10,16=12")));
    }

    [Test]
    public void APickWithoutAChoiceOrWithoutTheItemSendsNothing()
    {
        // ApplySelectedOption (@0x140f5b670)
        var r = new Rig();
        var it = r.AddGood(41);
        r.C.DropTarget(41);
        r.Confirm();
        r.Roll(41, (1, 20));
        r.Sent.Clear();
        r.Confirm(("selectedOptionIndex", -1));
        Assert.That((r.Notices.Last(), r.Sent.Count, r.Stored.Count, r.C.State), Is.EqualTo((r.T("s_itemremake_scroll_error_invalid_selected_option"), 0, 0, 3)));
        r.Inv.Remove(41);
        r.Confirm(("selectedOptionIndex", 1));
        Assert.That((r.Boxes.Last().Msg, r.Sent.Count, r.Stored.Count, r.C.State), Is.EqualTo((r.T("s_itemremake_scroll_error_have_not_item"), 0, 0, 3)));
        Assert.That(Stats(it), Is.EqualTo("4=10,16=12"));
    }

    [Test]
    public void PickingTheNewRollSendsKindThreeThenTheAnswerEnds()
    {
        // ApplySelectedOption stores the pick before the send (as the client does); FUN_140f5a9c0 (:542) and ShowComplete (:846)
        var r = new Rig();
        r.AddGood(41);
        r.C.DropTarget(41);
        r.Confirm();
        r.Roll(41, (1, 20));
        r.Sent.Clear();
        r.Confirm(("selectedOptionIndex", 1));
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo("03" + "2900000000000000"));
        Assert.That((Stats(r.Stored.Single()), r.C.State, r.C.InputEnabled), Is.EqualTo(("1=20", 3, false)));
        r.Applied(42, (1, 30));
        Assert.That((r.C.State, r.Stored.Count), Is.EqualTo((3, 1)), "another uid: nothing");
        r.Applied(41, (1, 30));
        Assert.That((r.C.State, Stats(r.Stored.Last()), r.C.Final, Stats(r.C.Target), Stats(r.C.TargetSlotItem)), Is.EqualTo((4, "1=30", true, "1=30", "1=30")));
        r.Confirm();
        Assert.That((r.C.State, r.ClosedCount), Is.EqualTo((2, 0)), "the scroll still held: again");
    }

    [Test]
    public void PickingTheOldRollEndsWithoutASend()
    {
        var r = new Rig();
        var it = r.AddGood(41);
        r.C.DropTarget(41);
        r.Confirm();
        r.Roll(41, (1, 20));
        r.Sent.Clear();
        r.Inv.Remove(ScrollUid);
        r.Confirm(("selectedOptionIndex", 0));
        Assert.That((r.Sent.Count, r.Stored.Single(), r.C.State, r.C.Final, r.C.Target), Is.EqualTo((0, it, 4, false, (Item)null)));
        r.Confirm();
        Assert.That(r.ClosedCount, Is.EqualTo(1), "the scroll used up: the end closes");
    }

    [Test]
    public void EveryErrorCodeHasItsNotice()
    {
        // OnRemakeError (@0x140f5ab10) and FUN_140f5e2a0 (:1065)
        var r = new Rig();
        var t = r.Model.Tables;
        var keys = new Dictionary<int, string>
        {
            [1] = "s_itemremake_scroll_error_invalid_target", [10] = "s_itemremake_scroll_error_invalid_scroll",
            [2] = "s_itemremake_scroll_error_invalid_target_data", [3] = "s_itemremake_scroll_error_invalid_target_stat",
            [4] = "s_itemremake_scroll_error_impossible_slot", [8] = "s_itemremake_scroll_error_impossible_item",
            [5] = "s_itemremake_scroll_error_impossible_rank", [6] = "s_itemremake_scroll_error_impossible_level",
            [7] = "s_itemremake_scroll_error_impossible_property", [0xb] = "s_itemremake_scroll_error_invalid_scroll_data",
            [0x10] = "s_itemremake_error_server_fail_lack_lock_consume_item", [0x11] = "s_content_shutdown_notice",
        };
        foreach (var (code, key) in keys)
        {
            Assert.That(ItemRemakeScrollDialogController.ErrorText(t, 0, 0, code), Is.EqualTo(t.Common(key) ?? ""), key);
            Assert.That(ItemRemakeScrollDialogController.ErrorText(t, 1, 0, code), Is.EqualTo(t.Common(key + "_pet") ?? ""), key + "_pet");
        }
        Assert.That(ItemRemakeScrollDialogController.ErrorText(t, 0, 0, 0), Is.Null);
        Assert.That(ItemRemakeScrollDialogController.ErrorText(t, 1, 0, 0xc), Is.EqualTo(r.T("s_itemremake_scroll_error_server_fail_remake")));
        Assert.That(ItemRemakeScrollDialogController.ErrorText(t, 0, 0, 0xd), Is.EqualTo(r.T("s_itemremake_scroll_error_server_fail_remake")));
        Assert.That(ItemRemakeScrollDialogController.ErrorText(t, 0, 0, 0xe), Is.EqualTo(r.T("s_itemremake_scroll_error_server_fail_apply_before_option")));
        Assert.That(ItemRemakeScrollDialogController.ErrorText(t, 0, 0, 0xf), Is.EqualTo(r.T("s_itemremake_scroll_error_server_fail_consume_scroll")));
        Assert.That(ItemRemakeScrollDialogController.ErrorText(t, 0, 3, 0x20), Is.EqualTo(r.F("s_itemremake_scroll_error_server_default", 3, 0x20)));

        var it = r.AddGood(41);
        r.C.DropTarget(41);
        r.Confirm();
        r.C.OnPacket(new ChangeAttributesScrollPacket(4) { Flag = 0, Code = 0xc }, null);
        Assert.That((r.Notices.Last(), r.C.InputEnabled, r.C.State, r.Stored.Count, Stats(it)),
            Is.EqualTo((r.T("s_itemremake_scroll_error_server_fail_remake"), true, 2, 0, "4=10,16=12")), "an error answer: input on, nothing else");
    }

    [Test]
    public void CloseAsksOnlyWhileANewRollIsShown()
    {
        // vfunc_71 (@0x140f59070, :343) and OnAskCloseMsgBox (@0x140f5f250)
        var r = new Rig();
        r.AddGood(41);
        r.C.DropTarget(41);
        r.Confirm();
        r.Roll(41, (1, 20));
        Assert.That(r.C.OnCallbackBeforeDialog("onCloseDialog", Array.Empty<object>()), Is.True);
        var ask = r.Boxes.Single();
        Assert.That((ask.Type, ask.Msg, r.C.IsClosed), Is.EqualTo((1, r.T("s_itemremake_scroll_ask_close"), false)));
        ask.Callback(1, ask);
        Assert.That(r.C.IsClosed, Is.False);
        ask.Callback(0, ask);
        Assert.That((r.ClosedCount, r.C.ScrollUid, r.C.Target, r.C.Option, r.C.State), Is.EqualTo((1, 0L, (Item)null, (Item)null, 0)));
        Assert.That(r.Sent.Count, Is.EqualTo(1), "only the remake was sent");
        for (int i = 0; i < 120 && !r.C.IsClosed; i++) r.Player.Tick();     // the close tween ends with OnEndClose
        int boxes = r.Boxes.Count;
        r.Roll(41, (4, 30));
        r.Applied(41, (4, 30));
        Assert.That((r.C.State, r.C.Option, r.Stored.Count, r.Boxes.Count), Is.EqualTo((0, (Item)null, 0, boxes)), "a late roll or apply after the close: nothing");

        var two = new Rig();
        two.AddGood(41);
        two.C.DropTarget(41);
        two.C.RequestClose();
        Assert.That((two.Boxes.Count, two.ClosedCount), Is.EqualTo((0, 1)), "state 2 closes at once");

        // the key walk's Escape (vfunc_13) closes the same way: state 3 asks and takes the key, state 2 closes at once
        var esc = new Rig();
        esc.AddGood(41);
        esc.C.DropTarget(41);
        esc.Confirm();
        esc.Roll(41, (1, 20));
        Assert.That(esc.C.EscapeKey(), Is.True);
        Assert.That((esc.Boxes.Count, esc.Boxes.Last().Msg, esc.C.IsClosed), Is.EqualTo((1, esc.T("s_itemremake_scroll_ask_close"), false)));
        var esc2 = new Rig();
        esc2.AddGood(41);
        esc2.C.DropTarget(41);
        Assert.That(esc2.C.EscapeKey(), Is.True);
        Assert.That((esc2.Boxes.Count, esc2.ClosedCount), Is.EqualTo((0, 1)));
    }

    [Test]
    public void ThePetLockIngredientMustBeAPetLockItem()
    {
        // OnIngredientSlotDrop (@0x140f5ede0, :1191)
        var r = new Rig();
        r.AddGood(41);
        r.C.DropIngredient(41);
        Assert.That((r.Notices.Last(), r.C.Ingredient, r.C.IngredientSlotItem), Is.EqualTo((r.T("s_itemremake_scroll_error_invalid_target"), (Item)null, (Item)null)));
        var d = new ItemData { Id = 20000002, Type = 3, FunctionName = ItemRemakeScrollDialogController.LockPetFunction };
        d.FunctionArgs.Add("2");
        var ing = Item.Create(d, 1);
        ing.Uid = LockUid;
        r.Inv[LockUid] = ing;
        r.C.DropTarget(41);
        r.C.DropIngredient(LockUid);
        Assert.That((r.C.Ingredient, r.C.IngredientUid, r.C.IngredientSlotItem, r.C.State, r.Sent.Count), Is.EqualTo((ing, LockUid, ing, 2, 0)));
        r.C.RemoveFromSlot(ItemRemakeScrollDialogController.IngredientSlot);
        Assert.That((r.C.Ingredient, r.C.IngredientUid), Is.EqualTo(((Item)null, 0L)));
    }

    [Test]
    public void ThePetBindBoxYesSendsTheRemakeOnlyWithThePlayerTheItemAndTheWindow()
    {
        // HandleRemakeOptionScrollBindPetButton (@0x1419fda70, uimessagebox.cpp:1809) through the box layer's tag handler
        var r = new Rig();
        r.AddGood(41);
        r.C.DropTarget(41);
        var box = new MessageBoxData(0x11) { Extra = ItemRemakeScrollDialogController.BindPetTag, ItemUid = 41 };
        box.Append(41L);
        bool player = true;
        var boxes = new MessageBoxController();
        boxes.RemakeOptionScrollBindPet = (b, uid, arg) => ItemRemakeScrollDialogController.AnswerBindPet(r.C, player, r.C.FindItem, b, uid, arg);
        boxes.data = null;
        Assert.That(boxes.HandlePlainTag(0, ItemRemakeScrollDialogController.BindPetTag), Is.True, "no record: 1");
        boxes.data = box;
        Assert.That((boxes.HandlePlainTag(1, ItemRemakeScrollDialogController.BindPetTag), r.Sent.Count), Is.EqualTo((true, 0)), "no: nothing");
        player = false;
        Assert.That((boxes.HandlePlainTag(0, ItemRemakeScrollDialogController.BindPetTag), r.Sent.Count), Is.EqualTo((false, 0)), "no player");
        player = true;
        var held = r.Inv[41];
        r.Inv.Remove(41);
        Assert.That((boxes.HandlePlainTag(0, ItemRemakeScrollDialogController.BindPetTag), r.Sent.Count), Is.EqualTo((false, 0)), "the item gone");
        r.Inv[41] = held;
        Assert.That(ItemRemakeScrollDialogController.AnswerBindPet(null, true, r.C.FindItem, 0, 41, 41), Is.True, "no window: 1, nothing");
        Assert.That(boxes.HandlePlainTag(0, ItemRemakeScrollDialogController.BindPetTag), Is.True);
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ChangeAttributesScrollRequest.Remake(ScrollUid, 41, 0, false))), "the yes: the remake");
        Assert.That((r.Stored.Count, Stats(held)), Is.EqualTo((0, "4=10,16=12")));
        r.C.Close();
        for (int i = 0; i < 120 && !r.C.IsClosed; i++) r.Player.Tick();
        Assert.That((boxes.HandlePlainTag(0, ItemRemakeScrollDialogController.BindPetTag), r.Sent.Count), Is.EqualTo((false, 1)), "the window closed");
        Assert.That(new MessageBoxController { data = box }.HandlePlainTag(0, ItemRemakeScrollDialogController.BindPetTag), Is.False, "no HUD: 0");
    }

    [Test]
    public void RowTextsFollowTheClient()
    {
        // GetScrollLimitNotice (@0x140f5d460) and GetScrollDescription (@0x140f5dd50)
        var r = new Rig();
        var row = new ItemRemakeScrollData(10, 10, new uint[] { 3, 4 }, new uint[] { 0xd, 0xe }, 0, Array.Empty<uint>(), 1, 1, 2);
        string Rank(uint n) => $"<FONT color='#{r.Model.Tables.Items.TextColor((int)n):x}'>{r.T("s_word_rank_0" + n)}</FONT>";
        string sep = r.T("s_itemremake_scroll_limit_add");
        var slots = r.Model.Tables.SlotName(0xd) + ClientFormat.Format(sep, r.Model.Tables.SlotName(0xe));
        Assert.That(r.C.ScrollLimitNotice(row), Is.EqualTo(r.F("s_itemremake_scroll_limit", r.F("s_common_scroll_limit_level", (ushort)10),
            Rank(3) + ClientFormat.Format(sep, Rank(4)), r.F("s_itemremake_scroll_notice_slot_condition", slots))));
        Assert.That(r.C.ScrollLimitNotice(row with { LevelMax = 0, Items = new uint[] { 1 } }), Is.EqualTo(r.F("s_itemremake_scroll_limit",
            r.F("s_common_scroll_limit_level_upper", (ushort)10), Rank(3) + ClientFormat.Format(sep, Rank(4)), r.T("s_itemremake_scroll_notice_item_condition"))));
        Assert.That(r.C.ScrollLimitNotice(null), Is.EqualTo(""));
        var scroll = r.Inv[ScrollUid];
        string p = "s_itemremake_scroll_desc_add_option_";
        string desc = r.C.ScrollDescription(scroll, row);
        Assert.That(desc, Does.Contain(r.T(p + "kind_o") + "\n" + r.T(p + "value_high")));
        Assert.That(desc, Does.Not.Contain(r.T("s_itemremake_scroll_desc_represent_wap_value_o") is { Length: > 0 } w ? w : "\u0001"), "armour slots: no represent line");
        var weapon = row with { Slots = new uint[] { 0x1e, 0x33 } };
        Assert.That(r.C.ScrollDescription(scroll, weapon), Does.Contain(r.T(p + "kind_o") + "\n" + r.T(p + "value_high") + "\n" + r.T("s_itemremake_scroll_desc_represent_wap_value_o")));
        Assert.That(new uint[] { 0x1d, 0x1e, 0x22, 0x23, 0x28, 0x29, 0x2a, 0x31, 0x32, 0x38, 0x39 }.Select(ItemRemakeScrollDialogController.LockSlotKind),
            Is.EqualTo(new[] { false, true, true, false, true, true, false, false, true, true, false }));
    }
}
