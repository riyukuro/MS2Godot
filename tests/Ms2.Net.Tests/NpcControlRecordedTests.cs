using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>NpcControl (0x59 GMS2, 0x58 KMS2): an i16 count, then per block an i16 size and the block itself. None of these
/// recorded blocks is a boss (the block parses without the i32 owner, and the reader's end check holds), so the layout
/// lookup answers false. The tutorial block ends in a 4-byte tail the reader keeps verbatim.</summary>
public class NpcControlRecordedTests {
    // gms2 recording: packet 2505, 30 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2 = new byte[] {
        0x01, 0x00, 0x1A, 0x00, 0x88, 0xC0, 0x06, 0x00, 0x00, 0xC2, 0x01, 0x2C, 0x01, 0x19, 0x04, 0x4E,
        0x0C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x64, 0x00, 0x16, 0xFF, 0xFF, 0x25, 0x00,
    };
    // gms2 recording: packet 2718, 34 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2Tutorial = new byte[] {
        0x01, 0x00, 0x1E, 0x00, 0x35, 0xE9, 0x2B, 0x00, 0x00, 0x8C, 0x0A, 0x12, 0xFD, 0xAF, 0x04, 0xCA,
        0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x64, 0x00, 0x11, 0xFF, 0xFF, 0x02, 0x00, 0x65, 0x00,
        0x00, 0x00,
    };
    // kms2 recording: packet 506, 30 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Kms = new byte[] {
        0x01, 0x00, 0x1A, 0x00, 0xD4, 0x6A, 0x00, 0x00, 0x00, 0x8C, 0x0A, 0x2C, 0x01, 0x71, 0x06, 0x8C,
        0x0A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x64, 0x00, 0x01, 0x03, 0x00, 0x28, 0x2E,
    };

    [Test] public void Gms2BlockReadsPositionRotationAndState() {
        var r = new ByteReader(Gms2);
        var c = NpcControl.Read(r, _ => false);
        Assert.That(c.Blocks, Has.Count.EqualTo(1));
        var b = c.Blocks[0];
        Assert.That((b.ObjectId, b.Flags), Is.EqualTo((442504, (byte) 0)));
        Assert.That((b.Position.X, b.Position.Y, b.Position.Z), Is.EqualTo(((short) 450, (short) 300, (short) 1049)));
        Assert.That(b.RotationZ, Is.EqualTo(315f));
        Assert.That((b.Velocity.X, b.Velocity.Y, b.Velocity.Z), Is.EqualTo(((short) 0, (short) 0, (short) 0)));
        Assert.That(b.AnimSpeed, Is.EqualTo(1f));
        Assert.That((b.State, b.Sequence, b.Serial), Is.EqualTo(((byte) 22, (short) -1, (ushort) 37)));
        Assert.That(b.Tail, Is.Empty);
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void Gms2TutorialBlockKeepsItsTail() {
        var r = new ByteReader(Gms2Tutorial);
        var c = NpcControl.Read(r, _ => false);
        var b = c.Blocks.Single();
        Assert.That((b.ObjectId, b.State, b.Sequence, b.Serial), Is.EqualTo((2877749, (byte) 17, (short) -1, (ushort) 2)));
        Assert.That(b.Tail, Has.Length.EqualTo(4));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void KmsBlockReadsPositionRotationAndState() {
        var r = new ByteReader(Kms);
        var c = NpcControl.Read(r, _ => false);
        var b = c.Blocks.Single();
        Assert.That(b.ObjectId, Is.EqualTo(27348));
        Assert.That((b.Position.X, b.Position.Y, b.Position.Z), Is.EqualTo(((short) 2700, (short) 300, (short) 1649)));
        Assert.That(b.RotationZ, Is.EqualTo(270f));
        Assert.That(b.AnimSpeed, Is.EqualTo(1f));
        Assert.That((b.State, b.Sequence, b.Serial), Is.EqualTo(((byte) 1, (short) 3, (ushort) 11816)));
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
