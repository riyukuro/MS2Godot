using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The guild mode map (GuildModes): every guild send writes the GMS2 client's mode on a GMS2 session and the KMS mode on
/// a KMS session, and the guild reader takes each GMS2 type as its KMS type.</summary>
public class GuildModesTests {
    static readonly ProtocolProfile Kms = ProtocolProfile.Kms2525, Gms = ProtocolProfile.Gms2;

    // As the channel writes a packet (MapleChannel.SendAsync): a profile packet gets the session's profile.
    static byte[] Wire(IOutPacket p, ProtocolProfile profile) {
        var w = new ByteWriter();
        if (p is IProfileOutPacket pp) pp.Write(w, profile); else p.Write(w);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }

    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    static readonly GuildInviteInfo Invite = new() { GuildId = 5, GuildName = "G", Mark = "m", Inviter = "a", Invitee = "b" };

    // Every guild send the client has a record for, with its KMS mode and the mode the GMS2 client writes (from the GMS2
    // client's senders, independent of the map).
    static readonly (IOutPacket Packet, byte Kms, byte Gms2)[] Sends = {
        (new GuildCreateRequest("G"), 1, 1),
        (new GuildBreakRequest(), 2, 2),
        (new GuildInviteRequest("a"), 3, 3),
        (new GuildInviteAnswerRequest(Invite, true), 5, 5),
        (new GuildLeaveRequest(), 7, 7),
        (new GuildExpelRequest("a"), 8, 8),
        (new GuildMemberGradeRequest("a", 2), 10, 10),
        (new GuildMemoRequest("t"), 0xd, 13),
        (new GuildAttendRequest(), 0xf, 15),
        (new GuildChangeMasterRequest("a"), 0x3f, 61),
        (new GuildTextRequest(1, "t"), 0x40, 62),
        (new GuildGradeRequest(1, new GuildGrade(1, "g", 3)), 0x43, 65),
        (new GuildPropensityRequest(1, 2), 0x44, 66),
        (new GuildPvpHistoryRequest(5), 0x4a, 72),
        (new GuildPvpSeasonHistoryRequest(), 0x4b, 73),
        (new GuildJoinRequestSend(5), 0x52, 80),
        (new GuildJoinCancelRequest(5), 0x53, 81),
        (new GuildRespondRequest(5, true), 0x54, 82),
        (new GuildListRequestsRequest(), 0x55, 83),
        (new GuildMyRequestsRequest(), 0x56, 84),
        (new GuildSearchRequest(1), 0x57, 85),
        (new GuildSearchByNameRequest("G"), 0x58, 86),
        (new GuildSkillUseRequest(1), 0x5a, 88),
        (new GuildPersonalSkillUseRequest(1), 0x5b, 89),
        (new GuildSkillUpgradeRequest(1), 0x5c, 90),
        (new GuildStorageGiftRequest(new GuildStorageItem(), "a"), 0x6d, 106),
        (new GuildStorageHistoryRequest(), 0x70, 109),
        (new GuildDonateRequest(1), 0x71, 110),
        (new GuildVsGameRegisterRequest(), 0x73, 112),
        (new GuildVsGameUnregisterRequest(), 0x74, 113),
    };

    [Test]
    public void EveryGuildSendWritesItsRegionsMode() {
        int checkedSends = 0;
        foreach (var (p, kms, gms2) in Sends) {
            string name = p.GetType().Name;
            Assert.That(p.Op, Is.EqualTo(Outbound.Guild), name);
            byte[] k = Wire(p, Kms), g = Wire(p, Gms);
            Assert.That(k[0], Is.EqualTo(kms), name + " on KMS");
            Assert.That(g[0], Is.EqualTo(gms2), name + " on GMS2");
            // Only the mode differs: the body is the same on both regions, and the plain write is the KMS one.
            Assert.That(g.AsSpan(1).ToArray(), Is.EqualTo(k.AsSpan(1).ToArray()), name + " body");
            var w = new ByteWriter(); p.Write(w);
            Assert.That(w.Buffer.AsSpan(0, w.Length).ToArray(), Is.EqualTo(k), name + " plain write");
            checkedSends++;
        }
        Assert.That(checkedSends, Is.EqualTo(30));
    }

    [Test]
    public void EverySendRecordOfTheAssemblyIsInTheList() {
        // A new guild send record has to join the list above (and so the map).
        var listed = Sends.Select(s => s.Packet.GetType()).ToHashSet();
        var records = typeof(GuildCreateRequest).Assembly.GetTypes()
            .Where(t => typeof(IOutPacket).IsAssignableFrom(t) && t.Namespace == "Ms2.Net.Packets" && t.Name.StartsWith("Guild")).ToList();
        Assert.That(records, Is.Not.Empty);
        foreach (var t in records) Assert.That(listed, Does.Contain(t), t.Name);
    }

    [Test]
    public void OpeningTheWindowOutsideAGuildSendsMode84OnGms2() {
        Assert.That(Wire(new GuildMyRequestsRequest(), Gms), Is.EqualTo(new byte[] { 84 }));
        Assert.That(Wire(new GuildMyRequestsRequest(), Kms), Is.EqualTo(new byte[] { 0x56 }));
    }

