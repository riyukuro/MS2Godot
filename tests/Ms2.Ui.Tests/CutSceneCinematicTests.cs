using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The Cinematic packet (CSceneStateGame::OnCinematic, UICutSceneCinematic::OnPacket uicutscenecinematic.cpp:302): types 1 to 3,
/// and its opcode under both profiles.</summary>
public class CutSceneCinematicTests
{
    static ByteReader R(Action<ByteWriter> f)
    {
        var w = new ByteWriter();
        f(w);
        return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
    }

    [Test]
    public void TheOpcodeIs0x66OnKmsAnd0x68OnGms2()
    {
        Assert.That(ProtocolProfile.Kms2525.Number(Inbound.Cinematic), Is.EqualTo(0x66));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.Cinematic), Is.EqualTo(0x68));
    }

    [Test]
    public void KeyModeStartAndCloseDriveTheState()
    {
        var c = new CutSceneCinematic();
        c.Apply(CinematicPacket.Read(R(w => { w.WriteByte(1); w.WriteBool(true); })));
        Assert.That(c.KeyMode, Is.True);
        c.Apply(CinematicPacket.Read(R(w => { w.WriteByte(3); w.WriteInt(7); w.WriteUnicodeString("hello"); w.WriteUnicodeString("x"); })));
        Assert.That((c.Id, c.Text, c.IsShowing), Is.EqualTo((7, "hello", true)));
        c.Apply(CinematicPacket.Read(R(w => w.WriteByte(2))));
        Assert.That(c.IsShowing, Is.False);
        c.Apply(CinematicPacket.Read(R(w => { w.WriteByte(1); w.WriteBool(false); })));
        Assert.That(c.KeyMode, Is.False);
    }

    [Test]
    public void StartWithIdZeroDoesNothingAndOtherTypesAreLeftAlone()
    {
        var c = new CutSceneCinematic();
        c.Apply(CinematicPacket.Read(R(w => { w.WriteByte(3); w.WriteInt(0); w.WriteUnicodeString("t"); w.WriteUnicodeString("") ; })));
        Assert.That(c.IsShowing, Is.False);
        var other = CinematicPacket.Read(R(w => { w.WriteByte(6); w.WriteInt(1); }));
        Assert.That(other, Is.EqualTo(new CinematicPacket.Other(6)));
        c.Apply(other);
        Assert.That(c.IsShowing, Is.False);
    }
}
