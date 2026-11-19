using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>UIPCInfoDialog's request state (TryBeginRequest @0x14132c080, vfunc_79 @0x14132d630).</summary>
public class PcInfoRequestsTests
{
    static PcInfoRequests New(Func<uint> now) => new() { TickMs = now };
    static CharacterInfoAnswer Found(long id) => new(id, 1, new PcInfo { CharacterId = id });

    [Test]
    public void ARepeatWithinFiveSecondsIsRefusedAndAnOpenWindowOnTheSameCharacterIsKept()
    {
        uint now = 1000;
        var r = New(() => now);
        Assert.That(r.TryBeginRequest(5, false), Is.EqualTo(0));
        Assert.That((r.Requested, r.Pending), Is.EqualTo((5L, 5L)));
        now += 4999;
        Assert.That(r.TryBeginRequest(5, false), Is.EqualTo(1), "m_1e0.Check(5000) fails");
        now += 1;
        Assert.That(r.TryBeginRequest(5, true), Is.EqualTo(2), "Check passes; the window is open on 5");
        Assert.That(r.TryBeginRequest(6, true), Is.EqualTo(0), "another character");
        Assert.That((r.Requested, r.Pending), Is.EqualTo((6L, 6L)));
        r.OnAnswer(new CharacterInfoAnswer(6, 0, null), true, false);
        Assert.That(r.Pending, Is.Zero);
        Assert.That(r.TryBeginRequest(6, false), Is.EqualTo(0), "answered: no longer pending");
    }

    [Test]
    public void AnAnswerOpensAClosedWindowOrRefreshesALoadedOne()
    {
        var r = New(() => 1);
        int open = 0, refresh = 0; var boxes = new List<int>();
        r.Open += () => open++; r.Refresh += () => refresh++; r.CommonBox += boxes.Add;
        r.TryBeginRequest(5, false);
        r.OnAnswer(Found(9), true, false);                              // not m_188: dropped, m_1d8 kept
        Assert.That((open, r.Pending, r.Info), Is.EqualTo((0, 5L, (PcInfo)null)));
        r.OnAnswer(Found(5), true, false);
        Assert.That((open, refresh, r.Pending, r.Info.CharacterId), Is.EqualTo((1, 0, 0L, 5L)));
        var shown = r.Info;
        r.OnAnswer(Found(5), true, false);                              // a record is shown: nothing
        Assert.That((open, r.Info), Is.EqualTo((1, shown)));
        r.OnAnswer(new CharacterInfoAnswer(5, 2, null), true, false);   // nor a box
        Assert.That(boxes, Is.Empty);
        r.Info = null;                                                  // vfunc_67 (the window closed)
        r.OnAnswer(Found(5), false, true);
        Assert.That((open, refresh), Is.EqualTo((1, 1)));
        r.Info = null;
        r.OnAnswer(Found(5), false, false);                             // open but not loaded: the record only
        Assert.That((open, refresh, r.Info?.CharacterId), Is.EqualTo((1, 1, 5L)));
        r.Info = null;
        r.OnAnswer(new CharacterInfoAnswer(5, 0, null), true, false);
        Assert.That(boxes, Is.EqualTo(new[] { 0x26 }));
        Assert.That(r.Info, Is.Null);
    }
}
