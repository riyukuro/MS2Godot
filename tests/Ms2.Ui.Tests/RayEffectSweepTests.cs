using System.Numerics;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The whole lists behind the path segment effects, on each data set:
/// - every attack that lays them (<see cref="SkillAttackData.RaySegments"/>: KMS attackMethodType 2, GMS2 a set effectRay on an
///   attack that is no arrow record): its effect (<see cref="SkillAttackData.RayEffect"/>) is there and has a lightning part
///   the service makes (but the four known beam / nif ones), its magic path row is there, and its rayType is 0, 1 or 2;
/// - every lightning part of every effect xml: three non-empty texture lists, each texture in the Effect archive, and its strip
///   from a deterministic walk: the start and end kept, at most 101 points, every step at most jointLen in x / y, the rows
///   width apart (camera straight down), 0 B per rebuild.
/// </summary>
public class RayEffectSweepTests
{
    const string EffectArchive = "Resource/Model/Effect";

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryRayAttackPlaysALightningAndEveryLightningLaysItsStrip(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} not set");
        using var packs = new PackSet(dir);
        var tables = GameTables.Shared(packs, "en");
        var effects = new EffectTable(packs);
        var paths = tables.MagicPaths;

        // every attack of every level of every skill
        int attacks = 0, rays = 0, gmsRows = 0;
        var rayTypes = new SortedDictionary<int, int>();
        var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var skills = new SortedSet<uint>();
        var missingFile = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var noLightning = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var noLightningSkills = new SortedDictionary<uint, string>();
        var noPath = new SortedSet<string>();
        var badRayType = new SortedSet<string>();
        foreach (var s in tables.Skills.Skills.Rows)
            foreach (var l in s.Levels)
                foreach (var m in l.Motions)
                    foreach (var a in m.Attacks)
                    {
                        attacks++;
                        if (!a.RaySegments) continue;
                        rays++;
                        if (a.Gms2Row) gmsRows++;
                        skills.Add(s.Id);
                        rayTypes[a.Arrow.RayType] = rayTypes.GetValueOrDefault(a.Arrow.RayType) + 1;
                        if (a.Arrow.RayType is < 0 or > 2) badRayType.Add($"{s.Id} rayType {a.Arrow.RayType}");
                        if (paths.Get(a.MagicPathId, 0) == null) noPath.Add($"{s.Id} magicPathID {a.MagicPathId}");
                        var file = a.RayEffect;
                        if (!files.Add(file)) continue;
                        var parts = effects.Get(file);
                        if (parts == null) { missingFile.Add($"{s.Id} {file}"); continue; }
                        if (!parts.Any(p => p.TypeId == EffectSkipTable.TypeLightning && p.Strip != null && EffectRandom.ParamMade(parts, p)))
                        {
                            var types = string.Join(",", parts.Select(p => p.Type));
                            noLightning.Add($"{s.Id} {file}: {types}");
                            noLightningSkills[s.Id] = types;
                        }
                    }

