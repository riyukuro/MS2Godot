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
    /// <item>0x67-0x6a, CPVPManager::OnMessage (pvpmanager.cpp:88) -> Unk_PvpTimer::OnMessage (@0x141017950,
    /// unit_141017950_pvprankinglist.cpp:143-201), the field ranking: 0x67 i64 character id, i32 score, kill, death, n1c;
    /// 0x68 i32 rank; 0x69 i32 string id, unicode name; 0x6a i64 character id, i32 damage (read as unsigned).</item>
    /// </list>
    /// Every other mode is a <see cref="PvpOther"/> with its body left unread: the arena, rank duel, team and guild modes
    /// (4, 9-0x12, 0x16-0x1f, 'r', 'u', the team tables 0x6b-0x71) are not ported. STILL-OPEN: those readers.
    /// </summary>
    public static PvpMessage Read(ByteReader r) {
        byte mode = r.ReadByte();
        PvpMessage m;
        switch (mode) {
            case (byte)'d': m = new PvpPointInfo(r.ReadInt(), r.ReadInt(), r.ReadByte(), r.ReadByte()); break;
            case (byte)'e': m = new PvpWinLose(r.ReadBool()); break;
            case (byte)'f': m = new PvpClear(); break;
            case 0x67: m = new PvpRankRow(r.ReadLong(), r.ReadInt(), r.ReadInt(), r.ReadInt(), (uint)r.ReadInt()); break;
            case 0x68: m = new PvpLastRank(r.ReadInt()); break;
            case 0x69: m = new PvpKillNotice(r.ReadInt(), r.ReadUnicodeString()); break;
            case 0x6a: m = new PvpDamage(r.ReadLong(), (uint)r.ReadInt()); break;
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

/// <summary>Pvp 0x67: a character's ranking row (Unk_PvpTimer FUN_141018260).</summary>
public sealed record PvpRankRow(long CharacterId, int Score, int Kill, int Death, uint N1c) : PvpMessage(0x67);

/// <summary>Pvp 0x68: the own last rank for the result dialog (UIPvPResultDialog::SetLastResult).</summary>
public sealed record PvpLastRank(int Rank) : PvpMessage(0x68);

/// <summary>Pvp 0x69: FormatStringId(string id, name) as a system notice of type 4 with System_KillSlayer.</summary>
public sealed record PvpKillNotice(int StringId, string Name) : PvpMessage(0x69);

/// <summary>Pvp 0x6a: a character's damage (Unk_PvpTimer FUN_141018540).</summary>
public sealed record PvpDamage(long CharacterId, uint Damage) : PvpMessage(0x6a);

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
