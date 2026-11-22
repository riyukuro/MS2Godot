using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Maple2.PacketLib.Tools;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The item colour dialog's own-avatar Customize: ApplyPalette, SetColors, GetModifiedAndSelect (customize.cpp:441, :460, :1253).</summary>
public class ItemColoringOwnCustomizeTests
{
    sealed class Avatar : IBeautyAvatar
    {
        public readonly List<EquipItem> Items = new();
        public IReadOnlyList<EquipItem> Worn => Items;
        public EquipItem Clone(EquipItem i) => i.Clone();
        public void Equip(EquipItem i) { Items.RemoveAll(w => w.Slot == i.Slot || w.Uid == i.Uid); Items.Add(i); }
        public void Unequip(EquipItem i) => Items.RemoveAll(w => w.Uid == i.Uid);
        public bool IsRealItem(EquipItem i) => true;
        public int GetSkinColor(int n) => 0;
        public void SetSkinColor(int n, int c) { }
        public void SetSlotHidden(EquipSlot s, bool h) { }
    }

    const string Xml = @"<ms2>
  <colorPalette id='7'>
    <color colorSN='11' ch0='0x112233' ch1='0x445566' ch2='0x778899' ch0_eye='0xaabbcc' ch2_eye='0xddeeff'/>
    <color colorSN='12' ch0='0x010203' ch1='0x040506' ch2='0x070809'/>
  </colorPalette>
  <colorPalette id='100000000'>
    <color colorSN='21' ch0='0x112233' ch1='0x445566' ch2='0x778899' ch0_eye='0xaabbcc' ch2_eye='0xddeeff'/>
  </colorPalette>
</ms2>";

    static ColorPaletteTable Table() => ColorPaletteTable.Parse(XDocument.Parse(Xml).Root);
    static int Hex(int v) => v;

    static (ItemColoringOwnCustomize c, Avatar a) Rig(bool eye = false)
    {
        var a = new Avatar();
        a.Items.Add(new EquipItem { Id = 5000, Uid = 1, Slot = (EquipSlot)3, Appearance = new float[4] });
        var b = new BeautyCustomize(a);
        var c = new ItemColoringOwnCustomize(b, Table(), id => eye);
        c.SelectUid(1);
        return (c, a);
    }

    static Rgba Rgb(int v) => new((byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24));
    static byte[] Extra(BeautyColorItem it) { var w = new ByteWriter(); it.WriteExtra(w); return w.ToArray(); }

    [Test]
    public void ApplyPaletteDyesTheWornItemFromTheRowOfTheSerialNumber()
    {
        var (c, a) = Rig();
        c.ApplyPalette(7, 12);
        var col = a.Items[0].Color;
        Assert.That(col, Is.EqualTo(new EquipColor(Rgb(0x010203), Rgb(0x040506), Rgb(0x070809), 12, 7)));
        Assert.That(c.IsEditItemColorChanged, Is.True);
        Assert.That(c.HasChanges, Is.True);
        Assert.That(c.TryGetEditSlotColor(out uint pal, out uint sn), Is.True);
        Assert.That((pal, sn), Is.EqualTo((7u, 12u)));
    }

    [Test]
    public void TheEyeValuesReplaceSlotsZeroAndTwoOnlyForAnEyeItemOfTheHairPalette()
    {
        var (c, a) = Rig(eye: true);
        c.ApplyPalette(100000000, 21);
        Assert.That(a.Items[0].Color, Is.EqualTo(new EquipColor(Rgb(0xaabbcc), Rgb(0x445566), Rgb(0xddeeff), 21, 100000000)));
        c.ApplyPalette(7, 11);
        Assert.That(a.Items[0].Color.Primary, Is.EqualTo(Rgb(0x112233)), "another palette keeps the plain values");
        var (n, b) = Rig(eye: false);
        n.ApplyPalette(100000000, 21);
        Assert.That(b.Items[0].Color.Primary, Is.EqualTo(Rgb(0x112233)), "not an eye item");
    }

    [Test]
    public void SetColorsChangesOneComponentAndClearsTheSerialNumberAndPalette()
    {
        var (c, a) = Rig();
        c.ApplyPalette(7, 12);
        c.SetColors(0x0a0b0c, 2);
        var col = a.Items[0].Color;
        Assert.That(col, Is.EqualTo(new EquipColor(Rgb(0x010203), Rgb(0x040506), Rgb(0x0a0b0c), -1, 0)));
    }

    [TestCase(3)]
    [TestCase(-1)]
    public void ASlotOutsideZeroToTwoChangesNothing(int slot)
    {
        var (c, a) = Rig();
        c.SetColors(0x0a0b0c, slot);
        Assert.That(a.Items[0].Color, Is.EqualTo(default(EquipColor)));
        Assert.That(c.HasChanges, Is.False);
    }

