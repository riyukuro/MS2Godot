using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class TriggerObjectsTests
{
    static ByteReader R(ByteWriter w) => new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());

    static readonly Dictionary<int, TriggerObjectKind> Field = new()
    {
        [3001] = TriggerObjectKind.Mesh, [7] = TriggerObjectKind.Cube, [8] = TriggerObjectKind.Actor, [9] = TriggerObjectKind.Rope,
        [10] = TriggerObjectKind.Effect, [11] = TriggerObjectKind.Camera, [12] = TriggerObjectKind.Plain,
    };
    static TriggerObjectKind? KindOf(int id) => Field.TryGetValue(id, out var k) ? k : null;

    [Test]
    public void Gms2TypesFromEightAreOneLower()
    {
        foreach (byte t in new byte[] { 2, 3, 5, 9, 0xf })
            Assert.That(TriggerObjects.ClientType(t, ProtocolProfile.Kms2525), Is.EqualTo(t));
        foreach (byte t in new byte[] { 0, 1, 2, 3, 5, 7 })
            Assert.That(TriggerObjects.ClientType(t, ProtocolProfile.Gms2), Is.EqualTo(t));
        Assert.That(TriggerObjects.ClientType(8, ProtocolProfile.Gms2), Is.EqualTo(TriggerObjects.Guide));
        Assert.That(TriggerObjects.ClientType(0xe, ProtocolProfile.Gms2), Is.EqualTo(0xf));
        Assert.That(TriggerObjects.ClientType(0x12, ProtocolProfile.Gms2), Is.EqualTo(0x13));
        Assert.That(TriggerObjects.ClientType(0x14, ProtocolProfile.Gms2), Is.EqualTo(0x15));
    }

    [Test]
    public void LoadReadsEachObjectByItsKind()
    {
        var w = new ByteWriter();
        w.WriteByte(TriggerObjects.Load); w.WriteInt(7);
        w.WriteInt(3001); w.WriteBool(false); w.WriteBool(true); w.WriteInt(5); w.WriteUnicodeString("m"); w.WriteFloat(1.005f);
        w.WriteInt(7); w.WriteBool(true);
        w.WriteInt(8); w.WriteBool(true); w.WriteUnicodeString("Idle_A");
        w.WriteInt(9); w.WriteBool(true); w.WriteBool(false); w.WriteInt(3);
        w.WriteInt(10); w.WriteBool(true); w.WriteByte(2); w.WriteInt(4);
        w.WriteInt(11); w.WriteByte(1);
        w.WriteInt(12);                                    // a plain TriggerObject reads nothing more
        var r = R(w);
        var ev = TriggerObjects.Read(r, KindOf);
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(ev.Select(e => e.Kind), Is.EqualTo(new[] { TriggerObjectKind.Mesh, TriggerObjectKind.Cube, TriggerObjectKind.Actor,
            TriggerObjectKind.Rope, TriggerObjectKind.Effect, TriggerObjectKind.Camera, TriggerObjectKind.Plain }));
        Assert.That((ev[0].Id, ev[0].Show, ev[0].Flag, ev[0].Fade, ev[0].Text, ev[0].Scale), Is.EqualTo((3001, false, (byte)1, 5, "m", 1.005f)));
        Assert.That((ev[2].Show, ev[2].Text), Is.EqualTo((true, "Idle_A")));
        Assert.That((ev[3].Show, ev[3].Flag2, ev[3].Fade), Is.EqualTo((true, false, 3)));
        Assert.That((ev[4].Flag, ev[4].Fade), Is.EqualTo(((byte)2, 4)));
        Assert.That(ev[5].Flag, Is.EqualTo(1));
    }

    // FUN_141863a90: an id the service does not hold is logged and its fields are not read
    [Test]
    public void UnknownIdReadsOnlyTheId()
    {
        var w = new ByteWriter();
        w.WriteByte(TriggerObjects.Load); w.WriteInt(2);
        w.WriteInt(555);
        w.WriteInt(7); w.WriteBool(true);
        var ev = TriggerObjects.Read(R(w), KindOf);
        Assert.That(ev.Select(e => (e.Id, e.Known)), Is.EqualTo(new[] { (555, false), (7, true) }));
    }

    [Test]
    public void UpdateIsOneEventAndOtherTypesNone()
    {
        var w = new ByteWriter();
        w.WriteByte(TriggerObjects.Update); w.WriteInt(7); w.WriteBool(false);
        Assert.That(TriggerObjects.Read(R(w), KindOf).Single().Show, Is.False);
        var c = new ByteWriter(); c.WriteByte(5); c.WriteByte(0);
        Assert.That(TriggerObjects.Read(R(c), KindOf), Is.Empty);
    }

    // TriggerObject::Create's order; no MS2TriggerObject mixin: no trigger object (MS2TriggerSound's flat lacks it)
    [Test]
    public void KindFollowsCreateOrder()
    {
        Func<string, bool> Has(params string[] m) => n => m.Contains(n);
        Assert.That(TriggerObjects.KindOf(Has("MS2TriggerMesh", "MS2TriggerObject")), Is.EqualTo(TriggerObjectKind.Mesh));
        Assert.That(TriggerObjects.KindOf(Has("MS2TriggerActorCube", "MS2TriggerActor", "MS2TriggerObject")), Is.EqualTo(TriggerObjectKind.Actor));
        Assert.That(TriggerObjects.KindOf(Has("MS2TriggerModel", "MS2TriggerObject")), Is.EqualTo(TriggerObjectKind.Plain));
        Assert.That(TriggerObjects.KindOf(Has("MS2TriggerSound")), Is.Null);
        Assert.That(TriggerObjects.KindOf(Has("MS2TriggerMesh")), Is.Null);
    }
}
