using System.Text;
using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Ui.Guide;

/// <summary>
/// The bodies of the per-install guide cache, the pieces <see cref="GuideTable"/> and <see cref="GuideStrings"/> keep
/// in the table cache store (user://uigen/data-KEY, keyed by the archives' hashes, the load's configuration and the
/// code's build): every file's &lt;event&gt; elements after the feature filter, and the texts and voices as loaded.
/// The events are stored as node trees (elements, attributes, text) over one string table, so a cached table loads
/// through the same GuideManager.Load as a parsed one; a body that fails its checks is not used (the table is then
/// parsed) and the next setup step writes it again. Nothing of it is in the repo.
/// </summary>
public static class GuideCache
{
    /// <summary>The format and parse version: bump it with any change to what the build writes or how the XML is read.</summary>
    public const int Version = 1;
    const uint Magic = 0x4447324D;     // "M2GD"
    const uint EndMagic = 0x444E4547;  // "GEND"
    const uint StringsMagic = 0x5347324D;     // "M2GS"
    const uint StringsEndMagic = 0x534E4547;  // "GENS"

    const byte NodeElement = 0, NodeText = 1, NodeCData = 2, NodeComment = 3;

    /// <summary>The cache body of <paramref name="table"/> (the store's file wraps it).</summary>
    public static byte[] WriteBody(GuideTable table)
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

        var w = new MemoryStream();
        var b = new BinaryWriter(w, Encoding.UTF8);
        b.Write(Magic);
        b.Write(Version);
        b.Write(strings.Count);
        foreach (var s in strings) b.Write(s);
        b.Write((int)body.Length);
        body.WriteTo(w);
        b.Write(EndMagic);
        b.Flush();
        return w.ToArray();
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

    /// <summary>The table of a cache body; null when it fails its checks. The store's file holds the body, so a body
    /// that fails is its reader's bug or a damaged write.</summary>
    public static GuideTable ReadBody(byte[] bytes)
    {
        try
        {
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

    // ---- the guide strings (GuideStrings) --------------------------------------------------------------------------

    /// <summary>The strings cache body of <paramref name="strings"/> (the store's file wraps it): the texts and
    /// voices as loaded, over the union of their ids, in id order.</summary>
    public static byte[] WriteStringsBody(GuideStrings strings)
    {
        var texts = strings.TextMap;
        var voices = strings.VoiceMap;
        var ids = new SortedSet<uint>(texts.Keys);
        ids.UnionWith(voices.Keys);
        var w = new MemoryStream();
        var b = new BinaryWriter(w, Encoding.UTF8);
        b.Write(StringsMagic);
        b.Write(Version);
        b.Write(ids.Count);
        foreach (var id in ids)
        {
            b.Write(id);
            b.Write(texts.TryGetValue(id, out var t) ? t : "");
            b.Write(voices.TryGetValue(id, out var v) ? v : "");
        }
        b.Write(StringsEndMagic);
        b.Flush();
        return w.ToArray();
    }

    /// <summary>The strings of a cache body; null when it fails its checks. The store's file holds the body.</summary>
    public static GuideStrings ReadStringsBody(byte[] bytes)
    {
        try
        {
            var r = new BinaryReader(new MemoryStream(bytes, false), Encoding.UTF8);
            if (r.ReadUInt32() != StringsMagic || r.ReadInt32() != Version) return null;
            int count = r.ReadInt32();
            if (count < 0 || (long)count * 4 > bytes.Length) return null;
            var s = new GuideStrings();
            var texts = s.TextMap;
            var voices = s.VoiceMap;
            for (int i = 0; i < count; i++)
            {
                uint id = r.ReadUInt32();
                string text = r.ReadString();
                string voice = r.ReadString();
                if (text.Length != 0) texts[id] = text;
                if (voice.Length != 0) voices[id] = voice;
            }
            if (r.ReadUInt32() != StringsEndMagic || r.BaseStream.Position != bytes.Length) return null;
            return s;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or IndexOutOfRangeException or ArgumentException or FormatException
                                      or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The first difference between two tables (MS2_UICACHE=check and the tests), null when they are the same:
    /// the same ids (the union of texts and voices) in order with the same text and the same voice.</summary>
    public static string FirstStringsDifference(GuideStrings parsed, GuideStrings cached)
    {
        if (cached == null) return "no cache";
        var a = new SortedSet<uint>(parsed.TextMap.Keys);
        a.UnionWith(parsed.VoiceMap.Keys);
        var b = new SortedSet<uint>(cached.TextMap.Keys);
        b.UnionWith(cached.VoiceMap.Keys);
        if (!a.SetEquals(b)) return $"{a.Count} ids parsed, {b.Count} cached";
        foreach (var id in a)
        {
            if (parsed.Text(id) != cached.Text(id)) return $"text {id} differs";
            if (parsed.Voice(id) != cached.Voice(id)) return $"the voice of text {id} differs";
        }
        return null;
    }
}
