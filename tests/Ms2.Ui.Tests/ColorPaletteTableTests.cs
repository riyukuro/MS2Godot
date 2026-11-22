using System.Xml.Linq;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CItemDataTable::LoadColorPalette (itemdatatable.cpp:1134), AddColor (:303), GetPaletteColors (:820).</summary>
public class ColorPaletteTableTests
{
    const string Xml = @"<ms2>
  <colorPalette id='5'>
    <color colorSN='3' ch0='0x11' ch0_eye='0xaa' ch2_eye='0xbb'/>
    <color ch0='0x22'/>
    <color colorSN='3' ch0='0x33'/>
    <color colorSN='1' ch0='ff00zz' ch1='zz' ch2='0xFFFFFFFFF'/>
  </colorPalette>
</ms2>";

    static ColorPaletteTable Table() => ColorPaletteTable.Parse(XDocument.Parse(Xml).Root);

    [Test]
    public void RowsAreTheFileOrderWithARepeatedSerialNumberKept()
    {
        var rows = Table().Rows(5);
        Assert.That(rows.Select(r => r.Ch0), Is.EqualTo(new uint[] { 0x11, 0x22, 0x33, 0xff00 }));
        Assert.That(Table().Rows(6), Is.Empty);
    }

    [Test]
    public void BySNKeepsTheLaterRowOfARepeatedSerialNumber()
        => Assert.That(Table().BySN(5, 3).Ch0, Is.EqualTo(0x33u));

    [Test]
    public void ARowWithoutColorSNIsMinusOne()
    {
        Assert.That(Table().Rows(5)[1].ColorSN, Is.EqualTo(-1));
        Assert.That(Table().BySN(5, -1).Ch0, Is.EqualTo(0x22u));
    }

    [Test]
    public void EyeColoursAreRead()
    {
        var c = Table().BySN(5, 1);
        Assert.That((Table().Rows(5)[0].Eye0, Table().Rows(5)[0].Eye2), Is.EqualTo((0xaau, 0xbbu)));
        Assert.That(c.Eye0, Is.EqualTo(0u));
    }

    [Test]
    public void HexReadsTheLeadingDigitsAsStrtoulDoes()
    {
        var r = Table().BySN(5, 1);
        Assert.That(r.Ch0, Is.EqualTo(0xff00u), "ff00zz");
        Assert.That(r.Ch1, Is.EqualTo(0u), "no digits");
        Assert.That(r.Ch2, Is.EqualTo(uint.MaxValue), "too many digits clamp");
    }
}
