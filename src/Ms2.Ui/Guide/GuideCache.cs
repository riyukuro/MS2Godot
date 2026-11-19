using System.Text;
using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Ui.Guide;

/// <summary>
/// The per-install cache of the guide tables (<see cref="GuideTable"/>): every file's &lt;event&gt; elements after the
/// feature filter, parsed once from the player's Xml.m2d and written next to the UI code (the game's
/// user://uigen/KEY/guide-XMLKEY-vN.bin). The file name carries the Xml.m2d key (Skills.AdditionalCache.XmlKey) and
/// <see cref="Version"/>, so other client data or another parse gets another file. The events are stored as node trees
/// (elements, attributes, text) over one string table, so a cached table loads through the same GuideManager.Load as a
/// parsed one; a file that fails its checks is not used (the table is then parsed) and the next setup step writes it
/// again. Nothing of it is in the repo.
/// </summary>
public static class GuideCache
{
    /// <summary>The format and parse version: bump it with any change to what the build writes or how the XML is read.</summary>
    public const int Version = 1;
    const uint Magic = 0x4447324D;     // "M2GD"
    const uint EndMagic = 0x444E4547;  // "GEND"

    const byte NodeElement = 0, NodeText = 1, NodeCData = 2, NodeComment = 3;

    /// <summary>The cache file name for an Xml.m2d key.</summary>
    public static string FileName(string xmlKey) => $"guide-{xmlKey}-v{Version}.bin";

    /// <summary>Parses the guide tables of <paramref name="packs"/> and writes <paramref name="path"/>.</summary>
    public static void Build(PackSet packs, string path) => Write(GuideTable.Parse(packs), path);

