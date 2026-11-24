using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// GlobalPortal (UIGlobalPortalDialog::OnGlobalPortalPacket @0x1412c3020, uiglobalportaldialog.cpp:216; SendGlobalPortal :233) and
/// RecallScroll (CSceneStateGame::OnRecallScroll @0x141734530, scenestategame.cpp:8038; UIMessageBox::HandleRecallscrollconfirmButton,
/// uimessagebox.cpp:1641). The GlobalPortal bytes are retail recordings (KMS2 field 2000051, GMS2 field 2000085); no recording of
/// RecallScroll exists in either profile, so its bytes are built from the client's reads.
/// </summary>
public class GlobalPortalTests
{
    static byte[] Concat(params byte[][] parts) => parts.SelectMany(b => b).ToArray();
    static byte[] Str(string s) => Concat(BitConverter.GetBytes((ushort)s.Length), System.Text.Encoding.Unicode.GetBytes(s));
    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    const string KmsNotice =
        "00853801002C000000170073005F006D006100730073006900760065005F006500760065006E0074005F006D006500730073" +
        "006100670065001900530079007300740065006D005F005100750069007A005F0047006C006F00620061006C005F0050006F" +
        "007200740061006C001F0073005F006D006100730073006900760065005F006500760065006E0074005F006E0061006D0065" +
        "005F0062006C006F006F0064005F006D0069006E006500210073005F006D006100730073006900760065005F006500760065" +
        "006E0074005F006E0061006D0065005F0073007000720069006E0067005F0062006500610063006800210073005F006D0061" +
        "00730073006900760065005F006500760065006E0074005F006E0061006D0065005F006300720061007A0079005F00720075" +
        "006E006E0065007200";
    const string Gms2Notice =
        "0083380100D0000000170073005F006D006100730073006900760065005F006500760065006E0074005F006D006500730073" +
        "006100670065001900530079007300740065006D005F005100750069007A005F0047006C006F00620061006C005F0050006F" +
        "007200740061006C001E0073005F006D006100730073006900760065005F006500760065006E0074005F006E0061006D0065" +
        "005F007200650064005F006100720065006E006100240073005F006D006100730073006900760065005F006500760065006E" +
        "0074005F006E0061006D0065005F00640061006E0063006500640061006E00630065005F00730074006F007000200073005F" +
        "006D006100730073006900760065005F006500760065006E0074005F006E0061006D0065005F0074007200610070005F006D" +
        "0061007300740065007200";

