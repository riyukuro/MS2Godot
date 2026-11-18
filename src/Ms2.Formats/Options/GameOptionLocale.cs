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
}
