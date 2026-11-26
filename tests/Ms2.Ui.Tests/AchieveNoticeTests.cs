using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2.Ui.Guide;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// CAchieveManager::OnPacket mode 2's notices (AchieveNotices through AchieveBook.Apply) over a small trophy book: NotifyClear,
/// NotifyReward of each reward type and the main player info's progress lines, in the client's order.
/// </summary>
public class AchieveNoticeTests
{
    static AchieveProgress P(byte state, int cur, int start, long count, params (int Grade, long Value)[] values) =>
        new(state, 0, cur, start, false, count, values.ToDictionary(v => v.Grade, v => v.Value));

    static AchieveGrade G(int grade, long goal, int type = 0, uint code = 0, uint value = 0, int rank = 1, int subJob = 1,
                          Dictionary<string, string> extra = null) =>
        new(grade, new QuestCondition { Type = 41, Goal = goal },
            new AchieveReward { Type = type, Code = code, Value = value, Rank = rank, SubJob = subJob, Extra = extra ?? new() });

    static AchieveData D(int id, int noticePercent, params AchieveGrade[] grades) =>
        new(id, id / 10000000, (id / 1000000) % 10, 1, grades.ToDictionary(g => g.Grade, _ => 41))
        {
            MaxGrade = grades.Length, Icon = "medal.png", NoticePercent = noticePercent, Grades = grades.ToDictionary(g => g.Grade),
        };

    sealed class Toasts(List<string> log) : IAchieveToasts
    {
        public void ShowAchieve(string name, string icon, int achieveType) => log.Add($"toast achieve {name} {icon} {achieveType}");
        public void ShowItemReward(int itemId, int grade) => log.Add($"toast item {itemId} {grade}");
        public void ShowStatPoint(int points) => log.Add($"toast stat {points}");
        public void ShowSkillPoint(int points, int kind) => log.Add($"toast skill {points} {kind}");
        public void ShowIconNotice(string icon, string msg, int effect) => log.Add($"toast icon {icon} {msg} {effect}");
    }

    static readonly Dictionary<string, string> Strings = new()
    {
        ["s_achieve_get_achieve_hero_progress"] = "earned {0}",
        ["s_achieve_get_achieve_hero_complete"] = "final {0}",
        ["s_achieve_tooltip_user_info"] = "{0} - {1}",
        ["s_achieve_state_progress"] = "in progress",
        ["s_achieve_state_complete"] = "unlocked",
        ["s_achieve_title"] = "{0}. {1}",
        ["s_achieve_progress_notify_float"] = " {0} [{1:0.1f}%]",
        ["s_achieve_progress_notify_integer"] = " {0} [{1}%]",
        ["s_reward_notify_stat_point"] = "stat x{0}",
        ["s_reward_notify_skill_point"] = "skill x{0}",
        ["s_reward_notify_skill_point_sub_1"] = "rank 2 skill x{0}",
        ["s_system_achieve_reward_shop_unlock"] = "shop {0}",
        ["s_system_achieve_reward_shop_auth_notify"] = "shop line {0}",
        ["s_system_achieve_reward_beauty"] = "beauty {0}",
        ["s_system_achieve_reward_beauty_auth_notify"] = "beauty line {0}",
        ["s_system_achieve_reward_dynamicaction"] = "action {0}",
        ["s_system_achieve_reward_dynamicaction_auth_notify"] = "action line {0}",
        ["s_system_achieve_reward_auth_notify_manual"] = "obtained {0}",
        ["s_achieve_reward_coloring"] = "colour {0}",
        ["s_system_achieve_reward_coloring"] = "coloring {0}",
        ["s_system_achieve_reward_coloring_auth_notify"] = "coloring line {0}",
    };

    sealed class Rig
    {
        public AchieveBook Book;
        public AchieveNotices Notices;
        public readonly List<string> Log = new();
        public int? Gender = 0;
        public bool FeatureBb = true, FeatureBc = true;
        public int TextsMade;
        public ColorPaletteTable Palettes = ColorPaletteTable.Empty;
    }

