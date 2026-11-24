using System.Xml.Linq;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The job change window (UIChangeJobDialog 0x4f6b), its table (CChangeJobTable, Table/changeJob.xml) and the game
/// menu's "jobchange" entry (m_isEnabled = UIChangeJobDialog::CanChangeJob).</summary>
public class ChangeJobTests
{
    static Func<int, bool> Features(PackSet packs) => id => LobbyFeatures.IsEnabled(packs, FeatureTable.Names[id - 1]);

    /// <summary>Every row of each data set: the sub job is its job group's first and the change its second (so CanChange can
    /// pass and the advance is a change target), the texts and the movie are there, and every skill has its skill row and
    /// a tooltip at level 1.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryRowOfTheDataIsAChangeTheWindowCanShow(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        var tables = new GameTables(packs, "en");
        var t = ChangeJobTable.Load(packs, Features(packs), tables);
        var jobs = tables.Skills.Jobs;
        var player = new SkillTooltipPlayer { Jobs = new JobSystem(tables.Skills), Level = 60 };
        Assert.That(t.Rows.Keys.OrderBy(k => k), Is.EqualTo(Enumerable.Range(1, 11).Select(i => i * 100)), "one row per job");
        Assert.That(tables.ChangeJobLevel, Is.EqualTo(60));
        int skills = 0, noTooltip = 0;
        var bad = new List<string>();
        foreach (var d in t.Rows.Values)
        {
            int group = d.SubJobCode / 10;
            if (jobs.GetSubJob1(group) != d.SubJobCode) bad.Add($"{d.SubJobCode}: not job {group}'s first sub job");
            if (jobs.GetJobData(group)?.SubJob2 != d.ChangeSubJobCode) bad.Add($"{d.SubJobCode}: {d.ChangeSubJobCode} is not job {group}'s second");
            if (!t.CanChange(group, d.SubJobCode, 60, tables.ChangeJobLevel, jobs) || t.CanChange(group, d.SubJobCode, 59, tables.ChangeJobLevel, jobs))
                bad.Add($"{d.SubJobCode}: CanChange from level 60 only");
            if (t.CanChange(group, d.ChangeSubJobCode, 60, tables.ChangeJobLevel, jobs)) bad.Add($"{d.SubJobCode}: the changed job can change again");
            if (!t.IsChangeTarget(d.ChangeSubJobCode) || t.IsChangeTarget(d.SubJobCode)) bad.Add($"{d.SubJobCode}: IsChangeTarget");
            if (d.StartQuestId == 0 || d.EndQuestId == 0) bad.Add($"{d.SubJobCode}: quests");
            foreach (var (name, text) in new[] { ("jobName", d.JobName), ("desc", d.Desc), ("skilldesc1", d.SkillDesc1), ("skilldesc2", d.SkillDesc2), ("movie", d.Movie) })
                if (string.IsNullOrEmpty(text)) bad.Add($"{d.SubJobCode}: no {name}");
            if (d.Skills1.Count != 4 || d.Skills2.Count != 4) bad.Add($"{d.SubJobCode}: {d.Skills1.Count}+{d.Skills2.Count} skills");
            foreach (uint id in d.Skills1.Concat(d.Skills2))
            {
                skills++;
                if (tables.Skills.GetSkillData(id) == null) bad.Add($"{d.SubJobCode}: skill {id} has no row");
                else if (SkillTooltip.Build(tables, player, id, 1, 0x10, true) == null) noTooltip++;
            }
        }
        Assert.That(bad, Is.Empty);
        Assert.That((skills, noTooltip), Is.EqualTo((88, 0)));
    }

    [Test]
    public void FeatureOffLoadsNoRow()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        Assert.That(ChangeJobTable.Load(packs, _ => false, new GameTables(packs, "en")).Rows, Is.Empty);
    }

    /// <summary>Load @0x1401d6550: a row whose feature is off is left out, a missing int keeps the last value read (one nValue,
    /// one nStringId), the skill lists are atoi per comma token, and a later row of a code replaces the first.</summary>
    [Test]
    public void LoadReadsTheRowsAsTheClient()
    {
        var doc = XDocument.Parse("""
            <ms2>
              <subjob subJobCode="100" changeSubJobCode="101" startquestid="5" desc="7" skilldesc1="8" skillID1="1, 2,x3" skillID2="" feature="On" />
              <subjob subJobCode="200" changeSubJobCode="201" startquestid="6" endquestid="9" desc="1" feature="Off" />
              <subjob subJobCode="300" changeSubJobCode="301" startquestid="4" endquestid="3" desc="2" skilldesc1="3" skilldesc2="4" movie="m" feature="On" />
              <subjob subJobCode="300" changeSubJobCode="302" feature="On" />
            </ms2>
            """);
        var t = ChangeJobTable.Load(doc, f => f == "On", job => "job" + job, id => "text" + id);
        Assert.That(t.Rows.Keys.OrderBy(k => k), Is.EqualTo(new[] { 100, 300 }));
        var a = t.Get(100);
        Assert.That((a.ChangeSubJobCode, a.StartQuestId, a.EndQuestId), Is.EqualTo((101, 5, 5)), "endquestid missing: startquestid's value");
        Assert.That((a.JobName, a.Desc, a.SkillDesc1, a.SkillDesc2, a.Movie), Is.EqualTo(("job101", "text7", "text8", "text8", "")));
        Assert.That(a.Skills1, Is.EqualTo(new uint[] { 1, 2, 0 }));
        Assert.That(a.Skills2, Is.Empty);
        var b = t.Get(300);
        Assert.That((b.ChangeSubJobCode, b.StartQuestId, b.EndQuestId), Is.EqualTo((302, 302, 302)), "the later row; its missing ints keep the last value");
        Assert.That(t.Get(200), Is.Null);
        Assert.That(t.GetStartQuestId(200), Is.EqualTo(0));
    }

    /// <summary>CanChangeJob @0x140bd88e0: a player, CanChange, and the start quest neither accepted nor completed.</summary>
    [Test]
    public void CanChangeJobIsTheClientsRule()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var tables = new GameTables(packs, "en");
        var t = ChangeJobTable.Load(packs, Features(packs), tables);
        var jobs = tables.Skills.Jobs;
        int start = t.GetStartQuestId(300);
        bool Can(bool player, int sub, int level, int state) =>
            ChangeJobDialogController.CanChangeJob(t, jobs, tables.ChangeJobLevel, player, 30, sub, level, q => q == start ? state : 0);
        Assert.That(Can(true, 300, 60, 0), Is.True);
        Assert.That(Can(true, 300, 60, 3), Is.True, "another state than accepted or completed");
        Assert.That(Can(true, 300, 60, 1), Is.False, "the start quest accepted");
        Assert.That(Can(true, 300, 60, 2), Is.False, "the start quest completed");
        Assert.That(Can(true, 300, 59, 0), Is.False, "below ChangeJobLevel");
        Assert.That(Can(true, 301, 70, 0), Is.False, "already changed");
        Assert.That(Can(false, 300, 60, 0), Is.False, "no local player");
        Assert.That(ChangeJobDialogController.CanChangeJob(ChangeJobTable.Empty, jobs, 60, true, 30, 300, 60, _ => 0), Is.False, "feature 0xca off");
    }

    /// <summary>OnLoadComplete fills the window with the sub job's row; onChangeJob closes it and asks for the start quest's
    /// talk.</summary>
    [Test]
    public void TheWindowShowsTheRowAndItsChangeButtonClosesIt()
    {
        var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        var tables = new GameTables(packs, "en");
        var model = new UiModel(tables) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(packs, f), HasLocalPlayer = true };
        var t = ChangeJobTable.Load(packs, Features(packs), tables);
        var notices = new List<int>();
        var c = new ChangeJobDialogController
        {
            Table = t, SubJob = () => 300, SkillData = tables.Skills.GetSkillData,
            SkillTooltip = id => new AsTooltip { ["tooltip"] = "export_common_tooltip_kindsSkill", ["skillName"] = tables.SkillName(id) },
        };
        c.JobChangeNotice += notices.Add;
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        var h = new UiHost(p, c, model);
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 10; i++) p.Tick();
        var d = t.Get(300);
        Assert.That(c.JobCode, Is.EqualTo(300));
        var content = h.Child("m_content");
        Assert.That(content, Is.Not.Null);
        var name = h.Get(h.Get(content, "m_jobName"), "textField");
        Assert.That(Rt.ToString(h.Get(name, "text")), Is.EqualTo(d.JobName));
        var slots = h.Get(content, "m_skillSlotContainer");
        Assert.That((int)Rt.ToNumber(h.Get(slots, "numChildren")), Is.GreaterThanOrEqualTo(8), "the 4 + 4 skill slots");
        Assert.That(d.Movie, Does.EndWith(".usm"));
        Assert.That(h.Get(content, "m_moviePlayer"), Is.InstanceOf<Ms2.Avm2.Flash.AsVideo>(), "setData's moviepath makes the preview's VideoPlayer");
        Assert.That(rt.Errors, Is.Empty);
        Assert.That(c.OnCallback("onChangeJob", Array.Empty<object>()), Is.True);
        Assert.That(notices, Is.EqualTo(new[] { d.StartQuestId }));
        for (int i = 0; i < 60; i++) p.Tick();
        Assert.That(c.IsClosed, Is.True);
        // a skill the table lacks is skipped; the tooltip only when one is built
        c.SkillTooltip = _ => null;
        var list = c.Skills(new uint[] { d.Skills1[0], 999 });
        Assert.That(list.Count, Is.EqualTo(1));
        Assert.That(Rt.ToString(h.Get(list[0], "icon")), Is.EqualTo(tables.Skills.GetSkillData(d.Skills1[0]).Icon));
        Assert.That(h.Get(list[0], "tooltip"), Is.Null.Or.EqualTo(Rt.Undef));
    }

    /// <summary>UIChangeJobEffect: OnLoadComplete's setData puts the job's symbol and s_changejob_effect_text with the job's
    /// name, and the movie's last frame (298) calls onHide, which hides it.</summary>
    [Test]
    public void TheEffectShowsTheJobsTextAndHidesAtItsLastFrame()
    {
        var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        var tables = new GameTables(packs, "en");
        var model = new UiModel(tables) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(packs, f), HasLocalPlayer = true };
        var t = ChangeJobTable.Load(packs, Features(packs), tables);
        int job = t.Get(300).ChangeSubJobCode;
        var c = new ChangeJobEffectController { SubJob = () => job };
        int hidden = 0;
        c.Hidden += () => hidden++;
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        var h = new UiHost(p, c, model);
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        p.Tick();
        string expected = Ms2.Quest.ClientFormat.Format(tables.Common("s_changejob_effect_text") ?? "", tables.JobNameOrEmpty(job));
        Assert.That(expected, Does.Contain(tables.JobNameOrEmpty(job)));
        var title = h.Get(h.Get(h.Child("m_text"), "title"), "textField");
        Assert.That(Rt.ToString(h.Get(title, "text")), Is.EqualTo(expected));
        for (int i = 0; i < 280 && hidden == 0; i++) p.Tick();
        Assert.That(hidden, Is.EqualTo(0), "not before the last frame");
        for (int i = 0; i < 40 && hidden == 0; i++) p.Tick();
        Assert.That(hidden, Is.EqualTo(1), "onHide at frame 298");
        Assert.That(rt.Errors, Is.Empty);
    }

    /// <summary>The GMS2 server's skill block (upstream SkillInfo.WriteTo) carries the job where the KMS2 block has the rank:
    /// for every row, the block of the sub job and of the changed job gives that sub job back through the job system
    /// (GetSubJob of the rank), which the window, the menu entry and the effect read.</summary>
    [Test]
    public void TheGms2SkillBlockGivesEveryRowsSubJob()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var tables = new GameTables(packs, "en");
        var t = ChangeJobTable.Load(packs, Features(packs), tables);
        Assert.That(t.Rows, Has.Count.EqualTo(11));
        var bad = new List<string>();
        foreach (var d in t.Rows.Values)
            foreach (int job in new[] { d.SubJobCode, d.ChangeSubJobCode })
            {
                var w = new Maple2.PacketLib.Tools.ByteWriter();
                w.WriteInt(job); w.WriteByte(1); w.WriteInt(job / 10);
                for (int i = 0; i < 4; i++) w.WriteByte(0);
                var book = Ms2.Net.Packets.SkillBook.Read(new Maple2.PacketLib.Tools.ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()), Ms2.Net.ProtocolProfile.Gms2);
                var jobs = new JobSystem(tables.Skills);
                jobs.Read(book);
                if (jobs.SubJob != job) bad.Add($"{job}: sub job {jobs.SubJob}");
            }
        Assert.That(bad, Is.Empty);
    }

    /// <summary>The game menu's skill book count (FUN_141938ae0(0x4f2d), refreshed on every Job packet):
    /// JobSystemCli::ChangedMainSkills, the changed skills with a skill row that are not sub skills.</summary>
    [Test]
    public void TheSkillBookCountIsTheChangedMainSkills()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var t = new GameTables(packs, "en").Skills;
        var rows = t.Jobs.GetJobData(30).Skills.ToList();
        var main = rows.Where(r => r.SubSkillIds.Count == 0).Take(3).Select(r => r.SkillId).ToList();
        uint sub = rows.First(r => r.SubSkillIds.Count > 0).SubSkillIds[0];
        Assert.That(main, Has.Count.EqualTo(3));
        var jobs = new JobSystem(t);
        jobs.Read(1, 30, new[]
        {
            new Ms2.Net.Packets.BookSkill((int)main[0], 1, true, true, false),
            new Ms2.Net.Packets.BookSkill((int)main[1], 1, true, false, false),
            new Ms2.Net.Packets.BookSkill((int)main[2], 1, false, true, false),
            new Ms2.Net.Packets.BookSkill((int)sub, 1, true, true, false),
        });
        Assert.That(jobs.FindSkill(sub)?.IsSubSkill, Is.True);
        Assert.That(jobs.ChangedMainSkills(), Is.EqualTo(2), "the two changed main skills; the unchanged one and the sub skill are not counted");
    }

    /// <summary>The menu's FUN_14193b4a0: a started quest marks the menu dirty only when it is the sub job's start quest.</summary>
    [Test]
    public void TheStartQuestOfTheSubJobRefreshesTheMenu()
    {
        var c = new GameMenuController { ChangeJobStartQuest = () => 40002754 };
        c.OnQuestStarted(40002755);
        Assert.That(c.Update(), Is.False);
        c.OnQuestStarted(40002754);
        Assert.That(c.Update(), Is.True);
        c.ChangeJobStartQuest = () => 0;
        c.OnQuestStarted(0);
        Assert.That(c.Update(), Is.False, "no row: quest 0 is no start quest");
    }
}
