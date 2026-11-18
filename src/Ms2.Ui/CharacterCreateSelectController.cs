using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui;

/// <summary>
/// UICharacterCreateSelectDialog (ms2/client/ms2/uicharactercreateselectdialog.cpp): the job pick page's C++ side over the
/// uicharactercreateselectdialog movie (CharacterCreateSelectDialog). The job tabs come from table/characterCreateSelect.xml
/// (<see cref="Load"/>); what the client does with the answers (the job's music and voice, entering the create map) is
/// the host's, through the events.
/// </summary>
public sealed class CharacterCreateSelectController : UiController
{
    /// <summary>TJobData: one tab (FUN_140f2ee10 fills name, desc, movie, jobCode; the tab row adds enabled and event).</summary>
    public sealed record Job(int JobCode, string Name, string Desc, string Movie, bool Enabled = true, bool Event = false);

    public override string Movie => "uicharactercreateselectdialog";
    protected override string EntityMethod => "changeTab";

    /// <summary>The constructor calls SetTweenEnabled(0): no startOpen on load; initData plays the movie's own popup.</summary>
    protected override bool IsDialog => false;

    /// <summary>m_jobs: the tabs in the display row's order.</summary>
    public readonly List<Job> Jobs = new();
    /// <summary>m_randomJobs: randomselectJobCode of the display row.</summary>
    public readonly List<int> RandomJobs = new();
    /// <summary>m_nSelectJob: selectJobCode of the display row, when that job is listed.</summary>
    public int SelectJob;
    /// <summary>The job selected on load instead of FUN_140f30150's pick (a host rebuilding a page already open); 0 for none.</summary>
    public int KeepJob;
    /// <summary>GetRandomRange.</summary>
    public Random Rng = new();
    /// <summary>GetRegion() == 1 (jp): onClickCreate asks for the terms first. The KR client runs region 0.</summary>
    public bool TermsRegion;

    /// <summary>m_nPopupJob, m_nPopupMsg, m_nBgm (constructor and vfunc_67 values).</summary>
    int popupJob = 1, popupMsg;
    bool bgm;
    const int MsgSelectCreateJob = 0x620001c;

    /// <summary>FUN_140f2ffb0: the job is shown (JobTable characterBGM through PlayMusic, characterVoice, chroma colour mode).</summary>
    public event Action<int> JobShown;
    /// <summary>FUN_140f30110: the job music started by <see cref="JobShown"/> stops (WorldService FUN_141bc7b80(0)).</summary>
    public event Action StopMusic;
    /// <summary>OnEndPopdown after onClickCreate: CSceneStateMulti::SelectCreateJob(job).</summary>
    public event Action<int> SelectCreateJob;
    /// <summary>OnEndPopdown with no message pending (the page popped down for onClickPrev).</summary>
    public event Action Closed;
    /// <summary>onClickCreate in the terms region: message box $s_msgbox_ugc_turms_content (FUN_140f2fa00); its OK goes on with <see cref="ConfirmTerms"/>.</summary>
    public event Action<int> TermsAsked;

    /// <summary>The job the movie shows (the last initData / changeTab).</summary>
    public int ShownJob { get; private set; }

    /// <summary>OnLoadComplete @0x140f2e6a0: initData({arrTab, selectedTabIndex, selectedJobData}).</summary>
    public override void OnLoadComplete()
    {
        int select = KeepJob != 0 ? KeepJob : PickJob();
        int selectedTab = 0;
        object selected = Host.Obj();
        var tabs = new List<object>();
        for (int i = 0; i < Jobs.Count; i++)
        {
            var j = Jobs[i];
            if (select == j.JobCode)
            {
                selectedTab = i;
                selected = JobData(j);
                Shown(j.JobCode);
            }
            tabs.Add(Host.Obj(("name", j.Name ?? ""), ("jobCode", j.JobCode), ("enabled", j.Enabled), ("event", j.Event)));
        }
        Host.Invoke(Host.Entity, "initData", Host.Obj(("arrTab", tabs), ("selectedTabIndex", selectedTab), ("selectedJobData", selected)));
    }

