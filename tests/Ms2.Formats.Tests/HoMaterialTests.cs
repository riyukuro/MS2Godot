using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// The _HO effect techniques and the alpha boost swap.
/// - The client ships two effect files for the _HO names (Resource/Shaders.m2d fx/effectuvdistortionmask_ho.fxo and
///   fx/effectdissolve_ho.fxo); each technique is a shader of that name (NiD3DXEffectParser.cpp:504-505), so a block that
///   names one draws through it (<see cref="EffectMesh.Ho"/>, <see cref="HoTechnique"/>), a mesh or a particle system alike.
/// - When the effect loads, a block whose NiMaterialProperty alpha is above 1, or whose alpha controller has a key above
///   1, is given MS2AlphaBoostMaterial in place of the material it names (unit_141567000.cpp:171-182, :853-865;
///   <see cref="ModelData.AlphaBoost"/>): a standard material, so it takes the detail alpha and draws no _HO technique.
/// </summary>
public class HoMaterialTests
{
    const string Archive = "Resource/Model/Effect";

    [Test]
    public void TheNineTechniquesAndTheirTerms()
    {
        // (name, dissolve, glow, wrap, c0 times the vertex colour, c1 times it), as the compiled passes read them
        var want = new (string, bool, bool, bool, bool, bool)[]
        {
            ("EffectUVDistortionMask_HO", false, false, false, false, false),
            ("EffectUVDistortionMaskGlow_HO", false, true, false, false, false),
            ("EffectUVDistortionMaskParticle_HO", false, false, false, true, false),
            ("EffectUVDistortionMaskParticleGlow_HO", false, true, false, true, false),
            ("EffectUVDistortionMaskParticleGlowLife_HO", false, true, false, true, true),
            ("EffectUVDistortionMaskWrap_HO", false, false, true, false, false),
            ("EffectUVDistortionMaskGlowWrap_HO", false, true, true, false, false),
            ("EffectDissolve_HO", true, false, false, false, false),
            ("EffectDissolveParticle_HO", true, false, false, true, false),
        };
        Assert.That(HoTechnique.All.Select(h => h.Name), Is.EqualTo(want.Select(w => w.Item1)));
        foreach (var (name, dissolve, glow, wrap, vc, glowVc) in want)
        {
            var h = HoTechnique.Of(name);
            Assert.That(h, Is.Not.Null, name);
            Assert.That((h.Dissolve, h.Glow, h.Wrap, h.VertexColor, h.GlowVertexColor), Is.EqualTo((dissolve, glow, wrap, vc, glowVc)), name);
        }
        Assert.That(HoTechnique.Of("MS2StandardMaterial"), Is.Null);
        Assert.That(HoTechnique.Of(null), Is.Null);
        Assert.That(HoTechnique.Of("EffectDissolve_ho"), Is.Null);
    }

    [Test]
    public void JumpupDrawsThroughItsTechniques()
    {
        var (packs, spec) = NifEnv.Open();
        var sc = EffectScene.Load(NifFile.Load(spec, packs.Read(Archive, "additional/etc/eff_additional_jumpup_01_keep.nif")));
        var ho = sc.Meshes.Where(m => m.Mesh.Material == "EffectUVDistortionMaskGlowWrap_HO").ToList();
        Assert.That(ho, Is.Not.Empty);
        Assert.That(ho.All(m => m.Ho?.Name == "EffectUVDistortionMaskGlowWrap_HO" && !m.AlphaBoost && !m.DetailOpacity));
        var sys = sc.Systems.Where(s => s.Material.Mesh.Material == "EffectUVDistortionMaskParticleGlowLife_HO").ToList();
        Assert.That(sys, Is.Not.Empty);
        Assert.That(sys.All(s => s.Material.Ho?.Name == "EffectUVDistortionMaskParticleGlowLife_HO" && !s.DetailOpacity));
    }

    [Test]
    public void BoostedBlocksDrawTheBoostMaterial()
    {
        var (packs, spec) = NifEnv.Open();
        // a dissolve mesh whose material alpha is above 1
        var knight = EffectScene.Load(NifFile.Load(spec, packs.Read(Archive, "ch/skill/knight/eff_knight_shieldupper_cast_01_a.nif")));
        var dm = knight.Meshes.Where(m => m.Mesh.Material == "EffectDissolve_HO").ToList();
        Assert.That(dm.Any(m => m.AlphaBoost && m.Mesh.MaterialAlpha > 1));
        Assert.That(dm.Where(m => m.AlphaBoost).All(m => m.Ho == null && m.DetailOpacity && m.ActiveMaterial == "MS2AlphaBoostMaterial"));
        // a mask particle system boosted by its material alpha
        var bean = EffectScene.Load(NifFile.Load(spec, packs.Read(Archive, "npc/skill/carparadeblackbean/eff_carparadeblackbean_stunbroken_b_electric_01.nif")));
        var bs = bean.Systems.Where(s => s.Material.Mesh.Material == "EffectUVDistortionMaskParticle_HO" && s.Material.AlphaBoost).ToList();
        Assert.That(bs, Is.Not.Empty);
        Assert.That(bs.All(s => s.Material.Ho == null && s.DetailOpacity));
        // unnamed meshes boosted by an alpha key above 1 take the detail alpha
        var lamp = EffectScene.Load(NifFile.Load(spec, packs.Read(Archive, "bg/common/eff_co_lapentalight_01.nif")));
        var lm = lamp.Meshes.Where(m => m.Mesh.Material == null && m.AlphaBoost && m.Mesh.Maps.ContainsKey("detail")).ToList();
        Assert.That(lm, Is.Not.Empty);
        Assert.That(lm.All(m => m.DetailOpacity && m.Mesh.MaterialAlpha <= 1));
    }