    static Rig Make(Dictionary<ulong, AchieveTexts.SpecialReward> specials, params AchieveData[] data) => Make(specials, false, data);

    static Rig Make(Dictionary<ulong, AchieveTexts.SpecialReward> specials, bool lazyTexts, params AchieveData[] data)
    {
        var rig = new Rig();
        rig.Book = new AchieveBook(data.ToDictionary(d => d.Id), data);
        var texts = new AchieveTexts(data.ToDictionary(d => d.Id, d => new AchieveTexts.Desc(d.Id, "", "", "", "")), specials ?? new())
        {
            Data = rig.Book.DataOf, Find = rig.Book.Find,
            CommonByKey = k => Strings.TryGetValue(k, out var v) ? v : "",
            TableString = (table, code) => table == 0x18 ? $"Trophy{code}" : $"S{table}:{code}",
            FeatureOn = f => f == 0xbb ? rig.FeatureBb : f == 0xbc && rig.FeatureBc,
            Gender = () => rig.Gender,
            PaletteColors = p => rig.Palettes.Rows(p),
        };
        var toasts = new Toasts(rig.Log);
        Func<AchieveTexts> made = () => { rig.TextsMade++; return texts; };
        rig.Notices = new AchieveNotices(lazyTexts ? made : () => texts)
        {
            Toasts = () => toasts,
            Line = (text, kind) => rig.Log.Add($"line {kind:x} {text}"),
            Trigger = (type, value) => rig.Log.Add($"trigger {type:x} {value}"),
            PlayerName = () => "Me",
            SkillIcon = code => code == 0 ? null : $"skill{code}.png",
            AppendMessage = (msg, type) => rig.Log.Add($"{type}{msg}"),
        };
        rig.Notices.Cleared += id => rig.Log.Add($"cleared {id}");
        rig.Book.Notices = rig.Notices;
        return rig;
    }

    const string Medal = ".\\Data\\Resource\\Image\\Emblem\\Medal\\Icon\\medal.png";

    static string Link(int id, int grade, string text, string title) =>
        MessengerBrowser.LinkMarkup($"'event:achieveTooltip,{id},4,{grade},{text},'", "#ffffff", title);

    [Test]
    public void LazyTextsAreMadeOnTheFirstUpdateOnly()
    {
        var rig = Make(null, true, D(21100001, 1, G(1, 10, 1, 20000001, 1, 3), G(2, 20, 3, 0, 5)));
        rig.Book.Apply(new AchieveMessage.Rows(new[] { (21100001, 1, P(2, 1, 1, 4)) }));
        Assert.That(rig.TextsMade, Is.Zero, "mode 1 reads no text");
        rig.Book.Apply(new AchieveMessage.Update(21100001, P(2, 2, 1, 0)));
        Assert.That(rig.TextsMade, Is.EqualTo(1), "the first mode 2 update makes the texts");
        Assert.That(rig.Log, Does.Contain($"toast achieve Trophy21100001 {Medal} 2"));
        rig.Book.Apply(new AchieveMessage.Update(21100001, P(2, 2, 1, 5)));
        Assert.That(rig.TextsMade, Is.EqualTo(1), "made once");
    }