    [Test]
    public void TheSendMapCoversEveryKmsSender() {
        // 39 KMS guild modes (guildmanager.cpp), each with its own GMS2 mode.
        Assert.That(GuildModes.Gms2Send, Has.Count.EqualTo(39));
        Assert.That(GuildModes.Gms2Send.Values.Distinct().Count(), Is.EqualTo(39));
        foreach (var (p, kms, gms2) in Sends) Assert.That(GuildModes.Gms2Send[kms], Is.EqualTo(gms2), p.GetType().Name);
        foreach (var (kms, gms2) in GuildModes.Gms2Send) {
            Assert.That(GuildModes.Send(kms, Kms), Is.EqualTo(kms));
            Assert.That(GuildModes.Send(kms, Gms), Is.EqualTo(gms2));
        }
        // GMS2 64 is the GMS2 client's own send (no KMS sender).
        Assert.That(GuildModes.Gms2Send.Values, Does.Not.Contain((byte) 64));
    }

    [Test]
    public void AnUnmappedSendModeNamesTheModeOnGms2() {
        // 0x42 is a KMS receive type with no KMS sender: no GMS2 row. KMS passes every byte through.
        Assert.That(GuildModes.Gms2Send.ContainsKey(0x42), Is.False);
        var e = Assert.Throws<InvalidOperationException>(() => GuildModes.Send(0x42, Gms));
        Assert.That(e.Message, Does.Contain("0x42"));
        Assert.That(GuildModes.Send(0x42, Kms), Is.EqualTo((byte) 0x42));
    }

    [Test]
    public void TheReceiveMapIsOneToOneAndKmsIsUnchanged() {
        // 91 GMS2 cases, 90 with a KMS case (GMS2 50 has none).
        Assert.That(GuildModes.Gms2Receive, Has.Count.EqualTo(90));
        Assert.That(GuildModes.Gms2Receive.Values.Distinct().Count(), Is.EqualTo(90));
        Assert.That(GuildModes.Gms2Receive.ContainsKey(50), Is.False);
        Assert.That(GuildModes.Gms2Receive.Values, Does.Not.Contain(GuildModes.NoKmsType));
        for (int t = 0; t < 256; t++) {
            Assert.That(GuildModes.Receive((byte) t, Kms), Is.EqualTo((byte) t));
            byte g = GuildModes.Receive((byte) t, Gms);
            Assert.That(g, Is.EqualTo(GuildModes.Gms2Receive.TryGetValue((byte) t, out byte k) ? k : GuildModes.NoKmsType));
        }
        // The modes both maps share agree: a send and its answer keep their pairing.
        foreach (byte kms in new byte[] { 1, 2, 3, 5, 7, 8, 0xf, 0x3f, 0x40, 0x41, 0x43, 0x44, 0x47, 0x4a, 0x4b, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57, 0x5a, 0x5b, 0x6d, 0x70, 0x71 })
            Assert.That(GuildModes.Gms2Receive[GuildModes.Gms2Send[kms]], Is.EqualTo(kms), $"0x{kms:x}");
    }

    // The GMS2 server's guild writers (GuildPacket) the reader decodes, as GMS2 sends them, and the KMS type each reads as.
    static readonly (byte Gms2, Action<ByteWriter> Body, byte Kms)[] Receives = {
        (1, w => { w.WriteByte(0); w.WriteUnicodeString("G"); }, 1),
        (2, w => w.WriteByte(0), 2),
        (3, w => w.WriteUnicodeString("a"), 3),
        (4, w => Invite.Write(w), 4),
        (5, w => { Invite.Write(w); w.WriteBool(true); }, 5),
        (6, w => { w.WriteUnicodeString("a"); w.WriteBool(true); w.WriteByte(0); }, 6),
        (7, w => { }, 7),
        (14, w => { w.WriteUnicodeString("a"); w.WriteInt(3); }, 0xe),
        (15, w => { }, 0xf),
        (19, w => w.WriteUnicodeString("a"), 0x13),
        (24, w => { w.WriteUnicodeString("a"); w.WriteLong(9); }, 0x18),
        (31, w => { w.WriteUnicodeString("a"); w.WriteInt(2000001); }, 0x1f),
        (35, w => { w.WriteUnicodeString("a"); w.WriteInt(1); w.WriteInt(2); w.WriteShort(2); }, 0x23),
        (45, w => { w.WriteLong(1); w.WriteLong(2); w.WriteLong(3); w.WriteLong(4); w.WriteUnicodeString("a"); w.WriteUnicodeString("p");
                    w.WriteInt(10); w.WriteInt(20); w.WriteInt(30); w.WriteInt(1); w.WriteInt(2); w.WriteInt(3); w.WriteLong(9); }, 0x2d),
        (46, w => w.WriteLong(5), 0x2e),
        (47, w => { w.WriteUnicodeString("a"); w.WriteUnicodeString("b"); w.WriteBool(true); w.WriteLong(5); }, 0x2f),
        (48, w => { w.WriteUnicodeString("G"); w.WriteLong(5); w.WriteBool(true); }, 0x30),
        (55, w => { w.WriteUnicodeString("a"); w.WriteInt(2); w.WriteInt(1); }, 0x39),
        (56, w => { w.WriteLong(5); w.WriteUnicodeString("a"); w.WriteInt(1); w.WriteUnicodeString("p"); }, 0x3a),
        (57, w => { w.WriteUnicodeString("a"); w.WriteInt(1); w.WriteInt(2); }, 0x3b),
        (63, w => w.WriteUnicodeString("m"), 0x41),
        (66, w => { w.WriteBool(true); w.WriteInt(4); }, 0x44),
        (80, w => { w.WriteLong(5); w.WriteUnicodeString("G"); }, 0x52),
        (81, w => { w.WriteLong(5); w.WriteUnicodeString("G"); }, 0x53),
        (82, w => { w.WriteLong(5); w.WriteUnicodeString("G"); w.WriteBool(true); }, 0x54),
        (83, w => w.WriteInt(0), 0x55),
        (84, w => w.WriteInt(0), 0x56),
        (85, w => w.WriteInt(0), 0x57),
        (110, w => { w.WriteInt(2); w.WriteLong(9); }, 0x71),
    };

