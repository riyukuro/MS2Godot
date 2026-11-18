using System.Globalization;
using System.Xml.Linq;

namespace Ms2.Quest;

/// <summary>
/// CConditionParam (conditionutil.cpp): a condition's code or target. "dict:" text is a dictionary (only its flag is
/// kept here); otherwise the text is split on "," and " " into items, each an "a-b" range, an "a:b" min and max, or ids.
/// </summary>
public sealed class ConditionParam
{
    public string Text = "";
    public readonly List<(long Start, long End)> Ranges = new();
    public readonly List<long> Ids = new();
    public long Min, Max;
    /// <summary>m_flags: bit 0 ranges, 1 ids, 2 min:max, 3 dictionary.</summary>
    public int Flags;

    /// <summary>
    /// CConditionParam::Parse: a range or a min:max item whose split does not give two numbers is skipped; an ids item
    /// adds each number (_atoi64) and sets the ids flag when the list is not empty.
    /// </summary>
    public static ConditionParam Parse(string text)
    {
        var p = new ConditionParam { Text = text ?? "" };
        if (p.Text.StartsWith("dict:", StringComparison.Ordinal)) { p.Flags |= 8; return p; }
        foreach (var item in FieldMissions.Tokens(p.Text, ", "))
        {
            if (item.Contains('-'))
            {
                var pair = FieldMissions.Tokens(item, "-");
                if (pair.Length != 2) continue;
                p.Ranges.Add((FieldMissions.Int(pair[0]), FieldMissions.Int(pair[1])));
                p.Flags |= 1;
            }
            else if (item.Contains(':'))
            {
                var pair = FieldMissions.Tokens(item, ":");
                if (pair.Length != 2) continue;
                p.Min = FieldMissions.Int(pair[0]);
                p.Max = FieldMissions.Int(pair[1]);
                p.Flags |= 4;
            }
            else
            {
                foreach (var t in FieldMissions.Tokens(item, ",")) p.Ids.Add(FieldMissions.Long(t));
                if (p.Ids.Count > 0) p.Flags |= 2;
            }
        }
        return p;
    }

    /// <summary>GetIdCount: the ids, 0 without the ids flag.</summary>
    public int IdCount => (Flags & 2) != 0 ? Ids.Count : 0;

    /// <summary>GetFirstValue: the first range's start, else the first id, else 0.</summary>
    public long FirstValue => (Flags & 1) != 0 && Ranges.Count > 0 ? Ranges[0].Start : (Flags & 2) != 0 && Ids.Count > 0 ? Ids[0] : 0;
}

/// <summary>CConditionCommonData: one quest condition, its type, goal (value), code and target.</summary>
public sealed class QuestCondition
{
    public int Type;
    public long Goal;
    public ConditionParam Code = new(), Target = new();

    /// <summary>
    /// CConditionCommonData::Load: a condition whose type name is unknown (ConditionTypeFromName 0) does not load. code
    /// and target are parsed unless empty or "0".
    /// </summary>
    public static QuestCondition Load(XElement e)
    {
        int type = ConditionTypes.FromName((string)e.Attribute("type") ?? "");
        if (type == 0) return null;
        var c = new QuestCondition { Type = type };
        if ((string)e.Attribute("value") is { } v) c.Goal = FieldMissions.Long(v);
        string code = (string)e.Attribute("code") ?? "", target = (string)e.Attribute("target") ?? "";
        if (code.Length != 0 && code != "0") c.Code = ConditionParam.Parse(code);
        if (target.Length != 0 && target != "0") c.Target = ConditionParam.Parse(target);
        return c;
    }
}

/// <summary>CQuestUIData: a quest's description row (string/&lt;lang&gt;/questdescription*.xml).</summary>
public sealed class QuestDescription
{
    public string Name = "", Summary = "", Desc = "", Complete = "", DescFameUI = "";
    public string[] Manual = Array.Empty<string>();
    public int[] Counts = Array.Empty<int>();