    [Test]
    public void AGradeClearedTheRewardTakenAndProgressNotifyInTheClientsOrder()
    {
        var rig = Make(null, D(21100001, 1, G(1, 10, 1, 20000001, 1, 3), G(2, 20, 3, 0, 5), G(3, 40, 4, 0, 2, 1, 2)));
        rig.Book.Apply(new AchieveMessage.Rows(new[] { (21100001, 1, P(2, 1, 1, 4)) }));
        Assert.That(rig.Log, Is.Empty, "mode 1 sends no notice");

        // grade 1 reached: NotifyClear of grade 1 (the one before the current), then no reward (the start grade stayed)
        // and the progress of grade 2's goal: 0 of 20 is under 1%
        Assert.That(rig.Book.Apply(new AchieveMessage.Update(21100001, P(2, 2, 1, 0))), Is.True);
        Assert.That(rig.Log, Is.EqualTo(new[]
        {
            $"toast achieve Trophy21100001 {Medal} 2",
            "trigger 17 0",
            "line 14 earned " + Link(21100001, 1, "Me - unlocked", "Ⅰ. Trophy21100001"),
            "cleared 21100001",
        }));
        rig.Log.Clear();

        // progress 5 / 20: 25%, a line; the same count again: no step
        rig.Book.Apply(new AchieveMessage.Update(21100001, P(2, 2, 1, 5)));
        rig.Book.Apply(new AchieveMessage.Update(21100001, P(2, 2, 1, 5)));
        Assert.That(rig.Log, Is.EqualTo(new[] { "achieve Trophy21100001 [25%]" }));
        Assert.That(rig.Book.Find(21100001)!.LastNotifiedPercent, Is.EqualTo(25f));
        rig.Log.Clear();

        // the grade 1 reward taken (the start grade 1 -> 2): NotifyReward(id, 1), an item toast of its rank, the
        // AchieveReward trigger of type 1; then the progress (6 / 20 = 30%)
        rig.Book.Apply(new AchieveMessage.Update(21100001, P(2, 2, 2, 6)));
        Assert.That(rig.Log, Is.EqualTo(new[] { "toast item 20000001 3", "trigger 3 1", "achieve Trophy21100001 [30%]" }));
        rig.Log.Clear();

        // 99% and up: the float line each time, whatever the step
        rig.Book.Apply(new AchieveMessage.Update(21100001, P(2, 2, 2, 20)));
        rig.Book.Apply(new AchieveMessage.Update(21100001, P(2, 2, 2, 20)));
        Assert.That(rig.Log, Is.EqualTo(new[] { "achieve Trophy21100001 [100.0%]", "achieve Trophy21100001 [100.0%]" }));
        rig.Log.Clear();

        // the last grade complete (state 3 at grade 3): the final line of grade 3 (the current one), no progress line
        rig.Book.Apply(new AchieveMessage.Update(21100001, P(3, 3, 2, 40)));
        Assert.That(rig.Log, Is.EqualTo(new[]
        {
            $"toast achieve Trophy21100001 {Medal} 2",
            "trigger 17 0",
            "line 14 final " + Link(21100001, 3, "Me - unlocked", "Ⅲ. Trophy21100001"),
            "cleared 21100001",
        }));
        rig.Log.Clear();

        // the stat (grade 2) and skill (grade 3, sub job 2) rewards taken
        rig.Book.Apply(new AchieveMessage.Update(21100001, P(3, 3, 3, 40)));
        rig.Book.Apply(new AchieveMessage.Update(21100001, P(3, 3, 4, 40)));
        Assert.That(rig.Log, Is.EqualTo(new[]
        {
            "toast stat 5", "line 14 stat x5", "trigger 3 3",
            "toast skill 2 2", "line 14 rank 2 skill x2", "trigger 3 4",
        }));
    }

    [Test]
    public void TheLinkSaysInProgressForTheCurrentGradeAndNothingWithoutAPlayer()
    {
        var rig = Make(null, D(21100001, 1, G(1, 10), G(2, 20)));
        rig.Book.Apply(new AchieveMessage.Rows(new[] { (21100001, 1, P(2, 2, 1, 4)) }));
        Assert.That(rig.Notices.AchieveLink(21100001, 2), Is.EqualTo(Link(21100001, 2, "Me - in progress", "Ⅱ. Trophy21100001")));
        Assert.That(rig.Notices.AchieveLink(21100001, 1), Is.EqualTo(Link(21100001, 1, "Me - unlocked", "Ⅰ. Trophy21100001")));
        Assert.That(rig.Notices.AchieveLink(29999999, 1), Is.EqualTo(""), "not the player's trophy");
        rig.Notices.PlayerName = () => null;
        Assert.That(rig.Notices.AchieveLink(21100001, 2), Is.EqualTo(Link(21100001, 2, "", "Ⅱ. Trophy21100001")));
    }

