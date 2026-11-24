using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>WorldService m_848 and its Trigger types 0x10-0x12 (worldservice_1.cpp:7302-7345), with OnActorAdded (:3598).</summary>
public class FieldEffectNifsTests
{
    static (FieldEffectNifs t, List<string> log, HashSet<int> loaded) Make()
    {
        var log = new List<string>();
        var loaded = new HashSet<int>();
        var t = new FieldEffectNifs
        {
            Loaded = loaded.Contains,
            Attach = (id, e) => log.Add($"attach {id} {e.NifPath}"),
            Detach = id => log.Add($"detach {id}"),
        };
        return (t, log, loaded);
    }

    static byte[] Body(byte wireType, Action<ByteWriter> rest)
    {
        var w = new ByteWriter();
        w.WriteByte(wireType);
        rest(w);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }

    static void Effect(ByteWriter w, int id, string nif) { w.WriteInt(id); w.WriteString(nif); w.WriteBool(false); w.WriteFloat(1f); w.WriteFloat(0f); }

    [Test]
    public void LoadKeepsEveryEntryAndAttachesTheLoadedOnesWithoutADetach()
    {
        var (t, log, loaded) = Make();
        loaded.Add(1);
        Assert.That(t.ApplyTrigger(Body(0x10, w => { w.WriteInt(2); w.WriteInt(1); Effect(w, 1, "a.nif"); w.WriteInt(2); Effect(w, 2, "b.nif"); }), ProtocolProfile.Kms2525), Is.True);
        Assert.That(log, Is.EqualTo(new[] { "attach 1 a.nif" }));
        Assert.That(t.Count, Is.EqualTo(2));
        // a second load keeps the old entries and attaches every loaded one again (the actor ignores an attach while it has one)
        log.Clear(); loaded.Add(2);
        t.ApplyTrigger(Body(0x10, w => { w.WriteInt(1); w.WriteInt(3); Effect(w, 3, "c.nif"); }), ProtocolProfile.Kms2525);
        Assert.That(log, Is.EquivalentTo(new[] { "attach 1 a.nif", "attach 2 b.nif" }));
        Assert.That(t.Count, Is.EqualTo(3));
    }

    [Test]
    public void AddDetachesFirstAndRemoveDropsTheEntry()
    {
        var (t, log, loaded) = Make();
        loaded.Add(5);
        t.ApplyTrigger(Body(0x11, w => Effect(w, 5, "a.nif")), ProtocolProfile.Kms2525);
        t.ApplyTrigger(Body(0x11, w => Effect(w, 6, "b.nif")), ProtocolProfile.Kms2525);   // not loaded: kept, not attached
        Assert.That(log, Is.EqualTo(new[] { "detach 5", "attach 5 a.nif" }));
        Assert.That(t.Find(6)?.NifPath, Is.EqualTo("b.nif"));
        log.Clear();
        t.ApplyTrigger(Body(0x12, w => w.WriteInt(5)), ProtocolProfile.Kms2525);
        t.ApplyTrigger(Body(0x12, w => w.WriteInt(6)), ProtocolProfile.Kms2525);
        Assert.That(log, Is.EqualTo(new[] { "detach 5" }));
        Assert.That(t.Count, Is.Zero);
    }

    [Test]
    public void AnActorAddedLaterGetsItsEntryAndTheFieldLoadEmptiesTheTable()
    {
        var (t, log, _) = Make();
        t.Add(new TriggerEffectNif(8, "a.nif", true, 0.5f, 45f));
        t.ActorAdded(7);
        t.ActorAdded(8);
        Assert.That(log, Is.EqualTo(new[] { "attach 8 a.nif" }));
        t.Clear();
        log.Clear();
        t.ActorAdded(8);
        Assert.That(log, Is.Empty);
    }

    [Test]
    public void OtherTypesAreLeftAndGms2WireTypesAreOneLower()
    {
        var (t, log, loaded) = Make();
        loaded.Add(5);
        Assert.That(t.ApplyTrigger(Body(0x13, w => { }), ProtocolProfile.Kms2525), Is.False);
        Assert.That(t.ApplyTrigger(Array.Empty<byte>(), ProtocolProfile.Kms2525), Is.False);
        // GMS2 sends the client's 0x11 as 0x10 (TriggerObjects.ClientType)
        Assert.That(t.ApplyTrigger(Body(0x10, w => Effect(w, 5, "a.nif")), ProtocolProfile.Gms2), Is.True);
        Assert.That(log, Is.EqualTo(new[] { "detach 5", "attach 5 a.nif" }));
    }
}
