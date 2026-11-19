using System.Globalization;
using Ms2.Quest;

namespace Ms2.Ui.Items;

/// <summary>
/// The tooltip's "itemCompare" member (client/ms2/astooltip.cpp:2034, FUN_1409cfbf0): the stat change of equipping the
/// item, MyPC::FUN_141523e60 (mypc.cpp:1444) over the sums of MyPC::FUN_141531470 (mypc.cpp:1545).
/// STILL-OPEN: a gem whose constant rank has createType 3 (LoadItemOptionConstant's Lua values) adds nothing.
/// </summary>
public sealed partial class ItemTooltip
{
    /// <summary>The tooltip owner's equipment when the owner is the local player (m_40 as MyPC); null leaves out
    /// "itemCompare".</summary>
    public WornEquipment OwnerEquipment;
    /// <summary>CUIService m_n128 (FUN_141adcb40): a Shift key is held (uiscenegame.cpp:1128 sets it from LShift / RShift).</summary>
    public Func<bool> ShiftHeld = () => false;

    /// <summary>FUN_1409cfbf0's s_aStatOrder: the stat display order.</summary>
    static readonly int[] CompareStatOrder =
    {
        0x1c, 0x1d, 0x1e, 0x14, 4, 7, 10, 0xe, 0x20, 0, 1, 2, 3, 0x17, 0x18, 0xd, 0xf, 0x11, 0x19, 0x1a, 0x12, 0x10, 0x13,
        0x15, 0x16, 0x1f, 5, 6, 8, 9, 0xb, 0xc,
    };

    /// <summary>FormatTenthsPercent (astooltip.cpp:4650): FormatStringId(0x98e, "{0:0.1f}" of tenths / 10), the built-in
    /// string whose text is the number with a % sign.</summary>
    static string TenthsPercent(int tenths) => ClientFormat.Format("{0:0.1f}", tenths / 10.0f) + "%";

    /// <summary>FormatInt (astooltip.cpp:4618): "{0}".</summary>
    static string IntText(int n) => n.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// FUN_1409cfbf0 (:2034): nothing without the local player as owner, with flag 0x800 or for a non-equip item. Else one
    /// object per non-zero stat change in <see cref="CompareStatOrder"/> (statName GetStatCommonText; statRate for a rate stat,
    /// IsStatKindTwo, else statValue, of the absolute value; up when it rose), then per special 0..0xce with a name
    /// (GetSATypeName) the same (the rate test FUN_14081dcd0), then the item level change as s_common_item_level with its
    /// int in statRate. The array is set even when empty (FUN_1409adbe0).
    /// </summary>
    void SetItemCompare(Item item)
    {
        if (OwnerEquipment == null) return;
        if ((Flags & 0x800) != 0) return;
        if (item.Data.Type != 1) return;
        var o = Tables.ItemOptions;
        var (stat, special, score) = StatChange(item);
        var rows = new List<object>();
        Dictionary<string, object> Row(string name, string key, string value, bool up) =>
            new() { ["statName"] = name, [key] = value, ["up"] = up };
        foreach (int s in CompareStatOrder)
        {
            int v = stat[s];
            if (v == 0) continue;
            rows.Add(o.IsStatRate(s) ? Row(Tables.StatName(s), "statRate", TenthsPercent(Math.Abs(v)), v > 0)
                : Row(Tables.StatName(s), "statValue", IntText(Math.Abs(v)), v > 0));
        }
        for (int s = 0; s < 0xcf; s++)
        {
            string name = Tables.SpecialAbilityName(s);
            if (string.IsNullOrEmpty(name)) continue;
            int v = special[s];
            if (v == 0) continue;
            rows.Add(o.IsSpecialRate(s) ? Row(name, "statRate", TenthsPercent(Math.Abs(v)), v > 0)
                : Row(name, "statValue", IntText(Math.Abs(v)), v > 0));
        }
        if (score != 0) rows.Add(Row(Common("s_common_item_level"), "statRate", IntText(Math.Abs(score)), score > 0));
        Data["itemCompare"] = rows;
    }