    [Test]
    public void NumbersInBothProfiles()
    {
        Assert.That(ProtocolProfile.Kms2525.Number(Inbound.GlobalPortal), Is.EqualTo((ushort)0xC1));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.GlobalPortal), Is.EqualTo((ushort)0xC3));
        Assert.That(ProtocolProfile.Kms2525.Number(Outbound.GlobalPortal), Is.EqualTo((ushort)0x72));
        Assert.That(ProtocolProfile.Gms2.Number(Outbound.GlobalPortal), Is.EqualTo((ushort)0x73));
        Assert.That(ProtocolProfile.Kms2525.Number(Inbound.RecallScroll), Is.EqualTo((ushort)0xBC));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.RecallScroll), Is.EqualTo((ushort)0xBE));
        Assert.That(ProtocolProfile.Kms2525.Number(Outbound.RecallScroll), Is.EqualTo((ushort)0x6F));
        Assert.That(ProtocolProfile.Gms2.Number(Outbound.RecallScroll), Is.EqualTo((ushort)0x70));
    }

    [TestCase(KmsNotice, 0x13885, 0x2c, "s_massive_event_name_crazy_runner")]
    [TestCase(Gms2Notice, 0x13883, 0xd0, "s_massive_event_name_trap_master")]
    public void TheRetailNoticeReadsToTheEnd(string hex, int eventId, int param, string caption3)
    {
        var r = new ByteReader(Convert.FromHexString(hex));
        var p = (GlobalPortalPacket.Notice)GlobalPortalPacket.Read(r);
        Assert.That((p.EventId, p.Param), Is.EqualTo((eventId, param)));
        Assert.That(p.TextKey, Is.EqualTo("s_massive_event_message"));
        Assert.That(p.Sound, Is.EqualTo("System_Quiz_Global_Portal"));
        Assert.That(p.Caption1, Does.StartWith("s_massive_event_name_"));
        Assert.That(p.Caption3, Is.EqualTo(caption3));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test]
    public void Mode1EndsTheEventAndLeavesTheRetailTailUnread()
    {
        // retail mode 1 carries the event id after the mode; the client reads only the mode (FUN_1412c3640 reads nothing)
        var r = new ByteReader(Convert.FromHexString("0186380100"));
        Assert.That(GlobalPortalPacket.Read(r), Is.TypeOf<GlobalPortalPacket.End>());
        Assert.That(r.Available, Is.EqualTo(4));
    }

    [TestCase((byte)3)]
    [TestCase((byte)4)]
    [TestCase((byte)2)]
    [TestCase((byte)9)]
    public void OtherModesReadOnlyTheMode(byte mode)
    {
        var r = new ByteReader(new[] { mode });
        Assert.That(GlobalPortalPacket.Read(r), Is.EqualTo(new GlobalPortalPacket.Other(mode)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test]
    public void TheEnterSendIsTheRetailBytes()
    {
        Assert.That(Bytes(new GlobalPortalEnter(0x13885, 2)), Is.EqualTo(Convert.FromHexString("028538010002000000")), "KMS2 retail, field 2000051");
        Assert.That(new GlobalPortalEnter(1, 0).Op, Is.EqualTo(Outbound.GlobalPortal));
    }

    [Test]
    public void RecallScrollOpenAndClose()
    {
        var r = new ByteReader(Concat(new byte[] { 1, 2 }, Str("Partner"), BitConverter.GetBytes(7), BitConverter.GetBytes(2000062),
            BitConverter.GetBytes(9), BitConverter.GetBytes(1_700_000_120L)));
        var p = RecallScrollPacket.Read(r);
        Assert.That(p, Is.EqualTo(new RecallScrollPacket.Open(1, 2, "Partner", 7, 2000062, 9, 1_700_000_120L)));
        Assert.That(r.Available, Is.EqualTo(0));
        var c = new ByteReader(new byte[] { 0, 1 });
        Assert.That(RecallScrollPacket.Read(c), Is.EqualTo(new RecallScrollPacket.Close(1)));
        Assert.That(c.Available, Is.EqualTo(0));
        Assert.That(RecallScrollPacket.NoticeText(0), Is.EqualTo((0xb64, (string)null)));
        Assert.That(RecallScrollPacket.NoticeText(1), Is.EqualTo((0xb63, (string)null)));
        Assert.That(RecallScrollPacket.NoticeText(2), Is.EqualTo((0, "s_cash_recall_wedding_notice")));
    }

    [Test]
    public void RecallScrollWithBytesLeftOverIsALayoutError()
    {
        // OnRecallScroll reads each mode to its end (the i64 expiry, or the type for a close): more bytes are a layout mismatch
        Assert.Throws<PacketLayoutException>(() => RecallScrollPacket.Read(new ByteReader(new byte[] { 0, 1, 0 })));
        Assert.Throws<PacketLayoutException>(() => RecallScrollPacket.Read(new ByteReader(Concat(new byte[] { 1, 0 }, Str("A"),
            BitConverter.GetBytes(0), BitConverter.GetBytes(2000062), BitConverter.GetBytes(0), BitConverter.GetBytes(1L), new byte[] { 0 }))));
    }

    [Test]
    public void RecallScrollAcceptSendsSlotNameAndField()
    {
        Assert.That(Bytes(new RecallScrollAccept(1, "Leader", 2000062)),
            Is.EqualTo(Concat(new byte[] { 1 }, Str("Leader"), BitConverter.GetBytes(2000062))));
        Assert.That(new RecallScrollAccept(0, "", 0).Op, Is.EqualTo(Outbound.RecallScroll));
    }
}
