using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>OnResponseCube 0x27 / 0x35 / 0x37 (scenestategame.cpp:10560-10606, :10746-10759) and the ServerEnter reset
/// FUN_14092f420 (accountsessionstate.cpp:293).</summary>
public class HousingStateTests
{
    [Test]
    public void HousingGradeSetsTheOwnPart()
    {
        var h = new HousingState();
        h.Apply(new ResponseCubeHousingGrade(true, 5, 3, 900, new[] { 1, 2, 2 }));
        Assert.That((h.Grade, h.Exp, h.DayChangeTime), Is.EqualTo(((ushort)3, 900L, 5L)));
        Assert.That(h.RewardedGrades, Is.EqualTo(new[] { 1, 2, 2 }));
        // a later packet replaces the list (RemoveAll, then Add)
        h.Apply(new ResponseCubeHousingGrade(true, 6, 4, 1000, new[] { 3 }));
        Assert.That(h.RewardedGrades, Is.EqualTo(new[] { 3 }));
        // another account's packet changes nothing
        h.Apply(new ResponseCubeHousingGrade(false, 0, 0, 0, System.Array.Empty<int>()));
        Assert.That((h.Grade, h.RewardedGrades.Count), Is.EqualTo(((ushort)4, 1)));
    }

    [Test]
    public void HouseOwnerCountsForTheOwnAccountOnly()
    {
        var h = new HousingState();
        h.Apply(new ResponseCubeHouseOwner(41, 77, 0, 0), 42);
        Assert.That((h.InHouse, h.OwnerId, h.InOwnersHouse), Is.EqualTo((false, 0L, false)));
        h.Apply(new ResponseCubeHouseOwner(42, 77, 0, 0), 42);
        Assert.That((h.InHouse, h.OwnerId, h.InOwnersHouse), Is.EqualTo((true, 77L, true)));
        // FUN_140930c50 needs the owner id too
        var zero = new HousingState();
        zero.Apply(new ResponseCubeHouseOwner(42, 0, 0, 0), 42);
        Assert.That((zero.InHouse, zero.InOwnersHouse), Is.EqualTo((true, false)));
    }

    [Test]
    public void OwnerTrophiesAreASet()
    {
        var h = new HousingState();
        h.Apply(new ResponseCubeOwnerTrophies(6, new[] { (2300, 1), (2300, 1), (2301, 2) }));
        Assert.That(h.OwnerGrade, Is.EqualTo(6));
        Assert.That(h.OwnerTrophies, Is.EquivalentTo(new[] { (2300, 1), (2301, 2) }));
        h.Apply(new ResponseCubeOwnerTrophies(2, new[] { (5, 5) }));
        Assert.That(h.OwnerTrophies, Is.EquivalentTo(new[] { (5, 5) }));
    }

    [Test]
    public void ServerEnterClearsOnlyTheOwnerPart()
    {
        var h = new HousingState();
        h.Apply(new ResponseCubeHousingGrade(true, 5, 3, 900, new[] { 1 }));
        h.Apply(new ResponseCubeHouseOwner(42, 77, 0, 0), 42);
        h.Apply(new ResponseCubeOwnerTrophies(6, new[] { (2300, 1) }));
        h.ServerEnter();
        Assert.That((h.InHouse, h.OwnerId, h.OwnerGrade, h.OwnerTrophies.Count), Is.EqualTo((false, 0L, (ushort)0, 0)));
        Assert.That((h.Grade, h.Exp, h.DayChangeTime, h.RewardedGrades.Count), Is.EqualTo(((ushort)3, 900L, 5L, 1)));
    }
}
