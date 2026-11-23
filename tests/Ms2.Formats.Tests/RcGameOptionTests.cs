using System.Xml.Linq;
using Ms2.Formats.Options;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// The status option GMS2 adds: m_displayHideOtherPet on the status tab; the GMS2 exe
/// has the name, KMS2_DEV does not). Expected API: GameOption.Status.DisplayHideOtherPet, loaded and saved with the other
/// status switches (gameoptionstruct.cpp:1371-1430).
/// </summary>
public class RcGameOptionTests
{
    [Ignore("DisplayHideOtherPet is not read")]
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
    }
}
