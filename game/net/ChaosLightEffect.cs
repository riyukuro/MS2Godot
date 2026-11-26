using System.Collections.Generic;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.World;

namespace Ms2Godot.Net;

/// <summary>
/// A chaoslight part (ChaosLightEffect, uichaoslightdialog.cpp:349-560; type 0xe): a fresnel rim light on the owner's
/// character meshes, run by <see cref="ChaosLightRun"/>.
/// - vfunc_3: the owner's nodes (GameObject::vfunc_44: its entity's scene node; PC::vfunc_44 adds every shown equip
///   model), all below the actor here; none without an owner. A start delay of 0 puts the layers on at once.
/// - vfunc_4 / AddLayers: one <see cref="ChaosLayers"/> layer on each mesh <see cref="ModelBuilder.Attached"/> marked
///   (<see cref="TakesLayer"/>), with the part's layer id. Meshes added to the owner later get none.
/// - vfunc_9: the blink and the keys; the values go to every layer, and the mesh shows them while the layer is its top.
/// - vfunc_7: the layers come off when the part ends or the effect is stopped (the node leaves the tree).
/// STILL-OPEN: the kind 4 material (FUN_140ef4dd0) whose chaos material is null; SetVisible on a kept effect does not
/// hide the light (no vfunc_10 read).
/// </summary>
public sealed partial class ChaosLightEffect : Node3D
{
    public static int Made;
    readonly ChaosLightRun run = new();
    readonly List<ChaosLayers.Layer> layers = new();
    Node3D owner;
    bool stopped;

    /// <summary>Meshes AddLayers puts a layer on: a character material (kind 1, 2 or 3), unless an
    /// NiTextureTransformController drives its glow map (uichaoslightdialog.cpp:527-545).</summary>
    public static bool TakesLayer(ModelMesh m) =>
        m.Material is "MS2CharacterMaterial" or "MS2CharacterSkinMaterial" or "MS2CharacterHairMaterial"
        && !(m.GlowMapDriven && m.Textures.ContainsKey("glow"));

    /// <summary>vfunc_3: null without an owner or a param (the client makes no part).</summary>
    public static ChaosLightEffect Make(EffectPart part, Node3D actor)
    {
        if (part.ChaosLight == null || actor == null) return null;
        var fx = new ChaosLightEffect { Name = "ChaosLight", owner = actor };
        if (fx.run.Begin(part.ChaosLight, part.StartDelay, part.Duration, part.Loop, CameraShakes.Now)) fx.AddLayers();
        Made++;
        return fx;
    }

    /// <summary>The layers the part holds (for the checks).</summary>
    public int LayerCount => layers.Count;
    public ChaosLightRun Run => run;

    public override void _Process(double delta)
    {
        if (stopped) return;
        bool alive = run.Tick(CameraShakes.Now, out bool startedNow);
        if (startedNow) AddLayers();
        if (!alive) { Stop(); QueueFree(); return; }
        for (int i = 0; i < layers.Count; i++) ChaosLayers.Set(layers[i], run.Layer);
    }

    public override void _ExitTree() => Stop();

    void AddLayers()
    {
        if (owner != null && IsInstanceValid(owner)) Walk(owner);
    }

    void Walk(Node n)
    {
        if (n is MeshInstance3D mi && mi.HasMeta(ChaosLayers.Meta)) layers.Add(ChaosLayers.Add(mi, run.Param.Layer, run.Layer));
        int count = n.GetChildCount();
        for (int i = 0; i < count; i++) Walk(n.GetChild(i));
    }

    void Stop()
    {
        if (stopped) return;
        stopped = true;
        for (int i = 0; i < layers.Count; i++) ChaosLayers.Remove(layers[i]);
        layers.Clear();
    }
}

