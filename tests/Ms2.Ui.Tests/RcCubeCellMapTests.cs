using System.Numerics;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// CubeCellMap's public removal (a removal the host needs): ResponseCube 0x0C clears the cell through CubeMapInformationService
/// FUN_1410c4b50, but Remove is private and only Replace/Load use it (CubeCellMap.cs:160-165), so the host has no call.
/// Expected API: public bool RemoveCube(int key).
/// </summary>
public class RcCubeCellMapTests
{
    [Ignore("CubeCellMap.Remove is private")]
    [Test]
    public void RemoveCubeDropsTheCellAndItsEntity()
    {
        var map = new CubeCellMap();
        int key = CubeCell.Key(4, 5, 6);
        Assert.That(map.AddMapCube((4, 5, 6), new Vector3(600, 750, 900)), Is.True);
        Assert.That((map.Count, map.EntityCount), Is.EqualTo((1, 1)));
        var removed = RcLane.Call(map, "RemoveCube", key);
        Assert.That(removed is bool b && b, Is.True, "RemoveCube reports whether a cube was there");
        Assert.That(map.HasCubeAtKey(key), Is.False);
        Assert.That((map.Count, map.EntityCount), Is.EqualTo((0, 0)));
        Assert.That(RcLane.Call(map, "RemoveCube", key) is bool again && again, Is.False, "a second removal finds none");
    }
}
