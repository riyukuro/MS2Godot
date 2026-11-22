using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using System.IO;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The GMS2 client's recorded client-to-server packets, read back into our request records and written again with the
/// GMS2 profile: each must come out byte for byte. A packet the parse cannot take whole is a layout the request does not cover.
/// Skipped without MS2_SNIFFS.</summary>
public class Gms2OutboundRewriteTests {
    static Vector3 V(ByteReader r) => new(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());

    /// <summary>Counts the recorded packets of an opcode that the parse reads whole and the request writes back unchanged.</summary>
    static (int Total, int Same, string First) Rewrite(ushort opcode, Func<ByteReader, IOutPacket> parse, Func<byte[], bool> take = null) {
        int total = 0, same = 0; string first = null; var diffs = new Dictionary<string, int>();
        foreach (var (file, data) in Gms2Sniffs.Outbound("gms2", opcode)) {
            if (take != null && !take(data)) continue;
            total++;
            try {
                var r = new ByteReader(data);
                var req = parse(r);
                var w = new ByteWriter();
                if (req is IProfileOutPacket pp) pp.Write(w, ProtocolProfile.Gms2); else req.Write(w);
                if (r.Available == 0 && w.ToArray().AsSpan().SequenceEqual(data)) { same++; continue; }
                var o = w.ToArray(); int at = 0; while (at < o.Length && at < data.Length && o[at] == data[at]) at++;
                diffs[$"len {data.Length}->{o.Length} at {at}"] = diffs.GetValueOrDefault($"len {data.Length}->{o.Length} at {at}") + 1;
                first ??= $"{file}: {(r.Available != 0 ? r.Available + " bytes left" : "rewrite differs")} len {data.Length}->{o.Length}, first diff at {at}, in {Convert.ToHexString(data)} out {Convert.ToHexString(o)}";
            } catch (Exception e) { first ??= $"{file}: {e.GetType().Name} {e.Message}"; }
        }
        TestContext.Out.WriteLine($"0x{opcode:X2}: {same}/{total} {first}");
        foreach (var kv in diffs.OrderByDescending(k => k.Value).Take(8)) TestContext.Out.WriteLine($"   {kv.Value}x {kv.Key}");
        return (total, same, first);
    }

    // Skill 0x20: the cast carries the hold-attack tail when the client has feature 174 on, so a cast is tried with and without it
    static IOutPacket SkillCast(ByteReader r, bool hold) {
        r.ReadByte();
        long cast = r.ReadLong(); int tick = r.ReadInt(), skill = r.ReadInt(); short lv = r.ReadShort(); byte motion = r.ReadByte();
        var p = V(r); var d = V(r); var rot = V(r); float dist = r.ReadFloat(); int el = r.ReadInt(); bool flag = r.ReadBool(); long item = r.ReadLong(), key = 0;   // GMS2 casts have no cooldown key
        int att = 0; string node = "";
        if (r.ReadBool()) { att = r.ReadInt(); node = r.ReadUnicodeString(); }
        return new SkillCastRequest(cast, tick, skill, lv, motion, p, d, rot, dist, el, flag, item, key, hold, att, node);
    }

    [Test] public void SkillCastsRewriteByteForByte() {
        var (t, s, f) = Rewrite(0x20, r => SkillCast(r, false), d => d[0] == 0);
        Assert.That(t, Is.GreaterThan(0));
        Assert.That(s, Is.EqualTo(t), f);
    }

    [Test] public void SkillModesTwoAndThreeRewriteByteForByte() {
        var (t, s, f) = Rewrite(0x20, r => { r.ReadByte(); long c = r.ReadLong(); int sk = r.ReadInt(); short lv = r.ReadShort(); byte m = r.ReadByte();
            var p = V(r); var d = V(r); var rot = V(r); var o = V(r); bool ch = r.ReadBool(), lp = r.ReadBool(); return new SkillSyncRequest(c, sk, lv, m, p, d, rot, o, ch, lp, r.ReadFloat()); }, d => d[0] == 2);
        Assert.That(t, Is.GreaterThan(0)); Assert.That(s, Is.EqualTo(t), f);
        (t, s, f) = Rewrite(0x20, r => { r.ReadByte(); return new SkillRoundRequest(r.ReadLong(), r.ReadInt()); }, d => d[0] == 3);
        Assert.That(t, Is.GreaterThan(0)); Assert.That(s, Is.EqualTo(t), f);
    }

