using System.Security.Cryptography;
using System.Text;
using Ms2.Formats.Pack;
using Ms2.Swf;
using Ms2.Swf.Abc;

static class Stats
{
    static readonly HashSet<byte> MnOps = new() { 0x5D, 0x5E, 0x5F, 0x60, 0x4A, 0x80, 0x86, 0xB2, 0x59 };

    public static int Run(PackSet packs)
    {
        var entries = packs.List("Resource/Gfx").Where(e => e.EndsWith(".gfx", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e).ToList();
        var ops = new SortedDictionary<string, long>();
        long totalInsn = 0, totalCode = 0, bodies = 0, classes = 0, interfaces = 0, movies = 0, withAbc = 0, abcBytes = 0, failures = 0, undecoded = 0;
        var defined = new Dictionary<string, (HashSet<string> Movies, HashSet<string> Hashes, string Super)>();
        var refs = new Dictionary<string, HashSet<string>>();      // external name -> movies using it
        var refCount = new Dictionary<string, long>();
        var supers = new Dictionary<string, long>();
        var perMovieClasses = new List<(string, int, int)>();
        var distinctBytes = new Dictionary<string, long>();      // name|hash -> bytecode bytes (counted once)
        var movieCount1Bytes = new Dictionary<string, long>();
        foreach (var e in entries)
        {
            SwfMovie sm;
            try { sm = SwfMovie.Parse(packs.Read("Resource/Gfx", e)); } catch (FormatException) { failures++; continue; }
            movies++;
            if (sm.AbcBlocks.Count == 0) continue;
            withAbc++;
            string mv = Path.GetFileNameWithoutExtension(e);
            foreach (var (_, raw) in sm.AbcBlocks)
            {
                abcBytes += raw.Length;
                AbcFile abc;
                try { abc = AbcFile.Parse(raw); } catch (Exception ex) { failures++; Console.Error.WriteLine($"{e}: abc parse: {ex.Message}"); continue; }
                int nc = abc.Instances.Length;
                perMovieClasses.Add((mv, nc, abc.Methods.Count(m => m.Body != null)));
                for (int ci = 0; ci < nc; ci++)
                {
                    var inst = abc.Instances[ci];
                    string cn = abc.Name(inst.Name);
                    if (inst.IsInterface) interfaces++; else classes++;
                    var h = new StringBuilder();
                    foreach (var t in inst.Traits.Concat(abc.Classes[ci].Traits))
                        if (t.Kind is TraitKind.Method or TraitKind.Getter or TraitKind.Setter && abc.Methods[t.MethodIndex].Body != null)
                            h.Append(abc.Local(t.Name)).Append(':').Append(Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(AbcHash.Normalised(abc, abc.Methods[t.MethodIndex])))));
                    h.Append("ctor:").Append(Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(AbcHash.Normalised(abc, abc.Methods[inst.Init])))));
                    var key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(h.ToString())));
                    long bytes = inst.Traits.Concat(abc.Classes[ci].Traits).Where(t => t.Kind is TraitKind.Method or TraitKind.Getter or TraitKind.Setter && abc.Methods[t.MethodIndex].Body != null).Sum(t => (long)(abc.Methods[t.MethodIndex].Body?.Code.Length ?? 0)) + (abc.Methods[inst.Init].Body?.Code.Length ?? 0);
                    distinctBytes[cn + "|" + key] = bytes;
                    if (!defined.TryGetValue(cn, out var d)) defined[cn] = d = (new HashSet<string>(), new HashSet<string>(), abc.Name(inst.Super));
                    d.Movies.Add(mv); d.Hashes.Add(key);
                    string sup = abc.Name(inst.Super); supers[sup] = supers.GetValueOrDefault(sup) + 1;
                }
                void Ref(string n) { if (!refs.TryGetValue(n, out var s)) refs[n] = s = new HashSet<string>(); s.Add(mv); refCount[n] = refCount.GetValueOrDefault(n) + 1; }
                foreach (var inst in abc.Instances)
                {
                    if (inst.Super != 0) Ref(abc.Name(inst.Super));
                    foreach (var i in inst.Interfaces) Ref(abc.Name(i));
                }
                foreach (var m in abc.Methods)
                {
                    if (m.ReturnType != 0) Ref(abc.Name(m.ReturnType));
                    foreach (var p in m.ParamTypes) if (p != 0) Ref(abc.Name(p));
                    if (m.Body == null) continue;
                    bodies++; totalCode += m.Body.Code.Length;
                    foreach (var i in Disasm.Decode(m.Body.Code))
                    {
                        totalInsn++;
                        ops[i.Name] = ops.GetValueOrDefault(i.Name) + 1;
                        if (i.Name.StartsWith("op_0x")) undecoded++;
                        if (MnOps.Contains(i.Op) && i.A > 0 && i.A < abc.Multinames.Length) Ref(abc.Name(i.A));
                    }
                }
            }
        }
        var w = Console.Out;
        w.WriteLine($"movies {movies} (unreadable {failures}), with ABC {withAbc}, ABC bytes {abcBytes}");
        w.WriteLine($"classes {classes}, interfaces {interfaces}, method bodies {bodies}, bytecode bytes {totalCode}, instructions {totalInsn}, undecoded opcodes {undecoded}");
        w.WriteLine("\n== opcode histogram (count, share)");
        foreach (var kv in ops.OrderByDescending(k => k.Value)) w.WriteLine($"{kv.Key,-18}{kv.Value,10}  {100.0 * kv.Value / totalInsn,6:F2}%");
        w.WriteLine("\n== classes defined: distinct names " + defined.Count);
        var multi = defined.Where(k => k.Value.Movies.Count > 1).ToList();
        w.WriteLine($"names defined in more than one movie: {multi.Count}; identical code everywhere: {multi.Count(k => k.Value.Hashes.Count == 1)}; variants: {multi.Count(k => k.Value.Hashes.Count > 1)}");
        long inst1 = defined.Sum(k => (long)k.Value.Movies.Count), uniq = defined.Sum(k => (long)k.Value.Hashes.Count);
        w.WriteLine($"class definitions {inst1}, distinct (name, code) pairs {uniq}: translating each distinct pair once covers {100.0 * (inst1 - uniq) / inst1:F1}% of definitions by reuse");
        w.WriteLine("most widely defined classes (movies / code variants):");
        foreach (var k in defined.OrderByDescending(k => k.Value.Movies.Count).Take(25)) w.WriteLine($"  {k.Key}  {k.Value.Movies.Count} / {k.Value.Hashes.Count}");
        {
            var byPkg = new SortedDictionary<string, (long Bytes, int Classes)>();
            var uniqueBytes = 0L; var libBytes = 0L;
            foreach (var (k, b) in distinctBytes)
            {
                var cn = k.Substring(0, k.IndexOf('|'));
                var parts = cn.Split('.');
                string pk = parts.Length <= 1 ? "(no package)" : parts.Length == 2 ? parts[0] : parts[0] + "." + parts[1] + (parts.Length > 3 && parts[1] == "ms2" ? "." + parts[2] : "");
                byPkg.TryGetValue(pk, out var cur); byPkg[pk] = (cur.Bytes + b, cur.Classes + 1);
                if (defined[cn].Movies.Count == 1) uniqueBytes += b; else libBytes += b;
            }
            w.WriteLine($"\n== distinct bytecode (each distinct class+code counted once): {distinctBytes.Values.Sum()} bytes; in classes found in one movie only {uniqueBytes}; in classes shared by several movies {libBytes}");
            foreach (var kv in byPkg.OrderByDescending(x => x.Value.Bytes).Take(30)) w.WriteLine($"  {kv.Key,-40}{kv.Value.Bytes,10} bytes {kv.Value.Classes,6} classes");
        }
        w.WriteLine("\n== super classes (top 25)");
        foreach (var k in supers.OrderByDescending(k => k.Value).Take(25)) w.WriteLine($"  {k.Key,-60}{k.Value}");
        // external references
        var groups = new SortedDictionary<string, List<string>>();
        foreach (var n in refs.Keys)
        {
            string g;
            if (defined.ContainsKey(n)) g = "defined in the movies (game/library classes)";
            else if (n.StartsWith("flash.")) g = "flash.*";
            else if (n.StartsWith("fl.")) g = "fl.*";
            else if (n.StartsWith("scaleform.")) g = "scaleform.*";
            else if (!n.Contains('.')) g = "top level (builtin or untyped)";
            else g = "other package";
            if (!groups.TryGetValue(g, out var l)) groups[g] = l = new();
            l.Add(n);
        }
        w.WriteLine("\n== referenced names by origin (distinct names, total references)");
        foreach (var (g, l) in groups) w.WriteLine($"{g}: {l.Count} names, {l.Sum(n => refCount[n])} refs");
        foreach (var g in new[] { "flash.*", "scaleform.*", "fl.*", "other package" })
            if (groups.TryGetValue(g, out var l)) { w.WriteLine($"\n-- {g} (by references, top 60)"); foreach (var n in l.OrderByDescending(n => refCount[n]).Take(60)) w.WriteLine($"  {n,-60}{refCount[n],8}  in {refs[n].Count} movies"); }
        if (groups.TryGetValue("top level (builtin or untyped)", out var tl)) { w.WriteLine("\n-- top level"); foreach (var n in tl.OrderByDescending(n => refCount[n]).Take(40)) w.WriteLine($"  {n,-30}{refCount[n],8}"); }
        w.WriteLine("\n== flash.* distinct names: " + (groups.TryGetValue("flash.*", out var fl) ? fl.Count : 0) + ", scaleform.*: " + (groups.TryGetValue("scaleform.*", out var sc) ? sc.Count : 0));
        return 0;
    }
}