    [Test]
    public void NoticePercentIsTheStepBetweenProgressLines()
    {
        var rig = Make(null, D(21100001, 10, G(1, 100)));
        rig.Book.Apply(new AchieveMessage.Rows(new[] { (21100001, 1, P(2, 1, 1, 0)) }));
        foreach (int count in new[] { 5, 10, 15, 19, 20, 35 })
            rig.Book.Apply(new AchieveMessage.Update(21100001, P(2, 1, 1, count)));
        Assert.That(rig.Log, Is.EqualTo(new[] { "achieve Trophy21100001 [10%]", "achieve Trophy21100001 [20%]", "achieve Trophy21100001 [35%]" }));
    }

    [Test]
    public void TheGuideLimitOrNoMainPlayerInfoHoldTheProgressLineBack()
    {
        var rig = Make(null, D(21100001, 1, G(1, 100)));
        rig.Book.Apply(new AchieveMessage.Rows(new[] { (21100001, 1, P(2, 1, 1, 0)) }));
        rig.Notices.ProgressRestricted = () => true;
        rig.Book.Apply(new AchieveMessage.Update(21100001, P(2, 1, 1, 30)));
        rig.Notices.ProgressRestricted = () => false;
        rig.Notices.HasPlayerInfo = () => false;
        rig.Book.Apply(new AchieveMessage.Update(21100001, P(2, 1, 1, 40)));
        Assert.That(rig.Log, Is.Empty);
        Assert.That(rig.Book.Find(21100001)!.LastNotifiedPercent, Is.EqualTo(0f), "the window's absence leaves the trophy's last percentage");
        rig.Notices.HasPlayerInfo = () => true;
        rig.Book.Apply(new AchieveMessage.Update(21100001, P(2, 1, 1, 41)));
        Assert.That(rig.Log, Is.EqualTo(new[] { "achieve Trophy21100001 [41%]" }));
    }

    [Test]
    public void AType8TrophyNotifiesNothing()
    {
        var rig = Make(null, D(81100001, 1, G(1, 10, 1, 20000001), G(2, 20)));
        rig.Book.Apply(new AchieveMessage.Rows(new[] { (81100001, 1, P(2, 1, 1, 0)) }));
        Assert.That(rig.Book.Apply(new AchieveMessage.Update(81100001, P(2, 2, 2, 5))), Is.False, "no menu or character window refresh");
        Assert.That(rig.Log, Is.Empty);
        Assert.That(rig.Book.Pending, Does.Contain(81100001));
    }

