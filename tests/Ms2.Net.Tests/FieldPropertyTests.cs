using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>0xCA FieldProperty (WorldService dispatch, worldservice_1.cpp:924; each kind's Decode in fieldproperty.cpp).</summary>
public class FieldPropertyTests {
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    /// <summary>Every kind's record in kind order, as the server writes it.</summary>
    static void WriteAll(ByteWriter w) {
        w.WriteByte(1); w.WriteFloat(0.5f);
        w.WriteByte(2); w.WriteLong(1234567890123); w.WriteInt(42);
        w.WriteByte(3);
        w.WriteByte(4);
        w.WriteByte(5); w.WriteUnicodeString("tag"); w.WriteUnicodeString("symbol");
        w.WriteByte(6); w.WriteFloat(900); w.WriteFloat(800); w.WriteFloat(700); w.WriteFloat(115); w.WriteBool(true); w.WriteByte(45); w.WriteBool(false);
        w.WriteByte(7); w.WriteByte(3);
        w.WriteByte(8); w.WriteByte(10); w.WriteByte(20); w.WriteByte(30); w.WriteBool(true); w.WriteFloat(1.5f);
        w.WriteByte(9); w.WriteByte(1); w.WriteByte(2); w.WriteByte(3); w.WriteByte(4); w.WriteByte(5); w.WriteByte(6); w.WriteBool(false); w.WriteFloat(2.5f);
        w.WriteByte(10); w.WriteBool(true);
        w.WriteByte(11); w.WriteBool(true);
    }

    static readonly FieldProperty[] All = {
        new FieldProperty.Gravity(0.5f),
        new FieldProperty.MusicConcert(1234567890123, 42),
        new FieldProperty.HideMyPC(),
        new FieldProperty.LockMyPC(),
        new FieldProperty.UserTagSymbol("tag", "symbol"),
        new FieldProperty.SightRange(900, 800, 700, 115, true, 45, false),
        new FieldProperty.Weather(3),
        new FieldProperty.AmbientLight(10, 20, 30, true, 1.5f),
        new FieldProperty.DirectionalLight(1, 2, 3, 4, 5, 6, false, 2.5f),
        new FieldProperty.EnableLocalCamera(true),
        new FieldProperty.PhotoStudio(true),
    };

    [Test] public void ListReadsACountThenEveryKindsFieldsInOrder() {
        var r = R(w => { w.WriteByte(0); w.WriteInt(11); WriteAll(w); });
        var m = (FieldPropertyMessage.List) FieldPropertyMessage.Read(r);
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(m.Properties, Is.EqualTo(All));
        Assert.That(m.Properties.Select(p => (int)p.Kind), Is.EqualTo(Enumerable.Range(1, 11)));
    }

    [Test] public void ListStopsAtAnUnknownKindAndLeavesTheRestUnread() {
        var r = R(w => { w.WriteByte(0); w.WriteInt(3); w.WriteByte(7); w.WriteByte(2); w.WriteByte(12); w.WriteByte(7); w.WriteByte(5); });
        var m = (FieldPropertyMessage.List) FieldPropertyMessage.Read(r);
        Assert.That(m.Properties, Is.EqualTo(new FieldProperty[] { new FieldProperty.Weather(2) }));
        Assert.That(r.Available, Is.EqualTo(2));
        Assert.That(((FieldPropertyMessage.List) FieldPropertyMessage.Read(R(w => { w.WriteByte(0); w.WriteInt(0); }))).Properties, Is.Empty);
    }

    [Test] public void AddReadsOneRecordAndAnUnknownKindDoesNothing() {
        var r = R(w => { w.WriteByte(1); w.WriteByte(6); w.WriteFloat(1500); w.WriteFloat(1); w.WriteFloat(2); w.WriteFloat(3); w.WriteBool(false); w.WriteByte(80); w.WriteBool(true); });
        Assert.That(FieldPropertyMessage.Read(r), Is.EqualTo(new FieldPropertyMessage.Add(new FieldProperty.SightRange(1500, 1, 2, 3, false, 80, true))));
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(FieldPropertyMessage.Read(R(w => { w.WriteByte(1); w.WriteByte(0); })), Is.Null);
        Assert.That(FieldPropertyMessage.Read(R(w => { w.WriteByte(1); w.WriteByte(12); w.WriteFloat(1); })), Is.Null);
        Assert.That(() => FieldPropertyMessage.Read(R(w => { w.WriteByte(1); w.WriteByte(7); w.WriteByte(1); w.WriteByte(0); })), Throws.Exception);   // ExpectEnd
    }

    [Test] public void RemoveReadsTheKindOnlyAndOtherModesNothing() {
        Assert.That(FieldPropertyMessage.Read(R(w => { w.WriteByte(2); w.WriteByte(6); })), Is.EqualTo(new FieldPropertyMessage.Remove(6)));
        Assert.That(FieldPropertyMessage.Read(R(w => { w.WriteByte(3); w.WriteByte(6); })), Is.Null);
        Assert.That(() => FieldPropertyMessage.Read(R(w => { w.WriteByte(2); w.WriteByte(6); w.WriteByte(0); })), Throws.Exception);
    }
}
