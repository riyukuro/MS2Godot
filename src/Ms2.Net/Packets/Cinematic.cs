using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// Cinematic (RecvOp 0x66, GMS2 0x68): CSceneStateGame::OnCinematic (@0x141723260, scenestategame.cpp:3867) hands the body to the
/// cut scene cinematic pane (UICutSceneCinematic 0x4e52) OnPacket (@0x141908290, uicutscenecinematic.cpp:302): a u8 type.
/// Type 1 (OnPacketKeyMode @0x141909700) reads a bool, the cinematic key mode; type 2 (OnPacketClose @0x141909750) reads
/// nothing; type 3 (OnPacketStartCinematic @0x141909780) reads an i32 id and two unicode strings, the text and an unused one.
/// STILL-OPEN: types 4 to 12 (talks, balloons, skip state, caption, opening, intro) are not read; they show as <see cref="Other"/>.
/// </summary>
public abstract record CinematicPacket {
    public const byte KeyModeType = 1, CloseType = 2, StartType = 3;

    public sealed record KeyMode(bool On) : CinematicPacket;
    public sealed record Close : CinematicPacket;
    public sealed record Start(int Id, string Text) : CinematicPacket;
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
            default: return new Other(type);
        }
    }
}
