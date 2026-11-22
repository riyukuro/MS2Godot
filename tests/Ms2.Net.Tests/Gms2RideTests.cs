using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>RequestRide mode 0 on a ride item: the GMS2 form has no option flag byte.</summary>
public class Gms2RideTests {
    [Test] public void Gms2RideOnItemDropsTheFlagByteAndKmsKeepsIt() {
        var req = new RideOnItemRequest(5, 9, true);
        var k = new ByteWriter(); req.Write(k, ProtocolProfile.Kms2525);
        var g = new ByteWriter(); req.Write(g, ProtocolProfile.Gms2);
        Assert.That(k.ToArray().Length - g.ToArray().Length, Is.EqualTo(1));
        Assert.That(k.ToArray()[10], Is.EqualTo(1));   // op mode, type, id, n20, then the flag
    }
}
