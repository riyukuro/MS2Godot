using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;

namespace Ms2.Formats.World;

/// <summary>
/// One effect dummy (CEffectDummy): a named node the client attaches to an actor at spawn, at a translate / rotation /
/// scale under its parent node (a model node name or another dummy, e.g. Eff_Damage under Eff_UI). The defaults are the
/// client's constructor: no parent, applyWorldTransform false, applyLocalScale true, no translate or rotation, scale 1.
/// Rows taken from a shared table (the default rows an npc does not override) are shared objects: treat them as read-only.
/// </summary>
public sealed class EffectDummy
{
    public string Name = "";
    public string ParentNode = "";
    public bool ApplyWorldTransform;
    public bool ApplyLocalScale = true;
    public Vector3 Translate;
    public Vector3 Rotation;                 // Euler degrees, as written
    public float Scale = 1f;

    public EffectDummy Clone() => (EffectDummy)MemberwiseClone();

    /// <summary>
    /// CEffectDummy::Parse: reads &lt;dummy name&gt;, &lt;attachInfo parentNode applyWorldTransform applyLocalScale&gt; and
    /// &lt;transform translate rotation scale&gt; over the current values; an attribute that is absent or does not parse
    /// leaves its value as it was.
    /// </summary>
    public void Parse(XElement dummy)
    {
        if (dummy == null) return;
        Name = (string)dummy.Attribute("name") ?? Name;
        if (dummy.Element("attachInfo") is { } attach)
        {
            ParentNode = (string)attach.Attribute("parentNode") ?? ParentNode;
            ApplyWorldTransform = Bool(attach, "applyWorldTransform", ApplyWorldTransform);
            ApplyLocalScale = Bool(attach, "applyLocalScale", ApplyLocalScale);
        }
        if (dummy.Element("transform") is { } xf)
        {
            Translate = Point(xf, "translate", Translate);
            Rotation = Point(xf, "rotation", Rotation);
            if ((string)xf.Attribute("scale") is { } s && TryAtof(s, out double v)) Scale = (float)v;   // QueryDoubleAttribute: sscanf %lf, kept when nothing reads
        }
    }

    /// <summary>TiXmlElement::QueryBoolAttribute: true / yes / 1 and false / no / 0, any case; anything else keeps the value.</summary>
    internal static bool Bool(XElement e, string name, bool keep)
    {
        var s = (string)e.Attribute(name);
        if (s == null) return keep;
        if (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Equals("yes", StringComparison.OrdinalIgnoreCase) || s == "1") return true;
        if (s.Equals("false", StringComparison.OrdinalIgnoreCase) || s.Equals("no", StringComparison.OrdinalIgnoreCase) || s == "0") return false;
        return keep;
    }

    /// <summary>XmlParsePoint: the comma-separated tokens (empty ones skipped); with fewer than three the value stays, else atof of the first three.</summary>
    static Vector3 Point(XElement e, string name, Vector3 keep)
    {
        var s = (string)e.Attribute(name);
        if (s == null) return keep;
        var t = s.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (t.Length < 3) return keep;
        return new Vector3((float)Atof(t[0]), (float)Atof(t[1]), (float)Atof(t[2]));
    }

    /// <summary>C atof: leading white space, then the longest number prefix; 0 when there is none.</summary>
    internal static double Atof(string s) => TryAtof(s, out double v) ? v : 0;

    /// <summary>The number prefix atof and sscanf %lf read; false when there is none.</summary>
    internal static bool TryAtof(string s, out double value)
    {
        value = 0;
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        int start = i;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
        int digits = 0;
        while (i < s.Length && char.IsAsciiDigit(s[i])) { i++; digits++; }
        if (i < s.Length && s[i] == '.') { i++; while (i < s.Length && char.IsAsciiDigit(s[i])) { i++; digits++; } }
        if (digits == 0) return false;
        int end = i;
        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            int j = i + 1;
            if (j < s.Length && (s[j] == '+' || s[j] == '-')) j++;
            if (j < s.Length && char.IsAsciiDigit(s[j])) { while (j < s.Length && char.IsAsciiDigit(s[j])) j++; end = j; }
        }
        value = double.Parse(s.AsSpan(start, end - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        return true;
    }
}

/// <summary>
/// The client's effect dummy tables: Table/defaultEffectDummy.xml (CommonEffectDummy, every npc's base rows) and
/// Table/pcEffectDummy.xml (CPCEffectDummy, the player character's rows per gender).
/// </summary>
public sealed class EffectDummyTable
{
    static readonly IReadOnlyList<EffectDummy> None = Array.Empty<EffectDummy>();
    readonly Dictionary<int, List<EffectDummy>> pc;