    [Test]
    public void TheBoostFollowsTheAlphaAndTheFirstControllersKeys()
    {
        var (packs, spec) = NifEnv.Open();
        // the yeti face disc that names no material and whose alpha keys stay at or below 1; an alpha of 2 swaps it
        var n = NifFile.Load(spec, packs.Read(Archive, "npc/skill/yetitwoheadwhite/eff_yetitwoheadwhite_facetrap.nif"));
        var face = EffectScene.Load(n).Meshes.Single(m => m.Mesh.Name == "PolyMesh#103");
        Assert.That(face.Mesh.Material, Is.Null);
        Assert.That(face.AlphaBoost || face.DetailOpacity, Is.False);
        var prop = MaterialProperty(n, face.Mesh.Block);
        var alpha = prop.Float("Alpha");
        prop["Alpha"] = 2f;
        var edited = EffectScene.Load(n).Meshes.Single(m => m.Mesh.Block == face.Mesh.Block);
        Assert.That(edited.AlphaBoost && edited.DetailOpacity && edited.ActiveMaterial == "MS2AlphaBoostMaterial");
        prop["Alpha"] = alpha;
        Assert.That(EffectScene.Load(n).Meshes.Single(m => m.Mesh.Block == face.Mesh.Block).AlphaBoost, Is.False);

        // jumpup's _HO meshes: an alpha controller key above 1 swaps the block, and it then draws no technique
        var j = NifFile.Load(spec, packs.Read(Archive, "additional/etc/eff_additional_jumpup_01_keep.nif"));
        var keyed = EffectScene.Load(j).Meshes.FirstOrDefault(m => m.Ho != null && Keys(j, m.Mesh.Block) is { Length: > 0 });
        Assert.That(keyed, Is.Not.Null, "an _HO mesh with alpha keys");
        var keys = Keys(j, keyed.Mesh.Block);
        Assert.That(keys.All(k => k.Float("Value") <= 1f));
        var old = keys[^1].Float("Value");
        keys[^1]["Value"] = 1.5f;
        var boosted = EffectScene.Load(j).Meshes.Single(m => m.Mesh.Block == keyed.Mesh.Block);
        Assert.That(boosted.AlphaBoost && boosted.Ho == null && boosted.DetailOpacity);
        keys[^1]["Value"] = old;
        Assert.That(EffectScene.Load(j).Meshes.Single(m => m.Mesh.Block == keyed.Mesh.Block).Ho, Is.Not.Null);
    }

    static NifStruct MaterialProperty(NifFile n, int block) =>
        n.Blocks[block].Ints("Properties").Where(p => p >= 0).Select(p => n.Blocks[(int)p]).First(p => p.Type == "NiMaterialProperty");

    /// <summary>The keys of the block's NiMaterialProperty's first controller when it is an NiAlphaController over float
    /// data, else null.</summary>
    static NifStruct[] Keys(NifFile n, int block)
    {
        var B = n.Blocks;
        var prop = B[block].Ints("Properties").Where(p => p >= 0).Select(p => B[(int)p]).FirstOrDefault(p => p.Type == "NiMaterialProperty");
        if (prop == null || prop.IntOr("Controller", -1) is var c && (c < 0 || B[(int)c].Type != "NiAlphaController")) return null;
        long ip = B[(int)c].IntOr("Interpolator", -1);
        if (ip < 0 || B[(int)ip].Type != "NiFloatInterpolator" || B[(int)ip].IntOr("Data", -1) is var d && d < 0) return null;
        return B[(int)d].Struct("Data").Structs("Keys");
    }

