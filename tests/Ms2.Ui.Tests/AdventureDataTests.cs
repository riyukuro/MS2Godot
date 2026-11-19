using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
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

    /// <summary>A relog: the entry's packets replay in arrival order (ServerEnter, then the Prestige kinds 0 and 7 the server
    /// sends before the field enter). ServerEnter clears the old character's missions alone; kind 0 then replaces the claims.</summary>
    [Test]
    public void TheEntrysPrestigeLoadsReachTheData()
    {
        var d = new AdventureData();
        d.Apply(new PrestigeMessage.MissionList(7, 1, new Dictionary<uint, (uint, bool)> { [99] = (1, true) }), null, true);
        d.Apply(new PrestigeMessage.ClaimResult(1, new SortedSet<uint> { 8 }), null, true);
        static RawPacket Frame(Action<ByteWriter> body) { var w = new ByteWriter(); body(w); return new RawPacket((ushort) Inbound.Prestige, w.Buffer.AsSpan(0, w.Length).ToArray()); }
        var entry = new GameEntry(null, null, null)
        {
            Early = new[]
            {
                Frame(w => { w.WriteByte(0); w.WriteLong(300); w.WriteInt(4); w.WriteLong(900); w.WriteInt(2); w.WriteInt(1); w.WriteInt(3); }),
                Frame(w => { w.WriteByte(7); w.WriteInt(1); w.WriteLong(11); w.WriteLong(2); w.WriteByte(0); }),
            },
            EarlyBeforeEnter = 0,
        };
        var order = new List<string>();
        entry.Replay(p => { order.Add("prestige"); d.Apply(PrestigeMessage.Read(p.Reader()), null, true); },
            () => { order.Add("enter"); Assert.That(d.Missions.Count, Is.EqualTo(1)); d.ServerEnter(); Assert.That(d.Claimed, Is.EqualTo(new uint[] { 8 }), "the claims stay"); });
        Assert.That(order, Is.EqualTo(new[] { "enter", "prestige", "prestige" }));
        Assert.That((d.Exp, d.FilledLevel, d.NextExp), Is.EqualTo((300L, 4, 900L)));
        Assert.That(d.Claimed, Is.EqualTo(new uint[] { 1, 3 }));
        Assert.That(d.Missions.Keys, Is.EqualTo(new uint[] { 11 }), "the old missions are gone");
        var pc = new PlayerState { AdventureLevel = 5, Adventure = d };
        Assert.That(AdventureLevelController.GuideState(pc, 3), Is.EqualTo("inactive"), "a claimed level is not offered again");
    }

    [Test]
    public void ServerEnterWithoutAMissionListLeavesNone()
    {
        var d = new AdventureData();
        d.Apply(new PrestigeMessage.MissionList(7, 1, new Dictionary<uint, (uint, bool)> { [99] = (1, true) }), null, true);
        new GameEntry(null, null, null).Replay(_ => { }, d.ServerEnter);
        Assert.That(d.Missions, Is.Empty);
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
