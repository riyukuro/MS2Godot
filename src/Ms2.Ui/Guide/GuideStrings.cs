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

    /// <summary>The tables built this run, per data folder and language (the client builds one per run).</summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet,
        System.Collections.Concurrent.ConcurrentDictionary<string, GuideStrings>> shared = new();

    /// <summary>The strings of <paramref name="packs"/> in <paramref name="language"/>, built on the first call.</summary>
    public static GuideStrings Shared(PackSet packs, string language) =>
        packs == null ? new() : shared.GetOrCreateValue(packs).GetOrAdd(language ?? "", l => Load(packs, l));

    const string CachePiece = "guidestrings";

    /// <summary>The cache configuration: the language's folder (the object words of the load are its tables') and the
    /// feature levels the key rows are picked by.</summary>
    static string CacheConfig(PackSet packs, string language) => $"{language}|{LobbyFeatures.Stamp(packs)}";

    /// <summary>The code the cached strings' form depends on: the guide's, the string tables' and the XML readers'.</summary>
    static string CacheBuild => Ms2.Formats.Cache.CacheStore.Build(typeof(GuideStrings).Assembly, typeof(GameStrings).Assembly, typeof(Ms2.Quest.QuestTable).Assembly);

    /// <summary>
    /// The strings of <paramref name="packs"/> in <paramref name="language"/>: from this install's cache store when it
    /// holds them, else parsed (and the file written for the next session). MS2_UICACHE=off parses; =check reads the
    /// cache too and reports the first difference. The setup step builds the file (<see cref="BuildCache"/>).
    /// </summary>
    public static GuideStrings Load(PackSet packs, string language)
    {
        var store = Ms2.Formats.Cache.CacheStore.Of?.Invoke(packs);
        if (store == null) return Parse(packs, language);
        var s = store.Get(CachePiece, CacheConfig(packs, language), CacheBuild, () => Parse(packs, language),
            GuideCache.WriteStringsBody, GuideCache.ReadStringsBody, GuideCache.FirstStringsDifference, out var outcome);
        Ms2.Formats.Cache.CacheStore.Logged?.Invoke(outcome);
        return s;
    }

    /// <summary>Writes the cache file of the strings when it is missing (the setup step); true when it was written.</summary>
    public static bool BuildCache(PackSet packs, string language, Ms2.Formats.Cache.CacheStore cache) =>
        cache.Ensure(CachePiece, CacheConfig(packs, language), CacheBuild, () => Parse(packs, language), GuideCache.WriteStringsBody);

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
