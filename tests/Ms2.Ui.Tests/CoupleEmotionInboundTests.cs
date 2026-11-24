using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CNetworkService::OnBuddyEmote (networkservice.cpp:1096-1184), HandleCoupleEmotionButton, RequestCoupleEmotion and
/// StateCoupleEmotion::Stop over a fake host.</summary>
public class CoupleEmotionInboundTests
{
    const uint Emo = 90220001;
    static readonly CouplePeer Asker = new(77, 4001, "Asker", new Vector3(100, 0, 0), new Vector3(0, 0, 90));

    sealed class Host : ICoupleEmotionHost
    {
        public readonly List<IOutPacket> Sent = new();
        public readonly List<(int Id, string Name)> Notices = new();
        public readonly List<string> Calls = new();
        public readonly CoupleEmotionState Own = new();
        public readonly Dictionary<long, CouplePeer> Peers = new() { [Asker.CharacterId] = Asker };
        public bool RefuseRequests { get; set; }
        public bool HasPlayer { get; set; } = true;
        public int StateId { get; set; } = 1;
        public bool InCubeMode { get; set; }
        public bool OnWater { get; set; }
        public int SubSceneId { get; set; }
        public long CharacterId { get; set; } = 4002;
        public int ObjectId { get; set; } = 55;
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; } = new(0, 0, 45);
        public string Name { get; set; } = "Me";
        public string ShownBoxExtra { get; set; }
        public int MaxDistance { get; set; } = 300;
        public bool Riding { get; set; }
        public bool Placement = true;
        public HashSet<long> BlockedIds = new();
        public bool Blocked(long id) => BlockedIds.Contains(id);
        public CouplePeer FindPeer(long id) => Peers.TryGetValue(id, out var p) ? p : null;
        public CouplePeer FindPeerByObject(int id) => Peers.Values.FirstOrDefault(p => p.ObjectId == id);
        public void Send(IOutPacket p) => Sent.Add(p);
        public void Notice(int id, string name) => Notices.Add((id, name));
        public void ShowInviteBox(int emotion, long id, string name) => Calls.Add($"box {emotion} {id} {name}");
        public void EnterIdle() { Calls.Add("idle"); Own.Exit(); StateId = 1; }
        public void EnterCouple(CoupleEmotionStart d, int sub) { Calls.Add($"couple {sub:x}"); Own.Enter(d, sub); StateId = 0x3a; }
        public void SetSub(int sub) { Calls.Add($"sub {sub:x}"); Own.SetSub(sub); }
        public void SetEffectTick(int tick) => Calls.Add($"tick {tick}");
        public void RideOffForCouple(int emotion, long id) => Calls.Add($"rideoff {emotion} {id}");
        public bool FindPlacement(out Vector3 a, out Vector3 b, out float yaw)
        {
            Calls.Add("place");
            a = new Vector3(1, 2, 3); b = new Vector3(4, 5, 6); yaw = 30f;
            return Placement;
        }

