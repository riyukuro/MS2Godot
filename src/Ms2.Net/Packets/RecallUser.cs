using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// RecallUser (RecvOp 0x8C, GMS2 0x8E): CSceneStateGame::OnRecallUser @0x141731c10 (scenestategame.cpp:4733), a u8 mode.
/// Mode 4 is an invitation to be recalled: u8 type (read, not used), wide name of the caller, i32 invite id. Mode 5 is the
/// caller's progress, a wide name and a u8 result; mode 6 a wide name (the one who recalls you); mode 7 a failure, a wide name
/// and a u8 result. Any other mode reads nothing, as in the client, which does not check for leftover bytes either.
/// The layouts are the client's own, so one reader serves both profiles.
/// </summary>
public abstract record RecallUserPacket {
    public const byte InviteMode = 4, ProgressMode = 5, RecalledMode = 6, FailureMode = 7;

    /// <summary>Mode 4: <paramref name="InviteId"/> is the id the answer sends back (DAT_143ab2904).</summary>
    public sealed record Invite(byte Type, string Name, int InviteId) : RecallUserPacket;
    /// <summary>Mode 5: result 0 or 2 is s_recall_user_notify_recall (0x8fb), 1 or 3 s_recall_user_notify_confirm_recall (0x8fc);
    /// another result shows nothing.</summary>
    public sealed record Progress(string Name, byte Result) : RecallUserPacket;
    /// <summary>Mode 6: s_recall_user_notify_recalled (0x8fd) with the caller's name.</summary>
    public sealed record Recalled(string Name) : RecallUserPacket;
    /// <summary>Mode 7: result 2 is 0x8ff, 4 is 0x901, 6 is 0x8fe, any other 0x900.</summary>
    public sealed record Failure(string Name, byte Result) : RecallUserPacket;
    /// <summary>A mode the client does not handle; nothing of it is read.</summary>
    public sealed record Other(byte Mode) : RecallUserPacket;

    public static RecallUserPacket Read(ByteReader r) {
        byte mode = r.ReadByte();
        switch (mode) {
            case InviteMode: { byte type = r.ReadByte(); string name = r.ReadUnicodeString(); return new Invite(type, name, r.ReadInt()); }
            case ProgressMode: { string name = r.ReadUnicodeString(); return new Progress(name, r.ReadByte()); }
            case RecalledMode: return new Recalled(r.ReadUnicodeString());
            case FailureMode: { string name = r.ReadUnicodeString(); return new Failure(name, r.ReadByte()); }
            default: return new Other(mode);
        }
    }

    /// <summary>The common string id of the chat line a mode 5 to 7 packet shows (FormatStringId(id, name) as a line of kind
    /// 0x14, FUN_1412751b0), or 0 when it shows none.</summary>
    public int NoticeStringId => this switch {
        Progress { Result: 0 or 2 } => 0x8fb,
        Progress { Result: 1 or 3 } => 0x8fc,
        Recalled => 0x8fd,
        Failure { Result: 2 } => 0x8ff,
        Failure { Result: 4 } => 0x901,
        Failure { Result: 6 } => 0x8fe,
        Failure => 0x900,
        _ => 0,
    };

    /// <summary>The name a mode 4 to 7 packet carries (a different member name, as a positional record would not make its own
    /// property over an inherited one).</summary>
    public string Who => this switch {
        Invite i => i.Name, Progress p => p.Name, Recalled c => c.Name, Failure f => f.Name, _ => "",
    };
}

/// <summary>CNetworkService::SendRecallUserReply @0x141538b10 (networkservice.cpp:522), the answer to the confirm box: u8 4, the
/// invite id, u8 accepted. The client sends nothing when no invite is pending (id 0).</summary>
public sealed record RecallUserReply(int InviteId, bool Accept) : IOutPacket {
    public Outbound Op => Outbound.RecallUser;
    public void Write(ByteWriter w) { w.WriteByte(4); w.WriteInt(InviteId); w.WriteByte((byte)(Accept ? 1 : 0)); }
}

/// <summary>CNetworkService::SendRecallUserText @0x141538bb0 (networkservice.cpp:528): u8 7, the wide name, u8 flag. OnRecallUser
/// sends it with flag 4 for a second invite while one is pending.</summary>
public sealed record RecallUserText(string Name, int Flag) : IOutPacket {
    public Outbound Op => Outbound.RecallUser;
    public void Write(ByteWriter w) { w.WriteByte(7); w.WriteUnicodeString(Name ?? ""); w.WriteByte((byte)Flag); }
}
