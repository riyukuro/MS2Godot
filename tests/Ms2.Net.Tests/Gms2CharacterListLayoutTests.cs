using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// The character-list packets of the GMS2 profile, built field by field from the Maple2 upstream server's writers
/// (CharacterListPacket.cs WriteEntry/WriteCharacter, EquipPacketHelper.cs, Item.WriteTo and the ItemStats, ItemEnchant, ItemLimitBreak,
/// ItemTransfer, ItemSocket, ItemCoupleInfo, ItemBinding, ItemBadge writers) and read through the profile. KMS2 reads stay in PacketTests.
/// </summary>
public class Gms2CharacterListLayoutTests {
    static readonly ProtocolProfile Gms = ProtocolProfile.Gms2;
    static ByteReader R(ByteWriter w) => new(w.ToArray());

    // upstream Item.WriteTo with every optional block at its default, except what the callers pass in
    internal static void WriteBody(ByteWriter w, Action<ByteWriter> stats = null, Action<ByteWriter> enchantOptions = null, Action<ByteWriter> limitBreak = null,
                          Action<ByteWriter> extra = null, Action<ByteWriter> appearance = null) {
        w.WriteInt(1); w.WriteInt(0); w.WriteInt(-1);                 // Amount, 0, -1
        w.WriteLong(111); w.WriteLong(222); w.WriteLong(0);           // CreationTime, ExpiryTime, 0
        w.WriteInt(3); w.WriteInt(4); w.WriteBool(true); w.WriteLong(555);   // TimeChangedOption, RemainUses, IsLocked, UnlockTime
        w.WriteShort(6); w.WriteBool(false); w.WriteInt(7);           // GlamorForges, false, GachaDismantleId
        if (appearance != null) appearance(w);
        else { for (int i = 0; i < 3; i++) w.WriteInt(0); w.WriteInt(0); w.WriteInt(0); }   // ItemAppearance.Default: EquipColor (3 rgba, index, palette)
        w.WriteByte(0);                                               // ItemStats
        for (int i = 0; i < 9; i++) {
            if (i == 2 && stats != null) stats(w);
            else { w.WriteShort(0); w.WriteShort(0); }
            w.WriteInt(i + 100);
        }
        w.WriteInt(8); w.WriteInt(9); w.WriteByte(10); w.WriteLong(0); w.WriteInt(0); w.WriteInt(0); w.WriteBool(true); w.WriteInt(11);   // ItemEnchant
        if (enchantOptions != null) enchantOptions(w); else w.WriteByte(0);
        w.WriteInt(12);                                               // ItemLimitBreak
        if (limitBreak != null) limitBreak(w); else { w.WriteInt(0); w.WriteInt(0); }
        extra?.Invoke(w);
        w.WriteInt(2); w.WriteBool(false); w.WriteInt(13); w.WriteInt(14); w.WriteByte(0); w.WriteBool(true);   // ItemTransfer
        w.WriteBool(false);                                           // no transfer binding
        w.WriteByte(3); w.WriteByte(1); w.WriteBool(true); w.WriteInt(5000); w.WriteBool(true); w.WriteLong(77); w.WriteUnicodeString("gem");   // ItemSocket: one bound gem
        w.WriteBool(true); w.WriteByte(0); w.WriteLong(88);
        w.WriteLong(0);                                               // ItemCoupleInfo
        w.WriteLong(99); w.WriteUnicodeString("owner");               // ItemBinding
    }

    static void Hair(ByteWriter a) {
        for (int i = 0; i < 3; i++) a.WriteInt(0); a.WriteInt(0); a.WriteInt(0);
        for (int i = 0; i < 14; i++) a.WriteFloat(i + 1);
    }

    static void WriteEquip(ByteWriter w, int id, long uid, string slot, int rarity, Action<ByteWriter> body = null) {
        w.WriteInt(id); w.WriteLong(uid); w.WriteUnicodeString(slot); w.WriteInt(rarity);
        (body ?? (b => WriteBody(b, appearance: slot == "HR" ? Hair : null)))(w);
    }

