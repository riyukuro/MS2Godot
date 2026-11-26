using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The shop mode numbers (<see cref="ShopModes"/>): the same on KMS and GMS2, so every request writes its mode on both profiles
/// and every receive mode the client's switch reads is read by <see cref="ShopPacket"/>.</summary>
public class ShopModesTests {
    static byte[] Raw(IProfileOutPacket p, ProtocolProfile profile) { var w = new ByteWriter(); p.Write(w, profile); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
    static byte[] Raw(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    /// <summary>The GMS2 server's ShopPacket.Command and ShopHandler.Command numbers (upstream Maple2).</summary>
    static readonly Dictionary<string, byte> UpstreamWrites = new() {
        ["Open"] = 0, ["LoadItems"] = 1, ["Update"] = 2, ["Buy"] = 4, ["BuyBackItemCount"] = 6, ["LoadBuyBack"] = 7,
        ["RemoveBuyBack"] = 8, ["InstantRestock"] = 9, ["LoadNew"] = 14, ["Error"] = 15,
    };
    static readonly Dictionary<string, byte> UpstreamReads = new() {
        ["PurchaseBuyBack"] = 3, ["Buy"] = 4, ["Sell"] = 5, ["InstantRestock"] = 9, ["Refresh"] = 10, ["LoadNew"] = 13,
    };

    [Test]
    public void EveryRequestWritesItsModeOnBothProfiles() {
        foreach (var profile in new[] { ProtocolProfile.Kms2525, ProtocolProfile.Gms2 }) {
            Assert.That(Raw(new ShopBuyRequest(1, 2, 3), profile)[0], Is.EqualTo(ShopModes.SendBuy));
            Assert.That(Raw(new ShopRepurchaseRequest(1))[0], Is.EqualTo(ShopModes.SendRepurchase));
            Assert.That(Raw(new ShopSellRequest(1, 1))[0], Is.EqualTo(ShopModes.SendSell));
            Assert.That(Raw(new ShopResetListRequest(1))[0], Is.EqualTo(ShopModes.SendReset));
            Assert.That(Raw(new ShopRequestListRequest())[0], Is.EqualTo(ShopModes.SendRequestList));
            Assert.That(Raw(new ShopRequestNewItemsRequest())[0], Is.EqualTo(ShopModes.SendRequestNewItems));
        }
        Assert.That(ShopModes.Send, Is.EqualTo(new byte[] { 3, 4, 5, 9, 10, 13 }));
    }

    [Test]
    public void TheGms2ServersNumbersAreTheClientsNumbers() {
        // each mode the upstream server writes is a receive case of the client, and each it reads is a send mode of the client
        Assert.That(UpstreamWrites.Values.Except(ShopModes.Receive), Is.Empty);
        Assert.That(UpstreamReads.Values.Except(ShopModes.Send), Is.Empty);
        // by name
        Assert.That(ShopModes.RecvInfo, Is.EqualTo(UpstreamWrites["Open"]));
        Assert.That(ShopModes.RecvProducts, Is.EqualTo(UpstreamWrites["LoadItems"]));
        Assert.That(ShopModes.RecvProductUpdate, Is.EqualTo(UpstreamWrites["Update"]));
        Assert.That(ShopModes.RecvPurchaseResult, Is.EqualTo(UpstreamWrites["Buy"]));
        Assert.That(ShopModes.RecvRepurchaseStart, Is.EqualTo(UpstreamWrites["BuyBackItemCount"]));
        Assert.That(ShopModes.RecvRepurchaseAdd, Is.EqualTo(UpstreamWrites["LoadBuyBack"]));
        Assert.That(ShopModes.RecvRepurchaseRemoval, Is.EqualTo(UpstreamWrites["RemoveBuyBack"]));
        Assert.That(ShopModes.RecvResetResult, Is.EqualTo(UpstreamWrites["InstantRestock"]));
        Assert.That(ShopModes.RecvNewProducts, Is.EqualTo(UpstreamWrites["LoadNew"]));
        Assert.That(ShopModes.RecvError, Is.EqualTo(UpstreamWrites["Error"]));
        Assert.That(ShopModes.SendRepurchase, Is.EqualTo(UpstreamReads["PurchaseBuyBack"]));
        Assert.That(ShopModes.SendBuy, Is.EqualTo(UpstreamReads["Buy"]));
        Assert.That(ShopModes.SendSell, Is.EqualTo(UpstreamReads["Sell"]));
        Assert.That(ShopModes.SendReset, Is.EqualTo(UpstreamReads["InstantRestock"]));
        Assert.That(ShopModes.SendRequestList, Is.EqualTo(UpstreamReads["Refresh"]));
        Assert.That(ShopModes.SendRequestNewItems, Is.EqualTo(UpstreamReads["LoadNew"]));
    }

    [Test]
    public void EveryReceiveModeOfTheClientsSwitchIsRead() {
        // the fixtures of ShopPacket write each mode; the reader must return that mode with its body parsed (the body member is set)
        var bodies = new Dictionary<byte, byte[]> {
            [0] = ShopPacket.WriteInfo(new ShopInfo()),
            [1] = ShopPacket.WriteProducts(new[] { new ShopProduct { Serial = 1, ItemId = 2 } }),
            [2] = ShopPacket.WriteProductUpdate(1, 2),
            [4] = ShopPacket.WritePurchaseResult(new ShopPurchaseResult(1, 2, 3, 0, false)),
            [6] = ShopPacket.WriteRepurchaseStart(3),
            [7] = ShopPacket.WriteRepurchaseAdd(new[] { new ShopProduct { Serial = 1, ItemId = 2, Count = 1 } }),
            [8] = ShopPacket.WriteRepurchaseRemoval(5),
            [9] = ShopPacket.WriteResetResult(new ShopResetResult(true, 1, 2)),
            [13] = ShopPacket.WriteNewStart(new ShopNewStart(1, 2, 3, 4)),
            [14] = ShopPacket.WriteNewProducts(new[] { new ShopNewProduct { ItemId = 1 } }),
            [15] = ShopPacket.WriteError(new ShopError(8, 4, 99)),
        };
        foreach (var (mode, body) in bodies) {
            Assert.That(body[0], Is.EqualTo(mode));
            Assert.That(ShopModes.Receive, Does.Contain(mode));
            var p = ShopPacket.Read(new ByteReader(body));
            Assert.That(p.Command, Is.EqualTo((int) mode));
        }
        Assert.That(ShopPacket.Read(new ByteReader(bodies[15])).Error, Is.EqualTo(new ShopError(8, 4, 99)));
        Assert.That(ShopPacket.Read(new ByteReader(bodies[2])).ProductUpdate, Is.EqualTo(new ShopProductUpdate(1, 2)));
        Assert.That(ShopPacket.Read(new ByteReader(bodies[8])).RemoveSerial, Is.EqualTo(5));
        Assert.That(ShopPacket.Read(new ByteReader(bodies[6])).RepurchaseCount, Is.EqualTo(3));
        // 11 and 12 (merat) are KMS-only but in the table
        Assert.That(ShopModes.Receive, Does.Contain(ShopModes.RecvMeratStart).And.Contain(ShopModes.RecvMeratItems));
    }

    [Test]
    public void EverySystemShopTypeTheClientSendsRoundTripsAndTheGms2ServersAreTheClientsNumbers() {
        // upstream SystemShopHandler.Command: Arena 3, Fishing 4, Mentee 6, Mentor 7, Item 10
        var upstream = new Dictionary<string, byte> { ["Arena"] = 3, ["Fishing"] = 4, ["Mentee"] = 6, ["Mentor"] = 7, ["Item"] = 10 };
        Assert.That(upstream.Values.OrderBy(x => x), Is.EqualTo(SystemShopTypes.Gms2Server));
        Assert.That(SystemShopTypes.Gms2Server.Except(SystemShopTypes.All), Is.Empty);
        Assert.That(SystemShopTypes.All, Is.EqualTo(new byte[] { 3, 4, 6, 7, 8, 9, 10 }));
        foreach (byte t in SystemShopTypes.All) {
            Assert.That(Raw(new SystemShopRequest(t, 1)), Is.EqualTo(new byte[] { t, 1 }));
            Assert.That(Raw(new SystemShopRequest(t, 0)), Is.EqualTo(new byte[] { t, 0 }));
            Assert.That(SystemShopPacket.Read(new ByteReader(new byte[] { t, 1 })), Is.EqualTo(new SystemShopPacket(t, true)));
        }
        Assert.That(Raw(new SystemShopCoinRequest(7))[0], Is.EqualTo(SystemShopTypes.Item));
    }
}
