namespace Ms2.Ui.Items;

/// <summary>
/// CItem (ms2/commonfiles/item.cpp), as far as a slot shows it. A new item (<see cref="Create"/>) has the constructor's
/// values (:715): slot -1, count 1, no lock; its status has no enchant. The parts of owned items (UGC, gem and music
/// data, enchant, lock) are fields the owner fills.
/// </summary>
public sealed class Item
{
    public readonly ItemData Data;
    public int Rarity;                 // m_64 (GetRarity :266)
    public uint Count = 1;             // m_6c
    public int Slot = -1;              // m_5c (GetSlot :245)
    public int LockState;              // m_a4 (GetLockState :385)
    public long UnlockTime;            // m_a8 (GetUnlockTime :399), server time
    public int EnchantLevel;           // CItemStatus enchant: CItemEnchantInfo::GetLevel
    public int UnlimitedEnchantLevel;  // CItemStatus UnlimitedEnchantInfo::GetLevel

    Item(ItemData data) { Data = data; }

    /// <summary>
    /// FUN_14052b130 (:2607) through FUN_140523c40 (:2760): null for an unknown item; the rarity is the data's rank, or
    /// <paramref name="rarity"/> when the rank is 0.
    /// </summary>
    public static Item Create(ItemDataTable table, int itemId, int rarity)
    {
        var data = table.Get(itemId);
        if (data == null) return null;
        return new Item(data) { Rarity = data.Rank == 0 ? rarity : data.Rank };
    }

    public int ItemId => Data.Id;

    /// <summary>SetCountClamped (:1171): at most the slot maximum.</summary>
    public void SetCountClamped(int count) => Count = (uint)count < Data.SlotMax ? (uint)count : Data.SlotMax;

    /// <summary>
    /// GetIconPath (:2319): the data's icon path. The other cases (a set name m_strName, UGC look, a music score's
    /// instrument icon, a gem's icon) need the owned item's UGC / music / gem data, which is not ported.
    /// </summary>
    public string IconPath => Data.IconPath;

    /// <summary>GetUgcTag (:2065): 0 without UGC data.</summary>
    public int UgcTag => 0;
}
