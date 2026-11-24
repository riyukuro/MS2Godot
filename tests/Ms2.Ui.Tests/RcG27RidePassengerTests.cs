using Ms2.Formats.Pack;
using Ms2Godot.Movement;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// G27 — ride passenger rows (D21): GMS2 moves the passenger out of riding/50600208.xml into riding/passenger/50600208.xml
/// (&lt;ridepassenger passengerid="0" rideBone="Bip01 Spine02" rideTranslation="0,10,60" rideRotation="90,0,90"
/// rideAniPC="Ride_Idle_A"&gt;); KMS keeps &lt;passengers&gt;&lt;passenger&gt;
/// in the main file; RideData.Load reads the passenger file when the row has none. The file is linked into this project, as the Net tests link their game sources.
/// </summary>
public class RcG27RidePassengerTests
{
    [Test]
    public void TheKmsPassengerIsUnchanged()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var seat = RideData.Load(packs, 50600208).Passenger(0);
        Assert.That(seat, Is.Not.Null, "kms riding/50600208.xml <passengers>");
        Assert.That((seat.RideBone, seat.Translation, seat.Rotation),
            Is.EqualTo(("Bip01 Spine02", new float[] { 0, 10, 60 }, new float[] { 90, 0, 90 })));
    }

    [Test]
    public void TheGms2PassengerFileGivesTheSeat()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var seat = RideData.Load(packs, 50600208).Passenger(0);
        Assert.That(seat, Is.Not.Null, "gms/riding/passenger/50600208.xml");
        Assert.That(seat.RideBone, Is.EqualTo("Bip01 Spine02"));
        Assert.That(seat.Translation, Is.EqualTo(new float[] { 0, 10, 60 }));
        Assert.That(seat.Rotation, Is.EqualTo(new float[] { 90, 0, 90 }));
        Assert.That(seat.Sequences.Idle, Is.EqualTo("Ride_Idle_A"));
    }
}
