using Ms2.Formats.Pack;
using Ms2.Swf;

// GfxLayout: reads Scaleform .gfx / .swf UI movies and writes a layout description (JSON).
//   GfxLayout <movie> [<movie> ...] [--out DIR] [--data DIR] [--file] [--all]
// <movie> is a name inside the player's Resource/Gfx.m2d (e.g. uilogindialog or uilogindialog.gfx), or with --file a
// path to a loose .gfx/.swf. The Data folder comes from --data or the MS2_DATA environment variable.
// Imported shared symbols that the movie's code uses (component_v1, commonResource ...) are merged under "library"; --no-merge skips that.
// --all runs every .gfx in Gfx.m2d. --stats prints only the tag coverage. Without --out the JSON goes to standard output.

string data = Environment.GetEnvironmentVariable("MS2_DATA"), outDir = null;
bool file = false, stats = false, all = false, noMerge = false;
var names = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--data": data = args[++i]; break;
        case "--out": outDir = args[++i]; break;
        case "--file": file = true; break;
        case "--stats": stats = true; break;
        case "--all": all = true; break;
        case "--no-merge": noMerge = true; break;
        case "-h": case "--help": Console.WriteLine("usage: GfxLayout <movie>... [--out DIR] [--data DIR] [--file] [--all] [--stats]"); return 0;
        default: names.Add(args[i]); break;
    }
}
if (all && !file && !string.IsNullOrEmpty(data))
    names.AddRange(new PackSet(data).List("Resource/Gfx").Where(e => e.EndsWith(".gfx", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e));
if (names.Count == 0) { Console.Error.WriteLine("no movie given (try --help)"); return 2; }
if (!file && string.IsNullOrEmpty(data)) { Console.Error.WriteLine("set MS2_DATA or pass --data (the client's Data folder)"); return 2; }
if (outDir != null) Directory.CreateDirectory(outDir);

PackSet packs = file ? null : new PackSet(data);
int rc = 0, movies = 0, failedMovies = 0;
var total = new SortedDictionary<int, int[]>();
foreach (var n in names)
{
    try
    {
        byte[] bytes; string name;
        if (file) { bytes = File.ReadAllBytes(n); name = Path.GetFileNameWithoutExtension(n); }
        else
        {
            var entry = n.Contains('.') ? n : n + ".gfx";
            bytes = packs.Read("Resource/Gfx", entry);
            name = Path.GetFileNameWithoutExtension(entry);
        }
        var movie = SwfMovie.Parse(bytes);
        Func<string, SwfMovie> loader = null;
        if (!file && !noMerge)
            loader = key => { var e = packs.FindByFileName("Resource/Gfx", key + ".gfx"); return e == null ? null : SwfMovie.Parse(packs.Read("Resource/Gfx", e)); };
        var json = LayoutJson.Build(movie, name, loader);
        var cov = json["coverage"];
        movies++;
        foreach (var (code, st) in movie.Stats)
        {
            if (!total.TryGetValue(code, out var t)) total[code] = t = new int[3];
            t[0] += st.Parsed; t[1] += st.Skipped; t[2] += st.Failed;
        }
        if (movie.Problems.Count > 0) failedMovies++;
        Console.Error.WriteLine($"{name}: stage {movie.Stage.W}x{movie.Stage.H}, {movie.Characters.Count} characters, tags parsed {cov["parsed"]} skipped {cov["skipped"]} failed {cov["failed"]}");
        if (stats) continue;
        var text = LayoutJson.ToJson(json);
        if (outDir == null) Console.WriteLine(text); else File.WriteAllText(Path.Combine(outDir, name + ".json"), text);
    }
    catch (Exception e) { Console.Error.WriteLine($"{n}: {e.GetType().Name}: {e.Message}"); rc = 1; }
}
if (names.Count > 1)
{
    Console.Error.WriteLine($"{movies} movies, {failedMovies} with problems; tags parsed {total.Values.Sum(t => t[0])}, skipped {total.Values.Sum(t => t[1])}, failed {total.Values.Sum(t => t[2])}");
    foreach (var (code, t) in total) if (t[1] + t[2] > 0) Console.Error.WriteLine($"  tag {code}: parsed {t[0]} skipped {t[1]} failed {t[2]}");
}
return rc;
