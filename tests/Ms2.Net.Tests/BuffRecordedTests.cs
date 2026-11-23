using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Buff (0x48 GMS2, 0x47 KMS2) add / remove / modify as recorded: the owner header, the state and the shield value.
/// The layout is the same on both regions, so one recorded body of each mode per region suffices.</summary>
public class BuffRecordedTests {
    // gms2 recording: packet 2315, 40 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2Add = new byte[] {
        0x00, 0x7F, 0xC0, 0x06, 0x00, 0x46, 0x47, 0x0C, 0x00, 0x7F, 0xC0, 0x06, 0x00, 0x96, 0x88, 0xE0,
        0x06, 0x96, 0x88, 0xE0, 0x06, 0x1C, 0xE1, 0xF5, 0x05, 0x08, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    };
    // gms2 recording: packet 4817, 13 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2Remove = new byte[] {
        0x01, 0xE2, 0x89, 0x37, 0x00, 0x85, 0x50, 0x35, 0x00, 0xA8, 0x2D, 0x37, 0x00,
    };
    // gms2 recording: packet 8855, 44 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2Modify = new byte[] {
        0x02, 0xF2, 0x4B, 0x26, 0x00, 0xCA, 0x43, 0x0F, 0x00, 0xF2, 0x4B, 0x26, 0x00, 0x03, 0x00, 0x00,
        0x00, 0xAD, 0x7C, 0x8F, 0x10, 0x2D, 0x26, 0x93, 0x10, 0x2B, 0x52, 0xA6, 0x00, 0x0A, 0x00, 0x01,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    };
    // kms2 recording: packet 598, 40 bytes (names masked with '*' by the masking pass).
    static readonly byte[] KmsAdd = new byte[] {
        0x00, 0xEC, 0x6A, 0xAD, 0x00, 0x43, 0xDB, 0x83, 0x02, 0xEC, 0x6A, 0xAD, 0x00, 0xCD, 0x15, 0x88,
        0x72, 0xCD, 0x15, 0x88, 0x72, 0x1C, 0xE1, 0xF5, 0x05, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    };

    [Test] public void Gms2AddCarriesTheStateAndShield() {
        var r = new ByteReader(Gms2Add);
        var b = BuffPacket.Read(r);
        Assert.That((b.Mode, b.OwnerObjectId, b.Key, b.CasterObjectId), Is.EqualTo(((byte) 0, 442495, 804678, 442495)));
        Assert.That(b.State, Is.EqualTo(new BuffState(115378326, 115378326, 100000028, 8, 1, 1)));
        Assert.That(b.Shield, Is.EqualTo(0L));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void Gms2RemoveIsTheHeaderOnly() {
        var r = new ByteReader(Gms2Remove);
        var b = BuffPacket.Read(r);
        Assert.That((b.Mode, b.OwnerObjectId, b.Key, b.CasterObjectId), Is.EqualTo(((byte) 1, 3639778, 3494021, 3616168)));
        Assert.That((b.State, b.Shield), Is.EqualTo(((BuffState?) null, (long?) null)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void Gms2ModifyFlagsSelectTheStateAndShield() {
        var r = new ByteReader(Gms2Modify);
        var b = BuffPacket.Read(r);
        Assert.That((b.Mode, b.Flags), Is.EqualTo(((byte) 2, 3u)));
        Assert.That((b.OwnerObjectId, b.Key), Is.EqualTo((2509810, 1000394)));
        Assert.That(b.State, Is.EqualTo(new BuffState(277839021, 278079021, 10900011, 10, 1, 1)));
        Assert.That(b.Shield, Is.EqualTo(0L));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void KmsAddCarriesTheSameLayout() {
        var r = new ByteReader(KmsAdd);
        var b = BuffPacket.Read(r);
        Assert.That((b.Mode, b.OwnerObjectId, b.Key, b.CasterObjectId), Is.EqualTo(((byte) 0, 11365100, 42195779, 11365100)));
        Assert.That(b.State, Is.EqualTo(new BuffState(1921521101, 1921521101, 100000028, 1, 1, 1)));
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
