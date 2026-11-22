using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CTaxiManager (taximanager.cpp) and StateTaxiCall (statetaxicalldata.cpp).</summary>
public class TaxiManagerTests
{
    // The expected charges are the output of the client's precompiled calcTaxiCharge / calcAirTaxiCharge run in Lua 5.1
    // (luac5.1 -l on the packed chunk, then lua5.1 on the same inputs).
    [TestCase(1, 10, 27)]
    [TestCase(2, 24, 203)]
    [TestCase(3, 24, 304)]
    [TestCase(4, 25, 580)]
    [TestCase(5, 30, 1049)]
    [TestCase(7, 60, 5045)]
    [TestCase(1, 1, 16)]
    [TestCase(-1, 30, -210)]
    [TestCase(0, 30, 0)]
    [TestCase(3, 70, 2814)]
    public void CalcTaxiChargeIsTheClientsLuaChunk(int hops, int level, int charge) =>
        Assert.That(TaxiManager.CalcTaxiCharge(hops, (ushort)level), Is.EqualTo(charge));

    [TestCase(1, 30000)]
    [TestCase(10, 30000)]
    [TestCase(11, 30500)]
    [TestCase(60, 55000)]
    public void CalcAirTaxiChargeIsTheClientsLuaChunk(int level, int charge)
    {
        Assert.That(TaxiManager.CalcAirTaxiCharge((ushort)level), Is.EqualTo(charge));
        Assert.That(TaxiManager.GetAirTaxiFare((short)level), Is.EqualTo(charge));
    }

    static FieldMeta Graph(params (int, int[])[] edges)
    {
        var m = new FieldMeta();
        foreach (var (k, v) in edges) m.PortalTargets[k] = new List<int>(v);
        return m;
    }

    [Test]
    public void TheFareIsTheFewestPortalHopsPricedByLevel()
    {
        // 1 - 2 - 3 - 4 and a shortcut 1 - 4 through 5 - 4 is longer than 1 - 6 - 4? (1,6,4: 2 hops; 1,2,3,4: 3 hops)
        var t = new TaxiManager(Graph((1, new[] { 2, 6 }), (2, new[] { 3 }), (3, new[] { 4 }), (6, new[] { 4 })));
        Assert.That(t.GetTaxiFare(1, 4, 24), Is.EqualTo(TaxiManager.CalcTaxiCharge(2, 24)));
        Assert.That(t.GetTaxiFare(1, 3, 24), Is.EqualTo(TaxiManager.CalcTaxiCharge(2, 24)));
        Assert.That(t.GetTaxiFare(1, 2, 30), Is.EqualTo(TaxiManager.CalcTaxiCharge(1, 30)));
    }

    [Test]
    public void TheSameFieldIsFreeAndAnUnreachableOneCostsNothingOrLess()
    {
        var t = new TaxiManager(Graph((1, new[] { 2 })));
        Assert.That(t.GetTaxiFare(1, 1, 30), Is.Zero);
        Assert.That(t.GetTaxiFare(1, 9, 30), Is.LessThanOrEqualTo(0), "no route: hops -1, a negative charge (OnTaxi shows s_err_taxi_continent)");
        Assert.That(new TaxiManager(null).GetTaxiFare(1, 2, 30), Is.LessThanOrEqualTo(0));
    }

    [Test]
    public void TheRouteIsCachedPerUnorderedPairWithTheFirstAnswer()
    {
        var m = Graph((1, new[] { 2 }));
        var t = new TaxiManager(m);
        long first = t.GetTaxiFare(1, 2, 24);
        m.PortalTargets[1] = new List<int> { 3 };                 // the graph changes, the cached pair does not
        Assert.That(t.GetTaxiFare(2, 1, 24), Is.EqualTo(first), "(min, max) is the key, so 2 -> 1 hits 1 -> 2");
    }

    [Test]
    public void TheCallRunsThePhasesAndSendsTheRequestOnceFromThePlayerOnly()
    {
        var sent = new List<IOutPacket>();
        var s = new TaxiCallState { Send = sent.Add };
        s.Begin(true, 2000001, 3000, true);
        Assert.That(s.Phase, Is.EqualTo(TaxiCallState.Call));
        s.Tick(2.9); Assert.That(s.Phase, Is.EqualTo(TaxiCallState.Call));
        s.Tick(0.2); Assert.That(s.Phase, Is.EqualTo(TaxiCallState.Arrive));       // 3.0 s
        s.Tick(1.7); Assert.That(s.Phase, Is.EqualTo(TaxiCallState.Arrive));       // 3.1 + 1.7 < 3.0 + 1.9
        s.Tick(0.2); Assert.That(s.Phase, Is.EqualTo(TaxiCallState.Board));      // 5.0 >= 4.9
        s.Tick(0.45); Assert.That(s.Phase, Is.EqualTo(TaxiCallState.Ride));
        s.Tick(0.45); Assert.That(s.Phase, Is.EqualTo(TaxiCallState.Leave));
        Assert.That(sent, Is.Empty, "the request goes out at the end of 0x69");
        s.Tick(1.0);
        Assert.That(s.Phase, Is.EqualTo(TaxiCallState.Away));
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(sent[0], Is.EqualTo(new RequestTaxiRide(2000001)));
        s.Tick(2.7); Assert.That(s.Finished, Is.False);
        s.Tick(0.5); Assert.That(s.Finished, Is.True);
        Assert.That(sent, Has.Count.EqualTo(1));
    }

