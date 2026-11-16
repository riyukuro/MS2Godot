using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Quest;

public static class QuestStrings
{
    /// <summary>stringcommon.xml of a locale (en by default); the built-in English text when the entry is missing.</summary>
    public static TraceStrings Load(PackSet packs, string locale = "en")
    {
        var s = new TraceStrings();
        string entry = $"string/{locale}/stringcommon.xml";
        if (!packs.Exists("Xml", entry)) return s;
        var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", entry)).TrimStart('﻿'));
        foreach (var k in doc.Descendants("key"))
        {
            string v = (string)k.Attribute("value");
            switch ((string)k.Attribute("id"))
            {
                case "s_quest_navigation_dest": s.Dest = v; break;
                case "s_quest_navigation_distance_km": s.DistanceKm = v; break;
                case "s_quest_navigation_distance_m": s.DistanceM = v; break;
                case "s_quest_navigation_initializing": s.Initializing = v; break;
            }
        }
        return s;
    }
}
