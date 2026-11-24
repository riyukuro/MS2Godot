using System.Xml.Linq;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>The GMS2 data's layout of the drop table (dropitem rows by name, asset rows in the items' xmlData) read as the KMS table.</summary>
public class ItemDropTableGms2Tests {
    static readonly XElement Root = XElement.Parse("""
        <ms2>
          <gradeEffect id="2" effect="Item/Eff_DropEffect_Rare.xml" />
          <gradeNif id="1" nif="Item_Box_A01" />
          <gradePetNif id="1" nif="Eff_PetDrop_normal" />
          <dropitem name="Default" type="1" scale="1.0" bias="40" dropani="1,0,0" dropanispeed="5" />
          <dropitem name="equip_ItemBox" type="2" scale="1.0" bias="55.0" dropani="0,0,1" dropanispeed="3" bounceheight="30" />
          <dropitem name="equip_Money" type="6" scale="1.0" bias="30" beanispeed="1.8" bounceheight="0" />
          <dropitem name="equip_Bill" type="9" scale="1.0" bias="10.0" bounceheight="40" />
          <dropitem name="equip_ExpSphere1" type="14" bias="50" />
          <dropitem name="equip_ExpSphere2" type="15" bias="50" />
        </ms2>
        """);

    static XElement Item(string xmlData) => new("ms2", new XElement("environment", new XElement("property", new XAttribute("xmlData", xmlData))));

    static ItemDropTable Table() => new(Root, id => id switch {
        90000001 => Item("<v asset=\"90000002_CopperCoin\" displayType=\"equip_Money\" min=\"1\" />\n<v asset=\"90000001_Coin\" displayType=\"equip_Money\" min=\"20\" />\n<v asset=\"90000003_Bill\" displayType=\"equip_Bill\" min=\"100\" />"),
        90000008 => Item("<v asset=\"90000008_expBall_01\" displayType=\"equip_ExpSphere1\" minRate=\"0\" />\n<v asset=\"90000008_expBall_02\" displayType=\"equip_ExpSphere2\" minRate=\"0.25\" />"),
        _ => null,
    });

    [Test] public void AnOrdinaryItemTakesTheItemBoxRowAndTheGradeNif() {
        var look = Table().Look(20000001, 1, 1, false);
        Assert.That(look, Is.Not.Null, "styles 1 and 2 are the equip_ItemBox row");
        Assert.That((look.Asset, look.Style.RestHeight, look.Style.DropAni[2], look.Style.BounceHeight), Is.EqualTo(("Item_Box_A01", 55f, 1f, 30f)));
        Assert.That(Table().Look(20000001, 1, 1, true)?.Asset, Is.EqualTo("Eff_PetDrop_normal"));
    }

    [Test] public void TheMesoPilesComeFromTheItemsXmlData() {
        var t = Table();
        Assert.That(t.Look(90000001, 5, 0, false)?.Asset, Is.EqualTo("90000002_CopperCoin"));
        Assert.That(t.Look(90000002, 20, 0, false)?.Asset, Is.EqualTo("90000001_Coin"), "every meso id reads 90000001's rows");
        var bill = t.Look(90000003, 150, 0, false);
        Assert.That((bill?.Asset, bill?.Style.RestHeight, bill?.Style.BounceHeight, bill?.IsMeso), Is.EqualTo(("90000003_Bill", 10f, 40f, true)));
    }

    [Test] public void AMinRateIsTheKmsMinOverTenThousand() {
        var t = Table();
        Assert.That(t.Look(90000008, 2499, 0, false)?.Asset, Is.EqualTo("90000008_expBall_01"));
        Assert.That(t.Look(90000008, 2500, 0, false)?.Asset, Is.EqualTo("90000008_expBall_02"));
    }

    [Test] public void TheGradeTablesReadAsInKms() {
        var t = Table();
        Assert.That((t.GradeEffect(2), t.GradeNif(1), t.GradePetNif(1)), Is.EqualTo(("Item/Eff_DropEffect_Rare.xml", "Item_Box_A01", "Eff_PetDrop_normal")));
        Assert.That(t.Look(90000009, 1, 0, false), Is.Null, "a special item with no xmlData rows makes no drop");
    }
}
