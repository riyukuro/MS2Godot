using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/mesh_detail_opacity.tscn, game data) of the material each effect mesh and particle system
/// draws through:
/// - a block that names an _HO technique draws that technique's shader (EffectMesh.Ho; the client's effect shader library,
///   Resource/Shaders.m2d fx/effectuvdistortionmask_ho.fxo and fx/effectdissolve_ho.fxo), which reads the detail map as a
///   red mask;
/// - a block whose material alpha or alpha key is above 1 draws MS2AlphaBoostMaterial instead (EffectMesh.AlphaBoost;
///   PrepareLoadedNif, unit_141567000.cpp:171-182, :853-865), the standard shader with alpha_boost;
/// - any other block with a detail map sets detail_opacity exactly when its material is one of the eight MS2StandardMaterial
///   names (EffectMesh.DetailOpacity; ms2materiallibrary.cpp:486-500; NiStandardMaterial.cpp:4765-4774).
/// On effects with an unnamed material, MS2StandardMaterial, MS2CharacterMaterial, mask and dissolve techniques on meshes
/// and on systems, and boosted meshes and systems, the drawn nodes are counted per technique, per detail alpha and per
/// boost against the scene's blocks, each effect has at least one block of its kind, and each kind is on some effect in
/// the data (the dissolve effects are not in every region's). An _HO block's maps are counted too: each map its technique
/// reads is set from the block's own texture or, when it lacks one, from the texture the effect lends it (EffectMesh.Lent). Needs MS2_DATA and MS2_NIFXML (or
/// local.cfg). Quits with 0 when all hold, else 1.
/// </summary>
public partial class MeshDetailOpacityCheck : Node3D
{
    const string Archive = "Resource/Model/Effect";
    enum Kind { Default, Standard, Ho, Boost }
    static readonly (string nif, Kind kind)[] Effects =
    {
        ("npc/skill/yetitwoheadwhite/eff_yetitwoheadwhite_facetrap.nif", Kind.Default),
        ("bg/common/eff_bossportal_a01_opened.nif", Kind.Standard),
        ("bg/common/eff_ugc_tank_01_hologram.nif", Kind.Standard),
        ("additional/etc/eff_additional_jumpup_01_keep.nif", Kind.Ho),
        ("additional/etc/eff_additional_jumpup_01_stop.nif", Kind.Ho),
        ("npc/skill/blueluke/blueluke_01_attack.nif", Kind.Ho),
        ("npc/skill/primadonna/eff_primadonna_attack_e_explosion.nif", Kind.Ho),
        ("ch/skill/knight/eff_knight_shieldupper_cast_01_a.nif", Kind.Boost),
        ("npc/skill/carparadeblackbean/eff_carparadeblackbean_stunbroken_b_electric_01.nif", Kind.Boost),
        ("bg/common/eff_co_lapentalight_01.nif", Kind.Boost),
    };
    static readonly StringName HasDetail = "has_detail", DetailOpacity = "detail_opacity", AlphaBoost = "alpha_boost";

    public override void _Ready()
    {
        var fails = new List<string>();
        var packs = PackSet.Shared(Settings.DataDir);
        var spec = ModelBuilder.SharedSpec(Settings.NifXml);
        // effect textures come from the effect archive as well, as a field's builder takes them (FieldBuilder)
        var builder = new ModelBuilder(spec, packs) { TextureArchives = new[] { ModelBuilder.TextureArchive, Archive } };
        var seen = new HashSet<Kind>();
        foreach (var (nif, kind) in Effects)
        {
            // the dissolve effects are not in every region's data; each kind must still be on some effect there
            if (!packs.Exists(Archive, nif)) { GD.Print($"mesh detail opacity: {nif}: not in this data"); continue; }
            try { Check(nif, kind); }
            catch (Exception e) { fails.Add($"{nif}: {e.GetType().Name} {e.Message}"); }
        }
        foreach (var k in Enum.GetValues<Kind>()) if (!seen.Contains(k)) fails.Add($"no {k} block on any effect");
        foreach (var f in fails) GD.PrintErr("mesh detail opacity: FAIL " + f);
        GD.Print(fails.Count == 0 ? "mesh detail opacity: ok" : $"mesh detail opacity: {fails.Count} FAILED");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);

