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
/// Inbound 0x1D, a user's chat line (UIMessengerBrowser::vfunc_84, uimessenger.cpp:2829; reader FUN_1407a5500,
/// unit_1407a4f20_packetserializers.cpp:166, writer FUN_1407a5330 :148): i64 account id (m_00), i64 character id (m_08),
/// unicode name (m_10), u8 common: an i32 common string id (m_48) whose text (GetStringCommon) is the line, else a unicode
/// text (m_18); i32 type (m_20), u8 gm (m_34), i32 gender (m_30); then type 3: the whisper window's ReadUnicodeStringA
/// (m_38 m_strWindow, a unicode string made narrow), type 0x14: i64 club id (m_40), type 0x10 / 0x19: i32 item id (m_50,
/// m_nFieldId); last a u8 (m_4c), the chat image flag FUN_141842770 tests. A type 0x19 line is followed by a <see cref="WeddingChatHall"/> the handler
/// reads itself. No ExpectEnd: the client ignores trailing bytes (later builds append a field).
/// </summary>
public sealed record ChatMessage(long AccountId, long CharacterId, string Name, bool Common, int CommonId, string Text, int Type,
    bool Gm, int Gender, string WhisperWindow, long ClubId, int ItemId, byte ShowImage) {
    public static ChatMessage Read(ByteReader r) {
        long acc = r.ReadLong(), chr = r.ReadLong();
        string name = r.ReadUnicodeString();
        bool common = r.ReadByte() != 0;
        int commonId = 0; string text = "";
        if (common) commonId = r.ReadInt(); else text = r.ReadUnicodeString();
        int type = r.ReadInt();
        bool gm = r.ReadByte() != 0;
        int gender = r.ReadInt();
        string whisper = ""; long club = 0; int item = 0;
        if (type == 3) whisper = r.ReadUnicodeString();
        else if (type == 0x14) club = r.ReadLong();
        else if (type == 0x10 || type == 0x19) item = r.ReadInt();
        byte last = r.ReadByte();
        return new ChatMessage(acc, chr, name, common, commonId, text, type, gm, gender, whisper, club, item, last);
    }
}

/// <summary>
/// Inbound 0x1E, the items chat lines link (UIMessengerBrowser::vfunc_85 @0x141a22cd0, uimessenger.cpp:2963, the handler
/// of RecvOp 0x1e its ctor registers at :2699): u32 count, then per item i64 uid, i32 item id, i32 rarity and the body
/// CItem::ReadFromStream reads into the item FUN_140523c40 made. For an id the item data does not know the client makes no
/// item and reads no body, and its next loop reads the body's bytes as the next record; this reader stops there instead
/// (<see cref="ItemReader.Known"/>).
/// </summary>
public sealed record ChatItemLinks(IReadOnlyList<EquipItem> Items) {
    public static ChatItemLinks Read(ByteReader r) {
        uint count = (uint)r.ReadInt();
        var items = new List<EquipItem>();
        for (uint i = 0; i < count; i++) {
            long uid = r.ReadLong();
            int id = r.ReadInt(), rarity = r.ReadInt();
            if (ItemReader.Known != null && !ItemReader.Known(id)) break;
            var it = ItemReader.ReadItem(r, id, rarity);
            it.Uid = uid;                                   // SetItemUid before ReadFromStream
            items.Add(it);
        }
        return new ChatItemLinks(items);
    }
}

/// <summary>
/// Outbound 0x11 (SendOp::UserChat), a chat line to send: FUN_141276f80 (unit_141274d10_uihelpers.cpp:913) writes the
/// Unk_CommandChat record with FUN_1401fa700 (unit_1401f9eb0_packetpayloadserializers.cpp:415): i32 kind (+4), unicode
/// message (+8), unicode target (+0x10, the whisper's name), i64 value (+0x18, the club id of a club line).
/// </summary>
public sealed record UserChatRequest(int Kind, string Message, string Target, long Value) : Ms2.Net.Wire.IOutPacket {
    public Outbound Op => Outbound.UserChat;
    public void Write(ByteWriter w) {
        w.WriteInt(Kind);
        w.WriteUnicodeString(Message ?? "");
        w.WriteUnicodeString(Target ?? "");
        w.WriteLong(Value);
    }
}

/// <summary>
/// The wedding hall block after a type 0x19 chat line (reader FUN_1407a5960, unit_1407a4f20_packetserializers.cpp:205):
/// i64, i32 hall, i32 room, i64 time, two unicode strings. vfunc_84 reads it only with feature 0x104 on. STILL-OPEN: what
/// the first i64 and the strings are; the wedding line builder (FUN_141841180) needs the wedding table, not ported.
/// </summary>
public sealed record WeddingChatHall(long Id, int Hall, int Room, long Time, string Text1, string Text2) {
    public static WeddingChatHall Read(ByteReader r) =>
        new(r.ReadLong(), r.ReadInt(), r.ReadInt(), r.ReadLong(), r.ReadUnicodeString(), r.ReadUnicodeString());
}
