// Layouts adapted from the Maple2 server (AGPL-3.0, github.com/MS2Community/Maple2) and checked against the restored client and recorded KMS2 traffic.
using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>Outbound 0x09 CharacterManagement. Command 0 = select (then i64 character id). Create/delete are not typed here.</summary>
public sealed record SelectCharacter(long CharacterId) : IOutPacket {
    public Outbound Op => Outbound.CharacterManagement;
    public void Write(ByteWriter w) { w.WriteByte(0); w.WriteLong(CharacterId); }
    public static SelectCharacter Read(ByteReader r) {
        if (r.ReadByte() != 0) throw new PacketLayoutException("not a select command");
        var p = new SelectCharacter(r.ReadLong()); r.ExpectEnd("SelectCharacter"); return p;
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
    public int HpMax, HpCurrent;
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

    public static CharacterEntry Read(ByteReader r, string locale = "KR") {
        var e = new CharacterEntry { User = UserBlock.Read(r, locale), Picture = r.ReadUnicodeString(), DeleteTime = r.ReadLong() };
        int n = r.ReadByte();
        for (int i = 0; i < n; i++) e.Equips.Add(ItemReader.ReadEquip(r));
        n = r.ReadByte();
        for (int i = 0; i < n; i++) e.Badges.Add(ItemReader.ReadBadge(r));
        e.HasSkinTab = r.ReadBool();
        if (e.HasSkinTab) {
            e.SkinTabStart = r.ReadLong(); e.SkinTabEnd = r.ReadLong();
            n = r.ReadByte();
            for (int i = 0; i < n; i++) e.Outfit2.Add(ItemReader.ReadEquip(r));
        }
        return e;
    }
}

public enum CharacterListCommand : byte { List = 0, AppendEntry = 1, DeleteEntry = 2, StartList = 3, EndList = 4, BeginDelete = 5, CancelDelete = 6, NameChanged = 7 }

/// <summary>Inbound 0x0C: u8 command, then per command. Entries are filled for List and AppendEntry; other commands keep their fields in Raw.</summary>
public sealed class CharacterListMessage {
    public CharacterListCommand Command;
    public List<CharacterEntry> Entries = new();
    public bool CheckNames; // EndList
    public byte[] Raw;      // DeleteEntry/BeginDelete/CancelDelete/NameChanged: not decoded

    public static CharacterListMessage Read(ByteReader r, string locale = "KR") {
        var m = new CharacterListMessage { Command = (CharacterListCommand) r.ReadByte() };
        switch (m.Command) {
            case CharacterListCommand.List: {
                int n = r.ReadByte();
                for (int i = 0; i < n; i++) m.Entries.Add(CharacterEntry.Read(r, locale));
                break;
            }
            case CharacterListCommand.AppendEntry: m.Entries.Add(CharacterEntry.Read(r, locale)); break;
            case CharacterListCommand.StartList: break;
            case CharacterListCommand.EndList: m.CheckNames = r.ReadBool(); break;
            default: m.Raw = r.ReadBytes(r.Available); return m;
        }
        r.ExpectEnd($"CharacterList {m.Command}");
        return m;
    }
}