    [Test]
    public void AnotherPlayersCallSendsNothingAndCancelStopsItUnlessTheRequestIsOut()
    {
        var sent = new List<IOutPacket>();
        var other = new TaxiCallState { Send = sent.Add };
        other.Begin(true, 5, 100, false);
        for (int i = 0; i < 100; i++) other.Tick(0.1);
        Assert.That(sent, Is.Empty);
        var s = new TaxiCallState { Send = sent.Add };
        s.Begin(true, 5, 100, true);
        s.Cancel();
        Assert.That(s.Finished, Is.True, "cancelled in 0x65");
        s.Begin(true, 5, 100, true);
        s.Tick(0.1); s.Tick(1.95); s.Tick(0.5); s.Tick(0.5);
        Assert.That(s.Phase, Is.EqualTo(TaxiCallState.Leave));
        s.Cancel();
        Assert.That(s.Finished, Is.False, "0x69: the request is out, Cancel keeps phase 0x69");
        var bad = new TaxiCallState();
        bad.Begin(false, 0, 0, true);
        Assert.That(bad.Finished, Is.True, "data that is not a StateTaxiCallData: idle");
    }

    [Test]
    public void TheBestDriverCallIsUsedFromItsLevel()
    {
        Assert.That(TaxiCallState.Duration(24, 25, 3000, 1000), Is.EqualTo(3000));
        Assert.That(TaxiCallState.Duration(25, 25, 3000, 1000), Is.EqualTo(1000));
    }

    static TaxiCallState Running(List<IOutPacket> sent, int phase)
    {
        var s = new TaxiCallState { Send = sent.Add };
        s.Begin(true, 9, 100, true);
        double[] steps = { 0.1, 1.9, 0.45, 0.45 };
        for (int i = 0; s.Phase < phase; i++) s.Tick(steps[i]);
        Assert.That(s.Phase, Is.EqualTo(phase));
        return s;
    }

    [Test]
    public void AMoveInputInPhase65LeavesTheCallAndNothingIsSent()
    {
        var sent = new List<IOutPacket>();
        var s = Running(sent, TaxiCallState.Call);
        Assert.That(s.HoldsMove(false), Is.True, "no input: the state holds the body");
        Assert.That(s.HoldsMove(true), Is.False, "the move goes on to the walk state");
        Assert.That(s.Finished, Is.True);
        for (int i = 0; i < 100; i++) s.Tick(0.1);
        Assert.That(sent, Is.Empty, "the call ended, so no RequestTaxi goes out");
    }

    [Test]
    public void AMoveInputAfterPhase65IsHeldAndTheCallRunsOn()
    {
        var sent = new List<IOutPacket>();
        var s = Running(sent, TaxiCallState.Arrive);
        Assert.That(s.HoldsMove(true), Is.True);
        Assert.That(s.Finished, Is.False);
        s.Tick(1.9); s.Tick(0.45); s.Tick(0.45); s.Tick(1.0);
        Assert.That(sent, Has.Count.EqualTo(1));
    }

    [Test]
    public void LeavingTheStateInAnyPhaseBefore69EndsTheCallWithoutTheRequest()
    {
        foreach (int phase in new[] { TaxiCallState.Call, TaxiCallState.Arrive, TaxiCallState.Board, TaxiCallState.Ride })
        {
            var sent = new List<IOutPacket>();
            var s = Running(sent, phase);
            s.LeaveState();                               // death, a skill, another state set
            Assert.That(s.Finished, Is.True, $"phase {phase:x}");
            for (int i = 0; i < 100; i++) s.Tick(0.1);
            Assert.That(sent, Is.Empty, $"phase {phase:x}");
        }
        var out69 = new List<IOutPacket>();
        var l = Running(out69, TaxiCallState.Leave);
        l.LeaveState();
        Assert.That(l.Finished, Is.True, "a state change ends the call in 0x69 too (ChangeState swaps the state in every phase)");
        l.Tick(1.0);
        Assert.That(out69, Is.Empty, "and no request goes out");
        var hit = new List<IOutPacket>();
        var h = Running(hit, TaxiCallState.Leave);
        h.Cancel();
        h.Tick(1.0);
        Assert.That(hit, Has.Count.EqualTo(1), "a hit's vfunc_6 keeps 0x69 and the request goes out");
        var early = Running(new List<IOutPacket>(), TaxiCallState.Board);
        early.Cancel();
        Assert.That(early.Finished, Is.True, "a hit before 0x69 ends the call");
    }

    [Test]
    public void NoSkillFiresInPhases66To6aAndOneInPhase65LeavesTheCall()
    {
        foreach (int phase in new[] { TaxiCallState.Arrive, TaxiCallState.Board, TaxiCallState.Ride, TaxiCallState.Leave })
        {
            var s = Running(new List<IOutPacket>(), phase);
            Assert.That(s.HoldsSkill, Is.True, $"phase {phase:x}: the cast is held, the ride goes on");
            Assert.That(s.Finished, Is.False);
        }
        var call = Running(new List<IOutPacket>(), TaxiCallState.Call);
        Assert.That(call.HoldsSkill, Is.False, "phase 0x65 lets the skill fire");
        call.LeaveState();                                // the cast's state change
        Assert.That((call.Finished, call.HoldsSkill), Is.EqualTo((true, false)));
        Assert.That(new TaxiCallState().HoldsSkill, Is.False, "no call");
    }

    [Test]
    public void ASkillWithoutASequenceInPhase65KeepsTheCall()
    {
        var s = Running(new List<IOutPacket>(), TaxiCallState.Call);
        s.SkillCast(false);                               // no sequence name: Ridee / FireFirstAttack, no vfunc_59(0x10)
        Assert.That((s.Finished, s.Phase), Is.EqualTo((false, TaxiCallState.Call)));
        s.SkillCast(true);                                // a sequence enters the skill state
        Assert.That(s.Finished, Is.True);
    }
}
