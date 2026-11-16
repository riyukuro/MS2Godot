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
    // mode 3
    public int TargetId; public Hit DotHit;
    // mode 5
    public int RegionId;
    // mode 0
    public int ProjectileCount;
    public byte[] Raw;

    public static SkillDamage Read(ByteReader r) {
        var d = new SkillDamage { Mode = r.ReadByte() };
        switch (d.Mode) {
            case 0: {
                d.CastUid = r.ReadLong(); d.OwnerId = r.ReadInt(); d.SkillId = r.ReadInt(); d.Level = r.ReadShort(); r.ReadByte(); r.ReadByte();
                d.Position = r.ReadVec3S(); r.ReadVec3(); r.ReadByte();
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
                d.SkillId = r.ReadInt(); d.OwnerId = r.ReadInt(); d.TargetId = r.ReadInt(); d.DotHit = new Hit(r.ReadByte(), r.ReadLong());
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
