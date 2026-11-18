using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Quest;

namespace Ms2.Ui;

/// <summary>
/// CAchieveData (commonfiles/achievedata.cpp), the parts the trophy tally reads: the id's type (id / 10000000) and
/// category ((id / 1000000) % 10), the lowest grade and each grade's condition type (0: no condition, which no CAchieve
/// can start on).
/// </summary>
public sealed record AchieveData(int Id, int Type, int Category, int FirstGrade, IReadOnlyDictionary<int, int> GradeConditions)
{
    /// <summary>GetGrade (@0x14015bc40) is not null.</summary>
    public bool HasGrade(int grade) => GradeConditions.ContainsKey(grade);

    /// <summary>
    /// LoadAll (@0x14015c050): Load (@0x14015c270) of each Xml achieve/*.xml file. A file's &lt;achieves&gt; elements are
    /// FUN_14028faa0's (one per id, by locale and feature); one of type 0 is skipped, and a grade condition
    /// CConditionCommonData::Load (conditionutil.cpp:322) rejects (an unknown type name) stops the file there. STILL-OPEN:
    /// Load's Validate (@0x14029edc0) also rejects a type 0x11f condition whose target names a key
    /// CQuestConditionCheckerUserFind has no handler for; that check is not ported, so such a condition is kept.
    /// </summary>
    public static Dictionary<int, AchieveData> LoadAll(PackSet packs)
    {
        var all = new Dictionary<int, AchieveData>();
        if (packs == null) return all;
        bool rejected = false;
        var files = packs.List("Xml").Where(n => n.StartsWith("achieve/", StringComparison.OrdinalIgnoreCase)
                                                 && n.IndexOf('/', 8) < 0 && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(n => n, StringComparer.Ordinal);
        foreach (var file in files)
        {
            XElement root;
            try { root = GameStrings.Parse(packs.Read("Xml", file)).Root; } catch (System.Xml.XmlException) { continue; }
            if (root == null || root.Name != "ms2") continue;
            rejected = false;
            foreach (var e in FeatureElements.Collect(packs, root, "achieves", "id"))
            {
                if (Load(e) is not { } d) { if (rejected) break; continue; }
                all[d.Id] = d;                                                   // s_mapData.SetAt
            }
        }
        return all;

        AchieveData Load(XElement e)
        {
            int id = (int)Atoi(e, "id");
            int type = id / 10000000, category = id / 1000000 % 10;
            if (type == 0) return null;
            int first = int.MaxValue;
            var grades = new Dictionary<int, int>();
            foreach (var g in e.Elements("grade"))
            {
                int grade = (int)Atoi(g, "value");
                if (grade < first) first = grade;
                int condType = 0;
                if (g.Element("condition") is { } c)
                {
                    condType = ConditionTypes.FromName((string)c.Attribute("type") ?? "");
                    if (condType == 0) { rejected = true; return null; }        // Load returned 0: the whole file stops
                }
                grades[grade] = condType;                                       // m_mapGrade.SetAt (a later duplicate wins)
            }
            return new AchieveData(id, type, category, first, grades);
        }
    }

    static long Atoi(XElement e, string attr) => Items.ItemData.Atoi((string)e.Attribute(attr) ?? "0");
}

/// <summary>
/// CAchieveManager (client/ms2/achievemanager.cpp), the part the character window reads: the trophies the player has
/// (m_map) kept from the Achieve packet, and the tally of trophy grades reached per category (m_tally, GetTally), whose
/// Sum is the window's trophy count (UIMyInfoDialog::vfunc_104 @0x141a5ae90).
/// </summary>
public sealed class AchieveBook
{
    sealed class Entry
    {
        public AchieveData Data;
        public byte State;
        public int CurGrade, StartGrade;
        public long Count;
        public AchieveProgress Progress;

        public long GetValue(int grade) => Progress?.GetValue(grade) ?? 0;

        public void Decode(AchieveProgress p)
        {
            State = p.State; CurGrade = p.CurGrade; StartGrade = p.StartGrade; Count = p.Count; Progress = p;
        }
    }

    readonly Func<int, AchieveData> data;
    readonly Func<IEnumerable<AchieveData>> allData;
    readonly Dictionary<int, Entry> map = new();
    readonly int[] tally = new int[3];

