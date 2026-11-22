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
            Is.EqualTo(((byte) 2, (byte) 7, (byte) 9, 9, (short) 4, "Hold", 5, 150, (int?) 50)));   // server 100 - (client 200 - record 150)
        Assert.That(gms.ToArray()[12], Is.EqualTo(9));   // 10 header bytes, state, sub state, then the flags
        Assert.That(UserSyncOut.Read(new ByteReader(kms.ToArray())).Records.Count, Is.EqualTo(1));   // KMS layout unchanged
    }

    [Test] public void AnExplicitRecordServerTimeIsWrittenAsItIs() {
        var o = new UserSyncOut { SyncNumber = 3, ServerTick = 100, ClientTick = 200 };
        o.Records.Add((new StateSync { State = 1, Gms2ServerTick = 77 }, 150));
        var gms = new ByteWriter(); o.Write(gms, ProtocolProfile.Gms2);
        Assert.That(UserSyncOut.Read(new ByteReader(gms.ToArray()), ProtocolProfile.Gms2).Records[0].Record.Gms2ServerTick, Is.EqualTo(77));
    }
}
