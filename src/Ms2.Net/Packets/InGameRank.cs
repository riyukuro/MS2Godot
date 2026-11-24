// Inbound 0xF0 InGameRank (KMS 0xEE): CGameRankingCli::OnPacket (gamerankingcli.cpp:350, @0x140b2ab70).
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound 0xF0 (CGameRankingCli::OnPacket): u8 mode; the client acts on mode 0x1F only (and only when feature 0x3F, InGameRank, is on):
/// i32 lifetime in seconds of a personal or daily board answer (the client's default 120), i32 lifetime in minutes of the other boards'
/// answers (default 60). Any other mode reads nothing more. Every recorded GMS2 packet is mode 0x1F with 120 and 60.
/// </summary>
public sealed record InGameRankAnswer(byte Mode, bool IsLifetimes, int PersonalSeconds, int BoardMinutes)
{
    public const byte LifetimesMode = 0x1F;

    public static InGameRankAnswer Read(ByteReader r)
    {
        byte mode = r.ReadByte();
        if (mode != LifetimesMode) return new InGameRankAnswer(mode, false, 0, 0);
        int seconds = r.ReadInt();
        int minutes = r.ReadInt();
        r.ExpectEnd("InGameRank lifetimes");
        return new InGameRankAnswer(mode, true, seconds, minutes);
    }
}
