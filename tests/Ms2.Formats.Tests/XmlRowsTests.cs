using System.Text;
using System.Xml;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>XmlRows reads the rows XDocument would give: the same elements, in order, with the same attribute values.</summary>
public class XmlRowsTests
{
    static List<string> Dom(IEnumerable<XElement> rows) =>
        rows.Select(e => string.Join("|", e.Attributes().Select(a => a.Name + "=" + a.Value))).ToList();

    static List<string> Streamed(byte[] data, string name, bool rootChildren) =>
        XmlRows.Elements(data, name, rootChildren).Select(r =>
        {
            var parts = new List<string>();
            if (r.MoveToFirstAttribute())
            {
                do parts.Add(XName.Get(r.LocalName, r.NamespaceURI) + "=" + r.Value);
                while (r.MoveToNextAttribute());
                r.MoveToElement();
            }
            return string.Join("|", parts);
        }).ToList();

    static void Same(byte[] data, string name, string what)
    {
        var doc = GameStrings.Parse(data);
        Assert.That(Streamed(data, name, false), Is.EqualTo(Dom(doc.Descendants(name))), what + ": Descendants");
        Assert.That(Streamed(data, name, true), Is.EqualTo(Dom(doc.Root.Elements(name))), what + ": Root.Elements");
    }

    [Test]
    public void SyntheticRowsMatchTheDocument()
    {
        // two byte-order marks, an internal entity, character references, whitespace in values, rows at several depths,
        // namespaced look-alikes, a missing attribute, a comment and an invalid UTF-8 byte inside a value
        var head = new byte[] { 0xEF, 0xBB, 0xBF, 0xEF, 0xBB, 0xBF };
        var text = "<?xml version=\"1.0\" encoding=\"euc-kr\"?>\n<!DOCTYPE ms2 [<!ENTITY who \"Tria &amp; Co\">]>\n" +
                   "<ms2 xmlns:a=\"urn:x\">\n  <key id=\"1\" name=\"&who;\"/>\n  <!-- <key id=\"0\"/> -->\n" +
                   "  <key id=\"2\" name=\"a&#10;b&#x9;c\td\ne\"><key id=\"3\" name=\"inner\"/></key>\n" +
                   "  <group><key id=\"4\"/><a:key id=\"5\" name=\"ns\"/><key xmlns=\"urn:y\" id=\"6\"/></group>\n" +
                   "  <key id=\"7\" name=\"bad X here\" a:extra=\"q\"/>\n</ms2>\n";
        var body = Encoding.UTF8.GetBytes(text);
        int x = Array.IndexOf(body, (byte)'X');
        body[x] = 0xFF;                                              // not UTF-8: both read U+FFFD
        var data = head.Concat(body).ToArray();
        Same(data, "key", "synthetic");
        Assert.That(XmlRows.Elements(data, "key").Count(), Is.EqualTo(5));
        Assert.That(XmlRows.Elements(data, "key", true).Select(r => r.GetAttribute("id")), Is.EqualTo(new[] { "1", "2", "7" }));
        var names = XmlRows.Elements(data, "key").Select(r => r.GetAttribute("name")).ToList();
        Assert.That(names, Is.EqualTo(new[] { "Tria & Co", "a\nb\tc d e", "inner", null, "bad \uFFFD here" }));
    }

    [Test]
    public void MalformedXmlThrowsAsTheDocumentDoes()
    {
        var data = Encoding.UTF8.GetBytes("<ms2><key id=\"1\"/><key id=\"2\"></ms2>");
        Assert.Throws<XmlException>(() => GameStrings.Parse(data));
        Assert.Throws<XmlException>(() => XmlRows.Elements(data, "key").ToList());
    }

    [Test]
    public void StringTablesMatchTheDocument()
    {
        var dataDir = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(dataDir)) Assert.Ignore("MS2_DATA not set");
        var packs = new PackSet(dataDir);
        int files = 0;
        foreach (var entry in packs.List("Xml").Where(n => (n.StartsWith("string/en/") || n.StartsWith("string/kr/")) && n.EndsWith(".xml")).OrderBy(n => n))
        {
            Same(packs.Read("Xml", entry), "key", entry);
            files++;
        }
        TestContext.Out.WriteLine($"{files} string tables");
        Assert.That(files, Is.GreaterThan(20));
    }
}
