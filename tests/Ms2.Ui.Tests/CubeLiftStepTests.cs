using System.Numerics;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The wheel's lift step in cube mode: Unk_PCCubeCraft::FUN_1410ac1c0 / FUN_1410ac380 (cubecraft_2.cpp:1178-1222)
/// with FUN_1410ae960's limits (:1407-1450) and AdjustLift (:590-596).</summary>
public class CubeLiftStepTests
{
    sealed class Owner : ICubeCraftOwner
    {
        public bool IsLocal { get; set; } = true;
        public Vector3 Point { get; set; } = new(-1200f, -300f, 2251f);
        public float RotZ { get; set; }
        public float TargetYaw { get; set; }
        public int StateId { get; set; } = 1;
        public ICubeCells Cubes { get; set; }
    }

    /// <summary>The ground: one cube column up to the given cell (the map's cubes the craft's FreeCell tests).</summary>
    sealed class Ground(int x, int y, int top) : ICubeCells
    {
        public bool HasCubeAtKey(int key) => CubeCell.Unpack(key) is var (cx, cy, cz) && cx == x && cy == y && cz <= top;
    }

    static CubeCraftState Craft(int craftMode, Owner owner)
    {
        var c = new CubeCraftState { Owner = owner };
        c.NewPc();
        c.ModelAttached();
        c.Set(2, craftMode, new CubeItemInfo { ItemId = 30000772, InstanceId = 1, Count = 1 }, dead: false);
        return c;
    }

    [Test]
    public void ACarriedLiftGoesUpAndDownByACellWithinTheLimits()
    {
        var c = Craft(2, new Owner());
        Assert.That(c.LiftStep(true, 2, 2), Is.EqualTo(0));
        Assert.That(c.LiftStep(true, 2, 2), Is.EqualTo(0));
        Assert.That(c.Current.Lift, Is.EqualTo(300f));
        // two cells above the one in front at 0: CubeLiftHeightLimitUp 2 refuses the third, with common string 0x6f4
        Assert.That(c.LiftStep(true, 2, 2), Is.EqualTo(CubeCraftState.LiftUpRefusedText));
        Assert.That(c.Current.Lift, Is.EqualTo(300f));
        for (int i = 0; i < 4; i++) Assert.That(c.LiftStep(false, 2, 2), Is.EqualTo(0));
        Assert.That(c.Current.Lift, Is.EqualTo(-300f));
        Assert.That(c.LiftStep(false, 2, 2), Is.EqualTo(CubeCraftState.LiftDownRefusedText));
    }

    [Test]
    public void TheCellInFrontFollowsTheLift()
    {
        var c = Craft(2, new Owner { RotZ = 90f, Cubes = new Ground(-7, -2, 14) });   // facing +x; the ground's top cube at 2100-2250
        c.Update();
        Assert.That(c.Current.Cell.Z, Is.EqualTo(2250f));
        c.LiftStep(true, 2, 2);
        c.LiftStep(true, 2, 2);
        c.Update();
        Assert.That(c.Current.Cell, Is.EqualTo(new Vector3(-1050f, -300f, 2400f)));
    }

    [Test]
    public void NoStepOutsideACarriedLiftOrForAnotherPc()
    {
        Assert.That(Craft(0, new Owner()).LiftStep(true, 2, 2), Is.EqualTo(CubeCraftState.LiftUpRefusedText));   // STILL-OPEN modes refuse
        Assert.That(Craft(2, new Owner { IsLocal = false }).LiftStep(true, 2, 2), Is.EqualTo(0));
        Assert.That(new CubeCraftState().LiftStep(true, 2, 2), Is.EqualTo(0));
    }
}
