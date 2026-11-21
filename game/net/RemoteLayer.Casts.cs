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
    /// STILL-OPEN: the pet effect (bEffect, :3343-3347), CEffectService::SetTick and the record's extra are not ported.
    /// </summary>
    void OnSkillUse(SkillUse u)
    {
        if (users.TryGetValue(u.ActorId, out var r)) r.Cast.Use(u.CastUid, u.Motion);
        PlayCast(u.ActorId, u.SkillId, u.Level, u.Motion, u.Speed);
    }

    /// <summary>
    /// OnSkillSync @0x141718150 (:3361): another cast than the actor's running one is started as OnSkillUse starts it (serial 0,
    /// the packet's speed); the running one is updated.
    /// STILL-OPEN: the update's record rotation (m_kRot), PCBase::vfunc_22(move) and StateSkill::FUN_1417ec630's charge
    /// animation are not ported (only the flags are kept); an npc has no cast record here, so each of its syncs plays.
    /// </summary>
    void OnSkillSync(SkillSync s)
    {
        CastsSynced++;
        if (users.TryGetValue(s.ActorId, out var r))
        {
            if (r.Cast.Sync(s) == CastSyncResult.Update) return;
        }
        else if (npcs.Info(s.ActorId) == null) return;
        PlayCast(s.ActorId, s.SkillId, s.Level, s.Motion, s.Speed);
    }

    /// <summary>OnSkillCancel @0x141718680 (:3409): the cast of the uid in the skill state leaves it; the movement animations take the body back.</summary>
    void OnSkillCancel(SkillCancelledEvent c)
    {
        if (!users.TryGetValue(c.ActorId, out var r) || !r.Cast.Cancel(c.CastUid)) return;
        r.CastLeft = 0; r.Pc.Release();
        CastsCancelled++;
    }

    void PlayCast(int actorId, int skillId, int level, int motionIndex, float speed)
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
                float sp = Math.Max(speed, 0.1f);
                r.Pc.Hold(sk.name, sp);
                r.CastLeft = (float)(sk.an.Length / sp);
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
                var an = ModelBuilder.BuildAnimation(builder.LoadKf(entry), body.md);
                an.LoopMode = Godot.Animation.LoopModeEnum.None;
                string name = "cast_" + low;
                if (!body.lib.HasAnimation(name)) body.lib.AddAnimation(name, an);
                sk = (an, name);
            }
        }
        catch (Exception ex) { GD.PushWarning($"skill kf {entry}: {ex.Message}"); }
        return skillAnims[(gender, seq)] = sk;
    }

    /// <summary>Per frame: true while the remote's cast motion still plays (the movement animations wait, RemotePc.Hold);
    /// its end leaves the skill state and releases the body.</summary>
    static bool CastHolds(Remote a, float dt)
    {
        if (!a.Cast.InSkillState) return false;
        a.CastLeft -= dt;
        if (a.CastLeft > 0) return true;
        a.Cast.End();
        a.Pc.Release();
        return false;
    }
}
