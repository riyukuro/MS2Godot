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

/// <summary>The merge revert window (uiitemmergerevertdialog, UI 0x4f3d; needs MS2_DATA): CheckItem's refusals, the notices, the
/// pick, onOk's send, the answer and the inventory's use mode 17.</summary>
public class ItemMergeRevertDialogTests
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
        public ItemMergeRevertDialogController C = new();
        public List<IOutPacket> Sent = new();
        public List<string> Notices = new(), Done = new();
        public Dictionary<long, Item> Inv = new();
        public UiModel Model;
        public UiHost Host;
        public int[] Ids;

        public Rig()
        {
            Packs();
            Model = new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true };
            Ids = new[] { 20000001, 20000002, 30000001, 11300001, 12000001 }.Where(i => !string.IsNullOrEmpty(Model.Tables.ItemName(i))).ToArray();
            if (Ids.Length < 3) Assert.Ignore("no three known items");
            C.Send = Sent.Add;
            C.Find = u => Inv.GetValueOrDefault(u);
            C.Notice = Notices.Add;
            C.DoneNotice = Done.Add;
            C.Text = k => k;
            C.ItemName = i => "item" + i.ItemId;
            var (rt, p) = src.Open(C.Movie, r => r.Budget = 20_000_000);
            p.StageW = 1280; p.StageH = 720;
            p.Translate = k => k.TrimStart('$');
            Host = new UiHost(p, C, Model);
            Assert.That(Host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
            for (int i = 0; i < 10; i++) p.Tick();
        }

        Item Make(long uid, int id, int rarity, bool merged)
        {
            var it = Item.Create(Model.Tables.Items, id, rarity);
            it.Uid = uid; it.Rarity = rarity;
            var e = new EquipItem { Id = id, Uid = uid };
            if (merged) e.Options.Stat[4][4] = 10;
            it.Record = e;
            Inv[uid] = it;
            return it;
        }

        /// <summary>A scroll of rank 3 whose kind is the first item's, and the item.</summary>
        public (Item scroll, Item item) Pair(int itemRarity = 3, bool merged = true, int scrollKind = -1, bool fusionable = true)
        {
            Model.Tables.Items.Get(Ids[0]).Fusionable = fusionable ? 1 : 0;
            var item = Make(7, Ids[0], itemRarity, merged);
            var scroll = Make(9, Ids[2], 3, false);
            scroll.Data.FunctionArgs.Clear();
            scroll.Data.FunctionArgs.Add((scrollKind < 0 ? item.Data.MergeRevertKind : scrollKind).ToString());
            C.SetItemUid(scroll);
            return (scroll, item);
        }
    }

    [Test]
    public void LoadsEmpty()
    {
        var r = new Rig();
        Assert.That(r.C.Picked, Is.Null);
        Assert.That(r.Host.Entity, Is.Not.Null);
    }

    [Test]
    public void CheckItemRefusals()
    {
        var r = new Rig();
        var (scroll, item) = r.Pair();
        Assert.That(r.C.CheckItem(item), Is.EqualTo(ItemMergeRevertDialogController.CheckOk));
        Assert.That(r.C.CheckItem(null), Is.EqualTo(ItemMergeRevertDialogController.CheckInvalidItem));
        r.Pair(fusionable: false);
        Assert.That(r.C.CheckItem(r.Inv[7]), Is.EqualTo(ItemMergeRevertDialogController.CheckInvalidItem));
        (scroll, item) = r.Pair(merged: false);
        Assert.That(r.C.CheckItem(item), Is.EqualTo(ItemMergeRevertDialogController.CheckNoOption));
        (scroll, item) = r.Pair(scrollKind: 9);
        Assert.That(r.C.CheckItem(item), Is.EqualTo(ItemMergeRevertDialogController.CheckSlot));
        (scroll, item) = r.Pair();
        scroll.Rarity = 4;
        Assert.That(r.C.CheckItem(item), Is.EqualTo(ItemMergeRevertDialogController.CheckRank));
        scroll.Rarity = 3;
        r.Inv.Remove(9);
        Assert.That(r.C.CheckItem(item), Is.EqualTo(ItemMergeRevertDialogController.CheckNoScroll));
    }

    [Test]
    public void NoticesFollowTheCode()
    {
        var r = new Rig();
        r.Pair(scrollKind: 9);
        r.C.UseFromInventory(r.Inv[7]);
        Assert.That(r.Notices.Single(), Is.EqualTo("s_item_merge_option_error_invalid_revert_slot"));
        Assert.That(r.C.Picked, Is.Null);
        r.Notices.Clear();
        r.C.ShowErrorMessage(6);
        r.C.ShowErrorMessage(7);
        r.C.ShowErrorMessage(5);
        r.C.ShowErrorMessage(99);
        Assert.That(r.Notices, Is.EqualTo(new[] { "s_item_merge_option_error_invalid_revert_rank", "s_item_merge_option_error_invalid_revert_option", "s_item_merge_option_error_invalid_merge_scroll" }));
    }

    [Test]
    public void PickThenOkSendsModeSix()
    {
        var r = new Rig();
        var (scroll, item) = r.Pair();
        r.C.UseFromInventory(item);
        Assert.That(r.C.Picked, Is.SameAs(item));
        Assert.That(r.C.OnCallback("OnOk", new object[] { "7" }), Is.True);
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ItemMergeRequest.Revert(9, 7))));
    }

    [Test]
    public void OkWithUnknownOrRefusedItemSendsNothing()
    {
        var r = new Rig();
        r.Pair();
        r.C.OnCallback("onOk", new object[] { "55" });
        r.C.OnCallback("onOk", Array.Empty<object>());
        r.Pair(merged: false);
        r.C.OnCallback("onOk", new object[] { "7" });
        Assert.That(r.Sent, Is.Empty);
        Assert.That(r.C.OnCallback("other", null), Is.False);
    }

    [Test]
    public void AnswerNoticesAndCloses()
    {
        var r = new Rig();
        var (_, item) = r.Pair();
        int closed = 0; r.C.Closed += () => closed++;
        r.C.ShowReverted(item);
        Assert.That(r.Done.Single(), Does.Contain("s_item_merge_option_revert").Or.Contain("item"));
        Assert.That(closed, Is.EqualTo(1));
        Assert.That(r.C.ScrollUid, Is.EqualTo(0));
    }

    [Test]
    public void ClosingForgetsTheScroll()
    {
        var r = new Rig();
        r.Pair();
        r.C.Close();
        Assert.That(r.C.ScrollUid, Is.EqualTo(0));
        Assert.That(r.C.Picked, Is.Null);
    }

    [Test]
    public void RemoveEmptiesTheSlot()
    {
        var r = new Rig();
        var (_, item) = r.Pair();
        r.C.UseFromInventory(item);
        r.C.RemoveFromSlot();
        Assert.That(r.C.Picked, Is.Null);
    }

    [Test]
    public void MergeKindOfSubtypes()
    {
        var d = new ItemData();
        Assert.That(d.MergeRevertKind, Is.EqualTo(1));
    }
}
