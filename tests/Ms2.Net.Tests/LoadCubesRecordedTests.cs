using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// LoadCubes (KMS 0x6A, GMS2 0x6C): CSceneStateGame::OnLoadCubes (scenestategame.cpp:10857-10944), handled by
/// <see cref="CubeCellMap.Load"/> (mode 0 house cubes) and <see cref="CubePlots.Apply"/> (modes 1-3 plot states, owners and sales).
/// </summary>
public class LoadCubesRecordedTests {
    // gms2 recording: 0x6C mode 0 (2 cubes), packet 1933, 108 bytes (extracted with extract.py; owner masked with 0x11, instance uid with 0x22)
    static readonly byte[] Gms2Cubes = Convert.FromHexString(
        "000002000000F5F1010011111111111111116E84FF022222222222222222000000000000000000010000000000000000" +
        "000087430000000000F2F407001111111111111111BE0B01032222222222222222000000000000000000010000000000" +
        "000000000087430000000000");
    // gms2 recording: 0x6C mode 1 (plot states), packet 2267, 10 bytes (extracted with extract.py): plot 1
    static readonly byte[] Gms2PlotStates = Convert.FromHexString("01010000000100000000");
    // gms2 recording: 0x6C mode 2 (plot owners), packet 2275, 51 bytes (extracted with extract.py; name masked with '*', owner id with 0x11)
    static readonly byte[] Gms2PlotOwners = Convert.FromHexString(
        "02010000000C000000000000000E002A002A002A002A002A002A002A002A002A002A002A002A002A002A001111111111111111");
    // gms2 recording: 0x6C mode 3 (plot sales), packet 2253, 22 bytes (extracted with extract.py): plot 12, block 0, state 2
    static readonly byte[] Gms2PlotSales = Convert.FromHexString("03010000000C00000000000000025E119E5E00000000");

    // kms2 recording: 0x6A mode 0 (4 cubes, one with masked 3-char notice), packet 563, 219 bytes
    // (extracted with extract.py; owner masked with 0x11, instance uid with 0x22)
    static readonly byte[] KmsCubes = Convert.FromHexString(
        "000004000000EAE9010011111111111111119E0B01032222222222222222000000000000000000010000000000000000" +
        "0000B4420000000000EAEA010011111111111111111A0C01032222222222222222000000000000000000010000000000" +
        "0000000000B4420000000000EBEA01001111111111111111F20B01032222222222222222000000000000000000010000" +
        "00000000000000000000000000000103002A002A002A0001EFEF01001111111111111111BE0B01032222222222222222" +
        "000000000000000000010000000000000000000034430000000000");
    // kms2 recording: 0x6A mode 1 (plot states), packet 564, 10 bytes (extracted with extract.py): plot 1
    static readonly byte[] KmsPlotStates = Convert.FromHexString("01010000000100000001");
    // kms2 recording: 0x6A mode 2 (plot owners), packet 562, 23 bytes (extracted with extract.py; owner id masked with 0x11)
    static readonly byte[] KmsPlotOwners = Convert.FromHexString("0201000000010000000000000000001111111111111111");
    // kms2 recording: 0x6A mode 3 (plot sales), packet 1789, 56 bytes (extracted with extract.py): plots 7, 8, 10
    static readonly byte[] KmsPlotSales = Convert.FromHexString(
        "030300000007000000000000000131630161000000000800000000000000012D7D0361000000000A0000000000000001E31B136100000000");

