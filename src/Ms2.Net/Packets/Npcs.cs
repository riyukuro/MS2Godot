// Layouts from the restored client's readers (CSceneStateGame::OnFieldAddNpc, OnNpcControl, CNpc::ApplySyncBlock), checked against the
// Maple2 server fork's writers (AGPL-3.0) and recorded KMS2 traffic.
using System.Numerics;
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>What the reader of FieldAddNpc needs from the npc table: the packet's layout depends on the npc's data row.</summary>
public readonly record struct NpcLayout(bool IsBoss, bool HasDamagedHpBarTag);

/// <summary>
/// Inbound 0x55. i32 object id, i32 npc id, position, rotation, [boss: narrow model name], stats, bool dead/hidden, buff list, i64 maid uid,
/// u8 talkable, i32 level, i32, [boss: effect string, i32 n x (i32 skill id, i16 level), i32], [npc has property tag 5: i64 damage], u8.
/// A boss is an npc row with friendly = 0 and class >= 3 (CNpcData::IsBoss); tag 5 is damaged_hp_upper_hp_bar. The row decides the layout,
/// so the reader takes a lookup (NpcTable in Ms2.Formats-side code fills it from the player's own npcdata).
/// </summary>
public sealed class FieldAddNpc {
    public int ObjectId, NpcId; public Vector3 Position, Rotation;
    public string BossModelName;
    public (long Health, int Spirit)[] Stats = new (long, int)[3];
    /// <summary>The single-stat form's stat id and its total, base and current (0 and null for the three-row form; see <see cref="ReadStats"/>).</summary>
    public byte OneStat; public long[] OneStatValues;
    public bool Hidden;
    public List<Buff> Buffs = new();
    public long MaidUid; public bool Talkable; public int Level, Unknown;
    public string BossEffect; public (int SkillId, short Level)[] BossSkills = Array.Empty<(int, short)>(); public int BossValue;
    public long AccumulatedDamage; public bool Flag;

    public static FieldAddNpc Read(ByteReader r, Func<int, NpcLayout> layoutOf) {
        var n = new FieldAddNpc { ObjectId = r.ReadInt(), NpcId = r.ReadInt(), Position = r.ReadVec3(), Rotation = r.ReadVec3() };
        var layout = layoutOf(n.NpcId);
        if (layout.IsBoss) n.BossModelName = r.ReadString();
        (n.OneStat, n.OneStatValues) = ReadStats(r, n.Stats);
        n.Hidden = r.ReadByte() != 0;
        int buffs = (ushort) r.ReadShort();
        for (int i = 0; i < buffs; i++) n.Buffs.Add(Buff.Read(r));
        n.MaidUid = r.ReadLong(); n.Talkable = r.ReadBool(); n.Level = r.ReadInt(); n.Unknown = r.ReadInt();
        if (layout.IsBoss) {
            n.BossEffect = r.ReadUnicodeString();
            n.BossSkills = new (int, short)[r.ReadInt()];
            for (int i = 0; i < n.BossSkills.Length; i++) n.BossSkills[i] = (r.ReadInt(), r.ReadShort());
            n.BossValue = r.ReadInt();
        }
        if (layout.HasDamagedHpBarTag) n.AccumulatedDamage = r.ReadLong();
        n.Flag = r.ReadByte() == 1;
        r.ExpectEnd("FieldAddNpc");
        return n;
    }

