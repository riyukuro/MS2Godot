using System.Xml.Linq;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>ItemInstallData::Load (itemdatastruct.cpp:1290-1317): attachable as an int, staticAngle as a float.</summary>
public class ItemInstallDataTests
{
    [Test]
    public void AttachableAndStaticAngleAreRead()
    {
        var d = ItemData.Load(50400001, XElement.Parse("<e><install attachable=\"1\" staticAngle=\"-90.5\"/></e>"));
        Assert.That(d.InstallAttachable, Is.EqualTo(1));
        Assert.That(d.InstallStaticAngle, Is.EqualTo(-90.5f));
    }

    [Test]
    public void MissingOrUnreadableAttributesStayZero()
    {
        var d = ItemData.Load(50400001, XElement.Parse("<e><install staticAngle=\"x\"/></e>"));
        Assert.That(d.InstallAttachable, Is.EqualTo(0));
        Assert.That(d.InstallStaticAngle, Is.EqualTo(0f));
        var none = ItemData.Load(50400001, XElement.Parse("<e/>"));
        Assert.That(none.InstallAttachable, Is.EqualTo(0));
    }

    [Test]
    public void AssetAndPresetAreRead()
    {
        // the lift item 30000856's install (both data sets): asset a NIF path, preset empty
        var d = ItemData.Load(30000856, XElement.Parse("<e><install asset=\"Data/Resource/Model/Npc/11/11003300/11003300_Lance.nif\" preset=\"\"/></e>"));
        Assert.That(d.InstallAsset, Is.EqualTo("Data/Resource/Model/Npc/11/11003300/11003300_Lance.nif"));
        Assert.That(d.InstallPreset, Is.Empty);
        Assert.That(ItemData.Load(1, XElement.Parse("<e/>")).InstallAsset, Is.Null);
    }

    [Test]
    public void AModelPathSplitsIntoItsPackAndEntry()
    {
        Assert.That(ItemData.ModelPackEntry("Data/Resource/Model/Npc/11/11003300/11003300_Lance.nif"),
            Is.EqualTo(("Resource/Model/Npc", "11/11003300/11003300_Lance.nif")));
        Assert.That(ItemData.ModelPackEntry("data\\resource\\model\\Map\\Common\\Field\\co_fi_cubric_rock_A01.nif"),
            Is.EqualTo(("Resource/Model/Map", "Common/Field/co_fi_cubric_rock_A01.nif")));
        Assert.That(ItemData.ModelPackEntry("Resource/Model/Item/x.nif"), Is.EqualTo(("Resource/Model/Item", "x.nif")));
        Assert.That(ItemData.ModelPackEntry("Data/Resource/Image/x.png"), Is.Null);
        Assert.That(ItemData.ModelPackEntry("Data/Resource/Model/Npc"), Is.Null);
        Assert.That(ItemData.ModelPackEntry(""), Is.Null);
    }
}
