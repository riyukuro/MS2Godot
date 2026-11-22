using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui.Items;

/// <summary>
/// CItemLapenshardTable (ms2/commonfiles/itemlapensharddata.cpp), the part the item tooltip reads. Load (:188) reads
/// Table/itemLapenshardUpgrade.xml (the locale folder first) on first use: each available &lt;key&gt; with an ItemId and
/// its EquipPart mask (<see cref="GemstoneUpgradeTable.EquipPartMask"/>, the constructor's 0); a later row of an id
/// replaces an earlier one (SetAt).
/// </summary>
public sealed class LapenshardTable
{
    readonly PackSet packs;
    Dictionary<int, uint> masks;

    public static readonly LapenshardTable Empty = new(null);

    public LapenshardTable(PackSet packs) { this.packs = packs; }

    /// <summary>Get (:339)'s m_nMask: 1 red, 2 blue, 4 green; null without a row.</summary>
    public uint? Mask(int itemId)
    {
        if (masks == null)
        {
            masks = new();
            foreach (var p in packs != null && TablePath.Resolve(packs, "itemlapenshardupgrade.xml") is { } lp ? new[] { lp } : Array.Empty<string>())   // GetXmlPath("Table", ...)
            {
                var root = GameStrings.Parse(packs.Read("Xml", p)).Root;
                if (root == null) break;
                foreach (var e in root.Elements("key"))
                {
                    if (!FeatureElements.IsAvailable(packs, e)) continue;
                    int id = ItemData.Atoi((string)e.Attribute("ItemId") ?? "0");
                    if (id == 0) continue;
                    masks[id] = GemstoneUpgradeTable.EquipPartMask(e, 0);
                }
                break;
            }
        }
        return masks.TryGetValue(itemId, out var m) ? m : null;
    }
}