    /// <summary>
    /// CNpc::vfunc_90 @0x1410909a0, the stats FieldAddNpc and FieldAddPet carry, rows total, base, current (SetStat(i, stat, value)).
    /// u8 type; type 1 = one stat: u8 stat id, 3 values, i64 when IsPercentStat (stat.cpp:314: only health, 4), else u32; health
    /// goes to the rows' Health, spirit (0xd) to their Spirit. Any other type = 3 x (i64 health, u32 spirit). Returns the one
    /// stat's id and values, or (0, null) for the three-row form.
    /// </summary>
    internal static (byte Stat, long[] Values) ReadStats(ByteReader r, (long Health, int Spirit)[] stats) {
        byte type = r.ReadByte();
        if (type != 1) {
            for (int i = 0; i < 3; i++) stats[i] = (r.ReadLong(), r.ReadInt());
            return (0, null);
        }
        byte stat = r.ReadByte();
        var v = new long[3];
        for (int i = 0; i < 3; i++) {
            v[i] = stat == 4 ? r.ReadLong() : (uint) r.ReadInt();
            if (stat == 4) stats[i].Health = v[i];
            else if (stat == 0xd) stats[i].Spirit = (int) v[i];
        }
        return (stat, v);
    }
}

/// <summary>
/// Inbound FieldAddPet (KMS 0x5A, GMS2 0x5B), the whole of CSceneStateGame::OnFieldAddPet @0x141719a60 (scenestategame.cpp:9682-9709),
/// the same on both: i32 object id (SetObjectId), i32 skin id (vfunc_124, the npc the pet is drawn as), i32 npc id (SetPetId),
/// position, rotation, f32 scale, i32 owner (SetOwnerId: the owner's object id), the npc stats (CNpc::vfunc_90), i64 pet item uid
/// (SetMaidUid), u8, i16 level (FUN_14174a580, the setter FieldAddNpc's i32 level goes to, :3562), i16 taming rank (SetGrade), i32,
/// unicode name.
/// </summary>
public sealed record FieldAddPet(int ObjectId, int SkinId, int NpcId, System.Numerics.Vector3 Position, System.Numerics.Vector3 Rotation, float Scale, int OwnerId) {
    public (long Health, int Spirit)[] Stats { get; init; } = new (long, int)[3];
    public long PetUid { get; init; }
    public int Level { get; init; }
    public short TamingRank { get; init; }
    public string Name { get; init; } = "";

    public static FieldAddPet Read(ByteReader r) {
        int id = r.ReadInt(), skin = r.ReadInt(), npc = r.ReadInt();
        var pos = r.ReadVec3(); var rot = r.ReadVec3(); float scale = r.ReadFloat(); int owner = r.ReadInt();
        var stats = new (long, int)[3];
        FieldAddNpc.ReadStats(r, stats);
        long uid = r.ReadLong(); r.ReadByte();
        short level = r.ReadShort(), rank = r.ReadShort(); r.ReadInt();
        string name = r.ReadUnicodeString();
        r.ExpectEnd("FieldAddPet");
        return new(id, skin, npc, pos, rot, scale, owner) { Stats = stats, PetUid = uid, Level = level, TamingRank = rank, Name = name };
    }

    /// <summary>Both regions read the same body (<see cref="Read(ByteReader)"/>).</summary>
    public static FieldAddPet Read(ByteReader r, ProtocolProfile profile) => Read(r);
}

/// <summary>Inbound FieldRemovePet (RecvOp 0x5b): CSceneStateGame::OnFieldRemovePet reads the i32 object id first.</summary>
public sealed record PetObjectId(int ObjectId) {
    public static PetObjectId Read(ByteReader r) => new(r.ReadInt());
}

/// <summary>Inbound 0x56 and 0x57 (remove, dead): i32 object id.</summary>
public sealed record NpcObjectId(int ObjectId) {
    public static NpcObjectId Read(ByteReader r) { var p = new NpcObjectId(r.ReadInt()); r.ExpectEnd("npc id"); return p; }
}