    [Test] public void Gms2CubesAndPlotsPopulateCubeCellMap() {
        var map = new CubeCellMap();

        var rc = new ByteReader(Gms2Cubes);
        var cubes = LoadCubes.Read(rc);
        Assert.That(cubes, Is.Not.Null);
        Assert.That(cubes.Cubes, Has.Count.EqualTo(2));
        Assert.That((cubes.Cubes[0].Cell, cubes.Cubes[0].Item.ItemId, cubes.Cubes[0].Rotation), Is.EqualTo((127477, 50300014, 270f)));
        Assert.That((cubes.Cubes[1].Cell, cubes.Cubes[1].Item.ItemId, cubes.Cubes[1].Rotation), Is.EqualTo((521458, 50400190, 270f)));
        Assert.That(rc.Available, Is.EqualTo(0));
        Assert.That(map.Load(cubes, _ => true), Is.EqualTo(2));
        Assert.That(map.Count, Is.EqualTo(2));
        Assert.That(map.TryCubeItemAtKey(127477, out var item0) && item0.ItemId == 50300014, Is.True);
        Assert.That(map.TryCubeItemAtKey(521458, out var item1) && item1.ItemId == 50400190, Is.True);

        var r1 = new ByteReader(Gms2PlotStates);
        var p1 = LoadCubesPlots.Read(r1);
        Assert.That(r1.Available, Is.EqualTo(0));
        map.Plots.Apply(p1);
        Assert.That(map.Plots.HasPlot(1), Is.True);

        var r2 = new ByteReader(Gms2PlotOwners);
        var p2 = LoadCubesPlots.Read(r2);
        Assert.That(r2.Available, Is.EqualTo(0));
        map.Plots.Apply(p2);
        var owner = map.Plots.FindOwner(12, 0);
        Assert.That(owner, Is.Not.Null);
        Assert.That((owner.OwnerId, owner.Name.Length), Is.EqualTo((0x1111111111111111L, 14)));
        Assert.That(map.Plots.FindOwnerById(0x1111111111111111L), Is.SameAs(owner));

        var r3 = new ByteReader(Gms2PlotSales);
        var p3 = LoadCubesPlots.Read(r3);
        Assert.That(r3.Available, Is.EqualTo(0));
        map.Plots.Apply(p3);
        Assert.That(map.Plots.SaleState(12, 0), Is.EqualTo(((byte)2, 1587417438L)));
    }

    [Test] public void KmsCubesAndPlotsPopulateCubeCellMap() {
        var map = new CubeCellMap();

        var rc = new ByteReader(KmsCubes);
        var cubes = LoadCubes.Read(rc);
        Assert.That(cubes, Is.Not.Null);
        Assert.That(cubes.Cubes, Has.Count.EqualTo(4));
        Assert.That((cubes.Cubes[0].Cell, cubes.Cubes[0].Item.ItemId, cubes.Cubes[0].Rotation), Is.EqualTo((125418, 50400158, 90f)));
        Assert.That((cubes.Cubes[2].Cell, cubes.Cubes[2].NoticeText, cubes.Cubes[2].NoticeType), Is.EqualTo((125675, "***", (byte)1)));
        Assert.That(rc.Available, Is.EqualTo(0));
        Assert.That(map.Load(cubes, _ => true), Is.EqualTo(4));
        Assert.That(map.Count, Is.EqualTo(4));
        Assert.That(map.TryCubeItemAtKey(125418, out var item0) && item0.ItemId == 50400158, Is.True);
        Assert.That(map.TryCubeItemAtKey(126959, out var item3) && item3.ItemId == 50400190, Is.True);

        var r1 = new ByteReader(KmsPlotStates);
        var p1 = LoadCubesPlots.Read(r1);
        Assert.That(r1.Available, Is.EqualTo(0));
        map.Plots.Apply(p1);
        Assert.That(map.Plots.HasPlot(1), Is.True);

        var r2 = new ByteReader(KmsPlotOwners);
        var p2 = LoadCubesPlots.Read(r2);
        Assert.That(r2.Available, Is.EqualTo(0));
        map.Plots.Apply(p2);
        Assert.That(map.Plots.FindOwner(1, 0)?.OwnerId, Is.EqualTo(0x1111111111111111L));

        var r3 = new ByteReader(KmsPlotSales);
        var p3 = LoadCubesPlots.Read(r3);
        Assert.That(r3.Available, Is.EqualTo(0));
        map.Plots.Apply(p3);
        Assert.That(map.Plots.SaleState(7, 0), Is.EqualTo(((byte)1, 1627480881L)));
        Assert.That(map.Plots.SaleState(10, 0), Is.EqualTo(((byte)1, 1628642275L)));
    }
}
