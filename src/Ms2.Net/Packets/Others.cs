// Layouts from the restored client's readers (CSceneStateGame::On*, scenestategame.cpp), checked against the server fork's writers and recorded KMS2 traffic.
using System.Numerics;
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>Inbound 0x36 (OnUserState): i32 object id, u8 state. State 12 is the death state.</summary>
public sealed record UserState(int ObjectId, byte State) {
    public static UserState Read(ByteReader r) { var p = new UserState(r.ReadInt(), r.ReadByte()); r.ExpectEnd("UserState"); return p; }
}

/// <summary>Inbound 0x34 (CSceneStateGame::OnRevival, scenestategame.cpp:7387): i32 object id, u8 flag (PC::FUN_141661ba0). The
/// client ignores trailing bytes.</summary>
public sealed record Revival(int ObjectId, byte Flag) {
    public static Revival Read(ByteReader r) => new(r.ReadInt(), r.ReadByte());
}

/// <summary>Inbound 0xA6 (CSceneStateGame::OnDeadUser, scenestategame.cpp:6532): i32 object id, u8 flag (FUN_14028d780: set
/// = PC::FUN_141661ba0(1)), then the die (PC::vfunc_137). The client ignores trailing bytes.</summary>
public sealed record DeadUser(int ObjectId, bool Flag) {
    public static DeadUser Read(ByteReader r) => new(r.ReadInt(), r.ReadByte() != 0);
}

/// <summary>Inbound 0x2F (OnUserBattle): i32 object id, u8 emotion (the combat stance face).</summary>
public sealed record UserBattle(int ObjectId, byte Emotion) {
    public static UserBattle Read(ByteReader r) { var p = new UserBattle(r.ReadInt(), r.ReadByte()); r.ExpectEnd("UserBattle"); return p; }
}

/// <summary>Inbound 0x65 (OnStateFallDamage): i32 object id, i32 amount.</summary>
public sealed record StateFallDamage(int ObjectId, int Amount) {
    public static StateFallDamage Read(ByteReader r) { var p = new StateFallDamage(r.ReadInt(), r.ReadInt()); r.ExpectEnd("StateFallDamage"); return p; }
}

/// <summary>
/// Inbound 0x3C (OnSkillUse): i64 cast uid, i32 serial, i32 actor object id, i32 skill id, i16 level, u8 motion,
/// position (3 x i16), direction (vec3), rotation (vec3), speed (i16, tenths), bool effect, bool has extra (+ i32, string).
/// </summary>
public sealed record SkillUse(long CastUid, int Serial, int ActorId, int SkillId, short Level, byte Motion, Vector3S Position,
    Vector3 Direction, Vector3 Rotation, float Speed, bool PetEffect, bool HasExtra, int Extra, string ExtraText) {
    public static SkillUse Read(ByteReader r) {
        var p = new SkillUse(r.ReadLong(), r.ReadInt(), r.ReadInt(), r.ReadInt(), r.ReadShort(), r.ReadByte(), r.ReadVec3S(),
            r.ReadVec3(), r.ReadVec3(), r.ReadShort() / 10f, r.ReadBool(), false, 0, null);
        if (r.ReadBool()) p = p with { HasExtra = true, Extra = r.ReadInt(), ExtraText = r.ReadUnicodeString() };
        r.ExpectEnd("SkillUse"); return p;
    }
}

/// <summary>Inbound 0x3E (OnSkillSync): i64 cast uid, i32 actor, i32 skill id, i16 level, u8 motion, 4 x vec3 (position, direction, rotation, move), 2 bools, float speed.</summary>
public sealed record SkillSync(long CastUid, int ActorId, int SkillId, short Level, byte Motion, Vector3 Position, Vector3 Direction,
    Vector3 Rotation, Vector3 Move, bool A, bool B, float Speed) {
    public static SkillSync Read(ByteReader r) {
        var p = new SkillSync(r.ReadLong(), r.ReadInt(), r.ReadInt(), r.ReadShort(), r.ReadByte(), r.ReadVec3(), r.ReadVec3(), r.ReadVec3(), r.ReadVec3(),
            r.ReadBool(), r.ReadBool(), r.ReadFloat());
        r.ExpectEnd("SkillSync"); return p;
    }
}

/// <summary>Inbound 0x3F (OnSkillCancel): i64 cast uid, i32 actor.</summary>
public sealed record SkillCancel(long CastUid, int ActorId) {
    public static SkillCancel Read(ByteReader r) { var p = new SkillCancel(r.ReadLong(), r.ReadInt()); r.ExpectEnd("SkillCancel"); return p; }
}

/// <summary>Inbound 0x41 (OnStateSkill): u8 type; the client acts only on type 6 (i32 actor, i64 cast uid, i32 skill id, i32 level). Bytes beyond what the client reads are kept in Rest.</summary>
public sealed record StateSkill(byte Type, int ActorId, long CastUid, int SkillId, int Level, byte[] Rest) {
    public static StateSkill Read(ByteReader r) {
        byte type = r.ReadByte();
        if (type != 6) return new StateSkill(type, 0, 0, 0, 0, r.ReadBytes(r.Available));
        return new StateSkill(type, r.ReadInt(), r.ReadLong(), r.ReadInt(), r.ReadInt(), r.ReadBytes(r.Available));
    }
}

