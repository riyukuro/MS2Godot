using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>UserChat (0x1D): the account and character ids, the name, the text or common string, the type, the gm and gender
/// fields. Every name and chat text was masked with '*', so only their lengths are asserted; no player-authored text
/// is embedded.</summary>
public class ChatRecordedTests {
    // gms2 recording: packet 2570, 79 bytes (names masked with '*' by the masking pass; account/character id bytes replaced with 0x11).
    static readonly byte[] Gms2 = new byte[] {
        0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11,
        0x0B, 0x00, 0x2A, 0x00, 0x2A, 0x00, 0x2A, 0x00, 0x2A, 0x00, 0x2A, 0x00, 0x2A, 0x00, 0x2A, 0x00,
        0x2A, 0x00, 0x2A, 0x00, 0x2A, 0x00, 0x2A, 0x00, 0x00, 0x0D, 0x00, 0x2A, 0x00, 0x2A, 0x00, 0x2A,
        0x00, 0x2A, 0x00, 0x2A, 0x00, 0x2A, 0x00, 0x2A, 0x00, 0x2A, 0x00, 0x2A, 0x00, 0x2A, 0x00, 0x2A,
        0x00, 0x2A, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00,
    };
    // kms2 recording: packet 268, 37 bytes (names masked with '*' by the masking pass; account/character id bytes replaced with 0x11).
    static readonly byte[] Kms = new byte[] {
        0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11,
        0x02, 0x00, 0x2A, 0x00, 0x2A, 0x00, 0x00, 0x01, 0x00, 0x7E, 0x00, 0x0B, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x00, 0x00, 0x00,
    };

    [Test] public void Gms2ChatReadsTheLine() {
        var r = new ByteReader(Gms2);
        var c = ChatMessage.Read(r);
        Assert.That((c.AccountId, c.CharacterId), Is.EqualTo((0x1111111111111111L, 0x1111111111111111L)), "the recorded ids are masked");
        Assert.That(c.Name, Has.Length.EqualTo(11));
        Assert.That(c.Common, Is.False);
        Assert.That(c.Text, Has.Length.EqualTo(13));
        Assert.That((c.Type, c.Gm, c.Gender), Is.EqualTo((0, false, 2)));
        Assert.That((c.WhisperWindow, c.ClubId, c.ItemId, c.ShowImage), Is.EqualTo(("", 0L, 0, (byte) 0)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void KmsChatReadsTheLine() {
        var r = new ByteReader(Kms);
        var c = ChatMessage.Read(r);
        Assert.That((c.AccountId, c.CharacterId), Is.EqualTo((0x1111111111111111L, 0x1111111111111111L)), "the recorded ids are masked");
        Assert.That(c.Name, Has.Length.EqualTo(2));
        Assert.That(c.Text, Has.Length.EqualTo(1));
        Assert.That((c.Type, c.Gender), Is.EqualTo((11, 1)));
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
