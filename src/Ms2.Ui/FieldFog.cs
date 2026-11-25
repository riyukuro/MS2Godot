using System.Globalization;
using System.Xml.Linq;

namespace Ms2.Ui;

/// <summary>
/// The field's fog as CFieldDataCli::LoadXBlock (@0x140a56690, fielddatacli.cpp:389-430) reads it from the mapxblock: the depth
/// fog (&lt;fog color near far&gt;) and the height fog (&lt;heightfog color upper lower percentage&gt;). The defaults are the
/// constructor's (fielddatacli.cpp:73-74); a missing height fog element sets the height fog's defaults again (:418-424). The
/// telescope's skip puts this fog back (CInteractTelescopeObject::OnCutSceneSkip, interactobject.cpp:1016-1022).
/// </summary>
public sealed class FieldFog
{
    /// <summary>m_bFog (+0x1fd): a &lt;fog&gt; element was there.</summary>
    public bool Fog;
    /// <summary>m_nFogColor (+0x200), 0xAARRGGBB.</summary>
    public uint FogColor;
    /// <summary>m_fFogNear (+0x204), m_fFogFar (+0x208).</summary>
    public float FogNear = 1000f, FogFar = 10000f;
    /// <summary>m_bHeightFog (+0x20c).</summary>
    public bool HeightFog;
    /// <summary>m_nHeightFogColor (+0x210), 0xAARRGGBB.</summary>
    public uint HeightFogColor;
    /// <summary>m_fHeightFogUpper (+0x214), m_fHeightFogLower (+0x218), m_fHeightFogPercentage (+0x21c).</summary>
    public float HeightFogUpper, HeightFogLower = -500f, HeightFogPercentage = 100f;

    /// <summary>LoadXBlock's fog part on the mapxblock document (its "ms2" root).</summary>
    public static FieldFog Parse(XDocument doc)
    {
        var f = new FieldFog();
        var root = doc?.Root;
        if (root == null || root.Name.LocalName != "ms2") return f;
        if (root.Element("fog") is { } fog)
        {
            f.Fog = true;
            if ((string)fog.Attribute("color") is { } c) f.FogColor = ParseColor(c);
            Attr(fog, "near", ref f.FogNear);
            Attr(fog, "far", ref f.FogFar);
        }
        if (root.Element("heightfog") is { } hf)
        {
            f.HeightFog = true;
            if ((string)hf.Attribute("color") is { } c) f.HeightFogColor = ParseColor(c);
            Attr(hf, "upper", ref f.HeightFogUpper);
            Attr(hf, "lower", ref f.HeightFogLower);
            Attr(hf, "percentage", ref f.HeightFogPercentage);
        }
        else
        {
            f.HeightFog = false;
            f.HeightFogColor = 0;
            f.HeightFogUpper = 0;
            f.HeightFogLower = -500f;
            f.HeightFogPercentage = 100f;
        }
        return f;
    }

    /// <summary>FUN_14028c0d0 (@0x14028c0d0, commonutil.cpp:314-324): "r,g,b" to 0xFFRRGGBB, each part's atoi masked to a byte;
    /// 0 unless the text splits into three parts (FUN_14028c3e0(text, parts, ",", 3)).
    /// STILL-OPEN: FUN_14028c3e0's body is not recovered; "three parts, else 0" is its caller's use of the count, and text with
    /// more commas is taken as failing. Every mapxblock colour has three parts.</summary>
    public static uint ParseColor(string text)
    {
        var parts = text.Split(',');
        if (parts.Length != 3) return 0;
        return 0xff000000u | ((uint)Guide.GuideXml.Atoi(parts[0]) & 0xff) << 16 | ((uint)Guide.GuideXml.Atoi(parts[1]) & 0xff) << 8
               | ((uint)Guide.GuideXml.Atoi(parts[2]) & 0xff);
    }

    /// <summary>The height range the height fog blend takes for SetFogEffect's upper and lower: the engine's CalculateHeightFog
    /// divides by upper - lower, so equal heights give a step at that height (above it no fog; at and below it the whole
    /// percentage, the 0/0 at the height saturating to 0). Here that is a 0.001 range above lower, which draws the same step
    /// without the division by zero. The data has one such telescope (KMS2 02020011_bf, heights 0 and 0); no mapxblock has one.
    /// STILL-OPEN: D3D9 leaves saturate of NaN to the hardware; 0 is D3D10's rule.</summary>
    public static (float Upper, float Lower) BlendRange(float upper, float lower) =>
        upper == lower ? (lower + 0.001f, lower) : (upper, lower);

    /// <summary>XmlGetAttr(element, name, float&amp;): the value when the attribute is there, else the one held.</summary>
    static void Attr(XElement e, string name, ref float v)
    {
        if ((string)e.Attribute(name) is { } s && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) v = f;
    }
}
