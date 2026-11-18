using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Swf.Abc;

// UiGen: translates every UI movie's ActionScript (the ABC blocks in Resource/Gfx) into C# on the Ms2.Avm2 runtime and
// compiles it into one assembly. The output is made from the player's own data and is never committed.
//   UiGen --out DIR [--dll FILE] [--only SUBSTR]    (MS2_DATA = the client's Data folder)
string data = Environment.GetEnvironmentVariable("MS2_DATA"), outDir = null, dll = null, only = null;
for (int i = 0; i < args.Length; i++)
    switch (args[i])
    {
        case "--data": data = args[++i]; break;
        case "--out": outDir = args[++i]; break;
        case "--dll": dll = args[++i]; break;
        case "--only": only = args[++i]; break;
    }
if (string.IsNullOrEmpty(data) || outDir == null) { Console.Error.WriteLine("usage: UiGen --out DIR [--dll FILE] [--only SUBSTR] (MS2_DATA)"); return 2; }
Directory.CreateDirectory(outDir);

var sw = Stopwatch.StartNew();
var packs = new PackSet(data);
var gfx = packs.List("Resource/Gfx").Where(e => e.EndsWith(".gfx", StringComparison.OrdinalIgnoreCase)).ToList();
var byKey = gfx.GroupBy(e => Path.GetFileNameWithoutExtension(e).ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
var src = new MovieSource(k => byKey.TryGetValue(k.ToLowerInvariant(), out var e) ? packs.Read("Resource/Gfx", e) : null);

var blocks = new Dictionary<string, (AbcFile Abc, string Class)>();
var movieBlocks = new Dictionary<string, List<string>>();
int movies = 0, withCode = 0;
foreach (var key in byKey.Keys.OrderBy(k => k, StringComparer.Ordinal))
{
    if (only != null && !key.Contains(only, StringComparison.OrdinalIgnoreCase)) continue;
    movies++;
    List<(string Name, AbcFile Abc)> abcs;
    try { abcs = src.Abc(key); } catch (Exception e) { Console.Error.WriteLine($"{key}: {e.Message}"); continue; }
    if (abcs.Count > 0) withCode++;
    movieBlocks[key] = abcs.Select(a => a.Abc.Hash).ToList();
    foreach (var (_, abc) in abcs)
        if (!blocks.ContainsKey(abc.Hash)) blocks[abc.Hash] = (abc, "Abc_" + abc.Hash[..16]);
}
double tRead = sw.Elapsed.TotalSeconds;

int methods = 0, skipped = 0;
var unsupported = new SortedSet<string>();
// every body's text (named by a placeholder); the same text is one shared method, whichever block it came from
var bodyId = new Dictionary<string, int>(StringComparer.Ordinal);
var bodies = new List<string>();
var blockIds = new Dictionary<string, int[]>();   // block hash -> body id per method index (-1: interpreted)
foreach (var (hash, (abc, _)) in blocks)
{
    var t = new Translator(abc);
    var ids = new int[abc.Methods.Length];
    for (int i = 0; i < ids.Length; i++)
    {
        var text = t.Body(abc.Methods[i]);
        if (text == null) { ids[i] = -1; continue; }
        if (!bodyId.TryGetValue(text, out int id)) { bodyId[text] = id = bodies.Count; bodies.Add(text); }
        ids[i] = id;
    }
    methods += t.Methods; skipped += t.Skipped;
    unsupported.UnionWith(t.Unsupported);
    blockIds[hash] = ids;
}
bodyId = null;

// the shared bodies, in files of a few thousand (Roslyn compiles files in parallel)
const int PerFile = 2000;
var files = new List<string>();
var fileOf = new int[bodies.Count];
for (int f = 0; f * PerFile < bodies.Count; f++)
{
    var sb = new StringBuilder(Translator.Header);
    sb.AppendLine($"static class Bodies{f}");
    sb.AppendLine("{");
    sb.AppendLine("    public static void Fill(CompiledBody[] T)");
    sb.AppendLine("    {");
    int end = Math.Min(bodies.Count, (f + 1) * PerFile);
    for (int id = f * PerFile; id < end; id++) sb.AppendLine($"        T[{id}] = B{id};");
    sb.AppendLine("    }");
    for (int id = f * PerFile; id < end; id++) { sb.Append(bodies[id].Replace(Translator.NamePlaceholder, "B" + id)); fileOf[id] = f; }
    sb.AppendLine("}");
    var path = Path.Combine(outDir, $"Bodies{f}.ms2gen.cs");
    File.WriteAllText(path, sb.ToString());
    files.Add(path);
}
int bodyCount = bodies.Count;
bodies = null;

// Init: the shared table, then each block's ids (one method per block keeps every method small)
string InitText(ISet<int> badFiles)
{
    var sb = new StringBuilder(Translator.Header);
    sb.AppendLine("public static class Ms2UiGen");
    sb.AppendLine("{");
    sb.AppendLine("    public static void Init()");
    sb.AppendLine("    {");
    sb.AppendLine($"        var T = new CompiledBody[{bodyCount}];");
    for (int f = 0; f < files.Count; f++) if (!badFiles.Contains(f)) sb.AppendLine($"        Bodies{f}.Fill(T);");
    int n = 0;
    foreach (var hash in blockIds.Keys) sb.AppendLine($"        R{n++}(T);");
    sb.AppendLine("    }");
    n = 0;
    foreach (var (hash, ids) in blockIds)
    {
        var list = string.Join(",", ids.Select(id => id >= 0 && !badFiles.Contains(fileOf[id]) ? id : -1));
        sb.AppendLine($"    static void R{n++}(CompiledBody[] T) => GeneratedCode.Register(\"{hash}\", T, new int[] {{ {list} }});");
    }
    sb.AppendLine("}");
    return sb.ToString();
}
var initPath = Path.Combine(outDir, "Ms2UiGen.ms2gen.cs");
File.WriteAllText(initPath, InitText(new HashSet<int>()));
double tTranslate = sw.Elapsed.TotalSeconds - tRead;
long srcBytes = files.Append(initPath).Sum(p => new FileInfo(p).Length);
Console.WriteLine($"movies {movies}, with code {withCode}; ABC blocks {blocks.Count}; method bodies {methods} translated, {skipped} skipped; {bodyCount} distinct");
if (unsupported.Count > 0) Console.WriteLine("unsupported: " + string.Join(", ", unsupported.Take(30)));
Console.WriteLine($"read {tRead:F1} s, translate {tTranslate:F1} s, source {srcBytes / 1048576.0:F1} MB in {files.Count + 1} files");

if (dll == null) return 0;

// compile; a body file with errors is left out (its bodies run in the interpreter) and Init is written again
var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
    .Where(p => Path.GetFileName(p).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(p) is "mscorlib.dll" or "netstandard.dll")
    .Select(p => MetadataReference.CreateFromFile(p)).ToList();
refs.Add(MetadataReference.CreateFromFile(typeof(Ms2.Avm2.Gen).Assembly.Location));
refs.Add(MetadataReference.CreateFromFile(typeof(AbcFile).Assembly.Location));
var parse = new CSharpParseOptions(LanguageVersion.Latest);
var trees = files.Select(p => CSharpSyntaxTree.ParseText(File.ReadAllText(p), parse, p, Encoding.UTF8)).ToList();
var opts = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, concurrentBuild: true);
var tc = Stopwatch.StartNew();
var initTree = CSharpSyntaxTree.ParseText(File.ReadAllText(initPath), parse, initPath, Encoding.UTF8);
var comp = CSharpCompilation.Create("Ms2.UiGen", trees.Append(initTree), refs, opts);
var bad = comp.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
foreach (var d in bad.Take(15)) Console.WriteLine("  " + d);
var badFiles = bad.Select(d => files.IndexOf(d.Location.SourceTree?.FilePath)).Where(i => i >= 0).ToHashSet();
if (badFiles.Count > 0)
{
    File.WriteAllText(initPath, InitText(badFiles));
    initTree = CSharpSyntaxTree.ParseText(File.ReadAllText(initPath), parse, initPath, Encoding.UTF8);
    comp = CSharpCompilation.Create("Ms2.UiGen", trees.Where((_, i) => !badFiles.Contains(i)).Append(initTree), refs, opts);
}
using (var fs = File.Create(dll))
{
    var r = comp.Emit(fs);
    if (!r.Success) foreach (var d in r.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(10)) Console.WriteLine("  emit: " + d);
}
double tCompile = tc.Elapsed.TotalSeconds;
var lost = blockIds.Where(kv => kv.Value.Any(id => id >= 0 && badFiles.Contains(fileOf[id]))).Select(kv => kv.Key).ToHashSet();
int moviesOk = movieBlocks.Count(kv => kv.Value.Count > 0 && kv.Value.All(h => !lost.Contains(h)));
Console.WriteLine($"compile {tCompile:F1} s: body files {files.Count - badFiles.Count}/{files.Count} compile as-is ({bad.Count} errors); " +
                  $"movies with code whose bodies all compile {moviesOk}/{movieBlocks.Count(kv => kv.Value.Count > 0)}; dll {new FileInfo(dll).Length / 1048576.0:F2} MB");
File.WriteAllLines(Path.Combine(outDir, "failed-blocks.txt"), movieBlocks.Where(kv => kv.Value.Any(lost.Contains)).Select(kv => kv.Key));
return 0;
