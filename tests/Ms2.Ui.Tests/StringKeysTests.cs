using System.Text.RegularExpressions;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Swf;
using Ms2.Swf.Abc;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The "$" string tokens the UI movies ask the Flash translator for (GFxManager::Localize, korflash.xml rows read as
/// NiGFxLocalizeManager::ParseStringXml does, <see cref="GameStrings.FlashKeys"/>): every whole-key string of each movie's ABC
/// and every text field whose initial text is a key, against each string folder of the data.</summary>
public class StringKeysTests
{
    static PackSet Packs()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        return new PackSet(d);
    }

    static readonly Regex WholeKey = new(@"^\$[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);
    static readonly Regex Tags = new("<[^>]*>", RegexOptions.Compiled);

    /// <summary>The movie's tokens: (token, where) for each ABC string and text field initial text that is a whole key.</summary>
    public static IEnumerable<(string Token, string Where)> Tokens(SwfMovie m)
    {
        foreach (var (name, data) in m.AbcBlocks)
        {
            AbcFile abc;
            try { abc = AbcFile.Parse(data); } catch { continue; }
            foreach (var s in abc.Strings ?? Array.Empty<string>())
                if (s != null && WholeKey.IsMatch(s)) yield return (s, "abc");
        }
        foreach (var c in m.Characters.Values)
        {
            var t = c.Text?.InitialText;
            if (string.IsNullOrEmpty(t)) continue;
            var plain = System.Net.WebUtility.HtmlDecode(Tags.Replace(t, "")).Trim();
            if (WholeKey.IsMatch(plain)) yield return (plain, "text " + c.Id);
        }
    }

    [Test, Explicit("probe")]
    public void ProbeEveryMoviesTokens()
    {
        var packs = Packs();
        string locale = LobbyFeatures.LocaleOf(packs);
        var langs = packs.List("Xml").Where(e => e.StartsWith("string/") && e.EndsWith("/korflash.xml"))
            .Select(e => e.Split('/')[1]).Distinct().OrderBy(x => x).ToList();
        var kor = langs.ToDictionary(l => l, l => GameStrings.FlashKeys(packs.Read("Xml", $"string/{l}/korflash.xml"), locale));
        var flash = langs.ToDictionary(l => l, l => GameStrings.FlashTable(packs, l, locale));
        var common = langs.ToDictionary(l => l, l => new GameStrings(packs, l).CommonTables().Common);
        var movies = packs.List("Resource/Gfx").Where(e => e.EndsWith(".gfx", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x).ToList();
        var w = new StringWriter();
        w.WriteLine($"data {packs.DataDir} locale {locale} langs {string.Join(",", langs.Select(l => l + ":" + flash[l].Count))} movies {movies.Count}");
        var missingAll = langs.ToDictionary(l => l, _ => new SortedDictionary<string, SortedSet<string>>());
        int tokens = 0;
        foreach (var e in movies)
        {
            SwfMovie m;
            try { m = SwfMovie.Parse(packs.Read("Resource/Gfx", e)); } catch (Exception ex) { w.WriteLine($"  parse {e}: {ex.Message}"); continue; }
            string key = Path.GetFileNameWithoutExtension(e).ToLowerInvariant();
            foreach (var (tok, where) in Tokens(m).Distinct())
            {
                tokens++;
                foreach (var l in langs)
                    if (!flash[l].ContainsKey(tok))
                    {
                        if (!missingAll[l].TryGetValue(tok, out var set)) missingAll[l][tok] = set = new();
                        set.Add(key + "(" + where + ")");
                    }
            }
        }
        w.WriteLine($"tokens {tokens}");
        foreach (var l in langs)
            w.WriteLine($"{l}: korflash {kor[l].Count}, whole map {flash[l].Count}; movie tokens only in the other files: " +
                string.Join(" ", missingAll[l].Keys.Count) );
        foreach (var l in langs)
        {
            w.WriteLine($"== {l}: {missingAll[l].Count} missing");
            foreach (var (tok, set) in missingAll[l])
            {
                string ci = flash[l].Keys.FirstOrDefault(k => string.Equals(k, tok, StringComparison.OrdinalIgnoreCase));
                string flags = (ci != null ? " [case:" + ci + "]" : "") + (common[l].ContainsKey(tok[1..]) ? " [common]" : "")
                    + (flash[l].Keys.Any(k => k.StartsWith(tok, StringComparison.Ordinal) && k != tok) ? " [prefix]" : "");
                w.WriteLine($"  {tok}{flags}  {string.Join(" ", set)}");
            }
        }
        var outp = Environment.GetEnvironmentVariable("MS2_SWEEP_OUT");
        if (!string.IsNullOrEmpty(outp)) File.WriteAllText(outp, w.ToString());
        TestContext.WriteLine(w.ToString());
    }

    /// <summary>The repository's src folder, found above the test's base directory (null when the tests run without it).</summary>
    static string SrcRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "src", "Ms2.Ui"))) return d.FullName;
        return null;
    }

    static readonly Regex KeyLiteral = new("\"(\\$?s_[A-Za-z0-9_]+)\"", RegexOptions.Compiled);

    /// <summary>
    /// Every string key our src and game code look up, against the table its lookup reads: a "$s_" literal, or a plain key given to a
    /// wrapper that adds the "$" (Loc), is a GFxManager::Localize key of the Flash table (<see cref="GameStrings.FlashTable"/>); any other
    /// plain "s_" literal is a GetStringByKey / GetStringCommonByKey key of the common table (stringcommon.xml). A key that only the
    /// other table has is looked up in the wrong one (it shows as the raw key or as nothing). Keys in neither table are the data's
    /// (the client shows the same) and are not checked; neither are generated files.
    /// </summary>
    [Test]
    public void SourceKeysAreInTheTableTheirLookupReads()
    {
        var packs = Packs();
        string root = SrcRoot();
        if (root == null) Assert.Ignore("no source tree");
        string locale = LobbyFeatures.LocaleOf(packs);
        var lits = SourceKeys(root);
        Assert.That(lits.Count, Is.GreaterThan(1000), "the sweep found the source's keys");
        var wrong = new List<string>();
        foreach (var l in Languages(packs))
        {
            var flash = GameStrings.FlashTable(packs, l, locale);
            var common = new GameStrings(packs, l).CommonTables().Common;
            foreach (var ((key, isFlash), where) in lits)
            {
                bool inFlash = flash.ContainsKey("$" + key), inCommon = common.ContainsKey(key);
                if (isFlash ? !inFlash && inCommon : inFlash && !inCommon)
                    wrong.Add($"{l}: {(isFlash ? "$" : "")}{key} ({(isFlash ? "Flash lookup, common row" : "common lookup, Flash row")}) {string.Join(" ", where.Take(3))}");
            }
        }
        TestContext.WriteLine($"{lits.Count} keys");
        Assert.That(wrong, Is.Empty);
    }

    static List<string> Languages(PackSet packs) => packs.List("Xml").Where(e => e.StartsWith("string/") && e.EndsWith("/korflash.xml"))
        .Select(e => e.Split('/')[1]).Distinct().OrderBy(x => x).ToList();

    /// <summary>The source's key literals: (key without "$", whether the lookup is the Flash one) -> where.</summary>
    static SortedDictionary<(string, bool), SortedSet<string>> SourceKeys(string root)
    {
        var lits = new SortedDictionary<(string, bool), SortedSet<string>>();
        foreach (var dir in new[] { "src", "game" })
        {
            var full = Path.Combine(root, dir);
            if (!Directory.Exists(full)) continue;
            foreach (var f in Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                if (rel.Contains("/contracts/") || rel.Contains("/obj/") || rel.Contains("/bin/") || rel.Contains("/.godot/") || rel.EndsWith(".g.cs")
                    || rel.EndsWith("/CommonStringIds.cs")) continue;
                var lines = File.ReadAllLines(f);
                for (int i = 0; i < lines.Length; i++)
                    foreach (Match m in KeyLiteral.Matches(lines[i]))
                    {
                        string lit = m.Groups[1].Value;
                        bool flash = lit[0] == '$' || m.Index >= 4 && lines[i].AsSpan(m.Index - 4, 4).SequenceEqual("Loc(");
                        var k = (lit.TrimStart('$'), flash);
                        if (!lits.TryGetValue(k, out var set)) lits[k] = set = new();
                        set.Add(rel + ":" + (i + 1));
                    }
            }
        }
        return lits;
    }

    /// <summary>The Flash table is korFlash.xml's rows, then usmSubtitleText.xml's and stringMovie.xml's by their "name" attribute
    /// (NiGFxLocalizeManager::LoadStringTables, nigfxlocalizemanager.cpp:105-134), a later row replacing an earlier one.</summary>
    [Test]
    public void TheFlashTableHoldsTheThreeFilesInTheClientsOrder()
    {
        var packs = Packs();
        string locale = LobbyFeatures.LocaleOf(packs);
        foreach (var l in Languages(packs))
        {
            var table = GameStrings.FlashTable(packs, l, locale);
            var expect = new Dictionary<string, string>();
            int files = 0;
            foreach (var (file, attr) in new[] { ("korflash.xml", "value"), ("usmsubtitletext.xml", "name"), ("stringmovie.xml", "name") })
            {
                var entry = packs.List("Xml").FirstOrDefault(e => string.Equals(e, $"string/{l}/{file}", StringComparison.OrdinalIgnoreCase));
                if (entry == null) continue;
                files++;
                GameStrings.FlashKeys(packs.Read("Xml", entry), locale, attr, expect);
            }
            Assert.That(table, Is.EqualTo(expect), l);
            Assert.That(table.Count, Is.GreaterThan(GameStrings.FlashKeys(packs.Read("Xml", $"string/{l}/korflash.xml"), locale).Count),
                l + ": the other files add rows (" + files + " files)");
        }
    }
}
