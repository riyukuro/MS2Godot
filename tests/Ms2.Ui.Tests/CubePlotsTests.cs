using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The plot tables of CSceneStateGame::OnLoadCubes modes 1-3 (@0x141728110, scenestategame.cpp:10902-10944).</summary>
public class CubePlotsTests
{
    static LoadCubesPlots States(params (int, byte)[] s) => new(LoadCubesPlots.ModeStates, s, [], []);
    static LoadCubesPlots Owners(params LoadCubesPlots.PlotOwner[] o) => new(LoadCubesPlots.ModeOwners, [], o, []);
    static LoadCubesPlots Sales(params LoadCubesPlots.PlotSale[] s) => new(LoadCubesPlots.ModeSales, [], [], s);

    [Test]
    public void ModeOneAddsEachPlotOnceAndDropsItsState()
    {
        // FUN_1410bbc00 (cubemapinformationservice_3.cpp:1006-1013): an empty fd8 entry for a missing plot; the state is unused
        var plots = new CubePlots();
        plots.Apply(States((7, 1), (7, 0), (-3, 1)));
        Assert.That((plots.PlotCount, plots.HasPlot(7), plots.HasPlot(-3), plots.HasPlot(1)), Is.EqualTo((2, true, true, false)));
        Assert.That((plots.FindOwner(7, 0), plots.SaleState(7, 0)), Is.EqualTo(((CubePlots.Owner)null, ((byte, long)?)null)));
    }

    [Test]
    public void ModeTwoOverwritesAPairsOwnerInPlaceAndKeepsTheFirstOwnerOfAnId()
    {
        var plots = new CubePlots();
        plots.Apply(Owners(new(4, 101, "a", 10), new(5, 102, "b", 10)));
        var first = plots.FindOwner(4, 101);
        Assert.That((first.OwnerId, first.Name), Is.EqualTo((10L, "a")));
        // AddOwner (:1334-1338): the id is held, so (5, 102)'s owner is not put in f148
        Assert.That((plots.OwnerIdCount, plots.FindOwnerById(10)), Is.EqualTo((1, first)));
        Assert.That(plots.FindOwner(5, 102).Name, Is.EqualTo("b"));

        // SetCubeOwner (ugcmapdata_1.cpp:252-266): a known pair's owner is overwritten in place, so f148 sees the new values
        plots.Apply(Owners(new LoadCubesPlots.PlotOwner(4, 101, "c", 11)));
        Assert.That(plots.FindOwner(4, 101), Is.SameAs(first));
        Assert.That((first.OwnerId, first.Name), Is.EqualTo((11L, "c")));
        Assert.That(plots.FindOwnerById(10), Is.SameAs(first));
        Assert.That((plots.OwnerIdCount, plots.FindOwnerById(11)), Is.EqualTo((2, first)));
    }

    [Test]
    public void ModeThreeSetsAPairsSaleStateAndValue()
    {
        // SetSaleState (cubemapinformationservice_3.cpp:2255-2265): made on first use, then overwritten
        var plots = new CubePlots();
        plots.Apply(Sales(new LoadCubesPlots.PlotSale(5, 102, 2, 900)));
        Assert.That(plots.SaleState(5, 102), Is.EqualTo(((byte)2, 900L)));
        plots.Apply(Sales(new LoadCubesPlots.PlotSale(5, 102, 1, -1)));
        Assert.That(plots.SaleState(5, 102), Is.EqualTo(((byte)1, -1L)));
        Assert.That(plots.SaleState(5, 103), Is.Null);
        Assert.That(plots.PlotCount, Is.EqualTo(0));
    }
}
