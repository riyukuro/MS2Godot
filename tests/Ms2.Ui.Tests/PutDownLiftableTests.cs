using System.Collections.Generic;
using System.Numerics;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The cube cell math (cubemapinformationservice_3.cpp:431-459, ugcmapdata_1.cpp:113, 272-279) and
/// PutDownLiftableAction::vfunc_1 (@0x14095aa70, actionkeyobject.cpp:1972-1997).</summary>
public class PutDownLiftableTests
{
    sealed class Cells : ICubeCells
    {
        public readonly HashSet<int> Keys = new();
        public bool HasCubeAtKey(int key) => Keys.Contains(key);
    }

    static LiftableTargetBoxes Boxes(string pos)
    {
        var t = new LiftableTargetBoxes();
        t.AttachField(new List<FieldEntity> { FieldObjectStatesTests.TargetBox("box1", "1", pos) });
        return t;
    }

    static CubeCraftState InCubeMode(int craftMode)
    {
        var c = new CubeCraftState();
        c.NewPc();
        c.ModelAttached();
        c.Set(1, craftMode, new CubeItemInfo { ItemId = 50100001 }, dead: false);
        return c;
    }

    [Test]
    public void TheCellTruncatesTowardZero()
    {
        Assert.That(CubeCell.Position(new Vector3(149.9f, -10f, 300f)), Is.EqualTo(new Vector3(0f, 0f, 300f)));
        Assert.That(CubeCell.Position(new Vector3(-151f, 450f, 299f)), Is.EqualTo(new Vector3(-150f, 450f, 150f)));
        Assert.That(CubeCell.Key(-1, 0, 2), Is.EqualTo(0xff + (2 << 16)));
        Assert.That(CubeCell.Key(new Vector3(-151f, 300f, 0f)), Is.EqualTo(0xff + (2 << 8)));
    }

    [Test]
    public void TheCellInFrontGoesOneCellAheadAndOneMoreDownWhenNegative()
    {
        // rotation 0 faces -y (FUN_140a01250): (0, -150, 0) + (75, 75, 0) = (75, -75), y < 0 so -225
        var p = CubeCell.InFrontOf(Vector3.Zero, 0f, 0f, 1);
        Assert.That(p.X, Is.EqualTo(75f));
        Assert.That(p.Y, Is.EqualTo(-225f));
        Assert.That(CubeCell.Position(p), Is.EqualTo(new Vector3(0f, -150f, 0f)));
        // rotation 90 faces +x
        Assert.That(CubeCell.Position(CubeCell.InFrontOf(new Vector3(10f, 10f, 160f), 90f, 0f, 1)), Is.EqualTo(new Vector3(150f, 0f, 150f)));
        Assert.That(CubeCell.InFrontOf(Vector3.Zero, 90f, 30f, 0).Z, Is.EqualTo(30f));
    }

    [Test]
    public void ATargetBoxInTheCellInFrontApplies()
    {
        var c = InCubeMode(1);
        Assert.That(PutDownLiftable.Poll(c, 2, Vector3.Zero, 0f, new Cells(), Boxes("75, -200, 30")), Is.True);
        Assert.That(PutDownLiftable.Poll(c, 2, Vector3.Zero, 0f, new Cells(), Boxes("75, 200, 30")), Is.False);
        Assert.That(PutDownLiftable.Poll(c, 2, Vector3.Zero, 90f, new Cells(), Boxes("75, -200, 30")), Is.False);
    }

    [Test]
    public void ACubeInTheCellInFrontRefuses()
    {
        var cells = new Cells();
        cells.Keys.Add(CubeCell.Key(0, -1, 0));
        Assert.That(PutDownLiftable.Poll(InCubeMode(1), 2, Vector3.Zero, 0f, cells, Boxes("75, -200, 30")), Is.False);
    }

    [Test]
    public void AHeldLiftInStateOneAppliesAnywhere()
    {
        var none = new LiftableTargetBoxes();
        Assert.That(PutDownLiftable.Poll(InCubeMode(2), 1, Vector3.Zero, 0f, new Cells(), none), Is.True);
        Assert.That(PutDownLiftable.Poll(InCubeMode(2), 2, Vector3.Zero, 0f, new Cells(), none), Is.False);
        Assert.That(PutDownLiftable.Poll(InCubeMode(1), 1, Vector3.Zero, 0f, new Cells(), none), Is.False);
    }
}
