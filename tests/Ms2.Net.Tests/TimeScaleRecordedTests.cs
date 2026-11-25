using System;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Recorded-packet decode and handler-effect tests for TimeScale (KMS 0xF3, GMS2 0xF5;
/// CSceneStateGame::OnTimeScale @0x14173a5e0, scenestategame.cpp:8465 -> TimeScaleRamp.Start/Stop).
/// </summary>
public class TimeScaleRecordedTests
{
    // kms2: 0xF3, 14 bytes (Start=true, From=0.5f, To=0.3f, DurationMs=15f, Curve=1)
    static readonly byte[] KmsStartLinear = Convert.FromHexString("010000003F9A99993E0000704101");

    // kms2: 0xF3, 14 bytes (Start=false, From=1.0f, To=1.0f, DurationMs=3.0f, Curve=0)
    static readonly byte[] KmsStop = Convert.FromHexString("000000803F0000803F0000404000");

    // gms2: 0xF5, 14 bytes (Start=true, From=1.0f, To=0.1f, DurationMs=10f, Curve=1)
    static readonly byte[] Gms2StartLinear = Convert.FromHexString("010000803FCDCCCC3D0000204101");

    // gms2: 0xF5, 14 bytes (Start=false, From=1.0f, To=1.0f, DurationMs=3.0f, Curve=0)
    static readonly byte[] Gms2Stop = Convert.FromHexString("000000803F0000803F0000404000");

    static void VerifyStartRampAndStop(byte[] startBytes, byte[] stopBytes, float expectedFrom, float expectedTo, float expectedDuration)
    {
        var start = TimeScale.Read(new ByteReader(startBytes));
        Assert.That(start.Start, Is.True);
        Assert.That(start.From, Is.EqualTo(expectedFrom).Within(1e-6f));
        Assert.That(start.To, Is.EqualTo(expectedTo).Within(1e-6f));
        Assert.That(start.DurationMs, Is.EqualTo(expectedDuration).Within(1e-6f));
        Assert.That(start.Curve, Is.EqualTo(1));

        var ramp = new TimeScaleRamp();
        const uint t0 = 1000;
        Assert.That(ramp.Start(start.From, start.To, start.DurationMs, start.Curve, t0), Is.True);
        Assert.That(ramp.Running, Is.True);
        Assert.That(ramp.Scale, Is.EqualTo(start.From).Within(1e-6f));

        // Halfway through the linear ramp, Tick moves the scale between From and To.
        uint half = t0 + (uint)((int)start.DurationMs / 2);
        Assert.That(ramp.Tick(half), Is.True);
        float expectedMid = start.To * ((half - t0) / (float)(int)start.DurationMs) +
                            (1f - (half - t0) / (float)(int)start.DurationMs) * start.From;
        Assert.That(ramp.Scale, Is.EqualTo(expectedMid).Within(1e-5f));

        var stop = TimeScale.Read(new ByteReader(stopBytes));
        Assert.That(stop.Start, Is.False);
        Assert.That(ramp.Stop(), Is.True);
        Assert.That(ramp.Running, Is.False);
        Assert.That(ramp.Scale, Is.EqualTo(1f));
        Assert.That(ramp.Stop(), Is.False);
    }

    [Test]
    public void KmsTimeScaleStartsRampsAndStops()
        => VerifyStartRampAndStop(KmsStartLinear, KmsStop, expectedFrom: 0.5f, expectedTo: 0.3f, expectedDuration: 15f);

    [Test]
    public void Gms2TimeScaleStartsRampsAndStops()
        => VerifyStartRampAndStop(Gms2StartLinear, Gms2Stop, expectedFrom: 1.0f, expectedTo: 0.1f, expectedDuration: 10f);
}
