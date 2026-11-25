using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2.Net.Wire;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Prestige (KMS 0x11C, GMS2 0x11E): CSceneStateGame::OnPrestige (scenestategame.cpp:8815-8955), handled by
/// <see cref="AdventureData.Apply"/> (kind 0 loaded state, kind 1 exp gain, kind 2 first-level fill) and
/// <see cref="FieldTracker.Inbound"/> (kind 2 updates the tracked actor's PrestigeLevel).
/// </summary>
public class PrestigeRecordedTests {
    // gms2: 0x11E kind 0 (Loaded), 81 bytes: exp 774885, filled 402, 14 claimed
    static readonly byte[] Gms2Loaded = Convert.FromHexString(
        "00E5D20B000000000092010000E5D20B00000000000E000000020000000400000006000000080000000A0000000C000000" +
        "140000001E00000028000000320000003C00000046000000500000005A000000");
    // gms2: 0x11E kind 1 (ExpGain), 17 bytes: value 826385, gain 15000
    static readonly byte[] Gms2ExpGain = Convert.FromHexString("01119C0C0000000000983A000000000000");
    // gms2: 0x11E kind 2 (LevelUp), 9 bytes: object 1705804, level 402
    static readonly byte[] Gms2LevelUp = Convert.FromHexString("024C071A0092010000");

    // kms2: 0x11C kind 0 (Loaded), 57 bytes: exp 710735, filled 40, 8 claimed
    static readonly byte[] KmsLoaded = Convert.FromHexString(
        "004FD80A0000000000280000004FD80A000000000008000000020000000400000006000000080000000A0000000C000000140000001E000000");
    // kms2: 0x11C kind 1 (ExpGain), 17 bytes: value 101000, gain 15000
    static readonly byte[] KmsExpGain = Convert.FromHexString("01888A010000000000983A000000000000");
    // kms2: 0x11C kind 2 (LevelUp), 9 bytes: object 3623263, level 2
    static readonly byte[] KmsLevelUp = Convert.FromHexString("025F49370002000000");

    [Test] public void Gms2PrestigeUpdatesAdventureDataAndFieldTracker() {
        var adv = new AdventureData();

        var r0 = new ByteReader(Gms2Loaded);
        var loaded = PrestigeMessage.Read(r0) as PrestigeMessage.Loaded;
        Assert.That(loaded, Is.Not.Null);
        Assert.That((loaded.Exp, loaded.FilledLevel, loaded.NextExp, loaded.Claimed.Count),
            Is.EqualTo((774885L, 402, 774885L, 14)));
        Assert.That(r0.Available, Is.EqualTo(0));
        adv.Apply(loaded, null, missionFeature: true);
        Assert.That((adv.Exp, adv.FilledLevel, adv.NextExp, adv.Claimed.Count), Is.EqualTo((774885L, 402, 774885L, 14)));
        Assert.That(adv.Claimed.Contains(90u), Is.True);

        var r1 = new ByteReader(Gms2ExpGain);
        var gain = PrestigeMessage.Read(r1) as PrestigeMessage.ExpGain;
        Assert.That(gain, Is.Not.Null);
        Assert.That((gain.Value, gain.Gain), Is.EqualTo((826385L, 15000L)));
        Assert.That(r1.Available, Is.EqualTo(0));
        adv.Apply(gain, null, missionFeature: true);
        Assert.That(adv.Exp, Is.EqualTo(826385L));

        var r2 = new ByteReader(Gms2LevelUp);
        var up = PrestigeMessage.Read(r2) as PrestigeMessage.LevelUp;
        Assert.That(up, Is.Not.Null);
        Assert.That((up.ObjectId, up.Value), Is.EqualTo((1705804, 402)));
        Assert.That(r2.Available, Is.EqualTo(0));

        var tracker = new FieldTracker { Profile = ProtocolProfile.Gms2 };
        FieldTrackerSeed.AddUser(tracker, up.ObjectId, "Hero");
        tracker.Inbound(new RawPacket(ProtocolProfile.Gms2.Number(Inbound.Prestige), Gms2LevelUp));
        Assert.That(tracker.Snapshot().Single(a => a.ObjectId == up.ObjectId).Add.User.PrestigeLevel, Is.EqualTo(up.Value));
    }

    [Test] public void KmsPrestigeUpdatesAdventureDataAndFieldTracker() {
        var adv = new AdventureData();

        var r0 = new ByteReader(KmsLoaded);
        var loaded = PrestigeMessage.Read(r0) as PrestigeMessage.Loaded;
        Assert.That(loaded, Is.Not.Null);
        Assert.That((loaded.Exp, loaded.FilledLevel, loaded.NextExp, loaded.Claimed.Count),
            Is.EqualTo((710735L, 40, 710735L, 8)));
        Assert.That(r0.Available, Is.EqualTo(0));
        adv.Apply(loaded, null, missionFeature: true);
        Assert.That((adv.Exp, adv.FilledLevel, adv.NextExp, adv.Claimed.Count), Is.EqualTo((710735L, 40, 710735L, 8)));
        Assert.That(adv.Claimed.Contains(30u), Is.True);

        var r1 = new ByteReader(KmsExpGain);
        var gain = PrestigeMessage.Read(r1) as PrestigeMessage.ExpGain;
        Assert.That(gain, Is.Not.Null);
        Assert.That((gain.Value, gain.Gain), Is.EqualTo((101000L, 15000L)));
        Assert.That(r1.Available, Is.EqualTo(0));
        adv.Apply(gain, null, missionFeature: true);
        Assert.That(adv.Exp, Is.EqualTo(101000L));

        var r2 = new ByteReader(KmsLevelUp);
        var up = PrestigeMessage.Read(r2) as PrestigeMessage.LevelUp;
        Assert.That(up, Is.Not.Null);
        Assert.That((up.ObjectId, up.Value), Is.EqualTo((3623263, 2)));
        Assert.That(r2.Available, Is.EqualTo(0));

        var tracker = new FieldTracker { Profile = ProtocolProfile.Kms2525 };
        FieldTrackerSeed.AddUser(tracker, up.ObjectId, "Hero");
        tracker.Inbound(new RawPacket(ProtocolProfile.Kms2525.Number(Inbound.Prestige), KmsLevelUp));
        Assert.That(tracker.Snapshot().Single(a => a.ObjectId == up.ObjectId).Add.User.PrestigeLevel, Is.EqualTo(up.Value));
    }
}
