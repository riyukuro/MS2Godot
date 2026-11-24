using Ms2.Formats.Nif;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// The reader evaluates nif.xml expressions through NifScope, with each name's kind decided when the expression is
/// compiled. These tests hold it to the name lookup the reader used before, applied on every evaluation: Version, User
/// Version, BS Header*, #ARG*, then a field of the struct read so far (a\b walks sub-structs), a missing one 0.
/// </summary>
public class NifScopeTests
{
    static double Lookup(NifStruct d, Dictionary<string, double> args, NifSpec spec, string name)
    {
        switch (name)
        {
            case "Version": return spec.Version;
            case "User Version": return spec.UserVersion;
        }
        if (name.StartsWith("BS Header", StringComparison.Ordinal)) return spec.BsVersion;
        if (name.StartsWith("#ARG", StringComparison.Ordinal)) return args[name.Trim('#')];
        object cur = d;
        if (name.IndexOf('\\') < 0)
            cur = d.TryGetValue(name, out var v0) ? v0 : 0L;
        else
            foreach (var k in name.Split('\\'))
                cur = cur is NifStruct s && s.TryGetValue(k, out var v) ? v : 0L;
        return NifReader.ToNum(cur);
    }

    static string Both(NifExpr e, NifStruct d, Dictionary<string, double> args, NifSpec spec, out string scoped)
    {
        string Try(Func<double> f) { try { return f().ToString("R"); } catch (Exception x) { return x.GetType().Name; } }
        scoped = Try(() => e.Eval(new NifScope(d, args, spec)));
        return Try(() => e.Eval(n => Lookup(d, args, spec, n)));
    }

    /// <summary>Every expression nif.xml has for this version (cond, length, width, args of every struct and niobject),
    /// against hand-made structs that have, lack, and nest the fields, with and without template arguments.</summary>
    [Test]
    public void EveryExpressionEvaluatesAsTheNameLookup()
    {
        var nx = NifEnv.NifXml();
        var spec = new NifSpec(nx, NifSpec.V30_2_0_3, 0, 0);
        var root = System.Xml.Linq.XDocument.Load(nx).Root!;
        var names = root.Elements().Where(e => e.Name.LocalName is "struct" or "niobject")
            .Select(e => (string)e.Attribute("name")).Where(n => n != null).Distinct().ToArray();
        var exprs = new List<NifExpr>();
        var fieldNames = new HashSet<string>();
        foreach (var n in names)
            foreach (var f in spec.Fields(n))
            {
                fieldNames.Add(f.Name);
                exprs.AddRange(new[] { f.Cond, f.Length, f.Width }.Where(x => x != null));
                exprs.AddRange(f.Args.Select(a => a.expr));
            }
        Assert.That(exprs, Has.Count.GreaterThan(500));

        // every field name set (a long, a float, an empty and a full array, a struct), none set, and nested
        var full = new NifStruct { Type = "t" };
        int i = 0;
        foreach (var fn in fieldNames)
        {
            full[fn] = (i++ % 5) switch
            {
                0 => (object)(long)(i * 7 + 3), 1 => 2.5f * i, 2 => Array.Empty<long>(), 3 => new float[] { 1 },
                _ => new NifStruct { Type = "s", ["Num Vertices"] = 4L, ["Has Normals"] = 1L, ["Flags"] = 0x10L },
            };
        }
        var structs = new[] { full, new NifStruct { Type = "e" } };
        var argSets = new[] { null, new Dictionary<string, double> { ["ARG"] = 3, ["ARG1"] = 1, ["ARG2"] = 0, ["ARG3"] = 9 },
            new Dictionary<string, double> { ["ARG"] = 0 } };
        int checkedCount = 0, differ = 0;
        var firstDiffer = new List<string>();
        foreach (var e in exprs)
            foreach (var d in structs)
                foreach (var a in argSets)
                {
                    var want = Both(e, d, a, spec, out var got);
                    checkedCount++;
                    if (want != got) { differ++; if (firstDiffer.Count < 5) firstDiffer.Add($"{e.Source}: {want} vs {got}"); }
                }
        TestContext.Progress.WriteLine($"nif scope: {exprs.Count} expressions, {checkedCount} evaluations, {differ} differ");
        Assert.That(differ, Is.Zero, string.Join("; ", firstDiffer));
    }

