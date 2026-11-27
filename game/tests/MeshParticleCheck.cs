using System;
using System.Collections.Generic;
using Godot;
using Ms2.Formats.Model;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/mesh_particle.tscn, no game data): an NiPSMeshParticleSystem whose master particle is a node
/// with a quad and a child node 2 up with another. The masters hang under no node, so they are never drawn where they stand
/// (EffectMesh.Master); each live particle draws a clone (NiPSMeshParticleSystem::InitializeParticle,
/// NiPSMeshParticleSystem.cpp:229-300) at its position, turned by the mesh-align step's fixed normal +Y and up +Z in the
/// system's frame, the system node being turned a quarter about +Z (MeshAlign: columns normal, up x normal, normal x right,
/// so the clone's axes are -X, -Y, +Z), scaled by the particle's size, the child at its local offset under it
/// (PostUpdate, :376-436). A dead particle's clone is hidden (RemoveParticle, :326-340). Steps that reuse the pooled clones
/// allocate 0 B, both when the masters share one material and when an alpha and a UV controller give each clone its own,
/// run at the clone's age (StartAnimations, :229-300; most master meshes in the data are animated). Before the port the system drew its particles as quads and the master meshes statically.
/// With a glow map on the masters each clone of a glow-click master (blended SRCALPHA / INVSRCALPHA) draws a glow-mask twin,
/// shared with the clone's material or its own with it; an additive master's clone draws none (EffectInstance.GlowTwin).
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class MeshParticleCheck : Node
{
    const float Size = 2f, Up = 2f;

    static ModelMesh Quad(string name) => new()
    {
        Name = name, FileName = name, Positions = new float[] { -1, -1, 0, 1, -1, 0, 1, 1, 0, -1, 1, 0 },
        Normals = new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1 }, Indices = new[] { 0, 1, 2, 0, 2, 3 },
        Ambient = new float[] { 0, 0, 0 }, Diffuse = new float[] { 0, 0, 0 }, Emissive = new float[] { 1, 1, 1 },
    };

    static EffectScene Scene(double stop, bool animated = false, bool quads = false, bool glow = false)
    {
        var sc = new EffectScene { Duration = 10 };
        // the system node turned a quarter about +Z (row-major: X goes to +Y)
        sc.Nodes.Add(new EffectNode { Name = "system", Local = Xform.Identity with { R = new double[] { 0, -1, 0, 1, 0, 0, 0, 0, 1 } } });
        sc.Nodes.Add(new EffectNode { Name = "master" });
        sc.Nodes.Add(new EffectNode { Name = "leaf", Parent = 1, Local = Xform.Identity with { T = new Vec3(0, 0, Up) } });
        sc.Meshes.Add(new EffectMesh { Mesh = Quad("root"), Node = 1, Master = true });
        sc.Meshes.Add(new EffectMesh { Mesh = Quad("leaf"), Node = 2, Master = true });
        if (glow)
        {
            // the root blends SRCALPHA / INVSRCALPHA (a glow-click mesh), the leaf SRCALPHA / ONE (not one)
            foreach (var em in sc.Meshes) em.Mesh.Textures["glow"] = "glow.dds";
            sc.Meshes[0].Mesh.AlphaFlags = 0xED; sc.Meshes[1].Mesh.AlphaFlags = 0x100D;
        }
        if (animated)
            foreach (var em in sc.Meshes)
            {
                em.Alpha = t => 1 - t; em.AlphaCtl = new TimeCtl { Stop = 1 };
                em.Uv.Add(new UvOp { Operation = 0, Slot = "base", Curve = t => t, Ctl = new TimeCtl { Stop = 1 } });
            }
        var d = new ParticleSystemDef
        {
            Node = 0, Name = "meshps", MaxParticles = 64, MeshParticles = true,
            MasterNodes = new[] { new[] { 1, 2 } }, MasterMeshes = new[] { new[] { 0, 1 } },
            NormalMethod = AlignedQuad.LocalFixed, UpMethod = AlignedQuad.LocalFixed,
            NormalDirection = new Vec3(0, 1, 0), UpDirection = new Vec3(0, 0, 1), HasRotations = false,
            Material = new EffectMesh { Node = 0, Mesh = Quad("material") },
            Emitters =
            {
                new EmitterDef
                {
                    Kind = EmitterKind.Box, Speed = 10, Size = Size, Lifespan = 0.5, Rate = lt => lt < stop ? 40 : 0,
                    RateCtl = new TimeCtl { Stop = 10 },
                },
            },
        };
        sc.Systems.Add(d);
        // a quad system after it (the mirrored case): its material is set after the mesh-particle system's clones
        if (quads)
            sc.Systems.Add(new ParticleSystemDef
            {
                Node = 0, Name = "quads", MaxParticles = 8, Material = new EffectMesh { Node = 0, Mesh = Quad("quads") },
                Emitters = { new EmitterDef { Kind = EmitterKind.Box, Speed = 1, Size = 1, Lifespan = 1, Rate = _ => 4, RateCtl = new TimeCtl { Stop = 10 } } },
            });
        return sc;
    }

    public override void _Ready()
    {
        var fails = new List<string>();
        try { Run(fails); }
        catch (Exception e) { fails.Add(e.ToString()); }
        foreach (var f in fails) GD.PrintErr("mesh particle: " + f);
        GD.Print(fails.Count == 0 ? "mesh particle: PASS" : $"mesh particle: FAIL ({fails.Count})");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    ModelBuilder glowMb;

    // a builder whose texture cache holds the glow map (no game data here), as alpha_render does
    ModelBuilder GlowBuilder()
    {
        if (glowMb != null) return glowMb;
        glowMb = new ModelBuilder(null, null);
        var img = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
        img.Fill(Colors.White);
        var cache = (Dictionary<string, Texture2D>)typeof(ModelBuilder).GetField("textures", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(glowMb);
        cache["glow.dds"] = ImageTexture.CreateFromImage(img);
        return glowMb;
    }

    EffectInstance Start(double stop, bool animated = false, bool quads = false, Node parent = null, bool glow = false)
    {
        var fx = new EffectInstance(Scene(stop, animated, quads, glow), glow ? GlowBuilder() : new ModelBuilder(null, null), 10, 1);
        (parent ?? this).AddChild(fx);
        fx.FileSpace = Transform3D.Identity;
        return fx;
    }

    /// <summary>The shown clones under the effect's space node: each clone adds its meshes in MasterMeshes order (BuildClone),
    /// so with no other mesh the children pair up as (root, leaf).</summary>
    static List<(MeshInstance3D Root, MeshInstance3D Leaf)> Shown(EffectInstance fx, List<string> fails)
    {
        var list = new List<(MeshInstance3D, MeshInstance3D)>();
        var z = fx.GetChild<Node3D>(0);
        var ms = new List<MeshInstance3D>();
        for (int i = 0; i < z.GetChildCount(); i++) if (z.GetChild(i) is MeshInstance3D m) ms.Add(m);
        if (ms.Count % 2 != 0) { fails.Add($"{ms.Count} meshes, want a root and a leaf per clone"); return list; }
        for (int i = 0; i < ms.Count; i += 2)
        {
            if (ms[i].Visible != ms[i + 1].Visible) fails.Add($"clone {i / 2}: root shown {ms[i].Visible}, leaf shown {ms[i + 1].Visible}");
            if (ms[i].Visible) list.Add((ms[i], ms[i + 1]));
        }
        return list;
    }

    static int Meshes(EffectInstance fx)
    {
        int n = 0; var z = fx.GetChild<Node3D>(0);
        for (int i = 0; i < z.GetChildCount(); i++) if (z.GetChild(i) is MeshInstance3D m && m.Visible) n++;
        return n;
    }

    static bool Near(Vector3 a, Vector3 b) => a.DistanceTo(b) < 1e-4f;

    void Run(List<string> fails)
    {
        var fx = Start(0.3);
        // before any step: nothing drawn (the masters are not in the scene graph)
        if (Meshes(fx) != 0) fails.Add($"{Meshes(fx)} meshes shown before any particle: the masters are drawn statically");
        for (int f = 0; f < 10; f++) fx._Process(0.02);
        var clones = Shown(fx, fails);
        GD.Print($"mesh particle: at 0.2 s {clones.Count} clones shown, {fx.ParticlesEmitted} emitted");
        if (clones.Count == 0) fails.Add("no clone drawn for the live particles");
        if (Meshes(fx) != 2 * clones.Count) fails.Add("a mesh other than the clones is drawn");
        foreach (var (root, leaf) in clones)
        {
            var b = root.Transform.Basis;
            float s = b.Column0.Length();
            if (MathF.Abs(s - Size) > 1e-3f) fails.Add($"clone scale {s}, want the particle size {Size}");
            if (!Near(b.Column0 / s, -Vector3.Right) || !Near(b.Column1 / s, -Vector3.Up) || !Near(b.Column2 / s, Vector3.Back))
                fails.Add($"clone axes {b.Column0 / s} {b.Column1 / s} {b.Column2 / s}, want -X -Y +Z (the system's turn of normal +Y, up +Z)");
            // its child: the root's transform times the leaf's local offset
            var want = root.Transform.Origin + b * new Vector3(0, 0, Up);
            if (!Near(leaf.Transform.Origin, want)) fails.Add($"leaf clone at {leaf.Transform.Origin}, want {want} under the root at {root.Transform.Origin}");
        }
        if (clones.Count > 1 && Near(clones[0].Root.Transform.Origin, clones[^1].Root.Transform.Origin)) fails.Add("the clones do not follow their particles (one place)");
        // all dead by 1 s (emission stops at 0.3 s, life 0.5 s): every clone hidden
        for (int f = 0; f < 40; f++) fx._Process(0.02);
        if (Meshes(fx) != 0) fails.Add($"{Meshes(fx)} clones still shown after their particles died");
        fx.QueueFree();

        // steady emission: once the pool holds a clone for each slot, steps allocate nothing, with shared materials and with
        // a material per animated clone
        foreach (bool animated in new[] { false, true })
        {
            string path = animated ? "animated" : "shared";
            fx = Start(10, animated);
            for (int f = 0; f < 100; f++) fx._Process(0.02);
            var live = Shown(fx, fails);
            if (live.Count < 2) fails.Add($"{path}: {live.Count} clones live in steady emission");
            else if ((live[0].Root.MaterialOverride == live[1].Root.MaterialOverride) == animated)
                fails.Add(animated ? "animated: two clones share a material, so the per-clone path is not run" : "shared: two clones of an unanimated master have their own materials");
            long bytes = CheckAlloc.Least(() => { for (int i = 0; i < 50; i++) fx._Process(0.02); });
            GD.Print($"mesh particle: 250 steps reusing pooled clones ({path} materials, {live.Count} live): {bytes} B");
            if (bytes != 0) fails.Add($"{path}: steps reusing pooled clones allocated {bytes} B");
            fx.QueueFree();
        }

        // glow-mask twins on the clones (the glow pass on for this part only)
        bool wanted = GlowPass.Wanted;
        GlowPass.Wanted = true;
        try { Glow(fails); }
        finally { GlowPass.Wanted = wanted; }

        // A mirrored world (an x-mirrored parent): the effect's culled sides swap when it enters the tree (_EnterTree). A
        // mesh-particle system builds no quad material, so the quad pass skips it and goes on to the quad system after it;
        // the clones made so far take the mirrored cull of their master meshes, and later ones take it as they are made.
        fx = Start(10, quads: true);
        for (int f = 0; f < 20; f++) fx._Process(0.02);
        int before = Shown(fx, fails).Count;
        var flip = new Node3D { Transform = new Transform3D(new Basis(new Vector3(-1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1)), Vector3.Zero) };
        AddChild(flip);
        RemoveChild(fx);
        flip.AddChild(fx);
        Mirrored(fx, "clones made before the move", fails);
        for (int f = 0; f < 50; f++) fx._Process(0.02);
        Mirrored(fx, "clones made after it", fails);
        GD.Print($"mesh particle: mirrored: {before} clones before the move, {Shown(fx, fails).Count} after 50 steps");
        if (before == 0) fails.Add("mirrored: no clone made before the move");
        flip.QueueFree();
    }

    /// <summary>The glow twin under a clone mesh: its one child on the glow layer, drawing the mask shader.</summary>
    static MeshInstance3D Twin(MeshInstance3D m)
    {
        MeshInstance3D twin = null;
        for (int i = 0; i < m.GetChildCount(); i++)
            if (m.GetChild(i) is MeshInstance3D c && c.Layers == GlowPass.Bit) twin = c;
        return twin;
    }

    void Glow(List<string> fails)
    {
        foreach (bool animated in new[] { false, true })
        {
            string path = animated ? "glow animated" : "glow shared";
            var fx = Start(10, animated, glow: true);
            for (int f = 0; f < 100; f++) fx._Process(0.02);
            var live = Shown(fx, fails);
            if (live.Count < 2) { fails.Add($"{path}: {live.Count} clones live"); fx.QueueFree(); continue; }
            foreach (var (root, leaf) in live)
            {
                var t = Twin(root);
                if (t == null) { fails.Add($"{path}: a blended clone with a glow map draws no glow-mask twin"); continue; }
                if (t.Mesh != root.Mesh) fails.Add($"{path}: the twin draws another mesh");
                if (t.MaterialOverride is not ShaderMaterial gm || !gm.Shader.Code.Contains("ALBEDO = mix(vec3(g)"))
                    fails.Add($"{path}: the twin does not draw the glow mask");
                else if (animated && (float)gm.GetShaderParameter("alpha") != (float)((ShaderMaterial)root.MaterialOverride).GetShaderParameter("alpha"))
                    fails.Add($"{path}: twin alpha {gm.GetShaderParameter("alpha")}, the clone's {((ShaderMaterial)root.MaterialOverride).GetShaderParameter("alpha")}");
                if (Twin(leaf) != null) fails.Add($"{path}: an additive clone draws a glow-mask twin");
            }
            var t0 = Twin(live[0].Root); var t1 = Twin(live[1].Root);
            if (t0 != null && t1 != null && (t0.MaterialOverride == t1.MaterialOverride) == animated)
                fails.Add(animated ? $"{path}: two animated clones share a mask material" : $"{path}: two clones of an unanimated master have their own mask materials");
            long bytes = CheckAlloc.Least(() => { for (int i = 0; i < 50; i++) fx._Process(0.02); });
            GD.Print($"mesh particle: {path}: {live.Count} clones with twins, 250 steps {bytes} B");
            if (bytes != 0) fails.Add($"{path}: steps reusing pooled clones allocated {bytes} B");
            fx.QueueFree();
        }
        // mirrored: the clones' twins take the mirrored cull too, before and after the move
        var mfx = Start(10, glow: true);
        for (int f = 0; f < 20; f++) mfx._Process(0.02);
        var flip = new Node3D { Transform = new Transform3D(new Basis(new Vector3(-1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1)), Vector3.Zero) };
        AddChild(flip);
        RemoveChild(mfx);
        flip.AddChild(mfx);
        for (int pass = 0; pass < 2; pass++)
        {
            int twins = 0;
            foreach (var (root, _) in Shown(mfx, fails))
                if (Twin(root) is { } t)
                {
                    twins++;
                    if (!((ShaderMaterial)t.MaterialOverride).Shader.Code.Contains("cull_front")) fails.Add($"glow mirrored ({(pass == 0 ? "before" : "after")} the move): a twin keeps its unmirrored cull");
                }
            if (twins == 0) fails.Add("glow mirrored: no twin shown");
            for (int f = 0; f < 50; f++) mfx._Process(0.02);
        }
        flip.QueueFree();
    }

    /// <summary>Every shown clone mesh and the quad system's material cull the front faces (a back-culled mesh under a
    /// mirrored world, FaceCull.UnderWorld).</summary>
    static void Mirrored(EffectInstance fx, string what, List<string> fails)
    {
        var z = fx.GetChild<Node3D>(0);
        int quads = 0;
        for (int i = 0; i < z.GetChildCount(); i++)
        {
            ShaderMaterial mat = null;
            if (z.GetChild(i) is MeshInstance3D m && m.Visible) mat = m.MaterialOverride as ShaderMaterial;
            else if (z.GetChild(i) is MultiMeshInstance3D q) { quads++; mat = (q.Multimesh.Mesh as PrimitiveMesh)?.Material as ShaderMaterial; }
            if (mat != null && !mat.Shader.Code.Contains("cull_front"))
                fails.Add($"mirrored ({what}): {z.GetChild(i).Name} keeps its unmirrored cull");
        }
        if (quads != 1) fails.Add($"mirrored: {quads} quad systems drawn, want 1");
    }
}
