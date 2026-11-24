using System.Collections.Generic;
using System.Linq;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>Every state class of the client: the state entered as OnUserState enters it (no state data,
/// scenestategame.cpp:5759-5770), then our exit against the client's (<see cref="OwnStateExit.Classes"/>).</summary>
public class OwnStateExitTests
{
    const int Self = 9;
    const double StandUpA = 1.2, Mantle = 0.8;
    const int SummonMs = 3000, RecallMs = 4000;   // SummonRideeDuration and RecallCastingTime as the tables set them

    static (MyCharacter me, OwnStateExit exit) Pc()
    {
        var me = new MyCharacter();
        var u = new FieldAddUser { ObjectId = Self, User = new UserBlock { Name = "a" } };
        u.Stats[0].Health = 500; u.Stats[2].Health = 500;
        me.Apply(u);
        var exit = new OwnStateExit
        {
            Stun = { Rand = () => 1 },   // rand() % 2 != 0: the "A" set
            SequenceLength = n => n == "Stnd_Up_A" ? StandUpA : n == OwnStateExit.TransformSequence ? Mantle : null,
            SummonRideeDurationMs = SummonMs,
            RecallCastingTimeMs = RecallMs,
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
            [StateExitCause.AtOnce] = 14, [StateExitCause.Fallback] = 1, [StateExitCause.InputStep] = 1, [StateExitCause.Timer] = 1,
            [StateExitCause.SequenceEnd] = 2, [StateExitCause.NoSelfExit] = 2, [StateExitCause.CastingBar] = 4,
            [StateExitCause.Body] = 13, [StateExitCause.Apart] = 4, [StateExitCause.NotOwn] = 12, [StateExitCause.Later] = 7,
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
            case StateExitCause.Fallback:
                // StateSwimDash: StateSwim entered inside the enter, then the dash effect started, once each
                if (me.State != 0 || exit.State != 0) return $"still in 0x{me.State:x}";
                if (OwnStateExit.BodyEntryOf(row.Id) != BodyEntry.Swim) return "the body is not left in Swim";
                if (exit.TakeFallback() != OwnStateExit.SwimDashFallback || !exit.TakeDashEffect()) return "no fallback or dash effect";
                return exit.TakeFallback() == 0 && !exit.TakeDashEffect() ? null : "taken twice";
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
                // StateStun plays Down_A on the enter and Stnd_Up_A from its first frame, which runs from the next
                double len = row.Id == 0x12 ? StandUpA + 0.01 : Mantle;
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
                if (exit.HoldsBody != (row.Id is 0x28 or 0x16)) return "the body hold";   // StatePvPWinLose keeps State's step, StateTalk's is empty
                me.LeaveForBodyState(1);   // our leave: the next body state (the client's: input, a cancel or a packet)
                return me.State == 0 ? null : "kept after a body state";
            case StateExitCause.Body:
                if (exit.State != 0) return "run here, not by the body";
                if (row.Id is 1 or 2) return me.State == row.Id ? null : "not entered";
                me.LeaveForBodyState(1);   // the body's own exit to StateIdle
                return me.State == 0 ? null : "kept after the body's exit";
            case StateExitCause.CastingBar:
            {
                var bar = new Bar(exit);
                bar.Sync();
                var want = row.Id switch
                {
                    0x26 or 0x3e => (Text: 0, Ms: SummonMs),
                    0x2c => (Text: OwnStateExit.RecallCaption, Ms: RecallMs),
                    _ => (Text: OwnStateExit.NoCaption, Ms: 0),
                };
                if (bar.Started != want) return $"cast {bar.Started}, the client's {want}";
                if (exit.HoldsBody || OwnStateExit.BodyEntryOf(row.Id) != BodyEntry.Idle) return "the body";
                double end = want.Ms / 1000.0;
                bar.Run(me, end - 0.02);   // a frame before the bar's timer ends
                if (want.Ms > 0 && me.State != row.Id) return "left before the cast finished";
                bar.Run(me, 0.03);
                return me.State == 0 && exit.State == 0 && bar.Stops == 1 && !bar.Controller.IsCasting ? null : $"kept after the cast (stops {bar.Stops})";
            }
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
        exit.Stun.Rand = () => 0;   // the "B" set, which this body lacks
        me.ApplyUserState(Self, 0x12);
        Assert.That((exit.Sequence, exit.Left), Is.EqualTo(("Stnd_Up_B", -1.0)));
        Run(me, exit, 30);
        Assert.That(me.State, Is.EqualTo(0x12));
    }

    /// <summary>LiveSession.SyncOwnStateCast and the frame on the HUD's casting bar: the left state stops the bar, the entered
    /// one starts its cast; each frame the bar's timer (vfunc_15) runs, then the state's frame. A cast of 0 ms is completed by
    /// the movie's startCasting at once (HudTests.ANewCastCancelsTheRunningOneAndTheMovieCompletesAZeroLengthCast), as here.</summary>
    sealed class Bar : ICastingEventListener
    {
        readonly OwnStateExit exit;
        long now;
        public readonly CastingBarController Controller;
        public (int Text, int Ms)? Started;
        public int Stops, Finished;
        public Bar(OwnStateExit exit)
        {
            this.exit = exit;
            Controller = new CastingBarController { NowMs = () => now };
        }
        public void OnCastingFinished() { Finished++; exit.CastFinished(); }
        public void OnCastingCancelled() { }
        public void Sync()
        {
            if (exit.TakeBarStopped()) { Stops++; Controller.Cancel(); }
            if (exit.TakeCast() is not { } c) return;
            Started = (c.StringCommonId, c.DurationMs);
            Controller.StartCasting(this, new CastingParam { Duration = c.DurationMs });
            if (c.DurationMs == 0) Controller.OnCallback("OnCastingComplete", System.Array.Empty<object>());
        }
        public void Run(MyCharacter me, double seconds)
        {
            for (double t = 0; t < seconds - 1e-9; t += 0.01)
            {
                now += 10;
                Controller.Process();
                int st = exit.State;
                if (exit.Advance(0.01, me.State)) me.LeaveServerState(st);
                Sync();
            }
        }
    }

    [Test] public void AHitLeavesTheCastsButTheAutoInteraction()
    {
        foreach (var (id, leaves) in new[] { (0x26, true), (0x2c, true), (0x3e, true), (0x39, false) })
        {
            var (me, exit) = Pc();
            var bar = new Bar(exit);
            me.ApplyUserState(Self, id);
            bar.Sync();
            if (id == 0x39) { Assert.That(exit.State, Is.EqualTo(0x39)); continue; }   // done at once; State's empty vfunc_6
            bar.Run(me, 1);
            Assert.That(exit.Hit(), Is.EqualTo(leaves), $"0x{id:x}");
            if (leaves) me.LeaveServerState(id);
            bar.Sync();
            Assert.That((me.State, exit.State, bar.Stops, bar.Controller.IsCasting, bar.Finished), Is.EqualTo((0, 0, 1, false, 0)), $"0x{id:x}");
        }
        var (m2, x2) = Pc();
        Assert.That(x2.Hit(), Is.False);   // no state running
    }

    [Test] public void AStateReplacedStopsItsCastAndAnotherCastKeepsTheState()
    {
        // a body state replaces StateRecall: its vfunc_5 stops the bar, the caster is not told (FUN_1418e1390)
        var (me, exit) = Pc();
        var bar = new Bar(exit);
        me.ApplyUserState(Self, 0x2c);
        bar.Sync();
        bar.Run(me, 1);
        me.LeaveForBodyState(2);
        bar.Run(me, 0.01);
        Assert.That((me.State, exit.State, bar.Stops, bar.Controller.IsCasting, bar.Finished), Is.EqualTo((0, 0, 1, false, 0)));
        // the server's next state replaces StateSummon: the bar stops before the new state's cast starts
        (me, exit) = Pc();
        bar = new Bar(exit);
        me.ApplyUserState(Self, 0x26);
        bar.Sync();
        bar.Run(me, 1);
        me.ApplyUserState(Self, 0x3e);
        bar.Sync();
        Assert.That((me.State, exit.State, bar.Stops, bar.Started, bar.Controller.IsCasting), Is.EqualTo((0x3e, 0x3e, 1, (0, SummonMs), true)));
        bar.Run(me, SummonMs / 1000.0 + 0.02);
        Assert.That((me.State, exit.State), Is.EqualTo((0, 0)));
        // another caster's cast replaces StateTreeWatering's: the base's empty OnCastingCancelled, so the state stays
        (me, exit) = Pc();
        bar = new Bar(exit);
        me.ApplyUserState(Self, 0x3e);
        bar.Sync();
        var other = new Other();
        bar.Controller.StartCastingText(other, "x", 500, false, true, "");
        bar.Run(me, 10);
        Assert.That((other.Finished, me.State, exit.State, bar.Finished), Is.EqualTo((1, 0x3e, 0x3e, 0)));
        // a late finish for a state no longer running does nothing
        me.LeaveForBodyState(1);
        bar.Run(me, 0.01);
        exit.CastFinished();
        me.ApplyUserState(Self, 0x2c);
        bar.Sync();
        bar.Run(me, 0.5);
        Assert.That(me.State, Is.EqualTo(0x2c));
    }

    sealed class Other : ICastingEventListener
    {
        public int Finished;
        public void OnCastingFinished() => Finished++;
        public void OnCastingCancelled() { }
    }

    [Test] public void SpawnDelayIsSeededByTheObjectId()
    {
        Assert.That(OwnStateExit.SpawnDelayMs(Self), Is.EqualTo(OwnStateExit.SpawnDelayMs(Self)));
        var all = Enumerable.Range(1, 2000).Select(OwnStateExit.SpawnDelayMs).ToList();
        Assert.That((all.Min() >= 100, all.Max() <= 399, all.Distinct().Count() > 250), Is.EqualTo((true, true, true)));
    }
}
