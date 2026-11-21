using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CItemBreakManager (itembreakmanager.cpp): the add/remove refusals, the confirm boxes, each send and the packet's changes.</summary>
public class ItemBreakManagerTests
{
    readonly Dictionary<long, Item> inv = new();
    readonly List<IOutPacket> sent = new();
    readonly List<(int Id, int Arg)> noticeIds = new();
    readonly List<string> noticeKeys = new();
    readonly List<MessageBoxData> boxes = new();
    readonly List<string> sounds = new();
    ItemBreakManager m;
    string typed = "";

    [SetUp]
    public void Setup()
    {
        inv.Clear(); sent.Clear(); noticeIds.Clear(); noticeKeys.Clear(); boxes.Clear(); sounds.Clear(); typed = "";
        m = new ItemBreakManager
        {
            Find = u => inv.GetValueOrDefault(u),
            Send = sent.Add,
            NoticeId = (i, a) => noticeIds.Add((i, a)),
            NoticeKey = noticeKeys.Add,
            ShowBox = boxes.Add,
            Sound = sounds.Add,
            InputText = _ => typed,
            DialogOpen = true,
        };
        m.SendMode0(1);
        sent.Clear();
    }

    Item Make(long uid, int rarity = 3, uint count = 1, uint slotMax = 1, bool skin = false)
    {
        var d = new ItemData { Id = 11000000 + (int)uid, SlotMax = slotMax };
        if (skin) d.SkinType = 1;
        var it = Item.Create(d, rarity);
        it.Uid = uid; it.Count = count; it.Rarity = rarity;
        inv[uid] = it;
        return it;
    }

    static string Bytes(IOutPacket p)
    {
        var w = new Maple2.PacketLib.Tools.ByteWriter();
        p.Write(w);
        return Convert.ToHexString(w.ToArray());
    }

    [Test]
    public void ModeZeroClearsTheListAndSendsTheType()
    {
        var it = Make(1);
        m.OnPacket(new ItemDismantlePacket(1) { Slots = new[] { new DismantleSlot(4, 1, 1) } });
        Assert.That(m.Contains(1), Is.True);
        m.SendMode0(2);
        Assert.That((m.Type, m.Contains(1), Bytes(sent[^1])), Is.EqualTo(((byte)2, false, "0002")));
        m.ResetBreakMode();
        Assert.That((m.Type, Bytes(sent[^1])), Is.EqualTo(((byte)0, "0000")));
    }

    [Test]
    public void LockedItemIsNotBreakable()
    {
        var it = Make(1);
        Assert.That(m.CanBreakItem(it), Is.True);
        it.LockState = 1;
        Assert.That(m.CanBreakItem(it), Is.False, "locked");
        it.LockState = 0; it.UnlockTime = 500; m.ServerTime = () => 100;
        Assert.That(m.CanBreakItem(it), Is.False, "unlocking");
        m.ServerTime = () => 600;
        Assert.That(m.CanBreakItem(it), Is.True, "unlock time passed");
        Assert.That(m.CanBreakItem(null), Is.False);
    }

    [Test]
    public void SummonedPetPcBangTransferAndSkinRefuse()
    {
        var it = Make(1);
        m.SummonedPetUid = () => 1;
        Assert.That(m.CanBreakItem(it), Is.False, "the summoned pet");
        m.SummonedPetUid = () => 99;
        Assert.That(m.CanBreakItem(it), Is.True);
        it.Record = new EquipItem { Transfer = new ItemTransfer { BreakDisabled = true } };
        Assert.That(m.CanBreakItem(it), Is.False, "BreakDisabled");
        it.Record = null;
        it.Data.PCBang = true;
        Assert.That(m.CanBreakItem(it), Is.False, "PC bang");
        it.Data.PCBang = false;
        var skin = Make(2, skin: true);
        Assert.That(m.CanBreakItem(skin), Is.False, "skin without feature 0x3a");
        m.SkinFeature = () => true;
        Assert.That(m.CanBreakItem(skin), Is.False, "skin without a reward");
        m.SkinHasReward = _ => true;
        Assert.That(m.CanBreakItem(skin), Is.True);
        m.SkinHasReward = _ => false; m.HasIngredients = _ => true;
        Assert.That(m.CanBreakItem(skin), Is.True, "a skin with an ingredient entry");
        m.HasIngredients = _ => false;
        Assert.That(m.CanBreakItem(skin), Is.False);
        m.GachaBox = _ => true;
        Assert.That(m.CanBreakItem(skin), Is.True, "the gacha box path skips the rest");
    }

    [Test]
    public void GemsNotBreakableBlockWithABox()
    {
        var it = Make(1);
        m.GemsBreakable = _ => false;
        Assert.That(m.CanBreakItem(it), Is.True, "CanBreakItem (@0x140531270) does not look at the gems; TryAddItem does");
        Assert.That(m.TryAddItem(it, 3), Is.EqualTo(0));
        Assert.That((boxes.Count, boxes[0].Type, sent.Count), Is.EqualTo((1, 0, 0)));
    }

