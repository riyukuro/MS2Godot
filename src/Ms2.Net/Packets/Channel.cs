using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Outbound Channel (0x3B): the request to change to another channel, WorldService::FUN_141bc80c0 @0x141bc80c0
/// (worldservice_1.cpp:4936) and the message box answer OnMessage @0x141BC8230 (worldservice_1.cpp:10620). The payload is
/// one i16 written by FUN_1401fcc00 (unit_1401f9eb0_packetpayloadserializers.cpp:893, WriteInt16 of the channel).
/// </summary>
public sealed record ChannelRequest(short Channel) : IOutPacket {
    public Outbound Op => Outbound.Channel;
    public void Write(ByteWriter w) => w.WriteShort(Channel);
}

/// <summary>
/// Inbound DynamicChannel (0xA7), CNetworkService::OnDynamicChannel @0x14153c4b0 (networkservice.cpp:977): u8 kind. Kind 1:
/// u16 count, count x u16 channel (the server list dialog's SetChannels). Kind 0: eight u16 (the per class table kept in
/// DAT_143add400, read by WorldService::FUN_141bd9870). Another kind reads nothing and changes nothing.
/// </summary>
public abstract record DynamicChannelMessage {
    /// <summary>Kind 1: the channels the current server offers, in the order sent.</summary>
    public sealed record Channels(ushort[] Tabs) : DynamicChannelMessage;
    /// <summary>Kind 0: the eight values of the per class table.</summary>
    public sealed record ClassTable(ushort[] Values) : DynamicChannelMessage;

    public static DynamicChannelMessage Read(ByteReader r) {
        switch (r.ReadByte()) {
            case 1: {
                int n = (ushort) r.ReadShort();
                var tabs = new ushort[n];
                for (int i = 0; i < n; i++) tabs[i] = (ushort) r.ReadShort();
                return new Channels(tabs);
            }
            case 0: {
                var v = new ushort[8];
                for (int i = 0; i < 8; i++) v[i] = (ushort) r.ReadShort();
                return new ClassTable(v);
            }
            default: return null;
        }
    }
}
