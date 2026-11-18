using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui.Items;

/// <summary>
/// CItemDataTable's item models (ms2/commonfiles/itemdatatable.cpp), the parts the item tooltip reads. GetModel (:599)
/// loads itemmodel/&lt;preset / 100000&gt;.xml on first use (LoadModelFile :1569): each &lt;ItemModel&gt; by id, a later
/// one replacing an earlier one. Of a model ItemModelHead::Load's &lt;customize&gt; (itemdatastruct.cpp:930) is read, and of
/// ItemModelData::Load (0x14054af00) each &lt;slots&gt;&lt;slot&gt;'s type only.
/// </summary>
public sealed class ItemModelTable
{
    /// <summary>
    /// ItemModelHead (ctor itemdatastruct.cpp:924): ColorEnabled is "color" (flag10, cleared without a palette),
    /// Palette is "colorPalette" (FUN_140547250), DefaultColors is "defaultColorIndex" split at "," (default "-1"; GetIdAt
    /// :1049), and MultiColor is more than one default (flag11). SlotTypes are the groups' types in order: ModelGroup::Load
    /// (0x140544cf0) reads "name" through ReadSlotAttr and Parse_14021bdc0 (SlotType); a missing name keeps 0.
    /// </summary>
    public sealed record Model(bool ColorEnabled, int Palette, IReadOnlyList<int> DefaultColors, IReadOnlyList<int> SlotTypes)
    {
        /// <summary>ItemModelData::GetFirstGroupType (itemdatastruct.cpp:2079): 0 without groups.</summary>
        public int FirstSlotType => SlotTypes.Count > 0 ? SlotTypes[0] : 0;

        /// <summary>ItemModelData::HasGroupType (0x14054b450): an OH group, met first, answers for RH and LH only.</summary>
        public bool HasSlotType(int type)
        {
            foreach (int t in SlotTypes)
            {
                if (t == 19) return type == 5 || type == 4;
                if (t == type) return true;
            }
            return false;
        }

        public bool MultiColor => DefaultColors.Count > 1;
        /// <summary>GetIdAt: -1 past the list.</summary>
        public int DefaultColor(int index) => index < DefaultColors.Count ? DefaultColors[index] : -1;
    }

    readonly PackSet packs;
    readonly Dictionary<int, Dictionary<int, Model>> files = new();

    public static readonly ItemModelTable Empty = new(null);

    public ItemModelTable(PackSet packs) { this.packs = packs; }

    /// <summary>GetModel: the model of a preset, null when none.</summary>
    public Model Get(int preset)
    {
        int file = (int)((uint)preset / 100000);                                   // ItemModelData::FUN_14054aec0
        if (!files.TryGetValue(file, out var models)) files[file] = models = Load(file);
        return models.TryGetValue(preset, out var m) ? m : null;
    }

    Dictionary<int, Model> Load(int file)
    {
        var models = new Dictionary<int, Model>();
        string path = $"itemmodel/{file}.xml";
        if (packs == null || !packs.Exists("Xml", path)) return models;
        foreach (var e in GameStrings.Parse(packs.Read("Xml", path)).Root.Elements("ItemModel"))
        {
            int id = ItemData.Atoi((string)e.Attribute("id") ?? "0");
            bool color = false; int palette = 0; var ids = new List<int>();
            if (e.Element("customize") is { } c)
            {
                ItemData.Bool(c, "color", ref color);
                palette = ItemData.Atoi((string)c.Attribute("colorPalette") ?? "0");
                ids = ((string)c.Attribute("defaultColorIndex") ?? "-1").Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(ItemData.Atoi).ToList();
                if (palette == 0) color = false;
            }
            var slots = e.Element("slots")?.Elements("slot").Select(x => x.Attribute("name") is { } n ? SlotType(n.Value) : 0).ToList()
                ?? new List<int>();
            models[id] = new Model(color, palette, ids, slots);
        }
        return models;
    }

    static readonly string[] SlotNames = { "HR", "FA", "FD", "LH", "RH", "CP", "MT", "CL", "PA", "GL", "SH", "FH", "EY", "EA", "PD", "RI", "BE", "ER", "OH" };

    /// <summary>Parse_14021bdc0 (commonenum.cpp:1401): HR 1 through OH 19 in this order, exact case; anything else 0.</summary>
    public static int SlotType(string name) => Array.IndexOf(SlotNames, name) + 1;

    /// <summary>Name_14021c050 (commonenum.cpp:1000): the equip slot's name, "" outside 1-19.</summary>
    public static string SlotName(int slot) => slot is >= 1 and <= 19 ? SlotNames[slot - 1] : "";
}