    [Test]
    public void EveryGms2GuildWriterReadsAsItsKmsType() {
        foreach (var (gms2, body, kms) in Receives) {
            var r = R(w => { w.WriteByte(gms2); body(w); });
            GuildMessage m = null;
            Assert.DoesNotThrow(() => m = GuildMessage.Read(r, Gms), $"GMS2 {gms2}");
            Assert.That(m.Type, Is.EqualTo(kms), $"GMS2 {gms2}");
            Assert.That(r.Available, Is.EqualTo(0), $"GMS2 {gms2} read whole");
        }
        Assert.That(Receives.Length, Is.EqualTo(29));
    }

    [Test]
    public void Gms2GuildWritersCarryTheirFields() {
        var m = GuildMessage.Read(R(w => { w.WriteByte(110); w.WriteInt(2); w.WriteLong(9); }), Gms);
        Assert.That((m.DonationCount, m.DonationTime), Is.EqualTo((2, 9L)));
        m = GuildMessage.Read(R(w => { w.WriteByte(82); w.WriteLong(5); w.WriteUnicodeString("G"); w.WriteBool(true); }), Gms);
        Assert.That((m.GuildId, m.Applicant, m.Accepted), Is.EqualTo((5L, "G", true)));
        m = GuildMessage.Read(R(w => { w.WriteByte(66); w.WriteBool(true); w.WriteInt(4); }), Gms);
        Assert.That((m.Focus, m.Value), Is.EqualTo((1, 4)));
        m = GuildMessage.Read(R(w => {
            w.WriteByte(45); w.WriteLong(1); w.WriteLong(2); w.WriteLong(3); w.WriteLong(4); w.WriteUnicodeString("a"); w.WriteUnicodeString("p");
            w.WriteInt(10); w.WriteInt(20); w.WriteInt(30); w.WriteInt(1); w.WriteInt(2); w.WriteInt(3); w.WriteLong(9);
        }), Gms);
        Assert.That((m.NewRequest.Id, m.NewRequest.CharId, m.NewRequest.Job, m.NewRequest.JobCode, m.NewRequest.Time), Is.EqualTo((1L, 3L, 10, 20, 9L)));
    }

    [Test]
    public void AGms2TypeWithoutAKmsCaseIsNotTakenAsAKmsType() {
        // GMS2 50 (the funds) has no KMS case; on a KMS session 0x32 stays 0x32.
        var m = GuildMessage.Read(R(w => { w.WriteByte(50); w.WriteInt(7); }), Gms);
        Assert.That(m.Type, Is.EqualTo(GuildModes.NoKmsType));
        Assert.That(GuildMessage.Read(R(w => w.WriteByte(0x32)), Kms).Type, Is.EqualTo((byte) 0x32));
    }

    [Test]
    public void EveryRecordedGms2GuildPacketHasAKmsTypeAndDecodes() {
        var all = Gms2Sniffs.Inbound("gms2", Gms.Number(Inbound.Guild));
        Assert.That(all, Is.Not.Empty);
        var types = new SortedSet<byte>();
        foreach (var (file, data) in all) {
            if (data.Length == 0) continue;
            types.Add(data[0]);
            // GMS2 50 (the funds alone) is the one GMS2 type with no KMS case; it reads as NoKmsType and changes nothing.
            if (data[0] == 50) Assert.That(GuildMessage.Read(new ByteReader(data), Gms).Type, Is.EqualTo(GuildModes.NoKmsType), file);
            else Assert.That(GuildModes.Gms2Receive.ContainsKey(data[0]), Is.True, $"{file}: GMS2 type {data[0]}");
            var r = new ByteReader(data);
            Assert.DoesNotThrow(() => GuildMessage.Read(r, Gms), file);
        }
        TestContext.Out.WriteLine($"recorded GMS2 guild types: {string.Join(",", types)} in {all.Count()} packets");
    }
}
