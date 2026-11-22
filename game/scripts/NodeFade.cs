using Godot;

namespace Ms2Godot;

/// <summary>
/// SetNodeAlpha (@0x14098b3e0, alphaanimation.cpp:478-545) on a character's node tree, as GameObject::vfunc_42
/// (gameobject.cpp:893) and PC::vfunc_42 (pc.cpp:4545) call it: every material of the tree without an NiAlphaController
/// takes its original alpha times the factor with blending on; a hidden (AppCulled) node is skipped unless forced, its children
/// are still walked (Unk_NodeQueue::Next queues them before the checks, alphaanimation.cpp:375-388, :487-493); a node flagged
/// 0x400 (IS_PARTICLE_SYSTEM, NiAVObject.h:252; FUN_14098dfc0 alphaanimation.cpp:392-395) is skipped, which
/// ModelBuilder.Attached settles by not marking such a mesh; a factor of 1 or more restores the original blending and alpha.
/// The alpha test is left as it is.
/// A mesh that ModelBuilder.Attached marked (<see cref="Meta"/>, the blended form of its shader) takes the factor as the
/// shader's node_alpha instance uniform; an opaque mesh draws in that blended form through a material override while the
/// factor is below 1, and a shadow-only twin with its own material keeps its shadow (Godot casts none from a transparent
/// material; the client's shadow write ignores the blend, NiDirectionalShadowWriteMaterial.cpp:405-409).
/// A mesh without its own NiMaterialProperty (<see cref="Inherit"/>) draws with the one EnsureAlphaProperties attached to the
/// root (alphaanimation.cpp:426-441), whose original alpha is -1: SetNodeAlpha gives it -factor (:503-515), which the
/// NiDX9Renderer's fixed-point target clamps to 0, so the mesh is not seen until the restore detaches that property.
/// STILL-OPEN: the swapped meshes keep their CharacterAlphaSort place; the glow twins keep their alpha; a material on a node
/// above the mesh (which it would inherit instead of the root's) is not read; a faded mesh with its own NiAlphaProperty
/// blends with a material alpha below 1 (alphaanimation.cpp:478-536), so the client's shadow culling
/// (ModelBuilder.ShadowCullingKeeps) drops it from the shadow map while the fade lasts, but here it keeps casting
/// (its shadow-only twin below, or its own setting).
/// </summary>
public static class NodeFade
{
    /// <summary>The blended form of a character mesh's shader (ModelBuilder.Attached).</summary>
    public static readonly StringName Meta = "ms2_node_fade";
    /// <summary>The mesh is opaque: its blended form needs a material override (ModelBuilder.Attached).</summary>
    public static readonly StringName Swap = "ms2_node_fade_swap";
    /// <summary>The mesh has no NiMaterialProperty of its own (ModelBuilder.Attached).</summary>
    public static readonly StringName Inherit = "ms2_node_fade_inherit";
    static readonly StringName Override = "node_fade";
    const string ShadowName = "node_fade_shadow";

    /// <summary>SetNodeAlpha(root, <paramref name="alpha"/>, <paramref name="force"/>).</summary>
    public static void Set(Node root, float alpha, bool force)
    {
        if (root != null) Walk(root, alpha, force, alpha >= 1f);
    }

    static void Walk(Node n, float alpha, bool force, bool restore)
    {
        if (n is MeshInstance3D mi && (force || mi.Visible) && mi.HasMeta(Meta)) Apply(mi, alpha, restore);
        int count = n.GetChildCount();
        for (int i = 0; i < count; i++) Walk(n.GetChild(i), alpha, force, restore);
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
        if (mi.HasMeta(Swap) && !mi.HasMeta(Override) && mi.Mesh?.SurfaceGetMaterial(0) is ShaderMaterial own)
        {
            var copy = (ShaderMaterial)own.Duplicate();
            copy.Shader = mi.GetMeta(Meta).As<Shader>();
            mi.MaterialOverride = copy;
            mi.SetMeta(Override, true);
            // STILL-OPEN: the client's shadow culling drops a mesh faded this way (blending, material alpha below 1;
            // see the summary); the twin keeps its shadow
            if (mi.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off)
            {
                var twin = new MeshInstance3D { Name = ShadowName, Mesh = mi.Mesh, Skin = mi.Skin, MaterialOverride = own, CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly };
                twin.SetMeta(Override, (int)mi.CastShadow);   // the setting to restore
                mi.AddChild(twin);
                if (mi.Skin != null && mi.GetNodeOrNull(mi.Skeleton) is Node skel) twin.Skeleton = twin.GetPathTo(skel);
                mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            }
        }
        // the root's added material: -1 * factor, clamped to 0 by the target
        mi.SetInstanceShaderParameter(ShaderParam.NodeAlpha, mi.HasMeta(Inherit) ? Mathf.Max(-1f * alpha, 0f) : alpha);
    }
}