    [Test]
    public void TryAddRefusesInTheClientsOrder()
    {
        Assert.That(m.TryAddItem(null, 0), Is.EqualTo(0));
        var it = Make(1);
        m.CanUseItem = _ => false;
        Assert.That(m.TryAddItem(it, 0), Is.EqualTo(0), "the item lock check");
        Assert.That(sent, Is.Empty);
        m.CanUseItem = _ => true;
        m.OnPacket(new ItemDismantlePacket(1) { Slots = new[] { new DismantleSlot(0, 1, 1) } });
        Assert.That(m.TryAddItem(it, 0), Is.EqualTo(0));
        Assert.That(noticeKeys, Is.EqualTo(new[] { "s_itembreak_selected_item" }), "already listed");
        var skin = Make(2, skin: true);
        Assert.That(m.TryAddItem(skin, 0), Is.EqualTo(0));
        Assert.That(noticeIds[^1], Is.EqualTo((0x8f6, int.MinValue)), "type 1: transfer refuses");
        m.SendMode0(2);
        Assert.That(m.TryAddItem(skin, 0), Is.EqualTo(0));
        Assert.That(noticeIds[^1], Is.EqualTo((0x8f7, int.MinValue)), "type 2");
    }

    [Test]
    public void PlainItemSendsModeOneWithTheArgument()
    {
        var it = Make(0x0102, count: 1);
        sent.Clear();
        Assert.That(m.TryAddItem(it, 7), Is.EqualTo(1));
        Assert.That(Bytes(sent.Single()), Is.EqualTo("01" + "07000000" + "0201000000000000" + "01000000"));
        Assert.That(m.TryAddItem(Make(5), -1), Is.EqualTo(1));
        Assert.That(Bytes(sent[^1]).StartsWith("01FFFFFFFF"), Is.True, "the right-click arg -1");
    }

    [Test]
    public void StackAsksTheCountAndChecksIt()
    {
        var it = Make(9, count: 50, slotMax: 99);
        sent.Clear();
        Assert.That(m.TryAddItem(it, 2), Is.EqualTo(1));
        var box = boxes.Single();
        Assert.That((box.Type, sent.Count, box.InputDefault), Is.EqualTo((5, 0, "50")));
        typed = "0";
        Assert.That(box.Callback(0, box), Is.False, "0 is refused");
        typed = "51";
        Assert.That(box.Callback(0, box), Is.False, "above the stack");
        Assert.That(noticeIds, Is.EqualTo(new[] { (0xee, 50), (0xee, 50) }));
        Assert.That(sent, Is.Empty);
        typed = "1,0";
        Assert.That(box.Callback(0, box), Is.True);
        Assert.That(Bytes(sent.Single()), Is.EqualTo("01" + "02000000" + "0900000000000000" + "0A000000"));
        sent.Clear();
        Assert.That(box.Callback(1, box), Is.True, "no sends");
        Assert.That(sent, Is.Empty);
    }

    [Test]
    public void HighSaOptionAsksToConfirmFirst()
    {
        var it = Make(3, rarity: 5);
        m.HighSaOption = _ => "Option";
        sent.Clear();
        Assert.That(m.TryAddItem(it, -1), Is.EqualTo(1));
        var box = boxes.Single();
        Assert.That((box.Type, box.ItemUid, sent.Count), Is.EqualTo((0x11, 3L, 0)));
        Assert.That(box.Msg, Does.StartWith("Option\n"));
        box.Callback(1, box);
        Assert.That(sent, Is.Empty, "no");
        box.Callback(0, box);
        Assert.That(Bytes(sent.Single()), Is.EqualTo("01" + "FFFFFFFF" + "0300000000000000" + "01000000"));
        boxes.Clear(); sent.Clear();
        it.Rarity = 4;
        m.TryAddItem(it, 0);
        Assert.That((boxes.Count, sent.Count), Is.EqualTo((0, 1)), "below rarity 5 sends at once");
    }

    [Test]
    public void BlockedItemIsNotAdded()
    {
        m.BlockAdd = _ => true;
        sent.Clear();
        Assert.That(m.TryAddItem(Make(1), 0), Is.EqualTo(1));
        Assert.That((sent.Count, boxes.Count), Is.EqualTo((0, 0)));
    }

    [Test]
    public void RemoveNeedsABreakableItem()
    {
        var it = Make(1);
        Assert.That(m.RequestRemoveItem(it), Is.EqualTo(1));
        Assert.That(Bytes(sent.Single()), Is.EqualTo("020100000000000000"));
        it.LockState = 1; sent.Clear();
        Assert.That(m.RequestRemoveItem(it), Is.EqualTo(0));
        Assert.That((sent.Count, noticeIds.Single()), Is.EqualTo((0, (0x8f6, int.MinValue))));
    }

