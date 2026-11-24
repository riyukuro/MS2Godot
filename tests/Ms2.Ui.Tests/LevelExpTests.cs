using System.Xml.Linq;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The exp, level and adventure level notices (CSceneStateGame::OnExpUp, OnLevelUp, OnPrestige): the exp chat lines
/// (FUN_1413606c0, FUN_1413610f0), the job system's level change (UpdateLearnable), the learnt skill toasts
/// (FUN_141ac0210) and the skill point notice check; then a sweep of the tables they read on the data set (MS2_DATA).
/// </summary>
public class LevelExpTests
{
    // ---- FUN_1413606c0 with stand-in strings: "<id>:{0}" / "<key>:{0}" so the id and arguments show ----

    static string Common(int id) => id switch
    {
        0x2c0 or 0x2c1 or 0x2c2 => $"{id:x}:{{0}}/{{1}}",
        _ => $"{id:x}:{{0}}",
    };
    static string Key(string key) => key + ":{0}";

    static ExpChatLines.Lines Compose(int type, bool rested = false, bool additional = false, long amount = 12345) =>
        ExpChatLines.Compose(amount, type, rested, additional, Common, Key, () => "Field");

    [Test]
    public void ExpLineByType()
    {
        // the plain gain: s_msg_take_exp (0x2bd), grouped, no notice, no second line
        Assert.That(Compose(0), Is.EqualTo(new ExpChatLines.Lines("2bd:12,345", "", false)));
        Assert.That(Compose(0x15).Line, Is.EqualTo("2bd:12,345"));
        // the assist bonus: two lines, the second of kind 0x14
        Assert.That(Compose(0xea62), Is.EqualTo(new ExpChatLines.Lines("2be:12,345", "2bf:12,345", false)));
        // map, taxi and telescope exp name the field (string table 3); map and taxi ask for the reward notice
        Assert.That(Compose(3001), Is.EqualTo(new ExpChatLines.Lines("2c0:Field/12,345", "", true)));
        Assert.That(Compose(3002), Is.EqualTo(new ExpChatLines.Lines("2c0:Field/12,345", "", true)));
        Assert.That(Compose(3000), Is.EqualTo(new ExpChatLines.Lines("2c1:Field/12,345", "", true)));
        Assert.That(Compose(3003), Is.EqualTo(new ExpChatLines.Lines("2c2:Field/12,345", "", false)));
        // 3004 and 3007: the plain line with the notice
        Assert.That(Compose(3004), Is.EqualTo(new ExpChatLines.Lines("2bd:12,345", "", true)));
        Assert.That(Compose(3007), Is.EqualTo(new ExpChatLines.Lines("2bd:12,345", "", true)));
        // the keyed lines
        Assert.That(Compose(0x427).Line, Is.EqualTo("s_msg_take_play_instrument_exp:12,345"));
        Assert.That(Compose(0x423).Line, Is.EqualTo("s_msg_take_fishing_exp:12,345"));
        Assert.That(Compose(0x42f).Line, Is.EqualTo("s_msg_take_arcade_exp:12,345"));
        Assert.That(Compose(0x443).Line, Is.EqualTo("s_msg_take_normal_chest_exp:12,345"));
        Assert.That(Compose(0x444).Line, Is.EqualTo("s_msg_take_normal_rare_exp:12,345"));
        Assert.That(Compose(0x445).Line, Is.EqualTo("s_msg_take_normal_rare_first_exp:12,345"));
        Assert.That(new[] { 0x427, 0x423, 0x42f, 0x443, 0x444, 0x445, 3003, 0 }.Select(t => Compose(t).Notify), Has.All.False);
    }

    [Test]
    public void ExpLineSuffixesAreEventThenRest()
    {
        // the packet's flag adds s_msg_take_event_additional, then a rest exp above 0 before the packet s_msg_take_rest_exp
        Assert.That(Compose(0, additional: true).Line, Is.EqualTo("2bd:12,345s_msg_take_event_additional:{0}"));
        Assert.That(Compose(0, rested: true).Line, Is.EqualTo("2bd:12,345s_msg_take_rest_exp:{0}"));
        Assert.That(Compose(3001, rested: true, additional: true).Line,
            Is.EqualTo("2c0:Field/12,345s_msg_take_event_additional:{0}s_msg_take_rest_exp:{0}"));
        // a gain of 0 still makes its line (OnExpUp calls it for every mode 0 packet)
        Assert.That(Compose(0, amount: 0).Line, Is.EqualTo("2bd:0"));
    }

    [Test]
    public void AdventureExpLine() =>
        Assert.That(ExpChatLines.Adventure(1234567, Key), Is.EqualTo("s_msg_take_adventure_level_exp:1,234,567"));

