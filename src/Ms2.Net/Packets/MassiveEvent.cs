// Layouts from the restored client's reader, checked against the server fork's writer (MassiveEventPacket) and the
// recorded KMS2 and GMS2 bodies.
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound MassiveEvent (RecvOp 0x60, commonfiles/opcodes.h:104; GMS2 0x62): CSceneStateGame::OnMassiveEvent (@0x141722f70,
/// scenestategame.cpp:3858) hands it to the massive event pane (UI 0x4e4c), whose FUN_1419f5e90 (uimassiveevent.cpp:183)
/// reads a u8 kind:
/// 0 (:218) i32 stage and three i32 for setStage;
/// 1 (:254) a unicode server string key, i32 step, i32 duration in seconds, i32 count sound type, a step message;
/// 2 (OnTypedMessagePacket, :291) u8 message type, a unicode server string key, i32 duration, a message;
/// 3 (:246) a StringIdInterface text and i32 duration, a message of type 6;
/// 4 (:265) a StringIdInterface text, i32 step, i32 duration, i32 count sound type, a step message;
/// 5 (:275) / 6 (:283) a StringIdInterface text and i32 duration, a message of type 2 / 1;
/// 7 (:228) i32 round, bool final, i32 goal, for the event introduce dialog (0x4f06);
/// 8 (:301) i32 duration, a step message of common string 0x974.
/// Any other kind reads nothing more. The client reads no further, so a body is not checked for its end.
/// </summary>
public sealed record MassiveEventMessage(byte Kind)
{
    public const byte Stage = 0, StepKey = 1, TypedKey = 2, Text = 3, StepText = 4, Win = 5, Lose = 6, Round = 7, PvpCountdown = 8;

    /// <summary>Kind 0: the stage and its three values (setStage; the second one decides m_bStage).</summary>
    public int StageNumber { get; init; }
    public int A { get; init; }
    public int B { get; init; }
    public int C { get; init; }
    /// <summary>Kinds 1 and 2: the unicode text, a server string key ("$key$") the pane looks up in CServerString's trigger group.</summary>
    public string Key { get; init; } = "";
    /// <summary>Kinds 3 to 6: the StringIdInterface text.</summary>
    public StringIdText Message { get; init; }
    /// <summary>Kind 2: the message type (ShowMessage's nSubType).</summary>
    public byte MessageType { get; init; }
    /// <summary>Kinds 1 and 4: the step number.</summary>
    public int Step { get; init; }
    /// <summary>The duration: seconds for a step message's countdown, the movie's m_duration for a message.</summary>
    public int Duration { get; init; }
    /// <summary>Kinds 1 and 4: the count sound type (m_nType: 1 the voice count and its effect, 2 the effect only).</summary>
    public int SoundType { get; init; }
    /// <summary>Kind 7: the round, whether it is the final one, and the goal.</summary>
    public int RoundNumber { get; init; }
    public bool Final { get; init; }
    public int Goal { get; init; }

    public static MassiveEventMessage Read(ByteReader r)
    {
        byte kind = r.ReadByte();
        var m = new MassiveEventMessage(kind);
        switch (kind)
        {
            case Stage:
                return m with { StageNumber = r.ReadInt(), A = r.ReadInt(), B = r.ReadInt(), C = r.ReadInt() };
            case StepKey:
                return m with { Key = r.ReadUnicodeString(), Step = r.ReadInt(), Duration = r.ReadInt(), SoundType = r.ReadInt() };
            case TypedKey:
                return m with { MessageType = r.ReadByte(), Key = r.ReadUnicodeString(), Duration = r.ReadInt() };
            case Text:
            case Win:
            case Lose:
                return m with { Message = StringIdText.Read(r), Duration = r.ReadInt() };
            case StepText:
                return m with { Message = StringIdText.Read(r), Step = r.ReadInt(), Duration = r.ReadInt(), SoundType = r.ReadInt() };
            case Round:
                return m with { RoundNumber = r.ReadInt(), Final = r.ReadBool(), Goal = r.ReadInt() };
            case PvpCountdown:
                return m with { Duration = r.ReadInt() };
            default:
                return m;
        }
    }
}
