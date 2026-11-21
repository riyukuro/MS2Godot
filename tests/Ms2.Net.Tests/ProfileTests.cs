using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class ProfileTests {
    [Test]
    public void UserMoveByPortalDecodes() {
        var w = new ByteWriter();
        w.WriteInt(10000009); w.WriteFloat(150); w.WriteFloat(-300); w.WriteFloat(175); w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(90); w.WriteBool(false);
        var m = UserMoveByPortal.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()));
        Assert.That(m, Is.EqualTo(new UserMoveByPortal(10000009, new Vector3(150, -300, 175), new Vector3(0, 0, 90), false)));
    }

    [Test]
    public void KmsProfileCarriesTheVersionAndTheEnumNumbers() {
        var p = ProtocolProfile.Kms2525;
        Assert.That(p.Version, Is.EqualTo(2525u));
        Assert.That(p.Number(Outbound.UserSync), Is.EqualTo((ushort) 0x12));
        Assert.That(p.Number(Inbound.UserMoveByPortal), Is.EqualTo((ushort) 0x5F));
        Assert.That(p.TryInbound(0x14, out var op) && op == Inbound.SyncNumber);
    }

    [Test]
    public async Task ChannelSendsThroughTheProfileNumbers() {
        var srv = new FakeServer(1, 2);
        var io = new MemoryStream(); io.Write(srv.HelloFrame); io.Position = 0;
        var ch = new Wire.MapleChannel(io);
        await ch.ReadHandshakeAsync();
        var map = Enum.GetValues<Outbound>().ToDictionary(o => o, o => (ushort) o);
        map[Outbound.UserSync] = 0x77;
        ch.Profile = new ProtocolProfile("test", 2525, Enum.GetValues<Inbound>().ToDictionary(o => o, o => (ushort) o), map);
        long start = io.Length;
        await ch.SendAsync(Outbound.UserSync, w => w.WriteByte(1));
        var all = io.ToArray().AsSpan((int) start).ToArray();
        Assert.That(srv.Read(all).op, Is.EqualTo((ushort) 0x77));
    }

    [Test]
    public void Gms2ProfileCarriesVersion12AndTheUpstreamNumbers() {
        var p = ProtocolProfile.Gms2;
        Assert.That(p.Version, Is.EqualTo(12u));
        // upstream SendOp / RecvOp rows
        Assert.That(p.Number(Inbound.RequestVersion), Is.EqualTo((ushort) 0x01));
        Assert.That(p.Number(Inbound.LoginResult), Is.EqualTo((ushort) 0x0A));
        Assert.That(p.Number(Inbound.UserMoveByPortal), Is.EqualTo((ushort) 0x60));
        Assert.That(p.Number(Inbound.FieldAddUser), Is.EqualTo((ushort) 0x17));
        Assert.That(p.Number(Outbound.ResponseLogin), Is.EqualTo((ushort) 0x03));
        Assert.That(p.Number(Outbound.UserSync), Is.EqualTo((ushort) 0x12));
        Assert.That(p.Number(Outbound.KeyTable), Is.EqualTo((ushort) 0x3B));
        Assert.That(p.Number(Inbound.SkillMacro), Is.EqualTo((ushort) 0xD3));
        Assert.That(p.Number(Outbound.SkillMacro), Is.EqualTo((ushort) 0x7C));
        Assert.That(p.Number(Outbound.ChatSticker), Is.EqualTo((ushort) 0xB9));
        Assert.That(p.Number(Outbound.RequestMoveField), Is.EqualTo((ushort) 0x6D));
        Assert.That(p.Number(Inbound.MeretMarket), Is.EqualTo((ushort) 0x6E));
        Assert.That(p.Number(Outbound.MeretMarket), Is.EqualTo((ushort) 0x3A));
        Assert.That(p.TryInbound(0x60, out var op) && op == Inbound.UserMoveByPortal);
        Assert.That(p.Number(Inbound.Meret), Is.EqualTo((ushort) 0x3B));
        Assert.That(p.Number(Inbound.CurrencyToken), Is.EqualTo((ushort) 0x3C));
        Assert.That(p.Number(Inbound.MeretUse), Is.EqualTo((ushort) 0x119));
        Assert.That(p.Number(Inbound.UiText), Is.EqualTo((ushort) 0x96));
        Assert.That(p.Number(Inbound.NewsNotification), Is.EqualTo((ushort) 0xA1));
        Assert.That(p.Number(Inbound.PlaySystemSound), Is.EqualTo((ushort) 0xC5));
        Assert.That(p.Number(Inbound.NoticeDialog), Is.EqualTo((ushort) 0xD1));
        Assert.That(p.Number(Inbound.SmartPush), Is.EqualTo((ushort) 0xD9));
        Assert.That(p.Number(Outbound.NewsNotification), Is.EqualTo((ushort) 0x64));
        Assert.That(p.Number(Outbound.SmartPush), Is.EqualTo((ushort) 0x84));
        // rows upstream lacks stay unmapped
        Assert.Throws<KeyNotFoundException>(() => p.Number(Inbound.UserChatItemLink));
        Assert.Throws<KeyNotFoundException>(() => p.Number(Outbound.PersonalInfoAgreement));
        // every other member is mapped, and the numbers stay unique per direction
        // (the failure message names the members to add a row for in Gms2Opcodes.cs)
        Assert.That(Enum.GetValues<Inbound>().Where(o => o != Inbound.UserChatItemLink && !TryNumber(p, o)).Select(o => "Inbound." + o), Is.Empty, "no GMS2 number for");
        Assert.That(Enum.GetValues<Outbound>().Where(o => o != Outbound.PersonalInfoAgreement && !TryNumber(p, o)).Select(o => "Outbound." + o), Is.Empty, "no GMS2 number for");
    }

    [Test]
    public void Gms2NumbersAreUniquePerDirection() {
        var p = ProtocolProfile.Gms2;
        // a number on two members means one row was copied from the KMS value or mistyped; the right numbers are upstream's
        // Maple2.Server.Core/Constants/SendOp.cs (Inbound, server to client) and RecvOp.cs (Outbound, client to server)
        var inDup = Enum.GetValues<Inbound>().Where(o => TryNumber(p, o)).GroupBy(o => p.Number(o)).Where(g => g.Count() > 1)
            .Select(g => $"0x{g.Key:X2}: {string.Join(", ", g)}");
        var outDup = Enum.GetValues<Outbound>().Where(o => TryNumber(p, o)).GroupBy(o => p.Number(o)).Where(g => g.Count() > 1)
            .Select(g => $"0x{g.Key:X2}: {string.Join(", ", g)}");
        Assert.That(inDup, Is.Empty, "Inbound numbers on two members (check SendOp.cs)");
        Assert.That(outDup, Is.Empty, "Outbound numbers on two members (check RecvOp.cs)");
    }

    static bool TryNumber(ProtocolProfile p, Inbound o) { try { p.Number(o); return true; } catch (KeyNotFoundException) { return false; } }
    static bool TryNumber(ProtocolProfile p, Outbound o) { try { p.Number(o); return true; } catch (KeyNotFoundException) { return false; } }
}
