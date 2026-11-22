using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Skills;

namespace Ms2Godot.Net;

// The other players' magics (SkillDamage mode 0, RemoteTagMagics): made from the packet, started by the owner's skill state,
// ticked once per frame over the field's objects, their end effects played in the field.
public sealed partial class RemoteLayer
{
    readonly RemoteTagMagics remoteMagics = new();
    readonly List<HitCandidate> magicObjects = new();
    readonly List<MagicEndEffect> magicEndEffects = new();
    // CMagic::FUN_141464d00's RayCastFromPoint mask (magicdata.cpp:1786): groups 0 and 9
    readonly RayQuery remoteMagicRay = RayQuery.CastRay(0x201);
    MagicRayCast remoteMagicRayCast;
    static readonly Func<int> magicRand = () => Random.Shared.Next(0x8000);
    static readonly StringName PlayerGroup = Player.Group;
    Player magicPlayer;
    public int MagicsMade;
    HitEffectThrottle hitEffects = new();
    readonly List<DamageHitEffect> damageHitEffects = new();
    DamageHitFind findHitObject;
    public int DamageHitEffectsPlayed;

    /// <summary>The objects' hit effect throttle, shared with the own magics (<see cref="LiveSession.HitEffects"/>).</summary>
    public HitEffectThrottle HitEffects
    {
        get => hitEffects;
        set { hitEffects = value; remoteMagics.HitEffects = value; }
    }

    /// <summary>The remote's skill state drives its magics' cast records: the entry takes the current record, each fired attack
    /// sets its tick (FUN_1417ecce0 for an owner other than MyPC), the exit cancels and starts the rest.</summary>
    void HookMagics(Remote r)
    {
        int id = r.ObjectId;
        var st = r.Pc.States;
        st.SkillEntered = () => remoteMagics.SkillEntered(id);
        st.SkillExited = () => remoteMagics.SkillExited(id, (int)Time.GetTicksMsec());
        st.AttackFired = a => remoteMagics.AttackFired(id, a, (int)Time.GetTicksMsec());
    }

    /// <summary>
    /// CSceneStateGame::FUN_14171c1d0 (scenestategame.cpp:5511) for another player's mode 0: its skill level's attack, the
    /// objects now, then <see cref="RemoteTagMagics.OnDamage"/>. An owner not in the field is skipped (FUN_1414609e0 returns
    /// without one).
    /// STILL-OPEN: the local player's own echo is not taken (its magics are made at its attack, OwnTagMagics), and an npc owner's
    /// magics are not made.
    /// </summary>
    void OnRemoteMagics(SkillDamage d)
    {
        if (d.Mode != 0 || d.Projectiles == null || d.OwnerId == Tracker.SelfObjectId || !users.ContainsKey(d.OwnerId)) return;
        var tables = Ms2.Ui.GameTables.Shared(packs, Ms2Godot.GameOptions.StringLanguage);
        var skill = tables.Skills.GetSkillData((uint)d.SkillId);
        var motions = skill?.GetLevelData(d.Level)?.Motions;
        var attack = motions != null && d.Motion < motions.Count && d.AttackPoint < motions[d.Motion].Attacks.Count
            ? motions[d.Motion].Attacks[d.AttackPoint] : null;
        FillMagicObjects();
        MagicsMade += remoteMagics.OnDamage(d, skill, attack, tables.MagicPaths, (int)Time.GetTicksMsec(),
            remoteMagicRayCast ??= RemoteMagicRayCast, OwnerObjects(d.OwnerId));
    }

    /// <summary>One frame of every other player's magics (CMagic::FUN_14145de80 for an owner other than MyPC: no send, no
    /// registration, no hit sound), posed from the owner now; the end effects play in the field as another's
    /// (CEffectService::InsertEffect).</summary>
    void TickRemoteMagics(uint now)
    {
        if (!remoteMagics.Any) return;
        FillMagicObjects();
        remoteMagicRayCast ??= RemoteMagicRayCast;
        foreach (var (id, r) in users)
        {
            if (remoteMagics.Count(id) == 0) continue;
            var st = r.Pc.States;
            var owner = new MagicOwner(r.Pc.Position, r.Pc.RotationZ, false, st.Dead, false);
            magicEndEffects.Clear();
            remoteMagics.Tick(id, st.Cur == Ms2.Net.Replay.RemotePcStates.StateSkill, (int)now, owner, OwnerObjects(id), magicRand,
                remoteMagicRayCast, magicEndEffects);
            foreach (var fx in magicEndEffects)
                combat.PlayEffect(fx.File, new Vector3(fx.At.X, fx.At.Z, -fx.At.Y), MagicEffectBasis(fx),
                    Ms2.Audio.SoundMusicEffectState.CasterOtherPc);   // PCBase::vfunc_58 (pcbase.cpp:1092)
        }
        magicEndEffects.Clear();
    }

