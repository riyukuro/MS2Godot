using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>HoldAction (action key 6, actionkeyobject.cpp:2196-2295).</summary>
public class HoldActionTests
{
    sealed class World : IHoldWorld
    {
        public bool Usable = true, Riding, IsPlayer;
        public int State = 1, Self = 1, Found = 7, Npc = 11000001;
        public float AskedRange;
        public bool HoldUsable => Usable;
        public int HoldState => State;
        public int HoldSelfId => Self;
        public bool HoldRiding => Riding;
        public int FindHoldTarget(float range, out string node) { AskedRange = range; node = Found != 0 ? "Hold_Point00" : null; return Found; }
        public bool IsPc(int objectId) => IsPlayer;
        public bool FindNpc(int objectId, out int npcId) { npcId = Npc; return Npc != 0; }
    }

    [Test]
    public void AnNpcWithAHoldNodeInRangeIsTheActionAndThePressHoldsIt()
    {
        var a = new HoldAction();
        var w = new World();
        Assert.That(a.Poll(w), Is.True);
        Assert.That(w.AskedRange, Is.EqualTo(HoldAction.DefaultRange));
        Assert.That(a.NpcId, Is.EqualTo(11000001));
        Assert.That(a.Refresh, Is.True);
        Assert.That(a.Use(w, out int target, out string node), Is.EqualTo(HoldAction.Press.Hold));
        Assert.That((target, node), Is.EqualTo((7, "Hold_Point00")));
        Assert.That(w.AskedRange, Is.EqualTo(HoldAction.DefaultRange + 20f));
        Assert.That(HoldAction.Key, Is.EqualTo(6));
    }

    [Test]
    public void TheOwnPcAPlayerNoTargetAndTheRefusedStatesRefuse()
    {
        var a = new HoldAction();
        Assert.That(a.Poll(new World { Usable = false }), Is.False);
        Assert.That(a.Poll(new World { State = 0x2b }), Is.False);
        Assert.That(a.Poll(new World { Found = 0 }), Is.False);
        Assert.That(a.Poll(new World { Found = 1 }), Is.False);
        Assert.That(a.Poll(new World { IsPlayer = true }), Is.False);
        Assert.That(a.NpcId, Is.EqualTo(0));
        Assert.That(a.Use(new World { State = 0x2b }, out _, out _), Is.EqualTo(HoldAction.Press.None));
        Assert.That(a.Use(new World { Usable = false }, out _, out _), Is.EqualTo(HoldAction.Press.None));
        Assert.That(a.Use(new World { Found = 0 }, out _, out _), Is.EqualTo(HoldAction.Press.None));
    }

    [Test]
    public void ThePressLeavesAHoldAndRidesOffFirst()
    {
        var a = new HoldAction();
        Assert.That(a.Use(new World { State = 0x20 }, out _, out _), Is.EqualTo(HoldAction.Press.Leave));
        Assert.That(a.Use(new World { Riding = true }, out _, out _), Is.EqualTo(HoldAction.Press.RideOff));
        // in StateHold the leave comes before the riding test
        Assert.That(a.Use(new World { State = 0x20, Riding = true }, out _, out _), Is.EqualTo(HoldAction.Press.Leave));
    }

    [Test]
    public void TheRefreshFollowsTheNpcAndTheCaptionsHoldState()
    {
        var a = new HoldAction();
        var w = new World();
        Assert.That(a.Poll(w) && a.Refresh, Is.True);
        Assert.That(a.Caption(w.State, out string key), Is.EqualTo(HoldAction.CaptionHold));
        Assert.That(key, Is.Null);
        Assert.That(a.Poll(w), Is.True);
        Assert.That(a.Refresh, Is.False);                  // the same npc, the caption's state unchanged
        w.State = 0x20;
        Assert.That(a.Poll(w) && a.Refresh, Is.True);      // the hold was entered since the caption
        Assert.That(a.Caption(w.State, out _), Is.EqualTo(HoldAction.CaptionHoldEnd));
        Assert.That(a.Poll(w), Is.True);
        Assert.That(a.Refresh, Is.False);
        w.State = 1;
        Assert.That(a.Poll(w) && a.Refresh, Is.True);      // and left
        w.Npc = 0;
        Assert.That(a.Poll(w), Is.True);                   // an actor that is not an npc still applies
        Assert.That(a.NpcId, Is.EqualTo(0));
    }

    [Test]
    public void TheCartHasItsOwnCaptions()
    {
        var a = new HoldAction();
        var w = new World { Npc = HoldAction.CartNpcId };
        Assert.That(a.Poll(w), Is.True);
        Assert.That(a.Caption(1, out string key), Is.EqualTo(0));
        Assert.That(key, Is.EqualTo("s_action_hold_cart"));
        Assert.That(a.Caption(0x20, out key), Is.EqualTo(0));
        Assert.That(key, Is.EqualTo("s_action_hold_end_cart"));
    }

    [Test]
    public void TheCommonCaptionIdsAreTheHoldStrings()
    {
        Assert.That(Ms2.Formats.Text.CommonStringIds.Key(HoldAction.CaptionHold), Is.EqualTo("s_action_hold"));
        Assert.That(Ms2.Formats.Text.CommonStringIds.Key(HoldAction.CaptionHoldEnd), Is.EqualTo("s_action_hold_end"));
    }

    [Test]
    public void TheInteractPollAsksTheHoldBeforeTheTaxi()
    {
        int taxiAsked = 0;
        bool ok = InteractKey.Poll(null, null, default, default, () => true, () => { taxiAsked++; return true; },
            out bool npc, out bool onHold, out bool onTaxi);
        Assert.That((ok, npc, onHold, onTaxi, taxiAsked), Is.EqualTo((true, false, true, false, 0)));
        ok = InteractKey.Poll(null, null, default, default, () => false, () => true, out npc, out onHold, out onTaxi);
        Assert.That((ok, onHold, onTaxi), Is.EqualTo((true, false, true)));
        Assert.That(ActionKeyPoll.Pick(true, false, false, false, false), Is.EqualTo(ActionKeyTarget.Interact));
    }
}
