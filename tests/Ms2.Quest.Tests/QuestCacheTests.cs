using Ms2.Formats.Cache;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Quest.Tests;

/// <summary>A quest table read back from its cache file is the parsed one: every field, and the functions wired over them.</summary>
public class QuestCacheTests
{
    [Test]
    public void CachedTableEqualsTheParsedOne()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        var packs = new PackSet(data);
        var languages = new GameStrings(packs, "en").Languages;
        var off = new HashSet<string> { "LapentaFrontierQuest" };
        var dir = Path.Combine(Path.GetTempPath(), "ms2-questcache-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CacheStore(dir);
            var parsed = QuestTable.Load(packs, languages, "KR", off, null, out _);     // the same load, without the cache
            Assert.That(QuestTable.BuildCache(packs, languages, "KR", off, store), Is.True);
            var cached = QuestTable.Load(packs, languages, "KR", off, store, out var outcome);
            Assert.That(outcome.Source, Is.EqualTo("cache"));
            Assert.That(Snapshot.Diff(parsed, cached), Is.Null);
            Assert.That(cached.Quests.Count(), Is.EqualTo(parsed.Quests.Count()).And.GreaterThan(1000));
            foreach (var q in parsed.Quests.Take(500))
                Assert.That(cached.QuestName(q.Id), Is.EqualTo(parsed.QuestName(q.Id)), $"quest {q.Id}");
            Assert.That(cached.Available("KR", "LapentaFrontierQuest"), Is.False);
            Assert.That(cached.Available("KR", "SomethingElse"), Is.EqualTo(parsed.Available("KR", "SomethingElse")));
            Assert.That(cached.Common("s_quest_ui_accept"), Is.EqualTo(parsed.Common("s_quest_ui_accept")));
            Assert.That(QuestTable.BuildCache(packs, languages, "KR", off, store), Is.False, "built once");
            // another language is another file, not this one read
            QuestTable.Load(packs, new GameStrings(packs, "kr").Languages, "KR", off, store, out var other);
            Assert.That(other.Source, Is.EqualTo("parse"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    /// <summary>The field meta table, its map graph and the field missions read back from their files are the parsed ones.</summary>
    [Test]
    public void CachedFieldTablesEqualTheParsedOnes()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        var parsedPacks = new PackSet(data);
        var languages = new GameStrings(parsedPacks, "en").Languages;
        var off = new HashSet<string> { "LapentaFrontierQuest" };
        var dir = Path.Combine(Path.GetTempPath(), "ms2-fieldcache-" + Guid.NewGuid().ToString("N"));
        var cachedPacks = new PackSet(data);
        var store = new CacheStore(dir);
        var outcomes = new List<CacheStore.Outcome>();
        var (of, logged) = (CacheStore.Of, CacheStore.Logged);
        try
        {
            // the parse: no store for this pack set
            var (meta, graph) = QuestMapGraph.Shared(parsedPacks);
            var words = new ReservedWords();
            var missions = FieldMissions.Load(parsedPacks, languages, "KR", off, words);

            Assert.That(QuestMapGraph.BuildCache(cachedPacks, store), Is.EqualTo(new[] { "fieldmeta", "questmap" }));
            Assert.That(FieldMissions.BuildCache(cachedPacks, languages, "KR", off, store), Is.True);
            Assert.That(QuestMapGraph.BuildCache(cachedPacks, store), Is.Empty, "built once");
            CacheStore.Of = p => ReferenceEquals(p, cachedPacks) ? store : null;
            CacheStore.Logged = outcomes.Add;
            var (cmeta, cgraph) = QuestMapGraph.Shared(cachedPacks);
            var cmissions = FieldMissions.Load(cachedPacks, languages, "KR", off, words);
            Assert.That(outcomes.Select(o => (o.Piece, o.Source)), Is.EqualTo(new[] { ("fieldmeta", "cache"), ("questmap", "cache"), ("missions", "cache") }));

            Assert.That(Snapshot.Diff(meta, cmeta), Is.Null);
            Assert.That(Snapshot.Diff(graph, cgraph), Is.Null);
            Assert.That(Snapshot.Diff(missions, cmissions), Is.Null);
            Assert.That(cgraph.Count, Is.EqualTo(graph.Count).And.GreaterThan(100));
            var fields = meta.PortalFields.Take(40).ToList();
            foreach (var a in fields)
                foreach (var b in fields)
                    Assert.That(cgraph.FindMapPath(a, b), Is.EqualTo(graph.FindMapPath(a, b)), $"path {a} -> {b}");
            foreach (var f in meta.ParentFields.Keys.Take(300).Concat(meta.PortalFields.Take(300)))
                Assert.That(cmissions.Goals(f), Is.EqualTo(missions.Goals(f)), $"field {f}");
        }
        finally
        {
            (CacheStore.Of, CacheStore.Logged) = (of, logged);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
