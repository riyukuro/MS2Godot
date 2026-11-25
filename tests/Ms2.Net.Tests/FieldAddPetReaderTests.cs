using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// FieldAddPet on both regions: CSceneStateGame::OnFieldAddPet @0x141719a60 (scenestategame.cpp:9682-9709) reads the same body on
/// KMS (0x5A) and GMS2 (0x5B): object id, skin id (vfunc_124), npc id (SetPetId), pose, scale, owner, stats, uid, u8, i16 level,
/// i16 taming rank, i32, name.
/// </summary>
public class FieldAddPetReaderTests {
    static ByteReader Body(int skin, int npc, short level, short rank, string name) {
        var w = new ByteWriter();
        w.WriteInt(7); w.WriteInt(skin); w.WriteInt(npc);
        w.WriteFloat(1); w.WriteFloat(2); w.WriteFloat(3); w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(90); w.WriteFloat(1.5f); w.WriteInt(99);
        w.WriteByte(35); for (int i = 0; i < 3; i++) { w.WriteLong(2500); w.WriteInt(100); }
        w.WriteLong(5); w.WriteByte(0); w.WriteShort(level); w.WriteShort(rank); w.WriteInt(0); w.WriteUnicodeString(name);
        return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
    }

    [Test]
    public void KmsReadsTheSkinIdNotALevelThirdAndTheWholeBody() {
        var r = Body(61100010, 61100020, 4, 5, "Mimi");
        var p = FieldAddPet.Read(r, ProtocolProfile.Kms2525);
        Assert.That((p.ObjectId, p.SkinId, p.NpcId, p.OwnerId, p.Level, p.TamingRank, p.PetUid, p.Name),
            Is.EqualTo((7, 61100010, 61100020, 99, 4, (short) 5, 5L, "Mimi")));
        Assert.That(p.Stats[0], Is.EqualTo((2500L, 100)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test]
    public void BothRegionsReadTheSameBody() {
        var k = FieldAddPet.Read(Body(1, 2, 3, 4, "a"), ProtocolProfile.Kms2525);
        var g = FieldAddPet.Read(Body(1, 2, 3, 4, "a"), ProtocolProfile.Gms2);
        Assert.That((k.SkinId, k.NpcId, k.Level, k.TamingRank, k.Name), Is.EqualTo((g.SkinId, g.NpcId, g.Level, g.TamingRank, g.Name)));
    }

    /// <summary>Every KMS2 recording reads to its end; the skin differs from the npc in some (a pet drawn as another npc), and the
    /// level is a level (the old prefix reader put the npc id there).</summary>
    [Test]
    public void EveryRecordedKmsFieldAddPetIsReadWhole() {
        var all = Gms2Sniffs.Inbound("kms2", ProtocolProfile.Kms2525.Number(Inbound.FieldAddPet));
        if (all.Count == 0) Assert.Ignore("no KMS2 FieldAddPet recordings");
        int skinned = 0;
        foreach (var (file, data) in all) {
            var r = new ByteReader(data);
            var p = FieldAddPet.Read(r, ProtocolProfile.Kms2525);
            Assert.That(r.Available, Is.EqualTo(0), file);
            Assert.That((p.SkinId > 1000000, p.NpcId > 1000000, p.Level is >= 0 and <= 100), Is.EqualTo((true, true, true)),
                $"{file}: {p.SkinId}/{p.NpcId} level {p.Level}");
            if (p.SkinId != p.NpcId) skinned++;
        }
        Assert.That(skinned, Is.GreaterThan(0), "a pet drawn as another npc");
        TestContext.Out.WriteLine($"{all.Count} KMS2 FieldAddPet packets, {skinned} with a skin other than the npc");
    }
}
