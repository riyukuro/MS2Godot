using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Swf.Abc;

namespace Ms2.UiGen;

/// <summary>
/// Builds the UI code assembly from the player's own client: every UI movie's ActionScript (the ABC blocks in
/// Resource/Gfx) translated to C# on the Ms2.Avm2 runtime (<see cref="Translator"/>), one method per distinct body,
/// compiled with Roslyn. The output is made on the player's machine and is never committed.
/// </summary>
public static class UiGenerator
{
    /// <summary>Raise when the translator's output changes, so the cached assemblies are built again.</summary>
    public const int Version = 4;
    /// <summary>The file name of the built assembly.</summary>
    public const string DllName = "Ms2.UiCode.dll";

    public sealed class Result
    {
        public int Movies, MoviesWithCode, Blocks, Bodies, Skipped, Distinct, BodyFiles, BadBodyFiles, MoviesAllCompiled;
        public long SourceBytes, DllBytes;
        public double ReadSeconds, TranslateSeconds, CompileSeconds;
        public List<string> Errors = new(), Unsupported = new(), FailedMovies = new();
        /// <summary>Version 4: the bodies translated as version 3 instead, by reason.</summary>
        public SortedDictionary<string, int> Fallbacks = new(StringComparer.Ordinal);
        public bool Ok => DllBytes > 0;
    }

    /// <summary>
    /// The cache key of a data folder's UI code: the SHA-1 of Resource/Gfx.m2d with the translator <see cref="Version"/>.
    /// The hash of a file whose size and write time match the stamp in <paramref name="stampFile"/> is taken from it.
    /// </summary>
    public static string Key(string dataDir, string stampFile)
    {
        var gfx = Path.Combine(dataDir, "Resource", "Gfx.m2d");
        var fi = new FileInfo(gfx);
        string stamp = $"{fi.Length} {fi.LastWriteTimeUtc.Ticks}";
        string hash = null;
        if (stampFile != null && File.Exists(stampFile) && File.ReadAllText(stampFile).Split('\n') is { Length: >= 2 } l && l[0] == stamp) hash = l[1].Trim();
        if (hash == null)
        {
            using (var s = File.OpenRead(gfx)) hash = Convert.ToHexString(SHA1.HashData(s)).ToLowerInvariant();
            if (stampFile != null) { Directory.CreateDirectory(Path.GetDirectoryName(stampFile)); File.WriteAllText(stampFile, stamp + "\n" + hash + "\n"); }
        }
        return KeyOf(hash);
    }

    /// <summary>The cache key of the UI code of a Gfx.m2d whose SHA-1 (hex) is <paramref name="gfxSha1"/>.</summary>
    public static string KeyOf(string gfxSha1) => $"{gfxSha1[..16]}-v{Version}";

    /// <summary>The Gfx.m2d hash prefix of a key made by <see cref="KeyOf"/> for this translator version, else null.</summary>
    public static string HashOfKey(string key) =>
        key.Length == 16 + 2 + Version.ToString().Length && key.EndsWith($"-v{Version}", StringComparison.Ordinal) ? key[..16] : null;

