using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>OnBindItem (scenestategame.cpp:8542) and OnItemUse (:8603), read as the client reads them.</summary>
public class BindItemUseTests {
    static ByteReader Reader(ByteWriter w) => new(w.Buffer.AsSpan(0, w.Length).ToArray());

    static ByteWriter Bind(long uid, int amount) {
        var w = new ByteWriter(); w.WriteLong(uid);
        ItemReader.WriteEmptyBody(w, amount);
        w.WriteInt(-999);
        return w;
    }

    [Test] public void ABagItemIsReadAgain() {
        var bag = new EquipItem { Id = 11, Uid = 5, Amount = 1 };
        var r = Reader(Bind(5, 9));
        var b = BindItem.Read(r, uid => uid == 5 ? bag : null, uid => null);
        Assert.That((b.Found, b.Worn), Is.EqualTo((true, false)));
        Assert.That(bag.Amount, Is.EqualTo(9));
        Assert.That(r.ReadInt(), Is.EqualTo(-999), "the record is consumed");
    }

    [Test] public void AWornItemIsTheFallbackWhenTheBagHasNone() {
        var worn = new EquipItem { Id = 12, Uid = 6, Amount = 1 };
        var b = BindItem.Read(Reader(Bind(6, 4)), uid => null, uid => uid == 6 ? worn : null);
        Assert.That((b.Found, b.Worn), Is.EqualTo((true, true)));
        Assert.That(worn.Amount, Is.EqualTo(4));
    }

    [Test] public void AnUnknownUidReadsNothing() {
        var other = new EquipItem { Id = 11, Uid = 5, Amount = 1 };
        var r = Reader(Bind(7, 9));
        var b = BindItem.Read(r, uid => null, uid => null);
        Assert.That(b.Found, Is.False);
        Assert.That(other.Amount, Is.EqualTo(1));
        Assert.That(r.ReadInt(), Is.Not.EqualTo(-999), "the record is left unread");
    }

    [TestCase(0, 0x2a2)] [TestCase(1, 0x2a3)] [TestCase(2, 0x2a4)] [TestCase(3, 0x2a5)]
    public void ModesZeroToThreeAreNoticeBoxes(int mode, int id) {
        var w = new ByteWriter(); w.WriteByte((byte) mode);
        var n = ItemUseNotice.Read(Reader(w));
        Assert.That((n.Mode, n.NoticeStringId, n.ItemId, n.MarksDialogDirty), Is.EqualTo((mode, id, 0, false)));
    }

    [Test] public void ModeFourReadsTheItemId() {
        var w = new ByteWriter(); w.WriteByte(4); w.WriteInt(20000001);
        var n = ItemUseNotice.Read(Reader(w));
        Assert.That((n.Mode, n.NoticeStringId, n.ItemId, n.MarksDialogDirty), Is.EqualTo((4, 0, 20000001, true)));
    }

    [Test] public void ModeSevenOnlyMarksTheDialogDirty() {
        var w = new ByteWriter(); w.WriteByte(7);
        var n = ItemUseNotice.Read(Reader(w));
        Assert.That((n.NoticeStringId, n.ItemId, n.MarksDialogDirty), Is.EqualTo((0, 0, true)));
    }
}
