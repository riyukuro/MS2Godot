using System.Xml.Linq;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The colour head an item is dyed by (ItemModelHead::Load, itemdatastruct.cpp:930-950): the preset model's &lt;customize&gt;, or,
/// where the preset names no palette (the GMS2 itempreset files), the item file's own. No data needed.
/// </summary>
public class ColourHeadTests
{
    // GMS2 item 12200398: its own customize carries the head; its preset 12200067 has colour flags only
    const string Gms2Item = @"<environment><customize colorPalette=""10"" color=""1"" colordetail=""1"" ch0=""1"" ch1=""0"" ch2=""1"" defaultColorIndex=""13"" />
        <tool itemPreset=""12200067"" /><property type=""1"" /></environment>";
    const string Gms2Preset = @"<ItemModel id=""12200067""><customize color=""1"" colordetail=""1"" ch0=""1"" ch2=""1"" /><slots><slot name=""CL"" /></slots></ItemModel>";
    const string KmsModel = @"<ItemModel id=""12200398""><customize color=""1"" colorPalette=""10"" defaultColorIndex=""13"" /><slots><slot name=""CL"" /></slots></ItemModel>";

    [Test]
    public void TheItemFilesOwnCustomizeIsReadAsAColourHead()
    {
        var d = ItemData.Load(12200398, XElement.Parse(Gms2Item));
        Assert.That(d.OwnColourHead, Is.Not.Null);
        var h = d.OwnColourHead;
        Assert.That((h.ColorEnabled, h.Palette, h.DefaultColor(0), h.MultiColor), Is.EqualTo((true, 10, 13, false)));
        // a KMS item row has no customize; a customize without a palette is no head
        Assert.That(ItemData.Load(1, XElement.Parse("<environment><tool itemPreset=\"2\" /></environment>")).OwnColourHead, Is.Null);
        Assert.That(ItemData.Load(1, XElement.Parse("<environment><customize color=\"1\" /></environment>")).OwnColourHead, Is.Null);
    }

    [Test]
    public void HeadOfIsTheClientsRead()
    {
        var off = ItemModelTable.HeadOf(XElement.Parse(@"<customize color=""1"" />"), new[] { 3 });
        Assert.That((off.ColorEnabled, off.Palette, off.DefaultColor(0), off.FirstSlotType), Is.EqualTo((false, 0, -1, 3)), "no palette clears color");
        var multi = ItemModelTable.HeadOf(XElement.Parse(@"<customize color=""1"" colorPalette=""2"" defaultColorIndex=""4,5"" />"), Array.Empty<int>());
        Assert.That((multi.ColorEnabled, multi.MultiColor, multi.DefaultColor(1)), Is.EqualTo((true, true, 5)));
        Assert.That(ItemModelTable.HeadOf(null, Array.Empty<int>()).Palette, Is.Zero);
    }

    [Test]
    public void ThePresetsHeadWinsWhereItNamesAPalette()
    {
        var item = XElement.Parse(Gms2Item);
        bool asked = false;
        XElement Item() { asked = true; return item; }
        var kms = XElement.Parse(KmsModel);
        Assert.That(ItemFiles.ColourHead(kms, Item), Is.SameAs(kms.Element("customize")));
        Assert.That(asked, Is.False, "the item row is not read when the model names a palette");
        var preset = XElement.Parse(Gms2Preset);
        Assert.That(ItemFiles.ColourHead(preset, Item), Is.SameAs(item.Element("customize")));
        Assert.That(asked, Is.True);
        // neither names one: the model's (no dye)
        Assert.That(ItemFiles.ColourHead(preset, () => XElement.Parse("<environment />")), Is.SameAs(preset.Element("customize")));
        Assert.That(ItemFiles.ColourHead(null, () => null), Is.Null);
    }
}
