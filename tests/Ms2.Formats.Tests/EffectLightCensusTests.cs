using System.Xml.Linq;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// How far the effect light forms (<see cref="EffectMesh.LightForm"/>, <see cref="EffectMesh.Light"/>) move each block's
/// shading from the old one, under the lights of a day map and of a dark map, and the parts whose root takes no field
/// light (&lt;attachInfo useLighting="false"&gt;, <see cref="EffectPart.UseLighting"/>).
/// The old shading took the field's ambient light alone: emissive + diffuse + ambient x A, a vertex colour in the diffuse
/// place at full weight and in the ambient place times A (A = ms2_ambient). The light forms take the ambient accumulator La
/// and the diffuse accumulator Ld of <see cref="EffectMesh.Light"/> instead: emissive + ambient x La + diffuse x Ld.
/// </summary>
public class EffectLightCensusTests
{
    const string Archive = "Resource/Model/Effect";

    [Test]
    public void AttachInfoUseLightingIsRead()
    {
        var parts = EffectTable.Parse(XDocument.Parse(
            "<ms2><effect type=\"nif\" filePath=\"a.nif\"/>" +
            "<effect type=\"nif\" filePath=\"b.nif\"><attachInfo useLighting=\"false\"/></effect>" +
            "<effect type=\"nif\" filePath=\"c.nif\"><attachInfo useLighting=\"0\" useShadow=\"false\"/></effect>" +
            "<effect type=\"nif\" filePath=\"d.nif\"><attachInfo useLighting=\"true\"/></effect>" +
            "<effect type=\"nif\" filePath=\"e.nif\"><attachInfo attachToNode=\"true\"/></effect></ms2>"));
        Assert.That(parts.Select(p => p.UseLighting), Is.EqualTo(new[] { true, false, false, true, true }));
        Assert.That(EffectMesh.PartLightForm(2, true), Is.EqualTo(2));
        Assert.That(EffectMesh.PartLightForm(1, false), Is.EqualTo(0));
        Assert.That(EffectMesh.PartLightForm(2, false), Is.EqualTo(0));
        Assert.That(EffectMesh.PartLightForm(0, true), Is.EqualTo(0));
        // a block with the lights off keeps its emissive alone, as LIGHTING_E
        Assert.That(EffectMesh.Light(EffectMesh.PartLightForm(2, false), true, 1, 0.88, 0.88, 0.1), Is.EqualTo((0.0, 0.0)));
    }

    /// <summary>The lights Field.SetSunAndAmbient gives the effect shader for <paramref name="field"/> without HDR (k = 1.1):
    /// the ambient light's colour x Dimmer, the directional light's colour and ambient colour x its Dimmer, and its +X axis.</summary>
    internal readonly record struct Lights(string Field, double[] Ambient, double[] Sun, double[] SunAmbient, Vec3 Dir);

    internal static Lights FieldLights(PackSet packs, FlatLibrary flats, string field)
    {
        const double k = 1.1;
        double[] amb = { 1, 1, 1 }, sun = { 1, 1, 1 }, sa = { 0, 0, 0 };
        double ad = 0.8, sd = 0.8;
        var dir = new Vec3(0, 0, -1);
        foreach (var e in FieldMap.Load(packs, flats, field).Entities)
        {
            if (e.ModelName.StartsWith("MS2AmbientLight", StringComparison.Ordinal)) { amb = e.Numbers("AmbientColor") ?? amb; ad = e.Number("Dimmer", 1); }
            else if (e.ModelName.StartsWith("MS2DirectionalLight", StringComparison.Ordinal))
            {
                sun = e.Numbers("DiffuseColor") ?? sun; sa = e.Numbers("AmbientColor") ?? sa; sd = e.Number("Dimmer", 1);
                dir = e.Transform.Apply(new Vec3(1, 0, 0)) - e.Transform.Apply(new Vec3(0, 0, 0));
            }
        }
        return new Lights(field, amb.Take(3).Select(c => c * ad * k).ToArray(), sun.Take(3).Select(c => c * sd * k).ToArray(), sa.Take(3).Select(c => c * sd * k).ToArray(), dir);
    }

    /// <summary>One channel's coefficient (the texture colour's factor) at time 0, white vertex colours.</summary>
    static double Old(EffectMesh em, double emi, double dif, double amb, double a)
    {
        var vc = em.VertexColors;
        return emi + dif + amb * a + (vc.Emissive ? 1 : 0) + (vc.Diffuse ? 1 : 0) + (vc.Ambient ? a : 0);
    }

