using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>The NiAlphaController a mesh's NiMaterialProperty carries (NifModel ReadAlphaController).</summary>
public class AlphaAnimTests
{
    const string Archive = "Resource/Model/Map";

    static (PackSet, NifSpec) Env() => NifEnv.Open();

    /// <summary>
    /// The workstone's glow plane has material alpha 0 and a looping alpha controller over 0..1.67 s whose Bezier keys
    /// flash to 1 at 0.13 s and back to 0 by 0.33 s. Its body meshes have no controller.
    /// </summary>
    [Test]
    public void WorkstonePlaneFlashes()
    {
        var (packs, spec) = Env();
        var md = ModelData.Load(NifFile.Load(spec, packs.Read(Archive, "ugc/indoor/ugc_in_funct_workstone_c01.nif")));
        var plane = md.Meshes.Single(m => m.FileName == "Plane");
        Assert.That(plane.MaterialAlpha, Is.EqualTo(0f));
        var a = plane.AlphaAnim;
        Assert.That(a, Is.Not.Null);
        Assert.That(a.KeyType, Is.EqualTo(2));
        Assert.That(a.Start, Is.EqualTo(0f));
        Assert.That(a.Stop, Is.EqualTo(1.6666667f).Within(1e-5));
        Assert.That(a.Frequency, Is.EqualTo(1f));
        Assert.That(a.Phase, Is.EqualTo(0f));
        Assert.That(a.Times, Is.EqualTo(new[] { 0f, 0.1f, 0.13333334f, 0.33333334f, 1.6666667f }).Within(1e-5));
        Assert.That(a.Values, Is.EqualTo(new[] { 0f, 0f, 1f, 0f, 0f }).Within(1e-5));
        Assert.That(a.InTans, Has.Length.EqualTo(5));
        Assert.That(a.OutTans, Has.Length.EqualTo(5));
        TestContext.WriteLine("in " + string.Join(" ", a.InTans) + " out " + string.Join(" ", a.OutTans));
        Assert.That(md.Meshes.Where(m => m.FileName != "Plane").Select(m => m.AlphaAnim), Is.All.Null);
    }

    /// <summary>The bank's glass sweep: alpha 0 between two 0.6 flashes, at 1.77 s and 3.5 s of a 4.33 s loop.</summary>
    [Test]
    public void BankSweepFlashesTwice()
    {
        var (packs, spec) = Env();
        var md = ModelData.Load(NifFile.Load(spec, packs.Read(Archive, "tria/field/tr_fi_struc_bank_a01.nif")));
        var a = md.Meshes.Single(m => m.FileName == "Object01").AlphaAnim;
        Assert.That(a, Is.Not.Null);
        Assert.That(a.Stop, Is.EqualTo(4.3333335f).Within(1e-5));
        Assert.That(a.Values, Is.EqualTo(new[] { 0f, 0f, 0.6f, 0f, 0f, 0.6f, 0f, 0f }).Within(1e-5));
    }

    /// <summary>The library's sphere has an alpha property but no alpha controller.</summary>
    [Test]
    public void LibrarySphereHasNoController()
    {
        var (packs, spec) = Env();
        var md = ModelData.Load(NifFile.Load(spec, packs.Read(Archive, "tria/field/tr_fi_struc_library_a01.nif")));
        Assert.That(md.Meshes.Single(m => m.FileName == "Sphere").AlphaAnim, Is.Null);
    }
}
