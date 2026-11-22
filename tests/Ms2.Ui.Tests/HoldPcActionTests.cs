using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>HoldPCAction (action key 0x1b, actionkeyobject.cpp:2298-2380) and MyPC::FUN_1415279d0 / vfunc_121's case 7.</summary>
public class HoldPcActionTests
{
    sealed class World : IHoldPcWorld
    {
        public bool Usable = true, Riding, IsPlayer = true, Board = true, NoBoard;
        public int State = 1, Self = 1, Found = 7;
        public float AskedRange;
        public int BoardAsked;
        public bool HoldPcUsable => Usable;
        public int HoldPcState => State;
        public int HoldPcSelfId => Self;
        public bool HoldPcRiding => Riding;
        public int FindHoldTarget(float range, out string node) { AskedRange = range; node = Found != 0 ? "Seat_01" : null; return Found; }
        public bool IsPc(int objectId) => IsPlayer;
        public bool CanBoard(int objectId) { BoardAsked++; return Board; }
        public bool NoBoarding(int objectId) => NoBoard;
    }

    [Test]
    public void APlayerWhoseMountTakesTheOwnPcIsKept()
    {
        var a = new HoldPcAction();
        var w = new World();
        Assert.That(a.Poll(w), Is.True);
        Assert.That(a.Target, Is.EqualTo(7));
        Assert.That(w.AskedRange, Is.EqualTo(HoldPcAction.DefaultRange));
        Assert.That(a.Label, Is.EqualTo("s_action_hold_pc"));
        Assert.That(a.Use(w), Is.EqualTo(HoldPcAction.Press.Board));
        Assert.That(HoldPcAction.Key, Is.EqualTo(0x1b));
    }

    [Test]
    public void TheRefusalsBeforeTheTargetKeepNothing()
    {
        var a = new HoldPcAction();
        Assert.That(a.Poll(new World { Usable = false }), Is.False);
        Assert.That(a.Poll(new World { State = 0x2b }), Is.False);
        Assert.That(a.Poll(new World { Found = 0 }), Is.False);
        Assert.That(a.Poll(new World { Found = 1 }), Is.False, "the own pc's node is nearest");
        Assert.That(a.Target, Is.Zero);
        Assert.That(a.Label, Is.Null);
        Assert.That(a.Use(new World()), Is.EqualTo(HoldPcAction.Press.None));
    }

    [Test]
    public void ANonPlayerClearsTheTargetTheOtherRefusalsKeepIt()
    {
        var a = new HoldPcAction();
        Assert.That(a.Poll(new World()), Is.True);
        foreach (var w in new[] { new World { State = 0x20 }, new World { Board = false }, new World { State = 0x2f }, new World { NoBoard = true } })
        {
            Assert.That(a.Poll(w), Is.False);
            Assert.That(a.Target, Is.EqualTo(7), "m_n18 stays as it was");
        }
        var hold = new World { State = 0x20 };
        a.Poll(hold);
        Assert.That(hold.BoardAsked, Is.Zero, "state 0x20 refuses before FUN_1415279d0");
        Assert.That(a.Poll(new World { IsPlayer = false }), Is.False);
        Assert.That(a.Target, Is.Zero);
    }

    [Test]
    public void TheKeyRidesOffARiderAndRefusesTheHoldStates()
    {
        var a = new HoldPcAction();
        Assert.That(a.Use(new World { Riding = true }), Is.EqualTo(HoldPcAction.Press.RideOff), "a rider rides off even without a target");
        a.Poll(new World());
        Assert.That(a.Use(new World { State = 0x20 }), Is.EqualTo(HoldPcAction.Press.None));
        Assert.That(a.Use(new World { State = 0x2b }), Is.EqualTo(HoldPcAction.Press.None));
        Assert.That(a.Use(new World { State = 0x2f }), Is.EqualTo(HoldPcAction.Press.Board), "0x2f refuses the poll only");
        Assert.That(a.Use(new World { Usable = false }), Is.EqualTo(HoldPcAction.Press.None));
    }

    [Test]
    public void CanBoardNeedsASeatTheSightAndWithTheFeatureAFriend()
    {
        Assert.That(HoldPcAction.CanBoard(true, 0, 1, true, false, false, false, "", ""), Is.True);
        Assert.That(HoldPcAction.CanBoard(false, 0, 1, true, false, false, false, "", ""), Is.False);
        Assert.That(HoldPcAction.CanBoard(true, 1, 1, true, false, false, false, "", ""), Is.False, "no free seat");
        Assert.That(HoldPcAction.CanBoard(true, 0, 1, false, false, false, false, "", ""), Is.False, "no line of sight");
        Assert.That(HoldPcAction.CanBoard(true, 0, 1, true, true, false, false, "", ""), Is.False, "feature 0xad: a stranger");
        Assert.That(HoldPcAction.CanBoard(true, 0, 1, true, true, true, false, "", ""), Is.True, "a friend");
        Assert.That(HoldPcAction.CanBoard(true, 0, 1, true, true, false, true, "", ""), Is.True, "a party member");
        Assert.That(HoldPcAction.CanBoard(true, 0, 1, true, true, false, false, "G", "G"), Is.True, "the same guild");
        Assert.That(HoldPcAction.CanBoard(true, 0, 1, true, true, false, false, "", ""), Is.False, "an empty guild name matches none");
        Assert.That(HoldPcAction.CanBoard(true, 0, 1, true, true, false, false, "G", "H"), Is.False);
    }

    [Test]
    public void AfterTheHoldRideOffAMountedPlayerIsBoardedAnotherIsHeld()
    {
        var w = new World();
        Assert.That(HoldPcAction.AfterRideOff(w, true, 7, out int id), Is.EqualTo(HoldPcAction.RideOffOutcome.Board));
        Assert.That(id, Is.EqualTo(7));
        Assert.That(HoldPcAction.AfterRideOff(new World { Board = false }, true, 7, out id), Is.EqualTo(HoldPcAction.RideOffOutcome.None));
        Assert.That(id, Is.Zero);
        Assert.That(HoldPcAction.AfterRideOff(w, false, 7, out _), Is.EqualTo(HoldPcAction.RideOffOutcome.Hold));
        Assert.That(HoldPcAction.AfterRideOff(new World { IsPlayer = false }, true, 7, out _), Is.EqualTo(HoldPcAction.RideOffOutcome.None));
        Assert.That(HoldPcAction.AfterRideOff(w, true, 0, out _), Is.EqualTo(HoldPcAction.RideOffOutcome.None));
    }

    [Test]
    public void TheHoldPcKeyComesAfterTheReactor()
    {
        Assert.That(ActionKeyPoll.Pick(false, false, false, false, false, false, true), Is.EqualTo(ActionKeyTarget.HoldPc));
        Assert.That(ActionKeyPoll.Pick(false, false, false, false, false, true, true), Is.EqualTo(ActionKeyTarget.Reactor));
        var p = new ActionKeyPoll { Current = ActionKeyTarget.HoldPc };
        Assert.That(p.InActiveMap(cubeMode: true), Is.False, "not in the cube-mode map");
        Assert.That(p.InActiveMap(cubeMode: false), Is.True);
    }
}
