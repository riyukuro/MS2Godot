using System.Xml.Linq;
using Ms2.Formats.Options;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// The status option GMS2 adds: the GMS2 option movie has the m_displayHideOtherPet box (the KMS movie does not), and the
/// GMS2 client's own Config/GameOption.xml saves DisplayHideOtherPet with the other status switches
/// (gameoptionstruct.cpp:1371-1430 has none of it).
/// </summary>
public class RcGameOptionTests
{
    [Test]
    public void TheStatusOptionSavesAndLoadsTheHideOtherPetSwitch()
    {
        var status = new GameOption.Status(regionKr: false);
        RcLane.Set(status, "DisplayHideOtherPet", true);
        var doc = XDocument.Parse("<ms2/>");
        status.Save(doc);
        Assert.That((string)doc.Root.Element("status")?.Attribute("DisplayHideOtherPet"), Is.EqualTo("true"));
        var back = new GameOption.Status(regionKr: false);
        back.Load(doc);
        Assert.That(RcLane.Member<bool>(back, "DisplayHideOtherPet"), Is.True);
        RcLane.Set(back, "DisplayHideOtherPet", false);
        var again = XDocument.Parse("<ms2/>");
        back.Save(again);
        Assert.That((string)again.Root.Element("status")?.Attribute("DisplayHideOtherPet"), Is.EqualTo("false"));
        // where the GMS2 client's own GameOption.xml has it: after DisplayHideMyPet, before DisplayHideSmartPush
        var names = again.Root.Element("status").Attributes().Select(a => a.Name.LocalName).ToList();
        Assert.That(names.IndexOf("DisplayHideOtherPet"), Is.EqualTo(names.IndexOf("DisplayHideMyPet") + 1));
        Assert.That(names.IndexOf("DisplayHideSmartPush"), Is.EqualTo(names.IndexOf("DisplayHideOtherPet") + 1));
    }

    /// <summary>The KMS exe's status save has no DisplayHideOtherPet (gameoptionstruct.cpp:1405-1430), so a KR status
    /// leaves it out; it still loads one (off after a reset).</summary>
    [Test]
    public void AKrStatusDoesNotSaveTheHideOtherPetSwitch()
    {
        var status = new GameOption.Status(regionKr: true);
        Assert.That(status.DisplayHideOtherPet, Is.False);
        status.DisplayHideOtherPet = true;
        var doc = XDocument.Parse("<ms2/>");
        status.Save(doc);
        Assert.That(doc.Root.Element("status")?.Attribute("DisplayHideOtherPet"), Is.Null);
        Assert.That(doc.Root.Element("status")?.Attribute("DisplayHideMyPet"), Is.Not.Null);
        var back = new GameOption.Status(regionKr: true);
        back.Load(XDocument.Parse("<ms2><status DisplayHideOtherPet=\"true\"/></ms2>"));
        Assert.That(back.DisplayHideOtherPet, Is.True);
    }
}
