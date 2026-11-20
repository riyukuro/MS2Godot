using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Outbound Revival (SendOp 0x1F), what the revive dialog's buttons send (UIRevivalDialog, uirevivaldialog.cpp):
/// u8 mode, then for the paid modes u8 the coupon check box (FUN_141abc9d0, 0 / 1).
/// Mode 0 (free revive at the return point: SendRevivalMode0 @0x141abadb0, FUN_141abb600 @0x141abb600 :437): [0].
/// Mode 1 (merat: SendRevivalMode1 @0x141abaa30 :144, FUN_141abbb20 :488): [1, coupon].
/// Mode 2 (meso: FUN_141abc080 :619): [2, coupon].
/// </summary>
public sealed record RevivalRequest(byte Mode, bool Coupon = false) : IOutPacket {
    public Outbound Op => Outbound.Revival;
    public void Write(ByteWriter w) {
        w.WriteByte(Mode);
        if (Mode != 0) w.WriteByte(Coupon ? (byte)1 : (byte)0);
    }
}

/// <summary>
/// Outbound RequestMoveField (0x6C) mode 1, UIRevivalDialog::OnMessage (@0x141aba920, uirevivaldialog.cpp:100) on UI message
/// 0x7d2 (the "leave the dungeon" answer): u8 1, nothing else.
/// </summary>
public sealed record RequestMoveFieldLeave : IOutPacket {
    public Outbound Op => Outbound.RequestMoveField;
    public void Write(ByteWriter w) => w.WriteByte(1);
}
