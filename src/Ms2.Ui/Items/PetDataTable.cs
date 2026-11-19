using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui.Items;

/// <summary>
/// CPetDataTable (petdata.cpp), the part the item name reads: LoadProperties (@0x140728a70) takes Table/petProperty.xml's
/// &lt;pets&gt; rows under &lt;ms2&gt; by FUN_14028faa0 (one per "code"); CPetData::Load (@0x140728320) skips a row that is not
/// available and reads its code and type (XmlQuery, the constructor's 0 when missing). Read on first use, as Find does.
/// </summary>
public sealed class PetDataTable
{
    readonly PackSet packs;
    Dictionary<uint, int> types;        // m_pets: code -> CPetData m_nType

    public static readonly PetDataTable Empty = new(null);

    public PetDataTable(PackSet packs) { this.packs = packs; }

    Dictionary<uint, int> Types()
    {
        if (types != null) return types;
        types = new();
        if (packs == null || !packs.Exists("Xml", "table/petproperty.xml")) return types;
        var root = GameStrings.Parse(packs.Read("Xml", "table/petproperty.xml")).Root;
        if (root?.Name.LocalName != "ms2") return types;
        foreach (var e in FeatureElements.Collect(packs, root, "pets", "code"))
        {
            uint code = (uint)ItemData.Atoi((string)e.Attribute("code") ?? "");
            int type = ItemData.Atoi((string)e.Attribute("type") ?? "");
            types[code] = type;                                             // m_pets.SetAt(GetId(), spPet)
        }
        return types;
    }

    /// <summary>Find (@0x1407290f0): the pet's m_nType; null when the table has no pet of the id.</summary>
    public int? TypeOf(uint petId) => Types().TryGetValue(petId, out int t) ? t : null;

    /// <summary>The pet ids of the table.</summary>
    public IEnumerable<uint> Ids => Types().Keys;
}
