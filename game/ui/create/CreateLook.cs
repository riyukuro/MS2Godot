using System.Xml.Linq;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2Godot.Look;
using Ms2Godot.Net;

namespace Ms2Godot.Ui.Create;

/// <summary>
/// The look being created: gender, job, the item worn in each category and its palette row, and the skin row.
/// Categories follow UICharacterCreateDialog (GetItemTabAndCategory / FUN_1418e46c0): 0 hair (HR), 1 face (FA),
/// 2 makeup (FD), 3 skin (the skin colour item 10100001), 4 top (CL), 5 bottom (PA), 6 shoes (SH).
/// The items a category offers are CItemDefaultData::GetSlotItems: table/defaultitems.xml rows of job 0 and the job,
/// for the slot, whose itemdata genderLimit fits (FUN_1418e6950).
/// </summary>
public sealed class CreateLook
{
    public const int SkinItem = 0x9a1d21;              // the skin colour row of the skin category (10100001)
    public static readonly EquipSlot[] CategorySlot = { EquipSlot.HR, EquipSlot.FA, EquipSlot.FD, EquipSlot.SK, EquipSlot.CL, EquipSlot.PA, EquipSlot.SH };

    readonly PackSet packs;
    readonly ColorPalettes palettes;
    readonly XDocument defaults;
    readonly Dictionary<int, XElement> dataRoots = new(), modelRoots = new();
    readonly Dictionary<int, string> itemNames = new();

    public int Gender, JobCode;
    public readonly Dictionary<EquipSlot, int> Items = new();        // slot -> item id
    public readonly Dictionary<EquipSlot, int> Rows = new();         // slot -> palette row
    public int SkinRow;

    public CreateLook(PackSet packs, ColorPalettes palettes, string lang = "en")
    {
        this.packs = packs; this.palettes = palettes;
        defaults = Load("table/defaultitems.xml");
        var names = Load($"string/{lang}/itemname.xml");
        if (names != null)
            foreach (var k in names.Root.Elements("key"))
                if (int.TryParse((string)k.Attribute("id"), out int id)) itemNames.TryAdd(id, (string)k.Attribute("name"));
    }

    XDocument Load(string entry) => packs.Exists("Xml", entry)
        ? XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", entry)).TrimStart('﻿')) : null;

    XElement Find(Dictionary<int, XElement> cache, string dir, int id, string element)
    {
        int prefix = id / 100000;
        if (!cache.TryGetValue(prefix, out var root)) cache[prefix] = root = Load($"{dir}/{prefix}.xml")?.Root;
        return root?.Elements(element).FirstOrDefault(e => (string)e.Attribute("id") == id.ToString());
    }

    public string ItemName(int id) => itemNames.GetValueOrDefault(id, "");

    /// <summary>The item's slotIcon (itemdata property), a Resource/Image path.</summary>
    public string IconPath(int id) => (string)Find(dataRoots, "itemdata", id, "item")?.Descendants("property").FirstOrDefault()?.Attribute("slotIcon");

    public int GenderLimit(int id)
    {
        var e = Find(dataRoots, "itemdata", id, "item");
        var limit = e?.Descendants("limit").FirstOrDefault();
        return int.TryParse((string)limit?.Attribute("genderLimit"), out int g) ? g : 2;
    }

    /// <summary>The item's model id (a tool itemPreset replaces it, as for the female copies of the starter outfits).</summary>
    int ModelId(int id)
    {
        var data = Find(dataRoots, "itemdata", id, "item");
        return int.TryParse((string)data?.Descendants("tool").FirstOrDefault()?.Attribute("itemPreset"), out int pre) && pre != 0 ? pre : id;
    }

    /// <summary>The item model's customize block: colour palette and default row, or (0, 0) when the item takes no dye.</summary>
    public (int Palette, int DefaultRow) Palette(int id)
    {
        if (id == SkinItem) return (ColorPalettes.SkinPalette, 0);
        var cust = Find(modelRoots, "itemmodel", ModelId(id), "ItemModel")?.Element("customize");
        if (cust == null || (string)cust.Attribute("color") != "1" || !int.TryParse((string)cust.Attribute("colorPalette"), out int pal)) return (0, 0);
        int.TryParse((string)cust.Attribute("defaultColorIndex"), out int idx);
        return (pal, idx);
    }

    public IReadOnlyList<(Rgba c0, Rgba c1, Rgba c2)> PaletteRows(int palette) => palettes.Rows(palette);

