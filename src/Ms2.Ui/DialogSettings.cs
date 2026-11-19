using System.Globalization;
using System.Xml.Linq;

namespace Ms2.Ui;

/// <summary>
/// The character's UI settings folder: GetCharacterFolder (unit_141274d10_uihelpers.cpp:408) is
/// "../../Config/&lt;account id&gt;/&lt;character id&gt;" and a dialog keeps one file there, &lt;GetTypeName()&gt;.xml.
/// </summary>
public interface ICharacterFolder
{
    /// <summary>The file's text, or null when there is none (TiXmlDocument::LoadFile fails).</summary>
    string Read(string file);
    void Write(string file, string text);
}

/// <summary>
/// A dialog's settings file as UIDialog writes it (vfunc_4, uidialog.cpp:261): the document is cleared, a root element named
/// after the type name is added and vfunc_53 fills it. Attributes go on child elements (XmlSetChildAttr reuses the first
/// child of the name, tinyxmlhelper.cpp:101); a float is written by TiXmlAttribute::SetDoubleValue as "%g".
/// </summary>
public sealed class DialogSettings
{
    public readonly XElement Root;

    public DialogSettings(string typeName) => Root = new XElement(typeName);
    DialogSettings(XElement root) => Root = root;

    /// <summary>vfunc_5 (uidialog.cpp:279): the file's first child named after the type, else null.</summary>
    public static DialogSettings Parse(string typeName, string text)
    {
        if (text == null) return null;
        XDocument doc;
        try { doc = XDocument.Parse(text); } catch (System.Xml.XmlException) { return null; }
        var root = doc.Root?.Name.LocalName == typeName ? doc.Root : null;
        return root == null ? null : new DialogSettings(root);
    }

    public override string ToString() => new XDocument(Root).ToString();

    XElement Child(string name)
    {
        var c = Root.Element(name);
        if (c == null) Root.Add(c = new XElement(name));
        return c;
    }

    public void Set(string child, string name, int value) => Child(child).SetAttributeValue(name, value.ToString(CultureInfo.InvariantCulture));
    public void Set(string child, string name, float value) => Child(child).SetAttributeValue(name, FormatG(value));

    /// <summary>XmlGetChildAttr: the value when the child and the attribute are there and parse, else <paramref name="value"/> is kept.</summary>
    public void Get(string child, string name, ref int value)
    {
        if ((string)Root.Element(child)?.Attribute(name) is string s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) value = v;
    }

    public void Get(string child, string name, ref float value)
    {
        if ((string)Root.Element(child)?.Attribute(name) is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) value = (float)v;
    }

    /// <summary>XmlSetAttr (bool): an attribute on the root element itself, "true" or "false".</summary>
    public void SetAttr(string name, bool value) => Root.SetAttributeValue(name, value ? "true" : "false");
    /// <summary>XmlSetAttr (int): an attribute on the root element itself.</summary>
    public void SetAttr(string name, int value) => Root.SetAttributeValue(name, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// XmlGetAttr (bool&amp;): TiXmlElement::QueryBoolAttribute (tinyxml.cpp:618) on the root element: an empty or missing
    /// attribute keeps <paramref name="value"/>; else a case-insensitive prefix compare (StringEqual) gives true for
    /// "true", "yes" or "1" and false for "false", "no" or "0"; anything else keeps the value.
    /// </summary>
    public void GetAttr(string name, ref bool value)
    {
        if ((string)Root.Attribute(name) is not { Length: > 0 } s) return;
        if (Prefix(s, "true") || Prefix(s, "yes") || Prefix(s, "1")) value = true;
        else if (Prefix(s, "false") || Prefix(s, "no") || Prefix(s, "0")) value = false;
    }

    /// <summary>XmlGetAttr (int&amp;): the root element's attribute when there and it parses, else <paramref name="value"/> is kept.</summary>
    public void GetAttr(string name, ref int value)
    {
        if ((string)Root.Attribute(name) is string s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) value = v;
    }

    /// <summary>StringEqual (tinyxml.cpp) with ignoreCase: the attribute starts with the tag.</summary>
    static bool Prefix(string s, string tag) => s.StartsWith(tag, StringComparison.OrdinalIgnoreCase);

    /// <summary>printf "%g" of a float widened to double: 6 significant digits, trailing zeros dropped, exponent below 1e-4 or from 1e6.</summary>
    public static string FormatG(double v)
    {
        if (v == 0) return "0";
        if (double.IsNaN(v)) return "nan";
        if (double.IsInfinity(v)) return v > 0 ? "inf" : "-inf";
        string e = v.ToString("E5", CultureInfo.InvariantCulture);      // d.dddddE+xxx, rounded to 6 digits
        int exp = int.Parse(e[(e.IndexOf('E') + 1)..], CultureInfo.InvariantCulture);
        if (exp < -4 || exp >= 6)
        {
            string m = e[..e.IndexOf('E')];
            if (m.Contains('.')) m = m.TrimEnd('0').TrimEnd('.');
            return $"{m}e{(exp < 0 ? '-' : '+')}{Math.Abs(exp):00}";
        }
        string f = v.ToString("F" + Math.Max(0, 5 - exp), CultureInfo.InvariantCulture);
        if (f.Contains('.')) f = f.TrimEnd('0').TrimEnd('.');
        return f;
    }
}
