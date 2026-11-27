namespace Ms2.Ui.Items;

/// <summary>
/// CItemRemakeScrollData (placeholder/citemremakescrolldata.h): one &lt;remakeScroll&gt; row of Table/itemRemakeScroll.xml as
/// CItemRemakeScrollTable::Load (@0x140615700, itemremakescrolldata.cpp:148) reads it: minLv and maxLv (unsigned short), the
/// rank, slot and item lists (XmlTokenList), onlyPet, and the enum attributes representOpValue (the option lock),
/// addOpKind and addOpValue (XmlValue).
/// </summary>
public sealed record ItemRemakeScrollData(int LevelMin, int LevelMax, uint[] Ranks, uint[] Slots, int PetKind, uint[] Items,
    int LockOption, int OptionKind, int OptionValue);

/// <summary>The remake scroll checks of commonfiles/itemremakescrolldata.cpp.</summary>
public static class ItemRemakeScroll
{
    /// <summary>The codes of <see cref="Check"/>: no item or not a pet, no item data, the options do not allow it, the slot,
    /// the rank, the level, remake disabled, not in the item list, no scroll, no option value.</summary>
    public const int Ok = 0, NoItem = 1, NoData = 2, NoOption = 3, WrongSlot = 4, WrongRank = 5, WrongLevel = 6,
        RemakeDisabled = 7, NotListed = 8, NoScroll = 10, NoOptionValue = 0xb;

    /// <summary>
    /// FUN_140616170 (@0x140616170, itemremakescrolldata.cpp:283): whether the scroll can remake the item. In order: no item 1,
    /// no scroll 10, no data 2, remakeDisable 7, no option record 3; an item list that does not hold the item id 8, else a pet
    /// scroll (onlyPet) on a non-pet 1, else a slot list that does not hold the equip subtype 4; a rank not listed 5; a
    /// levelLimit outside minLv-maxLv 6; addOpValue 0 11. Then the group 2 options: none 3; each stat and special must be in
    /// the item's random option (GetRandom of FUN_14053eff0 at the item's rarity: HasStat, HasSpecialStat), else 3; and on an
    /// armour piece (FUN_14053d600) more than one of them in the exclusive group (FindOptionNameList) is 3. Else 0.
    /// STILL-OPEN: the client dereferences a missing random option (GetRandom null) without a check; here that is 3.
    /// </summary>
    public static int Check(Item item, ItemRemakeScrollData scroll, ItemOptionTables tables)
    {
        if (item == null) return NoItem;
        if (scroll == null) return NoScroll;
        var d = item.Data;
        if (d == null) return NoData;
        if (d.RemakeDisable) return RemakeDisabled;
        var options = item.Record?.Options;
        if (options == null) return NoOption;
        if (scroll.Items.Length != 0)
        {
            if (Array.IndexOf(scroll.Items, (uint)d.Id) == -1) return NotListed;
        }
        else if (scroll.PetKind != 0)
        {
            if (!d.IsPetItem) return NoItem;
        }
        else if (Array.IndexOf(scroll.Slots, (uint)d.EquipSubtype) == -1) return WrongSlot;
        if (Array.IndexOf(scroll.Ranks, (uint)item.Rarity) == -1) return WrongRank;
        int level = d.LevelLimit;
        if (level < scroll.LevelMin || level > scroll.LevelMax) return WrongLevel;
        if (scroll.OptionValue == 0) return NoOptionValue;
        bool exclusive = IsArmor(d);
        int code = ItemOptionTables.RandomCode(d, item.Rarity);
        if (options.Stat.Length <= 2) return NoOption;
        var specials = options.Special[2];
        var stats = options.Stat[2];
        if (specials.Count == 0 && stats.Count == 0) return NoOption;
        int count = 0;
        foreach (int stat in stats.Keys)
        {
            if (tables.RandomHas(code, stat, 0) != true) return NoOption;
            if (exclusive && tables.IsExclusiveOptionName(tables.StatOptionName(stat))) count++;
        }
        foreach (int special in specials.Keys)
        {
            if (tables.RandomHas(code, -1, special) != true) return NoOption;
            if (exclusive && tables.IsExclusiveOptionName(tables.SpecialOption(special))) count++;
        }
        return count > 1 ? NoOption : Ok;
    }

    /// <summary>CItemData::FUN_14053d600 (itemdata.cpp): an armour equip subtype, 0xd-0x11 or 0x16.</summary>
    public static bool IsArmor(ItemData d) => d.EquipSubtype is (>= 0xd and <= 0x11) or 0x16;
}
