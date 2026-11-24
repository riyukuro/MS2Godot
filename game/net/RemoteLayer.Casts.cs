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
            new System.Numerics.Vector3(u.Direction.X, u.Direction.Y, u.Direction.Z), u.HasExtra ? u.Extra : 0, u.HasExtra ? u.ExtraText ?? "" : "", u.CastUid);
    }

    /// <summary>
    /// OnSkillSync @0x141718150 (:3361): another cast than the actor's running one is started as OnSkillUse starts it (serial 0,
    /// the packet's speed); the running one takes the record rotation (m_kRot), PCBase::vfunc_22(move) and, in StateSkill,
    /// FUN_1417ec630(A, B) (<see cref="RemotePcStates.SkillSync"/>).
    /// STILL-OPEN: an npc's sync plays its sequence again even for the cast record it holds (<see cref="NpcLayer.BeginCast"/>).
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
        PlayCast(s.ActorId, s.SkillId, s.Level, s.Motion, s.Speed, new System.Numerics.Vector3(s.Position.X, s.Position.Y, s.Position.Z), s.Rotation.Z,
            new System.Numerics.Vector3(s.Direction.X, s.Direction.Y, s.Direction.Z), castUid: s.CastUid);
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
    /// with the motion index and the level's motion count that decide whether the sequence end leaves the state, and what
    /// the motion controller reads (<see cref="CastMove"/>: the motion's move data, the skill's stateAttr and magicControl,
    /// and the packet's direction <paramref name="dir"/>, the record's m_kStart).
    /// PlayCastStartEffect returns at once for a dead owner (stateskill.cpp:1821-1822, PCBase::IsDead: the user's
    /// <see cref="RemotePcStates.Dead"/>, the npc's <see cref="NpcLayer.IsDead"/>, set by CNpc::vfunc_125).
    /// A user's cast record of <paramref name="castUid"/> and the motion is the one its magics join
    /// (<see cref="Ms2.Ui.Hud.RemoteTagMagics.BeginCast"/>).
    /// </summary>
    void PlayCast(int actorId, int skillId, int level, int motionIndex, float speed, System.Numerics.Vector3 pos, float rotZ,
        System.Numerics.Vector3 dir, int holdId = 0, string holdNode = "", long castUid = 0)
    {
        skillMotions ??= Ms2.Formats.World.SkillTable.For(packs);
        var motion = skillMotions.Motion(skillId, level, motionIndex);
        if (motion == null) return;
        if (users.TryGetValue(actorId, out var r))
        {
            if (r.Pc.States.Dead) return;
            if (!string.IsNullOrEmpty(motion.SequenceName) && SkillAnim(r.Gender, motion.SequenceName) is { an: not null } sk)
            {
                var st = skillMotions.StateMove(skillId);
                var move = new CastMove(motion.MoveType, motion.KeyMove, motion.MoveDistance, motion.MoveAngle,
                    st.MoveType, st.MovingInMovekey, st.MagicControl, dir);
                // FUN_141673ce0 before the state is entered: the record list replaced unless the uid and motion are there
                remoteMagics.BeginCast(actorId, castUid, motionIndex);
                r.Pc.PlaySkill(sk.name, Math.Max(speed, 0.1f), skillId, skillMotions.ChargingSkill(skillId), pos, rotZ, holdId, holdNode,
                    motionIndex, skillMotions.MotionCount(skillId, level), motion.LoopCount, motion.AttackPoints, move, motion.TagEffects,
                    skillMotions.NotKeepEffect(skillId), EffectsOf(motion));
                CastsPlayed++;
            }
            else if (string.IsNullOrEmpty(motion.SequenceName) && !string.IsNullOrEmpty(motion.MotionEffect))
            {
                // PlayCastStartEffect for a level without a sequence (stateskill.cpp:1829-1838): InsertEffect on the caster,
                // which nothing keeps, past the sight test (:1837)
                InsertOnRemote(r, motion.MotionEffect);
            }
            return;   // a sequence the body lacks never activates, so AnimActivated plays no motion effect
        }
        else if (npcs.Info(actorId) is { } n)
        {
            // GetObjectKind of the npc (CNpc::vfunc_58): 6 for a boss, else 5
            int kind = npcs.CasterKind(actorId);
            if (npcs.IsDead(actorId)) return;
            if (!string.IsNullOrEmpty(motion.SequenceName))
            {
                string anim = npcs.PlaySequence(actorId, motion.SequenceName);
                // FUN_141673ce0 then vfunc_59(0x10): the record and the skill state the force zones read
                if (npcs.BeginCast(actorId, castUid, motionIndex, motionIndex + 1 >= skillMotions.MotionCount(skillId, level), motion, anim))
                    NpcSkillEntered(actorId, n.root, kind, skillMotions.NotKeepEffect(skillId), EffectsOf(motion), anim != null);
            }
            else if (!string.IsNullOrEmpty(motion.MotionEffect))
                ReleaseKeptEffect(SpawnKeptEffect(motion.MotionEffect, n.root, kind));   // :1829-1838
        }
    }

    /// <summary>The effect files StateSkill plays for a motion (<see cref="SkillTagEffects.MotionEffects"/>).</summary>
    public static SkillTagEffects.MotionEffects EffectsOf(Ms2.Formats.World.SkillMotion m) =>
        new(m.MotionEffect, m.LoopEffect, m.EndEffect, m.LoopKeepEffect, m.LoopEndEffect);

    /// <summary>
    /// StateSkill::FUN_1417f4bc0 (@0x1417f4bc0, stateskill.cpp:2790-2807), the test before each of the state's effects on
    /// another PC (object kind 3; the own PC and npcs are never tested): true, so nothing is played, when the field has a
    /// sight range (WorldService::GetSightRange, the field property 6) and the PC is farther than it from the local player.
    /// </summary>
    bool OutOfSight(Remote r)
    {
        if (SightRange?.Invoke() is not { } range) return false;
        if (npcs.OwnPosition?.Invoke() is not { } me) return false;
        return System.Numerics.Vector3.Distance(r.Pc.Position, me) > range;
    }

    /// <summary>An effect of another PC's skill state kept on it (FUN_1411a49c0, SetActor, object kind 3) past the sight test;
    /// its id, 0 for none.</summary>
    int KeepOnRemote(Remote r, string fx) =>
        OutOfSight(r) ? 0 : SpawnKeptEffect(fx, r.Visual, Ms2.Audio.SoundMusicEffectState.CasterOtherPc);

    /// <summary>An effect of another PC's skill state inserted on it and kept by nothing (InsertEffect, SetActor) past the
    /// sight test.</summary>
    void InsertOnRemote(Remote r, string fx)
    {
        if (!OutOfSight(r)) ReleaseKeptEffect(SpawnKeptEffect(fx, r.Visual, Ms2.Audio.SoundMusicEffectState.CasterOtherPc));
    }

    /// <summary>An npc's skill state's effects: the record's kept ids, the motion's effect files and the skill's
    /// notKeepEffect.</summary>
    sealed class NpcSkillEffects
    {
        public readonly SkillTagEffects.Handles Ids = new();
        public SkillTagEffects.MotionEffects Fx;
        public bool NotKeepEffect;
        public Node3D Root;
        public int Kind;
    }
    readonly Dictionary<int, NpcSkillEffects> npcSkillEffects = new();
    Action<int> stopKept, releaseKept;
    Action<string> insertNpcEffect;
    Node3D insertNpcRoot;
    int insertNpcKind;

    /// <summary>
    /// An npc's new record entered StateSkill (<see cref="NpcLayer.BeginCast"/>): the running record's state is left first
    /// (<see cref="EndNpcSkill"/>, the next state 0x10), then AnimActivated (stateskill.cpp:750-761) keeps the motion
    /// effect on the npc once the sequence plays (<paramref name="activated"/>: the body has it). Object kind
    /// <paramref name="kind"/>: 5, or 6 for a boss (CNpc::vfunc_58).
    /// </summary>
    void NpcSkillEntered(int id, Node3D root, int kind, bool notKeepEffect, SkillTagEffects.MotionEffects fx, bool activated)
    {
        if (npcSkillEffects.TryGetValue(id, out var e)) EndNpcSkill(id, e, 0x10);
        else npcSkillEffects[id] = e = new NpcSkillEffects();
        e.Fx = fx; e.NotKeepEffect = notKeepEffect; e.Root = root; e.Kind = kind;
        if (activated && !string.IsNullOrEmpty(fx.Motion))
            e.Ids.Motion = SpawnKeptEffect(fx.Motion, root, kind);
    }

    /// <summary>
    /// StateSkill::vfunc_5 (stateskill.cpp:515-567) for an npc leaving the state for <paramref name="next"/>: the motion
    /// effect stops at once when the npc is dead, the next state is StateHit (0xd) or StateStun (0x12, the stun test, see
    /// <see cref="RemotePcStates.ChangeState"/>) or the skill keeps no effect, else it is released; then the end effect is
    /// inserted on the npc (<see cref="SkillTagEffects.Handles.End"/>).
    /// STILL-OPEN: an npc's sequence keys are not walked here, so its loop and loop keep effects (and the tag effects) are
    /// not played.
    /// </summary>
    void EndNpcSkill(int id, NpcSkillEffects e, byte next)
    {
        stopKept ??= StopKeptEffect;
        releaseKept ??= ReleaseKeptEffect;
        insertNpcEffect ??= fx => ReleaseKeptEffect(SpawnKeptEffect(fx, insertNpcRoot, insertNpcKind));
        bool gone = e.Root == null || !GodotObject.IsInstanceValid(e.Root);
        insertNpcRoot = gone ? null : e.Root; insertNpcKind = e.Kind;
        e.Ids.End(gone || npcs.IsDead(id) || next == 0xd || next == 0x12 || e.NotKeepEffect, stopKept, releaseKept,
            gone ? null : e.Fx.End, insertNpcEffect);
        insertNpcRoot = null;
    }

    /// <summary>Each frame: an npc whose skill state was left (another state, or the npc gone) ends the state's effects
    /// (<see cref="EndNpcSkill"/>). No allocation.</summary>
    void TickNpcSkillEffects()
    {
        if (npcSkillEffects.Count == 0) return;
        foreach (var (id, e) in npcSkillEffects)
        {
            byte state = npcs.StateOf(id);
            if (state == 0x10) continue;
            EndNpcSkill(id, e, state);
            npcSkillEffects.Remove(id);
        }
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
