using System.Text;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

public class MapXBlockTests
{
    const string Block = """
        <?xml version="1.0" encoding="utf-8"?>
        <ms2>
          <fog color="128,235,255" near="0" far="48000" />
          <clientProperty bgDay="BG_A.dds" />
        </ms2>
        """;

    [Test]
    public void ByteOrderMarkIsSkippedAsTinyXmlDoes()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(Block)).ToArray();
        var x = MapXBlock.Parse(bytes);
        Assert.That((string)x.Root.Element("fog").Attribute("far"), Is.EqualTo("48000"));
        Assert.That((string)x.Root.Element("clientProperty").Attribute("bgDay"), Is.EqualTo("BG_A.dds"));
        Assert.That(MapXBlock.Parse(Encoding.UTF8.GetBytes(Block)).Root.Element("fog"), Is.Not.Null);
    }

    // 02020120_bf.xml starts with a byte order mark in both data sets; before, the field lost its fog, sky picture and minimap
    [Test]
    public void FieldWithAByteOrderMarkKeepsFogBackdropAndMinimap()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        var packs = new Ms2.Formats.Pack.PackSet(data);
        var raw = packs.Read(MapXBlock.Archive, MapXBlock.Entry("02020120_bf"));
        Assert.That(raw[..3], Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));
        var x = MapXBlock.Load(packs, "02020120_bf");
        Assert.That(x.Root.Element("fog"), Is.Not.Null);
        Assert.That(x.Root.Element("heightfog"), Is.Not.Null);
        Assert.That((string)x.Root.Element("clientProperty")?.Attribute("bgDay"), Is.Not.Empty);
        Assert.That(MinimapInfo.Parse(x)?.ImageEntry, Is.EqualTo("map/minimap/02020120.dds"));
    }
}
