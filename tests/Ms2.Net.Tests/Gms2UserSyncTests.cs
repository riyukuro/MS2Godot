using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The GMS2 client's UserSync (0x12) and RideSync (0x42): the KMS2 layout plus a trailing i32, the server time of its first record.</summary>
public class Gms2UserSyncTests {
    static IEnumerable<(string File, MsbFile Msb)> Recordings() {
        string root = Environment.GetEnvironmentVariable("MS2_SNIFFS");
        if (string.IsNullOrEmpty(root)) Assert.Ignore("MS2_SNIFFS needed");
        string dir = Path.Combine(root, "gms2-sniffs");
        if (!Directory.Exists(dir)) Assert.Ignore("MS2_SNIFFS has no gms2-sniffs folder");
        foreach (var path in Directory.GetFiles(dir, "*.msb").OrderBy(x => x)) yield return (Path.GetFileName(path), MsbFile.Read(path));
    }

    [Test] public void EveryRecordedOneRecordGms2SyncEndsWithTheServerTimeOfItsRecord() {
        // The records' flag bytes make a whole-packet read ambiguous for some states, so a one-record packet is read from its end:
        // ... i32 record client tick, i32 trailer. The trailer is the server tick minus (client tick - record tick).
        int total = 0, match = 0;
        foreach (var (name, f) in Recordings())
            foreach (var p in f.Packets) {
                if (p.ServerToClient || p.Opcode is not (0x12 or 0x42) || p.Data.Length < 14 || p.Data[9] != 1) continue;
                int server = BitConverter.ToInt32(p.Data, 1), client = BitConverter.ToInt32(p.Data, 5);
                int recTick = BitConverter.ToInt32(p.Data, p.Data.Length - 8), trailer = BitConverter.ToInt32(p.Data, p.Data.Length - 4);
                total++; if (trailer == server - (client - recTick)) match++;
            }
        TestContext.Out.WriteLine($"{match}/{total} one-record packets match");
        Assert.That(total, Is.GreaterThan(1000));
        Assert.That(match, Is.GreaterThan(total * 0.85));   // 88% recorded; the rest carry an earlier time (the state began before the record)
    }

    [Test] public void Gms2UserSyncWritesTheFirstRecordsServerTimeAfterTheRecordsAndKmsDoesNot() {
        var o = new UserSyncOut { SyncNumber = 3, ServerTick = 100, ClientTick = 200, };
        o.Records.Add((new StateSync { State = 2, Position = new Vector3S(1, 2, 3), SyncNumber = 5 }, 150));
        var kms = new ByteWriter(); o.Write(kms, ProtocolProfile.Kms2525);
        var gms = new ByteWriter(); o.Write(gms, ProtocolProfile.Gms2);
        Assert.That(gms.Length, Is.EqualTo(kms.Length + 4));
        var back = UserSyncOut.Read(new ByteReader(gms.ToArray()), ProtocolProfile.Gms2);
        Assert.That((back.ServerTick, back.Trailer, back.Records.Count), Is.EqualTo((100, 50, 1)));
        Assert.That(UserSyncOut.Read(new ByteReader(kms.ToArray())).Records.Count, Is.EqualTo(1));
    }
}
