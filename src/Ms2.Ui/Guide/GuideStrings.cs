using System.Text;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui.Guide;

/// <summary>
/// The guide texts (UIGuideEvent::LoadGuideStrings, uiguideeventarrow.cpp @0x14195b970): Xml.m2d's
/// String/&lt;locale&gt;/stringGuide.xml. The client asks for String/stringGuide.xml (FUN_14071ae20,
/// unit_14071a5b0_datapathbuilders.cpp:129) and the pack reads the locale path first (CPackFile::Read,
/// ms2filestring.cpp:357). Every &lt;key&gt; the client's locale and features make available (IsElementAvailable) fills
/// the texts with its name — "\n" written as backslash-n turned into a line break, the object words replaced
/// (CReservedWordTable::ReplaceObjectWords, as every string table load does) — and, with a voiceID, the voices; a later
/// key of the same id wins (CAtlMap::SetAt). A missing file or a root that is not &lt;ms2&gt; leaves it empty, as the
/// client's does (it logs the latter). GetGuideText runs the value words on every read, which the pane owns.
/// <para>The per-install cache (<see cref="GuideCache"/>) holds the texts and voices as loaded; a file that fails its
/// checks is not used (the table is then parsed) and the next setup step writes it again.</para>
/// </summary>
public sealed class GuideStrings
{
    readonly Dictionary<uint, string> texts = new();
    readonly Dictionary<uint, string> voices = new();

    /// <summary>The per-install <see cref="GuideCache"/> file of a data folder and language (the game sets it:
    /// user://uigen/KEY/stringguide-LANG-XMLKEY-vN.bin); null = none, the table is parsed.</summary>
    public static Func<string, string, string> CacheOf = (_, _) => null;
    /// <summary>Where MS2_UICACHE=check reports a difference between the cache and the parse.</summary>
    public static Action<string> Log = _ => { };
    /// <summary>The tables built this run, per data folder and language (the client builds one per run).</summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet,
        System.Collections.Concurrent.ConcurrentDictionary<string, GuideStrings>> shared = new();

    /// <summary>The strings of <paramref name="packs"/> in <paramref name="language"/>, built on the first call.</summary>
    public static GuideStrings Shared(PackSet packs, string language) =>
        packs == null ? new() : shared.GetOrCreateValue(packs).GetOrAdd(language ?? "", l => Load(packs, l));

    /// <summary>
    /// The strings of <paramref name="packs"/> in <paramref name="language"/>: from this install's cache when it is there
    /// and passes its checks, else parsed. MS2_UICACHE=off always parses; MS2_UICACHE=check parses, reads the cache too
    /// and logs the first difference.
    /// </summary>
    public static GuideStrings Load(PackSet packs, string language)
    {
        string mode = Environment.GetEnvironmentVariable("MS2_UICACHE");
        string path = packs?.DataDir is { } dir ? CacheOf(dir, language) : null;
        if (path == null || mode == "off") return Parse(packs, language);
        if (mode == "check")
        {
            var parsed = Parse(packs, language);
            if (GuideCache.FirstStringsDifference(parsed, GuideCache.ReadStrings(path)) is { } diff)
                Log("guide strings cache: " + diff);
            return parsed;
        }
        return GuideCache.ReadStrings(path) ?? Parse(packs, language);
    }

    /// <summary>The table parsed from the XML (the cache's reference).</summary>
    public static GuideStrings Parse(PackSet packs, string language)
    {
        var s = new GuideStrings();
        if (packs == null) return s;
        byte[] xml = null;
        foreach (var path in new[] { $"String/{language}/stringGuide.xml", "String/stringGuide.xml" })
            if (packs.Exists("Xml", path))
            {
                try { xml = packs.Read("Xml", path); } catch { }
                break;
            }
        if (xml == null) return s;
        System.Xml.Linq.XDocument doc;
        try { doc = GameStrings.Parse(xml); }
        catch (System.Xml.XmlException) { return s; }
        var root = doc.Root;
        if (root == null || root.Name != "ms2") return s;
        var words = new ReservedWords(new GameStrings(packs, language), null);
        foreach (var k in root.Elements("key"))
        {
            if (!FeatureElements.IsAvailable(packs, k)) continue;
            int id = 0;
            GuideXml.UInt(k, "id", ref id);
            var name = (string)k.Attribute("name") ?? "";
            var voice = (string)k.Attribute("voiceID") ?? "";
            if (name.Length != 0)
            {
                name = name.Replace("\\n", "\n");
                if (name.Contains('$')) name = words.ReplaceObjectWords(name);
                s.texts[(uint)id] = name;                       // CAtlMap::SetAt: a later key of the id wins
            }
            if (voice.Length != 0) s.voices[(uint)id] = voice;
        }
        return s;
    }

    /// <summary>The text of an id (GetGuideText's lookup, before the value words), "" when it has none.</summary>
    public string Text(uint id) => texts.TryGetValue(id, out var t) ? t : "";

    /// <summary>GetGuideVoice: the voice file of an id, "" when it has none.</summary>
    public string Voice(uint id) => voices.TryGetValue(id, out var v) ? v : "";

    /// <summary>The text ids in id order (the cache's records).</summary>
    public IEnumerable<uint> Ids => texts.Keys.OrderBy(id => id);

    internal Dictionary<uint, string> TextMap => texts;
    internal Dictionary<uint, string> VoiceMap => voices;
}
