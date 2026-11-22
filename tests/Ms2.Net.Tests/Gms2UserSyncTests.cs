using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The GMS2 client's UserSync (0x12) and RideSync (0x42): its records put the flags byte right after the sub state and each ends with the server time of the record.</summary>
public class Gms2UserSyncTests {
    [Test] public void Gms2WritesTheFlagsByteEarlyAndTheRecordServerTimeAfterTheClientTick() {
        var o = new UserSyncOut { SyncNumber = 3, ServerTick = 100, ClientTick = 200 };
        o.Records.Add((new StateSync { State = 2, SubState = 7, Position = new Vector3S(1, 2, 3), SyncNumber = 5, Flags = StateSync.Flag.Flag1 | StateSync.Flag.Animation, Flag1Value = 9, Flag3Value = 4, AnimationName = "Hold" }, 150));
        var kms = new ByteWriter(); o.Write(kms, ProtocolProfile.Kms2525);
        var gms = new ByteWriter(); o.Write(gms, ProtocolProfile.Gms2);
        var back = UserSyncOut.Read(new ByteReader(gms.ToArray()), ProtocolProfile.Gms2);
        var rec = back.Records[0].Record;
        Assert.That((rec.State, rec.SubState, (byte) rec.Flags, rec.Flag1Value, rec.Flag3Value, rec.AnimationName, rec.SyncNumber, back.Records[0].Tick, rec.Gms2ServerTick),
            Is.EqualTo(((byte) 2, (byte) 7, (byte) 9, 9, (short) 4, "Hold", 5, 250, (int?) 150)));   // the live record's server tick 150 is the trailer; its client tick is 150 - (100 - 200)
        Assert.That(gms.ToArray()[12], Is.EqualTo(9));   // 10 header bytes, state, sub state, then the flags
        Assert.That(UserSyncOut.Read(new ByteReader(kms.ToArray())).Records.Count, Is.EqualTo(1));   // KMS layout unchanged
    }

    [Test] public void AnExplicitRecordServerTimeIsWrittenAsItIs() {
        var o = new UserSyncOut { SyncNumber = 3, ServerTick = 100, ClientTick = 200 };
        o.Records.Add((new StateSync { State = 1, Gms2ServerTick = 77 }, 150));
        var gms = new ByteWriter(); o.Write(gms, ProtocolProfile.Gms2);
        Assert.That(UserSyncOut.Read(new ByteReader(gms.ToArray()), ProtocolProfile.Gms2).Records[0].Record.Gms2ServerTick, Is.EqualTo(77));
    }

    [Test] public void ALiveRecordCarriesItsServerTickInTheTrailerAndTheElapsedTickInTheClientSlot() {
        var o = new UserSyncOut { SyncNumber = 1, ServerTick = 5000, ClientTick = 2000 };   // the server clock is 3000 ahead of the elapsed one
        o.Records.Add((new StateSync { State = 1 }, 4900));   // a live record: its server tick
        var back = UserSyncOut.Read(new ByteReader(Write(o)), ProtocolProfile.Gms2);
        Assert.That((back.Records[0].Tick, back.Records[0].Record.Gms2ServerTick), Is.EqualTo((1900, (int?) 4900)));
    }

    [Test] public void AHoldRecordWithATargetPutsItInBitTwoAndNeverSetsBitFive() {
        var o = new UserSyncOut { SyncNumber = 1, ServerTick = 10, ClientTick = 10 };
        o.Records.Add((new StateSync { State = 32, Flags = StateSync.Flag.Flag1 | StateSync.Flag.Flag3 | StateSync.Flag.Flag6, Flag1Value = 20000031, Flag3Value = 1, Flag6Value = 3732559, Flag6Text = "Hold_Point00" }, 10));
        var bytes = Write(o);
        var rec = UserSyncOut.Read(new ByteReader(bytes), ProtocolProfile.Gms2).Records[0].Record;
        Assert.That(((byte) rec.Flags, rec.Flag2Value, rec.Flag2Text, rec.Flag1Value), Is.EqualTo(((byte) 5, 3732559, "Hold_Point00", 20000031)));
        // the same record for KMS keeps the KMS meaning
        var k = new ByteWriter(); o.Write(k, ProtocolProfile.Kms2525);
        Assert.That(UserSyncOut.Read(new ByteReader(k.ToArray())).Records[0].Record.Flags & StateSync.Flag.Flag6, Is.EqualTo(StateSync.Flag.Flag6));
    }

    [Test] public void RecordedGms2PacketsRebuiltAsLiveOnesKeepBothTicks() {
        int total = 0, close = 0;
        foreach (var (_, d) in Gms2Sniffs.Outbound("gms2", 0x12)) {
            var p = UserSyncOut.Read(new ByteReader(d), ProtocolProfile.Gms2);
            var live = new UserSyncOut { SyncNumber = p.SyncNumber, ServerTick = p.ServerTick, ClientTick = p.ClientTick };
            var orig = UserSyncOut.Read(new ByteReader(d), ProtocolProfile.Gms2);
            // the sender is fed server ticks: each record's recorded server tick, with no explicit one
            foreach (var (rec, _) in p.Records) { int srv = rec.Gms2ServerTick.Value; rec.Gms2ServerTick = null; live.Records.Add((rec, srv)); }
            var re = UserSyncOut.Read(new ByteReader(Write(live)), ProtocolProfile.Gms2);
            for (int i = 0; i < re.Records.Count; i++) { total++; if (Math.Abs(re.Records[i].Tick - orig.Records[i].Tick) <= 1000 && re.Records[i].Record.Gms2ServerTick == live.Records[i].Tick) close++; }
        }
        Assert.That(total, Is.GreaterThan(0)); Assert.That(close, Is.GreaterThan(total * 0.98));
    }

    [Test] public void EveryRecordedGms2UserSyncEchoParses() {
        int n = 0;
        foreach (var (f, d) in Gms2Sniffs.Inbound("gms2", 0x1C)) { n++; Assert.DoesNotThrow(() => UserSyncIn.Read(new ByteReader(d), ProtocolProfile.Gms2), f); }
        Assert.That(n, Is.GreaterThan(0));
    }

    static byte[] Write(UserSyncOut o) { var w = new ByteWriter(); o.Write(w, ProtocolProfile.Gms2); return w.ToArray(); }
}
