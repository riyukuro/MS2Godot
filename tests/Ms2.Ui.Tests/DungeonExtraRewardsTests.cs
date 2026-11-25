using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CGameRule's dungeon extra reward counts (AddDungeonExtraRewardCount @0x14047a420, RemoveDungeonExtraRewardCount @0x14047a4e0) as
/// the game event list fills them from GameEventDungeonExtraReward (OnStart, vfunc_6), and the window refresh the Cli class adds
/// (gameeventcli.cpp:737-752).</summary>
public class DungeonExtraRewardsTests
{
    static DungeonExtraRewardEvent Ev(int id, params (uint Room, uint Count)[] rooms) =>
        new(id, rooms.Select(r => new DungeonExtraRewardCount(r.Room, id, r.Count)).ToList());

    static GameEventMessage Events(byte kind, params DungeonExtraRewardEvent[] e) =>
        new(kind, Array.Empty<ShutdownInteractObjectEvent>(), Array.Empty<int>(), false) { DungeonExtraRewardEvents = e };

    static GameEventMessage Removed(params int[] ids) =>
        new(2, Array.Empty<ShutdownInteractObjectEvent>(), ids, false);

    [Test]
    public void AnEventsRoomsCarryItsCountUntilItEnds()
    {
        var r = new DungeonExtraRewards();
        Assert.That(r.CountOf(100), Is.Null, "no event: no count");
        r.Apply(Events(1, Ev(7, (100, 3), (101, 5))));
        Assert.That((r.CountOf(100), r.CountOf(101), r.CountOf(102)), Is.EqualTo(((uint?)3, (uint?)5, (uint?)null)));
        r.Apply(Events(1, Ev(7, (100, 9))));
        Assert.That(r.CountOf(100), Is.EqualTo(3u), "an id already held is not started again");
        r.Apply(Removed(99));
        Assert.That(r.CountOf(100), Is.EqualTo(3u), "an id not held: nothing");
        r.Apply(Removed(7));
        Assert.That((r.CountOf(100), r.CountOf(101)), Is.EqualTo(((uint?)null, (uint?)null)));
    }

    [Test]
    public void ARoomAnotherEventHoldsIsNotTakenOverNorTakenBack()
    {
        var r = new DungeonExtraRewards();
        r.Apply(Events(1, Ev(7, (100, 3))));
        r.Apply(Events(1, Ev(8, (100, 4), (101, 6))));     // OnStart: "Duplicate DungeonID" for 100, 101 goes in
        Assert.That((r.CountOf(100), r.CountOf(101)), Is.EqualTo(((uint?)3, (uint?)6)));
        r.Apply(Removed(8));                                  // vfunc_6: 100 is event 7's, so only 101 goes
        Assert.That((r.CountOf(100), r.CountOf(101)), Is.EqualTo(((uint?)3, (uint?)null)));
    }

    [Test]
    public void AnUpdateEndsTheOldEventAndStartsTheNewOne()
    {
        var r = new DungeonExtraRewards();
        r.Apply(Events(1, Ev(7, (100, 3), (101, 5))));
        r.Apply(Events(3, Ev(7, (100, 8))));
        Assert.That((r.CountOf(100), r.CountOf(101)), Is.EqualTo(((uint?)8, (uint?)null)));
    }

    [Test]
    public void ClearEndsEveryEvent()
    {
        var r = new DungeonExtraRewards();
        r.Apply(Events(1, Ev(7, (100, 3)), Ev(8, (101, 4))));
        r.Clear();
        Assert.That((r.CountOf(100), r.CountOf(101)), Is.EqualTo(((uint?)null, (uint?)null)));
        r.Apply(Events(1, Ev(7, (100, 3))));
        Assert.That(r.CountOf(100), Is.EqualTo(3u), "a cleared id starts again");
    }

    /// <summary>OnStart and vfunc_6 of the Cli class each ask the open window to RefreshReward: once per event started or ended.</summary>
    [Test]
    public void EachEventStartedOrEndedAsksForARewardRefresh()
    {
        var r = new DungeonExtraRewards();
        int changed = 0;
        r.Changed += () => changed++;
        r.Apply(Events(1, Ev(7, (100, 3)), Ev(8, (101, 4))));
        Assert.That(changed, Is.EqualTo(2));
        r.Apply(Events(1, Ev(7, (100, 3))));
        Assert.That(changed, Is.EqualTo(2), "an id already held is not started again");
        r.Apply(Removed(7));
        Assert.That(changed, Is.EqualTo(3));
        r.Apply(Events(3, Ev(8, (101, 5))));
        Assert.That(changed, Is.EqualTo(5), "an update: the old event's vfunc_6, then OnStart");
        r.Clear();
        Assert.That(changed, Is.EqualTo(6));
    }
}
