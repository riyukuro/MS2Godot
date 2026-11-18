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
var files = new Dictionary<string, string>();     // hash -> path
foreach (var (hash, (abc, cls)) in blocks)
{
    var t = new Translator(abc);
    string text = Translator.Header + t.Class(cls);
    methods += t.Methods; skipped += t.Skipped;
    unsupported.UnionWith(t.Unsupported);
    var path = Path.Combine(outDir, cls + ".ms2gen.cs");
    File.WriteAllText(path, text);
    files[hash] = path;
}
var init = new StringBuilder(Translator.Header);
init.AppendLine("public static class Ms2UiGen");
init.AppendLine("{");
init.AppendLine("    public static void Init()");
init.AppendLine("    {");
foreach (var (_, cls) in blocks.Values) init.AppendLine($"        {cls}.Register();");
init.AppendLine("    }");
init.AppendLine("}");
var initPath = Path.Combine(outDir, "Ms2UiGen.ms2gen.cs");
File.WriteAllText(initPath, init.ToString());
double tTranslate = sw.Elapsed.TotalSeconds - tRead;
long srcBytes = files.Values.Append(initPath).Sum(p => new FileInfo(p).Length);
Console.WriteLine($"movies {movies}, with code {withCode}; ABC blocks {blocks.Count}; method bodies {methods} translated, {skipped} skipped");
if (unsupported.Count > 0) Console.WriteLine("unsupported: " + string.Join(", ", unsupported.Take(30)));
Console.WriteLine($"read {tRead:F1} s, translate {tTranslate:F1} s, source {srcBytes / 1048576.0:F1} MB");

if (dll == null) return 0;

// compile: every tree at once; trees with errors are reported by block and left out of the assembly
var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
    .Where(p => Path.GetFileName(p).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(p) is "mscorlib.dll" or "netstandard.dll")
    .Select(p => MetadataReference.CreateFromFile(p)).ToList();
refs.Add(MetadataReference.CreateFromFile(typeof(Ms2.Avm2.Gen).Assembly.Location));
refs.Add(MetadataReference.CreateFromFile(typeof(AbcFile).Assembly.Location));
var parse = new CSharpParseOptions(LanguageVersion.Latest);
var trees = files.ToDictionary(kv => kv.Key, kv => CSharpSyntaxTree.ParseText(File.ReadAllText(kv.Value), parse, kv.Value, Encoding.UTF8));
var opts = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, concurrentBuild: true);
var tc = Stopwatch.StartNew();
var comp = CSharpCompilation.Create("Ms2.UiGen", trees.Values, refs, opts);
var bad = comp.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
var badFiles = bad.Select(d => d.Location.SourceTree?.FilePath).Where(p => p != null).ToHashSet();
foreach (var d in bad.Take(15)) Console.WriteLine("  " + d);
var good = trees.Where(kv => !badFiles.Contains(kv.Value.FilePath)).ToList();
var initGood = new StringBuilder(Translator.Header + "public static class Ms2UiGen\n{\n    public static void Init()\n    {\n");
foreach (var kv in good) initGood.AppendLine($"        {blocks[kv.Key].Class}.Register();");
initGood.AppendLine("    }\n}");
comp = CSharpCompilation.Create("Ms2.UiGen", good.Select(kv => kv.Value).Append(CSharpSyntaxTree.ParseText(initGood.ToString(), parse, initPath)), refs, opts);
using (var fs = File.Create(dll))
{
    var r = comp.Emit(fs);
    if (!r.Success) foreach (var d in r.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(10)) Console.WriteLine("  emit: " + d);
}
double tCompile = tc.Elapsed.TotalSeconds;
var badHashes = trees.Where(kv => badFiles.Contains(kv.Value.FilePath)).Select(kv => kv.Key).ToHashSet();
int moviesOk = movieBlocks.Count(kv => kv.Value.Count > 0 && kv.Value.All(h => !badHashes.Contains(h)));
Console.WriteLine($"compile {tCompile:F1} s: blocks {blocks.Count - badHashes.Count}/{blocks.Count} compile as-is ({bad.Count} errors); " +
                  $"movies with code whose blocks all compile {moviesOk}/{movieBlocks.Count(kv => kv.Value.Count > 0)}; dll {new FileInfo(dll).Length / 1048576.0:F2} MB");
File.WriteAllLines(Path.Combine(outDir, "failed-blocks.txt"), movieBlocks.Where(kv => kv.Value.Any(badHashes.Contains)).Select(kv => kv.Key));
return 0;
