using System;
using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Recorded-packet decode and handler-effect tests for Gvg (KMS 0x6D, GMS2 0x6F;
/// CNetworkService::OnGvg @0x14153a8f0, networkservice.cpp:811-832 -> ReconnectionManager.SetOptions).
/// </summary>
public class GvgRecordedTests
{
    // kms2: 0x6D, 83 bytes (4 entries: LaunchFlag, GroupState, DungeonRates, ReconnectionOptions)
    static readonly byte[] KmsGvgFourGroups = Convert.FromHexString(
        "04000000010000000022000000000000000000020000002A0000000000803F0000803F" +
        "2B00000001000000809698002D000000050000000100000000000000010000000F000000020000000000000001000000");

    // kms2: 0x6D, 12 bytes (1 entry: ContentShutdown [0x3A])
    static readonly byte[] KmsGvgShutdown = Convert.FromHexString("010000000700000001003A00");

    // gms2: 0x6F, 83 bytes (4 entries: LaunchFlag, GroupState, DungeonRates, ReconnectionOptions)
    static readonly byte[] Gms2GvgFourGroups = Convert.FromHexString(
        "04000000010000000022000000370000000000020000002A0000000000803F0000803F" +
        "2B00000001000000809698002D000000050000000100000000000000010000000F000000020000000000000001000000");

    // gms2: 0x6F, 16 bytes (1 entry: ContentShutdown [0x15, 0x3C, 0x60])
    static readonly byte[] Gms2GvgShutdown = Convert.FromHexString("0100000007000000030015003C006000");

    static void VerifyFourGroupGvg(byte[] payload, short expectedGroupStateValue0)
    {
        var entries = GvgOptions.ReadAll(new ByteReader(payload));
        Assert.That(entries.Count, Is.EqualTo(4));
        Assert.That(((GvgEntry.LaunchFlag)entries[0]).Enabled, Is.False);
        Assert.That(((GvgEntry.GroupState)entries[1]).Value0, Is.EqualTo(expectedGroupStateValue0));
        Assert.That(((GvgEntry.GroupState)entries[1]).Value2, Is.EqualTo(2));
        Assert.That(((GvgEntry.DungeonRates)entries[2]).RateA, Is.EqualTo(1f));
        Assert.That(((GvgEntry.DungeonRates)entries[2]).RateB, Is.EqualTo(1f));

        var opts = GvgOptions.FindReconnection(new ByteReader(payload));
        Assert.That(opts, Is.Not.Null);

        var host = new RecordedReconnectHost();
        var mgr = new ReconnectionManager(host);
        mgr.OnPacket(new ReconnectNotice(ReconnectNotice.KindContext, new ReconnectionContext(1, "token", 21001), 0));
        Assert.That(mgr.ShouldReconnect(ReconnectionManager.ErrorSocketClosed), Is.False);

        mgr.SetOptions(opts);
        Assert.That(mgr.Options[ReconnectionOptions.Enabled], Is.EqualTo(1));
        Assert.That(mgr.Options[ReconnectionOptions.BufferLimit], Is.EqualTo(10_000_000));
        Assert.That(mgr.Options[ReconnectionOptions.GiveUpSeconds], Is.EqualTo(45));
        Assert.That(mgr.Options[ReconnectionOptions.MaxAttempts], Is.EqualTo(15));
        Assert.That(mgr.Options[ReconnectionOptions.RetryDelaySeconds], Is.EqualTo(2));
        Assert.That(mgr.ShouldReconnect(ReconnectionManager.ErrorSocketClosed), Is.True);
    }

    [Test]
    public void KmsGvgDecodesAllGroupsAndAppliesReconnectionOptions()
    {
        VerifyFourGroupGvg(KmsGvgFourGroups, expectedGroupStateValue0: 0);

        var shutdown = GvgOptions.ReadAll(new ByteReader(KmsGvgShutdown));
        Assert.That(shutdown.Count, Is.EqualTo(1));
        Assert.That(((GvgEntry.ContentShutdown)shutdown[0]).DisabledContents, Is.EqualTo(new ushort[] { 0x3A }));
        Assert.That(GvgOptions.FindReconnection(new ByteReader(KmsGvgShutdown)), Is.Null);
    }

    [Test]
    public void Gms2GvgDecodesAllGroupsAndAppliesReconnectionOptions()
    {
        VerifyFourGroupGvg(Gms2GvgFourGroups, expectedGroupStateValue0: 55);

        var shutdown = GvgOptions.ReadAll(new ByteReader(Gms2GvgShutdown));
        Assert.That(shutdown.Count, Is.EqualTo(1));
        Assert.That(((GvgEntry.ContentShutdown)shutdown[0]).DisabledContents, Is.EqualTo(new ushort[] { 0x15, 0x3C, 0x60 }));
        Assert.That(GvgOptions.FindReconnection(new ByteReader(Gms2GvgShutdown)), Is.Null);
    }
}
