using System;
using System.Collections.Generic;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Replay;

namespace Ms2Godot.Net;

// The other actors' casts from the field's skill packets, played live: the cast's motion sequence on the body and its motion
// effect through the CombatLayer (the replay's OnSkillUse, ReplayField.cs).
public sealed partial class RemoteLayer
{
    Ms2.Formats.World.SkillTable skillMotions;
    readonly Dictionary<(byte, string), (Godot.Animation an, string name)> skillAnims = new();
    public int CastsPlayed, CastsSynced, CastsCancelled;

    /// <summary>
    /// OnSkillUse @0x141717b40 (scenestategame.cpp:3297): the actor's new record of the uid and motion (CSkillRecord::Init,
    /// FUN_141673b40 -> StateSkill::PlayCastStartEffect plays its motion at the record's speed).
    /// The record's extra (an actor id and its node) is StateSkill's m_nB0 / m_strB8: the cast's end holds that actor
    /// (StateHold, 0x20) instead of going to StateIdle.
    /// STILL-OPEN: the pet effect (bEffect, :3343-3347) and CEffectService::SetTick are not ported.
    /// </summary>
    void OnSkillUse(SkillUse u)
    {
        if (users.TryGetValue(u.ActorId, out var r)) r.Cast.Use(u.CastUid, u.Motion);
        PlayCast(u.ActorId, u.SkillId, u.Level, u.Motion, u.Speed, new System.Numerics.Vector3(u.Position.X, u.Position.Y, u.Position.Z), u.Rotation.Z,
            u.HasExtra ? u.Extra : 0, u.HasExtra ? u.ExtraText ?? "" : "");
    }

    /// <summary>
    /// OnSkillSync @0x141718150 (:3361): another cast than the actor's running one is started as OnSkillUse starts it (serial 0,
    /// the packet's speed); the running one takes the record rotation (m_kRot), PCBase::vfunc_22(move) and, in StateSkill,
    /// FUN_1417ec630(A, B) (<see cref="RemotePcStates.SkillSync"/>).
    /// STILL-OPEN: an npc has no cast record here, so each of its syncs plays.
    /// </summary>
    void OnSkillSync(SkillSync s)
    {
        CastsSynced++;
        if (users.TryGetValue(s.ActorId, out var r))
        {
            InSkill(r);
            if (r.Cast.Sync(s) == CastSyncResult.Update)
            {
                r.Pc.States.SkillSync(s.Rotation.Z, new System.Numerics.Vector2(s.Move.X, s.Move.Y), s.A, s.B);
                return;
            }
        }
        else if (npcs.Info(s.ActorId) == null) return;
        PlayCast(s.ActorId, s.SkillId, s.Level, s.Motion, s.Speed, new System.Numerics.Vector3(s.Position.X, s.Position.Y, s.Position.Z), s.Rotation.Z);
    }

    /// <summary>OnSkillCancel @0x141718680 (:3409): the cast of the uid in the skill state leaves it for StateIdle
    /// (State::vfunc_7(1)), whose sequence takes the body back.</summary>
    void OnSkillCancel(SkillCancelledEvent c)
    {
        if (!users.TryGetValue(c.ActorId, out var r)) return;
        InSkill(r);
        if (!r.Cast.Cancel(c.CastUid)) return;
        r.Pc.States.CancelSkill();
        CastsCancelled++;
    }

    /// <summary>The packets' GetCurStateId() == 0x10: the remote's state machine, which leaves StateSkill by itself too.</summary>
    static void InSkill(Remote r) => r.Cast.InSkillState = r.Pc.States.Cur == RemotePcStates.StateSkill;

    /// <summary>
    /// FUN_141673b40 -> StateSkill::PlayCastStartEffect for a user: a motion with a sequence the body has enters StateSkill
    /// (RemotePc.PlaySkill) at the record's position <paramref name="pos"/> (MS2 axes) and rotation <paramref name="rotZ"/>,
    /// with the motion index and the level's motion count that decide whether the sequence end leaves the state.
    /// STILL-OPEN: PlayCastStartEffect returns at once for a dead owner (stateskill.cpp:1822, PCBase::IsDead); a remote
    /// PC's death is not tracked here, so a dead PC's cast still plays.
    /// </summary>
    void PlayCast(int actorId, int skillId, int level, int motionIndex, float speed, System.Numerics.Vector3 pos, float rotZ,
        int holdId = 0, string holdNode = "")
    {
        skillMotions ??= new Ms2.Formats.World.SkillTable(packs);
        var motion = skillMotions.Motion(skillId, level, motionIndex);
        if (motion == null) return;
        Node3D at; float yaw;
        if (users.TryGetValue(actorId, out var r))
        {
            at = r.Root; yaw = r.Visual.Rotation.Y;
            if (!string.IsNullOrEmpty(motion.SequenceName) && SkillAnim(r.Gender, motion.SequenceName) is { an: not null } sk)
            {
                r.Pc.PlaySkill(sk.name, Math.Max(speed, 0.1f), skillId, skillMotions.ChargingSkill(skillId), pos, rotZ, holdId, holdNode,
                    motionIndex, skillMotions.MotionCount(skillId, level), motion.LoopCount, motion.AttackPoints);
                CastsPlayed++;
            }
        }
        else if (npcs.Info(actorId) is { } n)
        {
            at = n.root; yaw = n.yaw;
            if (!string.IsNullOrEmpty(motion.SequenceName)) npcs.PlaySequence(actorId, motion.SequenceName);
        }
        else return;
        if (!string.IsNullOrEmpty(motion.MotionEffect)) combat.PlayEffect(motion.MotionEffect, at.Position, yaw);
    }

    /// <summary>
    /// The body's &lt;sequence&gt;.kf as a one-shot animation (the replay's SkillAnim), cached per gender and name and added
    /// once, under its own name, to the body's library that every PC of the gender shares.
    /// </summary>
    (Godot.Animation an, string name) SkillAnim(byte gender, string seq)
    {
        if (skillAnims.TryGetValue((gender, seq), out var sk)) return sk;
        sk = default;
        string low = seq.ToLowerInvariant(), entry = $"{BodyDir(gender)}/{low}.kf";
        try
        {
            if (bodies.TryGetValue(gender, out var body) && packs.Exists(ModelBuilder.ModelArchive, entry))
            {
                var kf = builder.LoadKf(entry);
                var an = ModelBuilder.BuildAnimation(kf, body.md);
                an.LoopMode = Godot.Animation.LoopModeEnum.None;
                string name = "cast_" + low;
                if (!body.lib.HasAnimation(name)) body.lib.AddAnimation(name, an);
                if (body.anims != null && !body.anims.Kf.ContainsKey(name)) body.anims.AddSequenceKeys(name, low, kf);
                sk = (an, name);
            }
        }
        catch (Exception ex) { GD.PushWarning($"skill kf {entry}: {ex.Message}"); }
        return skillAnims[(gender, seq)] = sk;
    }
}
