using System.Xml.Linq;

namespace Ms2.Formats.Nif;

/// <summary>One field of a struct or niobject, as listed in nif.xml (version filters already applied).</summary>
public sealed class NifField
{
    public string Name, Type, Template, OnlyT, ExcludeT;
    public NifExpr Cond, Length, Width;
    public string WidthName;                     // raw width text: may name an array field (per-row widths)
    public bool Binary;
    public (string key, NifExpr expr)[] Args = Array.Empty<(string, NifExpr)>();

    // resolved once when the field list is built, for a type that is not the template (#T#): its storage type, its basic
    // read (Fmt 0 when it is a struct), and that struct's own fields on first read
    internal string Store;
    internal char Fmt;
    internal int Size;
    internal List<NifField> Sub;
}

/// <summary>
/// The nif.xml format description (niftools nifxml, BSD), interpreted at run time for one file version.
/// The file is not shipped with this project; the caller passes its path.
/// </summary>
public sealed class NifSpec
{
    public const uint V30_2_0_3 = 0x1E020003;

    public uint Version { get; }
    public uint UserVersion { get; }
    public uint BsVersion { get; }

    readonly List<(string token, string str)> tokens = new();
    readonly Dictionary<string, string> storage = new();
    readonly Dictionary<string, XElement> structs = new();
    readonly Dictionary<string, XElement> objs = new();
    // filled on first use; concurrent (a field's navmesh loads on a worker with the spec the main thread builds models with,
    // and one spec serves the whole process): a racing fill computes the same value twice
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, NifExpr> exprCache = new();
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<NifField>> fieldCache = new();

    public NifSpec(string path, uint version = V30_2_0_3, uint userVersion = 0, uint bsVersion = 0)
    {
        Version = version; UserVersion = userVersion; BsVersion = bsVersion;
        var root = XDocument.Load(path).Root!;
        foreach (var tok in root.Elements("token"))
            foreach (var e in tok.Elements())
                tokens.Add(((string)e.Attribute("token"), (string)e.Attribute("string")));
        foreach (var e in root.Elements())
        {
            var name = (string)e.Attribute("name");
            switch (e.Name.LocalName)
            {
                case "enum": case "bitfield": case "bitflags": storage[name] = (string)e.Attribute("storage"); break;
                case "struct": structs[name] = e; break;
                case "niobject": objs[name] = e; break;
            }
        }
    }

    public bool HasObject(string name) => objs.ContainsKey(name);
    public bool IsStruct(string name) => structs.ContainsKey(name);

    /// <summary>Underlying storage type of an enum/bitfield, or the name itself.</summary>
    public string Storage(string t) => storage.TryGetValue(t, out var s) ? s : t;

    public string Expand(string s)
    {
        for (int i = 0; i < 4; i++)
            foreach (var (t, r) in tokens)
                if (s.Contains(t)) s = s.Replace(t, r);
        return s;
    }

    public NifExpr Expr(string s)
    {
        if (!exprCache.TryGetValue(s, out var e))
            exprCache[s] = e = NifExpr.Compile(Expand(s));
        return e;
    }

    /// <summary>Evaluates a version-only expression (vercond).</summary>
    double Global(string name) => name switch
    {
        "Version" => Version,
        "User Version" => UserVersion,
        _ when name.StartsWith("BS Header") => BsVersion,
        _ => 0,
    };

    public double EvalGlobal(NifExpr e) => e.Eval(Global);

    public bool IsA(string t, string baseName)
    {
        while (t != null)
        {
            if (t == baseName) return true;
            t = objs.TryGetValue(t, out var e) ? (string)e.Attribute("inherit") : null;
        }
        return false;
    }

    /// <summary>Field list of a struct or niobject (inherited fields first), filtered for this version.</summary>
    public List<NifField> Fields(string name)
    {
        if (fieldCache.TryGetValue(name, out var f)) return f;
        XElement e;
        if (objs.TryGetValue(name, out e))
        {
            var inh = (string)e.Attribute("inherit");
            f = inh != null ? new List<NifField>(Fields(inh)) : new List<NifField>();
        }
        else if (structs.TryGetValue(name, out e)) f = new List<NifField>();
        else throw new KeyNotFoundException("nif.xml has no struct or niobject " + name);

        foreach (var fe in e.Elements("field"))
        {
            string A(string k) => (string)fe.Attribute(k);
            if (A("abstract") == "true") continue;
            if (A("since") != null && Version < NifExpr.ParseVersion(A("since"))) continue;
            if (A("until") != null && Version > NifExpr.ParseVersion(A("until"))) continue;
            if (A("vercond") != null && EvalGlobal(Expr(A("vercond"))) == 0) continue;
            var nf = new NifField
            {
                Name = A("name"), Type = A("type"), Template = A("template"),
                OnlyT = A("onlyT"), ExcludeT = A("excludeT"), Binary = A("binary") == "true",
                Cond = A("cond") != null ? Expr(A("cond")) : null,
                Length = A("length") != null ? Expr(A("length")) : null,
                Width = A("width") != null ? Expr(A("width")) : null,
                WidthName = A("width"),
            };
            var args = new List<(string, NifExpr)>();
            foreach (var at in fe.Attributes())
            {
                var k = at.Name.LocalName;
                if (!k.StartsWith("arg")) continue;
                args.Add((k == "arg" ? "ARG" : "ARG" + k[3..], Expr(at.Value)));
            }
            nf.Args = args.ToArray();
            if (nf.Type != "#T#")
            {
                nf.Store = Storage(nf.Type);
                NifReader.BasicOf(nf.Store, out nf.Fmt, out nf.Size);
            }
            f.Add(nf);
        }
        fieldCache[name] = f;
        return f;
    }
}
