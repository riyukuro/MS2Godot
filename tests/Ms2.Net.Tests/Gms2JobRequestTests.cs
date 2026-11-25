using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Net.Replay;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>GMS2 Job requests (outbound 0x25): the modes do not shift against KMS2 (close 8, apply 9), unlike Trigger (9 -> 8).
/// The recordings hold both; our packets write the same bytes under both profiles, so they need no profile variant.</summary>
public class Gms2JobRequestTests {
    static List<(string File, byte[] Data)> Recorded() {
        string root = Environment.GetEnvironmentVariable("MS2_SNIFFS");
        if (string.IsNullOrEmpty(root)) Assert.Ignore("MS2_SNIFFS needed");
        string dir = Path.Combine(root, "gms2-sniffs");
        if (!Directory.Exists(dir)) Assert.Ignore("MS2_SNIFFS has no gms2-sniffs folder");
        var list = new List<(string, byte[])>();
        ushort op = ProtocolProfile.Gms2.Number(Outbound.Job);
        foreach (var path in Directory.GetFiles(dir, "*.msb"))
            foreach (var p in MsbFile.Read(path).Packets) if (!p.ServerToClient && p.Opcode == op) list.Add((Path.GetFileName(path), p.Data));
        return list;
    }

    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    [Test]
    public void EveryRecordedGms2JobRequestIsACloseOrAnApplyWeWriteTheSameWay() {
        var all = Recorded();
        Assert.That(all, Is.Not.Empty);
        foreach (var (file, data) in all) {
            if (data[0] == 8) { Assert.That(data, Is.EqualTo(Bytes(new JobCloseRequest())), file); continue; }
            Assert.That(data[0], Is.EqualTo(9), file);
            var r = new ByteReader(data);
            r.ReadByte();
            int n = r.ReadInt();
            var skills = new List<(int, int, bool)>();
            for (int i = 0; i < n; i++) skills.Add((r.ReadInt(), r.ReadShort(), r.ReadBool()));
            Assert.That(r.Available, Is.EqualTo(0), file);
            Assert.That(Bytes(new JobSkillsRequest(9, skills)), Is.EqualTo(data), file);
        }
    }

    [Test]
    public void TheJobRequestsWriteTheModesOfTheKmsPacketsUnderBothProfiles() {
        Assert.That(Bytes(new JobCloseRequest()), Is.EqualTo(new byte[] { 8 }));
        Assert.That(Bytes(new JobResetRequest(3)), Is.EqualTo(new byte[] { 10, 3, 0, 0, 0 }));
        Assert.That(Bytes(new JobSkillsRequest(9, new[] { (1, 2, true) })), Is.EqualTo(new byte[] { 9, 1, 0, 0, 0, 1, 0, 0, 0, 2, 0, 1 }));
        Assert.That(ProtocolProfile.Gms2.Has(Outbound.Job), Is.True);
    }

    [Test]
    public void TheSkillBookLearnWritesJobMode7() {
        // JobSystemCli::SendRequest @0x140a6e190 (jobsystemcli.cpp:169-180): u8 7, then FUN_1401fdb50: i32 job, i64 uid, i32 skill, i16 level
        Assert.That(Bytes(new JobSkillLearnRequest(10, 0x0102030405060708, 10500001, 2)), Is.EqualTo(new byte[] {
            7, 10, 0, 0, 0, 8, 7, 6, 5, 4, 3, 2, 1, 0xa1, 0x37, 0xa0, 0, 2, 0 }));
        Assert.That(new JobSkillLearnRequest(10, 1, 2, 3).Op, Is.EqualTo(Outbound.Job));
    }
}