    static double New(EffectMesh em, int form, bool normals, double ndl, double emi, double dif, double amb, double a, double s, double sa)
    {
        var (la, ld) = EffectMesh.Light(form, normals, ndl, a, s, sa);
        var vc = em.VertexColors;
        return emi + amb * la + dif * ld + (vc.Emissive ? 1 : 0) + (vc.Diffuse ? ld : 0) + (vc.Ambient ? la : 0);
    }

    /// <summary>The change of one block: the largest channel change over the largest old channel.</summary>
    internal static double Change(EffectMesh em, bool normals, double ndl, Lights l, out double oldMax, out double newMax)
    {
        double[] lit = new double[3], dif = new double[3], amb = new double[3];
        em.Shade(0, lit, dif, amb);
        double worst = 0; oldMax = 0; newMax = 0;
        for (int c = 0; c < 3; c++)
        {
            double o = Old(em, lit[c], dif[c], amb[c], l.Ambient[c]);
            double n = New(em, em.LightForm, normals, ndl, lit[c], dif[c], amb[c], l.Ambient[c], l.Sun[c], l.SunAmbient[c]);
            oldMax = Math.Max(oldMax, o); newMax = Math.Max(newMax, n);
            worst = Math.Max(worst, Math.Abs(n - o));
        }
        return oldMax > 1e-6 ? worst / oldMax : worst > 1e-6 ? double.PositiveInfinity : 0;
    }

    static string FindField(PackSet packs, string part) =>
        packs.List(XBlock.Archive).Select(Path.GetFileNameWithoutExtension).Where(n => n.Contains(part, StringComparison.OrdinalIgnoreCase)).OrderBy(n => n.Length).ThenBy(n => n, StringComparer.Ordinal).First();

    /// <summary>Every effect block's change under a day map (Tria) and the darkest map (02000012_in, by the mean of its ambient
    /// and sun), facing the sun (N.L 1) and side on (N.L 0): the blocks that change by more than 5% by kind and form, darker
    /// or brighter, and the healing spot's MS2StandardMaterial mesh (form 2).</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void HowFarEachBlockMoves(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} not set");
        using var packs = new PackSet(dir);
        var flats = new FlatLibrary(packs);
        var spec = new NifSpec(NifEnv.NifXml());
        var maps = new[] { FieldLights(packs, flats, FindField(packs, "_tw_tria")), FieldLights(packs, flats, "02000012_in") };
        foreach (var l in maps)
            TestContext.Out.WriteLine($"{env}: {l.Field} ambient {string.Join(",", l.Ambient.Select(x => x.ToString("0.###")))} sun {string.Join(",", l.Sun.Select(x => x.ToString("0.###")))} sun ambient {string.Join(",", l.SunAmbient.Select(x => x.ToString("0.###")))} dir {l.Dir.X:0.###},{l.Dir.Y:0.###},{l.Dir.Z:0.###}");
        var count = new SortedDictionary<string, int>(StringComparer.Ordinal);
        void Add(string k) => count[k] = count.GetValueOrDefault(k) + 1;
        var samples = new SortedDictionary<string, string>(StringComparer.Ordinal);
        int blocks = 0;
        foreach (var entry in packs.List(Archive).Where(e => e.EndsWith(".nif", StringComparison.Ordinal)).OrderBy(e => e, StringComparer.Ordinal))
        {
            EffectScene sc;
            try { sc = EffectScene.Load(NifFile.Load(spec, packs.Read(Archive, entry))); }
            catch (Exception) { continue; }
            var items = sc.Meshes.Select(m => ("mesh", m, m.Mesh.Normals != null)).Concat(sc.Systems.Select(s => ("system", s.Material, true)));
            foreach (var (kind, em, normals) in items)
            {
                blocks++;
                string key = $"{kind} form {em.LightForm}{(normals ? "" : " no normals")}";
                Add($"{key}: blocks");
                foreach (var l in maps)
                    foreach (var ndl in new[] { 1.0, 0.0 })
                    {
                        if (!normals && ndl == 0) continue;
                        double ch = Change(em, normals, ndl, l, out double o, out double n);
                        string at = normals ? $"N.L {ndl}" : "any N.L";
                        if (ch <= 0.05) continue;
                        string way = n > o ? "brighter" : "darker";
                        Add($"{key}: {l.Field} {at}: > 5% {way}");
                        samples.TryAdd($"{key}: {l.Field} {at} {way}", $"{entry} {em.Mesh.Name} {o:0.###} -> {n:0.###}");
                    }
                if (entry == "bg/common/eff_com_recovery.nif" && em.LightForm == 2)
                    foreach (var l in maps)
                        foreach (var ndl in new[] { 1.0, 0.0 })
                        {
                            double ch = Change(em, normals, ndl, l, out double o, out double n);
                            TestContext.Out.WriteLine($"{env}: healing spot {kind} {em.Mesh.Name} ({em.ActiveMaterial}) {l.Field} N.L {ndl}: {o:0.###} -> {n:0.###} ({ch:P1})");
                        }
            }
        }
        TestContext.Out.WriteLine($"{env}: {blocks} blocks");
        foreach (var (k, c) in count) TestContext.Out.WriteLine($"{env}:   {k}: {c}");
        foreach (var (k, s) in samples) TestContext.Out.WriteLine($"{env}: e.g. {k}: {s}");
        Assert.That(blocks, Is.GreaterThan(40000));
        // a form 0 block keeps its old shading under any light
        Assert.That(count.Keys.Where(k => k.StartsWith("mesh form 0") || k.StartsWith("system form 0")).Where(k => k.Contains("> 5%")), Is.Empty);
    }

