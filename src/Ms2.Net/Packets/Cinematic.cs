using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// Cinematic (RecvOp 0x66, GMS2 0x68): CSceneStateGame::OnCinematic (@0x141723260, scenestategame.cpp:3867) hands the body to the
/// cut scene cinematic pane (UICutSceneCinematic 0x4e52) OnPacket (@0x141908290, uicutscenecinematic.cpp:302): a u8 type.
/// Type 1 (OnPacketKeyMode @0x141909700) reads a bool, the cinematic key mode; type 2 (OnPacketClose @0x141909750) reads
/// nothing; type 3 (OnPacketStartCinematic @0x141909780) reads an i32 id and two unicode strings, the text and an unused one.
/// Types 4 to 12 are read in the client's order (the narrow string is ReadString, the wide one ReadUnicodeString, on both profiles):
/// 4 SetSkip (FUN_14190a4c0 @0x14190a4c0: u8 kind, narrow message), 5 ResetSkip (@0x14190afb0, no body), 6 Talk (FUN_141909880
/// @0x141909880: i32 npc id, narrow illustration, wide script, i32 duration, u8 flag), 7 ClearTalks (@0x141909e80, no body),
/// 8 BalloonTalk (FUN_141909eb0 @0x141909eb0: u8 raw, i32 object id, wide text, i32 duration, i32 delay), 9 ClearBalloonGroup
/// (@0x14190a400: i32 object id), 10 Caption (@0x14190a6e0: four wide strings, i32 duration, three floats), 11 Opening (@0x14190ac70:
/// wide text, bool), 12 Intro (@0x14190aee0: wide text). A type outside 1 to 12 is <see cref="Other"/>, nothing of it is read.
/// The client does not check for leftover bytes after a type, so neither does this reader.
/// </summary>
public abstract record CinematicPacket {
    public const byte KeyModeType = 1, CloseType = 2, StartType = 3, SetSkipType = 4, ResetSkipType = 5, TalkType = 6,
        ClearTalksType = 7, BalloonTalkType = 8, ClearBalloonGroupType = 9, CaptionType = 10, OpeningType = 11, IntroType = 12;

    public sealed record KeyMode(bool On) : CinematicPacket;
    public sealed record Close : CinematicPacket;
    public sealed record Start(int Id, string Text) : CinematicPacket;
    /// <summary>Type 4: the skip prompt. Kind 1 labels it the scene skip, 2 the state skip; Message is the text sent back on a skip.</summary>
    public sealed record SetSkip(byte Kind, string Message) : CinematicPacket;
    /// <summary>Type 5: clears the talks and the skip prompt.</summary>
    public sealed record ResetSkip : CinematicPacket;
    /// <summary>Type 6: one line of an NPC talk. The client's last byte is a flag (the alignment).</summary>
    public sealed record Talk(int NpcId, string Illustration, string Script, int Duration, byte Flag) : CinematicPacket;
    /// <summary>Type 7: clears the queued talks.</summary>
    public sealed record ClearTalks : CinematicPacket;
    /// <summary>Type 8: one balloon line over an object. Raw 0 means a script text, else a server text key.</summary>
    public sealed record BalloonTalk(bool Raw, int ObjectId, string Text, int Duration, int Delay) : CinematicPacket;
    /// <summary>Type 9: drops the balloon lines of one object.</summary>
    public sealed record ClearBalloonGroup(int ObjectId) : CinematicPacket;
    /// <summary>Type 10: a caption shown by the pane's movie.</summary>
    public sealed record Caption(string Type, string Text1, string Text2, string Align, int Duration, float OffsetX, float OffsetY, float Scale) : CinematicPacket;
    /// <summary>Type 11: the opening screen text; IsOpening is the client's m_bOpening.</summary>
    public sealed record Opening(string Text, bool IsOpening) : CinematicPacket;
    /// <summary>Type 12: the intro screen text.</summary>
    public sealed record Intro(string Text) : CinematicPacket;
    /// <summary>A type that is not ported; nothing of it is read.</summary>
    public sealed record Other(byte Type) : CinematicPacket;

    public static CinematicPacket Read(ByteReader r) {
        byte type = r.ReadByte();
        switch (type) {
            case KeyModeType: return new KeyMode(r.ReadBool());
            case CloseType: return new Close();
            case StartType: {
                int id = r.ReadInt();
                string text = r.ReadUnicodeString();
                r.ReadUnicodeString();      // read and dropped by the client
                return new Start(id, text);
            }
            case SetSkipType: { byte kind = r.ReadByte(); return new SetSkip(kind, r.ReadString()); }
            case ResetSkipType: return new ResetSkip();
            case TalkType: {
                int npc = r.ReadInt();
                string illust = r.ReadString();
                string script = r.ReadUnicodeString();
                int duration = r.ReadInt();
                return new Talk(npc, illust, script, duration, r.ReadByte());
            }
            case ClearTalksType: return new ClearTalks();
            case BalloonTalkType: {
                bool raw = r.ReadByte() != 0;
                int id = r.ReadInt();
                string text = r.ReadUnicodeString();
                int duration = r.ReadInt();
                return new BalloonTalk(raw, id, text, duration, r.ReadInt());
            }
            case ClearBalloonGroupType: return new ClearBalloonGroup(r.ReadInt());
            case CaptionType: {
                string t = r.ReadUnicodeString(), a = r.ReadUnicodeString(), b = r.ReadUnicodeString(), align = r.ReadUnicodeString();
                int duration = r.ReadInt();
                float x = r.ReadFloat(), y = r.ReadFloat();
                return new Caption(t, a, b, align, duration, x, y, r.ReadFloat());
            }
            case OpeningType: { string text = r.ReadUnicodeString(); return new Opening(text, r.ReadBool()); }
            case IntroType: return new Intro(r.ReadUnicodeString());
            default: return new Other(type);
        }
    }
}
