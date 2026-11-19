using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The other player's character window: SendOp CharacterInfo (UIPopUpMenu::SendCharacterInfo) and its answer, RecvOp
/// 0x78 (UIPCInfoDialog::vfunc_79) with the CPCDataPack (commonfiles/pcdata.cpp).</summary>
public class PcDataTests {
    static byte[] Bytes(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
    static ByteReader R(Action<ByteWriter> f) => new(Bytes(f));

    [Test] public void RequestIsTheCharacterIdAlone() {
        Assert.That((ushort) Outbound.CharacterInfo, Is.EqualTo(0x1E));
        Assert.That((ushort) Inbound.CharacterInfo, Is.EqualTo(0x78));
        IOutPacket p = new CharacterInfoRequest(0x1122334455667788);
        Assert.That(Bytes(p.Write), Is.EqualTo(BitConverter.GetBytes(0x1122334455667788L)));
    }

    [Test] public void AnotherResultCarriesNoRecord() {
        var a = CharacterInfoAnswer.Read(R(w => { w.WriteLong(7); w.WriteByte(0); }));
        Assert.That((a.CharacterId, a.Result, a.Info), Is.EqualTo((7L, (byte) 0, (PcInfo) null)));
    }

    // CPCData::Write's order (pcdata.cpp:101 reads it): the record buffer
    static byte[] Record() => Bytes(w => {
        w.WriteLong(11); w.WriteLong(22); w.WriteUnicodeString("Other"); w.WriteShort(55);
        w.WriteInt(10); w.WriteInt(100); w.WriteInt(1); w.WriteInt(42); w.WriteByte(9);
        for (int b = 0; b < 3; b++) for (int s = 0; s < PcInfo.StatCount; s++) w.WriteLong(b * 1000 + s);
        w.WriteInt(1); w.WriteShort(unchecked((short) 0x8001)); w.WriteInt(5);
        w.WriteUnicodeString("pic"); w.WriteUnicodeString("motto"); w.WriteUnicodeString("guild"); w.WriteUnicodeString("grade");
        w.WriteUnicodeString("home");
        w.WriteInt(1); w.WriteInt(2); w.WriteInt(3); w.WriteInt(10000001);
        w.WriteInt(2); w.WriteInt(10000001); w.WriteInt(10000002);
        w.WriteInt(321); w.WriteInt(4); w.WriteLong(5);
        w.WriteInt(66); w.WriteInt(77); w.WriteInt(0x101010); w.WriteInt(0x202020);
        w.WriteShort(3); w.WriteUnicodeString("g2"); w.WriteUnicodeString("emblem"); w.WriteLong(88); w.WriteLong(99);
    });

    static void Equip(ByteWriter w, int id, long uid, byte slot) { w.WriteInt(id); w.WriteLong(uid); w.WriteByte(slot); w.WriteInt(1); ItemReader.WriteEmptyBody(w, 1); }

    // a gem's body: the empty body with CItemGem's has-flag (false) after the limit break, 36 bytes before its end
    static byte[] GemBody() { var b = Bytes(w => ItemReader.WriteEmptyBody(w, 1)).ToList(); b.Insert(b.Count - 36, 0); return b.ToArray(); }

    static ByteReader Answer(byte[] items, byte[] extra) => R(w => {
        w.WriteLong(22); w.WriteByte(1);
        w.WriteLong(1); w.WriteLong(2); w.WriteLong(3);
        foreach (var buf in new[] { Record(), items, extra }) { w.WriteInt(buf.Length); w.WriteBytes(buf); }
    });

    [Test] public void PackIsTheWindowsRecord() {
        var items = Bytes(w => {
            w.WriteByte(2); Equip(w, 11200001, 501, 8); Equip(w, 11400001, 502, 5);
            w.WriteBool(true); w.WriteLong(1000); w.WriteLong(2000);
            w.WriteByte(1); Equip(w, 12200001, 503, 8);
        });
        var extra = Bytes(w => { w.WriteByte(1); w.WriteByte(4); w.WriteInt(40000001); w.WriteLong(601); w.WriteInt(1); w.WriteBytes(GemBody()); });
        var a = CharacterInfoAnswer.Read(Answer(items, extra));
        var i = a.Info;
        Assert.That((a.CharacterId, a.Result, i.IsLocal), Is.EqualTo((22L, (byte) 1, false)));
        Assert.That((i.AccountId, i.CharacterId, i.Name, i.Level, i.JobGroup, i.SubJob, i.Gender, i.PrestigeLevel),
                    Is.EqualTo((11L, 22L, "Other", (ushort) 55, 10, 100, (byte) 1, 42)));
        Assert.That((i.Picture, i.Motto, i.GuildName, i.GuildGrade, i.Title, i.Trophy, i.HomeCommendPoint, i.HomeCommendMonthly),
                    Is.EqualTo(("pic", "motto", "guild", "grade", 10000001, 321, 66, 77)));
        Assert.That(i.HomeAddress, Is.Empty);                               // STILL-OPEN: FormatBuddy
        Assert.That(i.Titles, Is.EqualTo(new uint[] { 10000001, 10000002 }));
        Assert.That(i.Special, Is.EqualTo(new Dictionary<int, int> { [0x8001] = 5 }));
        Assert.That((i.Stats0[1], i.Stats1[2], i.Stats2[0x22]), Is.EqualTo((1L, 1002L, 2034L)));
        Assert.That(i.Equips.Select(e => (e.Id, e.Uid, e.Slot)), Is.EqualTo(new[] { (11200001, 501L, EquipSlot.CL), (11400001, 502L, EquipSlot.RH) }));
        Assert.That((i.CodiActive, i.CodiStart, i.CodiEnd, i.CodiItems), Is.EqualTo((true, 1000L, 2000L, 1)));
        Assert.That(i.Outfit2.Single().Uid, Is.EqualTo(503L));
        var gem = i.Gems.Single();
        Assert.That((gem.Id, gem.Uid, gem.IsBadge, gem.BadgeSlot), Is.EqualTo((40000001, 601L, true, (byte) 0)), "the slot byte is not kept");
        var pack = PcDataPack.Read(new ByteReader(Bytes(w => { w.WriteLong(1); w.WriteLong(2); w.WriteLong(3);
            foreach (var buf in new[] { Record(), items, extra }) { w.WriteInt(buf.Length); w.WriteBytes(buf); } })));
        Assert.That((pack.Value18, pack.Value20, pack.Value28), Is.EqualTo((1L, 2L, 3L)));
        Assert.That((pack.Record.HomeName, pack.Record.HomeMapId, pack.Record.HomeType, pack.Record.HomeValue), Is.EqualTo(("home", 1, 2, 3)));
        Assert.That((pack.Record.Value44, pack.Record.Value3fc, pack.Record.Value400), Is.EqualTo(((byte) 9, 4, 5L)));
        Assert.That((pack.Record.SkinColor0, pack.Record.SkinColor1, pack.Record.GuildRank), Is.EqualTo((0x101010, 0x202020, (ushort) 3)));
        Assert.That((pack.Record.GuildName2, pack.Record.GuildEmblemPath, pack.Record.GuildId, pack.Record.GuildEmblem), Is.EqualTo(("g2", "emblem", 88L, 99L)));
    }

    [Test] public void InactiveLayerKeepsNoOutfitsAndUnknownIdsEndTheLists() {
        var was = ItemReader.Known;
        try {
            ItemReader.Known = id => id != 11400001 && id != 40000002;
            var items = Bytes(w => {
                // the unknown second equip is read as a header alone and ends the list; the third is never reached
                w.WriteByte(3); Equip(w, 11200001, 501, 8);
                w.WriteInt(11400001); w.WriteLong(502); w.WriteByte(5); w.WriteInt(1);
                w.WriteBool(false); w.WriteLong(0); w.WriteLong(0);
                w.WriteByte(1); Equip(w, 12200001, 503, 8);
            });
            var extra = Bytes(w => { w.WriteByte(2); w.WriteByte(0); w.WriteInt(40000002); w.WriteLong(601); w.WriteInt(1); });
            var i = CharacterInfoAnswer.Read(Answer(items, extra)).Info;
            Assert.That(i.Equips.Select(e => e.Uid), Is.EqualTo(new[] { 501L }));
            Assert.That((i.CodiActive, i.CodiItems), Is.EqualTo((false, 0)));
            Assert.That(i.Outfit2, Is.Empty, "m_mapEquip2 is filled only while m_508");
            Assert.That(i.Gems, Is.Empty);
        } finally { ItemReader.Known = was; }
    }
}
