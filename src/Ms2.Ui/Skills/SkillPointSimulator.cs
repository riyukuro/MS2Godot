namespace Ms2.Ui.Skills;

/// <summary>
/// CSkillPointSimulator (client/ms2/skillpointsimulator.cpp): the skill window's point preview. It keeps a snapshot and an edit
/// copy of the book's skills, the points each rank grants and what the edit spends. The client keeps both copies in CAtlMaps
/// (hash order); here they are in id order, which only changes the order <see cref="AutoDistribute"/> and the Get* lists walk.
/// </summary>
public sealed class SkillPointSimulator
{
    readonly SkillTables t;
    readonly JobSystem player;
    readonly Func<uint, bool> isQuestRewarded;
    readonly SortedDictionary<uint, JobSkill> snapshot = new(), edit = new();
    readonly Dictionary<int, int> total = new(), remain = new(), spent = new();
    Dictionary<int, int> extra = new();
    int bonus;

    public int Level { get; private set; }
    public int JobCode { get; private set; }
    public int JobRank { get; private set; }
    public int Spent { get; private set; }
    public int Remain { get; private set; }
    public int LimitLevel1 = 80, LimitLevel2 = 100;

    /// <param name="player">The local player's job system (CheckRequirements reads its job data).</param>
    /// <param name="isQuestRewarded">CQuestManagerCommon::IsQuestRewarded of the player.</param>
    public SkillPointSimulator(SkillTables tables, JobSystem player, Func<uint, bool> isQuestRewarded)
    {
        t = tables ?? SkillTables.Empty;
        this.player = player;
        this.isQuestRewarded = isQuestRewarded ?? (_ => false);
    }

    /// <summary>Reset @0x140abc500 (:37): copies of the skills into both maps, then Recalc.</summary>
    public void Reset(int level, int jobCode, IEnumerable<JobSkill> skills, IReadOnlyDictionary<int, int> extraPoints, int bonusPoints)
    {
        snapshot.Clear();
        edit.Clear();
        JobCode = jobCode;
        JobRank = JobTable.GetJobRank(jobCode);
        Level = level;
        extra = extraPoints != null ? new Dictionary<int, int>(extraPoints) : new();
        Spent = 0;
        remain.Clear();
        spent.Clear();
        bonus = bonusPoints;
        foreach (var s in skills)
        {
            snapshot[s.Id] = s.Copy();
            edit[s.Id] = s.Copy();
        }
        Recalc();
    }

    /// <summary>GetTotalPoints @0x140abc820.</summary>
    public int GetTotalPoints(int rank) => total.GetValueOrDefault(rank);
    /// <summary>GetRemainPoints @0x140abc860.</summary>
    public int GetRemainPoints(int rank) => remain.GetValueOrDefault(rank);
    /// <summary>GetSpentPoints @0x140abc8a0.</summary>
    public int GetSpentPoints(int rank) => spent.GetValueOrDefault(rank);
    public IEnumerable<JobSkill> EditSkills => edit.Values;
    /// <summary>GetSnapshotSkill @0x140abc900.</summary>
    public JobSkill GetSnapshotSkill(uint id) => snapshot.TryGetValue(id, out var s) ? s : null;
    /// <summary>GetEditSkill @0x140abc980.</summary>
    public JobSkill GetEditSkill(uint id) => edit.TryGetValue(id, out var s) ? s : null;

    /// <summary>Recalc @0x140abca00 (:114): ranks 1 and 2 get level points + extra + bonus; the learnt edit skills spend; remain is the rest, not below 0.</summary>
    public void Recalc()
    {
        total.Clear();
        for (int r = 1; r < 3; r++)
            total[r] = JobSystem.GetLevelSkillPoint(Level, r, LimitLevel1, LimitLevel2) + extra.GetValueOrDefault(r) + bonus;
        Spent = 0;
        Remain = 0;
        remain.Clear();
        spent.Clear();
        var used = new Dictionary<int, int>();
        foreach (var s in edit.Values)
        {
            if (!s.Learned) continue;
            used[s.JobLevel] = used.GetValueOrDefault(s.JobLevel) + s.UsedPoint(t);
            Spent += s.UsedPoint(t);
        }
        for (int r = 1; r < 3; r++)
        {
            int u = used.GetValueOrDefault(r);
            spent[r] = u;
            int left = Math.Max(0, GetTotalPoints(r) - u);
            remain[r] = left;
            Remain += left;
        }
    }

