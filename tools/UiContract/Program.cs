using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ms2.Formats.Pack;
using Ms2.Swf;
using Ms2.Swf.Abc;

// UiContract: writes the C# contract of one UI movie, from its ABC code and its layout.
//   UiContract <movie> [--cpp FILE] [--out DIR] [--data DIR]
// The contract lists
//   ToHost      callbacks the movie raises:  ExternalInterface.call(id, "Name", args...)   (found in the movie's own code)
//   FromHost    methods the host calls on the movie: public methods of the movie's classes whose name appears as a string
//               literal in the C++ dialog source given with --cpp (Invoke("initData") ...)
//   Instances   named instances (path, class, nearest CLIK base)
//   Inspector   component inspector values (the __setProp_* methods the Flash IDE generates), per instance
//   Library     with --lib MOVIE:CLASS[,CLASS...]: the inspector values of library symbols the movie builds by name
//               (getDefinitionByName) from a dependency movie, per class, since their instance names repeat across symbols
// Classes that a small reference movie (uihpbar) also defines are the shared library; their callbacks are listed apart.

string data = Environment.GetEnvironmentVariable("MS2_DATA"), cpp = null, outDir = null, movie = null;
var libs = new List<(string Movie, string[] Classes)>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--data") data = args[++i];
    else if (args[i] == "--lib") { var lp = args[++i].Split(':', 2); libs.Add((lp[0], lp[1].Split(','))); }
    else if (args[i] == "--cpp") cpp = args[++i];
    else if (args[i] == "--out") outDir = args[++i];
    else movie = args[i];
}
if (movie == null || string.IsNullOrEmpty(data)) { Console.Error.WriteLine("usage: UiContract <movie> [--cpp FILE] [--out DIR] [--data DIR]"); return 2; }
var packs = new PackSet(data);
SwfMovie Load(string n) => SwfMovie.Parse(packs.Read("Resource/Gfx", n.Contains('.') ? n : n + ".gfx"));
var mv = Load(movie);
var layout = LayoutJson.Build(mv, movie);

// shared library = classes of a reference movie
var shared = new HashSet<string>();
foreach (var (_, raw) in Load("uihpbar").AbcBlocks) { var a = AbcFile.Parse(raw); foreach (var i in a.Instances) shared.Add(a.Name(i.Name)); }

var toHost = new SortedDictionary<string, (string Where, int Argc, bool Shared)>();
var methods = new SortedDictionary<string, string>();      // public method name -> class
var inspector = new SortedDictionary<string, SortedDictionary<string, string>>();
var callRx = new Regex("ExternalInterface\\.call\\(.*?, \"(\\w+)\"(.*)\\);\\s*$");
foreach (var (_, raw) in mv.AbcBlocks)
{
    var abc = AbcFile.Parse(raw);
    var d = new Decompiler(abc) { AllDynamic = true };
    for (int ci = 0; ci < abc.Instances.Length; ci++)
    {
        string cn = abc.Name(abc.Instances[ci].Name);
        bool isShared = shared.Contains(cn) || cn.StartsWith("fl.") || cn == "Prop" || cn == "XFormData";
        var text = d.Class(ci);
        string cur = null;
        foreach (var line in text.Split('\n'))
        {
            var mm = Regex.Match(line, @"^  public (?:static )?(?:new )?\S+ (\w+)\(");
            if (mm.Success) { cur = mm.Groups[1].Value; if (!isShared && !cur.StartsWith("__setProp_") && !cur.StartsWith("get_") && !cur.StartsWith("set_")) methods.TryAdd(cur, cn); continue; }
            var cm = callRx.Match(line);
            if (cm.Success)
            {
                int argc = 0; var rest = cm.Groups[2].Value;
                if (rest.Trim().Length > 0) { int depth = 0; foreach (var ch in rest.TrimEnd(')', ' ')) { if (ch == '(') depth++; else if (ch == ')') depth--; else if (ch == ',' && depth == 0) argc++; } }
                var key = cm.Groups[1].Value;
                if (!toHost.TryGetValue(key, out var ex) || (ex.Shared && !isShared)) toHost[key] = ($"{cn}.{cur}", argc, isShared);
            }
            if (cur != null && cur.StartsWith("__setProp_"))
            {
                var sm = Regex.Match(line, @"^\s*this\.(\w+)\.(\w+) = (.+);$");
                if (sm.Success && sm.Groups[2].Value != "componentInspectorSetting")
                {
                    var inst = sm.Groups[1].Value;
                    if (!inspector.TryGetValue(inst, out var props)) inspector[inst] = props = new();
                    props[sm.Groups[2].Value] = sm.Groups[3].Value;
                }
            }
        }
    }
}

