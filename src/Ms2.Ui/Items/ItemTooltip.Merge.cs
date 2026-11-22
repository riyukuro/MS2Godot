using Ms2.Quest;

namespace Ms2.Ui.Items;

public sealed partial class ItemTooltip
{
    /// <summary>
    /// FUN_1409cd5b0 (astooltip.cpp:1715), "itemMerge": a non-empty GetMergeOptionDescription(item, 1) after
    /// s_tooltip_itemoption_kinds_merge. GetMergeOptionDescription (itemmergeoptiontable.cpp:608) walks the group 4 stats,
    /// then the group 4 specials, in their map order: the value text (FUN_140631600 / FUN_140631830), wrapped when the
    /// value lies in the slot option's top range (FUN_1405c0870 / FUN_1405c0a70) in 0x48d (s_word_stat_max) at the whole
    /// range's max (FUN_1405c0c70 / FUN_1405c0e80), then in s_word_stat_max_color; each through FormatRandomOptionValue /
    /// FormatRandomSpecialOptionValue with group 4; the text is TrimRight'd. The slot is FindMergeData(item id, merge id).
    /// STILL-OPEN: the debug-mode merge id tag.
    /// </summary>
    void SetItemMerge(Item item)
    {
        var opt = item.Record?.Options;
        if (opt == null) return;
        var slot = Tables.MergeOptions.Find(item.Data, Tables.Items.Get(opt.MergeId));
        var text = new System.Text.StringBuilder();
        var stats = opt.Stat[4];
        foreach (int stat in AtlMapOrder(stats.Keys))
        {
            int v = stats[stat];
            string line = MergeMark(slot, o => o.Stat == stat, v, StatValue(stat, v));
            text.Append(RandomOptionRow(item, 4, stat, 0, v, line));
        }
        var specials = opt.Special[4];
        foreach (int special in AtlMapOrder(specials.Keys))
        {
            int v = specials[special];
            string line = MergeMark(slot, o => o.Special == special, v, SpecialValue(special, v) ?? "");
            text.Append(RandomOptionRow(item, 4, -1, special, v, line));
        }
        string s = text.ToString().TrimEnd();
        if (s.Length != 0) Data["itemMerge"] = Common("s_tooltip_itemoption_kinds_merge") + s;     // StringFormat("{0}{1}")
    }

    /// <summary>Each check takes the first slot option of the stat (or special) whose range is set, as the client's loops
    /// pass over an unset one: in the top range, the max mark at the whole range's max and the max colour.</summary>
    string MergeMark(IReadOnlyList<ItemMergeOptionTable.Option> slot, Func<ItemMergeOptionTable.Option, bool> match, int v, string line)
    {
        if (slot == null) return line;
        var top = slot.FirstOrDefault(o => match(o) && ItemMergeOptionTable.IsSet(o.SecondMin, o.SecondMax));
        if (top == null || v < top.SecondMin || v > top.SecondMax) return line;
        var whole = slot.FirstOrDefault(o => match(o) && ItemMergeOptionTable.IsSet(o.FirstMin, o.FirstMax));
        if (whole != null && whole.FirstMax == v) line = ClientFormat.Format(Common("s_word_stat_max"), line);
        return CommonF("s_word_stat_max_color", line);
    }
}
