using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Outbound Revival (SendOp 0x1F), what the revive dialog's buttons send (UIRevivalDialog, uirevivaldialog.cpp):
/// u8 mode, then for the paid modes u8 the coupon check box (FUN_141abc9d0, 0 / 1).
/// Mode 0 (free revive at the return point: SendRevivalMode0 @0x141abadb0, FUN_141abb600 @0x141abb600, uirevivaldialog.cpp:393): [0].
/// Mode 1 (merat: SendRevivalMode1 @0x141abaa30, uirevivaldialog.cpp:224, FUN_141abbb20, :433): [1, coupon].
/// Mode 2 (meso: FUN_141abc080, uirevivaldialog.cpp:488): [2, coupon].
/// </summary>
public sealed record RevivalRequest(byte Mode, bool Coupon = false) : IOutPacket {
    public Outbound Op => Outbound.Revival;
    public void Write(ByteWriter w) {
        w.WriteByte(Mode);
        if (Mode != 0) w.WriteByte(Coupon ? (byte)1 : (byte)0);
    }
}

/// <summary>
/// Outbound RequestMoveField (0x6C) mode 1, UIRevivalDialog::OnMessage (@0x141aba920, uirevivaldialog.cpp:213-216) on UI message
/// 0x7d2 (the "leave the dungeon" answer): u8 1, nothing else.
/// </summary>
public sealed record RequestMoveFieldLeave : IOutPacket {
    public Outbound Op => Outbound.RequestMoveField;
    public void Write(ByteWriter w) => w.WriteByte(1);
}

/// <summary>
/// Outbound StateFallDamage (SendOp 0x34), PCBase::SendFallDamage (pcbase.cpp:4350 @0x141676140): f32 height, the fall start z
/// minus the landing z (WriteFallDamageHeight @0x1401fc710), sent only when it is above 0.
/// </summary>
public sealed record StateFallDamageRequest(float Height) : IOutPacket {
    public Outbound Op => Outbound.StateFallDamage;
    public void Write(ByteWriter w) => w.WriteFloat(Height);
}

/// <summary>
/// Outbound Tombstone (SendOp 0x2A), WorldService::FUN_141bc3560 (worldservice_1.cpp:9370 @0x141bc3560): i32 object id, i32 hitter
/// (1 there), sent when a player enters a tombstone tag area.
/// </summary>
public sealed record TombstoneRequest(int ObjectId, int Hitter = 1) : IOutPacket {
    public Outbound Op => Outbound.Tombstone;
    public void Write(ByteWriter w) { w.WriteInt(ObjectId); w.WriteInt(Hitter); }
}

/// <summary>
/// Outbound Breakable (SendOp 0x27), WorldService::FUN_141bc3560 (worldservice_1.cpp:9312 @0x141bc3560), sent when a hittable
/// breakable's box meets a skill tag area it has not entered yet. WriteBreakableInfo (@0x1401fe0d0,
/// unit_1401f9eb0_packetpayloadserializers.cpp:1168): str the breakable's name, i64 the tag id, i32 the tag's skill id, i16 the
/// tag's skill level, u8 the cast's motion (tag +0x58) and u8 its attack index (tag +0x5c), stored as 32 bits, written as bytes.
/// The sender is Ms2.Ui's TombTagPass.RunMarkers over the own cast's tag areas.
/// </summary>
public sealed record BreakableRequest(string Name, long TagId, int SkillId, short SkillLevel, byte Motion, byte Attack) : IOutPacket {
    public Outbound Op => Outbound.Breakable;
    public void Write(ByteWriter w) {
        w.WriteString(Name ?? "");
        w.WriteLong(TagId);
        w.WriteInt(SkillId);
        w.WriteShort(SkillLevel);
        w.WriteByte(Motion);
        w.WriteByte(Attack);
    }
}
