using System.Collections.Generic;
using System.Reflection;
using Godot;
using Ms2.Formats.Model;
using Ms2.Ui.Hud;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/node_fade.tscn, no game data): SetNodeAlpha on a built character (NodeFade) and the own
/// player's invincible blink driving it (LiveSession's combat effects tick, PC::FUN_1416615f0 pc.cpp:2612).
/// - ModelBuilder.Attached marks the character meshes without an alpha controller with their blended form;
/// - a factor below 1 swaps an opaque mesh to the blended form (a material override) with a shadow-only twin in its place
///   and sets the node_alpha instance uniform; a blended mesh keeps its material and takes the factor;
/// - a mesh flagged 0x400 (IS_PARTICLE_SYSTEM) is not marked; a mesh without its own material takes the root's added one
///   (-1 times the factor, clamped to 0);
/// - a hidden mesh is skipped unless forced, a shown mesh under a hidden node is not; 1 forced restores every mesh, hidden ones
///   too;
/// - the blink's 0.9 frame reaches the own player's meshes, a mesh shown mid-blink takes it on the next frame, and the end
///   restores them.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class NodeFadeCheck : Node
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    readonly List<string> fails = new();
    void Check(bool ok, string what) { if (!ok) fails.Add(what); }

    static ModelMesh Quad(string name)
    {
        return new ModelMesh
        {
            Name = name, FileName = name, Positions = new[] { -1f, -1, 0, 1, -1, 0, 1, 1, 0, -1, 1, 0 }, Normals = new[] { 0f, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1 },
            Indices = new[] { 0, 1, 2, 0, 2, 3 }, Ambient = new float[] { 0, 0, 0 }, Diffuse = new float[] { 0, 0, 0 }, Emissive = new float[] { 1, 1, 1 },
        };
    }

    static float Param(MeshInstance3D mi) => mi.GetInstanceShaderParameter(ShaderParam.NodeAlpha).VariantType == Variant.Type.Nil ? 1f : mi.GetInstanceShaderParameter(ShaderParam.NodeAlpha).AsSingle();

    public override void _Ready()
    {
        var mb = new ModelBuilder(null, null);
        var opaque = Quad("opaque");
        var blend = Quad("blend"); blend.AlphaFlags = 0xED; blend.MaterialAlpha = 0.5f;
        var anim = Quad("anim"); anim.AlphaFlags = 0xED;
        anim.AlphaAnim = new AlphaAnim { Start = 0, Stop = 1, KeyType = 1, Times = new[] { 0f, 1f }, Values = new[] { 0.5f, 0.5f } };
        var hidden = Quad("hidden");
        var psys = Quad("psys"); psys.Flags = 0x400;
        var bare = Quad("bare"); bare.Ambient = bare.Diffuse = bare.Emissive = null;
        var under = Quad("under");
        var md = new ModelData { RootLocal = Xform.Identity };
        md.Meshes.AddRange(new[] { opaque, blend, anim, hidden, psys, bare, under });
        var root = mb.Build(md, "pc");
        AddChild(root);
        var skel = root.GetNode("Model/" + ModelBuilder.SkeletonName);
        var o = skel.GetNode<MeshInstance3D>("opaque");
        var b = skel.GetNode<MeshInstance3D>("blend");
        var a = skel.GetNode<MeshInstance3D>("anim");
        var h = skel.GetNode<MeshInstance3D>("hidden");
        var bm = skel.GetNode<MeshInstance3D>("bare");
        var u = skel.GetNode<MeshInstance3D>("under");
        var group = new Node3D { Name = "group", Visible = false };
        skel.AddChild(group);
        u.Reparent(group);
        var oMat = (ShaderMaterial)o.Mesh.SurfaceGetMaterial(0);
        var oCast = o.CastShadow;

        Check(o.HasMeta(NodeFade.Meta) && o.GetMeta(NodeFade.Meta).As<Shader>() is { } os && os.Code.Contains("#define BLEND") && os.Code.Contains("blend_mix"),
            "the opaque mesh has no blend_mix form (no alpha property: SRCALPHA / INVSRCALPHA)");
        Check(b.HasMeta(NodeFade.Meta) && b.GetMeta(NodeFade.Meta).As<Shader>() == ((ShaderMaterial)b.Mesh.SurfaceGetMaterial(0)).Shader, "the blended mesh's form is not its own shader");
        Check(!a.HasMeta(NodeFade.Meta), "the alpha-controller mesh is marked (SetNodeAlpha skips its material)");
        Check(!skel.GetNode("psys").HasMeta(NodeFade.Meta), "the 0x400 mesh is marked (SetNodeAlpha skips it)");
        Check(bm.HasMeta(NodeFade.Inherit) && !o.HasMeta(NodeFade.Inherit), "the material-less mesh is not told from one with a material");

        h.Visible = false;
        NodeFade.Set(root, 0.5f, false);
        Check(o.MaterialOverride is ShaderMaterial ov && ov.Shader == o.GetMeta(NodeFade.Meta).As<Shader>() && ov != oMat, "the opaque mesh is not drawn in its blended form at 0.5");
        Check(Param(o) == 0.5f && Param(b) == 0.5f, $"node_alpha {Param(o)} / {Param(b)}, want 0.5");
        Check(b.MaterialOverride == null, "the blended mesh got an override");
        var twin = o.GetNodeOrNull<MeshInstance3D>("node_fade_shadow");
        Check(twin != null && twin.CastShadow == GeometryInstance3D.ShadowCastingSetting.ShadowsOnly && twin.MaterialOverride == oMat && twin.Mesh == o.Mesh
              && o.CastShadow == GeometryInstance3D.ShadowCastingSetting.Off, "no opaque shadow twin of the faded mesh");
        Check(Param(h) == 1f && h.MaterialOverride == null, "the hidden mesh was faded without force");
        Check(Param(u) == 0.5f, $"the shown mesh under a hidden node has node_alpha {Param(u)}, want 0.5 (its children are walked)");
        Check(Param(bm) == 0f, $"the material-less mesh has node_alpha {Param(bm)}, want 0 (the root's -1 times 0.5)");
        NodeFade.Set(root, 0.9f, false);
        Check(Param(o) == 0.9f && o.GetChildCount() == 1, "a second factor made another override or twin");

        NodeFade.Set(root, 0.5f, true);
        Check(Param(h) == 0.5f && h.MaterialOverride != null, "the forced factor skipped the hidden mesh");
        NodeFade.Set(root, 1f, true);
        Check(o.MaterialOverride == null && h.MaterialOverride == null && Param(o) == 1f && Param(b) == 1f && Param(h) == 1f && Param(bm) == 1f && Param(u) == 1f,
            "1 forced did not restore every mesh");
        Check(o.CastShadow == oCast && o.GetNodeOrNull("node_fade_shadow") == null, "the shadow twin outlived the restore");

        // the own player's blink: the first frame is 0.9 (not dimmed), the end restores
        var ls = new LiveSession();
        AddChild(ls);
        ((System.Threading.CancellationTokenSource)typeof(LiveSession).GetField("exitCts", Private).GetValue(ls)).Cancel();
        var player = new Player();
        RemoveChild(root);
        player.AddChild(root);
        typeof(LiveSession).GetField("player", Private).SetValue(ls, player);
        var tick = typeof(LiveSession).GetMethod("TickCombatEffects", Private);
        uint now = (uint)Time.GetTicksMsec();
        ls.Invincible.Start(10000, InvincibleBlink.PortalTimer, now);
        tick.Invoke(ls, null);
        Check(Param(o) == 0.9f && o.MaterialOverride != null, $"the blink frame left node_alpha {Param(o)}, want 0.9");
        Check(Param(h) == 1f, "the hidden mesh took the blink");
        h.Visible = true;
        tick.Invoke(ls, null);
        Check(Param(h) == 0.9f, $"the mesh shown mid-blink has node_alpha {Param(h)} on the next frame, want 0.9");
        // the walk runs every frame of the blink: 0 B
        var frame = tick.CreateDelegate<System.Action>(ls);
        frame();
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10; i++) frame();
        long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
        Check(bytes == 0, $"10 blink frames allocated {bytes} B");
        before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10; i++) NodeFade.Set(player, 0.9f, false);
        bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
        Check(bytes == 0, $"10 walks allocated {bytes} B");
        ls.Invincible.Start(1, InvincibleBlink.PortalTimer, (uint)Time.GetTicksMsec());
        OS.DelayMsec(5);
        tick.Invoke(ls, null);
        Check(!ls.Invincible.Active && Param(o) == 1f && o.MaterialOverride == null, $"the blink's end left node_alpha {Param(o)}");
        typeof(LiveSession).GetField("player", Private).SetValue(ls, null);
        player.Free();

        foreach (var f in fails) GD.PrintErr("FAIL " + f);
        GD.Print(fails.Count == 0 ? "node fade: ok" : $"node fade: {fails.Count} FAILED");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
