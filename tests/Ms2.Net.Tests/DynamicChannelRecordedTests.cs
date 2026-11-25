using System;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Recorded-packet decode tests for DynamicChannel (KMS 0xA7, GMS2 0xA9;
/// CNetworkService::OnDynamicChannel @0x14153c4b0, networkservice.cpp:977 -> DynamicChannelMessage.Read).
/// Reader-only: LiveSession.Channel.cs stores channelTabs/classTable and raises ChannelsChanged in the Godot layer.
/// </summary>
public class DynamicChannelRecordedTests
{
    // kms2: 0xA7 kind 0, 17 bytes (8 u16 class-table entries)
    static readonly byte[] KmsKind0ClassTable = Convert.FromHexString("000A0064006400640064000A000A000A00");

    // kms2: 0xA7 kind 3, 14 bytes (unknown kind returns null)
    static readonly byte[] KmsKind3Ignored = Convert.FromHexString("0300000000000000000000000000");

    // gms2: 0xA9 kind 0, 17 bytes (8 u16 class-table entries)
    static readonly byte[] Gms2Kind0ClassTable = Convert.FromHexString("000A0002000200020002000A000A000A00");

    [Test]
    public void KmsDynamicChannelReadsClassTableAndIgnoresUnknownKind()
    {
        var r0 = new ByteReader(KmsKind0ClassTable);
        var msg = DynamicChannelMessage.Read(r0);
        Assert.That(r0.Available, Is.EqualTo(0));
        Assert.That(msg, Is.InstanceOf<DynamicChannelMessage.ClassTable>());
        Assert.That(((DynamicChannelMessage.ClassTable)msg).Values,
            Is.EqualTo(new ushort[] { 10, 100, 100, 100, 100, 10, 10, 10 }));

        var ignored = DynamicChannelMessage.Read(new ByteReader(KmsKind3Ignored));
        Assert.That(ignored, Is.Null);
    }

    [Test]
    public void Gms2DynamicChannelReadsClassTableToEnd()
    {
        var r0 = new ByteReader(Gms2Kind0ClassTable);
        var msg = DynamicChannelMessage.Read(r0);
        Assert.That(r0.Available, Is.EqualTo(0));
        Assert.That(msg, Is.InstanceOf<DynamicChannelMessage.ClassTable>());
        Assert.That(((DynamicChannelMessage.ClassTable)msg).Values,
            Is.EqualTo(new ushort[] { 10, 2, 2, 2, 2, 10, 10, 10 }));
    }
}
