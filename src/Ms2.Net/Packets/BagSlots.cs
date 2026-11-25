namespace Ms2.Net.Packets;

/// <summary>
/// The base slot count of an inventory tab, FUN_140a66510 (inventory.cpp:1694, 0x140a66510). Off the feature BagRenewal01 (id 116)
/// it is 48. On, it is the first value of the tab's constants.xml entry (table/constants.xml, key below); the second value is the
/// expand maximum (FUN_140a66750). A missing or empty entry leaves the default 48. The values come from client data, not from the server.
/// </summary>
public static class BagSlots {
    public const int Default = 48;

    /// <summary>The constants.xml key of each tab 0-14, in the order of the switch at 0x140a66510 (fields bagLimit0.. of Client::Constants at +0x74d8, 0x80 apart),
    /// then the GMS2 survival tab (<see cref="ItemBook.SurvivalTab"/>): the GMS2 switch @0x140333bb0, over the wire's tabs 0-16, reads
    /// bagSlotTabSurvivalCount for 14 (+0x81e0) and returns the first value, else 48. Neither data set has that row, so it is 48.</summary>
    public static readonly string[] Keys = {
        "bagSlotTabGameCount", "bagSlotTabSkinCount", "bagSlotTabSummonCount", "bagSlotTabMaterialCount", "bagSlotTabLifeCount",
        "bagSlotTabQuestCount", "bagSlotTabGemCount", "bagSlotTabMiscCount", "bagSlotTabMasteryCount", "bagSlotTabPetCount",
        "bagSlotTabActiveSkillCount", "bagSlotTabCoinCount", "bagSlotTabBadgeCount", "bagSlotTabLapenShardCount", "bagSlotTabPieceCount",
        "bagSlotTabSurvivalCount",
    };

    /// <summary>The count for a tab; <paramref name="bagRenewal"/> is IsFeatureEnabled(116), <paramref name="values"/> the entry's values by key (null = absent).</summary>
    public static int Base(int tab, bool bagRenewal, Func<string, int[]> values) {
        if (!bagRenewal) return Default;
        if ((uint) tab >= (uint) Keys.Length) throw new ArgumentOutOfRangeException(nameof(tab));   // the client asserts (inventory.cpp:2333)
        var v = values(Keys[tab]);
        return v != null && v.Length > 0 ? v[0] : Default;
    }

    /// <summary>The expand maximum (FUN_140a66750, 0x140a66750): the second value, else 48.</summary>
    public static int Max(int tab, bool bagRenewal, Func<string, int[]> values) {
        if (!bagRenewal) return Default;
        if ((uint) tab >= (uint) Keys.Length) throw new ArgumentOutOfRangeException(nameof(tab));
        var v = values(Keys[tab]);
        return v != null && v.Length > 1 ? v[1] : Default;
    }
}
