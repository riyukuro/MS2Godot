using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Ui;

namespace Ms2.Formats.World;

/// <summary>
/// The data that keeps one file per row instead of one table: GMS2's Xml.m2d has map/&lt;id&gt;.xml and npc/&lt;aa&gt;/&lt;bb&gt;/&lt;id&gt;.xml
/// where the KMS tables hold &lt;fieldData id&gt; / &lt;npc id&gt; rows. A file's &lt;ms2&gt; root holds the same &lt;environment&gt; children a
/// table row holds, plus the locale and feature attributes the KMS rows of these tables do not carry.
/// STILL-OPEN: the GMS2 client's own reader and path rule (its exe is packed, so unread); the id is the file name's number.
/// </summary>
public static class PerFileTable
{
    /// <summary>
    /// A table document of the files under <paramref name="folder"/> ("map/", "npc/"): one &lt;<paramref name="row"/> id&gt; per file,
    /// holding a copy of the environment the client picks (FeatureElements.SelectEnvironment, FUN_14028f8d0) with its locale and
    /// feature attributes dropped, which is the shape of the KMS row's single &lt;environment&gt;. A file without an available
    /// environment or a readable name or content has no row. Null when the pack has no such file, so a table that is absent
    /// in this data stays absent.
    /// </summary>
    public static XDocument Rows(PackSet packs, string folder, string row) => Build(Files(packs, folder), row, f => FeatureElements.SelectEnvironment(packs, f));

    static IEnumerable<(string Name, XElement Root)> Files(PackSet packs, string folder)
    {
        foreach (var name in packs.List("Xml"))
        {
            if (!name.StartsWith(folder, StringComparison.Ordinal) || !name.EndsWith(".xml", StringComparison.Ordinal)) continue;
            XElement root = null;
            if (IdOf(name) != null)
                try { root = PackXml.Parse(packs.Read("Xml", name)).Root; } catch (System.Xml.XmlException) { }
            yield return (name, root);
        }
    }

    static long? IdOf(string name)
    {
        int slash = name.LastIndexOf('/');
        return long.TryParse(name.AsSpan(slash + 1, name.Length - slash - 5), NumberStyles.None, CultureInfo.InvariantCulture, out long id) ? id : null;
    }

    /// <summary>The rows of the files (entry name, parsed root or null), each file's environment picked by <paramref name="pick"/>.</summary>
    internal static XDocument Build(IEnumerable<(string Name, XElement Root)> files, string row, Func<XElement, XElement> pick)
    {
        XElement root = null;
        foreach (var (name, file) in files)
        {
            if (IdOf(name) is not long id) continue;
            root ??= new XElement("ms2");
            if (file == null || pick(file) is not { } env) continue;
            var copy = new XElement(env);
            copy.SetAttributeValue("locale", null);
            copy.SetAttributeValue("feature", null);
            root.Add(new XElement(row, new XAttribute("id", id.ToString(CultureInfo.InvariantCulture)), copy));
        }
        return root == null ? null : new XDocument(root);
    }
}
