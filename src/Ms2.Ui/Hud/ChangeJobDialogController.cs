using Ms2.Avm2;
using Ms2.Ui.Skills;

namespace Ms2.Ui.Hud;

/// <summary>
/// The job change window, UIChangeJobDialog (unnamed/unit_140bd7e10_uichangejobdialog.cpp, UI id 0x4f6b, type name
/// "ChangeJobDialog", movie uichangejobdialog), toggled by the game menu's "jobchange" entry (FUN_14193d590,
/// uigamemenudialog.cpp:2200), which shows only while <see cref="CanChangeJob"/> passes (m_isEnabled,
/// gameeventmanagercli.cpp:956-965). It shows the local player's sub job's changeJob.xml row: the changed job's name
/// and icon, the texts, the two skill rows with their tooltips and the preview movie. Its change button closes it and
/// starts the job's start quest's talk.
/// <para>STILL-OPEN: ShowJobChangeNotice's talk (FindQuestTalk(quest, 100) through UITalkManager::BeginTalkWithoutNpc,
/// uitalkcinematic.cpp:1798) has no counterpart here; <see cref="JobChangeNotice"/> is raised with the quest instead.</para>
/// </summary>
public sealed class ChangeJobDialogController : UiController
{
    public override string Movie => "uichangejobdialog";
    /// <summary>The CommonDepth layer of the movie: UIChangeJobDialog's ctor (@0x140bd7e10) sets m_2c to 0x23.</summary>
    public override int LoadLayer => CommonDepth.GuideEvent;
    protected override string EntityMethod => "alignment";
    public override string TypeName => "ChangeJobDialog";

    /// <summary>vfunc_48 @0x140bd8b70.</summary>
    public const int UiId = 0x4f6b;

    /// <summary>CChangeJobTable.</summary>
    public ChangeJobTable Table = ChangeJobTable.Empty;
    /// <summary>PC::GetSubJob of the local player.</summary>
    public Func<int> SubJob = () => 0;
    /// <summary>The skill table's row (SkillDataTable::GetSkillData): null skips the skill, as the client does.</summary>
    public Func<uint, SkillData> SkillData = _ => null;
    /// <summary>ASTooltip(local player, 0x10, 2).FUN_1409e1c50(id, 1, 1): null when it builds none.</summary>
    public Func<uint, AsTooltip> SkillTooltip = _ => null;
    /// <summary>ShowJobChangeNotice(GetStartQuestId(m_nJobCode)) @0x140bd89d0: the quest whose talk 100 starts.</summary>
    public event Action<int> JobChangeNotice;

    /// <summary>m_nJobCode: the sub job the window was filled for (0 before).</summary>
    public int JobCode { get; private set; }

    /// <summary>OnLoadComplete @0x140bd7f50: UIDialog's, then FUN_140bd8040 with the local player's sub job.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (!Model.HasLocalPlayer) return;
        SetData(SubJob());
    }

    /// <summary>FUN_140bd8040 @0x140bd8040: m_nJobCode, then the row's setData: jobcode (the changed sub job), jobName, desc,
    /// skilldesc1, skilldesc2, moviepath, and per skill of each row that the skill table has {id, icon, tooltip (when one is
    /// built)} in skills1 / skills2. Nothing is sent without the movie or the row.</summary>
    public void SetData(int jobCode)
    {
        if (Host?.Entity == null) return;
        JobCode = jobCode;
        var d = Table.Get(jobCode);
        if (d == null) return;
        Host.Invoke("setData", Host.Obj(("jobcode", d.ChangeSubJobCode), ("jobName", d.JobName), ("desc", d.Desc),
            ("skilldesc1", d.SkillDesc1), ("skilldesc2", d.SkillDesc2), ("moviepath", d.Movie),
            ("skills1", Skills(d.Skills1)), ("skills2", Skills(d.Skills2))));
    }

    /// <summary>One skill row's entries.</summary>
    public List<AsObject> Skills(IReadOnlyList<uint> ids)
    {
        var list = new List<AsObject>(ids.Count);
        foreach (uint id in ids)
        {
            var s = SkillData(id);
            if (s == null) continue;
            var members = new List<(string, object)> { ("id", id), ("icon", s.Icon ?? "") };
            if (SkillTooltip(id) is { } tip) members.Add(("tooltip", tip));
            list.Add(Host.Obj(members.ToArray()));
        }
        return list;
    }

    /// <summary>vfunc_28 @0x140bd7fa0: onChangeJob closes the window (vfunc_67) and starts the start quest's talk.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (!string.Equals(name, "onChangeJob", StringComparison.OrdinalIgnoreCase)) return false;
        Close();
        JobChangeNotice?.Invoke(Table.GetStartQuestId(JobCode));
        return true;
    }

    /// <summary>CanChangeJob @0x140bd88e0: a local player, CChangeJobTable::CanChange(job group, sub job, level), and the
    /// sub job's start quest neither accepted (1) nor completed (2).</summary>
    public static bool CanChangeJob(ChangeJobTable table, JobTable jobs, int changeJobLevel, bool hasPlayer, int jobGroup, int subJob,
        int level, Func<int, int> questState)
    {
        if (!hasPlayer) return false;
        if (!table.CanChange(jobGroup, subJob, level, changeJobLevel, jobs)) return false;
        int state = questState(table.GetStartQuestId(subJob));
        return state != 1 && state != 2;
    }
}
