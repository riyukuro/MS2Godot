// Inbound 0x4C RegionSkill: CSceneStateGame::OnRegionSkill (@0x141720e50, scenestategame.cpp:7718-7749).
using System.Numerics;
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound 0x4C (OnRegionSkill): u8 mode, i32 id, then by mode.
/// <list type="bullet">
/// <item>0 (<see cref="Add"/>): i32, i32, u8 count, count Vector3 (3 floats), i32, i16 level, float, float. The reader's
/// locals are named nSkillId, nTick, nOwnerId, fA, fB, and it calls Init(nId, nSkillId, kPoints, nOwnerId, nLevel, fB, fA,
/// nTick), whose parameters are (nId, nOwnerId, points, nSkillId, nLevel, fRotX, fRotY, nTick) (regionskillobject.cpp:83).
/// So on the wire: the owner, the start tick, the points, the skill id, the level, then rotY (degrees) and rotX.</item>
/// <item>1 (<see cref="Remove"/>): WorldService::RemoveRegionSkillObject(id).</item>
/// <item>2 (<see cref="StopLoop"/>): the object's StopLoopEffects.</item>
/// </list>
/// Other modes read nothing more and do nothing (null).
/// </summary>
public abstract record RegionSkillMessage(int Id)
{
    public sealed record Add(int Id, int OwnerId, int StartTick, Vector3[] Points, int SkillId, short Level, float RotY, float RotX) : RegionSkillMessage(Id);
    public sealed record Remove(int Id) : RegionSkillMessage(Id);
    public sealed record StopLoop(int Id) : RegionSkillMessage(Id);

    public static RegionSkillMessage Read(ByteReader r)
    {
        int mode = r.ReadByte(), id = r.ReadInt();
        RegionSkillMessage m;
        switch (mode)
        {
            case 0:
            {
                int owner = r.ReadInt(), tick = r.ReadInt();
                var points = new Vector3[r.ReadByte()];
                for (int i = 0; i < points.Length; i++) points[i] = r.ReadVec3();   // ReadVector3
                int skill = r.ReadInt(); short level = r.ReadShort();
                float fA = r.ReadFloat(), fB = r.ReadFloat();
                m = new Add(id, owner, tick, points, skill, level, fA, fB);
                break;
            }
            case 1: m = new Remove(id); break;
            case 2: m = new StopLoop(id); break;
            default: return null;   // the client reads no more for another mode
        }
        r.ExpectEnd($"RegionSkill mode {mode}");
        return m;
    }
}
