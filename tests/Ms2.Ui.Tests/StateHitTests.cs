using System.Collections.Generic;
using System.Numerics;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// StateHit on the own PC (statehit.cpp): its timeline, the lift (GameObjectController StartLift / EndLift and
/// FUN_1412407f0's lift branch), the escape (vfunc_8) and the push tick (CPushControl::vfunc_1, hitcontrol.cpp:58-96, with
/// PCBase::FUN_1416725c0's ledge probe).
/// </summary>
public class StateHitTests
{
    sealed class PushBody : IPushBody
    {
        public Vector3 FieldPosition { get; set; }
        public int PrevStateId { get; set; } = HitReaction.StateIdle;
        public bool Wall, FlatWall;
        public float? Floor;                                  // the ground's z under every point, or none
        public bool FloorOnlyAside;                           // ground only under the probe's second point
        public readonly List<(Vector3 From, float Len)> Downs = new();
        public readonly List<(Vector3 From, Vector3 Dir, float Len)> Rays = new();
        public Vector3 Moved;
        public int Moves;

        public bool RayHits(Vector3 from, Vector3 dir, float len)
        {
            Rays.Add((from, dir, len));
            return dir.Z != 0f ? Wall : Wall || FlatWall;
        }

        public bool GroundBelow(Vector3 from, float len, out float hitZ)
        {
            Downs.Add((from, len));
            hitZ = Floor ?? 0f;
            if (Floor == null) return false;
            if (FloorOnlyAside) return len == 0f || Downs.Count == 3;   // [0] the lift's, [1] the probe's first, [2] the second
            return true;
        }

        public void PushMove(Vector3 move, double dt)
        {
            Moved += move;
            Moves++;
        }
    }

    static StateHitData Data(float up = 0f, int down = 1, float duration = 0.5f, float pause = 0.25f, string anim = "anim") => new()
    {
        SpeedRate = 1.5f, PauseTime = pause, Distance = 30f, Duration = duration, UpDistance = up, Down = down, Priority = 2,
        Animation = anim,
    };

    [Test]
    public void TheLiftRisesToItsPeakThenFalls()
    {
        var lift = new LiftArc();
        Assert.That(lift.Start(1f, 100f, 50f), Is.True);
        Assert.That(lift.Start(2f, 10f, 0f), Is.False, "StartLift does nothing while a lift runs");
        Assert.That(lift.Step(0.25f, out bool falling), Is.EqualTo(75f).Within(1e-4f));   // f .25: 50 + 4*100*.25*.75
        Assert.That(falling, Is.False);
        Assert.That(lift.Step(0.25f, out falling), Is.EqualTo(25f).Within(1e-4f));        // the peak, 150
        Assert.That(lift.Step(0.25f, out falling), Is.EqualTo(-25f).Within(1e-4f));
        Assert.That((falling, lift.Falling), Is.EqualTo((true, true)), "the first drop turns it to falling");
        lift.Step(0.25f, out falling);
        Assert.That(falling, Is.False, "only the first drop reports the fall start");
        lift.End();
        Assert.That(lift.Active, Is.False);
        Assert.That(lift.Start(1f, 10f, 0f), Is.True);
        Assert.That(lift.Falling, Is.False);
    }

    [Test]
    public void EnteringCopiesTheDataAndPicksTheSequence()
    {
        var t = new StateHitTimeline();
        t.Enter(Data(), 10.0, 7f);
        Assert.That((t.Speed, t.ArriveTime, t.OwnerZ, t.Data.Distance, t.Data.Priority, t.Lifts),
            Is.EqualTo((1.5f, 10.5f, 7f, 30f, 2, false)));
        Assert.That(t.PickSequence("Damg_A", s => s == "Damg_A"), Is.EqualTo("Damg_A"), "the data's animation, not on the body");
        Assert.That(t.PickSequence("Damg_A", s => s is "Damg_A" or "anim"), Is.EqualTo("anim"), "the data's animation replaces it");
        Assert.That(t.PickSequence("Damg_A", _ => false), Is.EqualTo(""), "a sequence the body lacks leaves none");

        t.Enter(Data(up: 20f, anim: ""), 10.0, 7f);
        Assert.That(t.Lifts, Is.True);
        Assert.That(t.PickSequence("Damg_A", _ => true), Is.EqualTo(StateHitTimeline.FlySequence), "a lift plays the fly sequence");

        t.Enter(null, 10.0, 7f);
        Assert.That((t.Speed, t.ArriveTime, t.OwnerZ, t.Data.Duration, t.Sequence), Is.EqualTo((1f, 0f, 0f, 0f, "")));
    }

