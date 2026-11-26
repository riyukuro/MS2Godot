using System.Text;
using Ms2.Quest;

namespace Ms2.Ui.Items;

/// <summary>
/// The tooltip's "itemStatus" member (client/ms2/astooltip.cpp): the dispatcher FUN_1409c7fc0 (:305), the option block
/// FUN_1409c9170 (:1102) and the compact count block FUN_1409cc090 (:1494). The texts are the unit statics
/// FUN_1409e5480 (:644) loads.
/// STILL-OPEN, left out (no "itemStatus" member): pets (the pet table, FUN_1405cb900 and the pet exp gauge are not
/// ported), merge items (subtype 2, FUN_1409cb7b0), the additional-effect rows of a data that has them
/// (SkillDataTableCli::FUN_140aafef0, IsVisibleItemOption and GetBuffName; the data's options, enchant and unlimited
/// blocks are still shown), a skill level-up tier special option (0x4d-0x5a, FUN_140630ea0), and the compact block of
/// an item whose socket max count comes from Lua (no socketDataId).
/// </summary>
public sealed partial class ItemTooltip
{
    /// <summary>s_wsKindLabel (FUN_1409e5480): kinds 0, 2 and 4 have a label; 1 and 3 are empty.</summary>
    string KindLabel(int kind) => kind switch
    {
        0 => Common("s_tooltip_itemoption_kinds_constant"),                // 0xbf3
        2 => Common("s_tooltip_itemoption_kinds_random"),                  // 0xbf4
        4 => Common("s_tooltip_itemoption_kinds_merge_1step"),
        _ => "",
    };

    /// <summary>StringFormat(s_wsText0xbf8, value): one option row.</summary>
    string StatusRow(string value) => ClientFormat.Format(Common("s_tooltip_itemoption_stat_row"), value);

    /// <summary>
    /// FUN_1409c7cc0 (astooltip.cpp:871), "itemMovie": without flag 0x40, the item data's movie, set even when empty. A UGC
    /// item (type 0x16) uses its UGC look's movie instead, and only when it has UGC info.
    /// STILL-OPEN: that UGC movie (CUgcManager::FUN_1418859f0, the common resource's UGC movie or the download path of the
    /// UGC name); a UGC item sets nothing here.
    /// </summary>
    void SetItemMovie(Item item)
    {
        if ((Flags & 0x40) != 0) return;
        if (item.Data.Type == 0x16) return;
        Data["itemMovie"] = item.Data.Movie;
    }

    /// <summary>
    /// FUN_1409c7fc0 (:305): flag 0x800 without 0x10000 fills the compact block FUN_1409cc090, else the option block
    /// FUN_1409c9170.
    /// </summary>
    void SetItemStatus(Item item)
    {
        if ((Flags & 0x800) != 0 && (Flags & 0x10000) == 0) SetItemStatusCount(item);
        else SetItemStatusOptions(item);
    }

    /// <summary>
    /// FormatRandomOptionValue / FormatRandomSpecialOptionValue (:687, :725). Kind 4 wraps the value in its label; kind 2
    /// marks the top of the roll range (the range's last value, with s_word_stat_max) or a high roll (IsHighRollValue)
    /// as s_tooltip_itemoption_stat_row_best. <paramref name="stat"/> -1 means the <paramref name="special"/> twin.
    /// </summary>
    string RandomOptionRow(Item item, int kind, int stat, int special, int value, string text)
    {
        var o = Tables.ItemOptions;
        if (kind != 2)
            return StatusRow(kind == 4 ? ClientFormat.Format(KindLabel(kind), text) : text);   // FUN_1409e5450: kind == 4
        var range = o.RandomOptionRange(item.Data, item.Rarity, stat, special);
        if (range.Last == value)
            return ClientFormat.Format(Common("s_tooltip_itemoption_stat_row_best"), ClientFormat.Format(Common("s_word_stat_max"), text));   // 0x48d
        if (o.IsHighRoll(item.Data, stat, special, value))
            return ClientFormat.Format(Common("s_tooltip_itemoption_stat_row_best"), text);
        return StatusRow(text);
    }

    /// <summary>FUN_140631600 (itemsocketsystemutil.cpp:2203): a stat value in its value text; a rate stat
    /// (IsStatKindTwo) in tenths (FUN_1409e0bf0), else the int (FUN_1409e0970).</summary>
    string StatValue(int stat, int value)
    {
        var o = Tables.ItemOptions;
        return o.IsStatRate(stat) ? ClientFormat.Format(o.StatValueText(stat), value / 10.0f) : ClientFormat.Format(o.StatValueText(stat), value);
    }

