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
}
