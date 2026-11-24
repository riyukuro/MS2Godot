// Layouts adapted from the Maple2 server (AGPL-3.0, github.com/MS2Community/Maple2) and checked against the restored client and recorded KMS2 traffic.
using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>Outbound 0x09 CharacterManagement, command 0 (UIServerListDialog::EnterGame, WriteLoginPing): i64 character id, i16 channel (0 when none was picked).</summary>
public sealed record SelectCharacter(long CharacterId, short Channel = 0) : IOutPacket {
    public Outbound Op => Outbound.CharacterManagement;
    public void Write(ByteWriter w) { w.WriteByte(0); w.WriteLong(CharacterId); w.WriteShort(Channel); }
    public static SelectCharacter Read(ByteReader r) {
        if (r.ReadByte() != 0) throw new PacketLayoutException("not a select command");
        var p = new SelectCharacter(r.ReadLong(), r.ReadShort()); r.ExpectEnd("SelectCharacter"); return p;
    }
}

/// <summary>
/// Outbound 0x09, command 1 (UICharacterCreateDialog::FUN_1418e3f50, FUN_1401f9f20): u8 gender, i16 job group, name,
/// i32 skin color 0, i32 skin color 1 (ARGB), i16 length + the shown equips (u8 count, each: i32 item id, u8, then the
/// item's color/look data, kept raw), i32 elapsed seconds in the dialog.
/// </summary>
public sealed record CreateCharacter(byte Gender, short JobGroup, string Name, int Color0, int Color1, byte[] Items, int Elapsed) : IProfileOutPacket {
    public Outbound Op => Outbound.CharacterManagement;
    public void Write(ByteWriter w) {
        w.WriteByte(1); w.WriteByte(Gender); w.WriteShort(JobGroup); w.WriteUnicodeString(Name); w.WriteInt(Color0); w.WriteInt(Color1);
        w.WriteShort((short) Items.Length); w.WriteBytes(Items); w.WriteInt(Elapsed);
    }
    /// <summary>
    /// The request of a GMS2 server: the same fields, the item block with the slot written as its name (a counted UTF-16 string, "HR") instead of
    /// the slot byte. <see cref="Items"/> stays in the KMS form and is converted here.
    /// </summary>
    public void Write(ByteWriter w, ProtocolProfile profile) {
        if (!profile.IsGms2) { Write(w); return; }
        w.WriteByte(1); w.WriteByte(Gender); w.WriteShort(JobGroup); w.WriteUnicodeString(Name); w.WriteInt(Color0); w.WriteInt(Color1);
        var block = EncodeItemsGms2(DecodeItems(Items));
        w.WriteShort((short) block.Length); w.WriteBytes(block); w.WriteInt(Elapsed);
    }

    /// <summary>The GMS2 item block: u8 count, per item i32 id, the slot name, the colours and the appearance as in <see cref="EncodeItems"/>.</summary>
    public static byte[] EncodeItemsGms2(IReadOnlyList<CreateItem> list) {
        var w = new ByteWriter();
        w.WriteByte((byte) list.Count);
        foreach (var it in list) {
            w.WriteInt(it.Id); w.WriteUnicodeString(it.Slot.ToString());
            foreach (var c in new[] { it.Color.Primary, it.Color.Secondary, it.Color.Tertiary }) { w.WriteByte(c.B); w.WriteByte(c.G); w.WriteByte(c.R); w.WriteByte(c.A); }
            w.WriteInt(it.Color.Index); w.WriteInt(it.Color.PaletteId);
            foreach (var f in it.Appearance) w.WriteFloat(f);
        }
        return w.ToArray();
    }

