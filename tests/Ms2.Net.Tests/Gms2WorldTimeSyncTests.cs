using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// SyncWorld (KMS 0x10F, i64 server time, CSceneStateMulti::OnServerTimePacket @0x14174fcc0, scenestatemulti.cpp:766) and
/// WorldTimeSync (KMS 0x110, i32 idle interval, CNetworkService::OnWorldTimeSync @0x14153a8a0, networkservice.cpp:806) on the GMS2
/// profile. The GMS2 recordings carry them at 0x111 (always 8 bytes) and 0x112 (always 4 bytes, 120000 ms), the KMS numbers + 2.
/// The profile mapped SyncWorld to 0x112 and had no WorldTimeSync, so every GMS2 session read the 4-byte idle interval packet as an
/// i64 and threw.
/// </summary>
public class Gms2WorldTimeSyncTests
{
    [Test]
    public void Gms2NumbersAreTheKmsNumbersPlusTwo()
    {
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.SyncWorld), Is.EqualTo((ushort)0x111));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.WorldTimeSync), Is.EqualTo((ushort)0x112));
        Assert.That(ProtocolProfile.Kms2525.Number(Inbound.SyncWorld), Is.EqualTo((ushort)0x10F));
        Assert.That(ProtocolProfile.Kms2525.Number(Inbound.WorldTimeSync), Is.EqualTo((ushort)0x110));
        Assert.That(ProtocolProfile.Gms2.Is(0x111, Inbound.SyncWorld), Is.True);
        Assert.That(ProtocolProfile.Gms2.Is(0x112, Inbound.WorldTimeSync), Is.True);
        Assert.That(ProtocolProfile.Gms2.Is(0x112, Inbound.SyncWorld), Is.False);
    }

    [Test]
    public void TheGms2IdleIntervalPacketReadsAsWorldTimeSync()
    {
        // the first body in the GMS2 recordings at 0x112: i32 120000
        var body = new byte[] { 0xC0, 0xD4, 0x01, 0x00 };
        Assert.That(WorldTimeSync.Read(new ByteReader(body)).IdleIntervalMs, Is.EqualTo(120000));
        // read as the 8-byte SyncWorld, the profile's old mapping of this number, it runs out of bytes
        Assert.That(() => SyncWorldMessage.Read(new ByteReader(body)), Throws.Exception);
    }

    [Test]
    public void EveryGms2RecordingAtTheTwoNumbersHasTheirLayout()
    {
        var sync = Gms2Sniffs.Inbound("gms2", ProtocolProfile.Gms2.Number(Inbound.SyncWorld));
        var idle = Gms2Sniffs.Inbound("gms2", ProtocolProfile.Gms2.Number(Inbound.WorldTimeSync));
        Assert.That(sync, Is.Not.Empty, "no GMS2 SyncWorld recordings");
        Assert.That(idle, Is.Not.Empty, "no GMS2 WorldTimeSync recordings");
        foreach (var (file, data) in sync) Assert.That(SyncWorldMessage.Read(new ByteReader(data)).ServerTime, Is.GreaterThanOrEqualTo(0L), file);
        foreach (var (file, data) in idle) Assert.That(WorldTimeSync.Read(new ByteReader(data)).IdleIntervalMs, Is.GreaterThan(0), file);
    }
}
