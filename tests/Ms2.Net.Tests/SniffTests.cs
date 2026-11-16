using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Decodes recorded KMS2 traffic (MapleShark2 .msb files). The files are local only: set MS2_SNIFFS to a folder of them
/// (never copy them into the repo); the tests are skipped when it is unset. Builds from 2520 on are checked, the client in
/// the restored tree is 2525; older builds are known to differ in the user sync record and in FieldAddUser.
/// </summary>
public class SniffTests {
    const uint MinBuild = 2520;
    static List<MsbFile> files;

    static List<MsbFile> Files() {
        string dir = Environment.GetEnvironmentVariable("MS2_SNIFFS");
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore("MS2_SNIFFS not set");
        return files ??= Directory.GetFiles(dir, "*.msb", SearchOption.AllDirectories).OrderBy(p => p)
            .Select(MsbFile.Read).Where(f => f.Build >= MinBuild).ToList();
    }

    static IEnumerable<(MsbFile f, MsbPacket p)> Packets(bool s2c, ushort op) =>
        Files().SelectMany(f => f.Packets.Where(p => p.ServerToClient == s2c && p.Opcode == op).Select(p => (f, p)));

    static int Decode(bool s2c, ushort op, Func<ByteReader, object> read, Func<MsbPacket, bool> filter = null, uint maxBuild = uint.MaxValue) {
        int n = 0;
        foreach (var (f, p) in Packets(s2c, op)) {
            if (f.Build > maxBuild) continue;
            if (filter != null && !filter(p)) continue;
            try { read(new ByteReader(p.Data)); n++; }
            catch (Exception e) { Assert.Fail($"{Path.GetFileName(f.Path)} op {op:X2} build {f.Build}: {e.GetType().Name} {e.Message[..Math.Min(80, e.Message.Length)]}"); }
        }
        TestContext.Out.WriteLine($"{(s2c ? "in " : "out")} 0x{op:X2}: {n} decoded");
        return n;
    }

    [Test] public void LoginAndMigration() {
        Decode(false, 0x01, r => ResponseVersion.Read(r));
        Decode(false, 0x03, r => { var c = r.Buffer.ToArray(); try { return ResponseLogin.Read(new ByteReader(c)); } catch (Exception) { return ResponseLoginB.Read(new ByteReader(c)); } });
        Decode(false, 0x04, r => ResponseKey.Read(r));
        Decode(true, 0x0A, r => LoginResult.Read(r));
        Decode(true, 0x0D, r => MoveResult.Read(r));
        Decode(true, 0x0E, r => LoginToGame.Read(r));
    }

    [Test] public void Keepalive() {
        Decode(true, 0x12, r => TickRequest.Read(r));
        Decode(false, 0x0A, r => ResponseHeartbeat.Read(r), p => p.Data.Length == 8);
        Decode(true, 0x13, r => TickRequest.Read(r));
        Decode(false, 0x0C, r => ResponseClientTickSync.Read(r));
    }

    [Test] public void KeepaliveAnswersEchoTheRequest() {
        int pairs = 0;
        foreach (var f in Files()) {
            int? pendingBeat = null, pendingSync = null;
            foreach (var p in f.Packets) {
                if (p.ServerToClient && p.Opcode == 0x12) pendingBeat = TickRequest.Read(new ByteReader(p.Data)).Tick;
                else if (p.ServerToClient && p.Opcode == 0x13) pendingSync = TickRequest.Read(new ByteReader(p.Data)).Tick;
                else if (!p.ServerToClient && p.Opcode == 0x0A && p.Data.Length == 8 && pendingBeat != null) { Assert.That(ResponseHeartbeat.Read(new ByteReader(p.Data)).ServerTick, Is.EqualTo(pendingBeat)); pendingBeat = null; pairs++; }
                else if (!p.ServerToClient && p.Opcode == 0x0C && pendingSync != null) { Assert.That(ResponseClientTickSync.Read(new ByteReader(p.Data)).ServerTick, Is.EqualTo(pendingSync)); pendingSync = null; pairs++; }
            }
        }
        Assert.That(pairs, Is.GreaterThan(100));
    }