/// <summary>Inbound 0x25 (OnItemPutOn): another player (or this one) wears an item.</summary>
public sealed record ItemPutOn(int ObjectId, byte Kind, EquipItem Item) {
    public static ItemPutOn Read(ByteReader r) { var (o, k, it) = ItemReader.ReadPutOn(r); r.ExpectEnd("ItemPutOn"); return new ItemPutOn(o, k, it); }
}

/// <summary>
/// Inbound 0x7F (CSceneStateGame::OnBadgeEquip, scenestategame.cpp:11006): u8 mode. Mode 0 a gem put in a gem slot
/// (<see cref="ItemReader.ReadBadgeEquip"/>); mode 1 one taken out: i32 object id, u8 the gem type (RemoveByGemType);
/// mode 4 an error: i16 code. Other modes carry nothing the client reads.
/// </summary>
public sealed record BadgeEquipMessage(byte Mode, int ObjectId, EquipItem Item, byte GemType, short Error) {
    public static BadgeEquipMessage Read(ByteReader r) {
        byte mode = r.ReadByte();
        switch (mode) {
            case 0: { var (o, it) = ItemReader.ReadBadgeEquip(r); return new BadgeEquipMessage(0, o, it, it.GemType, 0); }
            case 1: return new BadgeEquipMessage(1, r.ReadInt(), null, r.ReadByte(), 0);
            case 4: return new BadgeEquipMessage(4, 0, null, 0, r.ReadShort());
            default: return new BadgeEquipMessage(mode, 0, null, 0, 0);
        }
    }
}

/// <summary>Inbound 0x26 (OnItemPutOff): i32 object id, i64 item uid.</summary>
public sealed record ItemPutOff(int ObjectId, long ItemUid) {
    public static ItemPutOff Read(ByteReader r) { var p = new ItemPutOff(r.ReadInt(), r.ReadLong()); r.ExpectEnd("ItemPutOff"); return p; }
}

public enum BuffMode : byte { Add = 0, Remove = 1, Modify = 2 }

/// <summary>
/// Inbound 0x47 (OnBuff): u8 mode, owner header (i32 object id, i32 buff key, i32 caster id). Add: state (i32 start tick, i32 end tick,
/// i32 skill id, i16 level, i32 overlap, u8 enabled) + i64 shield. Remove: nothing more. Modify: i32 flags, bit 1 state, bit 2 i64 shield.
/// </summary>
public sealed record BuffMessage(BuffMode Mode, int ObjectId, int BuffKey, int CasterId, int StartTick, int EndTick, int SkillId, short Level,
    int OverlapCount, byte Enabled, long Shield, int Flags) {
    public static BuffMessage Read(ByteReader r) {
        var mode = (BuffMode) r.ReadByte();
        var m = new BuffMessage(mode, r.ReadInt(), r.ReadInt(), r.ReadInt(), 0, 0, 0, 0, 0, 0, 0, 0);
        switch (mode) {
            case BuffMode.Add:
                m = m with { StartTick = r.ReadInt(), EndTick = r.ReadInt(), SkillId = r.ReadInt(), Level = r.ReadShort(), OverlapCount = r.ReadInt(), Enabled = r.ReadByte(), Shield = r.ReadLong() };
                break;
            case BuffMode.Remove: break;
            case BuffMode.Modify: {
                int f = r.ReadInt();
                m = m with { Flags = f };
                if ((f & 1) != 0) m = m with { StartTick = r.ReadInt(), EndTick = r.ReadInt(), SkillId = r.ReadInt(), Level = r.ReadShort(), OverlapCount = r.ReadInt(), Enabled = r.ReadByte() };
                if ((f & 2) != 0) m = m with { Shield = r.ReadLong() };
                break;
            }
            default: throw new PacketLayoutException($"buff mode {mode}");
        }
        r.ExpectEnd("Buff"); return m;
    }
}

/// <summary>
/// Inbound 0x1D chat line: i64 account id, i64 character id, name, bool, text, i32 chat type, bool gm, i32 channel, then by type:
/// 3 whisper-from: string; 16 super and 25 wedding: i32; 20 club: i64; and a final bool. The client reader was not located in the
/// decomp; layout from the server writer and checked on recordings.
/// </summary>
public sealed record ChatMessage(long AccountId, long CharacterId, string Name, string Text, int ChatType, bool Gm, int Channel, string WhisperTo, int Extra, long ClubId) {
    public static ChatMessage Read(ByteReader r) {
        long acc = r.ReadLong(), chr = r.ReadLong(); string name = r.ReadUnicodeString(); r.ReadBool(); string text = r.ReadUnicodeString();
        int type = r.ReadInt(); bool gm = r.ReadBool(); int ch = r.ReadInt();
        string w = null; int extra = 0; long club = 0;
        if (type == 3) w = r.ReadUnicodeString();
        else if (type == 16 || type == 25) extra = r.ReadInt();
        else if (type == 20) club = r.ReadLong();
        r.ReadBool();
        r.ExpectEnd("ChatMessage");
        return new ChatMessage(acc, chr, name, text, type, gm, ch, w, extra, club);
    }
}