    /// <summary>
    /// FUN_14171cdf0's hit effects (<see cref="DamageHitEffects.Records"/>) over the field's objects now: the effectHit at its
    /// transform, the effectRemain at the target. STILL-OPEN: the remain is placed at the target's position, not attached to
    /// it (SetActor), and its end with the target is not ported.
    /// </summary>
    void OnDamageHitEffects(SkillDamage d)
    {
        if (d.Mode != 1 || d.Targets.Count == 0) return;
        var attack = AttackDamage(d.SkillId, d.Level, d.Motion, d.AttackPoint);
        if (attack == null) return;
        var tables = Ms2.Ui.GameTables.Shared(packs, Ms2Godot.GameOptions.StringLanguage);
        var motions = tables.Skills.GetSkillData((uint)d.SkillId)?.GetLevelData(d.Level)?.Motions;
        var motion = motions != null && d.Motion < motions.Count ? motions[d.Motion] : null;
        var data = motion != null && d.AttackPoint < motion.Attacks.Count ? motion.Attacks[d.AttackPoint] : null;
        FillMagicObjects();
        damageHitEffects.Clear();
        DamageHitEffects.Records(d, attack, data?.Direction ?? 3, motion?.MoveDistance ?? 0f, findHitObject ??= FindHitObject,
            hitEffects, (uint)Time.GetTicksMsec(), magicRand, damageHitEffects);
        foreach (var fx in damageHitEffects)
        {
            var at = new Vector3(fx.At.X, fx.At.Z, -fx.At.Y);
            combat.PlayEffect(fx.File, at, ClientBasis(fx.AxisX, fx.AxisY, fx.AxisZ), fx.CasterKind);
            DamageHitEffectsPlayed++;
        }
        damageHitEffects.Clear();
    }

    /// <summary>WorldService::FindObjectById over <see cref="magicObjects"/>: the position, the facing (the local player's or
    /// <see cref="PoseOf"/>), half the body box's height (vfunc_37's extent z) and FUN_14146c990's kind.
    /// STILL-OPEN: vfunc_43's height and a riding PC's ridee box are not added.</summary>
    bool FindHitObject(int id, out DamageHitObject o)
    {
        foreach (var c in magicObjects)
        {
            if (c.Id != id) continue;
            bool self = id == Tracker.SelfObjectId;
            float rot = self ? magicPlayer.FacingRotZ : PoseOf(id)?.RotZ ?? 0f;
            int kind = self ? 1 : c.Kind == HitKind.Pc ? 3 : 0;
            o = new DamageHitObject(c.Position, rot, (c.Body.MaxZ - c.Body.MinZ) * 0.5f, kind);
            return true;
        }
        o = default;
        return false;
    }

    /// <summary>The field's players and npcs and the local player last, in client space; no one is the owner yet.
    /// STILL-OPEN: the local player's buff state (pvp, hidden, invincible) and its state id are not fed here.</summary>
    void FillMagicObjects()
    {
        magicObjects.Clear();
        HitCandidates(magicObjects);
        if (magicPlayer == null || !IsInstanceValid(magicPlayer)) magicPlayer = GetTree().GetFirstNodeInGroup(PlayerGroup) as Player;
        if (magicPlayer == null) return;
        var g = magicPlayer.GlobalPosition;
        var pos = new System.Numerics.Vector3(g.X, -g.Z, g.Y);
        magicObjects.Add(new HitCandidate { Id = Tracker.SelfObjectId, Position = pos, Body = SkillHit.PlayerBody(pos, magicPlayer.FacingRotZ),
            Kind = HitKind.Pc, Team = 1, Hp = 1 });
    }

    /// <summary>The objects with <paramref name="ownerId"/>'s the <see cref="HitCandidate.IsOwner"/> one.</summary>
    ReadOnlySpan<HitCandidate> OwnerObjects(int ownerId)
    {
        var span = CollectionsMarshal.AsSpan(magicObjects);
        for (int i = 0; i < span.Length; i++) span[i].IsOwner = span[i].Id == ownerId && span[i].Kind == HitKind.Pc;
        return span;
    }

    /// <summary>CPhysXHelper::RayCastFromPoint(from, out hit, dir, length, 0x201) in client space, past the local player's
    /// body (the map's groups only, as the own magics' ray).</summary>
    bool RemoteMagicRayCast(System.Numerics.Vector3 from, System.Numerics.Vector3 dir, float length, out System.Numerics.Vector3 hit)
    {
        hit = default;
        var to = from + dir * length;
        var a = new Vector3(from.X, from.Z, -from.Y);
        var b = new Vector3(to.X, to.Z, -to.Y);
        var self = magicPlayer != null && IsInstanceValid(magicPlayer) ? magicPlayer.GetRid() : default;
        if (remoteMagicRay.Nearest(GetWorld3D().DirectSpaceState, a, b, self, 0x201) is not { } h) return false;
        hit = new System.Numerics.Vector3(h.Position.X, -h.Position.Z, h.Position.Y);
        return true;
    }

    /// <summary>An end effect's client rotation columns as a godot basis (client (x, y, z) is godot (x, z, -y)).</summary>
    static Basis MagicEffectBasis(in MagicEndEffect fx) => ClientBasis(fx.AxisX, fx.AxisY, fx.AxisZ);

    /// <summary>Client rotation columns as a godot basis.</summary>
    static Basis ClientBasis(System.Numerics.Vector3 x, System.Numerics.Vector3 y, System.Numerics.Vector3 z)
    {
        static Vector3 G(System.Numerics.Vector3 v) => new(v.X, v.Z, -v.Y);
        return new Basis(G(x), G(z), G(-y));
    }
}
