using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Formats.Text;

namespace Ms2Godot.Look;

/// <summary>
/// Colour palettes from Xml.m2d table/colorpalette.xml: &lt;colorPalette id&gt; holds &lt;color ch0 ch1 ch2&gt; rows (ARGB hex),
/// looked up by row index. Item dyes use ch0..ch2 as the three override colours; skin colours use ch0 and ch2.
/// </summary>
public sealed class ColorPalettes
{
    public const int SkinPalette = 10000;      // the palette the client lists skin colours from

    readonly Dictionary<int, List<(Rgba c0, Rgba c1, Rgba c2)>> rows = new();
    readonly Dictionary<int, List<Rgba>> visible = new();

    public ColorPalettes(PackSet packs)
    {
        if (!packs.Exists("Xml", "table/colorpalette.xml")) return;
        var doc = PackXml.Parse(packs.Read("Xml", "table/colorpalette.xml"));
        foreach (var p in doc.Root.Elements("colorPalette"))
        {
            if (!int.TryParse((string)p.Attribute("id"), out int id)) continue;
            rows[id] = p.Elements("color").Select(c => (Hex((string)c.Attribute("ch0")), Hex((string)c.Attribute("ch1")), Hex((string)c.Attribute("ch2")))).ToList();
            // ItemColorEx m_n38 (CItemDataTable palette load): the palette attribute, ch0 without one
            visible[id] = p.Elements("color").Select(c => string.IsNullOrEmpty((string)c.Attribute("palette"))
                ? Hex((string)c.Attribute("ch0")) : Hex((string)c.Attribute("palette"))).ToList();
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

    /// <summary>The colour a palette row shows in the UI (ItemColorEx m_n38, the visibleColor of buildColors).</summary>
    public Rgba Visible(int palette, int index) =>
        visible.TryGetValue(palette, out var list) && index >= 0 && index < list.Count ? list[index] : default;

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
        var doc = PackXml.Parse(packs.Read("Xml", "table/defaultitems.xml"));
        var genders = ItemFiles(packs, "itemdata");      // itemdata file prefix -> root
        var models = ItemFiles(packs, "itemmodel");

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
        // the job's starting weapons: table/job.xml startItems, placed by category as TCreateCharInfo::FUN_14107b720
        // (@0x14107b720, unit_14107ac30_tcreatecharinfo.cpp:365-431) and CEquipment::FUN_1411e6140 (@0x1411e6140,
        // equipment.cpp:964-1035) assign them: RH and two-handed BH go to RH, LH to LH, and dual-wield OH (Thief
        // daggers, Assassin stars) fills RH first and then LH
        if (packs.Exists("Xml", "table/job.xml"))
        {
            var jobs = PackXml.Parse(packs.Read("Xml", "table/job.xml"));
            var job = jobs.Root.Descendants("job").FirstOrDefault(j => (string)j.Attribute("jobGroupID") == jobCode.ToString());
            foreach (var part in ((string)job?.Attribute("startItems") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(part, out int id)) continue;
                var category = (string)Find(packs, genders, "itemdata", id, "item")?.Descendants("property").FirstOrDefault()?.Attribute("category");
                EquipSlot slot;
                if (category is "RH" or "BH") slot = EquipSlot.RH;
                else if (category == "LH") slot = EquipSlot.LH;
                else if (category == "OH") slot = items.All(i => i.Slot != EquipSlot.RH) ? EquipSlot.RH : EquipSlot.LH;
                else if (!Enum.TryParse(category, out slot)) continue;
                if (items.Any(i => i.Slot == slot)) continue;
                items.Add(new EquipItem { Id = id, Slot = slot, Color = ColorOf(id, slot.ToString()) });
            }
        }
        return (items, palettes.Skin(skinRow));
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, Dictionary<string, Dictionary<int, XElement>>> files = new();

    /// <summary>The parsed {dir}/{prefix}.xml roots of a pack set, kept across calls: a pack entry never changes and callers only read the trees.</summary>
    static Dictionary<int, XElement> ItemFiles(PackSet packs, string dir)
    {
        var all = files.GetValue(packs, _ => new());
        lock (all) { if (!all.TryGetValue(dir, out var d)) all[dir] = d = new(); return d; }
    }

    /// <summary>An element with the given id in Xml.m2d {dir}/{id / 100000}.xml (itemdata and itemmodel are split that way).</summary>
    static XElement Find(PackSet packs, Dictionary<int, XElement> cache, string dir, int id, string element)
    {
        int prefix = id / 100000;
        XElement root;
        lock (cache)
        if (!cache.TryGetValue(prefix, out root))
        {
            string entry = $"{dir}/{prefix}.xml";
            root = packs.Exists("Xml", entry)
                ? PackXml.Parse(packs.Read("Xml", entry)).Root
                : null;
            cache[prefix] = root;
        }
        return root?.Elements(element).FirstOrDefault(e => (string)e.Attribute("id") == id.ToString());
    }
}
