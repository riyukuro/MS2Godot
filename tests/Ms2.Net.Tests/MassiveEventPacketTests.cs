using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// The MassiveEvent reader (UIMassiveEvent FUN_1419f5e90, uimassiveevent.cpp:183) on recorded KMS2 and GMS2 bodies, one of
/// each kind they hold (KMS2 311 files: kind 0 in 20 packets, 1 in 20, 2 in 111; GMS2 760 files: 0 in 61, 1 in 23, 2 in 159,
/// 7 in 38), and on the kinds the recordings lack, built the way the server fork writes them.
/// </summary>
public class MassiveEventPacketTests
{
    static MassiveEventMessage Read(string hex, out ByteReader r)
    {
        r = new ByteReader(Convert.FromHexString(hex));
        return MassiveEventMessage.Read(r);
    }

    [Test]
    public void TheStageKindReadsTheStageAndItsThreeValues()
    {
        var m = Read("0001000000050000000100000000000000", out var r);   // KMS2 and GMS2
        Assert.That((m.Kind, m.StageNumber, m.A, m.B, m.C), Is.EqualTo((MassiveEventMessage.Stage, 1, 5, 1, 0)));
        Assert.That(r.Available, Is.Zero);
    }

    [Test]
    public void TheStepKindReadsAServerStringKeyAndItsCountdown()
    {
        // GMS2: "$02000066_BF__MAIN__6$", step 1, 5 s, the voice count
        var m = Read("0116002400300032003000300030003000360036005F00420046005F005F004D00410049004E005F005F0036002400010000000500000001000000", out var r);
        Assert.That(m.Kind, Is.EqualTo(MassiveEventMessage.StepKey));
        Assert.That(m.Key, Does.StartWith("$").And.EndWith("$").And.Length.EqualTo(0x16));
        Assert.That((m.Step, m.Duration, m.SoundType), Is.EqualTo((1, 5, 1)));
        Assert.That(r.Available, Is.Zero);
    }

    [Test]
    public void TheTypedKindReadsTheMessageTypeTheKeyAndTheDuration()
    {
        // KMS2: type 6, "$61000007_ME__MAINPROCESS_SPRINGBEACH__0$", 5000
        var m = Read("020629002400360031003000300030003000300037005F004D0045005F005F004D00410049004E00500052004F0043004500530053005F0053005000520049004E004700420045004100430048005F005F003000240088130000", out var r);
        Assert.That((m.Kind, m.MessageType, m.Key.Length, m.Duration), Is.EqualTo((MassiveEventMessage.TypedKey, (byte)6, 0x29, 5000)));
        Assert.That(r.Available, Is.Zero);
    }

    [Test]
    public void TheRoundKindReadsTheRoundTheFinalFlagAndTheGoal()
    {
        var m = Read("070100000000B80B0000", out var r);   // GMS2
        Assert.That((m.Kind, m.RoundNumber, m.Final, m.Goal), Is.EqualTo((MassiveEventMessage.Round, 1, false, 3000)));
        Assert.That(r.Available, Is.Zero);
    }

    [TestCase(MassiveEventMessage.Text)]
    [TestCase(MassiveEventMessage.Win)]
    [TestCase(MassiveEventMessage.Lose)]
    public void TheStringIdKindsReadTheTextAndTheDuration(byte kind)
    {
        var w = new ByteWriter();
        w.WriteByte(kind);
        w.WriteBool(true); w.WriteInt(1); w.WriteInt(0x974); w.WriteInt(1); w.WriteUnicodeString("3");   // formatted, common string 0x974, one argument
        w.WriteInt(4000);
        var m = Read(Convert.ToHexString(w.Buffer, 0, w.Length), out var r);
        Assert.That((m.Kind, m.Message.Formatted, m.Message.Id, m.Message.Args.Length, m.Duration), Is.EqualTo((kind, true, 0x974, 1, 4000)));
        Assert.That(r.Available, Is.Zero);
    }

    [Test]
    public void TheStepTextAndPvpCountdownKindsReadTheirFields()
    {
        var w = new ByteWriter();
        w.WriteByte(MassiveEventMessage.StepText);
        w.WriteBool(false); w.WriteInt(0); w.WriteUnicodeString("t");
        w.WriteInt(2); w.WriteInt(3); w.WriteInt(2);
        var m = Read(Convert.ToHexString(w.Buffer, 0, w.Length), out var r);
        Assert.That((m.Message.Text, m.Step, m.Duration, m.SoundType), Is.EqualTo(("t", 2, 3, 2)));
        Assert.That(r.Available, Is.Zero);
        m = Read("0805000000", out r);
        Assert.That((m.Kind, m.Duration), Is.EqualTo((MassiveEventMessage.PvpCountdown, 5)));
        Assert.That(r.Available, Is.Zero);
    }

    [Test]
    public void AnUnknownKindReadsNothingMore()
    {
        var m = Read("0901020304", out var r);
        Assert.That(m.Kind, Is.EqualTo((byte)9));
        Assert.That(r.Available, Is.EqualTo(4));
    }
}
