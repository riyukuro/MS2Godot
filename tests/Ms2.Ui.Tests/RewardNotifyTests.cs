using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>UIRewardNotify (uirewardnotify.cpp, UI id 0x4e5d) run by its own movie (needs MS2_DATA).</summary>
public class RewardNotifyTests
{
    static PackSet packs;
    static MovieSource src;

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

    static (UiHost Host, RewardNotifyController C, Player P) Load(int subScene = 0)
    {
        var model = new UiModel(new GameTables(Packs(), "en")) { HasLocalPlayer = true };
        var c = new RewardNotifyController { SubSceneId = () => subScene };
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => k;
        var h = new UiHost(p, c, model);
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();
        return (h, c, p);
    }

    static string NotifyType(UiHost h)
    {
        var n = h.Get(h.Entity, "m_notify");
        return n == null ? null : Rt.ToString(h.Invoke(n, "getType"));
    }

    [Test]
    public void MeratNoticeReachesTheMovieAsAMeratToast()
    {
        var (h, c, p) = Load();
        c.ShowMeratNotice(1500);
        for (int i = 0; i < 5; i++) p.Tick();
        Assert.That(c.Pending, Is.EqualTo(0));
        Assert.That(NotifyType(h), Is.EqualTo("merat"));
    }

    [Test]
    public void RedMeratNoticeIsTheMeratRedToast()
    {
        var (h, c, p) = Load();
        c.ShowMeratRedNotice(20);
        for (int i = 0; i < 5; i++) p.Tick();
        Assert.That(NotifyType(h), Is.EqualTo("merat_red"));
    }

    [Test]
    public void BlockedOrHeldSubSceneKeepsTheToastQueuedUntilItClears()
    {
        var (h, c, p) = Load();
        c.Block = true;
        c.ShowMeratNotice(5);
        Assert.That(c.Pending, Is.EqualTo(1));
        c.Update();
        Assert.That(c.Pending, Is.EqualTo(1), "still blocked");
        c.Block = false;
        c.Update();
        for (int i = 0; i < 5; i++) p.Tick();
        Assert.That(c.Pending, Is.EqualTo(0));
        Assert.That(NotifyType(h), Is.EqualTo("merat"));

        var (_, c2, _) = Load(RewardNotifyController.HeldSubScene);
        c2.ShowMeratNotice(5);
        Assert.That(c2.Pending, Is.EqualTo(1), "sub scene 0x6200031 holds it");
    }

    [Test]
    public void AchieveRewardPlaysTheTrophySoundAndSkillLevelUpOpensTheDialog()
    {
        var (_, c, _) = Load();
        var sounds = new List<string>(); var opened = new List<int>();
        c.Sound = sounds.Add; c.OpenDialog += opened.Add;
        c.OnCallback("onReward", new object[] { "merat" });
        Assert.That(sounds, Is.Empty);
        c.OnCallback("onReward", new object[] { "achieve" });
        Assert.That(sounds, Is.EqualTo(new[] { "system_Trophy_Hero" }));
        c.OnCallback("onSkillLevelUp", Array.Empty<object>());
        Assert.That(opened, Is.EqualTo(new[] { 0x4f2d }));
    }

    [Test]
    public void ToastStringsAndIconKeysResolveInTheData()
    {
        var t = new GameTables(Packs(), "en");
        foreach (int id in new[] { 0x681, 0x683, 0x687, 0x689, 0x68c, 0x68d, 0x68e, 0x68f, 0x804, 0x85b, 0xbd8, 0xbd9 })
            Assert.That(t.Common(id), Is.Not.Empty, "string 0x" + id.ToString("x"));
        foreach (var k in new[] { "s_system_achieve_reward_reverse_coin", "s_system_achieve_reward_star_point", "s_reward_notify_skill_point", "s_reward_notify_stat_point" })
            Assert.That(t.Common(k), Is.Not.Empty, k);
        foreach (var k in new[] { "meso_icon_path", "title_icon_path", "honor_token_icon_path", "karma_token_icon_path", "skill_point_icon_path", "stat_point_icon_path" })
            Assert.That(t.CommonResource(k), Is.Not.Empty, k);
    }

    [Test]
    public void ExpMoneyAndStampToastsReachTheMovieWithTheirTypes()
    {
        var (h, c, p) = Load();
        c.ShowExp(1200, 0);
        for (int i = 0; i < 5; i++) p.Tick();
        Assert.That(NotifyType(h), Is.EqualTo("exp"));
        c.RewardComplete();
        for (int i = 0; i < 40; i++) p.Tick();
        c.ShowStamp("m", 1, "tag");
        for (int i = 0; i < 5; i++) p.Tick();
        Assert.That(NotifyType(h), Is.EqualTo("stamp"));
    }

    [Test]
    public void SkillLevelUpAndFishingToastsAreQueuedByType()
    {
        var (h, c, p) = Load();
        c.ShowSkillLevelUp();
        for (int i = 0; i < 5; i++) p.Tick();
        Assert.That(NotifyType(h), Is.EqualTo("skillLevelUp"));
        c.ShowFishingSuccess(1, "Carp", "icon.png", 30, 3, 1);
        Assert.That(c.Pending, Is.EqualTo(0));
    }

    [Test]
    public void ChangedTokenMoneyIsIgnoredWithoutACountAndForUnknownKinds()
    {
        var (h, c, p) = Load();
        c.Block = true;
        c.NotifyChangedTokenMoney(3, 0);
        c.NotifyChangedTokenMoney(7, 5);
        Assert.That(c.Pending, Is.EqualTo(0));
        foreach (int kind in new[] { 3, 4, 5, 6, 9, 10, 11, 12, 13 }) c.NotifyChangedTokenMoney(kind, 12);
        Assert.That(c.Pending, Is.EqualTo(9));
    }

    [Test]
    public void SkillBookDialogCallDoesNotThrowBeforeAndAfterLoad()
    {
        var (_, c, _) = Load();
        Assert.DoesNotThrow(() => c.NotifySkillBookDialog(true));
        Assert.DoesNotThrow(() => new RewardNotifyController().NotifySkillBookDialog(false));
    }

    [Test]
    public void MeratMessageIsTheRawNumberWithoutSeparators()
    {
        var (_, c, _) = Load();
        string msg = c.MeratMessage(1234567);
        Assert.That(msg, Does.Contain("1234567"));
        Assert.That(msg, Does.Not.Contain("1,234,567"));
    }

    [Test]
    public void LearnEmotionToastsOnlyAnEmotionSkill()
    {
        var (_, c, _) = Load();
        c.Skill = id => id switch
        {
            7 => new Ms2.Ui.Skills.SkillData { Id = 7, MainType = 14, Icon = "emote.png" },
            8 => new Ms2.Ui.Skills.SkillData { Id = 8, MainType = 3, Icon = "other.png" },
            _ => null,
        };
        c.Block = true;
        c.OnLearnEmotion(0);
        c.OnLearnEmotion(8);
        Assert.That(c.Pending, Is.EqualTo(0));
        c.OnLearnEmotion(7);
        Assert.That(c.Pending, Is.EqualTo(1));
    }
}
