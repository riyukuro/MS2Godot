using System.Collections.Generic;
using System.Linq;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>Every state class of the client: the state entered as OnUserState enters it (no state data,
/// scenestategame.cpp:5759-5770), then our exit against the client's (<see cref="OwnStateExit.Classes"/>).</summary>
public class OwnStateExitTests
{
    const int Self = 9;
    const double StandUpA = 1.2, Mantle = 0.8;

    static (MyCharacter me, OwnStateExit exit) Pc()
    {
        var me = new MyCharacter();
        var u = new FieldAddUser { ObjectId = Self, User = new UserBlock { Name = "a" } };
        u.Stats[0].Health = 500; u.Stats[2].Health = 500;
        me.Apply(u);
        var exit = new OwnStateExit
        {
            Rand = () => 1,   // rand() % 2 != 0: the "A" set
            SequenceLength = n => n == "Stnd_Up_A" ? StandUpA : n == OwnStateExit.TransformSequence ? Mantle : null,
        };
        me.StateEntered += st => { if (exit.Enter(st, Self)) me.LeaveServerState(st); };   // LiveSession.EnterOwnStateExit
        return (me, exit);
    }

    /// <summary>Frames of <paramref name="seconds"/> with LiveSession.TickOwnStateExit's leave.</summary>
    static void Run(MyCharacter me, OwnStateExit exit, double seconds, bool crawlKey = false)
    {
        for (double t = 0; t < seconds - 1e-9; t += 0.01)
        {
            int st = exit.State;
            if (exit.Advance(System.Math.Min(0.01, seconds - t), me.State, crawlKey)) me.LeaveServerState(st);
        }
    }

    [Test] public void EveryStateClassLeavesAsTheClient()
    {
        var counts = new Dictionary<StateExitCause, int>();
        var mismatches = new List<string>();
        foreach (var row in OwnStateExit.Classes)
        {
            counts[row.Cause] = counts.GetValueOrDefault(row.Cause) + 1;
            string bad = Check(row);
            if (bad != null) mismatches.Add($"{row.Class} (0x{row.Id:x}) {row.Cause}: {bad}");
        }
        foreach (var kv in counts.OrderBy(k => k.Key)) TestContext.Out.WriteLine($"{kv.Key}: {kv.Value}");
        TestContext.Out.WriteLine($"classes {OwnStateExit.Classes.Count}, matching {OwnStateExit.Classes.Count - mismatches.Count}");
        Assert.That(mismatches, Is.Empty);
        Assert.That(OwnStateExit.Classes.Select(r => r.Class).Distinct().Count(), Is.EqualTo(61));
        Assert.That(counts, Is.EquivalentTo(new Dictionary<StateExitCause, int>
        {
            [StateExitCause.AtOnce] = 6, [StateExitCause.InputStep] = 1, [StateExitCause.Timer] = 1,
            [StateExitCause.SequenceEnd] = 2, [StateExitCause.NoSelfExit] = 10, [StateExitCause.Body] = 15,
            [StateExitCause.Apart] = 7, [StateExitCause.NotOwn] = 12, [StateExitCause.Later] = 7,
        }));
        // the pc registers one class per id (pc.cpp:668-721)
        var own = OwnStateExit.Classes.Where(r => r.Id >= 0 && r.Cause != StateExitCause.NotOwn).GroupBy(r => r.Id);
        Assert.That(own.Where(g => g.Count() > 1).Select(g => g.Key), Is.Empty);
    }

    /// <summary>Our exit for one class against the client's; null when they agree.</summary>
    static string Check(StateExitRow row)
    {
        if (row.Cause == StateExitCause.NotOwn)
            return OwnStateExit.OfOwnState(row.Id)?.Class == row.Class ? "the pc's class for the id" : null;
        if (row.Id < 0) return "an own state with no id";
        if (OwnStateExit.OfOwnState(row.Id)?.Class != row.Class) return "not the pc's class for the id";
        var (me, exit) = Pc();
        me.ApplyUserState(Self, row.Id);
        switch (row.Cause)
        {
            case StateExitCause.AtOnce:
                return me.State == 0 && exit.State == 0 ? null : $"still in 0x{me.State:x}";
            case StateExitCause.InputStep:
                if (me.State != row.Id || exit.HoldsBody) return "not entered, or the body is held";
                Run(me, exit, 0.5, crawlKey: true);
                if (me.State != row.Id) return "left with the crawl key held";
                Run(me, exit, 0.01);
                return me.State == 0 ? null : "kept without the crawl key";
            case StateExitCause.Timer:
            {
                double d = OwnStateExit.SpawnDelayMs(Self) / 1000.0;
                if (d is < 0.1 or >= 0.4) return $"delay {d}";
                if (!exit.HoldsBody) return "the body is not held";
                Run(me, exit, d - 0.005);
                if (me.State != row.Id) return "left before the timer";
                Run(me, exit, 0.01);
                return me.State == 0 ? null : "kept after the timer";
            }
            case StateExitCause.SequenceEnd:
            {
                double len = row.Id == 0x12 ? StandUpA : Mantle;
                if (!exit.HoldsBody || exit.Sequence != (row.Id == 0x12 ? "Stnd_Up_A" : OwnStateExit.TransformSequence)) return $"sequence {exit.Sequence}";
                me.ApplyUserState(Self, row.Id);   // asked again with no sub: the running state is kept (State::vfunc_25)
                Run(me, exit, len - 0.005);
                if (me.State != row.Id) return "left before the sequence end";
                Run(me, exit, 0.01);
                return me.State == 0 ? null : "kept after the sequence end";
            }
            case StateExitCause.NoSelfExit:
                Run(me, exit, 30);
                if (me.State != row.Id) return "left by itself";
                if (exit.HoldsBody != (row.Id == 0x28)) return "the body hold";
                me.LeaveForBodyState(1);   // our leave: the next body state (the client's: input, a cancel or a packet)
                return me.State == 0 ? null : "kept after a body state";
            case StateExitCause.Body:
                if (exit.State != 0) return "run here, not by the body";
                if (row.Id is 1 or 2) return me.State == row.Id ? null : "not entered";
                me.LeaveForBodyState(1);   // the body's own exit to StateIdle
                return me.State == 0 ? null : "kept after the body's exit";
            case StateExitCause.Apart:
            case StateExitCause.Later:
                Run(me, exit, 30);
                return exit.State == 0 && me.State == row.Id ? null : "run here";
        }
        return "no cause";
    }

    [Test] public void AStateReplacedByTheBodyStopsItsExit()
    {
        var (me, exit) = Pc();
        me.ApplyUserState(Self, 0x12);
        Assert.That((me.State, exit.State, exit.HoldsBody), Is.EqualTo((0x12, 0x12, true)));
        me.LeaveForBodyState(5);   // a fall the body entered
        Run(me, exit, 2);
        Assert.That((me.State, exit.State, exit.HoldsBody), Is.EqualTo((0, 0, false)));
    }

    [Test] public void AStunWithNoSequenceOnTheBodyNeverEnds()
    {
        var (me, exit) = Pc();
        exit.Rand = () => 0;   // the "B" set, which this body lacks
        me.ApplyUserState(Self, 0x12);
        Assert.That((exit.Sequence, exit.Left), Is.EqualTo(("Stnd_Up_B", -1.0)));
        Run(me, exit, 30);
        Assert.That(me.State, Is.EqualTo(0x12));
    }

    [Test] public void SpawnDelayIsSeededByTheObjectId()
    {
        Assert.That(OwnStateExit.SpawnDelayMs(Self), Is.EqualTo(OwnStateExit.SpawnDelayMs(Self)));
        var all = Enumerable.Range(1, 2000).Select(OwnStateExit.SpawnDelayMs).ToList();
        Assert.That((all.Min() >= 100, all.Max() <= 399, all.Distinct().Count() > 250), Is.EqualTo((true, true, true)));
    }
}
