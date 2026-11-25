using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// The Attendance packet (UIAttendGiftDialog::OnAttendancePacket @0x14102EBB0, uiattendgiftdialog.cpp:345-354), the GameEventUserValue
/// packet (FUN_140930fe0 @0x140930fe0) and the GameEventAttendGift entry (vfunc_4 @0x1403fb770, gameeventcontext.cpp:424-443), read
/// from the recordings of both regions (the whole lists) and from built bodies. The readers are new (the old code dropped the Attendance
/// and GameEventUserValue packets unread and skipped the AttendGift entry's fields), so these tests do not compile on the old code.
/// </summary>
public class AttendanceRecordedTests
{
    static ByteReader Reader(Action<ByteWriter> write)
    {
        var w = new ByteWriter();
        write(w);
        return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
    }

    // gms2 0xA6: kind 6, two started events (id 171008 ends 1561593599, id 171009 ends 1567092600)
    static readonly byte[] Gms2Started2 = Convert.FromHexString("0602000000009C0200FF06145D00000000019C020078EF675D00000000");
    // kms2 0xA4: kind 6, one started event
    static readonly byte[] Kms2Started1 = Convert.FromHexString("0601000000EE030000EF2AB65B00000000");
    // gms2 0xCD (entering the character select): kind 0, list 0, no values
    static readonly byte[] Gms2EmptyTable = Convert.FromHexString("000000000000");

