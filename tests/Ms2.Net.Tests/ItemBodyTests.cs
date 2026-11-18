using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class ItemBodyTests {
    static void Body(ByteWriter w, bool full) {
        w.WriteInt(3); w.WriteInt(-1); w.WriteLong(100); w.WriteLong(200); w.WriteLong(300); w.WriteInt(4); w.WriteInt(5);
        w.WriteByte(1); w.WriteLong(400); w.WriteShort(6); w.WriteBool(true); w.WriteInt(7);
        w.WriteInt(0); w.WriteInt(0); w.WriteInt(0); w.WriteInt(0); w.WriteInt(0);     // EquipColor: 3 RGBA, 2 ints
        for (int i = 0; i < 5; i++) {
            if (full && i == 0) { w.WriteShort(1); w.WriteShort(0x10); w.WriteInt(25); } else w.WriteShort(0);
            if (full && i == 2) { w.WriteShort(1); w.WriteShort(0x23); w.WriteInt(30); } else w.WriteShort(0);
        }
        w.WriteInt(8);
        w.WriteInt(full ? 5 : 0); w.WriteInt(9); w.WriteByte(2);
        w.WriteLong(11); w.WriteInt(12); w.WriteInt(13); w.WriteBool(true); w.WriteInt(14);
        if (full) { w.WriteByte(1); w.WriteInt(0x10); w.WriteInt(50); } else w.WriteByte(0);
        w.WriteInt(2);
        if (full) { w.WriteInt(1); w.WriteShort(4); w.WriteInt(60); } else w.WriteInt(0);
        if (full) { w.WriteInt(1); w.WriteShort(0x12); w.WriteInt(70); } else w.WriteInt(0);
        // transfer, binding only in the full one
        w.WriteInt(3); w.WriteBool(true); w.WriteInt(1); w.WriteInt(2); w.WriteByte(1); w.WriteByte(0);
        if (full) { w.WriteBool(true); w.WriteLong(77); w.WriteUnicodeString("Solo"); } else w.WriteBool(false);
        // socket: max 3, slot 0 empty, slot 1 a gem with bind and lock
        w.WriteByte(3);
        if (full) {
            w.WriteByte(2); w.WriteBool(false);
            w.WriteBool(true); w.WriteInt(40000001); w.WriteBool(true); w.WriteLong(78); w.WriteUnicodeString("Gem"); w.WriteBool(true); w.WriteByte(1); w.WriteLong(500);
        } else w.WriteByte(0);
        if (full) { w.WriteLong(9); w.WriteUnicodeString("Partner"); w.WriteBool(true); } else w.WriteLong(0);
        w.WriteLong(full ? 79 : 0); w.WriteUnicodeString(full ? "Owner" : "");
        w.WriteInt(-12345);                                                             // a marker after the body
    }

    static ByteReader R(bool full, out ByteWriter w) {
        w = new ByteWriter(); w.WriteInt(11400021); w.WriteLong(55); w.WriteByte((byte) EquipSlot.CL); w.WriteInt(1);
        Body(w, full);
        return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
    }

    [Test] public void TheBodyKeepsWhatTheClientsReadersFill() {
        var r = R(true, out _);
        var it = ItemReader.ReadEquip(r);
        Assert.That(r.ReadInt(), Is.EqualTo(-12345), "the whole body is read");
        Assert.That((it.Amount, it.HeaderInt60, it.CreationTime, it.ExpiryTime, it.RetrievableUntil, it.RemakeCount, it.RemainUses), Is.EqualTo((3, -1, 100L, 200L, 300L, 4, 5)));
        Assert.That((it.LockState, it.UnlockTime, it.GlamorForges, it.HeaderFlagD4, it.GachaDismantleId), Is.EqualTo(((byte) 1, 400L, (ushort) 6, true, 7)));
        Assert.That(it.Options.Stat[0][0x10], Is.EqualTo(25)); Assert.That(it.Options.Special[2][0x23], Is.EqualTo(30)); Assert.That(it.Options.MergeId, Is.EqualTo(8));
        var e = it.Enchant;
        Assert.That((e.Level, e.Charge, e.Version, e.HistoryItemId, e.Attempts, e.Failures, e.HistoryEnabled, e.FailStack), Is.EqualTo((5, 9, (byte) 2, 11L, 12, 13, true, 14)));
        Assert.That(e.Options[0x10], Is.EqualTo(50));
        Assert.That((it.LimitBreak.Level, it.LimitBreak.Stat[4], it.LimitBreak.Special[0x12]), Is.EqualTo((2, 60, 70)));
        var t = it.Transfer;
        Assert.That((t.Rule, t.TransferFlag38, t.Count, t.Max, t.BreakDisabled, t.SocketTransferEnabled, t.HasBinding, t.BindingId, t.BindingName), Is.EqualTo((3, true, 1, 2, true, false, true, 77L, "Solo")));
        Assert.That(it.Socket.MaxSlot, Is.EqualTo(3)); Assert.That(it.Socket.Gems[0], Is.Null);
        var g = it.Socket.Gems[1];
        Assert.That((g.GemId, g.HasBind, g.BindCharacterId, g.BindName, g.HasLock, g.LockState, g.LockExpireTime), Is.EqualTo((40000001, true, 78L, "Gem", true, (byte) 1, 500L)));
        Assert.That((it.CoupleId, it.CoupleName, it.CoupleFlag, it.BindingId, it.BindingName), Is.EqualTo((9L, "Partner", true, 79L, "Owner")));
        Assert.That(it.HasUgc, Is.False);
    }

    [Test] public void AReReadStartsTheBlocksOverLikeTheClient() {
        var it = ItemReader.ReadEquip(R(true, out _));
        var w = new ByteWriter(); Body(w, false);
        var r = new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
        ItemReader.ReadBodyInto(r, it);
        Assert.That(r.ReadInt(), Is.EqualTo(-12345));
        Assert.That(it.Options.Stat[0], Is.Empty); Assert.That(it.Enchant.Options, Is.Empty); Assert.That(it.LimitBreak.Stat, Is.Empty);
        Assert.That(it.Socket.Gems, Is.Empty);
        // vfunc_2 assigns the binding only when present; the couple name is only read with a partner
        Assert.That((it.Transfer.HasBinding, it.Transfer.BindingName), Is.EqualTo((true, "Solo")));
        Assert.That((it.CoupleId, it.CoupleName), Is.EqualTo((0L, "Partner")));
        Assert.That((it.BindingId, it.BindingName), Is.EqualTo((0L, "")));
    }

    /// <summary>Inbound 0x7F (OnBadgeEquip, scenestategame.cpp:11006): mode 0 the gem put on with its slot and gem block,
    /// mode 1 the object and gem type taken out, mode 4 the error.</summary>
    [Test] public void TheBadgeEquipModesReadWhatTheClientReads() {
        var w = new ByteWriter(); w.WriteByte(0); w.WriteInt(1); w.WriteInt(70200001); w.WriteLong(961); w.WriteInt(2); w.WriteByte(5);
        var full = new ByteWriter(); Body(full, false);
        var body = full.Buffer.AsSpan(0, full.Length).ToArray();
        const int status = 143;                                                         // the header, options, enchant and limit break
        w.WriteBytes(body[..status]);
        w.WriteBool(true); w.WriteByte(5); w.WriteInt(70200002);                         // CItemGem after the status: has, type 5, gem id
        w.WriteBytes(body[status..]);                                                   // transfer .. binding, then the marker
        var r = new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
        var on = BadgeEquipMessage.Read(r);
        Assert.That(r.ReadInt(), Is.EqualTo(-12345), "the whole gem is read");
        Assert.That((on.Mode, on.ObjectId, on.Item.Id, on.Item.Uid, on.Item.Rarity, on.Item.BadgeSlot, on.Item.GemType, on.Item.IsBadge),
            Is.EqualTo(((byte) 0, 1, 70200001, 961L, 2, (byte) 5, (byte) 5, true)));

        w = new ByteWriter(); w.WriteByte(1); w.WriteInt(1); w.WriteByte(3);
        var off = BadgeEquipMessage.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()));
        Assert.That((off.Mode, off.ObjectId, off.GemType, off.Item), Is.EqualTo(((byte) 1, 1, (byte) 3, (EquipItem) null)));

        w = new ByteWriter(); w.WriteByte(4); w.WriteShort(5);
        var err = BadgeEquipMessage.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()));
        Assert.That((err.Mode, err.Error), Is.EqualTo(((byte) 4, (short) 5)));
    }

    [Test] public void TheUgcFlagIsTheReadBlock() {
        var w = new ByteWriter(); w.WriteInt(11400021); w.WriteLong(55); w.WriteByte((byte) EquipSlot.CL); w.WriteInt(1);
        var full = new ByteWriter(); Body(full, false);
        var body = full.Buffer.AsSpan(0, full.Length).ToArray();
        int cut = Measure(false);                                                   // the bytes before the transfer block
        w.WriteBytes(body[..cut]);
        // UgcItemLook, then the blueprint
        w.WriteLong(5); w.WriteUnicodeString("5"); w.WriteUnicodeString("Look"); w.WriteByte(0); w.WriteInt(0); w.WriteLong(0); w.WriteLong(0);
        w.WriteUnicodeString("Maker"); w.WriteLong(1700000000); w.WriteUnicodeString(""); w.WriteByte(2);
        w.WriteLong(0); w.WriteInt(0); w.WriteInt(0); w.WriteInt(0); w.WriteLong(0); w.WriteInt(0); w.WriteLong(0); w.WriteLong(0); w.WriteUnicodeString("");
        w.WriteBytes(body[cut..]);
        var r = new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
        var it = ItemReader.ReadEquip(r);
        Assert.That(it.HasUgc, Is.True);
        Assert.That((it.Ugc.Uid, it.Ugc.Author, it.Ugc.CreationTime), Is.EqualTo((5L, "Maker", 1700000000L)));
        Assert.That(it.Ugc.State, Is.EqualTo((byte) 2), "the look's last byte");
        Assert.That(r.ReadInt(), Is.EqualTo(-12345));
    }

    // the length of the body up to the transfer block: the header, colour, status, enchant and limit break
    static int Measure(bool full) {
        var w = new ByteWriter(); Body(w, full);
        var b = w.Buffer.AsSpan(0, w.Length).ToArray();
        // transfer + socket + couple + binding + marker of the plain body: 4+1+4+4+1+1+1, 1+1, 8, 8+2, 4
        return b.Length - (16 + 2 + 8 + 10 + 4);
    }
}
