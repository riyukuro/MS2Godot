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
}
