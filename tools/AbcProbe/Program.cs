using System.Text;
using Ms2.Formats.Pack;
using Ms2.Swf;
using Ms2.Swf.Abc;

// AbcProbe: what is in the ActionScript 3 code of the UI movies.
//   AbcProbe stats [--data DIR]                 opcode histogram, classes, external references, shared code over every movie
//   AbcProbe dump <movie> [--class SUBSTR] [--out FILE]   pseudo C# for the classes of one movie
//   AbcProbe classes <movie>                    class list with super classes
string data = Environment.GetEnvironmentVariable("MS2_DATA");
bool dyn = false; var pos = new List<string>(); string classFilter = null, outFile = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--data") data = args[++i];
    else if (args[i] == "--class") classFilter = args[++i];
    else if (args[i] == "--out") outFile = args[++i];
    else if (args[i] == "--dynamic") dyn = true;
    else pos.Add(args[i]);
}
if (pos.Count == 0 || string.IsNullOrEmpty(data)) { Console.Error.WriteLine("usage: AbcProbe stats|dump|classes [movie] [--data DIR] (MS2_DATA)"); return 2; }
var packs = new PackSet(data);

List<(string Name, AbcFile Abc)> LoadAbc(string entry)
{
    var m = SwfMovie.Parse(packs.Read("Resource/Gfx", entry));
    return m.AbcBlocks.Select(b => (b.Name, AbcFile.Parse(b.Data))).ToList();
}

switch (pos[0])
{
    case "dump":
    {
        var entry = pos[1].Contains('.') ? pos[1] : pos[1] + ".gfx";
        var sb = new StringBuilder();
        int classes = 0; int methods = 0, unstructured = 0, unknown = 0, merges = 0, tries = 0, switches = 0;
        foreach (var (_, abc) in LoadAbc(entry))
        {
            var d = new Decompiler(abc) { AllDynamic = dyn };
            for (int ci = 0; ci < abc.Instances.Length; ci++)
            {
                var n = abc.ClassName(ci);
                if (classFilter != null && !n.Contains(classFilter, StringComparison.OrdinalIgnoreCase)) continue;
                sb.AppendLine(d.Class(ci)); classes++;
            }
            methods += d.Methods; unstructured += d.Unstructured; unknown += d.Unknown; merges += d.StackMerges; tries += d.TryBlocks; switches += d.Switches;
        }
        Console.Error.WriteLine($"{classes} classes, {methods} method bodies, {unstructured} gotos, {switches} switches, {merges} stack merges, {tries} methods with try/catch, {unknown} unsupported");
        if (outFile != null) File.WriteAllText(outFile, sb.ToString()); else Console.Write(sb);
        return 0;
    }
    case "classes":
    {
        foreach (var (_, abc) in LoadAbc(pos[1].Contains('.') ? pos[1] : pos[1] + ".gfx"))
            for (int ci = 0; ci < abc.Instances.Length; ci++)
                Console.WriteLine($"{abc.ClassName(ci)} : {abc.Name(abc.Instances[ci].Super)}");
        return 0;
    }
    case "stats": return Stats.Run(packs);
    case "decompile-all":
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int movies = 0, classes = 0, methods = 0, gotos = 0, switches = 0, merges = 0, tries = 0, unknown = 0, lines = 0;
        var callbacks = new Dictionary<string, HashSet<string>>(); var rx = new System.Text.RegularExpressions.Regex("ExternalInterface\\.call\\(.*?, \\\"(\\w+)\\\"");
        foreach (var e in packs.List("Resource/Gfx").Where(e => e.EndsWith(".gfx", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e))
        {
            SwfMovie sm;
            try { sm = SwfMovie.Parse(packs.Read("Resource/Gfx", e)); } catch (FormatException) { continue; }
            if (sm.AbcBlocks.Count == 0) continue;
            movies++;
            foreach (var (_, raw) in sm.AbcBlocks)
            {
                var abc = AbcFile.Parse(raw);
                var d = new Decompiler(abc) { AllDynamic = true };
                for (int ci = 0; ci < abc.Instances.Length; ci++)
                {
                    var txt = d.Class(ci); lines += txt.Count(c => c == '\n'); classes++;
                    foreach (System.Text.RegularExpressions.Match mt in rx.Matches(txt)) { if (!callbacks.TryGetValue(mt.Groups[1].Value, out var hs)) callbacks[mt.Groups[1].Value] = hs = new(); hs.Add(e); }
                }
                methods += d.Methods; gotos += d.Unstructured; switches += d.Switches; merges += d.StackMerges; tries += d.TryBlocks; unknown += d.Unknown;
            }
        }
        Console.WriteLine($"decompiled {movies} movies, {classes} classes, {methods} method bodies, {lines} lines in {sw.Elapsed.TotalSeconds:F0}s; gotos {gotos}, switches {switches}, stack merges {merges}, methods with try/catch {tries}, decompile failures/unsupported {unknown}");
        Console.WriteLine($"ExternalInterface.call callback names found in the code: {callbacks.Count} distinct, {callbacks.Sum(k => k.Value.Count)} (name, movie) pairs");
        foreach (var k in callbacks.OrderByDescending(k => k.Value.Count).Take(15)) Console.WriteLine($"  {k.Key} in {k.Value.Count} movies");
        return 0;
    }
}
return 2;
