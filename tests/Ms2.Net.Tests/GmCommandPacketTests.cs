using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class GmCommandPacketTests
{
    [Test]
    public void OpcodeMatchesTheClientAndUpstreamTables()
    {
        // commonfiles/opcodes.h:263 RecvOp::GmCommand = 0x0106; upstream SendOp.GmCommand = 0x0108
        Assert.That((ushort)Inbound.GmCommand, Is.EqualTo(0x0106));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.GmCommand), Is.EqualTo((ushort)0x0108));
    }

    static ByteReader Line(byte mode)
    {
        var w = new ByteWriter();
        w.WriteByte(mode);
        w.WriteUnicodeString("https://example.com/event");
        w.WriteUnicodeString("Event starts soon");
        w.WriteUnicodeString("extra");
        return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
    }

    [Test]
    public void ModeOneReadsTheAddressTheTextAndTheDroppedStringWithTheFeatureOn()
    {
        // FUN_14173c0f0 (scenestategame.cpp:8562-8576): A (the address), Name (the shown text), C (read and dropped)
        var r = Line(1);
        var g = GmCommand.Read(r, popupFeature: true);
        Assert.That((g.Mode, g.Url, g.Text, g.Extra, g.IsPopupText), Is.EqualTo(((byte)1, "https://example.com/event", "Event starts soon", "extra", true)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test]
    public void WithoutTheFeatureOrInAnotherModeOnlyTheModeIsRead()
    {
        // FUN_14173c0f0 returns before reading when feature 0x88 is off; OnGmCommand has no reader for the other modes
        Assert.That(GmCommand.PopupTextFeature, Is.EqualTo(0x88));
        var off = Line(1);
        var g = GmCommand.Read(off, popupFeature: false);
        Assert.That((g.IsPopupText, g.Text), Is.EqualTo((false, (string)null)));
        Assert.That(off.Available, Is.GreaterThan(0));
        foreach (byte mode in new byte[] { 0, 2, 3, 0xff })
        {
            var r = Line(mode);
            var o = GmCommand.Read(r, popupFeature: true);
            Assert.That((o.Mode, o.IsPopupText), Is.EqualTo((mode, false)), $"mode {mode}");
            Assert.That(r.Position, Is.EqualTo(1), $"mode {mode}");
        }
    }
}
