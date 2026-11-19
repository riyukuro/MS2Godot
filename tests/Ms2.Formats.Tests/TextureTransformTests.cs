using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>NiTextureTransform matrices (NiTextureTransform::UpdateMatrix), the per-map transforms, uv sets and clamp
/// modes read from NiTexturingProperty, and the map an NiTextureTransformController moves.</summary>
public class TextureTransformTests
{
    const string Archive = "Resource/Model/Effect";

    static EffectScene Load(string entry)
    {
        var (packs, spec) = NifEnv.Open();
        return EffectScene.Load(NifFile.Load(spec, packs.Read(Archive, entry)));
    }

    static void Rows(TexTransform t, params float[] want) => Assert.That(t.Rows(), Is.EqualTo(want).Within(1e-5f));

    /// <summary>The identity transform: MAX and MAYA_DEPRECATED leave uv alone, MAYA flips v (v' = 1 - v).</summary>
    [Test]
    public void IdentityPerMethod()
    {
        Rows(new TexTransform { Method = TexTransform.Max }, 1, 0, 0, 0, 1, 0);
        Rows(new TexTransform { Method = TexTransform.MayaDeprecated }, 1, 0, 0, 0, 1, 0);
        Rows(new TexTransform { Method = TexTransform.Maya }, 1, 0, 0, 0, -1, 1);
        Assert.That(TexTransform.ControllerDefault().Method, Is.EqualTo(TexTransform.Maya));
    }

    /// <summary>MAX: a U translation subtracts and a V translation adds; scale is about the centre.</summary>
    [Test]
    public void MaxTranslationAndScale()
    {
        Rows(new TexTransform { Method = TexTransform.Max, TU = 0.1f, TV = 0.2f }, 1, 0, -0.1f, 0, 1, 0.2f);
        // portal_b's base map: translation (0, 0.75), scale (3, 1)
        Rows(new TexTransform { Method = TexTransform.Max, TV = 0.75f, SU = 3 }, 3, 0, -1, 0, 1, 0.75f);
        var r = new TexTransform { Method = TexTransform.Max, Rotate = MathF.PI / 2 }.Rows();
        Assert.That(r, Is.EqualTo(new[] { 0f, 1f, 0f, -1f, 0f, 1f }).Within(1e-5f));
    }

    /// <summary>A controller value replaces one component (0 TU, 1 TV, 2 rotate, 3 SU, 4 SV).</summary>
    [Test]
    public void SetReplacesOneComponent()
    {
        var t = new TexTransform();
        t.Set(0, 1); t.Set(1, 2); t.Set(2, 3); t.Set(3, 4); t.Set(4, 5);
        Assert.That(new[] { t.TU, t.TV, t.Rotate, t.SU, t.SV }, Is.EqualTo(new[] { 1f, 2f, 3f, 4f, 5f }));
    }

    /// <summary>portal_b: the glow maps carry their own MAX transforms (one rotated a quarter turn, one scaled 2) and wrap
    /// both axes (flags 12800); the band's base map is translated, scaled and clamps T only (flags 8704).
    /// Its texture-slot-4 controllers move the glow map.</summary>
    [Test]
    public void PortalMapsAndControllerSlots()
    {
        var sc = Load("bg/common/eff_com_portal_b.nif");
        var glows = sc.Meshes.Select(m => m.Mesh.Maps.GetValueOrDefault("glow")).Where(g => g != null).ToList();
        Assert.That(glows.Any(g => g.Transform is { Method: TexTransform.Max } t && Math.Abs(t.Rotate - 1.571f) < 1e-3f && g.Clamp == 3));
        Assert.That(glows.Any(g => g.Transform is { SU: 2, SV: 2 }));
        var band = sc.Meshes.Select(m => m.Mesh.Maps.GetValueOrDefault("base")).First(b => b?.Transform != null && b.Clamp == 2);
        Assert.That((band.Clamp, band.ClampS, band.ClampT, band.UvSet), Is.EqualTo((2, false, true, 0)));
        Assert.That(new[] { band.Transform.TV, band.Transform.SU }, Is.EqualTo(new[] { 0.75f, 3f }).Within(1e-5f));
        var ops = sc.Meshes.SelectMany(m => m.Uv).ToList();
        Assert.That(ops.Any(o => o.Slot == "glow" && o.Operation == 2));
        Assert.That(ops.Any(o => o.Slot == "base"));
        Assert.That(ops.All(o => o.Slot is "base" or "glow"));
    }

    /// <summary>The per-frame uv path allocates nothing: copying the static transform, replaying the controllers and
    /// writing the rows into caller storage, as EffectInstance.AnimateUv does each frame (portal_b's glow scroll).</summary>
    [Test]
    public void UvPathAllocatesNothing()
    {
        var sc = Load("bg/common/eff_com_portal_b.nif");
        var meshes = sc.Meshes.Where(m => m.Uv.Count > 0).ToList();
        Assert.That(meshes, Is.Not.Empty);
        var work = new TexTransform(); var rows = new float[6]; var none = TexTransform.ControllerDefault();
        void Frame(double t)
        {
            foreach (var m in meshes)
            {
                work.CopyFrom(m.Mesh.Maps.GetValueOrDefault("glow")?.Transform ?? none);
                foreach (var op in m.Uv) if (op.Ctl.Active) work.Set(op.Operation, (float)op.Curve(op.Ctl.Local(t)));
                work.Rows(rows);
            }
        }
        for (int i = 0; i < 100; i++) Frame(i / 60.0);      // warm up (JIT, lazy curve state)
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 600; i++) Frame(i / 60.0);
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0));
    }
}