/// <summary>
/// One npc's block in NpcControl (CNpc::ApplySyncBlock): i32 id, u8 flags (1 = apply rotation, 2 = emotion), position (3 x i16), rotation (i16 / 10),
/// velocity (3 x i16), animation speed (i16 / 100), [boss: i32 owner actor], u8 state, i16 animation sequence id (-1 keep, -2 and -3 jump arcs;
/// otherwise the KFM sequence id), u16 state serial; a jump sequence is followed by its arc (u8 absolute, then 2 vec3 and 2 floats, or 1 vec3).
/// What follows (fields of the new state, read only when the serial changes) stays raw in <see cref="Tail"/>; the block is length-prefixed.
/// </summary>
public sealed class NpcSync {
    public int ObjectId; public byte Flags;
    public Vector3S Position; public float RotationZ; public Vector3S Velocity; public float AnimSpeed;
    public int OwnerActor; public byte State; public short Sequence; public ushort Serial;
    public bool JumpAbsolute; public Vector3 JumpFrom, JumpTo; public float JumpWidth, JumpScale;
    public byte[] Tail = Array.Empty<byte>();
    public bool Rotates => (Flags & 1) != 0;
    public bool Emotion => (Flags & 2) != 0;
    /// <summary>The spawn id a block of the spawn state (0x11) leads its tail with (ApplySyncBlock :976, ReadInt32); 0 otherwise.</summary>
    public int SpawnId => State == 0x11 && Tail.Length >= 4 ? System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(Tail) : 0;

    public static NpcSync Read(ByteReader r, Func<int, NpcLayout> layoutOf) {
        var s = new NpcSync { ObjectId = r.ReadInt(), Flags = r.ReadByte(), Position = r.ReadVec3S(), RotationZ = r.ReadShort() / 10f, Velocity = r.ReadVec3S(), AnimSpeed = r.ReadShort() / 100f };
        if (layoutOf(s.ObjectId).IsBoss) s.OwnerActor = r.ReadInt();
        s.State = r.ReadByte(); s.Sequence = r.ReadShort(); s.Serial = r.ReadUShort();
        if (s.Sequence is -2 or -3) {
            if (r.ReadByte() != 0) { s.JumpAbsolute = true; s.JumpFrom = r.ReadVec3(); s.JumpTo = r.ReadVec3(); s.JumpWidth = r.ReadFloat(); s.JumpScale = r.ReadFloat(); }
            else s.JumpTo = r.ReadVec3();
        }
        s.Tail = r.ReadBytes(r.Available);
        return s;
    }
}

/// <summary>
/// Inbound 0x58: i16 count, then per npc i16 size and the block. The first int of a block is the NPC's object id, and the boss layout depends on the
/// npc row, which is looked up by the npc id of the object seen in FieldAddNpc: <paramref name="isBossObject"/> answers by object id.
/// </summary>
public sealed class NpcControl {
    public List<NpcSync> Blocks = new();
    public static NpcControl Read(ByteReader r, Func<int, bool> isBossObject) {
        var c = new NpcControl();
        int n = (ushort) r.ReadShort();
        for (int i = 0; i < n; i++) {
            int size = (ushort) r.ReadShort();
            var block = new ByteReader(r.ReadBytes(size));
            c.Blocks.Add(NpcSync.Read(block, id => new NpcLayout(isBossObject(id), false)));
        }
        r.ExpectEnd("NpcControl");
        return c;
    }
}

/// <summary>
/// Inbound 0x130 (CSceneStateGame::FUN_141720c60 @0x141720c60, scenestategame.cpp:7677-7692; the switch's case 0x130, :1533):
/// i32 actor object id, i32 skill id, i16 level, Vector3 position, Vector3 direction (34 bytes). The client looks the actor and the
/// skill's level row up and has CSkillCollisionManager::Show (skillcollisionmanager.cpp:375-398) draw the row's spread range shape
/// there, only while its debug switches to show skill collision shapes are on (DAT_143ae95e0 for a pc, DAT_143ae95e1 for an npc,
/// off in a normal client), so the game reads it and shows nothing. Upstream Maple2 has no SendOp row for it, so GMS2 has no number.
/// </summary>
public sealed record NpcSkillSpread(int ActorId, int SkillId, short Level, Vector3 Position, Vector3 Direction) {
    public static NpcSkillSpread Read(ByteReader r) => new(r.ReadInt(), r.ReadInt(), r.ReadShort(), r.ReadVec3(), r.ReadVec3());
}
