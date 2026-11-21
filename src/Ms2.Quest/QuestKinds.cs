using System.Text;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
namespace Ms2.Quest;

/// <summary>
/// The quest table columns the quest packets need, read as CQuestData::Load (0x1407495d0) does: basic questType and repeatable,
/// require groupID (which the client keeps only when basic locking is 0). A quest already read keeps its first definition.
/// </summary>
public static class QuestKinds
{
    public static Dictionary<int, (int Type, int Repeatable, int GroupId)> Load(PackSet packs)
    {
        var rows = new Dictionary<int, (int, int, int)>();
        foreach (var entry in packs.List("Xml").Where(n => n.StartsWith("quest/", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            XDocument doc;
            try { doc = PackXml.Parse(packs.Read("Xml", entry)); } catch (System.Xml.XmlException) { continue; }
            foreach (var q in doc.Descendants("quest"))
            {
                var basic = q.Element("basic");
                int id = FieldMissions.Int((string)basic?.Attribute("questID"));
                if (id == 0 || rows.ContainsKey(id)) continue;
                int group = FieldMissions.Int((string)q.Element("require")?.Attribute("groupID"));
                if ((string)basic.Attribute("locking") is "1" or "true" or "True") group = 0;
                rows[id] = (FieldMissions.Int((string)basic.Attribute("questType")), FieldMissions.Int((string)basic.Attribute("repeatable")), group);
            }
        }
        return rows;
    }
}
