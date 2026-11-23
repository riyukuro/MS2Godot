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
        var m = FieldPropertyMessage.Read(r, ProtocolProfile.Gms2) as FieldPropertyMessage.Add;
        Assert.That(m, Is.Not.Null);
        Assert.That(m.Property.Kind, Is.EqualTo((byte) 10));
        Assert.That(((FieldProperty.EnableLocalCamera) m.Property).Value, Is.False);
        Assert.That(r.Available, Is.EqualTo(0));
    }

    // the add packet the user hit live ("Not enough space in packet": the KMS width on a GMS2 kind 9)
    static readonly byte[] UserKind9 = RcPkt.Hex(
        "0109E1E2E6DCDCDC");

    [Test] public void Gms2Kind8ReadsOnlyRgb() {
        foreach (var (bytes, rgb) in new[] { (Kind8, ((byte) 0x78, (byte) 0x77, (byte) 0xB7)), (Kind8B, ((byte) 0x80, (byte) 0x80, (byte) 0x80)) }) {
            var r = new ByteReader(bytes);
            var m = (FieldPropertyMessage.Add) FieldPropertyMessage.Read(r, ProtocolProfile.Gms2);
            var p = (FieldProperty.AmbientLight) m.Property;
            Assert.That((p.R, p.G, p.B), Is.EqualTo(rgb));
            Assert.That((p.Flag, p.Value), Is.EqualTo((false, 0f)), "the missing bool + f32 keep the constructor's defaults");
            Assert.That(r.Available, Is.EqualTo(0), "the GMS2 kind 8 body ends at the rgb");
        }
    }

    [Test] public void Gms2Kind9ReadsTwoRgb() {
        var r = new ByteReader(Kind9);
        var m = (FieldPropertyMessage.Add) FieldPropertyMessage.Read(r, ProtocolProfile.Gms2);
        var p = (FieldProperty.DirectionalLight) m.Property;
        Assert.That((p.R1, p.G1, p.B1), Is.EqualTo(((byte) 0xC1, (byte) 0x64, (byte) 0x77)));
        Assert.That((p.R2, p.G2, p.B2), Is.EqualTo(((byte) 0xAA, (byte) 0xAA, (byte) 0xAA)));
        Assert.That((p.Flag, p.Value), Is.EqualTo((false, 0f)));
        Assert.That(r.Available, Is.EqualTo(0), "the GMS2 kind 9 body ends at the second rgb");
    }

    [Test] public void Gms2LiveAddReadsClean() {
        var r = new ByteReader(UserKind9);
        var m = (FieldPropertyMessage.Add) FieldPropertyMessage.Read(r, ProtocolProfile.Gms2);
        Assert.That(m.Property, Is.EqualTo(new FieldProperty.DirectionalLight(0xE1, 0xE2, 0xE6, 0xDC, 0xDC, 0xDC, false, 0f)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void Gms2ListReadsEveryRecord() {
        var r = new ByteReader(List);
        var m = (FieldPropertyMessage.List) FieldPropertyMessage.Read(r, ProtocolProfile.Gms2);
        Assert.That(m.Properties, Is.EqualTo(new FieldProperty[] {
            new FieldProperty.AmbientLight(0x83, 0xA0, 0xD1, false, 0f),
            new FieldProperty.DirectionalLight(0x86, 0xA0, 0x8F, 0x82, 0x82, 0x82, false, 0f) }));
        Assert.That(r.Available, Is.EqualTo(0), "the list must read both records and the trailing rgb");
    }

    [Test] public void KmsWidthsAreUnchanged() {
        // the KMS bodies keep their bool + f32 (8 + 14 bytes), under the default and the explicit KMS profile
        var kms = RcPkt.Hex("00020000000878" + "77B7" + "01" + "0000C03F" + "09010203040506" + "00" + "00002040");
        foreach (var m in new[] { FieldPropertyMessage.Read(new ByteReader(kms)), FieldPropertyMessage.Read(new ByteReader(kms), ProtocolProfile.Kms2525) }) {
            Assert.That(((FieldPropertyMessage.List) m).Properties, Is.EqualTo(new FieldProperty[] {
                new FieldProperty.AmbientLight(0x78, 0x77, 0xB7, true, 1.5f),
                new FieldProperty.DirectionalLight(1, 2, 3, 4, 5, 6, false, 2.5f) }));
        }
        // and a GMS2-width record under the KMS profile still runs out of bytes (the KMS read is not loosened)
        Assert.That(() => FieldPropertyMessage.Read(new ByteReader(UserKind9), ProtocolProfile.Kms2525), Throws.Exception);
    }
}