    /// <summary>Every block of real model files, with each expression evaluated in that block's scope.</summary>
    [Test]
    public void BlocksOfModelFilesEvaluateAsTheNameLookup()
    {
        var (packs, spec) = NifEnv.Open();
        var files = new[] { ("Resource/Model/Effect", "bg/eliinia/eff_el_obj_chimneya_01.nif"), ("Resource/Model/Effect", "effect/eff_co_galaxy_b01.nif") }
            .Where(x => packs.Exists(x.Item1, x.Item2)).ToArray();
        if (files.Length == 0) Assert.Ignore("these data have neither effect file");
        int differ = 0, n = 0;
        foreach (var (arc, entry) in files)
        {
            var nif = NifFile.Load(spec, packs.Read(arc, entry));
            foreach (var blk in nif.Blocks)
                foreach (var f in spec.Fields(blk.Type))
                    foreach (var e in new[] { f.Cond, f.Length, f.Width }.Where(x => x != null))
                    {
                        n++;
                        if (Both(e, blk, null, spec, out var got) != got) differ++;
                    }
        }
        TestContext.Progress.WriteLine($"nif scope: {n} block evaluations, {differ} differ");
        Assert.That(n, Is.GreaterThan(0));
        Assert.That(differ, Is.Zero);
    }
    /// <summary>
    /// The expression tokenizer's patterns are generated at build time (no runtime parse or IL emit): over every
    /// expression nif.xml has for this version, the generated regexes split and match each part as a run-time Regex of
    /// the same pattern does.
    /// </summary>
    [Test]
    public void GeneratedPatternsTokenizeEveryExpressionAsRuntimeRegex()
    {
        var nx = NifEnv.NifXml();
        var spec = new NifSpec(nx, NifSpec.V30_2_0_3, 0, 0);
        var root = System.Xml.Linq.XDocument.Load(nx).Root!;
        var sources = new HashSet<string>();
        foreach (var n in root.Elements().Where(e => e.Name.LocalName is "struct" or "niobject").Select(e => (string)e.Attribute("name")).Where(n => n != null).Distinct())
            foreach (var f in spec.Fields(n))
                foreach (var x in new[] { f.Cond, f.Length, f.Width }.Concat(f.Args.Select(a => a.expr)))
                    if (x?.Source != null) sources.Add(x.Source);
        Assert.That(sources, Has.Count.GreaterThan(100));
        var ops = new System.Text.RegularExpressions.Regex(NifExpr.Ops().ToString());
        var ver = new System.Text.RegularExpressions.Regex(NifExpr.VerLit().ToString());
        var num = new System.Text.RegularExpressions.Regex(NifExpr.NumLit().ToString());
        int parts = 0;
        foreach (var src in sources)
        {
            Assert.That(NifExpr.Ops().Split(src), Is.EqualTo(ops.Split(src)), src);
            foreach (var part in ops.Split(src).Select(p => p.Trim()).Where(p => p.Length > 0))
            {
                parts++;
                Assert.That(NifExpr.Ops().Match(part).Length, Is.EqualTo(ops.Match(part).Length), part);
                Assert.That(NifExpr.VerLit().IsMatch(part), Is.EqualTo(ver.IsMatch(part)), part);
                Assert.That(NifExpr.NumLit().IsMatch(part), Is.EqualTo(num.IsMatch(part)), part);
            }
        }
        TestContext.WriteLine($"{sources.Count} expressions, {parts} parts");
    }
}
