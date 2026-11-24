using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>ResponsePet (KMS 0xCC, GMS2 0xCE): CPetManager::OnResponsePet (petmanager.cpp:722). The modes recorded on GMS2 are 0, 1, 2, 5, 6, 7, 9, 0xA, 0xC and 0xF.</summary>
public class RcPetResponseTests {
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }
    static ByteReader Hex(params string[] s) => new(RcPkt.Hex(s.Select(x => x.Replace(" ", "")).ToArray()));
    static PetResponse Read(ByteReader r) => PetResponse.Read(r, ProtocolProfile.Gms2);

    // recorded: pet_summon_and_settings (modes 5, 6), Accept (9), pet_fusion (0xA, 0xC), Ground_Buff... (1), Dark_Descent (2)
    [Test] public void OptionsUpdateIsOwnerAndOptionList() {
        var m = Read(Hex("05 89 FE 4C 00 02 09 00 00 00 00 00 00 00 00 00 00 00 09 00 00 00 00 00 00 00 00 00 00 00"));
        Assert.That(m.Kind, Is.EqualTo(PetResponseKind.OptionsUpdate));
        Assert.That(m.OwnerId, Is.EqualTo(0x004CFE89));
        Assert.That(m.Options, Is.EqualTo(new[] { new PetOption(9, 0f, 0), new PetOption(9, 0f, 0) }));
    }

    [Test] public void FlagsUpdateIsOwnerAndFlags() {
        var m = Read(Hex("06 89 FE 4C 00 00 00 00 00 00 00 00 00 03 00 00 00 00"));
        Assert.That(m.Kind, Is.EqualTo(PetResponseKind.FlagsUpdate));
        Assert.That(m.Flags.Amount, Is.EqualTo(3));
        Assert.That(m.Flags.F0, Is.EqualTo(0));
    }

    [Test] public void UnsummonedIsOwnerAndUid() {
        var m = Read(Hex("01 74 01 2B 00 42 38 87 D2 C1 16 C0 29"));
        Assert.That(m.Kind, Is.EqualTo(PetResponseKind.Unsummoned));
        Assert.That(m.OwnerId, Is.EqualTo(0x002B0174));
        Assert.That(m.Uid, Is.EqualTo(0x29C016C1D2873842L));
    }

    [Test] public void SummonSoundIsOwnerOnly() {
        var m = Read(Hex("02 33 A4 53 00"));
        Assert.That(m.Kind, Is.EqualTo(PetResponseKind.SummonSound));
        Assert.That(m.OwnerId, Is.EqualTo(0x0053A433));
    }

    [Test] public void ExperienceIsOwnerExpAndUid() {
        var m = Read(Hex("0A 05 D0 4D 00 58 8A 00 00 00 00 00 00 0C 34 54 B3 40 98 6D 26"));
        Assert.That(m.Kind, Is.EqualTo(PetResponseKind.Experience));
        Assert.That(m.Exp, Is.EqualTo(0x8A58));
        Assert.That(m.Uid, Is.EqualTo(0x266D9840B354340CL));
    }

    [Test] public void FusionCountIsOneInt() {
        var m = Read(Hex("0C 01 00 00 00"));
        Assert.That(m.Kind, Is.EqualTo(PetResponseKind.FusionCount));
        Assert.That(m.Value, Is.EqualTo(1));
    }

    [Test] public void AutoChangedIsOneByte() {
        var m = Read(Hex("0F 01"));
        Assert.That(m.Flag, Is.EqualTo(1));
    }

    [Test] public void CountListIsKeyAndShortPairs() {
        var m = Read(R(w => { w.WriteByte(7); w.WriteInt(2); w.WriteInt(11); w.WriteShort(3); w.WriteInt(12); w.WriteShort(unchecked((short) 65535)); }));
        Assert.That(m.Counts, Is.EqualTo(new[] { new KeyValuePair<int, ushort>(11, 3), new KeyValuePair<int, ushort>(12, 65535) }));
    }

    [Test] public void CountUpdateIsOneKeyAndShort() {
        var m = Read(R(w => { w.WriteByte(8); w.WriteInt(11); w.WriteShort(4); }));
        Assert.That(m.Counts, Is.EqualTo(new[] { new KeyValuePair<int, ushort>(11, 4) }));
    }

    [Test] public void InfoIsIdentityOptionsAndFlags() {
        var m = Read(R(w => {
            w.WriteByte(9); w.WriteInt(7);
            w.WriteUnicodeString("Mochi"); w.WriteLong(1200); w.WriteInt(0); w.WriteShort(5); w.WriteShort(0); w.WriteShort(2);
            w.WriteByte(1); w.WriteInt(1); w.WriteFloat(0.5f); w.WriteInt(1);
            for (int i = 0; i < 8; i++) w.WriteByte(1); w.WriteInt(2); w.WriteByte(1);
        }));
        Assert.That(m.Identity, Is.EqualTo(new PetIdentity("Mochi", 1200, 0, 5, 0, 2)));
        Assert.That(m.Options, Is.EqualTo(new[] { new PetOption(1, 0.5f, 1) }));
        Assert.That(m.Flags.Amount, Is.EqualTo(2));
    }

    [Test] public void RenamedIsOwnerAndIdentity() {
        var m = Read(R(w => { w.WriteByte(4); w.WriteInt(7); w.WriteUnicodeString("Koro"); w.WriteLong(0); w.WriteInt(0); w.WriteShort(1); w.WriteShort(0); w.WriteShort(0); }));
        Assert.That(m.Identity.Name, Is.EqualTo("Koro"));
    }

    [Test] public void LevelUpAndItemValueAreOwnerIntAndUid() {
        foreach (byte mode in new byte[] { 0xB, 0x12 }) {
            var m = Read(R(w => { w.WriteByte(mode); w.WriteInt(7); w.WriteInt(9); w.WriteLong(0x1122334455L); }));
            Assert.That((m.OwnerId, m.Value, m.Uid), Is.EqualTo((7, 9, 0x1122334455L)));
        }
    }

    [Test] public void ErrorAndTrapSkillAreOneInt() {
        Assert.That(Read(R(w => { w.WriteByte(0x13); w.WriteInt(0x1b); })).Value, Is.EqualTo(0x1b));
        Assert.That(Read(R(w => { w.WriteByte(0x14); w.WriteInt(33); })).Kind, Is.EqualTo(PetResponseKind.TrapSkillConsumed));
    }

    [Test] public void SummonedWithoutItemIsFlagFalse() {
        var m = Read(R(w => { w.WriteByte(0); w.WriteInt(7); w.WriteInt(8); w.WriteBool(false); }));
        Assert.That(m.Item, Is.Null);
        Assert.That(m.Value, Is.EqualTo(8));
    }

    [Test] public void SummonedItemReadsHeadAndBody() {
        var m = PetResponse.Read(R(w => {
            w.WriteByte(0); w.WriteInt(7); w.WriteInt(8); w.WriteBool(true);
            w.WriteInt(61000001); w.WriteLong(0x77L); w.WriteInt(4);
            ItemReader.WriteEmptyBody(w, 1);
        }), ProtocolProfile.Kms2525);
        Assert.That(m.Item.ItemId, Is.EqualTo(61000001));
        Assert.That(m.Item.Uid, Is.EqualTo(0x77L));
        Assert.That(m.Item.Item, Is.Not.Null);
    }

    [Test] public void ItemDataUpdateWithoutAnItemFoundReadsNoBody() {
        var m = Read(R(w => { w.WriteByte(0x11); w.WriteInt(7); w.WriteLong(5); w.WriteByte(1); w.WriteInt(123); }));
        Assert.That(m.Uid, Is.EqualTo(5));
        Assert.That(m.Item, Is.Null);
    }

    [Test] public void TrailingBytesAreRefused() {
        Assert.Throws<PacketLayoutException>(() => Read(Hex("0C 01 00 00 00 00")));
        Assert.Throws<PacketLayoutException>(() => Read(Hex("02 33 A4 53 00 00")));
    }

    [Test] public void UnknownModeReadsNothingMore() {
        var m = Read(Hex("03 01 02"));
        Assert.That(m.Kind, Is.EqualTo(PetResponseKind.Unknown));
    }

    [Test] public void ImpossibleCountsAreRefused() {
        Assert.Throws<InvalidDataException>(() => Read(R(w => { w.WriteByte(7); w.WriteInt(1000); w.WriteInt(1); w.WriteShort(1); })));
        Assert.Throws<InvalidDataException>(() => Read(R(w => { w.WriteByte(5); w.WriteInt(1); w.WriteByte(200); })));
    }

    static void SweepModes(Func<byte, bool> take, byte[] expected) {
        var all = Gms2Sniffs.Inbound("gms2", ProtocolProfile.Gms2.Number(Inbound.ResponsePet));
        Assert.That(all, Is.Not.Empty);
        var kinds = new HashSet<byte>();
        foreach (var (file, data) in all) {
            if (!take(data[0])) continue;
            var r = new ByteReader(data);
            var m = Read(r);
            kinds.Add(m.Mode);
            if (m.Kind == PetResponseKind.Summoned && m.Item?.Item != null) Assert.That(m.TrailerUid, Is.EqualTo(m.Item.Uid), file);
            Assert.That(m.Kind, Is.Not.EqualTo(PetResponseKind.Unknown), file);
            Assert.That(r.Available, Is.EqualTo(0), file);
        }
        Assert.That(kinds, Is.EquivalentTo(expected));
    }

    [Test] public void EveryRecordedGms2PacketWithoutAnItemReadsToTheEnd() =>
        SweepModes(m => m != 0, new byte[] { 1, 2, 5, 6, 7, 9, 0xA, 0xC, 0xF });

    [Test] public void EveryRecordedGms2SummonReadsToTheEndWithTheItemData() =>
        Gms2Sniffs.WithGms2ItemData(() => SweepModes(m => m == 0, new byte[] { 0 }));

    [Test] public void NumberOnBothProfiles() {
        Assert.That(ProtocolProfile.Kms2525.Number(Inbound.ResponsePet), Is.EqualTo((ushort) 0xCC));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.ResponsePet), Is.EqualTo((ushort) 0xCE));
    }
}
