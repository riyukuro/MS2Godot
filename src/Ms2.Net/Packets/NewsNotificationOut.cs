using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>Outbound NewsNotification 0x63 (CWebBrowserService::SendNewsNotification, webbrowserservice.cpp:359): u8 0, u8 flag, i32 the browser id.</summary>
public sealed record NewsNotificationRequest(bool Flag, int BrowserId) : IOutPacket {
    public Outbound Op => Outbound.NewsNotification;
    public void Write(ByteWriter w) { w.WriteByte(0); w.WriteByte((byte)(Flag ? 1 : 0)); w.WriteInt(BrowserId); }
}
