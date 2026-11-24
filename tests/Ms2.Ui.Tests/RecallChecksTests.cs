using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The recall scrolls: CCashCallManager::CanRecall / FUN_140b04430 (cashcallmanager.cpp:294-367), RecallGuild and
/// RecallWedding (itemfunctiondispatch.cpp:1346-1389), the party window's recall checks (uipartydialog.cpp:894-962), the
/// member counts (guilddef_1.cpp:2232, partymanager.cpp:840), FindItemByTag's pick (inventory.cpp:380, :630) and StateRecall
/// (statesummon.cpp:479-587).
/// </summary>
public class RecallChecksTests
{
    /// <summary>Facts that pass every check: alive, idle, channel 1, no service type, the field allows it, no capacity.</summary>
    static RecallFacts Ok() => new()
    {
        HasPlayer = true, StateId = 1, Channel = 1, FieldAllowsRecall = true, StateAllows = true, Players = 1,
    };

    static int Notice(RecallFacts f, int count) { RecallChecks.CanRecall(f, count, out int n); return n; }

    [Test]
    public void CanRecallRefusesInTheClientsOrder()
    {
        Assert.That(RecallChecks.CanRecall(Ok(), 3, out int n), Is.True);
        Assert.That(n, Is.Zero);
        var f = Ok(); f.HasPlayer = false;
        Assert.That((RecallChecks.CanRecall(f, 1, out n), n), Is.EqualTo((false, 0)), "no own pc: refused, no notice");
        f = Ok(); f.Dead = true; f.OnWater = true; f.Channel = 0;
        Assert.That(Notice(f, 1), Is.EqualTo(0xb57), "dead comes first");
        f = Ok(); f.StateId = 0x2c;
        Assert.That(Notice(f, 1), Is.EqualTo(0xb57), "already recalling");
        f = Ok(); f.StateId = 2; f.Channel = 0;
        Assert.That(Notice(f, 1), Is.EqualTo(0xb59), "not idle, before the channel");
        f = Ok(); f.OnWater = true;
        Assert.That(Notice(f, 1), Is.EqualTo(0xb59), "on water");
        f = Ok(); f.Channel = 0;
        Assert.That(Notice(f, 1), Is.EqualTo(0xb58), "no channel");
        f = Ok(); f.ServiceType = true;
        Assert.That(Notice(f, 1), Is.EqualTo(0xb58), "a service type");
        f = Ok(); f.FieldAllowsRecall = false;
        Assert.That(Notice(f, 1), Is.EqualTo(0xb58), "the field prohibits it");
        // capacity: players + (count + the portals' sum) over the capacity refuses; equal passes; 0 is no limit
        f = Ok(); f.Capacity = 10; f.Players = 4; f.PortalUsers = 2;
        Assert.That(Notice(f, 4), Is.Zero, "4 + 4 + 2 = 10 fits");
        Assert.That(Notice(f, 5), Is.EqualTo(0xb5f), "4 + 5 + 2 = 11 does not");
        f.Capacity = 0;
        Assert.That(Notice(f, 500), Is.Zero, "no capacity, no limit");
    }

    [Test]
    public void StartCastsOrRidesOffAfterTheChecks()
    {
        Assert.That(RecallChecks.Start(Ok(), 1, out int n), Is.EqualTo(RecallChecks.Outcome.Cast));
        var f = Ok(); f.Riding = true; f.StateAllows = false;
        Assert.That(RecallChecks.Start(f, 1, out n), Is.EqualTo(RecallChecks.Outcome.RideOff), "riding: the ride off, the state gate not asked");
        f = Ok(); f.StateAllows = false;
        Assert.That((RecallChecks.Start(f, 1, out n), n), Is.EqualTo((RecallChecks.Outcome.Refused, 0xb59)));
        f = Ok(); f.Dead = true;
        Assert.That((RecallChecks.Start(f, 1, out n), n), Is.EqualTo((RecallChecks.Outcome.Refused, 0xb57)));
    }

    [Test]
    public void TheGuildRecallChecksTheGuildThenCanRecallForTheOtherMembers()
    {
        var f = Ok(); f.Shutdown = true;
        Assert.That((RecallChecks.Guild(f, true, 5, out int n), n), Is.EqualTo((RecallChecks.Outcome.Refused, 0x2f7)));
        Assert.That((RecallChecks.Guild(Ok(), false, 5, out n), n), Is.EqualTo((RecallChecks.Outcome.Refused, 0xb5a)));
        Assert.That((RecallChecks.Guild(Ok(), true, 1, out n), n), Is.EqualTo((RecallChecks.Outcome.Refused, 0xb5c)), "only the own pc");
        f = Ok(); f.Capacity = 5; f.Players = 2;
        Assert.That((RecallChecks.Guild(f, true, 5, out n), n), Is.EqualTo((RecallChecks.Outcome.Refused, 0xb5f)), "2 + 4 others > 5");
        Assert.That(RecallChecks.Guild(f, true, 4, out n), Is.EqualTo(RecallChecks.Outcome.Cast), "2 + 3 others = 5");
    }