    [Test] public void PongTracksReceivedFrames() {
        // The answer to a Ping is the client's count of received frames (CGameSocket m_sequence, which counts the handshake).
        // Recordings that begin with the handshake: the value trails the sniffer's own count by a small, mostly constant amount
        // (3 in most recorded answers; the sniffer sees frames before the client has processed them, and I could not account for
        // the constant part). So the check is: never ahead, and within 30 for nearly every answer.
        int pongs = 0, near = 0, three = 0, files = 0;
        foreach (var f in Files()) {
            if (f.Packets.Count == 0 || !f.Packets[0].ServerToClient || f.Packets[0].Opcode != 1 || f.Packets[0].Data.Length < 25) continue;
            files++;
            int received = 0; bool pingSeen = false;
            foreach (var p in f.Packets) {
                if (p.ServerToClient) { received++; if (p.Opcode == 6) pingSeen = true; }
                else if (p.Opcode == 6 && pingSeen) {
                    int diff = received - Pong.Read(new ByteReader(p.Data)).ReceivedCount;
                    pongs++; if (diff >= 0 && diff <= 30) near++; if (diff == 3) three++;
                }
            }
        }
        TestContext.Out.WriteLine($"{pongs} pongs in {files} recordings: {near} within 0..30 of the sniffer's count, {three} exactly 3 behind");
        Assert.That(pongs, Is.GreaterThan(100));
        Assert.That(near, Is.GreaterThan(pongs * 9 / 10));
    }

    [Test] public void ServerListAndCharacterManagement() {
        int n = Decode(true, 0x0B, r => ServerList.Read(r));
        Assert.That(n, Is.GreaterThan(0));
        foreach (var (_, p) in Packets(true, 0x0B)) { var l = ServerList.Read(new ByteReader(p.Data)); Assert.That(l.Servers, Is.Not.Empty); Assert.That(l.Servers[0].Channels, Is.Not.Empty); }
        Decode(false, 0x14, r => { r.ExpectEnd("RequestServerList"); return null; });
        int sel = 0, other = 0;
        foreach (var (f, p) in Packets(false, 0x09)) {
            var r = new ByteReader(p.Data);
            try {
                switch (p.Data[0]) {
                    case 0: SelectCharacter.Read(r); break;
                    case 1: { var c = CreateCharacter.Read(r); Assert.That(c.Name, Is.Not.Empty); break; }
                    case 2: case 3: case 4: ManageCharacter.Read(r); break;
                    case 5: RenameCharacter.Read(r); break;
                    default: Assert.Fail($"command {p.Data[0]}"); break;
                }
                if (p.Data[0] == 0) sel++; else other++;
            } catch (Exception e) when (e is not AssertionException) { Assert.Fail($"{Path.GetFileName(f.Path)} cmd {p.Data[0]}: {e.Message[..Math.Min(70, e.Message.Length)]}"); }
        }
        TestContext.Out.WriteLine($"character management: {sel} select, {other} other");
        Assert.That(sel, Is.GreaterThan(2));
    }

    [Test] public void StateJump() => Assert.That(Decode(false, 0x80, r => StateChange.Read(r)), Is.GreaterThan(100));

    [Test] public void OtherPlayers() {
        Decode(true, 0x36, r => UserState.Read(r));
        Decode(true, 0x2F, r => UserBattle.Read(r));
        Decode(true, 0x65, r => StateFallDamage.Read(r));
        Decode(true, 0x3C, r => SkillUse.Read(r));
        Decode(true, 0x3E, r => SkillSync.Read(r));
        Decode(true, 0x3F, r => SkillCancel.Read(r));
        Decode(true, 0x41, r => StateSkill.Read(r));
        Decode(true, 0x25, r => ItemPutOn.Read(r));
        Decode(true, 0x26, r => ItemPutOff.Read(r));
        Decode(true, 0x47, r => BuffMessage.Read(r));
        Decode(true, 0x1D, r => ChatMessage.Read(r), maxBuild: 2540); // build 2546 appends a field
        int rides = Decode(true, 0x7A, r => UserSyncIn.Read(r));
        Assert.That(rides, Is.GreaterThan(100));
    }

    [Test] public void CharacterList() {
        int n = Decode(true, 0x0C, r => CharacterListMessage.Read(r));
        Assert.That(n, Is.GreaterThan(10));
        // spot check: decoded entries carry a name and equips
        int entries = 0;
        foreach (var (_, p) in Packets(true, 0x0C)) {
            var m = CharacterListMessage.Read(new ByteReader(p.Data));
            foreach (var e in m.Entries) { entries++; Assert.That(e.User.Name, Is.Not.Empty); Assert.That(e.User.CharacterId, Is.Not.Zero); Assert.That(e.Equips, Is.Not.Empty); }
        }
        Assert.That(entries, Is.GreaterThan(10));
    }