    /// <summary>The default rows in file order.</summary>
    public IReadOnlyList<EffectDummy> Default { get; }

    public EffectDummyTable(IReadOnlyList<EffectDummy> defaults, Dictionary<int, List<EffectDummy>> pcRows)
    {
        Default = defaults ?? None;
        pc = pcRows ?? new();
    }

    /// <summary>Reads both tables from the Xml pack; a table that is missing gives no rows.</summary>
    public static EffectDummyTable Load(PackSet packs)
    {
        XElement Root(string entry) =>
            packs.Exists("Xml", entry) ? PackXml.Parse(packs.Read("Xml", entry)).Root : null;
        return new EffectDummyTable(ParseDefault(Root("table/defaulteffectdummy.xml")), ParsePc(Root("table/pceffectdummy.xml")));
    }

    /// <summary>The player character's rows for a gender (CPCEffectDummy::Get), in file order; none for an unknown gender.</summary>
    public IReadOnlyList<EffectDummy> Pc(int gender) => pc.TryGetValue(gender, out var l) ? l : None;

    /// <summary>
    /// EffectDummyGroup::ParseDummies: every &lt;dummy&gt; under &lt;ms2&gt;, each parsed into a fresh row.
    /// </summary>
    public static List<EffectDummy> ParseDefault(XElement ms2)
    {
        var list = new List<EffectDummy>();
        if (ms2 == null) return list;
        foreach (var d in ms2.Elements("dummy"))
        {
            var row = new EffectDummy();
            row.Parse(d);
            list.Add(row);
        }
        return list;
    }

    /// <summary>
    /// CPCEffectDummy::LoadFile: the &lt;dummy gender&gt; rows under &lt;ms2&gt;&lt;effectdummy&gt;, grouped by gender (absent = 0).
    /// The client parses every row into the same CEffectDummy, so an attribute a row leaves out keeps the previous row's value.
    /// </summary>
    public static Dictionary<int, List<EffectDummy>> ParsePc(XElement ms2)
    {
        var rows = new Dictionary<int, List<EffectDummy>>();
        var group = ms2?.Element("effectdummy");
        if (group == null) return rows;
        var cur = new EffectDummy();
        foreach (var d in group.Elements("dummy"))
        {
            int gender = int.TryParse((string)d.Attribute("gender"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int g) ? g : 0;
            cur.Parse(d);
            if (!rows.TryGetValue(gender, out var l)) rows[gender] = l = new List<EffectDummy>();
            l.Add(cur.Clone());
        }
        return rows;
    }

    /// <summary>
    /// NpcDataCli::LoadXml (effect dummies): the default rows, then each &lt;effectdummy&gt;&lt;dummy&gt; of the npc's
    /// &lt;environment&gt;: one whose name matches a row (the first, exact case) is parsed over a copy of that row, any
    /// other is parsed into a fresh row added at the end. Returns the defaults themselves when the npc has no dummies.
    /// </summary>
    public static IReadOnlyList<EffectDummy> MergeNpc(IReadOnlyList<EffectDummy> defaults, XElement environment)
    {
        defaults ??= None;
        var group = environment?.Element("effectdummy");
        if (group == null || !group.Elements("dummy").Any()) return defaults;
        var list = new List<EffectDummy>(defaults);
        foreach (var d in group.Elements("dummy"))
        {
            string name = (string)d.Attribute("name") ?? "";
            int i = list.FindIndex(r => r.Name == name);
            if (i >= 0)
            {
                var row = list[i].Clone();
                row.Parse(d);
                list[i] = row;
            }
            else
            {
                var row = new EffectDummy();
                row.Parse(d);
                list.Add(row);
            }
        }
        return list;
    }

    /// <summary>The first row with this name (exact case), or null.</summary>
    public static EffectDummy Find(IReadOnlyList<EffectDummy> rows, string name)
    {
        if (rows == null) return null;
        for (int i = 0; i < rows.Count; i++) if (rows[i].Name == name) return rows[i];   // by index: an interface foreach boxes the enumerator
        return null;
    }
}
