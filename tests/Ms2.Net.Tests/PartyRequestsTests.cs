using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The party window's and the popup menu's sends (CPartyManager, UIPopUpMenu, UIPartyDialog, CClubClientManager).</summary>
public class PartyRequestsTests
{
    static byte[] Bytes(IOutPacket p)
    {
        var w = new ByteWriter();
        p.Write(w);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }

    static byte[] Concat(params byte[][] parts) => parts.SelectMany(b => b).ToArray();
    static byte[] Str(string s) => Concat(BitConverter.GetBytes((ushort)s.Length), System.Text.Encoding.Unicode.GetBytes(s));

    [Test]
    public void OpcodesAreTheClientSendOps()
    {
        // SendOp (commonfiles/opcodes.h): Party 0x2C, RoomDungeon 0x2B, DungeonReward 0x9D, Club 0x95
        Assert.That((ushort)Outbound.Party, Is.EqualTo(0x2C));
        Assert.That((ushort)Outbound.RoomDungeon, Is.EqualTo(0x2B));
        Assert.That((ushort)Outbound.DungeonReward, Is.EqualTo(0x9D));
        Assert.That((ushort)Outbound.Club, Is.EqualTo(0x95));
        Assert.That(new PartyLeaveRequest().Op, Is.EqualTo(Outbound.Party));
        Assert.That(new RoomDungeonEnterRequest(0).Op, Is.EqualTo(Outbound.RoomDungeon));
        Assert.That(new DungeonGiveUpRequest().Op, Is.EqualTo(Outbound.DungeonReward));
        Assert.That(new ClubCreateRequest("x").Op, Is.EqualTo(Outbound.Club));
    }

    [Test]
    public void ThePopupHomeSendsFollowTheClient()
    {
        // UIPopUpMenu::SendRequestHome: RequestHome 0x51, u8 1, the name; SendRequestCube: RequestCube 0x37, u8 0x39, the name
        Assert.That((ushort)Outbound.RequestHome, Is.EqualTo(0x51));
        Assert.That((ushort)Outbound.RequestCube, Is.EqualTo(0x37));
        Assert.That(new HomeInviteRequest("x").Op, Is.EqualTo(Outbound.RequestHome));
        Assert.That(new FurnishingRightsRequest("x").Op, Is.EqualTo(Outbound.RequestCube));
        Assert.That(Bytes(new HomeInviteRequest("Ab")), Is.EqualTo(Concat(new byte[] { 1 }, Str("Ab"))));
        Assert.That(Bytes(new FurnishingRightsRequest("Ab")), Is.EqualTo(Concat(new byte[] { 0x39 }, Str("Ab"))));
    }

    [Test]
    public void PayloadsFollowTheClient()
    {
        Assert.That(Bytes(new PartyInviteRequest("Ab")), Is.EqualTo(Concat(new byte[] { 1 }, Str("Ab"))));
        Assert.That(Bytes(new PartyLeaveRequest()), Is.EqualTo(new byte[] { 3 }));
        Assert.That(Bytes(new PartyKickRequest(0x0102030405060708)), Is.EqualTo(Concat(new byte[] { 4 }, BitConverter.GetBytes(0x0102030405060708L))));
        Assert.That(Bytes(new PartyLeaderChangeRequest("Ab")), Is.EqualTo(Concat(new byte[] { 0x11 }, Str("Ab"))));
        Assert.That(Bytes(new PartyVoteKickRequest(7)), Is.EqualTo(Concat(new byte[] { 0x2d }, BitConverter.GetBytes(7L))));
        Assert.That(Bytes(new PartyReadyCheckRequest()), Is.EqualTo(new byte[] { 0x2e }));
        Assert.That(Bytes(new RoomDungeonEnterRequest(0)), Is.EqualTo(new byte[] { 3, 0, 0, 0, 0 }));
        Assert.That(Bytes(new DungeonGiveUpRequest()), Is.EqualTo(new byte[] { 5 }));
        Assert.That(Bytes(new ClubCreateRequest("Club")), Is.EqualTo(Concat(new byte[] { 1 }, Str("Club"))));
    }
}