    /// <summary>CItemDefaultData::GetSlotItems(job, gender, slot) filtered by gender.</summary>
    public List<int> SlotItems(EquipSlot slot)
    {
        var list = new List<int>();
        if (slot == EquipSlot.SK) { list.Add(SkinItem); return list; }
        if (defaults == null) return list;
        foreach (var job in new[] { 0, JobCode })
            foreach (var k in defaults.Root.Elements("key").Where(k => (string)k.Attribute("jobCode") == job.ToString()))
                foreach (var s in k.Elements("slot").Where(s => (string)s.Attribute("name") == slot.ToString()))
                    foreach (var it in s.Elements("item"))
                        if (int.TryParse((string)it.Attribute("id"), out int id) && !list.Contains(id))
                        {
                            int g = GenderLimit(id);
                            if (g == 2 || g == Gender) list.Add(id);
                        }
        return list;
    }

    /// <summary>CSceneStateMulti::FUN_14174ec40 for a fresh gender: the starter look (first fitting items, default rows).</summary>
    public void Reset(int gender, int jobCode)
    {
        Gender = gender; JobCode = jobCode;
        Items.Clear(); Rows.Clear(); SkinRow = 0;
        var (items, _) = StarterLook.Build(packs, palettes, gender, jobCode);
        foreach (var it in items) { Items[it.Slot] = it.Id; Rows[it.Slot] = it.Color.Index; }
    }

    /// <summary>OnRandom: a random item and row for every category.</summary>
    public void Randomize(Random rng)
    {
        foreach (var slot in new[] { EquipSlot.HR, EquipSlot.FA, EquipSlot.FD, EquipSlot.CL, EquipSlot.PA, EquipSlot.SH })
        {
            var list = SlotItems(slot);
            if (list.Count == 0) continue;
            int id = list[rng.Next(list.Count)];
            Items[slot] = id;
            var (pal, def) = Palette(id);
            int n = pal == 0 ? 0 : PaletteRows(pal).Count;
            Rows[slot] = n > 0 ? rng.Next(n) : def;
        }
        int skins = PaletteRows(ColorPalettes.SkinPalette).Count;
        SkinRow = skins > 0 ? rng.Next(skins) : 0;
    }

    public int RowOf(EquipSlot slot) => slot == EquipSlot.SK ? SkinRow : Rows.TryGetValue(slot, out int r) && r >= 0 ? r : Palette(Items.GetValueOrDefault(slot)).DefaultRow;

    public EquipColor ColorOf(EquipSlot slot)
    {
        if (!Items.TryGetValue(slot, out int id)) return default;
        var (pal, _) = Palette(id);
        return pal == 0 ? default : palettes.Equip(pal, RowOf(slot)) ?? default;
    }

    public SkinColor? Skin => palettes.Skin(SkinRow);

    /// <summary>The worn items as EquipApplier takes them (ears from the starter look as well).</summary>
    public List<EquipItem> Equips()
    {
        var list = new List<EquipItem>();
        foreach (var (slot, id) in Items) list.Add(new EquipItem { Id = id, Slot = slot, Color = ColorOf(slot) });
        return list;
    }

    /// <summary>
    /// The create request's item block (UICharacterCreateDialog::FUN_1418e3f50): the shown items, each with its id,
    /// equip slot and appearance (colour, then the slot's customize floats; created looks keep the item defaults, zero).
    /// </summary>
    public byte[] PacketItems() => CreateCharacter.EncodeItems(Equips().Where(e => e.Slot != EquipSlot.ER)
        .Select(e => new CreateItem(e.Id, e.Slot, e.Color, CreateItem.DefaultAppearance(e.Slot))));

    public CreateCharacter Packet(string name, int elapsedSeconds)
    {
        var skin = Skin ?? default;
        return new CreateCharacter((byte)Gender, (short)JobCode, name, Argb(skin.Primary), Argb(skin.Secondary), PacketItems(), elapsedSeconds);
        static int Argb(Rgba c) => c.A << 24 | c.R << 16 | c.G << 8 | c.B;
    }

    /// <summary>The preview body: the gender's body NIF with idle, dressed through EquipApplier with the skin colour.</summary>
    public (Node3D Visual, AnimationPlayer Anim) Build(ModelBuilder builder)
    {
        string dir = Gender == 0 ? "male" : "female";
        var md = builder.LoadModel($"{dir}/{(Gender == 0 ? "m_body.nif" : "f_body.nif")}");
        var visual = builder.Build(md, Gender == 0 ? "Male" : "Female");
        var anim = visual.GetNode<AnimationPlayer>("Model/AnimationPlayer");
        var lib = anim.GetAnimationLibrary("");
        lib.AddAnimation("idle", ModelBuilder.BuildAnimation(builder.LoadKf($"{dir}/idle_a.kf"), md));
        try
        {
            var equip = new EquipApplier(packs, builder) { Palettes = palettes };
            var skin = Skin;
            equip.Apply(visual, md, Gender, Equips(), skin?.Primary, skin?.Secondary);
        }
        catch (Exception ex) { GD.PushWarning("create look: " + ex.Message); }
        return (visual, anim);
    }
}