    /// <summary>Reads back a GMS2 item block.</summary>
    public static List<CreateItem> DecodeItemsGms2(byte[] block) {
        var r = new ByteReader(block, 0);
        var list = new List<CreateItem>();
        int n = r.ReadByte();
        for (int i = 0; i < n; i++) {
            int id = r.ReadInt();
            if (!Enum.TryParse<EquipSlot>(r.ReadUnicodeString(), out var slot)) throw new PacketLayoutException("unknown equip slot name");
            var color = r.ReadEquipColor();
            var app = new float[CreateItem.AppearanceLength(slot)];
            for (int k = 0; k < app.Length; k++) app[k] = r.ReadFloat();
            list.Add(new CreateItem(id, slot, color, app));
        }
        r.ExpectEnd("CreateCharacter items");
        return list;
    }

    /// <summary>The item block: u8 count, then each <see cref="CreateItem"/>.</summary>
    public static byte[] EncodeItems(IEnumerable<CreateItem> items) {
        var list = items.ToList();
        var w = new ByteWriter();
        w.WriteByte((byte) list.Count);
        foreach (var it in list) {
            int n = CreateItem.AppearanceLength(it.Slot);
            if ((it.Appearance?.Length ?? 0) != n) throw new ArgumentException($"item {it.Id}: {it.Slot} takes {n} appearance floats");
            w.WriteInt(it.Id); w.WriteByte((byte) it.Slot);
            foreach (var c in new[] { it.Color.Primary, it.Color.Secondary, it.Color.Tertiary }) { w.WriteByte(c.B); w.WriteByte(c.G); w.WriteByte(c.R); w.WriteByte(c.A); }
            w.WriteInt(it.Color.Index); w.WriteInt(it.Color.PaletteId);
            foreach (var f in it.Appearance) w.WriteFloat(f);
        }
        return w.ToArray();
    }

    /// <summary>Reads back an item block written by <see cref="EncodeItems"/>.</summary>
    public static List<CreateItem> DecodeItems(byte[] block) {
        var r = new ByteReader(block, 0);
        var list = new List<CreateItem>();
        int n = r.ReadByte();
        for (int i = 0; i < n; i++) {
            int id = r.ReadInt(); var slot = (EquipSlot) r.ReadByte(); var color = r.ReadEquipColor();
            var app = new float[CreateItem.AppearanceLength(slot)];
            for (int k = 0; k < app.Length; k++) app[k] = r.ReadFloat();
            list.Add(new CreateItem(id, slot, color, app));
        }
        r.ExpectEnd("CreateCharacter items");
        return list;
    }

    public static CreateCharacter Read(ByteReader r) {
        if (r.ReadByte() != 1) throw new PacketLayoutException("not a create command");
        var p = new CreateCharacter(r.ReadByte(), r.ReadShort(), r.ReadUnicodeString(), r.ReadInt(), r.ReadInt(), null, 0);
        p = p with { Items = r.ReadBytes(r.ReadUShort()) };
        p = p with { Elapsed = r.ReadInt() };
        r.ExpectEnd("CreateCharacter"); return p;
    }
}

/// <summary>
/// One item of the create request's item block: i32 item id, u8 equip slot (CItem::FUN_140526c30), then the item's
/// appearance as its proxy encodes it: the 20 byte equip colour, then 14 floats for hair (back length, back position
/// 1 and 2, front length, front position 1 and 2), 4 for a face decal, 13 for a cap; nothing for other slots.
/// </summary>
public sealed record CreateItem(int Id, EquipSlot Slot, EquipColor Color, float[] Appearance) {
    public static int AppearanceLength(EquipSlot slot) => slot switch { EquipSlot.HR => 14, EquipSlot.FD => 4, EquipSlot.CP => 13, _ => 0 };
    public static float[] DefaultAppearance(EquipSlot slot) => new float[AppearanceLength(slot)];
}

public enum CharacterIdCommand : byte { BeginDelete = 2, CancelDelete = 3, ConfirmDelete = 4 }