    /// <summary>The parts whose &lt;attachInfo useLighting&gt; is false, on both data sets: where the attribute is written and
    /// with what values, the nif parts it turns off, and their blocks by the light form they had (now 0).</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void PartsWithTheLightsOff(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} not set");
        using var packs = new PackSet(dir);
        var spec = new NifSpec(NifEnv.NifXml());
        var count = new SortedDictionary<string, int>(StringComparer.Ordinal);
        void Add(string k, int n = 1) => count[k] = count.GetValueOrDefault(k) + n;
        var off = new SortedSet<string>(StringComparer.Ordinal);
        var on = new HashSet<string>(StringComparer.Ordinal);
        var sample = new List<string>();
        int xmls = 0;
        foreach (var entry in packs.List("Xml").Where(e => e.StartsWith("effect/", StringComparison.Ordinal) && e.EndsWith(".xml", StringComparison.Ordinal)))
        {
            XDocument x;
            try { x = PackXml.Parse(packs.Read("Xml", entry)); } catch (Exception) { continue; }
            xmls++;
            foreach (var a in x.Descendants().Attributes("useLighting")) Add($"attribute on <{a.Parent!.Name.LocalName}> = {a.Value}");
            foreach (var p in EffectTable.Parse(x))
            {
                if (string.IsNullOrEmpty(p.FilePath) || !p.FilePath.EndsWith(".nif", StringComparison.OrdinalIgnoreCase)) continue;
                string nif = p.FilePath.Replace('\\', '/').ToLowerInvariant();
                if (p.UseLighting) { on.Add(nif); continue; }
                Add("nif parts with the lights off");
                if (off.Add(nif) && sample.Count < 15) sample.Add($"{entry}: {nif}");
            }
        }
        foreach (var nif in off)
        {
            if (on.Contains(nif)) Add("of their nifs, also used with the lights on");
            if (!packs.Exists(Archive, nif)) { Add("of their nifs, missing"); continue; }
            EffectScene sc;
            try { sc = EffectScene.Load(NifFile.Load(spec, packs.Read(Archive, nif))); } catch (Exception) { Add("of their nifs, not loaded"); continue; }
            foreach (var m in sc.Meshes) Add($"their mesh blocks, form {m.LightForm} -> 0");
            foreach (var s in sc.Systems) Add($"their system blocks, form {s.Material.LightForm} -> 0");
        }
        TestContext.Out.WriteLine($"{env}: {xmls} effect xmls, {off.Count} nifs with the lights off");
        foreach (var (k, c) in count) TestContext.Out.WriteLine($"{env}:   {k}: {c}");
        foreach (var s in sample) TestContext.Out.WriteLine($"{env}: {s}");
        Assert.That(xmls, Is.GreaterThan(1000));
        Assert.That(count.Keys.Where(k => k.StartsWith("attribute on") && !k.StartsWith("attribute on <attachInfo>")), Is.Empty, "useLighting is read from <attachInfo> only");
    }
}
