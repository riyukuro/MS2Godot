using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui.Items;

/// <summary>
/// CItemDataTable's item models (ms2/commonfiles/itemdatatable.cpp), the parts the item tooltip and weapon attachment read.
/// GetModel (:599) loads itemmodel/&lt;preset / 100000&gt;.xml on first use (LoadModelFile :1569): each &lt;ItemModel&gt; by id, a later
/// one replacing an earlier one. Of a model ItemModelHead::Load's &lt;customize&gt; (itemdatastruct.cpp:930) is read, and of
/// ItemModelData::Load (0x14054af00) each &lt;slots&gt;&lt;slot&gt;'s type and, on demand (<see cref="GetAssets"/>), each slot's
/// &lt;asset&gt; (ModelPart::Load @0x140543ca0, itemdatastruct.cpp:287) and &lt;dummy&gt; (ItemDummyInfo::Load @0x140314350,
/// dungeonmatch.cpp:36; itemdatastruct.cpp:379-406).
/// </summary>
public sealed class ItemModelTable
{
    /// <summary>
    /// ItemDummyInfo (commonfiles/dungeonmatch.cpp:28-46), loaded by ModelPart::Load (@0x140543ca0, itemdatastruct.cpp:379-406):
    /// gender (default 2 = both), sequence (+0x08), targetnode (+0x10; when empty in XML, ModelPart::Load defaults it to the
    /// parent asset's targetnode at itemdatastruct.cpp:393-394), rotation (+0x18, Euler XYZ degrees), translation (+0x24),
    /// step (+0x30, default 0.0003f), worldmode (+0x34) and worldlerp (+0x35).
    /// </summary>
    public sealed record Dummy(int Gender, string Sequence, string TargetNode, Vector3 Rotation, Vector3 Translation,
        float Step = 0.0003f, bool WorldMode = false, bool WorldLerp = false);

    /// <summary>
    /// ModelPart (commonfiles/itemdata.h:118-150), loaded by ModelPart::Load (@0x140543ca0, itemdatastruct.cpp:287-412) inside
    /// a &lt;slot&gt; (ModelGroup::Load @0x140544cf0, :439). MaleDummies and FemaleDummies are ModelPart::entries[0] and
    /// entries[1] (+0x128): a &lt;dummy&gt; with gender 2 is pushed to both (:395-398).
    /// </summary>
    public sealed record Asset(int SlotType, string SlotName, string Name, string SelfNode, string TargetNode, string AttachNode,
        int Gender, int Weapon, bool Replace, bool Pony, IReadOnlyList<Dummy> MaleDummies, IReadOnlyList<Dummy> FemaleDummies)
    {
        /// <summary>ModelPart::entries[gender] (+0x128, 0 male, 1 female).</summary>
        public IReadOnlyList<Dummy> DummiesFor(int gender) => gender == 0 ? MaleDummies : FemaleDummies;

        /// <summary>
        /// CEquipModel::FUN_141202940 (@0x141202940, client/ms2/equipmodel.cpp:1071-1080): the model's effective hand slot.
        /// For a dual-wield OH item (CItemData::FUN_14053d510 @0x14053d510, itemdata.cpp:925: subtype 31 dagger or 34 star),
        /// returns the item's equipped slot (CItem::FUN_140526c30 @0x140526c30, 4 LH or 5 RH); otherwise the group's own
        /// ModelGroup::GetType() (4 for &lt;slot name="LH"&gt;, 5 for &lt;slot name="RH"&gt;).
        /// </summary>
        public int EffectiveSlot(int equippedSlot, bool isDualWield) => isDualWield ? equippedSlot : SlotType;

        /// <summary>
        /// CEquipModel::GetPartName (@0x1412029d0, client/ms2/equipmodel.cpp:1084-1102): the sheathed target node on the body.
        /// Starts with ModelPart::name10 (targetnode); when the item is a dual-wield OH weapon (FUN_14053d510) worn in LH
        /// (slot 4), swaps "_L_" -&gt; "_R_" and " L " -&gt; " R ", or if neither was replaced, "_R_" -&gt; "_L_" and " R " -&gt; " L ".
        /// </summary>
        public string SheathedNode(int equippedSlot, bool isDualWield)
        {
            string node = TargetNode ?? "";
            if (!isDualWield || equippedSlot != 4 || node.Length == 0) return node;
            string swapped = node.Replace("_L_", "_R_", StringComparison.Ordinal).Replace(" L ", " R ", StringComparison.Ordinal);
            if (swapped != node) return swapped;
            return node.Replace("_R_", "_L_", StringComparison.Ordinal).Replace(" R ", " L ", StringComparison.Ordinal);
        }

        /// <summary>
        /// PC::FUN_141658150 (@0x141658150, client/ms2/pc.cpp:4013-4018): whether any &lt;dummy&gt; for the gender has
        /// worldmode or worldlerp set (nWorld != 0 keeps an LH part on its sheathed/world node).
        /// </summary>
        public bool HasWorldDummy(int gender)
        {
            foreach (var d in DummiesFor(gender))
                if (d.WorldMode || d.WorldLerp) return true;
            return false;
        }

        /// <summary>
        /// FUN_141200390 (@0x141200390, client/ms2/equipmodel.cpp:563-604): the WeaponAttachTransformNode's base translation
        /// and Euler XYZ rotation (degrees) when attached under <paramref name="parentNode"/> while the character plays
        /// <paramref name="sequence"/>. Resets to zero translation and identity rotation, then walks ModelPart::entries[gender]
        /// in order: skips any entry whose sequence is non-empty and not a prefix of <paramref name="sequence"/> (:580-585), or
        /// whose targetnode is non-empty and not equal to <paramref name="parentNode"/> (:587-588); each matching entry sets
        /// the base rotation and position (:589-594). Because ModelPart::Load defaults an empty dummy targetnode to the asset's
        /// sheathed targetnode (itemdatastruct.cpp:393-394), a dummy without an explicit hand targetnode applies only on the
        /// sheathed node and resets to (0, 0, 0) when re-parented to Weapon_Hand_R_Point / Weapon_Hand_L_Point.
        /// </summary>
        public (Vector3 Translation, Vector3 Rotation) DummyFor(int gender, string parentNode, string sequence = "")
        {
            var translation = Vector3.Zero;
            var rotation = Vector3.Zero;
            string seq = sequence ?? "";
            foreach (var d in DummiesFor(gender))
            {
                if (d.Sequence.Length > 0 && !seq.StartsWith(d.Sequence, StringComparison.Ordinal)) continue;
                if (d.TargetNode.Length > 0 && d.TargetNode != parentNode) continue;
                translation = d.Translation;
                rotation = d.Rotation;
            }
            return (translation, rotation);
        }
    }

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
    readonly Dictionary<int, Dictionary<int, IReadOnlyList<Asset>>> assetFiles = new();

