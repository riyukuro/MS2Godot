using System.Globalization;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The data's DropSPEPBall constants (needs MS2_DATA): CDropItem::LoadSpepBallConstants (dropitem.cpp:1093-1102) reads six of them
/// for the SP and EP orbs, and the drops take them through <see cref="DropConstants.ForItem"/>.
/// </summary>
public class DropSpepBallConstantsTests
{
    static readonly string[] Keys =
    {
        "DropSPEPBallTargetZPos", "DropSPEPBallMaxLength", "DropSPEPBallPickUpVel", "DropSPEPBallPickUpGravity",
        "DropSPEPBallPickUpCompleteRotateTime", "DropSPEPBallPickUpCompleteRotateVel",
    };

    [Test]
    public void TheOrbsTakeTheDatasSpepBallRows()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = TestData.Packs(d);
        var c = FeatureElements.Constants(packs);
        var k = DropConstants.From(c);
        var table = new Dictionary<string, float>();
        foreach (var key in Keys)
        {
            var v = ClientConstantsTests.TableValue(packs, key);
            Assert.That(v, Is.Not.Null, key + ": a row in table/constants.xml");
            table[key] = float.Parse(v, CultureInfo.InvariantCulture);
        }
        TestContext.Out.WriteLine("SPEP: " + string.Join(", ", Keys.Select(x => $"{x[12..]} {table[x]}")) +
                                  $"; item: TargetZPos {k.TargetZ}, MaxLength {k.MaxLength}, PickUpVel {k.PickUpVel}");
        var b = k.ForItem(0x55d4a8a);
        Assert.That(k.ForItem(0x55d4a89), Is.SameAs(b));
        Assert.That(new[] { b.TargetZ, b.MaxLength, b.PickUpVel, b.PickUpGravity, b.RotateTime, b.RotateVel },
            Is.EqualTo(Keys.Select(x => table[x]).ToArray()));
        Assert.That(b.MaxLength, Is.GreaterThan(0f), "an orb can be reached");
    }
}
