using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>Emitter directions follow NiPSEmitter::ComputeDirection: declination 0 is +Z, and the pre-RPI and RPI methods
/// differ.</summary>
public class EffectEmitterTests
{
    static void Near(Vec3 v, double x, double y, double z, string what)
    {
        Assert.That(v.X, Is.EqualTo(x).Within(1e-6), what + " x");
        Assert.That(v.Y, Is.EqualTo(y).Within(1e-6), what + " y");
        Assert.That(v.Z, Is.EqualTo(z).Within(1e-6), what + " z");
    }

    [Test]
    public void DirectionMethods()
    {
        foreach (var pre in new[] { true, false })
        {
            Near(EmitterDef.Direction(0, 1.2, pre), 0, 0, 1, $"dec 0 pre {pre}");
        }
        Near(EmitterDef.Direction(Math.PI, 0, true), 0, 0, -1, "pre-RPI dec pi");
        Near(EmitterDef.Direction(Math.PI / 2, 0, true), 1, 0, 0, "pre-RPI dec pi/2");
        Near(EmitterDef.Direction(Math.PI / 2, Math.PI / 2, true), 0, 1, 0, "pre-RPI dec pi/2 plan pi/2");
        Near(EmitterDef.Direction(Math.PI / 2, 0, false), 0, -1, 0, "RPI dec pi/2");
        Near(EmitterDef.Direction(0.3, Math.PI / 2, false), Math.Cos(0.3), -Math.Sin(0.3), 0, "RPI dec 0.3 plan pi/2");
    }

    /// <summary>The GrandHall portal's star system (bg/common/eff_com_portal_b.nif, Blizzard01) is pre-RPI with declination
    /// pi, so its particles leave the box emitter along the box's -Z, the normal of the width x height plane.</summary>
    [Test]
    public void PortalStarsLeaveAlongBoxNormal()
    {
        var (packs, spec) = NifEnv.Open();
        var sc = EffectScene.Load(NifFile.Load(spec, packs.Read("Resource/Model/Effect", "bg/common/eff_com_portal_b.nif")));
        var ps = sc.Systems.Single(s => s.Name == "Blizzard01");
        Assert.That(ps.PreRpi, Is.True);
        var e = ps.Emitters.Single();
        Assert.That(e.Kind, Is.EqualTo(EmitterKind.Box));
        Assert.That(e.Depth, Is.EqualTo(0).Within(1e-6));
        Assert.That(e.Width * e.Height, Is.GreaterThan(1000));
        Near(EmitterDef.Direction(e.Declination, e.PlanarAngle, ps.PreRpi), 0, 0, -1, "Blizzard01");
    }