    /// <summary>Validate @0x140abcd70 (:153): every learnt edit skill is within its max level and meets its requirements.</summary>
    public bool Validate(out uint badSkill)
    {
        badSkill = 0;
        int sum = 0;
        foreach (var s in edit.Values)
        {
            if (!s.Learned) continue;
            badSkill = s.Id;
            var d = t.GetSkillData(s.Id);
            if (d == null || t.MaxLevel(d) < s.Level) return false;
            if (!CheckRequirements(s.Id, s.Level)) return false;
            sum += s.UsedPoint(t);
        }
        return Spent >= sum;
    }

    /// <summary>LevelUp @0x140abcfe0 (:179).</summary>
    public bool LevelUp(uint id)
    {
        var s = GetEditSkill(id);
        if (s == null || !CanLevelUp(id)) return false;
        var d = t.GetSkillData(s.Id);
        if (d == null) return false;
        if (s.Learned)
        {
            if (t.MaxLevel(d) < s.Level + 1) return false;
            s.Level++;
        }
        else s.Learned = true;
        Recalc();
        return true;
    }

    /// <summary>LevelDown @0x140abd1b0 (:204): undone when the edit no longer validates.</summary>
    public bool LevelDown(uint id, out uint badSkill)
    {
        badSkill = 0;
        var s = GetEditSkill(id);
        if (s == null || !CanLevelDown(id)) return false;
        ushort oldLevel = s.Level;
        bool oldLearned = s.Learned;
        if (s.IsLearn)
        {
            if (s.Level == 1) return false;
            s.Level--;
        }
        else if (s.Level == 1) s.Learned = false;
        else s.Level--;
        if (!Validate(out badSkill))
        {
            s.Level = oldLevel;
            s.Learned = oldLearned;
            return false;
        }
        Recalc();
        return true;
    }

    /// <summary>CanLevelUp @0x140abd3b0 (:237): points left in the skill's rank, below the max level when learnt, a rank the job has, and the requirements of the next level.</summary>
    public bool CanLevelUp(uint id)
    {
        var s = GetEditSkill(id);
        if (s == null || GetRemainPoints(s.JobLevel) == 0) return false;
        var d = t.GetSkillData(s.Id);
        if (d == null) return false;
        if (s.Learned && t.MaxLevel(d) <= s.Level) return false;
        if (s.JobLevel > JobRank) return false;
        return CheckRequirements(s.Id, (ushort)(s.Learned ? s.Level + 1 : s.Level));
    }

    /// <summary>CanLevelDown @0x140abd5f0 (:261).</summary>
    public bool CanLevelDown(uint id)
    {
        var s = GetEditSkill(id);
        if (s == null || !s.Learned || s.JobLevel > JobRank) return false;
        return s.IsLearn || s.Extra ? s.Level > 1 : s.Level >= 1;
    }

    /// <summary>CheckRequirements @0x140abd760 (:281): no player job data fails; a skill outside the job passes; then the player level the
    /// level needs, the required edit skills learnt at their levels, and the required quest rewarded.</summary>
    public bool CheckRequirements(uint id, ushort level)
    {
        var job = player?.Job;
        if (job == null) return false;
        var data = job.FindSkill(id);
        if (data == null) return true;
        if (data.GetRequireLevel(level) > Level) return false;
        for (int i = 0; i < data.RequireSkillIds.Count; i++)
        {
            var req = GetEditSkill(data.RequireSkillIds[i]);
            if (req == null || !req.Learned || req.Level < data.RequireSkillLevel(i)) return false;
        }
        return data.RequireQuestId == 0 || isQuestRewarded(data.RequireQuestId);
    }

