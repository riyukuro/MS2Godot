using Ms2.Formats.Cache;
using Ms2.Formats.Pack;

namespace Ms2.Quest;

public sealed partial class QuestTable
{
    /// <summary>The cache configuration of a load: the string languages in order, the locale, the features turned off.</summary>
    public static string CacheConfig(string[] languages, string locale, IEnumerable<string> featuresOff) =>
        $"{string.Join(",", languages)}|{locale}|{string.Join(",", featuresOff.OrderBy(f => f, StringComparer.Ordinal))}";

    public static string CacheBuild => CacheStore.Build(typeof(QuestTable).Assembly);

    /// <summary>
    /// <see cref="Load(PackSet, string[], string, Func{string, bool})"/> with the features named in
    /// <paramref name="featuresOff"/> off, from the install's cache when it holds this load (else parsed, and cached).
    /// </summary>
    public static QuestTable Load(PackSet packs, string[] languages, string locale, IReadOnlyCollection<string> featuresOff,
                                  CacheStore cache, out CacheStore.Outcome outcome)
    {
        var featureOn = FeatureOn(featuresOff);
        if (cache == null) { outcome = null; return Load(packs, languages, locale, featureOn); }
        return cache.Get("quests", CacheConfig(languages, locale, featuresOff), CacheBuild,
                         () => Load(packs, languages, locale, featureOn), t => t.Wire(featureOn), out outcome);
    }

    /// <summary>Writes the cache file of this load when it is missing (the setup step).</summary>
    public static bool BuildCache(PackSet packs, string[] languages, string locale, IReadOnlyCollection<string> featuresOff, CacheStore cache) =>
        cache.Ensure("quests", CacheConfig(languages, locale, featuresOff), CacheBuild,
                     () => Load(packs, languages, locale, FeatureOn(featuresOff)));

    // its own method, so the closure holds the feature set only (not the load's other arguments)
    static Func<string, bool> FeatureOn(IReadOnlyCollection<string> featuresOff) => f => !featuresOff.Contains(f);

    /// <summary>A table read back from <see cref="Snapshot"/> bytes, wired as a parsed one.</summary>
    public static QuestTable FromSnapshot(ReadOnlyMemory<byte> body, Func<string, bool> featureOn)
    {
        var t = Snapshot.Read<QuestTable>(body);
        t.Wire(featureOn);
        return t;
    }
}
