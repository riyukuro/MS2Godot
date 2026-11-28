using Godot;
using Ms2.Ui.Skills;

namespace Ms2Godot.Net;

// The own player's skill sends (SendOp Skill 0x20).
public partial class LiveSession
{
    /// <summary>Harness counts of the own skill sends and the server's refusals (the scripted combat sweep reads them per
    /// cast): casts, mode 1 target sends (attack, region hit, splash) and SkillUseFailed packets.</summary>
    public int CastsSent { get; private set; }
    public int DamagesSent { get; private set; }
    public int HitsSent { get; private set; }
    public int SplashesSent { get; private set; }
    public int SkillUseFailures { get; private set; }

    /// <summary>
    /// The send of an own cast (StateSkill::FUN_1417ea570, stateskill.cpp:1807, <see cref="SkillCast.Use"/>): a state skill's
    /// SendOp StateSkill 0x21, else the mode 0 send (FUN_1417ebc00, :2079) with the server tick taken at the send; both with the
    /// client's elapsed time (CMainSystem::GetElapsedTime); feature 174 (HoldAttack) adds the attached target. False offline.
    /// </summary>
    public bool SendCast(CastRecord r, bool holdAttackFeature)
    {
        if (game == null) return false;
        // UseStateSkill kind 0x26 (stateskill.cpp:2009-2021): MyPC's class takes UseRideeItem for the record's item
        if (!r.Skill.IsEmotion && r.Skill.ActionKind == 0x26 && r.ItemUid != 0) { UseRideeItem(r.ItemUid); return true; }
        if (SkillCast.Use(r, game.Session.ServerTick, game.Session.ElapsedMs, holdAttackFeature) is { } pk) { Send(pk); CastsSent++; }
        return true;
    }

    /// <summary>SendOp RequestAdditionalEffect 0x5C of the GM additional-effect dialog (uiadditionalcontroldialog.cpp:139-160).
    /// STILL-OPEN: the dialog (UIAdditionalControlDialog) and its AdditionalLevelData check have no port; nothing calls this yet.
    /// False offline.</summary>
    public bool SendAdditionalEffect(Ms2.Net.Packets.AdditionalEffectRequest req)
    {
        if (game == null) return false;
        Send(req);
        return true;
    }

    /// <summary>The mode 4 send of the running own cast's id when the skill state is left for the walk or the jump
    /// (StateSkill::SendSkillMode4 @0x1417f4e10, stateskill.cpp:1691; the swing cancel :1553, :1558). False offline.</summary>
    public bool SendSkillStop(long castId)
    {
        if (game == null) return false;
        Send(new Ms2.Net.Packets.SkillStopRequest(castId));
        return true;
    }

    /// <summary>The mode 1 sub-mode 0 send of an own attack's targets (StateSkill::FUN_1417ecce0, stateskill.cpp:1370). False offline.</summary>
    public bool SendDamage(Ms2.Net.Packets.SkillDamageRequest req)
    {
        if (game == null) return false;
        Send(req); DamagesSent++;
        return true;
    }

    /// <summary>The mode 1 sub-mode 1 send of an own magic's region hits (CMagic::FUN_141464d00, magicdata.cpp:1830). False offline.</summary>
    public bool SendHit(Ms2.Net.Packets.SkillHitRequest req)
    {
        if (game == null) return false;
        Send(req); HitsSent++;
        return true;
    }

    /// <summary>The mode 1 sub-mode 2 send of an own magic's splash (CMagic::FUN_14145f570, magicdata.cpp:1156): every own
    /// magic's end and a piercing arrow's intervals (OwnTagMagics, sent from HudScene.TickOwnTagMagics). False offline.</summary>
    public bool SendSplash(Ms2.Net.Packets.SkillSplashRequest req)
    {
        if (game == null) return false;
        Send(req); SplashesSent++;
        return true;
    }

    /// <summary>The mode 2 sync of the own skill state (StateSkill::SendChargeState, stateskill.cpp:1271). False offline.</summary>
    public bool SendSync(Ms2.Net.Packets.SkillSyncRequest req)
    {
        if (game == null) return false;
        Send(req);
        return true;
    }

    /// <summary>A continue round's mode 3 (CSkillRecord::SendSkill, skillrecord.cpp:441) at the server tick. False offline.</summary>
    public bool SendRound(CastRecord r)
    {
        if (game == null) return false;
        Send(SkillCast.SendSkill(r, game.Session.ServerTick));
        return true;
    }

    /// <summary>The field's other players and npcs an own attack can hit (cleared first); empty before the field is built.</summary>
    public void HitCandidates(System.Collections.Generic.List<HitCandidate> into)
    {
        into.Clear();
        remote?.HitCandidates(into);
    }

    /// <summary>Another player of the field as a hit candidate (<see cref="RemoteLayer.TryPcCandidate"/>); false for any
    /// other id and before the field is built.</summary>
    public bool TryPcCandidate(int id, out HitCandidate c)
    {
        c = default;
        return remote != null && remote.TryPcCandidate(id, out c);
    }

