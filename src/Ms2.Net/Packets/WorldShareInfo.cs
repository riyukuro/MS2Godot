// Inbound 0xB0 (KMS) / 0xB2 (GMS2) WorldShareInfo: CSceneStateGame::OnWorldShareInfo (@0x141733b30, scenestategame.cpp:9756).
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// One SpecialNPCInfo (SpecialNPCInfo::Read, winhttpclient.cpp:1492), 19 bytes: i32 npc id, i32 field id, u16, i64, u8. The
/// client names none of the last three; the recordings carry 2, a unix time and 1.
/// </summary>
public readonly record struct SpecialNpcInfo(int NpcId, int FieldId, ushort Unk08, long Unk10, byte Unk18)
{
    public const int Size = 19;

    public static SpecialNpcInfo Read(ByteReader r) => new(r.ReadInt(), r.ReadInt(), (ushort) r.ReadShort(), r.ReadLong(), r.ReadByte());
}

/// <summary>
/// Inbound WorldShareInfo (OnWorldShareInfo, scenestategame.cpp:9756): u8 mode, then for mode 0 one <see cref="SpecialNpcInfo"/>, an
/// announced event NPC. Any other mode reads nothing more, as the client's single <c>if</c> does. The wire is the same on KMS and
/// GMS2 (every recorded packet is mode 0, 20 bytes).
/// </summary>
public sealed record WorldShareInfo(byte Mode, SpecialNpcInfo Npc)
{
    public static WorldShareInfo Read(ByteReader r)
    {
        byte mode = r.ReadByte();
        if (mode != 0) return new WorldShareInfo(mode, default);
        var npc = SpecialNpcInfo.Read(r);
        r.ExpectEnd("WorldShareInfo");
        return new WorldShareInfo(mode, npc);
    }
}
