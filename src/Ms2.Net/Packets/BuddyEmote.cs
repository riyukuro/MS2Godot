using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound BuddyEmote (RecvOp 0x10D, commonfiles/opcodes.h:269; GMS2 0x10F), CNetworkService::OnBuddyEmote (@0x14153e280,
/// networkservice.cpp:1096-1184), the couple emotion: a u8 mode, then
/// 0 (an invite to the local player): i32 emotion, i64 the asker's character id, the asker's name;
/// 1 (the invite reached the partner): i64 the partner's character id;
/// 2 (a failure): u8 the reason (FUN_140b84010's notice);
/// 3 (the partner accepted): i32 emotion, i64 the partner's character id;
/// 4 (the partner declined): i32 emotion, i64 the partner's character id;
/// 5 (both start): i32 emotion, i64 the asker's and the partner's character ids, the start position, the end angles (both
///   ReadVector3) and an i32 for CEffectService::SetTick;
/// 6 (the partner left): i32 emotion, i64 the partner's character id.
/// Another mode reads nothing more and does nothing (the switch has no default), so it is null here. Each mode reads its
/// fields and nothing else; the handler never tests for the packet's end.
/// </summary>
public abstract record BuddyEmoteMessage
{
    public sealed record Invite(int Emotion, long CharacterId, string Name) : BuddyEmoteMessage;
    public sealed record InviteSent(long CharacterId) : BuddyEmoteMessage;
    public sealed record Failed(byte Reason) : BuddyEmoteMessage;
    public sealed record Accepted(int Emotion, long CharacterId) : BuddyEmoteMessage;
    public sealed record Declined(int Emotion, long CharacterId) : BuddyEmoteMessage;
    public sealed record Start(int Emotion, long First, long Second, Vector3 StartPosition, Vector3 EndAngles, int Tick) : BuddyEmoteMessage;
    public sealed record Left(int Emotion, long CharacterId) : BuddyEmoteMessage;

    public static BuddyEmoteMessage Read(ByteReader r)
    {
        switch (r.ReadByte())
        {
            case 0:
            {
                int emotion = r.ReadInt();
                long id = r.ReadLong();
                return new Invite(emotion, id, r.ReadUnicodeString());
            }
            case 1: return new InviteSent(r.ReadLong());
            case 2: return new Failed(r.ReadByte());
            case 3: { int emotion = r.ReadInt(); return new Accepted(emotion, r.ReadLong()); }
            case 4: { int emotion = r.ReadInt(); return new Declined(emotion, r.ReadLong()); }
            case 5:
            {
                int emotion = r.ReadInt();
                long first = r.ReadLong(), second = r.ReadLong();
                var start = r.ReadVec3();
                var end = r.ReadVec3();
                return new Start(emotion, first, second, start, end, r.ReadInt());
            }
            case 6: { int emotion = r.ReadInt(); return new Left(emotion, r.ReadLong()); }
        }
        return null;
    }
}

/// <summary>Outbound BuddyEmote (SendOp 0xA3, commonfiles/opcodes.h:466; GMS2 0xA4) mode 0, StateSkill's couple emotion
/// (main type 0xf, stateskill.cpp:1983-1987): ask the partner. Wire: u8 0, i32 emotion, i64 the partner's character id.</summary>
public sealed record BuddyEmoteInviteRequest(int Emotion, long CharacterId) : IOutPacket
{
    public Outbound Op => Outbound.BuddyEmote;
    public void Write(ByteWriter w) { w.WriteByte(0); w.WriteInt(Emotion); w.WriteLong(CharacterId); }
}

/// <summary>Outbound BuddyEmote mode 1, OnBuddyEmote's invite after its box is shown (networkservice.cpp:1127): the invite
/// arrived. Wire: u8 1, i64 the asker's character id.</summary>
public sealed record BuddyEmoteInviteReceived(long CharacterId) : IOutPacket
{
    public Outbound Op => Outbound.BuddyEmote;
    public void Write(ByteWriter w) { w.WriteByte(1); w.WriteLong(CharacterId); }
}

/// <summary>Outbound BuddyEmote mode 2, a refusal before any box (networkservice.cpp:1104-1121, statecoupleemotion.cpp:1350,
/// :1360, :1370, :1400): u8 2, i64 the other player's character id, u8 the reason (1 a couple box is up, 2 already in a
/// couple emotion, 3 the option refuses requests, 5 the state does not allow it, 7 no place found, 8 too far).</summary>
public sealed record BuddyEmoteRefuse(long CharacterId, byte Reason) : IOutPacket
{
    public Outbound Op => Outbound.BuddyEmote;
    public void Write(ByteWriter w) { w.WriteByte(2); w.WriteLong(CharacterId); w.WriteByte(Reason); }
}

/// <summary>Outbound BuddyEmote mode 3, RequestCoupleEmotion @0x140b845a0 (statecoupleemotion.cpp:1408-1415): the accept,
/// with the place FindPlacement found. Wire: u8 3, i32 emotion, i64 the asker's character id, the two positions
/// (WriteVector3) and the yaw (WriteFloat).</summary>
public sealed record BuddyEmoteAccept(int Emotion, long CharacterId, Vector3 First, Vector3 Second, float Yaw) : IOutPacket
{
    public Outbound Op => Outbound.BuddyEmote;
    public void Write(ByteWriter w)
    {
        w.WriteByte(3); w.WriteInt(Emotion); w.WriteLong(CharacterId);
        w.WriteFloat(First.X); w.WriteFloat(First.Y); w.WriteFloat(First.Z);
        w.WriteFloat(Second.X); w.WriteFloat(Second.Y); w.WriteFloat(Second.Z);
        w.WriteFloat(Yaw);
    }
}

/// <summary>Outbound BuddyEmote mode 4, the invite box's no (HandleCoupleEmotionButton @0x1419ff870, uimessagebox.cpp:2283):
/// u8 4, i32 emotion, i64 the asker's character id.</summary>
public sealed record BuddyEmoteDecline(int Emotion, long CharacterId) : IOutPacket
{
    public Outbound Op => Outbound.BuddyEmote;
    public void Write(ByteWriter w) { w.WriteByte(4); w.WriteInt(Emotion); w.WriteLong(CharacterId); }
}

/// <summary>Outbound BuddyEmote mode 6, StateCoupleEmotion::Stop @0x140b82aa0 (statecoupleemotion.cpp:924-928): the own
/// couple emotion ended. Wire: u8 6, i32 emotion, i64 the partner's character id.</summary>
public sealed record BuddyEmoteCancel(int Emotion, long CharacterId) : IOutPacket
{
    public Outbound Op => Outbound.BuddyEmote;
    public void Write(ByteWriter w) { w.WriteByte(6); w.WriteInt(Emotion); w.WriteLong(CharacterId); }
}