    /// <summary>The cast id of a new own cast: GenerateUniqueId over the server time (offline the local clock).</summary>
    public static long NewCastId() =>
        Ms2.Net.Packets.UniqueId.Next(Instance?.GameSession?.ServerTime ?? System.DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    /// <summary>The server tick a new record takes (CMainSystem::GetServerTick); offline the elapsed time.</summary>
    public static int CastTick() => Instance?.GameSession?.ServerTick ?? (int)Time.GetTicksMsec();

    /// <summary>A skill motion's effect in the field, through the combat layer the other players' casts use;
    /// <paramref name="casterKind"/> is the effect's object relation (FUN_141bd19d0: 1 the own PC).</summary>
    public void PlayFieldEffect(string effect, Vector3 at, float yawRadians, int casterKind)
        => remote?.PlayEffect(effect, at, yawRadians, casterKind);

    /// <summary>The own magics' scene effects (an arrow's "effect" while it flies) after their tick, in the field.</summary>
    public void SyncOwnMagicEffects(Ms2.Ui.Hud.OwnTagMagics own) => remote?.SyncOwnSceneEffects(own);

    /// <summary>WorldService::GetObjectKind of an object id (worldservice_1.cpp:6296-6309): MyPC::vfunc_58 1 for the own PC
    /// (mypc.cpp:1247), PCBase::vfunc_58 3 for another (pcbase.cpp:1092), an npc's 5 or 6 for a boss; 0 for no object.</summary>
    public int ObjectKind(int objectId) =>
        objectId != 0 && objectId == SelfObjectId ? Ms2.Audio.SoundMusicEffectState.CasterMyPc : remote?.ObjectKind(objectId) ?? 0;

    /// <summary>A kept effect on <paramref name="actor"/> (FUN_1411a49c0, SetActor) in the field's region skill node: its id,
    /// 0 when it did not spawn.</summary>
    public int SpawnKeptEffect(string effect, Node3D actor, int casterKind) => remote?.SpawnKeptEffect(effect, actor, casterKind) ?? 0;
    /// <summary>A kept effect stopped at once (CEffectService::FUN_1411a6060).</summary>
    public void StopKeptEffect(int id) => remote?.StopKeptEffect(id);
    /// <summary>Whether a kept effect is still in (for the live checks).</summary>
    public bool IsKeptEffect(int id) => remote?.IsKeptEffect(id) == true;

    /// <summary>The buff lists' effect service through the field of the moment (none between fields).</summary>
    public Ms2.Ui.Skills.IBuffMotionEffects BuffEffects => buffEffects ??= new FieldBuffEffects(this);
    FieldBuffEffects buffEffects;

    sealed class FieldBuffEffects : Ms2.Ui.Skills.IBuffMotionEffects
    {
        readonly LiveSession s;
        public FieldBuffEffects(LiveSession s) => this.s = s;
        public int Keep(string effect, int ownerId, int casterId) => s.remote?.BuffEffects.Keep(effect, ownerId, casterId) ?? 0;
        public void Insert(string effect, int ownerId, int casterId) => s.remote?.BuffEffects.Insert(effect, ownerId, casterId);
        public void Stop(int id) => s.remote?.BuffEffects.Stop(id);
        public bool OwnerShown(int ownerId) => s.remote?.BuffEffects.OwnerShown(ownerId) == true;
    }
    /// <summary>A kept effect let go of, as an insert (<see cref="RemoteLayer.ReleaseKeptEffect"/>).</summary>
    public void ReleaseKeptEffect(int id) => remote?.ReleaseKeptEffect(id);
    /// <summary>A skill state's kept effect released (<see cref="RemoteLayer.ReleaseStateEffect"/>).</summary>
    public void ReleaseStateEffect(int id) => remote?.ReleaseStateEffect(id);

    /// <summary>The same turned by a full rotation (godot space), as an own magic's end effect.</summary>
    public void PlayFieldEffect(string effect, Vector3 at, Basis rotation, int casterKind)
        => remote?.PlayEffect(effect, at, rotation, casterKind);

    /// <summary>
    /// A CommonResource effect on the own PC (CEffectService::InsertEffect with the actor set and kind 1, the own PC):
    /// <paramref name="key"/> is the commonResource.xml key. Played once at the body's place and facing.
    /// STILL-OPEN: the effect is not attached to the actor (it stays where it started) and a kept one
    /// (StateDead's m_nEffect) is not removed early by its owner.
    /// </summary>
    void PlayOwnCommonEffect(string key)
    {
        if (player == null || !IsInstanceValid(player)) return;
        string path;
        try { path = Ms2.Ui.GameTables.Shared(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), GameOptions.StringLanguage).CommonResource(key); }
        catch (Exception ex) { GD.PushWarning($"common effect {key}: {ex.Message}"); return; }
        PlayOwnEffect(path);
    }

