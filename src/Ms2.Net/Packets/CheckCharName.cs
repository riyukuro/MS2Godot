using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>Outbound CheckCharName (UIMessageBox::HandleChangeCharNameButton, uimessagebox.cpp:2109-2112): the name, then the
/// name change item's uid.</summary>
public sealed record CheckCharNameRequest(string Name, long ItemUid) : IOutPacket {
    public Outbound Op => Outbound.CheckCharName;
    public void Write(ByteWriter w) { w.WriteUnicodeString(Name ?? ""); w.WriteLong(ItemUid); }
}

/// <summary>Inbound CheckCharNameResult (ReadCharNameResult @0x1407a5fd0, unit_1407a4f20_packetserializers.cpp:308): u8 the
/// name is used (== 1), the item uid, the name.</summary>
public sealed record CheckCharNameResult(bool Used, long ItemUid, string Name) {
    public static CheckCharNameResult Read(ByteReader r) {
        bool used = r.ReadByte() == 1;
        long uid = r.ReadLong();
        return new CheckCharNameResult(used, uid, r.ReadUnicodeString());
    }
}
