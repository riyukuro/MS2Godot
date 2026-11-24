// Inbound 0x75 Vibrate: CSceneStateGame::OnVibrate (@0x14172a1c0, scenestategame.cpp:5981).
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound 0x75 (GMS2 0x77) Vibrate (OnVibrate, scenestategame.cpp:5981-6008): a skill cast on a named world object.
/// u8 mode, then:
/// <list type="bullet">
/// <item>0 and 1: narrow string entity name, i64 cast uid, i32 skill id, i16 level, u8 motion, u8 attack point, Vector3S position,
/// i32 serial, narrow string bone, u8 flag. The client builds a skill record from the skill, level, serial, cast uid, motion
/// and position, and hands it with the name, bone and flag to the world service.</item>
/// <item>2: narrow string entity name, narrow string model, u8 flag.</item>
/// </list>
/// Any other mode reads nothing more. The client's two branches are separate ifs, not a chain, but the modes are exclusive.
/// The wire is the same on KMS and GMS2.
/// </summary>
public sealed record Vibrate(byte Mode, string Entity, long CastUid, int SkillId, short Level, byte Motion, byte AttackPoint,
    Vector3S Position, int Serial, string Bone, string Model, byte Flag)
{
    public static Vibrate Read(ByteReader r) => Read(r, ProtocolProfile.Kms2525);

    public static Vibrate Read(ByteReader r, ProtocolProfile profile)
    {
        byte mode = r.ReadByte();
        if (mode == 0 || mode == 1)
        {
            string entity = r.ReadString();
            long castUid = r.ReadLong();
            int skillId = r.ReadInt();
            short level = r.ReadShort();
            byte motion = r.ReadByte();
            byte attackPoint = r.ReadByte();
            var position = r.ReadVec3S();
            int serial = r.ReadInt();
            string bone = r.ReadString();
            byte flag = r.ReadByte();
            r.ExpectEnd("Vibrate cast");
            return new Vibrate(mode, entity, castUid, skillId, level, motion, attackPoint, position, serial, bone, "", flag);
        }
        if (mode == 2)
        {
            string entity = r.ReadString();
            string model = r.ReadString();
            byte flag = r.ReadByte();
            r.ExpectEnd("Vibrate model");
            return new Vibrate(mode, entity, 0, 0, 0, 0, 0, default, 0, "", model, flag);
        }
        return new Vibrate(mode, "", 0, 0, 0, 0, 0, default, 0, "", "", 0);
    }
}

/// <summary>
/// Inbound 0x76 HideVibrate and 0x77 ShowVibrate (GMS2 0x78 and 0x79): OnHideVibrate (@0x14172a7b0, scenestategame.cpp:6018)
/// and OnShowVibrate (@0x14172a850, :6026) each read a narrow string, the object's name.
/// </summary>
public static class VibrateName
{
    public static string Read(ByteReader r)
    {
        string name = r.ReadString();
        r.ExpectEnd("Vibrate name");
        return name;
    }
}

/// <summary>
/// Outbound Vibrate (SendOp 0x26, GMS2 0x26), WorldService::FUN_141bc3560 (worldservice_1.cpp:9220-9230 @0x141bc3560), sent when a
/// vibrate object's box meets an own skill tag area it has not entered yet. WriteVibrateInfo (@0x1401fddd0,
/// unit_1401f9eb0_packetpayloadserializers.cpp:1149): str the object's name, i64 the tag id, i32 the tag's skill id, i16 its
/// level, u8 the cast's motion (tag +0x58), u8 its attack index (tag +0x5c), i32 the tag's +0x8c (the record's send count),
/// then the local player's position as three floats.
/// </summary>
public sealed record VibrateRequest(string Name, long TagId, int SkillId, short SkillLevel, byte Motion, byte Attack, int SendCount,
    System.Numerics.Vector3 PlayerPosition) : IOutPacket {
    public Outbound Op => Outbound.Vibrate;
    public void Write(ByteWriter w) {
        w.WriteString(Name ?? "");
        w.WriteLong(TagId);
        w.WriteInt(SkillId);
        w.WriteShort(SkillLevel);
        w.WriteByte(Motion);
        w.WriteByte(Attack);
        w.WriteInt(SendCount);
        w.WriteFloat(PlayerPosition.X); w.WriteFloat(PlayerPosition.Y); w.WriteFloat(PlayerPosition.Z);
    }
}
