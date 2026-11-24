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
        var other = CinematicPacket.Read(R(w => { w.WriteByte(13); w.WriteInt(1); }));
        Assert.That(other, Is.EqualTo(new CinematicPacket.Other(13)));
        c.Apply(other);
        Assert.That(c.IsShowing, Is.False);
    }

    [Test]
    public void SkipTalksBalloonsAndTheOpeningKeepTheClientsLists()
    {
        var c = new CutSceneCinematic();
        c.Apply(CinematicPacket.Read(R(w => { w.WriteByte(4); w.WriteByte(1); w.WriteString("next"); })));
        Assert.That((c.SkipState, c.SkipMessage), Is.EqualTo((1, "next")));
        c.Apply(CinematicPacket.Read(R(w => { w.WriteByte(6); w.WriteInt(11); w.WriteString(""); w.WriteUnicodeString("hi"); w.WriteInt(3000); w.WriteByte(0); })));
        c.Apply(CinematicPacket.Read(R(w => { w.WriteByte(6); w.WriteInt(11); w.WriteString(""); w.WriteUnicodeString(""); w.WriteInt(3000); w.WriteByte(0); })));
        Assert.That(c.Talks, Has.Count.EqualTo(1), "a talk with no text is not queued");
        c.Apply(CinematicPacket.Read(R(w => { w.WriteByte(8); w.WriteByte(0); w.WriteInt(5); w.WriteUnicodeString("yo"); w.WriteInt(1000); w.WriteInt(0); })));
        Assert.That(c.BalloonGroups[5], Has.Count.EqualTo(1));
        c.Apply(CinematicPacket.Read(R(w => { w.WriteByte(9); w.WriteInt(5); })));
        Assert.That(c.BalloonGroups[5], Is.Empty);
        c.Apply(CinematicPacket.Read(R(w => w.WriteByte(7))));
        Assert.That(c.Talks, Is.Empty);
        c.Apply(CinematicPacket.Read(R(w => { w.WriteByte(11); w.WriteUnicodeString("open"); w.WriteBool(true); })));
        Assert.That((c.Mode, c.Text, c.IsOpening, c.KeyMode), Is.EqualTo((2, "open", true, true)));
        c.Apply(CinematicPacket.Read(R(w => w.WriteByte(5))));
        Assert.That((c.SkipState, c.SkipMessage), Is.EqualTo((0, "")));
        c.Apply(CinematicPacket.Read(R(w => { w.WriteByte(12); w.WriteUnicodeString("intro"); })));
        Assert.That((c.Mode, c.Text), Is.EqualTo((3, "intro")));
    }

    /// <summary>The job intro's last state turns the key group on and starts cinematic 3, then moves the player to another field
    /// with no key mode off: the field's unload leaves the cinematic sub scene, so the key group (and the pane with it) goes off.</summary>
    [Test]
    public void TheFieldsUnloadTurnsTheKeyGroupOff()
    {
        var c = new CutSceneCinematic();
        var changes = new List<bool>();
        c.KeyModeChanged += changes.Add;
        c.Apply(CinematicPacket.Read(R(w => { w.WriteByte(1); w.WriteBool(true); })));
        c.Apply(CinematicPacket.Read(R(w => { w.WriteByte(3); w.WriteInt(3); w.WriteUnicodeString(""); w.WriteUnicodeString(""); })));
        c.FieldUnloaded();
        Assert.That(c.KeyMode, Is.False);
        Assert.That(changes, Is.EqualTo(new[] { true, false }));
        Assert.That((c.Mode, c.Id, c.IsShowing), Is.EqualTo((1, 3, true)), "the client keeps the mode, id and text");
        c.FieldUnloaded();
        Assert.That(changes, Has.Count.EqualTo(2), "out of the sub scene already: no change");
    }
}