    [Test]
    public void EquipSlotIsAUnicodeNameAndTheBodyHasAnExtraInt() {
        var w = new ByteWriter();
        WriteEquip(w, 11400001, 1234, "CL", 4);
        var it = ItemReader.ReadEquip(R(w), Gms);
        Assert.That(it.Id, Is.EqualTo(11400001)); Assert.That(it.Uid, Is.EqualTo(1234)); Assert.That(it.Slot, Is.EqualTo(EquipSlot.CL)); Assert.That(it.Rarity, Is.EqualTo(4));
        Assert.That(it.Amount, Is.EqualTo(1)); Assert.That(it.CreationTime, Is.EqualTo(111)); Assert.That(it.ExpiryTime, Is.EqualTo(222));
        Assert.That(it.RemainUses, Is.EqualTo(4)); Assert.That(it.LockState, Is.EqualTo(1)); Assert.That(it.UnlockTime, Is.EqualTo(555));
        Assert.That(it.GlamorForges, Is.EqualTo(6)); Assert.That(it.GachaDismantleId, Is.EqualTo(7));
    }

    [Test]
    public void EquipReadConsumesTheWholeBodyThroughBindingAndSocket() {
        var w = new ByteWriter();
        WriteEquip(w, 1, 2, "HR", 1, b => WriteBody(b, appearance: a => {
            for (int i = 0; i < 3; i++) a.WriteInt(0); a.WriteInt(0); a.WriteInt(0);      // EquipColor
            for (int i = 0; i < 14; i++) a.WriteFloat(i + 1);                              // HairAppearance: 1 + 3 + 3 + 1 + 3 + 3 floats
        }));
        var r = R(w);
        var it = ItemReader.ReadEquip(r, Gms);
        r.ExpectEnd("equip");
        Assert.That(it.Appearance.Length, Is.EqualTo(14));
        Assert.That(it.Socket.MaxSlot, Is.EqualTo(3)); Assert.That(it.Socket.Gems, Has.Count.EqualTo(1));
        Assert.That(it.Socket.Gems[0].GemId, Is.EqualTo(5000)); Assert.That(it.Socket.Gems[0].BindName, Is.EqualTo("gem")); Assert.That(it.Socket.Gems[0].LockExpireTime, Is.EqualTo(88));
        Assert.That(it.BindingId, Is.EqualTo(99)); Assert.That(it.BindingName, Is.EqualTo("owner"));
        Assert.That(it.Transfer.Rule, Is.EqualTo(2)); Assert.That(it.Transfer.Count, Is.EqualTo(13)); Assert.That(it.Transfer.Max, Is.EqualTo(14));
    }

    [Test]
    public void StatsAreNineSetsOfEightByteOptions() {
        var w = new ByteWriter();
        WriteEquip(w, 1, 2, "RH", 3, b => WriteBody(b, stats: s => {
            s.WriteShort(2); s.WriteShort(5); s.WriteInt(70); s.WriteFloat(0.25f); s.WriteShort(6); s.WriteInt(0); s.WriteFloat(0.5f);   // BasicOption: Value, Rate
            s.WriteShort(1); s.WriteShort(9); s.WriteFloat(0.125f); s.WriteFloat(33f);                                                   // SpecialOption: Rate, Value
        }));
        var r = R(w);
        var it = ItemReader.ReadEquip(r, Gms);
        r.ExpectEnd("equip");
        Assert.That(it.Options.Stat, Has.Length.EqualTo(9));
        Assert.That(it.Options.Stat[2][5], Is.EqualTo(70)); Assert.That(it.Options.StatRate[2][5], Is.EqualTo(0.25f));
        Assert.That(it.Options.Stat[2][6], Is.EqualTo(0)); Assert.That(it.Options.StatRate[2][6], Is.EqualTo(0.5f));
        Assert.That(it.Options.Special[2][9], Is.EqualTo(33)); Assert.That(it.Options.SpecialRate[2][9], Is.EqualTo(0.125f));
        Assert.That(it.Options.MergeId, Is.EqualTo(108));      // the last set's trailing int (8 + 100)
    }

