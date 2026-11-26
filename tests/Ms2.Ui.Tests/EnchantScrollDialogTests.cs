using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;
using Maple2.PacketLib.Tools;

namespace Ms2.Ui.Tests;

/// <summary>
/// The enchant scroll window (uienchantscrolldialog, UIEnchantScrollDialog 0x4ee0) over its book (needs MS2_DATA): the
/// server's open / preview / result in the client's order, the slot drop and onEnchant sends with the client's bytes, every
/// refused case sending nothing, nothing changing before the server answers, and the option rows' order and values. Texts
/// are not compared, so no language's strings.
/// </summary>
public class EnchantScrollDialogTests
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

    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
    static byte[] Of(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
    static byte[] Req(byte mode, long a, long b) => new[] { mode }.Concat(BitConverter.GetBytes(a)).Concat(BitConverter.GetBytes(b)).ToArray();

    const long Scroll = 0x7700000001, Gear = 0x5500000002, Other = 0x5500000003;

    sealed class Rig
    {
        public EnchantScrollDialogController C;
        public EnchantScrollBook Book = new();
        public List<IOutPacket> Sent = new();
        public List<MessageBoxData> Boxes = new();
        public List<string> Notices = new();
        public List<int> NoticeIds = new();
        public List<EquipItem> Stored = new();
        public int Opens, Closes, Clears;
        public Dictionary<long, EquipItem> Inv = new();
        public UiModel Model;
        public UiHost Host;
        public int ItemId;

        public Rig(bool load = true)
        {
            Packs();
            Model = new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true };
            ItemId = new[] { 11300001, 12000001, 20000001, 30000001 }.FirstOrDefault(i => !string.IsNullOrEmpty(Model.Tables.ItemName(i)));
            if (ItemId == 0) Assert.Ignore("no known item");
            Book.Send = Sent.Add;
            Book.Find = u => Inv.TryGetValue(u, out var i) ? i : null;
            Book.StoreItem = Stored.Add;
            Book.OpenRequested += () => Opens++;
            Book.CloseRequested += () => Closes++;
            Book.Cleared += () => Clears++;
            Book.Notice += NoticeIds.Add;
            C = new EnchantScrollDialogController { Book = Book, ShowData = Boxes.Add };
            C.FindItem = uid => Inv.TryGetValue(uid, out var e) ? Wrap(e) : null;
            C.NoticeText += Notices.Add;
            Add(Scroll, ItemId); Add(Gear, ItemId, 3); Add(Other, ItemId, 1);
            if (!load) return;
            var (rt, p) = src.Open(C.Movie, r => r.Budget = 20_000_000);
            p.StageW = 1280; p.StageH = 720;
            p.Translate = k => k.TrimStart('$');
            Host = new UiHost(p, C, Model);
            Assert.That(Host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
            for (int i = 0; i < 10; i++) p.Tick();
        }

        public EquipItem Add(long uid, int id, int level = 0)
        {
            var e = new EquipItem { Id = id, Uid = uid, Rarity = 3 };
            e.Enchant.Level = level;
            Inv[uid] = e;
            return e;
        }

        public Item Wrap(EquipItem e)
        {
            var item = Item.Create(Model.Tables.Items, e.Id, 1);
            if (item == null) Assert.Ignore("no item data");
            item.Uid = e.Uid; item.Record = e; item.Rarity = e.Rarity;
            return item;
        }

        public void Recv(byte[] b) => Book.Apply(EnchantScrollMessage.Read(new ByteReader(b), Book.Context));

        public void Open(int type, int target = 5, bool disableBreak = false) => Recv(Of(w =>
        {
            w.WriteByte(0); w.WriteLong(Scroll); w.WriteShort((short)type); w.WriteBool(disableBreak); w.WriteInt(target); w.WriteInt(3000);
            w.WriteShort(1); w.WriteShort(10); w.WriteInt(2); w.WriteInt(1); w.WriteInt(8); w.WriteInt(2); w.WriteInt(3); w.WriteInt(4);
            if (type == 3) { w.WriteInt(2); w.WriteInt(6); }
        }));

        public void Preview(long uid, int type, params (int Key, int Value)[][] lists) => Recv(Of(w =>
        {
            w.WriteByte(1); w.WriteLong(uid); w.WriteShort((short)type);
            foreach (var l in lists) { w.WriteInt(l.Length); foreach (var (k, v) in l) { w.WriteShort((short)k); w.WriteInt(v); } }
        }));

        public object Field(string child, string name) => Host.Get(Host.Child(child), name);
    }

    sealed class InvOwner : IInvenSlotOwner
    {
        public long? Uid; public Item Found; public int Refreshed;
        public long? InvenSlotUid(string name) => Uid;
        public Item FindItem(long uid) => Found;
        public bool CanEquipItem(Item item, int slot, int layer) => false;
        public void RequestEquipItem(Item item, int slot, int layer) { }
        public bool IsDragSlot(string name) => true;
        public void RefreshSlot(string name) { Refreshed++; }
        public void SlotDropUnresolved(string name) { }
    }

    /// <summary>FUN_140f3b920: mode 0 with the window closed asks OpenDialog and registers nothing; the load's
    /// UpdateScrollInfo registers the slot with the scroll and no item and sends nothing; a second mode 0 while open runs
    /// UpdateScrollInfo in place.</summary>
    [Test]
    public void ModeZeroOpensTheWindowAndTheLoadRegistersTheEmptySlot()
    {
        var r = new Rig(load: false);
        r.Open(1);
        Assert.That((r.Opens, r.Book.SlotRegistered, r.Book.ScrollUid, r.Book.Type, r.Book.FailRate, r.Book.TargetLevel), Is.EqualTo((1, false, Scroll, 1, 30u, 5)));
        Assert.That(r.Sent, Is.Empty);
        var l = new Rig();
        l.Open(1);    // OnLoadComplete already ran: the window is open now
        Assert.That(l.Opens, Is.EqualTo(0));
        Assert.That((l.Book.SlotRegistered, l.Book.SlotOwner, l.Book.SlotItemUid), Is.EqualTo((true, Scroll, 0L)));
        Assert.That(l.Host.Child("m_slot"), Is.Not.Null);
        Assert.That(l.Host.Child("m_enchant"), Is.Not.Null);
        var info = l.C.ScrollInfoData().ToDictionary(x => x.Item1, x => x.Item2);
        Assert.That(info.Keys, Is.EquivalentTo(new[] { "itemGUID", "scrollType", "bindID", "ready", "desc", "limit", "failDesc" }));
        Assert.That((info["itemGUID"], info["scrollType"], info["bindID"], info["ready"]), Is.EqualTo(("0", 1, EnchantScrollBook.TypeName, 0)));
        Assert.That(Rt.ToBoolean(l.Field("m_limit", "visible")), Is.True, "ready 0 shows the limits");
        Assert.That(Rt.ToBoolean(l.Field("m_prop", "visible")), Is.False);
        Assert.That(l.C.GetBinding(EnchantScrollDialogController.Slot), Is.Null, "no item on the slot yet");
        Assert.That(l.Sent, Is.Empty);
    }

    /// <summary>EnchantScrollSlot::vfunc_18 with every refused case: no registered slot, an uid not in the inventory, a slot
    /// name that is not this one, a source that is not an inventory slot; the old-version charge asks the refund box first and
    /// sends nothing (FUN_141902660). The source slot is pushed each time.</summary>
    [Test]
    public void ADropSendsThePutOnlyForAnInventoryItemOnTheRegisteredSlot()
    {
        var r = new Rig(load: false);
        r.Open(1);
        Assert.That(r.Book.Put(Gear), Is.EqualTo(EnchantScrollPut.None), "the slot is not registered before the load");
        Assert.That(r.Sent, Is.Empty);

        var l = new Rig();
        l.Open(1);
        var inv = new InvOwner { Uid = 0x999 };
        l.C.SlotDroppedOn(EnchantScrollDialogController.Slot, "InventoryDialog.0", inv);
        Assert.That(l.Sent, Is.Empty, "not in the inventory");
        Assert.That(inv.Refreshed, Is.EqualTo(1));
        inv.Uid = Gear;
        l.C.SlotDroppedOn("Other", "InventoryDialog.0", inv);
        Assert.That(l.Sent, Is.Empty, "another slot");
        Assert.That(inv.Refreshed, Is.EqualTo(2));
        l.C.SlotDroppedOn(EnchantScrollDialogController.Slot, "Other.0", new NotInven());
        Assert.That(l.Sent, Is.Empty, "not an inventory slot");

        var gear = l.Inv[Gear];
        gear.Enchant.Charge = 5; gear.Enchant.Version = ItemEnchantBook.ChargeVersion - 1;
        l.C.SlotDroppedOn(EnchantScrollDialogController.Slot, "InventoryDialog.0", inv);
        Assert.That(l.Sent, Is.Empty, "the refund is asked first");
        var box = l.Boxes.Single();
        Assert.That((box.Type, box.ItemUid, box.EnterAllowed), Is.EqualTo((1, Gear, false)));
        box.InvokeCallback(1, null);
        Assert.That(l.Sent, Is.Empty, "the refund box's no");
        box.InvokeCallback(0, null);
        Assert.That(Bytes(l.Sent.Single()), Is.EqualTo(new byte[] { 9 }.Concat(BitConverter.GetBytes(Gear)).ToArray()));
        Assert.That(l.Sent.Single().Op, Is.EqualTo(Outbound.RequestItemEnchant));
        l.Sent.Clear();

        gear.Enchant.Version = ItemEnchantBook.ChargeVersion;
        l.C.SlotDroppedOn(EnchantScrollDialogController.Slot, "InventoryDialog.0", inv);
        Assert.That(Bytes(l.Sent.Single()), Is.EqualTo(Req(1, Scroll, Gear)));
        Assert.That(l.Sent.Single().Op, Is.EqualTo(Outbound.EnchantScroll));
        // nothing changes before the server's preview: no item on the slot, the item as it was
        Assert.That((l.Book.SlotItemUid, l.Book.ItemUid, gear.Enchant.Level), Is.EqualTo((0L, 0L, 3)));
        Assert.That(l.Stored, Is.Empty);
    }

    sealed class NotInven : IDragSlotOwner
    {
        public bool IsDragSlot(string name) => true;
        public void RefreshSlot(string name) { }
        public void SlotDragStarted(string name) { }
        public void SlotDropUnresolved(string name) { }
        public void SlotDroppedOn(string name, string source, IDragSlotOwner sourceOwner) { }
    }

    /// <summary>vfunc_28: onEnchant sends mode 2 with the open scroll only for an uid the inventory holds; the movie's "0"
    /// (UpdateScrollInfo's itemGUID), an unknown uid, a text that is no number and no argument send nothing; the item is not
    /// changed by the send.</summary>
    [Test]
    public void OnEnchantSendsOnlyForAnInventoryItem()
    {
        var r = new Rig();
        r.Open(1);
        foreach (var arg in new object[] { "0", "12345", "abc", "-5", "" })
        {
            r.C.OnCallback("onEnchant", new[] { arg });
            Assert.That(r.Sent, Is.Empty, $"onEnchant {arg}");
        }
        Assert.That(r.C.OnCallback("onEnchant", Array.Empty<object>()), Is.False);
        Assert.That(r.Sent, Is.Empty);
        r.C.OnCallback("onEnchant", new object[] { Gear.ToString() });
        Assert.That(Bytes(r.Sent.Single()), Is.EqualTo(Req(2, Scroll, Gear)));
        Assert.That((r.Inv[Gear].Enchant.Level, r.Stored.Count, r.Book.SlotItemUid), Is.EqualTo((3, 0, 0L)));
    }

    /// <summary>FUN_140f3bd10: the preview registers the slot with the item and fills the window (ready 1); a preview of an
    /// item the inventory does not hold keeps m_nItemUid but shows nothing. The movie's onEnchant then sends the guid it
    /// was given.</summary>
    [Test]
    public void APreviewPutsTheItemOnTheSlotAndTheMovieSendsItsGuid()
    {
        var r = new Rig();
        r.Open(1);
        r.Preview(Other + 100, 1, new[] { (0x1b, 40) });
        Assert.That((r.Book.ItemUid, r.Book.SlotItemUid), Is.EqualTo((Other + 100, 0L)));
        r.Preview(Gear, 1, new[] { (0x1b, 40), (0x1c, 0) });
        Assert.That((r.Book.ItemUid, r.Book.SlotItemUid, r.Book.SlotOwner), Is.EqualTo((Gear, Gear, Scroll)));
        Assert.That(r.C.GetBinding(EnchantScrollDialogController.Slot), Is.Not.Null);
        Assert.That(r.C.GetSlotTooltip(EnchantScrollDialogController.Slot), Is.Not.Empty);
        Assert.That(Rt.ToBoolean(r.Field("m_prop", "visible")), Is.True);
        Assert.That(Rt.ToBoolean(r.Field("m_limit", "visible")), Is.False);
        Assert.That(Rt.ToBoolean(r.Field("m_resultStat1", "visible")), Is.True, "one row with a value");
        Assert.That(Rt.ToBoolean(r.Field("m_resultStat2", "visible")), Is.False, "the row without a value is not shown");
        Assert.That(r.Sent, Is.Empty);
        // the button's CLICK listener is onEnchant, which calls back with the guid setData kept (m_guid)
        r.Host.Invoke(r.Host.Child("m_enchant"), "dispatchEvent", r.Host.RT.Construct(r.Host.RT.GetClass("flash.events.MouseEvent"), new object[] { "click" }));
        Assert.That(Bytes(r.Sent.Single()), Is.EqualTo(Req(2, Scroll, Gear)), "the movie's m_guid");
        // the right button takes the item off (vfunc_14 -> vfunc_17): the empty slot and the limits again, nothing sent
        r.Sent.Clear();
        r.C.OnCallback("notifySlotUse", new object[] { EnchantScrollDialogController.Slot, 1 });
        Assert.That((r.Book.SlotRegistered, r.Book.SlotItemUid), Is.EqualTo((true, 0L)));
        Assert.That(Rt.ToBoolean(r.Field("m_limit", "visible")), Is.True);
        Assert.That(r.Sent, Is.Empty);
    }

    /// <summary>The preview objects of every type: the keys FUN_140f3bd10 sets for 1 / 2, 5 and 3.</summary>
    [Test]
    public void ThePreviewObjectOfEveryType()
    {
        var r = new Rig();
        foreach (int type in new[] { 1, 2, 3, 5 })
        {
            r.Open(type);
            var p = type == 3
                ? new EnchantScrollMessage.Preview(Gear, 3, true, true, new[] { new EnchantScrollRow(0x1b, 10, 0) }, new[] { new EnchantScrollRow(0x1b, 30, 0) })
                : new EnchantScrollMessage.Preview(Gear, type, true, type != 5, type != 5 ? new[] { new EnchantScrollRow(0x1b, 10, 0) } : Array.Empty<EnchantScrollRow>(), Array.Empty<EnchantScrollRow>());
            var keys = r.C.PreviewData(p).Select(x => x.Item1).ToList();
            var want = new List<string> { "scrollType", "itemGUID", "bindID", "ready", "itemName", "desc" };
            if (type != 3) want.Add("prop");
            want.Add("resultItemName");
            if (type != 5) want.Add("options");
            Assert.That(keys, Is.EqualTo(want), $"type {type}");
            Assert.That(r.C.PreviewData(p with { Shown = false }), Is.Null);
            Assert.That(r.C.ScrollInfoData().Single(x => x.Item1 == "failDesc").Item2, Is.Not.Null);
        }
        r.Preview(Gear, 5);
        Assert.That(r.C.LevelText(), Is.EqualTo("3"), "type 5: the level of the item put on");
        r.Open(3);
        Assert.That(r.C.LevelText(), Is.EqualTo("2~6"));
        r.Open(1, target: 7);
        Assert.That(r.C.LevelText(), Is.EqualTo("7"));
        r.Open(4);
        Assert.That(r.C.LevelText(), Is.EqualTo(""));
    }

    /// <summary>FUN_140f3d5b0: the rows in CAtlMap(17) order (bins ascending, each bin newest first; a repeated key keeps its
    /// place and takes the last value), rows without a value dropped, before = the group 0 stat scaled by the enchant ratio,
    /// after = scaled by the ratio plus the row's rate (ratio stats 0x1b / 0x1c / 0x14 only).</summary>
    [Test]
    public void TheLevelRowsKeepTheMapsOrderAndTheClientsValues()
    {
        var r = new Rig();
        var gear = r.Inv[Gear];
        gear.Options.Stat[0][0x1b] = 1000; gear.Options.Stat[0][0x1c] = 2000; gear.Options.Stat[0][0x0a] = 300;
        gear.Enchant.Options[0x1b] = 100;     // the item's own enchant ratio 0.1
        var rows = new[]
        {
            new EnchantScrollRow(0x1c, 50, 0), new EnchantScrollRow(0x0a, 20, 0), new EnchantScrollRow(0x1b, 10, 0),
            new EnchantScrollRow(0x05, 0, 0), new EnchantScrollRow(0x1c, 70, 0),
        };
        var l = r.C.LevelRowValues(gear, rows);
        // bins: 0x0a and 0x1b both bin 10 (0x1b newer, first), 0x1c bin 11; 0x05 has no value
        Assert.That(l.Count, Is.EqualTo(3));
        Assert.That(l[0].PrevStat, Is.EqualTo("100")); Assert.That(l[0].AfterStat, Is.EqualTo("110"));
        Assert.That(l[1].PrevStat, Is.EqualTo("0")); Assert.That(l[1].AfterStat, Is.EqualTo("6"), "0x0a: no ratio stat, only the row's rate: 300 * 0.02");
        Assert.That(l[2].PrevStat, Is.EqualTo("0")); Assert.That(l[2].AfterStat, Is.EqualTo("140"));
        Assert.That(l[0].StatName, Is.EqualTo(r.Model.Tables.ItemOptions.StatValueText(0x1b).Replace("{0}", "")));
        Assert.That(l[2].StatName, Is.EqualTo(r.Model.Tables.ItemOptions.StatValueText(0x1c).Replace("{0}", "")));
    }

    /// <summary>FUN_140f3db20: first-seen keys over the low then the high rows, shown only when both lists hold the key with a
    /// value; afterStat "{low}~{high}".</summary>
    [Test]
    public void TheRandomRowsNeedTheKeyInBothLists()
    {
        var r = new Rig();
        var gear = r.Inv[Gear];
        gear.Options.Stat[0][0x1b] = 1000; gear.Options.Stat[0][0x1c] = 2000;
        var low = new[] { new EnchantScrollRow(0x1c, 20, 0), new EnchantScrollRow(0x1b, 10, 0), new EnchantScrollRow(0x14, 5, 0) };
        var high = new[] { new EnchantScrollRow(0x1b, 30, 0), new EnchantScrollRow(0x1c, 0, 0), new EnchantScrollRow(0x0a, 9, 0) };
        var l = r.C.RandomRowValues(gear, low, high);
        Assert.That(l.Count, Is.EqualTo(1), "0x1c has no high value, 0x14 no high row, 0x0a no low row");
        Assert.That((l[0].PrevStat, l[0].AfterStat), Is.EqualTo(("0", "10~30")));
    }

    /// <summary>The ratio: KMS value / 1000 of the ratio stats only; a GMS2 record's rate counts as well.</summary>
    [Test]
    public void TheEnchantRatioIsTheRatioStatsOnly()
    {
        var e = new EquipItem();
        e.Enchant.Options[0x1b] = 250; e.Enchant.Options[0x0a] = 900; e.Enchant.OptionRates[0x1c] = 0.5f;
        Assert.That(EnchantScrollDialogController.EnchantRatio(e, 0x1b), Is.EqualTo(0.25f));
        Assert.That(EnchantScrollDialogController.EnchantRatio(e, 0x0a), Is.EqualTo(0f));
        Assert.That(EnchantScrollDialogController.EnchantRatio(e, 0x1c), Is.EqualTo(0.5f));
        Assert.That(EnchantScrollDialogController.EnchantRatio(e, 0x14), Is.EqualTo(0f));
    }

    /// <summary>FUN_140f3c6c0: code 0 with the item stores the read copy and raises the "enchant_ok" box (type 0x11, the item,
    /// Enter off, the OK label 0x475) whose OK closes the window; codes 1..8 show notices 0xb72..0xb79 (code 1 then closes);
    /// other codes do nothing; a code 0 for an item not in the inventory stores nothing.</summary>
    [Test]
    public void TheResultStoresTheItemAndEveryCodeShowsItsNotice()
    {
        var r = new Rig();
        r.Open(1);
        r.Preview(Gear, 1, new[] { (0x1b, 40) });
        var body = Of(w => { var probe = new ByteReader(new byte[600]); ItemReader.ReadBodyInto(probe, new EquipItem { Id = 1 }); w.WriteBytes(new byte[probe.Position]); });
        r.Recv(Of(w => { w.WriteByte(3); w.WriteShort(0); w.WriteLong(Other + 100); w.WriteBytes(body); }));
        Assert.That((r.Stored.Count, r.Boxes.Count), Is.EqualTo((0, 0)));
        r.Recv(Of(w => { w.WriteByte(3); w.WriteShort(0); w.WriteLong(Gear); w.WriteBytes(body); }));
        Assert.That(r.Stored.Single().Uid, Is.EqualTo(Gear));
        Assert.That(r.Stored.Single(), Is.Not.SameAs(r.Inv[Gear]));
        var box = r.Boxes.Single();
        Assert.That((box.Type, box.Extra, box.ItemUid, box.EnterAllowed), Is.EqualTo((0x11, EnchantScrollDialogController.ResultTag, Gear, false)));
        Assert.That(r.Sent, Is.Empty);
        Assert.That(r.Closes, Is.EqualTo(0));
        box.InvokeCallback(0, null);
        Assert.That(r.Closes, Is.EqualTo(1));
        Assert.That((r.Book.SlotRegistered, r.Book.ScrollUid, r.Book.Type), Is.EqualTo((false, 0L, 1)), "the reset keeps m_nType");
        for (int i = 0; i < 300 && !r.C.IsClosed; i++) r.Host.Player.Tick();
        Assert.That(r.C.IsClosed, Is.True);

        var n = new Rig(load: false);
        n.Book.IsOpen = true;
        n.Open(2);
        for (int code = 1; code <= 9; code++)
            n.Recv(Of(w => { w.WriteByte(3); w.WriteShort((short)code); }));
        Assert.That(n.NoticeIds, Is.EqualTo(Enumerable.Range(0, 8).Select(i => EnchantScrollBook.FirstNotice + i)));
        Assert.That(n.NoticeIds.Last(), Is.EqualTo(0xb79));
        Assert.That(n.Closes, Is.EqualTo(1), "only code 1 closes");
        Assert.That((n.Sent.Count, n.Stored.Count), Is.EqualTo((0, 0)));
    }
}
