using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

// SendOp::Skill 0x20 (commonfiles/opcodes.h:337): the own player's skill sends, one sub-mode byte first.

/// <summary>
/// GenerateUniqueId @0x14028d0e0 (commonutil.cpp:580), the id of a new cast (Unk_InventorySkillRequest's ctor @0x141763fa0,
/// skillslot.cpp:2888): the process-wide counter g_lUniqueSeq is incremented (InterlockedIncrement, the new value), then
/// <c>"%09I64d%05d"</c> of (GetServerTime() - 1000000000, seq % 100000) is read back as a 64-bit number.
/// </summary>
public static class UniqueId
{
    static int seq;   // g_lUniqueSeq (data_bss_game.cpp:93), zero at start

    /// <summary>The next id for the server time <paramref name="serverTime"/> (seconds since 1970, CMainSystem::GetServerTime).</summary>
    public static long Next(long serverTime) => Make(serverTime, Interlocked.Increment(ref seq));

    /// <summary>
    /// The number the client's format gives: the time digits then five counter digits. A negative time prints its sign
    /// first ("%09" counts the sign), so the whole number is negative.
    /// STILL-OPEN: a negative counter (after 2^31 ids) prints "-" inside the digits, which _atoi64 stops at; not handled.
    /// </summary>
    public static long Make(long serverTime, int sequence)
    {
        long time = serverTime - 1000000000;
        long low = sequence % 100000;
        return time >= 0 ? time * 100000 + low : -(-time * 100000 + low);
    }
}

/// <summary>
/// Skill mode 0, the cast: StateSkill::FUN_1417ebc00 @0x1417ebc00 (stateskill.cpp:2079-2124) writes u8 0 then the skill use
/// payload (WriteSkillUse @0x1401fd510, unit_1401f9eb0_packetpayloadserializers.cpp:1057): i64 cast id, i32 tick (the server
/// tick taken just before the send), i32 skill id, i16 level, u8 motion, three float vectors (the record's position,
/// direction and rotation, WritePoint :173), float (the record's +0x74), i32 the client's elapsed time
/// (CMainSystem::GetElapsedTime), bool (the record's +0xae), i64 the item uid (FUN_1417599c0), i64 the cooldown key
/// (GetCooldownKey). When feature 174 (HoldAttack) is on: bool attached, and when set i32 the attached id and a string node.
/// </summary>
public sealed record SkillCastRequest(long CastId, int ServerTick, int SkillId, short Level, byte Motion, Vector3 Position,
    Vector3 Direction, Vector3 Rotation, float TargetDistance, int ElapsedTime, bool Flag, long ItemUid, long CooldownKey,
    bool HoldAttackFeature, int AttachId = 0, string AttachNode = "") : IOutPacket
{
    public Outbound Op => Outbound.Skill;

    public void Write(ByteWriter w)
    {
        w.WriteByte(0);
        w.WriteLong(CastId); w.WriteInt(ServerTick); w.WriteInt(SkillId);
        w.WriteShort(Level); w.WriteByte(Motion);
        WritePoint(w, Position); WritePoint(w, Direction); WritePoint(w, Rotation);
        w.WriteFloat(TargetDistance); w.WriteInt(ElapsedTime);
        w.WriteBool(Flag); w.WriteLong(ItemUid); w.WriteLong(CooldownKey);
        if (!HoldAttackFeature) return;
        bool attached = AttachId != 0;   // FUN_1417ebc00: m_bAttached = m_nB0 != 0
        w.WriteBool(attached);
        if (attached) { w.WriteInt(AttachId); w.WriteUnicodeString(AttachNode ?? ""); }
    }

    static void WritePoint(ByteWriter w, Vector3 v) { w.WriteFloat(v.X); w.WriteFloat(v.Y); w.WriteFloat(v.Z); }
}

/// <summary>
/// One link of a bounce chain in the damage send: the next SkillTargetRecord after a hit (the record's m_spLinked, made by
/// StateSkill::FUN_1417f3e80 @0x1417f3e80, stateskill.cpp:2779): its serial (m_nTagSub, GenerateSerialId), the object it
/// hits (m_nCasterId), the bounce number (m_n80, bounceCount - bounces left + 1) and the hit index (m_n84, 0 on a bounce).
/// </summary>
public readonly record struct SkillDamageLink(long Serial, int TargetId, byte Bounce, byte Hit);

/// <summary>
/// One target of the damage send (a SkillTargetRecord of PickAttackTargets @0x1417f3180, stateskill.cpp:2565): the serial
/// (m_nTagSub), the object it hits (m_nCasterId; 0 for a record without a target) and the hit index (m_n84), then its bounce
/// chain in order (empty when the record has no link).
/// </summary>
public sealed record SkillDamageTarget(long Serial, int TargetId, byte Hit, IReadOnlyList<SkillDamageLink> Links);

/// <summary>
/// Skill mode 1 sub-mode 0, the attack's targets: StateSkill::FUN_1417ecce0 @0x1417ecce0 (stateskill.cpp:1370-1398) writes
/// u8 1, u8 0, i64 the cast id, u8 the attack index, the record's position and the attack direction (WriteVector3, three
/// floats each), u8 the target count, i32 the record's send count (m_nSendCount, skillrecord.cpp:449), then per target i64
/// serial, i32 target id, u8 hit index, and its chain: u8 linked, and while linked i64 serial, i32 target id, u8 bounce,
/// u8 hit index, then the next u8 linked.
/// </summary>
public sealed record SkillDamageRequest(long CastId, byte Attack, Vector3 Position, Vector3 Direction, int SendCount,
    IReadOnlyList<SkillDamageTarget> Targets) : IOutPacket
{
    public Outbound Op => Outbound.Skill;

    public void Write(ByteWriter w)
    {
        w.WriteByte(1);
        w.WriteByte(0);
        w.WriteLong(CastId);
        w.WriteByte(Attack);
        WritePoint(w, Position); WritePoint(w, Direction);
        w.WriteByte((byte)Targets.Count);
        w.WriteInt(SendCount);
        for (int i = 0; i < Targets.Count; i++)
        {
            var t = Targets[i];
            w.WriteLong(t.Serial); w.WriteInt(t.TargetId); w.WriteByte(t.Hit);
            var links = t.Links;
            for (int j = 0; j < links.Count; j++)
            {
                w.WriteBool(true);
                w.WriteLong(links[j].Serial); w.WriteInt(links[j].TargetId); w.WriteByte(links[j].Bounce); w.WriteByte(links[j].Hit);
            }
            w.WriteBool(false);   // the chain's end: the do/while writes the last link's null m_spLinked too
        }
    }

    static void WritePoint(ByteWriter w, Vector3 v) { w.WriteFloat(v.X); w.WriteFloat(v.Y); w.WriteFloat(v.Z); }
}
