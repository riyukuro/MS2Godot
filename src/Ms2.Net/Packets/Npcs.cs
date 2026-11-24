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
    public bool Hidden;
    public List<Buff> Buffs = new();
    public long MaidUid; public bool Talkable; public int Level, Unknown;
    public string BossEffect; public (int SkillId, short Level)[] BossSkills = Array.Empty<(int, short)>(); public int BossValue;
    public long AccumulatedDamage; public bool Flag;

    public static FieldAddNpc Read(ByteReader r, Func<int, NpcLayout> layoutOf) {
        var n = new FieldAddNpc { ObjectId = r.ReadInt(), NpcId = r.ReadInt(), Position = r.ReadVec3(), Rotation = r.ReadVec3() };
        var layout = layoutOf(n.NpcId);
        if (layout.IsBoss) n.BossModelName = r.ReadString();
        // CNpc::vfunc_90: type 1 = one stat (u8 id, 3 values), else 3 x (i64 health, i32 spirit)
        byte type = r.ReadByte();
        if (type == 1) { r.ReadByte(); for (int i = 0; i < 3; i++) r.ReadLong(); /* percent stats are i64, others i32; only seen as type 35 */ throw new PacketLayoutException("npc single-stat form not decoded"); }
        for (int i = 0; i < 3; i++) n.Stats[i] = (r.ReadLong(), r.ReadInt());
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
}

/// <summary>
/// Inbound FieldAddPet (RecvOp 0x5a), the prefix CSceneStateGame::OnFieldAddPet (scenestategame.cpp:9682) reads before the
/// pet's stats: i32 object id, i32 npc id, i32 level, position, rotation, f32 scale, i32 owner (SetOwnerId: the owner's object
/// id). The rest (the npc stats, maid uid, grade, name, ...) is left unread.
/// </summary>
public sealed record FieldAddPet(int ObjectId, int NpcId, int Level, System.Numerics.Vector3 Position, System.Numerics.Vector3 Rotation, float Scale, int OwnerId) {
    public static FieldAddPet Read(ByteReader r) =>
        new(r.ReadInt(), r.ReadInt(), r.ReadInt(), r.ReadVec3(), r.ReadVec3(), r.ReadFloat(), r.ReadInt());

    /// <summary>The pet's skin id (GMS2 only: the second field), 0 on KMS2.</summary>
    public int SkinId { get; init; }
    /// <summary>The pet item's uid, taming rank and name (GMS2 only; the whole packet is read there).</summary>
    public long PetUid { get; init; }
    public short TamingRank { get; init; }
    public string Name { get; init; } = "";

    /// <summary>
    /// GMS2 (0x5B): i32 object id, i32 skin id, i32 npc id, position, rotation, f32 scale, i32 owner, the npc stats (u8 type, 3 x (i64, i32)),
    /// i64 pet uid, u8, i16 level, i16 taming rank, i32, unicode name.
    /// </summary>
    public static FieldAddPet Read(ByteReader r, ProtocolProfile profile) {
        if (!profile.IsGms2) return Read(r);
        int id = r.ReadInt(), skin = r.ReadInt(), npc = r.ReadInt();
        var pos = r.ReadVec3(); var rot = r.ReadVec3(); float scale = r.ReadFloat(); int owner = r.ReadInt();
        r.ReadByte(); for (int i = 0; i < 3; i++) { r.ReadLong(); r.ReadInt(); }
        long uid = r.ReadLong(); r.ReadByte();
        short level = r.ReadShort(), rank = r.ReadShort(); r.ReadInt();
        string name = r.ReadUnicodeString();
        r.ExpectEnd("FieldAddPet");
        return new(id, npc, level, pos, rot, scale, owner) { SkinId = skin, PetUid = uid, TamingRank = rank, Name = name };
    }
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
