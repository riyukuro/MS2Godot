using System.Text;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Formats.Text;
namespace Ms2Godot.Net;

/// <summary>
/// The first model slot of an item, which picks the appearance variant of an item body (hair HR, decal FD, cap CP;
/// CItemData::CreateExtraData itemdata.cpp:1430, ModelGroup types via GetFirstGroupType). CItemData loads the model of the item
/// named by its tool itemPreset, then itemmodel/{id/100000}.xml gives the slots; the first listed one is used; HR and FD pick the hair and decal layouts, a cap (type 1, subtype 0xd) the cap layout.
/// </summary>
public sealed class ItemModelSlots
{
    readonly PackSet packs;
    readonly Dictionary<int, Dictionary<int, (int Preset, bool Cap)>> presets = new();
    readonly Dictionary<int, Dictionary<int, EquipSlot>> models = new();
    readonly Ms2.Ui.Items.ItemDataTable perItem;
    public ItemModelSlots(PackSet packs) { this.packs = packs; perItem = new Ms2.Ui.Items.ItemDataTable(packs); }

    static XDocument Doc(PackSet p, string entry) => PackXml.Parse(p.Read("Xml", entry));

    public EquipSlot SlotOf(int id)
    {
        int prefix = id / 100000;
        if (!presets.TryGetValue(prefix, out var byItem))
        {
            byItem = new();
            string entry = $"itemdata/{prefix}.xml";
            if (packs.Exists("Xml", entry))
                foreach (var e in Doc(packs, entry).Root.Elements("item"))
                {
                    if (!int.TryParse((string)e.Attribute("id"), out int i) || Ms2.Ui.FeatureElements.SelectEnvironment(packs, e) is not { } env) continue;
                    int.TryParse((string)env.Element("tool")?.Attribute("itemPreset"), out int p);
                    var prop = env.Element("property");
                    int.TryParse((string)prop?.Attribute("type"), out int type);
                    int.TryParse((string)prop?.Attribute("subtype"), out int sub);
                    byItem[i] = (p, type == 1 && sub == 0xd);   // GetEquipSubtype is 0 unless the type is 1
                }
            presets[prefix] = byItem;
        }
        // no itemPreset, no model (itemdata.cpp:377); a cap is told by subtype 0xd, not by its slot (CreateExtraData itemdata.cpp:1430)
        // data with one file per item (GMS2 item/<a>/<bb>/<id>.xml): the same property and tool rows through the table's lazy index
        if (!byItem.TryGetValue(id, out var info) && !packs.Exists("Xml", $"itemdata/{prefix}.xml") && perItem.Get(id) is { } d) byItem[id] = info = (d.ItemPreset, d.Type == 1 && d.Subtype == 0xd);
        if (!byItem.TryGetValue(id, out info)) return EquipSlot.Unknown;
        // GMS2 item files name their model slot (<slots><slot name>). The client picks the decal appearance by its first model group type 3 (FD) and
        // the hair by type 1 (HR); a cap only by GetEquipSubtype() == 0xd, CP is never consulted (CreateExtraData itemdata.cpp:1430). Here CP stands in
        // for subtype 0xd: in the KMS data CP <-> subtype 13 one to one (2847 = 2847); the GMS2 subtypes seen are 0 and 16, so the name is what
        // tells a GMS2 cap. The FA face slot (group type 2) is no case of that switch, so leaving it unmapped is what the client does.
        // The name is matched exactly and case-sensitively (Parse_14021bdc0).
        if (!packs.Exists("Xml", $"itemdata/{prefix}.xml") && perItem.Get(id) is { } own && own.FirstSlot is "HR" or "FD" or "CP")
            return own.FirstSlot switch { "HR" => EquipSlot.HR, "FD" => EquipSlot.FD, _ => EquipSlot.CP };
        if (info.Preset == 0) return info.Cap ? EquipSlot.CP : EquipSlot.Unknown;
        int model = info.Preset;
        int mp = model / 100000;
        if (!models.TryGetValue(mp, out var slots))
        {
            slots = new();
            string entry = $"itemmodel/{mp}.xml";
            var presetDoc = packs.Exists("Xml", entry) ? Doc(packs, entry) : Ms2.Formats.World.PerFileTable.ItemModels(packs, mp);   // GMS2: itempreset/
            if (presetDoc != null)
                foreach (var e in presetDoc.Descendants("ItemModel"))
                    if (int.TryParse((string)e.Attribute("id"), out int i)
                        && Enum.TryParse<EquipSlot>((string)e.Element("slots")?.Element("slot")?.Attribute("name"), true, out var s))
                        slots[i] = s;
            models[mp] = slots;
        }
        var first = slots.TryGetValue(model, out var r) ? r : EquipSlot.Unknown;
        if (first is EquipSlot.HR or EquipSlot.FD) return first;   // model group types 1 (hair) and 3 (decal)
        return info.Cap ? EquipSlot.CP : EquipSlot.Unknown;
    }
}