    /// <summary>The Focus Seal cast (ch/skill/wizard/eff_wizard_concentration_cast_01_a.nif) system PArray01 emits from the
    /// face surface of a hidden thin column mesh (z 0 to 120) along its normals. Its particles start all along the column
    /// and leave sideways, not on the two vertex rings at z 0 and z 120 going the emitter direction.</summary>
    [Test]
    public void FocusSealEmitsFromColumnSurfaceAlongNormals()
    {
        var (packs, spec) = NifEnv.Open();
        var sc = EffectScene.Load(NifFile.Load(spec, packs.Read("Resource/Model/Effect", "ch/skill/wizard/eff_wizard_concentration_cast_01_a.nif")));
        var e = sc.Systems.Single(s => s.Name == "PArray01").Emitters.Single();
        Assert.That(e.Kind, Is.EqualTo(EmitterKind.Mesh));
        Assert.That(e.MeshEmission, Is.EqualTo(MeshEmitFrom.FaceSurface));
        Assert.That(e.MeshVelocityType, Is.EqualTo(MeshVelocity.Normals));
        Assert.That(e.MeshTriangles, Has.Length.EqualTo(108));
        Assert.That(e.MeshNormals, Has.Length.EqualTo(e.MeshPositions.Length));
        var rng = new Random(7);
        int middle = 0, sideways = 0;
        const int N = 2000;
        for (int k = 0; k < N; k++)
        {
            Assert.That(e.SampleMesh(rng, out var p, out var d), Is.True);
            Assert.That(p.Z, Is.InRange(-1e-3, 120 + 1e-3));
            if (p.Z > 10 && p.Z < 110) middle++;
            if (Math.Abs(d.Z) < 0.3) sideways++;
            Assert.That(Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z), Is.EqualTo(1).Within(1e-9));
        }
        Assert.That(middle, Is.GreaterThan(N * 7 / 10), "points along the column");
        Assert.That(sideways, Is.GreaterThan(N * 9 / 10), "directions off the side normals");
    }

    /// <summary>Each emission type on one triangle (0,0,0) (10,0,0) (0,10,0) with +Z normals: the vertex, centroid, edge
    /// midpoint and surface points, and a mesh without indices falling back to vertices.</summary>
    [Test]
    public void MeshEmissionTypes()
    {
        var e = new EmitterDef
        {
            MeshPositions = new float[] { 0, 0, 0, 10, 0, 0, 0, 10, 0 },
            MeshNormals = new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1 },
            MeshTriangles = new[] { 0, 1, 2 },
        };
        var rng = new Random(3);
        bool IsVertex(Vec3 p) => (p.X == 0 && p.Y == 0) || (p.X == 10 && p.Y == 0) || (p.X == 0 && p.Y == 10);
        bool IsMid(Vec3 p) => (p.X == 5 && p.Y == 0) || (p.X == 5 && p.Y == 5) || (p.X == 0 && p.Y == 5);
        for (int k = 0; k < 200; k++)
        {
            e.MeshEmission = MeshEmitFrom.Vertices; e.SampleMesh(rng, out var p, out var d);
            Assert.That(IsVertex(p), Is.True, $"vertex {p}");
            Near(d, 0, 0, 1, "normal");
            e.MeshEmission = MeshEmitFrom.FaceCenter; e.SampleMesh(rng, out p, out _);
            Near(p, 10.0 / 3, 10.0 / 3, 0, "centroid");
            e.MeshEmission = MeshEmitFrom.EdgeCenter; e.SampleMesh(rng, out p, out _);
            Assert.That(IsMid(p), Is.True, $"edge midpoint {p}");
            e.MeshEmission = MeshEmitFrom.FaceSurface; e.SampleMesh(rng, out p, out _);
            Assert.That(p.X >= -1e-9 && p.Y >= -1e-9 && p.X + p.Y <= 10 + 1e-9 && p.Z == 0, Is.True, $"face surface {p}");
            e.MeshEmission = MeshEmitFrom.EdgeSurface; e.SampleMesh(rng, out p, out _);
            Assert.That(Math.Abs(p.X) < 1e-9 || Math.Abs(p.Y) < 1e-9 || Math.Abs(p.X + p.Y - 10) < 1e-9, Is.True, $"edge surface {p}");
        }
        e.MeshTriangles = null; e.MeshEmission = MeshEmitFrom.FaceSurface;
        e.SampleMesh(rng, out var q, out _);
        Assert.That(IsVertex(q), Is.True, "no indices: a vertex");
        e.MeshVelocityType = MeshVelocity.Direction;
        Assert.That(e.SampleMesh(rng, out _, out _), Is.False, "direction velocity takes no normal");
    }

    /// <summary>The mesh emitter's triangle list is checked at load: only the submesh 0 range of the INDEX stream is used,
    /// and a range with an index past the vertex count, past the stream's end or not whole triangles gives no list (the
    /// emitter then emits from vertices instead of reading past its vertices every frame).</summary>
    [Test]
    public void MeshTrianglesCheckedAtLoad()
    {
        double[] two = { 0, 1, 2, 2, 1, 3, 4, 5, 6 };
        Assert.That(EmitterDef.Triangles(two, 0, 6, 4), Is.EqualTo(new[] { 0, 1, 2, 2, 1, 3 }), "submesh 0 of two regions");
        Assert.That(EmitterDef.Triangles(two, 0, 9, 4), Is.Null, "an index past the vertex count");
        Assert.That(EmitterDef.Triangles(two, 0, 9, 7), Is.EqualTo(new[] { 0, 1, 2, 2, 1, 3, 4, 5, 6 }), "all in range");
        Assert.That(EmitterDef.Triangles(two, 6, 6, 7), Is.Null, "a range past the stream's end");
        Assert.That(EmitterDef.Triangles(two, 0, 4, 7), Is.Null, "not whole triangles");
        Assert.That(EmitterDef.Triangles(new double[] { 0, -1, 2 }, 0, 3, 3), Is.Null, "a negative index");
    }
}
