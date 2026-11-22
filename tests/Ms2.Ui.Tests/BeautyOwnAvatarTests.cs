using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The own player as the beauty shop's avatar: the preview changes the live worn lists and tells the views.</summary>
public class BeautyOwnAvatarTests
{
    static int? Layer(int id) => 0;

    static MyCharacter Me(params (int id, long uid, EquipSlot slot)[] worn)
    {
        var me = new MyCharacter { User = new UserBlock { Skin = new SkinColor(new Rgba(1, 2, 3, 4), new Rgba(5, 6, 7, 8)) } };
        me.Equips = worn.Select(w => new EquipItem { Id = w.id, Uid = w.uid, Slot = w.slot }).ToList();
        me.Outfit2 = new List<EquipItem>();
        return me;
    }

    [Test]
    public void EquipReplacesTheSlotAndRedrawsTheViews()
    {
        var me = Me((100, 1, EquipSlot.HR), (200, 2, EquipSlot.CL));
        int changed = 0; me.Changed += () => changed++;
        var a = new BeautyOwnAvatar(me, Layer);
        a.Equip(new EquipItem { Id = 101, Uid = 9, Slot = EquipSlot.HR });
        Assert.That(me.Equips.Select(e => e.Uid), Is.EquivalentTo(new long[] { 2, 9 }));
        Assert.That(changed, Is.EqualTo(1));
        a.Unequip(me.Equips.First(e => e.Uid == 9));
        Assert.That(me.Equips.Select(e => e.Uid), Is.EqualTo(new long[] { 2 }));
        Assert.That(changed, Is.EqualTo(2));
    }

    [Test]
    public void AnItemWithoutAModelPresetIsNotWornAndRaisesNothing()
    {
        var me = Me((100, 1, EquipSlot.HR));
        int changed = 0; me.Changed += () => changed++;
        new BeautyOwnAvatar(me, _ => null).Equip(new EquipItem { Id = 101, Uid = 9, Slot = EquipSlot.HR });
        Assert.That(me.Equips.Select(e => e.Uid), Is.EqualTo(new long[] { 1 }));
        Assert.That(changed, Is.EqualTo(0));
    }

    [Test]
    public void OnlyTheItemsWornAtTheStartAreReal()
    {
        var me = Me((100, 1, EquipSlot.HR));
        var a = new BeautyOwnAvatar(me, Layer);
        a.Equip(new EquipItem { Id = 101, Uid = 9, Slot = EquipSlot.HR });
        Assert.That(a.IsRealItem(new EquipItem { Uid = 1 }), Is.True);
        Assert.That(a.IsRealItem(new EquipItem { Uid = 9 }), Is.False);
    }

    [Test]
    public void SkinColoursComeFromTheUserBlockAndAreSettable()
    {
        var a = new BeautyOwnAvatar(Me((100, 1, EquipSlot.HR)), Layer);
        Assert.That(a.GetSkinColor(0), Is.Not.EqualTo(a.GetSkinColor(1)));
        a.SetSkinColor(1, 77);
        Assert.That(a.GetSkinColor(1), Is.EqualTo(77));
    }

    [Test]
    public void ACustomizeOverTheOwnPlayerPreviewsAndRestores()
    {
        var me = Me((100, 1, EquipSlot.HR), (200, 2, EquipSlot.CL));
        var avatar = new BeautyOwnAvatar(me, Layer);
        var c = new BeautyCustomize(avatar);
        avatar.Equip(new EquipItem { Id = 101, Uid = 9, Slot = EquipSlot.HR });
        Assert.That(c.GetAdded().ContainsKey(9), Is.True);
        Assert.That(c.GetRemoved().ContainsKey(1), Is.True);
        c.RestoreAll();
        Assert.That(me.Equips.Select(e => e.Uid), Is.EquivalentTo(new long[] { 1, 2 }));
    }
}