    [Test]
    public void CanAddItemIsBreakableAndNotListed()
    {
        var it = Make(1);
        Assert.That(m.CanAddItem(it), Is.True);
        m.OnPacket(new ItemDismantlePacket(1) { Slots = new[] { new DismantleSlot(0, 1, 1) } });
        Assert.That(m.CanAddItem(it), Is.False);
    }

    [Test]
    public void OptionsSendOnlyWithTheDialogIdle()
    {
        m.SendMode6(1, 4);
        Assert.That(sent, Is.Empty, "FUN_140532ab0 false");
        m.CanSendOptions = true;
        m.SendMode6(1, 4);
        Assert.That(Bytes(sent.Single()), Is.EqualTo("060104"));
        sent.Clear();
        Assert.That(m.SendMode3(), Is.True);
        Assert.That(Bytes(sent.Single()), Is.EqualTo("03"));
    }

    [Test]
    public void TryAddUsesCanBreakItemForBothTypes()
    {
        var locked = Make(1); locked.LockState = 1;
        Assert.That(m.TryAddItem(locked, 0), Is.EqualTo(0));
        Assert.That(noticeIds[^1], Is.EqualTo((0x8f6, int.MinValue)), "type 1: a locked item");
        m.SendMode0(2);
        Assert.That(m.TryAddItem(locked, 0), Is.EqualTo(0));
        Assert.That(noticeIds[^1], Is.EqualTo((0x8f7, int.MinValue)), "type 2: a locked item");
        sent.Clear();
        var plain = Make(3);           // not an equipment item, no skin: FUN_14053cbc0 || HasSkin fails
        Assert.That(m.TryAddItem(plain, 0), Is.EqualTo(0));
        Assert.That(noticeIds[^1], Is.EqualTo((0x8f7, int.MinValue)), "type 2: neither equipment nor skin");
        Assert.That(sent, Is.Empty);
        plain.Data.Type = 1;
        Assert.That(m.TryAddItem(plain, 0), Is.EqualTo(1));
    }

    [Test]
    public void TheFrameCheckAsksExistsNotFind()
    {
        Make(1); Make(2);
        m.OnPacket(new ItemDismantlePacket(6) { Slots = new[] { new DismantleSlot(0, 1, 1), new DismantleSlot(1, 2, 1) } });
        int finds = 0, exists = 0;
        m.Find = u => { finds++; return inv.GetValueOrDefault(u); };
        m.Exists = u => { exists++; return inv.ContainsKey(u); };
        for (int i = 0; i < 5; i++) m.RemoveMissingItems();
        _ = m.HasBreakItems;
        Assert.That(finds, Is.EqualTo(0), "Find builds an Item: not per frame");
        Assert.That(exists, Is.GreaterThan(0));
    }

    [Test]
    public void MissingItemsAreRemovedOnce()
    {
        Make(1); Make(2);
        m.OnPacket(new ItemDismantlePacket(6) { Slots = new[] { new DismantleSlot(0, 1, 1), new DismantleSlot(1, 2, 1) } });
        m.RemoveMissingItems();
        Assert.That(sent, Is.Empty);
        inv.Remove(2);
        m.RemoveMissingItems(); m.RemoveMissingItems();
        Assert.That(sent.Select(Bytes), Is.EqualTo(new[] { "020200000000000000" }), "once");
        m.OnPacket(new ItemDismantlePacket(2) { Uid = 2 });
        inv[2] = Make(2);
        m.OnPacket(new ItemDismantlePacket(1) { Slots = new[] { new DismantleSlot(1, 2, 1) } });
        inv.Remove(2);
        m.RemoveMissingItems();
        Assert.That(sent.Count, Is.EqualTo(2), "pending cleared by the mode 2 answer");
    }

    [Test]
    public void PacketsAreIgnoredWithoutTheDialog()
    {
        Make(1);
        m.DialogOpen = false;
        m.OnPacket(new ItemDismantlePacket(1) { Slots = new[] { new DismantleSlot(0, 1, 1) } });
        Assert.That(m.Contains(1), Is.False);
        m.DialogOpen = true;
        m.SendMode0(0);
        m.OnPacket(new ItemDismantlePacket(1) { Slots = new[] { new DismantleSlot(0, 1, 1) } });
        Assert.That(m.Contains(1), Is.False, "type 0");
    }

