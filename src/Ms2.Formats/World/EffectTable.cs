using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Formats.World;

/// <summary>One effect entry of an effect xml (Xml pack effect/...): a nif model, or another kind (sound, particle file...) that is only recorded.</summary>
public sealed class EffectPart
{
    public string Type;                  // nif, sound, ...
    public string FilePath;              // for nif: path in Resource/Model/Effect
    public string SoundGroup, SoundEvent;   // for soundEvent: FEV group path and event name
    /// <summary>soundMusic effect type 3 attributes (CSoundMusicEffectParam::vfunc_1 @0x14178fdb0, soundmusiceffectparam.cpp:69-84).</summary>
    public int MusicCueId = -1, MusicParamId = -1, MusicPriority = 0x80;
    public float MusicParamValue, MusicVolume = -1f;
    public string MusicReverb = "";
    public float StartDelay, Duration;
    public float[] Translate = new float[3], Rotation = new float[3];
    public float Scale = 1f;
    /// <summary>loop: the effect stays past its duration (CEffectParam::vfunc_1, CEffect::vfunc_9).</summary>
    public bool Loop;
    /// <summary>
    /// attachInfo, as CNifEffectParam::vfunc_1 reads it (nifeffectparam.cpp:388-401): attachType (text, e.g. "Mesh"),
    /// attachToNode="true" (attach type 1), applyNodeTransform (false when absent) and parentNode.
    /// </summary>
    public string AttachType, ParentNode;
    public bool AttachToNode, ApplyNodeTransform;
    /// <summary>update translate / rotation / scale: the parts of the parent's world the effect follows (true when absent).</summary>
    /// <summary>The light a type="light" part makes (null for other parts).</summary>
    public EffectLightParam Light;
    public bool UpdateTranslate = true, UpdateRotation = true, UpdateScale = true;
    /// <summary>type="weather" fallType: the sky object WeatherEffect::vfunc_4 hands CEffectService::SetSkyObject
    /// (0 clear, 1 snow, 2 heavy snow, 3 rain, 4 heavy rain, 5 sandstorm, 6 cherry blossom, 7 leaf fall).</summary>
    public int FallType;
}

/// <summary>
/// The values of a type="light" effect part (CLightEffectParam::vfunc_1, lighteffectparam.cpp:192-250): the colours are 0..255
/// in the file (divided by 255; white when fewer than 3 components), dimmer 1, range 1000 and attenuation 1 / 0 / 0 when absent.
/// </summary>
public sealed class EffectLightParam
{
    public float[] Diffuse = { 1, 1, 1 }, Specular = { 1, 1, 1 };
    public float Dimmer = 1, Range = 1000, Constant = 1, Linear = 0, Quadratic = 0;
}

/// <summary>Effect definitions on demand from the Xml pack.</summary>
public sealed class EffectTable
{
    readonly PackSet packs;
    readonly Dictionary<string, List<EffectPart>> cache = new(StringComparer.OrdinalIgnoreCase);
    public EffectTable(PackSet packs) { this.packs = packs; }