    /// <summary>FUN_1416e4590's read of one &lt;quest&gt; row.</summary>
    public static QuestDescription Read(XElement q) => new()
    {
        Name = (string)q.Attribute("name") ?? "",
        Summary = (string)q.Attribute("name") ?? "",
        Desc = ((string)q.Attribute("desc") ?? "").Replace("\\n", "\n"),
        Complete = ((string)q.Attribute("complete") ?? "").Replace("\\n", "\n"),
        DescFameUI = ((string)q.Attribute("descFameUI") ?? "").Replace("\\n", "\n"),
        Manual = FieldMissions.Tokens((string)q.Attribute("manual"), "|").Select(s => s.TrimEnd()).ToArray(),
        Counts = FieldMissions.Tokens((string)q.Attribute("count"), "|").Select(FieldMissions.Int).ToArray(),
    };
}

public sealed partial class QuestTable
{
    /// <summary>The description row of a quest (FUN_1416e5f60), null when it has none.</summary>
    public QuestDescription Description(int id) => descriptions.TryGetValue((uint)id, out var d) ? d : null;

    /// <summary>
    /// GetQuestConditionText / CQuestUIData::GetConditionText: the condition's manual text (object words replaced,
    /// FUN_1416e43d0), else its type's formatter (s_condTexts); the value words replaced, then FUN_1416e6900's count
    /// suffix. Empty without a description row, a condition at the index, or a formatter for its type.
    /// </summary>
    /// <param name="state">The character's quest state; null when there is no local player.</param>
    public string ConditionText(int id, int index, IQuestState state)
    {
        var row = Description(id);
        var d = Quest(id);
        if (row == null || d == null || index >= d.Conditions.Count) return "";
        string manual = ManualText(row, index);
        if (manual.Length != 0) return CountSuffix(row, d, index, Words.ReplaceValueWords(manual), state);
        string text = d.Conditions[index].Type switch
        {
            12 => NpcText(d.Conditions[index]),
            26 => TalkText(d.Conditions[index]),
            29 or 30 => InteractText(d.Conditions[index]),
            37 => FieldText(d.Conditions[index]),
            2 or 3 or 4 => ItemText(d.Conditions[index]),
            156 => ItemMoveText(d.Conditions[index]),
            _ => null,
        };
        if (text == null) return "";
        return CountSuffix(row, d, index, Words.ReplaceValueWords(text), state);
    }

    /// <summary>GetConditionTexts: every condition's text in order.</summary>
    public List<string> ConditionTexts(int id, IQuestState state)
    {
        var list = new List<string>();
        var d = Quest(id);
        if (d == null || Description(id) == null) return list;
        for (int i = 0; i < d.Conditions.Count; i++) list.Add(ConditionText(id, i, state));
        return list;
    }

    /// <summary>
    /// CQuestUIData::GetCompleteText: the complete text, else s_quest_replace_satisfied (common string 1511) with the
    /// complete npc's name. Empty without a description row or quest data.
    /// </summary>
    public string CompleteText(int id)
    {
        var row = Description(id);
        var d = Quest(id);
        if (row == null || d == null) return "";
        string complete = Words.ReplaceObjectWords(row.Complete);
        if (complete.Length != 0) return complete;
        return Format(Common("s_quest_replace_satisfied"), Words.NpcFullName(d.CompleteNpc));
    }

    /// <summary>GetManualText: the manual of the condition, object words replaced (FUN_1416e43d0), empty when none.</summary>
    string ManualText(QuestDescription row, int index) => index < row.Manual.Length ? Words.ReplaceObjectWords(row.Manual[index]) : "";

    /// <summary>
    /// FUN_1416e6900: a manual whose count is 0 and a type 37 condition without a manual stay as they are; otherwise,
    /// with a local player, s_quest_desc_with_count (text, current, goal): the goal when the quest is rewarded, else
    /// its progress (GetProgress, 0 past the list), clamped to 0 .. goal; 0 when the character has no record.
    /// </summary>
    string CountSuffix(QuestDescription row, QuestData d, int index, string text, IQuestState state)
    {
        int count = index < row.Counts.Length ? row.Counts[index] : 0;
        bool hasManual = ManualText(row, index).Length != 0;
        if (hasManual && count == 0) return text;
        var cond = d.Conditions[index];
        if (cond.Type == 37 && !hasManual) return text;
        if (state == null) return text;
        long goal = cond.Goal, current = 0;
        var rec = state.Get(d.Id);
        if (rec != null)
        {
            current = rec.IsRewarded ? goal : rec.Progress != null && index < rec.Progress.Length ? rec.Progress[index] : 0;
            if (current < 0) current = 0;
            if (current > goal) current = goal;
        }
        return Format(Common("s_quest_desc_with_count"), text, current.ToString(CultureInfo.InvariantCulture),
                      goal.ToString(CultureInfo.InvariantCulture));
    }