// inspector values of named library classes: class -> instance -> property -> value
var libInspector = new SortedDictionary<string, SortedDictionary<string, SortedDictionary<string, string>>>(StringComparer.Ordinal);
foreach (var (lm, classes) in libs)
    foreach (var (_, raw) in Load(lm).AbcBlocks)
    {
        var abc = AbcFile.Parse(raw);
        var d = new Decompiler(abc) { AllDynamic = true };
        for (int ci = 0; ci < abc.Instances.Length; ci++)
        {
            string cn = abc.Name(abc.Instances[ci].Name);
            if (!classes.Contains(cn)) continue;
            var byInst = libInspector.TryGetValue(cn, out var bi) ? bi : libInspector[cn] = new(StringComparer.Ordinal);
            string cur = null;
            foreach (var line in d.Class(ci).Split('\n'))
            {
                var mm = Regex.Match(line, @"^  public (?:static )?(?:new )?\S+ (\w+)\(");
                if (mm.Success) { cur = mm.Groups[1].Value; continue; }
                if (cur == null || !cur.StartsWith("__setProp_")) continue;
                var sm = Regex.Match(line, @"^\s*this\.(\w+)\.(\w+) = (.+);$");
                if (!sm.Success || sm.Groups[2].Value == "componentInspectorSetting") continue;
                var inst = sm.Groups[1].Value;
                if (!byInst.TryGetValue(inst, out var props)) byInst[inst] = props = new(StringComparer.Ordinal);
                props[sm.Groups[2].Value] = sm.Groups[3].Value;
            }
        }
    }

var fromHost = new SortedDictionary<string, string>(); var cppCallbacks = new SortedSet<string>();
if (cpp != null)
{
    var src = File.ReadAllText(cpp);
    var lits = Regex.Matches(src, "\"(\\w+)\"").Select(m => m.Groups[1].Value).ToHashSet();
    foreach (var l in lits)
    {
        if (methods.TryGetValue(l, out var cls)) fromHost[l] = cls;
        if (toHost.ContainsKey(l)) cppCallbacks.Add(l);
    }
}

// names
var inst2 = new List<(string Path, string Class, string Base, string Kind)>();
foreach (var n in (JsonArray)layout["namedInstances"])
{
    var sym = ((JsonObject)layout["symbols"]).TryGetPropertyValue(((int)n["id"]).ToString(), out var sn) ? (JsonObject)sn : null;
    var bases = sym?["bases"] as JsonArray;
    string clik = bases?.Select(b => (string)b).FirstOrDefault(b => b.StartsWith("scaleform.clik.") || b.StartsWith("scaleform.ms2.")) ;
    inst2.Add(((string)n["path"], (string)n["class"], clik, (string)n["kind"]));
}

static string Ident(string s)
{
    var sb = new StringBuilder();
    bool up = true;
    foreach (var c in s) { if (char.IsLetterOrDigit(c)) { sb.Append(up ? char.ToUpperInvariant(c) : c); up = false; } else { sb.Append('_'); up = false; } }
    var r = sb.ToString(); return char.IsDigit(r[0]) ? "_" + r : r;
}
string Cs(string v) => v.StartsWith("\"") ? v : v;