    /// <summary>
    /// MyPC::FUN_141523e60 (mypc.cpp:1444): all zero when the owner wears the item (FindItemByUid, any layer). Else the
    /// items it replaces in its layer (1 for a skin item): a weapon (FUN_14053d2f0) of subtype 0x1f / 0x22
    /// (FUN_14053d510) the LH item while Shift is held, else the RH one; a two-handed weapon (FUN_14053d460) LH and RH (RH
    /// once when it is the same item); another weapon (FUN_14053d340) LH when its model has group 4 and RH when it has group
    /// 5; subtype 0x16 (FUN_14053d2b0) the slot 8 and 9 items (the same way); else the item at the slot of
    /// CItem::FUN_140527a20 (the model's first group type). The change is the item's sums less theirs; stat 0x1c becomes
    /// the mean of 0x1b and 0x1c (in float, truncated) and 0x1b is 0.
    /// </summary>
    (int[] Stat, int[] Special, int Score) StatChange(Item item)
    {
        var stat = new int[0x23];
        var special = new int[0xcf];
        var eq = OwnerEquipment;
        if (eq.Wears(item.Uid)) return (stat, special, 0);
        var d = item.Data;
        var models = Tables.ItemModels;
        int kind = d.HasSkinType ? 1 : 0;                                   // CItem::HasSkin
        var old = new List<Item>();
        void Pair(int first, int second)
        {
            var a = eq.At(first, kind);
            var b = eq.At(second, kind);
            if (a != null) old.Add(a);
            if (b != null && !ReferenceEquals(a, b)) old.Add(b);
        }
        if (WornEquipment.IsWeapon(d, models))
        {
            if (d.EquipSubtype is 0x1f or 0x22)
            {
                if (eq.At(ShiftHeld() ? 4 : 5, kind) is { } w) old.Add(w);
            }
            else if (WornEquipment.IsTwoHanded(d, models)) Pair(4, 5);
            else if (WornEquipment.IsOneSlotWeapon(d, models))
            {
                var m = models.Get(d.ItemPreset);                           // GetModelOrEmpty: no model, no groups
                if (m != null && m.HasSlotType(4) && eq.At(4, kind) is { } l) old.Add(l);
                if (m != null && m.HasSlotType(5) && eq.At(5, kind) is { } r) old.Add(r);
            }
        }
        else if (d.EquipSubtype == 0x16) Pair(8, 9);
        else if (eq.At(models.Get(d.ItemPreset)?.FirstSlotType ?? 0, kind) is { } w) old.Add(w);

        var (oldStat, oldSpecial, oldScore) = SumStats(old);
        var (newStat, newSpecial, newScore) = SumStats(new List<Item> { item });
        for (int i = 0; i < 0x23; i++) stat[i] = newStat[i] - oldStat[i];
        for (int i = 1; i < 0xcf; i++) special[i] = newSpecial[i] - oldSpecial[i];
        stat[0x1c] = (int)((stat[0x1c] + stat[0x1b]) * 0.5f);
        stat[0x1b] = 0;
        return (stat, special, newScore - oldScore);
    }

    /// <summary>CItemStatus::IsRatioStat (itemsocketsystemutil.cpp:2125).</summary>
    static bool IsRatioStat(int stat) => stat is 0x1b or 0x1c or 0x14;

    /// <summary>ScaleValue (itemsocketsystemutil.cpp:2288).</summary>
    static long ScaleValue(long value, float rate) => (long)((double)value * (double)rate + 0.000001);

