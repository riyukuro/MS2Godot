using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// UserBattle (KMS 0x2F, GMS2 0x30): CSceneStateGame::OnUserBattle (scenestategame.cpp:3269-3288), handled by
/// <see cref="FieldTracker.Inbound"/> (enqueues <see cref="UserBattleEvent"/> with object id and emotion byte).
/// </summary>
public class UserBattleRecordedTests {
    // gms2 recording: 0x30 (battle on), packet 2411, 5 bytes (extracted with extract.py): object 442495, emotion 1
    static readonly byte[] Gms2On = Convert.FromHexString("7FC0060001");
    // gms2 recording: 0x30 (battle off), packet 2498, 5 bytes (extracted with extract.py): object 442495, emotion 0
    static readonly byte[] Gms2Off = Convert.FromHexString("7FC0060000");

    // kms2 recording: 0x2F (battle on), packet 8759, 5 bytes (extracted with extract.py): object 15040624, emotion 1
    static readonly byte[] KmsOn = Convert.FromHexString("7080E50001");
    // kms2 recording: 0x2F (battle off), packet 11376, 5 bytes (extracted with extract.py): object 15040624, emotion 0
    static readonly byte[] KmsOff = Convert.FromHexString("7080E50000");

    [Test] public void Gms2UserBattleDecodesAndEnqueuesFieldTrackerEvents() {
        var r1 = new ByteReader(Gms2On);
        var b1 = UserBattle.Read(r1);
        Assert.That((b1.ObjectId, b1.Emotion), Is.EqualTo((442495, (byte)1)));
        Assert.That(r1.Available, Is.EqualTo(0));

        var r0 = new ByteReader(Gms2Off);
        var b0 = UserBattle.Read(r0);
        Assert.That((b0.ObjectId, b0.Emotion), Is.EqualTo((442495, (byte)0)));
        Assert.That(r0.Available, Is.EqualTo(0));

        var tracker = new FieldTracker { Profile = ProtocolProfile.Gms2, Time = 10.0 };
        ushort op = ProtocolProfile.Gms2.Number(Inbound.UserBattle);
        tracker.Inbound(new RawPacket(op, Gms2On));
        tracker.Time = 12.5;
        tracker.Inbound(new RawPacket(op, Gms2Off));

        var evs = tracker.Events.OfType<UserBattleEvent>().ToList();
        Assert.That(evs, Is.EqualTo(new[] {
            new UserBattleEvent(10.0, 442495, 1),
            new UserBattleEvent(12.5, 442495, 0),
        }));
    }

    [Test] public void KmsUserBattleDecodesAndEnqueuesFieldTrackerEvents() {
        var r1 = new ByteReader(KmsOn);
        var b1 = UserBattle.Read(r1);
        Assert.That((b1.ObjectId, b1.Emotion), Is.EqualTo((15040624, (byte)1)));
        Assert.That(r1.Available, Is.EqualTo(0));

        var r0 = new ByteReader(KmsOff);
        var b0 = UserBattle.Read(r0);
        Assert.That((b0.ObjectId, b0.Emotion), Is.EqualTo((15040624, (byte)0)));
        Assert.That(r0.Available, Is.EqualTo(0));

        var tracker = new FieldTracker { Profile = ProtocolProfile.Kms2525, Time = 3.0 };
        ushort op = ProtocolProfile.Kms2525.Number(Inbound.UserBattle);
        tracker.Inbound(new RawPacket(op, KmsOn));
        tracker.Time = 5.0;
        tracker.Inbound(new RawPacket(op, KmsOff));

        var evs = tracker.Events.OfType<UserBattleEvent>().ToList();
        Assert.That(evs, Is.EqualTo(new[] {
            new UserBattleEvent(3.0, 15040624, 1),
            new UserBattleEvent(5.0, 15040624, 0),
        }));
    }
}