    [Test]
    public void EnchantAndLimitBreakOptionsAreEightBytes() {
        var w = new ByteWriter();
        WriteEquip(w, 1, 2, "LH", 3, b => WriteBody(b,
            enchantOptions: e => { e.WriteByte(1); e.WriteInt(4); e.WriteInt(15); e.WriteFloat(0.75f); },
            limitBreak: l => { l.WriteInt(1); l.WriteShort(3); l.WriteInt(8); l.WriteFloat(0.5f); l.WriteInt(1); l.WriteShort(4); l.WriteFloat(0.375f); l.WriteFloat(12f); }));
        var r = R(w);
        var it = ItemReader.ReadEquip(r, Gms);
        r.ExpectEnd("equip");
        Assert.That(it.Enchant.Level, Is.EqualTo(8)); Assert.That(it.Enchant.Charge, Is.EqualTo(9)); Assert.That(it.Enchant.Version, Is.EqualTo(10));
        Assert.That(it.Enchant.Options[4], Is.EqualTo(15)); Assert.That(it.Enchant.OptionRates[4], Is.EqualTo(0.75f));
        Assert.That(it.LimitBreak.Level, Is.EqualTo(12));
        Assert.That(it.LimitBreak.Stat[3], Is.EqualTo(8)); Assert.That(it.LimitBreak.StatRate[3], Is.EqualTo(0.5f));
        Assert.That(it.LimitBreak.Special[4], Is.EqualTo(12)); Assert.That(it.LimitBreak.SpecialRate[4], Is.EqualTo(0.375f));
    }

    static void WriteBadge(ByteWriter w, byte type, int id, Action<ByteWriter> block) {
        w.WriteByte(type); w.WriteInt(id); w.WriteLong(9); w.WriteInt(1);     // upstream WriteBadge: BadgeType, id, uid, rarity
        WriteBody(w, extra: x => { x.WriteByte(1); x.WriteByte(type); x.WriteUnicodeString(id.ToString()); block?.Invoke(x); });
    }

    [Test]
    public void BadgeTransparencyBlockIsTenBools() {
        var w = new ByteWriter();
        WriteBadge(w, 1, 70000001, x => { for (int i = 0; i < 10; i++) x.WriteBool(i % 2 == 0); });
        var r = R(w);
        var it = ItemReader.ReadBadge(r, Gms);
        r.ExpectEnd("badge");
        Assert.That(it.IsBadge); Assert.That(it.BadgeSlot, Is.EqualTo(1)); Assert.That(it.Id, Is.EqualTo(70000001));
        Assert.That(it.GemType, Is.EqualTo(1)); Assert.That(it.GemId, Is.EqualTo(70000001));
    }

    [Test]
    public void BadgePetSkinBlockIsTheSkinId() {
        var w = new ByteWriter();
        WriteBadge(w, 11, 70000002, x => x.WriteInt(4321));
        var r = R(w);
        var it = ItemReader.ReadBadge(r, Gms);
        r.ExpectEnd("badge");
        Assert.That(it.GemType, Is.EqualTo(11)); Assert.That(it.GemPetId, Is.EqualTo(4321));
    }

    [Test]
    public void BadgeOfAnotherTypeHasNoBlockBody() {
        var w = new ByteWriter();
        WriteBadge(w, 2, 70000003, null);
        var r = R(w);
        ItemReader.ReadBadge(r, Gms);
        r.ExpectEnd("badge");
    }

    // upstream WriteCharacter, all ints and longs zero but the identifying ones
    static void WriteCharacter(ByteWriter w) {
        w.WriteLong(1); w.WriteLong(2); w.WriteUnicodeString("Hero"); w.WriteByte(1); w.WriteByte(1); w.WriteLong(0); w.WriteInt(0);
        w.WriteInt(02000001); w.WriteInt(1); w.WriteInt(0); w.WriteShort(50); w.WriteShort(3);
        w.WriteInt(10); w.WriteInt(10); w.WriteInt(0); w.WriteInt(0); w.WriteShort(0); w.WriteLong(0); w.WriteLong(0); w.WriteLong(0);
        w.WriteInt(2000062); w.WriteFloat(1); w.WriteFloat(2); w.WriteFloat(3); w.WriteInt(0);
        w.WriteInt(0); w.WriteInt(0); w.WriteLong(1700000000);                // SkinColor (2 x rgba), CreationTime
        w.WriteInt(0); w.WriteInt(0); w.WriteInt(0);                          // AchievementInfo
        w.WriteLong(0); w.WriteUnicodeString("G"); w.WriteUnicodeString("motto"); w.WriteUnicodeString("pic");
        w.WriteByte(0); w.WriteByte(0);
        for (int i = 0; i < 12; i++) w.WriteInt(0);                           // Mastery
        w.WriteUnicodeString("plat"); w.WriteLong(0); w.WriteLong(0); w.WriteLong(0);
        w.WriteInt(0); w.WriteByte(0); w.WriteBool(false); w.WriteLong(0); w.WriteInt(0); w.WriteInt(0); w.WriteLong(1700000001);
        w.WriteInt(5); w.WriteLong(1700000002); w.WriteInt(0); w.WriteInt(0); w.WriteShort(0); w.WriteLong(0);
    }

