// Layouts adapted from the Maple2 server (AGPL-3.0, github.com/MS2Community/Maple2) and checked against the restored client and recorded KMS2 traffic.
using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>Inbound 0x16: u8 error, i32 map, u8 field type, u8 instance type, i32 instance id, i32 dungeon id, vec3 position, vec3 rotation, i32 field key.</summary>
public sealed record RequestFieldEnter(byte Error, int MapId, byte FieldType, byte InstanceType, int InstanceId, int DungeonId, Vector3 Position, Vector3 Rotation, int FieldKey) {
    public static RequestFieldEnter Read(ByteReader r) {
        byte err = r.ReadByte();
        if (err != 0) return new RequestFieldEnter(err, 0, 0, 0, 0, 0, default, default, 0);
        var p = new RequestFieldEnter(0, r.ReadInt(), r.ReadByte(), r.ReadByte(), r.ReadInt(), r.ReadInt(), r.ReadVec3(), r.ReadVec3(), r.ReadInt());
        r.ExpectEnd("RequestFieldEnter"); return p;
    }
}

/// <summary>Outbound 0x10: the field key, answered once the map has loaded.</summary>
public sealed record ResponseFieldEnter(int FieldKey) : IOutPacket {
    public Outbound Op => Outbound.ResponseFieldEnter;
    public void Write(ByteWriter w) => w.WriteInt(FieldKey);
    public static ResponseFieldEnter Read(ByteReader r) { var p = new ResponseFieldEnter(r.ReadInt()); r.ExpectEnd("ResponseFieldEnter"); return p; }
}

/// <summary>Inbound 0x18.</summary>
public sealed record FieldRemoveUser(int ObjectId) {
    public static FieldRemoveUser Read(ByteReader r) { var p = new FieldRemoveUser(r.ReadInt()); r.ExpectEnd("FieldRemoveUser"); return p; }
}

/// <summary>Inbound 0x14: the sync number (a byte), also echoed as the first byte of outbound UserSync.</summary>
public sealed record SyncNumber(byte Number) {
    public static SyncNumber Read(ByteReader r) { var p = new SyncNumber(r.ReadByte()); r.ExpectEnd("SyncNumber"); return p; }
}

/// <summary>
/// Inbound 0x15, the first part: object id, character id, channel, exp, rest exp, meso, then the wallet longs.
/// The map unlock lists and urls that follow are read too. Layout from the server writer; the client reader is scenestategame.cpp OnServerEnter.
/// </summary>
public sealed class ServerEnter {
    public int ObjectId; public long CharacterId; public short Channel; public long Exp, RestExp, Meso;
    public long[] Wallet; // 4 meret slots, 1 game meret etc.: 4 + 4... as written: meret, 0, 0, 0, gameMeret, then 9 more
    public string Picture; public byte Unknown;
    public int[] UnlockedMaps, UnlockedTaxis;
    public long ChampionGuildId; public string ChampionGuildName, Url1, Url2, Url3;

    public static ServerEnter Read(ByteReader r) {
        var s = new ServerEnter { ObjectId = r.ReadInt(), CharacterId = r.ReadLong(), Channel = r.ReadShort(), Exp = r.ReadLong(), RestExp = r.ReadLong(), Meso = r.ReadLong() };
        s.Wallet = new long[5 + 9];
        for (int i = 0; i < s.Wallet.Length; i++) s.Wallet[i] = r.ReadLong();
        s.Picture = r.ReadUnicodeString(); s.Unknown = r.ReadByte();
        s.UnlockedMaps = ReadInts(r); s.UnlockedTaxis = ReadInts(r);
        s.ChampionGuildId = r.ReadLong(); s.ChampionGuildName = r.ReadUnicodeString();
        s.Url1 = r.ReadUnicodeString(); s.Url2 = r.ReadUnicodeString(); s.Url3 = r.ReadUnicodeString();
        r.ExpectEnd("ServerEnter"); return s;
    }
    static int[] ReadInts(ByteReader r) { int n = (ushort) r.ReadShort(); var a = new int[n]; for (int i = 0; i < n; i++) a[i] = r.ReadInt(); return a; }
}

/// <summary>Inbound 0x7E sub command 3 (AddPlayer proxy). Other sub commands are returned with only the command set.</summary>
public sealed class ProxyGameObj {
    public byte Command;
    public int ObjectId; public long CharacterId, AccountId; public string Name, Picture, Motto; public bool IsDead;
    public Vector3 Position; public short Level; public short JobCode; public int Job;
    public int HomePlotMapId, HomePlotNumber, HomeApartmentNumber; public string HomeName; public int GearScore; public short State;
    public int AchievementCombat, AchievementAdventure, AchievementLifestyle;