    [Test] public void EnterField() {
        Decode(true, 0x15, r => ServerEnter.Read(r));
        Decode(true, 0x16, r => RequestFieldEnter.Read(r));
        Decode(false, 0x10, r => ResponseFieldEnter.Read(r));
        Decode(true, 0x14, r => SyncNumber.Read(r));
        Decode(true, 0x18, r => FieldRemoveUser.Read(r));
        Decode(true, 0x7E, r => ProxyGameObj.Read(r));
        Decode(true, 0x7E, r => { var p = ProxyGameObj.Read(r); Assert.That(p.Name, Is.Not.Empty); return p; }, p => p.Data[0] == 3);
    }

    [Test] public void FieldAddUserDecodes() {
        int ok = 0, unsupported = 0;
        foreach (var (f, p) in Packets(true, 0x17)) {
            try {
                var u = FieldAddUser.Read(new ByteReader(p.Data));
                Assert.That(u.User.Name, Is.Not.Empty); Assert.That(u.ObjectId, Is.Not.Zero);
                ok++;
            } catch (PacketLayoutException e) when (e.Message.Contains("not decoded")) { unsupported++; }
            catch (Exception e) { Assert.Fail($"{Path.GetFileName(f.Path)} build {f.Build}: {e.GetType().Name} {e.Message[..Math.Min(80, e.Message.Length)]}"); }
        }
        TestContext.Out.WriteLine($"FieldAddUser: {ok} decoded, {unsupported} with a riding state or held ugc cube (not decoded)");
        Assert.That(ok, Is.GreaterThan(100));
        int rides = 0, buffs = 0, pets = 0;
        foreach (var (_, p) in Packets(true, 0x17)) { var u = FieldAddUser.Read(new ByteReader(p.Data)); if (u.Ride != null) rides++; buffs += u.Buffs.Count; if (u.Pet != null) pets++; }
        TestContext.Out.WriteLine($"FieldAddUser: {rides} riding, {buffs} buffs, {pets} with a pet item");
        Assert.That(unsupported, Is.EqualTo(0));
    }

    [Test] public void UserSyncDecodesAndRoundTrips() {
        int n = 0;
        foreach (var (f, p) in Packets(false, 0x12)) {
            var o = UserSyncOut.Read(new ByteReader(p.Data));
            var w = new ByteWriter(); o.Write(w);
            Assert.That(w.Buffer.AsSpan(0, w.Length).ToArray(), Is.EqualTo(p.Data), $"{Path.GetFileName(f.Path)} out");
            n++;
        }
        Assert.That(n, Is.GreaterThan(1000));
        int m = 0;
        foreach (var (f, p) in Packets(true, 0x1C)) {
            var i = UserSyncIn.Read(new ByteReader(p.Data));
            var w = new ByteWriter(); w.WriteInt(i.ObjectId); w.WriteByte((byte) i.Records.Count);
            foreach (var r in i.Records) r.Write(w);
            Assert.That(w.Buffer.AsSpan(0, w.Length).ToArray(), Is.EqualTo(p.Data), $"{Path.GetFileName(f.Path)} in");
            m++;
        }
        Assert.That(m, Is.GreaterThan(1000));
        TestContext.Out.WriteLine($"user sync: {n} outbound, {m} inbound, all re-encode to the recorded bytes");
    }

    [Test] public void HandshakeFrameInRecordings() {
        // MapleShark keeps the first frame with its 6 byte header: seqbase = version ^ (siv >> 16) must hold
        int n = 0;
        foreach (var (f, p) in Packets(true, 0x01)) {
            if (p.Data.Length < 25 || BitConverter.ToUInt16(p.Data, 6) != 1) continue;
            Assert.That(Handshake.TryParse(p.Data, out var hs, out _), Is.True);
            Assert.That((ushort) (BitConverter.ToUInt16(p.Data, 0) ^ (hs.ServerSendIv >> 16)), Is.EqualTo((ushort) hs.Version), Path.GetFileName(f.Path));
            n++;
        }
        Assert.That(n, Is.GreaterThan(10));
    }
}
