using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Trigger: GMS2 type 8 is the Ui command, 0x12 SidePopup; the wire type is the KMS client type minus one from 8
/// upward. Types 0/1, 2 and 5 pass today. Layouts: the GMS2 packet layout notes §B6.</summary>
public class RcTriggerLayoutTests {
    // gms2 recording: packet 2257, 1 bytes
    static readonly byte[] Type0 = RcPkt.Hex(
        "00");
    // gms2 recording: packet 12256, 1 bytes
    static readonly byte[] Type1 = RcPkt.Hex(
        "01");
    // gms2 recording: packet 2398, 7 bytes
    static readonly byte[] Type5 = RcPkt.Hex(
        "0501A30F000000");
    // gms2 recording: packet 2317, 33 bytes
    static readonly byte[] Type8Movie = RcPkt.Hex(
        "08041900636F6D6D6F6E5C436F6D6D6F6E5F4F70656E696E672E75736D01000000");
    // gms2 recording: packet 3160, 14 bytes
    static readonly byte[] Type8Summary = RcPkt.Hex(
        "0802158880011588800170170000");
    // gms2 recording: packet 3577, 6 bytes
    static readonly byte[] Type8HideSummary = RcPkt.Hex(
        "080316888001");
    // gms2 recording: packet 5962, 89 bytes
    static readonly byte[] SidePopupTalk = RcPkt.Hex(
        "12008813000000000E004D61736F6E5F636C6F73654579650F006B6F2F4E70632F3030303032303939000016002A002A",
        "002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A00");
    // gms2 recording: packet 2795, 46 bytes
    static readonly byte[] SidePopupMovie = RcPkt.Hex(
        "1201000000001E00436F6D6D6F6E2F576F726C64496E766173696F6E5363656E65312E75736D0000000000000000");

    [Test] public void Types0And1CarryNoBody() {
        foreach (var bytes in new[] { Type0, Type1 }) {
            var r = new ByteReader(bytes);
            var list = TriggerObjects.Read(r, _ => null, out byte type);
            Assert.That(type, Is.EqualTo(bytes[0]));
            Assert.That(list, Is.Empty);
            Assert.That(r.Available, Is.EqualTo(0));
        }
    }

    [Test] public void Type5ReadsTheCameraObjectList() {
        var r = new ByteReader(Type5);
        Assert.That(r.ReadByte(), Is.EqualTo(TriggerObjects.CameraObjects));
        var (ids, end) = TriggerObjects.ReadCameraObjects(r);
        Assert.That(ids, Is.EqualTo(new[] { 4003 }));
        Assert.That(end, Is.False);
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Ignore("GMS2 trigger type not read yet")]
    [Test] public void Gms2Type8ReadsTheUiCommand() {
        var movie = ReadUi(Type8Movie, 4);
        Assert.That(movie, Is.Not.Null);
        Assert.That(RcPkt.MemberAny<string>(movie, "Path", "Value", "Movie", "Text"), Has.Length.EqualTo(25));
        Assert.That(RcPkt.MemberAny<int>(movie, "Argument", "Value2", "Id"), Is.EqualTo(1));
        ReadUi(Type8Summary, 2);
        ReadUi(Type8HideSummary, 3);
    }

    static object ReadUi(byte[] bytes, byte sub) {
        var r = new ByteReader(bytes);
        var ui = RcPkt.CallAny("TriggerObjects", new[] { "ReadUi", "ReadUiCommand", "ReadType8" },
            new[] { new object[] { r } });
        Assert.That(RcPkt.MemberAny<byte>(ui, "Sub", "Command"), Is.EqualTo(sub));
        Assert.That(r.Available, Is.EqualTo(0), "the Ui sub-command body must be read");
        return ui;
    }

    [Ignore("GMS2 trigger type not read yet")]
    [Test] public void Gms2Type12ReadsTheSidePopup() {
        var talk = ReadPopup(SidePopupTalk, kind: 0, duration: 5000, name: 0, icon: 14, sound: 15, script: 0, text: 22);
        Assert.That(talk, Is.Not.Null);
        ReadPopup(SidePopupMovie, kind: 1, duration: 0, name: 0, icon: 30, sound: 0, script: 0, text: 0);
    }

    static object ReadPopup(byte[] bytes, int kind, int duration, int name, int icon, int sound, int script, int text) {
        var r = new ByteReader(bytes);
        var p = RcPkt.CallAny("TriggerObjects", new[] { "ReadSidePopup", "ReadSidePopupCommand", "ReadType18" },
            new[] { new object[] { r } });
        Assert.That(RcPkt.MemberAny<byte>(p, "Kind"), Is.EqualTo(kind));
        Assert.That(RcPkt.MemberAny<int>(p, "NpcOrDuration", "Duration", "NpcId"), Is.EqualTo(duration));
        Assert.That(RcPkt.MemberAny<string>(p, "Name"), Has.Length.EqualTo(name));
        Assert.That(RcPkt.MemberAny<string>(p, "Icon", "Illustration"), Has.Length.EqualTo(icon));
        Assert.That(RcPkt.MemberAny<string>(p, "Sound", "Voice"), Has.Length.EqualTo(sound));
        Assert.That(RcPkt.MemberAny<string>(p, "Script"), Has.Length.EqualTo(script));
        Assert.That(RcPkt.MemberAny<string>(p, "Text"), Has.Length.EqualTo(text));
        Assert.That(r.Available, Is.EqualTo(0), "the four narrow strings and the wide text must be read");
        return p;
    }
}
