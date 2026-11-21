using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>MyPC::GetActiveCubeCraft (@0x141530290, mypc.cpp:4516) and the guards of Unk_PCCubeCraft::ConfirmPlacement
/// (@0x1410aad50, cubecraft_2.cpp:936-948).</summary>
public class ConfirmPlacementTests
{
    static CubeCraftState Craft(int cubeMode, int craftMode)
    {
        var c = new CubeCraftState();
        c.NewPc();
        c.ModelAttached();
        c.Set(cubeMode, craftMode, new CubeItemInfo { ItemId = 50100001 }, dead: false);
        return c;
    }

    [Test]
    public void TheActiveCraftNeedsALivePcInCubeMode()
    {
        var c = Craft(2, 2);
        Assert.That(c.Active(false), Is.SameAs(c.Current));
        Assert.That(c.Active(true), Is.Null);
        Assert.That(c.ConfirmPlacement(true), Is.EqualTo(CubeCraftState.PlacementStop.NoCraft));
        Assert.That(new CubeCraftState().ConfirmPlacement(false), Is.EqualTo(CubeCraftState.PlacementStop.NoCraft));
    }

    [Test]
    public void CubeModeOneStopsAtTheConstructionGuideBeforeTheNotice()
    {
        Assert.That(Craft(1, 1).ConfirmPlacement(false), Is.EqualTo(CubeCraftState.PlacementStop.NoConstructionGuide));
        Assert.That(Craft(1, 2).ConfirmPlacement(false), Is.EqualTo(CubeCraftState.PlacementStop.NoConstructionGuide));
    }

    [Test]
    public void CraftModeOneGivesTheBuildNotice()
    {
        Assert.That(Craft(2, 1).ConfirmPlacement(false), Is.EqualTo(CubeCraftState.PlacementStop.BuildNotice));
        Assert.That(CubeCraftState.ModeOneNotice, Is.EqualTo(0x39));
    }

    [Test]
    public void ALiftedCubeStopsAtTheMissingGuide()
    {
        Assert.That(Craft(2, 2).ConfirmPlacement(false), Is.EqualTo(CubeCraftState.PlacementStop.NoGuide));
    }
}
