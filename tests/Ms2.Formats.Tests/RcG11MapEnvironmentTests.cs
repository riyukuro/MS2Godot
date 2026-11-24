using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// G11 — the map environment by feature/locale (D7): gms/map/00000001.xml has four environments, (CBT,NA) with xblock
/// Login_NA; the GMS2 client picks the available one by feature level then locale. KMS table/fielddata.xml id 1 has a
/// single environment with xblock Login.
/// </summary>
public class RcG11MapEnvironmentTests
{
    [Test]
    public void TheKmsFieldKeepsItsLoginXBlock()
    {
        using var packs = new PackSet(RcGap.KmsData());
        var f = FieldData.FindById(packs, 1);
        Assert.That(f, Is.Not.Null, "kms table/fielddata.xml id 1");
        Assert.That(f.XBlock, Is.EqualTo("Login"));
    }

    // Already passes at origin/main: FieldData.Table reads the GMS2 map/ files through PerFileTable, which already picks
    // the environment with FeatureElements.SelectEnvironment, so the per-file row holds the (CBT,NA) environment. The
    // still-open part is DefaultEnvironment on the multi-environment table rows (KMS), which this fixture does not have.
    [Test]
    public void TheGms2LoginMapPicksTheNaEnvironment()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        Assert.That(LobbyFeatures.LocaleOf(packs), Is.EqualTo("NA"), "the data is the NA client's (table/na/)");
        var f = FieldData.FindById(packs, 1);
        Assert.That(f, Is.Not.Null, "gms/map/00000001.xml");
        Assert.That(f.XBlock, Is.EqualTo("Login_NA"), "the (CBT,NA) environment");
        Assert.That(FieldData.XBlockOf(packs, 1), Is.EqualTo("Login_NA"));
    }
}
