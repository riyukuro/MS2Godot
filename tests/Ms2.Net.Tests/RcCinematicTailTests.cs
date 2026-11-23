using Ms2.Net;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Cinematic (0x68) types 4-12 that main returns as Other. and the client's
/// UICutSceneCinematic::OnPacket (uicutscenecinematic.cpp:302-321, handlers :583-797). Samples are GMS2 recordings,
/// read as CinematicPacket.Talk (type 6), .BalloonTalk (8), .Caption (10), .Opening (11), .Intro (12) and the bodiless .ResetSkip (5) and .ClearTalks (7), plus .ClearBalloonGroup (9).
/// </summary>
public class RcCinematicTailTests
{
    // gms2 recording: packet 2711, 60 bytes: npc 11003431, empty illustration, 23-char script, 3000 ms, align 0
    static readonly byte[] Talk = RcLane.Hex(
        "0627E6A70000001700" + string.Concat(Enumerable.Repeat("2A00", 23)) + "B80B000000");
    // gms2 recording: packet 2816, 60 bytes: not an npc, object 694978, 22-char script, 3000 ms, no delay
    static readonly byte[] Balloon = RcLane.Hex(
        "0800C29A0A00" + "1600" + string.Concat(Enumerable.Repeat("2A00", 22)) + "B80B0000" + "00000000");
    // gms2 recording: packet 6447, 113 bytes: type 11, title 27, no script, align 6, 3500 ms, (-0.15, 0.15), scale 2
    static readonly byte[] Caption = RcLane.Hex(
        "0A0B00" + string.Concat(Enumerable.Repeat("2A00", 11)) +
        "1B00" + string.Concat(Enumerable.Repeat("2A00", 27)) +
        "0000" +
        "0600" + string.Concat(Enumerable.Repeat("2A00", 6)) +
        "AC0D0000" + "9A9919BE" + "9A99193E" + "00000040");

    static object Type(byte[] bytes, string notThis, string expected)
    {
        var p = RcLane.Read("CinematicPacket", bytes);
        Assert.That(p.GetType().Name, Is.Not.EqualTo(notThis), $"type byte {bytes[0]} is still returned as {notThis}, not {expected}");
        return p;
    }

    [Test]
    public void Gms2Type6ReadsTalk()
    {
        var p = Type(Talk, "Other", "Talk");
        Assert.That(RcLane.Long(p, "NpcId"), Is.EqualTo(11_003_431L));
        Assert.That(RcLane.MemberAny<string>(p, "Illustration", "Illust"), Is.Empty);
        Assert.That(RcLane.MemberAny<string>(p, "Script", "Text"), Has.Length.EqualTo(23));
        Assert.That(RcLane.Int(p, "Duration"), Is.EqualTo(3000));
        Assert.That(RcLane.Int(p, "Flag"), Is.EqualTo(0));
    }

    [Test]
    public void Gms2Type8ReadsBalloonTalk()
    {
        var p = Type(Balloon, "Other", "BalloonTalk");
        Assert.That(RcLane.Bool(p, "Raw"), Is.False);
        Assert.That(RcLane.Long(p, "ObjectId"), Is.EqualTo(694_978L));
        Assert.That(RcLane.MemberAny<string>(p, "Text"), Has.Length.EqualTo(22));
        Assert.That(RcLane.Int(p, "Duration"), Is.EqualTo(3000));
        Assert.That(RcLane.Int(p, "Delay"), Is.EqualTo(0));
    }

    [Test]
    public void Gms2Type10ReadsCaption()
    {
        var p = Type(Caption, "Other", "Caption");
        Assert.That(RcLane.MemberAny<string>(p, "Type"), Has.Length.EqualTo(11));
        Assert.That(RcLane.MemberAny<string>(p, "Text1"), Has.Length.EqualTo(27));
        Assert.That(RcLane.MemberAny<string>(p, "Text2"), Is.Empty);
        Assert.That(RcLane.MemberAny<string>(p, "Align"), Has.Length.EqualTo(6));
        Assert.That(RcLane.Int(p, "Duration"), Is.EqualTo(3500));
        Assert.That(RcLane.MemberAny<float>(p, "OffsetX", "X"), Is.EqualTo(-0.15f).Within(0.001f));
        Assert.That(RcLane.MemberAny<float>(p, "OffsetY", "Y"), Is.EqualTo(0.15f).Within(0.001f));
        Assert.That(RcLane.MemberAny<float>(p, "Scale"), Is.EqualTo(2f));
    }

    [Test]
    public void Gms2TypesWithoutABodyAreTheirRecords()
    {
        Assert.That(RcLane.Read("CinematicPacket", RcLane.Bytes("05")).GetType().Name, Is.EqualTo("ResetSkip"));
        Assert.That(RcLane.Read("CinematicPacket", RcLane.Bytes("07")).GetType().Name, Is.EqualTo("ClearTalks"));
        var remove = RcLane.Read("CinematicPacket", RcLane.Bytes("0901000000"));
        Assert.That(remove.GetType().Name, Is.EqualTo("ClearBalloonGroup"));
        Assert.That(RcLane.Int(remove, "ObjectId"), Is.EqualTo(1));
        var opening = RcLane.Read("CinematicPacket", RcLane.Bytes("0B030061006200630001"));
        Assert.That(opening.GetType().Name, Is.EqualTo("Opening"));
        Assert.That(RcLane.MemberAny<string>(opening, "Text"), Is.EqualTo("abc"));
        Assert.That(RcLane.MemberAny<bool>(opening, "IsOpening"), Is.True);
        var intro = RcLane.Read("CinematicPacket", RcLane.Bytes("0C0300610062006300"));
        Assert.That(intro.GetType().Name, Is.EqualTo("Intro"));
        Assert.That(RcLane.MemberAny<string>(intro, "Text"), Is.EqualTo("abc"));
    }

    // kms recording (a tutorial map): packet 516, 74 bytes: npc 11001217, illustration "11001217", 26-char script, 2000 ms, flag 0
    static readonly byte[] KmsTalk = RcLane.Hex(
        "0681DDA700" + "0800" + "3131303031323137" + "1A00" + string.Concat(Enumerable.Repeat("2A00", 26)) + "D007000000");

    [Test]
    public void KmsRecordedTalkReadsTheSameWay()
    {
        // the layouts are the client's own, so one reader serves both profiles
        var p = RcLane.Read("CinematicPacket", KmsTalk);
        Assert.That(p.GetType().Name, Is.EqualTo("Talk"));
        Assert.That(RcLane.Long(p, "NpcId"), Is.EqualTo(11_001_217L));
        Assert.That(RcLane.MemberAny<string>(p, "Illustration"), Is.EqualTo("11001217"));
        Assert.That(RcLane.MemberAny<string>(p, "Script"), Has.Length.EqualTo(26));
        Assert.That(RcLane.Int(p, "Duration"), Is.EqualTo(2000));
    }
}