    [Test]
    public void TheWeddingRecallNeedsTheMarriageThenRecallsOne()
    {
        Assert.That((RecallChecks.Wedding(Ok(), false, true, out int n, out string key), n, key),
            Is.EqualTo((RecallChecks.Outcome.Refused, 0, RecallChecks.NotMarriedKey)));
        Assert.That((RecallChecks.Wedding(Ok(), true, false, out n, out key), key), Is.EqualTo((RecallChecks.Outcome.Refused, RecallChecks.NotMarriedKey)));
        var f = Ok(); f.Capacity = 2; f.Players = 2;
        Assert.That((RecallChecks.Wedding(f, true, true, out n, out key), n, key), Is.EqualTo((RecallChecks.Outcome.Refused, 0xb5f, (string)null)));
        Assert.That(RecallChecks.Wedding(Ok(), true, true, out n, out key), Is.EqualTo(RecallChecks.Outcome.Cast));
    }

    [Test]
    public void ThePartyButtonChecksThePartyThenCanRecallThenTheStateGate()
    {
        var f = Ok(); f.Shutdown = true;
        Assert.That((RecallChecks.PartyButton(f, true, 3, out int n), n), Is.EqualTo((false, 0x2f7)));
        Assert.That((RecallChecks.PartyButton(Ok(), false, 3, out n), n), Is.EqualTo((false, 0xb5b)));
        Assert.That((RecallChecks.PartyButton(Ok(), true, 1, out n), n), Is.EqualTo((false, 0xb5d)));
        f = Ok(); f.Riding = true; f.StateId = 0x21;
        Assert.That((RecallChecks.PartyButton(f, true, 3, out n), n), Is.EqualTo((false, 0xb59)), "no riding branch: the state refuses");
        f = Ok(); f.StateAllows = false;
        Assert.That((RecallChecks.PartyButton(f, true, 3, out n), n), Is.EqualTo((false, 0xb59)));
        Assert.That(RecallChecks.PartyButton(Ok(), true, 3, out n), Is.True);
    }

    [Test]
    public void TheMemberCountsTakeOnlineMembersInAKnownFieldThatIsNoTutorial()
    {
        bool? Tutorial(int id) => id switch { 100 => false, 200 => true, _ => null };
        var guild = new[]
        {
            new GuildMember { Mode = 3, Offline = 0, MapId = 100 },
            new GuildMember { Mode = 3, Offline = 0, MapId = 100 },
            new GuildMember { Mode = 3, Offline = 1, MapId = 100 },   // offline (f158)
            new GuildMember { Mode = 1, Offline = 0, MapId = 100 },   // not a full record (fb0)
            new GuildMember { Mode = 3, Offline = 0, MapId = 200 },   // a tutorial field
            new GuildMember { Mode = 3, Offline = 0, MapId = 999 },   // a field FindField does not find
        };
        Assert.That(RecallChecks.GuildMembersOutsideTutorial(guild, Tutorial), Is.EqualTo(2));
        PartyMember P(bool offline, int current, int map) => new() { Offline = offline, User = new PartyUser { CurrentMapId = current, MapId = map } };
        var party = new[] { P(false, 100, 200), P(false, 200, 100), P(true, 100, 100), P(false, 999, 100), P(false, 100, 100) };
        Assert.That(RecallChecks.PartyMembersOutsideTutorial(party, Tutorial), Is.EqualTo(2), "the field is user m_28, not m_2c");
    }

    [Test]
    public void FindItemByTagTakesTheEarliestExpiryThenTheBagOrder()
    {
        var items = new (long, long, bool, int)[]
        {
            (1, 0, true, 0x14),        // no expiry
            (2, 500, false, 0x14),     // not usable
            (3, 900, true, 0x13),      // another tag
            (4, 700, true, 0x14),
            (5, 600, true, 0x14),      // the earliest usable expiry
            (6, 0, true, 0x14),
        };
        Assert.That(RecallChecks.FirstByTag(items, 0x14), Is.EqualTo(5));
        Assert.That(RecallChecks.FirstByTag(new (long, long, bool, int)[] { (1, 0, true, 0x14), (6, 0, true, 0x14) }, 0x14), Is.EqualTo(1));
        Assert.That(RecallChecks.FirstByTag(items, 0x15), Is.Zero);
        Assert.That(RecallChecks.FirstByTag(items, 0), Is.Zero, "tag 0 finds nothing");
    }

