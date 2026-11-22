using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

[TestFixture]
public class BodyAttachTests
{
    /// <summary>
    /// A replace asset (the female ribbon wave hair) hangs on Bip01 Head with its self node moved by the body's own HR mesh local
    /// (CKfmModel::vfunc_13 @0x14143e900, kfmmodel.cpp:548-585, kXf from FUN_141202280 @0x141202280). That local is the offset from
    /// the head bone back to the model root, about -81 along the bone's x axis. Without it the hair, authored 81 units above the
    /// root, was rotated by the head bone and floated beside a bald head.
    /// </summary>
    [Test]
    public void ReplaceAssetKeepsBodyMeshLocal()
    {
        var (packs, spec) = NifEnv.Open();
        var md = ModelData.Load(NifFile.Load(spec, packs.Read("Resource/Model/Character", "female/f_body.nif")));
        var (bone, local) = md.FindBodyAttach("HR", true);
        Assert.That(bone, Is.EqualTo("Bip01 Head"));
        Assert.That(local, Is.Not.Null, "the replace asset must be moved by the body mesh local, not left at identity");
        Assert.That(local!.Value.T.X, Is.EqualTo(-81.39).Within(0.05));
        Assert.That(Math.Abs(local.Value.T.Y), Is.LessThan(0.1));
        Assert.That(Math.Abs(local.Value.T.Z), Is.LessThan(0.1));
        var (bone2, local2) = md.FindBodyAttach("NoSuchNode", true);
        Assert.That(bone2, Is.EqualTo("Bip01 Head"));
        Assert.That(local2, Is.Null);
    }
}