    /// <summary>An effect xml at the own PC as <see cref="PlayOwnCommonEffect"/> plays it; nothing for an empty path.</summary>
    void PlayOwnEffect(string path)
    {
        if (string.IsNullOrEmpty(path) || player == null || !IsInstanceValid(player)) return;
        OwnEffectsPlayed?.Invoke(path);
        remote?.PlayEffect(path, player.GlobalPosition, player.Visual?.Rotation.Y ?? 0, Ms2.Audio.SoundMusicEffectState.CasterMyPc);
    }

    /// <summary>The effect xml of each own CommonResource effect played (for the checks).</summary>
    public Action<string> OwnEffectsPlayed;

    /// <summary>The skill rows the cooldowns look up (SkillDataTableCli); the HUD sets it once its tables are read.</summary>
    public System.Func<uint, SkillData> SkillRows;
    SkillCooltimes cooltimes;
    /// <summary>The own player's CSkillSystem cooldowns and charge uses, fed by the cooldown packets.</summary>
    public SkillCooltimes Cooltimes => cooltimes ??= new SkillCooltimes(id => SkillRows?.Invoke(id));

    /// <summary>
    /// The own state's cancel (State::vfunc_7(1)): the cast uid to match (0 for any) and the state id the player must be in
    /// (0x10, the skill state, for OnSkillCancel and OnSkillUseFailed mode 5; mode 6's own state id).
    /// </summary>
    public event System.Action<long, int> OwnStateCancelled;

    /// <summary>OnSkillSync @0x141718150 (scenestategame.cpp:3361) for the own player: the HUD tests it against the own cast.</summary>
    public event System.Action<Ms2.Net.Packets.SkillSync> OwnSkillSync;

    /// <summary>The own player's skill packets; false when the packet is not one of them.</summary>
    bool HandleOwnSkill(Ms2.Net.Wire.RawPacket p)
    {
        if (game.Is(p, Ms2.Net.Inbound.SkillUseFailed))
        {
            // OnSkillUseFailed @0x1417187b0 (scenestategame.cpp:3425): only with MyPC; mode 5 cancels the own skill state of the
            // uid, mode 6 the own state of the id; then the cooldown and the uses
            var f = Ms2.Net.Packets.SkillUseFailed.Read(p.Reader());
            if (selfObject < 0) return true;
            if (f.Mode == 5) OwnStateCancelled?.Invoke(f.SkillUid, 0x10);
            else if (f.Mode == 6) OwnStateCancelled?.Invoke(0, f.State);
            Cooltimes.Apply(f);
            GD.Print($"live: skill use failed, skill {f.SkillId} error {f.Error} mode {f.Mode}");
            SkillUseFailures++;
        }
        else if (game.Is(p, Ms2.Net.Inbound.SkillCooldown))
        {
            // OnSkillCooldown @0x14172f030 (scenestategame.cpp:7856), only with MyPC
            var c = Ms2.Net.Packets.SkillCooldown.Read(p.Reader());
            if (selfObject >= 0) Cooltimes.Apply(c, (uint)game.Session.ServerTick);
        }
        else if (game.Is(p, Ms2.Net.Inbound.SkillResetCooldown))
        {
            // OnSkillResetCooldown @0x14172f1c0 (scenestategame.cpp:4231): FUN_141765de0, only with MyPC
            if (selfObject >= 0) Cooltimes.ResetAll();
        }
        else if (game.Is(p, Ms2.Net.Inbound.SkillCancel))
        {
            // OnSkillCancel @0x141718680 (scenestategame.cpp:3409): the actor's skill state of the uid is cancelled; the other
            // players' go through the field tracker, the own one here
            var c = Ms2.Net.Packets.SkillCancel.Read(p.Reader());
            if (c.ActorId == selfObject) OwnStateCancelled?.Invoke(c.CastUid, 0x10);
            return false;
        }
        else if (game.Is(p, Ms2.Net.Inbound.SkillSync))
        {
            // the other actors' syncs go through the field tracker, the own one to the HUD's cast
            var s = Ms2.Net.Packets.SkillSync.Read(p.Reader());
            if (selfObject >= 0 && s.ActorId == selfObject) OwnSkillSync?.Invoke(s);
            return false;
        }
        else if (game.Is(p, Ms2.Net.Inbound.StateSkill))
        {
            // OnStateSkill @0x141718b80 (:3476): type 6 calls the PC's current state's vfunc_33(true), which only StateHit
            // overrides (statehit.cpp:350, its m_bC8). STILL-OPEN: no player here has a hit state, so it is logged only.
            var st = Ms2.Net.Packets.StateSkill.Read(p.Reader(), game.Profile);
            if (LogPackets && st.Type == 6 && selfObject >= 0 && st.ActorId == selfObject) GD.Print($"live: state skill 6, skill {st.SkillId} level {st.Level}");
            return false;
        }
        else return false;
        return true;
    }
}