    public static ProxyGameObj Read(ByteReader r) {
        var p = new ProxyGameObj { Command = r.ReadByte() };
        if (p.Command != 3) return p;
        p.ObjectId = r.ReadInt(); p.CharacterId = r.ReadLong(); p.AccountId = r.ReadLong();
        p.Name = r.ReadUnicodeString(); p.Picture = r.ReadUnicodeString(); p.Motto = r.ReadUnicodeString();
        p.IsDead = r.ReadBool(); p.Position = r.ReadVec3(); p.Level = r.ReadShort();
        p.JobCode = r.ReadShort(); p.Job = r.ReadInt();
        p.HomePlotMapId = r.ReadInt(); p.HomePlotNumber = r.ReadInt(); p.HomeApartmentNumber = r.ReadInt(); p.HomeName = r.ReadUnicodeString();
        p.GearScore = r.ReadInt(); p.State = r.ReadShort();
        p.AchievementCombat = r.ReadInt(); p.AchievementAdventure = r.ReadInt(); p.AchievementLifestyle = r.ReadInt();
        r.ExpectEnd("ProxyGameObj.AddPlayer"); return p;
    }
}

/// <summary>
/// Inbound 0x17, decoded through the stats block, then the appearance. Not decoded: buffs (when present) and what follows them.
/// </summary>
public sealed class FieldAddUser {
    public int ObjectId;
    public UserBlock User;
    public int SkillRank, SkillJobCode;
    public List<(int Id, int Level, bool Notify, bool Learned)> Skills = new();
    public Vector3 Position, Rotation;
    /// <summary>The player stats block: 3 sets (total, base, current) of health (i64), attack speed, move speed, mount speed, jump height.</summary>
    public (long Health, int AttackSpeed, int MoveSpeed, int MountSpeed, int JumpHeight)[] Stats = new (long, int, int, int, int)[3];
    public bool InBattle;
    public int HeldCubeItemId; public long HeldCubeId;
    public SkinColor Skin; public string Picture;
    public List<EquipItem> Equips = new(); public List<EquipItem> Outfit2 = new(); public List<EquipItem> Badges = new();
    public long SkinTabStart, SkinTabEnd;
    public short BuffCount;
    /// <summary>The bytes from the buff list on. Decoded only when BuffCount is 0 (Title, Insignia, Pet, premium time follow).</summary>
    public byte[] Tail;
    public int Title; public short Insignia; public bool InsigniaShown;
    public bool HasPet; public long PremiumTime;

    public static FieldAddUser Read(ByteReader r, string locale = "KR") {
        var f = new FieldAddUser { ObjectId = r.ReadInt(), User = UserBlock.Read(r, locale) };
        f.SkillRank = r.ReadInt(); f.SkillJobCode = r.ReadInt();
        int skills = r.ReadByte();
        for (int i = 0; i < skills; i++) { var sk = (r.ReadInt(), r.ReadInt(), r.ReadBool(), r.ReadBool()); r.ReadBool(); f.Skills.Add(sk); }
        f.Position = r.ReadVec3(); f.Rotation = r.ReadVec3();
        r.ReadByte();
        if (r.ReadByte() != 0x23) throw new PacketLayoutException("player stats count");
        for (int i = 0; i < 3; i++) f.Stats[i] = (r.ReadLong(), r.ReadInt(), r.ReadInt(), r.ReadInt(), r.ReadInt());
        f.InBattle = r.ReadBool();
        r.ReadByte();
        // HeldCube: i32 item id, i64 id, i64 expiry, bool template (+ ugc look)
        f.HeldCubeItemId = r.ReadInt(); f.HeldCubeId = r.ReadLong(); r.ReadLong();
        if (r.ReadBool()) throw new PacketLayoutException("held cube with ugc template is not decoded");
        r.ReadInt();
        f.Skin = new SkinColor(r.ReadRgba(), r.ReadRgba());
        f.Picture = r.ReadUnicodeString();
        if (r.ReadBool()) throw new PacketLayoutException("riding state is not decoded");
        r.ReadInt(); r.ReadLong(); r.ReadInt(); r.ReadInt();

        using (var b = r.ReadDeflated()) {
            int n = b.ReadByte();
            for (int i = 0; i < n; i++) f.Equips.Add(ItemReader.ReadEquip(b));
            b.ReadBool();
            f.SkinTabStart = b.ReadLong(); f.SkinTabEnd = b.ReadLong();
            n = b.ReadByte();
            for (int i = 0; i < n; i++) f.Outfit2.Add(ItemReader.ReadEquip(b));
            b.ExpectEnd("FieldAddUser equips");
        }
        using (var b = r.ReadDeflated()) { b.ReadByte(); b.ExpectEnd("FieldAddUser unknown block"); }
        using (var b = r.ReadDeflated()) {
            int n = b.ReadByte();
            for (int i = 0; i < n; i++) f.Badges.Add(ItemReader.ReadBadge(b));
            b.ExpectEnd("FieldAddUser badges");
        }
        f.BuffCount = r.ReadShort();
        if (f.BuffCount != 0) { f.Tail = r.ReadBytes(r.Available); return f; }
        r.ReadInt(); r.ReadInt(); r.ReadByte();
        r.ReadInt(); r.ReadByte(); r.ReadByte();
        f.Title = r.ReadInt(); f.Insignia = r.ReadShort(); f.InsigniaShown = r.ReadBool();
        r.ReadInt();
        f.HasPet = r.ReadBool();
        if (f.HasPet) { f.Tail = r.ReadBytes(r.Available); return f; }
        f.PremiumTime = r.ReadLong(); r.ReadInt(); r.ReadByte(); r.ReadShort();
        r.ExpectEnd("FieldAddUser");
        return f;
    }
}