    public static readonly ItemModelTable Empty = new(null);

    public ItemModelTable(PackSet packs) { this.packs = packs; }

    /// <summary>GetModel: the model of a preset, null when none.</summary>
    public Model Get(int preset)
    {
        int file = (int)((uint)preset / 100000);                                   // ItemModelData::FUN_14054aec0
        if (!files.TryGetValue(file, out var models)) files[file] = models = Load(file);
        return models.TryGetValue(preset, out var m) ? m : null;
    }

    /// <summary>The &lt;asset&gt; entries of <paramref name="preset"/> across its &lt;slots&gt;, empty when unknown.</summary>
    public IReadOnlyList<Asset> GetAssets(int preset)
    {
        int file = (int)((uint)preset / 100000);
        if (!assetFiles.TryGetValue(file, out var byId)) assetFiles[file] = byId = LoadAssets(file);
        return byId.TryGetValue(preset, out var list) ? list : Array.Empty<Asset>();
    }

    Dictionary<int, Model> Load(int file)
    {
        var models = new Dictionary<int, Model>();
        string path = $"itemmodel/{file}.xml";
        if (packs == null) return models;
        var doc = PerFileTable.Choose(packs.Exists("Xml", path), () => GameStrings.Parse(packs.Read("Xml", path)), () => PerFileTable.ItemModels(packs, file));    // GMS2: itempreset/
        if (doc == null) return models;
        foreach (var e in doc.Root.Elements("ItemModel"))
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

    Dictionary<int, IReadOnlyList<Asset>> LoadAssets(int file)
    {
        var byId = new Dictionary<int, IReadOnlyList<Asset>>();
        string path = $"itemmodel/{file}.xml";
        if (packs == null) return byId;
        var doc = PerFileTable.Choose(packs.Exists("Xml", path), () => GameStrings.Parse(packs.Read("Xml", path)), () => PerFileTable.ItemModels(packs, file));
        if (doc == null) return byId;
        foreach (var e in doc.Root.Elements("ItemModel"))
        {
            int id = ItemData.Atoi((string)e.Attribute("id") ?? "0");
            byId[id] = ParseAssets(e);
        }
        return byId;
    }

    /// <summary>Parses all &lt;slots&gt;&lt;slot&gt;&lt;asset&gt; elements of an &lt;ItemModel&gt; (ModelGroup::Load @0x140544cf0, ModelPart::Load @0x140543ca0).</summary>
    public static IReadOnlyList<Asset> ParseAssets(XElement itemModel)
    {
        var list = new List<Asset>();
        if (itemModel?.Element("slots") is not { } slots) return list;
        foreach (var slot in slots.Elements("slot"))
        {
            string slotName = (string)slot.Attribute("name") ?? "";
            int slotType = SlotType(slotName);
            foreach (var asset in slot.Elements("asset"))
                list.Add(ParseAsset(slotType, slotName, asset));
        }
        return list;
    }

    /// <summary>ModelPart::Load (@0x140543ca0, commonfiles/itemdatastruct.cpp:287-412) and ItemDummyInfo::Load (@0x140314350, commonfiles/dungeonmatch.cpp:36-46).</summary>
    public static Asset ParseAsset(int slotType, string slotName, XElement asset)
    {
        string name = (string)asset.Attribute("name") ?? "";
        string selfNode = (string)asset.Attribute("selfnode") ?? "";
        string targetNode = (string)asset.Attribute("targetnode") ?? "";
        string attachNode = (string)asset.Attribute("attachnode") ?? "";
        int gender = asset.Attribute("gender") is { } ga ? ItemData.Atoi(ga.Value) : 2;
        int weapon = asset.Attribute("weapon") is { } wa ? ItemData.Atoi(wa.Value) : 0;
        bool replace = false, pony = false;
        ItemData.Bool(asset, "replace", ref replace);
        ItemData.Bool(asset, "pony", ref pony);
        var male = new List<Dummy>();
        var female = new List<Dummy>();
        foreach (var d in asset.Elements("dummy"))
        {
            int dg = d.Attribute("gender") is { } dga ? ItemData.Atoi(dga.Value) : 2;
            if (dg is < 0 or > 2) continue;
            string seq = (string)d.Attribute("sequence") ?? "";
            string dt = (string)d.Attribute("targetnode") ?? "";
            if (dt.Length == 0) dt = targetNode;     // itemdatastruct.cpp:393-394: if (kEntry.targetnode.IsEmpty()) kEntry.targetnode = name10
            var rot = ParseVec3((string)d.Attribute("rotation"));
            var pos = ParseVec3((string)d.Attribute("translation"));
            float step = d.Attribute("step") is { } sa && float.TryParse(sa.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var sf) ? sf : 0.0003f;
            bool worldMode = false, worldLerp = false;
            ItemData.Bool(d, "worldmode", ref worldMode);
            ItemData.Bool(d, "worldlerp", ref worldLerp);
            var info = new Dummy(dg, seq, dt, rot, pos, step, worldMode, worldLerp);
            if (dg == 2) { male.Add(info); female.Add(info); }
            else if (dg == 0) male.Add(info);
            else female.Add(info);
        }
        return new Asset(slotType, slotName ?? "", name, selfNode, targetNode, attachNode, gender, weapon, replace, pony, male, female);
    }

    static Vector3 ParseVec3(string s)
    {
        if (string.IsNullOrEmpty(s)) return Vector3.Zero;
        var parts = s.Split(',');
        float x = parts.Length > 0 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var vx) ? vx : 0f;
        float y = parts.Length > 1 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var vy) ? vy : 0f;
        float z = parts.Length > 2 && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var vz) ? vz : 0f;
        return new Vector3(x, y, z);
    }

    static readonly string[] SlotNames = { "HR", "FA", "FD", "LH", "RH", "CP", "MT", "CL", "PA", "GL", "SH", "FH", "EY", "EA", "PD", "RI", "BE", "ER", "OH" };

    /// <summary>Parse_14021bdc0 (commonenum.cpp:1401): HR 1 through OH 19 in this order, exact case; anything else 0.</summary>
    public static int SlotType(string name) => Array.IndexOf(SlotNames, name) + 1;

    /// <summary>Name_14021c050 (commonenum.cpp:1000): the equip slot's name, "" outside 1-19.</summary>
    public static string SlotName(int slot) => slot is >= 1 and <= 19 ? SlotNames[slot - 1] : "";
}
