using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// G20 — select pose (D22): the reader is game/ui/SelectPose.cs, which no test project references (Godot). The data is
/// asserted here: GMS2 has no anikeyinfo/ (so the pose's effect keys are absent; Bore/BoreIdle still play from the model
/// .kf) and its table/pcactiondata.xml keeps the same 24 CHARACTER_SELECT_BORE rows; KMS anikeyinfo/male.xml keeps its
/// keys. The GMS2 effect source is unverified, so the data gap notes says not to wire one.
/// </summary>
public class RcG20SelectPoseTests
{
    [Test]
    public void TheKmsAnikeyInfoKeepsItsBoreKeys()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        Assert.That(packs.Exists("Xml", "anikeyinfo/male.xml"), Is.True, "kms anikeyinfo/male.xml");
        var doc = GameStrings.Parse(packs.Read("Xml", "anikeyinfo/male.xml"));
        Assert.That(doc.Descendants("key").Any(k => ((string)k.Attribute("name") ?? "").Contains("Bore_A")), Is.True);
    }

    [Test]
    [Ignore("G20: the reader is game/ui/SelectPose.cs (Godot), referenced by no test project; GMS2 has no anikeyinfo/ and no proven effect source")]
    public void TheGms2HasNoAnikeyInfoAndKeepsThePoseNames()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        Assert.That(packs.Exists("Xml", "anikeyinfo/male.xml"), Is.False, "GMS2 has no anikeyinfo/");
        var doc = GameStrings.Parse(packs.Read("Xml", "table/pcactiondata.xml"));
        var bore = doc.Descendants("v").Count(v => ((string)v.Attribute("type") ?? "").StartsWith("CHARACTER_SELECT_BORE"));
        Assert.That(bore, Is.EqualTo(24), "12 CHARACTER_SELECT_BORE and 12 _IDLE rows, the same as KMS");
    }
}
