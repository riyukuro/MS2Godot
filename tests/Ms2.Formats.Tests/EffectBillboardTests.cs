using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>The effect scene keeps an NiBillboardNode's face mode (EffectNode.BillboardMode, BillboardFace.ModeOf). The healing
/// spot's light shafts (bg/common/eff_com_recovery.nif, region skill 70000018) hang their mesh from the NiBillboardNode
/// lighit06, flags 9: ROTATE_ABOUT_UP with the update-controllers bit. The effect runtime turned every billboard's X axis to
/// the camera, so the shafts stood at the wrong angle.</summary>
public class EffectBillboardTests
{
    const string Archive = "Resource/Model/Effect";

    [Test]
    public void RecoveryShaftsHangFromARotateAboutUpBillboard()
    {
        var (packs, spec) = NifEnv.Open();
        var sc = EffectScene.Load(NifFile.Load(spec, packs.Read(Archive, "bg/common/eff_com_recovery.nif")));
        var bb = sc.Nodes.Single(n => n.Name == "lighit06");
        Assert.That(bb.Billboard, Is.True);
        Assert.That(bb.BillboardMode, Is.EqualTo(BillboardFace.RotateAboutUp));
        Assert.That(sc.Nodes.Where(n => !n.Billboard).All(n => n.BillboardMode == -1), "plain nodes carry no mode");
    }
}
