using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class NoticeTests {
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    [Test]
    public void TextNotice() {
        var n = NoticeMessage.Read(R(w => { w.WriteByte(4); w.WriteShort(0x11); w.WriteBool(false); w.WriteInt(0); w.WriteUnicodeString("hello"); w.WriteShort(3000); }));
        Assert.That((n.Kind, n.Flags, n.Arg, n.Text.Formatted, n.Text.Kind, n.Text.Text), Is.EqualTo(((byte)4, (ushort)0x11, (ushort)3000, false, 0, "hello")));
    }

    [Test]
    public void FormattedNotice() {
        var n = NoticeMessage.Read(R(w => { w.WriteByte(4); w.WriteShort(1); w.WriteBool(true); w.WriteInt(1); w.WriteInt(169); w.WriteInt(2); w.WriteUnicodeString("a"); w.WriteUnicodeString("b"); }));
        Assert.That((n.Text.Formatted, n.Text.Kind, n.Text.Id), Is.EqualTo((true, 1, 169)));
        Assert.That(n.Text.Args, Is.EqualTo(new[] { "a", "b" }));
        Assert.That(n.Arg, Is.EqualTo(0));
    }

    [Test]
    public void TopLayerValueOnlyWhenItIsShown() {
        var top = NoticeMessage.Read(R(w => { w.WriteByte(4); w.WriteShort(0x201); w.WriteBool(false); w.WriteInt(0); w.WriteUnicodeString("t"); w.WriteInt(5000); }));
        Assert.That(top.TopLayerArg, Is.EqualTo(5000));
        // flags 4 picks the system notice first, so OnNotice does not read the value
        var gm = NoticeMessage.Read(R(w => { w.WriteByte(4); w.WriteShort(0x204); w.WriteBool(false); w.WriteInt(0); w.WriteUnicodeString("t"); w.WriteInt(5000); }));
        Assert.That(gm.TopLayerArg, Is.EqualTo(0));
        var other = NoticeMessage.Read(R(w => { w.WriteByte(5); w.WriteShort(0x200); w.WriteBool(false); w.WriteInt(0); w.WriteUnicodeString("t"); w.WriteInt(5000); }));
        Assert.That(other.TopLayerArg, Is.EqualTo(0));
    }
}
