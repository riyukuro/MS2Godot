using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The session-lived adventure data (<see cref="AdventureData"/>, the client's OnPrestige globals).</summary>
public class AdventureDataTests
{
    static readonly Dictionary<uint, (uint, bool)> NoMissions = new();

    /// <summary>A claim, then a field move: the new HUD's player binds the session's data and keeps the claimed level,
    /// so the guide shows it inactive and no second claim is offered.</summary>
    [Test]
    public void ClaimSurvivesAPortal()
    {
        var session = new AdventureData();
        var before = new PlayerState { AdventureLevel = 7, Adventure = session };
        session.Apply(new PrestigeMessage.Loaded(300, 4, 900, new SortedSet<uint> { 1, 2 }), null, true);
        session.Apply(new PrestigeMessage.MissionList(7, 1, new Dictionary<uint, (uint, bool)> { [11] = (3, false) }), null, true);
        Assert.That(AdventureLevelController.GuideState(before, 5), Is.EqualTo("active"));
        session.Apply(new PrestigeMessage.ClaimResult(1, new SortedSet<uint> { 5 }), null, true);
        Assert.That(AdventureLevelController.GuideState(before, 5), Is.EqualTo("inactive"));

        var after = new PlayerState { AdventureLevel = 7, Adventure = session };     // the new field's HUD
        Assert.That(AdventureLevelController.GuideState(after, 5), Is.EqualTo("inactive"), "the claim is kept");
        Assert.That(AdventureLevelController.GuideState(after, 6), Is.EqualTo("active"));
        Assert.That(AdventureLevelController.GuideState(after, 8), Is.EqualTo("lock"));
        Assert.That((after.AdventureExp, after.AdventureFilledLevel, after.AdventureNextExp), Is.EqualTo((300L, 4, 900L)));
        Assert.That(after.AdventureClaimed, Is.EqualTo(new uint[] { 1, 2, 5 }));
        Assert.That(after.AdventureMissions[11], Is.EqualTo((3u, false)));
    }

    [Test]
    public void KindsWriteWhatTheClientWrites()
    {
        var d = new AdventureData();
        d.Apply(new PrestigeMessage.ExpGain(40, 10), null, true);
        Assert.That(d.Exp, Is.EqualTo(40));
        d.Apply(new PrestigeMessage.LevelUp(9, 1), null, true);
        Assert.That(d.FilledLevel, Is.EqualTo(0), "another PC's level up");
        d.Apply(new PrestigeMessage.LevelUp(9, 2), 1, true);
        Assert.That(d.FilledLevel, Is.EqualTo(0), "the own level from 1");
        d.Apply(new PrestigeMessage.LevelUp(9, 1), 0, true);
        Assert.That(d.FilledLevel, Is.EqualTo(1), "the own first level fills level 1");
        d.Apply(new PrestigeMessage.ClaimResult(0, new SortedSet<uint> { 3 }), null, true);
        Assert.That(d.Claimed, Is.Empty, "a failed claim");
        d.Apply(new PrestigeMessage.MissionList(7, 1, new Dictionary<uint, (uint, bool)> { [1] = (1, false) }), null, false);
        Assert.That(d.Missions, Is.Empty, "no missions without feature 0xfb");
        d.Apply(new PrestigeMessage.MissionList(7, 1, new Dictionary<uint, (uint, bool)> { [1] = (1, false) }), null, true);
        d.Apply(new PrestigeMessage.MissionList(6, 0, NoMissions), null, true);
        Assert.That(d.Missions.Count, Is.EqualTo(1), "kind 6 with a failed result keeps them");
        d.Apply(new PrestigeMessage.MissionList(6, 1, NoMissions), null, true);
        Assert.That(d.Missions, Is.Empty);
    }

    [Test]
    public void TheLiveLevelComesFromTheUserBlock()
    {
        var p = new PlayerState();
        var me = new MyCharacter { User = new UserBlock { Name = "A", Level = 10, PrestigeLevel = 12 } };
        Assert.That(p.Apply(me), Does.Contain(StatusEvent.AdventureLevel));
        Assert.That(p.AdventureLevel, Is.EqualTo(12));
    }
}