        void Check(string nif, Kind kind)
        {
            var sc = EffectScene.Load(NifFile.Load(spec, packs.Read(Archive, nif)));
            // what the blocks want: the technique per mesh / system, else the detail alpha (when the detail loads) and the boost
            var want = new SortedDictionary<string, int>(StringComparer.Ordinal);
            void Want(string k) => want[k] = want.GetValueOrDefault(k) + 1;
            void Block(string what, EffectMesh em)
            {
                if (em.Ho != null)
                {
                    Want($"{what} {em.Ho.Name}");
                    // each map the technique reads: the block's own, else the one the effect lends it
                    foreach (var slot in em.Ho.Maps)
                        if ((em.Mesh.Textures.TryGetValue(slot, out var f) || em.Lent?.TryGetValue(slot, out f) == true) && builder.Texture(f) != null)
                            Want($"{what} {slot} map");
                    return;
                }
                if (em.AlphaBoost) Want($"{what} boost");
                if (em.Mesh.Textures.TryGetValue("detail", out var dn) && builder.Texture(dn) != null)
                    Want($"{what} detail {(em.DetailOpacity ? "alpha" : "colour")}");
            }
            foreach (var em in sc.Meshes) Block("mesh", em);
            foreach (var d in sc.Systems) if (d.Emitters.Count > 0) Block("system", d.Material);
            var fx = new EffectInstance(sc, builder, (float)sc.Duration, 1);
            AddChild(fx);
            var got = new SortedDictionary<string, int>(StringComparer.Ordinal);
            void Got(string k) => got[k] = got.GetValueOrDefault(k) + 1;
            var stack = new Stack<Node>();
            stack.Push(fx);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                for (int i = 0; i < n.GetChildCount(); i++) stack.Push(n.GetChild(i));
                var (what, material) = n switch
                {
                    MeshInstance3D mi => ("mesh", mi.MaterialOverride),
                    MultiMeshInstance3D mm => ("system", mm.Multimesh?.Mesh?.SurfaceGetMaterial(0)),
                    _ => (null, null),
                };
                if (material is not ShaderMaterial mat) continue;
                var technique = mat.Shader?.ResourceName;
                if (HoTechnique.Of(technique) is { } h)
                {
                    Got($"{what} {technique}");
                    foreach (var slot in h.Maps)
                    {
                        var v = mat.GetShaderParameter(slot == "base" ? "tex" : slot + "_tex");
                        if (v.VariantType == Variant.Type.Object && v.AsGodotObject() is Texture2D) Got($"{what} {slot} map");
                    }
                    continue;
                }
                if (On(mat, AlphaBoost)) Got($"{what} boost");
                if (On(mat, HasDetail)) Got($"{what} detail {(On(mat, DetailOpacity) ? "alpha" : "colour")}");
            }
            string Show(SortedDictionary<string, int> c) => string.Join(", ", c.Select(x => $"{x.Key} {x.Value}"));
            if (Show(got) != Show(want)) fails.Add($"{nif}: drawn {Show(got)}; the blocks want {Show(want)}");
            bool has = kind switch
            {
                Kind.Default => want.ContainsKey("mesh detail colour"),
                Kind.Standard => want.ContainsKey("mesh detail alpha"),
                Kind.Ho => want.Keys.Any(k => k.EndsWith("_HO", StringComparison.Ordinal)),
                _ => want.Keys.Any(k => k.EndsWith(" boost", StringComparison.Ordinal)),
            };
            if (has) seen.Add(kind); else fails.Add($"{nif}: no {kind} block");
            GD.Print($"mesh detail opacity: {nif}: {Show(got)}");
            fx.QueueFree();
        }
    }

    static bool On(ShaderMaterial mat, StringName p)
    {
        var v = mat.GetShaderParameter(p);
        return v.VariantType == Variant.Type.Bool && (bool)v;
    }
}
