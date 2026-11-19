using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

// The trophy dialog's sends (UIAchieveDialog, client/ms2/uiachievedialog.cpp): SendOp::Achieve 0x46 by mode, and
// SendOp::RequestUserEnv 0x69 when the dialog loads.

/// <summary>UIAchieveDialog::vfunc_28 "onReward" (uiachievedialog.cpp:255): take a trophy's reward (u8 3, i32 trophy id).</summary>
public sealed record AchieveRewardRequest(int Id) : IOutPacket {
    public Outbound Op => Outbound.Achieve;
    public void Write(ByteWriter w) { w.WriteByte(3); w.WriteInt(Id); }
}

/// <summary>UIAchieveDialog::vfunc_28 "onBookmark" (uiachievedialog.cpp:350): mark or unmark a trophy (u8 4, i32 trophy id,
/// u8 the mark).</summary>
public sealed record AchieveBookmarkRequest(int Id, bool Bookmark) : IOutPacket {
    public Outbound Op => Outbound.Achieve;
    public void Write(ByteWriter w) { w.WriteByte(4); w.WriteInt(Id); w.WriteByte(Bookmark ? (byte)1 : (byte)0); }
}

/// <summary>UIAchieveDialog::vfunc_28 "onDbgUp" / "onDbgDown" / "onDbgUp30" / "onDbgUp99" (uiachievedialog.cpp:279-325),
/// the GM build's progress buttons: u8 5, i32 trophy id, u8 mode (up 0, down 1, up 30 2, up 99 3).</summary>
public sealed record AchieveDebugRequest(int Id, byte Mode) : IOutPacket {
    public Outbound Op => Outbound.Achieve;
    public void Write(ByteWriter w) { w.WriteByte(5); w.WriteInt(Id); w.WriteByte(Mode); }
}

/// <summary>UIAchieveDialog::OnLoadComplete (uiachievedialog.cpp:218): RequestUserEnv u8 3.</summary>
public sealed record UserEnvRequest : IOutPacket {
    public Outbound Op => Outbound.RequestUserEnv;
    public void Write(ByteWriter w) => w.WriteByte(3);
}