    [Test]
    public void TheSequenceEndSetsTheEndTimeAndTheUpdateEndsTheState()
    {
        var t = new StateHitTimeline();
        t.Enter(Data(), 10.0, 0f);
        Assert.That(t.Update(100.0), Is.EqualTo(HitStep.None), "no end time before a sequence ends");
        Assert.That(t.EndOfSequence("Damg_A"), Is.EqualTo(HitStep.None));
        Assert.That(t.EndTime, Is.EqualTo(10.75f));
        Assert.That(t.Update(10.7), Is.EqualTo(HitStep.None));
        Assert.That(t.Update(10.75), Is.EqualTo(HitStep.Down), "with down: the down sequence");
        Assert.That(t.EndOfSequence(StateHitTimeline.DownSequence), Is.EqualTo(HitStep.Idle));

        t.Enter(Data(down: 0), 10.0, 0f);
        t.EndOfSequence(StateHitTimeline.FlySequence);
        Assert.That(t.Update(10.75), Is.EqualTo(HitStep.Idle), "without down: on to idle");
        Assert.That(t.EndOfSequence(StateHitTimeline.FlySequence), Is.EqualTo(HitStep.ReplayFly));

        t.Enter(Data(duration: 0f, pause: 0f), 10.0, 0f);
        Assert.That(t.EndOfSequence("Damg_A"), Is.EqualTo(HitStep.Idle), "no duration and no pause: on to idle at once");
        Assert.That(t.EndTime, Is.EqualTo(0f));
    }

    [Test]
    public void FallDamageWaitsForALiftingHitToArrive()
    {
        var t = new StateHitTimeline();
        t.Enter(Data(up: 20f), 10.0, 0f);
        Assert.That((t.FallDamageAllowed(10.4), t.FallDamageAllowed(10.5)), Is.EqualTo((false, true)));
        t.Enter(Data(), 10.0, 0f);
        Assert.That(t.FallDamageAllowed(10.4), Is.True, "a hit that does not lift never holds it");
    }

    [Test]
    public void TheEscapeFollowsTheConstantsInTheClientsOrder()
    {
        var none = new EscapeHitMethods();
        Assert.That(none.Escape(false, false, true, true, false, true), Is.EqualTo(HitEscape.None), "all four default to 0");
        var all = new EscapeHitMethods { Move = 1, Jump = 1, Skill = 1 };
        Assert.That(all.Escape(true, false, true, true, false, true), Is.EqualTo(HitEscape.None), "a rising lift holds");
        Assert.That(all.Escape(true, true, true, true, false, true), Is.EqualTo(HitEscape.Walk), "a falling one does not");
        Assert.That(all.Escape(false, false, false, true, false, true), Is.EqualTo(HitEscape.None), "a body that cannot act");
        Assert.That(all.Escape(false, false, true, false, false, true), Is.EqualTo(HitEscape.Jump));
        Assert.That(all.Escape(false, false, true, false, true, true), Is.EqualTo(HitEscape.Skill), "no jump in the air");
        Assert.That(all.Escape(false, false, true, false, false, false), Is.EqualTo(HitEscape.Skill));
        Assert.That(new EscapeHitMethods { Jump = 1 }.Escape(false, false, true, true, false, false), Is.EqualTo(HitEscape.None));
    }