    /// <summary>FUN_140f30150: a random job of m_randomJobs, else m_nSelectJob.</summary>
    public int PickJob() => RandomJobs.Count == 0 ? SelectJob : RandomJobs[Rng.Next(RandomJobs.Count)];

    /// <summary>TJobData::FUN_140f2ee10.</summary>
    object JobData(Job j) => Host.Obj(("name", j.Name ?? ""), ("desc", j.Desc ?? ""), ("movie", j.Movie ?? ""), ("jobCode", j.JobCode));

    void Shown(int jobCode)
    {
        ShownJob = jobCode;
        bgm = true;
        JobShown?.Invoke(jobCode);
    }

    /// <summary>vfunc_28 @0x140f2eac0.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (string.Equals(name, "onChangeTab", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length >= 2) OnTabChanged((int)(long)Avm2.Rt.ToNumber(args[0]), (int)Avm2.Rt.ToNumber(args[1]));
            return true;
        }
        if (string.Equals(name, "onClickCreate", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length >= 1) ClickCreate((int)Avm2.Rt.ToNumber(args[0]));
            return true;
        }
        if (string.Equals(name, "onClickPrev", StringComparison.OrdinalIgnoreCase))
        {
            Popdown();
            StopJobMusic();
            return true;
        }
        if (string.Equals(name, "OnEndPopdown", StringComparison.OrdinalIgnoreCase))
        {
            OnEndPopdown();
            return true;
        }
        return false;
    }

    /// <summary>OnTabChanged @0x140f2fba0: the index's job, if its code agrees, goes to changeTab.</summary>
    void OnTabChanged(int index, int jobCode)
    {
        if (index < 0 || index >= Jobs.Count) return;
        var j = Jobs[index];
        if (j.JobCode != jobCode) return;
        Host.Invoke(Host.Entity, "changeTab", JobData(j));
        Shown(j.JobCode);
    }

    /// <summary>FUN_140f2fa00: the terms box in region 1, else FUN_140f2f9c0.</summary>
    void ClickCreate(int jobCode)
    {
        if (TermsRegion) TermsAsked?.Invoke(jobCode);
        else ConfirmTerms(jobCode);
    }

    /// <summary>FUN_140f2f9c0 (also the terms box's OK, FUN_140f301d0): popdown, then SelectCreateJob at its end.</summary>
    public void ConfirmTerms(int jobCode)
    {
        Popdown();
        popupMsg = MsgSelectCreateJob;
        popupJob = jobCode;
    }

    /// <summary>Popdown @0x140f2fd50.</summary>
    public void Popdown() => Host.Invoke(Host.Entity, "popdown");

    /// <summary>FUN_140f30110.</summary>
    void StopJobMusic()
    {
        if (!bgm) return;
        bgm = false;
        StopMusic?.Invoke();
    }

    /// <summary>OnEndPopdown @0x140f2fdb0, then vfunc_67 (m_nPopupJob 1, m_nPopupMsg 0, m_nBgm 0).</summary>
    void OnEndPopdown()
    {
        int msg = popupMsg, job = popupJob;
        popupMsg = 0;
        popupJob = 1;
        bgm = false;
        if (msg == MsgSelectCreateJob) SelectCreateJob?.Invoke(job);
        else Closed?.Invoke();
    }

    /// <summary>
    /// LoadCharacterCreateSelect: the "data" group's rows (IsElementAvailable: the client's locale or none, an enabled feature or
    /// none) by jobCode, named by string table 6 (jobName) of JobTable::GetSubJob1 and described by table 28
    /// (stringCharacterCreateSelect) descKey; the "display" group of the client's locale, else the one without a locale; of its
    /// rows with an enabled feature, the one of the highest feature level (SelectHigherLevelFeatureName: ties go to the
    /// greater name, a later row of the same feature wins). Its order lists the tabs; disableJobCode and eventJobCode mark
    /// them; selectJobCode and randomselectJobCode are the start picks.
    /// </summary>
    public void Load(PackSet packs, params string[] languages)
    {
        Jobs.Clear(); RandomJobs.Clear(); SelectJob = 0;
        var x = Xml(packs, "table/charactercreateselect.xml")?.Root;
        if (x == null) return;
        var strings = new GameStrings(packs, languages);
        var names = strings.Table("jobname", "name");
        var descs = strings.Table("stringcharactercreateselect", "name");
        var subJob1 = new Dictionary<int, int>();
        foreach (var e in Xml(packs, "table/job.xml")?.Root.Descendants("job") ?? Enumerable.Empty<XElement>())
            if (int.TryParse((string)e.Attribute("jobGroupID"), out int g))
                subJob1.TryAdd(g, int.TryParse((string)e.Attribute("subJob1"), out int s) ? s : 0);

        XElement data = null, display = null, displayAny = null;
        foreach (var g in x.Elements("group"))
        {
            var kind = (string)g.Attribute("name") ?? "";
            if (kind.Equals("data", StringComparison.OrdinalIgnoreCase)) data = g;
            else if (kind.Equals("display", StringComparison.OrdinalIgnoreCase))
            {
                var locale = (string)g.Attribute("locale") ?? "";
                if (!LocaleInList(locale)) continue;
                if (locale.Length == 0) displayAny = g; else display = g;
            }
        }
        display ??= displayAny;
        XElement selected = null;
        if (display != null)
        {
            string best = "";
            foreach (var row in display.Elements("list"))
            {
                var feature = (string)row.Attribute("feature") ?? "";
                if (!LobbyFeatures.IsEnabledByName(packs, feature)) continue;
                if (HigherLevel(packs, best, feature) == feature) { selected = row; best = feature; }
            }
        }
        var jobs = new Dictionary<int, Job>();
        foreach (var row in data?.Elements("list") ?? Enumerable.Empty<XElement>())
        {
            if (!Available(packs, row)) continue;
            int code = Int((string)row.Attribute("jobCode"));
            if (code == 0) continue;
            int descKey = Int((string)row.Attribute("descKey"));
            string name = names.GetValueOrDefault(subJob1.GetValueOrDefault(code).ToString(CultureInfo.InvariantCulture)) ?? "";
            string desc = descKey != 0 ? descs.GetValueOrDefault(descKey.ToString(CultureInfo.InvariantCulture)) ?? "" : "";
            jobs[code] = new Job(code, name, desc, (string)row.Attribute("movie") ?? "");
        }
        if (selected == null) return;
        var disable = Ints((string)selected.Attribute("disableJobCode"));
        var events = Ints((string)selected.Attribute("eventJobCode"));
        int selectJob = Int((string)selected.Attribute("selectJobCode"));
        RandomJobs.AddRange(Ints((string)selected.Attribute("randomselectJobCode")));
        foreach (int code in Ints((string)selected.Attribute("order")))
        {
            if (!jobs.TryGetValue(code, out var j)) continue;
            j = j with { Enabled = !disable.Contains(code), Event = events.Contains(code) };
            if (code == selectJob) SelectJob = code;
            Jobs.Add(j);
        }
    }

    /// <summary>CFeatureManager::IsLocaleInList: empty, or a comma list holding the client's locale.</summary>
    static bool LocaleInList(string list) => list.Length == 0
        || list.Split(',').Any(l => string.Equals(l.Trim(), LobbyFeatures.LocaleName, StringComparison.OrdinalIgnoreCase));

    /// <summary>IsElementAvailable: the client's locale (or none) and an enabled feature (or none).</summary>
    static bool Available(PackSet packs, XElement e) => LocaleInList((string)e.Attribute("locale") ?? "")
        && LobbyFeatures.IsEnabledByName(packs, (string)e.Attribute("feature"));

    /// <summary>CFeatureManager::SelectHigherLevelFeatureName.</summary>
    static string HigherLevel(PackSet packs, string a, string b)
    {
        if (a == b) return a;
        int la = LobbyFeatures.LevelOf(packs, a), lb = LobbyFeatures.LevelOf(packs, b);
        if (la != lb) return la > lb ? a : b;
        return string.CompareOrdinal(a, b) > 0 ? a : b;
    }

    static int Int(string s) => int.TryParse(s?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

    /// <summary>XmlTokenList / XmlSplit: comma separated integers.</summary>
    static List<int> Ints(string s) => (s ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(Int).ToList();

    static XDocument Xml(PackSet packs, string entry) => packs.Exists("Xml", entry) ? GameStrings.Parse(packs.Read("Xml", entry)) : null;
}
