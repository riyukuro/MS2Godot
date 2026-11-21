using Ms2.Formats.Model;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>TaxiStationAction::vfunc_1 (@0x14095bca0, actionkeyobject.cpp:769-808): the station poll and its RequestTaxi
/// register. No data needed.</summary>
public class TaxiStationActionTests
{
    const int Field = 2000001;
    static readonly Vec3 Station = new(1000, 2000, 300);

    static (TaxiStationAction, List<IOutPacket>) New(bool house = false)
    {
        var sent = new List<IOutPacket>();
        var a = new TaxiStationAction { Send = sent.Add };
        a.AddStation(Station, house);
        return (a, sent);
    }

    static bool Poll(TaxiStationAction a, Vec3 at, ICollection<int> found, bool? home = null, bool dead = false, bool canMove = true,
                     int state = 1) => a.Poll(true, dead, canMove, state, at, home, Field, found);

    [Test]
    public void AnUnfoundStationSendsRequestTaxiOnceAndTheKeyDoesNotApply()
    {
        var (a, sent) = New();
        var near = Station + new Vec3(100, 0, 0);
        Assert.That(Poll(a, near, new HashSet<int>()), Is.False);
        Assert.That(Poll(a, near, new HashSet<int>()), Is.False);
        Assert.That(sent, Has.Count.EqualTo(1), "the requested list holds the field after the first send");
        Assert.That(sent[0], Is.TypeOf<RequestTaxiRegister>());
        Assert.That(a.Requested, Is.EqualTo(new[] { Field }));
        a.ServerEnter();
        Assert.That(Poll(a, near, new HashSet<int>()), Is.False);
        Assert.That(sent, Has.Count.EqualTo(2), "OnServerEnter (scenestategame.cpp:2115) empties the requested list");
    }

    [Test]
    public void AFoundStationTakesTheKeyAndSendsNothing()
    {
        var (a, sent) = New();
        Assert.That(Poll(a, Station + new Vec3(0, 0, 249), new HashSet<int> { Field }), Is.True);
        Assert.That(sent, Is.Empty);
    }

    [Test]
    public void TheSearchIsStrictlyInsideTheRadiusIn3D()
    {
        var (a, sent) = New();
        var found = new HashSet<int> { Field };
        Assert.That(Poll(a, Station + new Vec3(0, 0, 250), found), Is.False, "d < maxDist (worldservice_1.cpp:5851)");
        Assert.That(Poll(a, Station + new Vec3(0, 150, 200), found), Is.False, "3D distance (flat 0): 250");
        Assert.That(Poll(a, Station + new Vec3(0, 150, 199), found), Is.True);
        Assert.That(sent, Is.Empty);
    }

    [Test]
    public void TheGatesRefuseBeforeTheSearchAndSendNothing()
    {
        var (a, sent) = New();
        var empty = new HashSet<int>();
        Assert.That(a.Poll(false, false, true, 1, Station, null, Field, empty), Is.False);
        Assert.That(Poll(a, Station, empty, dead: true), Is.False);
        Assert.That(Poll(a, Station, empty, canMove: false), Is.False);
        Assert.That(Poll(a, Station, empty, state: TaxiStationAction.StateTaxiCall), Is.False);
        Assert.That(sent, Is.Empty);
        Assert.That(a.Requested, Is.Empty);
    }

    [Test]
    public void AHomeStationAppliesOnlyWithThePrivilegeAndNeverRegisters()
    {
        var (a, sent) = New(house: true);
        var empty = new HashSet<int>();
        Assert.That(Poll(a, Station, empty, home: null), Is.False, "unknown privilege: not held");
        Assert.That(Poll(a, Station, empty, home: false), Is.False);
        Assert.That(Poll(a, Station, empty, home: true), Is.True, "the found list is not read for a home station");
        Assert.That(sent, Is.Empty);
    }
}
