using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// The Party, Guild, UserEnv, LevelUp, Buddy and ProxyGameObj readers take the whole packet (ExpectEnd) on real KMS2 traffic.
/// The recordings need MS2_SNIFFS; the byte-level cases below run without data.
/// </summary>
public class KmsReaderTests {
    static ByteReader Reader(Action<ByteWriter> body) { var w = new ByteWriter(); body(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    static IEnumerable<TestCaseData> Readers() {
        yield return new TestCaseData((ushort) Inbound.Party, (Action<ByteReader>) (r => PartyMessage.Read(r))).SetName("Party");
        yield return new TestCaseData((ushort) Inbound.Guild, (Action<ByteReader>) (r => GuildMessage.Read(r))).SetName("Guild");
        yield return new TestCaseData((ushort) Inbound.UserEnv, (Action<ByteReader>) (r => UserEnvMessage.Read(r))).SetName("UserEnv");
        yield return new TestCaseData((ushort) Inbound.LevelUp, (Action<ByteReader>) (r => LevelUp.Read(r))).SetName("LevelUp");
        yield return new TestCaseData((ushort) Inbound.Buddy, (Action<ByteReader>) (r => BuddyMessage.Read(r))).SetName("Buddy");
        yield return new TestCaseData((ushort) Inbound.ProxyGameObj, (Action<ByteReader>) (r => ProxyGameObj.Read(r))).SetName("ProxyGameObj");
    }

    [TestCaseSource(nameof(Readers))]
    public void SniffPacketsEndExactly(ushort opcode, Action<ByteReader> read) {
        string dir = Environment.GetEnvironmentVariable("MS2_SNIFFS");
        if (string.IsNullOrEmpty(dir)) Assert.Ignore("MS2_SNIFFS needed");
        int n = 0; var fail = new List<string>();
        foreach (var path in Directory.GetFiles(dir, "*.msb", SearchOption.AllDirectories)) {
            var f = MsbFile.Read(path);
            if (f.Build < 2520) continue;
            foreach (var p in f.Packets) {
                if (!p.ServerToClient || p.Opcode != opcode) continue;
                n++;
                try {
                    var r = new ByteReader(p.Data); read(r);
                    if (r.Available != 0 && fail.Count < 8) fail.Add($"{Path.GetFileName(path)} mode {(p.Data.Length > 0 ? p.Data[0] : -1):x2}: {r.Available} bytes left");
                } catch (Exception e) when (e is not AssertionException) {
                    if (fail.Count < 8) fail.Add($"{Path.GetFileName(path)} mode {(p.Data.Length > 0 ? p.Data[0] : -1):x2}: {e.GetType().Name} {e.Message}");
                }
            }
        }
        Assert.That(fail, Is.Empty, $"{n} packets");
    }

    [Test] public void LevelUpWithI32Level() {
        var r = Reader(w => { w.WriteInt(7); w.WriteInt(55); });
        Assert.That(LevelUp.Read(r), Is.EqualTo(new LevelUp(7, 55)));
        Assert.That(r.Available, Is.Zero);
    }

    [Test] public void ProxyGameObjCommands() {
        var r = Reader(w => { w.WriteByte(6); w.WriteInt(1); w.WriteInt(2); w.WriteByte(3); w.WriteInt(4); w.WriteFloat(1); w.WriteFloat(2); w.WriteFloat(3); });
        var p = ProxyGameObj.Read(r);
        Assert.That((p.ObjectId, p.TemplateId, p.Kind, p.Extra, p.Position), Is.EqualTo((1, 2, (byte) 3, 4, new Vector3(1, 2, 3))));
        r = Reader(w => { w.WriteByte(9); w.WriteInt(1); w.WriteInt(4); w.WriteInt(2); w.WriteByte(3); w.WriteFloat(1); w.WriteFloat(2); w.WriteFloat(3); });
        p = ProxyGameObj.Read(r);
        Assert.That((p.ObjectId, p.TemplateId, p.Extra), Is.EqualTo((1, 2, 4)));
        r = Reader(w => { w.WriteByte(8); w.WriteInt(1); w.WriteByte(3); w.WriteFloat(1); w.WriteFloat(2); w.WriteFloat(3); });
        Assert.That(ProxyGameObj.Read(r).ObjectId, Is.EqualTo(1));
        r = Reader(w => { w.WriteByte(7); w.WriteInt(1); });
        Assert.That(ProxyGameObj.Read(r).ObjectId, Is.EqualTo(1));
        // a body shorter than the client reads fails; a longer one is refused
        Assert.That(() => ProxyGameObj.Read(Reader(w => { w.WriteByte(10); w.WriteInt(1); w.WriteInt(2); })), Throws.Exception);
    }

    [Test] public void UserEnvModes() {
        var r = Reader(w => { w.WriteByte(4); w.WriteInt(2); w.WriteInt(11); w.WriteInt(12); });
        Assert.That(((UserEnvMessage.Templates) UserEnvMessage.Read(r)).Ids, Is.EqualTo(new[] { 11, 12 }));
        r = Reader(w => { w.WriteByte(0xd); w.WriteInt(9); w.WriteLong(77); });
        UserEnvMessage.Read(r);
        Assert.That(r.Available, Is.Zero);
        r = Reader(w => { w.WriteByte(0xb); w.WriteShort(3); w.WriteInt(9); });
        UserEnvMessage.Read(r);
        Assert.That(r.Available, Is.Zero);
    }

    [Test] public void PartyNoticeAndResult() {
        var r = Reader(w => { w.WriteByte(0x14); w.WriteUnicodeString("a"); w.WriteUnicodeString("b"); w.WriteUnicodeString("c"); });
        Assert.That(PartyMessage.Read(r), Is.EqualTo(new PartyNotice("a", "b", "c")));
        r = Reader(w => { w.WriteByte(0); w.WriteByte(5); w.WriteUnicodeString("x"); });
        Assert.That(PartyMessage.Read(r), Is.EqualTo(new PartyResult(5, "x")));
        r = Reader(w => { w.WriteByte(0x19); w.WriteBool(true); w.WriteInt(3); });
        Assert.That(PartyMessage.Read(r), Is.EqualTo(new PartyMatchDungeonChange(true, 3)));
    }

    [Test] public void BuddyUnhandledModeKeepsItsBody() {
        var r = Reader(w => { w.WriteByte(0x13); w.WriteInt(1); });
        BuddyMessage.Read(r);
        Assert.That(r.Available, Is.Zero);
    }

    [Test] public void GuildSmallModes() {
        var r = Reader(w => { w.WriteByte(0xe); w.WriteUnicodeString("n"); w.WriteInt(5); });
        Assert.That(GuildMessage.Read(r), Is.EqualTo(new GuildMessage(0xe) { Name = "n", Value = 5 }));
        r = Reader(w => { w.WriteByte(0x23); w.WriteUnicodeString("n"); w.WriteInt(1); w.WriteInt(2); w.WriteShort(3); });
        var m = GuildMessage.Read(r);
        Assert.That((m.AchieveId, m.Grade, m.Kind), Is.EqualTo((1, 2, 3)));
        r = Reader(w => { w.WriteByte(0x3a); w.WriteLong(9); w.WriteUnicodeString("u"); w.WriteInt(4); w.WriteUnicodeString("t"); });
        m = GuildMessage.Read(r);
        Assert.That((m.GuildId, m.Value, m.Text), Is.EqualTo((9L, 4, "t")));
        // a trailing byte is refused
        r = Reader(w => { w.WriteByte(0x41); w.WriteUnicodeString("s"); w.WriteByte(0); });
        Assert.That(() => GuildMessage.Read(r), Throws.TypeOf<PacketLayoutException>());
    }
    [Test] public void GuildInviteAndDonationModesRejectLeftoverBytes() {
        foreach (byte t in new byte[] { 3, 6, 7, 0xf, 0x71 }) {
            var body = Reader(w => { w.WriteByte(t); });
            var bytes = new ByteWriter();
            bytes.WriteByte(t);
            switch (t) {
                case 3: bytes.WriteUnicodeString("g"); break;
                case 6: bytes.WriteUnicodeString("n"); bytes.WriteByte(1); bytes.WriteByte(0); break;
                case 0x71: bytes.WriteInt(1); bytes.WriteLong(2); break;
            }
            var ok = new ByteReader(bytes.Buffer.AsSpan(0, bytes.Length).ToArray());
            Assert.DoesNotThrow(() => GuildMessage.Read(ok), $"type {t:x}");
            bytes.WriteByte(9);
            var extra = new ByteReader(bytes.Buffer.AsSpan(0, bytes.Length).ToArray());
            Assert.Throws<PacketLayoutException>(() => GuildMessage.Read(extra), $"type {t:x}");
        }
    }

    [Test] public void UserEnvEmptyListModeRejectsLeftoverBytes() {
        Assert.DoesNotThrow(() => UserEnvMessage.Read(Reader(w => { w.WriteByte(5); w.WriteInt(0); })));
        Assert.Throws<PacketLayoutException>(() => UserEnvMessage.Read(Reader(w => { w.WriteByte(5); w.WriteInt(0); w.WriteByte(1); })));
    }
}
