using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Ms2.Quest;

namespace Ms2.Ui.Guide;

/// <summary>
/// The attribute readers the guide loaders use (commonfiles/tinyxmlhelper.cpp and the guide's own optional readers in
/// unnamed/unit_1413a9df0_6.cpp). Each one leaves the field as it was when the attribute is missing or does not parse, as
/// TinyXML's Query*Attribute does; the optional readers clear the value first.
/// </summary>
static class GuideXml
{
    public static void Int(XElement e, string name, ref int v) => QuestTable.QueryInt(e, name, ref v);
    public static void UInt(XElement e, string name, ref int v) => QuestTable.QueryUInt(e, name, ref v);
    public static void Bool(XElement e, string name, ref bool v) => QuestTable.QueryBool(e, name, ref v);

    /// <summary>XmlGetAttr into a CStringA: the attribute's text when it is present.</summary>
    public static void String(XElement e, string name, ref string v)
    {
        var s = (string)e.Attribute(name);
        if (s != null) v = s;
    }

    /// <summary>XmlGetAttr into a float (QueryFloatAttribute, sscanf "%f").</summary>
    public static void Float(XElement e, string name, ref float v)
    {
        var s = (string)e.Attribute(name);
        if (s != null && ScanFloat(s, out float f)) v = f;
    }

    /// <summary>FUN_1413b1d00: an optional int, unset unless the attribute reads as one.</summary>
    public static int? OptInt(XElement e, string name)
    {
        var s = (string)e.Attribute(name);
        if (s == null || !ScanIntPrefix(s)) return null;
        int n = 0;
        QuestTable.QueryInt(e, name, ref n);
        return n;
    }

    /// <summary>FUN_1413b1ed0: an optional bool (QueryBoolAttribute).</summary>
    public static bool? OptBool(XElement e, string name)
    {
        var s = (string)e.Attribute(name);
        if (string.IsNullOrEmpty(s)) return null;
        switch (s.ToLowerInvariant())
        {
            case "true": case "yes": case "1": return true;
            case "false": case "no": case "0": return false;
        }
        return null;
    }

    /// <summary>XmlQuery Point3 (@0x140874760): "x,y,z"; anything but exactly three values leaves it.</summary>
    public static bool Point3(XElement e, string name, ref Vector3 v)
    {
        var s = (string)e.Attribute(name);
        if (s == null) return false;
        var f = Floats(s, ",");
        if (f.Count != 3) return false;
        v = new Vector3(f[0], f[1], f[2]);
        return true;
    }

    /// <summary>FUN_1413b1e70: an optional Point3.</summary>
    public static Vector3? OptPoint3(XElement e, string name)
    {
        var v = Vector3.Zero;
        return Point3(e, name, ref v) ? v : null;
    }

    /// <summary>FUN_1413b1d60: "a-b" (exactly two values) into a range.</summary>
    public static void Range(XElement e, string name, ref GuideRange v)
    {
        var s = (string)e.Attribute(name);
        if (s == null) return;
        var parts = Tokens(s, "-");
        if (parts.Count != 2) return;
        v = new GuideRange(Atoi(parts[0]), Atoi(parts[1]));
    }

    /// <summary>ReadValueList (guideaction.cpp @0x141386ab0): the "v" of each <paramref name="child"/> element.</summary>
    public static List<int> IntList(XElement e, string child)
    {
        var list = new List<int>();
        foreach (var c in e.Elements(child))
        {
            int n = 0;
            Int(c, "v", ref n);
            list.Add(n);
        }
        return list;
    }

    /// <summary>ReadValueList (@0x141386b50): the "v" text of each <paramref name="child"/> element.</summary>
    public static List<string> StringList(XElement e, string child)
    {
        var list = new List<string>();
        foreach (var c in e.Elements(child))
        {
            string s = "";
            String(c, "v", ref s);
            list.Add(s);
        }
        return list;
    }

    /// <summary>The tokens of <paramref name="s"/> between any of <paramref name="separators"/> (CString::Tokenize: no empty ones).</summary>
    public static List<string> Tokens(string s, string separators) =>
        s.Split(separators.ToCharArray(), StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>XmlSplit into floats: atof of each token.</summary>
    public static List<float> Floats(string s, string separators) =>
        Tokens(s, separators).Select(t => ScanFloat(t, out float f) ? f : 0f).ToList();

    /// <summary>XmlTokenList into ints: atoi of each comma-separated token.</summary>
    public static List<int> Ints(string s) => Tokens(s, ",").Select(Atoi).ToList();

    public static int Atoi(string s)
    {
        var m = Regex.Match(s, @"^\s*[+-]?\d+");
        return m.Success && long.TryParse(m.Value, NumberStyles.AllowLeadingWhite | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long n)
            ? (int)n : 0;
    }

    static bool ScanIntPrefix(string s) => Regex.IsMatch(s, @"^\s*[+-]?\d");

    static bool ScanFloat(string s, out float f)
    {
        f = 0;
        var m = Regex.Match(s, @"^\s*[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?");
        if (!m.Success) return false;
        f = (float)double.Parse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture);
        return true;
    }
}

/// <summary>Unk_GameEventIntRange (commonfiles/gameevent.h): an inclusive [Min, Max] the Trigger condition tests.</summary>
public readonly record struct GuideRange(int Min, int Max)
{
    /// <summary>IsRangeSet: Min &gt; Max or 0/0 is unset.</summary>
    public bool IsSet => Min <= Max && !(Min == 0 && Max == 0);

    public bool Contains(int n) => Min <= n && n <= Max;
}
