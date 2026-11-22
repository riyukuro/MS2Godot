using System.Numerics;
using Ms2.Net.Replay;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// The cast's motion controller (StateSkill::vfunc_4, FUN_1417edc20, motioncontroller.cpp) that the remote PCs and the own
/// PC both run: the position after N ticks for each movetype, the move keys that start the record's move time, the
/// loop restart's re-arm (FUN_1417f1a90), and the move input (stateAttr moveType 2 / 3).
/// </summary>
public class SkillMotionTests
{
    sealed class Owner : ISkillMotionOwner
    {
        public Vector3 Position { get; set; }
        public bool Air, Jumping, Ledge;
        public readonly Dictionary<string, float> Keys = new();
        public bool InAirFor(int ms) => Air;
        bool ISkillMotionOwner.Jumping => Jumping;
        public float CastKeyTime(string key) => Keys.TryGetValue(key, out var t) ? t : -1f;
        public bool LedgeAhead(Vector3 dir, float dist, float height, float up) => Ledge;
    }

    static Owner Keys(params (string key, float t)[] keys)
    {
        var o = new Owner();
        foreach (var (k, t) in keys) o.Keys[k] = t;
        return o;
    }

    static CastMove Move(int motionType, float keyMove = 300f, float distance = 0f, int skillMoveType = 0, bool inMoveKey = false,
        bool magicControl = false) =>
        new(motionType, keyMove, distance, 0f, skillMoveType, inMoveKey, magicControl, new Vector3(1, 0, 0));

    static SkillMotion Start(Owner o, CastMove m, float rotZ = 0f, bool holding = false)
    {
        var s = new SkillMotion();
        s.Record(m, 1f, rotZ);
        s.Enter(o, holding);
        return s;
    }

    static Vector3 Run(SkillMotion s, Owner o, int ticks, Vector3 input = default)
    {
        var sum = Vector3.Zero;
        for (int i = 0; i < ticks; i++)
        {
            s.Step(o, 0.1f, input);
            sum += s.Displacement;
        }
        return sum;
    }

    static void Near(Vector3 a, Vector3 b, string what) =>
        Assert.That(Vector3.Distance(a, b), Is.LessThan(0.01f), $"{what}: {a} vs {b}");

    [Test]
    public void TheLinearMoveSpendsKeyMoveOverTheMoveKeysSpan()
    {
        // CLinearMotionController::vfunc_5: keyMove (units) over m_fDuration = move1 - move0, along the record's m_kStart
        var o = Keys(("move0", 0.1f), ("move1", 0.6f));
        var s = Start(o, Move(0));
        Near(Run(s, o, 3), Vector3.Zero, "no move time before the move0 key");
        Assert.That(s.TextKey(o, "move0"), Is.True);
        Assert.That(s.Duration, Is.EqualTo(0.5f).Within(1e-6f));
        Near(Run(s, o, 2), new Vector3(120, 0, 0), "two of five steps");
        Near(Run(s, o, 5), new Vector3(180, 0, 0), "the rest, then nothing past the duration");
        Assert.That(s.TextKey(o, "move0"), Is.False, "a phase starts once");
    }

    [Test]
    public void TheLinearMoveStopsInTheAirAndAtALedge()
    {
        var o = Keys(("move0", 0f), ("move1", 0.5f));
        var s = Start(o, Move(0));
        s.TextKey(o, "move0");
        o.Air = true;
        Near(Run(s, o, 1), Vector3.Zero, "in the air");
        o.Air = false; o.Jumping = true;
        Near(Run(s, o, 1), Vector3.Zero, "jumping");
        o.Jumping = false; o.Ledge = true;
        Near(Run(s, o, 1), Vector3.Zero, "over a ledge");
        Assert.That((s.Duration, s.Elapsed), Is.EqualTo((0f, 0f)), "the ledge clears the record's move time");
    }

    [Test]
    public void TheArcLaysA04SecondCurveToMoveDistanceAlongTheRotation()
    {
        // CArcMotionController: start at the feet, end movedistance along (sin z, -cos z), control point 100 higher halfway
        var o = new Owner { Position = new Vector3(10, 20, 0) };
        var s = Start(o, Move(1, distance: 200f));
        Near(Run(s, o, 2), new Vector3(0, -100, 50), "half way: the bezier's middle");
        Near(Run(s, o, 2), new Vector3(0, -100, -50), "the end");
        Near(Run(s, o, 3), Vector3.Zero, "nothing after 0.4 s");

        var turned = Start(o, Move(1, distance: 200f), rotZ: 90f);
        Near(Run(turned, o, 4), new Vector3(200, 0, 0), "rotation z 90: +x");
    }

    [Test]
    public void TheBlinkAndUnknownTypesMoveNothing()
    {
        foreach (int type in new[] { 2, 7 })
        {
            var o = Keys(("move0", 0f), ("move1", 0.5f));
            var s = Start(o, Move(type));
            s.TextKey(o, "move0");
            Near(Run(s, o, 5), Vector3.Zero, $"movetype {type}");
            Assert.That(s.Elapsed, Is.EqualTo(0.5f).Within(1e-5f), "the record's time still runs");
        }
    }

