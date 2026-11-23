using System.Xml.Linq;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2Godot.Look;
using Ms2Godot.Net;
using Ms2.Formats.Text;

namespace Ms2Godot.Ui.Create;

/// <summary>
/// The lobby actor being created (the preview PC of CSceneStateMulti create mode): gender, job, the item worn in each
/// equip slot with its palette row and customize values, and the skin row.
/// Items come from CItemDefaultData (table/defaultitems.xml): per job code (0 = every job) and gender, the items of each
/// slot, in file order (common lists first). A row counts when it is visible or a base item of group type 0x12; an item
/// whose genderLimit is 2 goes to both genders' lists; an overall (subtype 22) in the top list (CL) is in the bottom list
/// (PA) as well (TDefaultItems::AddSlotItem). The skin is the avatar colour item 10100001 and its palette.
/// </summary>
public sealed class CreateLook
{
    public const int SkinItem = 0x9a1d21;              // CItemData::IsAvatarColorItem (10100001)

    /// <summary>
    /// The item whose palette row is the skin colour: the avatar colour item where the data has it (KMS), else the gender's
    /// skin base item (GMS2 defaultitems slot SK: 1 male, 2 female). 0 when the data has neither.
    /// </summary>
    public int SkinOwner => Data(SkinItem) != null || Model(SkinItem) != null ? SkinItem : baseItems[Gender is 1 ? 1 : 0].GetValueOrDefault(EquipSlot.SK);
    const int OverallSubtype = 0x16;                   // CItemData::FUN_14053d2b0
    /// <summary>CItemDefaultData s_aSlots that hold lists (the rest stay empty in the table).</summary>
    static readonly EquipSlot[] ListSlots = { EquipSlot.HR, EquipSlot.FA, EquipSlot.FD, EquipSlot.CL, EquipSlot.PA, EquipSlot.SH };

    /// <summary>A defaultitems row (DefaultItemData): id, saleTag, select.</summary>
    public sealed record Row(int Id, int SaleTag, bool Select);

    readonly PackSet packs;
    readonly ColorPalettes palettes;
    readonly Ms2.Ui.Items.ItemFiles files;
    readonly Ms2.Ui.Items.ItemDataTable itemTable;
    readonly Dictionary<int, string> itemNames = new();
    // job code -> gender -> slot -> rows (TDefaultItems::m_mapSlotItems); base items per gender (ms_aMaps)
    readonly Dictionary<int, Dictionary<EquipSlot, List<Row>>[]> lists = new();
    readonly Dictionary<EquipSlot, int>[] baseItems = { new(), new() };

    public int Gender, JobCode;
    public readonly Dictionary<EquipSlot, int> Items = new();        // slot -> item id
    public readonly Dictionary<EquipSlot, int> Rows = new();         // slot -> palette row
    public int SkinRow;
    /// <summary>The customize values of each item worn so far.</summary>
    public readonly Dictionary<int, CustomValues> Custom = new();
    readonly Dictionary<int, ItemCustomize> customizes = new();
    public Random Rng = new();

    public CreateLook(PackSet packs, ColorPalettes palettes, string lang = null)
    {
        this.packs = packs; this.palettes = palettes;
        files = new(packs); itemTable = new(packs);
        lang ??= GameOptions.StringLanguage;
        var names = Load($"string/{lang}/itemname.xml");
        if (names != null)
            foreach (var k in names.Root.Elements("key"))
                if (int.TryParse((string)k.Attribute("id"), out int id)) itemNames.TryAdd(id, (string)k.Attribute("name"));
        LoadDefaults(Load("table/defaultitems.xml"));
    }

    CreateLook(CreateLook o)
    {
        packs = o.packs; palettes = o.palettes; files = o.files; itemTable = o.itemTable; itemNames = o.itemNames;
        lists = o.lists; baseItems = o.baseItems; customizes = o.customizes; Rng = o.Rng;
        Gender = o.Gender; JobCode = o.JobCode; SkinRow = o.SkinRow;
        foreach (var (k, v) in o.Items) Items[k] = v;
        foreach (var (k, v) in o.Rows) Rows[k] = v;
        foreach (var (k, v) in o.Custom) Custom[k] = v.Clone();
    }

    /// <summary>BackUpCharacterInfo: the look as it is now (gender, job, items, colours, customize values).</summary>
    public CreateLook Snapshot() => new(this);

    /// <summary>CSceneStateMulti::FUN_14174ee00: the actor made again from a backup.</summary>
    public void Restore(CreateLook b)
    {
        Gender = b.Gender; JobCode = b.JobCode; SkinRow = b.SkinRow;
        Items.Clear(); Rows.Clear(); Custom.Clear();
        foreach (var (k, v) in b.Items) Items[k] = v;
        foreach (var (k, v) in b.Rows) Rows[k] = v;
        foreach (var (k, v) in b.Custom) Custom[k] = v.Clone();
    }

