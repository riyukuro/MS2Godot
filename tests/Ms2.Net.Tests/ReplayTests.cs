using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Plays a recording through ReplayStream, MapleChannel, ClientSession and FieldTracker without waiting and compares what comes out
/// with an independent walk over the recording. MS2_REPLAY names one .msb file (local, never in the repo); skipped when unset.
/// </summary>
public class ReplayTests {
    static MsbFile Recording() {
        string path = Environment.GetEnvironmentVariable("MS2_REPLAY");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) Assert.Ignore("MS2_REPLAY not set");
        return MsbFile.Read(path);
    }

    [Test] public async Task PositionsMatchTheRecording() {
        var file = Recording();
        var rs = new ReplaySession(file, speed: double.PositiveInfinity);
        await rs.Start();
        Assert.That(rs.Error, Is.Null);

        // independent expectation straight from the recorded packets
        int self = 0; var adds = new List<int>(); var removed = new List<int>();
        var expectPos = new Dictionary<int, Vector3>(); var expectYaw = new Dictionary<int, float>(); var moves = new Dictionary<int, int>();
        var live = new HashSet<int>();
        foreach (var p in file.Packets) {
            if (p.ServerToClient) {
                var r = new ByteReader(p.Data);
                switch (p.Opcode) {
                    case 0x15: self = ServerEnter.Read(r).ObjectId; break;
                    case 0x16: if (RequestFieldEnter.Read(r).Error == 0) { live.Clear(); } break;
                    case 0x17: { var u = FieldAddUser.Read(r); adds.Add(u.ObjectId); live.Add(u.ObjectId); expectPos[u.ObjectId] = u.Position; expectYaw[u.ObjectId] = u.Rotation.Z; moves[u.ObjectId] = 0; break; }
                    case 0x18: { int id = FieldRemoveUser.Read(r).ObjectId; removed.Add(id); live.Remove(id); break; }
                    case 0x1C: case 0x7A: {
                        var s = UserSyncIn.Read(r);
                        if (!live.Contains(s.ObjectId)) break;
                        foreach (var rec in s.Records) { expectPos[s.ObjectId] = rec.Position.ToVector3(); expectYaw[s.ObjectId] = rec.Rotation2 / 10f; moves[s.ObjectId]++; }
                        break;
                    }
                }
            } else if (p.Opcode == 0x12 && self != 0 && live.Contains(self)) {
                foreach (var (rec, _) in UserSyncOut.Read(new ByteReader(p.Data)).Records) { expectPos[self] = rec.Position.ToVector3(); expectYaw[self] = rec.Rotation2 / 10f; moves[self]++; }
            }
        }

        var got = rs.Tracker.Snapshot().ToDictionary(a => a.ObjectId);
        TestContext.Out.WriteLine($"{adds.Count} adds, {removed.Count} removes, {got.Count} players left, {moves.Values.Sum()} moves");
        Assert.That(got.Keys.OrderBy(x => x), Is.EqualTo(live.OrderBy(x => x)));
        foreach (var (id, a) in got) {
            Assert.That(a.Position, Is.EqualTo(expectPos[id]), $"position of {a.Name}");
            Assert.That(a.YawDegrees, Is.EqualTo(expectYaw[id]), $"yaw of {a.Name}");
            Assert.That(a.Moves, Is.EqualTo(moves[id]), $"move count of {a.Name}");
        }
        int ev = rs.Tracker.Events.Count(e => e is UserAddedEvent);
        Assert.That(ev, Is.EqualTo(adds.Count));
        Assert.That(rs.Tracker.Events.Count(e => e is UserRemovedEvent), Is.EqualTo(removed.Count));
        Assert.That(rs.Stream.BytesWrittenByClient, Is.GreaterThan(0)); // the hello answer and keepalives went to the sink
    }

    [Test] public async Task PacedPlaybackReleasesPacketsAtRecordedTimes() {
        var file = Recording();
        var rs = new ReplaySession(file, speed: 50, startOffsetSeconds: 5);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await rs.Start();
        double wall = sw.Elapsed.TotalSeconds, rec = rs.Stream.Duration - 5;
        TestContext.Out.WriteLine($"recording {rs.Stream.Duration:F1}s played in {wall:F2}s at x50 from 5s");
        Assert.That(wall, Is.GreaterThan(rec / 50 * 0.8));
        Assert.That(wall, Is.LessThan(rec / 50 + 3));
    }
}
