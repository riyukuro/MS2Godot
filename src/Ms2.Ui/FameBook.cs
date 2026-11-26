using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// The fame state the client keeps from UserEnv (CSceneStateGame::OnUserEnv, scenestategame.cpp:4491-4526): the player's
/// fame points per alliance (DAT_143ab3220, a CAtlMap alliance -> points; modes 0xa whole and 0xb one) and the fame log
/// (DAT_143ab31e8, a CRBMap time -> fameLog id, so it walks in time order; modes 0xc whole and 0xd one). Session-lived.
/// </summary>
public sealed class FameBook
{
    readonly Dictionary<int, int> points = new();           // DAT_143ab3220
    readonly SortedDictionary<long, int> log = new();       // DAT_143ab31e8

    /// <summary>What a UserEnv message changed.</summary>
    public enum Change
    {
        None,
        /// <summary>Mode 0xa or 0xb: the points.</summary>
        Points,
        /// <summary>Mode 0xc: the whole log.</summary>
        Log,
        /// <summary>Mode 0xd: one log entry, with the s_fame_point_update chat line (FUN_1412751b0(text, 0x14)).</summary>
        LogEntry,
    }

    /// <summary>OnUserEnv modes 0xa-0xd: 0xa and 0xc RemoveAll first; each entry SetAt.</summary>
    public Change Apply(UserEnvMessage m)
    {
        switch (m)
        {
            case UserEnvMessage.FameByAlliance f:
                if (f.Replace) points.Clear();
                foreach (var (a, v) in f.Fame) points[a] = v;
                return Change.Points;
            case UserEnvMessage.FameLog l:
                if (l.Replace) log.Clear();
                foreach (var (t, id) in l.LogIds) log[t] = id;
                return l.Replace ? Change.Log : Change.LogEntry;
        }
        return Change.None;
    }

    /// <summary>GetFamePoints: the alliance's points in DAT_143ab3220, 0 without an entry.</summary>
    public int GetFamePoints(int alliance) => points.TryGetValue(alliance, out int v) ? v : 0;

    public IReadOnlyDictionary<int, int> Points => points;

    /// <summary>The log in time order (key = the entry's time in Unix seconds, value = its fameLog id).</summary>
    public IEnumerable<KeyValuePair<long, int>> Log => log;
}

/// <summary>
/// The fame texts of questmasktable.cpp (src/ms2/commonfiles) over <see cref="FameTable"/> and <see cref="FameBook"/>, which
/// the fame point board and the fame contents notice read.
/// </summary>
public sealed class FameTexts
{
    public FameTable Table = FameTable.Empty;
    public FameBook Book = new();
    /// <summary>GetStringCommonByKey / GetStringByKey.</summary>
    public Func<string, string> CommonByKey = _ => "";

    string Common(string key) => CommonByKey(key) ?? "";

    /// <summary>GetAllianceName (@0x140762de0): the common string s_quest_alliance_{Name_14021a6b0(alliance)}.</summary>
    public string AllianceName(int alliance) => Common("s_quest_alliance_" + FameTable.AllianceName(alliance));

    /// <summary>GetAllianceSymbol (@0x140762e90): CFameTable::GetSymbol.</summary>
    public string AllianceSymbol(int alliance) => Table.GetSymbol(alliance);

    /// <summary>FUN_140762ee0 (@0x140762ee0): the grade of the points (GetGradeByPoint) through FUN_140762f50 (@0x140762f50),
    /// the common string s_fame_grade_{grade}.</summary>
    public string GradeName(int alliance, int point) => Common("s_fame_grade_" + Table.GetGradeByPoint(alliance, point));

    /// <summary>The player's grade name for the alliance (FUN_140762ee0 of GetFamePoints).</summary>
    public string GradeName(int alliance) => GradeName(alliance, Book.GetFamePoints(alliance));

    /// <summary>
    /// GetFamePointProgress (@0x140762fc0, questmasktable.cpp:1031): at the alliance's max grade 1 / 1 and the common
    /// s_fame_point_board_max; else the points into the grade over the grade's span, and "{0} / {1}" of the two
    /// grouped (FUN_140763e10).
    /// </summary>
    public (int Current, int Max, string Text) Progress(int alliance)
    {
        int point = Book.GetFamePoints(alliance);
        int grade = Table.GetGradeByPoint(alliance, point);
        if (grade == Table.GetMaxGrade(alliance)) return (1, 1, Common("s_fame_point_board_max"));
        int start = Table.GetGradePoint(alliance, grade);
        int next = Table.GetNextGradePoint(alliance, grade);
        int cur = unchecked(point - start), max = unchecked(next - start);
        return (cur, max, AchieveTexts.Group(cur) + " / " + AchieveTexts.Group(max));
    }

    /// <summary>
    /// UIFamePointBoardDialog::FUN_140c156d0 (@0x140c156d0): some alliance of fame.xml has its season's requireQuest
    /// rewarded (the game menu's fame board entry opens only then).
    /// </summary>
    public bool AnyAllianceOpen(Func<long, bool> questRewarded)
    {
        foreach (int a in Table.AllianceIds)
            if (questRewarded(Table.GetSeasonRequireQuest(a))) return true;
        return false;
    }
}