    /// <summary>GetChanged @0x140abdac0 (:315): edit skills (of the rank, 0 = all) whose learnt flag or level differs from the snapshot.</summary>
    public List<JobSkill> GetChanged(int rank)
    {
        var o = new List<JobSkill>();
        foreach (var e in edit.Values)
        {
            if (GetSnapshotSkill(e.Id) is not { } old) continue;
            if (rank != 0 && rank != e.JobLevel) continue;
            if (old.Learned != e.Learned || old.Level != e.Level) o.Add(e);
        }
        return o;
    }

    /// <summary>GetMerged @0x140abdc90 (:341): the edit skill of the rank (0 = all), the snapshot one otherwise.</summary>
    public List<JobSkill> GetMerged(int rank)
    {
        var o = new List<JobSkill>();
        foreach (var e in edit.Values)
        {
            if (GetSnapshotSkill(e.Id) is not { } old) continue;
            o.Add(rank != 0 && rank != e.JobLevel ? old : e);
        }
        return o;
    }

    /// <summary>GetSnapshot @0x140abddf0 (:360).</summary>
    public List<JobSkill> GetSnapshot(int rank) => snapshot.Values.Where(s => rank == 0 || rank == s.JobLevel).ToList();

    /// <summary>IsChanged @0x140abdef0 (:377).</summary>
    public bool IsChanged() => GetChanged(0).Count > 0;

    /// <summary>ResetRank @0x140abe0a0 (:402): level 1, and not learnt unless learnt with the rank or extra.</summary>
    public void ResetRank(int rank)
    {
        foreach (var s in edit.Values)
        {
            if (rank != 0 && s.JobLevel != rank) continue;
            s.Level = 1;
            if (!s.IsLearn && !s.Extra) s.Learned = false;
        }
        Recalc();
    }

    /// <summary>AutoDistribute @0x140abe1e0 (:425): from a reset, spends each rank's points one at a time on the first skill of
    /// <paramref name="order"/> that can level up, dropping an entry once it spends its count.</summary>
    public bool AutoDistribute(List<uint> order, List<uint> counts)
    {
        ResetRank(0);
        if (Remain == 0) return false;
        for (int r = 1; r < 3; r++)
        {
            uint points = (uint)GetRemainPoints(r);
            while (points > 0)
            {
                int i;
                JobSkill s = null;
                bool found = false;
                for (i = 0; i < order.Count; i++)
                {
                    s = GetEditSkill(order[i]);
                    if (s == null || s.JobLevel != r) continue;
                    if (CanLevelUp(order[i]))
                    {
                        if (!s.Learned) s.Learned = true;
                        else s.Level++;
                        found = true;
                        break;
                    }
                }
                if (!found) break;
                points--;
                if (s.UsedPoint(t) == (int)counts[i])
                {
                    order.RemoveAt(i);
                    counts.RemoveAt(i);
                }
            }
        }
        Recalc();
        return Validate(out _);
    }

    /// <summary>GetUsedPoints @0x140abe4a0 (:482): (id, used points) of each skill that spends any; with a rank, a skill of another
    /// rank counts its snapshot (the client reads the snapshot map at the edit map's position; by id here).</summary>
    public List<(uint Id, int Points)> GetUsedPoints(int rank)
    {
        var o = new List<(uint, int)>();
        foreach (var e in edit.Values)
        {
            var s = e;
            if (rank != 0 && rank != e.JobLevel)
            {
                s = GetSnapshotSkill(e.Id);
                if (s == null) continue;
            }
            int used = s.UsedPoint(t);
            if (used != 0) o.Add((s.Id, used));
        }
        return o;
    }

    /// <summary>Commit @0x140abe620 (:507): the edit becomes the snapshot; with a rank, the other ranks keep their snapshot.</summary>
    public void Commit(int rank)
    {
        var keep = rank != 0 ? snapshot.Values.Where(s => s.JobLevel != rank).ToList() : new List<JobSkill>();
        snapshot.Clear();
        foreach (var e in edit.Values) snapshot[e.Id] = e.Copy();
        foreach (var s in keep) snapshot[s.Id] = s;
    }
}