    static ByteWriter ListFrame(int equips, int badges) {
        var w = new ByteWriter();
        w.WriteByte(0); w.WriteByte(1);                                       // Command.List, one character
        WriteCharacter(w); w.WriteUnicodeString("pic"); w.WriteLong(0);
        w.WriteByte((byte) equips);
        for (int i = 0; i < equips; i++) WriteEquip(w, 10000 + i, 50 + i, i == 0 ? "HR" : "CL", 1);
        w.WriteByte((byte) badges);
        for (int i = 0; i < badges; i++) WriteBadge(w, 1, 70000000 + i, x => { for (int j = 0; j < 10; j++) x.WriteBool(false); });
        w.WriteByte(0);                                                       // Outfit2
        return w;
    }

    [Test]
    public void CharacterListEntryReadsEquipsBadgesAndTheOutfit2Byte() {
        var r = R(ListFrame(2, 1));
        var m = CharacterListMessage.Read(r, "KR", Gms);
        Assert.That(m.Command, Is.EqualTo(CharacterListCommand.List));
        var e = m.Entries.Single();
        Assert.That(e.User.Name, Is.EqualTo("Hero")); Assert.That(e.User.Level, Is.EqualTo(50)); Assert.That(e.User.PrestigeLevel, Is.EqualTo(5));
        Assert.That(e.User.PlatformName, Is.EqualTo("plat")); Assert.That(e.Picture, Is.EqualTo("pic"));
        Assert.That(e.Equips.Select(x => x.Slot), Is.EqualTo(new[] { EquipSlot.HR, EquipSlot.CL }));
        Assert.That(e.Equips[0].Appearance, Has.Length.EqualTo(14));
        Assert.That(e.Badges, Has.Count.EqualTo(1)); Assert.That(e.HasSkinTab, Is.False);
    }

    [Test]
    public void AppendEntryAndTheSmallCommandsAreUnchanged() {
        var w = new ByteWriter();
        w.WriteByte(1); WriteCharacter(w); w.WriteUnicodeString("pic"); w.WriteLong(0); w.WriteByte(0); w.WriteByte(0); w.WriteByte(0);
        Assert.That(CharacterListMessage.Read(R(w), "KR", Gms).Entries, Has.Count.EqualTo(1));
        var s = new ByteWriter(); s.WriteByte(3);
        Assert.That(CharacterListMessage.Read(R(s), "KR", Gms).Command, Is.EqualTo(CharacterListCommand.StartList));
        var f = new ByteWriter(); f.WriteByte(4); f.WriteBool(true);
        Assert.That(CharacterListMessage.Read(R(f), "KR", Gms).CheckNames, Is.True);
        var d = new ByteWriter(); d.WriteByte(2); d.WriteInt(0); d.WriteLong(7);
        Assert.That(CharacterListMessage.Read(R(d), "KR", Gms).CharacterId, Is.EqualTo(7));
        var b = new ByteWriter(); b.WriteByte(5); b.WriteLong(9); b.WriteInt(10); b.WriteLong(1700000000);
        Assert.That(CharacterListMessage.Read(R(b), "KR", Gms).DeleteTime, Is.EqualTo(1700000000));
    }

    [Test]
    public void UnknownSlotNameIsRejected() {
        var w = new ByteWriter();
        WriteEquip(w, 1, 2, "ZZ", 1);
        Assert.Throws<InvalidDataException>(() => ItemReader.ReadEquip(R(w), Gms));
    }

    [Test]
    public void KmsProfileUsesTheOriginalByteSlot() {
        var w = new ByteWriter();
        w.WriteInt(1); w.WriteLong(2); w.WriteByte((byte) EquipSlot.CL); w.WriteInt(1);
        ItemReader.WriteEmptyBody(w, 1);
        var it = ItemReader.ReadEquip(R(w), ProtocolProfile.Kms2525);
        Assert.That(it.Slot, Is.EqualTo(EquipSlot.CL));
        var old = ItemReader.ReadEquip(R(w));
        Assert.That(old.Slot, Is.EqualTo(it.Slot)); Assert.That(old.Amount, Is.EqualTo(it.Amount));
    }
}
