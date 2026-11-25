using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;

namespace Ms2.Ui.Tests;

/// <summary>
/// One pack set per data folder and one movie source per pack set for the whole run, as the game keeps them (PackSet.Shared,
/// UiAssets.Movies). The per-pack-set caches (archive indexes, tables keyed by pack set) and the parsed movies, ABC and layouts
/// are then built once, not once per fixture: with a pack set and a movie source per fixture the suite held ~90 copies of
/// the shared library movies' layouts and fonts and peaked near 12 GB. For fixtures that keep their pack set for the run;
/// a test that disposes its pack set or needs fresh caches opens its own.
/// </summary>
static class TestData
{
    static readonly ConcurrentDictionary<string, Lazy<PackSet>> packs = new(StringComparer.Ordinal);
    static readonly ConditionalWeakTable<PackSet, MovieSource> movies = new();

    public static PackSet Packs(string dataDir) => packs.GetOrAdd(dataDir, d => new Lazy<PackSet>(() => new PackSet(d))).Value;

    public static MovieSource Movies(PackSet p) => movies.GetValue(p,
        k => new MovieSource(n => k.FindByFileName("Resource/Gfx", n + ".gfx") is { } e ? k.Read("Resource/Gfx", e) : null));

    static readonly ConditionalWeakTable<PackSet, ConcurrentDictionary<string, Lazy<Ms2.Swf.SwfMovie>>> libraries = new();

    /// <summary>
    /// A source for one movie of a sweep over every movie: the movie itself, its code and layouts are dropped with the source, and
    /// only the movies it imports (the shared libraries) are kept, parsed once per pack set. The run-long <see cref="Movies"/> would
    /// keep all ~500 movies for the rest of the run.
    /// </summary>
    public static MovieSource SweepSource(PackSet p, string movie)
    {
        var libs = libraries.GetValue(p, _ => new(StringComparer.OrdinalIgnoreCase));
        Ms2.Swf.SwfMovie Parse(string n) => p.FindByFileName("Resource/Gfx", n + ".gfx") is { } e ? Ms2.Swf.SwfMovie.Parse(p.Read("Resource/Gfx", e)) : null;
        return MovieSource.FromParsed(n => string.Equals(n, movie, StringComparison.OrdinalIgnoreCase) ? Parse(n)
            : libs.GetOrAdd(n, k => new Lazy<Ms2.Swf.SwfMovie>(() => Parse(k))).Value);
    }
}
