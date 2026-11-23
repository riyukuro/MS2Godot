using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Achieve (0x5F GMS2, 0x5E KMS2): mode 0 reloads the book, mode 2 updates one trophy (id and progress: state, the
/// unexplained i32, the current and start grades, bookmark, condition count and the grade values). The recorded GMS2 update
/// carries one value (grade 1, 1587350952); the KMS one carries none.</summary>
public class AchieveRecordedTests {
    // gms2 recording: packet 2200, 1 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2Reload = new byte[] {
        0x00,
    };
    // gms2 recording: packet 2327, 43 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2Update = new byte[] {
        0x02, 0x17, 0x7B, 0x60, 0x01, 0x03, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02, 0x00,
        0x00, 0x00, 0x00, 0x43, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x00, 0x00, 0x00, 0xA8, 0x0D, 0x9D, 0x5E, 0x00, 0x00, 0x00, 0x00,
    };
    // kms2 recording: packet 489, 1 bytes (names masked with '*' by the masking pass).
    static readonly byte[] KmsReload = new byte[] {
        0x00,
    };
    // kms2 recording: packet 501, 31 bytes (names masked with '*' by the masking pass).
    static readonly byte[] KmsUpdate = new byte[] {
        0x02, 0xC2, 0xBE, 0x52, 0x01, 0x02, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    };

    [Test] public void Gms2ReloadReadsOneByte() {
        var r = new ByteReader(Gms2Reload);
        Assert.That(AchieveMessage.Read(r), Is.TypeOf<AchieveMessage.Reload>());
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void Gms2UpdateReadsTheProgressAndItsValues() {
        var r = new ByteReader(Gms2Update);
        var u = AchieveMessage.Read(r) as AchieveMessage.Update;
        Assert.That(u, Is.Not.Null);
        Assert.That((u.Id, u.Progress.State, u.Progress.Value80, u.Progress.CurGrade, u.Progress.StartGrade, u.Progress.Bookmark, u.Progress.Count),
            Is.EqualTo((23100183, (byte) 3, 1, 1, 2, false, 67L)));
        Assert.That(u.Progress.Values, Is.EqualTo(new Dictionary<int, long> { [1] = 1587350952 }));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void KmsReloadReadsOneByte() {
        var r = new ByteReader(KmsReload);
        Assert.That(AchieveMessage.Read(r), Is.TypeOf<AchieveMessage.Reload>());
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void KmsUpdateReadsAnEmptyValueMap() {
        var r = new ByteReader(KmsUpdate);
        var u = AchieveMessage.Read(r) as AchieveMessage.Update;
        Assert.That(u, Is.Not.Null);
        Assert.That((u.Id, u.Progress.State, u.Progress.CurGrade, u.Progress.StartGrade, u.Progress.Count),
            Is.EqualTo((22200002, (byte) 2, 1, 1, 1L)));
        Assert.That(u.Progress.Values, Is.Empty);
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
