using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// A pack's XML entry may start with a UTF-8 byte-order mark (KMS2 Character/female/female.xml does); the client's TinyXML
/// skips it. A field load stopped on "Data at the root level is invalid. Line 1, position 1" where a reader left it in.
/// </summary>
public class PackXmlTests
{
    const string Body = "<ms2><key text=\"a\" time=\"0.5\"/></ms2>";

    static byte[] WithBom(string text) => new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(text)).ToArray();

    [Test]
    public void Utf8BomEntryParses()
    {
        var doc = PackXml.Parse(WithBom(Body));
        Assert.That((string)doc.Root!.Element("key")!.Attribute("text")!, Is.EqualTo("a"));
    }

    [Test]
    public void TheOldReadFailsOnTheSameBytes()
    {
        // XDocument.Parse(Encoding.UTF8.GetString(bytes)), the shape every site had before PackXml
        Assert.Throws<XmlException>(() => XDocument.Parse(Encoding.UTF8.GetString(WithBom(Body))));
    }

    [Test]
    public void PlainRepeatedBomAndUtf16Parse()
    {
        Assert.That(PackXml.Parse(Encoding.UTF8.GetBytes(Body)).Root!.Name.LocalName, Is.EqualTo("ms2"));
        Assert.That(PackXml.Parse(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(WithBom(Body)).ToArray()).Root!.Name.LocalName, Is.EqualTo("ms2"));
        Assert.That(PackXml.Parse(new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes(Body)).ToArray()).Root!.Name.LocalName, Is.EqualTo("ms2"));
        Assert.That(PackXml.Parse(new byte[] { 0xFE, 0xFF }.Concat(Encoding.BigEndianUnicode.GetBytes(Body)).ToArray()).Root!.Name.LocalName, Is.EqualTo("ms2"));
    }

    [Test]
    public void AnEncodingDeclarationIsIgnoredAsTinyXmlDoes()
    {
        var doc = PackXml.Parse(WithBom("<?xml version=\"1.0\" encoding=\"euc-kr\"?>" + Body));
        Assert.That(doc.Root!.Name.LocalName, Is.EqualTo("ms2"));
    }

    [Test]
    public void TheReaderGivesTheTextAndTakesTheSameEntries()
    {
        // the forward-only form of Text: the same characters for every mark (a cut one too), and an XmlReader over it ignores a declaration
        var forms = new[]
        {
            Encoding.UTF8.GetBytes(Body), WithBom(Body), new byte[] { 0xEF, 0xBB, 0xBF }.Concat(WithBom(Body)).ToArray(),
            new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("\uFEFF" + Body)).ToArray(),
            new byte[] { 0xFE, 0xFF }.Concat(Encoding.BigEndianUnicode.GetBytes(Body)).ToArray(),
            WithBom("<?xml version=\"1.0\" encoding=\"euc-kr\"?>" + Body), Encoding.UTF8.GetBytes("\uFEFF\uFEFF" + Body),
            new byte[] { 0xFF }, new byte[] { 0xEF, 0xBB }, Array.Empty<byte>(),
        };
        foreach (var data in forms)
        {
            using (var r = PackXml.Reader(new MemoryStream(data))) Assert.That(r.ReadToEnd(), Is.EqualTo(PackXml.Text(data)));
            if (data.Length < Body.Length) continue;   // the cut marks: text only
            using var x = XmlReader.Create(PackXml.Reader(new MemoryStream(data)), new XmlReaderSettings { CloseInput = true });
            Assert.That(x.MoveToContent() == XmlNodeType.Element && x.LocalName == "ms2", Is.True);
        }
    }

    /// <summary>No site parses a pack entry with XDocument.Parse(Encoding.UTF8.GetString(...Read(...))): that is PackXml.Parse.</summary>
    [Test]
    public void NoSiteParsesAPackEntryByHand()
    {
        string? root = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "src", "Ms2.Formats")) && Directory.Exists(Path.Combine(d.FullName, "game"))) { root = d.FullName; break; }
        if (root == null) Assert.Ignore("source tree not found");
        var hand = new Regex(@"XDocument\.Parse\(\s*(System\.Text\.)?Encoding\.UTF8\.GetString\(");
        var bad = new List<string>();
        foreach (var dir in new[] { "src", "game" })
            foreach (var f in Directory.EnumerateFiles(Path.Combine(root!, dir), "*.cs", SearchOption.AllDirectories))
            {
                if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || f.Contains($"{Path.DirectorySeparatorChar}.godot{Path.DirectorySeparatorChar}")) continue;
                if (hand.IsMatch(File.ReadAllText(f))) bad.Add(Path.GetRelativePath(root!, f));
            }
        Assert.That(bad, Is.Empty, "use PackXml.Parse(bytes)");
    }

    /// <summary>
    /// The body companion files and the action table of every client set beside MS2_DATA (Game Versions/*/Data), as the field
    /// load reads them: KMS2_DEV's male.xml and female.xml start with a UTF-8 mark, KMS2's do not; all must parse.
    /// </summary>
    [Test]
    public void EverySetsBodyXmlAndActionTableParse()
    {
        var data = System.Environment.GetEnvironmentVariable("MS2_DATA");
        var versions = string.IsNullOrEmpty(data) ? null : Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(data.TrimEnd('/', '\\'))));
        if (versions == null || !Directory.Exists(versions)) Assert.Ignore("MS2_DATA not set");
        int sets = 0;
        foreach (var dir in Directory.EnumerateDirectories(versions!).Select(d => Path.Combine(d, "Data")).Where(Directory.Exists))
        {
            if (!File.Exists(Path.Combine(dir, "Xml.m2d"))) continue;
            using var packs = new PackSet(dir);
            Assert.That(PackXml.Read(packs, "Xml", "table/pcactiondata.xml").Root, Is.Not.Null, dir);
            if (File.Exists(Path.Combine(dir, "Resource", "Model", "Character.m2d")))
                foreach (var body in new[] { "male", "female" })
                    Assert.That(PackXml.Read(packs, "Resource/Model/Character", $"{body}/{body}.xml").Root, Is.Not.Null, $"{dir} {body}");
            sets++;
        }
        Assert.That(sets, Is.GreaterThan(0));
    }
}
