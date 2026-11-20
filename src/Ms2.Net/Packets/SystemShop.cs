// Layout from the restored client's CSceneStateGame::OnSystemShop (scenestategame.cpp:7928, RecvOp 0xA1),
// CNetworkService::SendSystemShop (networkservice.cpp:530, SendOp 0x65), and the coin-shop open writers
// (invenslot.cpp:862, uiitemgachaboxdialog.cpp:433, uimeratmarketdialog.cpp:4165).
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>Inbound 0xA1 (RecvOp::SystemShop, scenestategame.cpp:7928): u8 type, bool ok.</summary>
public sealed record SystemShopPacket(byte Type, bool Ok) {
    public static SystemShopPacket Read(ByteReader r) => new(r.ReadByte(), r.ReadBool());

    public void Write(ByteWriter w) {
        w.WriteByte(Type);
        w.WriteBool(Ok);
    }

    public byte[] WriteBytes() {
        var w = new ByteWriter();
        Write(w);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }
}

/// <summary>Outbound 0x65 (CNetworkService::SendSystemShop, networkservice.cpp:530): u8 type, u8 state (1 open, 0 close).</summary>
public sealed record SystemShopRequest(byte Type, byte State) : IOutPacket {
    public Outbound Op => Outbound.SystemShop;
    public void Write(ByteWriter w) {
        w.WriteByte(Type);
        w.WriteByte(State);
    }
}

/// <summary>Outbound 0x65 coin-shop open (invenslot.cpp:862, uiitemgachaboxdialog.cpp:433, uimeratmarketdialog.cpp:4165):
/// u8(10), u8(1), i32(itemId).</summary>
public sealed record SystemShopCoinRequest(int ItemId) : IOutPacket {
    public Outbound Op => Outbound.SystemShop;
    public void Write(ByteWriter w) {
        w.WriteByte(10);
        w.WriteByte(1);
        w.WriteInt(ItemId);
    }
}
