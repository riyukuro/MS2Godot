namespace Ms2.Ui.Items;

/// <summary>
/// The item level: CItem::UpdateItemLevel (@0x1405290b0, commonfiles/item.cpp:1544) with the CItemData formulas it reads
/// (itemdata.cpp:1670-1830), and the worn total PC::vfunc_126 (@0x141655230, client/ms2/pc.cpp:4172) adds into the record's
/// m_3cc / m_3d0, the Gear Score row of the character window.
/// </summary>
public static class ItemLevels
{
    /// <summary>
    /// UpdateItemLevel: (CItem m_b0, m_b4), the total and the additional level. Not an equipment item (FUN_14053cbc0, type
    /// 1): (0, 0). Else the base (FUN_14053f0d0 of the rarity), the additional FUN_14053f390(base, enchant level) and, with a
    /// plus (unlimited enchant) level, Lua CalcUnlimitedEnchantItemLevel(plus, unlimitedEnchantMaxGrade, equip subtype) on top;
    /// the total is additional + base. Null for a plus level when <paramref name="unlimitedEnchant"/> is not given.
    /// </summary>
    public static (int Total, int Additional)? Of(Item item, Func<int, int, int, int> unlimitedEnchant = null)
    {
        var d = item.Data;
        if (d.Type != 1) return (0, 0);
        int nBase = Base(d, item.Rarity);
        int additional = Additional(d, nBase, item.EnchantLevel);
        int plus = item.UnlimitedEnchantLevel;
        if (plus > 0)
        {
            if (unlimitedEnchant == null) return null;
            additional += unlimitedEnchant(plus, d.UnlimitedEnchantMaxGrade, d.EquipSubtype);
        }
        return (additional + nBase, additional);
    }

    /// <summary>
    /// CItemData::FUN_14053f0d0 (itemdata.cpp:1670): the base level of a grade (the data's rank when set). gearScore 1-69:
    /// (gear * 10 + (grade - 1) * 5) * factor, and from grade 4 with gear 50 up that doubled per grade over 3 plus
    /// (gear - 50) * 100 * factor; gearScore 70 up: (2 + the gear's base) * 1030 * the grade's rate * factor. 0 for no gearScore.
    /// </summary>
    public static int Base(ItemData d, int grade)
    {
        int gear = d.GearScore;
        if (d.Rank != 0) grade = d.Rank;
        int sub = d.EquipSubtype;
        double r = 0.0;
        if (0 < gear)
        {
            if (gear < 0x46)
            {
                r = (gear * 10 + (grade - 1) * 5) * SubtypeFactor(sub);
                if (grade >= 4 && gear >= 0x32)
                    r = r * 2.0 * (grade - 3) + (gear - 0x32) * 100 * SubtypeFactor(sub);
            }
            else
            {
                double gearBase = gear switch
                {
                    0x43 => 4.41, 0x46 => 5.2, 0x50 => 19.537, 0x51 => 71.334, 0x52 => 80.0, 0x5a => 61.35, _ => 0.0,
                };
                double rate = grade switch { 4 => 0.558, 5 => 1.2, 6 => 1.9, _ => 0.0 };
                r = (2.0 + gearBase) * 1030.0 * rate * SubtypeFactor(sub);
            }
        }
        return (int)r;
    }

    /// <summary>
    /// CItemData::FUN_14053f390 (itemdata.cpp:1728): the enchant's additional level. gearScore under 70 (an unsigned
    /// compare): base * the low-gear enchant rate for rank 4 up (the data's rank, so 0 for an item of no fixed rank), else 0;
    /// gearScore 70 up: base * the high-gear enchant rate.
    /// </summary>
    public static int Additional(ItemData d, int nBase, int enchant)
    {
        double b = nBase;
        if ((uint)d.GearScore < 0x46) return d.Rank >= 4 ? (int)(b * LowGearEnchantRate(enchant)) : 0;
        return (int)((int)b * HighGearEnchantRate(enchant));
    }

    /// <summary>CItemData::FUN_14053f430 (itemdata.cpp:1749): the equip subtype's share of the level.</summary>
    public static double SubtypeFactor(int sub) => sub switch
    {
        0xc or 0x12 or 0x13 or 0x14 or 0x15 => 0.2,
        0xd => 0.21,
        0xe => 0.32,
        0xf => 0.3,
        0x10 or 0x11 => 0.06,
        0x16 => 0.62,
        _ => 1.0,
    };

