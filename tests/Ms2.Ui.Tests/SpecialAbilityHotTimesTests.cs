using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

public class SpecialAbilityHotTimesTests
{
    static SpecialAbilityHotTimeEvent Ev(int id, string ids, string values, bool pcBang = false) => new(id, pcBang, ids, values);
    static GameEventMessage Msg(byte kind, params SpecialAbilityHotTimeEvent[] e) =>
        new(kind, Array.Empty<ShutdownInteractObjectEvent>(), Array.Empty<int>(), false) { HotTimes = e };
    static GameEventMessage Removed(params int[] ids) => new(2, Array.Empty<ShutdownInteractObjectEvent>(), ids, false);

    [Test]
    public void ARateAbilityIsStoredTimesAThousandAndAPlainOneTruncated()
    {
        // GameEventSAHotTime::vfunc_2 (gameevent.cpp:671-688): FUN_14081dcd0 (valueType 2) -> (int)(v * 1000), else (int)v;
        // the id and value lists skip empty tokens (CStringW::Tokenize)
        var h = new SpecialAbilityHotTimes(id => id == 35);
        var r = h.Parse(Ev(1, ",35,,4,", "0.15,,7.9"));
        Assert.That((r.Count, r[35], r[4]), Is.EqualTo((2, 150, 7)));
    }

    [Test]
    public void ListsOfDifferentLengthsStoreNothing()
    {
        // vfunc_2 :679-680: the counts differ -> return before any rate is stored
        var h = new SpecialAbilityHotTimes(_ => false);
        Assert.That(h.Parse(Ev(1, "35,4", "3")), Is.Empty);
    }

    [Test]
    public void ThePcBangSumCountsOnlyForAPcBangWearer()
    {
        // FUN_14047f9d0 (gamerule.cpp:2897): an event with m_30 (GameEventPCBangSAHotTime m_e8 = 1) sums into m_c0, the
        // rest into m_b0; FUN_1404774a0 (:805) adds m_c0 only when bAll (PC::FUN_141658f30)
        var h = new SpecialAbilityHotTimes(_ => true);
        h.Apply(Msg(0, Ev(1, "35", "0.1"), Ev(2, "35", "0.05"), Ev(3, "35", "0.2", pcBang: true)));
        Assert.That((h.Total(35, false), h.Total(35, true), h.Total(4, true)), Is.EqualTo((150, 350, 0)));
    }

    [Test]
    public void AddSkipsAHeldIdUpdateReplacesItAndRemoveTakesItOff()
    {
        // GameEventManagerCli Add (:211-220) skips a held id; Update (:236-242) -> vfunc_7 (gameeventbase.cpp:202-207):
        // the old vfunc_6, then OnStart; Remove (:171-185) -> vfunc_6 -> RemoveHotTimeEvent
        var h = new SpecialAbilityHotTimes(_ => false);
        h.Apply(Msg(1, Ev(1, "35", "10")));
        h.Apply(Msg(1, Ev(1, "35", "99")));
        Assert.That(h.Total(35, false), Is.EqualTo(10));
        h.Apply(Msg(3, Ev(1, "35", "20")));
        Assert.That(h.Total(35, false), Is.EqualTo(20));
        h.Apply(Removed(1));
        Assert.That((h.Total(35, false), h.Count), Is.EqualTo((0, 0)));
    }

    [Test]
    public void ClearTakesEveryEventOff()
    {
        // GameEventManagerCli::Clear (:98-107): each event's vfunc_6
        var h = new SpecialAbilityHotTimes(_ => false);
        h.Apply(Msg(0, Ev(1, "35", "10"), Ev(2, "35", "5", pcBang: true)));
        h.Clear();
        Assert.That(h.Total(35, true), Is.EqualTo(0));
    }

    [Test]
    public void TheTotalAllocatesNothing()
    {
        var h = new SpecialAbilityHotTimes(_ => false);
        h.Apply(Msg(0, Ev(1, "35", "10"), Ev(2, "35", "5", pcBang: true)));
        h.Total(35, true);
        Assert.That(Alloc.Least(() => { for (int i = 0; i < 1000; i++) h.Total(35, true); }), Is.LessThan(512));
    }
}
