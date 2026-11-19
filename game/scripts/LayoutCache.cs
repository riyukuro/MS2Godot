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

    // the name as asked, case kept: a movie asked as an import's name and as its archive entry's ("CommonResource",
    // "commonResource") builds a layout of each name (its "movie" is the name), and each keeps its own file
    static string Piece(string key) => "layout-" + key;

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
            // a whole file the codec can't read (its checksum matched: a codec fault) builds the layout from the movie, as
            // without the cache; the file is removed, so the setup step (or the next load) writes it again
            try { return LayoutCodec.Read(bytes, withLibrary); }
            catch (Exception e)
            {
                s.Store.Remove(Piece(key), s.Config + "," + key);
                GD.PushWarning($"layout cache: {key}: unreadable ({e.GetType().Name}: {e.Message}); built from the movie");
                return withLibrary ? full() : plain();
            }
        })).Value;
    }

    // the names the layouts are asked by: the archive's movie names and their imports' names (an import names a movie
    // in its own case), one per line, written by the setup step once every file is built
    const string NamesPiece = "layoutnames";

    /// <summary>
    /// The setup step (a worker thread): writes the layout file of every movie, and of every import name, that has none;
    /// the count built. With the names file of this build in place no movie is parsed: one header read per name.
    /// </summary>
    public static int BuildAll(PackSet packs, Action<double> progress)
    {
        var s = Of(packs);
        if (s.Store == null || CacheStore.Mode == "off") return 0;
        var parsed = new Dictionary<string, SwfMovie>(StringComparer.OrdinalIgnoreCase);
        SwfMovie Movie(string k)
        {
            if (parsed.TryGetValue(k, out var m)) return m;
            var e = packs.FindByFileName("Resource/Gfx", k + ".gfx");
            return parsed[k] = e == null ? null : SwfMovie.Parse(packs.Read("Resource/Gfx", e));
        }
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(string k) { if (seen.Add(k)) names.Add(k); }
        foreach (var n in packs.List("Resource/Gfx").Where(n => n.EndsWith(".gfx", StringComparison.OrdinalIgnoreCase)))
            Add(System.IO.Path.GetFileNameWithoutExtension(n));
        var namesKey = CacheStore.Key(NamesPiece, s.Config, Build);
        var listed = s.Store.Read(NamesPiece, namesKey);
        if (listed != null)
            foreach (var k in System.Text.Encoding.UTF8.GetString(listed).Split('\n', StringSplitOptions.RemoveEmptyEntries)) Add(k);
        else
            // no names file: every movie is parsed for its imports (the movies are parsed here for this step alone, and
            // dropped after it)
            for (int i = 0; i < names.Count; i++)
                if (Movie(names[i]) is { } m)
                    foreach (var g in m.Imports)
                        if (System.IO.Path.GetFileNameWithoutExtension(g.Url) is var ik && Movie(ik) != null) Add(ik);
        var missing = names.Where(k => !s.Store.IsWhole(Piece(k), CacheStore.Key(Piece(k), s.Config + "," + k, Build))).ToList();
        int built = 0;
        for (int i = 0; i < missing.Count; i++)
        {
            var k = missing[i];
            if (Movie(k) is { } m && s.Store.Ensure(Piece(k), s.Config + "," + k, Build, () => LayoutCodec.Write(LayoutJson.Build(m, k, Movie)), b => b, onlyConfig: true))
                built++;
            progress?.Invoke((i + 1.0) / missing.Count);
        }
        if (listed == null) s.Store.Write(NamesPiece, namesKey, System.Text.Encoding.UTF8.GetBytes(string.Join("\n", names)), onlyConfig: true);
        return built;
    }
}
