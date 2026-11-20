using System.Xml.Linq;
using Ms2.Formats.Model;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Swf.Tests;

public class MinimapInfoTests
{
    // a minimap block shaped like the client's: 45-degree isometric camera, image rectangle inside a 1920x1200 screen
    const string Xml = """
        <ms2><minimap>
          <image name="./Data/Resource/Image/Map/Minimap/00000001.dds" left="455.75" right="1479.75" top="50" bottom="1074" />
          <frustum left="-16013" right="16013" top="10008.1" bottom="-10008.1" near="10" far="100000" ortho="true" />
          <screen left="0" right="1920" top="0" bottom="1200" />
          <camera position="-13474.386719, -13366.070313, 19641.191406" rotation="90.000000, 45.000000, -45.000000" />
        </minimap></ms2>
        """;

    [Test]
    public void AxesAreIsometric()
    {
        var (right, fwd, up) = MinimapInfo.Parse(XDocument.Parse(Xml)).Axes();
        Assert.That(right.X, Is.EqualTo(0.7071).Within(1e-3)); Assert.That(right.Y, Is.EqualTo(-0.7071).Within(1e-3)); Assert.That(right.Z, Is.EqualTo(0).Within(1e-3));
        Assert.That(fwd.Z, Is.EqualTo(-0.7071).Within(1e-3));
        Assert.That(up.X, Is.EqualTo(0.5).Within(1e-3)); Assert.That(up.Z, Is.EqualTo(0.7071).Within(1e-3));
    }

    [Test]
    public void ProjectsOntoImage()
    {
        var m = MinimapInfo.Parse(XDocument.Parse(Xml));
        Assert.That(m.ImageEntry, Is.EqualTo("map/minimap/00000001.dds"));
        var (u, v) = m.ToImage(new Vec3(-900, -900, 3150));
        Assert.That(u * 1024, Is.EqualTo(509).Within(2));
        Assert.That(v * 1024, Is.EqualTo(498).Within(2));
    }

    // MinimapViewport::FUN_141486450: screen x minus the image's left edge; false off the frustum
    [Test]
    public void ScreenXIsTheImageColumnOfThePoint()
    {
        var m = MinimapInfo.Parse(XDocument.Parse(Xml));
        Assert.That(m.ToScreenX(new Vec3(-900, -900, 3150), out var x), Is.True);
        Assert.That(x, Is.EqualTo(509).Within(2));
        Assert.That(m.ToScreenX(new Vec3(90000, -90000, 3150), out _), Is.False);
    }
}