    /// <summary>FUN_140631830 (itemsocketsystemutil.cpp:2227): a special value in its value text; a rate special
    /// (FUN_14081dcd0) in tenths. Null for a skill level-up tier (IsSkillLevelUpTier, 0x4d-0x5a: FUN_140630ea0 is not
    /// ported).</summary>
    string SpecialValue(int special, int value)
    {
        var o = Tables.ItemOptions;
        if (o.IsSpecialRate(special)) return ClientFormat.Format(o.SpecialValueText(special), value / 10.0f);
        if (special >= 0x4d && special <= 0x5a) return null;
        return ClientFormat.Format(o.SpecialValueText(special), value);
    }

    /// <summary>
    /// GetEnchantStatText (itemsocketsystemutil.cpp:2255): with a value (BaseOption::HasValue, value != 0), the stat's
    /// option 0 value (CItemStatus::GetStatValue(stat, 0, 0)) times the rate (BaseOption::GetRate, value / 1000); a zero
    /// result formats s_word_stat_increase_near_zero (0x48c) with the stat's common text, else the int in its value text.
    /// </summary>
    string EnchantStatText(Item item, int stat, int value)
    {
        if (value == 0) return "";
        int b = item.Record != null && item.Record.Options.Stat[0].TryGetValue(stat, out var v) ? v : 0;
        long extra = (long)((float)(long)b * ((float)value / 1000.0f));
        if (extra == 0) return ClientFormat.Format(Common("s_word_stat_increase_near_zero"), Tables.StatName(stat));
        return ClientFormat.Format(Tables.ItemOptions.StatValueText(stat), (int)extra);
    }

    /// <summary>
    /// The iteration order of an ATL CAtlMap&lt;int, …&gt; (GetStartPosition / GetNext) built by SetAt in the dictionary's
    /// insertion order: the 17 bins in order (bin = key mod 17), the newest insert first in a bin. The rehash past 38
    /// entries (the high threshold 2.25) is not modelled; an option map is far below it.
    /// </summary>
    internal static List<int> AtlMapOrder(IEnumerable<int> keys)
    {
        var bins = new List<int>[17];
        foreach (int k in keys) (bins[(uint)k % 17] ??= new()).Insert(0, k);
        var order = new List<int>();
        foreach (var b in bins) if (b != null) order.AddRange(b);
        return order;
    }

