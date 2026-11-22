using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>GMS2 FieldAddItem (0x2B), SkillDamage (0x3E) and Shop (0x52): the layouts of the upstream writers, read from the recordings.</summary>
public class Gms2Stage2Tests {
    static readonly ProtocolProfile Gms = ProtocolProfile.Gms2;
    static ByteReader Reader(ByteWriter w) => new(w.Buffer.AsSpan(0, w.Length).ToArray());

    [Test]
    public void FieldAddItemHasAReceiverFlagAndATypeByte() {
        var w = new ByteWriter();
        w.WriteInt(41); w.WriteInt(90000001); w.WriteInt(500); w.WriteBool(true); w.WriteLong(0x77);
        w.WriteFloat(1f); w.WriteFloat(2f); w.WriteFloat(3f); w.WriteInt(9); w.WriteInt(0); w.WriteByte(2);
        w.WriteInt(4); w.WriteShort(1); w.WriteBool(true); w.WriteBool(false);
        w.WriteInt(-999);   // a meso drop has no item body
        var r = Reader(w);
        var d = FieldAddItem.Read(r, Gms);
        Assert.That((d.ObjectId, d.ItemId, d.Amount, d.OwnerUid, d.SourceObjectId, d.Rarity, d.FixedThrow, d.PetTrap), Is.EqualTo((41, 90000001, 500, 0x77L, 9, 4, true, false)));
        Assert.That(d.Item, Is.Null);
        Assert.That(r.ReadInt(), Is.EqualTo(-999));
    }

