using Ms2.Net.Packets;

namespace Ms2.Ui.Skills;

/// <summary>One skill of a skill book (JobSkill, src/ms2/guess/jobsystem.cpp:37): the packet's level and flags, and its job table row.</summary>
public sealed class JobSkill
{
    public readonly uint Id;
    public ushort Level = 1;
    public bool Changed, Learned, Extra;
    /// <summary>m_spData: the job table row (of the skill, or of the skill it is a sub skill of).</summary>
    public JobSkillData Data;

    public JobSkill(uint id) { Id = id; }

    /// <summary>The copy constructor the simulator snapshots with.</summary>
    public JobSkill Copy() => new(Id) { Level = Level, Changed = Changed, Learned = Learned, Extra = Extra, Data = Data };

    /// <summary>Init @0x140637ba0 (:44): the job's row (none only for an extra skill) and a skill table row.</summary>
    public bool Init(JobData job, SkillTables t)
    {
        Data = job?.GetSkillData(Id);
        if (!Extra && Data == null) return false;
        return t.GetSkillData(Id) != null;
    }

    /// <summary>GetJobLevel @0x140637e00 (:82): the row's rank, 0 without one.</summary>
    public int JobLevel => Data?.JobLevel ?? 0;
    /// <summary>IsSubSkill @0x140637e50 (:90).</summary>
    public bool IsSubSkill => Data != null && Id != Data.SkillId;
    /// <summary>IsLearn @0x140637ec0 (:98).</summary>
    public bool IsLearn => Data?.IsLearn ?? false;

    /// <summary>GetUsedPoint @0x140637ce0 (:62): 0 without skill data or for a sub skill; a learn-with-rank or extra skill spends
    /// level - 1; another spends its level when learnt.</summary>
    public int UsedPoint(SkillTables t)
    {
        if (t.GetSkillData(Id) == null) return 0;
        if (IsSubSkill) return 0;
        if (IsLearn || Extra) return Level - 1;
        return Learned ? Level : 0;
    }
}

/// <summary>
/// The local player's JobSystem (guess/jobsystem.cpp) as JobSystemCli (client/ms2/jobsystemcli.cpp) reads it: the rank, the
/// job and the skills of the Job / FieldAddUser stream (JobSystem::vfunc_7 @0x140638f10). The skill set (CSkillSet), the
/// skill tier options (CSkillTierOptionManager, PC::GetSkillTree) and the lapenshard levels have no feed here; they are
/// the hooks <see cref="SkillSetLevel"/>, <see cref="TierLevel"/>, <see cref="LapenshardLevel"/> (0 = none).
/// </summary>
public sealed class JobSystem
{
    readonly SkillTables t;
    readonly SortedDictionary<uint, JobSkill> skills = new();     // m_skills: std::map by id
    readonly Dictionary<int, int> usedPoints = new();

    public int Rank { get; private set; }
    public JobData Job { get; private set; }
    public int TotalPoint { get; private set; }

    /// <summary>CSkillSet::GetSkillLevel of the player's skill system (0 = not in the set).</summary>
    public Func<uint, ushort> SkillSetLevel = _ => 0;
    /// <summary>CSkillTierOptionManager::GetSkillLevel (skilltieroptionmanager.cpp:133).</summary>
    public Func<uint, ushort> TierLevel = _ => 0;
    /// <summary>MyPC::GetLapenshardSkillLevel, read with feature 0xd3.</summary>
    public Func<uint, ushort> LapenshardLevel = _ => 0;
    public Func<int, bool> IsFeatureEnabled = _ => false;

    public JobSystem(SkillTables tables) { t = tables ?? SkillTables.Empty; }

    /// <summary>JobSystem::vfunc_7 @0x140638f10 (:274): rank, job group, the skills (a skill Init drops is not kept), then RecalcPoints.</summary>
    public void Read(int rank, int jobGroup, IEnumerable<BookSkill> list)
    {
        Rank = rank;
        Job = t.Jobs.GetJobData(jobGroup);
        skills.Clear();
        foreach (var b in list)
        {
            var s = new JobSkill((uint)b.SkillId) { Level = (ushort)b.Level, Changed = b.Changed, Learned = b.Learned, Extra = b.Extra };
            if (!s.Init(Job, t)) continue;
            skills.TryAdd(s.Id, s);
        }
        RecalcPoints();
    }

    public void Read(SkillBook book) => Read(book?.Rank ?? 0, book?.JobGroup ?? 0, book?.Skills ?? Array.Empty<BookSkill>());

    /// <summary>GetJobGroupID @0x140639340 (:319).</summary>
    public int JobGroupId => Job?.JobGroupId ?? 0;
    /// <summary>GetSubJob @0x1406393f0 (:327): the job's sub job of the rank (PC::GetSubJob).</summary>
    public int SubJob => Job?.GetSubJob(Rank) ?? 0;

    /// <summary>FindSkill @0x1406394d0 (:341).</summary>
    public JobSkill FindSkill(uint id) => skills.TryGetValue(id, out var s) ? s : null;

    /// <summary>GetSkills @0x1406395c0 (:351): in id order, learnt ones only or all, sub skills left out or not.</summary>
    public List<JobSkill> GetSkills(bool learnedOnly, bool noSubSkills) =>
        skills.Values.Where(s => (!learnedOnly || s.Learned) && (!noSubSkills || !s.IsSubSkill)).ToList();

    /// <summary>RecalcPoints @0x140639970 (:400): the learnt skills' used points by rank and in total (left as they were when none is learnt).</summary>
    void RecalcPoints()
    {
        var learnt = GetSkills(true, false);
        if (learnt.Count == 0) return;
        TotalPoint = 0;
        usedPoints.Clear();
        foreach (var s in learnt)
        {
            int p = s.UsedPoint(t);
            usedPoints[s.JobLevel] = usedPoints.GetValueOrDefault(s.JobLevel) + p;
            TotalPoint += p;
        }
    }

