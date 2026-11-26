using System.Collections.Generic;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

// Inbound EnchantScroll (KMS 0xBF, GMS2 0xC1) and outbound EnchantScroll (KMS 0x71, GMS2 0x72): UIEnchantScrollDialog
// (client ms2/uienchantscrolldialog.cpp) and EnchantScrollSlot (enchantscrollslot.cpp).
public class EnchantScrollPacketTests {
    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
    static byte[] Of(System.Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    static readonly int KmsBodyLen = Measure();
    static int Measure() { var r = new ByteReader(new byte[600]); ItemReader.ReadBodyInto(r, new EquipItem { Id = 1 }); return r.Position; }

    sealed class Ctx {
        public readonly Dictionary<long, EquipItem> Inv = new();
        public readonly EnchantScrollReadContext C = new();
        public Ctx(long scroll = 0) { C.ScrollUid = scroll; C.Find = u => Inv.TryGetValue(u, out var i) ? i : null; }
        public EquipItem Add(long uid, int id = 1, int level = 0) {
            var i = new EquipItem { Id = id, Uid = uid }; i.Enchant.Level = level; Inv[uid] = i; return i;
        }
        public (EnchantScrollMessage M, ByteReader R) Read(byte[] b, ProtocolProfile p = null) {
            var r = new ByteReader(b);
            return (EnchantScrollMessage.Read(r, C, p ?? ProtocolProfile.Kms2525), r);
        }
    }

    [Test] public void TheOpcodesAreTheClientsOnBothProfiles() {
        Assert.That((ushort) Inbound.EnchantScroll, Is.EqualTo(0xBF));
        Assert.That((ushort) Outbound.EnchantScroll, Is.EqualTo(0x71));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.EnchantScroll), Is.EqualTo(0xC1));
        Assert.That(ProtocolProfile.Gms2.Number(Outbound.EnchantScroll), Is.EqualTo(0x72));
    }

    /// <summary>EnchantScrollSlot::vfunc_18: u8 1, i64 the slot's owner (the scroll), i64 the item; onEnchant: u8 2, i64 the
    /// scroll, i64 the item.</summary>
    [Test] public void TheSendsWriteTheClientsBytes() {
        Assert.That(Bytes(new EnchantScrollPutRequest(0x1122334455667788, 0x0102030405060708)),
            Is.EqualTo(Of(w => { w.WriteByte(1); w.WriteLong(0x1122334455667788); w.WriteLong(0x0102030405060708); })));
        Assert.That(Bytes(new EnchantScrollEnchantRequest(5, 6)), Is.EqualTo(Of(w => { w.WriteByte(2); w.WriteLong(5); w.WriteLong(6); })));
        Assert.That(new EnchantScrollPutRequest(1, 2).Op, Is.EqualTo(Outbound.EnchantScroll));
        Assert.That(new EnchantScrollEnchantRequest(1, 2).Op, Is.EqualTo(Outbound.EnchantScroll));
    }

    static byte[] OpenBytes(int type, int rmin = 0, int rmax = 0) => Of(w => {
        w.WriteByte(0); w.WriteLong(77); w.WriteShort((short) type); w.WriteByte(1); w.WriteInt(10); w.WriteInt(3000);
        w.WriteShort(50); w.WriteShort(60);
        w.WriteInt(2); w.WriteInt(1); w.WriteInt(8);
        w.WriteInt(3); w.WriteInt(1); w.WriteInt(2); w.WriteInt(3);
        if (type == 3) { w.WriteInt(rmin); w.WriteInt(rmax); }
    });

    /// <summary>FUN_140f3b920: the scroll's fields in order; the fail rate / 100; the random range only for type 3.</summary>
    [Test] public void ModeZeroReadsTheScrollsLimitsInTheClientsOrder() {
        foreach (int type in new[] { 1, 2, 3, 5 }) {
            var (m, r) = new Ctx().Read(OpenBytes(type, 3, 7));
            var o = (EnchantScrollMessage.Open) m;
            Assert.That(r.Available, Is.EqualTo(0), $"type {type}");
            Assert.That((o.ScrollUid, o.Type, o.DisableBreak, o.TargetLevel, o.FailRate, o.LevelMin, o.LevelMax),
                Is.EqualTo((77L, type, true, 10, 30u, (ushort) 50, (ushort) 60)));
            Assert.That(o.EquipTypes, Is.EqualTo(new[] { 1, 8 }));
            Assert.That(o.Ranks, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That((o.RandomMin, o.RandomMax), Is.EqualTo(type == 3 ? (3, 7) : (0, 0)));
        }
        // type 3 without its range is short; a type 1 with one is long
        Assert.That(() => new Ctx().Read(OpenBytes(3)[..^8]), Throws.Exception);
        Assert.That(() => new Ctx().Read(Of(w => { w.WriteBytes(OpenBytes(1)); w.WriteInt(1); })), Throws.Exception);
    }

    static byte[] PreviewBytes(long uid, int type, params (int Key, int Value)[][] lists) => Of(w => {
        w.WriteByte(1); w.WriteLong(uid); w.WriteShort((short) type);
        foreach (var l in lists) { w.WriteInt(l.Length); foreach (var (k, v) in l) { w.WriteShort((short) k); w.WriteInt(v); } }
    });

    /// <summary>FUN_140f3bd10: the client returns with the rows unread when the scroll or the item is not in the inventory
    /// (:233) and for types 1 / 2 when the item is at the target level (:245); a type outside the switch reads nothing more.</summary>
    [Test] public void ModeOneReadsTheRowsOnlyWhereTheClientDoes() {
        var c = new Ctx(77); c.Add(77, 2000001); c.Add(5, 1, level: 4); c.C.TargetLevel = 5;
        var rows = new[] { (0x1b, 40), (0x1c, 0) };
        // the item missing, then the scroll missing: nothing past the type is read
        var (m, r) = c.Read(PreviewBytes(6, 1, rows));
        var p = (EnchantScrollMessage.Preview) m;
        Assert.That((p.ItemUid, p.Type, p.Shown, p.RowsRead), Is.EqualTo((6L, 1, false, false)));
        Assert.That(r.Available, Is.EqualTo(4 + 2 * 6));
        c.C.ScrollUid = 78;
        (m, r) = c.Read(PreviewBytes(5, 1, rows));
        Assert.That(((EnchantScrollMessage.Preview) m).Shown, Is.False); Assert.That(r.Available, Is.EqualTo(4 + 2 * 6));
        c.C.ScrollUid = 77;
        // types 1 and 2 below the target level: the level rows, in the wire order
        foreach (int type in new[] { 1, 2 }) {
            (m, r) = c.Read(PreviewBytes(5, type, rows));
            p = (EnchantScrollMessage.Preview) m;
            Assert.That((p.Shown, p.RowsRead, r.Available), Is.EqualTo((true, true, 0)));
            Assert.That(p.Rows.Select(x => (x.Key, x.Value)), Is.EqualTo(rows));
            Assert.That(p.Rows[0].Rate, Is.EqualTo(0.04f)); Assert.That(p.Rows[0].HasValue, Is.True); Assert.That(p.Rows[1].HasValue, Is.False);
        }
        // at the target level: shown, the rows unread
        c.Inv[5].Enchant.Level = 5;
        (m, r) = c.Read(PreviewBytes(5, 1, rows));
        p = (EnchantScrollMessage.Preview) m;
        Assert.That((p.Shown, p.RowsRead, r.Available), Is.EqualTo((true, false, 4 + 2 * 6)));
        // type 3: the low rows then the high rows
        (m, r) = c.Read(PreviewBytes(5, 3, rows, new[] { (0x1b, 90) }));
        p = (EnchantScrollMessage.Preview) m;
        Assert.That((p.Shown, p.RowsRead, r.Available, p.Rows.Count, p.High.Count), Is.EqualTo((true, true, 0, 2, 1)));
        Assert.That(p.High[0].Value, Is.EqualTo(90));
        // type 5 reads nothing more and the packet ends there
        (m, r) = c.Read(PreviewBytes(5, 5));
        Assert.That((((EnchantScrollMessage.Preview) m).Shown, r.Available), Is.EqualTo((true, 0)));
        Assert.That(() => c.Read(PreviewBytes(5, 5, rows)), Throws.Exception);
        // type 4 is not in the switch: nothing more is read
        (m, r) = c.Read(PreviewBytes(5, 4, rows));
        Assert.That((((EnchantScrollMessage.Preview) m).RowsRead, r.Available), Is.EqualTo((false, 4 + 2 * 6)));
    }

    /// <summary>FUN_140f3c6c0: code 0 reads the uid and, for an item in the inventory, its body into a copy (the inventory item
    /// is not touched by the read); a missing item leaves the body unread (:277); other codes carry nothing.</summary>
    [Test] public void ModeThreeReadsTheItemBodyIntoACopy() {
        var c = new Ctx(77); var inv = c.Add(5, 1, level: 2); inv.Rarity = 3; inv.Slot = (EquipSlot) 4; inv.InventorySlot = 9;
        var body = new byte[KmsBodyLen];
        var (m, r) = c.Read(Of(w => { w.WriteByte(3); w.WriteShort(0); w.WriteLong(5); w.WriteBytes(body); }));
        var res = (EnchantScrollMessage.Result) m;
        Assert.That((res.Code, res.ItemUid, r.Available), Is.EqualTo((0, 5L, 0)));
        Assert.That(res.Item, Is.Not.SameAs(inv));
        Assert.That((res.Item.Id, res.Item.Uid, res.Item.Rarity, res.Item.Slot, res.Item.InventorySlot), Is.EqualTo((1, 5L, 3, inv.Slot, 9)));
        Assert.That(inv.Enchant.Level, Is.EqualTo(2));
        (m, r) = c.Read(Of(w => { w.WriteByte(3); w.WriteShort(0); w.WriteLong(6); w.WriteBytes(body); }));
        Assert.That(((EnchantScrollMessage.Result) m).Item, Is.Null); Assert.That(r.Available, Is.EqualTo(KmsBodyLen));
        for (int code = 1; code <= 9; code++) {
            (m, r) = c.Read(Of(w => { w.WriteByte(3); w.WriteShort((short) code); }));
            Assert.That((((EnchantScrollMessage.Result) m).Code, ((EnchantScrollMessage.Result) m).Item, r.Available), Is.EqualTo((code, (EquipItem) null, 0)));
        }
    }

    [Test] public void ModeFourAndOtherModes() {
        var c = new Ctx();
        var (m, r) = c.Read(Of(w => { w.WriteByte(4); w.WriteInt(2); w.WriteInt(100); w.WriteShort(3); w.WriteInt(200); w.WriteShort(-1); }));
        Assert.That(((EnchantScrollMessage.Reward) m).Items, Is.EqualTo(new[] { (100, 3), (200, 0xffff) })); Assert.That(r.Available, Is.EqualTo(0));
        foreach (byte mode in new byte[] { 2, 5, 0xff }) {
            (m, _) = c.Read(new[] { mode, (byte) 1 });
            Assert.That(((EnchantScrollMessage.Other) m).Mode, Is.EqualTo(mode));
        }
    }

    /// <summary>GMS2 rows: u16 key, f32 rate, i32 value; KMS rows: u16 key, i32 value.</summary>
    [Test] public void Gms2RowsCarryAFloatBeforeTheValue() {
        var b = Of(w => { w.WriteInt(2); w.WriteShort(0x1b); w.WriteFloat(0.352f); w.WriteInt(0); w.WriteShort(0x1c); w.WriteFloat(0); w.WriteInt(12); });
        var r = new ByteReader(b);
        var rows = EnchantScrollMessage.ReadRows(r, ProtocolProfile.Gms2);
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That((rows[0].Key, rows[0].WireRate, rows[0].Value, rows[0].HasValue), Is.EqualTo((0x1b, 0.352f, 0, true)));
        Assert.That(rows[1].Rate, Is.EqualTo(0.012f)); Assert.That(rows[1].HasValue, Is.True);
        r = new ByteReader(b);
        EnchantScrollMessage.ReadRows(r, ProtocolProfile.Kms2525);
        Assert.That(r.Available, Is.Not.EqualTo(0));
    }

    /// <summary>Every recorded GMS2 EnchantScroll packet (server to client) read to its end with every uid in the inventory
    /// (and no item at the target level), and every recorded client send equal to the writer's bytes.</summary>
    [Test] public void EveryRecordedGms2PacketReadsToTheEndAndEverySendMatches() {
        var list = Gms2Sniffs.Inbound("gms2", ProtocolProfile.Gms2.Number(Inbound.EnchantScroll));
        Assert.That(list, Is.Not.Empty);
        var modes = new SortedDictionary<int, int>();
        Gms2Sniffs.WithGms2ItemData(() => {
            var ctx = new EnchantScrollReadContext { Find = u => new EquipItem { Id = 20000001, Uid = u }, TargetLevel = -1 };
            foreach (var (file, data) in list) {
                var r = new ByteReader(data);
                var m = EnchantScrollMessage.Read(r, ctx, ProtocolProfile.Gms2);
                if (m is EnchantScrollMessage.Open o) { ctx.ScrollUid = o.ScrollUid; Assert.That(o.FailRate, Is.LessThanOrEqualTo(100u), file); }
                if (m is EnchantScrollMessage.Preview p) Assert.That(p.RowsRead || p.Type == 5, Is.True, file);
                Assert.That(m, Is.Not.TypeOf<EnchantScrollMessage.Other>(), file);
                Assert.That(r.Available, Is.EqualTo(0), $"{file} mode {data[0]}");
                modes[data[0]] = modes.GetValueOrDefault(data[0]) + 1;
            }
        });
        TestContext.Out.WriteLine("GMS2 EnchantScroll modes: " + string.Join(", ", modes.Select(kv => $"{kv.Key}x{kv.Value}")));
        Assert.That(modes.Keys, Is.SupersetOf(new[] { 0, 1, 3 }));
        var sends = Gms2Sniffs.Outbound("gms2", ProtocolProfile.Gms2.Number(Outbound.EnchantScroll));
        Assert.That(sends, Is.Not.Empty);
        foreach (var (file, data) in sends) {
            Assert.That(data[0], Is.EqualTo(1).Or.EqualTo(2), file);
            var r = new ByteReader(data); r.ReadByte();
            long a = r.ReadLong(), b = r.ReadLong();
            Assert.That(r.Available, Is.EqualTo(0), file);
            IOutPacket ours = data[0] == 1 ? new EnchantScrollPutRequest(a, b) : new EnchantScrollEnchantRequest(a, b);
            Assert.That(Bytes(ours), Is.EqualTo(data), file);
        }
    }
}
