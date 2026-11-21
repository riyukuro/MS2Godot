using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class ServerMessagePacketTests
{
    static ByteReader Reader(Action<ByteWriter> write)
    {
        var w = new ByteWriter();
        write(w);
        return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
    }

    [Test]
    public void OpcodesAreTheClientsRecvOpNumbers()
    {
        // opcodes.h RecvOp
        Assert.That((int)Inbound.Meret, Is.EqualTo(0x3A));
        Assert.That((int)Inbound.CurrencyToken, Is.EqualTo(0x3B));
        Assert.That((int)Inbound.UiText, Is.EqualTo(0x94));
        Assert.That((int)Inbound.NewsNotification, Is.EqualTo(0x9F));
        Assert.That((int)Inbound.PlaySystemSound, Is.EqualTo(0xC3));
        Assert.That((int)Inbound.NoticeDialog, Is.EqualTo(0xCF));
        Assert.That((int)Inbound.SmartPush, Is.EqualTo(0xD7));
        Assert.That((int)Inbound.MeretUse, Is.EqualTo(0x117));
    }

    [TestCase(4, 5), TestCase(7, 6), TestCase(0xa, 7), TestCase(1, -1), TestCase(0, -1)]
    public void UiTextTypeMapsToTheRecoverGrade(int type, int grade)
    {
        // FUN_141731190 (scenestategame.cpp:7878): vfunc_111(value, 5 | 6 | 7, id)
        var m = UiText.Read(Reader(w => { w.WriteInt(77); w.WriteInt(type); w.WriteInt(-12); }));
        Assert.That(m, Is.EqualTo(new UiText(77, type, -12)));
        Assert.That(m.Grade, Is.EqualTo(grade));
    }

    [Test]
    public void NoticeDialogShowsOnlyForFlagZero()
    {
        Assert.That(NoticeDialogMessage.Read(Reader(w => w.WriteByte(0))).Show, Is.True);
        Assert.That(NoticeDialogMessage.Read(Reader(w => w.WriteByte(1))).Show, Is.False);
        Assert.Throws<PacketLayoutException>(() => NoticeDialogMessage.Read(Reader(w => { w.WriteByte(0); w.WriteByte(0); })));
    }

    [Test]
    public void SystemSoundIsOneUnicodeKey()
    {
        Assert.That(SystemSound.Read(Reader(w => w.WriteUnicodeString("Merat_Get"))).Key, Is.EqualTo("Merat_Get"));
    }

    [Test]
    public void NewsNotificationReadsTheServersOpenUiAndTokenForms()
    {
        // NewsNotificationPacket.OpenUi / ResponseToken
        var open = NewsNotification.Read(Reader(w => { w.WriteByte(0); w.WriteUnicodeString("86BFAEA2"); w.WriteBool(true); w.WriteInt(2); }));
        Assert.That(open, Is.EqualTo(new NewsNotification(0, "86BFAEA2", true, 2)));
        var token = NewsNotification.Read(Reader(w => { w.WriteByte(0); w.WriteUnicodeString("tok"); w.WriteBool(false); w.WriteInt(0); }));
        Assert.That(token.InGame, Is.False);
        Assert.That(NewsNotification.Read(Reader(w => w.WriteByte(3))).Mode, Is.EqualTo(3));
    }

    [Test]
    public void SmartPushReadsPositionOrModeOrStringId()
    {
        Assert.That(SmartPushMessage.Read(Reader(w => { w.WriteBool(true); w.WriteInt(10); w.WriteInt(-20); })), Is.EqualTo(new SmartPushMessage(true, 10, -20, 0, 0)));
        Assert.That(SmartPushMessage.Read(Reader(w => { w.WriteBool(false); w.WriteByte(1); })).Mode, Is.EqualTo(1));
        var n = SmartPushMessage.Read(Reader(w => { w.WriteBool(false); w.WriteByte(0); w.WriteInt(555); }));
        Assert.That(n.StringId, Is.EqualTo(555));
    }

    [Test]
    public void MeretReadsTheFourBalancesAndTheDelta()
    {
        // CurrencyPacket.UpdateMeret
        var m = MeretUpdate.Read(Reader(w => { w.WriteLong(100); w.WriteLong(0); w.WriteLong(40); w.WriteLong(0); w.WriteLong(-5); }));
        Assert.That(m, Is.EqualTo(new MeretUpdate(100, 0, 40, 0, -5)));
        var u = MeretUseUpdate.Read(Reader(w => { w.WriteLong(9); w.WriteLong(3); }));
        Assert.That(u, Is.EqualTo(new MeretUseUpdate(9, 3)));
    }

    [Test]
    public void CurrencyTokenReadsTheServersUpdateCurrency()
    {
        var m = CurrencyTokenUpdate.Read(Reader(w => { w.WriteByte(3); w.WriteLong(50); w.WriteLong(7); w.WriteInt(0); w.WriteLong(2); }));
        Assert.That(m, Is.EqualTo(new CurrencyTokenUpdate(3, 50, 7, 0, 2)));
    }
}
