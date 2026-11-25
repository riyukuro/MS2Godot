using System.Globalization;
using System.Xml.Linq;

namespace Ms2.Ui.Items;

/// <summary>
/// CBankTable's bank rows (commonfiles/banktable.cpp, LoadBankType @0x1401b47a0: GetXmlPath("Table", "bankType.xml")): per
/// &lt;bankType&gt; its id, useCash and slotCount; a later row with the same id replaces the earlier one (CRBMap::SetAt).
/// GetBankData (@0x1401b4160) finds a row by id. The storeItemType lists (SetSlots, IsStorable) are not read: the only
/// store that asks them (CStore::Allowed) runs with the type cleared.
/// </summary>
public sealed class BankTable
{
    public static readonly BankTable Empty = new(null);

    /// <summary>CBankData.</summary>
    public sealed record Bank(uint Id, int SlotCount, int UseCash);

    readonly Dictionary<uint, Bank> banks = new();

    public BankTable(XDocument doc)
    {
        var root = doc?.Root;
        if (root == null || root.Name.LocalName != "ms2") return;
        foreach (var e in root.Elements("bankType"))
        {
            var b = new Bank(UInt(e, "id"), Int(e, "slotCount"), Int(e, "useCash"));
            banks[b.Id] = b;
        }
    }

    public int Count => banks.Count;

    /// <summary>GetBankData: null without the row.</summary>
    public Bank Get(uint id) => banks.TryGetValue(id, out var b) ? b : null;

    /// <summary>
    /// GetBankSlotCount (@0x1408b6200, ugcmapoptions.cpp:849-862): 0 for item 0, an item without data or a bank without a
    /// row; else the slot count of the item's bank, CItemData::GetBankId (@0x14053dce0, itemdata.cpp:1153-1160): param2 of
    /// an item of type 6 and subtype 6, else 0 (the GMS2 data: the install bankType, <see cref="BankIdOf"/>).
    /// </summary>
    public int SlotCountOf(ItemData item) => item == null ? 0 : Get((uint)BankIdOf(item))?.SlotCount ?? 0;

    /// <summary>
    /// CItemData::GetBankId. The GMS2 client reads the item's install bankType instead, with no type check (GMS2
    /// UIObjectStoreDialog OnLoadComplete @0x140e00550 loads item data +0x2d8 at 0x140e005fd): an item with one takes it.
    /// </summary>
    public static int BankIdOf(ItemData item) => item.InstallBankType != 0 ? item.InstallBankType
        : item.Type == 6 && item.Subtype == 6 ? item.Param2 : 0;

    static int Int(XElement e, string name) =>
        int.TryParse((string)e.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

    static uint UInt(XElement e, string name) =>
        uint.TryParse((string)e.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint v) ? v : 0;
}
