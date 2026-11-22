using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The own-avatar Customize state (customize.cpp CloneItems / EditItems / Equipment) on a fake worn list.</summary>
public class BeautyCustomizeTests
{
    sealed class Avatar : IBeautyAvatar
    {
        public readonly List<EquipItem> Items = new();
        public readonly HashSet<long> Real = new();
        public int[] Skin = { 10, 20 };
        public readonly HashSet<EquipSlot> Hidden = new();
        public IReadOnlyList<EquipItem> Worn => Items;
        public EquipItem Clone(EquipItem i) => new EquipItem { Id = i.Id, Uid = i.Uid, Slot = i.Slot, Color = i.Color, Appearance = (float[])i.Appearance.Clone() };
        public void Equip(EquipItem i) { Items.RemoveAll(w => w.Slot == i.Slot || w.Uid == i.Uid); Items.Add(i); }
        public void Unequip(EquipItem i) => Items.RemoveAll(w => w.Uid == i.Uid);
        public bool IsRealItem(EquipItem i) => Real.Contains(i.Uid);
        public int GetSkinColor(int n) => Skin[n];
        public void SetSkinColor(int n, int c) => Skin[n] = c;
        public void SetSlotHidden(EquipSlot s, bool h) { if (h) Hidden.Add(s); else Hidden.Remove(s); }
    }

    static EquipColor Dye(byte r) => new(new Rgba(0, 0, r, 255), default, default, 0, 0);

    static Avatar Worn(params (int id, long uid, EquipSlot slot)[] items)
    {
        var a = new Avatar();
        foreach (var (id, uid, slot) in items) { a.Items.Add(new EquipItem { Id = id, Uid = uid, Slot = slot }); a.Real.Add(uid); }
        return a;
    }

    [Test]
    public void InitCopiesTheWornItemsAndNothingIsChanged()
    {
        var a = Worn((100, 1, EquipSlot.HR), (200, 2, EquipSlot.CL));
        var c = new BeautyCustomize(a);
        Assert.That(c.GetAdded(), Is.Empty);
        Assert.That(c.GetModified(), Is.Empty);
        Assert.That(c.GetRemoved(), Is.Empty);
        Assert.That(c.IsColorChanged(), Is.False);
        Assert.That(c.HasChanges, Is.False);
    }

    [Test]
    public void APreviewItemIsAddedAndAnotherOneInItsSlotIsRemoved()
    {
        var a = Worn((100, 1, EquipSlot.HR));
        var c = new BeautyCustomize(a);
        var preview = new EquipItem { Id = 101, Uid = 50, Slot = EquipSlot.HR };
        a.Equip(preview);
        Assert.That(c.GetAdded().Keys, Is.EquivalentTo(new long[] { 50 }));
        Assert.That(c.GetRemoved().Keys, Is.EquivalentTo(new long[] { 1 }));
        Assert.That(c.IsAddedUid(50), Is.True);
        Assert.That(c.IsRemovedUid(1), Is.True);
        Assert.That(c.IsAddedSlot((int)EquipSlot.HR), Is.True);
        c.SelectUid(50);
        Assert.That(c.EditItemId, Is.EqualTo(101));
        Assert.That(c.IsEditItemAdded, Is.True);
        Assert.That(c.HasChanges, Is.True);
    }

    [Test]
    public void ADyedWornItemIsModifiedAndItsFlagIsTheColourBit()
    {
        var a = Worn((100, 1, EquipSlot.HR));
        var c = new BeautyCustomize(a);
        a.Items[0].Color = Dye(9);
        Assert.That(c.GetModified().Keys, Is.EquivalentTo(new long[] { 1 }));
        Assert.That(c.GetChangeFlags(1), Is.EqualTo(1));
        c.SelectUid(1);
        Assert.That(c.IsEditItemColorChanged, Is.True);
        Assert.That(c.IsEditItemAppearanceChanged, Is.False);
        Assert.That(c.IsSameUid(1, out int flags), Is.False);
        Assert.That(flags, Is.EqualTo(1));
    }

    [Test]
    public void AnAppearanceChangeIsFlagTwo()
    {
        var a = Worn((100, 1, EquipSlot.HR));
        a.Items[0].Appearance = new[] { 1f, 2f };
        var c = new BeautyCustomize(a);
        a.Items[0].Appearance = new[] { 1f, 3f };
        Assert.That(c.GetChangeFlags(1), Is.EqualTo(2));
        Assert.That(c.IsModifiedSlot((int)EquipSlot.HR), Is.True);
    }

    [Test]
    public void TheSkinColourChangeIsKeptAndSlotZeroRestoresIt()
    {
        var a = Worn((100, 1, EquipSlot.HR));
        var c = new BeautyCustomize(a);
        a.Skin[1] = 77;
        Assert.That(c.IsColorChanged(), Is.True);
        Assert.That(c.HasChanges, Is.True);
        ((IBeautyCustomize)c).RestoreSlot(0);
        Assert.That(a.Skin, Is.EqualTo(new[] { 10, 20 }));
        Assert.That(c.IsColorChanged(), Is.False);
    }

