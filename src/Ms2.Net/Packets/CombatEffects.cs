using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound OneTimeEffect 0xF1 (CSceneStateGame::OnOneTimeEffect @0x14173a420, scenestategame.cpp:8453): i32 the effect's id,
/// bool add; when add, a unicode string, the effect's file. The client hands the three to CTriggerService::FUN_141863630
/// (triggerservice.cpp:885), which spawns the named effect under the id or removes the id's effect.
/// </summary>
public sealed record OneTimeEffect(int Id, bool Add, string Name) {
    public static OneTimeEffect Read(ByteReader r) {
        int id = r.ReadInt();
        bool add = r.ReadBool();
        string name = add ? r.ReadUnicodeString() : "";
        r.ExpectEnd("OneTimeEffect");
        return new OneTimeEffect(id, add, name);
    }
}

/// <summary>
/// Inbound TimeScale 0xF3 (CSceneStateGame::OnTimeScale @0x14173a5e0, scenestategame.cpp:8465): bool start, float the scale at
/// the start, float the scale at the end, float the length in ms, u8 the easing curve. Start calls WorldService::StartTimeScale
/// (worldservice_1.cpp:8430), otherwise FUN_141bd9380 (:8445) ends a running change.
/// </summary>
public sealed record TimeScale(bool Start, float From, float To, float DurationMs, byte Curve) {
    public static TimeScale Read(ByteReader r) {
        var p = new TimeScale(r.ReadBool(), r.ReadFloat(), r.ReadFloat(), r.ReadFloat(), r.ReadByte());
        r.ExpectEnd("TimeScale");
        return p;
    }
}

/// <summary>
/// SendOp::StateSkill 0x21: StateSkill::UseStateSkill @0x1417eb3a0 (stateskill.cpp:2045-2070), a skill whose action kind
/// (CSkill::FUN_1407e9bd0) names a player state, sent after the own player enters that state: u8 0, i64 the cast id, i32 the
/// record's tick, i32 skill id, i16 level, i32 the state, i32 the client's elapsed time (CMainSystem::GetElapsedTime), i64 the
/// item uid (CSkillRecord::FUN_1417599c0; the glide item for state 0x1e).
/// </summary>
public sealed record StateSkillRequest(long CastId, int Tick, int SkillId, short Level, int State, int ElapsedTime, long ItemUid) : IOutPacket {
    public Outbound Op => Outbound.StateSkill;

    public void Write(ByteWriter w) {
        w.WriteByte(0);
        w.WriteLong(CastId); w.WriteInt(Tick); w.WriteInt(SkillId); w.WriteShort(Level);
        w.WriteInt(State); w.WriteInt(ElapsedTime); w.WriteLong(ItemUid);
    }
}

/// <summary>
/// SendOp::RequestAdditionalEffect 0x5C, UIAdditionalControlDialog::vfunc_28 @0x1418b2860 (uiadditionalcontroldialog.cpp:139-160).
/// OnAddEffect (only when the skill table has the effect's level): u8 0 then WriteAdditionalEffectRequest @0x1401fd400
/// (packetpayloadserializers.cpp:1037): i32 id, i32 level (kept as u16 in the request, so widened from its low 16 bits),
/// float value. OnResetEffect: u8 1 alone.
/// </summary>
public sealed record AdditionalEffectRequest(bool Reset, int Id = 0, int Level = 0, float Value = 0) : IOutPacket {
    public Outbound Op => Outbound.RequestAdditionalEffect;

    public static AdditionalEffectRequest Add(int id, int level, float value) => new(false, id, level, value);
    public static AdditionalEffectRequest ResetAll() => new(true);

    public void Write(ByteWriter w) {
        w.WriteByte(Reset ? (byte)1 : (byte)0);
        if (Reset) return;
        w.WriteInt(Id); w.WriteInt((ushort)Level); w.WriteFloat(Value);
    }
}