    XDocument Load(string entry) => packs.Exists("Xml", entry)
        ? PackXml.Parse(packs.Read("Xml", entry)) : null;

    XElement Data(int id) => files.Item(id);
    XElement Model(int id) => files.Model(ModelId(id));

    /// <summary>
    /// CItemDefaultData load: each key's slots, each item that is visible or a base item (the rows carry no locale or
    /// feature, so IsElementAvailable passes them all); a non-base item whose model slot is not the list's slot is dropped.
    /// Base items set the gender's default for the slot and join the list only when visible or of group type 0x12.
    /// </summary>
    void LoadDefaults(XDocument doc)
    {
        if (doc == null) return;
        foreach (var key in doc.Root.Elements("key"))
        {
            int job = int.TryParse((string)key.Attribute("jobCode"), out int j) ? j : 0;
            if (!lists.TryGetValue(job, out var byGender)) lists[job] = byGender = new[] { new Dictionary<EquipSlot, List<Row>>(), new() };
            foreach (var slotEl in key.Elements("slot"))
            {
                if (!Enum.TryParse<EquipSlot>((string)slotEl.Attribute("name"), out var slot)) continue;
                foreach (var it in slotEl.Elements("item"))
                {
                    bool visible = (string)it.Attribute("visible") == "1", isBase = (string)it.Attribute("base") == "1";
                    if (!visible && !isBase) continue;
                    if (!int.TryParse((string)it.Attribute("id"), out int id) || Data(id) == null) continue;
                    if (!isBase && ModelSlot(id) != slot) continue;
                    int gender = GenderLimit(id);
                    var row = new Row(id, int.TryParse((string)it.Attribute("saleTag"), out int tag) ? tag : 0, (string)it.Attribute("select") == "1");
                    if (isBase)
                    {
                        if (gender == 2) { baseItems[0][slot] = id; baseItems[1][slot] = id; }
                        else if (gender is 0 or 1) baseItems[gender][slot] = id;
                        // CItemData::FUN_14053cf40: group type 0x12 (the ears)
                        if (!visible && ModelSlot(id) != EquipSlot.ER) continue;
                    }
                    for (int g = 0; g < 2; g++)
                        if (gender == 2 || gender == g) AddSlotItem(byGender[g], slot, row);
                }
            }
        }
    }

    void AddSlotItem(Dictionary<EquipSlot, List<Row>> map, EquipSlot slot, Row row)
    {
        if (!map.TryGetValue(slot, out var list)) map[slot] = list = new();
        list.Add(row);
        if (slot == EquipSlot.CL && IsOverall(row.Id)) AddSlotItem(map, EquipSlot.PA, row);
    }

    public string ItemName(int id) => itemNames.GetValueOrDefault(id, "");

    /// <summary>The item's slotIcon (itemdata property), a Resource/Image path.</summary>
    public string IconPath(int id) => (string)Data(id)?.Descendants("property").FirstOrDefault()?.Attribute("slotIcon") ?? "";

    /// <summary>The item's guide movie (itemdata property movie).</summary>
    public string MovieOf(int id) => (string)Data(id)?.Descendants("property").FirstOrDefault()?.Attribute("movie") ?? "";

    /// <summary>CItemData::GetEquipSubtype: the property subtype of an equip (type 1).</summary>
    public int Subtype(int id)
    {
        return itemTable.Get(id)?.EquipSubtype ?? 0;
    }

    /// <summary>CItemData::FUN_14053d2b0: an overall (top and bottom in one).</summary>
    public bool IsOverall(int id) => Subtype(id) == OverallSubtype;

    /// <summary>GetModelOrEmpty()->GetFirstGroupType(): the item model's first slot, the equip slot the item goes to.</summary>
    public EquipSlot? ModelSlot(int id) =>
        Enum.TryParse<EquipSlot>((string)Model(id)?.Element("slots")?.Element("slot")?.Attribute("name"), out var s) ? s : null;

    /// <summary>The item model's customize options (cached), and the guide movie of its itemdata property.</summary>
    public ItemCustomize Customize(int id)
    {
        if (customizes.TryGetValue(id, out var c)) return c;
        if (id == SkinItem) return customizes[id] = new ItemCustomize();
        return customizes[id] = ItemCustomize.Read(Model(id), MovieOf(id));
    }

