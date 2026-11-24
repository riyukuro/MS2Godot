using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// GlobalPortal (RecvOp 0xC1, GMS2 0xC3): CSceneStateGame::OnGlobalPortal @0x141734b20 (scenestategame.cpp:4369) hands the packet
/// to UIGlobalPortalDialog::OnGlobalPortalPacket @0x1412c3020 (uiglobalportaldialog.cpp:216), which reads a u8 mode. Mode 0
/// (FUN_1412c32a0, :252) is the event entrance notice: i32 event id, i32 param, the notice text's common string key, the system
/// sound's name and the three enter buttons' caption keys (all wide). Mode 1 (FUN_1412c3640, :268) ends the event and reads
/// nothing; mode 3 and 4 show a notice and read nothing; any other mode reads nothing. Both profiles use this layout (KMS2 and
/// GMS2 retail: modes 0 and 1 only).
/// </summary>
public abstract record GlobalPortalPacket {
    public const byte NoticeMode = 0, EndMode = 1, DeadMode = 3, LevelLimitMode = 4;

    /// <summary>Mode 0: <paramref name="TextKey"/> and the captions are common string keys; <paramref name="Sound"/> a system
    /// sound event name, empty for none.</summary>
    public sealed record Notice(int EventId, int Param, string TextKey, string Sound, string Caption1, string Caption2,
        string Caption3) : GlobalPortalPacket;
    /// <summary>Mode 1: the event is over.</summary>
    public sealed record End : GlobalPortalPacket;
    /// <summary>Mode 3, 4 or one the client has no case for: nothing more is read.</summary>
    public sealed record Other(byte Mode) : GlobalPortalPacket;

    public static GlobalPortalPacket Read(ByteReader r) {
        byte mode = r.ReadByte();
        switch (mode) {
            case NoticeMode: {
                int id = r.ReadInt(), param = r.ReadInt();
                string text = r.ReadUnicodeString(), sound = r.ReadUnicodeString();
                string c1 = r.ReadUnicodeString(), c2 = r.ReadUnicodeString(), c3 = r.ReadUnicodeString();
                return new Notice(id, param, text, sound, c1, c2, c3);
            }
            case EndMode: return new End();
            default: return new Other(mode);
        }
    }
}

/// <summary>UIGlobalPortalDialog::SendGlobalPortal @0x1412c31a0 (uiglobalportaldialog.cpp:233): an enter button's click, u8 2, the
/// event id the last notice set, i32 the button (0 to 2).</summary>
public sealed record GlobalPortalEnter(int EventId, int Button) : IOutPacket {
    public Outbound Op => Outbound.GlobalPortal;
    public void Write(ByteWriter w) { w.WriteByte(2); w.WriteInt(EventId); w.WriteInt(Button); }
}