    [Test]
    public void TheShopBeautyActionAndSpecialRewardsShowTheirNotices()
    {
        const int id = 21100001;
        var male = new Dictionary<string, string> { ["male"] = "11300001", ["female"] = "11400001" };
        var specials = new Dictionary<ulong, AchieveTexts.SpecialReward>
        {
            [(ulong)id << 32 | 9] = new("a special", "special.png", "special notice"),
        };
        var rig = Make(specials, D(id, 1,
            G(1, 1, 5, 15000001), G(2, 1, 6, 50000001), G(3, 1, 7, 50100001), G(4, 1, 10, 0, 0, 1, 1, male),
            G(5, 1, 12, 90200001), G(6, 1, 9, 0, 0, 1, 1, male), G(7, 1, 8, 7), G(8, 1, 2, 30), G(9, 1, 1, 20000001)));
        rig.Book.Apply(new AchieveMessage.Rows(new[] { (id, 1, P(3, 9, 1, 1)) }));
        // palette 7: two colours of this trophy's grade 7 (in file order, a repeated colorSN kept), one of grade 6, one of
        // another trophy and one of none
        rig.Palettes = ColorPaletteTable.Parse(System.Xml.Linq.XElement.Parse($@"<ms2><colorPalette id=""7"">
            <color ch0=""ff010101"" stringKey=""31"" colorSN=""1"" achieveID=""{id}"" achieveGrade=""7"" />
            <color ch0=""ff020202"" stringKey=""32"" colorSN=""2"" achieveID=""{id}"" achieveGrade=""6"" />
            <color ch0=""ff030303"" stringKey=""33"" colorSN=""3"" achieveID=""21100002"" achieveGrade=""7"" />
            <color ch0=""ff040404"" stringKey=""34"" colorSN=""4"" />
            <color ch0=""ff050505"" stringKey=""35"" colorSN=""1"" achieveID=""{id}"" achieveGrade=""7"" />
            </colorPalette></ms2>"));
        for (int g = 1; g <= 9; g++) rig.Notices.NotifyReward(id, g);
        const string etc = "./Data/Resource/Image/ETC/";
        Assert.That(rig.Log, Is.EqualTo(new[]
        {
            $"toast icon {etc}weaponshop_unlock.png shop S1:15000001 0", "line 1c shop line S1:15000001", "trigger 3 5",
            $"toast icon {etc}buildshop_unlock.png shop S1:50000001 0", "line 1c shop line S1:50000001", "trigger 3 6",
            $"toast icon {etc}rideshop_unlock.png shop S1:50100001 0", "line 1c shop line S1:50100001", "trigger 3 7",
            $"toast icon {etc}customizeshop_unlock.png beauty S1:11300001 0", "line 1c beauty line S1:11300001", "trigger 3 10",
            "toast icon skill90200001.png action S4:90200001 0", "line 1c action line S4:90200001", "trigger 3 12",
            $"toast icon {etc}customizeshop_unlock.png beauty S1:11300001 0", "line 1c beauty line S1:11300001", "trigger 3 9",
            $"toast icon {etc}colorpalette_unlock.png coloring S72:31 0", "line 1c coloring line S72:31",
            $"toast icon {etc}colorpalette_unlock.png coloring S72:35 0", "line 1c coloring line S72:35", "trigger 3 8",
            "trigger 3 2",          // a title: no toast
            "toast icon special.png special notice 0", "line 1c obtained a special", "trigger 3 1",
        }));

        // the reward text (FUN_14093ce70): a line per colour of this trophy and grade
        Assert.That(rig.Notices.Texts.RewardText(id, 7), Is.EqualTo("colour S72:31\ncolour S72:35"));
        Assert.That(rig.Notices.Texts.RewardText(id, 6), Is.EqualTo(""), "grade 6's reward is a beauty item");

        // a female player gets the female row; no local player ends a type 9 notice before the trigger and shows no
        // type 10 one; the features off show none of theirs
        rig.Log.Clear();
        rig.Gender = 1;
        rig.Notices.NotifyReward(id, 4);
        rig.Gender = null;
        rig.Notices.NotifyReward(id, 4);
        rig.Notices.NotifyReward(id, 6);
        rig.Gender = 0; rig.FeatureBb = false; rig.FeatureBc = false;
        rig.Notices.NotifyReward(id, 4);
        rig.Notices.NotifyReward(id, 5);
        rig.Notices.NotifyReward(id, 6);
        rig.Notices.NotifyReward(id, 10);      // no such grade: nothing
        Assert.That(rig.Log, Is.EqualTo(new[]
        {
            $"toast icon {etc}customizeshop_unlock.png beauty S1:11400001 0", "line 1c beauty line S1:11400001", "trigger 3 10",
            "trigger 3 10",
            "trigger 3 10", "trigger 3 12", "trigger 3 9",
        }));

        // no reward notify window: nothing at all
        rig.Log.Clear();
        rig.Notices.Toasts = () => null;
        rig.Notices.NotifyReward(id, 1);
        Assert.That(rig.Log, Is.Empty);
    }

    [Test]
    public void TheGuideNamesHaveTheClientsValues()
    {
        Assert.That((int)GuideLimit.ProgressAchieve, Is.EqualTo(0x16));
        Assert.That((int)GuideTrigger.AchieveReward, Is.EqualTo(3));
        Assert.That((int)GuideTrigger.CanAchieveReward, Is.EqualTo(0x17));
        Assert.That(TalkKind.Auth, Is.EqualTo(0x1c));
        Assert.That(TalkKind.SystemNotice, Is.EqualTo(0x14));
    }
}
