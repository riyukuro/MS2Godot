using System.Globalization;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using NUnit.Framework;
using Maple2.PacketLib.Tools;

namespace Ms2.Ui.Tests;

/// <summary>The beauty shop's saved-styles dialog (uibeautyshopstyledialog, FUN_140f05a80 @0x140f05a80; needs MS2_DATA): the packet
/// modes, the slot math and the extend / delete asks with their sends. Texts are not compared.</summary>
public class BeautyStyleDialogTests
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

    sealed class Cust : IBeautyCustomize
    {
        public int EditItemId { get; set; }
        public bool HasChanges { get; set; }
        public bool HasStyleChanges { get; set; }
        public bool IsEditItemAdded { get; set; }
        public bool IsEditItemColorChanged { get; set; }
        public bool IsDragging { get; set; }
        public List<string> Calls = new();
        public void UpdateEditItems() => Calls.Add("update");
        public void CommitSlot(int s) => Calls.Add("commit" + s);
        public void RestoreSlot(int s) => Calls.Add("restore" + s);
    }

    sealed class Rig
    {
        public BeautyStyleDialogController C = new();
        public Cust Cu = new();
        public List<IOutPacket> Sent = new();
        public List<MessageBoxData> Boxes = new();
        public List<string> Notices = new();
        public List<int> NoticeTypes = new();
        public Dictionary<long, EquipItem> Worn = new();

        public Rig(bool load = true)
        {
            Packs();
            var model = new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true };
            C.Customize = Cu;
            C.Send = Sent.Add;
            C.ShowData = Boxes.Add;
            C.NoticeText += (t, ty) => { Notices.Add(t); NoticeTypes.Add(ty); };
            C.CloneWorn = uid => Worn.TryGetValue(uid, out var w) ? new EquipItem { Id = w.Id, Uid = w.Uid } : null;
            C.ItemName = id => "item" + id;
            if (!load) return;
            var (rt, p) = src.Open(C.Movie, r => r.Budget = 20_000_000);
            p.StageW = 1280; p.StageH = 720;
            p.Translate = k => k.TrimStart('$');
            var host = new UiHost(p, C, model);
            Assert.That(host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
            for (int i = 0; i < 10; i++) p.Tick();
        }

        public static BeautyStylePacket Shop(int kind = 1) => new(2)
        {
            ShopMode = 1,
            Shop = new BeautyShopData(7, kind, 0, false, 0, 0, false, new BeautyPayment(1, 0, 5000, ""), new BeautyPayment(1, 0, 700, "")),
        };

        public void List(params (int id, long uid)[] items)
        {
            C.OnPacket(new BeautyStylePacket(0xd));
            C.OnPacket(new BeautyStylePacket(0xe) { ExpectedStyles = items.Length });
            C.OnPacket(new BeautyStylePacket(0xf)
            {
                Styles = items.Select((x, i) => new BeautyStyleItem(i, new EquipItem { Id = x.id, Uid = x.uid })).ToArray(),
            });
        }
    }

    [Test]
    public void SlotMathFollowsTheConstants()
    {
        var r = new Rig(false);
        Assert.That(r.C.MaxSlotCount, Is.EqualTo(13), "default 1 + max extend 12");
        r.C.SetOpenSlots(99);
        Assert.That(r.C.OpenSlots, Is.EqualTo(13), "min(n, max)");
        Assert.That(r.C.AtMaxSlots, Is.True);
        r.C.SetOpenSlots(4);
        Assert.That(r.C.AtMaxSlots, Is.False);
        Assert.That(r.C.RemainSlots, Is.EqualTo(4));
        Assert.That(r.C.SavedAt(0), Is.Null);
        Assert.That(r.C.SavedAt(-1), Is.Null);
        Assert.That(new[] { 1, 2, 3, 4 }.Select(BeautyStyleDialogController.SlotOfKind), Is.EqualTo(new[] { 1, 3, 2, 0 }));
    }

    [Test]
    public void TheConstantsComeFromTheTable()
    {
        Packs();
        var t = new GameTables(packs, "en");
        // each the data's own row (KMS2: 330, 1, 30, 5; GMS2: the NA price 980 and a max of 0, no slot to buy)
        long Want(string key, long fallback) => ClientConstantsTests.TableValue(packs, key) is { } v ? long.Parse(v, CultureInfo.InvariantCulture) : fallback;
        Assert.That(t.BeautyStyleExpandSlotPrice, Is.EqualTo(Want("BeautyStyleExpandSlotPrice", 790)));
        Assert.That(t.BeautyStyleExpandSlotCount1time, Is.EqualTo(Want("BeautyStyleExpandSlotCount1time", 3)));
        Assert.That(t.BeautyStyleDefaultSlotCount, Is.EqualTo(Want("BeautyStyleDefaultSlotCount", 1)));
        Assert.That(t.BeautyStyleMaxSlotCount, Is.EqualTo(Want("BeautyStyleMaxSlotCount", 12)));
        Assert.That(t.BeautyStyleExpandSlotPrice, Is.Not.EqualTo(790), "the table has a row for the price");
    }

    [Test]
    public void TheListModesBuildTheSavedList()
    {
        var r = new Rig();
        r.C.OnPacket(Rig.Shop(2));
        Assert.That(r.C.Shop.ShopId, Is.EqualTo(7));
        Assert.That(r.C.ShopMode, Is.EqualTo(1));
        r.C.OnPacket(new BeautyStylePacket(0xe) { ExpectedStyles = 2 });
        Assert.That(r.C.ListArrived, Is.True);
        Assert.That(r.C.DataReady, Is.False);
        r.C.OnPacket(new BeautyStylePacket(0xf) { Styles = new[] { new BeautyStyleItem(0, new EquipItem { Id = 1, Uid = 11 }) } });
        Assert.That(r.C.DataReady, Is.False, "one of two");
        r.C.OnPacket(new BeautyStylePacket(0xf) { Styles = new[] { new BeautyStyleItem(1, new EquipItem { Id = 2, Uid = 12 }) } });
        Assert.That(r.C.DataReady, Is.True);
        Assert.That(r.C.Saved.Select(s => s.Uid), Is.EqualTo(new long[] { 11, 12 }));
        r.C.OnPacket(new BeautyStylePacket(0xd));
        Assert.That(r.C.Saved, Is.Empty);
    }

    [Test]
    public void ExtendedSlotsShowTheCountOfTheAddedOnes()
    {
        var r = new Rig();
        r.C.OnPacket(new BeautyStylePacket(0xe) { ExpectedStyles = 0 });
        r.C.SetOpenSlots(1);
        r.C.OnPacket(new BeautyStylePacket(0x14) { ExtendType = 0, ExtendedSlots = 3 });
        Assert.That(r.C.OpenSlots, Is.EqualTo(4), "extended + default");
        Assert.That(r.Notices, Has.Count.EqualTo(1));
        r.C.OnPacket(new BeautyStylePacket(0x14) { ExtendedSlots = 3 });
        Assert.That(r.Notices, Has.Count.EqualTo(1), "nothing added, no notice");
        r.C.OnPacket(new BeautyStylePacket(0x14) { ExtendedSlots = 500 });
        Assert.That(r.C.OpenSlots, Is.EqualTo(r.C.MaxSlotCount));
    }

    [Test]
    public void ASaveCopiesTheWornItemIntoTheList()
    {
        var r = new Rig();
        r.Worn[5] = new EquipItem { Id = 900, Uid = 5 };
        r.C.Waiting = true;
        r.C.OnPacket(new BeautyStylePacket(0x10) { Uid = 5, NewUid = 77, Slot = 2, Time = 1234 });
        Assert.That(r.C.Waiting, Is.False);
        Assert.That(r.C.Saved, Has.Count.EqualTo(1));
        Assert.That(r.C.Saved[0].Uid, Is.EqualTo(77));
        Assert.That(r.C.Saved[0].CreationTime, Is.EqualTo(1234));
        Assert.That(r.C.Saved[0].Id, Is.EqualTo(900));
        Assert.That(r.Notices, Has.Count.EqualTo(1));
        r.C.OnPacket(new BeautyStylePacket(0x11) { Uid = 404, NewUid = 78 });
        Assert.That(r.C.Saved, Has.Count.EqualTo(1), "no worn item: nothing");
        Assert.That(r.Notices, Has.Count.EqualTo(1));
    }

    [Test]
    public void ARemoveDropsTheItemAndRestoresTheSlot()
    {
        var r = new Rig();
        r.C.OnPacket(Rig.Shop(2));
        r.List((1, 11), (2, 12), (3, 13));
        r.C.OnPacket(new BeautyStylePacket(0x12) { Uid = 12 });
        Assert.That(r.C.Saved.Select(s => s.Uid), Is.EqualTo(new long[] { 11, 13 }));
        Assert.That(r.Cu.Calls, Is.EqualTo(new[] { "restore3" }), "slot of shop kind 2");
        Assert.That(r.Notices, Has.Count.EqualTo(1));
    }

    [Test]
    public void AnApplyShowsTheNoticeAndRestoresTheSlot()
    {
        var r = new Rig();
        r.C.OnPacket(Rig.Shop(1));
        int effects = 0;
        r.C.PlayHairApplyEffect = () => effects++;
        r.C.OnPacket(new BeautyStylePacket(0x15));
        Assert.That(effects, Is.EqualTo(1));
        Assert.That(r.Cu.Calls, Is.EqualTo(new[] { "restore1" }));
        Assert.That(r.Notices, Has.Count.EqualTo(1));
        Assert.That(r.NoticeTypes, Is.EqualTo(new[] { 3 }), "FUN_1412752d0(..., 3) at :1327");
    }

    [Test]
    public void TheOtherNoticesCarryTypeOne()
    {
        var r = new Rig();
        r.Worn[5] = new EquipItem { Id = 900, Uid = 5 };
        r.C.OnPacket(Rig.Shop(2));
        r.List((1, 11));
        r.C.OnPacket(new BeautyStylePacket(0x10) { Uid = 5, NewUid = 77, Slot = 2, Time = 1 });
        r.C.OnPacket(new BeautyStylePacket(0x12) { Uid = 11 });
        r.C.OnPacket(new BeautyStylePacket(8) { Result = 0x18 });
        Assert.That(r.NoticeTypes, Is.EqualTo(new[] { 1, 1, 1 }));
    }

    [Test]
    public void TheSavedItemsKeepTheirSlot()
    {
        var r = new Rig();
        r.Worn[5] = new EquipItem { Id = 900, Uid = 5 };
        r.C.OnPacket(new BeautyStylePacket(0xf)
        {
            Styles = new[] { new BeautyStyleItem(4, new EquipItem { Id = 1, Uid = 11 }), new BeautyStyleItem(9, new EquipItem { Id = 2, Uid = 12 }) },
        });
        Assert.That(r.C.Saved.Select(x => (int)x.Slot), Is.EqualTo(new[] { 4, 9 }), "CItem::SetSlot(i32) on every listed item");
        r.C.OnPacket(new BeautyStylePacket(0x10) { Uid = 5, NewUid = 77, Slot = 6, Time = 1 });
        Assert.That((int)r.C.Saved[2].Slot, Is.EqualTo(6), "CItem::SetSlot(byte) on the saved copy");
    }

    [Test]
    public void ApplyTablesTakesTheTableValues()
    {
        Packs();
        var t = new GameTables(packs, "en") { BeautyStyleExpandSlotPrice = 123, BeautyStyleExpandSlotCount1time = 5, BeautyStyleDefaultSlotCount = 2, BeautyStyleMaxSlotCount = 7 };
        var c = new BeautyStyleDialogController();
        c.ApplyTables(t);
        Assert.That(c.ExpandSlotPrice, Is.EqualTo(123));
        Assert.That(c.ExpandSlotCount1Time, Is.EqualTo(5));
        Assert.That(c.DefaultSlotCount, Is.EqualTo(2));
        Assert.That(c.MaxSlotCount, Is.EqualTo(9));
    }

    [Test]
    public void AClosedDialogIgnoresTheGatedModes()
    {
        var r = new Rig();
        r.C.OnPacket(Rig.Shop(1));
        r.C.Close();
        r.C.Waiting = true;
        r.C.OnPacket(new BeautyStylePacket(0x15));
        r.C.OnPacket(new BeautyStylePacket(0x12) { Uid = 1 });
        r.C.OnPacket(new BeautyStylePacket(8) { Result = 7 });
        Assert.That(r.Notices, Is.Empty);
        Assert.That(r.Boxes, Is.Empty);
        Assert.That(r.Cu.Calls, Is.Empty);
        Assert.That(r.C.Waiting, Is.True, "a closed dialog does not reset the pending flag");
    }

    [Test]
    public void TheResultModesShowTheirBoxesOrNotices()
    {
        var r = new Rig();
        int notEnough = 0, shortcut = 0;
        r.C.NotEnough = () => notEnough++;
        r.C.Unavailable = () => shortcut++;
        r.C.OnPacket(new BeautyStylePacket(8) { Result = 2 });
        r.C.OnPacket(new BeautyStylePacket(8) { Result = 0x14 });
        Assert.That((notEnough, shortcut), Is.EqualTo((1, 1)));
        r.C.OnPacket(new BeautyStylePacket(8) { Result = 0x18 });
        r.C.OnPacket(new BeautyStylePacket(8) { Result = 0x19 });
        Assert.That(r.Notices, Has.Count.EqualTo(2));
        Assert.That(r.Boxes, Is.Empty);
        r.C.OnPacket(new BeautyStylePacket(8) { Result = 0x13 });
        r.C.OnPacket(new BeautyStylePacket(8) { Result = 0x17 });
        r.C.OnPacket(new BeautyStylePacket(8) { Result = 7 });
        r.C.OnPacket(new BeautyStylePacket(8) { Result = 99 });
        Assert.That(r.Boxes, Has.Count.EqualTo(4));
        Assert.That(r.Boxes[0].Callback(0, r.Boxes[0]), Is.True);
        Assert.That(r.C.Closing, Is.False, "the plain box leaves the dialog open");
        r.Boxes[3].Callback(1, r.Boxes[3]);
        Assert.That(r.C.Closing, Is.False, "answer 1 does nothing");
        r.Boxes[3].Callback(0, r.Boxes[3]);
        Assert.That(r.C.Closing, Is.True, "the code box ends the dialog on ok");
    }

    [Test]
    public void EscWithoutAStyleChangeLeavesAtOnceAndRestoresTheWornSlot()
    {
        var r = new Rig();
        r.C.OnPacket(Rig.Shop(kind: 2));
        r.Cu.HasChanges = true;          // the shop's test (FUN_140cd2a00) says changed, the style dialog's (FUN_140cd2c20) does not
        Assert.That(r.C.EscapeKey(), Is.True);
        Assert.That(r.Boxes, Is.Empty, "no change: no ask");
        Assert.That(r.Cu.Calls, Is.EqualTo(new[] { "restore3" }), "FUN_140f08220 restores the slot of shop kind 2");
        Assert.That(r.C.Closing, Is.True);
    }

    [Test]
    public void EscWithAStyleChangeAsksAndOkLeaves()
    {
        var r = new Rig();
        r.C.OnPacket(Rig.Shop(kind: 1));
        r.Cu.HasStyleChanges = true;
        var common = MessageBoxData.CommonByKey;
        MessageBoxData.CommonByKey = k => "#" + k;
        try
        {
            Assert.That(r.C.EscapeKey(), Is.True);
            Assert.That(r.Boxes, Has.Count.EqualTo(1));
            Assert.That((r.Boxes[0].Btn0, r.Boxes[0].Btn1), Is.EqualTo((MessageBoxData.Common(0x475), MessageBoxData.Common(0x476))), "button preset 2: ok / cancel");
            Assert.That(r.Boxes[0].Btn0, Is.Not.EqualTo(MessageBoxData.Common(0x477)).And.Not.Empty, "not the yes / no of the box type");
        }
        finally { MessageBoxData.CommonByKey = common; }
        var box = r.Boxes[0];
        Assert.That(box.Msg, Is.EqualTo(r.C.Model.Tables.Common(0x271)).And.Not.Empty);
        Assert.That(r.C.Closing, Is.False, "the ask leaves the dialog open");
        Assert.That(box.Callback(1, box), Is.True);
        Assert.That((r.C.Closing, r.Cu.Calls.Count), Is.EqualTo((false, 0)), "cancel: nothing");
        Assert.That(box.Callback(0, box), Is.True);
        Assert.That(r.Cu.Calls, Is.EqualTo(new[] { "restore1" }));
        Assert.That(r.C.Closing, Is.True);
    }

    [Test]
    public void ShopModeTwoAndTheCloseButtonFollowTheSamePath()
    {
        var r = new Rig();
        var p = Rig.Shop(kind: 1);
        p.ShopMode = 2;
        r.C.OnPacket(p);
        r.Cu.HasStyleChanges = true;
        Assert.That(r.C.OnCallback("OnClickClose", System.Array.Empty<object>()), Is.True);
        Assert.That(r.Boxes, Is.Empty, "shop mode 2 leaves without the ask");
        Assert.That(r.C.Closing, Is.True);

        var q = new Rig();
        q.C.OnPacket(Rig.Shop(kind: 1));
        q.Cu.HasStyleChanges = true;
        Assert.That(q.C.OnCallback("OnClickClose", System.Array.Empty<object>()), Is.True);
        Assert.That(q.Boxes, Has.Count.EqualTo(1), "the close button asks as Esc does");
    }

    [Test]
    public void WithoutACustomizeEscIsTakenAndNothingHappens()
    {
        var r = new Rig();
        r.C.Customize = null;
        Assert.That(r.C.EscapeKey(), Is.True, "vfunc_13 returns 1 after FUN_140f082d0");
        Assert.That(r.Boxes, Is.Empty);
        Assert.That(r.C.Closing, Is.False, "FUN_140f082d0 returns at once without m_198");
    }

    [Test]
    public void ExtendAsksFirstAndSendsMode0x13OnYes()
    {
        var r = new Rig();
        r.C.SetOpenSlots(2);
        r.C.ClickExtendSlot();
        Assert.That(r.Boxes, Has.Count.EqualTo(1));
        Assert.That(r.Sent, Is.Empty, "nothing before the answer");
        Assert.That(r.Boxes[0].Callback(1, r.Boxes[0]), Is.True);
        Assert.That(r.Sent, Is.Empty, "no answer: nothing");
        Assert.That(r.Boxes[0].Callback(0, r.Boxes[0]), Is.True);
        Assert.That(r.Sent, Has.Count.EqualTo(1));
        Assert.That(Bytes(r.Sent[0]), Is.EqualTo(Bytes(new BeautyExtendStylesRequest())));
        Assert.That(r.C.Waiting, Is.True);
    }

    [Test]
    public void ExtendAtTheMaximumShowsAPlainBoxAndSendsNothing()
    {
        var r = new Rig();
        r.C.SetOpenSlots(r.C.MaxSlotCount);
        r.C.ClickExtendSlot();
        Assert.That(r.Boxes, Has.Count.EqualTo(1));
        r.Boxes[0].Callback(0, r.Boxes[0]);
        Assert.That(r.Sent, Is.Empty);
    }

    [Test]
    public void DeleteAsksWithTheItemAndSendsUidAndIndexOnYes()
    {
        var r = new Rig();
        r.List((1, 11), (2, 12));
        r.C.ClickDelete(1);
        Assert.That(r.Boxes, Has.Count.EqualTo(1));
        Assert.That(r.Boxes[0].Msg, Does.Contain("item2").Or.Not.Empty);
        r.Boxes[0].Callback(1, r.Boxes[0]);
        Assert.That(r.Sent, Is.Empty);
        r.Boxes[0].Callback(0, r.Boxes[0]);
        Assert.That(Bytes(r.Sent.Single()), Is.EqualTo(Bytes(new BeautyDeleteStyleRequest(12, 1))));
        Assert.That(r.C.Waiting, Is.True);
    }

    [Test]
    public void DeletingAnEmptySlotShowsAPlainBoxAndSendsNothing()
    {
        var r = new Rig();
        r.List((1, 11));
        r.C.ClickDelete(5);
        r.C.ClickDelete(-1);
        Assert.That(r.Boxes, Has.Count.EqualTo(2));
        Assert.That(r.Sent, Is.Empty);
    }
}
