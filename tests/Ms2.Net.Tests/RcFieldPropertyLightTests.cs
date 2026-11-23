using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>FieldProperty: GMS2 kinds 8 and 9 carry only rgb / two rgb (no trailing bool+f32) and the list keeps reading;
/// the KMS reader throws on the GMS2 width and returns after the first record. Kind 10 passes today. Layouts:
/// the GMS2 packet layout notes §B4.</summary>
public class RcFieldPropertyLightTests {
    // gms2 recording: packet 7212, 3 bytes
    static readonly byte[] Kind10 = RcPkt.Hex(
        "010A00");
    // gms2 recording: packet 5779, 5 bytes
    static readonly byte[] Kind8 = RcPkt.Hex(
        "01087877B7");
    // gms2 recording: packet 2743, 5 bytes
    static readonly byte[] Kind8B = RcPkt.Hex(
        "0108808080");
    // gms2 recording: packet 5780, 8 bytes
    static readonly byte[] Kind9 = RcPkt.Hex(
        "0109C16477AAAAAA");
    // gms2 recording: packet 2295, 16 bytes
    static readonly byte[] List = RcPkt.Hex(
        "00020000000883A0D10986A08F828282");

    [Test] public void Gms2Kind10ReadsTheBool() {
        var r = new ByteReader(Kind10);
        var m = FieldPropertyMessage.Read(r) as FieldPropertyMessage.Add;
        Assert.That(m, Is.Not.Null);
        Assert.That(m.Property.Kind, Is.EqualTo((byte) 10));
        Assert.That(((FieldProperty.EnableLocalCamera) m.Property).Value, Is.False);
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Ignore("GMS2 field-light kind not read yet")]
    [Test] public void Gms2Kind8ReadsOnlyRgb() {
        foreach (var (bytes, rgb) in new[] { (Kind8, ((byte) 0x78, (byte) 0x77, (byte) 0xB7)), (Kind8B, ((byte) 0x80, (byte) 0x80, (byte) 0x80)) }) {
            var r = new ByteReader(bytes);
            var m = RcPkt.Read("FieldPropertyMessage", r, ProtocolProfile.Gms2);
            var p = RcPkt.MemberAny(m, "Property", "Field");
            Assert.That(RcPkt.Member<byte>(p, "Kind"), Is.EqualTo((byte) 8));
            Assert.That((RcPkt.Member<byte>(p, "R"), RcPkt.Member<byte>(p, "G"), RcPkt.Member<byte>(p, "B")),
                Is.EqualTo(rgb));
            Assert.That(r.Available, Is.EqualTo(0), "the GMS2 kind 8 body ends at the rgb");
        }
    }

    [Ignore("GMS2 field-light kind not read yet")]
    [Test] public void Gms2Kind9ReadsTwoRgb() {
        var r = new ByteReader(Kind9);
        var m = RcPkt.Read("FieldPropertyMessage", r, ProtocolProfile.Gms2);
        var p = RcPkt.MemberAny(m, "Property", "Field");
        Assert.That(RcPkt.Member<byte>(p, "Kind"), Is.EqualTo((byte) 9));
        Assert.That((RcPkt.Member<byte>(p, "R1"), RcPkt.Member<byte>(p, "G1"), RcPkt.Member<byte>(p, "B1")),
            Is.EqualTo(((byte) 0xC1, (byte) 0x64, (byte) 0x77)));
        Assert.That((RcPkt.Member<byte>(p, "R2"), RcPkt.Member<byte>(p, "G2"), RcPkt.Member<byte>(p, "B2")),
            Is.EqualTo(((byte) 0xAA, (byte) 0xAA, (byte) 0xAA)));
        Assert.That(r.Available, Is.EqualTo(0), "the GMS2 kind 9 body ends at the second rgb");
    }

    [Ignore("GMS2 field-light kind not read yet")]
    [Test] public void Gms2ListReadsEveryRecord() {
        var r = new ByteReader(List);
        var m = RcPkt.Read("FieldPropertyMessage", r, ProtocolProfile.Gms2);
        var props = (System.Collections.IEnumerable) RcPkt.MemberAny(m, "Properties", "Items");
        var list = props.Cast<object>().ToArray();
        Assert.That(list.Length, Is.EqualTo(2));
        Assert.That(RcPkt.Member<byte>(list[0], "Kind"), Is.EqualTo((byte) 8));
        Assert.That((RcPkt.Member<byte>(list[0], "R"), RcPkt.Member<byte>(list[0], "G"), RcPkt.Member<byte>(list[0], "B")),
            Is.EqualTo(((byte) 0x83, (byte) 0xA0, (byte) 0xD1)));
        Assert.That(RcPkt.Member<byte>(list[1], "Kind"), Is.EqualTo((byte) 9));
        Assert.That((RcPkt.Member<byte>(list[1], "R1"), RcPkt.Member<byte>(list[1], "G1"), RcPkt.Member<byte>(list[1], "B1")),
            Is.EqualTo(((byte) 0x86, (byte) 0xA0, (byte) 0x8F)));
        Assert.That((RcPkt.Member<byte>(list[1], "R2"), RcPkt.Member<byte>(list[1], "G2"), RcPkt.Member<byte>(list[1], "B2")),
            Is.EqualTo(((byte) 0x82, (byte) 0x82, (byte) 0x82)));
        Assert.That(r.Available, Is.EqualTo(0), "the list must read both records and the trailing rgb");
    }
}