    /// <summary><paramref name="data"/>: GetAchieveData (@0x14015d9e0); <paramref name="allData"/>: GetAll (Reload's
    /// list).</summary>
    public AchieveBook(Func<int, AchieveData> data, Func<IEnumerable<AchieveData>> allData)
    {
        this.data = data;
        this.allData = allData;
    }

    /// <summary>A book over a loaded data table.</summary>
    public AchieveBook(IReadOnlyDictionary<int, AchieveData> table)
        : this(id => table.TryGetValue(id, out var d) ? d : null, () => table.Values) { }

    /// <summary>The tally's three category slots (category 1, 2, 3).</summary>
    public IReadOnlyList<int> Tally => tally;

    /// <summary>GetTally()->Sum().</summary>
    public uint Sum => (uint)(tally[0] + tally[1] + tally[2]);

    /// <summary>Whether the player has trophy <paramref name="id"/> (Find @0x140935e60).</summary>
    public bool Has(int id) => map.ContainsKey(id);

    /// <summary>
    /// OnPacket (@0x140936a50). True when the client refreshes the character window (mode 2 on a trophy the player has
    /// whose type is not 8: UIMyInfoDialog::FUN_141a5a560). STILL-OPEN: mode 2's NotifyClear (the clear toast and
    /// system line), NotifyReward, the game menu and main player info refreshes and the DAT_143af0f30 mark are not ported.
    /// </summary>
    public bool Apply(AchieveMessage m)
    {
        switch (m)
        {
            case AchieveMessage.Reload:
                Reload();
                return false;
            case AchieveMessage.Rows rows:
                foreach (var (id, grade, progress) in rows.List)
                {
                    if (Init(id, grade, 0) is not { } a) continue;              // decoded and dropped
                    a.Decode(progress);
                    map[id] = a;                                                // m_map.SetAt
                    Add(a);                                                     // Tally: started or not
                }
                return false;
            case AchieveMessage.Update u:
            {
                if (!map.TryGetValue(u.Id, out var a)) return false;
                int oldGrade = a.CurGrade, oldState = a.State;
                a.Decode(u.Progress);
                if (oldGrade != a.CurGrade || (oldState != 3 && a.State == 3))
                    RebuildTally();                                             // after NotifyClear
                return a.Data.Type != 8;
            }
        }
        return false;
    }

    /// <summary>Reload (@0x140935cd0): every trophy of the data at its lowest grade, state 2, not started; the tally
    /// cleared.</summary>
    void Reload()
    {
        map.Clear();
        Array.Clear(tally);
        foreach (var d in allData())
            if (Init(d.Id, d.FirstGrade, 2) is { } a) map[d.Id] = a;
    }

    /// <summary>
    /// CAchieve::Init (@0x140158350): the data, the grade (or the lowest grade when the data has none of that value) and
    /// CAchieveCondition::Init (@0x140158130 → CConditionCommon::vfunc_3 @0x14029f090: the grade's condition has a type);
    /// null when one is missing. The current grade is the grade, the lowest when 0; the start grade the lowest.
    /// </summary>
    Entry Init(int id, int grade, byte state)
    {
        if (id == 0 || data(id) is not { } d) return null;
        if (!d.HasGrade(grade))
        {
            if (!d.HasGrade(d.FirstGrade)) return null;
            grade = d.FirstGrade;
        }
        if (d.GradeConditions[grade] == 0) return null;
        return new Entry { Data = d, State = state, CurGrade = grade != 0 ? grade : d.FirstGrade, StartGrade = d.FirstGrade };
    }

    /// <summary>Tally (@0x1409388a0): a type 2 trophy adds 1 to its category's slot for each grade from the lowest to the
    /// current one whose value is not 0.</summary>
    void Add(Entry a)
    {
        if (a.Data.Type != 2) return;
        for (int g = a.Data.FirstGrade; g <= a.CurGrade; g++)
        {
            if (a.GetValue(g) == 0) continue;
            if (a.Data.Category is >= 1 and <= 3) tally[a.Data.Category - 1]++;
        }
    }

    /// <summary>RebuildTally (@0x1409389f0): the tally of the started trophies (IsStarted: a count other than 0).</summary>
    void RebuildTally()
    {
        Array.Clear(tally);
        foreach (var a in map.Values)
            if (a.Count != 0) Add(a);
    }
}
