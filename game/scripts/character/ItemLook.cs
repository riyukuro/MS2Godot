using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;

namespace Ms2Godot.Look;

/// <summary>
/// Colour palettes from Xml.m2d table/colorpalette.xml: &lt;colorPalette id&gt; holds &lt;color ch0 ch1 ch2&gt; rows (ARGB hex),
/// looked up by row index. Item dyes use ch0..ch2 as the three override colours; skin colours use ch0 and ch2.
/// </summary>
public sealed class ColorPalettes
{
    public const int SkinPalette = 10000;      // the palette the client lists skin colours from

    readonly Dictionary<int, List<(Rgba c0, Rgba c1, Rgba c2)>> rows = new();

    public ColorPalettes(PackSet packs)
    {
        if (!packs.Exists("Xml", "table/colorpalette.xml")) return;
        var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/colorpalette.xml")).TrimStart('﻿'));
        foreach (var p in doc.Root.Elements("colorPalette"))
        {
            if (!int.TryParse((string)p.Attribute("id"), out int id)) continue;
            rows[id] = p.Elements("color").Select(c => (Hex((string)c.Attribute("ch0")), Hex((string)c.Attribute("ch1")), Hex((string)c.Attribute("ch2")))).ToList();
        }
    }

    static Rgba Hex(string s)
    {
        if (string.IsNullOrEmpty(s)) return default;
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        uint v = uint.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Rgba((byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24));
    }

    public (Rgba c0, Rgba c1, Rgba c2)? Get(int palette, int index)
    {
        if (!rows.TryGetValue(palette, out var list) || list.Count == 0) return null;
        return list[index >= 0 && index < list.Count ? index : 0];
    }

    /// <summary>The rows of a palette (empty when unknown).</summary>
    public IReadOnlyList<(Rgba c0, Rgba c1, Rgba c2)> Rows(int palette) => rows.TryGetValue(palette, out var list) ? list : Array.Empty<(Rgba, Rgba, Rgba)>();

    public EquipColor? Equip(int palette, int index) =>
        Get(palette, index) is { } c ? new EquipColor(c.c0, c.c1, c.c2, index, palette) : null;

    /// <summary>The skin colour pair at a row of the skin palette (ch0 lit, ch2 shade).</summary>
    public SkinColor? Skin(int index) => Get(SkinPalette, index) is { } c ? new SkinColor(c.c0, c.c2) : null;
}

/// <summary>
/// A new character's look from the player's own Xml pack, as character creation offers it: table/defaultitems.xml lists,
/// per job code (0 = every job), the items of each slot; an item fits a gender by its itemdata limit genderLimit
/// (0 male, 1 female, 2 both). The first fitting face, hair and ears of job 0 and the first fitting job outfit
/// (CL, PA, SH; job 0 when the job lists none) are picked, plus the job's start weapons (table/job.xml startItems). Colours: each item's itemmodel customize defaultColorIndex
/// in its colorPalette (row 0 without one) unless colorRows names a row for the slot; skin: skinRow of the skin palette.
/// </summary>
public static class StarterLook
{
    static readonly string[] Slots = { "HR", "FA", "ER", "CL", "PA", "SH" };

    public static (List<EquipItem> items, SkinColor? skin) Build(PackSet packs, ColorPalettes palettes, int gender, int jobCode,
        IReadOnlyDictionary<string, int> colorRows = null, int skinRow = 0)
    {
        var items = new List<EquipItem>();
        if (!packs.Exists("Xml", "table/defaultitems.xml")) return (items, palettes.Skin(0));
        var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/defaultitems.xml")).TrimStart('﻿'));
        var genders = new Dictionary<int, XElement>();      // itemdata file prefix -> root
        var models = new Dictionary<int, XElement>();

        int GenderOf(int id)
        {
            var e = Find(packs, genders, "itemdata", id, "item");
            var limit = e?.Element("environment")?.Element("limit") ?? e?.Descendants("limit").FirstOrDefault();
            return int.TryParse((string)limit?.Attribute("genderLimit"), out int g) ? g : 2;
        }

        EquipColor ColorOf(int id, string slot)
        {
            var data = Find(packs, genders, "itemdata", id, "item");
            int modelId = int.TryParse((string)data?.Descendants("tool").FirstOrDefault()?.Attribute("itemPreset"), out int pre) && pre != 0 ? pre : id;
            var cust = Find(packs, models, "itemmodel", modelId, "ItemModel")?.Element("customize");
            if (cust == null || (string)cust.Attribute("color") != "1" || !int.TryParse((string)cust.Attribute("colorPalette"), out int pal)) return default;
            int.TryParse((string)cust.Attribute("defaultColorIndex"), out int idx);
            if (colorRows != null && colorRows.TryGetValue(slot, out int row)) idx = row;
            return palettes.Equip(pal, idx) ?? default;
        }

        foreach (var slot in Slots)
        {
            IEnumerable<XElement> Candidates(int job) => doc.Root.Elements("key")
                .Where(k => (string)k.Attribute("jobCode") == job.ToString())
                .SelectMany(k => k.Elements("slot")).Where(s => (string)s.Attribute("name") == slot)
                .SelectMany(s => s.Elements("item")).Where(i => (string)i.Attribute("visible") != "0");
            var list = slot is "CL" or "PA" or "SH" ? Candidates(jobCode).Concat(Candidates(0)) : Candidates(0);
            if (slot == "ER")    // ears are a base item (visible="0") shared by both genders
                list = doc.Root.Elements("key").Where(k => (string)k.Attribute("jobCode") == "0")
                    .SelectMany(k => k.Elements("slot")).Where(s => (string)s.Attribute("name") == slot).SelectMany(s => s.Elements("item"));
            foreach (var it in list)
            {
                if (!int.TryParse((string)it.Attribute("id"), out int id)) continue;
                int g = GenderOf(id);
                if (g != 2 && g != gender) continue;
                items.Add(new EquipItem { Id = id, Slot = Enum.Parse<EquipSlot>(slot), Color = ColorOf(id, slot) });
                break;
            }
        }
        // the job's starting weapons: table/job.xml startItems, each in the slot its itemdata category names (RH, LH)
        if (packs.Exists("Xml", "table/job.xml"))
        {
            var jobs = XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/job.xml")).TrimStart('\uFEFF'));
            var job = jobs.Root.Descendants("job").FirstOrDefault(j => (string)j.Attribute("jobGroupID") == jobCode.ToString());
            foreach (var part in ((string)job?.Attribute("startItems") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(part, out int id)) continue;
                var category = (string)Find(packs, genders, "itemdata", id, "item")?.Descendants("property").FirstOrDefault()?.Attribute("category");
                if (!Enum.TryParse<EquipSlot>(category, out var slot) || items.Any(i => i.Slot == slot)) continue;
                items.Add(new EquipItem { Id = id, Slot = slot, Color = ColorOf(id, category) });
            }
        }
        return (items, palettes.Skin(skinRow));
    }

    /// <summary>An element with the given id in Xml.m2d {dir}/{id / 100000}.xml (itemdata and itemmodel are split that way).</summary>
    static XElement Find(PackSet packs, Dictionary<int, XElement> cache, string dir, int id, string element)
    {
        int prefix = id / 100000;
        if (!cache.TryGetValue(prefix, out var root))
        {
            string entry = $"{dir}/{prefix}.xml";
            root = packs.Exists("Xml", entry)
                ? XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", entry)).TrimStart('﻿')).Root
                : null;
            cache[prefix] = root;
        }
        return root?.Elements(element).FirstOrDefault(e => (string)e.Attribute("id") == id.ToString());
    }
}
