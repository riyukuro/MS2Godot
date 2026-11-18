using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui.Items;

/// <summary>
/// CItemDataTable's item models (ms2/commonfiles/itemdatatable.cpp), the parts the item tooltip reads. GetModel (:599)
/// loads itemmodel/&lt;preset / 100000&gt;.xml on first use (LoadModelFile :1569): each &lt;ItemModel&gt; by id, a later
/// one replacing an earlier one. Of a model only ItemModelHead::Load's &lt;customize&gt; (itemdatastruct.cpp:930) is read.
/// </summary>
public sealed class ItemModelTable
{
    /// <summary>
    /// ItemModelHead (ctor itemdatastruct.cpp:924): ColorEnabled is "color" (flag10, cleared without a palette),
    /// Palette is "colorPalette" (FUN_140547250), DefaultColors is "defaultColorIndex" split at "," (default "-1"; GetIdAt
    /// :1049), and MultiColor is more than one default (flag11).
    /// </summary>
    public sealed record Model(bool ColorEnabled, int Palette, IReadOnlyList<int> DefaultColors)
    {
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
            models[id] = new Model(color, palette, ids);
        }
        return models;
    }
}
