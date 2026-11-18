using System.Xml.Linq;

namespace Ms2.Formats.Options;

/// <summary>table/gameoptionlocale.xml as CLocaleLanguage loads it (LoadGameOptionLocale localepath.cpp:223-235): one
/// row per &lt;gameoptionlocale&gt;, its language / region / voice codes split on ",".</summary>
public sealed class GameOptionLocale
{
    public sealed record Row(string Locale, string[] Languages, string[] Regions, string[] Voices);

    public readonly List<Row> Rows = new();

    public static GameOptionLocale Parse(XDocument doc)
    {
        var t = new GameOptionLocale();
        var root = doc.Root?.Name == "ms2" ? doc.Root : null;
        if (root == null) return t;
        static string[] Split(XElement e, string a) =>
            ((string)e.Attribute(a) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var e in root.Elements("gameoptionlocale"))
            t.Rows.Add(new Row((string)e.Attribute("locale") ?? "", Split(e, "language"), Split(e, "region"), Split(e, "voice")));
        return t;
    }

    /// <summary>GetLocaleData(0) @0x14064f070: the last row in debug mode, else the row at the region index; null past the
    /// end.</summary>
    public Row GetLocaleData(int region, bool debugMode)
    {
        int index = debugMode ? Rows.Count - 1 : region;
        return index < 0 || index >= Rows.Count ? null : Rows[index];
    }

    /// <summary>GetLanguageId @0x140651920 (localepath.cpp:445): language code to enum, 0 for an unknown code. The client
    /// compares "FR)" for FR, so FR maps to 0.</summary>
    public static int GetLanguageId(string code) => code.ToUpperInvariant() switch
    {
        "KR" => 0, "JP" => 1, "EN" => 2, "CN" => 3, "FR)" => 4, "DE" => 5, "PT" => 6, "ES" => 7, _ => 0,
    };

    /// <summary>FUN_140651b00 (localepath.cpp:459): region code to enum, 0 for an unknown code.</summary>
    public static int RegionId(string code) => code.ToUpperInvariant() switch
    {
        "KR" => 0, "JP" => 1, "NA" => 2, "CN" => 3, _ => 0,
    };

    /// <summary>FUN_140652030 (localepath.cpp:518): language enum to code, "" past the list.</summary>
    public static string LanguageCode(int n) => n switch
    {
        0 => "KR", 1 => "JP", 2 => "EN", 3 => "CN", 4 => "FR", 5 => "DE", 6 => "PT", 7 => "ES", _ => "",
    };

    /// <summary>GetRegionCode (localepath.cpp:535): region enum to code, "" past the list.</summary>
    public static string RegionCode(int n) => n switch { 0 => "KR", 1 => "JP", 2 => "NA", 3 => "CN", _ => "" };

    /// <summary>
    /// The string folder of a language: SetLanguage @0x1406507a0 (localepath.cpp:578-593) sets g_strLocaleDir2 to "jp/",
    /// "en/", "cn/", "fr/", "de/", "pt/", "es/" for 1-7 and "kr/" for anything else. The client builds string paths without
    /// a locale ("./Data/Xml/String/&lt;file&gt;", FUN_14071ae20 unit_14071a5b0_datapathbuilders.cpp) and the file system
    /// inserts this folder before the file name of any path under "./data/xml/string/" (GetLocalePath filesystem.cpp:930-944,
    /// FileExists :532-546), so string/&lt;folder&gt;/&lt;file&gt; in Xml.m2d. Returned without the slash.
    /// </summary>
    public static string StringDirectory(int language) => language switch
    {
        1 => "jp", 2 => "en", 3 => "cn", 4 => "fr", 5 => "de", 6 => "pt", 7 => "es", _ => "kr",
    };

    /// <summary>SetLanguageByCode @0x140650e70 (localepath.cpp:276-289): the code upper-cased against FUN_140652030's codes;
    /// an unknown code keeps <paramref name="current"/>.</summary>
    public static int LanguageByCode(string code, int current)
    {
        code = (code ?? "").ToUpperInvariant();
        for (int n = 0; n < 8; n++) if (code == LanguageCode(n)) return n;
        return current;
    }
}
