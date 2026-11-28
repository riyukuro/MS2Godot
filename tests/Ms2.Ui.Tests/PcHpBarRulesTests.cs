using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>Another player's head bar under PvP (UIHpBar::Entity of a PC, uihpbar.cpp; PC::vfunc_80, pc.cpp:2757).</summary>
public class PcHpBarRulesTests
{
    [Test]
    public void APointOrAHostilePcGetsABarWithoutAParty()
    {
        Assert.That(PcHpBarRules.Makes(partyGauge: true, hasPoint: false, partyMember: false, hostile: false), Is.False, "a stranger: none");
        Assert.That(PcHpBarRules.Makes(true, hasPoint: true, partyMember: false, hostile: false), Is.True, "a PvP point");
        Assert.That(PcHpBarRules.Makes(true, false, false, hostile: true), Is.True, "hostile on a pk field");
        Assert.That(PcHpBarRules.Makes(true, false, partyMember: true, false), Is.True);
        Assert.That(PcHpBarRules.Makes(partyGauge: false, true, true, true), Is.False, "the +0xc option gates every PC's bar");
    }

    [Test]
    public void AHostilePcsBarFollowsTheMonsterOption()
    {
        Assert.That(PcHpBarRules.Gate(partyGauge: false, monsterGauge: true, partyMember: false, hostile: true), Is.True);
        Assert.That(PcHpBarRules.Gate(partyGauge: true, monsterGauge: false, partyMember: true, hostile: true), Is.False,
            "a hostile party member still reads the monster option");
        Assert.That(PcHpBarRules.Gate(true, false, partyMember: true, hostile: false), Is.True);
        Assert.That(PcHpBarRules.Gate(true, true, partyMember: false, hostile: false), Is.False,
            "a teammate with a point outside the party: its bar exists but is not shown");
    }

    [Test]
    public void APointPcShowsAtAnyDistanceUnlessItsBuffsHideIt()
    {
        Assert.That(PcHpBarRules.Hidden(hostile: false, buffHidden: false, rideHidesTag: false, hasPoint: true, inBattle: false, hurt: false, far: true), Is.False);
        Assert.That(PcHpBarRules.Hidden(false, buffHidden: true, false, hasPoint: true, false, false, far: false), Is.True);
        Assert.That(PcHpBarRules.Hidden(false, false, false, hasPoint: false, false, false, far: true), Is.True, "no point, full HP, far");
        Assert.That(PcHpBarRules.Hidden(false, false, false, false, inBattle: true, false, far: true), Is.False);
        Assert.That(PcHpBarRules.Hidden(false, false, false, false, false, hurt: true, far: true), Is.False);
    }

    [Test]
    public void AHostilePcHiddenByItsBuffsHidesEvenInBattle()
    {
        Assert.That(PcHpBarRules.Hidden(hostile: true, buffHidden: true, false, false, inBattle: true, hurt: true, far: false), Is.True);
        Assert.That(PcHpBarRules.Hidden(hostile: false, buffHidden: true, false, false, inBattle: true, hurt: false, far: false), Is.False,
            "a friendly PC's buff hide is not read without a point");
        Assert.That(PcHpBarRules.Hidden(false, false, rideHidesTag: true, hasPoint: true, true, true, false), Is.True);
    }

    [Test]
    public void ThePointPicksTheFrameAndTheRelationTheGauge()
    {
        Assert.That((PcHpBarRules.Rank(true), PcHpBarRules.Rank(false)), Is.EqualTo((100, 1)));
        Assert.That((PcHpBarRules.Friendly(self: false, hostile: true), PcHpBarRules.Friendly(false, false), PcHpBarRules.Friendly(true, true)),
            Is.EqualTo((false, true, true)));
    }

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void TheHpBarMovieHasTheFramesTheRulesOpen(string env)
    {
        var d = System.Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(d) || !System.IO.Directory.Exists(d)) Assert.Ignore($"{env} is not set");
        var packs = TestData.Packs(d);
        var movie = Ms2.Swf.SwfMovie.Parse(packs.Read("Resource/Gfx", "uihpbar.gfx"));
        var labels = new System.Collections.Generic.HashSet<string>();
        foreach (var c in movie.Characters.Values) if (c.Timeline != null) foreach (var l in c.Timeline.Labels) labels.Add(l.Name);
        Assert.That(labels, Does.Contain("_rank" + PcHpBarRules.PvpRank).And.Contain("_rank1").And.Contain("friendly").And.Contain("hostile"));
    }
}