    [Test]
    public void SkillPointNoticeFromLevel10UpToTheHigherLimit()
    {
        // KMS2 / GMS2 / restore constants: SkillPointLimitLevel1 80, SkillPointLimitLevel2 70 (80 with JobChange_02)
        Assert.That(LevelUpNotices.SkillPointNotice(10, 9, 80, 70), Is.True);
        Assert.That(LevelUpNotices.SkillPointNotice(9, 8, 80, 70), Is.False, "below 10");
        Assert.That(LevelUpNotices.SkillPointNotice(10, 10, 80, 70), Is.False, "no rise");
        Assert.That(LevelUpNotices.SkillPointNotice(80, 79, 80, 70), Is.True);
        Assert.That(LevelUpNotices.SkillPointNotice(81, 80, 80, 70), Is.False, "above the higher limit");
        Assert.That(LevelUpNotices.SkillPointNotice(75, 74, 70, 80), Is.True, "the higher of the two");
    }

    // ---- the data set ----

    static PackSet packs;
    static PackSet Packs()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        }
        return packs;
    }

    static MovieSource src;
    static GameTables tables;
    static GameTables Tables() => tables ??= new GameTables(Packs(), "en");

    /// <summary>A new archer's book (job 50, rank 1) where nothing is learnt yet, as a packet that says so would read.</summary>
    static JobSystem UnlearntArcher(out List<JobSkill> learnByRank)
    {
        var t = Tables().Skills;
        var jobs = new JobSystem(t);
        jobs.Read(1, 50, t.Jobs.GetJobData(50).Skills.Select(r => new BookSkill((int)r.SkillId, 1, false, false, false)));
        learnByRank = jobs.GetSkills(false, true).Where(s => s.IsLearn).ToList();
        return jobs;
    }

    [Test]
    public void LevelChangeLearnsTheRankSkillsOnce()
    {
        var jobs = UnlearntArcher(out var byRank);
        Assume.That(byRank, Is.Not.Empty, "the archer has skills learnt by rank");
        var rank1 = byRank.Where(s => s.JobLevel <= 1).ToList();
        var later = byRank.Where(s => s.JobLevel > 1).ToList();
        Assert.That(rank1, Is.Not.Empty);
        jobs.UpdateLearnable(10);
        Assert.That(rank1.Select(s => (s.Id, s.Learned, s.Changed)), Is.EqualTo(rank1.Select(s => (s.Id, true, true))),
            "learnt by the rank and changed: not learnt before");
        Assert.That(later.Select(s => s.Learned), Has.All.False, "a higher rank's skill stays unlearnt");
        // the sub skills of a learnt skill follow it
        var t = Tables().Skills;
        foreach (var s in rank1)
            foreach (uint sub in jobs.Job.FindSkill(s.Id)?.SubSkillIds ?? new List<uint>())
                if (jobs.FindSkill(sub) is { } ss && t.GetSkillData(sub) != null) Assert.That(ss.Learned, Is.True, $"sub skill {sub} of {s.Id}");
        // the next level change: still learnt, no longer changed
        jobs.UpdateLearnable(11);
        Assert.That(rank1.Select(s => (s.Learned, s.Changed)), Has.All.EqualTo((true, false)));
    }

    [Test]
    public void LearntSkillToastsOnePerChangedMainSkillAndOneSound()
    {
        var jobs = UnlearntArcher(out _);
        jobs.UpdateLearnable(10);
        var t = Tables();
        int expect = jobs.GetSkills(true, true).Count(s => s.Changed && t.Skills.GetSkillData(s.Id) != null);
        Assert.That(expect, Is.GreaterThan(0));
        var sounds = new List<string>();
        var c = new RewardNotifyController { Sound = sounds.Add };
        var (rt, mp) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        mp.StageW = 1280; mp.StageH = 720;
        var h = new UiHost(mp, c, new UiModel(t) { HasLocalPlayer = true });
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        c.Block = true;
        Assert.That(c.ShowLearnSkills(jobs), Is.EqualTo(expect));
        Assert.That(c.Pending, Is.EqualTo(expect), "one learn_skill toast each");
        Assert.That(sounds, Is.EqualTo(new[] { "PC_SkillGet" }), "PC_SkillGet once");
        // the next level: nothing changed, no toast, no sound
        jobs.UpdateLearnable(11);
        sounds.Clear();
        Assert.That(c.ShowLearnSkills(jobs), Is.EqualTo(0));
        Assert.That(sounds, Is.Empty);
    }

    /// <summary>
    /// The sweep: table/nextexp.xml (CExpTable::LoadNextExp: levels 1-99, the last available row of a level), the
    /// adventure level rewards' items and the level-up reward item (AdventureLevelLvUpRewardItem), the mission reward
    /// items, the common strings and keys the notices format, and the effect xmls of EFFECT_LEVEL_UP,
    /// EFFECT_ADVENTURE_LEVEL_UP, EFFECT_GET_EXP and EFFECT_GET_EXP_END. Prints the counts.
    /// </summary>
    [Test]
    public void SweepExpAndAdventureTables()
    {
        var p = Packs();
        var t = Tables();
        string set = Environment.GetEnvironmentVariable("MS2_DATA");
        // nextexp: the port's NextExp is the table's value for each level the client keeps
        var rows = XDocument.Load(new MemoryStream(p.Read("Xml", "table/nextexp.xml"))).Descendants("exp").ToList();
        var client = new Dictionary<int, long>();
        foreach (var e in rows)
        {
            if (!FeatureElements.IsAvailable(p, e)) continue;
            int lv = (int?)e.Attribute("level") ?? 0;
            if (lv is < 1 or > 99) continue;
            client[lv] = long.Parse((string)e.Attribute("value") ?? "0");
        }
        Assert.That(client.Keys.OrderBy(k => k), Is.EqualTo(Enumerable.Range(1, 99)), "every level 1-99 has a row");
        Assert.That(client.All(kv => t.NextExp(kv.Key) == kv.Value), Is.True, "the port's next exp is the client's row");
        Assert.That(client.OrderBy(kv => kv.Key).Select(kv => kv.Value), Is.Ordered.Ascending);
        TestContext.WriteLine($"{set} nextexp: {rows.Count} rows, {client.Count} levels kept, max {client[99]}");

        // adventure level tables
        XDocument Table(string name) => XDocument.Load(new MemoryStream(p.Read("Xml", "table/" + name + ".xml")));
        var adv = AdventureLevelTable.Load(Table("adventurelevelreward"), Table("adventurelevelability"), Table("adventurelevelmission"),
            e => FeatureElements.IsAvailable(p, e), true, t.Additional);
        var itemRewards = adv.Levels.Select(adv.GetReward).Where(r => r.Type == 1).ToList();
        var missingRewardItems = itemRewards.Where(r => t.Items.Get((int)r.Id) == null).Select(r => r.Id).ToList();
        int missions = 0; var missingMissionItems = new List<uint>();
        foreach (var e in Table("adventurelevelmission").Descendants().Where(e => e.Attribute("missionId") != null))
        {
            uint id = (uint?)e.Attribute("missionId") ?? 0;
            var m = adv.GetMission(id);
            if (ReferenceEquals(m, AdventureLevelTable.EmptyMission)) continue;
            missions++;
            if (m.ItemId != 0 && t.Items.Get((int)m.ItemId) == null) missingMissionItems.Add(m.ItemId);
        }
        int lvUpItem = FeatureElements.Constants(p).Int("AdventureLevelLvUpRewardItem", 0);
        TestContext.WriteLine($"{set} adventure: {adv.Levels.Count} reward levels, {itemRewards.Count} item rewards ({missingRewardItems.Count} without item data), "
            + $"{missions} missions ({missingMissionItems.Count} reward items without data), level-up item {lvUpItem}");
        Assert.That(missingRewardItems, Is.Empty, "every item reward has item data");
        Assert.That(missingMissionItems, Is.Empty, "every mission reward item has item data");
        Assert.That(lvUpItem, Is.Not.Zero);
        Assert.That(t.Items.Get(lvUpItem), Is.Not.Null, "the adventure level-up reward item");

        // the common strings and keys
        int[] ids = { 0x2bd, 0x2be, 0x2bf, 0x2c0, 0x2c1, 0x2c2, 0x681, 0x803, 0x804, 0xd22 };
        Assert.That(ids.Where(id => string.IsNullOrEmpty(t.Common(id))).Select(id => id.ToString("x")), Is.Empty, "common string ids");
        string[] keys = { "s_msg_take_play_instrument_exp", "s_msg_take_fishing_exp", "s_msg_take_arcade_exp", "s_msg_take_normal_chest_exp",
            "s_msg_take_normal_rare_exp", "s_msg_take_normal_rare_first_exp", "s_msg_take_event_additional", "s_msg_take_rest_exp",
            "s_msg_take_adventure_level_exp", "s_msg_skillbook_add_skillpoint" };
        var noText = keys.Where(k => string.IsNullOrEmpty(t.Common(k))).ToList();
        TestContext.WriteLine($"{set} strings: {ids.Length} ids, {keys.Length - noText.Count}/{keys.Length} keys with text" + (noText.Count > 0 ? " (none: " + string.Join(", ", noText) + ")" : ""));
        // the default chat windows (DefaultMessenger0..99.xml): the tabs that show the exp lines (kind 8, Require "experience")
        var browser = new MessengerBrowser();
        var tabs = new List<string>();
        foreach (var doc in t.DefaultMessengers)
            foreach (var tab in doc.Root?.Elements("Tab") ?? Enumerable.Empty<XElement>())
            {
                var panel = new PanelSetting();
                panel.Read(tab.Element("PanelSetting"), browser, _ => true);
                tabs.Add($"{tabs.Count}:{(panel.Hidden(ExpChatLines.ExpKind) ? "hidden" : "shown")}");
            }
        TestContext.WriteLine($"{set} chat tabs and the exp lines: {t.DefaultMessengers.Count} windows, {string.Join(" ", tabs)}");
        Assert.That(noText, Is.Empty, "common string keys");

        // the effects
        string[] fx = { "EFFECT_LEVEL_UP", "EFFECT_ADVENTURE_LEVEL_UP", "EFFECT_GET_EXP", "EFFECT_GET_EXP_END" };
        foreach (var k in fx)
        {
            string path = t.CommonResource(k);
            Assert.That(path, Is.Not.Empty, k);
            Assert.That(p.Exists("Xml", "effect/" + path.ToLowerInvariant()), Is.True, $"{k}: {path}");
        }
    }
}