    [Test]
    public void AnUnknownPaletteOrSerialNumberOrPaletteZeroChangesNothing()
    {
        var (c, a) = Rig();
        c.ApplyPalette(7, 999);
        c.ApplyPalette(999, 11);
        c.ApplyPalette(0, 11);
        Assert.That(a.Items[0].Color, Is.EqualTo(default(EquipColor)));
        Assert.That(c.HasChanges, Is.False);
    }

    [Test]
    public void NothingSelectedChangesNothing()
    {
        var a = new Avatar();
        a.Items.Add(new EquipItem { Id = 5000, Uid = 1, Slot = (EquipSlot)3 });
        var c = new ItemColoringOwnCustomize(new BeautyCustomize(a), Table(), _ => false);
        c.ApplyPalette(7, 12);
        c.SetColors(1, 0);
        Assert.That(a.Items[0].Color, Is.EqualTo(default(EquipColor)));
        Assert.That(c.HasEditItem, Is.False);
    }

    [Test]
    public void GetModifiedAndSelectGivesTheDyedItemWithItsColourAsExtraDataAndNullWhenUnchanged()
    {
        var (c, a) = Rig();
        Assert.That(c.GetModifiedAndSelect(1), Is.Null, "nothing modified");
        Assert.That(c.EditItemUid, Is.EqualTo(1), "the uid is selected again");
        c.ApplyPalette(7, 12);
        var it = c.GetModifiedAndSelect(1);
        Assert.That(it, Is.Not.Null);
        Assert.That((it.Uid, it.ItemId), Is.EqualTo((1L, 5000)));
        var w = new ByteWriter(); w.WriteEquipColor(a.Items[0].Color);
        foreach (float f in a.Items[0].Appearance) w.WriteFloat(f);
        Assert.That(Extra(it), Is.EqualTo(w.ToArray()));
        Assert.That(Extra(it).Length, Is.EqualTo(20 + 4 * 4), "the colour and the decal's 4 floats");
        Assert.That(c.GetModifiedAndSelect(2), Is.Null, "not worn");
    }

    /// <summary>The extra data put into an item body in place of its colour block, then read with the item reader.</summary>
    static EquipItem ReadBack(EquipSlot slot, byte[] extra)
    {
        var empty = new ByteWriter(); ItemReader.WriteEmptyBody(empty, 1);
        byte[] e = empty.ToArray();
        const int headerLen = 4 + 4 + 8 + 8 + 8 + 4 + 4 + 1 + 8 + 2 + 1 + 4, colorLen = 20;
        var body = new ByteWriter();
        body.WriteBytes(e[..headerLen]); body.WriteBytes(extra); body.WriteBytes(e[(headerLen + colorLen)..]);
        var r = new ByteReader(body.ToArray(), 0);
        var it = new EquipItem { Id = 5000, Uid = 1, Slot = slot };
        ItemReader.ReadBodyInto(r, it);
        r.ExpectEnd("body");
        return it;
    }

    [TestCase(EquipSlot.HR, 14)]
    [TestCase(EquipSlot.FD, 4)]
    [TestCase(EquipSlot.CP, 13)]
    public void TheExtraDataCarriesTheColourAndTheSlotsFloatsAndTheItemReaderReadsThemBack(EquipSlot slot, int floats)
    {
        var a = new Avatar();
        var app = Enumerable.Range(0, floats).Select(i => 0.5f + i).ToArray();
        a.Items.Add(new EquipItem { Id = 5000, Uid = 1, Slot = slot, Appearance = app });
        var c = new ItemColoringOwnCustomize(new BeautyCustomize(a), Table(), _ => false);
        c.SelectUid(1);
        c.ApplyPalette(7, 12);
        var it = c.GetModifiedAndSelect(1);
        Assert.That(it, Is.Not.Null);
        byte[] extra = Extra(it);
        Assert.That(extra.Length, Is.EqualTo(20 + 4 * floats));
        var back = ReadBack(slot, extra);
        Assert.That(back.Color, Is.EqualTo(a.Items[0].Color));
        Assert.That(back.Appearance, Is.EqualTo(app));
    }

    [Test]
    public void AnItemWhoseFloatsAreNotTheSlotsCountIsNotSent()
    {
        var a = new Avatar();
        a.Items.Add(new EquipItem { Id = 5000, Uid = 1, Slot = EquipSlot.HR, Appearance = new float[3] });
        var c = new ItemColoringOwnCustomize(new BeautyCustomize(a), Table(), _ => false);
        c.SelectUid(1);
        c.ApplyPalette(7, 12);
        Assert.That(c.GetModifiedAndSelect(1), Is.Null);
    }

    [Test]
    public void RestoreAllPutsTheCopyBack()
    {
        var (c, a) = Rig();
        c.ApplyPalette(7, 12);
        c.RestoreAll();
        Assert.That(a.Items[0].Color, Is.EqualTo(default(EquipColor)));
        Assert.That(c.HasChanges, Is.False);
    }
}
