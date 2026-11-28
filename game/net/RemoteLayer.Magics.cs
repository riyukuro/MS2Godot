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
    // STILL-OPEN: the client's CRT rand (FUN_14109a9e0); Random.Shared gives the same range (0..32767) from another generator
    static readonly Func<int> magicRand = () => Random.Shared.Next(0x8000);
    static readonly StringName PlayerGroup = Player.Group;
    Player magicPlayer;
    System.Numerics.Vector3? magicPlayerPos;   // client space, FillMagicObjects; null without a local player
    /// <summary>The field's sight range (LiveSession.FieldProperties, field property 6), null without one.</summary>
    public Func<float?> SightRange;
    // after FillMagicObjects: IsOutsideSightRange's range and local player (worldservice_1.cpp:7956)
    FieldSight MagicSight() => new(SightRange?.Invoke(), magicPlayerPos);
    public int MagicsMade;
    // the scene effect takes its magic owner's object kind (CMagic, magicdata.cpp:927-931; MyPC 1, PCBase 3)
    readonly MagicSceneEffects remoteSceneEffects = new(Ms2.Audio.SoundMusicEffectState.CasterOtherPc),
        ownSceneEffects = new(Ms2.Audio.SoundMusicEffectState.CasterMyPc);

    /// <summary>
    /// The magics' scene effects in the field (CMagic m_nEffectId, <see cref="MagicSceneEffect"/>): a holder node per magic
    /// posed at its scene each frame, the effect kept on it by the effect service (FUN_1411a49c0 with the magic scene as the
    /// request's node: <see cref="World.RegionSkills.SpawnOn"/>), stopped with the node once the magic is gone (FUN_1411a6060).
    /// A spawn that failed is not tried again. 0 B per frame past the lists' growth.
    /// STILL-OPEN: FUN_1411a68f0(m_nEffectId, bHidden) after the spawn (the scene's app-culled flag; CEffect::vfunc_10).
    /// </summary>
    sealed class MagicSceneEffects(int casterKind)
    {
        public readonly List<MagicSceneEffect> Now = new();
        readonly List<(int Key, Node3D Node, int Handle)> shown = new();
        public int Count => shown.Count;

        public void Sync(World.RegionSkills fx)
        {
            for (int i = shown.Count - 1; i >= 0; i--)
            {
                if (IndexOf(Now, shown[i].Key) >= 0) continue;
                var (_, node, h) = shown[i];
                if (h != 0 && fx != null) fx.FadeOut(h, 0);
                if (node != null && GodotObject.IsInstanceValid(node)) node.QueueFree();
                shown.RemoveAt(i);
            }
            foreach (var e in Now)
            {
                var pose = new Transform3D(ClientBasis(e.AxisX, e.AxisY, e.AxisZ), new Vector3(e.At.X, e.At.Z, -e.At.Y));
                int k = -1;
                for (int i = 0; i < shown.Count; i++) if (shown[i].Key == e.Key) { k = i; break; }
                if (k >= 0)
                {
                    var n = shown[k].Node;
                    if (n != null && GodotObject.IsInstanceValid(n)) n.GlobalTransform = pose;
                    continue;
                }
                if (fx == null) continue;
                var node = new Node3D();
                fx.AddChild(node);
                node.GlobalTransform = pose;
                int h = fx.SpawnOn(e.File, node, casterKind);
                if (h == 0) { node.QueueFree(); node = null; }
                shown.Add((e.Key, node, h));
            }
            Now.Clear();
        }

        static int IndexOf(List<MagicSceneEffect> list, int key)
        {
            for (int i = 0; i < list.Count; i++) if (list[i].Key == key) return i;
            return -1;
        }
    }

    /// <summary>
    /// The magics' path segment effects in the field (CMagic m_slots, <see cref="MagicRaySegment"/>): per slot a holder node
    /// posed at the magic's scene each frame (the request's node) and the slot's point list
    /// (<see cref="LightningPoints"/>, the request's m_pIds) written each frame, the effect kept on it by the effect service
    /// (FUN_1411a49c0, <see cref="World.RegionSkills.SpawnOn"/> with the points), stopped with the node once the slot stops
    /// (FUN_1411a6060). A spawn that failed is not tried again while the slot plays. 0 B per frame past the lists' growth.
    /// </summary>
    sealed class MagicRayEffects(int casterKind)
    {
        public readonly List<MagicRaySegment> Now = new();
        readonly List<(long Key, Node3D Node, int Handle, LightningPoints Points)> shown = new();
        public int Count => shown.Count;
        /// <summary>The segments spawned so far (for the checks).</summary>
        public int Spawned;

        static long KeyOf(in MagicRaySegment e) => ((long)e.Key << 5) | (uint)e.Slot;

        public void Sync(World.RegionSkills fx)
        {
            for (int i = shown.Count - 1; i >= 0; i--)
            {
                if (IndexOf(Now, shown[i].Key) >= 0) continue;
                var (_, node, h, _) = shown[i];
                if (h != 0 && fx != null) fx.FadeOut(h, 0);
                if (node != null && GodotObject.IsInstanceValid(node)) node.QueueFree();
                shown.RemoveAt(i);
            }
            foreach (var e in Now)
            {
                var pose = new Transform3D(ClientBasis(e.AxisX, e.AxisY, e.AxisZ), new Vector3(e.At.X, e.At.Z, -e.At.Y));
                long key = KeyOf(e);
                int k = -1;
                for (int i = 0; i < shown.Count; i++) if (shown[i].Key == key) { k = i; break; }
                if (k >= 0)
                {
                    var (_, n, _, pts) = shown[k];
                    if (n != null && GodotObject.IsInstanceValid(n)) n.GlobalTransform = pose;
                    pts.From = e.From;
                    pts.To = e.To;
                    continue;
                }
                if (fx == null) continue;
                var points = new LightningPoints { From = e.From, To = e.To };
                var node = new Node3D();
                fx.AddChild(node);
                node.GlobalTransform = pose;
                int h = fx.SpawnOn(e.File, node, casterKind, points: points);
                if (h == 0) { node.QueueFree(); node = null; }
                else
                {
                    Spawned++;
                    if (LiveSession.LiveCheck) GD.Print($"live: ray {e.File} slot {e.Slot} kind {casterKind} from {e.From} to {e.To}");
                }
                shown.Add((key, node, h, points));
            }
            Now.Clear();
        }

        static int IndexOf(List<MagicRaySegment> list, long key)
        {
            for (int i = 0; i < list.Count; i++) if (KeyOf(list[i]) == key) return i;
            return -1;
        }
    }

    readonly MagicRayEffects remoteRayEffects = new(Ms2.Audio.SoundMusicEffectState.CasterOtherPc),
        ownRayEffects = new(Ms2.Audio.SoundMusicEffectState.CasterMyPc);
    /// <summary>The path segment effects spawned for the own and the other players' magics (for the checks).</summary>
    public int OwnRaysSpawned => ownRayEffects.Spawned;
    public int RemoteRaysSpawned => remoteRayEffects.Spawned;
    /// <summary>The path segment effects shown now (own, other players').</summary>
    public (int Own, int Remote) RaysShown => (ownRayEffects.Count, remoteRayEffects.Count);

    /// <summary>The own player's magics' scene effects and path segment effects this frame (<see cref="MagicSceneEffects"/>,
    /// <see cref="MagicRayEffects"/>), after their tick.</summary>
    public void SyncOwnSceneEffects(OwnTagMagics own)
    {
        own.SceneEffects(ownSceneEffects.Now);
        if (ownSceneEffects.Now.Count != 0 || ownSceneEffects.Count != 0) ownSceneEffects.Sync(RegionFx);
        own.RaySegments(ownRayEffects.Now);
        if (ownRayEffects.Now.Count != 0 || ownRayEffects.Count != 0) ownRayEffects.Sync(RegionFx);
    }
    HitEffectThrottle hitEffects = new();
    readonly List<DamageHitEffect> damageHitEffects = new();
    DamageHitFind findHitObject;
    DamageHitAnchors hitAnchors;   // NpcLayer.CollisionAnchors, made once
    readonly List<System.Numerics.Vector3> anchorScratch = new();
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
        if (remoteMagics.NodePosition == null) remoteMagics.NodePosition = RemoteNodePosition;
        st.SkillEntered = () => remoteMagics.SkillEntered(id);
        st.SkillExited = () => remoteMagics.SkillExited(id, (int)Time.GetTicksMsec());
        st.AttackFired = a => remoteMagics.AttackFired(id, a, (int)Time.GetTicksMsec());
        // the cast's tag effects kept on the caster (an actor target, SetActor) and ended with its skill state
        // (object kind 3, PCBase::vfunc_58), each past the sight test (FUN_1417f4bc0); its other effects too
        st.TagEffect = (_, fx) => KeepOnRemote(r, fx);
        st.KeepEffect = fx => KeepOnRemote(r, fx);
        st.InsertEffect = fx => InsertOnRemote(r, fx);
        // PlayEventVoice for the other PC: its gender and job group, not the local player (the table's rows for it)
        st.EventSound = key => FieldAudio.Instance?.PlayEventVoice(Ms2.Audio.EventSoundTable.TextKeyType(key), r.Gender,
            r.Pc.JobGroup, false, Ms2.Audio.SoundMusicEffectState.CasterOtherPc, r.Visual.GlobalPosition, npcs.Viewer?.Invoke());   // GetObjectKind 3 (effectservice.cpp:1381)
        st.StopEffect = FxTrace ? h => { GD.Print($"fx: remote stop {h} at {Time.GetTicksMsec()}"); StopKeptEffect(h); } : StopKeptEffect;
        st.ReleaseEffect = FxTrace ? h => { GD.Print($"fx: remote release {h} at {Time.GetTicksMsec()}"); ReleaseStateEffect(h); } : ReleaseStateEffect;
    }

    /// <summary>A kept effect on an actor (FUN_1411a49c0, SetActor): its id, 0 when it did not spawn. <paramref name="noCull"/>
    /// is the target's m_b30 (<see cref="RegionSkills.SpawnOn"/>).</summary>
    public int SpawnKeptEffect(string effect, Node3D actor, int casterKind, bool noCull = false) =>
        RegionFx?.SpawnOn(effect, actor, casterKind, noCull) ?? 0;
    /// <summary>FUN_1411a49c0 with no actor at a client space point (the identity rotation, scale 1), kept: its id, 0 when it
    /// did not spawn (the couple effect system's SpawnEffect).</summary>
    public int SpawnKeptEffectAt(string effect, System.Numerics.Vector3 at) =>
        RegionFx?.Spawn(effect, new Ms2.Formats.Model.Xform(new Ms2.Formats.Model.Vec3(at.X, at.Y, at.Z), Ms2.Formats.Model.Xform.Identity.R, 1), true, 0) ?? 0;
    /// <summary>CEffectService::FUN_1411a6060: a kept effect stops at once.</summary>
    public void StopKeptEffect(int id) => RegionFx?.FadeOut(id, 0);
    /// <summary>A kept effect let go of, as the client's InsertEffect keeps no id (<see cref="RegionSkills.Release"/>).</summary>
    public void ReleaseKeptEffect(int id) => RegionFx?.Release(id);
    /// <summary>CEffectService::FUN_1411a69e0 from a skill state's end (StateSkill::vfunc_5, stateskill.cpp:521-524): a kept
    /// effect released, its attach type 1 parts left in the world (<see cref="RegionSkills.ReleaseFromState"/>).</summary>
    public void ReleaseStateEffect(int id) => RegionFx?.ReleaseFromState(id);
    /// <summary>The field's region skill node the kept effect ids belong to (a new field's node numbers them again).</summary>
    public object KeptEffectScope => RegionFx;
    /// <summary>Whether a kept effect of the field's effect layer is still in (for the live checks).</summary>
    public bool IsKeptEffect(int id) => RegionFx?.IsKept(id) == true;

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
    /// (CEffectService::InsertEffect), then the magics' scene effects (<see cref="MagicSceneEffects"/>).</summary>
    void TickRemoteMagics(uint now)
    {
        TickNpcSkillEffects();
        if (!remoteMagics.Any)
        {
            if (remoteSceneEffects.Count != 0) remoteSceneEffects.Sync(RegionFx);   // the last ones went
            if (remoteRayEffects.Count != 0) remoteRayEffects.Sync(RegionFx);
            return;
        }
        FillMagicObjects();
        remoteMagicRayCast ??= RemoteMagicRayCast;
        var sight = MagicSight();
        foreach (var (id, r) in users)
        {
            if (remoteMagics.Count(id) == 0) continue;
            var st = r.Pc.States;
            var owner = new MagicOwner(r.Pc.Position, r.Pc.RotationZ, false, st.Dead, false, Sight: sight);
            magicEndEffects.Clear();
            remoteMagics.Tick(id, st.Cur == Ms2.Net.Replay.RemotePcStates.StateSkill, (int)now, owner, OwnerObjects(id), magicRand,
                remoteMagicRayCast, magicEndEffects);
            foreach (var fx in magicEndEffects)
                combat.PlayEffect(fx.File, new Vector3(fx.At.X, fx.At.Z, -fx.At.Y), MagicEffectBasis(fx),
                    Ms2.Audio.SoundMusicEffectState.CasterOtherPc);   // PCBase::vfunc_58 (pcbase.cpp:1092)
        }
        magicEndEffects.Clear();
        remoteMagics.SceneEffects(remoteSceneEffects.Now);
        if (remoteSceneEffects.Now.Count != 0 || remoteSceneEffects.Count != 0) remoteSceneEffects.Sync(RegionFx);
        remoteMagics.RaySegments(remoteRayEffects.Now);
        if (remoteRayEffects.Now.Count != 0 || remoteRayEffects.Count != 0) remoteRayEffects.Sync(RegionFx);
    }

    /// <summary>The other player's vf36_GetNodePosition (client space) for its path segment magics; the owner's position
    /// without a model, zero once it left (its magics go with it).</summary>
    System.Numerics.Vector3 RemoteNodePosition(int ownerId, string node) =>
        users.TryGetValue(ownerId, out var r) ? r.NodePosition?.Invoke(node) ?? r.Pc.Position : default;

    /// <summary>
    /// FUN_14171cdf0's hit effects (<see cref="DamageHitEffects.Records"/>) over the field's objects now: the effectHit at its
    /// transform, the effectRemain on the target (SetActor: it follows the target and goes with its node).
    /// </summary>
    void OnDamageHitEffects(SkillDamage d)
    {
        if (d.Mode is 5 or 6)
        {
            OnSplashHitEffects(d);
            return;
        }
        if (d.Mode != 1 || d.Targets.Count == 0) return;
        var attack = AttackDamage(d.SkillId, d.Level, d.Motion, d.AttackPoint);
        if (attack == null) return;
        var tables = Ms2.Ui.GameTables.Shared(packs, Ms2Godot.GameOptions.StringLanguage);
        var motions = tables.Skills.GetSkillData((uint)d.SkillId)?.GetLevelData(d.Level)?.Motions;
        var motion = motions != null && d.Motion < motions.Count ? motions[d.Motion] : null;
        var data = motion != null && d.AttackPoint < motion.Attacks.Count ? motion.Attacks[d.AttackPoint] : null;
        FillMagicObjects();
        damageHitEffects.Clear();
        DamageHitEffects.Records(d, attack, data?.ArrowRecord ?? false, data?.Direction ?? 3, motion?.MoveDistance ?? 0f, findHitObject ??= FindHitObject,
            hitEffects, (uint)Time.GetTicksMsec(), magicRand, damageHitEffects, MagicSight());
        foreach (var fx in damageHitEffects)
        {
            PlayDamageHitEffect(fx);
        }
        damageHitEffects.Clear();
    }

    /// <summary>An effect played on an object as a hit's effectRemain is (<see cref="PlayDamageHitEffect"/>); false when the
    /// object is not here. The live harness's playfx step.</summary>
    internal bool PlayEffectOnObject(string xmlPath, int objectId, int casterKind)
    {
        var node = NodeOf(objectId);
        if (node == null) return false;
        combat.PlayEffectOn(xmlPath, node, casterKind);
        return true;
    }

    /// <summary>One hit effect: on its actor when it has one (Records found that object, so a node is
    /// missing only if the layers disagree; then nothing plays), else at its transform.</summary>
    void PlayDamageHitEffect(in DamageHitEffect fx)
    {
        if (fx.ActorId != 0)
            combat.PlayEffectOn(fx.File, NodeOf(fx.ActorId), fx.CasterKind);
        else
            combat.PlayEffect(fx.File, new Vector3(fx.At.X, fx.At.Z, -fx.At.Y), ClientBasis(fx.AxisX, fx.AxisY, fx.AxisZ),
                fx.CasterKind);
        DamageHitEffectsPlayed++;
        if (FxTrace)
        {
            var place = fx.ActorId != 0 ? NodeOf(fx.ActorId)?.GlobalPosition : new Vector3(fx.At.X, fx.At.Z, -fx.At.Y);
            float away = place is { } p && EffectView.Viewer is { } eye ? System.Numerics.Vector3.Distance(EffectView.Client(p), eye) : -1;
            GD.Print($"fx: hit {fx.File} kind {fx.CasterKind} at {Time.GetTicksMsec()}, viewer {away:F0} away, {combat.PartsOutOfView} nif part(s) out of view so far");
        }
    }

    /// <summary>
    /// OnSplashDamage (mode 6) and OnRegionSkillDamage (mode 5)'s hit effects (<see cref="DamageHitEffects.SplashRecords"/>):
    /// the records' attack damage is the packet's skill at motion 0, attack 0 (mode 6), or the region skill object's skill and
    /// level at motion 0 and the packet's attack (mode 5, nothing without the object). The region's own hit effect, which
    /// mode 5 plays first, plays when the packet is read (<see cref="Ms2Godot.World.RegionSkills.PlayHit"/>).
    /// STILL-OPEN: the region is looked up when the queue is drained, so a region removed in the same read as its damage
    /// gives no per-target effectHit here; the client reads both in order.
    /// </summary>
    void OnSplashHitEffects(SkillDamage d)
    {
        if (d.Targets.Count == 0) return;
        int skillId = d.SkillId, attackPoint = 0;
        short level = d.Level;
        if (d.Mode == 5)
        {
            if (RegionSkillOf?.Invoke(d.RegionId) is not { } region) return;
            (skillId, level) = region;
            attackPoint = d.AttackPoint;
        }
        var attack = AttackDamage(skillId, level, 0, attackPoint);
        if (attack == null) return;
        FillMagicObjects();
        damageHitEffects.Clear();
        DamageHitEffects.SplashRecords(d, attack, findHitObject ??= FindHitObject, hitEffects, (uint)Time.GetTicksMsec(),
            damageHitEffects, hitAnchors ??= npcs.CollisionAnchors, anchorScratch, MagicSight());
        foreach (var fx in damageHitEffects)
        {
            PlayDamageHitEffect(fx);
        }
        damageHitEffects.Clear();
    }

    /// <summary>WorldService::FindObjectById over <see cref="magicObjects"/>: the position, the facing (the local player's or
    /// <see cref="PoseOf"/>), vfunc_37's extent (half the body box's height, an npc's collision offset turned,
    /// <see cref="DamageHitEffects.NpcExtent"/>) and the object kind (<see cref="DamageHitEffects.KindOf"/>).
    /// STILL-OPEN: vfunc_43's height and a riding PC's ridee box are not added.</summary>
    bool FindHitObject(int id, out DamageHitObject o)
    {
        foreach (var c in magicObjects)
        {
            if (c.Id != id) continue;
            bool self = id == Tracker.SelfObjectId;
            float rot = self ? magicPlayer.FacingRotZ : PoseOf(id)?.RotZ ?? 0f;
            float half = (c.Body.MaxZ - c.Body.MinZ) * 0.5f;
            var extent = c.Kind == HitKind.Npc ? DamageHitEffects.NpcExtent(c.CollisionOffset, rot, half, c.BoundOffset)
                : new System.Numerics.Vector3(0, 0, half);
            o = new DamageHitObject(c.Position, rot, extent, DamageHitEffects.KindOf(self, c.Kind, c.Boss));
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
        if (UpdateMagicPlayerPos() is not System.Numerics.Vector3 pos) return;
        var own = LiveSession.Instance?.OwnPvp ?? default;   // PC::vfunc_116 and HasPvPPoint of the own CPvPInfo
        magicObjects.Add(new HitCandidate { Id = Tracker.SelfObjectId, Position = pos, Body = SkillHit.PlayerBody(pos, magicPlayer.FacingRotZ),
            Kind = HitKind.Pc, Team = own.PcTeam, PvPPoint = own.HasPoint, Hp = 1 });
    }

    /// <summary>The local player's position in client space into <see cref="magicPlayerPos"/>; null without a local player.</summary>
    System.Numerics.Vector3? UpdateMagicPlayerPos()
    {
        magicPlayerPos = null;
        if (magicPlayer == null || !IsInstanceValid(magicPlayer)) magicPlayer = GetTree().GetFirstNodeInGroup(PlayerGroup) as Player;
        if (magicPlayer == null) return null;
        var g = magicPlayer.GlobalPosition;
        magicPlayerPos = new System.Numerics.Vector3(g.X, -g.Z, g.Y);
        return magicPlayerPos;
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
