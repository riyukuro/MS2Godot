using Ms2.Formats.Model;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// Field effect entities are placed with their full Rotation (R = Rx * Ry * Rz, FieldEntity.Transform), not only the Z angle:
/// 1513 EffectMesh entities across the fields carry an X or Y rotation (the 02000141_in steam vents stand at 90, 0, 0).
/// </summary>
public class FieldEffectRotationTests
{
    static double[] Axis(int axis, double deg)
    {
        double a = deg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        return axis switch
        {
            0 => new[] { 1, 0, 0, 0, c, -s, 0, s, c },
            1 => new[] { c, 0, s, 0, 1, 0, -s, 0, c },
            _ => new[] { c, -s, 0, s, c, 0, 0, 0, 1 },
        };
    }

    static double[] Mul(double[] a, double[] b)
    {
        var m = new double[9];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                for (int k = 0; k < 3; k++) m[i * 3 + j] += a[i * 3 + k] * b[k * 3 + j];
        return m;
    }

    static void AssertRotation(FieldEntity e)
    {
        var r = e.RotationDeg;
        var want = Mul(Mul(Axis(0, r.X), Axis(1, r.Y)), Axis(2, r.Z));
        var got = e.Transform.R;
        for (int i = 0; i < 9; i++) Assert.That(got[i], Is.EqualTo(want[i]).Within(1e-9), $"{e.Raw.ModelName} {r} [{i}]");
    }

    /// <summary>The space world/FieldEffects plays the entity's effect in: Rx * Ry * Rz times the scale, no translation.</summary>
    static void AssertEffectSpace(FieldEntity e)
    {
        var r = e.RotationDeg;
        var want = Mul(Mul(Axis(0, r.X), Axis(1, r.Y)), Axis(2, r.Z));
        var fs = e.EffectSpace;
        Assert.That(new[] { fs.T.X, fs.T.Y, fs.T.Z }, Is.EqualTo(new double[] { 0, 0, 0 }), $"{e.Raw.ModelName} translation");
        for (int i = 0; i < 9; i++)
            Assert.That(fs.R[i] * fs.S, Is.EqualTo(want[i] * e.Scale).Within(1e-9), $"{e.Raw.ModelName} {r} scale {e.Scale} [{i}]");
    }

    [Test]
    public void SyntheticAnglesComposeXThenYThenZ()
    {
        foreach (var rot in new[] { "90,0,0", "0,90,0", "0,0,90", "58.49,0,0", "180,0,45", "30,-20,110" })
        {
            var raw = new XEntity { ModelName = "m" };
            raw.Props["Rotation"] = rot;
            raw.Props["Scale"] = "1.5";
            var fe = new FieldEntity { Raw = raw, Flat = new FlatModel { Name = "m" } };
            AssertRotation(fe);
            AssertEffectSpace(fe);
        }
        // 90 about X turns the file's up (+Z) to -Y: a steam vent stood on its side
        var x90 = new XEntity { ModelName = "m" };
        x90.Props["Rotation"] = "90,0,0";
        var R = new FieldEntity { Raw = x90, Flat = new FlatModel { Name = "m" } }.Transform.R;
        Assert.That(new[] { R[2], R[5], R[8] }, Is.EqualTo(new double[] { 0, -1, 0 }).Within(1e-12));
    }

    [Test]
    public void RealFieldEffectEntitiesUseTheirXAndYRotation()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        var packs = new Ms2.Formats.Pack.PackSet(data);
        var map = FieldMap.Load(packs, new FlatLibrary(packs), "02000141_in");
        var tilted = map.Entities.Where(e => e.HasMixin("EffectMesh") && (e.RotationDeg.X != 0 || e.RotationDeg.Y != 0)).ToList();
        Assert.That(tilted, Is.Not.Empty);
        foreach (var e in tilted) { AssertRotation(e); AssertEffectSpace(e); }
    }
}
