using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.Cache;
using Ms2.Ui;

namespace Ms2.Formats.World;

/// <summary>
/// The data that keeps one file per row instead of one table: GMS2's Xml.m2d has map/&lt;id&gt;.xml and npc/&lt;aa&gt;/&lt;bb&gt;/&lt;id&gt;.xml
/// where the KMS tables hold &lt;fieldData id&gt; / &lt;npc id&gt; rows. A file's &lt;ms2&gt; root holds the same &lt;environment&gt; children a
/// table row holds, plus the locale and feature attributes the KMS rows of these tables do not carry.
/// A data-shape adapter: the client reads its own layout (single tables), and the GMS2 client's reader and path rule are
/// not in the restored source (its exe is packed, so unread; STILL-OPEN). The id is the file name's number.
/// Reading thousands of files is done once per install: <see cref="BuildCache"/> at setup writes the merged table into the
/// install's cache store and a launch reads that one file (<see cref="Rows"/>), not the files.
/// </summary>
public static class PerFileTable
{
    /// <summary>The KMS layout where the data has it (its file is read, the per-file folder never touched), else the per-file rows.</summary>
    public static T Choose<T>(bool kmsHas, Func<T> kms, Func<T> perFile) => kmsHas ? kms() : perFile();

    /// <summary>
    /// A table document of the files under <paramref name="folder"/> ("map/", "npc/"): one &lt;<paramref name="row"/> id&gt; per file,
    /// holding a copy of the environment the client picks (FeatureElements.SelectEnvironment, FUN_14028f8d0) with its locale and
    /// feature attributes dropped, which is the shape of the KMS row's single &lt;environment&gt;. A file without an available
    /// environment or a readable name or content has no row. Null when the pack has no such file, so a table that is absent
    /// in this data stays absent. Read only: the document is shared.
    /// </summary>
    public static XDocument Rows(PackSet packs, string folder, string row)
    {
        // once per pack set and folder: the readers only read the tree
        var cache = built.GetOrCreateValue(packs);
        lock (cache)
        {
            string key = folder + "|" + row;
            if (cache.TryGetValue(key, out var d)) return d;
            if (!Has(packs, folder)) return cache[key] = null;
            var store = CacheStore.Of?.Invoke(packs);
            if (store == null) return cache[key] = Parse(packs, folder, row);
            var t = store.Get(Piece(folder), Config(packs), Code, () => new Table(Parse(packs, folder, row)),
                Write, Read, (a, b) => Text(a.Doc) == Text(b.Doc) ? null : "rows differ", out var outcome);
            CacheStore.Logged?.Invoke(outcome);
            return cache[key] = t.Doc;
        }
    }

    /// <summary>Writes the merged table of the folder into the install's cache store when it is missing (the setup step); true when written.</summary>
    public static bool BuildCache(PackSet packs, CacheStore store, string folder, string row) =>
        Has(packs, folder) && store.Ensure(Piece(folder), Config(packs), Code, () => new Table(Parse(packs, folder, row)), Write);

    sealed record Table(XDocument Doc);
    /// <summary>The cache config: the features stamp and the data's locale, which SelectEnvironment picks the rows by (a loose table/na or table/kr folder changes it).</summary>
    internal static string Config(PackSet packs) => LobbyFeatures.Stamp(packs) + "|" + LobbyFeatures.LocaleOf(packs);
    static string Piece(string folder) => "perfile-" + folder.TrimEnd('/');
    static string Code => CacheStore.Build(typeof(PerFileTable).Assembly);
    static string Text(XDocument d) => d?.ToString(SaveOptions.DisableFormatting) ?? "";
    static byte[] Write(Table t) => Serialize(t.Doc);
    internal static byte[] Serialize(XDocument d) => System.Text.Encoding.UTF8.GetBytes(Text(d));
    internal static XDocument Deserialize(byte[] body) => Read(body).Doc;
    static Table Read(byte[] body) => new(body.Length == 0 ? null : PackXml.Parse(body));

    static bool Has(PackSet packs, string folder) => packs.List("Xml").Any(n => n.StartsWith(folder, StringComparison.Ordinal) && n.EndsWith(".xml", StringComparison.Ordinal));

    static XDocument Parse(PackSet packs, string folder, string row) =>
        Build(Files(packs, folder), row, f => FeatureElements.SelectEnvironment(packs, f));

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, Dictionary<string, XDocument>> built = new();

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

    /// <summary>
    /// GMS2's itemmodel: itempreset/&lt;a&gt;/&lt;bb&gt;/&lt;id&gt;.xml, one file per model whose &lt;ms2&gt; root holds the children of the KMS
    /// itemmodel/&lt;id / 100000&gt;.xml &lt;ItemModel id&gt; row (slots with their assets, customize). The rows of one prefix file, or null
    /// when the pack has no preset of that prefix. Read only. The customize element is the file's own. The client reads
    /// colorPalette from it (itemdatastruct.cpp:936); 262 of the 7079 customize elements of the GMS2 presets have one (hair 10200001
    /// has colorPalette="2"), the others carry colordetail / ch0 / ch2 only. STILL-OPEN: whether the KMS rows of those others had
    /// a colorPalette the GMS2 data dropped, and where their default dye then comes from.
    /// </summary>
    public static XDocument ItemModels(PackSet packs, int prefix)
    {
        var cache = presets.GetOrCreateValue(packs);
        lock (cache)
        {
            cache.Index ??= PresetIndex(packs.List("Xml"));
            if (!cache.Index.TryGetValue(prefix, out var names)) return null;
            if (cache.Docs.TryGetValue(prefix, out var d)) return d;
            var root = new XElement("ms2");
            foreach (var (id, name) in names)
            {
                XElement file;
                try { file = PackXml.Parse(packs.Read("Xml", name)).Root; } catch (System.Xml.XmlException) { continue; }
                if (file != null) root.Add(new XElement("ItemModel", new XAttribute("id", id.ToString(CultureInfo.InvariantCulture)), file.Elements()));
            }
            return cache.Docs[prefix] = new XDocument(root);
        }
    }

    sealed class Presets { public Dictionary<int, List<(long Id, string Name)>> Index; public readonly Dictionary<int, XDocument> Docs = new(); }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, Presets> presets = new();

    /// <summary>prefix (id / 100000) -> the itempreset files of it, in id order.</summary>
    internal static Dictionary<int, List<(long Id, string Name)>> PresetIndex(IEnumerable<string> names)
    {
        var index = new Dictionary<int, List<(long, string)>>();
        foreach (var n in names)
        {
            if (!n.StartsWith("itempreset/", StringComparison.Ordinal) || !n.EndsWith(".xml", StringComparison.Ordinal) || IdOf(n) is not long id) continue;
            int prefix = (int)(id / 100000);
            if (!index.TryGetValue(prefix, out var l)) index[prefix] = l = new();
            l.Add((id, n));
        }
        foreach (var l in index.Values) l.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return index;
    }
}
