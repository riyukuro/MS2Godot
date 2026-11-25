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

    /// <summary>
    /// Every single flag bit (and the /notice default Message|Alert) as the server fork's NoticePacket.Notice writes it: the
    /// text, then a u16 duration for Mint (0x10), else an i32 for LargeAlert (0x200). The reader takes the u16 for 0x10 and the
    /// i32 when OnNotice shows the top layer line; the only bytes left are the fork's i32 when another flag picks first.
    /// </summary>
    [Test]
    public void EveryFlagAsTheForkWritesIt() {
        foreach (int flags in Enumerable.Range(0, 16).Select(b => 1 << b).Append(5).Append(0x204).Append(0x210)) {
            var r = R(w => {
                w.WriteByte(4); w.WriteShort((short)flags);
                w.WriteBool(true); w.WriteInt(1); w.WriteInt(74); w.WriteInt(0);
                if ((flags & 0x10) != 0) w.WriteShort(1234);
                else if ((flags & 0x200) != 0) w.WriteInt(1234);
            });
            var n = NoticeMessage.Read(r);
            Assert.That((n.Flags, n.Text.Id), Is.EqualTo(((ushort)flags, 74)), $"0x{flags:x}");
            Assert.That(n.Arg, Is.EqualTo((flags & 0x10) != 0 ? 1234 : 0), $"0x{flags:x}");
            Assert.That(n.TopLayerArg, Is.EqualTo(flags == 0x200 ? 1234 : 0), $"0x{flags:x}");
            Assert.That(r.Available, Is.EqualTo(flags == 0x204 ? 4 : 0), $"0x{flags:x}");
        }
    }
}
