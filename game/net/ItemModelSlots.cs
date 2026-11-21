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
    public ItemModelSlots(PackSet packs) { this.packs = packs; }

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
        if (!byItem.TryGetValue(id, out var info)) return EquipSlot.Unknown;
        if (info.Preset == 0) return info.Cap ? EquipSlot.CP : EquipSlot.Unknown;
        int model = info.Preset;
        int mp = model / 100000;
        if (!models.TryGetValue(mp, out var slots))
        {
            slots = new();
            string entry = $"itemmodel/{mp}.xml";
            if (packs.Exists("Xml", entry))
                foreach (var e in Doc(packs, entry).Descendants("ItemModel"))
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
