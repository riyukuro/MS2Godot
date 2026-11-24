using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// Every swoosh, ribbon, lightning, beam, afterImage and status part of every effect xml, read again from the raw attributes
/// with the client's readers (TinyXML's sscanf "%d" / "%lf" prefixes, CompareNoCase, CString::Tokenize) and held against
/// <see cref="EffectTable"/>'s parse; plus what the data asks of the runtime: the textures and nifs the parts name, the
/// trail shapes at each effect step, and the values only a prefix read takes.
/// </summary>
public class EffectStripSweepTests
{
    static readonly string[] Types = { "swoosh", "ribbon", "lightning", "beam", "afterImage", "status" };

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryStripPartReadsAsTheClientReadsIt(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} not set");
        using var packs = new PackSet(dir);
        const string effectArchive = "Resource/Model/Effect";
        int xmls = 0, unreadable = 0;
        var count = new Dictionary<string, int>();
        var xmlsOf = new Dictionary<string, HashSet<string>>();
        var bad = new List<string>();
        int prefixOnly = 0, qualityCut = 0, samplingCut = 0, swooshNif = 0, swooshTex = 0, swooshNeither = 0, nifMissing = 0, texMissing = 0;
        int notMade = 0, nested = 0, step3 = 0, step2None = 0, maxCurve = 0, lightTex = 0, lightTexMissing = 0, beamNoTex = 0;
        var alpha = new int[3]; var beamCurves = new int[3];
        var missing = new SortedSet<string>();
        foreach (var entry in packs.List("Xml").Where(e => e.StartsWith("effect/", StringComparison.Ordinal) && e.EndsWith(".xml", StringComparison.Ordinal)).OrderBy(e => e, StringComparer.Ordinal))
        {
            XDocument x;
            try { x = PackXml.Parse(packs.Read("Xml", entry)); } catch { unreadable++; continue; }
            xmls++;
            var parts = EffectTable.Parse(x);
            var elems = x.Descendants("effect").ToList();
            Assert.That(parts.Count, Is.EqualTo(elems.Count), entry);
            for (int i = 0; i < elems.Count; i++)
            {
                var e = elems[i];
                string type = (string)e.Attribute("type");
                string kind = Types.FirstOrDefault(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase));
                if (kind == null) continue;
                var p = parts[i];
                count[kind] = count.GetValueOrDefault(kind) + 1;
                if (!xmlsOf.TryGetValue(kind, out var set)) xmlsOf[kind] = set = new();
                set.Add(entry);
                if (!p.TopLevel) nested++;
                // CEffectParam::vfunc_2: a swoosh or status part with no loop and no duration is not made
                if (!EffectRandom.ParamMade(parts, p)) notMade++;
                foreach (var a in e.Attributes())
                    if (IsNumberAttr(a.Name.LocalName) && !double.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) prefixOnly++;
                string where = $"{entry} part {i} ({kind})";
                switch (kind)
                {
                    case "swoosh":
                    {
                        var s = p.Strip;
                        if (s == null) { bad.Add(where + ": no param"); break; }
                        int q = ScanInt(e, "quality", 4), sm = ScanInt(e, "sampling", 15);
                        if (q > 4) qualityCut++;
                        if (sm > 60) samplingCut++;
                        Check(bad, where, "quality", s.Quality, Math.Min(q, 4));
                        Check(bad, where, "sampling", s.Sampling, Math.Min(sm, 60));
                        Check(bad, where, "length", s.Length, ScanFloat(e, "length", 0.1f));
                        Check(bad, where, "node1", s.Node1, (string)e.Attribute("node1") ?? "");
                        Check(bad, where, "node2", s.Node2, (string)e.Attribute("node2") ?? "");
                        Check(bad, where, "texturePath", s.TexturePath, (string)e.Attribute("texturePath") ?? "");
                        Check(bad, where, "nifPath", s.NifPath, (string)e.Attribute("nifPath") ?? "");
                        Check(bad, where, "alphaMode", s.AlphaMode, AlphaOf(e));
                        alpha[s.AlphaMode]++;
                        if (s.NifPath.Length > 0)
                        {
                            swooshNif++;
                            if (!packs.Exists(effectArchive, s.NifPath.Replace('\\', '/').ToLowerInvariant())) { nifMissing++; missing.Add("nif " + s.NifPath); }
                        }
                        else if (s.TexturePath.Length > 0)
                        {
                            swooshTex++;
                            if (!packs.Exists(effectArchive, EffectStripParam.TextureEntry(s.TexturePath))) { texMissing++; missing.Add("tex " + s.TexturePath); }
                        }
                        else swooshNeither++;
                        // the shape at the default step (3) and the lower one (2); step 1 makes no swoosh
                        if (s.SwooshShape(3, false, 1, 60, out int seg, out int curve, out float iv)) step3++;
                        Check(bad, where, "segments", seg, (int)Math.Max(1u, (uint)(long)(s.Length * Math.Max((uint)s.Sampling >> 1, 5u))));
                        Check(bad, where, "interval", iv, 1f / Math.Max((uint)s.Sampling >> 1, 5u));
                        maxCurve = Math.Max(maxCurve, curve);
                        if (!s.SwooshShape(2, false, 1, 60, out _, out _, out _)) step2None++;
                        if (s.SwooshShape(1, false, 1, 60, out _, out _, out _)) bad.Add(where + ": step 1 made a swoosh");
                        break;
                    }
                    case "ribbon":
                    {
                        var s = p.Strip;
                        if (s == null) { bad.Add(where + ": no param"); break; }
                        Check(bad, where, "width", s.Width, ScanFloat(e, "width", 10f));
                        Check(bad, where, "quality", s.Quality, ScanInt(e, "quality", 4));
                        Check(bad, where, "nifPath", s.NifPath, (string)e.Attribute("nifPath") ?? "");
                        Check(bad, where, "texturePath", s.TexturePath, (string)e.Attribute("texturePath") ?? "");
                        Check(bad, where, "alphaMode", s.AlphaMode, AlphaOf(e));
                        if (s.NifPath.Length > 0 && !packs.Exists(effectArchive, s.NifPath.Replace('\\', '/').ToLowerInvariant())) { nifMissing++; missing.Add("nif " + s.NifPath); }
                        break;
                    }
                    case "lightning":
                    {
                        var s = p.Strip;
                        if (s == null) { bad.Add(where + ": no param"); break; }
                        Check(bad, where, "texturePathA", string.Join("|", s.TexturesA), string.Join("|", Tokens((string)e.Attribute("texturePathA"))));
                        Check(bad, where, "texturePathB", string.Join("|", s.TexturesB), string.Join("|", Tokens((string)e.Attribute("texturePathB"))));
                        Check(bad, where, "texturePathC", string.Join("|", s.TexturesC), string.Join("|", Tokens((string)e.Attribute("texturePathC"))));
                        Check(bad, where, "spread", s.Spread, ScanFloat(e, "spread", 1f));
                        Check(bad, where, "jointLen", s.JointLen, ScanFloat(e, "jointLen", 32f));
                        Check(bad, where, "width", s.Width, ScanFloat(e, "width", 10f));
                        Check(bad, where, "alphaMode", s.AlphaMode, AlphaOf(e));
                        string sort = (string)e.Attribute("sortingMode");
                        Check(bad, where, "sortingMode", s.SortingMode, string.Equals(sort, "inherit", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
                        foreach (var t in s.TexturesA.Concat(s.TexturesB).Concat(s.TexturesC))
                        {
                            lightTex++;
                            if (!packs.Exists(effectArchive, EffectStripParam.TextureEntry(t))) { lightTexMissing++; missing.Add("tex " + t); }
                        }
                        break;
                    }
                    case "beam":
                    {
                        var s = p.Strip;
                        if (s == null) { bad.Add(where + ": no param"); break; }
                        var tex = Tokens((string)e.Attribute("texturePath"));
                        Check(bad, where, "texturePath", string.Join("|", s.TexturesA), string.Join("|", tex));
                        if (tex.Length == 0) { beamNoTex++; Check(bad, where, "width (no texture)", s.Width, 10f); break; }
                        Check(bad, where, "width", s.Width, ScanFloat(e, "width", 10f));
                        Check(bad, where, "quality", s.Quality, ScanInt(e, "quality", 4));
                        Check(bad, where, "alphaMode", s.AlphaMode, AlphaOf(e));
                        var bi = e.Element("beamInfo");
                        string ct = (string)bi?.Attribute("type");
                        int curve = string.Equals(ct, "pointcurve", StringComparison.OrdinalIgnoreCase) ? 1 : string.Equals(ct, "directioncurve", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
                        Check(bad, where, "beamInfo type", s.BeamCurve, curve);
                        beamCurves[curve]++;
                        if (curve == 2)
                        {
                            Check(bad, where, "directionRate", s.DirectionRate, ScanFloat(bi, "directionRate", 0.5f));
                            Check(bad, where, "lengthRate", s.LengthRate, ScanFloat(bi, "lengthRate", 0.5f));
                        }
                        if (curve == 1)
                        {
                            var v = ((string)bi.Attribute("passThroughPoint") ?? "").Split(',').Where(t => t.Length > 0).Select(t => Atof(t)).ToArray();
                            if (v.Length == 3)
                            {
                                Check(bad, where, "passThroughPoint.x", s.PassThroughPoint.X, v[0]);
                                Check(bad, where, "passThroughPoint.y", s.PassThroughPoint.Y, v[1]);
                                Check(bad, where, "passThroughPoint.z", s.PassThroughPoint.Z, v[2]);
                            }
                        }
                        break;
                    }
                    case "afterImage":
                    {
                        var s = p.AfterImage;
                        if (s == null) { bad.Add(where + ": no param"); break; }
                        Check(bad, where, "imageSize", s.ImageSize, ScanInt(e, "imageSize", 0x100));
                        Check(bad, where, "timeLife", s.TimeLifeMs, (uint)(ScanFloat(e, "timeLife", 1f) * 1000f));
                        Check(bad, where, "timeInterval", s.TimeIntervalMs, (uint)(ScanFloat(e, "timeInterval", 0.2f) * 1000f));
                        Check(bad, where, "moveInterval", s.MoveInterval, ScanFloat(e, "moveInterval", 100f));
                        break;
                    }
                    case "status":
                        Check(bad, where, "category", p.StatusCategory, (string)e.Attribute("category") ?? "");
                        break;
                }
            }
        }
        string Of(string k) => $"{count.GetValueOrDefault(k)} parts / {xmlsOf.GetValueOrDefault(k)?.Count ?? 0} xmls";
        TestContext.Out.WriteLine($"{env}: {xmls} xmls ({unreadable} unreadable); swoosh {Of("swoosh")}, ribbon {Of("ribbon")}, lightning {Of("lightning")}, beam {Of("beam")}, afterImage {Of("afterImage")}, status {Of("status")}; {nested} nested, {notMade} not made (no loop, duration 0)");
        TestContext.Out.WriteLine($"{env}: swoosh: {swooshTex} texture, {swooshNif} nif, {swooshNeither} neither; quality cut to 4 on {qualityCut}, sampling cut to 60 on {samplingCut}; alpha swoosh/standard/add {alpha[0]}/{alpha[1]}/{alpha[2]}; step 3 makes {step3}, step 2 makes none of {step2None}; most curve samples {maxCurve}");
        TestContext.Out.WriteLine($"{env}: lightning: {lightTex} textures named, {lightTexMissing} not in the Effect archive; beam: {beamNoTex} with no texture, curves none/point/direction {beamCurves[0]}/{beamCurves[1]}/{beamCurves[2]}");
        TestContext.Out.WriteLine($"{env}: missing: {nifMissing} nifs, {texMissing + lightTexMissing} textures ({missing.Count} names); {prefixOnly} number values only a prefix read takes");
        foreach (var m in missing.Take(20)) TestContext.Out.WriteLine("  missing " + m);
        Assert.That(bad, Is.Empty, string.Join("\n", bad.Take(30)));
        Assert.That(count.GetValueOrDefault("swoosh"), Is.GreaterThan(1000));
    }

    [Test]
    public void TheClientsDefaultsAndCuts()
    {
        var x = XDocument.Parse("<effects><effect type=\"swoosh\" loop=\"true\" quality=\"10\" sampling=\"120\" length=\"0.5\" alphaMode=\"Add\" />"
            + "<effect type=\"swoosh\" duration=\"1\" quality=\"4.0\" alphaMode=\"bogus\" />"
            + "<effect type=\"beam\" width=\"50\" quality=\"9\"><beamInfo type=\"directionCurve\" /></effect>"
            + "<effect type=\"beam\" texturePath=\"//a.dds,,//b.dds\" width=\"50\"><beamInfo type=\"pointCurve\" passThroughPoint=\"1, 2\" /></effect>"
            + "<effect type=\"lightning\" texturePathA=\"//x.dds\" sortingMode=\"inherit\" alphaMode=\"standard\" />"
            + "<effect type=\"afterImage\" color=\"1,1,1\" timeLife=\"2\" /></effects>");
        var p = EffectTable.Parse(x);
        Assert.Multiple(() =>
        {
            Assert.That(p[0].Strip.Quality, Is.EqualTo(4));
            Assert.That(p[0].Strip.Sampling, Is.EqualTo(60));
            Assert.That(p[0].Strip.AlphaMode, Is.EqualTo(EffectStripParam.AlphaAdd));
            Assert.That(p[1].Strip.Quality, Is.EqualTo(4));
            Assert.That(p[1].Strip.Sampling, Is.EqualTo(15));
            Assert.That(p[1].Strip.Length, Is.EqualTo(0.1f));
            Assert.That(p[1].Strip.AlphaMode, Is.EqualTo(EffectStripParam.AlphaSwoosh));
            // no texture: the beam reads nothing more
            Assert.That(p[2].Strip.Width, Is.EqualTo(10f));
            Assert.That(p[2].Strip.Quality, Is.EqualTo(4));
            Assert.That(p[2].Strip.BeamCurve, Is.EqualTo(0));
            Assert.That(p[3].Strip.TexturesA, Is.EqualTo(new[] { "//a.dds", "//b.dds" }));
            Assert.That(p[3].Strip.Width, Is.EqualTo(50f));
            Assert.That(p[3].Strip.BeamCurve, Is.EqualTo(1));
            Assert.That(p[3].Strip.PassThroughPoint, Is.EqualTo(System.Numerics.Vector3.Zero));   // two values: not read
            Assert.That(p[4].Strip.SortingMode, Is.EqualTo(1));
            Assert.That(p[4].Strip.AlphaMode, Is.EqualTo(EffectStripParam.AlphaStandard));
            Assert.That(p[5].AfterImage.Color, Is.EqualTo(System.Numerics.Vector4.Zero));   // three values: ParseColorA's (0,0,0,0)
            Assert.That(p[5].AfterImage.TimeLifeMs, Is.EqualTo(2000u));
            Assert.That(p[5].AfterImage.TimeIntervalMs, Is.EqualTo(200u));
            Assert.That(EffectStripParam.TextureEntry("//Lighten_sp_001/Lighten_sp_011.dds"), Is.EqualTo("textures/lighten_sp_001/lighten_sp_011.dds"));
            Assert.That(EffectStripParam.TextureEntry("swoosh/Swoosh17.dds"), Is.EqualTo("textures/swoosh/swoosh17.dds"));
        });
    }

    [Test]
    public void TheTrailShapeFollowsTheEffectStepAndTheFrameRate()
    {
        var s = new EffectStripParam { Length = 0.3f, Quality = 4, Sampling = 60 };
        Assert.Multiple(() =>
        {
            // step 3: sampling 30, 9 segments, 36 curve samples
            Assert.That(s.SwooshShape(3, true, 1, 30, out int seg, out int curve, out float iv), Is.True);
            Assert.That((seg, curve, iv), Is.EqualTo((9, 36, 1f / 30)));
            // step 2: sampling 15
            Assert.That(s.SwooshShape(2, true, 1, 30, out seg, out _, out iv), Is.True);
            Assert.That((seg, iv), Is.EqualTo((4, 1f / 15)));
            Assert.That(s.SwooshShape(1, true, 1, 30, out _, out _, out _), Is.False);
            // kinds 3 and 5 drop steps under 60 fps when the trail mode is on
            Assert.That(s.SwooshShape(3, true, 3, 60, out _, out _, out iv), Is.True);
            Assert.That(iv, Is.EqualTo(1f / 15));
            Assert.That(s.SwooshShape(3, true, 5, 55, out _, out _, out _), Is.False);
            Assert.That(s.SwooshShape(3, false, 5, 30, out _, out _, out iv), Is.True);
            Assert.That(iv, Is.EqualTo(1f / 30));
            Assert.That(s.SwooshShape(3, true, 3, 61, out _, out _, out iv), Is.True);
            Assert.That(iv, Is.EqualTo(1f / 30));
            // a low sampling keeps 5 a second
            var slow = new EffectStripParam { Length = 0.1f, Quality = 2, Sampling = 6 };
            Assert.That(slow.SwooshShape(3, false, 1, 60, out seg, out curve, out iv), Is.True);
            Assert.That((seg, curve, iv), Is.EqualTo((1, 2, 1f / 5)));
        });
    }

    [Test]
    public void TheTrailKeepsSegmentsPlusThreePointsAndCurvesThroughThem()
    {
        var t = new SwooshTrail(4, 16, 0.125f);
        int laid = 0;
        for (int i = 0; i < 20; i++)
        {
            var a = new System.Numerics.Vector3(i * 10, 0, 0);
            if (t.Sample(i * 0.125 + 0.125, a, a + new System.Numerics.Vector3(0, 0, 5))) laid++;
            // back to segments + 2 after each lay at segments + 3
            Assert.That(t.Points, Is.EqualTo(i + 1 < 7 ? i + 1 : 6), $"sample {i}");
            if (i + 1 >= 4)
            {
                int pts = Math.Min(i + 1, 7);
                Assert.That(t.Count, Is.EqualTo((pts - 3) * 4 + 1), $"sample {i}");
                // the last vertex is the newest point; the curve starts at the second kept point
                Assert.That(t.CurveA[t.Count - 1], Is.EqualTo(a));
                Assert.That(t.CurveB[t.Count - 1].Z, Is.EqualTo(5f));
            }
        }
        Assert.That(laid, Is.EqualTo(17));
        // between samples nothing moves
        Assert.That(t.Sample(2.55, default, default), Is.False);
        // on a straight line the curve is the line
        Assert.That(SwooshTrail.CatmullRom(new(0, 0, 0), new(1, 0, 0), new(2, 0, 0), new(3, 0, 0), 0.5f).X, Is.EqualTo(1.5f).Within(1e-6));
        Assert.That(SwooshTrail.CatmullRom(new(0, 0, 0), new(1, 7, 0), new(2, 0, 3), new(3, 0, 0), 0f), Is.EqualTo(new System.Numerics.Vector3(1, 7, 0)));
        Assert.That(SwooshTrail.CatmullRom(new(0, 0, 0), new(1, 7, 0), new(2, 0, 3), new(3, 0, 0), 1f), Is.EqualTo(new System.Numerics.Vector3(2, 0, 3)));
    }

    [Test]
    public void ASampleAllocatesNothing()
    {
        var t = new SwooshTrail(60, 240, 1f / 30);
        double now = 0;
        for (int i = 0; i < 200; i++) t.Sample(now += 0.04, new(i, 0, 0), new(i, 0, 1));
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++) t.Sample(now += 0.04, new(i, 0, 0), new(i, 0, 1));
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0));
    }

    static bool IsNumberAttr(string n) => n is "length" or "quality" or "sampling" or "width" or "spread" or "jointLen" or "imageSize" or "timeLife" or "timeInterval" or "moveInterval" or "textureChangeTick";

    static void Check<T>(List<string> bad, string where, string what, T got, T want)
    {
        if (!EqualityComparer<T>.Default.Equals(got, want)) bad.Add($"{where}: {what} {got}, the client reads {want}");
    }

    static int AlphaOf(XElement e)
    {
        string s = ((string)e.Attribute("alphaMode") ?? "").ToUpperInvariant();
        return s == "STANDARD" ? 1 : s == "ADD" ? 2 : 0;
    }

    static string[] Tokens(string s) => string.IsNullOrEmpty(s) ? Array.Empty<string>() : s.Split(',').Where(t => t.Length > 0).ToArray();

    /// <summary>sscanf "%d": white space, a sign, digits; the default when no digit.</summary>
    static int ScanInt(XElement e, string name, int def)
    {
        string s = (string)e?.Attribute(name);
        if (s == null) return def;
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        int st = i;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
        int d = i;
        while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
        return i > d ? int.Parse(s[st..i], CultureInfo.InvariantCulture) : def;
    }

    /// <summary>sscanf "%lf" (QueryDoubleValue) as a float: the longest number prefix; the default when none.</summary>
    static float ScanFloat(XElement e, string name, float def)
    {
        string s = (string)e?.Attribute(name);
        if (s == null) return def;
        for (int n = s.Length; n > 0; n--)
            if (double.TryParse(s[..n], NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return (float)v;
        return def;
    }

    static float Atof(string s)
    {
        s = s.Trim();
        for (int n = s.Length; n > 0; n--)
            if (double.TryParse(s[..n], NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return (float)v;
        return 0;
    }
}
