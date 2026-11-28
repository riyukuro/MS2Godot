using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>CubeMapInformationService::FUN_1410c3cd0 (cubemapinformationservice_3.cpp:1823-1843), the house rule the pet item
/// use asks (FUN_141bdb660(7), mypc.cpp:3751), and MyPC::IsPartyMemberByAccountId (mypc.cpp:1987).</summary>
public class PetHouseRuleTests
{
    static CubeMapInfo House(long owner, params (bool, byte)[] permissions)
    {
        var info = new CubeMapInfo();
        info.Apply(new LoadUgcMap(2, 1, new UgcMapHome(owner, "n", "m", 0, 0, 0, 0, 0, 0, 10, 5, 0, 0, 0,
            permissions, Array.Empty<UgcMapLayout>(), Array.Empty<UgcMapLayout>())));
        return info;
    }

    static (bool, byte)[] Kind7(bool set, byte value)
    {
        var p = new (bool, byte)[CubeMapInfo.PermissionKinds];
        p[7] = (set, value);
        return p;
    }

    [Test]
    public void AnUnsetOrOffRuleNeverHolds()
    {
        Assert.That(House(5).PermissionApplies(7, 9, true, null), Is.False);
        Assert.That(House(5, Kind7(false, 2)).PermissionApplies(7, 9, true, null), Is.False);
    }

    [Test]
    public void ValueOneHoldsForAllButTheOwner()
    {
        var h = House(5, Kind7(true, 1));
        Assert.That(h.PermissionApplies(7, 5, true, null), Is.False);
        Assert.That(h.PermissionApplies(7, 9, true, _ => true), Is.True, "a party member gets no pass on value 1");
    }

    [Test]
    public void ValueTwoSparesTheOwnerAndTheOwnersParty()
    {
        var h = House(5, Kind7(true, 2));
        Assert.That(h.PermissionApplies(7, 5, true, null), Is.False);
        Assert.That(h.PermissionApplies(7, 9, true, a => a == 5), Is.False);
        Assert.That(h.PermissionApplies(7, 9, true, a => a == 6), Is.True);
        Assert.That(h.PermissionApplies(7, 9, false, a => a == 5), Is.True, "no local player: it holds");
    }

    [Test]
    public void ValueZeroOrAnotherHoldsForEveryone()
    {
        Assert.That(House(5, Kind7(true, 0)).PermissionApplies(7, 5, true, _ => true), Is.True);
        Assert.That(House(5, Kind7(true, 3)).PermissionApplies(7, 5, true, _ => true), Is.True);
    }

    static PartyUser User(long id) => new() { AccountId = id + 100, CharacterId = id, Name = "n" + id, MapId = 2000001, Level = 30, Channel = 1 };

    [Test]
    public void ThePartyLookupComparesTheAccountIdOnlyInAParty()
    {
        var book = new PartyBook();
        Assert.That(book.IsMemberByAccountId(101), Is.False);
        book.Apply(new PartyInfo(false, 1, 1, new[] { new PartyInfoMember(false, User(1)), new PartyInfoMember(false, User(2)) }, false, 0), 1, false);
        Assert.That(book.IsMemberByAccountId(102), Is.True);
        Assert.That(book.IsMemberByAccountId(2), Is.False, "the character id is not the key");
        Assert.That(book.IsMemberByAccountId(103), Is.False);
    }
}
