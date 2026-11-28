using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>A Pvp message (<see cref="PvpMessage.Read"/>).</summary>
public abstract record PvpMessage(byte Mode) {
    /// <summary>
    /// Inbound Pvp (CSceneStateGame::OnPvp @0x141732350, scenestategame.cpp:4986): u8 mode.
    /// <list type="bullet">
    /// <item>'d' 0x64: i32 object id, then CPvPInfo::ReadPointTeamMode (pvpinfo.cpp:156): i32 point, u8 team, u8 mode.</item>
    /// <item>'e' 0x65: u8 win (the own PC's win or lose pose).</item>
    /// <item>'f' 0x66: CPVPManager::Clear, no body.</item>
    /// </list>
    /// Every other mode is a <see cref="PvpOther"/> with its body left unread: the arena, rank duel, team and guild modes
    /// (9-0x11, 'r', 'u', 4, CPVPManager::OnMessage) are not ported. STILL-OPEN: those readers.
    /// </summary>
    public static PvpMessage Read(ByteReader r) {
        byte mode = r.ReadByte();
        PvpMessage m;
        switch (mode) {
            case (byte)'d': m = new PvpPointInfo(r.ReadInt(), r.ReadInt(), r.ReadByte(), r.ReadByte()); break;
            case (byte)'e': m = new PvpWinLose(r.ReadBool()); break;
            case (byte)'f': m = new PvpClear(); break;
            default: return new PvpOther(mode);
        }
        r.ExpectEnd("Pvp");
        return m;
    }
}

/// <summary>Pvp 'd': a PC's PvP point, team and mode (CPvPInfo m_nPoint, m_nTeam, m_nMode).</summary>
public sealed record PvpPointInfo(int ObjectId, int Point, byte Team, byte PvpMode) : PvpMessage((byte)'d');

/// <summary>Pvp 'e': the own PC's result pose, vfunc_59(0x28, 0, win ? 0x73 : 0x74, 0).</summary>
public sealed record PvpWinLose(bool Win) : PvpMessage((byte)'e');

/// <summary>Pvp 'f': CPVPManager::Clear.</summary>
public sealed record PvpClear() : PvpMessage((byte)'f');

/// <summary>A Pvp mode whose reader is not ported (the body stays unread).</summary>
public sealed record PvpOther(byte OtherMode) : PvpMessage(OtherMode);

/// <summary>
/// Inbound PlayerKillNotice (CSceneStateGame::OnPlayerKillNotice @0x141733850, scenestategame.cpp:4344): u8 mode. Mode 0:
/// unicode killer, unicode victim (UIPvPKillLogDialog::AddKill). Mode 1: a StringIdInterface text (the system notice, type
/// 4). The client reads nothing more for another mode (the kill log dialog still opens), so that body stays unread.
/// </summary>
public sealed record PlayerKillNotice(byte Mode, string Killer, string Victim, StringIdText Text) {
    public static PlayerKillNotice Read(ByteReader r) {
        byte mode = r.ReadByte();
        PlayerKillNotice m;
        if (mode == 0) m = new PlayerKillNotice(0, r.ReadUnicodeString(), r.ReadUnicodeString(), null);
        else if (mode == 1) m = new PlayerKillNotice(1, "", "", StringIdText.Read(r));
        else return new PlayerKillNotice(mode, "", "", null);
        r.ExpectEnd("PlayerKillNotice");
        return m;
    }
}