    [Test]
    public void TheMoveControllerFollowsTheInput()
    {
        // CMoveMotionController::vfunc_5: the linear step along the move input; no input, no move
        var o = Keys(("move0", 0f), ("move1", 0.5f));
        var s = Start(o, Move(3));
        s.TextKey(o, "move0");
        Near(Run(s, o, 2, new Vector3(0, 1, 0)), new Vector3(0, 120, 0), "along the input");
        Near(Run(s, o, 1), Vector3.Zero, "no input");
        o.Air = true;
        Near(Run(s, o, 1, new Vector3(0, 1, 0)), Vector3.Zero, "any fall time stops it (vfunc_99(0))");
    }

    [Test]
    public void OnlyStateAttrMoveType2Or3TakesTheInputAndAMoveKeySkillOnlyInItsTime()
    {
        var o = Keys(("move0", 0f), ("move1", 0.2f));
        Assert.That(Start(o, Move(0, skillMoveType: 0)).TakesInput, Is.False);
        Assert.That(Start(o, Move(0, skillMoveType: 3)).TakesInput, Is.True);
        var s = Start(o, Move(0, skillMoveType: 2, inMoveKey: true));
        Assert.That(s.TakesInput, Is.False, "movingInMovekey before its move key");
        s.TextKey(o, "move0");
        Assert.That(s.Duration, Is.EqualTo(0.25f).Within(1e-6f), "movingInMovekey: 0.05 s longer");
        Assert.That(s.TakesInput, Is.True);
        Run(s, o, 3);
        Assert.That(s.TakesInput, Is.False, "the move time ran out");
    }

    [Test]
    public void MagicControlWithoutAHeldActorMovesNothing()
    {
        var o = new Owner();
        Assert.That(Start(o, Move(0, magicControl: true)).NoMove, Is.True);
        Assert.That(Start(o, Move(0, magicControl: true), holding: true).NoMove, Is.False);
        Assert.That(Start(o, Move(0)).NoMove, Is.False);
    }

    [Test]
    public void TheKeyWindowFeedsTheKeysThePlayPassedAndALoopRestartRearmsThem()
    {
        var keys = new List<(double, string)> { (0.05, "loopstart"), (0.1, "move0"), (0.4, "move1"), (0.7, "loopend") };
        var o = Keys(("loopstart", 0.05f), ("move0", 0.1f), ("move1", 0.4f), ("loopend", 0.7f));
        var s = Start(o, Move(0));
        s.TextKeys(o, keys, -1, 0.1 - 1e-9);
        Assert.That(s.Duration, Is.EqualTo(0f), "move0 not reached");
        s.TextKeys(o, keys, 0.1 - 1e-9, 0.2);
        Assert.That(s.Duration, Is.EqualTo(0.3f).Within(1e-6f), "move0 passed");
        Run(s, o, 3);
        s.TextKeys(o, keys, 0.2, 0.7);
        // the loop seeks back to loopstart: FUN_1417f1a90 arms move0 again and clears the time; the next pass starts it
        s.ArmMovePhases(o);
        Assert.That((s.Duration, s.Elapsed), Is.EqualTo((0f, 0f)));
        s.TextKeys(o, keys, 0.05, 0.15);
        Assert.That(s.Duration, Is.EqualTo(0.3f).Within(1e-6f), "the re-armed move0 starts the move time again");
        Near(Run(s, o, 3), new Vector3(300, 0, 0), "and the move runs again");
    }

    [Test]
    public void AMovePhaseSpanningTheLoopTakesItsDurationAtTheRestart()
    {
        var o = Keys(("loopstart", 0.1f), ("move0", 0.1f), ("move1", 0.5f), ("loopend", 0.5f));
        var s = Start(o, Move(0));
        s.ArmMovePhases(o);
        Assert.That(s.Duration, Is.EqualTo(0.4f).Within(1e-6f));
    }

    [Test]
    public void TheStepsAllocateNothing()
    {
        var keys = new List<(double, string)> { (0.0, "move0"), (0.5, "move1") };
        var o = Keys(("move0", 0f), ("move1", 0.5f));
        var lin = Start(o, Move(0));
        var arc = Start(o, Move(1, distance: 200f));
        var mv = Start(o, Move(3));
        long Window()   // measured over windows, the least kept (one-time JIT and tiering allocations)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                lin.TextKeys(o, keys, -1, 0.1);
                lin.Step(o, 0.016f, Vector3.Zero);
                arc.Step(o, 0.016f, Vector3.Zero);
                mv.Step(o, 0.016f, Vector3.UnitX);
                lin.ArmMovePhases(o);
                _ = lin.TakesInput;
            }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        long least = long.MaxValue;
        for (int w = 0; w < 5; w++) least = Math.Min(least, Window());
        Assert.That(least, Is.EqualTo(0));
    }
}
