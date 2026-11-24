using System.Xml.Linq;
using Ms2.Formats.Options;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

public class GraphicOptionShadowTests
{
    /// <summary>A shadow row's levels: the first option that is 1, the first that is 2; a missing attribute repeats the last
    /// value read, across rows; no 2 = 5, no 1 = 0.</summary>
    [Test]
    public void ShadowRowsGiveFirstSimpleAndRealLevels()
    {
        var t = GraphicOptionTable.Parse(XDocument.Parse(
            "<ms2><option name=\"mirror\" option1=\"0\" option2=\"0\" option3=\"1\"/>" +
            "<shadow name=\"myPC\" option1=\"0\" option2=\"1\" option3=\"1\" option4=\"2\" option5=\"2\"/>" +
            "<shadow name=\"mapObject\" option1=\"0\" option2=\"0\" option3=\"2\"/>" +
            "<shadow name=\"dropItem\" option2=\"1\"/>" +
            "<shadow name=\"none\" option1=\"0\" option2=\"0\" option3=\"0\" option4=\"0\" option5=\"0\"/></ms2>"));
        Assert.That((t.ShadowOptionOffset("myPC"), t.ShadowOption("myPC")), Is.EqualTo((1, 3)));
        Assert.That((t.ShadowOptionOffset("mapObject"), t.ShadowOption("mapObject")), Is.EqualTo((0, 2)));
        // option1 missing: still 2 from mapObject's option5 (carried), so level 0 is the real shadow; then 1 from option2
        Assert.That((t.ShadowOptionOffset("dropItem"), t.ShadowOption("dropItem")), Is.EqualTo((1, 0)));
        Assert.That((t.ShadowOptionOffset("none"), t.ShadowOption("none")), Is.EqualTo((0, 5)));
        Assert.That((t.ShadowOptionOffset("absent"), t.ShadowOption("absent")), Is.EqualTo((0, 5)));
        Assert.That(t.IsMirrorSupported(2), Is.True);
    }

    /// <summary>The shipped table: GetShadowOption(5) (mapObject 0,0,2,2,2) = 2; myPC (0,1,1,2,2) simple from 1, real from 3.
    /// The plaza fields have no optionHighLevel; 61000023 (MassiveHideAndSeek) has 3.</summary>
    [Test]
    public void ShippedTableAndFieldLevels()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        using var packs = new PackSet(data);
        var t = GraphicOptionTable.Parse(XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/graphicoption.xml")).TrimStart('﻿')));
        Assert.That(t.ShadowOption(GraphicOptionTable.MapObject), Is.EqualTo(2));
        Assert.That((t.ShadowOptionOffset(GraphicOptionTable.MyPC), t.ShadowOption(GraphicOptionTable.MyPC)), Is.EqualTo((1, 3)));
        Assert.That(FieldData.FindByXBlock(packs, "02000001_tw_tria").ShadowOptionHighLevel, Is.EqualTo(0));
        // KMS table/fielddata.xml id 61000023 (61000023_ME) sets <shadow optionHighLevel="3"/>;
        // GMS2 map/61000023.xml has <shadow intensity="0.3" simpleshadow="0.5"/> (searched all 1686 map/*.xml files
        // and all 115322 entries in GMS2 Xml.m2d for optionHighLevel: 0 matches; <shadow> only has intensity/simpleshadow).
        Assert.That(FieldData.XBlockOf(packs, 61000023) is { } xb ? FieldData.FindByXBlock(packs, xb).ShadowOptionHighLevel : -1,
            Is.EqualTo(RcGap.IsKr(packs) ? 3 : 0));
    }
}