    sealed class Host : IRecallHost
    {
        public bool Bar = true, Has = true;
        public long Now;
        public readonly List<IOutPacket> Sent = new();
        public readonly List<long> Used = new();
        public string Text, Anim;
        public int Ms, Cancels;
        public ICastingEventListener Listener;
        public bool HasPlayer => Has;
        public void Send(IOutPacket packet) => Sent.Add(packet);
        public bool StartCasting(ICastingEventListener listener, string text, int durationMs)
        {
            Listener = listener; Text = text; Ms = durationMs;
            return Bar;
        }
        public void CancelCasting() => Cancels++;
        public long NowMs => Now;
        public void PlayPlayerAnim(string animName, bool loop) => Anim = animName;
        public void StopPlayerInteractAnim() => Anim = null;
        public bool UseItem(long uid) { Used.Add(uid); return true; }
    }

    [Test]
    public void AFinishedRecallCastUsesTheScrollOrSendsTheMeratRecall()
    {
        var h = new Host();
        var s = new RecallCast(h);
        s.Start(0x77, 5000, "Recalling");
        Assert.That((h.Text, h.Ms, h.Anim, s.IsCasting, s.Target), Is.EqualTo(("Recalling", 5000, "Object_React_A", true, 0x77L)));
        Assert.That(s.Tick(false), Is.False);
        h.Listener.OnCastingFinished();
        Assert.That(s.Tick(true), Is.True, "the finish wins over the move input");
        Assert.That((h.Used.Single(), h.Sent.Count, s.IsCasting, h.Anim), Is.EqualTo((0x77L, 0, false, (string)null)));

        s.Start(RecallChecks.NoScroll, 5000, "Recalling");
        h.Listener.OnCastingFinished();
        s.Tick(false);
        Assert.That(h.Sent.Single(), Is.EqualTo(new PartyRecallRequest()));
        Assert.That(h.Used, Has.Count.EqualTo(1), "no item use for the merat recall");
    }

    [Test]
    public void MoveInputOrALostPcEndsTheRecallWithoutASend()
    {
        var h = new Host();
        var s = new RecallCast(h);
        s.Start(0x77, 5000, "Recalling");
        Assert.That(s.Tick(true), Is.True);
        Assert.That((h.Cancels, h.Sent.Count, h.Used.Count, s.IsCasting), Is.EqualTo((1, 0, 0, false)), "state 2's exit cancels the bar");

        s.Start(0x77, 5000, "Recalling");
        h.Listener.OnCastingCancelled();
        Assert.That((s.IsCasting, h.Used.Count), Is.EqualTo((false, 0)), "a cancelled bar goes back to state 1");

        s.Start(0x77, 5000, "Recalling");
        h.Has = false;
        Assert.That(s.Tick(false), Is.True);
        Assert.That((h.Cancels, h.Used.Count), Is.EqualTo((1, 0)), "the pc gone: no send, no cancel");

        // without a casting bar the time runs on the clock
        h.Has = true; h.Bar = false; h.Now = 1000;
        s.Start(0x78, 5000, "Recalling");
        h.Now = 5999;
        Assert.That(s.Tick(false), Is.False);
        h.Now = 6000;
        Assert.That(s.Tick(false), Is.True);
        Assert.That(h.Used.Last(), Is.EqualTo(0x78));
    }

    [Test]
    public void ThePacketsAreTheClientsBytes()
    {
        static string Hex(IOutPacket p) { var w = new Maple2.PacketLib.Tools.ByteWriter(); p.Write(w); return Convert.ToHexString(w.Buffer.AsSpan(0, w.Length)); }
        Assert.That(Hex(new PartyRecallRequest()), Is.EqualTo("1D"));
        Assert.That(new PartyRecallRequest().Op, Is.EqualTo(Outbound.Party));
        Assert.That(Hex(new RideOffRecallRequest(0x0102030405060708)), Is.EqualTo("010800" + "0807060504030201"));
        Assert.That(new RideOffRecallRequest(1).Op, Is.EqualTo(Outbound.RequestRide));
        // the answer's ride-off action reads back as the recall with its target
        var r = new Maple2.PacketLib.Tools.ByteWriter(); r.WriteByte(8); r.WriteByte(0); r.WriteLong(0x77);
        var action = RideOffAction.Read(new Maple2.PacketLib.Tools.ByteReader(r.Buffer.AsSpan(0, r.Length).ToArray()));
        Assert.That(action, Is.EqualTo(new RideOffRecall(0, 0x77)));
        // the party member's m_28 (the field the tutorial count reads) is read, apart from m_2c
        var u = new Maple2.PacketLib.Tools.ByteWriter();
        new PartyUser { CharacterId = 5, Name = "A", CurrentMapId = 111, MapId = 222 }.Write(u);
        var back = PartyUser.Read(new Maple2.PacketLib.Tools.ByteReader(u.Buffer.AsSpan(0, u.Length).ToArray()));
        Assert.That((back.CurrentMapId, back.MapId), Is.EqualTo((111, 222)));
    }
}
