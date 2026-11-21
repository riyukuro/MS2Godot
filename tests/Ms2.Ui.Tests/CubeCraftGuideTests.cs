using System;
using System.Collections.Generic;
using System.Numerics;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>Unk_PCCubeCraft's start (FUN_1410a8af0, cubecraft_2.cpp:340-392), its guide yaw (FUN_1410aa6f0, :921-925) and
/// Update (@0x1410ac9b0, :1530-1590) with FUN_1410aeec0 (:1456-1480).</summary>
public class CubeCraftGuideTests
{
    sealed class Cells : ICubeCells
    {
        public readonly HashSet<int> Keys = new();
        public bool HasCubeAtKey(int key) => Keys.Contains(key);
    }

    sealed class Owner : ICubeCraftOwner
    {
        public bool IsLocal { get; set; } = true;
        public Vector3 Point { get; set; }
        public float RotZ { get; set; }
        public float TargetYaw { get; set; }
        public int StateId { get; set; } = 1;
        public Cells Cells = new();
        public ICubeCells Cubes => Cells;
    }

    static CubeCraftState Craft(Owner o, int cubeMode, int craftMode)
    {
        var c = new CubeCraftState { Owner = o };
        c.NewPc();
        c.ModelAttached();
        c.Set(cubeMode, craftMode, new CubeItemInfo { ItemId = 50100001 }, dead: false);
        return c;
    }

    [Test]
    public void TheGuideYawSnapsUpWithoutASignBranch()
    {
        Assert.That(CubeCraftState.GuideYawOf(0f), Is.EqualTo(0f));
        Assert.That(CubeCraftState.GuideYawOf(1f), Is.EqualTo(90f));
        Assert.That(CubeCraftState.GuideYawOf(-100f), Is.EqualTo(0f));
        Assert.That(CubeCraftState.GuideYawOf(-179f), Is.EqualTo(-90f));
        Assert.That(CubeCraftState.GuideYawOf(200f), Is.EqualTo(270f));
    }

    [Test]
    public void TheStartTakesTheCellInFrontAndTheGuideYaw()
    {
        var o = new Owner { Point = new Vector3(10f, 10f, 160f), RotZ = 90f, TargetYaw = 95f };
        var c = Craft(o, 2, 2);
        Assert.That(c.Current.HasGuide, Is.True);
        Assert.That(c.Current.Cell, Is.EqualTo(new Vector3(150f, 0f, 150f)));
        Assert.That(c.Current.GuideYaw, Is.EqualTo(180f));
        Assert.That(c.Current.Lift, Is.EqualTo(0f));
    }

    [Test]
    public void AWaitingCraftStartsWhenTheModelComes()
    {
        var o = new Owner { Point = Vector3.Zero, RotZ = 0f, TargetYaw = -10f };
        var c = new CubeCraftState { Owner = o };
        c.NewPc();
        c.Set(2, 2, new CubeItemInfo { ItemId = 1 }, dead: false);
        Assert.That(c.Pending, Is.Not.Null);
        c.ModelAttached();
        Assert.That(c.Current.HasGuide, Is.True);
        Assert.That(c.Current.Cell, Is.EqualTo(new Vector3(0f, -150f, 0f)));
        Assert.That(c.Current.GuideYaw, Is.EqualTo(0f));
    }

    [Test]
    public void NoGuideWithoutTheLocalOwnerOrForOtherModes()
    {
        Assert.That(Craft(null, 2, 2).Current.HasGuide, Is.False);
        Assert.That(Craft(new Owner { IsLocal = false }, 2, 2).Current.HasGuide, Is.False);
        Assert.That(Craft(new Owner(), 2, 3).Current.HasGuide, Is.False);
        Assert.That(Craft(new Owner(), 2, 0).Current.HasGuide, Is.True);
    }

    [Test]
    public void UpdateMovesTheCellAndSetsTheYawOnlyOnAMove()
    {
        var o = new Owner { Point = Vector3.Zero, RotZ = 0f, TargetYaw = 0f };
        var c = Craft(o, 2, 0);
        o.TargetYaw = 100f;
        c.Update();
        Assert.That(c.Current.GuideYaw, Is.EqualTo(0f), "same cell: the yaw stays");
        o.RotZ = 90f;
        c.Update();
        Assert.That(c.Current.Cell, Is.EqualTo(new Vector3(150f, 0f, 0f)));
        Assert.That(c.Current.GuideYaw, Is.EqualTo(180f));
    }

    [Test]
    public void UpdateStopsInState0x10InCubeMode1AndOutsideTheCubeMode()
    {
        var o = new Owner { Point = Vector3.Zero, RotZ = 0f };
        var c = Craft(o, 2, 0);
        var cell = c.Current.Cell;
        o.RotZ = 90f;
        o.StateId = 0x10;
        c.Update();
        Assert.That(c.Current.Cell, Is.EqualTo(cell));
        o.StateId = 1;
        var one = Craft(new Owner { Point = Vector3.Zero, RotZ = 0f }, 1, 0);
        ((Owner)one.Owner).RotZ = 90f;
        one.Update();
        Assert.That(one.Current.Cell, Is.EqualTo(Vector3.Zero));
        c.Set(0, 0, null, dead: false);
        c.Update();
        Assert.That(c.Current, Is.Null);
    }

    [Test]
    public void AHeldLiftTakesTheFirstFreeCellOfItsColumn()
    {
        var o = new Owner { Point = new Vector3(0f, 0f, 300f), RotZ = 90f };
        var c = Craft(o, 2, 2);
        o.Point = new Vector3(0f, 0f, 310f);
        o.RotZ = 0f;                                   // the cell in front is (0, -1, 2)
        o.Cells.Keys.Add(CubeCell.Key(0, -1, 1));
        o.Cells.Keys.Add(CubeCell.Key(0, -1, 2));
        c.Update();
        Assert.That(c.Current.Cell, Is.EqualTo(new Vector3(0f, -150f, 450f)));
        Assert.That(CubeCraftState.FreeCell(new Vector3(75f, -225f, 310f), new Cells()), Is.EqualTo(new Vector3(0f, -150f, 150f)));
        var full = new Cells();
        full.Keys.Add(CubeCell.Key(0, -1, 1)); full.Keys.Add(CubeCell.Key(0, -1, 2)); full.Keys.Add(CubeCell.Key(0, -1, 3));
        Assert.That(CubeCraftState.FreeCell(new Vector3(75f, -225f, 310f), full), Is.EqualTo(new Vector3(75f, -225f, 310f)));
    }

    [Test]
    public void UpdateAllocatesLittle()
    {
        var o = new Owner { Point = Vector3.Zero, RotZ = 0f };
        var c = Craft(o, 2, 2);
        Assert.That(Alloc.Least(() => Walk(o, c)), Is.LessThan(512));
    }

    static void Walk(Owner o, CubeCraftState c)
    {
        for (int i = 0; i < 1000; i++) { o.RotZ = i % 4 * 90f; c.Update(); }
    }
}
