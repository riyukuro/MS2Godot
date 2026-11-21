using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>OnItemDropNotice (scenestategame.cpp:8023) and OnItemUpdate (:3214), read as the client reads them.</summary>
public class ItemNoticePacketTests {
    static ByteReader Reader(ByteWriter w) => new(w.Buffer.AsSpan(0, w.Length).ToArray());

    [Test] public void ADropNoticeIsANameAndAnItemLink() {
        var w = new ByteWriter(); w.WriteUnicodeString("Nameé"); w.WriteInt(20000001); w.WriteInt(4); w.WriteLong(0x1122334455L); w.WriteInt(-999);
        var r = Reader(w);
        var n = ItemDropNotice.Read(r);
        Assert.That(n, Is.EqualTo(new ItemDropNotice("Nameé", 20000001, 4, 0x1122334455L)));
        Assert.That(r.ReadInt(), Is.EqualTo(-999), "the notice is read whole");
    }

    static ByteWriter Update(int obj, long uid, int amount) {
        var w = new ByteWriter(); w.WriteInt(obj); w.WriteLong(uid);
        ItemReader.WriteEmptyBody(w, amount);
        w.WriteInt(-999);
        return w;
    }

    [Test] public void AnotherPlayersEquipIsReadAgain() {
        var eq = new EquipItem { Id = 11, Uid = 5, Amount = 1 };
        var r = Reader(Update(8, 5, 9));
        var u = ItemUpdate.Read(r, (o, uid) => (o == 8, false, uid == 5 ? eq : null));
        Assert.That((u.PcKnown, u.EquipKnown, u.Own, u.Updated), Is.EqualTo((true, true, false, true)));
        Assert.That(eq.Amount, Is.EqualTo(9));
        Assert.That(r.ReadInt(), Is.EqualTo(-999), "the body is consumed");
    }

    [Test] public void TheLocalPlayersEquipReadsNoBody() {
        var eq = new EquipItem { Id = 11, Uid = 5, Amount = 1 };
        var r = Reader(Update(8, 5, 9));
        var u = ItemUpdate.Read(r, (o, uid) => (true, true, eq));
        Assert.That((u.Own, u.Updated), Is.EqualTo((true, false)));
        Assert.That(eq.Amount, Is.EqualTo(1));
        Assert.That(r.ReadInt(), Is.Not.EqualTo(-999), "the body is left unread, so the sentinel is not next");
    }

    [Test] public void AnUnknownPlayerOrEquipChangesNothing() {
        var eq = new EquipItem { Id = 11, Uid = 5, Amount = 1 };
        var u = ItemUpdate.Read(Reader(Update(8, 5, 9)), (o, uid) => (false, false, eq));
        Assert.That((u.PcKnown, u.EquipKnown, u.Updated), Is.EqualTo((false, false, false)));
        var v = ItemUpdate.Read(Reader(Update(8, 6, 9)), (o, uid) => (true, false, null));
        Assert.That((v.PcKnown, v.EquipKnown, v.Updated), Is.EqualTo((true, false, false)));
        Assert.That(eq.Amount, Is.EqualTo(1));
    }
}
