using Ms2.Formats.Cache;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The game tables loaded through their cache file are the parsed ones.</summary>
public class GameTablesCacheTests
{
    [Test]
    public void CachedTablesEqualTheParsedOnes()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        var parsedPacks = new PackSet(data);
        var cachedPacks = new PackSet(data);
        var dir = Path.Combine(Path.GetTempPath(), "ms2-gametables-" + Guid.NewGuid().ToString("N"));
        var store = new CacheStore(dir);
        var outcomes = new List<CacheStore.Outcome>();
        var (of, logged) = (CacheStore.Of, CacheStore.Logged);
        try
        {
            var parsed = new GameTables(parsedPacks, "en");      // no store for this pack set: the parse
            CacheStore.Of = p => p == cachedPacks ? store : null;
            CacheStore.Logged = outcomes.Add;
            Assert.That(GameTables.BuildCache(cachedPacks, "en", store), Is.True);
            Assert.That(GameTables.BuildCache(cachedPacks, "en", store), Is.False, "built once");
            var cached = new GameTables(cachedPacks, "en");
            Assert.That(outcomes.Select(o => (o.Piece, o.Source)), Is.EqualTo(new[] { ("gametables", "cache") }));
            Assert.That(GameTables.ReadDiff(parsed, cached), Is.Null);
            for (int id = 0; id < 20000000; id += 997)
                Assert.That(cached.ItemName(id), Is.EqualTo(parsed.ItemName(id)), $"item {id}");
            foreach (var key in new[] { "s_quest_ui_accept", "s_word_level", "s_common_ok", "s_key_jump" })
                Assert.That(cached.Common(key), Is.EqualTo(parsed.Common(key)), key);
            for (int id = 0; id < 3000; id++) Assert.That(cached.Common(id), Is.EqualTo(parsed.Common(id)), $"common {id}");
            for (int map = 2000000; map < 2010000; map++) Assert.That(cached.MapName(map), Is.EqualTo(parsed.MapName(map)), $"map {map}");
            for (int stat = 0; stat < 0x23; stat++)
            {
                Assert.That(cached.StatName(stat), Is.EqualTo(parsed.StatName(stat)));
                Assert.That(cached.StatOption(stat), Is.EqualTo(parsed.StatOption(stat)));
            }
            for (int lv = 0; lv < 100; lv++) Assert.That(cached.NextExp(lv), Is.EqualTo(parsed.NextExp(lv)));
            Assert.That(cached.MaxLevel, Is.EqualTo(parsed.MaxLevel).And.GreaterThan(0));
            Assert.That(cached.JobName(10), Is.EqualTo(parsed.JobName(10)));
            for (int g = 0; g < 200; g++) Assert.That(cached.Skills.Jobs.GetSubJob1(g), Is.EqualTo(parsed.Skills.Jobs.GetSubJob1(g)));
            for (uint v = 0; v < 100000; v += 777) Assert.That(cached.Fishing.Grade(v), Is.EqualTo(parsed.Fishing.Grade(v)));
            Assert.That(cached.CommonResource("HDR_PATH"), Is.EqualTo(parsed.CommonResource("HDR_PATH")));
            Assert.That(cached.EquipmentSellRate(3), Is.EqualTo(parsed.EquipmentSellRate(3)));
            // another language is another file
            outcomes.Clear();
            _ = new GameTables(cachedPacks, "kr");
            Assert.That(outcomes.Single().Source, Is.EqualTo("parse"));
        }
        finally
        {
            (CacheStore.Of, CacheStore.Logged) = (of, logged);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
