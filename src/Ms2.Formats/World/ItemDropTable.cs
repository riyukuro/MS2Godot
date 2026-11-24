using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Formats.World;

/// <summary>One &lt;v&gt; of Table/ItemData.xml's dropStyle (ItemDropEffect::Load, itemdatastruct.cpp:239): how a dropped item looks and moves.</summary>
public sealed class DropStyle
{
    public int TypeId;
    /// <summary>"bias" (CItemDropEffect +0x1c, default 50): the height the drop rests above the ground.</summary>
    public float RestHeight = 50f;
    public float Scale = 1f, GetAniSpeed, DropAniSpeed, BeAniSpeed, ShadowBias, ShadowScale;
    /// <summary>The bob of a drop on the ground: its height and period (defaults 30 and 1).</summary>
    public float BounceHeight = 30f, BouncePeriod = 1f;
    /// <summary>The spin axes: while it flies out (dropani), rests (beani) and flies to the picker (getani).</summary>
    public float[] GetAni = new float[3], DropAni = new float[3], BeAni = new float[3];
    public string BeEffect = "", AssetName = "", DropSoundId = "", GetSoundId = "";
}

/// <summary>What a drop looks like: its style row, the model name (the nif under "urn:gamebryo-scenegraph:") and whether it is meso.</summary>
public sealed record DropLook(DropStyle Style, string Asset, bool IsMeso);

/// <summary>
/// The drop part of Table/ItemData.xml (CItemDataTable::Load @0x140556b40, LoadDropItemAsset, LoadDropStyle, LoadGradeNif, LoadGradePetNif,
/// LoadGradeEffect): the model of a drop is a meso or special item's own asset (dropItemAsset, by item id and the amount) or the grade's nif.
/// </summary>
public sealed class ItemDropTable
{
    readonly Dictionary<int, DropStyle> styles = new();
    readonly Dictionary<int, List<(int TypeId, uint Min)>> assets = new();
    readonly Dictionary<int, string> gradeNif = new(), gradePetNif = new(), gradeEffect = new();

    /// <summary>Style types the client's InitDrop asks for: 1 = an ordinary item's drop, 2 = a pet item's.</summary>
    public const int ItemStyle = 1, PetStyle = 2;

    public ItemDropTable(PackSet packs)
    {
        const string entry = "table/itemdata.xml";
        if (!packs.Exists("Xml", entry)) return;
        var root = GameStrings.Parse(packs.Read("Xml", entry)).Root;
        Load(root);
        if (styles.Count == 0 && root?.Element("dropitem") != null) LoadGms2(root, id => ItemFile(packs, id));
    }

    /// <summary>The GMS2 layout (<see cref="LoadGms2"/>) with the item files given by id; for tests.</summary>
    public ItemDropTable(XElement root, Func<int, XElement> itemFile)
    {
        Load(root);
        if (styles.Count == 0 && root?.Element("dropitem") != null) LoadGms2(root, itemFile);
    }

    /// <summary>The ids whose dropItemAsset rows InitDrop reads: the meso (its rows serve all three meso ids) and the four special items.</summary>
    public static readonly int[] AssetItemIds = { 0x55d4a81, 0x55d4a88, 0x55d4a89, 0x55d4a8a, 0x55d4a93 };