    static readonly double[] LowRates = { 0.02, 0.04, 0.06, 0.08, 0.1, 0.14, 0.18, 0.23, 0.29, 0.35, 0.41, 0.47, 0.53, 0.59, 0.65 };
    static readonly double[] HighRates = { 0.02, 0.04, 0.07, 0.1, 0.14, 0.19, 0.25, 0.32, 0.4, 0.5, 0.64, 0.84, 1.12, 1.5, 2.0 };

    /// <summary>CItemData::FUN_14053f4f0 (itemdata.cpp:1770): enchant levels 1-15, else 0.</summary>
    public static double LowGearEnchantRate(int level) => (uint)(level - 1) < 15u ? LowRates[level - 1] : 0.0;
    /// <summary>CItemData::FUN_14053f620 (itemdata.cpp:1810): enchant levels 1-15, else 0.</summary>
    public static double HighGearEnchantRate(int level) => (uint)(level - 1) < 15u ? HighRates[level - 1] : 0.0;

    /// <summary>
    /// PC::vfunc_126's sum for layer 0, the worn items by their slot (m_68): items <paramref name="canUse"/> passes
    /// (PC::CanUseItem, which tests CItem::FUN_140528980 first, so its second test here is the same: <see cref="Usable"/>)
    /// give (m_b0, m_b4);
    /// slot 4 and slot 5 are kept aside, each one-handed weapon there (FUN_14053d510, subtype 0x1f / 0x22) counted; the
    /// others add up. Then by that count: none, the slot 5 levels are added; one (or more than two), they are added divided
    /// by <paramref name="divisor"/> (BothHandGearScorePenaltyDiv), in float as the exe adds them to the unsigned sums; two,
    /// Lua CalcOHItemAdditionalLevel(low, high, low max, high max) of the two hands' levels is added, here
    /// <paramref name="oneHanded"/>. Null when an item level or the Lua part is not known.
    /// </summary>
    public static (uint Total, uint Additional)? Worn(IReadOnlyDictionary<int, Item> layer0, float divisor, Func<Item, bool> canUse,
        Func<int, int, int, int> unlimitedEnchant = null, Func<uint, uint, uint, uint, (int Min, int Max)?> oneHanded = null)
    {
        uint min = 0, max = 0, slot4Min = 0, slot5Min = 0, slot4Max = 0, slot5Max = 0;
        int oneHandedCount = 0;
        foreach (var (slot, item) in layer0)
        {
            if (item == null || !canUse(item)) continue;
            if (Of(item, unlimitedEnchant) is not var (total, additional)) return null;
            uint nMin = (uint)total, nMax = (uint)additional;
            if (slot == 4)
            {
                slot4Min = nMin; slot4Max = nMax;
                if (item.Data.EquipSubtype is 0x1f or 0x22) oneHandedCount++;
            }
            else if (slot == 5)
            {
                slot5Min = nMin; slot5Max = nMax;
                if (item.Data.EquipSubtype is 0x1f or 0x22) oneHandedCount++;
            }
            else
            {
                min += nMin;
                max += nMax;
            }
        }
        switch (oneHandedCount)
        {
            case 0:
                min += slot5Min;
                max += slot5Max;
                break;
            case 2:
                if (oneHanded?.Invoke(Math.Min(slot4Min, slot5Min), Math.Max(slot4Min, slot5Min), Math.Min(slot4Max, slot5Max),
                        Math.Max(slot4Max, slot5Max)) is not var (addMin, addMax))
                    return null;
                min += (uint)addMin;
                max += (uint)addMax;
                break;
            default:
                min = (uint)((float)min + (float)slot5Min / divisor);
                max = (uint)((float)max + (float)slot5Max / divisor);
                break;
        }
        return (min, max);
    }

    /// <summary>CItem::FUN_140528980 (item.cpp:1716) for an equipment item: not expired (CItem::IsExpired, m_80 set and not
    /// after the server time <paramref name="now"/>), or a pet, or a maid summon (the score-item checks after it are for music
    /// scores).</summary>
    public static bool Usable(Item item, long now) =>
        !(item.ExpiryTime > 0 && item.ExpiryTime <= now) || item.Data.IsPetItem || item.Data.HasMaidId;
}