    /// <summary>
    /// CItemStatus::GetStatValue(stat, 3, sub) (itemsocketsystemutil.cpp:1900): the option group sub's value (sub 5: the
    /// five groups, SumStatValue), plus the enchant option and the limit break option (UnlimitedEnchantInfo::FindOption),
    /// each for a ratio stat as ScaleValue of the group 0 value (GetStatValue(stat, 0, 0)) by its rate, value / 1000. 0
    /// without a record (no status).
    /// </summary>
    static int StatusValue(Item item, int s, int sub)
    {
        var rec = item.Record;
        if (rec == null) return 0;
        int v = 0;
        if (sub == 5) { foreach (var g in rec.Options.Stat) if (g.TryGetValue(s, out var x)) v += x; }
        else if (rec.Options.Stat[sub].TryGetValue(s, out var x)) v += x;
        int b = rec.Options.Stat[0].TryGetValue(s, out var b0) ? b0 : 0;
        if (rec.Enchant.Options.TryGetValue(s, out var e))
            v += IsRatioStat(s) ? (int)ScaleValue(b, e / 1000.0f) : e;
        if (rec.LimitBreak.Stat.TryGetValue(s, out var u))
            v += IsRatioStat(s) ? (int)ScaleValue(b, u / 1000.0f) : u;
        return v;
    }

    static int StatusValue(Item item, int s) => StatusValue(item, s, 0);

    /// <summary>
    /// MyPC::FUN_141531470 (mypc.cpp:1545): nothing for no items. Per item its level (CItem m_b0, FUN_140529070) into the
    /// score; stats 0..0x22 CItemStatus::GetStatValue(stat, 3, 5) (itemsocketsystemutil.cpp:1900: the five option groups,
    /// the enchant option and the limit break option, the last two for a ratio stat as ScaleValue of the group 0 value by
    /// their rate, value / 1000); specials 1..0xce GetSpecialValue(special, 3, 5) (:1955: the five groups and the limit
    /// break special); then the socket's gem stats (GetGemStats / GetGemSpecialStats, a non-zero value). The score is the
    /// integer mean over the item count.
    /// </summary>
    (int[] Stat, int[] Special, int Score) SumStats(List<Item> items)
    {
        var stat = new int[0x23];
        var special = new int[0xcf];
        int score = 0;
        if (items.Count == 0) return (stat, special, 0);
        foreach (var it in items)
        {
            score += ItemLevels.Of(it)?.Total ?? 0;
            var rec = it.Record;
            if (rec == null) continue;                                      // GetStatus() == 0
            for (int s = 0; s < 0x23; s++) stat[s] += StatusValue(it, s, 5);
            for (int s = 1; s < 0xcf; s++)
            {
                int v = 0;
                foreach (var g in rec.Options.Special) if (g.TryGetValue(s, out var x)) v += x;   // SumSpecialValue
                if (rec.LimitBreak.Special.TryGetValue(s, out var u)) v += u;                     // FindSecondaryOption
                special[s] += v;
            }
            var (gemStat, gemSpecial) = GemStats(rec.Socket);
            foreach (var (s, v) in gemStat) if (v != 0) stat[s] += v;                            // BaseOption::HasValue
            foreach (var (s, v) in gemSpecial) if (v != 0) special[s] += v;
        }
        return (stat, special, score / items.Count);
    }

    /// <summary>
    /// CItemSocket::RebuildStats (itemsocket.cpp:909): nothing without a max slot or an open slot; else per open slot's gem
    /// (GetGemId not 0) its constant options at its rank (ApplyConstantOptions with GetItemRank, the gem data's rank), the
    /// group 0 stats and specials summed by key (AccumulateStatOptions / AccumulateSpecialOptions).
    /// </summary>
    (Dictionary<int, int> Stat, Dictionary<int, int> Special) GemStats(Ms2.Net.Packets.ItemSocket socket)
    {
        var stat = new Dictionary<int, int>();
        var special = new Dictionary<int, int>();
        if (socket == null || socket.MaxSlot == 0 || socket.Gems.Count == 0) return (stat, special);
        foreach (var gem in socket.Gems)
        {
            if (gem == null || gem.GemId == 0) continue;
            var gd = Tables.Items.Get(gem.GemId);
            if (gd == null) continue;                                       // ApplyConstantOptions: no item data
            if (Tables.ItemOptions.ConstantOptions(gd, gd.Rank) is not { } c) continue;     // a Lua rank: STILL-OPEN
            var (s, sp) = c;
            foreach (var (k, v) in s) stat[k] = stat.GetValueOrDefault(k) + v;
            foreach (var (k, v) in sp) special[k] = special.GetValueOrDefault(k) + v;
        }
        return (stat, special);
    }
}
