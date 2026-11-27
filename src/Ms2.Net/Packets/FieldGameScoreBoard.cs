using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound ShadowExpedition (opcode 0xE1 on the KMS2 profile, 0xE3 on GMS2), the client's FieldGameScoreBoardCli listener
/// (uishadowexpeditionbossbar.cpp, ctor @0x140f9c1e0, handler @0x140f9c300): u8 type, then
/// 0 = open (@0x140f9c3a0: u8 mode, a unicode string title (the stream's slot 8, ReadUnicodeString), i32 current, i32 most), 1 = set the current points (@0x140f9c520: i32),
/// 2 = close (@0x140f9c5b0: nothing more). Another type reads nothing (the switch has no default). The client leaves the rest of a body
/// unread, so there is no ExpectEnd. The title is looked up in the trigger string group by the receiver (CServerStringGroup::GetString).
/// </summary>
public sealed record FieldGameScoreBoardPacket(byte Type, byte Mode, string Title, int Cur, int Max)
{
    public const byte Open = 0, SetCurrent = 1, Close = 2;

    public static FieldGameScoreBoardPacket Read(ByteReader r)
    {
        byte type = r.ReadByte();
        switch (type)
        {
            case Open:
                byte mode = r.ReadByte();
                string title = r.ReadUnicodeString();
                int cur = r.ReadInt();
                int max = r.ReadInt();
                return new FieldGameScoreBoardPacket(type, mode, title, cur, max);
            case SetCurrent:
                return new FieldGameScoreBoardPacket(type, 0, "", r.ReadInt(), 0);
            default:
                return new FieldGameScoreBoardPacket(type, 0, "", 0, 0);
        }
    }
}
