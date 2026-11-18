using System.Text;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
namespace Ms2Godot.Net;

/// <summary>
/// The first model slot of an item, which picks the appearance variant of an item body (hair HR, decal FD, cap CP;
/// CItemData::CreateExtraData itemdata.cpp:1430, ModelGroup types via GetFirstGroupType). CItemData loads the model of the item
/// named by its tool itemPreset, then itemmodel/{id/100000}.xml gives the slots; the first listed one is used.
/// </summary>
public sealed class ItemModelSlots
{
    readonly PackSet packs;
    readonly Dictionary<int, Dictionary<int, int>> presets = new();
    readonly Dictionary<int, Dictionary<int, EquipSlot>> models = new();
    public ItemModelSlots(PackSet packs) { this.packs = packs; }

    static XDocument Doc(PackSet p, string entry) => XDocument.Parse(Encoding.UTF8.GetString(p.Read("Xml", entry)).TrimStart('﻿'));

    public EquipSlot SlotOf(int id)
    {
        int prefix = id / 100000;
        if (!presets.TryGetValue(prefix, out var byItem))
        {
            byItem = new();
            string entry = $"itemdata/{prefix}.xml";
            if (packs.Exists("Xml", entry))
                foreach (var e in Doc(packs, entry).Root.Elements("item"))
                    if (int.TryParse((string)e.Attribute("id"), out int i) && int.TryParse((string)e.Descendants("tool").FirstOrDefault()?.Attribute("itemPreset"), out int p) && p != 0)
                        byItem[i] = p;
            presets[prefix] = byItem;
        }
        int model = byItem.TryGetValue(id, out int preset) ? preset : id;
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
        return slots.TryGetValue(model, out var r) ? r : EquipSlot.Unknown;
    }
}
