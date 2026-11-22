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

    /// <summary>GetLanguageFromLangId @0x1406518d0 (localepath.cpp:433): the NA region's pick from the Windows UI language id.
    /// 0x407 (German, Germany) is DE, 0x416 (Portuguese, Brazil) is PT, any other is EN.</summary>
    public static int LanguageFromLangId(int langId) => (langId & 0xffff) switch { 0x407 => 5, 0x416 => 6, _ => 2 };

    /// <summary>The Windows language id of a POSIX locale name such as "de_DE" or "pt_BR" (0 when it has none of the two the
    /// client tells apart: the plain "de" and "pt" are other ids, 0x0c07 and 0x816 among them, and fall to EN as there).</summary>
    public static int WindowsLangId(string posixLocale) => (posixLocale ?? "").Replace('-', '_').Split('.')[0] switch
    {
        "de_DE" => 0x407, "pt_BR" => 0x416, _ => 0,
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

    /// <summary>The language the strings are read in: <paramref name="language"/> when the data has its string folder, else the
    /// region's own language (a fresh file's pick, Unk_LgState reset gameoptionstruct.cpp:2466-2490). A saved index the data has
    /// no strings for (a KMS2 run's GameOption.xml on the GMS2 data) would otherwise leave every UI text unresolved.</summary>
    public static int LanguageWithStrings(int language, int region, Func<int, bool> hasStrings) => hasStrings(language) ? language : region;

    /// <summary>SetLanguageByCode @0x140650e70 (localepath.cpp:276-289): the code upper-cased against FUN_140652030's codes;
    /// an unknown code keeps <paramref name="current"/>.</summary>
    public static int LanguageByCode(string code, int current)
    {
        code = (code ?? "").ToUpperInvariant();
        for (int n = 0; n < 8; n++) if (code == LanguageCode(n)) return n;
        return current;
    }
}
