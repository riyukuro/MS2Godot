using System.Text;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>korFlash.xml rows as NiGFxLocalizeManager::ParseStringXml picks them: by locale, later rows win, features ignored.</summary>
public class FlashKeysTests
{
    const string Xml = "<ms2>"
        + "<key id=\"$s_a\" value=\"first\" />"
        + "<key id=\"$s_a\" value=\"na only\" locale=\"NA\" />"
        + "<key id=\"$s_b\" value=\"x\\ny\" feature=\"SomeFeature\" />"
        + "<key id=\"$s_c\" value=\"kr only\" locale=\"KR\" />"
        + "</ms2>";

    [Test]
    public void ARowForAnotherLocaleIsSkipped()
    {
        var kr = GameStrings.FlashKeys(Encoding.UTF8.GetBytes(Xml), "KR");
        Assert.That(kr["$s_a"], Is.EqualTo("first"), "the NA row does not replace the general one");
        Assert.That(kr["$s_c"], Is.EqualTo("kr only"));
        var na = GameStrings.FlashKeys(Encoding.UTF8.GetBytes(Xml), "na");
        Assert.That(na["$s_a"], Is.EqualTo("na only"), "the later row of the client's locale wins, case-insensitive");
        Assert.That(na.ContainsKey("$s_c"), Is.False);
    }

    [Test]
    public void TheFeatureAttributeIsNotLookedAtAndEscapesBecomeLineBreaks()
    {
        Assert.That(GameStrings.FlashKeys(Encoding.UTF8.GetBytes(Xml), "KR")["$s_b"], Is.EqualTo("x\ny"));
    }
}
