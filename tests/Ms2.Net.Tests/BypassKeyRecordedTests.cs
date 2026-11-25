using System;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Recorded-packet decode tests for BypassKey (KMS 0xE6, GMS2 0xE8;
/// CNetworkService::OnBypassKey @0x14153b3b0, networkservice.cpp:891-895).
/// Reader-only: LiveSession.Session.cs reads and logs BypassKey as not ported.
/// </summary>
public class BypassKeyRecordedTests
{
    // kms2: 0xE6, 12 bytes (i32 clientValue, i64 platformToken; both session tokens masked)
    static readonly byte[] KmsBypassKey = Convert.FromHexString("040302014433221100000000");

    // gms2: 0xE8, 12 bytes (i32 clientValue, i64 platformToken; both session tokens masked)
    static readonly byte[] Gms2BypassKey = Convert.FromHexString("080706058877665500000000");

    [Test]
    public void KmsBypassKeyReadsClientValueAndPlatformTokenToEnd()
    {
        var key = BypassKey.Read(new ByteReader(KmsBypassKey));
        Assert.That(key.ClientValue, Is.EqualTo(0x0102_0304));
        Assert.That(key.PlatformToken, Is.EqualTo(0x0000_0000_1122_3344L));
    }

    [Test]
    public void Gms2BypassKeyReadsClientValueAndPlatformTokenToEnd()
    {
        var key = BypassKey.Read(new ByteReader(Gms2BypassKey));
        Assert.That(key.ClientValue, Is.EqualTo(0x0506_0708));
        Assert.That(key.PlatformToken, Is.EqualTo(0x0000_0000_5566_7788L));
    }
}
