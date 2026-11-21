using Godot;

namespace Ms2Godot;

/// <summary>
/// SetNodeAlpha (@0x14098b3e0, alphaanimation.cpp:478-545) on a character's node tree, as GameObject::vfunc_42
/// (gameobject.cpp:893) and PC::vfunc_42 (pc.cpp:4545) call it: every material of the tree without an NiAlphaController
/// takes its original alpha times the factor with blending on; hidden (AppCulled) subtrees are skipped unless forced; a
/// factor of 1 or more restores the original blending and alpha. The alpha test is left as it is.
/// A mesh that ModelBuilder.Attached marked (<see cref="Meta"/>, the blended form of its shader) takes the factor as the
/// shader's node_alpha instance uniform; an opaque mesh draws in that blended form through a material override while the
/// factor is below 1, and a shadow-only twin with its own material keeps its shadow (Godot casts none from a transparent
/// material; the client's shadow write ignores the blend, NiDirectionalShadowWriteMaterial.cpp:405-409).
/// STILL-OPEN: nodes with flag 0x400 are not skipped; a mesh without its own NiMaterialProperty takes the root's added one in
/// the client (original alpha -1 times the factor); the swapped meshes keep their CharacterAlphaSort place; the glow twins
/// keep their alpha.
/// </summary>
public static class NodeFade
{
    /// <summary>The blended form of a character mesh's shader (ModelBuilder.Attached).</summary>
    public static readonly StringName Meta = "ms2_node_fade";
    static readonly StringName Override = "node_fade";
    const string ShadowName = "node_fade_shadow";

    /// <summary>SetNodeAlpha(root, <paramref name="alpha"/>, <paramref name="force"/>).</summary>
    public static void Set(Node root, float alpha, bool force)
    {
        if (root != null) Walk(root, alpha, force, alpha >= 1f);
    }

    static void Walk(Node n, float alpha, bool force, bool restore)
    {
        if (n is MeshInstance3D mi && mi.HasMeta(Meta)) Apply(mi, alpha, restore);
        int count = n.GetChildCount();
        for (int i = 0; i < count; i++)
        {
            var c = n.GetChild(i);
            if (!force && c is Node3D { Visible: false }) continue;
            Walk(c, alpha, force, restore);
        }
    }

    static void Apply(MeshInstance3D mi, float alpha, bool restore)
    {
        if (restore)
        {
            mi.SetInstanceShaderParameter(ShaderParam.NodeAlpha, 1f);
            if (!mi.HasMeta(Override)) return;
            mi.RemoveMeta(Override);
            mi.MaterialOverride = null;
            if (mi.GetNodeOrNull(ShadowName) is Node twin)
            {
                mi.CastShadow = (GeometryInstance3D.ShadowCastingSetting)twin.GetMeta(Override).AsInt32();
                mi.RemoveChild(twin);
                twin.QueueFree();
            }
            return;
        }
        if (!mi.HasMeta(Override) && mi.Mesh?.SurfaceGetMaterial(0) is ShaderMaterial own)
        {
            var blended = mi.GetMeta(Meta).As<Shader>();
            if (own.Shader != blended)
            {
                var copy = (ShaderMaterial)own.Duplicate();
                copy.Shader = blended;
                mi.MaterialOverride = copy;
                mi.SetMeta(Override, true);
                if (mi.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off)
                {
                    var twin = new MeshInstance3D { Name = ShadowName, Mesh = mi.Mesh, Skin = mi.Skin, MaterialOverride = own, CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly };
                    twin.SetMeta(Override, (int)mi.CastShadow);   // the setting to restore
                    mi.AddChild(twin);
                    if (mi.Skin != null && mi.GetNodeOrNull(mi.Skeleton) is Node skel) twin.Skeleton = twin.GetPathTo(skel);
                    mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                }
            }
        }
        mi.SetInstanceShaderParameter(ShaderParam.NodeAlpha, alpha);
    }
}