    [Test]
    public void ModeOneSetsTheSlotRefreshesAndPlaysTheSound()
    {
        Make(1);
        var changed = new List<int>(); var refreshed = new List<long>();
        m.SlotChanged += changed.Add; m.InventoryRefresh += refreshed.Add;
        m.OnPacket(new ItemDismantlePacket(1) { Slots = new[] { new DismantleSlot(100, 1, 1) } });
        Assert.That((m.Contains(1), changed.Count, refreshed.Count), Is.EqualTo((false, 0, 0)), "index out of range");
        m.OnPacket(new ItemDismantlePacket(1) { Slots = new[] { new DismantleSlot(99, 1, 5) } });
        Assert.That((m.Uid(99), m.Count(99), changed, refreshed, sounds), Is.EqualTo((1L, 5u, new List<int> { 99 }, new List<long> { 1 }, new List<string> { "Extract_ItemChoice" })));
        Assert.That(m.LastItemIndex, Is.EqualTo(99));
        m.OnPacket(new ItemDismantlePacket(1) { Slots = new[] { new DismantleSlot(3, 77, 1) } });
        Assert.That((m.Contains(77), refreshed[^1], sounds.Count), Is.EqualTo((false, 77L, 1)), "an unknown item: only the inventory refresh");
    }

    [Test]
    public void ModeTwoClearsTheSlot()
    {
        Make(1);
        m.OnPacket(new ItemDismantlePacket(1) { Slots = new[] { new DismantleSlot(2, 1, 1) } });
        var changed = new List<int>();
        m.SlotChanged += changed.Add;
        m.OnPacket(new ItemDismantlePacket(2) { Uid = 1 });
        Assert.That((m.Contains(1), changed), Is.EqualTo((false, new List<int> { 2 })));
        Assert.That(m.HasBreakItems, Is.False);
    }

    [Test]
    public void ModeThreeNeedsItsTypeThenClearsAndForwards()
    {
        Make(1);
        m.OnPacket(new ItemDismantlePacket(1) { Slots = new[] { new DismantleSlot(2, 1, 1) } });
        ItemDismantlePacket got = null; int refreshed = 0;
        m.ResultForwarded += p => got = p; m.RefreshActive += () => refreshed++;
        m.OnPacket(new ItemDismantlePacket(3) { Type = 2 });
        Assert.That((m.Contains(1), got), Is.EqualTo((true, (ItemDismantlePacket)null)), "another type");
        var rewards = new[] { new DismantleReward(5, 2) };
        m.OnPacket(new ItemDismantlePacket(3) { Type = 1, Rewards = rewards });
        Assert.That((m.Contains(1), got.Rewards, refreshed), Is.EqualTo((false, rewards, 1)));
    }

    [TestCase(1, 0x8f6, int.MinValue)]
    [TestCase(2, 0x8f7, int.MinValue)]
    [TestCase(3, 0x43, int.MinValue)]
    [TestCase(9, 0x8f8, 9)]
    public void ModeFourNotices(int result, int id, int arg)
    {
        m.OnPacket(new ItemDismantlePacket(4) { Result = result });
        Assert.That(noticeIds.Single(), Is.EqualTo((id, arg)));
    }

    [Test]
    public void ModeFourOtherAnswers()
    {
        int reset = 0, refresh = 0;
        m.ResetBreak += () => reset++; m.RefreshActive += () => refresh++;
        m.OnPacket(new ItemDismantlePacket(4) { Result = 4 });
        Assert.That((noticeIds.Count, noticeKeys.Count, reset, refresh), Is.EqualTo((0, 0, 0, 1)));
        m.OnPacket(new ItemDismantlePacket(4) { Result = 5 });
        Assert.That(noticeKeys, Is.EqualTo(new[] { "s_itembreak_empty" }));
        m.BreakInProgress = true;
        m.OnPacket(new ItemDismantlePacket(4) { Result = 4 });
        Assert.That((reset, refresh), Is.EqualTo((1, 3)));
    }

    [Test]
    public void ModeFiveForwardsThePreview()
    {
        ItemDismantlePacket got = null;
        m.PreviewReceived += p => got = p;
        var pv = new[] { new DismantlePreview(1, 2, 3) };
        m.OnPacket(new ItemDismantlePacket(5) { Previews = pv });
        Assert.That(got.Previews, Is.EqualTo(pv));
    }

    [Test]
    public void MaxRarityAndBreakItemsOnlyCountInventoryItems()
    {
        Make(1, rarity: 3); Make(2, rarity: 6);
        m.OnPacket(new ItemDismantlePacket(6) { Slots = new[] { new DismantleSlot(0, 1, 1), new DismantleSlot(5, 2, 1) } });
        Assert.That((m.MaxItemRarity, m.HasBreakItems, m.LastItemIndex), Is.EqualTo((6, true, 5)));
        inv.Remove(2);
        Assert.That((m.MaxItemRarity, m.LastItemIndex), Is.EqualTo((3, 5)));
        inv.Remove(1);
        Assert.That((m.MaxItemRarity, m.HasBreakItems), Is.EqualTo((0, false)));
    }
}