    [Test]
    public void TheLedgeProbeLooksForGroundAheadAndBeside()
    {
        var b = new PushBody { FieldPosition = new Vector3(100f, 200f, 50f) };
        Assert.That(PushTick.LedgeAhead(b, new Vector3(2f, 0f, 0f), 30f, PushTick.LedgeHeight, -5f), Is.True, "no ground: a ledge");
        Assert.That(b.Rays[0], Is.EqualTo((new Vector3(100f, 200f, 45f), new Vector3(1f, 0f, 0f), 30f)));
        Assert.That(b.Downs[0], Is.EqualTo((new Vector3(130f, 200f, 55f), 161f)), "10 above the end, 10 + 150 + 1 down");
        Assert.That(b.Downs[1], Is.EqualTo((new Vector3(130.1f, 200.05f, 55f), 161f)));

        b = new PushBody { FieldPosition = Vector3.Zero, Floor = 0f };
        Assert.That(PushTick.LedgeAhead(b, new Vector3(1f, 0f, 0f), 30f, 150f, 0f), Is.False, "ground ahead");
        b = new PushBody { FieldPosition = Vector3.Zero, FlatWall = true };
        Assert.That(PushTick.LedgeAhead(b, new Vector3(1f, 0f, 1f), 30f, 150f, 0f), Is.False, "a wall along the flat direction");
        Assert.That(b.Downs, Is.Empty);
        b = new PushBody { FieldPosition = Vector3.Zero };
        PushTick.LedgeAhead(b, new Vector3(1f, 0f, 0f), -30f, 150f, 0f);
        Assert.That(b.Downs[0].From, Is.EqualTo(new Vector3(-30f, 0f, 10f)), "a negative distance turns the direction round");
        Assert.That(PushTick.LedgeAhead(b, Vector3.Zero, 30f, 150f, 0f), Is.False, "no direction: 0");
    }

    static PushControl Running(float speed = 0.3f, float duration = 1f, bool lift = false, int fall = 0) => new()
    {
        Dir = new Vector3(1f, 0f, 0f), Speed = speed, Duration = duration, Lift = lift, Fall = fall, Curve = 0,
    };

    [Test]
    public void ThePushTickMovesUntilALedgeUnlessItFalls()
    {
        var b = new PushBody { Floor = 0f };
        var p = Running();
        Assert.That(p.Tick(0.5, b), Is.True);
        Assert.That(b.Moves, Is.EqualTo(1));
        Assert.That(b.Moved.X, Is.EqualTo(15f).Within(1e-4f), "half the push, in field units");
        Assert.That(b.Downs[0].Len, Is.EqualTo(161f), "the probe goes half the push ahead");
        Assert.That(b.Rays[0].Len, Is.EqualTo(15f).Within(1e-4f));

        b = new PushBody();
        p = Running();
        Assert.That(p.Tick(0.5, b), Is.True);
        Assert.That((b.Moves, p.Running), Is.EqualTo((0, false)), "a ledge stops the push");

        b = new PushBody();
        p = Running(fall: 1);
        p.Tick(0.5, b);
        Assert.That((b.Moves, p.Running), Is.EqualTo((1, true)), "pushFall goes over it");
        b = new PushBody { PrevStateId = HitReaction.StateHold };
        p = Running();
        p.Tick(0.5, b);
        Assert.That(b.Moves, Is.EqualTo(1), "so does a push out of the hold state");

        p = Running();
        p.Time = 1f;
        Assert.That((p.Tick(0.5, new PushBody()), p.Running), Is.EqualTo((false, false)), "no distance left: reset");
        Assert.That(new PushControl().Tick(0.5, new PushBody()), Is.False);
    }

    [Test]
    public void ALiftingPushProbesFromTheGroundUnderIt()
    {
        var b = new PushBody { FieldPosition = new Vector3(0f, 0f, 80f), Floor = 20f, FloorOnlyAside = true };
        Running(lift: true).Tick(0.5, b);
        Assert.That(b.Downs[0], Is.EqualTo((new Vector3(0f, 0f, 80f), 0f)), "an unlimited ray down from the feet");
        Assert.That(b.Rays[0].From, Is.EqualTo(new Vector3(0f, 0f, 21f)), "the probe starts 1 above the ground");
        Assert.That(b.Moves, Is.EqualTo(1), "the ground beside the end is ground");

        b = new PushBody { FieldPosition = new Vector3(0f, 0f, 10f), Floor = 20f };
        Running(lift: true).Tick(0.5, b);
        Assert.That(b.Rays[0].From.Z, Is.EqualTo(10f), "feet under the hit: no change");
    }

    [Test]
    public void APushTickAllocatesNothing()
    {
        var b = new PushBody { Floor = 0f };
        b.Rays.Capacity = 64;
        b.Downs.Capacity = 64;
        var p = Running(duration: 1000f);
        p.Tick(0.01, b);
        // 1000 ticks a window, up to 5 windows: 50 s of the 1000 s push
        Assert.That(Alloc.Least(() =>
        {
            for (int i = 0; i < 1000; i++)
            {
                b.Rays.Clear();
                b.Downs.Clear();
                p.Tick(0.01, b);
            }
        }), Is.LessThan(512));
    }
}