    /// <summary>
    /// The GMS2 data's layout of the same table. Its drop styles are &lt;dropitem name type ...&gt; rows with no sound ids, and
    /// each item keeps its asset rows in its own file (item/a/bb/id.xml): property@xmlData holds &lt;v asset displayType
    /// min|minRate/&gt;, the style by name. The KMS table is this data converted: style 1 has every value of the row
    /// "equip_ItemBox"; rows 11-14, 51-54, 61-64, 71-74 are the xmlData rows of 90000001, 90000008, 90000009 and 90000010
    /// as (displayType's values, asset); 90000008's minRate is its KMS min over 10000. Built the same way here: each xmlData
    /// row becomes a style of its own, and styles 1 and 2 (InitDrop's item and pet styles) take "equip_ItemBox".
    /// STILL-OPEN: the GMS2 data has no pet style (KMS style 2 is the box at scale 1.5 with no bounce) and no drop or get
    /// sound ids, so a GMS2 drop plays none.
    /// </summary>
    void LoadGms2(XElement root, Func<int, XElement> itemFile)
    {
        var byName = new Dictionary<string, DropStyle>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in root.Elements("dropitem")) byName[S(v, "name")] = Row(v, I(v, "type"));
        if (byName.TryGetValue("equip_ItemBox", out var box))
        {
            styles[ItemStyle] = Copy(box, ItemStyle, "");
            styles[PetStyle] = Copy(box, PetStyle, "");
        }
        int next = 1000;
        foreach (int id in AssetItemIds)
        {
            string data = (string)itemFile(id)?.Element("environment")?.Element("property")?.Attribute("xmlData");
            if (string.IsNullOrWhiteSpace(data)) continue;
            XElement rows;
            try { rows = XElement.Parse("<rows>" + data + "</rows>"); } catch (System.Xml.XmlException) { continue; }
            var list = assets[id] = new();
            foreach (var v in rows.Elements("v"))
            {
                if (!byName.TryGetValue(S(v, "displayType"), out var row)) continue;
                int type = next++;
                styles[type] = Copy(row, type, S(v, "asset"));
                uint min = v.Attribute("minRate") != null ? (uint)MathF.Round(F(v, "minRate") * 10000f) : (uint)I(v, "min");
                list.Add((type, min));
            }
        }
    }

    /// <summary>An item's own file in the GMS2 layout: item/{id / 10^7}/{(id / 10^5) % 100:00}/{id}.xml; null when there is none.</summary>
    static XElement ItemFile(PackSet packs, int id)
    {
        string entry = $"item/{id / 10000000}/{id / 100000 % 100:D2}/{id}.xml";
        return packs.Exists("Xml", entry) ? GameStrings.Parse(packs.Read("Xml", entry)).Root : null;
    }

    static DropStyle Copy(DropStyle s, int type, string asset) => new()
    {
        TypeId = type, RestHeight = s.RestHeight, Scale = s.Scale, GetAniSpeed = s.GetAniSpeed, DropAniSpeed = s.DropAniSpeed, BeAniSpeed = s.BeAniSpeed,
        ShadowBias = s.ShadowBias, ShadowScale = s.ShadowScale, BounceHeight = s.BounceHeight, BouncePeriod = s.BouncePeriod,
        GetAni = s.GetAni, DropAni = s.DropAni, BeAni = s.BeAni, BeEffect = s.BeEffect, AssetName = asset, DropSoundId = s.DropSoundId, GetSoundId = s.GetSoundId,
    };

    static DropStyle Row(XElement v, int type) => new()
    {
        TypeId = type, Scale = F(v, "scale", 1f), RestHeight = F(v, "bias", 50f), ShadowScale = F(v, "shadowscale"), ShadowBias = F(v, "shadowbias"),
        DropAni = V(v, "dropani"), DropAniSpeed = F(v, "dropanispeed"), BeAni = V(v, "beani"), BeAniSpeed = F(v, "beanispeed"),
        GetAni = V(v, "getani"), GetAniSpeed = F(v, "getanispeed"), BounceHeight = F(v, "bounceheight", 30f), BouncePeriod = F(v, "bounceperiod", 1f),
        BeEffect = S(v, "beeffect"), DropSoundId = S(v, "dropSoundID"), GetSoundId = S(v, "getSoundID"), AssetName = S(v, "assetName"),
    };

    public ItemDropTable(XElement root) { Load(root); }

    void Load(XElement root)
    {
        if (root == null) return;
        foreach (var v in root.Elements("dropStyle").SelectMany(x => x.Elements("v")))
        {
            var s = Row(v, I(v, "typeID"));
            styles[s.TypeId] = s;
        }
        foreach (var item in root.Elements("dropItemAsset").SelectMany(x => x.Elements("Item")))
        {
            int id = I(item, "itemID");
            if (id == 0) continue;
            if (!assets.TryGetValue(id, out var list)) assets[id] = list = new();
            foreach (var v in item.Elements("v")) list.Add((I(v, "typeID"), (uint)I(v, "min")));
        }
        foreach (var n in root.Elements("gradeNif")) gradeNif[I(n, "id")] = S(n, "nif");
        foreach (var n in root.Elements("gradePetNif")) gradePetNif[I(n, "id")] = S(n, "nif");
        foreach (var n in root.Elements("gradeEffect")) gradeEffect[I(n, "id")] = S(n, "effect");
    }

    /// <summary>Every style row, for the data checks.</summary>
    public IEnumerable<DropStyle> Styles => styles.Values;
    /// <summary>Every asset row (item id, style type, minimum amount), for the data checks.</summary>
    public IEnumerable<(int ItemId, int TypeId, uint Min)> AssetRows => assets.SelectMany(a => a.Value.Select(r => (a.Key, r.TypeId, r.Min)));
    /// <summary>The grade keys of the grade nifs, pet grade nifs and grade effects, for the data checks.</summary>
    public IEnumerable<int> Grades => gradeNif.Keys.Union(gradePetNif.Keys).Union(gradeEffect.Keys).OrderBy(g => g);

    /// <summary>GetDropEffect: the style of a type; null when there is none.</summary>
    public DropStyle Style(int type) => styles.TryGetValue(type, out var s) ? s : null;

    /// <summary>
    /// GetItemDropEffect (itemdatatable.cpp:335): the item's rows are walked in order and the last whose minimum is at most the
    /// amount decides the style type (FUN_140543550 = min &lt;= n); null for an item with no row or no row that fits.
    /// </summary>
    public DropStyle ForItem(int itemId, int amount)
    {
        if (!assets.TryGetValue(itemId, out var rows)) return null;
        int type = -1;
        foreach (var r in rows) if (r.Min <= (uint)amount) type = r.TypeId;
        return type < 0 ? null : Style(type);
    }

    /// <summary>
    /// CDropItem::InitDrop (dropitem.cpp:235) after the owner test: a meso's look is the row of item 90000001 for its amount; the four special
    /// items (88, 89, 8a, 93 of 0x55d4a..) take their own row; a pet item the style 2 with the pet grade nif; any other item the style 1 with the
    /// grade nif. Null where the client's InitDrop returns 0 (no row, no style), and no drop is made.
    /// </summary>
    public DropLook Look(int itemId, int amount, int rarity, bool petItem)
    {
        if (itemId is 0x55d4a81 or 0x55d4a82 or 0x55d4a83)
            return ForItem(0x55d4a81, amount) is { } m ? new DropLook(m, m.AssetName, true) : null;
        if (itemId is 0x55d4a88 or 0x55d4a89 or 0x55d4a8a or 0x55d4a93)
            return ForItem(itemId, amount) is { } sp ? new DropLook(sp, sp.AssetName, false) : null;
        if (petItem) return Style(PetStyle) is { } ps ? new DropLook(ps, GradePetNif(rarity), false) : null;
        return Style(ItemStyle) is { } st ? new DropLook(st, GradeNif(rarity), false) : null;
    }

    public string GradeNif(int grade) => gradeNif.TryGetValue(grade, out var s) ? s : "";
    public string GradePetNif(int grade) => gradePetNif.TryGetValue(grade, out var s) ? s : "";
    public string GradeEffect(int grade) => gradeEffect.TryGetValue(grade, out var s) ? s : "";

    static string S(XElement e, string name) => (string)e.Attribute(name) ?? "";
    static int I(XElement e, string name) => int.TryParse((string)e.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
    static float F(XElement e, string name, float def = 0f) => float.TryParse((string)e.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : def;
    static float[] V(XElement e, string name)
    {
        var r = new float[3];
        var s = (string)e.Attribute(name);
        if (s == null) return r;
        var p = s.Split(',', StringSplitOptions.TrimEntries);
        for (int i = 0; i < 3 && i < p.Length; i++) r[i] = float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0f;
        return r;
    }
}
