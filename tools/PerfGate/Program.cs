using System.Text.Json;
using System.Text.Json.Nodes;

// PerfGate: the perf regression gate's summary, baseline and compare (tools/perfgate/run.sh makes the session files).
//   PerfGate summarize OUT.json SESSION.json...   one run's metrics: every value per metric, over the sessions
//   PerfGate baseline OUT.json RUN.json...        a baseline with limits from one or more summarized runs
//   PerfGate compare BASELINE.json RUN.json       exit 1 when any count is past its limit (or missing), else 0
// A metric whose name ends in "ms" is a time: recorded and printed (median and worst against the baseline's), never a
// failure, as machine load moves it several times over (IsTime lists the other recorded-only metrics). Every other
// metric is a count (bytes, objects, nodes, collections, main-thread JIT-compiled methods): its worst value is held
// to a limit.
var opt = new JsonSerializerOptions { WriteIndented = true };
if (args.Length < 3) return Usage();
switch (args[0])
{
    case "summarize": Write(args[1], Summarize(args.Skip(2).Select(Load))); return 0;
    case "baseline": Write(args[1], Baseline(args.Skip(2).Select(Metrics).ToList())); return 0;
    case "compare": return Compare(Load(args[1]), Metrics(args[2]));
    default: return Usage();
}

static int Usage()
{
    Console.Error.WriteLine("PerfGate summarize OUT SESSION... | baseline OUT RUN... | compare BASELINE RUN");
    return 2;
}

