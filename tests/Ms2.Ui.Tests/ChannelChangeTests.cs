using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>WorldService::FUN_141bc80c0 (worldservice_1.cpp:4936): the checks, their notices and the Channel packet.</summary>
public class ChannelChangeTests
{
    static ChannelChangeState Ok() => new() { HasPlayer = true, Current = 1 };

    static (bool Result, List<int> Notices, List<IOutPacket> Sent, int Prepared) Run(int channel, ChannelChangeState s)
    {
        var notices = new List<int>(); var sent = new List<IOutPacket>(); int prepared = 0;
        bool r = ChannelChange.Request(channel, s, notices.Add, () => prepared++, sent.Add);
        return (r, notices, sent, prepared);
    }

    [Test] public void AllowedChangePreparesAndSendsTheChannel()
    {
        var (r, n, sent, prep) = Run(2, Ok());
        Assert.That(r, Is.True);
        Assert.That(n, Is.Empty);
        Assert.That(prep, Is.EqualTo(1));
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { new ChannelRequest(2) }));
    }

    [Test] public void NoPlayerOrTheCurrentChannelSendsNothingAndSaysNothing()
    {
        foreach (var s in new[] { new ChannelChangeState { Current = 1 }, Ok() })
        {
            var (r, n, sent, prep) = Run(1, s);
            Assert.That((r, n.Count, sent.Count, prep), Is.EqualTo((false, 0, 0, 0)));
        }
        var (r2, _, sent2, _) = Run(2, new ChannelChangeState { Current = 1 });
        Assert.That((r2, sent2.Count), Is.EqualTo((false, 0)));
    }

    [Test] public void RefusalsShowTheirNotice()
    {
        var a = Ok(); a.NoServerChannel = true;
        var b = Ok(); b.ServiceTypeSet = true;
        var c = Ok(); c.EmoteActive = true;
        var d = Ok(); d.Dead = true;
        Assert.That(Run(2, a).Notices, Is.EqualTo(new[] { 0x62a }));
        Assert.That(Run(2, b).Notices, Is.EqualTo(new[] { 0x62a }));
        Assert.That(Run(2, c).Notices, Is.EqualTo(new[] { 0x62b }));
        Assert.That(Run(2, d).Notices, Is.EqualTo(new[] { 0x62c }));
        foreach (var s in new[] { a, b, c, d })
        {
            var (r, _, sent, prep) = Run(2, s);
            Assert.That((r, sent.Count, prep), Is.EqualTo((false, 0, 0)));
        }
    }
}
