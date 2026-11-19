using System.Xml.Linq;

namespace Ms2.Formats.Options;

/// <summary>
/// COptimizer::LoadOptimization (optimizer.cpp:373-427), run once at startup (ms2.cpp:1769): the optimizer's switches come
/// from "Optimization.xml" in the option folder (JoinPath(GetOptionFolder(), ...), optimizer.cpp:381; GetOptionFolder
/// "../../Config/" @0x1412755b0, the folder of GameOption.xml, gameoption.cpp:1700) when that file loads, else
/// every switch is the graphic state's m_b10 (GetGraphicState = CGameOption+0x9c, ms2clientplatformbase.cpp:625, an
/// Unk_GraphicState2). m_b10 is set to 1 by that block's reset (gameoptionstruct.cpp:2314) and read from no GameOption.xml
/// attribute (LoadGameOptionOptimizer, gameoptionstruct.cpp:2319-2335), so without the file every switch is on.
/// </summary>
public static class Optimization
{
    public const string FileName = "Optimization.xml";

    /// <summary>
    /// ProgressiveLoading (DAT_143ae2b40, COptimizer FUN_1409f7ea0): the root's first "ProgressiveLoading" child's int
    /// "value" (XmlGetAttr, QueryIntAttribute, tinyxmlhelper.cpp:151, 242) when the file loads; the switch starts at 0 (a .bss int), so a loaded file
    /// without it gives off. No file or one that does not parse: on.
    /// </summary>
    public static bool ProgressiveLoading(string optionFolder)
    {
        var path = Path.Combine(optionFolder ?? "", FileName);
        XDocument doc;
        try
        {
            if (!File.Exists(path)) return true;
            // TiXmlDocument::LoadFile(TIXML_ENCODING_UNKNOWN, optimizer.cpp:380) takes the bytes as they are, whatever
            // encoding the declaration names, so the declaration is not followed here either.
            doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path)).TrimStart('\uFEFF'));
        }
        catch (Exception) { return true; }
        int value = 0;
        var node = doc.Root?.Element("ProgressiveLoading");
        if (node?.Attribute("value") is { } attr && LeadingInt(attr.Value) is { } v) value = v;
        return value != 0;
    }

    /// <summary>TiXmlAttribute::QueryIntValue's sscanf "%d": the leading signed digits; null when there are none.</summary>
    static int? LeadingInt(string s)
    {
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        int start = i;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
        int digits = i;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
        if (i == digits) return null;
        return int.TryParse(s.AsSpan(start, i - start), out var v) ? v : null;
    }
}
