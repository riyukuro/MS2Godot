using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class GameEventPacketTests
{
    static ByteReader Reader(Action<ByteWriter> write)
    {
        var w = new ByteWriter();
        write(w);
        return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
    }

    static void Shutdown(ByteWriter w, int id, params (int Field, int Obj)[] pairs)
    {
        // GameEventManagerCli::Packet0 (gameeventmanagercli.cpp:129): ReadUnicodeString name, then the type's vfunc_4;
        // GameEvent::vfunc_4 i32 id, GameEventShutdownInteractObjectId::vfunc_4 (gameevent.cpp:4306-4317) i32 count,
        // each u32 field id, i32 object id
        w.WriteUnicodeString(GameEventMessage.ShutdownInteractObjectName);
        w.WriteInt(id);
        w.WriteInt(pairs.Length);
        foreach (var (f, o) in pairs) { w.WriteInt(f); w.WriteInt(o); }
    }

    [Test]
    public void OpcodeMatchesClientTable()
    {
        // commonfiles/opcodes.h:185 RecvOp::GameEvent = 0x00B2
        Assert.That((ushort)Inbound.GameEvent, Is.EqualTo(0x00B2));
    }

    [Test]
    public void ListReadsShutdownEvents()
    {
        var m = GameEventMessage.Read(Reader(w =>
        {
            w.WriteByte(0); w.WriteInt(2);
            Shutdown(w, 7, (2000001, 10), (2000001, 11));
            Shutdown(w, 8, (2000002, 5));
        }));
        Assert.That(m.Kind, Is.EqualTo(0));
        Assert.That(m.Truncated, Is.False);
        Assert.That(m.Shutdowns.Count, Is.EqualTo(2));
        Assert.That(m.Shutdowns[0].Id, Is.EqualTo(7));
        Assert.That(m.Shutdowns[0].Objects, Is.EqualTo(new[] { (2000001u, 10), (2000001u, 11) }));
        Assert.That(m.Shutdowns[1].Objects, Is.EqualTo(new[] { (2000002u, 5) }));
    }

    [Test]
    public void RemovedReadsIds()
    {
        // Packet2 (:171-185): i32 count, each i32 id
        var m = GameEventMessage.Read(Reader(w => { w.WriteByte(2); w.WriteInt(2); w.WriteInt(7); w.WriteInt(9); }));
        Assert.That(m.RemovedIds, Is.EqualTo(new[] { 7, 9 }));
        Assert.That(m.Shutdowns, Is.Empty);
    }

    static void Item(ByteWriter w, int id)
    {
        // ItemEntity::Read (commonstruct_wa.cpp:94-102): i32 id, i16 rank, i32 count, 4 bools
        w.WriteInt(id); w.WriteShort(1); w.WriteInt(1);
        w.WriteBool(false); w.WriteBool(false); w.WriteBool(false); w.WriteBool(false);
    }

    [Test]
    public void OtherTypesBeforeTheShutdownAreRead()
    {
        // the client reads every entry with its type's vfunc_4 (Packet1, gameeventmanagercli.cpp:131-170)
        var m = GameEventMessage.Read(Reader(w =>
        {
            w.WriteByte(1); w.WriteInt(6);
            Shutdown(w, 4, (2000003, 1));
            // GameEventStringBoard (gameeventstringboard.cpp:70): id, i32, unicode
            w.WriteUnicodeString("StringBoard"); w.WriteInt(3); w.WriteInt(1); w.WriteUnicodeString("x");
            // GameEventSaleChat (gameevent.cpp:618): id, i32, i32
            w.WriteUnicodeString("SaleChat"); w.WriteInt(9); w.WriteInt(2); w.WriteInt(3);
            // GameEventStampEvent (gameevent.cpp:2733), StampEventContext::Read (gameeventcontext.cpp:658-683)
            w.WriteUnicodeString("StampEvent"); w.WriteInt(10);
            w.WriteUnicodeString("t"); w.WriteLong(5); w.WriteInt(2); w.WriteInt(0); w.WriteBool(true);
            for (int i = 0; i < 2; i++) { w.WriteInt(i); w.WriteByte(0); w.WriteInt(0); w.WriteInt(0); }
            w.WriteInt(1); w.WriteInt(1); w.WriteByte(0); Item(w, 20000001); Item(w, 20000002);
            Item(w, 20000003);
            // GameEventTreeWatering (gameevent.cpp:5651), TreeWateringContext::Read: ASCII strings (ReadString)
            w.WriteUnicodeString("TreeWatering"); w.WriteInt(11); w.WriteInt(11);
            w.WriteInt(1); w.WriteInt(1); w.WriteLong(0); w.WriteLong(0);
            w.WriteInt(0);
            w.WriteInt(0); w.WriteInt(0); w.WriteByte(0); w.WriteInt(0); w.WriteInt(0); w.WriteInt(0); w.WriteByte(0); w.WriteInt(0);
            w.WriteInt(1); w.WriteInt(1); w.WriteString("res");
            w.WriteInt(0); w.WriteString("a"); w.WriteInt(0); w.WriteString("b"); w.WriteString("c"); w.WriteString("d");
            Shutdown(w, 5, (2000003, 2));
        }));
        Assert.That(m.Truncated, Is.False);
        Assert.That(m.Shutdowns.Select(e => e.Id), Is.EqualTo(new[] { 4, 5 }));
        Assert.That(m.Shutdowns[1].Objects, Is.EqualTo(new[] { (2000003u, 2) }));
    }

    [Test]
    public void ContextTypeBeforeTheShutdownIsRead()
    {
        // GameEventBurningLevelup (gameevent.cpp:6042), BurningLevelupContext::Read: the jumping item block only when its
        // bool is set
        var m = GameEventMessage.Read(Reader(w =>
        {
            w.WriteByte(0); w.WriteInt(2);
            w.WriteUnicodeString("BurningLevelup"); w.WriteInt(12);
            w.WriteInt(12); w.WriteLong(0); w.WriteInt(1); w.WriteInt(50); w.WriteInt(0); w.WriteInt(0);
            w.WriteUnicodeString("a"); w.WriteUnicodeString("b"); w.WriteUnicodeString("c"); w.WriteUnicodeString("d");
            w.WriteShort(0); w.WriteShort(1); w.WriteInt(7); w.WriteInt(1); Item(w, 20000004);
            w.WriteBool(true); Item(w, 20000005); w.WriteInt(1); w.WriteInt(2000001);
            w.WriteUnicodeString("e"); w.WriteUnicodeString("f"); w.WriteUnicodeString("g");
            Shutdown(w, 6, (2000004, 3));
        }));
        Assert.That(m.Truncated, Is.False);
        Assert.That(m.Shutdowns.Select(e => e.Id), Is.EqualTo(new[] { 6 }));
    }

    [Test]
    public void ConstructShowItemReadsItsShortBeforeTheCount()
    {
        // GameEventConstructShowItem::vfunc_4 (gameevent.cpp:3754-3766): id, i32, unicode, i16, i16 count, count x i32
        var m = GameEventMessage.Read(Reader(w =>
        {
            w.WriteByte(0); w.WriteInt(2);
            w.WriteUnicodeString("ConstructShowItem"); w.WriteInt(13);
            w.WriteInt(1); w.WriteUnicodeString(""); w.WriteShort(1); w.WriteShort(3);
            w.WriteInt(50000001); w.WriteInt(50000002); w.WriteInt(50000003);
            Shutdown(w, 7, (2000005, 4));
        }));
        Assert.That(m.Truncated, Is.False);
        Assert.That(m.Shutdowns.Select(e => e.Id), Is.EqualTo(new[] { 7 }));
        Assert.That(m.Shutdowns[0].Objects, Is.EqualTo(new[] { (2000005u, 4) }));
    }

    [Test]
    public void SnowmanReadsItsStringAfterTheContext()
    {
        // GameEventSnowman::vfunc_4 (gameevent.cpp:5068-5075): id, SnowmanContext::Read (gameeventcontext.cpp:1160-1185:
        // i32, unicode, u16 count of i32 x3 + two u16-counted item lists, i64), then a unicode string
        var m = GameEventMessage.Read(Reader(w =>
        {
            w.WriteByte(0); w.WriteInt(2);
            w.WriteUnicodeString("Snowman"); w.WriteInt(14);
            w.WriteInt(14); w.WriteUnicodeString("t"); w.WriteShort(1);
            w.WriteInt(1); w.WriteInt(2); w.WriteInt(3); w.WriteShort(1); Item(w, 20000006); w.WriteShort(0);
            w.WriteLong(0); w.WriteUnicodeString("tail");
            Shutdown(w, 8, (2000006, 5));
        }));
        Assert.That(m.Truncated, Is.False);
        Assert.That(m.Shutdowns.Select(e => e.Id), Is.EqualTo(new[] { 8 }));
        Assert.That(m.Shutdowns[0].Objects, Is.EqualTo(new[] { (2000006u, 5) }));
    }

    [Test]
    public void ReadingStopsAtANameNoClassIsRegisteredUnder()
    {
        // Create fails: the client asserts and returns, the rest unread (gameeventmanagercli.cpp:144-147)
        var m = GameEventMessage.Read(Reader(w =>
        {
            w.WriteByte(1); w.WriteInt(3);
            Shutdown(w, 4, (2000003, 1));
            w.WriteUnicodeString("NoSuchEvent"); w.WriteInt(3);
            Shutdown(w, 5, (2000003, 2));
        }));
        Assert.That(m.Truncated, Is.True);
        Assert.That(m.Shutdowns.Select(e => e.Id), Is.EqualTo(new[] { 4 }));
    }

    [Test]
    public void SpecialAbilityHotTimesKeepTheirIdsAndValues()
    {
        // GameEventSAHotTime::vfunc_4 (gameevent.cpp:701-710): id, then 4 unicode strings, the ids and values first;
        // GameEventPCBangSAHotTime the same with m_e8 = 1; DTReward (gameeventdtreward.cpp:82) is read past
        var m = GameEventMessage.Read(Reader(w =>
        {
            w.WriteByte(1); w.WriteInt(4);
            w.WriteUnicodeString("SAHotTime"); w.WriteInt(7);
            w.WriteUnicodeString("35,4"); w.WriteUnicodeString("0.1,3"); w.WriteUnicodeString("a"); w.WriteUnicodeString("b");
            w.WriteUnicodeString("DTReward"); w.WriteInt(8);
            for (int i = 0; i < 4; i++) w.WriteUnicodeString("x");
            w.WriteUnicodeString("PCBangSAHotTime"); w.WriteInt(9);
            w.WriteUnicodeString("35"); w.WriteUnicodeString("0.2"); w.WriteUnicodeString(""); w.WriteUnicodeString("");
            Shutdown(w, 4, (2000003, 1));
        }));
        Assert.That(m.Truncated, Is.False);
        Assert.That(m.HotTimes, Is.EqualTo(new[]
        {
            new SpecialAbilityHotTimeEvent(7, false, "35,4", "0.1,3"),
            new SpecialAbilityHotTimeEvent(9, true, "35", "0.2"),
        }));
        Assert.That(m.Shutdowns.Select(e => e.Id), Is.EqualTo(new[] { 4 }));
    }

    [Test]
    public void PremiumMarketSaleEventsKeepRateIdsAndCurrency()
    {
        // GameEventPremiumMarketSale (gameevent.cpp:1415-1439): id, rate (1/10000), n x covered id, currency item id
        var m = GameEventMessage.Read(Reader(w =>
        {
            w.WriteByte(1); w.WriteInt(1);
            w.WriteUnicodeString("PremiumMarketSale"); w.WriteInt(21); w.WriteInt(1500);
            w.WriteInt(2); w.WriteInt(0); w.WriteInt(10); w.WriteInt(100000);
        }));
        Assert.That(m.Truncated, Is.False);
        Assert.That(m.SaleEvents.Count, Is.EqualTo(1));
        var e = m.SaleEvents[0];
        Assert.That((e.Id, e.Rate, e.Currency), Is.EqualTo((21, 1500, 100000)));
        Assert.That(e.Ids, Is.EqualTo(new[] { 0, 10 }));
    }
}