/// <summary>Outbound 0x09, commands 2 (start the delete wait), 3 (cancel it), 4 (delete for good): i64 character id.</summary>
public sealed record ManageCharacter(CharacterIdCommand Command, long CharacterId) : IOutPacket {
    public Outbound Op => Outbound.CharacterManagement;
    public void Write(ByteWriter w) { w.WriteByte((byte) Command); w.WriteLong(CharacterId); }
    public static ManageCharacter Read(ByteReader r) {
        byte c = r.ReadByte();
        if (c < 2 || c > 4) throw new PacketLayoutException($"command {c}");
        var p = new ManageCharacter((CharacterIdCommand) c, r.ReadLong()); r.ExpectEnd("ManageCharacter"); return p;
    }
}

/// <summary>Outbound 0x09, command 5 (UICharacterListDialog::SendCharacterManagementMode5): i64 character id, new name.</summary>
public sealed record RenameCharacter(long CharacterId, string Name) : IOutPacket {
    public Outbound Op => Outbound.CharacterManagement;
    public void Write(ByteWriter w) { w.WriteByte(5); w.WriteLong(CharacterId); w.WriteUnicodeString(Name); }
    public static RenameCharacter Read(ByteReader r) {
        if (r.ReadByte() != 5) throw new PacketLayoutException("not a rename command");
        var p = new RenameCharacter(r.ReadLong(), r.ReadUnicodeString()); r.ExpectEnd("RenameCharacter"); return p;
    }
}

public readonly record struct SkinColor(Rgba Primary, Rgba Secondary);

/// <summary>
/// The user block shared by the character list entries and FieldAddUser (CCharacterInfo::Read / CSceneStateGame::OnAddUser).
/// The two senders differ in a few slots: the list writes 0 where FieldAddUser writes the account id (field 6),
/// and MapA/MapB are (map, instance map) in the list but (return map, map) in FieldAddUser; they are kept as
/// the two ints the wire has.
/// </summary>
public sealed class UserBlock {
    public long AccountId, CharacterId; public string Name;
    public byte Gender, GmGrade;
    public int MapA, MapB, RoomId;
    public short Level, Channel;
    public int JobCode, Job;
    /// <summary>Read as i32 in the character list; the own Stat (i64 health pair) then fills them, so they are long.</summary>
    public long HpMax, HpCurrent;
    public short DeathCount;
    public long StorageCooldown, DoctorCooldown;
    public int ReturnMap; public Vector3 ReturnPosition;
    public int GearScore;
    public SkinColor Skin;
    public long CreationTime;
    public int AchievementCombat, AchievementAdventure, AchievementLifestyle;
    public long GuildId; public string GuildName, Motto, Picture;
    public long[] ClubIds;
    public byte PcBangGrade;
    public int[] Mastery; // 12 ints: a leading 0, then fishing, instrument, mining, foraging, ranching, farming, smithing, handicrafts, alchemy, cooking, pet taming
    public string PlatformName; public long PlatformValue; // KR/JP platform part; see 'locale'
    public byte MentorRole; public long Birthday; public int SuperChatId;
    public int PetFusionCount; public long PetFusionTime;
    public int PrestigeLevel; public long LastModified;
    public long[] LongListA, LongListB;
    public short SurvivalLevel;

