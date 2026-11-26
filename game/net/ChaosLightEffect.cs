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
/// - A mesh shows its top layer's values through character_lit's chaos instance uniforms; the chaos material has no glow
///   map, so the mesh's glow pass twin (ModelBuilder.AddGlowTwin, its sibling "&lt;name&gt;_glow") is hidden meanwhile.
/// </summary>
public static class ChaosLayers
{
    /// <summary>The mesh takes chaos light layers (<see cref="ChaosLightEffect.TakesLayer"/>, set by ModelBuilder.Attached).</summary>
    public static readonly StringName Meta = "ms2_chaos_light";

    public sealed class Layer
    {
        public int Id;
        public MeshInstance3D Mesh;
        public ChaosLightValues Values;
        public bool IsBase;
        /// <summary>On the base layer: the mesh's glow pass twin, or null.</summary>
        public MeshInstance3D Glow;
    }

    static readonly Dictionary<MeshInstance3D, List<Layer>> entries = new();

    /// <summary>Meshes holding a chaos layer now.</summary>
    public static int Meshes => entries.Count;

    /// <summary>The layer the mesh shows (null: none, the mesh's own material).</summary>
    public static Layer Top(MeshInstance3D mesh) =>
        entries.TryGetValue(mesh, out var list) && !list[^1].IsBase ? list[^1] : null;

    public static Layer Add(MeshInstance3D mesh, int id, ChaosLightValues values)
    {
        if (!entries.TryGetValue(mesh, out var list))
            entries[mesh] = list = new List<Layer>(2)
                { new Layer { Id = -1, Mesh = mesh, IsBase = true, Glow = mesh.GetParent()?.GetNodeOrNull<MeshInstance3D>(mesh.Name + "_glow") } };
        var layer = new Layer { Id = id, Mesh = mesh, Values = values };
        if (list[^1].Id <= id)
        {
            list.Add(layer);
            Apply(layer);
        }
        else
        {
            for (int i = 0; i < list.Count; i++)
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
        var glow = entries.TryGetValue(mi, out var list) ? list[0].Glow : null;
        if (glow != null && GodotObject.IsInstanceValid(glow)) glow.Visible = layer.IsBase;
        if (layer.IsBase) { mi.SetInstanceShaderParameter(ShaderParam.ChaosOn, 0f); return; }
        var v = layer.Values;
        mi.SetInstanceShaderParameter(ShaderParam.ChaosOn, 1f);
        mi.SetInstanceShaderParameter(ShaderParam.ChaosColor, new Vector3(v.R, v.G, v.B));
        mi.SetInstanceShaderParameter(ShaderParam.ChaosBoost, v.Boost);
        mi.SetInstanceShaderParameter(ShaderParam.ChaosExponent, v.Exponent);
        mi.SetInstanceShaderParameter(ShaderParam.ChaosDimmer, v.Dimmer);
    }
}
