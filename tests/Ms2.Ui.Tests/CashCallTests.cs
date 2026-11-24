using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The air taxi call: CCashCallManager::FUN_140b030b0 (cashcallmanager.cpp:65, the exe's tail at 0x140b033de), the ticket count
/// FUN_140a5f7b0 (inventory.cpp:835), StateCashCall (statecashcalldata.cpp) and the RideOffActionCashCall request
/// (rideonaction.cpp:399).
/// </summary>
public class CashCallTests
{
    /// <summary>A player who can call: alive, idle, on foot, the field visited, no item, no VIP, no event.</summary>
    static CashCallFacts Ok() => new()
    {
        HasPlayer = true, StateId = 1, CanEnterCashCall = true, Visited = true,
        MeratAirTaxiPrice = CashCall.DefaultMeratAirTaxiPrice,
    };

    static (CashCall.Outcome, int, long) Start(CashCallFacts f) => (CashCall.Start(f, out int k, out long u), k, u);

    [Test]
    public void NoPlayerADeadOneOrOneAlreadyCallingIsRefused()
    {
        var f = Ok(); f.HasPlayer = false;
        Assert.That(Start(f).Item1, Is.EqualTo(CashCall.Outcome.Refused));
        f = Ok(); f.Dead = true;
        Assert.That(Start(f).Item1, Is.EqualTo(CashCall.Outcome.Refused));
        f = Ok(); f.StateId = 0x25;
        Assert.That(Start(f).Item1, Is.EqualTo(CashCall.Outcome.Refused), "StateTaxiCall");
        f = Ok(); f.StateId = 0x2a;
        Assert.That(Start(f).Item1, Is.EqualTo(CashCall.Outcome.Refused), "StateCashCall");
        f = Ok(); f.CanEnterCashCall = false;
        Assert.That(Start(f).Item1, Is.EqualTo(CashCall.Outcome.Refused), "FUN_141527160(0x2a) refuses after the kind is picked");
    }

    [Test]
    public void AVisitedFieldTakesTheFreeEventThenAnAirTaxiThenAnAdvancedTicketThenMesos()
    {
        var f = Ok();
        Assert.That(Start(f), Is.EqualTo((CashCall.Outcome.Call, CashCall.KindMeso, 0L)), "no ticket: the meso air taxi");
        f.AdvancedUid = 22;
        Assert.That(Start(f), Is.EqualTo((CashCall.Outcome.Call, CashCall.KindTicket, 22L)), "FindItemByTag(0xb) when no air_taxi");
        f.AirTaxiUid = 11;
        Assert.That(Start(f), Is.EqualTo((CashCall.Outcome.Call, CashCall.KindTicket, 11L)), "FindItemByTag(10) first");
        f.FreeAirTaxi = true;
        Assert.That(Start(f), Is.EqualTo((CashCall.Outcome.Call, CashCall.KindTicket, 0L)), "a free air taxi event uses no item");
        f = Ok(); f.Vip = true; f.AirTaxiUid = 11;
        Assert.That(Start(f), Is.EqualTo((CashCall.Outcome.Call, CashCall.KindTicket, 11L)), "VIP without feature 0x57 is a plain player");
        f.VipAirTaxiFeature = true; f.Visited = false;
        Assert.That(Start(f), Is.EqualTo((CashCall.Outcome.Call, CashCall.KindTicket, 0L)), "VIP with feature 0x57 goes anywhere free");
    }

    [Test]
    public void AFieldNotVisitedNeedsThreeAdvancedTicketsOrMerats()
    {
        var f = Ok(); f.Visited = false; f.AirTaxiUid = 11; f.AdvancedUid = 22; f.AdvancedTickets = 2;
        Assert.That(Start(f).Item1, Is.EqualTo(CashCall.Outcome.Refused), "two tickets and no merat feature");
        f.RegionJP = true;
        Assert.That(Start(f), Is.EqualTo((CashCall.Outcome.Call, CashCall.KindTicket, 22L)), "JP needs one");
        f.RegionJP = false; f.AdvancedTickets = 3;
        Assert.That(Start(f), Is.EqualTo((CashCall.Outcome.Call, CashCall.KindTicket, 22L)), "three: the advanced ticket, never the air_taxi");
        f.AdvancedUid = 0;
        Assert.That(Start(f).Item1, Is.EqualTo(CashCall.Outcome.Refused), "counted but FindItemByTag(0xb) found none");
        f = Ok(); f.Visited = false; f.MeratAirTaxiFeature = true; f.MeratBalance = 389;
        Assert.That(Start(f).Item1, Is.EqualTo(CashCall.Outcome.Refused), "fewer merats than MeratAirTaxiPrice");
        f.MeratBalance = 390;
        Assert.That(Start(f), Is.EqualTo((CashCall.Outcome.Call, CashCall.KindMerat, 0L)));
    }

    [Test]
    public void ARiderRidesOffWithTheKindAndItem()
    {
        var f = Ok(); f.Riding = true; f.AirTaxiUid = 11; f.CanEnterCashCall = false;
        Assert.That(Start(f), Is.EqualTo((CashCall.Outcome.RideOff, CashCall.KindTicket, 11L)), "the ride off comes before FUN_141527160");
        static string Hex(IOutPacket p) { var w = new Maple2.PacketLib.Tools.ByteWriter(); p.Write(w); return Convert.ToHexString(w.Buffer.AsSpan(0, w.Length)); }
        Assert.That(Hex(new RideOffCashCallRequest(0x01020304, 2, 0x0a0b0c0d0e0f1011)), Is.EqualTo("010400" + "04030201" + "02" + "11100F0E0D0C0B0A"));
        Assert.That(new RideOffCashCallRequest(1, 1, 0).Op, Is.EqualTo(Outbound.RequestRide));
        // the answer's action reads back with the three fields the state starts from (mypc.cpp:3420-3425)
        var r = new Maple2.PacketLib.Tools.ByteWriter(); r.WriteByte(4); r.WriteByte(0); r.WriteInt(7); r.WriteByte(2); r.WriteLong(11);
        var action = RideOffAction.Read(new Maple2.PacketLib.Tools.ByteReader(r.Buffer.AsSpan(0, r.Length).ToArray()));
        Assert.That(action, Is.EqualTo(new RideOffCashCall(0, 7, 2, 11)));
    }

    [Test]
    public void TheTicketCountSumsTheUsableItemsAmounts()
    {
        var items = new (long, long, bool, int, int)[]
        {
            (1, 0, true, CashCall.AdvancedAirTaxiTag, 2), (2, 0, false, CashCall.AdvancedAirTaxiTag, 5),
            (3, 0, true, CashCall.AirTaxiTag, 4), (4, 0, true, CashCall.AdvancedAirTaxiTag, 1),
        };
        Assert.That(CashCall.CountByTag(items, CashCall.AdvancedAirTaxiTag), Is.EqualTo(3));
        Assert.That(CashCall.CountByTag(items, CashCall.AirTaxiTag), Is.EqualTo(4));
        Assert.That(CashCall.CountByTag(items, 0), Is.Zero);
    }

    // ---- StateCashCall ----

    sealed class Host : ICashCallHost
    {
        public readonly List<string> Log = new();
        public readonly List<IOutPacket> Sent = new();
        public readonly Dictionary<long, string> Bag = new();
        public readonly Dictionary<string, string> Resource = new()
        {
            ["EFFECT_AIRTAXI_CASH"] = "ETC/Eff_AirTaxi_Cash.xml", ["EFFECT_AIRTAXI_MESO"] = "ETC/Eff_AirTaxi_meso.xml",
        };
        public bool Shown = true;
        public void Send(IOutPacket packet) => Sent.Add(packet);
        public bool UseItem(long uid, string argument) { if (!Bag.ContainsKey(uid)) return false; Log.Add($"use {uid} {argument}"); return true; }
        public string ItemName(long uid) => Bag.TryGetValue(uid, out var n) ? n : null;
        public void SystemNotice(string text) => Log.Add("notice " + text);
        public string Common(int id) => $"[{id:x}{{0}}]";
        public string CommonResource(string key) => Resource.TryGetValue(key, out var v) ? v : "";
        public void StartCasting(string text, int durationMs) => Log.Add($"cast {text} {durationMs}");
        public void CancelCasting() => Log.Add("bar off");
        public void PlayAnimation(string name) => Log.Add("anim " + name);
        public void PlayIdle() => Log.Add("idle");
        public void SetShown(bool shown) { Shown = shown; Log.Add(shown ? "show" : "hide"); }
        public void SpawnEffect(string path) => Log.Add("effect " + path);
    }

    static void Run(CashCallState s, int ms) { for (int i = 0; i < ms / 10; i++) s.Tick(0.01); }

    [Test]
    public void TheMesoAirTaxiCastsArrivesHidesSendsModeThreeAndReturnsToIdle()
    {
        var h = new Host();
        var s = new CashCallState(h);
        s.Begin(CashCall.KindMeso, 0, 2000062, 3000);
        Assert.That(h.Log, Is.EqualTo(new[] { "anim AirTaxi_A", "cast [64c{0}] 3000" }));
        Assert.That(s.Phase, Is.EqualTo(CashCallState.Cast));
        Run(s, 3090);
        Assert.That(s.Phase, Is.EqualTo(CashCallState.Cast), "the cast ends 100 ms after the bar");
        Run(s, 10);
        Assert.That(s.Phase, Is.EqualTo(CashCallState.Arrive));
        Assert.That(h.Log.Skip(2), Is.EqualTo(new[] { "bar off", "effect ETC/Eff_AirTaxi_meso.xml", "idle" }));
        Run(s, 3190);
        Assert.That((s.Phase, h.Shown), Is.EqualTo((CashCallState.Arrive, true)), "the meso helicopter waits 0xc80 ms");
        Run(s, 10);
        Assert.That((s.Phase, h.Shown), Is.EqualTo((CashCallState.Leave, false)), "OnArrived hides the pc");
        Assert.That(h.Sent, Is.Empty);
        Run(s, 1000);
        Assert.That(s.Phase, Is.EqualTo(CashCallState.Away));
        Assert.That(h.Sent, Is.EqualTo(new IOutPacket[] { new RequestTaxiCash(1, 2000062) }), "RequestTaxi mode 3 with the field");
        Run(s, 2990);
        Assert.That(s.Finished, Is.False);
        Run(s, 10);
        Assert.That((s.Finished, h.Shown), Is.EqualTo((true, true)), "vfunc_7(1): shown again, idle");
        Assert.That(h.Log.Count(l => l.StartsWith("notice")), Is.Zero, "sent: no notice on leave");
    }

    [Test]
    public void ATicketCallUsesTheItemWithTheFieldAndTheCashHelicopterWaitsOneSecond()
    {
        var h = new Host();
        h.Bag[55] = "Ticket";
        var s = new CashCallState(h);
        s.Begin(CashCall.KindTicket, 55, 2000062, 500);
        Run(s, 600);
        Assert.That(s.Phase, Is.EqualTo(CashCallState.Arrive));
        Run(s, 1000);
        Assert.That(s.Phase, Is.EqualTo(CashCallState.Leave), "EFFECT_AIRTAXI_CASH waits 1 s");
        Run(s, 1000);
        Assert.That(h.Sent, Is.Empty, "an item call sends no RequestTaxi");
        Assert.That(h.Log, Does.Contain("use 55 2000062"));
        // the merat call (kind 3, no item) is RequestTaxi mode 4; a call with no vehicle effect goes on at once
        h = new Host(); h.Resource.Clear();
        s = new CashCallState(h);
        s.Begin(CashCall.KindMerat, 0, 7, 500);
        Run(s, 600);
        Assert.That(s.Phase, Is.EqualTo(CashCallState.Leave), "no effect: 0x6d after 100 ms");
        Run(s, 100);
        Assert.That(h.Sent, Is.EqualTo(new IOutPacket[] { new RequestTaxiCash(3, 7) }));
    }

    [Test]
    public void AMoveInTheCastLeavesWithTheItemsNoticeAndLaterPhasesHoldTheBody()
    {
        var h = new Host();
        h.Bag[55] = "Ticket";
        var s = new CashCallState(h);
        s.Begin(CashCall.KindTicket, 55, 9, 500);
        Assert.That(s.HoldsMove(false), Is.True, "the cast holds the body while no move comes");
        Assert.That(s.HoldsMove(true), Is.False, "a move in 0x6b goes to the walk state");
        Assert.That(s.Finished, Is.True);
        Assert.That(h.Log, Does.Contain("notice [a0Ticket]"), "vfunc_5: not sent, the item's notice 0xa0");
        Assert.That(h.Sent, Is.Empty);
        h = new Host();
        s = new CashCallState(h);
        s.Begin(CashCall.KindMeso, 0, 9, 500);
        Run(s, 600);
        Assert.That(s.HoldsMove(true), Is.True, "after the cast vfunc_8 returns first: the move is held");
        Assert.That(s.Finished, Is.False);
        s.LeaveState();
        Assert.That(h.Log.Count(l => l.StartsWith("notice")), Is.Zero, "no item, no notice");
    }

    [Test]
    public void TheDurationIsTheCallKindsConstant()
    {
        var k = new Dictionary<string, int> { ["TaxiCallDuration"] = 3000, ["AirTaxiCashCallDuration"] = 500, ["AirTaxiMesoCallDuration"] = 2500, ["BankCallDuration"] = 400 };
        int C(string key, int def) => k.TryGetValue(key, out var v) ? v : def;
        Assert.That(CashCallState.Duration(1, C, true), Is.EqualTo(2500));
        Assert.That(CashCallState.Duration(2, C, true), Is.EqualTo(500));
        Assert.That(CashCallState.Duration(3, C, true), Is.EqualTo(500));
        Assert.That(CashCallState.Duration(4, C, true), Is.EqualTo(3000));
        Assert.That(CashCallState.Duration(6, C, true), Is.EqualTo(400));
        Assert.That(CashCallState.Duration(1, C, false), Is.EqualTo(2300), "200 less for another pc");
        Assert.That(CashCallState.Duration(2, (_, d) => d, true), Is.Zero, "AirTaxiCashCallDuration's default is 0");
        Assert.That(CashCallState.AnimationName(5), Is.EqualTo("EmergencyHelicopter_A"));
        Assert.That(CashCallState.AnimationName(2), Is.EqualTo("AirTaxi_A"));
    }
}
