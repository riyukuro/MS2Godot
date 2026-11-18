using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui.Hud;

/// <summary>
/// CServerStringGroup (unit_14087e8c0_serverstringtables.cpp; placeholder/cserverstring.h): one server string file of
/// CServerString as key -> text, a map of the file and one the game events fill at run time. OnNotice reads the trigger
/// group (CServerString m_trigger, stringTrigger.xml, through FUN_140a87690).
/// </summary>
public sealed class ServerStringGroup
{
    readonly Dictionary<string, string> file = new(StringComparer.Ordinal);       // m_38
    readonly Dictionary<string, string> runtime = new(StringComparer.Ordinal);    // m_80

    public static readonly ServerStringGroup Empty = new();

    /// <summary>The file's keys, for the tests.</summary>
    public int Count => file.Count;

    /// <summary>
    /// Load (@0x14087ea50): each &lt;key&gt; of &lt;ms2&gt; that IsElementAvailable passes, its "id" -> its "name" with
    /// "\\n" made a line break and ReplaceObjectWords applied; a repeated id is logged and replaces the earlier one.
    /// GetXmlPath("String", file) (FUN_14071ae20): string/&lt;language&gt;/file, the first of <paramref name="languages"/>
    /// that has it.
    /// </summary>
    public static ServerStringGroup Load(PackSet packs, IEnumerable<string> languages, string fileName,
        Func<XElement, bool> available, Func<string, string> replaceObjectWords)
    {
        var g = new ServerStringGroup();
        if (packs == null) return g;
        foreach (var lang in languages)
        {
            string path = $"string/{lang}/{fileName}";
            if (!packs.Exists("Xml", path)) continue;
            XDocument doc;
            try { doc = GameStrings.Parse(packs.Read("Xml", path)); } catch { return g; }   // LoadXmlFile failed
            if (doc.Root?.Name != "ms2") return g;
            foreach (var key in doc.Root.Elements("key"))
            {
                if (!available(key)) continue;
                string name = ((string)key.Attribute("name") ?? "").Replace("\\n", "\n");
                g.file[(string)key.Attribute("id") ?? ""] = replaceObjectWords(name);
            }
            break;
        }
        return g;
    }

    /// <summary>
    /// GetString (@0x14087ede0): a "$key$" text is the run time map's text of the key (every "$" removed), else the file's,
    /// with ReplaceValueWords (<paramref name="replaceValueWords"/>: CReservedWordTable with the player's name and gender);
    /// a key in neither gives "NO KEY : $key$". Any other text is returned as it is.
    /// </summary>
    public string GetString(string key, Func<string, string> replaceValueWords = null)
    {
        key ??= "";
        if (!(key.StartsWith('$') && key.EndsWith('$'))) return key;
        string name = key.Replace("$", "");
        if (runtime.TryGetValue(name, out var t) || file.TryGetValue(name, out t)) return replaceValueWords == null ? t : replaceValueWords(t);
        return "NO KEY : " + key;
    }

    /// <summary>SetString (@0x14087f0f0): into the run time map.</summary>
    public void SetString(string key, string text) => runtime[key] = text;

    /// <summary>RemoveString (@0x14087f140).</summary>
    public bool RemoveString(string key) => runtime.Remove(key);

    /// <summary>
    /// GetLocalizedText (@0x14083d340, unit_14083bab0_localizedtexthelpers.cpp:333): a text starting "&lt;ms2&gt;" is read as
    /// XML; the root's first child element gives its attribute named for the client language (DAT_143ab0250: "KR", "JP",
    /// "EN", "CN", "FR", "DE", "PT", "ES"), else the first non-empty one of the region's fallback languages
    /// (GetFallbackLanguages @0x1406514e0: region 0 KR, 1 JP, 2 EN DE PT, 3 CN). Anything else, or XML that does not
    /// parse or has no such element, is returned unchanged.
    /// </summary>
    public static string LocalizedText(string text, string language, int region)
    {
        if (text == null || !text.StartsWith("<ms2>", StringComparison.Ordinal)) return text;
        XElement elem;
        try { elem = XDocument.Parse(text).Root?.Elements().FirstOrDefault(); } catch { return text; }
        if (elem == null) return text;
        string s = (string)elem.Attribute(language ?? "") ?? "";
        if (s.Length > 0) return s;
        string[] fallbacks = region switch
        {
            0 => new[] { "KR" }, 1 => new[] { "JP" }, 2 => new[] { "EN", "DE", "PT" }, 3 => new[] { "CN" }, _ => Array.Empty<string>(),
        };
        foreach (var l in fallbacks)
        {
            s = (string)elem.Attribute(l) ?? "";
            if (s.Length > 0) return s;
        }
        return text;
    }
}
