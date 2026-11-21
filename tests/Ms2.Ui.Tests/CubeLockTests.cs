using System;
using System.Xml.Linq;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>MyPC::FUN_1415302e0 (@0x1415302e0, mypc.cpp:4445-4497).</summary>
public class CubeLockTests
{
    const long Me = 5, Owner = 9;

    static HousingTable Rows(string rows, string prices = "<ms2/>") =>
        new(XDocument.Parse("<ms2>" + rows + "</ms2>"), XDocument.Parse(prices));

    static CubeLock Lock(string rows, ushort grade = 1, bool feature = true, string interiors = "<ms2/>",
        Func<int, int, bool> cleared = null, string prices = "<ms2/>")
    {
        var l = new CubeLock
        {
            Housing = Rows(rows, prices),
            Interiors = new CubeInteriorTable(XDocument.Parse(interiors)),
            IsFeatureEnabled = id => feature && id == CubeLock.HousingLevelFeature,
            IsGradeCleared = cleared,
        };
        l.State.Apply(new ResponseCubeHousingGrade(true, 0, grade, 0, Array.Empty<int>()));
        return l;
    }

    static void EnterHouse(CubeLock l, ushort ownerGrade, params (int, int)[] trophies)
    {
        l.State.Apply(new ResponseCubeHouseOwner(Me, Owner, 0, 0), Me);
        l.State.Apply(new ResponseCubeOwnerTrophies(ownerGrade, trophies));
    }

    [Test]
    public void NoItemDataIsLockedAndNoHousingRowIsNot()
    {
        var l = Lock("<housing id=\"7\" interiorLevel=\"9\"/>");
        Assert.That(l.IsLocked(7, hasItemData: false, false), Is.True);
        Assert.That(l.IsLocked(8, hasItemData: true, false), Is.False);
        Assert.That(l.IsLocked(8, hasItemData: false, false), Is.True, "the item data comes first");
    }

    [Test]
    public void TheInteriorLevelAboveTheOwnGradeIsLocked()
    {
        var l = Lock("<housing id=\"7\" interiorLevel=\"3\"/><housing id=\"8\" interiorLevel=\"2\"/>", grade: 2);
        Assert.That(l.IsLocked(7, true, false), Is.True);
        Assert.That(l.IsLocked(8, true, false), Is.False);
    }

    [Test]
    public void MoneyTypeThreeAndTheConstructionUnlockPassFirst()
    {
        var l = Lock("<housing id=\"7\" interiorLevel=\"3\"/>", prices: "<ms2><key id=\"7\" ugcHousingMoneyType=\"3\"/></ms2>");
        Assert.That(l.IsLocked(7, true, false), Is.False);
        var u = Lock("<housing id=\"7\" interiorLevel=\"3\"/>");
        Assert.That(u.IsLocked(7, true, constructionUnlocked: true), Is.False);
        Assert.That(u.IsLocked(7, true, constructionUnlocked: false), Is.True);
    }

    [Test]
    public void WithoutTheFeatureTheGradesAreNotChecked()
    {
        var l = Lock("<housing id=\"7\" interiorLevel=\"3\"/>", feature: false,
            interiors: "<ms2><list categoryIndex=\"10000\" requireHousingLevel=\"5\"/></ms2>");
        Assert.That(l.IsLocked(7, true, false), Is.False);
    }

    [Test]
    public void OutsideAHouseTheCategoryRequireLevelIsChecked()
    {
        var l = Lock("<housing id=\"7\" categoryIndex=\"12\"/><housing id=\"8\" categoryIndex=\"13\"/>", grade: 2,
            interiors: "<ms2><list categoryIndex=\"12\" requireHousingLevel=\"3\"/><list categoryIndex=\"13\" requireHousingLevel=\"2\"/></ms2>");
        Assert.That(l.IsLocked(7, true, false), Is.True);
        Assert.That(l.IsLocked(8, true, false), Is.False);
    }

    [Test]
    public void InTheOwnersHouseTheOwnersGradeCountsAndTheRequireLevelIsNot()
    {
        var l = Lock("<housing id=\"7\" categoryIndex=\"12\" interiorLevel=\"3\"/><housing id=\"8\" categoryIndex=\"12\" interiorLevel=\"2\"/>",
            grade: 9, interiors: "<ms2><list categoryIndex=\"12\" requireHousingLevel=\"8\"/></ms2>");
        EnterHouse(l, ownerGrade: 2);
        Assert.That(l.IsLocked(7, true, false), Is.True, "the owner's grade, not the own");
        Assert.That(l.IsLocked(8, true, false), Is.False, "the second test repeats the interiorLevel one");
    }

    [Test]
    public void TheIgnoredLockListOnlyCountsInTheOwnersHouse()
    {
        var l = Lock("<housing id=\"7\" interiorLevel=\"3\"/>");
        l.IgnoredConstructionLock = id => id == Owner;
        Assert.That(l.IsLocked(7, true, false), Is.True);
        EnterHouse(l, ownerGrade: 1);
        Assert.That(l.IsLocked(7, true, false), Is.False);
        l.IgnoredConstructionLock = id => id == Me;
        Assert.That(l.IsLocked(7, true, false), Is.True, "the owner's id is asked");
    }

    [Test]
    public void OutsideAHouseATrophyNeedsItsGradeCleared()
    {
        var l = Lock("<housing id=\"7\" trophyID=\"2300\" trophyLevel=\"4\"/>", cleared: (id, g) => id == 2300 && g == 4);
        Assert.That(l.IsLocked(7, true, false), Is.False);
        l.IsGradeCleared = (id, g) => id == 2300 && g == 3;
        Assert.That(l.IsLocked(7, true, false), Is.True);
        l.IsGradeCleared = null;
        Assert.That(l.IsLocked(7, true, false), Is.True, "no trophies clear nothing");
    }

    [Test]
    public void InTheOwnersHouseATrophyInTheOwnersSetIsLocked()
    {
        var l = Lock("<housing id=\"7\" trophyID=\"2300\" trophyLevel=\"4\"/>", cleared: (_, _) => false);
        EnterHouse(l, 1, (2300, 3));
        Assert.That(l.IsLocked(7, true, false), Is.False, "the own trophies are not asked in the house");
        EnterHouse(l, 1, (2300, 4));
        Assert.That(l.IsLocked(7, true, false), Is.True);
    }

    [Test]
    public void ACheckAllocatesNothing()
    {
        var l = Lock("<housing id=\"7\" trophyID=\"2300\" interiorLevel=\"1\" categoryIndex=\"12\"/>", cleared: (_, _) => true,
            interiors: "<ms2><list categoryIndex=\"12\"/></ms2>");
        l.IsLocked(7, true, false);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) l.IsLocked(7, true, false);
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }
}