    /// <param name="locale">Platform part after the mastery block: KR/JP (and unknown) = string + long; NA = string + 3 longs; CN = i32, i64, u8, i32.</param>
    public static UserBlock Read(ByteReader r, string locale = "KR") {
        var u = new UserBlock();
        u.AccountId = r.ReadLong(); u.CharacterId = r.ReadLong(); u.Name = r.ReadUnicodeString();
        u.Gender = r.ReadByte(); u.GmGrade = r.ReadByte();
        r.ReadLong(); r.ReadInt();
        u.MapA = r.ReadInt(); u.MapB = r.ReadInt(); u.RoomId = r.ReadInt();
        u.Level = r.ReadShort(); u.Channel = r.ReadShort();
        u.JobCode = r.ReadInt(); u.Job = r.ReadInt();
        u.HpMax = r.ReadInt(); u.HpCurrent = r.ReadInt();
        u.DeathCount = r.ReadShort();
        r.ReadLong();
        u.StorageCooldown = r.ReadLong(); u.DoctorCooldown = r.ReadLong();
        u.ReturnMap = r.ReadInt(); u.ReturnPosition = r.ReadVec3();
        u.GearScore = r.ReadInt();
        u.Skin = new SkinColor(r.ReadRgba(), r.ReadRgba());
        u.CreationTime = r.ReadLong();
        u.AchievementCombat = r.ReadInt(); u.AchievementAdventure = r.ReadInt(); u.AchievementLifestyle = r.ReadInt();
        u.GuildId = r.ReadLong(); u.GuildName = r.ReadUnicodeString(); u.Motto = r.ReadUnicodeString(); u.Picture = r.ReadUnicodeString();
        int clubs = r.ReadByte();
        u.ClubIds = new long[clubs];
        for (int i = 0; i < clubs; i++) {
            if (r.ReadBool()) { u.ClubIds[i] = r.ReadLong(); r.ReadUnicodeString(); }
        }
        u.PcBangGrade = r.ReadByte();
        u.Mastery = new int[12];
        for (int i = 0; i < 12; i++) u.Mastery[i] = r.ReadInt();
        switch (locale) {
            case "NA": u.PlatformName = r.ReadUnicodeString(); u.PlatformValue = r.ReadLong(); r.ReadLong(); r.ReadLong(); break;
            case "CN": r.ReadInt(); u.PlatformValue = r.ReadLong(); r.ReadByte(); r.ReadInt(); break;
            default: u.PlatformName = r.ReadUnicodeString(); u.PlatformValue = r.ReadLong(); break;
        }
        u.LongListA = ReadLongs(r);
        u.MentorRole = r.ReadByte();
        r.ReadBool();
        u.Birthday = r.ReadLong();
        u.SuperChatId = r.ReadInt();
        u.PetFusionCount = r.ReadInt(); u.PetFusionTime = r.ReadLong();
        u.PrestigeLevel = r.ReadInt();
        u.LastModified = r.ReadLong();
        u.LongListB = ReadLongs(r);
        // second count list (charObj+0x1E8 region)
        int c2 = r.ReadInt();
        for (int i = 0; i < c2; i++) r.ReadLong();
        u.SurvivalLevel = r.ReadShort();
        r.ReadLong();
        return u;
    }

    static long[] ReadLongs(ByteReader r) {
        int n = r.ReadInt();
        if (n < 0 || n > r.Available / 8) throw new PacketLayoutException($"long list count {n}");
        var l = new long[n];
        for (int i = 0; i < n; i++) l[i] = r.ReadLong();
        return l;
    }
}

public sealed class CharacterEntry {
    public UserBlock User;
    public string Picture;
    public long DeleteTime;
    public List<EquipItem> Equips = new();
    public List<EquipItem> Badges = new();
    public bool HasSkinTab; public long SkinTabStart, SkinTabEnd;
    public List<EquipItem> Outfit2 = new();

    public static CharacterEntry Read(ByteReader r, string locale = "KR") => Read(r, locale, ProtocolProfile.Kms2525);

    /// <summary>The entry for a profile. GMS2 (upstream CharacterListPacket.WriteEntry): the user block with locale "NA", the equips and badges in the
    /// GMS2 item layout, and the one Outfit2 byte (0), which reads as no skin tab.</summary>
    public static CharacterEntry Read(ByteReader r, string locale, ProtocolProfile profile) {
        var e = new CharacterEntry { User = UserBlock.Read(r, profile.IsGms2 ? "NA" : locale), Picture = r.ReadUnicodeString(), DeleteTime = r.ReadLong() };
        int n = r.ReadByte();
        for (int i = 0; i < n; i++) e.Equips.Add(ItemReader.ReadEquip(r, profile));
        n = r.ReadByte();
        for (int i = 0; i < n; i++) e.Badges.Add(ItemReader.ReadBadge(r, profile));
        e.HasSkinTab = r.ReadBool();
        if (e.HasSkinTab) {
            e.SkinTabStart = r.ReadLong(); e.SkinTabEnd = r.ReadLong();
            n = r.ReadByte();
            for (int i = 0; i < n; i++) e.Outfit2.Add(ItemReader.ReadEquip(r, profile));
        }
        return e;
    }
}

