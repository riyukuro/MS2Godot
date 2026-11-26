using System.Collections.Generic;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/mesh_detail_opacity.tscn, game data) of the effect mesh shader's detail alpha: a mesh whose
/// block names one of the eight MS2StandardMaterial names multiplies its opacity by the detail map's alpha, any other
/// mesh takes the detail colour only (EffectMesh.DetailOpacity; MS2MaterialLibrary::CreateMaterial,
/// ms2materiallibrary.cpp:486-500; NiStandardMaterial::HandleDetailMap, NiStandardMaterial.cpp:4765-4774). On four effects
/// with detail maps that have alpha (an unnamed material, MS2StandardMaterial, MS2CharacterMaterial and an
/// EffectUVDistortion name), every drawn mesh with a detail map sets detail_opacity exactly when its material is one of
/// the eight (counted against the meshes' materials), and each effect has at least one mesh of its kind. Needs MS2_DATA
/// and MS2_NIFXML (or local.cfg). Quits with 0 when all hold, else 1.
/// </summary>
public partial class MeshDetailOpacityCheck : Node3D
{
    const string Archive = "Resource/Model/Effect";
    static readonly (string nif, bool standard)[] Effects =
    {
        ("npc/skill/yetitwoheadwhite/eff_yetitwoheadwhite_facetrap.nif", false),
        ("bg/common/eff_bossportal_a01_opened.nif", true),
        ("bg/common/eff_ugc_tank_01_hologram.nif", true),
        ("additional/etc/eff_additional_jumpup_01_keep.nif", false),
    };
    static readonly StringName HasDetail = "has_detail", DetailOpacity = "detail_opacity";

    public override void _Ready()
    {
        var fails = new List<string>();
        var packs = PackSet.Shared(Settings.DataDir);
        var spec = ModelBuilder.SharedSpec(Settings.NifXml);
        // effect textures come from the effect archive as well, as a field's builder takes them (FieldBuilder)
        var builder = new ModelBuilder(spec, packs) { TextureArchives = new[] { ModelBuilder.TextureArchive, Archive } };
        foreach (var (nif, standard) in Effects)
        {
            var sc = EffectScene.Load(NifFile.Load(spec, packs.Read(Archive, nif)));
            // the meshes whose detail texture loads, by whether their material is one of the eight
            int wantOn = 0, wantOff = 0;
            foreach (var em in sc.Meshes)
                if (em.Mesh.Textures.TryGetValue("detail", out var dn) && builder.Texture(dn) != null)
                {
                    if (ModelData.IsStandardMaterial(em.Mesh.Material)) wantOn++; else wantOff++;
                }
            var fx = new EffectInstance(sc, builder, (float)sc.Duration, 1);
            AddChild(fx);
            int on = 0, off = 0;
            var stack = new Stack<Node>();
            stack.Push(fx);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                for (int i = 0; i < n.GetChildCount(); i++) stack.Push(n.GetChild(i));
                if (n is not MeshInstance3D mi || mi.MaterialOverride is not ShaderMaterial mat || !(bool)mat.GetShaderParameter(HasDetail)) continue;
                var v = mat.GetShaderParameter(DetailOpacity);
                if (v.VariantType == Variant.Type.Bool && (bool)v) on++; else off++;
            }
            if (on != wantOn || off != wantOff) fails.Add($"{nif}: detail meshes with detail_opacity {on}, without {off}; want {wantOn} and {wantOff}");
            if ((standard ? wantOn : wantOff) == 0) fails.Add($"{nif}: no detail mesh under {(standard ? "a standard" : "another")} material");
            GD.Print($"mesh detail opacity: {nif}: detail meshes with the detail alpha {on}, without {off}");
            fx.QueueFree();
        }
        foreach (var f in fails) GD.PrintErr("mesh detail opacity: FAIL " + f);
        GD.Print(fails.Count == 0 ? "mesh detail opacity: ok" : $"mesh detail opacity: {fails.Count} FAILED");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
