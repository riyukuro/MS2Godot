// Layouts from the restored client's readers (CSceneStateGame::OnSkillDamage and its modes, OnStat / CNpc::vfunc_90), checked against recorded KMS2 traffic.
using System.Numerics;
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>One hit: a flag byte (hit kind; 0 normal) and the damage.</summary>
public readonly record struct Hit(byte Flag, long Damage);
public sealed record HitTarget(int TargetId, Hit[] Hits, Vector3S Origin, Vector3 Direction);

/// <summary>
/// Inbound 0x3D (OnSkillDamage): u8 mode.
/// 0 projectile records, 1 hits of a cast (cast uids, owner, skill, level, motion, attack point, position, direction, then per target id + hits),
/// 3 damage over time, 5 region skill, 6 splash. Modes 4, 7 and 8 are not decoded (Raw).
/// </summary>
public sealed class SkillDamage {
    public byte Mode;
    public long CasterUid, CastUid; public int OwnerId, SkillId; public short Level; public byte Motion, AttackPoint;
    public Vector3S Position, DirectionShort;
    public List<HitTarget> Targets = new();
    // mode 3 (OnDotDamage, scenestategame.cpp:5663): the attacker (OwnerId, the record's +0x2c), the object hit (TargetId, +0x28)
    // and the buff on it (BuffId, CDotDamageRecord m_nEffectLevel: vfunc_1 looks it up in the hit object's buff list)
    public int TargetId, BuffId; public Hit DotHit;
    // mode 5
    public int RegionId;
    // mode 0
    public int ProjectileCount;
    public byte[] Raw;

    public static SkillDamage Read(ByteReader r) => Read(r, ProtocolProfile.Kms2525);

    /// <summary>GMS2 (upstream SkillDamagePacket): mode 0 has an i32 server tick after the bool, mode 3 ends in an i32 hp amount instead of an i64.</summary>
    public static SkillDamage Read(ByteReader r, ProtocolProfile profile) {
        bool gms = profile.IsGms2;
        var d = new SkillDamage { Mode = r.ReadByte() };
        switch (d.Mode) {
            case 0: {
                d.CastUid = r.ReadLong(); d.OwnerId = r.ReadInt(); d.SkillId = r.ReadInt(); d.Level = r.ReadShort(); r.ReadByte(); r.ReadByte();
                d.Position = r.ReadVec3S(); r.ReadVec3(); r.ReadByte(); if (gms) r.ReadInt();
                d.ProjectileCount = r.ReadByte();
                for (int i = 0; i < d.ProjectileCount; i++) { r.ReadLong(); r.ReadLong(); r.ReadInt(); r.ReadByte(); r.ReadByte(); }
                break;
            }
            case 1: {
                d.CasterUid = r.ReadLong(); d.CastUid = r.ReadLong(); d.OwnerId = r.ReadInt(); d.SkillId = r.ReadInt(); d.Level = r.ReadShort();
                d.Motion = r.ReadByte(); d.AttackPoint = r.ReadByte(); d.Position = r.ReadVec3S(); d.DirectionShort = r.ReadVec3S();
                int n = r.ReadByte();
                for (int i = 0; i < n; i++) {
                    int id = r.ReadInt(); var hits = new Hit[r.ReadByte()];
                    for (int j = 0; j < hits.Length; j++) hits[j] = new Hit(r.ReadByte(), r.ReadLong());
                    d.Targets.Add(new HitTarget(id, hits, d.Position, default));
                }
                break;
            }
            case 3:
                d.OwnerId = r.ReadInt(); d.TargetId = r.ReadInt(); d.BuffId = r.ReadInt(); d.DotHit = new Hit(r.ReadByte(), gms ? r.ReadInt() : r.ReadLong());
                d.Targets.Add(new HitTarget(d.TargetId, new[] { d.DotHit }, default, default));
                break;
            case 5: {
                d.CastUid = r.ReadLong(); d.OwnerId = r.ReadInt(); d.RegionId = r.ReadInt(); d.AttackPoint = r.ReadByte();
                int n = r.ReadByte();
                for (int i = 0; i < n; i++) {
                    int id = r.ReadInt(); var hits = new Hit[r.ReadByte()]; var o = r.ReadVec3S(); var dir = r.ReadVec3();
                    for (int j = 0; j < hits.Length; j++) hits[j] = new Hit(r.ReadByte(), r.ReadLong());
                    d.Targets.Add(new HitTarget(id, hits, o, dir));
                }
                break;
            }
            case 6: {
                d.CastUid = r.ReadLong(); d.SkillId = r.ReadInt(); d.Level = (short) r.ReadUShort();
                int n = r.ReadByte();
                for (int i = 0; i < n; i++) {
                    int id = r.ReadInt(); var hits = new Hit[r.ReadByte()]; var dir = r.ReadVec3S(); var pos = r.ReadVec3();
                    for (int j = 0; j < hits.Length; j++) hits[j] = new Hit(r.ReadByte(), r.ReadLong());
                    d.Targets.Add(new HitTarget(id, hits, new Vector3S((short) pos.X, (short) pos.Y, (short) pos.Z), default));
                }
                break;
            }
            default:
                d.Raw = r.ReadBytes(r.Available);
                return d;
        }
        r.ExpectEnd($"SkillDamage mode {d.Mode}");
        return d;
    }
}