        /// <summary>The own request (the cast's 0x3a sub 0x93) on the asker.</summary>
        public Host Requesting()
        {
            EnterCouple(new CoupleEmotionStart(Emo, Asker.ObjectId, ObjectId, Asker.Position, Asker.Rotation), CoupleEmotionState.Waiting);
            Calls.Clear();
            return this;
        }
    }

    static void Handle(Host h, BuddyEmoteMessage m) => CoupleEmotionInbound.Handle(m, h, h.Own);

    [Test]
    public void AnInviteShowsTheBoxAndAnswersModeOne()
    {
        var h = new Host();
        Handle(h, new BuddyEmoteMessage.Invite((int)Emo, 4001, "Asker"));
        Assert.That(h.Calls, Is.EqualTo(new[] { $"box {Emo} 4001 Asker" }));
        Assert.That(h.Sent, Is.EqualTo(new IOutPacket[] { new BuddyEmoteInviteReceived(4001) }));
        Assert.That(h.Notices, Is.Empty);
    }

    [Test]
    public void EachInviteRefusalHasItsReasonAndNotice()
    {
        void Refused(Action<Host> set, byte reason, int notice)
        {
            var h = new Host();
            set(h);
            Handle(h, new BuddyEmoteMessage.Invite((int)Emo, 4001, "Asker"));
            Assert.That(h.Sent, Is.EqualTo(new IOutPacket[] { new BuddyEmoteRefuse(4001, reason) }));
            Assert.That(h.Notices, Is.EqualTo(notice == 0 ? Array.Empty<(int, string)>() : new[] { (notice, (string)null) }));
            Assert.That(h.Calls, Is.Empty, "no box");
        }
        Refused(h => h.RefuseRequests = true, 3, 0);
        Refused(h => h.StateId = 0x3a, 2, 0xceb);
        Refused(h => h.ShownBoxExtra = "coupleEmotion", 1, 0xceb);
        Refused(h => h.BlockedIds.Add(4001), 5, 0xcfa);
        Refused(h => h.InCubeMode = true, 5, 0xcfa);
        Refused(h => h.SubSceneId = 0x620001f, 5, 0xcfa);
        // the option is read before the player: even without one it answers
        Refused(h => { h.RefuseRequests = true; h.HasPlayer = false; }, 3, 0);
    }

    [Test]
    public void TheInviteChecksRunInOrder()
    {
        var h = new Host { StateId = 0x3a, ShownBoxExtra = "coupleEmotion" };
        h.BlockedIds.Add(4001);
        Handle(h, new BuddyEmoteMessage.Invite((int)Emo, 4001, "Asker"));
        Assert.That(h.Sent, Is.EqualTo(new IOutPacket[] { new BuddyEmoteRefuse(4001, 2) }));
        h = new Host { HasPlayer = false };
        Handle(h, new BuddyEmoteMessage.Invite((int)Emo, 4001, "Asker"));
        Assert.That(h.Sent, Is.Empty, "no player: nothing (assert return)");
    }

    [Test]
    public void InviteSentNamesTheTarget()
    {
        var h = new Host();
        Handle(h, new BuddyEmoteMessage.InviteSent(4001));
        Handle(h, new BuddyEmoteMessage.InviteSent(4009));
        Assert.That(h.Notices, Is.EqualTo(new[] { (0xce6, "Asker") }));
    }

    [Test]
    public void FailedShowsTheReasonsNoticeWithThePartnerThenIdles()
    {
        var h = new Host().Requesting();
        Handle(h, new BuddyEmoteMessage.Failed(4));
        Assert.That(h.Notices, Is.EqualTo(new[] { (0xcf3, "Asker") }));
        Assert.That(h.Calls, Is.EqualTo(new[] { "idle" }));
        h = new Host();
        Handle(h, new BuddyEmoteMessage.Failed(4));
        Assert.That(h.Notices, Is.Empty, "not in 0x3a: nothing");
        Assert.That(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }.Select(CoupleEmotionInbound.FailNoticeId),
            Is.EqualTo(new[] { 0xcf2, 0xcf4, 0xcf5, 0xcf6, 0xcf3, 0xcf7, 0xcf8, 0xcea, 0xcf9, 0xcf0, 0xce7 }));
    }

    [Test]
    public void AcceptedAndDeclinedMatchTheOwnRequest()
    {
        var h = new Host().Requesting();
        Handle(h, new BuddyEmoteMessage.Accepted((int)Emo + 1, 4001));
        Assert.That(h.Notices, Is.Empty, "another emotion");
        Handle(h, new BuddyEmoteMessage.Accepted((int)Emo, 4001));
        Assert.That(h.Notices, Is.EqualTo(new[] { (0xce8, (string)null) }));
        h.Notices.Clear();
        Handle(h, new BuddyEmoteMessage.Declined((int)Emo, 4001));
        Assert.That(h.Calls, Is.EqualTo(new[] { "idle" }));
        Assert.That(h.Notices, Is.EqualTo(new[] { (0xce9, "Asker") }));
        // the accepter's own 0x94 is not a request
        h = new Host();
        h.EnterCouple(new CoupleEmotionStart(Emo, 77, 77, default, default), CoupleEmotionState.Accepting);
        h.Calls.Clear();
        Handle(h, new BuddyEmoteMessage.Declined((int)Emo, 4001));
        Handle(h, new BuddyEmoteMessage.Accepted((int)Emo, 4001));
        Assert.That(h.Calls, Is.Empty);
        Assert.That(h.Notices, Is.Empty);
    }

    [Test]
    public void StartTakesTheServersPointAndAnglesThenSubNinetyFive()
    {
        var h = new Host().Requesting();
        Handle(h, new BuddyEmoteMessage.Start((int)Emo, 4002, 4001, new Vector3(7, 8, 9), new Vector3(0, 0, 120), 5555));
        Assert.That(h.Calls, Is.EqualTo(new[] { "tick 5555", "sub 95" }));
        Assert.That((h.Own.PosStart, h.Own.PosEnd), Is.EqualTo((new Vector3(7, 8, 9), new Vector3(0, 0, 120))));
        // the first id is the partner when it is not this character
        h = new Host().Requesting();
        Handle(h, new BuddyEmoteMessage.Start((int)Emo, 4001, 4002, default, default, 1));
        Assert.That(h.Own.Sub, Is.EqualTo(0x95));
        h = new Host().Requesting();
        Handle(h, new BuddyEmoteMessage.Start((int)Emo, 4002, 4003, default, default, 1));
        Assert.That(h.Own.Sub, Is.EqualTo(0x93), "a partner not in the field");
    }

    [Test]
    public void LeftIdlesOnlyForTheOwnPair()
    {
        var h = new Host().Requesting();
        Handle(h, new BuddyEmoteMessage.Left((int)Emo + 1, 4001));
        Assert.That(h.Calls, Is.Empty);
        Handle(h, new BuddyEmoteMessage.Left((int)Emo, 4001));
        Assert.That(h.Calls, Is.EqualTo(new[] { "idle" }));
        Assert.That(h.Sent, Is.Empty);
    }

    [Test]
    public void TheBoxAnswer()
    {
        var h = new Host();
        CoupleEmotionInbound.Answer(1, (int)Emo, 4001, h, h.Own);
        Assert.That(h.Sent, Is.EqualTo(new IOutPacket[] { new BuddyEmoteDecline((int)Emo, 4001) }));
        h = new Host { Riding = true };
        CoupleEmotionInbound.Answer(0, (int)Emo, 4001, h, h.Own);
        Assert.That(h.Calls, Is.EqualTo(new[] { $"rideoff {Emo} 4001" }));
        Assert.That(h.Sent, Is.Empty);
        h = new Host();
        CoupleEmotionInbound.Answer(0, (int)Emo, 4001, h, h.Own);
        Assert.That(h.Calls, Is.EqualTo(new[] { "couple 94", "place" }));
        Assert.That(h.Sent, Is.EqualTo(new IOutPacket[] { new BuddyEmoteAccept((int)Emo, 4001, new Vector3(1, 2, 3), new Vector3(4, 5, 6), 30f) }));
        Assert.That(h.Own.PartnerId == 77 && h.Own.RequesterId == 77, Is.True, "both the asker's object id");
        Assert.That(h.Own.PosEnd, Is.EqualTo(new Vector3(0, 0, 45)), "the own angles");
    }

    [Test]
    public void TheAcceptsRefusals()
    {
        var h = new Host();
        h.Peers.Clear();
        Assert.That(CoupleEmotionInbound.Request(4001, (int)Emo, h, h.Own), Is.False);
        Assert.That(h.Notices, Is.EqualTo(new[] { (0xcf1, (string)null) }));
        Assert.That(h.Sent, Is.Empty);

        h = new Host { Position = new Vector3(401, 0, 0) };
        CoupleEmotionInbound.Request(4001, (int)Emo, h, h.Own);
        Assert.That(h.Sent, Is.EqualTo(new IOutPacket[] { new BuddyEmoteRefuse(4001, 8) }));
        Assert.That(h.Notices, Is.EqualTo(new[] { (0xcee, "Asker") }));

        h = new Host { StateId = 0xb };
        CoupleEmotionInbound.Request(4001, (int)Emo, h, h.Own);
        Assert.That(h.Sent, Is.EqualTo(new IOutPacket[] { new BuddyEmoteRefuse(4001, 5) }));
        Assert.That(h.Notices, Is.EqualTo(new[] { (0xcfa, (string)null) }));

        h = new Host { Placement = false };
        CoupleEmotionInbound.Request(4001, (int)Emo, h, h.Own);
        Assert.That(h.Sent, Is.EqualTo(new IOutPacket[] { new BuddyEmoteRefuse(4001, 7) }));
        Assert.That(h.Notices, Is.EqualTo(new[] { (0xcea, "Me") }));
        Assert.That(h.Calls, Is.EqualTo(new[] { "couple 94", "place", "idle" }));
    }

    [Test]
    public void StopIdlesThenTellsThePartner()
    {
        var h = new Host().Requesting();
        CoupleEmotionInbound.Stop(true, h, h.Own);
        Assert.That(h.Calls, Is.EqualTo(new[] { "idle" }));
        Assert.That(h.Sent, Is.EqualTo(new IOutPacket[] { new BuddyEmoteCancel((int)Emo, 4001) }));
        Assert.That(h.Notices, Is.EqualTo(new[] { (0xcee, "Asker") }));
        h = new Host().Requesting();
        h.Peers.Clear();
        CoupleEmotionInbound.Stop(false, h, h.Own);
        Assert.That(h.Sent, Is.Empty, "no partner: no send");
    }

    [Test]
    public void TheSyncTailIsTheClientsOrder()
    {
        var s = new CoupleEmotionState();
        s.Enter(new CoupleEmotionStart(Emo, 77, 55, new Vector3(1, 2, 3), new Vector3(0, 0, 90)), 0x96);
        s.Ani = 2;
        var w = new ByteWriter();
        w.WriteInt(55); w.WriteInt(77); w.WriteByte(2);
        w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(90);
        w.WriteFloat(1); w.WriteFloat(2); w.WriteFloat(3);
        Assert.That(s.SyncTail(), Is.EqualTo(w.Buffer.AsSpan(0, w.Length).ToArray()));
        var back = CoupleEmotionState.ReadTail(Emo, s.SyncTail(), out int ani);
        Assert.That(back, Is.EqualTo(new CoupleEmotionStart(Emo, 77, 55, new Vector3(1, 2, 3), new Vector3(0, 0, 90))));
        Assert.That(ani, Is.EqualTo(2));
        Assert.That(CoupleEmotionState.ReadTail(Emo, new byte[5], out _), Is.Null);
    }
}