var o = new StringBuilder();
string cname = Ident(mv.ExporterName ?? movie) + "Contract";
o.AppendLine("// Generated by tools/UiContract from the movie's ABC and layout. Do not edit; rerun the tool.");
if (libs.Count > 0) o.AppendLine("// Library symbols: " + string.Join(" ", libs.Select(l => $"--lib {l.Movie}:{string.Join(',', l.Classes)}")));
o.AppendLine("// Names only: identifiers and string keys of the UI, no art and no text.");
o.AppendLine("namespace Ms2Godot.Ui.Contracts;");
o.AppendLine();
o.AppendLine($"public static class {cname}");
o.AppendLine("{");
o.AppendLine($"    public const string Movie = \"{movie}\";");
o.AppendLine($"    public const int StageWidth = {(int)mv.Stage.W}, StageHeight = {(int)mv.Stage.H};");
o.AppendLine();
o.AppendLine("    /// <summary>Calls the movie makes to the host: ExternalInterface.call(movieId, name, args...).</summary>");
o.AppendLine("    public static class ToHost");
o.AppendLine("    {");
foreach (var (k, v) in toHost.Where(x => !x.Value.Shared)) o.AppendLine($"        public const string {Ident(k)} = \"{k}\";   // {v.Where}, {v.Argc} arg(s){(cppCallbacks.Contains(k) ? ", handled by the C++ dialog" : "")}");
o.AppendLine("        /// <summary>Shared library callbacks (tooltips, drag, close, load).</summary>");
o.AppendLine("        public static readonly string[] Shared = { " + string.Join(", ", toHost.Where(x => x.Value.Shared).Select(x => "\"" + x.Key + "\"")) + " };");
o.AppendLine("    }");
o.AppendLine();
o.AppendLine("    /// <summary>Methods the host calls on the movie (names that the C++ dialog source uses and the movie defines).</summary>");
o.AppendLine("    public static class FromHost");
o.AppendLine("    {");
foreach (var (k, v) in fromHost) o.AppendLine($"        public const string {Ident(k)} = \"{k}\";   // {v}");
o.AppendLine("    }");
o.AppendLine();
o.AppendLine("    /// <summary>Named instances: path from the stage, symbol class, nearest CLIK or game base class.</summary>");
o.AppendLine("    public static class Instances");
o.AppendLine("    {");
var seen = new HashSet<string>();
foreach (var (p, c, b, k) in inst2)
    if (seen.Add(p)) o.AppendLine($"        public const string {Ident(p)} = \"{p}\";   // {k}{(c != null ? ", " + c : "")}{(b != null ? " : " + b : "")}");
o.AppendLine("    }");
o.AppendLine();
o.AppendLine("    /// <summary>Component inspector values: instance name, property, AS3 constant expression.</summary>");
o.AppendLine("    public static readonly (string Instance, string Property, string Value)[] Inspector =");
o.AppendLine("    {");
foreach (var (i, props) in inspector) foreach (var (p, v) in props) o.AppendLine($"        (\"{i}\", \"{p}\", {EscapeCs(v)}),");
o.AppendLine("    };");
if (libInspector.Count > 0)
{
    o.AppendLine();
    o.AppendLine("    /// <summary>Component inspector values of library symbols the movie builds by name, per symbol class.</summary>");
    o.AppendLine("    public static class Library");
    o.AppendLine("    {");
    foreach (var (cls, byInst) in libInspector)
    {
        o.AppendLine($"        public static readonly (string Instance, string Property, string Value)[] {Ident(cls)} =");
        o.AppendLine("        {");
        foreach (var (i, props) in byInst) foreach (var (p, v) in props) o.AppendLine($"            (\"{i}\", \"{p}\", {EscapeCs(v)}),");
        o.AppendLine("        };");
    }
    o.AppendLine("    }");
}
o.AppendLine("}");
static string EscapeCs(string v) => "@\"" + v.Replace("\"", "\"\"") + "\"";

if (outDir == null) Console.Write(o); else { Directory.CreateDirectory(outDir); File.WriteAllText(Path.Combine(outDir, cname + ".g.cs"), o.ToString()); }
Console.Error.WriteLine($"{movie}: {toHost.Count(x => !x.Value.Shared)} callbacks to host ({toHost.Count(x => x.Value.Shared)} shared), {fromHost.Count} host-called methods, {seen.Count} named instances, {inspector.Count} inspected instances");
return 0;