    /// <summary>GetUsedPoint @0x140639b40 (:427).</summary>
    public int GetUsedPoint(int jobLevel) => usedPoints.GetValueOrDefault(jobLevel);

    /// <summary>
    /// JobSystemCli::vfunc_2 @0x140a6da90 (jobsystemcli.cpp:81): an unknown skill is not learnt; a skill that is not a book
    /// skill kind (SkillData::FUN_1407e9f30: main types 1, 3-5, 7, 10, 11, 13-15, 17-19) is; then the skill set, the tier
    /// option, the lapenshard level (feature 0xd3), the tier of the skill it is a sub skill of; then JobSystem::vfunc_2 (:214),
    /// the book's learnt flag.
    /// </summary>
    public bool IsLearned(uint id)
    {
        var d = t.GetSkillData(id);
        if (d == null) return false;
        if (!IsBookSkillKind(d)) return true;
        if (SkillSetLevel(id) != 0) return true;
        if (TierLevel(id) > 0) return true;
        if (IsFeatureEnabled(0xd3) && LapenshardLevel(id) > 0) return true;
        if (Job != null && Job.FindSubSkill(id) is { } parent && TierLevel(parent.SkillId) > 0) return true;
        return FindSkill(id)?.Learned ?? false;
    }

    /// <summary>SkillData::FUN_1407e9f30 (skilloverlapcompare.cpp:1111).</summary>
    public static bool IsBookSkillKind(SkillData d) => d.MainType is not (1 or 3 or 4 or 5 or 7 or 10 or 11 or 13 or 14 or 15 or 17 or 18 or 19);

    /// <summary>JobSystemCli::vfunc_5 @0x140a6dec0 (:131): the tier level of the skill, else of the skill it is a sub skill of.</summary>
    public ushort BonusLevel(uint id)
    {
        ushort lv = TierLevel(id);
        if (lv != 0) return lv;
        if (Job?.FindSubSkill(id) is not { } parent) return 0;
        return TierLevel(parent.SkillId);
    }

    /// <summary>
    /// JobSystemCli::vfunc_1 @0x140a6d910 (:58): the skill set's level, the lapenshard level (feature 0xd3), else JobSystem::vfunc_1
    /// (:193): a book skill at min(max level, learnt level + bonus), a skill outside the book that is not a book skill kind at level 1.
    /// </summary>
    public SkillLevelData LevelData(uint id)
    {
        ushort set = SkillSetLevel(id);
        if (set != 0) return t.Skills.GetSkillLevelData(id, set);
        if (IsFeatureEnabled(0xd3) && LapenshardLevel(id) is var lp && lp != 0) return t.Skills.GetSkillLevelData(id, lp);
        ushort bonus = BonusLevel(id);
        if (FindSkill(id) is { } s)
        {
            var d = t.GetSkillData(s.Id);
            if (d == null) return null;
            int level = s.Learned ? s.Level : 0;
            return d.GetLevelData(Math.Min((int)d.MaxLevelRaw, level + bonus));
        }
        var other = t.GetSkillData(id);
        return other != null && !IsBookSkillKind(other) ? other.GetLevelData(1) : null;
    }

    /// <summary>MyPC::GetSkillLevel @0x141528b80 (mypc.cpp:2727): the level of <see cref="LevelData"/>, 1 without one.</summary>
    public ushort SkillLevel(uint id) => LevelData(id)?.Level ?? 1;

    /// <summary>
    /// JobSystem::GetLevelSkillPoint @0x140639260 (:301): rank 1 points are min(level, SkillPointLimitLevel1) - 9 from level 10;
    /// rank 2 points min(level, SkillPointLimitLevel2) - 59 from level 60.
    /// </summary>
    public static int GetLevelSkillPoint(int level, int rank, int limit1, int limit2)
    {
        if (level < 10) return 0;
        if (rank == 1) return Math.Min(level, limit1) - 9;
        if (rank == 2) return level < 60 ? 0 : Math.Min(level, limit2) - 59;
        return 0;
    }
}

/// <summary>
/// The skill point state of CSceneStateGame::OnSkillPoint @0x14172f210 (scenestategame.cpp:4241): DAT_143ab32e8, the points by
/// source then rank; DAT_143ab32f8, their sum by rank; and the last i32, which MyPC keeps at m_n179c and the skill window adds
/// to every rank's total (GetRemainingSkillPoints, mypc.cpp:3965; read by "GetCharacterLevel", uiskillbookautodistributedialog.cpp:401).
/// </summary>
public sealed class SkillPoints
{
    public readonly Dictionary<int, Dictionary<int, int>> BySource = new();
    public readonly Dictionary<int, int> ByRank = new();
    public int Extra;

    public void Apply(SkillPointPacket p)
    {
        BySource.Clear();
        ByRank.Clear();
        foreach (var (source, entries) in p.Sources)
        {
            if (!BySource.TryGetValue(source, out var g)) BySource[source] = g = new Dictionary<int, int>();
            foreach (var (rank, value) in entries)
            {
                g[rank] = value;
                ByRank[rank] = ByRank.GetValueOrDefault(rank) + value;
            }
        }
        Extra = p.Extra;
    }

    /// <summary>DAT_143ab32f8[rank].</summary>
    public int RankTotal(int rank) => ByRank.GetValueOrDefault(rank);
    /// <summary>DAT_143ab32e8[source][rank].</summary>
    public int Source(int source, int rank) => BySource.TryGetValue(source, out var g) ? g.GetValueOrDefault(rank) : 0;
}
