using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Mentoring (KMS 0x10A, GMS2 0x10C): CMentoringManager::OnPacket (mentoringmanager.cpp:255). Recorded on GMS2: modes 3 and 12 only; the rest is client-derived bytes.</summary>
public class RcMentoringTests {
    static ByteReader Hex(string s) => new(RcPkt.Hex(s.Replace(" ", "")));
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }
    static void Entry(ByteWriter w, long a, string name) {
        w.WriteLong(a); w.WriteLong(2); w.WriteLong(3); w.WriteUnicodeString(name); w.WriteShort(60); w.WriteUnicodeString("Guild");
        w.WriteInt(7); w.WriteBool(true); w.WriteInt(9);
    }

    // recorded: an empty mentee list (5 bytes)
    [Test] public void EmptyMenteeList() {
        var m = MentoringPacket.Read(Hex("03 00000000"));
        Assert.That(m.Mode, Is.EqualTo(3));
        Assert.That(m.Entries, Is.Empty);
    }

    // recorded: 17 bytes, two ids
    [Test] public void IdsAreTwoInt64() {
        var m = MentoringPacket.Read(Hex("0C 0102030405060708 1112131415161718"));
        Assert.That((m.Id, m.IdB), Is.EqualTo((0x0807060504030201L, 0x1817161514131211L)));
    }

    [Test] public void ListsHoldEntries() {
        foreach (byte mode in new byte[] { 2, 3 }) {
            var m = MentoringPacket.Read(R(w => { w.WriteByte(mode); w.WriteInt(2); Entry(w, 11, "Ann"); Entry(w, 12, "Bo"); }));
            Assert.That(m.Entries.Select(e => (e.A, e.Name)), Is.EqualTo(new[] { (11L, "Ann"), (12L, "Bo") }));
            Assert.That(m.Entries[0], Is.EqualTo(new MentorEntry(11, 2, 3, "Ann", 60, "Guild", 7, true, 9)));
        }
    }

    [Test] public void PairModesAreIdAndFlag() {
        var m = MentoringPacket.Read(R(w => { w.WriteByte(15); w.WriteInt(2); w.WriteLong(5); w.WriteBool(true); w.WriteLong(6); w.WriteBool(false); }));
        Assert.That(m.PairModes, Is.EqualTo(new[] { new MentorPairMode(5, true), new MentorPairMode(6, false) }));
    }

    [Test] public void ResultAndErrorAreByteAndInt() {
        foreach (byte mode in new byte[] { 1, 16 }) {
            var m = MentoringPacket.Read(R(w => { w.WriteByte(mode); w.WriteByte(4); w.WriteInt(77); }));
            Assert.That((m.Result, m.Value), Is.EqualTo(((byte) 4, 77)));
        }
    }

    [Test] public void InviteIsIdAndName() {
        var m = MentoringPacket.Read(R(w => { w.WriteByte(5); w.WriteLong(9); w.WriteUnicodeString("Mochi"); }));
        Assert.That((m.Id, m.Name), Is.EqualTo((9L, "Mochi")));
    }

    [Test] public void ApprovedAndDeclinedAreOneId() {
        foreach (byte mode in new byte[] { 6, 7 })
            Assert.That(MentoringPacket.Read(R(w => { w.WriteByte(mode); w.WriteLong(42); })).Id, Is.EqualTo(42));
    }

    [Test] public void DateAndCounts() {
        var d = MentoringPacket.Read(R(w => { w.WriteByte(10); w.WriteInt(1700000000); w.WriteLong(3); }));
        Assert.That((d.Value, d.Id), Is.EqualTo((1700000000, 3L)));
        var c = MentoringPacket.Read(R(w => { w.WriteByte(11); w.WriteInt(1); w.WriteInt(2); w.WriteLong(3); }));
        Assert.That((c.Value, c.ValueB, c.Id), Is.EqualTo((1, 2, 3L)));
    }

    [Test] public void RequestIsNameAndEntry() {
        var m = MentoringPacket.Read(R(w => { w.WriteByte(14); w.WriteUnicodeString("Req"); Entry(w, 21, "Cy"); }));
        Assert.That((m.Name, m.Entries[0].A), Is.EqualTo(("Req", 21L)));
    }

    [Test] public void ModesWithoutAPayloadReadNothingMore() {
        foreach (byte mode in new byte[] { 4, 8, 9, 13, 99 })
            Assert.That(MentoringPacket.Read(Hex($"{mode:X2} 0102")).Mode, Is.EqualTo(mode));
    }

    [Test] public void TrailingBytesLongNamesAndImpossibleCountsAreRefused() {
        Assert.Throws<PacketLayoutException>(() => MentoringPacket.Read(Hex("03 00000000 00")));
        Assert.Throws<PacketLayoutException>(() => MentoringPacket.Read(Hex("0C 0102030405060708 1112131415161718 00")));
        Assert.Throws<InvalidDataException>(() => MentoringPacket.Read(Hex("02 FFFFFFFF 00")));
        Assert.Throws<InvalidDataException>(() => MentoringPacket.Read(R(w => { w.WriteByte(5); w.WriteLong(1); w.WriteUnicodeString(new string('a', 25)); })));
        Assert.Throws<IndexOutOfRangeException>(() => MentoringPacket.Read(Hex("0C 0102030405060708")));
    }

    [Test] public void EveryRecordedGms2PacketReadsToTheEnd() {
        var all = Gms2Sniffs.Inbound("gms2", ProtocolProfile.Gms2.Number(Inbound.Mentoring));
        Assert.That(all, Is.Not.Empty);
        var kinds = new HashSet<byte>();
        foreach (var (file, data) in all) {
            var r = new ByteReader(data);
            kinds.Add(MentoringPacket.Read(r).Mode);
            Assert.That(r.Available, Is.EqualTo(0), file);
        }
        Assert.That(kinds, Is.EquivalentTo(new byte[] { 3, 12 }));
    }

    [Test] public void NumberOnBothProfiles() {
        Assert.That(ProtocolProfile.Kms2525.Number(Inbound.Mentoring), Is.EqualTo((ushort) 0x10A));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.Mentoring), Is.EqualTo((ushort) 0x10C));
    }
}
