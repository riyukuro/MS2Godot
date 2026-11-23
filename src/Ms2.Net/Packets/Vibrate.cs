// Inbound 0x75 Vibrate: CSceneStateGame::OnVibrate (@0x14172a1c0, scenestategame.cpp:5981).
using Maple2.PacketLib.Tools;

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
