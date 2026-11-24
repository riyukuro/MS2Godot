using System.Text;
using Ms2.Formats.Pack;
using Ms2.Swf;
using Ms2.Swf.Abc;

// AbcProbe: what is in the ActionScript 3 code of the UI movies.
//   AbcProbe stats [--data DIR]                 opcode histogram, classes, external references, shared code over every movie
//   AbcProbe dump <movie> [--class SUBSTR] [--out FILE]   pseudo C# for the classes of one movie
//   AbcProbe classes <movie>                    class list with super classes
//   AbcProbe members [--out FILE]              member use on TextField / TextArea receivers over every movie (TSV: member, uses, movies)
//   AbcProbe contract [--out FILE]              TSV over every movie: classes, method signatures, ExternalInterface.call and getUserData sites
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
    case "members":
    {
        // Which members the movies use on a TextField / TextArea. The decompiler types fields and parameters but not locals, so a
        // receiver counts when it is a field or parameter declared TextField / TextArea (or the TextFieldEx static helpers);
        // a call is a row of its own by argument count.
        var rxField = new System.Text.RegularExpressions.Regex(@"^\s+(?:public|private|protected|internal)\s+(?:static\s+)?(?:TextField|TextArea)\s+(\w+)\s*[;=]");
        var rxHead = new System.Text.RegularExpressions.Regex(@"^  [^/\s].*\((.*)\)\s*\{\s*$");
        var rxParam = new System.Text.RegularExpressions.Regex(@"(?:TextField|TextArea)\s+(a\d+)");
        var rxEx = new System.Text.RegularExpressions.Regex(@"TextFieldEx\.(\w+)");
        var uses = new SortedDictionary<string, (int N, HashSet<string> Movies)>();
        // a call is a row of its own by argument count: getTextFormat() and getTextFormat(b, e) differ
        static string Call(string open, string l, int end)
        {
            if (open != "(" && open != "()") return "";
            if (open == "()") return "(0)";
            int depth = 1, n = 1;
            for (int i = end; i < l.Length && depth > 0; i++) { char ch = l[i]; if (ch is '(' or '[' or '{') depth++; else if (ch is ')' or ']' or '}') depth--; else if (ch == ',' && depth == 1) n++; }
            return "(" + n + ")";
        }
        void Hit(string m, string movie) { uses.TryGetValue(m, out var c); (c.Movies ??= new()).Add(movie); uses[m] = (c.N + 1, c.Movies); }
        foreach (var e in packs.List("Resource/Gfx").Where(e => e.EndsWith(".gfx", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e))
        {
            SwfMovie sm;
            try { sm = SwfMovie.Parse(packs.Read("Resource/Gfx", e)); } catch (FormatException) { continue; }
            var movie = Path.GetFileNameWithoutExtension(e).ToLowerInvariant();
            foreach (var (_, raw) in sm.AbcBlocks)
            {
                var abc = AbcFile.Parse(raw);
                var d = new Decompiler(abc);
                var text = new List<string>();
                for (int ci = 0; ci < abc.Instances.Length; ci++) text.AddRange(d.Class(ci).Split('\n'));
                var names = new HashSet<string>();
                foreach (var l in text) { var m = rxField.Match(l); if (m.Success) names.Add(m.Groups[1].Value); }
                var paramNames = new HashSet<string>();
                foreach (var l in text)
                {
                    var h = rxHead.Match(l);
                    if (h.Success) { paramNames.Clear(); foreach (System.Text.RegularExpressions.Match pm in rxParam.Matches(h.Groups[1].Value)) paramNames.Add(pm.Groups[1].Value); }
                    foreach (System.Text.RegularExpressions.Match em in rxEx.Matches(l)) Hit("TextFieldEx." + em.Groups[1].Value, movie);
                    foreach (var n in names)
                        foreach (System.Text.RegularExpressions.Match mm in System.Text.RegularExpressions.Regex.Matches(l, @"(?<![\w])(?:\w+\.)*" + System.Text.RegularExpressions.Regex.Escape(n) + @"\.(\w+)(\(\)|\()?"))
                            Hit(mm.Groups[1].Value + Call(mm.Groups[2].Value, l, mm.Index + mm.Length), movie);
                    foreach (var n in paramNames)
                        foreach (System.Text.RegularExpressions.Match mm in System.Text.RegularExpressions.Regex.Matches(l, @"(?<![\w.])" + n + @"\.(\w+)(\(\)|\()?"))
                            Hit(mm.Groups[1].Value + Call(mm.Groups[2].Value, l, mm.Index + mm.Length), movie);
                }
            }
        }
        var sbm = new StringBuilder();
        foreach (var (m, c) in uses) sbm.Append(m).Append('\t').Append(c.N).Append('\t').Append(c.Movies.Count).Append('\t').AppendLine(string.Join(",", c.Movies.Take(6)));
        File.WriteAllText(outFile ?? "members.tsv", sbm.ToString());
        return 0;
    }
    case "contract":
    {
        // TSV over every movie: class rows, instance method signatures, ExternalInterface.call sites (one line of
        // decompiled code each). The input of the C++ <-> AS data contract; shared library classes show up in many movies.
        var w = new StringBuilder();
        var rx = new System.Text.RegularExpressions.Regex("ExternalInterface\\.call\\(.*?, \\\"(\\w+)\\\"");
        var ud = new System.Text.RegularExpressions.Regex("getUserData\\([^,]+, \\\"(\\w+)\\\"");
        foreach (var e in packs.List("Resource/Gfx").Where(e => e.EndsWith(".gfx", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e))
        {
            SwfMovie sm;
            try { sm = SwfMovie.Parse(packs.Read("Resource/Gfx", e)); } catch (FormatException) { continue; }
            var movie = Path.GetFileNameWithoutExtension(e).ToLowerInvariant();
            foreach (var (_, raw) in sm.AbcBlocks)
            {
                var abc = AbcFile.Parse(raw);
                var d = new Decompiler(abc) { AllDynamic = true };
                for (int ci = 0; ci < abc.Instances.Length; ci++)
                {
                    var cn = abc.ClassName(ci);
                    w.Append("class\t").Append(movie).Append('\t').Append(cn).Append('\t').AppendLine(abc.Name(abc.Instances[ci].Super));
                    foreach (var t in abc.Instances[ci].Traits)
                    {
                        if (t.Kind is not (TraitKind.Method or TraitKind.Setter)) continue;
                        var mi = abc.Methods[t.MethodIndex];
                        var ps = Enumerable.Range(0, mi.ParamCount).Select(i => (mi.ParamNames != null && i < mi.ParamNames.Length ? mi.ParamNames[i] : "p" + i) + ":" + abc.Local(mi.ParamTypes[i]));
                        if (mi.NeedRest) ps = ps.Append("...rest");
                        w.Append(t.Kind == TraitKind.Setter ? "set\t" : "fn\t").Append(movie).Append('\t').Append(cn).Append('\t').Append(abc.Local(t.Name)).Append('\t')
                         .Append(string.Join(", ", ps)).Append('\t').AppendLine(abc.Local(mi.ReturnType));
                    }
                    foreach (var line in d.Class(ci).Split('\n'))
                    {
                        foreach (System.Text.RegularExpressions.Match mt in ud.Matches(line))
                            w.Append("ud\t").Append(movie).Append('\t').Append(cn).Append('\t').Append(mt.Groups[1].Value).Append('\t').AppendLine(line.Trim().Replace('\t', ' '));
                        foreach (System.Text.RegularExpressions.Match mt in rx.Matches(line))
                            w.Append("cb\t").Append(movie).Append('\t').Append(cn).Append('\t').Append(mt.Groups[1].Value).Append('\t').AppendLine(line.Trim().Replace('\t', ' '));
                    }
                }
            }
        }
        File.WriteAllText(outFile ?? "contract.tsv", w.ToString());
        return 0;
    }
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