    /// <summary>
    /// FUN_1409c9170 (:1102), "itemStatus": per option kind 0, 2, 3 and 4 the rows of its stat and special options in
    /// their map order, looked up again by option name (GetStatName / FUN_14081d380, then FindStatByName / FUN_14081d260);
    /// a kind 0 stat the data has (HasStat) is skipped. The kinds (not 4) are joined with s_tooltip_itemoption_kinds_space,
    /// each in its kind label. Then the enchant rows (s_tooltip_itemoption_enchant, 0xbf6; FUN_14058c8a0 is taken as 0),
    /// the enchant gauge when the item can be enchanted (CItemEnchantLimitTable::CanEnchant; itemEnchantExpString /
    /// itemEnchantExpPercent) and the unlimited enchant rows (s_tooltip_itemoption_unlimited).
    /// </summary>
    void SetItemStatusOptions(Item item)
    {
        var d = item.Data;
        if (d.Subtype == 2) return;                         // FUN_1409cb7b0: STILL-OPEN
        if (d.IsPetItem) return;                            // STILL-OPEN, see the class comment
        var o = Tables.ItemOptions;
        var rec = item.Record;
        var kinds = new StringBuilder[5];
        for (int i = 0; i < 5; i++)
        {
            kinds[i] = new StringBuilder();
            if (i == 1) continue;
            var stats = rec?.Options.Stat[i] ?? new Dictionary<int, int>();
            var specials = rec?.Options.Special[i] ?? new Dictionary<int, int>();
            var texts = new List<string>();                 // Unk_OptionTextMap: by insertion index
            foreach (int s in AtlMapOrder(stats.Keys)) texts.Add(Tables.StatOption(s));
            foreach (int s in AtlMapOrder(specials.Keys)) texts.Add(o.SpecialOption(s));
            foreach (var name in texts)
            {
                int stat = o.FindStatByName(name);
                if (stat != -1)
                {
                    if (!stats.TryGetValue(stat, out int v)) continue;
                    if (i == 0 && d.HasStat(stat)) continue;
                    kinds[i].Append(RandomOptionRow(item, i, stat, 0, v, StatValue(stat, v)));
                    continue;
                }
                int special = o.FindSpecialByName(name);
                if (special == 0) continue;
                if (!specials.TryGetValue(special, out int sv)) continue;
                var value = SpecialValue(special, sv);
                if (value == null) return;                  // STILL-OPEN: FUN_140630ea0
                kinds[i].Append(RandomOptionRow(item, i, -1, special, sv, value));
            }
        }
        // Flag 0x10000 adds a row only for a pet; FUN_140526390's additional effect rows are left out above.
        string separator = Common("s_tooltip_itemoption_kinds_space");   // 0xbf5
        var text = new StringBuilder();
        for (int k = 0; k < 5; k++)
        {
            if (k == 4 || kinds[k].Length == 0) continue;
            if (separator.Length != 0 && text.Length != 0) text.Append(separator);
            text.Append(ClientFormat.Format(KindLabel(k), kinds[k].ToString()));   // GetOptionKindLabel: not a pet
        }

        var enchant = rec?.Enchant;
        var enchantOptions = enchant?.Options ?? new Dictionary<int, int>();
        if (enchantOptions.Count > 0)
        {
            var rows = new StringBuilder();
            foreach (int s in AtlMapOrder(enchantOptions.Keys)) rows.Append(StatusRow(EnchantStatText(item, s, enchantOptions[s])));
            if (rows.Length != 0)
            {
                if (text.Length != 0) text.Append(separator);
                text.Append(ClientFormat.Format(Common("s_tooltip_itemoption_enchant"), rows.ToString()));   // 0xbf6
            }
        }
        if (o.CanEnchant(item))
        {
            int charge = enchant?.Charge ?? 0, version = enchant?.Version ?? 0;
            int percent = charge * 100 / 10000;
            int need = 0, cur = 0;
            int step = ItemOptionTables.EnchantStep(item.EnchantLevel, charge, version);   // FUN_140528f90
            if (step > 0)
            {
                need = (int)(10000.0f / step + 0.5f);
                cur = charge / step;
            }
            string exp = ClientFormat.Format("{0} / {1}", cur, need);
            // NeedsChargeVersionUpdate: a charge read with a version other than the current one (1).
            string label = version != 1 && charge != 0 ? Common("s_tooltip_itemoption_enchant_exp_refund")
                : ClientFormat.Format(Common("s_tooltip_itemoption_enchant_exp"));
            if (text.Length != 0) text.Append(separator);
            text.Append(label).Append("gauge:\n\n");
            Data["itemEnchantExpString"] = exp;
            Data["itemEnchantExpPercent"] = percent;
        }

        var limit = rec?.LimitBreak;
        if (limit != null && limit.Level > 0)
        {
            var rows = new StringBuilder();
            foreach (var (s, v) in limit.Stat.OrderBy(p => p.Key))    // std::map: by key
                rows.Append(StatusRow(s == 0x1c || s == 0x1b || s == 0x14 ? EnchantStatText(item, s, v) : StatValue(s, v)));
            foreach (var (s, v) in limit.Special.OrderBy(p => p.Key))
            {
                var value = SpecialValue(s, v);
                if (value == null) return;                  // STILL-OPEN: FUN_140630ea0
                rows.Append(StatusRow(value));
            }
            if (rows.Length != 0)
            {
                if (text.Length != 0) text.Append(separator);
                text.Append(ClientFormat.Format(Common("s_tooltip_itemoption_unlimited"), limit.Level, rows.ToString()));
            }
        }
        Data["itemStatus"] = text.ToString();
    }

    /// <summary>
    /// FUN_1409cc090 (:1494), "itemStatus" in the compact tooltip: the option count (the kind 0 options, less the ones
    /// the data has (2 for stats 0x1b / 0x1c, else 1; the last such key wins), plus the random option pick count
    /// CItem::FUN_14052a060) as s_maid_recipe_item_option (0x7a8), and the socket max count
    /// (s_itemsocketsystem_socket_maxcount) unless flag 0x4000 is set, each in white.
    /// </summary>
    void SetItemStatusCount(Item item)
    {
        var d = item.Data;
        if (d.IsPetItem || d.AdditionalEffectIds.Count > 0) return;   // STILL-OPEN, see the class comment
        var rec = item.Record;
        var stats = rec?.Options.Stat[0] ?? new Dictionary<int, int>();
        int specials = rec?.Options.Special[0].Count ?? 0;
        int skip = 0;
        foreach (int s in AtlMapOrder(stats.Keys))
            if (d.HasStat(s)) skip = s == 0x1b || s == 0x1c ? 2 : 1;
        int picks = Tables.ItemOptions.PickCount(d, item.Rarity) ?? 0;
        uint total = (uint)(stats.Count + specials - skip + picks);
        const string white = "<font color='#ffffff'>{0}</font>";
        var text = new StringBuilder();
        if (total >= 1) text.Append(ClientFormat.Format(white, ClientFormat.Format(Common("s_maid_recipe_item_option"), total)));
        uint? max = Tables.ItemSockets.GetMaxSocketCount(d, item.Rarity, model.IsFeatureEnabled);
        if (max == null) return;                            // STILL-OPEN: the Lua socket max count
        if (max > 0 && (Flags & 0x4000) == 0)
        {
            if (text.Length != 0) text.Append('\n');
            text.Append(ClientFormat.Format(white, CommonF("s_itemsocketsystem_socket_maxcount", max.Value)));
        }
        Data["itemStatus"] = text.ToString();
    }
}