/// <summary>
/// Inbound 0x2E for an npc (OnStat, CNpc::vfunc_90): i32 object id, u8 flag, u8 type; type 1 = one stat (u8 stat id, 3 values: i64 for health 4, else i32),
/// otherwise 3 x (i64 health, i32 spirit). The three values are total, base and current. Player stat packets use a different list form
/// and are not decoded here.
/// </summary>
public sealed record NpcStat(int ObjectId, byte Flag, byte Stat, long[] Values, bool IsHealth) {
    public long Max => Values[0];
    public long Current => Values[2];
    public static NpcStat Read(ByteReader r) {
        int id = r.ReadInt(); byte flag = r.ReadByte(); byte type = r.ReadByte();
        if (type == 1) {
            byte stat = r.ReadByte(); var v = new long[3];
            for (int i = 0; i < 3; i++) v[i] = stat == 4 ? r.ReadLong() : (uint) r.ReadInt();
            r.ExpectEnd("NpcStat");
            return new NpcStat(id, flag, stat, v, stat == 4);
        } else {
            var v = new long[3];
            for (int i = 0; i < 3; i++) { v[i] = r.ReadLong(); r.ReadInt(); }
            r.ExpectEnd("NpcStat");
            return new NpcStat(id, flag, 4, v, true);
        }
    }
}

/// <summary>
/// Inbound 0x2E for the own player (CSceneStateGame::OnStat, scenestategame.cpp:9424, then PCBase::vfunc_90, pcbase.cpp:2409):
/// i32 object id, u8 command (0), u8 count. Writer: StatsPacket.Init / Update(entity, attributes) in the kms2525 server.
/// count 35 (BASIC_TOTAL) = Init: every attribute in index order without the id byte; otherwise count x (u8 attribute,
/// total, base, current), i64 for Health (4), i32 for the rest. This is the self reader: the own packet is consumed by the
/// session before the field tracker; another player's packet uses <see cref="PcStat"/> (PC::vfunc_90's other 0x23 form).
/// </summary>
public sealed record PlayerStat(int ObjectId, IReadOnlyDictionary<byte, long[]> Values) {
    public const int Total = 35, Health = 4, Spirit = 7, Stamina = 10;
    public static PlayerStat Read(ByteReader r) {
        int id = r.ReadInt(); r.ReadByte(); int count = r.ReadByte();
        var d = new Dictionary<byte, long[]>();
        for (int n = 0; n < count; n++) {
            byte a = count == Total ? (byte) n : r.ReadByte();
            var v = new long[3];
            for (int i = 0; i < 3; i++) v[i] = a == Health ? r.ReadLong() : (uint) r.ReadInt();
            d[a] = v;
        }
        r.ExpectEnd("PlayerStat");
        return new PlayerStat(id, d);
    }
    public bool TryGet(byte attribute, out long total, out long current) {
        if (Values.TryGetValue(attribute, out var v)) { total = v[0]; current = v[2]; return true; }
        total = current = 0; return false;
    }
}

/// <summary>
/// Inbound 0x2E for another player's actor (OnStat's PCBase is a PC, so PC::vfunc_90, pc.cpp:1645; FieldAddUser's stat
/// block goes through PC::vfunc_91, the same body): i32 object id, u8 command, u8 count. 0x23 = the same block
/// FieldAddUser carries, three stat rows (total, base, current) of i64 health, i32 attack speed (0xd), i32 movement
/// speed (0xe), i32 jump height (0x20), i32 mount speed (0x16); any other count x (u8 stat, 3 values, i64 for health 4
/// else i32, IsPercentStat stat.cpp:314). No end check: PC::vfunc_90 consumes only what the count's branch reads, so the
/// self form the server broadcasts on a skill use (StatsPacket.Init, the 35-attribute body) is read as its first three
/// rows and the rest is left unread, exactly as the client leaves it.
/// </summary>
public sealed record PcStat(int ObjectId, IReadOnlyDictionary<byte, long[]> Values) {
    public const byte Health = 4, AttackSpeed = 0xd, MovementSpeed = 0xe, JumpHeight = 0x20, MountSpeed = 0x16;
    public static PcStat Read(ByteReader r) {
        int id = r.ReadInt(); r.ReadByte(); byte count = r.ReadByte();
        var d = new Dictionary<byte, long[]>();
        if (count == 0x23) {
            for (byte kind = 0; kind < 3; kind++) {
                void Set(byte s, long v) { if (!d.TryGetValue(s, out var a)) d[s] = a = new long[3]; a[kind] = v; }
                Set(Health, r.ReadLong()); Set(AttackSpeed, r.ReadInt()); Set(MovementSpeed, r.ReadInt());
                Set(JumpHeight, r.ReadInt()); Set(MountSpeed, r.ReadInt());
            }
        } else {
            for (int n = 0; n < count; n++) {
                byte s = r.ReadByte();
                var v = new long[3];
                for (int i = 0; i < 3; i++) v[i] = s == Health ? r.ReadLong() : (uint) r.ReadInt();
                d[s] = v;
            }
        }
        return new PcStat(id, d);
    }
    public bool TryGet(byte stat, out long total, out long current) {
        if (Values.TryGetValue(stat, out var v)) { total = v[0]; current = v[2]; return true; }
        total = current = 0; return false;
    }
}