    [Test] public void SkillModeOneRewritesByteForByte() {
        var (t, s, f) = Rewrite(0x20, r => { r.ReadByte(); r.ReadByte(); long c = r.ReadLong(); byte a = r.ReadByte(); var p = V(r); var d = V(r);
            int n = r.ReadByte(); int sc = r.ReadInt(); var ts = new List<SkillDamageTarget>();
            for (int i = 0; i < n; i++) { long ser = r.ReadLong(); int id = r.ReadInt(); byte h = r.ReadByte(); var ls = new List<SkillDamageLink>();
                while (r.ReadBool()) ls.Add(new(r.ReadLong(), r.ReadInt(), r.ReadByte(), r.ReadByte())); ts.Add(new(ser, id, h, ls)); }
            return new SkillDamageRequest(c, a, p, d, sc, ts); }, d => d[0] == 1 && d[1] == 0);
        Assert.That(t, Is.GreaterThan(0)); Assert.That(s, Is.EqualTo(t), f);
        (t, s, f) = Rewrite(0x20, r => { r.ReadByte(); r.ReadByte(); long c = r.ReadLong(), ser = r.ReadLong(); var st = V(r); var o = V(r); var rot = V(r); byte a = r.ReadByte(); int n = r.ReadByte(); int sc = r.ReadInt();
            var ts = new List<SkillHitTarget>(); for (int i = 0; i < n; i++) ts.Add(new(r.ReadInt(), r.ReadByte())); return new SkillHitRequest(c, ser, st, o, rot, a, sc, ts); }, d => d[0] == 1 && d[1] == 1);
        Assert.That(t, Is.GreaterThan(0)); Assert.That(s, Is.EqualTo(t), f);
        (t, s, f) = Rewrite(0x20, r => { r.ReadByte(); r.ReadByte(); long c = r.ReadLong(); byte a = r.ReadByte(); int sc = r.ReadInt(), h = r.ReadInt(); return new SkillSplashRequest(c, a, sc, h, V(r), V(r)); }, d => d[0] == 1 && d[1] == 2);
        Assert.That(t, Is.GreaterThan(0)); Assert.That(s, Is.EqualTo(t), f);
    }

    [Test] public void SkillStopsRewriteByteForByte() {
        var (t, s, f) = Rewrite(0x20, r => { r.ReadByte(); return new SkillStopRequest(r.ReadLong()); }, d => d[0] == 4);
        Assert.That(t, Is.GreaterThan(0)); Assert.That(s, Is.EqualTo(t), f);
    }

    static void All(ushort op, Func<ByteReader, IOutPacket> parse, Func<byte[], bool> take = null) {
        var (t, s, f) = Rewrite(op, parse, take);
        Assert.That(t, Is.GreaterThan(0)); Assert.That(s, Is.EqualTo(t), f);
    }

    [Test] public void UserSyncRewritesByteForByte() => All(0x12, r => UserSyncOut.Read(r, ProtocolProfile.Gms2));
    [Test] public void RideSyncRewritesByteForByte() => All(0x42, r => { var p = UserSyncOut.Read(r, ProtocolProfile.Gms2); p.Ride = true; return p; });
    [Test] public void PongRewritesByteForByte() => All(0x06, Pong.Read);
    [Test] public void RequestTimeSyncRewritesByteForByte() => All(0x0B, RequestTimeSync.Read);
    [Test] public void StateChangeRewritesByteForByte() => All(0x81, StateChange.Read);
    [Test] public void StateSkillRewritesByteForByte() => All(0x21, r => { r.ReadByte(); return new StateSkillRequest(r.ReadLong(), r.ReadInt(), r.ReadInt(), r.ReadShort(), r.ReadInt(), r.ReadInt(), r.ReadLong()); }, d => d[0] == 0);
    [Test] public void NpcTalkRewritesByteForByte() => All(0x22, r => {
        byte m = r.ReadByte();
        return m switch { 0 => new NpcTalkEndRequest(), 1 => new NpcTalkBeginRequest(r.ReadInt()), 2 => new NpcTalkNextRequest(r.ReadInt()),
            7 => new NpcTalkQuestRedirectRequest(r.ReadInt(), r.ReadShort()),
            6 => new NpcTalkModifyInTalkRequest(r.ReadInt(), r.ReadInt(), r.ReadShort(), r.Available >= 8 ? r.ReadLong() : null),
            _ => throw new InvalidDataException("mode " + m) }; }, d => d[0] != 8);   // mode 8 (i32, i16; 3 recorded) has no client writer in the KMS client: STILL-OPEN
}