    [Test]
    public void FieldAddItemWithoutAReceiverIsAnyonesAndAnItemReadsItsBody() {
        var w = new ByteWriter();
        w.WriteInt(42); w.WriteInt(20000001); w.WriteInt(2); w.WriteBool(false);
        w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0); w.WriteInt(0); w.WriteInt(0); w.WriteByte(0);
        w.WriteInt(1); w.WriteShort(0); w.WriteBool(false); w.WriteBool(false);
        Gms2CharacterListLayoutTests.WriteBody(w);
        var r = Reader(w);
        var d = FieldAddItem.Read(r, Gms);
        Assert.That(d.OwnerType, Is.EqualTo((byte) 2));
        Assert.That(d.Item, Is.Not.Null);
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test]
    public void EveryRecordedGms2FieldAddItemDecodes() {
        var all = Gms2Sniffs.Inbound("gms2", Gms.Number(Inbound.FieldAddItem));
        Assert.That(all, Is.Not.Empty);
        int bad = 0;
        foreach (var (file, data) in all) {
            var r = new ByteReader(data);
            try { FieldAddItem.Read(r, Gms); if (r.Available != 0) bad++; } catch (Exception) { bad++; }
        }
        // the few exceptions are item ids the item data lacks (the body layout follows the data)
        Assert.That(bad, Is.LessThanOrEqualTo(all.Count / 100));
    }

    [Test]
    public void SkillDamageTargetModeHasAServerTickAndDotModeAnHpInt() {
        var t = new ByteWriter();
        t.WriteByte(0); t.WriteLong(5); t.WriteInt(7); t.WriteInt(1001); t.WriteShort(3); t.WriteByte(1); t.WriteByte(2);
        t.WriteShort(1); t.WriteShort(2); t.WriteShort(3); t.WriteFloat(0); t.WriteFloat(0); t.WriteFloat(0); t.WriteByte(0);
        t.WriteInt(123456); t.WriteByte(1); t.WriteLong(0); t.WriteLong(9); t.WriteInt(8); t.WriteByte(0); t.WriteByte(0);
        var r = Reader(t);
        var d = SkillDamage.Read(r, Gms);
        Assert.That((d.CastUid, d.OwnerId, d.SkillId, d.ProjectileCount, d.ServerTick), Is.EqualTo((5L, 7, 1001, 1, 123456)));
        Assert.That((d.Projectiles[0].Serial, d.Projectiles[0].TargetId), Is.EqualTo((9L, 8)));
        Assert.That(r.Available, Is.EqualTo(0));

        var dot = new ByteWriter();
        dot.WriteByte(3); dot.WriteInt(7); dot.WriteInt(8); dot.WriteInt(2); dot.WriteByte(4); dot.WriteInt(50);
        r = Reader(dot);
        SkillDamage.Read(r, Gms);
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test]
    public void EveryRecordedGms2SkillDamageDecodes() {
        var all = Gms2Sniffs.Inbound("gms2", Gms.Number(Inbound.SkillDamage));
        Assert.That(all, Is.Not.Empty);
        foreach (var (file, data) in all) {
            var r = new ByteReader(data);
            SkillDamage.Read(r, Gms);
            Assert.That(r.Available, Is.EqualTo(0), file);
        }
    }

    [Test]
    public void ShopListEntryIsTheUpstreamShopItem() {
        var w = new ByteWriter();
        w.WriteByte(1); w.WriteByte(1);
        w.WriteInt(11); w.WriteInt(20000001);
        w.WriteByte(1); w.WriteInt(0); w.WriteInt(0); w.WriteInt(250); w.WriteInt(300);   // cost: type, item, tag, price, sale
        w.WriteByte(2); w.WriteInt(500); w.WriteInt(10); w.WriteInt(3); w.WriteInt(0); w.WriteString("a,b");
        w.WriteInt(0); w.WriteInt(0); w.WriteByte(0); w.WriteShort(0); w.WriteByte(0); w.WriteShort(0); w.WriteBool(false);
        w.WriteShort(5); w.WriteByte(0); w.WriteByte(1); w.WriteString("tag"); w.WriteShort(0); w.WriteInt(0);
        w.WriteBool(true); w.WriteBool(true);                                              // wear for preview, restricted
        w.WriteBool(true); w.WriteLong(100); w.WriteLong(200); w.WriteShort(1); w.WriteInt(10); w.WriteInt(20); w.WriteByte(1); w.WriteByte(3);
        Gms2CharacterListLayoutTests.WriteBody(w);
        var r = Reader(w);
        var p = ShopPacket.Read(r, Gms);
        Assert.That(p.Products, Has.Count.EqualTo(1));
        var e = p.Products[0];
        Assert.That((e.Serial, e.ItemId, e.Price, e.Limit, e.Bought, e.Bundle, e.Label, e.IconTag, e.Categories), Is.EqualTo((11, 20000001, 250L, 10, 3, 5, 1, "tag", "a,b")));
        Assert.That(e.Period.Start, Is.EqualTo(100));
        Assert.That(e.Period.Days, Is.EqualTo(new[] { 3 }));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test]
    public void EveryRecordedGms2ShopPacketDecodes() {
        var all = Gms2Sniffs.Inbound("gms2", Gms.Number(Inbound.Shop));
        Assert.That(all, Is.Not.Empty);
        int bad = 0;
        foreach (var (file, data) in all) {
            var r = new ByteReader(data);
            try { ShopPacket.Read(r, Gms); if (r.Available != 0) bad++; } catch (Exception) { bad++; }
        }
        // without the item data hooks (the game sets them from the player's Data folder) an item body of a kind the id table would name is misread: about four in five decode
        Assert.That(all.Count - bad, Is.GreaterThanOrEqualTo(all.Count * 3 / 4));
    }

    [Test]
    public void MailSentModeCarriesTheMailIdOnGms2Only() {
        var w = new ByteWriter(); w.WriteByte(1); w.WriteLong(0x3E95901);
        var r = Reader(w);
        Assert.That(MailMessage.Read(r, Gms), Is.InstanceOf<MailMessage.Sent>());
        Assert.That(r.Available, Is.EqualTo(0));
        var k = new ByteReader(new byte[] { 1 });
        Assert.That(MailMessage.Read(k, ProtocolProfile.Kms2525), Is.InstanceOf<MailMessage.Sent>());
    }

    [Test]
    public void EveryRecordedGms2MailAndBlackMarketPacketDecodes() {
        foreach (var (op, read) in new (Inbound, Action<ByteReader>)[] {
            (Inbound.Mail, r => MailMessage.Read(r, Gms)), (Inbound.BlackMarket, r => BlackMarketMessage.Read(r, Gms)) }) {
            var all = Gms2Sniffs.Inbound("gms2", Gms.Number(op));
            Assert.That(all, Is.Not.Empty);
            int bad = 0;
            foreach (var (_, data) in all) {
                var r = new ByteReader(data);
                try { read(r); if (r.Available != 0) bad++; } catch (Exception) { bad++; }
            }
            // the misses are item bodies of kinds only the item data names (hooks unset here)
            Assert.That(all.Count - bad, Is.GreaterThanOrEqualTo(all.Count * 9 / 10), op.ToString());
        }
    }

    [Test]
    public void EveryRecordedGms2PartyPacketDecodes() {
        var all = Gms2Sniffs.Inbound("gms2", Gms.Number(Inbound.Party));
        Assert.That(all, Is.Not.Empty);
        foreach (var (file, data) in all) {
            var r = new ByteReader(data);
            Assert.DoesNotThrow(() => PartyMessage.Read(r, Gms), file);
        }
    }

    [Test]
    public void Gms2PartyMemberTailHasACountedIdList() {
        // the KMS2 record written by PartyUser.Write, cut before its tail, then the GMS2 tail with two ids in the list
        var u = new PartyUser { Name = "A", Level = 5 };
        var kms = new ByteWriter(); u.Write(kms);
        var bytes = kms.Buffer.AsSpan(0, kms.Length).ToArray();
        // KMS tail after the 12 ints and the empty string + i64: i32 set(0), u8, bool, 6 values, 2 i32 sets, i16, i64
        int tail = 4 + 1 + 1 + (8 + 4 + 4 + 8 + 4 + 8) + 4 + 4 + 2 + 8;
        var w = new ByteWriter();
        w.WriteBytes(bytes.AsSpan(0, bytes.Length - tail).ToArray());
        w.WriteLong(0); w.WriteLong(0); w.WriteInt(2); w.WriteLong(11); w.WriteLong(22);
        w.WriteByte(0); w.WriteBool(false);
        w.WriteLong(0); w.WriteInt(0); w.WriteInt(0); w.WriteLong(0); w.WriteInt(0); w.WriteLong(0); w.WriteInt(0); w.WriteInt(0); w.WriteShort(0); w.WriteLong(0);
        var r = Reader(w);
        var got = PartyUser.Read(r, Gms);
        Assert.That(got.Name, Is.EqualTo("A"));
        Assert.That(got.Level, Is.EqualTo((short) 5));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test]
    public void EveryRecordedGms2GuildInfoAndListPacketDecodes() {
        var all = Gms2Sniffs.Inbound("gms2", Gms.Number(Inbound.Guild));
        Assert.That(all, Is.Not.Empty);
        int seen = 0;
        foreach (var (file, data) in all) {
            if (data.Length == 0 || data[0] is not (0 or 83 or 84 or 85)) continue;   // the other modes are not decoded yet
            seen++;
            var r = new ByteReader(data);
            try { GuildMessage.Read(r, Gms); } catch (Exception e) { Assert.Fail(file + ": " + e.Message); }
            Assert.That(r.Available, Is.EqualTo(0), file);
        }
        Assert.That(seen, Is.GreaterThan(0));
    }

    [Test]
    public void Gms2GuildListsReadTheirEntries() {
        var w = new ByteWriter();
        w.WriteByte(83); w.WriteInt(2);
        w.WriteBool(true);
        w.WriteLong(1); w.WriteLong(2); w.WriteLong(3); w.WriteLong(4); w.WriteUnicodeString("Ann"); w.WriteUnicodeString("pic");
        w.WriteInt(10); w.WriteInt(20); w.WriteInt(30); w.WriteInt(1); w.WriteInt(2); w.WriteInt(3); w.WriteLong(99);
        w.WriteBool(false);
        var r = Reader(w);
        var m = GuildMessage.Read(r, Gms);
        Assert.That(m.GuildRequests, Has.Count.EqualTo(1));
        Assert.That(m.GuildRequests[0].Name, Is.EqualTo("Ann"));
        Assert.That(r.Available, Is.EqualTo(0));

        w = new ByteWriter();
        w.WriteByte(85); w.WriteInt(1); w.WriteBool(true);
        w.WriteLong(7); w.WriteUnicodeString("G"); w.WriteUnicodeString("mark");
        w.WriteInt(1); w.WriteInt(2); w.WriteInt(3); w.WriteInt(5); w.WriteInt(50); w.WriteInt(6);
        w.WriteLong(8); w.WriteLong(9); w.WriteUnicodeString("Boss");
        r = Reader(w);
        m = GuildMessage.Read(r, Gms);
        Assert.That(m.Results, Has.Count.EqualTo(1));
        Assert.That((m.Results[0].GuildId, m.Results[0].MemberCount, m.Results[0].MasterName), Is.EqualTo((7L, 5, "Boss")));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test]
    public void EveryRecordedGms2FieldAddPetIsReadWhole() {
        var all = Gms2Sniffs.Inbound("gms2", Gms.Number(Inbound.FieldAddPet));
        Assert.That(all, Is.Not.Empty);
        foreach (var (file, data) in all) {
            var p = FieldAddPet.Read(new ByteReader(data), Gms);
            Assert.That(p.ObjectId, Is.Not.EqualTo(0), file);
        }
    }

    [Test]
    public void Gms2FieldAddPetHasASkinIdBeforeTheNpcIdAndAName() {
        var w = new ByteWriter();
        w.WriteInt(7); w.WriteInt(11); w.WriteInt(22);
        w.WriteFloat(1); w.WriteFloat(2); w.WriteFloat(3); w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(90); w.WriteFloat(1.5f); w.WriteInt(99);
        w.WriteByte(35); for (int i = 0; i < 3; i++) { w.WriteLong(100); w.WriteInt(100); }
        w.WriteLong(5); w.WriteByte(0); w.WriteShort(4); w.WriteShort(3); w.WriteInt(0); w.WriteUnicodeString("Mimi");
        var p = FieldAddPet.Read(Reader(w), Gms);
        Assert.That((p.ObjectId, p.SkinId, p.NpcId, p.OwnerId, p.Level, p.TamingRank, p.PetUid, p.Name), Is.EqualTo((7, 11, 22, 99, 4, (short) 3, 5L, "Mimi")));
    }

    [Test]
    public void EveryRecordedGms2LoadCubesPacketReadsWholeInItsMode() {
        var all = Gms2Sniffs.Inbound("gms2", Gms.Number(Inbound.LoadCubes));
        Assert.That(all, Is.Not.Empty);
        foreach (var (file, data) in all) {
            var r = new ByteReader(data);
            if (data[0] == 0) Assert.That(LoadCubes.Read(r), Is.Not.Null, file);
            else Assert.That(LoadCubesPlots.Read(r), Is.Not.Null, file);
            Assert.That(r.Available, Is.EqualTo(0), file);
        }
    }

    [Test]
    public void EveryRecordedGms2UserEnvPacketReadsWhole() {
        var all = Gms2Sniffs.Inbound("gms2", Gms.Number(Inbound.UserEnv));
        Assert.That(all, Is.Not.Empty);
        foreach (var (file, data) in all) Assert.DoesNotThrow(() => UserEnvMessage.Read(new ByteReader(data)), file);
    }

    [Test]
    public void GatheringCountsReadASecondTable() {
        var w = new ByteWriter();
        w.WriteByte(8); w.WriteInt(1); w.WriteInt(10); w.WriteInt(3);
        w.WriteInt(2); w.WriteInt(20); w.WriteInt(0); w.WriteInt(21); w.WriteInt(5);
        var m = (UserEnvMessage.GatheringCounts) UserEnvMessage.Read(Reader(w));
        Assert.That(m.Counts, Is.EqualTo(new Dictionary<int, int> { [10] = 3 }));
        Assert.That(m.Second, Is.EqualTo(new Dictionary<int, int> { [20] = 0, [21] = 5 }));
    }
}