        // every lightning part of every effect xml
        int xmls = 0, lightnings = 0, textures = 0, walks = 0;
        var emptyLists = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var missingTex = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var badWalk = new List<string>();
        var notRay = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var rng = new Random(7);
        Func<int> rand = () => rng.Next(0x8000);
        long bytes = 0;
        foreach (var entry in packs.List("Xml"))
        {
            if (!entry.StartsWith("effect/", StringComparison.OrdinalIgnoreCase) || !entry.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
            xmls++;
            var file = entry.Substring("effect/".Length);
            var parts = effects.Get(file);
            if (parts == null) continue;
            foreach (var p in parts)
            {
                if (p.TypeId != EffectSkipTable.TypeLightning || p.Strip == null) continue;
                lightnings++;
                if (!files.Contains(file)) notRay.Add(file);
                var path = new LightningPath(p.Strip);
                for (int c = 0; c < 3; c++)
                {
                    var list = path.Textures(c);
                    if (list.Length == 0) emptyLists.Add($"{file} class {c}");
                    foreach (var t in list)
                    {
                        textures++;
                        var e = EffectStripParam.TextureEntry(t);
                        if (e == null || !packs.Exists(EffectArchive, e)) missingTex.Add($"{file}: {t}");
                    }
                }
                foreach (float len in new[] { 120f, 400f, 900f })
                {
                    var a = new Vector3(10, 20, 30);
                    var b = a + new Vector3(len * 0.6f, -len * 0.8f, 40);
                    // the least of three rebuilds (the JIT can charge its own bytes to one; a real allocation shows on all)
                    long least = long.MaxValue;
                    for (int k = 0; k < 3 && least != 0; k++)
                        least = Math.Min(least, Bytes(path, a, b, (uint)(1_000_000 + walks * 100_000 + k * 1000), rand));
                    bytes += least;
                    walks++;
                    var pts = path.Path;
                    bool ok = pts.Length >= 2 && pts.Length <= LightningPath.MaxRow && pts[0] == a && pts[^1] == b;
                    for (int i = 1; ok && i < pts.Length - 1; i++)
                    {
                        var d = pts[i] - pts[i - 1];
                        ok = MathF.Sqrt(d.X * d.X + d.Y * d.Y) <= p.Strip.JointLen + 1e-3f;
                    }
                    for (int i = 0; ok && i < pts.Length; i++)
                        ok = MathF.Abs(Vector3.Distance(path.Left[i], path.Right[i]) - p.Strip.Width) < 1e-2f * MathF.Max(1f, p.Strip.Width);
                    if (!ok) badWalk.Add($"{file} length {len}");
                }
            }
        }

        TestContext.Progress.WriteLine($"{env}: {attacks} attacks, {rays} lay ray segments ({gmsRows} GMS2 rows) in {skills.Count} skills, "
            + $"rayType {string.Join(" ", rayTypes.Select(kv => $"{kv.Key}:{kv.Value}"))}, {files.Count} effect files");
        TestContext.Progress.WriteLine($"{env}: {xmls} effect xmls, {lightnings} lightning parts, {textures} textures, {walks} walks, {bytes} B");
        foreach (var x in missingFile) TestContext.Progress.WriteLine($"  missing effect: {x}");
        foreach (var x in noLightning) TestContext.Progress.WriteLine($"  no lightning: {x}");
        foreach (var x in noPath) TestContext.Progress.WriteLine($"  no path row: {x}");
        foreach (var x in emptyLists) TestContext.Progress.WriteLine($"  empty texture list: {x}");
        foreach (var x in missingTex) TestContext.Progress.WriteLine($"  missing texture: {x}");
        foreach (var x in notRay) TestContext.Progress.WriteLine($"  lightning outside the ray effects: {x}");

        Assert.That(rays, Is.GreaterThan(0));
        Assert.That(badRayType, Is.Empty);
        Assert.That(noPath, Is.Empty);
        Assert.That(missingFile, Is.Empty);
        // The ray effects with no lightning part, the same on both data sets. Their segments play the effect all the same
        // (the slots turn on and each part plays on the segment's request):
        // - three lay beams (type 0x15, BeamEffect, which reads the same point list); STILL-OPEN: the beam part is not made;
        // - the scar burst item's ray effect is a nif and a sound, played at the segment's node.
        Assert.That(noLightningSkills, Is.EqualTo(new SortedDictionary<uint, string>
        {
            [10300181] = "beam,beam",
            [10600171] = "beam,beam",
            [11100242] = "beam,beam,beam",
            [90000558] = "nif,soundEvent",
        }), "the ray effects without a lightning part");
        Assert.That(emptyLists, Is.Empty);
        Assert.That(missingTex, Is.Empty);
        Assert.That(badWalk, Is.Empty);
        Assert.That(bytes, Is.EqualTo(0), "a rebuild allocates nothing");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    static long Bytes(LightningPath path, Vector3 a, Vector3 b, uint nowMs, Func<int> rand)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        path.Rebuild(a, b, new Vector3(0, 0, -1), nowMs, rand);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
