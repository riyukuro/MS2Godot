using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Quest;

/// <summary>
/// The parts of Table/FieldMetaData.xml (CFieldMetaTable::LoadFieldMetaData) the quest guide reads: which fields have portals
/// to which (fieldPortal / portal targetField, portalType 0 only, as HasPortalTarget) and which fields an npc stands in.
/// </summary>
public sealed class FieldMeta
{
    public const string Archive = "Xml";
    public const string Entry = "table/kr/fieldmetadata.xml";

    /// <summary>Every field that has a fieldPortal element, in file order (the nodes of the map graph).</summary>
    public readonly List<int> PortalFields = new();
    /// <summary>field -> target fields of its portalType 0 portals (HasPortalTarget).</summary>
    public readonly Dictionary<int, List<int>> PortalTargets = new();
    /// <summary>npc id -> fields it stands in.</summary>
    public readonly Dictionary<int, List<int>> NpcFields = new();

    public static FieldMeta Load(PackSet packs) =>
        Parse(System.Text.Encoding.UTF8.GetString(packs.Read(Archive, Entry)).TrimStart('﻿'));

    public static FieldMeta Parse(string xml)
    {
        var m = new FieldMeta();
        var root = XDocument.Parse(xml).Root;
        foreach (var npc in root.Elements("npc"))
        {
            int id = Int(npc, "id");
            foreach (var f in npc.Elements("field"))
            {
                if (!m.NpcFields.TryGetValue(id, out var l)) m.NpcFields[id] = l = new List<int>();
                l.Add(Int(f, "id"));
            }
        }
        foreach (var fp in root.Elements("fieldPortal"))
        {
            int field = Int(fp, "field");
            var targets = new List<int>();
            foreach (var p in fp.Elements("portal"))
                if (Int(p, "portalType") == 0) targets.Add(Int(p, "targetField"));
            m.PortalFields.Add(field);
            m.PortalTargets[field] = targets;
        }
        return m;
    }

    static int Int(XElement e, string a) =>
        int.TryParse((string)e.Attribute(a), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
