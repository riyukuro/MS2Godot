using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CAdventureLevelTable over the client's tables (needs MS2_DATA).</summary>
public class AdventureLevelTableTests
{
    static PackSet packs;

    static PackSet Packs()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = TestData.Packs(d);
        }
        return packs;
    }

    static XDocument Table(string name) => XDocument.Load(new MemoryStream(Packs().Read("Xml", "table/" + name + ".xml")));

    static AdventureLevelTable Load(bool missions) =>
        AdventureLevelTable.Load(Table("adventurelevelreward"), Table("adventurelevelability"), Table("adventurelevelmission"),
            e => FeatureElements.IsAvailable(Packs(), e), missions, new GameTables(Packs(), "en").Additional);

    [Test]
    public void RewardsAreItemsOrStatPoints()
    {
        var t = Load(true);
        Assert.That(t.GetReward(2), Is.EqualTo(new AdventureLevelTable.Reward(1, 20300555, 1, 1)));
        Assert.That(t.GetReward(3), Is.EqualTo(new AdventureLevelTable.Reward(2, 0, 0, 2)));
        Assert.That(t.GetReward(1), Is.SameAs(AdventureLevelTable.EmptyReward));
        Assert.That(t.Levels, Is.Ordered);
        Assert.That(t.Levels.Take(3), Is.EqualTo(new uint[] { 2, 3, 4 }));
        // the rewards at or below level 5 but the stat points, less the claimed ones: 2 and 4, then 4 alone
        Assert.That(t.CountUnclaimedRewards(5, new SortedSet<uint>()), Is.EqualTo(2));
        Assert.That(t.CountUnclaimedRewards(5, new SortedSet<uint> { 2 }), Is.EqualTo(1));
    }

    [Test]
    public void IsReachedNeedsTheCurrentAbilitysRequiredLevel()
    {
        // CAdventureLevelTableHelper::IsReached @0x1401955f0
        var t = Load(true);
        if (t.Current == null) { Assert.That(t.IsReached(1000), Is.False, "no current ability"); return; }
        Assert.That(t.IsReached(t.Current.RequireLevel), Is.True);
        if (t.Current.RequireLevel > 0) Assert.That(t.IsReached(t.Current.RequireLevel - 1), Is.False);
    }

    [Test]
    public void AbilityStepsFollowTheInterval()
    {
        var t = Load(true);
        var a = t.GetAbility(100000028);        // requireLevel 5, interval 50, maxCount 20, 0.04 + 0.02 per step
        Assert.That((a.Id, a.RequireLevel, a.Interval, a.MaxCount), Is.EqualTo((1, 5u, 50, 20)));
        Assert.That(a.GetCount(4), Is.EqualTo(0));
        Assert.That(a.GetCount(5), Is.EqualTo(1));
        Assert.That(a.GetCount(54), Is.EqualTo(1));
        Assert.That(a.GetCount(55), Is.EqualTo(2));
        Assert.That(a.GetCount(100000), Is.EqualTo(20));
        Assert.That(a.GetLevelOfCount(60), Is.EqualTo(55));
        Assert.That(a.GetPercent(55), Is.EqualTo(6f).Within(1e-4));
        Assert.That(t.AbilityIds.Select(id => t.GetAbility(id).RequireLevel), Is.Ordered);
        Assert.That(t.GetAbility(1), Is.SameAs(AdventureLevelTable.EmptyAbility));
        TestContext.Progress.WriteLine($"adventure level: {t.AbilityIds.Count} abilities, current {t.Current?.EffectId.ToString() ?? "none"}");
        if (t.Current != null) Assert.That(t.AbilityIds, Does.Contain(t.Current.EffectId));
    }

    [Test]
    public void MissionsLoadOnlyWithTheFeature()
    {
        Assert.That(Load(true).GetMission(2), Is.EqualTo(new AdventureLevelTable.Mission(2, 1, 20302616, 4, 1)));
        Assert.That(Load(false).GetMission(2), Is.SameAs(AdventureLevelTable.EmptyMission));
    }
}
