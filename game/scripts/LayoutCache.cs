using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using Ms2.Formats.Cache;
using Ms2.Formats.Pack;
using Ms2.Swf;
using Ms2Godot.Ui;

namespace Ms2Godot;

/// <summary>
/// The movies' layouts (<see cref="LayoutJson.Build"/>), kept per install in the folder of the client's Gfx.m2d
/// (user://uigen/KEY/, next to the UI code built from the same archive): one file per movie in <see cref="LayoutCodec"/>'s
/// form, keyed by the hashes of Gfx.m2d and Gfx.m2h and the build of the layout code. A session reads a movie's file
/// once, its symbols decoded as they are first asked for; a movie used as a library reads the same file without its
/// imports. A missing, stale or damaged file builds the layout from the movie as before, and writes the file.
/// MS2_UICACHE=off builds every layout as without the cache; =check builds it too and logs the first difference.
/// </summary>
public static class LayoutCache
{
    sealed class Set
    {
        public CacheStore Store;
        public string Config;
        public bool Ready;
        public readonly ConcurrentDictionary<(string, bool), Lazy<JsonObject>> Layouts = new();
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, Set> sets = new();

    static string Build => CacheStore.Build(typeof(LayoutJson).Assembly);

    static string Piece(string key) => "layout-" + key.ToLowerInvariant();

    static Set Of(PackSet packs)
    {
        var s = sets.GetOrCreateValue(packs);
        lock (s)
        {
            if (s.Ready) return s;
            s.Ready = true;
            try
            {
                var gfx = System.IO.Path.Combine(packs.DataDir, "Resource", "Gfx.m2d");
                s.Config = UiCode.Stamps.Hash(gfx) + "," + UiCode.Stamps.Hash(System.IO.Path.ChangeExtension(gfx, ".m2h"));
                s.Store = new CacheStore(System.IO.Path.GetDirectoryName(UiCode.DllPath(packs.DataDir)));
            }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
            {
                GD.PushWarning("layout cache: " + e.Message);
            }
            return s;
        }
    }

    /// <summary>
    /// The layout of movie <paramref name="key"/>, built once per pack set and shared (read-only: a caller that changes it
    /// takes a <see cref="LayoutCodec.Clone"/>). <paramref name="full"/> builds it with its imports' symbols (the file's
    /// content); <paramref name="plain"/> without them, as a movie used as a library is built (asked with
    /// <paramref name="withLibrary"/> false; the file read without its library gives the same layout).
    /// </summary>
    public static JsonObject Get(PackSet packs, string key, bool withLibrary, Func<JsonObject> full, Func<JsonObject> plain)
    {
        var s = Of(packs);
        return s.Layouts.GetOrAdd((key, withLibrary), _ => new Lazy<JsonObject>(() =>
        {
            if (s.Store == null || CacheStore.Mode == "off") return withLibrary ? full() : plain();
            var bytes = s.Store.Get(Piece(key), s.Config + "," + key, Build, () => LayoutCodec.Write(full()), b => b, b => b,
                (a, b) => a.AsSpan().SequenceEqual(b) ? null : LayoutCodec.Diff(LayoutCodec.Read(a), LayoutCodec.Read(b)) ?? "bytes differ",
                out var outcome, onlyConfig: true);
            if (outcome != null && (PerfLog.On || outcome.Diff != null)) GD.Print("layout cache: " + outcome);
            return LayoutCodec.Read(bytes, withLibrary);
        })).Value;
    }

    /// <summary>The setup step (a worker thread): writes the layout file of every movie that has none; the count built.</summary>
    public static int BuildAll(PackSet packs, Action<double> progress)
    {
        var s = Of(packs);
        if (s.Store == null || CacheStore.Mode == "off") return 0;
        var movies = packs.List("Resource/Gfx").Where(n => n.EndsWith(".gfx", StringComparison.OrdinalIgnoreCase)).ToList();
        // the movies are parsed here for this step alone (a layout reads its imports' symbols), and dropped after it
        var parsed = new Dictionary<string, SwfMovie>(StringComparer.OrdinalIgnoreCase);
        SwfMovie Movie(string k)
        {
            if (parsed.TryGetValue(k, out var m)) return m;
            var e = packs.FindByFileName("Resource/Gfx", k + ".gfx");
            return parsed[k] = e == null ? null : SwfMovie.Parse(packs.Read("Resource/Gfx", e));
        }
        int built = 0;
        for (int i = 0; i < movies.Count; i++)
        {
            var k = System.IO.Path.GetFileNameWithoutExtension(movies[i]);
            if (Movie(k) is { } m && s.Store.Ensure(Piece(k), s.Config + "," + k, Build, () => LayoutCodec.Write(LayoutJson.Build(m, k, Movie)), b => b, onlyConfig: true))
                built++;
            progress?.Invoke((i + 1.0) / movies.Count);
        }
        return built;
    }
}