    /// <summary>Writes <paramref name="table"/> to <paramref name="path"/> (through a temporary file, so a stopped build
    /// leaves no partial cache).</summary>
    public static void Write(GuideTable table, string path)
    {
        var strings = new List<string>();
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        int Id(string s)
        {
            if (!ids.TryGetValue(s, out int i)) { i = strings.Count; strings.Add(s); ids.Add(s, i); }
            return i;
        }

        var body = new MemoryStream();
        var bw = new BinaryWriter(body, Encoding.UTF8);
        bw.Write(table.BadFiles.Count);
        foreach (var f in table.BadFiles) bw.Write(Id(f));
        bw.Write(table.Files.Count);
        foreach (var f in table.Files)
        {
            bw.Write(Id(f.Path));
            bw.Write(f.Events.Count);
            foreach (var e in f.Events) WriteNode(bw, e, Id);
        }
        bw.Flush();

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        string tmp = path + ".tmp";
        using (var fs = File.Create(tmp))
        using (var w = new BinaryWriter(fs, Encoding.UTF8))
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write(strings.Count);
            foreach (var s in strings) w.Write(s);
            w.Write((int)body.Length);
            body.WriteTo(fs);
            w.Write(EndMagic);
        }
        File.Move(tmp, path, true);
    }

    static void WriteNode(BinaryWriter w, XNode node, Func<string, int> id)
    {
        switch (node)
        {
            case XElement e:
                w.Write(NodeElement);
                w.Write(id(e.Name.ToString()));
                var attrs = e.Attributes().ToList();
                w.Write(attrs.Count);
                foreach (var a in attrs) { w.Write(id(a.Name.ToString())); w.Write(id(a.Value)); }
                var children = e.Nodes().ToList();
                w.Write(children.Count);
                foreach (var c in children) WriteNode(w, c, id);
                break;
            case XCData c:
                w.Write(NodeCData); w.Write(id(c.Value));
                break;
            case XText t:
                w.Write(NodeText); w.Write(id(t.Value));
                break;
            case XComment c:
                w.Write(NodeComment); w.Write(id(c.Value));
                break;
            default:
                throw new InvalidDataException($"guide cache: no form for a {node.NodeType} node");
        }
    }

    /// <summary>The table in <paramref name="path"/>; null when the file is missing or fails its checks.</summary>
    public static GuideTable Read(string path)
    {
        if (path == null || !File.Exists(path)) return null;
        try
        {
            var bytes = File.ReadAllBytes(path);
            var r = new BinaryReader(new MemoryStream(bytes, false), Encoding.UTF8);
            if (r.ReadUInt32() != Magic || r.ReadInt32() != Version) return null;
            int n = r.ReadInt32();
            if (n < 0) return null;
            var strings = new string[n];
            for (int i = 0; i < n; i++) strings[i] = r.ReadString();
            int len = r.ReadInt32();
            long end = r.BaseStream.Position + len;
            if (len < 0 || end + 4 != bytes.Length) return null;

            var names = new Dictionary<int, XName>();
            XName Name(int i) => names.TryGetValue(i, out var x) ? x : names[i] = XName.Get(strings[i]);
            var table = new GuideTable();
            int bad = r.ReadInt32();
            for (int i = 0; i < bad; i++) table.BadFiles.Add(strings[r.ReadInt32()]);
            int files = r.ReadInt32();
            for (int i = 0; i < files; i++)
            {
                string file = strings[r.ReadInt32()];
                int count = r.ReadInt32();
                var events = new List<XElement>(Math.Max(0, count));
                for (int k = 0; k < count; k++)
                    events.Add(ReadNode(r, strings, Name) as XElement ?? throw new InvalidDataException("guide cache: an event is not an element"));
                table.Files.Add(new GuideTableFile(file, events));
            }
            if (r.BaseStream.Position != end || r.ReadUInt32() != EndMagic) return null;
            return table;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or IndexOutOfRangeException or ArgumentException or FormatException
                                      or System.Xml.XmlException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static XNode ReadNode(BinaryReader r, string[] strings, Func<int, XName> name)
    {
        byte kind = r.ReadByte();
        switch (kind)
        {
            case NodeElement:
                var e = new XElement(name(r.ReadInt32()));
                int attrs = r.ReadInt32();
                for (int i = 0; i < attrs; i++)
                {
                    var an = name(r.ReadInt32());
                    e.Add(new XAttribute(an, strings[r.ReadInt32()]));
                }
                int children = r.ReadInt32();
                for (int i = 0; i < children; i++) e.Add(ReadNode(r, strings, name));
                return e;
            case NodeText: return new XText(strings[r.ReadInt32()]);
            case NodeCData: return new XCData(strings[r.ReadInt32()]);
            case NodeComment: return new XComment(strings[r.ReadInt32()]);
            default: throw new InvalidDataException("guide cache: bad node kind");
        }
    }

    /// <summary>The first difference between two tables (MS2_UICACHE=check and the tests), null when they are the same:
    /// the same files with no &lt;ms2&gt; root, the same files in order and the same events (XNode.DeepEquals).</summary>
    public static string FirstDifference(GuideTable parsed, GuideTable cached)
    {
        if (cached == null) return "no cache";
        if (!parsed.BadFiles.SequenceEqual(cached.BadFiles, StringComparer.Ordinal)) return "the files without an <ms2> root differ";
        if (parsed.Files.Count != cached.Files.Count) return $"{parsed.Files.Count} files parsed, {cached.Files.Count} cached";
        for (int i = 0; i < parsed.Files.Count; i++)
        {
            var a = parsed.Files[i];
            var b = cached.Files[i];
            if (a.Path != b.Path) return $"file {i}: {a.Path} parsed, {b.Path} cached";
            if (a.Events.Count != b.Events.Count) return $"{a.Path}: {a.Events.Count} events parsed, {b.Events.Count} cached";
            for (int k = 0; k < a.Events.Count; k++)
                if (!XNode.DeepEquals(a.Events[k], b.Events[k]))
                    return $"{a.Path}: event {k} (id {(string)a.Events[k].Attribute("id")}) differs";
        }
        return null;
    }
}
