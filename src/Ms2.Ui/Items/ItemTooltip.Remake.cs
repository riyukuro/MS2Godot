using System.Text;
using Ms2.Quest;

namespace Ms2.Ui.Items;

/// <summary>
/// The Change Attributes option texts (commonfiles/itemremakescrolldata.cpp, Unk_RemakeStat) and the high-roll text of its
/// confirm box (FUN_1409e4950, astooltip.cpp:5272).
/// STILL-OPEN: a pet's group 2 names (CItemOption::FUN_1405cb900, not ported): a pet gives no rows here, so its count is 0; and a
/// skill level-up tier special (0x4d-0x5a, FUN_140630ea0): its value text is empty, the row is still counted.
/// </summary>
public sealed partial class ItemTooltip
{
    /// <summary>
    /// The option names of a group (Unk_OptionTextMap, by insertion row): the stats by GetStatName in their map order, then the
    /// specials by FUN_14081d380 (itemremakescrolldata.cpp:722-737, :872-886). A pet in group 2 takes FUN_1405cb900
    /// (STILL-OPEN): no names.
    /// </summary>
    List<string> RemakeOptionNames(Item item, int group)
    {
        var names = new List<string>();
        var options = item.Record?.Options;
        if (options == null || group < 0 || group >= options.Stat.Length) return names;
        if (item.Data.IsPetItem && group == 2) return names;   // FUN_1405cb900: STILL-OPEN
        foreach (int s in AtlMapOrder(options.Stat[group].Keys)) names.Add(Tables.StatOption(s));
        foreach (int s in AtlMapOrder(options.Special[group].Keys)) names.Add(Tables.ItemOptions.SpecialOption(s));
        return names;
    }

    /// <summary>
    /// GetOptionText (@0x140617ef0, itemremakescrolldata.cpp:706): one row per option of the group. A value at its range's top is
    /// wrapped in s_word_stat_max (0x48d); with <paramref name="final"/> the value also carries its range
    /// (s_itemremake_option_with_range, the _r twin in tenths for a rate); a top or high roll
    /// (IsHighRollValue) row is s_tooltip_itemoption_stat_row_best, else s_tooltip_itemoption_stat_row. Unlike the tooltip, no
    /// stat is skipped for HasStat. <paramref name="count"/> is the number of rows.
    /// </summary>
    internal string RemakeOptionText(Item item, int group, bool final, out int count)
    {
        count = 0;
        var options = item?.Record?.Options;
        if (options == null || group < 0 || group >= options.Stat.Length) return "";
        var o = Tables.ItemOptions;
        var stats = options.Stat[group];
        var specials = options.Special[group];
        string best = Common("s_word_stat_max");                          // 0x48d
        string rowBest = Common("s_tooltip_itemoption_stat_row_best");
        string row = Common("s_tooltip_itemoption_stat_row");
        string range = final ? Common("s_itemremake_option_with_range") : "";
        string rangeR = final ? Common("s_itemremake_option_with_range_r") : "";
        var text = new StringBuilder();
        foreach (var name in RemakeOptionNames(item, group))
        {
            int stat = o.FindStatByName(name);
            int special = 0, v;
            string value;
            bool rate;
            if (stat != -1)
            {
                if (!stats.TryGetValue(stat, out v)) continue;
                value = StatValue(stat, v);                              // FUN_140631600
                rate = o.IsStatRate(stat);                               // IsStatKindTwo
            }
            else
            {
                special = o.FindSpecialByName(name);                     // FUN_14081d260
                if (special == 0) continue;
                if (!specials.TryGetValue(special, out v)) continue;
                value = SpecialValue(special, v) ?? "";                  // FUN_140631830; STILL-OPEN for 0x4d-0x5a
                rate = o.IsSpecialRate(special);                         // FUN_14081dcd0
                stat = -1;
            }
            var r = o.RandomOptionRange(item.Data, item.Rarity, stat, special);
            bool isBest = r.Last == v;
            if (isBest) value = ClientFormat.Format(best, value);
            string shown = range.Length == 0 ? value
                : rate ? ClientFormat.Format(rangeR, value, (float)(r.First / 10.0), (float)(r.Last / 10.0))
                : ClientFormat.Format(range, value, r.First, r.Last);
            bool high = isBest || o.IsHighRoll(item.Data, stat, special, v);
            text.Append(ClientFormat.Format(high ? rowBest : row, shown));
            count++;
        }
        return text.ToString();
    }

    /// <summary>FormatOptionText (@0x140617e50, :698): the option text inside the common text <paramref name="key"/>.</summary>
    internal string RemakeOptionText(string key, Item item, int group, bool final, out int count) =>
        ClientFormat.Format(Common(key), RemakeOptionText(item, group, final, out count));

    /// <summary>
    /// Unk_RemakeStat::FUN_140619290 (@0x140619290, :854): the <paramref name="option"/>-th option of the group as a lock:
    /// <paramref name="index"/> 0 and the stat id for a stat, 1 and the special id for a special. False for a negative or missing
    /// row, or a name that is neither.
    /// </summary>
    internal bool RemakeOptionLock(Item item, int group, int option, out int index, out int value)
    {
        index = value = 0;
        if (item?.Record?.Options == null || option < 0) return false;
        var names = RemakeOptionNames(item, group);
        if (option >= names.Count || string.IsNullOrEmpty(names[option])) return false;
        var o = Tables.ItemOptions;
        int stat = o.FindStatByName(names[option]);
        if (stat != -1) { index = 0; value = stat; return true; }
        int special = o.FindSpecialByName(names[option]);
        if (special != 0) { index = 1; value = special; return true; }
        return false;
    }

    /// <summary>
    /// FUN_1409e4950 (@0x1409e4950, astooltip.cpp:5272): the group 2 rows (FormatRandomOptionValue / its special twin) inside
    /// s_tooltip_itemoption_kinds_random_nohead, empty without rows; <paramref name="best"/> is set when a row is a high roll
    /// (IsHighRollValue). A pet takes FUN_1405cb900 (STILL-OPEN: no rows).
    /// </summary>
    internal string RemakeHighRollText(Item item, out bool best)
    {
        best = false;
        var options = item?.Record?.Options;
        if (item?.Data == null || options == null || options.Stat.Length <= 2) return "";
        var o = Tables.ItemOptions;
        var text = new StringBuilder();
        foreach (var name in RemakeOptionNames(item, 2))
        {
            int stat = o.FindStatByName(name);
            if (stat != -1)
            {
                if (!options.Stat[2].TryGetValue(stat, out int v)) continue;
                if (o.IsHighRoll(item.Data, stat, 0, v)) best = true;
                text.Append(RandomOptionRow(item, 2, stat, 0, v, StatValue(stat, v)));
                continue;
            }
            int special = o.FindSpecialByName(name);
            if (special == 0 || !options.Special[2].TryGetValue(special, out int sv)) continue;
            if (o.IsHighRoll(item.Data, -1, special, sv)) best = true;
            text.Append(RandomOptionRow(item, 2, -1, special, sv, SpecialValue(special, sv) ?? ""));   // STILL-OPEN for 0x4d-0x5a
        }
        return text.Length == 0 ? "" : ClientFormat.Format(Common("s_tooltip_itemoption_kinds_random_nohead"), text.ToString());
    }
}