    /// <summary>The item's customize values, made as a new item makes them the first time it is worn.</summary>
    public CustomValues ValuesOf(int id)
    {
        if (!Custom.TryGetValue(id, out var v)) Custom[id] = v = CustomValues.New(Customize(id), Rng);
        return v;
    }

    public int GenderLimit(int id)
    {
        var limit = Data(id)?.Descendants("limit").FirstOrDefault();
        return int.TryParse((string)limit?.Attribute("genderLimit"), out int g) ? g : 2;
    }

    /// <summary>The item's model id (a tool itemPreset replaces it, as for the female copies of the starter outfits).</summary>
    int ModelId(int id)
    {
        var data = Data(id);
        return int.TryParse((string)data?.Descendants("tool").FirstOrDefault()?.Attribute("itemPreset"), out int pre) && pre != 0 ? pre : id;
    }

    /// <summary>
    /// The item model's customize block (CItemData::FUN_14053e260 / FUN_14053e2b0): colour palette and default row, (0, -1)
    /// when the item takes no dye; the default row is -1 when the model names none (defaultColorIndex "-1").
    /// </summary>
    public (int Palette, int DefaultRow) Palette(int id)
    {
        var cust = Model(id)?.Element("customize");
        // GMS2: the skin item's preset has no palette; its item row carries it (STILL-OPEN: the GMS2 exe's own avatar-colour test)
        if (id == SkinOwner && cust?.Attribute("colorPalette") == null) cust = Data(id)?.Descendants("customize").FirstOrDefault();
        if (cust == null || (string)cust.Attribute("color") != "1" || !int.TryParse((string)cust.Attribute("colorPalette"), out int pal)) return (0, -1);
        var first = ((string)cust.Attribute("defaultColorIndex") ?? "-1").Split(',')[0];
        return (pal, int.TryParse(first, out int idx) ? idx : -1);
    }

    public IReadOnlyList<(Rgba c0, Rgba c1, Rgba c2)> PaletteRows(int palette) => palettes.Rows(palette);

    /// <summary>The palette row's UI colour (visibleColor) and its ch0 (realColor).</summary>
    public Rgba Visible(int palette, int row) => palettes.Visible(palette, row);

    /// <summary>
    /// CItemExtraData::InitColorsFromPalette (GetPaletteColorOrRandom): the model's default row when it names one inside
    /// the palette, else a random row.
    /// </summary>
    public int NewRow(int id)
    {
        var (pal, def) = Palette(id);
        int n = pal == 0 ? 0 : PaletteRows(pal).Count;
        if (n == 0) return -1;
        return def != -1 && def < n ? def : Rng.Next(n);
    }

    /// <summary>CItemDefaultData::GetSlotItems(job, gender, slot): the common rows, then the job's.</summary>
    public List<Row> SlotRows(EquipSlot slot)
    {
        var list = new List<Row>();
        if (JobCode == 0 || Gender is not (0 or 1)) return list;
        foreach (var job in new[] { 0, JobCode })
            if (lists.TryGetValue(job, out var g) && g[Gender].TryGetValue(slot, out var rows)) list.AddRange(rows);
        return list;
    }

    /// <summary>
    /// CItemDefaultData::GetCreateItems and the look it makes (CSceneStateMulti::FUN_14174ec40, TCreateCharInfo): per slot
    /// the list's select row when <paramref name="selected"/> (the job's own row winning), else a random row of the common
    /// and job lists; an overall top keeps the bottom off. Each item gets InitColorsFromPalette colours and new customize
    /// values; the skin colour is the avatar colour item's (TCreateCharInfo::FUN_14107ae00).
    /// </summary>
    public void Create(int gender, int jobCode, bool selected)
    {
        Gender = gender; JobCode = jobCode;
        Items.Clear(); Rows.Clear(); Custom.Clear();
        SkinRow = NewRow(SkinOwner);
        int top = 0, bottom = 0;
        foreach (var slot in ListSlots)
        {
            List<Row> common = lists.TryGetValue(0, out var c) && c[gender].TryGetValue(slot, out var cl) ? cl : null;
            List<Row> job = lists.TryGetValue(jobCode, out var j) && j[gender].TryGetValue(slot, out var jl) ? jl : null;
            int count = (common?.Count ?? 0) + (job?.Count ?? 0);
            if (count == 0) continue;
            Row pick = null;
            if (selected) pick = job?.LastOrDefault(r => r.Select) ?? common?.LastOrDefault(r => r.Select);
            if (pick == null)
            {
                int i = Rng.Next(count);
                pick = common != null && i < common.Count ? common[i] : job[i - (common?.Count ?? 0)];
            }
            if (slot == EquipSlot.CL) top = pick.Id;
            else if (slot == EquipSlot.PA) bottom = pick.Id;
            else Wear(pick.Id);
        }
        // the top and bottom go last: an overall top alone, else the top and a different bottom (an overall drawn from the
        // bottom list goes on in the top's slot, as the PC equips by the model's slot)
        if (top != 0 && IsOverall(top)) Wear(top);
        else
        {
            if (top != 0) Wear(top);
            if (bottom != 0 && bottom != top) Wear(bottom);
        }
        // the gender's base items fill the slots the lists left empty (the ears)
        foreach (var (slot, id) in baseItems[gender])
            if (slot != EquipSlot.SK && !Items.ContainsKey(slot)) { Items[slot] = id; Rows[slot] = NewRow(id); }
    }

