using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui;

namespace Ms2Godot.Look;

/// <summary>
/// The face texture layer on a built pc (<see cref="IFaceTexture"/>): the target is the first shown mesh of that name in
/// the model (FindTargetNode, textureanimationdata.cpp:440-463; the port keeps a replaced body mesh hidden where the client
/// swaps it out, so a hidden one is passed over). A frame sets the base texture and the dye control map on a copy of the
/// mesh's character material (TextureLayer::vfunc_19, :599-606: SetBaseTexture and shader map 0, which the port's material
/// holds as its control map, ColorOverrideMapIndex 0); a mesh with no control map keeps none. Stop puts the mesh's own
/// material back (TextureLayerManager::RemoveLayer).
/// </summary>
public sealed class FaceTexture : IFaceTexture
{
    /// <summary>The face item a built look plays its emotions from (PC::vfunc_69), set on the visual by PlayerLook.</summary>
    public const string FaceItemMeta = "face_item";

    readonly Node3D visual;
    readonly ModelBuilder builder;
    MeshInstance3D target;              // the mesh found for targetName
    string targetName;
    MeshInstance3D layerOn;             // the mesh the layer is on, with its material before it and the layer's copy
    Material before;
    ShaderMaterial layer;
    bool hasCtrl;

    public FaceTexture(Node3D visual, ModelBuilder builder) { this.visual = visual; this.builder = builder; }

    MeshInstance3D Find(string name)
    {
        if (target != null && targetName == name && GodotObject.IsInstanceValid(target) && target.Visible) return target;
        targetName = name;
        target = null;
        foreach (var n in visual.FindChildren("*", "MeshInstance3D", true, false))
            if (n is MeshInstance3D mi && mi.Visible
                && (mi.HasMeta("mesh") ? (string)mi.GetMeta("mesh") : mi.Name.ToString()) == name) { target = mi; break; }
        return target;
    }

    public bool Has(string name) => Find(name) != null;

    public bool Show(string name, string baseEntry, string controlEntry)
    {
        var mi = Find(name);
        if (mi == null) return false;
        // the layer's material is made once per mesh and kept while the mesh's own material stays as it was
        var cur = mi.GetSurfaceOverrideMaterial(0);
        if (layerOn != mi || layer == null || (cur != layer && cur != before))
        {
            if (mi.GetActiveMaterial(0) is not ShaderMaterial sm || !ModelBuilder.IsCharacterShader(sm.Shader)) return false;
            Stop();
            before = cur;
            layer = (ShaderMaterial)sm.Duplicate();
            hasCtrl = sm.GetShaderParameter(ShaderParam.HasCtrl).AsBool();
            layerOn = mi;
        }
        if (mi.GetSurfaceOverrideMaterial(0) != layer) mi.SetSurfaceOverrideMaterial(0, layer);
        var tex = builder.TextureEntry(EmotionTextureFrame.Archive, baseEntry);
        layer.SetShaderParameter(ShaderParam.HasTex, tex != null);
        if (tex != null) layer.SetShaderParameter(ShaderParam.BaseTex, tex);
        if (hasCtrl)
        {
            var ctrl = controlEntry.Length == 0 ? null : builder.TextureEntry(EmotionTextureFrame.Archive, controlEntry);
            layer.SetShaderParameter(ShaderParam.HasCtrl, ctrl != null);
            if (ctrl != null) layer.SetShaderParameter(ShaderParam.CtrlTex, ctrl);
        }
        return true;
    }

    public void Stop()
    {
        if (layerOn != null && GodotObject.IsInstanceValid(layerOn) && layerOn.GetSurfaceOverrideMaterial(0) == layer)
            layerOn.SetSurfaceOverrideMaterial(0, before);
    }

    /// <summary>The own pc's face emotion on a look PlayerLook built: the group of its face item (the item table's
    /// emotion/item file), null when the look has no face item or the data no group for it.</summary>
    public static (FaceEmotion face, EmotionGroup group) For(Node3D visual, ModelBuilder builder, PackSet packs, int gender)
    {
        int worn = visual.HasMeta(FaceItemMeta) ? (int)visual.GetMeta(FaceItemMeta) : 0;
        int id = FaceEmotion.FaceItem(worn, worn != 0, packs, gender);
        var group = id > 0 ? new EmotionTable(packs, EmotionTable.Item).Group((uint)id) : null;
        return (new FaceEmotion(new FaceTexture(visual, builder)), group);
    }
}