    [Test]
    public void OpcodesMatchTheClientTables()
    {
        // opcodes.h: RecvOp::Attendance 0xA4 (GMS2 0xA6), RecvOp::GameEventUserValue 0xCB (GMS2 0xCD), SendOp::Attendance 0x66 (GMS2 0x67)
        Assert.That((ushort)Inbound.Attendance, Is.EqualTo(0xA4));
        Assert.That((ushort)Inbound.GameEventUserValue, Is.EqualTo(0xCB));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.Attendance), Is.EqualTo(0xA6));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.GameEventUserValue), Is.EqualTo(0xCD));
        Assert.That((ushort)Outbound.Attendance, Is.EqualTo(0x66));
        Assert.That(ProtocolProfile.Gms2.Number(Outbound.Attendance), Is.EqualTo(0x67));
    }

    [Test]
    public void RecordedStartedEventsRead()
    {
        var g = (AttendanceMessage.StartedEvents)AttendanceMessage.Read(new ByteReader(Gms2Started2));
        Assert.That(g.Events, Is.EqualTo(new[] { (171008, 1561593599L), (171009, 1567092600L) }));
        var k = (AttendanceMessage.StartedEvents)AttendanceMessage.Read(new ByteReader(Kms2Started1));
        Assert.That(k.Events, Is.EqualTo(new[] { (1006, 1538665199L) }));
    }

    [Test]
    public void TheKindsThePacketCarriesRead()
    {
        Assert.That(AttendanceMessage.Read(Reader(w => w.WriteByte(5))), Is.TypeOf<AttendanceMessage.RefreshAll>());
        Assert.That(((AttendanceMessage.SelectEvent)AttendanceMessage.Read(Reader(w => { w.WriteByte(7); w.WriteInt(170042); }))).EventId, Is.EqualTo(170042u));
        Assert.That(((AttendanceMessage.Error)AttendanceMessage.Read(Reader(w => { w.WriteByte(9); w.WriteByte(2); }))).Result, Is.EqualTo(2));
        // the client's switch has no other case: nothing more is read
        Assert.That(AttendanceMessage.Read(Reader(w => { w.WriteByte(8); w.WriteInt(1); })), Is.EqualTo(new AttendanceMessage.Other(8)));
        // a body longer than the kind's is a protocol error (ExpectEnd)
        Assert.That(() => AttendanceMessage.Read(Reader(w => { w.WriteByte(5); w.WriteByte(0); })), Throws.Exception);
        Assert.That(() => AttendanceMessage.Read(Reader(w => { w.WriteByte(7); w.WriteInt(1); w.WriteByte(0); })), Throws.Exception);
    }

    [Test]
    public void TheSendsWriteTheirModes()
    {
        static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
        Assert.That(Bytes(new AttendanceWindowOpened()), Is.EqualTo(new byte[] { 2 }));
        Assert.That(Bytes(new AttendanceMeratPay()), Is.EqualTo(new byte[] { 3 }));
        Assert.That(Bytes(new AttendanceEarlyParticipation(170042, 0x0102030405060708)), Is.EqualTo(Convert.FromHexString("043A9802000807060504030201")));
        Assert.That(Bytes(new AttendanceSelectEvent(170042)), Is.EqualTo(Convert.FromHexString("083A980200")));
    }

    [TestCase("gms2")]
    [TestCase("kms2")]
    public void EveryRecordedAttendancePacketReadsToItsEnd(string set)
    {
        ushort op = (ushort)(set == "gms2" ? ProtocolProfile.Gms2.Number(Inbound.Attendance) : (ushort)Inbound.Attendance);
        var all = Gms2Sniffs.Inbound(set, op);
        int kind6 = 0, events = 0;
        foreach (var (file, data) in all)
        {
            var m = AttendanceMessage.Read(new ByteReader(data));
            if (m is AttendanceMessage.StartedEvents s)
            {
                kind6++;
                foreach (var (id, end) in s.Events) { events++; Assert.That(id, Is.GreaterThan(0), file); Assert.That(end, Is.GreaterThan(0), file); }
            }
            else Assert.That(m, Is.Not.TypeOf<AttendanceMessage.Other>(), $"{file}: a kind the client's switch has");
        }
        TestContext.WriteLine($"{set}: {all.Count} Attendance packets, {kind6} kind 6, {events} started events");
    }

    [TestCase("gms2")]
    [TestCase("kms2")]
    public void EveryRecordedGameEventUserValueReadsToItsEnd(string set)
    {
        ushort op = (ushort)(set == "gms2" ? ProtocolProfile.Gms2.Number(Inbound.GameEventUserValue) : (ushort)Inbound.GameEventUserValue);
        var all = Gms2Sniffs.Inbound(set, op);
        int vars = 0, attend = 0;
        var attendTypes = new HashSet<int> { 0x65, 0x66, 0x67, 0x69, 0x6a };
        foreach (var (file, data) in all)
        {
            var m = GameEventUserValueMessage.Read(new ByteReader(data));
            Assert.That(m.Kind, Is.LessThanOrEqualTo(1), file);
            Assert.That(m.List, Is.InRange(0, 1), file);
            if (m.Kind == 1) Assert.That(m.Variables.Count, Is.EqualTo(1), file);
            foreach (var v in m.Variables)
            {
                vars++;
                Assert.That(v.Type, Is.GreaterThan(0), file);
                Assert.That(v.Value, Is.Not.Null, file);
                Assert.That(v.Time, Is.GreaterThan(0), file);
                if (attendTypes.Contains(v.Type)) { attend++; Assert.That(long.TryParse(v.Value, out _), $"{file}: attend value type {v.Type} '{v.Value}'"); }
            }
        }
        TestContext.WriteLine($"{set}: {all.Count} GameEventUserValue packets, {vars} variables, {attend} attendance value types");
    }

    [Test]
    public void RecordedUserValueTablesRead()
    {
        var empty = GameEventUserValueMessage.Read(new ByteReader(Gms2EmptyTable));
        Assert.That((empty.Kind, empty.List, empty.Variables.Count, empty.Replaces), Is.EqualTo(((byte)0, 0, 0, true)));
        // gms2 "Invis Badge": a 7-value table of types 0x258, 0x259 (the first value is the text "20021200")
        var t = GameEventUserValueMessage.Read(Reader(w =>
        {
            w.WriteByte(0); w.WriteByte(1); w.WriteInt(2);
            w.WriteInt(0x258); w.WriteInt(0x1D4E3); w.WriteUnicodeString("20021200"); w.WriteLong(0x5E7F6D78);
            w.WriteInt(0x259); w.WriteInt(0x1D4E3); w.WriteUnicodeString("1"); w.WriteLong(0x5E7F6D78);
        }));
        Assert.That(t.List, Is.EqualTo(1));
        Assert.That(t.Variables, Is.EqualTo(new[]
        {
            new GameEventVariable(0x258, 0x1D4E3, "20021200", 0x5E7F6D78), new GameEventVariable(0x259, 0x1D4E3, "1", 0x5E7F6D78),
        }));
    }

    [Test]
    public void AUserValueOfAnotherKindOrListReadsNothing()
    {
        // FUN_140930fe0's switch has only kinds 0 and 1 and lists 0 and 1: the rest of the body stays unread
        var k = GameEventUserValueMessage.Read(Reader(w => { w.WriteByte(2); w.WriteInt(99); }));
        Assert.That((k.Kind, k.List, k.Variables.Count), Is.EqualTo(((byte)2, -1, 0)));
        var l = GameEventUserValueMessage.Read(Reader(w => { w.WriteByte(0); w.WriteByte(2); w.WriteInt(99); }));
        Assert.That((l.List, l.Replaces), Is.EqualTo((-1, false)));
    }

    static void Gift(ByteWriter w, int id, int count)
    {
        w.WriteInt(id); w.WriteShort(1); w.WriteInt(count);
        w.WriteBool(false); w.WriteBool(false); w.WriteBool(false); w.WriteBool(false);
    }

    [Test]
    public void AnAttendGiftEntryKeepsEveryField()
    {
        var m = GameEventMessage.Read(Reader(w =>
        {
            w.WriteByte(0); w.WriteInt(1);
            w.WriteUnicodeString("AttendGift");
            w.WriteInt(170042); w.WriteLong(1644447600); w.WriteLong(1647471600);
            w.WriteUnicodeString("Attend"); w.WriteString("desc");
            w.WriteBool(false); w.WriteBool(true);       // m_b124, m_b125
            w.WriteInt(1800);                            // m_i120
            w.WriteByte(2); w.WriteInt(55);              // m_i114, m_i118
            w.WriteByte(1); w.WriteInt(3); w.WriteLong(500); w.WriteInt(20000001);   // m_i100 and the three it gates
            w.WriteInt(2); Gift(w, 20302686, 1); Gift(w, 20302775, 30);
        }));
        var e = m.AttendGiftEvents.Single();
        Assert.That((e.Id, e.Begin, e.End, e.Name, e.Description), Is.EqualTo((170042, 1644447600L, 1647471600L, "Attend", "desc")));
        Assert.That((e.Disabled, e.UsePoints, e.PopupLimit, e.Kind, e.ValueId), Is.EqualTo((false, true, 1800, (byte)2, 55)));
        Assert.That((e.PayType, e.MaxPayAttends, e.PayCost, e.CouponItemId), Is.EqualTo(((byte)1, 3, 500L, 20000001)));
        Assert.That(e.Gifts.Select(g => (g.ItemId, g.Count)), Is.EqualTo(new[] { (20302686, 1), (20302775, 30) }));
    }

    [Test]
    public void AnAttendGiftEntryWithNoPayTypeLeavesThePayFieldsOut()
    {
        var m = GameEventMessage.Read(Reader(w =>
        {
            w.WriteByte(1); w.WriteInt(1);
            w.WriteUnicodeString("AttendGift");
            w.WriteInt(7); w.WriteLong(1); w.WriteLong(2); w.WriteUnicodeString("n"); w.WriteString("");
            w.WriteBool(true); w.WriteBool(false); w.WriteInt(0); w.WriteByte(0); w.WriteInt(0);
            w.WriteByte(0);
            w.WriteInt(1); Gift(w, 5, 9);
        }));
        var e = m.AttendGiftEvents.Single();
        Assert.That((e.Disabled, e.UsePoints, e.PayType, e.MaxPayAttends, e.PayCost, e.CouponItemId), Is.EqualTo((true, false, (byte)0, 0, 0L, 0)));
        Assert.That(e.Gifts.Single().Count, Is.EqualTo(9));
    }

    [TestCase("gms2")]
    [TestCase("kms2")]
    public void EveryRecordedGameEventKeepsItsAttendGifts(string set)
    {
        ushort op = (ushort)(set == "gms2" ? ProtocolProfile.Gms2.Number(Inbound.GameEvent) : (ushort)Inbound.GameEvent);
        var all = Gms2Sniffs.Inbound(set, op);
        int events = 0;
        foreach (var (file, data) in all)
        {
            var m = GameEventMessage.Read(new ByteReader(data));
            foreach (var e in m.AttendGiftEvents)
            {
                events++;
                Assert.That(e.Id, Is.GreaterThan(0), file);
                Assert.That(e.End, Is.GreaterThan(e.Begin), file);
                Assert.That(e.Name, Is.Not.Empty, file);
                Assert.That(e.Kind, Is.InRange((byte)0, (byte)3), file);
                Assert.That(e.Gifts, Is.Not.Empty, file);
                foreach (var g in e.Gifts) { Assert.That(g.ItemId, Is.GreaterThan(0), file); Assert.That(g.Count, Is.GreaterThan(0), file); }
            }
        }
        TestContext.WriteLine($"{set}: {all.Count} GameEvent packets, {events} AttendGift events");
    }
}