/// <summary>Inbound 0x46, the create refusal (UICharacterCreateDialog::OnCreateCharacterAnswer, uicameraeditdialog.cpp:1608; server CharacterListPacket.CreateError):
/// u8 error kind, unicode message. The client picks its text by kind (1, 6, 8, 9, 10, 11, 12 ...). A create that works sends no 0x46: the server answers
/// with CharMaxCount (0xB8, networkservice.cpp:1069) and a CharacterList AppendEntry (LoginSession.CreateCharacter).</summary>
public sealed record CharacterCreateAnswer(byte Kind, string Message) {
    public static CharacterCreateAnswer Read(ByteReader r) { var p = new CharacterCreateAnswer(r.ReadByte(), r.ReadUnicodeString()); r.ExpectEnd("CharacterCreateAnswer"); return p; }
}

public enum CharacterListCommand : byte { List = 0, AppendEntry = 1, DeleteEntry = 2, StartList = 3, EndList = 4, BeginDelete = 5, CancelDelete = 6, NameChanged = 7 }

/// <summary>Inbound 0x0C: u8 command, then per command. Entries are filled for List and AppendEntry; other commands keep their fields in Raw.</summary>
public sealed class CharacterListMessage {
    public CharacterListCommand Command;
    public List<CharacterEntry> Entries = new();
    public bool CheckNames; // EndList
    public byte[] Raw;      // a command not decoded
    /// <summary>NameChanged (UICharacterListDialog::vfunc_79 case 0xc type 7, uicharacterlistdialog.cpp:605-611): i32 code
    /// (in <see cref="Error"/>; 1 the name is taken), i64 character id, unicode name.</summary>
    public string Name;
    // DeleteEntry / BeginDelete / CancelDelete (UICharacterListDialog::OnCharacterListPacket, uicharacterlistdialog.cpp:894-1018; server CharacterListPacket.cs:48-71).
    // DeleteEntry: i32 error, i64 id. BeginDelete: i64 id, i32 error, i64 deleteTime (unix seconds; error 10 = too early, the time is the next allowed date). CancelDelete: i64 id, i32 error.
    public long CharacterId;
    public int Error;       // CharacterDeleteError, 0 = ok
    public long DeleteTime;

    public static CharacterListMessage Read(ByteReader r, string locale = "KR") => Read(r, locale, ProtocolProfile.Kms2525);

    public static CharacterListMessage Read(ByteReader r, string locale, ProtocolProfile profile) {
        var m = new CharacterListMessage { Command = (CharacterListCommand) r.ReadByte() };
        switch (m.Command) {
            case CharacterListCommand.List: {
                int n = r.ReadByte();
                for (int i = 0; i < n; i++) m.Entries.Add(CharacterEntry.Read(r, locale, profile));
                break;
            }
            case CharacterListCommand.AppendEntry: m.Entries.Add(CharacterEntry.Read(r, locale, profile)); break;
            case CharacterListCommand.StartList: break;
            case CharacterListCommand.EndList: m.CheckNames = r.ReadBool(); break;
            case CharacterListCommand.DeleteEntry: m.Error = r.ReadInt(); m.CharacterId = r.ReadLong(); break;
            case CharacterListCommand.BeginDelete: m.CharacterId = r.ReadLong(); m.Error = r.ReadInt(); m.DeleteTime = r.ReadLong(); break;
            case CharacterListCommand.CancelDelete: m.CharacterId = r.ReadLong(); m.Error = r.ReadInt(); break;
            case CharacterListCommand.NameChanged: m.Error = r.ReadInt(); m.CharacterId = r.ReadLong(); m.Name = r.ReadUnicodeString(); break;
            default: m.Raw = r.ReadBytes(r.Available); return m;
        }
        r.ExpectEnd($"CharacterList {m.Command}");
        return m;
    }
}