JsonObject Load(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
void Write(string path, JsonNode n) => File.WriteAllText(path, n.ToJsonString(opt) + "\n");

// recorded only, never checked:
// - times;
// - the JIT methods of every thread (the tiering thread's recompiles land in a window or not);
// - an open's or idle window's bytes on every thread: the install session's background cache build lands in whichever
//   window it overlaps (inventory's first open saw 36 to 371 MB);
// - for the same reason, the collections (process-wide) of the install session's windows.
// The field load keeps its all-thread counts checked: they are the only ones it has, and their spread is in the baseline.
static bool IsTime(string name) => name.EndsWith("ms") || name.EndsWith("jit_all_methods")
    || !name.StartsWith("field.") && (name.EndsWith(".total_bytes") || name.EndsWith(".total_bytes_per_frame")
        || name.Contains(".install.") && System.Text.RegularExpressions.Regex.IsMatch(name, @"\.gc\d$"));

// one run's metrics: name -> every value (one per session, or per open)
static JsonObject Summarize(IEnumerable<JsonObject> sessions)
{
    var m = new SortedDictionary<string, List<double>>(StringComparer.Ordinal);
    void Add(string name, JsonNode v) { if (!m.TryGetValue(name, out var l)) m[name] = l = new(); l.Add(v.GetValue<double>()); }
    void AddAll(string prefix, JsonObject o, params string[] skip)
    {
        foreach (var (k, v) in o)
            if (v is JsonValue jv && jv.TryGetValue<double>(out _) && !skip.Contains(k)) Add(prefix + "." + k, v);
    }
    int n = 0;
    foreach (var s in sessions)
    {
        n++;
        string c = (string)s["case"];
        AddAll("field." + c, s["field"].AsObject());
        foreach (var idleKey in new[] { "idle", "idle_3_open" })
        {
            // per case: the install session's background work (the per-install cache) still runs in its idle
            var o = s[idleKey].AsObject();
            string idle = idleKey + "." + c;
            double frames = o["frames"].GetValue<double>(), fx = o["fx_effect_frames"].GetValue<double>();
            Add(idle + ".main_bytes_per_frame", Math.Round(o["main_bytes"].GetValue<double>() / frames, 1));
            Add(idle + ".total_bytes_per_frame", Math.Round(o["total_bytes"].GetValue<double>() / frames, 1));
            Add(idle + ".fx_bytes_per_effect_frame", fx == 0 ? 0 : Math.Round(o["fx_bytes"].GetValue<double>() / fx, 2));
            foreach (var k in new[] { "gc0", "gc1", "gc2", "objects", "nodes", "jit_methods", "jit_all_methods", "jit_ms" }) Add(idle + "." + k, o[k]);
            Add(idle + ".frame_median_ms", o["frame_ms_median"]);   // times: their names end in ms
            Add(idle + ".frame_worst_ms", o["frame_ms_worst"]);
            Add(idle + ".fx_us_per_effect_ms", o["fx_us_per_effect"]);   // a time (its name ends in ms for the rule above)
        }
        double firstMs = 0, repeatMs = 0;
        foreach (var e in s["opens"].AsArray())
        {
            var o = e.AsObject();
            bool first = (string)o["kind"] == "first";
            AddAll($"open.{(string)o["dialog"]}.{(first ? c : "repeat")}", o);
            if (first) firstMs += o["ms"].GetValue<double>(); else repeatMs += o["ms"].GetValue<double>();
        }
        // every dialog's open time summed, per session: one first open each is a single, noisy sample; the sum is not
        Add($"opens.{c}.sum_ms", Math.Round(firstMs, 3));
        Add("opens.repeat.sum_ms", Math.Round(repeatMs, 3));
    }
    var metrics = new JsonObject();
    foreach (var (k, l) in m) metrics[k] = new JsonArray(l.Select(x => (JsonNode)x).ToArray());
    return new JsonObject { ["sessions"] = n, ["metrics"] = metrics };
}

static Dictionary<string, double[]> Metrics(string path)
{
    var o = JsonNode.Parse(File.ReadAllText(path))!["metrics"].AsObject();
    return o.ToDictionary(kv => kv.Key, kv => kv.Value.AsArray().Select(x => x.GetValue<double>()).ToArray());
}

static double Median(IEnumerable<double> v) { var a = v.OrderBy(x => x).ToArray(); return a.Length == 0 ? 0 : a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2; }

// The slack a count gets over the worst value the baseline runs saw: the spread they showed, at least a floor per
// kind. tools/perfgate/README.md has the run-to-run noise the floors come from.
static (double Rel, double Abs) CountTolerance(string name) =>
    name.EndsWith("bytes") || name.EndsWith("bytes_per_frame") ? (0.02, 16384)
    : name.EndsWith("bytes_per_effect_frame") ? (0.05, 8)
    : name.EndsWith("kept_objects") ? (0.03, 64)   // options, shop and skill repeats move by up to ~50 between runs
    : name.EndsWith(".objects") || name.EndsWith(".nodes") ? (0.03, 16)
    : name.EndsWith("jit_methods") ? (0.02, 8)
    : (0, 1);   // collections

static JsonObject Baseline(List<Dictionary<string, double[]>> runs)
{
    var outM = new JsonObject();
    foreach (var name in runs.SelectMany(r => r.Keys).Distinct().OrderBy(x => x, StringComparer.Ordinal))
    {
        var per = runs.Where(r => r.ContainsKey(name)).Select(r => r[name]).ToList();
        var all = per.SelectMany(x => x).ToArray();
        if (IsTime(name))
        {
            double med = Median(per.Select(Median)), medHi = per.Max(Median), worst = per.Max(x => x.Max());
            outM[name] = new JsonObject
            {
                ["median"] = Math.Round(med, 3), ["median_max"] = Math.Round(medHi, 3), ["worst"] = Math.Round(worst, 3),
            };
        }
        else
        {
            double lo = all.Min(), hi = all.Max();
            var (rel, abs) = CountTolerance(name);
            double slack = Math.Max(hi - lo, Math.Max(Math.Abs(hi) * rel, abs));
            outM[name] = new JsonObject { ["min"] = lo, ["max"] = hi, ["limit"] = Math.Ceiling(hi + slack) };
        }
    }
    return new JsonObject
    {
        ["note"] = "tools/perfgate: limits from PerfGate baseline over the runs below; change only with a Perf-baseline: trailer",
        ["runs"] = runs.Count,
        ["metrics"] = outM,
    };
}

static int Compare(JsonObject baseline, Dictionary<string, double[]> run)
{
    int fails = 0, checkedN = 0;
    var notes = new List<string>();
    var times = new List<string>();
    foreach (var (name, b) in baseline["metrics"].AsObject())
    {
        bool time = IsTime(name);
        if (!run.TryGetValue(name, out var v) || v.Length == 0)
        {
            if (time) notes.Add($"note {name}: missing from the run");
            else { Console.WriteLine($"FAIL {name}: missing from the run"); fails++; checkedN++; }
            continue;
        }
        if (time)
        {
            double med = Median(v), worst = v.Max(), bm = b["median"].GetValue<double>();
            times.Add($"time {name}: median {med:0.###} (baseline {bm:0.###}, x{(bm > 0 ? med / bm : 0):0.00}), worst {worst:0.###} (baseline {b["worst"]})");
        }
        else
        {
            checkedN++;
            double worst = v.Max(), lim = b["limit"].GetValue<double>(), bmax = b["max"].GetValue<double>();
            if (worst > lim) { Console.WriteLine($"FAIL {name}: {worst:0.##} > limit {lim:0.##} (baseline max {bmax:0.##})"); fails++; }
            else if (bmax > 0 && worst < b["min"].GetValue<double>() * 0.9 - CountTolerance(name).Abs)
                notes.Add($"note {name}: {worst:0.##}, baseline {b["min"]}..{bmax:0.##} (lower: a baseline update may be due)");
        }
    }
    foreach (var name in run.Keys.Where(k => !baseline["metrics"].AsObject().ContainsKey(k)).OrderBy(x => x, StringComparer.Ordinal))
        notes.Add($"note {name}: not in the baseline");
    foreach (var n in notes) Console.WriteLine(n);
    if (Environment.GetEnvironmentVariable("PERFGATE_TIMES") == "1") foreach (var t in times) Console.WriteLine(t);
    else Console.WriteLine($"perfgate: {times.Count} metrics recorded only, not checked (times and the rest IsTime lists; PERFGATE_TIMES=1 prints them)");
    Console.WriteLine(fails == 0 ? $"perfgate: OK, {checkedN} count metrics within their limits" : $"perfgate: FAIL, {fails} of {checkedN} count metrics past their limits");
    return fails == 0 ? 0 : 1;
}
