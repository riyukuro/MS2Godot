using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// FieldAddUser of the GMS2 profile, built field by field from the Maple2 upstream server's FieldPacket.AddPlayer (WriteCharacter, SkillInfo.WriteTo,
/// HeldCube.WriteTo, RideOnAction.WriteTo, Buff.WriteTo, WritePlayerStats, the equip, badge and Item writers). There is no old code path to
/// fail: the profile overloads do not exist without the profile.
/// </summary>
public class Gms2FieldAddUserTests {
    static void Block(ByteWriter w, Action<ByteWriter> body) {
        var b = new ByteWriter(); body(b);
        var data = b.ToArray();
        w.WriteBool(false); w.WriteInt(data.Length); w.WriteBytes(data);
    }

    static void Skills(ByteWriter w) {
        w.WriteInt(10); w.WriteByte(1); w.WriteInt(100);                    // job, group count, job code
        for (int t = 0; t < 4; t++) {
            w.WriteByte((byte) (t == 1 ? 2 : 0));
            if (t != 1) continue;
            w.WriteBool(true); w.WriteBool(true); w.WriteInt(10100001); w.WriteInt(3); w.WriteByte(0);
            w.WriteBool(false); w.WriteBool(false); w.WriteInt(10100002); w.WriteInt(1); w.WriteByte(1);
        }
    }

    static byte[] Packet(bool ride, bool pet) {
        var w = new ByteWriter();
        w.WriteInt(77);
        Gms2CharacterListLayoutTests.WriteCharacter(w);
        Skills(w);
        for (int i = 0; i < 6; i++) w.WriteFloat(i + 1);                    // position, rotation
        w.WriteByte(0);
        w.WriteByte(0x23);
        for (int i = 0; i < 3; i++) { w.WriteLong(1000 + i); w.WriteInt(100); w.WriteInt(200); w.WriteInt(300); w.WriteInt(400); }
        w.WriteBool(true);
        w.WriteByte(0); w.WriteInt(50000001); w.WriteLong(9); w.WriteLong(0); w.WriteBool(false); w.WriteInt(0);   // HeldCube, no template
        w.WriteInt(1); w.WriteInt(2);                                 // SkinColor: two rgba
        w.WriteUnicodeString("pic");
        w.WriteBool(ride);
        if (ride) { w.WriteByte(3); w.WriteInt(5); w.WriteInt(6); w.WriteByte(0); }   // RideOnType.Object: no fields after the object id; unknown count
        w.WriteInt(0); w.WriteLong(1700000000); w.WriteInt(11); w.WriteInt(12);
        Block(w, b => {
            b.WriteByte(2);
            Gms2CharacterListLayoutTests.WriteEquip(b, 11400001, 1, "CL", 3);
            Gms2CharacterListLayoutTests.WriteEquip(b, 10100001, 2, "HR", 1, null);
            b.WriteBool(true); b.WriteLong(0); b.WriteLong(0); b.WriteByte(0);
        });
        Block(w, b => b.WriteByte(0));
        Block(w, b => {
            b.WriteByte(1);
            Gms2CharacterListLayoutTests.WriteBadge(b, 1, 70000001, x => { for (int i = 0; i < 10; i++) x.WriteBool(false); });
        });
        w.WriteShort(1);
        w.WriteInt(77); w.WriteInt(901); w.WriteInt(77); w.WriteInt(1); w.WriteInt(2); w.WriteInt(70000001); w.WriteShort(4); w.WriteInt(1); w.WriteBool(true); w.WriteLong(0);
        w.WriteInt(0); w.WriteInt(0);
        w.WriteByte(0);
        w.WriteInt(0); w.WriteByte(0); w.WriteByte(0);
        w.WriteInt(1234); w.WriteShort(5); w.WriteByte(0);
        w.WriteInt(0);
        w.WriteBool(pet);
        if (pet) {
            w.WriteInt(60000001); w.WriteLong(31); w.WriteInt(2);
            Gms2CharacterListLayoutTests.WriteBody(w, extra: x => { x.WriteUnicodeString("Mochi"); x.WriteLong(5); x.WriteInt(1); x.WriteInt(7); x.WriteBool(false); });
        }
        w.WriteLong(1800000000); w.WriteInt(0); w.WriteByte(0);
        w.WriteInt(0); w.WriteInt(0); w.WriteShort(2);                      // Tail: two ints, then the wedding state
        return w.ToArray();
    }

    [Test]
    public void ReadsTheWholePacketIncludingTheTwoExtraTailInts() {
        var f = FieldAddUser.Read(new ByteReader(Packet(false, false)), ProtocolProfile.Gms2);
        Assert.That(f.ObjectId, Is.EqualTo(77)); Assert.That(f.User.Name, Is.EqualTo("Hero")); Assert.That(f.User.PlatformName, Is.EqualTo("plat"));
        Assert.That(f.Position.X, Is.EqualTo(1)); Assert.That(f.Rotation.Z, Is.EqualTo(6));
        Assert.That(f.Stats[2].Health, Is.EqualTo(1002)); Assert.That(f.CubeItem.ItemId, Is.EqualTo(50000001));
        Assert.That(f.Picture, Is.EqualTo("pic")); Assert.That(f.Ride, Is.Null);
        Assert.That(f.Equips.Select(e => e.Slot), Is.EqualTo(new[] { EquipSlot.CL, EquipSlot.HR }));
        Assert.That(f.Badges, Has.Count.EqualTo(1));
        Assert.That(f.Buffs.Single().SkillId, Is.EqualTo(70000001)); Assert.That(f.Title, Is.EqualTo(1234)); Assert.That(f.Insignia, Is.EqualTo(5));
        Assert.That(f.PremiumTime, Is.EqualTo(1800000000)); Assert.That(f.WeddingState, Is.EqualTo(2));
    }

    [Test]
    public void SkillBlockIsPerTypeListsWithTheNotifyFlagFirst() {
        var f = FieldAddUser.Read(new ByteReader(Packet(false, false)), ProtocolProfile.Gms2);
        Assert.That(f.SkillRank, Is.EqualTo(10)); Assert.That(f.SkillJobCode, Is.EqualTo(100));
        Assert.That(f.Book.Skills.Select(s => (s.SkillId, s.Level, s.Changed, s.Learned, s.Extra)),
            Is.EqualTo(new[] { (10100001, 3, true, true, false), (10100002, 1, false, false, true) }));
    }

    [Test]
    public void RideActionHasNoFlagByteAndPetIsAGms2Item() {
        var f = FieldAddUser.Read(new ByteReader(Packet(true, true)), ProtocolProfile.Gms2);
        Assert.That(f.Ride, Is.Not.Null); Assert.That(f.Ride.RideId, Is.EqualTo(5)); Assert.That(f.Passengers, Is.Empty);
        Assert.That(f.HasPet); Assert.That(f.Pet.Id, Is.EqualTo(60000001)); Assert.That(f.Pet.PetName, Is.EqualTo("Mochi"));
    }

    [Test]
    public void KmsReaderRejectsTheGms2Frame() {
        Assert.Catch<Exception>(() => FieldAddUser.Read(new ByteReader(Packet(false, false)), ProtocolProfile.Kms2525));
    }
}
