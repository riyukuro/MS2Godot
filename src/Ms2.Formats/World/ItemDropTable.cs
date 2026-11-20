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
        Load(GameStrings.Parse(packs.Read("Xml", entry)).Root);
    }

    public ItemDropTable(XElement root) { Load(root); }

    void Load(XElement root)
    {
        if (root == null) return;
        foreach (var v in root.Elements("dropStyle").SelectMany(x => x.Elements("v")))
        {
            var s = new DropStyle
            {
                TypeId = I(v, "typeID"), Scale = F(v, "scale", 1f), RestHeight = F(v, "bias", 50f), ShadowScale = F(v, "shadowscale"), ShadowBias = F(v, "shadowbias"),
                DropAni = V(v, "dropani"), DropAniSpeed = F(v, "dropanispeed"), BeAni = V(v, "beani"), BeAniSpeed = F(v, "beanispeed"),
                GetAni = V(v, "getani"), GetAniSpeed = F(v, "getanispeed"), BounceHeight = F(v, "bounceheight", 30f), BouncePeriod = F(v, "bounceperiod", 1f),
                BeEffect = S(v, "beeffect"), DropSoundId = S(v, "dropSoundID"), GetSoundId = S(v, "getSoundID"), AssetName = S(v, "assetName"),
            };
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
