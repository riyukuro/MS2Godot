using System.Text;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
namespace Ms2Godot.Net;

/// <summary>
/// What the item data says about an item, for the packet readers: which extra blocks follow the item status in
/// CItem::ReadFromStream (item.cpp:2655) and which inventory tab the item sits in (CItemData::FUN_14053e530 itemdata.cpp, the
/// getter Unk_Inventory::FUN_140a617f0 uses; RemoveItemsInCategory acts on that tab).
/// The data: itemdata/{id/100000}.xml, item/environment/property@type, @skinType, environment/ucc@mesh; table/musicscore.xml isCustomNote.
/// </summary>
public sealed class ItemKinds
{
    readonly PackSet packs;
    readonly Dictionary<int, Dictionary<int, (int Type, bool Skin, bool Ucc)>> files = new();
    HashSet<int> customNotes;
    public ItemKinds(PackSet packs) { this.packs = packs; }

    static XDocument Doc(PackSet p, string entry) => XDocument.Parse(Encoding.UTF8.GetString(p.Read("Xml", entry)).TrimStart('﻿'));

    (int Type, bool Skin, bool Ucc)? Row(int id)
    {
        int prefix = id / 100000;
        if (!files.TryGetValue(prefix, out var rows))
        {
            rows = new();
            string entry = $"itemdata/{prefix}.xml";
            if (packs.Exists("Xml", entry))
                foreach (var e in Doc(packs, entry).Root.Elements("item"))
                {
                    if (!int.TryParse((string)e.Attribute("id"), out int i)) continue;
                    var prop = e.Descendants("property").FirstOrDefault();
                    int.TryParse((string)prop?.Attribute("type"), out int type);
                    int.TryParse((string)prop?.Attribute("skinType"), out int skin);
                    rows[i] = (type, skin != 0, !string.IsNullOrEmpty((string)e.Descendants("ucc").FirstOrDefault()?.Attribute("mesh")));
                }
            files[prefix] = rows;
        }
        return rows.TryGetValue(id, out var r) ? r : null;
    }

    bool CustomNote(int id)
    {
        if (customNotes == null)
        {
            customNotes = new();
            if (packs.Exists("Xml", "table/musicscore.xml"))
                foreach (var e in Doc(packs, "table/musicscore.xml").Descendants("MusicScore"))
                    if (((string)e.Attribute("isCustomNote"))?.ToLowerInvariant() is "true" or "1" && int.TryParse((string)e.Attribute("id"), out int i))
                        customNotes.Add(i);
        }
        return customNotes.Contains(id);
    }

    /// <summary>CItem::ReadFromStream: ugc when the item has a cube mesh or type 0x16, gem for type 7, a custom music score for type 0xc, pet for type 0xb. Null = item not in the data.</summary>
    public ItemExtra? ExtraOf(int id)
    {
        if (Row(id) is not { } r) return null;
        var x = ItemExtra.None;
        if (r.Ucc || r.Type == 0x16) x |= ItemExtra.Ugc;
        if (r.Type == 7) x |= ItemExtra.Gem;
        if (r.Type == 0xc && CustomNote(id)) x |= ItemExtra.Music;
        if (r.Type == 0xb) x |= ItemExtra.Pet;
        return x;
    }

    /// <summary>The inventory tab of an item (CItemData::FUN_14053e530); 7 when the item is unknown (Unk_Inventory::FUN_140a61870).</summary>
    public int TabOf(int id)
    {
        if (Row(id) is not { } r) return 7;
        switch (r.Type)
        {
            case 1: return r.Skin ? 1 : 0;
            case 6: case 8: case 0xc: case 0x17: return 4;
            case 5: case 9: return 2;
            case 0xd: case 0xe: return 6;
            case 3: return 5;
            case 0xf: case 0x13: return 3;
            case 0x10: return 8;
            case 0xb: return 9;
            case 2: return 10;
            case 0x14: return 0xb;
            case 7: return 0xc;
            case 0x15: return 0xd;
            case 0x1b: return 0xe;
        }
        return 7;
    }
}
