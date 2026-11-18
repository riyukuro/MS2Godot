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

    // Command 4 (remove) and 5 (update, flag driven): CSceneStateGame::FUN_14172f500
    public int UpdateFlags; public byte UpdateKind; public Vector3? UpdatePosition; public short UpdateLevel;
    public short UpdateJobCode; public int UpdateJob; public string UpdateMotto; public int UpdateGearScore; public short UpdateState;

    public static ProxyGameObj Read(ByteReader r) {
        var p = new ProxyGameObj { Command = r.ReadByte() };
        if (p.Command == 4) { p.ObjectId = r.ReadInt(); r.ExpectEnd("ProxyGameObj.Remove"); return p; }
        if (p.Command == 5) {
            p.ObjectId = r.ReadInt();
            int f = p.UpdateFlags = r.ReadByte();
            if ((f & 1) != 0) p.UpdateKind = r.ReadByte();
            if ((f & 2) != 0) p.UpdatePosition = r.ReadVec3();
            if ((f & 4) != 0) p.UpdateLevel = r.ReadShort();
            if ((f & 8) != 0) { p.UpdateJobCode = r.ReadShort(); p.UpdateJob = r.ReadInt(); }
            if ((f & 0x10) != 0) p.UpdateMotto = r.ReadUnicodeString();
            if ((f & 0x20) != 0) p.UpdateGearScore = r.ReadInt();
            if ((f & 0x40) != 0) p.UpdateState = r.ReadShort();
            r.ExpectEnd("ProxyGameObj.Update"); return p;
        }
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

/// <summary>RideOnAction (ReadRideOnAction, commonfiles/rideonaction.cpp): u8 type, i32, i32, bool; type 1 adds i32 item id, i64 uid, a ugc look; type 2 adds i32, i16.</summary>
public sealed record RideOnAction(byte Type, int RideId, int ObjectId, bool Flag, int ItemId, long ItemUid, int Extra, short ExtraShort) {
    public static RideOnAction Read(ByteReader r) {
        byte type = r.ReadByte();
        if (type > 3) throw new PacketLayoutException($"ride action type {type}");
        int rideId = r.ReadInt(), obj = r.ReadInt(); bool flag = r.ReadBool();
        int itemId = 0; long uid = 0; int extra = 0; short es = 0;
        if (type == 1) { itemId = r.ReadInt(); uid = r.ReadLong(); ItemReader.ReadUgcLook(r); }
        else if (type == 2) { extra = r.ReadInt(); es = r.ReadShort(); }
        return new RideOnAction(type, rideId, obj, flag, itemId, uid, extra, es);
    }
}

/// <summary>One buff in a FieldAddUser list or an add-buff packet (AdditionalCli::FUN_14096be10): owner header (3 i32), state (i32, i32 end tick?, i32 skill id, i16 level, i32 overlap count, u8), i64 shield value.</summary>
public sealed record Buff(int OwnerObjectId, int BuffObjectId, int CasterObjectId, int StartTick, int EndTick, int SkillId, short Level, int OverlapCount, byte Enabled, long Shield) {
    public static Buff Read(ByteReader r) => new(r.ReadInt(), r.ReadInt(), r.ReadInt(), r.ReadInt(), r.ReadInt(), r.ReadInt(), r.ReadShort(), r.ReadInt(), r.ReadByte(), r.ReadLong());
}

/// <summary>
/// Inbound 0x17, decoded to the last byte (client reader: CSceneStateGame::OnAddUser, scenestategame.cpp).
/// </summary>
public sealed class FieldAddUser {
    public int ObjectId;
    public UserBlock User;
    public int SkillRank, SkillJobCode;
    public List<(int Id, int Level, bool Notify, bool Learned)> Skills = new();
    /// <summary>The whole skill book block (rank, job group, skills with their extra flag).</summary>
    public SkillBook Book;
    public Vector3 Position, Rotation;
    /// <summary>The player stats block: 3 sets (total, base, current) of health (i64), attack speed, move speed, mount speed, jump height.</summary>
    public (long Health, int AttackSpeed, int MoveSpeed, int MountSpeed, int JumpHeight)[] Stats = new (long, int, int, int, int)[3];
    public bool InBattle;
    public RideOnAction Ride; public (int Key, byte Value)[] Passengers = Array.Empty<(int, byte)>();
    public List<Buff> Buffs = new();
    public EquipItem Pet;
    public int HeldCubeItemId; public long HeldCubeId;
    public SkinColor Skin; public string Picture;
    public List<EquipItem> Equips = new(); public List<EquipItem> Outfit2 = new(); public List<EquipItem> Badges = new();
    public long SkinTabStart, SkinTabEnd;
    public short BuffCount;
    public int Title; public short Insignia; public bool InsigniaShown;
    public bool HasPet; public long PremiumTime;
    /// <summary>The four values after the ride block (CSceneStateGame, scenestategame.cpp:11217-11224): i32 PC::FUN_14165f090
    /// (m_n1038; pc.cpp:853 passes it to FUN_14165f1f0 when nothing is held), then the architect score: i64 the month it counts in (m_n1140),
    /// i32 that month's count (m_n1148), i32 the score (m_n114c).</summary>
    public int HeldObject; public long HomeCommendMonth; public int HomeCommendMonthly, HomeCommendPoint;

    public static FieldAddUser Read(ByteReader r, string locale = "KR") {
        var f = new FieldAddUser { ObjectId = r.ReadInt(), User = UserBlock.Read(r, locale) };
        f.Book = SkillBook.Read(r);     // the job system block JobSystem::vfunc_7 reads (the Job packet's layout)
        f.SkillRank = f.Book.Rank; f.SkillJobCode = f.Book.JobGroup;
        foreach (var sk in f.Book.Skills) f.Skills.Add((sk.SkillId, sk.Level, sk.Changed, sk.Learned));
        f.Position = r.ReadVec3(); f.Rotation = r.ReadVec3();
        r.ReadByte();
        if (r.ReadByte() != 0x23) throw new PacketLayoutException("player stats count");
        for (int i = 0; i < 3; i++) f.Stats[i] = (r.ReadLong(), r.ReadInt(), r.ReadInt(), r.ReadInt(), r.ReadInt());
        f.InBattle = r.ReadBool();
        r.ReadByte();
        // HeldCube: i32 item id, i64 id, i64 expiry, bool template (+ ugc look)
        f.HeldCubeItemId = r.ReadInt(); f.HeldCubeId = r.ReadLong(); r.ReadLong();
        if (r.ReadBool()) ItemReader.ReadUgcLook(r);
        r.ReadInt();
        f.Skin = new SkinColor(r.ReadRgba(), r.ReadRgba());
        f.Picture = r.ReadUnicodeString();
        if (r.ReadBool()) {
            f.Ride = RideOnAction.Read(r);
            f.Passengers = new (int, byte)[r.ReadByte()];
            for (int i = 0; i < f.Passengers.Length; i++) f.Passengers[i] = (r.ReadInt(), r.ReadByte());
        }
        f.HeldObject = r.ReadInt(); f.HomeCommendMonth = r.ReadLong(); f.HomeCommendMonthly = r.ReadInt(); f.HomeCommendPoint = r.ReadInt();

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
        for (int i = 0; i < (ushort) f.BuffCount; i++) f.Buffs.Add(Buff.Read(r));
        r.ReadInt(); r.ReadInt(); r.ReadByte();
        r.ReadInt(); r.ReadByte(); r.ReadByte();
        f.Title = r.ReadInt(); f.Insignia = r.ReadShort(); f.InsigniaShown = r.ReadBool();
        r.ReadInt();
        f.HasPet = r.ReadBool();
        if (f.HasPet) f.Pet = ItemReader.ReadPetItem(r);
        f.PremiumTime = r.ReadLong(); r.ReadInt(); r.ReadByte(); r.ReadShort();
        r.ExpectEnd("FieldAddUser");
        return f;
    }
}