/// <summary>
/// The chaos light layer manager (WorldService's LayerManager, layer.cpp:51-168): per mesh, its layers in id order with
/// the base layer (the mesh's own material, id -1) first; the last is the top, the one the mesh shows.
/// - LayerEntry::AddLayer: a layer whose id is at least the top's goes on top (the newest of equal ids wins); a lower one
///   is put before the first layer with a greater id and is not shown.
/// - RemoveLayer: the next top is shown when the top goes; the base layer turns the chaos material off (the mesh's
///   own material and glow map, ChaosLightEffectLayer::vfunc_20).
/// - A mesh shows its top layer's values through character_lit's chaos instance uniforms. The colour pass ignores the
///   glow map then (ms2standardmaterial.cpp:1542, GLOWMAPCOUNT &amp;&amp; !m_bChaos), but a layer installs the manager's 1x1
///   white texture as the mesh's glow map (vfunc_19, chaoslighttransitiondata.cpp:289-303, the pixel :313-320). The glow
///   pass picks meshes by having a glow map (FUN_1414cffc0, ms2materialswapprocessor.cpp:158-170) and writes its alpha
///   (ms2glowmaterial.cpp:174-185), so while a layer is on the mesh's silhouette glows at 1 (times its detail alpha):
///   its glow pass twin (ModelBuilder.AddGlowTwin, the sibling "&lt;name&gt;_glow") draws with field_glow's chaos_glow, and a
///   mesh without one gets a twin (<see cref="ChaosGlowSource"/>) shown only meanwhile. A mesh that blends other than
///   SRCALPHA / INVSRCALPHA stays out of the glow pass (ModelBuilder.GlowMaterial).
/// </summary>
public static class ChaosLayers
{
    /// <summary>The mesh takes chaos light layers (<see cref="ChaosLightEffect.TakesLayer"/>, set by ModelBuilder.Attached).</summary>
    public static readonly StringName Meta = "ms2_chaos_light";
    /// <summary>The <see cref="ChaosGlowSource"/> of a marked mesh without a glow pass twin of its own.</summary>
    public static readonly StringName GlowSource = "ms2_chaos_glow";

    public sealed class Layer
    {
        public int Id;
        public MeshInstance3D Mesh;
        public ChaosLightValues Values;
        public bool IsBase;
        /// <summary>On the base layer: the mesh's glow pass twin (its own, or one made for the chaos glow), or null.</summary>
        public MeshInstance3D Glow;
        /// <summary>On the base layer: <see cref="Glow"/> is the mesh's own twin, shown without a layer too.</summary>
        public bool OwnGlow;
        /// <summary>On the base layer: the twin draws the chaos glow now.</summary>
        public bool ChaosGlow;
    }

    /// <summary>Times a glow twin was switched (for the checks: only when a layer goes on or the last comes off).</summary>
    public static int GlowSwitches;

    static readonly Dictionary<MeshInstance3D, List<Layer>> entries = new();

    /// <summary>Meshes holding a chaos layer now.</summary>
    public static int Meshes => entries.Count;

    /// <summary>The layer the mesh shows (null: none, the mesh's own material).</summary>
    public static Layer Top(MeshInstance3D mesh) =>
        entries.TryGetValue(mesh, out var list) && !list[^1].IsBase ? list[^1] : null;

    public static Layer Add(MeshInstance3D mesh, int id, ChaosLightValues values)
    {
        if (!entries.TryGetValue(mesh, out var list))
        {
            var own = mesh.GetParent()?.GetNodeOrNull<MeshInstance3D>(mesh.Name + "_glow");
            entries[mesh] = list = new List<Layer>(2) { new Layer { Id = -1, Mesh = mesh, IsBase = true, Glow = own, OwnGlow = own != null } };
        }
        var layer = new Layer { Id = id, Mesh = mesh, Values = values };
        if (list[^1].Id <= id)
        {
            list.Add(layer);
            Apply(layer);
        }
        else
        {
            // an id below the base's (-1) goes under it in the client and is never shown, as the base never leaves; it is
            // kept after the base here so that list[0] stays the base (the data has none: ChaosLightSweepTests)
            for (int i = 1; i < list.Count; i++)
                if (list[i].Id > id) { list.Insert(i, layer); break; }
        }
        return layer;
    }

    /// <summary>ChaosLightEffectLayer::FUN_141046410: the values kept, pushed to the mesh while the layer is its top.</summary>
    public static void Set(Layer layer, in ChaosLightValues values)
    {
        if (layer.Values.Equals(values)) return;
        layer.Values = values;
        if (entries.TryGetValue(layer.Mesh, out var list) && list[^1] == layer) Apply(layer);
    }

