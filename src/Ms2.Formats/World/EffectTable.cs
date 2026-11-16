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
    public float StartDelay, Duration;
    public float[] Translate = new float[3], Rotation = new float[3];
    public float Scale = 1f;
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
                    var t = e.Element("transform");
                    if (t != null) { p.Translate = V((string)t.Attribute("translate")); p.Rotation = V((string)t.Attribute("rotation")); p.Scale = F((string)t.Attribute("scale"), 1f); }
                    parts.Add(p);
                }
            }
            catch { parts = null; }
        }
        return cache[xmlPath] = parts;
    }

    static float F(string s, float def = 0) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : def;
    static float[] V(string s)
    {
        var r = new float[3];
        if (s == null) return r;
        var p = s.Split(',', StringSplitOptions.TrimEntries);
        for (int i = 0; i < 3 && i < p.Length; i++) r[i] = F(p[i]);
        return r;
    }
}