    /// <summary>Every effect nif: a block draws the _HO technique it names unless it is boosted, a boosted block takes the
    /// detail alpha, and every _HO name in the data is one of the nine. Counts the blocks per technique and the boosted ones.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryBlockDrawsItsTechniqueOrTheBoost(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} not set");
        using var packs = new PackSet(dir);
        var spec = new NifSpec(NifEnv.NifXml());
        var count = new SortedDictionary<string, int>(StringComparer.Ordinal);
        void Add(string k) => count[k] = count.GetValueOrDefault(k) + 1;
        var bad = new List<string>();
        int nifs = 0;
        foreach (var entry in packs.List(Archive).Where(e => e.EndsWith(".nif", StringComparison.Ordinal)).OrderBy(e => e, StringComparer.Ordinal))
        {
            NifFile nif; EffectScene sc;
            try { nif = NifFile.Load(spec, packs.Read(Archive, entry)); sc = EffectScene.Load(nif); }
            catch (Exception) { continue; }
            nifs++;
            var items = sc.Meshes.Select(m => ("mesh", m, m.Mesh.Name)).Concat(sc.Systems.Select(s => ("system", s.Material, s.Name)));
            foreach (var (kind, em, name) in items)
            {
                var mat = em.Mesh.Material;
                bool boost = Boosted(nif, em.Mesh.Block);
                if (em.AlphaBoost != boost) bad.Add($"{entry} {kind} {name}: AlphaBoost {em.AlphaBoost}, the block {boost}");
                if (boost)
                {
                    Add($"{kind} boosted, named {(mat == null ? "(none)" : mat.EndsWith("_HO", StringComparison.Ordinal) ? "_HO" : ModelData.IsStandardMaterial(mat) ? "standard" : "other")}");
                    if (em.Ho != null || !em.DetailOpacity) bad.Add($"{entry} {kind} {name}: boosted, Ho {em.Ho?.Name} DetailOpacity {em.DetailOpacity}");
                    continue;
                }
                if (mat == null || !mat.EndsWith("_HO", StringComparison.Ordinal)) { if (em.Ho != null) bad.Add($"{entry} {kind} {name}: {mat} has Ho"); continue; }
                Add($"{kind} {mat}");
                if (em.Ho?.Name != mat) bad.Add($"{entry} {kind} {name}: {mat} draws {em.Ho?.Name}");
                if (em.DetailOpacity) bad.Add($"{entry} {kind} {name}: {mat} DetailOpacity");
                foreach (var slot in em.Ho.Maps)
                {
                    if (em.Mesh.Textures.ContainsKey(slot)) { if (em.Lent?.ContainsKey(slot) == true) bad.Add($"{entry} {kind} {name}: lent its own {slot}"); continue; }
                    Add($"{kind} lacks {slot}, {(em.Lent?.ContainsKey(slot) == true ? "lent" : "none")}");
                }
            }
        }
        TestContext.Out.WriteLine($"{env}: {nifs} nifs");
        foreach (var (k, c) in count) TestContext.Out.WriteLine($"  {k}: {c}");
        Assert.That(bad, Is.Empty, string.Join("\n", bad.Take(20)));
        Assert.That(count.Keys.Count(k => k.Contains("_HO", StringComparison.Ordinal) && !k.Contains("boosted", StringComparison.Ordinal)), Is.GreaterThanOrEqualTo(9));
    }

    /// <summary>A map the block lacks reads what the shared effect was last given for that map (NiD3DXEffectParameter.cpp:862-881
    /// sets nothing; NiD3DXEffectTechnique.cpp:66 shares one effect per file): the texture the effect's other blocks of that
    /// file carry when they all carry the same one, else nothing.</summary>
    [Test]
    public void AMissingMapTakesTheTextureTheEffectsOtherBlocksCarry()
    {
        var (packs, spec) = NifEnv.Open();
        var jump = EffectScene.Load(NifFile.Load(spec, packs.Read(Archive, "additional/etc/eff_additional_jumpup_01_keep.nif")));
        var life = jump.Systems.Where(s => s.Emitters.Count > 0 && s.Material.Ho?.Name == "EffectUVDistortionMaskParticleGlowLife_HO").ToList();
        Assert.That(life, Is.Not.Empty);
        foreach (var s in life)
        {
            Assert.That(s.Material.Mesh.Textures.ContainsKey("glow"), Is.False, s.Name);
            Assert.That(s.Material.Lent?.GetValueOrDefault("glow"), Is.EqualTo("LoopFractal_003.dds").IgnoreCase, s.Name);
        }
        // the blocks that carry a map lend nothing for it
        foreach (var m in jump.Meshes.Where(m => m.Ho != null))
            foreach (var slot in m.Ho.Maps.Where(m.Mesh.Textures.ContainsKey))
                Assert.That(m.Lent?.ContainsKey(slot) == true, Is.False, $"{m.Mesh.Name} {slot}");

        // two different glow textures in the effect: which one the draw reads is unknown, so none is lent
        var giant = EffectScene.Load(NifFile.Load(spec, packs.Read(Archive, "npc/skill/gigantikahead/eff_gigantikahead_attack_01_c.nif")));
        var bare = giant.Systems.Where(s => s.Emitters.Count > 0 && s.Material.Ho != null && !s.Material.Mesh.Textures.ContainsKey("glow")).ToList();
        Assert.That(bare, Is.Not.Empty);
        Assert.That(bare.All(s => s.Material.Lent?.ContainsKey("glow") != true));
    }

    /// <summary>The swap read here from the block on its own: its NiMaterialProperty's alpha above 1, or a key above 1 on
    /// that property's first controller when it is an NiAlphaController over float data.</summary>
    internal static bool Boosted(NifFile n, int block)
    {
        var prop = n.Blocks[block].Ints("Properties").Where(p => p >= 0).Select(p => n.Blocks[(int)p]).FirstOrDefault(p => p.Type == "NiMaterialProperty");
        if (prop == null) return false;
        return prop.Float("Alpha") > 1f || Keys(n, block)?.Any(k => k.Float("Value") > 1f) == true;
    }
}