    public static void Remove(Layer layer)
    {
        if (!entries.TryGetValue(layer.Mesh, out var list)) return;
        int i = list.IndexOf(layer);
        if (i < 0) return;
        bool top = i == list.Count - 1;
        list.RemoveAt(i);
        if (top && list.Count > 0) Apply(list[^1]);
        // only the base left: the entry is the same as a new one
        if (list.Count == 1 && list[0].IsBase) entries.Remove(layer.Mesh);
    }

    /// <summary>The glow pass twin of a mesh holding chaos layers (for the checks).</summary>
    public static MeshInstance3D GlowOf(MeshInstance3D mesh) => entries.TryGetValue(mesh, out var list) ? list[0].Glow : null;

    static void Apply(Layer layer)
    {
        var mi = layer.Mesh;
        if (!GodotObject.IsInstanceValid(mi)) return;
        if (entries.TryGetValue(mi, out var list)) SwitchGlow(list[0], !layer.IsBase);
        if (layer.IsBase) { mi.SetInstanceShaderParameter(ShaderParam.ChaosOn, 0f); return; }
        var v = layer.Values;
        mi.SetInstanceShaderParameter(ShaderParam.ChaosOn, 1f);
        mi.SetInstanceShaderParameter(ShaderParam.ChaosColor, new Vector3(v.R, v.G, v.B));
        mi.SetInstanceShaderParameter(ShaderParam.ChaosBoost, v.Boost);
        mi.SetInstanceShaderParameter(ShaderParam.ChaosExponent, v.Exponent);
        mi.SetInstanceShaderParameter(ShaderParam.ChaosDimmer, v.Dimmer);
    }

    /// <summary>The glow pass while a layer is on (<paramref name="chaos"/>) or not: the own twin draws the chaos glow or its
    /// glow map, a made twin is shown or hidden. Only on a change.</summary>
    static void SwitchGlow(Layer b, bool chaos)
    {
        if (b.ChaosGlow == chaos) return;
        b.ChaosGlow = chaos;
        if (chaos && b.Glow == null && b.Mesh.HasMeta(GlowSource) && b.Mesh.GetMeta(GlowSource).As<ChaosGlowSource>() is { } source)
            b.Glow = source.Twin(b.Mesh);
        if (b.Glow == null || !GodotObject.IsInstanceValid(b.Glow)) return;
        GlowSwitches++;
        if (b.OwnGlow) ((ShaderMaterial)b.Glow.MaterialOverride).SetShaderParameter(ShaderParam.ChaosGlow, chaos);
        else b.Glow.Visible = chaos;
    }
}

/// <summary>
/// The glow pass twin of a marked mesh without one of its own, made when a chaos light layer first goes on: field_glow
/// with chaos_glow (ModelBuilder.GlowMaterial), a child of the mesh so it moves, hides and frees with it.
/// </summary>
public sealed partial class ChaosGlowSource : RefCounted
{
    readonly ModelBuilder builder;
    readonly ModelMesh mesh;
    MeshInstance3D twin;
    bool tried;

    public ChaosGlowSource() { }
    public ChaosGlowSource(ModelBuilder builder, ModelMesh mesh) { this.builder = builder; this.mesh = mesh; }

    /// <summary>The twin, made hidden on the first call; null for a mesh the glow pass leaves out.</summary>
    public MeshInstance3D Twin(MeshInstance3D mi)
    {
        if (twin != null && GodotObject.IsInstanceValid(twin)) return twin;
        if (tried || builder == null) return null;
        tried = true;
        bool uv2 = mi.Mesh is ArrayMesh am && (am.SurfaceGetFormat(0) & Mesh.ArrayFormat.FormatTexUV2) != 0;
        if (builder.GlowMaterial(mesh, uv2, -1f, field: false, chaos: true) is not { } gm) return null;
        twin = new MeshInstance3D { Name = mi.Name + "_chaosglow", Mesh = mi.Mesh, Layers = GlowPass.Bit, MaterialOverride = gm,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        mi.AddChild(twin);
        if (mi.Skin != null) { twin.Skin = mi.Skin; twin.Skeleton = "../.."; }
        return twin;
    }
}