    /// <summary>
    /// Translates and compiles every movie of <paramref name="dataDir"/> into <paramref name="dllPath"/>. With
    /// <paramref name="sourceDir"/> the C# is also written there (a developer's local copy; never commit it).
    /// <paramref name="progress"/> gets a phase text and a fraction 0..1. <paramref name="assemblyDirs"/>: where to find
    /// Ms2.Avm2.dll and Ms2.Swf.dll when the host loaded them from memory (Godot does), so they have no location.
    /// </summary>
    public static Result Build(string dataDir, string dllPath, string sourceDir = null, Action<string, double> progress = null,
                               IEnumerable<string> assemblyDirs = null)
    {
        var res = new Result();
        progress ??= (_, _) => { };
        var sw = Stopwatch.StartNew();
        using var packs = new PackSet(dataDir);
        var gfx = packs.List("Resource/Gfx").Where(e => e.EndsWith(".gfx", StringComparison.OrdinalIgnoreCase)).ToList();
        var byKey = gfx.GroupBy(e => Path.GetFileNameWithoutExtension(e).ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        var src = new MovieSource(k => byKey.TryGetValue(k.ToLowerInvariant(), out var e) ? packs.Read("Resource/Gfx", e) : null);

        var blocks = new Dictionary<string, AbcFile>();
        var movieBlocks = new Dictionary<string, List<string>>();
        var keys = byKey.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        foreach (var key in keys)
        {
            res.Movies++;
            progress("Reading the UI movies", 0.3 * res.Movies / keys.Count);
            List<(string Name, AbcFile Abc)> abcs;
            try { abcs = src.Abc(key); } catch (Exception e) { res.Errors.Add($"{key}: {e.Message}"); continue; }
            if (abcs.Count > 0) res.MoviesWithCode++;
            movieBlocks[key] = abcs.Select(a => a.Abc.Hash).ToList();
            foreach (var (_, abc) in abcs) blocks.TryAdd(abc.Hash, abc);
        }
        res.Blocks = blocks.Count;
        res.ReadSeconds = sw.Elapsed.TotalSeconds;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dllPath)));
        string tmp = dllPath + ".tmp";
        HashSet<string> lost;
        using (var fs = File.Create(tmp)) lost = Compile(blocks, fs, res, sourceDir, progress, assemblyDirs);
        if (lost == null) { File.Delete(tmp); return res; }
        File.Move(tmp, dllPath, true);      // the finished file appears at once, so a stopped build leaves no half assembly
        res.DllBytes = new FileInfo(dllPath).Length;
        res.MoviesAllCompiled = movieBlocks.Count(kv => kv.Value.Count > 0 && kv.Value.All(h => !lost.Contains(h)));
        res.FailedMovies = movieBlocks.Where(kv => kv.Value.Any(lost.Contains)).Select(kv => kv.Key).ToList();
        progress("Done", 1);
        return res;
    }

    /// <summary>
    /// Translates <paramref name="blocks"/> (by hash) and writes the compiled assembly to <paramref name="output"/>; the
    /// statistics go to <paramref name="res"/>. Returns the blocks that lost a body to a file with errors (they run those
    /// in the interpreter), or null when nothing could be emitted.
    /// </summary>
    public static HashSet<string> Compile(IReadOnlyDictionary<string, AbcFile> blocks, Stream output, Result res, string sourceDir = null,
                                          Action<string, double> progress = null, IEnumerable<string> assemblyDirs = null, int version = Version)
    {
        progress ??= (_, _) => { };
        var sw = Stopwatch.StartNew();
        // every body's text; the same text is one shared method, whichever block it came from
        var bodyId = new Dictionary<string, int>(StringComparer.Ordinal);
        var bodies = new List<string>();
        var blockIds = new Dictionary<string, int[]>();
        var unsupported = new SortedSet<string>();
        int done = 0;
        foreach (var (hash, abc) in blocks)
        {
            progress("Translating the UI code", 0.3 + 0.2 * done++ / Math.Max(1, blocks.Count));
            var t = new Translator(abc, v4: version >= 4);
            var ids = new int[abc.Methods.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                var text = t.Body(abc.Methods[i]);
                if (text == null) { ids[i] = -1; continue; }
                if (!bodyId.TryGetValue(text, out int id)) { bodyId[text] = id = bodies.Count; bodies.Add(text); }
                ids[i] = id;
            }
            res.Bodies += t.Methods; res.Skipped += t.Skipped;
            unsupported.UnionWith(t.Unsupported);
            foreach (var (why, k) in t.Fallbacks) res.Fallbacks[why] = res.Fallbacks.GetValueOrDefault(why) + k;
            blockIds[hash] = ids;
        }
        bodyId = null;
        res.Distinct = bodies.Count;
        res.Unsupported = unsupported.ToList();

        // the shared bodies, in files of a few thousand (Roslyn compiles files in parallel)
        const int PerFile = 2000;
        var texts = new List<string>();
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
            texts.Add(sb.ToString());
        }
        int bodyCount = bodies.Count;
        bodies = null;
        res.BodyFiles = texts.Count;

        // Init: the shared table, then each block's ids (one method per block keeps every method small)
        string InitText(ISet<int> badFiles)
        {
            var sb = new StringBuilder(Translator.Header);
            sb.AppendLine("public static class Ms2UiGen");
            sb.AppendLine("{");
            sb.AppendLine("    public static void Init()");
            sb.AppendLine("    {");
            sb.AppendLine($"        var T = new CompiledBody[{bodyCount}];");
            for (int f = 0; f < texts.Count; f++) if (!badFiles.Contains(f)) sb.AppendLine($"        Bodies{f}.Fill(T);");
            int n = 0;
            foreach (var _ in blockIds.Keys) sb.AppendLine($"        R{n++}(T);");
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
        string init = InitText(new HashSet<int>());
        res.SourceBytes = texts.Sum(t => (long)Encoding.UTF8.GetByteCount(t)) + Encoding.UTF8.GetByteCount(init);
        string Name(int f) => f < 0 ? "Ms2UiGen.ms2gen.cs" : $"Bodies{f}.ms2gen.cs";
        if (sourceDir != null)
        {
            Directory.CreateDirectory(sourceDir);
            for (int f = 0; f < texts.Count; f++) File.WriteAllText(Path.Combine(sourceDir, Name(f)), texts[f]);
            File.WriteAllText(Path.Combine(sourceDir, Name(-1)), init);
        }
        res.TranslateSeconds = sw.Elapsed.TotalSeconds;

        // compile; a body file with errors is left out (its bodies run in the interpreter) and Init is made again
        progress("Compiling the UI code", 0.55);
        var tc = Stopwatch.StartNew();
        var refs = References(assemblyDirs);
        var parse = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = texts.Select((t, f) => CSharpSyntaxTree.ParseText(t, parse, Name(f), Encoding.UTF8)).ToList();
        texts = null;
        var opts = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, concurrentBuild: true);
        var comp = CSharpCompilation.Create("Ms2.UiCode", trees.Append(CSharpSyntaxTree.ParseText(init, parse, Name(-1), Encoding.UTF8)), refs, opts);
        var bad = comp.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        foreach (var d in bad.Take(15)) res.Errors.Add(d.ToString());
        var badFiles = bad.Select(d => trees.IndexOf(d.Location.SourceTree)).Where(i => i >= 0).ToHashSet();
        if (badFiles.Count > 0)
            comp = CSharpCompilation.Create("Ms2.UiCode", trees.Where((_, i) => !badFiles.Contains(i))
                .Append(CSharpSyntaxTree.ParseText(InitText(badFiles), parse, Name(-1), Encoding.UTF8)), refs, opts);
        res.BadBodyFiles = badFiles.Count;
        progress("Compiling the UI code", 0.85);
        var r = comp.Emit(output);
        if (!r.Success) { foreach (var d in r.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(10)) res.Errors.Add("emit: " + d); return null; }
        res.CompileSeconds = tc.Elapsed.TotalSeconds;
        return blockIds.Where(kv => kv.Value.Any(id => id >= 0 && badFiles.Contains(fileOf[id]))).Select(kv => kv.Key).ToHashSet();
    }

    /// <summary>
    /// Translates and compiles <paramref name="blocks"/> in memory and registers their bodies in this process (a test
    /// harness runs them against the interpreter), translated as <paramref name="version"/> (3 or 4). Returns the build statistics.
    /// </summary>
    public static Result LoadInMemory(IEnumerable<AbcFile> blocks, int version = Version)
    {
        var res = new Result();
        var map = new Dictionary<string, AbcFile>();
        foreach (var b in blocks) map.TryAdd(b.Hash, b);
        res.Blocks = map.Count;
        using var ms = new MemoryStream();
        if (Compile(map, ms, res, version: version) == null) return res;
        res.DllBytes = ms.Length;
        var asm = System.Reflection.Assembly.Load(ms.ToArray());
        asm.GetType("Ms2UiGen").GetMethod("Init").Invoke(null, null);
        return res;
    }

    /// <summary>What the generated code compiles against: the runtime's framework assemblies and Ms2.Avm2 / Ms2.Swf.</summary>
    static List<MetadataReference> References(IEnumerable<string> assemblyDirs)
    {
        var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "").Split(Path.PathSeparator)
            .Where(p => Path.GetFileName(p).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(p) is "mscorlib.dll" or "netstandard.dll")
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
        if (refs.Count == 0)        // a host without the list: the framework folder of the running runtime
            refs = Directory.GetFiles(Path.GetDirectoryName(typeof(object).Assembly.Location), "*.dll")
                .Where(p => Path.GetFileName(p).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(p) is "mscorlib.dll" or "netstandard.dll")
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
        foreach (var asm in new[] { typeof(Gen).Assembly, typeof(AbcFile).Assembly })
        {
            string path = asm.Location;
            if (string.IsNullOrEmpty(path))
                path = (assemblyDirs ?? Array.Empty<string>()).Select(d => Path.Combine(d, asm.GetName().Name + ".dll")).FirstOrDefault(File.Exists)
                       ?? throw new FileNotFoundException("no file for " + asm.GetName().Name + " to compile the UI code against");
            refs.Add(MetadataReference.CreateFromFile(path));
        }
        return refs;
    }
}
