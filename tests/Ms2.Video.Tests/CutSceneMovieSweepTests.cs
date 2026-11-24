using System.Text.RegularExpressions;
using Ms2.Formats.Pack;
using Ms2.Video;
using NUnit.Framework;

namespace Ms2.Video.Tests;

/// <summary>Every cut scene movie the trigger scripts start (PlaySceneMovie's fileName and movieID, the packet UICutSceneMovie
/// takes; and the side NPC talk's movie) resolves to a file in the movie archives and decodes, in each data set under test.
/// The player (VideoPlayerNode.Load) looks the file name up in the Common, Item and Emotion archives by its base name.</summary>
public class CutSceneMovieSweepTests
{
    static readonly string[] Archives = { "Resource/Movie/Common", "Resource/Movie/Item", "Resource/Movie/Emotion" };
    static readonly Regex SceneMovie = new(@"<action\s+name=""PlaySceneMovie""[^>]*?fileName=[""'](?<f>[^""']*)[""'][^>]*?movieID=[""'](?<id>[^""']*)[""']", RegexOptions.IgnoreCase);
    static readonly Regex SideMovie = new(@"<action\s+name=""SideNpcTalk""[^>]*?type=""movie""[^>]*?usm=""(?<f>[^""]*)""", RegexOptions.IgnoreCase);

    /// <summary>Swf movies the quest scripts name that a data set under test does not ship (checked in KMS2_DEV, KMS2 and GMS2; the GMS2 data lacks the third): the
    /// client's swf loader errors and the pane stops (UICutSceneMovie::vfunc_30 "error").</summary>
    static readonly string[] NotShipped = { "Royal_IshuraRemember.swf", "Starlight_expedition.swf", "Kritias_EpicCutScene_second_01.swf" };

    static IEnumerable<(string env, string data)> DataSets()
    {
        foreach (var env in new[] { "MS2_DATA", "MS2_GMS2_DATA" })
        {
            var data = Environment.GetEnvironmentVariable(env);
            if (!string.IsNullOrEmpty(data) && File.Exists(Path.Combine(data, "Resource/Movie/Common.m2d"))) yield return (env, data);
        }
    }

    static UsmVideo Open(PackSet packs, string path)
    {
        foreach (var archive in Archives)
        {
            var entry = packs.FindByFileName(archive, path);
            if (entry != null) return UsmVideo.Open(packs.Read(archive, entry));
        }
        return null;
    }

    [Test]
    public void EveryTriggerCutSceneMovieResolvesAndDecodes()
    {
        var any = false;
        foreach (var (env, data) in DataSets())
        {
            any = true;
            using var packs = new PackSet(data);
            var paths = new SortedDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in packs.List("Xml").Where(n => n.StartsWith("trigger/", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".xml")).ToList())
            {
                var text = System.Text.Encoding.UTF8.GetString(packs.Read("Xml", n));
                if (!text.Contains("movie", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (Match m in SceneMovie.Matches(text)) Add(paths, m.Groups["f"].Value, $"{n} id {m.Groups["id"].Value}");
                foreach (Match m in SideMovie.Matches(text)) Add(paths, m.Groups["f"].Value, $"{n} side talk");
            }
            Assert.That(paths, Is.Not.Empty, env);
            var bad = new List<string>();
            foreach (var (path, where) in paths)
            {
                TestContext.Out.WriteLine($"{env}: {path} ({where.Count}x, e.g. {where[0]})");
                if (path.EndsWith(".swf", StringComparison.OrdinalIgnoreCase))
                {
                    // PlaySwf: the pane's m_30 movie loads the swf (playSwfMovie); the client's packaged form is the .gfx in Resource/Gfx
                    var gfx = Path.ChangeExtension(Path.GetFileName(path), ".gfx");
                    if (packs.FindByFileName("Resource/Gfx", gfx.ToLowerInvariant()) == null && !NotShipped.Contains(path, StringComparer.OrdinalIgnoreCase)) bad.Add($"{path}: no {gfx} in Resource/Gfx, {where[0]}");
                    continue;
                }
                if (!path.EndsWith(".usm", StringComparison.OrdinalIgnoreCase)) { bad.Add($"{path}: neither a usm nor a swf, {where[0]}"); continue; }
                UsmVideo v;
                try { v = Open(packs, path); } catch (Exception e) { bad.Add($"{path}: {e.GetType().Name} {e.Message}"); continue; }
                if (v == null) { bad.Add($"{path}: not in the movie archives, {where[0]}"); continue; }
                int frames = 0;
                for (; frames < 10; frames++)
                {
                    var f = v.Next();
                    if (f == null || f.Width != v.Width || f.Height != v.Height) break;
                }
                if (v.Width <= 0 || frames < 10) bad.Add($"{path}: {v.Width}x{v.Height}, only {frames} frames decode");
            }
            Assert.That(bad, Is.Empty, env + ":\n" + string.Join("\n", bad));
        }
        if (!any) Assert.Ignore("no data set with Resource/Movie/Common.m2d (MS2_DATA, MS2_GMS2_DATA)");
    }

    static void Add(SortedDictionary<string, List<string>> d, string path, string where)
    {
        if (!d.TryGetValue(path, out var l)) d[path] = l = new List<string>();
        l.Add(where);
    }
}
