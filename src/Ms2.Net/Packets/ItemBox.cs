// Layouts from the restored client (CSceneStateGame::OnItemBox @0x141739e40, UIItemMultiOpenBoxDialog::vfunc_15 @0x140c3f8a0).
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound 0xAC, CSceneStateGame::OnItemBox (@0x141739e40, scenestategame.cpp:8407): i32 the box item id, i32 how many boxes were
/// opened by the request, u16 the result (2 = opened, 3 = the open failed, 4 = the inventory is full). The client looks the item's
/// data up and, when its function is OpenItemBox or SelectItemBox, hands the count and result to the multi open dialog
/// (UIItemMultiOpenBoxDialog::OnOpenResult); for another function it ends there. The packet ends with the result.
/// </summary>
public sealed record ItemBoxResult(int ItemId, int Opened, int Result) {
    public const int ResultOpened = 2, ResultFailed = 3, ResultInventoryFull = 4;

    public static ItemBoxResult Read(ByteReader r) {
        var m = new ItemBoxResult(r.ReadInt(), r.ReadInt(), (ushort) r.ReadShort());
        r.ExpectEnd("ItemBox");
        return m;
    }
}

/// <summary>
/// Outbound 0x1A (SendOp::RequestItemBox), UIItemMultiOpenBoxDialog::vfunc_15 (@0x140c3f8a0, uiitemmultiopenboxdialog.cpp:137): i32 the
/// box item id, i16 its bag slot, i32 how many to open (at most the client constant multiOpenMax), the key string the dialog
/// was opened with.
/// </summary>
public sealed record ItemBoxRequest(int ItemId, short Slot, int Count, string Key) : IOutPacket {
    public Outbound Op => Outbound.RequestItemBox;
    public void Write(ByteWriter w) { w.WriteInt(ItemId); w.WriteShort(Slot); w.WriteInt(Count); w.WriteUnicodeString(Key ?? ""); }
}