    void Wear(int id)
    {
        var slot = ModelSlot(id) ?? EquipSlot.CL;
        if (slot == EquipSlot.CL && IsOverall(id)) { Items.Remove(EquipSlot.PA); Rows.Remove(EquipSlot.PA); }
        Items[slot] = id;
        Rows[slot] = NewRow(id);
        ValuesOf(id);
    }

    /// <summary>
    /// Customize::EquipItemId / EquipItem: the item goes to its model's slot (an overall takes the bottom off; a bottom takes
    /// an overall off), with <paramref name="row"/> and <paramref name="values"/> when the dialog kept the item from earlier
    /// (m_items), else as a new item. Returns the slot.
    /// </summary>
    public EquipSlot Equip(int id, int? row = null, CustomValues values = null)
    {
        var slot = ModelSlot(id) ?? EquipSlot.CL;
        if (slot == EquipSlot.CL && IsOverall(id)) { Items.Remove(EquipSlot.PA); Rows.Remove(EquipSlot.PA); }
        if (slot == EquipSlot.PA && Items.TryGetValue(EquipSlot.CL, out int t) && IsOverall(t)) { Items.Remove(EquipSlot.CL); Rows.Remove(EquipSlot.CL); }
        Items[slot] = id;
        Rows[slot] = row ?? NewRow(id);
        if (values != null) Custom[id] = values.Clone(); else { Custom.Remove(id); ValuesOf(id); }
        return slot;
    }

    public int RowOf(EquipSlot slot) => Rows.TryGetValue(slot, out int r) ? r : -1;

    public EquipColor ColorOf(EquipSlot slot)
    {
        if (!Items.TryGetValue(slot, out int id)) return default;
        var (pal, _) = Palette(id);
        return pal == 0 ? default : palettes.Equip(pal, RowOf(slot)) ?? default;
    }

    /// <summary>The skin colour pair (ch0 lit, ch2 shade) of the avatar colour item's palette row.</summary>
    public SkinColor? Skin => palettes.Get(Palette(SkinOwner).Palette, SkinRow) is { } c ? new SkinColor(c.c0, c.c2) : null;

    /// <summary>The worn items as EquipApplier takes them.</summary>
    public List<EquipItem> Equips()
    {
        var list = new List<EquipItem>();
        foreach (var (slot, id) in Items) list.Add(new EquipItem { Id = id, Slot = slot, Color = ColorOf(slot) });
        return list;
    }

    /// <summary>
    /// The create request's item block (UICharacterCreateDialog::FUN_1418e3f50): the shown items, each with its id,
    /// equip slot and appearance (colour, then the slot's customize floats: CHairExtraData / CDecalExtraData::vfunc_5).
    /// </summary>
    public byte[] PacketItems() => CreateCharacter.EncodeItems(Equips().Where(e => e.Slot != EquipSlot.ER)
        .Select(e => new CreateItem(e.Id, e.Slot, e.Color, ValuesOf(e.Id).Appearance(e.Slot))));

    public CreateCharacter Packet(string name, int elapsedSeconds)
    {
        var skin = Skin ?? default;
        return new CreateCharacter((byte)Gender, (short)JobCode, name, Argb(skin.Primary), Argb(skin.Secondary), PacketItems(), elapsedSeconds);
    }

    public static int Argb(Rgba c) => c.A << 24 | c.R << 16 | c.G << 8 | c.B;

    /// <summary>The root node's name in the last built body's file (the effect dummies' "Scene Root" parent).</summary>
    public string RootName { get; private set; }

    /// <summary>The preview body: the gender's body NIF with idle, dressed through EquipApplier with the skin colour.</summary>
    public (Node3D Visual, AnimationPlayer Anim) Build(ModelBuilder builder)
    {
        string dir = Gender == 0 ? "male" : "female";
        var md = builder.LoadModel($"{dir}/{(Gender == 0 ? "m_body.nif" : "f_body.nif")}");
        RootName = md.RootName;
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
