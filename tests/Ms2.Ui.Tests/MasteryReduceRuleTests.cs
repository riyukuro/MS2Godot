using Ms2.Formats.World;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CGameEventValueMasteryReduce (gameeventvalue.cpp:640-662) as the game event list sets it (GameEventManagerCli Add,
/// Remove, Update and Clear with GameEventMasteryReduce's OnStart and vfunc_6).</summary>
public class MasteryReduceRuleTests
{
    static GameEventMessage Events(byte kind, params MasteryReduceEvent[] e) =>
        new(kind, Array.Empty<ShutdownInteractObjectEvent>(), Array.Empty<int>(), false) { MasteryReduceEvents = e };

    static GameEventMessage Removed(params int[] ids) =>
        new(2, Array.Empty<ShutdownInteractObjectEvent>(), ids, false);

    [Test]
    public void TheLastStartedEventsRateHoldsUntilAnEventEnds()
    {
        var r = new MasteryReduceRule();
        Assert.That((r.Rate, r.Apply(1000)), Is.EqualTo((0, 1000u)), "no event: the value as it is");
        r.Apply(Events(0, new MasteryReduceEvent(5, 20000)));
        Assert.That(r.Apply(1000), Is.EqualTo(500u), "1000 / 2.0");
        r.Apply(Events(1, new MasteryReduceEvent(5, 40000)));
        Assert.That(r.Rate, Is.EqualTo(20000), "an id already held is not started again");
        r.Apply(Events(1, new MasteryReduceEvent(6, 15000)));
        Assert.That(r.Apply(1000), Is.EqualTo(666u), "the last started event's rate, the float cut");
        r.Apply(Removed(99));
        Assert.That(r.Rate, Is.EqualTo(15000), "an id not held: nothing");
        r.Apply(Removed(5));
        Assert.That(r.Rate, Is.Zero, "any MasteryReduce event's end sets the rate to 0, though another is held");
        r.Apply(Events(3, new MasteryReduceEvent(6, 30000)));
        Assert.That(r.Rate, Is.EqualTo(30000), "an update: the old event ends, the new one starts");
        r.Clear();
        Assert.That((r.Rate, r.Text(_ => "x {0}")), Is.EqualTo((0, "")));
        r.Apply(Events(1, new MasteryReduceEvent(6, 30000)));
        Assert.That(r.Rate, Is.EqualTo(30000), "cleared ids start again");
    }

    /// <summary>GetText: the common string s_mastery_reduce_event formatted with the rate over 10000.</summary>
    [Test]
    public void TheTextIsTheCommonStringWithTheRate()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var common = new GameStrings(new Ms2.Formats.Pack.PackSet(d)).Common();
        var r = new MasteryReduceRule();
        r.Apply(Events(1, new MasteryReduceEvent(1, 20000)));
        string format = common("s_mastery_reduce_event");
        TestContext.Out.WriteLine($"s_mastery_reduce_event: {format} -> {r.Text(common)}");
        Assert.That(format, Does.Contain("{0"));
        Assert.That(r.Text(common), Is.EqualTo(Ms2.Quest.ClientFormat.Format(format, 2.0f)));
        Assert.That(r.Text(common), Does.Contain("2").And.Not.Contain("{0"));
    }
}