    public List<EffectPart> Get(string xmlPath)
    {
        if (string.IsNullOrEmpty(xmlPath)) return null;
        if (cache.TryGetValue(xmlPath, out var l)) return l;
        string entry = "effect/" + xmlPath.Replace('\\', '/').ToLowerInvariant();
        List<EffectPart> parts = null;
        if (packs.Exists("Xml", entry))
        {
            try
            {
                var x = XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", entry)).TrimStart('﻿'));
                parts = new List<EffectPart>();
                foreach (var e in x.Descendants("effect"))
                {
                    var p = new EffectPart { Type = (string)e.Attribute("type"), FilePath = (string)e.Attribute("filePath"), SoundGroup = (string)e.Attribute("group"), SoundEvent = (string)e.Attribute("event"), StartDelay = F((string)e.Attribute("startDelay")), Duration = F((string)e.Attribute("duration")) };
                    if (string.Equals(p.Type, "soundMusic", StringComparison.OrdinalIgnoreCase))
                    {
                        p.MusicCueId = I((string)e.Attribute("cueID"), -1);
                        p.MusicParamId = I((string)e.Attribute("paramID"), -1);
                        p.MusicParamValue = F((string)e.Attribute("paramValue"), 0f);
                        p.MusicPriority = I((string)e.Attribute("priority"), 0x80);
                        p.MusicVolume = F((string)e.Attribute("volume"), -1f);
                        p.MusicReverb = (string)e.Attribute("reverb") ?? "";
                    }
                    var t = e.Element("transform");
                    if (t != null) { p.Translate = V((string)t.Attribute("translate")); p.Rotation = V((string)t.Attribute("rotation")); p.Scale = F((string)t.Attribute("scale"), 1f); }
                    p.Loop = B((string)e.Attribute("loop"), false);
                    p.FallType = (int)F((string)e.Attribute("fallType"));
                    if (e.Element("attachInfo") is { } a)
                    {
                        p.AttachType = (string)a.Attribute("attachType");
                        p.ParentNode = (string)a.Attribute("parentNode");
                        p.AttachToNode = B((string)a.Attribute("attachToNode"), false);
                        p.ApplyNodeTransform = B((string)a.Attribute("applyNodeTransform"), false);
                    }
                    if (e.Element("update") is { } u)
                    {
                        p.UpdateTranslate = B((string)u.Attribute("translate"), true);
                        p.UpdateRotation = B((string)u.Attribute("rotation"), true);
                        p.UpdateScale = B((string)u.Attribute("scale"), true);
                    }
                    if (p.Type == "light")
                    {
                        var lp = new EffectLightParam();
                        if (e.Element("light") is { } li)
                        {
                            lp.Diffuse = Color((string)li.Attribute("diffuse"));
                            lp.Specular = Color((string)li.Attribute("specular"));
                            lp.Dimmer = F((string)li.Attribute("dimmer"), 1f);
                            lp.Range = F((string)li.Attribute("range"), 1000f);
                        }
                        if (e.Element("attenuation") is { } at)
                        {
                            lp.Constant = F((string)at.Attribute("constant"), 1f);
                            lp.Linear = F((string)at.Attribute("linear"), 0f);
                            lp.Quadratic = F((string)at.Attribute("quadratic"), 0f);
                        }
                        p.Light = lp;
                    }
                    parts.Add(p);
                }
            }
            catch { parts = null; }
        }
        return cache[xmlPath] = parts;
    }

    static bool B(string s, bool def) => s == null ? def : s.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) || s.Trim() == "1";
    static float F(string s, float def = 0) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : def;
    /// <summary>"r, g, b" in 0..255 as 0..1; white when absent or short.</summary>
    static float[] Color(string s)
    {
        var p = s?.Split(',', StringSplitOptions.TrimEntries);
        if (p == null || p.Length < 3) return new[] { 1f, 1f, 1f };
        return new[] { F(p[0]) / 255f, F(p[1]) / 255f, F(p[2]) / 255f };
    }

    /// <summary>TinyXML QueryIntAttribute (sscanf "%d") semantics: accepts leading int prefix such as "1.0" -> 1.</summary>
    static int I(string s, int def)
    {
        if (string.IsNullOrEmpty(s)) return def;
        var span = s.AsSpan().TrimStart();
        if (span.Length == 0) return def;
        int end = (span[0] == '+' || span[0] == '-') ? 1 : 0;
        int digits = 0;
        while (end < span.Length && span[end] >= '0' && span[end] <= '9') { end++; digits++; }
        if (digits == 0) return def;
        return int.TryParse(span.Slice(0, end), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : def;
    }
    static float[] V(string s)
    {
        var r = new float[3];
        if (s == null) return r;
        var p = s.Split(',', StringSplitOptions.TrimEntries);
        for (int i = 0; i < 3 && i < p.Length; i++) r[i] = F(p[i]);
        return r;
    }
}
