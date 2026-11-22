using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>GMS2 Inbound 0x6C (LoadCubes): every recorded packet reads whole, mode 0 as the cubes and modes 1-3 as the plot states, owners and sale states (skipped without MS2_SNIFFS).</summary>
public class Gms2LoadCubesTests {
    [Test]
    public void EveryRecordedLoadCubesPacketReadsWhole() {
        var all = Gms2Sniffs.Inbound("gms2", 0x6C);
        Assert.That(all, Is.Not.Empty);
        var seen = new HashSet<byte>();
        foreach (var (file, d) in all) {
            var mode = d[0];
            seen.Add(mode);
            if (mode == LoadCubes.ModeCubes) Assert.That(LoadCubes.Read(new ByteReader(d)), Is.Not.Null, file);
            else Assert.That(LoadCubesPlots.Read(new ByteReader(d)), Is.Not.Null, file);
        }
        TestContext.Out.WriteLine("modes seen: " + string.Join(",", seen.OrderBy(m => m)));
    }
}