    [Test]
    public void RestoreSlotPutsTheCopyBackOrTakesThePreviewOff()
    {
        var a = Worn((100, 1, EquipSlot.HR));
        var c = new BeautyCustomize(a);
        a.Equip(new EquipItem { Id = 101, Uid = 50, Slot = EquipSlot.HR });
        a.Equip(new EquipItem { Id = 300, Uid = 60, Slot = EquipSlot.SH });
        c.RestoreSlot((int)EquipSlot.HR);
        Assert.That(a.Items.Single(i => i.Slot == EquipSlot.HR).Uid, Is.EqualTo(1));
        c.RestoreSlot((int)EquipSlot.SH);
        Assert.That(a.Items.Any(i => i.Slot == EquipSlot.SH), Is.False);
    }

    [Test]
    public void RestoreAllTakesThePreviewsOffAndReclones()
    {
        var a = Worn((100, 1, EquipSlot.HR));
        var c = new BeautyCustomize(a);
        a.Equip(new EquipItem { Id = 101, Uid = 50, Slot = EquipSlot.HR });
        a.Skin[0] = 5;
        c.RestoreAll();
        Assert.That(a.Items.Select(i => i.Uid), Is.EquivalentTo(new long[] { 1 }));
        Assert.That(a.Skin, Is.EqualTo(new[] { 10, 20 }));
        Assert.That(c.GetAdded(), Is.Empty);
    }

    [Test]
    public void CommitSlotSendsTheColoursThenTheAddedAndTheModifiedOfThatSlotOnly()
    {
        var a = Worn((100, 1, EquipSlot.HR), (200, 2, EquipSlot.CL));
        var c = new BeautyCustomize(a);
        var log = new List<string>();
        c.SendColors = (x, y) => log.Add($"colors {x} {y}");
        c.SendAdded = i => log.Add($"added {i.Uid}");
        c.SendModified = i => log.Add($"modified {i.Uid}");
        a.Skin[0] = 11;
        a.Equip(new EquipItem { Id = 101, Uid = 50, Slot = EquipSlot.HR });
        a.Items.Single(i => i.Slot == EquipSlot.CL).Color = Dye(4);
        c.CommitSlot((int)EquipSlot.HR);
        Assert.That(log, Is.EqualTo(new[] { "colors 11 20", "added 50" }));
        Assert.That(c.EditItemUid, Is.EqualTo(50), "the slot is selected again");
    }

    [Test]
    public void CommitAllSendsEveryChange()
    {
        var a = Worn((100, 1, EquipSlot.HR), (200, 2, EquipSlot.CL));
        var c = new BeautyCustomize(a);
        var log = new List<string>();
        c.SendAdded = i => log.Add($"added {i.Uid}");
        c.SendModified = i => log.Add($"modified {i.Uid}");
        a.Equip(new EquipItem { Id = 101, Uid = 50, Slot = EquipSlot.HR });
        a.Items.Single(i => i.Slot == EquipSlot.CL).Color = Dye(4);
        c.CommitAll();
        Assert.That(log, Is.EquivalentTo(new[] { "added 50", "modified 2" }));
    }

    [Test]
    public void ATwoSlotItemOnNinePutsItsPairBackOn()
    {
        var a = Worn((900, 1, EquipSlot.CL));       // a suit on slot 8
        var c = new BeautyCustomize(a) { IsTopBottom = i => i.Id == 900 };
        a.Items.Clear();                              // the preview put something on 9 and took the suit off
        a.Equip(new EquipItem { Id = 901, Uid = 50, Slot = EquipSlot.PA });
        c.Apply(new EquipItem { Id = 901, Uid = 50, Slot = EquipSlot.PA }, (int)EquipSlot.PA);
        Assert.That(a.Items.Any(i => i.Uid == 1), Is.True, "the pair's copy is equipped again");
    }

    [Test]
    public void CreateItemCopiesTheSavedItemOrAsksTheHost()
    {
        var a = Worn((100, 1, EquipSlot.HR));
        var c = new BeautyCustomize(a) { NewItem = id => new EquipItem { Id = id, Slot = EquipSlot.CL }, NextUid = () => 77 };
        Assert.That(c.CreateItem(100).Uid, Is.EqualTo(1));
        var n = c.CreateItem(555);
        Assert.That((n.Id, n.Uid), Is.EqualTo((555, 77L)));
        Assert.That(new BeautyCustomize(a).CreateItem(555), Is.Null);
    }

    [Test]
    public void SelectIdTakesTheSlotOfTheIdAndAnAvatarColourItemHasNoItem()
    {
        var a = Worn((100, 1, EquipSlot.HR));
        var c = new BeautyCustomize(a) { SlotOfId = id => EquipSlot.HR, IsAvatarColorId = id => id == 9 };
        c.SelectId(123);
        Assert.That(c.EditItemUid, Is.EqualTo(1));
        c.SelectId(9);
        Assert.That(c.EditItemUid, Is.EqualTo(0));
        Assert.That(c.EditItemId, Is.EqualTo(9));
    }

    [Test]
    public void HiddenSlotsComeBackAfterACommit()
    {
        var a = Worn((100, 1, EquipSlot.HR));
        var c = new BeautyCustomize(a);
        c.HideSlot(EquipSlot.CP);
        a.Hidden.Clear();
        c.CommitSlot((int)EquipSlot.HR);
        Assert.That(a.Hidden, Is.EquivalentTo(new[] { EquipSlot.CP }));
        c.ShowSlot(EquipSlot.CP);
        Assert.That(a.Hidden, Is.Empty);
    }
}
