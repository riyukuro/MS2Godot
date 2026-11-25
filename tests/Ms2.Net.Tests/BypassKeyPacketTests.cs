using System;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Hand-built decode tests for BypassKey (KMS 0xE6, GMS2 0xE8;
/// CNetworkService::OnBypassKey @0x14153b3b0, networkservice.cpp:891-895).
/// The entire 12-byte wire body consists of session tokens (i32 clientValue, i64 platformToken),
/// so every byte below is synthetic rather than taken from a capture.
/// Reader-only: LiveSession.Session.cs reads and logs BypassKey as not ported.
/// </summary>
public class BypassKeyPacketTests
{
    // Hand-built 12-byte body matching the KMS 0xE6 layout (i32 clientValue, i64 platformToken).
    static readonly byte[] KmsLayoutBody = Convert.FromHexString("040302014433221100000000");

    // Hand-built 12-byte body matching the GMS2 0xE8 layout (i32 clientValue, i64 platformToken).
    static readonly byte[] Gms2LayoutBody = Convert.FromHexString("080706058877665500000000");

    [Test]
    public void KmsBypassKeyLayoutReadsClientValueAndPlatformTokenToEnd()
    {
        var r = new ByteReader(KmsLayoutBody);
        var key = BypassKey.Read(r);
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(key.ClientValue, Is.EqualTo(0x0102_0304));
        Assert.That(key.PlatformToken, Is.EqualTo(0x0000_0000_1122_3344L));
    }

    [Test]
    public void Gms2BypassKeyLayoutReadsClientValueAndPlatformTokenToEnd()
    {
        var r = new ByteReader(Gms2LayoutBody);
        var key = BypassKey.Read(r);
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(key.ClientValue, Is.EqualTo(0x0506_0708));
        Assert.That(key.PlatformToken, Is.EqualTo(0x0000_0000_5566_7788L));
    }
}