    // ---- the s_condTexts formatters (questuidata.cpp) ----------------------------------------------------------------

    /// <summary>FUN_1416e6f10, type 12: s_quest_replace_npc (1506) with the npc's name.</summary>
    string NpcText(QuestCondition c) =>
        c.Code.IdCount < 1 ? "" : Format(Common("s_quest_replace_npc"), Words.NpcFullName((int)c.Code.FirstValue));

    /// <summary>FUN_1416e7000, type 26: s_quest_replace_talk (1507), its josa picked by the npc's name, with the name.</summary>
    string TalkText(QuestCondition c)
    {
        if (c.Code.IdCount < 1) return "";
        string name = Words.NpcFullName((int)c.Code.FirstValue);
        return Format(Josa(Common("s_quest_replace_talk"), name), name);
    }

    /// <summary>FUN_1416e71a0, types 29 and 30: the stringinteractobjectaction text (table 11) of the code, empty for 0.</summary>
    string InteractText(QuestCondition c)
    {
        if (c.Code.IdCount < 1) return "";
        int id = (int)c.Code.FirstValue;
        return id == 0 ? "" : Words.Get(ReservedWords.InteractObjectAction, id);
    }

    /// <summary>FUN_1416e7270, type 37: s_quest_replace_field (1509), its josa picked by the map name, with the name.</summary>
    string FieldText(QuestCondition c)
    {
        if (c.Code.IdCount < 1) return "";
        string map = Words.Get(ReservedWords.MapName, (int)c.Code.FirstValue);
        return Format(Josa(Common("s_quest_replace_field"), map), map);
    }

    /// <summary>FUN_1416e7470, types 2, 3 and 4: s_quest_replace_item (1510) with the item name.</summary>
    string ItemText(QuestCondition c) =>
        c.Code.IdCount < 1 ? "" : Format(Common("s_quest_replace_item"), Words.Get(ReservedWords.ItemName, (int)c.Code.FirstValue));

    /// <summary>
    /// FUN_1416e7570, type 156: s_quest_replace_item_move (1513), its first josa picked by the item name and the next by
    /// the target box name (table 12), with both.
    /// </summary>
    string ItemMoveText(QuestCondition c)
    {
        if (c.Code.IdCount < 1 || c.Target.IdCount < 1) return "";
        string box = Words.Get(ReservedWords.LiftableTargetBox, (int)c.Target.FirstValue);
        string item = Words.Get(ReservedWords.ItemName, (int)c.Code.FirstValue);
        string format = Josa(Josa(Common("s_quest_replace_item_move"), item), box);
        return Format(format, item, box);
    }

    /// <summary>
    /// FUN_1413fac10: the first "$a, b$" of the text replaced by a when the word's last letter has no final consonant
    /// (GetFinalConsonantType != 1), else b. No "$" leaves the text; an unclosed one, a token that does not split
    /// (on "," and " ") into two parts, or an empty word gives "".
    /// </summary>
    public static string Josa(string text, string word)
    {
        int first = text.IndexOf('$');
        if (first == -1) return text;
        int second = text.IndexOf('$', first + 1);
        if (second == -1) return "";
        string token = text.Substring(first, second - first + 1).Replace("$", "");
        var parts = FieldMissions.Tokens(token, ", ");
        if (parts.Length != 2) return "";
        if (word.Length == 0) return "";
        string josa = ReservedWords.FinalConsonantType(word[^1]) != 1 ? parts[0] : parts[1];
        return text.Remove(first, second - first + 1).Insert(first, josa);
    }

    /// <summary>StringFormat: each "{n}" replaced by argument n in one pass; other text is kept.</summary>
    static string Format(string format, params string[] args)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < format.Length; i++)
        {
            int close = format[i] == '{' ? format.IndexOf('}', i + 1) : -1;
            if (close > i + 1 && int.TryParse(format.AsSpan(i + 1, close - i - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int n)
                && n < args.Length)
            {
                sb.Append(args[n]);
                i = close;
                continue;
            }
            sb.Append(format[i]);
        }
        return sb.ToString();
    }
}
