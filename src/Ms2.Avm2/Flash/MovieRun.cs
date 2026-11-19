using System.Text.Json.Nodes;
using Ms2.Swf;
using Ms2.Swf.Abc;

namespace Ms2.Avm2.Flash;

/// <summary>
/// Loads a UI movie for the AS3 runtime: its layout, the ABC of the movies it imports (the shared CLIK / scaleform.ms2
/// library lives in commonresource), then its own ABC. Parsed movies and ABC blocks are cached across movies.
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

    public SwfMovie Movie(string key)
    {
        key = Path.GetFileNameWithoutExtension(key);
        if (movies.TryGetValue(key, out var m)) return m;
        movies[key] = m = parse(key);
        return m;
    }

    public List<(string Name, AbcFile Abc)> Abc(string key)
    {
        key = Path.GetFileNameWithoutExtension(key);
        if (abcs.TryGetValue(key, out var l)) return l;
        var m = Movie(key);
        abcs[key] = l = m == null ? new() : m.AbcBlocks.Select(b => (b.Name, AbcFile.Parse(b.Data))).ToList();
        return l;
    }

    readonly Dictionary<string, JsonObject> layouts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The layout of a movie used as a library (built once; read-only).</summary>
    public JsonObject Layout(string key)
    {
        key = Path.GetFileNameWithoutExtension(key);
        if (layouts.TryGetValue(key, out var l)) return l;
        var m = Movie(key);
        layouts[key] = l = m == null ? null : LayoutJson.Build(m, key, null);
        return l;
    }

    readonly Dictionary<string, JsonObject> mains = new(StringComparer.OrdinalIgnoreCase);

    // a movie's own layout (its imports resolved), built once like the library layouts: the build is pure and the player
    // and views only read it
    JsonObject MainLayout(string key, SwfMovie m)
    {
        if (mains.TryGetValue(key, out var l)) return l;
        return mains[key] = LayoutJson.Build(m, key, k => Movie(k));
    }

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
        return (rt, player);
    }
}
