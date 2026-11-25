using System.Text.Json.Nodes;
using Ms2.Swf;
using Ms2.Swf.Abc;

namespace Ms2.Avm2.Flash;

/// <summary>
/// Loads a UI movie for the AS3 runtime: its layout, the ABC of the movies it imports (the shared CLIK / scaleform.ms2
/// library lives in commonresource), then its own ABC. Parsed movies and ABC blocks are cached across movies.
/// One source may be shared by threads (the tests' fixtures run side by side on one source per data folder): each cache entry
/// is built outside the lock and the first one stored is the one every caller gets; the builds are pure, so a second build
/// of the same entry is only wasted work.
/// </summary>
public sealed class MovieSource
{
    readonly Func<string, SwfMovie> parse;
    readonly Dictionary<string, SwfMovie> movies = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, List<(string Name, AbcFile Abc)>> abcs = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="read">Bytes of a movie by key without extension ("uilogindialog"); null when absent.</param>
    public MovieSource(Func<string, byte[]> read) { parse = k => read(k) is { } b ? SwfMovie.Parse(b) : null; }

    MovieSource(Func<string, SwfMovie> parsed) { parse = parsed; }

    /// <summary>
    /// A source over movies already parsed elsewhere (a cache shared by every load: the parse is pure and nothing changes a
    /// parsed movie). <paramref name="parsed"/> gives a movie by key without extension, null when absent.
    /// </summary>
    public static MovieSource FromParsed(Func<string, SwfMovie> parsed) => new(parsed);

    static T Cached<T>(Dictionary<string, T> cache, string key, Func<T> build)
    {
        lock (cache) if (cache.TryGetValue(key, out var v)) return v;
        var b = build();
        lock (cache) return cache.TryAdd(key, b) ? b : cache[key];
    }

    public SwfMovie Movie(string key)
    {
        key = Path.GetFileNameWithoutExtension(key);
        return Cached(movies, key, () => parse(key));
    }

    public List<(string Name, AbcFile Abc)> Abc(string key)
    {
        key = Path.GetFileNameWithoutExtension(key);
        return Cached(abcs, key, () => Movie(key) is { } m ? m.AbcBlocks.Select(b => (b.Name, AbcFile.Parse(b.Data))).ToList() : new());
    }

    readonly Dictionary<string, JsonObject> layouts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Where the layouts come from when set (the game's per-install files): given the movie key, whether its imports'
    /// symbols are wanted (a main movie) or not (a library), and the two builds it stands for, it returns that layout.
    /// Unset, the layouts are built here.
    /// </summary>
    public Func<string, bool, Func<JsonObject>, Func<JsonObject>, JsonObject> Layouts { get; set; }

    /// <summary>The layout of a movie used as a library (built once; read-only).</summary>
    public JsonObject Layout(string key)
    {
        key = Path.GetFileNameWithoutExtension(key);
        var k0 = key;
        return Cached(layouts, key, () => Movie(k0) is not { } m ? null
            : Layouts != null ? Layouts(k0, false, () => LayoutJson.Build(m, k0, k => Movie(k)), () => LayoutJson.Build(m, k0, null))
            : LayoutJson.Build(m, k0, null));
    }

    readonly Dictionary<string, JsonObject> mains = new(StringComparer.OrdinalIgnoreCase);

    // a movie's own layout (its imports resolved), built once like the library layouts: the build is pure and the player
    // and views only read it
    JsonObject MainLayout(string key, SwfMovie m) => Cached(mains, key,
        () => Layouts != null ? Layouts(key, true, () => LayoutJson.Build(m, key, k => Movie(k)), null) : LayoutJson.Build(m, key, k => Movie(k)));

    /// <summary>A runtime and player for one movie, its imports' code loaded first. The document class is not constructed yet (call Start).</summary>
    /// <param name="libraries">
    /// Movies the movie does not import but finds loaded beside it: the client loads each UI swf into a shared root movie
    /// (GFxManager::Initialize @0x1422ad620 roots, UISwfEntityManager::LoadSwf @0x1422d1320 by the entity's group), and
    /// getDefinitionByName looks up the root VM's one class table (VM::GetClass @0x142507be0 over vm.ClassTraitsSet), so a
    /// class of any movie in that root, or of what those movies import, resolves. These load like imports, before the
    /// movie's own imports.
    /// </param>
    public (Runtime Rt, Player Player) Open(string key, Action<Runtime> setup = null, IEnumerable<string> libraries = null)
    {
        key = Path.GetFileNameWithoutExtension(key);
        var m = Movie(key) ?? throw new FileNotFoundException("movie " + key);
        var layout = MainLayout(key, m);
        var rt = new Runtime();
        setup?.Invoke(rt);
        var libs = libraries?.Select(Path.GetFileNameWithoutExtension).ToList() ?? new List<string>();
        IEnumerable<string> Direct(SwfMovie mm) => mm.Imports.Select(g => Path.GetFileNameWithoutExtension(g.Url));
        var importKeys = new List<string>();
        void ImportKeys(IEnumerable<string> keys, HashSet<string> seen0)
        {
            foreach (var ik in keys)
            {
                if (!seen0.Add(ik) || Movie(ik) is not { } im) continue;
                importKeys.Add(ik); ImportKeys(Direct(im), seen0);
            }
        }
        ImportKeys(libs.Concat(Direct(m)), new HashSet<string>(StringComparer.OrdinalIgnoreCase) { key });
        var player = new Player(rt, layout, importKeys.Select(k => (k, Layout(k))));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { key };
        void Imports(IEnumerable<string> keys)
        {
            foreach (var ik in keys)
            {
                if (!seen.Add(ik)) continue;
                var im = Movie(ik);
                if (im == null) { rt.NoteMissing("movie " + ik); continue; }
                Imports(Direct(im));
                foreach (var (n, abc) in Abc(ik)) rt.Load(abc, ik + ":" + n);
            }
        }
        Imports(libs.Concat(Direct(m)));
        foreach (var (n, abc) in Abc(key)) rt.Load(abc, key + ":" + n);
        // Loader.load of another movie ("BG_mask_shadow.swf"): the client opens it through the GFx file opener like the
        // dialog movies themselves, which it names "<name>.swf" too (GetSwfFileName, ms2memfile.cpp:362-376;
        // OpenGfxFile, ms2memfile.cpp:519-530), so it resolves by the same key as Open. Its code and imports load first.
        player.LoadUrl = (l, url) =>
        {
            if (url == null || !(url.EndsWith(".swf", StringComparison.OrdinalIgnoreCase) || url.EndsWith(".gfx", StringComparison.OrdinalIgnoreCase))) return;
            var ck = Path.GetFileNameWithoutExtension(url.Replace('\\', '/'));
            if (Movie(ck) is not { } cm) { rt.NoteMissing("movie " + ck); return; }
            if (seen.Add(ck)) { Imports(Direct(cm)); foreach (var (n, abc) in Abc(ck)) rt.Load(abc, ck + ":" + n); }
            player.LoadMovie(l, ck, MainLayout(ck, cm));
        };
        return (rt, player);
    }
}
