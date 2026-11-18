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
    readonly Func<string, byte[]> read;
    readonly Dictionary<string, SwfMovie> movies = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, List<(string Name, AbcFile Abc)>> abcs = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="read">Bytes of a movie by key without extension ("uilogindialog"); null when absent.</param>
    public MovieSource(Func<string, byte[]> read) { this.read = read; }

    public SwfMovie Movie(string key)
    {
        key = Path.GetFileNameWithoutExtension(key);
        if (movies.TryGetValue(key, out var m)) return m;
        var b = read(key);
        movies[key] = m = b == null ? null : SwfMovie.Parse(b);
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

    /// <summary>A runtime and player for one movie, its imports' code loaded first. The document class is not constructed yet (call Start).</summary>
    public (Runtime Rt, Player Player) Open(string key, Action<Runtime> setup = null)
    {
        key = Path.GetFileNameWithoutExtension(key);
        var m = Movie(key) ?? throw new FileNotFoundException("movie " + key);
        var layout = LayoutJson.Build(m, key, k => Movie(k));
        var rt = new Runtime();
        setup?.Invoke(rt);
        var importKeys = new List<string>();
        void ImportKeys(SwfMovie mm, HashSet<string> seen0)
        {
            foreach (var g in mm.Imports)
            {
                var ik = Path.GetFileNameWithoutExtension(g.Url);
                if (!seen0.Add(ik) || Movie(ik) is not { } im) continue;
                importKeys.Add(ik); ImportKeys(im, seen0);
            }
        }
        ImportKeys(m, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { key });
        var player = new Player(rt, layout, importKeys.Select(k => (k, Layout(k))));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { key };
        void Imports(SwfMovie mm)
        {
            foreach (var g in mm.Imports)
            {
                var ik = Path.GetFileNameWithoutExtension(g.Url);
                if (!seen.Add(ik)) continue;
                var im = Movie(ik);
                if (im == null) { rt.NoteMissing("movie " + ik); continue; }
                Imports(im);
                foreach (var (n, abc) in Abc(ik)) rt.Load(abc, ik + ":" + n);
            }
        }
        Imports(m);
        foreach (var (n, abc) in Abc(key)) rt.Load(abc, key + ":" + n);
        return (rt, player);
    }
}
